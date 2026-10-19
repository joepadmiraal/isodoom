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
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Game;

/// <summary>
/// T7.4: wi_stuff.c's intermission (<see cref="WiStuff"/>). Against vanilla:
/// each DOOM1.WAD exit route (but E1M8's, whose exit goes to the finale) and
/// (T10.1) each Doom II one (<c>INTERPIC</c>, <c>CWILV*</c>, Doom II's pars,
/// no next location; then the next level or Doom II's text screen)
/// played through the game flow to its intermission, which then runs tic by
/// tic as vanilla's did with the same presses, compared with the dump of
/// <c>tools/VanillaRef/intermissions.sh</c> (the stats and par it starts
/// with; every tic's state, counters, animations, <c>M_Random</c> index,
/// sounds and the hash of every pixel drawn); skips without DOOM1.WAD or the
/// dumps (T11.7: a multi-player route plays every player's commands, and
/// its net game stats are compared too; T11.7a: the synthetic IWAD's
/// exit routes too, their dumps committed beside the route, so CI compares
/// them: every pixel's hash only with DOOM1.WAD, whose graphics vanilla drew
/// them with, the rest without). Without a WAD: the par tables, the counting's timing, its sounds
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

        public void G_LevelTicker(ticcmd_t[] netcmds, bool paused)
        {
            World!.G_Ticker(netcmds);
            St.ST_Ticker(); // the status bar's M_Random, once a tic
        }

        public void LevelCompleted()
        {
        }

        public void EndGame(string? why) => World = null;
    }

    /// <summary>
    /// The DOOM1.WAD, (T10.1) Doom II and (T9.4) The Ultimate Doom exit routes with an intermission (all but the ExM8 ones,
    /// whose exits go to the finale, T9.2; T10.4: MAP30's too, the Icon's death), and (T11.7a) the synthetic IWAD's.
    /// </summary>
    private static List<VanillaRoute> ExitRoutes() =>
        [.. VanillaRoute.Names().Select(VanillaRoute.Load).Where(r => (r.IdWad || r.Iwad == "synthetic") && r.Exit != 0 && !(r.GameMode != GameMode.commercial && r.Map.EndsWith("M8", StringComparison.Ordinal)))];

    /// <summary>The exit routes with an intermission and the dumps' suffixes (<c>wi</c>: counted out; <c>wiskip</c>: skipped).</summary>
    public static TheoryData<string, string> Dumps()
    {
        var data = new TheoryData<string, string>();
        // P10: and the PWAD sets' (VanillaRoute.PwadDir: each set's first map to its second, its own INTERPIC and level names)
        foreach (VanillaRoute r in ExitRoutes().Concat(VanillaRoute.PwadNames().Select(VanillaRoute.Load).Where(r => r.Exit != 0)))
        {
            data.Add(r.Name, "wi");
            data.Add(r.Name, "wiskip");
        }
        return data;
    }

    [Fact]
    public void EveryEpisode1ExitHasAnIntermissionToCompare()
    {
        List<VanillaRoute> routes = [.. ExitRoutes().Where(r => r.Iwad == "doom1")];
        Assert.Equal(["E1M1", "E1M2", "E1M3", "E1M4", "E1M5", "E1M6", "E1M7", "E1M9"], routes.Select(r => r.Map).Distinct().Order());
        Assert.Contains(routes, r => r.Map == "E1M3" && r.Exit == 2);
    }

    /// <summary>
    /// T9.4: The Ultimate Doom's flow has its intermissions to compare: the
    /// secret exits of E2M5, E3M6 and E4M2 (to the secret maps: <c>WIMAP1</c>,
    /// <c>WIMAP2</c> with their animations, E4's <c>INTERPIC</c>, no par for E4) and the secret maps'
    /// exits back (E2M9 to E2M6, E3M9 to E3M7, E4M9 to E4M3); M9: E3M2's and E4M1's exits.
    /// </summary>
    [Fact]
    public void TheUltimateDoomsSecretMapsHaveIntermissionsToCompare()
    {
        List<VanillaRoute> routes = [.. ExitRoutes().Where(r => r.Iwad == "doom")];
        Assert.Equal(["E2M5:2", "E2M9:1", "E3M2:1", "E3M6:2", "E3M9:1", "E4M1:1", "E4M2:2", "E4M9:1"],
            routes.Select(r => $"{r.Map}:{r.Exit}").Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// T10.1: Doom II's flow has its intermissions to compare: MAP01's exit
    /// (to MAP02), the maps before a text screen (MAP06, MAP11, MAP20, and,
    /// T10.4, MAP30's after the Icon's death), the secret maps' way in (MAP15's and
    /// MAP31's secret exits) and out (MAP31's and MAP32's exits, to MAP16);
    /// T10.6: also MAP12's W1 exit, MAP15's W1 secret exit and MAP31 walked from its start.
    /// </summary>
    [Fact]
    public void DoomIIsFlowHasIntermissionsToCompare()
    {
        List<VanillaRoute> routes = [.. ExitRoutes().Where(r => r.Iwad == "doom2")];
        Assert.Equal(["MAP01:1", "MAP06:1", "MAP07:1", "MAP11:1", "MAP12:1", "MAP15:2", "MAP15:2", "MAP20:1", "MAP30:1", "MAP31:1", "MAP31:1", "MAP31:2", "MAP32:1"],
            routes.Select(r => $"{r.Map}:{r.Exit}").Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// T11.7a: the deathmatch intermissions to compare: the synthetic IWAD's two- and four-player
    /// exits (CI; frags both ways, a suicide, a negative total, the four-player one in altdeath) and DOOM1's timer exit.
    /// </summary>
    [Fact]
    public void DeathmatchExitsHaveIntermissionsToCompare()
    {
        List<VanillaRoute> routes = [.. ExitRoutes().Where(r => r.File.Deathmatch != 0)];
        Assert.Equal(["dm-e1m1-timer", "synthetic-dm-exit-2p", "synthetic-dm-exit-4p"], routes.Select(r => r.Name).Order(StringComparer.Ordinal));
        Assert.Equal([2, 4], routes.Where(r => r.Iwad == "synthetic").Select(r => System.Numerics.BitOperations.PopCount((uint)r.PlayersInGame)).Order());
        Assert.Contains(routes, r => r.File.Deathmatch == 2);
    }

    [Theory]
    [MemberData(nameof(Dumps))]
    public void MatchesVanilla(string name, string suffix)
    {
        var route = VanillaRoute.Load(name);
        string path;
        if (route.IdWad)
        {
            route.RequireWad();
            path = Path.Combine(VanillaRoute.DumpDir(), $"{name}.{suffix}");
        }
        else
        {
            path = Path.ChangeExtension(route.Path, "." + suffix); // T11.7a: committed beside the route
        }
        if (!File.Exists(path))
            Assert.Skip($"No vanilla dump {path}: run tools/VanillaRef/intermissions.sh (or set {VanillaRoute.DumpDirEnvVar}).");
        string[] expected = [.. File.ReadAllLines(path).Where(l => l.Length > 0)];
        Assert.StartsWith("presses ", expected[0]);
        Assert.StartsWith("wminfo ", expected[1]);
        HashSet<int> presses = [.. expected[0]["presses ".Length..].Split(',').Select(int.Parse)];

        World world = route.NewWorld(out WadArchive wad);
        var mrandom = new DoomRandom(); // G_InitNew's M_ClearRandom
        var host = new RouteHost(world, wad, mrandom);
        var flow = new GameFlow(host, route.GameMode, mrandom, GameVariant.vanilla, route.GameMission, route.GameVersion);
        for (int tic = 0; tic < route.Cmds.Count; tic++)
        {
            Assert.Equal(gamestate_t.GS_LEVEL, flow.gamestate);
            route.File.RunEvents(world, tic);
            if (route.File.Netgame)
                flow.G_Ticker(route.NetCmds[tic]); // T11.7: every player's
            else
                flow.G_Ticker(route.Cmds[tic]);
        }
        Assert.Equal(gamestate_t.GS_INTERMISSION, flow.gamestate);
        WiStuff wi = flow.Wi;
        Assert.Equal(expected[1], WminfoLine(wi.wbs!, world.netgame));

        // T11.7a: a synthetic route's screen as vanilla drew it, over DOOM1.WAD; without it, every pixel's hash is left out
        WadArchive? graphics = route.IdWad ? wad : route.HudGraphics();
        var g = new ScreenGraphics(graphics ?? wad, null);
        var screen = new HudScreen(0, HudScreen.SCREENHEIGHT);
        for (int t = 1; t < expected.Length - 1; t++)
        {
            Assert.Equal(gamestate_t.GS_INTERMISSION, flow.gamestate);
            flow.G_Ticker(new ticcmd_t { buttons = presses.Contains(t) ? buttoncode_t.BT_USE : (byte)0 });
            wi.WI_Drawer(g, screen);
            string actual = Line(wi, world, screen, mrandom);
            if (graphics is null)
                actual = WithHash(actual, expected[t + 1].Split(' ')[2]);
            if (actual != expected[t + 1])
                Assert.Fail($"{name}.{suffix}: intermission tic {t} differs from vanilla\n  vanilla: {expected[t + 1]}\n  game:    {actual}");
        }
        // the last tic ended it: the next level, or (T10.1) Doom II's text screen before it
        if (TextScreenAfter(route))
        {
            Assert.Equal(gamestate_t.GS_FINALE, flow.gamestate);
            return;
        }
        string nextMap = World.MapName(route.GameMode, route.GameMode == GameMode.commercial ? 1 : route.Map[1] - '0', wi.wbs!.next + 1);
        if (!route.IdWad && !host.HasLump(nextMap))
        {
            // T11.7a: the synthetic IWAD has no E1M9 (vanilla went on to DOOM1.WAD's): the game ends at the title loop
            Assert.Equal((gamestate_t.GS_DEMOSCREEN, "E1M9"), (flow.gamestate, nextMap));
            return;
        }
        Assert.Equal((gamestate_t.GS_LEVEL, nextMap), (flow.gamestate, world.level.Name));
        // T9.4: the secret maps' way in and back, as vanilla's G_DoCompleted
        string? next = route.Map switch
        {
            "E1M3" or "E2M5" or "E3M6" or "E4M2" when route.Exit == 2 => $"{route.Map[..2]}M9",
            "E1M9" => "E1M4",
            "E2M9" => "E2M6",
            "E3M9" => "E3M7",
            "E4M9" => "E4M3",
            _ => null,
        };
        if (next is not null)
            Assert.Equal(next, world.level.Name);
    }

    /// <summary>T11.7a: a dump line with its pixels' hash (the third field) replaced by <paramref name="hash"/>.</summary>
    private static string WithHash(string line, string hash)
    {
        string[] f = line.Split(' ');
        f[2] = hash;
        return string.Join(' ', f);
    }

    /// <summary>T10.1: whether Doom II's <c>G_WorldDone</c> shows a text screen after the route's exit (maps 6, 11, 20, 30, and the secret exits of 15 and 31).</summary>
    internal static bool TextScreenAfter(VanillaRoute route) =>
        route.GameMode == GameMode.commercial && route.Exit != 0 && (route.Exit == 2 ? route.Map is "MAP15" or "MAP31" : route.Map is "MAP06" or "MAP11" or "MAP20" or "MAP30");

    /// <summary>
    /// dump.c's <c>wminfo</c> line: the stats and par the intermission starts
    /// with (T11.7: in a net game every player in the game's stats and frags after them).
    /// </summary>
    private static string WminfoLine(wbstartstruct_t w, bool netgame)
    {
        wbplayerstruct_t p = w.plyr[w.pnum];
        var s = new StringBuilder($"wminfo {w.epsd} {(w.didsecret ? 1 : 0)} {w.last} {w.next} {w.maxkills} {w.maxitems} {w.maxsecret} {w.partime} {p.skills} {p.sitems} {p.ssecret} {p.stime}");
        for (int i = 0; netgame && i < World.MAXPLAYERS; i++)
        {
            wbplayerstruct_t q = w.plyr[i];
            if (q.@in)
                s.Append(CultureInfo.InvariantCulture, $" p{i + 1}={q.skills}.{q.sitems}.{q.ssecret}.{q.stime}:{string.Join('.', q.frags)}");
        }
        return s.ToString();
    }

    /// <summary>
    /// A line of dump.c's intermission dump: BCNT STATE HASH RNDINDEX SOUNDS
    /// (T11.7: STATE with a deathmatch's frag table or a net game's counters
    /// after it, ref.patch's <c>WI_dumpState</c>).
    /// </summary>
    internal static string Line(WiStuff wi, World world, HudScreen screen, DoomRandom mrandom)
    {
        var s = new StringBuilder();
        s.Append(CultureInfo.InvariantCulture, $"{wi.bcnt} {(int)wi.state}:{wi.sp_state}:{wi.cnt_kills[0]}:{wi.cnt_items[0]}:{wi.cnt_secret[0]}:{wi.cnt_time}:{wi.cnt_par}:{wi.cnt_pause}:{wi.cnt}:{wi.acceleratestage}:{(wi.snl_pointeron ? 1 : 0)}:");
        s.Append(wi.anims.Length == 0 ? "-" : string.Join('.', wi.anims.Select(a => a.ctr)));
        int[] ingame = [.. Enumerable.Range(0, World.MAXPLAYERS).Where(i => world.playeringame[i])];
        if (world.deathmatch != 0)
        {
            s.Append(CultureInfo.InvariantCulture, $":dm:{wi.dm_state}:");
            s.AppendJoin('/', ingame.Select(i => string.Join('.', ingame.Select(j => wi.dm_frags[i, j])) + "=" + wi.dm_totals[i].ToString(CultureInfo.InvariantCulture)));
        }
        else if (world.netgame)
        {
            s.Append(CultureInfo.InvariantCulture, $":ng:{wi.ng_state}:{wi.dofrags}:");
            s.AppendJoin('/', ingame.Select(i => $"{wi.cnt_kills[i]}.{wi.cnt_items[i]}.{wi.cnt_secret[i]}.{wi.cnt_frags[i]}"));
        }
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
        flow.G_Ticker(new ticcmd_t());
        WiStuff wi = flow.Wi;
        Assert.Equal((30 * SimInfo.TICRATE, 100 * SimInfo.TICRATE, 1), (wi.wbs!.partime, wi.plrs[0].stime, wi.wbs.maxsecret));
        var log = new List<string>();
        for (int t = 2; wi.sp_state != 10; t++)
        {
            Assert.True(t < 1000);
            flow.G_Ticker(new ticcmd_t());
            if (wi.sounds.Count > 0)
                log.Add($"{wi.bcnt}:{string.Join('+', wi.sounds.Select(s => s.ToString()[4..]))}");
        }
        // kills from bcnt 36: 1, 3 … 49 → 50 at 61; items from 97 (36 tics' pause): 1 … 25 at 109;
        // secret from 145: 0 at once; time and par from 181: 3 s a tic, the par (30 s) done at 191, the time (100 s) at 214
        Assert.Equal("36:pistol 40:pistol 44:pistol 48:pistol 52:pistol 56:pistol 60:pistol 61:barexp "
            + "100:pistol 104:pistol 108:pistol 109:barexp 145:barexp "
            + "184:pistol 188:pistol 192:pistol 196:pistol 200:pistol 204:pistol 208:pistol 212:pistol 214:barexp",
            string.Join(' ', log));
        Assert.Equal((50, 25, 0, 100, 30), (wi.cnt_kills[0], wi.cnt_items[0], wi.cnt_secret[0], wi.cnt_time, wi.cnt_par));
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
        flow.G_Ticker(new ticcmd_t());
        flow.G_Ticker(new ticcmd_t { buttons = buttoncode_t.BT_USE });
        WiStuff wi = flow.Wi;
        Assert.Equal((10, 66, 0, 0, 4000 / SimInfo.TICRATE, 30), (wi.sp_state, wi.cnt_kills[0], wi.cnt_items[0], wi.cnt_secret[0], wi.cnt_time, wi.cnt_par));
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
