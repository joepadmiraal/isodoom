using System.Collections.Generic;

namespace IsoDoom.Sim;

// Stubs for the specials the line triggers and P_SpawnSpecials call that M5's
// later tasks port (T5.2): lights (p_lights.c, T5.7) and exits (g_game.c,
// T5.8). Each records its call in unported and does
// nothing else; each task moves its functions to their own file and deletes
// their stubs here (doors: World.Doors.cs, T5.3; floors, lifts, ceilings and
// stairs: World.Floor.cs, World.Plats.cs, World.Ceiling.cs, T5.5;
// teleports: World.Telept.cs, T5.6).
public sealed partial class World
{
    /// <summary>
    /// Not vanilla: the calls to specials not ported yet, in order, as
    /// <c>EV_LightTurnOn(line 5, 0)</c> (T5.2), so tests can see which
    /// special a trigger ran. Cleared by <see cref="P_SetupLevel"/>; not sim
    /// state (outside the checksum).
    /// </summary>
    public readonly List<string> unported = new();

    // ---- p_lights.c (T5.7) ----

    /// <summary>p_lights.c <c>EV_LightTurnOn</c>: a stub until T5.7.</summary>
    public void EV_LightTurnOn(line_t line, int bright) => unported.Add($"EV_LightTurnOn(line {line.Index}, {bright})");

    /// <summary>p_lights.c <c>EV_StartLightStrobing</c>: a stub until T5.7.</summary>
    public void EV_StartLightStrobing(line_t line) => unported.Add($"EV_StartLightStrobing(line {line.Index})");

    /// <summary>p_lights.c <c>EV_TurnTagLightsOff</c>: a stub until T5.7.</summary>
    public void EV_TurnTagLightsOff(line_t line) => unported.Add($"EV_TurnTagLightsOff(line {line.Index})");

    /// <summary>p_lights.c <c>P_SpawnLightFlash</c>: a stub until T5.7.</summary>
    public void P_SpawnLightFlash(sector_t sector) => unported.Add($"P_SpawnLightFlash(sector {sector.Index})");

    /// <summary>p_lights.c <c>P_SpawnStrobeFlash</c>: a stub until T5.7.</summary>
    public void P_SpawnStrobeFlash(sector_t sector, int fastOrSlow, int inSync) =>
        unported.Add($"P_SpawnStrobeFlash(sector {sector.Index}, {fastOrSlow}, {inSync})");

    /// <summary>p_lights.c <c>P_SpawnGlowingLight</c>: a stub until T5.7.</summary>
    public void P_SpawnGlowingLight(sector_t sector) => unported.Add($"P_SpawnGlowingLight(sector {sector.Index})");

    /// <summary>p_lights.c <c>P_SpawnFireFlicker</c>: a stub until T5.7.</summary>
    public void P_SpawnFireFlicker(sector_t sector) => unported.Add($"P_SpawnFireFlicker(sector {sector.Index})");

    // ---- g_game.c (T5.8) ----

    /// <summary>g_game.c <c>G_ExitLevel</c>: a stub until T5.8.</summary>
    public void G_ExitLevel() => unported.Add("G_ExitLevel()");

    /// <summary>g_game.c <c>G_SecretExitLevel</c>: a stub until T5.8.</summary>
    public void G_SecretExitLevel() => unported.Add("G_SecretExitLevel()");
}
