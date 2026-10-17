// T11.9d: LAN discovery (docs/plans/lan-discovery.md, SPEC §12 "Player names
// and LAN discovery"). The client queries, the hosts answer, as Chocolate
// Doom's net_query.c LAN search (NET_LANQuery; GPL-2.0, its idea, our wire
// format): a join's Join Game page sends a small UDP query to the broadcast
// addresses on a fixed discovery port, and every host listening there answers
// the sender directly with its game. IPv4 only for now (IPv6's ff02::1
// multicast may come later: nothing here is IPv4 but the sockets and targets).
//
// Separate from the lockstep protocol (NetMessages.cs) and from ENet, which
// owns the game port's socket and drops packets it doesn't know: discovery
// has a port of its own, NetDiscovery.Port. Plain System.Net.Sockets (not
// Godot's PacketPeerUDP): testable in `dotnet test` and outside the Godot
// lifetime rules. Polled by its owner (the host's NetSession, the Join Game
// page); no threads.
//
// WIRE FORMAT. Little-endian, as NetMessages; a string is a byte length and
// that many UTF-8 bytes, an MD5 its 16 bytes. Every message starts with a
// 4-byte magic and the discovery version (u8).
//   Query "ISDQ"  version, nonce (u32), zeros to QuerySize (256) bytes. A
//                 responder ignores anything else: another size, magic or
//                 version. The padding makes a reply never bigger than the
//                 query that asked for it: no amplification.
//   Reply "ISDR"  version, the nonce echoed (u32), the host's instance id
//                 (u64, random per session: the browser's key), the lockstep
//                 protocol version (u16), the build (string), the IWAD's MD5,
//                 the IWAD's file name (string, for display), the PWAD count
//                 (u8) and one MD5 over the PWADs' MD5s in load order
//                 (PwadHash), the host's player name (string), the game port
//                 (u16), the phase (u8: 0 lobby, 1 started), players in
//                 (u8), max players (u8), the mode (u8: deathmatch 0–2), the
//                 first map's name (string), the skill (u8, 1–5). At most
//                 QuerySize bytes: the name, the map and the IWAD's name are
//                 cut to fit.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace IsoDoom.Net;

/// <summary>A host's answer to a discovery query: who hosts what, and whether it takes players.</summary>
/// <param name="Nonce">The query's nonce, echoed.</param>
/// <param name="Instance">The host's session's random id (the browser's key: one entry a host, whatever address it answered from).</param>
/// <param name="Protocol">The lockstep protocol's version (<see cref="NetProtocol.Version"/>).</param>
/// <param name="Build">The build stamp (<see cref="NetIdentity.Build"/>).</param>
/// <param name="IwadMd5">The IWAD's MD5.</param>
/// <param name="IwadName">The IWAD's file name, for display.</param>
/// <param name="PwadCount">How many PWADs the host plays.</param>
/// <param name="PwadHash">The MD5 over their MD5s in load order (<see cref="NetDiscovery.PwadHash"/>).</param>
/// <param name="HostName">The host's player name (T11.9c).</param>
/// <param name="GamePort">The port the game listens on.</param>
/// <param name="Started">The game has started (nobody joins it).</param>
/// <param name="Players">The players in.</param>
/// <param name="MaxPlayers">The slots open.</param>
/// <param name="Deathmatch">The mode: 0 co-op, 1 deathmatch, 2 altdeath.</param>
/// <param name="MapName">The first map (<c>E1M1</c>, <c>MAP01</c>).</param>
/// <param name="Skill">The skill, 1–5.</param>
public sealed record NetDiscoveryReply(uint Nonce, ulong Instance, ushort Protocol, string Build, string IwadMd5, string IwadName,
    int PwadCount, string PwadHash, string HostName, int GamePort, bool Started, int Players, int MaxPlayers, int Deathmatch,
    string MapName, int Skill);

/// <summary>A game the browser lists: the host's reply, where it came from and whether this machine can join it.</summary>
/// <param name="Reply">The host's last reply.</param>
/// <param name="Address">The address it answered from (a non-loopback one preferred).</param>
/// <param name="LastHeardUs">When the browser last heard from it (its clock, µs).</param>
/// <param name="Refusal">Why the host would refuse this machine (the hello's order), or null: it can join.</param>
/// <param name="Reason">The refusal as the Join Game page shows it (<c>ANOTHER IWAD (DOOM2.WAD)</c>), or null.</param>
public sealed record NetDiscoveryEntry(NetDiscoveryReply Reply, IPAddress Address, long LastHeardUs, NetRefusal? Refusal, string? Reason)
{
    /// <summary>Whether this machine can join it.</summary>
    public bool Compatible => Refusal is null;

