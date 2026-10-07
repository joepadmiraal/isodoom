using System;
using IsoDoom.Map;

namespace IsoDoom.Sim;

// p_map.c, movement part: position checks (P_CheckPosition, PIT_CheckLine, PIT_CheckThing),
// P_TryMove and wall sliding (P_SlideMove, PTR_SlideTraverse, P_HitSlideLine), and sector
// height changes (P_ChangeSector, PIT_ChangeSector, P_ThingHeightClip; T5.1), and
// the use action (P_UseLines, PTR_UseTraverse, with the use fallback of SPEC §6.3 #3; T5.2).
// Teleport moves are in World.Telept.cs, aiming, shooting and radius attacks in
// World.Attack.cs (T6.3).
public sealed partial class World
{
    // ---- p_local.h ----

    /// <summary>p_local.h <c>MAXRADIUS</c>: the largest thing radius, how far a thing can reach into the next block.</summary>
    public const int MAXRADIUS = 32 * Fixed.FRACUNIT;

    /// <summary>
    /// p_map.c <c>MAXSPECIALCROSS</c>: the size of vanilla's <see cref="spechit"/>
    /// array. Vanilla writes past it (no check); here the array grows instead
    /// and <see cref="spechitoverruns"/> counts the entries vanilla would have
    /// overrun with (SPEC §12 T4.4).
    /// </summary>
    public const int MAXSPECIALCROSS = 8;

    // ---- p_map.c globals ----

    /// <summary>p_map.c <c>tmbbox</c>: the moving thing's box at the position checked (<see cref="BBox"/> indices).</summary>
    public readonly int[] tmbbox = new int[4];

    /// <summary>p_map.c <c>tmthing</c>: the thing <see cref="P_CheckPosition"/> checks.</summary>
    public mobj_t? tmthing;

    /// <summary>p_map.c <c>tmflags</c>: <see cref="tmthing"/>'s flags.</summary>
    public mobjflag_t tmflags;

    /// <summary>p_map.c <c>tmx</c>: the position checked (fixed_t).</summary>
    public int tmx;

    /// <summary>p_map.c <c>tmy</c>: the position checked (fixed_t).</summary>
    public int tmy;

    /// <summary>
    /// p_map.c <c>floatok</c>: if true, move would be ok if within
    /// <see cref="tmfloorz"/> - <see cref="tmceilingz"/> (set by <see cref="P_TryMove"/>).
    /// </summary>
    public bool floatok;

    /// <summary>p_map.c <c>tmfloorz</c>: the highest floor the box touches (fixed_t).</summary>
    public int tmfloorz;

    /// <summary>p_map.c <c>tmceilingz</c>: the lowest ceiling the box touches (fixed_t).</summary>
    public int tmceilingz;

    /// <summary>p_map.c <c>tmdropoffz</c>: the lowest floor the box touches (fixed_t).</summary>
    public int tmdropoffz;

    /// <summary>
    /// p_map.c <c>ceilingline</c>: keep track of the line that lowers the
    /// ceiling, so missiles don't explode against sky hack walls.
    /// </summary>
    public line_t? ceilingline;

    /// <summary>
    /// p_map.c <c>spechit</c>: special lines the box touched, to check for
    /// crossings once the move is proven ok, up to <see cref="numspechit"/>.
    /// </summary>
    public line_t?[] spechit => _spechit;

    private line_t?[] _spechit = new line_t?[MAXSPECIALCROSS];

    /// <summary>
    /// p_map.c <c>numspechit</c>. <see cref="P_TryMove"/>'s
    /// <c>while (numspechit--)</c> leaves it at -1, as vanilla.
    /// </summary>
    public int numspechit;

    /// <summary>
    /// The special lines collected beyond <see cref="MAXSPECIALCROSS"/> in one
    /// position check, summed over the world's life: each one is a write past
    /// vanilla's array (Chocolate Doom's <c>SpechitOverrun</c> emulates the
    /// first few; this port does not, SPEC §12 T4.4).
    /// </summary>
    public int spechitoverruns;

    /// <summary>p_map.c <c>bestslidefrac</c>: the nearest blocking line's fraction along the slide traces.</summary>
    public int bestslidefrac;

    /// <summary>p_map.c <c>secondslidefrac</c> (set, never read, as vanilla).</summary>
    public int secondslidefrac;

    /// <summary>p_map.c <c>bestslideline</c>.</summary>
    public line_t? bestslideline;

    /// <summary>p_map.c <c>secondslideline</c> (set, never read, as vanilla).</summary>
    public line_t? secondslideline;

    /// <summary>p_map.c <c>slidemo</c>: the thing <see cref="P_SlideMove"/> moves.</summary>
    public mobj_t? slidemo;

