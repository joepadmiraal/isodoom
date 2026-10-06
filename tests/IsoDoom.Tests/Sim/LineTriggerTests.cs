using System;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T5.2: the walk-over (<c>P_CrossSpecialLine</c>) and use (<c>P_UseLines</c>,
/// <c>P_UseSpecialLine</c>) triggers with vanilla's once-only rules, the use
/// fallback (SPEC §6.3 #3) and <c>P_SpawnSpecials</c>/<c>P_UpdateSpecials</c>,
/// on the synthetic specials map (E1M2; see <c>SyntheticIwad.BuildSpecialsMap</c>:
/// line 0 the corridor's south wall, an SR button; line 1 its west wall, an S1
/// switch; line 2 its east wall, an S1 switch of no sector; line 3 alcove 0's
/// opening, a WR lift; line 25 alcove 22's, a W1 floor). The effects are
/// stubs until T5.3-T5.8, which record their calls in <see cref="World.unported"/>.
/// </summary>
public class LineTriggerTests
{
    private const int FRACUNIT = 1 << 16;

    private static int F(int units) => units * FRACUNIT;

    private static uint Deg(int degrees) => (uint)((ulong)degrees * 0x100000000UL / 360);

    private static Level SpecialsLevel() =>
        Level.Load(new WadArchive(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) }), "E1M2");

    private static World Specials(Tweaks? tweaks = null, Level? level = null)
    {
        var world = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium), tweaks ?? Tweaks.Vanilla);
        world.G_DoLoadLevel(level ?? SpecialsLevel());
        return world;
    }

    private static mobj_t Player(World world) => world.players[0].mo!;

    /// <summary>Puts the player at (x, y) facing <paramref name="degrees"/> and presses use once.</summary>
    private static void Use(World world, int x, int y, int degrees)
    {
        world.PlaceMobj(Player(world), F(x), F(y), Deg(degrees));
        world.P_UseLines(world.players[0]);
    }

    [Fact]
    public void TheSpecialsMapHasItsTriggers()
    {
        World world = Specials();
        Assert.Equal(63, world.lines[0].special);
        Assert.Equal(103, world.lines[1].special);
        Assert.Equal(103, world.lines[2].special);
        Assert.Equal(9, world.lines[2].tag);
        Assert.Equal(88, world.lines[3].special);
        Assert.Equal(38, world.lines[25].special);
        Assert.Empty(world.unported); // no sector specials
        Assert.Equal(0, world.totalsecret);
    }

    [Fact]
    public void AW1LineFiresOnceForThePlayer()
    {
        World world = Specials();
        mobj_t mo = Player(world);
        world.PlaceMobj(mo, F(720), F(-40));
        // Into alcove 22 (floor 8) across line 25: the box touches the line, the centre crosses it.
        Assert.True(world.P_TryMove(mo, F(720), F(10)));
        Assert.Equal(new[] { "EV_DoFloor(line 25, lowerFloorToLowest)" }, world.unported);
        Assert.Equal(0, world.lines[25].special);
        // Back out and in again: the line has no special left.
        Assert.True(world.P_TryMove(mo, F(720), F(-10)));
        Assert.True(world.P_TryMove(mo, F(720), F(10)));
        Assert.Single(world.unported);
    }

    [Fact]
    public void AWRLineFiresOnEveryCrossingEitherWay()
    {
        World world = Specials();
        mobj_t mo = Player(world);
        world.PlaceMobj(mo, F(16), F(-40));
        Assert.True(world.P_TryMove(mo, F(16), F(10)));  // in (from the back side)
        Assert.True(world.P_TryMove(mo, F(16), F(-10))); // out (from the front)
        Assert.True(world.P_TryMove(mo, F(16), F(10)));  // in
        Assert.True(world.P_TryMove(mo, F(16), F(12)));  // no crossing
        Assert.Equal(3, world.unported.Count);
        Assert.All(world.unported, call => Assert.Equal("EV_DoPlat(line 3, downWaitUpStay, 0)", call));
        Assert.Equal(88, world.lines[3].special);
    }

    [Fact]
    public void MonstersTriggerOnlyTheirLinesAndMissilesNone()
    {
        World world = Specials();
        // A lost soul (radius 16, so it fits the 32-unit alcoves) into alcove 0: the WR lift (88) is a monster line.
        mobj_t skull = world.P_SpawnMobj(F(16), F(-40), World.ONFLOORZ, mobjtype_t.MT_SKULL);
        Assert.True(world.P_TryMove(skull, F(16), F(10)));
        Assert.Equal(new[] { "EV_DoPlat(line 3, downWaitUpStay, 0)" }, world.unported);
        // Into alcove 22: the W1 floor (38) is the player's only, and stays.
        skull = world.P_SpawnMobj(F(720), F(-40), World.ONFLOORZ, mobjtype_t.MT_SKULL);
        Assert.True(world.P_TryMove(skull, F(720), F(10)));
        Assert.Single(world.unported);
        Assert.Equal(38, world.lines[25].special);
        // An imp's fireball across the lift line triggers nothing.
        world = Specials();
        mobj_t shot = world.P_SpawnMobj(F(16), F(-40), F(32), mobjtype_t.MT_TROOPSHOT);
        Assert.True(world.P_TryMove(shot, F(16), F(4)));
        Assert.Empty(world.unported);
    }

    [Fact]
    public void AnS1SwitchWorksOnceAndAFailedOneStays()
    {
        World world = Specials();
        Use(world, 40, -64, 180); // west, at line 1
        Assert.Equal(new[] { "EV_DoDoor(line 1, vld_open)" }, world.unported);
        Assert.Equal(0, world.lines[1].special);
        Use(world, 40, -64, 180);
        Assert.Single(world.unported);

        // Line 2's tag (9) has no sector: the door does not start and the switch stays usable.
        Use(world, 740, -64, 0);
        Use(world, 740, -64, 0);
        Assert.Equal(new[] { "EV_DoDoor(line 1, vld_open)", "EV_DoDoor(line 2, vld_open)", "EV_DoDoor(line 2, vld_open)" }, world.unported);
        Assert.Equal(103, world.lines[2].special);
    }

    [Fact]
    public void AnSRButtonWorksOncePerPress()
    {
        World world = Specials();
        player_t player = world.players[0];
        world.PlaceMobj(player.mo!, F(40), F(-100), Deg(270)); // south, at line 0
        var use = new ticcmd_t { buttons = buttoncode_t.BT_USE };
        var none = new ticcmd_t();
        // A use held from the spawn does nothing (P_SpawnPlayer sets usedown). Pressed and held
        // for three tics: used once. Released and pressed again: once more.
        world.G_Ticker(use);
        Assert.Empty(world.unported);
        foreach (ticcmd_t cmd in new[] { none, use, use, use, none, use })
            world.G_Ticker(cmd);
        Assert.Equal(2, world.unported.Count);
        Assert.All(world.unported, call => Assert.Equal("EV_DoDoor(line 0, vld_normal)", call));
        Assert.Equal(63, world.lines[0].special);
    }

    [Fact]
    public void ALineIsUsedFromItsFrontOnlyAndStopsTheTrace()
    {
        World world = Specials();
        world.lines[4].special = 103; // alcove 1's opening, whose front is the alcove
        Use(world, 48, -40, 90);      // from the corridor: its back
        Assert.Empty(world.unported);
        Assert.Equal(103, world.lines[4].special);
        Assert.False(world.usetraceused);

        // A walk-over line (alcove 0's lift, from its back) stops the trace before a switch behind it.
        world.lines[51].special = 103; // alcove 0's west wall, facing the alcove
        world.lines[51].tag = 5;
        Use(world, 16, -40, 100);
        Assert.Empty(world.unported);
    }

    [Fact]
    public void TheUseFallbackTakesTheNearestUsableLineWhenTheAimMisses()
    {
        // At (24, -80) aiming at 100°: the trace ends at (13, -17) without a line. Line 1 (the west
        // wall) is 24 units away, line 0 (the south wall) 48 but behind the aim.
        World vanilla = Specials();
        Use(vanilla, 24, -80, 100);
        Assert.Empty(vanilla.unported);

        var tweaks = new Tweaks { UseFallback = true };
        World world = Specials(tweaks);
        Assert.Same(world.lines[1], world.UseFallbackLine(PlaceAndGet(world, 24, -80, 100)));
        Use(world, 24, -80, 100);
        Assert.Equal(new[] { "EV_DoDoor(line 1, vld_open)" }, world.unported);

        // Aiming south-east (330°; the trace ends at (79, -112)), the nearer line 1 is behind the
        // player: line 0 is taken.
        world = Specials(tweaks);
        Use(world, 24, -80, 330);
        Assert.Equal(new[] { "EV_DoDoor(line 0, vld_normal)" }, world.unported);

        // Nothing within USERANGE: at (100, -40) the walls are 88 and 100 units away.
        world = Specials(tweaks);
        Use(world, 100, -40, 90);
        Assert.Empty(world.unported);
        Assert.Null(world.UseFallbackLine(Player(world)));

        // A trace that hits its line is not second-guessed: aiming at line 1 from (40, -110) uses it,
        // not line 0 below the player (18 units away against 40).
        world = Specials(tweaks);
        Use(world, 40, -110, 180);
        Assert.Equal(new[] { "EV_DoDoor(line 1, vld_open)" }, world.unported);
    }

    private static mobj_t PlaceAndGet(World world, int x, int y, int degrees)
    {
        world.PlaceMobj(Player(world), F(x), F(y), Deg(degrees));
        return Player(world);
    }

    [Fact]
    public void TheUseFallbackReachesPastOpenLinesButNotThroughWalls()
    {
        var tweaks = new Tweaks { UseFallback = true };
        World Setup(bool closeAlcove)
        {
            Level level = SpecialsLevel();
            World w = Specials(tweaks, level);
            w.lines[1].special = 0;        // the west wall is no switch here
            w.lines[51].special = 103;     // alcove 0's west wall, facing the alcove
            w.lines[51].tag = 5;
            if (closeAlcove)
                w.sectors[1].ceilingheight = w.sectors[1].floorheight;
            return w;
        }

        // From (24, -20) aiming north, the trace stops at alcove 0's lift line (from its back): the
        // fallback reaches line 51 (33 units away) across that open line.
        World world = Setup(closeAlcove: false);
        Use(world, 24, -20, 90);
        Assert.Equal(new[] { "EV_DoDoor(line 51, vld_open)" }, world.unported);
        Assert.Equal(0, world.lines[51].special);

        // With the alcove shut (no opening), not through the wall.
        world = Setup(closeAlcove: true);
        Use(world, 24, -20, 90);
        Assert.Empty(world.unported);
    }

    [Fact]
    public void TheTopDownUseTracesAlongTheAim()
    {
        // With the twin-stick tweaks the ticcmd's angle is the aim: aiming west at (40, -64) uses line 1.
        World world = Specials(Tweaks.TopDown);
        world.PlaceMobj(Player(world), F(40), F(-64), Deg(90));
        world.G_Ticker(new ticcmd_t()); // releases use (held from the spawn)
        world.G_Ticker(new ticcmd_t { angleturn = unchecked((short)(Deg(180) >> 16)), buttons = buttoncode_t.BT_USE });
        Assert.Equal(new[] { "EV_DoDoor(line 1, vld_open)" }, world.unported);
    }

    [Fact]
    public void SpawnSpecialsCountsSecretsAndStartsSectorAndLineEffects()
    {
        Level level = SpecialsLevel();
        level.Sectors[4].Special = 9;  // secret
        level.Sectors[5].Special = 9;
        level.Sectors[6].Special = 1;  // flickering light
        level.Sectors[7].Special = 4;  // strobe fast / death slime
        level.Sectors[8].Special = 14; // door raise in 5 minutes
        level.Lines[27].Special = 48;  // scrolling wall
        World world = Specials(level: level);
        Assert.Equal(2, world.totalsecret);
        Assert.Equal(new[]
        {
            "P_SpawnLightFlash(sector 6)",
            $"P_SpawnStrobeFlash(sector 7, {LightFlash.FASTDARK}, 0)",
            "P_SpawnDoorRaiseIn5Mins(sector 8)",
        }, world.unported);
        Assert.Equal(1, world.numlinespecials);
        Assert.Same(world.lines[27], world.linespeciallist[0]);

        // P_UpdateSpecials scrolls the wall's front side a unit a tic.
        side_t side = world.sides[world.lines[27].sidenum[0]];
        int offset = side.textureoffset;
        world.G_Ticker(new ticcmd_t());
        world.G_Ticker(new ticcmd_t());
        Assert.Equal(offset + 2 * FRACUNIT, side.textureoffset);
    }

    [Fact]
    public void MoreThan64ScrollingWallsIsAnError()
    {
        Level level = SpecialsLevel();
        for (int i = 0; i <= World.MAXLINEANIMS; i++)
            level.Lines[i].Special = 48;
        Assert.Throws<WadFormatException>(() => Specials(level: level));
    }

    [Fact]
    public void Doom1E1M1SpawnsItsSpecials()
    {
        WadArchive wad = WadArchive.Open(TestWads.RequireDoom1());
        var world = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium), Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(wad, "E1M1"));
        Assert.Equal(3, world.totalsecret);
        Assert.Contains(world.unported, call => call.StartsWith("P_SpawnLightFlash(", StringComparison.Ordinal));
    }
}
