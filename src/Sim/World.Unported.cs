using System.Collections.Generic;

namespace IsoDoom.Sim;

// Stubs for the specials the line triggers and P_SpawnSpecials call that M5's
// later tasks port (T5.2): doors (p_doors.c, T5.3), floors, lifts, ceilings and
// stairs (p_floor.c, p_plats.c, p_ceilng.c, T5.5), teleports (p_telept.c,
// T5.6), lights (p_lights.c, T5.7) and exits (g_game.c, T5.8). Each records its
// call in unported and does nothing else; each task moves its functions to
// their own file and deletes their stubs here.
public sealed partial class World
{
    /// <summary>
    /// Not vanilla: the calls to specials not ported yet, in order, as
    /// <c>EV_DoDoor(line 3, vld_open)</c> (T5.2), so tests can see which
    /// special a trigger ran. Cleared by <see cref="P_SetupLevel"/>; not sim
    /// state (outside the checksum).
    /// </summary>
    public readonly List<string> unported = new();

    /// <summary>
    /// A stub of an <c>EV_</c> function that acts on the sectors tagged like
    /// <paramref name="line"/>: returns what vanilla's would on a map where
    /// no sector is busy, 1 when a sector has the line's tag.
    /// </summary>
    private int TaggedStub(string call, line_t line)
    {
        unported.Add(call);
        return P_FindSectorFromLineTag(line, -1) >= 0 ? 1 : 0;
    }

    // ---- p_doors.c (T5.3) ----

    /// <summary>p_doors.c <c>EV_DoDoor</c>: a stub until T5.3.</summary>
    public int EV_DoDoor(line_t line, vldoor_e type) => TaggedStub($"EV_DoDoor(line {line.Index}, {type})", line);

    /// <summary>p_doors.c <c>EV_DoLockedDoor</c>: a stub until T5.3 (no key check).</summary>
    public int EV_DoLockedDoor(line_t line, vldoor_e type, mobj_t thing) => TaggedStub($"EV_DoLockedDoor(line {line.Index}, {type})", line);

    /// <summary>p_doors.c <c>EV_VerticalDoor</c>: open a door manually, no tag value: a stub until T5.3.</summary>
    public void EV_VerticalDoor(line_t line, mobj_t thing) => unported.Add($"EV_VerticalDoor(line {line.Index})");

    /// <summary>p_doors.c <c>P_SpawnDoorCloseIn30</c>: a stub until T5.3.</summary>
    public void P_SpawnDoorCloseIn30(sector_t sec) => unported.Add($"P_SpawnDoorCloseIn30(sector {sec.Index})");

    /// <summary>p_doors.c <c>P_SpawnDoorRaiseIn5Mins</c>: a stub until T5.3.</summary>
    public void P_SpawnDoorRaiseIn5Mins(sector_t sec, int secnum) => unported.Add($"P_SpawnDoorRaiseIn5Mins(sector {secnum})");

    // ---- p_floor.c, p_plats.c, p_ceilng.c (T5.5) ----

    /// <summary>p_floor.c <c>EV_DoFloor</c>: a stub until T5.5.</summary>
    public int EV_DoFloor(line_t line, floor_e floortype) => TaggedStub($"EV_DoFloor(line {line.Index}, {floortype})", line);

    /// <summary>p_floor.c <c>EV_BuildStairs</c>: a stub until T5.5.</summary>
    public int EV_BuildStairs(line_t line, stair_e type) => TaggedStub($"EV_BuildStairs(line {line.Index}, {type})", line);

    /// <summary>p_floor.c <c>EV_DoDonut</c>: a stub until T5.5.</summary>
    public int EV_DoDonut(line_t line) => TaggedStub($"EV_DoDonut(line {line.Index})", line);

    /// <summary>p_plats.c <c>EV_DoPlat</c>: a stub until T5.5.</summary>
    public int EV_DoPlat(line_t line, plattype_e type, int amount) => TaggedStub($"EV_DoPlat(line {line.Index}, {type}, {amount})", line);

    /// <summary>p_plats.c <c>EV_StopPlat</c>: a stub until T5.5.</summary>
    public void EV_StopPlat(line_t line) => unported.Add($"EV_StopPlat(line {line.Index})");

    /// <summary>p_ceilng.c <c>EV_DoCeiling</c>: a stub until T5.5.</summary>
    public int EV_DoCeiling(line_t line, ceiling_e type) => TaggedStub($"EV_DoCeiling(line {line.Index}, {type})", line);

    /// <summary>p_ceilng.c <c>EV_CeilingCrushStop</c>: a stub until T5.5 (returns 0, no ceiling is active).</summary>
    public int EV_CeilingCrushStop(line_t line)
    {
        unported.Add($"EV_CeilingCrushStop(line {line.Index})");
        return 0;
    }

    // ---- p_telept.c (T5.6) ----

    /// <summary>p_telept.c <c>EV_Teleport</c>: a stub until T5.6 (returns 0: not teleported).</summary>
    public int EV_Teleport(line_t line, int side, mobj_t thing)
    {
        unported.Add($"EV_Teleport(line {line.Index}, side {side}, {thing.type})");
        return 0;
    }

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
