using System.Linq;
using IsoDoom.Map;
using IsoDoom.Render;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Render;

/// <summary>T6.13b: which sectors get a lid on the solid above them, and at what height (<see cref="DoorLids"/>).</summary>
public class DoorLidsTests
{
    private static (Level Level, FloorTriangles Floors, DoorLids Lids) Build(WadArchive wad, string map)
    {
        var level = Level.Load(wad, map);
        var floors = FloorTriangles.Build(level, SubsectorPolygons.Build(level));
        return (level, floors, DoorLids.Build(level, floors));
    }

    private static WadArchive Synthetic() => new([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);

    private static float Ceiling(Level level, int sector) => (float)(level.Sectors[sector].CeilingHeight / 65536.0);

    [Fact]
    public void TheSyntheticDoorHasALidAtTheLowerNeighbouringCeiling()
    {
        (Level level, FloorTriangles floors, DoorLids lids) = Build(Synthetic(), "E1M1");
        // Door D (sector 3, 16 thick, closed) between room C (ceiling 128) and courtyard A (sky at 256).
        Assert.Equal([3], lids.Sectors);
        Assert.Equal([2, 4], lids.Neighbours(3));
        Assert.Equal(128f, lids.LidHeight(3, s => Ceiling(level, s)));
        Assert.True(DoorLids.Shows(128, Ceiling(level, 3)));
        Assert.InRange(DoorLids.Thickness(level, floors.BySector.Single(f => f.Sector == 3)), 15, 16);

        // Neither the rooms (too thick), the sky courtyard and ledge, nor the raised east room get one.
        Assert.All([0, 1, 2, 4, 5], s => Assert.False(lids.Has(s)));
        Assert.Equal(DoorLids.None, lids.LidHeight(2, s => Ceiling(level, s)));
        Assert.Empty(lids.Neighbours(2));
    }

    [Fact]
    public void TheLidFollowsTheNeighboursCeilingsAndShowsWhileTheDoorIsBelowIt()
    {
        (Level level, _, DoorLids lids) = Build(Synthetic(), "E1M1");
        // Room C's ceiling drops to 96: the lid with it; the door opens to 92 (4 below, as p_doors.c): the lintel stays.
        float LidWithC(float c) => lids.LidHeight(3, s => s == 2 ? c : Ceiling(level, s));
        Assert.Equal(96f, LidWithC(96));
        Assert.Equal(256f, LidWithC(300)); // the courtyard's is then the lowest
        Assert.True(DoorLids.Shows(96, 92));
        Assert.False(DoorLids.Shows(96, 96));
        Assert.False(DoorLids.Shows(96, 120));
    }

    [Fact]
    public void Doom1E1M1()
    {
        (Level level, FloorTriangles floors, DoorLids lids) = Build(WadArchive.Open(TestWads.RequireDoom1()), "E1M1");
        Assert.Equal(36, lids.Sectors.Count);
        // The door the hand play found (sector 4, x 1536-1552, 16 thick): a lid at 72, its neighbours' ceilings.
        Assert.True(lids.Has(4));
        Assert.Equal(72f, lids.LidHeight(4, s => Ceiling(level, s)));
        Assert.True(DoorLids.Shows(72, Ceiling(level, 4)));
        // The other doors of the task: (2912-2944, -3904 to -3776) and (2976-3040, -4648 to -4632).
        foreach ((int x, int y) in new[] { (2928, -3840), (3008, -4640) })
        {
            int sector = level.R_PointInSubsector(x << 16, y << 16).Sector.Index;
            Assert.True(lids.Has(sector), $"sector {sector} at ({x}, {y})");
            Assert.True(DoorLids.Shows(lids.LidHeight(sector, s => Ceiling(level, s)), Ceiling(level, sector)));
        }
        // A wide room with a low ceiling (sector 74, about 190 thick, ceiling 48 under 72 around it) gets no lid.
        Assert.False(lids.Has(74));
        Assert.True(DoorLids.Thickness(level, floors.BySector.Single(f => f.Sector == 74)) > DoorLids.MaxThickness);
    }

    [Fact]
    public void TheSyntheticDoorsJambsCloseItsLidsEnds()
    {
        WadArchive wad = Synthetic();
        (Level level, _, DoorLids lids) = Build(wad, "E1M1");
        var walls = WallSections.Build(level, Textures.R_InitTextures(wad));
        // T6.13f: door D's (sector 3) south track (L13, facing north, away from the game camera: its end shows), and
        // nothing else: its north track (L11) has no texture ("-"), so no wall, cap or end.
        int[] ends = [.. walls.Sections.Where(lids.HasEnd).Select(s => s.Line.Index).Order()];
        Assert.Equal([13], ends);
        Assert.All(walls.Sections.Where(lids.HasEnd), s => Assert.Equal(3, s.FrontSector.Index));
        Assert.DoesNotContain(walls.Sections, s => lids.HasEnd(s) && s.BackSector is not null);
    }

    [Fact]
    public void Doom1E1M1LidEnds()
    {
        WadArchive wad = WadArchive.Open(TestWads.RequireDoom1());
        (Level level, _, DoorLids lids) = Build(wad, "E1M1");
        var walls = WallSections.Build(level, Textures.R_InitTextures(wad));
        // T6.13f: the door west of the shotgun room (sector 76) has both jambs (337 at its west end, facing east, its cap
        // closing the block from above; 338 at its east end, facing west, away from the game camera: its end closes it).
        int[] door = [.. walls.Sections.Where(s => lids.HasEnd(s) && s.FrontSector.Index == 76).Select(s => s.Line.Index).Order()];
        Assert.Equal([337, 338], door);
        // Every lid sector's drawn one-sided middle has one; nothing else.
        Assert.All(walls.Sections, s => Assert.Equal(
            s.Texture != 0 && s.BackSector is null && s.Kind == WallSectionKind.Middle && lids.Has(s.FrontSector.Index), lids.HasEnd(s)));
    }

    [Fact]
    public void Doom1E1M1Doors()
    {
        (Level level, _, DoorLids lids) = Build(WadArchive.Open(TestWads.RequireDoom1()), "E1M1");
        // T6.13d: the manual doors (sector 4; (2928, -3840); (3008, -4640)) are doors, as is every sector a door line tags.
        foreach ((int x, int y) in new[] { (1544, -2496), (2928, -3840), (3008, -4640) })
        {
            int sector = level.R_PointInSubsector(x << 16, y << 16).Sector.Index;
            Assert.True(lids.IsDoor(sector), $"sector {sector} at ({x}, {y})");
        }
        foreach (Line line in level.Lines.Where(l => l.Special == 1 && l.BackSector is not null))
            Assert.True(lids.IsDoor(line.BackSector!.Index), $"line {line.Index}'s back sector");
        // Rooms, and the lintels over doorways that are not doors, are not.
        Assert.False(lids.IsDoor(74));
        Assert.False(lids.IsDoor(level.R_PointInSubsector(1056 << 16, -3616 << 16).Sector.Index)); // the start room
        Assert.Contains(lids.Sectors, s => !lids.IsDoor(s));
    }

    [Fact]
    public void Doom1E1M1UpperWallsKeptOverDoorsAndLintelsOnly()
    {
        (Level level, _, DoorLids lids) = Build(WadArchive.Open(TestWads.RequireDoom1()), "E1M1");
        float Ceiling(int s) => DoorLidsTests.Ceiling(level, s);
        // The start room (ceiling 72) and the 32-wide step around the pad (120, between 72 and
        // 200: a lid sector whose lid never shows) drop the upper walls over them.
        int start = level.R_PointInSubsector(1056 << 16, -3616 << 16).Sector.Index;
        Assert.False(lids.KeepsUppers(start, Ceiling));
        Assert.True(lids.Has(37));
        Assert.False(lids.KeepsUppers(37, Ceiling));
        // The door east of the start (sector 4) and the start room's east window (sector 14, a lintel) keep theirs.
        Assert.True(lids.KeepsUppers(4, Ceiling));
        Assert.False(lids.MovesCeiling(14));
        Assert.True(lids.KeepsUppers(14, Ceiling));
    }

    [Fact]
    public void CeilingMoversKeepTheirUpperWalls()
    {
        // Three rooms, no specials; then a W1 crusher (line 0) tagged to room 2. The specials as loaded (P12f).
        static (Level, DoorLids) Lids(TestMap map)
        {
            var level = Level.Load(new WadArchive([WadFile.FromBytes(map.Build(), "testmap.wad")]), "E1M1");
            return (level, DoorLids.Build(level, FloorTriangles.Build(level, SubsectorPolygons.Build(level))));
        }
        static TestMap Map() => TestMap.Strip(0, 0, 128, new(128, 0, 128), new(128, 0, 96), new(128, 0, 128));
        (Level plain, DoorLids none) = Lids(Map());
        Assert.DoesNotContain(Enumerable.Range(0, plain.Sectors.Length), none.MovesCeiling);
        (Level level, DoorLids lids) = Lids(Map().Special(0, 25, 7).SectorTag(2, 7));
        Assert.Equal([2], Enumerable.Range(0, level.Sectors.Length).Where(lids.MovesCeiling));
        Assert.True(lids.KeepsUppers(2, s => Ceiling(level, s)));
    }

    [Fact]
    public void DoorsAreTheSectorsDoorSpecialsMove()
    {
        // T6.13d: a manual door line's back sector, a tagged door line's sectors (not tag 0), and the door sector specials.
        // Rooms A (0), door D (1, closed), B (2); Boundaries[2] has B in front and D behind.
        static TestMap Map() => TestMap.Strip(0, 0, 128, new(128, 0, 128), new(16, 0, 0), new(128, 0, 128));
        static int[] Doors(TestMap map)
        {
            bool[] doors = DoorLids.FindDoors(Level.Load(new WadArchive([WadFile.FromBytes(map.Build(), "testmap.wad")]), "E1M1"));
            return [.. Enumerable.Range(0, doors.Length).Where(i => doors[i])];
        }

        Assert.Empty(Doors(Map()));
        TestMap dr = Map();
        Assert.Equal([1], Doors(dr.Special(dr.Boundaries[2], 1))); // DR door
        Assert.Empty(Doors(Map().Special(0, 29))); // S1 door, tag 0: nothing (P_FindSectorFromLineTag would take every tag-0 sector)
        Assert.Equal([2], Doors(Map().Special(0, 29, 7).SectorTag(2, 7)));
        Assert.Equal([1], Doors(Map().SectorSpecial(1, 14))); // (cleared at spawn: DoorSectorSpecialsClearedAtSpawnStayDoors)

        // As loaded (P12d, P12f): a sector special (a floor's change-type) or line special set after load makes no door.
        var level = Level.Load(new WadArchive([WadFile.FromBytes(Map().Build(), "testmap.wad")]), "E1M1");
        level.Sectors[1].Special = 10;
        level.Lines[0].Special = 29;
        level.Lines[0].Tag = 7;
        level.Sectors[2].Tag = 7;
        Assert.DoesNotContain(true, DoorLids.FindDoors(level));
    }

    /// <summary>
    /// P12f: the sim clears a one-time line's special once triggered (a D1 door used, a W1 line crossed) and a
    /// loaded game restores it so; the doors and the lids stay those of the map as loaded, whenever they are built.
    /// </summary>
    [Fact]
    public void OneTimeDoorLinesTriggeredStayDoors()
    {
        // Door D0 (0, closed), rooms A (1) and B (2), door D3 (3, closed, tag 5), the ceiling crusher C (4, tag 6), room E (5).
        TestMap map = TestMap.Strip(0, 0, 128, new(16, 0, 0), new(128, 0, 128), new(128, 0, 128), new(16, 0, 0), new(16, 0, 128), new(128, 0, 128));
        int d1 = map.Boundaries[1], w1 = map.Boundaries[2], crusher = map.Boundaries[5];
        map.Special(d1, 31).Special(w1, 2, 5).SectorTag(3, 5).Special(crusher, 44, 6).SectorTag(4, 6).Player(40, 64, 180);
        var wad = new WadArchive([WadFile.FromBytes(map.Build(), "testmap.wad")]);
        var fresh = Level.Load(wad, "E1M1");

        var world = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium), Tweaks.Vanilla);
        world.P_InitPicAnims(null, PicAnims.FlatNames(wad)); // as SaveGameTests.Load's
        world.G_DoLoadLevel(Level.Load(wad, "E1M1"));
        world.P_UseLines(world.players[0]); // EV_VerticalDoor: a D1 door clears its special
        world.P_CrossSpecialLine(w1, 0, world.players[0].mo!); // W1: cleared
        world.P_CrossSpecialLine(crusher, 0, world.players[0].mo!);
        Level live = world.level;
        Assert.All([d1, w1, crusher], l => Assert.Equal(0, live.Lines[l].Special));
        Assert.Equal([31, 2, 44], [live.Lines[d1].LoadedSpecial, live.Lines[w1].LoadedSpecial, live.Lines[crusher].LoadedSpecial]);
        Assert.NotNull(world.sectors[0].specialdata);

