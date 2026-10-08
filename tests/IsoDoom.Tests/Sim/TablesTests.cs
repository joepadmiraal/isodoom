using System;
using Xunit;
using static IsoDoom.Map.Fixed;
using static IsoDoom.Sim.Tables;

namespace IsoDoom.Tests.Sim;

/// <summary>T4.1: tables.c, tables.h and r_main.c <c>R_PointToAngle2</c>.</summary>
public class TablesTests
{
    // A 64-bit polynomial hash of a table (entries as 32-bit unsigned), worked
    // out over linuxdoom-1.10's tables.c, so any edit to the data shows.
    private static ulong Hash(ReadOnlySpan<int> table)
    {
        ulong h = 0;
        foreach (int e in table)
            h = unchecked(h * 31 + (uint)e);
        return h;
    }

    private static ulong Hash(ReadOnlySpan<uint> table)
    {
        ulong h = 0;
        foreach (uint e in table)
            h = unchecked(h * 31 + e);
        return h;
    }

    [Fact]
    public void Lengths_match_tables_h()
    {
        Assert.Equal(FINEANGLES / 2, finetangent.Length);
        Assert.Equal(5 * FINEANGLES / 4, finesine.Length);
        Assert.Equal(10240, finesine.Length);
        Assert.Equal(FINEANGLES, finecosine.Length);
        Assert.Equal(SLOPERANGE + 1, tantoangle.Length);
    }

    [Fact]
    public void Whole_tables_match_linuxdoom()
    {
        Assert.Equal(0xb996ad97bf22fe60UL, Hash(finetangent));
        Assert.Equal(0xee04190e3e10edcfUL, Hash(finesine));
        Assert.Equal(0x8d46013ae5654f16UL, Hash(tantoangle));
    }

    [Theory]
    [InlineData(0, -170910304)]
    [InlineData(1, -56965752)]
    [InlineData(1024, -65485)]
    [InlineData(2047, -25)]
    [InlineData(2048, 25)]
    [InlineData(3072, 65586)]
    [InlineData(4095, 170910304)]
    public void Finetangent_spot_checks(int i, int value) => Assert.Equal(value, finetangent[i]);

    [Fact]
    public void Finetangent_is_antisymmetric()
    {
        for (int i = 0; i < FINEANGLES / 2; i++)
            Assert.Equal(-finetangent[FINEANGLES / 2 - 1 - i], finetangent[i]);
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(1, 75)]
    [InlineData(1024, 46358)]
    [InlineData(2047, 65535)]
    [InlineData(2048, 65535)]
    [InlineData(4095, 25)]
    [InlineData(4096, -25)]
    [InlineData(6144, -65535)]
    [InlineData(8191, -25)]
    [InlineData(8192, 25)]
    [InlineData(10239, 65535)]
    public void Finesine_spot_checks(int i, int value) => Assert.Equal(value, finesine[i]);

    [Fact]
    public void Finecosine_is_finesine_a_quarter_turn_on()
    {
        for (int i = 0; i < FINEANGLES; i++)
            Assert.Equal(finesine[i + FINEANGLES / 4], finecosine[i]);
        Assert.Equal(65535, finecosine[0]);
        Assert.Equal(-65535, finecosine[FINEANGLES / 2]);
    }

    [Fact]
    public void Finesine_fifth_quarter_repeats_the_first_but_for_vanillas_rounding()
    {
        // tables.c was generated in floating point: 12 entries of the copy
        // differ by 1 from the first quarter. Kept as they are (vanilla).
        int differ = 0;
        for (int i = 0; i < FINEANGLES / 4; i++)
        {
            int d = finesine[i + FINEANGLES] - finesine[i];
            Assert.InRange(d, -1, 1);
            if (d != 0)
                differ++;
        }
        Assert.Equal(12, differ);
        Assert.Equal(3541, finesine[70]);
        Assert.Equal(3542, finesine[FINEANGLES + 70]);
    }

    [Theory]
    [InlineData(0, 0u)]
    [InlineData(1, 333772u)]
    [InlineData(512, 167458912u)]
    [InlineData(1024, 316933408u)]
    [InlineData(2047, 536704000u)]
    [InlineData(2048, 536870912u)] // ANG45
    public void Tantoangle_spot_checks(int i, uint value) => Assert.Equal(value, tantoangle[i]);

    [Fact]
    public void Tantoangle_ends_at_ang45_and_rises()
    {
        Assert.Equal(ANG45, tantoangle[SLOPERANGE]);
        for (int i = 1; i <= SLOPERANGE; i++)
            Assert.True(tantoangle[i] > tantoangle[i - 1]);
    }

