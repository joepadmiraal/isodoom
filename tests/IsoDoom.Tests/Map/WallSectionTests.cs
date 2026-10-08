using System;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;
using static IsoDoom.Map.Fixed;
using static IsoDoom.Map.WallPlane;
using static IsoDoom.Map.WallSectionKind;

namespace IsoDoom.Tests.Map;

/// <summary>
/// T2.3: wall sections (r_segs.c <c>R_StoreWallRange</c> per linedef side).
/// Every section of the synthetic <c>E1M1</c> is checked exactly (runs in
/// CI); DOOM1 E1M1 is spot-checked against hand-worked vanilla values, and
/// every E1 and Doom II map builds with only the known missing textures.
/// </summary>
public class WallSectionTests
{
    private static WadArchive Synthetic() =>
        new([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);

    private static (Level Map, Textures Textures, WallSections Walls) BuildSynthetic()
    {
        WadArchive wad = Synthetic();
        var map = Level.Load(wad, "E1M1");
        var textures = Textures.R_InitTextures(wad);
        return (map, textures, WallSections.Build(map, textures));
    }

    private static PlaneRef P(WallPlane plane, int units = 0) => new(plane, units << FRACBITS);

    private static WallSection Find(WallSections walls, int line, int side, WallSectionKind kind) =>
        walls.Sections.Single(s => s.Line.Index == line && s.Side == side && s.Kind == kind);

    private static void AssertEmpty(WallSection s)
    {
        (int bottom, int top) = s.Span();
        Assert.True(top <= bottom, $"{s}: span {bottom >> FRACBITS}..{top >> FRACBITS} is not empty");
    }

    private static (int, int) Units((int Bottom, int Top) span) => (span.Bottom >> FRACBITS, span.Top >> FRACBITS);

    // line, side, kind, texture ("-" for none), bottom, top, row 0, x offset (map units)
    private static readonly (int Line, int Side, WallSectionKind Kind, string Texture, PlaneRef Bottom, PlaneRef Top, PlaneRef TextureTop, int XOffset)[] _syntheticSections =
    [
        (0, 0, Middle, "BRICK1", P(FrontFloor), P(FrontCeiling), P(FrontCeiling), 0),
        // L1: PANEL (72 high) upper pegged to the back ceiling, lower to the back floor.
        (1, 0, Upper, "PANEL", P(BackCeiling), P(FrontCeiling), P(BackCeiling, 72), 0),
        (1, 0, Lower, "PANEL", P(FrontFloor), P(BackFloor), P(BackFloor), 0),
        (1, 1, Upper, "-", P(BackCeiling), P(FrontCeiling), P(BackCeiling), 0),
        (1, 1, Lower, "-", P(FrontFloor), P(BackFloor), P(BackFloor), 0),
        (1, 1, MaskedMiddle, "GRATE", P(HigherFloor), P(LowerCeiling), P(LowerCeiling), 0),
        (2, 0, Middle, "BRKPNL", P(FrontFloor), P(FrontCeiling), P(FrontCeiling), 32),
        (3, 0, Middle, "BRICK1", P(FrontFloor), P(FrontCeiling), P(FrontCeiling), 0),
        (4, 0, Middle, "BRKPNL", P(FrontFloor), P(FrontCeiling), P(FrontCeiling), 0),
        (5, 0, Middle, "BRICK1", P(FrontFloor), P(FrontCeiling), P(FrontCeiling, 8), 0),
        (6, 0, Middle, "BRKPNL", P(FrontFloor), P(FrontCeiling), P(FrontCeiling), 0),
        (7, 0, Middle, "BRICK1", P(FrontFloor), P(FrontCeiling), P(FrontCeiling), 0),
        // L8: room C | closed door D.
        (8, 0, Upper, "PANEL", P(BackCeiling), P(FrontCeiling), P(BackCeiling, 72), 0),
        (8, 0, Lower, "-", P(FrontFloor), P(BackFloor), P(BackFloor), 0),
        (8, 1, Upper, "-", P(BackCeiling), P(FrontCeiling), P(BackCeiling), 0),
        (8, 1, Lower, "-", P(FrontFloor), P(BackFloor), P(BackFloor), 0),
        (9, 0, Middle, "BRICK1", P(FrontFloor), P(FrontCeiling), P(FrontCeiling), 0),
        // L10: one-sided, lower unpegged: the texture's bottom at the floor, + rowoffset 8.
        (10, 0, Middle, "BRICK1", P(FrontFloor), P(FrontCeiling), P(FrontFloor, 64 + 8), 0),
        (11, 0, Middle, "-", P(FrontFloor), P(FrontCeiling), P(FrontFloor), 0),
        // L12: door D | courtyard A, upper and lower unpegged: both anchored at the front ceiling (+ rowoffset 4).
        (12, 0, Upper, "-", P(BackCeiling), P(FrontCeiling), P(FrontCeiling), 0),
        (12, 0, Lower, "-", P(FrontFloor), P(BackFloor), P(FrontCeiling), 0),
        (12, 1, Upper, "PANEL", P(BackCeiling), P(FrontCeiling), P(FrontCeiling, 4), 16),
        (12, 1, Lower, "BRICK1", P(FrontFloor), P(BackFloor), P(FrontCeiling, 4), 16),
        (13, 0, Middle, "BRICK1", P(FrontFloor), P(FrontCeiling), P(FrontFloor, 64), 0),
        (14, 0, Middle, "BRKPNL", P(FrontFloor), P(FrontCeiling), P(FrontCeiling), 0),
        // L15: sky on both sides: no upper, and the unpegged lower anchors at worldtop = the back ceiling.
        (15, 0, Lower, "BRKPNL", P(FrontFloor), P(BackFloor), P(BackCeiling, 8), 8),
        (15, 0, MaskedMiddle, "GRATE", P(HigherFloor), P(LowerCeiling), P(HigherFloor, 64 + 8), 8),
        (15, 1, Lower, "-", P(FrontFloor), P(BackFloor), P(BackCeiling), 0),
        (15, 1, MaskedMiddle, "GRATE", P(HigherFloor), P(LowerCeiling), P(HigherFloor, 64), 0),
        (16, 0, Middle, "BRKPNL", P(FrontFloor), P(FrontCeiling), P(FrontCeiling), 0),
        (17, 0, Middle, "BRICK1", P(FrontFloor), P(FrontCeiling), P(FrontCeiling), 0),
        (18, 0, Middle, "BRICK1", P(FrontFloor), P(FrontCeiling), P(FrontCeiling), 0),
        (19, 0, Middle, "BRICK1", P(FrontFloor), P(FrontCeiling), P(FrontCeiling), 0),
    ];

    /// <summary>
    /// T5.1: the sections whose texture bottom is pegged (the texture height is
    /// in the anchor's offset) are the pegged uppers and the lower-unpegged
    /// middles and masked middles; <see cref="WallSection.TextureTopFor"/>
    /// moves only their anchor with another texture's height.
    /// </summary>
    [Fact]
    public void BottomPeggedSectionsAnchorOnTheirTextureHeight()
    {
        (_, _, WallSections walls) = BuildSynthetic();
        (int Index, int Side, WallSectionKind Kind)[] pegged = [.. walls.Sections.Where(s => s.BottomPegged).Select(s => (s.Line.Index, s.Side, s.Kind))];
        Assert.Equal(new[]
        {
            (1, 0, Upper), (1, 1, Upper), (8, 0, Upper), (8, 1, Upper), (10, 0, Middle), (11, 0, Middle), (13, 0, Middle),
            (15, 0, MaskedMiddle), (15, 1, MaskedMiddle),
        }, pegged);
        foreach (WallSection s in walls.Sections)
        {
            int now = s.TextureTop.Evaluate(s.FrontSector, s.BackSector);
            Assert.Equal(now, s.TextureTopFor(s.TextureHeight));
            Assert.Equal(s.BottomPegged ? now + (16 << FRACBITS) : now, s.TextureTopFor(s.TextureHeight + (16 << FRACBITS)));
        }
    }

    [Fact]
    public void SyntheticSectionsAreExact()
    {
        (Level map, Textures textures, WallSections walls) = BuildSynthetic();
        Assert.Equal(
            _syntheticSections.Select(e => (e.Line, e.Side, e.Kind, e.Texture, e.Bottom, e.Top, e.TextureTop, e.XOffset)),
            walls.Sections.Select(s => (s.Line.Index, s.Side, s.Kind, s.Texture == 0 ? "-" : textures.TextureDefs[s.Texture].Name,
                s.Bottom, s.Top, s.TextureTop, s.TextureOffset >> FRACBITS)));
        Assert.Empty(walls.Missing);
        foreach (WallSection s in walls.Sections)
        {
            Assert.Same(map.Sides[s.Line.SideNum[s.Side]], s.SideDef);
            Assert.Equal(s.Texture == 0 ? 0 : textures.TextureDefs[s.Texture].Height << FRACBITS, s.TextureHeight);
            Assert.Equal(s.Kind != MaskedMiddle, s.TilesVertically);
            // Every line of the synthetic map is ML_TWOSIDED exactly when it has two sides.
            Assert.Equal(s.Line.SideNum[1] == -1 ? null : map.Sides[s.Line.SideNum[s.Side ^ 1]].Sector, s.BackSector);
        }
    }

    [Fact]
    public void SyntheticSectionsRunAlongTheirSide()
    {
        (Level map, _, WallSections walls) = BuildSynthetic();
        WallSection front = Find(walls, 12, 0, Upper), back = Find(walls, 12, 1, Upper);
        Assert.Same(map.Vertexes[10], front.V1);
        Assert.Same(map.Vertexes[15], front.V2);
        Assert.Same(map.Vertexes[15], back.V1); // the back side runs the other way, its sector on the right
        Assert.Same(map.Vertexes[10], back.V2);
        Assert.Same(map.Sectors[4], back.FrontSector);
        Assert.Same(map.Sectors[3], back.BackSector);
    }

    [Fact]
    public void SyntheticSpansAndAnchorsAtLoadHeights()
    {
        (_, _, WallSections walls) = BuildSynthetic();
        // West room 0/128 | east room 16/112: upper 112..128 with PANEL's bottom row at 112, lower 0..16 from row 0 at 16.
        Assert.Equal((112, 128), Units(Find(walls, 1, 0, Upper).Span()));
        Assert.Equal((0, 16), Units(Find(walls, 1, 0, Lower).Span()));
        // From the east room, the back planes are outside the front span: nothing drawn.
        AssertEmpty(Find(walls, 1, 1, Upper));
        AssertEmpty(Find(walls, 1, 1, Lower));
        // The GRATE (64 high) hangs from the opening's top: 112 - 64 = 48 .. 112.
        WallSection grate = Find(walls, 1, 1, MaskedMiddle);
        Assert.Equal((48, 112), Units(grate.Span()));
        Assert.Equal(112 << FRACBITS, grate.TextureTop.Evaluate(grate.FrontSector, grate.BackSector));

        // The closed door: C sees an upper over its whole height, the door sees nothing.
        Assert.Equal((0, 128), Units(Find(walls, 8, 0, Upper).Span()));
        AssertEmpty(Find(walls, 8, 0, Lower));
        AssertEmpty(Find(walls, 8, 1, Upper));
        AssertEmpty(Find(walls, 8, 1, Lower));
        AssertEmpty(Find(walls, 11, 0, Middle));
        AssertEmpty(Find(walls, 12, 0, Upper));
        AssertEmpty(Find(walls, 12, 0, Lower));
        // From the courtyard (-16/256 sky): upper 0..256 and lower -16..0, both from row 0 at 256 + 4.
        WallSection upper = Find(walls, 12, 1, Upper), lower = Find(walls, 12, 1, Lower);
        Assert.Equal((0, 256), Units(upper.Span()));
        Assert.Equal((-16, 0), Units(lower.Span()));
        Assert.Equal(260 << FRACBITS, upper.TextureTop.Evaluate(upper.FrontSector, upper.BackSector));
        Assert.Equal(260 << FRACBITS, lower.TextureTop.Evaluate(lower.FrontSector, lower.BackSector));

        // Sky on both sides of L15 (A 256, B 192): no upper; the lower -16..64 is anchored at B's ceiling.
        Assert.DoesNotContain(walls.Sections, s => s.Line.Index == 15 && s.Kind == Upper);
        WallSection skyLower = Find(walls, 15, 0, Lower);
        Assert.Equal((-16, 64), Units(skyLower.Span()));
        Assert.Equal(200 << FRACBITS, skyLower.TextureTop.Evaluate(skyLower.FrontSector, skyLower.BackSector));
        // The unpegged GRATE stands on the higher floor (64) with row 0 at 64 + 64, raised by A's
        // rowoffset 8 to 136 on A's side, so it is drawn once from 136 down to 72 (not tiled down to 64).
        WallSection skyGrate = Find(walls, 15, 0, MaskedMiddle);
        Assert.Equal((72, 136), Units(skyGrate.Span()));
        Assert.Equal((64, 128), Units(Find(walls, 15, 1, MaskedMiddle).Span()));
    }

    [Fact]
    public void SectionsFollowMovingSectors()
    {
        (Level map, _, WallSections walls) = BuildSynthetic();
        Sector door = map.Sectors[3];
        WallSection roomSide = Find(walls, 8, 0, Upper), courtyardSide = Find(walls, 12, 1, Upper);
        door.CeilingHeight = 120 << FRACBITS; // the door opens
        Assert.Equal((120, 128), Units(roomSide.Span()));
        Assert.Equal(120 + 72 << FRACBITS, roomSide.TextureTop.Evaluate(roomSide.FrontSector, roomSide.BackSector)); // pegged: moves with the door
        Assert.Equal((120, 256), Units(courtyardSide.Span()));
        Assert.Equal(260 << FRACBITS, courtyardSide.TextureTop.Evaluate(courtyardSide.FrontSector, courtyardSide.BackSector)); // unpegged: stays

        // A lift: lowering the east room's floor below the west room's gives the east side a lower.
        map.Sectors[1].FloorHeight = -32 << FRACBITS;
        Assert.Equal((-32, 0), Units(Find(walls, 1, 1, Lower).Span()));
        AssertEmpty(Find(walls, 1, 0, Lower));
    }

    [Fact]
    public void MissingTexturesAreReportedNotThrown()
    {
        WadArchive wad = Synthetic();
        var map = Level.Load(wad, "E1M1");
        var textures = Textures.R_InitTextures(wad);
        map.Sides[0].MidTexture = "-";     // one-sided wall
        map.Sides[1].TopTexture = "-";     // L1's upper, 16 high
        map.Sides[9].BottomTexture = "-";  // already "-": no height, not reported
        map.Sides[18].TopTexture = "-";    // sky on both sides: no upper, not reported
        map.Sides[2].MidTexture = "-";     // no masked middle: never reported
        var walls = WallSections.Build(map, textures);
        Assert.Equal([new MissingWallTexture(0, 0, Middle), new MissingWallTexture(1, 0, Upper)], walls.Missing);
        Assert.Equal(0, Find(walls, 0, 0, Middle).Texture);
        Assert.DoesNotContain(walls.Sections, s => s.Line.Index == 1 && s.Kind == MaskedMiddle);

        // An unknown name is vanilla's R_TextureNumForName error, even in a slot that is not drawn.
        map.Sides[0].TopTexture = "NOSUCH";
        WadFormatException e = Assert.Throws<WadFormatException>(() => WallSections.Build(map, textures));
        Assert.Contains("NOSUCH", e.Message);
    }

    [Fact]
    public void TwoSidedLineWithoutTheFlagIsOneSidedOnBothSides()
    {
        // p_setup.c P_LoadSegs: no ML_TWOSIDED, no backsector, whatever sidenum[1] says.
        (Level map, Textures textures, _) = BuildSynthetic();
        map.Lines[1].Flags = 0;
        var walls = WallSections.Build(map, textures);
        WallSection front = Find(walls, 1, 0, Middle), back = Find(walls, 1, 1, Middle);
        Assert.Null(front.BackSector);
        Assert.Equal("-", map.Sides[1].MidTexture);
        Assert.Contains(new MissingWallTexture(1, 0, Middle), walls.Missing);
        Assert.Equal(textures.R_TextureNumForName("GRATE"), back.Texture);
    }

    // ---- DOOM1 v1.9 ----

    private static (Textures, WallSections) BuildDoom1(string name)
    {
        var wad = WadArchive.Open(TestWads.RequireDoom1());
        var textures = Textures.R_InitTextures(wad);
        return (textures, WallSections.Build(Level.Load(wad, name), textures));
    }

    /// <summary>
    /// The texture row vanilla draws at height <paramref name="z"/> (map
    /// units): <c>R_DrawColumn</c>'s <c>frac = dc_texturemid + (y - centery) * fracstep</c>
    /// is <c>texturemid - (z - viewz)</c> for the screen row of height z, so the
    /// row is <c>(texturemid + viewz - z) &gt;&gt; FRACBITS</c>, wrapped by 128 (<c>&amp; 127</c>).
    /// </summary>
    private static int Row(WallSection s, int z) =>
        ((s.TextureTop.Evaluate(s.FrontSector, s.BackSector) - (z << FRACBITS)) >> FRACBITS) & 127;

    [Fact]
    public void Doom1E1M1PeggingSpotChecks()
    {
        (Textures textures, WallSections walls) = BuildDoom1("E1M1");
        string Name(WallSection s) => textures.TextureDefs[s.Texture].Name;

        // Start room, line 39 (front sector 37, -8/120; back 38, 0/72; ML_DONTPEGTOP):
        // the COMPUTE2 upper (56 high) hangs from the front ceiling, rw_toptexturemid = worldtop,
        // so its rows 0..47 cover 120..72 and the bottom 8 rows are never seen. The STEP6 lower
        // is pegged: rw_bottomtexturemid = worldlow, row 0 at the back floor (0).
        WallSection computer = Find(walls, 39, 0, Upper);
        Assert.Equal(("COMPUTE2", P(FrontCeiling)), (Name(computer), computer.TextureTop));
        Assert.Equal((72, 120), Units(computer.Span()));
        Assert.Equal((0, 47), (Row(computer, 120), Row(computer, 73)));
        WallSection step = Find(walls, 39, 0, Lower);
        Assert.Equal(("STEP6", P(BackFloor), (-8, 0)), (Name(step), step.TextureTop, Units(step.Span())));
        Assert.Equal(7, Row(step, -7));

        // Line 64 (no pegging flags, rowoffset 104): STARG3 upper over the start room's
        // west door, vtop = backsector->ceilingheight + textureheight, + rowoffset: 120 + 128 + 104.
        WallSection starg = Find(walls, 64, 0, Upper);
        Assert.Equal(("STARG3", P(BackCeiling, 128 + 104), (120, 224)), (Name(starg), starg.TextureTop, Units(starg.Span())));
        Assert.Equal((352 - 224) & 127, Row(starg, 224));

        // Line 65, a one-sided door track with ML_DONTPEGBOTTOM: vtop = floorheight + textureheight.
        WallSection track = Find(walls, 65, 0, Middle);
        Assert.Equal(("DOORTRAK", P(FrontFloor, 128), (-8, 120)), (Name(track), track.TextureTop, Units(track.Span())));
        Assert.Equal(127, Row(track, -7));

        // Line 73, the zig-zag steps to the nukage: lower unpegged SLADWALL, rw_bottomtexturemid = worldtop (224).
        WallSection slad = Find(walls, 73, 0, Lower);
        Assert.Equal(("SLADWALL", P(FrontCeiling), (-8, 88)), (Name(slad), slad.TextureTop, Units(slad.Span())));
        Assert.Equal((224 - 88) & 127, Row(slad, 88));

        // Line 151, a door (special 1): BIGDOOR2 upper pegged to the (closed) door's ceiling at 0.
        WallSection door = Find(walls, 151, 0, Upper);
        Assert.Equal(("BIGDOOR2", P(BackCeiling, 128), (0, 88)), (Name(door), door.TextureTop, Units(door.Span())));
        Assert.Equal((1, 0), (door.Line.Special, Row(door, 128)));

        // Line 200: pegged BROWNGRN lower with rowoffset 32: rw_bottomtexturemid = worldlow + 32.
        WallSection browngrn = Find(walls, 200, 0, Lower);
        Assert.Equal(("BROWNGRN", P(BackFloor, 32), (-48, 96)), (Name(browngrn), browngrn.TextureTop, Units(browngrn.Span())));

        // Line 130, the outside window: F_SKY1 on both sides (264 and 128): no upper, as worldtop = worldhigh.
        Assert.DoesNotContain(walls.Sections, s => s.Line.Index == 130 && s.Kind == Upper);
        Assert.Equal("STARG3", textures.TextureDefs[textures.R_TextureNumForName(Find(walls, 130, 0, Lower).SideDef.TopTexture)].Name);

        // Line 164 (ML_DONTPEGBOTTOM, sky on both sides): the BROWN144 lower is anchored at worldtop,
        // which the sky rule set to the back ceiling.
        WallSection brown = Find(walls, 164, 0, Lower);
        Assert.Equal(("BROWN144", P(BackCeiling), (-80, -56)), (Name(brown), brown.TextureTop, Units(brown.Span())));

        // Line 298, the shareware grate (two-sided, both sides the same sector): masked BRNBIGL and BRNBIGR.
        WallSection grate = Find(walls, 298, 0, MaskedMiddle);
        Assert.Equal(("BRNBIGL", P(LowerCeiling), (-24, 104)), (Name(grate), grate.TextureTop, Units(grate.Span())));
        Assert.Equal("BRNBIGR", Name(Find(walls, 298, 1, MaskedMiddle)));

        // Line 318, the exit door wall: EXITDOOR with textureoffset 88.
        WallSection exit = Find(walls, 318, 0, Middle);
        Assert.Equal(("EXITDOOR", 88 << FRACBITS), (Name(exit), exit.TextureOffset));
        Assert.Empty(walls.Missing);
    }

    /// <summary>The sides of DOOM1 v1.9 that vanilla draws with a <c>-</c> texture (line:side and U/L/M).</summary>
    public static TheoryData<string, string> E1Missing() => new()
    {
            { "E1M1", "" },
            { "E1M2", "134:1U 574:1U" },
            { "E1M3", "" },
            { "E1M4", "321:0L 327:0L 338:0L 346:0L 693:1U" },
            { "E1M5", "" },
            { "E1M6", "" },
            { "E1M7", "450:1U 744:1L 745:1L 746:1L 747:1L" },
            { "E1M8", "" },
            { "E1M9", "" },
    };

    [Theory]
    [MemberData(nameof(E1Missing))]
    public void EveryE1MapBuildsWithOnlyTheKnownMissingTextures(string name, string missing)
    {
        (_, WallSections walls) = BuildDoom1(name);
        Assert.Equal(missing, Format(walls.Missing));
        CheckConsistent(walls);
    }

    /// <summary>The same for DOOM II v1.666 (other versions only have to build).</summary>
    private static readonly Dictionary<string, string> _doom2V1666Missing = new()
    {
            { "MAP01", "334:1L 335:1L 369:1L" },
            { "MAP02", "347:0L 348:0L 358:0L 359:0L" },
            { "MAP03", "" },
            { "MAP04", "3:1L 108:0U 109:0U 110:0U 111:0U 127:0U 128:0U 187:1L 200:1L 201:1L 456:1U" },
            { "MAP05", "489:1U 561:1U" },
            { "MAP06", "" },
            { "MAP07", "" },
            { "MAP08", "101:1U 270:1U 276:1U" },
            { "MAP09", "628:1L" },
            { "MAP10", "" },
            { "MAP11", "" },
            { "MAP12", "648:1L 773:1L" },
            { "MAP13", "305:1L 308:1L 318:1L 331:1L 622:1U 879:1U" },
            { "MAP14", "429:0U 430:0U 531:0U 607:1U 608:1U 609:1U 786:1U 787:1U 788:1U 789:1U 791:1U 792:1U 838:1L 845:1L 1023:1U 1133:1U 1134:1U 1135:1U 1137:1U 1138:1U 1139:1U 1140:1U 1141:1U 1142:1U" },
            { "MAP15", "94:1U 95:1U 989:1U" },
            { "MAP16", "162:1U 303:0U 304:0U 328:1L" },
            { "MAP17", "379:1U" },
            { "MAP18", "451:1U 451:1L 459:1U 459:1L 574:0U" },
            { "MAP19", "355:1U 455:1L 736:0U 1181:1U" },
            { "MAP20", "73:1L 74:1L 75:1L 76:1L 453:1L" },
            { "MAP21", "110:1U 111:1U 112:1U 113:1U 134:0U" },
            { "MAP22", "158:0L 442:1U 443:1U 530:1U 530:1L 539:1U 539:1L 542:1U 542:1L 543:1U 543:1L 544:1U 544:1L 545:1U 545:1L 548:1U 548:1L" },
            { "MAP23", "" },
            { "MAP24", "687:1L 688:1L 960:1L 1012:1L 1013:1L" },
            { "MAP25", "110:0U 111:0U 112:0U 113:0U 436:0L 436:1U" },
            { "MAP26", "761:1U 826:1U 827:1U 828:1U 829:1U" },
            { "MAP27", "582:1U 810:1L" },
            { "MAP28", "38:1L 39:1L 103:0U 104:0U 105:0U 106:0U 107:0U 161:1L 170:1L 213:1L 214:1L 215:1L 221:0L 388:1L 391:0U 531:1U 547:1U 548:1U" },
            { "MAP29", "405:1L 406:1L 407:1L 408:1L 516:1L 517:1L 518:1L 519:1L 524:1L 525:1L 526:1L 527:1L 603:1U 655:1U 656:1U 657:1U 658:1U 1138:1L 1139:1L 1140:1L 1141:1L 1146:1L 1147:1L 1148:1L 1149:1L" },
            { "MAP30", "" },
            { "MAP31", "32:0L 34:0L 41:0L 43:0L 57:0L 137:0L 163:0L" },
            { "MAP32", "" },
    };

    public static TheoryData<string> Doom2Maps()
    {
        var data = new TheoryData<string>();
        for (int i = 1; i <= 32; i++)
            data.Add($"MAP{i:00}");
        return data;
    }

    [Theory]
    [MemberData(nameof(Doom2Maps))]
    public void EveryDoom2MapBuilds(string name)
    {
        var wad = WadArchive.Open(TestWads.RequireDoom2());
        var textures = Textures.R_InitTextures(wad);
        var walls = WallSections.Build(Level.Load(wad, name), textures);
        CheckConsistent(walls);
        if (TestWads.Doom2Md5 == TestWads.Doom2V1666Md5)
            Assert.Equal(_doom2V1666Missing[name], Format(walls.Missing));
    }

    private static string Format(IEnumerable<MissingWallTexture> missing) =>
        string.Join(" ", missing.Select(m => $"{m.Line}:{m.Side}{m.Kind.ToString()[0]}"));

    private static void CheckConsistent(WallSections walls)
    {
        foreach (WallSection s in walls.Sections)
        {
            bool twoSided = s.BackSector is not null;
            Assert.Equal(twoSided, s.Kind != Middle);
            if (s.Kind == MaskedMiddle)
                Assert.NotEqual(0, s.Texture);
            if (s.Kind == Upper)
                Assert.False(s.FrontSector.CeilingPic == WallSections.SKYFLATNAME && s.BackSector!.CeilingPic == WallSections.SKYFLATNAME);
        }
        // Each side has at most one section of each kind, and sections come in line order.
        Assert.Equal(walls.Sections.Count, walls.Sections.Select(s => (s.Line.Index, s.Side, s.Kind)).Distinct().Count());
        Assert.True(walls.Sections.Select(s => s.Line.Index).SequenceEqual(walls.Sections.Select(s => s.Line.Index).Order()));
    }
}
