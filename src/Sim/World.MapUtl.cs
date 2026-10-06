using IsoDoom.Map;

namespace IsoDoom.Sim;

// p_maputl.c: sector and block links (the rest of p_maputl.c is T4.3's).
public sealed partial class World
{
    /// <summary>
    /// p_maputl.c <c>P_UnsetThingPosition</c>: unlinks a thing from its
    /// sector's and block's lists. Call it before changing the thing's
    /// position (or flags that affect the links), then
    /// <see cref="P_SetThingPosition"/>.
    /// </summary>
    public void P_UnsetThingPosition(mobj_t thing)
    {
        if ((thing.flags & mobjflag_t.MF_NOSECTOR) == 0)
        {
            // inert things don't need to be in blockmap?
            // unlink from subsector
            if (thing.snext != null)
                thing.snext.sprev = thing.sprev;

            if (thing.sprev != null)
                thing.sprev.snext = thing.snext;
            else
                thing.subsector.sector.thinglist = thing.snext;
        }

        if ((thing.flags & mobjflag_t.MF_NOBLOCKMAP) == 0)
        {
            // inert things don't need to be in blockmap
            // unlink from block map
            if (thing.bnext != null)
                thing.bnext.bprev = thing.bprev;

            if (thing.bprev != null)
            {
                thing.bprev.bnext = thing.bnext;
            }
            else
            {
                int blockx = (thing.x - bmaporgx) >> Blockmap.MAPBLOCKSHIFT;
                int blocky = (thing.y - bmaporgy) >> Blockmap.MAPBLOCKSHIFT;

                if (blockx >= 0 && blockx < bmapwidth && blocky >= 0 && blocky < bmapheight)
                    blocklinks[blocky * bmapwidth + blockx] = thing.bnext;
            }
        }
    }

    /// <summary>
    /// p_maputl.c <c>P_SetThingPosition</c>: links a thing into its
    /// subsector's sector list (at the head, unless <c>MF_NOSECTOR</c>) and
    /// its block's list (at the head, unless <c>MF_NOBLOCKMAP</c>; a thing
    /// off the blockmap is in no block). Sets <see cref="mobj_t.subsector"/>.
    /// </summary>
    public void P_SetThingPosition(mobj_t thing)
    {
        // link into subsector
        subsector_t ss = R_PointInSubsector(thing.x, thing.y);
        thing.subsector = ss;

        if ((thing.flags & mobjflag_t.MF_NOSECTOR) == 0)
        {
            // invisible things don't go into the sector links
            sector_t sec = ss.sector;

            thing.sprev = null;
            thing.snext = sec.thinglist;

            if (sec.thinglist != null)
                sec.thinglist.sprev = thing;

            sec.thinglist = thing;
        }

        // link into blockmap
        if ((thing.flags & mobjflag_t.MF_NOBLOCKMAP) == 0)
        {
            // inert things don't need to be in blockmap
            int blockx = (thing.x - bmaporgx) >> Blockmap.MAPBLOCKSHIFT;
            int blocky = (thing.y - bmaporgy) >> Blockmap.MAPBLOCKSHIFT;

            if (blockx >= 0 && blockx < bmapwidth && blocky >= 0 && blocky < bmapheight)
            {
                ref mobj_t? link = ref blocklinks[blocky * bmapwidth + blockx];
                thing.bprev = null;
                thing.bnext = link;
                if (link != null)
                    link.bprev = thing;

                link = thing;
            }
            else
            {
                // thing is off the map
                thing.bnext = thing.bprev = null;
            }
        }
    }
}
