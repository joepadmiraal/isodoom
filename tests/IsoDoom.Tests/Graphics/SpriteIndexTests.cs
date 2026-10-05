using System.Linq;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Graphics;

/// <summary>T1.5 sprite grouping on synthetic WADs (no DOOM1.WAD needed).</summary>
public class SpriteIndexTests
{
    private static readonly string[] Names = { "TROO", "BAR1", "NONE" };

    private static WadFile SpriteWad(WadType type, params string[] lumps)
    {
        WadBuilder b = new(type);
        b.Markers("S_START");
        foreach (string l in lumps)
            b.Lump(l, 1);
        b.Markers("S_END");
        return b.ToWadFile(type == WadType.Iwad ? "iwad.wad" : "pwad.wad");
    }

    private static (WadArchive Wad, Sprites Sprites) Index(params WadFile[] files)
    {
        WadArchive wad = new(files);
        return (wad, Sprites.R_InitSprites(wad, Names));
    }

    private static string[] Slots(WadArchive wad, SpriteFrame f) =>
        f.Lump.Select((l, r) => $"{wad.Lumps[l].File.Name[0]}:{wad.Lumps[l].Name}{(f.Flip[r] ? "*" : "")}").ToArray();

    private static readonly string[] FullRotations = { "TROOA1", "TROOA2A8", "TROOA3A7", "TROOA4A6", "TROOA5" };

    [Fact]
    public void GroupsFramesRotationsAndMirroredPairs()
    {
        (WadArchive wad, Sprites s) = Index(SpriteWad(WadType.Iwad, [.. FullRotations, "TROOB0", "BAR1A0", "XXXXA0"]));
        Assert.Equal(3, s.NumSprites);

        SpriteDef troo = s.SpriteDefs[0];
        Assert.Equal(2, troo.NumFrames);
        Assert.True(troo.Frames[0].Rotate);
        Assert.Equal(new[] { "i:TROOA1", "i:TROOA2A8", "i:TROOA3A7", "i:TROOA4A6", "i:TROOA5", "i:TROOA4A6*", "i:TROOA3A7*", "i:TROOA2A8*" },
            Slots(wad, troo.Frames[0]));
        Assert.False(troo.Frames[1].Rotate);
        Assert.All(Slots(wad, troo.Frames[1]), x => Assert.Equal("i:TROOB0", x));

        Assert.Equal(1, s.Find("bar1")!.NumFrames);
        Assert.Equal(0, s.Find("NONE")!.NumFrames); // no lumps: no frames, not an error
        Assert.Null(s.Find("XXXX"));                // not in the name list: ignored
    }

    [Fact]
    public void PairAcrossFramesFlipsTheSecondFrame()
    {
        // e.g. a two-frame item sharing one patch: BAR1A0B0 is frame A, and frame B mirrored.
        (WadArchive wad, Sprites s) = Index(SpriteWad(WadType.Iwad, "BAR1A0B0"));
        SpriteDef bar1 = s.Find("BAR1")!;
        Assert.Equal(2, bar1.NumFrames);
        Assert.All(bar1.Frames[0].Flip, Assert.False);
        Assert.All(bar1.Frames[1].Flip, Assert.True);
        Assert.Equal(bar1.Frames[0].Lump, bar1.Frames[1].Lump);
    }

    [Theory]
    [InlineData("TROOA0", "TROOA1")]                // rot 0 and rotations
    [InlineData("TROOA1", "TROOA0")]
    [InlineData("TROOA0", "TROOB0A0")]              // two rot-0 lumps
    [InlineData("TROOA1", "TROOB1A1")]              // two lumps for one rotation
    [InlineData("TROOA1", "TROOA2", "TROOA3")]      // missing rotations
    [InlineData("TROOB0")]                          // frame A missing
    [InlineData("TROOA9")]                          // bad rotation
    [InlineData("TROO^0")]                          // frame past ']'
    [InlineData("TROOA")]                           // too short
    [InlineData("TROOA0B")]                         // odd length
    public void VanillaErrors(params string[] lumps)
    {
        Assert.Throws<WadFormatException>(() => Index(SpriteWad(WadType.Iwad, lumps)));
    }

    [Fact]
    public void DuplicateNameInOneFileUsesTheLastCopy()
    {
        WadBuilder b = new(WadType.Iwad);
        b.Markers("S_START").Lump("BAR1A0", 1).Lump("BAR1A0", 2).Markers("S_END");
        (WadArchive wad, Sprites s) = Index(b.ToWadFile());
        Assert.Equal(2, wad.Lumps[s.Find("BAR1")!.Frames[0].Lump[0]].Index);
    }

    [Fact]
    public void PwadReplacesSingleRotationOfAMirroredPair()
    {
        // The T1.1 note: a PWAD TROOA2 drops only the A2 half of the IWAD's TROOA2A8.
        (WadArchive wad, Sprites s) = Index(
            SpriteWad(WadType.Iwad, FullRotations),
            SpriteWad(WadType.Pwad, "TROOA2"));
        Assert.Equal(new[] { "i:TROOA1", "p:TROOA2", "i:TROOA3A7", "i:TROOA4A6", "i:TROOA5", "i:TROOA4A6*", "i:TROOA3A7*", "i:TROOA2A8*" },
            Slots(wad, s.Find("TROO")!.Frames[0]));
    }

    [Fact]
    public void PwadRotationZeroReplacesAllRotations()
    {
        (WadArchive wad, Sprites s) = Index(
            SpriteWad(WadType.Iwad, FullRotations),
            SpriteWad(WadType.Pwad, "TROOA0"));
        SpriteFrame a = s.Find("TROO")!.Frames[0];
        Assert.False(a.Rotate);
        Assert.All(Slots(wad, a), x => Assert.Equal("p:TROOA0", x));
    }

    [Fact]
    public void PwadRotationOverRotationZeroFrameMakesItRotate()
    {
        (WadArchive wad, Sprites s) = Index(
            SpriteWad(WadType.Iwad, "TROOA0"),
            SpriteWad(WadType.Pwad, "TROOA1"));
        SpriteFrame a = s.Find("TROO")!.Frames[0];
        Assert.True(a.Rotate);
        Assert.Equal("p:TROOA1", Slots(wad, a)[0]);
        Assert.All(Slots(wad, a).Skip(1), x => Assert.Equal("i:TROOA0", x));
    }

    [Fact]
    public void PwadCanAddFramesAndLaterFilesWin()
    {
        (WadArchive wad, Sprites s) = Index(
            SpriteWad(WadType.Iwad, "BAR1A0"),
            SpriteWad(WadType.Pwad, "BAR1B0"),
            SpriteWad(WadType.Pwad, "BAR1B0"));
        SpriteDef bar1 = s.Find("BAR1")!;
        Assert.Equal(2, bar1.NumFrames);
        Assert.Same(wad.Files[2], wad.Lumps[bar1.Frames[1].Lump[0]].File);
    }

    [Fact]
    public void ConflictsAcrossFilesAreNotErrors()
    {
        // Same slot in two files: the later file wins instead of vanilla's "two lumps" error.
        (WadArchive wad, Sprites s) = Index(
            SpriteWad(WadType.Iwad, "BAR1A0"),
            SpriteWad(WadType.Pwad, "BAR1B0A0"));
        Assert.All(Slots(wad, s.Find("BAR1")!.Frames[0]), x => Assert.Equal("p:BAR1B0A0*", x));
    }
}
