using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Wad;

namespace IsoDoom.Tests.Support;

/// <summary>
/// Builds small collision test maps (T4.4): a <see cref="Strip"/> of
/// rectangular rooms side by side along x (one sector and one subsector per
/// room, a chain of BSP nodes on the room boundaries), or one convex
/// <see cref="Polygon"/> room (one sector, no nodes). Walls are one-sided,
/// room boundaries two-sided; the blockmap lists each line in every block its
/// bounding box touches, after the customary 0. Things are added with
/// <see cref="Thing"/>; <see cref="Load"/> spawns them in a new world.
/// </summary>
public sealed class TestMap
{
    /// <summary>A strip room: <paramref name="Width"/> units wide, with its floor and ceiling heights.</summary>
    public readonly record struct Room(int Width, int Floor, int Ceiling);

    private readonly List<(int X, int Y)> _vertexes = new();
    private readonly List<(int V1, int V2, int Flags, int Special, int Front, int Back)> _lines = new();
    private readonly List<(int Floor, int Ceiling, string CeilingPic)> _sectors = new();
    private readonly List<(int V1, int V2, int Line, int Side)> _segs = new();
    private readonly List<(int Count, int First)> _subsectors = new();
    private readonly List<short> _nodes = new();
    private readonly List<short> _things = new();

    private TestMap()
    {
    }

    /// <summary>The line number of the boundary between room <c>i - 1</c> and room <c>i</c> of a strip.</summary>
    public int[] Boundaries { get; private set; } = Array.Empty<int>();

    /// <summary>
    /// Rooms from x = <paramref name="x0"/> eastwards, all from
    /// y = <paramref name="y0"/> to <paramref name="y1"/>. Lines: for each
    /// room its south wall (westwards) and north wall (eastwards), then the
    /// west and east end walls, then the boundaries (northwards, the east room
    /// in front: <see cref="Boundaries"/>).
    /// </summary>
    public static TestMap Strip(int x0, int y0, int y1, params Room[] rooms)
    {
        var map = new TestMap();
        int n = rooms.Length;
        int[] xs = new int[n + 1];
        xs[0] = x0;
        for (int i = 0; i < n; i++)
            xs[i + 1] = xs[i] + rooms[i].Width;
        // vertexes: bottom row 0..n, top row n+1..2n+1
        for (int i = 0; i <= n; i++)
            map._vertexes.Add((xs[i], y0));
        for (int i = 0; i <= n; i++)
            map._vertexes.Add((xs[i], y1));
        int Bottom(int i) => i;
        int Top(int i) => n + 1 + i;

        foreach (Room r in rooms)
            map._sectors.Add((r.Floor, r.Ceiling, "FLAT"));

        var south = new int[n];
        var north = new int[n];
        for (int i = 0; i < n; i++)
        {
            south[i] = map.AddLine(Bottom(i + 1), Bottom(i), Line.ML_BLOCKING, i, -1);
            north[i] = map.AddLine(Top(i), Top(i + 1), Line.ML_BLOCKING, i, -1);
        }
        int west = map.AddLine(Bottom(0), Top(0), Line.ML_BLOCKING, 0, -1);
        int east = map.AddLine(Top(n), Bottom(n), Line.ML_BLOCKING, n - 1, -1);
        map.Boundaries = new int[n];
        map.Boundaries[0] = -1;
        for (int i = 1; i < n; i++)
            map.Boundaries[i] = map.AddLine(Bottom(i), Top(i), Line.ML_TWOSIDED, i, i - 1);

        // one convex subsector per room, its segs clockwise from the south wall
        for (int i = 0; i < n; i++)
        {
            int first = map._segs.Count;
            map._segs.Add((Bottom(i + 1), Bottom(i), south[i], 0));
            if (i == 0)
                map._segs.Add((Bottom(0), Top(0), west, 0));
            else
                map._segs.Add((Bottom(i), Top(i), map.Boundaries[i], 0));
            map._segs.Add((Top(i), Top(i + 1), north[i], 0));
            if (i == n - 1)
                map._segs.Add((Top(n), Bottom(n), east, 0));
            else
                map._segs.Add((Top(i + 1), Bottom(i + 1), map.Boundaries[i + 1], 1));
            map._subsectors.Add((map._segs.Count - first, first));
        }

        // node for boundary i (northwards: x > X in front, children[0]) at index n - 1 - i:
        // the back child is room i - 1, the front child the node for boundary i + 1 (or the last room)
        for (int i = n - 1; i >= 1; i--)
        {
            int front = i == n - 1 ? 0x8000 | (n - 1) : n - 2 - i;
            int back = 0x8000 | (i - 1);
            map._nodes.AddRange(new short[]
            {
                (short)xs[i], (short)y0, 0, (short)(y1 - y0),
                (short)y1, (short)y0, (short)xs[i], (short)xs[n], // right box: top, bottom, left, right
                (short)y1, (short)y0, (short)xs[0], (short)xs[i], // left box
                unchecked((short)front), unchecked((short)back),
            });
        }
        return map;
    }

