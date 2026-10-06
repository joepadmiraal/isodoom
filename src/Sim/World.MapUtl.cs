using System;
using IsoDoom.Map;

namespace IsoDoom.Sim;

// p_maputl.c (all of it): distances, line sides, line openings, sector and block links,
// the blockmap iterators and path traversal (intercepts).
public sealed partial class World
{
    // ---- p_local.h ----

    /// <summary>
    /// p_local.h <c>MAXINTERCEPTS</c>: the size of vanilla's intercept array.
    /// Vanilla writes past it (no check); here the list grows instead and
    /// <see cref="interceptoverruns"/> counts the entries vanilla would have
    /// overrun with (SPEC §12 T4.3).
    /// </summary>
    public const int MAXINTERCEPTS = 128;

    /// <summary>p_local.h <c>PT_ADDLINES</c>: <see cref="P_PathTraverse"/> collects line intercepts.</summary>
    public const int PT_ADDLINES = 1;

    /// <summary>p_local.h <c>PT_ADDTHINGS</c>: <see cref="P_PathTraverse"/> collects thing intercepts.</summary>
    public const int PT_ADDTHINGS = 2;

    /// <summary>p_local.h <c>PT_EARLYOUT</c>: stop collecting at the first one-sided line within the trace.</summary>
    public const int PT_EARLYOUT = 4;

    /// <summary>p_local.h <c>MAPBTOFRAC</c>: block-relative fixed_t to blocks as 16.16.</summary>
    public const int MAPBTOFRAC = Blockmap.MAPBLOCKSHIFT - Fixed.FRACBITS;

    // ---- p_maputl.c globals ----

    /// <summary>
    /// r_main.c <c>validcount</c>: incremented to start a search, so each
    /// line (<see cref="line_t.validcount"/>) is visited once per search
    /// although it sits in several blocks. Starts at 1, as vanilla.
    /// </summary>
    public int validcount = 1;

    /// <summary>p_maputl.c <c>opentop</c>: set by <see cref="P_LineOpening"/>, the lower ceiling.</summary>
    public int opentop;

    /// <summary>p_maputl.c <c>openbottom</c>: set by <see cref="P_LineOpening"/>, the higher floor.</summary>
    public int openbottom;

    /// <summary>p_maputl.c <c>openrange</c>: set by <see cref="P_LineOpening"/>, <c>opentop - openbottom</c> (0 for a one-sided line).</summary>
    public int openrange;

    /// <summary>p_maputl.c <c>lowfloor</c>: set by <see cref="P_LineOpening"/>, the lower floor.</summary>
    public int lowfloor;

    /// <summary>p_maputl.c <c>intercepts</c>: the intercepts <see cref="P_PathTraverse"/> collected, up to <see cref="intercept_p"/>.</summary>
    public intercept_t[] intercepts { get; private set; } = NewIntercepts(0, MAXINTERCEPTS);

    /// <summary>p_maputl.c <c>intercept_p</c>: the number of intercepts in <see cref="intercepts"/>.</summary>
    public int intercept_p;

    /// <summary>
    /// The intercepts collected beyond <see cref="MAXINTERCEPTS"/> in one
    /// traversal, summed over the world's life: each one is a write past
    /// vanilla's array (which corrupts the globals after it; Chocolate Doom's
    /// <c>InterceptsOverrun</c> emulates part of that, this port does not).
    /// </summary>
    public int interceptoverruns;

    /// <summary>p_maputl.c <c>trace</c>: the line <see cref="P_PathTraverse"/> traces.</summary>
    public divline_t trace;

    /// <summary>p_maputl.c <c>earlyout</c>: <see cref="PT_EARLYOUT"/> was given.</summary>
    public bool earlyout;

    // Cached delegates for the iterators (no allocation per traversal).
    private Func<line_t, bool>? _pitAddLineIntercepts;
    private Func<mobj_t, bool>? _pitAddThingIntercepts;

    private static intercept_t[] NewIntercepts(int from, int length)
    {
        var array = new intercept_t[length];
        for (int i = from; i < length; i++)
            array[i] = new intercept_t();
        return array;
    }

    // C's abs(): abs(INT_MIN) stays INT_MIN.
    private static int abs(int v) => v < 0 ? unchecked(-v) : v;

