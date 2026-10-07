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
/// follows); <c>place X Y [ANGLE]</c> (T3.3: put the player mobj at map
/// point X, Y on the floor, optionally facing ANGLE degrees, with no momentum
/// and no collision check, and centre the game camera on it); <c>visible [MIN]</c>
/// (T3.4: print how much of the player the game camera shows, in % of its
/// pixels, and fail the script below MIN %, default 25; needs a real renderer;
/// the world holds still meanwhile); <c>walkto X Y [STEP] [MIN]</c> (T3.4:
/// move the player in a straight line, through walls, to map point X, Y,
/// STEP units at a time (default 32), centring the camera and running
/// <c>visible MIN</c> at every step, and print the least visible step);
/// <c>tour [STEP] [MIN]</c> (T3.4: <c>walkto</c> a point inside every
/// sector's floor of the map, nearest unvisited first from where the
/// player stands); <c>frametime [FRAMES]</c> (T3.8: measure the wall-clock
/// time of the next FRAMES frames, default 300, and print the mean, median, 95th
/// percentile and worst in ms; run with <c>--disable-vsync</c> before <c>--</c>);
/// <c>things on|off</c> (T3.8: show or hide the thing billboards, e.g. to
/// measure what they cost); <c>ticcmd [vanilla]</c> (T4.6: print the
/// <c>ticcmd</c> the builder makes from the input since the last
/// <c>ticcmd</c> for the player, with the twin-stick tweaks or vanilla's; it
/// takes that input from the game loop's next tic);
/// <c>quit</c> (also implied at the end).
/// </para>
/// <para>
/// The game loop (T4.7): <c>cmd FORWARD SIDE ANGLETURN [BUTTONS] [TICS]</c>
/// queues TICS tics (default 1) of that <c>ticcmd</c> (raw fields: with the
/// twin-stick tweaks ANGLETURN is the absolute angle's upper 16 bits, e.g.
/// 16384 = north, FORWARD thrusts north and SIDE east; with
/// <c>--level-tweaks=vanilla</c> they are relative, as a demo's) and
/// <c>step TICS</c> queues TICS tics built from the input at each tic (keys
/// held by <c>down</c>, the cursor ground point) and waits until they ran.
/// Either switches the game loop to scripted tics (<see cref="LevelScene.ScriptedTics"/>,
/// also from the start when the script has a <c>cmd</c>, <c>step</c> or <c>sim scripted</c>):
/// tics run only from the queue, at 35 Hz, and the world holds still while
/// it is empty, so the result does not depend on the frame rate
/// (<c>--fixed-fps 30</c>, <c>60</c>, <c>144</c> give the same checksum;
/// a cursor aim does depend on it, as the camera follows the player with
/// smoothing). <c>sim live|scripted</c> switches the mode (live: tics from the
/// input every 1/35 s, as the game). <c>tics N</c> waits until N more tics
/// ran (or the queue is empty). <c>checksum</c> waits until the queue is empty and
/// prints <c>leveltime</c>, <c>World.Checksum()</c> and the player mobj.
/// <c>tictime TICS</c> (T4.9) times <c>World.G_Ticker</c> over the next TICS
/// tics (or until the scripted queue is empty) and prints the mean, median,
/// 95th percentile and worst in ms (SPEC §9's budget is 2 ms).
/// <c>map NAME [EXITS]</c> (T5.8) waits until the queue is empty, prints the map
/// shown, the maps completed through exits and the player's status, and
/// fails the script unless the map is NAME (and, T5.10, EXITS maps were
/// completed through exits since the scene started; e.g. after an exit:
/// <c>place 2940 -4768 180; cmd 0 0 -32768 0 2; cmd 0 0 -32768 2; map E1M2</c>
/// on DOOM1.WAD's E1M1).
/// <c>route FILE</c> (T5.10) queues a <c>.route</c> file's tics
/// (<see cref="RouteFile"/>, e.g. <c>tests/IsoDoom.Tests/Sim/Routes/e1m1-exit.route</c>)
/// and does not wait for them, so <c>tics N; shot FILE.png</c> can follow
/// along; a <c>start</c> header places the player first, as the route tests
/// do. Routes are vanilla demos: the scene needs <c>--level-tweaks=vanilla</c>,
/// <c>--level-monsters=off</c>, the route's skill and map, or the script
/// fails. <c>map NAME</c> after it checks that the route left by its exit.
/// <c>joy AXIS VALUE</c> (T4.9) moves a gamepad axis by Godot <c>JoyAxis</c>
/// name (<c>LeftX</c>, <c>LeftY</c>: the move stick, <c>RightX</c>,
/// <c>RightY</c>: the aim stick, <c>TriggerRight</c>: fire; Y down is
/// positive) to VALUE (−1 to 1); <c>joybutton BUTTON down|up</c> presses or
/// releases a gamepad button by <c>JoyButton</c> name (e.g. <c>A</c> use,
/// <c>LeftStick</c> run toggle). Both go through <see cref="Input.ParseInputEvent"/>
/// as device 0, as a real pad.
/// <c>smooth FRAMES</c> records where the player is drawn on each of the next
/// FRAMES frames and prints the steps between frames (interpolation: even
/// steps while moving at a steady speed); <c>smooth FRAMES SECTOR</c> (T5.1)
/// does the same for sector SECTOR's drawn floor and ceiling.
/// <c>plane SECTOR floor|ceiling HEIGHT [SPEED]</c> (T5.1, a debug move) moves
/// the sector's floor or ceiling towards HEIGHT by SPEED units (default 2, a
/// door's speed) at the end of each tic, as a mover thinker would, with
/// <c>P_ChangeSector</c>; it runs with the tics (queue some, or <c>sim live</c>).
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

    public override void _Ready()
    {
        // Scripted tics from the first frame, so no input tic runs before the first command.
        foreach (string command in _commands)
        {
            if (command.StartsWith("cmd ", StringComparison.Ordinal) || command.StartsWith("step ", StringComparison.Ordinal)
                || command.StartsWith("route ", StringComparison.Ordinal) || command == "sim scripted")
            {
                _scene.ScriptedTics = true;
                break;
            }
        }
        _ = RunAsync();
    }

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
                        _scene.PlacePlayer(Int(w[1]), Int(w[2]), w.Length > 3 ? float.Parse(w[3], CultureInfo.InvariantCulture) : null);
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
                    case "frametime": await FrameTime(w.Length > 1 ? Int(w[1]) : 300); break;
                    case "things":
                        if (_scene.Things is { } things)
                            things.Visible = things.Shadows.Visible = w[1] == "on";
                        break;
                    case "ticcmd":
                        {
                            var cmd = _scene.BuildTiccmd(w.Length > 1 && w[1] == "vanilla" ? IsoDoom.Sim.Tweaks.Vanilla : IsoDoom.Sim.Tweaks.TopDown);
                            GD.Print($"Level script: ticcmd forwardmove {cmd.forwardmove} sidemove {cmd.sidemove} angleturn {cmd.angleturn} ({(ushort)cmd.angleturn * 360.0 / 65536:0.##} deg) buttons {cmd.buttons}");
                            break;
                        }
                    case "cmd":
                        {
                            var cmd = new IsoDoom.Sim.ticcmd_t
                            {
                                forwardmove = checked((sbyte)Int(w[1])),
                                sidemove = checked((sbyte)Int(w[2])),
                                angleturn = unchecked((short)Int(w[3])),
                                buttons = w.Length > 4 ? checked((byte)Int(w[4])) : (byte)0,
                            };
                            _scene.ScriptedTics = true;
                            for (int i = 0, n = w.Length > 5 ? Int(w[5]) : 1; i < n; i++)
                                _scene.QueueTic(cmd);
                            break;
                        }
                    case "step":
                        _scene.ScriptedTics = true;
                        for (int i = 0, n = Int(w[1]); i < n; i++)
                            _scene.QueueTic(null);
                        await Drain();
                        break;
                    case "sim":
                        _scene.ScriptedTics = w[1] switch
                        {
                            "scripted" => true,
                            "live" => false,
                            _ => throw new ArgumentException($"sim: \"{w[1]}\" (live or scripted)"),
                        };
                        if (!_scene.ScriptedTics)
                            _scene.ClearQueuedTics();
                        break;
                    case "tics":
                        {
                            long until = _scene.TicsRun + Int(w[1]);
                            while (_scene.TicsRun < until && !(_scene.ScriptedTics && _scene.QueuedTics == 0) && _scene.World is not null)
                                await Frames(1);
                            break;
                        }
                    case "checksum":
                        await Drain();
                        PrintChecksum();
                        break;
                    case "smooth":
                        if (w.Length > 2)
                            await SmoothSector(Int(w[1]), Int(w[2]));
                        else
                            await Smooth(Int(w[1]));
                        break;
                    case "plane":
                        _scene.MovePlane(Int(w[1]), w[2] switch
                        {
                            "floor" => false,
                            "ceiling" => true,
                            _ => throw new ArgumentException($"plane: \"{w[2]}\" (floor or ceiling)"),
                        }, Int(w[3]), w.Length > 4 ? Int(w[4]) : 2);
                        break;
                    case "tictime": await TicTime(Int(w[1])); break;
                    case "route":
                        if (!QueueRoute(string.Join(' ', w[1..])))
                        {
                            GetTree().Quit(1);
                            return;
                        }
                        break;
                    case "map":
                        {
                            await Drain();
                            string now = _scene.Mesh?.Level.Name ?? "no map";
                            string status = _scene.World is { } world && world.players[world.consoleplayer] is { mo: not null } p
                                ? LevelScene.StatusText(world, p) : "no player";
                            GD.Print($"Level script: map {now} ({_scene.LevelsCompleted} completed by exits): {status}");
                            if (!string.Equals(now, w[1], StringComparison.OrdinalIgnoreCase))
                            {
                                GD.PrintErr($"Level script: map: expected {w[1].ToUpperInvariant()}, the scene shows {now}");
                                exit = 1;
                            }
                            if (w.Length > 2 && _scene.LevelsCompleted != Int(w[2]))
                            {
                                GD.PrintErr($"Level script: map: expected {Int(w[2])} map(s) completed by exits, there were {_scene.LevelsCompleted}");
                                exit = 1;
                            }
                            break;
                        }
                    case "joy":
                        Input.ParseInputEvent(new InputEventJoypadMotion { Device = 0, Axis = Enum.Parse<JoyAxis>(w[1], true), AxisValue = float.Parse(w[2], CultureInfo.InvariantCulture) });
                        break;
                    case "joybutton":
                        Input.ParseInputEvent(new InputEventJoypadButton { Device = 0, ButtonIndex = Enum.Parse<JoyButton>(w[1], true), Pressed = w[2] == "down", Pressure = w[2] == "down" ? 1 : 0 });
                        break;
                    case "quit": GetTree().Quit(exit); return;
                    default: throw new ArgumentException($"unknown command \"{w[0]}\"");
                }
            }
            catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or FormatException or OverflowException)
            {
                GD.PrintErr($"Level script: bad command \"{command}\": {e.Message}");
                GetTree().Quit(1);
                return;
            }
            await Frames(1); // let the event reach the nodes
        }
        GetTree().Quit(exit);
    }

    /// <summary>Waits until the scripted tics queued so far ran (a map without a player runs none).</summary>
    private async Task Drain()
    {
        while (_scene.QueuedTics > 0 && _scene.PlayerMobj is not null)
            await Frames(1);
    }

    private void PrintChecksum()
    {
        if (_scene.World is not { } world)
        {
            GD.PrintErr("Level script: checksum: no world");
            return;
        }
        string player = _scene.PlayerMobj is { } mo
            ? $"; player x {mo.x} y {mo.y} z {mo.z} ({mo.x / 65536.0:F2}, {mo.y / 65536.0:F2}, {mo.z / 65536.0:F2}) angle {mo.angle} momx {mo.momx} momy {mo.momy} {mo.state}"
            : "; no player";
        GD.Print($"Level script: checksum leveltime {world.leveltime} checksum {world.Checksum():x16}{player}");
    }

    /// <summary>Records the drawn player position over <paramref name="n"/> frames and prints the steps between frames (map units).</summary>
    private async Task Smooth(int n)
    {
        if (_scene.Player is not { } p)
            throw new ArgumentException("no player");
        var steps = new List<float>();
        Vector2 last = p.MapPosition;
        long tics = _scene.TicsRun;
        for (int i = 0; i < n; i++)
        {
            await Frames(1);
            Vector2 at = p.MapPosition;
            steps.Add(at.DistanceTo(last));
            last = at;
        }
        float jerk = 0; // the largest change between consecutive steps: small while the speed changes smoothly
        for (int i = 1; i < steps.Count; i++)
            jerk = Math.Max(jerk, Math.Abs(steps[i] - steps[i - 1]));
        steps.Sort();
        float sum = 0;
        foreach (float s in steps)
            sum += s;
        string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
        GD.Print($"Level script: smooth: {n} frames, {_scene.TicsRun - tics} tics; player step per frame min {F(steps[0])} median {F(steps[steps.Count / 2])} max {F(steps[^1])} mean {F(sum / steps.Count)} units, largest change between consecutive steps {F(jerk)}");
    }

    /// <summary>
    /// T5.1: records sector <paramref name="sector"/>'s drawn floor and ceiling
    /// (its data texel) over <paramref name="n"/> frames and prints the steps
    /// between frames (interpolation: even steps while a plane moves at a steady speed).
    /// </summary>
    private async Task SmoothSector(int n, int sector)
    {
        if (_scene.Mesh is not { } mesh || sector < 0 || sector >= mesh.Level.Sectors.Length)
            throw new ArgumentException($"no sector {sector}");
        var steps = new List<float>();
        Color last = mesh.SectorData(sector);
        long tics = _scene.TicsRun;
        for (int i = 0; i < n; i++)
        {
            await Frames(1);
            Color at = mesh.SectorData(sector);
            steps.Add(Math.Abs(at.R - last.R) + Math.Abs(at.G - last.G));
            last = at;
        }
        float jerk = 0;
        for (int i = 1; i < steps.Count; i++)
            jerk = Math.Max(jerk, Math.Abs(steps[i] - steps[i - 1]));
        steps.Sort();
        string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
        GD.Print($"Level script: smooth: sector {sector}, {n} frames, {_scene.TicsRun - tics} tics; drawn floor + ceiling step per frame min {F(steps[0])} "
            + $"median {F(steps[steps.Count / 2])} max {F(steps[^1])} units, largest change between consecutive steps {F(jerk)}; now floor {F(last.R)}, ceiling {F(last.G)}");
    }

    /// <summary>
    /// Times <see cref="World.G_Ticker(in ticcmd_t)"/> over the next
    /// <paramref name="n"/> tics (T4.9, SPEC §9: under 2 ms a tic) and prints
    /// the mean, median, 95th percentile and worst in ms.
    /// </summary>
    private async Task TicTime(int n)
    {
        if (_scene.World is not { } world)
            throw new ArgumentException("no world");
        var times = new List<double>(n);
        _scene.TicTimes = times;
        while (times.Count < n && !(_scene.ScriptedTics && _scene.QueuedTics == 0) && _scene.PlayerMobj is not null)
            await Frames(1);
        _scene.TicTimes = null;
        if (times.Count == 0)
        {
            GD.PrintErr("Level script: tictime: no tic ran");
            return;
        }
        double mean = 0;
        foreach (double t in times)
            mean += t;
        mean /= times.Count;
        times.Sort();
        string F(double v) => v.ToString("F4", CultureInfo.InvariantCulture);
        GD.Print($"Level script: tic time over {times.Count} tics, {System.Linq.Enumerable.Count(world.Mobjs())} mobjs: mean {F(mean)} ms, median {F(times[times.Count / 2])}, p95 {F(times[(int)(times.Count * 0.95)])}, worst {F(times[^1])}");
    }

    /// <summary>
    /// T5.10: queues the tics of the route file at <paramref name="path"/>
    /// (and places the player at its <c>start</c>); false, with an error,
    /// when the scene cannot play it as vanilla does.
    /// </summary>
    private bool QueueRoute(string path)
    {
        RouteFile route;
        try
        {
            route = RouteFile.Parse(path);
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or FormatException)
        {
            GD.PrintErr($"Level script: route: {e.Message}");
            return false;
        }
        string map = (route.Map ?? "E1M1").ToUpperInvariant();
        string? shown = _scene.Mesh?.Level.Name;
        var wrong = new List<string>();
        if (_scene.Tweaks != IsoDoom.Sim.Tweaks.Vanilla)
            wrong.Add("needs --level-tweaks=vanilla (a route is a vanilla demo)");
        if (!_scene.NoMonsters)
            wrong.Add("needs --level-monsters=off (routes are -nomonsters)");
        if ((int)_scene.Skill + 1 != route.Skill)
            wrong.Add($"needs --level-skill={route.Skill}");
        if (!string.Equals(shown, map, StringComparison.OrdinalIgnoreCase))
            wrong.Add($"is for {map}, the scene shows {shown ?? "no map"}");
        if (_scene.PlayerMobj is null)
            wrong.Add("no player");
        if (wrong.Count > 0)
        {
            GD.PrintErr($"Level script: route {path}: {string.Join("; ", wrong)}");
            return false;
        }
        if (route.Start is { } s && !_scene.PlaceRouteStart(s.X, s.Y, s.Angle))
        {
            GD.PrintErr($"Level script: route {path}: start {s.X} {s.Y}: something stands there");
            return false;
        }
        _scene.ScriptedTics = true;
        foreach (var cmd in route.Cmds)
            _scene.QueueTic(cmd);
        GD.Print($"Level script: route {path}: {route.Cmds.Count} tics queued on {map}{(route.Exit switch { 1 => ", ending at the exit", 2 => ", ending at the secret exit", _ => "" })}");
        return true;
    }

    /// <summary>The least share of the player's pixels (%) <c>visible</c> and <c>walkto</c> accept by default.</summary>
    public const int DefaultMinVisible = 25;

    /// <summary>Measures the player's visibility (%); null (and an error) when it is below <paramref name="min"/> or can't be measured.</summary>
    private async Task<double?> Visible(int min, bool print)
    {
        if (await _scene.PlayerVisibilityAsync() is not (int visible, int total))
        {
            GD.PrintErr("Level script: visible needs a real renderer and the player (run without --headless)");
            return null;
        }
        double share = total == 0 ? 0 : 100.0 * visible / total;
        Vector2 at = _scene.Player!.MapPosition;
        string where = $"player at ({at.X:F0}, {at.Y:F0}), z {_scene.Player.Z:F0}";
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
        if (_scene.Player is not { } p || _scene.PlayerMobj is null)
            throw new ArgumentException("no player");
        Vector2 from = p.MapPosition, to = new(x, y);
        int steps = Math.Max(1, (int)Math.Ceiling(from.DistanceTo(to) / Math.Max(1, step)));
        int failed = 0;
        double least = double.MaxValue;
        Vector2 leastAt = from;
        for (int i = 1; i <= steps; i++)
        {
            Vector2 at = from.Lerp(to, (float)i / steps);
            _scene.PlacePlayer(at.X, at.Y);
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
        if (_scene.Mesh is not { } m || _scene.Player is not { } p || _scene.PlayerMobj is null)
            throw new ArgumentException("no map or player");
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

    /// <summary>Prints wall-clock frame times over <paramref name="n"/> frames (T3.8; after a few frames to settle).</summary>
    private async Task FrameTime(int n)
    {
        await Frames(10);
        var ms = new double[Math.Max(1, n)];
        ulong last = Time.GetTicksUsec();
        for (int i = 0; i < ms.Length; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ulong now = Time.GetTicksUsec();
            ms[i] = (now - last) / 1000.0;
            last = now;
        }
        double mean = 0;
        foreach (double t in ms)
            mean += t;
        mean /= ms.Length;
        Array.Sort(ms);
        string F(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
        GD.Print($"Level script: frame time over {ms.Length} frames at {GetViewport().GetVisibleRect().Size}: mean {F(mean)} ms ({F(1000 / mean)} fps), median {F(ms[ms.Length / 2])}, p95 {F(ms[(int)(ms.Length * 0.95)])}, worst {F(ms[^1])}");
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
