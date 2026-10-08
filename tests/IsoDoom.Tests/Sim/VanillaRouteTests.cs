using System.Linq;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T4.8: every route in <c>Routes/</c> plays exactly as in vanilla, tic by
/// tic (<see cref="VanillaRoute"/>). The synthetic IWAD's and the test maps'
/// (T4.8a) routes check against committed dumps (in CI); DOOM1.WAD's need the WAD and a local dump from
/// <c>tools/VanillaRef/routes.sh</c>, and skip without them.
/// </summary>
public class VanillaRouteTests
{
    public static TheoryData<string> Routes() => [.. VanillaRoute.Names()];

    [Theory]
    [MemberData(nameof(Routes))]
    public void MatchesVanilla(string route) => VanillaRoute.Load(route).Check();

    [Fact]
    public void TheSyntheticIwadHasRoutes()
    {
        Assert.Contains(VanillaRoute.Names(), n => VanillaRoute.Load(n).Iwad == "synthetic");
        Assert.Contains(VanillaRoute.Names(), n => VanillaRoute.Load(n).Iwad == "doom1");
        Assert.Contains(VanillaRoute.Names(), n => VanillaRoute.Load(n).Iwad == "testmap");
    }

    /// <summary>
    /// T5.9: every map of DOOM1.WAD's episode has a route to its exit (E1M3
    /// to its secret exit too; E1M8's starts in the arena, which only the
    /// barons' death opens: SPEC §12 T5.9), each ending on the tic that leaves
    /// the level in vanilla and the sim (<see cref="VanillaRoute.Check"/>).
    /// </summary>
    [Fact]
    public void EveryEpisode1MapHasAnExitRoute()
    {
        var exits = VanillaRoute.Names().Select(VanillaRoute.Load).Where(r => r.Iwad == "doom1" && r.Exit != 0).ToList();
        for (int m = 1; m <= 9; m++)
            Assert.Contains(exits, r => r.Map == $"E1M{m}" && r.Exit == 1);
        Assert.Contains(exits, r => r.Map == "E1M3" && r.Exit == 2);
    }

    /// <summary>T5.9: the <c>exit</c> header.</summary>
    [Theory]
    [InlineData("exit normal\n", 1)]
    [InlineData("exit secret\n", 2)]
    [InlineData("", 0)]
    [InlineData("exit\n", -1)]
    [InlineData("exit nowhere\n", -1)]
    public void ParsesTheExitHeader(string header, int exit)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"isodoom-route-{System.Guid.NewGuid():N}.route");
        System.IO.File.WriteAllText(path, "iwad synthetic\n" + header + "0 0 0 2\n");
        try
        {
            if (exit < 0)
                Assert.Throws<System.FormatException>(() => VanillaRoute.Parse(path));
            else
                Assert.Equal(exit, VanillaRoute.Parse(path).Exit);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    /// <summary>The environment variable naming the directory <see cref="WritesTheTestMapPwads"/> writes to.</summary>
    public const string TestMapWadsEnvVar = "ISODOOM_TESTMAP_WADS";

    /// <summary>
    /// T4.8a: every <see cref="RouteTestMaps"/> map builds, and with
    /// <see cref="TestMapWadsEnvVar"/> set is written there as <c>NAME.wad</c>
    /// for <c>tools/VanillaRef/routes.sh</c>; every test map route names one of them.
    /// </summary>
    [Fact]
    public void WritesTheTestMapPwads()
    {
        string? dir = System.Environment.GetEnvironmentVariable(TestMapWadsEnvVar);
        if (!string.IsNullOrEmpty(dir))
            System.IO.Directory.CreateDirectory(dir);
        foreach ((string name, System.Func<IsoDoom.Tests.Support.TestMap> make) in RouteTestMaps.Maps)
        {
            byte[] pwad = make().Build();
            if (!string.IsNullOrEmpty(dir))
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name + ".wad"), pwad);
        }
        var routes = VanillaRoute.Names().Select(VanillaRoute.Load).Where(r => r.Iwad == "testmap").ToList();
        Assert.NotEmpty(routes);
        Assert.All(routes, r => Assert.Contains(r.Map, RouteTestMaps.Maps.Keys));
    }

    /// <summary>
    /// T4.8a: the spechit route touches more special lines under one box than
    /// vanilla's <c>spechit</c> holds (8), so it covers the overrun: vanilla
    /// (Chocolate Doom's emulation) and the sim (no emulation, SPEC §12 T4.4)
    /// still agree on every tic, since the lines vanilla then skips are only
    /// the same-height special boundaries.
    /// </summary>
    [Fact]
    public void TheSpechitRouteOverruns()
    {
        var route = VanillaRoute.Load("testmap-spechit");
        IsoDoom.Sim.World world = route.NewWorld();
        int most = 0;
        foreach (IsoDoom.Sim.ticcmd_t cmd in route.Cmds)
        {
            int before = world.spechitoverruns;
            world.G_Ticker(cmd);
            most = System.Math.Max(most, world.spechitoverruns - before);
        }
        // In one tic at least 7 past the 8, as a box over 15 special lines (vanilla's emulation writes
        // tmbbox at the 9th to 12th, so it skips every line after the 10th; the sim checks them all).
        Assert.True(most >= 7, $"at most {most} overruns in a tic ({world.spechitoverruns} in all)");
    }

    /// <summary>A demo's angleturn is the byte &lt;&lt; 8, sign included.</summary>
    [Fact]
    public void TurnIsTheDemoByte()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"isodoom-route-{System.Guid.NewGuid():N}.route");
        System.IO.File.WriteAllText(path, "iwad synthetic\nskill 4 # hard\n25 -24 -1 0 x2\n0 0 127 1\n");
        try
        {
            var r = VanillaRoute.Parse(path);
            Assert.Equal(IsoDoom.Sim.skill_t.sk_hard, r.Skill);
            Assert.Equal(new short[] { -256, -256, 127 << 8 }, r.Cmds.Select(c => c.angleturn));
            Assert.Equal(new sbyte[] { -24, -24, 0 }, r.Cmds.Select(c => c.sidemove));
            Assert.Equal(1, r.Cmds[2].buttons);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }
}
