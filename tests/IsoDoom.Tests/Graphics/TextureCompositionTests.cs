using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Graphics;

/// <summary>T1.4 PNAMES/TEXTUREx parsing and composition on synthetic WADs (no DOOM1.WAD needed).</summary>
public class TextureCompositionTests
{
    internal sealed record Tex(string Name, int Width, int Height, bool Masked, params (int X, int Y, int Patch)[] Patches);

    internal static byte[] BuildPNames(params string[] names)
    {
        byte[] lump = new byte[4 + 8 * names.Length];
        BinaryPrimitives.WriteInt32LittleEndian(lump, names.Length);
        for (int i = 0; i < names.Length; i++)
            Encoding.ASCII.GetBytes(names[i], lump.AsSpan(4 + 8 * i));
        return lump;
    }

    internal static byte[] BuildTextureLump(params Tex[] textures)
    {
        List<byte> body = new();
        int dirSize = 4 + 4 * textures.Length;
        byte[] dir = new byte[dirSize];
        BinaryPrimitives.WriteInt32LittleEndian(dir, textures.Length);
        for (int i = 0; i < textures.Length; i++)
        {
            Tex t = textures[i];
            BinaryPrimitives.WriteInt32LittleEndian(dir.AsSpan(4 + 4 * i), dirSize + body.Count);
            byte[] mt = new byte[22 + 10 * t.Patches.Length];
            Encoding.ASCII.GetBytes(t.Name, mt);
            BinaryPrimitives.WriteInt32LittleEndian(mt.AsSpan(8), t.Masked ? 1 : 0);
            BinaryPrimitives.WriteInt16LittleEndian(mt.AsSpan(12), (short)t.Width);
            BinaryPrimitives.WriteInt16LittleEndian(mt.AsSpan(14), (short)t.Height);
            BinaryPrimitives.WriteInt16LittleEndian(mt.AsSpan(20), (short)t.Patches.Length);
            for (int j = 0; j < t.Patches.Length; j++)
            {
                Span<byte> mp = mt.AsSpan(22 + 10 * j);
                BinaryPrimitives.WriteInt16LittleEndian(mp, (short)t.Patches[j].X);
                BinaryPrimitives.WriteInt16LittleEndian(mp[2..], (short)t.Patches[j].Y);
                BinaryPrimitives.WriteInt16LittleEndian(mp[4..], (short)t.Patches[j].Patch);
                BinaryPrimitives.WriteInt16LittleEndian(mp[6..], 1);  // stepdir (unused)
            }
            body.AddRange(mt);
        }
        byte[] lump = new byte[dirSize + body.Count];
        dir.CopyTo(lump, 0);
        body.CopyTo(lump, dirSize);
        return lump;
    }

    private static byte[] Solid(int width, int height, byte value)
    {
        var columns = new (int, byte[])[width][];
        for (int x = 0; x < width; x++)
        {
            byte[] col = new byte[height];
            for (int y = 0; y < height; y++)
                col[y] = (byte)(value + y);
            columns[x] = [(0, col)];
        }
        return GraphicsDecoderTests.BuildPatch(width, height, 0, 0, columns);
    }

    private static Textures Load(byte[] pnames, byte[] texture1, byte[]? texture2 = null, params (string Name, byte[] Data)[] patches) =>
        Load(pnames, texture1, texture2, PatchTopDeltaMode.Tall, patches);

    internal static Textures Load(byte[] pnames, byte[] texture1, byte[]? texture2, PatchTopDeltaMode topDeltaMode,
        params (string Name, byte[] Data)[] patches)
    {
        WadBuilder b = new WadBuilder(WadType.Iwad).Lump("PNAMES", pnames).Lump("TEXTURE1", texture1);
        if (texture2 != null)
            b.Lump("TEXTURE2", texture2);
        b.Lump("P_START");
        foreach ((string name, byte[] data) in patches)
            b.Lump(name, data);
        b.Lump("P_END");
        return Textures.R_InitTextures(new WadArchive([b.ToWadFile()]), topDeltaMode);
    }

    [Fact]
    public void ParsesTexture1ThenTexture2AndLooksUpTheFirstMatch()
    {
        Textures t = Load(
            BuildPNames("pa", "MISSING", "PB"),
            BuildTextureLump(new Tex("AASHITTY", 4, 4, false, (0, 0, 0)), new Tex("grate", 4, 2, true, (-1, -2, 2))),
            BuildTextureLump(new Tex("GRATE", 8, 8, false, (0, 0, 0))),
            ("PA", Solid(4, 4, 10)), ("PB", Solid(4, 4, 20)));

        Assert.Equal(new[] { "PA", "MISSING", "PB" }, t.PatchNames);  // an unused missing patch is fine
        Assert.Equal(3, t.NumTextures);
        TextureDef grate = t.TextureDefs[1];
        Assert.Equal(("GRATE", 4, 2, true), (grate.Name, grate.Width, grate.Height, grate.Masked));
        Assert.Equal(new TexturePatch(-1, -2, 2, "PB", grate.Patches[0].Lump), grate.Patches[0]);

        Assert.Equal(1, t.R_CheckTextureNumForName("Grate"));  // first match wins
        Assert.Equal(0, t.R_CheckTextureNumForName("-"));
        Assert.Equal(-1, t.R_CheckTextureNumForName("NOPE"));
        Assert.Throws<KeyNotFoundException>(() => t.R_TextureNumForName("NOPE"));
    }

