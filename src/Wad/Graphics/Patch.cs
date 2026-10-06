using System;
using System.Buffers.Binary;

namespace IsoDoom.Wad.Graphics;

/// <summary>How a patch post's <c>topdelta</c> is read (see SPEC §12, T1.3a).</summary>
public enum PatchTopDeltaMode
{
    /// <summary>
    /// The DeePsea "tall patch" convention (as in PrBoom+, ZDoom, Eternity and
    /// most other source ports): a <c>topdelta</c> not greater than the previous
    /// post's top is relative to that top, so a column can reach past row 254.
    /// The first post is always absolute. Identical to <see cref="Vanilla"/> for
    /// every patch whose posts go strictly downwards, which includes every IWAD
    /// graphic.
    /// </summary>
    Tall,

    /// <summary>Every <c>topdelta</c> is absolute, as in vanilla and Chocolate Doom.</summary>
    Vanilla,
}

/// <summary>
/// Decoder for the patch picture format used by wall patches, sprites and UI
/// graphics (r_defs.h <c>patch_t</c>/<c>post_t</c>, drawn by v_video.c
/// <c>V_DrawPatch</c> and r_things.c <c>R_DrawMaskedColumn</c>).
/// <para>
/// Header: <c>short width, height, leftoffset, topoffset</c>, then
/// <c>int columnofs[width]</c> (byte offsets from the start of the lump).
/// Each column is a list of posts: <c>byte topdelta</c> (0xFF ends the column),
/// <c>byte length</c>, an unused pad byte, <c>length</c> pixels and another pad
/// byte.
/// </para>
/// </summary>
public static class Patch
{
    private const int HeaderSize = 8;

    /// <summary>
    /// Decodes a patch into an <see cref="IndexedImage"/>. Posts are placed at
    /// their <c>topdelta</c>, read as <paramref name="mode"/> says (by default
    /// with DeePsea tall-patch support, see <see cref="PatchTopDeltaMode"/>;
    /// <see cref="PatchTopDeltaMode.Vanilla"/> places every post at its
    /// absolute <c>topdelta</c>). Pixels of a post that run past the patch height are
    /// dropped (vanilla would draw them below the patch). A header, column
    /// offset or post that runs past the end of the lump throws
    /// <see cref="WadFormatException"/>.
    /// </summary>
    public static IndexedImage Decode(ReadOnlySpan<byte> lump, string name = "patch",
        PatchTopDeltaMode mode = PatchTopDeltaMode.Tall)
    {
        if (lump.Length < HeaderSize)
            throw new WadFormatException($"{name}: {lump.Length} bytes is too short for a patch header.");

        int width = BinaryPrimitives.ReadInt16LittleEndian(lump);
        int height = BinaryPrimitives.ReadInt16LittleEndian(lump[2..]);
        int leftOffset = BinaryPrimitives.ReadInt16LittleEndian(lump[4..]);
        int topOffset = BinaryPrimitives.ReadInt16LittleEndian(lump[6..]);
        if (width <= 0 || height <= 0)
            throw new WadFormatException($"{name}: bad patch size {width}x{height}.");
        if (HeaderSize + 4L * width > lump.Length)
            throw new WadFormatException($"{name}: column offsets for width {width} run past the end of the lump.");

        byte[] pixels = new byte[width * height];
        byte[] opaque = new byte[width * height];

        for (int x = 0; x < width; x++)
        {
            int ofs = BinaryPrimitives.ReadInt32LittleEndian(lump[(HeaderSize + 4 * x)..]);
            if (ofs < HeaderSize || ofs >= lump.Length)
                throw new WadFormatException($"{name}: column {x} offset {ofs} is outside the lump.");

            // r_things.c R_DrawMaskedColumn: for ( ; column->topdelta != 0xff ; )
            int top = -1;
            while (true)
            {
                if (ofs >= lump.Length)
                    throw new WadFormatException($"{name}: column {x} has no 0xFF terminator.");
                int topDelta = lump[ofs];
                if (topDelta == 0xFF)
                    break;
                if (ofs + 1 >= lump.Length)
                    throw new WadFormatException($"{name}: column {x} post header runs past the end of the lump.");
                int length = lump[ofs + 1];
                int source = ofs + 3; // skip topdelta, length and the pad byte
                if (source + length > lump.Length)
                    throw new WadFormatException($"{name}: column {x} post runs past the end of the lump.");
                top = PostTop(topDelta, top, mode);

                for (int i = 0; i < length; i++)
                {
                    int y = top + i;
                    if (y >= height)
                        break;
                    pixels[y * width + x] = lump[source + i];
                    opaque[y * width + x] = 1;
                }
                ofs = source + length + 1; // skip the trailing pad byte
            }
        }

        return new IndexedImage(width, height, leftOffset, topOffset, pixels, opaque);
    }

