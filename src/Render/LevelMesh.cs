using System;
using System.Collections.Generic;
using Godot;
using IsoDoom.Map;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Render;

/// <summary>
/// The level's GPU side (SPEC §7.2, T2.5): per-sector <see cref="ArrayMesh"/>
/// chunks built from <see cref="FloorTriangles"/> (T2.4) and
/// <see cref="WallSections"/> (T2.3), and the two materials that draw them
/// all: <c>shaders/level.gdshader</c> (floors and solid walls) and
/// <c>shaders/level_masked.gdshader</c> (masked middles, T3.1).
/// <list type="bullet">
/// <item><b>Chunks:</b> sector <c>s</c>'s mesh holds its floor triangles and
/// the solid wall sections of the sides facing into it (their front sector is
/// <c>s</c>) in one surface with <see cref="Material"/>, and those sides'
/// masked middles in a second surface with <see cref="MaskedMaterial"/>
/// (alpha scissor; each surface only when it has something). Sections with
/// texture 0 (<c>-</c>) are not drawn. Every wall quad is one piece of its
/// side (<see cref="WallPieces"/>: per seg, on the floor's corners, vanilla's
/// seg offsets). Vertices hold map x/y and plane references, not
/// heights: the vertex shader places them from the per-sector data texture.</item>
/// <item><b>Textures:</b> every wall texture and flat the level uses goes into
/// one RG8 index atlas (<see cref="TextureAtlas"/>), with every texture or
/// flat it may change to at run time (the other members of a group one of its
/// textures or flats is in: switch pairs, SPEC §12 T5.1, and animation
/// sequences, T5.7); a texture slot's atlas rectangle sits in the
/// <c>texture_info</c> data texture, so an animation re-points a slot to
/// another frame's rectangle (<see cref="TranslateTexture"/>,
/// <see cref="TranslateFlat"/>: vanilla's <c>texturetranslation</c>) without
/// touching the meshes.</item>
/// <item><b>Per-sector data:</b> an RGBA float texture (<see cref="DataWidth"/>
/// texels per row): floor height, ceiling height (map units), light level,
/// the floor flat's slot. Change a <see cref="Sector"/> of <see cref="Level"/>
/// (the sim does, T5.1) and call <see cref="UpdateSectors"/> to move its floor
/// and walls (at heights interpolated between tics when given) and change its flat.</item>
/// <item><b>Per-side textures</b> (T5.1): an RG float texture (<c>side_textures</c>,
/// same layout) with the texture slot of each sidedef's upper, lower and middle
/// texture (R of texel <c>3 × side + </c><see cref="Part"/>; -1 for <c>-</c>), which
/// wall vertices refer to, so a switch changes a wall's texture through
/// <see cref="IsoDoom.Map.Side"/> and <see cref="UpdateSectors"/> without touching the meshes;
/// and (G of all three, T5.7) how far the sidedef's <c>textureoffset</c> moved
/// since the build (map units), which the vertex shader adds to the texture
/// column, so a scrolling wall (linedef special 48) scrolls without a rebuild.</item>
/// <item><b>Light</b> (T2.8): vanilla's <c>zlight</c>/<c>scalelight</c>
/// (<see cref="LightTables"/>) in a small R8 texture, the fake contrast per
/// wall quad (one quad per piece of a side, <see cref="WallPieces"/>: per seg, on the floor's corners),
/// and the distance by <see cref="LightDiminishing"/> (uniforms set with
/// <see cref="SetLightDiminishing"/>, <see cref="SetLightOrigin"/>,
/// <see cref="SetLightReference"/>, <see cref="SetExtraLight"/>,
/// <see cref="SetColormapOverride"/>).</item>
/// <item><b>Masked middles from behind</b> (T3.1a): a masked middle on
/// one side of a line only also gets a back-face quad in the back sector's
/// chunk (<see cref="KindMaskedBack"/>), drawn or collapsed by
/// <see cref="SetMaskedBackFaces"/> (<see cref="MaskedBackFaces"/>).</item>
/// <item><b>Cutaway</b> (T3.4): walls hiding the player (and optionally the
/// cursor ground point) are cut above a height in both materials
/// (<see cref="Render.Cutaway"/>, <see cref="SetCutaway"/>,
/// <see cref="SetCutawayCentres"/>); off until set. Each floor is also
/// in its chunk's solid surface twice more, after the walls, as the caps of
/// the two centres (<see cref="KindCap"/>, T3.4a), collapsed by the vertex
/// shader unless the floor is cut.</item>
/// <item><b>Lids</b> (T6.13b): each lid sector's floor again (<see cref="KindLid"/>,
/// <see cref="DoorLids"/>), which the vertex shader places at its lid height
/// from the <c>sector_lids</c> data texture (the lowest neighbouring
/// ceiling, as drawn) while the sector's ceiling is below it, showing its
/// ceiling flat: the top of a closed door or a lintel. <see cref="SetDoorLids"/>
/// turns them off. The caps also cap a lid's solid where it spans the cutoff.
/// A lid sector's one-sided walls are drawn again from its ceiling up to its
/// lid, facing the void (<see cref="KindLidEnd"/>, T6.13f): the block's ends,
/// where a jamb faces away from the camera and has no cap.</item>
/// <item><b>Wall caps</b> (T6.13e): a top on each one-sided wall, the strip of
/// the void behind it (<see cref="WallCaps"/>; <see cref="KindWallCap"/>), which
/// the vertex shader places at the wall's top (its sector's ceiling, or its lid
/// where it has one) while the wall faces the camera, showing the wall's
/// texture; and once more per cut centre (<see cref="KindWallCapCut"/>) as its
/// cap at the cutoff, as the floors' (T3.4a). The walls of an island (a
/// void enclosed by one loop of walls: a pillar) reach up to the island's top,
/// its highest wall's (<see cref="WallCaps.IslandTop"/>, the <c>wall_islands</c>
/// data texture): each gets its quads again above its ceiling
/// (<see cref="KindWallExtension"/>), and their caps go there.
/// <see cref="SetWallCaps"/> turns them off, extensions included.</item>
/// <item><b>Switch markers</b> (T6.13p): a decal on the floor in front of each
/// switch side (<see cref="SwitchMarkers"/>; <see cref="KindSwitchMarker"/>),
/// which the vertex shader places just above its sector's floor while the
/// wall faces away from the camera and has a height, showing the switch's
/// texture (its current one, from <c>side_textures</c>) laid back onto the
/// floor. <see cref="SetSwitchMarkers"/> turns them off.</item>
/// <item><b>Height cues</b> (T6.13s): a strip on each side of every two-sided
/// line (<see cref="LedgeEdges"/>; <see cref="KindLedge"/>), which the vertex
/// shader places just above its sector's floor while that floor is more than
/// <see cref="LedgeEdges.MinDrop"/> above the floor across the line, drawn in
/// a bright colour: the top edge of a ledge. The floors can also be shaded
/// by their height above the player's (<see cref="SetHeightCues"/>,
/// <see cref="SetHeightReference"/>).</item>
/// </list>
/// Scale: 1 map unit = 1/32 m (SPEC §7.1); map x → +X, map y → −Z, height → +Y.
/// </summary>
public sealed class LevelMesh : IDisposable
{
    public const string ShaderPath = "res://shaders/level.gdshader";

    /// <summary>The masked middles' variant of the level shader (T3.1).</summary>
    public const string MaskedShaderPath = "res://shaders/level_masked.gdshader";

    /// <summary>Map units per Godot metre (SPEC §7.1).</summary>
    public const float MapUnitsPerMetre = 32f;

    /// <summary>Texels per row of the sector and texture-info data textures (the shader's <c>SECTOR_DATA_WIDTH</c>).</summary>
    public const int DataWidth = 256;

    /// <summary>
    /// Vertex kinds (<c>CUSTOM0.x</c>). <see cref="KindMaskedBack"/> (T3.1a) is
    /// the back face of a one-sided masked middle: drawn as a masked middle from
    /// the other side, or collapsed by the vertex shader (<see cref="MaskedBackFaces"/>).
    /// <see cref="KindCap"/> (T3.4a) is a copy of a floor that the vertex shader
    /// places at a cut centre's cutoff (<see cref="CutawayCap"/>; <c>CUSTOM0.w</c>
    /// is the centre, <see cref="CapPlayer"/> or <see cref="CapCursor"/>).
    /// <see cref="KindLid"/> (T6.13b) is a copy of a lid sector's floor that the
    /// vertex shader places at its lid height (<see cref="DoorLids"/>).
    /// <see cref="KindWallCap"/> (T6.13e) is a piece of a one-sided wall's cap
    /// (<see cref="WallCaps"/>), placed at the wall's top; <see cref="KindWallCapCut"/>
    /// the same piece placed at a cut centre's cutoff (<c>CUSTOM0.w</c>) as its cutaway cap;
    /// both have their wall's island in <c>CUSTOM1.x</c> (-1 for none).
    /// <see cref="KindWallExtension"/> (T6.13e) is a quad of an island's one-sided
    /// wall again, from its sector's ceiling up to the island's top (<c>CUSTOM0.w</c>
    /// the island), textured as the wall goes on.
    /// <see cref="KindSwitchMarker"/> (T6.13p) is a piece of a switch side's marker
    /// (<see cref="SwitchMarkers"/>), placed on its sector's floor.
    /// <see cref="KindLedge"/> (T6.13s) is a piece of a ledge edge (<see cref="LedgeEdges"/>),
    /// placed on its sector's floor (<c>CUSTOM0.w</c> the sector across its line).
    /// <see cref="KindLidEnd"/> (T6.13f) is a quad of a lid sector's one-sided wall
    /// again (<see cref="DoorLids.HasEnd"/>), from its sector's ceiling up to its lid,
    /// wound the other way: the end of the lid's block, facing the void.
    /// </summary>
    public const int KindFloor = 0, KindWall = 1, KindMasked = 2, KindMaskedBack = 3, KindCap = 4, KindLid = 5, KindWallCap = 6, KindWallCapCut = 7,
        KindWallExtension = 8, KindSwitchMarker = 9, KindLedge = 10, KindLidEnd = 11;

    /// <summary>A cap vertex's centre (<c>CUSTOM0.w</c>, T3.4a): the shader's <c>cut_player</c> or <c>cut_cursor</c>.</summary>
    public const int CapPlayer = 0, CapCursor = 1;

    /// <summary>
    /// A wall texture of a sidedef (<c>side_textures</c> texel <c>3 × side + part</c>, T5.1):
    /// the upper (<c>toptexture</c>), lower (<c>bottomtexture</c>) or middle texture.
    /// </summary>
    public const int PartTop = 0, PartBottom = 1, PartMiddle = 2;

