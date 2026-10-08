using System.Linq;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Graphics;

/// <summary>T1.5 sprite indexing against the shareware v1.9 DOOM1.WAD (skipped without it).</summary>
public class Doom1SpriteTests
{
    private static (WadArchive Wad, Sprites Sprites) OpenDoom1()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom1());
        return (wad, Sprites.R_InitSprites(wad));
    }

    private static string LumpName(WadArchive wad, SpriteFrame frame, int r) => wad.Lumps[frame.Lump[r]].Name;

    [Fact]
    public void SpriteListFollowsInfoC()
    {
        (WadArchive wad, Sprites s) = OpenDoom1();
        Assert.Equal(SpriteNames.NUMSPRITES, s.NumSprites);
        Assert.Equal("TROO", s.SpriteDefs[0].Name);
        Assert.Equal("PLAY", s.SpriteDefs[28].Name);
        Assert.Equal("BAR1", s.SpriteDefs[57].Name);

        // Shareware has 61 of the 138 sprites; the others (SPID, CYBR, ...) have no frames.
        Assert.Equal(61, s.SpriteDefs.Count(d => d.NumFrames > 0));
        Assert.Equal(0, s.Find("SPID")!.NumFrames);

        // Every one of the 483 sprite lumps is used by some frame.
        int used = s.SpriteDefs.SelectMany(d => d.Frames).SelectMany(f => f.Lump).Distinct().Count();
        Assert.Equal(483, wad.GetNamespace(LumpNamespace.Sprites).Count);
        Assert.Equal(483, used);
    }

    [Theory]
    [InlineData("TROO", 21, 8)] // A-H walk/attack/pain rotate; I-U death and gib frames don't
    [InlineData("PLAY", 23, 7)] // A-G rotate; H-W don't
    [InlineData("BAR1", 2, 0)]
    public void FrameAndRotationCounts(string name, int frames, int rotatedFrames)
    {
        (_, Sprites s) = OpenDoom1();
        SpriteDef def = s.Find(name)!;
        Assert.Equal(frames, def.NumFrames);
        Assert.Equal(rotatedFrames, def.Frames.Count(f => f.Rotate));
        Assert.All(def.Frames.Take(rotatedFrames), f => Assert.True(f.Rotate));
    }

    [Theory]
    [InlineData("TROO")]
    [InlineData("PLAY")]
    public void RotatedFramesUseMirroredPairs(string name)
    {
        (WadArchive wad, Sprites s) = OpenDoom1();
        foreach ((SpriteFrame frame, int f) in s.Find(name)!.Frames.Select((fr, i) => (fr, i)).Where(x => x.fr.Rotate))
        {
            char c = (char)('A' + f);
            string[] expected =
            [
                $"{name}{c}1", $"{name}{c}2{c}8", $"{name}{c}3{c}7", $"{name}{c}4{c}6",
                $"{name}{c}5", $"{name}{c}4{c}6", $"{name}{c}3{c}7", $"{name}{c}2{c}8",
            ];
            Assert.Equal(expected, Enumerable.Range(0, 8).Select(r => LumpName(wad, frame, r)));
            // Rotations 6-8 are the flipped halves of the pairs.
            Assert.Equal([false, false, false, false, false, true, true, true], frame.Flip);
        }
    }

    [Fact]
    public void RotationZeroFramesUseOneLumpForAllAngles()
    {
        (WadArchive wad, Sprites s) = OpenDoom1();
        SpriteDef bar1 = s.Find("BAR1")!;
        Assert.Equal("BAR1A0", LumpName(wad, bar1.Frames[0], 0));
        Assert.Equal("BAR1B0", LumpName(wad, bar1.Frames[1], 0));
        foreach (SpriteFrame frame in bar1.Frames.Concat(s.Find("TROO")!.Frames.Skip(8)))
        {
            Assert.False(frame.Rotate);
            Assert.All(frame.Lump, l => Assert.Equal(frame.Lump[0], l));
            Assert.All(frame.Flip, Assert.False);
        }
        Assert.Equal("TROOU0", LumpName(wad, s.Find("TROO")!.Frames[20], 3));
    }

    [Fact]
    public void EveryFrameDecodes()
    {
        (WadArchive wad, Sprites s) = OpenDoom1();
        foreach (int l in s.SpriteDefs.SelectMany(d => d.Frames).SelectMany(f => f.Lump).Distinct())
        {
            IndexedImage img = Patch.Decode(wad.Lumps[l].Data.Span, wad.Lumps[l].Name);
            Assert.True(img.Width > 0 && img.Height > 0);
        }
    }
}
