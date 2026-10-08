using IsoDoom.Map;

namespace IsoDoom.Sim;

// p_enemy.c (and p_pspr.c's A_BFGSpray): the action functions of the mobj
// states, dispatched by P_SetMobjState (T6.1). Each is a stub until the task
// named in its summary; the player's pain and death ones are ported (T5.8),
// the sound alert and P_LookForPlayers (T6.2; sight in World.Sight.cs), and
// the monsters' movement, decisions, the shareware monsters' attacks, the
// death actions, the barrel's explosion and A_BossDeath (T6.4).

/// <summary>p_enemy.c <c>dirtype_t</c>: a monster's eight movement directions (<see cref="mobj_t.movedir"/>), counter-clockwise from east.</summary>
public enum dirtype_t
{
    DI_EAST,
    DI_NORTHEAST,
    DI_NORTH,
    DI_NORTHWEST,
    DI_WEST,
    DI_SOUTHWEST,
    DI_SOUTH,
    DI_SOUTHEAST,
    DI_NODIR,
    NUMDIRS,
}

public sealed partial class World
{
    /// <summary>
    /// Vanilla's call through <c>st->action.acp1</c> (p_mobj.c
    /// <c>P_SetMobjState</c>): runs a mobj state's action. A psprite action
    /// (<see cref="IsWeaponAction"/>; <see cref="A_CallWeapon"/>) never comes
    /// here: no mobj state has one, so it throws.
    /// </summary>
    public void A_Call(actionf_t action, mobj_t mobj)
    {
        switch (action)
        {
            case actionf_t.A_BFGSpray:
                A_BFGSpray(mobj);
                break;
            case actionf_t.A_Explode:
                A_Explode(mobj);
                break;
            case actionf_t.A_Pain:
                A_Pain(mobj);
                break;
            case actionf_t.A_PlayerScream:
                A_PlayerScream(mobj);
                break;
            case actionf_t.A_Fall:
                A_Fall(mobj);
                break;
            case actionf_t.A_XScream:
                A_XScream(mobj);
                break;
            case actionf_t.A_Look:
                A_Look(mobj);
                break;
            case actionf_t.A_Chase:
                A_Chase(mobj);
                break;
            case actionf_t.A_FaceTarget:
                A_FaceTarget(mobj);
                break;
            case actionf_t.A_PosAttack:
                A_PosAttack(mobj);
                break;
            case actionf_t.A_Scream:
                A_Scream(mobj);
                break;
            case actionf_t.A_SPosAttack:
                A_SPosAttack(mobj);
                break;
            case actionf_t.A_VileChase:
                A_VileChase(mobj);
                break;
            case actionf_t.A_VileStart:
                A_VileStart(mobj);
                break;
            case actionf_t.A_VileTarget:
                A_VileTarget(mobj);
                break;
            case actionf_t.A_VileAttack:
                A_VileAttack(mobj);
                break;
            case actionf_t.A_StartFire:
                A_StartFire(mobj);
                break;
            case actionf_t.A_Fire:
                A_Fire(mobj);
                break;
            case actionf_t.A_FireCrackle:
                A_FireCrackle(mobj);
                break;
            case actionf_t.A_Tracer:
                A_Tracer(mobj);
                break;
            case actionf_t.A_SkelWhoosh:
                A_SkelWhoosh(mobj);
                break;
            case actionf_t.A_SkelFist:
                A_SkelFist(mobj);
                break;
            case actionf_t.A_SkelMissile:
                A_SkelMissile(mobj);
                break;
            case actionf_t.A_FatRaise:
                A_FatRaise(mobj);
                break;
            case actionf_t.A_FatAttack1:
                A_FatAttack1(mobj);
                break;
            case actionf_t.A_FatAttack2:
                A_FatAttack2(mobj);
                break;
            case actionf_t.A_FatAttack3:
                A_FatAttack3(mobj);
                break;
            case actionf_t.A_BossDeath:
                A_BossDeath(mobj);
                break;
            case actionf_t.A_CPosAttack:
                A_CPosAttack(mobj);
                break;
            case actionf_t.A_CPosRefire:
                A_CPosRefire(mobj);
                break;
            case actionf_t.A_TroopAttack:
                A_TroopAttack(mobj);
                break;
            case actionf_t.A_SargAttack:
                A_SargAttack(mobj);
                break;
            case actionf_t.A_HeadAttack:
                A_HeadAttack(mobj);
                break;
            case actionf_t.A_BruisAttack:
                A_BruisAttack(mobj);
                break;
            case actionf_t.A_SkullAttack:
                A_SkullAttack(mobj);
                break;
            case actionf_t.A_Metal:
                A_Metal(mobj);
                break;
            case actionf_t.A_SpidRefire:
                A_SpidRefire(mobj);
                break;
            case actionf_t.A_BabyMetal:
                A_BabyMetal(mobj);
                break;
            case actionf_t.A_BspiAttack:
                A_BspiAttack(mobj);
                break;
            case actionf_t.A_Hoof:
                A_Hoof(mobj);
                break;
            case actionf_t.A_CyberAttack:
                A_CyberAttack(mobj);
                break;
            case actionf_t.A_PainAttack:
                A_PainAttack(mobj);
                break;
            case actionf_t.A_PainDie:
                A_PainDie(mobj);
                break;
            case actionf_t.A_KeenDie:
                A_KeenDie(mobj);
                break;
            case actionf_t.A_BrainPain:
                A_BrainPain(mobj);
                break;
            case actionf_t.A_BrainScream:
                A_BrainScream(mobj);
                break;
            case actionf_t.A_BrainDie:
                A_BrainDie(mobj);
                break;
            case actionf_t.A_BrainAwake:
                A_BrainAwake(mobj);
                break;
            case actionf_t.A_BrainSpit:
                A_BrainSpit(mobj);
                break;
            case actionf_t.A_SpawnSound:
                A_SpawnSound(mobj);
                break;
            case actionf_t.A_SpawnFly:
                A_SpawnFly(mobj);
                break;
            case actionf_t.A_BrainExplode:
                A_BrainExplode(mobj);
                break;
            default:
                throw new System.InvalidOperationException($"{action} is not a mobj action.");
        }
    }

