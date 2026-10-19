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
/// measured then give (<see cref="NetOptions.ChooseDelay"/>). Plain C#:
/// the game passes ENet transports (<c>NetEnetTransport</c>), the tests
/// <see cref="MemoryTransport"/>s. Driven by <see cref="Update"/>, from one thread.
/// </summary>
public sealed class NetSession : IDisposable
{
    private readonly INetTransport? _serverTransport;
    private readonly INetTransport _clientTransport;
    private readonly NetIdentity _identity;
    private readonly ulong _instance = NetDiscovery.RandomId();
    private string _iwadName = "";
    private NetClientState _lastState;
    private bool _readied;
    private long _allReadyUs = -1;

    /// <summary>
    /// How long the host waits, once everyone is ready, for
    /// <see cref="NetOptions.MinRttSamples"/> round trips from every client
    /// (T11.12a) before it starts with the round trips it has (µs).
    /// </summary>
    public const long RttWaitUs = 5_000_000;

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
        _identity = identity;
        if (options.Role == NetRole.Host)
        {
            _serverTransport = newTransport(NetTransportUse.Server);
            Server = new NetServer(_serverTransport, identity, settings, HostToken(), options.Players) { TimeoutUs = options.TimeoutUs }; // T11.11
            Server.Log += line => Log?.Invoke($"server: {line}");
            Server.Listen(options.Port);
            Port = _serverTransport is INetPortTransport { LocalPort: > 0 } p ? p.LocalPort : options.Port;
            Log?.Invoke($"listening on {options.BindAddress}:{Port}, waiting for {options.Players} player(s); addresses {string.Join(' ', HostAddresses(options.BindAddress, Port))}");
        }
        else
            Port = options.Port;
        _clientTransport = newTransport(NetTransportUse.Client);
        Client = new NetClient(_clientTransport, identity, Server?.HostToken ?? 0, options.PlayerName) { TimeoutUs = options.TimeoutUs }; // T11.11
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

    /// <summary>
    /// T11.8 (T11.1a): the host's check of the game before it starts, given
    /// the players' mask (bit i: slot i): why the game cannot start with
    /// them (a map lacking their starts), or null. Null: no check.
    /// </summary>
    public Func<byte, string?>? CheckStart { get; set; }

    /// <summary>T11.8 (T11.1a): why the host does not start the game with the players in the lobby (<see cref="CheckStart"/>), or null.</summary>
    public string? StartRefusal { get; private set; }

    private byte _checkedMask;

    /// <summary>T11.9: the players a game started from the lobby page needs (the host and one more).</summary>
    public const int MinPlayers = 2;

    /// <summary>
    /// T11.9: whether the host starts by itself once <see cref="NetOptions.Players"/>
    /// are in (the command line's <c>-players</c>); false for a game hosted
    /// from the menus, which the lobby page's Start begins (<see cref="RequestStart"/>).
    /// </summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>T11.9: the lobby page's Start was asked for and the host has not started yet (it may wait for the round trips).</summary>
    public bool StartRequested { get; private set; }

    /// <summary>T11.9: how many times the host refused to start (<see cref="StartRefusal"/>): the menus show each refusal once.</summary>
    public int StartRefusals { get; private set; }

    /// <summary>
    /// T11.9: the lobby page's Start (the host's): the game starts once every
    /// player in the lobby is ready and their round trips are known, if
    /// <see cref="MinPlayers"/> are in and <see cref="CheckStart"/> lets it;
    /// a refusal ends the request (<see cref="StartRefusals"/>). False on a
    /// join or after the game started.
    /// </summary>
    public bool RequestStart()
    {
        if (Server is not { Phase: NetServerPhase.Lobby } server || server.PlayerCount < MinPlayers)
            return false;
        StartRequested = true;
        _checkedMask = 0; // checked again at each request
        return true;
    }

    /// <summary>Why the session ended (refused, disconnected), or null while it goes on.</summary>
    public string? Failure { get; private set; }

    /// <summary>T11.9d: the host's LAN discovery responder (<see cref="AnswerDiscovery"/>), or null (a join, not asked, or its port taken).</summary>
    public NetDiscoveryResponder? Discovery { get; private set; }

