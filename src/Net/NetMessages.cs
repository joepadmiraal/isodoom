// The lockstep protocol (M11, T11.5; docs/plans/multiplayer.md, SPEC §12
// "T11.5 lockstep protocol"). Our own, with ideas from Chocolate Doom's
// net_client.c, net_server.c and net_structs.h (GPL-2.0); no interop with
// vanilla or Chocolate Doom netgames.
//
// TOPOLOGY. A star through the host, as Chocolate Doom's: every player runs a
// NetClient; the host also runs the NetServer, its own client connected to it
// like the others (slot 0, by the host token). Clients send their commands to
// the server; the server sends every tic's full bundle to everyone.
//
// CHANNELS. Everything but the pings goes on the transport's reliable,
// ordered channel (ENet's resends until acknowledged), so the protocol has no
// acknowledgements or resends of its own: lockstep needs every command anyway,
// and a lost packet costs a resend's delay either way. Ping/Pong go on the
// unreliable channel; a lost one is just a missing sample.
//
// ENCODING. One message a packet: a type byte, then little-endian fields.
// int8/16/32/64 as is; a string is a byte length and that many UTF-8 bytes; an
// MD5 its 16 bytes. A ticcmd_t is 8 bytes: forwardmove, sidemove (int8),
// angleturn, consistancy (int16), chatchar, buttons (uint8). A malformed
// message is a NetProtocolException: the server drops that peer, a client
// disconnects.
//
// LOBBY.
//   client -> Hello      protocol (u16), build (string), IWAD MD5, PWAD count
//                        (u8) and MD5s in load order, host token (u64). The
//                        type byte and protocol come first in every version,
//                        so a server reads them before the rest.
//   server -> Refuse     reason (u8, NetRefusal), detail (string); then the
//                        server disconnects. A different protocol, build,
//                        IWAD or PWAD list, a full game or one under way.
//   server -> Welcome    slot (u8), max players (u8). Slot 0 is the host's
//                        own client's (its hello carries the host token); the
//                        others take the lowest free slot from 1.
//   server -> Settings   the game settings (below), on joining and on change.
//   server -> Lobby      occupied slots (u8 mask), ready slots (u8 mask).
//   client -> Ready      ready (u8 0/1).
//   server -> Start      consoleplayer (u8: the receiver's slot), players in
//                        game (u8 mask), start tic (i32), input delay in tics
//                        (u8), the game settings, whose players in game
//                        must be the same mask (the server sets them to it:
//                        one source of truth, checked on decoding).
// Game settings: gamemode, gameskill (u8), flags (u8: 1 netgame, 2
// nomonsters, 4 respawnparm, 8 fastparm), deathmatch (u8), gameversion (u8,
// 255 for the IWAD's default), gamemission (u8), players in game (u8 mask,
// SpawnSettings.playersingame; T11.5a: in the lobby the host's, at the
// Start the players'), episode, map (u8), timer in
// minutes (u16, SpawnSettings.timelimit), the tweaks (u8 flags: 1 AbsoluteAiming, 2 AbsoluteMovement,
// 4 UseFallback, 8 InstantStop; u32 AimAssistCone). Every player runs these.
//
// TICS. The server starts the game at its clock's zero = the start tic, and
// fills tics start..start+delay-1 with empty commands for everyone. A
// client's clock then runs at 35 Hz from the Start's arrival (plus half its
// round trip); at clock tic c it has sent its commands up to tic c + delay.
//   client -> Commands   first tic (i32), count (u8), count ticcmds: the
//                        sender's commands for tics first..first+count-1,
//                        in order, none skipped. Each carries in consistancy
//                        the sender's own player's consistancy (below) after
//                        tic t - ConsistancyLag (0 before the start tic + lag),
//                        as vanilla's G_BuildTiccmd stamps maketic's.
//   server -> Bundle     first tic (i32), count (u8), then per tic the
//                        players in game (u8 mask) and a ticcmd for each, in
//                        slot order. Sent once every player in game has sent
//                        that tic's command, in tic order, none skipped.
// A client runs tic t only from its bundle, and only once its clock has
// reached t (so a bundle that comes early waits: the input delay holds). It
// never sends commands more than BackupTics past the last tic it ran.
//
// CHECKSUMS. Tics run in windows of 35 from the start tic (window w: start +
// 35w .. start + 35w + 34). After a window's last tic each client sends
//   client -> Checksum   window (i32), World.Checksum() after that tic (u64),
//                        the consistancy of each of the 4 players (4 × i16:
//                        vanilla's, the player mobj's x, or the P_Random
//                        index without a mobj; 0 not in game), and the first
//                        tic in the window whose bundle carried a consistancy
//                        that differs from the receiver's own (i32, -1 none).
// The server compares a window once every client still in the game has
// reported it; the first window where any two differ, or a client saw a
// consistancy differ, it announces once:
//   server -> Desync     window (i32), its first and last tic (i32 × 2).
//
// CLOCK. client -> Ping (unreliable): the client's time (i64 µs) and its
// last round trip (i32 µs). server -> Pong: the ping's time echoed, the
// server's game clock (i64 µs since the start, i64.MinValue before it), and
// (T11.11, protocol 3) the slots whose command the next bundle still waits
// for (u8 mask, 0 in the lobby) and each slot's last reported round trip
// (4 × u16 ms, 65535 unknown or empty): the overlay's pings and the
// "waiting for player N" message. The
// client estimates the server's clock now as that plus half the round trip
// (from the samples with the shortest round trips) and runs its own clock
// faster or slower, within ±5 %, to close the difference: Chocolate Doom's
// lag adjustment (net_client.c's offsetms), our way.
//
// LEAVING. client -> Quit, then it disconnects. On a quit or a disconnect in
// game the server drops the player from the bundles after the last command
// it sent, and tells everyone
//   server -> PlayerQuit slot (u8), the first tic without it (i32).
// The bundles' masks are what the game applies (playeringame), as vanilla's
// d_net.c does on NCMD_EXIT (T11.11: at the tic, Chocolate Doom's RunTic:
// World.PlayerQuitGame on every machine before that tic).
//
// TIMEOUTS (T11.11). Each side drops the other after TimeoutUs (default
// NetProtocol.DefaultTimeoutUs, -nettimeout) without a packet from it (the
// pings keep a live link busy), and the server drops a player whose command
// the next bundle has waited for that long (counted from the later of its
// last command and the last bundle sent): the player leaves as on a quit. The
// host's own client never times out on its server (the same machine). ENet's
// own peer timeout is set to the same time.
//
// DESYNC (T11.11). On the server's Desync every client stops: it runs no
// more tics and sends no more commands; the game stops with a message and
// writes its dumps (the game's side). The server bundles nothing after it.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using IsoDoom.Sim;
using IsoDoom.Wad;