    // ---- Enemy thinking: sound alert and looking for players (T6.2) ----

    /// <summary>p_local.h <c>MELEERANGE</c>: a monster this close reacts to a player behind its back (fixed_t).</summary>
    public const int MELEERANGE = 64 * Fixed.FRACUNIT;

    /// <summary>p_enemy.c <c>soundtarget</c>: the thing whose noise <see cref="P_RecursiveSound"/> floods the sectors with.</summary>
    public mobj_t? soundtarget;

    /// <summary>
    /// p_enemy.c <c>P_RecursiveSound</c>: the noise reaches
    /// <paramref name="sec"/> (its <see cref="sector_t.soundtarget"/>) after
    /// crossing <paramref name="soundblocks"/> sound-blocking lines, and
    /// spreads through every open two-sided line; a second
    /// <see cref="Line.ML_SOUNDBLOCK"/> line stops it. A sector already
    /// reached in this flood (<see cref="validcount"/>) is flooded again only
    /// when this path crossed fewer blocking lines.
    /// </summary>
    public void P_RecursiveSound(sector_t sec, int soundblocks)
    {
        // wake up all monsters in this sector
        if (sec.validcount == validcount
            && sec.soundtraversed <= soundblocks + 1)
        {
            return; // already flooded
        }

        sec.validcount = validcount;
        sec.soundtraversed = soundblocks + 1;
        sec.soundtarget = soundtarget;

        for (int i = 0; i < sec.linecount; i++)
        {
            line_t check = sec.lines[i];
            if ((check.flags & Line.ML_TWOSIDED) == 0)
                continue;

            P_LineOpening(check);

            if (openrange <= 0)
                continue; // closed door

            sector_t other;
            if (sides[check.sidenum[0]].sector == sec)
                other = sides[check.sidenum[1]].sector;
            else
                other = sides[check.sidenum[0]].sector;

            if ((check.flags & Line.ML_SOUNDBLOCK) != 0)
            {
                if (soundblocks == 0)
                    P_RecursiveSound(other, 1);
            }
            else
            {
                P_RecursiveSound(other, soundblocks);
            }
        }
    }

    /// <summary>
    /// p_enemy.c <c>P_NoiseAlert</c>: if a monster yells at a player, it will
    /// alert other monsters to the player: the noise <paramref name="emmiter"/>
    /// makes floods out from its sector (<see cref="P_RecursiveSound"/>),
    /// leaving <paramref name="target"/> as each reached sector's
    /// <see cref="sector_t.soundtarget"/>. Uses <see cref="validcount"/>.
    /// </summary>
    public void P_NoiseAlert(mobj_t target, mobj_t emmiter)
    {
        soundtarget = target;
        validcount++;
        P_RecursiveSound(emmiter.subsector.sector, 0);
    }

    /// <summary>
    /// p_enemy.c <c>P_LookForPlayers</c>: true when <paramref name="actor"/>
    /// sees a live player (<see cref="P_CheckSight"/>), who becomes its
    /// <see cref="mobj_t.target"/>. Unless <paramref name="allaround"/>, a
    /// player behind its back (more than 90° off its angle) counts only
    /// within <see cref="MELEERANGE"/>. Looks at no more than two players a
    /// call, from <see cref="mobj_t.lastlook"/> on (vanilla's loop, kept:
    /// with one player in the game it checks that player twice).
    /// </summary>
    public bool P_LookForPlayers(mobj_t actor, bool allaround)
    {
        int c = 0;
        int stop = (actor.lastlook - 1) & 3;

        for (; ; actor.lastlook = (actor.lastlook + 1) & 3)
        {
            if (!playeringame[actor.lastlook])
                continue;

            if (c++ == 2
                || actor.lastlook == stop)
            {
                // done looking
                return false;
            }

            player_t player = players[actor.lastlook];

            if (player.health <= 0)
                continue; // dead

            if (!P_CheckSight(actor, player.mo!))
                continue; // out of sight

            if (!allaround)
            {
                uint an = unchecked(Tables.R_PointToAngle2(actor.x, actor.y, player.mo!.x, player.mo.y) - actor.angle);

                if (an > Tables.ANG90 && an < Tables.ANG270)
                {
                    int dist = P_AproxDistance(player.mo.x - actor.x, player.mo.y - actor.y);
                    // if real close, react anyway
                    if (dist > MELEERANGE)
                        continue; // behind back
                }
            }

            actor.target = player.mo;
            return true;
        }
    }

    // ---- Movement and decisions (T6.4) ----

