using System;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;
using static IsoDoom.Map.Fixed;

namespace IsoDoom.Tests.Map;

/// <summary>
/// T2.2: subsector polygons. Every sector's polygon area matches its linedef
/// area, and a debug render (<c>TestResults/maps/</c>) shows no gaps, overlaps
/// or polygons in the wrong sector.
/// </summary>
public class SubsectorPolygonTests
{
    /// <summary>
    /// E1 maps of the shareware v1.9 IWAD with the subsectors that cover
    /// another sector's floor or the void (node artefacts, see
    /// <see cref="PolygonChecks.Render"/>), and the sectors whose polygon area
    /// differs from their linedef area:
    /// <list type="bullet">
    /// <item>E1M3: line 933 overshoots the corner at (-320, -1920) by 8 units into
    /// the void; its seg there is the only seg of subsector 44, whose leaf covers
    /// a trapezoid of void (about 24,600 square units) west of line 927. Sector 7
    /// does not close because of that line.</item>
    /// <item>E1M6: subsector 227 has a single seg (line 558, the east wall of the
    /// 96 × 16 sector 104), and its leaf is a sliver triangle reaching 128 units
    /// north into sector 20.</item>
    /// <item>E1M7: subsector 248, a few samples' worth.</item>
    /// </list>
    /// </summary>
    public static TheoryData<string, int[], int[]> E1Maps() => new()
    {
        { "E1M1", [], [] },
        { "E1M2", [], [] },
        { "E1M3", [44], [7] },
        { "E1M4", [], [] },
        { "E1M5", [], [] },
        { "E1M6", [227], [104] },
        { "E1M7", [248], [] },
        { "E1M8", [], [] },
        { "E1M9", [], [] },
    };

    public static TheoryData<string> Doom2Maps()
    {
        var data = new TheoryData<string>();
        for (int i = 1; i <= 32; i++)
            data.Add($"MAP{i:00}");
        return data;
    }

    private static Level LoadSynthetic() =>
        Level.Load(new WadArchive([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]), "E1M1");

    private static PolygonVertex V(int x, int y) => new(x << FRACBITS, y << FRACBITS);

    [Fact]
    public void SyntheticMapPolygonsAreTheRoomsAndHalves()
    {
        Level map = LoadSynthetic();
        var polys = SubsectorPolygons.Build(map);
        PolygonChecks.CheckShapes(map, polys);

        // West room 256×256; east room split at y = 0 (a partition with no linedef on it);
        // the strip east of x = 448: room C 128×256, door D 16×256, courtyard A 256×256, ledge B 128×256.
        double[] areas = [.. polys.Polygons.Select(PolygonChecks.PolygonArea)];
        Assert.Equal(new double[] { 256 * 256, 256 * 128, 256 * 128, 128 * 256, 16 * 256, 256 * 256, 128 * 256 }, areas);
        Assert.Equal(PolygonChecks.SectorAreasFromLines(map), PolygonChecks.SectorAreasFromPolygons(map, polys));
        Assert.Equal(new[] { 256.0 * 256, 256.0 * 256, 128.0 * 256, 16.0 * 256, 256.0 * 256, 128.0 * 256 }, PolygonChecks.SectorAreasFromLines(map));

        // Each polygon's corners are exactly the room corners (clockwise, from some start).
        foreach (Subsector ss in map.Subsectors)
        {
            PolygonVertex[] p = polys.Polygons[ss.Index];
            Assert.Equal(4, p.Length);
            int minX = p.Min(v => v.X), maxX = p.Max(v => v.X), minY = p.Min(v => v.Y), maxY = p.Max(v => v.Y);
            Assert.Equal(
                new[] { new PolygonVertex(minX, maxY), new PolygonVertex(maxX, maxY), new PolygonVertex(maxX, minY), new PolygonVertex(minX, minY) }.OrderBy(v => (v.X, v.Y)),
                p.OrderBy(v => (v.X, v.Y)));
            Assert.True(SubsectorPolygons.TwiceArea(p) > 0);
            // R_PointInSubsector agrees with the polygon at its centre.
            Assert.Same(ss, map.R_PointInSubsector((minX + maxX) / 2, (minY + maxY) / 2));
        }
        Assert.Contains(polys.Polygons, p => p.Contains(V(384, 0)) && p.Contains(V(128, 0)));
        Assert.Single(polys.BySector[0]);
        Assert.Equal(2, polys.BySector[1].Length);
        Assert.All(polys.BySector.Skip(2), s => Assert.Single(s));

        PolygonChecks.Coverage c = PolygonChecks.Render(map, polys, "synthetic-E1M1.png");
        Assert.True(c.Failures == 0 && c.Covered > 0, c.ToString());
    }

    [Theory]
    [MemberData(nameof(E1Maps))]
    public void Doom1SectorAreasMatchAndRenderHasNoGaps(string name, int[] artefactSubsectors, int[] mismatchedSectors)
    {
        (PolygonChecks.Coverage c, List<PolygonChecks.AreaMismatch> bad) = CheckMap(WadArchive.Open(TestWads.RequireDoom1()), name, "doom1", step: 1);
        Assert.Equal(artefactSubsectors, c.ArtefactSubsectors.Order());
        Assert.Equal(mismatchedSectors, bad.Select(m => m.Sector));
        Assert.Equal(0, c.UnclosedGaps);
    }

    [Theory]
    [MemberData(nameof(Doom2Maps))]
    public void Doom2SectorAreasMatchAndRenderHasNoGaps(string name) =>
        CheckMap(WadArchive.Open(TestWads.RequireDoom2()), name, "doom2", step: 2);

    /// <summary>
    /// Builds the polygons and checks them: shapes, the BSP agrees with each
    /// polygon's centroid, a debug render with no gaps, overlaps or unexplained
    /// coverage, and every sector's area within tolerance unless it does not
    /// close or a node artefact touches it.
    /// </summary>
    private static (PolygonChecks.Coverage, List<PolygonChecks.AreaMismatch>) CheckMap(WadArchive wad, string name, string prefix, double step)
    {
        var map = Level.Load(wad, name);
        var polys = SubsectorPolygons.Build(map);
        PolygonChecks.CheckShapes(map, polys);
        PolygonChecks.CheckCentroidsInBsp(map, polys);

        PolygonChecks.Coverage c = PolygonChecks.Render(map, polys, $"{prefix}-{name}.png", step);
        List<PolygonChecks.AreaMismatch> bad = PolygonChecks.AreaMismatches(map, polys, c);
        string report = $"{name}: {bad.Count} sector(s) off{PolygonChecks.Describe(bad.Select(m => m.ToString()))}\n{c}";
        Assert.True(c.Failures == 0, report);
        Assert.True(bad.All(m => !m.Closed || m.BspArtefact), report);
        Assert.True(c.Covered > c.Samples / 10, report);
        return (c, bad);
    }
}
