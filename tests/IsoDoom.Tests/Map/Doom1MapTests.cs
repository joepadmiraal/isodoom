using System.Linq;
using IsoDoom.Map;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;
using static IsoDoom.Map.Fixed;

namespace IsoDoom.Tests.Map;

/// <summary>T2.1 against the shareware v1.9 DOOM1.WAD (skipped without it).</summary>
public class Doom1MapTests
{
    private static WadArchive OpenDoom1() => WadArchive.Open(TestWads.RequireDoom1());

    [Fact]
    public void E1M1ElementCountsAndPlayerStart()
    {
        Level map = Level.Load(OpenDoom1(), "E1M1");
        Assert.Equal(138, map.Things.Length);
        Assert.Equal(475, map.Lines.Length);
        Assert.Equal(648, map.Sides.Length);
        Assert.Equal(467, map.Vertexes.Length);
        Assert.Equal(732, map.Segs.Length);
        Assert.Equal(237, map.Subsectors.Length);
        Assert.Equal(236, map.Nodes.Length);
        Assert.Equal(85, map.Sectors.Length);
        Assert.Equal(904, map.Reject.RejectMatrix.Length);
        Blockmap bm = map.Blockmap;
        Assert.Equal((-776 * FRACUNIT, -4872 * FRACUNIT, 36, 23), (bm.BmapOrgX, bm.BmapOrgY, bm.BmapWidth, bm.BmapHeight));

        MapThing start = Assert.Single(map.Things, t => t.Type == 1);
        Assert.Equal(new MapThing(1056, -3616, 90, 1, MapThing.MTF_EASY | MapThing.MTF_NORMAL | MapThing.MTF_HARD), start);
        Sector startSector = map.R_PointInSubsector(start.X << FRACBITS, start.Y << FRACBITS).Sector;
        Assert.Equal(38, startSector.Index);
        Assert.Equal(0, startSector.FloorHeight);
    }

    [Theory]
    [InlineData("E1M1")]
    [InlineData("E1M2")]
    [InlineData("E1M3")]
    [InlineData("E1M4")]
    [InlineData("E1M5")]
    [InlineData("E1M6")]
    [InlineData("E1M7")]
    [InlineData("E1M8")]
    [InlineData("E1M9")]
    public void EveryMapLoadsConsistently(string name)
    {
        WadArchive wad = OpenDoom1();
        Level map = Level.Load(wad, name);
        MapTestChecks.CheckConsistent(wad, map);
        Assert.Single(map.Things, t => t.Type == 1);
        // Every seg faces into its subsector's sector in the shareware maps.
        foreach (Subsector ss in map.Subsectors)
        {
            for (int i = ss.FirstLine; i < ss.FirstLine + ss.NumLines; i++)
                Assert.Same(ss.Sector, map.Segs[i].FrontSector);
        }
    }
}
