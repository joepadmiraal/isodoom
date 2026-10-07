using System;
using IsoDoom.Sim;
using IsoDoom.Wad;

namespace IsoDoom.Game;

/// <summary>doomdef.h <c>gamestate_t</c>: what the game shows and ticks.</summary>
public enum gamestate_t
{
    GS_LEVEL,
    GS_INTERMISSION,
    GS_FINALE,
    GS_DEMOSCREEN,
}

/// <summary>
/// What <see cref="GameFlow"/> needs from the game scene (T7.1): the level
/// it shows (its <see cref="World"/>, built with the presentation's mesh)
/// and the level's tic. The level scene implements it; the tests fake it.
/// </summary>
public interface IGameHost
{
    /// <summary>The level's game state, or null without a level (the title loop) or when its things can't be spawned.</summary>
    World? World { get; }

    /// <summary>Whether the WAD has lump <paramref name="name"/> (a page, a map).</summary>
    bool HasLump(string name);

    /// <summary>
    /// g_game.c <c>G_InitNew</c>'s level: a new game state on
    /// <paramref name="skill"/> (a new <see cref="Sim.World"/>) and its first
    /// level, <paramref name="map"/> (<c>G_DoLoadLevel</c>). False when the
    /// map can't be loaded (the host says why).
    /// </summary>
    bool G_InitNew(skill_t skill, string map);

    /// <summary>g_game.c <c>G_DoWorldDone</c>'s level: <paramref name="map"/> in the same world, the players as they left the last one. False when it can't be loaded.</summary>
    bool G_DoWorldDone(string map);

    /// <summary>g_game.c <c>G_DoLoadLevel</c> for <see cref="gameaction_t.ga_loadlevel"/> (a reborn, T6.12): the level afresh in the same world. False when it can't be loaded.</summary>
    bool G_DoLoadLevel();

    /// <summary>
    /// g_game.c <c>G_Ticker</c>'s <see cref="gamestate_t.GS_LEVEL"/> part:
    /// <c>P_Ticker</c> (the world's tic with <paramref name="cmd"/>; nothing
    /// while <paramref name="paused"/>), then <c>ST_Ticker</c> and <c>HU_Ticker</c>.
    /// </summary>
    void G_LevelTicker(in ticcmd_t cmd, bool paused);

    /// <summary>The level was completed (<c>G_DoCompleted</c>, before the intermission or the finale).</summary>
    void LevelCompleted();

    /// <summary>The game ended or stopped (<paramref name="why"/>) and the title loop starts (<see cref="GameFlow.D_StartTitle"/>): the level goes.</summary>
    void EndGame(string? why);
}

/// <summary>
/// g_game.c's game flow and d_main.c's title loop (T7.1, SPEC §7.6, §8), in
/// plain C# (no Godot types; the tests link it): <see cref="gamestate"/>,
/// the game actions (<see cref="gameaction"/>: <see cref="G_DoGameActions"/>),
/// <see cref="G_InitNew"/>, <see cref="G_DeferedInitNew"/>,
/// <see cref="G_DoCompleted"/>, <see cref="G_WorldDone"/>, pausing
/// (<c>BTS_PAUSE</c>), and the title loop (<see cref="D_StartTitle"/>,
/// <see cref="D_DoAdvanceDemo"/>, <see cref="D_PageTicker"/>) without its
/// demos (SPEC §7.6). The sim's part of the flow is the world's
/// (<c>World.Game.cs</c>, T5.8); the level itself is the host's
/// (<see cref="IGameHost"/>). The intermission (<see cref="WiStuff"/>) and
/// the finale (<see cref="FFinale"/>) are placeholders until T7.4 and T7.5.
/// </summary>
public sealed class GameFlow
{
    private readonly IGameHost host;
    private gameaction_t _gameaction;

    public GameFlow(IGameHost host, GameMode gamemode, DoomRandom mrandom, GameVariant gamevariant = GameVariant.vanilla)
    {
        this.host = host;
        this.gamemode = gamemode;
        this.gamevariant = gamevariant;
        MRandom = mrandom;
        Wi = new WiStuff(this);
        Finale = new FFinale(this);
        Menu = new MMenu(this, gamevariant);
    }

