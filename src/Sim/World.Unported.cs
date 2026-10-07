using System.Collections.Generic;

namespace IsoDoom.Sim;

// Stubs for what the ported code calls that later tasks port: the weapon
// drop on death (p_pspr.c, T6.6). Each records its call in unported and does
// nothing else; each task moves its functions to their own file and deletes
// their stubs here (doors: World.Doors.cs, T5.3; floors, lifts, ceilings and
// stairs: World.Floor.cs, World.Plats.cs, World.Ceiling.cs, T5.5;
// teleports: World.Telept.cs, T5.6; lights: World.Lights.cs, T5.7; exits:
// World.Game.cs, T5.8).
public sealed partial class World
{
    /// <summary>
    /// Not vanilla: the calls to functions not ported yet, in order, as
    /// <c>P_DropWeapon()</c>, so tests and the level scene can see them.
    /// Cleared by <see cref="P_SetupLevel"/>; not sim state (outside the checksum).
    /// </summary>
    public readonly List<string> unported = new();

    // ---- p_pspr.c (T6.6) ----

    /// <summary>p_pspr.c <c>P_DropWeapon</c> (the player died): a stub until T6.6.</summary>
    public void P_DropWeapon(player_t player) => unported.Add("P_DropWeapon()");
}
