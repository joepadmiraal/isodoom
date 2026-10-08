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

/// <summary>T6.13e: the wall caps, the strips of void behind one-sided walls (<see cref="WallCaps"/>).</summary>
public class WallCapsTests
{
    private const double T = WallCaps.Thickness;

    private static (Level Level, SubsectorPolygons Polygons, WallCaps Caps) Build(Level level)
    {
        var polygons = SubsectorPolygons.Build(level);
        return (level, polygons, WallCaps.Build(level, polygons));
    }

    private static Level Load(TestMap map)
    {
        var level = Level.Load(new WadArchive([WadFile.FromBytes(map.Build(), "testmap.wad")]), "E1M1");
        foreach (IsoDoom.Map.Side side in level.Sides)
            side.MidTexture = "STARTAN3";
        return level;
    }

    [Fact]
    public void ASquareRoomsCapsFrameItWithMitredCorners()
    {
        (Level level, _, WallCaps caps) = Build(Load(TestMap.Polygon(0, 128, (0, 0), (0, 256), (256, 256), (256, 0))));
        Assert.Equal(4, caps.Caps.Count);
        // A frame T wide around the room: the corners filled once, by the mitres.
        Assert.Equal(Math.Pow(256 + 2 * T, 2) - 256 * 256, caps.Caps.Sum(c => c.Pieces.Sum(Area)), 6);
        foreach (WallCap cap in caps.Caps)
            Assert.Equal(256 * T + T * T, cap.Pieces.Sum(Area), 6);
        CheckBehindTheirWalls(caps);
        Assert.Equal(0, Overlap(caps.Caps), 6);
    }

    [Fact]
    public void ARoomsInnerCornerSplitsItsCapsAlongTheBisector()
    {
        // An L-shaped room (clockwise): its corner at (128, 128) turns into the room, where
        // the two caps behind it would overlap; each keeps the half nearer its wall.
        Level level = Load(TestMap.Polygon(0, 128, (0, 0), (0, 256), (128, 256), (128, 128), (256, 128), (256, 0)));
        (_, SubsectorPolygons polygons, WallCaps caps) = Build(level);
        Assert.Equal(6, caps.Caps.Count);
        CheckBehindTheirWalls(caps);
        CheckInTheVoid(level, polygons, caps);
        Assert.Equal(0, Overlap(caps.Caps), 6);
        // A frame T wide: the perimeter times T, plus a corner square per outer corner, less
        // one per inner corner (5 and 1).
        Assert.Equal(1024 * T + (5 - 1) * T * T, caps.Caps.Sum(c => c.Pieces.Sum(Area)), 6);
        // The two walls at the inner corner (lines 2 and 3): their 128-long strips, less half
        // the corner square they share, plus half a square at their outer corners.
        Assert.Equal(128 * T, caps.Caps.Single(c => c.Line == 2).Pieces.Sum(Area), 6);
        Assert.Equal(128 * T, caps.Caps.Single(c => c.Line == 3).Pieces.Sum(Area), 6);
        Assert.True(caps.Caps.Single(c => c.Line == 2).Pieces.All(p => p.All(v => v.X - 128 <= v.Y - 128 + 1e-6 || v.Y >= 256)));
    }

    [Fact]
    public void CapsFaceTheCameraAsTheirWallsDo()
    {
        (_, _, WallCaps caps) = Build(Load(TestMap.Polygon(0, 128, (0, 0), (0, 256), (256, 256), (256, 0))));
        // The game camera looks north-west from the south-east: the walls facing south and
        // east, the north and west walls, whose caps lie north and west of them.
        (double x, double y) toCamera = (Math.Sqrt(0.5), -Math.Sqrt(0.5));
        string Behind(WallCap c) => c.Normal switch { ( < -0.5, _) => "west", ( > 0.5, _) => "east", (_, > 0.5) => "north", _ => "south" };
        Assert.Equal(["north", "west"], caps.Caps.Where(c => WallCaps.Faces(c, toCamera.x, toCamera.y)).Select(Behind).Order());
        // Seen straight down, every cap shows.
        Assert.All(caps.Caps, c => Assert.True(WallCaps.Faces(c, 0, 0)));
    }