    /// <summary>The address a join connects to: <c>ADDRESS:PORT</c>, as <c>-connect</c> takes it.</summary>
    public string JoinAddress => $"{Address}:{Reply.GamePort}";
}

/// <summary>T11.9d: LAN discovery's constants, wire format and the interfaces' addresses (the comment block at the top of <c>NetDiscovery.cs</c>).</summary>
public static class NetDiscovery
{
    /// <summary>The discovery port, fixed: just below the game's default (5029), where the Host Game page's ports start.</summary>
    public const int Port = 5028;

    /// <summary>The discovery format's version; a message of another is ignored.</summary>
    public const byte Version = 1;

    /// <summary>A query's size; a reply is never bigger.</summary>
    public const int QuerySize = 256;

    /// <summary>How often a browser queries (µs).</summary>
    public const long QueryIntervalUs = 1_500_000;

    /// <summary>How long a browser keeps a host it no longer hears from (µs).</summary>
    public const long ExpireUs = 5_000_000;

    /// <summary>The most games a browser lists (the Join Game page's rows).</summary>
    public const int MaxEntries = 6;

    /// <summary>The most replies a responder sends one source address a second.</summary>
    public const int RepliesPerSecond = 4;

    private const int HeaderSize = 5;

    private static ReadOnlySpan<byte> QueryMagic => "ISDQ"u8;

    private static ReadOnlySpan<byte> ReplyMagic => "ISDR"u8;

    /// <summary>A query carrying <paramref name="nonce"/>: <see cref="QuerySize"/> bytes.</summary>
    public static byte[] EncodeQuery(uint nonce)
    {
        byte[] query = new byte[QuerySize];
        QueryMagic.CopyTo(query);
        query[4] = Version;
        BinaryPrimitives.WriteUInt32LittleEndian(query.AsSpan(HeaderSize), nonce);
        return query;
    }

    /// <summary>Whether <paramref name="data"/> is a query of this version, and its nonce.</summary>
    public static bool TryDecodeQuery(ReadOnlySpan<byte> data, out uint nonce)
    {
        nonce = 0;
        if (data.Length != QuerySize || !data[..4].SequenceEqual(QueryMagic) || data[4] != Version)
            return false;
        nonce = BinaryPrimitives.ReadUInt32LittleEndian(data[HeaderSize..]);
        return true;
    }

    /// <summary>
    /// A reply, at most <see cref="QuerySize"/> bytes: the host's name, then
    /// the map's, then the IWAD's file name are cut (whole characters) until
    /// it fits; a build stamp too long to fit even so is refused.
    /// </summary>
    public static byte[] EncodeReply(NetDiscoveryReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        string name = reply.HostName, map = reply.MapName, iwad = reply.IwadName;
        int Size() => 63 + Bytes(reply.Build) + Bytes(iwad) + Bytes(name) + Bytes(map);
        while (Size() > QuerySize && name.Length > 0)
            name = name[..^1];
        while (Size() > QuerySize && map.Length > 0)
            map = map[..^1];
        while (Size() > QuerySize && iwad.Length > 0)
            iwad = iwad[..^1];
        if (Size() > QuerySize)
            throw new ArgumentException("The build stamp is too long for a discovery reply.", nameof(reply));
        var w = new NetWriter(NetMessageType.Hello); // the type byte is overwritten by the magic below
        w.U8(0);
        w.U8(0);
        w.U8(0);
        w.U8(Version);
        w.U32(reply.Nonce);
        w.U64(reply.Instance);
        w.U16(reply.Protocol);
        w.Text(reply.Build);
        w.Md5(reply.IwadMd5);
        w.Text(iwad);
        w.U8(checked((byte)reply.PwadCount));
        w.Md5(reply.PwadHash);
        w.Text(name);
        w.U16(checked((ushort)reply.GamePort));
        w.U8(reply.Started ? (byte)1 : (byte)0);
        w.U8(checked((byte)reply.Players));
        w.U8(checked((byte)reply.MaxPlayers));
        w.U8(checked((byte)reply.Deathmatch));
        w.Text(map);
        w.U8(checked((byte)reply.Skill));
        byte[] bytes = w.ToArray();
        ReplyMagic.CopyTo(bytes);
        return bytes;
    }

