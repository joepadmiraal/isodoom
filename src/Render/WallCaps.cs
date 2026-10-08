using System;
using System.Collections.Generic;
using IsoDoom.Map;

namespace IsoDoom.Render;

/// <summary>Whether the one-sided walls get a top (T6.13e, SPEC §12; a presentation option). The shader's <c>wall_caps</c>.</summary>
public enum WallCapMode
{
    /// <summary>No caps: a wall is a sheet with nothing behind it (before T6.13e), the void black.</summary>
    Off = 0,

    /// <summary>Caps on (the default).</summary>
    On = 1,
}

/// <summary>
/// One one-sided wall's cap (T6.13e): the pieces of the strip behind it, in the
/// void, and what places and textures them. Map units, y up.
/// </summary>
/// <param name="Line">The wall's line.</param>
/// <param name="Side">Its front sidedef's number (the cap shows its middle texture).</param>
/// <param name="Sector">Its front sector (the cap's height and light).</param>
/// <param name="X">The line's start (<c>V1</c>), map units.</param>
/// <param name="Y">The line's start (<c>V1</c>), map units.</param>
/// <param name="Dx">The unit direction from <c>V1</c> to <c>V2</c>.</param>
/// <param name="Dy">The unit direction from <c>V1</c> to <c>V2</c>.</param>
/// <param name="Pieces">Convex polygons, clockwise (interior on the right of each edge), in the void within <see cref="WallCaps.Thickness"/> behind the wall.</param>
public sealed record WallCap(int Line, int Side, int Sector, double X, double Y, double Dx, double Dy, IReadOnlyList<(double X, double Y)[]> Pieces)
{
    /// <summary>The unit normal into the void (the line's left), map axes.</summary>
    public (double X, double Y) Normal => (-Dy, Dx);

    /// <summary>How far <paramref name="x"/>, <paramref name="y"/> lies along the wall from its start (the texture column without the sidedef's offset).</summary>
    public double Along(double x, double y) => (x - X) * Dx + (y - Y) * Dy;

    /// <summary>How far <paramref name="x"/>, <paramref name="y"/> lies behind the wall, into the void (the texture row).</summary>
    public double Behind(double x, double y) => -(x - X) * Dy + (y - Y) * Dx;
}

/// <summary>
/// The wall caps (T6.13e, SPEC §7.2, §12): a top on every drawn one-sided wall,
/// a strip <see cref="Thickness"/> units deep into the void behind it, so walls
/// read as solid blocks rather than sheets with black behind them. Pure C#, no
/// Godot types (the tests link it); <c>LevelMesh</c> builds the cap triangles
/// from it and the shader places them at the wall's top.
/// <para>
/// <b>Shape:</b> the strip behind each wall, cut at each end where the void's
/// boundary turns to the next wall (the next one-sided line around the void,
/// whatever its sector) along the line where both are equally far behind
/// their walls: a mitre, so the strips of a corner meet without a gap
/// (reaching past the wall's end by at most <see cref="MitreLimit"/> times the
/// thickness) or an overlap. A wall end with no next wall (or a straight one)
/// is cut square. The strip is then clipped to the void
/// (<see cref="SubsectorPolygons.Voids"/>), so a cap never covers a floor:
/// two rooms closer than the thickness get thinner caps. Where the caps of
/// two walls that are not neighbours still overlap (back to back across a
/// thin void), each keeps the part nearer its own wall.
/// </para>
/// </summary>
public sealed class WallCaps
{
    /// <summary>How deep a cap reaches into the void behind its wall, map units.</summary>
    public const double Thickness = 16;

    /// <summary>How far past a wall's end its cap may reach at a corner, in thicknesses (a sharp corner's mitre stops there).</summary>
    public const double MitreLimit = 4;

    // A clipped vertex this close to a halfplane's line counts as on it (map units).
    private const double Epsilon = 1e-6;

    // Two directions closer than this (the sine of the angle between them) are parallel.
    private const double Parallel = 1e-3;

    // Pieces smaller than this (square map units) are dropped.
    private const double MinArea = 1e-3;