    /// <summary>p_enemy.c <c>opposite</c>: the direction opposite each <see cref="dirtype_t"/> (P_NewChaseDir related LUT).</summary>
    private static readonly dirtype_t[] opposite =
    [
        dirtype_t.DI_WEST, dirtype_t.DI_SOUTHWEST, dirtype_t.DI_SOUTH, dirtype_t.DI_SOUTHEAST,
        dirtype_t.DI_EAST, dirtype_t.DI_NORTHEAST, dirtype_t.DI_NORTH, dirtype_t.DI_NORTHWEST, dirtype_t.DI_NODIR,
    ];

    /// <summary>p_enemy.c <c>diags</c>: the diagonal towards a target, by <c>((deltay &lt; 0) &lt;&lt; 1) + (deltax &gt; 0)</c>.</summary>
    private static readonly dirtype_t[] diags =
    [
        dirtype_t.DI_NORTHWEST, dirtype_t.DI_NORTHEAST, dirtype_t.DI_SOUTHWEST, dirtype_t.DI_SOUTHEAST,
    ];

    /// <summary>p_enemy.c <c>xspeed</c>: a step's x per unit of speed in each direction (fixed_t; 47000 ≈ FRACUNIT·√½).</summary>
    private static readonly int[] xspeed = [Fixed.FRACUNIT, 47000, 0, -47000, -Fixed.FRACUNIT, -47000, 0, 47000];

    /// <summary>p_enemy.c <c>yspeed</c>: a step's y per unit of speed in each direction (fixed_t).</summary>
    private static readonly int[] yspeed = [0, 47000, Fixed.FRACUNIT, 47000, 0, -47000, -Fixed.FRACUNIT, -47000];

    /// <summary>
    /// p_enemy.c <c>P_CheckMeleeRange</c>: whether <paramref name="actor"/>'s
    /// target is close enough to hit (closer than <see cref="MELEERANGE"/>
    /// - 20 units + the target's radius, by <see cref="P_AproxDistance"/>) and in sight.
    /// </summary>
    public bool P_CheckMeleeRange(mobj_t actor)
    {
        if (actor.target == null)
            return false;

        mobj_t pl = actor.target;
        int dist = P_AproxDistance(pl.x - actor.x, pl.y - actor.y);

        if (dist >= MELEERANGE - 20 * Fixed.FRACUNIT + pl.info.radius)
            return false;

        if (!P_CheckSight(actor, actor.target))
            return false;

        return true;
    }

    /// <summary>
    /// p_enemy.c <c>P_CheckMissileRange</c>: whether <paramref name="actor"/>
    /// fires at its target now: in sight, and either just hurt
    /// (<see cref="mobjflag_t.MF_JUSTHIT"/>, cleared) or awake
    /// (<see cref="mobj_t.reactiontime"/> 0) and lucky: a <c>P_Random</c>
    /// at least the distance in units less 64 (less 128 more without a
    /// melee attack; halved for the revenant beyond 196, the Cyberdemon,
    /// the Spider Mastermind and the lost soul), capped at 200 (160 for the Cyberdemon).
    /// </summary>
    public bool P_CheckMissileRange(mobj_t actor)
    {
        if (!P_CheckSight(actor, actor.target!))
            return false;

        if ((actor.flags & mobjflag_t.MF_JUSTHIT) != 0)
        {
            // the target just hit the enemy,
            // so fight back!
            actor.flags &= ~mobjflag_t.MF_JUSTHIT;
            return true;
        }

        if (actor.reactiontime != 0)
            return false; // do not attack yet

        // OPTIMIZE: get this from a global checksight
        int dist = P_AproxDistance(actor.x - actor.target!.x, actor.y - actor.target.y) - 64 * Fixed.FRACUNIT;

        if (actor.info.meleestate == statenum_t.S_NULL)
            dist -= 128 * Fixed.FRACUNIT; // no melee attack, so fire more

        dist >>= 16;

        if (actor.type == mobjtype_t.MT_VILE)
        {
            if (dist > 14 * 64)
                return false; // too far away
        }

        if (actor.type == mobjtype_t.MT_UNDEAD)
        {
            if (dist < 196)
                return false; // close for fist attack
            dist >>= 1;
        }

        if (actor.type == mobjtype_t.MT_CYBORG
            || actor.type == mobjtype_t.MT_SPIDER
            || actor.type == mobjtype_t.MT_SKULL)
        {
            dist >>= 1;
        }

        if (dist > 200)
            dist = 200;

        if (actor.type == mobjtype_t.MT_CYBORG && dist > 160)
            dist = 160;

        if (P_Random() < dist)
            return false;

        return true;
    }

