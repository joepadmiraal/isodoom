using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IsoDoom.Game;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Wad;

/// <summary>
/// P1 (docs/PWAD-VALIDATION.md): each PWAD set opened over its IWAD as the game
/// opens it (<see cref="WadArchive"/>, <see cref="IwadIdentification"/>,
/// <see cref="ModifiedGame.D_CheckModifiedGame"/>): the maps the game lists
/// (<see cref="LumpDirectory.FindMaps"/>, the level scene's <c>MapNames</c>)
/// and those its exits reach (<see cref="NetStarts.ReachableMaps"/>, g_game.c
/// <c>G_DoCompleted</c>'s order, as <c>GameFlow</c> follows it), each map's
/// elements, and the lump kinds the sets add. Skips without a set or its IWAD.
/// </summary>
public sealed class PwadInventoryTests(ITestOutputHelper output)
{
    // doomdata.h ML_THINGS … ML_BLOCKMAP, and the record sizes p_setup.c reads them by (0: not a record lump).
    private static readonly (string Name, int Size)[] _elements =
    [
        ("THINGS", 10), ("LINEDEFS", 14), ("SIDEDEFS", 30), ("VERTEXES", 4), ("SEGS", 12),
        ("SSECTORS", 4), ("NODES", 28), ("SECTORS", 26), ("REJECT", 0), ("BLOCKMAP", 0),
    ];

    /// <summary>What the table and a look at the files say each set adds, by lump kind.</summary>
    private sealed record Expected(
        int Songs, int MidiSongs, int Sprites, int Demos, bool Palette, bool Texture2,
        string[] Graphics, string[] Ignored, string[] Unreachable);

    private static Expected ExpectedFor(string folder) => folder switch
    {
        "scythe" => new(35, 1, 0, 3, false, false, ["TITLEPIC", "INTERPIC"], ["_DEUTEX_"], []),
        // AV: four flats repeated outside F_START/F_END (copies of ones inside)
        "av" => new(35, 35, 0, 3, false, false, ["TITLEPIC", "INTERPIC", "STBAR"], ["STEP1", "STEP2", "GRNROCK", "SLIME03", "_DEUTEX_"], []),
        "mm" => new(35, 0, 0, 3, false, false, ["INTERPIC"], [], []),
        "d2twid" => new(4, 1, 63, 3, true, false, [], ["DEHACKED", "ANIMDEFS", "MAPINFO", "ZMAPINFO", "EMAPINFO"], ["MAP33"]),
        "requiem" => new(35, 0, 0, 3, false, false, ["TITLEPIC", "INTERPIC", "STBAR"], ["TAGDESC", "_DEUTEX_"], []),
        // HR: HRLOGO is a PCX image
        "hr" => new(26, 0, 0, 6, false, false, ["TITLEPIC", "INTERPIC", "STBAR"], ["HRLOGO"], []),
        // DTWID: IN_EPI2 is a ZDoom intermission script
        "dtwid" => new(1, 0, 0, 3, false, true, ["TITLEPIC"], ["DEHACKED", "IN_EPI2"], []),
        "neis" => new(1, 0, 0, 4, false, true, ["TITLEPIC", "INTERPIC", "STBAR"],
            ["DEHACKED", "MAPINFO", "EMAPINFO", "GAMEINFO", "ZDSPEC", "EESPEC", "CREDITS"], ["E1M0", "E4M0"]),
        _ => throw new ArgumentException(folder, nameof(folder)),
    };

    private static (PwadSet Set, WadArchive Wad, IwadInfo Info) Open(string folder)
    {
        PwadSet set = PwadSets.Get(folder);
        (string iwad, string[] pwads) = set.Require();
        var wad = WadArchive.Open(iwad, pwads);
        return (set, wad, IwadIdentification.D_IdentifyVersion(wad));
    }

    private static bool InPwad(WadArchive wad, WadLump lump) => lump.File != wad.Files[0];

