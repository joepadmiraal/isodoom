using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T5.5: p_floor.c (<c>EV_DoFloor</c> with every floor type,
/// <c>T_MoveFloor</c>, <c>EV_BuildStairs</c>) and p_spec.c
/// <c>EV_DoDonut</c> on the synthetic specials map (E1M2; see
/// <c>SyntheticIwad.BuildSpecialsMap</c>): the corridor (sector 0, floor 0,
/// ceiling 128) and its alcoves (alcove i is sector 1 + i, floors
/// <see cref="SyntheticIwad.AlcoveFloor"/>: alcove 3 at 136 between 96 and
/// 176; the tag-5 alcoves 3, 7 and 12). The tests call the <c>EV_</c>
/// functions with line 0 (tag 5, the corridor in front) or give a line and a
/// sector another tag; the player waits in room R (sector 25), apart.
/// </summary>
public class FloorTests
{
    private const int FRACUNIT = 1 << 16;

    /// <summary>Alcove 3's sector (tag 5): floor 136, ceiling 280; neighbours the corridor (0), alcove 2 (96) and alcove 4 (176).</summary>
    private const int A3 = 1 + 3;

    private static int F(int units) => units * FRACUNIT;

    private static World Specials()
    {
        var wad = new WadArchive([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);
        var world = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium), Tweaks.Vanilla)
        {
            textures = Textures.R_InitTextures(wad),
        };
        world.G_DoLoadLevel(Level.Load(wad, "E1M2"));
        // Out of the way: room R.
        world.PlaceMobj(world.players[0].mo!, F(64), F(196));
        return world;
    }

    /// <summary>Line 0 (the corridor's south wall, tag 5) with tag <paramref name="tag"/>, which <paramref name="sectors"/> get.</summary>
    private static line_t Tagged(World world, short tag, params int[] sectors)
    {
        foreach (int s in sectors)
            world.sectors[s].tag = tag;
        line_t line = world.lines[0];
        line.tag = tag;
        return line;
    }

    private static void Run(World world, int tics)
    {
        for (int i = 0; i < tics; i++)
            world.G_Ticker(new ticcmd_t());
    }

    /// <summary>Runs until <paramref name="sector"/> is idle; returns the tics it took.</summary>
    private static int RunUntilIdle(World world, int sector, int max = 2000)
    {
        int tics = 0;
        while (world.sectors[sector].specialdata != null)
        {
            Assert.True(tics < max, $"sector {sector} still moving after {max} tics");
            world.G_Ticker(new ticcmd_t());
            tics++;
        }
        return tics;
    }

    private static floormove_t FloorOf(World world, int sector) => Assert.IsType<floormove_t>(world.sectors[sector].specialdata);

    [Fact]
    public void EachFloorTypeGoesWhereVanillaSends()
    {
        // (type, sector, its floor before, destination, speed in units, crush)
        (floor_e Type, int Sector, int From, int To, int Speed, bool Crush)[] cases =
        [
            (floor_e.lowerFloor, 1 + 4, 176, 136, 1, false),         // to the highest neighbour (alcove 3)
            (floor_e.lowerFloorToLowest, A3, 136, 0, 1, false),      // to the lowest (the corridor)
            (floor_e.turboLower, 1 + 4, 176, 144, 4, false),         // highest neighbour + 8, 4 a tic
            (floor_e.raiseFloor, 0, 0, 96, 1, false),                // the lowest neighbouring ceiling (alcove 22's)
            (floor_e.raiseFloorCrush, 0, 0, 88, 1, true),            // ... less 8, crushing
            (floor_e.raiseFloorToNearest, A3, 136, 176, 1, false),   // the next higher neighbour
            (floor_e.raiseFloorTurbo, A3, 136, 176, 4, false),
            (floor_e.raiseFloor24, A3, 136, 160, 1, false),
            (floor_e.raiseFloor512, A3, 136, 648, 1, false),
            (floor_e.raiseFloor24AndChange, A3, 136, 160, 1, false),
        ];
        foreach ((floor_e Type, int Sector, int From, int To, int Speed, bool Crush) c in cases)
        {
            World world = Specials();
            line_t line = Tagged(world, 7, c.Sector);
            Assert.Equal(F(c.From), world.sectors[c.Sector].floorheight);
            Assert.Equal(1, world.EV_DoFloor(line, c.Type));
            floormove_t floor = FloorOf(world, c.Sector);
            Assert.Equal(c.Type, floor.type);
            Assert.Equal(F(c.To), floor.floordestheight);
            Assert.Equal(F(c.Speed), floor.speed);
            Assert.Equal(c.Crush, floor.crush);
            Assert.Equal(c.To > c.From ? 1 : -1, floor.direction);
            Assert.Equal(0, world.EV_DoFloor(line, c.Type)); // busy: nothing new

            // One step a tic, then the destination (pastdest) on the tic after the last full step.
            Run(world, 1);
            Assert.Equal(F(c.From + floor.direction * c.Speed), world.sectors[c.Sector].floorheight);
            int steps = (System.Math.Abs(c.To - c.From) + c.Speed - 1) / c.Speed;
            int tics = 1 + RunUntilIdle(world, c.Sector);
            Assert.Equal(steps + (System.Math.Abs(c.To - c.From) % c.Speed == 0 ? 1 : 0), tics);
            Assert.Equal(F(c.To), world.sectors[c.Sector].floorheight);
            Assert.Equal(think_t.REMOVED, floor.function);
            Assert.Contains(world.StartedSounds(), s => s.sfx == sfxenum_t.sfx_pstop && s.sector == world.sectors[c.Sector]);
        }
    }

    [Fact]
    public void ALowerFloorBelowItsHighestNeighbourJumpsUp()
    {
        // Vanilla: "lower" to the highest neighbouring floor, which is above alcove 3: there at once.
        World world = Specials();
        line_t line = Tagged(world, 7, A3);
        world.EV_DoFloor(line, floor_e.lowerFloor);
        Assert.Equal(F(176), FloorOf(world, A3).floordestheight);
        Run(world, 1);
        Assert.Equal(F(176), world.sectors[A3].floorheight);
        Assert.Null(world.sectors[A3].specialdata);
    }

    [Fact]
    public void TheTaggedSectorsAllMoveAndBusyOnesAreSkipped()
    {
        World world = Specials();
        line_t line = world.lines[0]; // tag 5: alcoves 3, 7 and 12
        int[] tagged = [1 + 3, 1 + 7, 1 + 12];
        world.EV_DoFloor(line, floor_e.raiseFloor24);
        Assert.All(tagged, s => Assert.IsType<floormove_t>(world.sectors[s].specialdata));
        Assert.Equal(0, world.EV_DoFloor(line, floor_e.lowerFloorToLowest));
        Assert.All(tagged, s => Assert.Equal(floor_e.raiseFloor24, FloorOf(world, s).type));
        // A tag no sector has.
        line.tag = 9;
        Assert.Equal(0, world.EV_DoFloor(line, floor_e.raiseFloor24));
    }

    [Fact]
    public void RaiseFloor24AndChangeTakesTheLinesFrontFlatAndSpecialAtOnce()
    {
        World world = Specials();
        line_t line = Tagged(world, 7, A3);
        world.sectors[A3].special = 7;
        world.sectors[0].special = 5; // the corridor, line 0's front sector
        Assert.Equal("LAVA1", world.sectors[A3].floorpic);
        world.EV_DoFloor(line, floor_e.raiseFloor24AndChange);
        Assert.Equal("FLOOR1", world.sectors[A3].floorpic);
        Assert.Equal(5, world.sectors[A3].special);
    }

    [Fact]
    public void LowerAndChangeTakesTheLowestNeighboursFlatAndSpecialWhenDone()
    {
        World world = Specials();
        line_t line = Tagged(world, 7, A3);
        world.sectors[0].special = 5;
        world.sectors[A3].special = 7;
        world.EV_DoFloor(line, floor_e.lowerAndChange);
        floormove_t floor = FloorOf(world, A3);
        Assert.Equal("FLOOR1", floor.texture); // the corridor's, at the destination 0
        Assert.Equal(5, floor.newspecial);
        Assert.Equal("LAVA1", world.sectors[A3].floorpic); // not yet
        RunUntilIdle(world, A3);
        Assert.Equal(0, world.sectors[A3].floorheight);
        Assert.Equal("FLOOR1", world.sectors[A3].floorpic);
        Assert.Equal(5, world.sectors[A3].special);
    }

    [Fact]
    public void LowerAndChangeFollowsTheNeighboursLineCountAsVanilla()
    {
        // The corridor's lowest neighbour is alcove 23 (-32), but vanilla's loop bound becomes the
        // first two-sided neighbour's line count (alcove 0: 4 lines) and the corridor's fourth line
        // is alcove 0's opening: the search ends there, so the corridor keeps its flat and special 0.
        World world = Specials();
        line_t line = Tagged(world, 7, 0);
        world.sectors[0].special = 5;
        world.EV_DoFloor(line, floor_e.lowerAndChange);
        floormove_t floor = FloorOf(world, 0);
        Assert.Equal(F(-32), floor.floordestheight);
        Assert.Equal("FLOOR1", floor.texture);
        Assert.Equal(0, floor.newspecial);
        RunUntilIdle(world, 0);
        Assert.Equal("FLOOR1", world.sectors[0].floorpic);
        Assert.Equal(0, world.sectors[0].special);
    }

    [Fact]
    public void RaiseToTextureRisesByTheShortestLowerTexture()
    {
        World world = Specials();
        line_t line = Tagged(world, 7, A3);
        // Alcove 3's two-sided lines (its opening, line 6, and the boundaries 55 and 56) draw
        // BRICK1 (64 high) on both sides.
        world.EV_DoFloor(line, floor_e.raiseToTexture);
        Assert.Equal(F(136 + 64), FloorOf(world, A3).floordestheight);

        // PANEL (72) everywhere but one "-": texture 0 (NULLTEX, 64), as vanilla reads it.
        world = Specials();
        line = Tagged(world, 7, A3);
        foreach (int l in new[] { 6, 55, 56 })
        {
            foreach (int s in world.lines[l].sidenum)
                world.sides[s].bottomtexture = "PANEL";
        }
        world.EV_DoFloor(line, floor_e.raiseToTexture);
        Assert.Equal(F(136 + 72), FloorOf(world, A3).floordestheight);
        world = Specials();
        line = Tagged(world, 7, A3);
        foreach (int l in new[] { 6, 55, 56 })
        {
            foreach (int s in world.lines[l].sidenum)
                world.sides[s].bottomtexture = "PANEL";
        }
        world.sides[world.lines[55].sidenum[1]].bottomtexture = "-";
        world.EV_DoFloor(line, floor_e.raiseToTexture);
        Assert.Equal(F(136 + 64), FloorOf(world, A3).floordestheight);

        // Without the texture list the sim cannot know the heights.
        world = Specials();
        world.textures = null;
        Assert.Throws<System.InvalidOperationException>(() => world.EV_DoFloor(Tagged(world, 7, A3), floor_e.raiseToTexture));
    }

    [Fact]
    public void ARisingFloorStopsUnderAThingAndACrushingOneDoesNot()
    {
        // The corridor rises to 96 (88 crushing) under its ceiling of 128: the player (56 high)
        // stops it at 72, unless it crushes.
        foreach (floor_e type in new[] { floor_e.raiseFloor, floor_e.raiseFloorCrush })
        {
            World world = Specials();
            world.PlaceMobj(world.players[0].mo!, F(300), F(-64));
            line_t line = Tagged(world, 7, 0);
            world.EV_DoFloor(line, type);
            Run(world, 100);
            if (type == floor_e.raiseFloor)
            {
                Assert.Equal(F(72), world.sectors[0].floorheight);
                Assert.NotNull(world.sectors[0].specialdata); // still trying
            }
            else
            {
                Assert.Equal(F(88), world.sectors[0].floorheight);
                Assert.Null(world.sectors[0].specialdata);
            }
        }
    }

    [Fact]
    public void AMovingFloorSoundsEveryEighthTic()
    {
        World world = Specials();
        line_t line = Tagged(world, 7, A3);
        world.EV_DoFloor(line, floor_e.lowerFloorToLowest);
        var stnmov = new System.Collections.Generic.List<int>();
        for (int i = 0; i < 40; i++)
        {
            int leveltime = world.leveltime;
            world.G_Ticker(new ticcmd_t());
            if (world.StartedSounds().Any(s => s.sfx == sfxenum_t.sfx_stnmov))
                stnmov.Add(leveltime);
        }
        Assert.Equal(new[] { 0, 8, 16, 24, 32 }, stnmov); // leveltime & 7 == 0
    }

    [Fact]
    public void StairsBuildFromTheTaggedStepAlongItsFrontLinesWhileTheFlatMatches()
    {
        foreach (stair_e type in new[] { stair_e.build8, stair_e.turbo16 })
        {
            World world = Specials();
            // Alcove 4 (176, FLOOR2): its boundary with alcove 3 (line 56) has alcove 4 in front, and so on
            // westwards. Give alcoves 3 and 1 FLOOR2 too: the steps are alcoves 4, 3, 2, 1 and 0.
            world.sectors[1 + 3].floorpic = "FLOOR2";
            world.sectors[1 + 1].floorpic = "FLOOR2";
            line_t line = Tagged(world, 7, 1 + 4);
            Assert.Equal(1, world.EV_BuildStairs(line, type));
            int size = type == stair_e.build8 ? 8 : 16;
            int speed = type == stair_e.build8 ? FRACUNIT / 4 : 4 * FRACUNIT;
            for (int k = 0; k < 5; k++)
            {
                floormove_t step = FloorOf(world, 1 + 4 - k);
                Assert.Equal(F(176 + size * (k + 1)), step.floordestheight);
                Assert.Equal(speed, step.speed);
                Assert.Equal(1, step.direction);
            }
            Assert.Null(world.sectors[0].specialdata);
            Assert.Null(world.sectors[1 + 5].specialdata);
            for (int k = 0; k < 5; k++)
                RunUntilIdle(world, 1 + 4 - k, 4000);
            for (int k = 0; k < 5; k++)
                Assert.Equal(F(176 + size * (k + 1)), world.sectors[1 + 4 - k].floorheight);
        }
    }

    [Fact]
    public void ABusyStepStillCountsAndEndsTheStairs()
    {
        World world = Specials();
        world.sectors[1 + 3].floorpic = "FLOOR2";
        world.sectors[1 + 1].floorpic = "FLOOR2";
        world.EV_DoFloor(Tagged(world, 8, 1 + 2), floor_e.raiseFloor24); // alcove 2 busy
        line_t line = Tagged(world, 7, 1 + 4);
        world.EV_BuildStairs(line, stair_e.build8);
        Assert.Equal(F(184), FloorOf(world, 1 + 4).floordestheight);
        Assert.Equal(F(192), FloorOf(world, 1 + 3).floordestheight);
        Assert.Equal(floor_e.raiseFloor24, FloorOf(world, 1 + 2).type);
        Assert.Null(world.sectors[1 + 1].specialdata);
        Assert.Equal(0, world.EV_BuildStairs(line, stair_e.build8)); // the first step is busy now
    }

    [Fact]
    public void ADonutRaisesTheRingToTheOuterFloorAndLowersTheHole()
    {
        // E1M2 has no donut: the hole's first line must face the ring. Alcove 4 is the hole with its
        // boundary to alcove 3 (line 56) first, so alcove 3 is the ring, whose first line (its opening,
        // line 6) has the corridor behind: the outer sector, floor 0, flat FLOOR1.
        World world = Specials();
        sector_t hole = world.sectors[1 + 4], ring = world.sectors[A3];
        hole.lines = [.. hole.lines.OrderBy(l => l.Index == 56 ? 0 : 1)];
        ring.floorheight = F(-16);
        ring.special = 7;
        line_t line = Tagged(world, 7, 1 + 4);
        Assert.Equal(1, world.EV_DoDonut(line));
        floormove_t slime = FloorOf(world, A3), pit = FloorOf(world, 1 + 4);
        Assert.Equal((floor_e.donutRaise, 1, FRACUNIT / 2, 0, "FLOOR1"), (slime.type, slime.direction, slime.speed, slime.floordestheight, slime.texture));
        Assert.Equal((floor_e.lowerFloor, -1, FRACUNIT / 2, 0), (pit.type, pit.direction, pit.speed, pit.floordestheight));
        Assert.Equal(0, world.EV_DoDonut(line));
        RunUntilIdle(world, A3);
        Assert.Equal(0, ring.floorheight);
        Assert.Equal("FLOOR1", ring.floorpic);
        Assert.Equal(0, ring.special);
        RunUntilIdle(world, 1 + 4);
        Assert.Equal(0, hole.floorheight);
        Assert.Equal("FLOOR2", hole.floorpic); // the hole keeps its flat
    }

    [Fact]
    public void ADonutRingWithAOneSidedLineEmulatesVanillasOverrun()
    {
        // Alcove 3 as the hole: its first line faces the corridor (the ring), whose first line (0)
        // is one-sided: Chocolate Doom's emulation, height 0 (the ring keeps its flat here).
        World world = Specials();
        world.sectors[0].floorheight = F(-8);
        line_t line = Tagged(world, 7, A3);
        Assert.Equal(1, world.EV_DoDonut(line));
        Assert.Equal(0, FloorOf(world, 0).floordestheight);
        Assert.Null(FloorOf(world, 0).texture);
        RunUntilIdle(world, 0);
        Assert.Equal("FLOOR1", world.sectors[0].floorpic);
        Assert.Equal(0, world.sectors[0].floorheight);

        // A hole whose first line is one-sided: Chocolate Doom stops (vanilla reads garbage).
        world = Specials();
        sector_t a5 = world.sectors[1 + 5];
        a5.lines = [.. a5.lines.OrderBy(l => (l.flags & Line.ML_TWOSIDED) != 0 ? 1 : 0)];
        Assert.Equal(1, world.EV_DoDonut(Tagged(world, 7, 1 + 5)));
        Assert.Null(a5.specialdata);
    }
}
