using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// P8 (docs/PWAD-VALIDATION.md, SPEC §12 "P8"): vanilla's static limits on
/// the PWAD sets' maps. Counted from the map data where the data decides it
/// (<see cref="VanillaLimits.Static"/>: the scrolling walls, the most sectors
/// one lift or ceiling line moves, the sectors they can move in all, the
/// neighbours <c>P_FindNextHighestFloor</c> lists, the brain targets, the
/// deathmatch starts, the 16-bit indices and block offsets, a short REJECT,
/// a donut's missing ring), and played where play decides it (the sets' demo
/// lumps and, opt-in, a minute of every map with every monster awake:
/// <see cref="VanillaLimits.Played"/>: the lifts, ceilings and buttons active
/// at once, the deepest <c>spechit</c> and <c>intercepts</c>). Skips without
/// a set or its IWAD.
/// </summary>
public sealed class PwadLimitsTests(ITestOutputHelper output)
{
    /// <summary>The environment variable that turns on the played survey's wake runs (a minute of every map of every set; about 2 min in all).</summary>
    public const string WakeEnvVar = "ISODOOM_PWAD_LIMITS_WAKE";

    /// <summary>
    /// The static limits each set's maps reach, by map: <see cref="VanillaLimits.Static"/>'s
    /// hits (<c>!</c> where vanilla errors or corrupts memory, <c>~</c> where it
    /// may, depending on play; the emulated quirks are not listed). Every other
    /// map reaches none.
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, string[]>> _expected = new()
    {
        ["scythe"] = new()
        {
            ["MAP29"] = ["~ next highest floor: line 1922's sector 358 has 222 neighbours > 20 (heightlist, if enough are higher when it moves)"],
            ["MAP30"] = ["~ next highest floor: line 3148's sector 492 has 31 neighbours > 20 (heightlist, if enough are higher when it moves)"],
        },
        ["av"] = new()
        {
            ["MAP10"] = ["~ next highest floor: line 6929's sector 666 has 46 neighbours > 20 (heightlist, if enough are higher when it moves)"],
        },
        ["requiem"] = new()
        {
            ["MAP08"] = ["~ next highest floor: line 2205's sector 300 has 22 neighbours > 20 (heightlist, if enough are higher when it moves)"],
            ["MAP27"] = ["! lifts: line 685 starts 260 > 30 (MAXPLATS: P_AddActivePlat's error)"],
        },
        ["hr"] = new()
        {
            ["MAP25"] = ["! next highest floor: line 488's sector 61 lists 22 higher neighbours > 20 at load (heightlist overrun, emulated: to 32, as a port without the limit)"],
        },
        ["neis"] = new()
        {
            ["E3M7"] =
            [
                "~ lifts: 16 perpetual lift sectors and line 1033's 22 > 30 (MAXPLATS: P_AddActivePlat's error when it is crossed with the perpetual lifts running)",
                "~ next highest floor: line 1681's sector 266 has 24 neighbours > 20 (heightlist, if enough are higher when it moves)",
            ],
            ["E4M7"] = ["! blockmap: block offsets to 32937 > 32767, read as negative"],
            ["E4M8"] = ["! next highest floor: line 3755's sector 299 lists 22 higher neighbours > 20 at load (heightlist overrun, emulated: to -224, as a port without the limit)"],
        },
    };

    /// <summary>
    /// The deepest <c>spechit</c> (past <see cref="World.MAXSPECIALCROSS"/>) of
    /// <see cref="EveryMapAwakeOverflowsNoTable"/>'s runs, by set and map; no other
    /// table overflows there. Scythe MAP30's (14) makes the sim leave vanilla's
    /// tics: Chocolate Doom's <c>SpechitOverrun</c>, which the reference runs and
    /// the sim does not (SPEC §12 T4.4, "P8"; T4.4a).
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, int>> _awakeSpechit = new()
    {
        ["scythe"] = new() { ["MAP18"] = 12, ["MAP30"] = 14 },
        ["av"] = new() { ["MAP15"] = 9, ["MAP21"] = 10 },
        ["requiem"] = new() { ["MAP31"] = 17 },
    };

    private static (PwadSet Set, WadArchive Wad, GameMode Mode) Open(string folder)
    {
        PwadSet set = PwadSets.Get(folder);
        (string iwad, string[] pwads) = set.Require();
        var wad = WadArchive.Open(iwad, pwads);
        return (set, wad, IwadIdentification.D_IdentifyVersion(wad).GameMode);
    }

