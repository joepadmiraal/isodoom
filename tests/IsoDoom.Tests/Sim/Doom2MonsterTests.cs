using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T10.3: p_enemy.c's Doom II monster actions (the chaingunner and the
/// Wolfenstein SS, the revenant, the mancubus, the arachnotron, the pain
/// elemental, the arch-vile and its fire, Commander Keen) and the registered
/// monsters' ones Doom II's maps need (the cacodemon, the lost soul's
/// charge, the Cyberdemon, the Spider Mastermind), on synthetic
/// <see cref="TestMap"/>s, without a WAD. The Doom II routes
/// (<c>map07-bosses</c>, <c>map10-revenant</c>, <c>map28-vile</c>, …;
/// <see cref="VanillaRouteTests"/>) check them against vanilla tic by tic.
/// </summary>
public class Doom2MonsterTests
{
    private const int FRACUNIT = 1 << 16;

    private static int F(int units) => units * FRACUNIT;

    private static mobj_t Player(World world) => world.players[0].mo!;

    private static mobj_t Spawn(World world, int x, int y, mobjtype_t type) =>
        world.P_SpawnMobj(F(x), F(y), World.ONFLOORZ, type);

    /// <summary>One room, x 0..512, y 0..256, floor 0, ceiling 128, and a raised one east of it tagged 666; the player at (64, 128) facing east.</summary>
    private static World Room(GameMode mode = GameMode.commercial)
    {
        TestMap map = TestMap.Strip(0, 0, 256, new TestMap.Room(512, 0, 128), new TestMap.Room(128, 64, 128)).SectorTag(1, 666).Player(64, 128);
        var wad = new WadArchive([WadFile.FromBytes(map.Build(), "testmap.wad")]);
        var world = new World(new SpawnSettings(mode, skill_t.sk_medium), Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(wad, "E1M1"));
        return world;
    }

    private static mobj_t[] All(World world, mobjtype_t type) => [.. world.Mobjs().Where(m => m.type == type)];

    // ---- the chaingunner, the Wolfenstein SS ----

    [Fact]
    public void AChaingunnerFiresOneBulletAShotWithTheShotgunsSound()
    {
        World world = Room();
        mobj_t gunner = Spawn(world, 300, 128, mobjtype_t.MT_CHAINGUY);
        gunner.target = Player(world);
        int before = world.random.prndindex;
        world.A_CPosAttack(gunner);
        Assert.Contains(world.StartedSounds(), s => s.sfx == sfxenum_t.sfx_shotgn && s.origin == gunner);
        Assert.InRange(gunner.angle, Tables.ANG180 - 2, Tables.ANG180); // faced the player
        int used = world.random.prndindex - before;
        Assert.InRange(used, 3, 8); // the spread (2) and damage (1), and any puff or blood

        gunner.target = null;
        before = world.random.prndindex;
        world.A_CPosAttack(gunner); // no target: nothing
        Assert.Equal(before, world.random.prndindex);
    }

    [Fact]
    public void AChaingunnerKeepsFiringUnlessItsTargetIsGone()
    {
        World world = Room();
        mobj_t gunner = Spawn(world, 300, 128, mobjtype_t.MT_CHAINGUY);
        gunner.target = Player(world);
        gunner.state = statenum_t.S_CPOS_ATK4; // (without running the state's own A_CPosRefire)
        world.random.prndindex = 2; // the next P_Random is rndtable[3], 220: no early return
        world.A_CPosRefire(gunner);
        Assert.Equal(statenum_t.S_CPOS_ATK4, gunner.state); // in sight, alive: fires on

        Player(world).health = 0;
        world.random.prndindex = 2;
        world.A_CPosRefire(gunner);
        Assert.Equal(statenum_t.S_CPOS_RUN1, gunner.state); // the see state

        gunner.state = statenum_t.S_CPOS_ATK4; // (without running the state's own A_CPosRefire)
        world.random.prndindex = 0; // rndtable[1], 8 < 40: keeps firing whatever
        world.A_CPosRefire(gunner);
        Assert.Equal(statenum_t.S_CPOS_ATK4, gunner.state);
    }

