using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T5.5: p_plats.c (<c>EV_DoPlat</c> with every lift type,
/// <c>T_PlatRaise</c>, <c>P_ActivateInStasis</c>, <c>EV_StopPlat</c>, the
/// <c>activeplats</c> list) and Chocolate Doom's <c>EV_VerticalDoor</c> on a
/// lift, on the synthetic specials map (E1M2, as <see cref="FloorTests"/>:
/// alcove 3, sector 4, floor 136 between the corridor's 0, alcove 2's 96 and
/// alcove 4's 176; the door sector 26 between rooms R and S).
/// </summary>
public class PlatTests
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

    private static plat_t PlatOf(World world, int sector) => Assert.IsType<plat_t>(world.sectors[sector].specialdata);

    /// <summary>The floor heights (units) of <paramref name="sector"/> over <paramref name="tics"/> tics.</summary>
    private static int[] Heights(World world, int sector, int tics)
    {
        var heights = new int[tics];
        for (int i = 0; i < tics; i++)
        {
            world.G_Ticker(new ticcmd_t());
            heights[i] = world.sectors[sector].floorheight;
        }
        return heights;
    }

    [Fact]
    public void ADownWaitUpStayLiftLowersWaitsThreeSecondsAndComesBack()
    {
        foreach ((plattype_e type, int speed) in new[] { (plattype_e.downWaitUpStay, 4), (plattype_e.blazeDWUS, 8) })
        {
            World world = Specials();
            line_t line = Tagged(world, 7, A3);
            Assert.Equal(1, world.EV_DoPlat(line, type, 0));
            plat_t plat = PlatOf(world, A3);
            Assert.Equal((plat_e.down, F(0), F(136), 105, F(speed)), (plat.status, plat.low, plat.high, plat.wait, plat.speed));
            Assert.Contains(plat, world.activeplats);
            Assert.Equal(new[] { sfxenum_t.sfx_pstart }, world.sounds.Select(s => s.sfx));
            Assert.Equal(0, world.EV_DoPlat(line, type, 0)); // busy

            // Down: 136 / speed steps, at the bottom (pastdest) the tic after.
            int down = 136 / speed;
            Run(world, down);
            Assert.Equal(0, world.sectors[A3].floorheight);
            Assert.Equal(plat_e.down, plat.status);
            Run(world, 1);
            Assert.Equal(plat_e.waiting, plat.status);
            Assert.Equal(new[] { sfxenum_t.sfx_pstop }, world.sounds.Select(s => s.sfx));
            // 105 tics, then up.
            Run(world, 104);
            Assert.Equal(plat_e.waiting, plat.status);
            Run(world, 1);
            Assert.Equal(plat_e.up, plat.status);
            Assert.Equal(new[] { sfxenum_t.sfx_pstart }, world.sounds.Select(s => s.sfx));
            Run(world, down);
            Assert.Equal(F(136), world.sectors[A3].floorheight);
            Assert.NotNull(world.sectors[A3].specialdata);
            Run(world, 1);
            Assert.Null(world.sectors[A3].specialdata);
            Assert.Equal(think_t.REMOVED, plat.function);
            Assert.DoesNotContain(plat, world.activeplats);
        }
    }

    [Fact]
    public void ALiftWhoseLowestNeighbourIsHigherStaysPut()
    {
        // Alcove 23 (-32) is lower than all its neighbours: the lift goes nowhere, waits and is done.
        World world = Specials();
        line_t line = Tagged(world, 7, 1 + 23);
        world.EV_DoPlat(line, plattype_e.downWaitUpStay, 0);
        Assert.Equal(F(-32), PlatOf(world, 1 + 23).low);
        int[] heights = Heights(world, 1 + 23, 1 + 105 + 1);
        Assert.All(heights, h => Assert.Equal(F(-32), h));
        Assert.Null(world.sectors[1 + 23].specialdata);
    }

    [Fact]
    public void ALiftComingUpUnderAThingGoesBackDown()
    {
        // The corridor's lift goes down to alcove 23's -32; with a ceiling of 40 the player (56
        // high) blocks it on the way up at -16, and it goes down again, waits and retries.
        World world = Specials();
        world.sectors[0].ceilingheight = F(40);
        line_t line = Tagged(world, 7, 0);
        world.EV_DoPlat(line, plattype_e.downWaitUpStay, 0);
        Run(world, 9 + 105); // down 32 at 4 a tic (8 steps, pastdest the 9th), wait
        Assert.Equal(plat_e.up, PlatOf(world, 0).status);
        world.PlaceMobj(world.players[0].mo!, F(300), F(-64));
        int[] heights = Heights(world, 0, 6);
        Assert.Equal(new[] { -28, -24, -20, -16, -16, -20 }.Select(F), heights);
        Assert.Equal(plat_e.down, PlatOf(world, 0).status);
        Assert.Equal(105, PlatOf(world, 0).count);
    }

    [Fact]
    public void RaiseAndChangeLiftsTakeTheLinesFrontFlat()
    {
        foreach ((plattype_e type, int amount, int high) in new[]
                 {
                     (plattype_e.raiseAndChange, 24, 160),
                     (plattype_e.raiseAndChange, 32, 168),
                     (plattype_e.raiseToNearestAndChange, 0, 176),
                 })
        {
            World world = Specials();
            world.sectors[A3].special = 7;
            line_t line = Tagged(world, 7, A3);
            world.EV_DoPlat(line, type, amount);
            plat_t plat = PlatOf(world, A3);
            Assert.Equal((plat_e.up, F(high), FRACUNIT / 2, 0), (plat.status, plat.high, plat.speed, plat.wait));
            Assert.Equal("FLOOR1", world.sectors[A3].floorpic); // line 0's front sector's (the corridor), at once
            Assert.Equal(type == plattype_e.raiseToNearestAndChange ? 0 : 7, world.sectors[A3].special);
            Assert.Equal(new[] { sfxenum_t.sfx_stnmov }, world.sounds.Select(s => s.sfx));
            int tics = 0;
            while (world.sectors[A3].specialdata != null)
            {
                world.G_Ticker(new ticcmd_t());
                tics++;
            }
            Assert.Equal(2 * (high - 136) + 1, tics);
            Assert.Equal(F(high), world.sectors[A3].floorheight);
            Assert.DoesNotContain(plat, world.activeplats);
        }
    }

    [Fact]
    public void APerpetualLiftRunsForEverUntilStoppedAndRestartsWhereItWas()
    {
        World world = Specials();
        line_t line = Tagged(world, 7, A3);
        int prnd = world.random.prndindex;
        world.EV_DoPlat(line, plattype_e.perpetualRaise, 0);
        Assert.Equal(prnd + 1, world.random.prndindex); // P_Random() & 1 picks up or down
        plat_t plat = PlatOf(world, A3);
        Assert.Equal((F(0), F(176), FRACUNIT, 105), (plat.low, plat.high, plat.speed, plat.wait));
        var seen = new System.Collections.Generic.HashSet<int>();
        for (int i = 0; i < 1000; i++)
        {
            world.G_Ticker(new ticcmd_t());
            seen.Add(world.sectors[A3].floorheight);
        }
        Assert.Contains(F(0), seen);
        Assert.Contains(F(176), seen);
        Assert.Same(plat, world.sectors[A3].specialdata);

        // Stop: in stasis where it is (the thinker does nothing).
        plat_e status = plat.status;
        int height = world.sectors[A3].floorheight;
        world.EV_StopPlat(line);
        Assert.Equal((plat_e.in_stasis, status, think_t.NULL), (plat.status, plat.oldstatus, plat.function));
        Run(world, 50);
        Assert.Equal(height, world.sectors[A3].floorheight);
        world.EV_StopPlat(line); // already stopped: oldstatus stays
        Assert.Equal(status, plat.oldstatus);

        // A perpetual raise of the tag restarts it (its sector is busy: no new lift, returns 0).
        Assert.Equal(0, world.EV_DoPlat(line, plattype_e.perpetualRaise, 0));
        Assert.Equal((status, think_t.T_PlatRaise), (plat.status, plat.function));
        Assert.Single(world.activeplats, p => p != null);

        // Another tag neither stops nor restarts it.
        line.tag = 8;
        world.EV_StopPlat(line);
        Assert.Equal(status, plat.status);
    }

    [Fact]
    public void TheLiftListHoldsThirtyAndANewLevelClearsIt()
    {
        World world = Specials();
        for (int i = 0; i < Plat.MAXPLATS; i++)
            world.P_AddActivePlat(new plat_t { sector = world.sectors[0] });
        var e = Assert.Throws<System.InvalidOperationException>(() => world.P_AddActivePlat(new plat_t()));
        Assert.Contains("no more plats", e.Message);
        Assert.Throws<System.InvalidOperationException>(() => world.P_RemoveActivePlat(new plat_t()));

        world = Specials();
        world.EV_DoPlat(Tagged(world, 7, A3), plattype_e.perpetualRaise, 0);
        Assert.NotNull(world.activeplats[0]);
        world.G_DoLoadLevel(Level.Load(new WadArchive(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) }), "E1M2"));
        Assert.All(world.activeplats, p => Assert.Null(p));
    }

    [Fact]
    public void ManualDoorUseOnALiftSetsItsWaitAsChocolateDoom()
    {
        // The door sector (26, tag 6) as a lift: its lowest neighbour is its own floor, so it waits.
        World world = Specials();
        world.EV_DoPlat(Tagged(world, SyntheticIwad.DoorTag, Door), plattype_e.downWaitUpStay, 0);
        plat_t plat = PlatOf(world, Door);
        Run(world, 1);
        Assert.Equal(plat_e.waiting, plat.status);
        mobj_t mo = world.players[0].mo!;
        world.PlaceMobj(mo, F(100), F(196), Deg(0));
        // A monster's use (a raise door: "bad guys never close doors") changes nothing.
        mobj_t imp = world.P_SpawnMobj(F(100), F(160), World.ONFLOORZ, mobjtype_t.MT_TROOP);
        world.EV_VerticalDoor(world.lines[SyntheticIwad.DoorLineR], imp);
        Assert.Equal(105, plat.wait);
        // The player's: the door's direction is the lift's wait (-1, Chocolate Doom); no door starts.
        world.P_UseLines(world.players[0]);
        Assert.Same(plat, world.sectors[Door].specialdata);
        Assert.Equal(-1, plat.wait);
        // Again: "direction" -1 goes back up (1).
        world.EV_VerticalDoor(world.lines[SyntheticIwad.DoorLineR], mo);
        Assert.Equal(1, plat.wait);
    }
}