namespace IsoDoom.Net;

/// <summary>The protocol's constants (see the comment block at the top of <c>NetMessages.cs</c>).</summary>
public static class NetProtocol
{
    /// <summary>The protocol version; a hello with another is refused (2: the settings carry <c>playersingame</c>, T11.5a; 3: the pong carries the waiting slots and the pings, T11.11).</summary>
    public const ushort Version = 3;

    /// <summary>
    /// T11.11: how long either side waits without a packet from the other (or
    /// the server for a player's command the next bundle waits for) before it
    /// drops it, in microseconds: 10 s (<c>-nettimeout</c>; SPEC §12 T11.11).
    /// </summary>
    public const long DefaultTimeoutUs = 10_000_000;

    /// <summary>T11.11: a pong's round trip of an empty slot, or of a player that has not reported one.</summary>
    public const ushort NoRtt = ushort.MaxValue;

    /// <summary>vanilla's <c>MAXPLAYERS</c>: the slots.</summary>
    public const int MaxPlayers = 4;

    /// <summary>The tic rate (doomdef.h <c>TICRATE</c>).</summary>
    public const int TicRate = 35;

    /// <summary>Tics a checksum window holds: one second.</summary>
    public const int WindowTics = 35;

    /// <summary>
    /// How far past the last tic it ran a client may send commands (vanilla's
    /// <c>BACKUPTICS</c>, larger: vanilla's 12 is 340 ms, too short for the internet).
    /// </summary>
    public const int BackupTics = 64;

    /// <summary>A command for tic t carries its sender's consistancy after tic t − this (vanilla: <c>BACKUPTICS</c>).</summary>
    public const int ConsistancyLag = BackupTics;

