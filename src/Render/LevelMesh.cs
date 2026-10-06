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
/// one RG8 index atlas (<see cref="TextureAtlas"/>); a texture slot's atlas
/// rectangle sits in the <c>texture_info</c> data texture, so animated textures
/// can later re-point a slot without touching the meshes.</item>
/// <item><b>Per-sector data:</b> an RGBA float texture (<see cref="DataWidth"/>
/// texels per row): floor height, ceiling height (map units), light level.
/// Change a <see cref="Sector"/> of <see cref="Level"/> and call
/// <see cref="UpdateSectors"/> to move its floor and walls.</item>
/// <item><b>Light</b> (T2.8): vanilla's <c>zlight</c>/<c>scalelight</c>
/// (<see cref="LightTables"/>) in a small R8 texture, the fake contrast per
/// wall quad (one quad per piece of a side, <see cref="WallPieces"/>: per seg, on the floor's corners),
/// and the distance by <see cref="LightDiminishing"/> (uniforms set with
/// <see cref="SetLightDiminishing"/>, <see cref="SetLightOrigin"/>,
/// <see cref="SetLightReference"/>, <see cref="SetExtraLight"/>,
/// <see cref="SetColormapOverride"/>).</item>
/// </list>
/// Scale: 1 map unit = 1/32 m (SPEC §7.1); map x → +X, map y → −Z, height → +Y.
/// </summary>
public sealed class LevelMesh
{
    public const string ShaderPath = "res://shaders/level.gdshader";

    /// <summary>The masked middles' variant of the level shader (T3.1).</summary>
    public const string MaskedShaderPath = "res://shaders/level_masked.gdshader";

    /// <summary>Map units per Godot metre (SPEC §7.1).</summary>
    public const float MapUnitsPerMetre = 32f;

    /// <summary>Texels per row of the sector and texture-info data textures (the shader's <c>SECTOR_DATA_WIDTH</c>).</summary>
    public const int DataWidth = 256;

    /// <summary>Vertex kinds (<c>CUSTOM0.x</c>).</summary>
    public const int KindFloor = 0, KindWall = 1, KindMasked = 2;

    /// <summary>Half the height range of a chunk's culling box, in metres (any fixed_t height fits).</summary>
    private const float HeightRange = 32768f / MapUnitsPerMetre;

    private readonly Image _sectorImage;
    private Image _infoImage = null!;
    private readonly int[] _textureSlot; // texture number → slot, -1 if unused
    private readonly Dictionary<string, int> _flatSlot = new(StringComparer.OrdinalIgnoreCase);

    private LevelMesh(Level level, WallSections walls, FloorTriangles floors, Textures textures, int[] textureSlot, List<string> slotNames,
        TextureAtlas atlas, Image sectorImage)
    {
        Level = level;
        Walls = walls;
        Floors = floors;
        Pieces = WallPieces.Build(level, floors);
        Textures = textures;
        _textureSlot = textureSlot;
        SlotNames = slotNames;
        Atlas = atlas;
        _sectorImage = sectorImage;
    }

    public Level Level { get; }
    public WallSections Walls { get; }
    public FloorTriangles Floors { get; }
    public Textures Textures { get; }

    /// <summary>The quads of every side (per seg, on the floor's corners, with each seg's fake contrast; T2.9): one wall quad per piece of each drawn section.</summary>
    public WallPieces Pieces { get; }

    /// <summary>The light tables the shader reads (r_main.c, full-screen view; SPEC §12 T2.8).</summary>
    public LightTables Lights { get; } = LightTables.R_InitLightTables();

    public ImageTexture LightTablesTexture { get; private set; } = null!;

    /// <summary>The light settings as last set (<see cref="SetLightDiminishing"/> and friends).</summary>
    public LightDiminishing LightMode { get; private set; } = LightDiminishing.Player;
    public Vector2 LightOrigin { get; private set; }
    public float LightReference { get; private set; } = LightTables.DefaultReferenceDistance;
    public int ExtraLight { get; private set; }

    /// <summary>The texture atlas; slot <c>i</c>'s rectangle is <c>Atlas.Rects[i]</c>.</summary>
    public TextureAtlas Atlas { get; }

    /// <summary>The name of each texture slot: wall textures first, then flats.</summary>
    public IReadOnlyList<string> SlotNames { get; }

    /// <summary>The chunk of each sector (null when the sector has no floor and no wall).</summary>
    public ArrayMesh?[] SectorMeshes { get; private set; } = Array.Empty<ArrayMesh?>();

    /// <summary>The material of every chunk's floor and solid walls (its first surface).</summary>
    public ShaderMaterial Material { get; private set; } = null!;

    /// <summary>The material of the masked middles (a chunk's last surface, when it has any; T3.1). Its parameters follow <see cref="Material"/>'s.</summary>
    public ShaderMaterial MaskedMaterial { get; private set; } = null!;

