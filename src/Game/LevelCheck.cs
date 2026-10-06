using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Map;
using IsoDoom.Render;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

/// <summary>
/// The level check (T2.6), started by the user argument <c>--level-check</c>
/// (the level scene adds it after opening the WAD; modelled on
/// <see cref="WadViewerCheck"/>):
/// <list type="number">
/// <item><b>Every map</b> of the WAD is loaded in the level scene and its
/// meshes built, each in under 1 s (SPEC §9; the first map with opening the
/// WAD). For each: every sector's data texel (floor, ceiling, light)
/// must equal the <see cref="Level"/>'s values; every drawn wall section's
/// texture slot must name its texture and point (through <c>texture_info</c>)
/// at an atlas rectangle holding exactly its composite, every floor's slot
/// exactly its flat; every chunk's vertex arrays must hold the sector's floor
/// triangles and its wall quads in order (masked middles in a second surface
/// with the masked material, whose parameters must equal the level
/// material's; T3.1), with the right positions, texture
/// columns, slots and sector ids, and the plane references in the attributes,
/// evaluated as the vertex shader does from the uploaded data texels, must
/// give <see cref="WallSection.Span"/> and the texture anchor. The atlas,
/// <c>texture_info</c> and sector data uploads are also read back from the
/// GPU and compared when a real renderer runs.</item>
/// <item><b>Moving a sector:</b> on the rendered map (<c>--level MAP</c>, else
/// <c>E1M1</c>/<c>MAP01</c>), the back sector of a pegged lower wall gets its
/// floor raised 24 units through the data texture; the data and attribute
/// checks run again, then the floor is put back.</item>
/// <item><b>Cursor ground point</b> (T3.3, every map, no renderer needed):
/// with a game camera (<see cref="IsoCamera"/>, not current) in each
/// projection, a point inside every sector's floor (its largest triangle's
/// centroid) is projected to the screen and picked back with
/// <see cref="CursorGround.Pick"/>: the pick must land on that floor at
/// that point, or on a higher floor in front of it along the ray, and the
/// floor it names must be the one <see cref="FloorTriangles.SectorAt"/>
/// finds there.</item>
/// <item><b>With a real renderer</b> (not <c>--headless</c>), drawn pixels of
/// the rendered map are compared with the CPU palette conversion
/// (<see cref="TextureWrap"/>, the light mapping of <see cref="LightTables"/>,
/// COLORMAP, PLAYPAL) at 1 map unit per pixel,
/// pixel centres on texel centres: straight down onto every floor (tiles over
/// the whole map; the void must show the background, a colour in no palette);
/// side-on (orthographic, perpendicular) onto every drawn axis-aligned wall
/// section, in vanilla tiling and again in texture-size tiling where the two
/// differ (masked middles over their whole opening: opaque texels drawn once,
/// holes and the rows above and below the texture showing the background); and for the moved sector an oblique view of its floor before and
/// after the move (the expected texels must change and the drawn ones follow)
/// and side-on views of every wall that uses its planes. Light (T2.8): all of
/// it with the default player-distance mapping from player 1's start; the
/// top-down floors again with no diminishing (and <c>extralight</c> 1), by
/// camera depth, and (one tile) with a fixed colormap. Pixels whose distance
/// is within <see cref="LightMargin"/> of a table step are skipped; at least
/// two sector light levels and both fake contrasts must be compared. The
/// cutaway (T3.4) from the game camera's angle: see LevelCheck.Cutaway.cs.</item>
/// </list>
/// Prints a summary and quits with exit code 1 on any failure.
/// </summary>
public partial class LevelCheck : Godot.Node
{
    /// <summary>Map units kept clear of section, sector and texel edges when picking pixels.</summary>
    private const int EdgeMargin = 1;

    /// <summary>SPEC §9: a level loads (WAD parse, mesh build) in under 1 s (T2.9).</summary>
    private const int LoadBudgetMilliseconds = 1000;

    /// <summary>How far the check raises the moved sector's floor (map units).</summary>
    private const int MoveUnits = 24;

    /// <summary>Allowed difference per channel for the background colour (it isn't a palette colour, so it goes through Godot's colour conversion).</summary>
    private const int BackgroundTolerance = 2;

    private readonly LevelScene _scene;
    private int _failures;
    private int _logged;
    private long _pixels;
    private int _views;
    private readonly Dictionary<int, IndexedImage> _composites = new();
    private readonly Dictionary<string, IndexedImage> _flats = new(StringComparer.OrdinalIgnoreCase);
    private (int R, int G, int B) _background;
    private readonly SortedSet<int> _lightLevels = new(), _contrasts = new(), _colormaps = new();
    private long _lightSkipped;
    private long _maskedOpaque, _maskedClear;
    private IsoCamera? _isoProbe;
    private int _cursorPoints, _cursorInFront;

    public LevelCheck(LevelScene scene) => _scene = scene;

    public LevelCheck() => _scene = null!; // for Godot's reflection; never used

    public override void _Ready() => _ = RunAsync();

    private static bool CanCapture => DisplayServer.GetName() != "headless";

    private Textures Textures => _scene.Textures!;
    private Playpal Playpal => _scene.Playpal!;
    private Colormap Colormap => _scene.Colormap!;

    private async Task RunAsync()
    {
        try
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            await NextFrame();
            CheckAllMaps();

            string map = RenderMapName();
            _scene.LoadMap(map);
            await NextFrame();
            LevelMesh m = _scene.Mesh!;
            (WallSection Lower, Sector Sector)? move = PickMovedSector(m);
            if (move is null)
                Fail($"{map}: no sector to move (needs the back sector of a drawn, pegged, axis-aligned lower with room to rise {MoveUnits})");
            if (CanCapture)
                await RenderChecks(m, move);
            else if (move is { } mv)
                MoveCheckData(m, mv.Sector);
            GD.Print(CanCapture
                ? $"Level check: {map}: {_views} views rendered, {_pixels} drawn pixels compared"
                : "Level check: headless: no pixel readback (run with a real renderer for the drawn-pixel checks)");
        }
        catch (Exception e)
        {
            Fail($"exception: {e}");
        }
        GD.Print(_failures == 0 ? "Level check: OK" : $"Level check: FAILED ({_failures} failure(s))");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private string RenderMapName()
    {
        string? map = WadLocator.GetUserArg("--level");
        return map is null || map.StartsWith('-') ? _scene.DefaultMap() : map.ToUpperInvariant();
    }

    // ---- Every map: meshes, slots, data textures (no renderer needed) ----

    private void CheckAllMaps()
    {
        if (_scene.MapNames.Count == 0)
            Fail("the WAD has no maps");
        int sections = 0, sectors = 0, slots = 0, vertices = 0;
        double slowest = 0;
        string slowestMap = "";
        bool first = true;
        foreach (string map in _scene.MapNames)
        {
            try
            {
                _scene.LoadMap(map);
                // SPEC §9: level load (WAD parse, mesh build) under 1 s per map; the first map also pays
                // for opening the WAD (and the JIT).
                double ms = _scene.LastLoadMilliseconds + (first ? _scene.OpenWadMilliseconds : 0);
                first = false;
                if (ms > slowest)
                    (slowest, slowestMap) = (ms, map);
                if (ms > LoadBudgetMilliseconds)
                    Fail($"{map}: loading took {ms:F0} ms, over the {LoadBudgetMilliseconds} ms budget (SPEC §9)");
            }
            catch (Exception e) when (e is WadFormatException or KeyNotFoundException)
            {
                Fail($"{map}: does not load: {e.Message}");
                continue;
            }
            LevelMesh m = _scene.Mesh!;
            int failures = _failures;
            CheckSectorData(m, map);
            slots += CheckSlots(m, map);
            CheckLightTables(m, map);
            CheckCursorGround(m, map);
            (int s, int v) = CheckChunks(m, map);
            sections += s;
            vertices += v;
            sectors += m.Level.Sectors.Length;
            if (_failures > failures)
                GD.PrintErr($"Level check: {map}: {_failures - failures} failure(s)");
        }
        GD.Print($"Level check: {_scene.MapNames.Count} maps built: {sectors} sector data texels, {slots} texture slots, "
            + $"{sections} wall sections and {vertices} vertices checked against the levels");
        GD.Print($"Level check: cursor ground point: {_cursorPoints} sector floors picked through the game camera "
            + $"(both projections), {_cursorInFront} on a higher floor in front");
        GD.Print($"Level check: load times: WAD opened in {_scene.OpenWadMilliseconds:F0} ms; slowest map {slowestMap}, "
            + $"{slowest:F0} ms (budget {LoadBudgetMilliseconds} ms, SPEC §9)");
    }

