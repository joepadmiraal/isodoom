using System;
using System.Linq;
using IsoDoom.Tests.Support;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// P10: the PWAD sets' routes (<see cref="VanillaRoute.PwadDir"/>, SPEC §12
/// "P10"): each plays exactly as in vanilla, tic by tic, against its dump from
/// <c>tools/VanillaRef/routes.sh</c> (WAD-derived, in
/// <see cref="VanillaRoute.DumpDirEnvVar"/>; skips without the set, its IWAD or
/// the dump). Not in <see cref="RouteChecksumTests"/>: a set's checksums would
/// be WAD-derived too, and the vanilla dump checks more.
/// </summary>
public class PwadRouteTests
{
    public static TheoryData<string> Routes() => [.. VanillaRoute.PwadNames()];

    [Theory]
    [MemberData(nameof(Routes))]
    public void MatchesVanilla(string route) => VanillaRoute.Load(route).Check();

    /// <summary>
    /// Every PWAD route names a set (and only those: <see cref="VanillaRoute.Names"/>
    /// has none), and every set has one from its first map through its normal
    /// exit, whose intermission and next map <c>IntermissionTests</c> check.
    /// </summary>
    [Fact]
    public void EverySetHasARouteFromItsFirstMapToItsExit()
    {
        var routes = VanillaRoute.PwadNames().Select(VanillaRoute.Load).ToList();
        Assert.All(routes, r => Assert.NotNull(r.Pwad));
        Assert.All(routes, r => Assert.StartsWith(r.Pwad!.Folder + "-", r.Name, StringComparison.Ordinal));
        Assert.DoesNotContain(VanillaRoute.Names(), n => VanillaRoute.Load(n).Pwad is not null);
        foreach (PwadSet set in PwadSets.All)
            Assert.Contains(routes, r => r.Pwad == set && r.Map == set.Maps[0] && r.Exit == 1);
    }
}