    private static int Bytes(string s) => Encoding.UTF8.GetByteCount(s);

    /// <summary>Whether <paramref name="data"/> is a well-formed reply of this version (at most <see cref="QuerySize"/> bytes), and the reply.</summary>
    public static bool TryDecodeReply(ReadOnlySpan<byte> data, [NotNullWhen(true)] out NetDiscoveryReply? reply)
    {
        reply = null;
        if (data.Length is < HeaderSize or > QuerySize || !data[..4].SequenceEqual(ReplyMagic) || data[4] != Version)
            return false;
        byte[] copy = data.ToArray();
        copy[3] = (byte)NetMessageType.Hello; // a reader past a type byte: the magic's last byte stands in for it
        try
        {
            var r = new NetReader(copy.AsMemory(3));
            r.U8(); // the version
            uint nonce = r.U32();
            ulong instance = r.U64();
            ushort protocol = r.U16();
            string build = r.Text();
            string iwadMd5 = r.Md5();
            string iwadName = r.Text();
            int pwadCount = r.U8();
            string pwadHash = r.Md5();
            string name = r.Text();
            int port = r.U16();
            int phase = r.U8();
            int players = r.U8();
            int maxPlayers = r.U8();
            int deathmatch = r.U8();
            string map = r.Text();
            int skill = r.U8();
            r.End();
            if (phase > 1 || deathmatch > 2)
                return false;
            reply = new NetDiscoveryReply(nonce, instance, protocol, build, iwadMd5, iwadName, pwadCount, pwadHash, name, port,
                phase == 1, players, maxPlayers, deathmatch, map, skill);
            return true;
        }
        catch (NetProtocolException)
        {
            return false;
        }
    }

    /// <summary>One MD5 over the PWADs' MD5s (their 16 bytes each, in load order): a reply's PWAD list in 16 bytes.</summary>
    [SuppressMessage("Security", "CA5351", Justification = "MD5 over the WADs' MD5s names a list, not a security check")]
    public static string PwadHash(IReadOnlyList<string> pwadMd5s)
    {
        ArgumentNullException.ThrowIfNull(pwadMd5s);
        byte[] all = new byte[16 * pwadMd5s.Count];
        for (int i = 0; i < pwadMd5s.Count; i++)
            Convert.FromHexString(pwadMd5s[i]).CopyTo(all, 16 * i);
        return Convert.ToHexStringLower(MD5.HashData(all));
    }

