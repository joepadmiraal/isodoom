using IsoDoom.Map;

namespace IsoDoom.Sim;

// p_map.c, attack part (T6.3): aiming (P_AimLineAttack, PTR_AimTraverse),
// hitscans (P_LineAttack, PTR_ShootTraverse) and radius attacks
// (P_RadiusAttack, PIT_RadiusAttack); p_mobj.c's bullet puffs and blood
// (P_SpawnPuff, P_SpawnBlood). Damage is p_inter.c's P_DamageMobj
// (World.Inter.cs), gun-triggered lines p_spec.c's P_ShootSpecialLine
// (World.Spec.cs).
public sealed partial class World
{
    /// <summary>p_local.h <c>MISSILERANGE</c>: how far hitscans reach (fixed_t).</summary>
    public const int MISSILERANGE = 32 * 64 * Fixed.FRACUNIT;

    // ---- p_map.c globals ----

    /// <summary>p_map.c <c>linetarget</c>: who got hit (or null) by the last <see cref="P_AimLineAttack"/>.</summary>
    public mobj_t? linetarget;

    /// <summary>p_map.c <c>shootthing</c>: the thing aiming or shooting.</summary>
    public mobj_t? shootthing;

    /// <summary>p_map.c <c>shootz</c>: the height the shot starts at, 8 units above the shooter's middle (fixed_t).</summary>
    public int shootz;

    /// <summary>p_map.c <c>la_damage</c>: the damage of the current <see cref="P_LineAttack"/>.</summary>
    public int la_damage;

    /// <summary>p_map.c <c>attackrange</c>: the length of the current aim or shot (fixed_t).</summary>
    public int attackrange;

    /// <summary>p_map.c <c>aimslope</c>: the slope found by the aim, or the shot's slope (fixed_t).</summary>
    public int aimslope;

    /// <summary>p_map.c <c>bombsource</c>: the thing a radius attack's damage counts for.</summary>
    public mobj_t? bombsource;

    /// <summary>p_map.c <c>bombspot</c>: the centre of the radius attack.</summary>
    public mobj_t? bombspot;

    /// <summary>p_map.c <c>bombdamage</c>: the radius attack's damage at its centre, and its reach in units.</summary>
    public int bombdamage;

    private traverser_t? _ptrAimTraverse;
    private traverser_t? _ptrShootTraverse;
    private System.Func<mobj_t, bool>? _pitRadiusAttack;

    /// <summary>
    /// p_map.c <c>PTR_AimTraverse</c>: a closed or one-sided line stops the
    /// aim; a two-sided one narrows <see cref="topslope"/>/<see cref="bottomslope"/>
    /// (the p_sight.c globals) by its opening; the first shootable thing (not
    /// the shooter) within the slopes is <see cref="linetarget"/>, aimed at
    /// the middle of its part within them (<see cref="aimslope"/>).
    /// </summary>
    public bool PTR_AimTraverse(intercept_t @in)
    {
        int slope;
        int dist;

        if (@in.isaline)
        {
            line_t li = @in.line!;

            if ((li.flags & Line.ML_TWOSIDED) == 0)
                return false; // stop

            // Not vanilla (broken maps only): a two-sided line without a back
            // side stops the aim (vanilla reads sides[-1]; SPEC §12 T6.3).
            if (li.backsector == null)
                return false;

            // Crosses a two sided line.
            // A two sided line will restrict
            // the possible target ranges.
            P_LineOpening(li);

            if (openbottom >= opentop)
                return false; // stop

            dist = Fixed.FixedMul(attackrange, @in.frac);

            if (li.frontsector!.floorheight != li.backsector.floorheight)
            {
                slope = Fixed.FixedDiv(openbottom - shootz, dist);
                if (slope > bottomslope)
                    bottomslope = slope;
            }

            if (li.frontsector.ceilingheight != li.backsector.ceilingheight)
            {
                slope = Fixed.FixedDiv(opentop - shootz, dist);
                if (slope < topslope)
                    topslope = slope;
            }

            if (topslope <= bottomslope)
                return false; // stop

            return true; // shot continues
        }

        // shoot a thing
        mobj_t th = @in.thing!;
        if (th == shootthing)
            return true; // can't shoot self

        if ((th.flags & mobjflag_t.MF_SHOOTABLE) == 0)
            return true; // corpse or something

        // check angles to see if the thing can be aimed at
        dist = Fixed.FixedMul(attackrange, @in.frac);
        int thingtopslope = Fixed.FixedDiv(th.z + th.height - shootz, dist);

        if (thingtopslope < bottomslope)
            return true; // shot over the thing

        int thingbottomslope = Fixed.FixedDiv(th.z - shootz, dist);

        if (thingbottomslope > topslope)
            return true; // shot under the thing

        // this thing can be hit!
        if (thingtopslope > topslope)
            thingtopslope = topslope;

        if (thingbottomslope < bottomslope)
            thingbottomslope = bottomslope;

        aimslope = (thingtopslope + thingbottomslope) / 2;
        linetarget = th;

        return false; // don't go any farther
    }

