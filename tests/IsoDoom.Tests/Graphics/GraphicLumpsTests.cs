using System.Linq;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;
using static IsoDoom.Tests.Graphics.GraphicsDecoderTests;

namespace IsoDoom.Tests.Graphics;

/// <summary>T1.6 patch detection among global lumps, on synthetic data.</summary>
public class GraphicLumpsTests
{
    private static readonly byte[] GoodPatch = BuildPatch(2, 3, 0, 0, [(0, [1, 2])], [(1, [3, 4])]);

    [Fact]
    public void IsPatchAcceptsAWellFormedPatch()
    {
        Assert.True(Patch.IsPatch(GoodPatch));
        Assert.True(Patch.IsPatch(BuildPatch(1, 1, 0, 0, [[]]))); // an empty column
    }

    [Fact]
    public void IsPatchRejectsMalformedData()
    {
        Assert.False(Patch.IsPatch(new byte[7]));
        Assert.False(Patch.IsPatch(new byte[768])); // all zeros: width 0 (e.g. a palette)
        Assert.False(Patch.IsPatch(BuildPatch(1, 2, 0, 0, [(2, [9])]))); // post starts below the height

        byte[] intoTable = (byte[])GoodPatch.Clone();
        intoTable[8] = 9; // column 0 offset points into the offset table
        Assert.False(Patch.IsPatch(intoTable));

        byte[] unterminated = GoodPatch[..^1]; // drop column 1's 0xFF
        Assert.False(Patch.IsPatch(unterminated));

        byte[] pastEnd = (byte[])GoodPatch.Clone();
        pastEnd[8 + 4] = 200; // column 1 offset past the end
        Assert.False(Patch.IsPatch(pastEnd));
    }

    [Theory]
    [InlineData("PLAYPAL", true)]
    [InlineData("THINGS", true)]
    [InlineData("E1M1", true)]
    [InlineData("MAP07", true)]
    [InlineData("DEMO1", true)]
    [InlineData("D_E1M1", true)]
    [InlineData("DSPISTOL", true)]
    [InlineData("DPPISTOL", true)]
    [InlineData("TITLEPIC", false)]
    [InlineData("STBAR", false)]
    [InlineData("M_DOOM", false)]
    [InlineData("EAMMO", false)]
    public void NonGraphicNames(string name, bool nonGraphic) =>
        Assert.Equal(nonGraphic, GraphicLumps.IsNonGraphicName(name));

    [Fact]
    public void FindGlobalPatchesSkipsNamespacesNonGraphicsAndMergesByName()
    {
        byte[] other = BuildPatch(1, 1, 0, 0, [(0, [7])]);
        WadFile iwad = new WadBuilder(WadType.Iwad)
            .Lump("PLAYPAL", new byte[768])
            .Lump("M_FOO", GoodPatch)
            .Lump("DSBOOM", GoodPatch)     // a sound name, even if it parses as a patch
            .Markers("S_START").Lump("TROOA1", GoodPatch).Markers("S_END")
            .Lump("M_BAR", GoodPatch)
            .ToWadFile("iwad.wad");
        WadFile pwad = new WadBuilder()
            .Lump("M_FOO", other)
            .Lump("M_NEW", other)
            .ToWadFile("pwad.wad");

        var found = GraphicLumps.FindGlobalPatches(new WadArchive([iwad, pwad]));
        Assert.Equal(["M_FOO", "M_BAR", "M_NEW"], found.Select(l => l.Name));
        Assert.Equal("pwad.wad", found[0].File.Name); // replaced in place
    }
}
