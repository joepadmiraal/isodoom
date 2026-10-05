using System;

namespace IsoDoom.Wad.Graphics;

/// <summary>
/// A decoded 8-bit palette-indexed graphic (patch, sprite, UI graphic or flat).
/// <para>
/// <see cref="Pixels"/> holds one palette index per pixel, row-major
/// (<c>y * Width + x</c>). Index 0 is a real colour (black), so transparency is
/// kept separately in <see cref="Opaque"/>: 1 where a column post covers the
/// pixel, 0 where the patch is see-through. Flats are fully opaque.
/// </para>
/// <para>
/// <see cref="LeftOffset"/>/<see cref="TopOffset"/> are the patch's
/// <c>leftoffset</c>/<c>topoffset</c> (r_defs.h <c>patch_t</c>): how far the
/// graphic's origin sits to the right of its left edge and below its top edge.
/// </para>
/// </summary>
public sealed class IndexedImage
{
    public IndexedImage(int width, int height, int leftOffset, int topOffset, byte[] pixels, byte[] opaque)
    {
        if (width < 0 || height < 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width and height must not be negative.");
        if (pixels.Length != width * height || opaque.Length != width * height)
            throw new ArgumentException("Pixel and mask buffers must be width * height bytes.");
        Width = width;
        Height = height;
        LeftOffset = leftOffset;
        TopOffset = topOffset;
        Pixels = pixels;
        Opaque = opaque;
    }

    public int Width { get; }
    public int Height { get; }
    public int LeftOffset { get; }
    public int TopOffset { get; }

    /// <summary>Palette indices, row-major, <c>Width * Height</c> bytes.</summary>
    public byte[] Pixels { get; }

    /// <summary>1 where the pixel is drawn, 0 where it is transparent; same layout as <see cref="Pixels"/>.</summary>
    public byte[] Opaque { get; }

    public byte this[int x, int y] => Pixels[y * Width + x];

    public bool IsOpaque(int x, int y) => Opaque[y * Width + x] != 0;

    /// <summary>
    /// Converts to 8-bit RGBA (row-major, 4 bytes per pixel) through one
    /// <see cref="Playpal"/> palette and, optionally, a 256-byte colormap
    /// (e.g. <see cref="Colormap.GetMap"/>). Transparent pixels become (0,0,0,0).
    /// For debug exports and tests; the game applies the palette in a shader.
    /// </summary>
    public byte[] ToRgba(ReadOnlySpan<byte> palette, ReadOnlySpan<byte> colormap = default)
    {
        if (palette.Length != Playpal.PaletteSize)
            throw new ArgumentException($"A palette is {Playpal.PaletteSize} bytes.", nameof(palette));
        if (!colormap.IsEmpty && colormap.Length != Colormap.MapSize)
            throw new ArgumentException($"A colormap is {Colormap.MapSize} bytes.", nameof(colormap));

        byte[] rgba = new byte[Pixels.Length * 4];
        for (int i = 0; i < Pixels.Length; i++)
        {
            if (Opaque[i] == 0)
                continue;
            int index = colormap.IsEmpty ? Pixels[i] : colormap[Pixels[i]];
            rgba[i * 4] = palette[index * 3];
            rgba[i * 4 + 1] = palette[index * 3 + 1];
            rgba[i * 4 + 2] = palette[index * 3 + 2];
            rgba[i * 4 + 3] = 255;
        }
        return rgba;
    }
}