    /// <summary>
    /// p_enemy.c <c>P_Move</c>: moves <paramref name="actor"/> one step
    /// (its <c>speed</c> in map units) in its <see cref="mobj_t.movedir"/>;
    /// false when the move is blocked. A blocked floater rises or sinks
    /// towards the height it fits at (<see cref="floatok"/>); a blocked
    /// walker tries the special lines it touched (<see cref="P_UseSpecialLine"/>:
    /// doors it may open), true when one starts, and loses its direction.
    /// </summary>
    public bool P_Move(mobj_t actor)
    {
        if (actor.movedir == (int)dirtype_t.DI_NODIR)
            return false;

        if ((uint)actor.movedir >= 8)
            throw new System.InvalidOperationException("Weird actor->movedir!");

        int tryx = unchecked(actor.x + actor.info.speed * xspeed[actor.movedir]);
        int tryy = unchecked(actor.y + actor.info.speed * yspeed[actor.movedir]);

        bool try_ok = P_TryMove(actor, tryx, tryy);

        if (!try_ok)
        {
            // open any specials
            if ((actor.flags & mobjflag_t.MF_FLOAT) != 0 && floatok)
            {
                // must adjust height
                if (actor.z < tmfloorz)
                    actor.z += FLOATSPEED;
                else
                    actor.z -= FLOATSPEED;

                actor.flags |= mobjflag_t.MF_INFLOAT;
                return true;
            }

            if (numspechit == 0)
                return false;

            actor.movedir = (int)dirtype_t.DI_NODIR;
            bool good = false;
            while (numspechit-- != 0)
            {
                line_t ld = spechit[numspechit]!;
                // if the special is not a door
                // that can be opened,
                // return false
                if (P_UseSpecialLine(actor, ld, 0))
                    good = true;
            }
            return good;
        }
        else
        {
            actor.flags &= ~mobjflag_t.MF_INFLOAT;
        }

        if ((actor.flags & mobjflag_t.MF_FLOAT) == 0)
            actor.z = actor.floorz;
        return true;
    }

    /// <summary>
    /// p_enemy.c <c>P_TryWalk</c>: attempts to move <paramref name="actor"/>
    /// on in its current direction (<see cref="P_Move"/>); when it moved (or
    /// a door in the way started opening), it keeps the direction for 0-15
    /// more steps (<see cref="mobj_t.movecount"/>, a <c>P_Random</c>).
    /// </summary>
    public bool P_TryWalk(mobj_t actor)
    {
        if (!P_Move(actor))
            return false;

        actor.movecount = P_Random() & 15;
        return true;
    }

    /// <summary>
    /// p_enemy.c <c>P_NewChaseDir</c>: picks <paramref name="actor"/>'s next
    /// direction towards its target: the diagonal, then the two axes (the
    /// larger difference first, or at random), the old direction, every
    /// other direction in a random order and, last, turning around; each
    /// tried with <see cref="P_TryWalk"/>. <see cref="dirtype_t.DI_NODIR"/> when none works.
    /// </summary>
    public void P_NewChaseDir(mobj_t actor)
    {
        if (actor.target == null)
            throw new System.InvalidOperationException("P_NewChaseDir: called with no target");

        System.Span<dirtype_t> d = stackalloc dirtype_t[3];

        var olddir = (dirtype_t)actor.movedir;
        dirtype_t turnaround = opposite[(int)olddir];

        int deltax = unchecked(actor.target.x - actor.x);
        int deltay = unchecked(actor.target.y - actor.y);

        if (deltax > 10 * Fixed.FRACUNIT)
            d[1] = dirtype_t.DI_EAST;
        else if (deltax < -10 * Fixed.FRACUNIT)
            d[1] = dirtype_t.DI_WEST;
        else
            d[1] = dirtype_t.DI_NODIR;

        if (deltay < -10 * Fixed.FRACUNIT)
            d[2] = dirtype_t.DI_SOUTH;
        else if (deltay > 10 * Fixed.FRACUNIT)
            d[2] = dirtype_t.DI_NORTH;
        else
            d[2] = dirtype_t.DI_NODIR;

        // try direct route
        if (d[1] != dirtype_t.DI_NODIR
            && d[2] != dirtype_t.DI_NODIR)
        {
            actor.movedir = (int)diags[((deltay < 0 ? 1 : 0) << 1) + (deltax > 0 ? 1 : 0)];
            if (actor.movedir != (int)turnaround && P_TryWalk(actor))
                return;
        }

        // try other directions
        if (P_Random() > 200
            || abs(deltay) > abs(deltax))
        {
            dirtype_t tdir = d[1];
            d[1] = d[2];
            d[2] = tdir;
        }

        if (d[1] == turnaround)
            d[1] = dirtype_t.DI_NODIR;
        if (d[2] == turnaround)
            d[2] = dirtype_t.DI_NODIR;

        if (d[1] != dirtype_t.DI_NODIR)
        {
            actor.movedir = (int)d[1];
            if (P_TryWalk(actor))
            {
                // either moved forward or attacked
                return;
            }
        }

        if (d[2] != dirtype_t.DI_NODIR)
        {
            actor.movedir = (int)d[2];

            if (P_TryWalk(actor))
                return;
        }

        // there is no direct path to the player,
        // so pick another direction.
        if (olddir != dirtype_t.DI_NODIR)
        {
            actor.movedir = (int)olddir;

            if (P_TryWalk(actor))
                return;
        }

        // randomly determine direction of search
        if ((P_Random() & 1) != 0)
        {
            for (int tdir = (int)dirtype_t.DI_EAST;
                 tdir <= (int)dirtype_t.DI_SOUTHEAST;
                 tdir++)
            {
                if (tdir != (int)turnaround)
                {
                    actor.movedir = tdir;

                    if (P_TryWalk(actor))
                        return;
                }
            }
        }
        else
        {
            for (int tdir = (int)dirtype_t.DI_SOUTHEAST;
                 tdir != (int)dirtype_t.DI_EAST - 1;
                 tdir--)
            {
                if (tdir != (int)turnaround)
                {
                    actor.movedir = tdir;

                    if (P_TryWalk(actor))
                        return;
                }
            }
        }

        if (turnaround != dirtype_t.DI_NODIR)
        {
            actor.movedir = (int)turnaround;
            if (P_TryWalk(actor))
                return;
        }

        actor.movedir = (int)dirtype_t.DI_NODIR; // can not move
    }

