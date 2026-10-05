using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace IsoDoom.Wad.Graphics;

/// <summary>
/// One patch placed in a wall texture (r_data.c <c>texpatch_t</c>, read from
/// the TEXTUREx <c>mappatch_t</c>). <see cref="OriginX"/>/<see cref="OriginY"/>
/// are signed 16-bit values (the lump stores <c>short</c>s) giving the patch's
/// top-left corner in texture space; they may be negative or run past the
/// texture's edges. <see cref="PatchNum"/> indexes <c>PNAMES</c>;
/// <see cref="Lump"/> is the archive lump it resolved to.
/// </summary>
public readonly record struct TexturePatch(int OriginX, int OriginY, int PatchNum, string PatchName, int Lump);

/// <summary>
/// A wall texture definition (r_data.c <c>texture_t</c>, read from a TEXTUREx
/// <c>maptexture_t</c>). <see cref="Masked"/> is the lump's <c>masked</c> flag;
/// vanilla reads past it and never uses it, and so do we (it is kept for
/// tools and tests only).
/// </summary>
public sealed class TextureDef
{
    public TextureDef(string name, bool masked, int width, int height, TexturePatch[] patches)
    {
        Name = name;
        Masked = masked;
        Width = width;
        Height = height;
        Patches = patches;
    }

    public string Name { get; }
    public bool Masked { get; }
    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<TexturePatch> Patches { get; }
}

/// <summary>How <see cref="Textures.R_GenerateComposite"/> treats patch <c>originy</c>.</summary>
public enum TextureCompositeMode
{
    /// <summary>What vanilla draws (r_data.c): see <see cref="Textures.R_GenerateComposite"/>.</summary>
    Vanilla,

    /// <summary><c>originy</c> honoured in every column, top-clipped posts skip their clipped pixels.</summary>
    Corrected,
}

/// <summary>
/// The wall texture set: <c>PNAMES</c> + <c>TEXTURE1</c> (+ <c>TEXTURE2</c>)
/// parsed as r_data.c <c>R_InitTextures</c> does, and composited into
/// <see cref="IndexedImage"/>s by <see cref="R_GenerateComposite"/>.
/// <para>
/// Layouts (all little-endian):
/// <c>PNAMES</c> is <c>int count</c> followed by <c>count</c> 8-byte names.
/// <c>TEXTUREx</c> is <c>int numtextures</c>, <c>int offset[numtextures]</c>
/// (from the start of the lump), and at each offset a <c>maptexture_t</c>:
/// <c>char name[8]; int masked; short width, height; int columndirectory
/// (unused); short patchcount;</c> then <c>patchcount</c> <c>mappatch_t</c>s:
/// <c>short originx, originy, patch, stepdir (unused), colormap (unused)</c>.
/// </para>
/// </summary>
public sealed class Textures
{
    private const int MapTextureHeaderSize = 22; // name[8], masked, width, height, columndirectory, patchcount
    private const int MapPatchSize = 10;

    private readonly WadArchive _wad;
    private readonly TextureDef[] _textures;
    private readonly int?[] _patchWidth;
    private readonly IndexedImage?[] _compositeCache;
    private readonly IndexedImage?[] _correctedCache;

    private Textures(WadArchive wad, string[] patchNames, TextureDef[] textures)
    {
        _wad = wad;
        PatchNames = patchNames;
        _textures = textures;
        _patchWidth = new int?[wad.NumLumps];
        _compositeCache = new IndexedImage?[textures.Length];
        _correctedCache = new IndexedImage?[textures.Length];
    }

    /// <summary>The <c>PNAMES</c> entries, upper-cased and cut at the first NUL.</summary>
    public IReadOnlyList<string> PatchNames { get; }

    /// <summary>r_data.c <c>textures[]</c>: TEXTURE1's entries, then TEXTURE2's.</summary>
    public IReadOnlyList<TextureDef> TextureDefs => _textures;

    /// <summary>r_data.c <c>numtextures</c>.</summary>
    public int NumTextures => _textures.Length;