    /// <summary>
    /// p_maputl.c <c>P_AproxDistance</c>: the distance between two points
    /// given their differences, approximated as the larger plus half the
    /// smaller (fixed_t).
    /// </summary>
    public static int P_AproxDistance(int dx, int dy)
    {
        dx = abs(dx);
        dy = abs(dy);
        if (dx < dy)
            return dx + dy - (dx >> 1);
        return dx + dy - (dy >> 1);
    }

    /// <summary>
    /// p_maputl.c <c>P_PointOnLineSide</c>: 0 when (<paramref name="x"/>,
    /// <paramref name="y"/>) is on the front (right) side of the line, 1 on
    /// the back; a point on the line is on the back, except on axis-aligned
    /// lines, where it depends on the direction.
    /// </summary>
    public static int P_PointOnLineSide(int x, int y, line_t line)
    {
        if (line.dx == 0)
        {
            if (x <= line.v1.X)
                return line.dy > 0 ? 1 : 0;
            return line.dy < 0 ? 1 : 0;
        }
        if (line.dy == 0)
        {
            if (y <= line.v1.Y)
                return line.dx < 0 ? 1 : 0;
            return line.dx > 0 ? 1 : 0;
        }

        int dx = x - line.v1.X;
        int dy = y - line.v1.Y;

        int left = Fixed.FixedMul(line.dy >> Fixed.FRACBITS, dx);
        int right = Fixed.FixedMul(dy, line.dx >> Fixed.FRACBITS);

        if (right < left)
            return 0; // front side
        return 1; // back side
    }

    /// <summary>
    /// p_maputl.c <c>P_BoxOnLineSide</c>: the side (0 front, 1 back) of the
    /// line the whole box (<see cref="BBox"/> indices) is on, or -1 when the
    /// line's infinite extension crosses it.
    /// </summary>
    public static int P_BoxOnLineSide(int[] tmbox, line_t ld)
    {
        int p1, p2;
        switch (ld.slopetype)
        {
            case SlopeType.ST_HORIZONTAL:
                p1 = tmbox[BBox.BOXTOP] > ld.v1.Y ? 1 : 0;
                p2 = tmbox[BBox.BOXBOTTOM] > ld.v1.Y ? 1 : 0;
                if (ld.dx < 0)
                {
                    p1 ^= 1;
                    p2 ^= 1;
                }
                break;

            case SlopeType.ST_VERTICAL:
                p1 = tmbox[BBox.BOXRIGHT] < ld.v1.X ? 1 : 0;
                p2 = tmbox[BBox.BOXLEFT] < ld.v1.X ? 1 : 0;
                if (ld.dy < 0)
                {
                    p1 ^= 1;
                    p2 ^= 1;
                }
                break;

            case SlopeType.ST_POSITIVE:
                p1 = P_PointOnLineSide(tmbox[BBox.BOXLEFT], tmbox[BBox.BOXTOP], ld);
                p2 = P_PointOnLineSide(tmbox[BBox.BOXRIGHT], tmbox[BBox.BOXBOTTOM], ld);
                break;

            default: // ST_NEGATIVE
                p1 = P_PointOnLineSide(tmbox[BBox.BOXRIGHT], tmbox[BBox.BOXTOP], ld);
                p2 = P_PointOnLineSide(tmbox[BBox.BOXLEFT], tmbox[BBox.BOXBOTTOM], ld);
                break;
        }

        if (p1 == p2)
            return p1;
        return -1;
    }

