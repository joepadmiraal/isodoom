using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T6.3: p_inter.c <c>P_DamageMobj</c> and <c>P_KillMobj</c> for monsters
/// and barrels, p_map.c's aiming (<c>P_AimLineAttack</c>), hitscans
/// (<c>P_LineAttack</c>: puffs, blood, sky hits), radius attacks
/// (<c>P_RadiusAttack</c>), gun lines (<c>P_ShootSpecialLine</c>) and crushing
/// (<c>PIT_ChangeSector</c>) on synthetic <see cref="TestMap"/>s. The player
/// (56 units tall) shoots from 36 units above its feet.
/// </summary>
public class DamageHitscanTests
{
    private const int FRACUNIT = 1 << 16;

    private static int F(int units) => units * FRACUNIT;

    private static mobj_t Player(World world) => world.players[0].mo!;

    private static mobj_t Spawn(World world, int x, int y, mobjtype_t type) =>
        world.P_SpawnMobj(F(x), F(y), World.ONFLOORZ, type);

    private static mobj_t[] Of(World world, mobjtype_t type) => [.. world.Mobjs().Where(m => m.type == type)];

    /// <summary>Makes the next <c>P_Random</c> return <c>rndtable[index + 1]</c> (1: 8, 2: 109, 3: 220).</summary>
    private static void NextRandom(World world, int index) => world.random.prndindex = index - 1;

    /// <summary>
    /// Three rooms west to east: A (x 0..256), the middle room M (256..320,
    /// floor <paramref name="floor"/>, ceiling <paramref name="ceiling"/>) and B
    /// (320..576); A and B have floor 0 and ceiling 128. The player in A at
    /// (64, 128) facing east.
    /// </summary>
    private static TestMap ThreeRooms(int floor, int ceiling) =>
        TestMap.Strip(0, 0, 256, new TestMap.Room(256, 0, 128), new TestMap.Room(64, floor, ceiling), new TestMap.Room(256, 0, 128))
            .Player(64, 128);

    private static World OneRoom() => TestMap.Strip(0, 0, 256, new TestMap.Room(512, 0, 128)).Player(64, 128).Load();

    // ---- aiming and hitscans ----

    [Theory]
    [InlineData(0, 128, true, true)]    // open: both hit
    [InlineData(0, 0, false, false)]    // a closed door stops both
    [InlineData(64, 64, false, false)]  // a closed door off the floor
    [InlineData(40, 64, true, false)]   // a window above the gun: the aim goes up through it, a level shot hits its sill
    [InlineData(0, 30, true, false)]    // a low opening: the aim goes down under it, a level shot hits its lintel
    [InlineData(100, 128, false, false)] // a high step hides the zombieman: the aim finds nothing
    public void ShotsPassOpeningsAndStopAtWalls(int floor, int ceiling, bool aimHits, bool levelShotHits)
    {
        World world = ThreeRooms(floor, ceiling).Load();
        mobj_t me = Player(world);
        mobj_t zombie = Spawn(world, 448, 128, mobjtype_t.MT_POSSESSED);

        int slope = world.P_AimLineAttack(me, 0, World.MISSILERANGE);
        Assert.Equal(aimHits ? zombie : null, world.linetarget);
        if (!aimHits)
            Assert.Equal(0, slope);

        if (aimHits)
        {
            NextRandom(world, 3); // no pain
            world.P_LineAttack(me, 0, World.MISSILERANGE, slope, 5);
            Assert.Equal(15, zombie.health);
            mobj_t blood = Assert.Single(Of(world, mobjtype_t.MT_BLOOD));
            Assert.InRange(blood.x, F(436), F(440)); // 10 units before its centre (where the trace crosses its box's diagonal)
            Assert.Equal(statenum_t.S_BLOOD3, blood.state); // damage below 9
            world.P_RemoveMobj(blood);
        }

        zombie.health = 20;
        world.P_LineAttack(me, 0, World.MISSILERANGE, 0, 5);
        if (levelShotHits)
        {
            Assert.Equal(15, zombie.health);
            Assert.Empty(Of(world, mobjtype_t.MT_PUFF));
        }
        else
        {
            Assert.Equal(20, zombie.health);
            // a puff 4 units before the boundary, give or take 4 units of height
            mobj_t puff = Assert.Single(Of(world, mobjtype_t.MT_PUFF));
            Assert.InRange(puff.x, F(251), F(253));
            Assert.InRange(puff.z, F(32), F(40));
            Assert.Equal(FRACUNIT, puff.momz);
            Assert.Empty(Of(world, mobjtype_t.MT_BLOOD));
        }
    }

