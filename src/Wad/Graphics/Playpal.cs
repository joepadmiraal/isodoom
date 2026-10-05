using System;

namespace IsoDoom.Wad.Graphics;

/// <summary>
/// The <c>PLAYPAL</c> lump: 256-colour RGB palettes, 768 bytes each
/// (v_video.c / st_stuff.c). Doom IWADs hold 14: palette 0 is the normal one,
/// 1–8 the red damage/berserk tints (<c>STARTREDPALS</c>), 9–12 the gold pickup
/// tints (<c>STARTBONUSPALS</c>) and 13 the green radiation suit tint
/// (<c>RADIATIONPAL</c>).
/// </summary>
public sealed class Playpal
{
    /// <summary>Bytes per palette (256 colours × RGB).</summary>
    public const int PaletteSize = 256 * 3;

    /// <summary>Palettes in a Doom IWAD's PLAYPAL.</summary>
    public const int NumPalettes = 14;

    // st_stuff.c
    public const int STARTREDPALS = 1;
    public const int NUMREDPALS = 8;
    public const int STARTBONUSPALS = 9;
    public const int NUMBONUSPALS = 4;
    public const int RADIATIONPAL = 13;

    private readonly byte[] _data;

    private Playpal(byte[] data) => _data = data;

    /// <summary>
    /// Decodes a PLAYPAL lump. Any whole number of palettes (at least one) is
    /// accepted; PWADs normally keep all 14.
    /// </summary>
    public static Playpal Decode(ReadOnlySpan<byte> lump)
    {
        if (lump.Length < PaletteSize || lump.Length % PaletteSize != 0)
            throw new WadFormatException($"PLAYPAL is {lump.Length} bytes; expected a whole number of {PaletteSize}-byte palettes.");
        return new Playpal(lump.ToArray());
    }

    /// <summary>Loads the <c>PLAYPAL</c> lump from <paramref name="wad"/>.</summary>
    public static Playpal Load(WadArchive wad) => Decode(wad.W_CacheLumpName("PLAYPAL").Span);

    public int Count => _data.Length / PaletteSize;

    /// <summary>All palettes, back to back (<c>Count * 768</c> bytes), e.g. for a palette texture.</summary>
    public ReadOnlySpan<byte> Data => _data;

    /// <summary>One palette: 256 RGB triplets.</summary>
    public ReadOnlySpan<byte> GetPalette(int palette)
    {
        if ((uint)palette >= (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(palette));
        return _data.AsSpan(palette * PaletteSize, PaletteSize);
    }

    /// <summary>The RGB colour of <paramref name="index"/> in <paramref name="palette"/>.</summary>
    public (byte R, byte G, byte B) GetColor(int palette, int index)
    {
        if ((uint)index > 255)
            throw new ArgumentOutOfRangeException(nameof(index));
        ReadOnlySpan<byte> pal = GetPalette(palette);
        return (pal[index * 3], pal[index * 3 + 1], pal[index * 3 + 2]);
    }
}