    // ---- Action routines (T6.4) ----

    /// <summary>
    /// p_enemy.c <c>A_Look</c>: stay in state until a player is sighted.
    /// A monster in a sector a noise reached (<see cref="sector_t.soundtarget"/>,
    /// still shootable) wakes to it, a deaf one (<see cref="mobjflag_t.MF_AMBUSH"/>)
    /// only when it also sees it; else it looks for players
    /// (<see cref="P_LookForPlayers"/>, 180° in front). Waking: the see
    /// sound (one of the former humans' three or the imps' two at random)
    /// and the see state.
    /// </summary>
    public void A_Look(mobj_t actor)
    {
        actor.threshold = 0; // any shot will wake up
        mobj_t? targ = actor.subsector.sector.soundtarget;

        bool seeyou = false;
        if (targ != null
            && (targ.flags & mobjflag_t.MF_SHOOTABLE) != 0)
        {
            actor.target = targ;

            if ((actor.flags & mobjflag_t.MF_AMBUSH) != 0)
            {
                if (P_CheckSight(actor, actor.target))
                    seeyou = true;
            }
            else
            {
                seeyou = true;
            }
        }

        if (!seeyou && !P_LookForPlayers(actor, false))
            return;

        // go into chase state
        // seeyou:
        if (actor.info.seesound != sfxenum_t.sfx_None)
        {
            sfxenum_t sound;

            switch (actor.info.seesound)
            {
                case sfxenum_t.sfx_posit1:
                case sfxenum_t.sfx_posit2:
                case sfxenum_t.sfx_posit3:
                    sound = sfxenum_t.sfx_posit1 + P_Random() % 3;
                    break;

                case sfxenum_t.sfx_bgsit1:
                case sfxenum_t.sfx_bgsit2:
                    sound = sfxenum_t.sfx_bgsit1 + P_Random() % 2;
                    break;

                default:
                    sound = actor.info.seesound;
                    break;
            }

            if (actor.type == mobjtype_t.MT_SPIDER
                || actor.type == mobjtype_t.MT_CYBORG)
            {
                // full volume
                S_StartSound((mobj_t?)null, sound);
            }
            else
            {
                S_StartSound(actor, sound);
            }
        }

        P_SetMobjState(actor, actor.info.seestate);
    }

    /// <summary>
    /// p_enemy.c <c>A_Chase</c>: actor has a melee attack, so it tries to
    /// close as fast as possible. Counts down <see cref="mobj_t.reactiontime"/>
    /// and <see cref="mobj_t.threshold"/>, turns 45° towards its direction;
    /// without a live target looks all around for a player or goes back to
    /// its spawn state; after an attack only picks a new direction (not on
    /// Nightmare or <c>-fast</c>); attacks in melee range, or with a missile
    /// when <see cref="P_CheckMissileRange"/> says so (between moves only,
    /// except on Nightmare or <c>-fast</c>); else (in a netgame, looking for
    /// another player first) steps on (<see cref="P_Move"/>,
    /// <see cref="P_NewChaseDir"/> when its steps are counted down or it is
    /// blocked) and sometimes makes its active sound.
    /// </summary>
    public void A_Chase(mobj_t actor)
    {
        if (actor.reactiontime != 0)
            actor.reactiontime--;

        // modify target threshold
        if (actor.threshold != 0)
        {
            if (actor.target == null
                || actor.target.health <= 0)
            {
                actor.threshold = 0;
            }
            else
            {
                actor.threshold--;
            }
        }

        // turn towards movement direction if not there yet
        if (actor.movedir < 8)
        {
            actor.angle &= 7u << 29;
            int delta = unchecked((int)(actor.angle - ((uint)actor.movedir << 29)));

            if (delta > 0)
                actor.angle = unchecked(actor.angle - Tables.ANG90 / 2);
            else if (delta < 0)
                actor.angle = unchecked(actor.angle + Tables.ANG90 / 2);
        }

        if (actor.target == null
            || (actor.target.flags & mobjflag_t.MF_SHOOTABLE) == 0)
        {
            // look for a new target
            if (P_LookForPlayers(actor, true))
                return; // got a new target

            P_SetMobjState(actor, actor.info.spawnstate);
            return;
        }

        // do not attack twice in a row
        if ((actor.flags & mobjflag_t.MF_JUSTATTACKED) != 0)
        {
            actor.flags &= ~mobjflag_t.MF_JUSTATTACKED;
            if (gameskill != skill_t.sk_nightmare && !fastparm)
                P_NewChaseDir(actor);
            return;
        }

        // check for melee attack
        if (actor.info.meleestate != statenum_t.S_NULL
            && P_CheckMeleeRange(actor))
        {
            if (actor.info.attacksound != sfxenum_t.sfx_None)
                S_StartSound(actor, actor.info.attacksound);

            P_SetMobjState(actor, actor.info.meleestate);
            return;
        }

        // check for missile attack
        if (actor.info.missilestate != statenum_t.S_NULL)
        {
            if (!(gameskill < skill_t.sk_nightmare
                  && !fastparm && actor.movecount != 0)
                && P_CheckMissileRange(actor))
            {
                P_SetMobjState(actor, actor.info.missilestate);
                actor.flags |= mobjflag_t.MF_JUSTATTACKED;
                return;
            }
        }

        // ?
        // nomissile:
        // possibly choose another target
        if (netgame
            && actor.threshold == 0
            && !P_CheckSight(actor, actor.target))
        {
            if (P_LookForPlayers(actor, true))
                return; // got a new target
        }

        // chase towards player
        if (--actor.movecount < 0
            || !P_Move(actor))
        {
            P_NewChaseDir(actor);
        }

        // make active sound
        if (actor.info.activesound != sfxenum_t.sfx_None
            && P_Random() < 3)
        {
            S_StartSound(actor, actor.info.activesound);
        }
    }

