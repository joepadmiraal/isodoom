using System;
using System.IO;
using System.Linq;
using IsoDoom.Game;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T6.4: p_enemy.c's monster movement and decisions (<c>P_Move</c>,
/// <c>P_TryWalk</c>, <c>P_NewChaseDir</c>, <c>P_CheckMeleeRange</c>,
/// <c>P_CheckMissileRange</c>, <c>A_Look</c>, <c>A_Chase</c>,
/// <c>A_FaceTarget</c>), the shareware monsters' attacks, <c>A_Explode</c>,
/// <c>A_BossDeath</c>, <c>P_SpawnMissile</c>, the fast monsters and the
/// route events, on synthetic <see cref="TestMap"/>s. The vanilla routes
/// with monsters (<c>e1m1-monsters</c>, <c>e1m2-monsters</c>,
/// <c>e1m8-barons</c>, <c>e1m8-demons</c>, <c>e1m8-nightmare</c>,
/// <c>synthetic-monsters</c>; <see cref="VanillaRouteTests"/>) check the
/// same against vanilla tic by tic.
/// </summary>
public class MonsterAiTests
{
    private const int FRACUNIT = 1 << 16;

    private static int F(int units) => units * FRACUNIT;

    private static mobj_t Player(World world) => world.players[0].mo!;

    private static mobj_t Spawn(World world, int x, int y, mobjtype_t type) =>
        world.P_SpawnMobj(F(x), F(y), World.ONFLOORZ, type);

    /// <summary>Makes the next <c>P_Random</c> return <c>rndtable[index + 1]</c> (1: 8, 2: 109, 3: 220, 4: 0).</summary>
    private static void NextRandom(World world, int index) => world.random.prndindex = index - 1;

    /// <summary>One room, x 0..512, y 0..256, floor 0, ceiling 128; the player at (64, 128) facing east.</summary>
    private static TestMap OneRoomMap() => TestMap.Strip(0, 0, 256, new TestMap.Room(512, 0, 128)).Player(64, 128);

    private static World OneRoom() => OneRoomMap().Load();

    /// <summary>Loads <paramref name="map"/> with other game settings (mode, -fast, -respawn).</summary>
    private static World Load(TestMap map, SpawnSettings settings)
    {
        var wad = new WadArchive(new[] { WadFile.FromBytes(map.Build(), "testmap.wad") });
        var world = new World(settings, Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(wad, "E1M1"));
        return world;
    }

    // ---- P_Move, P_TryWalk, P_NewChaseDir ----

    [Fact]
    public void ANewChaseDirTakesTheDiagonalTowardsTheTarget()
    {
        World world = OneRoom();
        mobj_t zombie = Spawn(world, 300, 200, mobjtype_t.MT_POSSESSED);
        zombie.target = Player(world);
        zombie.movedir = (int)dirtype_t.DI_NODIR;

        world.P_NewChaseDir(zombie);

        Assert.Equal((int)dirtype_t.DI_SOUTHWEST, zombie.movedir); // the player is west and south
        Assert.Equal(F(300) - 8 * 47000, zombie.x); // one step at speed 8
        Assert.Equal(F(200) - 8 * 47000, zombie.y);
        Assert.InRange(zombie.movecount, 0, 15);
    }

    [Fact]
    public void ANewChaseDirNeverTurnsAroundWhileAnotherWayIsOpen()
    {
        World world = OneRoom();
        mobj_t zombie = Spawn(world, 300, 128, mobjtype_t.MT_POSSESSED);
        zombie.target = Player(world); // straight west
        zombie.movedir = (int)dirtype_t.DI_EAST; // turning around would be west
        world.P_NewChaseDir(zombie);
        Assert.NotEqual((int)dirtype_t.DI_WEST, zombie.movedir);
        Assert.NotEqual((int)dirtype_t.DI_NODIR, zombie.movedir);
    }

    [Fact]
    public void AMoveIntoAWallFails()
    {
        World world = OneRoom();
        mobj_t zombie = Spawn(world, 300, 256 - 22, mobjtype_t.MT_POSSESSED);
        zombie.movedir = (int)dirtype_t.DI_NORTH;
        Assert.False(world.P_Move(zombie));
        Assert.Equal((F(300), F(234)), (zombie.x, zombie.y));
        Assert.Equal((int)dirtype_t.DI_NORTH, zombie.movedir); // no special line: the direction stays

        zombie.movedir = (int)dirtype_t.DI_NODIR;
        Assert.False(world.P_Move(zombie));
    }

