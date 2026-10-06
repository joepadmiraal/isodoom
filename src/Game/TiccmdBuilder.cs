using System;
using IsoDoom.Map;
using IsoDoom.Sim;

namespace IsoDoom.Game;

/// <summary>
/// What the player asks for in one tic, as the devices report it (T4.6):
/// <c>GameInput</c> fills it from Godot's input actions. Plain C#
/// with no Godot types, so <see cref="TiccmdBuilder"/> is unit-tested
/// without the engine. Directions are relative to the screen: X right, Y up.
/// </summary>
public struct TiccmdInput
{
    /// <summary>
    /// Move, each axis in [−1, 1]: keys give −1, 0 or 1 per axis (so W+D is
    /// (1, 1)), the left stick its deflection past the dead zone.
    /// </summary>
    public float MoveX, MoveY;

    /// <summary>The right stick (aim), each axis in [−1, 1].</summary>
    public float AimX, AimY;

    /// <summary>The run key is held (Shift).</summary>
    public bool Run;

    /// <summary>The run toggle was pressed since the last tic (left-stick click): flips <see cref="TiccmdBuilder.RunToggled"/>.</summary>
    public bool RunToggle;

    /// <summary>Vanilla turning keys (used without <see cref="Tweaks.AbsoluteAiming"/>).</summary>
    public bool TurnLeft, TurnRight;

    /// <summary>The mouse moved since the last tic: the cursor takes over the aim from the stick.</summary>
    public bool CursorMoved;

    /// <summary>The cursor ground point (T3.3), map units, or null when the cursor is off the level.</summary>
    public (float X, float Y)? Cursor;

    /// <summary>Fire.</summary>
    public bool Attack;

    /// <summary>Use (open, switch).</summary>
    public bool Use;

    /// <summary>The weapon slot key pressed since the last tic, 1–8 (vanilla's keys '1'–'8'), or 0.</summary>
    public int Weapon;
}

/// <summary>Where the aim of <see cref="TiccmdBuilder"/> comes from.</summary>
public enum AimSource
{
    /// <summary>Nothing has aimed yet (a new level): the player keeps its angle.</summary>
    None,
    /// <summary>The mouse: the angle from the player to the cursor ground point, every tic.</summary>
    Cursor,
    /// <summary>The right stick: its direction while deflected, the last angle when released.</summary>
    Stick,
}

/// <summary>
/// The presentation's g_game.c <c>G_BuildTiccmd</c> (T4.6, SPEC §6.2):
/// turns a <see cref="TiccmdInput"/> into the player's <see cref="ticcmd_t"/>.
/// Floats stop here: the device values are turned into BAM angles and
/// <c>ticcmd</c> units, and the quantisation goes through the sim's integer
/// <see cref="Ticcmds"/> helpers.
/// <para>
/// With the twin-stick tweaks (<see cref="Tweaks.TopDown"/>) the move is
/// relative to the screen (<c>screenUp</c>: the world direction of screen
/// up, from the iso camera's yaw), at <see cref="Ticcmds.TwinStickSpeed"/>
/// scaled by the deflection (keyboard diagonals are normalised), encoded by
/// <see cref="Ticcmds.AbsoluteMove"/>; the aim is the angle from the player
/// to the cursor ground point or the right stick's direction, encoded by
/// <see cref="Ticcmds.AbsoluteAngle"/>. Every command carries an angle (an
/// absolute <c>angleturn</c> of 0 turns the player east): until something
/// aims, and while the cursor is off the level or on the player, the
/// player's own angle. Without <see cref="Tweaks.AbsoluteMovement"/> but with
/// <see cref="Tweaks.AbsoluteAiming"/> the screen-relative move is turned
/// into <c>forwardmove</c>/<c>sidemove</c> against the new facing. Vanilla
/// (both off) is g_game.c's: up/down are <c>forwardmove</c>, left/right
/// <c>sidemove</c> (strafe), the turning keys or the right stick's X turn
/// with <c>angleturn</c> and the slow first <see cref="Ticcmds.SLOWTURNTICS"/>.
/// </para>
/// </summary>
public sealed class TiccmdBuilder
{
    /// <summary>The right stick's deflection that aims (below it the last aim is kept, and vanilla does not turn).</summary>
    public const float StickAimThreshold = 0.5f;

    /// <summary>g_game.c <c>NUMWEAPONS</c> − 1: the weapon slot keys '1'–'8'.</summary>
    public const int WeaponSlots = 8;

    /// <summary>g_game.c <c>turnheld</c>: tics the turning keys have been held.</summary>
    private int turnheld;

    /// <summary>The run toggle's state (flipped by <see cref="TiccmdInput.RunToggle"/>); run = held XOR toggled.</summary>
    public bool RunToggled { get; set; }

    /// <summary>Where the aim comes from.</summary>
    public AimSource Aim { get; private set; }

    /// <summary>A new level (or a respawn): the player keeps its angle until something aims, and turning starts slow.</summary>
    public void Reset()
    {
        Aim = AimSource.None;
        turnheld = 0;
    }

