using System;
using IsoDoom.Audio;
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

    /// <summary>
    /// d_main.c <c>D_Display</c>'s drawing after a tic (<see cref="GameFlow.D_Display"/>):
    /// on the level the status bar (<c>ST_Drawer</c>, whole with <paramref name="wipe"/>:
    /// the game state changed, vanilla's <c>redrawsbar</c>) and the message
    /// line (<c>HU_Drawer</c>), else the game state's screen. Nothing by
    /// default (the tests' hosts draw nothing).
    /// </summary>
    void D_Display(gamestate_t state, bool wipe)
    {
    }

    /// <summary>The game ended or stopped (<paramref name="why"/>) and the title loop starts (<see cref="GameFlow.D_StartTitle"/>): the level goes.</summary>
    void EndGame(string? why);

    /// <summary>
    /// T7.6: whether <paramref name="save"/> loads on this WAD
    /// (<see cref="SaveGameFile.LoadWorld"/> on its map, freshly loaded, the
    /// game shown left as it is); throws a <see cref="SaveGameException"/>
    /// with the player's message when not. Before <see cref="G_LoadGame"/>,
    /// which then can't fail. None by default (the tests' hosts load nothing).
    /// </summary>
    void G_CheckLoadGame(SaveGameFile save) => throw new SaveGameException(SaveGameFile.OTHERGAME);

    /// <summary>
    /// T7.6: g_game.c <c>G_DoLoadGame</c>'s level: the save's map and its
    /// world (<see cref="SaveGameFile.LoadWorld"/>) in place of the game
    /// shown, as <see cref="G_InitNew"/>'s new game. False when it can't be
    /// loaded (the host says why).
    /// </summary>
    bool G_LoadGame(SaveGameFile save) => false;
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
/// (<see cref="IGameHost"/>). The intermission is <see cref="WiStuff"/>
/// (T7.4), the finale <see cref="FFinale"/> (T7.5).
/// </summary>
public sealed class GameFlow
{
    private readonly IGameHost host;
    private gameaction_t _gameaction;

    public GameFlow(IGameHost host, GameMode gamemode, DoomRandom mrandom, GameVariant gamevariant = GameVariant.vanilla,
        GameMission gamemission = GameMission.none)
    {
        this.host = host;
        this.gamemode = gamemode;
        this.gamemission = gamemission != GameMission.none ? gamemission
            : gamemode == GameMode.commercial ? GameMission.doom2 : GameMission.doom;
        this.gamevariant = gamevariant;
        MRandom = mrandom;
        Wi = new WiStuff(this);
        Finale = new FFinale(this);
        Menu = new MMenu(this, gamevariant);
    }

    /// <summary>doomstat.h <c>gamemode</c>.</summary>
    public readonly GameMode gamemode;

    /// <summary>
    /// Chocolate Doom's <c>logical_gamemission</c> (the finale's texts, T7.5):
    /// the IWAD's mission, or without one (<see cref="GameMission.none"/>)
    /// Doom II's for the commercial game mode, else Doom's.
    /// </summary>
    public readonly GameMission gamemission;

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

    /// <summary>The intermission (wi_stuff.c, T7.4).</summary>
    public WiStuff Wi { get; }

    /// <summary>
    /// s_sound.c (T7.7, its music half T7.8c): the flow changes the music
    /// where vanilla's code does (<see cref="S_ChangeMusic"/>: the title
    /// loop, the levels' <c>S_Start</c>, the intermission, the finale) and
    /// pauses it with the game. Null: no sound (the tests' flows but the music's).
    /// </summary>
    public SSound? Sound;

    /// <summary>s_sound.c <c>S_ChangeMusic</c> (<see cref="Sound"/>'s; nothing without).</summary>
    public void S_ChangeMusic(musicenum_t musicnum, bool looping) => Sound?.S_ChangeMusic(musicnum, looping);

    /// <summary>s_sound.c <c>S_StartMusic</c>: <paramref name="m_id"/> once.</summary>
    public void S_StartMusic(musicenum_t m_id) => Sound?.S_StartMusic(m_id);

    /// <summary>
    /// p_setup.c <c>P_SetupLevel</c>'s <c>S_Start</c> after the host loaded
    /// <paramref name="map"/> (T7.7, T7.8c): the sounds stop and the level's
    /// song starts (the same song goes on). Vanilla calls it during the load;
    /// nothing in between makes a sound or changes the music.
    /// </summary>
    private void S_Start(string map) => Sound?.S_Start(World.EpisodeNumber(map), World.MapNumber(map));