    [Fact]
    public void TheWolfensteinSsAndTheSpiderUseTheChaingunnersAndShotgunGuysAttacks()
    {
        foreach (statenum_t s in new[] { statenum_t.S_SSWV_ATK2, statenum_t.S_SSWV_ATK3, statenum_t.S_SSWV_ATK5 })
            Assert.Contains(Info.states[(int)s].action, new[] { actionf_t.A_CPosAttack, actionf_t.A_FaceTarget });
        Assert.Equal(actionf_t.A_CPosRefire, Info.states[(int)statenum_t.S_SSWV_ATK6].action);
        Assert.Equal(actionf_t.A_SpidRefire, Info.states[(int)statenum_t.S_SPID_ATK4].action);

        World world = Room();
        mobj_t spider = Spawn(world, 300, 128, mobjtype_t.MT_SPIDER);
        spider.target = Player(world);
        spider.state = statenum_t.S_SPID_ATK4; // (without running the state's own A_SpidRefire)
        world.random.prndindex = 0; // 8 < 10: fires on
        Player(world).health = 0;
        world.A_SpidRefire(spider);
        Assert.Equal(statenum_t.S_SPID_ATK4, spider.state);
        world.random.prndindex = 1; // 109: the dead target sends it back
        world.A_SpidRefire(spider);
        Assert.Equal(statenum_t.S_SPID_RUN1, spider.state);
    }

    // ---- the revenant ----

    [Fact]
    public void ARevenantMissileSpawnsHigherAStepAheadAndHomesOnItsTarget()
    {
        World world = Room();
        mobj_t rev = Spawn(world, 400, 128, mobjtype_t.MT_UNDEAD);
        rev.target = Player(world);
        world.A_SkelMissile(rev);
        mobj_t missile = Assert.Single(All(world, mobjtype_t.MT_TRACER));
        Assert.Same(Player(world), missile.tracer);
        Assert.Same(rev, missile.target);
        Assert.Equal(0, rev.z); // back to normal
        Assert.Equal(F(48) + (missile.momz >> 1), missile.z); // 32 + 16 units up, and P_CheckMissileSpawn's half step
        // P_SpawnMissile's half step, then a whole one more
        Assert.Equal(F(400) + (missile.momx >> 1) + missile.momx, missile.x);
    }

    [Fact]
    public void ARevenantMissileTurnsOnlyEveryFourthGametic()
    {
        World world = Room();
        mobj_t rev = Spawn(world, 400, 200, mobjtype_t.MT_UNDEAD);
        rev.target = Player(world);
        world.A_SkelMissile(rev);
        mobj_t missile = Assert.Single(All(world, mobjtype_t.MT_TRACER));
        missile.angle = 0; // flying east, away from the player (west)
        missile.momz = 0;

        world.gametic = 1;
        world.A_Tracer(missile);
        Assert.Equal(0u, missile.angle);
        Assert.Empty(All(world, mobjtype_t.MT_SMOKE));

        world.gametic = 4;
        world.A_Tracer(missile);
        Assert.Single(All(world, mobjtype_t.MT_SMOKE));
        Assert.Single(All(world, mobjtype_t.MT_PUFF));
        // the player is south-west and behind: one TRACEANGLE turn, the shorter way (clockwise, south)
        Assert.Equal(unchecked(0u - 0xc000000u), missile.angle);
        Assert.Equal(Fixed.FixedMul(missile.info.speed, Tables.finecosine[(int)(missile.angle >> Tables.ANGLETOFINESHIFT)]), missile.momx);
        Assert.Equal(-FRACUNIT / 8, missile.momz); // the player's 40 units are below the missile's 48
    }