    [Theory]
    [MemberData(nameof(PwadSets.Folders), MemberType = typeof(PwadSets))]
    public void OpensOverItsIwadAsAModifiedGame(string folder)
    {
        (PwadSet set, WadArchive wad, IwadInfo info) = Open(folder);
        Assert.Equal(set.Iwad == PwadIwad.Doom2 ? GameMode.commercial : GameMode.retail, info.GameMode);
        Assert.Equal(set.Files.Count + 1, wad.Files.Count);
        Assert.Equal(set.Files, wad.Files.Skip(1).Select(f => f.Name));
        Assert.True(ModifiedGame.modifiedgame(wad));
        ModifiedGame.D_CheckModifiedGame(wad, info); // throws when the game would refuse to start
    }

    [Theory]
    [MemberData(nameof(PwadSets.Folders), MemberType = typeof(PwadSets))]
    public void ListsTheTablesMapsInTheGamesOrder(string folder)
    {
        (PwadSet set, WadArchive wad, IwadInfo info) = Open(folder);
        IReadOnlyList<string> listed = LumpDirectory.FindMaps(wad);
        output.WriteLine($"{folder}: {listed.Count} maps: {string.Join(" ", listed)}");

        // The set's own maps are the table's, and each name resolves to the set's copy.
        var pwadMaps = Enumerable.Range(0, wad.NumLumps - 1)
            .Where(i => InPwad(wad, wad.Lumps[i]) && LumpDirectory.IsMapName(wad.Lumps[i].Name) && wad.Lumps[i + 1].Name == "THINGS")
            .Select(i => wad.Lumps[i].Name).Distinct().Order(StringComparer.Ordinal).ToList();
        Assert.Equal(set.Maps.Order(StringComparer.Ordinal), pwadMaps);
        foreach (string map in set.Maps)
            Assert.True(InPwad(wad, wad.Lumps[wad.W_CheckNumForName(map)]), $"{map} resolves to the IWAD's");

        // The game lists the IWAD's maps in its order, then the set's new ones in the set's order.
        IReadOnlyList<string> iwadMaps = LumpDirectory.FindMaps(new WadArchive([wad.Files[0]]));
        Assert.Equal([.. iwadMaps, .. set.Maps.Except(iwadMaps)], listed);

        // GameFlow's order: the exits from each episode's first map (Doom II: MAP01) reach every
        // listed map but those vanilla has no way to (P7: --level reaches them, SPEC §12 "P7").
        var reached = new List<string>();
        if (info.GameMode == GameMode.commercial)
        {
            IReadOnlyList<string> order = NetStarts.ReachableMaps(wad, info.GameMode, 1, 1);
            Assert.Equal([.. Enumerable.Range(1, 30).Select(m => $"MAP{m:00}"), "MAP31", "MAP32"], order);
            reached.AddRange(order);
        }
        else
        {
            for (int e = 1; e <= 4; e++)
            {
                IReadOnlyList<string> order = NetStarts.ReachableMaps(wad, info.GameMode, e, 1);
                Assert.Equal(Enumerable.Range(1, 9).Select(m => $"E{e}M{m}"), order);
                reached.AddRange(order);
            }
        }
        Assert.Equal(ExpectedFor(folder).Unreachable, listed.Except(reached));
    }

    [Theory]
    [MemberData(nameof(PwadSets.Folders), MemberType = typeof(PwadSets))]
    public void EachMapHasItsElements(string folder)
    {
        (PwadSet set, WadArchive wad, _) = Open(folder);
        foreach (string map in set.Maps)
        {
            int header = wad.W_CheckNumForName(map);
            WadFile file = wad.Lumps[header].File;
            for (int ml = 0; ml < _elements.Length; ml++)
            {
                (string name, int size) = _elements[ml];
                Assert.True(header + 1 + ml < wad.NumLumps, $"{map}: no {name}");
                WadLump lump = wad.Lumps[header + 1 + ml];
                Assert.True(lump.Name == name, $"{map}: lump {ml + 1} after the header is {lump.Name}, not {name}");
                Assert.True(lump.File == file, $"{map}: {name} is in {lump.File.Name}, the header in {file.Name}");
                if (size > 0)
                {
                    Assert.True(lump.Size > 0, $"{map}: {name} is empty");
                    Assert.True(lump.Size % size == 0, $"{map}: {name} is {lump.Size} bytes, not whole {size}-byte records");
                }
            }
            Assert.True(wad.Lumps[header + 10].Size > 0, $"{map}: BLOCKMAP is empty");
        }
    }

