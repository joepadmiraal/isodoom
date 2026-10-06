using System;
using System.Collections.Generic;
using IsoDoom.Wad;

namespace IsoDoom.Map;

/// <summary>
/// The <c>BLOCKMAP</c> lump (p_setup.c <c>P_LoadBlockMap</c>): a grid of
/// 128×128 map unit blocks, each with a list of the lines that touch it, used
/// by the sim's collision and line-of-fire searches.
/// <para>
/// The lump is kept as vanilla keeps it (<c>blockmaplump</c>, little-endian
/// 16-bit values): a header (<c>bmaporgx</c>, <c>bmaporgy</c>,
/// <c>bmapwidth</c>, <c>bmapheight</c>), then one offset per block (row by row
/// from the bottom left; vanilla's <c>blockmap</c> points here), then the
/// lists. A list is a run of line numbers ending in -1; the node builders put
/// a 0 first, which vanilla's <c>P_BlockLinesIterator</c> walks like any line
/// number (so line 0 is checked from every block).
/// </para>
/// </summary>
public sealed class Blockmap
{
    /// <summary>p_local.h <c>MAPBLOCKUNITS</c>: block size in map units.</summary>
    public const int MAPBLOCKUNITS = 128;

    /// <summary>p_local.h <c>MAPBLOCKSIZE</c>: block size in fixed_t.</summary>
    public const int MAPBLOCKSIZE = MAPBLOCKUNITS * Fixed.FRACUNIT;

    /// <summary>p_local.h <c>MAPBLOCKSHIFT</c>: fixed_t to block number.</summary>
    public const int MAPBLOCKSHIFT = Fixed.FRACBITS + 7;

    /// <summary>The header size in 16-bit values; <c>blockmap = blockmaplump + 4</c>.</summary>
    public const int HeaderSize = 4;

    private readonly short[] _lump;

    internal Blockmap(short[] lump)
    {
        _lump = lump;
        if (lump.Length < HeaderSize)
            throw new WadFormatException($"BLOCKMAP is {lump.Length * 2} bytes, shorter than its header.");
        BmapOrgX = lump[0] << Fixed.FRACBITS;
        BmapOrgY = lump[1] << Fixed.FRACBITS;
        BmapWidth = lump[2];
        BmapHeight = lump[3];
        if (BmapWidth <= 0 || BmapHeight <= 0 || lump.Length < HeaderSize + BmapWidth * BmapHeight)
            throw new WadFormatException($"BLOCKMAP of {BmapWidth}×{BmapHeight} blocks has only {lump.Length - HeaderSize} offset/list values.");
        for (int b = 0; b < BmapWidth * BmapHeight; b++)
        {
            int offset = BlockOffset(b);
            int end = offset;
            while (end < lump.Length && lump[end] != -1)
                end++;
            if (offset < HeaderSize + BmapWidth * BmapHeight || end >= lump.Length)
                throw new WadFormatException($"BLOCKMAP block {b} list at {offset} is out of range or not terminated.");
        }
    }

    /// <summary><c>bmaporgx</c>: fixed_t x of the bottom left corner.</summary>
    public int BmapOrgX { get; }

    /// <summary><c>bmaporgy</c>: fixed_t y of the bottom left corner.</summary>
    public int BmapOrgY { get; }

    /// <summary><c>bmapwidth</c>: width in blocks.</summary>
    public int BmapWidth { get; }

    /// <summary><c>bmapheight</c>: height in blocks.</summary>
    public int BmapHeight { get; }

    /// <summary>The whole lump as 16-bit values (<c>blockmaplump</c>).</summary>
    public ReadOnlySpan<short> BlockmapLump => _lump;

    /// <summary>
    /// The line numbers in block (<paramref name="x"/>, <paramref name="y"/>),
    /// in list order, the leading 0 included, as <c>P_BlockLinesIterator</c>
    /// visits them (without its <c>validcount</c> check). Out-of-range blocks
    /// have no lines.
    /// </summary>
    public IEnumerable<int> BlockLines(int x, int y)
    {
        if (x < 0 || y < 0 || x >= BmapWidth || y >= BmapHeight)
            yield break;
        for (int i = BlockOffset(y * BmapWidth + x); _lump[i] != -1; i++)
            yield return (ushort)_lump[i];
    }

    // Vanilla reads the offset as a signed short, so a list past 32767 values
    // (64 KB) is out of its reach; read it unsigned, as later ports do.
    private int BlockOffset(int block) => (ushort)_lump[HeaderSize + block];
}

/// <summary>
/// The <c>REJECT</c> lump (p_setup.c <c>P_LoadReject</c>): one bit per ordered
/// sector pair; a set bit means no thing in the first sector can see one in
/// the second, so <c>P_CheckSight</c> gives up without tracing the BSP.
/// </summary>
public sealed class Reject
{
    internal Reject(byte[] rejectmatrix) => RejectMatrix = rejectmatrix;

    /// <summary>
    /// <c>rejectmatrix</c>, at least <c>(numsectors² + 7) / 8</c> bytes: a shorter lump is
    /// padded as Chocolate Doom's <c>PadRejectArray</c> does (see <see cref="Level"/>).
    /// </summary>
    public byte[] RejectMatrix { get; }

    /// <summary>
    /// p_sight.c <c>P_CheckSight</c>'s reject test: true when the bit for
    /// (<paramref name="s1"/>, <paramref name="s2"/>) is set, i.e. sight between
    /// them is rejected.
    /// </summary>
    public bool IsRejected(int s1, int s2, int numsectors)
    {
        int pnum = s1 * numsectors + s2;
        return (RejectMatrix[pnum >> 3] & (1 << (pnum & 7))) != 0;
    }
}