    [Fact]
    public void AMonsterOpensAManualDoorInItsWay()
    {
        // A (0..256), the closed door D (256..288), B (288..544); the boundary D | B (B in front) is a DR door (1).
        TestMap map = TestMap.Strip(0, 0, 256, new TestMap.Room(256, 0, 128), new TestMap.Room(32, 0, 0), new TestMap.Room(256, 0, 128))
            .Player(64, 128);
        World world = map.Special(map.Boundaries[2], 1).Load();
        mobj_t zombie = Spawn(world, 288 + 21, 128, mobjtype_t.MT_POSSESSED);
        zombie.movedir = (int)dirtype_t.DI_WEST;

        Assert.True(world.P_Move(zombie)); // blocked, but the door starts opening
        Assert.Equal((int)dirtype_t.DI_NODIR, zombie.movedir);
        Assert.IsType<vldoor_t>(world.sectors[1].specialdata);
        Assert.Equal(F(309), zombie.x);
    }

    [Fact]
    public void AMonsterDoesNotOpenASecretDoor()
    {
        TestMap map = TestMap.Strip(0, 0, 256, new TestMap.Room(256, 0, 128), new TestMap.Room(32, 0, 0), new TestMap.Room(256, 0, 128))
            .Player(64, 128);
        World world = map.Special(map.Boundaries[2], 1).Flags(map.Boundaries[2], Line.ML_TWOSIDED | Line.ML_SECRET).Load();
        mobj_t zombie = Spawn(world, 288 + 21, 128, mobjtype_t.MT_POSSESSED);
        zombie.movedir = (int)dirtype_t.DI_WEST;
        Assert.False(world.P_Move(zombie));
        Assert.Null(world.sectors[1].specialdata);
    }

    [Fact]
    public void ATryWalkKeepsTheDirectionForUpTo15Steps()
    {
        World world = OneRoom();
        mobj_t zombie = Spawn(world, 300, 128, mobjtype_t.MT_POSSESSED);
        zombie.movedir = (int)dirtype_t.DI_EAST;
        NextRandom(world, 2); // 109 & 15 = 13
        Assert.True(world.P_TryWalk(zombie));
        Assert.Equal(13, zombie.movecount);
        Assert.Equal(F(308), zombie.x);
    }

    // ---- P_CheckMeleeRange, P_CheckMissileRange ----

    [Theory]
    [InlineData(64 + 59, true)]  // under MELEERANGE - 20 + the player's radius 16 = 60
    [InlineData(64 + 60, false)]
    public void TheMeleeRangeIs44UnitsPlusTheTargetsRadius(int x, bool inRange)
    {
        World world = OneRoom();
        mobj_t imp = Spawn(world, x, 128, mobjtype_t.MT_TROOP);
        Assert.False(world.P_CheckMeleeRange(imp)); // no target
        imp.target = Player(world);
        Assert.Equal(inRange, world.P_CheckMeleeRange(imp));
    }

    [Fact]
    public void NothingIsInMeleeRangeOutOfSight()
    {
        // A wall (a closed door) between the imp and the player.
        World world = TestMap.Strip(0, 0, 256, new TestMap.Room(100, 0, 128), new TestMap.Room(8, 0, 0), new TestMap.Room(256, 0, 128))
            .Player(80, 128).Load();
        mobj_t imp = Spawn(world, 130, 128, mobjtype_t.MT_TROOP);
        imp.target = Player(world);
        Assert.False(world.P_CheckMeleeRange(imp));
    }

