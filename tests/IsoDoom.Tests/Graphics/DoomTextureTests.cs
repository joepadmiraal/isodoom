using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Graphics;

/// <summary>
/// T1.4b wall textures against a registered or Ultimate Doom IWAD (any
/// version; skipped without it, <see cref="TestWads.RequireDoom"/>). Unlike
/// DOOM II's, its textures come from two lumps, <c>TEXTURE1</c> and
/// <c>TEXTURE2</c>, so this is the real-data check of the TEXTURE2 path.
/// Counts and lists that may differ between releases are asserted only for
/// the 2024 re-release (<see cref="TestWads.DoomUltimate2024Md5"/>).
/// </summary>
public class DoomTextureTests
{
    private static readonly TextureCompositeMode[] _modes = [TextureCompositeMode.Vanilla, TextureCompositeMode.Corrected];

    // The see-through midtextures (grates, vines, the brown "big" and "small" grates).
    private static readonly string[] _maskedMidtextures =
    [
        "BRNBIGC", "BRNBIGL", "BRNBIGR", "BRNSMAL1", "BRNSMAL2", "BRNSMALC", "BRNSMALL", "BRNSMALR",
        "MIDBRN1", "MIDGRATE", "MIDVINE1", "MIDVINE2",
    ];

    // Multi-patch textures with a hole: a single missing pixel in one of their patches.
    private static readonly string[] _multiPatchWithHoles = ["SKINEDGE", "SKINTEK1"];

