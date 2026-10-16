using System;
using System.Collections.Generic;

namespace IsoDoom.Net;

/// <summary>
/// How a <see cref="MemoryTransport"/> endpoint's link to the network behaves.
/// A packet between two endpoints takes both endpoints' <see cref="LatencyUs"/>
/// plus a random 0–<see cref="JitterUs"/> of each.
/// </summary>
public sealed record MemoryLinkOptions
{
    /// <summary>A perfect link: no latency, jitter or loss.</summary>
    public static MemoryLinkOptions Perfect { get; } = new();

    /// <summary>One-way latency this endpoint adds to every packet, in microseconds.</summary>
    public long LatencyUs { get; init; }

    /// <summary>Extra random one-way delay, uniform in 0–this, in microseconds.</summary>
    public long JitterUs { get; init; }

    /// <summary>Chance (0–1) that this endpoint loses an unreliable packet.</summary>
    public double UnreliableLoss { get; init; }

    /// <summary>
    /// Chance (0–1) that a reliable packet is lost on the wire and resent:
    /// it then arrives <see cref="ResendUs"/> later, and so does every reliable
    /// packet behind it (ENet's head-of-line blocking).
    /// </summary>
    public double ReliableLoss { get; init; }

    /// <summary>How much later a resent reliable packet arrives, in microseconds (ENet: about a round trip).</summary>
    public long ResendUs { get; init; } = 50_000;
}

/// <summary>
/// An in-memory network for tests (T11.5): endpoints made by
/// <see cref="CreateEndpoint"/> connect to each other by address and port,
/// and their packets arrive after the latency and jitter of both endpoints'
/// <see cref="MemoryLinkOptions"/>. Time is the test's: <see cref="NowUs"/>,
/// moved by <see cref="Advance"/>. Seeded, so a run repeats exactly.
/// </summary>
public sealed class MemoryNetwork
{
    private readonly Dictionary<(string Address, int Port), MemoryTransport> _listeners = [];
    private long _sequence;

    /// <summary>The network's time, in microseconds.</summary>
    public long NowUs { get; private set; }

    /// <summary>Move the network's time on by <paramref name="us"/> microseconds.</summary>
    public void Advance(long us)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(us);
        NowUs += us;
    }

    /// <summary>A new endpoint at <paramref name="address"/>, its link behaving as <paramref name="options"/>.</summary>
    public MemoryTransport CreateEndpoint(string address, MemoryLinkOptions? options = null, int seed = 0)
        => new(this, address, options ?? MemoryLinkOptions.Perfect, seed);

    internal long NextSequence() => _sequence++;

    internal void Register(MemoryTransport listener, int port)
    {
        if (!_listeners.TryAdd((listener.Address, port), listener))
            throw new InvalidOperationException($"{listener.Address}:{port} is already listening.");
    }

    internal void Unregister(MemoryTransport listener)
    {
        foreach (var key in new List<(string, int)>(_listeners.Keys))
        {
            if (_listeners[key] == listener)
                _listeners.Remove(key);
        }
    }

    internal MemoryTransport? Listener(string address, int port) => _listeners.GetValueOrDefault((address, port));
}

/// <summary>
/// An <see cref="INetTransport"/> endpoint of a <see cref="MemoryNetwork"/>.
/// The reliable channel keeps each link's order (a packet never overtakes an
/// earlier one; a resent one holds back those behind it); the unreliable
/// channel loses and reorders packets as the options say. Counts what it sends.
/// </summary>
public sealed class MemoryTransport : INetTransport
{
    private readonly MemoryNetwork _network;
    private readonly MemoryLinkOptions _options;
    private readonly Random _random;
    private readonly Dictionary<int, Link> _links = [];
    private readonly PriorityQueue<NetEvent, (long DueUs, long Sequence)> _inbox = new();
    private int _nextPeer = 1;
    private bool _disposed;

    internal MemoryTransport(MemoryNetwork network, string address, MemoryLinkOptions options, int seed)
    {
        _network = network;
        Address = address;
        _options = options;
        _random = new Random(seed);
    }

    /// <summary>This endpoint's address.</summary>
    public string Address { get; }

    /// <summary>Bytes sent on the reliable channel.</summary>
    public long ReliableBytesSent { get; private set; }

    /// <summary>Bytes sent on the unreliable channel (lost ones included).</summary>
    public long UnreliableBytesSent { get; private set; }

    /// <summary>Packets sent on either channel.</summary>
    public long PacketsSent { get; private set; }