    [Fact]
    public void AimingAtNothingGivesZeroAndAShotStopsAtTheFarWall()
    {
        World world = OneRoom();
        mobj_t me = Player(world);
        Assert.Equal(0, world.P_AimLineAttack(me, 0, World.MISSILERANGE));
        Assert.Null(world.linetarget);

        world.P_LineAttack(me, 0, World.MISSILERANGE, 0, 10);
        mobj_t puff = Assert.Single(Of(world, mobjtype_t.MT_PUFF));
        Assert.InRange(puff.x, F(507), F(509));
        Assert.InRange(puff.y, F(128), F(129)); // finesine[0] is 25: the trace drifts north

        // out of range: nothing at all
        world.P_RemoveMobj(puff);
        world.P_LineAttack(me, 0, World.MELEERANGE, 0, 10);
        Assert.Empty(Of(world, mobjtype_t.MT_PUFF));
    }

    [Fact]
    public void AimingFindsATargetOnALedgeAndSkipsTheShooterAndCorpses()
    {
        World world = TestMap.Strip(0, 0, 256, new TestMap.Room(256, 0, 128), new TestMap.Room(256, 64, 192)).Player(64, 128).Load();
        mobj_t me = Player(world);
        mobj_t corpse = Spawn(world, 160, 128, mobjtype_t.MT_POSSESSED);
        corpse.flags &= ~mobjflag_t.MF_SHOOTABLE;
        mobj_t zombie = Spawn(world, 448, 128, mobjtype_t.MT_POSSESSED);
        Assert.Equal(F(64), zombie.z);

        int slope = world.P_AimLineAttack(me, 0, World.MISSILERANGE);
        Assert.Same(zombie, world.linetarget);
        Assert.True(slope > 0);

        NextRandom(world, 3);
        world.P_LineAttack(me, 0, World.MISSILERANGE, slope, 10);
        Assert.Equal(10, zombie.health);
        Assert.Equal(20, corpse.health);
        Assert.Equal(statenum_t.S_BLOOD2, Assert.Single(Of(world, mobjtype_t.MT_BLOOD)).state); // damage 9-12
    }

    [Fact]
    public void ABarrelBleedsPuffs()
    {
        World world = OneRoom();
        mobj_t barrel = Spawn(world, 256, 128, mobjtype_t.MT_BARREL);
        world.P_LineAttack(Player(world), 0, World.MISSILERANGE, 0, 15);
        Assert.Equal(5, barrel.health);
        Assert.Single(Of(world, mobjtype_t.MT_PUFF));
        Assert.Empty(Of(world, mobjtype_t.MT_BLOOD));

        world.P_LineAttack(Player(world), 0, World.MISSILERANGE, 0, 15);
        Assert.Equal(statenum_t.S_BEXP, barrel.state);
        Assert.True((barrel.flags & mobjflag_t.MF_CORPSE) != 0);
    }

    [Fact]
    public void TheSkyEatsShots()
    {
        // above a sky ceiling: no puff; below it, a puff
        World world = TestMap.Strip(0, 0, 256, new TestMap.Room(256, 0, 128)).CeilingPic(0, World.SKYFLATNAME).Player(64, 128).Load();
        mobj_t me = Player(world);
        world.P_LineAttack(me, 0, World.MISSILERANGE, FRACUNIT / 2, 10); // 36 + 188 / 2 > 128 at the wall
        Assert.Empty(Of(world, mobjtype_t.MT_PUFF));
        world.P_LineAttack(me, 0, World.MISSILERANGE, FRACUNIT / 4, 10); // 36 + 188 / 4 < 128
        Assert.Single(Of(world, mobjtype_t.MT_PUFF));

        // a sky hack wall: the upper part between two sky sectors (the far one in front)
        TestMap hack = TestMap.Strip(0, 0, 256, new TestMap.Room(256, 0, 64), new TestMap.Room(256, 0, 128))
            .CeilingPic(1, World.SKYFLATNAME).Player(64, 128);
        world = hack.CeilingPic(0, World.SKYFLATNAME).Load();
        world.P_LineAttack(Player(world), 0, World.MISSILERANGE, FRACUNIT * 3 / 10, 10); // hits the boundary at 92
        Assert.Empty(Of(world, mobjtype_t.MT_PUFF));

        // the same wall with a plain ceiling behind it: a puff
        world = TestMap.Strip(0, 0, 256, new TestMap.Room(256, 0, 64), new TestMap.Room(256, 0, 128))
            .CeilingPic(1, World.SKYFLATNAME).Player(64, 128).Load();
        world.P_LineAttack(Player(world), 0, World.MISSILERANGE, FRACUNIT * 3 / 10, 10);
        mobj_t puff = Assert.Single(Of(world, mobjtype_t.MT_PUFF));
        Assert.InRange(puff.x, F(251), F(253));
    }