    /// <summary>Both materials, for setting a shader parameter on each.</summary>
    public IEnumerable<ShaderMaterial> Materials => new[] { Material, MaskedMaterial };

    public ImageTexture AtlasTexture { get; private set; } = null!;
    public ImageTexture TextureInfoTexture { get; private set; } = null!;
    public ImageTexture SectorDataTexture { get; private set; } = null!;

    /// <summary>Number of solid wall quads in the meshes (one per <see cref="WallPiece"/> of each drawn section), of masked middle quads, and of floor triangles.</summary>
    public int WallQuads { get; private set; }
    public int MaskedQuads { get; private set; }
    public int FloorTriangleCount { get; private set; }

    /// <summary>The level's bounds in Godot space at its load-time heights.</summary>
    public Aabb Bounds { get; private set; }

    /// <summary>The slot of wall texture <paramref name="texnum"/>, or -1 when the level does not use it.</summary>
    public int TextureSlot(int texnum) => texnum > 0 && texnum < _textureSlot.Length ? _textureSlot[texnum] : -1;

    /// <summary>The slot of flat <paramref name="name"/>, or -1 when the level does not use it.</summary>
    public int FlatSlot(string name) => _flatSlot.TryGetValue(name, out int slot) ? slot : -1;

    /// <summary>Builds the meshes, atlas, data textures and material of <paramref name="level"/>.</summary>
    public static LevelMesh Build(WadArchive wad, Level level, Textures textures, Playpal playpal, Colormap colormap,
        TextureCompositeMode compositeMode = TextureCompositeMode.Vanilla)
    {
        WallSections walls = WallSections.Build(level, textures);
        FloorTriangles floors = FloorTriangles.Build(level, SubsectorPolygons.Build(level));

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
        var flatSlot = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Sector sector in level.Sectors)
        {
            if (flatSlot.ContainsKey(sector.FloorPic))
                continue;
            flatSlot[sector.FloorPic] = images.Count;
            images.Add(Flat.Load(wad, sector.FloorPic));
            names.Add(sector.FloorPic);
        }
        if (images.Count == 0)
            throw new WadFormatException($"{level.Name}: nothing to draw");
        TextureAtlas atlas = TextureAtlas.Build(images);