    /// <summary>The most tics one Commands or Bundle message carries.</summary>
    public const int MaxTicsPerMessage = 64;

    /// <summary>The longest input delay, in tics.</summary>
    public const int MaxDelay = 35;

    /// <summary>The bytes of one encoded <see cref="ticcmd_t"/>.</summary>
    public const int TiccmdSize = 8;

    /// <summary>Microseconds per tic, as a fraction: <c>tic = us * TicRate / 1_000_000</c>.</summary>
    public const long MicrosecondsPerSecond = 1_000_000;
}

/// <summary>Why a server refused a hello.</summary>
public enum NetRefusal : byte
{
    /// <summary>The protocol versions differ.</summary>
    ProtocolMismatch = 1,

    /// <summary>The builds differ (the version stamp, <c>dev</c> unstamped).</summary>
    BuildMismatch = 2,

    /// <summary>The IWADs differ (MD5).</summary>
    IwadMismatch = 3,

    /// <summary>The PWADs differ (MD5s, in load order).</summary>
    PwadMismatch = 4,

    /// <summary>Every slot is taken.</summary>
    GameFull = 5,

    /// <summary>The game has started: nobody joins a game in progress.</summary>
    GameInProgress = 6,
}

/// <summary>The message types (the first byte of every packet).</summary>
public enum NetMessageType : byte
{
    Hello = 1,
    Refuse = 2,
    Welcome = 3,
    Lobby = 4,
    Settings = 5,
    Ready = 6,
    Start = 7,
    Commands = 8,
    Bundle = 9,
    Checksum = 10,
    Desync = 11,
    Quit = 12,
    PlayerQuit = 13,
    Ping = 14,
    Pong = 15,
}

/// <summary>A message that breaks the protocol (malformed, unexpected or out of order).</summary>
public sealed class NetProtocolException : Exception
{
    public NetProtocolException()
    {
    }

    public NetProtocolException(string message)
        : base(message)
    {
    }