    /// <summary>
    /// Why the host of <paramref name="reply"/> would refuse a machine of
    /// <paramref name="self"/> (<see cref="NetIdentity.Refusal"/>, the
    /// hello's order: protocol, build, IWAD, PWADs, started, full) and the
    /// Join Game page's short reason, or (null, null).
    /// </summary>
    public static (NetRefusal? Refusal, string? Reason) Compatibility(NetDiscoveryReply reply, NetIdentity self)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(self);
        bool samePwads = reply.PwadCount == self.PwadMd5s.Count && string.Equals(reply.PwadHash, PwadHash(self.PwadMd5s), StringComparison.OrdinalIgnoreCase);
        NetRefusal? refusal = NetIdentity.Refusal(reply.Protocol, reply.Build, reply.IwadMd5, samePwads, reply.Started,
            reply.Players >= reply.MaxPlayers, self);
        string? reason = refusal switch
        {
            null => null,
            NetRefusal.ProtocolMismatch or NetRefusal.BuildMismatch => "ANOTHER BUILD",
            NetRefusal.IwadMismatch => reply.IwadName.Length > 0 ? $"ANOTHER IWAD ({reply.IwadName.ToUpperInvariant()})" : "ANOTHER IWAD",
            NetRefusal.PwadMismatch => "OTHER PWADS",
            NetRefusal.GameInProgress => "STARTED",
            _ => "FULL",
        };
        return (refusal, reason);
    }

    /// <summary>
    /// This machine's LAN addresses: the IPv4 unicast addresses of the
    /// interfaces that are up, but loopback and link-local (169.254/16), each
    /// with its subnet's broadcast address; none where the system won't say.
    /// </summary>
    public static List<(IPAddress Address, IPAddress Broadcast)> LanAddresses()
    {
        var list = new List<(IPAddress, IPAddress)>();
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return list;
        }
        catch (PlatformNotSupportedException)
        {
            return list;
        }
        foreach (NetworkInterface nic in interfaces)
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            UnicastIPAddressInformationCollection unicast;
            try
            {
                unicast = nic.GetIPProperties().UnicastAddresses;
            }
            catch (NetworkInformationException)
            {
                continue;
            }
            foreach (UnicastIPAddressInformation info in unicast)
            {
                IPAddress a = info.Address;
                if (a.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(a) || IsLinkLocal(a))
                    continue;
                byte[] bytes = a.GetAddressBytes();
                byte[] mask;
                try
                {
                    mask = info.IPv4Mask?.GetAddressBytes() ?? [255, 255, 255, 0];
                }
                catch (NotImplementedException)
                {
                    mask = [255, 255, 255, 0];
                }
                if (mask.Length != 4)
                    mask = [255, 255, 255, 0];
                for (int i = 0; i < 4; i++)
                    bytes[i] |= (byte)~mask[i];
                list.Add((a, new IPAddress(bytes)));
            }
        }
        return list;
    }

    /// <summary>Whether <paramref name="a"/> is an IPv4 link-local address (169.254/16).</summary>
    public static bool IsLinkLocal(IPAddress a)
    {
        ArgumentNullException.ThrowIfNull(a);
        byte[] b = a.GetAddressBytes();
        return b.Length == 4 && b[0] == 169 && b[1] == 254;
    }

    /// <summary>
    /// Where a browser sends its queries by default: the limited broadcast
    /// (255.255.255.255), each LAN interface's subnet broadcast (the limited
    /// one leaves only by the default interface on some systems), and
    /// 127.0.0.1 (a host on this machine), all on <paramref name="port"/>.
    /// </summary>
    public static List<IPEndPoint> DefaultTargets(int port = Port)
    {
        var targets = new List<IPEndPoint> { new(IPAddress.Broadcast, port) };
        foreach ((IPAddress _, IPAddress broadcast) in LanAddresses())
        {
            if (!targets.Exists(t => t.Address.Equals(broadcast)))
                targets.Add(new IPEndPoint(broadcast, port));
        }
        targets.Add(new IPEndPoint(IPAddress.Loopback, port));
        return targets;
    }

    /// <summary>A random nonzero 64-bit number (a host's instance id).</summary>
    public static ulong RandomId()
    {
        Span<byte> bytes = stackalloc byte[8];
        ulong id;
        do
        {
            RandomNumberGenerator.Fill(bytes);
            id = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        }
        while (id == 0);
        return id;
    }

    /// <summary>
    /// A non-blocking UDP socket bound to <paramref name="address"/>:<paramref name="port"/>
    /// (0: any free port), not shared with another socket on that port, Windows'
    /// ICMP "port unreachable" resets off (they would fail the next receive).
    /// </summary>
    internal static Socket Bind(IPAddress address, int port, bool broadcast)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                socket.ExclusiveAddressUse = true;
                const int SIO_UDP_CONNRESET = -1744830452;
                socket.IOControl(SIO_UDP_CONNRESET, [0, 0, 0, 0], null);
            }
            socket.EnableBroadcast = broadcast;
            socket.Blocking = false;
            socket.Bind(new IPEndPoint(address, port));
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>The next datagram waiting on <paramref name="socket"/> into <paramref name="buffer"/>: its length and sender, or false when none waits.</summary>
    internal static bool TryReceive(Socket socket, byte[] buffer, out int length, [NotNullWhen(true)] out IPEndPoint? from)
    {
        length = 0;
        from = null;
        while (true)
        {
            try
            {
                if (socket.Available <= 0)
                    return false;
                EndPoint any = new IPEndPoint(IPAddress.Any, 0);
                length = socket.ReceiveFrom(buffer, ref any);
                from = (IPEndPoint)any;
                return true;
            }
            catch (SocketException e) when (e.SocketErrorCode is SocketError.MessageSize or SocketError.ConnectionReset)
            {
                // a datagram bigger than the buffer (dropped), or a reset: the next one
            }
            catch (SocketException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
    }
}

/// <summary>
/// T11.9d: a host's side of LAN discovery: a UDP socket on the discovery port
/// answering each valid query with the game as <see cref="NetDiscoveryResponder(IPAddress, int, Func{NetDiscoveryReply})"/>'s
/// callback says it is now. At most <see cref="NetDiscovery.RepliesPerSecond"/>
/// replies a second to one source address. Polled (<see cref="Update"/>); no threads.
/// </summary>
public sealed class NetDiscoveryResponder : IDisposable
{
    private readonly Socket _socket;
    private readonly Func<NetDiscoveryReply> _state;
    private readonly byte[] _buffer = new byte[2048];
    private readonly Dictionary<IPAddress, (long WindowUs, int Count)> _sent = [];
    private long _pruneUs;

    /// <summary>
    /// Binds <paramref name="address"/>:<paramref name="port"/> (0: any free
    /// port); <paramref name="state"/> gives the reply but its nonce. Throws
    /// <see cref="SocketException"/> when the port is taken.
    /// </summary>
    public NetDiscoveryResponder(IPAddress address, int port, Func<NetDiscoveryReply> state)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        _socket = NetDiscovery.Bind(address, port, broadcast: false);
        Port = ((IPEndPoint)_socket.LocalEndPoint!).Port;
    }

    /// <summary>The port it listens on.</summary>
    public int Port { get; }

    /// <summary>The replies sent.</summary>
    public int Answered { get; private set; }

    /// <summary>The queries not answered for the rate limit.</summary>
    public int Limited { get; private set; }

    /// <summary>Answers every query waiting, at <paramref name="nowUs"/> (µs, the owner's clock: the rate limit's).</summary>
    public void Update(long nowUs)
    {
        if (nowUs - _pruneUs > 10 * NetProtocol.MicrosecondsPerSecond)
        {
            _pruneUs = nowUs;
            var old = new List<IPAddress>();
            foreach (KeyValuePair<IPAddress, (long WindowUs, int Count)> pair in _sent)
            {
                if (nowUs - pair.Value.WindowUs >= NetProtocol.MicrosecondsPerSecond)
                    old.Add(pair.Key);
            }
            foreach (IPAddress a in old)
                _sent.Remove(a);
        }
        while (NetDiscovery.TryReceive(_socket, _buffer, out int length, out IPEndPoint? from))
        {
            if (!NetDiscovery.TryDecodeQuery(_buffer.AsSpan(0, length), out uint nonce))
                continue;
            (long window, int count) = _sent.TryGetValue(from.Address, out var s) && nowUs - s.WindowUs < NetProtocol.MicrosecondsPerSecond ? s : (nowUs, 0);
            if (count >= NetDiscovery.RepliesPerSecond)
            {
                Limited++;
                continue;
            }
            _sent[from.Address] = (window, count + 1);
            try
            {
                _socket.SendTo(NetDiscovery.EncodeReply(_state() with { Nonce = nonce }), from);
                Answered++;
            }
            catch (SocketException)
            {
                // unreachable sender: no answer
            }
        }
    }

    public void Dispose() => _socket.Dispose();
}

