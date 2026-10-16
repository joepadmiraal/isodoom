using System;
using System.Collections.Generic;
using Godot;
using IsoDoom.Net;

namespace IsoDoom.Game;

/// <summary>
/// T11.6: <see cref="INetTransport"/> over Godot's ENet
/// (<see cref="ENetConnection"/>, <see cref="ENetPacketPeer"/>; UDP), two
/// channels: <see cref="NetChannel.Reliable"/> (ENet's reliable, ordered
/// channel 0) and <see cref="NetChannel.Unreliable"/> (channel 1, unreliable
/// sequenced). Polled from the main thread, once a frame through the
/// <see cref="NetServer"/>'s and <see cref="NetClient"/>'s <c>Update</c>
/// (SPEC §12 T11.6: no thread of its own). A send is flushed at once, so a
/// command goes out in the frame it is made, not at the next poll.
/// <para>
/// Godot lifetimes (CLAUDE.md invariants, SPEC §12 T8.3d): every packet is
/// copied out to a <c>byte[]</c> when its event is polled; the event array
/// <see cref="ENetConnection.Service"/> returns and each <see cref="Variant"/>
/// read from it are disposed; the peers' wrappers are held in
/// <see cref="_peers"/> while connected and disposed on the main thread
/// when they go; the connection is destroyed and disposed with the transport.
/// </para>
/// </summary>
public sealed class NetEnetTransport : INetTransport, INetPortTransport
{
    /// <summary>The ENet channels: one per <see cref="NetChannel"/>.</summary>
    public const int Channels = 2;

    private readonly ENetConnection _connection = new();
    private readonly Dictionary<int, ENetPacketPeer> _peers = [];
    private readonly Dictionary<ulong, int> _ids = [];
    private readonly Queue<NetEvent> _events = new();
    private readonly int _maxPeers;
    private bool _created;
    private int _nextPeer = 1;
    private bool _disposed;

