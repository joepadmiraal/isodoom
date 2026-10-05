using System;

namespace IsoDoom.Wad.Graphics;

/// <summary>
/// Flats (floor/ceiling textures between <c>F_START</c>/<c>F_END</c>): raw
/// 64×64 palette indices, row-major, no header (r_draw.c / r_plane.c read
/// 4096 bytes per flat).
/// </summary>
public static class Flat
{
    public const int Width = 64;
    public const int Height = 64;
    public const int Size = Width * Height;

    /// <summary>
    /// Decodes a flat. Lumps longer than 4096 bytes are accepted and only the
    /// first 4096 bytes are used, as vanilla does; shorter ones are an error.
    /// </summary>
    public static IndexedImage Decode(ReadOnlySpan<byte> lump, string name = "flat")
    {
        if (lump.Length < Size)
            throw new WadFormatException($"{name}: flat is {lump.Length} bytes, expected {Size}.");
        byte[] opaque = new byte[Size];
        opaque.AsSpan().Fill(1);
        return new IndexedImage(Width, Height, 0, 0, lump[..Size].ToArray(), opaque);
    }

    /// <summary>Decodes the flat <paramref name="name"/> from the flat namespace.</summary>
    public static IndexedImage Load(WadArchive wad, string name)
    {
        WadLump lump = wad.Find(name, LumpNamespace.Flats)
            ?? throw new System.Collections.Generic.KeyNotFoundException($"Flat {name} not found.");
        return Decode(lump.Data.Span, lump.Name);
    }
}
