using System;
using System.Linq;
using IsoDoom.Game;
using IsoDoom.Render;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Wad;

/// <summary>
/// P5 (docs/PWAD-VALIDATION.md): a PWAD's own <c>PLAYPAL</c> and
/// <c>COLORMAP</c> replace the IWAD's everywhere the game loads them (the
/// level scene and the viewer through <see cref="Playpal.Load"/> and
/// <see cref="Colormap.Load"/>, the menus' greyed text through
/// <see cref="ScreenGraphics.GreyMap"/>), and what is made from them (the
/// flashes' blend, the fog's dim mapping) is made from the PWAD's. The
/// WAD-less half builds a PWAD over a synthetic IWAD; the other half pins
/// D2TWID's tables over Doom II's (skipped without the set). The drawn pixels
/// are <c>--level-check</c>'s and <c>compare.sh --tables</c>'s.
/// </summary>
public sealed class PwadPaletteTests
{
    private static byte[] Palettes(int count, Func<int, int, byte> value)
    {
        var data = new byte[count * Playpal.PaletteSize];
        for (int i = 0; i < data.Length; i++)
            data[i] = value(i / Playpal.PaletteSize, i % Playpal.PaletteSize);
        return data;
    }

    private static byte[] Colormaps(Func<int, int, byte> value)
    {
        var data = new byte[Colormap.NumMaps * Colormap.MapSize];
        for (int i = 0; i < data.Length; i++)
            data[i] = value(i / Colormap.MapSize, i % Colormap.MapSize);
        return data;
    }

    [Fact]
    public void APwadsTablesReplaceTheIwads()
    {
        byte[] iwadPal = Palettes(Playpal.NumPalettes, (p, i) => (byte)(i / 3));
        byte[] pwadPal = Palettes(Playpal.NumPalettes, (p, i) => (byte)(255 - i / 3 + p));
        byte[] iwadMap = Colormaps((m, i) => (byte)i);
        byte[] pwadMap = Colormaps((m, i) => (byte)(255 - i));
        var iwad = new WadBuilder(WadType.Iwad).Lump("PLAYPAL", iwadPal).Lump("COLORMAP", iwadMap).ToWadFile("iwad.wad");
        var pwad = new WadBuilder().Lump("COLORMAP", pwadMap).Lump("PLAYPAL", pwadPal).ToWadFile("pwad.wad");
        WadArchive wad = new([iwad, pwad]);

        Playpal playpal = Playpal.Load(wad);
        Colormap colormap = Colormap.Load(wad);
        Assert.Equal(pwadPal, playpal.Data.ToArray());
        Assert.Equal(pwadMap, colormap.Data.ToArray());
        Assert.Equal(pwadMap.AsSpan(ScreenGraphics.GreyRow * Colormap.MapSize, Colormap.MapSize).ToArray(), new ScreenGraphics(wad, null).GreyMap.ToArray());
        Assert.Equal(pwadPal.AsSpan(4 * Playpal.PaletteSize, Playpal.PaletteSize).ToArray(), PaletteFlash.Blend(playpal, 4, PaletteFlash.FullStrength));

        // Only one of the two replaced: each table from the last file that has it.
        WadArchive half = new([iwad, new WadBuilder().Lump("COLORMAP", pwadMap).ToWadFile("half.wad")]);
        Assert.Equal(iwadPal, Playpal.Load(half).Data.ToArray());
        Assert.Equal(pwadMap, Colormap.Load(half).Data.ToArray());
    }

    [Fact]
    public void D2twidsTablesReplaceDoom2s()
    {
        PwadSet set = PwadSets.Get("d2twid");
        (string iwadPath, string[] pwads) = set.Require();
        WadArchive wad = WadArchive.Open(iwadPath, pwads);
        WadFile iwad = wad.Files[0], d2twid = wad.Files[^1];
        byte[] Lump(WadFile file, string name) => file.Lumps.Single(l => l.Name == name).Data.ToArray();

        Playpal playpal = Playpal.Load(wad), doom2Pal = Playpal.Decode(Lump(iwad, "PLAYPAL"));
        Colormap colormap = Colormap.Load(wad), doom2Map = Colormap.Decode(Lump(iwad, "COLORMAP"));
        Assert.Equal(Lump(d2twid, "PLAYPAL"), playpal.Data.ToArray());
        Assert.Equal(Lump(d2twid, "COLORMAP"), colormap.Data.ToArray());

        // D2TWID changes colours 250 and 255 in all 14 palettes: (255, 123, 255) and (167, 107, 107) become cyans.
        Assert.Equal(Playpal.NumPalettes, playpal.Count);
        for (int p = 0; p < playpal.Count; p++)
        {
            int[] changed = [.. Enumerable.Range(0, 256).Where(i => playpal.GetColor(p, i) != doom2Pal.GetColor(p, i))];
            Assert.Equal([250, 255], changed);
        }
        Assert.Equal(((byte)84, (byte)252, (byte)252), playpal.GetColor(0, 250));
        Assert.Equal(((byte)0, (byte)168, (byte)168), playpal.GetColor(0, 255));

        // Its COLORMAP no longer maps the dim reds to 255 (rows 4-32: light diminishing and invulnerability's 32).
        int[] rows = [.. Enumerable.Range(0, colormap.Count).Where(m => !colormap.GetMap(m).SequenceEqual(doom2Map.GetMap(m)))];
        Assert.Equal(Enumerable.Range(4, 29), rows);
        Assert.Equal(25, colormap.GetMap(6)[21]);
        Assert.Equal(255, doom2Map.GetMap(6)[21]);
        Assert.Equal(108, colormap.GetMap(Colormap.INVERSECOLORMAP)[247]);
        Assert.Equal(4, doom2Map.GetMap(Colormap.INVERSECOLORMAP)[247]);

        // What is made from them: the fog's dim mapping and the greyed menu text differ from Doom II's.
        FogDim look = FogDim.Default;
        Assert.NotEqual(FogOfWar.DimMap(doom2Pal, doom2Map, look.Row, look.Grey), FogOfWar.DimMap(playpal, colormap, look.Row, look.Grey));
        Assert.Equal(colormap.GetMap(ScreenGraphics.GreyRow).ToArray(), new ScreenGraphics(wad, null).GreyMap.ToArray());
    }
}
