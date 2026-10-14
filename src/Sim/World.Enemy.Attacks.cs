using IsoDoom.Map;

namespace IsoDoom.Sim;

// p_enemy.c: the attacks and special deaths of the registered and Doom II
// monsters (T10.3): the chaingunner, the revenant, the arch-vile and its
// fire, the mancubus, the arachnotron, the pain elemental, Commander Keen;
// and, for Doom II's maps, the registered ones T9.2 names (the cacodemon,
// the lost soul's charge, the Cyberdemon and the Spider Mastermind). In
// p_enemy.c's order. The Icon of Sin's are T10.4's.
public sealed partial class World
{
    /// <summary>
    /// p_enemy.c <c>A_KeenDie</c>: DOOM II special, map 32. Commander Keen
    /// falls (<see cref="A_Fall"/>); when no other live Keen is left, the
    /// doors tagged 666 open (<see cref="vldoor_e.vld_open"/>), on any map.
    /// </summary>
    public void A_KeenDie(mobj_t mo)
    {
        A_Fall(mo);

        // scan the remaining thinkers
        // to see if all Keens are dead
        foreach (mobj_t mo2 in Mobjs())
        {
            if (mo2 != mo
                && mo2.type == mo.type
                && mo2.health > 0)
            {
                // other Keen not dead
                return;
            }
        }

        EV_DoDoor(JunkLine(666), vldoor_e.vld_open);
    }

    /// <summary>
    /// p_enemy.c <c>A_CPosAttack</c>: the chaingunner's bullet (the shotgun's
    /// sound): faces the target, aims and fires one bullet up to ±22.5° off (3-15 damage).
    /// </summary>
    public void A_CPosAttack(mobj_t actor)
    {
        if (actor.target == null)
            return;

        S_StartSound(actor, sfxenum_t.sfx_shotgn);
        A_FaceTarget(actor);
        uint bangle = actor.angle;
        int slope = P_AimLineAttack(actor, bangle, MISSILERANGE);

        uint angle = unchecked(bangle + (uint)((P_Random() - P_Random()) << 20));
        int damage = ((P_Random() % 5) + 1) * 3;
        P_LineAttack(actor, angle, MISSILERANGE, slope, damage);
    }

    /// <summary>
    /// p_enemy.c <c>A_CPosRefire</c>: keep firing unless target got out of
    /// sight: faces the target; unless a <c>P_Random</c> under 40, a dead,
    /// lost or unseen target sends the chaingunner back to its see state.
    /// </summary>
    public void A_CPosRefire(mobj_t actor)
    {
        // keep firing unless target got out of sight
        A_FaceTarget(actor);

        if (P_Random() < 40)
            return;

        if (actor.target == null
            || actor.target.health <= 0
            || !P_CheckSight(actor, actor.target))
        {
            P_SetMobjState(actor, actor.info.seestate);
        }
    }

    /// <summary>p_enemy.c <c>A_SpidRefire</c>: the Spider Mastermind's <see cref="A_CPosRefire"/>, going on unless a <c>P_Random</c> of 10 or more.</summary>
    public void A_SpidRefire(mobj_t actor)
    {
        // keep firing unless target got out of sight
        A_FaceTarget(actor);

        if (P_Random() < 10)
            return;

        if (actor.target == null
            || actor.target.health <= 0
            || !P_CheckSight(actor, actor.target))
        {
            P_SetMobjState(actor, actor.info.seestate);
        }
    }

    /// <summary>p_enemy.c <c>A_BspiAttack</c>: the arachnotron's plasma (<see cref="mobjtype_t.MT_ARACHPLAZ"/>).</summary>
    public void A_BspiAttack(mobj_t actor)
    {
        if (actor.target == null)
            return;

        A_FaceTarget(actor);

        // launch a missile
        P_SpawnMissile(actor, actor.target, mobjtype_t.MT_ARACHPLAZ);
    }

