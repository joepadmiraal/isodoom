using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T6.2: p_sight.c <c>P_CheckSight</c> (the REJECT table, then the BSP),
/// p_enemy.c <c>P_NoiseAlert</c>/<c>P_RecursiveSound</c> and
/// <c>P_LookForPlayers</c> on synthetic <see cref="TestMap"/>s. Eye height is
/// 3/4 of the looker's height: 42 for the 56-unit player and zombieman.
/// </summary>
public class SightSoundTests
{
    private const int FRACUNIT = 1 << 16;

    private static int F(int units) => units * FRACUNIT;

    private static uint Deg(int degrees) => (uint)((ulong)degrees * 0x100000000UL / 360);

    private static mobj_t Player(World world) => world.players[0].mo!;

    private static mobj_t Monster(World world) => world.Mobjs().Single(m => m.type == mobjtype_t.MT_POSSESSED);

    /// <summary>Puts a thing at (x, y) map units on its new sector's floor.</summary>
    private static void Move(World world, mobj_t mo, int x, int y)
    {
        world.P_UnsetThingPosition(mo);
        mo.x = F(x);
        mo.y = F(y);
        world.P_SetThingPosition(mo);
        mo.z = mo.floorz = mo.subsector.sector.floorheight;
        mo.ceilingz = mo.subsector.sector.ceilingheight;
    }

    /// <summary>
    /// Three rooms west to east: A (x 0..256), the middle room M (256..320, floor
    /// <paramref name="floor"/>, ceiling <paramref name="ceiling"/>) and B (320..576);
    /// A and B have floor 0 and ceiling 128. A zombieman in A at (64, 128) facing east, the
    /// player in B at (512, 128).
    /// </summary>
    private static TestMap ThreeRooms(int floor, int ceiling) =>
        TestMap.Strip(0, 0, 256, new TestMap.Room(256, 0, 128), new TestMap.Room(64, floor, ceiling), new TestMap.Room(256, 0, 128))
            .Thing(64, 128, 3004, 0).Player(512, 128);

    // ---- P_CheckSight ----

    [Theory]
    [InlineData(0, 128, true)]   // no height difference: the boundaries are skipped
    [InlineData(40, 64, true)]   // a window from 40 to 64, around the eyes at 42
    [InlineData(0, 30, true)]    // a low opening: the feet still show under it
    [InlineData(24, 128, true)]  // a step below the eyes
    [InlineData(100, 128, false)] // a floor above the eyes and the target's top
    [InlineData(0, 0, false)]    // a closed door
    [InlineData(64, 64, false)]  // a closed door off the floor
    public void SightPassesOpeningsAndStopsAtClosedOnes(int floor, int ceiling, bool seen)
    {
        World world = ThreeRooms(floor, ceiling).Load();
        Assert.Equal(seen, world.P_CheckSight(Monster(world), Player(world)));
        Assert.Equal(seen, world.P_CheckSight(Player(world), Monster(world)));
        Assert.Equal(new[] { 0, 2 }, world.sightcounts);
    }

    [Fact]
    public void DivlineSideKeepsVanillasHorizontalLineBug()
    {
        var vertical = new divline_t(F(128), 0, 0, F(64));
        Assert.Equal(2, World.P_DivlineSide(F(128), F(500), vertical));
        Assert.Equal(0, World.P_DivlineSide(F(200), F(500), vertical)); // east of a northward line: front
        Assert.Equal(1, World.P_DivlineSide(F(100), F(500), vertical));

        // a horizontal line: "on the line" compares x with its y (sic)
        var horizontal = new divline_t(0, F(128), F(64), 0);
        Assert.Equal(2, World.P_DivlineSide(F(128), F(500), horizontal));
        Assert.Equal(0, World.P_DivlineSide(F(100), F(128), horizontal)); // on the line, but not 2: y <= its y, an eastward line's front
        Assert.Equal(1, World.P_DivlineSide(F(100), F(200), horizontal));

        // a diagonal line: whole units, 2 on the line
        var diagonal = new divline_t(0, 0, F(64), F(64));
        Assert.Equal(2, World.P_DivlineSide(F(10), F(10), diagonal));
        Assert.Equal(0, World.P_DivlineSide(F(10), F(5), diagonal));
        Assert.Equal(1, World.P_DivlineSide(F(5), F(10), diagonal));
    }

    [Fact]
    public void AOneSidedLineBlocksSight()
    {
        TestMap map = ThreeRooms(0, 128);
        map.Flags(map.Boundaries[1], Line.ML_BLOCKING); // no longer ML_TWOSIDED: a wall to sight
        World world = map.Load();
        Assert.False(world.P_CheckSight(Monster(world), Player(world)));
        Assert.False(world.P_CheckSight(Player(world), Monster(world)));

        // in the same room, the wall is not crossed
        Move(world, Player(world), 200, 40);
        Assert.True(world.P_CheckSight(Monster(world), Player(world)));
    }

