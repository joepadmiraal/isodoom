using System.Collections.Generic;

namespace IsoDoom.Sim;

// The log of calls to functions later tasks port. A stub records its call in
// unported and does nothing else; each task moves its functions to their own
// file and deletes their stubs here (doors: World.Doors.cs, T5.3; floors,
// lifts, ceilings and stairs: World.Floor.cs, World.Plats.cs,
// World.Ceiling.cs, T5.5; teleports: World.Telept.cs, T5.6; lights:
// World.Lights.cs, T5.7; exits: World.Game.cs, T5.8; the weapon drop on
// death: World.Pspr.cs, T6.6). No stub is left; the log stays for the next.
public sealed partial class World
{
    /// <summary>
    /// Not vanilla: the calls to functions not ported yet, in order, as
    /// <c>P_Foo()</c>, so tests and the level scene can see them.
    /// Cleared by <see cref="P_SetupLevel"/>; not sim state (outside the checksum).
    /// </summary>
    public readonly List<string> Unported = [];
}