    /// <summary>
    /// p_maputl.c <c>P_PointOnDivlineSide</c>: 0 when the point is on the
    /// front (right) side of the divline, 1 on the back. Coarser than
    /// <see cref="P_PointOnLineSide"/> (both products drop 8 bits), with a
    /// sign-bit shortcut.
    /// </summary>
    public static int P_PointOnDivlineSide(int x, int y, in divline_t line)
    {
        if (line.dx == 0)
        {
            if (x <= line.x)
                return line.dy > 0 ? 1 : 0;
            return line.dy < 0 ? 1 : 0;
        }
        if (line.dy == 0)
        {
            if (y <= line.y)
                return line.dx < 0 ? 1 : 0;
            return line.dx > 0 ? 1 : 0;
        }

        int dx = x - line.x;
        int dy = y - line.y;

        // try to quickly decide by looking at sign bits
        if (((line.dy ^ line.dx ^ dx ^ dy) & unchecked((int)0x80000000)) != 0)
        {
            if (((line.dy ^ dx) & unchecked((int)0x80000000)) != 0)
                return 1; // (left is negative)
            return 0;
        }

        int left = Fixed.FixedMul(line.dy >> 8, dx >> 8);
        int right = Fixed.FixedMul(dy >> 8, line.dx >> 8);

        if (right < left)
            return 0; // front side
        return 1; // back side
    }

    /// <summary>p_maputl.c <c>P_MakeDivline</c>: the line's first vertex and direction.</summary>
    public static void P_MakeDivline(line_t li, out divline_t dl)
    {
        dl.x = li.v1.X;
        dl.y = li.v1.Y;
        dl.dx = li.dx;
        dl.dy = li.dy;
    }

    /// <summary>
    /// p_maputl.c <c>P_InterceptVector</c>: the fractional position (fixed_t,
    /// 0 to <see cref="Fixed.FRACUNIT"/> between its ends) along
    /// <paramref name="v2"/> where it crosses <paramref name="v1"/>; 0 for
    /// parallel lines. Precision drops 8 bits on <paramref name="v1"/>'s side,
    /// as vanilla.
    /// </summary>
    public static int P_InterceptVector(in divline_t v2, in divline_t v1)
    {
        int den = Fixed.FixedMul(v1.dy >> 8, v2.dx) - Fixed.FixedMul(v1.dx >> 8, v2.dy);

        if (den == 0)
            return 0;
        // I_Error ("P_InterceptVector: parallel");

        int num = Fixed.FixedMul((v1.x - v2.x) >> 8, v1.dy)
            + Fixed.FixedMul((v2.y - v1.y) >> 8, v1.dx);

        return Fixed.FixedDiv(num, den);
    }

    /// <summary>
    /// p_maputl.c <c>P_LineOpening</c>: sets <see cref="opentop"/>,
    /// <see cref="openbottom"/>, <see cref="openrange"/> and
    /// <see cref="lowfloor"/> for the gap between the line's two sectors. A
    /// one-sided line sets only <c>openrange = 0</c> (the rest keep their
    /// last values, as vanilla).
    /// </summary>
    public void P_LineOpening(line_t linedef)
    {
        if (linedef.sidenum[1] == -1)
        {
            // single sided line
            openrange = 0;
            return;
        }

        sector_t front = linedef.frontsector!;
        sector_t back = linedef.backsector!;

        if (front.ceilingheight < back.ceilingheight)
            opentop = front.ceilingheight;
        else
            opentop = back.ceilingheight;

        if (front.floorheight > back.floorheight)
        {
            openbottom = front.floorheight;
            lowfloor = back.floorheight;
        }
        else
        {
            openbottom = back.floorheight;
            lowfloor = front.floorheight;
        }

        openrange = opentop - openbottom;
    }

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

    /// <summary>
    /// p_maputl.c <c>P_BlockLinesIterator</c>: calls <paramref name="func"/>
    /// for each line in block (<paramref name="x"/>, <paramref name="y"/>)
    /// not yet visited in this search (<see cref="validcount"/>; increment it
    /// first to start one), in blockmap list order (the node builders' leading
    /// line 0 included). Returns false as soon as <paramref name="func"/> does;
    /// a block off the map has no lines (true).
    /// </summary>
    public bool P_BlockLinesIterator(int x, int y, Func<line_t, bool> func)
    {
        if (x < 0 || y < 0 || x >= bmapwidth || y >= bmapheight)
            return true;

        ReadOnlySpan<short> blockmaplump = level.Blockmap.BlockmapLump;
        int offset = y * bmapwidth + x;
        // blockmap = blockmaplump + 4; the offset read unsigned (Blockmap)
        offset = (ushort)blockmaplump[Blockmap.HeaderSize + offset];

        for (int list = offset; blockmaplump[list] != -1; list++)
        {
            line_t ld = lines[(ushort)blockmaplump[list]];

            if (ld.validcount == validcount)
                continue; // line has already been checked

            ld.validcount = validcount;

            if (!func(ld))
                return false;
        }
        return true; // everything was checked
    }

