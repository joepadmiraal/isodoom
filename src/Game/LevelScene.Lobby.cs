using System;
using System.Globalization;
using System.IO;
using Godot;
using IsoDoom.Net;
using IsoDoom.Sim;

namespace IsoDoom.Game;

// T11.9: the multiplayer pages' host (MMenu.Net.cs): Host Game makes the
// session from the net/* settings (T11.9f: and the command line's -netdelay,
// -nettimeout and -bind; Join Game its -nettimeout) (no -players start: the lobby page's Start
// begins the game), Join Game from an address (kept in net/addresses once the
// host let it in), the lobby page reads the session, Escape leaves it.
public partial class LevelScene : INetMenuHost
{
    // T11.9: the session came from the menus (a failure ends it), and the address a join made from them connects to
    private bool _netFromMenus;
    private string? _joiningAddress;
    private NetIdentity? _netIdentity;

    /// <summary>T11.9d: the port a host's session answers LAN discovery on (<see cref="NetDiscovery.Port"/>; the level check's 0, a free one).</summary>
    internal int DiscoveryPort { get; set; } = NetDiscovery.Port;

    /// <summary>T11.9d: where the Join Game page's browser queries (null: <see cref="NetDiscovery.DefaultTargets"/>; the level check's own hosts).</summary>
    internal System.Collections.Generic.IReadOnlyList<System.Net.IPEndPoint>? DiscoveryTargets { get; set; }

    /// <summary>T11.9d: this machine's identity (the build and WADs' MD5s), read once.</summary>
    private NetIdentity NetIdentity() => _netIdentity ??= NetSession.Identity(Main.Version, _iwadPath!, _pwadPaths);

    /// <summary>T11.9d: the IWAD's file name as a host's discovery reply shows it.</summary>
    private string IwadName() => _iwadPath is null ? "" : Path.GetFileName(_iwadPath).ToUpperInvariant();

