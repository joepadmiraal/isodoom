using System.Collections.Generic;
using IsoDoom.Map;
using IsoDoom.Wad;

namespace IsoDoom.Sim;

/// <summary>doomdef.h <c>skill_t</c>.</summary>
public enum skill_t
{
    sk_baby,
    sk_easy,
    sk_medium,
    sk_hard,
    sk_nightmare,
}

/// <summary>
/// The game settings <c>P_LoadThings</c> and <c>P_SpawnMapThing</c> read
/// (doomstat.h globals): <c>gamemode</c>, <c>gameskill</c>, <c>netgame</c>,
/// <c>deathmatch</c> (0 cooperative or single player, 1 deathmatch, 2
/// altdeath) and <c>nomonsters</c>; and (T6.4) d_main.c's <c>respawnparm</c>
/// (<c>-respawn</c>: monsters respawn as on Nightmare) and <c>fastparm</c>
/// (<c>-fast</c>: fast monsters as on Nightmare), which the world reads.
/// </summary>
public readonly record struct SpawnSettings(GameMode gamemode, skill_t gameskill, bool netgame = false, int deathmatch = 0, bool nomonsters = false,
    bool respawnparm = false, bool fastparm = false);

/// <summary>What <see cref="MapThingSpawning.Select"/> decided for one map thing.</summary>
public enum MapThingSpawnKind
{
    /// <summary>A mobj of <see cref="MapThingSpawn.Type"/> spawns (<c>P_SpawnMobj</c>).</summary>
    Mobj,

    /// <summary>
    /// A player start (types 1–4): stored in <c>playerstarts[]</c>, and
    /// <c>P_SpawnPlayer</c> runs unless in deathmatch (<see cref="MapThingSpawn.SpawnsPlayer"/>).
    /// </summary>
    PlayerStart,

    /// <summary>A deathmatch start (type 11), one of the first 10, stored in <c>deathmatchstarts[]</c>.</summary>
    DeathmatchStart,

    /// <summary>Ignored: a deathmatch start beyond the 10th, or a type ≤ 0 (Chocolate Doom's rule; SPEC §12 T3.2).</summary>
    Ignored,

    /// <summary>Skipped: options bit 16 (not in single player) outside a netgame.</summary>
    NotSinglePlayer,

    /// <summary>Skipped: not on this skill (the <c>MTF_EASY</c>/<c>MTF_NORMAL</c>/<c>MTF_HARD</c> bit is clear).</summary>
    NotThisSkill,

    /// <summary>Skipped: <c>MF_NOTDMATCH</c> in deathmatch (keys).</summary>
    NotDeathmatch,

    /// <summary>Skipped: a monster (<c>MF_COUNTKILL</c>, or <c>MT_SKULL</c>) under <c>-nomonsters</c>.</summary>
    NoMonsters,

    /// <summary>
    /// Never reached: outside <c>commercial</c>, p_setup.c <c>P_LoadThings</c>
    /// stops at the first Doom II monster (<see cref="MapThingSpawning.IsDoom2Monster"/>),
    /// so it and every later thing are not spawned (vanilla's <c>break</c>).
    /// </summary>
    NotLoaded,
}

/// <summary>
/// The outcome of the selection part of <c>P_SpawnMapThing</c> for one thing.
/// </summary>
/// <param name="Thing">The map thing.</param>
/// <param name="Kind">What happens to it.</param>
/// <param name="Type">The mobj type for <see cref="MapThingSpawnKind.Mobj"/> and the skips decided after the doomednum lookup (<see cref="MapThingSpawnKind.NotDeathmatch"/>, <see cref="MapThingSpawnKind.NoMonsters"/>); otherwise <see cref="mobjtype_t.NUMMOBJTYPES"/>.</param>
/// <param name="SpawnsPlayer">For a player start: whether <c>P_SpawnPlayer</c> is called (not in deathmatch).</param>
public readonly record struct MapThingSpawn(MapThing Thing, MapThingSpawnKind Kind, mobjtype_t Type, bool SpawnsPlayer = false)
{
    /// <summary>For a player start, the player number (0–3).</summary>
    public int PlayerNum => Thing.Type - 1;

    /// <summary>Whether something enters the level: a mobj, or a player through <c>P_SpawnPlayer</c>.</summary>
    public bool Spawns => Kind == MapThingSpawnKind.Mobj || (Kind == MapThingSpawnKind.PlayerStart && SpawnsPlayer);

    /// <summary>
    /// The mobj's <c>angle</c> (BAM): <c>ANG45 * (mthing->angle / 45)</c>,
    /// so map angles snap down to multiples of 45° (C truncating division).
    /// </summary>
    public uint Angle => unchecked(Tables.ANG45 * (uint)(Thing.Angle / 45));

    /// <summary>Whether the mobj gets <c>MF_AMBUSH</c> (<c>MTF_AMBUSH</c> in the options).</summary>
    public bool Ambush => (Thing.Options & MapThing.MTF_AMBUSH) != 0;
}

/// <summary>
/// The selection part of p_mobj.c <c>P_SpawnMapThing</c>: which map things
/// spawn, as what, on a skill and game mode, without spawning anything
/// (M4's <c>P_SpawnMapThing</c> spawns from it). The checks run in vanilla's
/// order, so e.g. an unknown type on another skill is skipped, not an error.
/// Floats and unordered collections stay out (SPEC §6.1).
/// </summary>
public static class MapThingSpawning
{
    /// <summary>
    /// The options bit p_mobj.c tests as <c>16</c>: not in single player
    /// (vanilla has no name for it; Boom calls it <c>MTF_NOTSINGLE</c>).
    /// </summary>
    public const int MTF_NOTSINGLE = 16;