    // The size of the void lookup grid's cells, map units.
    private const double GridSize = 256;

    private WallCaps(List<WallCap> caps) => Caps = caps;

    /// <summary>The caps, in line order (only walls with a piece left).</summary>
    public IReadOnlyList<WallCap> Caps { get; }

    /// <summary>Whether a cap is drawn for a wall seen with its front towards <paramref name="toCameraX"/>, <paramref name="toCameraY"/> (map axes, the direction towards the camera flattened): the shader's test. A wall facing away (whose own quad is culled) gets none, which would float over its room.</summary>
    public static bool Faces(WallCap cap, double toCameraX, double toCameraY) => -(toCameraX * cap.Normal.X + toCameraY * cap.Normal.Y) >= -Parallel;

    /// <summary>Whether <paramref name="line"/> gets a cap: one-sided (as segs are: without <c>ML_TWOSIDED</c>), with a front sector and a middle texture.</summary>
    public static bool HasCap(Level level, Line line) =>
        line.FrontSector is not null && (line.Flags & Line.ML_TWOSIDED) == 0 && line.SideNum[0] >= 0 && level.Sides[line.SideNum[0]].MidTexture != "-"
        && (line.Dx != 0 || line.Dy != 0);

    /// <summary>Builds the caps of <paramref name="level"/> (<paramref name="polygons"/>: its subsector polygons and void).</summary>
    public static WallCaps Build(Level level, SubsectorPolygons polygons, double thickness = Thickness)
    {
        var walls = new List<Wall>();
        var starts = new Dictionary<(int X, int Y), List<int>>();
        var ends = new Dictionary<(int X, int Y), List<int>>();
        foreach (Line line in level.Lines)
        {
            if (!HasCap(level, line))
                continue;
            var w = new Wall(line, level.Sides[line.SideNum[0]]);
            int i = walls.Count;
            walls.Add(w);
            Add(starts, (line.V1.X, line.V1.Y), i);
            Add(ends, (line.V2.X, line.V2.Y), i);
        }

        var voids = new VoidGrid(polygons.Voids);
        var pieces = new List<(double X, double Y)[]>[walls.Count];
        var neighbours = new HashSet<(int, int)>();
        for (int i = 0; i < walls.Count; i++)
        {
            Wall a = walls[i];
            double e = thickness * MitreLimit;
            // The strip, reaching past both ends by the mitre limit.
            List<(double X, double Y)> strip =
            [
                a.Point(-e, 0), a.Point(a.Length + e, 0), a.Point(a.Length + e, thickness), a.Point(-e, thickness),
            ];
            // The end at V2: the next wall around the void, the void on its left too.
            int next = Next(walls, starts, a, (a.Line.V2.X, a.Line.V2.Y));
            strip = Clip(strip, EndPlane(a, next < 0 ? null : walls[next], a.X2, a.Y2, -1));
            int prev = Previous(walls, ends, a, (a.Line.V1.X, a.Line.V1.Y));
            strip = Clip(strip, EndPlane(a, prev < 0 ? null : walls[prev], a.X1, a.Y1, 1));
            if (next >= 0)
                neighbours.Add((Math.Min(i, next), Math.Max(i, next)));
            if (prev >= 0)
                neighbours.Add((Math.Min(i, prev), Math.Max(i, prev)));
            pieces[i] = [];
            if (strip.Count < 3)
                continue;
            foreach ((double X, double Y)[] solid in voids.Overlapping(Bounds(strip)))
            {
                List<(double X, double Y)> piece = strip;
                for (int k = 0; k < solid.Length && piece.Count >= 3; k++)
                {
                    (double ax, double ay) = solid[k];
                    (double bx, double by) = solid[(k + 1) % solid.Length];
                    // Clockwise: the interior is on the right of each edge.
                    piece = Clip(piece, (by - ay, ax - bx, -((by - ay) * ax + (ax - bx) * ay)));
                }
                if (piece.Count >= 3 && Math.Abs(Area(piece)) > MinArea)
                    pieces[i].Add([.. piece]);
            }
        }

        // Caps of walls that are not neighbours and still overlap (back to back across a
        // thin void): each keeps the part nearer its own wall.
        var boxes = new (double MinX, double MinY, double MaxX, double MaxY)[walls.Count];
        for (int i = 0; i < walls.Count; i++)
            boxes[i] = Bounds(pieces[i]);
        for (int i = 0; i < walls.Count; i++)
        {
            for (int j = i + 1; j < walls.Count; j++)
            {
                if (pieces[i].Count == 0 || pieces[j].Count == 0 || !Overlap(boxes[i], boxes[j]) || neighbours.Contains((i, j)))
                    continue;
                Wall a = walls[i], b = walls[j];
                if (a.Nx * b.Nx + a.Ny * b.Ny > 0.5 || !Overlaps(pieces[i], pieces[j]))
                    continue;
                // Behind a minus behind b: a keeps where it is not more.
                (double X, double Y, double C) nearerA = (b.Nx - a.Nx, b.Ny - a.Ny, -(b.Nx * b.X1 + b.Ny * b.Y1) + (a.Nx * a.X1 + a.Ny * a.Y1));
                pieces[i] = ClipAll(pieces[i], nearerA);
                pieces[j] = ClipAll(pieces[j], (-nearerA.X, -nearerA.Y, -nearerA.C));
                boxes[i] = Bounds(pieces[i]);
                boxes[j] = Bounds(pieces[j]);
            }
        }

        var caps = new List<WallCap>();
        for (int i = 0; i < walls.Count; i++)
        {
            Wall w = walls[i];
            if (pieces[i].Count == 0)
                continue;
            var clockwise = new List<(double X, double Y)[]>();
            foreach ((double X, double Y)[] p in pieces[i])
            {
                if (Area(p) > 0)
                    Array.Reverse(p);
                clockwise.Add(p);
            }
            caps.Add(new WallCap(w.Line.Index, w.Side.Index, w.Line.FrontSector!.Index, w.X1, w.Y1, w.Dx, w.Dy, clockwise));
        }
        return new WallCaps(caps);
    }

