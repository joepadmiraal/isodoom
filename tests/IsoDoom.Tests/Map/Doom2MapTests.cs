using System.Linq;
using IsoDoom.Map;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Map;

/// <summary>T2.1 against a DOOM II IWAD (skipped without it); counts only for v1.666.</summary>
public class Doom2MapTests
{
    public static TheoryData<string> MapNames()
    {
        var data = new TheoryData<string>();
        for (int i = 1; i <= 32; i++)
            data.Add($"MAP{i:00}");
        return data;
    }

    [Theory]
    [MemberData(nameof(MapNames))]
    public void EveryMapLoadsConsistently(string name)
    {
        var wad = WadArchive.Open(TestWads.RequireDoom2());
        var map = Level.Load(wad, name);
        MapTestChecks.CheckConsistent(wad, map);
        Assert.Contains(map.Things, t => t.Type == 1);
    }

    [Fact]
    public void Map01ElementCountsAndPlayerStart()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom2());
        if (TestWads.Doom2Md5 != TestWads.Doom2V1666Md5)
            Assert.Skip("MAP01 counts are asserted for DOOM II v1.666 only.");
        var map = Level.Load(wad, "MAP01");
        Assert.Equal(
            (69, 370, 529, 383, 601, 194, 193, 59),
            (map.Things.Length, map.Lines.Length, map.Sides.Length, map.Vertexes.Length,
             map.Segs.Length, map.Subsectors.Length, map.Nodes.Length, map.Sectors.Length));
        Assert.Equal(new MapThing(1568, 3168, 90, 1, 7), map.Things.Single(t => t.Type == 1));
    }
}
