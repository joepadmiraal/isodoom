using IsoDoom.Map;
using Xunit;
using static IsoDoom.Map.Fixed;

namespace IsoDoom.Tests.Map;

/// <summary>m_fixed.c and m_bbox.c ports.</summary>
public class FixedTests
{
    [Fact]
    public void FixedMulAndDiv()
    {
        Assert.Equal(6 * FRACUNIT, FixedMul(2 * FRACUNIT, 3 * FRACUNIT));
        Assert.Equal(-FRACUNIT / 2, FixedMul(-FRACUNIT, FRACUNIT / 2));
        Assert.Equal(FRACUNIT / 4, FixedDiv(FRACUNIT, 4 * FRACUNIT));
        Assert.Equal(-3 * FRACUNIT, FixedDiv(6 * FRACUNIT, -2 * FRACUNIT));
        // Overflow saturates by the sign of the result.
        Assert.Equal(int.MaxValue, FixedDiv(1 << 30, 1));
        Assert.Equal(int.MinValue, FixedDiv(-(1 << 30), 1));
        // C abs(INT_MIN) is negative, so the overflow check misses it and the result wraps, as in vanilla.
        Assert.Equal(0, FixedDiv(int.MinValue, 1));
    }

    [Fact]
    public void AddToBoxKeepsVanillasElseIf()
    {
        int[] box = new int[4];
        BBox.M_ClearBox(box);
        BBox.M_AddToBox(box, 5, 7);
        // The first point only sets left and bottom: right and top stay cleared.
        Assert.Equal(new[] { int.MinValue, 7, 5, int.MinValue }, box);
        BBox.M_AddToBox(box, 9, 8);
        BBox.M_AddToBox(box, 1, 2);
        Assert.Equal(new[] { 8, 2, 1, 9 }, box);
    }
}
