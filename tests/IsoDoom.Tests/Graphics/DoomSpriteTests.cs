using System.Collections.Generic;
using System.Linq;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Graphics;

/// <summary>
/// T1.5b sprite indexing against a registered or Ultimate Doom IWAD (any
/// version; skipped without it, <see cref="TestWads.RequireDoom"/>). Doom has
/// 111 of the 138 info.c <c>sprnames</c>: the 27 DOOM II-only ones have no
/// frames, and every sprite lump is used. Totals that may differ between
/// releases are asserted only for the 2024 re-release
/// (<see cref="TestWads.DoomUltimate2024Md5"/>), whose sprites are vanilla's
/// (its extra lumps, <c>GAMECONF</c>, <c>DMENUPIC</c>, …, are outside
/// <c>S_START</c>/<c>S_END</c>).
/// </summary>
public class DoomSpriteTests
{
    // info.c's sprnames that only DOOM II has lumps for.
    private static readonly string[] _doomTwoOnly =
    [
        "SHT2", "VILE", "FIRE", "FATB", "FBXP", "SKEL", "FATT", "CPOS", "BOS2", "BSPI", "PAIN", "SSWV", "KEEN", "BBRN",
        "MEGA", "SGN2", "HDB1", "HDB2", "HDB3", "HDB4", "HDB5", "HDB6", "POB1", "POB2", "BRS1", "TLMP", "TLP2",
    ];

    private static (WadArchive Wad, Sprites Sprites) OpenDoom()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom());
        return (wad, Sprites.R_InitSprites(wad));
    }

    private static bool Is2024 => TestWads.DoomMd5 == TestWads.DoomUltimate2024Md5;

    private static IEnumerable<int> UsedLumps(Sprites s) =>
        s.SpriteDefs.SelectMany(d => d.Frames).SelectMany(f => f.Lump).Distinct();

    [Fact]
    public void DoomSpritesHaveFramesAndEveryLumpIsUsed()
    {
        (WadArchive wad, Sprites s) = OpenDoom();
        Assert.Equal(SpriteNames.NUMSPRITES, s.NumSprites);
        Assert.Equal(SpriteNames.sprnames, s.SpriteDefs.Select(d => d.Name));
        Assert.All(_doomTwoOnly, n => Assert.Contains(n, SpriteNames.sprnames));
        Assert.All(s.SpriteDefs, d => Assert.True(_doomTwoOnly.Contains(d.Name) ? d.NumFrames == 0 : d.NumFrames > 0,
            $"{d.Name} has {d.NumFrames} frames"));
        Assert.Equal(SpriteNames.NUMSPRITES - _doomTwoOnly.Length, s.SpriteDefs.Count(d => d.NumFrames > 0));

        // No sprite lump is left over, so no sprite of the IWAD is missing from the name table.
        IReadOnlyList<WadLump> ns = wad.GetNamespace(LumpNamespace.Sprites);
        HashSet<WadLump> used = [.. UsedLumps(s).Select(l => wad.Lumps[l])];
        Assert.Equal(ns.Count, used.Count);
        Assert.All(ns, l => Assert.True(used.Contains(l), $"sprite lump {l.Name} is not used"));

        if (Is2024)
        {
            Assert.Equal(764, ns.Count);
            Assert.Equal(416, s.SpriteDefs.Sum(d => d.NumFrames));
        }
    }

    // Frame counts from the info.c states that use each sprite (vanilla v1.9), for the registered-only monsters.
    [Theory]
    [InlineData("CYBR", 16, "RRRRRRR.........")]
    [InlineData("SPID", 19, "RRRRRRRRR..........")]
    [InlineData("BOSS", 15, "RRRRRRRR.......")]
    [InlineData("SKUL", 11, "RRRRR......")]
    [InlineData("HEAD", 12, "RRRRRR......")]
    [InlineData("BAL7", 5, "RR...")]
    [InlineData("BFE2", 4, "....")]
    public void RegisteredSpriteFrames(string name, int frames, string rotate)
    {
        (_, Sprites s) = OpenDoom();
        SpriteDef def = s.Find(name)!;
        Assert.Equal(frames, def.NumFrames);
        Assert.Equal(rotate, string.Concat(def.Frames.Select(f => f.Rotate ? 'R' : '.')));
    }

    [Fact]
    public void EverySlotMatchesItsLumpName()
    {
        (WadArchive wad, Sprites s) = OpenDoom();
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
        (WadArchive wad, Sprites s) = OpenDoom();
        foreach (int l in UsedLumps(s))
        {
            IndexedImage img = Patch.Decode(wad.Lumps[l].Data.Span, wad.Lumps[l].Name);
            Assert.True(img.Width > 0 && img.Height > 0, wad.Lumps[l].Name);
            Assert.Equal(img.Width * img.Height, img.Pixels.Length);
        }
    }
}
