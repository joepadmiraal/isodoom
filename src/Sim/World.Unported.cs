using System.Collections.Generic;

namespace IsoDoom.Sim;

// Stubs for the specials the line triggers call that M5's later tasks port
// (T5.2): exits (g_game.c, T5.8). Each records its call in unported and does
// nothing else; each task moves its functions to their own file and deletes
// their stubs here (doors: World.Doors.cs, T5.3; floors, lifts, ceilings and
// stairs: World.Floor.cs, World.Plats.cs, World.Ceiling.cs, T5.5;
// teleports: World.Telept.cs, T5.6; lights: World.Lights.cs, T5.7).
public sealed partial class World
{
    /// <summary>
    /// Not vanilla: the calls to specials not ported yet, in order, as
    /// <c>G_ExitLevel()</c> (T5.2), so tests can see which
    /// special a trigger ran. Cleared by <see cref="P_SetupLevel"/>; not sim
    /// state (outside the checksum).
    /// </summary>
    public readonly List<string> unported = new();

    // ---- g_game.c (T5.8) ----

    /// <summary>g_game.c <c>G_ExitLevel</c>: a stub until T5.8.</summary>
    public void G_ExitLevel() => unported.Add("G_ExitLevel()");

    /// <summary>g_game.c <c>G_SecretExitLevel</c>: a stub until T5.8.</summary>
    public void G_SecretExitLevel() => unported.Add("G_SecretExitLevel()");
}
