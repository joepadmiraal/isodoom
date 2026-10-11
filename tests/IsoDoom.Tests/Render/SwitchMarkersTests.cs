using System;
using System.Collections.Generic;
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

/// <summary>T6.13p: the switch markers, decals on the floor in front of switches on walls facing away from the camera (<see cref="SwitchMarkers"/>).</summary>
public class SwitchMarkersTests
{
    // The game camera looks north-west from the south-east (IsoCamera.Yaw 45°): towards it, flattened.
    private static (double X, double Y) ToCamera => (Math.Sqrt(0.5), -Math.Sqrt(0.5));

    private static (Level Level, WallSections Walls, SubsectorPolygons Polygons, Textures Textures, SwitchMarkers Markers) Build(WadArchive wad, string map,
        GameMode mode = GameMode.shareware)
    {
        var level = Level.Load(wad, map);
        var textures = Textures.R_InitTextures(wad);
        var walls = WallSections.Build(level, textures);
        var polygons = SubsectorPolygons.Build(level);
        var rows = new Dictionary<string, (int Top, int Bottom)>(StringComparer.OrdinalIgnoreCase);
        foreach (switchlist_t s in Switches.For(mode))
        {
            int a = textures.R_CheckTextureNumForName(s.name1), b = textures.R_CheckTextureNumForName(s.name2);
            if (a > 0 && b > 0)
                rows[s.name1] = rows[s.name2] = SwitchMarkers.Band(textures.R_GenerateComposite(a), textures.R_GenerateComposite(b));
        }
        return (level, walls, polygons, textures, SwitchMarkers.Build(walls, polygons, rows));
    }

