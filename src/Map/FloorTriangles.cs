using System;
using System.Collections.Generic;

namespace IsoDoom.Map;

/// <summary>
/// The floor triangles of one sector (SPEC §7.2 <i>Floors</i>): an indexed
/// triangle list over corners shared within the sector, in map space (fixed_t).
/// </summary>
public sealed class SectorFloor
{
    internal SectorFloor(int sector, int[] subsectors, PolygonVertex[] vertices, int[] indices)
    {
        Sector = sector;
        Subsectors = subsectors;
        Vertices = vertices;
        Indices = indices;
    }

    /// <summary>The sector number (index into <see cref="Level.Sectors"/>).</summary>
    public int Sector { get; }

    /// <summary>The subsectors whose polygons make up this floor, in subsector order.</summary>
    public IReadOnlyList<int> Subsectors { get; }

    /// <summary>The distinct corners of this sector's triangles (each appears once).</summary>
    public IReadOnlyList<PolygonVertex> Vertices { get; }

    /// <summary>
    /// Three indices into <see cref="Vertices"/> per triangle, each triangle
    /// <b>clockwise in map space (y up)</b>, like the subsector polygons.
    /// </summary>
    public IReadOnlyList<int> Indices { get; }

    /// <summary>The number of triangles.</summary>
    public int TriangleCount => Indices.Count / 3;

    /// <summary>Corner <paramref name="corner"/> (0–2) of triangle <paramref name="triangle"/>.</summary>
    public PolygonVertex Corner(int triangle, int corner) => Vertices[Indices[triangle * 3 + corner]];
}

/// <summary>
/// Per-sector floor triangles built from <see cref="SubsectorPolygons"/>
/// (SPEC §7.2 <i>Floors</i>), integer only like the polygons, so it stays
/// under the determinism scan (SPEC §12).
/// <para>
/// <b>T-junctions.</b> Neighbouring subsector polygons share edges but not
/// always corners: a corner of one may sit in the middle of the other's edge.
/// The GPU rasterises the two edges differently there, which shows
/// single-pixel cracks. <see cref="Build"/> therefore first welds corners
/// that differ only by rounding (<see cref="WeldEpsilon"/>), then inserts into each
/// polygon edge every corner of any other subsector polygon (any sector) that
/// lies on it (<see cref="OnEdge"/>), giving the <see cref="Rings"/>, and then
/// triangulates each ring by ear clipping. On a convex polygon without
/// inserted corners that is a fan from its first corner; with inserted
/// corners (collinear, or off the edge line by a rounding error) it uses every
/// corner, never makes a zero-area triangle, and never leaves a corner on a
/// diagonal, so the triangles of the whole map meet corner to corner.
/// </para>
/// <para>
/// <b>Winding.</b> Every triangle is clockwise in map space with y up (the
/// interior on the right of each edge, as for segs and subsector polygons).
/// The renderer maps map (x, y) to its own axes; with Godot's usual mapping
/// (map x → +X, map y → −Z, so north is −Z, floors facing +Y) the triangles
/// stay clockwise seen from above, but the renderer must check its front face
/// after the axis flip.
/// </para>
/// </summary>
public sealed class FloorTriangles
{
    /// <summary>
    /// fixed_t: a corner this close to the line of another polygon's edge
    /// (measured like <see cref="SubsectorPolygons.OnLineEpsilon"/>, with the
    /// L1 length of the edge) and strictly between its ends counts as on that
    /// edge: a T-junction (1/256 map unit, the polygons' own on-line tolerance).
    /// </summary>
    public const int OnEdgeEpsilon = SubsectorPolygons.OnLineEpsilon;

    /// <summary>
    /// fixed_t: a corner within this distance (on both axes) of an edge's end
    /// is that end, not a T-junction (the polygons' merge tolerance).
    /// </summary>
    public const int EndEpsilon = SubsectorPolygons.MergeEpsilon;

