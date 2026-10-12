using System;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Render;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Render;

/// <summary>T6.13s: the height cues' ledge edges, strips on the floor along each side of the two-sided lines (<see cref="LedgeEdges"/>).</summary>
public class LedgeEdgesTests
{
    private static (Level Level, SubsectorPolygons Polygons, LedgeEdges Ledges) Build(WadArchive wad, string map)
    {
        var level = Level.Load(wad, map);
        var polygons = SubsectorPolygons.Build(level);
        return (level, polygons, LedgeEdges.Build(level, polygons));
    }

    private static WadArchive Synthetic() => new([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);

    private static WadArchive Strip() => new([WadFile.FromBytes(TestMap.Strip(0, 0, 96,
        new TestMap.Room(64, 0, 128), new TestMap.Room(64, 32, 128), new TestMap.Room(64, 40, 128)).Build(), "testmap.wad")]);

    private static double Length(Line line)
    {
        double dx = (line.V2.X - (double)line.V1.X) / 65536, dy = (line.V2.Y - (double)line.V1.Y) / 65536;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    [Fact]
    public void ALedgeIsADropTheStepDoesNotClimb()
    {
        Assert.False(LedgeEdges.Shows(24, 0));
        Assert.True(LedgeEdges.Shows(24.5, 0));
        Assert.True(LedgeEdges.Shows(25, 0));
        Assert.True(LedgeEdges.Shows(-40, -72));
        // From below: never.
        Assert.False(LedgeEdges.Shows(0, 64));
        Assert.False(LedgeEdges.Shows(0, 0));
    }

    [Fact]
    public void TheShadingRoundsToNearestLevelAndClamps()
    {
        Assert.Equal(0, LedgeEdges.ShadeLevels(0, 0));
        Assert.Equal(2, LedgeEdges.ShadeLevels(48, 0));
        Assert.Equal(-2, LedgeEdges.ShadeLevels(0, 48));
        // Halves round up, as the shader's floor(x + 0.5).
        Assert.Equal(1, LedgeEdges.ShadeLevels(12, 0));
        Assert.Equal(0, LedgeEdges.ShadeLevels(11.9, 0));
        Assert.Equal(0, LedgeEdges.ShadeLevels(-12, 0));
        Assert.Equal(-1, LedgeEdges.ShadeLevels(-12.1, 0));
        Assert.Equal(2, LedgeEdges.ShadeLevels(36, 0));
        Assert.Equal(1, LedgeEdges.ShadeLevels(35.9, 0));
        Assert.Equal(3, LedgeEdges.ShadeLevels(160, 100));
        // At most ShadeMax either way.
        Assert.Equal(LedgeEdges.ShadeMax, LedgeEdges.ShadeLevels(LedgeEdges.ShadeStep * (LedgeEdges.ShadeMax + 0.4), 0));
        Assert.Equal(LedgeEdges.ShadeMax, LedgeEdges.ShadeLevels(1000, 0));
        Assert.Equal(-LedgeEdges.ShadeMax, LedgeEdges.ShadeLevels(-1000, 0));
        Assert.Equal(-LedgeEdges.ShadeMax, LedgeEdges.ShadeLevels(0, 1000));
    }

    [Fact]
    public void TheModesParseTheirNames()
    {
        foreach (HeightCueMode mode in new[] { HeightCueMode.Off, HeightCueMode.Edges, HeightCueMode.Shade, HeightCueMode.Both })
            Assert.Equal(mode, LedgeEdges.Parse(LedgeEdges.Name(mode), "test"));
        Assert.Equal(["off", "edges", "shade", "both"], new[] { HeightCueMode.Off, HeightCueMode.Edges, HeightCueMode.Shade, HeightCueMode.Both }.Select(LedgeEdges.Name));
        Assert.Equal(HeightCueMode.Off, LedgeEdges.Parse("vanilla", "test"));
        Assert.Equal(HeightCueMode.Both, HeightCueMode.Edges | HeightCueMode.Shade);
        ArgumentException e = Assert.Throws<ArgumentException>(() => LedgeEdges.Parse("Edges", "--level-height-cues"));
        Assert.Contains("--level-height-cues", e.Message);
        Assert.Throws<ArgumentException>(() => LedgeEdges.Parse("", "test"));
    }

    [Fact]
    public void EachSideOfARoomBoundaryGetsAStripOnItsFloor()
    {
        // Three rooms 64 wide, 96 deep, floors 0, 32 and 40: boundary 1 (x 64, northwards, room 1
        // in front) is a ledge seen from room 1, boundary 2 (x 128) a step either way.
        (Level level, SubsectorPolygons polygons, LedgeEdges ledges) = Build(Strip(), "E1M1");
        int b1 = level.Lines.Single(l => l.FrontSector?.Index == 1 && l.BackSector?.Index == 0).Index;
        int b2 = level.Lines.Single(l => l.FrontSector?.Index == 2 && l.BackSector?.Index == 1).Index;
        Assert.Equal([(b1, 0), (b1, 1), (b2, 0), (b2, 1)], ledges.Edges.Select(e => (e.Line, e.Side)).Order());
        foreach (LedgeEdge e in ledges.Edges)
        {
            Assert.Equal(96 * LedgeEdges.Width, e.Pieces.Sum(p => Math.Abs(WallCaps.Area(p))), 6);
            Assert.Equal(e.Side == 0 ? level.Lines[e.Line].FrontSector!.Index : level.Lines[e.Line].BackSector!.Index, e.Sector);
            Assert.Equal(e.Side == 0 ? level.Lines[e.Line].BackSector!.Index : level.Lines[e.Line].FrontSector!.Index, e.Other);
        }
        // Boundary 1's front side: on room 1's floor, x 64 to 67; its back side on room 0's, x 61 to 64.
        LedgeEdge front = ledges.Edges.Single(e => e.Line == b1 && e.Side == 0), back = ledges.Edges.Single(e => e.Line == b1 && e.Side == 1);
        Assert.All(front.Pieces.SelectMany(p => p), v => Assert.InRange(v.X, 64 - 1e-9, 64 + LedgeEdges.Width + 1e-9));
        Assert.All(back.Pieces.SelectMany(p => p), v => Assert.InRange(v.X, 64 - LedgeEdges.Width - 1e-9, 64 + 1e-9));
        Assert.Equal(LedgeEdges.Width, front.Inside(64 + LedgeEdges.Width, 50), 9);
        Assert.Equal(LedgeEdges.Width, back.Inside(64 - LedgeEdges.Width, 50), 9);
        // The side's start is its first vertex as seen from it: going along it, its sector on the right.
        Assert.Equal(0, front.Along(64, 0), 9);
        Assert.Equal(0, back.Along(64, 96), 9);
        // Only room 1's side of boundary 1 is a ledge (32 over 0); boundary 2 is an 8-unit step.
        int[] floors = [.. level.Sectors.Select(s => s.FloorHeight >> 16)];
        Assert.Equal([(b1, 0)], ledges.Edges.Where(e => LedgeEdges.Shows(floors[e.Sector], floors[e.Other])).Select(e => (e.Line, e.Side)));
        CheckPieces(level, polygons, ledges);
    }

    [Fact]
    public void AOneSidedRoomHasNoLedgeEdges()
    {
        var level = Level.Load(new WadArchive([WadFile.FromBytes(TestMap.Polygon(0, 128, (0, 0), (0, 88), (88, 88), (88, 0)).Build(), "testmap.wad")]), "E1M1");
        Assert.Empty(LedgeEdges.Build(level, SubsectorPolygons.Build(level)).Edges);
    }

    [Theory]
    [InlineData("E1M1")]
    [InlineData("E1M2")]
    public void TheSyntheticLedgeEdgesLieAlongTheirLinesOnTheirFloors(string map)
    {
        (Level level, SubsectorPolygons polygons, LedgeEdges ledges) = Build(Synthetic(), map);
        Assert.NotEmpty(ledges.Edges);
        CheckSides(level, polygons, ledges);
        CheckPieces(level, polygons, ledges);
        // A side along a straight run of its sector's floor gets the whole strip.
        Assert.Contains(ledges.Edges, e => Math.Abs(e.Pieces.Sum(p => Math.Abs(WallCaps.Area(p))) - Length(level.Lines[e.Line]) * LedgeEdges.Width) < 1e-6);
    }

    [Fact]
    public void TheSyntheticMapsHaveLedgesShown()
    {
        // The render check's map (E1M1) needs a ledge shown at load; E1M2 (the specials) has many.
        foreach ((string map, int least) in new[] { ("E1M1", 1), ("E1M2", 10) })
        {
            (Level level, _, LedgeEdges ledges) = Build(Synthetic(), map);
            int shown = ledges.Edges.Count(e => LedgeEdges.Shows(level.Sectors[e.Sector].FloorHeight / 65536.0, level.Sectors[e.Other].FloorHeight / 65536.0));
            Assert.True(shown >= least, $"{map}: {shown} ledge edges shown at load");
        }
    }

    public static TheoryData<string> Doom1Maps() => new() { "E1M1", "E1M2", "E1M3", "E1M4", "E1M5", "E1M6", "E1M7", "E1M8", "E1M9" };

    [Theory]
    [MemberData(nameof(Doom1Maps))]
    public void Doom1LedgeEdgesLieAlongTheirLinesOnTheirFloors(string map)
    {
        (Level level, SubsectorPolygons polygons, LedgeEdges ledges) = Build(WadArchive.Open(TestWads.RequireDoom1()), map);
        CheckSides(level, polygons, ledges);
        CheckPieces(level, polygons, ledges);
        Assert.Contains(ledges.Edges, e => LedgeEdges.Shows(level.Sectors[e.Sector].FloorHeight / 65536.0, level.Sectors[e.Other].FloorHeight / 65536.0));
    }

    // Edges only on the sides of two-sided lines between two sectors, in line order, front side
    // first, with the side's sectors; every such side whose sector has a floor there has one.
    private static void CheckSides(Level level, SubsectorPolygons polygons, LedgeEdges ledges)
    {
        var floors = FloorTriangles.Build(level, polygons);
        var expected = new List<(int, int)>();
        foreach (Line line in level.Lines)
        {
            if (line.FrontSector is not Sector front || line.BackSector is not Sector back || front == back || Length(line) == 0)
                continue;
            for (int side = 0; side < 2; side++)
            {
                // The strip's middle, half its width in: on the side's sector's floor when the sector has one there.
                double x1 = line.V1.X / 65536.0, y1 = line.V1.Y / 65536.0, x2 = line.V2.X / 65536.0, y2 = line.V2.Y / 65536.0;
                double length = Length(line), nx = (y2 - y1) / length, ny = -(x2 - x1) / length; // the front side's normal, into it
                double sign = side == 0 ? 1 : -1, d = LedgeEdges.Width / 2;
                int at = floors.SectorAt((int)Math.Round(((x1 + x2) / 2 + sign * nx * d) * 65536), (int)Math.Round(((y1 + y2) / 2 + sign * ny * d) * 65536));
                if (at == (side == 0 ? front : back).Index)
                    expected.Add((line.Index, side));
            }
        }
        var got = ledges.Edges.Select(e => (e.Line, e.Side)).ToList();
        Assert.Equal(got.Order(), got);
        Assert.Subset(got.ToHashSet(), expected.ToHashSet());
        foreach (LedgeEdge e in ledges.Edges)
        {
            Line line = level.Lines[e.Line];
            Assert.NotNull(line.FrontSector);
            Assert.NotNull(line.BackSector);
            Assert.NotEqual(line.FrontSector, line.BackSector);
            Assert.Equal((e.Side == 0 ? line.FrontSector : line.BackSector)!.Index, e.Sector);
            Assert.Equal((e.Side == 0 ? line.BackSector : line.FrontSector)!.Index, e.Other);
        }
    }

    // Every piece: clockwise and convex, on its sector's floor, within Width of its line and along it.
    private static void CheckPieces(Level level, SubsectorPolygons polygons, LedgeEdges ledges)
    {
        var floors = FloorTriangles.Build(level, polygons);
        foreach (LedgeEdge e in ledges.Edges)
        {
            Assert.NotEmpty(e.Pieces);
            double length = Length(level.Lines[e.Line]);
            Assert.Equal(1, e.Dx * e.Dx + e.Dy * e.Dy, 9);
            foreach ((double X, double Y)[] p in e.Pieces)
            {
                Assert.True(p.Length >= 3);
                // Clockwise, as the floors: the interior on the right of each edge, every turn the same way.
                Assert.True(WallCaps.Area(p) < 0, $"line {e.Line} side {e.Side}: a piece of area {WallCaps.Area(p)}");
                for (int i = 0; i < p.Length; i++)
                {
                    (double ax, double ay) = p[i];
                    (double bx, double by) = p[(i + 1) % p.Length];
                    (double cx, double cy) = p[(i + 2) % p.Length];
                    Assert.True((bx - ax) * (cy - by) - (by - ay) * (cx - bx) <= 1e-6, $"line {e.Line} side {e.Side}: not convex at {(bx, by)}");
                }
                double mx = p.Average(v => v.X), my = p.Average(v => v.Y);
                Assert.Equal(e.Sector, floors.SectorAt((int)Math.Round(mx * 65536), (int)Math.Round(my * 65536)));
                foreach ((double x, double y) in p)
                {
                    Assert.InRange(e.Inside(x, y), -1e-6, LedgeEdges.Width + 1e-6);
                    Assert.InRange(e.Along(x, y), -1e-6, length + 1e-6);
                }
            }
        }
    }
}
