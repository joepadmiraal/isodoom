using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Collections.Generic;
using Godot;
using SectorFloor = IsoDoom.Map.SectorFloor;
using IsoDoom.Render;

namespace IsoDoom.Game;

/// <summary>
/// Scripted input for the level scene (T2.7, debugging): the user argument
/// <c>--level-script="COMMAND; COMMAND; …"</c> feeds input events through
/// <see cref="Input.ParseInputEvent"/>, so they take the same path as real
/// keys and mouse, one command after another, then quits. Lets the free-fly
/// camera and the map switching be checked without a person at the keyboard
/// (with <c>--fixed-fps 60</c> before <c>--</c>, movement per frame is fixed).
/// <para>
/// Commands (keys by Godot name, e.g. <c>W</c>, <c>Shift</c>, <c>Tab</c>,
/// <c>PageDown</c>): <c>down KEY</c> / <c>up KEY</c> (press or release);
/// <c>tap KEY</c> (press, a frame, release); <c>hold KEY FRAMES</c>;
/// <c>wait FRAMES</c>; <c>click</c> (left button: captures the mouse);
/// <c>look DX DY</c> (mouse motion in pixels); <c>wheel up|down [STEPS]</c>
/// (Ctrl as currently held); <c>print</c> (the overlay text to stdout);
/// <c>shot FILE.png</c> (capture, needs a real renderer; WAD-derived, keep it
/// out of the repo); <c>view X Y ANGLE [Z]</c> (T2.9: put the free-fly camera
/// at map point X, Y facing vanilla angle ANGLE in degrees, no pitch, at
/// height Z or by default at eye level, floor + <c>VIEWHEIGHT</c>, as a
/// vanilla player standing still); <c>fov DEGREES|vanilla</c> (the free-fly
/// camera's vertical field of view; <c>vanilla</c> is
/// <see cref="FreeFlyCamera.VanillaFov"/>, so a 16:10 window shows what
/// vanilla's full-screen 320×200 view shows); <c>mouse X Y</c> (T3.3: move
/// the cursor to viewport pixel X, Y; the game camera's cursor ground point
/// follows); <c>place X Y [ANGLE]</c> (T3.3: put the placeholder at map
/// point X, Y, optionally facing ANGLE degrees, and centre the game camera on
/// it); <c>visible [MIN]</c> (T3.4: print how much of the placeholder the
/// game camera shows, in % of its pixels, and fail the script below MIN %,
/// default 25; needs a real renderer); <c>walkto X Y [STEP] [MIN]</c> (T3.4:
/// move the placeholder in a straight line, through walls, to map point X, Y,
/// STEP units at a time (default 32), centring the camera and running
/// <c>visible MIN</c> at every step, and print the least visible step);
/// <c>tour [STEP] [MIN]</c> (T3.4: <c>walkto</c> a point inside every
/// sector's floor of the map, nearest unvisited first from where the
/// placeholder stands);
/// <c>quit</c> (also implied at the end).
/// </para>
/// </summary>
public partial class LevelScript : Node
{
    private readonly LevelScene _scene;
    private readonly string[] _commands;