    /// <summary>The finale (f_finale.c, T7.5).</summary>
    public FFinale Finale { get; }

    /// <summary>
    /// d_main.c <c>wipegamestate</c>: the game state the last
    /// <see cref="D_Display"/> showed; <see cref="GS_FORCEWIPE"/> (vanilla's
    /// −1) forces a wipe at the next. Vanilla's starts at the title loop's
    /// (<see cref="gamestate_t.GS_DEMOSCREEN"/>).
    /// </summary>
    public gamestate_t wipegamestate = gamestate_t.GS_DEMOSCREEN;

    /// <summary>d_main.c's <c>wipegamestate = -1</c>: no game state, so the next <see cref="D_Display"/> wipes.</summary>
    public const gamestate_t GS_FORCEWIPE = (gamestate_t)(-1);

    /// <summary>
    /// T7.1a: whether <see cref="D_Display"/> melts the screen when the game
    /// state changes (the option, <c>video/wipe</c>, <c>--level-wipe</c>).
    /// Off, as vanilla's <c>-nodraw</c> (<c>nodrawers</c>: no
    /// <c>D_Display</c>, so no wipe and no <c>M_Random</c> drawn for it),
    /// unless the game scene turns it on: the tests' flows compare with
    /// vanilla's runs without drawing.
    /// </summary>
    public bool Wipes;

    /// <summary>Called after each tic of the game state, before <see cref="gametic"/> counts it and <see cref="D_Display"/> (the vanilla reference's dump point: the tests').</summary>
    public Action? Ticked;

    /// <summary>The melt (f_wipe.c, T7.1a): under way while <see cref="FWipe.go"/>; no tic runs meanwhile (<see cref="CanTic"/>).</summary>
    public FWipe Wipe { get; } = new();

    /// <summary>
    /// Whether a tic can run: always but on a level without a player mobj
    /// (no world, or none spawned), as before T7.1, unless a game action or
    /// the title loop's step is due, and never while the screen melts
    /// (T7.1a: d_main.c's wipe loop runs no tics).
    /// </summary>
    public bool CanTic =>
        !Wipe.go && (gamestate != gamestate_t.GS_LEVEL || advancedemo || gameaction != gameaction_t.ga_nothing
            || host.World is { } w && w.players[w.consoleplayer].mo is not null);

    /// <summary>
    /// g_game.c <c>G_Ticker</c> for the console player's <paramref name="ticcmd"/>,
    /// after d_main.c's title loop step (<see cref="D_DoAdvanceDemo"/>, which
    /// vanilla's tic loop runs before it): the game actions due, the special
    /// buttons (<c>BTS_PAUSE</c> toggles <see cref="paused"/>; <c>BTS_SAVEGAME</c>
    /// saves, T7.6: <see cref="G_SaveGame"/> puts it in this tic's buttons), then the tic of the <see cref="gamestate"/> and d_main.c's
    /// <see cref="D_Display"/> (T7.1a: after every tic, as vanilla's at 35
    /// frames a second or more). The game actions the tic set run at its end
    /// (not vanilla's place, which is the next tic's start, after
    /// <c>D_Display</c>: nothing runs in between, so the same order; a
    /// scripted run sees the next level or the intermission without
    /// another tic; SPEC §12 T5.8, T6.12, T7.1).
    /// </summary>
    public void G_Ticker(in ticcmd_t ticcmd)
    {
        // g_game.c G_BuildTiccmd's special buttons: a save asked for (T7.6) goes with this tic, in place of the buttons
        ticcmd_t cmd = ticcmd;
        if (sendsave)
        {
            sendsave = false;
            cmd.buttons = (byte)(buttoncode_t.BT_SPECIAL | buttoncode_t.BTS_SAVEGAME | (savegameslot << buttoncode_t.BTS_SAVESHIFT));
        }

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
        if ((cmd.buttons & buttoncode_t.BT_SPECIAL) != 0)
        {
            switch (cmd.buttons & buttoncode_t.BT_SPECIALMASK)
            {
                case buttoncode_t.BTS_PAUSE:
                    paused = !paused;
                    if (paused)
                        Sound?.S_PauseSound();
                    else
                        Sound?.S_ResumeSound();
                    break;
                case buttoncode_t.BTS_SAVEGAME:
                    if (savedescription.Length == 0)
                        savedescription = "NET GAME";
                    savegameslot = (cmd.buttons & buttoncode_t.BTS_SAVEMASK) >> buttoncode_t.BTS_SAVESHIFT;
                    gameaction = gameaction_t.ga_savegame;
                    break;
            }
        }

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
        Ticked?.Invoke();
        gametic++;

        D_Display();

        G_DoGameActions();
    }

