using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using IsoDoom.Net;
using IsoDoom.Sim;

namespace IsoDoom.Game;

/// <summary>A transport that can say which port it listens on (ENet's, for <c>-host 0</c>).</summary>
public interface INetPortTransport
{
    /// <summary>The port listened on, or 0.</summary>
    int LocalPort { get; }
}

/// <summary>What a transport made by a <see cref="NetSession"/>'s factory is for.</summary>
public enum NetTransportUse
{
    /// <summary>The host's server: it listens.</summary>
    Server,

    /// <summary>A player's client: it connects (the host's own too, to its server).</summary>
    Client,
}

/// <summary>
/// A machine's netgame (T11.6) from its <see cref="NetOptions"/>: on the
/// host, a <see cref="NetServer"/> listening and the host's own
/// <see cref="NetClient"/> connected to it (slot 0, by the host token; over
/// the transport like the others: SPEC §12 T11.6); on a join, a
/// <see cref="NetClient"/> connected to the host. With no lobby page yet
/// (T11.9), every client says it is ready as soon as it is in the lobby, and
/// the host starts once <see cref="NetOptions.Players"/> players are in and
/// ready, with <see cref="NetOptions.Delay"/> or the delay the round trips
/// measured then give (<see cref="NetOptions.DelayFromRtt"/>). Plain C#:
/// the game passes ENet transports (<c>NetEnetTransport</c>), the tests
/// <see cref="MemoryTransport"/>s. Driven by <see cref="Update"/>, from one thread.
/// </summary>
public sealed class NetSession : IDisposable
{
    private readonly INetTransport? _serverTransport;
    private readonly INetTransport _clientTransport;
    private NetClientState _lastState;
    private bool _readied;
    private long _allReadyUs = -1;

    /// <summary>
    /// How long the host waits, once everyone is ready, for a round trip from
    /// every client before it starts with the round trips it has (µs).
    /// </summary>
    public const long RttWaitUs = 3_000_000;

    /// <summary>A session for <paramref name="options"/> (host or join).</summary>
    /// <param name="options">The arguments (<see cref="NetRole.None"/> is refused).</param>
    /// <param name="identity">This machine's build and WADs.</param>
    /// <param name="settings">The game the host starts (a join's comes from the host).</param>
    /// <param name="newTransport">Makes the transports.</param>
    /// <param name="log">A line for each event, for the log.</param>
    public NetSession(NetOptions options, NetIdentity identity, NetGameSettings settings, Func<NetTransportUse, INetTransport> newTransport,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(newTransport);
        if (options.Role == NetRole.None)
            throw new ArgumentException("A session hosts or joins.", nameof(options));
        Options = options;
        Log = log;
        if (options.Role == NetRole.Host)
        {
            _serverTransport = newTransport(NetTransportUse.Server);
            Server = new NetServer(_serverTransport, identity, settings, HostToken(), options.Players);
            Server.Log += line => Log?.Invoke($"server: {line}");
            Server.Listen(options.Port);
            Port = _serverTransport is INetPortTransport { LocalPort: > 0 } p ? p.LocalPort : options.Port;
            Log?.Invoke($"listening on {options.BindAddress}:{Port}, waiting for {options.Players} player(s)");
        }
        else
            Port = options.Port;
        _clientTransport = newTransport(NetTransportUse.Client);
        Client = new NetClient(_clientTransport, identity, Server?.HostToken ?? 0);
        Client.Log += line => Log?.Invoke($"client: {line}");
        Address = options.Role == NetRole.Host ? OwnAddress(options.BindAddress) : options.ConnectAddress!;
        Client.Connect(Address, Port);
        _lastState = Client.State;
    }

    /// <summary>The arguments.</summary>
    public NetOptions Options { get; }

    /// <summary>The server (the host's), or null on a join.</summary>
    public NetServer? Server { get; }

    /// <summary>This machine's client.</summary>
    public NetClient Client { get; }

    /// <summary>The address the client connected to.</summary>
    public string Address { get; }

    /// <summary>The port the host listens on (the one picked for <c>-host 0</c>), and the client connects to.</summary>
    public int Port { get; }

    /// <summary>A line for each event.</summary>
    public Action<string>? Log { get; set; }

    /// <summary>Why the session ended (refused, disconnected), or null while it goes on.</summary>
    public string? Failure { get; private set; }

    /// <summary>Poll the server and the client at <paramref name="nowUs"/> (µs, this machine's clock); start the game when it is time.</summary>
    public void Update(long nowUs)
    {
        Server?.Update(nowUs);
        Client.Update(nowUs);
        if (Client.State != _lastState)
        {
            _lastState = Client.State;
            Log?.Invoke($"client: {Client.State}{(Client.Slot >= 0 ? $", slot {Client.Slot}" : "")}");
            if (Client.State == NetClientState.Refused)
                Failure = $"refused: {Client.Refusal}";
            else if (Client.State == NetClientState.Disconnected && Failure is null)
                Failure = Client.Error is { } error ? $"disconnected: {error}" : "disconnected";
        }
        if (Client.State == NetClientState.Lobby && !_readied)
        {
            Client.SetReady(true); // no lobby page yet (T11.9): ready on joining
            _readied = true;
        }
        if (Server is { Phase: NetServerPhase.Lobby } server)
            StartWhenReady(server, nowUs);
    }