    [Theory]
    [MemberData(nameof(PwadSets.Folders), MemberType = typeof(PwadSets))]
    public void EveryMapsStaticLimitsAreTheListedOnes(string folder)
    {
        (PwadSet set, WadArchive wad, GameMode mode) = Open(folder);
        Dictionary<string, string[]> expected = _expected.GetValueOrDefault(folder) ?? [];
        var wrong = new List<string>();
        foreach (string map in set.Maps)
        {
            VanillaLimits.Report report = VanillaLimits.Static(wad, mode, map);
            output.WriteLine($"{map}: {report.Measures}");
            string[] hits = [.. report.Hits];
            foreach (string hit in hits)
                output.WriteLine($"  {hit}");
            string[] want = expected.GetValueOrDefault(map) ?? [];
            if (!hits.SequenceEqual(want))
                wrong.Add($"{map}: [{string.Join("; ", hits)}], not [{string.Join("; ", want)}]");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    /// <summary>The sets' demo lumps (P6's), played as P6 plays them: no table a demo fills overflows in vanilla.</summary>
    [Theory]
    [MemberData(nameof(PwadDemoTests.Demos), MemberType = typeof(PwadDemoTests))]
    public void TheDemosOverflowNoTable(string set, string demo)
    {
        VanillaRoute route = VanillaRoute.Demo(demo, PwadSets.Get(set));
        var peaks = new VanillaLimits.Peaks();
        foreach ((_, World world, _, _, _) in route.Play(null))
            peaks.Sample(world);
        output.WriteLine($"{set} {demo} ({route.Map}, {route.Cmds.Count} tics): {peaks}");
        Assert.True(peaks.Hits.Count == 0, string.Join("; ", peaks.Hits));
    }

    /// <summary>
    /// Requiem MAP27's line 685, a switched lift (62) with tag 0, lowers every
    /// sector of tag 0 (vanilla's <c>P_FindSectorFromLineTag</c> matches it):
    /// the 31st is <c>P_AddActivePlat</c>'s error, in the sim as in the
    /// reference (a route beside it, used: <c>P_AddActivePlat: no more plats!</c>
    /// on its third tic). T5.5a decides what the game does.
    /// </summary>
    [Fact]
    public void RequiemMap27sTagZeroLiftErrs()
    {
        (_, WadArchive wad, GameMode mode) = Open("requiem");
        World world = Load(wad, mode, "MAP27");
        line_t line = world.lines[685];
        Assert.Equal((62, 0), (line.special, line.tag));
        var e = Assert.Throws<InvalidOperationException>(() => world.P_UseSpecialLine(world.players[0].mo!, line, 0));
        Assert.Equal("P_AddActivePlat: no more plats!", e.Message);
        Assert.Equal(Plat.MAXPLATS, world.activeplats.Count(p => p is not null));
    }

    /// <summary>
    /// NEIS E3M7: its perpetual lifts (tag 63's five 87 lines, 15 sectors, and
    /// line 3648's 53, one) stay listed once started, so line 1033's 22
    /// raise-and-change lifts (tag 10) then overflow <c>activeplats</c>: an error
    /// in vanilla's order of play, none the other way round.
    /// </summary>
    [Fact]
    public void NeisE3M7sPerpetualLiftsAndLine1033OverflowMaxplats()
    {
        (_, WadArchive wad, GameMode mode) = Open("neis");
        World world = Load(wad, mode, "E3M7");
        mobj_t player = world.players[0].mo!;
        world.P_CrossSpecialLine(1033, 0, player);
        Assert.Equal(22, world.activeplats.Count(p => p is not null));

        world = Load(wad, mode, "E3M7");
        player = world.players[0].mo!;
        world.P_CrossSpecialLine(6879, 0, player);
        world.P_CrossSpecialLine(3648, 0, player);
        Assert.Equal(16, world.activeplats.Count(p => p is not null));
        world.G_Ticker(new ticcmd_t());
        var e = Assert.Throws<InvalidOperationException>(() => world.P_CrossSpecialLine(1033, 0, player));
        Assert.Equal("P_AddActivePlat: no more plats!", e.Message);
    }

    /// <summary>
    /// NEIS E4M7's BLOCKMAP is 65,888 bytes: the lists of its 58 top blocks
    /// start past offset 32767, which vanilla reads as a negative short (the
    /// reference crashes as soon as the player moves there: a route from
    /// (615, 4336) into line 380, signal 11 on its third tic). The sim reads
    /// them unsigned (SPEC §12 T4.3) and plays on: the player walks into the
    /// wall there and slides along it.
    /// </summary>
    [Fact]
    public void NeisE4M7sFarBlocksPlayUnsigned()
    {
        (_, WadArchive wad, GameMode mode) = Open("neis");
        World world = Load(wad, mode, "E4M7");
        Blockmap bmap = world.level.Blockmap;
        int far = 0;
        for (int b = 0; b < bmap.BmapWidth * bmap.BmapHeight; b++)
        {
            if ((ushort)bmap.BlockmapLump[Blockmap.HeaderSize + b] > short.MaxValue)
                far++;
        }
        Assert.Equal(58, far);
        mobj_t player = world.players[0].mo!;
        world.PlaceMobj(player, 615 << Fixed.FRACBITS, 4336 << Fixed.FRACBITS, (uint)(98L * 0x100000000L / 360));
        for (int tic = 0; tic < 105; tic++)
            world.G_Ticker(new ticcmd_t { forwardmove = 25, sidemove = (sbyte)(tic < 35 ? 0 : 20) });
        Assert.InRange(player.y >> Fixed.FRACBITS, 4280, 4664); // still among the far blocks' rows
        Assert.NotEqual(615, player.x >> Fixed.FRACBITS);
    }

    /// <summary>The scan on the synthetic IWAD's maps (no WAD needed): no limit, and its measures.</summary>
    [Fact]
    public void TheSyntheticIwadReachesNoLimit()
    {
        var wad = new WadArchive([WadFile.FromBytes(IsoDoom.Tools.SyntheticIwad.SyntheticIwad.Build(), IsoDoom.Tools.SyntheticIwad.SyntheticIwad.DefaultFileName)]);
        foreach (string map in new[] { "E1M1", "E1M2" })
        {
            VanillaLimits.Report report = VanillaLimits.Static(wad, GameMode.shareware, map);
            output.WriteLine($"{map}: {report.Measures}");
            Assert.Empty(report.Hits);
            Assert.Contains("deathmatch starts 4", report.Measures, StringComparison.Ordinal);
        }
    }

    private static World Load(WadArchive wad, GameMode mode, string map)
    {
        var world = new World(new SpawnSettings(mode, skill_t.sk_hard), Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(wad, map));
        return world;
    }

    /// <summary>
    /// Opt-in (<see cref="WakeEnvVar"/>): a minute of every map of the set at
    /// UV with every monster awake and hunting a player in god mode who stands
    /// still (the level script's <c>wake</c>): what the monsters trigger
    /// (lifts 10 and 88, doors, teleports) and the traces and moves of a map's
    /// whole population. Prints each map's peaks; fails unless the tables
    /// vanilla overflows are <see cref="_awakeSpechit"/>'s.
    /// </summary>
    [Theory]
    [MemberData(nameof(PwadSets.Folders), MemberType = typeof(PwadSets))]
    public void EveryMapAwakeOverflowsNoTable(string folder)
    {
        if (Environment.GetEnvironmentVariable(WakeEnvVar) != "1")
            Assert.Skip($"set {WakeEnvVar}=1 to play every map a minute with every monster awake.");
        (PwadSet set, WadArchive wad, GameMode mode) = Open(folder);
        Dictionary<string, int> spechit = _awakeSpechit.GetValueOrDefault(folder) ?? [];
        var wrong = new List<string>();
        foreach (string map in set.Maps)
        {
            VanillaLimits.Peaks peaks = VanillaLimits.Played(wad, mode, map, 60 * SimInfo.TICRATE);
            output.WriteLine($"{map}: {peaks}");
            string[] want = spechit.TryGetValue(map, out int deepest) ? [$"spechit: {deepest} > {World.MAXSPECIALCROSS} (MAXSPECIALCROSS)"] : [];
            if (!peaks.Hits.SequenceEqual(want))
                wrong.Add($"{map}: [{string.Join("; ", peaks.Hits)}], not [{string.Join("; ", want)}]");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }
}

/// <summary>
/// P8: vanilla's fixed tables and 16-bit fields against a map, as the DOS
/// executables (and the Chocolate Doom based reference, <c>tools/VanillaRef</c>)
/// have them (SPEC §12 "P8").
/// </summary>
public static class VanillaLimits
{
    /// <summary>The line specials that start lifts (<c>EV_DoPlat</c>): crossed, shot and used.</summary>
    public static readonly int[] PlatSpecials = [10, 22, 53, 87, 88, 95, 120, 121, 47, 14, 15, 20, 21, 62, 66, 67, 68, 122, 123];

    /// <summary>The perpetual lifts' (53, 87): listed until the level ends, stopped or not.</summary>
    public static readonly int[] PerpetualPlatSpecials = [53, 87];

    /// <summary>The line specials that start ceilings (<c>EV_DoCeiling</c>).</summary>
    public static readonly int[] CeilingSpecials = [6, 25, 40, 44, 72, 73, 77, 141, 41, 43, 49];

    /// <summary>The crushers' (listed until the level ends, stopped or not).</summary>
    public static readonly int[] CrusherSpecials = [6, 25, 49, 73, 77, 141];

    /// <summary>The line specials whose movers call <c>P_FindNextHighestFloor</c> (lifts raiseToNearestAndChange, floors raiseFloorToNearest and raiseFloorTurbo).</summary>
    public static readonly int[] NextHighestFloorSpecials = [20, 22, 47, 68, 95, 18, 69, 119, 128, 129, 130, 131, 132];

    /// <summary>The donut (<c>EV_DoDonut</c>).</summary>
    public const int DonutSpecial = 9;

    /// <summary>One map's measures and the limits they reach.</summary>
    public sealed record Report(string Measures, IReadOnlyList<string> Hits);

    /// <summary>The map's static measures (from its data, at load) and the limits they reach.</summary>
    public static Report Static(WadArchive wad, GameMode mode, string map)
    {
        Level level = Level.Load(wad, map);
        var hits = new List<string>();
        var m = new List<string>();
        CultureInfo inv = CultureInfo.InvariantCulture;

        // 16-bit fields: vanilla reads the indices and block offsets as signed shorts.
        (string Name, int Count)[] counts =
        [
            ("vertexes", level.Vertexes.Length), ("sides", level.Sides.Length), ("lines", level.Lines.Length),
            ("sectors", level.Sectors.Length), ("segs", level.Segs.Length), ("things", level.Things.Length),
        ];
        m.Add(string.Join(' ', counts.Select(c => $"{c.Name} {c.Count}")));
        foreach ((string name, int count) in counts.Where(c => c.Name != "things" && c.Count > short.MaxValue + 1))
            hits.Add($"! {name}: {count} > 32768, past a signed short index");
        ReadOnlySpan<short> bmap = level.Blockmap.BlockmapLump;
        int blocks = level.Blockmap.BmapWidth * level.Blockmap.BmapHeight;
        int maxOffset = 0;
        for (int b = 0; b < blocks; b++)
            maxOffset = Math.Max(maxOffset, (ushort)bmap[Blockmap.HeaderSize + b]);
        m.Add(string.Create(inv, $"blockmap {bmap.Length * 2} bytes, offsets to {maxOffset}"));
        if (maxOffset > short.MaxValue)
            hits.Add($"! blockmap: block offsets to {maxOffset} > 32767, read as negative");

        int lumpnum = wad.W_CheckNumForName(map);
        int rejectLength = wad.W_CacheLumpNum(lumpnum + Level.ML_REJECT).Length;
        int rejectWant = (level.Sectors.Length * level.Sectors.Length + 7) / 8;
        if (rejectLength < rejectWant)
            m.Add(string.Create(inv, $"REJECT {rejectLength} < {rejectWant} (padded, emulated)"));

        // Scrolling walls: Chocolate Doom's (and the sim's) error past 64, vanilla's overrun.
        int scrollers = level.Lines.Count(l => l.Special == 48);
        m.Add($"scrollers {scrollers}");
        if (scrollers > World.MAXLINEANIMS)
            hits.Add($"! scrollers: {scrollers} > {World.MAXLINEANIMS} (MAXLINEANIMS)");

        // The tagged movers: sectors with a line's tag (tag 0 too: vanilla's P_FindSectorFromLineTag matches it).
        ILookup<int, int> byTag = level.Sectors.Select((s, i) => (s.Tag, i)).ToLookup(t => (int)t.Tag, t => t.i);
        (int Most, int MostLine, int Sectors) Movers(int[] specials, HashSet<int>? besides = null)
        {
            int most = 0, mostLine = -1;
            var all = new HashSet<int>();
            foreach (Line line in level.Lines.Where(l => specials.Contains(l.Special)))
            {
                int n = byTag[line.Tag].Count(s => besides is null || !besides.Contains(s));
                if (n > most)
                    (most, mostLine) = (n, line.Index);
                all.UnionWith(byTag[line.Tag]);
            }
            return (most, mostLine, all.Count);
        }
        HashSet<int> Sectors(int[] specials) =>
            [.. level.Lines.Where(l => specials.Contains(l.Special)).SelectMany(l => byTag[l.Tag])];

        // Lifts: a line starts one per sector of its tag (P_AddActivePlat errors past MAXPLATS); the perpetual
        // ones stay listed (stopped or not), so they and the most any other line starts besides them can run at once.
        HashSet<int> perpetual = Sectors(PerpetualPlatSpecials);
        (int platMost, int platLine, int platSectors) = Movers(PlatSpecials);
        (int platBesides, int platBesidesLine, _) = Movers(PlatSpecials, perpetual);
        m.Add($"lifts: most {platMost} (line {platLine}), {platSectors} sectors, {perpetual.Count} perpetual (+ {platBesides}, line {platBesidesLine})");
        if (platMost > Plat.MAXPLATS)
            hits.Add($"! lifts: line {platLine} starts {platMost} > {Plat.MAXPLATS} (MAXPLATS: P_AddActivePlat's error)");
        else if (perpetual.Count + platBesides > Plat.MAXPLATS)
            hits.Add($"~ lifts: {perpetual.Count} perpetual lift sectors and line {platBesidesLine}'s {platBesides} > {Plat.MAXPLATS} (MAXPLATS: P_AddActivePlat's error when it is crossed with the perpetual lifts running)");

        // Ceilings: past MAXCEILINGS one is not listed (it can't be stopped, and one that ends keeps its sector busy).
        HashSet<int> crushers = Sectors(CrusherSpecials);
        (int ceilMost, int ceilLine, int ceilSectors) = Movers(CeilingSpecials);
        (int ceilBesides, int ceilBesidesLine, _) = Movers(CeilingSpecials, crushers);
        m.Add($"ceilings: most {ceilMost} (line {ceilLine}), {ceilSectors} sectors, {crushers.Count} crushers (+ {ceilBesides}, line {ceilBesidesLine})");
        if (ceilMost > CeilingMove.MAXCEILINGS)
            hits.Add($"! ceilings: line {ceilLine} starts {ceilMost} > {CeilingMove.MAXCEILINGS} (MAXCEILINGS: the rest unlisted)");
        else if (crushers.Count + ceilBesides > CeilingMove.MAXCEILINGS)
            hits.Add($"~ ceilings: {crushers.Count} crusher sectors and line {ceilBesidesLine}'s {ceilBesides} > {CeilingMove.MAXCEILINGS} (MAXCEILINGS: the rest unlisted)");

        // P_FindNextHighestFloor's heightlist, at load: Chocolate Doom's emulation (the sim's) of the
        // DOS executables' stack overrun: the 21st entry past MAX_ADJOINING_SECTORS raises the height
        // compared against, the 22nd crashes.
        int worstH = 0, worstAll = 0, worstSector = -1, worstLine = -1, worstResult = 0, worstUnlimited = 0;
        bool crash = false;
        foreach (Line line in level.Lines.Where(l => NextHighestFloorSpecials.Contains(l.Special)))
        {
            foreach (int s in byTag[line.Tag])
            {
                (int h, int all, bool fatal, int result, int unlimited) = HeightList(level.Sectors[s]);
                if (fatal && !crash)
                    (crash, worstH, worstAll, worstSector, worstLine) = (true, h, all, s, line.Index);
                else if (!crash && (h > worstH || (h == worstH && all > worstAll)))
                    (worstH, worstAll, worstSector, worstLine, worstResult, worstUnlimited) = (h, all, s, line.Index, result, unlimited);
            }
        }
        m.Add($"next highest floor: {worstH} higher of {worstAll} (sector {worstSector}, line {worstLine})");
        if (crash)
            hits.Add($"! next highest floor: line {worstLine}'s sector {worstSector} has more than 22 higher neighbours at load (Chocolate Doom's error: vanilla crashes)");
        else if (worstH > World.MAX_ADJOINING_SECTORS)
            hits.Add($"! next highest floor: line {worstLine}'s sector {worstSector} lists {worstH} higher neighbours > 20 at load (heightlist overrun, emulated: to {worstResult >> Fixed.FRACBITS}, {(worstResult == worstUnlimited ? "as" : $"{worstUnlimited >> Fixed.FRACBITS} in")} a port without the limit)");
        else if (worstAll > World.MAX_ADJOINING_SECTORS)
            hits.Add($"~ next highest floor: line {worstLine}'s sector {worstSector} has {worstAll} neighbours > 20 (heightlist, if enough are higher when it moves)");

        // Things: the brain targets (braintargets[32], unchecked) and deathmatch starts (the first 10 kept).
        int targets = level.Things.Count(t => t.Type == 87 && (t.Options & MapThing.MTF_HARD) != 0 && (t.Options & MapThingSpawning.MTF_NOTSINGLE) == 0);
        int dmstarts = level.Things.Count(t => t.Type == 11);
        m.Add($"brain targets {targets}, deathmatch starts {dmstarts}");
        if (targets > 32)
            hits.Add($"! brain targets: {targets} > 32 (braintargets[], unchecked)");
        if (dmstarts > MapThingSpawning.MAX_DM_STARTS)
            m.Add($"deathmatch starts past {MapThingSpawning.MAX_DM_STARTS} ignored (emulated)");

        // The donut: s3 is null when the ring's first line not to the hole is one-sided (or has the ring behind it).
        foreach (Line line in level.Lines.Where(l => l.Special == DonutSpecial))
        {
            foreach (int s in byTag[line.Tag])
            {
                Sector s1 = level.Sectors[s];
                if (s1.Lines.Count == 0)
                    continue;
                Line first = s1.Lines[0];
                Sector? s2 = first.FrontSector == s1 ? first.BackSector : first.FrontSector;
                if (first.BackSector is null || s2 is null)
                {
                    hits.Add($"! donut: line {line.Index}'s sector {s} has no ring (vanilla dereferences null)");
                    continue;
                }
                Line? ringLine = s2.Lines.FirstOrDefault(l => l.BackSector != s1);
                if (ringLine is not null && ringLine.BackSector is null)
                    m.Add($"donut line {line.Index}: sector {s}'s ring has no outer sector (DonutOverrun, emulated)");
            }
        }

        _ = mode;
        return new Report(string.Join("; ", m), hits);
    }

    // P_FindNextHighestFloor's list for the sector's floor at load: its length, the two-sided lines, whether it
    // crashes, and its result (fixed_t) against the lowest higher neighbour of all (a limit-removing port's).
    private static (int H, int All, bool Fatal, int Result, int Unlimited) HeightList(Sector sec)
    {
        int height = sec.FloorHeight, h = 0, all = 0, result = int.MaxValue, unlimited = int.MaxValue;
        foreach (Line l in sec.Lines)
        {
            if ((l.Flags & Line.ML_TWOSIDED) == 0)
                continue;
            Sector? other = l.FrontSector == sec ? l.BackSector : l.FrontSector;
            if (other is null)
                continue;
            all++;
            if (other.FloorHeight > sec.FloorHeight)
                unlimited = Math.Min(unlimited, other.FloorHeight);
            if (other.FloorHeight > height)
            {
                if (h == World.MAX_ADJOINING_SECTORS + 1)
                    height = other.FloorHeight;
                else if (h == World.MAX_ADJOINING_SECTORS + 2)
                    return (h + 1, all, true, 0, unlimited);
                result = Math.Min(result, other.FloorHeight);
                h++;
            }
        }
        return h == 0 ? (0, all, false, sec.FloorHeight, sec.FloorHeight) : (h, all, false, result, unlimited);
    }

    /// <summary>The peaks of the tables play fills, sampled after each tic.</summary>
    public sealed class Peaks
    {
        public int Tics { get; private set; }
        public int Plats { get; private set; }
        public int Ceilings { get; private set; }
        public int Unlisted { get; private set; }
        public int Buttons { get; private set; }
        public int Spechit { get; private set; }
        public int Intercepts { get; private set; }
        public int BrainTargets { get; private set; }
        public string? Error { get; set; }

        public void Sample(World world)
        {
            Tics++;
            Plats = Math.Max(Plats, world.activeplats.Count(p => p is not null));
            Ceilings = Math.Max(Ceilings, world.activeceilings.Count(c => c is not null));
            int unlisted = 0;
            for (thinker_t th = world.thinkercap.next; th != world.thinkercap; th = th.next)
            {
                if (th is ceiling_t c && th.function != think_t.REMOVED && Array.IndexOf(world.activeceilings, c) < 0)
                    unlisted++;
            }
            Unlisted = Math.Max(Unlisted, unlisted);
            Buttons = Math.Max(Buttons, world.buttonlist.Count(b => b.btimer != 0));
            Spechit = Math.Max(Spechit, world.spechitpeak);
            Intercepts = Math.Max(Intercepts, world.interceptpeak);
            BrainTargets = Math.Max(BrainTargets, world.numbraintargets);
        }

        /// <summary>The tables vanilla overflowed (or would have).</summary>
        public IReadOnlyList<string> Hits
        {
            get
            {
                var hits = new List<string>();
                if (Error is not null)
                    hits.Add(Error);
                if (Ceilings >= CeilingMove.MAXCEILINGS && Unlisted > 0)
                    hits.Add($"ceilings: {Unlisted} unlisted past {CeilingMove.MAXCEILINGS} (MAXCEILINGS)");
                if (Spechit > World.MAXSPECIALCROSS)
                    hits.Add($"spechit: {Spechit} > {World.MAXSPECIALCROSS} (MAXSPECIALCROSS)");
                if (Intercepts > World.MAXINTERCEPTS)
                    hits.Add($"intercepts: {Intercepts} > {World.MAXINTERCEPTS} (MAXINTERCEPTS)");
                if (BrainTargets > 32)
                    hits.Add($"brain targets: {BrainTargets} > 32");
                return hits;
            }
        }

        public override string ToString() => string.Create(CultureInfo.InvariantCulture,
            $"{Tics} tics: lifts {Plats}, ceilings {Ceilings} (unlisted {Unlisted}), buttons {Buttons}, spechit {Spechit}, intercepts {Intercepts}, brain targets {BrainTargets}{(Error is null ? "" : ", " + Error)}");
    }

    /// <summary>
    /// <paramref name="tics"/> tics of <paramref name="map"/> at UV with every
    /// monster awake and hunting the player (the level script's <c>wake</c>),
    /// who stands still in god mode; the peaks after each tic. A vanilla error
    /// (<c>P_AddActivePlat</c>'s, <c>P_StartButton</c>'s, the 23rd neighbour's)
    /// ends the run and is the result's <see cref="Peaks.Error"/>.
    /// </summary>
    public static Peaks Played(WadArchive wad, GameMode mode, string map, int tics)
    {
        var peaks = new Peaks();
        World world;
        try
        {
            world = new World(new SpawnSettings(mode, skill_t.sk_hard), Tweaks.Vanilla);
            world.G_DoLoadLevel(Level.Load(wad, map));
        }
        catch (WadFormatException e)
        {
            peaks.Error = "load: " + e.Message;
            return peaks;
        }
        player_t player = world.players[0];
        player.cheats |= player_t.CF_GODMODE;
        if (player.mo is mobj_t pmo)
        {
            foreach (mobj_t m in world.Mobjs().ToList())
            {
                if ((m.flags & mobjflag_t.MF_COUNTKILL) == 0 || m.health <= 0)
                    continue;
                m.flags &= ~mobjflag_t.MF_AMBUSH;
                m.target = pmo;
                if (m.state == m.info.spawnstate)
                    world.P_SetMobjState(m, m.info.seestate);
            }
        }
        try
        {
            for (int tic = 0; tic < tics && world.gameaction == gameaction_t.ga_nothing; tic++)
            {
                world.G_Ticker(new ticcmd_t());
                peaks.Sample(world);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or WadFormatException)
        {
            peaks.Error = $"tic {world.leveltime}: {e.Message}";
        }
        return peaks;
    }
}
