using System.Diagnostics;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Render;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Render;

/// <summary>T6.13l: the fog of war's sight walk (<see cref="FogOfWar"/>): what the player sees, what it has seen, and the lines it maps.</summary>
public class FogOfWarTests
{
    private static WadArchive Synthetic() => new([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);

    private static FogOfWar See(Level level, int x, int y)
    {
        var fog = new FogOfWar(level);
        fog.See(x << Fixed.FRACBITS, y << Fixed.FRACBITS);
        return fog;
    }

    private static bool Mapped(Level level, int line) => (level.Lines[line].Flags & Line.ML_MAPPED) != 0;

    // Synthetic E1M1: west room 0 | (L1, open: a step and a lower ceiling) | east room 1; apart from them,
    // the strip room C (2) | (L8) closed door D (3) | (L12) courtyard A (4) | (L15, a grate) ledge B (5).

    [Fact]
    public void AnOpenLineLetsSightThroughAndOneSidedWallsBlock()
    {
        Level level = Level.Load(Synthetic(), "E1M1");
        FogOfWar fog = See(level, 0, 0); // player 1's start, in the west room
        Assert.Equal(SectorSight.Visible, fog.State(0));
        Assert.Equal(SectorSight.Visible, fog.State(1)); // through L1
        // The strip lies beyond the east room's one-sided east wall and the void.
        Assert.All([2, 3, 4, 5], s => Assert.Equal(SectorSight.Unseen, fog.State(s)));
        Assert.Equal(2, fog.VisibleCount);
        Assert.Equal(2, fog.DiscoveredCount);
        Assert.All(Enumerable.Range(0, 7), l => Assert.True(Mapped(level, l), $"line {l}"));
        Assert.All(Enumerable.Range(7, 13), l => Assert.False(Mapped(level, l), $"line {l}"));
    }

    [Fact]
    public void SightCoversTheFullCircle()
    {
        Level level = Level.Load(Synthetic(), "E1M1");
        // Near the east room's east wall: the west room lies behind whatever way the player faces.
        FogOfWar fog = See(level, 376, -8);
        Assert.Equal(SectorSight.Visible, fog.State(1));
        Assert.Equal(SectorSight.Visible, fog.State(0));
        Assert.True(Mapped(level, 3)); // the west room's west wall, the far side of the circle
    }

    [Fact]
    public void AClosedDoorBlocksUntilItOpens()
    {
        Level level = Level.Load(Synthetic(), "E1M1");
        var fog = new FogOfWar(level);
        fog.See(576 << Fixed.FRACBITS, 0); // room C
        Assert.Equal(SectorSight.Visible, fog.State(2));
        // The door's face is seen, so the door is discovered (L8 mapped, either side), but not in sight.
        Assert.Equal(SectorSight.Discovered, fog.State(3));
        Assert.Equal(SectorSight.Unseen, fog.State(4));
        Assert.Equal(SectorSight.Unseen, fog.State(5));
        Assert.All([0, 1], s => Assert.Equal(SectorSight.Unseen, fog.State(s)));
        Assert.All([7, 8, 9, 10], l => Assert.True(Mapped(level, l), $"line {l}"));
        Assert.All([11, 12, 13, 14, 15], l => Assert.False(Mapped(level, l), $"line {l}"));
        int version = fog.Version;

        // The door opens (as p_doors.c's raise, to the courtyard's lower ceiling less 4).
        level.Sectors[3].CeilingHeight = 124 << Fixed.FRACBITS;
        fog.See(576 << Fixed.FRACBITS, 0);
        Assert.Equal(SectorSight.Visible, fog.State(3));
        Assert.Equal(SectorSight.Visible, fog.State(4));
        Assert.Equal(SectorSight.Visible, fog.State(5)); // over the grate's line, onto the ledge
        Assert.True(Mapped(level, 12));
        Assert.True(Mapped(level, 18)); // the ledge's east wall
        Assert.NotEqual(version, fog.Version);

        // It closes again: what was seen stays discovered.
        level.Sectors[3].CeilingHeight = 0;
        fog.See(576 << Fixed.FRACBITS, 0);
        Assert.Equal(SectorSight.Visible, fog.State(2));
        Assert.Equal(SectorSight.Discovered, fog.State(3));
        Assert.Equal(SectorSight.Discovered, fog.State(4));
        Assert.Equal(SectorSight.Discovered, fog.State(5));
        Assert.Equal(1, fog.VisibleCount);
        Assert.Equal(4, fog.DiscoveredCount);
    }

    [Fact]
    public void TheDiscoveredSetComesBackFromTheMappedLines()
    {
        Level level = Level.Load(Synthetic(), "E1M1");
        var fog = new FogOfWar(level);
        level.Sectors[3].CeilingHeight = 124 << Fixed.FRACBITS;
        fog.See(576 << Fixed.FRACBITS, 0);
        fog.See(0, 0);
        SectorSight[] before = fog.States.ToArray();
        Assert.Equal(6, fog.DiscoveredCount);

        // A loaded game: the same line flags (P_UnArchiveWorld), a fresh fog.
        var loaded = new FogOfWar(level);
        Assert.False(loaded.HasSeen);
        Assert.Equal(6, loaded.DiscoveredCount);
        Assert.Equal(0, loaded.VisibleCount);
        Assert.Equal(before.Select(s => s != SectorSight.Unseen), loaded.States.ToArray().Select(s => s != SectorSight.Unseen));

        // A fresh level: nothing mapped, nothing discovered.
        var fresh = new FogOfWar(Level.Load(Synthetic(), "E1M1"));
        Assert.Equal(0, fresh.DiscoveredCount);
    }

    [Fact]
    public void TheSpecialsMapsDoorHidesRoomS()
    {
        Level level = Level.Load(Synthetic(), "E1M2");
        const int RoomR = SyntheticIwad.DoorSector - 1, RoomS = SyntheticIwad.DoorSector + 1;
        FogOfWar fog = See(level, 64, 196);
        Assert.Equal(SectorSight.Visible, fog.State(RoomR));
        Assert.Equal(SectorSight.Discovered, fog.State(SyntheticIwad.DoorSector));
        Assert.Equal(SectorSight.Unseen, fog.State(RoomS));
        Assert.True(Mapped(level, SyntheticIwad.DoorLineR));
        Assert.False(Mapped(level, SyntheticIwad.DoorLineS));

        level.Sectors[SyntheticIwad.DoorSector].CeilingHeight = 124 << Fixed.FRACBITS;
        fog.See(64 << Fixed.FRACBITS, 196 << Fixed.FRACBITS);
        Assert.Equal(SectorSight.Visible, fog.State(RoomS));
        Assert.True(Mapped(level, SyntheticIwad.DoorLineS));
    }

    [Fact]
    public void Doom1E1M1()
    {
        Level level = Level.Load(WadArchive.Open(TestWads.RequireDoom1()), "E1M1");
        MapThing start = level.PlayerStart(0)!.Value;
        int x = start.X << Fixed.FRACBITS, y = start.Y << Fixed.FRACBITS;
        var fog = new FogOfWar(level);
        fog.See(x, y);
        int startSector = level.R_PointInSubsector(x, y).Sector.Index;
        Assert.True(fog.IsVisible(startSector));
        // The start room and what its windows and openings show, not the level.
        Assert.InRange(fog.VisibleCount, 2, level.Sectors.Length / 4);
        Assert.True(fog.DiscoveredCount >= fog.VisibleCount);
        Assert.Contains(Enumerable.Range(0, level.Sectors.Length), s => !fog.IsDiscovered(s));
        // Every visible sector has a mapped line.
        Assert.All(Enumerable.Range(0, level.Sectors.Length).Where(fog.IsVisible),
            s => Assert.Contains(level.Sectors[s].Lines, l => (l.Flags & Line.ML_MAPPED) != 0));

        // The door the lid tests use (sector 4, x 1536-1552, y -2560 to -2432, closed), out of sight of the start.
        Assert.Equal(SectorSight.Unseen, fog.State(4));
        // In front of it, on either side: its face is seen, it is discovered, not in sight, and opening it shows the other side.
        foreach ((int fx, int bx) in new[] { (1504, 1584), (1584, 1504) })
        {
            level = Level.Load(WadArchive.Open(TestWads.RequireDoom1()), "E1M1");
            fog = new FogOfWar(level);
            int here = level.R_PointInSubsector(fx << Fixed.FRACBITS, -2496 << Fixed.FRACBITS).Sector.Index;
            int there = level.R_PointInSubsector(bx << Fixed.FRACBITS, -2496 << Fixed.FRACBITS).Sector.Index;
            Assert.NotEqual(here, there);
            fog.See(fx << Fixed.FRACBITS, -2496 << Fixed.FRACBITS);
            Assert.True(fog.IsVisible(here));
            Assert.Equal(SectorSight.Discovered, fog.State(4));
            Assert.False(fog.IsVisible(there), $"sector {there} seen through the closed door from sector {here}");
            int before = fog.VisibleCount;
            level.Sectors[4].CeilingHeight = 68 << Fixed.FRACBITS;
            fog.See(fx << Fixed.FRACBITS, -2496 << Fixed.FRACBITS);
            Assert.True(fog.IsVisible(4));
            Assert.True(fog.IsVisible(there));
            Assert.True(fog.VisibleCount > before);
        }
    }

    [Fact]
    public void AVisibleSectorsDoorsAreDiscovered()
    {
        // DOOM1 E1M1 from a 64-unit grid of points: a door shows with its room, its face in sight or not
        // (the user's report: walking up to the first door, it popped in once its face came into sight).
        Level level = Level.Load(WadArchive.Open(TestWads.RequireDoom1()), "E1M1");
        bool[] doors = DoorLids.FindDoors(level);
        int points = 0, mappedByRoom = 0;
        for (int x = -1024; x <= 3584; x += 64)
        {
            for (int y = -5120; y <= -2048; y += 64)
            {
                Sector sector = level.R_PointInSubsector(x << Fixed.FRACBITS, y << Fixed.FRACBITS).Sector;
                if (sector.CeilingHeight - sector.FloorHeight < 56 << Fixed.FRACBITS)
                    continue;
                foreach (Line l in level.Lines)
                    l.Flags &= ~Line.ML_MAPPED;
                var fog = new FogOfWar(level);
                fog.See(x << Fixed.FRACBITS, y << Fixed.FRACBITS);
                points++;
                foreach (Sector s in level.Sectors.Where(s => fog.IsVisible(s.Index)))
                {
                    foreach (Line l in s.Lines.Where(l => l.BackSector is not null))
                    {
                        Sector other = l.FrontSector == s ? l.BackSector! : l.FrontSector!;
                        if (!doors[other.Index])
                            continue;
                        Assert.True(fog.IsDiscovered(other.Index), $"from ({x}, {y}): door sector {other.Index} next to visible sector {s.Index} is {fog.State(other.Index)}");
                        Assert.True(Mapped(level, l.Index), $"from ({x}, {y}): line {l.Index} between visible sector {s.Index} and door sector {other.Index} not mapped");
                        mappedByRoom++;
                    }
                }
            }
        }
        Assert.True(points > 100, $"{points} points");
        Assert.True(mappedByRoom > 0);
    }

    [Fact]
    public void AShutDoorAboveItsNeighboursFloorBlocks()
    {
        // DOOM1 E1M4's door sector 10 (x -216 to -200), shut at 144, between sectors 8 (floor 136) and 9 (floor 136):
        // R_AddLine's closed-door test lets it through (vanilla's renderer hides the rest by its column clipping).
        Level level = Level.Load(WadArchive.Open(TestWads.RequireDoom1()), "E1M4");
        FogOfWar fog = See(level, -240, 672);
        Assert.Equal(8, level.R_PointInSubsector(-240 << Fixed.FRACBITS, 672 << Fixed.FRACBITS).Sector.Index);
        Assert.Equal(SectorSight.Discovered, fog.State(10));
        Assert.False(fog.IsVisible(9));
    }

    [Fact]
    public void TheWalkIsCheapOnDoom2sBiggestMaps()
    {
        string wad = TestWads.RequireDoom2();
        foreach (string map in new[] { "MAP18", "MAP27" })
        {
            Level level = Level.Load(WadArchive.Open(wad), map);
            MapThing start = level.PlayerStart(0)!.Value;
            var fog = new FogOfWar(level);
            int x = start.X << Fixed.FRACBITS, y = start.Y << Fixed.FRACBITS;
            for (int i = 0; i < 20; i++)
                fog.See(x, y); // warm up
            // Open every closed sector too, the worst case: sight goes everywhere.
            foreach (Sector s in level.Sectors.Where(s => s.CeilingHeight <= s.FloorHeight))
                s.CeilingHeight = s.FloorHeight + (128 << Fixed.FRACBITS);
            // The best of ten batches: other tests run in parallel.
            double ms = double.MaxValue;
            for (int batch = 0; batch < 10; batch++)
            {
                const int Runs = 20;
                long t0 = Stopwatch.GetTimestamp();
                for (int i = 0; i < Runs; i++)
                    fog.See(x, y);
                ms = System.Math.Min(ms, Stopwatch.GetElapsedTime(t0).TotalMilliseconds / Runs);
            }
            // SPEC §9's frame budget is ~16 ms; the target is 0.2 ms, with a margin for a slow test machine.
            Assert.True(ms < 1.0, $"{map}: {ms:F3} ms a walk");
        }
    }

    [Fact]
    public void TheDimMapIsDarkAndGreyer()
    {
        WadArchive wad = Synthetic();
        Playpal playpal = Playpal.Load(wad);
        Colormap colormap = Colormap.Load(wad);
        byte[] map = FogOfWar.DimMap(playpal, colormap, 20, 100);
        Assert.Equal(256, map.Length);
        foreach (int i in Enumerable.Range(0, 256))
        {
            (byte r, byte g, byte b) = playpal.GetColor(0, i);
            (byte dr, byte dg, byte db) = playpal.GetColor(0, map[i]);
            Assert.True(dr + dg + db <= r + g + b + 24, $"index {i} brighter");
        }
        // Row 0 with no grey: each texel's own colour (or an equal one).
        byte[] same = FogOfWar.DimMap(playpal, colormap, 0, 0);
        Assert.All(Enumerable.Range(0, 256), i => Assert.Equal(playpal.GetColor(0, colormap.GetMap(0)[i]), playpal.GetColor(0, same[i])));
    }
}
