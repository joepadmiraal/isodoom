using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T5.5: p_ceilng.c (<c>EV_DoCeiling</c> with every ceiling type,
/// <c>T_MoveCeiling</c>, crushers, <c>EV_CeilingCrushStop</c>,
/// <c>P_ActivateInStasisCeiling</c>, the <c>activeceilings</c> list) and
/// <c>EV_VerticalDoor</c> on a ceiling or a floor, on the synthetic specials
/// map (E1M2, as <see cref="FloorTests"/>: alcove 3, sector 4, floor 136,
/// ceiling 280; alcove 2's ceiling 272, alcove 4's 256, the corridor's 128).
/// </summary>
public class CeilingTests
{
    private const int FRACUNIT = 1 << 16;
    private const int A3 = 1 + 3;
    private const int Door = SyntheticIwad.DoorSector;

    private static int F(int units) => units * FRACUNIT;

    private static uint Deg(int degrees) => (uint)((ulong)degrees * 0x100000000UL / 360);

    private static World Specials()
    {
        var wad = new WadArchive(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) });
        var world = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium), Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(wad, "E1M2"));
        world.PlaceMobj(world.players[0].mo!, F(64), F(196)); // room R, out of the way
        return world;
    }

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

    private static ceiling_t CeilingOf(World world, int sector) => Assert.IsType<ceiling_t>(world.sectors[sector].specialdata);

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

    [Fact]
    public void TheOneWayCeilingsGoWhereVanillaSendsAndAreDone()
    {
        (ceiling_e Type, int To, int Tics)[] cases =
        {
            (ceiling_e.lowerToFloor, 136, 145),   // 144 steps, pastdest the tic after
            (ceiling_e.lowerAndCrush, 144, 137),
            (ceiling_e.raiseToHighest, 272, 1),   // the highest neighbouring ceiling (alcove 2) is below: at once
        };
        foreach (var c in cases)
        {
            World world = Specials();
            line_t line = Tagged(world, 7, A3);
            Assert.Equal(1, world.EV_DoCeiling(line, c.Type));
            ceiling_t ceiling = CeilingOf(world, A3);
            Assert.Equal((c.Type, 7, FRACUNIT), (ceiling.type, ceiling.tag, ceiling.speed));
            Assert.Equal(c.Type == ceiling_e.raiseToHighest ? 1 : -1, ceiling.direction);
            Assert.Equal(0, ceiling.crush);
            Assert.Contains(ceiling, world.activeceilings);
            Assert.Equal(0, world.EV_DoCeiling(line, c.Type)); // busy
            Assert.Equal(c.Tics, RunUntilIdle(world, A3));
            Assert.Equal(F(c.To), world.sectors[A3].ceilingheight);
            Assert.DoesNotContain(ceiling, world.activeceilings);
            Assert.Equal(think_t.REMOVED, ceiling.function);
        }

        // Raise to highest from below: alcove 4 (256) to alcove 3's 280.
        World w = Specials();
        w.EV_DoCeiling(Tagged(w, 7, 1 + 4), ceiling_e.raiseToHighest);
        Assert.Equal(25, RunUntilIdle(w, 1 + 4));
        Assert.Equal(F(280), w.sectors[1 + 4].ceilingheight);
    }

    [Fact]
    public void CrushersGoDownAndUpForEver()
    {
        (ceiling_e Type, int Speed)[] cases =
        {
            (ceiling_e.crushAndRaise, 1),
            (ceiling_e.fastCrushAndRaise, 2),
            (ceiling_e.silentCrushAndRaise, 1),
        };
        foreach (var c in cases)
        {
            World world = Specials();
            line_t line = Tagged(world, 7, A3);
            world.EV_DoCeiling(line, c.Type);
            ceiling_t ceiling = CeilingOf(world, A3);
            Assert.Equal((F(280), F(144), F(c.Speed), 1, -1), (ceiling.topheight, ceiling.bottomheight, ceiling.speed, ceiling.crush, ceiling.direction));
            var sounds = new System.Collections.Generic.List<sfxenum_t>();
            int min = int.MaxValue, max = int.MinValue;
            for (int i = 0; i < 600; i++)
            {
                world.G_Ticker(new ticcmd_t());
                sounds.AddRange(world.sounds.Select(s => s.sfx));
                min = System.Math.Min(min, world.sectors[A3].ceilingheight);
                max = System.Math.Max(max, world.sectors[A3].ceilingheight);
            }
            Assert.Equal((F(144), F(280)), (min, max));
            Assert.Same(ceiling, world.sectors[A3].specialdata);
            if (c.Type == ceiling_e.silentCrushAndRaise)
            {
                Assert.DoesNotContain(sfxenum_t.sfx_stnmov, sounds);
                Assert.Contains(sfxenum_t.sfx_pstop, sounds);
            }
            else
            {
                Assert.Contains(sfxenum_t.sfx_stnmov, sounds);
                Assert.DoesNotContain(sfxenum_t.sfx_pstop, sounds);
            }
        }
    }

    [Fact]
    public void ACrusherSlowsOnAThingExceptTheFastOne()
    {
        foreach ((ceiling_e type, int slow) in new[]
                 {
                     (ceiling_e.crushAndRaise, FRACUNIT / 8),
                     (ceiling_e.silentCrushAndRaise, FRACUNIT / 8),
                     (ceiling_e.fastCrushAndRaise, 2 * FRACUNIT),
                 })
        {
            World world = Specials();
            mobj_t mo = world.players[0].mo!;
            world.PlaceMobj(mo, F(3 * 32 + 16), F(64)); // in alcove 3: floor 136, the player's top at 192
            line_t line = Tagged(world, 7, A3);
            world.EV_DoCeiling(line, type);
            ceiling_t ceiling = CeilingOf(world, A3);
            int tics = 0;
            while (world.sectors[A3].ceilingheight >= F(192))
            {
                world.G_Ticker(new ticcmd_t());
                tics++;
                Assert.True(tics < 200, $"{type}: ceiling {world.sectors[A3].ceilingheight / (double)FRACUNIT} after {tics}, speed {ceiling.speed}, dir {ceiling.direction}");
            }
            // Crushing: the ceiling goes on (crush) through the player, now at the reduced speed.
            Assert.Equal(slow, ceiling.speed);
            if (type != ceiling_e.fastCrushAndRaise)
            {
                int before = world.sectors[A3].ceilingheight;
                Run(world, 1);
                Assert.Equal(before - slow, world.sectors[A3].ceilingheight);
            }
            if (type == ceiling_e.crushAndRaise)
            {
                // At the bottom: back to full speed and up.
                while (ceiling.direction == -1)
                    world.G_Ticker(new ticcmd_t());
                Assert.Equal(FRACUNIT, ceiling.speed);
            }
        }
    }

    [Fact]
    public void ALowerAndCrushCeilingDoesNotCrushAsVanilla()
    {
        // Vanilla's EV_DoCeiling sets crush only for the crush-and-raise types: lowerAndCrush (44, 72)
        // stops on the player's head (192), slowed to 1/8, and waits there.
        World world = Specials();
        world.PlaceMobj(world.players[0].mo!, F(3 * 32 + 16), F(64));
        world.EV_DoCeiling(Tagged(world, 7, A3), ceiling_e.lowerAndCrush);
        ceiling_t ceiling = CeilingOf(world, A3);
        Assert.Equal(0, ceiling.crush);
        Run(world, 200);
        Assert.Equal(F(192), world.sectors[A3].ceilingheight);
        Assert.Equal(FRACUNIT / 8, ceiling.speed);
        Assert.Same(ceiling, world.sectors[A3].specialdata);
    }

    [Fact]
    public void ACrusherStopsInStasisAndRestartsFromACrusherLine()
    {
        World world = Specials();
        line_t line = Tagged(world, 7, A3);
        world.EV_DoCeiling(line, ceiling_e.crushAndRaise);
        ceiling_t ceiling = CeilingOf(world, A3);
        Run(world, 10);
        Assert.Equal(1, world.EV_CeilingCrushStop(line));
        Assert.Equal((0, -1, think_t.NULL), (ceiling.direction, ceiling.olddirection, ceiling.function));
        Assert.Equal(0, world.EV_CeilingCrushStop(line)); // already stopped
        int height = world.sectors[A3].ceilingheight;
        Run(world, 20);
        Assert.Equal(height, world.sectors[A3].ceilingheight);
        // A non-crusher of the tag does not restart it.
        Assert.Equal(0, world.EV_DoCeiling(line, ceiling_e.lowerToFloor));
        Assert.Equal(0, ceiling.direction);
        // A crusher does (no new ceiling: the sector is busy).
        Assert.Equal(0, world.EV_DoCeiling(line, ceiling_e.fastCrushAndRaise));
        Assert.Equal((-1, think_t.T_MoveCeiling, ceiling_e.crushAndRaise), (ceiling.direction, ceiling.function, ceiling.type));
        Run(world, 1);
        Assert.Equal(height - FRACUNIT, world.sectors[A3].ceilingheight);
    }

    [Fact]
    public void TheCeilingListHoldsThirtyAndAnUnlistedCeilingStaysBusy()
    {
        World world = Specials();
        for (int i = 0; i < CeilingMove.MAXCEILINGS; i++)
            world.P_AddActiveCeiling(new ceiling_t { sector = world.sectors[0] });
        // The 31st is not listed (no error, as vanilla): it moves, but stays when done.
        line_t line = Tagged(world, 7, A3);
        Assert.Equal(1, world.EV_DoCeiling(line, ceiling_e.lowerToFloor));
        ceiling_t ceiling = CeilingOf(world, A3);
        Assert.DoesNotContain(ceiling, world.activeceilings);
        Run(world, 200);
        Assert.Equal(F(136), world.sectors[A3].ceilingheight);
        Assert.Same(ceiling, world.sectors[A3].specialdata);
        Assert.Equal(0, world.EV_CeilingCrushStop(line)); // cannot be stopped

        // A new level clears the list.
        world.G_DoLoadLevel(Level.Load(new WadArchive(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) }), "E1M2"));
        Assert.All(world.activeceilings, c => Assert.Null(c));
    }

    [Fact]
    public void ManualDoorUseOnACeilingOrAFloorWritesTheDoorsDirectionThere()
    {
        // A ceiling: the door's direction is its crush flag (Chocolate Doom writes it anyway).
        World world = Specials();
        world.EV_DoCeiling(Tagged(world, SyntheticIwad.DoorTag, Door), ceiling_e.raiseToHighest);
        ceiling_t ceiling = CeilingOf(world, Door);
        mobj_t mo = world.players[0].mo!;
        world.PlaceMobj(mo, F(100), F(196), Deg(0));
        world.P_UseLines(world.players[0]);
        Assert.Same(ceiling, world.sectors[Door].specialdata);
        Assert.Equal(-1, ceiling.crush);
        world.EV_VerticalDoor(world.lines[SyntheticIwad.DoorLineR], mo);
        Assert.Equal(1, ceiling.crush); // -1 read as "closing": back up

        // A floor: its flat (vanilla's flat -1); a lower-and-change floor then keeps its flat.
        world = Specials();
        world.sectors[Door].floorheight = F(16);
        world.sectors[Door].special = 7;
        world.EV_DoFloor(Tagged(world, SyntheticIwad.DoorTag, Door), floor_e.lowerAndChange);
        floormove_t floor = Assert.IsType<floormove_t>(world.sectors[Door].specialdata);
        Assert.Equal("FLOOR1", floor.texture); // room R's
        mo = world.players[0].mo!;
        world.PlaceMobj(mo, F(100), F(196), Deg(0));
        world.P_UseLines(world.players[0]);
        Assert.Same(floor, world.sectors[Door].specialdata);
        Assert.Equal(-1, floor.doordirection);
        Assert.Null(floor.texture);
        RunUntilIdle(world, Door);
        Assert.Equal(0, world.sectors[Door].floorheight);
        Assert.Equal("FLOOR2", world.sectors[Door].floorpic); // its own
        Assert.Equal(0, world.sectors[Door].special); // the new special still comes
    }
}