    /// <summary>
    /// d_main.c <c>D_Display</c>'s game part, after each tic (T7.1a): the
    /// host draws (<see cref="IGameHost.D_Display"/>: the level's status bar
    /// and message line, the bar whole after a game state change, or the
    /// state's screen), and, when the game state is
    /// not the one shown last (<see cref="wipegamestate"/>), a wipe
    /// (<c>wipe_StartScreen</c>, <c>wipe_EndScreen</c> are the game scene's;
    /// the melt's columns are set up at once, <see cref="FWipe.wipe_initMelt"/>,
    /// as no tic runs before vanilla's first <c>wipe_ScreenWipe</c> does it)
    /// with <see cref="Wipes"/>. The game scene also draws the screens every
    /// frame (<c>LevelScene.UpdateScreens</c>).
    /// </summary>
    public void D_Display()
    {
        // save the current screen if about to wipe
        bool wipe = gamestate != wipegamestate;
        host.D_Display(gamestate, wipe); // ST_Drawer (…, redrawsbar), HU_Drawer; WI_Drawer, F_Drawer, D_PageDrawer
        wipegamestate = gamestate;
        if (wipe && Wipes)
            Wipe.wipe_initMelt(MRandom);
    }

    /// <summary>
    /// d_main.c's wipe loop (T7.1a): a step of the melt under way
    /// (<see cref="FWipe.wipe_ScreenWipe"/>, one tic's), every 1/35 s while no
    /// tic runs; true once it is over (or none was under way).
    /// </summary>
    public bool WipeStep() => Wipe.wipe_ScreenWipe(1);

    /// <summary>g_game.c <c>G_DoLoadLevel</c>'s forced wipe: a level loaded over a level (a reborn, a new game) melts too.</summary>
    private void G_DoLoadLevelWipe()
    {
        if (wipegamestate == gamestate_t.GS_LEVEL)
            wipegamestate = GS_FORCEWIPE; // force a wipe
    }

    /// <summary>
    /// p_tick.c <c>P_Ticker</c>'s hold while the menu is up in single player
    /// (<c>menuactive &amp;&amp; !demoplayback &amp;&amp; viewz != 1</c>: the first tic of a
    /// level the player came to alive, whose <c>P_SetupLevel</c> set
    /// <c>viewz</c> to 1, still runs; a reborn player's is 0, so held at once).
    /// </summary>
    public bool MenuHolds(World world) =>
        !world.netgame && Menu.menuactive && world.players[world.consoleplayer].viewz != 1;

    /// <summary>
    /// s_sound.c <c>S_StartSound(NULL, …)</c> for the game's screens (the
    /// intermission's, T7.4): to the menus' host, whose UI sounds play from T7.7.
    /// </summary>
    public void S_StartSound(sfxenum_t sfx) => Menu.Host?.StartSound(sfx);

    /// <summary>g_game.c <c>savegameslot</c> and <c>savedescription</c>: the save the menu asked for last.</summary>
    public int savegameslot = -1;

    /// <inheritdoc cref="savegameslot"/>
    public string savedescription = "";

    /// <summary>g_game.c <c>sendsave</c>: the next tic carries <c>BTS_SAVEGAME</c> (<see cref="G_SaveGame"/>).</summary>
    public bool sendsave;

    /// <summary>d_englsh.h <c>GGSAVED</c>: the message after a save.</summary>
    public const string GGSAVED = "game saved.";

    /// <summary>
    /// T7.6: the directory of the save slots' files (Chocolate Doom's
    /// <c>savegamedir</c>: the host's, one per IWAD); null: no saves (the
    /// slots show empty, nothing is written).
    /// </summary>
    public string? SaveDir;

    /// <summary>T7.6: where the flow says what it saved or why a save didn't load (the host prints it).</summary>
    public Action<string>? Log;

    /// <summary>p_saveg.c <c>P_SaveGameFile</c>: slot <paramref name="slot"/>'s file, or null without <see cref="SaveDir"/>.</summary>
    public string? SaveGamePath(int slot) => SaveDir is null || slot < 0 ? null : System.IO.Path.Combine(SaveDir, SaveGameFile.SlotFileName(slot));

    /// <summary>
    /// m_menu.c <c>M_ReadSaveStrings</c>' read (the menus' host's
    /// <see cref="IMenuHost.SaveDescription"/>): slot <paramref name="slot"/>'s
    /// description, null for an empty slot; a file that is no save shows as
    /// <c>?</c> (loading it says so).
    /// </summary>
    public string? SaveDescription(int slot)
    {
        if (SaveGamePath(slot) is not { } path || !System.IO.File.Exists(path))
            return null;
        try
        {
            return SaveGameFile.ReadDescription(System.IO.File.ReadAllBytes(path)) ?? "?";
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            return "?";
        }
    }

