using System;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Wad;

/// <summary>T1.6a lump directory against the shareware DOOM1.WAD and a DOOM II IWAD (skipped without them).</summary>
public class Doom1LumpDirectoryTests
{
    public static TheoryData<string> Iwads => new() { "DOOM1", "DOOM2" };

    private static WadArchive OpenIwad(string which) =>
        WadArchive.Open(which == "DOOM1" ? TestWads.RequireDoom1() : TestWads.RequireDoom2());

    private static Dictionary<LumpKind, int> CountKinds(IReadOnlyList<LumpEntry> list) =>
        list.GroupBy(e => e.Kind).ToDictionary(g => g.Key, g => g.Count());

    [Fact]
    public void EveryDoom1LumpIsClassified()
    {
        WadArchive wad = OpenIwad("DOOM1");
        IReadOnlyList<LumpEntry> list = LumpDirectory.Build(wad);
        Assert.Equal(1264, list.Count);
        Assert.Equal(Enumerable.Range(0, 1264), list.Select(e => e.Index));

        Dictionary<LumpKind, int> kinds = CountKinds(list);
        Assert.False(kinds.ContainsKey(LumpKind.Other), "unclassified: " + string.Join(", ", list.Where(e => e.Kind == LumpKind.Other).Select(e => e.Lump.Name)));
        var expected = new Dictionary<LumpKind, int>
        {
            [LumpKind.Marker] = 10,       // S_, P_, P1_, F_, F1_ start and end
            [LumpKind.MapMarker] = 9,     // E1M1..E1M9
            [LumpKind.MapData] = 90,
            [LumpKind.Graphic] = 320,
            [LumpKind.Flat] = 54,
            [LumpKind.Sprite] = 483,
            [LumpKind.Patch] = 165,       // SW18_7 twice
            [LumpKind.TextureDefs] = 2,   // TEXTURE1, PNAMES
            [LumpKind.Palette] = 1,
            [LumpKind.Colormap] = 1,
            [LumpKind.Sound] = 110,       // 55 DS*, 55 DP*
            [LumpKind.Music] = 13,        // D_E1M1..D_E1M9, D_INTER, D_INTRO, D_VICTOR, D_INTROA
            [LumpKind.Instruments] = 2,   // GENMIDI, DMXGUS
            [LumpKind.Demo] = 3,
            [LumpKind.Endoom] = 1,
        };
        foreach ((LumpKind kind, int count) in expected)
            Assert.True(kinds.GetValueOrDefault(kind) == count, $"{kind}: {kinds.GetValueOrDefault(kind)}, expected {count}");
        Assert.Equal(1264, kinds.Values.Sum());

        // The IWAD's only duplicate: SW18_7 is in the patch namespace twice, and the second copy wins.
        LumpEntry[] overridden = list.Where(e => e.IsOverridden).ToArray();
        LumpEntry sw18 = Assert.Single(overridden);
        Assert.Equal("SW18_7", sw18.Lump.Name);
        Assert.Same(list.Last(e => e.Lump.Name == "SW18_7").Lump, sw18.OverriddenBy);
        Assert.All(list.Where(e => e.Kind == LumpKind.Music), e => Assert.Equal("MUS", e.Detail));
        // Every DS* sound is 11025 Hz except the item respawn sound, the one 22050 Hz sample.
        Assert.All(list.Where(e => e.Kind == LumpKind.Sound && e.Lump.Name.StartsWith("DS")),
            e => Assert.StartsWith(e.Lump.Name == "DSITMBK" ? "digitized, 22050 Hz, " : "digitized, 11025 Hz, ", e.Detail));
        Assert.DoesNotContain(list, e => e.Kind == LumpKind.Sound && e.Detail.Contains("not playable", StringComparison.Ordinal));
        Assert.All(list.Where(e => e.Kind == LumpKind.Sound && e.Lump.Name.StartsWith("DP")), e => Assert.Equal("PC speaker", e.Detail));
        Assert.Equal("of E1M9", list.Last(e => e.Kind == LumpKind.MapData).Detail);
    }

    [Theory]
    [MemberData(nameof(Iwads))]
    public void EveryGraphicLumpIsListedByTheViewer(string which)
    {
        WadArchive wad = OpenIwad(which);
        IReadOnlyList<LumpEntry> list = LumpDirectory.Build(wad);
        GraphicsCatalog catalog = GraphicsCatalog.Load(wad);
        Assert.DoesNotContain(list, e => e.Kind == LumpKind.Other);
        foreach (LumpEntry e in list)
        {
            GraphicLocation? loc = catalog.Locate(e.Index);
            if (!e.IsGraphic)
            {
                Assert.True(loc is null || loc.Value.Category == GraphicCategory.Palette, e.Lump.Name);
                continue;
            }
            if (e.IsOverridden)
            {
                // Not in any list, but the viewer still shows it decoded directly.
                Assert.Null(loc);
                Assert.Equal(e.Lump.Name, catalog.ViewLump(e.Lump, "overridden").Name);
                continue;
            }
            Assert.True(loc is not null, $"{e.Lump.Name} ({e.Kind}) is in no viewer list");
            GraphicView view = loc.Value.Category == GraphicCategory.Sprites
                ? catalog.GetSprite(loc.Value.Index, loc.Value.Frame, loc.Value.Slot)
                : catalog.Get(loc.Value.Category, loc.Value.Index);
            Assert.Equal(e.Lump.Name, view.Name);
            Assert.False(view.Flip, e.Lump.Name);
        }
    }
}