    /// <summary>
    /// p_enemy.c <c>A_HeadAttack</c>: the cacodemon's bite in melee range
    /// (10-60 damage), else its ball (<see cref="mobjtype_t.MT_HEADSHOT"/>).
    /// </summary>
    public void A_HeadAttack(mobj_t actor)
    {
        if (actor.target == null)
            return;

        A_FaceTarget(actor);
        if (P_CheckMeleeRange(actor))
        {
            int damage = (P_Random() % 6 + 1) * 10;
            P_DamageMobj(actor.target, actor, actor, damage);
            return;
        }

        // launch a missile
        P_SpawnMissile(actor, actor.target, mobjtype_t.MT_HEADSHOT);
    }

    /// <summary>p_enemy.c <c>A_CyberAttack</c>: the Cyberdemon's rocket (<see cref="mobjtype_t.MT_ROCKET"/>).</summary>
    public void A_CyberAttack(mobj_t actor)
    {
        if (actor.target == null)
            return;

        A_FaceTarget(actor);
        P_SpawnMissile(actor, actor.target, mobjtype_t.MT_ROCKET);
    }

    /// <summary>
    /// p_enemy.c <c>A_SkelMissile</c>: the revenant's homing missile
    /// (<see cref="mobjtype_t.MT_TRACER"/>), spawned 16 units higher than
    /// other missiles and a step ahead, its <see cref="mobj_t.tracer"/> the target.
    /// </summary>
    public void A_SkelMissile(mobj_t actor)
    {
        if (actor.target == null)
            return;

        A_FaceTarget(actor);
        actor.z += 16 * Fixed.FRACUNIT; // so missile spawns higher
        mobj_t mo = P_SpawnMissile(actor, actor.target, mobjtype_t.MT_TRACER);
        actor.z -= 16 * Fixed.FRACUNIT; // back to normal

        mo.x += mo.momx;
        mo.y += mo.momy;
        mo.tracer = actor.target;
    }

    /// <summary>p_enemy.c <c>TRACEANGLE</c>: the most a revenant missile turns per <see cref="A_Tracer"/> (BAM, about 16.9°).</summary>
    private const uint TRACEANGLE = 0xc000000;

    /// <summary>
    /// p_enemy.c <c>A_Tracer</c>: the revenant missile's homing, on every
    /// fourth <see cref="gametic"/> only: a puff and a smoke behind it, then
    /// (while its <see cref="mobj_t.tracer"/> lives) a turn of up to
    /// <see cref="TRACEANGLE"/> towards it and a climb or dive of 1/8 unit a
    /// tic towards 40 units above its feet.
    /// </summary>
    public void A_Tracer(mobj_t actor)
    {
        if ((gametic & 3) != 0)
            return;

        // spawn a puff of smoke behind the rocket
        P_SpawnPuff(actor.x, actor.y, actor.z);

        mobj_t th = P_SpawnMobj(actor.x - actor.momx,
                                actor.y - actor.momy,
                                actor.z, mobjtype_t.MT_SMOKE);

        th.momz = Fixed.FRACUNIT;
        th.tics -= P_Random() & 3;
        if (th.tics < 1)
            th.tics = 1;

        // adjust direction
        mobj_t? dest = actor.tracer;

        if (dest == null || dest.health <= 0)
            return;

        // change angle
        uint exact = Tables.R_PointToAngle2(actor.x,
                                            actor.y,
                                            dest.x,
                                            dest.y);

        if (exact != actor.angle)
        {
            if (unchecked(exact - actor.angle) > 0x80000000)
            {
                actor.angle = unchecked(actor.angle - TRACEANGLE);
                if (unchecked(exact - actor.angle) < 0x80000000)
                    actor.angle = exact;
            }
            else
            {
                actor.angle = unchecked(actor.angle + TRACEANGLE);
                if (unchecked(exact - actor.angle) > 0x80000000)
                    actor.angle = exact;
            }
        }

        exact = actor.angle >> Tables.ANGLETOFINESHIFT;
        actor.momx = Fixed.FixedMul(actor.info.speed, Tables.finecosine[(int)exact]);
        actor.momy = Fixed.FixedMul(actor.info.speed, Tables.finesine[(int)exact]);

        // change slope
        int dist = P_AproxDistance(dest.x - actor.x,
                                   dest.y - actor.y);

        dist /= actor.info.speed;

        if (dist < 1)
            dist = 1;
        int slope = (dest.z + 40 * Fixed.FRACUNIT - actor.z) / dist;

        if (slope < actor.momz)
            actor.momz -= Fixed.FRACUNIT / 8;
        else
            actor.momz += Fixed.FRACUNIT / 8;
    }

