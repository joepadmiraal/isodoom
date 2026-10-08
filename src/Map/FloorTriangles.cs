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

    /// <summary>A point well inside the floor (fixed_t): the centroid of its largest triangle (the first on ties). Throws without triangles.</summary>
    public (int X, int Y) InteriorPoint()
    {
        if (TriangleCount == 0)
            throw new InvalidOperationException($"sector {Sector} has no floor triangles");
        int best = 0;
        Int128 bestArea = -1;
        for (int t = 0; t < TriangleCount; t++)
        {
            Int128 area = FloorTriangles.TwiceArea(Corner(t, 0), Corner(t, 1), Corner(t, 2));
            if (area > bestArea)
                (best, bestArea) = (t, area);
        }
        PolygonVertex a = Corner(best, 0), b = Corner(best, 1), c = Corner(best, 2);
        return ((int)(((long)a.X + b.X + c.X) / 3), (int)(((long)a.Y + b.Y + c.Y) / 3));
    }
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
    /// one corner (1/8 map unit; 1/32 until T2.9). Neighbouring polygons
    /// compute a shared corner separately, each rounded to fixed_t, and
    /// corners where nearly parallel lines cross can differ by a hundredth of
    /// a unit, in Doom II MAP29 (sector 201) by 0.08; welding them first keeps a
    /// near-duplicate from being inserted as a T-junction on both edges at that
    /// corner, or leaving a sliver no triangulation can cut without a corner
    /// on a diagonal (MAP29 once T2.9 added the seg ends). Far above
    /// <see cref="OnEdgeEpsilon"/>, so a corner can lie on at most one edge of
    /// a polygon.
    /// </summary>
    public const int WeldEpsilon = Fixed.FRACUNIT / 8;

    // Grid cells of 64 map units for looking up corners near an edge.
    private const int CellShift = Fixed.FRACBITS + 6;

    private FloorTriangles(PolygonVertex[][] rings, SectorFloor[] bySector, int[] floorSector, PolygonVertex[][] segChains)
    {
        Rings = rings;
        BySector = bySector;
        FloorSectorOf = floorSector;
        SegChains = segChains;
    }

    /// <summary>
    /// Per seg (indexed like <see cref="Level.Segs"/>), the corners of its
    /// subsector's floor edge along it, from the seg's start to its end in
    /// ring order (at least two), or empty when that subsector's polygon has
    /// no edge along the seg (or the seg has no length). T2.9: walls are
    /// built on these, so a wall's bottom edge (and a lower wall's top edge,
    /// against the back floor) has exactly the floor's corners, with no crack
    /// between them. The polygon's edge along a seg can be up to
    /// <see cref="SubsectorPolygons.SegSnapEpsilon"/> off the seg (the seg is
    /// then not clipped against, <see cref="SubsectorPolygons"/>), so each
    /// seg end first goes to the nearest end of that edge when one is within
    /// <see cref="PastEdgeDistance"/> (the floor's corner, where the next wall
    /// starts), else is projected onto the edge (the nearest point, to the
    /// nearest fixed_t, snapped to a corner within <see cref="SegEndSnap"/>)
    /// and added as a corner there, and to every other polygon edge it lies
    /// on, before the T-junction pass. Where a seg end
    /// lies well past its subsector's edge (more than <see cref="PastEdgeDistance"/>:
    /// the node builder left that part of the seg to another subsector's
    /// floor), the chain continues to the end's
    /// projection on the edge's line, through the corners on the way. When the
    /// subsector's floor is hidden (<c>floorSector</c>), the chain is just the
    /// two projected ends.
    /// </summary>
    public IReadOnlyList<PolygonVertex[]> SegChains { get; }

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

        // The seg ends projected onto their subsector's polygon edge along the seg (T2.9).
        var segEnds = new (PolygonVertex Start, PolygonVertex End, PolygonVertex? Before, PolygonVertex? After)?[level.Segs.Length];
        for (int i = 0; i < n; i++)
        {
            Subsector ss = level.Subsectors[i];
            if (welded[i].Length == 0)
                continue;
            for (int k = 0; k < ss.NumLines; k++)
            {
                int segIndex = ss.FirstLine + k;
                if (segIndex < 0 || segIndex >= level.Segs.Length)
                    continue;
                Seg seg = level.Segs[segIndex];
                if (seg.V1.X == seg.V2.X && seg.V1.Y == seg.V2.Y)
                    continue;
                if (ProjectAlongEdges(welded[i], seg, seg.V1.X, seg.V1.Y) is not (PolygonVertex start, PolygonVertex before)
                    || ProjectAlongEdges(welded[i], seg, seg.V2.X, seg.V2.Y) is not (PolygonVertex end, PolygonVertex after))
                    continue;
                start = grid.Snap(start, SegEndSnap);
                end = grid.Snap(end, SegEndSnap);
                // Well past the polygon's edge (another subsector's floor lies along that part of the
                // seg): on to the projection on the edge's line, a corner for the neighbours too. Near
                // the edge's end, the polygon's corner is where this wall meets the next one.
                PolygonVertex? b = FarFrom(seg.V1, start) ? grid.Snap(before, SegEndSnap) : null, a = FarFrom(seg.V2, end) ? grid.Snap(after, SegEndSnap) : null;
                if (start != end)
                    segEnds[segIndex] = (start, end, b == start ? null : b, a == end ? null : a);
            }
        }

        var rings = new PolygonVertex[n][];
        int[] sectorOf = new int[n];
        var perSector = new List<int>[level.Sectors.Length];
        for (int i = 0; i < perSector.Length; i++)
            perSector[i] = [];

        for (int i = 0; i < n; i++)
        {
            PolygonVertex[] polygon = welded[i];
            Subsector ss = level.Subsectors[i];
            Sector? sector = polygon.Length == 0 ? null : floorSector is null ? ss.Sector : floorSector(ss);
            if (sector is null)
            {
                rings[i] = [];
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
            bySector[s] = new SectorFloor(s, [.. perSector[s]], [.. vertices], [.. indices]);
        }
        var segChains = new PolygonVertex[level.Segs.Length][];
        for (int i = 0; i < n; i++)
        {
            Subsector ss = level.Subsectors[i];
            for (int k = 0; k < ss.NumLines; k++)
            {
                int segIndex = ss.FirstLine + k;
                if (segIndex < 0 || segIndex >= segChains.Length || segEnds[segIndex] is not { } ends)
                    continue;
                (PolygonVertex start, PolygonVertex end, PolygonVertex? before, PolygonVertex? after) = ends;
                var chain = new List<PolygonVertex>();
                if (before is PolygonVertex b)
                {
                    chain.Add(b);
                    AddBetween(chain, grid, b, start);
                }
                chain.AddRange(Chain(rings[i], level.Segs[segIndex], start, end));
                if (after is PolygonVertex a)
                {
                    AddBetween(chain, grid, end, a);
                    chain.Add(a);
                }
                segChains[segIndex] = [.. chain];
            }
        }
        for (int i = 0; i < segChains.Length; i++)
            segChains[i] ??= [];
        return new FloorTriangles(rings, bySector, sectorOf, segChains);
    }

    /// <summary>
    /// Whether the polygon edge <paramref name="a"/>→<paramref name="b"/> runs
    /// along <paramref name="seg"/>: in its direction, with both seg ends
    /// within <see cref="SubsectorPolygons.SegSnapEpsilon"/> of its line (the
    /// test with which <see cref="SubsectorPolygons"/> skips clipping by a seg).
    /// </summary>
    private static bool AlongSeg(PolygonVertex a, PolygonVertex b, Seg seg)
    {
        long sdx = (long)seg.V2.X - seg.V1.X, sdy = (long)seg.V2.Y - seg.V1.Y;
        long ex = (long)b.X - a.X, ey = (long)b.Y - a.Y;
        if ((Int128)ex * sdx + (Int128)ey * sdy <= 0)
            return false;
        Int128 tol = (Int128)SubsectorPolygons.SegSnapEpsilon * (Math.Abs(ex) + Math.Abs(ey));
        Int128 s1 = (Int128)((long)seg.V1.X - a.X) * ey - (Int128)((long)seg.V1.Y - a.Y) * ex;
        Int128 s2 = (Int128)((long)seg.V2.X - a.X) * ey - (Int128)((long)seg.V2.Y - a.Y) * ex;
        return s1 <= tol && s1 >= -tol && s2 <= tol && s2 >= -tol;
    }

    /// <summary>Appends the corners (of any polygon) that lie on the edge <paramref name="a"/>→<paramref name="b"/> strictly between its ends, in order along it.</summary>
    private static void AddBetween(List<PolygonVertex> chain, CornerGrid grid, PolygonVertex a, PolygonVertex b)
    {
        var onEdge = new List<(Int128 Along, PolygonVertex P)>();
        grid.Near(a, b, p =>
        {
            if (OnEdge(a, b, p))
                onEdge.Add((((Int128)p.X - a.X) * ((long)b.X - a.X) + ((Int128)p.Y - a.Y) * ((long)b.Y - a.Y), p));
        });
        onEdge.Sort((u, v) => u.Along != v.Along ? u.Along.CompareTo(v.Along)
            : u.P.X != v.P.X ? u.P.X.CompareTo(v.P.X) : u.P.Y.CompareTo(v.P.Y));
        foreach ((_, PolygonVertex p) in onEdge)
            chain.Add(p);
    }

    /// <summary>
    /// Where the wall along <paramref name="seg"/> ends near the seg's end
    /// (<paramref name="x"/>, <paramref name="y"/>) on the polygon's edges along
    /// the seg (<see cref="AlongSeg"/>): the nearest end of such an edge when
    /// one is within <see cref="PastEdgeDistance"/> (a corner of the floor);
    /// else the nearest point on those edges and its projection on that
    /// edge's line (the same point unless it lies past the edge's ends), both
    /// rounded to the nearest fixed_t; null when no edge runs along the seg.
    /// </summary>
    private static (PolygonVertex OnEdge, PolygonVertex OnLine)? ProjectAlongEdges(PolygonVertex[] polygon, Seg seg, int x, int y)
    {
        (PolygonVertex, PolygonVertex)? best = null;
        Int128 bestDistance = Int128.MaxValue;
        PolygonVertex? corner = null;
        Int128 cornerDistance = (Int128)PastEdgeDistance * PastEdgeDistance;
        for (int k = 0; k < polygon.Length; k++)
        {
            PolygonVertex a = polygon[k], b = polygon[(k + 1) % polygon.Length];
            if (!AlongSeg(a, b, seg))
                continue;
            foreach (PolygonVertex c in new[] { a, b })
            {
                Int128 dc = (Int128)((long)c.X - x) * ((long)c.X - x) + (Int128)((long)c.Y - y) * ((long)c.Y - y);
                if (dc <= cornerDistance)
                {
                    cornerDistance = dc;
                    corner = c;
                }
            }
            long ex = (long)b.X - a.X, ey = (long)b.Y - a.Y;
            Int128 length2 = (Int128)ex * ex + (Int128)ey * ey;
            Int128 along = (Int128)((long)x - a.X) * ex + (Int128)((long)y - a.Y) * ey;
            Int128 clamped = along < 0 ? 0 : along > length2 ? length2 : along;
            var p = new PolygonVertex((int)(a.X + RoundDiv(clamped * ex, length2)), (int)(a.Y + RoundDiv(clamped * ey, length2)));
            var q = new PolygonVertex((int)(a.X + RoundDiv(along * ex, length2)), (int)(a.Y + RoundDiv(along * ey, length2)));
            Int128 d = (Int128)((long)p.X - x) * ((long)p.X - x) + (Int128)((long)p.Y - y) * ((long)p.Y - y);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = (p, q);
            }
        }
        // An end of the edge near the seg's end is the polygon's corner where the next wall starts (or
        // the next seg's subsector's floor): end there, so walls meet exactly at the floor's corners.
        return corner is PolygonVertex cv ? (cv, cv) : best;
    }

    /// <summary>
    /// fixed_t: a seg end further than this from the nearest point of its
    /// subsector's edge along the seg lies past that edge's end (on a
    /// neighbour's floor), not at the polygon's corner where the next wall
    /// starts (the edge can be up to <see cref="SubsectorPolygons.SegSnapEpsilon"/>,
    /// measured with the L1 length, off the seg: under 3 units at the corner).
    /// </summary>
    public const int PastEdgeDistance = 3 * Fixed.FRACUNIT;

    /// <summary>
    /// fixed_t: a projected seg end this close (on both axes) to a corner
    /// already there becomes that corner (1/4 map unit), rather than a new
    /// corner a hair away from it (neighbouring polygons can round a shared
    /// corner a tenth of a unit apart, beyond <see cref="WeldEpsilon"/>).
    /// </summary>
    public const int SegEndSnap = Fixed.FRACUNIT / 4;

    private static bool FarFrom(Vertex v, PolygonVertex p)
    {
        long dx = (long)p.X - v.X, dy = (long)p.Y - v.Y;
        return (Int128)dx * dx + (Int128)dy * dy > (Int128)PastEdgeDistance * PastEdgeDistance;
    }

    /// <summary><paramref name="n"/> / <paramref name="d"/> rounded to the nearest integer, halves away from zero (<paramref name="d"/> &gt; 0).</summary>
    private static Int128 RoundDiv(Int128 n, Int128 d) => n >= 0 ? (n + d / 2) / d : -((-n + d / 2) / d);

    /// <summary>
    /// The ring's corners from <paramref name="start"/> to <paramref name="end"/>
    /// (both included, in ring order), when both are corners of the ring and
    /// every corner between them lies along <paramref name="seg"/> (within
    /// <see cref="SubsectorPolygons.SegSnapEpsilon"/> of its line); otherwise
    /// (a hidden floor, or a chain that would leave the seg) just the two ends.
    /// </summary>
    private static PolygonVertex[] Chain(PolygonVertex[] ring, Seg seg, PolygonVertex start, PolygonVertex end)
    {
        int i0 = Array.IndexOf(ring, start), i1 = Array.IndexOf(ring, end);
        if (i0 < 0 || i1 < 0)
            return [start, end];
        long sdx = (long)seg.V2.X - seg.V1.X, sdy = (long)seg.V2.Y - seg.V1.Y;
        Int128 tol = (Int128)SubsectorPolygons.SegSnapEpsilon * (Math.Abs(sdx) + Math.Abs(sdy));
        var chain = new List<PolygonVertex> { start };
        for (int k = (i0 + 1) % ring.Length; k != i1; k = (k + 1) % ring.Length)
        {
            PolygonVertex p = ring[k];
            Int128 cross = (Int128)((long)p.X - seg.V1.X) * sdy - (Int128)((long)p.Y - seg.V1.Y) * sdx;
            if (cross > tol || cross < -tol)
                return [start, end];
            chain.Add(p);
        }
        chain.Add(end);
        return [.. chain];
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
            return [];
        return [.. result];
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
        return [.. ring];
    }

    /// <summary>
    /// Ear clipping of a clockwise ring whose corners are a convex polygon's
    /// plus extra corners on (or within a rounding error of) its edges. An ear
    /// is a corner whose triangle with its two neighbours is clockwise (the
    /// corner more than <see cref="OnEdgeEpsilon"/> off the diagonal) and holds
    /// no other remaining corner, not even on or within that tolerance of the
    /// diagonal (so no corner is left on a diagonal). The scan starts after the ring's
    /// first corner each time, so a polygon without extra corners becomes a
    /// fan from its first corner. When only slivers are left (T2.9), it starts
    /// again with a fan from the first corner that gives no sliver and no
    /// corner on a diagonal, and failing that clips thin ears (any positive
    /// area). Appends three ring indices per triangle.
    /// </summary>
    private static void Triangulate(PolygonVertex[] ring, List<int> output)
    {
        int start = output.Count;
        if (EarClip(ring, output, relaxed: false))
            return;
        // Only slivers left (a polygon that is itself nearly a sliver, e.g. two corners a
        // hair apart next to a run of collinear ones, T2.9): a fan from a corner from which
        // every triangle has area and no corner lies on a diagonal, else thin ears.
        output.RemoveRange(start, output.Count - start);
        for (int k = 0; k < ring.Length; k++)
        {
            if (TryFan(ring, k, output))
                return;
        }
        EarClip(ring, output, relaxed: true);
    }

    /// <summary>
    /// Ear clipping (see <see cref="Triangulate"/>); false when only slivers
    /// are left: then, if <paramref name="relaxed"/>, thin ears (any positive
    /// area), and a fan of what has area when even those run out.
    /// </summary>
    private static bool EarClip(PolygonVertex[] ring, List<int> output, bool relaxed)
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
                if (IsEar(ring, rest, j, sliverTolerance: true))
                    ear = j;
            }
            for (int k = 1; relaxed && k <= rest.Count && ear < 0; k++)
            {
                int j = k % rest.Count;
                if (IsEar(ring, rest, j, sliverTolerance: false))
                    ear = j;
            }
            if (ear < 0)
            {
                if (!relaxed)
                    return false;
                // Only zero-area corners left (or a ring that is not simple, which the
                // polygons' tolerances rule out): fan what has area and stop.
                for (int k = 1; k + 1 < rest.Count; k++)
                {
                    if (TwiceArea(ring[rest[0]], ring[rest[k]], ring[rest[k + 1]]) > 0)
                        output.AddRange([rest[0], rest[k], rest[k + 1]]);
                }
                return true;
            }
            int prev = rest[(ear + rest.Count - 1) % rest.Count], next = rest[(ear + 1) % rest.Count];
            output.Add(prev);
            output.Add(rest[ear]);
            output.Add(next);
            rest.RemoveAt(ear);
        }
        return true;
    }

    /// <summary>
    /// A fan from corner <paramref name="apex"/> when each of its triangles has
    /// every corner more than <see cref="OnEdgeEpsilon"/> (by the L1 length)
    /// off the opposite side and no ring corner lies on a diagonal; false (and
    /// nothing appended) otherwise.
    /// </summary>
    private static bool TryFan(PolygonVertex[] ring, int apex, List<int> output)
    {
        int n = ring.Length;
        PolygonVertex a = ring[apex];
        for (int i = 1; i + 1 < n; i++)
        {
            PolygonVertex b = ring[(apex + i) % n], c = ring[(apex + i + 1) % n];
            long longest = Math.Max(L1(a, b), Math.Max(L1(b, c), L1(c, a)));
            if (TwiceArea(a, b, c) <= (Int128)OnEdgeEpsilon * longest)
                return false;
        }
        for (int i = 2; i + 1 < n; i++)
        {
            PolygonVertex d = ring[(apex + i) % n];
            foreach (PolygonVertex q in ring)
            {
                if (q != a && q != d && OnEdge(a, d, q))
                    return false;
            }
        }
        for (int i = 1; i + 1 < n; i++)
            output.AddRange([apex, (apex + i) % n, (apex + i + 1) % n]);
        return true;
    }

    private static long L1(PolygonVertex a, PolygonVertex b) => Math.Abs((long)a.X - b.X) + Math.Abs((long)a.Y - b.Y);

    private static bool IsEar(PolygonVertex[] ring, List<int> rest, int j, bool sliverTolerance)
    {
        int pi = rest[(j + rest.Count - 1) % rest.Count], ci = rest[j], ni = rest[(j + 1) % rest.Count];
        PolygonVertex p = ring[pi], c = ring[ci], n = ring[ni];
        // Strictly convex: c further than OnEdgeEpsilon from the diagonal n→p (else the
        // triangle is a sliver whose long edge runs through its own corner); without the
        // tolerance, any positive area.
        Int128 area = TwiceArea(p, c, n);
        if (area <= 0 || (sliverTolerance && area <= (Int128)OnEdgeEpsilon * (Math.Abs((long)p.X - n.X) + Math.Abs((long)p.Y - n.Y))))
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
        private readonly Dictionary<(int, int), List<PolygonVertex>> cells = [];

        /// <summary>
        /// The corner already in the grid within <see cref="WeldEpsilon"/> of
        /// <paramref name="v"/> (on both axes; the first one found, cells and
        /// corners in a fixed order), or <paramref name="v"/> itself, added.
        /// </summary>
        public PolygonVertex Snap(PolygonVertex v) => Snap(v, WeldEpsilon);

        /// <summary>As <see cref="Snap(PolygonVertex)"/>, within <paramref name="epsilon"/> (at most the 64-unit cell size).</summary>
        public PolygonVertex Snap(PolygonVertex v, int epsilon)
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
                        if (Math.Abs((long)w.X - v.X) <= epsilon && Math.Abs((long)w.Y - v.Y) <= epsilon)
                            return w;
                    }
                }
            }
            if (!cells.TryGetValue((cx, cy), out List<PolygonVertex>? list))
                cells.Add((cx, cy), list = []);
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