    /// <summary>Parses a <c>PNAMES</c> lump (r_data.c <c>R_InitTextures</c>).</summary>
    public static string[] ParsePNames(ReadOnlySpan<byte> lump)
    {
        if (lump.Length < 4)
            throw new WadFormatException("PNAMES: too short for the patch count.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(lump);
        if (count < 0 || 4L + 8L * count > lump.Length)
            throw new WadFormatException($"PNAMES: {count} names do not fit in {lump.Length} bytes.");
        string[] names = new string[count];
        for (int i = 0; i < count; i++)
            names[i] = WadFile.NormalizeName(ReadName(lump.Slice(4 + 8 * i, 8)));
        return names;
    }

    /// <summary>
    /// r_data.c <c>R_InitTextures</c>: reads <c>PNAMES</c>, <c>TEXTURE1</c> and,
    /// when present, <c>TEXTURE2</c> (the last lump of each name). Each
    /// <c>PNAMES</c> entry resolves through <c>W_CheckNumForName</c> (last
    /// lump of that name, any namespace). A name that resolves to nothing is
    /// only an error when a texture uses it ("Missing patch in texture"), as
    /// in vanilla; shareware's PNAMES lists registered-only patches.
    /// </summary>
    public static Textures R_InitTextures(WadArchive wad)
    {
        string[] names = ParsePNames(wad.W_CacheLumpName("PNAMES").Span);
        int[] patchlookup = new int[names.Length];
        for (int i = 0; i < names.Length; i++)
            patchlookup[i] = wad.W_CheckNumForName(names[i]);

        List<TextureDef> textures = new();
        ParseTextureLump(wad.W_CacheLumpName("TEXTURE1").Span, "TEXTURE1", names, patchlookup, textures);
        if (wad.W_CheckNumForName("TEXTURE2") != -1)
            ParseTextureLump(wad.W_CacheLumpName("TEXTURE2").Span, "TEXTURE2", names, patchlookup, textures);

        return new Textures(wad, names, textures.ToArray());
    }

    private static void ParseTextureLump(ReadOnlySpan<byte> maptex, string lumpName, string[] names, int[] patchlookup, List<TextureDef> textures)
    {
        if (maptex.Length < 4)
            throw new WadFormatException($"{lumpName}: too short for the texture count.");
        int numtextures = BinaryPrimitives.ReadInt32LittleEndian(maptex);
        if (numtextures < 0 || 4L + 4L * numtextures > maptex.Length)
            throw new WadFormatException($"{lumpName}: {numtextures} directory entries do not fit in {maptex.Length} bytes.");

        for (int i = 0; i < numtextures; i++)
        {
            int offset = BinaryPrimitives.ReadInt32LittleEndian(maptex[(4 + 4 * i)..]);
            // Vanilla checks only offset > maxoff; we also require the whole header to fit.
            if (offset < 0 || offset + (long)MapTextureHeaderSize > maptex.Length)
                throw new WadFormatException($"R_InitTextures: bad texture directory ({lumpName} entry {i}).");

            ReadOnlySpan<byte> mtexture = maptex[offset..];
            string name = WadFile.NormalizeName(ReadName(mtexture[..8]));
            bool masked = BinaryPrimitives.ReadInt32LittleEndian(mtexture[8..]) != 0;
            int width = BinaryPrimitives.ReadInt16LittleEndian(mtexture[12..]);
            int height = BinaryPrimitives.ReadInt16LittleEndian(mtexture[14..]);
            int patchcount = BinaryPrimitives.ReadInt16LittleEndian(mtexture[20..]);
            if (width < 0 || height < 0 || patchcount < 0)
                throw new WadFormatException($"{lumpName}: texture {name} has a bad size {width}x{height} or patch count {patchcount}.");
            if (MapTextureHeaderSize + (long)MapPatchSize * patchcount > mtexture.Length)
                throw new WadFormatException($"{lumpName}: texture {name}'s patches run past the end of the lump.");

            TexturePatch[] patches = new TexturePatch[patchcount];
            for (int j = 0; j < patchcount; j++)
            {
                ReadOnlySpan<byte> mpatch = mtexture[(MapTextureHeaderSize + MapPatchSize * j)..];
                int originx = BinaryPrimitives.ReadInt16LittleEndian(mpatch);
                int originy = BinaryPrimitives.ReadInt16LittleEndian(mpatch[2..]);
                int patchNum = BinaryPrimitives.ReadInt16LittleEndian(mpatch[4..]);
                int lump = patchNum >= 0 && patchNum < patchlookup.Length ? patchlookup[patchNum] : -1;
                if (lump == -1)
                    throw new WadFormatException($"R_InitTextures: Missing patch in texture {name} (PNAMES entry {patchNum}).");
                patches[j] = new TexturePatch(originx, originy, patchNum, names[patchNum], lump);
            }
            textures.Add(new TextureDef(name, masked, width, height, patches));
        }
    }

    /// <summary>
    /// r_data.c <c>R_CheckTextureNumForName</c>: the index of the <b>first</b>
    /// texture called <paramref name="name"/> (case-insensitive), 0 for the
    /// "no texture" marker <c>-</c>, or -1.
    /// </summary>
    public int R_CheckTextureNumForName(string name)
    {
        if (name.Length > 0 && name[0] == '-')
            return 0;
        string key = WadFile.NormalizeName(name);
        for (int i = 0; i < _textures.Length; i++)
        {
            if (_textures[i].Name == key)
                return i;
        }
        return -1;
    }

    /// <summary>r_data.c <c>R_TextureNumForName</c>: like <see cref="R_CheckTextureNumForName"/>, but missing is an error.</summary>
    public int R_TextureNumForName(string name)
    {
        int i = R_CheckTextureNumForName(name);
        if (i == -1)
            throw new KeyNotFoundException($"R_TextureNumForName: {name} not found");
        return i;
    }

    /// <summary>
    /// r_data.c <c>R_GenerateLookup</c> + <c>R_GenerateComposite</c>: builds
    /// texture <paramref name="texnum"/> as a <c>Width × Height</c> image by
    /// drawing its patches in order (later patches over earlier ones).
    /// Patches are clipped to the texture on all four sides: columns left of
    /// 0 or at/after the width are skipped (vanilla's <c>x1 &lt; 0</c> /
    /// <c>x2 &gt; width</c> clamps) and post pixels outside rows
    /// <c>0..Height-1</c> are dropped.
    /// <para>
    /// <see cref="TextureCompositeMode.Vanilla"/> (default) reproduces what
    /// vanilla draws: a column covered by exactly one patch is that patch's
    /// column with <c>originy</c> ignored (vanilla draws such columns straight
    /// from the patch lump), and a column covered by several patches is
    /// composited by <c>R_DrawColumnInCache</c>, where a post clipped at the
    /// top is shortened but still copied from its first pixel.
    /// <see cref="TextureCompositeMode.Corrected"/> honours <c>originy</c>
    /// everywhere and skips the clipped pixels of a post (Boom and later).
    /// </para>
    /// <para>
    /// In both modes the opacity mask is the union of the drawn posts, so holes
    /// stay transparent whatever a column's patch count, and uncovered pixels
    /// have index 0 (vanilla's Medusa/garbage columns are not modelled; see
    /// SPEC §12). Composites are cached per mode; the returned image is shared
    /// and must not be modified.
    /// </para>
    /// </summary>
    public IndexedImage R_GenerateComposite(int texnum, TextureCompositeMode mode = TextureCompositeMode.Vanilla)
    {
        IndexedImage?[] cache = mode == TextureCompositeMode.Vanilla ? _compositeCache : _correctedCache;
        if (cache[texnum] is { } cached)
            return cached;

        TextureDef texture = _textures[texnum];
        int width = texture.Width;
        int height = texture.Height;
        byte[] pixels = new byte[width * height];
        byte[] opaque = new byte[width * height];

        // R_GenerateLookup: how many patches cover each column.
        int[] patchcount = new int[width];
        foreach (TexturePatch patch in texture.Patches)
        {
            (int x, int x2) = ClipColumns(patch, GetPatchWidth(patch.Lump), width);
            for (; x < x2; x++)
                patchcount[x]++;
        }

        foreach (TexturePatch patch in texture.Patches)
        {
            ReadOnlySpan<byte> realpatch = _wad.Lumps[patch.Lump].Data.Span;
            (int x, int x2) = ClipColumns(patch, GetPatchWidth(patch.Lump), width);
            for (; x < x2; x++)
            {
                int colofs = BinaryPrimitives.ReadInt32LittleEndian(realpatch[(8 + 4 * (x - patch.OriginX))..]);
                if (mode == TextureCompositeMode.Corrected)
                    DrawColumnInCache(realpatch, colofs, patch.OriginY, true, pixels, opaque, x, width, height);
                else if (patchcount[x] == 1)
                    DrawColumnInCache(realpatch, colofs, 0, true, pixels, opaque, x, width, height);
                else
                    DrawColumnInCache(realpatch, colofs, patch.OriginY, false, pixels, opaque, x, width, height);
            }
        }

        IndexedImage composite = new(width, height, 0, 0, pixels, opaque);
        cache[texnum] = composite;
        return composite;
    }

    /// <summary>The composite of the first texture called <paramref name="name"/>.</summary>
    public IndexedImage GetComposite(string name, TextureCompositeMode mode = TextureCompositeMode.Vanilla) =>
        R_GenerateComposite(R_TextureNumForName(name), mode);

    /// <summary>r_data.c <c>R_GenerateComposite</c>: <c>x1 &lt; 0 → 0</c>, <c>x2 &gt; width → width</c>.</summary>
    private static (int X1, int X2) ClipColumns(TexturePatch patch, int patchWidth, int width)
    {
        int x1 = patch.OriginX;
        int x2 = x1 + patchWidth;
        return (x1 < 0 ? 0 : x1, x2 > width ? width : x2);
    }

    /// <summary>
    /// r_data.c <c>R_DrawColumnInCache</c>: copies one patch column's posts
    /// into texture column <paramref name="x"/>. With
    /// <paramref name="skipClippedTop"/> false a post starting above row 0
    /// is shortened but copied from its first pixel, as vanilla does.
    /// </summary>
    private static void DrawColumnInCache(ReadOnlySpan<byte> patch, int ofs, int originy, bool skipClippedTop,
        byte[] pixels, byte[] opaque, int x, int width, int cacheheight)
    {
        // Patch.Decode has already checked that the column is well formed.
        while (patch[ofs] != 0xFF)
        {
            int source = ofs + 3;
            int count = patch[ofs + 1];
            int position = originy + patch[ofs];
            int next = ofs + count + 4;

            if (position < 0)
            {
                if (skipClippedTop)
                    source -= position;
                count += position;
                position = 0;
            }
            if (position + count > cacheheight)
                count = cacheheight - position;

            for (int i = 0; i < count; i++)
            {
                int dst = (position + i) * width + x;
                pixels[dst] = patch[source + i];
                opaque[dst] = 1;
            }
            ofs = next;
        }
    }

    /// <summary>Validates the patch lump once (via <see cref="Patch.Decode"/>) and returns its width.</summary>
    private int GetPatchWidth(int lump)
    {
        if (_patchWidth[lump] is int w)
            return w;
        WadLump l = _wad.Lumps[lump];
        w = Patch.Decode(l.Data.Span, l.Name).Width;
        _patchWidth[lump] = w;
        return w;
    }

    private static string ReadName(ReadOnlySpan<byte> raw)
    {
        int len = raw.IndexOf((byte)0);
        if (len < 0)
            len = raw.Length;
        char[] chars = new char[len];
        for (int i = 0; i < len; i++)
            chars[i] = (char)raw[i];
        return new string(chars);
    }
}