    /// <inheritdoc/>
    NetDiscoveryBrowser? INetMenuHost.OpenBrowser()
    {
        if (_iwadPath is null)
            return null;
        try
        {
            return new NetDiscoveryBrowser(NetIdentity(), DiscoveryTargets);
        }
        catch (System.Net.Sockets.SocketException e)
        {
            GD.PrintErr($"Net: no LAN search: {e.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    string? INetMenuHost.HostGame()
    {
        if (Net is not null)
            return "a netgame is set up already";
        if (_iwadPath is null)
            return "no iwad";
        int Number(string key) => int.Parse(GetSetting(key), NumberStyles.None, CultureInfo.InvariantCulture);
        string timer = GetSetting("net/timer");
        var options = new NetOptions
        {
            Role = NetRole.Host,
            Port = Number("net/port"),
            Players = Number("net/players"),
            Deathmatch = GetSetting("net/mode") switch { "deathmatch" => 1, "altdeath" => 2, _ => 0 },
            TimerMinutes = timer == "off" ? 0 : int.Parse(timer, NumberStyles.None, CultureInfo.InvariantCulture),
            Respawn = GetSetting("net/respawn") == "on",
            Fast = GetSetting("net/fast") == "on",
            NoMonsters = GetSetting("net/monsters") == "off",
        };
        var spawn = new SpawnSettings(GameMode, (skill_t)(Number("net/skill") - 1), gameversion: GameVersion, gamemission: GameMission);
        return OpenMenuSession(options, spawn, Menu.HostMap());
    }

    /// <inheritdoc/>
    string? INetMenuHost.JoinGame(string address)
    {
        if (Net is not null)
            return "a netgame is set up already";
        if (_iwadPath is null)
            return "no iwad";
        NetOptions options;
        try
        {
            options = NetOptions.Parse(["-connect", address])!; // T11.9f: -nettimeout from the command line (OpenMenuSession)
        }
        catch (ArgumentException e)
        {
            return e.Message;
        }
        string? error = OpenMenuSession(options, new SpawnSettings(GameMode, Skill, gameversion: GameVersion, gamemission: GameMission), DefaultMap());
        _joiningAddress = error is null ? address : null;
        return error;
    }

    /// <summary>A session from the menus (not started by itself: the host's Start), or why it could not be made.</summary>
    private string? OpenMenuSession(NetOptions options, SpawnSettings spawn, string map)
    {
        try
        {
            // T11.9f: the command line's session arguments that are not the net/* settings: -netdelay, -nettimeout, -bind
            NetSessionArgs overrides = NetOptions.Overrides(WadLocator.UserArgs);
            options = overrides.Apply(options);
            NetSession session = OpenNetSession(options, spawn, map);
            session.AutoStart = false; // T11.9: the lobby page's Start
            _netFromMenus = true;
            string given = overrides.ToString();
            GD.Print($"Net: from the menus: {options.Role}{(options.Role == NetRole.Host ? $", {map}, {options.Players} players at most" : $" {options.ConnectAddress}:{options.Port}")}"
                + (given.Length > 0 ? $"; the command line's {given}" : ""));
            return null;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            GD.PrintErr($"Net: from the menus: {e.Message}");
            LeaveNetGame();
            return e.Message.StartsWith("ENet: ", StringComparison.Ordinal) ? e.Message["ENet: ".Length..] : e.Message;
        }
    }

    /// <inheritdoc/>
    void INetMenuHost.LeaveGame() => LeaveNetGame();

    /// <summary>T11.9: the lobby left (or a session from the menus ended before its game): the session closes, the game is single player again.</summary>
    public void LeaveNetGame()
    {
        if (NetGame is not null)
            return; // a game in progress is T11.11's
        if (Net is not null)
            GD.Print("Net: left the lobby");
        StopNet();
        Flow.netsession = false;
        _netFromMenus = false;
        _joiningAddress = null;
    }

    /// <inheritdoc/>
    bool INetMenuHost.StartGame()
    {
        bool asked = Net?.RequestStart() == true;
        GD.Print(asked ? "Net: the host's Start" : "Net: the host's Start: not yet (fewer than 2 players)");
        return asked;
    }

    /// <inheritdoc/>
    NetLobbyView? INetMenuHost.Lobby
    {
        get
        {
            if (Net is not { } net)
                return null;
            NetClient c = net.Client;
            NetGameSettings? settings = net.Server?.Settings ?? c.Settings;
            string wad = _iwadPath is null ? "" : Path.GetFileName(_iwadPath);
            if (_pwadPaths.Count > 0)
                wad += " + " + string.Join(' ', System.Linq.Enumerable.Select(_pwadPaths, Path.GetFileName));
            int maxPlayers = net.Server is not null ? net.Options.Players : c.MaxPlayers > 0 ? c.MaxPlayers : NetProtocol.MaxPlayers;
            return new NetLobbyView(net.Server is not null, _netFromMenus, net.Address, net.Port, c.State, maxPlayers, net.LobbyPlayers(),
                settings, settings is null ? null : MapName(settings), wad, net.Failure, c.Refusal, net.StartRefusal, net.StartRefusals,
                net.StartRequested, NetGame is not null || c.State == NetClientState.InGame,
                net.Server is not null ? NetSession.HostAddresses(net.Options.BindAddress, net.Port) : null, net.DiscoveryError, net.StartWarning);
        }
    }

    /// <summary>
    /// T11.9 (the level check): a session of the check's own over ENet
    /// loopback, another machine in this process: this machine's WADs, or
    /// with <paramref name="otherIwad"/> another IWAD's MD5 (refused);
    /// T11.9c: its player named <paramref name="name"/>.
    /// </summary>
    internal NetSession CheckSession(NetOptions options, bool otherIwad = false, string name = "CHECK", bool discovery = false)
    {
        options = options with { PlayerName = name };
        NetIdentity identity = NetIdentity();
        if (otherIwad)
            identity = identity with { IwadMd5 = new string('0', 32) };
        var spawn = new SpawnSettings(GameMode, Skill, gameversion: GameVersion, gamemission: GameMission);
        NetGameSettings settings = NetSession.GameSettings(options, spawn, Tweaks, DefaultMap());
        var made = new System.Collections.Generic.List<NetEnetTransport>();
        try
        {
            var session = new NetSession(options with { BindAddress = "127.0.0.1" }, identity, settings, use =>
            {
                var transport = new NetEnetTransport(options.Players) { BindAddress = use == NetTransportUse.Server ? "127.0.0.1" : "*" };
                made.Add(transport);
                return transport;
            }, line => GD.Print($"Level check: net: {line}"));
            if (discovery)
                session.AnswerDiscovery(otherIwad ? "OTHER.WAD" : IwadName(), 0); // T11.9d: on a free port of its own
            return session;
        }
        catch
        {
            foreach (NetEnetTransport transport in made)
                transport.Dispose();
            throw;
        }
    }

    /// <summary>T11.9 (the level check): the netgame the scene began is dropped (its session closed) and the title loop shows.</summary>
    /// <summary>T11.1b (the level check, whose frames run no tics): one frame of the netgame's game loop (<see cref="RunNetTics"/>).</summary>
    internal void NetTicsForCheck()
    {
        if (NetPlaying)
            RunNetTics(TicSeconds);
    }

    internal void EndNetGameForCheck()
    {
        StopNet();
        NetGame = null;
        LastNetChecksum = null;
        Flow.netsession = false;
        _netFromMenus = false;
        _joiningAddress = null;
        Flow.D_StartTitle(null);
    }

    /// <summary>T11.9: each frame after the session's poll: a join's address kept once the host let it in; the menus follow the session (<see cref="MMenu.NetTicker"/>).</summary>
    private void NetMenuFrame()
    {
        if (_flow is null)
            return;
        if (_joiningAddress is { } address && Net?.Client.State is NetClientState.Lobby or NetClientState.InGame)
        {
            _joiningAddress = null;
            SetSetting("net/addresses", Settings.RememberAddress(GetSetting("net/addresses"), address));
        }
        Menu.NetTicker();
    }
}
