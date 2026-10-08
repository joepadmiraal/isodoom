using System.Collections.Generic;
using IsoDoom.Map;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;
using static IsoDoom.Map.Fixed;

namespace IsoDoom.Tests.Map;

/// <summary>
/// T2.8: the ported light tables (r_main.c) and the wall and floor colormap
/// choice (r_segs.c, r_plane.c). Expected entries come from linuxdoom-1.10's
/// <c>R_InitLightTables</c> and <c>R_ExecuteSetViewSize</c> loops compiled
/// as C (with its <c>FixedDiv</c>/<c>FixedDiv2</c>).
/// </summary>
public class LightTablesTests
{
    private static readonly LightTables _full = LightTables.R_InitLightTables();

    [Theory]
    [InlineData(0, 0, 0)] // startmap 60, scale 160: clamped to 0
    [InlineData(0, 1, 20)]
    [InlineData(0, 2, 31)]
    [InlineData(5, 7, 30)]
    [InlineData(7, 10, 25)]
    [InlineData(8, 15, 23)]
    [InlineData(8, 127, 28)]
    [InlineData(9, 79, 23)]
    [InlineData(9, 80, 24)]
    [InlineData(10, 4, 4)]
    [InlineData(10, 127, 20)]
    [InlineData(12, 6, 1)]
    [InlineData(13, 9, 0)]
    [InlineData(13, 10, 1)]
    [InlineData(14, 20, 1)]
    [InlineData(14, 127, 4)]
    [InlineData(15, 0, 0)]
    [InlineData(15, 127, 0)]
    public void ZlightMatchesVanilla(int light, int z, int colormap) => Assert.Equal(colormap, _full.zlight(light, z));

    [Theory]
    [InlineData(0, 0, 31)]
    [InlineData(0, 47, 31)]
    [InlineData(2, 44, 30)]
    [InlineData(2, 47, 29)]
    [InlineData(7, 3, 31)]
    [InlineData(7, 4, 30)]
    [InlineData(8, 0, 28)]
    [InlineData(8, 10, 23)]
    [InlineData(9, 47, 1)]
    [InlineData(10, 39, 1)]
    [InlineData(10, 40, 0)]
    [InlineData(12, 0, 12)]
    [InlineData(15, 0, 0)]
    public void ScalelightMatchesVanillaAtFullSize(int light, int scale, int colormap) => Assert.Equal(colormap, _full.scalelight(light, scale));

    [Theory]
    [InlineData(1, 47, 30)]
    [InlineData(8, 9, 23)]
    [InlineData(8, 47, 2)]
    [InlineData(10, 35, 1)]
    [InlineData(10, 36, 0)]
    public void ScalelightMatchesVanillaForScreenSize9InLowDetail(int light, int scale, int colormap) =>
        Assert.Equal(colormap, LightTables.R_InitLightTables(144, 1).scalelight(light, scale));

    [Fact]
    public void TablesDarkenWithDistanceAndBrightenWithLight()
    {
        for (int i = 0; i < LightTables.LIGHTLEVELS; i++)
        {
            for (int j = 1; j < LightTables.MAXLIGHTZ; j++)
                Assert.True(_full.zlight(i, j) >= _full.zlight(i, j - 1));
            for (int j = 1; j < LightTables.MAXLIGHTSCALE; j++)
                Assert.True(_full.scalelight(i, j) <= _full.scalelight(i, j - 1)); // bigger scale = nearer = brighter
            if (i > 0)
            {
                for (int j = 0; j < LightTables.MAXLIGHTZ; j++)
                    Assert.True(_full.zlight(i, j) <= _full.zlight(i - 1, j));
            }
        }
    }

    [Fact]
    public void WallScaleIndexIsTheScaleOfAWallSeenStraightOn()
    {
        Assert.Equal(160, _full.CenterX);
        Assert.Equal(LightTables.MAXLIGHTSCALE - 1, _full.WallScaleIndex(0));
        Assert.Equal(LightTables.MAXLIGHTSCALE - 1, _full.WallScaleIndex(1));
        Assert.Equal(LightTables.MAXLIGHTSCALE - 1, _full.WallScaleIndex(54 << FRACBITS)); // 2560 / 54 = 47
        Assert.Equal(46, _full.WallScaleIndex(55 << FRACBITS));
        Assert.Equal(10, _full.WallScaleIndex(256 << FRACBITS));
        Assert.Equal(1, _full.WallScaleIndex(2560 << FRACBITS));
        Assert.Equal(0, _full.WallScaleIndex((2560 << FRACBITS) + 1));
        // The shader's integer form: (centerx << 20) / distance.
        for (int d = 1; d < LightTables.MaxDistanceUnits << FRACBITS; d += 9973)
            Assert.Equal(System.Math.Min((160 << 20) / d, LightTables.MAXLIGHTSCALE - 1), _full.WallScaleIndex(d));
    }

    [Fact]
    public void PlaneZIndexSteps16Units()
    {
        Assert.Equal(0, LightTables.PlaneZIndex(-5));
        Assert.Equal(0, LightTables.PlaneZIndex((16 << FRACBITS) - 1));
        Assert.Equal(1, LightTables.PlaneZIndex(16 << FRACBITS));
        Assert.Equal(127, LightTables.PlaneZIndex(2048 << FRACBITS));
        Assert.Equal(127, LightTables.PlaneZIndex(LightTables.MaxDistanceUnits << FRACBITS));
    }

