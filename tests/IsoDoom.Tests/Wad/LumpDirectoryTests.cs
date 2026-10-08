using System.Collections.Generic;
using System.Linq;
using IsoDoom.Tests.Graphics;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;
using static IsoDoom.Tests.Graphics.GraphicsDecoderTests;

namespace IsoDoom.Tests.Wad;

/// <summary>T1.6a lump classification and overrides on synthetic WADs (no DOOM1.WAD needed).</summary>
public class LumpDirectoryTests
{
    private static readonly byte[] _goodPatch = BuildPatch(2, 3, 0, 0, [(0, [1, 2])], [(1, [3, 4])]);
    private static readonly byte[] _otherPatch = BuildPatch(1, 1, 0, 0, [(0, [7])]);

    private static byte[] Bytes(string ascii, int size)
    {
        byte[] data = new byte[size];
        System.Text.Encoding.Latin1.GetBytes(ascii).CopyTo(data, 0);
        return data;
    }

    private static WadBuilder MapLumps(WadBuilder b)
    {
        foreach (string name in new[] { "THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES", "SEGS", "SSECTORS", "NODES", "SECTORS", "REJECT", "BLOCKMAP" })
            b.Lump(name, 1, 2, 3, 4);
        return b;
    }

    private static WadFile Iwad()
    {
        WadBuilder b = new WadBuilder(WadType.Iwad)
            .Lump("PLAYPAL", new byte[768 * 2])
            .Lump("COLORMAP", new byte[256 * 34])
            .Lump("ENDOOM", new byte[4000])
            .Lump("DEMO1", 109, 2, 1)
            .Markers("E1M1");
        MapLumps(b).Markers("MAP01");
        MapLumps(b).Markers("MYMAP");
        MapLumps(b)
            .Lump("TEXTURE1", TextureCompositionTests.BuildTextureLump(new TextureCompositionTests.Tex("WALLTEX", 2, 3, false, (0, 0, 0))))
            .Lump("PNAMES", TextureCompositionTests.BuildPNames("WALL1"))
            .Lump("GENMIDI", Bytes("#OPL_II#", 64))
            .Lump("DMXGUS", Bytes("# DMX", 64))
            .Lump("D_E1M1", Bytes("MUS\x1A", 32))
            .Lump("D_RUNNIN", Bytes("MThd", 32))
            .Lump("D_JUNK", 1, 2, 3, 4)
            .Lump("SONG", Bytes("MThd", 32))
            .Lump("DSPISTOL", 3, 0, 0x11, 0x2B, 4, 0, 0, 0, 128, 128, 128, 128)
            .Lump("DPPISTOL", 0, 0, 2, 0, 9, 9)
            .Lump("TITLEPIC", _goodPatch)
            .Lump("FOOBAR", 0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0, 1)
            .Markers("EMPTY", "X_END", "S_END")
            .Markers("S_START").Lump("TROOA0", _goodPatch).Markers("S_END")
            .Markers("F_START", "F1_START").Lump("FLAT1", new byte[4096]).Lump("SHORT", new byte[10]).Markers("F1_END", "F_END")
            .Markers("P_START").Lump("WALL1", _goodPatch).Markers("P_END");
        return b.ToWadFile("iwad.wad");
    }

    private static WadFile Pwad()
    {
        WadBuilder b = new WadBuilder().Markers("E1M1");
        return MapLumps(b)
            .Lump("TITLEPIC", _otherPatch)
            .Markers("FF_START").Lump("FLAT1", new byte[4096]).Markers("FF_END")
            .Markers("SS_START").Lump("TROOA0", _otherPatch).Markers("SS_END")
            .ToWadFile("pwad.wad");
    }

    private static LumpEntry Entry(System.Collections.Generic.IReadOnlyList<LumpEntry> list, string file, string name, int nth = 0) =>
        list.Where(e => e.Lump.File.Name == file && e.Lump.Name == name).ElementAt(nth);