    /// <summary>p_setup.c/p_mobj.c <c>MAX_DM_STARTS</c> (vanilla's <c>deathmatchstarts[10]</c>).</summary>
    public const int MAX_DM_STARTS = 10;

    /// <summary>
    /// Selects for one thing. <paramref name="deathmatchStarts"/> is the number of
    /// deathmatch starts stored so far and is incremented when this one is stored.
    /// Throws <see cref="WadFormatException"/> for an unknown type that passes the
    /// skill checks (vanilla <c>I_Error</c>s).
    /// </summary>
    public static MapThingSpawn Select(MapThing mthing, SpawnSettings settings, ref int deathmatchStarts)
    {
        const mobjtype_t none = mobjtype_t.NUMMOBJTYPES;

        // count deathmatch start positions
        if (mthing.Type == 11)
        {
            if (deathmatchStarts < MAX_DM_STARTS)
            {
                deathmatchStarts++;
                return new MapThingSpawn(mthing, MapThingSpawnKind.DeathmatchStart, none);
            }
            return new MapThingSpawn(mthing, MapThingSpawnKind.Ignored, none);
        }

        // Chocolate Doom: "Thing type 0 is actually "player -1 start". For
        // some reason, Vanilla Doom accepts/ignores this." (vanilla writes
        // playerstarts[-1]).
        if (mthing.Type <= 0)
            return new MapThingSpawn(mthing, MapThingSpawnKind.Ignored, none);

        // check for players specially
        if (mthing.Type <= 4)
            return new MapThingSpawn(mthing, MapThingSpawnKind.PlayerStart, none, SpawnsPlayer: settings.deathmatch == 0);

        // check for apropriate skill level
        if (!settings.netgame && (mthing.Options & MTF_NOTSINGLE) != 0)
            return new MapThingSpawn(mthing, MapThingSpawnKind.NotSinglePlayer, none);

        int bit;
        if (settings.gameskill == skill_t.sk_baby)
            bit = 1;
        else if (settings.gameskill == skill_t.sk_nightmare)
            bit = 4;
        else
            bit = 1 << ((int)settings.gameskill - 1);

        if ((mthing.Options & bit) == 0)
            return new MapThingSpawn(mthing, MapThingSpawnKind.NotThisSkill, none);

        // find which type to spawn
        int i;
        for (i = 0; i < (int)mobjtype_t.NUMMOBJTYPES; i++)
        {
            if (mthing.Type == Info.mobjinfo[i].doomednum)
                break;
        }

        if (i == (int)mobjtype_t.NUMMOBJTYPES)
            throw new WadFormatException($"P_SpawnMapThing: Unknown type {mthing.Type} at ({mthing.X}, {mthing.Y})");

        var type = (mobjtype_t)i;
        mobjflag_t flags = Info.mobjinfo[i].flags;

        // don't spawn keycards and players in deathmatch
        if (settings.deathmatch != 0 && (flags & mobjflag_t.MF_NOTDMATCH) != 0)
            return new MapThingSpawn(mthing, MapThingSpawnKind.NotDeathmatch, type);

        // don't spawn any monsters if -nomonsters
        if (settings.nomonsters && (type == mobjtype_t.MT_SKULL || (flags & mobjflag_t.MF_COUNTKILL) != 0))
            return new MapThingSpawn(mthing, MapThingSpawnKind.NoMonsters, type);

        return new MapThingSpawn(mthing, MapThingSpawnKind.Mobj, type);
    }

    /// <summary>
    /// p_setup.c <c>P_LoadThings</c>' "Do not spawn cool, new monsters if
    /// !commercial" list: arachnotron, arch-vile, boss brain, boss shooter,
    /// hell knight, mancubus, pain elemental, commando, revenant, Wolf SS.
    /// </summary>
    public static bool IsDoom2Monster(short type) => type switch
    {
        68 or 64 or 88 or 89 or 69 or 67 or 71 or 65 or 66 or 84 => true,
        _ => false,
    };

    /// <summary>
    /// Selects for every thing of a map in <c>THINGS</c> order, as p_setup.c
    /// <c>P_LoadThings</c> calls <c>P_SpawnMapThing</c>: one entry per thing,
    /// skipped ones included (see <see cref="MapThingSpawn.Kind"/>).
    /// </summary>
    public static MapThingSpawn[] SpawnList(IReadOnlyList<MapThing> things, SpawnSettings settings)
    {
        var list = new MapThingSpawn[things.Count];
        int deathmatchStarts = 0;
        bool stopped = false;
        for (int i = 0; i < list.Length; i++)
        {
            MapThing mt = things[i];

            // Do not spawn cool, new monsters if !commercial
            if (settings.gamemode != GameMode.commercial && IsDoom2Monster(mt.Type))
                stopped = true; // vanilla: if (spawn == false) break;

            list[i] = stopped
                ? new MapThingSpawn(mt, MapThingSpawnKind.NotLoaded, mobjtype_t.NUMMOBJTYPES)
                : Select(mt, settings, ref deathmatchStarts);
        }
        return list;
    }
}
