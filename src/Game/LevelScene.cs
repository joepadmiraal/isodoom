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
/// <c>--level-camera=overview|fly</c> (start with the overview or the free-fly
/// camera, T2.7; default overview);
/// <c>--level-script=COMMANDS</c> (feed scripted input: <see cref="LevelScript"/>);
/// <c>--level-light=player|none|camera</c> (<see cref="LightDiminishing"/>, T2.8;
/// default player); <c>--level-light-origin=X,Y</c> (the player position light
/// diminishing uses, in map units; by default the free-fly camera's pivot, or
/// player 1's start under the overview camera);
/// <c>--level-background=RRGGBB</c> (the void's colour, default black; a
/// colour in no palette makes cracks easy to find, T2.9);
/// <c>--level-check</c> (load every map and check the meshes, data textures
/// and, with a real renderer, drawn pixels: <see cref="LevelCheck"/>).
/// </para>
/// <para>
/// Keys (T2.7; not under <c>--level-check</c>): Tab switches between the
/// overview and the free-fly camera (<see cref="FreeFlyCamera"/>, which has
/// its own keys), Home puts the free-fly camera at player 1's start, Page
/// Down / Page Up load the next / previous map of the WAD, L cycles the light
/// diminishing mode, F1 shows the controls, F3 hides the overlay.
/// </para>
/// </summary>
public partial class LevelScene : Node3D
{
    private Camera3D _camera = null!;
    private Label _message = null!;
    private string _status = "";
    private Control _crosshair = null!;
    private bool _showHelp;
    private readonly List<MeshInstance3D> _chunks = new();
    private LightDiminishing _lightMode = LightDiminishing.Player;

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

    /// <summary>The free-fly debug camera (T2.7); null under <c>--level-check</c>, which must keep the overview camera current.</summary>
    public FreeFlyCamera? FreeFly { get; private set; }

    /// <summary>p_local.h <c>VIEWHEIGHT</c>: eye height above the floor, map units (where Home puts the free-fly camera).</summary>
    public const int VIEWHEIGHT = 41;

    /// <summary>Controls shown by F1.</summary>
    public const string ControlsHelp =
        "Tab overview/free-fly   Home player 1 start   PgDn/PgUp next/previous map   L light mode   F1 controls   F3 overlay\n"
        + "Free-fly: click captures the mouse (Esc releases), mouse look, W/A/S/D move, E/Space up, Q/C down,\n"
        + "Shift x4, Alt x1/4, wheel speed, Ctrl+wheel FOV / ortho size, O perspective/orthographic";

    /// <summary>The scene's environment (black background; checks change it).</summary>
    public Godot.Environment Environment { get; private set; } = null!;

    /// <summary>The overlay with the status line (checks hide it so it doesn't cover pixels).</summary>
    public CanvasLayer Overlay { get; private set; } = null!;

    /// <summary>The node of each sector's chunk (null where <see cref="LevelMesh.SectorMeshes"/> has none).</summary>
    public MeshInstance3D?[] Chunks { get; private set; } = Array.Empty<MeshInstance3D?>();

    private static bool IsCheckRun => WadLocator.HasUserArg("--level-check");

    /// <summary>
    /// How long opening the WAD took (read, identify, texture table, palettes;
    /// once per WAD), and how long the last <see cref="LoadMap"/> took (map
    /// lumps, wall sections, floors, atlas, meshes, uploads and scene nodes),
    /// in milliseconds. SPEC §9: under 1 s per map (T2.9; <c>--level-check</c>
    /// checks every map).
    /// </summary>
    public double OpenWadMilliseconds { get; private set; }

    /// <inheritdoc cref="OpenWadMilliseconds"/>
    public double LastLoadMilliseconds { get; private set; }

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
        if (WadLocator.GetUserArg("--level-background") is string bg && Color.HtmlIsValid(bg))
            Environment.BackgroundColor = Color.FromHtml(bg); // T2.9: a colour in no palette shows cracks
        AddChild(new WorldEnvironment { Environment = Environment });
        Overlay = new CanvasLayer();
        _message = new Label
        {
            Position = new Vector2(8, 8),
            LabelSettings = new LabelSettings { FontSize = 14, OutlineSize = 4, OutlineColor = Colors.Black },
        };
        Overlay.AddChild(_message);
        _crosshair = Crosshair();
        Overlay.AddChild(_crosshair);
        AddChild(Overlay);