    /// <summary>
    /// p_maputl.c <c>P_BlockThingsIterator</c>: calls <paramref name="func"/>
    /// for each mobj in block (<paramref name="x"/>, <paramref name="y"/>)'s
    /// list (by position: a thing is only in the block of its centre).
    /// Returns false as soon as <paramref name="func"/> does.
    /// </summary>
    public bool P_BlockThingsIterator(int x, int y, Func<mobj_t, bool> func)
    {
        if (x < 0 || y < 0 || x >= bmapwidth || y >= bmapheight)
            return true;

        for (mobj_t? mobj = blocklinks[y * bmapwidth + x]; mobj != null; mobj = mobj.bnext)
        {
            if (!func(mobj))
                return false;
        }
        return true;
    }

    // Takes the next free intercept, growing the array past vanilla's size.
    private intercept_t NextIntercept()
    {
        if (intercept_p >= MAXINTERCEPTS)
            interceptoverruns++;
        if (intercept_p == intercepts.Length)
        {
            intercept_t[] grown = NewIntercepts(intercepts.Length, intercepts.Length * 2);
            Array.Copy(intercepts, grown, intercepts.Length);
            intercepts = grown;
        }
        return intercepts[intercept_p++];
    }

    /// <summary>
    /// p_maputl.c <c>PIT_AddLineIntercepts</c>: adds the line to the
    /// intercepts when <see cref="trace"/> crosses it in front of its start.
    /// The side test switches from the line's to the trace's for traces over
    /// 16 units on an axis ("avoid precision problems with two routines").
    /// With <see cref="earlyout"/>, a one-sided line within the trace stops
    /// the search (returns false) without being added.
    /// </summary>
    public bool PIT_AddLineIntercepts(line_t ld)
    {
        int s1, s2;

        // avoid precision problems with two routines
        if (trace.dx > Fixed.FRACUNIT * 16
            || trace.dy > Fixed.FRACUNIT * 16
            || trace.dx < -Fixed.FRACUNIT * 16
            || trace.dy < -Fixed.FRACUNIT * 16)
        {
            s1 = P_PointOnDivlineSide(ld.v1.X, ld.v1.Y, trace);
            s2 = P_PointOnDivlineSide(ld.v2.X, ld.v2.Y, trace);
        }
        else
        {
            s1 = P_PointOnLineSide(trace.x, trace.y, ld);
            s2 = P_PointOnLineSide(trace.x + trace.dx, trace.y + trace.dy, ld);
        }

        if (s1 == s2)
            return true; // line isn't crossed

        // hit the line
        P_MakeDivline(ld, out divline_t dl);
        int frac = P_InterceptVector(trace, dl);

        if (frac < 0)
            return true; // behind source

        // try to early out the check
        if (earlyout && frac < Fixed.FRACUNIT && ld.backsector == null)
            return false; // stop checking

        intercept_t intercept = NextIntercept();
        intercept.frac = frac;
        intercept.isaline = true;
        intercept.line = ld;
        intercept.thing = null;
        return true; // continue
    }

    /// <summary>
    /// p_maputl.c <c>PIT_AddThingIntercepts</c>: adds the thing when
    /// <see cref="trace"/> crosses the diagonal of its box that is more
    /// across the trace, in front of the trace's start.
    /// </summary>
    public bool PIT_AddThingIntercepts(mobj_t thing)
    {
        int x1, y1, x2, y2;

        bool tracepositive = (trace.dx ^ trace.dy) > 0;

        // check a corner to corner crossection for hit
        if (tracepositive)
        {
            x1 = thing.x - thing.radius;
            y1 = thing.y + thing.radius;

            x2 = thing.x + thing.radius;
            y2 = thing.y - thing.radius;
        }
        else
        {
            x1 = thing.x - thing.radius;
            y1 = thing.y - thing.radius;

            x2 = thing.x + thing.radius;
            y2 = thing.y + thing.radius;
        }

        int s1 = P_PointOnDivlineSide(x1, y1, trace);
        int s2 = P_PointOnDivlineSide(x2, y2, trace);

        if (s1 == s2)
            return true; // line isn't crossed

        divline_t dl;
        dl.x = x1;
        dl.y = y1;
        dl.dx = x2 - x1;
        dl.dy = y2 - y1;

        int frac = P_InterceptVector(trace, dl);

        if (frac < 0)
            return true; // behind source

        intercept_t intercept = NextIntercept();
        intercept.frac = frac;
        intercept.isaline = false;
        intercept.line = null;
        intercept.thing = thing;
        return true; // keep going
    }

