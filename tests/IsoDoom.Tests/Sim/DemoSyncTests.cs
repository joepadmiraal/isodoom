using System;
using System.Linq;
using IsoDoom.Game;
using IsoDoom.Sim;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T6.13 (SPEC §10's stretch): DOOM1.WAD's demos, read by the vanilla-input
/// adapter (<see cref="RouteFile.FromDemo"/>) and played with every tweak off,
/// stay in sync with vanilla to their end, tic by tic, against the
/// reference's dumps (<c>tools/VanillaRef/demos.sh</c>: WAD-derived, in
/// <see cref="VanillaRoute.DumpDirEnvVar"/>; skips without DOOM1.WAD or the dump).
/// </summary>
public class DemoSyncTests
{
    public static TheoryData<string> Demos() => new("DEMO1", "DEMO2", "DEMO3");

    [Theory]
    [MemberData(nameof(Demos))]
    public void StaysInSyncWithVanilla(string demo) => VanillaRoute.Demo(demo).Check();

    /// <summary>DOOM1.WAD v1.9's demos: their maps, Hurt Me Plenty, monsters, and their lengths.</summary>
    [Theory]
    [InlineData("DEMO1", "E1M5", 5026)]
    [InlineData("DEMO2", "E1M3", 3836)]
    [InlineData("DEMO3", "E1M7", 2134)]
    public void ReadsTheDemoHeaders(string demo, string map, int tics)
    {
        var route = VanillaRoute.Demo(demo);
        Assert.Equal(map, route.Map);
        Assert.Equal(skill_t.sk_medium, route.Skill);
        Assert.True(route.Monsters);
        Assert.Equal(tics, route.Cmds.Count);
    }

    /// <summary>g_game.c's demo format: the header, 4 bytes a tic, the marker; what the sim cannot play is refused.</summary>
    [Fact]
    public void ReadsAV19Demo()
    {
        byte[] lump = [109, 3, 1, 2, 0, 0, 0, 1, 0, 1, 0, 0, 0, 25, 0xe8, 0xff, 1, 0x32, 0, 0x7f, 6, RouteFile.DEMOMARKER];
        var r = RouteFile.FromDemo(lump, "test");
        Assert.Equal("E1M2", r.Map);
        Assert.Equal(4, r.Skill);
        Assert.False(r.Monsters);
        Assert.Equal(new sbyte[] { 25, 0x32 }, r.Cmds.Select(c => c.forwardmove));
        Assert.Equal(new sbyte[] { -24, 0 }, r.Cmds.Select(c => c.sidemove));
        Assert.Equal(new short[] { -256, 127 << 8 }, r.Cmds.Select(c => c.angleturn));
        Assert.Equal(new byte[] { 1, 6 }, r.Cmds.Select(c => c.buttons));
        Assert.Equal("MAP02", RouteFile.FromDemo(lump, "test", commercial: true).Map);

        Assert.Throws<FormatException>(() => RouteFile.FromDemo(lump.AsSpan(..^1), "no marker"));
        Assert.Throws<FormatException>(() => RouteFile.FromDemo(lump.AsSpan(..12), "short"));
        byte[] old = (byte[])lump.Clone();
        old[0] = 110;
        Assert.Throws<FormatException>(() => RouteFile.FromDemo(old, "version"));
        foreach (int i in new[] { 4, 5, 6, 8, 10 })
        {
            byte[] other = (byte[])lump.Clone();
            other[i] = 1;
            Assert.Throws<NotSupportedException>(() => RouteFile.FromDemo(other, $"byte {i}"));
        }
    }
}