    /// <summary>
    /// p_enemy.c <c>A_FaceTarget</c>: turns <paramref name="actor"/> to face
    /// its target (no longer deaf: <see cref="mobjflag_t.MF_AMBUSH"/> cleared),
    /// off by up to ±45° (two <c>P_Random</c>s) for a fuzzy (<see cref="mobjflag_t.MF_SHADOW"/>) one.
    /// </summary>
    public void A_FaceTarget(mobj_t actor)
    {
        if (actor.target == null)
            return;

        actor.flags &= ~mobjflag_t.MF_AMBUSH;

        actor.angle = Tables.R_PointToAngle2(actor.x,
                                             actor.y,
                                             actor.target.x,
                                             actor.target.y);

        if ((actor.target.flags & mobjflag_t.MF_SHADOW) != 0)
            actor.angle = unchecked(actor.angle + (uint)((P_Random() - P_Random()) << 21));
    }

    /// <summary>
    /// p_enemy.c <c>A_PosAttack</c>: the zombieman's shot: faces the target,
    /// aims (<see cref="P_AimLineAttack"/>) and fires one bullet up to ±22.5°
    /// off (3-15 damage).
    /// </summary>
    public void A_PosAttack(mobj_t actor)
    {
        if (actor.target == null)
            return;

        A_FaceTarget(actor);
        uint angle = actor.angle;
        int slope = P_AimLineAttack(actor, angle, MISSILERANGE);

        S_StartSound(actor, sfxenum_t.sfx_pistol);
        angle = unchecked(angle + (uint)((P_Random() - P_Random()) << 20));
        int damage = ((P_Random() % 5) + 1) * 3;
        P_LineAttack(actor, angle, MISSILERANGE, slope, damage);
    }

    /// <summary>
    /// p_enemy.c <c>A_SPosAttack</c>: the shotgun guy's (and the Spider
    /// Mastermind's) three pellets, each up to ±22.5° off (3-15 damage).
    /// </summary>
    public void A_SPosAttack(mobj_t actor)
    {
        if (actor.target == null)
            return;

        S_StartSound(actor, sfxenum_t.sfx_shotgn);
        A_FaceTarget(actor);
        uint bangle = actor.angle;
        int slope = P_AimLineAttack(actor, bangle, MISSILERANGE);

        for (int i = 0; i < 3; i++)
        {
            uint angle = unchecked(bangle + (uint)((P_Random() - P_Random()) << 20));
            int damage = ((P_Random() % 5) + 1) * 3;
            P_LineAttack(actor, angle, MISSILERANGE, slope, damage);
        }
    }

    /// <summary>
    /// p_enemy.c <c>A_TroopAttack</c>: the imp's scratch in melee range
    /// (3-24 damage), else its fireball (<see cref="P_SpawnMissile"/>,
    /// <see cref="mobjtype_t.MT_TROOPSHOT"/>).
    /// </summary>
    public void A_TroopAttack(mobj_t actor)
    {
        if (actor.target == null)
            return;

        A_FaceTarget(actor);
        if (P_CheckMeleeRange(actor))
        {
            S_StartSound(actor, sfxenum_t.sfx_claw);
            int damage = (P_Random() % 8 + 1) * 3;
            P_DamageMobj(actor.target, actor, actor, damage);
            return;
        }

        // launch a missile
        P_SpawnMissile(actor, actor.target, mobjtype_t.MT_TROOPSHOT);
    }

    /// <summary>p_enemy.c <c>A_SargAttack</c>: the demon's (and spectre's) bite in melee range (4-40 damage).</summary>
    public void A_SargAttack(mobj_t actor)
    {
        if (actor.target == null)
            return;

        A_FaceTarget(actor);
        if (P_CheckMeleeRange(actor))
        {
            int damage = ((P_Random() % 10) + 1) * 4;
            P_DamageMobj(actor.target, actor, actor, damage);
        }
    }

    /// <summary>
    /// p_enemy.c <c>A_BruisAttack</c>: the baron's claw in melee range (10-80
    /// damage), else its ball (<see cref="mobjtype_t.MT_BRUISERSHOT"/>). It
    /// does not face the target itself: its attack states' <c>A_FaceTarget</c> does.
    /// </summary>
    public void A_BruisAttack(mobj_t actor)
    {
        if (actor.target == null)
            return;

        if (P_CheckMeleeRange(actor))
        {
            S_StartSound(actor, sfxenum_t.sfx_claw);
            int damage = (P_Random() % 8 + 1) * 10;
            P_DamageMobj(actor.target, actor, actor, damage);
            return;
        }

        // launch a missile
        P_SpawnMissile(actor, actor.target, mobjtype_t.MT_BRUISERSHOT);
    }