/// <summary>
/// T11.9d: a join's side of LAN discovery: queries the targets every
/// <see cref="NetDiscovery.QueryIntervalUs"/> from an ephemeral port and
/// lists the hosts that answer (<see cref="Entries"/>: one per host, by its
/// instance id; dropped after <see cref="NetDiscovery.ExpireUs"/> without a
/// reply; sorted by name, then address; at most <see cref="NetDiscovery.MaxEntries"/>),
/// each marked against this machine's identity. Polled (<see cref="Update"/>); no threads.
/// </summary>
public sealed class NetDiscoveryBrowser : IDisposable
{
    private readonly Socket _socket;
    private readonly NetIdentity _self;
    private readonly IReadOnlyList<IPEndPoint>? _targets;
    private readonly int _port;
    private readonly uint _nonce;
    private readonly byte[] _buffer = new byte[2048];
    private readonly Dictionary<ulong, NetDiscoveryEntry> _hosts = [];
    private List<NetDiscoveryEntry> _entries = [];
    private long _nextQueryUs = long.MinValue;

    /// <summary>
    /// A browser for a machine of <paramref name="self"/>, querying
    /// <paramref name="targets"/> (null: <see cref="NetDiscovery.DefaultTargets"/>
    /// on <paramref name="port"/>, looked up again at each query). Throws
    /// <see cref="SocketException"/> when no socket can be made.
    /// </summary>
    public NetDiscoveryBrowser(NetIdentity self, IReadOnlyList<IPEndPoint>? targets = null, int port = NetDiscovery.Port)
    {
        ArgumentNullException.ThrowIfNull(self);
        _self = self;
        _targets = targets;
        _port = port;
        _nonce = (uint)NetDiscovery.RandomId();
        _socket = NetDiscovery.Bind(IPAddress.Any, 0, broadcast: true);
        Port = ((IPEndPoint)_socket.LocalEndPoint!).Port;
    }