    private static void Add(Dictionary<(int X, int Y), List<int>> map, (int X, int Y) key, int wall)
    {
        if (!map.TryGetValue(key, out List<int>? list))
            map[key] = list = [];
        list.Add(wall);
    }

    // The next wall around the void from a's end: of the walls starting there, the first
    // met turning clockwise from the way back along a (the void is on a's left).
    private static int Next(List<Wall> walls, Dictionary<(int X, int Y), List<int>> starts, Wall a, (int X, int Y) at)
    {
        if (!starts.TryGetValue(at, out List<int>? candidates))
            return -1;
        int best = -1;
        double bestAngle = double.MaxValue;
        foreach (int c in candidates)
        {
            Wall b = walls[c];
            if (b == a)
                continue;
            double angle = Clockwise(-a.Dx, -a.Dy, b.Dx, b.Dy);
            if (angle < bestAngle)
                (best, bestAngle) = (c, angle);
        }
        return best;
    }

    // The previous wall around the void into a's start: of the walls ending there, the
    // first met turning counterclockwise from a's direction.
    private static int Previous(List<Wall> walls, Dictionary<(int X, int Y), List<int>> ends, Wall a, (int X, int Y) at)
    {
        if (!ends.TryGetValue(at, out List<int>? candidates))
            return -1;
        int best = -1;
        double bestAngle = double.MaxValue;
        foreach (int c in candidates)
        {
            Wall b = walls[c];
            if (b == a)
                continue;
            double angle = Clockwise(-b.Dx, -b.Dy, a.Dx, a.Dy);
            if (angle < bestAngle)
                (best, bestAngle) = (c, angle);
        }
        return best;
    }

    // The clockwise angle from direction u to direction v, in (0, 2π].
    private static double Clockwise(double ux, double uy, double vx, double vy)
    {
        double angle = -Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
        return angle <= 1e-9 ? angle + 2 * Math.PI : angle;
    }