    public LevelScript(LevelScene scene, string script)
    {
        _scene = scene;
        _commands = script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        int exit = 0;
        await Frames(2);
        foreach (string command in _commands)
        {
            string[] w = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            GD.Print($"Level script: {command}");
            try
            {
                switch (w[0])
                {
                    case "down": Key(w[1], true); break;
                    case "up": Key(w[1], false); break;
                    case "tap": Key(w[1], true); await Frames(1); Key(w[1], false); break;
                    case "hold": Key(w[1], true); await Frames(Int(w[2])); Key(w[1], false); break;
                    case "wait": await Frames(Int(w[1])); break;
                    case "click": Click(); break;
                    case "look":
                        Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(Int(w[1]), Int(w[2])) });
                        break;
                    case "wheel":
                        for (int i = 0, n = w.Length > 2 ? Int(w[2]) : 1; i < n; i++)
                            Wheel(w[1] == "up" ? MouseButton.WheelUp : MouseButton.WheelDown);
                        break;
                    case "print": GD.Print($"Level script: overlay:\n{_scene.OverlayText()}"); break;
                    case "shot": exit |= await Shot(w[1]); break;
                    case "view":
                        _scene.PlaceFreeFly(Int(w[1]), Int(w[2]), float.Parse(w[3], CultureInfo.InvariantCulture), w.Length > 4 ? Int(w[4]) : null);
                        break;
                    case "fov":
                        if (_scene.FreeFly is { } fly)
                            fly.Fov = w[1] == "vanilla" ? FreeFlyCamera.VanillaFov : float.Parse(w[1], CultureInfo.InvariantCulture);
                        break;
                    case "mouse":
                        Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(Int(w[1]), Int(w[2])), GlobalPosition = new Vector2(Int(w[1]), Int(w[2])) });
                        break;
                    case "place":
                        _scene.PlacePlaceholder(Int(w[1]), Int(w[2]), w.Length > 3 ? float.Parse(w[3], CultureInfo.InvariantCulture) : null);
                        break;
                    case "visible":
                        exit |= await Visible(w.Length > 1 ? Int(w[1]) : DefaultMinVisible, true) is null ? 1 : 0;
                        break;
                    case "walkto":
                        exit |= await WalkTo(Int(w[1]), Int(w[2]), w.Length > 3 ? Int(w[3]) : 32, w.Length > 4 ? Int(w[4]) : DefaultMinVisible);
                        break;
                    case "tour":
                        exit |= await Tour(w.Length > 1 ? Int(w[1]) : 32, w.Length > 2 ? Int(w[2]) : DefaultMinVisible);
                        break;
                    case "quit": GetTree().Quit(exit); return;
                    default: throw new ArgumentException($"unknown command \"{w[0]}\"");
                }
            }
            catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or FormatException)
            {
                GD.PrintErr($"Level script: bad command \"{command}\": {e.Message}");
                GetTree().Quit(1);
                return;
            }
            await Frames(1); // let the event reach the nodes
        }
        GetTree().Quit(exit);
    }

    /// <summary>The least share of the placeholder's pixels (%) <c>visible</c> and <c>walkto</c> accept by default.</summary>
    public const int DefaultMinVisible = 25;

    /// <summary>Measures the placeholder's visibility (%); null (and an error) when it is below <paramref name="min"/> or can't be measured.</summary>
    private async Task<double?> Visible(int min, bool print)
    {
        if (await _scene.PlaceholderVisibilityAsync() is not (int visible, int total))
        {
            GD.PrintErr("Level script: visible needs a real renderer and the placeholder (run without --headless)");
            return null;
        }
        double share = total == 0 ? 0 : 100.0 * visible / total;
        Vector2 at = _scene.Placeholder!.MapPosition;
        string where = $"placeholder at ({at.X:F0}, {at.Y:F0}), floor {_scene.Placeholder.FloorHeight:F0}";
        if (total == 0 || share < min)
        {
            GD.PrintErr($"Level script: {where}: {visible} of {total} pixels visible ({share:F0}%), below {min}%");
            return null;
        }
        if (print)
            GD.Print($"Level script: {where}: {visible} of {total} pixels visible ({share:F0}%)");
        return share;
    }

    private async Task<int> WalkTo(int x, int y, int step, int min)
    {
        if (_scene.Placeholder is not { } p)
            throw new ArgumentException("no placeholder");
        Vector2 from = p.MapPosition, to = new(x, y);
        int steps = Math.Max(1, (int)Math.Ceiling(from.DistanceTo(to) / Math.Max(1, step)));
        int failed = 0;
        double least = double.MaxValue;
        Vector2 leastAt = from;
        for (int i = 1; i <= steps; i++)
        {
            Vector2 at = from.Lerp(to, (float)i / steps);
            _scene.PlacePlaceholder(at.X, at.Y);
            if (await Visible(min, false) is double share)
            {
                if (share < least)
                    (least, leastAt) = (share, at);
            }
            else
                failed++;
        }
        GD.Print(failed == 0
            ? $"Level script: walked to ({x}, {y}) in {steps} steps; least visible {least:F0}% at ({leastAt.X:F0}, {leastAt.Y:F0})"
            : $"Level script: walked to ({x}, {y}) in {steps} steps; {failed} step(s) below {min}%");
        return failed == 0 ? 0 : 1;
    }

    private async Task<int> Tour(int step, int min)
    {
        if (_scene.Mesh is not { } m || _scene.Placeholder is not { } p)
            throw new ArgumentException("no map or placeholder");
        var points = new List<Vector2>();
        foreach (SectorFloor floor in m.Floors.BySector)
        {
            if (floor.TriangleCount > 0)
            {
                (int x, int y) = floor.InteriorPoint();
                points.Add(new Vector2(MathF.Round(x / 65536f), MathF.Round(y / 65536f)));
            }
        }
        int exit = 0, legs = 0;
        while (points.Count > 0)
        {
            Vector2 at = p.MapPosition;
            int next = 0;
            for (int i = 1; i < points.Count; i++)
            {
                if (points[i].DistanceSquaredTo(at) < points[next].DistanceSquaredTo(at))
                    next = i;
            }
            Vector2 to = points[next];
            points.RemoveAt(next);
            exit |= await WalkTo((int)to.X, (int)to.Y, step, min);
            legs++;
        }
        GD.Print($"Level script: toured {legs} sector floors of {m.Level.Name}{(exit == 0 ? "" : " (some steps below the minimum)")}");
        return exit;
    }

    private static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static void Key(string name, bool pressed)
    {
        Key key = OS.FindKeycodeFromString(name);
        if (key == Godot.Key.None)
            throw new ArgumentException($"unknown key \"{name}\"");
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
    }

    private static void Click()
    {
        foreach (bool pressed in new[] { true, false })
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed });
    }

    private static void Wheel(MouseButton button)
    {
        bool ctrl = Input.IsPhysicalKeyPressed(Godot.Key.Ctrl);
        foreach (bool pressed in new[] { true, false })
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = pressed, CtrlPressed = ctrl, Factor = 1 });
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task<int> Shot(string path)
    {
        if (DisplayServer.GetName() == "headless")
        {
            GD.PrintErr("Level script: shot needs a real renderer (run without --headless)");
            return 1;
        }
        for (int i = 0; i < 3; i++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Error err = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print(err == Error.Ok ? $"Level script: saved {path}" : $"Level script: could not save {path}: {err}");
        return err == Error.Ok ? 0 : 1;
    }
}