    /// <summary>The ephemeral port it queries from (the replies come back to it).</summary>
    public int Port { get; }

    /// <summary>The games found, sorted by name, then address.</summary>
    public IReadOnlyList<NetDiscoveryEntry> Entries => _entries;

    /// <summary>Counts each change of <see cref="Entries"/> (the page rebuilds its rows).</summary>
    public int Changes { get; private set; }

    /// <summary>The queries sent (each target counts).</summary>
    public int QueriesSent { get; private set; }

    /// <summary>Sends the queries when due and takes the replies waiting, at <paramref name="nowUs"/> (µs, the owner's clock).</summary>
    public void Update(long nowUs)
    {
        if (nowUs >= _nextQueryUs)
        {
            _nextQueryUs = nowUs + NetDiscovery.QueryIntervalUs;
            byte[] query = NetDiscovery.EncodeQuery(_nonce);
            foreach (IPEndPoint target in _targets ?? NetDiscovery.DefaultTargets(_port))
            {
                try
                {
                    _socket.SendTo(query, target);
                    QueriesSent++;
                }
                catch (SocketException)
                {
                    // no route there (no broadcast on this system, an interface gone): the others still go
                }
            }
        }
        bool changed = false, heard = false;
        while (NetDiscovery.TryReceive(_socket, _buffer, out int length, out IPEndPoint? from))
        {
            if (!NetDiscovery.TryDecodeReply(_buffer.AsSpan(0, length), out NetDiscoveryReply? reply) || reply.Nonce != _nonce)
                continue;
            IPAddress address = from.Address.IsIPv4MappedToIPv6 ? from.Address.MapToIPv4() : from.Address;
            // the same host heard over loopback and the LAN: its LAN address, which the others can use too
            if (_hosts.TryGetValue(reply.Instance, out NetDiscoveryEntry? old) && IPAddress.IsLoopback(address) && !IPAddress.IsLoopback(old.Address)
                && nowUs - old.LastHeardUs < NetDiscovery.ExpireUs)
                address = old.Address;
            (NetRefusal? refusal, string? reason) = NetDiscovery.Compatibility(reply, _self);
            var entry = new NetDiscoveryEntry(reply, address, nowUs, refusal, reason);
            changed |= old is null || old.Reply with { Nonce = 0 } != reply with { Nonce = 0 } || !old.Address.Equals(address);
            _hosts[reply.Instance] = entry;
            heard = true;
        }
        var gone = new List<ulong>();
        foreach (KeyValuePair<ulong, NetDiscoveryEntry> pair in _hosts)
        {
            if (nowUs - pair.Value.LastHeardUs >= NetDiscovery.ExpireUs)
                gone.Add(pair.Key);
        }
        foreach (ulong id in gone)
            _hosts.Remove(id);
        if (changed || heard || gone.Count > 0)
        {
            var list = new List<NetDiscoveryEntry>(_hosts.Values);
            list.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.Reply.HostName, b.Reply.HostName);
                if (c == 0)
                    c = string.CompareOrdinal(a.JoinAddress, b.JoinAddress);
                return c != 0 ? c : a.Reply.Instance.CompareTo(b.Reply.Instance);
            });
            if (list.Count > NetDiscovery.MaxEntries)
                list.RemoveRange(NetDiscovery.MaxEntries, list.Count - NetDiscovery.MaxEntries);
            _entries = list; // (each reply's time too)
            if (changed || gone.Count > 0)
                Changes++;
        }
    }

    public void Dispose() => _socket.Dispose();
}
