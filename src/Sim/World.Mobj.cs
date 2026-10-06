using IsoDoom.Map;

namespace IsoDoom.Sim;

// p_mobj.c (and g_game.c G_PlayerReborn).
public sealed partial class World
{
    /// <summary>
    /// p_mobj.c <c>P_SetMobjState</c>: enters <paramref name="state"/> and
    /// every following zero-tic state, calling each one's action. Returns
    /// false when the mobj was removed (<see cref="statenum_t.S_NULL"/>).
    /// </summary>
    public bool P_SetMobjState(mobj_t mobj, statenum_t state)
    {
        do
        {
            if (state == statenum_t.S_NULL)
            {
                mobj.state = statenum_t.S_NULL;
                P_RemoveMobj(mobj);
                return false;
            }

            ref readonly state_t st = ref Info.states[(int)state];
            mobj.state = state;
            mobj.tics = st.tics;
            mobj.sprite = st.sprite;
            mobj.frame = st.frame;

            // Modified handling.
            // Call action functions when the state is set
            if (st.action != actionf_t.NULL)
                A_Call(st.action, mobj);

            state = st.nextstate;
        } while (mobj.tics == 0);

        return true;
    }

    /// <summary>
    /// Vanilla's call through <c>st->action.acp1</c>. Every action is a stub
    /// until its task (T6.1 dispatches them, T6.4–T6.6 fill them in).
    /// </summary>
    private void A_Call(actionf_t action, mobj_t mobj)
    {
        switch (action)
        {
            default:
                break;
        }
    }

    /// <summary>
    /// p_mobj.c <c>P_MobjThinker</c>. The momentum movement (<c>P_XYMovement</c>,
    /// <c>P_ZMovement</c>) comes with T4.4, the Nightmare respawn branch of
    /// <c>tics == -1</c> things with T6.1; until then a mobj only cycles
    /// through its states.
    /// </summary>
    public void P_MobjThinker(mobj_t mobj)
    {
        // momentum movement: P_XYMovement / P_ZMovement (T4.4)

        // cycle through states,
        // calling action functions at transitions
        if (mobj.tics != -1)
        {
            mobj.tics--;

            // you can cycle through multiple states in a tic
            if (mobj.tics == 0)
            {
                if (!P_SetMobjState(mobj, Info.states[(int)mobj.state].nextstate))
                    return; // freed itself
            }
        }
        // else: check for nightmare respawn (T6.1)
    }

    /// <summary>
    /// p_mobj.c <c>P_SpawnMobj</c>: a new mobj of <paramref name="type"/> at
    /// (<paramref name="x"/>, <paramref name="y"/>) (fixed_t), linked into its
    /// sector and block and added to the thinker list. <paramref name="z"/>
    /// is a height or <see cref="ONFLOORZ"/>/<see cref="ONCEILINGZ"/>.
    /// Uses one <c>P_Random</c> (<c>lastlook</c>).
    /// </summary>
    public mobj_t P_SpawnMobj(int x, int y, int z, mobjtype_t type)
    {
        mobjinfo_t info = Info.mobjinfo[(int)type];
        var mobj = new mobj_t
        {
            type = type,
            info = info,
            x = x,
            y = y,
            radius = info.radius,
            height = info.height,
            flags = info.flags,
            health = info.spawnhealth,
        };

        if (gameskill != skill_t.sk_nightmare)
            mobj.reactiontime = info.reactiontime;

        mobj.lastlook = P_Random() % MAXPLAYERS;
        // do not set the state with P_SetMobjState,
        // because action routines can not be called yet
        ref readonly state_t st = ref Info.states[(int)info.spawnstate];

        mobj.state = info.spawnstate;
        mobj.tics = st.tics;
        mobj.sprite = st.sprite;
        mobj.frame = st.frame;

        // set subsector and/or block links
        P_SetThingPosition(mobj);

        mobj.floorz = mobj.subsector.sector.floorheight;
        mobj.ceilingz = mobj.subsector.sector.ceilingheight;

        if (z == ONFLOORZ)
            mobj.z = mobj.floorz;
        else if (z == ONCEILINGZ)
            mobj.z = mobj.ceilingz - mobj.info.height;
        else
            mobj.z = z;

        mobj.function = think_t.P_MobjThinker;

        P_AddThinker(mobj);

        return mobj;
    }

    /// <summary>
    /// p_mobj.c <c>P_RemoveMobj</c>: queues a level-placed item (not the
    /// invulnerability or invisibility sphere) for deathmatch 2 respawning,
    /// unlinks the mobj from its sector and block and removes its thinker.
    /// </summary>
    public void P_RemoveMobj(mobj_t mobj)
    {
        if ((mobj.flags & mobjflag_t.MF_SPECIAL) != 0
            && (mobj.flags & mobjflag_t.MF_DROPPED) == 0
            && mobj.type != mobjtype_t.MT_INV
            && mobj.type != mobjtype_t.MT_INS)
        {
            itemrespawnque[iquehead] = mobj.spawnpoint;
            itemrespawntime[iquehead] = leveltime;
            iquehead = (iquehead + 1) & (ITEMQUESIZE - 1);

            // lose one off the end?
            if (iquehead == iquetail)
                iquetail = (iquetail + 1) & (ITEMQUESIZE - 1);
        }

        // unlink from sector and block lists
        P_UnsetThingPosition(mobj);

        // stop any playing sound: S_StopSound (mobj) (T6.10)

        // free block
        P_RemoveThinker(mobj);
    }