    [Fact]
    public void ARevenantPunchesOnlyInMeleeRange()
    {
        World world = Room();
        mobj_t rev = Spawn(world, 300, 128, mobjtype_t.MT_UNDEAD);
        rev.target = Player(world);
        world.A_SkelWhoosh(rev);
        Assert.Contains(world.StartedSounds(), s => s.sfx == sfxenum_t.sfx_skeswg && s.origin == rev);
        world.A_SkelFist(rev);
        Assert.Equal(100, world.players[0].health);
        world.PlaceMobj(rev, F(64 + 50), F(128));
        world.A_SkelFist(rev);
        Assert.InRange(world.players[0].health, 100 - 60, 100 - 6);
        Assert.Contains(world.StartedSounds(), s => s.sfx == sfxenum_t.sfx_skepch && s.origin == rev);
    }

    // ---- the mancubus, the arachnotron, the cacodemon, the Cyberdemon ----

    [Fact]
    public void AMancubusFiresTwoBallsAVolleySpreadAroundItsTarget()
    {
        const uint FATSPREAD = Tables.ANG90 / 8;
        World world = Room();
        mobj_t fatso = Spawn(world, 400, 128, mobjtype_t.MT_FATSO);
        fatso.target = Player(world);
        world.A_FatRaise(fatso);
        Assert.Contains(world.StartedSounds(), s => s.sfx == sfxenum_t.sfx_manatk && s.origin == fatso);
        uint at = fatso.angle;

        world.A_FatAttack1(fatso);
        mobj_t[] shots = All(world, mobjtype_t.MT_FATSHOT);
        Assert.Equal(2, shots.Length);
        Assert.Equal(unchecked(at + FATSPREAD), fatso.angle); // turned
        Assert.Equal(unchecked(at + FATSPREAD), shots[1].angle); // the second turned on: at the target + FATSPREAD
        Assert.Equal(at, shots[0].angle); // the first at the target

        foreach (mobj_t m in shots)
            world.P_RemoveMobj(m);
        world.A_FatAttack2(fatso);
        shots = All(world, mobjtype_t.MT_FATSHOT);
        Assert.Equal(unchecked(at - FATSPREAD), fatso.angle);
        Assert.Equal(unchecked(at - FATSPREAD * 2), shots[1].angle);

        foreach (mobj_t m in shots)
            world.P_RemoveMobj(m);
        world.A_FatAttack3(fatso);
        shots = All(world, mobjtype_t.MT_FATSHOT);
        Assert.Equal(unchecked(at - FATSPREAD / 2), shots[0].angle);
        Assert.Equal(unchecked(at + FATSPREAD / 2), shots[1].angle);
    }

    [Fact]
    public void AMancubusWithoutATargetFiresAtTheMapOrigin()
    {
        // Chocolate Doom's P_SubstNullMobj: a dummy target at (0, 0, 0) where vanilla read a null pointer
        World world = Room();
        mobj_t fatso = Spawn(world, 400, 128, mobjtype_t.MT_FATSO);
        world.A_FatAttack3(fatso);
        Assert.Equal(2, All(world, mobjtype_t.MT_FATSHOT).Length);
        Assert.Null(fatso.target);
    }

    [Theory]
    [InlineData(mobjtype_t.MT_BABY, mobjtype_t.MT_ARACHPLAZ)]
    [InlineData(mobjtype_t.MT_CYBORG, mobjtype_t.MT_ROCKET)]
    [InlineData(mobjtype_t.MT_HEAD, mobjtype_t.MT_HEADSHOT)]
    public void MissileMonstersFireTheirMissiles(mobjtype_t monster, mobjtype_t missile)
    {
        World world = Room();
        mobj_t mo = Spawn(world, 400, 128, monster);
        mo.target = Player(world);
        switch (monster)
        {
            case mobjtype_t.MT_BABY:
                world.A_BspiAttack(mo);
                break;
            case mobjtype_t.MT_CYBORG:
                world.A_CyberAttack(mo);
                break;
            default:
                world.A_HeadAttack(mo);
                break;
        }
        Assert.Same(mo, Assert.Single(All(world, missile)).target);
    }