    [Fact]
    public void PuffsAndBloodComeInSizes()
    {
        World world = OneRoom();
        world.attackrange = World.MELEERANGE;
        Assert.Equal(statenum_t.S_PUFF3, world.P_SpawnPuff(F(128), F(128), F(32)).state); // no spark for a punch
        world.attackrange = World.MISSILERANGE;
        mobj_t puff = world.P_SpawnPuff(F(128), F(128), F(32));
        Assert.Equal(statenum_t.S_PUFF1, puff.state);
        Assert.InRange(puff.tics, 1, 4);

        Assert.Equal(statenum_t.S_BLOOD1, world.P_SpawnBlood(F(128), F(128), F(32), 13).state);
        Assert.Equal(statenum_t.S_BLOOD2, world.P_SpawnBlood(F(128), F(128), F(32), 9).state);
        Assert.Equal(statenum_t.S_BLOOD3, world.P_SpawnBlood(F(128), F(128), F(32), 8).state);
        Assert.Equal(2 * FRACUNIT, Of(world, mobjtype_t.MT_BLOOD)[0].momz);
    }

    // ---- gun lines ----

    /// <summary>A, a closed door sector D (tag 1) and B; the boundary A|D has <paramref name="special"/>.</summary>
    private static World GunLine(int special, out line_t line)
    {
        TestMap map = TestMap.Strip(0, 0, 256, new TestMap.Room(256, 0, 128), new TestMap.Room(16, 0, 0), new TestMap.Room(256, 0, 128))
            .SectorTag(1, 1).Player(64, 128);
        World world = map.Special(map.Boundaries[1], special, 1).Load();
        line = world.lines[map.Boundaries[1]];
        return world;
    }

    [Theory]
    [InlineData(24, typeof(floormove_t))]
    [InlineData(46, typeof(vldoor_t))]
    [InlineData(47, typeof(plat_t))]
    public void ShotsTriggerGunLines(int special, System.Type mover)
    {
        World world = GunLine(special, out line_t line);
        world.P_LineAttack(Player(world), 0, World.MISSILERANGE, 0, 10);
        Assert.IsType(mover, world.sectors[1].specialdata);
        Assert.Single(Of(world, mobjtype_t.MT_PUFF)); // the shot still stops at the closed door
        // G1 lines lose their special, GR 46 keeps it
        Assert.Equal(special == 46 ? 46 : 0, line.special);
    }

    [Theory]
    [InlineData(24, false)]
    [InlineData(46, true)]
    [InlineData(47, false)]
    public void MonstersTriggerOnlyTheDoorGunLine(int special, bool opens)
    {
        World world = GunLine(special, out line_t line);
        mobj_t zombie = Spawn(world, 128, 64, mobjtype_t.MT_POSSESSED);
        world.P_LineAttack(zombie, 0, World.MISSILERANGE, 0, 10);
        Assert.Equal(opens, world.sectors[1].specialdata != null);
        Assert.Equal(special, line.special);
    }

    [Fact]
    public void AShotThroughAnOpenGunLineTriggersItAndGoesOn()
    {
        TestMap map = ThreeRooms(0, 128).SectorTag(1, 1);
        World world = map.Special(map.Boundaries[1], 46, 1).Load();
        mobj_t zombie = Spawn(world, 448, 128, mobjtype_t.MT_POSSESSED);
        NextRandom(world, 3);
        world.P_LineAttack(Player(world), 0, World.MISSILERANGE, 0, 5);
        Assert.IsType<vldoor_t>(world.sectors[1].specialdata);
        Assert.Equal(15, zombie.health);
    }