    /// <summary>
    /// The texture-top plane flag (<c>CUSTOM2.x</c>, T5.1): the vertex shader
    /// adds the slot's texture height to the texture top, for sections whose
    /// texture bottom is pegged to a plane (<see cref="WallSection.BottomPegged"/>).
    /// </summary>
    public const int PlaneAddsTextureHeight = 8;

    /// <summary>Half the height range of a chunk's culling box, in metres (any fixed_t height fits).</summary>
    private const float HeightRange = 32768f / MapUnitsPerMetre;

    private readonly Image _sectorImage;
    private readonly Image _lidImage;
    private readonly Image _islandImage;
    private readonly float[] _islandTops;
    private readonly Color[] _lidTexels;
    private readonly string?[] _lidFlats;
    private Func<int, float>? _drawnCeiling; // a sector's ceiling as written to the sector data (T6.13b)
    private Image _infoImage = null!;
    private readonly Image _sideImage;
    private readonly int[] _textureSlot; // texture number → slot, -1 if unused
    private readonly Dictionary<string, int> _flatSlot = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(int Line, int Side)> _maskedSides = [];

    // What the data textures hold (T5.1): per sector its texel and the floor
    // flat name it was resolved from, per side part the texture name and slot.
    private readonly Color[] _sectorTexels;
    private readonly string?[] _sectorFlats;
    private readonly string?[] _sideNames;
    private readonly int[] _sideSlots;
    private readonly bool[] _sidePartDrawn;
    private readonly int[] _sideBaseOffset; // each sidedef's textureoffset at the build (T5.7)
    private readonly int[] _sideScroll; // and how far it moved since, as uploaded (fixed_t)
    private int[] _slotShows = []; // the slot whose rectangle each slot's texture_info holds (T5.7)
    private bool _infoDirty;
    private readonly HashSet<string> _warned = new(StringComparer.OrdinalIgnoreCase);

    private LevelMesh(Level level, WallSections walls, FloorTriangles floors, DoorLids lids, WallCaps caps, SwitchMarkers markers, LedgeEdges ledges,
        Textures textures, int[] textureSlot, List<string> slotNames, TextureAtlas atlas, Image sectorImage)
    {
        Ledges = ledges;
        Level = level;
        Walls = walls;
        Floors = floors;
        Lids = lids;
        Caps = caps;
        Markers = markers;
        Pieces = WallPieces.Build(level, floors);
        foreach (WallSection s in walls.Sections)
        {
            if (IsDrawn(s) && IsMasked(s))
                _maskedSides.Add((s.Line.Index, s.Side));
        }
        Textures = textures;
        _textureSlot = textureSlot;
        SlotNames = slotNames;
        Atlas = atlas;
        _sectorImage = sectorImage;
        _sectorTexels = new Color[level.Sectors.Length];
        _sectorFlats = new string?[level.Sectors.Length];
        _lidImage = Image.CreateEmpty(DataWidth, Rows(level.Sectors.Length), false, Image.Format.Rgbaf);
        _islandImage = Image.CreateEmpty(DataWidth, Rows(caps.Islands.Count), false, Image.Format.Rf);
        _islandTops = new float[caps.Islands.Count];
        _lidTexels = new Color[level.Sectors.Length];
        _lidFlats = new string?[level.Sectors.Length];
        _sideImage = Image.CreateEmpty(DataWidth, Rows(3 * level.Sides.Length), false, Image.Format.Rgf);
        _sideNames = new string?[3 * level.Sides.Length];
        _sideSlots = new int[3 * level.Sides.Length];
        _sidePartDrawn = new bool[3 * level.Sides.Length];
        _sideBaseOffset = new int[level.Sides.Length];
        _sideScroll = new int[level.Sides.Length];
        foreach (IsoDoom.Map.Side side in level.Sides)
            _sideBaseOffset[side.Index] = side.TextureOffset;
        foreach (WallSection s in walls.Sections)
        {
            if (IsDrawn(s))
                _sidePartDrawn[SidePartId(s)] = true;
        }
    }

    public Level Level { get; }
    public WallSections Walls { get; }
    public FloorTriangles Floors { get; }
    public Textures Textures { get; }

    /// <summary>The lid sectors (T6.13b), whose floor is in their chunk once more as the lid.</summary>
    public DoorLids Lids { get; }

    /// <summary>The wall caps (T6.13e), in their sectors' chunks.</summary>
    public WallCaps Caps { get; }

    /// <summary>The switch markers (T6.13p), in their sectors' chunks.</summary>
    public SwitchMarkers Markers { get; }

    /// <summary>The ledge edges (T6.13s), in their sectors' chunks.</summary>
    public LedgeEdges Ledges { get; }

    /// <summary>The quads of every side (per seg, on the floor's corners, with each seg's fake contrast; T2.9): one wall quad per piece of each drawn section.</summary>
    public WallPieces Pieces { get; }

    /// <summary>The light tables the shader reads (r_main.c, full-screen view; SPEC §12 T2.8).</summary>
    public LightTables Lights { get; } = LightTables.R_InitLightTables();

    public ImageTexture LightTablesTexture { get; private set; } = null!;

    /// <summary>The light settings as last set (<see cref="SetLightDiminishing"/> and friends).</summary>
    public LightDiminishing LightMode { get; private set; } = LightDiminishing.Player;
    public Vector2 LightOrigin { get; private set; }
    public float LightReference { get; private set; } = LightTables.DefaultReferenceDistance;
    public float LightNear { get; private set; } = LightTables.DefaultNearDistance;
    public int ExtraLight { get; private set; }

    /// <summary>The texture atlas; slot <c>i</c>'s rectangle is <c>Atlas.Rects[i]</c>.</summary>
    public TextureAtlas Atlas { get; }

    /// <summary>The name of each texture slot: wall textures first (the drawn sections', then the ones only reachable at run time), then flats.</summary>
    public IReadOnlyList<string> SlotNames { get; }

    /// <summary>
    /// The wall textures in the atlas that no drawn section uses at load: the
    /// other textures of the texture groups (switch pairs) the level uses,
    /// which the sim may change a wall to (SPEC §12 T5.1).
    /// </summary>
    public IReadOnlyCollection<string> RuntimeTextures { get; private set; } = [];

    /// <summary>
    /// The flats in the atlas that no sector's floor uses at load: the other
    /// frames of the animated flat sequences the level draws (T5.7).
    /// </summary>
    public IReadOnlyCollection<string> RuntimeFlats { get; private set; } = [];

    /// <summary>
    /// Run-time texture or flat changes to a name the atlas lacks (SPEC §12
    /// T5.1): the wall or floor keeps its previous texture (a warning per name).
    /// Zero unless a group of <see cref="Build"/> missed a texture.
    /// </summary>
    public int RuntimeMisses { get; private set; }

    /// <summary>The chunk of each sector (null when the sector has no floor and no wall).</summary>
    public ArrayMesh?[] SectorMeshes { get; private set; } = [];

    /// <summary>The material of every chunk's floor and solid walls (its first surface).</summary>
    public ShaderMaterial Material { get; private set; } = null!;

    /// <summary>The material of the masked middles (a chunk's last surface, when it has any; T3.1). Its parameters follow <see cref="Material"/>'s.</summary>
    public ShaderMaterial MaskedMaterial { get; private set; } = null!;

    /// <summary>
    /// The material of the level's thing sprites (T3.5, <c>shaders/sprite.gdshader</c>,
    /// used by <see cref="ThingSprites"/>): it gets every shared parameter
    /// (palette, light tables and settings, sector data) with the other two;
    /// its sprite atlas is set by <see cref="ThingSprites.Bind"/>.
    /// </summary>
    public ShaderMaterial SpriteMaterial { get; private set; } = null!;

    /// <summary>
    /// The fuzz pass of the thing sprites (T6.9, <c>shaders/sprite_fuzz.gdshader</c>,
    /// <see cref="Fuzz"/>): <see cref="SpriteMaterial"/>'s next pass, drawing
    /// only the things with <c>MF_SHADOW</c>; it gets every parameter the
    /// sprite material gets.
    /// </summary>
    public ShaderMaterial FuzzMaterial { get; private set; } = null!;

    /// <summary>The material of the things' blob shadows (T3.6, <c>shaders/sprite_shadow.gdshader</c>, <see cref="ThingSprites.Shadows"/>); set by <see cref="SetSprites"/>.</summary>
    public ShaderMaterial ShadowMaterial { get; private set; } = null!;

    /// <summary>Every material (level, masked, sprites and their fuzz pass), for setting a shader parameter on each.</summary>
    public IEnumerable<ShaderMaterial> Materials => [Material, MaskedMaterial, SpriteMaterial, FuzzMaterial];

    public ImageTexture AtlasTexture { get; private set; } = null!;
    public ImageTexture TextureInfoTexture { get; private set; } = null!;
    public ImageTexture SectorDataTexture { get; private set; } = null!;

    /// <summary>The lids' data texture (T6.13b, <c>sector_lids</c>, RGBA float, one texel per sector): lid height (map units; <see cref="DoorLids.None"/> without a lid), ceiling flat slot, 1 for a door (T6.13d, <see cref="DoorLids.IsDoor"/>), 1 when its ceiling moves (<see cref="DoorLids.MovesCeiling"/>: the upper walls over it are kept).</summary>
    public ImageTexture LidDataTexture { get; private set; } = null!;

    /// <summary>Number of lid triangles (T6.13b).</summary>
    public int LidTriangleCount { get; private set; }

    /// <summary>Number of wall cap triangles (T6.13e; each also twice more as cutaway caps).</summary>
    public int WallCapTriangleCount { get; private set; }

    /// <summary>Number of switch marker triangles (T6.13p, <see cref="KindSwitchMarker"/>).</summary>
    public int SwitchMarkerTriangleCount { get; private set; }

    /// <summary>Number of ledge edge triangles (T6.13s, <see cref="KindLedge"/>).</summary>
    public int LedgeTriangleCount { get; private set; }

    /// <summary>Number of quads that draw the islands' walls on above their ceilings (T6.13e, <see cref="KindWallExtension"/>).</summary>
    public int WallExtensionQuads { get; private set; }

    /// <summary>Number of quads that close the ends of the lids' blocks (T6.13f, <see cref="KindLidEnd"/>).</summary>
    public int LidEndQuads { get; private set; }

    /// <summary>The islands' data texture (T6.13e, <c>wall_islands</c>, R float, one texel per island): its top, map units.</summary>
    public ImageTexture IslandDataTexture { get; private set; } = null!;

    /// <summary>The per-side texture slots (T5.1, <c>side_textures</c>).</summary>
    public ImageTexture SideTexturesTexture { get; private set; } = null!;