    /// <summary>
    /// T3.3: a point inside every sector's floor, seen through the game camera
    /// (orthographic and perspective, focused 96 units off the point so the ray
    /// is oblique in perspective), must be picked back by <see cref="CursorGround.Pick"/>
    /// on that floor, or on a higher floor in front of it along the ray.
    /// </summary>
    private void CheckCursorGround(LevelMesh m, string map)
    {
        if (_isoProbe is null)
        {
            _isoProbe = new IsoCamera { Name = "IsoProbe", InputEnabled = false };
            AddChild(_isoProbe);
        }
        IsoCamera cam = _isoProbe;
        foreach (IsoProjection projection in new[] { IsoProjection.Orthographic, IsoProjection.Perspective })
        {
            cam.SetProjectionMode(projection);
            foreach (SectorFloor floor in m.Floors.BySector)
            {
                if (floor.TriangleCount == 0)
                    continue;
                (int cx, int cy) = floor.InteriorPoint();
                Sector sector = m.Level.Sectors[floor.Sector];
                float height = sector.FloorHeight / 65536f;
                Vector3 p = LevelMesh.ToGodot(cx, cy, height);
                cam.Snap(p + new Vector3(96, 0, -48) / LevelMesh.MapUnitsPerMetre);
                Vector2 screen = cam.UnprojectPosition(p);
                Vector3 origin = cam.ProjectRayOrigin(screen), dir = cam.ProjectRayNormal(screen);
                string what = $"{map}: cursor ground ({projection}) over sector {floor.Sector} at ({cx / 65536.0:F1}, {cy / 65536.0:F1})";
                _cursorPoints++;
                if (CursorGround.Pick(m, origin, dir, height) is not { OnFloor: true } hit)
                {
                    Fail($"{what}: no floor picked");
                    continue;
                }
                Vector3 got = hit.MapUnits;
                int hitFloor = m.Level.Sectors[hit.Sector].FloorHeight;
                double offBy = (hit.Point - p).Length() * LevelMesh.MapUnitsPerMetre;
                if (hitFloor == sector.FloorHeight && offBy < 0.5)
                {
                    // On this floor (a sector of the same height there is the same surface).
                }
                else if (hitFloor > sector.FloorHeight && (hit.Point - origin).Dot(dir) < (p - origin).Dot(dir))
                    _cursorInFront++;
                else
                {
                    Fail($"{what}: picked sector {hit.Sector} at ({got.X:F1}, {got.Y:F1}, {got.Z:F1}), {offBy:F1} units off, "
                        + $"neither this floor ({sector.FloorHeight >> Fixed.FRACBITS}) nor a higher one in front");
                    continue;
                }
                int fx = (int)Math.Round(got.X * 65536.0), fy = (int)Math.Round(got.Y * 65536.0);
                int drawn = m.Floors.SectorAt(fx, fy);
                if (drawn != hit.Sector && (drawn < 0 || m.Level.Sectors[drawn].FloorHeight != hitFloor))
                    Fail($"{what}: picked sector {hit.Sector}, but the floor drawn at ({got.X:F1}, {got.Y:F1}) is {drawn}");
            }
        }
    }

    /// <summary>Every sector's data texel (and, with a renderer, the GPU copy) against the level.</summary>
    private void CheckSectorData(LevelMesh m, string map)
    {
        Image? gpu = CanCapture ? m.SectorDataTexture.GetImage() : null;
        if (CanCapture && (gpu is null || gpu.GetFormat() != Image.Format.Rgbaf))
            Fail($"{map}: the sector data texture can't be read back as RGBAF");
        foreach (Sector s in m.Level.Sectors)
        {
            var expected = new Color((float)(s.FloorHeight / 65536.0), (float)(s.CeilingHeight / 65536.0), s.LightLevel, 0);
            if (m.SectorData(s.Index) != expected)
                Fail($"{map}: sector {s.Index}: data texel {m.SectorData(s.Index)}, expected {expected}");
            if (gpu is not null && gpu.GetPixel(s.Index % LevelMesh.DataWidth, s.Index / LevelMesh.DataWidth) != expected)
                Fail($"{map}: sector {s.Index}: GPU data texel {gpu.GetPixel(s.Index % LevelMesh.DataWidth, s.Index / LevelMesh.DataWidth)}, expected {expected}");
        }
    }

    /// <summary>Texture binding: each drawn section's and floor's slot, its <c>texture_info</c> texel and atlas pixels. Returns slots checked.</summary>
    private int CheckSlots(LevelMesh m, string map)
    {
        var used = new bool[m.SlotNames.Count];
        if (m.Atlas.Rects.Count != m.SlotNames.Count)
            Fail($"{map}: {m.Atlas.Rects.Count} atlas rectangles for {m.SlotNames.Count} slots");

        void CheckSlot(int slot, string name, IndexedImage expected, string what)
        {
            if (slot < 0 || slot >= m.SlotNames.Count || slot >= m.Atlas.Rects.Count)
            {
                Fail($"{map}: {what}: no texture slot ({slot})");
                return;
            }
            if (m.SlotNames[slot] != name)
                Fail($"{map}: {what}: slot {slot} is {m.SlotNames[slot]}, expected {name}");
            if (used[slot])
                return;
            used[slot] = true;
            AtlasRect r = m.Atlas.Rects[slot];
            if (m.TextureInfo(slot) != new Color(r.X, r.Y, r.Width, r.Height))
                Fail($"{map}: slot {slot} ({name}): texture_info {m.TextureInfo(slot)}, atlas rectangle {r}");
            if (r.Width != expected.Width || r.Height != expected.Height)
            {
                Fail($"{map}: slot {slot} ({name}): atlas rectangle {r.Width}x{r.Height}, the texture is {expected.Width}x{expected.Height}");
                return;
            }
            IndexedImage atlas = m.Atlas.Image;
            for (int y = 0; y < r.Height; y++)
            {
                for (int x = 0; x < r.Width; x++)
                {
                    if (atlas[r.X + x, r.Y + y] != expected[x, y] || atlas.IsOpaque(r.X + x, r.Y + y) != expected.IsOpaque(x, y))
                    {
                        Fail($"{map}: slot {slot} ({name}): atlas texel ({x},{y}) differs from the texture");
                        return;
                    }
                }
            }
        }

        foreach (WallSection s in m.Walls.Sections)
        {
            if (LevelMesh.IsDrawn(s))
                CheckSlot(m.TextureSlot(s.Texture), Textures.TextureDefs[s.Texture].Name, Composite(s.Texture), $"line {s.Line.Index} side {s.Side} {s.Kind}");
        }
        foreach (Sector s in m.Level.Sectors)
            CheckSlot(m.FlatSlot(s.FloorPic), s.FloorPic, FlatImage(s.FloorPic), $"sector {s.Index} floor");
        for (int i = 0; i < used.Length; i++)
        {
            if (!used[i])
                Fail($"{map}: slot {i} ({m.SlotNames[i]}) is used by nothing");
        }

        // The uploads: the atlas as RG8, texture_info as RGBAF (read back from the GPU when there is one).
        byte[] rg8 = IndexedTextures.ToRg8(m.Atlas.Image);
        Image? atlasImage = CanCapture ? m.AtlasTexture.GetImage() : IndexedTextures.CreateImage(m.Atlas.Image);
        if (atlasImage is null || atlasImage.GetFormat() != Image.Format.Rg8 || atlasImage.GetWidth() != m.Atlas.Image.Width
            || atlasImage.GetHeight() != m.Atlas.Image.Height || !atlasImage.GetData().AsSpan().SequenceEqual(rg8))
            Fail($"{map}: the atlas upload differs from the atlas");
        if (CanCapture)
        {
            Image? info = m.TextureInfoTexture.GetImage();
            for (int i = 0; info is not null && i < m.Atlas.Rects.Count; i++)
            {
                if (info.GetPixel(i % LevelMesh.DataWidth, i / LevelMesh.DataWidth) != m.TextureInfo(i))
                {
                    Fail($"{map}: slot {i}: the GPU texture_info texel differs");
                    break;
                }
            }
            if (info is null)
                Fail($"{map}: texture_info can't be read back");
        }
        if (m.Material.GetShaderParameter("atlas").As<Texture2D>() != m.AtlasTexture
            || m.Material.GetShaderParameter("texture_info").As<Texture2D>() != m.TextureInfoTexture
            || m.Material.GetShaderParameter("sector_data").As<Texture2D>() != m.SectorDataTexture)
            Fail($"{map}: the material doesn't bind the level's atlas and data textures");
        // The masked middles' material (T3.1) gets every parameter the level material has.
        foreach (Godot.Collections.Dictionary parameter in m.Material.Shader.GetShaderUniformList())
        {
            string name = parameter["name"].AsString();
            Variant a = m.Material.GetShaderParameter(name), b = m.MaskedMaterial.GetShaderParameter(name);
            if (a.VariantType != b.VariantType || a.ToString() != b.ToString()
                || (a.VariantType == Variant.Type.Object && a.AsGodotObject() != b.AsGodotObject()))
                Fail($"{map}: the masked material's {name} ({b}) differs from the level material's ({a})");
        }
        return used.Length;
    }

    /// <summary>The light tables upload (r_main.c zlight/scalelight as <see cref="LightTables.ToBytes"/>) and its binding.</summary>
    private void CheckLightTables(LevelMesh m, string map)
    {
        byte[] expected = m.Lights.ToBytes();
        Image? image = m.LightTablesTexture.GetImage(); // null under the dummy renderer
        if (image is null)
        {
            if (CanCapture)
                Fail($"{map}: the light tables can't be read back");
        }
        else if (image.GetFormat() != Image.Format.R8 || image.GetWidth() != LightTables.TableWidth || image.GetHeight() != LightTables.TableHeight
            || !image.GetData().AsSpan().SequenceEqual(expected))
            Fail($"{map}: the light tables upload differs from LightTables");
        if (m.Material.GetShaderParameter("light_tables").As<Texture2D>() != m.LightTablesTexture
            || m.Material.GetShaderParameter("light_centerx").AsInt32() != m.Lights.CenterX)
            Fail($"{map}: the material doesn't bind the light tables");
        // Vanilla's tables, spot-checked (r_main.c; unit tests check more).
        if (m.Lights.zlight(8, 15) != 23 || m.Lights.scalelight(8, 10) != 23 || m.Lights.zlight(0, 1) != 20 || m.Lights.scalelight(10, 40) != 0)
            Fail($"{map}: the light tables aren't vanilla's");
    }

