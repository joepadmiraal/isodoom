using System;
using IsoDoom.Map;
using IsoDoom.Wad;

namespace IsoDoom.Sim;

// g_game.c: a co-op net game's reborn (T11.3), G_DoReborn's branch after the
// deathmatch one; G_CheckSpot and the body queue are World.Deathmatch.cs's (T11.4).
public sealed partial class World
{
    /// <summary>
    /// <see cref="G_CheckSpot"/>'s fog direction for a start facing
    /// <paramref name="angle"/> degrees (Chocolate Doom's, from PrBoom+):
    /// the released source computes <c>an = (ANG45 * (angle / 45)) &gt;&gt;
    /// ANGLETOFINESHIFT</c>, which is negative in the DOS executable for
    /// angles of 180 and more, so <c>finecosine[an]</c> and
    /// <c>finesine[an]</c> read the entries before them, in
    /// <c>finetangent</c> (a start facing west gets its fog far off: "silently").
    /// Chocolate Doom stops with an error for an angle outside 0–359; so does this.
    /// </summary>
    public static (int Xa, int Ya) G_CheckSpotFogOffset(int angle)
    {
        int an = (int)(Tables.ANG45 >> Tables.ANGLETOFINESHIFT) * (angle / 45);
        return an switch
        {
            4096 => (Tables.finetangent[2048], Tables.finetangent[0]), // finecosine[-4096], finesine[-4096]
            5120 => (Tables.finetangent[3072], Tables.finetangent[1024]), // -3072
            6144 => (Tables.finesine[0], Tables.finetangent[2048]), // -2048
            7168 => (Tables.finesine[1024], Tables.finetangent[3072]), // -1024
            0 or 1024 or 2048 or 3072 => (Tables.finecosine[an], Tables.finesine[an]),
            _ => throw new WadFormatException($"G_CheckSpot: unexpected angle {an}"),
        };
    }

    /// <summary>
    /// g_game.c <c>G_DoReborn</c>'s co-op branch (T11.3; the body already
    /// disassociated by <see cref="G_DoReborn"/>): the player spawns afresh
    /// (<see cref="P_SpawnPlayer"/>, <see cref="G_PlayerReborn"/>) at its own
    /// start when <see cref="G_CheckSpot"/> allows, else at the first other
    /// start that allows it (as that player, coloured as itself), else at its
    /// own start whatever is there ("he's going to be inside something. Too
    /// bad."). A start the map lacks is skipped: vanilla checks the stale or
    /// zeroed <c>playerstarts</c> entry instead (SPEC §12 T11.3).
    /// </summary>
    private void G_DoCoopReborn(int playernum)
    {
        MapThing own = playerstarts[playernum]
            ?? throw new InvalidOperationException($"G_DoReborn: player {playernum + 1} has no start on the map");
        if (G_CheckSpot(playernum, own))
        {
            P_SpawnPlayer(own);
            return;
        }

        // try to spawn at one of the other players spots
        for (int i = 0; i < MAXPLAYERS; i++)
        {
            if (playerstarts[i] is { } start && G_CheckSpot(playernum, start))
            {
                P_SpawnPlayer(start with { Type = (short)(playernum + 1) }); // fake as other player
                return;
            }
            // he's going to be inside something.  Too bad.
        }
        P_SpawnPlayer(own);
    }

    /// <summary>
    /// T11.3: a net game's reborns now (<see cref="G_CheckReborns"/>, which
    /// <see cref="G_Ticker(ticcmd_t[])"/> runs at the tic's start, as
    /// vanilla's <c>G_Ticker</c>). A route's events run before the tic but
    /// after these in vanilla (dump.c's <c>dump_pretic</c> is in
    /// <c>P_Ticker</c>), so <c>RouteEvent.Run</c> calls this first; the
    /// tic's own call then finds nothing to do. Nothing in a single player game.
    /// </summary>
    public void G_DoNetReborns()
    {
        if (netgame)
            G_CheckReborns();
    }
}