    public NetProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// What a machine must share with the others to play: the build and the WADs
/// (MD5s as 32 hex digits; the PWADs in load order). The hello carries it.
/// </summary>
/// <param name="Build">The build's version stamp (the game's <c>application/config/version</c>, <c>dev</c> unstamped).</param>
/// <param name="IwadMd5">The IWAD's MD5.</param>
/// <param name="PwadMd5s">The PWADs' MD5s, in load order.</param>
public sealed record NetIdentity(string Build, string IwadMd5, IReadOnlyList<string> PwadMd5s)
{
    /// <summary>Whether <paramref name="other"/>'s PWADs are this one's, in the same order.</summary>
    public bool SamePwads(NetIdentity other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (PwadMd5s.Count != other.PwadMd5s.Count)
            return false;
        for (int i = 0; i < PwadMd5s.Count; i++)
        {
            if (!string.Equals(PwadMd5s[i], other.PwadMd5s[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }
}

/// <summary>
/// The game every player runs: the spawn settings (mode, skill, netgame,
/// deathmatch, monsters, version), the tweaks (every player must run the
/// same, <c>Tweaks.cs</c>) and the first map (vanilla's <c>startepisode</c>,
/// <c>startmap</c>). T11.4: <c>-timer</c>'s minutes are the spawn settings'
/// <see cref="SpawnSettings.timelimit"/>.
/// </summary>
public sealed record NetGameSettings(SpawnSettings Spawn, Tweaks Tweaks, int Episode, int Map);

/// <summary>The Start message as a client keeps it.</summary>
/// <param name="ConsolePlayer">The receiver's player (its slot).</param>
/// <param name="PlayerMask">The players in game (bit i: slot i).</param>
/// <param name="StartTic">The game's first tic.</param>
/// <param name="Delay">The input delay, in tics.</param>
/// <param name="Settings">The game's settings.</param>
public sealed record NetStart(int ConsolePlayer, byte PlayerMask, int StartTic, int Delay, NetGameSettings Settings);

/// <summary>A server's desync announcement: the first checksum window that differs.</summary>
/// <param name="Window">The window (from 0 at the start tic).</param>
/// <param name="FirstTic">Its first tic.</param>
/// <param name="LastTic">Its last tic.</param>
public readonly record struct NetDesync(int Window, int FirstTic, int LastTic);

/// <summary>A client's report of one checksum window.</summary>
/// <param name="Window">The window.</param>
/// <param name="Checksum"><c>World.Checksum()</c> after its last tic.</param>
/// <param name="Consistancy">The 4 players' consistancy after its last tic.</param>
/// <param name="FirstBadTic">The first tic of the window whose bundle carried a consistancy differing from the reporter's own, or −1.</param>
public sealed record NetChecksumReport(int Window, ulong Checksum, short[] Consistancy, int FirstBadTic)
{
    /// <summary>Whether this and <paramref name="other"/> agree on the state (checksum and consistancy).</summary>
    public bool SameState(NetChecksumReport other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Checksum == other.Checksum && Consistancy.AsSpan().SequenceEqual(other.Consistancy);
    }
}

/// <summary>A decoded hello.</summary>
public sealed record NetHello(ushort Protocol, NetIdentity? Identity, ulong HostToken);

/// <summary>
/// Encodes and decodes the protocol's messages (the layout is in the comment
/// block at the top of this file). Each <c>Encode*</c> returns one packet.
/// </summary>
public static class NetMessages
{
    // ---- lobby ----

    /// <summary>Hello: <paramref name="protocol"/> is <see cref="NetProtocol.Version"/> but in tests.</summary>
    public static byte[] EncodeHello(NetIdentity identity, ulong hostToken, ushort protocol = NetProtocol.Version)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var w = new NetWriter(NetMessageType.Hello);
        w.U16(protocol);
        w.Text(identity.Build);
        w.Md5(identity.IwadMd5);
        if (identity.PwadMd5s.Count > byte.MaxValue)
            throw new ArgumentException("Too many PWADs.", nameof(identity));
        w.U8((byte)identity.PwadMd5s.Count);
        foreach (string md5 in identity.PwadMd5s)
            w.Md5(md5);
        w.U64(hostToken);
        return w.ToArray();
    }

    /// <summary>A hello; for another protocol only <see cref="NetHello.Protocol"/> is read.</summary>
    public static NetHello DecodeHello(NetReader r)
    {
        ArgumentNullException.ThrowIfNull(r);
        ushort protocol = r.U16();
        if (protocol != NetProtocol.Version)
            return new NetHello(protocol, null, 0);
        string build = r.Text();
        string iwad = r.Md5();
        int count = r.U8();
        var pwads = new string[count];
        for (int i = 0; i < count; i++)
            pwads[i] = r.Md5();
        ulong token = r.U64();
        r.End();
        return new NetHello(protocol, new NetIdentity(build, iwad, pwads), token);
    }

    public static byte[] EncodeRefuse(NetRefusal reason, string detail)
    {
        var w = new NetWriter(NetMessageType.Refuse);
        w.U8((byte)reason);
        w.Text(detail);
        return w.ToArray();
    }

    public static byte[] EncodeWelcome(int slot, int maxPlayers)
    {
        var w = new NetWriter(NetMessageType.Welcome);
        w.U8((byte)slot);
        w.U8((byte)maxPlayers);
        return w.ToArray();
    }

    public static byte[] EncodeLobby(byte occupied, byte ready)
    {
        var w = new NetWriter(NetMessageType.Lobby);
        w.U8(occupied);
        w.U8(ready);
        return w.ToArray();
    }

    public static byte[] EncodeSettings(NetGameSettings settings)
    {
        var w = new NetWriter(NetMessageType.Settings);
        w.Settings(settings);
        return w.ToArray();
    }

    public static byte[] EncodeReady(bool ready)
    {
        var w = new NetWriter(NetMessageType.Ready);
        w.U8(ready ? (byte)1 : (byte)0);
        return w.ToArray();
    }

    public static byte[] EncodeStart(NetStart start)
    {
        ArgumentNullException.ThrowIfNull(start);
        var w = new NetWriter(NetMessageType.Start);
        w.U8((byte)start.ConsolePlayer);
        w.U8(start.PlayerMask);
        w.I32(start.StartTic);
        w.U8((byte)start.Delay);
        w.Settings(start.Settings);
        return w.ToArray();
    }

    public static NetStart DecodeStart(NetReader r)
    {
        ArgumentNullException.ThrowIfNull(r);
        int consoleplayer = r.U8();
        byte mask = r.U8();
        int startTic = r.I32();
        int delay = r.U8();
        NetGameSettings settings = r.Settings();
        r.End();
        if (consoleplayer >= NetProtocol.MaxPlayers || (mask & (1 << consoleplayer)) == 0 || mask >= 1 << NetProtocol.MaxPlayers)
            throw new NetProtocolException($"Start: player {consoleplayer} not in mask {mask}.");
        if (delay > NetProtocol.MaxDelay)
            throw new NetProtocolException($"Start: delay {delay}.");
        if (settings.Spawn.playersingame != mask)
            throw new NetProtocolException($"Start: players in game {settings.Spawn.playersingame} differ from the mask {mask}.");
        return new NetStart(consoleplayer, mask, startTic, delay, settings);
    }

    // ---- tics ----

    /// <summary>Commands: the sender's <paramref name="cmds"/> for tics <paramref name="firstTic"/> on.</summary>
    public static byte[] EncodeCommands(int firstTic, ReadOnlySpan<ticcmd_t> cmds)
    {
        if (cmds.Length is 0 or > NetProtocol.MaxTicsPerMessage)
            throw new ArgumentOutOfRangeException(nameof(cmds));
        var w = new NetWriter(NetMessageType.Commands);
        w.I32(firstTic);
        w.U8((byte)cmds.Length);
        foreach (ticcmd_t cmd in cmds)
            w.Ticcmd(cmd);
        return w.ToArray();
    }

    /// <summary>A Commands message's first tic and commands.</summary>
    public static int DecodeCommands(NetReader r, List<ticcmd_t> into)
    {
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(into);
        int first = r.I32();
        int count = r.U8();
        if (count is 0 or > NetProtocol.MaxTicsPerMessage)
            throw new NetProtocolException($"Commands: {count} tics.");
        for (int i = 0; i < count; i++)
            into.Add(r.Ticcmd());
        r.End();
        return first;
    }

    /// <summary>
    /// Bundle: tics <paramref name="firstTic"/> to <paramref name="firstTic"/> +
    /// <paramref name="count"/> − 1, each the players in <paramref name="masks"/>
    /// and their commands in <paramref name="cmds"/> (<see cref="NetProtocol.MaxPlayers"/> a tic, by slot).
    /// </summary>
    public static byte[] EncodeBundle(int firstTic, int count, ReadOnlySpan<byte> masks, ReadOnlySpan<ticcmd_t> cmds)
    {
        if (count is 0 or > NetProtocol.MaxTicsPerMessage)
            throw new ArgumentOutOfRangeException(nameof(count));
        var w = new NetWriter(NetMessageType.Bundle);
        w.I32(firstTic);
        w.U8((byte)count);
        for (int t = 0; t < count; t++)
        {
            w.U8(masks[t]);
            for (int i = 0; i < NetProtocol.MaxPlayers; i++)
            {
                if ((masks[t] & (1 << i)) != 0)
                    w.Ticcmd(cmds[t * NetProtocol.MaxPlayers + i]);
            }
        }
        return w.ToArray();
    }

    public static byte[] EncodeChecksum(NetChecksumReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Consistancy.Length != NetProtocol.MaxPlayers)
            throw new ArgumentException("One consistancy a player.", nameof(report));
        var w = new NetWriter(NetMessageType.Checksum);
        w.I32(report.Window);
        w.U64(report.Checksum);
        foreach (short c in report.Consistancy)
            w.I16(c);
        w.I32(report.FirstBadTic);
        return w.ToArray();
    }

    public static NetChecksumReport DecodeChecksum(NetReader r)
    {
        ArgumentNullException.ThrowIfNull(r);
        int window = r.I32();
        ulong checksum = r.U64();
        var consistancy = new short[NetProtocol.MaxPlayers];
        for (int i = 0; i < consistancy.Length; i++)
            consistancy[i] = r.I16();
        int bad = r.I32();
        r.End();
        return new NetChecksumReport(window, checksum, consistancy, bad);
    }

    public static byte[] EncodeDesync(NetDesync desync)
    {
        var w = new NetWriter(NetMessageType.Desync);
        w.I32(desync.Window);
        w.I32(desync.FirstTic);
        w.I32(desync.LastTic);
        return w.ToArray();
    }

    public static byte[] EncodeQuit() => new NetWriter(NetMessageType.Quit).ToArray();

    public static byte[] EncodePlayerQuit(int slot, int tic)
    {
        var w = new NetWriter(NetMessageType.PlayerQuit);
        w.U8((byte)slot);
        w.I32(tic);
        return w.ToArray();
    }

    public static byte[] EncodePing(long clientTimeUs, int lastRttUs)
    {
        var w = new NetWriter(NetMessageType.Ping);
        w.I64(clientTimeUs);
        w.I32(lastRttUs);
        return w.ToArray();
    }

    /// <summary>
    /// Pong: the ping's time, the server's game clock, the slots the next
    /// bundle waits for and the slots' round trips (<paramref name="rttMs"/>,
    /// <see cref="NetProtocol.MaxPlayers"/> of them, <see cref="NetProtocol.NoRtt"/> unknown).
    /// </summary>
    public static byte[] EncodePong(long clientTimeUs, long serverClockUs, byte waiting, ReadOnlySpan<ushort> rttMs)
    {
        if (rttMs.Length != NetProtocol.MaxPlayers)
            throw new ArgumentException("One round trip a slot.", nameof(rttMs));
        var w = new NetWriter(NetMessageType.Pong);
        w.I64(clientTimeUs);
        w.I64(serverClockUs);
        w.U8(waiting);
        foreach (ushort rtt in rttMs)
            w.U16(rtt);
        return w.ToArray();
    }
}

/// <summary>Writes one message (see the layout at the top of <c>NetMessages.cs</c>).</summary>
public sealed class NetWriter
{
    private byte[] _buffer = new byte[64];
    private int _length;

    /// <summary>A message of <paramref name="type"/>.</summary>
    public NetWriter(NetMessageType type) => U8((byte)type);

    /// <summary>The bytes written.</summary>
    public byte[] ToArray() => _buffer.AsSpan(0, _length).ToArray();

    private Span<byte> Take(int count)
    {
        if (_length + count > _buffer.Length)
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + count));
        Span<byte> span = _buffer.AsSpan(_length, count);
        _length += count;
        return span;
    }

