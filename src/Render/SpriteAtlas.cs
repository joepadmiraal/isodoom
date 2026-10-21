using System;
using System.Collections.Generic;
using Godot;
using IsoDoom.Sim;
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
/// <para>
/// T6.14d (SPEC §12 "T6.14 Player weapons"): after the WAD's lumps, the
/// composites of a <see cref="HeldWeaponSet"/> (the players holding each
/// weapon): one synthetic sprite per weapon slot that has composites
/// (<c>#PW0</c>–<c>#PW8</c>, never a lump name), appended to
/// <see cref="Sprites"/> after the WAD's sprites so their indices don't move,
/// with frames A–G laid out as <c>PLAY</c>'s (its rotations and mirrors): each
/// rotation slot shows the composite of <c>PLAY</c>'s lump there, or that stock
/// lump itself where it has none (a lump without a pose).
/// <see cref="SpriteFrame.Select"/> and the shader's mirroring work on them
/// untouched. A composite's slot has no WAD lump: its <see cref="Lumps"/> id is
/// past the WAD's lumps and <see cref="HeldSource"/> names it.
/// </para>
/// </summary>
public sealed class SpriteAtlas
{
    private readonly Dictionary<int, int> _slotOfLump = [];
    private readonly Image _info;

    private readonly (weapontype_t Weapon, string Lump)?[] _heldSources;
    private readonly int[] _heldSprite;

    private SpriteAtlas(Sprites sprites, List<int> lumps, List<IndexedImage> images, TextureAtlas atlas, HeldWeaponSet? held, int[] heldSprite, (weapontype_t, string)?[] heldSources)
    {
        Sprites = sprites;
        Held = held;
        _heldSprite = heldSprite;
        _heldSources = heldSources;
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

    /// <summary>T6.14d: the composites the synthetic sprites show, or null (none: the players drawn stock).</summary>
    public HeldWeaponSet? Held { get; }

    /// <summary>T6.14d: the synthetic sprite of the players holding <paramref name="weapon"/> (an index of <see cref="Sprites"/>), or −1 (none: drawn stock).</summary>
    public int HeldSprite(weapontype_t weapon) => (uint)weapon < (uint)_heldSprite.Length ? _heldSprite[(int)weapon] : -1;

    /// <summary>T6.14d: the weapon whose synthetic sprite <paramref name="sprite"/> is, or null (a WAD's sprite).</summary>
    public weapontype_t? HeldWeaponOf(int sprite)
    {
        for (int w = 0; w < _heldSprite.Length; w++)
        {
            if (_heldSprite[w] == sprite && sprite >= 0)
                return (weapontype_t)w;
        }
        return null;
    }

    /// <summary>T6.14d: the weapon and <c>PLAY</c> lump of slot <paramref name="slot"/>'s composite, or null (a WAD lump's slot).</summary>
    public (weapontype_t Weapon, string Lump)? HeldSource(int slot) => (uint)slot < (uint)_heldSources.Length ? _heldSources[slot] : null;

    /// <summary>The <c>WadArchive.Lumps</c> index of each slot's lump (T6.14d: past the WAD's lumps for a composite, <see cref="HeldSource"/>).</summary>
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

    /// <summary>T6.14d: the synthetic sprites' frames, A–G (<c>PLAY</c>'s standing, walking, firing and pain frames).</summary>
    public const int HeldFrames = 7;

    /// <summary>
    /// Decodes and packs every lump a frame slot of <paramref name="sprites"/>
    /// uses (in sprite, frame and rotation order, each lump once). Patches
    /// with no pixels (0 wide or high) get no slot. T6.14d: then
    /// <paramref name="held"/>'s composites, as the synthetic sprites.
    /// </summary>
    public static SpriteAtlas Build(WadArchive wad, Sprites sprites, HeldWeaponSet? held = null)
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
        int[] heldSprite = new int[(int)weapontype_t.NUMWEAPONS];
        Array.Fill(heldSprite, -1);
        var sources = new List<(weapontype_t, string)?>();
        for (int i = 0; i < lumps.Count; i++)
            sources.Add(null);
        SpriteDef? play = sprites.Find("PLAY");
        if (held is not null && play is not null && play.NumFrames >= HeldFrames)
        {
            var defs = new List<SpriteDef>();
            var slotOf = new Dictionary<(weapontype_t, string), int>();
            for (int w = 0; w < heldSprite.Length; w++)
            {
                if (!held.Has((weapontype_t)w))
                    continue;
                var frames = new SpriteFrame[HeldFrames];
                for (int f = 0; f < HeldFrames; f++)
                {
                    SpriteFrame stock = play.Frames[f];
                    int[] frameLumps = new int[8];
                    bool[] flips = new bool[8];
                    for (int r = 0; r < 8; r++)
                    {
                        int lump = stock.Lump[r];
                        flips[r] = stock.Flip[r];
                        frameLumps[r] = lump;
                        string name = wad.Lumps[lump].Name;
                        if (held.Get((weapontype_t)w, name) is not { } composite || composite.Image.Width == 0 || composite.Image.Height == 0)
                            continue; // (no pose: the stock lump)
                        if (!slotOf.TryGetValue(((weapontype_t)w, name), out int id))
                        {
                            slotOf[((weapontype_t)w, name)] = id = lumps.Count;
                            lumps.Add(wad.NumLumps + id);
                            images.Add(composite.Image);
                            sources.Add(((weapontype_t)w, name));
                        }
                        frameLumps[r] = lumps[id];
                    }
                    frames[f] = new SpriteFrame(stock.Rotate, frameLumps, flips);
                }
                heldSprite[w] = sprites.NumSprites + defs.Count;
                defs.Add(new SpriteDef("#PW" + w.ToString(System.Globalization.CultureInfo.InvariantCulture), frames));
            }
            sprites = sprites.Append(defs);
        }
        if (images.Count == 0)
        {
            // Keep the textures valid: one transparent texel.
            images.Add(new IndexedImage(1, 1, 0, 0, new byte[1], new byte[1]));
            lumps.Add(-1);
            sources.Add(null);
        }
        var atlas = new SpriteAtlas(sprites, lumps, images, TextureAtlas.Build(images), held, heldSprite, [.. sources])
        {
            BuildMilliseconds = clock.Elapsed.TotalMilliseconds
        };
        return atlas;
    }
}
