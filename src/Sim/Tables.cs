using System;
using static IsoDoom.Map.Fixed;

namespace IsoDoom.Sim;

/// <summary>
/// Lookup tables and BAM angles (tables.h, tables.c), plus r_main.c
/// <c>R_PointToAngle2</c>, which the play code uses for every angle between
/// two points. An <c>angle_t</c> is a <see cref="uint"/>: a full turn is 2^32,
/// so angle arithmetic wraps as in C (unsigned overflow). The table data is in
/// Tables.Data.cs.
/// </summary>
public static partial class Tables
{
    // tables.h

    /// <summary>tables.h <c>FINEANGLES</c>: fine angles in a full turn.</summary>
    public const int FINEANGLES = 8192;

    /// <summary>tables.h <c>FINEMASK</c>.</summary>
    public const int FINEMASK = FINEANGLES - 1;

    /// <summary>tables.h <c>ANGLETOFINESHIFT</c>: <c>angle &gt;&gt; ANGLETOFINESHIFT</c> is a fine angle (0 to 8191).</summary>
    public const int ANGLETOFINESHIFT = 19;

    /// <summary>tables.h <c>ANG45</c> (BAM).</summary>
    public const uint ANG45 = 0x20000000;

    /// <summary>tables.h <c>ANG90</c> (BAM).</summary>
    public const uint ANG90 = 0x40000000;

    /// <summary>tables.h <c>ANG180</c> (BAM).</summary>
    public const uint ANG180 = 0x80000000;

    /// <summary>tables.h <c>ANG270</c> (BAM).</summary>
    public const uint ANG270 = 0xc0000000;

    /// <summary>Chocolate Doom's tables.h <c>ANG_MAX</c> (BAM; linuxdoom writes <c>-1</c> or <c>0xffffffff</c>).</summary>
    public const uint ANG_MAX = 0xffffffff;

    /// <summary>Chocolate Doom's tables.h <c>ANG1</c> (BAM; <c>ANG45 / 45</c>, truncated).</summary>
    public const uint ANG1 = ANG45 / 45;

    /// <summary>tables.h <c>SLOPERANGE</c>: <see cref="tantoangle"/> has <c>SLOPERANGE + 1</c> entries.</summary>
    public const int SLOPERANGE = 2048;

    /// <summary>tables.h <c>SLOPEBITS</c>.</summary>
    public const int SLOPEBITS = 11;

    /// <summary>tables.h <c>DBITS</c>.</summary>
    public const int DBITS = FRACBITS - SLOPEBITS;

    /// <summary>
    /// tables.c <c>finecosine</c>: vanilla's pointer
    /// <c>&amp;finesine[FINEANGLES/4]</c>, here a view of
    /// <see cref="finesine"/> from that entry (8192 entries).
    /// </summary>
    public static ReadOnlySpan<int> finecosine => finesine[(FINEANGLES / 4)..];

    /// <summary>
    /// tables.c <c>SlopeDiv</c>: the <see cref="tantoangle"/> index of the
    /// slope <c>num / den</c> (both unsigned, <c>num &lt;= den</c> in use),
    /// <see cref="SLOPERANGE"/> when <c>den</c> is under 512 or the slope over 1.
    /// </summary>
    public static int SlopeDiv(uint num, uint den)
    {
        if (den < 512)
            return SLOPERANGE;
        uint ans = unchecked(num << 3) / (den >> 8);
        return ans <= SLOPERANGE ? (int)ans : SLOPERANGE;
    }

    // r_main.c

    /// <summary>
    /// r_main.c <c>R_PointToAngle2</c>: the BAM angle from (x1, y1) to
    /// (x2, y2), by octant through <see cref="tantoangle"/>; 0 for the same
    /// point. Vanilla sets the renderer's <c>viewx</c>/<c>viewy</c> to (x1, y1)
    /// and calls <c>R_PointToAngle</c>; this is that function on the
    /// difference, without the side effect. The subtraction and negations wrap
    /// as in C (two's complement), so far-apart points give vanilla's angles.
    /// </summary>
    public static uint R_PointToAngle2(int x1, int y1, int x2, int y2)
    {
        unchecked
        {
            int x = x2 - x1;
            int y = y2 - y1;

            if (x == 0 && y == 0)
                return 0;

            if (x >= 0)
            {
                if (y >= 0)
                {
                    if (x > y)
                        return tantoangle[SlopeDiv((uint)y, (uint)x)]; // octant 0
                    return ANG90 - 1 - tantoangle[SlopeDiv((uint)x, (uint)y)]; // octant 1
                }
                y = -y;
                if (x > y)
                    return (uint)-(int)tantoangle[SlopeDiv((uint)y, (uint)x)]; // octant 8
                return ANG270 + tantoangle[SlopeDiv((uint)x, (uint)y)]; // octant 7
            }

            x = -x;
            if (y >= 0)
            {
                if (x > y)
                    return ANG180 - 1 - tantoangle[SlopeDiv((uint)y, (uint)x)]; // octant 3
                return ANG90 + tantoangle[SlopeDiv((uint)x, (uint)y)]; // octant 2
            }
            y = -y;
            if (x > y)
                return ANG180 + tantoangle[SlopeDiv((uint)y, (uint)x)]; // octant 4
            return ANG270 - 1 - tantoangle[SlopeDiv((uint)x, (uint)y)]; // octant 5
        }
    }
}