    [Fact]
    public void ALowWallHidesThePlayerFromAMonsterCloseToItButNotFromOneFarAway()
    {
        // M's floor at 48, 6 above the eyes: the player (top at 56, 14 above them) shows over
        // it when the wall is near the player's end of the line of sight.
        World world = ThreeRooms(48, 128).Load();
        mobj_t monster = Monster(world), player = Player(world);

        Move(world, monster, 16, 128);
        Move(world, player, 352, 128);
        Assert.True(world.P_CheckSight(monster, player));

        Move(world, monster, 224, 128);
        Assert.False(world.P_CheckSight(monster, player));

        // standing on the wall, the monster looks down at the player
        Move(world, monster, 288, 128);
        Assert.Equal(F(48), monster.z);
        Assert.True(world.P_CheckSight(monster, player));
    }

    [Fact]
    public void SightCrossesTheBspDiagonally()
    {
        // five rooms with windows between them; diagonal lines of sight cross each partition
        TestMap map = TestMap.Strip(0, 0, 256,
                new TestMap.Room(128, 0, 128), new TestMap.Room(128, 16, 100), new TestMap.Room(128, 0, 128),
                new TestMap.Room(128, 16, 100), new TestMap.Room(128, 0, 128))
            .Thing(32, 16, 3004, 45).Player(608, 240);
        World world = map.Load();
        Assert.True(world.P_CheckSight(Monster(world), Player(world)));
        Assert.True(world.P_CheckSight(Player(world), Monster(world)));

        // the middle room closed: blocked both ways
        world.sectors[2].ceilingheight = 0;
        Assert.False(world.P_CheckSight(Monster(world), Player(world)));
        Assert.False(world.P_CheckSight(Player(world), Monster(world)));
    }

    [Fact]
    public void AMapWithoutNodesChecksItsOneSubsector()
    {
        World world = TestMap.Polygon(0, 128, (0, 0), (0, 512), (512, 512), (512, 0))
            .Thing(64, 64, 3004).Player(448, 448).Load();
        Assert.Empty(world.level.Nodes);
        Assert.True(world.P_CheckSight(Monster(world), Player(world)));
    }

    [Fact]
    public void TheRejectTableAnswersFirstAndOnlyForItsOrderedPair()
    {
        World world = ThreeRooms(0, 128).Reject(0, 2).Load();
        mobj_t monster = Monster(world), player = Player(world);
        int validcount = world.validcount;

        // nothing in A sees into B, though nothing is in the way
        Assert.False(world.P_CheckSight(monster, player));
        Assert.Equal(new[] { 1, 0 }, world.sightcounts);
        Assert.Equal(validcount, world.validcount); // no BSP trace

        // B into A is not rejected
        Assert.True(world.P_CheckSight(player, monster));
        Assert.Equal(new[] { 1, 1 }, world.sightcounts);

        // the bit goes by the things' sectors: from M the monster sees B again
        Move(world, monster, 288, 128);
        Assert.True(world.P_CheckSight(monster, player));
    }

    [Fact]
    public void SightUsesNoRandomNumbers()
    {
        World world = ThreeRooms(40, 64).Load();
        int index = world.random.prndindex;
        world.P_CheckSight(Monster(world), Player(world));
        world.P_LookForPlayers(Monster(world), false);
        Assert.Equal(index, world.random.prndindex);
    }

    // ---- P_NoiseAlert / P_RecursiveSound ----

    /// <summary>
    /// Five 128-unit rooms (sectors 0–4) west to east, all open (floor 0, ceiling 128);
    /// boundaries 2 (rooms 1 | 2) and 4 (3 | 4) block sound. The player in room 0.
    /// </summary>
    private static World SoundRooms(int closed = -1)
    {
        var rooms = new TestMap.Room[5];
        for (int i = 0; i < rooms.Length; i++)
            rooms[i] = new TestMap.Room(128, 0, i == closed ? 0 : 128);
        TestMap map = TestMap.Strip(0, 0, 128, rooms).Player(64, 64);
        map.Flags(map.Boundaries[2], Line.ML_TWOSIDED | Line.ML_SOUNDBLOCK);
        map.Flags(map.Boundaries[4], Line.ML_TWOSIDED | Line.ML_SOUNDBLOCK);
        return map.Load();
    }

    private static int[] Traversed(World world) => [.. world.sectors.Select(s => s.soundtraversed)];