    /// <summary>p_enemy.c <c>A_SkelWhoosh</c>: the revenant's punch swing: faces the target, with its sound.</summary>
    public void A_SkelWhoosh(mobj_t actor)
    {
        if (actor.target == null)
            return;
        A_FaceTarget(actor);
        S_StartSound(actor, sfxenum_t.sfx_skeswg);
    }

    /// <summary>p_enemy.c <c>A_SkelFist</c>: the revenant's punch in melee range (6-60 damage).</summary>
    public void A_SkelFist(mobj_t actor)
    {
        if (actor.target == null)
            return;

        A_FaceTarget(actor);

        if (P_CheckMeleeRange(actor))
        {
            int damage = ((P_Random() % 10) + 1) * 6;
            S_StartSound(actor, sfxenum_t.sfx_skepch);
            P_DamageMobj(actor.target, actor, actor, damage);
        }
    }

    /// <summary>p_enemy.c <c>corpsehit</c>: the corpse <see cref="PIT_VileCheck"/> found.</summary>
    private mobj_t? corpsehit;

    /// <summary>p_enemy.c <c>vileobj</c>: the arch-vile looking (set, never read, as vanilla's).</summary>
    public mobj_t? vileobj;

    /// <summary>p_enemy.c <c>viletryx</c>: where the arch-vile's next step takes it (fixed_t).</summary>
    private int viletryx;

    /// <summary>p_enemy.c <c>viletryy</c>.</summary>
    private int viletryy;

    private System.Func<mobj_t, bool>? _pitVileCheck;

    /// <summary>
    /// p_enemy.c <c>PIT_VileCheck</c>: Detect a corpse that could be raised:
    /// false (stop) at a still corpse with a raise state whose box touches
    /// the arch-vile's box at (<see cref="viletryx"/>, <see cref="viletryy"/>)
    /// and that fits where it lies at four times its (corpse) height
    /// (<see cref="P_CheckPosition"/>; its momentum is zeroed either way).
    /// </summary>
    public bool PIT_VileCheck(mobj_t thing)
    {
        if ((thing.flags & mobjflag_t.MF_CORPSE) == 0)
            return true; // not a monster

        if (thing.tics != -1)
            return true; // not lying still yet

        if (thing.info.raisestate == statenum_t.S_NULL)
            return true; // monster doesn't have a raise state

        int maxdist = thing.info.radius + Info.mobjinfo[(int)mobjtype_t.MT_VILE].radius;

        if (abs(thing.x - viletryx) > maxdist
            || abs(thing.y - viletryy) > maxdist)
        {
            return true; // not actually touching
        }

        corpsehit = thing;
        corpsehit.momx = corpsehit.momy = 0;
        corpsehit.height <<= 2;
        bool check = P_CheckPosition(corpsehit, corpsehit.x, corpsehit.y);
        corpsehit.height >>= 2;

        if (!check)
            return true; // doesn't fit here

        return false; // got one, so stop checking
    }