    [Fact]
    public void AHurtMonsterFiresBackAtOnceAndAnAwakeOneByChance()
    {
        World world = OneRoom();
        mobj_t zombie = Spawn(world, 448, 128, mobjtype_t.MT_POSSESSED);
        zombie.target = Player(world);

        // not yet awake (reactiontime 8): only when just hit
        Assert.False(world.P_CheckMissileRange(zombie));
        zombie.flags |= mobjflag_t.MF_JUSTHIT;
        Assert.True(world.P_CheckMissileRange(zombie));
        Assert.True((zombie.flags & mobjflag_t.MF_JUSTHIT) == 0);

        // awake: 384 units, less 64, less 128 more without a melee attack: 192; P_Random must not be below it
        zombie.reactiontime = 0;
        NextRandom(world, 2); // 109 < 192
        Assert.False(world.P_CheckMissileRange(zombie));
        NextRandom(world, 3); // 220
        Assert.True(world.P_CheckMissileRange(zombie));
    }

    // ---- A_Look, A_Chase, A_FaceTarget ----

    [Fact]
    public void AMonsterWakesWhenItSeesThePlayerInFront()
    {
        World world = OneRoom();
        mobj_t zombie = Spawn(world, 448, 128, mobjtype_t.MT_POSSESSED);
        zombie.lastlook = 0; // (from 1 vanilla's loop never looks at player 0 alone: SightSoundTests)
        zombie.angle = 0; // facing east, away from the player
        world.A_Look(zombie);
        Assert.Equal(statenum_t.S_POSS_STND, zombie.state);
        Assert.Null(zombie.target);

        zombie.angle = Tables.ANG180; // facing the player
        world.events.Clear();
        world.A_Look(zombie);
        Assert.Same(Player(world), zombie.target);
        Assert.NotEqual(statenum_t.S_POSS_STND, zombie.state); // its see state, whose A_Chase ran
        sound_event_t see = Assert.Single(world.StartedSounds(), s => s.sfx is >= sfxenum_t.sfx_posit1 and <= sfxenum_t.sfx_posit3);
        Assert.Same(zombie, see.origin);
    }

    [Fact]
    public void ANoiseWakesAMonsterUnlessItIsDeafAndBlind()
    {
        World world = OneRoom();
        mobj_t zombie = Spawn(world, 448, 128, mobjtype_t.MT_POSSESSED);
        mobj_t deaf = Spawn(world, 448, 64, mobjtype_t.MT_POSSESSED);
        zombie.angle = deaf.angle = 0; // facing away
        deaf.flags |= mobjflag_t.MF_AMBUSH;
        world.P_NoiseAlert(Player(world), Player(world));

        world.A_Look(zombie);
        Assert.Same(Player(world), zombie.target);
        Assert.NotEqual(statenum_t.S_POSS_STND, zombie.state);

        // the deaf one wakes to the noise because it can see the player too, whichever way it faces
        world.A_Look(deaf);
        Assert.Same(Player(world), deaf.target);
        Assert.NotEqual(statenum_t.S_POSS_STND, deaf.state);
    }

    [Fact]
    public void ADeafMonsterBehindAWallSleepsThroughTheNoise()
    {
        // The player in A, the monster in B behind a high step that hides it, the sound passes over the step.
        World world = TestMap.Strip(0, 0, 256, new TestMap.Room(256, 0, 128), new TestMap.Room(32, 100, 128), new TestMap.Room(256, 0, 128))
            .Player(64, 128).Load();
        mobj_t deaf = Spawn(world, 448, 128, mobjtype_t.MT_POSSESSED);
        deaf.flags |= mobjflag_t.MF_AMBUSH;
        deaf.angle = Tables.ANG180;
        world.P_NoiseAlert(Player(world), Player(world));
        Assert.Same(Player(world), deaf.subsector.sector.soundtarget);
        world.A_Look(deaf);
        Assert.Equal(statenum_t.S_POSS_STND, deaf.state);
    }

    [Fact]
    public void ChasingTurns45DegreesATicTowardsTheMoveDirection()
    {
        World world = OneRoom();
        mobj_t zombie = Spawn(world, 448, 128, mobjtype_t.MT_POSSESSED);
        zombie.target = Player(world);
        zombie.reactiontime = 5;
        zombie.angle = 0;
        zombie.movedir = (int)dirtype_t.DI_NORTH;
        zombie.movecount = 5;
        world.A_Chase(zombie);
        Assert.Equal(Tables.ANG45, zombie.angle);
        Assert.Equal(4, zombie.reactiontime);
    }

