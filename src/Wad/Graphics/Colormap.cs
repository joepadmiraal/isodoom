using System;

namespace IsoDoom.Wad.Graphics;

/// <summary>
/// The <c>COLORMAP</c> lump: 256-byte index-to-index remapping tables
/// (r_data.c <c>R_InitColormaps</c>). Doom IWADs hold 34: maps 0–31 go from
/// full bright to darkest (<c>NUMCOLORMAPS</c>), map 32 is the invulnerability
/// map (<c>INVERSECOLORMAP</c>) and map 33 is unused (all black).
/// </summary>
public sealed class Colormap
{
    /// <summary>Bytes per map.</summary>
    public const int MapSize = 256;

    /// <summary>Maps in a Doom IWAD's COLORMAP.</summary>
    public const int NumMaps = 34;

    /// <summary>Light-level maps (r_main.h).</summary>
    public const int NUMCOLORMAPS = 32;

    /// <summary>The invulnerability map (r_main.h).</summary>
    public const int INVERSECOLORMAP = 32;

    private readonly byte[] _data;

    private Colormap(byte[] data) => _data = data;

    /// <summary>
    /// Decodes a COLORMAP lump. It must hold at least the 32 light maps plus
    /// the invulnerability map; trailing bytes past whole maps are ignored, as
    /// vanilla only indexes into the start of the lump.
    /// </summary>
    public static Colormap Decode(ReadOnlySpan<byte> lump)
    {
        int count = lump.Length / MapSize;
        if (count < INVERSECOLORMAP + 1)
            throw new WadFormatException($"COLORMAP is {lump.Length} bytes; expected at least {(INVERSECOLORMAP + 1) * MapSize}.");
        return new Colormap(lump[..(count * MapSize)].ToArray());
    }

    /// <summary>Loads the <c>COLORMAP</c> lump from <paramref name="wad"/>.</summary>
    public static Colormap Load(WadArchive wad) => Decode(wad.W_CacheLumpName("COLORMAP").Span);

    public int Count => _data.Length / MapSize;

    /// <summary>All maps, back to back (<c>Count * 256</c> bytes), e.g. for a colormap texture.</summary>
    public ReadOnlySpan<byte> Data => _data;

    /// <summary>One 256-entry remapping table.</summary>
    public ReadOnlySpan<byte> GetMap(int map)
    {
        if ((uint)map >= (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(map));
        return _data.AsSpan(map * MapSize, MapSize);
    }
}