    /// <summary>
    /// p_map.c <c>PTR_ShootTraverse</c>: every special line the shot crosses
    /// gets <see cref="P_ShootSpecialLine"/>; a one-sided line, or an opening
    /// the shot's slope passes above or below, stops it with a puff 4 units
    /// before it (none against a sky ceiling: above it, or on a sky hack
    /// wall). The first shootable thing (not the shooter) the slope hits gets
    /// a puff (<c>MF_NOBLOOD</c>) or blood 10 units before it and
    /// <see cref="la_damage"/> (<see cref="P_DamageMobj"/>).
    /// </summary>
    public bool PTR_ShootTraverse(intercept_t @in)
    {
        int x, y, z, frac;
        int slope;
        int dist;

        if (@in.isaline)
        {
            line_t li = @in.line!;

            if (li.special != 0)
                P_ShootSpecialLine(shootthing!, li);

            bool hitline = false;
            if ((li.flags & Line.ML_TWOSIDED) == 0)
            {
                hitline = true;
            }
            else if (li.backsector == null)
            {
                // Not vanilla (broken maps only): a two-sided line without a
                // back side stops the shot (vanilla reads sides[-1]; SPEC §12 T6.3).
                hitline = true;
            }
            else
            {
                // crosses a two sided line
                P_LineOpening(li);

                dist = Fixed.FixedMul(attackrange, @in.frac);

                if (li.frontsector!.floorheight != li.backsector.floorheight)
                {
                    slope = Fixed.FixedDiv(openbottom - shootz, dist);
                    if (slope > aimslope)
                        hitline = true;
                }

                if (!hitline && li.frontsector.ceilingheight != li.backsector.ceilingheight)
                {
                    slope = Fixed.FixedDiv(opentop - shootz, dist);
                    if (slope < aimslope)
                        hitline = true;
                }

                if (!hitline)
                {
                    // shot continues
                    return true;
                }
            }

            // hit line
            // position a bit closer
            frac = @in.frac - Fixed.FixedDiv(4 * Fixed.FRACUNIT, attackrange);
            x = trace.x + Fixed.FixedMul(trace.dx, frac);
            y = trace.y + Fixed.FixedMul(trace.dy, frac);
            z = shootz + Fixed.FixedMul(aimslope, Fixed.FixedMul(frac, attackrange));

            if (li.frontsector!.ceilingpic == SKYFLATNAME)
            {
                // don't shoot the sky!
                if (z > li.frontsector.ceilingheight)
                    return false;

                // it's a sky hack wall
                if (li.backsector != null && li.backsector.ceilingpic == SKYFLATNAME)
                    return false;
            }

            // Spawn bullet puffs.
            P_SpawnPuff(x, y, z);

            // don't go any farther
            return false;
        }

        // shoot a thing
        mobj_t th = @in.thing!;
        if (th == shootthing)
            return true; // can't shoot self

        if ((th.flags & mobjflag_t.MF_SHOOTABLE) == 0)
            return true; // corpse or something

        // check angles to see if the thing can be aimed at
        dist = Fixed.FixedMul(attackrange, @in.frac);
        int thingtopslope = Fixed.FixedDiv(th.z + th.height - shootz, dist);

        if (thingtopslope < aimslope)
            return true; // shot over the thing

        int thingbottomslope = Fixed.FixedDiv(th.z - shootz, dist);

        if (thingbottomslope > aimslope)
            return true; // shot under the thing

        // hit thing
        // position a bit closer
        frac = @in.frac - Fixed.FixedDiv(10 * Fixed.FRACUNIT, attackrange);

        x = trace.x + Fixed.FixedMul(trace.dx, frac);
        y = trace.y + Fixed.FixedMul(trace.dy, frac);
        z = shootz + Fixed.FixedMul(aimslope, Fixed.FixedMul(frac, attackrange));

        // Spawn bullet puffs or blod spots,
        // depending on target type.
        if ((th.flags & mobjflag_t.MF_NOBLOOD) != 0)
            P_SpawnPuff(x, y, z);
        else
            P_SpawnBlood(x, y, z, la_damage);

        if (la_damage != 0)
            P_DamageMobj(th, shootthing, shootthing, la_damage);

        // don't go any farther
        return false;
    }