    /// <summary>The host's start: <see cref="NetOptions.Players"/> in and ready, and their round trips known (or waited for long enough).</summary>
    private void StartWhenReady(NetServer server, long nowUs)
    {
        if (!server.CanStart || server.PlayerCount < Options.Players)
        {
            _allReadyUs = -1;
            return;
        }
        if (_allReadyUs < 0)
            _allReadyUs = nowUs;
        int delay;
        if (Options.Delay is int fixedDelay)
            delay = fixedDelay;
        else
        {
            int maxRtt = 0;
            bool known = true;
            for (int slot = 0; slot < NetProtocol.MaxPlayers; slot++)
            {
                if ((server.OccupiedMask & (1 << slot)) == 0)
                    continue;
                int rtt = server.RttUsOf(slot);
                known &= rtt >= 0;
                maxRtt = Math.Max(maxRtt, rtt);
            }
            if (!known && nowUs - _allReadyUs < RttWaitUs)
                return;
            delay = NetOptions.DelayFromRtt(maxRtt);
            Log?.Invoke($"round trips: slowest {maxRtt / 1000.0:F1} ms{(known ? "" : " (some unknown)")}: delay {delay} tic(s)");
        }
        server.Start(delay);
    }

    /// <summary>The host's own client connects to its server here: loopback when it listens on every interface.</summary>
    private static string OwnAddress(string bind) => bind is "*" or "0.0.0.0" ? "127.0.0.1" : bind == "::" ? "::1" : bind;

    private static ulong HostToken()
    {
        Span<byte> bytes = stackalloc byte[8];
        ulong token;
        do
        {
            RandomNumberGenerator.Fill(bytes);
            token = BitConverter.ToUInt64(bytes);
        }
        while (token == 0);
        return token;
    }

    /// <summary>Leave the game (the client quits), then close the transports (ENet's disconnects go out).</summary>
    public void Dispose()
    {
        if (Client.State is not (NetClientState.Disconnected or NetClientState.Refused or NetClientState.Idle))
            Client.Quit();
        _clientTransport.Dispose();
        _serverTransport?.Dispose();
    }

    /// <summary>
    /// The build stamp the hello compares (SPEC §12 T11.6): a release's
    /// version, or for an unstamped <c>dev</c> build, <c>dev-</c> and the
    /// sim's and the protocol's assembly MVIDs (a deterministic build's hash
    /// of their code), so two dev builds play together only with the same
    /// sim and protocol.
    /// </summary>
    public static string BuildStamp(string version)
    {
        if (version != "dev")
            return version;
        Guid sim = typeof(World).Assembly.ManifestModule.ModuleVersionId;
        Guid net = typeof(NetClient).Assembly.ManifestModule.ModuleVersionId;
        return $"dev-{sim.ToString("N")[..8]}{net.ToString("N")[..8]}";
    }

    /// <summary>The MD5 of a file, as 32 lowercase hex digits (the IWAD's and PWADs' identity).</summary>
    [SuppressMessage("Security", "CA5351", Justification = "MD5 names the WAD, as the Doom community lists it, not a security check")]
    public static string FileMd5(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(MD5.HashData(stream));
    }

    /// <summary>The identity of a machine running <paramref name="version"/> on these WADs.</summary>
    public static NetIdentity Identity(string version, string iwadPath, IReadOnlyList<string> pwadPaths)
    {
        ArgumentNullException.ThrowIfNull(pwadPaths);
        var pwads = new List<string>(pwadPaths.Count);
        foreach (string pwad in pwadPaths)
            pwads.Add(FileMd5(pwad));
        return new NetIdentity(BuildStamp(version), FileMd5(iwadPath), pwads);
    }

    /// <summary>
    /// The host's game: <paramref name="spawn"/> as a netgame with the
    /// options' modes, the first map named <paramref name="map"/>
    /// (<c>ExMy</c> or <c>MAPxx</c>; null: episode 1, map 1).
    /// </summary>
    public static NetGameSettings GameSettings(NetOptions options, SpawnSettings spawn, Tweaks tweaks, string? map)
    {
        ArgumentNullException.ThrowIfNull(options);
        spawn = options.Apply(spawn with { netgame = true, deathmatch = options.Deathmatch, timelimit = options.TimerMinutes });
        (int episode, int number) = MapNumbers(map);
        return new NetGameSettings(spawn, tweaks, episode, number);
    }

