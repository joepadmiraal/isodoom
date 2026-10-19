using System;
using System.Collections.Generic;

namespace IsoDoom.Net;

/// <summary>
/// How a <see cref="MemoryTransport"/> endpoint's link to the network behaves.
/// A packet between two endpoints takes both endpoints' <see cref="LatencyUs"/>
/// plus a random 0–<see cref="JitterUs"/> of each, plus, while the endpoint
/// is in a burst (T11.12e: wifi power saving, another device on the air), a
/// random <see cref="SpikeMinUs"/>–<see cref="SpikeMaxUs"/> of each.
/// </summary>
public sealed record MemoryLinkOptions
{
    /// <summary>A perfect link: no latency, jitter or loss.</summary>
    public static MemoryLinkOptions Perfect { get; } = new();

    /// <summary>One-way latency this endpoint adds to every packet, in microseconds.</summary>
    public long LatencyUs { get; init; }

    /// <summary>Extra random one-way delay, uniform in 0–this, in microseconds.</summary>
    public long JitterUs { get; init; }

    /// <summary>Chance (0–1) that this endpoint loses an unreliable packet (sent or received).</summary>
    public double UnreliableLoss { get; init; }

    /// <summary>
    /// Chance (0–1) that a reliable packet to or from this endpoint is lost on the wire and resent:
    /// it then arrives <see cref="ResendUs"/> later, and so does every reliable
    /// packet behind it (ENet's head-of-line blocking).
    /// </summary>
    public double ReliableLoss { get; init; }

    /// <summary>
    /// How much later a resent reliable packet arrives, in microseconds (ENet:
    /// its retransmit timeout, the smoothed round trip + 4 × its variance).
    /// </summary>
    public long ResendUs { get; init; } = 50_000;

    /// <summary>
    /// T11.12e: the mean time between the starts of two bursts of latency
    /// spikes, in microseconds (each gap uniform in half to one and a half of
    /// it); 0 for none. The bursts come on the endpoint's own schedule, drawn
    /// from its seed apart from the packets' randomness, so they fall at the
    /// same network times whatever the traffic.
    /// </summary>
    public long BurstEveryUs { get; init; }

    /// <summary>T11.12e: a burst's mean length, in microseconds (each uniform in half to one and a half of it).</summary>
    public long BurstLengthUs { get; init; }

    /// <summary>T11.12e: the least extra one-way delay of a packet sent during a burst, in microseconds.</summary>
    public long SpikeMinUs { get; init; }

    /// <summary>T11.12e: the most extra one-way delay of a packet sent during a burst, in microseconds.</summary>
    public long SpikeMaxUs { get; init; }

    /// <summary>
    /// T11.12c: chance (0–1) that a packet sent to or from this endpoint
    /// during one of its bursts is lost: a loss burst (with no spikes, or on
    /// top of them). An unreliable one is gone; a reliable one is resent every
    /// <see cref="ResendUs"/> until a resend goes after the burst, and arrives
    /// its latency after that, holding back those behind it.
    /// </summary>
    public double BurstLoss { get; init; }
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
    private readonly Random _random;
    private readonly Random _bursts;
    private long _burstStartUs = -1;
    private long _burstEndUs = -1;
    private readonly Dictionary<int, Link> _links = [];
    private readonly PriorityQueue<NetEvent, (long DueUs, long Sequence)> _inbox = new();
    private int _nextPeer = 1;
    private bool _disposed;

    internal MemoryTransport(MemoryNetwork network, string address, MemoryLinkOptions options, int seed)
    {
        _network = network;
        Address = address;
        Options = options;
        _random = new Random(seed);
        _bursts = new Random(unchecked((seed * 0x2C1B3C6D) ^ 0x5bd1e995));
    }

    /// <summary>
    /// How this endpoint's link behaves; a test may change it mid-game
    /// (T11.12e: a link that gets worse), and the packets sent from then on
    /// follow it (a burst schedule already drawn keeps its current burst).
    /// </summary>
    public MemoryLinkOptions Options { get; set; }

    /// <summary>This endpoint's address.</summary>
    public string Address { get; }

    /// <summary>Bytes sent on the reliable channel.</summary>
    public long ReliableBytesSent { get; private set; }

    /// <summary>Bytes sent on the unreliable channel (lost ones included).</summary>
    public long UnreliableBytesSent { get; private set; }

    /// <summary>Packets sent on either channel.</summary>
    public long PacketsSent { get; private set; }

    /// <summary>T11.12c: packets sent on the unreliable channel (lost ones included).</summary>
    public long UnreliablePacketsSent { get; private set; }

    /// <summary>Bytes received on either channel.</summary>
    public long BytesReceived { get; private set; }

