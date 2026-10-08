using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using IsoDoom.Game;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Game;

/// <summary>
/// T7.4: wi_stuff.c's intermission (<see cref="WiStuff"/>). Against vanilla:
/// each DOOM1.WAD exit route (but E1M8's, whose exit goes to the finale)
/// played through the game flow to its intermission, which then runs tic by
/// tic as vanilla's did with the same presses, compared with the dump of
/// <c>tools/VanillaRef/intermissions.sh</c> (the stats and par it starts
/// with; every tic's state, counters, animations, <c>M_Random</c> index,
/// sounds and the hash of every pixel drawn); skips without DOOM1.WAD or the
/// dumps. Without a WAD: the par tables, the counting's timing, its sounds
/// and the skipping on the synthetic IWAD.
/// </summary>
public class IntermissionTests
{
    // ---- against vanilla ----

    internal sealed class RouteHost : IGameHost
    {
        private readonly WadArchive _wad;
        public readonly StStuff St;

        public RouteHost(World world, WadArchive wad, DoomRandom mrandom)
        {
            World = world;
            this._wad = wad;
            St = new StStuff(new StStuff.Graphics(), mrandom);
            St.ST_Start(world.players[world.consoleplayer]);
        }

        public World? World { get; private set; }
        public bool HasLump(string name) => _wad.W_CheckNumForName(name) >= 0;
        public bool G_InitNew(skill_t skill, string map) => throw new InvalidOperationException();
        public bool G_DoLoadLevel() => throw new InvalidOperationException();

        public bool G_DoWorldDone(string map)
        {
            World!.G_DoWorldDone(Level.Load(_wad, map));
            return true;
        }

        public void G_LevelTicker(in ticcmd_t cmd, bool paused)
        {
            World!.G_Ticker(cmd);
            St.ST_Ticker(); // the status bar's M_Random, once a tic
        }

        public void LevelCompleted()
        {
        }

        public void EndGame(string? why) => World = null;
    }

    /// <summary>The DOOM1.WAD exit routes with an intermission (all but E1M8's).</summary>
    private static List<VanillaRoute> ExitRoutes() =>
        [.. VanillaRoute.Names().Select(VanillaRoute.Load).Where(r => r.Iwad == "doom1" && r.Exit != 0 && r.Map != "E1M8")];

    /// <summary>The exit routes with an intermission and the dumps' suffixes (<c>wi</c>: counted out; <c>wiskip</c>: skipped).</summary>
    public static TheoryData<string, string> Dumps()
    {
        var data = new TheoryData<string, string>();
        foreach (VanillaRoute r in ExitRoutes())
        {
            data.Add(r.Name, "wi");
            data.Add(r.Name, "wiskip");
        }
        return data;
    }

    [Fact]
    public void EveryEpisode1ExitHasAnIntermissionToCompare()
    {
        List<VanillaRoute> routes = ExitRoutes();
        Assert.Equal(["E1M1", "E1M2", "E1M3", "E1M4", "E1M5", "E1M6", "E1M7", "E1M9"], routes.Select(r => r.Map).Distinct().Order());
        Assert.Contains(routes, r => r.Map == "E1M3" && r.Exit == 2);
    }

    [Theory]
    [MemberData(nameof(Dumps))]
    public void MatchesVanilla(string name, string suffix)
    {
        var route = VanillaRoute.Load(name);
        TestWads.RequireDoom1();
        string? dir = Environment.GetEnvironmentVariable(VanillaRoute.DumpDirEnvVar);
        if (string.IsNullOrEmpty(dir))
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "isodoom", "vanilla-routes");
        string path = Path.Combine(dir, $"{name}.{suffix}");
        if (!File.Exists(path))
            Assert.Skip($"No vanilla dump {path}: run tools/VanillaRef/intermissions.sh (or set {VanillaRoute.DumpDirEnvVar}).");
        string[] expected = [.. File.ReadAllLines(path).Where(l => l.Length > 0)];
        Assert.StartsWith("presses ", expected[0]);
        Assert.StartsWith("wminfo ", expected[1]);
        HashSet<int> presses = [.. expected[0]["presses ".Length..].Split(',').Select(int.Parse)];

        World world = route.NewWorld(out WadArchive wad);
        var mrandom = new DoomRandom(); // G_InitNew's M_ClearRandom
        var host = new RouteHost(world, wad, mrandom);
        var flow = new GameFlow(host, GameMode.shareware, mrandom);
        for (int tic = 0; tic < route.Cmds.Count; tic++)
        {
            Assert.Equal(gamestate_t.GS_LEVEL, flow.gamestate);
            route.File.RunEvents(world, tic);
            flow.G_Ticker(route.Cmds[tic]);
        }
        Assert.Equal(gamestate_t.GS_INTERMISSION, flow.gamestate);
        WiStuff wi = flow.Wi;
        Assert.Equal(expected[1], WminfoLine(wi.wbs!));

