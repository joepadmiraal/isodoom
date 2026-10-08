using System;
using System.Collections.Generic;

namespace IsoDoom.Map;

/// <summary>A polygon corner in map space (fixed_t).</summary>
public readonly record struct PolygonVertex(int X, int Y)
{
    public override string ToString() => $"({Units(X)}, {Units(Y)})";

    private static string Units(int v) =>
        (v & (Fixed.FRACUNIT - 1)) == 0 ? $"{v >> Fixed.FRACBITS}" : $"{v >> Fixed.FRACBITS}+{v & (Fixed.FRACUNIT - 1)}/65536";
}

/// <summary>
/// The floor/ceiling polygon of every subsector, grouped per sector (SPEC §7.2).
/// <para>
/// Vanilla nodes store no subsector shapes: a subsector is the convex region
/// its BSP leaf cuts out of the plane, and its segs cover only the edges that
/// lie on linedefs. <see cref="Build"/> recovers each shape as source ports do
/// for vanilla nodes: it starts from a box around the map, splits it along
/// every node's partition line on the way down the tree (front part to child
/// 0, back part to child 1), and at each leaf clips the cell to the front
/// side of each of the subsector's segs, which cuts away the void behind
/// one-sided walls. A seg that runs along an edge the cell already has is
/// skipped (<see cref="AlongEdge"/>), so rounded split vertices leave no gaps.
/// </para>
/// <para>
/// The polygons follow the node tree, so they agree with
/// <see cref="Level.R_PointInSubsector"/> and the floor drawn is the floor the
/// sim stands things on. Where the node builder's leaves are wrong, so are the
/// polygons: in DOOM1 v1.9, E1M3 subsector 44 covers void behind a wall and
/// E1M6 subsector 227 covers part of another sector (SPEC §12, T2.2).
/// </para>
/// <para>
/// Everything is integer arithmetic: side tests are exact (<see cref="Int128"/>
/// products of fixed_t values), and a new corner where an edge crosses a line
/// is rounded to the nearest fixed_t, so the result is the same on every
/// machine and the assembly stays under the determinism scan (SPEC §12).
/// </para>
/// </summary>
public sealed class SubsectorPolygons
{
    /// <summary>How far (map units) the starting box reaches past the outermost vertexes.</summary>
    public const int BoxMargin = 64;

    /// <summary>
    /// fixed_t: a corner this close to a clip line counts as on it, so the line
    /// does not shave a sliver off next to it (1/256 map unit).
    /// </summary>
    public const int OnLineEpsilon = Fixed.FRACUNIT / 256;

    /// <summary>
    /// fixed_t: a seg whose ends are both this close to the line of an edge the
    /// cell already has (in the same direction) does not clip it (2 map units;
    /// see <see cref="AlongEdge"/>).
    /// </summary>
    public const int SegSnapEpsilon = 2 * Fixed.FRACUNIT;

    /// <summary>fixed_t: consecutive corners this close (on both axes) merge into one (1/1024 map unit).</summary>
    public const int MergeEpsilon = Fixed.FRACUNIT / 1024;

    private SubsectorPolygons(PolygonVertex[][] polygons, int[][] bySector)
    {
        Polygons = polygons;
        BySector = bySector;
    }

    /// <summary>
    /// One convex polygon per subsector (indexed like <see cref="Level.Subsectors"/>),
    /// clockwise in map coordinates (y up), i.e. with the interior on the right
    /// of each edge as for segs. No two consecutive corners are within
    /// <see cref="MergeEpsilon"/> of each other. Empty
    /// when the clipping leaves no area (degenerate subsectors).
    /// </summary>
    public IReadOnlyList<PolygonVertex[]> Polygons { get; }

    /// <summary>
    /// Per sector (indexed like <see cref="Level.Sectors"/>), the numbers of the
    /// subsectors with a non-empty polygon whose <see cref="Subsector.Sector"/>
    /// it is, in subsector order.
    /// </summary>
    public IReadOnlyList<int[]> BySector { get; }

