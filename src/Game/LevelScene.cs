using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Map;
using IsoDoom.Render;
using IsoDoom.Sim;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

/// <summary>
/// The level scene (T2.5, milestone M2): loads one map of the IWAD and shows
/// it as a textured mesh (<see cref="LevelMesh"/>) through the game camera
/// (<see cref="IsoCamera"/>, T3.3) following a player stand-in
/// (<see cref="PlayerPlaceholder"/>), or from a fixed overview camera
/// (<see cref="LevelCamera"/>) or the free-fly debug camera. Opened by <see cref="Main"/> with the
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
/// <c>--level-camera=iso|overview|fly</c> (start with the game camera, the
/// overview or the free-fly camera, T2.7/T3.3; default iso);
/// <c>--level-projection=ortho|perspective</c> (the game camera's projection,
/// <see cref="IsoProjection"/>, default ortho); <c>--level-pitch=DEGREES</c>
/// (its pitch, 45–60, default 55); <c>--level-zoom=UNITS</c> (its view height
/// in map units, 320–1600, default 640);
/// <c>--level-script=COMMANDS</c> (feed scripted input: <see cref="LevelScript"/>);
/// <c>--level-cutaway=cut|dither|off</c> (the wall cutaway's style, T3.4,
/// <see cref="Cutaway"/>; default cut), <c>--level-cutaway-radius=UNITS</c>
/// (default 80), <c>--level-cutaway-height=UNITS</c> (the cutoff above the
/// player's floor, default 32), <c>--level-cutaway-cursor=on|off</c> (the
/// cursor ground point cuts too; default off);
/// <c>--level-skill=1-5</c> (the skill whose things are drawn, T3.5; default 3);
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
/// Keys (T2.7, T3.3; not under <c>--level-check</c>): Tab cycles the game
/// camera, the overview and the free-fly camera (<see cref="FreeFlyCamera"/>,
/// which has its own keys); with the game camera W/A/S/D walk the placeholder
/// relative to the screen (Shift runs), it faces the cursor ground point,
/// Ctrl + wheel zooms and O switches orthographic / perspective
/// (<see cref="IsoCamera"/>); Home puts the placeholder (or, in free-fly, the
/// free-fly camera) at player 1's start, Page
/// Down / Page Up load the next / previous map of the WAD, L cycles the light
/// diminishing mode, X cycles the cutaway style (cut, dither, off), F1 shows
/// the controls, F3 hides the overlay.
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

    /// <summary>The cutaway's presentation options (T3.4); applied while the game camera is current.</summary>
    public CutawaySettings Cutaway { get; set; } = new();

    /// <summary>The loaded level's mesh, or null when no map is loaded.</summary>
    public LevelMesh? Mesh { get; private set; }

    /// <summary>The loaded IWAD (plus PWADs), its texture table, palettes and colormaps; null until <see cref="OpenWad"/> succeeds.</summary>
    public WadArchive? Wad { get; private set; }
    public Textures? Textures { get; private set; }
    public Playpal? Playpal { get; private set; }
    public Colormap? Colormap { get; private set; }

    /// <summary>The WAD's sprite index and its sprite atlas (T3.5; null when the sprites can't be indexed).</summary>
    public Sprites? Sprites { get; private set; }
    public SpriteAtlas? SpriteAtlas { get; private set; }

    /// <summary>The IWAD's game mode (which things spawn, T3.2).</summary>
    public GameMode GameMode { get; private set; }

    /// <summary>The skill whose things are drawn (<c>--level-skill</c>, default 3: <see cref="skill_t.sk_medium"/>).</summary>
    public skill_t Skill { get; private set; } = skill_t.sk_medium;

    /// <summary>The loaded map's things as spawned on <see cref="Skill"/> (T3.2's spawn list, <see cref="SpawnedThings"/>), and their billboards (T3.5).</summary>
    public SpawnedThing[] SpawnedThings { get; private set; } = Array.Empty<SpawnedThing>();
    public ThingSprites? Things { get; private set; }

    /// <summary>The maps of the WAD (<c>ExMy</c>/<c>MAPxx</c> headers followed by <c>THINGS</c>), in lump order, each once.</summary>
    public IReadOnlyList<string> MapNames { get; private set; } = Array.Empty<string>();

    /// <summary>The scene's camera (the overview camera; checks move it).</summary>
    public Camera3D Camera => _camera;

    /// <summary>The free-fly debug camera (T2.7); null under <c>--level-check</c>, which must keep the overview camera current.</summary>
    public FreeFlyCamera? FreeFly { get; private set; }

    /// <summary>The game camera (T3.3); null under <c>--level-check</c>.</summary>
    public IsoCamera? Iso { get; private set; }

    /// <summary>The player stand-in the game camera follows (T3.3, until T4.7's player mobj); null under <c>--level-check</c>.</summary>
    public PlayerPlaceholder? Placeholder { get; private set; }

    /// <summary>While true the game camera does not follow the placeholder (scripted measurements keep the view still).</summary>
    public bool HoldCamera { get; set; }

    /// <summary>The cursor ground point (<see cref="CursorGround"/>) under the game camera, updated every frame while it is current.</summary>
    public CursorGround.Hit? Cursor { get; private set; }

    private MeshInstance3D? _cursorMarker;

    /// <summary>p_local.h <c>VIEWHEIGHT</c>: eye height above the floor, map units (where Home puts the free-fly camera).</summary>
    public const int VIEWHEIGHT = 41;

    /// <summary>Controls shown by F1.</summary>
    public const string ControlsHelp =
        "Tab game camera/overview/free-fly   Home player 1 start   PgDn/PgUp next/previous map   L light mode   X cutaway   F1 controls   F3 overlay\n"
        + "Game camera: W/A/S/D walk (Shift runs), the mouse aims, Ctrl+wheel zoom, O orthographic/perspective\n"
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
            if (WadLocator.GetUserArg("--level-skill") is string skill)
                Skill = int.TryParse(skill, out int n) && n >= 1 && n <= 5
                    ? (skill_t)(n - 1)
                    : throw new ArgumentException($"--level-skill: \"{skill}\" (1-5)");
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
            Cutaway = ParseCutaway();
            FreeFly = new FreeFlyCamera { Name = "FreeFly" };
            AddChild(FreeFly);
            CreateGameCamera();
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
        switch (WadLocator.GetUserArg("--level-camera"))
        {
            case "fly": UseCamera(CameraMode.FreeFly); break;
            case "overview": UseCamera(CameraMode.Overview); break;
            case null or "iso": UseCamera(CameraMode.Iso); break;
            case string other: Fail($"--level-camera: unknown camera \"{other}\" (iso, overview or fly)"); return;
        }
        if (WadLocator.GetUserArg("--level-script") is string script)
            AddChild(new LevelScript(this, script));
        else if (WadLocator.GetUserArg("--level-screenshot") is string path)
            _ = ScreenshotAsync(path);
    }

    /// <summary>The level scene's cameras (Tab cycles them in this order).</summary>
    public enum CameraMode { Iso, Overview, FreeFly }

    /// <summary>Makes the free-fly camera (<paramref name="fly"/>) or the overview camera current.</summary>
    public void UseFreeFly(bool fly) => UseCamera(fly ? CameraMode.FreeFly : CameraMode.Overview);

    /// <summary>Makes the camera <paramref name="mode"/> current (the overview camera when that one does not exist).</summary>
    public void UseCamera(CameraMode mode)
    {
        if (mode == CameraMode.FreeFly && FreeFly is not null)
        {
            FreeFly.MakeCurrent();
            return;
        }
        Input.MouseMode = Input.MouseModeEnum.Visible;
        if (mode == CameraMode.Iso && Iso is not null)
        {
            Iso.MakeCurrent();
            if (Placeholder is not null)
                Iso.Snap(Placeholder.Foot);
        }
        else
            _camera.MakeCurrent();
    }

    /// <summary>The current camera.</summary>
    public CameraMode CurrentCamera =>
        FreeFly is { Current: true } ? CameraMode.FreeFly : Iso is { Current: true } ? CameraMode.Iso : CameraMode.Overview;

    /// <summary>Whether the free-fly camera is the current one.</summary>
    public bool FreeFlyActive => FreeFly is { } f && f.Current;

    /// <summary>Whether the game camera is the current one.</summary>
    public bool IsoActive => Iso is { } i && i.Current;

    /// <summary>Creates the game camera, the placeholder (with the WAD's <c>PLAY</c> sprite) and the cursor marker.</summary>
    private void CreateGameCamera()
    {
        Iso = new IsoCamera { Name = "Iso" };
        AddChild(Iso);
        if (WadLocator.GetUserArg("--level-projection") is string projection)
            Iso.SetProjectionMode(projection switch
            {
                "ortho" or "orthographic" => IsoProjection.Orthographic,
                "perspective" => IsoProjection.Perspective,
                _ => throw new ArgumentException($"--level-projection: unknown projection \"{projection}\" (ortho or perspective)"),
            });
        if (WadLocator.GetUserArg("--level-pitch") is string pitch)
            Iso.SetPitch(ParseFloat(pitch, "--level-pitch"));
        if (WadLocator.GetUserArg("--level-zoom") is string zoom)
            Iso.SetViewUnits(ParseFloat(zoom, "--level-zoom"));

        Placeholder = new PlayerPlaceholder { Name = "Placeholder" };
        AddChild(Placeholder);

        // The cursor ground point: a small ring on the floor, drawn over everything (debug, hidden with the overlay).
        var ring = new TorusMesh { InnerRadius = 5f / LevelMesh.MapUnitsPerMetre, OuterRadius = 8f / LevelMesh.MapUnitsPerMetre, Rings = 16, RingSegments = 4 };
        ring.Material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1f, 0.85f, 0.1f),
            NoDepthTest = true,
            RenderPriority = 1,
        };
        _cursorMarker = new MeshInstance3D { Mesh = ring, Name = "CursorMarker", Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_cursorMarker);
    }

    /// <summary>The cutaway options from <c>--level-cutaway</c>, <c>--level-cutaway-radius</c>, <c>--level-cutaway-height</c> and <c>--level-cutaway-cursor</c>.</summary>
    private static CutawaySettings ParseCutaway()
    {
        var settings = new CutawaySettings();
        if (WadLocator.GetUserArg("--level-cutaway") is string style)
            settings = settings with { Style = Render.Cutaway.ParseStyle(style) };
        if (WadLocator.GetUserArg("--level-cutaway-radius") is string radius)
            settings = settings with { Radius = Math.Max(0, ParseFloat(radius, "--level-cutaway-radius")) };
        if (WadLocator.GetUserArg("--level-cutaway-height") is string height)
            settings = settings with { Height = ParseFloat(height, "--level-cutaway-height") };
        if (WadLocator.GetUserArg("--level-cutaway-cursor") is string cursor)
            settings = settings with
            {
                Cursor = cursor switch
                {
                    "on" => true,
                    "off" => false,
                    _ => throw new ArgumentException($"--level-cutaway-cursor: \"{cursor}\" (on or off)"),
                },
            };
        return settings;
    }

    private static float ParseFloat(string s, string what) =>
        float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f)
            ? f
            : throw new ArgumentException($"{what}: not a number: \"{s}\"");

    /// <summary>Puts the placeholder at map point (<paramref name="x"/>, <paramref name="y"/>) facing <paramref name="angle"/> (vanilla degrees) and centres the game camera on it.</summary>
    public void PlacePlaceholder(float x, float y, float? angle = null)
    {
        if (Placeholder is null || Mesh is null)
            return;
        Placeholder.Place(Mesh.Level, x, y);
        if (angle is float a)
            Placeholder.Angle = a;
        Iso?.Snap(Placeholder.Foot);
    }

    /// <summary>Puts the placeholder at player <paramref name="player"/>'s start (or the map's centre), facing its angle.</summary>
    public void PlaceholderToStart(int player = 0)
    {
        if (Mesh is null)
            return;
        if (Mesh.Level.PlayerStart(player) is MapThing start)
            PlacePlaceholder(start.X, start.Y, start.Angle);
        else
        {
            Vector3 c = Mesh.Bounds.GetCenter();
            PlacePlaceholder(c.X * LevelMesh.MapUnitsPerMetre, -c.Z * LevelMesh.MapUnitsPerMetre, 90);
        }
    }

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
                CameraMode next = (CameraMode)(((int)CurrentCamera + 1) % 3);
                if (next == CameraMode.Iso && Iso is null)
                    next = CameraMode.Overview;
                UseCamera(next);
                break;
            case Key.Home:
                if (FreeFlyActive || Placeholder is null)
                {
                    JumpToStart();
                    UseFreeFly(true);
                }
                else
                    PlaceholderToStart();
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
            case Key.X:
                Cutaway = Cutaway with { Style = Cutaway.Style switch { CutawayStyle.Cut => CutawayStyle.Dither, CutawayStyle.Dither => CutawayStyle.Off, _ => CutawayStyle.Cut } };
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
        {
            UpdateGameCamera(delta);
            if (GetViewport().GetCamera3D() is Camera3D current)
                Things?.UpdateRotations(current);
            Mesh.SetLightOrigin(LightOrigin());
            UpdateCutaway();
        }
        _crosshair.Visible = FreeFlyActive;
        if (!IsCheckRun && Overlay.Visible)
            _message.Text = OverlayText();
    }

    /// <summary>
    /// One frame of the game camera (T3.3): the cursor ground point under the
    /// mouse, the placeholder walked by W/A/S/D relative to the screen and
    /// facing the cursor ground point, and the camera following it.
    /// </summary>
    private void UpdateGameCamera(double delta)
    {
        if (Iso is null || Placeholder is null || Mesh is null)
            return;
        Placeholder.UpdateFloor();
        if (Iso.Current)
        {
            Cursor = CursorGround.Pick(Mesh, Iso, Iso.CursorOrCentre, Placeholder.FloorHeight);
            var move = new Vector2(Axis(Key.D, Key.A), Axis(Key.W, Key.S));
            if (move != Vector2.Zero)
            {
                float speed = Input.IsPhysicalKeyPressed(Key.Shift) ? PlayerPlaceholder.RunSpeed : PlayerPlaceholder.WalkSpeed;
                Vector3 dir = Iso.GroundRight * move.X + Iso.GroundUp * move.Y; // Godot XZ: map x = X, map y = −Z
                var step = new Vector2(dir.X, -dir.Z).Normalized() * speed * (float)delta;
                Placeholder.Move(step.X, step.Y);
            }
            if (Cursor is { } hit)
            {
                Vector3 c = hit.MapUnits;
                Vector2 toCursor = new Vector2(c.X, c.Y) - Placeholder.MapPosition;
                if (toCursor.LengthSquared() > 1f)
                    Placeholder.Angle = Mathf.PosMod(Mathf.RadToDeg(MathF.Atan2(toCursor.Y, toCursor.X)), 360f);
            }
            if (!HoldCamera)
                Iso.Follow(Placeholder.Foot, Cursor?.Point, delta);
        }
        else
            Cursor = null;
        if (GetViewport().GetCamera3D() is Camera3D current)
            Placeholder.FaceCamera(current);
        if (_cursorMarker is not null)
        {
            _cursorMarker.Visible = Cursor is not null && Overlay.Visible;
            if (Cursor is { } c)
                _cursorMarker.Position = c.Point + new Vector3(0, 0.5f / LevelMesh.MapUnitsPerMetre, 0);
        }
    }

    /// <summary>
    /// The cutaway (T3.4) follows the placeholder (and the cursor ground point
    /// when <see cref="CutawaySettings.Cursor"/>) under the game camera; the
    /// overview and free-fly cameras show the walls whole.
    /// </summary>
    public void UpdateCutaway()
    {
        if (Mesh is null)
            return;
        bool on = IsoActive && Placeholder is not null;
        CutawaySettings settings = on ? Cutaway : Cutaway with { Style = CutawayStyle.Off };
        if (Mesh.Cutaway != settings)
            Mesh.SetCutaway(settings);
        Vector3? player = on ? new Vector3(Placeholder!.MapPosition.X, Placeholder.MapPosition.Y, Placeholder.FloorHeight) : null;
        Vector3? cursor = on && Cutaway.Cursor && Cursor is { } hit ? hit.MapUnits : null;
        if (Mesh.CutPlayer != player || Mesh.CutCursor != cursor)
            Mesh.SetCutawayCentres(player, cursor);
    }

    private static float Axis(Key positive, Key negative) =>
        (Input.IsPhysicalKeyPressed(positive) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(negative) ? 1f : 0f);

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
        else if (Iso is { } iso && iso.Current)
        {
            text.Append(iso.Mode == IsoProjection.Orthographic ? "game camera, orthographic" : $"game camera, perspective {IsoCamera.PerspectiveFov:F0}°");
            text.Append($", view {iso.ViewUnits:F0} units, pitch {iso.Pitch:F0}°\n");
            (int fx, int fy, int fz) = MapPosition(iso.Focus);
            text.Append($"focus x {fx}  y {fy}  z {fz}\n");
        }
        else
            text.Append(FreeFly is null ? "overview\n" : "overview (Tab: free-fly)\n");
        if (Placeholder is { } p && Mesh is not null && !FreeFlyActive)
        {
            Vector2 at = p.MapPosition;
            text.Append($"placeholder x {at.X:F0}  y {at.Y:F0}  z {p.FloorHeight:F0}   angle {p.Angle:F0}°  rotation {p.Rotation + 1}");
            if (p.Sector is Sector ps)
                text.Append($"   sector {ps.Index} (floor {ps.FloorHeight >> Fixed.FRACBITS} {ps.FloorPic}, light {ps.LightLevel})");
            text.Append('\n');
        }
        if (Cursor is { } hit)
        {
            Vector3 c = hit.MapUnits;
            text.Append($"cursor x {c.X:F0}  y {c.Y:F0}  z {c.Z:F0}   ");
            text.Append(hit.OnFloor ? $"floor of sector {hit.Sector}\n" : "no floor (plane at the placeholder's floor)\n");
        }
        if (Iso is { Current: true })
            text.Append(Cutaway.Style == CutawayStyle.Off
                ? "cutaway: off\n"
                : $"cutaway: {Cutaway.Style.ToString().ToLowerInvariant()}, radius {Cutaway.Radius:F0}, above {Cutaway.Height:F0}{(Cutaway.Cursor ? ", and around the cursor" : "")}\n");
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
    /// until the game has a player (M4): <c>--level-light-origin</c>, else
    /// the free-fly camera's pivot when it is current, else the placeholder
    /// (T3.3), else player 1's start (the map's centre without one).
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
        if (Placeholder is { } p && Mesh is not null)
            return p.MapPosition;
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
        GameMode = info.GameMode;
        try
        {
            Sprites = Sprites.R_InitSprites(wad);
            SpriteAtlas = SpriteAtlas.Build(wad, Sprites);
        }
        catch (WadFormatException e)
        {
            GD.PushWarning($"Level: no sprites: {e.Message}");
            (Sprites, SpriteAtlas) = (null, null);
        }
        Playpal = Playpal.Load(wad);
        Colormap = Colormap.Load(wad);
        MapNames = FindMaps(wad);
        Wad = wad;
        OpenWadMilliseconds = clock.Elapsed.TotalMilliseconds;
        GD.Print($"Level: {found.Path}: {info}, {MapNames.Count} maps; opened in {OpenWadMilliseconds:F0} ms"
            + (SpriteAtlas is { } sa ? $" (sprite atlas: {sa.Lumps.Count} lumps in {sa.Atlas.Image.Width}x{sa.Atlas.Image.Height}, {sa.BuildMilliseconds:F0} ms)" : ""));
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
        Things?.QueueFree();
        Things = null;
        SpawnedThings = Array.Empty<SpawnedThing>();

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
        BuildThings(level, mesh);
        clock.Stop();
        LastLoadMilliseconds = clock.Elapsed.TotalMilliseconds;
        Mesh = mesh;
        if (SpriteAtlas is not null)
            Placeholder?.Bind(SpriteAtlas, mesh.SpriteMaterial);

        FrameCamera();
        if (FreeFly is not null)
            JumpToStart();
        PlaceholderToStart();
        string text = $"{map}: {level.Sectors.Length} sectors, {mesh.FloorTriangleCount} floor triangles, {mesh.WallQuads} wall quads, {mesh.MaskedQuads} masked, {SpawnedThings.Length} things, "
            + $"{mesh.SlotNames.Count} textures in a {mesh.Atlas.Image.Width}x{mesh.Atlas.Image.Height} atlas, {mesh.Walls.Missing.Count} missing; "
            + $"built in {clock.ElapsedMilliseconds} ms";
        GD.Print($"Level: {text}");
        _message.Text = _status = text;
    }

    /// <summary>
    /// The map's things on <see cref="Skill"/> (T3.2's spawn list; the player
    /// start is the placeholder's) as billboards (T3.5). A thing type the
    /// game doesn't know (vanilla <c>I_Error</c>s) leaves the map without things.
    /// </summary>
    private void BuildThings(Level level, LevelMesh mesh)
    {
        if (SpriteAtlas is null)
            return;
        try
        {
            SpawnedThings = IsoDoom.Sim.SpawnedThings.Build(level, MapThingSpawning.SpawnList(level.Things, new SpawnSettings(GameMode, Skill)));
        }
        catch (WadFormatException e)
        {
            GD.PushWarning($"Level: {level.Name}: no things: {e.Message}");
            SpawnedThings = Array.Empty<SpawnedThing>();
        }
        var entries = new ThingSprites.Entry[SpawnedThings.Length];
        for (int i = 0; i < entries.Length; i++)
            entries[i] = ThingEntry(SpawnedThings[i]);
        Things = new ThingSprites { Name = "Things" };
        Things.Bind(SpriteAtlas, mesh.SpriteMaterial);
        Things.SetEntries(entries);
        AddChild(Things);
        if (Things.MissingFrames > 0)
            GD.PushWarning($"Level: {level.Name}: {Things.MissingFrames} thing(s) whose spawn frame the WAD lacks are not drawn");
    }

    /// <summary>A spawned thing as a billboard entry (map units).</summary>
    public static ThingSprites.Entry ThingEntry(SpawnedThing t) =>
        new(new Vector3((float)(t.x / 65536.0), (float)(t.y / 65536.0), (float)(t.z / 65536.0)), t.angle, t.Sector.Index, (int)t.sprite, t.frame, t.fullbright);

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

    /// <summary>
    /// How much of the placeholder the game camera shows (T3.4; needs a real
    /// renderer, else null): the pixels that change when the placeholder is
    /// hidden, against the pixels it covers with the level hidden. The camera
    /// holds still and the overlay is hidden while the four frames render.
    /// </summary>
    public async Task<(int Visible, int Total)?> PlaceholderVisibilityAsync()
    {
        if (Placeholder is null || DisplayServer.GetName() == "headless")
            return null;
        bool overlay = Overlay.Visible, hold = HoldCamera;
        Overlay.Visible = false;
        HoldCamera = true;
        byte[] shown = await CaptureAsync();
        Placeholder.Visible = false;
        byte[] hidden = await CaptureAsync();
        foreach (MeshInstance3D chunk in _chunks)
            chunk.Visible = false;
        byte[] empty = await CaptureAsync();
        Placeholder.Visible = true;
        byte[] alone = await CaptureAsync();
        foreach (MeshInstance3D chunk in _chunks)
            chunk.Visible = true;
        Overlay.Visible = overlay;
        HoldCamera = hold;
        int visible = 0, total = 0;
        for (int i = 0; i + 3 < shown.Length; i += 4)
        {
            if (shown[i] != hidden[i] || shown[i + 1] != hidden[i + 1] || shown[i + 2] != hidden[i + 2])
                visible++;
            if (alone[i] != empty[i] || alone[i + 1] != empty[i + 1] || alone[i + 2] != empty[i + 2])
                total++;
        }
        return (visible, total);
    }

    /// <summary>Renders a frame and reads it back as RGBA8 bytes.</summary>
    private async Task<byte[]> CaptureAsync()
    {
        for (int i = 0; i < 2; i++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image frame = GetViewport().GetTexture().GetImage();
        if (frame.GetFormat() != Image.Format.Rgba8)
            frame.Convert(Image.Format.Rgba8);
        return frame.GetData();
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
