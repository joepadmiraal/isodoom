using System;
using System.Buffers.Binary;
using System.Linq;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Wad;

/// <summary>WAD parsing and merging on synthetic WADs (no DOOM1.WAD needed).</summary>
public class WadFileTests
{
    [Fact]
    public void ParsesHeaderAndDirectory()
    {
        WadFile wad = new WadBuilder(WadType.Iwad).Lump("PLAYPAL", 1, 2, 3).Lump("E1M1").Lump("THINGS", 9).ToWadFile();
        Assert.Equal(WadType.Iwad, wad.Type);
        Assert.Equal(new[] { "PLAYPAL", "E1M1", "THINGS" }, wad.Lumps.Select(l => l.Name));
        Assert.Equal(new byte[] { 1, 2, 3 }, wad.Lumps[0].Data.ToArray());
        Assert.Equal(0, wad.Lumps[1].Size);
        Assert.Equal(new byte[] { 9 }, wad.Lumps[2].Data.ToArray());
    }

    [Fact]
    public void NamesAreNormalised()
    {
        byte[] data = new WadBuilder().Lump("abcdefgh", 1).Lump("x", 2).Build();
        WadFile wad = WadFile.FromBytes(data);
        Assert.Equal("ABCDEFGH", wad.Lumps[0].Name); // 8 chars, no terminating NUL
        Assert.Equal("X", wad.Lumps[1].Name);

        WadArchive archive = new(new[] { wad });
        Assert.Equal(0, archive.W_CheckNumForName("AbCdEfGh"));
        Assert.Equal(0, archive.W_CheckNumForName("ABCDEFGHIJ")); // vanilla compares 8 chars only
    }

    [Fact]
    public void RejectsBadFiles()
    {
        Assert.Throws<WadFormatException>(() => WadFile.FromBytes(new byte[5]));

        byte[] badMagic = new WadBuilder().Build();
        badMagic[0] = (byte)'X';
        Assert.Throws<WadFormatException>(() => WadFile.FromBytes(badMagic));

        byte[] badDir = new WadBuilder().Lump("A", 1).Build();
        BinaryPrimitives.WriteInt32LittleEndian(badDir.AsSpan(4), 100);
        Assert.Throws<WadFormatException>(() => WadFile.FromBytes(badDir));

        byte[] badLump = new WadBuilder().Lump("A", 1).Build();
        BinaryPrimitives.WriteInt32LittleEndian(badLump.AsSpan(badLump.Length - 12), 1000); // size
        Assert.Throws<WadFormatException>(() => WadFile.FromBytes(badLump));
    }

    [Fact]
    public void ClassifiesNamespaces()
    {
        WadFile wad = new WadBuilder()
            .Lump("A", 1)
            .Markers("S_START").Lump("TROOA1", 1).Markers("S_END")
            .Markers("PP_START", "P1_START").Lump("WALL", 1).Markers("P1_END", "PP_END")
            .Markers("FF_START").Lump("FLAT", 1).Markers("F2_START").Markers("FF_END")
            .Markers("SS_START").Lump("BOSSA1", 1).Markers("SS_END")
            .Markers("F_END", "P1_START")  // stray: plain global lumps
            .Markers("F_START").Lump("LAST", 1) // unclosed: runs to end of file
            .ToWadFile();

        (string, LumpNamespace, bool)[] expected =
        {
            ("A", LumpNamespace.Global, false),
            ("S_START", LumpNamespace.Sprites, true), ("TROOA1", LumpNamespace.Sprites, false), ("S_END", LumpNamespace.Sprites, true),
            ("PP_START", LumpNamespace.Patches, true), ("P1_START", LumpNamespace.Patches, true), ("WALL", LumpNamespace.Patches, false),
            ("P1_END", LumpNamespace.Patches, true), ("PP_END", LumpNamespace.Patches, true),
            ("FF_START", LumpNamespace.Flats, true), ("FLAT", LumpNamespace.Flats, false), ("F2_START", LumpNamespace.Flats, true), ("FF_END", LumpNamespace.Flats, true),
            ("SS_START", LumpNamespace.Sprites, true), ("BOSSA1", LumpNamespace.Sprites, false), ("SS_END", LumpNamespace.Sprites, true),
            ("F_END", LumpNamespace.Global, false), ("P1_START", LumpNamespace.Global, false),
            ("F_START", LumpNamespace.Flats, true), ("LAST", LumpNamespace.Flats, false),
        };
        Assert.Equal(expected, wad.Lumps.Select(l => (l.Name, l.Namespace, l.IsMarker)));
    }

