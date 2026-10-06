using System;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;
using static IsoDoom.Map.Fixed;

namespace IsoDoom.Tests.Map;

/// <summary>T2.1 on the synthetic IWAD's <c>E1M1</c> (two rooms and a strip of four sectors, seven subsectors, six nodes; runs in CI).</summary>
public class SyntheticMapTests
{
    private static Level Load() =>
        Level.Load(new WadArchive(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) }), "E1M1");

    [Fact]
    public void ElementCountsAndPlayerStart()
    {
        Level map = Load();
        Assert.Equal("E1M1", map.Name);
        Assert.Equal(3, map.Things.Length);
        Assert.Equal(20, map.Lines.Length);
        Assert.Equal(24, map.Sides.Length);
        Assert.Equal(18, map.Vertexes.Length);
        Assert.Equal(26, map.Segs.Length);
        Assert.Equal(7, map.Subsectors.Length);
        Assert.Equal(6, map.Nodes.Length);
        Assert.Equal(6, map.Sectors.Length);
        Assert.Equal(new MapThing(0, 0, 90, 1, 7), map.Things.Single(t => t.Type == 1));
        Assert.Equal(new MapThing(320, 64, 180, 3001, 7), map.Things[2]);
    }

    [Fact]
    public void VertexesSectorsAndSidesAreFixedPoint()
    {
        Level map = Load();
        Assert.Equal((384 * FRACUNIT, -128 * FRACUNIT), (map.Vertexes[5].X, map.Vertexes[5].Y));
        Sector east = map.Sectors[1];
        Assert.Equal(16 * FRACUNIT, east.FloorHeight);
        Assert.Equal(112 * FRACUNIT, east.CeilingHeight);
        Assert.Equal(("LAVA1", "FLOOR1", (short)208), (east.FloorPic, east.CeilingPic, east.LightLevel));
        Side l1Front = map.Sides[1];
        Assert.Equal(("PANEL", "PANEL", "-"), (l1Front.TopTexture, l1Front.BottomTexture, l1Front.MidTexture));
        Assert.Same(map.Sectors[0], l1Front.Sector);
        Assert.Equal(32 * FRACUNIT, map.Sides[3].TextureOffset);
        Assert.Equal(8 * FRACUNIT, map.Sides[6].RowOffset);
    }

    [Fact]
    public void LinedefsHaveSectorsBoxesAndSlopes()
    {
        Level map = Load();
        Line l1 = map.Lines[1];
        Assert.Equal(Line.ML_TWOSIDED, l1.Flags);
        Assert.Equal(new[] { 1, 2 }, l1.SideNum);
        Assert.Same(map.Sectors[0], l1.FrontSector);
        Assert.Same(map.Sectors[1], l1.BackSector);
        Assert.Equal((0, -256 * FRACUNIT), (l1.Dx, l1.Dy));
        Assert.Equal(SlopeType.ST_VERTICAL, l1.SlopeType);
        Assert.Equal(new[] { 128 * FRACUNIT, -128 * FRACUNIT, 128 * FRACUNIT, 128 * FRACUNIT }, l1.BBox);

        Line l0 = map.Lines[0];
        Assert.Equal(new[] { 0, -1 }, l0.SideNum);
        Assert.Null(l0.BackSector);
        Assert.Equal(SlopeType.ST_HORIZONTAL, l0.SlopeType);
    }

    [Fact]
    public void SegsFollowTheirLinedefSides()
    {
        Level map = Load();
        Seg front = map.Segs[1], back = map.Segs[7];
        Assert.Same(map.Lines[1], front.LineDef);
        Assert.Equal(0, front.Side);
        Assert.Same(map.Sides[1], front.SideDef);
        Assert.Same(map.Sectors[0], front.FrontSector);
        Assert.Same(map.Sectors[1], front.BackSector);
        Assert.Equal(0xC0000000u, front.Angle);

        Assert.Equal(1, back.Side);
        Assert.Same(map.Sides[2], back.SideDef);
        Assert.Same(map.Sectors[1], back.FrontSector);
        Assert.Same(map.Sectors[0], back.BackSector);
        Assert.Equal(0x40000000u, back.Angle);
        Assert.Equal(128 * FRACUNIT, back.Offset);
        Assert.Same(map.Vertexes[7], back.V1);

        Assert.Null(map.Segs[0].BackSector); // one-sided
    }

    [Fact]
    public void BspNodesLocatePoints()
    {
        Level map = Load();
        Node root = map.Nodes[^1];
        Assert.Equal((448 * FRACUNIT, -128 * FRACUNIT, 0, 256 * FRACUNIT), (root.X, root.Y, root.Dx, root.Dy));
        Assert.Equal(new[] { 4, 1 }, root.Children);
        Node rooms = map.Nodes[1];
        Assert.Equal((128 * FRACUNIT, -128 * FRACUNIT, 0, 256 * FRACUNIT), (rooms.X, rooms.Y, rooms.Dx, rooms.Dy));
        Assert.Equal(new[] { 0, Node.NF_SUBSECTOR | 0 }, rooms.Children);
        Assert.Equal(new[] { 128 * FRACUNIT, -128 * FRACUNIT, 128 * FRACUNIT, 384 * FRACUNIT }, rooms.BBox[0]);
        Assert.Equal(new[] { Node.NF_SUBSECTOR | 1, Node.NF_SUBSECTOR | 2 }, map.Nodes[0].Children);

        Assert.Equal(0, map.R_PointInSubsector(0, 0).Index);
        Assert.Equal(1, map.R_PointInSubsector(256 * FRACUNIT, -64 * FRACUNIT).Index);
        Assert.Equal(2, map.R_PointInSubsector(320 * FRACUNIT, 64 * FRACUNIT).Index);
        // On a partition line: x <= node.x is the back side of a north-pointing partition.
        Assert.Equal(0, map.R_PointInSubsector(128 * FRACUNIT, 64 * FRACUNIT).Index);
        // The strip east of the root's partition: room C, door D, courtyard A, ledge B.
        Assert.Equal(new[] { 3, 4, 5, 6 }, new[] { 576, 648, 784, 976 }.Select(x => map.R_PointInSubsector(x * FRACUNIT, 0).Index));
    }

    [Fact]
    public void GroupLinesSetsSubsectorAndSectorData()
    {
        Level map = Load();
        Assert.Equal(new[] { 0, 1, 1, 2, 3, 4, 5 }, map.Subsectors.Select(s => s.Sector.Index));
        Assert.Equal((4, 0), (map.Subsectors[0].NumLines, map.Subsectors[0].FirstLine));
        Assert.Equal(new[] { 0, 1, 2, 3 }, map.Sectors[0].Lines.Select(l => l.Index));
        Assert.Equal(new[] { 1, 4, 5, 6 }, map.Sectors[1].Lines.Select(l => l.Index));
        Assert.Equal(new[] { 8, 11, 12, 13 }, map.Sectors[3].Lines.Select(l => l.Index));
        Assert.Equal(24, map.TotalLines);
        Assert.Equal((256 * FRACUNIT, 0), (map.Sectors[1].SoundOrgX, map.Sectors[1].SoundOrgY));
        // East room x 128..384, y -128..128, widened by 32, from origin (-136, -136): blocks x 1..4, y 0..2 (clamped).
        Assert.Equal(new[] { 2, 0, 1, 4 }, map.Sectors[1].BlockBox);
    }

    [Fact]
    public void BlockmapAndReject()
    {
        Level map = Load();
        Blockmap bm = map.Blockmap;
        Assert.Equal((-136 * FRACUNIT, -136 * FRACUNIT, 10, 3), (bm.BmapOrgX, bm.BmapOrgY, bm.BmapWidth, bm.BmapHeight));
        Assert.Equal(new[] { 0, 2, 3 }, bm.BlockLines(0, 0));       // bottom left: L2, L3
        Assert.Equal(new[] { 0, 1, 2, 6 }, bm.BlockLines(2, 0));    // L1 and the south walls
        Assert.Equal(new[] { 0, 5 }, bm.BlockLines(4, 1));          // east wall only
        Assert.Equal(new[] { 0, 9, 10 }, bm.BlockLines(5, 0));   // room C's south and west walls
        Assert.Equal(new[] { 0 }, bm.BlockLines(3, 1));             // inside the east room
        Assert.Empty(bm.BlockLines(10, 0));
        Assert.Equal(5, map.Reject.RejectMatrix.Length);
        Assert.False(map.Reject.IsRejected(0, 1, map.Sectors.Length));
    }

    [Fact]
    public void ShortRejectIsPaddedAsChocolateDoomDoes()
    {
        // The synthetic E1M1 with 3 extra (lineless) sectors and an empty REJECT:
        // 9 sectors need 11 bytes, which PadRejectArray fills with the zone block
        // header vanilla read past the lump: ((totallines * 4 + 3) & ~3) + 24, 0, PU_LEVEL (50).
        var iwad = new WadArchive(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) });
        int e1m1 = iwad.W_GetNumForName("E1M1");
        var b = new WadBuilder(WadType.Iwad).Markers("E1M1");
        for (int ml = Level.ML_THINGS; ml <= Level.ML_BLOCKMAP; ml++)
        {
            WadLump lump = iwad.Lumps[e1m1 + ml];
            byte[] data = lump.Data.ToArray();
            if (ml == Level.ML_SECTORS)
                data = data.Concat(new byte[26 * 3]).ToArray();
            if (ml == Level.ML_REJECT)
                data = Array.Empty<byte>();
            b.Lump(lump.Name, data);
        }
        Level map = Level.Load(new WadArchive(new[] { b.ToWadFile() }), "E1M1");
        Assert.Equal(9, map.Sectors.Length);
        Assert.Equal(24, map.TotalLines);
        Assert.Equal(new byte[] { 120, 0, 0, 0, 0, 0, 0, 0, 50, 0, 0 }, map.Reject.RejectMatrix);
        Assert.Empty(map.Sectors[8].Lines);
    }

    [Fact]
    public void MissingOrMisorderedMapsAreRejected()
    {
        var wad = new WadArchive(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) });
        Assert.Throws<WadFormatException>(() => Level.Load(wad, "E1M2"));
        Assert.Throws<WadFormatException>(() => Level.Load(wad, "PLAYPAL"));
    }
}
