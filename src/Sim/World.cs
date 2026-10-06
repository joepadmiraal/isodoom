using System;
using System.Collections.Generic;
using IsoDoom.Map;
using IsoDoom.Wad;

namespace IsoDoom.Sim;

/// <summary>
/// The play simulation's state: vanilla's globals (doomstat.h, g_game.c,
/// p_setup.c, p_tick.c, p_mobj.c, m_random.c) as one object, so tests can
/// run several worlds side by side. A world lives for a game (players and
/// the random index carry over between levels); <see cref="G_DoLoadLevel"/>
/// starts each level. The partial files follow vanilla's: World.Tick.cs is
/// p_tick.c, World.Mobj.cs p_mobj.c, World.MapUtl.cs p_maputl.c, World.Map.cs p_map.c,
/// World.User.cs p_user.c (and <c>G_Ticker</c>), World.Spec.cs p_spec.c,
/// World.Switch.cs p_switch.c, World.Doors.cs p_doors.c, World.Floor.cs p_floor.c,
/// World.Sound.cs the sound events, World.Unported.cs the stubs of the specials
/// still to port, World.Checksum.cs the per-tic state checksum (SPEC §6.1).
/// <para>
/// The <see cref="level"/>'s sectors are changed in place (heights, light,
/// flats), so the presentation reads the moving geometry from the same
/// objects; a world needs a <see cref="Level"/> of its own, freshly loaded
/// (SPEC §12 T4.2).
/// </para>
/// </summary>
public sealed partial class World
{
    /// <summary>doomdef.h <c>MAXPLAYERS</c>.</summary>
    public const int MAXPLAYERS = 4;

    /// <summary>p_local.h <c>ONFLOORZ</c>: spawn on the floor (fixed_t <c>MININT</c>).</summary>
    public const int ONFLOORZ = int.MinValue;

    /// <summary>p_local.h <c>ONCEILINGZ</c>: spawn hanging from the ceiling (fixed_t <c>MAXINT</c>).</summary>
    public const int ONCEILINGZ = int.MaxValue;

    /// <summary>p_local.h <c>ITEMQUESIZE</c>: the item respawn queue (deathmatch 2).</summary>
    public const int ITEMQUESIZE = 128;

    /// <summary>
    /// g_game.c <c>G_InitNew</c>'s game state part: a new game with
    /// <paramref name="settings"/> (skill, mode, netgame, deathmatch,
    /// nomonsters) and <paramref name="tweaks"/>. <c>M_ClearRandom</c>, every
    /// player to <see cref="playerstate_t.PST_REBORN"/>. Player
    /// <see cref="consoleplayer"/> is in the game; set <see cref="playeringame"/>
    /// for more before the first level.
    /// </summary>
    public World(SpawnSettings settings, Tweaks tweaks)
    {
        this.settings = settings;
        this.tweaks = tweaks;
        for (int i = 0; i < MAXPLAYERS; i++)
            players[i] = new player_t();
        playeringame[consoleplayer] = true;

        // g_game.c G_InitNew
        respawnmonsters = settings.gameskill == skill_t.sk_nightmare; // || respawnparm (later)
        random.M_ClearRandom();
        // force players to be initialized upon first level load
        for (int i = 0; i < MAXPLAYERS; i++)
            players[i].playerstate = playerstate_t.PST_REBORN;
        P_InitThinkers();

        // p_setup.c P_Init (the switch list; the animations come with T5.7)
        P_InitSwitchList();
    }

    /// <summary>The game settings (doomstat.h <c>gamemode</c>, <c>gameskill</c>, <c>netgame</c>, <c>deathmatch</c>, <c>nomonsters</c>).</summary>
    public readonly SpawnSettings settings;

    /// <summary>The deviations from vanilla in use (SPEC §6.3).</summary>
    public readonly Tweaks tweaks;

    public skill_t gameskill => settings.gameskill;
    public GameMode gamemode => settings.gamemode;
    public bool netgame => settings.netgame;
    public int deathmatch => settings.deathmatch;