    /// <summary>
    /// One convex room (clockwise <paramref name="corners"/>, so each wall's
    /// front faces in): one sector, one subsector, no nodes. Line i runs
    /// from corner i to corner i + 1.
    /// </summary>
    public static TestMap Polygon(int floor, int ceiling, params (int X, int Y)[] corners)
    {
        var map = new TestMap();
        map._sectors.Add((floor, ceiling, "FLAT"));
        foreach (var c in corners)
            map._vertexes.Add(c);
        for (int i = 0; i < corners.Length; i++)
        {
            int line = map.AddLine(i, (i + 1) % corners.Length, Line.ML_BLOCKING, 0, -1);
            map._segs.Add((i, (i + 1) % corners.Length, line, 0));
        }
        map._subsectors.Add((corners.Length, 0));
        return map;
    }

    private int AddLine(int v1, int v2, int flags, int front, int back)
    {
        _lines.Add((v1, v2, flags, 0, front, back));
        return _lines.Count - 1;
    }

    /// <summary>Sets a line's special.</summary>
    public TestMap Special(int line, int special)
    {
        var l = _lines[line];
        _lines[line] = l with { Special = special };
        return this;
    }

    /// <summary>Sets a line's flags.</summary>
    public TestMap Flags(int line, int flags)
    {
        var l = _lines[line];
        _lines[line] = l with { Flags = flags };
        return this;
    }

    /// <summary>Sets a sector's ceiling flat (e.g. <c>F_SKY1</c>).</summary>
    public TestMap CeilingPic(int sector, string pic)
    {
        _sectors[sector] = _sectors[sector] with { CeilingPic = pic };
        return this;
    }

    /// <summary>Adds a map thing (doomednum <paramref name="type"/>, all skills).</summary>
    public TestMap Thing(int x, int y, int type, int angle = 0, int flags = 7)
    {
        _things.AddRange(new[] { (short)x, (short)y, (short)angle, (short)type, (short)flags });
        return this;
    }

    /// <summary>Adds player 1's start.</summary>
    public TestMap Player(int x, int y, int angle = 0) => Thing(x, y, 1, angle);

