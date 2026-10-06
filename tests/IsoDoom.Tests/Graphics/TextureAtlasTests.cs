using System;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Graphics;

/// <summary>T2.5: the level texture atlas (shelf packing of wall textures and flats).</summary>
public class TextureAtlasTests
{
    private static IndexedImage Image(int w, int h, byte seed)
    {
        byte[] pixels = new byte[w * h];
        byte[] opaque = new byte[w * h];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(seed + i * 7);
            opaque[i] = (byte)((i + seed) % 3 == 0 ? 0 : 1);
        }
        return new IndexedImage(w, h, 0, 0, pixels, opaque);
    }

    /// <summary>Every rectangle inside the atlas, none overlapping, every texel copied with its mask, the rest transparent.</summary>
    private static void CheckAtlas(IReadOnlyList<IndexedImage> images, TextureAtlas atlas)
    {
        IndexedImage a = atlas.Image;
        Assert.Equal(images.Count, atlas.Rects.Count);
        Assert.True(a.Width <= TextureAtlas.MaxSize && a.Height <= TextureAtlas.MaxSize);
        Assert.True((a.Width & (a.Width - 1)) == 0, "atlas width is a power of two");
        var owner = new int[a.Width * a.Height];
        Array.Fill(owner, -1);
        for (int i = 0; i < images.Count; i++)
        {
            AtlasRect r = atlas.Rects[i];
            IndexedImage image = images[i];
            Assert.Equal((image.Width, image.Height), (r.Width, r.Height));
            Assert.True(r.X >= 0 && r.Y >= 0 && r.X + r.Width <= a.Width && r.Y + r.Height <= a.Height, $"rect {i} {r} outside {a.Width}x{a.Height}");
            for (int y = 0; y < r.Height; y++)
            {
                for (int x = 0; x < r.Width; x++)
                {
                    int at = (r.Y + y) * a.Width + r.X + x;
                    Assert.True(owner[at] == -1, $"rect {i} overlaps rect {owner[at]} at ({r.X + x}, {r.Y + y})");
                    owner[at] = i;
                    Assert.Equal(image[x, y], a.Pixels[at]);
                    Assert.Equal(image.IsOpaque(x, y), a.Opaque[at] != 0);
                }
            }
        }
        for (int at = 0; at < owner.Length; at++)
        {
            if (owner[at] == -1)
                Assert.Equal(0, a.Opaque[at]);
        }
    }

    [Fact]
    public void PacksWithoutOverlapAndCopiesPixels()
    {
        var images = new[] { Image(64, 128, 1), Image(64, 64, 2), Image(256, 128, 3), Image(72, 72, 4), Image(8, 200, 5), Image(64, 64, 6), Image(1, 1, 7) };
        TextureAtlas atlas = TextureAtlas.Build(images);
        CheckAtlas(images, atlas);
        Assert.Equal(256, atlas.Image.Width); // widest image, and 256² ≥ total area
    }

    [Fact]
    public void LayoutIsDeterministicAndSortedByHeight()
    {
        var sizes = new[] { (64, 64), (128, 128), (64, 64), (64, 128) };
        (AtlasRect[] rects, int width, int height) = TextureAtlas.Layout(sizes);
        (AtlasRect[] again, int width2, int height2) = TextureAtlas.Layout(sizes);
        Assert.Equal(rects, again);
        Assert.Equal((width, height), (width2, height2));
        Assert.Equal(256, width);
        Assert.Equal(192, height);
        // Tallest first, ties by width then input order.
        Assert.Equal(new AtlasRect(0, 0, 128, 128), rects[1]);
        Assert.Equal(new AtlasRect(128, 0, 64, 128), rects[3]);
        Assert.Equal(new AtlasRect(192, 0, 64, 64), rects[0]);
        Assert.Equal(new AtlasRect(0, 128, 64, 64), rects[2]);
    }

    [Fact]
    public void RejectsEmptyAndOversizedImages()
    {
        Assert.Throws<ArgumentException>(() => TextureAtlas.Layout(new[] { (0, 64) }));
        Assert.Throws<ArgumentException>(() => TextureAtlas.Layout(new[] { (TextureAtlas.MaxSize * 2, 1) }));
    }

    private static void CheckWholeIwad(WadArchive wad)
    {
        Textures textures = Textures.R_InitTextures(wad);
        var images = new List<IndexedImage>();
        for (int t = 1; t < textures.NumTextures; t++)
            images.Add(textures.R_GenerateComposite(t));
        foreach (WadLump flat in wad.GetNamespace(LumpNamespace.Flats).Where(l => l.Size >= Flat.Size))
            images.Add(Flat.Decode(flat.Data.Span, flat.Name));
        CheckAtlas(images, TextureAtlas.Build(images));
    }

    [Fact]
    public void SyntheticIwadFitsInOneAtlas() =>
        CheckWholeIwad(new WadArchive(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) }));

    [Fact]
    public void Doom1FitsInOneAtlas() => CheckWholeIwad(WadArchive.Open(TestWads.RequireDoom1()));

    [Fact]
    public void Doom2FitsInOneAtlas() => CheckWholeIwad(WadArchive.Open(TestWads.RequireDoom2()));
}