    [Fact]
    public void ClassifiesEveryKind()
    {
        IReadOnlyList<LumpEntry> list = LumpDirectory.Build(new WadArchive([Iwad()]));
        LumpKind Kind(string name, int nth = 0) => Entry(list, "iwad.wad", name, nth).Kind;
        string Detail(string name, int nth = 0) => Entry(list, "iwad.wad", name, nth).Detail;

        Assert.Equal(LumpKind.Palette, Kind("PLAYPAL"));
        Assert.Equal("2 palettes", Detail("PLAYPAL"));
        Assert.Equal(LumpKind.Colormap, Kind("COLORMAP"));
        Assert.Equal(LumpKind.Endoom, Kind("ENDOOM"));
        Assert.Equal(LumpKind.Demo, Kind("DEMO1"));
        Assert.Equal(LumpKind.MapMarker, Kind("E1M1"));
        Assert.Equal(LumpKind.MapMarker, Kind("MAP01"));
        Assert.Equal(LumpKind.MapMarker, Kind("MYMAP")); // not a vanilla name, but followed by THINGS
        Assert.Equal(LumpKind.MapData, Kind("THINGS", 0));
        Assert.Equal("of E1M1", Detail("THINGS", 0));
        Assert.Equal("of MAP01", Detail("BLOCKMAP", 1));
        Assert.Equal("MYMAP", Entry(list, "iwad.wad", "REJECT", 2).MapName);
        Assert.Equal(LumpKind.TextureDefs, Kind("TEXTURE1"));
        Assert.Equal(LumpKind.TextureDefs, Kind("PNAMES"));
        Assert.Equal((LumpKind.Instruments, "OPL instruments, not readable: 64 bytes, too short for 175 instruments (6308)"), (Kind("GENMIDI"), Detail("GENMIDI")));
        Assert.Equal(LumpKind.Instruments, Kind("DMXGUS"));
        Assert.Equal((LumpKind.Music, "MUS, not readable: cut off inside the event at byte 31"), (Kind("D_E1M1"), Detail("D_E1M1")));
        Assert.Equal((LumpKind.Music, "MIDI"), (Kind("D_RUNNIN"), Detail("D_RUNNIN")));
        Assert.Equal((LumpKind.Music, "unknown format"), (Kind("D_JUNK"), Detail("D_JUNK")));
        Assert.Equal((LumpKind.Music, "MIDI"), (Kind("SONG"), Detail("SONG"))); // by header alone
        Assert.Equal((LumpKind.Sound, "digitized, 11025 Hz, not playable: 4 samples, too short for DMX"), (Kind("DSPISTOL"), Detail("DSPISTOL")));
        Assert.Equal((LumpKind.Sound, "PC speaker"), (Kind("DPPISTOL"), Detail("DPPISTOL")));
        Assert.Equal(LumpKind.Graphic, Kind("TITLEPIC"));
        Assert.Equal(LumpKind.Other, Kind("FOOBAR"));
        Assert.Equal((LumpKind.Other, "empty"), (Kind("EMPTY"), Detail("EMPTY")));
        Assert.Equal(LumpKind.Marker, Kind("X_END"));     // not a namespace marker name
        Assert.Equal(LumpKind.Marker, Kind("S_END", 0));  // a stray end marker (WadFile: not IsMarker)
        Assert.False(Entry(list, "iwad.wad", "S_END", 0).Lump.IsMarker);
        Assert.Equal(LumpKind.Marker, Kind("S_START"));
        Assert.Equal(LumpKind.Sprite, Kind("TROOA0"));
        Assert.Equal(LumpKind.Marker, Kind("F1_START"));
        Assert.Equal(LumpKind.Flat, Kind("FLAT1"));
        Assert.Equal((LumpKind.Flat, "too short for a flat"), (Kind("SHORT"), Detail("SHORT")));
        Assert.Equal(LumpKind.Patch, Kind("WALL1"));
        Assert.All(list, e => Assert.False(e.IsOverridden, e.Lump.Name));
    }

    [Fact]
    public void ListsEveryLumpInArchiveOrder()
    {
        var wad = new WadArchive([Iwad(), Pwad()]);
        IReadOnlyList<LumpEntry> list = LumpDirectory.Build(wad);
        Assert.Equal(wad.NumLumps, list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            Assert.Equal(i, list[i].Index);
            Assert.Same(wad.Lumps[i], list[i].Lump);
        }
    }