    /// <summary>p_map.c <c>tmxmove</c>: the slide move along the wall (fixed_t).</summary>
    public int tmxmove;

    /// <summary>p_map.c <c>tmymove</c>: the slide move along the wall (fixed_t).</summary>
    public int tmymove;

    // Cached delegates for the iterators (no allocation per tic).
    private Func<line_t, bool>? _pitCheckLine;
    private Func<mobj_t, bool>? _pitCheckThing;
    private traverser_t? _ptrSlideTraverse;

    /// <summary>
    /// p_map.c <c>PIT_CheckLine</c>: adjusts <see cref="tmfloorz"/>,
    /// <see cref="tmceilingz"/> and <see cref="tmdropoffz"/> as lines are
    /// contacted. Returns false for a line that blocks the box: one-sided,
    /// or (except for missiles) <c>ML_BLOCKING</c>, or <c>ML_BLOCKMONSTERS</c>
    /// for a non-player. A special line touched goes into <see cref="spechit"/>.
    /// </summary>
    public bool PIT_CheckLine(line_t ld)
    {
        if (tmbbox[BBox.BOXRIGHT] <= ld.bbox[BBox.BOXLEFT]
            || tmbbox[BBox.BOXLEFT] >= ld.bbox[BBox.BOXRIGHT]
            || tmbbox[BBox.BOXTOP] <= ld.bbox[BBox.BOXBOTTOM]
            || tmbbox[BBox.BOXBOTTOM] >= ld.bbox[BBox.BOXTOP])
            return true;

        if (P_BoxOnLineSide(tmbbox, ld) != -1)
            return true;

        // A line has been hit

        // The moving thing's destination position will cross
        // the given line.
        // If this should not be allowed, return false.
        // If the line is special, keep track of it
        // to process later if the move is proven ok.
        // NOTE: specials are NOT sorted by order,
        // so two special lines that are only 8 pixels apart
        // could be crossed in either order.

        if (ld.backsector == null)
            return false; // one sided line

        if ((tmthing!.flags & mobjflag_t.MF_MISSILE) == 0)
        {
            if ((ld.flags & Line.ML_BLOCKING) != 0)
                return false; // explicitly blocking everything

            if (tmthing.player == null && (ld.flags & Line.ML_BLOCKMONSTERS) != 0)
                return false; // block monsters only
        }

        // set openrange, opentop, openbottom
        P_LineOpening(ld);

        // adjust floor / ceiling heights
        if (opentop < tmceilingz)
        {
            tmceilingz = opentop;
            ceilingline = ld;
        }

        if (openbottom > tmfloorz)
            tmfloorz = openbottom;

        if (lowfloor < tmdropoffz)
            tmdropoffz = lowfloor;

        // if contacted a special line, add it to the list
        if (ld.special != 0)
        {
            if (numspechit >= MAXSPECIALCROSS)
                spechitoverruns++;
            if (numspechit == _spechit.Length)
                Array.Resize(ref _spechit, _spechit.Length * 2);
            _spechit[numspechit] = ld;
            numspechit++;
        }

        return true;
    }

    /// <summary>
    /// p_map.c <c>PIT_CheckThing</c>: false when <paramref name="thing"/>
    /// blocks <see cref="tmthing"/> at (<see cref="tmx"/>, <see cref="tmy"/>):
    /// their boxes overlap and it is solid. A charging lost soul slams into
    /// it, a missile hits it (or flies over or under it), a pickup is touched
    /// (<c>MF_PICKUP</c>): <see cref="P_DamageMobj"/> and
    /// <see cref="P_TouchSpecialThing"/> (World.Inter.cs, T5.8).
    /// </summary>
    public bool PIT_CheckThing(mobj_t thing)
    {
        if ((thing.flags & (mobjflag_t.MF_SOLID | mobjflag_t.MF_SPECIAL | mobjflag_t.MF_SHOOTABLE)) == 0)
            return true;

        mobj_t tm = tmthing!;
        int blockdist = thing.radius + tm.radius;

        if (abs(thing.x - tmx) >= blockdist || abs(thing.y - tmy) >= blockdist)
        {
            // didn't hit it
            return true;
        }

        // don't clip against self
        if (thing == tm)
            return true;

        int damage;

        // check for skulls slamming into things
        if ((tm.flags & mobjflag_t.MF_SKULLFLY) != 0)
        {
            damage = ((P_Random() % 8) + 1) * tm.info.damage;

            P_DamageMobj(thing, tm, tm, damage);

            tm.flags &= ~mobjflag_t.MF_SKULLFLY;
            tm.momx = tm.momy = tm.momz = 0;

            P_SetMobjState(tm, tm.info.spawnstate);

            return false; // stop moving
        }

        // missiles can hit other things
        if ((tm.flags & mobjflag_t.MF_MISSILE) != 0)
        {
            // see if it went over / under
            if (tm.z > thing.z + thing.height)
                return true; // overhead
            if (tm.z + tm.height < thing.z)
                return true; // underneath

            if (tm.target != null
                && (tm.target.type == thing.type
                    || (tm.target.type == mobjtype_t.MT_KNIGHT && thing.type == mobjtype_t.MT_BRUISER)
                    || (tm.target.type == mobjtype_t.MT_BRUISER && thing.type == mobjtype_t.MT_KNIGHT)))
            {
                // Don't hit same species as originator.
                if (thing == tm.target)
                    return true;

                if (thing.type != mobjtype_t.MT_PLAYER)
                {
                    // Explode, but do no damage.
                    // Let players missile other players.
                    return false;
                }
            }

            if ((thing.flags & mobjflag_t.MF_SHOOTABLE) == 0)
            {
                // didn't do any damage
                return (thing.flags & mobjflag_t.MF_SOLID) == 0;
            }

            // damage / explode
            damage = ((P_Random() % 8) + 1) * tm.info.damage;
            P_DamageMobj(thing, tm, tm.target, damage);

            // don't traverse any more
            return false;
        }

        // check for special pickup
        if ((thing.flags & mobjflag_t.MF_SPECIAL) != 0)
        {
            bool solid = (thing.flags & mobjflag_t.MF_SOLID) != 0;
            if ((tmflags & mobjflag_t.MF_PICKUP) != 0)
            {
                // can remove thing
                P_TouchSpecialThing(thing, tm);
            }
            return !solid;
        }

        return (thing.flags & mobjflag_t.MF_SOLID) == 0;
    }

