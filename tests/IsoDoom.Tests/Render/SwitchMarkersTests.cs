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

/// <summary>
/// T6.13p: the switch markers, decals on the floor in front of switches on walls facing away from the camera (<see cref="SwitchMarkers"/>);
/// T10.6a: and a lower switch's top marker on the raised sector behind it (<see cref="SwitchMarkers.Tops"/>);
/// T10.6e: and an upper switch's over a door, on the door's lid (<see cref="SwitchMarker.Lid"/>).
/// </summary>
public class SwitchMarkersTests
{
    // The game camera looks north-west from the south-east (IsoCamera.Yaw 45°): towards it, flattened.
    private static (double X, double Y) ToCamera => (Math.Sqrt(0.5), -Math.Sqrt(0.5));

    // The same in 3D at the default pitch (IsoCamera.DefaultPitch, 55°), map axes, z up.
    private static (double X, double Y, double Z) ToCamera3 =>
        (Math.Cos(55 * Math.PI / 180) * Math.Sqrt(0.5), -Math.Cos(55 * Math.PI / 180) * Math.Sqrt(0.5), Math.Sin(55 * Math.PI / 180));

    // Whether a top marker shows from the game camera with its sectors' heights at the level's start.
    private static bool TopShows(SwitchMarker m) => SwitchMarkers.TopShows(m,
        (m.Section.BackSector!.FloorHeight - m.Section.FrontSector.FloorHeight) / 65536.0, ToCamera3.X, ToCamera3.Y, ToCamera3.Z);