    /// <summary>
    /// g_game.c <c>G_SaveGame</c> (the save menu's <c>M_DoSave</c>): the
    /// next tic carries <c>BTS_SAVEGAME</c> (<see cref="sendsave"/>), which
    /// saves after it (<see cref="G_DoSaveGame"/>).
    /// </summary>
    public void G_SaveGame(int slot, string description)
    {
        savegameslot = slot;
        savedescription = description;
        sendsave = true;
    }

    /// <summary>g_game.c <c>G_LoadGame</c> (the load menu's): <see cref="gameaction_t.ga_loadgame"/>, before the next tic (<see cref="G_DoLoadGame"/>).</summary>
    public void G_LoadGame(int slot)
    {
        savegameslot = slot;
        gameaction = gameaction_t.ga_loadgame;
    }

    /// <summary>
    /// g_game.c <c>G_DoSaveGame</c> (<see cref="gameaction_t.ga_savegame"/>):
    /// the level's game (<see cref="SaveGameFile.Write"/>) to slot
    /// <see cref="savegameslot"/>'s file with <see cref="savedescription"/>
    /// (written aside, then put in place: Chocolate Doom's), and the console
    /// player's <see cref="GGSAVED"/>. Only on a level the user started
    /// (<see cref="usergame"/>, as the save menu checks); a file that can't
    /// be written says so in the menus' message box (vanilla's <c>I_Error</c>).
    /// Also the level script's <c>save</c>. Returns whether it saved.
    /// </summary>
    public bool G_DoSaveGame()
    {
        gameaction = gameaction_t.ga_nothing;
        World? world = host.World;
        string? path = SaveGamePath(savegameslot);
        if (world is null || gamestate != gamestate_t.GS_LEVEL || !usergame || path is null)
        {
            Log?.Invoke($"Save: nothing saved (slot {savegameslot}): {(path is null ? "no save directory" : "no game on a level")}");
            savedescription = "";
            return false;
        }
        try
        {
            byte[] data = SaveGameFile.Write(world, savedescription);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            System.IO.File.WriteAllBytes(temp, data);
            System.IO.File.Move(temp, path, overwrite: true);
            Log?.Invoke($"Save: {world.level.Name} at tic {world.leveltime} saved to slot {savegameslot} (\"{savedescription}\", {data.Length} bytes): {path}");
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            Log?.Invoke($"Save: slot {savegameslot}: {e.Message}");
            Menu.M_StartMessage("the game couldn't be saved.\n\n" + MMenu.PRESSKEY, null, false);
            savedescription = "";
            return false;
        }
        savedescription = "";
        world.players[world.consoleplayer].message = GGSAVED;
        return true;
    }

    /// <summary>
    /// g_game.c <c>G_DoLoadGame</c> (<see cref="gameaction_t.ga_loadgame"/>):
    /// slot <see cref="savegameslot"/>'s file, refused with the menus' message
    /// box when it is no save, from another version, damaged or another
    /// game's (vanilla returns silently on another version); else as
    /// <see cref="G_InitNew"/> (unpaused, <c>M_ClearRandom</c>, a user game,
    /// its skill, the forced wipe of <c>G_DoLoadLevel</c>, so a load melts
    /// over a level too) the host shows the save's level and world
    /// (<see cref="IGameHost.G_LoadGame"/>). Returns whether it loaded.
    /// </summary>
    public bool G_DoLoadGame()
    {
        gameaction = gameaction_t.ga_nothing;
        string? path = SaveGamePath(savegameslot);
        SaveGameFile save;
        try
        {
            if (path is null || !System.IO.File.Exists(path))
                throw new SaveGameException(EMPTYSLOT);
            save = SaveGameFile.Read(System.IO.File.ReadAllBytes(path));
            if (save.GameMode != gamemode)
                throw new SaveGameException(SaveGameFile.OTHERGAME);
            host.G_CheckLoadGame(save);
        }
        catch (Exception e) when (e is SaveGameException or System.IO.IOException or UnauthorizedAccessException)
        {
            string reason = e is SaveGameException ? e.Message : SaveGameFile.DAMAGED;
            Log?.Invoke($"Load: slot {savegameslot} refused: {e.Message.Replace('\n', ' ')}{(e.InnerException is { } inner ? $" ({inner.Message})" : "")}");
            LoadRefused = reason;
            Menu.M_StartMessage(reason + "\n\n" + MMenu.PRESSKEY, null, false);
            return false;
        }

        // g_game.c G_InitNew's
        if (paused)
        {
            paused = false;
            Sound?.S_ResumeSound();
        }
        advancedemo = false; // not vanilla: as G_InitNewMap
        MRandom.M_ClearRandom();
        usergame = true;
        gameskill = save.Skill;
        G_DoLoadLevelWipe();
        if (!host.G_LoadGame(save))
        {
            D_StartTitle(null);
            return false;
        }
        LoadRefused = null;
        gamestate = gamestate_t.GS_LEVEL;
        S_Start(save.Map); // G_DoLoadLevel's P_SetupLevel
        _gameaction = gameaction_t.ga_nothing;
        Log?.Invoke($"Load: slot {savegameslot}, \"{save.Description}\": {save.Map} at tic {save.LevelTime}");
        return true;
    }