    /// <summary>
    /// g_game.c <c>G_PlayerReborn</c>: a fresh player (pistol, 50 bullets,
    /// 100 health), keeping its frags and level counts.
    /// </summary>
    public void G_PlayerReborn(int player)
    {
        player_t p = players[player];
        int[] frags = (int[])p.frags.Clone();
        int killcount = p.killcount;
        int itemcount = p.itemcount;
        int secretcount = p.secretcount;

        p.Clear();
        frags.CopyTo(p.frags, 0);
        p.killcount = killcount;
        p.itemcount = itemcount;
        p.secretcount = secretcount;

        p.usedown = p.attackdown = true; // don't do anything immediately
        p.playerstate = playerstate_t.PST_LIVE;
        p.health = player_t.MAXHEALTH;
        p.readyweapon = p.pendingweapon = weapontype_t.wp_pistol;
        p.weaponowned[(int)weapontype_t.wp_fist] = true;
        p.weaponowned[(int)weapontype_t.wp_pistol] = true;
        p.ammo[(int)ammotype_t.am_clip] = 50;

        for (int i = 0; i < (int)ammotype_t.NUMAMMO; i++)
            p.maxammo[i] = player_t.maxammo_table[i];
    }

    /// <summary>
    /// p_mobj.c <c>P_SpawnPlayer</c>: called when a player is spawned on the
    /// level. Most of the player structure stays unchanged between levels.
    /// Nothing happens for a player not in the game.
    /// </summary>
    public void P_SpawnPlayer(MapThing mthing)
    {
        // not playing?
        if (!playeringame[mthing.Type - 1])
            return;

        if (players[mthing.Type - 1].playerstate == playerstate_t.PST_REBORN)
            G_PlayerReborn(mthing.Type - 1);
        player_t p = players[mthing.Type - 1];

        int x = mthing.X << Fixed.FRACBITS;
        int y = mthing.Y << Fixed.FRACBITS;
        int z = ONFLOORZ;
        mobj_t mobj = P_SpawnMobj(x, y, z, mobjtype_t.MT_PLAYER);

        // set color translations for player sprites
        if (mthing.Type > 1)
            mobj.flags |= (mobjflag_t)((mthing.Type - 1) << Info.MF_TRANSSHIFT);

        mobj.angle = unchecked(Tables.ANG45 * (uint)(mthing.Angle / 45));
        mobj.player = p;
        mobj.health = p.health;

        p.mo = mobj;
        p.playerstate = playerstate_t.PST_LIVE;
        p.refire = 0;
        p.message = null;
        p.damagecount = 0;
        p.bonuscount = 0;
        p.extralight = 0;
        p.fixedcolormap = 0;
        p.viewheight = player_t.VIEWHEIGHT;

        // setup gun psprite: P_SetupPsprites (p) (T6.6)

        // give all cards in death match mode
        if (deathmatch != 0)
        {
            for (int i = 0; i < (int)card_t.NUMCARDS; i++)
                p.cards[i] = true;
        }

        // ST_Start / HU_Start for the console player: the presentation's.
    }

    /// <summary>
    /// p_mobj.c <c>P_SpawnMapThing</c>: spawns what
    /// <see cref="MapThingSpawning.Select"/> decided for one map thing (the
    /// selection part of vanilla's function). Player starts are stored in
    /// <see cref="playerstarts"/> and spawn their player (outside deathmatch),
    /// deathmatch starts are stored, and mobjs spawn with a random first tic
    /// count and count for the intermission totals. Returns the mobj spawned,
    /// if any.
    /// </summary>
    public mobj_t? P_SpawnMapThing(MapThingSpawn spawn)
    {
        MapThing mthing = spawn.Thing;
        switch (spawn.Kind)
        {
            case MapThingSpawnKind.DeathmatchStart:
                deathmatchstarts.Add(mthing);
                return null;

            case MapThingSpawnKind.PlayerStart:
                // save spots for respawning in network games
                playerstarts[mthing.Type - 1] = mthing;
                if (spawn.SpawnsPlayer) // if (!deathmatch)
                {
                    P_SpawnPlayer(mthing);
                    return playeringame[mthing.Type - 1] ? players[mthing.Type - 1].mo : null;
                }
                return null;

            case MapThingSpawnKind.Mobj:
                break;

            default:
                return null;
        }

        mobjtype_t i = spawn.Type;

        // spawn it
        int x = mthing.X << Fixed.FRACBITS;
        int y = mthing.Y << Fixed.FRACBITS;

        int z = (Info.mobjinfo[(int)i].flags & mobjflag_t.MF_SPAWNCEILING) != 0 ? ONCEILINGZ : ONFLOORZ;

        mobj_t mobj = P_SpawnMobj(x, y, z, i);
        mobj.spawnpoint = mthing;

        if (mobj.tics > 0)
            mobj.tics = 1 + (P_Random() % mobj.tics);
        if ((mobj.flags & mobjflag_t.MF_COUNTKILL) != 0)
            totalkills++;
        if ((mobj.flags & mobjflag_t.MF_COUNTITEM) != 0)
            totalitems++;

        mobj.angle = spawn.Angle;
        if (spawn.Ambush)
            mobj.flags |= mobjflag_t.MF_AMBUSH;

        return mobj;
    }
}