    [Fact]
    public void ACacodemonBitesInMeleeRange()
    {
        World world = Room();
        mobj_t caco = Spawn(world, 64 + 50, 128, mobjtype_t.MT_HEAD);
        caco.target = Player(world);
        world.players[0].mo!.health = world.players[0].health = 200;
        world.A_HeadAttack(caco);
        Assert.InRange(world.players[0].health, 200 - 60, 200 - 10);
        Assert.Empty(All(world, mobjtype_t.MT_HEADSHOT));
    }

    [Fact]
    public void TheWalkersStepSoundsThenChase()
    {
        World world = Room();
        foreach ((mobjtype_t type, sfxenum_t sfx) in new[] { (mobjtype_t.MT_BABY, sfxenum_t.sfx_bspwlk), (mobjtype_t.MT_CYBORG, sfxenum_t.sfx_hoof), (mobjtype_t.MT_SPIDER, sfxenum_t.sfx_metal) })
        {
            mobj_t mo = Spawn(world, 300, 128, type);
            mo.target = Player(world);
            mo.reactiontime = 2;
            if (type == mobjtype_t.MT_BABY)
                world.A_BabyMetal(mo);
            else if (type == mobjtype_t.MT_CYBORG)
                world.A_Hoof(mo);
            else
                world.A_Metal(mo);
            Assert.Contains(world.StartedSounds(), s => s.sfx == sfx && s.origin == mo);
            Assert.Equal(1, mo.reactiontime); // A_Chase ran
            world.P_RemoveMobj(mo);
        }
    }

    // ---- the lost soul and the pain elemental ----

    [Fact]
    public void ALostSoulChargesAtItsTargetsMiddle()
    {
        World world = Room();
        mobj_t skull = Spawn(world, 400, 128, mobjtype_t.MT_SKULL);
        skull.target = Player(world);
        world.A_SkullAttack(skull);
        Assert.True((skull.flags & mobjflag_t.MF_SKULLFLY) != 0);
        Assert.InRange(skull.momx, -20 * FRACUNIT, -20 * FRACUNIT + 64);
        int dist = (World.P_AproxDistance(Player(world).x - skull.x, Player(world).y - skull.y)) / (20 * FRACUNIT);
        Assert.Equal((Player(world).z + (Player(world).height >> 1) - skull.z) / dist, skull.momz);
        Assert.Contains(world.StartedSounds(), s => s.sfx == skull.info.attacksound && s.origin == skull);
    }

    [Theory]
    [InlineData(GameMode.commercial, false)] // Doom II v1.9 (and registered Doom 1.9): no bounce
    [InlineData(GameMode.shareware, false)]
    [InlineData(GameMode.retail, true)] // The Ultimate Doom: bounces, as linuxdoom-1.10
    public void AChargingLostSoulBouncesOffTheFloorOnlyInTheUltimateDoom(GameMode mode, bool bounces)
    {
        World world = Room(mode);
        mobj_t skull = Spawn(world, 300, 128, mobjtype_t.MT_SKULL);
        skull.z = F(4);
        skull.flags |= mobjflag_t.MF_SKULLFLY;
        skull.momz = -F(8);
        world.P_ZMovement(skull);
        Assert.Equal(0, skull.z);
        Assert.Equal(bounces ? F(8) : 0, skull.momz);
    }

