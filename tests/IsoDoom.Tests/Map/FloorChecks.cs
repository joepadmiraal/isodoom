using System;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Map;
using Xunit;

namespace IsoDoom.Tests.Map;

/// <summary>
/// Checks for <see cref="FloorTriangles"/> (T2.4): each sector's triangles
/// cover exactly its rings' area, no triangle is degenerate, every ring corner
/// is used, and no corner of any triangle lies inside another triangle's edge
/// (no T-junctions left anywhere on the map).
/// </summary>
public static class FloorChecks
{
    private const double Unit = Fixed.FRACUNIT;

    /// <summary>What <see cref="Check"/> measured.</summary>
    public sealed record Stats(int Triangles, int Inserted, int SubsectorsWithInserted, double MaxPolygonAreaDrift, int EdgesChecked)
    {
        public override string ToString() =>
            $"{Triangles} triangles, {Inserted} T-junction corners inserted in {SubsectorsWithInserted} subsectors, " +
            $"largest sector area drift from the polygons {MaxPolygonAreaDrift:G4} sq units, {EdgesChecked} edges checked";
    }

    /// <summary>Runs every check and returns the statistics.</summary>
    public static Stats Check(Level map, SubsectorPolygons polys, FloorTriangles floors)
    {
        Assert.Equal(map.Subsectors.Length, floors.Rings.Count);
        Assert.Equal(map.Sectors.Length, floors.BySector.Count);
        int triangles = 0, inserted = 0, withInserted = 0;
        double maxDrift = 0;

        for (int s = 0; s < floors.BySector.Count; s++)
        {
            SectorFloor floor = floors.BySector[s];
            Assert.Equal(s, floor.Sector);
            Assert.Equal(0, floor.Indices.Count % 3);
            // Default grouping: the polygons' own, less polygons that welding collapsed.
            Assert.Equal(polys.BySector[s].Where(ss => floors.FloorSectorOf[ss] == s), floor.Subsectors);

            // Each triangle strictly clockwise (not degenerate).
            Int128 triangleTwice = 0;
            for (int t = 0; t < floor.TriangleCount; t++)
            {
                Int128 a = FloorTriangles.TwiceArea(floor.Corner(t, 0), floor.Corner(t, 1), floor.Corner(t, 2));
                Assert.True(a > 0, $"sector {s} triangle {t} has area {(double)a / 2 / Unit / Unit}");
                triangleTwice += a;
            }

            // Triangle area equals the rings' area exactly, and the polygons' area up to
            // how far welding and the inserted corners move the outline.
            Int128 ringTwice = 0, polygonTwice = 0, driftBound = 0;
            var used = new HashSet<PolygonVertex>(floor.Vertices);
            foreach (int ss in polys.BySector[s])
            {
                PolygonVertex[] ring = floors.Rings[ss], polygon = polys.Polygons[ss];
                long perimeter = 0;
                for (int i = 0; i < polygon.Length; i++)
                {
                    PolygonVertex u = polygon[i], v = polygon[(i + 1) % polygon.Length];
                    perimeter += Math.Abs((long)v.X - u.X) + Math.Abs((long)v.Y - u.Y);
                }
                // Moving every corner by up to WeldEpsilon on each axis changes twice the area by at most
                // 2 × WeldEpsilon × the L1 perimeter; an inserted corner by OnEdgeEpsilon × its edge's L1 length.
                Int128 bound = (Int128)2 * FloorTriangles.WeldEpsilon * perimeter
                    + (Int128)Math.Max(0, ring.Length - polygon.Length) * FloorTriangles.OnEdgeEpsilon * perimeter;
                polygonTwice += SubsectorPolygons.TwiceArea(polygon);
                driftBound += bound;
                if (floors.FloorSectorOf[ss] != s)
                {
                    // Collapsed by welding: only a sliver may go.
                    Assert.Empty(ring);
                    Assert.True(SubsectorPolygons.TwiceArea(polygon) <= bound, $"subsector {ss} collapsed with area {PolygonChecks.PolygonArea(polygon)}");
                    continue;
                }
                ringTwice += SubsectorPolygons.TwiceArea(ring);
                // Every polygon corner is still there, up to welding.
                foreach (PolygonVertex c in polygon)
                    Assert.Contains(ring, r => Math.Abs((long)r.X - c.X) <= FloorTriangles.WeldEpsilon && Math.Abs((long)r.Y - c.Y) <= FloorTriangles.WeldEpsilon);
                int extra = ring.Length - polygon.Length;
                if (extra > 0)
                {
                    inserted += extra;
                    withInserted++;
                }
                Assert.All(ring, p => Assert.Contains(p, used));
            }
            Assert.Equal(ringTwice, triangleTwice);
            Assert.True(Int128.Abs(ringTwice - polygonTwice) <= driftBound, $"sector {s}: rings and polygons differ by more than welding and the inserted corners allow");
            maxDrift = Math.Max(maxDrift, (double)Int128.Abs(ringTwice - polygonTwice) / 2 / Unit / Unit);
            triangles += floor.TriangleCount;
        }

        for (int i = 0; i < floors.Rings.Count; i++)
            Assert.Equal(floors.FloorSectorOf[i] < 0, floors.Rings[i].Length == 0);

        int edges = CheckNoTJunctions(floors);
        return new Stats(triangles, inserted, withInserted, maxDrift, edges);
    }

    /// <summary>
    /// No corner of any triangle (any sector) lies on any triangle edge strictly
    /// between its ends (<see cref="FloorTriangles.OnEdge"/>). Returns the number of edges checked.
    /// </summary>
    public static int CheckNoTJunctions(FloorTriangles floors)
    {
        const int Shift = Fixed.FRACBITS + 6;
        var cells = new Dictionary<(int, int), List<PolygonVertex>>();
        var all = new HashSet<PolygonVertex>();
        foreach (SectorFloor f in floors.BySector)
        {
            foreach (PolygonVertex v in f.Vertices)
            {
                if (!all.Add(v))
                    continue;
                (int, int) key = (v.X >> Shift, v.Y >> Shift);
                if (!cells.TryGetValue(key, out List<PolygonVertex>? list))
                    cells[key] = list = [];
                list.Add(v);
            }
        }

        var bad = new List<string>();
        int edges = 0;
        foreach (SectorFloor f in floors.BySector)
        {
            for (int t = 0; t < f.TriangleCount; t++)
            {
                for (int k = 0; k < 3; k++)
                {
                    PolygonVertex a = f.Corner(t, k), b = f.Corner(t, (k + 1) % 3);
                    edges++;
                    long eps = FloorTriangles.OnEdgeEpsilon;
                    int x0 = (int)((Math.Min(a.X, b.X) - eps) >> Shift), x1 = (int)((Math.Max(a.X, b.X) + eps) >> Shift);
                    int y0 = (int)((Math.Min(a.Y, b.Y) - eps) >> Shift), y1 = (int)((Math.Max(a.Y, b.Y) + eps) >> Shift);
                    for (int cx = x0; cx <= x1; cx++)
                    {
                        for (int cy = y0; cy <= y1; cy++)
                        {
                            if (!cells.TryGetValue((cx, cy), out List<PolygonVertex>? list))
                                continue;
                            foreach (PolygonVertex p in list)
                            {
                                if (FloorTriangles.OnEdge(a, b, p))
                                    bad.Add($"sector {f.Sector} triangle {t}: corner {p} on edge {a}→{b}");
                            }
                        }
                    }
                }
            }
        }
        Assert.True(bad.Count == 0, $"{bad.Count} T-junction(s){PolygonChecks.Describe(bad)}");
        return edges;
    }
}
