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
            mobj.tics = StateTics(state); // st->tics (the fast monsters': T6.4)
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

        if (mo.info.deathsound != sfxenum_t.sfx_None)
            S_StartSound(mo, mo.info.deathsound);
    }

    /// <summary>
    /// p_mobj.c <c>P_CheckMissileSpawn</c>: a new missile's first tics cut by
    /// 0-3 (a <c>P_Random</c>, at least 1), moved half a step forward so an
    /// angle can be computed if it immediately explodes, and exploded
    /// (<see cref="P_ExplodeMissile"/>) when it does not fit there. Pulled
    /// forward from T6.5 for the imps' and barons' balls (T6.4).
    /// </summary>
    public void P_CheckMissileSpawn(mobj_t th)
    {
        th.tics -= P_Random() & 3;
        if (th.tics < 1)
            th.tics = 1;

        // move a little forward so an angle can
        // be computed if it immediately explodes
        th.x += th.momx >> 1;
        th.y += th.momy >> 1;
        th.z += th.momz >> 1;

        if (!P_TryMove(th, th.x, th.y))
            P_ExplodeMissile(th);
    }

    /// <summary>
    /// p_mobj.c <c>P_SpawnMissile</c>: a missile of <paramref name="type"/>
    /// fired by <paramref name="source"/> (its <see cref="mobj_t.target"/>)
    /// from 32 units above its feet at <paramref name="dest"/>: aimed at it
    /// (up to ±22.5° off at a fuzzy one), at its speed
    /// (<see cref="MissileSpeed"/>), climbing or dropping to reach its feet'
    /// height over the distance; then <see cref="P_CheckMissileSpawn"/>.
    /// Pulled forward from T6.5 (T6.4).
    /// </summary>
    public mobj_t P_SpawnMissile(mobj_t source, mobj_t dest, mobjtype_t type)
    {
        mobj_t th = P_SpawnMobj(source.x,
                                source.y,
                                source.z + 4 * 8 * Fixed.FRACUNIT, type);

        if (th.info.seesound != sfxenum_t.sfx_None)
            S_StartSound(th, th.info.seesound);

        th.target = source; // where it came from
        uint an = Tables.R_PointToAngle2(source.x, source.y, dest.x, dest.y);

        // fuzzy player
        if ((dest.flags & mobjflag_t.MF_SHADOW) != 0)
            an = unchecked(an + (uint)((P_Random() - P_Random()) << 20));

        int speed = MissileSpeed(type); // th->info->speed (the fast monsters': T6.4)
        th.angle = an;
        an >>= Tables.ANGLETOFINESHIFT;
        th.momx = Fixed.FixedMul(speed, Tables.finecosine[(int)an]);
        th.momy = Fixed.FixedMul(speed, Tables.finesine[(int)an]);

        int dist = P_AproxDistance(dest.x - source.x, dest.y - source.y);
        dist /= speed;

        if (dist < 1)
            dist = 1;

        th.momz = (dest.z - source.z) / dist;
        P_CheckMissileSpawn(th);

        return th;
    }

    /// <summary>
    /// p_mobj.c <c>P_SpawnPlayerMissile</c>: a missile of
    /// <paramref name="type"/> (the rocket; T9.3's plasma and BFG balls)
    /// fired by the player's mobj <paramref name="source"/> from 32 units
    /// above its feet, along its angle, or 1&lt;&lt;26 (5.6°) to the left or
    /// else the right of it when <see cref="P_AimLineAttack"/> finds a target
    /// there (and climbing at the slope to it; flat without one); then
    /// <see cref="P_CheckMissileSpawn"/>. The speed is the table's (vanilla's
    /// <c>-fast</c> changes no player missile).
    /// </summary>
    public mobj_t P_SpawnPlayerMissile(mobj_t source, mobjtype_t type)
    {
        // see which target is to be aimed at
        uint an = P_AimAssist(source, 16 * 64 * Fixed.FRACUNIT); // vanilla: source->angle (SPEC §12 T6.7)
        int slope = P_AimLineAttack(source, an, 16 * 64 * Fixed.FRACUNIT);

        if (linetarget == null)
        {
            an = unchecked(an + (1u << 26));
            slope = P_AimLineAttack(source, an, 16 * 64 * Fixed.FRACUNIT);

            if (linetarget == null)
            {
                an = unchecked(an - (2u << 26));
                slope = P_AimLineAttack(source, an, 16 * 64 * Fixed.FRACUNIT);
            }

            if (linetarget == null)
            {
                an = source.angle;
                slope = 0;
            }
        }

        int x = source.x;
        int y = source.y;
        int z = source.z + 4 * 8 * Fixed.FRACUNIT;

        mobj_t th = P_SpawnMobj(x, y, z, type);

        if (th.info.seesound != sfxenum_t.sfx_None)
            S_StartSound(th, th.info.seesound);

        th.target = source;
        th.angle = an;
        th.momx = Fixed.FixedMul(th.info.speed, Tables.finecosine[(int)(an >> Tables.ANGLETOFINESHIFT)]);
        th.momy = Fixed.FixedMul(th.info.speed, Tables.finesine[(int)(an >> Tables.ANGLETOFINESHIFT)]);
        th.momz = Fixed.FixedMul(th.info.speed, slope);

        P_CheckMissileSpawn(th);

        return th;
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
                    S_StartSound(mo, sfxenum_t.sfx_oof);
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
    /// then the state tics, calling the actions of the states entered
    /// (<see cref="P_SetMobjState"/>). A thing whose state lasts forever
    /// (<c>tics == -1</c>) is a candidate for the Nightmare respawn: a
    /// monster's corpse, 12 seconds after it died, on every 32nd tic with
    /// a chance of 5 in 256 (<see cref="P_NightmareRespawn"/>).
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
        else
        {
            // check for nightmare respawn
            if ((mobj.flags & mobjflag_t.MF_COUNTKILL) == 0)
                return;

            if (!respawnmonsters)
                return;

            mobj.movecount++;

            if (mobj.movecount < 12 * SimInfo.TICRATE)
                return;

            if ((leveltime & 31) != 0)
                return;

            if (P_Random() > 4)
                return;

            P_NightmareRespawn(mobj);
        }
    }

    /// <summary>
    /// p_mobj.c <c>P_NightmareRespawn</c>: unless something occupies its
    /// spawn point, the monster <paramref name="mobj"/> (a corpse) is
    /// replaced by a new one of its type at its map spot, with teleport fogs
    /// at both places.
    /// </summary>
    public void P_NightmareRespawn(mobj_t mobj)
    {
        int x = mobj.spawnpoint.X << Fixed.FRACBITS;
        int y = mobj.spawnpoint.Y << Fixed.FRACBITS;

        // somthing is occupying it's position?
        if (!P_CheckPosition(mobj, x, y))
            return; // no respwan

        // spawn a teleport fog at old spot
        // because of removal of the body?
        mobj_t mo = P_SpawnMobj(mobj.x,
                                mobj.y,
                                mobj.subsector.sector.floorheight, mobjtype_t.MT_TFOG);
        // initiate teleport sound
        S_StartSound(mo, sfxenum_t.sfx_telept);

        // spawn a teleport fog at the new spot
        subsector_t ss = R_PointInSubsector(x, y);

        mo = P_SpawnMobj(x, y, ss.sector.floorheight, mobjtype_t.MT_TFOG);

        S_StartSound(mo, sfxenum_t.sfx_telept);

        // spawn the new monster
        MapThing mthing = mobj.spawnpoint;

        // spawn it
        int z;
        if ((mobj.info.flags & mobjflag_t.MF_SPAWNCEILING) != 0)
            z = ONCEILINGZ;
        else
            z = ONFLOORZ;

        // inherit attributes from deceased one
        mo = P_SpawnMobj(x, y, z, mobj.type);
        mo.spawnpoint = mobj.spawnpoint;
        mo.angle = unchecked(Tables.ANG45 * (uint)(mthing.Angle / 45));

        if ((mthing.Options & MapThing.MTF_AMBUSH) != 0)
            mo.flags |= mobjflag_t.MF_AMBUSH;

        mo.reactiontime = 18;

        // remove the old monster,
        P_RemoveMobj(mobj);
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
        mobj.tics = StateTics(info.spawnstate); // st->tics (T6.4)
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

        // stop any playing sound
        S_StopSound(mobj);

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

        // setup gun psprite
        P_SetupPsprites(p);

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
