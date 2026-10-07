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
    /// until its task (T6.1 dispatches them, T6.4–T6.6 fill them in), but
    /// the player's pain and death ones (T5.8: <see cref="A_Pain"/>,
    /// <see cref="A_PlayerScream"/>, <see cref="A_Fall"/>).
    /// </summary>
    private void A_Call(actionf_t action, mobj_t mobj)
    {
        switch (action)
        {
            case actionf_t.A_Pain:
                A_Pain(mobj);
                break;
            case actionf_t.A_PlayerScream:
                A_PlayerScream(mobj);
                break;
            case actionf_t.A_Fall:
                A_Fall(mobj);
                break;
            default:
                break;
        }
    }

    /// <summary>p_enemy.c <c>A_Pain</c>: the pain sound (T5.8, for the player's pain state).</summary>
    public void A_Pain(mobj_t actor)
    {
        if (actor.info.painsound != sfxenum_t.sfx_None)
            S_StartSound(actor, actor.info.painsound);
    }

    /// <summary>p_enemy.c <c>A_Fall</c>: actor is on ground, it can be walked over (T5.8, for the player's death).</summary>
    public static void A_Fall(mobj_t actor)
    {
        actor.flags &= ~mobjflag_t.MF_SOLID;

        // So change this if corpse objects
        // are meant to be obstacles.
    }

    /// <summary>p_enemy.c <c>A_PlayerScream</c>: the player's death sound (T5.8).</summary>
    public void A_PlayerScream(mobj_t mo)
    {
        // Default death sound.
        sfxenum_t sound = sfxenum_t.sfx_pldeth;

        if (gamemode == IsoDoom.Wad.GameMode.commercial && mo.health < -50)
        {
            // IF THE PLAYER DIES
            // LESS THAN -50% WITHOUT GIBBING
            sound = sfxenum_t.sfx_pdiehi;
        }

        S_StartSound(mo, sound);
    }

    // ---- p_local.h / p_mobj.c ----

    /// <summary>
    /// r_sky.h <c>SKYFLATNAME</c>; vanilla compares flat numbers with
    /// <c>skyflatnum</c>, the sim compares names.
    /// </summary>
    public const string SKYFLATNAME = WallSections.SKYFLATNAME;

    /// <summary>p_local.h <c>FLOATSPEED</c>: how fast floaters rise or sink towards their target's height.</summary>
    public const int FLOATSPEED = Fixed.FRACUNIT * 4;

    /// <summary>p_local.h <c>GRAVITY</c>: the fall speed gained per tic.</summary>
    public const int GRAVITY = Fixed.FRACUNIT;

    /// <summary>p_local.h <c>MAXMOVE</c>: the momentum clamp on each axis.</summary>
    public const int MAXMOVE = 30 * Fixed.FRACUNIT;

    /// <summary>p_mobj.c <c>STOPSPEED</c>: below this on both axes a thing (without move input) stops.</summary>
    public const int STOPSPEED = 0x1000;

    /// <summary>p_mobj.c <c>FRICTION</c>: momentum kept per tic on the ground (0.90625).</summary>
    public const int FRICTION = 0xe800;

    /// <summary>
    /// p_mobj.c <c>P_ExplodeMissile</c>: stops the missile and enters its
    /// death state, shortened by 0–3 tics (one <c>P_Random</c>). The death
    /// sound comes with T6.10.
    /// </summary>
    public void P_ExplodeMissile(mobj_t mo)
    {
        mo.momx = mo.momy = mo.momz = 0;

        P_SetMobjState(mo, Info.mobjinfo[(int)mo.type].deathstate);

        mo.tics -= P_Random() & 3;

        if (mo.tics < 1)
            mo.tics = 1;

        mo.flags &= ~mobjflag_t.MF_MISSILE;

        // if (mo->info->deathsound) S_StartSound (mo, mo->info->deathsound); (T6.10)
    }

    /// <summary>
    /// p_mobj.c <c>P_XYMovement</c>: moves the mobj by its momentum (clamped
    /// to <see cref="MAXMOVE"/>, in two halves when a positive component is
    /// over half of it: vanilla's check ignores negative ones), sliding a
    /// blocked player along walls, exploding a blocked missile (removing it
    /// against a sky ceiling) and stopping anything else. Then friction on
    /// the ground: below <see cref="STOPSPEED"/> (and, for a player, without
    /// move input) the mobj stops and a walking player returns to
    /// <see cref="statenum_t.S_PLAY"/>, else its momentum is scaled by
    /// <see cref="FRICTION"/>. Missiles, charging lost souls and airborne
    /// things keep their momentum, a corpse with some momentum keeps sliding
    /// off a step.
    /// </summary>
    public void P_XYMovement(mobj_t mo)
    {
        if (mo.momx == 0 && mo.momy == 0)
        {
            if ((mo.flags & mobjflag_t.MF_SKULLFLY) != 0)
            {
                // the skull slammed into something
                mo.flags &= ~mobjflag_t.MF_SKULLFLY;
                mo.momx = mo.momy = mo.momz = 0;

                P_SetMobjState(mo, mo.info.spawnstate);
            }
            return;
        }

        player_t? player = mo.player;

        if (mo.momx > MAXMOVE)
            mo.momx = MAXMOVE;
        else if (mo.momx < -MAXMOVE)
            mo.momx = -MAXMOVE;

        if (mo.momy > MAXMOVE)
            mo.momy = MAXMOVE;
        else if (mo.momy < -MAXMOVE)
            mo.momy = -MAXMOVE;

        int xmove = mo.momx;
        int ymove = mo.momy;

        do
        {
            int ptryx, ptryy;
            if (xmove > MAXMOVE / 2 || ymove > MAXMOVE / 2)
            {
                ptryx = mo.x + xmove / 2;
                ptryy = mo.y + ymove / 2;
                xmove >>= 1;
                ymove >>= 1;
            }
            else
            {
                ptryx = mo.x + xmove;
                ptryy = mo.y + ymove;
                xmove = ymove = 0;
            }

            if (!P_TryMove(mo, ptryx, ptryy))
            {
                // blocked move
                if (mo.player != null)
                {
                    // try to slide along it
                    P_SlideMove(mo);
                }
                else if ((mo.flags & mobjflag_t.MF_MISSILE) != 0)
                {
                    // explode a missile
                    if (ceilingline != null
                        && ceilingline.backsector != null
                        && ceilingline.backsector.ceilingpic == SKYFLATNAME)
                    {
                        // Hack to prevent missiles exploding
                        // against the sky.
                        // Does not handle sky floors.
                        P_RemoveMobj(mo);
                        return;
                    }
                    P_ExplodeMissile(mo);
                }
                else
                {
                    mo.momx = mo.momy = 0;
                }
            }
        } while (xmove != 0 || ymove != 0);

        // slow down
        if (player != null && (player.cheats & player_t.CF_NOMOMENTUM) != 0)
        {
            // debug option for no sliding at all
            mo.momx = mo.momy = 0;
            return;
        }

        if ((mo.flags & (mobjflag_t.MF_MISSILE | mobjflag_t.MF_SKULLFLY)) != 0)
            return; // no friction for missiles ever

        if (mo.z > mo.floorz)
            return; // no friction when airborne

        if ((mo.flags & mobjflag_t.MF_CORPSE) != 0)
        {
            // do not stop sliding
            //  if halfway off a step with some momentum
            if (mo.momx > Fixed.FRACUNIT / 4
                || mo.momx < -Fixed.FRACUNIT / 4
                || mo.momy > Fixed.FRACUNIT / 4
                || mo.momy < -Fixed.FRACUNIT / 4)
            {
                if (mo.floorz != mo.subsector.sector.floorheight)
                    return;
            }
        }

        if (mo.momx > -STOPSPEED
            && mo.momx < STOPSPEED
            && mo.momy > -STOPSPEED
            && mo.momy < STOPSPEED
            && (player == null
                || (player.cmd.forwardmove == 0
                    && player.cmd.sidemove == 0)))
        {
            // if in a walking frame, stop moving
            if (player != null && unchecked((uint)(player.mo!.state - statenum_t.S_PLAY_RUN1)) < 4)
                P_SetMobjState(player.mo, statenum_t.S_PLAY);

            mo.momx = 0;
            mo.momy = 0;
        }
        else
        {
            mo.momx = Fixed.FixedMul(mo.momx, FRICTION);
            mo.momy = Fixed.FixedMul(mo.momy, FRICTION);
        }
    }

    /// <summary>
    /// p_mobj.c <c>P_ZMovement</c>: a player below its floor (it stepped up)
    /// lowers its view by the step and starts the view's smooth step up;
    /// then the mobj moves by <see cref="mobj_t.momz"/>, a floater with a
    /// target drifts towards its height (<see cref="FLOATSPEED"/>), and the
    /// mobj lands on the floor (a player landing faster than 8 units a tic
    /// squats: <see cref="player_t.deltaviewheight"/>), falls under
    /// <see cref="GRAVITY"/> (twice as fast on the first tic) or hits the
    /// ceiling. Missiles explode on floors and ceilings.
    /// <para>
    /// The lost soul's floor bounce comes before the landing, as in
    /// linuxdoom-1.10 and Ultimate Doom's executable (Doom II v1.9's bounces
    /// after; Chocolate Doom picks by version: T9.2).
    /// </para>
    /// </summary>
    public void P_ZMovement(mobj_t mo)
    {
        // check for smooth step up
        if (mo.player != null && mo.z < mo.floorz)
        {
            mo.player.viewheight -= mo.floorz - mo.z;

            mo.player.deltaviewheight = (player_t.VIEWHEIGHT - mo.player.viewheight) >> 3;
        }

        // adjust height
        mo.z += mo.momz;

        if ((mo.flags & mobjflag_t.MF_FLOAT) != 0 && mo.target != null)
        {
            // float down towards target if too close
            if ((mo.flags & mobjflag_t.MF_SKULLFLY) == 0 && (mo.flags & mobjflag_t.MF_INFLOAT) == 0)
            {
                int dist = P_AproxDistance(mo.x - mo.target.x, mo.y - mo.target.y);

                int delta = (mo.target.z + (mo.height >> 1)) - mo.z;

                if (delta < 0 && dist < -(delta * 3))
                    mo.z -= FLOATSPEED;
                else if (delta > 0 && dist < (delta * 3))
                    mo.z += FLOATSPEED;
            }
        }

        // clip movement
        if (mo.z <= mo.floorz)
        {
            // hit the floor

            // Note (id):
            //  somebody left this after the setting momz to 0,
            //  kinda useless there.
            if ((mo.flags & mobjflag_t.MF_SKULLFLY) != 0)
            {
                // the skull slammed into something
                mo.momz = -mo.momz;
            }

            if (mo.momz < 0)
            {
                if (mo.player != null && mo.momz < -GRAVITY * 8)
                {
                    // Squat down.
                    // Decrease viewheight for a moment
                    // after hitting the ground (hard),
                    // and utter appropriate sound.
                    mo.player.deltaviewheight = mo.momz >> 3;
                    // S_StartSound (mo, sfx_oof); (T6.10)
                }
                mo.momz = 0;
            }
            mo.z = mo.floorz;

            if ((mo.flags & mobjflag_t.MF_MISSILE) != 0 && (mo.flags & mobjflag_t.MF_NOCLIP) == 0)
            {
                P_ExplodeMissile(mo);
                return;
            }
        }
        else if ((mo.flags & mobjflag_t.MF_NOGRAVITY) == 0)
        {
            if (mo.momz == 0)
                mo.momz = -GRAVITY * 2;
            else
                mo.momz -= GRAVITY;
        }

        if (mo.z + mo.height > mo.ceilingz)
        {
            // hit the ceiling
            if (mo.momz > 0)
                mo.momz = 0;
            {
                mo.z = mo.ceilingz - mo.height;
            }

            if ((mo.flags & mobjflag_t.MF_SKULLFLY) != 0)
            {
                // the skull slammed into something
                mo.momz = -mo.momz;
            }

            if ((mo.flags & mobjflag_t.MF_MISSILE) != 0 && (mo.flags & mobjflag_t.MF_NOCLIP) == 0)
            {
                P_ExplodeMissile(mo);
                return;
            }
        }
    }

    /// <summary>
    /// p_mobj.c <c>P_MobjThinker</c>: momentum movement
    /// (<see cref="P_XYMovement"/> when moving or charging,
    /// <see cref="P_ZMovement"/> when off the floor or moving vertically),
    /// then the state tics. The Nightmare respawn branch of <c>tics == -1</c>
    /// things comes with T6.1.
    /// </summary>
    public void P_MobjThinker(mobj_t mobj)
    {
        // momentum movement
        if (mobj.momx != 0 || mobj.momy != 0 || (mobj.flags & mobjflag_t.MF_SKULLFLY) != 0)
        {
            P_XYMovement(mobj);

            if (mobj.function == think_t.REMOVED)
                return; // mobj was removed
        }
        if (mobj.z != mobj.floorz || mobj.momz != 0)
        {
            P_ZMovement(mobj);

            if (mobj.function == think_t.REMOVED)
                return; // mobj was removed
        }

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
