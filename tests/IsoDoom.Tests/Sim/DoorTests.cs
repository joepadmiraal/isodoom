using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T5.3: p_doors.c (<c>EV_VerticalDoor</c>, <c>EV_DoDoor</c>,
/// <c>EV_DoLockedDoor</c>, <c>T_VerticalDoor</c>, the sector spawners) and
/// p_floor.c <c>T_MovePlane</c> on the synthetic specials map's door
/// (E1M2: room R, sector 25, x 0..128; the door, sector 26, x 128..160,
/// closed at 0 with tag 6; room S, sector 27, x 160..288; all y 152..240,
/// ceilings 128, so the door opens to 124; lines 76 and 77 DR manual doors
/// facing R and S; see <c>SyntheticIwad.BuildSpecialsMap</c>).
/// </summary>
public class DoorTests
{
    private const int FRACUNIT = 1 << 16;
    private const int Door = SyntheticIwad.DoorSector;
    private const int Top = 124 * FRACUNIT;

    private static int F(int units) => units * FRACUNIT;

    private static uint Deg(int degrees) => (uint)((ulong)degrees * 0x100000000UL / 360);

    private static Level SpecialsLevel() =>
        Level.Load(new WadArchive([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]), "E1M2");

    private static World Specials(Level? level = null)
    {
        var world = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium), Tweaks.Vanilla);
        world.G_DoLoadLevel(level ?? SpecialsLevel());
        return world;
    }

    private static mobj_t Player(World world) => world.players[0].mo!;

    private static sector_t DoorSec(World world) => world.sectors[Door];

    private static vldoor_t? DoorThinker(World world) => DoorSec(world).specialdata as vldoor_t;

    /// <summary>Puts the player in room R facing the door (east) and presses use once.</summary>
    private static void UseFromR(World world, int x = 100)
    {
        world.PlaceMobj(Player(world), F(x), F(196), Deg(0));
        world.P_UseLines(world.players[0]);
    }

    /// <summary>Runs <paramref name="tics"/> tics with no input.</summary>
    private static void Run(World world, int tics)
    {
        for (int i = 0; i < tics; i++)
            world.G_Ticker(new ticcmd_t());
    }

    private static sfxenum_t[] DoorSounds(World world) =>
        [.. world.StartedSounds().Where(s => s.sector == DoorSec(world)).Select(s => s.sfx)];

    [Fact]
    public void ARaiseDoorOpensWaitsAndClosesAsVanilla()
    {
        World world = Specials();
        Assert.Equal(1, world.lines[SyntheticIwad.DoorLineR].special);
        Assert.Equal(0, DoorSec(world).ceilingheight);
        UseFromR(world);
        vldoor_t door = Assert.IsType<vldoor_t>(DoorSec(world).specialdata);
        Assert.Equal(vldoor_e.vld_normal, door.type);
        Assert.Equal(Top, door.topheight);
        Assert.Equal([sfxenum_t.sfx_doropn], DoorSounds(world));
        Assert.Equal(1, world.lines[SyntheticIwad.DoorLineR].special); // a DR line stays

        // Up 2 units a tic: 124 at tic 62, at the top (pastdest) at 63.
        Run(world, 1);
        Assert.Equal(F(2), DoorSec(world).ceilingheight);
        Run(world, 61);
        Assert.Equal(Top, DoorSec(world).ceilingheight);
        Assert.Equal(1, door.direction);
        Run(world, 1);
        Assert.Equal(0, door.direction);
        Assert.Equal(VDoor.VDOORWAIT, door.topcountdown);

        // Waits 150 tics, then closes.
        Run(world, VDoor.VDOORWAIT - 1);
        Assert.Equal(0, door.direction);
        Run(world, 1);
        Assert.Equal(-1, door.direction);
        Assert.Equal([sfxenum_t.sfx_dorcls], DoorSounds(world));
        Assert.Equal(Top, DoorSec(world).ceilingheight);
        Run(world, 62);
        Assert.Equal(0, DoorSec(world).ceilingheight);
        Assert.Same(door, DoorSec(world).specialdata);
        Run(world, 1);
        Assert.Null(DoorSec(world).specialdata);
        Assert.Equal(think_t.REMOVED, door.function);
        Assert.Empty(world.StartedSounds()); // a raise door closes silently
        Run(world, 1);
        Assert.DoesNotContain(Thinkers(world), t => t is vldoor_t);
    }

    private static System.Collections.Generic.IEnumerable<thinker_t> Thinkers(World world)
    {
        for (thinker_t th = world.thinkercap.next; th != world.thinkercap; th = th.next)
            yield return th;
    }

    [Fact]
    public void AClosingDoorBlockedByAThingReopens()
    {
        World world = Specials();
        UseFromR(world);
        vldoor_t door = DoorThinker(world)!;
        Run(world, 63 + VDoor.VDOORWAIT);
        Assert.Equal(-1, door.direction);
        // The player steps into the doorway: the door comes down to its height (56) and goes back up.
        world.PlaceMobj(Player(world), F(144), F(196));
        int lowest = Top;
        int tics = 0;
        while (door.direction == -1)
        {
            Run(world, 1);
            lowest = System.Math.Min(lowest, DoorSec(world).ceilingheight);
            tics++;
        }
        Assert.Equal(F(56), lowest);
        Assert.Equal(35, tics); // 124 to 56 in 34 tics; the 35th step (54) does not fit
        Assert.Equal(1, door.direction);
        Assert.Equal([sfxenum_t.sfx_doropn], DoorSounds(world));
        Assert.Equal(F(56), DoorSec(world).ceilingheight);
        Assert.Equal(F(56), Player(world).ceilingz);
        // Up again, waits at the top again.
        Run(world, 35);
        Assert.Equal(0, door.direction);
        Assert.Equal(VDoor.VDOORWAIT, door.topcountdown);
    }

    [Fact]
    public void UsingAMovingRaiseDoorReversesIt()
    {
        World world = Specials();
        UseFromR(world);
        vldoor_t door = DoorThinker(world)!;
        Run(world, 10);
        // Opening: the player closes it at once.
        UseFromR(world);
        Assert.Equal(-1, door.direction);
        Assert.Same(door, DoorThinker(world));
        Run(world, 3);
        Assert.Equal(F(14), DoorSec(world).ceilingheight);
        // Closing: back up.
        UseFromR(world);
        Assert.Equal(1, door.direction);
        Run(world, 2);
        Assert.Equal(F(18), DoorSec(world).ceilingheight);
        // Waiting open: closes at once, from the other side too (line 77, facing S).
        Run(world, 60);
        Assert.Equal(0, door.direction);
        world.PlaceMobj(Player(world), F(190), F(196), Deg(180));
        world.P_UseLines(world.players[0]);
        Assert.Equal(-1, door.direction);
    }

    [Fact]
    public void MonstersOpenManualDoorsButNeverCloseThem()
    {
        World world = Specials();
        mobj_t trooper = world.P_SpawnMobj(F(100), F(196), World.ONFLOORZ, mobjtype_t.MT_POSSESSED);
        line_t line = world.lines[SyntheticIwad.DoorLineR];
        Assert.True(world.P_UseSpecialLine(trooper, line, 0));
        vldoor_t door = DoorThinker(world)!;
        Assert.Equal(vldoor_e.vld_normal, door.type);
        Run(world, 70);
        Assert.Equal(0, door.direction);
        world.P_UseSpecialLine(trooper, line, 0);
        Assert.Equal(0, door.direction); // bad guys never close doors
        // ... but send a closing one back up.
        Run(world, VDoor.VDOORWAIT + 5);
        Assert.Equal(-1, door.direction);
        world.P_UseSpecialLine(trooper, line, 0);
        Assert.Equal(1, door.direction);

        // A secret door they leave alone, and the locked and open-stay kinds they cannot use.
        world = Specials();
        trooper = world.P_SpawnMobj(F(100), F(196), World.ONFLOORZ, mobjtype_t.MT_POSSESSED);
        line = world.lines[SyntheticIwad.DoorLineR];
        foreach (short special in new short[] { 26, 31, 32 })
        {
            line.special = special;
            world.P_UseSpecialLine(trooper, line, 0);
        }
        line.special = 1;
        line.flags |= Line.ML_SECRET;
        Assert.False(world.P_UseSpecialLine(trooper, line, 0));
        Assert.Null(DoorSec(world).specialdata);
    }

    [Fact]
    public void AnOpenDoorStaysOpenAndItsLineIsSpent()
    {
        World world = Specials();
        world.lines[SyntheticIwad.DoorLineR].special = 31;
        UseFromR(world);
        Assert.Equal(0, world.lines[SyntheticIwad.DoorLineR].special);
        vldoor_t door = DoorThinker(world)!;
        Assert.Equal(vldoor_e.vld_open, door.type);
        Run(world, 63);
        Assert.Null(DoorSec(world).specialdata);
        Assert.Equal(think_t.REMOVED, door.function);
        Run(world, 500);
        Assert.Equal(Top, DoorSec(world).ceilingheight);
    }

    [Theory]
    [InlineData(26, card_t.it_bluecard, card_t.it_blueskull, World.PD_BLUEK)]
    [InlineData(32, card_t.it_bluecard, card_t.it_blueskull, World.PD_BLUEK)]
    [InlineData(27, card_t.it_yellowcard, card_t.it_yellowskull, World.PD_YELLOWK)]
    [InlineData(34, card_t.it_yellowcard, card_t.it_yellowskull, World.PD_YELLOWK)]
    [InlineData(28, card_t.it_redcard, card_t.it_redskull, World.PD_REDK)]
    [InlineData(33, card_t.it_redcard, card_t.it_redskull, World.PD_REDK)]
    public void ALockedDoorNeedsItsCardOrSkull(short special, card_t card, card_t skull, string message)
    {
        foreach (card_t? key in new card_t?[] { null, card, skull })
        {
            World world = Specials();
            player_t player = world.players[0];
            world.lines[SyntheticIwad.DoorLineR].special = special;
            // Every other key does not help.
            for (int k = 0; k < (int)card_t.NUMCARDS; k++)
                player.cards[k] = k != (int)card && k != (int)skull;
            if (key is card_t has)
                player.cards[(int)has] = true;
            UseFromR(world);
            if (key is null)
            {
                Assert.Null(DoorSec(world).specialdata);
                Assert.Equal(message, player.message);
                Assert.Equal(new[] { new sound_event_t(sfxenum_t.sfx_oof, null, null) }, world.StartedSounds());
                Assert.Equal(special, world.lines[SyntheticIwad.DoorLineR].special);
            }
            else
            {
                vldoor_t door = DoorThinker(world)!;
                Assert.Equal(special < 29 ? vldoor_e.vld_normal : vldoor_e.vld_open, door.type);
                Assert.Null(player.message);
                Assert.Equal([sfxenum_t.sfx_doropn], DoorSounds(world));
                Assert.Equal(special < 29 ? special : 0, world.lines[SyntheticIwad.DoorLineR].special);
            }
        }
    }

    [Fact]
    public void BlazingDoorsMoveFourTimesAsFast()
    {
        World world = Specials();
        world.lines[SyntheticIwad.DoorLineR].special = 117;
        UseFromR(world);
        vldoor_t door = DoorThinker(world)!;
        Assert.Equal(vldoor_e.vld_blazeRaise, door.type);
        Assert.Equal(4 * VDoor.VDOORSPEED, door.speed);
        Assert.Equal([sfxenum_t.sfx_bdopn], DoorSounds(world));
        Run(world, 15); // 8 a tic: 120 at 15, at the top (124, pastdest) at 16
        Assert.Equal(F(120), DoorSec(world).ceilingheight);
        Run(world, 1);
        Assert.Equal(Top, DoorSec(world).ceilingheight);
        Assert.Equal(0, door.direction);
        Run(world, VDoor.VDOORWAIT - 1);
        Assert.Equal(0, door.direction);
        Run(world, 1);
        Assert.Equal(-1, door.direction);
        Assert.Equal([sfxenum_t.sfx_bdcls], DoorSounds(world));
        Run(world, 15);
        Assert.Equal(F(4), DoorSec(world).ceilingheight);
        Run(world, 1);
        Assert.Equal(0, DoorSec(world).ceilingheight);
        Assert.Null(DoorSec(world).specialdata);
        Assert.Equal([sfxenum_t.sfx_bdcls], DoorSounds(world)); // blazing doors clunk shut

        world = Specials();
        world.lines[SyntheticIwad.DoorLineR].special = 118;
        UseFromR(world);
        Assert.Equal(vldoor_e.vld_blazeOpen, DoorThinker(world)!.type);
        Assert.Equal(0, world.lines[SyntheticIwad.DoorLineR].special);
        Run(world, 16);
        Assert.Null(DoorSec(world).specialdata);
        Assert.Equal(Top, DoorSec(world).ceilingheight);
    }

    /// <summary>A line (the corridor's west wall) tagged as the door, with the special.</summary>
    private static line_t Tagged(World world, short special)
    {
        line_t line = world.lines[1];
        line.special = special;
        line.tag = SyntheticIwad.DoorTag;
        return line;
    }

    [Fact]
    public void TaggedDoorsOpenCloseAndSkipBusySectors()
    {
        World world = Specials();
        line_t line = Tagged(world, 0);
        Assert.Equal(1, world.EV_DoDoor(line, vldoor_e.vld_open));
        vldoor_t door = DoorThinker(world)!;
        Assert.Equal(0, world.EV_DoDoor(line, vldoor_e.vld_close)); // busy
        Assert.Same(door, DoorThinker(world));
        Run(world, 63);
        Assert.Null(DoorThinker(world));
        Assert.Equal(Top, DoorSec(world).ceilingheight);

        // Close: down, no sound at the bottom, gone.
        Assert.Equal(1, world.EV_DoDoor(line, vldoor_e.vld_close));
        Assert.Equal([sfxenum_t.sfx_dorcls], DoorSounds(world));
        Run(world, 62);
        Assert.Equal(0, DoorSec(world).ceilingheight);
        Run(world, 1);
        Assert.Null(DoorThinker(world));

        // An open door's sound only when it moves; a close door never goes back up for a thing in the way.
        world.events.Clear();
        DoorSec(world).ceilingheight = Top;
        Assert.Equal(1, world.EV_DoDoor(line, vldoor_e.vld_open));
        Assert.Empty(world.StartedSounds());
        Run(world, 1);
        Assert.Null(DoorThinker(world));
        world.PlaceMobj(Player(world), F(144), F(196));
        Assert.Equal(1, world.EV_DoDoor(line, vldoor_e.vld_close));
        Run(world, 40);
        Assert.Equal(F(56), DoorSec(world).ceilingheight);
        Assert.Equal(-1, DoorThinker(world)!.direction);
    }

    [Fact]
    public void ACloseThirtyDoorReopensAfterThirtySeconds()
    {
        World world = Specials();
        DoorSec(world).ceilingheight = F(100); // open, below the usual 124
        line_t line = Tagged(world, 0);
        Assert.Equal(1, world.EV_DoDoor(line, vldoor_e.vld_close30ThenOpen));
        vldoor_t door = DoorThinker(world)!;
        Assert.Equal(F(100), door.topheight);
        Run(world, 51); // 100 to 0 in 50 tics, pastdest at 51
        Assert.Equal(0, DoorSec(world).ceilingheight);
        Assert.Equal(0, door.direction);
        Assert.Equal(35 * 30, door.topcountdown);
        Run(world, 35 * 30);
        Assert.Equal(1, door.direction);
        Assert.Equal([sfxenum_t.sfx_doropn], DoorSounds(world));
        Run(world, 51);
        Assert.Equal(F(100), DoorSec(world).ceilingheight);
        Assert.Null(DoorThinker(world));
    }

    [Fact]
    public void BlazingTaggedDoorsAndTheirLockedSwitches()
    {
        World world = Specials();
        line_t line = Tagged(world, 0);
        Assert.Equal(1, world.EV_DoDoor(line, vldoor_e.vld_blazeOpen));
        Assert.Equal([sfxenum_t.sfx_bdopn], DoorSounds(world));
        Run(world, 16);
        Assert.Null(DoorThinker(world));
        Assert.Equal(1, world.EV_DoDoor(line, vldoor_e.vld_blazeClose));
        Assert.Equal([sfxenum_t.sfx_bdcls], DoorSounds(world));
        Run(world, 15);
        Assert.Equal(F(4), DoorSec(world).ceilingheight);
        Run(world, 1);
        Assert.Equal(0, DoorSec(world).ceilingheight);
        Assert.Null(DoorThinker(world));
        Assert.Equal([sfxenum_t.sfx_bdcls], DoorSounds(world));

        // A locked blazing switch (S1 133, blue): the message without the key, the door with it.
        world = Specials();
        player_t player = world.players[0];
        line = Tagged(world, 133);
        world.PlaceMobj(Player(world), F(40), F(-64), Deg(180));
        world.P_UseLines(player);
        Assert.Equal(World.PD_BLUEO, player.message);
        Assert.Null(DoorThinker(world));
        Assert.Equal(133, line.special);
        Assert.Equal(0, world.EV_DoLockedDoor(line, vldoor_e.vld_blazeOpen, world.P_SpawnMobj(F(40), F(-64), World.ONFLOORZ, mobjtype_t.MT_POSSESSED)));
        player.cards[(int)card_t.it_blueskull] = true;
        player.message = null;
        world.P_UseLines(player);
        Assert.Null(player.message);
        Assert.Equal(vldoor_e.vld_blazeOpen, DoorThinker(world)!.type);
        Assert.Equal(0, line.special);

        foreach ((short special, card_t key, string message) in new[]
        {
            ((short)99, card_t.it_bluecard, World.PD_BLUEO), ((short)134, card_t.it_redcard, World.PD_REDO),
            ((short)135, card_t.it_redskull, World.PD_REDO), ((short)136, card_t.it_yellowcard, World.PD_YELLOWO),
            ((short)137, card_t.it_yellowskull, World.PD_YELLOWO),
        })
        {
            world = Specials();
            player = world.players[0];
            line = Tagged(world, special);
            Assert.Equal(0, world.EV_DoLockedDoor(line, vldoor_e.vld_blazeOpen, Player(world)));
            Assert.Equal(message, player.message);
            player.cards[(int)key] = true;
            Assert.Equal(1, world.EV_DoLockedDoor(line, vldoor_e.vld_blazeOpen, Player(world)));
        }
    }

    [Fact]
    public void WalkOverDoorsTriggerFromTheirLines()
    {
        // A W1 raise door (4) on alcove 0's opening, tagged to the door: monsters may trigger it too.
        World world = Specials();
        line_t line = world.lines[3];
        line.special = 4;
        line.tag = SyntheticIwad.DoorTag;
        mobj_t skull = world.P_SpawnMobj(F(16), F(-40), World.ONFLOORZ, mobjtype_t.MT_SKULL);
        Assert.True(world.P_TryMove(skull, F(16), F(10)));
        Assert.Equal(vldoor_e.vld_normal, DoorThinker(world)!.type);
        Assert.Equal(0, line.special);

        // A WR close-30 (76): the player closes the open door; again while it is busy does nothing.
        world = Specials();
        DoorSec(world).ceilingheight = Top;
        line = world.lines[3];
        line.special = 76;
        line.tag = SyntheticIwad.DoorTag;
        mobj_t mo = Player(world);
        world.PlaceMobj(mo, F(16), F(-40));
        Assert.True(world.P_TryMove(mo, F(16), F(10)));
        vldoor_t door = DoorThinker(world)!;
        Assert.Equal(vldoor_e.vld_close30ThenOpen, door.type);
        Assert.True(world.P_TryMove(mo, F(16), F(-10)));
        Assert.Same(door, DoorThinker(world));
        Assert.Equal(76, line.special);
    }

    [Fact]
    public void SectorSpecialsSpawnTimedDoors()
    {
        // 10: an open door that closes after 30 seconds.
        Level level = SpecialsLevel();
        level.Sectors[Door].Special = 10;
        level.Sectors[Door].CeilingHeight = Top;
        World world = Specials(level);
        Assert.Equal(0, DoorSec(world).special);
        vldoor_t door = DoorThinker(world)!;
        Assert.Equal(vldoor_e.vld_normal, door.type);
        Run(world, 30 * 35 - 1);
        Assert.Equal(0, door.direction);
        Run(world, 1);
        Assert.Equal(-1, door.direction);
        Assert.Equal([sfxenum_t.sfx_dorcls], DoorSounds(world));
        Run(world, 63);
        Assert.Equal(0, DoorSec(world).ceilingheight);
        Assert.Null(DoorThinker(world));

        // 14: a closed door that opens after 5 minutes, then is a raise door.
        level = SpecialsLevel();
        level.Sectors[Door].Special = 14;
        world = Specials(level);
        Assert.Equal(0, DoorSec(world).special);
        door = DoorThinker(world)!;
        Assert.Equal(vldoor_e.vld_raiseIn5Mins, door.type);
        Assert.Equal(Top, door.topheight);
        Run(world, 5 * 60 * 35 - 1);
        Assert.Equal(2, door.direction);
        Assert.Equal(0, DoorSec(world).ceilingheight);
        Run(world, 1);
        Assert.Equal(1, door.direction);
        Assert.Equal(vldoor_e.vld_normal, door.type);
        Assert.Equal([sfxenum_t.sfx_doropn], DoorSounds(world));
        Run(world, 63 + VDoor.VDOORWAIT);
        Assert.Equal(-1, door.direction);
    }

    [Fact]
    public void MovePlaneStopsAtThingsUnlessCrushing()
    {
        World world = Specials();
        sector_t room = world.sectors[Door - 1];
        mobj_t mo = Player(world);
        world.PlaceMobj(mo, F(64), F(196));

        // Floor up: the player rides it until the 128 ceiling; a step that does not fit is undone, or kept when crushing.
        Assert.Equal(result_e.ok, world.T_MovePlane(room, F(8), F(100), false, 0, 1));
        Assert.Equal(F(8), mo.z);
        room.floorheight = F(70);
        world.P_ChangeSector(room, false);
        Assert.Equal(result_e.crushed, world.T_MovePlane(room, F(8), F(100), false, 0, 1));
        Assert.Equal(F(70), room.floorheight);
        Assert.Equal(result_e.crushed, world.T_MovePlane(room, F(8), F(100), true, 0, 1));
        Assert.Equal(F(78), room.floorheight);
        // Past the destination: there (pastdest), or back where it was when that does not fit.
        room.floorheight = F(70);
        world.P_ChangeSector(room, false);
        Assert.Equal(result_e.pastdest, world.T_MovePlane(room, F(8), F(72), false, 0, 1));
        Assert.Equal(F(72), room.floorheight);
        Assert.Equal(result_e.pastdest, world.T_MovePlane(room, F(80), F(100), false, 0, 1));
        Assert.Equal(F(72), room.floorheight);
        // Floor down.
        Assert.Equal(result_e.ok, world.T_MovePlane(room, F(8), F(0), false, 0, -1));
        Assert.Equal(F(64), mo.z);
        Assert.Equal(result_e.pastdest, world.T_MovePlane(room, F(100), F(0), false, 0, -1));
        Assert.Equal(0, room.floorheight);

        // Ceiling down onto the player: undone, or kept when crushing; up never stops.
        room.ceilingheight = F(60);
        world.P_ChangeSector(room, false);
        Assert.Equal(result_e.crushed, world.T_MovePlane(room, F(8), F(0), false, 1, -1));
        Assert.Equal(F(60), room.ceilingheight);
        Assert.Equal(result_e.crushed, world.T_MovePlane(room, F(8), F(0), true, 1, -1));
        Assert.Equal(F(52), room.ceilingheight);
        Assert.Equal(result_e.ok, world.T_MovePlane(room, F(2), F(128), false, 1, 1));
        Assert.Equal(F(54), room.ceilingheight);
        Assert.Equal(result_e.pastdest, world.T_MovePlane(room, F(100), F(128), false, 1, 1));
        Assert.Equal(F(128), room.ceilingheight);
    }
}