    [Fact]
    public void WithoutATargetAChasingMonsterLooksAllAroundOrGoesBackToSleep()
    {
        World world = OneRoom();
        mobj_t zombie = Spawn(world, 448, 128, mobjtype_t.MT_POSSESSED);
        zombie.lastlook = 0;
        zombie.angle = 0; // the player behind it: seen anyway (all around)
        world.P_SetMobjState(zombie, statenum_t.S_POSS_RUN1);
        Assert.Same(Player(world), zombie.target);

        // a dead player: back to the spawn state
        mobj_t other = Spawn(world, 300, 64, mobjtype_t.MT_POSSESSED);
        world.players[0].health = 0;
        world.P_SetMobjState(other, statenum_t.S_POSS_RUN1);
        Assert.Null(other.target);
        Assert.Equal(statenum_t.S_POSS_STND, other.state);
    }

    [Fact]
    public void AnImpScratchesInMeleeRangeAndThrowsAFireballFarther()
    {
        World world = OneRoom();
        mobj_t me = Player(world);
        mobj_t imp = Spawn(world, 64 + 50, 128, mobjtype_t.MT_TROOP);
        imp.target = me;
        world.A_TroopAttack(imp);
        Assert.InRange(world.players[0].health, 100 - 24, 100 - 3);
        Assert.InRange(imp.angle, Tables.ANG180 - 2, Tables.ANG180); // faces the target (R_PointToAngle2's octant 3: ANG180 - 1)
        Assert.DoesNotContain(world.Mobjs(), m => m.type == mobjtype_t.MT_TROOPSHOT);

        world.PlaceMobj(imp, F(400), F(128));
        world.A_TroopAttack(imp);
        mobj_t ball = Assert.Single(world.Mobjs(), m => m.type == mobjtype_t.MT_TROOPSHOT);
        Assert.Same(imp, ball.target);
    }

    [Fact]
    public void AZombiemanShootsOnceAndAShotgunGuyThreeTimes()
    {
        World world = OneRoom();
        mobj_t zombie = Spawn(world, 300, 128, mobjtype_t.MT_POSSESSED);
        zombie.target = Player(world);
        int before = world.random.prndindex;
        world.A_PosAttack(zombie);
        Assert.Contains(world.StartedSounds(), s => s.sfx == sfxenum_t.sfx_pistol && s.origin == zombie);
        int one = world.random.prndindex - before;

        mobj_t shotguy = Spawn(world, 300, 64, mobjtype_t.MT_SHOTGUY);
        shotguy.target = Player(world);
        world.players[0].health = 100;
        before = world.random.prndindex;
        world.A_SPosAttack(shotguy);
        Assert.Contains(world.StartedSounds(), s => s.sfx == sfxenum_t.sfx_shotgn && s.origin == shotguy);
        // the spread and damage of each pellet: 3 P_Randoms each (plus any blood or puff)
        Assert.True(world.random.prndindex - before >= 9, $"{world.random.prndindex - before} P_Randoms");
        Assert.True(one >= 3);
    }

    [Fact]
    public void ADemonBitesOnlyInMeleeRange()
    {
        World world = OneRoom();
        mobj_t demon = Spawn(world, 300, 128, mobjtype_t.MT_SERGEANT);
        demon.target = Player(world);
        world.A_SargAttack(demon);
        Assert.Equal(100, world.players[0].health);
        world.PlaceMobj(demon, F(64 + 50), F(128));
        world.A_SargAttack(demon);
        Assert.InRange(world.players[0].health, 100 - 40, 100 - 4);
    }

    [Fact]
    public void ABaronClawsInMeleeRangeAndThrowsABallFarther()
    {
        World world = OneRoom();
        mobj_t baron = Spawn(world, 64 + 50, 128, mobjtype_t.MT_BRUISER);
        baron.target = Player(world);
        world.players[0].mo!.health = world.players[0].health = 200; // survives the claw (10-80)
        world.A_BruisAttack(baron);
        Assert.InRange(world.players[0].health, 200 - 80, 200 - 10);

        world.PlaceMobj(baron, F(400), F(128));
        world.A_BruisAttack(baron);
        Assert.Single(world.Mobjs(), m => m.type == mobjtype_t.MT_BRUISERSHOT);
    }