    private static (Level Level, WallSections Walls, SubsectorPolygons Polygons, Textures Textures, SwitchMarkers Markers) Build(WadArchive wad, string map,
        GameMode mode = GameMode.shareware)
    {
        var level = Level.Load(wad, map);
        var textures = Textures.R_InitTextures(wad);
        var walls = WallSections.Build(level, textures);
        var polygons = SubsectorPolygons.Build(level);
        var rows = new Dictionary<string, (int Top, int Bottom)>(StringComparer.OrdinalIgnoreCase);
        var differences = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        foreach (switchlist_t s in Switches.For(mode))
        {
            int a = textures.R_CheckTextureNumForName(s.name1), b = textures.R_CheckTextureNumForName(s.name2);
            if (a > 0 && b > 0)
            {
                rows[s.name1] = rows[s.name2] = SwitchMarkers.Band(textures.R_GenerateComposite(a), textures.R_GenerateComposite(b));
                differences[s.name1] = differences[s.name2] = SwitchMarkers.RowDifferences(textures.R_GenerateComposite(a), textures.R_GenerateComposite(b));
            }
        }
        return (level, walls, polygons, textures, SwitchMarkers.Build(walls, polygons, rows, DoorLids.Build(level, FloorTriangles.Build(level, polygons)), level, differences));
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
    public void BestWindowIsWhereThePairDiffersMost()
    {
        int[] differences = [0, 1, 0, 0, 5, 5, 0, 9, 0, 0];
        Assert.Equal(4, SwitchMarkers.BestWindow(differences, (0, 10), 4)); // 5 + 5 + 0 + 9 = 19, against rows 5-8's 14 and 7-10's 9
        Assert.Equal(7, SwitchMarkers.BestWindow(differences, (0, 10), 1));
        Assert.Equal(2, SwitchMarkers.BestWindow([0, 0, 0, 0, 0], (2, 5), 2)); // none differ: the top
        Assert.Equal(2, SwitchMarkers.BestWindow(differences, (2, 5), 3)); // as deep as the band: its top
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

    [Fact]
    public void TheSyntheticLowerSwitchGetsATopMarkerOnTheLedge()
    {
        (Level level, _, SubsectorPolygons polygons, Textures textures, SwitchMarkers markers) = Build(Synthetic(), "E1M1");
        // E1M1's L15 (x 912, from y 128 down to -128) faces west into courtyard A (floor -16), its
        // lower SW1BRCOM under ledge B (floor 64, x 912-1040): a top marker on B, L6's (one-sided) none
        // (L8's, an upper over door D, is T10.6e's).
        SwitchMarker top = Assert.Single(markers.Tops, m => !m.Lid);
        SwitchMarker floor = markers.Markers.Single(m => m.Section.Line.Index == 15);
        Assert.Equal(15, top.Section.Line.Index);
        Assert.True(top.Top);
        Assert.False(floor.Top);
        Assert.Equal(WallSectionKind.Lower, top.Section.Kind);
        Assert.Equal((4, 5), (floor.Sector, top.Sector));
        Assert.Equal(floor.Rows, top.Rows);
        (int first, int last) = SwitchMarkers.Band(textures.R_GenerateComposite(textures.R_TextureNumForName("SW1BRCOM")), textures.R_GenerateComposite(textures.R_TextureNumForName("SW2BRCOM")));
        Assert.Equal((first, last), top.Rows);
        // The whole side, Depth deep behind the wall (B is 128 wide), clockwise as the floors.
        Assert.Equal(256.0 * top.Depth, top.Pieces.Sum(p => Math.Abs(WallCaps.Area(p))), 6);
        Assert.All(top.Pieces, p => Assert.True(WallCaps.Area(p) < 0));
        // Folded back over the wall's top edge: the band's top row along the edge, its rows going
        // down into the block; the columns mirrored as the floor marker's (V1, y 128, shows the last).
        Assert.Equal(first, top.Row(912, 0), 9);
        Assert.Equal(last, top.Row(912 + top.Depth, 0), 9);
        Assert.Equal(256, top.Column(912, 128), 9);
        Assert.Equal(0, top.Column(912, -128), 9);
        Assert.Equal(floor.Column(900, 40), top.Column(930, 40), 9);
        // From the game camera B's 80-unit rise hides 40 units in front of the wall, more than half
        // the floor marker's depth: the top marker shows. A rise of 16 (8 hidden) would not; nor
        // seen straight down, or with the wall facing the camera.
        Assert.True(SwitchMarkers.Shows(top, ToCamera.X, ToCamera.Y));
        Assert.True(TopShows(top));
        Assert.Equal(80 * Math.Sqrt(0.5) / Math.Tan(55 * Math.PI / 180), SwitchMarkers.Hidden(top, 80, ToCamera3.X, ToCamera3.Y, ToCamera3.Z), 9);
        Assert.False(SwitchMarkers.TopShows(top, 16, ToCamera3.X, ToCamera3.Y, ToCamera3.Z));
        Assert.False(SwitchMarkers.TopShows(top, 80, 0, 0, 1));
        Assert.False(SwitchMarkers.TopShows(top, 80, -ToCamera3.X, -ToCamera3.Y, ToCamera3.Z));
        CheckOnTheirFloors(level, polygons, markers);
        // E1M2's switches are all on one-sided walls: no top markers.
        Assert.Empty(Build(Synthetic(), "E1M2").Markers.Tops);
    }

    [Fact]
    public void TheSyntheticDoorSwitchGetsATopMarkerOnTheLid()
    {
        (Level level, _, SubsectorPolygons polygons, Textures textures, SwitchMarkers markers) = Build(Synthetic(), "E1M1");
        // T10.6e: E1M1's L8 (x 640, from y 128 down to -128) faces west into room C, its upper
        // SW1BRCOM over door D (sector 3, x 640-656, closed): a top marker on D, on its lid only.
        SwitchMarker top = Assert.Single(markers.Tops, m => m.Lid);
        SwitchMarker floor = markers.Markers.Single(m => m.Section.Line.Index == 8);
        Assert.Equal(8, top.Section.Line.Index);
        Assert.True(top.Top);
        Assert.False(floor.Lid);
        Assert.Equal(WallSectionKind.Upper, top.Section.Kind);
        Assert.Equal((2, 3), (floor.Sector, top.Sector));
        Assert.Equal(floor.Rows, top.Rows);
        // Clipped to the door's 16 units: the 16 rows of the band where the pair differs most, folded back over the edge.
        Assert.Equal(256.0 * 16, top.Pieces.Sum(p => Math.Abs(WallCaps.Area(p))), 6);
        int[] differences = SwitchMarkers.RowDifferences(textures.R_GenerateComposite(textures.R_TextureNumForName("SW1BRCOM")), textures.R_GenerateComposite(textures.R_TextureNumForName("SW2BRCOM")));
        Assert.Equal(SwitchMarkers.BestWindow(differences, top.Rows, 16) - top.Rows.Top, top.RowShift);
        Assert.Equal(top.Rows.Top + top.RowShift, top.Row(640, 0), 9);
        Assert.Equal(top.Rows.Top + top.RowShift + 16, top.Row(656, 0), 9);
        Assert.Equal(floor.Column(630, 40), top.Column(650, 40), 9);
        // Its lid at C's ceiling (128), courtyard A's (sector 4, sky at 256) the door's other side: the marker
        // lies at the top of A's upper wall (the shader's max), a rise of 256 hiding the floor marker.
        Assert.Equal(4, top.Beyond);
        Assert.True(SwitchMarkers.TopShows(top, 256, ToCamera3.X, ToCamera3.Y, ToCamera3.Z));
        CheckOnTheirFloors(level, polygons, markers);
        // Without the lid sectors, none (and L15's stays).
        var walls = WallSections.Build(level, Textures.R_InitTextures(Synthetic()));
        var rows = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase) { ["SW1BRCOM"] = top.Rows, ["SW2BRCOM"] = top.Rows };
        Assert.Equal([15], SwitchMarkers.Build(walls, polygons, rows).Tops.Select(m => m.Section.Line.Index));
    }

    [Fact]
    public void Doom2Map09DoorFaceSwitchesGetTopMarkersOnTheirLids()
    {
        // The sweep's MAP09 lines 210-216: SW*LION over doors, facing away; 209-215 face the camera.
        (Level level, _, SubsectorPolygons polygons, _, SwitchMarkers markers) = Build(WadArchive.Open(TestWads.RequireDoom2()), "MAP09", GameMode.commercial);
        foreach (int line in new[] { 210, 212, 214, 216 })
        {
            SwitchMarker top = markers.Tops.Single(m => m.Section.Line.Index == line);
            Assert.True(top.Lid, $"line {line}");
            Assert.Equal(WallSectionKind.Upper, top.Section.Kind);
            Assert.Equal(top.Section.BackSector!.Index, top.Sector);
            Assert.True(SwitchMarkers.Shows(top, ToCamera.X, ToCamera.Y), $"line {line}");
        }
        CheckOnTheirFloors(level, polygons, markers);
    }

    [Fact]
    public void DoomE2M5DoorFaceSwitchesGetTopMarkersOnTheirLids()
    {
        // The sweep's doom.wad E2M5 sides, upper SW1WOOD and others over doors, facing away.
        int[] lines = [94, 169, 357, 359, 391, 395, 444, 454, 503, 549, 555, 565, 610, 623, 729, 873, 884, 1130, 1322];
        (Level level, _, SubsectorPolygons polygons, _, SwitchMarkers markers) = Build(WadArchive.Open(TestWads.RequireDoom()), "E2M5", GameMode.retail);
        foreach (int line in lines)
        {
            SwitchMarker top = markers.Tops.Single(m => m.Section.Line.Index == line);
            Assert.True(top.Lid, $"line {line}");
            Assert.True(SwitchMarkers.Shows(top, ToCamera.X, ToCamera.Y), $"line {line}");
        }
        CheckOnTheirFloors(level, polygons, markers);
    }

    [Fact]
    public void DoorFaceMarkersLieAtTheTopTheCameraSees()
    {
        // doom.wad E3M7 line 980: SW1LION over door 126 (floor -88), its room's ceiling 8 (the lid), the door's other
        // side (sector 125) at 40: that side's upper wall faces the camera and hides the lid, so the marker lies at 40.
        (Level level, _, _, _, SwitchMarkers markers) = Build(WadArchive.Open(TestWads.RequireDoom()), "E3M7", GameMode.retail);
        SwitchMarker top = markers.Tops.Single(m => m.Section.Line.Index == 980);
        Assert.True(top.Lid);
        Assert.Equal((126, 125), (top.Sector, top.Beyond));
        Assert.Equal(40, level.Sectors[top.Beyond].CeilingHeight >> 16);
        // An 8-thick door shows the 8 rows of the band where the pair differs most.
        Assert.InRange(top.RowShift, 0, top.Depth - 8);
    }

    [Fact]
    public void Doom2Map01CounterSwitchGetsATopMarker()
    {
        // The user's report: line 75, SW1COMP on the west riser of block sector 46 (floor 112 over 48).
        (Level level, _, SubsectorPolygons polygons, _, SwitchMarkers markers) = Build(WadArchive.Open(TestWads.RequireDoom2()), "MAP01", GameMode.commercial);
        SwitchMarker top = markers.Tops.Single(m => m.Section.Line.Index == 75);
        Assert.Equal(46, top.Sector);
        Assert.Equal("SW1COMP", SwitchMarkers.TextureName(top.Section), ignoreCase: true);
        Assert.Equal((-1, 0), (Math.Round(top.Normal.X, 9), Math.Round(top.Normal.Y, 9)));
        Assert.Equal(top.Length * top.Depth, top.Pieces.Sum(p => Math.Abs(WallCaps.Area(p))), 3);
        Assert.True(TopShows(top));
        CheckOnTheirFloors(level, polygons, markers);
    }

    [Fact]
    public void TntMap01ComputerSwitchesGetTopMarkers()
    {
        // Lines 314 and 318: SW1COMP on the west faces of the computer blocks, sectors 71 and 72
        // (floor 138), in front of them the step sector 70 (floor 8).
        (Level level, _, SubsectorPolygons polygons, _, SwitchMarkers markers) = Build(WadArchive.Open(TestWads.RequireTnt()), "MAP01", GameMode.commercial);
        foreach ((int line, int sector) in new[] { (314, 71), (318, 72) })
        {
            SwitchMarker top = markers.Tops.Single(m => m.Section.Line.Index == line);
            Assert.Equal(sector, top.Sector);
            Assert.Equal(70, top.Section.FrontSector.Index);
            Assert.Equal("SW1COMP", SwitchMarkers.TextureName(top.Section), ignoreCase: true);
            Assert.Equal(top.Length * top.Depth, top.Pieces.Sum(p => Math.Abs(WallCaps.Area(p))), 3);
            Assert.True(TopShows(top), $"line {line}");
        }
        CheckOnTheirFloors(level, polygons, markers);
    }

    [Theory]
    [MemberData(nameof(Doom1Maps))]
    public void Doom1TopMarkersAreTheLowerSwitchesWithASectorBehind(string map)
    {
        (Level level, _, SubsectorPolygons polygons, _, SwitchMarkers markers) = Build(WadArchive.Open(TestWads.RequireDoom1()), map);
        // T10.6e: and the upper switches over a door with a lid, on its lid.
        DoorLids lids = DoorLids.Build(level, FloorTriangles.Build(level, polygons));
        Assert.Equal(markers.Markers.Where(m => (m.Section.Kind == WallSectionKind.Lower && m.Section.BackSector is not null) || SwitchMarkers.IsOverDoor(m.Section, lids))
            .Select(m => (m.Section.Line.Index, m.Section.Side)), markers.Tops.Select(m => (m.Section.Line.Index, m.Section.Side)));
        Assert.All(markers.Tops, m => Assert.Equal(m.Section.Kind == WallSectionKind.Upper, m.Lid));
        CheckOnTheirFloors(level, polygons, markers);
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

    // Every piece lies on its marker's sector's floor, in front of its wall within its depth (a top marker's behind it).
    private static void CheckOnTheirFloors(Level level, SubsectorPolygons polygons, SwitchMarkers markers)
    {
        var floors = FloorTriangles.Build(level, polygons);
        foreach (SwitchMarker m in markers.All)
        {
            Assert.NotEmpty(m.Pieces);
            foreach ((double X, double Y)[] p in m.Pieces)
            {
                double cx = p.Average(v => v.X), cy = p.Average(v => v.Y);
                Assert.Equal(m.Sector, floors.SectorAt((int)Math.Round(cx * 65536), (int)Math.Round(cy * 65536)));
                foreach ((double x, double y) in p)
                {
                    Assert.InRange(m.InFront(x, y), m.Top ? -m.Depth - 1e-6 : -1e-6, m.Top ? 1e-6 : m.Depth + 1e-6);
                    Assert.InRange(m.Along(x, y), -1e-6, m.Length + 1e-6);
                }
            }
        }
    }
}
