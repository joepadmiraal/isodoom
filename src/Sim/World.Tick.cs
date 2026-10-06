namespace IsoDoom.Sim;

// p_tick.c: the thinker list and the tic.
public sealed partial class World
{
    /// <summary>
    /// p_tick.c <c>thinkercap</c>: the list head; both ends of the doubly
    /// linked thinker list point back to it. Thinkers run in the order they
    /// were added.
    /// </summary>
    public readonly thinker_t thinkercap = new();

    /// <summary>p_tick.c <c>P_InitThinkers</c>: an empty list.</summary>
    public void P_InitThinkers()
    {
        thinkercap.prev = thinkercap.next = thinkercap;
    }

    /// <summary>p_tick.c <c>P_AddThinker</c>: adds a new thinker at the end of the list.</summary>
    public void P_AddThinker(thinker_t thinker)
    {
        thinkercap.prev.next = thinker;
        thinker.next = thinkercap;
        thinker.prev = thinkercap.prev;
        thinkercap.prev = thinker;
    }

    /// <summary>
    /// p_tick.c <c>P_RemoveThinker</c>: deallocation is lazy, the thinker is
    /// marked and <see cref="P_RunThinkers"/> unlinks it when it gets there
    /// (so removing one while the list runs is safe).
    /// </summary>
    public void P_RemoveThinker(thinker_t thinker)
    {
        thinker.function = think_t.REMOVED;
    }

    /// <summary>p_tick.c <c>P_RunThinkers</c>: runs every thinker once, unlinking the removed ones.</summary>
    public void P_RunThinkers()
    {
        thinker_t currentthinker = thinkercap.next;
        while (currentthinker != thinkercap)
        {
            if (currentthinker.function == think_t.REMOVED)
            {
                // time to remove it
                currentthinker.next.prev = currentthinker.prev;
                currentthinker.prev.next = currentthinker.next;
                // (vanilla Z_Frees it and then reads its next pointer, which still holds)
            }
            else
            {
                Think(currentthinker);
            }
            currentthinker = currentthinker.next;
        }
    }

    /// <summary>Vanilla's call through <c>thinker->function.acp1</c>.</summary>
    private void Think(thinker_t thinker)
    {
        switch (thinker.function)
        {
            case think_t.P_MobjThinker:
                P_MobjThinker((mobj_t)thinker);
                break;
            case think_t.T_VerticalDoor:
                T_VerticalDoor((vldoor_t)thinker);
                break;
            // think_t.NULL: nothing
        }
    }

    /// <summary>
    /// p_tick.c <c>P_Ticker</c>: one tic of the level. The pause and menu
    /// checks are the game loop's (T4.7); the players' <c>ticcmd</c>s are
    /// in <see cref="player_t.cmd"/> (<see cref="G_Ticker(ticcmd_t[])"/>).
    /// <c>P_RespawnSpecials</c> (deathmatch 2 only) is not ported.
    /// </summary>
    public void P_Ticker()
    {
        P_StoreInterpolation(); // not vanilla: presentation only (T4.7)
        sounds.Clear(); // not vanilla: this tic's sound events (T5.3)

        for (int i = 0; i < MAXPLAYERS; i++)
        {
            if (playeringame[i])
                P_PlayerThink(players[i]);
        }

        P_RunThinkers();
        P_UpdateSpecials();
        // P_RespawnSpecials (); deathmatch 2 only

        // for par times
        leveltime++;
    }

    /// <summary>
    /// Not vanilla (SPEC §12 T4.7, as source ports): before a tic, every mobj
    /// remembers its position and facing (<see cref="mobj_t.oldx"/>…) and may
    /// be interpolated (<see cref="mobj_t.interp"/>); mobjs spawned, teleported
    /// or moved by <see cref="PlaceMobj"/> during the tic clear it. Every
    /// sector remembers its floor and ceiling heights
    /// (<see cref="sector_t.oldfloorheight"/>, T5.1; light levels step). The
    /// presentation draws between the two. Changes nothing the sim reads.
    /// </summary>
    public void P_StoreInterpolation()
    {
        foreach (sector_t sector in sectors)
            sector.StoreInterpolation();
        for (thinker_t th = thinkercap.next; th != thinkercap; th = th.next)
        {
            if (th is mobj_t mo)
            {
                mo.oldx = mo.x;
                mo.oldy = mo.y;
                mo.oldz = mo.z;
                mo.oldangle = mo.angle;
                mo.interp = true;
            }
        }
    }

    /// <summary>
    /// Not vanilla: a debug move (the level script's <c>place</c>, T4.7):
    /// puts <paramref name="mo"/> at (<paramref name="x"/>, <paramref name="y"/>)
    /// (fixed_t) on the floor there, facing <paramref name="angle"/> when given,
    /// with no momentum and no collision check, like a teleport without the
    /// fog; a player's view goes to its eye height and it stops running
    /// (<c>S_PLAY</c>). Not interpolated.
    /// </summary>
    public void PlaceMobj(mobj_t mo, int x, int y, uint? angle = null)
    {
        P_UnsetThingPosition(mo);
        mo.x = x;
        mo.y = y;
        P_SetThingPosition(mo);
        mo.floorz = mo.subsector.sector.floorheight;
        mo.ceilingz = mo.subsector.sector.ceilingheight;
        mo.z = mo.floorz;
        mo.momx = mo.momy = mo.momz = 0;
        if (angle is uint a)
            mo.angle = a;
        if (mo.player is { } player)
        {
            // Standing still: vanilla resets the running frames only in
            // P_XYMovement, which a mobj without momentum skips.
            if (mo.state >= statenum_t.S_PLAY_RUN1 && mo.state <= statenum_t.S_PLAY_RUN4)
                P_SetMobjState(mo, statenum_t.S_PLAY);
            player.viewheight = player_t.VIEWHEIGHT;
            player.deltaviewheight = 0;
            player.viewz = mo.z + player.viewheight;
        }
        mo.interp = false;
    }
}
