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
