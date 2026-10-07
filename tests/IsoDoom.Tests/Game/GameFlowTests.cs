using System;
using System.Collections.Generic;
using IsoDoom.Game;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Game;

/// <summary>
/// T7.1: g_game.c's game flow and d_main.c's title loop (<see cref="GameFlow"/>)
/// with a host that keeps a real world on the synthetic IWAD's maps: the
/// title loop's pages and tics, a new game (and <c>G_InitNew</c>'s clamps),
/// the exits through the (placeholder) intermission to the next map with
/// vanilla's timing, the finale, Doom II's text screens, the pause, and a
/// next map the WAD lacks.
/// </summary>
public class GameFlowTests
{
    private static readonly WadArchive Wad = new(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) });

    internal sealed class Host : IGameHost
    {
        private readonly GameMode mode;

        public Host(GameMode mode) => this.mode = mode;

        public World? World { get; private set; }

        /// <summary>The lumps the WAD has besides the synthetic IWAD's (the title loop's pages).</summary>
        public HashSet<string> Extra { get; } = new();

        public List<string> Loads { get; } = new();
        public int Completed, LevelTics, PausedTics;
        public string? Ended;

        public bool HasLump(string name) => Extra.Contains(name) || Wad.W_CheckNumForName(name) >= 0;

        public bool G_InitNew(skill_t skill, string map)
        {
            Loads.Add("new " + map);
            if (Wad.W_CheckNumForName(map) < 0)
                return false;
            World = new World(new SpawnSettings(mode, skill), Tweaks.Vanilla);
            World.G_DoLoadLevel(Level.Load(Wad, map));
            return true;
        }

        public bool G_DoWorldDone(string map)
        {
            Loads.Add("next " + map);
            World!.G_DoWorldDone(Level.Load(Wad, map));
            return true;
        }

        public bool G_DoLoadLevel()
        {
            Loads.Add("reborn " + World!.level.Name);
            World.G_DoLoadLevel(Level.Load(Wad, World.level.Name));
            return true;
        }

        public void G_LevelTicker(in ticcmd_t cmd, bool paused)
        {
            if (paused)
                PausedTics++;
            else
            {
                World!.G_Ticker(cmd);
                LevelTics++;
            }
        }

        public void LevelCompleted() => Completed++;

        public void EndGame(string? why)
        {
            Ended = why;
            World = null;
        }
    }

    internal static (GameFlow Flow, Host Host) New(GameMode mode = GameMode.shareware)
    {
        var host = new Host(mode);
        return (new GameFlow(host, mode, new DoomRandom()), host);
    }

    private static readonly ticcmd_t None = default;
    private static readonly ticcmd_t Use = new() { buttons = buttoncode_t.BT_USE };
    private static readonly ticcmd_t Pause = new() { buttons = buttoncode_t.BT_SPECIAL | buttoncode_t.BTS_PAUSE };

    private static void Tics(GameFlow flow, int n, ticcmd_t cmd = default)
    {
        for (int i = 0; i < n; i++)
            flow.G_Ticker(cmd);
    }

    // ---- the title loop ----

    [Theory]
    [InlineData(GameMode.shareware, "TITLEPIC:170 CREDIT:200 HELP2:200")]
    [InlineData(GameMode.registered, "TITLEPIC:170 CREDIT:200 HELP2:200")]
    [InlineData(GameMode.retail, "TITLEPIC:170 CREDIT:200 CREDIT:200")]
    [InlineData(GameMode.commercial, "TITLEPIC:385 CREDIT:200 TITLEPIC:385")]
    public void TheTitleLoopShowsVanillasPagesWithoutTheDemos(GameMode mode, string expected)
    {
        (GameFlow flow, Host host) = New(mode);
        host.Extra.UnionWith(new[] { "CREDIT", "HELP2" });
        flow.D_StartTitle(null);
        Assert.Equal(gamestate_t.GS_DEMOSCREEN, flow.gamestate);
        var shown = new List<string>();
        for (int round = 0; round < 6; round++)
        {
            string page = flow.pagename;
            int tics = flow.pagetic;
            shown.Add($"{page}:{tics}");
            // D_PageTicker: the page shows until its pagetic runs below 0, then the next one at the next tic
            Tics(flow, tics + 1);
            Assert.Equal(page, flow.pagename);
            Assert.True(flow.advancedemo);
            flow.G_Ticker(None);
            Assert.False(flow.advancedemo);
            Assert.Equal(gamestate_t.GS_DEMOSCREEN, flow.gamestate);
            flow.pagetic++; // as D_DoAdvanceDemo set it (the tic's D_PageTicker took one)
        }
        Assert.Equal(expected + " " + expected, string.Join(' ', shown));
        Assert.False(flow.usergame);
        Assert.Null(host.World);
    }

    [Fact]
    public void TheTitleLoopPassesPagesTheWadLacks()
    {
        (GameFlow flow, _) = New(); // the synthetic IWAD has TITLEPIC only
        flow.D_StartTitle(null);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(("TITLEPIC", 170), (flow.pagename, flow.pagetic));
            Tics(flow, 172);
            flow.pagetic++;
        }
    }

    // ---- a new game ----

    [Fact]
    public void ANewGameFromTheTitleStartsE1M1()
    {
        (GameFlow flow, Host host) = New();
        flow.MRandom.M_Random();
        flow.D_StartTitle(null);
        Tics(flow, 5);
        flow.G_DeferedInitNew(skill_t.sk_hard, 1, 1);
        flow.G_Ticker(None); // G_DoNewGame at the tic's start, then the level's first tic
        Assert.Equal(gamestate_t.GS_LEVEL, flow.gamestate);
        Assert.Equal(new[] { "new E1M1" }, host.Loads);
        Assert.Equal(("E1M1", skill_t.sk_hard, 1), (host.World!.level.Name, host.World.gameskill, host.World.leveltime));
        Assert.Equal(0, flow.MRandom.rndindex); // M_ClearRandom
        Assert.True(flow.usergame);
        Assert.Equal(gameaction_t.ga_nothing, flow.gameaction);
    }

    [Theory]
    [InlineData(GameMode.shareware, 3, 5, "E1M5")]
    [InlineData(GameMode.shareware, 1, 12, "E1M9")]
    [InlineData(GameMode.registered, 4, 1, "E3M1")]
    [InlineData(GameMode.retail, 7, 0, "E4M1")]
    [InlineData(GameMode.commercial, 2, 12, "MAP12")]
    public void G_InitNewClampsTheEpisodeAndMap(GameMode mode, int episode, int map, string expected)
    {
        (GameFlow flow, Host host) = New(mode);
        flow.G_InitNew(skill_t.sk_medium, episode, map);
        Assert.Equal("new " + expected, host.Loads[0]);
        Assert.Equal(gamestate_t.GS_DEMOSCREEN, flow.gamestate); // not in the synthetic IWAD: back to the title loop
    }

    // ---- the exits, the intermission ----

    [Fact]
    public void AnExitGoesThroughTheIntermissionToTheNextMap()
    {
        (GameFlow flow, Host host) = New();
        flow.G_InitNewMap(skill_t.sk_medium, "E1M1");
        Tics(flow, 5);
        World world = host.World!;
        player_t p = world.players[0];
        p.health = p.mo!.health = 77;
        world.G_ExitLevel();
        p.usedown = true; // the exit switch's use, still held

        flow.G_Ticker(Use); // G_DoCompleted, then the intermission's first tic
        Assert.Equal((gamestate_t.GS_INTERMISSION, WiStuff.stateenum_t.StatCount, 1), (flow.gamestate, flow.Wi.state, host.Completed));
        Assert.Equal((0, 0, 1), (world.wminfo.epsd, world.wminfo.last, world.wminfo.next));
        Assert.Equal(5, world.wminfo.plyr[0].stime);
        Tics(flow, 100, Use); // held: no skip
        Assert.Equal(WiStuff.stateenum_t.StatCount, flow.Wi.state);
        Assert.NotEqual(10, flow.Wi.sp_state);
        Tics(flow, 200);
        Assert.Equal((WiStuff.stateenum_t.StatCount, 10), (flow.Wi.state, flow.Wi.sp_state)); // counted out
        Assert.Equal(5, host.LevelTics); // the level holds still

        flow.G_Ticker(Use); // a press: the next location
        Assert.Equal((WiStuff.stateenum_t.ShowNextLoc, WiStuff.SHOWNEXTLOCDELAY * SimInfo.TICRATE), (flow.Wi.state, flow.Wi.cnt));
        Tics(flow, WiStuff.SHOWNEXTLOCDELAY * SimInfo.TICRATE - 1);
        Assert.Equal(WiStuff.stateenum_t.ShowNextLoc, flow.Wi.state);
        flow.G_Ticker(None);
        Assert.Equal((WiStuff.stateenum_t.NoState, 10), (flow.Wi.state, flow.Wi.cnt));
        Tics(flow, 9);
        Assert.Equal(gamestate_t.GS_INTERMISSION, flow.gamestate);
        flow.G_Ticker(None); // WI_End, G_WorldDone: the next map at the tic's end
        Assert.Equal(gamestate_t.GS_LEVEL, flow.gamestate);
        Assert.Equal(new[] { "new E1M1", "next E1M2" }, host.Loads);
        Assert.Same(world, host.World);
        Assert.Equal(("E1M2", 0, 77), (world.level.Name, world.leveltime, p.health));
        flow.G_Ticker(None);
        Assert.Equal(1, world.leveltime);
    }

    [Fact]
    public void FireOrUseSkipsTheNextLocation()
    {
        (GameFlow flow, Host host) = New();
        flow.G_InitNewMap(skill_t.sk_medium, "E1M1");
        host.World!.G_ExitLevel();
        int tics = 0;
        ticcmd_t[] cmds = { None, new() { buttons = buttoncode_t.BT_ATTACK } };
        while (flow.gamestate != gamestate_t.GS_LEVEL || host.World.level.Name != "E1M2")
        {
            flow.G_Ticker(cmds[tics % 2]);
            Assert.True(++tics < 100);
        }
        Assert.Equal(1 + 5 + 10, tics); // the exit's tic, presses (all the stats, the next location, the end) and releases, then NoState's 10
    }

    [Fact]
    public void ANextMapTheWadLacksEndsTheGameAtTheTitle()
    {
        (GameFlow flow, Host host) = New();
        flow.G_InitNewMap(skill_t.sk_medium, "E1M2");
        host.World!.G_SecretExitLevel();
        flow.G_Ticker(None);
        Assert.Equal(8, host.World.wminfo.next); // E1M9
        for (int i = 0; i < 40 && flow.gamestate == gamestate_t.GS_INTERMISSION; i++)
            flow.G_Ticker(i % 2 == 0 ? None : Use);
        Assert.Equal(gamestate_t.GS_DEMOSCREEN, flow.gamestate);
        Assert.Null(host.World);
        Assert.Contains("E1M9", host.Ended);
        Assert.Equal("TITLEPIC", flow.pagename);
    }

    // ---- the finale ----

    [Fact]
    public void TheEpisodesEndShowsTheFinaleThenTheEndPicture()
    {
        (GameFlow flow, Host host) = New();
        flow.G_InitNewMap(skill_t.sk_medium, "E1M1");
        host.World!.gamemap = 8;
        host.World.G_ExitLevel();
        flow.G_Ticker(Use);
        Assert.Equal((gamestate_t.GS_FINALE, 0, 1), (flow.gamestate, flow.Finale.finalestage, host.Completed)); // no intermission
        Assert.Equal("FLOOR4_8", flow.Finale.finaleflat);
        int textTics = flow.Finale.finaletext.Length * FFinale.TEXTSPEED + FFinale.TEXTWAIT;
        Tics(flow, textTics - 1, Use); // no skipping before Doom II
        Assert.Equal(0, flow.Finale.finalestage);
        flow.G_Ticker(None);
        Assert.Equal((1, 0), (flow.Finale.finalestage, flow.Finale.finalecount));
        Tics(flow, 200);
        Assert.Equal(gamestate_t.GS_FINALE, flow.gamestate); // vanilla stays on the end picture
        Tics(flow, 10, Use);
        Assert.Equal(gamestate_t.GS_FINALE, flow.gamestate); // fire and use too (T7.2)
        // the menu's End Game goes back to the title loop
        MMenu menu = flow.Menu;
        Assert.True(menu.M_Responder(MMenu.KEY_ESCAPE));
        Assert.True(menu.M_Responder('o'));
        Assert.True(menu.M_Responder(MMenu.KEY_ENTER));
        Assert.Equal("options", menu.StateName);
        Assert.True(menu.M_Responder(MMenu.KEY_ENTER)); // End Game
        Assert.True(menu.M_Responder('y'));
        Assert.Equal(gamestate_t.GS_DEMOSCREEN, flow.gamestate);
        Assert.Null(host.World);
    }

    [Theory]
    [InlineData(6, false, true)]
    [InlineData(5, false, false)]
    [InlineData(15, true, true)]
    [InlineData(15, false, false)]
    public void Doom2sTextScreensComeAfterTheIntermission(int map, bool secret, bool text)
    {
        (GameFlow flow, Host host) = New(GameMode.commercial);
        flow.G_InitNewMap(skill_t.sk_medium, "E1M1");
        World world = host.World!;
        world.gamemap = map;
        if (secret)
            world.G_SecretExitLevel();
        else
            world.G_ExitLevel();
        flow.G_Ticker(None);
        Assert.Equal(gamestate_t.GS_INTERMISSION, flow.gamestate);
        flow.G_Ticker(Use); // all the stats at once
        flow.G_Ticker(None);
        flow.G_Ticker(Use); // Doom II: straight to NoState
        Assert.Equal(WiStuff.stateenum_t.NoState, flow.Wi.state);
        Tics(flow, 10);
        if (!text)
        {
            Assert.Equal(gamestate_t.GS_DEMOSCREEN, flow.gamestate); // the next MAPxx is not in the synthetic IWAD
            return;
        }
        Assert.Equal(gamestate_t.GS_FINALE, flow.gamestate);
        Tics(flow, 51, Use); // no skip before 50 tics
        Assert.Equal(gamestate_t.GS_FINALE, flow.gamestate);
        flow.G_Ticker(Use); // any button: the next level
        Assert.Equal(gamestate_t.GS_DEMOSCREEN, flow.gamestate);
        Assert.Contains(secret ? "MAP31" : "MAP07", host.Ended);
    }

    // ---- the pause, a reborn ----

    [Fact]
    public void ThePauseHoldsTheLevel()
    {
        (GameFlow flow, Host host) = New();
        flow.G_InitNewMap(skill_t.sk_medium, "E1M1");
        Tics(flow, 3);
        flow.G_Ticker(Pause);
        Tics(flow, 10);
        Assert.True(flow.paused);
        Assert.Equal((3, 11), (host.World!.leveltime, host.PausedTics));
        flow.G_Ticker(Pause);
        Assert.False(flow.paused);
        Assert.Equal(4, host.World.leveltime);
        flow.G_Ticker(Pause);
        flow.G_InitNewMap(skill_t.sk_medium, "E1M1");
        Assert.False(flow.paused); // G_InitNew unpauses
    }

    [Fact]
    public void ADeadPlayersUseReloadsTheLevel()
    {
        (GameFlow flow, Host host) = New();
        flow.G_InitNewMap(skill_t.sk_medium, "E1M2");
        World world = host.World!;
        world.P_DamageMobj(world.players[0].mo!, null, null, 10000);
        Tics(flow, 2);
        flow.G_Ticker(Use); // P_DeathThink: PST_REBORN, G_DoReborn: ga_loadlevel, run at the tic's end
        Assert.Equal(new[] { "new E1M2", "reborn E1M2" }, host.Loads);
        Assert.Equal((gamestate_t.GS_LEVEL, gameaction_t.ga_nothing, 0, 100), (flow.gamestate, flow.gameaction, world.leveltime, world.players[0].health));
    }
}
