using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IsoDoom.Game;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Game;

/// <summary>
/// T7.1a: f_wipe.c's melt (<see cref="FWipe"/>) and d_main.c's
/// <c>D_Display</c> wipes (<see cref="GameFlow.D_Display"/>). Against
/// vanilla: DOOM1.WAD routes played through the game flow with the wipes on
/// (a level's start, its exit into the intermission and the next level, the
/// finale and its end picture, a reborn), compared with the dumps of
/// <c>tools/VanillaRef/wipes.sh</c> (vanilla drawn, a <c>D_Display</c> after
/// every tic): every tic's game state and <c>M_Random</c> index, every
/// wipe's tic, game state, <c>M_Random</c> index and columns, every melt
/// step, and the frames of a wipe between two full screens (the finale's
/// text to its end picture), every pixel; skips without DOOM1.WAD or the
/// dumps. Without a WAD: the melt's columns, timing and frames, and which
/// game state changes wipe.
/// </summary>
public class WipeTests
{
    // ---- against vanilla ----

    /// <summary>The routes <c>wipes.sh</c> dumps by default.</summary>
    public static readonly string[] Routes = { "e1m1-exit", "e1m3-secret-exit", "e1m8-exit", "e1m8-death" };

    public static TheoryData<string> Dumps()
    {
        var data = new TheoryData<string>();
        foreach (string r in Routes)
            data.Add(r);
        return data;
    }

    /// <summary>A route's game, as <see cref="IntermissionTests.RouteHost"/>, with reborns (the map afresh, the route's start again).</summary>
    private sealed class RouteHost : IGameHost
    {
        private readonly WadArchive wad;
        private readonly VanillaRoute route;
        private readonly StStuff st;

        public RouteHost(VanillaRoute route, World world, WadArchive wad, DoomRandom mrandom)
        {
            this.route = route;
            World = world;
            this.wad = wad;
            st = new StStuff(new StStuff.Graphics(), mrandom);
            st.ST_Start(world.players[world.consoleplayer]);
        }

        public World? World { get; private set; }
        public bool HasLump(string name) => wad.W_CheckNumForName(name) >= 0;
        public bool G_InitNew(skill_t skill, string map) => throw new InvalidOperationException();

        public bool G_DoLoadLevel()
        {
            World!.gameaction = gameaction_t.ga_loadlevel;
            return route.Reborn(World, wad);
        }

        public bool G_DoWorldDone(string map)
        {
            World!.G_DoWorldDone(Level.Load(wad, map));
            return true;
        }

        public void G_LevelTicker(in ticcmd_t cmd, bool paused)
        {
            World!.G_Ticker(cmd);
            st.ST_Ticker(); // the status bar's M_Random, once a tic
        }

        public void LevelCompleted()
        {
        }

        public void EndGame(string? why) => World = null;
    }

    [Theory]
    [MemberData(nameof(Dumps))]
    public void MatchesVanilla(string name)
    {
        VanillaRoute route = VanillaRoute.Load(name);
        TestWads.RequireDoom1();
        string? dir = Environment.GetEnvironmentVariable(VanillaRoute.DumpDirEnvVar);
        if (string.IsNullOrEmpty(dir))
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "isodoom", "vanilla-routes");
        string path = Path.Combine(dir, $"{name}.wipe");
        if (!File.Exists(path))
            Assert.Skip($"No vanilla dump {path}: run tools/VanillaRef/wipes.sh (or set {VanillaRoute.DumpDirEnvVar}).");
        string[] expected = File.ReadAllLines(path).Where(l => l.Length > 0).ToArray();
        string[] tail = expected[0].Split(' ');
        Assert.Equal("tail", tail[0]);
        HashSet<int> presses = tail[1] == "-" ? new() : tail[1].Split(',').Select(int.Parse).ToHashSet();
        var cmds = new List<ticcmd_t>(route.Cmds);
        for (int t = 1; t <= int.Parse(tail[2]); t++)
            cmds.Add(new ticcmd_t { buttons = presses.Contains(t) ? buttoncode_t.BT_USE : (byte)0 });