    /// <summary>
    /// fixed_t: corners of different polygons this close (on both axes) are
    /// one corner (1/32 map unit). Neighbouring polygons compute a shared
    /// corner separately, each rounded to fixed_t, and corners where nearly
    /// parallel lines cross can differ by a hundredth of a unit; welding them
    /// first keeps a near-duplicate from being inserted as a T-junction on
    /// both edges at that corner. Far above <see cref="OnEdgeEpsilon"/>, so a
    /// corner can lie on at most one edge of a polygon.
    /// </summary>
    public const int WeldEpsilon = Fixed.FRACUNIT / 32;

    // Grid cells of 64 map units for looking up corners near an edge.
    private const int CellShift = Fixed.FRACBITS + 6;

    private FloorTriangles(PolygonVertex[][] rings, SectorFloor[] bySector, int[] floorSector)
    {
        Rings = rings;
        BySector = bySector;
        FloorSectorOf = floorSector;
    }

    /// <summary>
    /// Per subsector (indexed like <see cref="Level.Subsectors"/>), its polygon
    /// with the T-junction corners inserted, in the polygon's order and
    /// winding (clockwise, starting at the polygon's first corner). Empty when
    /// the polygon is empty or the subsector's floor is hidden.
    /// </summary>
    public IReadOnlyList<PolygonVertex[]> Rings { get; }

    /// <summary>Per sector (indexed like <see cref="Level.Sectors"/>), its floor triangles (possibly none).</summary>
    public IReadOnlyList<SectorFloor> BySector { get; }

    /// <summary>
    /// Per subsector, the sector whose floor its triangles are drawn in, or −1
    /// when it draws none (empty polygon, or hidden by the <c>floorSector</c>
    /// choice passed to <see cref="Build"/>).
    /// </summary>
    public IReadOnlyList<int> FloorSectorOf { get; }

    /// <summary>
    /// Builds the floor triangles of every sector of <paramref name="level"/>.
    /// </summary>
    /// <param name="level">The map.</param>
    /// <param name="polygons">Its subsector polygons (<see cref="SubsectorPolygons.Build"/>).</param>
    /// <param name="floorSector">
    /// Which sector's floor a subsector's polygon is drawn in, or null to hide
    /// it. The default is the subsector's own <see cref="Subsector.Sector"/>,
    /// the floor the sim uses. This is the hook for T2.2a (node artefacts: a
    /// leaf in the void or in another sector), a presentation choice. Hidden
    /// polygons still lend their corners to their neighbours' T-junction pass.
    /// </param>
    public static FloorTriangles Build(Level level, SubsectorPolygons polygons, Func<Subsector, Sector?>? floorSector = null)
    {
        int n = level.Subsectors.Length;
        if (polygons.Polygons.Count != n)
            throw new ArgumentException("The polygons were built for another level.", nameof(polygons));

        // Weld corners that differ only by rounding, so that neighbours share them exactly.
        var grid = new CornerGrid();
        var welded = new PolygonVertex[n][];
        for (int i = 0; i < n; i++)
            welded[i] = Weld(polygons.Polygons[i], grid);

        var rings = new PolygonVertex[n][];
        var sectorOf = new int[n];
        var perSector = new List<int>[level.Sectors.Length];
        for (int i = 0; i < perSector.Length; i++)
            perSector[i] = new List<int>();

        for (int i = 0; i < n; i++)
        {
            PolygonVertex[] polygon = welded[i];
            Subsector ss = level.Subsectors[i];
            Sector? sector = polygon.Length == 0 ? null : floorSector is null ? ss.Sector : floorSector(ss);
            if (sector is null)
            {
                rings[i] = Array.Empty<PolygonVertex>();
                sectorOf[i] = -1;
                continue;
            }
            if (sector.Index < 0 || sector.Index >= level.Sectors.Length || level.Sectors[sector.Index] != sector)
                throw new ArgumentException("floorSector returned a sector of another level.", nameof(floorSector));
            rings[i] = InsertTJunctions(polygon, grid);
            sectorOf[i] = sector.Index;
            perSector[sector.Index].Add(i);
        }

        var bySector = new SectorFloor[level.Sectors.Length];
        var tri = new List<int>();
        for (int s = 0; s < bySector.Length; s++)
        {
            var vertices = new List<PolygonVertex>();
            var index = new Dictionary<PolygonVertex, int>();
            var indices = new List<int>();
            foreach (int ssIndex in perSector[s])
            {
                PolygonVertex[] ring = rings[ssIndex];
                tri.Clear();
                Triangulate(ring, tri);
                foreach (int k in tri)
                {
                    PolygonVertex v = ring[k];
                    if (!index.TryGetValue(v, out int vi))
                    {
                        vi = vertices.Count;
                        index.Add(v, vi);
                        vertices.Add(v);
                    }
                    indices.Add(vi);
                }
            }
            bySector[s] = new SectorFloor(s, perSector[s].ToArray(), vertices.ToArray(), indices.ToArray());
        }
        return new FloorTriangles(rings, bySector, sectorOf);
    }

