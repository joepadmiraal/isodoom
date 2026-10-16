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
/// World.Plats.cs p_plats.c, World.Ceiling.cs p_ceilng.c, World.Telept.cs p_telept.c, World.Lights.cs p_lights.c, World.Inter.cs p_inter.c, World.Game.cs g_game.c's level flow, World.Sound.cs the sound events, World.Unported.cs the stubs of the specials
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
    /// player to <see cref="playerstate_t.PST_REBORN"/>. The players in the
    /// game are <see cref="SpawnSettings.playersingame"/>'s (T11.1; player 1
    /// alone by default); <see cref="playeringame"/> can still be changed
    /// before the first level.
    /// </summary>
    public World(SpawnSettings settings, Tweaks tweaks)
    {
        if (settings.playersingame <= 0 || settings.playersingame >= 1 << MAXPLAYERS)
            throw new ArgumentOutOfRangeException(nameof(settings), settings.playersingame, $"players in game: a mask of 1 to {MAXPLAYERS} players");
        this.settings = settings;
        this.tweaks = tweaks;
        for (int i = 0; i < MAXPLAYERS; i++)
        {
            players[i] = new player_t();
            playeringame[i] = (settings.playersingame & (1 << i)) != 0; // T11.1
        }

        // g_game.c G_InitNew
        respawnmonsters = settings.gameskill == skill_t.sk_nightmare || settings.respawnparm;
        // (vanilla halves the global states' tics and sets the missile speeds here;
        // the world reads them through StateTics and MissileSpeed instead: SPEC §12 T6.4)
        fastmonsters = settings.fastparm || settings.gameskill == skill_t.sk_nightmare;
        random.M_ClearRandom();
        // force players to be initialized upon first level load
        for (int i = 0; i < MAXPLAYERS; i++)
            players[i].playerstate = playerstate_t.PST_REBORN;
        P_InitThinkers();

        // p_setup.c P_Init: the switch list (the animations need the WAD: P_InitPicAnims, T5.7)
        P_InitSwitchList();
    }

    /// <summary>The game settings (doomstat.h <c>gamemode</c>, <c>gameskill</c>, <c>netgame</c>, <c>deathmatch</c>, <c>nomonsters</c>).</summary>
    public readonly SpawnSettings settings;

    /// <summary>
    /// The deviations from vanilla in use (SPEC §6.3). T7.3: the options menu
    /// changes the aim assist's strength between tics, so it is not read-only
    /// (a lockstep game would send the change with the tic's commands).
    /// </summary>
    public Tweaks tweaks;

    public skill_t gameskill => settings.gameskill;
    public GameMode gamemode => settings.gamemode;

    /// <summary>
    /// Chocolate Doom's <c>gameversion</c> (doomstat.c; T10.7): the executable
    /// whose behaviour the sim emulates, <see cref="SpawnSettings.gameversion"/>,
    /// or without one the IWAD's default (<see cref="GameVersions.Default"/> of
    /// the game mode and <see cref="SpawnSettings.gamemission"/>: Final Doom's
    /// for TNT and Plutonia, The Ultimate Doom's for <see cref="GameMode.retail"/>,
    /// else v1.9).
    /// </summary>
    public GameVersion gameversion => settings.gameversion ?? GameVersions.Default(gamemode, settings.gamemission);
    public bool netgame => settings.netgame;
    public int deathmatch => settings.deathmatch;

    /// <summary>d_main.c <c>respawnparm</c>: <c>-respawn</c> (T6.4).</summary>
    public bool respawnparm => settings.respawnparm;

    /// <summary>d_main.c <c>fastparm</c>: <c>-fast</c> (T6.4).</summary>
    public bool fastparm => settings.fastparm;

    /// <summary>g_game.c <c>respawnmonsters</c>: Nightmare or <c>-respawn</c>.</summary>
    public readonly bool respawnmonsters;

    /// <summary>
    /// Not a vanilla variable: whether <c>G_InitNew</c> made the monsters
    /// fast (Nightmare or <c>-fast</c>): the demons' and spectres' states
    /// from <c>S_SARG_RUN1</c> to <c>S_SARG_PAIN2</c> last half as long
    /// (<see cref="StateTics"/>) and the imps', cacodemons' and barons' balls
    /// fly at 20 units a tic (<see cref="MissileSpeed"/>). Vanilla changes
    /// the global <c>states</c> and <c>mobjinfo</c>; the world keeps the
    /// shared tables as they are (SPEC §12 T6.4).
    /// </summary>
    public readonly bool fastmonsters;

    /// <summary>
    /// <c>states[state].tics</c> as this game's <c>G_InitNew</c> left it:
    /// halved for the demons' run, attack and pain states with
    /// <see cref="fastmonsters"/>.
    /// </summary>
    public int StateTics(statenum_t state)
    {
        int tics = Info.states[(int)state].tics;
        if (fastmonsters && state >= statenum_t.S_SARG_RUN1 && state <= statenum_t.S_SARG_PAIN2)
            tics >>= 1;
        return tics;
    }

    /// <summary>
    /// <c>mobjinfo[type].speed</c> as this game's <c>G_InitNew</c> left it:
    /// 20 units a tic for <see cref="mobjtype_t.MT_BRUISERSHOT"/>,
    /// <see cref="mobjtype_t.MT_HEADSHOT"/> and <see cref="mobjtype_t.MT_TROOPSHOT"/>
    /// with <see cref="fastmonsters"/> (fixed_t for missiles).
    /// </summary>
    public int MissileSpeed(mobjtype_t type)
    {
        if (fastmonsters && type is mobjtype_t.MT_BRUISERSHOT or mobjtype_t.MT_HEADSHOT or mobjtype_t.MT_TROOPSHOT)
            return 20 * Fixed.FRACUNIT;
        return Info.mobjinfo[(int)type].speed;
    }

    /// <summary>m_random.c's indices (<c>P_Random</c>, <c>M_Random</c>).</summary>
    public readonly DoomRandom random = new();

    /// <summary>Shorthand for <c>random.P_Random()</c>.</summary>
    public int P_Random() => random.P_Random();

    // ---- players (g_game.c) ----

    public readonly player_t[] players = new player_t[MAXPLAYERS];
    public readonly bool[] playeringame = new bool[MAXPLAYERS];

    /// <summary>
    /// d_net.c <c>consoleplayer</c>: the player this machine shows and plays
    /// (T11.1; 0 by default). A per-machine view index: worlds of the same
    /// game on several machines differ in it, so the sim never lets it
    /// change state they share; it is neither saved nor in the
    /// <see cref="Checksum"/>. It only picks who gets a pickup's sound
    /// (an event), the turbo message (cleared after the tic), and the
    /// intermission's view (<see cref="wbstartstruct_t.pnum"/>,
    /// <see cref="wbstartstruct_t.didsecret"/>), as vanilla's (SPEC §12 T11.1).
    /// </summary>
    public int consoleplayer
    {
        get;
        set => field = value is >= 0 and < MAXPLAYERS ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "consoleplayer");
    }

    // ---- the level (p_setup.c) ----

    /// <summary>The current level; null before <see cref="G_DoLoadLevel"/>.</summary>
    public Level level { get; private set; } = null!;

    /// <summary>The level's sectors, in <c>SECTORS</c> order.</summary>
    public sector_t[] sectors { get; private set; } = [];

    /// <summary>The level's subsectors, in <c>SSECTORS</c> order.</summary>
    public subsector_t[] subsectors { get; private set; } = [];

    /// <summary>The level's lines, in <c>LINEDEFS</c> order.</summary>
    public line_t[] lines { get; private set; } = [];

    /// <summary>The level's sidedefs, in <c>SIDEDEFS</c> order.</summary>
    public side_t[] sides { get; private set; } = [];

    /// <summary>p_setup.c <c>blocklinks</c>: the first mobj of each block's list (through <see cref="mobj_t.bnext"/>), row by row from the bottom left.</summary>
    public mobj_t?[] blocklinks { get; private set; } = [];

    public int bmapwidth => level.Blockmap.BmapWidth;
    public int bmapheight => level.Blockmap.BmapHeight;
    public int bmaporgx => level.Blockmap.BmapOrgX;
    public int bmaporgy => level.Blockmap.BmapOrgY;

    /// <summary>p_setup.c <c>playerstarts</c>: the last start of each player in <c>THINGS</c>.</summary>
    public readonly MapThing?[] playerstarts = new MapThing?[MAXPLAYERS];

    /// <summary>p_setup.c <c>deathmatchstarts</c> up to <c>deathmatch_p</c> (at most <see cref="MapThingSpawning.MAX_DM_STARTS"/>).</summary>
    public readonly List<MapThing> deathmatchstarts = [];

    /// <summary>
    /// doomstat.h <c>gamemap</c>: the level's map number, from its name
    /// (<c>ExMy</c>: y, <c>MAPxx</c>: xx; 0 for another name). Read by
    /// <see cref="PIT_StompThing"/> (T5.6: anything telefrags on map 30).
    /// </summary>
    public int gamemap;

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
    /// frags cleared, then <see cref="P_SetupLevel"/>, and the game action is
    /// done (<see cref="gameaction_t.ga_nothing"/>: the game loop calls it
    /// for <see cref="gameaction_t.ga_loadlevel"/>, a reborn, with the same
    /// map freshly loaded, T6.12).
    /// </summary>
    public void G_DoLoadLevel(Level level)
    {
        for (int i = 0; i < MAXPLAYERS; i++)
        {
            turbodetected[i] = false; // T6.11
            if (playeringame[i] && players[i].playerstate == playerstate_t.PST_DEAD)
                players[i].playerstate = playerstate_t.PST_REBORN;
            Array.Clear(players[i].frags);
        }
        P_SetupLevel(level);
        gameaction = gameaction_t.ga_nothing;
    }

    /// <summary>
    /// p_setup.c <c>P_SetupLevel</c>'s sim part, on an already loaded
    /// <paramref name="level"/> (<see cref="Level.Load"/>, which this world
    /// then owns and changes): clears the counts and the thinker list,
    /// links the sectors and blocks, spawns the things (<c>P_LoadThings</c>;
    /// T11.4: in deathmatch, the players at random deathmatch starts,
    /// <see cref="G_DeathMatchSpawnPlayer"/>), clears the item respawn queue and runs <see cref="P_SpawnSpecials"/>.
    /// </summary>
    public void P_SetupLevel(Level level)
    {
        totalkills = totalitems = totalsecret = 0;
        for (int i = 0; i < MAXPLAYERS; i++)
            players[i].killcount = players[i].secretcount = players[i].itemcount = 0;

        // Initial height of PointOfView will be set by player think.
        // (T11.1: every player in the game's, not just consoleplayer's, so worlds that differ in
        // consoleplayer stay equal; P_CalcHeight sets it in the first tic: SPEC §12 T11.1)
        for (int i = 0; i < MAXPLAYERS; i++)
        {
            if (playeringame[i])
                players[i].viewz = 1;
        }


        P_InitThinkers();
        leveltime = 0;

        P_SetupLevelMap(level);

        bodyqueslot = 0; // T11.4
        Array.Clear(bodyque); // not vanilla's: the last level's bodies, unread, are not kept for a save (SPEC §12 T11.4)
        P_LoadThings();

        // if deathmatch, randomly spawn the active players (T11.4)
        if (deathmatch != 0)
        {
            for (int i = 0; i < MAXPLAYERS; i++)
            {
                if (playeringame[i])
                {
                    players[i].mo = null;
                    G_DeathMatchSpawnPlayer(i);
                }
            }
        }

        // clear special respawning que
        iquehead = iquetail = 0;

        // set up world state
        Unported.Clear(); // not vanilla (T5.2)
        events.Clear(); // not vanilla (T6.10)
        P_SpawnSpecials();
    }

    /// <summary>
    /// <see cref="P_SetupLevel"/>'s map part, before the things and the
    /// specials: the level, its number and episode, the sim's sectors,
    /// subsectors, lines and sides over the map's, the sectors' line lists
    /// (<c>P_GroupLines</c>), empty block lists, no starts and no player
    /// mobjs. Also the base of a loaded game (<see cref="P_UnArchiveGame"/>, T7.6).
    /// </summary>
    private void P_SetupLevelMap(Level level)
    {
        this.level = level;
        gamemap = MapNumber(level.Name);
        gameepisode = EpisodeNumber(level.Name);
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

    /// <summary>The map number of a map lump name: <c>ExMy</c> → y, <c>MAPxx</c> → xx, anything else 0 (<see cref="gamemap"/>).</summary>
    public static int MapNumber(string name)
    {
        name = name.ToUpperInvariant();
        if (name.Length == 4 && name[0] == 'E' && name[2] == 'M' && char.IsAsciiDigit(name[1]) && char.IsAsciiDigit(name[3]))
            return name[3] - '0';
        if (name.Length == 5 && name.StartsWith("MAP", StringComparison.Ordinal) && char.IsAsciiDigit(name[3]) && char.IsAsciiDigit(name[4]))
            return (name[3] - '0') * 10 + (name[4] - '0');
        return 0;
    }

    /// <summary>
    /// Every mobj in thinker list order (removed ones not yet unlinked are
    /// skipped). T8.3: a struct, so a <c>foreach</c> over it (the presentation
    /// does every frame) allocates nothing; LINQ takes it as an <see cref="IEnumerable{T}"/>.
    /// </summary>
    public MobjList Mobjs() => new(thinkercap);

    /// <summary>The mobjs of the thinker list (<see cref="Mobjs"/>).</summary>
    public readonly struct MobjList(thinker_t thinkercap) : IEnumerable<mobj_t>
    {
        public Enumerator GetEnumerator() => new(thinkercap);

        IEnumerator<mobj_t> IEnumerable<mobj_t>.GetEnumerator() => GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>Walks the thinker list from its cap to the next live mobj at each step.</summary>
        public struct Enumerator : IEnumerator<mobj_t>
        {
            private readonly thinker_t _cap;
            private thinker_t _th;

            public Enumerator(thinker_t thinkercap) => _cap = _th = thinkercap;

            public readonly mobj_t Current => (mobj_t)_th;

            readonly object System.Collections.IEnumerator.Current => Current;

            public bool MoveNext()
            {
                for (_th = _th.next; _th != _cap; _th = _th.next)
                {
                    if (_th is mobj_t && _th.function != think_t.REMOVED)
                        return true;
                }
                return false;
            }

            public void Reset() => _th = _cap;

            public readonly void Dispose() { }
        }
    }
}
