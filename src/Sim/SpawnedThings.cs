using System.Collections.Generic;
using IsoDoom.Map;

namespace IsoDoom.Sim;

/// <summary>
/// A map thing as <c>P_SpawnMapThing</c> would spawn it, before any tic runs:
/// its position (fixed_t; <see cref="z"/> as p_mobj.c <c>P_SpawnMobj</c>
/// sets it from <c>ONFLOORZ</c>/<c>ONCEILINGZ</c>), facing (BAM), the sector
/// of <see cref="Level.R_PointInSubsector"/> under it, and its spawn state's
/// sprite frame. The renderer draws these until M4's mobjs replace them (T3.5).
/// </summary>
/// <param name="Spawn">The selection (<see cref="MapThingSpawning.Select"/>; always a <see cref="MapThingSpawnKind.Mobj"/>).</param>
/// <param name="x">fixed_t.</param>
/// <param name="y">fixed_t.</param>
/// <param name="z">fixed_t: the floor height, or the ceiling height minus <c>info-&gt;height</c> for <c>MF_SPAWNCEILING</c> things.</param>
/// <param name="angle">BAM (<see cref="MapThingSpawn.Angle"/>).</param>
/// <param name="Sector">The sector under the thing (<c>mobj-&gt;subsector-&gt;sector</c>), whose light the sprite takes.</param>
/// <param name="state">The spawn state (<c>info-&gt;spawnstate</c>).</param>
/// <param name="sprite">The state's sprite.</param>
/// <param name="frame">The state's frame number (<c>frame &amp; FF_FRAMEMASK</c>; 0 = A).</param>
/// <param name="fullbright">Whether the state's frame has <c>FF_FULLBRIGHT</c>.</param>
public readonly record struct SpawnedThing(
    MapThingSpawn Spawn, int x, int y, int z, uint angle, Sector Sector, statenum_t state, spritenum_t sprite, int frame, bool fullbright);

/// <summary>
/// The spawn positions and frames of a map's things (p_mobj.c
/// <c>P_SpawnMapThing</c> and <c>P_SpawnMobj</c>, without creating mobjs):
/// what the level renderer draws before the sim runs (T3.5). Integer only.
/// </summary>
public static class SpawnedThings
{
    /// <summary>p_mobj.c <c>P_SpawnMobj</c>'s placement of one selected thing (<paramref name="spawn"/> must be a <see cref="MapThingSpawnKind.Mobj"/>).</summary>
    public static SpawnedThing P_SpawnMobjPosition(Level level, MapThingSpawn spawn)
    {
        mobjinfo_t info = Info.mobjinfo[(int)spawn.Type];
        // p_mobj.c P_SpawnMapThing: x = mthing->x << FRACBITS, ...
        int x = spawn.Thing.X << Fixed.FRACBITS;
        int y = spawn.Thing.Y << Fixed.FRACBITS;
        // P_SpawnMobj: set subsector and/or block links, then z from the sector's planes.
        Sector sector = level.R_PointInSubsector(x, y).Sector;
        int z = (info.flags & mobjflag_t.MF_SPAWNCEILING) != 0
            ? sector.CeilingHeight - info.height // ONCEILINGZ
            : sector.FloorHeight; // ONFLOORZ
        state_t st = Info.states[(int)info.spawnstate];
        return new SpawnedThing(spawn, x, y, z, spawn.Angle, sector, info.spawnstate, st.sprite,
            st.frame & Info.FF_FRAMEMASK, (st.frame & Info.FF_FULLBRIGHT) != 0);
    }

    /// <summary>
    /// Every mobj of <paramref name="list"/> (<see cref="MapThingSpawning.SpawnList"/>)
    /// in <c>THINGS</c> order. Players are not included: the player start's
    /// player is drawn by whatever stands for the player.
    /// </summary>
    public static SpawnedThing[] Build(Level level, IReadOnlyList<MapThingSpawn> list)
    {
        var things = new List<SpawnedThing>();
        foreach (MapThingSpawn spawn in list)
        {
            if (spawn.Kind == MapThingSpawnKind.Mobj)
                things.Add(P_SpawnMobjPosition(level, spawn));
        }
        return [.. things];
    }
}