        int[] Doors(Level level)
        {
            bool[] doors = DoorLids.FindDoors(level);
            return [.. Enumerable.Range(0, doors.Length).Where(i => doors[i])];
        }
        Assert.Equal([0, 3], Doors(fresh));
        Assert.Equal(Doors(fresh), Doors(live));
        AssertSameLids(fresh, live);

        // A loaded game: its lines' specials as saved (cleared), on a fresh copy of the map (LevelScene's StartFog after StartWorld).
        World loaded = Sim.SaveGameTests.Load(Sim.SaveGameTests.Save(world), wad);
        Assert.Equal(0, loaded.level.Lines[d1].Special);
        Assert.Equal(Doors(fresh), Doors(loaded.level));
        AssertSameLids(fresh, loaded.level);
    }

    private static void AssertSameLids(Level expected, Level actual)
    {
        DoorLids Lids(Level level) => DoorLids.Build(level, FloorTriangles.Build(level, SubsectorPolygons.Build(level)));
        DoorLids a = Lids(expected), b = Lids(actual);
        Assert.Equal(a.Sectors, b.Sectors);
        for (int s = 0; s < expected.Sectors.Length; s++)
        {
            Assert.Equal(a.IsDoor(s), b.IsDoor(s));
            Assert.Equal(a.MovesCeiling(s), b.MovesCeiling(s));
            Assert.Equal(a.Neighbours(s), b.Neighbours(s));
        }
    }

    /// <summary>P3: a door sector special the sim cleared at spawn (NEIS E1M9's 14) still makes a door, as the mesh built before the spawn says.</summary>
    [Fact]
    public void DoorSectorSpecialsClearedAtSpawnStayDoors()
    {
        foreach (int special in new[] { 10, 14 })
        {
            IsoDoom.Sim.World world = TestMap.Polygon(0, 0, (0, 0), (0, 64), (64, 64), (64, 0)).SectorSpecial(0, special).Player(32, 32).Load();
            Assert.Equal(0, world.level.Sectors[0].Special); // P_SpawnDoorCloseIn30, P_SpawnDoorRaiseIn5Mins
            Assert.Equal(special, world.level.Sectors[0].LoadedSpecial);
            Assert.True(DoorLids.FindDoors(world.level)[0]);
        }
    }

    [Fact]
    public void Doom2ThickDoorsButNotClosets()
    {
        string path = TestWads.RequireDoom2();
        (Level level, FloorTriangles floors, DoorLids lids) = Build(WadArchive.Open(path), "MAP27");
        // A 64-thick door closed at load gets a lid; a 128-thick closet closed at load does not.
        Assert.True(lids.Has(100));
        Assert.InRange(DoorLids.Thickness(level, floors.BySector.Single(f => f.Sector == 100)), 33, DoorLids.MaxDoorThickness);
        Assert.False(lids.Has(48));
    }
}
