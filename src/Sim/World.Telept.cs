using IsoDoom.Map;

namespace IsoDoom.Sim;

// p_telept.c: teleportation (EV_Teleport), and p_map.c's teleport move
// (P_TeleportMove, PIT_StompThing), T5.6.
public sealed partial class World
{
    /// <summary>
    /// p_telept.c <c>EV_Teleport</c>: teleports <paramref name="thing"/>
    /// (crossing <paramref name="line"/> from <paramref name="side"/>) to the
    /// first teleport destination (<c>MT_TELEPORTMAN</c>, in thinker order) in
    /// the first sector with the line's tag (in sector order) that holds one:
    /// <see cref="P_TeleportMove"/> there (killing what stands in the way), onto
    /// the floor, facing the destination's angle, with no momentum; teleport
    /// fog (<c>MT_TFOG</c>) and its sound at the old position and 20 units in
    /// front of the destination; a player cannot move for 18 tics
    /// (<see cref="mobj_t.reactiontime"/>). Missiles and things crossing from
    /// the back side are not teleported. Returns 1 when it teleported.
    /// Not vanilla: the thing is not interpolated across the move
    /// (<see cref="mobj_t.interp"/>, T4.7).
    /// </summary>
    public int EV_Teleport(line_t line, int side, mobj_t thing)
    {
        // don't teleport missiles
        if ((thing.flags & mobjflag_t.MF_MISSILE) != 0)
            return 0;

        // Don't teleport if hit back of line,
        //  so you can get out of teleporter.
        if (side == 1)
            return 0;

        int tag = line.tag;
        for (int i = 0; i < sectors.Length; i++)
        {
            if (sectors[i].tag != tag)
                continue;
            for (thinker_t thinker = thinkercap.next; thinker != thinkercap; thinker = thinker.next)
            {
                // not a mobj
                if (thinker.function != think_t.P_MobjThinker || thinker is not mobj_t m)
                    continue;

                // not a teleportman
                if (m.type != mobjtype_t.MT_TELEPORTMAN)
                    continue;

                sector_t sector = m.subsector.sector;
                // wrong sector
                if (sector.Index != i)
                    continue;

                int oldx = thing.x;
                int oldy = thing.y;
                int oldz = thing.z;

                if (!P_TeleportMove(thing, m.x, m.y))
                    return 0;

                // (Final Doom's executable leaves z as it was: T10.7.)
                thing.z = thing.floorz; //fixme: not needed?
                if (thing.player is { } player)
                    player.viewz = thing.z + player.viewheight;

                // spawn teleport fog at source and destination
                mobj_t fog = P_SpawnMobj(oldx, oldy, oldz, mobjtype_t.MT_TFOG);
                S_StartSound(fog, sfxenum_t.sfx_telept);
                int an = (int)(m.angle >> Tables.ANGLETOFINESHIFT);
                fog = P_SpawnMobj(m.x + 20 * Tables.finecosine[an], m.y + 20 * Tables.finesine[an], thing.z, mobjtype_t.MT_TFOG);

                // emit sound, where?
                S_StartSound(fog, sfxenum_t.sfx_telept);

                // don't move for a bit
                if (thing.player != null)
                    thing.reactiontime = 18;

                thing.angle = m.angle;
                thing.momx = thing.momy = thing.momz = 0;
                thing.interp = false; // not vanilla: no interpolation across the teleport (T4.7)
                return 1;
            }
        }
        return 0;
    }

    private System.Func<mobj_t, bool>? _pitStompThing;

    /// <summary>
    /// p_map.c <c>PIT_StompThing</c>: a shootable thing whose box overlaps
    /// <see cref="tmthing"/>'s at (<see cref="tmx"/>, <see cref="tmy"/>) is
    /// killed (<see cref="P_DamageMobj"/>, 10000) when a player teleports (or
    /// anything on map 30); a monster is stopped by it instead (false).
    /// </summary>
    public bool PIT_StompThing(mobj_t thing)
    {
        if ((thing.flags & mobjflag_t.MF_SHOOTABLE) == 0)
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

        // monsters don't stomp things except on boss level
        if (tm.player == null && gamemap != 30)
            return false;

        P_DamageMobj(thing, tm, tm, 10000);

        return true;
    }

    /// <summary>
    /// p_map.c <c>P_TeleportMove</c>: moves <paramref name="thing"/> to
    /// (<paramref name="x"/>, <paramref name="y"/>) (fixed_t) whatever the
    /// lines and heights there, killing the shootable things in the way
    /// (<see cref="PIT_StompThing"/>; false, not moved, when one stops it).
    /// Sets the thing's <see cref="mobj_t.floorz"/>/<see cref="mobj_t.ceilingz"/>
    /// to the sector's at the point (no line narrows them) and, as vanilla,
    /// empties <see cref="spechit"/> (<see cref="numspechit"/> 0), which ends
    /// the crossing loop of the <see cref="P_TryMove"/> that teleported.
    /// </summary>
    public bool P_TeleportMove(mobj_t thing, int x, int y)
    {
        _pitStompThing ??= PIT_StompThing;

        // kill anything occupying the position
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

        // The base floor/ceiling is from the subsector
        // that contains the point.
        // Any contacted lines the step closer together
        // will adjust them.
        tmfloorz = tmdropoffz = newsubsec.sector.floorheight;
        tmceilingz = newsubsec.sector.ceilingheight;

        validcount++;
        numspechit = 0;

        // stomp on any things contacted
        int xl = (tmbbox[BBox.BOXLEFT] - bmaporgx - MAXRADIUS) >> Blockmap.MAPBLOCKSHIFT;
        int xh = (tmbbox[BBox.BOXRIGHT] - bmaporgx + MAXRADIUS) >> Blockmap.MAPBLOCKSHIFT;
        int yl = (tmbbox[BBox.BOXBOTTOM] - bmaporgy - MAXRADIUS) >> Blockmap.MAPBLOCKSHIFT;
        int yh = (tmbbox[BBox.BOXTOP] - bmaporgy + MAXRADIUS) >> Blockmap.MAPBLOCKSHIFT;

        for (int bx = xl; bx <= xh; bx++)
        {
            for (int by = yl; by <= yh; by++)
            {
                if (!P_BlockThingsIterator(bx, by, _pitStompThing))
                    return false;
            }
        }

        // the move is ok,
        // so link the thing into its new position
        P_UnsetThingPosition(thing);

        thing.floorz = tmfloorz;
        thing.ceilingz = tmceilingz;
        thing.x = x;
        thing.y = y;

        P_SetThingPosition(thing);

        return true;
    }
}