    /// <summary>
    /// A strict structural check for "is this lump a patch?", for finding the
    /// graphics among the global lumps (<see cref="GraphicLumps"/>), which
    /// have no marker namespace. Stricter than <see cref="Decode"/>: the size
    /// must be 1..4096 on each side, every column offset must point past the
    /// offset table and inside the lump, and every column's posts must end
    /// with a 0xFF terminator inside the lump, with no post starting below
    /// the patch height (post tops read as <paramref name="mode"/> says).
    /// Never throws.
    /// </summary>
    public static bool IsPatch(ReadOnlySpan<byte> lump, PatchTopDeltaMode mode = PatchTopDeltaMode.Tall)
    {
        if (lump.Length < HeaderSize)
            return false;
        int width = BinaryPrimitives.ReadInt16LittleEndian(lump);
        int height = BinaryPrimitives.ReadInt16LittleEndian(lump[2..]);
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
            return false;
        int tableEnd = HeaderSize + 4 * width;
        if (tableEnd > lump.Length)
            return false;

        for (int x = 0; x < width; x++)
        {
            int ofs = BinaryPrimitives.ReadInt32LittleEndian(lump[(HeaderSize + 4 * x)..]);
            if (ofs < tableEnd || ofs >= lump.Length)
                return false;
            int top = -1;
            while (true)
            {
                if (ofs >= lump.Length)
                    return false;
                int topDelta = lump[ofs];
                if (topDelta == 0xFF)
                    break;
                top = PostTop(topDelta, top, mode);
                if (top >= height || ofs + 1 >= lump.Length)
                    return false;
                int length = lump[ofs + 1];
                ofs += 3 + length + 1;
                if (ofs > lump.Length)
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// The row a post starts at, given its raw <paramref name="topDelta"/> and
    /// the previous post's top (-1 for a column's first post). Tall patches
    /// (DeePsea convention, as PrBoom+'s r_patch.c reads them):
    /// <c>if (topdelta &lt;= top) top += topdelta; else top = topdelta;</c>.
    /// </summary>
    internal static int PostTop(int topDelta, int previousTop, PatchTopDeltaMode mode) =>
        mode == PatchTopDeltaMode.Tall && topDelta <= previousTop ? previousTop + topDelta : topDelta;

    /// <summary>Decodes the last lump called <paramref name="name"/> (any namespace) as a patch.</summary>
    public static IndexedImage Load(WadArchive wad, string name)
    {
        WadLump lump = wad.Find(name)
            ?? throw new System.Collections.Generic.KeyNotFoundException($"Patch {name} not found.");
        return Decode(lump.Data.Span, lump.Name);
    }

    /// <summary>Decodes the sprite <paramref name="name"/> from the sprite namespace.</summary>
    public static IndexedImage LoadSprite(WadArchive wad, string name)
    {
        WadLump lump = wad.Find(name, LumpNamespace.Sprites)
            ?? throw new System.Collections.Generic.KeyNotFoundException($"Sprite {name} not found.");
        return Decode(lump.Data.Span, lump.Name);
    }
}
