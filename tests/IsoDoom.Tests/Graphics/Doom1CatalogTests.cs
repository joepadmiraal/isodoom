using System.Collections.Generic;
using System.Linq;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Graphics;

/// <summary>T1.6 viewer catalog against the shareware v1.9 DOOM1.WAD (skipped without it).</summary>
public class Doom1CatalogTests
{
    private static GraphicsCatalog OpenDoom1() => GraphicsCatalog.Load(WadArchive.Open(TestWads.RequireDoom1()));

    [Fact]
    public void CategoryCounts()
    {
        GraphicsCatalog c = OpenDoom1();
        Assert.Equal(125, c.Count(GraphicCategory.Textures));
        Assert.Equal(54, c.Count(GraphicCategory.Flats));
        Assert.Equal(61, c.Count(GraphicCategory.Sprites));
        Assert.Equal(164, c.Count(GraphicCategory.Patches));
        Assert.Equal(320, c.Count(GraphicCategory.Graphics));
        Assert.Equal(2, c.Count(GraphicCategory.Palette));
    }

    [Fact]
    public void GraphicsHoldTheUiLumpsAndNothingElse()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom1());
        string[] names = [.. GraphicLumps.FindGlobalPatches(wad).Select(l => l.Name)];
        foreach (string expected in new[] { "TITLEPIC", "CREDIT", "HELP1", "HELP2", "STBAR", "STARMS", "M_DOOM", "M_SKULL1",
                     "STCFN033", "STCFN095", "STTNUM0", "STFST00", "WIMAP0", "WIA00000", "AMMNUM0", "BRDR_TL" })
            Assert.Contains(expected, names);

        // Every global lump that is not one of these is a known non-graphic (maps, sounds, music, tables).
        IEnumerable<WadLump> rest = wad.Lumps.Where(l => l.Namespace == LumpNamespace.Global && !l.IsMarker && !names.Contains(l.Name));
        Assert.All(rest, l => Assert.True(GraphicLumps.IsNonGraphicName(l.Name), l.Name));
    }

    [Fact]
    public void EveryViewDecodes()
    {
        GraphicsCatalog c = OpenDoom1();
        GraphicView[] views = [.. c.EnumerateAll()];
        Assert.All(views, v => Assert.True(v.Image.Width > 0 && v.Image.Height > 0, v.Label));

        // 125 + 54 + 164 + 320 + 2 non-sprite views; every sprite frame/rotation slot.
        int spriteViews = views.Count(v => v.Category == GraphicCategory.Sprites);
        Assert.Equal(c.Sprites.SpriteDefs.SelectMany(d => d.Frames).Sum(f => f.Rotate ? 8 : 1), spriteViews);
        Assert.Equal(665 + spriteViews, views.Length);

        // Every sprite lump shows up, and the mirrored halves of pairs are flipped.
        Assert.Equal(483, views.Where(v => v.Category == GraphicCategory.Sprites).Select(v => v.Name).Distinct().Count());
        Assert.Contains(views, v => v.Flip);
    }

    [Fact]
    public void MirroredRotationIsFlipped()
    {
        GraphicsCatalog c = OpenDoom1();
        int troo = Enumerable.Range(0, c.Count(GraphicCategory.Sprites)).First(i => c.GetName(GraphicCategory.Sprites, i) == "TROO");
        GraphicView rot2 = c.GetSprite(troo, 0, 1);
        GraphicView rot8 = c.GetSprite(troo, 0, 7);
        Assert.Equal(("TROOA2A8", false), (rot2.Name, rot2.Flip));
        Assert.Equal(("TROOA2A8", true), (rot8.Name, rot8.Flip));
        Assert.Equal(("TROOA1", false), (c.GetSprite(troo, 0, 0).Name, c.GetSprite(troo, 0, 0).Flip));
    }

    [Fact]
    public void PaletteViews()
    {
        GraphicsCatalog c = OpenDoom1();
        GraphicView pal = c.Get(GraphicCategory.Palette, 0);
        Assert.Equal((16, 16), (pal.Image.Width, pal.Image.Height));
        Assert.Equal(Enumerable.Range(0, 256).Select(i => (byte)i), pal.Image.Pixels);
        GraphicView cm = c.Get(GraphicCategory.Palette, 1);
        Assert.Equal((256, 34), (cm.Image.Width, cm.Image.Height));
        Assert.Equal(c.Colormap.GetMap(32)[176], cm.Image[176, 32]);
    }
}
