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
/// renderer; the image is WAD data, keep it out of the repo).
/// </para>
/// </summary>
public partial class LevelScene : Node3D
{
    private Camera3D _camera = null!;
    private Label _message = null!;

    /// <summary>The loaded level's mesh, or null when loading failed.</summary>
    public LevelMesh? Mesh { get; private set; }

    public override void _Ready()
    {
        _camera = new Camera3D { Current = true };
        AddChild(_camera);
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = Colors.Black, // SPEC §7.2: the void is black
                TonemapMode = Godot.Environment.ToneMapper.Linear,
                AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
            },
        });
        var overlay = new CanvasLayer();
        _message = new Label { Position = new Vector2(8, 8) };
        overlay.AddChild(_message);
        AddChild(overlay);

        try
        {
            Load();
        }
        catch (Exception e) when (e is WadFormatException or ModifiedGameException or IOException or UnauthorizedAccessException
            or KeyNotFoundException or ArgumentException)
        {
            Fail(e.Message);
            return;
        }
        if (WadLocator.GetUserArg("--level-screenshot") is string path)
            _ = ScreenshotAsync(path);
    }

    private void Load()
    {
        IwadSearchResult found = WadLocator.Find(out _);
        if (found.Warning is not null)
            GD.PushWarning($"Level: {found.Warning}");
        if (found.Path is null)
            throw new IOException(found.Error ?? "No IWAD found (pass -- -iwad PATH or set ISODOOM_IWAD).");

        var clock = Stopwatch.StartNew();
        var wad = WadArchive.Open(found.Path, [.. found.Pwads]);
        IwadInfo info = IwadIdentification.D_IdentifyVersion(wad);
        ModifiedGame.D_CheckModifiedGame(wad, info);
        string? map = WadLocator.GetUserArg("--level");
        if (map is null || map.StartsWith('-'))
            map = wad.W_CheckNumForName("E1M1") >= 0 ? "E1M1" : "MAP01";
        map = map.ToUpperInvariant();
        if (wad.W_CheckNumForName(map) < 0)
            throw new WadFormatException($"{found.Path} has no map {map}");

        Level level = Level.Load(wad, map);
        Textures textures = Textures.R_InitTextures(wad);
        Mesh = LevelMesh.Build(wad, level, textures, Playpal.Load(wad), Colormap.Load(wad));
        if (WadLocator.GetUserArg("--level-tiling") is string tiling)
            Mesh.SetWallTiling(tiling == "size" ? WallTextureTiling.TextureSize : WallTextureTiling.Vanilla);

        for (int s = 0; s < Mesh.SectorMeshes.Length; s++)
        {
            if (Mesh.SectorMeshes[s] is ArrayMesh chunk)
                AddChild(new MeshInstance3D { Mesh = chunk, Name = $"Sector{s}", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        clock.Stop();

        if (WadLocator.GetUserArg("--level-sector-floor") is string moves)
            MoveFloors(level, moves);

        FrameCamera();
        GetViewport().SizeChanged += FrameCamera;
        string text = $"{map}: {level.Sectors.Length} sectors, {Mesh.FloorTriangleCount} floor triangles, {Mesh.WallQuads} wall quads, "
            + $"{Mesh.SlotNames.Count} textures in a {Mesh.Atlas.Image.Width}x{Mesh.Atlas.Image.Height} atlas, {Mesh.Walls.Missing.Count} missing; "
            + $"built in {clock.ElapsedMilliseconds} ms";
        GD.Print($"Level: {found.Path}: {text}");
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

    private void FrameCamera()
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
        if (WadLocator.HasUserArg("--level-screenshot"))
            GetTree().Quit(1);
    }
}