    [Fact]
    public void MarksOverriddenLumps()
    {
        IReadOnlyList<LumpEntry> list = LumpDirectory.Build(new WadArchive([Iwad(), Pwad()]));

        // Global graphic, flat (FF_ namespace merges with F_), sprite.
        foreach (string name in new[] { "TITLEPIC", "FLAT1", "TROOA0" })
        {
            Assert.Same(Entry(list, "pwad.wad", name).Lump, Entry(list, "iwad.wad", name).OverriddenBy);
            Assert.False(Entry(list, "pwad.wad", name).IsOverridden);
        }

        // E1M1 and all its data lumps point at the PWAD's E1M1; MAP01's data is untouched.
        WadLump pwadMap = Entry(list, "pwad.wad", "E1M1").Lump;
        Assert.Same(pwadMap, Entry(list, "iwad.wad", "E1M1").OverriddenBy);
        Assert.Same(pwadMap, Entry(list, "iwad.wad", "THINGS", 0).OverriddenBy);
        Assert.Same(pwadMap, Entry(list, "iwad.wad", "BLOCKMAP", 0).OverriddenBy);
        Assert.False(Entry(list, "iwad.wad", "THINGS", 1).IsOverridden);
        Assert.False(Entry(list, "pwad.wad", "THINGS").IsOverridden);

        // Markers never are; lumps the PWAD doesn't touch aren't either.
        Assert.All(list.Where(e => e.Kind == LumpKind.Marker), e => Assert.False(e.IsOverridden));
        Assert.False(Entry(list, "iwad.wad", "WALL1").IsOverridden);
        Assert.False(Entry(list, "iwad.wad", "PLAYPAL").IsOverridden);
    }

    [Fact]
    public void GraphicKindMatchesTheViewerGraphicsList()
    {
        var wad = new WadArchive([Iwad(), Pwad()]);
        IEnumerable<WadLump> winners = LumpDirectory.Build(wad).Where(e => e.Kind == LumpKind.Graphic && !e.IsOverridden).Select(e => e.Lump);
        Assert.Equal(GraphicLumps.FindGlobalPatches(wad), winners);
    }

    [Fact]
    public void CatalogLocatesTheLumpsItLists()
    {
        var wad = new WadArchive([Iwad(), Pwad()]);
        IReadOnlyList<LumpEntry> list = LumpDirectory.Build(wad);
        var catalog = GraphicsCatalog.Load(wad);
        GraphicLocation? Locate(string file, string name) => catalog.Locate(Entry(list, file, name).Index);

        Assert.Equal(new GraphicLocation(GraphicCategory.Graphics, 0), Locate("pwad.wad", "TITLEPIC"));
        Assert.Equal(new GraphicLocation(GraphicCategory.Patches, 0), Locate("iwad.wad", "WALL1"));
        Assert.Equal(GraphicCategory.Flats, Locate("pwad.wad", "FLAT1")!.Value.Category);
        GraphicLocation troo = Locate("pwad.wad", "TROOA0")!.Value;
        Assert.Equal((GraphicCategory.Sprites, "TROO", 0, 0), (troo.Category, catalog.GetName(GraphicCategory.Sprites, troo.Index), troo.Frame, troo.Slot));
        Assert.Equal(new GraphicLocation(GraphicCategory.Palette, 0), Locate("iwad.wad", "PLAYPAL"));
        Assert.Equal(new GraphicLocation(GraphicCategory.Palette, 1), Locate("iwad.wad", "COLORMAP"));

        // Overridden lumps and non-graphics have no list entry; overridden graphics still decode directly.
        Assert.Null(Locate("iwad.wad", "TITLEPIC"));
        Assert.Null(Locate("iwad.wad", "TROOA0"));
        Assert.Null(Locate("iwad.wad", "DEMO1"));
        GraphicView old = GraphicsCatalog.ViewLump(Entry(list, "iwad.wad", "TITLEPIC").Lump, "overridden");
        Assert.Equal((2, 3), (old.Image.Width, old.Image.Height));
        Assert.Equal(GraphicCategory.Flats, GraphicsCatalog.ViewLump(Entry(list, "iwad.wad", "FLAT1").Lump, "overridden").Category);
    }
}
