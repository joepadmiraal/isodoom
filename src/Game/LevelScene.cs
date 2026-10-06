using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Map;
using IsoDoom.Render;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

/// <summary>
/// The level scene (T2.5, milestone M2): loads one map of the IWAD and shows
/// it as a textured mesh (<see cref="LevelMesh"/>) from a fixed overview
/// camera (<see cref="LevelCamera"/>). Opened by <see cref="Main"/> with the
/// user argument <c>--level MAP</c> (<c>godot -- --level E1M1</c>; without a
/// map name the first of <c>E1M1</c>/<c>MAP01</c>).
/// <para>
/// More user arguments (debugging and checks):
/// <c>--level-view=iso|top</c> (camera angle, default iso);
/// <c>--level-focus=SECTOR</c> (frame one sector and its neighbours instead of the map);
/// <c>--level-tiling=vanilla|size</c> (<see cref="WallTextureTiling"/>);
/// <c>--level-sector-floor=SECTOR:HEIGHT[,SECTOR:HEIGHT…]</c> (set sector floors,
/// in map units, through the data texture after loading);
/// <c>--level-screenshot=FILE.png</c> (save a capture and quit; needs a real
/// renderer; the image is WAD data, keep it out of the repo);
/// <c>--level-check</c> (load every map and check the meshes, data textures
/// and, with a real renderer, drawn pixels: <see cref="LevelCheck"/>).
/// </para>
/// </summary>
public partial class LevelScene : Node3D
{
    private Camera3D _camera = null!;
    private Label _message = null!;
    private readonly List<MeshInstance3D> _chunks = new();

    /// <summary>The loaded level's mesh, or null when no map is loaded.</summary>
    public LevelMesh? Mesh { get; private set; }

    /// <summary>The loaded IWAD (plus PWADs), its texture table, palettes and colormaps; null until <see cref="OpenWad"/> succeeds.</summary>
    public WadArchive? Wad { get; private set; }
    public Textures? Textures { get; private set; }
    public Playpal? Playpal { get; private set; }
    public Colormap? Colormap { get; private set; }

    /// <summary>The maps of the WAD (<c>ExMy</c>/<c>MAPxx</c> headers followed by <c>THINGS</c>), in lump order, each once.</summary>
    public IReadOnlyList<string> MapNames { get; private set; } = Array.Empty<string>();

    /// <summary>The scene's camera (the overview camera; checks move it).</summary>
    public Camera3D Camera => _camera;

    /// <summary>The scene's environment (black background; checks change it).</summary>
    public Godot.Environment Environment { get; private set; } = null!;

    /// <summary>The overlay with the status line (checks hide it so it doesn't cover pixels).</summary>
    public CanvasLayer Overlay { get; private set; } = null!;

    /// <summary>The node of each sector's chunk (null where <see cref="LevelMesh.SectorMeshes"/> has none).</summary>
    public MeshInstance3D?[] Chunks { get; private set; } = Array.Empty<MeshInstance3D?>();

    private static bool IsCheckRun => WadLocator.HasUserArg("--level-check");

    public override void _Ready()
    {
        _camera = new Camera3D { Current = true };
        AddChild(_camera);
        Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = Colors.Black, // SPEC §7.2: the void is black
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
        };
        AddChild(new WorldEnvironment { Environment = Environment });
        Overlay = new CanvasLayer();
        _message = new Label { Position = new Vector2(8, 8) };
        Overlay.AddChild(_message);
        AddChild(Overlay);