    /// <summary>T11.9d: why the host is not visible on the LAN (its discovery port taken), or null.</summary>
    public string? DiscoveryError { get; private set; }

    /// <summary>
    /// T11.9d: the host answers LAN discovery queries on <paramref name="port"/>
    /// (<see cref="NetDiscovery.Port"/>; 0 any free one, the tests' and the
    /// level check's) until the session ends, in the lobby and after the
    /// Start, bound on the game's address (<c>-bind 127.0.0.1</c>: loopback
    /// only), its IWAD shown as <paramref name="iwadName"/>. A port that is
    /// taken is logged and the session goes on, not visible on the LAN.
    /// </summary>
    public void AnswerDiscovery(string iwadName, int port = NetDiscovery.Port)
    {
        ArgumentNullException.ThrowIfNull(iwadName);
        if (Server is null || Discovery is not null)
            return;
        _iwadName = iwadName;
        string bind = Options.BindAddress;
        System.Net.IPAddress? address = bind is "*" or "0.0.0.0" or "::" ? System.Net.IPAddress.Any
            : System.Net.IPAddress.TryParse(bind, out System.Net.IPAddress? a) && a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? a
            : null;
        if (address is null)
        {
            DiscoveryError = $"{bind} is not an IPv4 address";
            Log?.Invoke($"not visible on the LAN: {DiscoveryError}");
            return;
        }
        try
        {
            Discovery = new NetDiscoveryResponder(address, port, DiscoveryState);
            Log?.Invoke($"visible on the LAN: discovery on {(address.Equals(System.Net.IPAddress.Any) ? "*" : bind)}:{Discovery.Port}");
        }
        catch (System.Net.Sockets.SocketException e)
        {
            DiscoveryError = $"port {port}: {e.Message}";
            Log?.Invoke($"not visible on the LAN: port {port} {e.SocketErrorCode}: {e.Message}");
        }
    }

    /// <summary>T11.9d: the discovery reply (but its nonce) for the game as it is now.</summary>
    private NetDiscoveryReply DiscoveryState()
    {
        NetServer server = Server!;
        NetGameSettings settings = server.Settings;
        string name = server.NameOf(0) is { Length: > 0 } n ? n : NetProtocol.CleanName(Options.PlayerName, 0);
        return new NetDiscoveryReply(0, _instance, NetProtocol.Version, _identity.Build, _identity.IwadMd5, _iwadName,
            _identity.PwadMd5s.Count, NetDiscovery.PwadHash(_identity.PwadMd5s), name, Port, server.Phase != NetServerPhase.Lobby,
            server.PlayerCount, Options.Players, settings.Spawn.deathmatch, MapName(settings), (int)settings.Spawn.gameskill + 1);
    }

    /// <summary>
    /// T11.9d: where the others reach a host listening on <paramref name="bind"/>:<paramref name="port"/>,
    /// as the lobby page shows it: the first two LAN addresses (up, not
    /// loopback, not link-local) for every interface, else 127.0.0.1; the
    /// bound address itself for one.
    /// </summary>
    public static List<string> HostAddresses(string bind, int port)
    {
        var list = new List<string>();
        if (bind is "*" or "0.0.0.0" or "::")
        {
            foreach ((System.Net.IPAddress address, System.Net.IPAddress _) in NetDiscovery.LanAddresses())
            {
                string a = $"{address}:{port}";
                if (list.Count < 2 && !list.Contains(a))
                    list.Add(a);
            }
            if (list.Count == 0)
                list.Add($"127.0.0.1:{port}");
        }
        else
            list.Add(bind.Contains(':', StringComparison.Ordinal) ? $"[{bind}]:{port}" : $"{bind}:{port}");
        return list;
    }

    /// <summary>
    /// T11.12d: the transport serviced again within the frame, after the
    /// local commands were built and flushed and before the tics are taken:
    /// the host's server takes its own client's command (over ENet loopback)
    /// and every command that came during the frame and sends the bundles
    /// they complete, and the client takes the bundles that came, so a tic
    /// completed in this frame runs in it. Only the protocol's polls (no
    /// lobby logic: <see cref="Update"/> follows next frame).
    /// </summary>
    public void Service(long nowUs)
    {
        Server?.Update(nowUs);
        Client.Update(nowUs);
    }

