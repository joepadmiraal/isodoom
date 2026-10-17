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

    // T11.11: the desync's dumps (the last tics and windows), the forced desync asked for, the banner over the view.
    private NetDesyncLog? _desyncLog;
    private bool _netBumpRandom;
    private CanvasLayer? _netBannerLayer;
    private Label? _netBanner;

    /// <summary>T11.11: how late a bundle is (the world holding) before "waiting for player N" shows over the view, µs (SPEC §12 T11.11).</summary>
    public const long NetWaitingMessageUs = 1_000_000;

    /// <summary>T11.11: the desync this machine stopped for, once it did (its dumps written: <see cref="NetDesyncDumps"/>).</summary>
    public NetDesync? NetStoppedDesync { get; private set; }

    /// <summary>T11.11: the directory of this machine's desync dumps, once written (null: none, or they could not be).</summary>
    public string? NetDesyncDumps { get; private set; }

    /// <summary>T11.11: why the netgame ended on this machine (the host left, the link timed out), once it went back to the title loop.</summary>
    public string? NetEnded { get; private set; }

    /// <summary>T11.11: the text shown over the view (waiting for a player, the desync), or null.</summary>
    public string? NetBannerText { get; private set; }

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
            OpenNetSession(options, spawn, map ?? DefaultMap());
            Menu.ShowLobby(); // T11.9: the lobby page shows who is in (Escape leaves it); the host starts at -players
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

    /// <summary>
    /// The session for <paramref name="options"/> (T11.6; T11.9: the
    /// command line's or the menus'), the host's game <paramref name="spawn"/>
    /// on <paramref name="map"/>; the host checks the players' starts
    /// (T11.1a). Its exceptions are the caller's.
    /// </summary>
    private NetSession OpenNetSession(NetOptions options, SpawnSettings spawn, string map)
    {
        NetGameSettings settings = NetSession.GameSettings(options, spawn, Tweaks, map);
        NetIdentity identity = NetSession.Identity(Main.Version, _iwadPath!, _pwadPaths);
        GD.Print($"Net: {options.Role}, build {identity.Build}, IWAD {identity.IwadMd5}" + (identity.PwadMd5s.Count > 0 ? $", PWADs {string.Join(' ', identity.PwadMd5s)}" : ""));
        var made = new List<NetEnetTransport>();
        try
        {
            Net = new NetSession(options, identity, settings, use =>
            {
                var transport = new NetEnetTransport(options.Players)
                {
                    BindAddress = use == NetTransportUse.Server ? options.BindAddress : "*",
                    TimeoutMs = options.TimeoutSeconds * 1000, // T11.11
                };
                made.Add(transport);
                return transport;
            }, line => GD.Print($"Net: {line}"));
        }
        catch
        {
            foreach (NetEnetTransport transport in made)
                transport.Dispose(); // T11.9: a port that can't be listened on leaves no ENet host to the finalizer
            throw;
        }
        Flow.netsession = true; // T11.8: the menus refuse a new game, loads, saves and the end of the game
        NetEnded = null;
        AddNetBanner(); // T11.11
        if (Net.Server is not null && Wad is { } wad)
        {
            // T11.1a: the host refuses a game whose maps lack a start for a player in it (SPEC §12 T11.8)
            Net.CheckStart = mask => NetStarts.Refusal(wad, GameMode, settings.Episode, settings.Map, mask, settings.Spawn.deathmatch != 0);
        }
        return Net;
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
        UpdateNetBanner(); // T11.11
    }

    /// <summary>The map a netgame's settings start on (<c>ExMy</c>, or <c>MAPxx</c> in Doom II).</summary>
    public static string MapName(NetGameSettings settings) => NetSession.MapName(settings);

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
        _desyncLog = new NetDesyncLog(start); // T11.11
        NetStoppedDesync = null;
        _netGameOver = null;
        _quitStoppedNetGame = false;
        NetDesyncDumps = null;
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
        if (_netGameOver is { } over)
        {
            // the game ended under the netgame: the host waits for the others to get there too, then every machine closes
            TicFraction = 1;
            if (Net.Server is not { PlayerCount: > 1 } || Time.GetTicksMsec() >= _netGameOverUntilMs)
                CloseNetGame(over);
            return;
        }
        if (c.State != NetClientState.InGame)
        {
            TicFraction = 1;
            EndNetGame(Net.Failure ?? "disconnected"); // T11.11: the host left, or the link to it is gone
            return;
        }
        if (c.Desync is { } desync)
        {
            TicFraction = 1;
            if (NetStoppedDesync is null)
                StopForDesync(c, desync); // T11.11: every machine stops, with its dumps
            else if (_quitStoppedNetGame)
                QuitStoppedNetGame(); // the stop's message answered: back to the title loop
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
        while (ran < MaxTicsPerFrame && _netGameOver is null && !flow.Wipe.go && !Paused && !Quitting && flow.CanTic && c.TryTakeTic(_netCmds, out int tic, out byte mask))
        {
            RunNetTic(c, tic, mask);
            ran++;
        }
        if (_netGameOver is { } ended)
        {
            flow.Menu.M_StartMessage($"{flow.Menu.Wrap("the netgame ended: " + ended)}.\n\n{MMenu.PRESSKEY}", null, false);
            TicFraction = 1;
            return;
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
    /// Runs tic <paramref name="tic"/> of the bundle (<see cref="_netCmds"/>,
    /// its players <paramref name="mask"/>): a route's events for it first,
    /// then every player's command (<see cref="TicPlayers"/>: T11.11, a player
    /// the bundle leaves out quits), then reports it to the client with each
    /// player's consistancy and, at a checksum window's end, <c>World.Checksum()</c>.
    /// T11.11: each tic is logged for the desync dumps (<see cref="NetDesyncLog"/>,
    /// the checksum every tic), and the world is saved at the start and at each window's end.
    /// </summary>
    private void RunNetTic(NetClient c, int tic, byte mask)
    {
        if (tic == c.Start!.StartTic && World is { } first)
            KeepNetSave(NetDesyncLog.StartWindow, first); // T11.11: the state before the first tic, the desync dumps' last agreed one for window 0
        if (_netBefore.Remove(tic, out Action<World>? before) && World is { } w)
            before(w);
        if (_netBumpRandom && World is { } bumped)
        {
            // T11.11: the level script's netdesync: this machine's world alone takes a P_Random
            _netBumpRandom = false;
            bumped.P_Random();
            GD.Print($"Net: forced desync: P_Random taken before tic {tic} on this machine (index {bumped.random.prndindex})");
        }
        TicPlayers(_netCmds, mask); // T11.11: the bundle's players: a player it leaves out quits at this tic
        World? world = World;
        int sinceStart = tic - c.Start.StartTic;
        ulong checksum = world?.Checksum() ?? 0; // T11.11: every tic's, for the desync dumps
        for (int i = 0; i < World.MAXPLAYERS; i++)
            _netConsistancy[i] = Consistancy(world, i);
        _desyncLog?.Tic(tic, mask, _netCmds, checksum, _netConsistancy);
        if ((sinceStart + 1) % NetProtocol.WindowTics == 0)
        {
            LastNetChecksum = (sinceStart / NetProtocol.WindowTics, checksum);
            if (world is not null)
                KeepNetSave(sinceStart / NetProtocol.WindowTics, world);
        }
        c.TicRan(tic, checksum, _netConsistancy);
    }

    /// <summary>T11.11: keeps the world's save after checksum window <paramref name="window"/> (the desync dumps' last agreed state).</summary>
    private void KeepNetSave(int window, World world)
    {
        try
        {
            _desyncLog?.Window(window, world);
        }
        catch (Exception e) when (e is SaveGameException or InvalidOperationException or System.IO.IOException)
        {
            GD.PushWarning($"Net: the world after window {window} could not be kept for the desync dumps: {e.Message}");
        }
    }

    /// <summary>
    /// T11.11: the server announced a desync: this machine stops (no tic
    /// runs, no command goes: <see cref="NetClient.Stopped"/>), writes its
    /// dumps (<see cref="NetDesyncLog.Write"/>: the last tics' checksums and
    /// commands, and the world at the last agreed window) under
    /// <see cref="NetDesyncRoot"/>, says where in the log, and shows the
    /// desync over the view until the game is quit.
    /// </summary>
    private void StopForDesync(NetClient c, NetDesync desync)
    {
        NetStoppedDesync = desync;
        string root = NetDesyncRoot();
        try
        {
            NetDesyncDumps = _desyncLog?.Write(root, desync, c.Slot, NetSession.BuildStamp(Main.Version), c.FirstBadTic, DateTime.Now, World);
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
        {
            GD.PushError($"Net: the desync dumps could not be written to {root}: {e.Message}");
        }
        GD.Print($"Net: desync in window {desync.Window} (tics {desync.FirstTic}-{desync.LastTic}): the game stopped after tic {c.LastRanTic}; "
            + (NetDesyncDumps is { } dir ? $"dumps in {dir}" : "no dumps"));
        // T11.11's follow-up (the user's decision): the stopped game offers to quit to the title loop
        _quitStoppedNetGame = false;
        Flow.Menu.M_StartMessage(DesyncMessage(desync), _ => _quitStoppedNetGame = true, false);
    }

    /// <summary>
    /// The menus' message when a desync stopped the game (after the dumps;
    /// any key answers it and the game goes back to the title loop:
    /// <see cref="QuitStoppedNetGame"/>; SPEC §12 "A desync stop offers to quit").
    /// </summary>
    public static string DesyncMessage(NetDesync desync) =>
        $"desync: the machines differ in\ntics {desync.FirstTic}-{desync.LastTic}. the game stopped.\n\npress a key to return\nto the title loop.";

    // The desync stop's message was answered: the next frame quits the netgame (outside the menus' responder).
    private bool _quitStoppedNetGame;

    /// <summary>
    /// T11.11's follow-up (the user's decision): the stop's message answered,
    /// this machine leaves the stopped netgame (its session closed, so the
    /// others see it leave) and goes back to the title loop, single player
    /// again; the overlay says why (<see cref="NetEnded"/>). The dumps stay.
    /// </summary>
    private void QuitStoppedNetGame()
    {
        _quitStoppedNetGame = false;
        GD.Print("Net: the stopped netgame quit to the title loop");
        CloseNetGame("the game desynced");
    }

    /// <summary>T11.11: where the desync dumps go: <c>--netdesync=DIR</c>, else <c>user://netdesync</c>.</summary>
    private static string NetDesyncRoot()
        => System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath(WadLocator.GetUserArg("--netdesync") is string dir ? WadLocator.ResolveUserPath(dir) : "user://netdesync"));

    /// <summary>
    /// T11.11: the netgame's session ended under this machine (the host
    /// quit, or its link timed out): back to the title loop with a message
    /// (the menus' message box and the overlay), the session closed; the
    /// game is single player again.
    /// </summary>
    private void EndNetGame(string why)
    {
        if (NetGame is null)
            return;
        string text = why.Contains("server is gone", StringComparison.OrdinalIgnoreCase) ? "the host left the game"
            : why.Replace("disconnected: ", "", StringComparison.Ordinal).TrimEnd('.');
        GD.Print($"Net: the netgame ended: {text}");
        CloseNetGame(text);
        Flow.Menu.M_StartMessage($"the netgame ended:\n{text}.\n\n{MMenu.PRESSKEY}", null, false);
    }

    // Why the game ended under the netgame (NetGameOver), and until when the host waits for the others to run that tic too.
    private string? _netGameOver;
    private ulong _netGameOverUntilMs;

    // How long the host keeps its session open after the game ended under the netgame (the others are a few tics behind).
    private const ulong NetGameOverGraceMs = 5000;

    /// <summary>
    /// The game ended under the netgame at a tic every machine runs (a next
    /// map that could not be built, as a deathmatch's map with fewer than 4
    /// deathmatch starts, or one the WAD lacks; SPEC §12 "A deathmatch checks
    /// its start map only"): no more tics run, a menu message says why, and
    /// the netgame closes (<see cref="CloseNetGame"/>): a join's at once, the
    /// host's once the others left or after <see cref="NetGameOverGraceMs"/>,
    /// so a join a few tics behind gets there before the host goes.
    /// </summary>
    private void NetGameOver(string why)
    {
        if (_netGameOver is not null)
            return;
        _netGameOver = why;
        _netGameOverUntilMs = Time.GetTicksMsec() + NetGameOverGraceMs;
        GD.Print($"Net: the game ended: {why}");
    }

    // The netgame ended (EndNetGame, NetGameOver) or quit after a desync (QuitStoppedNetGame): the session closed, the title loop, the banner gone.
    private void CloseNetGame(string text)
    {
        _netGameOver = null;
        NetEnded = text;
        NetGame = null;
        _desyncLog = null;
        _netBefore.Clear();
        ClearQueuedTics(); // the netgame's scripted commands are not single player's
        StopNet();
        Flow.netsession = false;
        Flow.D_StartTitle($"the netgame ended: {text}");
        NetStoppedDesync = null; // (the dumps' directory stays in NetDesyncDumps)
        _quitStoppedNetGame = false;
        UpdateNetBanner(); // its desync text gone (PollNet no longer runs it)
    }

    /// <summary>T11.11: the level script's <c>netdesync</c>: before the next tic this machine's world alone takes a <c>P_Random</c> (a forced desync, for the tests).</summary>
    public void ForceNetDesync() => _netBumpRandom = true;

    /// <summary>T11.11: the banner over the view (a label of its own, centred at the top; shown only with text).</summary>
    private void AddNetBanner()
    {
        if (_netBannerLayer is not null)
            return;
        _netBannerLayer = new CanvasLayer { Layer = 1 };
        using var settings = new LabelSettings { FontSize = 22, OutlineSize = 6, OutlineColor = Colors.Black }; // T8.3d: held across the setter
        _netBanner = new Label
        {
            LabelSettings = settings,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _netBanner.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _netBanner.OffsetTop = 64;
        _netBannerLayer.AddChild(_netBanner);
        AddChild(_netBannerLayer);
    }

    /// <summary>
    /// T11.11: the banner's text: the desync this machine stopped for, or,
    /// while a bundle is over <see cref="NetWaitingMessageUs"/> late, the
    /// players the host waits for (or the host itself when it named none:
    /// the link to it is slow). Vanilla shows nothing (SPEC §12 T11.11).
    /// </summary>
    private void UpdateNetBanner()
    {
        string? text = null;
        if (NetStoppedDesync is { } d)
            text = $"Desync: the machines differ in tics {d.FirstTic}-{d.LastTic}. The game stopped."
                + (NetDesyncDumps is { } dir ? $"\nDumps: {dir}" : "");
        else if (NetPlaying && _netGameOver is null && Net!.Client is { State: NetClientState.InGame, LateUs: > NetWaitingMessageUs } c)
            text = WaitingText(c.WaitingFor, c.Slot);
        NetBannerText = text;
        if (_netBanner is null)
            return;
        if (_netBanner.Text != (text ?? ""))
            _netBanner.Text = text ?? "";
        _netBanner.Visible = text is not null;
    }

    /// <summary>T11.11: "Waiting for player 2...", "players 2 and 3", or the host when no player is named (or only this one).</summary>
    public static string WaitingText(byte waiting, int self) => NetSession.WaitingText(waiting, self);

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
