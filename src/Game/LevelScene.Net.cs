using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using IsoDoom.Net;
using IsoDoom.Sim;

namespace IsoDoom.Game;

/// <summary>
/// T11.6: the netgame's session from the command line (<see cref="NetOptions"/>:
/// <c>-host</c>/<c>-server</c>, <c>-connect</c>, <c>-players</c>,
/// <c>-netdelay</c>, …) over ENet (<see cref="NetEnetTransport"/>), polled
/// once a frame on the main thread at the start of <see cref="_Process"/>
/// (<see cref="PollNet"/>) and ended with the scene (<see cref="StopNet"/>).
/// T11.8, the lockstep game loop: until the Start the scene shows the title
/// loop; at the Start it begins the game the Start describes
/// (<see cref="StartNetGame"/>: its settings and tweaks, its first map, the
/// slot the local player), and from then on its tics come from the network
/// (<see cref="RunNetTics"/>): each frame the local commands due are built and
/// sent (from the input, or the level script's queue), and every tic whose
/// bundle came and whose time the game clock reached runs with every
/// player's command, at most <see cref="MaxTicsPerFrame"/> a frame; while
/// one is late the world holds on its last tic. The level script's
/// <c>netexchange TICS</c> exchanges commands without a game instead (T11.6).
/// </summary>
public partial class LevelScene
{
    private IReadOnlyList<string> _pwadPaths = [];

    /// <summary>The netgame's session, or null in single player.</summary>
    public NetSession? Net { get; private set; }

    /// <summary>The exchange the level script runs, if any.</summary>
    public NetExchange? NetExchange { get; private set; }

    /// <summary>T11.6's exchange only (the level script has <c>netexchange</c>): the Start begins no game.</summary>
    public bool NetExchangeOnly { get; set; }

    /// <summary>T11.8: the Start the netgame's game began from, or null (single player, or before the Start).</summary>
    public NetStart? NetGame { get; private set; }

    /// <summary>T11.8: whether the tics come from the network (a netgame's game began; not under <c>netexchange</c>).</summary>
    public bool NetPlaying => NetGame is not null && Net is not null && NetExchange is null;

    /// <summary>T11.8: the last checksum window this machine reported (from 0 at the start tic) and its <c>World.Checksum()</c>.</summary>
    public (int Window, ulong Checksum)? LastNetChecksum { get; private set; }

    private readonly ticcmd_t[] _netCmds = new ticcmd_t[World.MAXPLAYERS];
    private readonly short[] _netConsistancy = new short[World.MAXPLAYERS];

    // The level script's commands in a netgame: from tic _netScriptFrom on (nettic), the tic the last one went to,
    // and the events of a route to run before their game tics (every machine playing the route runs them).
    private int _netScriptFrom;
    private int _netScriptLast = int.MinValue;
    private readonly Dictionary<int, Action<World>> _netBefore = [];

    /// <summary>Whether the arguments ask for a netgame (<c>-host</c>, <c>-server</c>, <c>-connect</c>): then <c>--level</c> waits for the Start.</summary>
    private static bool NetRequested()
    {
        if (IsCheckRun)
            return false;
        try
        {
            return NetOptions.Parse(OS.GetCmdlineUserArgs()) is not null;
        }
        catch (ArgumentException)
        {
            return true; // (StartNet fails the scene on it)
        }
    }