        World world = route.NewWorld(out WadArchive wad);
        var mrandom = new DoomRandom(); // G_InitNew's M_ClearRandom
        var host = new RouteHost(route, world, wad, mrandom);
        var flow = new GameFlow(host, GameMode.shareware, mrandom, GameVariant.vanilla, GameMission.doom) { Wipes = true };
        var g = new ScreenGraphics(wad, new HuStuff(wad));
        var shown = new HudScreen(0, HudScreen.SCREENHEIGHT);
        var now = new HudScreen(0, HudScreen.SCREENHEIGHT);
        var frame = new byte[HudScreen.SCREENWIDTH * HudScreen.SCREENHEIGHT];
        var actual = new List<string>();
        bool shownFull = false, nowFull = false;
        int fullWipes = 0, fullSteps = 0;
        flow.Ticked = () =>
        {
            actual.Add($"tic {flow.gametic} {(int)flow.gamestate} {mrandom.rndindex}");
            // what D_Display draws next (the full screens: the level is not vanilla's)
            nowFull = flow.gamestate is gamestate_t.GS_INTERMISSION or gamestate_t.GS_FINALE;
            if (flow.gamestate == gamestate_t.GS_INTERMISSION)
                flow.Wi.WI_Drawer(g, now);
            else if (flow.gamestate == gamestate_t.GS_FINALE)
                flow.Finale.F_Drawer(g, now);
        };
        int line = 1;
        for (int tic = 0; tic < cmds.Count && flow.World is not null; tic++)
        {
            if (tic < route.Cmds.Count)
                route.File.RunEvents(flow.World, tic);
            int wipes = flow.Wipe.count;
            flow.Wipes = tic > 0; // vanilla's D_DoomLoop runs its first tic before the first D_Display
            flow.G_Ticker(cmds[tic]);
            if (tic == 0)
                flow.wipegamestate = gamestate_t.GS_DEMOSCREEN;
            if (flow.Wipe.count != wipes)
            {
                actual.Add($"wipe {flow.gametic} {(int)flow.gamestate} {mrandom.rndindex} {string.Join(' ', flow.Wipe.y.Take(FWipe.COLUMNS))}");
                bool full = shownFull && nowFull;
                for (int step = 0; flow.Wipe.go; step++)
                {
                    string[] melt = line + actual.Count < expected.Length ? expected[line + actual.Count].Split(' ') : new[] { "melt", "1" };
                    bool done = flow.Wipe.wipe_ScreenWipe(melt[0] == "melt" ? int.Parse(melt[1]) : 1);
                    string hash = "-";
                    if (full)
                    {
                        flow.Wipe.Draw(shown.Pixels, now.Pixels, frame);
                        hash = Fnv(frame).ToString("x8");
                        fullSteps++;
                    }
                    actual.Add(full ? $"melt {melt[1]} {(done ? 1 : 0)} {hash}" : $"melt {melt[1]} {(done ? 1 : 0)}");
                }
                fullWipes += full ? 1 : 0;
            }
            if (nowFull)
                Array.Copy(now.Pixels, shown.Pixels, now.Pixels.Length);
            shownFull = nowFull;
            Compare(name, expected, line, actual);
            line += actual.Count;
            actual.Clear();
        }
        Assert.True(line == expected.Length, $"{name}: {line - 1} lines compared, the dump has {expected.Length - 1}");
        if (route.Map == "E1M8" && route.Exit != 0)
            Assert.True(fullWipes == 1 && fullSteps > 30, $"{name}: the finale's text to its end picture compared ({fullWipes} wipes, {fullSteps} frames)");
    }

    /// <summary>The game's lines against the dump's from <paramref name="line"/> (a melt line's hash only where the game has one).</summary>
    private static void Compare(string name, string[] expected, int line, List<string> actual)
    {
        for (int i = 0; i < actual.Count; i++)
        {
            string e = line + i < expected.Length ? expected[line + i] : "(the dump's end)";
            string a = actual[i];
            if (a.StartsWith("melt ") && a.Split(' ').Length == 3)
                e = string.Join(' ', e.Split(' ').Take(3));
            if (a != e)
                Assert.Fail($"{name}: dump line {line + i + 1} differs from vanilla\n  vanilla: {Short(e)}\n  game:    {Short(a)}");
        }
    }

    private static string Short(string s) => s.Length > 200 ? s[..200] + " …" : s;

    private static uint Fnv(byte[] bytes)
    {
        uint hash = 2166136261u;
        foreach (byte b in bytes)
            hash = unchecked((hash ^ b) * 16777619u);
        return hash;
    }

    // ---- without a WAD ----

    [Fact]
    public void TheColumnsStartAsVanillasFromMRandom()
    {
        var wipe = new FWipe();
        var mrandom = new DoomRandom { rndindex = 2 }; // a new game's first two tics (the status bar's draws)
        wipe.wipe_initMelt(mrandom);
        Assert.Equal((2 + 320) & 0xff, mrandom.rndindex); // 320 draws
        Assert.Equal(new[] { -12, -13, -13, -12, -11, -12 }, wipe.y.Take(6)); // vanilla's first wipe after a new game's first tics
        for (int i = 0; i < wipe.y.Length; i++)
        {
            Assert.InRange(wipe.y[i], -15, 0);
            if (i > 0)
                Assert.InRange(wipe.y[i] - wipe.y[i - 1], -1, 1);
        }
        Assert.True(wipe.go);
    }

    /// <summary>A column waits, then slides 1, 2, 4, 8, 16 rows and 8 a tic after: 200 rows in 27 tics; a step more says it is over.</summary>
    [Fact]
    public void AColumnMeltsAtVanillasSpeed()
    {
        var wipe = new FWipe();
        wipe.wipe_initMelt(new DoomRandom());
        Array.Fill(wipe.y, 0);
        wipe.y[3] = -15;
        var rows = new List<int>();
        int steps = 0;
        while (!wipe.wipe_ScreenWipe(1))
        {
            steps++;
            if (rows.Count < 6)
                rows.Add(wipe.y[0]);
            if (steps == 27)
                Assert.Equal((200, 87), (wipe.y[0], wipe.y[3])); // the waiting column 12 tics behind
        }
        Assert.Equal(new[] { 1, 3, 7, 15, 31, 39 }, rows);
        Assert.Equal(42, steps); // the last column: 15 tics' wait, 27 to melt
        Assert.Equal(43, wipe.steps);
        Assert.False(wipe.go);
    }

    [Fact]
    public void TheFrameSlidesTheOldScreenOverTheNew()
    {
        const int w = HudScreen.SCREENWIDTH, h = HudScreen.SCREENHEIGHT;
        byte[] start = new byte[w * h], end = new byte[w * h], frame = new byte[w * h];
        for (int i = 0; i < start.Length; i++)
        {
            start[i] = (byte)(i / w); // its row
            end[i] = 255;
        }
        var wipe = new FWipe();
        wipe.wipe_initMelt(new DoomRandom());
        wipe.Draw(start, end, frame);
        Assert.Equal(start, frame); // every column still waits
        while (wipe.y[0] < 0 || wipe.y[0] < 7)
            wipe.wipe_ScreenWipe(1);
        wipe.Draw(start, end, frame);
        int off = wipe.y[0];
        for (int row = 0; row < h; row++)
        {
            byte want = row < off ? (byte)255 : (byte)(row - off);
            Assert.Equal((want, want), (frame[row * w], frame[row * w + 1]));
        }
        while (!wipe.wipe_ScreenWipe(1))
        {
        }
        wipe.Draw(start, end, frame);
        Assert.Equal(end, frame);
    }

    /// <summary>
    /// Which game state changes wipe (with the wipes on): a new game, the
    /// intermission, the next level, a reborn (forced), the finale and its end
    /// picture (forced), the title loop and its pages; no tic runs while the
    /// screen melts; without the wipes no <c>M_Random</c> is drawn for them.
    /// </summary>
    [Fact]
    public void TheGameStateChangesWipe()
    {
        (GameFlow flow, GameFlowTests.Host host) = GameFlowTests.New();
        flow.Wipes = true;
        host.Extra.Add("CREDIT");
        var wipes = new List<string>();
        void Tic(ticcmd_t cmd = default)
        {
            Assert.True(flow.CanTic);
            int before = flow.Wipe.count;
            flow.G_Ticker(cmd);
            if (flow.Wipe.count == before)
                return;
            wipes.Add(GameFlow.StateName(flow.gamestate));
            Assert.False(flow.CanTic); // d_main.c's wipe loop: no tics
            while (!flow.WipeStep())
            {
            }
            Assert.True(flow.CanTic);
        }

        flow.D_StartTitle(null);
        Tic(); // the title's first page, shown first: no wipe (vanilla's wipegamestate starts there)
        for (int t = 0; t < 171; t++)
            Tic(); // TITLEPIC, then (DEMO1 passed) CREDIT
        Assert.Equal("CREDIT", flow.pagename);
        Assert.Equal(new[] { "demoscreen" }, wipes);

        flow.G_InitNewMap(skill_t.sk_medium, "E1M1");
        Tic(); // the level
        host.World!.gameaction = gameaction_t.ga_loadlevel; // a reborn: forced
        Tic();
        Tic();
        host.World!.G_ExitLevel();
        Tic(); // the exit tic, then the intermission's first: wiped after it
        Tic();
        for (int t = 0; flow.gamestate == gamestate_t.GS_INTERMISSION; t++)
            Tic(new ticcmd_t { buttons = t % 2 == 0 ? buttoncode_t.BT_USE : (byte)0 });
        Tic(); // the next level (E1M2): wiped after its first tic
        flow.G_InitNewMap(skill_t.sk_medium, "E1M1"); // a new game over the level: forced
        Tic();
        host.World!.gamemap = 8;
        host.World!.G_ExitLevel();
        Tic();
        Tic(); // the finale
        FFinale fi = flow.Finale;
        fi.finalecount = fi.finaletext.Length * FFinale.TEXTSPEED + FFinale.TEXTWAIT;
        Tic(); // its end picture: forced
        flow.D_StartTitle(null);
        Tic(); // the title loop
        Assert.Equal(new[] { "demoscreen", "level", "level", "intermission", "level", "level", "finale", "finale", "demoscreen" }, wipes);

        // without the wipes: no M_Random drawn for them, none under way
        (GameFlow off, GameFlowTests.Host offHost) = GameFlowTests.New();
        off.G_InitNewMap(skill_t.sk_medium, "E1M1");
        off.G_Ticker(default);
        offHost.World!.G_ExitLevel();
        off.G_Ticker(default);
        off.G_Ticker(default);
        Assert.Equal((gamestate_t.GS_INTERMISSION, 0, false), (off.gamestate, off.Wipe.count, off.Wipe.go));
    }
}