    /// <summary>
    /// The sector whose floor triangles cover the point (<paramref name="x"/>,
    /// <paramref name="y"/>) (fixed_t; edges count as inside, the first
    /// sector in index order wins on a shared edge), or −1 over the void.
    /// What is drawn under a point, as opposed to <see cref="Level.R_PointInSubsector"/>,
    /// which always finds a subsector, even outside the map (T2.7's overlay
    /// shows both).
    /// </summary>
    public int SectorAt(int x, int y)
    {
        var p = new PolygonVertex(x, y);
        foreach (SectorFloor floor in BySector)
        {
            for (int t = 0; t < floor.TriangleCount; t++)
            {
                PolygonVertex a = floor.Corner(t, 0), b = floor.Corner(t, 1), c = floor.Corner(t, 2);
                if (TwiceArea(a, b, p) >= 0 && TwiceArea(b, c, p) >= 0 && TwiceArea(c, a, p) >= 0)
                    return floor.Sector;
            }
        }
        return -1;
    }

    /// <summary>
    /// Twice the area of a triangle in this class's winding (clockwise, y up),
    /// in fixed_t² (2^32 per square map unit): positive when clockwise.
    /// </summary>
    public static Int128 TwiceArea(PolygonVertex a, PolygonVertex b, PolygonVertex c) =>
        (Int128)(b.Y - (long)a.Y) * (c.X - (long)a.X) - (Int128)(b.X - (long)a.X) * (c.Y - (long)a.Y);

    /// <summary>
    /// True when <paramref name="p"/> lies on the edge <paramref name="a"/>→<paramref name="b"/>
    /// strictly between its ends: within <see cref="OnEdgeEpsilon"/> of its line,
    /// projecting inside the edge, and not within <see cref="EndEpsilon"/> of
    /// either end (a T-junction if <paramref name="p"/> is a corner of a neighbour).
    /// </summary>
    public static bool OnEdge(PolygonVertex a, PolygonVertex b, PolygonVertex p)
    {
        if (Near(p, a) || Near(p, b))
            return false;
        long dx = (long)b.X - a.X, dy = (long)b.Y - a.Y;
        long px = (long)p.X - a.X, py = (long)p.Y - a.Y;
        Int128 along = (Int128)px * dx + (Int128)py * dy;
        if (along <= 0 || along >= (Int128)dx * dx + (Int128)dy * dy)
            return false;
        Int128 cross = (Int128)px * dy - (Int128)py * dx;
        Int128 tol = (Int128)OnEdgeEpsilon * (Math.Abs(dx) + Math.Abs(dy));
        return cross <= tol && cross >= -tol;
    }