    private static WadArchive Synthetic() => new([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);

    private static IndexedImage Image(int width, int height, Func<int, int, byte> pixel)
    {
        byte[] pixels = new byte[width * height], opaque = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                pixels[y * width + x] = pixel(x, y);
                opaque[y * width + x] = 1;
            }
        }
        return new IndexedImage(width, height, 0, 0, pixels, opaque);
    }

    // A pair of 64-wide textures that differ in rows [top, bottom).
    private static (int Top, int Bottom) BandOf(int height, int top, int bottom) =>
        SwitchMarkers.Band(Image(64, height, (_, _) => 1), Image(64, height, (x, y) => (byte)(y >= top && y < bottom && x == 30 ? 2 : 1)));

    [Fact]
    public void TheBandIsTheSwitchsRowsWithinItsLimits()
    {
        // 21 rows (as SW1BRCOM's 77-97): as they are.
        Assert.Equal((77, 98), BandOf(128, 77, 98));
        // 10 rows: grown evenly to 16 around them.
        Assert.Equal((83, 99), BandOf(128, 86, 96));
        // 65 rows (SW1SKULL's): their middle 32.
        Assert.Equal((69, 101), BandOf(128, 52, 117));
        // Every row (SW1DIRT's): the texture's middle 32.
        Assert.Equal((48, 80), BandOf(128, 0, 128));
        // Near the texture's top: moved inside it.
        Assert.Equal((0, 16), BandOf(128, 0, 4));
        // No row differs: the middle 16; a texture shorter than 16: all of it.
        Assert.Equal((56, 72), BandOf(128, 0, 0));
        Assert.Equal((0, 8), BandOf(8, 2, 3));
    }

    [Fact]
    public void TheSyntheticSwitchesGetMarkersThatShowOnlyFacingAway()
    {
        (Level level, _, SubsectorPolygons polygons, _, SwitchMarkers markers) = Build(Synthetic(), "E1M2");
        // E1M2's SW1BRCOM sides: lines 0 (the corridor's south wall, facing north), 1 (its west
        // wall, facing east), 27 (alcove 0's north wall, facing south), 84 and 85 (room S's north
        // wall, facing south, and its east wall, facing west).
        Assert.Equal([0, 1, 27, 84, 85], markers.Markers.Select(m => m.Section.Line.Index));
        Assert.Equal([0, 85], markers.Markers.Where(m => SwitchMarkers.Shows(m, ToCamera.X, ToCamera.Y)).Select(m => m.Section.Line.Index));
        // Seen straight down, every marker shows (every wall is edge-on).
        Assert.All(markers.Markers, m => Assert.True(SwitchMarkers.Shows(m, 0, 0)));
        // Looking south-east instead, the others.
        Assert.Equal([1, 27, 84], markers.Markers.Where(m => SwitchMarkers.Shows(m, -ToCamera.X, -ToCamera.Y)).Select(m => m.Section.Line.Index));
        CheckOnTheirFloors(level, polygons, markers);
    }

    [Fact]
    public void AMarkerLaysTheSwitchBackOntoTheFloorMirrored()
    {
        (_, _, _, Textures textures, SwitchMarkers markers) = Build(Synthetic(), "E1M2");
        // Line 85: room S's east wall, (288, 240) to (288, 152), facing west into room S (x 160-288).
        SwitchMarker m = markers.Markers.Single(m => m.Section.Line.Index == 85);
        (int top, int bottom) = SwitchMarkers.Band(textures.R_GenerateComposite(textures.R_TextureNumForName("SW1BRCOM")), textures.R_GenerateComposite(textures.R_TextureNumForName("SW2BRCOM")));
        Assert.Equal((top, bottom), m.Rows);
        Assert.Equal((-1, 0), (Math.Round(m.Normal.X, 9), Math.Round(m.Normal.Y, 9)));
        Assert.Equal(88, m.Length, 9);
        // The whole side, Depth deep: 88 × depth.
        Assert.Equal(88.0 * m.Depth, m.Pieces.Sum(p => Math.Abs(WallCaps.Area(p))), 6);
        // At the wall's foot, the switch's lowest row; at the far edge its top. The wall's V1
        // end (y 240) shows its last column, its V2 end (y 152) its first: the camera, behind
        // the wall, sees V1 on its right.
        Assert.Equal(bottom, m.Row(288, 200), 9);
        Assert.Equal(top, m.Row(288 - m.Depth, 200), 9);
        Assert.Equal(88, m.Column(288, 240), 9);
        Assert.Equal(0, m.Column(288, 152), 9);
        // Clockwise, as the floors: the interior on the right of each edge.
        Assert.All(m.Pieces, p => Assert.True(WallCaps.Area(p) < 0));
    }

    [Fact]
    public void ASwitchBesideAStepIsClippedToItsSectorsFloor()
    {
        // Line 0 runs along the whole corridor (768 wide); line 1 is its west wall, 128 long,
        // facing east into the corridor (whose floor is 128 deep): both whole.
        (_, _, _, _, SwitchMarkers markers) = Build(Synthetic(), "E1M2");
        SwitchMarker west = markers.Markers.Single(m => m.Section.Line.Index == 1);
        Assert.Equal(128.0 * west.Depth, west.Pieces.Sum(p => Math.Abs(WallCaps.Area(p))), 6);
        // A ledge 8 units deep, 88 long, the switch on its east wall (line 2, facing west):
        // the marker stops at the ledge's west wall, 8 units in front of it.
        var level = Level.Load(new WadArchive([WadFile.FromBytes(TestMap.Polygon(0, 128, (0, 0), (0, 88), (8, 88), (8, 0)).Build(), "testmap.wad")]), "E1M1");
        foreach (IsoDoom.Map.Side side in level.Sides)
            side.MidTexture = side.Index == level.Lines[2].SideNum[0] ? "SW1BRCOM" : "BRICK1";
        var walls = WallSections.Build(level, Textures.R_InitTextures(Synthetic()));
        var rows = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase) { ["SW1BRCOM"] = (0, 32), ["SW2BRCOM"] = (0, 32) };
        SwitchMarker narrow = SwitchMarkers.Build(walls, SubsectorPolygons.Build(level), rows).Markers.Single();
        Assert.Equal(2, narrow.Section.Line.Index);
        Assert.Equal(88.0 * 8, narrow.Pieces.Sum(p => Math.Abs(WallCaps.Area(p))), 3);
    }

    public static TheoryData<string> Doom1Maps() => new() { "E1M1", "E1M2", "E1M3", "E1M4", "E1M5", "E1M6", "E1M7", "E1M8", "E1M9" };

    [Theory]
    [MemberData(nameof(Doom1Maps))]
    public void Doom1EverySwitchSideHasAMarkerOnItsFloor(string map)
    {
        (Level level, WallSections walls, SubsectorPolygons polygons, _, SwitchMarkers markers) = Build(WadArchive.Open(TestWads.RequireDoom1()), map);
        var switches = Switches.For(GameMode.shareware).SelectMany(s => new[] { s.name1, s.name2 }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sides = walls.Sections.Where(s => s.Texture != 0 && switches.Contains(SwitchMarkers.TextureName(s))).Select(s => (s.Line.Index, s.Side)).Distinct();
        Assert.Equal(sides.Order(), markers.Markers.Select(m => (m.Section.Line.Index, m.Section.Side)));
        CheckOnTheirFloors(level, polygons, markers);
    }

    [Fact]
    public void Doom1ExitSwitchesFacingAwayShowTheirMarkers()
    {
        // The exits on walls facing north or west (the task's list), and E1M3's secret exit.
        foreach ((string map, int line) in new[] { ("E1M2", 873), ("E1M3", 982), ("E1M6", 627), ("E1M7", 810), ("E1M3", 785) })
        {
            (Level level, _, _, _, SwitchMarkers markers) = Build(WadArchive.Open(TestWads.RequireDoom1()), map);
            SwitchMarker m = markers.Markers.Single(m => m.Section.Line.Index == line);
            Assert.True(level.Lines[line].Special is 11 or 51, $"{map} line {line}");
            Assert.True(SwitchMarkers.Shows(m, ToCamera.X, ToCamera.Y), $"{map} line {line}");
            Assert.InRange(m.Depth, SwitchMarkers.MinDepth, SwitchMarkers.MaxDepth);
        }
        // E1M1's exit faces the camera: its marker does not show.
        (_, _, _, _, SwitchMarkers e1m1) = Build(WadArchive.Open(TestWads.RequireDoom1()), "E1M1");
        Assert.All(e1m1.Markers.Where(m => m.Section.Line.Special == 11), m => Assert.False(SwitchMarkers.Shows(m, ToCamera.X, ToCamera.Y)));
    }

    // Every piece lies on its marker's sector's floor, in front of its wall within its depth.
    private static void CheckOnTheirFloors(Level level, SubsectorPolygons polygons, SwitchMarkers markers)
    {
        var floors = FloorTriangles.Build(level, polygons);
        foreach (SwitchMarker m in markers.Markers)
        {
            Assert.NotEmpty(m.Pieces);
            foreach ((double X, double Y)[] p in m.Pieces)
            {
                double cx = p.Average(v => v.X), cy = p.Average(v => v.Y);
                Assert.Equal(m.Sector, floors.SectorAt((int)Math.Round(cx * 65536), (int)Math.Round(cy * 65536)));
                foreach ((double x, double y) in p)
                {
                    Assert.InRange(m.InFront(x, y), -1e-6, m.Depth + 1e-6);
                    Assert.InRange(m.Along(x, y), -1e-6, m.Length + 1e-6);
                }
            }
        }
    }
}