    [Fact]
    public void FacingAFuzzyTargetIsOffByUpTo45Degrees()
    {
        World world = OneRoom();
        mobj_t zombie = Spawn(world, 300, 128, mobjtype_t.MT_POSSESSED);
        zombie.target = Player(world);
        zombie.flags |= mobjflag_t.MF_AMBUSH;
        world.A_FaceTarget(zombie);
        uint facing = zombie.angle;
        Assert.InRange(facing, Tables.ANG180 - 2, Tables.ANG180);
        Assert.True((zombie.flags & mobjflag_t.MF_AMBUSH) == 0);

        Player(world).flags |= mobjflag_t.MF_SHADOW;
        NextRandom(world, 2); // 109 - 220
        world.A_FaceTarget(zombie);
        Assert.Equal(unchecked(facing + (uint)((109 - 220) << 21)), zombie.angle);
    }

    // ---- P_SpawnMissile and the fast monsters ----

    [Fact]
    public void AMissileFliesAtItsTargetFrom32UnitsUp()
    {
        World world = OneRoom();
        mobj_t imp = Spawn(world, 400, 128, mobjtype_t.MT_TROOP);
        NextRandom(world, 0); // P_SpawnMobj's lastlook 0, then 8 & 3 = 0: no tics off
        mobj_t ball = world.P_SpawnMissile(imp, Player(world), mobjtype_t.MT_TROOPSHOT);
        Assert.InRange(ball.angle, Tables.ANG180 - 2, Tables.ANG180);
        Assert.Equal(Fixed.FixedMul(10 * FRACUNIT, Tables.finecosine[(int)(ball.angle >> Tables.ANGLETOFINESHIFT)]), ball.momx); // 10 units a tic
        Assert.InRange(ball.momx, -10 * FRACUNIT, -10 * FRACUNIT + 16);
        Assert.Equal(0, ball.momz); // same heights
        Assert.Equal(F(400) + (ball.momx >> 1), ball.x); // half a step on
        Assert.Equal(F(32), ball.z);
        Assert.Equal(Info.states[(int)ball.info.spawnstate].tics, ball.tics);
        Assert.Contains(world.StartedSounds(), s => s.sfx == sfxenum_t.sfx_firsht && s.origin == ball);
    }

    [Theory]
    [InlineData(skill_t.sk_hard, false, false)]
    [InlineData(skill_t.sk_nightmare, false, true)]
    [InlineData(skill_t.sk_medium, true, true)] // -fast
    public void FastMonstersHaveShortDemonStatesAndFastBalls(skill_t skill, bool fastparm, bool fast)
    {
        World world = Load(OneRoomMap(), new SpawnSettings(GameMode.shareware, skill, fastparm: fastparm));
        World normal = OneRoom(); // side by side: the shared tables stay as they are
        Assert.Equal(fast, world.fastmonsters);
        Assert.Equal(fast ? 1 : 2, world.StateTics(statenum_t.S_SARG_RUN1));
        Assert.Equal(fast ? 4 : 8, world.StateTics(statenum_t.S_SARG_ATK1));
        Assert.Equal(fast ? 1 : 2, world.StateTics(statenum_t.S_SARG_PAIN2));
        Assert.Equal(10, world.StateTics(statenum_t.S_SARG_STND)); // outside the range
        Assert.Equal(3, world.StateTics(statenum_t.S_TROO_RUN1)); // other monsters
        Assert.Equal(fast ? 20 : 10, world.MissileSpeed(mobjtype_t.MT_TROOPSHOT) >> 16);
        Assert.Equal(fast ? 20 : 15, world.MissileSpeed(mobjtype_t.MT_BRUISERSHOT) >> 16);
        Assert.Equal(fast ? 20 : 10, world.MissileSpeed(mobjtype_t.MT_HEADSHOT) >> 16);
        Assert.Equal(20, world.MissileSpeed(mobjtype_t.MT_ROCKET) >> 16);
        Assert.Equal(2, normal.StateTics(statenum_t.S_SARG_RUN1));
        Assert.Equal(2, Info.states[(int)statenum_t.S_SARG_RUN1].tics);

        mobj_t demon = Spawn(world, 300, 128, mobjtype_t.MT_SERGEANT);
        world.P_SetMobjState(demon, statenum_t.S_SARG_ATK1);
        Assert.Equal(fast ? 4 : 8, demon.tics);

        mobj_t imp = Spawn(world, 400, 128, mobjtype_t.MT_TROOP);
        mobj_t ball = world.P_SpawnMissile(imp, Player(world), mobjtype_t.MT_TROOPSHOT);
        Assert.InRange(ball.momx, (fast ? -20 : -10) * FRACUNIT, (fast ? -20 : -10) * FRACUNIT + 32);
    }

