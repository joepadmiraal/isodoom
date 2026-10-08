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

/// <summary>Which upper walls are drawn (SPEC §12; a presentation option). The shader's <c>upper_walls</c>.</summary>
public enum UpperWallMode
{
    /// <summary>Every upper wall, as vanilla: a step down to a lower ceiling hangs in the air as a band, with no ceiling drawn above it.</summary>
    All = 0,

    /// <summary>Only the upper walls over a sector that keeps them (<see cref="DoorLids.KeepsUppers"/>: doors, lintels, windows, crushers; the default).</summary>
    Doors = 1,
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

    /// <summary>The line specials that open a door on the line's back sector by hand (p_switch.c <c>P_UseSpecialLine</c>'s <c>EV_VerticalDoor</c> cases).</summary>
    private static readonly int[] _manualDoorSpecials = [1, 26, 27, 28, 31, 32, 33, 34, 117, 118];

    /// <summary>The line specials that move the doors of their tagged sectors (p_spec.c, p_switch.c: <c>EV_DoDoor</c>, <c>EV_DoLockedDoor</c>).</summary>
    private static readonly int[] _taggedDoorSpecials =
        [2, 3, 4, 16, 29, 42, 46, 50, 61, 63, 75, 76, 86, 90, 99, 103, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 133, 134, 135, 136, 137];

    /// <summary>The sector specials that are doors (p_spec.c <c>P_SpawnSpecials</c>: 10 <c>P_SpawnDoorCloseIn30</c>, 14 <c>P_SpawnDoorRaiseIn5Mins</c>).</summary>
    private static readonly int[] _doorSectorSpecials = [10, 14];

    /// <summary>The line specials that move the ceilings of their tagged sectors (p_spec.c, p_switch.c: <c>EV_DoCeiling</c>: crushers, ceilings lowered to the floor or raised).</summary>
    private static readonly int[] _ceilingSpecials = [6, 25, 40, 41, 43, 44, 49, 72, 73, 77, 141];

    private readonly int[]?[] _neighbours;
    private readonly bool[] _doors;
    private readonly bool[] _ceilings;

    private DoorLids(int[]?[] neighbours, List<int> sectors, bool[] doors, bool[] ceilings)
    {
        _neighbours = neighbours;
        Sectors = sectors;
        _doors = doors;
        _ceilings = ceilings;
    }

    /// <summary>The lid sectors, in sector order.</summary>
    public IReadOnlyList<int> Sectors { get; }

    /// <summary>Whether <paramref name="sector"/> is a lid sector.</summary>
    public bool Has(int sector) => _neighbours[sector] is not null;

    /// <summary>
    /// Whether <paramref name="sector"/> is a door (T6.13d): a sector a door
    /// special moves, the back sector of a manual door line, a sector tagged
    /// by a door line (tag 0 not counted), or one with a door sector special.
    /// The cutaway keeps doors whole by default (<c>CutawayDoors</c>).
    /// </summary>
    public bool IsDoor(int sector) => _doors[sector];

    /// <summary>
    /// Whether <paramref name="sector"/>'s ceiling moves: a door
    /// (<see cref="IsDoor"/>), or a sector whose ceiling a line special moves
    /// (crushers, ceilings lowered to the floor). The upper walls over it are
    /// kept (<see cref="KeepsUppers"/>): they are the moving block's sides.
    /// </summary>
    public bool MovesCeiling(int sector) => _doors[sector] || _ceilings[sector];

    /// <summary>
    /// Whether the upper walls over <paramref name="sector"/> (those of the
    /// lines around it, on the side whose ceiling is higher) are drawn with
    /// <see cref="UpperWallMode.Doors"/>, its ceiling and its neighbours' at
    /// <paramref name="ceiling"/> (a sector's ceiling height as drawn, map
    /// units): its ceiling moves (<see cref="MovesCeiling"/>), or its lid
    /// shows (<see cref="Shows"/>, the lids on or not: a lintel, a window, a
    /// closed door; its ceiling below all its neighbours', so the upper walls
    /// around it make a closed block). Over any other sector (a room with a
    /// lower ceiling, a step down from a higher one to a lower one) an upper
    /// wall is a band hanging in the air, since no ceiling is drawn: the
    /// shader's test.
    /// </summary>
    public bool KeepsUppers(int sector, Func<int, float> ceiling) =>
        MovesCeiling(sector) || Shows(LidHeight(sector, ceiling), ceiling(sector));

    /// <summary>The door sectors (<see cref="IsDoor"/>) of <paramref name="level"/>.</summary>
    public static bool[] FindDoors(Level level)
    {
        bool[] doors = Tagged(level, _taggedDoorSpecials);
        foreach (Line line in level.Lines)
        {
            if (Array.IndexOf(_manualDoorSpecials, (int)line.Special) >= 0 && line.BackSector is Sector back)
                doors[back.Index] = true;
        }
        foreach (Sector s in level.Sectors)
        {
            if (Array.IndexOf(_doorSectorSpecials, (int)s.Special) >= 0)
                doors[s.Index] = true;
        }
        return doors;
    }

    // The sectors tagged (tag 0 not counted) by a line with one of the specials.
    private static bool[] Tagged(Level level, int[] specials)
    {
        bool[] tagged = new bool[level.Sectors.Length];
        var tags = new HashSet<int>();
        foreach (Line line in level.Lines)
        {
            if (Array.IndexOf(specials, (int)line.Special) >= 0 && line.Tag != 0)
                tags.Add(line.Tag);
        }
        foreach (Sector s in level.Sectors)
        {
            if (tags.Contains(s.Tag))
                tagged[s.Index] = true;
        }
        return tagged;
    }

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
        return new DoorLids(lids, sectors, FindDoors(level), Tagged(level, _ceilingSpecials));
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
