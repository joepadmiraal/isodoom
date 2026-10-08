using System.Linq;
using IsoDoom.Map;
using IsoDoom.Render;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
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
    public void DoorsAreTheSectorsDoorSpecialsMove()
    {
        // T6.13d: a manual door line's back sector, a tagged door line's sectors (not tag 0), and the door sector specials.
        (Level level, _, _) = Build(Synthetic(), "E1M1");
        foreach (Line l in level.Lines)
        {
            l.Special = 0;
            l.Tag = 0;
        }
        foreach (Sector s in level.Sectors)
        {
            s.Special = 0;
            s.Tag = 0;
        }
        Line twoSided = level.Lines.First(l => l.BackSector is not null && l.FrontSector != l.BackSector);
        Assert.DoesNotContain(true, DoorLids.FindDoors(level));
        twoSided.Special = 1; // DR door
        Assert.Equal([twoSided.BackSector!.Index], Enumerable.Range(0, level.Sectors.Length).Where(i => DoorLids.FindDoors(level)[i]));
        twoSided.Special = 0;
        level.Lines[0].Special = 29; // S1 door, tag 0: nothing (P_FindSectorFromLineTag would take every tag-0 sector)
        Assert.DoesNotContain(true, DoorLids.FindDoors(level));
        level.Lines[0].Tag = 7;
        level.Sectors[2].Tag = 7;
        Assert.Equal([2], Enumerable.Range(0, level.Sectors.Length).Where(i => DoorLids.FindDoors(level)[i]));
        level.Lines[0].Special = 0;
        level.Sectors[5].Special = 10; // door close in 30 s
        Assert.Equal([5], Enumerable.Range(0, level.Sectors.Length).Where(i => DoorLids.FindDoors(level)[i]));
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
