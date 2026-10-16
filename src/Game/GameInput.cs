using System;
using Godot;
using IsoDoom.Render;

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

    /// <summary>T7.6: quicksave and quickload (F6, F9; Chocolate Doom's <c>key_menu_qsave</c>, <c>key_menu_qload</c>), read by the menus (<see cref="MMenu.M_QuickSave"/>).</summary>
    public const string QuickSave = "quicksave", QuickLoad = "quickload";

    /// <summary>The weapon slot actions <c>weapon_1</c>–<c>weapon_8</c>.</summary>
    public static string Weapon(int slot) => "weapon_" + slot;

    /// <summary>T7.3: the menus' actions (Chocolate Doom's <c>key_menu_*</c>, rebindable: <see cref="Settings.MenuActions"/>).</summary>
    public const string MenuOpen = "menu_open", MenuUp = "menu_up", MenuDown = "menu_down", MenuLeft = "menu_left", MenuRight = "menu_right";
    public const string MenuSelect = "menu_select", MenuBack = "menu_back", MenuYes = "menu_yes", MenuNo = "menu_no";

    /// <summary>T7.9: the automap's actions (Chocolate Doom's <c>key_map_*</c>: Tab, the arrows, = and -, 0, F, G, M, C; the pad's Y, right stick, D-pad, stick click, X and B).</summary>
    public const string MapToggle = "map_toggle", MapNorth = "map_north", MapSouth = "map_south", MapEast = "map_east", MapWest = "map_west";
    public const string MapZoomIn = "map_zoomin", MapZoomOut = "map_zoomout", MapMaxZoom = "map_maxzoom", MapFollow = "map_follow";
    public const string MapGrid = "map_grid", MapMark = "map_mark", MapClearMark = "map_clearmark";

    /// <summary>Every action the game reads, the menus' too (the level check fails when one is missing).</summary>
    public static string[] Actions()
    {
        var actions = new System.Collections.Generic.List<string>
        {
            MoveLeft, MoveRight, MoveUp, MoveDown, AimLeft, AimRight, AimUp, AimDown,
            Run, RunToggle, TurnLeft, TurnRight, Attack, Use, WeaponNext, WeaponPrev, Pause, QuickSave, QuickLoad,
        };
        for (int i = 1; i <= TiccmdBuilder.WeaponSlots; i++)
            actions.Add(Weapon(i));
        actions.AddRange([MenuOpen, MenuUp, MenuDown, MenuLeft, MenuRight, MenuSelect, MenuBack, MenuYes, MenuNo]);
        actions.AddRange([MapToggle, MapNorth, MapSouth, MapEast, MapWest, MapZoomIn, MapZoomOut, MapMaxZoom, MapFollow, MapGrid, MapMark, MapClearMark]);
        return [.. actions];
    }

    /// <summary>The actions missing from the <see cref="InputMap"/>.</summary>
    public static string[] MissingActions() => Array.FindAll(Actions(), a => !InputMap.HasAction(StringNames.Get(a)));

    /// <summary>
    /// How long after the aim stick was last deflected mouse motion is still
    /// taken for its echo, milliseconds (Steam Input, e.g. on the Steam Deck,
    /// may also move the mouse with the right stick).
    /// </summary>
    public const ulong StickEchoMs = 250;

    // T8.3: the actions read every frame as StringNames made once (a string passed to Input
    // makes a StringName at each call, garbage for the collector).
    private static readonly StringName _moveLeft = MoveLeft, _moveRight = MoveRight, _moveUp = MoveUp, _moveDown = MoveDown;
    private static readonly StringName _aimLeft = AimLeft, _aimRight = AimRight, _aimUp = AimUp, _aimDown = AimDown;
    private static readonly StringName _run = Run, _runToggle = RunToggle, _turnLeft = TurnLeft, _turnRight = TurnRight;
    private static readonly StringName _attack = Attack, _use = Use, _pause = Pause;
    private static readonly StringName[] _weapons = WeaponNames();

    private static StringName[] WeaponNames()
    {
        var names = new StringName[TiccmdBuilder.WeaponSlots + 1];
        for (int i = 1; i <= TiccmdBuilder.WeaponSlots; i++)
            names[i] = Weapon(i);
        return names;
    }

    private readonly InputLatches _latches = new();
    private bool _cursorMoved;
    private float _mouseX;
    private ulong? _aimStickAt;

    /// <summary>The move axes, screen-relative (X right, Y up), each in [−1, 1]; keys give whole steps (W+D = (1, 1)).</summary>
    public static Vector2 Move() => new(Input.GetAxis(_moveLeft, _moveRight), Input.GetAxis(_moveDown, _moveUp));

    /// <summary>The aim stick's axes, screen-relative (X right, Y up), each in [−1, 1], past the actions' dead zone.</summary>
    public static Vector2 AimStick() => new(Input.GetAxis(_aimLeft, _aimRight), Input.GetAxis(_aimDown, _aimUp));

    /// <summary>
    /// Whether the pad aims: the aim stick is deflected now, or <see cref="StickEchoMs"/>
    /// ago (call every frame, <see cref="Poll"/> does). Mouse motion meanwhile
    /// is the stick's echo, not a mouse's (<see cref="LevelScene"/> drops it).
    /// </summary>
    public bool AimStickActive()
    {
        ulong now = Time.GetTicksMsec();
        if (AimStick() != Vector2.Zero)
            _aimStickAt = now;
        return _aimStickAt is ulong at && now - at < StickEchoMs;
    }

    /// <summary>The run key is held.</summary>
    public static bool RunHeld() => Input.IsActionPressed(_run);

    /// <summary>Records mouse motion (call from <c>_Input</c>): the cursor takes over the aim; <paramref name="dx"/> (pixels) turns as vanilla's mouse (T7.3).</summary>
    public void CursorMoved(float dx = 0)
    {
        _cursorMoved = true;
        _mouseX += dx;
    }

    /// <summary>
    /// T6.6: latches a next or previous weapon press from an input event (call
    /// from <c>_UnhandledInput</c>: a wheel step is pressed and released at
    /// once, so polling would miss it). Returns whether it was one.
    /// </summary>
    public bool WeaponEvent(InputEvent e)
    {
        if (e.IsActionPressed(StringNames.Get(WeaponNext)))
            _latches.StepWeapon(1);
        else if (e.IsActionPressed(StringNames.Get(WeaponPrev)))
            _latches.StepWeapon(-1);
        else
            return false;
        return true;
    }

    /// <summary>Latches this frame's presses (call every frame while the game reads input; again per tic is fine, <see cref="InputLatches"/>).</summary>
    public void Poll()
    {
        AimStickActive();
        int weapon = 0;
        for (int i = 1; i <= TiccmdBuilder.WeaponSlots; i++)
        {
            if (Input.IsActionJustPressed(_weapons[i]))
            {
                weapon = i;
                break;
            }
        }
        _latches.Poll(Engine.GetProcessFrames(), Input.IsActionPressed(_attack), Input.IsActionPressed(_use), Input.IsActionJustPressed(_runToggle), weapon, Input.IsActionJustPressed(_pause));
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
            AimX = Input.GetAxis(_aimLeft, _aimRight),
            AimY = Input.GetAxis(_aimDown, _aimUp),
            Run = RunHeld(),
            RunToggle = _latches.RunToggle,
            TurnLeft = Input.IsActionPressed(_turnLeft),
            TurnRight = Input.IsActionPressed(_turnRight),
            CursorMoved = _cursorMoved,
            MouseX = _mouseX,
            Cursor = cursor,
            Attack = _latches.Attack,
            Use = _latches.Use,
            Weapon = _latches.Weapon,
            WeaponStep = _latches.WeaponStep,
            Pause = _latches.Pause,
        };
        _latches.Clear();
        _cursorMoved = false;
        _mouseX = 0;
        return input;
    }

    /// <summary>The world direction (BAM) of screen up for a camera whose ground-up vector is <paramref name="groundUp"/> (Godot space; map x = X, map y = −Z).</summary>
    public static uint ScreenUp(Vector3 groundUp) => TiccmdBuilder.BamOf(groundUp.X, -groundUp.Z);
}