    public void U8(byte value) => Take(1)[0] = value;

    public void I8(sbyte value) => Take(1)[0] = unchecked((byte)value);

    public void U16(ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Take(2), value);

    public void I16(short value) => BinaryPrimitives.WriteInt16LittleEndian(Take(2), value);

    public void U32(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Take(4), value);

    public void I32(int value) => BinaryPrimitives.WriteInt32LittleEndian(Take(4), value);

    public void U64(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Take(8), value);

    public void I64(long value) => BinaryPrimitives.WriteInt64LittleEndian(Take(8), value);

    /// <summary>A byte length and the UTF-8 bytes (at most 255).</summary>
    public void Text(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > byte.MaxValue)
            throw new ArgumentException("String too long for the protocol.", nameof(value));
        U8((byte)bytes.Length);
        bytes.CopyTo(Take(bytes.Length));
    }

    /// <summary>An MD5 given as 32 hex digits, as its 16 bytes.</summary>
    public void Md5(string hex)
    {
        byte[] bytes = Convert.FromHexString(hex);
        if (bytes.Length != 16)
            throw new ArgumentException($"Not an MD5: {hex}", nameof(hex));
        bytes.CopyTo(Take(16));
    }

    public void Ticcmd(in ticcmd_t cmd)
    {
        I8(cmd.forwardmove);
        I8(cmd.sidemove);
        I16(cmd.angleturn);
        I16(cmd.consistancy);
        U8(cmd.chatchar);
        U8(cmd.buttons);
    }