        try
        {
            OpenWad();
            if (IsCheckRun)
            {
                AddChild(new LevelCheck(this)); // loads every map itself
                return;
            }
            string? map = WadLocator.GetUserArg("--level");
            if (map is null || map.StartsWith('-'))
                map = DefaultMap();
            LoadMap(map);
            if (WadLocator.GetUserArg("--level-sector-floor") is string moves)
                MoveFloors(Mesh!.Level, moves);
        }
        catch (Exception e) when (e is WadFormatException or ModifiedGameException or IOException or UnauthorizedAccessException
            or KeyNotFoundException or ArgumentException)
        {
            Fail(e.Message);
            return;
        }
        GetViewport().SizeChanged += FrameCamera;
        if (WadLocator.GetUserArg("--level-screenshot") is string path)
            _ = ScreenshotAsync(path);
    }

    /// <summary>Opens the IWAD <see cref="WadLocator"/> finds and reads its texture table, palettes and map list.</summary>
    private void OpenWad()
    {
        IwadSearchResult found = WadLocator.Find(out _);
        if (found.Warning is not null)
            GD.PushWarning($"Level: {found.Warning}");
        if (found.Path is null)
            throw new IOException(found.Error ?? "No IWAD found (pass -- -iwad PATH or set ISODOOM_IWAD).");

        var wad = WadArchive.Open(found.Path, [.. found.Pwads]);
        IwadInfo info = IwadIdentification.D_IdentifyVersion(wad);
        ModifiedGame.D_CheckModifiedGame(wad, info);
        Textures = Textures.R_InitTextures(wad);
        Playpal = Playpal.Load(wad);
        Colormap = Colormap.Load(wad);
        MapNames = FindMaps(wad);
        Wad = wad;
        GD.Print($"Level: {found.Path}: {info}, {MapNames.Count} maps");
    }

    /// <summary>The first of <c>E1M1</c>/<c>MAP01</c> the WAD has (else <c>MAP01</c>, which then fails to load).</summary>
    public string DefaultMap() => Wad!.W_CheckNumForName("E1M1") >= 0 ? "E1M1" : "MAP01";

    private static List<string> FindMaps(WadArchive wad)
    {
        var maps = new List<string>();
        for (int i = 0; i + 1 < wad.NumLumps; i++)
        {
            string name = wad.Lumps[i].Name;
            if (LumpDirectory.IsMapName(name) && wad.Lumps[i + 1].Name == "THINGS" && !maps.Contains(name))
                maps.Add(name);
        }
        return maps;
    }

    /// <summary>
    /// Shows map <paramref name="map"/> of the open WAD: frees the previous
    /// level's chunks, builds the new <see cref="LevelMesh"/> and frames the
    /// overview camera. Throws <see cref="WadFormatException"/> (or
    /// <see cref="KeyNotFoundException"/> for an unknown flat) when the map
    /// can't be built.
    /// </summary>
    public void LoadMap(string map)
    {
        WadArchive wad = Wad ?? throw new InvalidOperationException("No WAD open.");
        map = map.ToUpperInvariant();
        if (wad.W_CheckNumForName(map) < 0)
            throw new WadFormatException($"the WAD has no map {map}");

        foreach (MeshInstance3D chunk in _chunks)
            chunk.QueueFree();
        _chunks.Clear();
        Chunks = Array.Empty<MeshInstance3D?>();
        Mesh = null;

        var clock = Stopwatch.StartNew();
        Level level = Level.Load(wad, map);
        LevelMesh mesh = LevelMesh.Build(wad, level, Textures!, Playpal!, Colormap!);
        if (WadLocator.GetUserArg("--level-tiling") is string tiling)
            mesh.SetWallTiling(tiling == "size" ? WallTextureTiling.TextureSize : WallTextureTiling.Vanilla);

        Chunks = new MeshInstance3D?[mesh.SectorMeshes.Length];
        for (int s = 0; s < mesh.SectorMeshes.Length; s++)
        {
            if (mesh.SectorMeshes[s] is not ArrayMesh chunkMesh)
                continue;
            var node = new MeshInstance3D { Mesh = chunkMesh, Name = $"Sector{s}", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(node);
            _chunks.Add(node);
            Chunks[s] = node;
        }
        clock.Stop();
        Mesh = mesh;

        FrameCamera();
        string text = $"{map}: {level.Sectors.Length} sectors, {mesh.FloorTriangleCount} floor triangles, {mesh.WallQuads} wall quads, "
            + $"{mesh.SlotNames.Count} textures in a {mesh.Atlas.Image.Width}x{mesh.Atlas.Image.Height} atlas, {mesh.Walls.Missing.Count} missing; "
            + $"built in {clock.ElapsedMilliseconds} ms";
        GD.Print($"Level: {text}");
        _message.Text = text;
    }

    private void MoveFloors(Level level, string moves)
    {
        foreach (string move in moves.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = move.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int s) || !int.TryParse(parts[1], out int height)
                || s < 0 || s >= level.Sectors.Length)
                throw new ArgumentException($"--level-sector-floor: bad entry \"{move}\" (SECTOR:HEIGHT)");
            level.Sectors[s].FloorHeight = height << Fixed.FRACBITS;
            GD.Print($"Level: sector {s} floor set to {height}");
        }
        Mesh!.UpdateSectors();
    }

    /// <summary>Frames the overview camera on the level (or the <c>--level-focus</c> sector).</summary>
    public void FrameCamera()
    {
        if (Mesh is null)
            return;
        Aabb bounds = Mesh.Bounds;
        if (WadLocator.GetUserArg("--level-focus") is string focus && int.TryParse(focus, out int s) && s >= 0 && s < Mesh.Level.Sectors.Length)
            bounds = SectorBounds(Mesh.Level.Sectors[s]);
        Vector2 size = GetViewport().GetVisibleRect().Size;
        LevelCamera.FrameOverview(_camera, bounds, size.Y > 0 ? size.X / size.Y : 1.6f, WadLocator.GetUserArg("--level-view") == "top");
    }

    /// <summary>A sector and the sectors across its lines, at their current heights, in Godot space.</summary>
    private static Aabb SectorBounds(Sector sector)
    {
        Aabb? box = null;
        foreach (Line line in sector.Lines)
        {
            foreach (Sector? s in new[] { line.FrontSector, line.BackSector })
            {
                if (s is null)
                    continue;
                foreach (Vertex v in new[] { line.V1, line.V2 })
                {
                    var b = new Aabb(LevelMesh.ToGodot(v.X, v.Y, s.FloorHeight / 65536f), new Vector3(0, (s.CeilingHeight - s.FloorHeight) / 65536f / LevelMesh.MapUnitsPerMetre, 0));
                    box = box is Aabb a ? a.Merge(b) : b;
                }
            }
        }
        return box ?? new Aabb();
    }

    private async Task ScreenshotAsync(string path)
    {
        if (DisplayServer.GetName() == "headless")
        {
            GD.PrintErr("Level: --level-screenshot needs a real renderer (run without --headless)");
            GetTree().Quit(1);
            return;
        }
        for (int i = 0; i < 4; i++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Error err = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print(err == Error.Ok ? $"Level: saved {path}" : $"Level: could not save {path}: {err}");
        GetTree().Quit(err == Error.Ok ? 0 : 1);
    }

    private void Fail(string message)
    {
        GD.PrintErr($"Level: {message}");
        _message.Text = message;
        if (IsCheckRun)
            GD.PrintErr("Level check: FAILED (no WAD loaded)");
        if (WadLocator.HasUserArg("--level-screenshot") || IsCheckRun)
            GetTree().Quit(1);
    }
}