    [Fact]
    public void FakeContrastDarkensWallsAlongXAndLightensWallsAlongY()
    {
        Assert.Equal(-1, LightTables.FakeContrast(0, 64, 128, 64)); // v1->y == v2->y
        Assert.Equal(1, LightTables.FakeContrast(64, 0, 64, -128)); // v1->x == v2->x
        Assert.Equal(0, LightTables.FakeContrast(0, 0, 64, 64));
    }

    [Fact]
    public void WallAndPlaneColormapsClampTheLightLevel()
    {
        int d = 256 << FRACBITS; // scale index 10, z index 16
        Assert.Equal(_full.scalelight(10, 10), _full.WallColormap(160, 0, 0, d));
        Assert.Equal(_full.scalelight(9, 10), _full.WallColormap(160, 0, -1, d));
        Assert.Equal(_full.scalelight(11, 10), _full.WallColormap(160, 0, 1, d));
        Assert.Equal(_full.scalelight(12, 10), _full.WallColormap(160, 2, 0, d)); // extralight 2 (muzzle flash)
        Assert.Equal(_full.scalelight(15, 10), _full.WallColormap(255, 0, 1, d)); // clamped at LIGHTLEVELS - 1
        Assert.Equal(_full.scalelight(0, 10), _full.WallColormap(15, 0, -1, d)); // clamped at 0
        Assert.Equal(_full.zlight(10, 16), _full.PlaneColormap(160, 0, d));
        Assert.Equal(_full.zlight(11, 16), _full.PlaneColormap(160, 1, d));
        Assert.Equal(_full.zlight(15, 16), _full.PlaneColormap(255, 2, d));
        Assert.Equal(_full.zlight(0, 16), _full.PlaneColormap(0, 0, d));
    }

    [Fact]
    public void ToBytesHoldsZlightThenScalelight()
    {
        byte[] b = _full.ToBytes();
        Assert.Equal(LightTables.TableWidth * LightTables.TableHeight, b.Length);
        Assert.Equal(_full.zlight(8, 15), b[8 * LightTables.TableWidth + 15]);
        Assert.Equal(_full.scalelight(8, 10), b[(16 + 8) * LightTables.TableWidth + 10]);
        Assert.Equal(0, b[(16 + 0) * LightTables.TableWidth + 48]);
    }

    /// <summary>
    /// Vanilla picks the fake contrast per seg; a seg of a diagonal line
    /// rounded onto an axis gets that axis's contrast, so its side is split
    /// into runs. Lists every side whose runs aren't one run with the
    /// linedef's own contrast, as "MAP line:side start:contrast…" (start in
    /// whole units).
    /// </summary>
    private static List<string> UnusualSides(Level level)
    {
        var contrasts = SideContrasts.Build(level);
        var sides = new List<string>();
        foreach (Line l in level.Lines)
        {
            int lineContrast = LightTables.FakeContrast(l.V1.X, l.V1.Y, l.V2.X, l.V2.Y);
            for (int side = 0; side < 2; side++)
            {
                IReadOnlyList<ContrastRun> runs = contrasts.Runs(l, side);
                if (runs.Count == 0 || (runs.Count == 1 && runs[0].Contrast == lineContrast))
                    continue;
                var text = new List<string>();
                foreach (ContrastRun r in runs)
                    text.Add($"{r.Start >> FRACBITS}:{r.Contrast}");
                sides.Add($"{level.Name} {l.Index}:{side} {string.Join(" ", text)}");
            }
        }
        return sides;
    }

    [Fact]
    public void SyntheticSidesHaveTheirLinedefsContrast()
    {
        var level = Level.Load(new WadArchive([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]), "E1M1");
        Assert.Empty(UnusualSides(level));
        var contrasts = SideContrasts.Build(level);
        Assert.Equal([new ContrastRun(0, -1)], contrasts.Runs(level.Lines[0], 0)); // (-128,128)-(128,128): along x
    }

    [Fact]
    public void E1SidesSplitByAxisAlignedSegs()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom1());
        var sides = new List<string>();
        for (int m = 1; m <= 9; m++)
            sides.AddRange(UnusualSides(Level.Load(wad, $"E1M{m}")));
        Assert.Equal(E1Unusual, string.Join("; ", sides));
    }

    [Fact]
    public void Doom2SidesSplitByAxisAlignedSegs()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom2());
        var sides = new List<string>();
        for (int m = 1; m <= 32; m++)
            sides.AddRange(UnusualSides(Level.Load(wad, $"MAP{m:D2}")));
        if (TestWads.Doom2Md5 == TestWads.Doom2V1666Md5)
            Assert.Equal(Doom2Unusual, string.Join("; ", sides)); // other versions: built without errors
    }

    private const string E1Unusual = "E1M6 282:1 0:-1 8:0";
    private const string Doom2Unusual = "MAP05 859:0 0:0 48:1 56:0; MAP18 489:0 0:0 323:-1; MAP20 149:0 0:0 84:-1 304:0; "
        + "MAP20 647:0 0:0 1232:1 1357:0; MAP25 137:0 0:0 263:1 266:0; MAP31 607:0 0:0 145:-1";
}