    /// <summary>
    /// p_enemy.c <c>A_VileChase</c>: Check for ressurecting a body: with a
    /// direction, the blocks around its next step are searched
    /// (<see cref="PIT_VileCheck"/>); a corpse found is raised (the
    /// arch-vile faces it and heals, the corpse goes to its raise state with
    /// its height back, its spawn flags and health and no target). Else
    /// <see cref="A_Chase"/>.
    /// </summary>
    public void A_VileChase(mobj_t actor)
    {
        if (actor.movedir != (int)dirtype_t.DI_NODIR)
        {
            // check for corpses to raise
            viletryx = unchecked(actor.x + actor.info.speed * xspeed[actor.movedir]);
            viletryy = unchecked(actor.y + actor.info.speed * yspeed[actor.movedir]);

            int xl = (viletryx - bmaporgx - MAXRADIUS * 2) >> Blockmap.MAPBLOCKSHIFT;
            int xh = (viletryx - bmaporgx + MAXRADIUS * 2) >> Blockmap.MAPBLOCKSHIFT;
            int yl = (viletryy - bmaporgy - MAXRADIUS * 2) >> Blockmap.MAPBLOCKSHIFT;
            int yh = (viletryy - bmaporgy + MAXRADIUS * 2) >> Blockmap.MAPBLOCKSHIFT;

            vileobj = actor;
            _pitVileCheck ??= PIT_VileCheck;
            for (int bx = xl; bx <= xh; bx++)
            {
                for (int by = yl; by <= yh; by++)
                {
                    // Call PIT_VileCheck to check
                    // whether object is a corpse
                    // that canbe raised.
                    if (!P_BlockThingsIterator(bx, by, _pitVileCheck))
                    {
                        // got one!
                        mobj_t corpse = corpsehit!;
                        mobj_t? temp = actor.target;
                        actor.target = corpse;
                        A_FaceTarget(actor);
                        actor.target = temp;

                        P_SetMobjState(actor, statenum_t.S_VILE_HEAL1);
                        S_StartSound(corpse, sfxenum_t.sfx_slop);
                        mobjinfo_t info = corpse.info;

                        P_SetMobjState(corpse, info.raisestate);
                        corpse.height <<= 2;
                        corpse.flags = info.flags;
                        corpse.health = info.spawnhealth;
                        corpse.target = null;

                        return;
                    }
                }
            }
        }

        // Return to normal attack.
        A_Chase(actor);
    }

    /// <summary>p_enemy.c <c>A_VileStart</c>: the arch-vile's attack sound.</summary>
    public void A_VileStart(mobj_t actor)
    {
        S_StartSound(actor, sfxenum_t.sfx_vilatk);
    }

    /// <summary>p_enemy.c <c>A_StartFire</c>: the arch-vile's fire starting: its sound, then <see cref="A_Fire"/>.</summary>
    public void A_StartFire(mobj_t actor)
    {
        S_StartSound(actor, sfxenum_t.sfx_flamst);
        A_Fire(actor);
    }

    /// <summary>p_enemy.c <c>A_FireCrackle</c>: the fire's crackle, then <see cref="A_Fire"/>.</summary>
    public void A_FireCrackle(mobj_t actor)
    {
        S_StartSound(actor, sfxenum_t.sfx_flame);
        A_Fire(actor);
    }

    /// <summary>
    /// p_enemy.c <c>A_Fire</c>: Keep fire in front of player unless out of
    /// sight: the fire moves 24 units in front of its <see cref="mobj_t.tracer"/>
    /// (along its angle, at its feet) while its arch-vile
    /// (<see cref="mobj_t.target"/>, <see cref="P_SubstNullMobj"/>) sees it.
    /// </summary>
    public void A_Fire(mobj_t actor)
    {
        mobj_t? dest = actor.tracer;
        if (dest == null)
            return;

        mobj_t target = P_SubstNullMobj(actor.target);

        // don't move it if the vile lost sight
        if (!P_CheckSight(target, dest))
            return;

        uint an = dest.angle >> Tables.ANGLETOFINESHIFT;

        P_UnsetThingPosition(actor);
        actor.x = dest.x + Fixed.FixedMul(24 * Fixed.FRACUNIT, Tables.finecosine[(int)an]);
        actor.y = dest.y + Fixed.FixedMul(24 * Fixed.FRACUNIT, Tables.finesine[(int)an]);
        actor.z = dest.z;
        P_SetThingPosition(actor);
    }