    /// <summary>The game settings (layout at the top of <c>NetMessages.cs</c>).</summary>
    public void Settings(NetGameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        SpawnSettings s = settings.Spawn;
        U8((byte)s.gamemode);
        U8((byte)s.gameskill);
        U8((byte)((s.netgame ? 1 : 0) | (s.nomonsters ? 2 : 0) | (s.respawnparm ? 4 : 0) | (s.fastparm ? 8 : 0)));
        U8(checked((byte)s.deathmatch));
        U8(s.gameversion is GameVersion v ? checked((byte)v) : byte.MaxValue);
        U8((byte)s.gamemission);
        U8(checked((byte)s.playersingame));
        U8(checked((byte)settings.Episode));
        U8(checked((byte)settings.Map));
        U16(checked((ushort)s.timelimit));
        Tweaks t = settings.Tweaks;
        U8((byte)((t.AbsoluteAiming ? 1 : 0) | (t.AbsoluteMovement ? 2 : 0) | (t.UseFallback ? 4 : 0) | (t.InstantStop ? 8 : 0)));
        U32(t.AimAssistCone);
    }
}

/// <summary>Reads one message; a short or malformed one throws <see cref="NetProtocolException"/>.</summary>
public sealed class NetReader
{
    private readonly ReadOnlyMemory<byte> _data;
    private int _position;

