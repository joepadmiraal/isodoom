using System;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T8.3: the sim's tics allocate (next to) nothing, so a live game does not
/// collect garbage in the middle of a tic (T6.13's note: 4–6 collections in
/// 700 tics, single tics of 2–4 ms). Every monster hunts the player, who runs
/// a circle and fires (as <see cref="TicBudgetTests"/>' <c>all</c>), on the
/// synthetic E1M1 (CI), DOOM1 E1M9 and Doom II MAP29 at skill 4.
/// </summary>
public class TicAllocationTests
{
    private readonly ITestOutputHelper _output;

    public TicAllocationTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The most a tic may allocate on average, in bytes: the mobjs it spawns
    /// (puffs, blood, missiles: 36, 69 and 106 bytes a tic here) but no garbage
    /// made at every call (the tables' spans did 253 and 471 in a Debug build).
    /// </summary>
    public const long BudgetBytesPerTic = 192;

    [Fact]
    public void SyntheticE1M1TicsAllocateAlmostNothing() =>
        Measure(new WadArchive([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]), GameMode.shareware, "E1M1");

    [Fact]
    public void Doom1E1M9TicsAllocateAlmostNothing() =>
        Measure(WadArchive.Open(TestWads.RequireDoom1()), GameMode.shareware, "E1M9");

    [Fact]
    public void Doom2Map29TicsAllocateAlmostNothing() =>
        Measure(WadArchive.Open(TestWads.RequireDoom2()), GameMode.commercial, "MAP29");

    private void Measure(WadArchive wad, GameMode mode, string map)
    {
        var world = new World(new SpawnSettings(mode, skill_t.sk_hard), Tweaks.TopDown);
        world.G_DoLoadLevel(Level.Load(wad, map));
        TicBudgetTests.WakeAll(world);

        const int Warmup = 105, Measured = 700;
        long bytes = 0;
        int collections = GC.CollectionCount(0);
        for (int tic = 0; tic < Warmup + Measured; tic++)
        {
            ticcmd_t cmd = TicBudgetTests.CircleCmd(tic);
            cmd.buttons |= (byte)buttoncode_t.BT_ATTACK;
            long before = GC.GetAllocatedBytesForCurrentThread();
            world.G_Ticker(cmd);
            if (tic >= Warmup)
                bytes += GC.GetAllocatedBytesForCurrentThread() - before;
            else
                collections = GC.CollectionCount(0);
        }
        collections = GC.CollectionCount(0) - collections;
        int alive = world.Mobjs().Count(m => (m.flags & mobjflag_t.MF_COUNTKILL) != 0 && m.health > 0);
        _output.WriteLine($"{map} skill 4, {alive} monsters alive at the end, {Measured} tics: {bytes} bytes allocated, {bytes / Measured} a tic; {collections} gen-0 collections");
        Assert.True(bytes / Measured <= BudgetBytesPerTic, $"{bytes / Measured} bytes a tic");
    }
}
