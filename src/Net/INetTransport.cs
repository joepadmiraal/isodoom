using System;

namespace IsoDoom.Net;

/// <summary>The two channels every transport offers (T11.5, SPEC §12 "T11.5 lockstep protocol").</summary>
public enum NetChannel : byte
{
    /// <summary>
    /// Reliable and ordered: every packet arrives, once, in the order sent
    /// (ENet's reliable channel resends until acknowledged). The lobby, the
    /// Start, the checksum reports, the quits and the desync use it.
    /// </summary>
    Reliable = 0,

    /// <summary>
    /// Unreliable: a packet may be lost, or arrive out of order. The pings,
    /// and (T11.12c) the commands and bundles, which carry every tic not
    /// acknowledged yet, use it.
    /// </summary>
    Unreliable = 1,
}

/// <summary>What a <see cref="NetEvent"/> reports.</summary>
public enum NetEventKind : byte
{
    /// <summary>A peer connected: to a listener, a new peer; to a connecting side, the connection is up.</summary>
    Connected,

    /// <summary>A packet arrived from the peer (<see cref="NetEvent.Data"/>).</summary>
    Data,

    /// <summary>The peer disconnected, or was disconnected; it sends and receives nothing more.</summary>
    Disconnected,
}

/// <summary>One event polled from an <see cref="INetTransport"/>.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Peer">The transport's id of the peer it concerns.</param>
/// <param name="Data">For <see cref="NetEventKind.Data"/>, the packet: a copy the receiver owns; else empty.</param>
/// <param name="Channel">For <see cref="NetEventKind.Data"/>, the channel it came on.</param>
public readonly record struct NetEvent(NetEventKind Kind, int Peer, ReadOnlyMemory<byte> Data, NetChannel Channel);

/// <summary>
/// The packet transport under <see cref="NetServer"/> and <see cref="NetClient"/>
/// (T11.5): a host listens, the others connect; each side sends bytes to a
/// peer on a <see cref="NetChannel"/> and polls events. Implementations:
/// <see cref="MemoryTransport"/> (tests: latency, jitter, loss) and, in the
/// Godot project, Godot's ENet (T11.6). Single-threaded: the caller polls it
/// from the thread that sends.
/// </summary>
public interface INetTransport : IDisposable
{
    /// <summary>Accept connections on <paramref name="port"/> (the host).</summary>
    void Listen(int port);

    /// <summary>
    /// Start connecting to <paramref name="address"/>:<paramref name="port"/>
    /// and return the peer id it will have; a <see cref="NetEventKind.Connected"/>
    /// event (or <see cref="NetEventKind.Disconnected"/> on failure) follows.
    /// </summary>
    int Connect(string address, int port);

    /// <summary>
    /// Send <paramref name="data"/> to <paramref name="peer"/> on
    /// <paramref name="channel"/>; the transport copies it. Sending to a peer
    /// that is gone does nothing.
    /// </summary>
    void Send(int peer, ReadOnlySpan<byte> data, NetChannel channel);

    /// <summary>
    /// Disconnect <paramref name="peer"/> after the reliable packets already
    /// sent to it; both sides then get <see cref="NetEventKind.Disconnected"/>.
    /// </summary>
    void Disconnect(int peer);

    /// <summary>The next event that is due, if any.</summary>
    bool Poll(out NetEvent netEvent);
}
