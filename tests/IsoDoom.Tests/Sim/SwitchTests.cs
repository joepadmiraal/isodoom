using System;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T5.4: p_switch.c (<c>P_InitSwitchList</c>, <c>P_ChangeSwitchTexture</c>,
/// <c>P_StartButton</c>) and the button countdown of <c>P_UpdateSpecials</c>,
/// on the synthetic specials map (E1M2; see <c>SyntheticIwad.BuildSpecialsMap</c>:
/// line 0 the corridor's south wall, an SR raise-door button, and line 1 its
/// west wall, an S1 open-door switch, both of tag 5 and drawing
/// <c>SW1BRCOM</c>; line 2 its east wall, an S1 switch of no sector, drawing
/// <c>BRICK1</c>; line 27 alcove 0's north wall, an SR raise-door button of
/// the door sector, drawing <c>SW1BRCOM</c>) and DOOM1 E1M1's exit switch.
/// </summary>
public class SwitchTests
{
    private const int FRACUNIT = 1 << 16;

    private static int F(int units) => units * FRACUNIT;

    private static uint Deg(int degrees) => (uint)((ulong)degrees * 0x100000000UL / 360);

    private static Level SpecialsLevel() =>
        Level.Load(new WadArchive(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) }), "E1M2");

    private static World Specials(Level? level = null, GameMode mode = GameMode.shareware)
    {
        var world = new World(new SpawnSettings(mode, skill_t.sk_medium), Tweaks.Vanilla);
        world.G_DoLoadLevel(level ?? SpecialsLevel());
        return world;
    }

    private static Side FrontOf(Level level, int line) => level.Sides[level.Lines[line].SideNum[0]];

    private static side_t Front(World world, int line) => world.sides[world.lines[line].sidenum[0]];

    /// <summary>Puts the player at (x, y) facing <paramref name="degrees"/> and presses use once.</summary>
    private static void Use(World world, int x, int y, int degrees)
    {
        world.PlaceMobj(world.players[0].mo!, F(x), F(y), Deg(degrees));
        world.P_UseLines(world.players[0]);
    }

    private static void UseLine0(World world) => Use(world, 64, -100, 270);

    private static void UseLine1(World world) => Use(world, 30, -64, 180);

    private static void UseLine27(World world) => Use(world, 16, 100, 90);

    /// <summary>Runs <paramref name="tics"/> tics with no input.</summary>
    private static void Run(World world, int tics)
    {
        for (int i = 0; i < tics; i++)
            world.G_Ticker(new ticcmd_t());
    }

    private static sound_event_t[] SwitchSounds(World world) =>
        world.sounds.Where(s => s.sfx is sfxenum_t.sfx_swtchn or sfxenum_t.sfx_swtchx).ToArray();

    private static int ActiveButtons(World world) => world.buttonlist.Count(b => b.btimer != 0);

    [Theory]
    [InlineData(GameMode.shareware, 19)]
    [InlineData(GameMode.registered, 29)]
    [InlineData(GameMode.retail, 29)]
    [InlineData(GameMode.commercial, 40)]
    public void TheSwitchListHasTheGameModesPairs(GameMode mode, int pairs)
    {
        var world = new World(new SpawnSettings(mode, skill_t.sk_medium), Tweaks.Vanilla);
        Assert.Equal(pairs, world.numswitches);
        Assert.Equal("SW1BRCOM", world.switchlist[0]);
        Assert.Equal("SW2BRCOM", world.switchlist[1]);
        Assert.Equal("SW1STRTN", world.switchlist[2 * 18]);
        Assert.Equal("SW2STRTN", world.switchlist[2 * 18 + 1]);
        for (int i = 0; i < pairs; i++)
        {
            Assert.StartsWith("SW1", world.switchlist[2 * i]);
            Assert.Equal("SW2" + world.switchlist[2 * i][3..], world.switchlist[2 * i + 1]);
        }
        Assert.All(world.switchlist.Skip(2 * pairs), Assert.Null);
    }

    [Fact]
    public void TheSpecialsMapHasItsSwitches()
    {
        World world = Specials();
        Assert.Equal("SW1BRCOM", Front(world, 0).midtexture);
        Assert.Equal("SW1BRCOM", Front(world, 1).midtexture);
        Assert.Equal("BRICK1", Front(world, 2).midtexture);
        Assert.Equal("SW1BRCOM", Front(world, 27).midtexture);
        Assert.Equal(63, world.lines[27].special);
        Assert.Equal(SyntheticIwad.DoorTag, world.lines[27].tag);
        Assert.Equal(0, ActiveButtons(world));
    }

    [Fact]
    public void ASwitchTurnsOnceAndStays()
    {
        World world = Specials();
        UseLine1(world);
        Assert.Equal("SW2BRCOM", Front(world, 1).midtexture);
        Assert.Equal(0, world.lines[1].special);
        Assert.Equal(0, ActiveButtons(world));
        // The sound comes from buttonlist[0]'s origin: none while that slot is free.
        Assert.Equal(new[] { new sound_event_t(sfxenum_t.sfx_swtchn, null, null) }, SwitchSounds(world));

        Run(world, 100);
        Assert.Equal("SW2BRCOM", Front(world, 1).midtexture);
        UseLine1(world); // spent: no special
        Assert.Equal("SW2BRCOM", Front(world, 1).midtexture);
        Assert.Empty(SwitchSounds(world));
    }

    [Fact]
    public void AButtonTurnsBackAfterOneSecond()
    {
        World world = Specials();
        world.G_Ticker(new ticcmd_t()); // releases use (held from the spawn)
        world.PlaceMobj(world.players[0].mo!, F(64), F(-100), Deg(270));
        world.G_Ticker(new ticcmd_t { buttons = buttoncode_t.BT_USE });
        Assert.Equal("SW2BRCOM", Front(world, 0).midtexture);
        Assert.Equal(63, world.lines[0].special);
        Assert.Equal(new[] { new sound_event_t(sfxenum_t.sfx_swtchn, null, null) }, SwitchSounds(world));
        button_t b = world.buttonlist[0];
        Assert.Same(world.lines[0], b.line);
        Assert.Equal(bwhere_e.middle, b.where);
        Assert.Equal("SW1BRCOM", b.btexture);
        Assert.Same(world.sectors[0], b.soundorg);
        // The press's tic counted it down once already (P_UpdateSpecials runs after the players).
        Assert.Equal(World.BUTTONTIME - 1, b.btimer);

        Run(world, World.BUTTONTIME - 2);
        Assert.Equal("SW2BRCOM", Front(world, 0).midtexture);
        Assert.Empty(SwitchSounds(world));
        Run(world, 1);
        Assert.Equal("SW1BRCOM", Front(world, 0).midtexture);
        Assert.Equal(new[] { new sound_event_t(sfxenum_t.sfx_swtchn, null, world.sectors[0]) }, SwitchSounds(world));
        Assert.Equal(0, ActiveButtons(world));
        Assert.Null(b.line);
        Assert.Equal(63, world.lines[0].special);
    }

    [Fact]
    public void AFailedSpecialLeavesTheTexture()
    {
        Level level = SpecialsLevel();
        FrontOf(level, 2).MidTexture = "SW1BRCOM";
        World world = Specials(level);
        Use(world, 740, -64, 0); // line 2: tag 9, no sector
        Assert.Equal("SW1BRCOM", Front(world, 2).midtexture);
        Assert.Equal(103, world.lines[2].special);
        Assert.Empty(SwitchSounds(world));
    }

    [Fact]
    public void ASpecialWithoutASwitchTextureStillRuns()
    {
        Level level = SpecialsLevel();
        FrontOf(level, 1).MidTexture = "BRICK1";
        World world = Specials(level);
        UseLine1(world);
        Assert.Equal("BRICK1", Front(world, 1).midtexture);
        Assert.Equal(0, world.lines[1].special);
        Assert.Empty(SwitchSounds(world));
        Assert.NotNull(world.sectors[1 + 3].specialdata); // the tagged doors
    }

    [Fact]
    public void TheButtonOfTheDoorOpensItAndTurnsBack()
    {
        World world = Specials();
        UseLine27(world);
        Assert.Equal("SW2BRCOM", Front(world, 27).midtexture);
        var door = Assert.IsType<vldoor_t>(world.sectors[SyntheticIwad.DoorSector].specialdata);
        Assert.Equal(vldoor_e.vld_normal, door.type);
        Run(world, World.BUTTONTIME);
        Assert.Equal("SW1BRCOM", Front(world, 27).midtexture);
        Assert.True(world.sectors[SyntheticIwad.DoorSector].ceilingheight > 0);
    }

    [Theory]
    [InlineData(0, bwhere_e.top)]
    [InlineData(2, bwhere_e.bottom)]
    public void TopAndBottomTexturesSwitchToo(int part, bwhere_e where)
    {
        Level level = SpecialsLevel();
        Side side = FrontOf(level, 0);
        side.MidTexture = "BRICK1";
        if (part == 0)
            side.TopTexture = "SW1BRCOM";
        else
            side.BottomTexture = "SW1BRCOM";
        World world = Specials(level);
        UseLine0(world);
        Assert.Equal("SW2BRCOM", part == 0 ? Front(world, 0).toptexture : Front(world, 0).bottomtexture);
        Assert.Equal("BRICK1", Front(world, 0).midtexture);
        Assert.Equal(where, world.buttonlist[0].where);
        Run(world, World.BUTTONTIME);
        Assert.Equal("SW1BRCOM", part == 0 ? Front(world, 0).toptexture : Front(world, 0).bottomtexture);
    }

    [Fact]
    public void TheListOrderComesBeforeThePartOrder()
    {
        // Top SW2BRCOM (entry 1), middle SW1BRCOM (entry 0): entry 0 is found first, on the middle.
        World world = Specials();
        side_t side = Front(world, 1);
        side.toptexture = "SW2BRCOM";
        world.P_ChangeSwitchTexture(world.lines[1], 0);
        Assert.Equal("SW2BRCOM", side.toptexture);
        Assert.Equal("SW2BRCOM", side.midtexture);

        // Both parts the same switch: the top.
        world = Specials();
        side = Front(world, 1);
        side.toptexture = "SW1BRCOM";
        world.P_ChangeSwitchTexture(world.lines[1], 0);
        Assert.Equal("SW2BRCOM", side.toptexture);
        Assert.Equal("SW1BRCOM", side.midtexture);
    }

    [Fact]
    public void AnOnTextureTurnsOffAndThePressedButtonIsNotRestarted()
    {
        World world = Specials();
        line_t line = world.lines[0];
        world.P_ChangeSwitchTexture(line, 1);
        Assert.Equal("SW2BRCOM", Front(world, 0).midtexture);
        Run(world, 10);
        // Pressed again while on: SW2BRCOM is in the list too, so it turns back to SW1BRCOM at once,
        // and P_StartButton sees the line's button running and starts none.
        world.P_ChangeSwitchTexture(line, 1);
        Assert.Equal("SW1BRCOM", Front(world, 0).midtexture);
        Assert.Equal(1, ActiveButtons(world));
        Assert.Equal("SW1BRCOM", world.buttonlist[0].btexture);
        Assert.Equal(World.BUTTONTIME - 10, world.buttonlist[0].btimer);
        Run(world, World.BUTTONTIME - 10);
        Assert.Equal("SW1BRCOM", Front(world, 0).midtexture);
        Assert.Equal(0, ActiveButtons(world));
    }

    [Fact]
    public void TheSoundComesFromTheFirstButtonsOrigin()
    {
        World world = Specials();
        UseLine0(world); // slot 0: line 0, the corridor (sector 0)
        world.sounds.Clear();
        UseLine27(world); // slot 1: line 27, alcove 0 (sector 1)
        Assert.Equal(new[] { new sound_event_t(sfxenum_t.sfx_swtchn, null, world.sectors[0]) }, SwitchSounds(world));
        Assert.Same(world.lines[27], world.buttonlist[1].line);
        Assert.Same(world.sectors[1], world.buttonlist[1].soundorg);

        // Each button's release comes from its own line's front sector.
        Run(world, World.BUTTONTIME - 1);
        world.sounds.Clear();
        Run(world, 1);
        Assert.Equal(new[]
        {
            new sound_event_t(sfxenum_t.sfx_swtchn, null, world.sectors[0]),
            new sound_event_t(sfxenum_t.sfx_swtchn, null, world.sectors[1]),
        }, SwitchSounds(world));
    }

    [Fact]
    public void TheExitSoundNeverPlaysForASwitch()
    {
        // p_switch.c checks for special 11 after clearing it: an exit switch (S1) sounds as any switch.
        World world = Specials();
        line_t line = world.lines[1];
        line.special = 11;
        world.P_ChangeSwitchTexture(line, 0);
        Assert.Equal(sfxenum_t.sfx_swtchn, Assert.Single(SwitchSounds(world)).sfx);

        // Only a line keeping special 11 (useAgain) would play it.
        world = Specials();
        line = world.lines[1];
        line.special = 11;
        world.P_ChangeSwitchTexture(line, 1);
        Assert.Equal(sfxenum_t.sfx_swtchx, Assert.Single(SwitchSounds(world)).sfx);
    }

    [Fact]
    public void LiftAndFloorSwitchesTurnWhenTheirSpecialStarts()
    {
        // T5.5: line 1 (SW1BRCOM, tag 5: alcoves 3, 7 and 12) as an SR lift (62) and an S1 floor (20).
        World world = Specials();
        world.lines[1].special = 62;
        UseLine1(world);
        Assert.Equal("SW2BRCOM", Front(world, 1).midtexture);
        Assert.IsType<plat_t>(world.sectors[1 + 3].specialdata);
        Run(world, 35);
        Assert.Equal("SW1BRCOM", Front(world, 1).midtexture);
        // The lifts still move: nothing starts and the button stays off.
        UseLine1(world);
        Assert.Equal("SW1BRCOM", Front(world, 1).midtexture);
        Assert.Equal(0, ActiveButtons(world));

        world = Specials();
        world.lines[1].special = 20;
        UseLine1(world);
        Assert.Equal("SW2BRCOM", Front(world, 1).midtexture);
        Assert.Equal(0, world.lines[1].special);
        Assert.Equal(plattype_e.raiseToNearestAndChange, Assert.IsType<plat_t>(world.sectors[1 + 3].specialdata).type);
    }

    [Fact]
    public void MoreThanMaxButtonsIsAnError()
    {
        World world = Specials();
        for (int i = 0; i < World.MAXBUTTONS; i++)
            world.P_StartButton(world.lines[27 + i], bwhere_e.middle, "BRICK1", World.BUTTONTIME);
        Assert.Equal(World.MAXBUTTONS, ActiveButtons(world));
        world.P_StartButton(world.lines[27], bwhere_e.middle, "BRICK1", World.BUTTONTIME); // already pressed: nothing
        Assert.Throws<InvalidOperationException>(() =>
            world.P_StartButton(world.lines[27 + World.MAXBUTTONS], bwhere_e.middle, "BRICK1", World.BUTTONTIME));
    }

    [Fact]
    public void ANewLevelClearsTheButtons()
    {
        World world = Specials();
        UseLine0(world);
        Assert.Equal(1, ActiveButtons(world));
        world.G_DoLoadLevel(SpecialsLevel());
        Assert.Equal(0, ActiveButtons(world));
        Assert.All(world.buttonlist, b => Assert.Null(b.line));
    }

    [Fact]
    public void Doom1E1M1sExitSwitchTurnsAndExits()
    {
        WadArchive wad = WadArchive.Open(TestWads.RequireDoom1());
        var world = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium), Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(wad, "E1M1"));
        const int exit = 330;
        Assert.Equal(11, world.lines[exit].special);
        Assert.Equal("SW1STRTN", Front(world, exit).midtexture);
        Use(world, 2940, -4768, 180);
        Assert.Equal("SW2STRTN", Front(world, exit).midtexture);
        Assert.Equal(0, world.lines[exit].special);
        Assert.Contains("G_ExitLevel()", world.unported);
        Assert.Equal(sfxenum_t.sfx_swtchn, Assert.Single(SwitchSounds(world)).sfx);
        Run(world, 100);
        Assert.Equal("SW2STRTN", Front(world, exit).midtexture);
    }
}
