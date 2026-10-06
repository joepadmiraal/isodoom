// p_spec.h's action types, as the line specials pass them to the EV_
// functions (T5.2). Names, order and values are vanilla's.

using IsoDoom.Map;

namespace IsoDoom.Sim;

/// <summary>p_spec.h <c>vldoor_e</c>: what a door special does (p_doors.c <c>EV_DoDoor</c>).</summary>
public enum vldoor_e
{
    vld_normal,
    vld_close30ThenOpen,
    vld_close,
    vld_open,
    vld_raiseIn5Mins,
    vld_blazeRaise,
    vld_blazeOpen,
    vld_blazeClose,
}

/// <summary>p_spec.h <c>floor_e</c>: what a floor special does (p_floor.c <c>EV_DoFloor</c>).</summary>
public enum floor_e
{
    /// <summary>lower floor to highest surrounding floor</summary>
    lowerFloor,

    /// <summary>lower floor to lowest surrounding floor</summary>
    lowerFloorToLowest,

    /// <summary>lower floor to highest surrounding floor VERY FAST</summary>
    turboLower,

    /// <summary>raise floor to lowest surrounding CEILING</summary>
    raiseFloor,

    /// <summary>raise floor to next highest surrounding floor</summary>
    raiseFloorToNearest,

    /// <summary>raise floor to shortest height texture around it</summary>
    raiseToTexture,

    /// <summary>lower floor to lowest surrounding floor and change floorpic</summary>
    lowerAndChange,

    raiseFloor24,
    raiseFloor24AndChange,
    raiseFloorCrush,

    /// <summary>raise to next highest floor, turbo-speed</summary>
    raiseFloorTurbo,
    donutRaise,
    raiseFloor512,
}

/// <summary>p_spec.h <c>stair_e</c> (p_floor.c <c>EV_BuildStairs</c>).</summary>
public enum stair_e
{
    /// <summary>slowly build by 8</summary>
    build8,

    /// <summary>quickly build by 16</summary>
    turbo16,
}

/// <summary>p_spec.h <c>ceiling_e</c>: what a ceiling special does (p_ceilng.c <c>EV_DoCeiling</c>).</summary>
public enum ceiling_e
{
    lowerToFloor,
    raiseToHighest,
    lowerAndCrush,
    crushAndRaise,
    fastCrushAndRaise,
    silentCrushAndRaise,
}

/// <summary>p_spec.h <c>plattype_e</c>: what a lift special does (p_plats.c <c>EV_DoPlat</c>).</summary>
public enum plattype_e
{
    perpetualRaise,
    downWaitUpStay,
    raiseAndChange,
    raiseToNearestAndChange,
    blazeDWUS,
}

/// <summary>p_spec.h light flash levels for <c>P_SpawnStrobeFlash</c> (p_lights.c).</summary>
public static class LightFlash
{
    /// <summary>p_spec.h <c>GLOWSPEED</c>.</summary>
    public const int GLOWSPEED = 8;

    /// <summary>p_spec.h <c>STROBEBRIGHT</c>.</summary>
    public const int STROBEBRIGHT = 5;

    /// <summary>p_spec.h <c>FASTDARK</c>.</summary>
    public const int FASTDARK = 15;

    /// <summary>p_spec.h <c>SLOWDARK</c>.</summary>
    public const int SLOWDARK = 35;
}

/// <summary>p_spec.h <c>VDOORSPEED</c>, <c>VDOORWAIT</c>: door speed (fixed_t a tic) and the tics it waits open.</summary>
public static class VDoor
{
    /// <summary>p_spec.h <c>VDOORSPEED</c>: 2 units a tic.</summary>
    public const int VDOORSPEED = Fixed.FRACUNIT * 2;

    /// <summary>p_spec.h <c>VDOORWAIT</c>: 150 tics (about 4 seconds).</summary>
    public const int VDOORWAIT = 150;
}

/// <summary>p_spec.h <c>vldoor_t</c>: a door's thinker (p_doors.c, T5.3).</summary>
public sealed class vldoor_t : thinker_t
{
    public vldoor_e type;
    public sector_t sector = null!;