    /// <summary>doomstat.h <c>gamemode</c>.</summary>
    public readonly GameMode gamemode;

    /// <summary>Chocolate Doom's <c>gamevariant</c> (the BFG Edition's workarounds, T7.2).</summary>
    public readonly GameVariant gamevariant;

    /// <summary>The menus (m_menu.c, T7.2).</summary>
    public MMenu Menu { get; }

    /// <summary>m_random.c's <c>M_Random</c> index of the presentation (<see cref="G_InitNew"/> clears it, T6.11).</summary>
    public DoomRandom MRandom { get; }

    /// <summary>The level's world (the host's), or null.</summary>
    public World? World => host.World;

    /// <summary>g_game.c <c>gamestate</c> (vanilla's starts at <see cref="gamestate_t.GS_LEVEL"/> until the title loop's first step).</summary>
    public gamestate_t gamestate;

    /// <summary>
    /// g_game.c <c>gameaction</c>: the level's world's (<see cref="World.gameaction"/>,
    /// which the sim sets: the exits, a reborn) while there is one, else the flow's own.
    /// </summary>
    public gameaction_t gameaction
    {
        get => host.World?.gameaction ?? _gameaction;
        set
        {
            if (host.World is { } world)
                world.gameaction = value;
            _gameaction = value;
        }
    }

    /// <summary>g_game.c <c>paused</c>: <c>P_Ticker</c> does nothing (toggled by a <c>ticcmd</c>'s <c>BTS_PAUSE</c>).</summary>
    public bool paused;

    /// <summary>g_game.c <c>usergame</c>: a game the user started (not the title loop; T7.6's saves need one).</summary>
    public bool usergame;

    /// <summary>d_main.c <c>gametic</c> as the game counts it: every <see cref="G_Ticker"/> (title, intermission and level).</summary>
    public int gametic;

    /// <summary>g_game.c <c>gameskill</c>: the skill of the game started last.</summary>
    public skill_t gameskill = skill_t.sk_medium;

    // g_game.c G_DeferedInitNew's: the next game's skill, episode and map.
    private skill_t d_skill;
    private int d_episode, d_map;

    /// <summary>d_main.c <c>advancedemo</c>: the title loop's next step is due (before the next tic).</summary>
    public bool advancedemo;

    /// <summary>d_main.c <c>demosequence</c>: the title loop's step (−1 before the first).</summary>
    public int demosequence = -1;

    /// <summary>d_main.c <c>pagetic</c>: the tics the page still shows.</summary>
    public int pagetic;

    /// <summary>d_main.c <c>pagename</c>: the page the title loop shows (<c>TITLEPIC</c>, <c>CREDIT</c>, <c>HELP2</c>).</summary>
    public string pagename = "TITLEPIC";

    /// <summary>The intermission (wi_stuff.c; a placeholder until T7.4).</summary>
    public WiStuff Wi { get; }

    /// <summary>The finale (f_finale.c; a placeholder until T7.5).</summary>
    public FFinale Finale { get; }

    /// <summary>
    /// Whether a tic can run: always but on a level without a player mobj
    /// (no world, or none spawned), as before T7.1, unless a game action or
    /// the title loop's step is due.
    /// </summary>
    public bool CanTic =>
        gamestate != gamestate_t.GS_LEVEL || advancedemo || gameaction != gameaction_t.ga_nothing
        || host.World is { } w && w.players[w.consoleplayer].mo is not null;