    /// <summary>
    /// p_enemy.c <c>A_VileTarget</c>: Spawn the hellfire: the arch-vile faces
    /// its target and spawns its <see cref="mobjtype_t.MT_FIRE"/> (its
    /// <see cref="mobj_t.tracer"/>; the fire's target the arch-vile, its
    /// tracer the target) at (target x, target x): vanilla's typo, kept (the
    /// fire's <see cref="A_Fire"/> moves it to the target at once while the
    /// arch-vile sees it).
    /// </summary>
    public void A_VileTarget(mobj_t actor)
    {
        if (actor.target == null)
            return;

        A_FaceTarget(actor);

        mobj_t fog = P_SpawnMobj(actor.target.x,
                                 actor.target.x,
                                 actor.target.z, mobjtype_t.MT_FIRE);

        actor.tracer = fog;
        fog.target = actor;
        fog.tracer = actor.target;
        A_Fire(fog);
    }

    /// <summary>
    /// p_enemy.c <c>A_VileAttack</c>: the arch-vile's blast on a target in
    /// sight: 20 damage, the target thrown up (1000 units / its mass), and
    /// its fire, moved between the arch-vile and the target, explodes for 70
    /// (<see cref="P_RadiusAttack"/>, the arch-vile's).
    /// </summary>
    public void A_VileAttack(mobj_t actor)
    {
        if (actor.target == null)
            return;

        A_FaceTarget(actor);

        if (!P_CheckSight(actor, actor.target))
            return;

        S_StartSound(actor, sfxenum_t.sfx_barexp);
        P_DamageMobj(actor.target, actor, actor, 20);
        actor.target.momz = 1000 * Fixed.FRACUNIT / actor.target.info.mass;

        uint an = actor.angle >> Tables.ANGLETOFINESHIFT;

        mobj_t? fire = actor.tracer;

        if (fire == null)
            return;

        // move the fire between the vile and the player
        fire.x = actor.target.x - Fixed.FixedMul(24 * Fixed.FRACUNIT, Tables.finecosine[(int)an]);
        fire.y = actor.target.y - Fixed.FixedMul(24 * Fixed.FRACUNIT, Tables.finesine[(int)an]);
        P_RadiusAttack(fire, actor, 70);
    }

    /// <summary>p_enemy.c <c>FATSPREAD</c>: the mancubus' spread (ANG90/8, 11.25°).</summary>
    private const uint FATSPREAD = Tables.ANG90 / 8;

    /// <summary>p_enemy.c <c>A_FatRaise</c>: the mancubus faces its target, with its attack sound.</summary>
    public void A_FatRaise(mobj_t actor)
    {
        A_FaceTarget(actor);
        S_StartSound(actor, sfxenum_t.sfx_manatk);
    }

    /// <summary>
    /// p_enemy.c <c>A_FatAttack1</c>: the mancubus turns <see cref="FATSPREAD"/>
    /// left and fires two <see cref="mobjtype_t.MT_FATSHOT"/>s at its target
    /// (<see cref="P_SubstNullMobj"/>), the second turned another FATSPREAD left.
    /// </summary>
    public void A_FatAttack1(mobj_t actor)
    {
        A_FaceTarget(actor);

        // Change direction  to ...
        actor.angle = unchecked(actor.angle + FATSPREAD);
        mobj_t target = P_SubstNullMobj(actor.target);
        P_SpawnMissile(actor, target, mobjtype_t.MT_FATSHOT);

        mobj_t mo = P_SpawnMissile(actor, target, mobjtype_t.MT_FATSHOT);
        mo.angle = unchecked(mo.angle + FATSPREAD);
        uint an = mo.angle >> Tables.ANGLETOFINESHIFT;
        mo.momx = Fixed.FixedMul(mo.info.speed, Tables.finecosine[(int)an]);
        mo.momy = Fixed.FixedMul(mo.info.speed, Tables.finesine[(int)an]);
    }