    /// <summary>
    /// p_enemy.c <c>A_Scream</c>: the death sound (one of the former humans'
    /// three or the imps' two at random), at full volume for the Spider
    /// Mastermind and the Cyberdemon.
    /// </summary>
    public void A_Scream(mobj_t actor)
    {
        sfxenum_t sound;

        switch (actor.info.deathsound)
        {
            case sfxenum_t.sfx_None:
                return;

            case sfxenum_t.sfx_podth1:
            case sfxenum_t.sfx_podth2:
            case sfxenum_t.sfx_podth3:
                sound = sfxenum_t.sfx_podth1 + P_Random() % 3;
                break;

            case sfxenum_t.sfx_bgdth1:
            case sfxenum_t.sfx_bgdth2:
                sound = sfxenum_t.sfx_bgdth1 + P_Random() % 2;
                break;

            default:
                sound = actor.info.deathsound;
                break;
        }

        // Check for bosses.
        if (actor.type == mobjtype_t.MT_SPIDER
            || actor.type == mobjtype_t.MT_CYBORG)
        {
            // full volume
            S_StartSound((mobj_t?)null, sound);
        }
        else
        {
            S_StartSound(actor, sound);
        }
    }

    /// <summary>p_enemy.c <c>A_XScream</c>: the gibbing sound.</summary>
    public void A_XScream(mobj_t actor)
    {
        S_StartSound(actor, sfxenum_t.sfx_slop);
    }

    /// <summary>
    /// p_enemy.c <c>A_Explode</c>: the radius attack (128) of an exploding
    /// barrel or rocket (<see cref="P_RadiusAttack"/>), credited to its <see cref="mobj_t.target"/>.
    /// </summary>
    public void A_Explode(mobj_t thingy)
    {
        P_RadiusAttack(thingy, thingy.target, 128);
    }

    /// <summary>
    /// p_enemy.c <c>CheckBossEnd</c> (Chocolate Doom): whether the death of a
    /// <paramref name="motype"/> may trigger the end of episode special. Before
    /// The Ultimate Doom (every game mode but <see cref="IsoDoom.Wad.GameMode.retail"/>,
    /// as Chocolate Doom's default <c>gameversion</c>): any boss on map 8,
    /// barons only in episode 1; The Ultimate Doom: each episode's own boss.
    /// </summary>
    private bool CheckBossEnd(mobjtype_t motype)
    {
        if (gamemode != IsoDoom.Wad.GameMode.retail) // gameversion < exe_ultimate
        {
            if (gamemap != 8)
                return false;

            // Baron death on later episodes is nothing special.
            if (motype == mobjtype_t.MT_BRUISER && gameepisode != 1)
                return false;

            return true;
        }

        // New logic that appeared in Ultimate Doom.
        // Looks like the logic was overhauled while adding in the
        // episode 4 support.  Now bosses only trigger on their
        // specific episode.
        return gameepisode switch
        {
            1 => gamemap == 8 && motype == mobjtype_t.MT_BRUISER,
            2 => gamemap == 8 && motype == mobjtype_t.MT_CYBORG,
            3 => gamemap == 8 && motype == mobjtype_t.MT_SPIDER,
            4 => (gamemap == 6 && motype == mobjtype_t.MT_CYBORG)
                 || (gamemap == 8 && motype == mobjtype_t.MT_SPIDER),
            _ => gamemap == 8,
        };
    }

    /// <summary>
    /// p_enemy.c <c>A_BossDeath</c>: possibly trigger special effects if on
    /// first boss level. When the last boss of its type dies on a boss map
    /// (<see cref="CheckBossEnd"/>; Doom II's MAP07: the mancubi and
    /// arachnotrons) with a player alive: E1M8 lowers the floors tagged 666
    /// to their lowest neighbour (E4M6 opens doors 666 fast, E4M8 lowers
    /// 666; MAP07 lowers 666 for the mancubi and raises 667 by its texture
    /// for the arachnotrons); any other ends the level (<see cref="G_ExitLevel"/>).
    /// </summary>
    public void A_BossDeath(mobj_t mo)
    {
        if (gamemode == IsoDoom.Wad.GameMode.commercial)
        {
            if (gamemap != 7)
                return;

            if (mo.type != mobjtype_t.MT_FATSO
                && mo.type != mobjtype_t.MT_BABY)
                return;
        }
        else
        {
            if (!CheckBossEnd(mo.type))
                return;
        }

        // make sure there is a player alive for victory
        int i;
        for (i = 0; i < MAXPLAYERS; i++)
        {
            if (playeringame[i] && players[i].health > 0)
                break;
        }

        if (i == MAXPLAYERS)
            return; // no one left alive, so do not end game

        // scan the remaining thinkers to see
        // if all bosses are dead
        foreach (mobj_t mo2 in Mobjs())
        {
            if (mo2 != mo
                && mo2.type == mo.type
                && mo2.health > 0)
            {
                // other boss not dead
                return;
            }
        }

        // victory!
        if (gamemode == IsoDoom.Wad.GameMode.commercial)
        {
            if (gamemap == 7)
            {
                if (mo.type == mobjtype_t.MT_FATSO)
                {
                    EV_DoFloor(JunkLine(666), floor_e.lowerFloorToLowest);
                    return;
                }

                if (mo.type == mobjtype_t.MT_BABY)
                {
                    EV_DoFloor(JunkLine(667), floor_e.raiseToTexture);
                    return;
                }
            }
        }
        else
        {
            switch (gameepisode)
            {
                case 1:
                    EV_DoFloor(JunkLine(666), floor_e.lowerFloorToLowest);
                    return;

                case 4:
                    switch (gamemap)
                    {
                        case 6:
                            EV_DoDoor(JunkLine(666), vldoor_e.vld_blazeOpen);
                            return;

                        case 8:
                            EV_DoFloor(JunkLine(666), floor_e.lowerFloorToLowest);
                            return;
                    }
                    break;
            }
        }

        G_ExitLevel();
    }