    [Theory]
    [MemberData(nameof(PwadSets.Folders), MemberType = typeof(PwadSets))]
    public void AddsTheLumpKindsTheTableNames(string folder)
    {
        (PwadSet set, WadArchive wad, _) = Open(folder);
        Expected expected = ExpectedFor(folder);
        List<LumpEntry> entries = [.. LumpDirectory.Build(wad).Where(e => InPwad(wad, e.Lump))];
        foreach (IGrouping<LumpKind, LumpEntry> kind in entries.GroupBy(e => e.Kind).OrderBy(g => g.Key))
            output.WriteLine($"{kind.Key}: {kind.Count()}");
        List<LumpEntry> Of(LumpKind kind) => [.. entries.Where(e => e.Kind == kind)];

        int maps = set.Maps.Count + (folder == "av" ? 1 : 0); // AV.WAD's MAP20 and AVMOVFIX.WAD's
        Assert.Equal(maps, Of(LumpKind.MapMarker).Count);
        Assert.Equal(maps * 10, Of(LumpKind.MapData).Count);

        List<LumpEntry> songs = Of(LumpKind.Music);
        Assert.Equal(expected.Songs, songs.Count);
        Assert.All(songs, s => Assert.StartsWith("D_", s.Lump.Name, StringComparison.Ordinal));
        Assert.Equal(expected.MidiSongs, songs.Count(s => s.Detail == "MIDI"));
        Assert.All(songs.Where(s => s.Detail != "MIDI"), s => Assert.StartsWith("MUS", s.Detail, StringComparison.Ordinal));
        Assert.DoesNotContain(songs, s => s.Detail.Contains("not readable", StringComparison.Ordinal));

        Assert.Equal(expected.Sprites, Of(LumpKind.Sprite).Count);
        Assert.Equal(expected.Demos, Of(LumpKind.Demo).Count);
        Assert.Equal(expected.Palette ? 1 : 0, Of(LumpKind.Palette).Count);
        Assert.Equal(expected.Palette ? 1 : 0, Of(LumpKind.Colormap).Count);
        string[] textureDefs = expected.Texture2 ? ["PNAMES", "TEXTURE1", "TEXTURE2"] : ["PNAMES", "TEXTURE1"];
        Assert.Equal(textureDefs, Of(LumpKind.TextureDefs).Select(e => e.Lump.Name).Order(StringComparer.Ordinal));

        // The UI graphics the set replaces are the ones the game reads (a later file's wins).
        foreach (string name in new[] { "TITLEPIC", "INTERPIC", "STBAR" })
        {
            bool replaced = expected.Graphics.Contains(name);
            Assert.Equal(replaced, entries.Any(e => e.Lump.Name == name && e.Kind == LumpKind.Graphic));
            Assert.Equal(replaced, InPwad(wad, wad.Lumps[wad.W_CheckNumForName(name)]));
        }

        // What vanilla doesn't read (DEHACKED, MAPINFO, tool lumps, …): exactly these, so a new kind shows.
        Assert.Equal(expected.Ignored.Order(StringComparer.Ordinal), Of(LumpKind.Other).Select(e => e.Lump.Name).Order(StringComparer.Ordinal));
        // P7: the WAD's log line names the same (VanillaIgnores.IgnoredLumps)
        Assert.Equal(expected.Ignored.Order(StringComparer.Ordinal), IsoDoom.Map.VanillaIgnores.IgnoredLumps(wad).Select(l => l.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void MementoMorisMapsAreReversedWithJunkAfterTheNul()
    {
        (PwadSet set, WadArchive wad, _) = Open("mm");
        // The raw directory: MAP32 first, and every map name with bytes after its NUL (MAP32\0\0S).
        byte[] raw = File.ReadAllBytes(set.Paths[0]);
        int count = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(4));
        int offset = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(8));
        var rawMaps = new List<string>();
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> name = raw.AsSpan(offset + 16 * i + 8, 8);
            if (name.StartsWith("MAP"u8) && name[5] == 0)
            {
                Assert.True(name[6..].IndexOfAnyExcept((byte)0) >= 0, $"directory entry {i} has nothing after its NUL");
                rawMaps.Add(System.Text.Encoding.Latin1.GetString(name[..5]));
            }
        }
        Assert.Equal(Enumerable.Range(1, 32).Reverse().Select(m => $"MAP{m:00}"), rawMaps);

        // Read clean, listed in play order, each resolving to MM.WAD's copy with its elements after it.
        Assert.Equal(set.Maps, LumpDirectory.FindMaps(wad));
        foreach (string map in set.Maps)
        {
            int header = wad.W_CheckNumForName(map);
            Assert.Equal("MM.WAD", wad.Lumps[header].File.Name);
            Assert.Equal(map, wad.Lumps[header].Name);
            Assert.Equal("THINGS", wad.Lumps[header + 1].Name);
        }
    }