    private static bool Near(PolygonVertex a, PolygonVertex b) =>
        Math.Abs((long)a.X - b.X) <= EndEpsilon && Math.Abs((long)a.Y - b.Y) <= EndEpsilon;

    /// <summary>
    /// The polygon with each corner replaced by the first corner (of any
    /// polygon, in subsector order) within <see cref="WeldEpsilon"/> of it, and
    /// repeated corners dropped; empty when less than a positive area is left.
    /// </summary>
    private static PolygonVertex[] Weld(PolygonVertex[] polygon, CornerGrid grid)
    {
        var result = new List<PolygonVertex>(polygon.Length);
        foreach (PolygonVertex v in polygon)
        {
            PolygonVertex w = grid.Snap(v);
            if (result.Count == 0 || result[^1] != w)
                result.Add(w);
        }
        while (result.Count > 1 && result[^1] == result[0])
            result.RemoveAt(result.Count - 1);
        if (result.Count < 3 || SubsectorPolygons.TwiceArea(result.ToArray()) <= 0)
            return Array.Empty<PolygonVertex>();
        return result.ToArray();
    }

    /// <summary>The polygon with every other polygon's corner that lies on one of its edges inserted, in order along the edge.</summary>
    private static PolygonVertex[] InsertTJunctions(PolygonVertex[] polygon, CornerGrid grid)
    {
        var ring = new List<PolygonVertex>(polygon.Length);
        var onEdge = new List<(Int128 Along, PolygonVertex P)>();
        for (int i = 0; i < polygon.Length; i++)
        {
            PolygonVertex a = polygon[i], b = polygon[(i + 1) % polygon.Length];
            ring.Add(a);
            onEdge.Clear();
            grid.Near(a, b, p =>
            {
                if (OnEdge(a, b, p))
                    onEdge.Add((((Int128)p.X - a.X) * ((long)b.X - a.X) + ((Int128)p.Y - a.Y) * ((long)b.Y - a.Y), p));
            });
            // Along the edge; ties (distinct corners at the same projection) by coordinates, so the order is fixed.
            onEdge.Sort((u, v) => u.Along != v.Along ? u.Along.CompareTo(v.Along)
                : u.P.X != v.P.X ? u.P.X.CompareTo(v.P.X) : u.P.Y.CompareTo(v.P.Y));
            foreach ((_, PolygonVertex p) in onEdge)
                ring.Add(p);
        }
        return ring.ToArray();
    }

    /// <summary>
    /// Ear clipping of a clockwise ring whose corners are a convex polygon's
    /// plus extra corners on (or within a rounding error of) its edges. An ear
    /// is a corner whose triangle with its two neighbours is clockwise (the
    /// corner more than <see cref="OnEdgeEpsilon"/> off the diagonal) and holds
    /// no other remaining corner, not even on or within that tolerance of the
    /// diagonal (so no corner is left on a diagonal). The scan starts after the ring's
    /// first corner each time, so a polygon without extra corners becomes a
    /// fan from its first corner. Appends three ring indices per triangle.
    /// </summary>
    private static void Triangulate(PolygonVertex[] ring, List<int> output)
    {
        var rest = new List<int>(ring.Length);
        for (int i = 0; i < ring.Length; i++)
            rest.Add(i);

        while (rest.Count >= 3)
        {
            int ear = -1;
            for (int k = 1; k <= rest.Count && ear < 0; k++)
            {
                int j = k % rest.Count;
                if (IsEar(ring, rest, j))
                    ear = j;
            }
            if (ear < 0)
            {
                // Only zero-area corners left (or a ring that is not simple, which the
                // polygons' tolerances rule out): fan what has area and stop.
                for (int k = 1; k + 1 < rest.Count; k++)
                {
                    if (TwiceArea(ring[rest[0]], ring[rest[k]], ring[rest[k + 1]]) > 0)
                        output.AddRange(new[] { rest[0], rest[k], rest[k + 1] });
                }
                return;
            }
            int prev = rest[(ear + rest.Count - 1) % rest.Count], next = rest[(ear + 1) % rest.Count];
            output.Add(prev);
            output.Add(rest[ear]);
            output.Add(next);
            rest.RemoveAt(ear);
        }
    }