    [Fact]
    public void SoundCrossesOneSoundBlockingLineButNotTwo()
    {
        World world = SoundRooms();
        mobj_t player = Player(world);
        world.P_NoiseAlert(player, player);

        // rooms 0, 1 directly; 2 and 3 after one blocking line; room 4 would be the second
        Assert.Equal(new[] { 1, 1, 2, 2, 0 }, Traversed(world));
        Assert.Equal([player, player, player, player, null], world.sectors.Select(s => s.soundtarget).ToArray());
    }

    [Fact]
    public void SoundFromBetweenTwoSoundBlockingLinesReachesBothSides()
    {
        World world = SoundRooms();
        mobj_t player = Player(world);
        Move(world, player, 320, 64); // room 2
        world.P_NoiseAlert(player, player);
        Assert.Equal(new[] { 2, 2, 1, 1, 2 }, Traversed(world));
        Assert.All(world.sectors, s => Assert.Same(player, s.soundtarget));
    }

    [Fact]
    public void AClosedDoorStopsSound()
    {
        World world = SoundRooms(closed: 1);
        mobj_t player = Player(world);
        world.P_NoiseAlert(player, player);
        Assert.Equal(new[] { 1, 0, 0, 0, 0 }, Traversed(world));
        Assert.Null(world.sectors[2].soundtarget);
    }

    [Fact]
    public void ANewNoiseFloodsAgainWithItsTarget()
    {
        World world = SoundRooms();
        mobj_t player = Player(world);
        world.P_NoiseAlert(player, player);

        // a second noise from room 4: its own flood (a new validcount), its own target, as far
        // as room 2; rooms 0 and 1 keep the first noise
        mobj_t other = world.P_SpawnMobj(F(576), F(64), World.ONFLOORZ, mobjtype_t.MT_POSSESSED);
        world.P_NoiseAlert(other, other);
        Assert.Equal(new[] { 1, 1, 2, 2, 1 }, Traversed(world));
        Assert.All(world.sectors.Skip(2), s => Assert.Same(other, s.soundtarget));
        Assert.Same(player, world.sectors[1].soundtarget);
    }

    // ---- P_LookForPlayers ----

    /// <summary>A 1024-unit square room: a zombieman at (512, 512) facing <paramref name="angle"/>, the player at (<paramref name="px"/>, 512).</summary>
    private static World LookRoom(int angle, int px = 768)
    {
        World world = TestMap.Polygon(0, 128, (0, 0), (0, 1024), (1024, 1024), (1024, 0))
            .Thing(512, 512, 3004, angle).Player(px, 512).Load();
        Monster(world).lastlook = 0;
        return world;
    }

    [Theory]
    [InlineData(0, 768, false, true)]   // facing the player
    [InlineData(90, 768, false, true)]  // the player square to its side is not behind it
    [InlineData(180, 768, false, false)] // behind its back
    [InlineData(135, 768, false, false)]
    [InlineData(180, 560, false, true)] // behind its back, within MELEERANGE (48 away)
    [InlineData(180, 577, false, false)] // 65 away
    [InlineData(180, 768, true, true)]  // allaround
    public void AMonsterSeesThePlayerInFrontOrClose(int angle, int px, bool allaround, bool seen)
    {
        World world = LookRoom(angle, px);
        mobj_t monster = Monster(world);
        Assert.Equal(seen, world.P_LookForPlayers(monster, allaround));
        Assert.Same(seen ? Player(world) : null, monster.target);
    }

    [Fact]
    public void ADeadOrHiddenPlayerIsNotSeen()
    {
        World world = LookRoom(0);
        mobj_t monster = Monster(world);
        world.players[0].health = 0;
        Assert.False(world.P_LookForPlayers(monster, true));
        Assert.Equal(0, world.sightcounts[1]);

        world.players[0].health = 100;
        TestMap map = ThreeRooms(0, 0);
        world = map.Load();
        Monster(world).lastlook = 0;
        Assert.False(world.P_LookForPlayers(Monster(world), true));
        Assert.Null(Monster(world).target);
    }

    [Fact]
    public void WithOnePlayerALookChecksItTwiceOrNotAtAll()
    {
        // vanilla's loop: from lastlook 0 (stop 3) it comes round to player 0 twice
        World world = LookRoom(180);
        mobj_t monster = Monster(world);
        Assert.False(world.P_LookForPlayers(monster, false));
        Assert.Equal(2, world.sightcounts[1]);
        Assert.Equal(0, monster.lastlook);

        // from lastlook 1 (stop 0) it reaches player 0 as the stop and looks at nobody
        world = LookRoom(0);
        monster = Monster(world);
        monster.lastlook = 1;
        Assert.False(world.P_LookForPlayers(monster, false));
        Assert.Equal(0, world.sightcounts[1]);
        Assert.Equal(0, monster.lastlook);
        Assert.True(world.P_LookForPlayers(monster, false));
        Assert.Equal(1, world.sightcounts[1]);
    }
}