    /// <summary>
    /// Each sector chunk's vertex arrays against its floor triangles and wall
    /// sections, with the plane references evaluated from the uploaded data
    /// texels as the vertex shader does. A chunk has a first surface with
    /// <see cref="LevelMesh.Material"/> (floor, then solid wall quads) when it
    /// has either, and a last one with <see cref="LevelMesh.MaskedMaterial"/>
    /// (masked middle quads, T3.1) when it has any. Returns (sections, vertices) checked.
    /// </summary>
    private (int Sections, int Vertices) CheckChunks(LevelMesh m, string map)
    {
        Level level = m.Level;
        var walls = new List<WallSection>?[level.Sectors.Length];
        var maskedWalls = new List<WallSection>?[level.Sectors.Length];
        foreach (WallSection s in m.Walls.Sections)
        {
            if (LevelMesh.IsDrawn(s))
                ((LevelMesh.IsMasked(s) ? maskedWalls : walls)[s.FrontSector.Index] ??= new List<WallSection>()).Add(s);
        }
        var floors = new SectorFloor?[level.Sectors.Length];
        foreach (SectorFloor f in m.Floors.BySector)
            floors[f.Sector] = f;

        var sideSegs = new List<Seg>?[level.Lines.Length * 2];
        foreach (Seg seg in level.Segs)
            (sideSegs[seg.LineDef.Index * 2 + seg.Side] ??= new List<Seg>()).Add(seg);
        int sectionCount = 0, vertexCount = 0, quads = 0, maskedQuads = 0;
        for (int sector = 0; sector < level.Sectors.Length; sector++)
        {
            string what = $"{map}: sector {sector}";
            SectorFloor? floor = floors[sector] is { TriangleCount: > 0 } f ? f : null;
            List<WallSection> sectorWalls = walls[sector] ?? new List<WallSection>();
            List<WallSection> sectorMasked = maskedWalls[sector] ?? new List<WallSection>();
            int floorVertices = floor?.Vertices.Count ?? 0;
            int QuadCount(List<WallSection> list)
            {
                int n = 0;
                foreach (WallSection s in list)
                    n += m.Pieces.Of(s.Line, s.Side).Count;
                return n;
            }
            int solidVertices = floorVertices + 4 * QuadCount(sectorWalls);
            int maskedVertices = 4 * QuadCount(sectorMasked);
            ArrayMesh? mesh = sector < m.SectorMeshes.Length ? m.SectorMeshes[sector] : null;
            if (solidVertices + maskedVertices == 0)
            {
                if (mesh is not null)
                    Fail($"{what}: has a chunk but no floor or wall");
                continue;
            }
            int surfaces = (solidVertices > 0 ? 1 : 0) + (maskedVertices > 0 ? 1 : 0);
            if (mesh is null || mesh.GetSurfaceCount() != surfaces)
            {
                Fail($"{what}: no chunk (or not {surfaces} surface(s))");
                continue;
            }
            if (_scene.Chunks.Length <= sector || _scene.Chunks[sector]?.Mesh != mesh)
                Fail($"{what}: the chunk is not in the scene");

            int surface = 0;
            if (solidVertices > 0)
            {
                if (mesh.SurfaceGetMaterial(surface) != m.Material)
                    Fail($"{what}: the chunk's first surface doesn't use the level material");
                quads += CheckSurface(m, mesh, surface++, sector, floor, sectorWalls, sideSegs, what, ref sectionCount, ref vertexCount);
            }
            if (maskedVertices > 0)
            {
                if (mesh.SurfaceGetMaterial(surface) != m.MaskedMaterial)
                    Fail($"{what}: the chunk's masked surface doesn't use the masked material");
                maskedQuads += CheckSurface(m, mesh, surface, sector, null, sectorMasked, sideSegs, $"{what} (masked)", ref sectionCount, ref vertexCount);
            }
        }
        if (quads != m.WallQuads)
            Fail($"{map}: {m.WallQuads} wall quads, {quads} expected");
        if (maskedQuads != m.MaskedQuads)
            Fail($"{map}: {m.MaskedQuads} masked middle quads, {maskedQuads} expected");
        return (sectionCount, vertexCount);
    }

    /// <summary>
    /// One surface of a chunk: <paramref name="floor"/>'s triangles (if any),
    /// then one quad per piece of each of <paramref name="sectionsOfSurface"/>
    /// (T2.9), A bottom, A top, B top, B bottom. Returns the wall quads checked.
    /// </summary>
    private int CheckSurface(LevelMesh m, ArrayMesh mesh, int surface, int sector, SectorFloor? floor, List<WallSection> sectionsOfSurface,
        List<Seg>?[] sideSegs, string what, ref int sectionCount, ref int vertexCount)
    {
        Level level = m.Level;
        int floorVertices = floor?.Vertices.Count ?? 0;
        int quadCount = 0;
        foreach (WallSection s in sectionsOfSurface)
            quadCount += m.Pieces.Of(s.Line, s.Side).Count;
        int expectedVertices = floorVertices + 4 * quadCount;
        Godot.Collections.Array arrays = mesh.SurfaceGetArrays(surface);
        Vector3[] pos = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        Vector2[] uv = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        float[] c0 = arrays[(int)Mesh.ArrayType.Custom0].AsFloat32Array();
        float[] c1 = arrays[(int)Mesh.ArrayType.Custom1].AsFloat32Array();
        float[] c2 = arrays[(int)Mesh.ArrayType.Custom2].AsFloat32Array();
        int[] idx = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        int expectedIndices = (floor?.Indices.Count ?? 0) + 6 * quadCount;
        if (pos.Length != expectedVertices || uv.Length != expectedVertices || c0.Length != 4 * expectedVertices
            || c1.Length != 4 * expectedVertices || c2.Length != 4 * expectedVertices || idx.Length != expectedIndices)
        {
            Fail($"{what}: {pos.Length} vertices / {idx.Length} indices, expected {expectedVertices} / {expectedIndices}");
            return 0;
        }
        vertexCount += expectedVertices;

        // Floor: its corners, the flat's slot, the triangle list.
        if (floor is not null)
        {
            int flatSlot = m.FlatSlot(level.Sectors[sector].FloorPic);
            for (int i = 0; i < floorVertices; i++)
            {
                PolygonVertex v = floor.Vertices[i];
                if (!Near(pos[i], LevelMesh.ToGodot(v.X, v.Y, 0)) || !Near(uv[i], new Vector2((float)(v.X / 65536.0), (float)(-v.Y / 65536.0)))
                    || !Custom(c0, i, LevelMesh.KindFloor, flatSlot, sector, -1))
                {
                    Fail($"{what}: floor vertex {i} differs");
                    break;
                }
            }
            for (int i = 0; i < floor.Indices.Count; i++)
            {
                if (idx[i] != floor.Indices[i])
                {
                    Fail($"{what}: floor index {i} is {idx[i]}, expected {floor.Indices[i]}");
                    break;
                }
            }
        }

        // Walls: one quad per piece of each drawn section's side (T2.9), A bottom, A top, B top, B bottom.
        int quad = 0;
        foreach (WallSection s in sectionsOfSurface)
        {
            string sw = $"{level.Name}: line {s.Line.Index} side {s.Side} {s.Kind}";
            int kind = LevelMesh.IsMasked(s) ? LevelMesh.KindMasked : LevelMesh.KindWall;
            sectionCount++;
            IReadOnlyList<WallPiece> pieces = m.Pieces.Of(s.Line, s.Side);
            CheckPieces(s, sideSegs[s.Line.Index * 2 + s.Side], pieces, sw);
            foreach (WallPiece piece in pieces)
            {
                int contrast = piece.Contrast;
                int b = floorVertices + 4 * quad, bi = (floor?.Indices.Count ?? 0) + 6 * quad;
                quad++;
                Vector3 p1 = LevelMesh.ToGodot(piece.A.X, piece.A.Y, 0), p2 = LevelMesh.ToGodot(piece.B.X, piece.B.Y, 0);
                Vector3[] corners = { p1, p1, p2, p2 };
                double ua = (s.TextureOffset + (long)piece.ColumnA) / 65536.0, ub = (s.TextureOffset + (long)piece.ColumnB) / 65536.0;
                double[] us = { ua, ua, ub, ub };
                float[] vs = { 0, 1, 1, 0 };
                for (int k = 0; k < 4; k++)
                {
                    if (!Near(pos[b + k], corners[k]))
                        Fail($"{sw}: corner {k} at {pos[b + k]}, expected {corners[k]}");
                    if (Math.Abs(uv[b + k].X - us[k]) > 1e-3 || uv[b + k].Y != vs[k])
                        Fail($"{sw}: corner {k} texture coordinate {uv[b + k]}, expected ({us[k]}, {vs[k]})");
                    if (!Custom(c0, b + k, kind, m.TextureSlot(s.Texture), sector, s.BackSector?.Index ?? -1))
                        Fail($"{sw}: corner {k}: kind/slot/sectors ({Custom4(c0, b + k)}), expected {kind}, slot {m.TextureSlot(s.Texture)}, {sector}, {s.BackSector?.Index ?? -1}");
                    if (c2[(b + k) * 4 + 2] != contrast)
                        Fail($"{sw}: corner {k}: fake contrast {c2[(b + k) * 4 + 2]}, expected {contrast}");
                    if (c2[(b + k) * 4 + 3] != LevelMesh.PieceAngle(piece))
                        Fail($"{sw}: corner {k}: direction {c2[(b + k) * 4 + 3]}, expected {LevelMesh.PieceAngle(piece)} (T3.4)");
                }
                int[] quadIndices = { b, b + 1, b + 2, b, b + 2, b + 3 };
                for (int k = 0; k < 6; k++)
                {
                    if (idx[bi + k] != quadIndices[k])
                    {
                        Fail($"{sw}: quad indices differ");
                        break;
                    }
                }

                // The vertex shader's evaluation of the plane references, from the uploaded data texels
                // (and, for a masked middle, the texture height from texture_info).
                for (int k = 0; k < 4; k++)
                {
                    int front = (int)c0[(b + k) * 4 + 2], back = (int)c0[(b + k) * 4 + 3];
                    if (front < 0 || front >= level.Sectors.Length || back >= level.Sectors.Length)
                    {
                        Fail($"{sw}: corner {k}: sector ids {front}, {back} out of range");
                        break;
                    }
                    Color fd = m.SectorData(front), bd = back >= 0 ? m.SectorData(back) : fd;
                    float bottom = Math.Max(PlaneHeight((int)c1[(b + k) * 4], fd, bd) + c1[(b + k) * 4 + 1], fd.R);
                    float top = Math.Min(PlaneHeight((int)c1[(b + k) * 4 + 2], fd, bd) + c1[(b + k) * 4 + 3], fd.G);
                    float textureTop = PlaneHeight((int)c2[(b + k) * 4], fd, bd) + c2[(b + k) * 4 + 1];
                    if ((int)c0[(b + k) * 4] == LevelMesh.KindMasked)
                    {
                        top = Math.Min(top, textureTop);
                        bottom = Math.Max(bottom, textureTop - m.TextureInfo((int)c0[(b + k) * 4 + 1]).A);
                    }
                    top = Math.Max(top, bottom);
                    (int sb, int st) = s.Span();
                    float eb = (float)(sb / 65536.0), et = (float)(Math.Max(st, sb) / 65536.0);
                    float ett = (float)(s.TextureTop.Evaluate(s.FrontSector, s.BackSector) / 65536.0);
                    if (Math.Abs(bottom - eb) > 1e-3 || Math.Abs(top - et) > 1e-3 || Math.Abs(textureTop - ett) > 1e-3)
                    {
                        Fail($"{sw}: corner {k}: the attributes give span {bottom}..{top}, row 0 at {textureTop}; expected {eb}..{et}, row 0 at {ett}");
                        break;
                    }
                }
            }
        }
        return quad;
    }

