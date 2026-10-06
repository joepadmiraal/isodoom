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
            // think_t.NULL: nothing
        }
    }

    /// <summary>
    /// p_tick.c <c>P_Ticker</c>: one tic of the level. The pause and menu
    /// checks are the game loop's (T4.7); the players' <c>ticcmd</c>s are
    /// in <see cref="player_t.cmd"/> (<see cref="G_Ticker(ticcmd_t[])"/>).
    /// <c>P_UpdateSpecials</c> comes with M5 and <c>P_RespawnSpecials</c>
    /// (deathmatch 2 only) is not ported.
    /// </summary>
    public void P_Ticker()
    {
        for (int i = 0; i < MAXPLAYERS; i++)
        {
            if (playeringame[i])
                P_PlayerThink(players[i]);
        }

        P_RunThinkers();
        // P_UpdateSpecials (); M5
        // P_RespawnSpecials (); deathmatch 2 only

        // for par times
        leveltime++;
    }
}
