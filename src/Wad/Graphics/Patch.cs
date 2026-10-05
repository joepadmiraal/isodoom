using System;
using System.Buffers.Binary;

namespace IsoDoom.Wad.Graphics;

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
    /// their absolute <c>topdelta</c>, as vanilla does (no "tall patch"
    /// extension). Pixels of a post that run past the patch height are
    /// dropped (vanilla would draw them below the patch). A header, column
    /// offset or post that runs past the end of the lump throws
    /// <see cref="WadFormatException"/>.
    /// </summary>
    public static IndexedImage Decode(ReadOnlySpan<byte> lump, string name = "patch")
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

                for (int i = 0; i < length; i++)
                {
                    int y = topDelta + i;
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
