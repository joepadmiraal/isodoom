using System.Linq;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Wad;

/// <summary>T1.1 against the shareware v1.9 DOOM1.WAD (skipped without it).</summary>
public class Doom1WadTests
{
    private static WadArchive OpenDoom1() => WadArchive.Open(TestWads.RequireDoom1());

    [Fact]
    public void HeaderAndDirectory()
    {
        WadArchive wad = OpenDoom1();
        WadFile file = Assert.Single(wad.Files);
        Assert.Equal(WadType.Iwad, file.Type);
        Assert.Equal(1264, file.Lumps.Count);
        Assert.Equal(1264, wad.NumLumps);
        Assert.Equal("DOOM1.WAD", file.Name);
    }

    [Theory]
    [InlineData("E1M1", 6, 0, LumpNamespace.Global)]
    [InlineData("PLAYPAL", 0, 10752, LumpNamespace.Global)]
    [InlineData("COLORMAP", 1, 8704, LumpNamespace.Global)]
    [InlineData("TROOA1", 702, -1, LumpNamespace.Sprites)]
    [InlineData("NUKAGE1", -1, 4096, LumpNamespace.Flats)]
    [InlineData("WALL00_1", 1039, -1, LumpNamespace.Patches)]
    public void LumpsAreFound(string name, int index, int size, LumpNamespace ns)
    {
        WadArchive wad = OpenDoom1();
        int num = wad.W_CheckNumForName(name);
        Assert.True(num >= 0, $"{name} not found");
        if (index >= 0)
            Assert.Equal(index, num);
        if (size >= 0)
            Assert.Equal(size, wad.W_LumpLength(num));
        WadLump lump = wad.Lumps[num];
        Assert.Equal(name, lump.Name);
        Assert.Equal(ns, lump.Namespace);
        Assert.False(lump.IsMarker);
        Assert.Same(lump, wad.Find(name, ns));
        Assert.Equal(lump.Size, wad.W_CacheLumpName(name).Length);
    }

    [Fact]
    public void LookupIsCaseInsensitive()
    {
        WadArchive wad = OpenDoom1();
        Assert.Equal(wad.W_GetNumForName("TROOA1"), wad.W_CheckNumForName("trooa1"));
        Assert.Equal(-1, wad.W_CheckNumForName("MAP01"));
        Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => wad.W_GetNumForName("MAP01"));
    }

    [Fact]
    public void PlaypalStartsWithBlack()
    {
        // The first colour of palette 0 is (0, 0, 0); the second is (31, 23, 11).
        byte[] playpal = OpenDoom1().W_CacheLumpName("PLAYPAL").ToArray();
        Assert.Equal(new byte[] { 0, 0, 0, 31, 23, 11 }, playpal[..6]);
    }

    [Fact]
    public void Namespaces()
    {
        WadArchive wad = OpenDoom1();

        // S_START (552) .. S_END (1036).
        var sprites = wad.GetNamespace(LumpNamespace.Sprites);
        Assert.Equal(483, sprites.Count);
        Assert.Equal(553, sprites[0].Index);
        Assert.Contains(sprites, l => l.Name == "TROOA1");

        // F_START (1206) .. F_END (1263), less the F1_START/F1_END markers.
        var flats = wad.GetNamespace(LumpNamespace.Flats);
        Assert.Equal(54, flats.Count);
        Assert.All(flats, l => Assert.Equal(4096, l.Size));
        Assert.Equal("F_SKY1", flats[^1].Name);

        // P_START (1037) .. P_END (1205), less P1_START/P1_END: 165 lumps,
        // and the IWAD's duplicate SW18_7 (1120, 1121) collapses to the later one.
        var patches = wad.GetNamespace(LumpNamespace.Patches);
        Assert.Equal(164, patches.Count);
        Assert.Equal(1121, wad.Find("SW18_7", LumpNamespace.Patches)!.Index);
        Assert.Equal(1121, wad.W_CheckNumForName("SW18_7"));

        Assert.All(new[] { 552, 1036, 1037, 1038, 1204, 1205, 1206, 1207, 1262, 1263 },
            i => Assert.True(wad.Lumps[i].IsMarker, wad.Lumps[i].Name));
        Assert.Null(wad.Find("TROOA1", LumpNamespace.Flats));
        Assert.Equal(LumpNamespace.Global, wad.Lumps.Single(l => l.Name == "THINGS" && l.Index == 7).Namespace);
    }

    [Fact]
    public void PwadOverridesIwad()
    {
        WadFile iwad = WadFile.Open(TestWads.RequireDoom1());
        WadFile pwad = new WadBuilder()
            .Lump("PLAYPAL", 1, 2, 3)
            .Markers("FF_START").Lump("NUKAGE2", new byte[4096]).Lump("MYFLAT", new byte[4096]).Markers("FF_END")
            .ToWadFile("mod.wad");
        WadArchive wad = new(new[] { iwad, pwad });

        Assert.Equal(1264 + 5, wad.NumLumps);
        Assert.Equal(new byte[] { 1, 2, 3 }, wad.W_CacheLumpName("PLAYPAL").ToArray());
        Assert.Same(pwad, wad.Find("PLAYPAL")!.File);

        var flats = wad.GetNamespace(LumpNamespace.Flats);
        Assert.Equal(55, flats.Count);
        int nukage1 = IndexOf(flats, "NUKAGE1");
        Assert.Equal("NUKAGE2", flats[nukage1 + 1].Name);
        Assert.Same(pwad, flats[nukage1 + 1].File);
        Assert.Same(iwad, flats[nukage1].File);
        Assert.Equal("MYFLAT", flats[^1].Name);
    }

    private static int IndexOf(System.Collections.Generic.IReadOnlyList<WadLump> lumps, string name)
    {
        for (int i = 0; i < lumps.Count; i++)
        {
            if (lumps[i].Name == name)
                return i;
        }
        return -1;
    }
}