    /// <summary>
    /// p_map.c <c>P_AimLineAttack</c>: traces <paramref name="distance"/>
    /// (fixed_t) from <paramref name="t1"/> along <paramref name="angle"/>
    /// within vanilla's vertical view (slopes ±100/160) and returns the slope
    /// to the first thing it can hit (<see cref="linetarget"/>), or 0 when
    /// there is none.
    /// </summary>
    public int P_AimLineAttack(mobj_t t1, uint angle, int distance)
    {
        _ptrAimTraverse ??= PTR_AimTraverse;

        int a = (int)(angle >> Tables.ANGLETOFINESHIFT);
        shootthing = t1;

        int x2 = t1.x + (distance >> Fixed.FRACBITS) * Tables.finecosine[a];
        int y2 = t1.y + (distance >> Fixed.FRACBITS) * Tables.finesine[a];
        shootz = t1.z + (t1.height >> 1) + 8 * Fixed.FRACUNIT;

        // can't shoot outside view angles
        topslope = 100 * Fixed.FRACUNIT / 160;
        bottomslope = -100 * Fixed.FRACUNIT / 160;

        attackrange = distance;
        linetarget = null;

        P_PathTraverse(t1.x, t1.y, x2, y2, PT_ADDLINES | PT_ADDTHINGS, _ptrAimTraverse);

        if (linetarget != null)
            return aimslope;

        return 0;
    }

    /// <summary>
    /// p_map.c <c>P_LineAttack</c>: fires a hitscan of <paramref name="distance"/>
    /// (fixed_t) from <paramref name="t1"/> along <paramref name="angle"/> at
    /// <paramref name="slope"/> (from <see cref="P_AimLineAttack"/>), dealing
    /// <paramref name="damage"/> to the first thing it hits
    /// (<see cref="PTR_ShootTraverse"/>; with damage 0 it still spawns the
    /// puff or blood and triggers gun lines).
    /// </summary>
    public void P_LineAttack(mobj_t t1, uint angle, int distance, int slope, int damage)
    {
        _ptrShootTraverse ??= PTR_ShootTraverse;

        int a = (int)(angle >> Tables.ANGLETOFINESHIFT);
        shootthing = t1;
        la_damage = damage;
        int x2 = t1.x + (distance >> Fixed.FRACBITS) * Tables.finecosine[a];
        int y2 = t1.y + (distance >> Fixed.FRACBITS) * Tables.finesine[a];
        shootz = t1.z + (t1.height >> 1) + 8 * Fixed.FRACUNIT;
        attackrange = distance;
        aimslope = slope;

        P_PathTraverse(t1.x, t1.y, x2, y2, PT_ADDLINES | PT_ADDTHINGS, _ptrShootTraverse);
    }

    /// <summary>
    /// p_map.c <c>PIT_RadiusAttack</c>: a shootable thing (but the Spider
    /// Mastermind and the Cyberdemon) within <see cref="bombdamage"/> units
    /// of <see cref="bombspot"/> (the larger axis distance less its radius)
    /// and in its sight takes <see cref="bombdamage"/> less that distance.
    /// </summary>
    public bool PIT_RadiusAttack(mobj_t thing)
    {
        if ((thing.flags & mobjflag_t.MF_SHOOTABLE) == 0)
            return true;

        // Boss spider and cyborg
        // take no damage from concussion.
        if (thing.type == mobjtype_t.MT_CYBORG || thing.type == mobjtype_t.MT_SPIDER)
            return true;

        mobj_t spot = bombspot!;
        int dx = abs(thing.x - spot.x);
        int dy = abs(thing.y - spot.y);

        int dist = dx > dy ? dx : dy;
        dist = (dist - thing.radius) >> Fixed.FRACBITS;

        if (dist < 0)
            dist = 0;

        if (dist >= bombdamage)
            return true; // out of range

        if (P_CheckSight(thing, spot))
        {
            // must be in direct path
            P_DamageMobj(thing, spot, bombsource, bombdamage - dist);
        }

        return true;
    }