    /// <summary>
    /// p_map.c <c>P_CheckPosition</c>: whether <paramref name="thing"/> could
    /// stand at (<paramref name="x"/>, <paramref name="y"/>) (fixed_t), not
    /// counting heights: false when a solid thing (or, for a lost soul or a
    /// missile, any shootable thing) or a blocking line is in the way. Always
    /// sets <see cref="tmfloorz"/>, <see cref="tmceilingz"/> and
    /// <see cref="tmdropoffz"/> to the sector's heights there, narrowed by
    /// the two-sided lines the box crosses (as far as the search got),
    /// <see cref="ceilingline"/> and <see cref="spechit"/>. Things are
    /// searched first (in the blocks of the box widened by
    /// <see cref="MAXRADIUS"/>), then lines.
    /// </summary>
    public bool P_CheckPosition(mobj_t thing, int x, int y)
    {
        _pitCheckLine ??= PIT_CheckLine;
        _pitCheckThing ??= PIT_CheckThing;

        tmthing = thing;
        tmflags = thing.flags;

        tmx = x;
        tmy = y;

        tmbbox[BBox.BOXTOP] = y + tmthing.radius;
        tmbbox[BBox.BOXBOTTOM] = y - tmthing.radius;
        tmbbox[BBox.BOXRIGHT] = x + tmthing.radius;
        tmbbox[BBox.BOXLEFT] = x - tmthing.radius;

        subsector_t newsubsec = R_PointInSubsector(x, y);
        ceilingline = null;

        // The base floor / ceiling is from the subsector
        // that contains the point.
        // Any contacted lines the step closer together
        // will adjust them.
        tmfloorz = tmdropoffz = newsubsec.sector.floorheight;
        tmceilingz = newsubsec.sector.ceilingheight;

        validcount++;
        numspechit = 0;

        if ((tmflags & mobjflag_t.MF_NOCLIP) != 0)
            return true;

        // Check things first, possibly picking things up.
        // The bounding box is extended by MAXRADIUS
        // because mobj_ts are grouped into mapblocks
        // based on their origin point, and can overlap
        // into adjacent blocks by up to MAXRADIUS units.
        int xl = (tmbbox[BBox.BOXLEFT] - bmaporgx - MAXRADIUS) >> Blockmap.MAPBLOCKSHIFT;
        int xh = (tmbbox[BBox.BOXRIGHT] - bmaporgx + MAXRADIUS) >> Blockmap.MAPBLOCKSHIFT;
        int yl = (tmbbox[BBox.BOXBOTTOM] - bmaporgy - MAXRADIUS) >> Blockmap.MAPBLOCKSHIFT;
        int yh = (tmbbox[BBox.BOXTOP] - bmaporgy + MAXRADIUS) >> Blockmap.MAPBLOCKSHIFT;

        for (int bx = xl; bx <= xh; bx++)
        {
            for (int by = yl; by <= yh; by++)
            {
                if (!P_BlockThingsIterator(bx, by, _pitCheckThing))
                    return false;
            }
        }

        // check lines
        xl = (tmbbox[BBox.BOXLEFT] - bmaporgx) >> Blockmap.MAPBLOCKSHIFT;
        xh = (tmbbox[BBox.BOXRIGHT] - bmaporgx) >> Blockmap.MAPBLOCKSHIFT;
        yl = (tmbbox[BBox.BOXBOTTOM] - bmaporgy) >> Blockmap.MAPBLOCKSHIFT;
        yh = (tmbbox[BBox.BOXTOP] - bmaporgy) >> Blockmap.MAPBLOCKSHIFT;

        for (int bx = xl; bx <= xh; bx++)
        {
            for (int by = yl; by <= yh; by++)
            {
                if (!P_BlockLinesIterator(bx, by, _pitCheckLine))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// p_map.c <c>P_TryMove</c>: moves <paramref name="thing"/> to
    /// (<paramref name="x"/>, <paramref name="y"/>) (fixed_t) if
    /// <see cref="P_CheckPosition"/> allows it and it fits: the opening at
    /// least its height, its top under the ceiling, a step up of at most 24
    /// units, and (without <c>MF_DROPOFF</c> or <c>MF_FLOAT</c>) no drop of
    /// more than 24 units under it. Sets <see cref="floatok"/> when only the
    /// thing's height was wrong. On success relinks it with the new
    /// <see cref="mobj_t.floorz"/>/<see cref="mobj_t.ceilingz"/> and calls
    /// <see cref="P_CrossSpecialLine"/> for the special lines it crossed.
    /// </summary>
    public bool P_TryMove(mobj_t thing, int x, int y)
    {
        floatok = false;
        if (!P_CheckPosition(thing, x, y))
            return false; // solid wall or thing

        if ((thing.flags & mobjflag_t.MF_NOCLIP) == 0)
        {
            if (tmceilingz - tmfloorz < thing.height)
                return false; // doesn't fit

            floatok = true;

            if ((thing.flags & mobjflag_t.MF_TELEPORT) == 0
                && tmceilingz - thing.z < thing.height)
                return false; // mobj must lower itself to fit

            if ((thing.flags & mobjflag_t.MF_TELEPORT) == 0
                && tmfloorz - thing.z > 24 * Fixed.FRACUNIT)
                return false; // too big a step up

            if ((thing.flags & (mobjflag_t.MF_DROPOFF | mobjflag_t.MF_FLOAT)) == 0
                && tmfloorz - tmdropoffz > 24 * Fixed.FRACUNIT)
                return false; // don't stand over a dropoff
        }

        // the move is ok,
        // so link the thing into its new position
        P_UnsetThingPosition(thing);

        int oldx = thing.x;
        int oldy = thing.y;
        thing.floorz = tmfloorz;
        thing.ceilingz = tmceilingz;
        thing.x = x;
        thing.y = y;

        P_SetThingPosition(thing);

        // if any special lines were hit, do the effect
        if ((thing.flags & (mobjflag_t.MF_TELEPORT | mobjflag_t.MF_NOCLIP)) == 0)
        {
            while (numspechit-- > 0)
            {
                // see if the line was crossed
                line_t ld = spechit[numspechit]!;
                int side = P_PointOnLineSide(thing.x, thing.y, ld);
                int oldside = P_PointOnLineSide(oldx, oldy, ld);
                if (side != oldside)
                {
                    if (ld.special != 0)
                        P_CrossSpecialLine(ld.Index, oldside, thing);
                }
            }
        }

        return true;
    }

    /// <summary>
    /// p_map.c <c>P_HitSlideLine</c>: adjusts <see cref="tmxmove"/> and
    /// <see cref="tmymove"/> so the move runs along the line: an
    /// axis-aligned line drops the move across it, any other line projects
    /// the move's (approximate) length on it.
    /// </summary>
    public void P_HitSlideLine(line_t ld)
    {
        if (ld.slopetype == SlopeType.ST_HORIZONTAL)
        {
            tmymove = 0;
            return;
        }

        if (ld.slopetype == SlopeType.ST_VERTICAL)
        {
            tmxmove = 0;
            return;
        }

        int side = P_PointOnLineSide(slidemo!.x, slidemo.y, ld);

        uint lineangle = Tables.R_PointToAngle2(0, 0, ld.dx, ld.dy);

        if (side == 1)
            lineangle = unchecked(lineangle + Tables.ANG180);

        uint moveangle = Tables.R_PointToAngle2(0, 0, tmxmove, tmymove);
        uint deltaangle = unchecked(moveangle - lineangle);

        if (deltaangle > Tables.ANG180)
            deltaangle = unchecked(deltaangle + Tables.ANG180);
        // I_Error ("SlideLine: ang>ANG180");

        lineangle >>= Tables.ANGLETOFINESHIFT;
        deltaangle >>= Tables.ANGLETOFINESHIFT;

        int movelen = P_AproxDistance(tmxmove, tmymove);
        int newlen = Fixed.FixedMul(movelen, Tables.finecosine[(int)deltaangle]);

        tmxmove = Fixed.FixedMul(newlen, Tables.finecosine[(int)lineangle]);
        tmymove = Fixed.FixedMul(newlen, Tables.finesine[(int)lineangle]);
    }

    /// <summary>
    /// p_map.c <c>PTR_SlideTraverse</c>: for <see cref="P_SlideMove"/>'s
    /// traces; a line that blocks <see cref="slidemo"/> (one-sided from the
    /// front, or an opening it does not fit or cannot step up into) becomes
    /// <see cref="bestslideline"/> if it is the nearest so far, and stops the
    /// trace.
    /// </summary>
    public bool PTR_SlideTraverse(intercept_t @in)
    {
        if (!@in.isaline)
            throw new InvalidOperationException("PTR_SlideTraverse: not a line?");

        line_t li = @in.line!;
        mobj_t mo = slidemo!;

        if ((li.flags & Line.ML_TWOSIDED) == 0)
        {
            if (P_PointOnLineSide(mo.x, mo.y, li) != 0)
            {
                // don't hit the back side
                return true;
            }
            goto isblocking;
        }

        // set openrange, opentop, openbottom
        P_LineOpening(li);

        if (openrange < mo.height)
            goto isblocking; // doesn't fit

        if (opentop - mo.z < mo.height)
            goto isblocking; // mobj is too high

        if (openbottom - mo.z > 24 * Fixed.FRACUNIT)
            goto isblocking; // too big a step up

        // this line doesn't block movement
        return true;

    // the line does block movement,
    // see if it is closer than best so far
    isblocking:
        if (@in.frac < bestslidefrac)
        {
            secondslidefrac = bestslidefrac;
            secondslideline = bestslideline;
            bestslidefrac = @in.frac;
            bestslideline = li;
        }

        return false; // stop
    }

    /// <summary>
    /// p_map.c <c>P_SlideMove</c>: the momentum of <paramref name="mo"/>
    /// ran into something; traces its three leading corners along the move,
    /// moves up to the nearest blocking line and slides along it with the
    /// rest of the move (which becomes the new momentum). After three tries,
    /// or when no line was found (a thing, or a corner), it "stairsteps":
    /// tries the y move alone, then the x move alone.
    /// </summary>
    public void P_SlideMove(mobj_t mo)
    {
        _ptrSlideTraverse ??= PTR_SlideTraverse;

        int leadx, leady, trailx, traily;

        slidemo = mo;
        int hitcount = 0;

    retry:
        if (++hitcount == 3)
            goto stairstep; // don't loop forever

        // trace along the three leading corners
        if (mo.momx > 0)
        {
            leadx = mo.x + mo.radius;
            trailx = mo.x - mo.radius;
        }
        else
        {
            leadx = mo.x - mo.radius;
            trailx = mo.x + mo.radius;
        }

        if (mo.momy > 0)
        {
            leady = mo.y + mo.radius;
            traily = mo.y - mo.radius;
        }
        else
        {
            leady = mo.y - mo.radius;
            traily = mo.y + mo.radius;
        }

        bestslidefrac = Fixed.FRACUNIT + 1;

        P_PathTraverse(leadx, leady, leadx + mo.momx, leady + mo.momy, PT_ADDLINES, _ptrSlideTraverse);
        P_PathTraverse(trailx, leady, trailx + mo.momx, leady + mo.momy, PT_ADDLINES, _ptrSlideTraverse);
        P_PathTraverse(leadx, traily, leadx + mo.momx, traily + mo.momy, PT_ADDLINES, _ptrSlideTraverse);

        // move up to the wall
        if (bestslidefrac == Fixed.FRACUNIT + 1)
        {
            // the move most have hit the middle, so stairstep
            goto stairstep;
        }

        // fudge a bit to make sure it doesn't hit
        bestslidefrac -= 0x800;
        if (bestslidefrac > 0)
        {
            int newx = Fixed.FixedMul(mo.momx, bestslidefrac);
            int newy = Fixed.FixedMul(mo.momy, bestslidefrac);

            if (!P_TryMove(mo, mo.x + newx, mo.y + newy))
                goto stairstep;
        }

        // Now continue along the wall.
        // First calculate remainder.
        bestslidefrac = Fixed.FRACUNIT - (bestslidefrac + 0x800);

        if (bestslidefrac > Fixed.FRACUNIT)
            bestslidefrac = Fixed.FRACUNIT;

        if (bestslidefrac <= 0)
            return;

        tmxmove = Fixed.FixedMul(mo.momx, bestslidefrac);
        tmymove = Fixed.FixedMul(mo.momy, bestslidefrac);

        P_HitSlideLine(bestslideline!); // clip the moves

        mo.momx = tmxmove;
        mo.momy = tmymove;

        if (!P_TryMove(mo, mo.x + tmxmove, mo.y + tmymove))
            goto retry;
        return;

    stairstep:
        if (!P_TryMove(mo, mo.x, mo.y + mo.momy))
            P_TryMove(mo, mo.x + mo.momx, mo.y);
    }

    // ---- p_map.c: sector height changing (T5.1) ----

    /// <summary>p_map.c <c>crushchange</c>: whether <see cref="P_ChangeSector"/>'s move crushes (damages things that don't fit).</summary>
    public bool crushchange;

    /// <summary>p_map.c <c>nofit</c>: set by <see cref="PIT_ChangeSector"/> when a shootable thing no longer fits.</summary>
    public bool nofit;

    private Func<mobj_t, bool>? _pitChangeSector;

    /// <summary>
    /// p_map.c <c>P_ThingHeightClip</c>: takes a valid thing and adjusts the
    /// thing->floorz, thing->ceilingz, and possibly thing->z. This is called
    /// for all nearby monsters whenever a sector changes height. If the thing
    /// doesn't fit, the z will be set to the lowest value and false will be
    /// returned.
    /// </summary>
    public bool P_ThingHeightClip(mobj_t thing)
    {
        bool onfloor = thing.z == thing.floorz;

        P_CheckPosition(thing, thing.x, thing.y);
        // what about stranding a monster partially off an edge?

        thing.floorz = tmfloorz;
        thing.ceilingz = tmceilingz;

        if (onfloor)
        {
            // walking monsters rise and fall with the floor
            thing.z = thing.floorz;
        }
        else
        {
            // don't adjust a floating monster unless forced to
            if (thing.z + thing.height > thing.ceilingz)
                thing.z = thing.ceilingz - thing.height;
        }

        if (thing.ceilingz - thing.floorz < thing.height)
            return false;

        return true;
    }

    /// <summary>
    /// p_map.c <c>PIT_ChangeSector</c>: fits <paramref name="thing"/> to the
    /// new heights (<see cref="P_ThingHeightClip"/>). One that doesn't fit is
    /// crunched to gibs when dead, removed when a dropped item, left alone
    /// unless shootable; a shootable one sets <see cref="nofit"/> and, while
    /// <see cref="crushchange"/> on every fourth tic, takes 10 damage
    /// (<see cref="P_DamageMobj"/>) and
    /// sprays blood. Always keeps checking.
    /// </summary>
    public bool PIT_ChangeSector(mobj_t thing)
    {
        if (P_ThingHeightClip(thing))
        {
            // keep checking
            return true;
        }

        // crunch bodies to giblets
        if (thing.health <= 0)
        {
            P_SetMobjState(thing, statenum_t.S_GIBS);

            thing.flags &= ~mobjflag_t.MF_SOLID;
            thing.height = 0;
            thing.radius = 0;

            // keep checking
            return true;
        }

        // crunch dropped items
        if ((thing.flags & mobjflag_t.MF_DROPPED) != 0)
        {
            P_RemoveMobj(thing);

            // keep checking
            return true;
        }

        if ((thing.flags & mobjflag_t.MF_SHOOTABLE) == 0)
        {
            // assume it is bloody gibs or something
            return true;
        }

        nofit = true;

        if (crushchange && (leveltime & 3) == 0)
        {
            P_DamageMobj(thing, null, null, 10);

            // spray blood in a random direction
            mobj_t mo = P_SpawnMobj(thing.x, thing.y, thing.z + thing.height / 2, mobjtype_t.MT_BLOOD);

            // (left operand first, as Boom's order-independent rewrite and C#)
            mo.momx = (P_Random() - P_Random()) << 12;
            mo.momy = (P_Random() - P_Random()) << 12;
        }

        // keep checking (crush other things)
        return true;
    }

    /// <summary>
    /// p_map.c <c>P_ChangeSector</c>: after <paramref name="sector"/>'s floor
    /// or ceiling moved, re-checks heights for all things near it (the
    /// blocks of its <see cref="sector_t.blockbox"/>), crushing with
    /// <paramref name="crunch"/>. Returns <see cref="nofit"/>: whether a
    /// shootable thing no longer fits (a door or crusher then reverses or
    /// stops, T5.3/T5.5).
    /// </summary>
    public bool P_ChangeSector(sector_t sector, bool crunch)
    {
        _pitChangeSector ??= PIT_ChangeSector;

        nofit = false;
        crushchange = crunch;

        // re-check heights for all things near the moving sector
        for (int x = sector.blockbox[BBox.BOXLEFT]; x <= sector.blockbox[BBox.BOXRIGHT]; x++)
        {
            for (int y = sector.blockbox[BBox.BOXBOTTOM]; y <= sector.blockbox[BBox.BOXTOP]; y++)
                P_BlockThingsIterator(x, y, _pitChangeSector);
        }

        return nofit;
    }

    // ---- the use action (T5.2) ----

    /// <summary>p_local.h <c>USERANGE</c>: how far a player reaches to use a line (fixed_t).</summary>
    public const int USERANGE = 64 * Fixed.FRACUNIT;

    /// <summary>p_map.c <c>usething</c>: the thing <see cref="P_UseLines"/> uses lines for.</summary>
    public mobj_t? usething;

    /// <summary>
    /// Not vanilla: whether the last <see cref="P_UseLines"/> trace ran a use
    /// special (<see cref="IsUseSpecial"/>) from its front side; when it did not,
    /// <see cref="Tweaks.UseFallback"/> looks for another line (SPEC §12 T5.2).
    /// </summary>
    public bool usetraceused;

    // The use fallback's candidates (line, squared distance) and the one its trace looks for.
    private readonly System.Collections.Generic.List<(line_t line, long dist2)> _useCandidates = new();
    private line_t? _useFallbackTarget;
    private bool _useFallbackReached;

    private traverser_t? _ptrUseTraverse;
    private traverser_t? _ptrUseFallbackTraverse;
    private Func<line_t, bool>? _pitAddUseCandidate;

    /// <summary>
    /// p_map.c <c>PTR_UseTraverse</c>: the first special line on the trace is
    /// used (<see cref="P_UseSpecialLine"/>, from whichever side the thing is
    /// on) and stops it; a closed line (no opening) stops it first ("can't use
    /// through a wall", vanilla's <c>sfx_noway</c>).
    /// </summary>
    public bool PTR_UseTraverse(intercept_t @in)
    {
        line_t line = @in.line!;
        if (line.special == 0)
        {
            P_LineOpening(line);
            if (openrange <= 0)
            {
                // S_StartSound (usething, sfx_noway); (T6.10; with the use fallback,
                // only when it finds no line either, SPEC §12 T5.2)

                // can't use through a wall
                return false;
            }
            // not a special line, but keep checking
            return true;
        }

        int side = 0;
        if (P_PointOnLineSide(usething!.x, usething.y, line) == 1)
            side = 1;

        //	return false;		// don't use back side

        if (side == 0 && IsUseSpecial(line.special))
            usetraceused = true; // not vanilla: for the use fallback

        P_UseSpecialLine(usething, line, side);

        // can't use for than one special line in a row
        return false;
    }

    /// <summary>
    /// p_map.c <c>P_UseLines</c>: looks for special lines in front of the
    /// player to activate: a trace of <see cref="USERANGE"/> along its angle
    /// (with <see cref="Tweaks.AbsoluteAiming"/> the aim), <see cref="PTR_UseTraverse"/>.
    /// With <see cref="Tweaks.UseFallback"/>, when the trace ran no use
    /// special, <see cref="UseFallbackLine"/>'s line is used instead (SPEC
    /// §6.3 #3, §12 T5.2).
    /// </summary>
    public void P_UseLines(player_t player)
    {
        _ptrUseTraverse ??= PTR_UseTraverse;
        mobj_t mo = player.mo!;
        usething = mo;

        int angle = (int)(mo.angle >> Tables.ANGLETOFINESHIFT);

        int x1 = mo.x;
        int y1 = mo.y;
        int x2 = x1 + (USERANGE >> Fixed.FRACBITS) * Tables.finecosine[angle];
        int y2 = y1 + (USERANGE >> Fixed.FRACBITS) * Tables.finesine[angle];

        usetraceused = false;
        P_PathTraverse(x1, y1, x2, y2, PT_ADDLINES, _ptrUseTraverse);

        if (tweaks.UseFallback && !usetraceused)
        {
            line_t? line = UseFallbackLine(mo);
            if (line != null)
                P_UseSpecialLine(mo, line, 0);
            // else S_StartSound (usething, sfx_noway) if the trace met a wall (T6.10)
        }
    }

    /// <summary>
    /// Not vanilla: the use fallback (SPEC §6.3 #3, tuned in §12 T5.2): the
    /// nearest line within <see cref="USERANGE"/> of <paramref name="mo"/>'s
    /// centre (to the nearest point of the line, kept 1/64 of its length off
    /// its ends) that has a use special (<see cref="IsUseSpecial"/>), has
    /// <paramref name="mo"/> on its front side, is not behind it (that point
    /// within 90° of its angle) and can be reached: a trace to it crosses no
    /// closed line (two-sided lines with an opening let it through, special
    /// or not). Ties go to the lower line number. Null when there is none.
    /// Deterministic: fixed-point maths, blockmap order, a stable sort.
    /// </summary>
    public line_t? UseFallbackLine(mobj_t mo)
    {
        _pitAddUseCandidate ??= PIT_AddUseCandidate;
        _ptrUseFallbackTraverse ??= PTR_UseFallbackTraverse;
        usething = mo;
        _useCandidates.Clear();

        int xl = (mo.x - USERANGE - bmaporgx) >> Blockmap.MAPBLOCKSHIFT;
        int xh = (mo.x + USERANGE - bmaporgx) >> Blockmap.MAPBLOCKSHIFT;
        int yl = (mo.y - USERANGE - bmaporgy) >> Blockmap.MAPBLOCKSHIFT;
        int yh = (mo.y + USERANGE - bmaporgy) >> Blockmap.MAPBLOCKSHIFT;

        validcount++;
        for (int bx = xl; bx <= xh; bx++)
        {
            for (int by = yl; by <= yh; by++)
                P_BlockLinesIterator(bx, by, _pitAddUseCandidate);
        }

        // Nearest first, then by line number (insertion sort: stable and allocation free).
        for (int i = 1; i < _useCandidates.Count; i++)
        {
            var c = _useCandidates[i];
            int j = i - 1;
            while (j >= 0 && (_useCandidates[j].dist2 > c.dist2
                || (_useCandidates[j].dist2 == c.dist2 && _useCandidates[j].line.Index > c.line.Index)))
            {
                _useCandidates[j + 1] = _useCandidates[j];
                j--;
            }
            _useCandidates[j + 1] = c;
        }

        foreach ((line_t line, long _) in _useCandidates)
        {
            UseFallbackPoint(mo, line, out int qx, out int qy);
            // A trace through the point to as far beyond it: the line is crossed halfway.
            _useFallbackTarget = line;
            _useFallbackReached = false;
            P_PathTraverse(mo.x, mo.y, qx + (qx - mo.x), qy + (qy - mo.y), PT_ADDLINES, _ptrUseFallbackTraverse);
            if (_useFallbackReached)
                return line;
        }
        return null;
    }

    // UseFallbackLine's blockmap iterator: lists the lines that qualify but for the trace.
    private bool PIT_AddUseCandidate(line_t ld)
    {
        mobj_t mo = usething!;
        if (ld.special == 0 || !IsUseSpecial(ld.special))
            return true;
        if (P_PointOnLineSide(mo.x, mo.y, ld) != 0)
            return true; // not facing it
        if (!UseFallbackPoint(mo, ld, out int qx, out int qy))
            return true;

        long dx = (long)qx - mo.x;
        long dy = (long)qy - mo.y;
        if (dx > USERANGE || dx < -USERANGE || dy > USERANGE || dy < -USERANGE)
            return true;
        long dist2 = dx * dx + dy * dy;
        if (dist2 > (long)USERANGE * USERANGE)
            return true;

        // Not behind: within 90 degrees of the facing.
        int angle = (int)(mo.angle >> Tables.ANGLETOFINESHIFT);
        if (dx * Tables.finecosine[angle] + dy * Tables.finesine[angle] < 0)
            return true;

        _useCandidates.Add((ld, dist2));
        return true;
    }

    // UseFallbackLine's trace: stops at the line looked for (reached) or at a closed line.
    private bool PTR_UseFallbackTraverse(intercept_t @in)
    {
        line_t line = @in.line!;
        if (line == _useFallbackTarget)
        {
            _useFallbackReached = true;
            return false;
        }
        P_LineOpening(line);
        return openrange > 0;
    }

    /// <summary>
    /// The point of <paramref name="line"/> nearest to <paramref name="mo"/>'s
    /// centre (fixed_t), kept 1/64 of the line's length off its ends so a
    /// trace to it crosses the line; false for a line of no length. In
    /// 1/16-unit steps (64-bit), exact enough for whole-unit vertices.
    /// </summary>
    private static bool UseFallbackPoint(mobj_t mo, line_t line, out int qx, out int qy)
    {
        long ldx = line.dx >> 12;
        long ldy = line.dy >> 12;
        long len2 = ldx * ldx + ldy * ldy;
        qx = qy = 0;
        if (len2 == 0)
            return false;
        long num = ((long)(mo.x >> 12) - (line.v1.X >> 12)) * ldx + ((long)(mo.y >> 12) - (line.v1.Y >> 12)) * ldy;
        long margin = len2 / 64;
        if (num < margin)
            num = margin;
        if (num > len2 - margin)
            num = len2 - margin;
        long f = (num << Fixed.FRACBITS) / len2;
        qx = line.v1.X + (int)(((long)line.dx * f) >> Fixed.FRACBITS);
        qy = line.v1.Y + (int)(((long)line.dy * f) >> Fixed.FRACBITS);
        return true;
    }
}