    [Fact]
    public void FastMonstersAttackWithoutPausingBetweenSteps()
    {
        foreach (bool fast in new[] { false, true })
        {
            World world = Load(OneRoomMap(), new SpawnSettings(GameMode.shareware, skill_t.sk_hard, fastparm: fast));
            mobj_t zombie = Spawn(world, 300, 128, mobjtype_t.MT_POSSESSED);
            zombie.target = Player(world);
            zombie.reactiontime = 0;
            zombie.movecount = 5;
            zombie.flags |= mobjflag_t.MF_JUSTHIT;
            world.P_SetMobjState(zombie, statenum_t.S_POSS_RUN1);
            // steps left: only a fast one fires (just hit, so it fires for sure)
            Assert.Equal(fast, zombie.state == statenum_t.S_POSS_ATK1);
        }
    }

    [Fact]
    public void RespawnparmRespawnsBelowNightmare()
    {
        Assert.True(Load(OneRoomMap(), new SpawnSettings(GameMode.shareware, skill_t.sk_medium, respawnparm: true)).respawnmonsters);
        Assert.False(OneRoom().respawnmonsters);
        Assert.True(Load(OneRoomMap(), new SpawnSettings(GameMode.shareware, skill_t.sk_nightmare)).respawnmonsters);
    }

    [Fact]
    public void NomonstersSpawnsNone()
    {
        TestMap map = OneRoomMap().Thing(300, 128, 3004).Thing(300, 64, 3001).Thing(200, 200, 2035);
        Assert.Equal(2, map.Load().Mobjs().Count(m => (m.flags & mobjflag_t.MF_COUNTKILL) != 0));
        World none = Load(map, new SpawnSettings(GameMode.shareware, skill_t.sk_medium, nomonsters: true));
        Assert.DoesNotContain(none.Mobjs(), m => (m.flags & mobjflag_t.MF_COUNTKILL) != 0);
        Assert.Single(none.Mobjs(), m => m.type == mobjtype_t.MT_BARREL);
    }

    // ---- A_Explode, A_Scream, A_XScream ----

    [Fact]
    public void AShotBarrelExplodesAndKillsAMonsterNextToIt()
    {
        World world = OneRoom();
        mobj_t barrel = Spawn(world, 300, 128, mobjtype_t.MT_BARREL);
        mobj_t zombie = Spawn(world, 340, 128, mobjtype_t.MT_POSSESSED); // 40 - 20 = 20 units: 108 damage
        world.P_DamageMobj(barrel, Player(world), Player(world), 10);
        world.P_DamageMobj(barrel, Player(world), Player(world), 10);
        Assert.Same(Player(world), barrel.target); // from the first shot: the explosion is the player's (P_RadiusAttack's source)
        for (int i = 0; i < 20 && zombie.health > 0; i++)
            world.G_Ticker(default(ticcmd_t));
        Assert.True(zombie.health <= 0);
        Assert.Equal(1, world.players[0].killcount); // credited to the player who shot the barrel
    }

    [Fact]
    public void DeathsMakeTheirSounds()
    {
        World world = OneRoom();
        mobj_t zombie = Spawn(world, 300, 128, mobjtype_t.MT_POSSESSED);
        world.A_Scream(zombie);
        Assert.Contains(world.StartedSounds(), s => s.sfx is >= sfxenum_t.sfx_podth1 and <= sfxenum_t.sfx_podth3 && s.origin == zombie);
        world.A_XScream(zombie);
        Assert.Contains(world.StartedSounds(), s => s.sfx == sfxenum_t.sfx_slop && s.origin == zombie);
        mobj_t imp = Spawn(world, 300, 64, mobjtype_t.MT_TROOP);
        world.A_Scream(imp);
        Assert.Contains(world.StartedSounds(), s => s.sfx is sfxenum_t.sfx_bgdth1 or sfxenum_t.sfx_bgdth2 && s.origin == imp);
    }