    // The halfplane that ends a's cap at its end (x, y), towards = -1 at V2 (a lies before
    // it), 1 at V1: along the line where a and the neighbouring wall b are equally far
    // behind their walls (the mitre), or square without b or when b is parallel.
    private static (double X, double Y, double C) EndPlane(Wall a, Wall? b, double x, double y, int towards)
    {
        double dot = b is null ? 0 : b.Nx * a.Dx + b.Ny * a.Dy;
        if (b is null || Math.Abs(dot) < Parallel)
            return (towards * a.Dx, towards * a.Dy, -towards * (a.Dx * x + a.Dy * y));
        // g(p) = (na - nb)·(p - end) is zero where both are equally far behind; keep the
        // side of a point on a just inside the end, end + towards·ε·d, where g = towards·ε·(na - nb)·d.
        double gx = a.Nx - b.Nx, gy = a.Ny - b.Ny;
        double sign = Math.Sign(towards * (gx * a.Dx + gy * a.Dy));
        return (sign * gx, sign * gy, -sign * (gx * x + gy * y));
    }

    // Keeps the part of a convex polygon where h.X·x + h.Y·y + h.C ≥ 0.
    private static List<(double X, double Y)> Clip(List<(double X, double Y)> poly, (double X, double Y, double C) h)
    {
        var result = new List<(double X, double Y)>(poly.Count + 1);
        if (poly.Count == 0)
            return result;
        double scale = Math.Sqrt(h.X * h.X + h.Y * h.Y);
        if (scale == 0)
            return h.C >= 0 ? poly : result;
        double Side((double X, double Y) p) => (h.X * p.X + h.Y * p.Y + h.C) / scale;
        (double X, double Y) prev = poly[^1];
        double sPrev = Side(prev);
        foreach ((double X, double Y) cur in poly)
        {
            double sCur = Side(cur);
            if ((sPrev > Epsilon && sCur < -Epsilon) || (sPrev < -Epsilon && sCur > Epsilon))
            {
                double t = sPrev / (sPrev - sCur);
                result.Add((prev.X + (cur.X - prev.X) * t, prev.Y + (cur.Y - prev.Y) * t));
            }
            if (sCur >= -Epsilon)
                result.Add(cur);
            prev = cur;
            sPrev = sCur;
        }
        return result.Count >= 3 ? result : [];
    }

    private static List<(double X, double Y)[]> ClipAll(List<(double X, double Y)[]> pieces, (double X, double Y, double C) h)
    {
        var result = new List<(double X, double Y)[]>();
        foreach ((double X, double Y)[] p in pieces)
        {
            List<(double X, double Y)> clipped = Clip([.. p], h);
            if (clipped.Count >= 3 && Math.Abs(Area(clipped)) > MinArea)
                result.Add([.. clipped]);
        }
        return result;
    }

    // Whether two sets of convex pieces share any area.
    private static bool Overlaps(List<(double X, double Y)[]> a, List<(double X, double Y)[]> b)
    {
        foreach ((double X, double Y)[] p in a)
        {
            foreach ((double X, double Y)[] q in b)
            {
                if (!Overlap(Bounds(p), Bounds(q)))
                    continue;
                List<(double X, double Y)> r = [.. p];
                double sign = Area(q) > 0 ? 1 : -1; // counterclockwise: the interior on the left
                for (int k = 0; k < q.Length && r.Count >= 3; k++)
                {
                    (double ax, double ay) = q[k];
                    (double bx, double by) = q[(k + 1) % q.Length];
                    r = Clip(r, (sign * (ay - by), sign * (bx - ax), -sign * ((ay - by) * ax + (bx - ax) * ay)));
                }
                if (r.Count >= 3 && Math.Abs(Area(r)) > MinArea)
                    return true;
            }
        }
        return false;
    }