    /// <summary>Number of solid wall quads in the meshes (one per <see cref="WallPiece"/> of each drawn section), of masked middle quads, and of floor triangles.</summary>
    public int WallQuads { get; private set; }
    public int MaskedQuads { get; private set; }

    /// <summary>Number of back-face quads of one-sided masked middles (<see cref="HasBackFace"/>, T3.1a), drawn or not as <see cref="MaskedBacks"/> says.</summary>
    public int MaskedBackQuads { get; private set; }
    public int FloorTriangleCount { get; private set; }

    /// <summary>The level's bounds in Godot space at its load-time heights.</summary>
    public Aabb Bounds { get; private set; }

    /// <summary>The slot of wall texture <paramref name="texnum"/>, or -1 when the level does not use it.</summary>
    public int TextureSlot(int texnum) => texnum > 0 && texnum < _textureSlot.Length ? _textureSlot[texnum] : -1;

    /// <summary>The slot of flat <paramref name="name"/>, or -1 when the level does not use it.</summary>
    public int FlatSlot(string name) => _flatSlot.TryGetValue(name, out int slot) ? slot : -1;

    /// <summary>
    /// Builds the meshes, atlas, data textures and material of <paramref name="level"/>.
    /// <paramref name="textureGroups"/> are wall textures that change into each
    /// other at run time (switch pairs, <c>IsoDoom.Sim.Switches</c>; animation
    /// sequences, <c>IsoDoom.Sim.PicAnims</c>): when the level draws one
    /// texture of a group, the atlas gets all of them (the ones the WAD has;
    /// SPEC §12 T5.1). <paramref name="flatGroups"/> are the same for floor
    /// flats (the animated flat sequences, T5.7). <paramref name="switchPairs"/>
    /// are the switch textures (off, on; p_switch.c <c>alphSwitchList</c>'s for
    /// the game), whose sides get a marker (T6.13p, <see cref="SwitchMarkers"/>).
    /// </summary>
    public static LevelMesh Build(WadArchive wad, Level level, Textures textures, Playpal playpal, Colormap colormap,
        TextureCompositeMode compositeMode = TextureCompositeMode.Vanilla, IEnumerable<IReadOnlyList<string>>? textureGroups = null,
        IEnumerable<IReadOnlyList<string>>? flatGroups = null, IEnumerable<(string Off, string On)>? switchPairs = null)
    {
        var walls = WallSections.Build(level, textures);
        var polygons = SubsectorPolygons.Build(level);
        var floors = FloorTriangles.Build(level, polygons);
        var lids = DoorLids.Build(level, floors);
        var caps = WallCaps.Build(level, polygons);
        var markers = SwitchMarkers.Build(walls, polygons, SwitchRows(textures, walls, switchPairs, compositeMode));
        var ledges = LedgeEdges.Build(level, polygons);

        // Texture slots: the wall textures the drawn sections use, then the floor flats.
        var images = new List<IndexedImage>();
        var names = new List<string>();
        int[] textureSlot = new int[textures.NumTextures];
        Array.Fill(textureSlot, -1);
        foreach (WallSection s in walls.Sections)
        {
            if (!IsDrawn(s) || textureSlot[s.Texture] >= 0)
                continue;
            textureSlot[s.Texture] = images.Count;
            images.Add(textures.R_GenerateComposite(s.Texture, compositeMode));
            names.Add(textures.TextureDefs[s.Texture].Name);
        }
        var runtime = new List<string>();
        foreach (IReadOnlyList<string> group in textureGroups ?? [])
        {
            var nums = new List<int>();
            bool used = false;
            foreach (string name in group)
            {
                int num = textures.R_CheckTextureNumForName(name);
                if (num <= 0)
                    continue;
                nums.Add(num);
                used |= textureSlot[num] >= 0;
            }
            if (!used)
                continue;
            foreach (int num in nums)
            {
                if (textureSlot[num] >= 0)
                    continue;
                textureSlot[num] = images.Count;
                images.Add(textures.R_GenerateComposite(num, compositeMode));
                names.Add(textures.TextureDefs[num].Name);
                runtime.Add(textures.TextureDefs[num].Name);
            }
        }
        var flatSlot = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Sector sector in level.Sectors)
        {
            if (flatSlot.ContainsKey(sector.FloorPic))
                continue;
            flatSlot[sector.FloorPic] = images.Count;
            images.Add(Flat.Load(wad, sector.FloorPic));
            names.Add(sector.FloorPic);
        }
        // T6.13b: the lids show their sector's ceiling flat.
        foreach (int i in lids.Sectors)
        {
            string pic = level.Sectors[i].CeilingPic;
            if (flatSlot.ContainsKey(pic) || wad.Find(pic, LumpNamespace.Flats) is null)
                continue;
            flatSlot[pic] = images.Count;
            images.Add(Flat.Load(wad, pic));
            names.Add(pic);
        }
        var runtimeFlats = new List<string>();
        foreach (IReadOnlyList<string> group in flatGroups ?? [])
        {
            bool used = false;
            foreach (string name in group)
                used |= flatSlot.ContainsKey(name);
            if (!used)
                continue;
            foreach (string name in group)
            {
                if (flatSlot.ContainsKey(name) || wad.Find(name, LumpNamespace.Flats) is null)
                    continue;
                flatSlot[name] = images.Count;
                images.Add(Flat.Load(wad, name));
                names.Add(name);
                runtimeFlats.Add(name);
            }
        }
        if (images.Count == 0)
            throw new WadFormatException($"{level.Name}: nothing to draw");
        var atlas = TextureAtlas.Build(images);

