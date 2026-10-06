namespace IsoDoom.Sim;

/// <summary>
/// d_think.h <c>think_t</c>/<c>actionf_t</c> as the thinker list uses it:
/// which function runs a thinker each tic. Vanilla stores a function pointer;
/// an enum keeps it comparable, deterministic and serialisable, and
/// <see cref="World.P_RunThinkers"/> dispatches on it.
/// </summary>
public enum think_t
{
    /// <summary>No function: the thinker stays in the list but does nothing (vanilla's NULL, e.g. a ceiling in stasis).</summary>
    NULL,

    /// <summary>
    /// Vanilla's <c>(actionf_v)(-1)</c>: removed by <see cref="World.P_RemoveThinker"/>;
    /// <see cref="World.P_RunThinkers"/> unlinks it when it reaches it.
    /// </summary>
    REMOVED,

    /// <summary>p_mobj.c <c>P_MobjThinker</c> (every <see cref="mobj_t"/>).</summary>
    P_MobjThinker,

    /// <summary>p_doors.c <c>T_VerticalDoor</c> (a <see cref="vldoor_t"/>, T5.3).</summary>
    T_VerticalDoor,

    /// <summary>p_floor.c <c>T_MoveFloor</c> (a <see cref="floormove_t"/>, T5.5).</summary>
    T_MoveFloor,

    /// <summary>p_plats.c <c>T_PlatRaise</c> (a <see cref="plat_t"/>, T5.5).</summary>
    T_PlatRaise,

    /// <summary>p_ceilng.c <c>T_MoveCeiling</c> (a <see cref="ceiling_t"/>, T5.5).</summary>
    T_MoveCeiling,

    /// <summary>p_lights.c <c>T_FireFlicker</c> (a <see cref="fireflicker_t"/>, T5.7).</summary>
    T_FireFlicker,

    /// <summary>p_lights.c <c>T_LightFlash</c> (a <see cref="lightflash_t"/>, T5.7).</summary>
    T_LightFlash,

    /// <summary>p_lights.c <c>T_StrobeFlash</c> (a <see cref="strobe_t"/>, T5.7).</summary>
    T_StrobeFlash,

    /// <summary>p_lights.c <c>T_Glow</c> (a <see cref="glow_t"/>, T5.7).</summary>
    T_Glow,
}

/// <summary>
/// d_think.h <c>thinker_t</c>: a node of the doubly linked thinker list
/// (vanilla's first member of every thinker struct; here the base class).
/// </summary>
public class thinker_t
{
    public thinker_t prev = null!;
    public thinker_t next = null!;
    public think_t function;
}
