using System.Linq;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T8.6: every route of <c>Routes/</c> and the IWADs' demos play to the
/// committed checksums (<see cref="RouteChecksums"/>): the synthetic IWAD's
/// and the test maps' always, the id IWADs' with their WAD.
/// </summary>
public class RouteChecksumTests
{
    public static TheoryData<string> Routes() => [.. VanillaRoute.Names()];

    public static TheoryData<string> Demos() => [.. RouteChecksums.Demos];

    [Theory]
    [MemberData(nameof(Routes))]
    public void RoutePlaysToTheCommittedChecksums(string route) => RouteChecksums.Check(VanillaRoute.Load(route));

    [Theory]
    [MemberData(nameof(Demos))]
    public void DemoPlaysToTheCommittedChecksums(string demo)
    {
        VanillaRoute route = RouteChecksums.Demo(demo);
        Assert.Equal(demo, route.Name);
        RouteChecksums.Check(route);
    }

    /// <summary>Every route and demo has an entry and every entry one of them, without any WAD (CI).</summary>
    [Fact]
    public void EveryRouteHasCommittedChecksums()
    {
        if (RouteChecksums.Updating)
            Assert.Skip($"{RouteChecksums.UpdateEnvVar} is set.");
        RouteChecksums.Entries entries = RouteChecksums.Read();
        string[] names = [.. VanillaRoute.Names(), .. RouteChecksums.Demos];
        Assert.All(names, n => Assert.True(entries.Routes.ContainsKey(n), $"{n}: no committed checksums"));
        Assert.All(entries.Routes.Keys, n => Assert.True(names.Contains(n), $"{n}: checksums of no route or demo"));
        Assert.All(entries.Routes, e =>
        {
            Assert.Equal((e.Value.Tics + RouteChecksums.Interval - 1) / RouteChecksums.Interval, e.Value.Hashes.Length);
            Assert.All(e.Value.Hashes, h => Assert.Matches("^[0-9a-f]{8}$", h));
        });
        Assert.Equal(["doom1", "doom2", "plutonia", "tnt"], entries.Iwads.Keys);
    }
}
