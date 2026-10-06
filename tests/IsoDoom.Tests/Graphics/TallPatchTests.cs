using System;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Graphics;

/// <summary>
/// T1.3a: DeePsea tall patches (<see cref="PatchTopDeltaMode"/>). Synthetic
/// patches check both layouts; the IWAD tests check that tall-patch support
/// changes nothing for IWAD graphics (skipped without the IWADs).
/// </summary>
public class TallPatchTests
{
    private static byte[] Run(int length, byte value) => Enumerable.Repeat(value, length).ToArray();

    // One 400-row column: posts at raw topdelta 0, 254 and 100. The third post's
    // topdelta is not greater than the previous top (254), so a tall-patch
    // reader puts it at 254 + 100 = 354; vanilla puts it at row 100.
    private static byte[] TallColumnPatch() =>
        GraphicsDecoderTests.BuildPatch(1, 400, 0, 0, [(0, Run(10, 1)), (254, Run(10, 2)), (100, Run(5, 3))]);

    [Fact]
    public void TallPatchPostsAreRelativeToThePreviousTop()
    {
        IndexedImage img = Patch.Decode(TallColumnPatch());  // tall patches are the default
        Assert.Equal(1, img[0, 9]);
        Assert.Equal(2, img[0, 254]);
        Assert.Equal(2, img[0, 263]);
        Assert.False(img.IsOpaque(0, 100));
        Assert.False(img.IsOpaque(0, 353));
        for (int y = 354; y < 359; y++)
            Assert.Equal(3, img[0, y]);
        Assert.False(img.IsOpaque(0, 359));
        Assert.Equal(25, img.Opaque.Count(b => b != 0));
    }

    [Fact]
    public void VanillaModePlacesEveryPostAtItsAbsoluteTopDelta()
    {
        IndexedImage img = Patch.Decode(TallColumnPatch(), mode: PatchTopDeltaMode.Vanilla);
        for (int y = 100; y < 105; y++)
            Assert.Equal(3, img[0, y]);
        Assert.Equal(2, img[0, 254]);
        Assert.False(img.IsOpaque(0, 354));
        Assert.Equal(25, img.Opaque.Count(b => b != 0));
    }

    [Fact]
    public void RepeatedTopDeltasChainAndPostsRunningOffTheBottomAreClipped()
    {
        // 254, 254, 254: tops 254, 508, 762 (each post continues the column).
        byte[] lump = GraphicsDecoderTests.BuildPatch(1, 766, 0, 0, [(254, Run(254, 4)), (254, Run(254, 5)), (254, Run(10, 6))]);
        IndexedImage img = Patch.Decode(lump);
        Assert.False(img.IsOpaque(0, 253));
        Assert.Equal(4, img[0, 507]);
        Assert.Equal(5, img[0, 508]);
        Assert.Equal(5, img[0, 761]);
        Assert.Equal(6, img[0, 765]);  // rows 766.. are past the height and dropped
        Assert.True(Patch.IsPatch(lump));
        Assert.True(Patch.IsPatch(lump, PatchTopDeltaMode.Vanilla));
    }

    [Fact]
    public void IsPatchChecksTheRealPostTop()
    {
        // Raw topdeltas 10 and 10 are both inside a 20-row patch, but the
        // second post's tall-patch top is 20, below the patch.
        byte[] lump = GraphicsDecoderTests.BuildPatch(1, 20, 0, 0, [(10, [1]), (10, [2])]);
        Assert.False(Patch.IsPatch(lump));
        Assert.True(Patch.IsPatch(lump, PatchTopDeltaMode.Vanilla));
    }

    [Theory]
    [InlineData(TextureCompositeMode.Vanilla)]
    [InlineData(TextureCompositeMode.Corrected)]
    public void TexturesCompositeTallPatches(TextureCompositeMode compositeMode)
    {
        // Two copies of the tall column side by side, the second shifted down by 6
        // (a multi-patch column takes the R_DrawColumnInCache path).
        byte[] pnames = TextureCompositionTests.BuildPNames("TALL");
        byte[] texture1 = TextureCompositionTests.BuildTextureLump(
            new TextureCompositionTests.Tex("TALLTEX", 2, 400, false, (0, 0, 0), (1, 6, 0), (1, 0, 0)));
        foreach (PatchTopDeltaMode topDeltaMode in new[] { PatchTopDeltaMode.Tall, PatchTopDeltaMode.Vanilla })
        {
            Textures textures = TextureCompositionTests.Load(pnames, texture1, null, topDeltaMode, ("TALL", TallColumnPatch()));
            Assert.Equal(topDeltaMode, textures.TopDeltaMode);
            IndexedImage tex = textures.GetComposite("TALLTEX", compositeMode);
            int third = topDeltaMode == PatchTopDeltaMode.Tall ? 354 : 100;
            Assert.Equal(3, tex[0, third]);               // single-patch column
            Assert.Equal(3, tex[1, third + 6 + 4]);       // the shifted copy's last pixel of the third post
            Assert.Equal(3, tex[1, third]);               // the unshifted copy
            Assert.False(tex.IsOpaque(0, topDeltaMode == PatchTopDeltaMode.Tall ? 100 : 354));
        }
    }

    public static TheoryData<string> Iwads => new() { "DOOM1", "DOOM2" };

    private static WadArchive OpenIwad(string which) =>
        WadArchive.Open(which == "DOOM1" ? TestWads.RequireDoom1() : TestWads.RequireDoom2());

    private static void AssertSameImage(IndexedImage expected, IndexedImage actual, string name)
    {
        Assert.True(expected.Width == actual.Width && expected.Height == actual.Height
            && expected.LeftOffset == actual.LeftOffset && expected.TopOffset == actual.TopOffset
            && expected.Pixels.AsSpan().SequenceEqual(actual.Pixels)
            && expected.Opaque.AsSpan().SequenceEqual(actual.Opaque), $"{name} differs between tall and vanilla mode.");
    }

    [Theory]
    [MemberData(nameof(Iwads))]
    public void IwadPatchesDecodeTheSameWithTallPatchSupport(string which)
    {
        WadArchive wad = OpenIwad(which);
        int patches = 0;
        foreach (WadLump lump in wad.Lumps)
        {
            ReadOnlySpan<byte> data = lump.Data.Span;
            bool isPatch = Patch.IsPatch(data, PatchTopDeltaMode.Vanilla);
            Assert.Equal(isPatch, Patch.IsPatch(data, PatchTopDeltaMode.Tall));
            if (!isPatch)
                continue;
            patches++;
            AssertSameImage(Patch.Decode(data, lump.Name, PatchTopDeltaMode.Vanilla), Patch.Decode(data, lump.Name), lump.Name);
        }
        Assert.True(patches > 900, $"only {patches} patch-like lumps found");
    }

    [Theory]
    [MemberData(nameof(Iwads))]
    public void IwadTexturesCompositeTheSameWithTallPatchSupport(string which)
    {
        WadArchive wad = OpenIwad(which);
        Textures tall = Textures.R_InitTextures(wad);
        Textures vanilla = Textures.R_InitTextures(wad, PatchTopDeltaMode.Vanilla);
        for (int i = 0; i < tall.NumTextures; i++)
        {
            foreach (TextureCompositeMode mode in new[] { TextureCompositeMode.Vanilla, TextureCompositeMode.Corrected })
                AssertSameImage(vanilla.R_GenerateComposite(i, mode), tall.R_GenerateComposite(i, mode), tall.TextureDefs[i].Name);
        }
    }
}
