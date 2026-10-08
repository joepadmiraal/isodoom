using System;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Wad;

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

    /// <summary>T7.3: the mouse's horizontal motion since the last tic, in pixels (right positive): vanilla's mouse turning.</summary>
    public float MouseX;

    /// <summary>The cursor ground point (T3.3), map units, or null when the cursor is off the level.</summary>
    public (float X, float Y)? Cursor;

    /// <summary>Fire.</summary>
    public bool Attack;

    /// <summary>Use (open, switch).</summary>
    public bool Use;

    /// <summary>The weapon slot key pressed since the last tic, 1–8 (vanilla's keys '1'–'8'), or 0.</summary>
    public int Weapon;

    /// <summary>T6.6: the next (+1) or previous (−1) weapon asked for since the last tic (the mouse wheel, LB/RB), or 0; it wins over <see cref="Weapon"/>.</summary>
    public int WeaponStep;

    /// <summary>T7.1: the pause key was pressed since the last tic (g_game.c <c>sendpause</c>).</summary>
    public bool Pause;
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

    /// <summary>
    /// T7.3: m_menu.c <c>mouseSensitivity</c> (0–9, default 5), which scales
    /// the mouse's turning without the absolute aiming (g_game.c
    /// <c>G_Responder</c>: <c>mousex = data2 * (mouseSensitivity + 5) / 10</c>).
    /// </summary>
    public int MouseSensitivity { get; set; } = 5;

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
    /// <para>
    /// T6.6: a <see cref="TiccmdInput.WeaponStep"/> picks the next or previous
    /// weapon <paramref name="player"/> can select (<see cref="G_NextWeapon"/>;
    /// without a player it is ignored).
    /// </para>
    public ticcmd_t G_BuildTiccmd(in TiccmdInput input, Tweaks tweaks, uint screenUp, int playerX, int playerY, uint playerAngle,
        player_t? player = null, GameMode gamemode = GameMode.shareware)
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

            // T7.3: g_game.c's mouse turning (G_Responder's mousex, G_BuildTiccmd's angleturn -= mousex*0x8);
            // its forward motion (mousey) is left out, as Chocolate Doom's novert: the mouse only turns
            int mousex = (int)(input.MouseX * (MouseSensitivity + 5) / 10);
            cmd.angleturn = (short)(cmd.angleturn - mousex * 0x8);
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
        if (input.WeaponStep != 0 && player is not null)
            cmd.buttons |= (byte)(buttoncode_t.BT_CHANGE | ((int)G_NextWeapon(player, gamemode, input.WeaponStep) << buttoncode_t.BT_WEAPONSHIFT));
        else if (input.Weapon is >= 1 and <= WeaponSlots)
            cmd.buttons |= (byte)(buttoncode_t.BT_CHANGE | ((input.Weapon - 1) << buttoncode_t.BT_WEAPONSHIFT));

        // special buttons (T7.1): the pause replaces the buttons, as vanilla's sendpause
        if (input.Pause)
            cmd.buttons = buttoncode_t.BT_SPECIAL | buttoncode_t.BTS_PAUSE;
        return cmd;
    }

    /// <summary>
    /// Chocolate Doom's g_game.c <c>weapon_order_table</c>: the weapons in
    /// cycling order, each with the weapon number its <c>BT_CHANGE</c> carries
    /// (p_user.c turns the fist into the chainsaw and the shotgun into the
    /// super shotgun when they are owned).
    /// </summary>
    private static readonly (weapontype_t weapon, weapontype_t weapon_num)[] weapon_order_table =
    [
        (weapontype_t.wp_fist, weapontype_t.wp_fist),
        (weapontype_t.wp_chainsaw, weapontype_t.wp_fist),
        (weapontype_t.wp_pistol, weapontype_t.wp_pistol),
        (weapontype_t.wp_shotgun, weapontype_t.wp_shotgun),
        (weapontype_t.wp_supershotgun, weapontype_t.wp_shotgun),
        (weapontype_t.wp_chaingun, weapontype_t.wp_chaingun),
        (weapontype_t.wp_missile, weapontype_t.wp_missile),
        (weapontype_t.wp_plasma, weapontype_t.wp_plasma),
        (weapontype_t.wp_bfg, weapontype_t.wp_bfg),
    ];

    /// <summary>Chocolate Doom's g_game.c <c>WeaponSelectable</c>: whether the next/previous weapon keys may stop at <paramref name="weapon"/>.</summary>
    public static bool WeaponSelectable(player_t player, GameMode gamemode, weapontype_t weapon)
    {
        // Can't select the super shotgun in Doom 1.
        if (weapon == weapontype_t.wp_supershotgun && gamemode != GameMode.commercial)
            return false;

        // These weapons aren't available in shareware.
        if ((weapon == weapontype_t.wp_plasma || weapon == weapontype_t.wp_bfg) && gamemode == GameMode.shareware)
            return false;

        // Can't select a weapon if we don't own it.
        if (!player.weaponowned[(int)weapon])
            return false;

        // Can't select the fist if we have the chainsaw, unless
        // we also have the berserk pack.
        if (weapon == weapontype_t.wp_fist
            && player.weaponowned[(int)weapontype_t.wp_chainsaw]
            && player.powers[(int)powertype_t.pw_strength] == 0)
            return false;

        return true;
    }

    /// <summary>
    /// Chocolate Doom's g_game.c <c>G_NextWeapon</c> (T6.6): the weapon number
    /// for <c>BT_CHANGE</c> of the next (<paramref name="direction"/> +1) or
    /// previous (−1) selectable weapon after the pending one (else the ready one), in
    /// <see cref="weapon_order_table"/>'s order, wrapping round.
    /// </summary>
    public static weapontype_t G_NextWeapon(player_t player, GameMode gamemode, int direction)
    {
        // Find index in the table.
        weapontype_t weapon = player.pendingweapon == weapontype_t.wp_nochange ? player.readyweapon : player.pendingweapon;

        int i;
        for (i = 0; i < weapon_order_table.Length; ++i)
        {
            if (weapon_order_table[i].weapon == weapon)
                break;
        }

        // Switch weapon. Don't loop forever.
        int start_i = i;
        do
        {
            i += direction;
            i = (i + weapon_order_table.Length) % weapon_order_table.Length;
        } while (i != start_i && !WeaponSelectable(player, gamemode, weapon_order_table[i].weapon));

        return weapon_order_table[i].weapon_num;
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