    /// <summary>
    /// fixed_t: how far a piece's corner may lie from its seg: the floor edge's
    /// snap tolerance (<see cref="SubsectorPolygons.SegSnapEpsilon"/>, measured
    /// with the L1 length, so up to √2 times that for a diagonal) plus rounding.
    /// </summary>
    private const long PieceTolerance = 3 * Fixed.FRACUNIT;

    /// <summary>
    /// A section's pieces (T2.9) against vanilla's segs: every seg of the side
    /// has pieces, and the pieces of a seg join end to end along it (each
    /// corner within <see cref="PieceTolerance"/> of the seg's line and
    /// between its ends, the first and last near its ends), with the seg's
    /// fake contrast (r_segs.c) and texture columns of the seg's
    /// <c>offset</c> plus the distance along it (r_segs.c <c>rw_offset</c>).
    /// Consecutive segs' pieces join, or a connector pair (both directions)
    /// bridges them.
    /// </summary>
    private void CheckPieces(WallSection s, List<Seg>? segs, IReadOnlyList<WallPiece> pieces, string what)
    {
        if (segs is null || pieces.Count == 0)
        {
            Fail($"{what}: no seg or no wall piece along the side");
            return;
        }
        var bySeg = new Dictionary<int, Seg>();
        foreach (Seg seg in segs)
            bySeg[seg.Index] = seg;
        var seen = new HashSet<int>();
        bool Reversed(int i, int j) => i >= 0 && j < pieces.Count && pieces[i].A == pieces[j].B && pieces[i].B == pieces[j].A;
        for (int i = 0; i < pieces.Count; i++)
        {
            WallPiece piece = pieces[i];
            bool connector = Reversed(i, i + 1) || Reversed(i - 1, i);
            if (!bySeg.TryGetValue(piece.Seg, out Seg? seg))
            {
                Fail($"{what}: piece {i} belongs to seg {piece.Seg}, not a seg of the side");
                return;
            }
            seen.Add(seg.Index);
            if (piece.Contrast != LightTables.FakeContrast(seg.V1.X, seg.V1.Y, seg.V2.X, seg.V2.Y))
                Fail($"{what}: piece {i} has fake contrast {piece.Contrast}, its seg {seg.Index} {LightTables.FakeContrast(seg.V1.X, seg.V1.Y, seg.V2.X, seg.V2.Y)}");
            // A connector pair p → q, q → p: only q (its seg's first corner) is on its seg; p ends the previous seg.
            var corners = !connector ? new[] { (piece.A, piece.ColumnA), (piece.B, piece.ColumnB) }
                : Reversed(i, i + 1) ? new[] { (piece.B, piece.ColumnB) } : new[] { (piece.A, piece.ColumnA) };
            foreach ((PolygonVertex p, int column) in corners)
            {
                (double along, double off, double length) = OnSeg(seg, p);
                if (off > PieceTolerance / 65536.0 || along < -PieceTolerance / 65536.0 || along > length + PieceTolerance / 65536.0)
                    Fail($"{what}: piece {i} corner {p} is {off:F3} units off seg {seg.Index}, {along:F3} along it (length {length:F3})");
                if (Math.Abs(column / 65536.0 - (seg.Offset / 65536.0 + along)) > 1.0 / 256)
                    Fail($"{what}: piece {i} corner {p}: texture column {column / 65536.0:F3}, expected seg offset {seg.Offset / 65536.0} + {along:F3}");
            }
            // In order: ... → p, the connector p → q and back q → p, then q → ...
            if (i > 0 && pieces[i - 1].B != piece.A && !Reversed(i - 1, i) && !(Reversed(i - 2, i - 1) && pieces[i - 1].A == piece.A))
                Fail($"{what}: pieces {i - 1} and {i} don't join");
        }
        foreach (Seg seg in segs)
        {
            if (!seen.Contains(seg.Index) && (seg.V1.X != seg.V2.X || seg.V1.Y != seg.V2.Y))
                Fail($"{what}: seg {seg.Index} has no wall piece");
        }
    }

    /// <summary>Point <paramref name="p"/> against <paramref name="seg"/>: its distance along the seg from V1, from the seg's line, and the seg's length (map units).</summary>
    private static (double Along, double Off, double Length) OnSeg(Seg seg, PolygonVertex p)
    {
        double dx = (seg.V2.X - (double)seg.V1.X) / 65536.0, dy = (seg.V2.Y - (double)seg.V1.Y) / 65536.0;
        double px = (p.X - (double)seg.V1.X) / 65536.0, py = (p.Y - (double)seg.V1.Y) / 65536.0;
        double length = Math.Sqrt(dx * dx + dy * dy);
        return length == 0 ? (0, Math.Sqrt(px * px + py * py), 0) : ((px * dx + py * dy) / length, Math.Abs(px * dy - py * dx) / length, length);
    }

    /// <summary>The shader's <c>plane_height</c> (IsoDoom.Map.WallPlane numbers).</summary>
    private static float PlaneHeight(int plane, Color front, Color back) => plane switch
    {
        0 => front.R,
        1 => front.G,
        2 => back.R,
        3 => back.G,
        4 => Math.Max(front.R, back.R),
        _ => Math.Min(front.G, back.G),
    };

    private static bool Near(Vector3 a, Vector3 b) => (a - b).Length() < 1e-4f;

    private static bool Near(Vector2 a, Vector2 b) => (a - b).Length() < 1e-3f;

    private static bool Custom(float[] c, int vertex, int x, int y, int z, int w) =>
        c[vertex * 4] == x && c[vertex * 4 + 1] == y && c[vertex * 4 + 2] == z && c[vertex * 4 + 3] == w;

    private static string Custom4(float[] c, int vertex) => $"{c[vertex * 4]}, {c[vertex * 4 + 1]}, {c[vertex * 4 + 2]}, {c[vertex * 4 + 3]}";

    // ---- Moving a sector ----

    /// <summary>The back sector of the first drawn, pegged, axis-aligned lower (and the lower) that can rise <see cref="MoveUnits"/>.</summary>
    private static (WallSection Lower, Sector Sector)? PickMovedSector(LevelMesh m)
    {
        var hasFloor = new bool[m.Level.Sectors.Length];
        foreach (SectorFloor f in m.Floors.BySector)
            hasFloor[f.Sector] |= f.TriangleCount > 0;
        const int room = 2 * MoveUnits << Fixed.FRACBITS;
        foreach (WallSection s in m.Walls.Sections)
        {
            if (s.Kind == WallSectionKind.Lower && LevelMesh.IsDrawn(s) && s.BackSector is Sector back && back != s.FrontSector
                && (s.Line.Flags & Line.ML_DONTPEGBOTTOM) == 0 && IsSideOnCheckable(s) && WallLength(s) >= 16 && hasFloor[back.Index]
                && back.CeilingHeight - back.FloorHeight >= room && s.FrontSector.CeilingHeight - back.FloorHeight >= room)
                return (s, back);
        }
        return null;
    }

    /// <summary>The CPU part of the move: data texels and attributes follow, and come back.</summary>
    private void MoveCheckData(LevelMesh m, Sector sector)
    {
        int old = sector.FloorHeight;
        sector.FloorHeight = old + (MoveUnits << Fixed.FRACBITS);
        m.UpdateSectors();
        CheckSectorData(m, $"{m.Level.Name} (sector {sector.Index} raised {MoveUnits})");
        CheckChunks(m, $"{m.Level.Name} (sector {sector.Index} raised {MoveUnits})");
        sector.FloorHeight = old;
        m.UpdateSectors();
        CheckSectorData(m, $"{m.Level.Name} (sector {sector.Index} back)");
        GD.Print($"Level check: {m.Level.Name}: sector {sector.Index}'s floor raised {MoveUnits} through the data texture and back: data texels and wall attributes follow");
    }

    // ---- Drawn pixels (real renderer only) ----

