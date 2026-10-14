using System;
using System.Diagnostics;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T4.9: SPEC §9's budget of 2 ms per tic with a full map, on DOOM1 E1M9 at
/// skill 4 (every single-player thing spawned) with the player running and
/// turning (twin-stick tweaks, as the game). The things only count down their
/// state tics until M6 (T6.1), so measure again then. T6.4: also with every
/// monster awake (none deaf, a noise from the player at the start; the player
/// in god mode so they keep chasing and attacking). T6.13: also with every
/// monster hunting the player from the start (target and see state, as the
/// level script's <c>wake</c>). T10.6: also Doom II's busiest maps at skill 4
/// with every monster hunting (MAP27 and MAP29: the most mobjs; MAP30: the
/// Icon's spawn cubes and fires).
/// </summary>
public class TicBudgetTests
{
    private readonly ITestOutputHelper _output;

    public TicBudgetTests(ITestOutputHelper output) => _output = output;

    /// <summary>SPEC §9: the sim stays under 2 ms per tic.</summary>
    public const double BudgetMs = 2.0;

    [Theory]
    [InlineData("asleep")]
    [InlineData("noise")]
    [InlineData("all")]
    public void Doom1E1M9TicsStayInTheBudget(string awake) =>
        Measure(TestWads.RequireDoom1(), GameMode.shareware, "E1M9", awake);

    /// <summary>T10.6: Doom II's busiest maps, every monster hunting the player.</summary>
    [Theory]
    [InlineData("MAP27")]
    [InlineData("MAP29")]
    [InlineData("MAP30")]
    public void Doom2TicsStayInTheBudget(string map) =>
        Measure(TestWads.RequireDoom2(), GameMode.commercial, map, "all");

    private void Measure(string iwad, GameMode mode, string map, string awake)
    {
        var wad = WadArchive.Open(iwad);
        var world = new World(new SpawnSettings(mode, skill_t.sk_hard), Tweaks.TopDown);
        world.G_DoLoadLevel(Level.Load(wad, map));
        int mobjs = world.Mobjs().Count();
        if (awake != "asleep")
        {
            foreach (mobj_t m in world.Mobjs())
                m.flags &= ~mobjflag_t.MF_AMBUSH;
            world.players[world.consoleplayer].cheats |= player_t.CF_GODMODE;
            world.P_NoiseAlert(world.players[world.consoleplayer].mo!, world.players[world.consoleplayer].mo!);
        }
        if (awake == "all")
        {
            // T6.13: every monster hunts the player, as A_Look on seeing it (the level script's wake).
            foreach (mobj_t m in world.Mobjs().ToList())
            {
                // T10.6: MAP30's eye starts spitting cubes, as on seeing the player (A_BrainAwake).
                if (m.type == mobjtype_t.MT_BOSSSPIT && m.state == m.info.spawnstate)
                    world.P_SetMobjState(m, m.info.seestate);
                if ((m.flags & mobjflag_t.MF_COUNTKILL) == 0)
                    continue;
                m.target = world.players[world.consoleplayer].mo;
                if (m.state == m.info.spawnstate)
                    world.P_SetMobjState(m, m.info.seestate);
            }
        }

        const int Warmup = 105, Measured = 1050;
        double[] ms = new double[Measured];
        var clock = new Stopwatch();
        mobj_t player = world.players[world.consoleplayer].mo!;
        var sectors = new System.Collections.Generic.SortedSet<int>();
        int travelled = 0;
        for (int tic = 0; tic < Warmup + Measured; tic++)
        {
            // Run in a slow circle (twin-stick: an absolute direction and aim), so the player
            // collides, slides and changes sectors.
            uint direction = unchecked((uint)tic * (Tables.ANG90 / 35));
            var cmd = new ticcmd_t { angleturn = Ticcmds.AbsoluteAngle(direction) };
            Ticcmds.AbsoluteMove(ref cmd, direction, Ticcmds.TwinStickSpeed(true));
            clock.Restart();
            world.G_Ticker(cmd);
            clock.Stop();
            if (tic >= Warmup)
                ms[tic - Warmup] = clock.Elapsed.TotalMilliseconds;
            sectors.Add(player.subsector.sector.Index);
            travelled += Math.Abs(player.x - player.oldx) + Math.Abs(player.y - player.oldy);
        }

        double mean = ms.Average();
        Array.Sort(ms);
        double median = ms[Measured / 2], p95 = ms[(int)(Measured * 0.95)], worst = ms[^1];
        int chasing = world.Mobjs().Count(m => (m.flags & mobjflag_t.MF_COUNTKILL) != 0 && m.health > 0 && m.target != null);
        _output.WriteLine($"{map} skill 4, {mobjs} mobjs ({chasing} monsters with a target), player through {sectors.Count} sectors, {travelled >> 16} units, {Measured} tics: mean {mean:F4} ms, median {median:F4}, p95 {p95:F4}, worst {worst:F4}");
        Assert.True(mobjs > (map == "MAP30" ? 30 : 100), $"{mobjs} mobjs");
        Assert.True(sectors.Count > 1, "the player stayed in one sector");
        // The median and 95th percentile, not the worst tic: a GC pause or the test host can stall one.
        Assert.True(median < BudgetMs, $"median {median:F4} ms");
        Assert.True(p95 < BudgetMs, $"p95 {p95:F4} ms");
    }
}
