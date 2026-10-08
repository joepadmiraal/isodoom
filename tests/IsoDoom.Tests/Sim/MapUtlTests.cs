using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T4.3: p_maputl.c's distances, side tests, intercepts, line openings,
/// blockmap iterators and path traversal, on the synthetic E1M1, a hand-built
/// grid map with lines and things on block boundaries and a line through a
/// block corner, and DOOM1 E1M1.
/// </summary>
public class MapUtlTests
{
    private const int FRACUNIT = 1 << 16;
    private static readonly SpawnSettings Medium = new(GameMode.shareware, skill_t.sk_medium);

    private static World Synthetic()
    {
        var wad = new WadArchive([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);
        return Load(wad, "E1M1");
    }

    private static World Load(WadArchive wad, string map)
    {
        var world = new World(Medium, Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(wad, map));
        return world;
    }

    private static int F(int units) => units * FRACUNIT;

    private static int[] Box(int left, int right, int bottom, int top)
    {
        int[] box = new int[4];
        box[BBox.BOXLEFT] = F(left);
        box[BBox.BOXRIGHT] = F(right);
        box[BBox.BOXBOTTOM] = F(bottom);
        box[BBox.BOXTOP] = F(top);
        return box;
    }

    /// <summary>Runs a traversal and returns what it visited, in order, with the fractions.</summary>
    private static (bool Result, List<(object What, int Frac)> Visited) Traverse(World world, int x1, int y1, int x2, int y2, int flags)
    {
        var visited = new List<(object, int)>();
        bool result = world.P_PathTraverse(F(x1), F(y1), F(x2), F(y2), flags, @in =>
        {
            visited.Add((@in.isaline ? @in.line! : @in.thing!, @in.frac));
            return true;
        });
        return (result, visited);
    }

    private static void AssertFrac(double expected, int frac) =>
        Assert.True(Math.Abs(frac - expected * FRACUNIT) <= FRACUNIT / 256.0, $"frac {frac} ({frac / (double)FRACUNIT:F4}), expected {expected:F4}");

    // ---- the grid map ----

    // A 512×512 room (one sector, one subsector, no nodes) with a 5×5 blockmap from (-64, -64), so the block
    // boundaries are at -64, 64, 192, 320, 448 and 576 on both axes:
    //   L0–L3: the walls (x = 0, y = 512, x = 512, y = 0), one-sided.
    //   L4: (448, 64) → (448, 320), two-sided, on a block boundary along its length, ending on block corners.
    //   L5: (128, 128) → (256, 256), two-sided, positive slope, through the block corner (192, 192).
    //   L6: (80, 432) → (176, 336), two-sided, negative slope, inside block (1, 3).
    // The blockmap lists every line in each block its bounding box touches (edges included), after the
    // customary 0, so L4 is in both columns 3 and 4, and L5 in blocks (1, 2) and (2, 1), which it only
    // touches at the corner.
    private const int GridOrg = -64, GridBlocks = 5;

    private static readonly short[] GridVertexes =
    [
        0, 0, 0, 512, 512, 512, 512, 0, // v0-v3: the room
        448, 64, 448, 320,              // v4, v5: L4
        128, 128, 256, 256,             // v6, v7: L5
        80, 432, 176, 336,              // v8, v9: L6
    ];

    // v1, v2, flags, special, tag, right side, left side
    private static readonly short[,] GridLinedefs =
    {
        { 0, 1, 1, 0, 0, 0, -1 },
        { 1, 2, 1, 0, 0, 1, -1 },
        { 2, 3, 1, 0, 0, 2, -1 },
        { 3, 0, 1, 0, 0, 3, -1 },
        { 4, 5, Line.ML_TWOSIDED, 0, 0, 4, 5 },
        { 6, 7, Line.ML_TWOSIDED, 0, 0, 6, 7 },
        { 8, 9, Line.ML_TWOSIDED, 0, 0, 8, 9 },
    };

    private static byte[] Shorts(params short[] values)
    {
        byte[] data = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 2), values[i]);
        return data;
    }

    private static byte[] GridBlockmap()
    {
        var offsets = new List<short> { GridOrg, GridOrg, GridBlocks, GridBlocks };
        var lists = new List<short>();
        int listStart = 4 + GridBlocks * GridBlocks;
        for (int by = 0; by < GridBlocks; by++)
        {
            for (int bx = 0; bx < GridBlocks; bx++)
            {
                offsets.Add((short)(listStart + lists.Count));
                int left = GridOrg + bx * 128, bottom = GridOrg + by * 128;
                lists.Add(0);
                for (int i = 0; i < GridLinedefs.GetLength(0); i++)
                {
                    int v1 = GridLinedefs[i, 0], v2 = GridLinedefs[i, 1];
                    int x1 = GridVertexes[2 * v1], y1 = GridVertexes[2 * v1 + 1], x2 = GridVertexes[2 * v2], y2 = GridVertexes[2 * v2 + 1];
                    if (Math.Max(x1, x2) >= left && Math.Min(x1, x2) <= left + 128 && Math.Max(y1, y2) >= bottom && Math.Min(y1, y2) <= bottom + 128)
                        lists.Add((short)i);
                }
                lists.Add(-1);
            }
        }
        offsets.AddRange(lists);
        return Shorts([.. offsets]);
    }

    private static World Grid()
    {
        var linedefs = new List<short>();
        for (int i = 0; i < GridLinedefs.GetLength(0); i++)
        {
            for (int j = 0; j < GridLinedefs.GetLength(1); j++)
                linedefs.Add(GridLinedefs[i, j]);
        }
        byte[] sides = new byte[30 * 10];
        for (int s = 0; s < 10; s++)
        {
            Encoding.ASCII.GetBytes("-", sides.AsSpan(30 * s + 4));
            Encoding.ASCII.GetBytes("-", sides.AsSpan(30 * s + 12));
            Encoding.ASCII.GetBytes("-", sides.AsSpan(30 * s + 20));
        }
        byte[] sector = new byte[26];
        BinaryPrimitives.WriteInt16LittleEndian(sector.AsSpan(2), 128);
        Encoding.ASCII.GetBytes("FLAT", sector.AsSpan(4));
        Encoding.ASCII.GetBytes("FLAT", sector.AsSpan(12));
        BinaryPrimitives.WriteInt16LittleEndian(sector.AsSpan(20), 160);

        byte[] wad = new WadBuilder()
            .Markers("MAP01")
            .Lump("THINGS", [])
            .Lump("LINEDEFS", Shorts([.. linedefs]))
            .Lump("SIDEDEFS", sides)
            .Lump("VERTEXES", Shorts(GridVertexes))
            .Lump("SEGS", Shorts(0, 1, 0x4000, 0, 0, 0, 1, 2, 0, 1, 0, 0, 2, 3, unchecked((short)0xC000), 2, 0, 0, 3, 0, unchecked((short)0x8000), 3, 0, 0))
            .Lump("SSECTORS", Shorts(4, 0))
            .Lump("NODES", [])
            .Lump("SECTORS", sector)
            .Lump("REJECT", new byte[1])
            .Lump("BLOCKMAP", GridBlockmap())
            .Build();
        return Load(new WadArchive([WadFile.FromBytes(wad, "grid.wad")]), "MAP01");
    }

    // ---- P_AproxDistance ----

    [Theory]
    [InlineData(3, 4, 5.5)]
    [InlineData(-4, -3, 5.5)]
    [InlineData(4, 0, 4)]
    [InlineData(0, -7, 7)]
    [InlineData(10, 10, 15)]
    [InlineData(0, 0, 0)]
    public void AproxDistanceIsLargerPlusHalfSmaller(int dx, int dy, double expected)
    {
        Assert.Equal((int)(expected * FRACUNIT), World.P_AproxDistance(F(dx), F(dy)));
    }

    [Fact]
    public void AproxDistanceRoundsTheHalfDown()
    {
        Assert.Equal(3 + 1 - 0, World.P_AproxDistance(3, 1)); // fixed_t units: 1 >> 1 == 0
        Assert.Equal(1 + 3 - 0, World.P_AproxDistance(-1, 3));
    }

    // ---- side tests ----

    [Fact]
    public void PointOnLineSideOnAxisAlignedLines()
    {
        World w = Synthetic();
        line_t l0 = w.lines[0]; // (-128, 128) → (128, 128): east, front (right) is south
        line_t l2 = w.lines[2]; // (128, -128) → (-128, -128): west, front is north
        line_t l1 = w.lines[1]; // (128, 128) → (128, -128): south, front is west
        line_t l3 = w.lines[3]; // (-128, -128) → (-128, 128): north, front is east
        Assert.Equal(SlopeType.ST_HORIZONTAL, l0.slopetype);
        Assert.Equal(SlopeType.ST_VERTICAL, l1.slopetype);

        Assert.Equal(0, World.P_PointOnLineSide(0, 0, l0));
        Assert.Equal(1, World.P_PointOnLineSide(0, F(200), l0));
        Assert.Equal(0, World.P_PointOnLineSide(F(1000), 0, l0)); // the side of the infinite line
        Assert.Equal(0, World.P_PointOnLineSide(0, F(128), l0)); // on the line: y <= v1.y, dx > 0 → front
        Assert.Equal(0, World.P_PointOnLineSide(0, 0, l2));
        Assert.Equal(1, World.P_PointOnLineSide(0, F(-200), l2));
        Assert.Equal(1, World.P_PointOnLineSide(0, F(-128), l2)); // on the line: dx < 0 → back

        Assert.Equal(0, World.P_PointOnLineSide(0, 0, l1));
        Assert.Equal(1, World.P_PointOnLineSide(F(200), 0, l1));
        Assert.Equal(0, World.P_PointOnLineSide(F(128), 0, l1)); // on the line: x <= v1.x, dy < 0 → front
        Assert.Equal(0, World.P_PointOnLineSide(0, 0, l3));
        Assert.Equal(1, World.P_PointOnLineSide(F(-200), 0, l3));
        Assert.Equal(1, World.P_PointOnLineSide(F(-128), 0, l3)); // on the line: dy > 0 → back
    }

    [Fact]
    public void PointOnLineSideOnDiagonalLines()
    {
        World w = Grid();
        line_t l5 = w.lines[5]; // (128, 128) → (256, 256): north-east, front is south-east
        line_t l6 = w.lines[6]; // (80, 432) → (176, 336): south-east, front is south-west
        Assert.Equal(SlopeType.ST_POSITIVE, l5.slopetype);
        Assert.Equal(SlopeType.ST_NEGATIVE, l6.slopetype);

        Assert.Equal(0, World.P_PointOnLineSide(F(200), F(100), l5));
        Assert.Equal(1, World.P_PointOnLineSide(F(100), F(200), l5));
        Assert.Equal(1, World.P_PointOnLineSide(F(192), F(192), l5)); // on the line → back
        Assert.Equal(0, World.P_PointOnLineSide(F(193), F(192), l5)); // one unit off
        Assert.Equal(0, World.P_PointOnLineSide(F(1000), F(-1000), l5)); // beyond the ends
        Assert.Equal(0, World.P_PointOnLineSide(F(100), F(380), l6));
        Assert.Equal(1, World.P_PointOnLineSide(F(160), F(420), l6));
        Assert.Equal(1, World.P_PointOnLineSide(F(128), F(384), l6)); // on the line → back
    }

    [Fact]
    public void PointOnDivlineSideMatchesLineSideAndUsesTheSignShortcut()
    {
        World w = Grid();
        foreach (line_t ld in w.lines)
        {
            World.P_MakeDivline(ld, out divline_t dl);
            Assert.Equal((ld.v1.X, ld.v1.Y, ld.dx, ld.dy), (dl.x, dl.y, dl.dx, dl.dy));
            for (int x = -64; x <= 576; x += 37)
            {
                for (int y = -64; y <= 576; y += 41)
                    Assert.Equal(World.P_PointOnLineSide(F(x), F(y), ld), World.P_PointOnDivlineSide(F(x), F(y), dl));
            }
        }

        // Axis-aligned divlines: a point on the line goes by the direction, as P_PointOnLineSide.
        var north = new divline_t(0, 0, 0, F(10));
        Assert.Equal(1, World.P_PointOnDivlineSide(0, F(5), north));
        Assert.Equal(0, World.P_PointOnDivlineSide(1, F(5), north));
        var west = new divline_t(0, 0, F(-10), 0);
        Assert.Equal(1, World.P_PointOnDivlineSide(F(5), 0, west));
        Assert.Equal(0, World.P_PointOnDivlineSide(F(5), 1, west));

        // The sign shortcut decides points in the quadrants the line does not pass through.
        var ne = new divline_t(0, 0, F(1), F(1));
        Assert.Equal(0, World.P_PointOnDivlineSide(F(5), F(-5), ne));
        Assert.Equal(1, World.P_PointOnDivlineSide(F(-5), F(5), ne));
        // Each factor drops 8 bits: along a divline 1 unit long, a point under 1 unit off it reads as on it (back).
        Assert.Equal(1, World.P_PointOnDivlineSide(F(100) + FRACUNIT - 1, F(100), ne));
        Assert.Equal(0, World.P_PointOnDivlineSide(F(100) + FRACUNIT, F(100), ne));
    }

    [Fact]
    public void BoxOnLineSideOnEverySlopeType()
    {
        World s = Synthetic();
        line_t l0 = s.lines[0], l1 = s.lines[1], l2 = s.lines[2];
        Assert.Equal(0, World.P_BoxOnLineSide(Box(0, 10, -20, 0), l0));
        Assert.Equal(1, World.P_BoxOnLineSide(Box(0, 10, 200, 210), l0));
        Assert.Equal(-1, World.P_BoxOnLineSide(Box(0, 10, 120, 140), l0));
        Assert.Equal(0, World.P_BoxOnLineSide(Box(0, 10, 100, 128), l0)); // the top on the line: still front
        Assert.Equal(-1, World.P_BoxOnLineSide(Box(0, 10, 128, 140), l0)); // the bottom on it: crossed
        Assert.Equal(0, World.P_BoxOnLineSide(Box(0, 10, 0, 10), l2)); // dx < 0 flips the sides
        Assert.Equal(1, World.P_BoxOnLineSide(Box(0, 10, -210, -200), l2));

        Assert.Equal(0, World.P_BoxOnLineSide(Box(-10, 10, 0, 10), l1)); // dy < 0: front is west
        Assert.Equal(1, World.P_BoxOnLineSide(Box(200, 210, 0, 10), l1));
        Assert.Equal(-1, World.P_BoxOnLineSide(Box(120, 140, 0, 10), l1));
        Assert.Equal(-1, World.P_BoxOnLineSide(Box(100, 128, 0, 10), l1)); // the right edge on the line: crossed
        Assert.Equal(1, World.P_BoxOnLineSide(Box(128, 140, 0, 10), l1)); // the left edge on it: back

        World g = Grid();
        line_t l5 = g.lines[5], l6 = g.lines[6];
        Assert.Equal(1, World.P_BoxOnLineSide(Box(100, 120, 200, 220), l5));
        Assert.Equal(0, World.P_BoxOnLineSide(Box(200, 220, 100, 120), l5));
        Assert.Equal(-1, World.P_BoxOnLineSide(Box(180, 200, 180, 200), l5));
        Assert.Equal(-1, World.P_BoxOnLineSide(Box(500, 520, 500, 520), l5)); // the infinite line, past the end
        Assert.Equal(0, World.P_BoxOnLineSide(Box(0, 20, 300, 320), l6));
        Assert.Equal(1, World.P_BoxOnLineSide(Box(200, 220, 450, 470), l6));
        Assert.Equal(-1, World.P_BoxOnLineSide(Box(120, 140, 380, 400), l6));
        // A diagonal line's own bounding box straddles it.
        Assert.Equal(-1, World.P_BoxOnLineSide(l5.bbox, l5));
        Assert.Equal(-1, World.P_BoxOnLineSide(l6.bbox, l6));
    }

    // ---- P_InterceptVector ----

    [Fact]
    public void InterceptVectorGivesTheFractionAlongTheFirstLine()
    {
        var trace = new divline_t(0, 0, F(256), 0);
        Assert.Equal(FRACUNIT / 2, World.P_InterceptVector(trace, new divline_t(F(128), F(-64), 0, F(128))));
        Assert.Equal(FRACUNIT / 4, World.P_InterceptVector(trace, new divline_t(F(64), F(64), 0, F(-128))));
        Assert.Equal(-FRACUNIT / 4, World.P_InterceptVector(trace, new divline_t(F(-64), F(64), 0, F(-128))));
        Assert.Equal(2 * FRACUNIT, World.P_InterceptVector(trace, new divline_t(F(512), 0, 0, F(1)))); // past the end
        Assert.Equal(0, World.P_InterceptVector(trace, new divline_t(0, F(10), F(5), 0))); // parallel
        // Diagonal: y = x against x + y = 384 meets at (192, 192), half way along the second.
        Assert.Equal(FRACUNIT / 2, World.P_InterceptVector(new divline_t(F(128), F(256), F(128), F(-128)), new divline_t(F(128), F(128), F(128), F(128))));
    }

    // ---- P_LineOpening ----

    [Fact]
    public void LineOpeningOfTheSyntheticLines()
    {
        World w = Synthetic();
        w.P_LineOpening(w.lines[1]); // west room (0..128) | east room (16..112)
        Assert.Equal((F(112), F(16), F(96), F(0)), (w.opentop, w.openbottom, w.openrange, w.lowfloor));
        w.P_LineOpening(w.lines[15]); // courtyard A (-16..256) | ledge B (64..192)
        Assert.Equal((F(192), F(64), F(128), F(-16)), (w.opentop, w.openbottom, w.openrange, w.lowfloor));
        w.P_LineOpening(w.lines[12]); // closed door D (0..0) | courtyard A
        Assert.Equal((0, 0, 0, F(-16)), (w.opentop, w.openbottom, w.openrange, w.lowfloor));

        // One-sided: only openrange changes.
        w.P_LineOpening(w.lines[15]);
        w.P_LineOpening(w.lines[0]);
        Assert.Equal((F(192), F(64), 0, F(-16)), (w.opentop, w.openbottom, w.openrange, w.lowfloor));

        // It reads the sectors' current heights (the sim moves them in place).
        w.sectors[3].ceilingheight = F(72);
        w.P_LineOpening(w.lines[12]);
        Assert.Equal((F(72), 0, F(72), F(-16)), (w.opentop, w.openbottom, w.openrange, w.lowfloor));
        Assert.Equal(F(72), w.level.Sectors[3].CeilingHeight);
    }

    [Fact]
    public void LinesMirrorTheMap()
    {
        World w = Synthetic();
        Assert.Equal(w.level.Lines.Length, w.lines.Length);
        for (int i = 0; i < w.lines.Length; i++)
        {
            line_t ld = w.lines[i];
            Line map = w.level.Lines[i];
            Assert.Same(map, ld.map);
            Assert.Equal(i, ld.Index);
            Assert.Equal(map.FrontSector?.Index, ld.frontsector?.Index);
            Assert.Equal(map.BackSector?.Index, ld.backsector?.Index);
            if (ld.frontsector != null)
                Assert.Same(w.sectors[ld.frontsector.Index], ld.frontsector);
        }
        w.lines[8].special = 0; // forwards to the map line
        Assert.Equal(0, w.level.Lines[8].Special);
    }

    // ---- the blockmap iterators ----

    private static List<int> LinesIn(World w, int bx, int by)
    {
        var seen = new List<int>();
        Assert.True(w.P_BlockLinesIterator(bx, by, ld => { seen.Add(ld.Index); return true; }));
        return seen;
    }

    [Fact]
    public void BlockLinesIteratorVisitsEachLineOncePerSearch()
    {
        World w = Synthetic();
        // Blocks are 128 wide from (-136, -136): column 2 is x 120..248, so it holds L1 (x = 128) in all three rows.
        w.validcount++;
        Assert.Equal(new[] { 0, 1, 2, 6 }, LinesIn(w, 2, 0)); // the leading 0 is line 0, visited like any line
        Assert.Equal(Array.Empty<int>(), LinesIn(w, 2, 0)); // the same search: already checked
        Assert.Equal(Array.Empty<int>(), LinesIn(w, 2, 1)); // L0 (via the 0) and L1 again
        Assert.Equal(new[] { 4 }, LinesIn(w, 2, 2));
        w.validcount++;
        Assert.Equal(new[] { 0, 1 }, LinesIn(w, 2, 1)); // a new search sees them again
        Assert.All([0, 1], i => Assert.Equal(w.validcount, w.lines[i].validcount));

        // Off the map: no lines, true.
        Assert.Empty(LinesIn(w, -1, 0));
        Assert.Empty(LinesIn(w, 10, 0));
        Assert.Empty(LinesIn(w, 0, 3));

        // Stops at the first false, and the lines after it stay unchecked.
        w.validcount++;
        var stopped = new List<int>();
        Assert.False(w.P_BlockLinesIterator(2, 0, ld => { stopped.Add(ld.Index); return ld.Index != 1; }));
        Assert.Equal(new[] { 0, 1 }, stopped);
        Assert.Equal(new[] { 2, 6 }, LinesIn(w, 2, 0));
    }

    [Fact]
    public void BlockLinesIteratorOnBlockBoundariesAndCorners()
    {
        World w = Grid();
        Assert.Equal((F(-64), F(-64), 5, 5), (w.bmaporgx, w.bmaporgy, w.bmapwidth, w.bmapheight));

        // L4 lies on the boundary x = 448 between columns 3 and 4: listed in both, visited once per search.
        w.validcount++;
        Assert.Contains(4, LinesIn(w, 3, 2));
        Assert.DoesNotContain(4, LinesIn(w, 4, 2));
        w.validcount++;
        Assert.Contains(4, LinesIn(w, 4, 2));
        // It ends on the corners (448, 64) and (448, 320): the rows below and above list it too.
        w.validcount++;
        Assert.Contains(4, LinesIn(w, 4, 0));
        w.validcount++;
        Assert.Contains(4, LinesIn(w, 3, 3));

        // L5 passes through the corner (192, 192) of blocks (1, 1), (2, 1), (1, 2), (2, 2).
        foreach ((int bx, int by) in new[] { (1, 1), (2, 1), (1, 2), (2, 2) })
        {
            w.validcount++;
            Assert.Contains(5, LinesIn(w, bx, by));
        }
        w.validcount++;
        int visits = 0;
        foreach ((int bx, int by) in new[] { (1, 1), (2, 1), (1, 2), (2, 2) })
            w.P_BlockLinesIterator(bx, by, ld => { visits += ld.Index == 5 ? 1 : 0; return true; });
        Assert.Equal(1, visits);
    }

    [Fact]
    public void BlockThingsIteratorGoesByTheCentre()
    {
        World w = Grid();
        // On the boundaries x = 192 and y = 64: the block whose left and bottom edges they are.
        mobj_t barrel = w.P_SpawnMobj(F(192), F(64), World.ONFLOORZ, mobjtype_t.MT_BARREL);
        mobj_t imp = w.P_SpawnMobj(F(191), F(63), World.ONFLOORZ, mobjtype_t.MT_TROOP);
        mobj_t second = w.P_SpawnMobj(F(200), F(100), World.ONFLOORZ, mobjtype_t.MT_BARREL);

        List<mobj_t> In(int bx, int by)
        {
            var list = new List<mobj_t>();
            Assert.True(w.P_BlockThingsIterator(bx, by, m => { list.Add(m); return true; }));
            return list;
        }
        Assert.Equal(new[] { second, barrel }, In(2, 1)); // head insertion: newest first
        Assert.Equal(new[] { imp }, In(1, 0));
        Assert.Empty(In(1, 1));
        Assert.Empty(In(2, 0));
        Assert.Empty(In(-1, 1));
        Assert.Empty(In(2, 5));

        var stopped = new List<mobj_t>();
        Assert.False(w.P_BlockThingsIterator(2, 1, m => { stopped.Add(m); return false; }));
        Assert.Equal(new[] { second }, stopped);
    }

    // ---- P_PathTraverse ----

    [Fact]
    public void PathTraverseThroughTheSyntheticWestRoom()
    {
        World w = Synthetic();
        mobj_t player = w.players[0].mo!;
        mobj_t imp80 = w.Mobjs().Single(m => m.type == mobjtype_t.MT_TROOP && m.x == F(80) && m.y == 0);
        mobj_t imp320 = w.Mobjs().Single(m => m.type == mobjtype_t.MT_TROOP && m.x == F(320));

        // (0, 0) → (360, 72), y = x / 5: from the player's centre, through the imp at (80, 0) (radius 20: its
        // diagonal (60, 20)–(100, -20) is met at x = 66.7), L1 (x = 128) and the imp at (320, 64) (its diagonal
        // (300, 84)–(340, 44) at x = 320). The barrel at (64, 64) and the other imps are missed.
        (bool result, List<(object What, int Frac)>? visited) = Traverse(w, 0, 0, 360, 72, World.PT_ADDLINES | World.PT_ADDTHINGS);
        Assert.True(result);
        Assert.Equal([player, imp80, w.lines[1], imp320], visited.Select(v => v.What));
        AssertFrac(0, visited[0].Frac);
        AssertFrac(66.667 / 360, visited[1].Frac);
        AssertFrac(128.0 / 360, visited[2].Frac);
        AssertFrac(320.0 / 360, visited[3].Frac);
        Assert.Equal((0, 0, F(360), F(72)), (w.trace.x, w.trace.y, w.trace.dx, w.trace.dy));
        Assert.Equal(4, w.intercept_p);
        Assert.All(w.intercepts.Take(4), i => Assert.Equal(int.MaxValue, i.frac)); // visited ones are spent

        // Lines or things only.
        Assert.Equal([w.lines[1]], Traverse(w, 0, 0, 360, 72, World.PT_ADDLINES).Visited.Select(v => v.What));
        Assert.Equal([player, imp80, imp320], Traverse(w, 0, 0, 360, 72, World.PT_ADDTHINGS).Visited.Select(v => v.What));

        // Things beyond the end are collected but not visited (maxfrac FRACUNIT); behind the start not at all.
        (_, visited) = Traverse(w, 40, 0, 150, 30, World.PT_ADDLINES | World.PT_ADDTHINGS);
        Assert.Equal([imp80, w.lines[1]], visited.Select(v => v.What));

        // The traverser stops it.
        int calls = 0;
        Assert.False(w.P_PathTraverse(0, 0, F(360), F(72), World.PT_ADDLINES | World.PT_ADDTHINGS, _ => ++calls < 2));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void PathTraverseEarlyOutAtOneSidedLines()
    {
        World w = Synthetic();
        // West through L3 (one-sided, x = -128) at 128/300.
        (bool result, List<(object What, int Frac)>? visited) = Traverse(w, 0, 0, -300, 0, World.PT_ADDLINES);
        Assert.True(result);
        Assert.Equal([w.lines[3]], visited.Select(v => v.What));
        AssertFrac(128.0 / 300, visited[0].Frac);

        (result, visited) = Traverse(w, 0, 0, -300, 0, World.PT_ADDLINES | World.PT_EARLYOUT);
        Assert.False(result);
        Assert.Empty(visited);

        // Short of the wall: no early out. A two-sided line does not stop it.
        Assert.True(Traverse(w, 0, 0, -100, 0, World.PT_ADDLINES | World.PT_EARLYOUT).Result);
        (result, visited) = Traverse(w, 0, 0, 300, 0, World.PT_ADDLINES | World.PT_EARLYOUT);
        Assert.True(result);
        Assert.Equal([w.lines[1]], visited.Select(v => v.What));
        // A one-sided line beyond the end of a trace (frac >= 1) does not stop it either.
        (result, visited) = Traverse(w, 300, 0, 380, 0, World.PT_ADDLINES | World.PT_EARLYOUT);
        Assert.True(result);
        Assert.Empty(visited);
    }

    [Fact]
    public void PathTraverseThroughABlockCorner()
    {
        World w = Grid();
        // x + y = 384 crosses L5 (y = x) exactly at the block corner (192, 192), from block (1, 2) to (2, 1).
        (bool result, List<(object What, int Frac)>? visited) = Traverse(w, 150, 234, 234, 150, World.PT_ADDLINES);
        Assert.True(result);
        Assert.Equal([w.lines[5]], visited.Select(v => v.What));
        AssertFrac(0.5, visited[0].Frac);
        // And the other way round, and along the line's own direction through the corner (not crossed).
        Assert.Equal([w.lines[5]], Traverse(w, 234, 150, 150, 234, World.PT_ADDLINES).Visited.Select(v => v.What));
        Assert.Empty(Traverse(w, 100, 100, 300, 300, World.PT_ADDLINES).Visited);

        // A steep trace beside the corner crosses it once.
        Assert.Equal([w.lines[5]], Traverse(w, 180, 100, 200, 300, World.PT_ADDLINES).Visited.Select(v => v.What));
    }

    [Fact]
    public void PathTraverseAlongBlockBoundaries()
    {
        World w = Grid();
        // Crossing L4, which lies on the boundary x = 448 (listed in columns 3 and 4): one intercept.
        (bool _, List<(object What, int Frac)>? visited) = Traverse(w, 400, 200, 500, 200, World.PT_ADDLINES);
        Assert.Equal([w.lines[4]], visited.Select(v => v.What));
        AssertFrac(48.0 / 100, visited[0].Frac);
        Assert.Equal([w.lines[4]], Traverse(w, 500, 200, 400, 200, World.PT_ADDLINES).Visited.Select(v => v.What));

        // A start exactly on a block boundary moves one unit up or right ("don't side exactly on a line").
        Traverse(w, 192, 100, 300, 100, World.PT_ADDLINES);
        Assert.Equal((F(193), F(100), F(107), 0), (w.trace.x, w.trace.y, w.trace.dx, w.trace.dy));
        Traverse(w, 100, 320, 100, 0, World.PT_ADDLINES);
        Assert.Equal((F(100), F(321), 0, F(-321)), (w.trace.x, w.trace.y, w.trace.dx, w.trace.dy));
        Traverse(w, 448, 64, 0, 0, World.PT_ADDLINES);
        Assert.Equal((F(449), F(65)), (w.trace.x, w.trace.y));

        // Down the boundary x = 192 (x = 193 after the nudge) to the south wall: L5 just beside the corner, then L3.
        (_, visited) = Traverse(w, 192, 300, 192, -10, World.PT_ADDLINES);
        Assert.Equal([w.lines[5], w.lines[3]], visited.Select(v => v.What));
        AssertFrac((300 - 193) / 310.0, visited[0].Frac);

        // A trace that ends on a corner of L4 crosses it (both ends of the line on different sides).
        (_, visited) = Traverse(w, 300, 300, 448, 320, World.PT_ADDLINES);
        Assert.Equal([w.lines[4]], visited.Select(v => v.What));
    }

    [Fact]
    public void PathTraverseFindsThingsByTheirBlockOnly()
    {
        World w = Grid();
        // A barrel (radius 10) centred on the boundary x = 192: in column 2, its box reaching into column 1.
        mobj_t barrel = w.P_SpawnMobj(F(192), F(100), World.ONFLOORZ, mobjtype_t.MT_BARREL);
        Assert.Equal(barrel, w.blocklinks[1 * 5 + 2]);

        // A trace in column 1 (x = 186, through the box's west half) misses it: vanilla's blockmap check
        // only looks at things whose centre is in a block the trace passes.
        Assert.Empty(Traverse(w, 186, 20, 186, 180, World.PT_ADDTHINGS).Visited);
        // The same trace in column 2 (x = 195) finds it, at the box's diagonal: y = 100 - (195 - 192) = 97 here.
        (bool _, List<(object What, int Frac)>? visited) = Traverse(w, 195, 20, 195, 180, World.PT_ADDTHINGS);
        Assert.Equal([barrel], visited.Select(v => v.What));
        AssertFrac(77.0 / 160, visited[0].Frac);
        // A trace crossing from column 1 into 2 finds it.
        Assert.Equal([barrel], Traverse(w, 150, 90, 250, 110, World.PT_ADDTHINGS).Visited.Select(v => v.What));
    }

    [Fact]
    public void TraverseInterceptsSortsByFractionKeepingTiesInOrder()
    {
        World w = Grid();
        // Two barrels on the same spot: the same fraction, visited in collection (block list) order.
        mobj_t first = w.P_SpawnMobj(F(100), F(100), World.ONFLOORZ, mobjtype_t.MT_BARREL);
        mobj_t second = w.P_SpawnMobj(F(100), F(100), World.ONFLOORZ, mobjtype_t.MT_BARREL);
        mobj_t near = w.P_SpawnMobj(F(40), F(100), World.ONFLOORZ, mobjtype_t.MT_BARREL);
        (bool _, List<(object What, int Frac)>? visited) = Traverse(w, 10, 100, 180, 100, World.PT_ADDTHINGS);
        Assert.Equal([near, second, first], visited.Select(v => v.What)); // the block list is newest first
        Assert.Equal(visited[1].Frac, visited[2].Frac);
    }

    [Fact]
    public void PathTraversePastMaxIntercepts()
    {
        World w = Grid();
        // 150 barrels in a row in one block row: more intercepts than vanilla's array holds.
        var barrels = new List<mobj_t>();
        for (int i = 0; i < 150; i++)
            barrels.Add(w.P_SpawnMobj(F(20 + i * 3), F(100), World.ONFLOORZ, mobjtype_t.MT_BARREL));
        (bool result, List<(object What, int Frac)>? visited) = Traverse(w, 10, 100, 500, 100, World.PT_ADDTHINGS);
        Assert.True(result);
        Assert.Equal(150, w.intercept_p);
        Assert.Equal(150 - World.MAXINTERCEPTS, w.interceptoverruns);
        Assert.Equal(barrels, visited.Select(v => v.What)); // nearest first: west to east
        Assert.True(visited.Zip(visited.Skip(1)).All(p => p.First.Frac < p.Second.Frac));

        // The list stays grown; a second overflowing traversal adds to the count.
        Traverse(w, 10, 100, 500, 100, World.PT_ADDTHINGS);
        Assert.Equal(2 * (150 - World.MAXINTERCEPTS), w.interceptoverruns);
        Traverse(w, 10, 100, 50, 100, World.PT_ADDTHINGS);
        Assert.Equal(2 * (150 - World.MAXINTERCEPTS), w.interceptoverruns);
    }

    [Fact]
    public void PathTraverseStopsAfter64Blocks()
    {
        World w = Grid();
        // Blocks off the map are empty, and the walk stops after 64 blocks. From inside the room westwards
        // 10000 units: the west wall L0 is in the first blocks, so it is found.
        (bool _, List<(object What, int Frac)>? visited) = Traverse(w, 100, 100, -10000, 100, World.PT_ADDLINES);
        Assert.Equal([w.lines[0]], visited.Select(v => v.What));
        // From 10000 units west into the room: the room's blocks are more than 64 blocks away, nothing is found.
        Assert.Empty(Traverse(w, -10000, 100, 100, 100, World.PT_ADDLINES).Visited);
        // From 7000 units west (55 blocks): the walk reaches the room.
        Assert.Equal([w.lines[0]], Traverse(w, -7000, 100, 100, 100, World.PT_ADDLINES).Visited.Select(v => v.What));
    }

    // ---- DOOM1 E1M1 spot checks ----

    /// <summary>The lines a trace crosses by brute force over every line, with PIT_AddLineIntercepts' tests.</summary>
    private static List<line_t> CrossedLines(World w, divline_t trace)
    {
        var crossed = new List<(line_t Line, int Frac)>();
        foreach (line_t ld in w.lines)
        {
            int s1, s2;
            if (trace.dx > FRACUNIT * 16 || trace.dy > FRACUNIT * 16 || trace.dx < -FRACUNIT * 16 || trace.dy < -FRACUNIT * 16)
            {
                s1 = World.P_PointOnDivlineSide(ld.v1.X, ld.v1.Y, trace);
                s2 = World.P_PointOnDivlineSide(ld.v2.X, ld.v2.Y, trace);
            }
            else
            {
                s1 = World.P_PointOnLineSide(trace.x, trace.y, ld);
                s2 = World.P_PointOnLineSide(trace.x + trace.dx, trace.y + trace.dy, ld);
            }
            if (s1 == s2)
                continue;
            World.P_MakeDivline(ld, out divline_t dl);
            int frac = World.P_InterceptVector(trace, dl);
            if (frac < 0 || frac > FRACUNIT)
                continue;
            crossed.Add((ld, frac));
        }
        return [.. crossed.Select(c => c.Line)];
    }

    [Fact]
    public void Doom1E1M1TracesMatchBruteForce()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom1());
        World w = Load(wad, "E1M1");
        mobj_t player = w.players[0].mo!;

        // Random traces: the traversal finds exactly the lines a brute force search over all lines finds.
        int mismatches = 0, traces = 0;
        uint rng = 12345u;
        int Next(int range) { rng = rng * 1103515245 + 12345; return (int)((rng >> 8) % (uint)range); }
        for (int i = 0; i < 400; i++)
        {
            // Random segments up to 1024 units long inside the map's bounds.
            int x1 = w.bmaporgx + F(Next(w.bmapwidth * 128));
            int y1 = w.bmaporgy + F(Next(w.bmapheight * 128));
            int x2 = x1 + F(Next(2048) - 1024);
            int y2 = y1 + F(Next(2048) - 1024);
            var found = new List<line_t>();
            w.P_PathTraverse(x1, y1, x2, y2, World.PT_ADDLINES, @in => { found.Add(@in.line!); return true; });
            divline_t trace = w.trace; // after the boundary nudge
            List<line_t> expected = CrossedLines(w, trace);
            traces++;
            // Every intercept is a crossed line, each once.
            Assert.Equal(found.Count, found.Distinct().Count());
            Assert.All(found, ld => Assert.Contains(ld, expected));
            if (expected.Count != found.Count)
                mismatches++;
        }
        // The blockmap walk could miss a line only where the node builder's blockmap leaves it out of a block
        // the line crosses, or where a trace grazes a block corner; none of these random traces hit either.
        Assert.True(mismatches == 0, $"{mismatches} of {traces} traces missed lines");

        // The player at the start is found by a trace from just south through its centre.
        (bool _, List<(object What, int Frac)>? visited) = Traverse(w, player.x >> 16, (player.y >> 16) - 100, player.x >> 16, (player.y >> 16) + 10, World.PT_ADDTHINGS);
        Assert.Contains(player, visited.Select(v => v.What));
    }

    [Fact]
    public void Doom1E1M1LineOpeningsAndBoxSides()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom1());
        World w = Load(wad, "E1M1");
        int twoSided = 0;
        foreach (line_t ld in w.lines)
        {
            w.P_LineOpening(ld);
            if (ld.backsector == null)
            {
                Assert.Equal(0, w.openrange);
                continue;
            }
            twoSided++;
            sector_t f = ld.frontsector!, b = ld.backsector;
            Assert.Equal(Math.Min(f.ceilingheight, b.ceilingheight), w.opentop);
            Assert.Equal(Math.Max(f.floorheight, b.floorheight), w.openbottom);
            Assert.Equal(Math.Min(f.floorheight, b.floorheight), w.lowfloor);
            Assert.Equal(w.opentop - w.openbottom, w.openrange);

            // A line's own bounding box straddles it, unless it is axis-aligned (then the box is the line).
            int side = World.P_BoxOnLineSide(ld.bbox, ld);
            if (ld.slopetype is SlopeType.ST_POSITIVE or SlopeType.ST_NEGATIVE)
                Assert.Equal(-1, side);
        }
        Assert.True(twoSided > 100, $"{twoSided} two-sided lines");

        // Every thing's box (radius) against every line: P_BoxOnLineSide agrees with the corners' sides.
        foreach (mobj_t m in w.Mobjs())
        {
            int[] box = [m.y + m.radius, m.y - m.radius, m.x - m.radius, m.x + m.radius];
            foreach (line_t ld in w.lines.Where(l => l.slopetype is SlopeType.ST_POSITIVE or SlopeType.ST_NEGATIVE))
            {
                int[] corners =
                [
                    World.P_PointOnLineSide(box[BBox.BOXLEFT], box[BBox.BOXTOP], ld),
                    World.P_PointOnLineSide(box[BBox.BOXRIGHT], box[BBox.BOXTOP], ld),
                    World.P_PointOnLineSide(box[BBox.BOXLEFT], box[BBox.BOXBOTTOM], ld),
                    World.P_PointOnLineSide(box[BBox.BOXRIGHT], box[BBox.BOXBOTTOM], ld),
                ];
                int side = World.P_BoxOnLineSide(box, ld);
                if (side >= 0)
                    Assert.All(corners, c => Assert.Equal(side, c));
                else
                    Assert.True(corners.Distinct().Count() == 2);
            }
        }
    }
}