    private async Task RenderChecks(LevelMesh m, (WallSection Lower, Sector Sector)? move)
    {
        string map = m.Level.Name;
        Color oldBackground = _scene.Environment.BackgroundColor;
        _background = UnusedColor(Playpal);
        _scene.Environment.BackgroundColor = Color.Color8((byte)_background.R, (byte)_background.G, (byte)_background.B);
        _scene.Overlay.Visible = false;

        // Light (T2.8): the player-distance mapping (default) from player 1's
        // start; then top-down again with no diminishing (and extralight 1),
        // by camera depth, and with a fixed colormap (invulnerability).
        Vector2 origin = m.Level.PlayerStart(0) is MapThing start ? new Vector2(start.X, start.Y)
            : new Vector2(m.Bounds.GetCenter().X, -m.Bounds.GetCenter().Z) * LevelMesh.MapUnitsPerMetre;
        m.SetLightOrigin(origin);
        m.SetLightDiminishing(LightDiminishing.Player);
        await CheckFloorsTopDown(m, $"player light from ({origin.X}, {origin.Y})");
        m.SetLightDiminishing(LightDiminishing.None);
        m.SetExtraLight(1);
        await CheckFloorsTopDown(m, $"no diminishing (distance {m.LightReference}), extralight 1");
        m.SetExtraLight(0);
        m.SetLightDiminishing(LightDiminishing.Camera);
        await CheckFloorsTopDown(m, "camera depth light");
        m.SetLightDiminishing(LightDiminishing.Player);
        m.SetColormapOverride(Colormap.INVERSECOLORMAP);
        await CheckFloorsTopDown(m, "fixed colormap 32", 1);
        m.SetColormapOverride(-1);

        var sizeTiling = new List<WallSection>();
        int walls = 0, wallPixels = 0, maskedWalls = 0, maskedCandidates = 0;
        foreach (WallSection s in m.Walls.Sections)
        {
            if (!LevelMesh.IsDrawn(s) || !IsSideOnCheckable(s))
                continue;
            if (LevelMesh.IsMasked(s))
                maskedCandidates++;
            (int n, bool differs) = await CheckWall(m, s, WallTextureTiling.Vanilla, map);
            if (n > 0)
            {
                walls++;
                if (LevelMesh.IsMasked(s))
                    maskedWalls++;
            }
            wallPixels += n;
            if (differs)
                sizeTiling.Add(s);
        }
        GD.Print($"Level check: {map}: {walls} wall sections side-on (vanilla tiling; {maskedWalls} of {maskedCandidates} axis-aligned masked middles: "
            + $"{_maskedOpaque} opaque texels, {_maskedClear} clear pixels in their openings), {wallPixels} drawn pixels compared");
        if (walls == 0)
            Fail($"{map}: no wall section could be checked side-on");
        if (maskedCandidates > 0 && (maskedWalls == 0 || _maskedOpaque == 0 || _maskedClear == 0))
            Fail($"{map}: the masked middles must be compared side-on, with both opaque texels and clear pixels");

        m.SetWallTiling(WallTextureTiling.TextureSize);
        wallPixels = 0;
        foreach (WallSection s in sizeTiling)
            wallPixels += (await CheckWall(m, s, WallTextureTiling.TextureSize, map)).Pixels;
        m.SetWallTiling(WallTextureTiling.Vanilla);
        GD.Print($"Level check: {map}: {sizeTiling.Count} wall sections where the tilings differ, again in texture-size tiling: {wallPixels} drawn pixels compared");

        await CutawayCheck(m);

        if (move is { } mv)
            await MoveCheck(m, mv.Lower, mv.Sector);

        GD.Print($"Level check: {map}: light compared at sector light levels {string.Join(" ", _lightLevels)} (>> 4), "
            + $"wall contrasts {string.Join(" ", _contrasts)}, colormaps {string.Join(" ", _colormaps)}; "
            + $"{_lightSkipped} pixels skipped within {LightMargin} units of a light table step");
        if (_lightLevels.Count < 2)
            Fail($"{map}: fewer than two light levels compared");
        if (!_contrasts.Contains(-1) || !_contrasts.Contains(1))
            Fail($"{map}: walls of both orientations (fake contrast -1 and +1) must be compared");

        _scene.Overlay.Visible = true;
        _scene.Environment.BackgroundColor = oldBackground;
        _scene.FrameCamera();
    }

    private Vector2I ViewSize()
    {
        Vector2 size = GetViewport().GetVisibleRect().Size;
        return new Vector2I((int)size.X, (int)size.Y);
    }

    /// <summary>An orthographic camera at 1 map unit per pixel.</summary>
    private void Ortho(Basis basis, Vector3 originMapUnits, float nearUnits, float farUnits)
    {
        Camera3D cam = _scene.Camera;
        cam.Projection = Camera3D.ProjectionType.Orthogonal;
        cam.KeepAspect = Camera3D.KeepAspectEnum.Height;
        cam.Size = ViewSize().Y / LevelMesh.MapUnitsPerMetre;
        cam.Near = nearUnits / LevelMesh.MapUnitsPerMetre;
        cam.Far = farUnits / LevelMesh.MapUnitsPerMetre;
        // Map units (x, y, height) → Godot (x, height, -y), in metres.
        cam.GlobalTransform = new Transform3D(basis, new Vector3(originMapUnits.X, originMapUnits.Z, -originMapUnits.Y) / LevelMesh.MapUnitsPerMetre);
        cam.Current = true;
    }

    /// <summary>Renders and reads back the frame as RGBA8 bytes (null after a failure).</summary>
    private async Task<byte[]?> Capture(string what)
    {
        await NextFrame();
        await NextFrame();
        Image frame = GetViewport().GetTexture().GetImage();
        Vector2I size = ViewSize();
        if (frame.GetWidth() != size.X || frame.GetHeight() != size.Y)
        {
            Fail($"{what}: captured {frame.GetWidth()}x{frame.GetHeight()}, the viewport is {size.X}x{size.Y}");
            return null;
        }
        if (frame.GetFormat() != Image.Format.Rgba8)
            frame.Convert(Image.Format.Rgba8);
        _views++;
        return frame.GetData();
    }

    /// <summary>
    /// Straight down onto every floor, in tiles of one viewport at 1 unit per
    /// pixel with pixel centres on texel centres. Each pixel whose 5×5
    /// neighbourhood lies in one sector's floor triangles (rasterised on the
    /// CPU) must show that sector's flat texel; in the void, the background.
    /// </summary>
    private async Task CheckFloorsTopDown(LevelMesh m, string pass, int maxTiles = int.MaxValue)
    {
        string map = m.Level.Name;
        int tiles = 0;
        Vector2I size = ViewSize();
        int w = size.X, h = size.Y;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (SectorFloor f in m.Floors.BySector)
        {
            foreach (PolygonVertex v in f.Vertices)
            {
                minX = Math.Min(minX, v.X / 65536.0);
                maxX = Math.Max(maxX, v.X / 65536.0);
                minY = Math.Min(minY, v.Y / 65536.0);
                maxY = Math.Max(maxY, v.Y / 65536.0);
            }
        }
        (int lowest, int highest) = HeightRange(m.Level);
        int left0 = (int)Math.Floor(minX) - 8, top0 = (int)Math.Ceiling(maxY) + 8;
        int tilesX = (int)Math.Ceiling((maxX + 8 - left0) / w), tilesY = (int)Math.Ceiling((top0 - (minY - 8)) / h);
        // Screen right = map +x, screen up = map +y (Godot −Z), looking down (−Y).
        var basis = new Basis(new Vector3(1, 0, 0), new Vector3(0, 0, -1), new Vector3(0, 1, 0));

        long compared = 0, voidPixels = 0;
        var sectorsSeen = new bool[m.Level.Sectors.Length];
        for (int ty = 0; ty < tilesY; ty++)
        {
            for (int tx = 0; tx < tilesX; tx++)
            {
                if (tiles++ >= maxTiles)
                    break;
                int left = left0 + tx * w, top = top0 - ty * h;
                int[] grid = RasterFloors(m, left, top, w, h);
                bool[] uniform = Uniform(grid, w, h, 2);
                // Half a unit off whole heights, so camera depths don't sit on a light table step.
                float cameraZ = highest + 64.5f;
                Ortho(basis, new Vector3(left + w / 2f, top - h / 2f, cameraZ), 1, highest - lowest + 128);
                string what = $"{map}: floors top-down ({pass}), tile ({tx},{ty}) at x {left}, y {top}";
                byte[]? frame = await Capture(what);
                if (frame is null)
                    continue;
                int bad = 0;
                string first = "";
                for (int py = 0; py < h; py++)
                {
                    for (int px = 0; px < w; px++)
                    {
                        int p = py * w + px;
                        if (!uniform[p])
                            continue;
                        int s = grid[p];
                        int x = ((left + px) << Fixed.FRACBITS) + Fixed.FRACUNIT / 2;
                        int y = ((top - py) << Fixed.FRACBITS) - Fixed.FRACUNIT / 2;
                        (int R, int G, int B) got = (frame[p * 4], frame[p * 4 + 1], frame[p * 4 + 2]);
                        bool ok;
                        (int R, int G, int B) expected;
                        if (s < 0)
                        {
                            expected = _background;
                            ok = Math.Abs(got.R - expected.R) <= BackgroundTolerance && Math.Abs(got.G - expected.G) <= BackgroundTolerance
                                && Math.Abs(got.B - expected.B) <= BackgroundTolerance;
                            voidPixels++;
                        }
                        else
                        {
                            Sector sector = m.Level.Sectors[s];
                            int map0 = ExpectedColormap(m, false, sector.LightLevel, 0, x / 65536.0, y / 65536.0,
                                cameraZ - sector.FloorHeight / 65536.0);
                            if (map0 < 0)
                                continue;
                            (int col, int row) = TextureWrap.FlatTexel(x, y);
                            expected = Shade(FlatImage(sector.FloorPic)[col, row], map0);
                            ok = got == expected;
                            sectorsSeen[s] = true;
                        }
                        compared++;
                        if (!ok && bad++ == 0)
                            first = $"map ({x / 65536.0}, {y / 65536.0}) {(s < 0 ? "void" : $"sector {s}")}: drew {got}, expected {expected}";
                    }
                }
                if (bad > 0)
                    Fail($"{what}: {bad} pixel(s) differ, first at {first}");
            }
        }
        int seen = 0, withFloor = 0;
        foreach (SectorFloor f in m.Floors.BySector)
        {
            if (f.TriangleCount == 0)
                continue;
            withFloor++;
            if (sectorsSeen[f.Sector])
                seen++;
        }
        _pixels += compared;
        GD.Print($"Level check: {map}: floors top-down ({pass}) in {Math.Min(tiles, tilesX * tilesY)} tile(s): {compared} drawn pixels compared "
            + $"({voidPixels} void), {seen} of {withFloor} floors seen");
        if (seen == 0)
            Fail($"{map}: no floor pixel compared");
    }

