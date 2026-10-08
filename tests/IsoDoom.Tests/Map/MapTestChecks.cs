using System.Linq;
using IsoDoom.Map;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Map;

/// <summary>Structural checks that hold for every map a node builder made.</summary>
internal static class MapTestChecks
{
    /// <summary>Record counts match the lump sizes, and the parsed structures hang together.</summary>
    public static void CheckConsistent(WadArchive wad, Level map)
    {
        int header = wad.W_GetNumForName(map.Name);
        int Records(int ml, int size) => wad.W_LumpLength(header + ml) / size;
        Assert.Equal(Records(Level.ML_THINGS, 10), map.Things.Length);
        Assert.Equal(Records(Level.ML_LINEDEFS, 14), map.Lines.Length);
        Assert.Equal(Records(Level.ML_SIDEDEFS, 30), map.Sides.Length);
        Assert.Equal(Records(Level.ML_VERTEXES, 4), map.Vertexes.Length);
        Assert.Equal(Records(Level.ML_SEGS, 12), map.Segs.Length);
        Assert.Equal(Records(Level.ML_SSECTORS, 4), map.Subsectors.Length);
        Assert.Equal(Records(Level.ML_NODES, 28), map.Nodes.Length);
        Assert.Equal(Records(Level.ML_SECTORS, 26), map.Sectors.Length);

        // A binary tree over the subsectors, every node and subsector reached once from the root.
        Assert.Equal(map.Subsectors.Length - 1, map.Nodes.Length);
        bool[] seenNodes = new bool[map.Nodes.Length];
        bool[] seenSubsectors = new bool[map.Subsectors.Length];
        var stack = new System.Collections.Generic.Stack<int>();
        stack.Push(map.Nodes.Length - 1);
        while (stack.Count > 0)
        {
            int n = stack.Pop();
            if ((n & Node.NF_SUBSECTOR) != 0)
            {
                Assert.False(seenSubsectors[n & ~Node.NF_SUBSECTOR]);
                seenSubsectors[n & ~Node.NF_SUBSECTOR] = true;
                continue;
            }
            Assert.False(seenNodes[n]);
            seenNodes[n] = true;
            stack.Push(map.Nodes[n].Children[0]);
            stack.Push(map.Nodes[n].Children[1]);
        }
        Assert.All(seenNodes, Assert.True);
        Assert.All(seenSubsectors, Assert.True);

        // Subsectors cover the segs in order, without gaps or overlaps.
        int next = 0;
        foreach (Subsector ss in map.Subsectors)
        {
            Assert.Equal(next, ss.FirstLine);
            next += ss.NumLines;
        }
        Assert.Equal(map.Segs.Length, next);

        // P_GroupLines: each sector's line list, and the total.
        Assert.Equal(map.TotalLines, map.Sectors.Sum(s => s.Lines.Count));
        foreach (Sector sector in map.Sectors)
            Assert.All(sector.Lines, l => Assert.True(l.FrontSector == sector || l.BackSector == sector));

        Assert.True(map.Reject.RejectMatrix.Length >= (map.Sectors.Length * map.Sectors.Length + 7) / 8);

        // Every thing lies within the blockmap and locates to a subsector.
        Blockmap bm = map.Blockmap;
        foreach (MapThing t in map.Things)
        {
            int x = t.X << Fixed.FRACBITS, y = t.Y << Fixed.FRACBITS;
            Assert.InRange((x - bm.BmapOrgX) >> Blockmap.MAPBLOCKSHIFT, 0, bm.BmapWidth - 1);
            Assert.InRange((y - bm.BmapOrgY) >> Blockmap.MAPBLOCKSHIFT, 0, bm.BmapHeight - 1);
            map.R_PointInSubsector(x, y);
        }
        // Every block list names real lines.
        for (int by = 0; by < bm.BmapHeight; by++)
        {
            for (int bx = 0; bx < bm.BmapWidth; bx++)
                Assert.All(bm.BlockLines(bx, by), l => Assert.InRange(l, 0, map.Lines.Length - 1));
        }
    }
}