    /// <summary>Poll the server and the client at <paramref name="nowUs"/> (µs, this machine's clock); start the game when it is time.</summary>
    public void Update(long nowUs)
    {
        Server?.Update(nowUs);
        Discovery?.Update(nowUs); // T11.9d
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
        if (StartRequested && server.PlayerCount < MinPlayers)
            StartRequested = false; // T11.9: a player left meanwhile
        if (!server.CanStart || (AutoStart ? server.PlayerCount < Options.Players : !StartRequested))
        {
            _allReadyUs = -1;
            return;
        }
        if (CheckStart is { } check)
        {
            // T11.1a (T11.8): the game's maps must have a start for every player in it; checked again when the players change
            if (server.OccupiedMask != _checkedMask)
            {
                _checkedMask = server.OccupiedMask;
                StartRefusal = check(_checkedMask);
                if (StartRefusal is not null)
                {
                    Log?.Invoke($"the game cannot start: {StartRefusal}");
                    StartRefusals++;
                    StartRequested = false; // T11.9: the lobby page shows it; Start asks again
                }
            }
            if (StartRefusal is not null)
                return;
        }
        if (_allReadyUs < 0)
            _allReadyUs = nowUs;
        int delay;
        if (Options.Delay is int fixedDelay)
            delay = fixedDelay;
        else
        {
            // T11.12a: from every player's recent round trips (at least MinRttSamples each, or what came in RttWaitUs)
            if (NetOptions.ChooseDelay(server, nowUs - _allReadyUs >= RttWaitUs) is not { } choice)
                return;
            delay = choice.Delay;
            Log?.Invoke(choice.ToString());
        }
        server.Start(delay);
        StartRequested = false;
    }

    /// <summary>
    /// T11.9: the players in the lobby (or the game) as this machine knows
    /// them, by slot: whether each is ready and its round trip (µs; −1
    /// unknown): the host's server's, or on a join its own and the others'
    /// from the host's pong (T11.11; whole milliseconds).
    /// </summary>
    public List<NetLobbyPlayer> LobbyPlayers()
    {
        var players = new List<NetLobbyPlayer>();
        byte occupied, ready;
        if (Server is { } server)
            (occupied, ready) = server.Phase == NetServerPhase.Lobby ? (server.OccupiedMask, server.ReadyMask) : (Client.Start?.PlayerMask ?? server.OccupiedMask, (byte)0xff);
        else
            (occupied, ready) = Client.Start is { } start ? (start.PlayerMask, (byte)0xff) : (Client.LobbyOccupied, Client.LobbyReady);
        for (int slot = 0; slot < NetProtocol.MaxPlayers; slot++)
        {
            if ((occupied & (1 << slot)) == 0)
                continue;
            // T11.11's pong carries every player's round trip (ms): a join shows the others' too
            int rtt = Server is { } s ? s.RttUsOf(slot)
                : slot == Client.Slot ? Client.RttUs
                : Client.PlayerRttMs[slot] is var ms && ms != NetProtocol.NoRtt ? ms * 1000 : -1;
            // T11.9c: the names the server cleaned (a join's from the lobby message, kept through the game)
            string name = Server is { } host ? host.NameOf(slot) : Client.LobbyNames[slot];
            players.Add(new NetLobbyPlayer(slot, (ready & (1 << slot)) != 0, rtt, slot == Client.Slot,
                name.Length > 0 ? name : NetProtocol.CleanName(null, slot)));
        }
        return players;
    }