    /// <summary>T7.6: the message of the last load refused (<see cref="G_DoLoadGame"/>), null after a load (the tests', the level script's).</summary>
    public string? LoadRefused;

    /// <summary>The message for a load from an empty slot (the quickload's slot emptied since, a script's).</summary>
    public const string EMPTYSLOT = "this save slot is empty.";

    /// <summary>
    /// g_game.c <c>G_Ticker</c>'s "do things to change the game state": runs
    /// the game actions until none is left (T7.6: <c>ga_loadgame</c>,
    /// <c>ga_savegame</c>); there is no demo playback (SPEC §7.6)
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
                case gameaction_t.ga_loadgame:
                    G_DoLoadGame();
                    break;
                case gameaction_t.ga_savegame:
                    G_DoSaveGame();
                    break;
                default:
                    // ga_playdemo, ga_screenshot: not ported
                    gameaction = gameaction_t.ga_nothing;
                    break;
            }
        }
    }

    /// <summary>
    /// g_game.c <c>G_DoLoadLevel</c> for <see cref="gameaction_t.ga_loadlevel"/>
    /// (a reborn): the host reloads the level (whose world's
    /// <c>G_DoLoadLevel</c> ends the action), with vanilla's forced wipe (T7.1a).
    /// </summary>
    private void G_DoLoadLevel()
    {
        G_DoLoadLevelWipe();
        gamestate = gamestate_t.GS_LEVEL;
        string? map = host.World?.level.Name;
        if (!host.G_DoLoadLevel())
            D_StartTitle(null);
        else if (map is not null)
            S_Start(map);
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
        if (paused)
        {
            paused = false;
            Sound?.S_ResumeSound();
        }
        advancedemo = false; // not vanilla: a title loop step due (the game started before the title's first tic) is dropped
        MRandom.M_ClearRandom();
        usergame = true; // will be set false if a demo
        gameskill = skill;
        // the sky (R_TextureNumForName("SKY1"…)) is the presentation's: never drawn (SPEC §7.2)
        G_DoLoadLevelWipe();
        if (!host.G_InitNew(skill, map))
        {
            D_StartTitle(null);
            return;
        }
        gamestate = gamestate_t.GS_LEVEL;
        _gameaction = gameaction_t.ga_nothing;
        S_Start(map); // G_DoLoadLevel's P_SetupLevel
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
        G_DoLoadLevelWipe();
        if (!host.G_DoWorldDone(next))
        {
            D_StartTitle(null);
            return;
        }
        gameaction = gameaction_t.ga_nothing;
        S_Start(next); // G_DoLoadLevel's P_SetupLevel
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
    /// The title music (T7.8c): <c>mus_intro</c> once at the first step (Doom
    /// II <c>mus_dm2ttl</c>, at its fifth too); without the demos the song
    /// asked for is often the one playing, which goes on (vanilla's demos
    /// play their levels' songs in between).
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
            if (demosequence == 0 || demosequence == 4 && gamemode == GameMode.commercial)
            {
                gamestate = gamestate_t.GS_DEMOSCREEN; // vanilla's order: the page's state, then its song
                S_StartMusic(gamemode == GameMode.commercial ? musicenum_t.mus_dm2ttl : musicenum_t.mus_intro);
            }
            (string? page, int tics) = DemoStep(demosequence);
            if (page is null)
            {
                // G_DeferedPlayDemo: no demo playback. Vanilla wipes to the demo and back
                // (GS_LEVEL between the pages): one wipe from page to page (T7.1a)
                wipegamestate = GS_FORCEWIPE;
                continue;
            }
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
        gamestate_t.GS_INTERMISSION => $"intermission ({Wi.StateText()})",
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