    /// <summary>
    /// p_maputl.c <c>P_TraverseIntercepts</c>: calls <paramref name="func"/>
    /// on the collected intercepts nearest first, up to
    /// <paramref name="maxfrac"/>, by repeated selection (equal fractions in
    /// collection order); each one visited gets <c>frac = MAXINT</c>. Returns
    /// false as soon as <paramref name="func"/> does.
    /// </summary>
    public bool P_TraverseIntercepts(traverser_t func, int maxfrac)
    {
        int count = intercept_p;
        intercept_t? @in = null; // shut up compiler warning

        while (count-- > 0)
        {
            int dist = int.MaxValue;
            for (int scan = 0; scan < intercept_p; scan++)
            {
                if (intercepts[scan].frac < dist)
                {
                    dist = intercepts[scan].frac;
                    @in = intercepts[scan];
                }
            }

            if (dist > maxfrac)
                return true; // checked everything in range
            if (@in == null)
                return true; // (vanilla would dereference NULL: maxfrac == MAXINT and every frac MAXINT)

            if (!func(@in))
                return false; // don't bother going farther

            @in.frac = int.MaxValue;
        }

        return true; // everything was traversed
    }

    /// <summary>
    /// p_maputl.c <c>P_PathTraverse</c>: traces from (<paramref name="x1"/>,
    /// <paramref name="y1"/>) to (<paramref name="x2"/>, <paramref name="y2"/>)
    /// block by block (at most 64 blocks), collecting the lines
    /// (<see cref="PT_ADDLINES"/>) and things (<see cref="PT_ADDTHINGS"/>) it
    /// crosses, then calls <paramref name="trav"/> on them nearest first
    /// (<see cref="P_TraverseIntercepts"/>). Returns false when
    /// <paramref name="trav"/> or the <see cref="PT_EARLYOUT"/> stopped it.
    /// A start exactly on a block boundary moves 1 unit up or right, as
    /// vanilla.
    /// </summary>
    public bool P_PathTraverse(int x1, int y1, int x2, int y2, int flags, traverser_t trav)
    {
        _pitAddLineIntercepts ??= PIT_AddLineIntercepts;
        _pitAddThingIntercepts ??= PIT_AddThingIntercepts;

        earlyout = (flags & PT_EARLYOUT) != 0;

        validcount++;
        intercept_p = 0;

        if (((x1 - bmaporgx) & (Blockmap.MAPBLOCKSIZE - 1)) == 0)
            x1 += Fixed.FRACUNIT; // don't side exactly on a line

        if (((y1 - bmaporgy) & (Blockmap.MAPBLOCKSIZE - 1)) == 0)
            y1 += Fixed.FRACUNIT; // don't side exactly on a line

        trace.x = x1;
        trace.y = y1;
        trace.dx = x2 - x1;
        trace.dy = y2 - y1;

        x1 -= bmaporgx;
        y1 -= bmaporgy;
        int xt1 = x1 >> Blockmap.MAPBLOCKSHIFT;
        int yt1 = y1 >> Blockmap.MAPBLOCKSHIFT;

        x2 -= bmaporgx;
        y2 -= bmaporgy;
        int xt2 = x2 >> Blockmap.MAPBLOCKSHIFT;
        int yt2 = y2 >> Blockmap.MAPBLOCKSHIFT;

        int mapxstep, mapystep, partial, xstep, ystep;

        if (xt2 > xt1)
        {
            mapxstep = 1;
            partial = Fixed.FRACUNIT - ((x1 >> MAPBTOFRAC) & (Fixed.FRACUNIT - 1));
            ystep = Fixed.FixedDiv(y2 - y1, abs(x2 - x1));
        }
        else if (xt2 < xt1)
        {
            mapxstep = -1;
            partial = (x1 >> MAPBTOFRAC) & (Fixed.FRACUNIT - 1);
            ystep = Fixed.FixedDiv(y2 - y1, abs(x2 - x1));
        }
        else
        {
            mapxstep = 0;
            partial = Fixed.FRACUNIT;
            ystep = 256 * Fixed.FRACUNIT;
        }

        int yintercept = (y1 >> MAPBTOFRAC) + Fixed.FixedMul(partial, ystep);

        if (yt2 > yt1)
        {
            mapystep = 1;
            partial = Fixed.FRACUNIT - ((y1 >> MAPBTOFRAC) & (Fixed.FRACUNIT - 1));
            xstep = Fixed.FixedDiv(x2 - x1, abs(y2 - y1));
        }
        else if (yt2 < yt1)
        {
            mapystep = -1;
            partial = (y1 >> MAPBTOFRAC) & (Fixed.FRACUNIT - 1);
            xstep = Fixed.FixedDiv(x2 - x1, abs(y2 - y1));
        }
        else
        {
            mapystep = 0;
            partial = Fixed.FRACUNIT;
            xstep = 256 * Fixed.FRACUNIT;
        }
        int xintercept = (x1 >> MAPBTOFRAC) + Fixed.FixedMul(partial, xstep);

        // Step through map blocks.
        // Count is present to prevent a round off error
        // from skipping the break.
        int mapx = xt1;
        int mapy = yt1;

        for (int count = 0; count < 64; count++)
        {
            if ((flags & PT_ADDLINES) != 0)
            {
                if (!P_BlockLinesIterator(mapx, mapy, _pitAddLineIntercepts))
                    return false; // early out
            }

            if ((flags & PT_ADDTHINGS) != 0)
            {
                if (!P_BlockThingsIterator(mapx, mapy, _pitAddThingIntercepts))
                    return false; // early out
            }

            if (mapx == xt2 && mapy == yt2)
                break;

            if ((yintercept >> Fixed.FRACBITS) == mapy)
            {
                yintercept += ystep;
                mapx += mapxstep;
            }
            else if ((xintercept >> Fixed.FRACBITS) == mapx)
            {
                xintercept += xstep;
                mapy += mapystep;
            }
        }

        // go through the sorted list
        return P_TraverseIntercepts(trav, Fixed.FRACUNIT);
    }
}

