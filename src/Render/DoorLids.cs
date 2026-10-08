using System;
using System.Collections.Generic;
using IsoDoom.Map;

namespace IsoDoom.Render;

/// <summary>Whether lids are drawn on the solid above thin low-ceilinged sectors (T6.13b, SPEC §12; a presentation option). The shader's <c>lids</c>.</summary>
public enum DoorLidMode
{
    /// <summary>No lids: ceilings are never drawn (SPEC §7.2 before T6.13b), so a closed door is two upper walls with a slot between them.</summary>
    Off = 0,

    /// <summary>Lids on (the default).</summary>
    On = 1,
}

/// <summary>
/// The lids (T6.13b, SPEC §7.2, §12): which sectors get a lid on the solid
/// volume above them, and at what height. Pure C#, no Godot types (the tests
/// link it); <c>LevelMesh</c> builds the lid triangles from it.
/// <para>
/// A <b>lid sector</b> is a sector whose ceiling is not the sky, that has a
/// floor and at least one other sector across a two-sided line (its
/// <see cref="Neighbours"/>), and that is at most <see cref="MaxThickness"/>
/// thick (twice the radius of the largest disc inside its floor,
/// <see cref="Thickness"/>), or <see cref="MaxDoorThickness"/> when it is
/// closed at load (its ceiling at or below its floor: a door): doors, the
/// lintels over doorways, windows and thin low passages, but not a room with
/// a low ceiling, whose lid would be a ceiling hiding what is in it. Its lid is its floor polygons at the
/// <b>lowest ceiling of its neighbours</b> (<see cref="LidHeight"/>), drawn
/// while its own ceiling is below that: the height up to which every upper
/// wall around it reaches, so the lid closes the box they make. A door that
/// opens raises its ceiling to 4 below that height (p_doors.c), and the lid
/// stays as the lintel's top.
/// </para>
/// </summary>
public sealed class DoorLids
{
    /// <summary>
    /// The thickest sector that gets a lid, map units (twice the radius of the
    /// largest disc inside its floor). 32 is two player radii: nothing can
    /// stand wholly under a lid that thin. It takes most doors of DOOM1 and
    /// Doom II (8–32 thick), the lintels over doorways and windows.
    /// </summary>
    public const int MaxThickness = 32;

    /// <summary>
    /// The thickest sector closed at load (a door) that gets a lid, map units:
    /// Doom II's 64-thick doors (MAP17, MAP27). Thicker ones are closets and
    /// rooms that open (a lid over them would hide what comes out).
    /// </summary>
    public const int MaxDoorThickness = 64;

    /// <summary>A lid height meaning none (the sector is not a lid sector): below every ceiling.</summary>
    public const float None = -1e9f;

    /// <summary>The sample grid's step when measuring a sector's thickness, map units.</summary>
    private const double SampleStep = 1;

    private readonly int[]?[] _neighbours;

    private DoorLids(int[]?[] neighbours, List<int> sectors)
    {
        _neighbours = neighbours;
        Sectors = sectors;
    }

    /// <summary>The lid sectors, in sector order.</summary>
    public IReadOnlyList<int> Sectors { get; }

    /// <summary>Whether <paramref name="sector"/> is a lid sector.</summary>
    public bool Has(int sector) => _neighbours[sector] is not null;

    /// <summary>The other sectors across a lid sector's two-sided lines, in sector order (empty for a sector without a lid).</summary>
    public IReadOnlyList<int> Neighbours(int sector) => _neighbours[sector] ?? [];

    /// <summary>
    /// A lid sector's lid height from its neighbours' ceilings
    /// (<paramref name="ceiling"/>: a sector's ceiling height as drawn, map
    /// units): the lowest; <see cref="None"/> for a sector without a lid.
    /// </summary>
    public float LidHeight(int sector, Func<int, float> ceiling)
    {
        int[]? neighbours = _neighbours[sector];
        if (neighbours is null)
            return None;
        float lowest = float.MaxValue;
        foreach (int n in neighbours)
            lowest = Math.Min(lowest, ceiling(n));
        return lowest;
    }

    /// <summary>Whether a lid at <paramref name="lid"/> is drawn over a sector whose ceiling is at <paramref name="ceiling"/> (map units): the shader's test.</summary>
    public static bool Shows(float lid, float ceiling) => ceiling < lid;