    /// <summary>
    /// g_game.c <c>G_Ticker</c> for the console player's <paramref name="cmd"/>,
    /// after d_main.c's title loop step (<see cref="D_DoAdvanceDemo"/>, which
    /// vanilla's tic loop runs before it): the game actions due, the special
    /// buttons (<c>BTS_PAUSE</c> toggles <see cref="paused"/>; <c>BTS_SAVEGAME</c>
    /// is T7.6's), then the tic of the <see cref="gamestate"/>. The game
    /// actions the tic set run at its end too (not vanilla's place, which is
    /// the next tic's start: nothing runs in between, so the same order; a
    /// scripted run sees the next level or the intermission without
    /// another tic; SPEC §12 T5.8, T6.12, T7.1).
    /// </summary>
    public void G_Ticker(in ticcmd_t cmd)
    {
        if (advancedemo)
            D_DoAdvanceDemo();
        Menu.M_Ticker(); // d_net.c TryRunTics: M_Ticker before G_Ticker

        // do things to change the game state
        G_DoGameActions();

        // vanilla copies each player's ticcmd in every state (the level's copy is World.G_Ticker's)
        World? world = host.World;
        if (world is not null && gamestate != gamestate_t.GS_LEVEL)
            world.players[world.consoleplayer].cmd = cmd;

        // check for special buttons
        if ((cmd.buttons & buttoncode_t.BT_SPECIAL) != 0 && (cmd.buttons & buttoncode_t.BT_SPECIALMASK) == buttoncode_t.BTS_PAUSE)
            paused = !paused; // S_PauseSound / S_ResumeSound: T7.7

        // do main actions
        switch (gamestate)
        {
            case gamestate_t.GS_LEVEL:
                if (world is not null)
                    host.G_LevelTicker(cmd, paused || MenuHolds(world));
                break;
            case gamestate_t.GS_INTERMISSION:
                Wi.WI_Ticker();
                break;
            case gamestate_t.GS_FINALE:
                Finale.F_Ticker();
                break;
            case gamestate_t.GS_DEMOSCREEN:
                D_PageTicker();
                break;
        }
        gametic++;

        G_DoGameActions();
    }

    /// <summary>
    /// p_tick.c <c>P_Ticker</c>'s hold while the menu is up in single player
    /// (<c>menuactive &amp;&amp; !demoplayback &amp;&amp; viewz != 1</c>: the first tic of a
    /// level the player came to alive, whose <c>P_SetupLevel</c> set
    /// <c>viewz</c> to 1, still runs; a reborn player's is 0, so held at once).
    /// </summary>
    public bool MenuHolds(World world) =>
        !world.netgame && Menu.menuactive && world.players[world.consoleplayer].viewz != 1;

    /// <summary>g_game.c <c>savegameslot</c> and <c>savedescription</c>: the save the menu asked for last (saved from T7.6).</summary>
    public int savegameslot = -1;

    /// <inheritdoc cref="savegameslot"/>
    public string savedescription = "";

    /// <summary>
    /// g_game.c <c>G_SaveGame</c> (the save menu's): vanilla saves at the
    /// next tic's <c>BTS_SAVEGAME</c> (<c>sendsave</c>); the saving itself is
    /// T7.6's, so only the slot and description are kept.
    /// </summary>
    public void G_SaveGame(int slot, string description)
    {
        savegameslot = slot;
        savedescription = description;
    }

    /// <summary>g_game.c <c>G_LoadGame</c> (the load menu's): <see cref="gameaction_t.ga_loadgame"/>, which <see cref="G_DoGameActions"/> drops until T7.6.</summary>
    public void G_LoadGame(int slot)
    {
        savegameslot = slot;
        gameaction = gameaction_t.ga_loadgame;
    }

    /// <summary>
    /// g_game.c <c>G_Ticker</c>'s "do things to change the game state": runs
    /// the game actions until none is left. <c>ga_loadgame</c> and
    /// <c>ga_savegame</c> are T7.6's; there is no demo playback (SPEC §7.6)
    /// and no screenshot action (the level script's <c>shot</c>).
    /// </summary>
    public void G_DoGameActions()
    {
        while (gameaction != gameaction_t.ga_nothing)
        {
            switch (gameaction)
            {
                case gameaction_t.ga_loadlevel:
                    G_DoLoadLevel();
                    break;
                case gameaction_t.ga_newgame:
                    G_DoNewGame();
                    break;
                case gameaction_t.ga_completed:
                    G_DoCompleted();
                    break;
                case gameaction_t.ga_victory:
                    F_StartFinale();
                    break;
                case gameaction_t.ga_worlddone:
                    G_DoWorldDone();
                    break;
                default:
                    // ga_loadgame, ga_savegame (T7.6), ga_playdemo, ga_screenshot: not ported
                    gameaction = gameaction_t.ga_nothing;
                    break;
            }
        }
    }