    [Fact]
    public void APainElementalSpitsAChargingLostSoulInFront()
    {
        World world = Room();
        mobj_t pain = Spawn(world, 300, 128, mobjtype_t.MT_PAIN);
        pain.target = Player(world);
        world.A_PainAttack(pain);
        mobj_t skull = Assert.Single(All(world, mobjtype_t.MT_SKULL));
        Assert.Same(Player(world), skull.target);
        Assert.True((skull.flags & mobjflag_t.MF_SKULLFLY) != 0);
        // 4 + 3 * (31 + 16) / 2 units in front (towards the player, west), 8 up
        int prestep = 4 * FRACUNIT + 3 * (pain.info.radius + skull.info.radius) / 2;
        Assert.Equal(pain.x + Fixed.FixedMul(prestep, Tables.finecosine[(int)(pain.angle >> Tables.ANGLETOFINESHIFT)]), skull.x);
    }

    [Fact]
    public void APainElementalSpitsNoMoreThanTwentyOneLostSouls()
    {
        World world = Room();
        for (int i = 0; i < 21; i++)
            Spawn(world, 40 + i * 20, 220, mobjtype_t.MT_SKULL);
        mobj_t pain = Spawn(world, 300, 100, mobjtype_t.MT_PAIN);
        pain.target = Player(world);
        world.A_PainAttack(pain); // 21 > 20: none
        Assert.Equal(21, All(world, mobjtype_t.MT_SKULL).Length);
    }

    [Fact]
    public void ALostSoulSpatIntoAWallDiesAtOnce()
    {
        World world = Room();
        mobj_t pain = Spawn(world, 470, 128, mobjtype_t.MT_PAIN);
        pain.target = Player(world);
        world.A_PainShootSkull(pain, 0); // east: into the raised room's wall (64 units up)
        mobj_t skull = Assert.Single(All(world, mobjtype_t.MT_SKULL));
        Assert.True(skull.health <= 0);
        Assert.Null(skull.target);
    }

    [Fact]
    public void ADyingPainElementalSpitsThreeLostSouls()
    {
        World world = Room();
        mobj_t pain = Spawn(world, 300, 128, mobjtype_t.MT_PAIN);
        pain.angle = Tables.ANG180;
        pain.target = Player(world);
        world.A_PainDie(pain);
        Assert.True((pain.flags & mobjflag_t.MF_SOLID) == 0); // A_Fall
        mobj_t[] skulls = All(world, mobjtype_t.MT_SKULL);
        Assert.Equal(3, skulls.Length);
        Assert.Contains(skulls, s => s.y < pain.y && System.Math.Abs(s.x - pain.x) < FRACUNIT); // its left: south
        Assert.Contains(skulls, s => s.x > pain.x); // behind it: east
        Assert.Contains(skulls, s => s.y > pain.y && System.Math.Abs(s.x - pain.x) < FRACUNIT); // its right: north
    }

    // ---- the arch-vile ----

    [Fact]
    public void AnArchVileRaisesACorpseItsNextStepTouches()
    {
        World world = Room();
        mobj_t vile = Spawn(world, 200, 128, mobjtype_t.MT_VILE);
        vile.target = Player(world);
        vile.movedir = (int)dirtype_t.DI_EAST;
        mobj_t imp = Spawn(world, 250, 128, mobjtype_t.MT_TROOP);
        world.P_DamageMobj(imp, null, null, 1000); // gibbed: no raise state on the way
        world.P_SetMobjState(imp, statenum_t.S_TROO_DIE5); // lying still (tics -1)
        Assert.Equal(-1, imp.tics);
        Assert.True((imp.flags & mobjflag_t.MF_CORPSE) != 0);
        int corpseHeight = imp.height;

        world.A_VileChase(vile);

        Assert.Equal(statenum_t.S_VILE_HEAL1, vile.state);
        Assert.Equal(imp.info.raisestate, imp.state);
        Assert.Equal(corpseHeight << 2, imp.height);
        Assert.Equal(imp.info.flags, imp.flags);
        Assert.Equal(imp.info.spawnhealth, imp.health);
        Assert.Null(imp.target);
        Assert.Same(Player(world), vile.target); // only faced the corpse
        Assert.Equal(0u, vile.angle); // facing it, east
        Assert.Contains(world.StartedSounds(), s => s.sfx == sfxenum_t.sfx_slop && s.origin == imp);
    }

