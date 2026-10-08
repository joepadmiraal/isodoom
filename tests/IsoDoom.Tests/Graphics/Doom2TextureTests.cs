using System.Collections.Generic;
using System.Linq;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Graphics;

/// <summary>
/// T1.4a wall textures against a full IWAD: DOOM II (any version; skipped
/// without it). DOOM II has TEXTURE1 only, so the TEXTURE2 path stays covered
/// by the synthetic <see cref="TextureCompositionTests"/>. Counts and lists
/// that may differ between releases are asserted only for v1.666
/// (<see cref="TestWads.Doom2V1666Md5"/>); other versions get lower bounds.
/// </summary>
public class Doom2TextureTests
{
    private static readonly TextureCompositeMode[] _modes = [TextureCompositeMode.Vanilla, TextureCompositeMode.Corrected];

    // The see-through midtextures (grates, bars, the brown "small" grates).
    private static readonly string[] _maskedMidtextures =
        ["BRNSMAL1", "BRNSMAL2", "BRNSMALC", "BRNSMALL", "BRNSMALR", "MIDBARS1", "MIDBARS3", "MIDBRN1", "MIDBRONZ", "MIDGRATE", "MIDSPACE"];

    private static (WadArchive Wad, Textures Textures) OpenDoom2()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom2());
        return (wad, Textures.R_InitTextures(wad));
    }

    private static bool IsV1666 => TestWads.Doom2Md5 == TestWads.Doom2V1666Md5;

    private static IEnumerable<int> All(Textures t) => Enumerable.Range(0, t.NumTextures);

    [Fact]
    public void TextureDirectory()
    {
        (WadArchive wad, Textures t) = OpenDoom2();
        Assert.Equal(-1, wad.W_CheckNumForName("TEXTURE2"));
        Assert.All(t.PatchNames, n => Assert.True(wad.W_CheckNumForName(n) >= 0, $"PNAMES entry {n} has no lump")); // a full IWAD has every patch
        Assert.True(t.NumTextures >= 400, $"only {t.NumTextures} textures");

        TextureDef midgrate = t.TextureDefs[t.R_TextureNumForName("MIDGRATE")];
        Assert.Equal((128, 128), (midgrate.Width, midgrate.Height));
        Assert.Equal([("M1_1", 0, 0)], midgrate.Patches.Select(p => (p.PatchName, p.OriginX, p.OriginY)));

        if (IsV1666)
        {
            Assert.Equal(469, t.PatchNames.Count);
            Assert.Equal(428, t.NumTextures);
            Assert.Equal("AASHITTY", t.TextureDefs[0].Name);
            Assert.All(t.TextureDefs, d => Assert.False(d.Masked)); // like shareware, no texture sets the masked flag
        }
    }

    [Fact]
    public void EveryTextureComposites()
    {
        (_, Textures t) = OpenDoom2();
        foreach (int i in All(t))
        {
            foreach (TextureCompositeMode mode in _modes)
            {
                IndexedImage img = t.R_GenerateComposite(i, mode);
                Assert.Equal((t.TextureDefs[i].Width, t.TextureDefs[i].Height), (img.Width, img.Height));
                Assert.Equal(img.Width * img.Height, img.Pixels.Length);
            }
        }
    }

    [Fact]
    public void MidgrateHasHolesAndNoSeams()
    {
        (WadArchive wad, Textures t) = OpenDoom2();
        IndexedImage patch = Patch.Load(wad, "M1_1");
        foreach (TextureCompositeMode mode in _modes)
        {
            IndexedImage img = t.GetComposite("MIDGRATE", mode);
            // One 128×128 patch at (0,0): the composite is the patch, pixel for pixel and hole for hole.
            Assert.Equal((patch.Width, patch.Height), (img.Width, img.Height));
            Assert.Equal(patch.Opaque, img.Opaque);
            for (int i = 0; i < img.Pixels.Length; i++)
            {
                if (img.Opaque[i] != 0)
                    Assert.Equal(patch.Pixels[i], img.Pixels[i]);
            }
            Assert.InRange(img.Opaque.Count(b => b == 0), img.Opaque.Length / 4, img.Opaque.Length * 3 / 4);
        }
    }

    [Theory]
    [MemberData(nameof(MaskedMidtextureNames))]
    public void MaskedMidtexturesHaveHoles(string name)
    {
        (_, Textures t) = OpenDoom2();
        foreach (TextureCompositeMode mode in _modes)
        {
            IndexedImage img = t.GetComposite(name, mode);
            Assert.InRange(img.Opaque.Count(b => b == 0), img.Opaque.Length / 20, img.Opaque.Length * 9 / 10);
        }
    }

    public static TheoryData<string> MaskedMidtextureNames() => [.. _maskedMidtextures];

    /// <summary>
    /// Multi-patch textures leave no gaps between their patches: all are fully
    /// opaque except SKINEDGE, whose one hole is a missing pixel in its patch
    /// HELL8_1. (DOOM II has no multi-patch masked midtexture.)
    /// </summary>
    [Fact]
    public void MultiPatchTexturesHaveNoGaps()
    {
        (_, Textures t) = OpenDoom2();
        foreach (int i in All(t).Where(i => t.TextureDefs[i].Patches.Count > 1 && t.TextureDefs[i].Name != "SKINEDGE"))
        {
            foreach (TextureCompositeMode mode in _modes)
                Assert.True(t.R_GenerateComposite(i, mode).Opaque.All(b => b != 0), $"{t.TextureDefs[i].Name} ({mode}) has transparent pixels");
        }
    }

    [Fact]
    public void OnlyTheMaskedMidtexturesHaveHolesInV1666()
    {
        (WadArchive wad, Textures t) = OpenDoom2();
        Assert.SkipUnless(IsV1666, "Hole list is for DOOM II v1.666.");
        foreach (TextureCompositeMode mode in _modes)
        {
            string[] withHoles = [.. All(t).Where(i => t.R_GenerateComposite(i, mode).Opaque.Any(b => b == 0))
                .Select(i => t.TextureDefs[i].Name).Order()];
            // Plus three single missing pixels in the patch data itself.
            Assert.Equal(_maskedMidtextures.Concat(["SKINEDGE", "SKY2", "ZZZFACE3"]).Order(), withHoles);
        }

        Assert.Equal(1, t.GetComposite("SKINEDGE").Opaque.Count(b => b == 0));
        Assert.False(t.GetComposite("SKINEDGE").IsOpaque(73, 36));
        Assert.False(Patch.Load(wad, "HELL8_1").IsOpaque(9, 36)); // HELL8_1 sits at x = 64
        Assert.False(t.GetComposite("SKY2").IsOpaque(134, 59));
        Assert.False(t.GetComposite("SKY2").IsOpaque(134, 64));
        Assert.False(t.GetComposite("ZZZFACE3").IsOpaque(204, 94));
    }

    [Fact]
    public void VanillaAndCorrectedDifferOnlyWhereOriginYIsUsedInV1666()
    {
        (_, Textures t) = OpenDoom2();
        Assert.SkipUnless(IsV1666, "Difference list is for DOOM II v1.666.");
        string[] differ = [.. All(t).Where(i =>
            {
                IndexedImage v = t.R_GenerateComposite(i), c = t.R_GenerateComposite(i, TextureCompositeMode.Corrected);
                return !v.Pixels.SequenceEqual(c.Pixels) || !v.Opaque.SequenceEqual(c.Opaque);
            })
            .Select(i => t.TextureDefs[i].Name).Order()];
        Assert.Equal(["BROWN144", "GRAY2", "GRAYVINE", "STEP2", "SW1DIRT", "SW1VINE", "SW2DIRT", "SW2VINE", "TEKWALL1"], differ);
        Assert.All(differ, n => Assert.Contains(t.TextureDefs[t.R_TextureNumForName(n)].Patches, p => p.OriginY != 0));
    }

    /// <summary>
    /// Debug export for the T1.4a visual check: writes <c>DOOM2_TEX_*.png</c>
    /// to <c>TestResults/graphics/</c> (gitignored). The masked midtextures
    /// are written over a magenta checkerboard so holes show; MIDGRATE also
    /// plain and tiled 2×2 (seams would show at the tile edges). SKINEDGE is
    /// the only multi-patch texture with a hole; GRAY2 and SW1VINE are
    /// multi-patch textures with overlapping patches, in both composite modes.
    /// </summary>
    [Fact]
    public void ExportDebugPngs()
    {
        (WadArchive wad, Textures t) = OpenDoom2();
        string dir = DebugPng.RequireOutputDir();
        byte[] palette = Playpal.Load(wad).GetPalette(0).ToArray();

        DebugPng.Write(dir, "DOOM2_TEX_MIDGRATE_x4.png", t.GetComposite("MIDGRATE"), palette);
        DebugPng.Write(dir, "DOOM2_TEX_MIDGRATE_x2_tiled_checker.png", t.GetComposite("MIDGRATE"), palette, scale: 2, checker: true, tilesX: 2, tilesY: 2);
        foreach (string name in new[] { "MIDGRATE", "MIDBARS1", "MIDBRONZ", "MIDSPACE", "BRNSMALC" })
            DebugPng.Write(dir, $"DOOM2_TEX_{name}_x4_checker.png", t.GetComposite(name), palette, checker: true);
        DebugPng.Write(dir, "DOOM2_TEX_SKINEDGE_x4_checker.png", t.GetComposite("SKINEDGE"), palette, checker: true);
        foreach (string name in new[] { "GRAY2", "SW1VINE" })
        {
            DebugPng.Write(dir, $"DOOM2_TEX_{name}_x4_vanilla.png", t.GetComposite(name), palette, checker: true);
            DebugPng.Write(dir, $"DOOM2_TEX_{name}_x4_corrected.png", t.GetComposite(name, TextureCompositeMode.Corrected), palette, checker: true);
        }
    }
}