    /// <summary>A reader of <paramref name="data"/>, past its type byte (<see cref="Type"/>).</summary>
    public NetReader(ReadOnlyMemory<byte> data)
    {
        _data = data;
        if (data.Length == 0)
            throw new NetProtocolException("Empty message.");
        Type = (NetMessageType)U8();
    }

    /// <summary>The message's type.</summary>
    public NetMessageType Type { get; }

    private ReadOnlySpan<byte> Take(int count)
    {
        if (_position + count > _data.Length)
            throw new NetProtocolException($"{Type} message too short.");
        ReadOnlySpan<byte> span = _data.Span.Slice(_position, count);
        _position += count;
        return span;
    }

    /// <summary>Throws unless the whole message was read.</summary>
    public void End()
    {
        if (_position != _data.Length)
            throw new NetProtocolException($"{Type} message too long.");
    }

    public byte U8() => Take(1)[0];

    public sbyte I8() => unchecked((sbyte)Take(1)[0]);

    public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

    public short I16() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));

    public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

    public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

    public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));

    public long I64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));

    public string Text()
    {
        int length = U8();
        try
        {
            return new UTF8Encoding(false, true).GetString(Take(length));
        }
        catch (DecoderFallbackException e)
        {
            throw new NetProtocolException($"{Type}: bad UTF-8.", e);
        }
    }

#pragma warning disable CA1308 // MD5s are compared as lower-case hex, as TestWads and the IWAD menu print them.
    public string Md5() => Convert.ToHexString(Take(16)).ToLowerInvariant();
#pragma warning restore CA1308

    public ticcmd_t Ticcmd() => new()
    {
        forwardmove = I8(),
        sidemove = I8(),
        angleturn = I16(),
        consistancy = I16(),
        chatchar = U8(),
        buttons = U8(),
    };

    /// <summary>The game settings (layout at the top of <c>NetMessages.cs</c>).</summary>
    public NetGameSettings Settings()
    {
        var gamemode = Enum<GameMode>(U8());
        var skill = Enum<skill_t>(U8());
        int flags = U8();
        int deathmatch = U8();
        int version = U8();
        var mission = Enum<GameMission>(U8());
        int playersingame = U8();
        int episode = U8();
        int map = U8();
        int timer = U16();
        int tweakFlags = U8();
        uint cone = U32();
        if (flags > 15 || tweakFlags > 15 || deathmatch > 2)
            throw new NetProtocolException("Settings: unknown flags.");
        if (playersingame >= 1 << NetProtocol.MaxPlayers)
            throw new NetProtocolException($"Settings: players in game {playersingame}.");
        var spawn = new SpawnSettings(gamemode, skill, (flags & 1) != 0, deathmatch, (flags & 2) != 0,
            (flags & 4) != 0, (flags & 8) != 0, version == byte.MaxValue ? null : Enum<GameVersion>(version), mission,
            playersingame, timer);
        var tweaks = new Tweaks
        {
            AbsoluteAiming = (tweakFlags & 1) != 0,
            AbsoluteMovement = (tweakFlags & 2) != 0,
            UseFallback = (tweakFlags & 4) != 0,
            InstantStop = (tweakFlags & 8) != 0,
            AimAssistCone = cone,
        };
        return new NetGameSettings(spawn, tweaks, episode, map);
    }

    private T Enum<T>(int value)
        where T : struct, Enum
    {
        T result = (T)(object)value;
        if (!System.Enum.IsDefined(result))
            throw new NetProtocolException($"{Type}: {typeof(T).Name} {value}.");
        return result;
    }
}
