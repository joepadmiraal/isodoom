using System;
using System.Collections.Generic;

namespace IsoDoom.Wad.Graphics;

/// <summary>
/// Finds the patch-format graphics outside the marker namespaces: the UI,
/// menu, status bar, intermission and font graphics (<c>TITLEPIC</c>,
/// <c>STBAR</c>, <c>M_DOOM</c>, <c>WI*</c>, <c>STCFN*</c>, …; SPEC §5.2).
/// Vanilla never lists them, it loads each by name; the WAD viewer (T1.6)
/// needs them all, so they are found by structure: every global lump whose
/// name is not a known non-graphic lump and that passes
/// <see cref="Patch.IsPatch"/>.
/// </summary>
public static class GraphicLumps
{
    // Map data lumps that follow an ExMy/MAPxx marker (doomdata.h ML_*).
    private static readonly string[] _mapLumps =
    [
        "THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES", "SEGS",
        "SSECTORS", "NODES", "SECTORS", "REJECT", "BLOCKMAP",
    ];

    // Whole lumps known not to be patches.
    private static readonly string[] _nonGraphicLumps =
    [
        "PLAYPAL", "COLORMAP", "ENDOOM", "GENMIDI", "PNAMES", "TEXTURE1", "TEXTURE2",
        "DMXGUS", "DMXGUSC",
    ];

    /// <summary>
    /// True for lumps whose name says they are not a graphic: map markers and
    /// map data, palettes and tables, demos (<c>DEMO*</c>), music (<c>D_*</c>)
    /// and sound effects (<c>DS*</c>, PC speaker <c>DP*</c>).
    /// </summary>
    public static bool IsNonGraphicName(string name)
    {
        if (Array.IndexOf(_mapLumps, name) >= 0 || Array.IndexOf(_nonGraphicLumps, name) >= 0)
            return true;
        if (name.StartsWith("DEMO", StringComparison.Ordinal) || name.StartsWith("D_", StringComparison.Ordinal)
            || name.StartsWith("DS", StringComparison.Ordinal) || name.StartsWith("DP", StringComparison.Ordinal))
            return true;
        if (name.Length == 4 && name[0] == 'E' && name[2] == 'M' && char.IsAsciiDigit(name[1]) && char.IsAsciiDigit(name[3]))
            return true; // ExMy
        if (name.Length == 5 && name.StartsWith("MAP", StringComparison.Ordinal)
            && char.IsAsciiDigit(name[3]) && char.IsAsciiDigit(name[4]))
            return true; // MAPxx
        return false;
    }

    /// <summary>
    /// The global (non-namespace, non-marker) patch lumps, one per name. A
    /// later lump with the same name replaces an earlier one in place, as the
    /// namespace lists do, so the order is the IWAD's with PWAD additions at
    /// the end, and each entry is the lump vanilla's <c>W_CacheLumpName</c>
    /// would return for a global graphic.
    /// </summary>
    public static IReadOnlyList<WadLump> FindGlobalPatches(WadArchive wad)
    {
        var result = new List<WadLump>();
        var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (WadLump lump in wad.Lumps)
        {
            if (lump.Namespace != LumpNamespace.Global || lump.IsMarker || lump.Size == 0)
                continue;
            if (IsNonGraphicName(lump.Name) || !Patch.IsPatch(lump.Data.Span))
                continue;
            if (indexByName.TryGetValue(lump.Name, out int index))
            {
                result[index] = lump;
            }
            else
            {
                indexByName[lump.Name] = result.Count;
                result.Add(lump);
            }
        }
        return result;
    }
}