    /// <summary>The map as a PWAD with the map lump <c>E1M1</c>.</summary>
    public byte[] Build()
    {
        var linedefs = new List<short>();
        var sides = new List<(int Sector, int Line)>();
        foreach (var l in _lines)
        {
            int right = sides.Count;
            sides.Add((l.Front, 0));
            int left = -1;
            if (l.Back >= 0)
            {
                left = sides.Count;
                sides.Add((l.Back, 0));
            }
            linedefs.AddRange(new[] { (short)l.V1, (short)l.V2, (short)l.Flags, (short)l.Special, (short)0, (short)right, (short)left });
        }

        byte[] sidedefs = new byte[30 * sides.Count];
        for (int s = 0; s < sides.Count; s++)
        {
            Encoding.ASCII.GetBytes("-", sidedefs.AsSpan(30 * s + 4));
            Encoding.ASCII.GetBytes("-", sidedefs.AsSpan(30 * s + 12));
            Encoding.ASCII.GetBytes("-", sidedefs.AsSpan(30 * s + 20));
            BinaryPrimitives.WriteInt16LittleEndian(sidedefs.AsSpan(30 * s + 28), (short)sides[s].Sector);
        }

        byte[] sectors = new byte[26 * _sectors.Count];
        for (int i = 0; i < _sectors.Count; i++)
        {
            Span<byte> sec = sectors.AsSpan(26 * i, 26);
            BinaryPrimitives.WriteInt16LittleEndian(sec, (short)_sectors[i].Floor);
            BinaryPrimitives.WriteInt16LittleEndian(sec[2..], (short)_sectors[i].Ceiling);
            Encoding.ASCII.GetBytes("FLAT", sec[4..]);
            Encoding.ASCII.GetBytes(_sectors[i].CeilingPic, sec[12..]);
            BinaryPrimitives.WriteInt16LittleEndian(sec[20..], 160);
        }

        var vertexes = new List<short>();
        foreach (var v in _vertexes)
        {
            vertexes.Add((short)v.X);
            vertexes.Add((short)v.Y);
        }

        var segs = new List<short>();
        foreach (var s in _segs)
        {
            var (x1, y1) = _vertexes[s.V1];
            var (x2, y2) = _vertexes[s.V2];
            // BAM >> 16 of the seg's direction (only used for drawing)
            int angle = (int)(Math.Atan2(y2 - y1, x2 - x1) / (2 * Math.PI) * 65536) & 0xffff;
            segs.AddRange(new[] { (short)s.V1, (short)s.V2, unchecked((short)angle), (short)s.Line, (short)s.Side, (short)0 });
        }

        var ssectors = new List<short>();
        foreach (var ss in _subsectors)
        {
            ssectors.Add((short)ss.Count);
            ssectors.Add((short)ss.First);
        }

        return new WadBuilder()
            .Markers("E1M1")
            .Lump("THINGS", Shorts(_things))
            .Lump("LINEDEFS", Shorts(linedefs))
            .Lump("SIDEDEFS", sidedefs)
            .Lump("VERTEXES", Shorts(vertexes))
            .Lump("SEGS", Shorts(segs))
            .Lump("SSECTORS", Shorts(ssectors))
            .Lump("NODES", Shorts(_nodes))
            .Lump("SECTORS", sectors)
            .Lump("REJECT", new byte[(_sectors.Count * _sectors.Count + 7) / 8])
            .Lump("BLOCKMAP", BlockmapLump())
            .Build();
    }

    /// <summary>Loads the map in a new world (vanilla, no tweaks) and spawns its things.</summary>
    public World Load(skill_t skill = skill_t.sk_medium)
    {
        var wad = new WadArchive(new[] { WadFile.FromBytes(Build(), "testmap.wad") });
        var world = new World(new SpawnSettings(GameMode.shareware, skill), Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(wad, "E1M1"));
        return world;
    }

    private byte[] BlockmapLump()
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var v in _vertexes)
        {
            minX = Math.Min(minX, v.X);
            minY = Math.Min(minY, v.Y);
            maxX = Math.Max(maxX, v.X);
            maxY = Math.Max(maxY, v.Y);
        }
        int orgX = minX - 64, orgY = minY - 64;
        int width = (maxX - orgX) / 128 + 1, height = (maxY - orgY) / 128 + 1;

        var offsets = new List<short> { (short)orgX, (short)orgY, (short)width, (short)height };
        var lists = new List<short>();
        int listStart = 4 + width * height;
        for (int by = 0; by < height; by++)
        {
            for (int bx = 0; bx < width; bx++)
            {
                offsets.Add((short)(listStart + lists.Count));
                int left = orgX + bx * 128, bottom = orgY + by * 128;
                lists.Add(0);
                for (int i = 0; i < _lines.Count; i++)
                {
                    var (x1, y1) = _vertexes[_lines[i].V1];
                    var (x2, y2) = _vertexes[_lines[i].V2];
                    if (Math.Max(x1, x2) >= left && Math.Min(x1, x2) <= left + 128 && Math.Max(y1, y2) >= bottom && Math.Min(y1, y2) <= bottom + 128)
                        lists.Add((short)i);
                }
                lists.Add(-1);
            }
        }
        offsets.AddRange(lists);
        return Shorts(offsets);
    }

    private static byte[] Shorts(IReadOnlyList<short> values)
    {
        byte[] data = new byte[values.Count * 2];
        for (int i = 0; i < values.Count; i++)
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 2), values[i]);
        return data;
    }
}