        var g = new ScreenGraphics(wad, null);
        var screen = new HudScreen(0, HudScreen.SCREENHEIGHT);
        for (int t = 1; t < expected.Length - 1; t++)
        {
            Assert.Equal(gamestate_t.GS_INTERMISSION, flow.gamestate);
            flow.G_Ticker(new ticcmd_t { buttons = presses.Contains(t) ? buttoncode_t.BT_USE : (byte)0 });
            wi.WI_Drawer(g, screen);
            string actual = Line(wi, screen, mrandom);
            if (actual != expected[t + 1])
                Assert.Fail($"{name}.{suffix}: intermission tic {t} differs from vanilla\n  vanilla: {expected[t + 1]}\n  game:    {actual}");
        }
        // the last tic ended it: the next level
        Assert.Equal((gamestate_t.GS_LEVEL, World.MapName(GameMode.shareware, 1, wi.wbs!.next + 1)), (flow.gamestate, world.level.Name));
    }

    /// <summary>dump.c's <c>wminfo</c> line: the stats and par the intermission starts with.</summary>
    private static string WminfoLine(wbstartstruct_t w)
    {
        wbplayerstruct_t p = w.plyr[w.pnum];
        return $"wminfo {w.epsd} {(w.didsecret ? 1 : 0)} {w.last} {w.next} {w.maxkills} {w.maxitems} {w.maxsecret} {w.partime} {p.skills} {p.sitems} {p.ssecret} {p.stime}";
    }

    /// <summary>A line of dump.c's intermission dump: BCNT STATE HASH RNDINDEX SOUNDS.</summary>
    private static string Line(WiStuff wi, HudScreen screen, DoomRandom mrandom)
    {
        var s = new StringBuilder();
        s.Append(CultureInfo.InvariantCulture, $"{wi.bcnt} {(int)wi.state}:{wi.sp_state}:{wi.cnt_kills}:{wi.cnt_items}:{wi.cnt_secret}:{wi.cnt_time}:{wi.cnt_par}:{wi.cnt_pause}:{wi.cnt}:{wi.acceleratestage}:{(wi.snl_pointeron ? 1 : 0)}:");
        s.Append(wi.anims.Length == 0 ? "-" : string.Join('.', wi.anims.Select(a => a.ctr)));
        s.Append(CultureInfo.InvariantCulture, $" {screen.Hash():x8} {mrandom.rndindex} ");
        s.Append(wi.sounds.Count == 0 ? "-" : string.Join(',', wi.sounds.Select(x => x.ToString()["sfx_".Length..] + "@-")));
        return s.ToString();
    }

    // ---- without a WAD ----

    private static readonly WadArchive _synthetic = new([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);

    [Theory]
    [InlineData(GameMode.shareware, 1, 1, 30)]
    [InlineData(GameMode.shareware, 1, 9, 165)]
    [InlineData(GameMode.registered, 2, 6, 360)]
    [InlineData(GameMode.registered, 3, 7, 165)]
    [InlineData(GameMode.retail, 4, 2, 120)] // episode 4 has no pars: vanilla reads cpars[gamemap]
    [InlineData(GameMode.commercial, 1, 1, 30)]
    [InlineData(GameMode.commercial, 1, 32, 30)]
    [InlineData(GameMode.commercial, 1, 17, 420)]
    public void TheParComesFromVanillasTables(GameMode mode, int episode, int map, int seconds)
    {
        var world = new World(new SpawnSettings(mode, skill_t.sk_medium), Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(_synthetic, "E1M1"));
        world.gameepisode = episode;
        world.gamemap = map;
        world.G_DoCompleted();
        Assert.Equal(seconds * SimInfo.TICRATE, world.wminfo.partime);
    }

    /// <summary>
    /// The counting's timing (a second's pause before each stat, 2% a tic, the
    /// time and par 3 s a tic), its sounds (a pistol shot every 4 tics, an
    /// explosion as each stat ends) and the press that ends it.
    /// </summary>
    [Fact]
    public void TheStatsCountUpWithVanillasTimingAndSounds()
    {
        (GameFlow flow, GameFlowTests.Host host) = GameFlowTests.New();
        flow.G_InitNewMap(skill_t.sk_medium, "E1M1");
        World world = host.World!;
        player_t p = world.players[0];
        world.totalkills = 10;
        world.totalitems = 4;
        world.totalsecret = 0;
        p.killcount = 5; // 50%
        p.itemcount = 1; // 25%
        world.leveltime = 100 * SimInfo.TICRATE; // 100 s
        world.G_ExitLevel();
        flow.G_Ticker(default);
        WiStuff wi = flow.Wi;
        Assert.Equal((30 * SimInfo.TICRATE, 100 * SimInfo.TICRATE, 1), (wi.wbs!.partime, wi.plrs[0].stime, wi.wbs.maxsecret));
        var log = new List<string>();
        for (int t = 2; wi.sp_state != 10; t++)
        {
            Assert.True(t < 1000);
            flow.G_Ticker(default);
            if (wi.sounds.Count > 0)
                log.Add($"{wi.bcnt}:{string.Join('+', wi.sounds.Select(s => s.ToString()[4..]))}");
        }
        // kills from bcnt 36: 1, 3 … 49 → 50 at 61; items from 97 (36 tics' pause): 1 … 25 at 109;
        // secret from 145: 0 at once; time and par from 181: 3 s a tic, the par (30 s) done at 191, the time (100 s) at 214
        Assert.Equal("36:pistol 40:pistol 44:pistol 48:pistol 52:pistol 56:pistol 60:pistol 61:barexp "
            + "100:pistol 104:pistol 108:pistol 109:barexp 145:barexp "
            + "184:pistol 188:pistol 192:pistol 196:pistol 200:pistol 204:pistol 208:pistol 212:pistol 214:barexp",
            string.Join(' ', log));
        Assert.Equal((50, 25, 0, 100, 30), (wi.cnt_kills, wi.cnt_items, wi.cnt_secret, wi.cnt_time, wi.cnt_par));
        Assert.Equal(WiStuff.stateenum_t.StatCount, wi.state);
        flow.G_Ticker(new ticcmd_t { buttons = buttoncode_t.BT_ATTACK });
        Assert.Equal((WiStuff.stateenum_t.ShowNextLoc, sfxenum_t.sfx_sgcock), (wi.state, wi.sounds.Single()));
    }

    [Fact]
    public void APressShowsTheStatsAtOnce()
    {
        (GameFlow flow, GameFlowTests.Host host) = GameFlowTests.New();
        flow.G_InitNewMap(skill_t.sk_medium, "E1M1");
        World world = host.World!;
        world.totalkills = 3;
        world.players[0].killcount = 2;
        world.leveltime = 4000;
        world.G_ExitLevel();
        flow.G_Ticker(default);
        flow.G_Ticker(new ticcmd_t { buttons = buttoncode_t.BT_USE });
        WiStuff wi = flow.Wi;
        Assert.Equal((10, 66, 0, 0, 4000 / SimInfo.TICRATE, 30), (wi.sp_state, wi.cnt_kills, wi.cnt_items, wi.cnt_secret, wi.cnt_time, wi.cnt_par));
        Assert.Equal(sfxenum_t.sfx_barexp, wi.sounds.Single());
        Assert.Equal(WiStuff.stateenum_t.StatCount, wi.state);
    }

    /// <summary>The synthetic IWAD has no <c>WI*</c> graphics: the stand-ins in the message font draw, and nothing throws.</summary>
    [Fact]
    public void DrawsWithoutTheGraphics()
    {
        (GameFlow flow, GameFlowTests.Host host) = GameFlowTests.New();
        flow.G_InitNewMap(skill_t.sk_medium, "E1M1");
        host.World!.G_ExitLevel();
        var g = new ScreenGraphics(_synthetic, new HuStuff(_synthetic));
        var screen = new HudScreen(0, HudScreen.SCREENHEIGHT);
        var states = new HashSet<WiStuff.stateenum_t>();
        for (int t = 0; flow.gamestate == gamestate_t.GS_INTERMISSION || t == 0; t++)
        {
            Assert.True(t < 2000);
            flow.G_Ticker(new ticcmd_t { buttons = t % 300 == 299 ? buttoncode_t.BT_USE : (byte)0 });
            flow.Wi.WI_Drawer(g, screen);
            if (states.Add(flow.Wi.state) && flow.Wi.state == WiStuff.stateenum_t.StatCount)
                Assert.Contains(screen.Opaque, o => o != 0); // the stand-ins (the synthetic font has A, B and C only: SECRET's C)
        }
        Assert.Equal(3, states.Count);
    }
}
