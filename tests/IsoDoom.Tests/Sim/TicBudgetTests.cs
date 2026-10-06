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
/// state tics until M6 (T6.1), so measure again then.
/// </summary>
public class TicBudgetTests
{
    private readonly ITestOutputHelper _output;

    public TicBudgetTests(ITestOutputHelper output) => _output = output;

    /// <summary>SPEC §9: the sim stays under 2 ms per tic.</summary>
    public const double BudgetMs = 2.0;

    [Fact]
    public void Doom1E1M9TicsStayInTheBudget()
    {
        WadArchive wad = WadArchive.Open(TestWads.RequireDoom1());
        var world = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_hard), Tweaks.TopDown);
        world.G_DoLoadLevel(Level.Load(wad, "E1M9"));
        int mobjs = world.Mobjs().Count();

        const int warmup = 105, measured = 1050;
        var ms = new double[measured];
        var clock = new Stopwatch();
        mobj_t player = world.players[world.consoleplayer].mo!;
        var sectors = new System.Collections.Generic.SortedSet<int>();
        int travelled = 0;
        for (int tic = 0; tic < warmup + measured; tic++)
        {
            // Run in a slow circle (twin-stick: an absolute direction and aim), so the player
            // collides, slides and changes sectors.
            uint direction = unchecked((uint)tic * (Tables.ANG90 / 35));
            var cmd = new ticcmd_t { angleturn = Ticcmds.AbsoluteAngle(direction) };
            Ticcmds.AbsoluteMove(ref cmd, direction, Ticcmds.TwinStickSpeed(true));
            clock.Restart();
            world.G_Ticker(cmd);
            clock.Stop();
            if (tic >= warmup)
                ms[tic - warmup] = clock.Elapsed.TotalMilliseconds;
            sectors.Add(player.subsector.sector.Index);
            travelled += Math.Abs(player.x - player.oldx) + Math.Abs(player.y - player.oldy);
        }

        double mean = ms.Average();
        Array.Sort(ms);
        double median = ms[measured / 2], p95 = ms[(int)(measured * 0.95)], worst = ms[^1];
        _output.WriteLine($"E1M9 skill 4, {mobjs} mobjs, player through {sectors.Count} sectors, {travelled >> 16} units, {measured} tics: mean {mean:F4} ms, median {median:F4}, p95 {p95:F4}, worst {worst:F4}");
        Assert.True(mobjs > 100, $"{mobjs} mobjs");
        Assert.True(sectors.Count > 1, "the player stayed in one sector");
        // The median and 95th percentile, not the worst tic: a GC pause or the test host can stall one.
        Assert.True(median < BudgetMs, $"median {median:F4} ms");
        Assert.True(p95 < BudgetMs, $"p95 {p95:F4} ms");
    }
}