        var mesh = new LevelMesh(level, walls, floors, textures, textureSlot, names, atlas,
            Image.CreateEmpty(DataWidth, Rows(level.Sectors.Length), false, Image.Format.Rgbaf));
        foreach (var (name, slot) in flatSlot)
            mesh._flatSlot[name] = slot;
        mesh.CreateTextures(playpal, colormap);
        mesh.BuildChunks();
        return mesh;
    }

    /// <summary>Whether the meshes draw <paramref name="s"/>: every section with a texture (<see cref="IsMasked"/> ones in the masked pass).</summary>
    public static bool IsDrawn(WallSection s) => s.Texture != 0;

    /// <summary>Whether <paramref name="s"/> is drawn by <see cref="MaskedMaterial"/> (a masked middle, T3.1).</summary>
    public static bool IsMasked(WallSection s) => s.Kind == WallSectionKind.MaskedMiddle;

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

    /// <summary>r_main.c <c>extralight</c> (the player's weapon flash, 0–2), added to every light number.</summary>
    public void SetExtraLight(int extralight)
    {
        ExtraLight = extralight;
        SetParameter("extralight", extralight);
    }

    /// <summary>Selects the PLAYPAL palette.</summary>
    public void SetPalette(int palette) => SetParameter("palette_index", palette);

    /// <summary>
    /// Copies every sector's current floor and ceiling height and light level
    /// from <see cref="Level"/> into the sector data texture, so the meshes follow.
    /// </summary>
    public void UpdateSectors()
    {
        foreach (Sector s in Level.Sectors)
            WriteSector(s);
        SectorDataTexture.Update(_sectorImage);
    }

    /// <summary>The sector data texel of <paramref name="sector"/> as uploaded: floor, ceiling (map units), light.</summary>
    public Color SectorData(int sector) => _sectorImage.GetPixel(sector % DataWidth, sector / DataWidth);

    /// <summary>The <c>texture_info</c> texel of texture slot <paramref name="slot"/> as uploaded: atlas x, y, width, height.</summary>
    public Color TextureInfo(int slot) => _infoImage.GetPixel(slot % DataWidth, slot / DataWidth);

    /// <summary>Sets a shader parameter on both materials.</summary>
    private void SetParameter(string name, Variant value)
    {
        Material.SetShaderParameter(name, value);
        MaskedMaterial.SetShaderParameter(name, value);
    }

    private void WriteSector(Sector s) =>
        _sectorImage.SetPixel(s.Index % DataWidth, s.Index / DataWidth,
            new Color((float)(s.FloorHeight / 65536.0), (float)(s.CeilingHeight / 65536.0), s.LightLevel, 0));

    private static int Rows(int count) => Math.Max(1, (count + DataWidth - 1) / DataWidth);

    private void CreateTextures(Playpal playpal, Colormap colormap)
    {
        AtlasTexture = IndexedTextures.CreateTexture(Atlas.Image);

        var info = _infoImage = Image.CreateEmpty(DataWidth, Rows(Atlas.Rects.Count), false, Image.Format.Rgbaf);
        for (int i = 0; i < Atlas.Rects.Count; i++)
        {
            AtlasRect r = Atlas.Rects[i];
            info.SetPixel(i % DataWidth, i / DataWidth, new Color(r.X, r.Y, r.Width, r.Height));
        }
        TextureInfoTexture = ImageTexture.CreateFromImage(info);

        foreach (Sector s in Level.Sectors)
            WriteSector(s);
        SectorDataTexture = ImageTexture.CreateFromImage(_sectorImage);

        Material = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath) };
        MaskedMaterial = new ShaderMaterial { Shader = GD.Load<Shader>(MaskedShaderPath) };
        SetParameter("atlas", AtlasTexture);
        SetParameter("texture_info", TextureInfoTexture);
        SetParameter("sector_data", SectorDataTexture);
        SetParameter("playpal", IndexedTextures.CreatePlaypalTexture(playpal));
        SetParameter("colormap", IndexedTextures.CreateColormapTexture(colormap));
        LightTablesTexture = ImageTexture.CreateFromImage(
            Image.CreateFromData(LightTables.TableWidth, LightTables.TableHeight, false, Image.Format.R8, Lights.ToBytes()));
        SetParameter("light_tables", LightTablesTexture);
        SetParameter("light_centerx", Lights.CenterX);
        SetPalette(0);
        SetColormapOverride(-1);
        SetLightDiminishing(LightDiminishing.Player);
        SetLightOrigin(Vector2.Zero);
        SetLightReference(LightTables.DefaultReferenceDistance);
        SetExtraLight(0);
        SetWallTiling(WallTextureTiling.Vanilla);
    }

    private sealed class Chunk
    {
        public readonly List<Vector3> Vertices = new();
        public readonly List<Vector2> Uv = new();
        public readonly List<float> Custom0 = new(), Custom1 = new(), Custom2 = new();
        public readonly List<int> Indices = new();

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
            var c0 = new Vector4(KindFloor, FlatSlot(sector.FloorPic), floor.Sector, -1);
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
            var c0 = new Vector4(masked ? KindMasked : KindWall, TextureSlot(s.Texture), s.FrontSector.Index, s.BackSector?.Index ?? -1);
            var c1 = new Vector4((int)s.Bottom.Plane, Units(s.Bottom.Offset), (int)s.Top.Plane, Units(s.Top.Offset));
            foreach (WallPiece piece in Pieces.Of(s.Line, s.Side))
            {
                var c2 = new Vector4((int)s.TextureTop.Plane, Units(s.TextureTop.Offset), piece.Contrast, 0);
                (float u1, float u2) = (Column(s, piece.ColumnA), Column(s, piece.ColumnB));
                Vector3 p1 = ToGodot(piece.A.X, piece.A.Y, 0), p2 = ToGodot(piece.B.X, piece.B.Y, 0);
                int first = c.Vertices.Count;
                c.Add(p1, new Vector2(u1, 0), c0, c1, c2); // V1 bottom
                c.Add(p1, new Vector2(u1, 1), c0, c1, c2); // V1 top
                c.Add(p2, new Vector2(u2, 1), c0, c1, c2); // V2 top
                c.Add(p2, new Vector2(u2, 0), c0, c1, c2); // V2 bottom
                // Seen from the front sector (on the side's right), V1 is on the left:
                // V1b, V1t, V2t is clockwise on screen, Godot's front face.
                c.Indices.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
                if (masked)
                    MaskedQuads++;
                else
                    WallQuads++;
            }
        }

        const Mesh.ArrayFormat custom =
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
                var arrays = new Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = c.Vertices.ToArray();
                arrays[(int)Mesh.ArrayType.TexUV] = c.Uv.ToArray();
                arrays[(int)Mesh.ArrayType.Custom0] = c.Custom0.ToArray();
                arrays[(int)Mesh.ArrayType.Custom1] = c.Custom1.ToArray();
                arrays[(int)Mesh.ArrayType.Custom2] = c.Custom2.ToArray();
                arrays[(int)Mesh.ArrayType.Index] = c.Indices.ToArray();
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: custom);
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

    private static float Units(int fixedValue) => (float)(fixedValue / 65536.0);

    /// <summary>The texture column (map units, float) of a section's piece end: the sidedef's <c>textureoffset</c> plus the piece's column (<see cref="WallPiece"/>).</summary>
    public static float Column(WallSection s, int pieceColumn) => (float)(((long)s.TextureOffset + pieceColumn) / 65536.0);
}
