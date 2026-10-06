using System;
using System.Collections.Generic;
using Godot;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Render;

/// <summary>
/// Every sprite lump of a WAD in one RG8 index atlas (T3.5, SPEC §7.5;
/// <see cref="TextureAtlas"/>, as the level's wall and flat atlas): each lump
/// any frame slot of <see cref="Sprites"/> uses is decoded once and gets a
/// <b>slot</b>. Built once per WAD (sprites do not change between maps, and
/// M4's mobjs can enter any state), so it holds every frame, not only the
/// spawn frames. The <c>sprite_info</c> data texture (RGBA float,
/// <see cref="LevelMesh.DataWidth"/> texels per row) has two texels per slot:
/// <c>2·slot</c> = atlas x, y, width, height; <c>2·slot + 1</c> = the patch's
/// <c>leftoffset</c>, <c>topoffset</c>.
/// </summary>
public sealed class SpriteAtlas
{
    private readonly Dictionary<int, int> _slotOfLump = new();
    private readonly Image _info;

    private SpriteAtlas(Sprites sprites, List<int> lumps, List<IndexedImage> images, TextureAtlas atlas)
    {
        Sprites = sprites;
        Lumps = lumps;
        Images = images;
        Atlas = atlas;
        for (int i = 0; i < lumps.Count; i++)
            _slotOfLump[lumps[i]] = i;
        _info = Image.CreateEmpty(LevelMesh.DataWidth, Math.Max(1, (2 * lumps.Count + LevelMesh.DataWidth - 1) / LevelMesh.DataWidth), false, Image.Format.Rgbaf);
        for (int i = 0; i < lumps.Count; i++)
        {
            AtlasRect r = atlas.Rects[i];
            IndexedImage image = images[i];
            SetTexel(2 * i, new Color(r.X, r.Y, r.Width, r.Height));
            SetTexel(2 * i + 1, new Color(image.LeftOffset, image.TopOffset, 0, 0));
        }
        AtlasTexture = IndexedTextures.CreateTexture(atlas.Image);
        InfoTexture = ImageTexture.CreateFromImage(_info);
    }

    /// <summary>The sprite index this atlas was built from (r_things.c <c>sprites[]</c>).</summary>
    public Sprites Sprites { get; }

    /// <summary>The <c>WadArchive.Lumps</c> index of each slot's lump.</summary>
    public IReadOnlyList<int> Lumps { get; }

    /// <summary>Each slot's decoded patch (with its offsets).</summary>
    public IReadOnlyList<IndexedImage> Images { get; }

    /// <summary>The packed atlas; slot <c>i</c>'s rectangle is <c>Atlas.Rects[i]</c>.</summary>
    public TextureAtlas Atlas { get; }

    public ImageTexture AtlasTexture { get; }

    public ImageTexture InfoTexture { get; }

    /// <summary>How long <see cref="Build"/> took (decode, pack, upload), milliseconds.</summary>
    public double BuildMilliseconds { get; private set; }

    /// <summary>The slot of sprite lump <paramref name="lump"/> (a <c>WadArchive.Lumps</c> index), or -1 when the atlas lacks it (an empty patch).</summary>
    public int SlotOf(int lump) => lump >= 0 && _slotOfLump.TryGetValue(lump, out int slot) ? slot : -1;

    /// <summary>The <c>sprite_info</c> texel <paramref name="i"/> as uploaded.</summary>
    public Color InfoTexel(int i) => _info.GetPixel(i % LevelMesh.DataWidth, i / LevelMesh.DataWidth);

    private void SetTexel(int i, Color c) => _info.SetPixel(i % LevelMesh.DataWidth, i / LevelMesh.DataWidth, c);

    /// <summary>
    /// Decodes and packs every lump a frame slot of <paramref name="sprites"/>
    /// uses (in sprite, frame and rotation order, each lump once). Patches
    /// with no pixels (0 wide or high) get no slot.
    /// </summary>
    public static SpriteAtlas Build(WadArchive wad, Sprites sprites)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var lumps = new List<int>();
        var images = new List<IndexedImage>();
        var seen = new HashSet<int>();
        foreach (SpriteDef def in sprites.SpriteDefs)
        {
            foreach (SpriteFrame frame in def.Frames)
            {
                foreach (int lump in frame.Lump)
                {
                    if (lump < 0 || !seen.Add(lump))
                        continue;
                    WadLump l = wad.Lumps[lump];
                    IndexedImage image = Patch.Decode(l.Data.Span, l.Name);
                    if (image.Width == 0 || image.Height == 0)
                        continue;
                    lumps.Add(lump);
                    images.Add(image);
                }
            }
        }
        if (images.Count == 0)
        {
            // Keep the textures valid: one transparent texel.
            images.Add(new IndexedImage(1, 1, 0, 0, new byte[1], new byte[1]));
            lumps.Add(-1);
        }
        var atlas = new SpriteAtlas(sprites, lumps, images, TextureAtlas.Build(images));
        atlas.BuildMilliseconds = clock.Elapsed.TotalMilliseconds;
        return atlas;
    }
}