    /// <summary>
    /// T11.11: until when (network time, µs) this endpoint's link is down:
    /// a packet sent to or from it meanwhile is lost on the unreliable
    /// channel, and on the reliable one arrives once the link is back (ENet's
    /// resends), its latency after; packets already on the way still arrive.
    /// A loss burst, or a long stall with <see cref="Block"/>.
    /// </summary>
    public long BlockedUntilUs { get; private set; }

    /// <summary>T11.11: the link goes down for <paramref name="us"/> microseconds from now (<see cref="BlockedUntilUs"/>).</summary>
    public void Block(long us)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(us);
        BlockedUntilUs = Math.Max(BlockedUntilUs, _network.NowUs + us);
    }

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
        long blocked = Math.Max(BlockedUntilUs, link.Remote.BlockedUntilUs);
        // T11.12c: a loss burst at either end (drawn only when an end has one, so other links' runs are unchanged)
        long burstLost = Math.Max(BurstLost(this), BurstLost(link.Remote));
        if (channel == NetChannel.Unreliable)
        {
            UnreliableBytesSent += data.Length;
            UnreliablePacketsSent++;
            if (blocked > _network.NowUs || burstLost > 0)
                return;
            if (_random.NextDouble() < Options.UnreliableLoss || link.Remote._random.NextDouble() < link.Remote.Options.UnreliableLoss)
                return;
        }
        else
        {
            ReliableBytesSent += data.Length;
            if (blocked > _network.NowUs)
                due = Math.Max(due, blocked + Delay(link.Remote));
            if (burstLost > 0)
            {
                // resent every ResendUs (ENet's retransmit timeout; ENet doubles it, so this is the kind case) until one goes after the burst
                long resends = (burstLost - _network.NowUs + Options.ResendUs - 1) / Options.ResendUs;
                due = Math.Max(due, _network.NowUs + (resends * Options.ResendUs) + Delay(link.Remote));
            }
            // T11.12e: either end's link loses it (as the unreliable channel), each loss a resend later
            while (_random.NextDouble() < Options.ReliableLoss)
                due += Options.ResendUs;
            MemoryLinkOptions there = link.Remote.Options;
            if (there.ReliableLoss > 0)
            {
                while (link.Remote._random.NextDouble() < there.ReliableLoss)
                    due += there.ResendUs;
            }
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
        long delay = Options.LatencyUs + Jitter(this) + Spike(this);
        if (remote is not null)
            delay += remote.Options.LatencyUs + Jitter(remote) + Spike(remote);
        return delay;
    }

    /// <summary>T11.12e: whether this endpoint is in a burst of spikes now (its schedule drawn up to now).</summary>
    public bool InBurst
    {
        get
        {
            MemoryLinkOptions o = Options;
            if (o.BurstEveryUs <= 0 || o.BurstLengthUs <= 0)
                return false;
            long now = _network.NowUs;
            while (_burstEndUs <= now)
            {
                long from = Math.Max(_burstEndUs, 0);
                _burstStartUs = from + Spread(_bursts, o.BurstEveryUs);
                _burstEndUs = _burstStartUs + Math.Max(1, Spread(_bursts, o.BurstLengthUs));
            }
            return now >= _burstStartUs;
        }
    }

    /// <summary>
    /// T11.12c: when <paramref name="endpoint"/>'s burst ends if a packet sent
    /// now is lost in it (<see cref="MemoryLinkOptions.BurstLoss"/>), else 0.
    /// </summary>
    private static long BurstLost(MemoryTransport endpoint)
    {
        double loss = endpoint.Options.BurstLoss;
        if (loss <= 0 || !endpoint.InBurst || endpoint._random.NextDouble() >= loss)
            return 0;
        return endpoint._burstEndUs;
    }

    /// <summary>Uniform in half to one and a half of <paramref name="meanUs"/>.</summary>
    private static long Spread(Random random, long meanUs) => (meanUs / 2) + random.NextInt64(meanUs + 1);

    private static long Spike(MemoryTransport endpoint)
    {
        if (!endpoint.InBurst)
            return 0;
        MemoryLinkOptions o = endpoint.Options;
        return o.SpikeMinUs + (o.SpikeMaxUs > o.SpikeMinUs ? endpoint._random.NextInt64(o.SpikeMaxUs - o.SpikeMinUs + 1) : 0);
    }

    private static long Jitter(MemoryTransport endpoint)
        => endpoint.Options.JitterUs > 0 ? endpoint._random.NextInt64(endpoint.Options.JitterUs + 1) : 0;

    private void Deliver(MemoryTransport to, NetEvent netEvent, long dueUs)
        => to._inbox.Enqueue(netEvent, (dueUs, _network.NextSequence()));
}
