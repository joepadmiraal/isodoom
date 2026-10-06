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
/// <see cref="WallSections"/> (T2.3), and the one material that draws them
/// all with <c>shaders/level.gdshader</c>.
/// <list type="bullet">
/// <item><b>Chunks:</b> sector <c>s</c>'s mesh holds its floor triangles and
/// the solid wall sections of the sides facing into it (their front sector is
/// <c>s</c>). Masked middles are left for M3 and sections with texture 0
/// (<c>-</c>) are not drawn. Vertices hold map x/y and plane references, not
/// heights: the vertex shader places them from the per-sector data texture.</item>
/// <item><b>Textures:</b> every wall texture and flat the level uses goes into
/// one RG8 index atlas (<see cref="TextureAtlas"/>); a texture slot's atlas
/// rectangle sits in the <c>texture_info</c> data texture, so animated textures
/// can later re-point a slot without touching the meshes.</item>
/// <item><b>Per-sector data:</b> an RGBA float texture (<see cref="DataWidth"/>
/// texels per row): floor height, ceiling height (map units), light level.
/// Change a <see cref="Sector"/> of <see cref="Level"/> and call
/// <see cref="UpdateSectors"/> to move its floor and walls.</item>
/// </list>
/// Scale: 1 map unit = 1/32 m (SPEC §7.1); map x → +X, map y → −Z, height → +Y.
/// </summary>
public sealed class LevelMesh
{
    public const string ShaderPath = "res://shaders/level.gdshader";

    /// <summary>Map units per Godot metre (SPEC §7.1).</summary>
    public const float MapUnitsPerMetre = 32f;

    /// <summary>Texels per row of the sector and texture-info data textures (the shader's <c>SECTOR_DATA_WIDTH</c>).</summary>
    public const int DataWidth = 256;

    /// <summary>Vertex kinds (<c>CUSTOM0.x</c>).</summary>
    public const int KindFloor = 0, KindWall = 1;

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

    /// <summary>The texture atlas; slot <c>i</c>'s rectangle is <c>Atlas.Rects[i]</c>.</summary>
    public TextureAtlas Atlas { get; }

    /// <summary>The name of each texture slot: wall textures first, then flats.</summary>
    public IReadOnlyList<string> SlotNames { get; }

    /// <summary>The chunk of each sector (null when the sector has no floor and no wall).</summary>
    public ArrayMesh?[] SectorMeshes { get; private set; } = Array.Empty<ArrayMesh?>();

    /// <summary>The material every chunk uses.</summary>
    public ShaderMaterial Material { get; private set; } = null!;

    public ImageTexture AtlasTexture { get; private set; } = null!;
    public ImageTexture TextureInfoTexture { get; private set; } = null!;
    public ImageTexture SectorDataTexture { get; private set; } = null!;

    /// <summary>Number of wall sections in the meshes, and of floor triangles.</summary>
    public int WallQuads { get; private set; }
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

    /// <summary>Whether the meshes draw <paramref name="s"/>: solid sections with a texture (masked middles come in M3).</summary>
    public static bool IsDrawn(WallSection s) => s.Kind != WallSectionKind.MaskedMiddle && s.Texture != 0;

    /// <summary>A map position (fixed_t x, y) and height (map units) in Godot space.</summary>
    public static Vector3 ToGodot(int x, int y, float height) =>
        new((float)(x / 65536.0 / MapUnitsPerMetre), height / MapUnitsPerMetre, (float)(-y / 65536.0 / MapUnitsPerMetre));

    /// <summary>Selects the wall tiling (<see cref="WallTextureTiling"/>).</summary>
    public void SetWallTiling(WallTextureTiling tiling) => Material.SetShaderParameter("wall_tiling", (int)tiling);

    /// <summary>Forces one COLORMAP row everywhere (0–33), or -1 for the sector lights.</summary>
    public void SetColormapOverride(int map) => Material.SetShaderParameter("colormap_override", map);

    /// <summary>Selects the PLAYPAL palette.</summary>
    public void SetPalette(int palette) => Material.SetShaderParameter("palette_index", palette);

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
        Material.SetShaderParameter("atlas", AtlasTexture);
        Material.SetShaderParameter("texture_info", TextureInfoTexture);
        Material.SetShaderParameter("sector_data", SectorDataTexture);
        Material.SetShaderParameter("playpal", IndexedTextures.CreatePlaypalTexture(playpal));
        Material.SetShaderParameter("colormap", IndexedTextures.CreateColormapTexture(colormap));
        SetPalette(0);
        SetColormapOverride(-1);
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
            Chunk c = ChunkOf(s.FrontSector.Index);
            double dx = (s.V2.X - (double)s.V1.X) / 65536.0, dy = (s.V2.Y - (double)s.V1.Y) / 65536.0;
            float u1 = (float)(s.TextureOffset / 65536.0);
            float u2 = (float)(s.TextureOffset / 65536.0 + Math.Sqrt(dx * dx + dy * dy));
            var c0 = new Vector4(KindWall, TextureSlot(s.Texture), s.FrontSector.Index, s.BackSector?.Index ?? -1);
            var c1 = new Vector4((int)s.Bottom.Plane, Units(s.Bottom.Offset), (int)s.Top.Plane, Units(s.Top.Offset));
            var c2 = new Vector4((int)s.TextureTop.Plane, Units(s.TextureTop.Offset), 0, 0);
            Vector3 p1 = ToGodot(s.V1.X, s.V1.Y, 0), p2 = ToGodot(s.V2.X, s.V2.Y, 0);
            int first = c.Vertices.Count;
            c.Add(p1, new Vector2(u1, 0), c0, c1, c2); // V1 bottom
            c.Add(p1, new Vector2(u1, 1), c0, c1, c2); // V1 top
            c.Add(p2, new Vector2(u2, 1), c0, c1, c2); // V2 top
            c.Add(p2, new Vector2(u2, 0), c0, c1, c2); // V2 bottom
            // Seen from the front sector (on the side's right), V1 is on the left:
            // V1b, V1t, V2t is clockwise on screen, Godot's front face.
            c.Indices.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
            WallQuads++;
        }

        const Mesh.ArrayFormat custom =
            (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift)
            | (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift)
            | (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom2Shift);
        SectorMeshes = new ArrayMesh?[chunks.Length];
        Aabb? bounds = null;
        for (int s = 0; s < chunks.Length; s++)
        {
            Chunk? c = chunks[s];
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
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: custom);
            mesh.SurfaceSetMaterial(0, Material);

            // The shader moves vertices vertically, so cull with the full height range.
            Vector3 min = c.Vertices[0], max = c.Vertices[0];
            foreach (Vector3 v in c.Vertices)
            {
                min = min.Min(v);
                max = max.Max(v);
            }
            mesh.CustomAabb = new Aabb(new Vector3(min.X, -HeightRange, min.Z), new Vector3(max.X - min.X, 2 * HeightRange, max.Z - min.Z));
            SectorMeshes[s] = mesh;

            Sector sector = Level.Sectors[s];
            var box = new Aabb(new Vector3(min.X, sector.FloorHeight / 65536f / MapUnitsPerMetre, min.Z),
                new Vector3(max.X - min.X, Math.Max(0, sector.CeilingHeight - sector.FloorHeight) / 65536f / MapUnitsPerMetre, max.Z - min.Z));
            bounds = bounds is Aabb b ? b.Merge(box) : box;
        }
        Bounds = bounds ?? new Aabb();
    }

    private static float Units(int fixedValue) => (float)(fixedValue / 65536.0);
}