    /// <summary>
    /// p_enemy.c's <c>line_t junk</c>: a line in no map with only its tag
    /// set, for <see cref="A_BossDeath"/> (and A_KeenDie) to start
    /// <see cref="EV_DoFloor"/>/<see cref="EV_DoDoor"/> on the sectors tagged <paramref name="tag"/>.
    /// </summary>
    private static line_t JunkLine(short tag) => new(Line.Unlinked(tag), null, null);

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

    // ---- stubs ----

    /// <summary>p_pspr.c <c>A_BFGSpray</c>: the BFG ball's 40 tracers. A stub until T9.3.</summary>
    public void A_BFGSpray(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_VileChase</c>: the arch-vile's chase, raising corpses. A stub until T10.3.</summary>
    public void A_VileChase(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_VileStart</c>: the arch-vile's attack sound. A stub until T10.3.</summary>
    public void A_VileStart(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_VileTarget</c>: the arch-vile's fire on its target. A stub until T10.3.</summary>
    public void A_VileTarget(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_VileAttack</c>: the arch-vile's blast. A stub until T10.3.</summary>
    public void A_VileAttack(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_StartFire</c>: the arch-vile's fire starting. A stub until T10.3.</summary>
    public void A_StartFire(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_Fire</c>: the arch-vile's fire following its target. A stub until T10.3.</summary>
    public void A_Fire(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_FireCrackle</c>: the arch-vile's fire crackling. A stub until T10.3.</summary>
    public void A_FireCrackle(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_Tracer</c>: the revenant's homing missile. A stub until T10.3.</summary>
    public void A_Tracer(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_SkelWhoosh</c>: the revenant's punch swing. A stub until T10.3.</summary>
    public void A_SkelWhoosh(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_SkelFist</c>: the revenant's punch. A stub until T10.3.</summary>
    public void A_SkelFist(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_SkelMissile</c>: the revenant's missile. A stub until T10.3.</summary>
    public void A_SkelMissile(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_FatRaise</c>: the mancubus' attack sound. A stub until T10.3.</summary>
    public void A_FatRaise(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_FatAttack1</c>: the mancubus' first volley. A stub until T10.3.</summary>
    public void A_FatAttack1(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_FatAttack2</c>: the mancubus' second volley. A stub until T10.3.</summary>
    public void A_FatAttack2(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_FatAttack3</c>: the mancubus' third volley. A stub until T10.3.</summary>
    public void A_FatAttack3(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_CPosAttack</c>: the chaingunner's shot. A stub until T10.3.</summary>
    public void A_CPosAttack(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_CPosRefire</c>: the chaingunner's refire. A stub until T10.3.</summary>
    public void A_CPosRefire(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_HeadAttack</c>: the cacodemon's bite or ball. A stub until T9.2.</summary>
    public void A_HeadAttack(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_SkullAttack</c>: the lost soul's charge. A stub until T9.2.</summary>
    public void A_SkullAttack(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_Metal</c>: the Cyberdemon's and Spider Mastermind's steps. A stub until T9.2.</summary>
    public void A_Metal(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_SpidRefire</c>: the Spider Mastermind's refire. A stub until T9.2.</summary>
    public void A_SpidRefire(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_BabyMetal</c>: the arachnotron's steps. A stub until T10.3.</summary>
    public void A_BabyMetal(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_BspiAttack</c>: the arachnotron's plasma. A stub until T10.3.</summary>
    public void A_BspiAttack(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_Hoof</c>: the Cyberdemon's hoof. A stub until T9.2.</summary>
    public void A_Hoof(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_CyberAttack</c>: the Cyberdemon's rocket. A stub until T9.2.</summary>
    public void A_CyberAttack(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_PainAttack</c>: the pain elemental's lost soul. A stub until T10.3.</summary>
    public void A_PainAttack(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_PainDie</c>: the pain elemental's death souls. A stub until T10.3.</summary>
    public void A_PainDie(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_KeenDie</c>: Commander Keen's death, opening tag 666. A stub until T10.3.</summary>
    public void A_KeenDie(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_BrainPain</c>: the Icon of Sin's pain sound. A stub until T10.4.</summary>
    public void A_BrainPain(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_BrainScream</c>: the Icon of Sin's death explosions. A stub until T10.4.</summary>
    public void A_BrainScream(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_BrainDie</c>: the Icon of Sin's death: the level ends. A stub until T10.4.</summary>
    public void A_BrainDie(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_BrainAwake</c>: the Icon of Sin's targets and sight sound. A stub until T10.4.</summary>
    public void A_BrainAwake(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_BrainSpit</c>: the Icon of Sin's spawn cube. A stub until T10.4.</summary>
    public void A_BrainSpit(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_SpawnSound</c>: the spawn cube's sound. A stub until T10.4.</summary>
    public void A_SpawnSound(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_SpawnFly</c>: the spawn cube's monster. A stub until T10.4.</summary>
    public void A_SpawnFly(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_BrainExplode</c>: the Icon of Sin's explosions. A stub until T10.4.</summary>
    public void A_BrainExplode(mobj_t actor)
    {
    }

}