    /// <summary>Finds the lid sectors of <paramref name="level"/> (<paramref name="floors"/>: its floor triangles).</summary>
    public static DoorLids Build(Level level, FloorTriangles floors)
    {
        var neighbours = new SortedSet<int>?[level.Sectors.Length];
        foreach (Line line in level.Lines)
        {
            if (line.FrontSector is not Sector front || line.BackSector is not Sector back || front == back)
                continue;
            (neighbours[front.Index] ??= []).Add(back.Index);
            (neighbours[back.Index] ??= []).Add(front.Index);
        }
        int[]?[] lids = new int[]?[level.Sectors.Length];
        var sectors = new List<int>();
        foreach (SectorFloor floor in floors.BySector)
        {
            Sector s = level.Sectors[floor.Sector];
            int limit = s.CeilingHeight <= s.FloorHeight ? MaxDoorThickness : MaxThickness;
            if (floor.TriangleCount == 0 || neighbours[s.Index] is not { } n || s.CeilingPic == WallSections.SKYFLATNAME
                || Thickness(level, floor, limit) > limit)
                continue;
            lids[s.Index] = new int[n.Count];
            n.CopyTo(lids[s.Index]!);
        }
        for (int i = 0; i < lids.Length; i++)
        {
            if (lids[i] is not null)
                sectors.Add(i);
        }
        return new DoorLids(lids, sectors);
    }

    /// <summary>
    /// A sector's thickness, map units: twice the largest distance from a
    /// point of its floor to its boundary (the lines with it on one side
    /// only), sampled on a 1-unit grid and at each triangle's centroid. Stops
    /// early once it is past <paramref name="limit"/> (then it returns more
    /// than <paramref name="limit"/>, not the thickness).
    /// </summary>
    public static double Thickness(Level level, SectorFloor floor, double limit = double.MaxValue)
    {
        var boundary = new List<(double Ax, double Ay, double Bx, double By)>();
        foreach (Line line in level.Sectors[floor.Sector].Lines)
        {
            if (line.FrontSector == line.BackSector)
                continue;
            boundary.Add((line.V1.X / 65536.0, line.V1.Y / 65536.0, line.V2.X / 65536.0, line.V2.Y / 65536.0));
        }
        if (boundary.Count == 0)
            return double.MaxValue;
        double radius = 0;
        bool Sample(double x, double y)
        {
            double nearest = double.MaxValue;
            foreach ((double ax, double ay, double bx, double by) in boundary)
                nearest = Math.Min(nearest, PointSegment(x, y, ax, ay, bx, by));
            radius = Math.Max(radius, nearest);
            return 2 * radius > limit;
        }
        for (int t = 0; t < floor.TriangleCount; t++)
        {
            (double ax, double ay) = Units(floor.Corner(t, 0));
            (double bx, double by) = Units(floor.Corner(t, 1));
            (double cx, double cy) = Units(floor.Corner(t, 2));
            if (Sample((ax + bx + cx) / 3, (ay + by + cy) / 3))
                return 2 * radius;
        }
        for (int t = 0; t < floor.TriangleCount; t++)
        {
            (double ax, double ay) = Units(floor.Corner(t, 0));
            (double bx, double by) = Units(floor.Corner(t, 1));
            (double cx, double cy) = Units(floor.Corner(t, 2));
            double left = Math.Floor(Math.Min(ax, Math.Min(bx, cx))), right = Math.Max(ax, Math.Max(bx, cx));
            double bottom = Math.Floor(Math.Min(ay, Math.Min(by, cy))), top = Math.Max(ay, Math.Max(by, cy));
            for (double y = bottom + SampleStep / 2; y < top; y += SampleStep)
            {
                for (double x = left + SampleStep / 2; x < right; x += SampleStep)
                {
                    if (InTriangle(ax, ay, bx, by, cx, cy, x, y) && Sample(x, y))
                        return 2 * radius;
                }
            }
        }
        return 2 * radius;
    }

    private static (double X, double Y) Units(PolygonVertex v) => (v.X / 65536.0, v.Y / 65536.0);

    private static bool InTriangle(double ax, double ay, double bx, double by, double cx, double cy, double x, double y)
    {
        double d1 = (bx - ax) * (y - ay) - (by - ay) * (x - ax);
        double d2 = (cx - bx) * (y - by) - (cy - by) * (x - bx);
        double d3 = (ax - cx) * (y - cy) - (ay - cy) * (x - cx);
        return (d1 >= 0 && d2 >= 0 && d3 >= 0) || (d1 <= 0 && d2 <= 0 && d3 <= 0);
    }

    private static double PointSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay, len = dx * dx + dy * dy;
        double t = len == 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len, 0, 1);
        double ex = ax + t * dx - px, ey = ay + t * dy - py;
        return Math.Sqrt(ex * ex + ey * ey);
    }
}