    /// <summary>Bytes received on either channel.</summary>
    public long BytesReceived { get; private set; }

    private sealed class Link(MemoryTransport remote)
    {
        public MemoryTransport Remote { get; } = remote;
        public int RemotePeer { get; set; }
        /// <summary>When the last reliable packet (or control event) on this link arrives: the next one cannot arrive earlier.</summary>
        public long LastReliableDueUs { get; set; }
    }

    /// <inheritdoc/>
    public void Listen(int port)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _network.Register(this, port);
    }

    /// <inheritdoc/>
    public int Connect(string address, int port)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int peer = _nextPeer++;
        MemoryTransport? remote = _network.Listener(address, port);
        if (remote is null)
        {
            Deliver(this, new NetEvent(NetEventKind.Disconnected, peer, default, NetChannel.Reliable), _network.NowUs + Delay(remote: null));
            return peer;
        }
        int remotePeer = remote._nextPeer++;
        var mine = new Link(remote) { RemotePeer = remotePeer };
        var theirs = new Link(this) { RemotePeer = peer };
        _links[peer] = mine;
        remote._links[remotePeer] = theirs;
        // The handshake: the listener hears of it after one trip, this side after the round trip.
        long oneWay = Delay(remote);
        long there = _network.NowUs + oneWay;
        long back = there + remote.Delay(this);
        mine.LastReliableDueUs = back;
        theirs.LastReliableDueUs = back;
        Deliver(remote, new NetEvent(NetEventKind.Connected, remotePeer, default, NetChannel.Reliable), there);
        Deliver(this, new NetEvent(NetEventKind.Connected, peer, default, NetChannel.Reliable), back);
        return peer;
    }

    /// <inheritdoc/>
    public void Send(int peer, ReadOnlySpan<byte> data, NetChannel channel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_links.TryGetValue(peer, out Link? link))
            return;
        PacketsSent++;
        long due = _network.NowUs + Delay(link.Remote);
        if (channel == NetChannel.Unreliable)
        {
            UnreliableBytesSent += data.Length;
            if (_random.NextDouble() < _options.UnreliableLoss || link.Remote._random.NextDouble() < link.Remote._options.UnreliableLoss)
                return;
        }
        else
        {
            ReliableBytesSent += data.Length;
            while (_random.NextDouble() < _options.ReliableLoss)
                due += _options.ResendUs;
            due = Math.Max(due, link.LastReliableDueUs);
            link.LastReliableDueUs = due;
        }
        Deliver(link.Remote, new NetEvent(NetEventKind.Data, link.RemotePeer, data.ToArray(), channel), due);
    }

    /// <inheritdoc/>
    public void Disconnect(int peer)
    {
        if (!_links.Remove(peer, out Link? link))
            return;
        long due = Math.Max(_network.NowUs + Delay(link.Remote), link.LastReliableDueUs);
        link.Remote.Drop(link.RemotePeer, due);
        Deliver(this, new NetEvent(NetEventKind.Disconnected, peer, default, NetChannel.Reliable), _network.NowUs);
    }

    private void Drop(int peer, long dueUs)
    {
        // The remote side's link goes when the disconnect arrives; what it sends meanwhile is lost.
        if (_links.Remove(peer))
            Deliver(this, new NetEvent(NetEventKind.Disconnected, peer, default, NetChannel.Reliable), dueUs);
    }

    /// <inheritdoc/>
    public bool Poll(out NetEvent netEvent)
    {
        if (_inbox.TryPeek(out NetEvent next, out var priority) && priority.DueUs <= _network.NowUs)
        {
            _inbox.Dequeue();
            if (next.Kind == NetEventKind.Data)
                BytesReceived += next.Data.Length;
            netEvent = next;
            return true;
        }
        netEvent = default;
        return false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        foreach (int peer in new List<int>(_links.Keys))
            Disconnect(peer);
        _network.Unregister(this);
        _disposed = true;
    }

    private long Delay(MemoryTransport? remote)
    {
        long delay = _options.LatencyUs + Jitter(this);
        if (remote is not null)
            delay += remote._options.LatencyUs + Jitter(remote);
        return delay;
    }

    private static long Jitter(MemoryTransport endpoint)
        => endpoint._options.JitterUs > 0 ? endpoint._random.NextInt64(endpoint._options.JitterUs + 1) : 0;

    private void Deliver(MemoryTransport to, NetEvent netEvent, long dueUs)
        => to._inbox.Enqueue(netEvent, (dueUs, _network.NextSequence()));
}
