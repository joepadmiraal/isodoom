using IsoDoom.Map;

namespace IsoDoom.Sim;

// p_sight.c (all of it): line of sight between two mobjs, through the REJECT
// table and then the BSP (T6.2).
public sealed partial class World
{
    // ---- p_sight.c globals ----

    /// <summary>p_sight.c <c>sightzstart</c>: the eye height (fixed_t) of the looker.</summary>
    public int sightzstart;

    /// <summary>p_sight.c <c>topslope</c>: the slope to the top of the target, lowered by each opening's top.</summary>
    public int topslope;

    /// <summary>p_sight.c <c>bottomslope</c>: the slope to the bottom of the target, raised by each opening's bottom.</summary>
    public int bottomslope;

    /// <summary>p_sight.c <c>strace</c>: from the looker to the target.</summary>
    public divline_t strace;

    /// <summary>p_sight.c <c>t2x</c>: the target's x (fixed_t).</summary>
    public int t2x;

    /// <summary>p_sight.c <c>t2y</c>: the target's y (fixed_t).</summary>
    public int t2y;

    /// <summary>
    /// p_sight.c <c>sightcounts</c>: [0] the checks the REJECT table
    /// answered, [1] those that traced the BSP. Statistics only (vanilla
    /// never reads them; the tests do).
    /// </summary>
    public readonly int[] sightcounts = new int[2];

    /// <summary>
    /// p_sight.c <c>P_DivlineSide</c>: 0 when the point is on the front
    /// (right) side of <paramref name="node"/>, 1 on the back, 2 on the line.
    /// Coarser than <see cref="P_PointOnDivlineSide"/> (whole map units).
    /// Keeps vanilla's bug in the horizontal case, which compares
    /// <paramref name="x"/> with the line's y for "on the line".
    /// </summary>
    public static int P_DivlineSide(int x, int y, in divline_t node)
    {
        if (node.dx == 0)
        {
            if (x == node.x)
                return 2;
            if (x <= node.x)
                return node.dy > 0 ? 1 : 0;
            return node.dy < 0 ? 1 : 0;
        }

        if (node.dy == 0)
        {
            if (x == node.y) // sic: vanilla compares x with the line's y
                return 2;
            if (y <= node.y)
                return node.dx < 0 ? 1 : 0;
            return node.dx > 0 ? 1 : 0;
        }

        int dx = x - node.x;
        int dy = y - node.y;

        // Plain int products, wrapping on overflow as vanilla's do in practice.
        int left = unchecked((node.dy >> Fixed.FRACBITS) * (dx >> Fixed.FRACBITS));
        int right = unchecked((dy >> Fixed.FRACBITS) * (node.dx >> Fixed.FRACBITS));

        if (right < left)
            return 0; // front side
        if (left == right)
            return 2;
        return 1; // back side
    }

    /// <summary>The partition line of a BSP node as a divline (vanilla casts <c>node_t*</c> to <c>divline_t*</c>).</summary>
    private static divline_t NodeDivline(Node node) => new(node.X, node.Y, node.Dx, node.Dy);

