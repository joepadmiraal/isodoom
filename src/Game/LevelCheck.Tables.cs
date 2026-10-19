using System;
using Godot;
using IsoDoom.Render;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

/// <summary>
/// P5: a PWAD's own <c>PLAYPAL</c> and <c>COLORMAP</c> (D2TWID replaces
/// both). The tables are found here apart from the scene's lookup: the
/// lump of the last file that has one, scanning the files back to front (as
/// w_wad.c's search from the end). The scene's tables, every material's
/// lookup textures (the level, masked, sprite and fuzz shaders: light
/// diminishing, the flashes, the fixed colormaps, the fuzz), with a renderer
/// their GPU copies, the fog's dim mapping, the HUD's and menus' palette
/// (the scene's) and the greyed menu text's map must be those. With the
/// PWAD's tables differing from the IWAD's, <see cref="Shade"/> counts the
/// expected pixels whose colour the IWAD's tables would not give, so the
/// drawn-pixel comparisons show they reached the PWAD's entries.
/// </summary>
public partial class LevelCheck
{
    private bool _tablesChecked;
    private Playpal? _iwadPlaypal;
    private Colormap? _iwadColormap;
    private long _pwadTableShades;

    /// <summary>The last lump called <paramref name="name"/> by file, back to front; and its file's name.</summary>
    private static (byte[] Data, string File)? LastLump(WadArchive wad, string name)
    {
        for (int f = wad.Files.Count - 1; f >= 0; f--)
        {
            WadFile file = wad.Files[f];
            for (int i = file.Lumps.Count - 1; i >= 0; i--)
            {
                if (file.Lumps[i].Name == name)
                    return (file.Lumps[i].Data.ToArray(), file.Name);
            }
        }
        return null;
    }

    private void CheckPaletteTables(LevelMesh m, string map)
    {
        if (_tablesChecked || _scene.Wad is not { } wad)
            return;
        _tablesChecked = true;
        if (LastLump(wad, "PLAYPAL") is not { } pal || LastLump(wad, "COLORMAP") is not { } cmap)
        {
            Fail("the WAD has no PLAYPAL or COLORMAP");
            return;
        }
        if (!Playpal.Data.SequenceEqual(pal.Data))
            Fail($"the scene's PLAYPAL is not {pal.File}'s, the last file's that has one");
        if (!Colormap.Data.SequenceEqual(cmap.Data))
            Fail($"the scene's COLORMAP is not {cmap.File}'s, the last file's that has one");
        foreach (ShaderMaterial material in m.Materials)
        {
            using Variant p = material.GetShaderParameter(StringNames.Get("playpal"));
            using Variant c = material.GetShaderParameter(StringNames.Get("colormap"));
            if (p.As<Texture2D>() != m.PlaypalTexture || c.As<Texture2D>() != m.ColormapTexture)
                Fail($"{map}: a material ({material.Shader.ResourcePath}) is not bound to the mesh's PLAYPAL and COLORMAP textures");
        }
        if (CanCapture)
        {
            Image? playpal = m.PlaypalTexture?.GetImage();
            using var keepPlaypal = GodotRefs.Keep(playpal); // T8.3d
            Image? colormap = m.ColormapTexture?.GetImage();
            using var keepColormap = GodotRefs.Keep(colormap);
            if (playpal is null || !playpal.GetData().AsSpan(0, pal.Data.Length).SequenceEqual(pal.Data))
                Fail($"{map}: the GPU's PLAYPAL is not {pal.File}'s");
            if (colormap is null || !colormap.GetData().AsSpan().SequenceEqual(cmap.Data))
                Fail($"{map}: the GPU's COLORMAP is not {cmap.File}'s");
        }
        var expectedPlaypal = Playpal.Decode(pal.Data);
        var expectedColormap = Colormap.Decode(cmap.Data);
        if (!m.FogDimMap.AsSpan().SequenceEqual(FogOfWar.DimMap(expectedPlaypal, expectedColormap, m.FogDimLook.Row, m.FogDimLook.Grey)))
            Fail($"{map}: the fog's dim mapping is not made from {pal.File}'s PLAYPAL and {cmap.File}'s COLORMAP");
        if (_scene.Graphics is { } graphics && !graphics.GreyMap.SequenceEqual(expectedColormap.GetMap(ScreenGraphics.GreyRow)))
            Fail($"the greyed menu text's map is not {cmap.File}'s COLORMAP row {ScreenGraphics.GreyRow}");

        string iwadName = wad.Files[0].Name;
        byte[]? iwadPal = null, iwadCmap = null;
        foreach (WadLump lump in wad.Files[0].Lumps)
        {
            if (lump.Name == "PLAYPAL")
                iwadPal = lump.Data.ToArray();
            else if (lump.Name == "COLORMAP")
                iwadCmap = lump.Data.ToArray();
        }
        if (pal.File == iwadName && cmap.File == iwadName || iwadPal is null || iwadCmap is null)
        {
            GD.Print($"Level check: palette tables (P5): PLAYPAL and COLORMAP from {iwadName}; the scene, the materials{(CanCapture ? " (and their GPU copies)" : "")}, the fog's dim mapping and the menus use them");
            return;
        }
        _iwadPlaypal = Playpal.Decode(iwadPal);
        _iwadColormap = Colormap.Decode(iwadCmap);
        int colours = 0, entries = 0;
        for (int i = 0; i + 2 < Math.Min(iwadPal.Length, pal.Data.Length); i += 3)
        {
            if (iwadPal[i] != pal.Data[i] || iwadPal[i + 1] != pal.Data[i + 1] || iwadPal[i + 2] != pal.Data[i + 2])
                colours++;
        }
        for (int i = 0; i < Math.Min(iwadCmap.Length, cmap.Data.Length); i++)
        {
            if (iwadCmap[i] != cmap.Data[i])
                entries++;
        }
        GD.Print($"Level check: palette tables (P5): PLAYPAL from {pal.File} ({colours} of {pal.Data.Length / 3} colours differ from {iwadName}'s), "
            + $"COLORMAP from {cmap.File} ({entries} of {cmap.Data.Length} entries differ); the scene, the materials{(CanCapture ? " (and their GPU copies)" : "")}, "
            + "the fog's dim mapping and the menus use them");
    }

    /// <summary>
    /// P5: counts an expected pixel (index <paramref name="index"/> through
    /// COLORMAP row <paramref name="colormap"/>, drawn in palette row
    /// <paramref name="row"/> as <paramref name="drawn"/>) that the IWAD's
    /// tables would draw in another colour. Not for the blended flash row.
    /// </summary>
    private void CountPwadTableShade(byte index, int colormap, int row, (byte R, byte G, byte B) drawn)
    {
        if (_iwadPlaypal is not { } pal || _iwadColormap is not { } cmap || row >= pal.Count || colormap >= cmap.Count)
            return;
        if (pal.GetColor(row, cmap.GetMap(colormap)[index]) != drawn)
            _pwadTableShades++;
    }
}