    /// <summary>Builds the polygons of every subsector of <paramref name="level"/>.</summary>
    public static SubsectorPolygons Build(Level level)
    {
        var polygons = new PolygonVertex[level.Subsectors.Length][];
        for (int i = 0; i < polygons.Length; i++)
            polygons[i] = [];

        if (level.Subsectors.Length > 0)
        {
            List<P> box = StartBox(level);
            if (level.Nodes.Length == 0)
                Leaf(level, level.Subsectors[0], box, polygons);
            else
                Walk(level, level.Nodes.Length - 1, box, polygons);
        }

        var bySector = new List<int>[level.Sectors.Length];
        for (int i = 0; i < bySector.Length; i++)
            bySector[i] = [];
        foreach (Subsector ss in level.Subsectors)
        {
            if (polygons[ss.Index].Length > 0)
                bySector[ss.Sector.Index].Add(ss.Index);
        }
        int[][] grouped = new int[bySector.Length][];
        for (int i = 0; i < grouped.Length; i++)
            grouped[i] = [.. bySector[i]];
        return new SubsectorPolygons(polygons, grouped);
    }

    /// <summary>
    /// Twice the area of a polygon in this class's winding (clockwise), in
    /// fixed_t² (2^32 per square map unit): positive for a clockwise polygon.
    /// </summary>
    public static Int128 TwiceArea(ReadOnlySpan<PolygonVertex> polygon)
    {
        Int128 sum = 0;
        for (int i = 0; i < polygon.Length; i++)
        {
            PolygonVertex a = polygon[i];
            PolygonVertex b = polygon[(i + 1) % polygon.Length];
            sum += (Int128)a.X * b.Y - (Int128)b.X * a.Y;
        }
        return -sum;
    }

    // A working corner; long so the starting box may reach past the fixed_t range.
    private readonly record struct P(long X, long Y);

    private static List<P> StartBox(Level level)
    {
        long minX = long.MaxValue, minY = long.MaxValue, maxX = long.MinValue, maxY = long.MinValue;
        foreach (Vertex v in level.Vertexes)
        {
            minX = Math.Min(minX, v.X);
            minY = Math.Min(minY, v.Y);
            maxX = Math.Max(maxX, v.X);
            maxY = Math.Max(maxY, v.Y);
        }
        if (level.Vertexes.Length == 0)
            minX = minY = maxX = maxY = 0;
        long m = (long)BoxMargin << Fixed.FRACBITS;
        minX = Math.Max(minX - m, int.MinValue);
        minY = Math.Max(minY - m, int.MinValue);
        maxX = Math.Min(maxX + m, int.MaxValue);
        maxY = Math.Min(maxY + m, int.MaxValue);
        // Clockwise with y up: top-left, top-right, bottom-right, bottom-left.
        return [new(minX, maxY), new(maxX, maxY), new(maxX, minY), new(minX, minY)];
    }

    private static void Walk(Level level, int nodenum, List<P> cell, PolygonVertex[][] polygons)
    {
        if ((nodenum & Node.NF_SUBSECTOR) != 0)
        {
            Leaf(level, level.Subsectors[nodenum & ~Node.NF_SUBSECTOR], cell, polygons);
            return;
        }
        // Level.Load checks that the tree reaches every node once, so this recursion ends.
        Node node = level.Nodes[nodenum];
        Walk(level, node.Children[0], Clip(cell, node.X, node.Y, node.Dx, node.Dy, keepFront: true), polygons);
        Walk(level, node.Children[1], Clip(cell, node.X, node.Y, node.Dx, node.Dy, keepFront: false), polygons);
    }

    private static void Leaf(Level level, Subsector ss, List<P> cell, PolygonVertex[][] polygons)
    {
        for (int i = 0; i < ss.NumLines && cell.Count > 0; i++)
        {
            Seg seg = level.Segs[ss.FirstLine + i];
            long dx = (long)seg.V2.X - seg.V1.X, dy = (long)seg.V2.Y - seg.V1.Y;
            if ((dx != 0 || dy != 0) && !AlongEdge(cell, seg))
                cell = Clip(cell, seg.V1.X, seg.V1.Y, dx, dy, keepFront: true);
        }
        if (cell.Count < 3)
            return;

        var result = new PolygonVertex[cell.Count];
        for (int i = 0; i < cell.Count; i++)
            result[i] = new PolygonVertex((int)cell[i].X, (int)cell[i].Y);
        if (TwiceArea(result) > 0)
            polygons[ss.Index] = result;
    }