    // ---- P_DamageMobj and P_KillMobj ----

    [Fact]
    public void DamageWakesAMonsterAndTurnsItOnItsAttacker()
    {
        World world = OneRoom();
        mobj_t me = Player(world);
        mobj_t zombie = Spawn(world, 256, 128, mobjtype_t.MT_POSSESSED);
        Assert.Equal(8, zombie.reactiontime);

        NextRandom(world, 3); // 220: no pain (chance 200)
        world.P_DamageMobj(zombie, me, me, 10);
        Assert.Equal(10, zombie.health);
        Assert.Equal(0, zombie.reactiontime);
        Assert.Same(me, zombie.target);
        Assert.Equal(World.BASETHRESHOLD - 1, zombie.threshold); // (T6.4: the see state's A_Chase counted it down)
        // from its spawn state to its see state, whose A_Chase (T6.4) fires at once: in sight, awake, no steps left
        Assert.Equal(statenum_t.S_POSS_ATK1, zombie.state);
        Assert.True((zombie.flags & mobjflag_t.MF_JUSTHIT) == 0);
        // thrust away from the inflictor: 10 * 100 / mass 100 units / 8
        int thrust = 10 * (FRACUNIT >> 3) * 100 / 100;
        Assert.Equal(Fixed.FixedMul(thrust, Tables.finecosine[0]), zombie.momx);
        Assert.Equal(Fixed.FixedMul(thrust, Tables.finesine[0]), zombie.momy); // finesine[0] is 25, not 0
    }

    [Fact]
    public void PainChanceSetsJustHit()
    {
        World world = OneRoom();
        mobj_t me = Player(world);
        mobj_t zombie = Spawn(world, 256, 128, mobjtype_t.MT_POSSESSED);
        NextRandom(world, 1); // 8 < 200
        world.P_DamageMobj(zombie, me, me, 10);
        Assert.True((zombie.flags & mobjflag_t.MF_JUSTHIT) != 0);
        Assert.Equal(statenum_t.S_POSS_PAIN, zombie.state); // pain, then no see state (not in its spawn state)
        Assert.Same(me, zombie.target);
    }

    [Fact]
    public void MonstersFightBackUnlessIntentOnAnother()
    {
        World world = OneRoom();
        mobj_t me = Player(world);
        mobj_t zombie = Spawn(world, 256, 128, mobjtype_t.MT_POSSESSED);
        mobj_t imp = Spawn(world, 320, 128, mobjtype_t.MT_TROOP);
        zombie.target = me;

        // intent on the player: the imp's hit does not change that
        zombie.threshold = 50;
        NextRandom(world, 3);
        world.P_DamageMobj(zombie, imp, imp, 1);
        Assert.Same(me, zombie.target);
        Assert.Equal(50, zombie.threshold);

        // no longer intent: infighting
        zombie.threshold = 0;
        NextRandom(world, 3);
        world.P_DamageMobj(zombie, imp, imp, 1);
        Assert.Same(imp, zombie.target);
        Assert.Equal(World.BASETHRESHOLD - 1, zombie.threshold); // (T6.4: the see state's A_Chase counted it down)

        // its own or environment damage never changes the target
        zombie.threshold = 0;
        NextRandom(world, 3);
        world.P_DamageMobj(zombie, zombie, zombie, 1);
        NextRandom(world, 3);
        world.P_DamageMobj(zombie, null, null, 1);
        Assert.Same(imp, zombie.target);
        Assert.Equal(16, zombie.health);
    }

    [Fact]
    public void TheChainsawDoesNotPush()
    {
        World world = OneRoom();
        mobj_t me = Player(world);
        mobj_t zombie = Spawn(world, 256, 128, mobjtype_t.MT_POSSESSED);
        world.players[0].readyweapon = weapontype_t.wp_chainsaw;
        NextRandom(world, 3);
        world.P_DamageMobj(zombie, me, me, 5);
        Assert.Equal((0, 0), (zombie.momx, zombie.momy));
        world.players[0].readyweapon = weapontype_t.wp_pistol;
        NextRandom(world, 3);
        world.P_DamageMobj(zombie, me, me, 5);
        Assert.True(zombie.momx > 0);
    }