    /// <summary>g_game.c <c>respawnmonsters</c>: Nightmare (vanilla also <c>-respawn</c>).</summary>
    public readonly bool respawnmonsters;

    /// <summary>m_random.c's indices (<c>P_Random</c>, <c>M_Random</c>).</summary>
    public readonly DoomRandom random = new();

    /// <summary>Shorthand for <c>random.P_Random()</c>.</summary>
    public int P_Random() => random.P_Random();

    // ---- players (g_game.c) ----

    public readonly player_t[] players = new player_t[MAXPLAYERS];
    public readonly bool[] playeringame = new bool[MAXPLAYERS];

    /// <summary>The local player (always 0 for now).</summary>
    public int consoleplayer => 0;

    // ---- the level (p_setup.c) ----

    /// <summary>The current level; null before <see cref="G_DoLoadLevel"/>.</summary>
    public Level level { get; private set; } = null!;

    /// <summary>The level's sectors, in <c>SECTORS</c> order.</summary>
    public sector_t[] sectors { get; private set; } = Array.Empty<sector_t>();

    /// <summary>The level's subsectors, in <c>SSECTORS</c> order.</summary>
    public subsector_t[] subsectors { get; private set; } = Array.Empty<subsector_t>();

    /// <summary>The level's lines, in <c>LINEDEFS</c> order.</summary>
    public line_t[] lines { get; private set; } = Array.Empty<line_t>();

    /// <summary>The level's sidedefs, in <c>SIDEDEFS</c> order.</summary>
    public side_t[] sides { get; private set; } = Array.Empty<side_t>();

    /// <summary>p_setup.c <c>blocklinks</c>: the first mobj of each block's list (through <see cref="mobj_t.bnext"/>), row by row from the bottom left.</summary>
    public mobj_t?[] blocklinks { get; private set; } = Array.Empty<mobj_t?>();

    public int bmapwidth => level.Blockmap.BmapWidth;
    public int bmapheight => level.Blockmap.BmapHeight;
    public int bmaporgx => level.Blockmap.BmapOrgX;
    public int bmaporgy => level.Blockmap.BmapOrgY;

    /// <summary>p_setup.c <c>playerstarts</c>: the last start of each player in <c>THINGS</c>.</summary>
    public readonly MapThing?[] playerstarts = new MapThing?[MAXPLAYERS];

    /// <summary>p_setup.c <c>deathmatchstarts</c> up to <c>deathmatch_p</c> (at most <see cref="MapThingSpawning.MAX_DM_STARTS"/>).</summary>
    public readonly List<MapThing> deathmatchstarts = new();

    /// <summary>The level's tic count (p_tick.c <c>leveltime</c>).</summary>
    public int leveltime;

    // doomstat.h: for intermission stats
    public int totalkills;
    public int totalitems;
    public int totalsecret;

    // p_mobj.c: the item respawn queue (P_RemoveMobj, P_RespawnSpecials)
    public readonly MapThing[] itemrespawnque = new MapThing[ITEMQUESIZE];
    public readonly int[] itemrespawntime = new int[ITEMQUESIZE];
    public int iquehead;
    public int iquetail;

    /// <summary>The subsector of the map's <see cref="Subsector"/>.</summary>
    public subsector_t Subsector(Subsector ss) => subsectors[ss.Index];

    /// <summary>r_main.c <c>R_PointInSubsector</c> (fixed_t), as the sim's <see cref="subsector_t"/>.</summary>
    public subsector_t R_PointInSubsector(int x, int y) => subsectors[level.R_PointInSubsector(x, y).Index];

    /// <summary>
    /// g_game.c <c>G_DoLoadLevel</c>'s sim part: dead players are reborn,
    /// frags cleared, then <see cref="P_SetupLevel"/>.
    /// </summary>
    public void G_DoLoadLevel(Level level)
    {
        for (int i = 0; i < MAXPLAYERS; i++)
        {
            if (playeringame[i] && players[i].playerstate == playerstate_t.PST_DEAD)
                players[i].playerstate = playerstate_t.PST_REBORN;
            Array.Clear(players[i].frags);
        }
        P_SetupLevel(level);
    }

