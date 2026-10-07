using System;
using Godot;

namespace IsoDoom.Game;

/// <summary>
/// The game's input actions (T4.6, SPEC §6.2), defined in <c>project.godot</c>'s
/// <c>[input]</c> section so M7's rebinding can change them, and their reading
/// into a <see cref="TiccmdInput"/> for <see cref="TiccmdBuilder"/>.
/// <see cref="Poll"/> runs every frame and latches presses (fire, use, a weapon
/// key, the run toggle, mouse motion) so a tap between two tics is not lost;
/// <see cref="Take"/> hands one tic's input over and clears the latches.
/// </summary>
public sealed class GameInput
{
    public const string MoveLeft = "move_left", MoveRight = "move_right", MoveUp = "move_up", MoveDown = "move_down";
    public const string AimLeft = "aim_left", AimRight = "aim_right", AimUp = "aim_up", AimDown = "aim_down";
    public const string Run = "run", RunToggle = "run_toggle";
    public const string TurnLeft = "turn_left", TurnRight = "turn_right";
    public const string Attack = "attack", Use = "use";

    /// <summary>T6.6: the next and previous weapon (the mouse wheel, LB/RB; SPEC §6.2).</summary>
    public const string WeaponNext = "weapon_next", WeaponPrev = "weapon_prev";

    /// <summary>T7.1: the pause (Pause, the pad's Back; g_game.c <c>key_pause</c>).</summary>
    public const string Pause = "pause";

    /// <summary>The weapon slot actions <c>weapon_1</c>–<c>weapon_8</c>.</summary>
    public static string Weapon(int slot) => "weapon_" + slot;

    /// <summary>Every action the game reads (the level check fails when one is missing).</summary>
    public static string[] Actions()
    {
        var actions = new System.Collections.Generic.List<string>
        {
            MoveLeft, MoveRight, MoveUp, MoveDown, AimLeft, AimRight, AimUp, AimDown,
            Run, RunToggle, TurnLeft, TurnRight, Attack, Use, WeaponNext, WeaponPrev, Pause,
        };
        for (int i = 1; i <= TiccmdBuilder.WeaponSlots; i++)
            actions.Add(Weapon(i));
        return actions.ToArray();
    }

    /// <summary>The actions missing from the <see cref="InputMap"/>.</summary>
    public static string[] MissingActions() => Array.FindAll(Actions(), a => !InputMap.HasAction(a));

    private readonly InputLatches _latches = new();
    private bool _cursorMoved;

    /// <summary>The move axes, screen-relative (X right, Y up), each in [−1, 1]; keys give whole steps (W+D = (1, 1)).</summary>
    public static Vector2 Move() => new(Input.GetAxis(MoveLeft, MoveRight), Input.GetAxis(MoveDown, MoveUp));

    /// <summary>The run key is held.</summary>
    public static bool RunHeld() => Input.IsActionPressed(Run);

    /// <summary>Records mouse motion (call from <c>_Input</c>): the cursor takes over the aim.</summary>
    public void CursorMoved() => _cursorMoved = true;

    /// <summary>
    /// T6.6: latches a next or previous weapon press from an input event (call
    /// from <c>_UnhandledInput</c>: a wheel step is pressed and released at
    /// once, so polling would miss it). Returns whether it was one.
    /// </summary>
    public bool WeaponEvent(InputEvent e)
    {
        if (e.IsActionPressed(WeaponNext))
            _latches.StepWeapon(1);
        else if (e.IsActionPressed(WeaponPrev))
            _latches.StepWeapon(-1);
        else
            return false;
        return true;
    }

    /// <summary>Latches this frame's presses (call every frame while the game reads input; again per tic is fine, <see cref="InputLatches"/>).</summary>
    public void Poll()
    {
        int weapon = 0;
        for (int i = 1; i <= TiccmdBuilder.WeaponSlots; i++)
        {
            if (Input.IsActionJustPressed(Weapon(i)))
            {
                weapon = i;
                break;
            }
        }
        _latches.Poll(Engine.GetProcessFrames(), Input.IsActionPressed(Attack), Input.IsActionPressed(Use), Input.IsActionJustPressed(RunToggle), weapon, Input.IsActionJustPressed(Pause));
    }

    /// <summary>
    /// One tic's input: the held actions now, the presses latched since the
    /// last call, and <paramref name="cursor"/> (the cursor ground point, map units, or null).
    /// </summary>
    public TiccmdInput Take((float X, float Y)? cursor)
    {
        Poll();
        Vector2 move = Move();
        var input = new TiccmdInput
        {
            MoveX = move.X,
            MoveY = move.Y,
            AimX = Input.GetAxis(AimLeft, AimRight),
            AimY = Input.GetAxis(AimDown, AimUp),
            Run = RunHeld(),
            RunToggle = _latches.RunToggle,
            TurnLeft = Input.IsActionPressed(TurnLeft),
            TurnRight = Input.IsActionPressed(TurnRight),
            CursorMoved = _cursorMoved,
            Cursor = cursor,
            Attack = _latches.Attack,
            Use = _latches.Use,
            Weapon = _latches.Weapon,
            WeaponStep = _latches.WeaponStep,
            Pause = _latches.Pause,
        };
        _latches.Clear();
        _cursorMoved = false;
        return input;
    }

    /// <summary>The world direction (BAM) of screen up for a camera whose ground-up vector is <paramref name="groundUp"/> (Godot space; map x = X, map y = −Z).</summary>
    public static uint ScreenUp(Vector3 groundUp) => TiccmdBuilder.BamOf(groundUp.X, -groundUp.Z);
}