    /// <summary>
    /// p_map.c <c>P_RadiusAttack</c>: <paramref name="source"/> damages
    /// everything near <paramref name="spot"/> (<see cref="PIT_RadiusAttack"/>,
    /// in the blocks within <paramref name="damage"/> units: vanilla means
    /// <paramref name="damage"/> + <see cref="MAXRADIUS"/>, but the sum's
    /// shift overflows; rows from south to north).
    /// </summary>
    public void P_RadiusAttack(mobj_t spot, mobj_t? source, int damage)
    {
        _pitRadiusAttack ??= PIT_RadiusAttack;

        // (vanilla's overflow kept: MAXRADIUS << FRACBITS wraps to 0, so the
        // blocks searched reach only damage units; PIT_RadiusAttack's reach)
        int dist = unchecked((damage + MAXRADIUS) << Fixed.FRACBITS);
        int yh = (spot.y + dist - bmaporgy) >> Blockmap.MAPBLOCKSHIFT;
        int yl = (spot.y - dist - bmaporgy) >> Blockmap.MAPBLOCKSHIFT;
        int xh = (spot.x + dist - bmaporgx) >> Blockmap.MAPBLOCKSHIFT;
        int xl = (spot.x - dist - bmaporgx) >> Blockmap.MAPBLOCKSHIFT;
        bombspot = spot;
        bombsource = source;
        bombdamage = damage;

        for (int y = yl; y <= yh; y++)
        {
            for (int x = xl; x <= xh; x++)
                P_BlockThingsIterator(x, y, _pitRadiusAttack);
        }
    }

    // ---- p_mobj.c ----

    /// <summary>
    /// p_mobj.c <c>P_SpawnPuff</c>: a bullet puff at (<paramref name="x"/>,
    /// <paramref name="y"/>, <paramref name="z"/>) give or take 4 units of
    /// height, rising 1 unit a tic, its first frame 0–3 tics shorter; a punch
    /// (<see cref="attackrange"/> == <see cref="MELEERANGE"/>) shows no spark
    /// (<see cref="statenum_t.S_PUFF3"/>). Three <c>P_Random</c>s.
    /// </summary>
    public mobj_t P_SpawnPuff(int x, int y, int z)
    {
        // (left operand first, as PIT_ChangeSector's blood)
        z += (P_Random() - P_Random()) << 10;

        mobj_t th = P_SpawnMobj(x, y, z, mobjtype_t.MT_PUFF);
        th.momz = Fixed.FRACUNIT;
        th.tics -= P_Random() & 3;

        if (th.tics < 1)
            th.tics = 1;

        // don't make punches spark on the wall
        if (attackrange == MELEERANGE)
            P_SetMobjState(th, statenum_t.S_PUFF3);
        return th;
    }

    /// <summary>
    /// p_mobj.c <c>P_SpawnBlood</c>: a blood spot at (<paramref name="x"/>,
    /// <paramref name="y"/>, <paramref name="z"/>) give or take 4 units of
    /// height, rising 2 units a tic, its first frame 0–3 tics shorter, and
    /// smaller for less <paramref name="damage"/> (9–12:
    /// <see cref="statenum_t.S_BLOOD2"/>, below 9: <see cref="statenum_t.S_BLOOD3"/>).
    /// Three <c>P_Random</c>s.
    /// </summary>
    public mobj_t P_SpawnBlood(int x, int y, int z, int damage)
    {
        // (left operand first, as PIT_ChangeSector's blood)
        z += (P_Random() - P_Random()) << 10;
        mobj_t th = P_SpawnMobj(x, y, z, mobjtype_t.MT_BLOOD);
        th.momz = Fixed.FRACUNIT * 2;
        th.tics -= P_Random() & 3;

        if (th.tics < 1)
            th.tics = 1;

        if (damage <= 12 && damage >= 9)
            P_SetMobjState(th, statenum_t.S_BLOOD2);
        else if (damage < 9)
            P_SetMobjState(th, statenum_t.S_BLOOD3);
        return th;
    }
}