    /// <summary>
    /// True when <paramref name="seg"/> runs along an edge the cell already has
    /// (same direction, both ends within <see cref="SegSnapEpsilon"/> of the
    /// edge's line). Node builders round split vertices to whole units, so a
    /// seg and the partition line cut along the same linedef may differ by a
    /// unit or so. Clipping by the seg's own line would then shave a long
    /// wedge off the cell past the seg's end, where the neighbouring cell
    /// stops at the partition: a gap in the floor.
    /// </summary>
    private static bool AlongEdge(List<P> cell, Seg seg)
    {
        long sdx = (long)seg.V2.X - seg.V1.X, sdy = (long)seg.V2.Y - seg.V1.Y;
        for (int k = 0; k < cell.Count; k++)
        {
            P a = cell[k], b = cell[(k + 1) % cell.Count];
            long ex = b.X - a.X, ey = b.Y - a.Y;
            if ((Int128)ex * sdx + (Int128)ey * sdy <= 0)
                continue;
            Int128 tol = (Int128)SegSnapEpsilon * (Math.Abs(ex) + Math.Abs(ey));
            Int128 s1 = (Int128)(seg.V1.X - a.X) * ey - (Int128)(seg.V1.Y - a.Y) * ex;
            Int128 s2 = (Int128)(seg.V2.X - a.X) * ey - (Int128)(seg.V2.Y - a.Y) * ex;
            if (s1 <= tol && s1 >= -tol && s2 <= tol && s2 >= -tol)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Sutherland–Hodgman against one line: keeps the part of a convex polygon
    /// on the line's front (right) side, or its back (left) side. Corners on
    /// the line belong to both.
    /// </summary>
    private static List<P> Clip(List<P> poly, long lx, long ly, long ldx, long ldy, bool keepFront)
    {
        var result = new List<P>(poly.Count + 1);
        if (poly.Count == 0)
            return result;

        // Positive on the front (right) side.
        // A corner within OnLineEpsilon of the line (measured with the L1 length of
        // the line's direction, so between 1/√2 and 1 times that) counts as on it.
        Int128 onLine = (Int128)OnLineEpsilon * (Math.Abs(ldx) + Math.Abs(ldy));
        Int128 Side(P p)
        {
            Int128 s = (Int128)(p.X - lx) * ldy - (Int128)(p.Y - ly) * ldx;
            if (s <= onLine && s >= -onLine)
                return 0;
            return keepFront ? s : -s;
        }

        P prev = poly[^1];
        Int128 sPrev = Side(prev);
        foreach (P cur in poly)
        {
            Int128 sCur = Side(cur);
            if ((sPrev > 0 && sCur < 0) || (sPrev < 0 && sCur > 0))
                Add(result, Intersect(prev, cur, sPrev, sCur));
            if (sCur >= 0)
                Add(result, cur);
            prev = cur;
            sPrev = sCur;
        }
        if (result.Count > 1 && Near(result[^1], result[0]))
            result.RemoveAt(result.Count - 1);
        if (result.Count < 3)
            result.Clear();
        return result;
    }

    private static void Add(List<P> poly, P p)
    {
        if (poly.Count == 0 || !Near(poly[^1], p))
            poly.Add(p);
    }

    private static bool Near(P a, P b) => Math.Abs(a.X - b.X) <= MergeEpsilon && Math.Abs(a.Y - b.Y) <= MergeEpsilon;

    // The point where a→b crosses the line, given the side values of a and b (opposite signs).
    private static P Intersect(P a, P b, Int128 sa, Int128 sb)
    {
        Int128 den = sa - sb;
        return new P(
            a.X + (long)RoundDiv((Int128)(b.X - a.X) * sa, den),
            a.Y + (long)RoundDiv((Int128)(b.Y - a.Y) * sa, den));
    }

    // n / d rounded to the nearest integer, halves away from zero.
    private static Int128 RoundDiv(Int128 n, Int128 d)
    {
        if (d < 0)
        {
            n = -n;
            d = -d;
        }
        return n >= 0 ? (n + d / 2) / d : -((-n + d / 2) / d);
    }
}