    /// <summary>The sector whose floor triangles cover each pixel centre (−1 none, −2 several).</summary>
    private static int[] RasterFloors(LevelMesh m, int left, int top, int w, int h)
    {
        int[] grid = new int[w * h];
        Array.Fill(grid, -1);
        foreach (SectorFloor f in m.Floors.BySector)
        {
            for (int t = 0; t < f.TriangleCount; t++)
            {
                double ax = f.Corner(t, 0).X / 65536.0, ay = f.Corner(t, 0).Y / 65536.0;
                double bx = f.Corner(t, 1).X / 65536.0, by = f.Corner(t, 1).Y / 65536.0;
                double cx = f.Corner(t, 2).X / 65536.0, cy = f.Corner(t, 2).Y / 65536.0;
                // Pixel centre: x = left + px + 0.5, y = top − py − 0.5.
                int px0 = Math.Max(0, (int)Math.Ceiling(Math.Min(ax, Math.Min(bx, cx)) - left - 0.5));
                int px1 = Math.Min(w - 1, (int)Math.Floor(Math.Max(ax, Math.Max(bx, cx)) - left - 0.5));
                int py0 = Math.Max(0, (int)Math.Ceiling(top - 0.5 - Math.Max(ay, Math.Max(by, cy))));
                int py1 = Math.Min(h - 1, (int)Math.Floor(top - 0.5 - Math.Min(ay, Math.Min(by, cy))));
                for (int py = py0; py <= py1; py++)
                {
                    double y = top - py - 0.5;
                    for (int px = px0; px <= px1; px++)
                    {
                        double x = left + px + 0.5;
                        if (!InTriangle(ax, ay, bx, by, cx, cy, x, y))
                            continue;
                        ref int g = ref grid[py * w + px];
                        g = g == -1 || g == f.Sector ? f.Sector : -2;
                    }
                }
            }
        }
        return grid;
    }

    private static bool InTriangle(double ax, double ay, double bx, double by, double cx, double cy, double x, double y)
    {
        double e1 = (bx - ax) * (y - ay) - (by - ay) * (x - ax);
        double e2 = (cx - bx) * (y - by) - (cy - by) * (x - bx);
        double e3 = (ax - cx) * (y - cy) - (ay - cy) * (x - cx);
        return (e1 <= 0 && e2 <= 0 && e3 <= 0) || (e1 >= 0 && e2 >= 0 && e3 >= 0);
    }

