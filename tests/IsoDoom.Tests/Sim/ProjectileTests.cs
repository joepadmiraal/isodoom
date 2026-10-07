using System;
using System.Linq;
using IsoDoom.Game;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T6.5: projectiles (p_mobj.c <c>P_SpawnPlayerMissile</c>,
/// <c>P_CheckMissileSpawn</c>, <c>P_ExplodeMissile</c>, the sky hack in
/// <c>P_XYMovement</c>; p_map.c's missile part of <c>PIT_CheckThing</c>;
/// p_pspr.c <c>A_FireMissile</c>) on synthetic <see cref="TestMap"/>s.
/// <c>MonsterAiTests</c> covers <c>P_SpawnMissile</c>; the vanilla routes
/// <c>testmap-missiles</c> and <c>e1m1-rockets</c> (and T6.4's monster
/// routes for the imps' and barons' balls; <see cref="VanillaRouteTests"/>)
/// check flights and hits against vanilla tic by tic.
/// </summary>
public class ProjectileTests
{
    private const int FRACUNIT = 1 << 16;

    private static int F(int units) => units * FRACUNIT;

    private static mobj_t Player(World world) => world.players[0].mo!;

    /// <summary>Makes the next <c>P_Random</c> return <c>rndtable[index + 1]</c> (0: 0, then 8).</summary>
    private static void NextRandom(World world, int index) => world.random.prndindex = index - 1;

    private static void Tics(World world, int n)
    {
        for (int i = 0; i < n; i++)
            world.G_Ticker(default(ticcmd_t));
    }

    private static bool Removed(mobj_t mo) => mo.function == think_t.REMOVED;

    /// <summary>One room, x 0..512, y 0..256, floor 0, ceiling 128; the player at (64, 128) facing east.</summary>
    private static TestMap OneRoomMap() => TestMap.Strip(0, 0, 256, new TestMap.Room(512, 0, 128)).Player(64, 128);

    /// <summary>
    /// <see cref="RouteTestMaps"/>' <c>missiles</c> rooms without things: W (x 0..64, ceiling 36,
    /// sky), S (64..576, ceiling 128), E (576..640, ceiling 36, sky); boundaries W | S at x 64
    /// (W behind it) and S | E at x 576 (S behind it).
    /// </summary>
    private static TestMap SkyWalls(int x, int angle)
    {
        TestMap map = TestMap.Strip(0, 0, 512,
            new TestMap.Room(64, 0, 36), new TestMap.Room(512, 0, 128), new TestMap.Room(64, 0, 36));
        return map.CeilingPic(0, "F_SKY1").CeilingPic(2, "F_SKY1").Player(x, 256, angle);
    }

    // ---- P_SpawnPlayerMissile ----

    [Fact]
    public void ARocketWithoutATargetFliesLevelAlongTheFacing()
    {
        World world = OneRoomMap().Load();
        mobj_t player = Player(world);
        world.sounds.Clear();
        NextRandom(world, 0); // P_SpawnMobj's lastlook 0, then 8 & 3 = 0: no tics off
        mobj_t rocket = world.P_SpawnPlayerMissile(player, mobjtype_t.MT_ROCKET);
        Assert.Null(world.linetarget);
        Assert.Equal(statenum_t.S_ROCKET, rocket.state);
        Assert.Same(player, rocket.target);
        Assert.Equal(0u, rocket.angle);
        Assert.Equal(Fixed.FixedMul(F(20), Tables.finecosine[0]), rocket.momx); // 20 units a tic
        Assert.Equal(Fixed.FixedMul(F(20), Tables.finesine[0]), rocket.momy); // (finesine[0] is 25, not 0)
        Assert.Equal(0, rocket.momz);
        Assert.Equal(player.x + (rocket.momx >> 1), rocket.x); // half a step on
        Assert.Equal(F(32), rocket.z);
        Assert.Contains(world.sounds, s => s.sfx == sfxenum_t.sfx_rlaunc && s.origin == rocket);

        Tics(world, 1);
        Assert.Equal(player.x + (rocket.momx >> 1) + rocket.momx, rocket.x);
        Assert.NotEqual((mobjflag_t)0, rocket.flags & mobjflag_t.MF_MISSILE); // still flying
    }

    [Theory]
    [InlineData(149, 1u << 26)] // 4 degrees left: aimed 5.6 degrees left
    [InlineData(107, unchecked(0u - (1u << 26)))] // and right
    [InlineData(128, 0u)] // ahead
    public void ARocketAimsAtATarget5Point6DegreesOffTheFacing(int y, uint angle)
    {
        World world = OneRoomMap().Load();
        mobj_t player = Player(world);
        mobj_t zombie = world.P_SpawnMobj(F(364), F(y), World.ONFLOORZ, mobjtype_t.MT_POSSESSED);
        mobj_t rocket = world.P_SpawnPlayerMissile(player, mobjtype_t.MT_ROCKET);
        Assert.Same(zombie, world.linetarget);
        Assert.Equal(angle, rocket.angle);
        int slope = world.P_AimLineAttack(player, angle, 16 * 64 * FRACUNIT);
        Assert.NotEqual(0, slope); // the zombie's middle is below the shooter's aim height
        Assert.Equal(Fixed.FixedMul(F(20), slope), rocket.momz);
    }

    [Fact]
    public void ATargetFartherThan1024UnitsIsNotAimedAt()
    {
        World world = TestMap.Strip(0, 0, 256, new TestMap.Room(1400, 0, 128)).Player(64, 128).Load();
        world.P_SpawnMobj(F(1200), F(140), World.ONFLOORZ, mobjtype_t.MT_POSSESSED);
        mobj_t rocket = world.P_SpawnPlayerMissile(Player(world), mobjtype_t.MT_ROCKET);
        Assert.Null(world.linetarget);
        Assert.Equal(0u, rocket.angle);
        Assert.Equal(0, rocket.momz);
    }

    [Fact]
    public void AMissileSpawnedInAWallExplodesAtOnce()
    {
        World world = OneRoomMap().Load();
        RouteStart.Place(world, 20, 128, 180); // the west wall 20 units behind the facing
        mobj_t rocket = world.P_SpawnPlayerMissile(Player(world), mobjtype_t.MT_ROCKET);
        Assert.Equal(statenum_t.S_EXPLODE1, rocket.state);
        Assert.Equal((mobjflag_t)0, rocket.flags & mobjflag_t.MF_MISSILE);
        Assert.Equal(0, rocket.momx);
        Assert.True(Player(world).health < 100); // the blast (A_Explode) at point-blank range
    }

    [Fact]
    public void FireMissileUsesARocket()
    {
        World world = OneRoomMap().Load();
        player_t player = world.players[0];
        player.readyweapon = weapontype_t.wp_missile;
        player.ammo[(int)ammotype_t.am_misl] = 5;
        world.A_FireMissile(player, new pspdef_t());
        Assert.Equal(4, player.ammo[(int)ammotype_t.am_misl]);
        Assert.Single(world.Mobjs(), m => m.type == mobjtype_t.MT_ROCKET);
    }

    // ---- the sky hack ----

    [Fact]
    public void ARocketVanishesOnASkyWallBehindTheLine()
    {
        // From x 136 the rocket's steps go 126, 106, 86 and 66, still in S: W | S lowers the
        // ceiling to W's 36 (ceilingline), the rocket's top is at 40, and W, behind the line, is
        // sky: the move to 66 fails and the rocket is removed at 86.
        World world = SkyWalls(136, 180).Load();
        mobj_t rocket = world.P_SpawnPlayerMissile(Player(world), mobjtype_t.MT_ROCKET);
        for (int i = 0; i < 10 && !Removed(rocket); i++)
        {
            Tics(world, 1);
            Assert.Equal(statenum_t.S_ROCKET, rocket.state); // never explodes
        }
        Assert.True(Removed(rocket));
        Assert.Equal(86, rocket.x >> 16);
        Assert.Equal(100, Player(world).health);
    }

    [Fact]
    public void ARocketExplodesOnASkyWallWhoseFrontIsSky()
    {
        // S | E: the line's back is S (no sky): vanilla reads only the back sector.
        World world = SkyWalls(400, 0).Load();
        mobj_t rocket = world.P_SpawnPlayerMissile(Player(world), mobjtype_t.MT_ROCKET);
        Tics(world, 10);
        Assert.False(Removed(rocket));
        Assert.True(rocket.state >= statenum_t.S_EXPLODE1);
        Assert.InRange(rocket.x >> 16, 576 - 11 - 20, 576 - 11);
    }

    [Fact]
    public void ARocketWhoseCentreCrossesIntoTheSkySectorExplodes()
    {
        // From x 290 the 20-unit steps go from 80 to 60, past the line: at 60 the rocket is in
        // W, whose ceiling (36) is the lowest it touches, so no line lowered it (no ceilingline).
        World world = SkyWalls(290, 180).Load();
        mobj_t rocket = world.P_SpawnPlayerMissile(Player(world), mobjtype_t.MT_ROCKET);
        Tics(world, 12);
        Assert.False(Removed(rocket));
        Assert.True(rocket.state >= statenum_t.S_EXPLODE1);
        Assert.Equal(80, rocket.x >> 16);
    }

    [Fact]
    public void AFireballVanishesOnASkyWallBehindTheLine()
    {
        // 10-unit steps always stop with the centre in S (the ball's radius is 6).
        World world = SkyWalls(136, 0).Thing(300, 256, 3001, 180).Load();
        mobj_t imp = world.Mobjs().Single(m => m.type == mobjtype_t.MT_TROOP);
        RouteStart.Place(world, 300, 64, 90); // out of the way
        mobj_t dummy = world.P_SpawnMobj(F(100), F(256), World.ONFLOORZ, mobjtype_t.MT_POSSESSED);
        mobj_t ball = world.P_SpawnMissile(imp, dummy, mobjtype_t.MT_TROOPSHOT);
        world.P_RemoveMobj(dummy); // the ball flies on west
        for (int i = 0; i < 40 && !Removed(ball); i++)
        {
            Tics(world, 1);
            Assert.True(ball.state is statenum_t.S_TBALL1 or statenum_t.S_TBALL2);
        }
        Assert.True(Removed(ball));
    }

    [Fact]
    public void AMissileHittingASkyCeilingExplodes()
    {
        // Vanilla's hack is for walls only: P_ZMovement explodes a missile on any ceiling.
        World world = OneRoomMap().CeilingPic(0, "F_SKY1").Load();
        mobj_t rocket = world.P_SpawnMobj(F(300), F(128), F(100), mobjtype_t.MT_ROCKET);
        rocket.momz = F(20);
        Tics(world, 2);
        Assert.False(Removed(rocket));
        Assert.True(rocket.state >= statenum_t.S_EXPLODE1);
        Assert.Equal(F(128) - rocket.height, rocket.z);
    }

    // ---- PIT_CheckThing ----

    [Fact]
    public void ARocketHitsAMonsterAndItsBlastHurtsAroundIt()
    {
        World world = OneRoomMap().Thing(300, 128, 3004, 180).Thing(300, 180, 2035).Load();
        mobj_t zombie = world.Mobjs().Single(m => m.type == mobjtype_t.MT_POSSESSED);
        mobj_t barrel = world.Mobjs().Single(m => m.type == mobjtype_t.MT_BARREL);
        mobj_t rocket = world.P_SpawnPlayerMissile(Player(world), mobjtype_t.MT_ROCKET);
        for (int i = 0; i < 15 && rocket.state == statenum_t.S_ROCKET; i++)
            Tics(world, 1);
        Assert.Equal(statenum_t.S_EXPLODE1, rocket.state);
        Assert.True(zombie.health <= 0); // (1-8) × 20 + the blast: at least 20
        Assert.True(barrel.health < 20); // the blast (A_Explode) reaches it
        Assert.InRange(rocket.x >> 16, 300 - 20 - 11 - 20, 300 - 20 - 11);
        Assert.Equal(100, Player(world).health);
    }

    [Fact]
    public void ABallExplodesOnItsOwnSpeciesWithoutHurtingIt()
    {
        World world = OneRoomMap().Load();
        mobj_t Spawn(int x, int y, mobjtype_t type) => world.P_SpawnMobj(F(x), F(y), World.ONFLOORZ, type);
        mobj_t imp = Spawn(400, 128, mobjtype_t.MT_TROOP);
        mobj_t other = Spawn(250, 128, mobjtype_t.MT_TROOP);
        mobj_t baron = Spawn(450, 200, mobjtype_t.MT_BRUISER);
        mobj_t knight = Spawn(300, 172, mobjtype_t.MT_KNIGHT);
        mobj_t fireball = world.P_SpawnMissile(imp, Player(world), mobjtype_t.MT_TROOPSHOT);
        mobj_t ball = world.P_SpawnMissile(baron, Player(world), mobjtype_t.MT_BRUISERSHOT);
        Tics(world, 25);
        Assert.True(fireball.state >= statenum_t.S_TBALLX1 || Removed(fireball));
        Assert.True(ball.state >= statenum_t.S_BRBALLX1 || Removed(ball));
        Assert.True(fireball.x > F(250)); // stopped at the other imp
        Assert.True(ball.x > F(300)); // and at the knight (a baron's kin)
        Assert.Equal(60, other.health);
        Assert.Equal(500, knight.health);
        Assert.Equal(100, Player(world).health);
    }

    [Fact]
    public void AMissileFliesThroughItsShooterAndNonSolidThingsAndOverDecorations()
    {
        // A floor lamp is solid, but as every decoration only 16 units high (info.c): the rocket
        // flies over it at 32 and explodes on the east wall.
        World world = OneRoomMap().Thing(150, 128, 2014).Thing(300, 128, 2028).Load();
        mobj_t bonus = world.Mobjs().Single(m => m.type != mobjtype_t.MT_PLAYER && (m.flags & mobjflag_t.MF_SPECIAL) != 0);
        mobj_t lamp = world.Mobjs().Single(m => m.type != mobjtype_t.MT_PLAYER && (m.flags & mobjflag_t.MF_SOLID) != 0);
        Assert.Equal(F(16), lamp.height);
        mobj_t rocket = world.P_SpawnPlayerMissile(Player(world), mobjtype_t.MT_ROCKET); // starts inside the player
        Assert.Equal(statenum_t.S_ROCKET, rocket.state);
        for (int i = 0; i < 30 && rocket.state == statenum_t.S_ROCKET; i++)
            Tics(world, 1);
        Assert.False(Removed(bonus));
        Assert.Equal(statenum_t.S_EXPLODE1, rocket.state);
        Assert.InRange(rocket.x >> 16, 512 - 11 - 20, 512 - 11);
    }

    [Fact]
    public void AMissileFliesOverAThingBelowIt()
    {
        // From a 64-unit ledge the rocket flies at 96, over a zombie (56 high) below.
        World world = TestMap.Strip(0, 0, 256, new TestMap.Room(128, 64, 256), new TestMap.Room(384, 0, 256))
            .Player(64, 128).Thing(300, 40, 3004, 90).Load();
        mobj_t zombie = world.Mobjs().Single(m => m.type == mobjtype_t.MT_POSSESSED);
        mobj_t rocket = world.P_SpawnMobj(F(200), F(40), F(96), mobjtype_t.MT_ROCKET);
        rocket.target = Player(world);
        rocket.momx = F(20);
        Tics(world, 8);
        Assert.Equal(20, zombie.health);
        Assert.True(rocket.x > zombie.x);
    }

    // ---- the route event ----

    [Fact]
    public void RoutesParseAndRunTheRocketEvent()
    {
        RouteFile route = RouteFile.Parse("iwad synthetic\nrocket\n0 0 0 0\n".Split('\n'), "test.route");
        Assert.Equal(new[] { new RouteEvent(0, RouteEventKind.Rocket, 0, 0, 0) }, route.Events);
        Assert.Equal("rocket", route.Events[0].ToString());
        Assert.Throws<FormatException>(() => RouteFile.Parse(new[] { "iwad synthetic", "rocket 1", "0 0 0 0" }, "x"));

        World world = OneRoomMap().Load();
        route.RunEvents(world, 0);
        mobj_t rocket = world.Mobjs().Single(m => m.type == mobjtype_t.MT_ROCKET);
        Assert.Same(Player(world), rocket.target);
        Assert.Equal(0, world.players[0].ammo[(int)ammotype_t.am_misl]); // no ammo used (none held)
    }
}