    [Fact]
    public void MissingPatchInATextureIsAnError()
    {
        var ex = Assert.Throws<WadFormatException>(() => Load(
            BuildPNames("PA", "MISSING"),
            BuildTextureLump(new Tex("T", 4, 4, false, (0, 0, 1))),
            null, ("PA", Solid(4, 4, 10))));
        Assert.Contains("Missing patch in texture T", ex.Message);
    }

    [Fact]
    public void BadDirectoryOffsetIsAnError()
    {
        byte[] tex = BuildTextureLump(new Tex("T", 4, 4, false, (0, 0, 0)));
        BinaryPrimitives.WriteInt32LittleEndian(tex.AsSpan(4), tex.Length);
        Assert.Throws<WadFormatException>(() => Load(BuildPNames("PA"), tex, null, ("PA", Solid(4, 4, 10))));
    }

    [Fact]
    public void PatchesAreClippedAtAllFourEdges()
    {
        // 3x3 texture; a 4x4 patch at (-2, 1) runs off the left and bottom, a
        // 4x4 patch at (2, -1) off the right and top (single-patch columns).
        Textures t = Load(
            BuildPNames("PA", "PB"),
            BuildTextureLump(new Tex("T", 3, 3, false, (-2, 1, 0), (2, -1, 1))),
            null, ("PA", Solid(4, 4, 10)), ("PB", Solid(4, 4, 20)));

        IndexedImage c = t.GetComposite("T", TextureCompositeMode.Corrected);
        Assert.Equal((3, 3), (c.Width, c.Height));
        Assert.False(c.IsOpaque(0, 0));
        Assert.Equal(10, c[0, 1]);  // patch columns 2..3 land on texture columns 0..1
        Assert.Equal(11, c[1, 2]);
        Assert.Equal(21, c[2, 0]);  // patch row 1 at texture row 0
        Assert.Equal(23, c[2, 2]);

        IndexedImage v = t.GetComposite("T");
        Assert.Equal(10, v[0, 0]);  // vanilla: single-patch columns ignore originy
        Assert.Equal(12, v[0, 2]);
        Assert.Equal(20, v[2, 0]);
    }

    [Fact]
    public void MultiPatchColumnsUseVanillaTopClipping()
    {
        // Both patches cover every column: vanilla R_DrawColumnInCache shortens
        // the top-clipped post of PA but copies it from its first pixel.
        Textures t = Load(
            BuildPNames("PA", "PB"),
            BuildTextureLump(new Tex("T", 2, 6, false, (0, -2, 0), (0, 4, 1))),
            null, ("PA", Solid(2, 4, 10)), ("PB", Solid(2, 4, 20)));

        IndexedImage v = t.GetComposite("T");
        Assert.Equal(new byte[] { 10, 11, 0, 0, 20, 21 }, Column(v, 0));
        Assert.Equal(new byte[] { 1, 1, 0, 0, 1, 1 }, ColumnMask(v, 1));

        IndexedImage c = t.GetComposite("T", TextureCompositeMode.Corrected);
        Assert.Equal(new byte[] { 12, 13, 0, 0, 20, 21 }, Column(c, 0));
    }

    [Fact]
    public void HolesStayTransparentInMultiPatchColumns()
    {
        byte[] holey = GraphicsDecoderTests.BuildPatch(1, 4, 0, 0, [(0, [1]), (3, [4])]);
        Textures t = Load(
            BuildPNames("H"),
            BuildTextureLump(new Tex("T", 1, 8, true, (0, 0, 0), (0, 4, 0))),
            null, ("H", holey));

        IndexedImage v = t.GetComposite("T");
        Assert.Equal(new byte[] { 1, 0, 0, 1, 1, 0, 0, 1 }, ColumnMask(v, 0));
        Assert.Equal(new byte[] { 1, 0, 0, 4, 1, 0, 0, 4 }, Column(v, 0));
        Assert.Same(v, t.GetComposite("t"));  // cached
    }

    private static byte[] Column(IndexedImage img, int x)
    {
        byte[] col = new byte[img.Height];
        for (int y = 0; y < img.Height; y++)
            col[y] = img[x, y];
        return col;
    }

    private static byte[] ColumnMask(IndexedImage img, int x)
    {
        byte[] col = new byte[img.Height];
        for (int y = 0; y < img.Height; y++)
            col[y] = img.Opaque[y * img.Width + x];
        return col;
    }
}
