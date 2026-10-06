using System;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>T3.2: the selection part of p_mobj.c <c>P_SpawnMapThing</c> (and p_setup.c <c>P_LoadThings</c>' loop).</summary>
public class MapThingSpawnTests
{
    private static readonly SpawnSettings Medium = new(GameMode.shareware, skill_t.sk_medium);

    private static MapThingSpawn One(short type, short options, SpawnSettings settings)
    {
        int dm = 0;
        return MapThingSpawning.Select(new MapThing(1, 2, 0, type, options), settings, ref dm);
    }

    /// <summary>
    /// One line per kind and per spawned type, in enum order: "Kind n" for
    /// every kind present, then "MT_X n" for the spawned mobjs and "player n"
    /// for the player starts that spawn.
    /// </summary>
    private static string Summary(MapThingSpawn[] list)
    {
        var kinds = list.GroupBy(s => s.Kind).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}");
        var types = list.Where(s => s.Kind == MapThingSpawnKind.Mobj)
            .GroupBy(s => s.Type).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}");
        var players = list.Where(s => s.Kind == MapThingSpawnKind.PlayerStart && s.SpawnsPlayer).Select(s => $"player {s.PlayerNum + 1}");
        return string.Join("; ", kinds.Concat(types).Concat(players));
    }

    [Theory]
    [InlineData(skill_t.sk_baby, 1)]
    [InlineData(skill_t.sk_easy, 1)]
    [InlineData(skill_t.sk_medium, 2)]
    [InlineData(skill_t.sk_hard, 4)]
    [InlineData(skill_t.sk_nightmare, 4)]
    public void SkillBits(skill_t skill, int bit)
    {
        var settings = new SpawnSettings(GameMode.shareware, skill);
        for (short options = 0; options < 8; options++)
        {
            MapThingSpawn s = One(3001, options, settings);
            Assert.Equal((options & bit) != 0 ? MapThingSpawnKind.Mobj : MapThingSpawnKind.NotThisSkill, s.Kind);
        }
    }

    [Fact]
    public void LookupAmbushAndAngle()
    {
        MapThingSpawn imp = One(3001, MapThing.MTF_NORMAL | MapThing.MTF_AMBUSH, Medium);
        Assert.Equal((MapThingSpawnKind.Mobj, mobjtype_t.MT_TROOP, true), (imp.Kind, imp.Type, imp.Ambush));
        Assert.True(imp.Spawns);
        Assert.False(One(2035, 7, Medium).Ambush);
        Assert.Equal(mobjtype_t.MT_BARREL, One(2035, 7, Medium).Type);

        // ANG45 * (angle / 45): snaps down to 45° steps, C division truncates.
        int dm = 0;
        uint Angle(short a) => MapThingSpawning.Select(new MapThing(0, 0, a, 3001, 7), Medium, ref dm).Angle;
        Assert.Equal(0u, Angle(44));
        Assert.Equal(0x40000000u, Angle(90));
        Assert.Equal(0x40000000u, Angle(134));
        Assert.Equal(0xE0000000u, Angle(315));
        Assert.Equal(0u, Angle(-44));
        Assert.Equal(0xE0000000u, Angle(-45));
        Assert.Equal(0x00000000u, Angle(360));
    }

    [Fact]
    public void PlayerAndDeathmatchStarts()
    {
        for (short type = 1; type <= 4; type++)
        {
            // Player starts ignore the skill and multiplayer bits.
            MapThingSpawn p = One(type, 16, Medium);
            Assert.Equal((MapThingSpawnKind.PlayerStart, type - 1, true), (p.Kind, p.PlayerNum, p.SpawnsPlayer));
            MapThingSpawn dmp = One(type, 7, Medium with { netgame = true, deathmatch = 1 });
            Assert.Equal((MapThingSpawnKind.PlayerStart, false, false), (dmp.Kind, dmp.SpawnsPlayer, dmp.Spawns));
        }

        Assert.Equal(MapThingSpawnKind.Ignored, One(0, 7, Medium).Kind);
        Assert.Equal(MapThingSpawnKind.Ignored, One(-1, 7, Medium).Kind);

        // Only the first 10 deathmatch starts are kept, whatever the mode.
        MapThing[] starts = Enumerable.Range(0, 12).Select(i => new MapThing((short)i, 0, 0, 11, 0)).ToArray();
        MapThingSpawn[] list = MapThingSpawning.SpawnList(starts, Medium);
        Assert.All(list.Take(10), s => Assert.Equal(MapThingSpawnKind.DeathmatchStart, s.Kind));
        Assert.All(list.Skip(10), s => Assert.Equal(MapThingSpawnKind.Ignored, s.Kind));
    }

    [Fact]
    public void MultiplayerOnlyBit()
    {
        Assert.Equal(MapThingSpawnKind.NotSinglePlayer, One(2035, 7 | 16, Medium).Kind);
        Assert.Equal(MapThingSpawnKind.Mobj, One(2035, 7 | 16, Medium with { netgame = true }).Kind);
        // The bit is tested before the skill bits.
        Assert.Equal(MapThingSpawnKind.NotSinglePlayer, One(2035, 16, Medium).Kind);
        Assert.Equal(MapThingSpawnKind.NotThisSkill, One(2035, 16, Medium with { netgame = true }).Kind);
    }

    [Fact]
    public void DeathmatchAndNoMonsters()
    {
        // Keys have MF_NOTDMATCH.
        SpawnSettings dm = Medium with { netgame = true, deathmatch = 1 };
        Assert.Equal((MapThingSpawnKind.NotDeathmatch, mobjtype_t.MT_MISC4), (One(5, 7, dm).Kind, One(5, 7, dm).Type));
        Assert.Equal(MapThingSpawnKind.Mobj, One(5, 7, Medium).Kind);
        Assert.Equal(MapThingSpawnKind.Mobj, One(2035, 7, dm).Kind);

        SpawnSettings nm = Medium with { nomonsters = true };
        Assert.Equal(MapThingSpawnKind.NoMonsters, One(3001, 7, nm).Kind); // MF_COUNTKILL
        Assert.Equal(MapThingSpawnKind.NoMonsters, One(3006, 7, nm).Kind); // MT_SKULL, no MF_COUNTKILL
        Assert.Equal(MapThingSpawnKind.Mobj, One(2035, 7, nm).Kind);       // shootable but not a monster
        Assert.True(One(1, 7, nm).Spawns);                                   // players still spawn
    }

    [Fact]
    public void UnknownTypeErrorsOnlyWhenItWouldSpawn()
    {
        WadFormatException e = Assert.Throws<WadFormatException>(() => One(9999, 7, Medium));
        Assert.Equal("P_SpawnMapThing: Unknown type 9999 at (1, 2)", e.Message);
        Assert.Equal(MapThingSpawnKind.NotThisSkill, One(9999, MapThing.MTF_EASY, Medium).Kind);
        Assert.Equal(MapThingSpawnKind.NotSinglePlayer, One(9999, 7 | 16, Medium).Kind);
    }

    [Fact]
    public void Doom2MonstersStopLoadingOutsideCommercial()
    {
        MapThing[] things =
        {
            new(0, 0, 0, 1, 7),
            new(0, 0, 0, 3001, 7),
            new(0, 0, 0, 66, 7),   // revenant
            new(0, 0, 0, 2035, 7), // never reached outside commercial
        };
        MapThingSpawn[] doom1 = MapThingSpawning.SpawnList(things, Medium);
        Assert.Equal(
            new[] { MapThingSpawnKind.PlayerStart, MapThingSpawnKind.Mobj, MapThingSpawnKind.NotLoaded, MapThingSpawnKind.NotLoaded },
            doom1.Select(s => s.Kind));
        // Even on a skill the revenant isn't on.
        MapThing[] offSkill = things.Select(t => t.Type == 66 ? t with { Options = 1 } : t).ToArray();
        Assert.Equal(MapThingSpawnKind.NotLoaded, MapThingSpawning.SpawnList(offSkill, Medium)[3].Kind);

        MapThingSpawn[] doom2 = MapThingSpawning.SpawnList(things, Medium with { gamemode = GameMode.commercial });
        Assert.Equal(mobjtype_t.MT_UNDEAD, doom2[2].Type);
        Assert.All(doom2, s => Assert.True(s.Spawns));
    }

    [Theory]
    [InlineData(skill_t.sk_baby)]
    [InlineData(skill_t.sk_medium)]
    [InlineData(skill_t.sk_nightmare)]
    public void SyntheticE1M1(skill_t skill)
    {
        Level map = Level.Load(new WadArchive(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) }), "E1M1");
        MapThingSpawn[] list = MapThingSpawning.SpawnList(map.Things, new SpawnSettings(GameMode.shareware, skill));
        Assert.Equal(
            new[] { (MapThingSpawnKind.PlayerStart, mobjtype_t.NUMMOBJTYPES), (MapThingSpawnKind.Mobj, mobjtype_t.MT_BARREL), (MapThingSpawnKind.Mobj, mobjtype_t.MT_TROOP) },
            list.Select(s => (s.Kind, s.Type)));
        Assert.Equal(0x80000000u, list[2].Angle);
        Assert.Equal("Mobj 2; PlayerStart 1; MT_TROOP 1; MT_BARREL 1; player 1", Summary(list));
    }

    // Checked once against vanilla (doomgeneric at T2.9's pinned commit, the
    // thinker list after -warp 1 1 -skill N): the same mobj counts per type
    // (plus player 1's), totalkills 4/4/6/29/29 and totalitems 37/37/37/38/38.
    private const string E1M1Easy = "Mobj 89; PlayerStart 4; DeathmatchStart 5; NotSinglePlayer 14; NotThisSkill 26; MT_POSSESSED 2; MT_TROOP 2; MT_BARREL 6; MT_MISC0 1; MT_MISC1 1; MT_MISC2 12; MT_MISC3 25; MT_MISC10 1; MT_MISC11 3; MT_CLIP 2; MT_MISC17 1; MT_MISC22 2; MT_MISC23 3; MT_SHOTGUN 1; MT_MISC31 8; MT_MISC48 2; MT_MISC50 2; MT_MISC62 4; MT_MISC68 2; MT_MISC69 2; MT_MISC71 7; player 1; player 2; player 3; player 4";
    private const string E1M1Medium = "Mobj 91; PlayerStart 4; DeathmatchStart 5; NotSinglePlayer 14; NotThisSkill 24; MT_POSSESSED 4; MT_TROOP 2; MT_BARREL 6; MT_MISC0 1; MT_MISC1 1; MT_MISC2 12; MT_MISC3 25; MT_MISC10 1; MT_MISC11 3; MT_CLIP 2; MT_MISC17 1; MT_MISC22 2; MT_MISC23 3; MT_SHOTGUN 1; MT_MISC31 8; MT_MISC48 2; MT_MISC50 2; MT_MISC62 4; MT_MISC68 2; MT_MISC69 2; MT_MISC71 7; player 1; player 2; player 3; player 4";
    private const string E1M1Hard = "Mobj 115; PlayerStart 4; DeathmatchStart 5; NotSinglePlayer 14; MT_POSSESSED 9; MT_SHOTGUY 16; MT_TROOP 4; MT_BARREL 6; MT_MISC0 1; MT_MISC1 1; MT_MISC2 13; MT_MISC3 25; MT_MISC10 1; MT_MISC11 3; MT_CLIP 2; MT_MISC17 1; MT_MISC22 2; MT_MISC23 3; MT_SHOTGUN 1; MT_MISC31 8; MT_MISC48 2; MT_MISC50 2; MT_MISC62 4; MT_MISC68 2; MT_MISC69 2; MT_MISC71 7; player 1; player 2; player 3; player 4";

    [Theory]
    [InlineData(skill_t.sk_baby, E1M1Easy, 4, 37)]
    [InlineData(skill_t.sk_easy, E1M1Easy, 4, 37)]
    [InlineData(skill_t.sk_medium, E1M1Medium, 6, 37)]
    [InlineData(skill_t.sk_hard, E1M1Hard, 29, 38)]
    [InlineData(skill_t.sk_nightmare, E1M1Hard, 29, 38)]
    public void Doom1E1M1(skill_t skill, string expected, int totalkills, int totalitems)
    {
        Level map = Level.Load(WadArchive.Open(TestWads.RequireDoom1()), "E1M1");
        MapThingSpawn[] list = MapThingSpawning.SpawnList(map.Things, new SpawnSettings(GameMode.shareware, skill));
        Assert.Equal(map.Things.Length, list.Length);
        Assert.Equal(expected, Summary(list));
        mobjflag_t[] flags = list.Where(s => s.Kind == MapThingSpawnKind.Mobj).Select(s => Info.mobjinfo[(int)s.Type].flags).ToArray();
        Assert.Equal(totalkills, flags.Count(f => (f & mobjflag_t.MF_COUNTKILL) != 0));
        Assert.Equal(totalitems, flags.Count(f => (f & mobjflag_t.MF_COUNTITEM) != 0));

        // -nomonsters drops exactly the kills (E1M1 has no lost souls).
        MapThingSpawn[] nomonsters = MapThingSpawning.SpawnList(map.Things, new SpawnSettings(GameMode.shareware, skill, nomonsters: true));
        Assert.Equal(totalkills, nomonsters.Count(s => s.Kind == MapThingSpawnKind.NoMonsters));
    }

    /// <summary>Every thing of every map is a known type on every skill, single player and netgame; only Doom II monsters stop a non-commercial load.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryMapSelects(bool doom2)
    {
        WadArchive wad = WadArchive.Open(doom2 ? TestWads.RequireDoom2() : TestWads.RequireDoom1());
        GameMode mode = doom2 ? GameMode.commercial : GameMode.shareware;
        string[] maps = doom2
            ? Enumerable.Range(1, 32).Select(i => $"MAP{i:00}").ToArray()
            : Enumerable.Range(1, 9).Select(i => $"E1M{i}").ToArray();
        foreach (string name in maps.Where(m => wad.Find(m) is not null))
        {
            Level map = Level.Load(wad, name);
            foreach (skill_t skill in Enum.GetValues<skill_t>())
            {
                foreach (bool netgame in new[] { false, true })
                {
                    MapThingSpawn[] list = MapThingSpawning.SpawnList(map.Things, new SpawnSettings(mode, skill, netgame, deathmatch: netgame ? 1 : 0));
                    Assert.DoesNotContain(list, s => s.Kind == MapThingSpawnKind.NotLoaded);
                    Assert.Contains(list, s => s.Kind == MapThingSpawnKind.Mobj);
                }
            }
        }
    }
}