    [Fact]
    public void NamespaceEndsWithTheFile()
    {
        WadFile a = new WadBuilder().Markers("S_START").Lump("TROOA1", 1).ToWadFile("a.wad");
        WadFile b = new WadBuilder().Lump("PLAYPAL", 1).ToWadFile("b.wad");
        WadArchive archive = new(new[] { a, b });
        Assert.Equal(LumpNamespace.Global, archive.Find("PLAYPAL")!.Namespace);
    }

    [Fact]
    public void LaterFilesOverrideEarlierOnes()
    {
        WadFile iwad = new WadBuilder(WadType.Iwad)
            .Lump("PLAYPAL", 1).Lump("E1M1").Lump("THINGS", 10)
            .Markers("S_START").Lump("TROOA1", 1).Lump("TROOB1", 1).Markers("S_END")
            .Markers("F_START").Lump("NUKAGE1", 1).Lump("NUKAGE2", 1).Lump("NUKAGE3", 1).Markers("F_END")
            .ToWadFile("iwad.wad");
        WadFile pwad = new WadBuilder()
            .Lump("E1M1").Lump("THINGS", 20)
            .Markers("SS_START").Lump("TROOA1", 2).Lump("CYBRA1", 2).Markers("SS_END")
            .Markers("FF_START").Lump("NUKAGE2", 2).Markers("FF_END")
            .Lump("TROOB1", 3) // global lump that shares a sprite's name
            .ToWadFile("pwad.wad");
        WadArchive archive = new(new[] { iwad, pwad });

        Assert.Equal(iwad.Lumps.Count + pwad.Lumps.Count, archive.NumLumps);

        // Vanilla lookup: the last lump of that name wins.
        int map = archive.W_GetNumForName("E1M1");
        Assert.Same(pwad, archive.Lumps[map].File);
        Assert.Equal(new byte[] { 20 }, archive.W_CacheLumpNum(map + 1).ToArray());
        Assert.Same(iwad, archive.Find("PLAYPAL")!.File);
        Assert.Equal(new byte[] { 3 }, archive.W_CacheLumpName("TROOB1").ToArray());

        // Namespace merge: replace in place, append new names, markers excluded.
        Assert.Equal(new[] { ("TROOA1", 2), ("TROOB1", 1), ("CYBRA1", 2) },
            archive.GetNamespace(LumpNamespace.Sprites).Select(l => (l.Name, (int)l.Data.Span[0])));
        Assert.Equal(new[] { ("NUKAGE1", 1), ("NUKAGE2", 2), ("NUKAGE3", 1) },
            archive.GetNamespace(LumpNamespace.Flats).Select(l => (l.Name, (int)l.Data.Span[0])));
        Assert.Empty(archive.GetNamespace(LumpNamespace.Patches));
        Assert.Equal(1, archive.Find("TROOB1", LumpNamespace.Sprites)!.Data.Span[0]);
        Assert.Equal(3, archive.Find("TROOB1", LumpNamespace.Global)!.Data.Span[0]);
        Assert.Null(archive.Find("TROOA1", LumpNamespace.Global));
        Assert.Throws<ArgumentException>(() => archive.GetNamespace(LumpNamespace.Global));
    }

    [Fact]
    public void ArchiveNeedsAFile() =>
        Assert.Throws<ArgumentException>(() => new WadArchive(Array.Empty<WadFile>()));
}