    private static (WadArchive Wad, Textures Textures) OpenDoom()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom());
        return (wad, Textures.R_InitTextures(wad));
    }

    private static bool Is2024 => TestWads.DoomMd5 == TestWads.DoomUltimate2024Md5;

    private static IEnumerable<int> All(Textures t) => Enumerable.Range(0, t.NumTextures);

    /// <summary>The number of textures a TEXTURE1/TEXTURE2 lump lists (its first int).</summary>
    private static int Count(WadArchive wad, string lump) =>
        BinaryPrimitives.ReadInt32LittleEndian(wad.Lumps[wad.W_GetNumForName(lump)].Data.Span);

    [Fact]
    public void TextureDirectory()
    {
        (WadArchive wad, Textures t) = OpenDoom();
        int n1 = Count(wad, "TEXTURE1"), n2 = Count(wad, "TEXTURE2");
        Assert.True(n2 > 0, "no TEXTURE2 textures");
        Assert.Equal(n1 + n2, t.NumTextures); // TEXTURE1's, then TEXTURE2's (r_data.c)
        Assert.Equal(t.NumTextures, t.TextureDefs.Select(d => d.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(t.PatchNames, n => Assert.True(wad.W_CheckNumForName(n) >= 0, $"PNAMES entry {n} has no lump")); // unlike shareware's

        // The masked midtextures are TEXTURE2's: MIDGRATE is one 128×128 patch.
        TextureDef midgrate = t.TextureDefs[t.R_TextureNumForName("MIDGRATE")];
        Assert.True(t.R_TextureNumForName("MIDGRATE") >= n1);
        Assert.Equal((128, 128, 1), (midgrate.Width, midgrate.Height, midgrate.Patches.Count));

        if (Is2024)
        {
            Assert.Equal(351, t.PatchNames.Count);
            Assert.Equal((125, 162), (n1, n2));
            Assert.Equal("AASTINKY", t.TextureDefs[0].Name);
            Assert.Equal("ASHWALL", t.TextureDefs[n1].Name);
            Assert.All(t.TextureDefs, d => Assert.False(d.Masked)); // as shareware and DOOM II: no masked flags
        }
    }

    [Fact]
    public void EveryTextureComposites()
    {
        (_, Textures t) = OpenDoom();
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

    /// <summary>A one-patch TEXTURE2 texture at (0, 0) as big as its patch is the patch, pixel for pixel and hole for hole.</summary>
    [Fact]
    public void SinglePatchTexture2TexturesAreTheirPatch()
    {
        (WadArchive wad, Textures t) = OpenDoom();
        int n1 = Count(wad, "TEXTURE1"), checkedCount = 0;
        foreach (int i in All(t).Skip(n1))
        {
            TextureDef d = t.TextureDefs[i];
            if (d.Patches is not [{ OriginX: 0, OriginY: 0 } p])
                continue;
            IndexedImage patch = Patch.Load(wad, p.PatchName);
            if ((patch.Width, patch.Height) != (d.Width, d.Height))
                continue;
            checkedCount++;
            foreach (TextureCompositeMode mode in _modes)
            {
                IndexedImage img = t.R_GenerateComposite(i, mode);
                Assert.Equal(patch.Opaque, img.Opaque);
                for (int k = 0; k < img.Pixels.Length; k++)
                {
                    if (img.Opaque[k] != 0)
                        Assert.Equal(patch.Pixels[k], img.Pixels[k]);
                }
            }
        }
        Assert.True(checkedCount >= 10, $"only {checkedCount} single-patch TEXTURE2 textures");
    }

    [Theory]
    [MemberData(nameof(MaskedMidtextureNames))]
    public void MaskedMidtexturesHaveHoles(string name)
    {
        (_, Textures t) = OpenDoom();
        foreach (TextureCompositeMode mode in _modes)
        {
            IndexedImage img = t.GetComposite(name, mode);
            Assert.InRange(img.Opaque.Count(b => b == 0), img.Opaque.Length / 20, img.Opaque.Length * 9 / 10);
        }
    }

    public static TheoryData<string> MaskedMidtextureNames() => [.. _maskedMidtextures];

    /// <summary>
    /// Multi-patch textures leave no gaps between their patches: all are fully
    /// opaque but SKINEDGE and SKINTEK1, each with one missing pixel in a
    /// patch. (As DOOM II, Doom has no multi-patch masked midtexture.)
    /// </summary>
    [Fact]
    public void MultiPatchTexturesHaveNoGaps()
    {
        (_, Textures t) = OpenDoom();
        foreach (int i in All(t).Where(i => t.TextureDefs[i].Patches.Count > 1 && !_multiPatchWithHoles.Contains(t.TextureDefs[i].Name)))
        {
            foreach (TextureCompositeMode mode in _modes)
                Assert.True(t.R_GenerateComposite(i, mode).Opaque.All(b => b != 0), $"{t.TextureDefs[i].Name} ({mode}) has transparent pixels");
        }
    }

    [Fact]
    public void OnlyTheMaskedMidtexturesHaveHolesIn2024()
    {
        (WadArchive wad, Textures t) = OpenDoom();
        Assert.SkipUnless(Is2024, "Hole list is for The Ultimate Doom's 2024 re-release.");
        foreach (TextureCompositeMode mode in _modes)
        {
            string[] withHoles = [.. All(t).Where(i => t.R_GenerateComposite(i, mode).Opaque.Any(b => b == 0))
                .Select(i => t.TextureDefs[i].Name).Order(StringComparer.Ordinal)];
            // SKY1's one patch has y origin -8: the corrected composite (originy honoured) moves it
            // 8 rows up and leaves its bottom 8 rows empty; vanilla's does not.
            string[] sky = mode == TextureCompositeMode.Corrected ? ["SKY1"] : [];
            Assert.Equal(_maskedMidtextures.Concat(_multiPatchWithHoles).Concat(sky).Order(StringComparer.Ordinal), withHoles);
        }

        Assert.Equal(1, t.GetComposite("SKINEDGE").Opaque.Count(b => b == 0));
        Assert.False(t.GetComposite("SKINEDGE").IsOpaque(73, 36));
        Assert.False(Patch.Load(wad, "HELL8_1").IsOpaque(9, 36)); // HELL8_1 sits at x = 64, as in DOOM II
        Assert.Equal(1, t.GetComposite("SKINTEK1").Opaque.Count(b => b == 0));
        Assert.False(t.GetComposite("SKINTEK1").IsOpaque(32, 83));
        Assert.False(Patch.Load(wad, "W109_1").IsOpaque(32, 83)); // W109_1 sits at x = 0
    }

    [Fact]
    public void VanillaAndCorrectedDifferOnlyWhereOriginYIsUsedIn2024()
    {
        (_, Textures t) = OpenDoom();
        Assert.SkipUnless(Is2024, "Difference list is for The Ultimate Doom's 2024 re-release.");
        string[] differ = [.. All(t).Where(i =>
            {
                IndexedImage v = t.R_GenerateComposite(i), c = t.R_GenerateComposite(i, TextureCompositeMode.Corrected);
                return !v.Pixels.SequenceEqual(c.Pixels) || !v.Opaque.SequenceEqual(c.Opaque);
            })
            .Select(i => t.TextureDefs[i].Name).Order(StringComparer.Ordinal)];
        Assert.Equal(
            ["AASTINKY", "BIGDOOR7", "BROWN144", "COMPOHSO", "GRAY2", "GRAYVINE", "SKY1", "SW1DIRT", "SW1VINE", "SW2DIRT", "SW2VINE", "TEKWALL1", "TEKWALL5"],
            differ);
        Assert.All(differ, n => Assert.Contains(t.TextureDefs[t.R_TextureNumForName(n)].Patches, p => p.OriginY != 0));
    }

    /// <summary>
    /// Debug export for the T1.4b visual check: writes <c>DOOM_TEX_*.png</c>
    /// to <c>TestResults/graphics/</c> (gitignored). The masked midtextures
    /// over a magenta checkerboard so holes show (MIDGRATE also tiled 2×2:
    /// seams would show at the tile edges), and multi-patch textures, most of
    /// them TEXTURE2's: SKINTEK1 with its one hole, BIGDOOR7, COMPOHSO and
    /// GRAYVINE (patches with a y origin, in both composite modes), STARTAN3
    /// and BROWN144 (TEXTURE1's).
    /// </summary>
    [Fact]
    public void ExportDebugPngs()
    {
        (WadArchive wad, Textures t) = OpenDoom();
        string dir = DebugPng.RequireOutputDir();
        byte[] palette = Playpal.Load(wad).GetPalette(0).ToArray();

        DebugPng.Write(dir, "DOOM_TEX_MIDGRATE_x2_tiled_checker.png", t.GetComposite("MIDGRATE"), palette, scale: 2, checker: true, tilesX: 2, tilesY: 2);
        foreach (string name in new[] { "MIDGRATE", "MIDVINE1", "MIDBRN1", "BRNBIGC", "SKINTEK1" })
            DebugPng.Write(dir, $"DOOM_TEX_{name}_x4_checker.png", t.GetComposite(name), palette, checker: true);
        foreach (string name in new[] { "BIGDOOR7", "COMPOHSO", "GRAYVINE", "BROWN144" })
        {
            DebugPng.Write(dir, $"DOOM_TEX_{name}_x4_vanilla.png", t.GetComposite(name), palette, checker: true);
            DebugPng.Write(dir, $"DOOM_TEX_{name}_x4_corrected.png", t.GetComposite(name, TextureCompositeMode.Corrected), palette, checker: true);
        }
        DebugPng.Write(dir, "DOOM_TEX_STARTAN3_x4.png", t.GetComposite("STARTAN3"), palette);
    }
}