    [Fact]
    public void AStripOfRoomsGetsOneFrame()
    {
        // Two rooms side by side: their shared line is two-sided (no cap), the walls along
        // the strip meet straight on at it, square ends, no overlap.
        Level level = Load(TestMap.Strip(0, 0, 128, new TestMap.Room(128, 0, 128), new TestMap.Room(192, 8, 96)));
        (_, SubsectorPolygons polygons, WallCaps caps) = Build(level);
        Assert.Equal(6, caps.Caps.Count);
        Assert.Equal((320 + 2 * T) * (128 + 2 * T) - 320 * 128, caps.Caps.Sum(c => c.Pieces.Sum(Area)), 6);
        CheckBehindTheirWalls(caps);
        CheckInTheVoid(level, polygons, caps);
        Assert.Equal(0, Overlap(caps.Caps), 6);
    }

    [Fact]
    public void TheSyntheticMapsCapsLieInTheVoid()
    {
        var wad = new WadArchive([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);
        foreach (string map in new[] { "E1M1", "E1M2" })
            CheckMap(Level.Load(wad, map));
    }

    public static TheoryData<string> Doom1Maps() => new() { "E1M1", "E1M2", "E1M3", "E1M4", "E1M5", "E1M6", "E1M7", "E1M8", "E1M9" };

    [Theory]
    [MemberData(nameof(Doom1Maps))]
    public void Doom1CapsLieInTheVoid(string map) => CheckMap(Level.Load(WadArchive.Open(TestWads.RequireDoom1()), map));

    public static TheoryData<string> Doom2Maps()
    {
        var data = new TheoryData<string>();
        for (int i = 1; i <= 32; i++)
            data.Add($"MAP{i:00}");
        return data;
    }

    [Theory]
    [MemberData(nameof(Doom2Maps))]
    public void Doom2CapsLieInTheVoid(string map) => CheckMap(Level.Load(WadArchive.Open(TestWads.RequireDoom2()), map));

    [Fact]
    public void E1M1sDoorFramesGetCaps()
    {
        // DOOM1 E1M1 door sector 4's jambs (lines 150 and 153, DOORTRAK) are capped behind
        // them, at the lid's height in the shader (the door is closed).
        (Level level, _, WallCaps caps) = Build(Level.Load(WadArchive.Open(TestWads.RequireDoom1()), "E1M1"));
        int[] jambs = [.. level.Lines.Where(l => l.FrontSector?.Index == 4 && l.BackSector is null).Select(l => l.Index)];
        Assert.NotEmpty(jambs);
        Assert.All(jambs, j => Assert.Contains(caps.Caps, c => c.Line == j && c.Pieces.Count > 0));
    }

    // Every capped one-sided wall's cap: in the void, behind its wall, no overlap with
    // another wall's cap facing another way (same-facing ones may overlap where their
    // walls are offset, e.g. a step in a wall).
    private static void CheckMap(Level level)
    {
        (_, SubsectorPolygons polygons, WallCaps caps) = Build(level);
        int capped = level.Lines.Count(l => WallCaps.HasCap(level, l));
        Assert.True(caps.Caps.Count > capped * 9 / 10, $"{level.Name}: {caps.Caps.Count} of {capped} walls capped");
        CheckBehindTheirWalls(caps);
        CheckInTheVoid(level, polygons, caps);
        double total = caps.Caps.Sum(c => c.Pieces.Sum(Area));
        double overlap = Overlap(caps.Caps, sameFacing: false);
        Assert.True(overlap < 1, $"{level.Name}: {overlap:F2} square units of caps facing different ways overlap (of {total:F0})");
    }

    private static void CheckBehindTheirWalls(WallCaps caps)
    {
        foreach (WallCap cap in caps.Caps)
        {
            Assert.NotEmpty(cap.Pieces);
            foreach ((double X, double Y)[] piece in cap.Pieces)
            {
                Assert.True(piece.Length >= 3);
                Assert.True(SignedArea(piece) < 0, $"line {cap.Line}: a piece is not clockwise");
                foreach ((double x, double y) in piece)
                    Assert.InRange(cap.Behind(x, y), -1e-6, T + 1e-6);
            }
        }
    }

    // No cap covers a floor (the subsector polygons), but for node builders' slivers.
    private static void CheckInTheVoid(Level level, SubsectorPolygons polygons, WallCaps caps)
    {
        var floors = polygons.Polygons.Where(p => p.Length > 0)
            .Select(p => p.Select(v => (X: v.X / 65536.0, Y: v.Y / 65536.0)).ToArray()).ToList();
        double covered = 0;
        foreach (WallCap cap in caps.Caps)
        {
            foreach ((double X, double Y)[] piece in cap.Pieces)
            {
                foreach ((double X, double Y)[] floor in floors)
                    covered += Intersection(piece, floor);
            }
        }
        Assert.True(covered < 1, $"{level.Name}: caps cover {covered:F2} square units of floor");
    }

    // The area where caps of different walls overlap (only those facing different ways
    // unless sameFacing).
    private static double Overlap(IReadOnlyList<WallCap> caps, bool sameFacing = true)
    {
        double total = 0;
        for (int i = 0; i < caps.Count; i++)
        {
            for (int j = i + 1; j < caps.Count; j++)
            {
                (double ax, double ay) = caps[i].Normal;
                (double bx, double by) = caps[j].Normal;
                if (!sameFacing && ax * bx + ay * by > 0.5)
                    continue;
                foreach ((double X, double Y)[] p in caps[i].Pieces)
                {
                    foreach ((double X, double Y)[] q in caps[j].Pieces)
                        total += Intersection(p, q);
                }
            }
        }
        return total;
    }

    private static double Area((double X, double Y)[] p) => Math.Abs(SignedArea(p));

    private static double SignedArea(IReadOnlyList<(double X, double Y)> p)
    {
        double sum = 0;
        for (int i = 0; i < p.Count; i++)
            sum += p[i].X * p[(i + 1) % p.Count].Y - p[(i + 1) % p.Count].X * p[i].Y;
        return sum / 2;
    }

    // The area of the intersection of two convex polygons (either winding).
    private static double Intersection((double X, double Y)[] a, (double X, double Y)[] b)
    {
        if (a.Max(v => v.X) <= b.Min(v => v.X) || b.Max(v => v.X) <= a.Min(v => v.X)
            || a.Max(v => v.Y) <= b.Min(v => v.Y) || b.Max(v => v.Y) <= a.Min(v => v.Y))
            return 0;
        List<(double X, double Y)> r = [.. a];
        double sign = SignedArea(b) > 0 ? 1 : -1;
        for (int k = 0; k < b.Length && r.Count >= 3; k++)
        {
            (double px, double py) = b[k];
            (double qx, double qy) = b[(k + 1) % b.Length];
            var next = new List<(double X, double Y)>();
            double Side((double X, double Y) v) => sign * ((qx - px) * (v.Y - py) - (qy - py) * (v.X - px));
            for (int i = 0; i < r.Count; i++)
            {
                (double X, double Y) cur = r[i], prev = r[(i + r.Count - 1) % r.Count];
                double sc = Side(cur), sp = Side(prev);
                if ((sc >= 0) != (sp >= 0))
                {
                    double t = sp / (sp - sc);
                    next.Add((prev.X + (cur.X - prev.X) * t, prev.Y + (cur.Y - prev.Y) * t));
                }
                if (sc >= 0)
                    next.Add(cur);
            }
            r = next;
        }
        return r.Count >= 3 ? Math.Abs(SignedArea(r)) : 0;
    }
}