    /// <summary>
    /// p_enemy.c <c>A_FatAttack2</c>: Now here choose opposite deviation: the
    /// mancubus turns <see cref="FATSPREAD"/> right and fires two, the second
    /// turned two FATSPREADs right.
    /// </summary>
    public void A_FatAttack2(mobj_t actor)
    {
        A_FaceTarget(actor);
        // Now here choose opposite deviation.
        actor.angle = unchecked(actor.angle - FATSPREAD);
        mobj_t target = P_SubstNullMobj(actor.target);
        P_SpawnMissile(actor, target, mobjtype_t.MT_FATSHOT);

        mobj_t mo = P_SpawnMissile(actor, target, mobjtype_t.MT_FATSHOT);
        mo.angle = unchecked(mo.angle - FATSPREAD * 2);
        uint an = mo.angle >> Tables.ANGLETOFINESHIFT;
        mo.momx = Fixed.FixedMul(mo.info.speed, Tables.finecosine[(int)an]);
        mo.momy = Fixed.FixedMul(mo.info.speed, Tables.finesine[(int)an]);
    }

    /// <summary>p_enemy.c <c>A_FatAttack3</c>: two shots, half a <see cref="FATSPREAD"/> either side of the target.</summary>
    public void A_FatAttack3(mobj_t actor)
    {
        A_FaceTarget(actor);

        mobj_t target = P_SubstNullMobj(actor.target);

        mobj_t mo = P_SpawnMissile(actor, target, mobjtype_t.MT_FATSHOT);
        mo.angle = unchecked(mo.angle - FATSPREAD / 2);
        uint an = mo.angle >> Tables.ANGLETOFINESHIFT;
        mo.momx = Fixed.FixedMul(mo.info.speed, Tables.finecosine[(int)an]);
        mo.momy = Fixed.FixedMul(mo.info.speed, Tables.finesine[(int)an]);

        mo = P_SpawnMissile(actor, target, mobjtype_t.MT_FATSHOT);
        mo.angle = unchecked(mo.angle + FATSPREAD / 2);
        an = mo.angle >> Tables.ANGLETOFINESHIFT;
        mo.momx = Fixed.FixedMul(mo.info.speed, Tables.finecosine[(int)an]);
        mo.momy = Fixed.FixedMul(mo.info.speed, Tables.finesine[(int)an]);
    }

    /// <summary>p_enemy.c <c>SKULLSPEED</c>: the lost soul's charge speed (fixed_t).</summary>
    private const int SKULLSPEED = 20 * Fixed.FRACUNIT;

    /// <summary>
    /// p_enemy.c <c>A_SkullAttack</c>: Fly at the player like a missile: the
    /// lost soul (<see cref="mobjflag_t.MF_SKULLFLY"/>; P_XYMovement and
    /// PIT_CheckThing end the charge) flies at its target at
    /// <see cref="SKULLSPEED"/>, climbing or dropping to its middle.
    /// </summary>
    public void A_SkullAttack(mobj_t actor)
    {
        if (actor.target == null)
            return;

        mobj_t dest = actor.target;
        actor.flags |= mobjflag_t.MF_SKULLFLY;

        S_StartSound(actor, actor.info.attacksound);
        A_FaceTarget(actor);
        uint an = actor.angle >> Tables.ANGLETOFINESHIFT;
        actor.momx = Fixed.FixedMul(SKULLSPEED, Tables.finecosine[(int)an]);
        actor.momy = Fixed.FixedMul(SKULLSPEED, Tables.finesine[(int)an]);
        int dist = P_AproxDistance(dest.x - actor.x, dest.y - actor.y);
        dist /= SKULLSPEED;

        if (dist < 1)
            dist = 1;
        actor.momz = (dest.z + (dest.height >> 1) - actor.z) / dist;
    }