        try
        {
            OpenWad();
            if (IsCheckRun)
            {
                AddChild(new LevelCheck(this)); // loads every map itself; no free-fly camera, no keys
                return;
            }
            if (WadLocator.GetUserArg("--level-light") is string light)
                _lightMode = light switch
                {
                    "none" => LightDiminishing.None,
                    "camera" => LightDiminishing.Camera,
                    "player" => LightDiminishing.Player,
                    _ => throw new ArgumentException($"--level-light: unknown mode \"{light}\" (player, none or camera)"),
                };
            FreeFly = new FreeFlyCamera { Name = "FreeFly" };
            AddChild(FreeFly);
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
        if (WadLocator.GetUserArg("--level-camera") == "fly")
            UseFreeFly(true);
        if (WadLocator.GetUserArg("--level-script") is string script)
            AddChild(new LevelScript(this, script));
        else if (WadLocator.GetUserArg("--level-screenshot") is string path)
            _ = ScreenshotAsync(path);
    }

    /// <summary>Makes the free-fly camera (<paramref name="fly"/>) or the overview camera current.</summary>
    public void UseFreeFly(bool fly)
    {
        if (FreeFly is null)
            return;
        if (fly)
            FreeFly.MakeCurrent();
        else
        {
            _camera.MakeCurrent();
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }

    /// <summary>Whether the free-fly camera is the current one.</summary>
    public bool FreeFlyActive => FreeFly is { } f && f.Current;

    /// <summary>
    /// Puts the free-fly camera at player <paramref name="player"/>'s start of
    /// the loaded map, at <see cref="VIEWHEIGHT"/> above its floor, facing
    /// its angle (or at the map's centre when the map has no such start).
    /// </summary>
    public void JumpToStart(int player = 0)
    {
        if (FreeFly is null || Mesh is null)
            return;
        Level level = Mesh.Level;
        if (level.PlayerStart(player) is MapThing start)
            PlaceFreeFly(start.X, start.Y, start.Angle);
        else
            FreeFly.Place(Mesh.Bounds.GetCenter(), 0, -30);
    }

    /// <summary>
    /// Puts the free-fly camera at map point (<paramref name="x"/>, <paramref name="y"/>)
    /// facing vanilla angle <paramref name="angle"/> (degrees) with no pitch, at height
    /// <paramref name="z"/> (map units), or by default at <see cref="VIEWHEIGHT"/>
    /// above the floor there, as vanilla's view of a player standing still.
    /// </summary>
    public void PlaceFreeFly(int x, int y, float angle, int? z = null)
    {
        if (FreeFly is null || Mesh is null)
            return;
        Sector sector = Mesh.Level.R_PointInSubsector(x << Fixed.FRACBITS, y << Fixed.FRACBITS).Sector;
        float height = z ?? (sector.FloorHeight >> Fixed.FRACBITS) + VIEWHEIGHT;
        FreeFly.Place(LevelMesh.ToGodot(x << Fixed.FRACBITS, y << Fixed.FRACBITS, height), FreeFlyCamera.YawForMapAngle(angle), 0);
    }

    /// <summary>Loads the map <paramref name="step"/> places after the current one in <see cref="MapNames"/> (wrapping).</summary>
    public void SwitchMap(int step)
    {
        if (MapNames.Count == 0)
            return;
        int i = Mesh is null ? -1 : IndexOfMap(Mesh.Level.Name);
        string next = MapNames[Mathf.PosMod(i + step, MapNames.Count)];
        try
        {
            LoadMap(next);
        }
        catch (Exception e) when (e is WadFormatException or KeyNotFoundException)
        {
            GD.PrintErr($"Level: {next}: {e.Message}");
            _status = $"{next}: {e.Message}";
        }
    }

    private int IndexOfMap(string map)
    {
        for (int i = 0; i < MapNames.Count; i++)
        {
            if (MapNames[i] == map)
                return i;
        }
        return -1;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (IsCheckRun || e is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        switch (key.PhysicalKeycode)
        {
            case Key.Tab:
                UseFreeFly(!FreeFlyActive);
                break;
            case Key.Home:
                JumpToStart();
                UseFreeFly(true);
                break;
            case Key.Pagedown:
                SwitchMap(1);
                break;
            case Key.Pageup:
                SwitchMap(-1);
                break;
            case Key.L:
                _lightMode = (LightDiminishing)(((int)_lightMode + 1) % 3);
                Mesh?.SetLightDiminishing(_lightMode);
                break;
            case Key.F1:
                _showHelp = !_showHelp;
                break;
            case Key.F3:
                Overlay.Visible = !Overlay.Visible;
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (!IsCheckRun && Mesh is not null)
            Mesh.SetLightOrigin(LightOrigin());
        _crosshair.Visible = FreeFlyActive;
        if (!IsCheckRun && Overlay.Visible)
            _message.Text = OverlayText();
    }

    /// <summary>A small cross at the screen centre (the free-fly camera's view direction; straight down it marks the floor the overlay names).</summary>
    private static Control Crosshair()
    {
        var root = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.Center);
        foreach (Rect2 r in new[] { new Rect2(-6, -1, 5, 2), new Rect2(1, -1, 5, 2), new Rect2(-1, -6, 2, 5), new Rect2(-1, 1, 2, 5) })
            root.AddChild(new ColorRect { Position = r.Position, Size = r.Size, Color = Colors.White, MouseFilter = Control.MouseFilterEnum.Ignore });
        return root;
    }

    /// <summary>
    /// The debug overlay (SPEC §10): map, FPS, camera; for the free-fly camera
    /// its position in map units, angle, and the subsector and sector under it
    /// (<see cref="Level.R_PointInSubsector"/>); then the build status.
    /// </summary>
    public string OverlayText()
    {
        var text = new System.Text.StringBuilder();
        string map = Mesh?.Level.Name ?? "no map";
        text.Append($"{map} ({IndexOfMap(map) + 1}/{MapNames.Count})   FPS {Engine.GetFramesPerSecond():F0}   ");
        if (FreeFly is { } fly && fly.Current)
        {
            text.Append(fly.IsOrthographic
                ? $"free-fly, orthographic {fly.Size * LevelMesh.MapUnitsPerMetre:F0} units"
                : $"free-fly, perspective {fly.Fov:F0}°");
            text.Append($"   speed {fly.Speed:F0}/s\n");
            (int x, int y, int z) = MapPosition(fly.Pivot);
            text.Append($"x {x}  y {y}  z {z}   angle {fly.MapAngle:F0}°  pitch {fly.Pitch:F0}°\n");
            if (Mesh is not null)
            {
                int fx = ToFixed(fly.Pivot.X * LevelMesh.MapUnitsPerMetre), fy = ToFixed(-fly.Pivot.Z * LevelMesh.MapUnitsPerMetre);
                Subsector ss = Mesh.Level.R_PointInSubsector(fx, fy);
                Sector s = ss.Sector;
                int drawn = Mesh.Floors.SectorAt(fx, fy);
                text.Append($"sector {s.Index} (floor {s.FloorHeight >> Fixed.FRACBITS} {s.FloorPic}, ceiling {s.CeilingHeight >> Fixed.FRACBITS}, light {s.LightLevel})   subsector {ss.Index}");
                text.Append(drawn == s.Index ? "\n" : drawn < 0 ? "   (outside the map: no floor here)\n" : $"   (floor drawn here: sector {drawn})\n");
            }
        }
        else
            text.Append(FreeFly is null ? "overview\n" : "overview (Tab: free-fly)\n");
        if (Mesh is not null)
        {
            Vector2 o = Mesh.LightOrigin;
            text.Append(Mesh.LightMode switch
            {
                LightDiminishing.None => $"light: none (fixed distance {Mesh.LightReference:F0})\n",
                LightDiminishing.Camera => "light: camera depth\n",
                _ => $"light: player distance from ({o.X:F0}, {o.Y:F0})\n",
            });
        }
        text.Append(_status);
        text.Append(_showHelp ? "\n" + ControlsHelp : "\nF1: controls");
        return text.ToString();
    }

    /// <summary>
    /// The stand-in player position for light diminishing (map units x, y)
    /// until the game has a player (M3/M4): <c>--level-light-origin</c>, else
    /// the free-fly camera's pivot when it is current, else player 1's start
    /// (the map's centre without one).
    /// </summary>
    public Vector2 LightOrigin()
    {
        if (WadLocator.GetUserArg("--level-light-origin") is string arg)
        {
            string[] xy = arg.Split(',');
            if (xy.Length == 2 && float.TryParse(xy[0], System.Globalization.CultureInfo.InvariantCulture, out float x)
                && float.TryParse(xy[1], System.Globalization.CultureInfo.InvariantCulture, out float y))
                return new Vector2(x, y);
        }
        if (FreeFly is { } fly && fly.Current)
            return new Vector2(fly.Pivot.X, -fly.Pivot.Z) * LevelMesh.MapUnitsPerMetre;
        if (Mesh is null)
            return Vector2.Zero;
        if (Mesh.Level.PlayerStart(0) is MapThing start)
            return new Vector2(start.X, start.Y);
        Vector3 c = Mesh.Bounds.GetCenter();
        return new Vector2(c.X, -c.Z) * LevelMesh.MapUnitsPerMetre;
    }

    /// <summary>A Godot-space point in whole map units (x, y, height), rounded.</summary>
    public static (int X, int Y, int Z) MapPosition(Vector3 p) =>
        ((int)MathF.Round(p.X * LevelMesh.MapUnitsPerMetre), (int)MathF.Round(-p.Z * LevelMesh.MapUnitsPerMetre), (int)MathF.Round(p.Y * LevelMesh.MapUnitsPerMetre));

    /// <summary>Map units to fixed_t, clamped to the fixed_t range.</summary>
    private static int ToFixed(float units) => (int)Math.Clamp(Math.Round(units * 65536.0), int.MinValue, int.MaxValue);

    /// <summary>Opens the IWAD <see cref="WadLocator"/> finds and reads its texture table, palettes and map list.</summary>
    private void OpenWad()
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
        Textures = Textures.R_InitTextures(wad);
        Playpal = Playpal.Load(wad);
        Colormap = Colormap.Load(wad);
        MapNames = FindMaps(wad);
        Wad = wad;
        OpenWadMilliseconds = clock.Elapsed.TotalMilliseconds;
        GD.Print($"Level: {found.Path}: {info}, {MapNames.Count} maps; opened in {OpenWadMilliseconds:F0} ms");
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
        mesh.SetLightDiminishing(_lightMode);

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
        LastLoadMilliseconds = clock.Elapsed.TotalMilliseconds;
        Mesh = mesh;

        FrameCamera();
        if (FreeFly is not null)
            JumpToStart();
        string text = $"{map}: {level.Sectors.Length} sectors, {mesh.FloorTriangleCount} floor triangles, {mesh.WallQuads} wall quads, "
            + $"{mesh.SlotNames.Count} textures in a {mesh.Atlas.Image.Width}x{mesh.Atlas.Image.Height} atlas, {mesh.Walls.Missing.Count} missing; "
            + $"built in {clock.ElapsedMilliseconds} ms";
        GD.Print($"Level: {text}");
        _message.Text = _status = text;
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
        _message.Text = _status = message;
        if (IsCheckRun)
            GD.PrintErr("Level check: FAILED (no WAD loaded)");
        if (WadLocator.HasUserArg("--level-screenshot") || IsCheckRun)
            GetTree().Quit(1);
    }
}