    /// <summary>
    /// g_game.c <c>G_DoLoadLevel</c> for <see cref="gameaction_t.ga_loadlevel"/>
    /// (a reborn): the host reloads the level (whose world's
    /// <c>G_DoLoadLevel</c> ends the action). Vanilla's forced wipe is not
    /// ported (f_wipe.c, T7.1a).
    /// </summary>
    private void G_DoLoadLevel()
    {
        gamestate = gamestate_t.GS_LEVEL;
        if (!host.G_DoLoadLevel())
            D_StartTitle(null);
    }

    /// <summary>
    /// g_game.c <c>G_DeferedInitNew</c>: a new game on <paramref name="skill"/>,
    /// episode <paramref name="episode"/>, map <paramref name="map"/> starts
    /// before the next tic (the menus' <c>M_ChooseSkill</c>, T7.2, and the
    /// level script's <c>newgame</c>).
    /// </summary>
    public void G_DeferedInitNew(skill_t skill, int episode, int map)
    {
        d_skill = skill;
        d_episode = episode;
        d_map = map;
        gameaction = gameaction_t.ga_newgame;
    }

    /// <summary>
    /// g_game.c <c>G_DoNewGame</c>: <see cref="G_InitNew"/> with the deferred
    /// settings. Vanilla also clears the net game and the command-line
    /// parameters (<c>-nomonsters</c>, <c>-respawn</c>, <c>-fast</c>); the
    /// host keeps its debug arguments (<c>--level-monsters</c>).
    /// </summary>
    private void G_DoNewGame()
    {
        G_InitNew(d_skill, d_episode, d_map);
        gameaction = gameaction_t.ga_nothing;
    }

    /// <summary>
    /// g_game.c <c>G_InitNew</c>: clamps the skill, episode and map to the
    /// game mode's (shareware: episode 1; registered: 1–3; retail: 1–4; maps
    /// 1–9 but Doom II's) and starts the game on that map
    /// (<see cref="G_InitNewMap"/>).
    /// </summary>
    public void G_InitNew(skill_t skill, int episode, int map)
    {
        if (skill > skill_t.sk_nightmare)
            skill = skill_t.sk_nightmare;

        if (episode < 1)
            episode = 1;

        if (gamemode == GameMode.retail)
        {
            if (episode > 4)
                episode = 4;
        }
        else if (gamemode == GameMode.shareware)
        {
            if (episode > 1)
                episode = 1; // only start episode 1 on shareware
        }
        else
        {
            if (episode > 3)
                episode = 3;
        }

        if (map < 1)
            map = 1;

        if (map > 9 && gamemode != GameMode.commercial)
            map = 9;

        G_InitNewMap(skill, World.MapName(gamemode, episode, map));
    }

    /// <summary>
    /// <see cref="G_InitNew"/> after its clamps, on a map by name (also the
    /// debug arguments' way in: <c>--level MAP</c>, PgDn/PgUp, the level
    /// check): unpauses, clears <c>M_Random</c> (<c>M_ClearRandom</c>; the new
    /// world's <c>P_Random</c> starts cleared), and the host starts a new
    /// world on the map (its players reborn: <c>PST_REBORN</c>; Nightmare's
    /// respawning and fast monsters are the world's settings), the game
    /// state the level. A map that can't be loaded goes back to the title loop.
    /// </summary>
    public void G_InitNewMap(skill_t skill, string map)
    {
        paused = false; // S_ResumeSound: T7.7
        advancedemo = false; // not vanilla: a title loop step due (the game started before the title's first tic) is dropped
        MRandom.M_ClearRandom();
        usergame = true; // will be set false if a demo
        gameskill = skill;
        // the sky (R_TextureNumForName("SKY1"…)) is the presentation's: never drawn (SPEC §7.2)
        if (!host.G_InitNew(skill, map))
        {
            D_StartTitle(null);
            return;
        }
        gamestate = gamestate_t.GS_LEVEL;
        _gameaction = gameaction_t.ga_nothing;
    }

