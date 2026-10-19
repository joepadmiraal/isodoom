using System;
using System.Collections.Generic;
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
/// PWAD's tables differing from the IWAD's, <see cref="Shade"/> counts, per
/// map, the expected pixels whose colour the PWAD's tables (decoded here,
/// apart from the scene's) give and the IWAD's would not, and those whose
/// colour the scene's lookup gives and the IWAD's would not; the drawn-pixel
/// comparisons hold the drawn pixels to the latter, so a map with the first
/// count above 0 and the second at 0 fails (P12c): its pixels never reached
/// the PWAD's entries.
/// </summary>
public partial class LevelCheck
{
    private bool _tablesChecked;
    private Playpal? _iwadPlaypal;
    private Colormap? _iwadColormap;
    private Playpal? _pwadPlaypal;
    private Colormap? _pwadColormap;

    /// <summary>P12c: per map, in the order first counted: the expected pixels the PWAD's tables colour apart from the IWAD's, and those the scene's lookup does.</summary>
    private readonly List<(string Map, long Differ, long Reached)> _pwadTableShades = [];

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
        // COLORMAP's trailing bytes past whole maps are dropped (Colormap.Decode), from the scene's and the GPU's copies alike.
        ReadOnlySpan<byte> cmapMaps = cmap.Data.AsSpan(0, cmap.Data.Length - cmap.Data.Length % Colormap.MapSize);
        if (!Playpal.Data.SequenceEqual(pal.Data))
            Fail($"the scene's PLAYPAL is not {pal.File}'s, the last file's that has one");
        if (!Colormap.Data.SequenceEqual(cmapMaps))
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
            // P12c: a texture of another size or format than the lump fails here, never throws.
            // The mesh's PLAYPAL texture has one row more, the blended flash palette (T6.13v, BlendRow).
            if (GpuTableMismatch(playpal, Image.Format.Rgb8, pal.Data, Playpal.PaletteSize) is { } palWhy)
                Fail($"{map}: the GPU's PLAYPAL is not {pal.File}'s: {palWhy}");
            if (GpuTableMismatch(colormap, Image.Format.R8, cmapMaps, 0) is { } cmapWhy)
                Fail($"{map}: the GPU's COLORMAP is not {cmap.File}'s: {cmapWhy}");
        }
        Playpal expectedPlaypal;
        Colormap expectedColormap;
        try
        {
            expectedPlaypal = Playpal.Decode(pal.Data);
            expectedColormap = Colormap.Decode(cmap.Data);
        }
        catch (WadFormatException e)
        {
            Fail($"{map}: the last PLAYPAL or COLORMAP does not decode: {e.Message}");
            return;
        }
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
        _pwadPlaypal = expectedPlaypal;
        _pwadColormap = expectedColormap;
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
    /// P12c: why a lookup texture's read-back image does not start with
    /// <paramref name="lump"/> and run <paramref name="extra"/> bytes past it
    /// (no image, another format, another length or other bytes), or null.
    /// </summary>
    private static string? GpuTableMismatch(Image? image, Image.Format format, ReadOnlySpan<byte> lump, int extra)
    {
        if (image is null)
            return "no image";
        if (image.GetFormat() != format)
            return $"format {image.GetFormat()}, expected {format}";
        byte[] data = image.GetData();
        if (data.Length != lump.Length + extra)
            return $"{data.Length} bytes ({image.GetWidth()} x {image.GetHeight()}), expected the lump's {lump.Length}{(extra > 0 ? $" and {extra} more" : "")}";
        return data.AsSpan(0, lump.Length).SequenceEqual(lump) ? null : "other bytes";
    }

    /// <summary>
    /// P5, P12c: counts, for the shown map, an expected pixel (index
    /// <paramref name="index"/> through COLORMAP row <paramref name="colormap"/>,
    /// in palette row <paramref name="row"/>) that the PWAD's tables colour
    /// apart from the IWAD's, and one the scene's lookup draws as
    /// <paramref name="drawn"/> apart from the IWAD's. Not for the blended
    /// flash row.
    /// </summary>
    private void CountPwadTableShade(byte index, int colormap, int row, (byte R, byte G, byte B) drawn)
    {
        if (_iwadPlaypal is not { } pal || _iwadColormap is not { } cmap || _pwadPlaypal is not { } pwadPal || _pwadColormap is not { } pwadCmap
            || row >= pal.Count || row >= pwadPal.Count || colormap >= cmap.Count || colormap >= pwadCmap.Count)
            return;
        (byte R, byte G, byte B) iwad = pal.GetColor(row, cmap.GetMap(colormap)[index]);
        bool differ = pwadPal.GetColor(row, pwadCmap.GetMap(colormap)[index]) != iwad;
        bool reached = drawn != iwad;
        if (!differ && !reached)
            return;
        string map = _scene.Mesh?.Level.Name ?? "no map";
        int i = _pwadTableShades.FindIndex(e => e.Map == map);
        if (i < 0)
        {
            i = _pwadTableShades.Count;
            _pwadTableShades.Add((map, 0, 0));
        }
        (string m, long d, long r) = _pwadTableShades[i];
        _pwadTableShades[i] = (m, d + (differ ? 1 : 0), r + (reached ? 1 : 0));
    }

    /// <summary>
    /// P12c: prints each map's counts and fails a map whose expected pixels
    /// the PWAD's tables colour apart from the IWAD's when none of them came
    /// out of the scene's lookup so: the drawn pixels never reached the
    /// PWAD's entries there.
    /// </summary>
    private void ReportPwadTableShades()
    {
        if (_iwadPlaypal is null)
            return;
        if (_pwadTableShades.Count == 0)
            GD.Print("Level check: palette tables (P5): no compared pixel in a colour where the PWAD's PLAYPAL and COLORMAP differ from the IWAD's");
        foreach ((string map, long differ, long reached) in _pwadTableShades)
        {
            GD.Print($"Level check: palette tables (P5): {map}: {reached} expected pixels in a colour the IWAD's PLAYPAL and COLORMAP would not give "
                + $"({differ} where the PWAD's tables differ from the IWAD's)");
            if (differ > 0 && reached == 0)
                Fail($"{map}: {differ} compared pixels fall where the PWAD's PLAYPAL and COLORMAP differ from the IWAD's, but none was expected in the PWAD's colour: the scene's lookup does not reach the PWAD's entries");
        }
    }
}