    /// <summary>
    /// p_enemy.c <c>A_PainShootSkull</c>: Spawn a lost soul and launch it at
    /// the target: unless more than 20 lost souls are on the level, one
    /// spawns in front of the pain elemental along <paramref name="angle"/>
    /// (8 units up); one that cannot move there (<see cref="P_TryMove"/>) is
    /// killed at once, else it charges (<see cref="A_SkullAttack"/>) at the
    /// pain elemental's target. No wall check between them: vanilla's
    /// lost souls through walls, kept.
    /// </summary>
    public void A_PainShootSkull(mobj_t actor, uint angle)
    {
        // count total number of skull currently on the level
        int count = 0;
        foreach (mobj_t mo in Mobjs())
        {
            if (mo.type == mobjtype_t.MT_SKULL)
                count++;
        }

        // if there are allready 20 skulls on the level,
        // don't spit another one
        if (count > 20)
            return;

        // okay, there's playe for another one
        uint an = angle >> Tables.ANGLETOFINESHIFT;

        int prestep =
            4 * Fixed.FRACUNIT
            + 3 * (actor.info.radius + Info.mobjinfo[(int)mobjtype_t.MT_SKULL].radius) / 2;

        int x = actor.x + Fixed.FixedMul(prestep, Tables.finecosine[(int)an]);
        int y = actor.y + Fixed.FixedMul(prestep, Tables.finesine[(int)an]);
        int z = actor.z + 8 * Fixed.FRACUNIT;

        mobj_t newmobj = P_SpawnMobj(x, y, z, mobjtype_t.MT_SKULL);

        // Check for movements.
        if (!P_TryMove(newmobj, newmobj.x, newmobj.y))
        {
            // kill it immediately
            P_DamageMobj(newmobj, actor, actor, 10000);
            return;
        }

        newmobj.target = actor.target;
        A_SkullAttack(newmobj);
    }

    /// <summary>p_enemy.c <c>A_PainAttack</c>: Spawn a lost soul and launch it at the target (<see cref="A_PainShootSkull"/>, facing it).</summary>
    public void A_PainAttack(mobj_t actor)
    {
        if (actor.target == null)
            return;

        A_FaceTarget(actor);
        A_PainShootSkull(actor, actor.angle);
    }

    /// <summary>p_enemy.c <c>A_PainDie</c>: the pain elemental falls and spits three lost souls, to its left, back and right.</summary>
    public void A_PainDie(mobj_t actor)
    {
        A_Fall(actor);
        A_PainShootSkull(actor, unchecked(actor.angle + Tables.ANG90));
        A_PainShootSkull(actor, unchecked(actor.angle + Tables.ANG180));
        A_PainShootSkull(actor, unchecked(actor.angle + Tables.ANG270));
    }

    /// <summary>p_enemy.c <c>A_Hoof</c>: the Cyberdemon's hoof sound, then <see cref="A_Chase"/>.</summary>
    public void A_Hoof(mobj_t mo)
    {
        S_StartSound(mo, sfxenum_t.sfx_hoof);
        A_Chase(mo);
    }

    /// <summary>p_enemy.c <c>A_Metal</c>: the Cyberdemon's and Spider Mastermind's metal step, then <see cref="A_Chase"/>.</summary>
    public void A_Metal(mobj_t mo)
    {
        S_StartSound(mo, sfxenum_t.sfx_metal);
        A_Chase(mo);
    }

    /// <summary>p_enemy.c <c>A_BabyMetal</c>: the arachnotron's step, then <see cref="A_Chase"/>.</summary>
    public void A_BabyMetal(mobj_t mo)
    {
        S_StartSound(mo, sfxenum_t.sfx_bspwlk);
        A_Chase(mo);
    }

    /// <summary>
    /// p_mobj.c <c>P_SubstNullMobj</c> (Chocolate Doom): a missing mobj
    /// becomes a dummy at (0, 0, 0) without flags, where vanilla read
    /// through a null pointer (the mancubus' and the fire's target).
    /// </summary>
    public static mobj_t P_SubstNullMobj(mobj_t? mobj) =>
        mobj ?? new mobj_t { x = 0, y = 0, z = 0, flags = 0 };
}