    /// <summary>True where every grid value within <paramref name="r"/> pixels (clipped to the grid) is the same and not −2.</summary>
    private static bool[] Uniform(int[] grid, int w, int h, int r)
    {
        int[] min = new int[grid.Length], max = new int[grid.Length];
        int[] min2 = new int[grid.Length], max2 = new int[grid.Length];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int lo = int.MaxValue, hi = int.MinValue;
                for (int k = Math.Max(0, x - r); k <= Math.Min(w - 1, x + r); k++)
                {
                    lo = Math.Min(lo, grid[y * w + k]);
                    hi = Math.Max(hi, grid[y * w + k]);
                }
                min[y * w + x] = lo;
                max[y * w + x] = hi;
            }
        }
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int lo = int.MaxValue, hi = int.MinValue;
                for (int k = Math.Max(0, y - r); k <= Math.Min(h - 1, y + r); k++)
                {
                    lo = Math.Min(lo, min[k * w + x]);
                    hi = Math.Max(hi, max[k * w + x]);
                }
                min2[y * w + x] = lo;
                max2[y * w + x] = hi;
            }
        }
        bool[] uniform = new bool[grid.Length];
        for (int i = 0; i < grid.Length; i++)
            uniform[i] = min2[i] == max2[i] && grid[i] != -2;
        return uniform;
    }

    /// <summary>Whether a section can be viewed side-on at whole-unit pixels: axis-aligned, on whole map units.</summary>
    private static bool IsSideOnCheckable(WallSection s) =>
        (s.V1.X == s.V2.X || s.V1.Y == s.V2.Y) && ((s.V1.X | s.V1.Y | s.V2.X | s.V2.Y | s.TextureOffset) & (Fixed.FRACUNIT - 1)) == 0
        && WallLength(s) > 2 * EdgeMargin;

    private static int WallLength(WallSection s) => (Math.Abs(s.V2.X - s.V1.X) + Math.Abs(s.V2.Y - s.V1.Y)) >> Fixed.FRACBITS;

    /// <summary>
    /// Views <paramref name="s"/> side-on from its front sector (orthographic,
    /// perpendicular, 1 unit per pixel, pixel centres on texel centres) and
    /// compares its drawn span, away from its edges and from the spans of the
    /// side's other sections, with the CPU texel lookup. Returns the pixels
    /// compared and whether the two tilings give different texels there.
    /// A masked middle (T3.1) is compared over its whole opening: within its
    /// span (the texture's rows, drawn once) its opaque texels must show and
    /// its clear ones the background (nothing else lies within the view's
    /// depth range, and the other side's quad faces away), and the opening's
    /// rows above and below the texture must show the background.
    /// </summary>
    private async Task<(int Pixels, bool TilingsDiffer)> CheckWall(LevelMesh m, WallSection s, WallTextureTiling tiling, string map)
    {
        bool masked = LevelMesh.IsMasked(s);
        (int sb, int st) = s.Span();
        // The rows compared: the span, or a masked middle's whole opening.
        (int cb, int ct) = masked
            ? (Math.Max(s.Bottom.Evaluate(s.FrontSector, s.BackSector), s.FrontSector.FloorHeight),
                Math.Min(s.Top.Evaluate(s.FrontSector, s.BackSector), s.FrontSector.CeilingHeight))
            : (sb, st);
        int bottom = cb >> Fixed.FRACBITS, top = ct >> Fixed.FRACBITS;
        if (((cb | ct | sb | st) & (Fixed.FRACUNIT - 1)) != 0 || top - bottom <= 2 * EdgeMargin)
            return (0, false);
        Vector2I size = ViewSize();
        int w = size.X, h = size.Y;
        int len = WallLength(s);
        int dirX = Math.Sign(s.V2.X - s.V1.X), dirY = Math.Sign(s.V2.Y - s.V1.Y);
        int nX = dirY, nY = -dirX; // towards the front sector (on the right)
        int dl = (int)Math.Floor(len / 2.0 - w / 2.0); // d of the left pixel edge
        int zt = (int)Math.Ceiling((bottom + top) / 2.0 + h / 2.0); // z of the top pixel edge
        double dc = dl + w / 2.0, zc = zt - h / 2.0;
        double v1x = s.V1.X / 65536.0, v1y = s.V1.Y / 65536.0;
        // Screen right = V1 → V2, screen up = +height, looking at the wall from 0.5 units in front.
        var basis = new Basis(new Vector3(dirX, 0, -dirY), new Vector3(0, 1, 0), new Vector3(nX, 0, -nY));
        Ortho(basis, new Vector3((float)(v1x + dirX * dc + nX * 0.5), (float)(v1y + dirY * dc + nY * 0.5), (float)zc), 0.1f, 1.5f);

        // The side's other drawn sections may overlap this one's span: leave those rows out.
        var others = new List<(int Bottom, int Top)>();
        foreach (WallSection o in m.Walls.Sections)
        {
            if (o != s && o.Line == s.Line && o.Side == s.Side && LevelMesh.IsDrawn(o) && !LevelMesh.IsMasked(o))
            {
                (int ob, int ot) = o.Span();
                if (ot > ob)
                    others.Add((ob, ot));
            }
        }

        string what = $"{map}: line {s.Line.Index} side {s.Side} {s.Kind} ({Textures.TextureDefs[s.Texture].Name}, {tiling} tiling)";
        byte[]? frame = await Capture(what);
        if (frame is null)
            return (0, false);
        IndexedImage tex = Composite(s.Texture);
        int textureTop = s.TextureTop.Evaluate(s.FrontSector, s.BackSector);
        int light = s.FrontSector.LightLevel;
        int contrast = LightTables.FakeContrast(s.V1.X, s.V1.Y, s.V2.X, s.V2.Y); // axis-aligned: every seg has it
        // The pieces along the side (T2.9: per seg, vanilla's seg offsets) as distances from V1, columns
        // interpolated between their ends as the GPU does; connector pairs stand across the floor.
        var spans = new List<(double From, double To, long ColumnFrom, long ColumnTo)>();
        IReadOnlyList<WallPiece> pieces = m.Pieces.Of(s.Line, s.Side);
        for (int i = 0; i < pieces.Count; i++)
        {
            WallPiece pc = pieces[i];
            bool connector = (i + 1 < pieces.Count && pieces[i + 1].A == pc.B && pieces[i + 1].B == pc.A)
                || (i > 0 && pieces[i - 1].A == pc.B && pieces[i - 1].B == pc.A);
            double from = dirX * (pc.A.X / 65536.0 - v1x) + dirY * (pc.A.Y / 65536.0 - v1y);
            double to = dirX * (pc.B.X / 65536.0 - v1x) + dirY * (pc.B.Y / 65536.0 - v1y);
            if (!connector && to > from)
                spans.Add((from, to, (long)s.TextureOffset + pc.ColumnA, (long)s.TextureOffset + pc.ColumnB));
        }
        int compared = 0, bad = 0;
        bool differ = false;
        string first = "";
        for (int py = 0; py < h; py++)
        {
            int zFixed = ((zt - py) << Fixed.FRACBITS) - Fixed.FRACUNIT / 2;
            if (zFixed < cb + (EdgeMargin << Fixed.FRACBITS) || zFixed > ct - (EdgeMargin << Fixed.FRACBITS))
                continue;
            // A masked middle: away from its span's edges; inside the span or clear (the opening around it).
            if (masked && sb < st && Math.Min(Math.Abs((long)zFixed - sb), Math.Abs((long)zFixed - st)) < EdgeMargin << Fixed.FRACBITS)
                continue;
            bool inSpan = zFixed > sb && zFixed < st;
            bool overlapped = false;
            foreach ((int ob, int ot) in others)
                overlapped |= zFixed > ob - (EdgeMargin << Fixed.FRACBITS) && zFixed < ot + (EdgeMargin << Fixed.FRACBITS);
            if (overlapped)
                continue;
            int row = (int)(((long)textureTop - zFixed) >> Fixed.FRACBITS);
            for (int px = 0; px < w; px++)
            {
                int d = dl + px; // pixel centre at d + 0.5
                if (d < EdgeMargin || d + 1 > len - EdgeMargin)
                    continue;
                // The piece under the pixel centre, away from its ends (where a seg's column can jump).
                double dPixel = d + 0.5, column = double.NaN;
                foreach ((double from, double to, long cf, long cto) in spans)
                {
                    if (dPixel >= from + EdgeMargin && dPixel <= to - EdgeMargin)
                        column = (cf + (cto - cf) * (dPixel - from) / (to - from)) / 65536.0;
                }
                // Not on one piece, or within 1/64 of a texel edge (a seg starting off a whole unit).
                if (double.IsNaN(column) || Math.Abs(column - Math.Round(column)) < 1.0 / 64)
                    continue;
                int col = (int)Math.Floor(column);
                WallTextureTiling otherTiling = tiling == WallTextureTiling.Vanilla ? WallTextureTiling.TextureSize : WallTextureTiling.Vanilla;
                int p = (py * w + px) * 4;
                (int R, int G, int B) got = (frame[p], frame[p + 1], frame[p + 2]);
                int tc, tr;
                if (masked)
                {
                    (int Column, int Row)? texel = inSpan ? TextureWrap.MaskedTexel(col, row, tex.Width, tex.Height, tiling) : null;
                    (int Column, int Row)? otherTexel = inSpan ? TextureWrap.MaskedTexel(col, row, tex.Width, tex.Height, otherTiling) : null;
                    if (texel is { } a && otherTexel is { } o && a != o
                        && (tex.IsOpaque(a.Column, a.Row) != tex.IsOpaque(o.Column, o.Row) || tex[a.Column, a.Row] != tex[o.Column, o.Row]))
                        differ = true;
                    if (texel is not { } t || !tex.IsOpaque(t.Column, t.Row))
                    {
                        // Clear: a hole in the texture, or the opening above or below it.
                        compared++;
                        _maskedClear++;
                        if (!NearBackground(got) && bad++ == 0)
                            first = $"d {d + 0.5}, z {zFixed / 65536.0} ({(texel is null ? "outside the texture" : $"clear texel {texel}")}): drew {got}, expected the background {_background}";
                        continue;
                    }
                    (tc, tr) = t;
                }
                else
                {
                    (tc, tr) = TextureWrap.WallTexel(col, row, tex.Width, tex.Height, tiling);
                    var other = TextureWrap.WallTexel(col, row, tex.Width, tex.Height, otherTiling);
                    differ |= other != (tc, tr) && tex[other.Column, other.Row] != tex[tc, tr];
                }
                int map0 = ExpectedColormap(m, true, light, contrast, v1x + dirX * (d + 0.5), v1y + dirY * (d + 0.5), 0.5);
                if (map0 < 0)
                    continue;
                (int R, int G, int B) expected = Shade(tex[tc, tr], map0);
                compared++;
                if (masked)
                    _maskedOpaque++;
                if (got != expected && bad++ == 0)
                    first = $"d {d + 0.5}, z {zFixed / 65536.0} (texel {tc},{tr}): drew {got}, expected {expected}";
            }
        }
        if (bad > 0)
            Fail($"{what}: {bad} of {compared} pixel(s) differ, first at {first}");
        _pixels += compared;
        return (compared, differ);
    }

    /// <summary>
    /// Raises <paramref name="sector"/>'s floor through the data texture: its
    /// floor, seen obliquely with only its own chunk shown, must move (the
    /// expected texels change) and match; every wall using its planes,
    /// side-on, must match with the new heights (the lower's rows follow).
    /// Then the floor goes back.
    /// </summary>
    private async Task MoveCheck(LevelMesh m, WallSection lower, Sector sector)
    {
        string map = m.Level.Name;
        int old = sector.FloorHeight;
        foreach (MeshInstance3D? chunk in _scene.Chunks)
        {
            if (chunk is not null)
                chunk.Visible = chunk == _scene.Chunks[sector.Index];
        }
        var before = await CheckFloorOblique(m, sector, old, $"{map}: sector {sector.Index}'s floor, oblique, at {old >> Fixed.FRACBITS}");
        int textureTopBefore = lower.TextureTop.Evaluate(lower.FrontSector, lower.BackSector);
        (int, int) spanBefore = lower.Span();

        sector.FloorHeight = old + (MoveUnits << Fixed.FRACBITS);
        m.UpdateSectors();
        string moved = $"{map} (sector {sector.Index} raised {MoveUnits})";
        CheckSectorData(m, moved);
        CheckChunks(m, moved);
        var after = await CheckFloorOblique(m, sector, old, $"{moved}: its floor, oblique, at {sector.FloorHeight >> Fixed.FRACBITS}");
        int changed = 0;
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i] >= 0 && after[i] >= 0 && before[i] != after[i])
                changed++;
        }
        if (changed == 0)
            Fail($"{moved}: the floor's expected pixels did not change, so the move isn't visible in the check");
        foreach (MeshInstance3D? chunk in _scene.Chunks)
        {
            if (chunk is not null)
                chunk.Visible = true;
        }

        if (lower.TextureTop.Evaluate(lower.FrontSector, lower.BackSector) == textureTopBefore || lower.Span() == spanBefore)
            Fail($"{moved}: line {lower.Line.Index}'s lower didn't change with the floor");
        int walls = 0, pixels = 0;
        bool lowerChecked = false;
        foreach (WallSection s in m.Walls.Sections)
        {
            if ((s.FrontSector == sector || s.BackSector == sector) && LevelMesh.IsDrawn(s) && IsSideOnCheckable(s))
            {
                int n = (await CheckWall(m, s, WallTextureTiling.Vanilla, moved)).Pixels;
                pixels += n;
                walls += n > 0 ? 1 : 0;
                lowerChecked |= s == lower && n > 0;
            }
        }
        if (!lowerChecked)
            Fail($"{moved}: line {lower.Line.Index}'s lower was not compared after the move");
        GD.Print($"Level check: {moved}: {changed} floor pixels expected to change with the move, {walls} walls on its planes side-on ({pixels} drawn pixels compared)");

        sector.FloorHeight = old;
        m.UpdateSectors();
        CheckSectorData(m, $"{map} (sector {sector.Index} back)");
    }

    /// <summary>
    /// Views the sector's floor from the south at 45° (orthographic, framed on
    /// its height <paramref name="frameHeight"/> so a move shifts the image).
    /// Compares pixels whose ray hits the floor plane well inside the sector,
    /// away from flat texel edges, and passes no line of the sector below its
    /// ceiling (its walls could cover the floor there). Returns the expected
    /// colour per pixel (packed RGB, −1 where not compared).
    /// </summary>
    private async Task<int[]> CheckFloorOblique(LevelMesh m, Sector sector, int frameHeight, string what)
    {
        Vector2I size = ViewSize();
        int w = size.X, h = size.Y;
        int[] expectedColours = new int[w * h];
        Array.Fill(expectedColours, -1);
        SectorFloor? floor = null;
        foreach (SectorFloor f in m.Floors.BySector)
        {
            if (f.Sector == sector.Index)
                floor = f;
        }
        if (floor is null || floor.TriangleCount == 0)
        {
            Fail($"{what}: no floor");
            return expectedColours;
        }
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (PolygonVertex v in floor.Vertices)
        {
            minX = Math.Min(minX, v.X / 65536.0);
            maxX = Math.Max(maxX, v.X / 65536.0);
            minY = Math.Min(minY, v.Y / 65536.0);
            maxY = Math.Max(maxY, v.Y / 65536.0);
        }
        double s45 = Math.Sqrt(0.5);
        const double distance = 1024;
        double floorZ = sector.FloorHeight / 65536.0, ceilingZ = sector.CeilingHeight / 65536.0;
        // Camera centre (map x, y, height): looking north and down at 45° at the box's centre at the frame height;
        // x chosen so that pixel centres fall on x = n + 0.5.
        double cx = Math.Round((minX + maxX) / 2 - w / 2.0) + w / 2.0;
        double cy = (minY + maxY) / 2 - s45 * distance, cz = frameHeight / 65536.0 + s45 * distance;
        var basis = new Basis(new Vector3(1, 0, 0), new Vector3(0, (float)s45, -(float)s45), new Vector3(0, (float)s45, (float)s45));
        Ortho(basis, new Vector3((float)cx, (float)cy, (float)cz), 1, (float)(3 * distance));
        byte[]? frame = await Capture(what);
        if (frame is null)
            return expectedColours;

        int compared = 0, bad = 0;
        string first = "";
        for (int py = 0; py < h; py++)
        {
            double v = h / 2.0 - py - 0.5;
            double t = (cz + v * s45 - floorZ) / s45; // along the view direction to the floor plane
            double hy = cy + v * s45 + t * s45;
            if (Math.Abs(hy - Math.Round(hy)) < 0.05)
                continue; // too close to a flat texel edge
            for (int px = 0; px < w; px++)
            {
                double hx = cx + px + 0.5 - w / 2.0;
                if (!InsideFloor(floor, hx, hy, 2) || Occluded(sector, hx, hy, ceilingZ - floorZ + 2))
                    continue;
                int map0 = ExpectedColormap(m, false, sector.LightLevel, 0, hx, hy, 0);
                if (map0 < 0)
                    continue;
                (int col, int row) = TextureWrap.FlatTexel((int)Math.Floor(hx * 65536), (int)Math.Floor(hy * 65536));
                (int R, int G, int B) expected = Shade(FlatImage(sector.FloorPic)[col, row], map0);
                expectedColours[py * w + px] = (expected.R << 16) | (expected.G << 8) | expected.B;
                int p = (py * w + px) * 4;
                (int R, int G, int B) got = (frame[p], frame[p + 1], frame[p + 2]);
                compared++;
                if (got != expected && bad++ == 0)
                    first = $"map ({hx}, {hy}): drew {got}, expected {expected}";
            }
        }
        if (bad > 0)
            Fail($"{what}: {bad} of {compared} pixel(s) differ, first at {first}");
        if (compared < 100)
            Fail($"{what}: only {compared} pixels could be compared");
        _pixels += compared;
        return expectedColours;
    }

    /// <summary>Whether (x, y) and the four points <paramref name="margin"/> units diagonally from it all lie in the floor's triangles.</summary>
    private static bool InsideFloor(SectorFloor floor, double x, double y, double margin)
    {
        bool In(double px, double py)
        {
            for (int t = 0; t < floor.TriangleCount; t++)
            {
                if (InTriangle(floor.Corner(t, 0).X / 65536.0, floor.Corner(t, 0).Y / 65536.0, floor.Corner(t, 1).X / 65536.0,
                    floor.Corner(t, 1).Y / 65536.0, floor.Corner(t, 2).X / 65536.0, floor.Corner(t, 2).Y / 65536.0, px, py))
                    return true;
            }
            return false;
        }
        return In(x, y) && In(x - margin, y - margin) && In(x + margin, y - margin) && In(x - margin, y + margin) && In(x + margin, y + margin);
    }

    /// <summary>Whether the segment from (x, y) back south by <paramref name="reach"/> units comes within 1 unit of a line of the sector.</summary>
    private static bool Occluded(Sector sector, double x, double y, double reach)
    {
        foreach (Line line in sector.Lines)
        {
            double x1 = line.V1.X / 65536.0, y1 = line.V1.Y / 65536.0, x2 = line.V2.X / 65536.0, y2 = line.V2.Y / 65536.0;
            if (SegmentDistance(x, y - reach, x, y, x1, y1, x2, y2) < 1)
                return true;
        }
        return false;
    }

    private static double SegmentDistance(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
    {
        double Cross(double ox, double oy, double px, double py, double qx, double qy) => (px - ox) * (qy - oy) - (py - oy) * (qx - ox);
        double d1 = Cross(cx, cy, dx, dy, ax, ay), d2 = Cross(cx, cy, dx, dy, bx, by);
        double d3 = Cross(ax, ay, bx, by, cx, cy), d4 = Cross(ax, ay, bx, by, dx, dy);
        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            return 0;
        return Math.Min(Math.Min(PointSegment(ax, ay, cx, cy, dx, dy), PointSegment(bx, by, cx, cy, dx, dy)),
            Math.Min(PointSegment(cx, cy, ax, ay, bx, by), PointSegment(dx, dy, ax, ay, bx, by)));
    }

    private static double PointSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        double vx = bx - ax, vy = by - ay;
        double l2 = vx * vx + vy * vy;
        double t = l2 == 0 ? 0 : Math.Clamp(((px - ax) * vx + (py - ay) * vy) / l2, 0, 1);
        double ex = ax + t * vx - px, ey = ay + t * vy - py;
        return Math.Sqrt(ex * ex + ey * ey);
    }

    private static (int Lowest, int Highest) HeightRange(Level level)
    {
        int lo = int.MaxValue, hi = int.MinValue;
        foreach (Sector s in level.Sectors)
        {
            lo = Math.Min(lo, Math.Min(s.FloorHeight, s.CeilingHeight) >> Fixed.FRACBITS);
            hi = Math.Max(hi, Math.Max(s.FloorHeight, s.CeilingHeight) >> Fixed.FRACBITS);
        }
        return (lo, hi);
    }

    // ---- CPU references ----

    /// <summary>Distances (map units) this close to a light table step are not compared (the shader's distance is float).</summary>
    private const double LightMargin = 1.0 / 64;

    /// <summary>
    /// The colormap the level shader picks (<see cref="LightTables.WallColormap"/>
    /// or <see cref="LightTables.PlaneColormap"/>, by the mesh's light mode,
    /// origin, reference distance, extralight and override) for a wall or
    /// floor point at map (<paramref name="x"/>, <paramref name="y"/>) and
    /// camera depth <paramref name="depth"/> (map units); −1 when the distance
    /// is within <see cref="LightMargin"/> of a table step.
    /// </summary>
    private int ExpectedColormap(LevelMesh m, bool wall, int lightlevel, int contrast, double x, double y, double depth)
    {
        if (m.ColormapOverride >= 0)
            return m.ColormapOverride;
        double d = m.LightMode switch
        {
            LightDiminishing.None => m.LightReference,
            LightDiminishing.Camera => depth,
            _ => Math.Sqrt((x - m.LightOrigin.X) * (x - m.LightOrigin.X) + (y - m.LightOrigin.Y) * (y - m.LightOrigin.Y)),
        };
        int At(double units)
        {
            int dist = (int)(Math.Clamp(units, 0, LightTables.MaxDistanceUnits) * 65536.0); // the shader's conversion
            return wall ? m.Lights.WallColormap(lightlevel, m.ExtraLight, contrast, dist) : m.Lights.PlaneColormap(lightlevel, m.ExtraLight, dist);
        }
        int colormap = At(d);
        if (m.LightMode != LightDiminishing.None && (At(d - LightMargin) != colormap || At(d + LightMargin) != colormap))
        {
            _lightSkipped++;
            return -1;
        }
        _lightLevels.Add(lightlevel >> LightTables.LIGHTSEGSHIFT);
        if (wall)
            _contrasts.Add(contrast);
        _colormaps.Add(colormap);
        return colormap;
    }

    /// <summary>The colour the level shader draws for palette index <paramref name="index"/> through COLORMAP row <paramref name="colormap"/> (palette 0).</summary>
    private (int R, int G, int B) Shade(byte index, int colormap)
    {
        byte mapped = Colormap.GetMap(colormap)[index];
        (byte r, byte g, byte b) = Playpal.GetColor(0, mapped);
        return (r, g, b);
    }

    private IndexedImage Composite(int texnum)
    {
        if (!_composites.TryGetValue(texnum, out IndexedImage? image))
            _composites[texnum] = image = Textures.R_GenerateComposite(texnum, TextureCompositeMode.Vanilla);
        return image;
    }

    private IndexedImage FlatImage(string name)
    {
        if (!_flats.TryGetValue(name, out IndexedImage? image))
            _flats[name] = image = Flat.Load(_scene.Wad!, name);
        return image;
    }

    /// <summary>A background colour more than a few steps from every colour of palette 0, so a missing floor can't pass as drawn.</summary>
    private static (int R, int G, int B) UnusedColor(Playpal playpal)
    {
        const int distance = 3 * BackgroundTolerance;
        for (int g = 0; g < 256; g += 3)
        {
            for (int b = 255; b >= 0; b -= 5)
            {
                foreach (int r in new[] { 255, 0, 128 })
                {
                    bool free = true;
                    for (int i = 0; i < 256 && free; i++)
                    {
                        (byte pr, byte pg, byte pb) = playpal.GetColor(0, i);
                        free = Math.Abs(pr - r) > distance || Math.Abs(pg - g) > distance || Math.Abs(pb - b) > distance;
                    }
                    if (free)
                        return (r, g, b);
                }
            }
        }
        throw new InvalidOperationException("No background colour is free of the palette.");
    }

    /// <summary>Whether a drawn pixel shows the background (within <see cref="BackgroundTolerance"/>).</summary>
    private bool NearBackground((int R, int G, int B) got) =>
        Math.Abs(got.R - _background.R) <= BackgroundTolerance && Math.Abs(got.G - _background.G) <= BackgroundTolerance
        && Math.Abs(got.B - _background.B) <= BackgroundTolerance;

    private void Fail(string message)
    {
        _failures++;
        if (_logged++ < 50)
            GD.PrintErr($"Level check: {message}");
    }

    private SignalAwaiter NextFrame() => CanCapture
        ? ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw)
        : ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
}