    /// <summary>
    /// g_game.c <c>G_DoCompleted</c> (<see cref="gameaction_t.ga_completed"/>):
    /// the world's part (<see cref="World.G_DoCompleted"/>: the players finish
    /// the level, <see cref="World.wminfo"/>), then the intermission
    /// (<see cref="WiStuff.WI_Start"/>), or, at an episode's end before Doom
    /// II, <see cref="gameaction_t.ga_victory"/> (the finale, no intermission).
    /// <c>AM_Stop</c> is the automap's (T7.9).
    /// </summary>
    private void G_DoCompleted()
    {
        World world = host.World!;
        world.G_DoCompleted();
        host.LevelCompleted();
        if (world.gameaction == gameaction_t.ga_victory)
            return;
        gamestate = gamestate_t.GS_INTERMISSION;
        Wi.WI_Start(world.wminfo, world);
    }

    /// <summary>
    /// g_game.c <c>G_WorldDone</c> (the intermission is over, <c>WI_End</c>):
    /// the world's part (<see cref="World.G_WorldDone"/>:
    /// <see cref="gameaction_t.ga_worlddone"/>), and Doom II's text screens
    /// after maps 6, 11, 20 and 30 and the secret exits of 15 and 31
    /// (<see cref="F_StartFinale"/>, which then holds the next level until
    /// its text is skipped).
    /// </summary>
    public void G_WorldDone()
    {
        World world = host.World!;
        world.G_WorldDone();

        if (gamemode == GameMode.commercial)
        {
            switch (world.gamemap)
            {
                case 15:
                case 31:
                    if (!world.secretexit)
                        break;
                    F_StartFinale();
                    break;
                case 6:
                case 11:
                case 20:
                case 30:
                    F_StartFinale();
                    break;
            }
        }
    }

    /// <summary>
    /// g_game.c <c>G_DoWorldDone</c> (<see cref="gameaction_t.ga_worlddone"/>):
    /// the next level (<see cref="World.NextMapName"/>) in the same world. A
    /// next map the WAD lacks (vanilla's <c>I_Error</c>) ends the game: back
    /// to the title loop.
    /// </summary>
    private void G_DoWorldDone()
    {
        World world = host.World!;
        string next = world.NextMapName();
        gamestate = gamestate_t.GS_LEVEL;
        if (!host.HasLump(next))
        {
            D_StartTitle($"{world.level.Name} completed: the next map, {next}, is not in the WAD");
            return;
        }
        if (!host.G_DoWorldDone(next))
        {
            D_StartTitle(null);
            return;
        }
        gameaction = gameaction_t.ga_nothing;
    }

    /// <summary>f_finale.c <c>F_StartFinale</c>'s game part: the game action is done and the finale shows (<see cref="FFinale.F_StartFinale"/>).</summary>
    private void F_StartFinale()
    {
        World world = host.World!;
        gameaction = gameaction_t.ga_nothing;
        gamestate = gamestate_t.GS_FINALE;
        Finale.F_StartFinale(world.gameepisode, world.gamemap);
    }

    /// <summary>
    /// d_main.c <c>D_StartTitle</c>: the title loop starts again from its
    /// first page. The level goes (the host's <see cref="IGameHost.EndGame"/>,
    /// with <paramref name="why"/> when the game stopped short). Vanilla's
    /// first step (<see cref="D_DoAdvanceDemo"/>) waits for the next tic's
    /// start (<see cref="D_AdvanceDemo"/>); here it is taken at once, so
    /// the title shows (and a script sees it) before a tic runs: the page's
    /// time runs from the same tic (SPEC §12 T7.1).
    /// </summary>
    public void D_StartTitle(string? why)
    {
        host.EndGame(why);
        gameaction = gameaction_t.ga_nothing;
        demosequence = -1;
        D_DoAdvanceDemo();
    }