    /// <summary>
    /// p_setup.c <c>P_SetupLevel</c>'s sim part, on an already loaded
    /// <paramref name="level"/> (<see cref="Level.Load"/>, which this world
    /// then owns and changes): clears the counts and the thinker list,
    /// links the sectors and blocks, spawns the things (<c>P_LoadThings</c>)
    /// clears the item respawn queue and runs <see cref="P_SpawnSpecials"/>.
    /// </summary>
    public void P_SetupLevel(Level level)
    {
        if (settings.deathmatch != 0)
            throw new NotSupportedException("Deathmatch spawning (G_DeathMatchSpawnPlayer) is not ported.");

        totalkills = totalitems = totalsecret = 0;
        for (int i = 0; i < MAXPLAYERS; i++)
            players[i].killcount = players[i].secretcount = players[i].itemcount = 0;

        // Initial height of PointOfView will be set by player think.
        players[consoleplayer].viewz = 1;

        P_InitThinkers();
        leveltime = 0;

        this.level = level;
        sectors = new sector_t[level.Sectors.Length];
        for (int i = 0; i < sectors.Length; i++)
            sectors[i] = new sector_t(level.Sectors[i]);
        subsectors = new subsector_t[level.Subsectors.Length];
        for (int i = 0; i < subsectors.Length; i++)
            subsectors[i] = new subsector_t(level.Subsectors[i], sectors[level.Subsectors[i].Sector.Index]);
        lines = new line_t[level.Lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            Line ld = level.Lines[i];
            lines[i] = new line_t(ld,
                ld.FrontSector is null ? null : sectors[ld.FrontSector.Index],
                ld.BackSector is null ? null : sectors[ld.BackSector.Index]);
        }
        sides = new side_t[level.Sides.Length];
        for (int i = 0; i < sides.Length; i++)
            sides[i] = new side_t(level.Sides[i], sectors[level.Sides[i].Sector.Index]);
        // P_GroupLines: the sectors' line lists
        foreach (sector_t sector in sectors)
        {
            IReadOnlyList<Line> sectorLines = sector.map.Lines;
            sector.lines = new line_t[sectorLines.Count];
            for (int i = 0; i < sector.lines.Length; i++)
                sector.lines[i] = lines[sectorLines[i].Index];
            sector.StoreInterpolation(); // not vanilla (T5.1)
        }
        // P_LoadBlockMap: clear out mobj chains
        blocklinks = new mobj_t?[bmapwidth * bmapheight];

        Array.Clear(playerstarts);
        deathmatchstarts.Clear();
        for (int i = 0; i < MAXPLAYERS; i++)
            players[i].mo = null;

        P_LoadThings();

        // clear special respawning que
        iquehead = iquetail = 0;

        // set up world state
        unported.Clear(); // not vanilla (T5.2)
        sounds.Clear(); // not vanilla (T5.3)
        P_SpawnSpecials();
    }

    /// <summary>
    /// p_setup.c <c>P_LoadThings</c>: <see cref="P_SpawnMapThing"/> for each
    /// map thing in order, stopping at the first Doom II monster outside
    /// <c>commercial</c> (<see cref="MapThingSpawning.SpawnList"/>).
    /// </summary>
    private void P_LoadThings()
    {
        foreach (MapThingSpawn spawn in MapThingSpawning.SpawnList(level.Things, settings))
            P_SpawnMapThing(spawn);
    }

    /// <summary>Every mobj in thinker list order (removed ones not yet unlinked are skipped).</summary>
    public IEnumerable<mobj_t> Mobjs()
    {
        for (thinker_t th = thinkercap.next; th != thinkercap; th = th.next)
        {
            if (th is mobj_t mobj && th.function != think_t.REMOVED)
                yield return mobj;
        }
    }
}