        var mesh = new LevelMesh(level, walls, floors, lids, caps, markers, ledges, textures, textureSlot, names, atlas,
            Image.CreateEmpty(DataWidth, Rows(level.Sectors.Length), false, Image.Format.Rgbaf));
        foreach ((string? name, int slot) in flatSlot)
            mesh._flatSlot[name] = slot;
        mesh.RuntimeTextures = runtime;
        mesh.RuntimeFlats = runtimeFlats;
        mesh.CreateTextures(playpal, colormap);
        mesh.BuildChunks();
        return mesh;
    }

    /// <summary>
    /// The rows each switch texture's marker shows (T6.13p, <see cref="SwitchMarkers.Band"/>),
    /// by name: both textures of every pair (both in the WAD) one of which a section of <paramref name="walls"/> draws.
    /// </summary>
    public static Dictionary<string, (int Top, int Bottom)> SwitchRows(Textures textures, WallSections walls, IEnumerable<(string Off, string On)>? pairs,
        TextureCompositeMode compositeMode = TextureCompositeMode.Vanilla)
    {
        var rows = new Dictionary<string, (int Top, int Bottom)>(StringComparer.OrdinalIgnoreCase);
        var drawn = new HashSet<int>();
        foreach (WallSection s in walls.Sections)
        {
            if (IsDrawn(s))
                drawn.Add(s.Texture);
        }
        foreach ((string off, string on) in pairs ?? [])
        {
            int a = textures.R_CheckTextureNumForName(off), b = textures.R_CheckTextureNumForName(on);
            if (a <= 0 || b <= 0 || (!drawn.Contains(a) && !drawn.Contains(b)))
                continue;
            rows[off] = rows[on] = SwitchMarkers.Band(textures.R_GenerateComposite(a, compositeMode), textures.R_GenerateComposite(b, compositeMode));
        }
        return rows;
    }

    /// <summary>Whether the meshes draw <paramref name="s"/>: every section with a texture (<see cref="IsMasked"/> ones in the masked pass).</summary>
    public static bool IsDrawn(WallSection s) => s.Texture != 0;

    /// <summary>Whether <paramref name="s"/> is drawn by <see cref="MaskedMaterial"/> (a masked middle, T3.1).</summary>
    public static bool IsMasked(WallSection s) => s.Kind == WallSectionKind.MaskedMiddle;

    /// <summary>
    /// Whether <paramref name="s"/> gets a back-face quad (T3.1a): a drawn
    /// masked middle of a two-sided line whose other side has none, so vanilla
    /// shows it from one side only.
    /// </summary>
    public bool HasBackFace(WallSection s) =>
        IsDrawn(s) && IsMasked(s) && s.BackSector is not null && !_maskedSides.Contains((s.Line.Index, s.Side ^ 1));

    /// <summary>
    /// A plane reference seen from the other side of the line (front ↔ back;
    /// the higher floor and lower ceiling stay): a back face's
    /// <c>CUSTOM1</c>/<c>CUSTOM2</c> planes with its sectors swapped (T3.1a).
    /// </summary>
    public static WallPlane OtherSide(WallPlane plane) => plane switch
    {
        WallPlane.FrontFloor => WallPlane.BackFloor,
        WallPlane.FrontCeiling => WallPlane.BackCeiling,
        WallPlane.BackFloor => WallPlane.FrontFloor,
        WallPlane.BackCeiling => WallPlane.FrontCeiling,
        _ => plane,
    };

    /// <summary>Whether one-sided masked middles are drawn from behind as well (<see cref="MaskedBackFaces"/>, T3.1a), as last set.</summary>
    public MaskedBackFaces MaskedBacks { get; private set; } = MaskedBackFaces.Mirrored;

    /// <summary>Selects whether one-sided masked middles are drawn from behind (T3.1a).</summary>
    public void SetMaskedBackFaces(MaskedBackFaces mode)
    {
        MaskedBacks = mode;
        SetParameter("masked_backs", (int)mode);
    }

    /// <summary>A map position (fixed_t x, y) and height (map units) in Godot space.</summary>
    public static Vector3 ToGodot(int x, int y, float height) =>
        new((float)(x / 65536.0 / MapUnitsPerMetre), height / MapUnitsPerMetre, (float)(-y / 65536.0 / MapUnitsPerMetre));

    /// <summary>Selects the wall tiling (<see cref="WallTextureTiling"/>).</summary>
    public void SetWallTiling(WallTextureTiling tiling) => SetParameter("wall_tiling", (int)tiling);

    /// <summary>
    /// Forces one COLORMAP row everywhere (0–33), or -1 for the sector lights:
    /// vanilla's <c>fixedcolormap</c> (0 full bright, 1 light amplification
    /// visor, 32 <c>INVERSECOLORMAP</c> for invulnerability).
    /// </summary>
    public void SetColormapOverride(int map)
    {
        ColormapOverride = map;
        SetParameter("colormap_override", map);
    }

    /// <summary>The forced COLORMAP row (<see cref="SetColormapOverride"/>), -1 for none.</summary>
    public int ColormapOverride { get; private set; } = -1;

    /// <summary>Selects the distance light diminishing uses (<see cref="LightDiminishing"/>).</summary>
    public void SetLightDiminishing(LightDiminishing mode)
    {
        LightMode = mode;
        SetParameter("light_mode", (int)mode);
    }

    /// <summary>The player position for <see cref="LightDiminishing.Player"/>, in map units (x, y).</summary>
    public void SetLightOrigin(Vector2 mapUnits)
    {
        LightOrigin = mapUnits;
        SetParameter("light_origin", mapUnits);
    }

    /// <summary>The distance of <see cref="LightDiminishing.None"/>, in map units.</summary>
    public void SetLightReference(float mapUnits)
    {
        LightReference = mapUnits;
        SetParameter("light_reference", mapUnits);
    }

    /// <summary>
    /// The shortest distance <see cref="LightDiminishing.Player"/> feeds the
    /// tables, in map units (T3.7; 0 uses them down to the player's position).
    /// </summary>
    public void SetLightNear(float mapUnits)
    {
        LightNear = mapUnits;
        SetParameter("light_near", mapUnits);
    }

    /// <summary>r_main.c <c>extralight</c> (the player's weapon flash, 0–2), added to every light number.</summary>
    public void SetExtraLight(int extralight)
    {
        ExtraLight = extralight;
        SetParameter("extralight", extralight);
    }

    /// <summary>The cutaway settings as last set (<see cref="SetCutaway"/>; off until set).</summary>
    public CutawaySettings Cutaway { get; private set; } = new() { Style = CutawayStyle.Off };

    /// <summary>The cut centres as last set (<see cref="SetCutawayCentres"/>), map units (x, y, floor height); null: none.</summary>
    public Vector3? CutPlayer { get; private set; }
    public Vector3? CutCursor { get; private set; }

    /// <summary>Sets the wall cutaway's style, radius and cutoff height (T3.4, <see cref="IsoDoom.Render.Cutaway"/>), its cap (T3.4a) and the things it cuts (T3.4b; on <see cref="SpriteMaterial"/>).</summary>
    public void SetCutaway(CutawaySettings settings)
    {
        Cutaway = settings;
        SetParameter("cut_mode", Render.Cutaway.ShaderMode(settings.Style));
        SetParameter("cut_radius", settings.Radius);
        SetParameter("cut_height", settings.Height);
        SetParameter("cut_anchor", Render.Cutaway.Anchor);
        SetParameter("cut_cap", (int)settings.Cap);
        SetParameter("cut_things", (int)settings.Things);
        SetParameter("cut_doors", (int)settings.Doors);
    }

    /// <summary>
    /// Sets the cutaway's centres: the player's position on its floor and the
    /// cursor ground point (map units x, y, floor height; null: that centre
    /// cuts nothing). Whether the cursor is passed is the caller's choice
    /// (<see cref="CutawaySettings.Cursor"/>).
    /// </summary>
    public void SetCutawayCentres(Vector3? player, Vector3? cursor)
    {
        CutPlayer = player;
        CutCursor = cursor;
        SetParameter("cut_player", player is Vector3 p ? new Vector4(p.X, p.Y, p.Z, 1) : Vector4.Zero);
        SetParameter("cut_cursor", cursor is Vector3 c ? new Vector4(c.X, c.Y, c.Z, 1) : Vector4.Zero);
    }

    /// <summary>Whether the lids are drawn (T6.13b), as last set (<see cref="SetDoorLids"/>; on until set).</summary>
    public DoorLidMode LidMode { get; private set; } = DoorLidMode.On;

    /// <summary>Turns the lids (T6.13b, <see cref="Lids"/>) on or off, and with them the caps of their solids.</summary>
    public void SetDoorLids(DoorLidMode mode)
    {
        LidMode = mode;
        SetParameter("lids", (int)mode);
    }

    /// <summary>Whether the wall caps are drawn (T6.13e), as last set (<see cref="SetWallCaps"/>; on until set).</summary>
    public WallCapMode CapMode { get; private set; } = WallCapMode.On;

    /// <summary>Turns the wall caps (T6.13e, <see cref="Caps"/>) on or off, and with them their cutaway caps.</summary>
    public void SetWallCaps(WallCapMode mode)
    {
        CapMode = mode;
        SetParameter("wall_caps", (int)mode);
    }

    /// <summary>Whether the switch markers are drawn (T6.13p), as last set (<see cref="SetSwitchMarkers"/>; on until set).</summary>
    public SwitchMarkerMode MarkerMode { get; private set; } = SwitchMarkerMode.On;

    /// <summary>Turns the switch markers (T6.13p, <see cref="Markers"/>) on or off.</summary>
    public void SetSwitchMarkers(SwitchMarkerMode mode)
    {
        MarkerMode = mode;
        SetParameter("switch_markers", (int)mode);
    }

    /// <summary>The height cues (T6.13s), as last set (<see cref="SetHeightCues"/>; <see cref="HeightCueMode.Shade"/> until set).</summary>
    public HeightCueMode HeightCues { get; private set; } = HeightCueMode.Shade;

    /// <summary>Draws the ledge edges (<see cref="Ledges"/>), the height shading of the floors, both or neither (T6.13s).</summary>
    public void SetHeightCues(HeightCueMode mode)
    {
        HeightCues = mode;
        SetParameter("height_cues", (int)mode);
    }

    /// <summary>The height the shading (<see cref="HeightCueMode.Shade"/>) is relative to, map units, as last set (<see cref="SetHeightReference"/>): the player's floor; null shades nothing.</summary>
    public float? HeightReference { get; private set; }

    /// <summary>Sets the height the floors are shaded against (T6.13s): the player's feet, map units; null, none (no shading).</summary>
    public void SetHeightReference(float? height)
    {
        HeightReference = height;
        SetParameter("height_reference", height ?? 0f);
        SetParameter("height_reference_on", height is null ? 0 : 1);
    }

    /// <summary>Which upper walls are drawn, as last set (<see cref="SetUpperWalls"/>; <see cref="UpperWallMode.Doors"/> until set).</summary>
    public UpperWallMode UpperMode { get; private set; } = UpperWallMode.Doors;

    /// <summary>Draws every upper wall, or only those over the sectors that keep them (<see cref="DoorLids.KeepsUppers"/>).</summary>
    public void SetUpperWalls(UpperWallMode mode)
    {
        UpperMode = mode;
        SetParameter("upper_walls", (int)mode);
    }

    /// <summary>The fog of war's style as last set (<see cref="SetFog"/>; off until set). T6.13l.</summary>
    public FogStyle FogMode { get; private set; } = FogStyle.Off;

    /// <summary>The dim mapping in <c>fog_dim</c> (<see cref="FogOfWar.DimMap"/>), as last set (<see cref="SetFogDim"/>).</summary>
    public FogDim FogDimLook { get; private set; } = FogDim.Default;

    /// <summary>The fog of war's data texture (T6.13l, <c>sector_fog</c>, R8, one texel per sector): its <see cref="SectorSight"/>.</summary>
    public ImageTexture FogDataTexture { get; private set; } = null!;

    private Image _fogImage = null!;
    private byte[] _fogBytes = [];
    private int _fogVersion = -1;
    private FogOfWar? _fogSource;
    private ImageTexture _fogDimTexture = null!;

    /// <summary>Draws the sectors the player has not seen dimmed, hidden or as the others (T6.13l, <see cref="FogStyle"/>).</summary>
    public void SetFog(FogStyle style)
    {
        FogMode = style;
        SetParameter("fog_mode", (int)style);
    }

    /// <summary>
    /// Copies the sectors' states of <paramref name="fog"/> into <c>sector_fog</c>
    /// when they changed since the last upload (<see cref="FogOfWar.Version"/>);
    /// null shows every sector as visible.
    /// </summary>
    public void UpdateFog(FogOfWar? fog)
    {
        if (fog == _fogSource && (fog is null || fog.Version == _fogVersion))
            return;
        _fogSource = fog;
        _fogVersion = fog?.Version ?? -1;
        for (int i = 0; i < Level.Sectors.Length; i++)
            _fogBytes[i] = (byte)(fog is null ? SectorSight.Visible : fog.State(i));
        _fogImage.SetData(DataWidth, Rows(Level.Sectors.Length), false, Image.Format.R8, _fogBytes);
        FogDataTexture.Update(_fogImage);
    }

    /// <summary>The <c>sector_fog</c> texel of <paramref name="sector"/> as uploaded.</summary>
    public SectorSight FogData(int sector) => (SectorSight)_fogBytes[sector];

    /// <summary>The dim mapping as uploaded (<c>fog_dim</c>): the palette index each texel index of an unseen surface is drawn as.</summary>
    public byte[] FogDimMap { get; private set; } = [];

    /// <summary>Sets how dim draws an unseen surface (<see cref="FogDim"/>: its colormap row, grey and dither).</summary>
    public void SetFogDim(FogDim look)
    {
        FogDimLook = look;
        FogDimMap = FogOfWar.DimMap(_playpal!, _colormap!, look.Row, look.Grey);
        using Image dim = Image.CreateFromData(256, 1, false, Image.Format.R8, FogDimMap);
        _fogDimTexture.Update(dim);
        SetParameter("fog_dither", look.Dither ? 1 : 0);
    }

    /// <summary>The sprite readability settings as last set (<see cref="SetSprites"/>; the defaults until set).</summary>
    public SpriteSettings Sprites { get; private set; } = new();

    /// <summary>Sets the thing sprites' tilt, outline and blob shadows (T3.6), wall pull (T3.5a), upright hiding (T3.6a) and the player's minimum light (T4.7a; <see cref="SpriteSettings"/>).</summary>
    public void SetSprites(SpriteSettings settings)
    {
        Sprites = settings;
        // The upright hiding (T3.6a) is a shader variant; the parameters stay on the material.
        bool upright = settings.Hidden == SpriteHidden.Upright;
        Shader shader = GodotRefs.LoadShader(upright ? ThingSprites.HiddenShaderPath : ThingSprites.ShaderPath);
        if (SpriteMaterial.Shader != shader)
            SpriteMaterial.Shader = shader;
        Shader fuzz = GodotRefs.LoadShader(upright ? ThingSprites.FuzzHiddenShaderPath : ThingSprites.FuzzShaderPath);
        if (FuzzMaterial.Shader != fuzz)
            FuzzMaterial.Shader = fuzz;
        foreach (ShaderMaterial material in new[] { SpriteMaterial, FuzzMaterial })
        {
            material.SetParam("tilt", Math.Clamp(settings.Tilt, 0f, 1f));
            material.SetParam("tilt_depth", (int)settings.TiltDepth);
            material.SetParam("outline", settings.Outline);
            material.SetParam("wall_pull", Math.Max(settings.WallPull, 0f));
            material.SetParam("own_light", Math.Clamp(settings.PlayerLight, 0, 255));
            material.SetParam("fuzz", settings.Fuzz ? 1 : 0);
        }
        ShadowMaterial.SetParam("shadow_mode", (int)settings.Shadow);
        ShadowMaterial.SetParam("shadow_opacity", settings.ShadowOpacity);
    }

    /// <summary>
    /// Selects the PLAYPAL palette (0 normal, 1–8 red, 9–12 gold, 13 radiation
    /// suit; T6.8: st_stuff.c's flashes) for the level and its sprites; the
    /// blob shadows take its colour 0 (black in palette 0). T6.13v: below
    /// <see cref="PaletteFlash.FullStrength"/>, <paramref name="strength"/>
    /// percent of the way from palette 0 (<see cref="PaletteFlash.Blend(Playpal, int, int)"/>,
    /// written to the texture's <see cref="BlendRow"/>); with
    /// <paramref name="playerOnly"/> the level stays on palette 0 and only the
    /// player's own sprite (<see cref="ThingSprites.FlagOwn"/>) shows it, a
    /// pickup's in white (<see cref="PaletteFlash.PlayerPalette"/>, always in
    /// the <see cref="BlendRow"/>).
    /// </summary>
    public void SetPalette(int palette, int strength = PaletteFlash.FullStrength, bool playerOnly = false)
    {
        Palette = palette;
        PaletteStrength = strength;
        PalettePlayerOnly = playerOnly;
        int row = palette;
        bool white = playerOnly && PaletteFlash.IsBonus(palette);
        if (_playpal is { } playpal && palette > 0 && palette < playpal.Count && (strength < PaletteFlash.FullStrength || white))
        {
            row = BlendRow;
            if (_blendRow != palette || _blendStrength != strength || _blendWhite != white)
            {
                (_blendRow, _blendStrength, _blendWhite) = (palette, strength, white);
                if (white)
                    PaletteFlash.PlayerPalette(playpal, palette, strength).CopyTo(_blend, 0);
                else
                    PaletteFlash.Blend(playpal.GetPalette(0), playpal.GetPalette(palette), strength, _blend);
                _blend.CopyTo(_playpalRows.AsSpan(row * Playpal.PaletteSize));
                _playpalImage!.SetData(256, playpal.Count + 1, false, Image.Format.Rgb8, _playpalRows);
                _playpalTexture!.Update(_playpalImage);
            }
        }
        ScreenRow = playerOnly ? 0 : row;
        OwnRow = row;
        SetParameter("palette_index", ScreenRow);
        SetParameter("own_palette_index", OwnRow);
        if (_playpal is not null && ScreenRow >= 0 && ScreenRow <= BlendRow)
        {
            (byte r, byte g, byte b) = DrawnColor(ScreenRow, 0);
            ShadowMaterial.SetParam("shadow_colour", Color.Color8(r, g, b).SrgbToLinear());
        }
    }

    /// <summary>The flash's strength as last set (<see cref="SetPalette"/>, T6.13v), in percent.</summary>
    public int PaletteStrength { get; private set; } = PaletteFlash.FullStrength;

    /// <summary>Whether the flash shows on the player's own sprite only (<see cref="SetPalette"/>, T6.13v).</summary>
    public bool PalettePlayerOnly { get; private set; }

    /// <summary>The palette texture's row of the blended flash palette (T6.13v): the one after PLAYPAL's.</summary>
    public int BlendRow => _playpal?.Count ?? Playpal.NumPalettes;

    /// <summary>The palette texture's row the level and its sprites are drawn in (<c>palette_index</c>; <see cref="BlendRow"/> for a blended flash).</summary>
    public int ScreenRow { get; private set; }

    /// <summary>The palette texture's row the player's own sprite is drawn in (<c>own_palette_index</c>, T6.13v).</summary>
    public int OwnRow { get; private set; }

    /// <summary>The colour of <paramref name="index"/> in the palette texture's row <paramref name="row"/> (a PLAYPAL palette, or the <see cref="BlendRow"/>).</summary>
    public (byte R, byte G, byte B) DrawnColor(int row, int index) =>
        row == BlendRow ? (_blend[index * 3], _blend[index * 3 + 1], _blend[index * 3 + 2]) : _playpal!.GetColor(row, index);

    /// <summary>The 768 bytes of the palette texture's row <paramref name="row"/> (<see cref="DrawnColor"/>).</summary>
    public ReadOnlySpan<byte> DrawnPalette(int row) => row == BlendRow ? _blend : _playpal!.GetPalette(row);

    private byte[] _playpalRows = [];
    private Image? _playpalImage;
    private ImageTexture? _playpalTexture;
    private ImageTexture? _colormapTexture;

    /// <summary>The <c>playpal</c> texture every material samples: PLAYPAL's palettes, then the <see cref="BlendRow"/> (P5: the level check reads it back).</summary>
    public ImageTexture? PlaypalTexture => _playpalTexture;

    /// <summary>The <c>colormap</c> texture every material samples: COLORMAP (P5: the level check reads it back).</summary>
    public ImageTexture? ColormapTexture => _colormapTexture;
    private readonly byte[] _blend = new byte[Playpal.PaletteSize];
    private int _blendRow = -1, _blendStrength;
    private bool _blendWhite;

    /// <summary>The fuzz's phase (T6.9, <see cref="Fuzz.Phase"/>, 0–49) as last set (<see cref="SetFuzzPhase"/>).</summary>
    public int FuzzPhase { get; private set; }

    /// <summary>Sets the fuzz's phase (T6.9): vanilla's <c>fuzzpos</c> at a fuzzed thing's first texel, 0–49.</summary>
    public void SetFuzzPhase(int phase)
    {
        FuzzPhase = ((phase % Fuzz.FUZZTABLE) + Fuzz.FUZZTABLE) % Fuzz.FUZZTABLE;
        FuzzMaterial.SetParam("fuzz_phase", FuzzPhase);
    }

    /// <summary>The PLAYPAL palette as last set (<see cref="SetPalette"/>).</summary>
    public int Palette { get; private set; }

    private Playpal? _playpal;
    private Colormap? _colormap;

    /// <summary>A sector's floor and ceiling heights to draw, in map units (T5.1: interpolated between tics).</summary>
    public delegate (float Floor, float Ceiling) SectorHeights(Sector sector);

    /// <summary>
    /// Copies every sector's floor and ceiling height (its current ones, or
    /// <paramref name="heights"/>'), light level and floor flat from
    /// <see cref="Level"/> into the sector data texture, and every sidedef's
    /// texture names into the side texture slots, so the meshes follow (T5.1).
    /// Uploads only what changed. A flat or texture the atlas lacks keeps the
    /// previous one (<see cref="RuntimeMisses"/>).
    /// </summary>
    public void UpdateSectors(SectorHeights? heights = null)
    {
        bool dirty = false;
        foreach (Sector s in Level.Sectors)
            dirty |= WriteSector(s, heights);
        if (dirty)
            SectorDataTexture.Update(_sectorImage);
        if (WriteLids())
            LidDataTexture.Update(_lidImage);
        if (WriteIslands())
            IslandDataTexture.Update(_islandImage);
        if (WriteSides())
            SideTexturesTexture.Update(_sideImage);
        if (_infoDirty)
        {
            TextureInfoTexture.Update(_infoImage);
            _infoDirty = false;
        }
    }

    /// <summary>
    /// Draws wall texture <paramref name="texnum"/> as texture <paramref name="to"/>
    /// (T5.7, r_data.c <c>texturetranslation[texnum] = to</c>: an animation's
    /// frame): re-points its slot's <c>texture_info</c> texel to the atlas
    /// rectangle of <paramref name="to"/>'s slot, uploaded by the next
    /// <see cref="UpdateSectors"/>. Nothing when the level does not use
    /// <paramref name="texnum"/>; a <paramref name="to"/> the atlas lacks is a
    /// miss (<see cref="RuntimeMisses"/>; the slot keeps what it shows).
    /// </summary>
    public void TranslateTexture(int texnum, int to)
    {
        int slot = TextureSlot(texnum);
        if (slot >= 0)
            Show(slot, TextureSlot(to), to > 0 && to < Textures.NumTextures ? Textures.TextureDefs[to].Name : $"#{to}");
    }

    /// <summary>As <see cref="TranslateTexture"/> for floor flat <paramref name="name"/>, drawn as flat <paramref name="to"/> (vanilla's <c>flattranslation</c>).</summary>
    public void TranslateFlat(string name, string to)
    {
        int slot = FlatSlot(name);
        if (slot >= 0)
            Show(slot, FlatSlot(to), to);
    }

    /// <summary>The slot whose atlas rectangle slot <paramref name="slot"/> draws (itself unless an animation translated it, T5.7).</summary>
    public int SlotShows(int slot) => _slotShows[slot];

    private void Show(int slot, int toSlot, string to)
    {
        if (toSlot < 0)
        {
            Miss($"animation frame {to} (slot {slot}, {SlotNames[slot]})");
            return;
        }
        if (_slotShows[slot] == toSlot)
            return;
        _slotShows[slot] = toSlot;
        AtlasRect r = Atlas.Rects[toSlot];
        _infoImage.SetPixel(slot % DataWidth, slot / DataWidth, new Color(r.X, r.Y, r.Width, r.Height));
        _infoDirty = true;
    }

    /// <summary>The <c>sector_lids</c> texel of <paramref name="sector"/> as uploaded (T6.13b): lid height (map units; <see cref="DoorLids.None"/> without a lid), ceiling flat slot, whether it is a door (T6.13d), and whether its ceiling moves (<see cref="DoorLids.MovesCeiling"/>: the upper walls over it are kept).</summary>
    public (float Height, int Slot, bool Door, bool MovesCeiling) LidData(int sector)
    {
        Color c = _lidImage.GetPixel(sector % DataWidth, sector / DataWidth);
        return (c.R, (int)MathF.Round(c.G), c.B > 0.5f, c.A > 0.5f);
    }

    /// <summary>The sector data texel of <paramref name="sector"/> as uploaded: floor, ceiling (map units), light, floor flat slot.</summary>
    public Color SectorData(int sector) => _sectorImage.GetPixel(sector % DataWidth, sector / DataWidth);

    /// <summary>The <c>side_textures</c> texel of a sidedef's texture (<see cref="PartTop"/>…) as uploaded: its slot, -1 for none.</summary>
    public int SideTextureSlot(int side, int part) => (int)MathF.Round(_sideImage.GetPixel((3 * side + part) % DataWidth, (3 * side + part) / DataWidth).R);

    /// <summary>
    /// The <c>side_textures</c> scroll of a sidedef's texture as uploaded (T5.7,
    /// G of each of its three texels): how far its <c>textureoffset</c> moved
    /// since the build, in map units.
    /// </summary>
    public float SideScroll(int side, int part = PartTop) => _sideImage.GetPixel((3 * side + part) % DataWidth, (3 * side + part) / DataWidth).G;

    /// <summary>The <c>side_textures</c> texel a section's quads read (<c>CUSTOM0.y</c>): its sidedef and part.</summary>
    public static int SidePartId(WallSection s) => 3 * s.SideDef.Index + Part(s.Kind);

    /// <summary>The sidedef texture a section of <paramref name="kind"/> draws (<see cref="PartTop"/>…).</summary>
    public static int Part(WallSectionKind kind) => kind switch
    {
        WallSectionKind.Upper => PartTop,
        WallSectionKind.Lower => PartBottom,
        _ => PartMiddle,
    };

    /// <summary>A sidedef's texture name of <paramref name="part"/>.</summary>
    public static string PartTexture(IsoDoom.Map.Side side, int part) => part switch
    {
        PartTop => side.TopTexture,
        PartBottom => side.BottomTexture,
        _ => side.MidTexture,
    };

    /// <summary>The <c>texture_info</c> texel of texture slot <paramref name="slot"/> as uploaded: atlas x, y, width, height.</summary>
    public Color TextureInfo(int slot) => _infoImage.GetPixel(slot % DataWidth, slot / DataWidth);

    /// <summary>Sets a shader parameter on every material (the sprite shader ignores the ones it lacks).</summary>
    private void SetParameter(string name, Variant value)
    {
        // T8.3: set every frame for some (the light origin, the cutaway): no StringName or array made per call.
        // T8.3d: the value disposed after the calls (a texture's Variant, GodotRefs).
        StringName stringName = StringNames.Get(name);
        using (value)
        {
            Material.SetShaderParameter(stringName, value);
            MaskedMaterial.SetShaderParameter(stringName, value);
            SpriteMaterial.SetShaderParameter(stringName, value);
            FuzzMaterial.SetShaderParameter(stringName, value);
        }
    }

    // One sector's texel; returns whether it changed.
    private bool WriteSector(Sector s, SectorHeights? heights)
    {
        (float floor, float ceiling) = heights?.Invoke(s) ?? ((float)(s.FloorHeight / 65536.0), (float)(s.CeilingHeight / 65536.0));
        int i = s.Index;
        float flat = _sectorTexels[i].A;
        if (!ReferenceEquals(_sectorFlats[i], s.FloorPic))
        {
            int slot = FlatSlot(s.FloorPic);
            if (slot >= 0)
                flat = slot;
            else
                Miss($"flat {s.FloorPic} (sector {i})");
            _sectorFlats[i] = s.FloorPic;
        }
        var texel = new Color(floor, ceiling, s.LightLevel, flat);
        if (texel == _sectorTexels[i])
            return false;
        _sectorTexels[i] = texel;
        _sectorImage.SetPixel(i % DataWidth, i / DataWidth, texel);
        return true;
    }

    // Every lid sector's lid height from its neighbours' ceilings as just written, and its
    // ceiling flat's slot (T6.13b); returns whether one changed.
    private bool WriteLids()
    {
        bool dirty = false;
        IReadOnlyList<int> lids = Lids.Sectors;
        for (int l = 0; l < lids.Count; l++) // (T8.3: no enumerator a frame)
        {
            int i = lids[l];
            Sector s = Level.Sectors[i];
            float slot = _lidTexels[i].G;
            if (!ReferenceEquals(_lidFlats[i], s.CeilingPic))
            {
                int found = FlatSlot(s.CeilingPic);
                if (found >= 0)
                    slot = found;
                else
                    Miss($"ceiling flat {s.CeilingPic} (sector {i}'s lid)");
                _lidFlats[i] = s.CeilingPic;
            }
            var texel = new Color(Lids.LidHeight(i, _drawnCeiling ??= n => _sectorTexels[n].G), slot, Lids.IsDoor(i) ? 1 : 0, _lidTexels[i].A);
            if (texel == _lidTexels[i])
                continue;
            _lidTexels[i] = texel;
            _lidImage.SetPixel(i % DataWidth, i / DataWidth, texel);
            dirty = true;
        }
        return dirty;
    }

    // Every island's top from its walls' ceilings as just written (T6.13e); returns whether one changed.
    private bool WriteIslands()
    {
        bool dirty = false;
        for (int i = 0; i < _islandTops.Length; i++)
        {
            float top = Caps.IslandTop(i, _drawnCeiling ??= n => _sectorTexels[n].G); // (T8.3: one delegate, not one a frame)
            if (top == _islandTops[i])
                continue;
            _islandTops[i] = top;
            _islandImage.SetPixel(i % DataWidth, i / DataWidth, new Color(top, 0, 0));
            dirty = true;
        }
        return dirty;
    }

    /// <summary>The <c>wall_islands</c> texel of <paramref name="island"/> as uploaded (T6.13e): its top, map units.</summary>
    public float IslandData(int island) => _islandImage.GetPixel(island % DataWidth, island / DataWidth).R;

    // Every side part's texture slot and every side's scroll (T5.7); returns whether one changed.
    private bool WriteSides()
    {
        bool dirty = false;
        foreach (IsoDoom.Map.Side side in Level.Sides)
        {
            int scroll = unchecked(side.TextureOffset - _sideBaseOffset[side.Index]);
            bool scrolled = scroll != _sideScroll[side.Index];
            _sideScroll[side.Index] = scroll;
            for (int part = 0; part < 3; part++)
            {
                int id = 3 * side.Index + part;
                string name = PartTexture(side, part);
                bool first = _sideNames[id] is null;
                if (!ReferenceEquals(_sideNames[id], name))
                {
                    _sideNames[id] = name;
                    int texnum = Textures.R_CheckTextureNumForName(name);
                    int slot = texnum == 0 ? -1 : TextureSlot(texnum);
                    if (slot < 0 && texnum != 0)
                    {
                        // Not in the atlas: at load a part with nothing drawn; later a miss on a drawn one.
                        if (first || !_sidePartDrawn[id])
                            slot = -1;
                        else
                        {
                            Miss($"texture {name} (side {side.Index})");
                            slot = _sideSlots[id];
                        }
                    }
                    if (first || slot != _sideSlots[id])
                    {
                        _sideSlots[id] = slot;
                        scrolled = true;
                    }
                }
                if (!scrolled && !first)
                    continue;
                _sideImage.SetPixel(id % DataWidth, id / DataWidth, new Color(_sideSlots[id], (float)(scroll / 65536.0), 0));
                dirty = true;
            }
        }
        return dirty;
    }

    private void Miss(string what)
    {
        RuntimeMisses++;
        if (_warned.Add(what))
            GD.PushWarning($"Level: {Level.Name}: {what} is not in the level's atlas; kept the previous one (SPEC §12 T5.1)");
    }

    private static int Rows(int count) => Math.Max(1, (count + DataWidth - 1) / DataWidth);

    private void CreateTextures(Playpal playpal, Colormap colormap)
    {
        _playpal = playpal;
        _colormap = colormap;
        AtlasTexture = IndexedTextures.CreateTexture(Atlas.Image);

        Image info = _infoImage = Image.CreateEmpty(DataWidth, Rows(Atlas.Rects.Count), false, Image.Format.Rgbaf);
        _slotShows = new int[Atlas.Rects.Count];
        for (int i = 0; i < Atlas.Rects.Count; i++)
        {
            _slotShows[i] = i;
            AtlasRect r = Atlas.Rects[i];
            info.SetPixel(i % DataWidth, i / DataWidth, new Color(r.X, r.Y, r.Width, r.Height));
        }
        TextureInfoTexture = ImageTexture.CreateFromImage(info);

        Array.Fill(_sectorTexels, new Color(float.NaN, 0, 0, 0)); // nothing written yet
        foreach (Sector s in Level.Sectors)
            WriteSector(s, null);
        SectorDataTexture = ImageTexture.CreateFromImage(_sectorImage);
        for (int i = 0; i < Level.Sectors.Length; i++)
        {
            _lidTexels[i] = new Color(DoorLids.None, -1, Lids.IsDoor(i) ? 1 : 0, Lids.MovesCeiling(i) ? 1 : 0);
            _lidImage.SetPixel(i % DataWidth, i / DataWidth, _lidTexels[i]);
        }
        WriteLids();
        LidDataTexture = ImageTexture.CreateFromImage(_lidImage);
        Array.Fill(_islandTops, float.NaN); // nothing written yet
        WriteIslands();
        IslandDataTexture = ImageTexture.CreateFromImage(_islandImage);
        WriteSides();
        SideTexturesTexture = ImageTexture.CreateFromImage(_sideImage);
        _fogBytes = new byte[DataWidth * Rows(Level.Sectors.Length)];
        Array.Fill(_fogBytes, (byte)SectorSight.Visible);
        _fogImage = Image.CreateFromData(DataWidth, Rows(Level.Sectors.Length), false, Image.Format.R8, _fogBytes);
        FogDataTexture = ImageTexture.CreateFromImage(_fogImage);
        using (Image dim = Image.CreateFromData(256, 1, false, Image.Format.R8, new byte[256]))
            _fogDimTexture = ImageTexture.CreateFromImage(dim);

        Material = new ShaderMaterial { Shader = GodotRefs.LoadShader(ShaderPath) };
        MaskedMaterial = new ShaderMaterial { Shader = GodotRefs.LoadShader(MaskedShaderPath) };
        FuzzMaterial = new ShaderMaterial { Shader = GodotRefs.LoadShader(ThingSprites.FuzzShaderPath) };
        SpriteMaterial = new ShaderMaterial { Shader = GodotRefs.LoadShader(ThingSprites.ShaderPath), NextPass = FuzzMaterial };
        ShadowMaterial = new ShaderMaterial { Shader = GodotRefs.LoadShader(ThingSprites.ShadowShaderPath) };
        SetParameter("atlas", AtlasTexture);
        SetParameter("texture_info", TextureInfoTexture);
        SetParameter("sector_data", SectorDataTexture);
        SetParameter("sector_lids", LidDataTexture);
        SetParameter("wall_islands", IslandDataTexture);
        SetParameter("side_textures", SideTexturesTexture);
        SetParameter("sector_fog", FogDataTexture);
        SetParameter("fog_dim", _fogDimTexture);
        // PLAYPAL and, after its palettes, a row for the blended flash palette (T6.13v, BlendRow).
        _playpalRows = new byte[(playpal.Count + 1) * Playpal.PaletteSize];
        playpal.Data.CopyTo(_playpalRows);
        playpal.GetPalette(0).CopyTo(_playpalRows.AsSpan(playpal.Count * Playpal.PaletteSize));
        _playpalImage = Image.CreateFromData(256, playpal.Count + 1, false, Image.Format.Rgb8, _playpalRows);
        _playpalTexture = ImageTexture.CreateFromImage(_playpalImage);
        _blendRow = -1;
        SetParameter("playpal", _playpalTexture);
        _colormapTexture = IndexedTextures.CreateColormapTexture(colormap);
        SetParameter("colormap", _colormapTexture);
        using (Image lights = Image.CreateFromData(LightTables.TableWidth, LightTables.TableHeight, false, Image.Format.R8, Lights.ToBytes()))
            LightTablesTexture = ImageTexture.CreateFromImage(lights);
        SetParameter("light_tables", LightTablesTexture);
        SetParameter("light_centerx", Lights.CenterX);
        SetPalette(0);
        SetColormapOverride(-1);
        SetLightDiminishing(LightDiminishing.Player);
        SetLightOrigin(Vector2.Zero);
        SetLightReference(LightTables.DefaultReferenceDistance);
        SetLightNear(LightTables.DefaultNearDistance);
        SetExtraLight(0);
        SetWallTiling(WallTextureTiling.Vanilla);
        SetMaskedBackFaces(MaskedBacks);
        SetCutaway(Cutaway);
        SetDoorLids(LidMode);
        SetWallCaps(CapMode);
        SetSwitchMarkers(MarkerMode);
        SetHeightCues(HeightCues);
        SetHeightReference(null);
        SetUpperWalls(UpperMode);
        SetFog(FogMode);
        SetFogDim(FogDimLook);
        SetSprites(Sprites);
        SetCutawayCentres(null, null);
        SetFuzzPhase(0);
    }

    /// <summary>
    /// Drops the C# side's references to the Godot resources the level made
    /// (its chunk meshes, materials, textures and images) at once, rather than
    /// when the GC gets to their wrappers: it cannot see their native and GPU
    /// memory, and wrappers left at exit crash Godot's teardown. Nodes still
    /// drawing with one keep it alive. Safe to call twice.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (ArrayMesh? mesh in SectorMeshes)
            mesh?.Dispose();
        SectorMeshes = [];
        GodotObject?[] owned =
        [
            Material, MaskedMaterial, SpriteMaterial, FuzzMaterial, ShadowMaterial,
            AtlasTexture, TextureInfoTexture, SectorDataTexture, LidDataTexture, IslandDataTexture, SideTexturesTexture,
            FogDataTexture, _fogDimTexture, _playpalTexture, _colormapTexture, LightTablesTexture,
            _sectorImage, _lidImage, _islandImage, _infoImage, _sideImage, _fogImage, _playpalImage,
        ];
        foreach (GodotObject? o in owned)
            o?.Dispose();
    }

    private bool _disposed;

    private sealed class Chunk
    {
        public readonly List<Vector3> Vertices = [];
        public readonly List<Vector2> Uv = [];
        public readonly List<float> Custom0 = [], Custom1 = [], Custom2 = [];
        public readonly List<int> Indices = [];

        public void Add(Vector3 v, Vector2 uv, Vector4 c0, Vector4 c1, Vector4 c2)
        {
            Vertices.Add(v);
            Uv.Add(uv);
            Push(Custom0, c0);
            Push(Custom1, c1);
            Push(Custom2, c2);
        }

        private static void Push(List<float> list, Vector4 v)
        {
            list.Add(v.X);
            list.Add(v.Y);
            list.Add(v.Z);
            list.Add(v.W);
        }
    }

    private void BuildChunks()
    {
        var chunks = new Chunk?[Level.Sectors.Length];
        var maskedChunks = new Chunk?[Level.Sectors.Length];
        Chunk ChunkOf(int sector) => chunks[sector] ??= new Chunk();

        foreach (SectorFloor floor in Floors.BySector)
        {
            if (floor.TriangleCount == 0)
                continue;
            Sector sector = Level.Sectors[floor.Sector];
            Chunk c = ChunkOf(floor.Sector);
            int first = c.Vertices.Count;
            var c0 = new Vector4(KindFloor, 0, floor.Sector, -1); // the flat's slot is the sector data's A (T5.1)
            foreach (PolygonVertex v in floor.Vertices)
                c.Add(ToGodot(v.X, v.Y, 0), new Vector2((float)(v.X / 65536.0), (float)(-v.Y / 65536.0)), c0, Vector4.Zero, Vector4.Zero);
            foreach (int i in floor.Indices)
                c.Indices.Add(first + i);
            FloorTriangleCount += floor.TriangleCount;
        }

        foreach (WallSection s in Walls.Sections)
        {
            if (!IsDrawn(s))
                continue;
            bool masked = IsMasked(s);
            Chunk c = masked ? maskedChunks[s.FrontSector.Index] ??= new Chunk() : ChunkOf(s.FrontSector.Index);
            var c0 = new Vector4(masked ? KindMasked : KindWall, SidePartId(s), s.FrontSector.Index, s.BackSector?.Index ?? -1);
            var c1 = new Vector4((int)s.Bottom.Plane, Units(s.Bottom.Offset), (int)s.Top.Plane, Units(s.Top.Offset));
            foreach (WallPiece piece in Pieces.Of(s.Line, s.Side))
            {
                var c2 = new Vector4(TexturePlane(s, s.TextureTop.Plane), TextureTopOffset(s), piece.Contrast, PieceAngle(piece));
                (float u1, float u2) = (Column(s, piece.ColumnA), Column(s, piece.ColumnB));
                Vector3 p1 = ToGodot(piece.A.X, piece.A.Y, 0), p2 = ToGodot(piece.B.X, piece.B.Y, 0);
                int first = c.Vertices.Count;
                c.Add(p1, new Vector2(u1, 0), c0, c1, c2); // V1 bottom
                c.Add(p1, new Vector2(u1, 1), c0, c1, c2); // V1 top
                c.Add(p2, new Vector2(u2, 1), c0, c1, c2); // V2 top
                c.Add(p2, new Vector2(u2, 0), c0, c1, c2); // V2 bottom
                // Seen from the front sector (on the side's right), V1 is on the left:
                // V1b, V1t, V2t is clockwise on screen, Godot's front face.
                c.Indices.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
                if (masked)
                    MaskedQuads++;
                else
                    WallQuads++;
            }

            // T3.1a: the back face, in the back sector's chunk with the sectors swapped (so it takes
            // the back sector's light, the viewer's side as vanilla's seg), the same corners and
            // columns (so the texture shows mirrored, as the quad's other face), the other winding.
            if (!HasBackFace(s))
                continue;
            Sector back = s.BackSector!;
            Chunk bc = maskedChunks[back.Index] ??= new Chunk();
            var b0 = new Vector4(KindMaskedBack, SidePartId(s), back.Index, s.FrontSector.Index);
            var b1 = new Vector4((int)OtherSide(s.Bottom.Plane), Units(s.Bottom.Offset), (int)OtherSide(s.Top.Plane), Units(s.Top.Offset));
            foreach (WallPiece piece in Pieces.Of(s.Line, s.Side))
            {
                var c2 = new Vector4(TexturePlane(s, OtherSide(s.TextureTop.Plane)), TextureTopOffset(s), piece.Contrast, PieceAngle(piece));
                (float u1, float u2) = (Column(s, piece.ColumnA), Column(s, piece.ColumnB));
                Vector3 p1 = ToGodot(piece.A.X, piece.A.Y, 0), p2 = ToGodot(piece.B.X, piece.B.Y, 0);
                int first = bc.Vertices.Count;
                bc.Add(p1, new Vector2(u1, 0), b0, b1, c2);
                bc.Add(p1, new Vector2(u1, 1), b0, b1, c2);
                bc.Add(p2, new Vector2(u2, 1), b0, b1, c2);
                bc.Add(p2, new Vector2(u2, 0), b0, b1, c2);
                bc.Indices.AddRange(BackFaceIndices(first));
                MaskedBackQuads++;
            }
        }

        // T3.4a: each floor again per cut centre, after the walls, as its cap (placed and
        // collapsed by the vertex shader).
        foreach (SectorFloor floor in Floors.BySector)
        {
            if (floor.TriangleCount == 0)
                continue;
            Chunk c = chunks[floor.Sector]!;
            foreach (int centre in new[] { CapPlayer, CapCursor })
            {
                int first = c.Vertices.Count;
                var c0 = new Vector4(KindCap, 0, floor.Sector, centre);
                foreach (PolygonVertex v in floor.Vertices)
                    c.Add(ToGodot(v.X, v.Y, 0), new Vector2((float)(v.X / 65536.0), (float)(-v.Y / 65536.0)), c0, Vector4.Zero, Vector4.Zero);
                foreach (int i in floor.Indices)
                    c.Indices.Add(first + i);
            }

            // T6.13b: a lid sector's floor once more, after its caps, as its lid.
            if (Lids.Has(floor.Sector))
            {
                int first = c.Vertices.Count;
                var c0 = new Vector4(KindLid, 0, floor.Sector, -1);
                foreach (PolygonVertex v in floor.Vertices)
                    c.Add(ToGodot(v.X, v.Y, 0), new Vector2((float)(v.X / 65536.0), (float)(-v.Y / 65536.0)), c0, Vector4.Zero, Vector4.Zero);
                foreach (int i in floor.Indices)
                    c.Indices.Add(first + i);
                LidTriangleCount += floor.TriangleCount;
            }
        }

        // T6.13e: each wall's cap, then once more per cut centre as its cutaway cap.
        foreach (WallCap cap in Caps.Caps)
        {
            Chunk c = ChunkOf(cap.Sector);
            int part = 3 * cap.Side + PartMiddle;
            float column = (float)(Level.Sides[cap.Side].TextureOffset / 65536.0);
            var c2 = new Vector4(0, 0, 0, (float)Math.Atan2(cap.Dy, cap.Dx));
            foreach (int centre in new[] { -1, CapPlayer, CapCursor })
            {
                var c0 = new Vector4(centre < 0 ? KindWallCap : KindWallCapCut, part, cap.Sector, centre);
                var c1 = new Vector4(cap.Island, 0, 0, 0);
                foreach ((double X, double Y)[] piece in cap.Pieces)
                {
                    int first = c.Vertices.Count;
                    foreach ((double x, double y) in piece)
                    {
                        var uv = new Vector2(column + (float)cap.Along(x, y), (float)cap.Behind(x, y));
                        c.Add(new Vector3((float)(x / MapUnitsPerMetre), 0, (float)(-y / MapUnitsPerMetre)), uv, c0, c1, c2);
                    }
                    // A fan; clockwise in map space is Godot's front face from above, as the floors.
                    for (int i = 1; i + 1 < piece.Length; i++)
                        c.Indices.AddRange([first, first + i, first + i + 1]);
                    if (centre < 0)
                        WallCapTriangleCount += piece.Length - 2;
                }
            }
        }

        // T6.13p: each switch side's marker, on its sector's floor.
        foreach (SwitchMarker marker in Markers.Markers)
        {
            WallSection s = marker.Section;
            Chunk c = ChunkOf(marker.Sector);
            var c0 = new Vector4(KindSwitchMarker, SidePartId(s), marker.Sector, s.BackSector?.Index ?? -1);
            var c1 = new Vector4((int)s.Bottom.Plane, Units(s.Bottom.Offset), (int)s.Top.Plane, Units(s.Top.Offset));
            var c2 = new Vector4(0, 0, 0, (float)Math.Atan2(marker.Dy, marker.Dx));
            float column = Units(s.TextureOffset);
            foreach ((double X, double Y)[] piece in marker.Pieces)
            {
                int first = c.Vertices.Count;
                foreach ((double x, double y) in piece)
                {
                    var uv = new Vector2(column + (float)marker.Column(x, y), (float)marker.Row(x, y));
                    c.Add(new Vector3((float)(x / MapUnitsPerMetre), 0, (float)(-y / MapUnitsPerMetre)), uv, c0, c1, c2);
                }
                // A fan, clockwise in map space as the floors.
                for (int i = 1; i + 1 < piece.Length; i++)
                    c.Indices.AddRange([first, first + i, first + i + 1]);
                SwitchMarkerTriangleCount += piece.Length - 2;
            }
        }

        // T6.13s: each side's ledge edge, on its sector's floor.
        foreach (LedgeEdge edge in Ledges.Edges)
        {
            Chunk c = ChunkOf(edge.Sector);
            var c0 = new Vector4(KindLedge, 0, edge.Sector, edge.Other);
            foreach ((double X, double Y)[] piece in edge.Pieces)
            {
                int first = c.Vertices.Count;
                foreach ((double x, double y) in piece)
                {
                    var uv = new Vector2((float)edge.Along(x, y), (float)edge.Inside(x, y));
                    c.Add(new Vector3((float)(x / MapUnitsPerMetre), 0, (float)(-y / MapUnitsPerMetre)), uv, c0, Vector4.Zero, Vector4.Zero);
                }
                // A fan, clockwise in map space as the floors.
                for (int i = 1; i + 1 < piece.Length; i++)
                    c.Indices.AddRange([first, first + i, first + i + 1]);
                LedgeTriangleCount += piece.Length - 2;
            }
        }

        // T6.13e: an island's walls once more, from their ceiling up to the island's top.
        foreach (WallSection s in Walls.Sections)
        {
            if (!IsDrawn(s) || s.BackSector is not null || s.Kind != WallSectionKind.Middle || Caps.IslandOf(s.Line.Index) is not (>= 0 and int island))
                continue;
            Chunk c = ChunkOf(s.FrontSector.Index);
            var c0 = new Vector4(KindWallExtension, SidePartId(s), s.FrontSector.Index, island);
            var c1 = new Vector4((int)WallPlane.FrontCeiling, 0, (int)WallPlane.FrontCeiling, 0);
            foreach (WallPiece piece in Pieces.Of(s.Line, s.Side))
            {
                var c2 = new Vector4(TexturePlane(s, s.TextureTop.Plane), TextureTopOffset(s), piece.Contrast, PieceAngle(piece));
                (float u1, float u2) = (Column(s, piece.ColumnA), Column(s, piece.ColumnB));
                Vector3 p1 = ToGodot(piece.A.X, piece.A.Y, 0), p2 = ToGodot(piece.B.X, piece.B.Y, 0);
                int first = c.Vertices.Count;
                c.Add(p1, new Vector2(u1, 0), c0, c1, c2);
                c.Add(p1, new Vector2(u1, 1), c0, c1, c2);
                c.Add(p2, new Vector2(u2, 1), c0, c1, c2);
                c.Add(p2, new Vector2(u2, 0), c0, c1, c2);
                c.Indices.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
                WallExtensionQuads++;
            }
        }

        // T6.13f: a lid sector's one-sided walls once more, from its ceiling up to its lid, facing the void.
        foreach (WallSection s in Walls.Sections)
        {
            if (!Lids.HasEnd(s))
                continue;
            Chunk c = ChunkOf(s.FrontSector.Index);
            var c0 = new Vector4(KindLidEnd, SidePartId(s), s.FrontSector.Index, -1);
            var c1 = new Vector4((int)WallPlane.FrontCeiling, 0, (int)WallPlane.FrontCeiling, 0);
            foreach (WallPiece piece in Pieces.Of(s.Line, s.Side))
            {
                // The wall's columns, so its texture joins its cap's (seen from behind: mirrored, as a back face).
                var c2 = new Vector4(TexturePlane(s, s.TextureTop.Plane), TextureTopOffset(s), piece.Contrast, PieceAngle(piece));
                (float u1, float u2) = (Column(s, piece.ColumnA), Column(s, piece.ColumnB));
                Vector3 p1 = ToGodot(piece.A.X, piece.A.Y, 0), p2 = ToGodot(piece.B.X, piece.B.Y, 0);
                int first = c.Vertices.Count;
                c.Add(p1, new Vector2(u1, 0), c0, c1, c2);
                c.Add(p1, new Vector2(u1, 1), c0, c1, c2);
                c.Add(p2, new Vector2(u2, 1), c0, c1, c2);
                c.Add(p2, new Vector2(u2, 0), c0, c1, c2);
                c.Indices.AddRange(BackFaceIndices(first));
                LidEndQuads++;
            }
        }

        const Mesh.ArrayFormat Custom =
            (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift)
            | (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift)
            | (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom2Shift);
        SectorMeshes = new ArrayMesh?[chunks.Length];
        Aabb? bounds = null;
        for (int s = 0; s < chunks.Length; s++)
        {
            if (chunks[s] is null && maskedChunks[s] is null)
                continue;
            var mesh = new ArrayMesh();
            Vector3? min = null, max = null;
            foreach ((Chunk? c, ShaderMaterial material) in new[] { (chunks[s], Material), (maskedChunks[s], MaskedMaterial) })
            {
                if (c is null)
                    continue;
                // T8.3b: disposed, never finalized. Godot's binding passes the native array by a
                // borrowed pointer, so a GC during the call could otherwise finalize (free) it.
                // T8.3d: so is each slot's Variant, after its store (GodotRefs.SetSlot).
                using var arrays = new Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                arrays.SetSlot(Mesh.ArrayType.Vertex, c.Vertices.ToArray());
                arrays.SetSlot(Mesh.ArrayType.TexUV, c.Uv.ToArray());
                arrays.SetSlot(Mesh.ArrayType.Custom0, c.Custom0.ToArray());
                arrays.SetSlot(Mesh.ArrayType.Custom1, c.Custom1.ToArray());
                arrays.SetSlot(Mesh.ArrayType.Custom2, c.Custom2.ToArray());
                arrays.SetSlot(Mesh.ArrayType.Index, c.Indices.ToArray());
                mesh.AddSurface(arrays, Custom);
                mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
                foreach (Vector3 v in c.Vertices)
                {
                    min = min?.Min(v) ?? v;
                    max = max?.Max(v) ?? v;
                }
            }

            // The shader moves vertices vertically, so cull with the full height range.
            (Vector3 lo, Vector3 hi) = (min!.Value, max!.Value);
            mesh.CustomAabb = new Aabb(new Vector3(lo.X, -HeightRange, lo.Z), new Vector3(hi.X - lo.X, 2 * HeightRange, hi.Z - lo.Z));
            SectorMeshes[s] = mesh;

            Sector sector = Level.Sectors[s];
            var box = new Aabb(new Vector3(lo.X, sector.FloorHeight / 65536f / MapUnitsPerMetre, lo.Z),
                new Vector3(hi.X - lo.X, Math.Max(0, sector.CeilingHeight - sector.FloorHeight) / 65536f / MapUnitsPerMetre, hi.Z - lo.Z));
            bounds = bounds is Aabb b ? b.Merge(box) : box;
        }
        Bounds = bounds ?? new Aabb();
    }

    /// <summary>A back-face quad's indices (T3.1a): the front quad's triangles (A bottom, A top, B top, B bottom from <paramref name="first"/>) wound the other way.</summary>
    public static int[] BackFaceIndices(int first) => [first, first + 2, first + 1, first, first + 3, first + 2];

    /// <summary>
    /// The direction of a wall piece from its end A to B, radians (map space,
    /// counterclockwise from +x): the shader's <c>CUSTOM2.w</c>, from which the
    /// cutaway (T3.4) takes the wall's normal, (sin, −cos), towards the front sector.
    /// </summary>
    public static float PieceAngle(WallPiece piece) => (float)Math.Atan2((double)piece.B.Y - piece.A.Y, (double)piece.B.X - piece.A.X);

    private static float Units(int fixedValue) => (float)(fixedValue / 65536.0);

    /// <summary>A section's texture-top plane reference as <c>CUSTOM2.x</c>: the plane, plus <see cref="PlaneAddsTextureHeight"/> when its texture bottom is pegged (T5.1).</summary>
    public static int TexturePlane(WallSection s, WallPlane plane) => (int)plane + (s.BottomPegged ? PlaneAddsTextureHeight : 0);

    /// <summary>A section's texture-top offset as <c>CUSTOM2.y</c> (map units): without the texture height when the shader adds the slot's (T5.1).</summary>
    public static float TextureTopOffset(WallSection s) => Units(s.TextureTop.Offset - (s.BottomPegged ? s.TextureHeight : 0));

    /// <summary>The texture column (map units, float) of a section's piece end: the sidedef's <c>textureoffset</c> plus the piece's column (<see cref="WallPiece"/>).</summary>
    public static float Column(WallSection s, int pieceColumn) => (float)(((long)s.TextureOffset + pieceColumn) / 65536.0);
}

/// <summary>
/// Whether a masked middle on one side of a line only (vanilla draws it from
/// that side alone; SPEC §12 T3.1a) is drawn from behind as well.
/// </summary>
public enum MaskedBackFaces
{
    /// <summary>Vanilla: invisible from behind (with the fixed camera, for good when its side faces away).</summary>
    Off,

    /// <summary>The default: from behind too, as the quad's other face (the texture mirrored), lit by the sector on that side.</summary>
    Mirrored,
}