    /// <summary>
    /// g_game.c <c>G_BuildTiccmd</c>: the command for one tic of <paramref name="input"/>,
    /// for a player at (<paramref name="playerX"/>, <paramref name="playerY"/>) (fixed_t)
    /// facing <paramref name="playerAngle"/> (BAM), under <paramref name="tweaks"/>,
    /// with screen up pointing to world direction <paramref name="screenUp"/> (BAM, 0 = east, <c>ANG90</c> = north).
    /// </summary>
    public ticcmd_t G_BuildTiccmd(in TiccmdInput input, Tweaks tweaks, uint screenUp, int playerX, int playerY, uint playerAngle)
    {
        var cmd = new ticcmd_t();
        if (input.RunToggle)
            RunToggled = !RunToggled;
        bool run = input.Run != RunToggled;
        int speed = run ? 1 : 0;

        // The aim.
        bool stickAims = input.AimX * input.AimX + input.AimY * input.AimY >= StickAimThreshold * StickAimThreshold;
        if (stickAims)
            Aim = AimSource.Stick;
        else if (input.CursorMoved)
            Aim = AimSource.Cursor;

        uint facing = playerAngle;
        if (tweaks.AbsoluteAiming)
        {
            uint aim = playerAngle;
            if (Aim == AimSource.Stick && stickAims)
                aim = ScreenToWorld(screenUp, input.AimX, input.AimY);
            else if (Aim == AimSource.Cursor && input.Cursor is { } c)
            {
                int cx = ToFixed(c.X), cy = ToFixed(c.Y);
                if (Math.Abs((long)cx - playerX) >= Fixed.FRACUNIT || Math.Abs((long)cy - playerY) >= Fixed.FRACUNIT)
                    aim = Tables.R_PointToAngle2(playerX, playerY, cx, cy);
            }
            cmd.angleturn = Ticcmds.AbsoluteAngle(aim);
            facing = (uint)(ushort)cmd.angleturn << 16; // what P_MovePlayer sets
        }
        else
        {
            // g_game.c: use two stage accelerative turning on the keyboard and joystick.
            bool turnRight = input.TurnRight || (stickAims && input.AimX >= StickAimThreshold);
            bool turnLeft = input.TurnLeft || (stickAims && input.AimX <= -StickAimThreshold);
            if (turnLeft || turnRight)
                turnheld++;
            else
                turnheld = 0;
            int tspeed = turnheld < Ticcmds.SLOWTURNTICS ? 2 : speed;
            if (turnRight)
                cmd.angleturn -= Ticcmds.angleturn[tspeed];
            if (turnLeft)
                cmd.angleturn += Ticcmds.angleturn[tspeed];
        }

        // The move.
        if (tweaks.AbsoluteMovement || tweaks.AbsoluteAiming)
        {
            float mx = Math.Clamp(input.MoveX, -1f, 1f), my = Math.Clamp(input.MoveY, -1f, 1f);
            float deflection = MathF.Min(1f, MathF.Sqrt(mx * mx + my * my));
            int move = (int)MathF.Round(Ticcmds.TwinStickSpeed(run) * deflection, MidpointRounding.AwayFromZero);
            if (move > 0)
            {
                uint direction = ScreenToWorld(screenUp, mx, my);
                // Against the facing: forward = move·cos(d − f) = move·sin(d − f + 90°),
                // side (right) = move·sin(f − d) = move·cos(d − f + 90°).
                Ticcmds.AbsoluteMove(ref cmd, tweaks.AbsoluteMovement ? direction : unchecked(direction - facing + Tables.ANG90), move);
            }
        }
        else
        {
            int forward = (int)MathF.Round(Ticcmds.forwardmove[speed] * Math.Clamp(input.MoveY, -1f, 1f), MidpointRounding.AwayFromZero);
            int side = (int)MathF.Round(Ticcmds.sidemove[speed] * Math.Clamp(input.MoveX, -1f, 1f), MidpointRounding.AwayFromZero);
            cmd.forwardmove = (sbyte)Math.Clamp(forward, -Ticcmds.MAXPLMOVE, Ticcmds.MAXPLMOVE);
            cmd.sidemove = (sbyte)Math.Clamp(side, -Ticcmds.MAXPLMOVE, Ticcmds.MAXPLMOVE);
        }

        // Buttons (g_game.c; the sim reads them from M5/M6).
        if (input.Attack)
            cmd.buttons |= buttoncode_t.BT_ATTACK;
        if (input.Use)
            cmd.buttons |= buttoncode_t.BT_USE;
        if (input.Weapon is >= 1 and <= WeaponSlots)
            cmd.buttons |= (byte)(buttoncode_t.BT_CHANGE | ((input.Weapon - 1) << buttoncode_t.BT_WEAPONSHIFT));
        return cmd;
    }

    /// <summary>
    /// The world direction (BAM) of the screen vector (<paramref name="x"/> right,
    /// <paramref name="y"/> up) when screen up points to <paramref name="screenUp"/>:
    /// screen right is 90° clockwise of it.
    /// </summary>
    public static uint ScreenToWorld(uint screenUp, float x, float y) =>
        unchecked(screenUp - Tables.ANG90 + BamOf(x, y));

    /// <summary>The BAM angle of the vector (<paramref name="x"/>, <paramref name="y"/>) (any scale; 0 for the zero vector), through <see cref="Tables.R_PointToAngle2"/>.</summary>
    public static uint BamOf(double x, double y)
    {
        double length = Math.Sqrt(x * x + y * y);
        if (!(length > 0))
            return 0;
        const double scale = 1 << 24; // the unit vector in fixed_t × 256: well inside R_PointToAngle2's precision
        return Tables.R_PointToAngle2(0, 0, (int)Math.Round(x / length * scale), (int)Math.Round(y / length * scale));
    }

    private static int ToFixed(float units) => (int)Math.Clamp(Math.Round(units * (double)Fixed.FRACUNIT), int.MinValue, int.MaxValue);
}
