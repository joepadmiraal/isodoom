namespace IsoDoom.Map;

/// <summary>
/// 16.16 fixed-point arithmetic (m_fixed.h, m_fixed.c). A <c>fixed_t</c> is an
/// <see cref="int"/>; map coordinates are whole map units shifted left by
/// <see cref="FRACBITS"/>.
/// </summary>
public static class Fixed
{
    /// <summary>m_fixed.h <c>FRACBITS</c>.</summary>
    public const int FRACBITS = 16;

    /// <summary>m_fixed.h <c>FRACUNIT</c>: 1.0.</summary>
    public const int FRACUNIT = 1 << FRACBITS;

    /// <summary>m_fixed.c <c>FixedMul</c> (64-bit product, as Chocolate Doom).</summary>
    public static int FixedMul(int a, int b) => (int)(((long)a * b) >> FRACBITS);

    /// <summary>
    /// m_fixed.c <c>FixedDiv</c>: <see cref="int.MaxValue"/> or
    /// <see cref="int.MinValue"/> (by the sign of the result) when it would
    /// overflow, else <c>FixedDiv2</c> (64-bit, as Chocolate Doom).
    /// </summary>
    public static int FixedDiv(int a, int b)
    {
        if ((Abs(a) >> 14) >= Abs(b))
            return (a ^ b) < 0 ? int.MinValue : int.MaxValue;
        return (int)(((long)a << FRACBITS) / b);
    }

    // C's abs(): abs(INT_MIN) stays INT_MIN.
    private static int Abs(int v) => v < 0 ? unchecked(-v) : v;
}

/// <summary>Bounding boxes (m_bbox.h, m_bbox.c): <c>fixed_t[4]</c> indexed by the <c>BOX*</c> constants.</summary>
public static class BBox
{
    public const int BOXTOP = 0;
    public const int BOXBOTTOM = 1;
    public const int BOXLEFT = 2;
    public const int BOXRIGHT = 3;

    /// <summary>m_bbox.c <c>M_ClearBox</c>.</summary>
    public static void M_ClearBox(int[] box)
    {
        box[BOXTOP] = box[BOXRIGHT] = int.MinValue;
        box[BOXBOTTOM] = box[BOXLEFT] = int.MaxValue;
    }

    /// <summary>
    /// m_bbox.c <c>M_AddToBox</c>, with its <c>else if</c>: the first point added
    /// to a cleared box only sets its left and bottom edges.
    /// </summary>
    public static void M_AddToBox(int[] box, int x, int y)
    {
        if (x < box[BOXLEFT])
            box[BOXLEFT] = x;
        else if (x > box[BOXRIGHT])
            box[BOXRIGHT] = x;
        if (y < box[BOXBOTTOM])
            box[BOXBOTTOM] = y;
        else if (y > box[BOXTOP])
            box[BOXTOP] = y;
    }
}
