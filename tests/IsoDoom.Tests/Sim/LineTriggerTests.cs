using System.Collections.Generic;
using System.Linq;
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
/// opening, a WR lift; line 25 alcove 22's, a W1 floor). The effects not
/// ported yet are stubs until T5.6-T5.8, which record their calls in
/// <see cref="World.unported"/>; the doors (T5.3), lifts and floors (T5.5)
/// are checked by the thinkers they start in the tagged alcoves 3, 7 and 12.
/// </summary>
public class LineTriggerTests
{
    private const int FRACUNIT = 1 << 16;

    private static int F(int units) => units * FRACUNIT;

    private static uint Deg(int degrees) => (uint)((ulong)degrees * 0x100000000UL / 360);

    private static Level SpecialsLevel() =>
        Level.Load(new WadArchive([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]), "E1M2");

    private static World Specials(Tweaks? tweaks = null, Level? level = null)
    {
        var world = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium), tweaks ?? Tweaks.Vanilla);
        world.G_DoLoadLevel(level ?? SpecialsLevel());
        return world;
    }

    private static mobj_t Player(World world) => world.players[0].mo!;

    /// <summary>The tag-5 alcoves (sectors 4, 8 and 13), which lines 0 and 1 open as doors.</summary>
    private static readonly int[] Tagged = [1 + 3, 1 + 7, 1 + 12];

    /// <summary>
    /// The type of the doors moving the tag-5 alcoves (every one has the same),
    /// or null when none moves.
    /// </summary>
    private static vldoor_e? TaggedDoors(World world)
    {
        var doors = Tagged.Select(s => world.sectors[s].specialdata).ToList();
        if (doors.All(d => d == null))
            return null;
        Assert.All(doors, d => Assert.IsType<vldoor_t>(d));
        vldoor_e type = ((vldoor_t)doors[0]!).type;
        Assert.All(doors, d => Assert.Equal(type, ((vldoor_t)d!).type));
        return type;
    }

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
        Assert.Equal(11, world.lines[SyntheticIwad.ExitSwitchLine].special); // the exits (T5.8)
        Assert.Equal(51, world.lines[SyntheticIwad.SecretExitSwitchLine].special);
        Assert.Equal(52, world.lines[SyntheticIwad.ExitWalkLine].special);
        Assert.Equal(124, world.lines[SyntheticIwad.SecretExitWalkLine].special);
        Assert.Empty(world.unported); // the sector specials are ported (T5.7)
        Assert.Equal(1, world.totalsecret); // alcove 15 (T5.8)
    }

    /// <summary>The thinkers of type <typeparamref name="T"/> moving the tag-5 alcoves (none or all three).</summary>
    private static T[] TaggedMovers<T>(World world) where T : thinker_t
    {
        T[] movers = [.. Tagged.Select(s => world.sectors[s].specialdata).OfType<T>()];
        Assert.True(movers.Length is 0 or 3, $"{movers.Length} of the tagged alcoves move");
        return movers;
    }

    /// <summary>Runs tics with no input until no tagged alcove moves (at most <paramref name="max"/>).</summary>
    private static void RunUntilIdle(World world, int max = 1000)
    {
        for (int i = 0; i < max && Tagged.Any(s => world.sectors[s].specialdata != null); i++)
            world.G_Ticker(new ticcmd_t());
        Assert.All(Tagged, s => Assert.Null(world.sectors[s].specialdata));
    }

    [Fact]
    public void AW1LineFiresOnceForThePlayer()
    {
        World world = Specials();
        mobj_t mo = Player(world);
        world.PlaceMobj(mo, F(720), F(-40));
        // Into alcove 22 (floor 8) across line 25: the box touches the line, the centre crosses it.
        Assert.True(world.P_TryMove(mo, F(720), F(10)));
        Assert.All(TaggedMovers<floormove_t>(world), f => Assert.Equal(floor_e.lowerFloorToLowest, f.type));
        Assert.Equal(3, TaggedMovers<floormove_t>(world).Length);
        Assert.Equal(0, world.lines[25].special);
        RunUntilIdle(world);
        // Back out and in again: the line has no special left.
        Assert.True(world.P_TryMove(mo, F(720), F(-10)));
        Assert.True(world.P_TryMove(mo, F(720), F(10)));
        Assert.Empty(TaggedMovers<thinker_t>(world));
        Assert.Empty(world.unported);
    }

    [Fact]
    public void AWRLineFiresOnEveryCrossingEitherWay()
    {
        World world = Specials();
        mobj_t mo = Player(world);
        world.PlaceMobj(mo, F(16), F(-40));
        Assert.True(world.P_TryMove(mo, F(16), F(10)));  // in (from the back side)
        Assert.Equal(3, TaggedMovers<plat_t>(world).Length);
        RunUntilIdle(world);
        Assert.True(world.P_TryMove(mo, F(16), F(-10))); // out (from the front)
        Assert.Equal(3, TaggedMovers<plat_t>(world).Length);
        RunUntilIdle(world);
        Assert.True(world.P_TryMove(mo, F(16), F(10)));  // in
        Assert.Equal(3, TaggedMovers<plat_t>(world).Length);
        RunUntilIdle(world);
        Assert.True(world.P_TryMove(mo, F(16), F(12)));  // no crossing
        Assert.Empty(TaggedMovers<plat_t>(world));
        Assert.Equal(88, world.lines[3].special);
    }

    [Fact]
    public void MonstersTriggerOnlyTheirLinesAndMissilesNone()
    {
        World world = Specials();
        // A lost soul (radius 16, so it fits the 32-unit alcoves) into alcove 0: the WR lift (88) is a monster line.
        mobj_t skull = world.P_SpawnMobj(F(16), F(-40), World.ONFLOORZ, mobjtype_t.MT_SKULL);
        Assert.True(world.P_TryMove(skull, F(16), F(10)));
        Assert.Equal(3, TaggedMovers<plat_t>(world).Length);
        RunUntilIdle(world);
        // Into alcove 22: the W1 floor (38) is the player's only, and stays.
        skull = world.P_SpawnMobj(F(720), F(-40), World.ONFLOORZ, mobjtype_t.MT_SKULL);
        Assert.True(world.P_TryMove(skull, F(720), F(10)));
        Assert.Empty(TaggedMovers<thinker_t>(world));
        Assert.Equal(38, world.lines[25].special);
        // An imp's fireball across the lift line triggers nothing.
        world = Specials();
        mobj_t shot = world.P_SpawnMobj(F(16), F(-40), F(32), mobjtype_t.MT_TROOPSHOT);
        Assert.True(world.P_TryMove(shot, F(16), F(4)));
        Assert.Empty(TaggedMovers<thinker_t>(world));
    }

    [Fact]
    public void AnS1SwitchWorksOnceAndAFailedOneStays()
    {
        World world = Specials();
        Use(world, 40, -64, 180); // west, at line 1
        Assert.Equal(vldoor_e.vld_open, TaggedDoors(world));
        Assert.Equal(0, world.lines[1].special);
        // The doors open to the corridor's ceiling less 4, below the alcoves' ceilings: there at once.
        world.G_Ticker(new ticcmd_t());
        Assert.Null(TaggedDoors(world));
        Assert.All(Tagged, s => Assert.Equal(F(124), world.sectors[s].ceilingheight));
        world.sectors[Tagged[0]].ceilingheight = F(200);
        Use(world, 40, -64, 180);
        Assert.Null(TaggedDoors(world));

        // Line 2's tag (9) has no sector: the door does not start and the switch stays usable.
        Use(world, 740, -64, 0);
        Use(world, 740, -64, 0);
        Assert.Equal(103, world.lines[2].special);
        Assert.Empty(world.unported);
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
        // for three tics: used once. Released and pressed again once the doors are done: once more.
        var doors = new HashSet<vldoor_t>(ReferenceEqualityComparer.Instance);
        void Tic(ticcmd_t cmd)
        {
            world.G_Ticker(cmd);
            foreach (int s in Tagged)
            {
                if (world.sectors[s].specialdata is vldoor_t door)
                    doors.Add(door);
            }
        }
        Tic(use);
        Assert.Empty(doors);
        foreach (ticcmd_t cmd in new[] { none, use, use, use })
            Tic(cmd);
        Assert.Equal(3, doors.Count);
        Assert.All(doors, d => Assert.Equal(vldoor_e.vld_normal, d.type));
        // Up at once (to 124, below the alcoves' ceilings), 150 tics open, down to the floor (2 units a tic).
        for (int i = 0; i < VDoor.VDOORWAIT + 4; i++)
            Tic(none);
        Assert.Null(TaggedDoors(world));
        Assert.All(Tagged, s => Assert.Equal(world.sectors[s].floorheight, world.sectors[s].ceilingheight));
        Tic(use);
        Assert.Equal(6, doors.Count);
        Assert.Equal(63, world.lines[0].special);
    }

    [Fact]
    public void ALineIsUsedFromItsFrontOnlyAndStopsTheTrace()
    {
        World world = Specials();
        world.lines[4].special = 103; // alcove 1's opening, whose front is the alcove
        Use(world, 48, -40, 90);      // from the corridor: its back
        Assert.Null(TaggedDoors(world));
        Assert.Equal(103, world.lines[4].special);
        Assert.False(world.usetraceused);

        // A walk-over line (alcove 0's lift, from its back) stops the trace before a switch behind it.
        world.lines[51].special = 103; // alcove 0's west wall, facing the alcove
        world.lines[51].tag = 5;
        Use(world, 16, -40, 100);
        Assert.Null(TaggedDoors(world));
    }

    [Fact]
    public void TheUseFallbackTakesTheNearestUsableLineWhenTheAimMisses()
    {
        // At (24, -80) aiming at 100°: the trace ends at (13, -17) without a line. Line 1 (the west
        // wall) is 24 units away, line 0 (the south wall) 48 but behind the aim.
        World vanilla = Specials();
        Use(vanilla, 24, -80, 100);
        Assert.Null(TaggedDoors(vanilla));

        var tweaks = new Tweaks { UseFallback = true };
        World world = Specials(tweaks);
        Assert.Same(world.lines[1], world.UseFallbackLine(PlaceAndGet(world, 24, -80, 100)));
        Use(world, 24, -80, 100);
        Assert.Equal(vldoor_e.vld_open, TaggedDoors(world));
        Assert.Equal(0, world.lines[1].special);

        // Aiming south-east (330°; the trace ends at (79, -112)), the nearer line 1 is behind the
        // player: line 0 is taken.
        world = Specials(tweaks);
        Use(world, 24, -80, 330);
        Assert.Equal(vldoor_e.vld_normal, TaggedDoors(world));

        // Nothing within USERANGE: at (100, -40) the walls are 88 and 100 units away.
        world = Specials(tweaks);
        Use(world, 100, -40, 90);
        Assert.Null(TaggedDoors(world));
        Assert.Null(world.UseFallbackLine(Player(world)));

        // A trace that hits its line is not second-guessed: aiming at line 1 from (40, -110) uses it,
        // not line 0 below the player (18 units away against 40).
        world = Specials(tweaks);
        Use(world, 40, -110, 180);
        Assert.Equal(vldoor_e.vld_open, TaggedDoors(world));
        Assert.Equal(0, world.lines[1].special);
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
        Assert.Equal(vldoor_e.vld_open, TaggedDoors(world));
        Assert.Equal(0, world.lines[51].special);

        // With the alcove shut (no opening), not through the wall.
        world = Setup(closeAlcove: true);
        Use(world, 24, -20, 90);
        Assert.Null(TaggedDoors(world));
        Assert.Equal(103, world.lines[51].special);
    }

    [Fact]
    public void TheTopDownUseTracesAlongTheAim()
    {
        // With the twin-stick tweaks the ticcmd's angle is the aim: aiming west at (40, -64) uses line 1.
        World world = Specials(Tweaks.TopDown);
        world.PlaceMobj(Player(world), F(40), F(-64), Deg(90));
        world.G_Ticker(new ticcmd_t()); // releases use (held from the spawn)
        world.G_Ticker(new ticcmd_t { angleturn = unchecked((short)(Deg(180) >> 16)), buttons = buttoncode_t.BT_USE });
        // The doors started and finished in the tic (the alcoves' ceilings are above where they open to).
        Assert.Equal(0, world.lines[1].special);
        Assert.All(Tagged, s => Assert.Equal(F(124), world.sectors[s].ceilingheight));
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
        Assert.Equal(2 + 1, world.totalsecret); // and the map's own, alcove 15 (T5.8)
        Assert.Empty(world.unported);
        // The light thinkers (T5.7), after the things, in sector order.
        var lights = new List<thinker_t>();
        for (thinker_t th = world.thinkercap.next; th != world.thinkercap; th = th.next)
        {
            if (th is lightflash_t { sector.Index: 6 } or strobe_t { sector.Index: 7 })
                lights.Add(th);
        }
        Assert.Collection(lights,
            th => Assert.Same(world.sectors[6], Assert.IsType<lightflash_t>(th).sector),
            th => Assert.Equal(LightFlash.FASTDARK, Assert.IsType<strobe_t>(th).darktime));
        Assert.Equal(0, world.sectors[6].special);
        Assert.Equal(4, world.sectors[7].special); // death slime keeps its special
        vldoor_t door = Assert.IsType<vldoor_t>(world.sectors[8].specialdata);
        Assert.Equal(vldoor_e.vld_raiseIn5Mins, door.type);
        Assert.Equal(0, world.sectors[8].special);
        Assert.Equal(2, world.numlinespecials); // and line 28's (the map's, T5.7)
        Assert.Same(world.lines[27], world.linespeciallist[0]);
        Assert.Same(world.lines[SyntheticIwad.ScrollLine], world.linespeciallist[1]);

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
        var wad = WadArchive.Open(TestWads.RequireDoom1());
        var world = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium), Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(wad, "E1M1"));
        Assert.Equal(3, world.totalsecret);
        Assert.Empty(world.unported);
        // Sector 40's blinking light (the start alcove; T4.8's note) and more (T5.7).
        bool flash40 = false;
        for (thinker_t th = world.thinkercap.next; th != world.thinkercap; th = th.next)
            flash40 |= th is lightflash_t { sector.Index: 40 };
        Assert.True(flash40);
    }
}