    [Theory]
    [InlineData(30, statenum_t.S_POSS_DIE1)]
    [InlineData(41, statenum_t.S_POSS_XDIE1)] // health -21: below minus its spawn health
    public void KilledMonstersBecomeCorpsesAndDropItems(int damage, statenum_t death)
    {
        World world = OneRoom();
        mobj_t me = Player(world);
        mobj_t zombie = Spawn(world, 256, 128, mobjtype_t.MT_POSSESSED);
        world.P_DamageMobj(zombie, me, me, damage);

        Assert.Equal(20 - damage, zombie.health);
        Assert.Equal(death, zombie.state);
        Assert.InRange(zombie.tics, 1, Info.states[(int)death].tics);
        Assert.Equal(0, (int)(zombie.flags & (mobjflag_t.MF_SHOOTABLE | mobjflag_t.MF_FLOAT | mobjflag_t.MF_SKULLFLY | mobjflag_t.MF_NOGRAVITY)));
        Assert.Equal(mobjflag_t.MF_CORPSE | mobjflag_t.MF_DROPOFF, zombie.flags & (mobjflag_t.MF_CORPSE | mobjflag_t.MF_DROPOFF));
        Assert.Equal(F(14), zombie.height);
        Assert.Equal(1, world.players[0].killcount);
        mobj_t clip = Assert.Single(Of(world, mobjtype_t.MT_CLIP));
        Assert.True((clip.flags & mobjflag_t.MF_DROPPED) != 0);
        Assert.Equal((zombie.x, zombie.y, 0), (clip.x, clip.y, clip.z));

        // a corpse takes no more damage, and shots pass it
        world.P_DamageMobj(zombie, me, me, 10);
        Assert.Equal(20 - damage, zombie.health);
        world.P_LineAttack(me, 0, World.MISSILERANGE, 0, 10);
        Assert.InRange(Assert.Single(Of(world, mobjtype_t.MT_PUFF)).x, F(507), F(509));
    }

    [Fact]
    public void AShotgunGuyDropsAShotgunAndAMonsterKillCountsWithoutAPlayer()
    {
        World world = OneRoom();
        mobj_t sarge = Spawn(world, 256, 128, mobjtype_t.MT_SHOTGUY);
        mobj_t imp = Spawn(world, 320, 128, mobjtype_t.MT_TROOP);
        world.P_DamageMobj(sarge, imp, imp, 100);
        Assert.Single(Of(world, mobjtype_t.MT_SHOTGUN));
        Assert.Equal(1, world.players[0].killcount); // all monster deaths count in single player
    }

    [Theory]
    [InlineData(1, 100, 30, 80, 90, 1)] // green armor takes a third
    [InlineData(2, 100, 30, 85, 85, 2)] // blue armor half
    [InlineData(1, 5, 30, 75, 0, 0)]    // used up
    [InlineData(0, 0, 30, 70, 0, 0)]
    public void ArmorAbsorbsAShare(int armortype, int armorpoints, int damage, int health, int armorLeft, int typeLeft)
    {
        World world = OneRoom();
        player_t p = world.players[0];
        p.armortype = armortype;
        p.armorpoints = armorpoints;
        mobj_t imp = Spawn(world, 256, 128, mobjtype_t.MT_TROOP);
        world.P_DamageMobj(p.mo!, imp, imp, damage);
        Assert.Equal((health, armorLeft, typeLeft), (p.health, p.armorpoints, p.armortype));
        Assert.Equal(health, p.mo!.health);
        Assert.Same(imp, p.attacker);
        Assert.Equal(100 - health, p.damagecount);
    }

    [Fact]
    public void ThePlayerTakesHalfDamageOnTheEasiestSkill()
    {
        World world = TestMap.Strip(0, 0, 256, new TestMap.Room(512, 0, 128)).Player(64, 128).Load(skill_t.sk_baby);
        world.P_DamageMobj(Player(world), null, null, 21);
        Assert.Equal(90, world.players[0].health);
        // monsters take full damage
        mobj_t zombie = Spawn(world, 256, 128, mobjtype_t.MT_POSSESSED);
        NextRandom(world, 3);
        world.P_DamageMobj(zombie, null, null, 11);
        Assert.Equal(9, zombie.health);
    }

