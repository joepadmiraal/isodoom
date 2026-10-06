using System;
using System.Collections.Generic;

namespace IsoDoom.Wad.Graphics;

/// <summary>The groups of graphics the WAD viewer browses (T1.6).</summary>
public enum GraphicCategory
{
    /// <summary>Composite wall textures from <c>TEXTURE1</c>/<c>TEXTURE2</c>.</summary>
    Textures,

    /// <summary>The flat namespace (<c>F_START</c>..<c>F_END</c>).</summary>
    Flats,

    /// <summary>Sprites from <see cref="Sprites"/>, browsed by sprite, frame and rotation.</summary>
    Sprites,

    /// <summary>The wall patch namespace (<c>P_START</c>..<c>P_END</c>).</summary>
    Patches,

    /// <summary>Patch-format lumps outside the namespaces (<see cref="GraphicLumps"/>).</summary>
    Graphics,

    /// <summary>Synthetic index images that show <c>PLAYPAL</c> and <c>COLORMAP</c>.</summary>
    Palette,
}

/// <summary>
/// One browsable picture: an index image, whether it is drawn mirrored (the
/// second half of a sprite's mirrored pair), and a label for the viewer.
/// </summary>
public readonly record struct GraphicView(GraphicCategory Category, string Name, IndexedImage Image, bool Flip, string Label);

/// <summary>
/// Where the viewer lists a lump: entry <paramref name="Index"/> of
/// <paramref name="Category"/>, and for sprites the frame and rotation slot
/// that show it unmirrored.
/// </summary>
public readonly record struct GraphicLocation(GraphicCategory Category, int Index, int Frame = 0, int Slot = 0);

/// <summary>
/// Everything in a WAD that the viewer can show, as index images (no Godot
/// types; the viewer uploads them). Each category has a name list; sprites are
/// listed by sprite (only those with frames) and picked further by frame and
/// rotation through <see cref="GetSprite"/>, so mirrored rotations come out
/// flipped as the renderer will draw them. Images are decoded on demand.
/// </summary>
public sealed class GraphicsCatalog
{
    /// <summary>Names of the synthetic <see cref="GraphicCategory.Palette"/> views.</summary>
    public const string PlaypalView = "PLAYPAL";
    public const string ColormapView = "COLORMAP";

    private readonly WadArchive _wad;
    private readonly IReadOnlyList<WadLump> _flats;
    private readonly IReadOnlyList<WadLump> _patches;
    private readonly IReadOnlyList<WadLump> _graphics;
    private readonly List<SpriteDef> _sprites = new();
    private Dictionary<WadLump, GraphicLocation>? _locations;

    private GraphicsCatalog(WadArchive wad)
    {
        _wad = wad;
        Playpal = Playpal.Load(wad);
        Colormap = Colormap.Load(wad);
        Textures = Textures.R_InitTextures(wad);
        Sprites = Sprites.R_InitSprites(wad);
        _flats = wad.GetNamespace(LumpNamespace.Flats);
        _patches = wad.GetNamespace(LumpNamespace.Patches);
        _graphics = GraphicLumps.FindGlobalPatches(wad);
        foreach (SpriteDef def in Sprites.SpriteDefs)
        {
            if (def.NumFrames > 0)
                _sprites.Add(def);
        }
    }

    public static GraphicsCatalog Load(WadArchive wad) => new(wad);

    public WadArchive Wad => _wad;
    public Playpal Playpal { get; }
    public Colormap Colormap { get; }
    public Textures Textures { get; }
    public Sprites Sprites { get; }

    /// <summary>Wall texture composition mode used for <see cref="GraphicCategory.Textures"/>.</summary>
    public TextureCompositeMode CompositeMode { get; set; } = TextureCompositeMode.Vanilla;

