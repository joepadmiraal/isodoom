using System;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Map;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T5.1: p_spec.c's lookup helpers, p_map.c's <c>P_ChangeSector</c> and the
/// sector interpolation on the synthetic specials map (E1M2: a corridor,
/// sector 0, with 24 alcoves, sectors 1-24, along its north side; see
/// <c>SyntheticIwad.BuildSpecialsMap</c>), the switch list, and the run-time
/// texture anchor of wall sections.
/// </summary>
public class SpecTests
{
    private const int FRACUNIT = 1 << 16;
    private const int Alcoves = SyntheticIwad.SpecialsAlcoves;

    private static WadArchive Wad() => new(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) });

    private static World Specials()
    {
        var world = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium), Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(Wad(), "E1M2"));
        return world;
    }

    private static int F(int units) => units * FRACUNIT;

    private static sector_t Alcove(World world, int i) => world.sectors[1 + i];

    [Fact]
    public void TheSpecialsMapIsSound()
    {
        WadArchive wad = Wad();
        Level map = Level.Load(wad, "E1M2");
        Assert.Equal(SyntheticIwad.SpecialsSectors, map.Sectors.Length);
        Assert.Equal(SyntheticIwad.SpecialsSectors, map.Subsectors.Length);
        SubsectorPolygons polys = SubsectorPolygons.Build(map);
        PolygonChecks.CheckShapes(map, polys);
        FloorTriangles floors = FloorTriangles.Build(map, polys);
        FloorChecks.Check(map, polys, floors);
        double[] fromLines = PolygonChecks.SectorAreasFromLines(map);
        double[] fromPolygons = PolygonChecks.SectorAreasFromPolygons(map, polys);
        Assert.Equal(768.0 * 128, fromLines[0]);
        for (int s = 0; s < map.Sectors.Length; s++)
            Assert.Equal(fromLines[s], fromPolygons[s]);
        for (int i = 0; i < Alcoves; i++)
        {
            Assert.Equal(1 + i, map.R_PointInSubsector(F(32 * i + 16), F(64)).Sector.Index);
            Assert.Equal(32.0 * 128, fromLines[1 + i]);
        }
        Assert.Equal(0, map.R_PointInSubsector(F(400), F(-64)).Sector.Index);
        // T5.3: rooms R and S and the door between them.
        Assert.Equal(SyntheticIwad.DoorSector - 1, map.R_PointInSubsector(F(64), F(196)).Sector.Index);
        Assert.Equal(SyntheticIwad.DoorSector, map.R_PointInSubsector(F(144), F(196)).Sector.Index);
        Assert.Equal(SyntheticIwad.DoorSector + 1, map.R_PointInSubsector(F(224), F(196)).Sector.Index);
        Assert.Equal(32.0 * 88, fromLines[SyntheticIwad.DoorSector]);

        WallSections walls = WallSections.Build(map, GraphicsCatalog.Load(wad).Textures);
        Assert.Empty(walls.Missing);
        Assert.Equal(3 + Alcoves, map.Sectors[0].Lines.Count);
        Assert.Contains(walls.Sections, s => s.Line.Index == 27 && s.SideDef.MidTexture == "SW1BRCOM");
    }

    [Fact]
    public void GetSideGetSectorAndTwoSided()
    {
        World world = Specials();
        // Corridor line 3 is alcove 0's opening (line 3), the alcove in front.
        Assert.Same(world.sides[world.lines[3].sidenum[0]], world.getSide(0, 3, 0));
        Assert.Same(world.sides[world.lines[3].sidenum[1]], world.getSide(0, 3, 1));
        Assert.Same(Alcove(world, 0), world.getSector(0, 3, 0));
        Assert.Same(world.sectors[0], world.getSector(0, 3, 1));
        Assert.Equal(0, world.twoSided(0, 0)); // the corridor's south wall
        Assert.Equal(Line.ML_TWOSIDED, world.twoSided(0, 3));

        Assert.Same(Alcove(world, 0), World.getNextSector(world.lines[3], world.sectors[0]));
        Assert.Same(world.sectors[0], World.getNextSector(world.lines[3], Alcove(world, 0)));
        // A line not touching the sector gives the front sector (vanilla doesn't check).
        Assert.Same(Alcove(world, 1), World.getNextSector(world.lines[4], Alcove(world, 5)));
        Assert.Null(World.getNextSector(world.lines[0], world.sectors[0]));
    }

    [Fact]
    public void SectorLinesFollowP_GroupLines()
    {
        World world = Specials();
        foreach (sector_t sector in world.sectors)
        {
            Assert.Equal(sector.map.Lines.Select(l => l.Index), sector.lines.Select(l => l.Index));
            Assert.All(sector.lines, l => Assert.True(l.frontsector == sector || l.backsector == sector));
        }
        Assert.Equal(new[] { 0, 1, 2 }.Concat(Enumerable.Range(3, Alcoves)), world.sectors[0].lines.Select(l => l.Index));
        Assert.Equal(new[] { 3 + 5, 27 + 5, 52 + 5, 52 + 6 }, Alcove(world, 5).lines.Select(l => l.Index)); // opening, north wall, boundaries
        Assert.Equal(Enumerable.Range(0, world.level.Sides.Length), world.sides.Select(s => s.Index));
        Assert.All(world.sides, s => Assert.Same(world.sectors[s.map.Sector.Index], s.sector));
    }

    [Fact]
    public void FloorHelpers()
    {
        World world = Specials();
        sector_t corridor = world.sectors[0];
        Assert.Equal(F(-32), World.P_FindLowestFloorSurrounding(corridor));
        Assert.Equal(F(16 + 8 * 21), World.P_FindHighestFloorSurrounding(corridor));

        // Alcove 5 (floor 16 + 8 * (25 % 22) = 40): the corridor (0), alcoves 4 (176) and 6 (80).
        Assert.Equal(F(40), Alcove(world, 5).floorheight);
        Assert.Equal(0, World.P_FindLowestFloorSurrounding(Alcove(world, 5)));
        Assert.Equal(F(176), World.P_FindHighestFloorSurrounding(Alcove(world, 5)));
        Assert.Equal(F(80), World.P_FindNextHighestFloor(Alcove(world, 5), F(40)));
        Assert.Equal(F(176), World.P_FindNextHighestFloor(Alcove(world, 5), F(80)));
        Assert.Equal(F(176), World.P_FindNextHighestFloor(Alcove(world, 5), F(100)));
        Assert.Equal(F(200), World.P_FindNextHighestFloor(Alcove(world, 5), F(200))); // none higher: the height itself

        // Its own floor counts for the lowest; with no two-sided line the highest is -500.
        Assert.Equal(F(-32), World.P_FindLowestFloorSurrounding(Alcove(world, 23)));
        World room = TestMap.Polygon(24, 128, (0, 0), (0, 64), (64, 64), (64, 0)).Load();
        Assert.Equal(F(24), World.P_FindLowestFloorSurrounding(room.sectors[0]));
        Assert.Equal(F(-500), World.P_FindHighestFloorSurrounding(room.sectors[0]));
        Assert.Equal(int.MaxValue, World.P_FindLowestCeilingSurrounding(room.sectors[0]));
        Assert.Equal(0, World.P_FindHighestCeilingSurrounding(room.sectors[0]));
        Assert.Equal(77, World.P_FindMinSurroundingLight(room.sectors[0], 77));
    }

    [Fact]
    public void NextHighestFloorOverrunsAsVanilla()
    {
        World world = Specials();
        sector_t corridor = world.sectors[0];
        // 22 higher neighbours (alcoves 0-21, the lowest 16): vanilla's 22nd write overwrites the height
        // compared against with the 22nd's floor (152), so alcove 22's floor of 8, above the corridor's
        // 0 and lower than all of them, is skipped.
        Assert.Equal(F(16), World.P_FindNextHighestFloor(corridor, 0));
        // linuxdoom-1.10's cut at 20 would give min(alcoves 0-19) = 16 too; a lower 21st tells them apart.
        Alcove(world, 20).floorheight = F(12);
        Assert.Equal(F(12), World.P_FindNextHighestFloor(corridor, 0));
        // A 23rd higher neighbour (above the overwritten height) crashes vanilla.
        Alcove(world, 23).floorheight = F(400);
        Assert.Throws<WadFormatException>(() => World.P_FindNextHighestFloor(corridor, 0));
        // From 15: alcoves 20 (12) and 22 (8) aren't higher, so 400 is the 22nd: no crash.
        Assert.Equal(F(16), World.P_FindNextHighestFloor(corridor, F(15)));
    }

    [Fact]
    public void CeilingAndLightHelpers()
    {
        World world = Specials();
        sector_t corridor = world.sectors[0];
        Assert.Equal(F(96), World.P_FindLowestCeilingSurrounding(corridor));
        Assert.Equal(F(320), World.P_FindHighestCeilingSurrounding(corridor));
        Assert.Equal(64, World.P_FindMinSurroundingLight(corridor, 255));
        Assert.Equal(50, World.P_FindMinSurroundingLight(corridor, 50));
        // Alcove 5: the corridor (128, light 160), alcoves 4 (256, 128) and 6 (272, 144).
        Assert.Equal(F(128), World.P_FindLowestCeilingSurrounding(Alcove(world, 5)));
        Assert.Equal(F(272), World.P_FindHighestCeilingSurrounding(Alcove(world, 5)));
        Assert.Equal(128, World.P_FindMinSurroundingLight(Alcove(world, 5), 255));
    }

    [Fact]
    public void SectorsFromLineTags()
    {
        World world = Specials();
        line_t tagged = world.lines[0];
        Assert.Equal(5, tagged.tag);
        Assert.Equal(1 + 3, world.P_FindSectorFromLineTag(tagged, -1));
        Assert.Equal(1 + 7, world.P_FindSectorFromLineTag(tagged, 4));
        Assert.Equal(1 + 12, world.P_FindSectorFromLineTag(tagged, 8));
        Assert.Equal(-1, world.P_FindSectorFromLineTag(tagged, 13));
        // Tag 0 finds the untagged sectors (vanilla doesn't skip them).
        Assert.Equal(0, world.P_FindSectorFromLineTag(world.lines[4], -1));
    }

    [Fact]
    public void ChangeSectorMovesThingsWithTheFloor()
    {
        World world = Specials();
        mobj_t me = world.players[0].mo!;
        sector_t corridor = world.sectors[0];
        Assert.Same(corridor, me.subsector.sector);
        Assert.Equal(0, me.z);

        corridor.floorheight = F(-16);
        Assert.False(world.P_ChangeSector(corridor, false));
        Assert.Equal(F(-16), me.floorz);
        Assert.Equal(F(-16), me.z);
        corridor.floorheight = F(8);
        Assert.False(world.P_ChangeSector(corridor, false));
        Assert.Equal(F(8), me.z);

        // A floating thing stays where it is unless the ceiling pushes it down.
        mobj_t flying = world.P_SpawnMobj(F(300), F(-64), F(40), mobjtype_t.MT_HEAD);
        corridor.ceilingheight = F(96);
        world.P_ChangeSector(corridor, false);
        Assert.Equal(F(40), flying.z);
        corridor.ceilingheight = F(80);
        world.P_ChangeSector(corridor, false);
        Assert.Equal(F(80) - flying.height, flying.z);
    }

    [Fact]
    public void ChangeSectorCrushes()
    {
        World world = Specials();
        mobj_t me = world.players[0].mo!;
        sector_t corridor = world.sectors[0];
        mobj_t clip = world.P_SpawnMobj(F(200), F(-64), World.ONFLOORZ, mobjtype_t.MT_CLIP);
        clip.flags |= mobjflag_t.MF_DROPPED;
        mobj_t corpse = world.P_SpawnMobj(F(400), F(-64), World.ONFLOORZ, mobjtype_t.MT_POSSESSED);
        corpse.health = 0;
        mobj_t lamp = world.P_SpawnMobj(F(600), F(-64), World.ONFLOORZ, mobjtype_t.MT_MISC31); // solid, not shootable

        // The ceiling down to 8 units: nothing but a shootable thing stops it.
        corridor.ceilingheight = F(8);
        int prnd = world.random.prndindex;
        world.leveltime = 1; // not a crushing tic
        Assert.True(world.P_ChangeSector(corridor, true));
        Assert.Equal(prnd, world.random.prndindex);
        Assert.Equal(think_t.REMOVED, clip.function);      // dropped items are crunched
        Assert.Equal(statenum_t.S_GIBS, corpse.state);     // bodies become giblets
        Assert.Equal(0, corpse.height);
        Assert.Equal(0, corpse.radius);
        Assert.Equal((mobjflag_t)0, corpse.flags & mobjflag_t.MF_SOLID);
        Assert.NotEqual(think_t.REMOVED, lamp.function);   // gibs or something: left alone
        int mobjs = world.Mobjs().Count();

        // Without crunch, or off the fourth tic, no damage and no blood.
        Assert.True(world.P_ChangeSector(corridor, false));
        world.leveltime = 4;
        Assert.True(world.P_ChangeSector(corridor, false));
        Assert.Equal(mobjs, world.Mobjs().Count());
        // Crushing on every fourth tic: the damage hook (P_DamageMobj, M6) and blood with four P_Random calls
        // after the spawn's own (lastlook).
        Assert.True(world.P_ChangeSector(corridor, true));
        mobj_t blood = world.Mobjs().Last();
        Assert.Equal(mobjtype_t.MT_BLOOD, blood.type);
        Assert.Equal(me.x, blood.x);
        Assert.Equal((prnd + 1 + 4) & 0xff, world.random.prndindex);
        Assert.True(world.crushchange);
        Assert.True(world.nofit);
    }

    [Fact]
    public void SectorHeightsAreStoredForInterpolation()
    {
        World world = Specials();
        sector_t corridor = world.sectors[0];
        Assert.Equal((corridor.floorheight, corridor.ceilingheight), (corridor.oldfloorheight, corridor.oldceilingheight));
        ulong checksum = world.Checksum();
        corridor.floorheight = F(-8);
        corridor.ceilingheight = F(136);
        Assert.Equal((0, F(128)), (corridor.oldfloorheight, corridor.oldceilingheight));
        world.P_StoreInterpolation();
        Assert.Equal((F(-8), F(136)), (corridor.oldfloorheight, corridor.oldceilingheight));
        // Not sim state: storing changes no checksum.
        corridor.floorheight = 0;
        corridor.ceilingheight = F(128);
        Assert.Equal(checksum, world.Checksum());
    }

    [Fact]
    public void SwitchListPerGameMode()
    {
        Assert.Equal(19, Switches.For(GameMode.shareware).Count());
        Assert.Equal(29, Switches.For(GameMode.registered).Count());
        Assert.Equal(29, Switches.For(GameMode.retail).Count());
        Assert.Equal(40, Switches.For(GameMode.commercial).Count());
        Assert.Equal(new switchlist_t("SW1BRCOM", "SW2BRCOM", 1), Switches.alphSwitchList[0]);
        Assert.True(Switches.alphSwitchList.Length * 2 <= Switches.MAXSWITCHES * 2);
        Assert.All(Switches.alphSwitchList, s => Assert.Equal("SW2" + s.name1[3..], s.name2));
    }

    [Fact]
    public void ShareSwitchesExistInDoom1()
    {
        Textures textures = GraphicsCatalog.Load(WadArchive.Open(TestWads.RequireDoom1())).Textures;
        foreach (switchlist_t s in Switches.For(GameMode.shareware))
        {
            Assert.True(textures.R_CheckTextureNumForName(s.name1) > 0, s.name1);
            Assert.True(textures.R_CheckTextureNumForName(s.name2) > 0, s.name2);
        }
    }
}
