using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Graphics;

/// <summary>T1.3 decoders on synthetic lumps (no DOOM1.WAD needed).</summary>
public class GraphicsDecoderTests
{
    /// <summary>Builds a patch lump: header, column offsets, then each column's posts (topdelta, pixels).</summary>
    internal static byte[] BuildPatch(int width, int height, int left, int top, params (int TopDelta, byte[] Pixels)[][] columns)
    {
        List<byte> body = [];
        int[] offsets = new int[width];
        int dataStart = 8 + 4 * width;
        for (int x = 0; x < width; x++)
        {
            offsets[x] = dataStart + body.Count;
            foreach ((int topDelta, byte[] pixels) in columns[x])
            {
                body.Add((byte)topDelta);
                body.Add((byte)pixels.Length);
                body.Add(0xAA); // pad
                body.AddRange(pixels);
                body.Add(0xAA); // pad
            }
            body.Add(0xFF);
        }
        byte[] lump = new byte[dataStart + body.Count];
        BinaryPrimitives.WriteInt16LittleEndian(lump, (short)width);
        BinaryPrimitives.WriteInt16LittleEndian(lump.AsSpan(2), (short)height);
        BinaryPrimitives.WriteInt16LittleEndian(lump.AsSpan(4), (short)left);
        BinaryPrimitives.WriteInt16LittleEndian(lump.AsSpan(6), (short)top);
        for (int x = 0; x < width; x++)
            BinaryPrimitives.WriteInt32LittleEndian(lump.AsSpan(8 + 4 * x), offsets[x]);
        body.CopyTo(lump, dataStart);
        return lump;
    }

    [Fact]
    public void PatchPostsLandAtTheirTopDelta()
    {
        byte[] lump = BuildPatch(3, 5, 1, -2,
            [(0, [10, 11]), (3, [12])],      // two posts with a gap
            [],                              // empty column
            [(1, [0, 20, 21, 22])]);         // index 0 is a real colour
        IndexedImage img = Patch.Decode(lump);

        Assert.Equal(3, img.Width);
        Assert.Equal(5, img.Height);
        Assert.Equal(1, img.LeftOffset);
        Assert.Equal(-2, img.TopOffset);

        Assert.Equal(10, img[0, 0]);
        Assert.Equal(11, img[0, 1]);
        Assert.False(img.IsOpaque(0, 2));
        Assert.Equal(12, img[0, 3]);
        Assert.False(img.IsOpaque(0, 4));

        for (int y = 0; y < 5; y++)
            Assert.False(img.IsOpaque(1, y));

        Assert.False(img.IsOpaque(2, 0));
        Assert.True(img.IsOpaque(2, 1));
        Assert.Equal(0, img[2, 1]);
        Assert.Equal(22, img[2, 4]);
    }

    [Fact]
    public void PatchPostPastTheBottomIsClipped()
    {
        IndexedImage img = Patch.Decode(BuildPatch(1, 2, 0, 0, [(1, [5, 6, 7])]));
        Assert.False(img.IsOpaque(0, 0));
        Assert.Equal(5, img[0, 1]);
    }

    [Fact]
    public void MalformedPatchesThrow()
    {
        byte[] good = BuildPatch(2, 2, 0, 0, [(0, [1, 2])], [(0, [3])]);
        Assert.Throws<WadFormatException>(() => Patch.Decode(good.AsSpan(0, 6)));          // short header
        Assert.Throws<WadFormatException>(() => Patch.Decode(good.AsSpan(0, 12)));         // column offsets cut off
        Assert.Throws<WadFormatException>(() => Patch.Decode(good.AsSpan(0, good.Length - 1))); // missing terminator

        byte[] badOffset = (byte[])good.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(badOffset.AsSpan(12), 9999);
        Assert.Throws<WadFormatException>(() => Patch.Decode(badOffset));

        byte[] zeroWidth = (byte[])good.Clone();
        BinaryPrimitives.WriteInt16LittleEndian(zeroWidth, 0);
        Assert.Throws<WadFormatException>(() => Patch.Decode(zeroWidth));
    }

    [Fact]
    public void FlatIsRowMajor64x64()
    {
        byte[] lump = new byte[Flat.Size + 10]; // extra bytes are ignored
        for (int i = 0; i < Flat.Size; i++)
            lump[i] = (byte)i;
        IndexedImage img = Flat.Decode(lump);
        Assert.Equal(64, img.Width);
        Assert.Equal(64, img.Height);
        Assert.Equal(0, img.LeftOffset);
        Assert.Equal(1, img[1, 0]);
        Assert.Equal(64 & 0xFF, img[0, 1]);
        Assert.All(img.Opaque, b => Assert.Equal(1, b));
        Assert.Throws<WadFormatException>(() => Flat.Decode(new byte[Flat.Size - 1]));
    }

    [Fact]
    public void PlaypalAndColormapSizes()
    {
        byte[] pal = new byte[Playpal.PaletteSize * 2];
        pal[Playpal.PaletteSize + 3 * 7 + 1] = 99; // palette 1, index 7, green
        var playpal = Playpal.Decode(pal);
        Assert.Equal(2, playpal.Count);
        Assert.Equal(((byte)0, (byte)99, (byte)0), playpal.GetColor(1, 7));
        Assert.Throws<WadFormatException>(() => Playpal.Decode(new byte[Playpal.PaletteSize + 1]));
        Assert.Throws<WadFormatException>(() => Playpal.Decode([]));

        byte[] cm = new byte[Colormap.MapSize * Colormap.NumMaps];
        cm[Colormap.MapSize * Colormap.INVERSECOLORMAP + 5] = 42;
        var colormap = Colormap.Decode(cm);
        Assert.Equal(34, colormap.Count);
        Assert.Equal(42, colormap.GetMap(Colormap.INVERSECOLORMAP)[5]);
        Assert.Throws<WadFormatException>(() => Colormap.Decode(new byte[Colormap.MapSize * Colormap.INVERSECOLORMAP]));
    }

    [Fact]
    public void ToRgbaAppliesColormapAndTransparency()
    {
        byte[] pal = new byte[Playpal.PaletteSize];
        pal[3 * 2] = 200;     // index 2 = (200,0,0)
        pal[3 * 3 + 2] = 100; // index 3 = (0,0,100)
        byte[] map = new byte[Colormap.MapSize];
        map[2] = 3;
        IndexedImage img = new(2, 1, 0, 0, [2, 2], [1, 0]);

        Assert.Equal(new byte[] { 200, 0, 0, 255, 0, 0, 0, 0 }, img.ToRgba(pal));
        Assert.Equal(new byte[] { 0, 0, 100, 255, 0, 0, 0, 0 }, img.ToRgba(pal, map));
    }
}
