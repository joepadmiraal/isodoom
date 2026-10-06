namespace IsoDoom.Sim;

/// <summary>
/// d_ticcmd.h <c>ticcmd_t</c>: one player's input for one tic, the only
/// input the sim takes (SPEC §6.1). T4.4 needs <see cref="forwardmove"/> and
/// <see cref="sidemove"/> (<c>P_XYMovement</c>'s stop test); T4.5 drives the
/// player with it and adds the twin-stick reading of <see cref="angleturn"/>.
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