    /// <summary>
    /// Starts the session the arguments ask for (none: nothing). On a bad
    /// argument or a port that cannot be listened on, fails the scene (and a
    /// headless or scripted run quits with 1) and returns false.
    /// </summary>
    private bool StartNet()
    {
        try
        {
            if (NetOptions.Parse(OS.GetCmdlineUserArgs()) is not { } options)
                return true;
            if (IsCheckRun || _iwadPath is null)
                return true; // the checks are single player
            string? map = WadLocator.GetUserArg("--level") is string m && !m.StartsWith('-') ? m : null;
            var spawn = new SpawnSettings(GameMode, Skill, nomonsters: NoMonsters, gameversion: GameVersion, gamemission: GameMission);
            NetGameSettings settings = NetSession.GameSettings(options, spawn, Tweaks, map ?? DefaultMap());
            NetIdentity identity = NetSession.Identity(Main.Version, _iwadPath, _pwadPaths);
            GD.Print($"Net: {options.Role}, build {identity.Build}, IWAD {identity.IwadMd5}" + (identity.PwadMd5s.Count > 0 ? $", PWADs {string.Join(' ', identity.PwadMd5s)}" : ""));
            Net = new NetSession(options, identity, settings,
                use => new NetEnetTransport(options.Players) { BindAddress = use == NetTransportUse.Server ? options.BindAddress : "*" },
                line => GD.Print($"Net: {line}"));
            Flow.netsession = true; // T11.8: the menus refuse a new game, loads, saves and the end of the game
            if (Net.Server is not null && Wad is { } wad)
            {
                // T11.1a: the host refuses a game whose maps lack a start for a player in it (SPEC §12 T11.8)
                Net.CheckStart = mask => NetStarts.Refusal(wad, GameMode, settings.Episode, settings.Map, mask, settings.Spawn.deathmatch != 0);
            }
            return true;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        {
            Fail($"net: {e.Message}");
            if (DisplayServer.GetName() == "headless" || WadLocator.HasUserArg("--level-script"))
                GetTree().Quit(1);
            return false;
        }
    }

    /// <summary>One frame of the netgame: the session polled at this machine's clock, the game begun at the Start, and the script's exchange's frame.</summary>
    private void PollNet()
    {
        if (Net is not { } net)
            return;
        net.Update((long)Time.GetTicksUsec());
        if (NetGame is null && !NetExchangeOnly && NetExchange is null && net.Client is { State: NetClientState.InGame, Start: { } start })
            StartNetGame(start);
        NetExchange?.Frame();
    }

    /// <summary>The map a netgame's settings start on (<c>ExMy</c>, or <c>MAPxx</c> in Doom II).</summary>
    public static string MapName(NetGameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Spawn.gamemode == IsoDoom.Wad.GameMode.commercial
            ? string.Create(CultureInfo.InvariantCulture, $"MAP{settings.Map:00}")
            : string.Create(CultureInfo.InvariantCulture, $"E{settings.Episode}M{settings.Map}");
    }

    /// <summary>
    /// T11.8: the Start came: a new game (<see cref="GameFlow.G_InitNewMap"/>,
    /// as the menus' would) on its first map with its settings and tweaks
    /// (<see cref="StartWorld"/> makes the world from <see cref="NetGame"/>),
    /// this machine's slot the local player. Its first tic is the Start's.
    /// </summary>
    private void StartNetGame(NetStart start)
    {
        NetGameSettings s = start.Settings;
        if (s.Spawn.gameversion is { } version && version != Flow.gameversion)
        {
            // GameFlow's version (the intermissions, the finales, the next map) is the scene's: it must be the host's
            Fail($"net: the host plays -gameversion {IsoDoom.Wad.GameVersions.Cmdline(version)}, this machine {IsoDoom.Wad.GameVersions.Cmdline(Flow.gameversion)}: start it with -gameversion {IsoDoom.Wad.GameVersions.Cmdline(version)}");
            StopNet();
            if (DisplayServer.GetName() == "headless" || WadLocator.HasUserArg("--level-script"))
                GetTree().Quit(1);
            return;
        }
        NetGame = start;
        _netScriptFrom = start.StartTic;
        _netScriptLast = int.MinValue;
        _netBefore.Clear();
        _ticTime = 0;
        string map = MapName(s);
        GD.Print($"Net: game: {map}, skill {(int)s.Spawn.gameskill + 1}, {(s.Spawn.deathmatch == 0 ? "co-op" : s.Spawn.deathmatch == 1 ? "deathmatch" : "altdeath")}"
            + $"{(s.Spawn.nomonsters ? ", no monsters" : "")}, players {start.PlayerMask:x}, local player {start.ConsolePlayer + 1}, start tic {start.StartTic}, delay {start.Delay}");
        Flow.G_InitNewMap(s.Spawn.gameskill, map);
        if (World is null)
            GD.PrintErr($"Net: game: {map} did not load: {_status}");
    }

    /// <summary>
    /// T11.8: a netgame's frame of the game loop. The local commands due are
    /// built and sent (<see cref="NextNetCommand"/>); while the screen melts
    /// no tic runs (as single player's: the others' wait in the queue, and
    /// the protocol's pings go on, polled every frame); else every tic that
    /// is ready (<see cref="NetClient.TicsReady"/>) runs, at most
    /// <see cref="MaxTicsPerFrame"/>, and the presentation is drawn between
    /// the last two tics by the game clock's fraction, held on the last tic
    /// while the next is late (no extrapolation). A session that ended holds
    /// the world (T11.11 handles drop-outs).
    /// </summary>
    private void RunNetTics(double delta)
    {
        GameFlow flow = Flow;
        NetClient c = Net!.Client;
        if (c.State != NetClientState.InGame)
        {
            TicFraction = 1;
            return;
        }
        while (c.CommandsDue > 0)
            c.SendCommand(NextNetCommand(c.MakeTic));
        c.Flush();
        if (flow.Wipe.go)
        {
            // T7.1a: d_main.c's wipe loop, a melt step every tic's time
            _ticTime += delta;
            int steps = 0;
            while (_ticTime >= TicSeconds && flow.Wipe.go)
            {
                if (WipeHeld)
                {
                    _ticTime = 0;
                    break;
                }
                _ticTime -= TicSeconds;
                flow.WipeStep();
                if (++steps >= MaxTicsPerFrame)
                {
                    _ticTime = Math.Min(_ticTime, TicSeconds * 0.999);
                    break;
                }
            }
            TicFraction = 1;
            return;
        }
        _ticTime = 0;
        int ran = 0;
        while (ran < MaxTicsPerFrame && !flow.Wipe.go && !Paused && !Quitting && flow.CanTic && c.TryTakeTic(_netCmds, out int tic, out _))
        {
            RunNetTic(c, tic);
            ran++;
        }
        TicFraction = flow.Wipe.go || flow.paused || flow.gamestate != gamestate_t.GS_LEVEL ? 1 : Math.Clamp(c.ClockTics - c.LastRanTic, 0, 1);
    }

    /// <summary>
    /// This machine's command for <paramref name="tic"/>: with the level
    /// script's tics (<see cref="ScriptedTics"/>) the next queued one from the
    /// script's tic on (<c>nettic</c>), an empty one meanwhile; else built from
    /// the input (<see cref="BuildTiccmd"/>); then this machine's special
    /// buttons (<see cref="GameFlow.G_SpecialButtons"/>: a cheat typed).
    /// </summary>
    private ticcmd_t NextNetCommand(int tic)
    {
        ticcmd_t cmd = default;
        if (!ScriptedTics)
            cmd = BuildTiccmd(World?.tweaks ?? Tweaks);
        else if (tic >= _netScriptFrom && _scriptTics.Count > 0)
        {
            (ticcmd_t? scripted, Action<World>? before, _) = _scriptTics.Dequeue();
            cmd = scripted ?? BuildTiccmd(World?.tweaks ?? Tweaks);
            if (before is not null)
                _netBefore[tic] = before;
            _netScriptLast = tic;
        }
        Flow.G_SpecialButtons(ref cmd);
        return cmd;
    }

    /// <summary>
    /// Runs tic <paramref name="tic"/> of the bundle (<see cref="_netCmds"/>):
    /// a route's events for it first, then every player's command
    /// (<see cref="TicPlayers"/>), then reports it to the client with each
    /// player's consistancy and, at a checksum window's end, <c>World.Checksum()</c>.
    /// </summary>
    private void RunNetTic(NetClient c, int tic)
    {
        if (_netBefore.Remove(tic, out Action<World>? before) && World is { } w)
            before(w);
        TicPlayers(_netCmds);
        World? world = World;
        int sinceStart = tic - c.Start!.StartTic;
        ulong checksum = 0;
        if ((sinceStart + 1) % NetProtocol.WindowTics == 0)
        {
            checksum = world?.Checksum() ?? 0;
            LastNetChecksum = (sinceStart / NetProtocol.WindowTics, checksum);
        }
        for (int i = 0; i < World.MAXPLAYERS; i++)
            _netConsistancy[i] = Consistancy(world, i);
        c.TicRan(tic, checksum, _netConsistancy);
    }

    /// <summary>g_game.c <c>G_Ticker</c>'s consistancy of player <paramref name="player"/>: its mobj's <c>x</c>, or the <c>P_Random</c> index without one; 0 not in the game (or without a world: the title loop after the game).</summary>
    public static short Consistancy(World? world, int player)
    {
        if (world is null || !world.playeringame[player])
            return 0;
        return unchecked((short)(world.players[player].mo is mobj_t mo ? mo.x : world.random.prndindex));
    }

    /// <summary>The scripted commands sent but not run yet (the level script waits for them as for queued tics).</summary>
    private int NetTicsInFlight => NetPlaying && _netScriptLast > Net!.Client.LastRanTic ? _netScriptLast - Net.Client.LastRanTic : 0;

    /// <summary>
    /// T11.8: the level script's <c>nettic T</c>: the scripted commands queued
    /// from now on go to game tic T (counted from the game's first tic, 0) and
    /// on; false when this machine already sent its command for T. Every
    /// machine that plays the same route from the same tic plays the same game.
    /// </summary>
    public bool SetNetScriptTic(int tic)
    {
        if (NetGame is not { } start || Net is not { } net)
            return false;
        int at = start.StartTic + tic;
        if (net.Client.MakeTic > at)
            return false;
        _netScriptFrom = at;
        return true;
    }

    /// <summary>T11.8: a debug key or command that would change the sim on this machine alone is refused in a netgame (with a line).</summary>
    public bool NetRefuses(string what)
    {
        if (Net is null)
            return false;
        GD.Print($"Level: {what}: not in a netgame");
        return true;
    }

    /// <summary>T11.8: in a netgame the settings that are sim state (the aim assist's cone, <see cref="Tweaks"/>) are the host's: locked.</summary>
    public bool SettingLocked(string key) => key == "gameplay/aim_assist" && (Net is not null || World?.netgame == true);

    /// <summary>The level script's <c>netexchange TICS</c>: starts an exchange of TICS tics (whole windows) through this machine's client.</summary>
    public NetExchange StartNetExchange(int tics)
    {
        if (Net is not { } net)
            throw new ArgumentException("netexchange: no netgame (-host or -connect)");
        return NetExchange = new NetExchange(net.Client, tics);
    }

    /// <summary>Ends the session: the client quits, the transports close (the host's disconnects reach the others).</summary>
    public void StopNet()
    {
        NetExchange = null;
        Net?.Dispose();
        Net = null;
    }

    /// <summary>The level script's <c>net</c>: the session's state in a line.</summary>
    public string NetText()
    {
        if (Net is not { } net)
            return "no netgame";
        NetClient c = net.Client;
        var s = new System.Text.StringBuilder();
        s.Append(CultureInfo.InvariantCulture, $"{net.Options.Role}, {c.State}, slot {c.Slot}");
        if (c.Start is { } start)
            s.Append(CultureInfo.InvariantCulture, $", players {start.PlayerMask:x}, delay {start.Delay}, start tic {start.StartTic}");
        else if (c.State == NetClientState.Lobby)
            s.Append(CultureInfo.InvariantCulture, $", lobby {c.LobbyOccupied:x}");
        s.Append(CultureInfo.InvariantCulture, $", ping {(c.RttUs < 0 ? "-" : (c.RttUs / 1000.0).ToString("F1", CultureInfo.InvariantCulture) + " ms")}");
        s.Append(CultureInfo.InvariantCulture, $", tics waiting {c.TicsReady}, bundles to {c.BundleTic}, ran {c.LastRanTic}");
        if (LastNetChecksum is (int window, ulong checksum))
            s.Append(CultureInfo.InvariantCulture, $", window {window} checksum {checksum:x16}");
        if (c.FirstBadTic >= 0)
            s.Append(CultureInfo.InvariantCulture, $", CONSISTENCY FAILURE at tic {c.FirstBadTic}");
        if (net.Server is { } server)
            s.Append(CultureInfo.InvariantCulture, $"; server {server.Phase}, players {server.PlayerCount}, windows compared {server.WindowsCompared}");
        if ((net.Server?.Desync ?? c.Desync) is { } desync)
            s.Append(CultureInfo.InvariantCulture, $", DESYNC at window {desync.Window} (tics {desync.FirstTic}-{desync.LastTic})");
        if (net.StartRefusal is { } refusal)
            s.Append(CultureInfo.InvariantCulture, $"; cannot start: {refusal}");
        if (NetExchange is { } x)
            s.Append(CultureInfo.InvariantCulture, $"; exchange {x.TicsTaken}/{x.Tics} tics, hash {x.Hash:x16}");
        if (net.Failure is { } failure)
            s.Append(CultureInfo.InvariantCulture, $"; {failure}");
        return s.ToString();
    }
}