    /// <summary>
    /// T11.11: the overlay's (F3) <c>net:</c> line: this machine's player
    /// (slot), the round trip of each player in the game as the host last
    /// reported them, the input delay, the tics waiting (bundles come and not
    /// run yet), the stalls that began in the last second (the clock past the
    /// last bundle: the world held), how late the bundle is now and whom the
    /// host waits for, the players who left (and at which tic), the desync.
    /// </summary>
    public string OverlayText()
    {
        NetClient c = Client;
        var s = new System.Text.StringBuilder("net: ");
        s.Append(Options.Role == NetRole.Host ? "host, " : "join, ");
        if (c.State != NetClientState.InGame || c.Start is not { } start)
        {
            s.Append(System.Globalization.CultureInfo.InvariantCulture, $"{c.State}{(c.Slot >= 0 ? $", slot {c.Slot}" : "")}, ping {Ms(c.RttUs)}");
            if (Failure is not null)
                s.Append("; ").Append(Failure);
            return s.ToString();
        }
        s.Append(System.Globalization.CultureInfo.InvariantCulture, $"player {c.Slot + 1}, ping");
        for (int i = 0; i < NetProtocol.MaxPlayers; i++)
        {
            if ((start.PlayerMask & (1 << i)) == 0)
                continue;
            ushort rtt = c.PlayerRttMs[i];
            string ping = c.QuitTics[i] != int.MaxValue ? $"left at tic {c.QuitTics[i]}"
                : i == c.Slot && c.RttUs >= 0 ? Ms(c.RttUs)
                : rtt == NetProtocol.NoRtt ? "-"
                : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{rtt} ms");
            s.Append(System.Globalization.CultureInfo.InvariantCulture, $" {i + 1}: {ping}");
        }
        s.Append(System.Globalization.CultureInfo.InvariantCulture, $", delay {start.Delay}, tics waiting {Math.Max(0, c.BundleTic - c.LastRanTic)}, stalls {c.StallsLastSecond} in the last second");
        if (c.LateUs > 0)
            s.Append(System.Globalization.CultureInfo.InvariantCulture, $", late {c.LateUs / 1000} ms ({WaitingText(c.WaitingFor, c.Slot).TrimEnd('.')})");
        if ((Server?.Desync ?? c.Desync) is { } desync)
            s.Append(System.Globalization.CultureInfo.InvariantCulture, $", DESYNC in tics {desync.FirstTic}-{desync.LastTic}: stopped after tic {c.LastRanTic}");
        return s.ToString();
    }

    private static string Ms(int us) => us < 0 ? "-" : (us / 1000.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " ms";

    /// <summary>T11.11: "Waiting for player 2...", "players 2 and 3", or the host when no player but <paramref name="self"/> is named.</summary>
    public static string WaitingText(byte waiting, int self)
    {
        var players = new List<string>();
        for (int i = 0; i < NetProtocol.MaxPlayers; i++)
        {
            if ((waiting & (1 << i)) != 0 && i != self)
                players.Add((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        return players.Count switch
        {
            0 => "Waiting for the host...",
            1 => $"Waiting for player {players[0]}...",
            _ => $"Waiting for players {string.Join(", ", players.GetRange(0, players.Count - 1))} and {players[^1]}...",
        };
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
        Discovery?.Dispose();
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

    /// <summary>The map a netgame's settings start on (<c>ExMy</c>, or <c>MAPxx</c> in Doom II).</summary>
    public static string MapName(NetGameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Spawn.gamemode == IsoDoom.Wad.GameMode.commercial
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"MAP{settings.Map:00}")
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"E{settings.Episode}M{settings.Map}");
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

/// <summary>T11.9: a player in the lobby: its slot, whether it is ready, its round trip (µs, −1 unknown), whether it is this machine's, and (T11.9c) its name.</summary>
public readonly record struct NetLobbyPlayer(int Slot, bool Ready, int RttUs, bool Local, string Name = "");

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

    /// <summary>
    /// One frame: the commands due sent, the bundles due taken (after
    /// <see cref="NetClient.Update"/>); T11.12d: <paramref name="service"/>
    /// (<see cref="NetSession.Service"/>) runs between, as the game's frame does.
    /// </summary>
    public void Frame(Action? service = null)
    {
        if (_client.State != NetClientState.InGame)
            return;
        while (_client.CommandsDue > 0)
            _client.SendCommand(Script(_client.Slot, _client.MakeTic));
        _client.Flush();
        service?.Invoke();
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