    /// <summary>d_main.c <c>D_AdvanceDemo</c>: the title loop's next step at the start of the next tic.</summary>
    public void D_AdvanceDemo() => advancedemo = true;

    /// <summary>d_main.c <c>D_PageTicker</c>: the page's time runs down; then the next step.</summary>
    public void D_PageTicker()
    {
        if (--pagetic < 0)
            D_AdvanceDemo();
    }

    /// <summary>
    /// d_main.c <c>D_DoAdvanceDemo</c>: the title loop's next step. Vanilla
    /// alternates pages and demos (7 steps for the retail game, else 6):
    /// <c>TITLEPIC</c> (170 tics, Doom II 11 s), <c>DEMO1</c>, <c>CREDIT</c>
    /// (200), <c>DEMO2</c>, <c>HELP2</c> (200; the retail game <c>CREDIT</c>,
    /// Doom II <c>TITLEPIC</c> again for 11 s), <c>DEMO3</c> (and the retail
    /// game's <c>DEMO4</c>). There is no demo playback (SPEC §7.6): the demo
    /// steps are passed, so the pages follow each other; a page the WAD
    /// lacks is passed too (vanilla's <c>I_Error</c>), unless none is there.
    /// The title music (<c>mus_intro</c>, <c>mus_dm2ttl</c>) is T7.8's.
    /// </summary>
    public void D_DoAdvanceDemo()
    {
        if (host.World is { } world)
            world.players[world.consoleplayer].playerstate = playerstate_t.PST_LIVE; // not reborn
        advancedemo = false;
        usergame = false; // no save / end game here
        paused = false;
        gameaction = gameaction_t.ga_nothing;

        int steps = gamemode == GameMode.retail ? 7 : 6;
        for (int tries = 0; ; tries++)
        {
            demosequence = (demosequence + 1) % steps;
            (string? page, int tics) = DemoStep(demosequence);
            if (page is null)
                continue; // G_DeferedPlayDemo: no demo playback
            // Chocolate Doom: the BFG Edition's doom2.wad has no TITLEPIC; INTERPIC stands in
            if (gamevariant == GameVariant.bfgedition && page == "TITLEPIC" && !host.HasLump(page))
                page = "INTERPIC";
            if (!host.HasLump(page) && tries < 2 * steps)
                continue;
            pagename = page;
            pagetic = tics;
            gamestate = gamestate_t.GS_DEMOSCREEN;
            return;
        }
    }

    /// <summary>The page and its tics of title loop step <paramref name="step"/>, or no page for a demo.</summary>
    private (string? Page, int Tics) DemoStep(int step) => step switch
    {
        0 => ("TITLEPIC", gamemode == GameMode.commercial ? SimInfo.TICRATE * 11 : 170),
        2 => ("CREDIT", 200),
        4 => gamemode == GameMode.commercial ? ("TITLEPIC", SimInfo.TICRATE * 11)
            : gamemode == GameMode.retail ? ("CREDIT", 200) : ("HELP2", 200),
        _ => (null, 0), // demo1, demo2, demo3, demo4
    };

    /// <summary>The game state as the overlay shows it.</summary>
    public string StateText() => gamestate switch
    {
        gamestate_t.GS_LEVEL => "level",
        gamestate_t.GS_INTERMISSION => $"intermission ({Wi.state}, {Wi.cnt} tics)",
        gamestate_t.GS_FINALE => $"finale (stage {Finale.finalestage}, tic {Finale.finalecount})",
        _ => $"title loop: {pagename} ({pagetic} tics)",
    } + (paused ? ", paused" : "");

    /// <summary>The level script's names of the game states (<c>gamestate NAME</c>).</summary>
    public static string StateName(gamestate_t state) => state switch
    {
        gamestate_t.GS_LEVEL => "level",
        gamestate_t.GS_INTERMISSION => "intermission",
        gamestate_t.GS_FINALE => "finale",
        _ => "demoscreen",
    };
}
