using System.Collections.Generic;
using System.Linq;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Graphics;

/// <summary>
/// T1.5a sprite indexing against a full IWAD: DOOM II (any version; skipped
/// without it). DOOM II has a lump for every one of the 138 info.c
/// <c>sprnames</c>, so this also checks the ported name table. Totals that may
/// differ between releases are asserted only for v1.666
/// (<see cref="TestWads.Doom2V1666Md5"/>).
/// </summary>
public class Doom2SpriteTests
{
    private static (WadArchive Wad, Sprites Sprites) OpenDoom2()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom2());
        return (wad, Sprites.R_InitSprites(wad));
    }

    private static bool IsV1666 => TestWads.Doom2Md5 == TestWads.Doom2V1666Md5;

    private static IEnumerable<int> UsedLumps(Sprites s) =>
        s.SpriteDefs.SelectMany(d => d.Frames).SelectMany(f => f.Lump).Distinct();

    [Fact]
    public void EverySpriteNameHasFramesAndEveryLumpIsUsed()
    {
        (WadArchive wad, Sprites s) = OpenDoom2();
        Assert.Equal(SpriteNames.NUMSPRITES, s.NumSprites);
        Assert.Equal(SpriteNames.NUMSPRITES, SpriteNames.sprnames.Distinct().Count());
        Assert.Equal(SpriteNames.sprnames, s.SpriteDefs.Select(d => d.Name));
        Assert.All(s.SpriteDefs, d => Assert.True(d.NumFrames > 0, $"{d.Name} has no frames"));

        // No sprite lump is left over, so no DOOM II sprite is missing from the name table.
        IReadOnlyList<WadLump> ns = wad.GetNamespace(LumpNamespace.Sprites);
        HashSet<WadLump> used = [.. UsedLumps(s).Select(l => wad.Lumps[l])];
        Assert.Equal(ns.Count, used.Count);
        Assert.All(ns, l => Assert.True(used.Contains(l), $"sprite lump {l.Name} is not used"));

        if (IsV1666)
        {
            Assert.Equal(1381, ns.Count);
            Assert.Equal(628, s.SpriteDefs.Sum(d => d.NumFrames));
        }
    }

    // Frame counts from the info.c states that use each sprite (vanilla v1.9).
    [Theory]
    [InlineData("VILE", 29, "RRRRRRRRRRRRRRRRR.........RRR")] // A-Q rotate; R-Z death; '[', '\', ']' heal
    [InlineData("CYBR", 16, "RRRRRRR.........")]
    [InlineData("SPID", 19, "RRRRRRRRR..........")]
    [InlineData("SSWV", 22, "RRRR..................")] // attack and pain (E-H) face front only
    [InlineData("KEEN", 13, ".............")]
    [InlineData("BBRN", 2, "..")]
    [InlineData("BSPI", 16, "RRRRRRRRR.......")]
    [InlineData("FATT", 20, "RRRRRRRRRR..........")]
    [InlineData("SKEL", 17, "RRRRRRRRRRRR.....")]
    [InlineData("PAIN", 13, "RRRRRRR......")]
    [InlineData("CPOS", 20, "RRRRRRR.............")]
    [InlineData("BOS2", 15, "RRRRRRRR.......")]
    [InlineData("FATB", 2, "RR")]
    [InlineData("MANF", 2, "RR")]
    public void DoomTwoSpriteFrames(string name, int frames, string rotate)
    {
        (_, Sprites s) = OpenDoom2();
        SpriteDef def = s.Find(name)!;
        Assert.Equal(frames, def.NumFrames);
        Assert.Equal(rotate, string.Concat(def.Frames.Select(f => f.Rotate ? 'R' : '.')));
    }

    [Fact]
    public void EverySlotMatchesItsLumpName()
    {
        (WadArchive wad, Sprites s) = OpenDoom2();
        foreach (SpriteDef def in s.SpriteDefs)
        {
            for (int f = 0; f < def.NumFrames; f++)
            {
                SpriteFrame frame = def.Frames[f];
                char c = (char)('A' + f);
                for (int r = 0; r < 8; r++)
                {
                    string lump = wad.Lumps[frame.Lump[r]].Name;
                    Assert.StartsWith(def.Name, lump);
                    // Rotation 0 fills every slot; otherwise slot r holds rotation r+1,
                    // from the first half of the name or (flipped) the second.
                    string expected = frame.Rotate ? $"{c}{r + 1}" : $"{c}0";
                    string half = frame.Flip[r] ? lump[6..] : lump.Substring(4, 2);
                    Assert.True(half == expected, $"{def.Name} frame {c} slot {r}: {lump} (flip {frame.Flip[r]})");
                }
            }
        }
    }

    [Fact]
    public void EveryFrameDecodes()
    {
        (WadArchive wad, Sprites s) = OpenDoom2();
        foreach (int l in UsedLumps(s))
        {
            IndexedImage img = Patch.Decode(wad.Lumps[l].Data.Span, wad.Lumps[l].Name);
            Assert.True(img.Width > 0 && img.Height > 0, wad.Lumps[l].Name);
            Assert.Equal(img.Width * img.Height, img.Pixels.Length);
        }
    }
}