    [Fact]
    public void Angle_constants_match_tables_h()
    {
        Assert.Equal(0x20000000u, ANG45);
        Assert.Equal(0x40000000u, ANG90);
        Assert.Equal(0x80000000u, ANG180);
        Assert.Equal(0xc0000000u, ANG270);
        Assert.Equal(11930464u, ANG1);
        Assert.Equal(5, DBITS);
        Assert.Equal(FINEANGLES - 1, (int)(ANG_MAX >> ANGLETOFINESHIFT));
        Assert.Equal(FINEANGLES / 8, (int)(ANG45 >> ANGLETOFINESHIFT));
    }

    [Theory]
    [InlineData(0u, 511u, SLOPERANGE)] // den < 512
    [InlineData(0u, 512u, 0)]
    [InlineData(65536u, 65536u, SLOPERANGE)]
    [InlineData(32768u, 65536u, 1024)]
    [InlineData(65536u, 32768u, SLOPERANGE)] // slope over 1: clamped
    [InlineData(0x40000000u, 0x40000000u, 0)] // num << 3 wraps to 0, as in C
    public void SlopeDiv_matches_tables_c(uint num, uint den, int expected) => Assert.Equal(expected, SlopeDiv(num, den));

    [Theory]
    // On the axes and diagonals: vanilla's "- 1" in octants 1, 3 and 5 shows.
    [InlineData(1, 0, 0u)]
    [InlineData(1, 1, ANG45 - 1)]
    [InlineData(0, 1, ANG90 - 1)]
    [InlineData(-1, 1, ANG90 + ANG45)]
    [InlineData(-1, 0, ANG180 - 1)]
    [InlineData(-1, -1, ANG180 + ANG45 - 1)]
    [InlineData(0, -1, ANG270)]
    [InlineData(1, -1, ANG270 + ANG45)]
    // One point inside each octant (worked out with a port of r_main.c over
    // linuxdoom's tantoangle).
    [InlineData(3, 1, 0x0d18ec80u)] // octant 0
    [InlineData(1, 3, 0x32e7137fu)] // octant 1
    [InlineData(-1, 3, 0x4d18ec80u)] // octant 2
    [InlineData(-3, 1, 0x72e7137fu)] // octant 3
    [InlineData(-3, -1, 0x8d18ec80u)] // octant 4
    [InlineData(-1, -3, 0xb2e7137fu)] // octant 5
    [InlineData(1, -3, 0xcd18ec80u)] // octant 7
    [InlineData(3, -1, 0xf2e71380u)] // octant 8
    public void R_PointToAngle2_in_every_octant(int dx, int dy, uint expected)
    {
        // The same from any origin.
        Assert.Equal(expected, R_PointToAngle2(0, 0, dx * FRACUNIT, dy * FRACUNIT));
        int x1 = 1056 * FRACUNIT, y1 = -3616 * FRACUNIT;
        Assert.Equal(expected, R_PointToAngle2(x1, y1, x1 + dx * 100 * FRACUNIT, y1 + dy * 100 * FRACUNIT));
    }

    [Fact]
    public void R_PointToAngle2_same_point_is_zero() => Assert.Equal(0u, R_PointToAngle2(5, 7, 5, 7));

    [Fact]
    public void R_PointToAngle2_follows_atan2_all_round()
    {
        // Every half degree on a circle of 1000 units: within tantoangle's
        // resolution (a slope step of 1/2048 is at most 0.028°).
        for (int k = 0; k < 720; k++)
        {
            double a = k * Math.PI / 360;
            int x = (int)Math.Round(Math.Cos(a) * 1000 * FRACUNIT);
            int y = (int)Math.Round(Math.Sin(a) * 1000 * FRACUNIT);
            uint got = R_PointToAngle2(0, 0, x, y);
            double expected = Math.Atan2(y, x) / (2 * Math.PI) * 4294967296.0;
            double diff = got - (expected < 0 ? expected + 4294967296.0 : expected);
            if (diff > 2147483648.0)
                diff -= 4294967296.0;
            else if (diff < -2147483648.0)
                diff += 4294967296.0;
            Assert.True(Math.Abs(diff) < 0.03 / 360 * 4294967296.0, $"{k / 2.0}°: {got:x8}");
        }
    }

    [Fact]
    public void R_PointToAngle2_gives_45_degrees_below_512_fracunits()
    {
        // SlopeDiv's den < 512: points less than 1/128 unit apart on the long
        // axis all read as the octant's 45° edge, as in vanilla, even on the
        // x axis.
        Assert.Equal(ANG45, R_PointToAngle2(0, 0, 511, 0));
        Assert.Equal(ANG45, R_PointToAngle2(0, 0, 511, 1));
        Assert.Equal(0u, R_PointToAngle2(0, 0, 512, 0));
    }
}