    /// <summary>
    /// p_sight.c <c>P_InterceptVector2</c>: as <see cref="P_InterceptVector"/>
    /// (the fractional position along <paramref name="v2"/> where it crosses
    /// <paramref name="v1"/>, 0 for parallel lines); vanilla's copy for the
    /// sight code.
    /// </summary>
    public static int P_InterceptVector2(in divline_t v2, in divline_t v1)
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
    /// p_sight.c <c>P_CrossSubsector</c>: false when a line of subsector
    /// <paramref name="num"/> that <see cref="strace"/> crosses blocks the
    /// sight: a one-sided line, a closed opening, or openings that narrow
    /// the slopes to nothing (each two-sided line with different heights
    /// narrows <see cref="topslope"/>/<see cref="bottomslope"/>).
    /// </summary>
    public bool P_CrossSubsector(int num)
    {
        Subsector sub = level.Subsectors[num];
        Seg[] segs = level.Segs;

        // check lines
        int end = sub.FirstLine + sub.NumLines;
        for (int i = sub.FirstLine; i < end; i++)
        {
            Seg seg = segs[i];
            line_t line = lines[seg.LineDef.Index];

            // allready checked other side?
            if (line.validcount == validcount)
                continue;

            line.validcount = validcount;

            Vertex v1 = line.v1;
            Vertex v2 = line.v2;
            int s1 = P_DivlineSide(v1.X, v1.Y, strace);
            int s2 = P_DivlineSide(v2.X, v2.Y, strace);

            // line isn't crossed?
            if (s1 == s2)
                continue;

            var divl = new divline_t(v1.X, v1.Y, v2.X - v1.X, v2.Y - v1.Y);
            s1 = P_DivlineSide(strace.x, strace.y, divl);
            s2 = P_DivlineSide(t2x, t2y, divl);

            // line isn't crossed?
            if (s1 == s2)
                continue;

            // stop because it is not two sided anyway
            // might do this after updating validcount?
            if ((line.flags & Line.ML_TWOSIDED) == 0)
                return false;

            // crosses a two sided line
            // Not vanilla: a two-sided line without a back side (a broken map)
            // has no back sector here (vanilla reads garbage); it blocks, as a
            // one-sided line does (SPEC §12 T6.2).
            if (seg.BackSector is null)
                return false;
            sector_t front = sectors[seg.FrontSector.Index];
            sector_t back = sectors[seg.BackSector.Index];

            // no wall to block sight with?
            if (front.floorheight == back.floorheight
                && front.ceilingheight == back.ceilingheight)
                continue;

            // possible occluder
            // because of ceiling height differences
            int opentop = front.ceilingheight < back.ceilingheight ? front.ceilingheight : back.ceilingheight;

            // because of ceiling height differences
            int openbottom = front.floorheight > back.floorheight ? front.floorheight : back.floorheight;

            // quick test for totally closed doors
            if (openbottom >= opentop)
                return false; // stop

            int frac = P_InterceptVector2(strace, divl);

            if (front.floorheight != back.floorheight)
            {
                int slope = Fixed.FixedDiv(openbottom - sightzstart, frac);
                if (slope > bottomslope)
                    bottomslope = slope;
            }

            if (front.ceilingheight != back.ceilingheight)
            {
                int slope = Fixed.FixedDiv(opentop - sightzstart, frac);
                if (slope < topslope)
                    topslope = slope;
            }

            if (topslope <= bottomslope)
                return false; // stop
        }
        // passed the subsector ok
        return true;
    }

    /// <summary>
    /// p_sight.c <c>P_CrossBSPNode</c>: true when <see cref="strace"/>
    /// crosses the subtree <paramref name="bspnum"/> unblocked, visiting its
    /// subsectors from the looker's side to the target's.
    /// </summary>
    public bool P_CrossBSPNode(int bspnum)
    {
        if ((bspnum & Node.NF_SUBSECTOR) != 0)
        {
            if (bspnum == -1)
                return P_CrossSubsector(0);
            return P_CrossSubsector(bspnum & ~Node.NF_SUBSECTOR);
        }

        Node bsp = level.Nodes[bspnum];
        divline_t partition = NodeDivline(bsp);

        // decide which side the start point is on
        int side = P_DivlineSide(strace.x, strace.y, partition);
        if (side == 2)
            side = 0; // an "on" should cross both sides

        // cross the starting side
        if (!P_CrossBSPNode(bsp.Children[side]))
            return false;

        // the partition plane is crossed here
        if (side == P_DivlineSide(t2x, t2y, partition))
        {
            // the line doesn't touch the other side
            return true;
        }

        // cross the ending side
        return P_CrossBSPNode(bsp.Children[side ^ 1]);
    }

    /// <summary>
    /// p_sight.c <c>P_CheckSight</c>: true when a straight line can be drawn
    /// from <paramref name="t1"/>'s eyes (3/4 of its height) to any part of
    /// <paramref name="t2"/>. The REJECT table answers first; then the BSP is
    /// traced (<see cref="P_CrossBSPNode"/>). Uses <see cref="validcount"/>.
    /// </summary>
    public bool P_CheckSight(mobj_t t1, mobj_t t2)
    {
        // First check for trivial rejection.

        // Determine subsector entries in REJECT table.
        int s1 = t1.subsector.sector.Index;
        int s2 = t2.subsector.sector.Index;

        // Check in REJECT table.
        if (level.Reject.IsRejected(s1, s2, sectors.Length))
        {
            sightcounts[0]++;

            // can't possibly be connected
            return false;
        }

        // An unobstructed LOS is possible.
        // Now look from eyes of t1 to any part of t2.
        sightcounts[1]++;

        validcount++;

        sightzstart = t1.z + t1.height - (t1.height >> 2);
        topslope = (t2.z + t2.height) - sightzstart;
        bottomslope = t2.z - sightzstart;

        strace.x = t1.x;
        strace.y = t1.y;
        t2x = t2.x;
        t2y = t2.y;
        strace.dx = t2.x - t1.x;
        strace.dy = t2.y - t1.y;

        // the head node is the last node output
        return P_CrossBSPNode(level.Nodes.Length - 1);
    }
}