    /// <summary>fixed_t.</summary>
    public int topheight;

    /// <summary>fixed_t a tic.</summary>
    public int speed;

    /// <summary>1 = up, 0 = waiting at top, -1 = down (2 = the initial wait of <see cref="vldoor_e.vld_raiseIn5Mins"/>).</summary>
    public int direction;

    /// <summary>tics to wait at the top</summary>
    public int topwait;

    /// <summary>
    /// (keep in case a door going down is reset)
    /// when it reaches 0, start going down
    /// </summary>
    public int topcountdown;
}

/// <summary>p_spec.h <c>result_e</c>: what <c>T_MovePlane</c> did (p_floor.c).</summary>
public enum result_e
{
    ok,
    crushed,
    pastdest,
}

/// <summary>p_spec.h <c>bwhere_e</c>: which texture of a button's front side is switched (p_switch.c, T5.4).</summary>
public enum bwhere_e
{
    top,
    middle,
    bottom,
}

/// <summary>
/// p_spec.h <c>button_t</c>: a pressed button (a switch usable again) waiting
/// to switch its texture back (p_switch.c <c>P_StartButton</c>, counted down
/// by <c>P_UpdateSpecials</c>; T5.4). <see cref="btexture"/> is a texture
/// name (the sim keeps names); <see cref="soundorg"/> the sector whose sound
/// origin vanilla points at (the line's front sector), null for none.
/// </summary>
public sealed class button_t
{
    public line_t? line;
    public bwhere_e where;
    public string? btexture;
    public int btimer;
    public sector_t? soundorg;

    /// <summary><c>memset(&amp;buttonlist[i], 0, sizeof(button_t))</c>.</summary>
    public void Clear()
    {
        line = null;
        where = bwhere_e.top;
        btexture = null;
        btimer = 0;
        soundorg = null;
    }
}

/// <summary>p_spec.h <c>plat_e</c>: a lift's state (p_plats.c, T5.5).</summary>
public enum plat_e
{
    up,
    down,
    waiting,
    in_stasis,
}

/// <summary>p_spec.h lift constants (p_plats.c, T5.5).</summary>
public static class Plat
{
    /// <summary>p_spec.h <c>PLATWAIT</c>: seconds a lift waits (× <see cref="TICRATE"/>).</summary>
    public const int PLATWAIT = 3;

    /// <summary>p_spec.h <c>PLATSPEED</c>: 1 unit a tic.</summary>
    public const int PLATSPEED = Fixed.FRACUNIT;

    /// <summary>p_spec.h <c>MAXPLATS</c>: the size of <c>activeplats</c> (a 31st active lift is an error, as vanilla).</summary>
    public const int MAXPLATS = 30;

    /// <summary>doomdef.h <c>TICRATE</c>.</summary>
    public const int TICRATE = 35;
}

/// <summary>
/// p_spec.h <c>plat_t</c>: a lift's thinker (p_plats.c <c>EV_DoPlat</c>, T5.5).
/// Field order is vanilla's; <see cref="wait"/> is where a door's
/// <c>direction</c> lies (<c>EV_VerticalDoor</c> on a lift, SPEC §12 T5.5).
/// </summary>
public sealed class plat_t : thinker_t
{
    public sector_t sector = null!;

    /// <summary>fixed_t a tic.</summary>
    public int speed;

    /// <summary>fixed_t.</summary>
    public int low;

    /// <summary>fixed_t.</summary>
    public int high;

    public int wait;
    public int count;
    public plat_e status;
    public plat_e oldstatus;
    public bool crush;
    public int tag;
    public plattype_e type;
}

/// <summary>p_spec.h <c>FLOORSPEED</c>: 1 unit a tic (p_floor.c, T5.5).</summary>
public static class FloorMove
{
    /// <summary>p_spec.h <c>FLOORSPEED</c>.</summary>
    public const int FLOORSPEED = Fixed.FRACUNIT;
}

