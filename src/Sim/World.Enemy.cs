using IsoDoom.Map;

namespace IsoDoom.Sim;

// p_enemy.c (and p_pspr.c's A_BFGSpray): the action functions of the mobj
// states, dispatched by P_SetMobjState (T6.1). Each is a stub until the task
// named in its summary; the player's pain and death ones are ported (T5.8),
// and so are the sound alert and P_LookForPlayers (T6.2; sight in World.Sight.cs).
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

    /// <summary>p_enemy.c <c>A_Explode</c>: the radius attack of a barrel or rocket. A stub until T6.4.</summary>
    public void A_Explode(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_XScream</c>: the gibbing sound. A stub until T6.4.</summary>
    public void A_XScream(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_Look</c>: stay in the spawn state until a player is seen or heard. A stub until T6.4.</summary>
    public void A_Look(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_Chase</c>: walk towards the target, attack when in range. A stub until T6.4.</summary>
    public void A_Chase(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_FaceTarget</c>: turn towards the target. A stub until T6.4.</summary>
    public void A_FaceTarget(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_PosAttack</c>: the zombieman's shot. A stub until T6.4.</summary>
    public void A_PosAttack(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_Scream</c>: the death sound. A stub until T6.4.</summary>
    public void A_Scream(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_SPosAttack</c>: the shotgun guy's (and the Spider Mastermind's) shots. A stub until T6.4.</summary>
    public void A_SPosAttack(mobj_t actor)
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

    /// <summary>p_enemy.c <c>A_BossDeath</c>: the boss level's special when the last boss dies. A stub until T6.4.</summary>
    public void A_BossDeath(mobj_t actor)
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

    /// <summary>p_enemy.c <c>A_TroopAttack</c>: the imp's scratch or fireball. A stub until T6.4.</summary>
    public void A_TroopAttack(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_SargAttack</c>: the demon's bite. A stub until T6.4.</summary>
    public void A_SargAttack(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_HeadAttack</c>: the cacodemon's bite or ball. A stub until T9.2.</summary>
    public void A_HeadAttack(mobj_t actor)
    {
    }

    /// <summary>p_enemy.c <c>A_BruisAttack</c>: the baron's claw or ball. A stub until T6.4.</summary>
    public void A_BruisAttack(mobj_t actor)
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