    // Twice the signed area... halved: positive counterclockwise (y up).
    private static double Area(IReadOnlyList<(double X, double Y)> poly)
    {
        double sum = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            (double ax, double ay) = poly[i];
            (double bx, double by) = poly[(i + 1) % poly.Count];
            sum += ax * by - bx * ay;
        }
        return sum / 2;
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) Bounds(IReadOnlyList<(double X, double Y)> poly)
    {
        (double minX, double minY, double maxX, double maxY) = (double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
        foreach ((double x, double y) in poly)
            (minX, minY, maxX, maxY) = (Math.Min(minX, x), Math.Min(minY, y), Math.Max(maxX, x), Math.Max(maxY, y));
        return (minX, minY, maxX, maxY);
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) Bounds(List<(double X, double Y)[]> pieces)
    {
        (double minX, double minY, double maxX, double maxY) = (double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
        foreach ((double X, double Y)[] p in pieces)
        {
            (double x0, double y0, double x1, double y1) = Bounds(p);
            (minX, minY, maxX, maxY) = (Math.Min(minX, x0), Math.Min(minY, y0), Math.Max(maxX, x1), Math.Max(maxY, y1));
        }
        return (minX, minY, maxX, maxY);
    }

    private static bool Overlap((double MinX, double MinY, double MaxX, double MaxY) a, (double MinX, double MinY, double MaxX, double MaxY) b) =>
        a.MinX < b.MaxX && b.MinX < a.MaxX && a.MinY < b.MaxY && b.MinY < a.MaxY;

    // A capped wall in map units: its ends, unit direction and normal into the void (its left).
    private sealed class Wall(Line line, Side side)
    {
        public readonly Line Line = line;
        public readonly Side Side = side;
        public readonly double X1 = line.V1.X / 65536.0, Y1 = line.V1.Y / 65536.0, X2 = line.V2.X / 65536.0, Y2 = line.V2.Y / 65536.0;
        public double Length => Math.Sqrt((X2 - X1) * (X2 - X1) + (Y2 - Y1) * (Y2 - Y1));
        public double Dx => (X2 - X1) / Length;
        public double Dy => (Y2 - Y1) / Length;
        public double Nx => -Dy;
        public double Ny => Dx;

        // The point along the wall from V1 and behind it.
        public (double X, double Y) Point(double along, double behind) => (X1 + along * Dx + behind * Nx, Y1 + along * Dy + behind * Ny);
    }

    // The void's pieces by grid cell, for finding those near a strip.
    private sealed class VoidGrid
    {
        private readonly List<((double X, double Y)[] Poly, (double MinX, double MinY, double MaxX, double MaxY) Box)> _pieces = [];
        private readonly Dictionary<(int X, int Y), List<int>> _cells = [];

        public VoidGrid(IReadOnlyList<PolygonVertex[]> voids)
        {
            foreach (PolygonVertex[] v in voids)
            {
                var poly = new (double X, double Y)[v.Length];
                for (int i = 0; i < v.Length; i++)
                    poly[i] = (v[i].X / 65536.0, v[i].Y / 65536.0);
                var box = Bounds(poly);
                int index = _pieces.Count;
                _pieces.Add((poly, box));
                foreach ((int X, int Y) cell in Cells(box))
                {
                    if (!_cells.TryGetValue(cell, out List<int>? list))
                        _cells[cell] = list = [];
                    list.Add(index);
                }
            }
        }

        // The pieces whose box overlaps box, each once, in the void's order.
        public IEnumerable<(double X, double Y)[]> Overlapping((double MinX, double MinY, double MaxX, double MaxY) box)
        {
            var found = new SortedSet<int>();
            foreach ((int X, int Y) cell in Cells(box))
            {
                if (!_cells.TryGetValue(cell, out List<int>? list))
                    continue;
                foreach (int i in list)
                {
                    if (Overlap(_pieces[i].Box, box))
                        found.Add(i);
                }
            }
            foreach (int i in found)
                yield return _pieces[i].Poly;
        }

        private static IEnumerable<(int X, int Y)> Cells((double MinX, double MinY, double MaxX, double MaxY) box)
        {
            for (int y = (int)Math.Floor(box.MinY / GridSize); y <= (int)Math.Floor(box.MaxY / GridSize); y++)
            {
                for (int x = (int)Math.Floor(box.MinX / GridSize); x <= (int)Math.Floor(box.MaxX / GridSize); x++)
                    yield return (x, y);
            }
        }
    }
}