/// <summary>
/// p_spec.h <c>floormove_t</c>: a moving floor's thinker (p_floor.c
/// <c>EV_DoFloor</c>, <c>EV_BuildStairs</c>, p_spec.c <c>EV_DoDonut</c>; T5.5).
/// <see cref="texture"/> is a flat name (the sim keeps names); null is
/// vanilla's flat -1 that <c>EV_VerticalDoor</c> can write there (SPEC §12
/// T5.5), which then leaves the floor's flat as it is.
/// </summary>
public sealed class floormove_t : thinker_t
{
    public floor_e type;
    public bool crush;
    public sector_t sector = null!;

    /// <summary>1 up, -1 down.</summary>
    public int direction;

    public int newspecial;
    public string? texture;

    /// <summary>fixed_t.</summary>
    public int floordestheight;

    /// <summary>fixed_t a tic.</summary>
    public int speed;

    /// <summary>
    /// Not vanilla: the door direction <c>EV_VerticalDoor</c> last wrote over
    /// <see cref="texture"/> (and its padding), 0 for none (SPEC §12 T5.5).
    /// </summary>
    public int doordirection;
}

/// <summary>p_spec.h ceiling constants (p_ceilng.c, T5.5).</summary>
public static class CeilingMove
{
    /// <summary>p_spec.h <c>CEILSPEED</c>: 1 unit a tic.</summary>
    public const int CEILSPEED = Fixed.FRACUNIT;

    /// <summary>p_spec.h <c>CEILWAIT</c> (unused, as in vanilla).</summary>
    public const int CEILWAIT = 150;

    /// <summary>p_spec.h <c>MAXCEILINGS</c>: the size of <c>activeceilings</c> (a 31st is not listed, as vanilla).</summary>
    public const int MAXCEILINGS = 30;
}

/// <summary>
/// p_spec.h <c>ceiling_t</c>: a moving ceiling's thinker (p_ceilng.c
/// <c>EV_DoCeiling</c>, T5.5). <see cref="crush"/> is vanilla's
/// <c>boolean</c> (an int: <c>EV_VerticalDoor</c> on a ceiling writes a
/// door's direction, -1 or 1, there; nonzero crushes; SPEC §12 T5.5).
/// </summary>
public sealed class ceiling_t : thinker_t
{
    public ceiling_e type;
    public sector_t sector = null!;

    /// <summary>fixed_t.</summary>
    public int bottomheight;

    /// <summary>fixed_t.</summary>
    public int topheight;

    /// <summary>fixed_t a tic.</summary>
    public int speed;

    public int crush;

    /// <summary>1 = up, 0 = waiting, -1 = down</summary>
    public int direction;

    /// <summary>ID</summary>
    public int tag;

    public int olddirection;
}

/// <summary>p_spec.h <c>fireflicker_t</c>: a flickering fire light's thinker (p_lights.c <c>P_SpawnFireFlicker</c>, T5.7).</summary>
public sealed class fireflicker_t : thinker_t
{
    public sector_t sector = null!;
    public int count;
    public int maxlight;
    public int minlight;
}

/// <summary>p_spec.h <c>lightflash_t</c>: a broken, randomly flashing light's thinker (p_lights.c <c>P_SpawnLightFlash</c>, T5.7).</summary>
public sealed class lightflash_t : thinker_t
{
    public sector_t sector = null!;
    public int count;
    public int maxlight;
    public int minlight;
    public int maxtime;
    public int mintime;
}

/// <summary>p_spec.h <c>strobe_t</c>: a strobe light's thinker (p_lights.c <c>P_SpawnStrobeFlash</c>, T5.7).</summary>
public sealed class strobe_t : thinker_t
{
    public sector_t sector = null!;
    public int count;
    public int minlight;
    public int maxlight;
    public int darktime;
    public int brighttime;
}

/// <summary>p_spec.h <c>glow_t</c>: a glowing light's thinker (p_lights.c <c>P_SpawnGlowingLight</c>, T5.7).</summary>
public sealed class glow_t : thinker_t
{
    public sector_t sector = null!;
    public int minlight;
    public int maxlight;

    /// <summary>-1 down, 1 up.</summary>
    public int direction;
}