    [Fact]
    public void AvMovFixsMap20ReplacesAvs()
    {
        (_, WadArchive wad, _) = Open("av");
        int header = wad.W_CheckNumForName("MAP20");
        Assert.Equal("AVMOVFIX.WAD", wad.Lumps[header].File.Name);
        Assert.Equal(19, LumpDirectory.FindMaps(wad).ToList().IndexOf("MAP20"));
        Assert.Single(LumpDirectory.FindMaps(wad), m => m == "MAP20");

        // AV.WAD's MAP20 and every element of it are overridden by AVMOVFIX.WAD's map (the data lumps point at its header).
        List<LumpEntry> avMap20 = [.. LumpDirectory.Build(wad).Where(e => e.Lump.File.Name == "AV.WAD" && e.MapName == "MAP20")];
        Assert.Equal(11, avMap20.Count);
        Assert.All(avMap20, e => Assert.Same(wad.Lumps[header], e.OverriddenBy));
        for (int ml = 1; ml <= 10; ml++)
            Assert.Equal("AVMOVFIX.WAD", wad.Lumps[header + ml].File.Name);
    }

    [Fact]
    public void D2twidListsMap33AfterMap32()
    {
        (_, WadArchive wad, _) = Open("d2twid");
        IReadOnlyList<string> listed = LumpDirectory.FindMaps(wad);
        Assert.Equal(33, listed.Count);
        Assert.Equal("MAP33", listed[^1]);
        Assert.Equal("D2TWID.wad", wad.Lumps[wad.W_CheckNumForName("PLAYPAL")].File.Name);
        Assert.Equal("D2TWID.wad", wad.Lumps[wad.W_CheckNumForName("COLORMAP")].File.Name);
    }

    [Fact]
    public void NeisListsE1M0AndE4M0AfterTheIwadsMaps()
    {
        (_, WadArchive wad, _) = Open("neis");
        IReadOnlyList<string> listed = LumpDirectory.FindMaps(wad);
        // After the IWAD's 36 (the 2024 re-release keeps E4M1 last in its directory, so the list does too).
        Assert.Equal(38, listed.Count);
        Assert.Equal(["E1M0", "E4M0"], listed.Skip(36));
        Assert.All(listed, m => Assert.Equal("NEIS.wad", wad.Lumps[wad.W_CheckNumForName(m)].File.Name));
    }
}
