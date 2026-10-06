using IsoDoom.Map;
using Xunit;
using static IsoDoom.Map.Fixed;

namespace IsoDoom.Tests.Map;

/// <summary>T2.5: the CPU reference of the level shader's texel lookup.</summary>
public class TextureWrapTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(64, 63)]
    [InlineData(72, 63)]
    [InlineData(128, 127)]
    [InlineData(255, 127)]
    [InlineData(256, 255)]
    public void TextureWidthMaskIsTheLargestPowerOfTwoNotAboveTheWidth(int width, int mask) =>
        Assert.Equal(mask, TextureWrap.TextureWidthMask(width));

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(63, 127, 63, 127)]
    [InlineData(64, 128, 0, 0)] // wraps at the texture size
    [InlineData(-1, -1, 63, 127)] // negative coordinates wrap downwards
    [InlineData(130, 300, 2, 44)]
    public void TilingAgreesForPowerOfTwoTexturesOf128Rows(int column, int row, int wantColumn, int wantRow)
    {
        Assert.Equal((wantColumn, wantRow), TextureWrap.WallTexel(column, row, 64, 128, WallTextureTiling.Vanilla));
        Assert.Equal((wantColumn, wantRow), TextureWrap.WallTexel(column, row, 64, 128, WallTextureTiling.TextureSize));
    }

    [Fact]
    public void VanillaWrapsColumnsAtTheWidthMask()
    {
        // 72 wide: vanilla repeats columns 0-63 only; the real size repeats all 72.
        Assert.Equal(0, TextureWrap.WallTexel(64, 0, 72, 128, WallTextureTiling.Vanilla).Column);
        Assert.Equal(64, TextureWrap.WallTexel(64, 0, 72, 128, WallTextureTiling.TextureSize).Column);
        Assert.Equal(0, TextureWrap.WallTexel(72, 0, 72, 128, WallTextureTiling.TextureSize).Column);
    }

    [Fact]
    public void VanillaRowsWrapAt128AndReadOnPastShortColumns()
    {
        // DOOR3-like: 64 x 72. Rows 0-71 are the texture, 72-127 tutti-frutti, 128 wraps to 0.
        Assert.Equal((5, 71), TextureWrap.WallTexel(5, 71, 64, 72, WallTextureTiling.Vanilla));
        Assert.Equal((6, 0), TextureWrap.WallTexel(5, 72, 64, 72, WallTextureTiling.Vanilla)); // the next column's top
        Assert.Equal((6, 55), TextureWrap.WallTexel(5, 127, 64, 72, WallTextureTiling.Vanilla));
        Assert.Equal((5, 0), TextureWrap.WallTexel(5, 128, 64, 72, WallTextureTiling.Vanilla));
        Assert.Equal((0, 10), TextureWrap.WallTexel(63, 82, 64, 72, WallTextureTiling.Vanilla)); // past the last column: column 0
        // Tiled by the real height instead.
        Assert.Equal((5, 0), TextureWrap.WallTexel(5, 72, 64, 72, WallTextureTiling.TextureSize));
        Assert.Equal((5, 56), TextureWrap.WallTexel(5, 128, 64, 72, WallTextureTiling.TextureSize));
        Assert.Equal((5, 71), TextureWrap.WallTexel(5, -1, 64, 72, WallTextureTiling.TextureSize));
    }

    [Fact]
    public void VanillaShowsOnly128RowsOfTallTextures() =>
        Assert.Equal((0, 0), TextureWrap.WallTexel(0, 128, 64, 256, WallTextureTiling.Vanilla));

    [Fact]
    public void FlatsAreAlignedToTheWorldGridWithNorthUp()
    {
        Assert.Equal((0, 0), TextureWrap.FlatTexel(0, 0));
        Assert.Equal((1, 63), TextureWrap.FlatTexel(1 << FRACBITS, FRACUNIT / 2)); // y just above 0 is the last row
        Assert.Equal((63, 1), TextureWrap.FlatTexel(-1, -(1 << FRACBITS) - 1));
        Assert.Equal((32, 32), TextureWrap.FlatTexel(96 << FRACBITS, -(32 << FRACBITS)));
    }
}
