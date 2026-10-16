using IsoDoom.Map;
using IsoDoom.Wad;

namespace IsoDoom.Sim;

// g_game.c G_CheckSpot, G_DeathMatchSpawnPlayer and the body queue; p_mobj.c
// P_RespawnSpecials (T11.4: deathmatch and altdeath).
public sealed partial class World
{
    /// <summary>g_game.c <c>BODYQUESIZE</c>: the dead players' bodies kept in a netgame.</summary>
    public const int BODYQUESIZE = 32;

    /// <summary>g_game.c <c>bodyque</c>: the bodies left by netgame reborns, the oldest removed past <see cref="BODYQUESIZE"/>.</summary>
    public readonly mobj_t?[] bodyque = new mobj_t?[BODYQUESIZE];

    /// <summary>g_game.c <c>bodyqueslot</c>: the bodies queued since the level's start (<see cref="P_SetupLevel"/> clears it).</summary>
    public int bodyqueslot;

    /// <summary>
    /// g_game.c <c>G_CheckSpot</c>: whether player <paramref name="playernum"/>
    /// can be respawned at <paramref name="mthing"/>. At the level's first
    /// spawn (no mobj yet) only another player already standing on the spot
    /// blocks it; else <see cref="P_CheckPosition"/> must allow it, and then
    /// the old body goes into the body queue (the oldest past
    /// <see cref="BODYQUESIZE"/> removed) and a teleport fog spawns 20 units
    /// in front of the spot, with Chocolate Doom's emulation of the DOS
    /// executable's signed fine angle (a spot facing 180° or more reads
    /// <c>finetangent</c>: one facing west gets its fog on the spot).
    /// </summary>
    public bool G_CheckSpot(int playernum, MapThing mthing)
    {
        if (players[playernum].mo is not { } pmo)
        {
            // first spawn of level, before corpses
            for (int i = 0; i < playernum; i++)
            {
                // (vanilla reads every earlier player's mobj; one not in the game has none: SPEC §12 T11.4)
                if (players[i].mo is { } other
                    && other.x == mthing.X << Fixed.FRACBITS
                    && other.y == mthing.Y << Fixed.FRACBITS)
                    return false;
            }
            return true;
        }

        int x = mthing.X << Fixed.FRACBITS;
        int y = mthing.Y << Fixed.FRACBITS;

        if (!P_CheckPosition(pmo, x, y))
            return false;

        // flush an old corpse if needed
        if (bodyqueslot >= BODYQUESIZE)
            P_RemoveMobj(bodyque[bodyqueslot % BODYQUESIZE]!);
        bodyque[bodyqueslot % BODYQUESIZE] = pmo;
        bodyqueslot++;

        // spawn a teleport fog
        subsector_t ss = R_PointInSubsector(x, y);

        (int xa, int ya) = G_CheckSpotFogOffset(mthing.Angle); // (T11.3: shared with the tests)
        mobj_t mo = P_SpawnMobj(unchecked(x + 20 * xa), unchecked(y + 20 * ya), ss.sector.floorheight, mobjtype_t.MT_TFOG);

        // don't start sound on first frame (consoleplayer's view: this machine's, a sound only)
        if (players[consoleplayer].viewz != 1)
            S_StartSound(mo, sfxenum_t.sfx_telept);

        return true;
    }

    /// <summary>
    /// g_game.c <c>G_DeathMatchSpawnPlayer</c>: spawns player
    /// <paramref name="playernum"/> at one of the random deathmatch starts
    /// (20 tries of <c>P_Random() % starts</c> with <see cref="G_CheckSpot"/>,
    /// the start taking the player's number), else at its own start, where
    /// it will probably get stuck. Called at the level's load and each
    /// netgame reborn. Fewer than 4 deathmatch starts is vanilla's
    /// <c>I_Error</c> (a <see cref="WadFormatException"/>).
    /// </summary>
    public void G_DeathMatchSpawnPlayer(int playernum)
    {
        int selections = deathmatchstarts.Count;
        if (selections < 4)
            throw new WadFormatException($"Only {selections} deathmatch spots, 4 required");

        for (int j = 0; j < 20; j++)
        {
            int i = P_Random() % selections;
            if (G_CheckSpot(playernum, deathmatchstarts[i]))
            {
                deathmatchstarts[i] = deathmatchstarts[i] with { Type = (short)(playernum + 1) };
                P_SpawnPlayer(deathmatchstarts[i]);
                return;
            }
        }

        // no good spot, so the player will probably get stuck
        // (vanilla spawns at playerstarts[playernum] whatever it holds; a map without the start has none)
        if (playerstarts[playernum] is MapThing start)
            P_SpawnPlayer(start);
        else
            throw new WadFormatException($"G_DeathMatchSpawnPlayer: no free deathmatch start and no start for player {playernum + 1}");
    }

    /// <summary>
    /// p_mobj.c <c>P_RespawnSpecials</c> (altdeath only, once a tic after
    /// <see cref="P_UpdateSpecials"/>): the oldest item of the respawn queue
    /// (<see cref="P_RemoveMobj"/>'s) comes back at its map spot 30 seconds
    /// after it was picked up, with an item fog and <c>sfx_itmbk</c>.
    /// </summary>
    public void P_RespawnSpecials()
    {
        // only respawn items in deathmatch
        if (deathmatch != 2)
            return;

        // nothing left to respawn?
        if (iquehead == iquetail)
            return;

        // wait at least 30 seconds
        if (leveltime - itemrespawntime[iquetail] < 30 * SimInfo.TICRATE)
            return;

        MapThing mthing = itemrespawnque[iquetail];

        int x = mthing.X << Fixed.FRACBITS;
        int y = mthing.Y << Fixed.FRACBITS;

        // spawn a teleport fog at the new spot
        subsector_t ss = R_PointInSubsector(x, y);
        mobj_t mo = P_SpawnMobj(x, y, ss.sector.floorheight, mobjtype_t.MT_IFOG);
        S_StartSound(mo, sfxenum_t.sfx_itmbk);

        // find which type to spawn
        int i;
        for (i = 0; i < (int)mobjtype_t.NUMMOBJTYPES; i++)
        {
            if (mthing.Type == Info.mobjinfo[i].doomednum)
                break;
        }

        // (vanilla reads past mobjinfo for a spot of no type, which only an
        // item spawned off the map has: nothing spawns; SPEC §12 T11.4)
        if (i < (int)mobjtype_t.NUMMOBJTYPES)
        {
            // spawn it
            int z = (Info.mobjinfo[i].flags & mobjflag_t.MF_SPAWNCEILING) != 0 ? ONCEILINGZ : ONFLOORZ;

            mo = P_SpawnMobj(x, y, z, (mobjtype_t)i);
            mo.spawnpoint = mthing;
            mo.angle = unchecked(Tables.ANG45 * (uint)(mthing.Angle / 45));
        }

        // pull it from the que
        iquetail = (iquetail + 1) & (ITEMQUESIZE - 1);
    }
}