    private static bool IsEar(PolygonVertex[] ring, List<int> rest, int j)
    {
        int pi = rest[(j + rest.Count - 1) % rest.Count], ci = rest[j], ni = rest[(j + 1) % rest.Count];
        PolygonVertex p = ring[pi], c = ring[ci], n = ring[ni];
        // Strictly convex: c further than OnEdgeEpsilon from the diagonal n→p (else the
        // triangle is a sliver whose long edge runs through its own corner).
        if (TwiceArea(p, c, n) <= (Int128)OnEdgeEpsilon * (Math.Abs((long)p.X - n.X) + Math.Abs((long)p.Y - n.Y)))
            return false;
        foreach (int qi in rest)
        {
            if (qi == pi || qi == ci || qi == ni)
                continue;
            PolygonVertex q = ring[qi];
            if (q == p || q == c || q == n)
                return false;
            // Inside or on the clockwise triangle p, c, n (on the right of or on each edge),
            // or on the diagonal within the tolerance: it would be left on the diagonal.
            if ((TwiceArea(p, c, q) >= 0 && TwiceArea(c, n, q) >= 0 && TwiceArea(n, p, q) >= 0) || OnEdge(n, p, q))
                return false;
        }
        return true;
    }

    /// <summary>The distinct (welded) corners of all polygons, bucketed in 64-unit cells.</summary>
    private sealed class CornerGrid
    {
        private readonly Dictionary<(int, int), List<PolygonVertex>> cells = new();

        /// <summary>
        /// The corner already in the grid within <see cref="WeldEpsilon"/> of
        /// <paramref name="v"/> (on both axes; the first one found, cells and
        /// corners in a fixed order), or <paramref name="v"/> itself, added.
        /// </summary>
        public PolygonVertex Snap(PolygonVertex v)
        {
            int cx = v.X >> CellShift, cy = v.Y >> CellShift;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (!cells.TryGetValue((cx + dx, cy + dy), out List<PolygonVertex>? near))
                        continue;
                    foreach (PolygonVertex w in near)
                    {
                        if (Math.Abs((long)w.X - v.X) <= WeldEpsilon && Math.Abs((long)w.Y - v.Y) <= WeldEpsilon)
                            return w;
                    }
                }
            }
            if (!cells.TryGetValue((cx, cy), out List<PolygonVertex>? list))
                cells.Add((cx, cy), list = new List<PolygonVertex>());
            list.Add(v);
            return v;
        }

        /// <summary>Calls <paramref name="visit"/> for every corner in the cells the edge's bounding box (plus the tolerance) touches.</summary>
        public void Near(PolygonVertex a, PolygonVertex b, Action<PolygonVertex> visit)
        {
            int x0 = (int)(Math.Max((long)Math.Min(a.X, b.X) - OnEdgeEpsilon, int.MinValue) >> CellShift);
            int x1 = (int)(Math.Min((long)Math.Max(a.X, b.X) + OnEdgeEpsilon, int.MaxValue) >> CellShift);
            int y0 = (int)(Math.Max((long)Math.Min(a.Y, b.Y) - OnEdgeEpsilon, int.MinValue) >> CellShift);
            int y1 = (int)(Math.Min((long)Math.Max(a.Y, b.Y) + OnEdgeEpsilon, int.MaxValue) >> CellShift);
            for (int cx = x0; cx <= x1; cx++)
            {
                for (int cy = y0; cy <= y1; cy++)
                {
                    if (cells.TryGetValue((cx, cy), out List<PolygonVertex>? list))
                    {
                        foreach (PolygonVertex v in list)
                            visit(v);
                    }
                }
            }
        }
    }
}