/// <summary>
/// p_local.h <c>divline_t</c>: a line as a point and a direction (fixed_t),
/// for the side and intercept tests.
/// </summary>
public struct divline_t
{
    public int x;
    public int y;
    public int dx;
    public int dy;

    public divline_t(int x, int y, int dx, int dy)
    {
        this.x = x;
        this.y = y;
        this.dx = dx;
        this.dy = dy;
    }

    public override readonly string ToString() => $"({x >> Fixed.FRACBITS}, {y >> Fixed.FRACBITS}) + ({dx >> Fixed.FRACBITS}, {dy >> Fixed.FRACBITS})";
}

/// <summary>
/// p_local.h <c>intercept_t</c>: a line or thing a trace crosses, at
/// <see cref="frac"/> along it (fixed_t, 0 at the start, <see cref="Fixed.FRACUNIT"/>
/// at the end). Vanilla's union <c>d</c> is <see cref="line"/> or
/// <see cref="thing"/>, by <see cref="isaline"/>. The world reuses these
/// objects between traversals: a traverser must not keep them.
/// </summary>
public sealed class intercept_t
{
    public int frac;
    public bool isaline;
    /// <summary><c>d.line</c> when <see cref="isaline"/>.</summary>
    public line_t? line;
    /// <summary><c>d.thing</c> when not <see cref="isaline"/>.</summary>
    public mobj_t? thing;

    public override string ToString() => $"{(isaline ? line!.ToString() : thing!.ToString())} at {frac}";
}

/// <summary>p_local.h <c>traverser_t</c>: called by <see cref="World.P_TraverseIntercepts"/>; false stops the traversal.</summary>
public delegate bool traverser_t(intercept_t @in);