    // ---- A_BossDeath ----

    /// <summary>One room with a raised sector (tag 666) east of it: ExM8's pillar.</summary>
    private static TestMap BossMap() =>
        TestMap.Strip(0, 0, 256, new TestMap.Room(512, 0, 128), new TestMap.Room(128, 64, 128)).SectorTag(1, 666).Player(64, 128);

    private static World BossWorld(GameMode mode, int episode, int map)
    {
        World world = Load(BossMap(), new SpawnSettings(mode, skill_t.sk_medium));
        world.gameepisode = episode;
        world.gamemap = map;
        return world;
    }

    private static void Kill(World world, mobj_t mo)
    {
        mo.health = 0;
        world.A_BossDeath(mo);
    }

    [Fact]
    public void E1M8LowersTag666WhenTheLastBaronDies()
    {
        World world = BossWorld(GameMode.shareware, 1, 8);
        mobj_t a = Spawn(world, 300, 128, mobjtype_t.MT_BRUISER);
        mobj_t b = Spawn(world, 300, 64, mobjtype_t.MT_BRUISER);
        Kill(world, a);
        Assert.Null(world.sectors[1].specialdata); // the other is alive
        Kill(world, b);
        floormove_t floor = Assert.IsType<floormove_t>(world.sectors[1].specialdata);
        Assert.Equal(floor_e.lowerFloorToLowest, floor.type);
        Assert.Equal(0, floor.floordestheight);
        Assert.Equal(gameaction_t.ga_nothing, world.gameaction);
    }

    [Fact]
    public void NoBossSpecialWithoutALivePlayer()
    {
        World world = BossWorld(GameMode.shareware, 1, 8);
        mobj_t a = Spawn(world, 300, 128, mobjtype_t.MT_BRUISER);
        world.players[0].health = 0;
        Kill(world, a);
        Assert.Null(world.sectors[1].specialdata);
    }

    [Theory]
    [InlineData(GameMode.shareware, 1, 1, mobjtype_t.MT_BRUISER, "nothing")] // not map 8
    [InlineData(GameMode.registered, 2, 8, mobjtype_t.MT_BRUISER, "nothing")] // barons only in episode 1
    [InlineData(GameMode.registered, 2, 8, mobjtype_t.MT_CYBORG, "exit")]
    [InlineData(GameMode.registered, 3, 8, mobjtype_t.MT_CYBORG, "exit")] // before Ultimate: any boss on map 8
    [InlineData(GameMode.retail, 3, 8, mobjtype_t.MT_CYBORG, "nothing")] // Ultimate: each episode's own boss
    [InlineData(GameMode.retail, 3, 8, mobjtype_t.MT_SPIDER, "exit")]
    [InlineData(GameMode.retail, 1, 8, mobjtype_t.MT_BRUISER, "floor")]
    [InlineData(GameMode.retail, 4, 6, mobjtype_t.MT_CYBORG, "door")]
    [InlineData(GameMode.retail, 4, 8, mobjtype_t.MT_SPIDER, "floor")]
    [InlineData(GameMode.commercial, 1, 7, mobjtype_t.MT_FATSO, "floor")]
    [InlineData(GameMode.commercial, 1, 7, mobjtype_t.MT_BRUISER, "nothing")]
    [InlineData(GameMode.commercial, 1, 8, mobjtype_t.MT_FATSO, "nothing")]
    public void TheBossSpecials(GameMode mode, int episode, int map, mobjtype_t boss, string what)
    {
        World world = BossWorld(mode, episode, map);
        Kill(world, Spawn(world, 300, 128, boss));
        string happened = world.gameaction == gameaction_t.ga_completed ? "exit"
            : world.sectors[1].specialdata switch { floormove_t => "floor", vldoor_t => "door", _ => "nothing" };
        Assert.Equal(what, happened);
    }

    [Fact]
    public void ABarrelKilledByOneShotExplodesForNoOne()
    {
        // vanilla: P_DamageMobj returns before the target change when the damage kills, so the
        // barrel's target stays null and its explosion has no source
        World world = OneRoom();
        mobj_t barrel = Spawn(world, 300, 128, mobjtype_t.MT_BARREL);
        world.P_DamageMobj(barrel, Player(world), Player(world), 20);
        Assert.Null(barrel.target);
        Assert.True(barrel.health <= 0);
    }

