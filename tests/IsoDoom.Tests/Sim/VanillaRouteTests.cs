using System.Linq;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T4.8: every route in <c>Routes/</c> plays exactly as in vanilla, tic by
/// tic (<see cref="VanillaRoute"/>). The synthetic IWAD's routes check against
/// committed dumps (in CI); DOOM1.WAD's need the WAD and a local dump from
/// <c>tools/VanillaRef/routes.sh</c>, and skip without them.
/// </summary>
public class VanillaRouteTests
{
    public static TheoryData<string> Routes() => new(VanillaRoute.Names());

    [Theory]
    [MemberData(nameof(Routes))]
    public void MatchesVanilla(string route) => VanillaRoute.Load(route).Check();

    [Fact]
    public void TheSyntheticIwadHasRoutes()
    {
        Assert.Contains(VanillaRoute.Names(), n => VanillaRoute.Load(n).Iwad == "synthetic");
        Assert.Contains(VanillaRoute.Names(), n => VanillaRoute.Load(n).Iwad == "doom1");
    }

    /// <summary>A demo's angleturn is the byte &lt;&lt; 8, sign included.</summary>
    [Fact]
    public void TurnIsTheDemoByte()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"isodoom-route-{System.Guid.NewGuid():N}.route");
        System.IO.File.WriteAllText(path, "iwad synthetic\nskill 4 # hard\n25 -24 -1 0 x2\n0 0 127 1\n");
        try
        {
            VanillaRoute r = VanillaRoute.Parse(path);
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
