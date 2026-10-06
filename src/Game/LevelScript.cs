using System;
using System.Globalization;
using System.Threading.Tasks;
using Godot;

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
/// out of the repo); <c>quit</c> (also implied at the end).
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