    /// <summary>The number of entries in a category's list.</summary>
    public int Count(GraphicCategory category) => category switch
    {
        GraphicCategory.Textures => Textures.NumTextures,
        GraphicCategory.Flats => _flats.Count,
        GraphicCategory.Sprites => _sprites.Count,
        GraphicCategory.Patches => _patches.Count,
        GraphicCategory.Graphics => _graphics.Count,
        GraphicCategory.Palette => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    /// <summary>The name of entry <paramref name="index"/> in a category's list.</summary>
    public string GetName(GraphicCategory category, int index) => category switch
    {
        GraphicCategory.Textures => Textures.TextureDefs[index].Name,
        GraphicCategory.Flats => _flats[index].Name,
        GraphicCategory.Sprites => _sprites[index].Name,
        GraphicCategory.Patches => _patches[index].Name,
        GraphicCategory.Graphics => _graphics[index].Name,
        GraphicCategory.Palette => index == 0 ? PlaypalView : index == 1 ? ColormapView : throw new ArgumentOutOfRangeException(nameof(index)),
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    /// <summary>The sprite at <paramref name="index"/> of the <see cref="GraphicCategory.Sprites"/> list.</summary>
    public SpriteDef GetSpriteDef(int index) => _sprites[index];

    /// <summary>
    /// The picture of entry <paramref name="index"/> of a non-sprite category
    /// (sprites need a frame and rotation: <see cref="GetSprite"/>).
    /// </summary>
    public GraphicView Get(GraphicCategory category, int index)
    {
        string name = GetName(category, index);
        switch (category)
        {
            case GraphicCategory.Textures:
            {
                TextureDef def = Textures.TextureDefs[index];
                return new(category, name, Textures.R_GenerateComposite(index, CompositeMode), false,
                    $"{name}  {def.Width}x{def.Height}, {def.Patches.Count} patch(es), {CompositeMode} composite");
            }
            case GraphicCategory.Flats:
            {
                WadLump lump = _flats[index];
                return new(category, name, Flat.Decode(lump.Data.Span, name), false, $"{name}  64x64 flat ({lump.File.Name})");
            }
            case GraphicCategory.Patches:
                return PatchView(category, _patches[index]);
            case GraphicCategory.Graphics:
                return PatchView(category, _graphics[index]);
            case GraphicCategory.Palette:
                return index == 0
                    ? new(category, name, PaletteIndexImage(), false, "PLAYPAL  palette indices 0-255, 16 per row")
                    : new(category, name, ColormapIndexImage(), false,
                        $"COLORMAP  {Colormap.Count} maps of 256 entries, one map per row (map 0 at the top)");
            default:
                throw new ArgumentException("Sprites are picked by frame and rotation; use GetSprite.", nameof(category));
        }
    }

    /// <summary>
    /// Sprite <paramref name="index"/>, frame <paramref name="frame"/> (0 = A)
    /// seen from rotation slot <paramref name="slot"/> (0–7 = rotation 1–8,
    /// as in <see cref="SpriteFrame"/>; any slot of a rotation-0 frame gives
    /// its single lump). <see cref="GraphicView.Flip"/> is set for the mirrored
    /// half of a pair.
    /// </summary>
    public GraphicView GetSprite(int index, int frame, int slot)
    {
        SpriteDef def = _sprites[index];
        SpriteFrame f = def.Frames[frame];
        WadLump lump = _wad.Lumps[f.Lump[slot]];
        bool flip = f.Flip[slot];
        IndexedImage img = Patch.Decode(lump.Data.Span, lump.Name);
        string rotation = f.Rotate ? $"rotation {slot + 1}" : "rotation 0 (all angles)";
        string label = $"{def.Name} frame {(char)('A' + frame)} {rotation}: {lump.Name}{(flip ? " mirrored" : "")}  "
            + $"{img.Width}x{img.Height}, offset {img.LeftOffset},{img.TopOffset}";
        return new(GraphicCategory.Sprites, lump.Name, img, flip, label);
    }

    /// <summary>
    /// Every picture in the catalog: every entry of every category, and for
    /// sprites every frame and rotation (8 slots for rotating frames, 1 for
    /// rotation-0 frames).
    /// </summary>
    public IEnumerable<GraphicView> EnumerateAll()
    {
        foreach (GraphicCategory category in Enum.GetValues<GraphicCategory>())
        {
            for (int i = 0; i < Count(category); i++)
            {
                if (category != GraphicCategory.Sprites)
                {
                    yield return Get(category, i);
                    continue;
                }
                SpriteDef def = _sprites[i];
                for (int frame = 0; frame < def.NumFrames; frame++)
                {
                    int slots = def.Frames[frame].Rotate ? 8 : 1;
                    for (int slot = 0; slot < slots; slot++)
                        yield return GetSprite(i, frame, slot);
                }
            }
        }
    }

    /// <summary>
    /// Where lump <paramref name="lumpIndex"/> (an index into
    /// <see cref="WadArchive.Lumps"/>) is listed, or null when no list shows
    /// it (a non-graphic, an overridden lump, a sprite lump no frame uses).
    /// A sprite lump gives the first frame slot that draws it unmirrored;
    /// <c>PLAYPAL</c> and <c>COLORMAP</c> give their palette views.
    /// </summary>
    public GraphicLocation? Locate(int lumpIndex)
    {
        _locations ??= BuildLocations();
        return _locations.TryGetValue(_wad.Lumps[lumpIndex], out GraphicLocation loc) ? loc : null;
    }

    /// <summary>
    /// The picture of a patch-format or flat lump decoded directly, for lumps
    /// that no list shows (see <see cref="Locate"/>). Flat-namespace lumps
    /// decode as flats, everything else as patches.
    /// </summary>
    public GraphicView ViewLump(WadLump lump, string note)
    {
        if (lump.Namespace == LumpNamespace.Flats)
            return new(GraphicCategory.Flats, lump.Name, Flat.Decode(lump.Data.Span, lump.Name), false,
                $"{lump.Name}  64x64 flat ({lump.File.Name}); {note}");
        IndexedImage img = Patch.Decode(lump.Data.Span, lump.Name);
        GraphicCategory category = lump.Namespace switch
        {
            LumpNamespace.Sprites => GraphicCategory.Sprites,
            LumpNamespace.Patches => GraphicCategory.Patches,
            _ => GraphicCategory.Graphics,
        };
        return new(category, lump.Name, img, false,
            $"{lump.Name}  {img.Width}x{img.Height}, offset {img.LeftOffset},{img.TopOffset} ({lump.File.Name}); {note}");
    }

    private Dictionary<WadLump, GraphicLocation> BuildLocations()
    {
        var map = new Dictionary<WadLump, GraphicLocation>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < _flats.Count; i++)
            map[_flats[i]] = new(GraphicCategory.Flats, i);
        for (int i = 0; i < _patches.Count; i++)
            map[_patches[i]] = new(GraphicCategory.Patches, i);
        for (int i = 0; i < _graphics.Count; i++)
            map[_graphics[i]] = new(GraphicCategory.Graphics, i);
        for (int i = 0; i < _sprites.Count; i++)
        {
            SpriteDef def = _sprites[i];
            for (int frame = 0; frame < def.NumFrames; frame++)
            {
                SpriteFrame f = def.Frames[frame];
                for (int slot = 0; slot < 8; slot++)
                {
                    if (!f.Flip[slot])
                        map.TryAdd(_wad.Lumps[f.Lump[slot]], new(GraphicCategory.Sprites, i, frame, slot));
                }
            }
        }
        // Mirrored-only uses (a lump installed only as the second half of a pair).
        for (int i = 0; i < _sprites.Count; i++)
        {
            SpriteDef def = _sprites[i];
            for (int frame = 0; frame < def.NumFrames; frame++)
            {
                SpriteFrame f = def.Frames[frame];
                for (int slot = 0; slot < 8; slot++)
                    map.TryAdd(_wad.Lumps[f.Lump[slot]], new(GraphicCategory.Sprites, i, frame, slot));
            }
        }
        int playpal = _wad.W_CheckNumForName(PlaypalView), colormap = _wad.W_CheckNumForName(ColormapView);
        if (playpal >= 0)
            map.TryAdd(_wad.Lumps[playpal], new(GraphicCategory.Palette, 0));
        if (colormap >= 0)
            map.TryAdd(_wad.Lumps[colormap], new(GraphicCategory.Palette, 1));
        return map;
    }

    private static GraphicView PatchView(GraphicCategory category, WadLump lump)
    {
        IndexedImage img = Patch.Decode(lump.Data.Span, lump.Name);
        return new(category, lump.Name, img, false,
            $"{lump.Name}  {img.Width}x{img.Height}, offset {img.LeftOffset},{img.TopOffset} ({lump.File.Name})");
    }

    /// <summary>A 16x16 image whose pixel (x, y) is palette index <c>y * 16 + x</c>.</summary>
    private static IndexedImage PaletteIndexImage()
    {
        byte[] pixels = new byte[256];
        byte[] opaque = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            pixels[i] = (byte)i;
            opaque[i] = 1;
        }
        return new IndexedImage(16, 16, 0, 0, pixels, opaque);
    }

    /// <summary>A 256 x maps image whose pixel (x, y) is COLORMAP map y, entry x.</summary>
    private IndexedImage ColormapIndexImage()
    {
        byte[] pixels = Colormap.Data.ToArray();
        byte[] opaque = new byte[pixels.Length];
        Array.Fill(opaque, (byte)1);
        return new IndexedImage(Colormap.MapSize, Colormap.Count, 0, 0, pixels, opaque);
    }
}