    [Fact]
    public void BothDoom1E1M8BaronsLowerTheWall()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom1());
        var world = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium), Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(wad, "E1M8"));
        mobj_t[] barons = world.Mobjs().Where(m => m.type == mobjtype_t.MT_BRUISER).ToArray();
        Assert.Equal(2, barons.Length);
        foreach (mobj_t baron in barons)
        {
            world.P_DamageMobj(baron, null, null, 10000);
            for (int i = 0; i < 70; i++)
                world.G_Ticker(default(ticcmd_t));
        }
        Assert.True(world.sectors[30].tag == 666);
        for (int i = 0; i < 400 && world.sectors[30].specialdata != null; i++)
            world.G_Ticker(default(ticcmd_t));
        Assert.Equal(-136 * FRACUNIT, world.sectors[30].floorheight); // its lowest neighbour, the arena 29
    }

    // ---- the route events and the checksum ----

    [Fact]
    public void RoutesParseTheMonstersHeaderAndTheEvents()
    {
        string text = "iwad synthetic\nmonsters\n0 0 0 0 x2\nalert\ndamage 320 64 15\n25 0 0 0\n";
        RouteFile route = RouteFile.Parse(text.Split('\n'), "test.route");
        Assert.True(route.Monsters);
        Assert.Equal(3, route.Cmds.Count);
        Assert.Equal(new[] { new RouteEvent(2, RouteEventKind.Alert, 0, 0, 0), new RouteEvent(2, RouteEventKind.Damage, 320, 64, 15) }, route.Events);
        Assert.Equal("damage 320 64 15", route.Events[1].ToString());
        Assert.False(RouteFile.Parse(new[] { "iwad synthetic", "0 0 0 0" }, "x").Monsters);
        Assert.Throws<FormatException>(() => RouteFile.Parse(new[] { "iwad synthetic", "0 0 0 0", "alert" }, "x")); // after the last tic
        Assert.Throws<FormatException>(() => RouteFile.Parse(new[] { "iwad synthetic", "damage 1 2", "0 0 0 0" }, "x"));
        Assert.Throws<FormatException>(() => RouteFile.Parse(new[] { "iwad synthetic", "damage 1 2 0", "0 0 0 0" }, "x"));
    }

    [Fact]
    public void ADamageEventHurtsTheThingSpawnedThereAsAShot()
    {
        World world = OneRoomMap().Thing(300, 128, 3004, 180).Load();
        mobj_t zombie = world.Mobjs().Single(m => m.type == mobjtype_t.MT_POSSESSED);
        NextRandom(world, 3); // no pain
        new RouteEvent(0, RouteEventKind.Damage, 300, 128, 5).Run(world);
        Assert.Equal(15, zombie.health);
        Assert.Same(Player(world), zombie.target);
        Assert.True(zombie.momx > 0); // pushed away from the player
        Assert.Throws<InvalidOperationException>(() => new RouteEvent(0, RouteEventKind.Damage, 1, 1, 5).Run(world));

        new RouteEvent(0, RouteEventKind.Alert, 0, 0, 0).Run(world);
        Assert.Same(Player(world), zombie.subsector.sector.soundtarget);
    }

    [Fact]
    public void TheChecksumCoversWhatMonstersThinkWith()
    {
        World world = OneRoom();
        mobj_t zombie = Spawn(world, 300, 128, mobjtype_t.MT_POSSESSED);
        ulong sum = world.Checksum();
        zombie.target = Player(world);
        Assert.NotEqual(sum, world.Checksum());
        zombie.target = null;
        Assert.Equal(sum, world.Checksum());
        foreach (Action<mobj_t> change in new Action<mobj_t>[] { m => m.movedir = 3, m => m.movecount = 7, m => m.reactiontime = 1, m => m.threshold = 9 })
        {
            change(zombie);
            Assert.NotEqual(sum, world.Checksum());
            sum = world.Checksum();
        }
        world.sectors[0].soundtarget = zombie;
        Assert.NotEqual(sum, world.Checksum());
    }
}