    /// <summary><c>ExMy</c> as (x, y), <c>MAPxx</c> as (1, xx), anything else (1, 1).</summary>
    public static (int Episode, int Map) MapNumbers(string? map)
    {
        if (map is { Length: 4 } && (map[0] is 'E' or 'e') && (map[2] is 'M' or 'm') && char.IsAsciiDigit(map[1]) && char.IsAsciiDigit(map[3]))
            return (map[1] - '0', map[3] - '0');
        if (map is { Length: 5 } && map.StartsWith("MAP", StringComparison.OrdinalIgnoreCase) && char.IsAsciiDigit(map[3]) && char.IsAsciiDigit(map[4]))
            return (1, (map[3] - '0') * 10 + map[4] - '0');
        return (1, 1);
    }
}

/// <summary>
/// T11.6's exchange without a game (the level script's <c>netexchange</c>;
/// T11.8 runs the tics instead): each frame this machine sends a scripted
/// command for every tic due and takes every bundle that is due, without
/// running it: a hash of the bundles (FNV-1a over each tic's number, players
/// and commands) stands in for <c>World.Checksum()</c> in the checksum
/// reports, and its bytes for each player's consistancy, so the server's
/// window comparison checks that every machine got the same commands.
/// </summary>
public sealed class NetExchange
{
    private readonly NetClient _client;
    private readonly ticcmd_t[] _cmds = new ticcmd_t[NetProtocol.MaxPlayers];
    private readonly short[] _consistancy = new short[NetProtocol.MaxPlayers];

    /// <summary>An exchange of <paramref name="tics"/> tics (rounded up to whole checksum windows) through <paramref name="client"/>.</summary>
    public NetExchange(NetClient client, int tics)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentOutOfRangeException.ThrowIfLessThan(tics, 1);
        _client = client;
        Tics = (tics + NetProtocol.WindowTics - 1) / NetProtocol.WindowTics * NetProtocol.WindowTics;
    }

    /// <summary>The tics to take (whole windows).</summary>
    public int Tics { get; }

    /// <summary>The checksum windows they make.</summary>
    public int Windows => Tics / NetProtocol.WindowTics;

    /// <summary>The tics taken.</summary>
    public int TicsTaken { get; private set; }

    /// <summary>The bundles' hash so far.</summary>
    public ulong Hash { get; private set; } = 14695981039346656037UL;

    /// <summary>Whether every tic was taken.</summary>
    public bool Done => TicsTaken >= Tics;

    /// <summary>The command <paramref name="slot"/> sends for <paramref name="tic"/>: a walk, turn and buttons from a hash of both, so every run is the same.</summary>
    public static ticcmd_t Script(int slot, int tic)
    {
        uint h = (uint)(slot * 0x9E3779B1) ^ (uint)tic * 0x85EBCA6B;
        h ^= h >> 15;
        h *= 0xC2B2AE35;
        h ^= h >> 13;
        return new ticcmd_t
        {
            forwardmove = (sbyte)((int)(h % 101) - 50),
            sidemove = (sbyte)((int)((h >> 8) % 81) - 40),
            angleturn = (short)(h >> 16),
            buttons = (byte)((h >> 3) % 7 == 0 ? buttoncode_t.BT_ATTACK : 0),
        };
    }

    /// <summary>One frame: the commands due sent, the bundles due taken (after <see cref="NetClient.Update"/>).</summary>
    public void Frame()
    {
        if (_client.State != NetClientState.InGame)
            return;
        while (_client.CommandsDue > 0)
            _client.SendCommand(Script(_client.Slot, _client.MakeTic));
        _client.Flush();
        while (!Done && _client.TryTakeTic(_cmds, out int tic, out byte mask))
        {
            ulong h = Hash;
            h = Mix(h, (uint)tic);
            h = Mix(h, mask);
            for (int i = 0; i < NetProtocol.MaxPlayers; i++)
            {
                if ((mask & (1 << i)) == 0)
                    continue;
                ticcmd_t c = _cmds[i];
                h = Mix(h, (byte)c.forwardmove);
                h = Mix(h, (byte)c.sidemove);
                h = Mix(h, (ushort)c.angleturn);
                h = Mix(h, c.buttons);
                h = Mix(h, c.chatchar);
            }
            Hash = h;
            for (int i = 0; i < NetProtocol.MaxPlayers; i++)
                _consistancy[i] = (mask & (1 << i)) != 0 ? unchecked((short)(h >> (16 * i))) : (short)0;
            _client.TicRan(tic, h, _consistancy);
            TicsTaken++;
        }
    }

    private static ulong Mix(ulong h, uint value)
    {
        for (int i = 0; i < 4; i++)
        {
            h = (h ^ (byte)value) * 1099511628211UL;
            value >>= 8;
        }
        return h;
    }
}
