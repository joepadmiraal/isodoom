using System;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;
using static IsoDoom.Map.Fixed;

namespace IsoDoom.Tests.Map;

/// <summary>
/// T2.4: floor triangles. Each sector's triangle area equals its polygon area
/// (exactly on the synthetic map; elsewhere exactly the welded rings' area,
/// within the welding bound of the polygons'), no triangle is degenerate, and
/// no corner lies inside another triangle's edge (no T-junctions).
/// </summary>
public class FloorTriangleTests
{
    public static TheoryData<string> E1Maps()
    {
        var data = new TheoryData<string>();
        for (int i = 1; i <= 9; i++)
            data.Add($"E1M{i}");
        return data;
    }

    public static TheoryData<string> Doom2Maps() => SubsectorPolygonTests.Doom2Maps();

    private static Level LoadSynthetic() =>
        Level.Load(new WadArchive(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) }), "E1M1");

    private static PolygonVertex V(int x, int y) => new(x << FRACBITS, y << FRACBITS);

    private static (PolygonVertex, PolygonVertex, PolygonVertex)[] Triangles(SectorFloor f) =>
        Enumerable.Range(0, f.TriangleCount).Select(t => (f.Corner(t, 0), f.Corner(t, 1), f.Corner(t, 2))).ToArray();

    [Fact]
    public void SyntheticMapTrianglesAreExact()
    {
        Level map = LoadSynthetic();
        SubsectorPolygons polys = SubsectorPolygons.Build(map);
        FloorTriangles floors = FloorTriangles.Build(map, polys);
        FloorChecks.Stats stats = FloorChecks.Check(map, polys, floors);
        Assert.Equal(15, stats.Triangles);
        Assert.Equal(1, stats.Inserted);
        Assert.Equal(0, stats.MaxPolygonAreaDrift);

        // The west room's east edge has the east room's split corner (128, 0) on it (a T-junction):
        // it is inserted, and the room becomes a fan of three triangles from its first corner.
        Assert.Equal(new[] { V(-128, -128), V(-128, 128), V(128, 128), V(128, 0), V(128, -128) }, floors.Rings[0]);
        var expected = new[]
        {
            new[] { (V(-128, -128), V(-128, 128), V(128, 128)), (V(-128, -128), V(128, 128), V(128, 0)), (V(-128, -128), V(128, 0), V(128, -128)) },
            // East room: the two halves either side of the partition at y = 0.
            new[] { (V(128, -128), V(128, 0), V(384, 0)), (V(128, -128), V(384, 0), V(384, -128)), (V(384, 0), V(128, 0), V(128, 128)), (V(384, 0), V(128, 128), V(384, 128)) },
            // The strip: room C, closed door D, courtyard A, ledge B, each a rectangle fanned from its first corner.
            new[] { (V(512, -128), V(512, 128), V(640, 128)), (V(512, -128), V(640, 128), V(640, -128)) },
            new[] { (V(640, -128), V(640, 128), V(656, 128)), (V(640, -128), V(656, 128), V(656, -128)) },
            new[] { (V(656, -128), V(656, 128), V(912, 128)), (V(656, -128), V(912, 128), V(912, -128)) },
            new[] { (V(912, -128), V(912, 128), V(1040, 128)), (V(912, -128), V(1040, 128), V(1040, -128)) },
        };
        Assert.Equal(expected.Length, floors.BySector.Count);
        for (int s = 0; s < expected.Length; s++)
        {
            Assert.Equal(expected[s], Triangles(floors.BySector[s]));
            Assert.Equal(polys.BySector[s], floors.BySector[s].Subsectors);
            // Shared corners appear once per sector.
            Assert.Equal(floors.BySector[s].Vertices.Count, floors.BySector[s].Vertices.Distinct().Count());
        }
        Assert.Equal(new[] { 0, 1, 1, 2, 3, 4, 5 }, floors.FloorSectorOf);

        // Triangle area per sector equals the polygon area (and the linedef area) exactly.
        double[] fromLines = PolygonChecks.SectorAreasFromLines(map);
        for (int s = 0; s < expected.Length; s++)
        {
            Int128 twice = 0;
            foreach ((PolygonVertex a, PolygonVertex b, PolygonVertex c) in Triangles(floors.BySector[s]))
                twice += FloorTriangles.TwiceArea(a, b, c);
            Assert.Equal(polys.BySector[s].Aggregate((Int128)0, (sum, ss) => sum + SubsectorPolygons.TwiceArea(polys.Polygons[ss])), twice);
            Assert.Equal(fromLines[s], (double)twice / 2 / FRACUNIT / FRACUNIT);
        }
    }

    [Fact]
    public void TrianglesAreClockwiseWithYUp()
    {
        // (0,1) → (1,1) → (1,0) turns right with y up: clockwise, positive.
        Assert.Equal((Int128)1 << 32, FloorTriangles.TwiceArea(V(0, 1), V(1, 1), V(1, 0)));
        Assert.True(FloorTriangles.TwiceArea(V(0, 1), V(1, 0), V(1, 1)) < 0);
    }

    [Fact]
    public void OnEdgeIsStrictlyBetweenTheEndsWithinTheTolerance()
    {
        PolygonVertex a = V(0, 0), b = V(100, 0);
        Assert.True(FloorTriangles.OnEdge(a, b, V(50, 0)));
        Assert.True(FloorTriangles.OnEdge(a, b, new PolygonVertex(50 << FRACBITS, FloorTriangles.OnEdgeEpsilon / 2)));
        Assert.False(FloorTriangles.OnEdge(a, b, new PolygonVertex(50 << FRACBITS, FloorTriangles.OnEdgeEpsilon * 2)));
        Assert.False(FloorTriangles.OnEdge(a, b, a));
        Assert.False(FloorTriangles.OnEdge(a, b, b));
        Assert.False(FloorTriangles.OnEdge(a, b, new PolygonVertex(FloorTriangles.EndEpsilon, 0)));
        Assert.False(FloorTriangles.OnEdge(a, b, V(-1, 0)));
        Assert.False(FloorTriangles.OnEdge(a, b, V(101, 0)));
    }

    [Fact]
    public void FloorSectorHookHidesOrMovesASubsectorsFloor()
    {
        Level map = LoadSynthetic();
        SubsectorPolygons polys = SubsectorPolygons.Build(map);

        // Hide the lower half of the east room: its corners still go into the
        // west room's ring, and the east room keeps only its upper half.
        int lower = polys.BySector[1].First(ss => polys.Polygons[ss].All(v => v.Y <= 0));
        int upper = polys.BySector[1].Single(ss => ss != lower);
        FloorTriangles hidden = FloorTriangles.Build(map, polys, ss => ss.Index == lower ? null : ss.Sector);
        Assert.Empty(hidden.Rings[lower]);
        Assert.Equal(-1, hidden.FloorSectorOf[lower]);
        Assert.Equal(new[] { upper }, hidden.BySector[1].Subsectors);
        Assert.Equal(2, hidden.BySector[1].TriangleCount);
        Assert.Contains(V(128, 0), hidden.Rings[0]);
        Assert.Equal(3, hidden.BySector[0].TriangleCount);
        FloorChecks.CheckNoTJunctions(hidden);

        // Draw the west room's floor as sector 2's.
        FloorTriangles moved = FloorTriangles.Build(map, polys, ss => ss.Index == 0 ? map.Sectors[2] : ss.Sector);
        Assert.Equal(0, moved.BySector[0].TriangleCount);
        Assert.Equal(new[] { 0, polys.BySector[2][0] }, moved.BySector[2].Subsectors);
        Assert.Equal(5, moved.BySector[2].TriangleCount);
        Assert.Equal(2, moved.FloorSectorOf[0]);

        // A sector of another level is refused.
        Level other = LoadSynthetic();
        Assert.Throws<ArgumentException>(() => FloorTriangles.Build(map, polys, _ => other.Sectors[0]));
    }

    [Theory]
    [MemberData(nameof(E1Maps))]
    public void Doom1FloorsHaveNoDegenerateTrianglesOrTJunctions(string name)
    {
        Level map = Level.Load(WadArchive.Open(TestWads.RequireDoom1()), name);
        SubsectorPolygons polys = SubsectorPolygons.Build(map);
        FloorTriangles floors = FloorTriangles.Build(map, polys);
        FloorChecks.Stats stats = FloorChecks.Check(map, polys, floors);
        Assert.True(stats.Inserted > 0, stats.ToString());
        // No polygon collapses when welded.
        Assert.All(Enumerable.Range(0, map.Subsectors.Length), i => Assert.Equal(polys.Polygons[i].Length == 0, floors.FloorSectorOf[i] < 0));
        // The default floor is the subsector's own sector, node artefacts included (T2.2a may change that through the hook).
        Assert.All(map.Subsectors.Where(ss => polys.Polygons[ss.Index].Length > 0), ss => Assert.Equal(ss.Sector.Index, floors.FloorSectorOf[ss.Index]));
        if (name == "E1M1")
        {
            Assert.Equal(692, stats.Triangles);
            Assert.Equal(239, stats.Inserted);
        }

        // Deterministic: a second build is identical.
        FloorTriangles again = FloorTriangles.Build(map, polys);
        for (int s = 0; s < map.Sectors.Length; s++)
        {
            Assert.Equal(floors.BySector[s].Vertices, again.BySector[s].Vertices);
            Assert.Equal(floors.BySector[s].Indices, again.BySector[s].Indices);
        }
    }

    [Theory]
    [MemberData(nameof(Doom2Maps))]
    public void Doom2FloorsHaveNoDegenerateTrianglesOrTJunctions(string name)
    {
        Level map = Level.Load(WadArchive.Open(TestWads.RequireDoom2()), name);
        SubsectorPolygons polys = SubsectorPolygons.Build(map);
        FloorTriangles floors = FloorTriangles.Build(map, polys);
        FloorChecks.Check(map, polys, floors);
        Assert.All(Enumerable.Range(0, map.Subsectors.Length), i => Assert.Equal(polys.Polygons[i].Length == 0, floors.FloorSectorOf[i] < 0));
    }

    // ---- SectorAt (T2.7's overlay: the floor under the free-fly camera) ----

    [Fact]
    public void SyntheticSectorAtMatchesTheBsp()
    {
        Level map = LoadSynthetic();
        FloorTriangles floors = FloorTriangles.Build(map, SubsectorPolygons.Build(map));
        Assert.Equal(map.R_PointInSubsector(0, 0).Sector.Index, floors.SectorAt(0, 0)); // player 1 start
        Assert.Equal(-1, floors.SectorAt(5000 << FRACBITS, 5000 << FRACBITS));
        Assert.True(CheckSectorAtGrid(map, floors, 4) > 1000);
    }

    [Fact]
    public void Doom1E1M1SectorAtMatchesTheBsp()
    {
        Level map = Level.Load(WadArchive.Open(TestWads.RequireDoom1()), "E1M1");
        FloorTriangles floors = FloorTriangles.Build(map, SubsectorPolygons.Build(map));
        Assert.Equal(38, floors.SectorAt(1056 << FRACBITS, -3616 << FRACBITS)); // player 1 start
        // Inside the zig-zag corridor's bend: void, though the BSP's leaf there belongs to sector 16.
        Assert.Equal(-1, floors.SectorAt(2336 << FRACBITS, -3957 << FRACBITS));
        Assert.Equal(16, map.R_PointInSubsector(2336 << FRACBITS, -3957 << FRACBITS).Sector.Index);
        Assert.True(CheckSectorAtGrid(map, floors, 16) > 10000);
    }

    /// <summary>
    /// On a grid of <paramref name="step"/> map units over the map, wherever
    /// a floor is drawn (<see cref="FloorTriangles.SectorAt"/>) more than a
    /// unit from every linedef, <see cref="Level.R_PointInSubsector"/> names
    /// the same sector (so the overlay's sector is the floor under the
    /// camera). Returns the number of points compared.
    /// </summary>
    private static int CheckSectorAtGrid(Level map, FloorTriangles floors, int step)
    {
        int minX = map.Vertexes.Min(v => v.X >> FRACBITS), maxX = map.Vertexes.Max(v => v.X >> FRACBITS);
        int minY = map.Vertexes.Min(v => v.Y >> FRACBITS), maxY = map.Vertexes.Max(v => v.Y >> FRACBITS);
        var lines = map.Lines.Select(l => (X1: l.V1.X / 65536.0, Y1: l.V1.Y / 65536.0, X2: l.V2.X / 65536.0, Y2: l.V2.Y / 65536.0)).ToArray();
        int compared = 0;
        var wrong = new System.Collections.Generic.List<string>();
        for (int y = minY + step / 2; y < maxY; y += step)
        {
            for (int x = minX + step / 2; x < maxX; x += step)
            {
                int drawn = floors.SectorAt(x << FRACBITS, y << FRACBITS);
                if (drawn < 0 || lines.Any(l => DistanceToSegment(x, y, l) <= 1))
                    continue;
                compared++;
                int bsp = map.R_PointInSubsector(x << FRACBITS, y << FRACBITS).Sector.Index;
                if (bsp != drawn)
                    wrong.Add($"({x}, {y}): floor of sector {drawn}, BSP sector {bsp}");
            }
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        return compared;
    }

    private static double DistanceToSegment(double x, double y, (double X1, double Y1, double X2, double Y2) l)
    {
        double dx = l.X2 - l.X1, dy = l.Y2 - l.Y1;
        double t = Math.Clamp(((x - l.X1) * dx + (y - l.Y1) * dy) / (dx * dx + dy * dy), 0, 1);
        return Math.Sqrt(Math.Pow(x - l.X1 - t * dx, 2) + Math.Pow(y - l.Y1 - t * dy, 2));
    }
}
