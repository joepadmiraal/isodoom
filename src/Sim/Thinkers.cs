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