    [Fact]
    public void AnArchVileWithoutACorpseInReachChases()
    {
        World world = Room();
        mobj_t vile = Spawn(world, 200, 128, mobjtype_t.MT_VILE);
        vile.target = Player(world);
        vile.movedir = (int)dirtype_t.DI_EAST;
        vile.reactiontime = 3;
        mobj_t imp = Spawn(world, 400, 128, mobjtype_t.MT_TROOP); // too far
        world.P_DamageMobj(imp, null, null, 1000);
        world.P_SetMobjState(imp, statenum_t.S_TROO_DIE5);
        world.A_VileChase(vile);
        Assert.NotEqual(statenum_t.S_VILE_HEAL1, vile.state);
        Assert.Equal(2, vile.reactiontime); // A_Chase
        Assert.True(imp.health <= 0);
    }

    [Fact]
    public void AnArchVilesFireSpawnsAtTargetXTwiceThenKeepsInFrontOfIt()
    {
        World world = Room();
        mobj_t vile = Spawn(world, 300, 128, mobjtype_t.MT_VILE);
        mobj_t me = Player(world);
        vile.target = me;
        world.A_VileTarget(vile);
        mobj_t fire = Assert.Single(All(world, mobjtype_t.MT_FIRE));
        Assert.Same(fire, vile.tracer);
        Assert.Same(vile, fire.target);
        Assert.Same(me, fire.tracer);
        // spawned at (target x, target x), vanilla's typo; A_Fire moves it 24 units in front of the player at once
        uint an = me.angle >> Tables.ANGLETOFINESHIFT;
        Assert.Equal(me.x + Fixed.FixedMul(24 * FRACUNIT, Tables.finecosine[(int)an]), fire.x);
        Assert.Equal(me.y + Fixed.FixedMul(24 * FRACUNIT, Tables.finesine[(int)an]), fire.y);
    }

    [Fact]
    public void AnArchVilesBlastHurtsThrowsAndExplodesItsFire()
    {
        World world = Room();
        mobj_t vile = Spawn(world, 300, 128, mobjtype_t.MT_VILE);
        mobj_t me = Player(world);
        world.players[0].mo!.health = world.players[0].health = 200;
        vile.target = me;
        world.A_VileTarget(vile);
        world.A_VileAttack(vile);
        Assert.Contains(world.StartedSounds(), s => s.sfx == sfxenum_t.sfx_barexp && s.origin == vile);
        Assert.Equal(1000 * FRACUNIT / me.info.mass, me.momz);
        // 20, and the fire's 70 blast 24 units off: 70 - (24 - 16) = 62 (the player's radius)
        Assert.InRange(world.players[0].health, 200 - 20 - 70, 200 - 20 - 50);
        mobj_t fire = vile.tracer!;
        Assert.Equal(me.x - Fixed.FixedMul(24 * FRACUNIT, Tables.finecosine[(int)(vile.angle >> Tables.ANGLETOFINESHIFT)]), fire.x);
    }

    // ---- Commander Keen ----

    [Fact]
    public void TheLastKeensDeathOpensTheDoorsTagged666()
    {
        World world = Room();
        mobj_t a = Spawn(world, 300, 128, mobjtype_t.MT_KEEN);
        mobj_t b = Spawn(world, 300, 64, mobjtype_t.MT_KEEN);
        a.health = 0;
        world.A_KeenDie(a);
        Assert.True((a.flags & mobjflag_t.MF_SOLID) == 0);
        Assert.Null(world.sectors[1].specialdata); // the other lives
        b.health = 0;
        world.A_KeenDie(b);
        vldoor_t door = Assert.IsType<vldoor_t>(world.sectors[1].specialdata);
        Assert.Equal(vldoor_e.vld_open, door.type);
    }
}
