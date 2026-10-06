using System;
using IsoDoom.Map;

namespace IsoDoom.Sim;

/// <summary>
/// d_ticcmd.h <c>ticcmd_t</c>: one player's input for one tic, the only
/// input the sim takes (SPEC §6.1). <see cref="World.G_Ticker(ticcmd_t[])"/>
/// copies it into <see cref="player_t.cmd"/>; p_user.c <c>P_MovePlayer</c> reads it.
/// <para>
/// Vanilla meaning: <see cref="forwardmove"/>/<see cref="sidemove"/> thrust
/// along and right of the player's facing (×2048 fixed_t a tic),
/// <see cref="angleturn"/> turns the player by <c>angleturn &lt;&lt; 16</c>.
/// The twin-stick tweaks (SPEC §6.3 #1, §12 T4.5) change the reading:
/// with <see cref="Tweaks.AbsoluteAiming"/> <see cref="angleturn"/> is the
/// absolute angle's upper 16 bits, with <see cref="Tweaks.AbsoluteMovement"/>
/// <see cref="forwardmove"/> thrusts north (+y) and <see cref="sidemove"/> east (+x),
/// whatever the facing. <see cref="Ticcmds"/> has the builder's helpers.
/// </para>
/// </summary>
public struct ticcmd_t
{
    /// <summary>*2048 for move (vanilla's <c>signed char</c>).</summary>
    public sbyte forwardmove;
    /// <summary>*2048 for move (vanilla's <c>signed char</c>).</summary>
    public sbyte sidemove;
    /// <summary>&lt;&lt;16 for angle delta.</summary>
    public short angleturn;
    /// <summary>Checks for net game.</summary>
    public short consistancy;
    public byte chatchar;
    public byte buttons;
}

/// <summary>d_event.h <c>buttoncode_t</c>: the bits of <see cref="ticcmd_t.buttons"/>.</summary>
public static class buttoncode_t
{
    /// <summary>Press "Fire".</summary>
    public const byte BT_ATTACK = 1;
    /// <summary>Use button, to open doors, activate switches.</summary>
    public const byte BT_USE = 2;

    /// <summary>Flag: game events, not really buttons.</summary>
    public const byte BT_SPECIAL = 128;
    public const byte BT_SPECIALMASK = 3;

    /// <summary>Flag, weapon change pending. If true, the next 3 bits hold weapon num.</summary>
    public const byte BT_CHANGE = 4;
    /// <summary>The 3bit weapon mask and shift, convenience.</summary>
    public const byte BT_WEAPONMASK = 8 + 16 + 32;
    public const int BT_WEAPONSHIFT = 3;

    /// <summary>Pause the game.</summary>
    public const byte BTS_PAUSE = 1;
    /// <summary>Save the game at each console.</summary>
    public const byte BTS_SAVEGAME = 2;

    /// <summary>Savegame slot numbers occupy the second byte of buttons.</summary>
    public const byte BTS_SAVEMASK = 4 + 8 + 16;
    public const int BTS_SAVESHIFT = 2;
}

/// <summary>
/// The deterministic part of building a <see cref="ticcmd_t"/>: g_game.c's
/// speed tables (for <c>G_BuildTiccmd</c>, T4.6) and the twin-stick
/// encodings (SPEC §6.2, §12 T4.5). The presentation's builder decides what
/// the player wants (keys, cursor, sticks) and encodes it with these, so the
/// quantisation into the <c>ticcmd</c> is integer and the same everywhere.
/// </summary>
public static class Ticcmds
{
    /// <summary>g_game.c <c>forwardmove</c>: walk, run.</summary>
    public static ReadOnlySpan<sbyte> forwardmove => new sbyte[] { 0x19, 0x32 };

    /// <summary>g_game.c <c>sidemove</c>: walk, run.</summary>
    public static ReadOnlySpan<sbyte> sidemove => new sbyte[] { 0x18, 0x28 };

    /// <summary>g_game.c <c>angleturn</c>: walk, run, slow turn (the first <see cref="SLOWTURNTICS"/> tics).</summary>
    public static ReadOnlySpan<short> angleturn => new short[] { 640, 1280, 320 };

    /// <summary>g_game.c <c>MAXPLMOVE</c>: <c>forwardmove[1]</c>, the builder's clamp on both moves.</summary>
    public const int MAXPLMOVE = 0x32;

    /// <summary>g_game.c <c>SLOWTURNTICS</c>.</summary>
    public const int SLOWTURNTICS = 6;

    /// <summary>
    /// The twin-stick move speed (SPEC §6.2): every direction moves at
    /// vanilla's forward speed, <c>forwardmove[run]</c> (25 or 50), so
    /// diagonals and sideways moves are neither faster nor slower than straight ahead.
    /// </summary>
    public static int TwinStickSpeed(bool run) => forwardmove[run ? 1 : 0];

    /// <summary>
    /// <see cref="Tweaks.AbsoluteAiming"/>: the absolute angle <paramref name="angle"/>
    /// (BAM) as <see cref="ticcmd_t.angleturn"/>, rounded to the nearest 16-bit angle.
    /// </summary>
    public static short AbsoluteAngle(uint angle) => unchecked((short)((angle + 0x8000) >> 16));

    /// <summary>
    /// <see cref="Tweaks.AbsoluteMovement"/>: a move of <paramref name="speed"/>
    /// (in <see cref="ticcmd_t.forwardmove"/> units, at most 127) in the world
    /// direction <paramref name="direction"/> (BAM, 0 = east, <c>ANG90</c> =
    /// north): <see cref="ticcmd_t.forwardmove"/> = speed × sin,
    /// <see cref="ticcmd_t.sidemove"/> = speed × cos, each rounded to the
    /// nearest integer, so the length is <paramref name="speed"/> within half
    /// a unit per axis in every direction. This is where diagonals are
    /// normalised (SPEC §12 T4.5); the sim applies the vector as it is.
    /// </summary>
    public static void AbsoluteMove(ref ticcmd_t cmd, uint direction, int speed)
    {
        int a = (int)(direction >> Tables.ANGLETOFINESHIFT);
        cmd.forwardmove = (sbyte)((speed * Tables.finesine[a] + Fixed.FRACUNIT / 2) >> Fixed.FRACBITS);
        cmd.sidemove = (sbyte)((speed * Tables.finecosine[a] + Fixed.FRACUNIT / 2) >> Fixed.FRACBITS);
    }
}