    // ---- radius attacks ----

    [Fact]
    public void RadiusAttacksFallOffWithDistanceAndStopAtWalls()
    {
        // A (0..256) | closed door (256..272) | B (272..528)
        World world = TestMap.Strip(0, 0, 256, new TestMap.Room(256, 0, 128), new TestMap.Room(16, 0, 0), new TestMap.Room(256, 0, 128))
            .Player(32, 32).Load();
        mobj_t spot = Spawn(world, 200, 128, mobjtype_t.MT_PUFF);
        mobj_t near = Spawn(world, 140, 128, mobjtype_t.MT_POSSESSED);     // 60 - radius 20: 40 away
        mobj_t diagonal = Spawn(world, 170, 158, mobjtype_t.MT_POSSESSED); // the larger axis: 30 - 20 = 10
        mobj_t far = Spawn(world, 120, 128, mobjtype_t.MT_POSSESSED);      // 80 - 20 = 60: out of reach
        mobj_t behind = Spawn(world, 300, 128, mobjtype_t.MT_POSSESSED);   // 100 - 20 = 80 > 50, but see below
        mobj_t cyborg = Spawn(world, 200, 60, mobjtype_t.MT_CYBORG);
        int cyborgHealth = cyborg.health;
        mobj_t me = Player(world);

        world.random.prndindex = 0;
        world.P_RadiusAttack(spot, me, 50);
        Assert.Equal(20 - 10, near.health);
        Assert.Equal(20 - 40, diagonal.health); // killed
        Assert.Equal(20, far.health);
        Assert.Equal(20, behind.health);
        Assert.Equal(cyborgHealth, cyborg.health); // no concussion for the bosses
        Assert.Same(me, near.target);
        Assert.Equal(1, world.players[0].killcount);

        // within reach, but behind the closed door: unharmed
        world.P_RadiusAttack(spot, me, 128);
        Assert.Equal(20, behind.health);
        Assert.True(near.health <= 0);
        Assert.True(far.health <= 0);
    }

    [Theory]
    [InlineData(300, true)]  // 300 + 20 reaches the block from x 320
    [InlineData(290, false)] // 290 + 20 does not: the baron there is skipped although its edge is 10 units away
    public void RadiusAttacksSearchOnlyTheBlocksWithinTheDamage(int spotX, bool hurt)
    {
        // vanilla's (damage + MAXRADIUS) << FRACBITS wraps to damage << FRACBITS, so the
        // blocks searched reach only the damage, not the damage plus the largest radius
        World world = OneRoom(); // blockmap origin x -64: blocks from 64, 192, 320, ...
        mobj_t spot = Spawn(world, spotX, 128, mobjtype_t.MT_PUFF);
        mobj_t baron = Spawn(world, 324, 128, mobjtype_t.MT_BRUISER); // radius 24
        world.P_RadiusAttack(spot, null, 20);
        Assert.Equal(hurt, baron.health < 1000);
    }

    // ---- crushing ----

    [Fact]
    public void CrushersHurtMonstersAndGibCorpses()
    {
        World world = OneRoom();
        mobj_t zombie = Spawn(world, 256, 128, mobjtype_t.MT_POSSESSED);
        sector_t sector = world.sectors[0];
        sector.ceilingheight = F(32);

        world.leveltime = 0;
        Assert.True(world.P_ChangeSector(sector, true));
        Assert.Equal(10, zombie.health);
        Assert.Contains(world.Mobjs(), m => m.type == mobjtype_t.MT_BLOOD);

        world.leveltime = 1; // only every fourth tic
        world.P_ChangeSector(sector, true);
        Assert.Equal(10, zombie.health);

        world.leveltime = 4;
        world.P_ChangeSector(sector, true);
        Assert.True(zombie.health <= 0);
        Assert.Equal(F(14), zombie.height); // a corpse fits under 32

        sector.ceilingheight = F(8);
        world.P_ChangeSector(sector, true);
        Assert.Equal(statenum_t.S_GIBS, zombie.state);
        Assert.Equal((0, 0), (zombie.height, zombie.radius));
    }
}
