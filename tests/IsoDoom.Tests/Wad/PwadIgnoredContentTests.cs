using System;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Game;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Wad;

/// <summary>
/// P7 (docs/PWAD-VALIDATION.md): what the PWAD sets hold that vanilla doesn't
/// know, ignored as vanilla ignores it. Every map's line specials, sector
/// specials and thing flags outside vanilla's (<see cref="VanillaIgnores"/>,
/// the level's log line) are exactly the ones listed here; each such line
/// does nothing crossed, used or shot; and the maps no exit reaches
/// (D2TWID's MAP33, NEIS's E1M0 and E4M0) load, play, and leave by
/// <c>G_DoCompleted</c>'s arithmetic (SPEC §12 "P7"). Skips without a set or
/// its IWAD (the WAD-less half: <c>IgnoredSpecialsTests</c>).
/// </summary>
public sealed class PwadIgnoredContentTests(ITestOutputHelper output)
{
    /// <summary>The maps whose content vanilla ignores, by set, and the log line's text for each; every other map has none.</summary>
    private static readonly Dictionary<string, Dictionary<string, string>> _expected = new()
    {
        ["av"] = new()
        {
            ["MAP04"] = "thing flag 64 (Boom's not in co-op) on 24 things: 523, 524, 525, 526, ...",
            ["MAP27"] = "line special 85 (Boom's scroll right) on 5 lines: ",
        },
        ["neis"] = new()
        {
            ["E3M6"] = "line special 272 (MBF's sky transfer, flipped) on 2 lines: 4329, 4330",
            ["E3M7"] = "line special 271 (MBF's sky transfer) on 6 lines: 355, 361, 996, 997, ...",
            ["E4M6"] = "line special 307 on 1 line: 9819",
        },
    };

    private static (PwadSet Set, WadArchive Wad, GameMode Mode) Open(string folder)
    {
        PwadSet set = PwadSets.Get(folder);
        (string iwad, string[] pwads) = set.Require();
        var wad = WadArchive.Open(iwad, pwads);
        return (set, wad, IwadIdentification.D_IdentifyVersion(wad).GameMode);
    }

    private static World Load(WadArchive wad, GameMode mode, string map)
    {
        var world = new World(new SpawnSettings(mode, skill_t.sk_medium), Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(wad, map));
        return world;
    }

    [Theory]
    [MemberData(nameof(PwadSets.Folders), MemberType = typeof(PwadSets))]
    public void EveryMapsIgnoredContentIsTheListedOnes(string folder)
    {
        (PwadSet set, WadArchive wad, _) = Open(folder);
        Dictionary<string, string> expected = _expected.GetValueOrDefault(folder) ?? [];
        foreach (string map in set.Maps)
        {
            string? text = VanillaIgnores.Of(Level.Load(wad, map)).Describe();
            if (text is not null)
                output.WriteLine($"{map}: {text}");
            if (expected.TryGetValue(map, out string? start))
                Assert.True(text is not null && text.StartsWith(start, StringComparison.Ordinal), $"{map}: {text ?? "nothing"}, not {start}");
            else
                Assert.True(text is null, $"{map}: {text}");
        }
    }

    public static TheoryData<string, string> IgnoringMaps => [("av", "MAP27"), ("neis", "E3M6"), ("neis", "E3M7"), ("neis", "E4M6")];

    [Theory]
    [MemberData(nameof(IgnoringMaps))]
    public void TheirLinesDoNothing(string folder, string map)
    {
        (_, WadArchive wad, GameMode mode) = Open(folder);
        World world = Load(wad, mode, map);
        mobj_t player = world.players[0].mo!;
        List<line_t> lines = [.. world.lines.Where(l => !VanillaIgnores.IsVanillaLineSpecial((ushort)l.special))];
        Assert.NotEmpty(lines);
        ulong before = world.Checksum();
        foreach (line_t line in lines)
        {
            int special = line.special;
            world.P_CrossSpecialLine(line.Index, 0, player);
            world.P_CrossSpecialLine(line.Index, 1, player);
            Assert.True(world.P_UseSpecialLine(player, line, 0)); // vanilla's return for a player's use of a front, whatever ran
            Assert.False(world.P_UseSpecialLine(player, line, 1));
            world.P_ShootSpecialLine(player, line);
            Assert.Equal(special, line.special);
        }
        Assert.Equal(before, world.Checksum());
        Assert.Empty(world.Unported);

        // and the map plays on
        for (int tic = 0; tic < 35; tic++)
            world.G_Ticker(new ticcmd_t());
        Assert.Equal(35, world.leveltime);
    }

    // The par: none past Doom II's table (MAP33), pars[1][0] (E1M0), and episode 4's overflow into cpars, cpars[0] (E4M0).
    public static TheoryData<string, string, string, int, int> UnreachedMaps =>
        [("d2twid", "MAP33", "MAP34", 32, 0), ("neis", "E1M0", "E1M1", -1, 0), ("neis", "E4M0", "E4M1", -1, 30)];

    /// <summary>
    /// SPEC §12 "P7": a map no exit reaches loads (<c>--level</c>; MAP33
    /// also <c>idclev33</c> and the Host Game list, as vanilla's <c>-warp 33</c>),
    /// plays, and its exit goes where vanilla's arithmetic says: MAP33 to MAP34
    /// (the game's end at the title loop, where vanilla's lump lookup fails),
    /// ExM0 to ExM1 (<c>wminfo.next</c> = <c>gamemap</c> = 0), never its MAPINFO's next.
    /// </summary>
    [Theory]
    [MemberData(nameof(UnreachedMaps))]
    public void TheMapsNoExitReachesLoadPlayAndLeave(string folder, string map, string next, int last, int par)
    {
        (_, WadArchive wad, GameMode mode) = Open(folder);
        Assert.DoesNotContain(map, NetStarts.ReachableMaps(wad, mode, 1, 1).Concat(mode == GameMode.commercial ? [] : NetStarts.ReachableMaps(wad, mode, 4, 1)));
        World world = Load(wad, mode, map);
        Assert.Equal(map, world.level.Name);
        Assert.Equal(World.MapNumber(map), world.gamemap);
        Assert.NotNull(world.players[0].mo);
        for (int tic = 0; tic < 70; tic++)
            world.G_Ticker(new ticcmd_t());
        Assert.Equal(70, world.leveltime);

        world.G_ExitLevel();
        Assert.Equal(gameaction_t.ga_completed, world.gameaction);
        world.G_DoCompleted();
        Assert.False(world.G_GameEnds());
        Assert.Equal(last, world.wminfo.last);
        Assert.Equal(par * SimInfo.TICRATE, world.wminfo.partime);
        Assert.Equal(next, world.NextMapName());
        Assert.Equal(next != "MAP34", wad.W_CheckNumForName(next) >= 0);
    }

    [Theory]
    [InlineData("d2twid", new[] { "DEHACKED", "MAPINFO", "ZMAPINFO", "EMAPINFO" })]
    [InlineData("neis", new[] { "DEHACKED", "MAPINFO", "EMAPINFO" })]
    [InlineData("dtwid", new[] { "DEHACKED" })]
    public void TheWadsLogNamesItsIgnoredLumps(string folder, string[] some)
    {
        (PwadSet set, WadArchive wad, _) = Open(folder);
        IReadOnlyList<(string Name, string File)> lumps = VanillaIgnores.IgnoredLumps(wad);
        foreach (string name in some)
            Assert.Contains((name, set.Files[0]), lumps);
    }
}