    /// <summary>A transport that accepts up to <paramref name="maxPeers"/> peers when listening.</summary>
    public NetEnetTransport(int maxPeers = NetProtocol.MaxPlayers)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPeers, 1);
        _maxPeers = maxPeers;
    }

    /// <summary>
    /// The address <see cref="Listen"/> binds (<c>*</c>, every interface, by
    /// default; <c>127.0.0.1</c> for loopback only). Set before listening.
    /// </summary>
    public string BindAddress { get; init; } = "*";

    /// <summary>The port listened on (the one ENet picked for <see cref="Listen"/>(0)), or 0.</summary>
    public int LocalPort { get; private set; }

    /// <summary>A line for each transport error, for the log.</summary>
    public event Action<string>? Log;

    /// <inheritdoc/>
    public void Listen(int port)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_created)
            throw new InvalidOperationException("The ENet host exists already: listen before connecting.");
        Error error = _connection.CreateHostBound(BindAddress, port, _maxPeers, Channels);
        if (error != Error.Ok)
            throw new InvalidOperationException($"ENet: cannot listen on {BindAddress}:{port} ({error})");
        _created = true;
        LocalPort = _connection.GetLocalPort();
    }

    /// <inheritdoc/>
    public int Connect(string address, int port)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_created)
        {
            Error error = _connection.CreateHost(1, Channels);
            if (error != Error.Ok)
                throw new InvalidOperationException($"ENet: cannot create a host ({error})");
            _created = true;
        }
        int id = _nextPeer++;
        ENetPacketPeer? peer = _connection.ConnectToHost(address, port, Channels);
        if (peer is null)
        {
            Log?.Invoke($"ENet: cannot connect to {address}:{port}");
            _events.Enqueue(new NetEvent(NetEventKind.Disconnected, id, default, NetChannel.Reliable));
            return id;
        }
        Hold(id, peer);
        _connection.Flush();
        return id;
    }

    /// <inheritdoc/>
    public void Send(int peer, ReadOnlySpan<byte> data, NetChannel channel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_peers.TryGetValue(peer, out ENetPacketPeer? p))
            return;
        int flags = channel == NetChannel.Reliable ? (int)ENetPacketPeer.FlagReliable : 0;
        Error error = p.Send((int)channel, data, flags);
        if (error != Error.Ok)
            Log?.Invoke($"ENet: send to peer {peer} failed ({error})");
        _connection.Flush();
    }

    /// <inheritdoc/>
    public void Disconnect(int peer)
    {
        if (_disposed || !_peers.TryGetValue(peer, out ENetPacketPeer? p))
            return;
        p.PeerDisconnectLater(); // after the reliable packets already queued; the Disconnect event follows
        _connection.Flush();
    }

    /// <inheritdoc/>
    public bool Poll(out NetEvent netEvent)
    {
        if (_events.Count == 0 && _created && !_disposed)
            Service();
        return _events.TryDequeue(out netEvent);
    }

    /// <summary>Every event ENet has now, as <see cref="NetEvent"/>s (packets copied out).</summary>
    private void Service()
    {
        while (true)
        {
            using Godot.Collections.Array result = _connection.Service(0);
            using Variant typeVariant = result[0];
            var type = (ENetConnection.EventType)typeVariant.AsInt32();
            if (type is ENetConnection.EventType.None)
                return;
            if (type is ENetConnection.EventType.Error)
            {
                Log?.Invoke("ENet: service error");
                return;
            }
            using Variant peerVariant = result[1];
            using Variant channelVariant = result[3];
            ENetPacketPeer? peer = peerVariant.As<ENetPacketPeer>();
            if (peer is null)
                continue;
            int id = Identify(peer, type == ENetConnection.EventType.Connect);
            switch (type)
            {
                case ENetConnection.EventType.Connect:
                    if (id > 0)
                        _events.Enqueue(new NetEvent(NetEventKind.Connected, id, default, NetChannel.Reliable));
                    break;
                case ENetConnection.EventType.Receive:
                    {
                        byte[] packet = peer.GetPacket(); // the packet queued on the peer by this event, copied out
                        if (id > 0)
                        {
                            var channel = channelVariant.AsInt32() == (int)NetChannel.Unreliable ? NetChannel.Unreliable : NetChannel.Reliable;
                            _events.Enqueue(new NetEvent(NetEventKind.Data, id, packet, channel));
                        }
                        break;
                    }
                case ENetConnection.EventType.Disconnect:
                    if (id > 0)
                    {
                        Release(id);
                        _events.Enqueue(new NetEvent(NetEventKind.Disconnected, id, default, NetChannel.Reliable));
                    }
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>
    /// The id of <paramref name="peer"/> (a new one held for a new connection
    /// when <paramref name="connect"/>), or 0 for a peer this transport no
    /// longer knows. A second wrapper of a peer already held is disposed.
    /// </summary>
    private int Identify(ENetPacketPeer peer, bool connect)
    {
        ulong instance = peer.GetInstanceId();
        if (_ids.TryGetValue(instance, out int id))
        {
            if (!ReferenceEquals(_peers[id], peer))
                peer.Dispose();
            return id;
        }
        if (!connect)
        {
            peer.Dispose();
            return 0;
        }
        id = _nextPeer++;
        Hold(id, peer);
        return id;
    }

    private void Hold(int id, ENetPacketPeer peer)
    {
        _peers[id] = peer;
        _ids[peer.GetInstanceId()] = id;
    }

    private void Release(int id)
    {
        if (!_peers.Remove(id, out ENetPacketPeer? peer))
            return;
        _ids.Remove(peer.GetInstanceId());
        peer.Dispose();
    }

    /// <summary>Disconnects every peer (sent at once), then destroys the ENet host.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_created)
        {
            foreach (ENetPacketPeer peer in _peers.Values)
                peer.PeerDisconnect();
            _connection.Flush();
            _connection.Destroy();
        }
        foreach (ENetPacketPeer peer in _peers.Values)
            peer.Dispose();
        _peers.Clear();
        _ids.Clear();
        _connection.Dispose();
    }
}
