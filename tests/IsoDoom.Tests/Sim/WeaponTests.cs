using System;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T6.6: the player's weapons (p_pspr.c: the psprite state machine,
/// <c>P_BringUpWeapon</c>, <c>P_CheckAmmo</c>, <c>P_FireWeapon</c>, the
/// weapon actions; p_user.c's weapon change from <c>BT_CHANGE</c>) on
/// synthetic <see cref="TestMap"/>s. The vanilla routes <c>testmap-weapons</c>
/// (every shareware weapon) and <c>e1m1-weapons</c> (<see cref="VanillaRouteTests"/>)
/// check them against vanilla tic by tic, and every route checks the
/// psprites (the dump's <c>weapon</c> column).
/// </summary>
public class WeaponTests
{
    private const int FRACUNIT = 1 << 16;

    private static int F(int units) => units * FRACUNIT;

    private static player_t P(World world) => world.players[0];

    private static pspdef_t Weapon(World world) => P(world).psprites[World.ps_weapon];

    private static pspdef_t Flash(World world) => P(world).psprites[World.ps_flash];

    private static void Tics(World world, int n, byte buttons = 0)
    {
        for (int i = 0; i < n; i++)
            world.G_Ticker(new ticcmd_t { buttons = buttons });
    }

    private static byte Change(weapontype_t weapon) =>
        (byte)(buttoncode_t.BT_CHANGE | ((int)weapon << buttoncode_t.BT_WEAPONSHIFT));

    /// <summary>One room, x 0..512, y 0..256, floor 0, ceiling 128; the player at (64, 128) facing east.</summary>
    private static TestMap OneRoom() => TestMap.Strip(0, 0, 256, new TestMap.Room(512, 0, 128)).Player(64, 128);

    private static World Load(TestMap map, GameMode mode = GameMode.shareware)
    {
        var wad = new WadArchive(new[] { WadFile.FromBytes(map.Build(), "testmap.wad") });
        var world = new World(new SpawnSettings(mode, skill_t.sk_medium), Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(wad, "E1M1"));
        return world;
    }

    /// <summary>Tics until <paramref name="weapon"/> is up and ready (fails after 100).</summary>
    private static void UntilReady(World world, weapontype_t weapon)
    {
        for (int i = 0; i < 100; i++)
        {
            if (P(world).readyweapon == weapon && P(world).pendingweapon == weapontype_t.wp_nochange
                && Weapon(world).state == Info.weaponinfo[(int)weapon].readystate)
                return;
            Tics(world, 1);
        }
        Assert.Fail($"{weapon} never came up (now {P(world).readyweapon}, {Weapon(world).state})");
    }

    /// <summary>A world with <paramref name="weapon"/> owned, <paramref name="ammo"/> of its ammo, up and ready.</summary>
    private static World Armed(weapontype_t weapon, int ammo, TestMap? map = null)
    {
        World world = Load(map ?? OneRoom());
        player_t p = P(world);
        p.weaponowned[(int)weapon] = true;
        if (Info.weaponinfo[(int)weapon].ammo != ammotype_t.am_noammo)
            p.ammo[(int)Info.weaponinfo[(int)weapon].ammo] = ammo;
        UntilReady(world, weapontype_t.wp_pistol);
        if (weapon != weapontype_t.wp_pistol)
        {
            p.pendingweapon = weapon;
            UntilReady(world, weapon);
        }
        return world;
    }

    /// <summary>
    /// A zombieman spawned at (<paramref name="x"/>, <paramref name="y"/>) now: its
    /// first <c>A_Look</c> comes after its spawn state's 10 tics, so it stands
    /// still for a test's few tics (a map thing near the player would have
    /// seen it while the weapon came up).
    /// </summary>
    private static mobj_t Zombie(World world, int x, int y) =>
        world.P_SpawnMobj(F(x), F(y), World.ONFLOORZ, mobjtype_t.MT_POSSESSED);

    /// <summary>
    /// Holds fire until the chainsaw's first stroke (S_SAW1): its ready states
    /// last 4 tics, and only entering one (<c>A_WeaponReady</c>) fires.
    /// </summary>
    private static void SawStroke(World world)
    {
        for (int i = 0; i < 4 && Weapon(world).state != statenum_t.S_SAW1; i++)
            Tics(world, 1, buttoncode_t.BT_ATTACK);
        Assert.Equal(statenum_t.S_SAW1, Weapon(world).state);
    }

    // ---- bringing weapons up ----

    [Fact]
    public void ThePistolComesUpAtTheStartOfALevel()
    {
        World world = Load(OneRoom());
        player_t p = P(world);
        // P_SpawnPlayer's P_SetupPsprites: S_PISTOLUP from the bottom, A_Raise at once
        Assert.Equal((weapontype_t.wp_pistol, weapontype_t.wp_nochange), (p.readyweapon, p.pendingweapon));
        Assert.Equal((statenum_t.S_PISTOLUP, World.WEAPONBOTTOM - World.RAISESPEED), (Weapon(world).state, Weapon(world).sy));
        Assert.Equal(statenum_t.S_NULL, Flash(world).state);
        Tics(world, 14);
        Assert.Equal(statenum_t.S_PISTOLUP, Weapon(world).state);
        Tics(world, 1); // 6 units a tic: up after 15 tics
        Assert.Equal((statenum_t.S_PISTOL, World.WEAPONTOP), (Weapon(world).state, Weapon(world).sy));
        Assert.Equal(World.WEAPONTOP, Flash(world).sy); // the flash follows the weapon
    }

    [Fact]
    public void TheReadyWeaponBobsWithTheMovement()
    {
        World world = Armed(weapontype_t.wp_pistol, 50);
        Tics(world, 8, 0);
        world.G_Ticker(new ticcmd_t { forwardmove = 50 });
        world.G_Ticker(new ticcmd_t { forwardmove = 50 });
        player_t p = P(world);
        Assert.NotEqual(0, p.bob);
        int angle = (128 * (world.leveltime - 1)) & Tables.FINEMASK; // (leveltime counts at the end of the tic)
        Assert.Equal(FRACUNIT + Fixed.FixedMul(p.bob, Tables.finecosine[angle]), Weapon(world).sx);
        Assert.Equal(World.WEAPONTOP + Fixed.FixedMul(p.bob, Tables.finesine[angle & (Tables.FINEANGLES / 2 - 1)]), Weapon(world).sy);
    }

    // ---- firing ----

    [Fact]
    public void ThePistolFiresAfterFourTicsWithAFlashAndANoise()
    {
        World world = Armed(weapontype_t.wp_pistol, 50);
        player_t p = P(world);
        mobj_t mo = p.mo!;
        world.events.Clear();
        Tics(world, 1, buttoncode_t.BT_ATTACK);
        // P_FireWeapon: the player's attack frame, S_PISTOL1, the noise
        Assert.Equal((statenum_t.S_PLAY_ATK1, statenum_t.S_PISTOL1, 50), (mo.state, Weapon(world).state, p.ammo[0]));
        Assert.Same(mo, mo.subsector.sector.soundtarget);
        Assert.True(p.attackdown);
        Tics(world, 3, buttoncode_t.BT_ATTACK);
        Assert.Equal(50, p.ammo[0]);
        Tics(world, 1, buttoncode_t.BT_ATTACK);
        // A_FirePistol: a clip, the flash (A_Light1), the firing frame, the sound, a puff on the far wall
        Assert.Equal((statenum_t.S_PLAY_ATK2, statenum_t.S_PISTOL2, statenum_t.S_PISTOLFLASH, 49, 1),
            (mo.state, Weapon(world).state, Flash(world).state, p.ammo[0], p.extralight));
        Assert.Contains(world.StartedSounds(), s => s.sfx == sfxenum_t.sfx_pistol && s.origin == mo);
        Assert.Contains(world.Mobjs(), m => m.type == mobjtype_t.MT_PUFF);
        // the flash's 7 tics (counted from its first: P_MovePsprites moves it after the weapon), then S_LIGHTDONE's A_Light0 and nothing
        Tics(world, 5);
        Assert.Equal((statenum_t.S_PISTOLFLASH, 1), (Flash(world).state, p.extralight));
        Tics(world, 1);
        Assert.Equal((statenum_t.S_NULL, 0), (Flash(world).state, p.extralight));
        // released: back to ready, the player's frame back to S_PLAY
        UntilReady(world, weapontype_t.wp_pistol);
        Tics(world, 1);
        Assert.Equal(statenum_t.S_PLAY, mo.state);
        Assert.False(p.attackdown);
        Assert.Equal(0, p.refire);
    }

    [Fact]
    public void HoldingFireRefiresAndCountsRefire()
    {
        World world = Armed(weapontype_t.wp_pistol, 50);
        player_t p = P(world);
        Tics(world, 1 + 4 + 6 + 4, buttoncode_t.BT_ATTACK); // a whole cycle: entering S_PISTOL4, A_ReFire fires again
        Assert.Equal((1, statenum_t.S_PISTOL1, 49), (p.refire, Weapon(world).state, p.ammo[0]));
        Tics(world, 4, buttoncode_t.BT_ATTACK);
        Assert.Equal(48, p.ammo[0]);
        Tics(world, 15); // released: A_ReFire resets refire
        Assert.Equal(0, p.refire);
    }

    [Fact]
    public void TheShotgunFiresSevenPellets()
    {
        World world = Armed(weapontype_t.wp_shotgun, 10);
        player_t p = P(world);
        int before = world.random.prndindex;
        Tics(world, 1 + 3, buttoncode_t.BT_ATTACK); // S_SGUN1 (3 tics), then S_SGUN2's A_FireShotgun
        Assert.Equal((9, statenum_t.S_SGUNFLASH1, 1), (p.ammo[1], Flash(world).state, p.extralight));
        // 7 pellets of damage + spread (3 P_Random each), and their puffs (P_SpawnPuff: 2 each, 1 for the tics)
        Assert.Equal(7, world.Mobjs().Count(m => m.type == mobjtype_t.MT_PUFF));
        Assert.True(((world.random.prndindex - before) & 0xff) >= 7 * 3);
    }

    [Fact]
    public void TheChaingunsFlashFollowsItsFrame()
    {
        World world = Armed(weapontype_t.wp_chaingun, 20);
        player_t p = P(world);
        Tics(world, 1, buttoncode_t.BT_ATTACK); // S_CHAIN1: A_FireCGun at once
        Assert.Equal((statenum_t.S_CHAIN1, statenum_t.S_CHAINFLASH1, 19, 1), (Weapon(world).state, Flash(world).state, p.ammo[0], p.extralight));
        Tics(world, 4, buttoncode_t.BT_ATTACK); // S_CHAIN2: the second shot, the second flash
        Assert.Equal((statenum_t.S_CHAIN2, statenum_t.S_CHAINFLASH2, 18, 2), (Weapon(world).state, Flash(world).state, p.ammo[0], p.extralight));
    }

    [Fact]
    public void TheChaingunWithoutAmmoOnlyClicks()
    {
        World world = Armed(weapontype_t.wp_chaingun, 1);
        player_t p = P(world);
        Tics(world, 1, buttoncode_t.BT_ATTACK);
        Assert.Equal(0, p.ammo[0]);
        world.events.Clear();
        world.A_FireCGun(p, Weapon(world)); // S_CHAIN2's call with nothing left: the sound alone
        Assert.Equal((0, statenum_t.S_CHAINFLASH1), (p.ammo[0], Flash(world).state));
        Assert.Contains(world.StartedSounds(), s => s.sfx == sfxenum_t.sfx_pistol);
    }

    [Fact]
    public void TheRocketLauncherDoesNotFireAgainUntilFireIsReleased()
    {
        World world = Armed(weapontype_t.wp_missile, 5);
        player_t p = P(world);
        p.attackdown = true; // fire held since before the launcher was ready
        Tics(world, 10, buttoncode_t.BT_ATTACK);
        Assert.Equal((5, statenum_t.S_MISSILE), (p.ammo[3], Weapon(world).state));
        Tics(world, 1);
        Tics(world, 1, buttoncode_t.BT_ATTACK); // a new press
        Assert.Equal(statenum_t.S_MISSILE1, Weapon(world).state);
        Tics(world, 8, buttoncode_t.BT_ATTACK); // S_MISSILE1's 8 tics (A_GunFlash), then S_MISSILE2's A_FireMissile
        Assert.Equal(4, p.ammo[3]);
        Assert.Contains(world.Mobjs(), m => m.type == mobjtype_t.MT_ROCKET);
        Assert.Contains(p.mo!.state, new[] { statenum_t.S_PLAY_ATK1, statenum_t.S_PLAY_ATK2 }); // (they alternate until A_WeaponReady)
    }

    [Fact]
    public void RunningOutOfAmmoChangesToTheBestWeaponLeft()
    {
        World world = Armed(weapontype_t.wp_pistol, 1);
        player_t p = P(world);
        p.weaponowned[(int)weapontype_t.wp_shotgun] = true;
        p.ammo[1] = 4;
        Tics(world, 1 + 4 + 6 + 4, buttoncode_t.BT_ATTACK); // the shot, then A_ReFire: no bullet left
        Assert.Equal((0, weapontype_t.wp_shotgun, statenum_t.S_PISTOLDOWN), (p.ammo[0], p.pendingweapon, Weapon(world).state));
        UntilReady(world, weapontype_t.wp_shotgun);

        // nothing at all left: the fist
        World bare = Armed(weapontype_t.wp_pistol, 0);
        Tics(bare, 1, buttoncode_t.BT_ATTACK);
        Assert.Equal(weapontype_t.wp_fist, P(bare).pendingweapon);
        UntilReady(bare, weapontype_t.wp_fist);
    }

    [Fact]
    public void CheckAmmoPreferences()
    {
        World world = Load(OneRoom());
        player_t p = P(world);
        p.readyweapon = weapontype_t.wp_shotgun;
        p.ammo[0] = 0;
        Array.Fill(p.weaponowned, true);
        p.ammo[(int)ammotype_t.am_cell] = 100;
        p.ammo[(int)ammotype_t.am_misl] = 3;
        Weapon(world).sy = World.WEAPONTOP; // (P_CheckAmmo lowers the weapon: A_Lower changes at once from the bottom)
        Assert.False(world.P_CheckAmmo(p));
        Assert.Equal(weapontype_t.wp_chainsaw, p.pendingweapon); // shareware: no plasma; no clips; the saw before the launcher
        p.weaponowned[(int)weapontype_t.wp_chainsaw] = false;
        Weapon(world).sy = World.WEAPONTOP;
        world.P_CheckAmmo(p);
        Assert.Equal(weapontype_t.wp_missile, p.pendingweapon);
        p.ammo[(int)ammotype_t.am_misl] = 0;
        Weapon(world).sy = World.WEAPONTOP;
        world.P_CheckAmmo(p);
        Assert.Equal(weapontype_t.wp_fist, p.pendingweapon); // the BFG neither in shareware
        p.ammo[1] = 1;
        Assert.True(world.P_CheckAmmo(p));

        World doom2 = Load(OneRoom(), GameMode.commercial);
        player_t q = P(doom2);
        Array.Fill(q.weaponowned, true);
        q.readyweapon = weapontype_t.wp_supershotgun;
        q.ammo[1] = 1; // the super shotgun needs 2
        q.ammo[(int)ammotype_t.am_cell] = 1;
        Weapon(doom2).sy = World.WEAPONTOP;
        Assert.False(doom2.P_CheckAmmo(q));
        Assert.Equal(weapontype_t.wp_plasma, q.pendingweapon);
    }

    // ---- melee ----

    [Fact]
    public void TheFistPunchesFacesTheTargetAndBerserkKills()
    {
        World world = Armed(weapontype_t.wp_fist, 0);
        player_t p = P(world);
        mobj_t zombie = Zombie(world, 110, 140);
        world.events.Clear();
        Tics(world, 1 + 4, buttoncode_t.BT_ATTACK); // S_PUNCH1 (4 tics), then S_PUNCH2's A_Punch
        Assert.True(zombie.health < 20);
        Assert.Contains(world.StartedSounds(), s => s.sfx == sfxenum_t.sfx_punch);
        Assert.Equal(Tables.R_PointToAngle2(p.mo!.x, p.mo.y, zombie.x, zombie.y), p.mo.angle);
        Assert.Contains(world.Mobjs(), m => m.type == mobjtype_t.MT_BLOOD);

        World berserk = Armed(weapontype_t.wp_fist, 0);
        mobj_t victim = Zombie(berserk, 110, 128);
        P(berserk).powers[(int)powertype_t.pw_strength] = 1;
        Tics(berserk, 1 + 4, buttoncode_t.BT_ATTACK);
        Assert.True(victim.health <= 0); // at least 2 × 10
    }

    [Fact]
    public void ThePunchMissesBeyondMeleeRange()
    {
        World world = Armed(weapontype_t.wp_fist, 0);
        mobj_t zombie = Zombie(world, 64 + 16 + 20 + 70, 128);
        world.events.Clear();
        Tics(world, 1 + 4, buttoncode_t.BT_ATTACK);
        Assert.Equal(20, zombie.health);
        Assert.DoesNotContain(world.StartedSounds(), s => s.sfx == sfxenum_t.sfx_punch);
    }

    [Fact]
    public void TheChainsawTurnsTowardsItsTargetAndPullsForward()
    {
        World world = Armed(weapontype_t.wp_chainsaw, 0);
        player_t p = P(world);
        mobj_t mo = p.mo!;
        mobj_t zombie = Zombie(world, 114, 136);
        SawStroke(world); // S_SAW1: A_Saw at once
        Assert.True(zombie.health < 20);
        Assert.Contains(world.StartedSounds(), s => s.sfx == sfxenum_t.sfx_sawhit);
        uint toTarget = Tables.R_PointToAngle2(mo.x, mo.y, zombie.x, zombie.y); // about 9 degrees left
        Assert.True(toTarget > Tables.ANG90 / 20);
        Assert.Equal(unchecked(toTarget - Tables.ANG90 / 21), mo.angle); // more than ANG90/20 away: just short of it
        Assert.NotEqual((mobjflag_t)0, mo.flags & mobjflag_t.MF_JUSTATTACKED);
        Tics(world, 1); // P_PlayerThink's run forward
        Assert.Equal((mobjflag_t)0, mo.flags & mobjflag_t.MF_JUSTATTACKED);
        Assert.True(mo.momx > 0);
    }

    [Fact]
    public void TheChainsawTurnsByAtMostANG90Over20AndIdlesWithASound()
    {
        World world = Armed(weapontype_t.wp_chainsaw, 0);
        mobj_t mo = P(world).mo!;
        mobj_t zombie = Zombie(world, 114, 129);
        uint before = mo.angle;
        world.random.prndindex = 0; // a known spread
        SawStroke(world);
        uint toTarget = Tables.R_PointToAngle2(mo.x, mo.y, zombie.x, zombie.y);
        Assert.True(toTarget < Tables.ANG90 / 20); // within the step: one step past the target
        Assert.Equal(unchecked(before + Tables.ANG90 / 20), mo.angle);

        World idle = Armed(weapontype_t.wp_chainsaw, 0);
        bool idled = false;
        for (int i = 0; i < 8; i++)
        {
            Tics(idle, 1);
            idled |= idle.StartedSounds().Any(s => s.sfx == sfxenum_t.sfx_sawidl);
        }
        Assert.True(idled);
        SawStroke(idle);
        Assert.Contains(idle.StartedSounds(), s => s.sfx == sfxenum_t.sfx_sawful); // nothing in reach
    }

    [Fact]
    public void BringingUpTheChainsawRevsIt()
    {
        World world = Armed(weapontype_t.wp_pistol, 50);
        P(world).weaponowned[(int)weapontype_t.wp_chainsaw] = true;
        Tics(world, 1, Change(weapontype_t.wp_fist)); // key 1 with a chainsaw: the chainsaw
        Assert.Equal(weapontype_t.wp_chainsaw, P(world).pendingweapon);
        while (P(world).readyweapon != weapontype_t.wp_chainsaw)
            Tics(world, 1);
        Assert.Contains(world.StartedSounds(), s => s.sfx == sfxenum_t.sfx_sawup); // P_BringUpWeapon, in the tic it changed
        UntilReady(world, weapontype_t.wp_chainsaw);
    }

    // ---- changing weapons ----

    [Fact]
    public void TheWeaponKeysChangeToOwnedWeapons()
    {
        World world = Armed(weapontype_t.wp_pistol, 50);
        player_t p = P(world);
        Tics(world, 1, Change(weapontype_t.wp_shotgun)); // not owned
        Assert.Equal(weapontype_t.wp_nochange, p.pendingweapon);
        Tics(world, 1, Change(weapontype_t.wp_pistol)); // already ready
        Assert.Equal(weapontype_t.wp_nochange, p.pendingweapon);
        Tics(world, 1, Change(weapontype_t.wp_fist));
        // A_WeaponReady lowers the pistol at once
        Assert.Equal((weapontype_t.wp_fist, statenum_t.S_PISTOLDOWN), (p.pendingweapon, Weapon(world).state));
        Tics(world, 14); // 6 units a tic from the top, the first in A_WeaponReady's tic
        Assert.Equal(weapontype_t.wp_pistol, p.readyweapon);
        Tics(world, 1);
        Assert.Equal((weapontype_t.wp_fist, weapontype_t.wp_nochange, statenum_t.S_PUNCHUP), (p.readyweapon, p.pendingweapon, Weapon(world).state));
        UntilReady(world, weapontype_t.wp_fist);
    }

    [Fact]
    public void TheFistKeyKeepsTheFistWithBerserkAndTheChainsawReady()
    {
        World world = Armed(weapontype_t.wp_chainsaw, 0);
        player_t p = P(world);
        p.powers[(int)powertype_t.pw_strength] = 1;
        Tics(world, 1, Change(weapontype_t.wp_fist));
        Assert.Equal(weapontype_t.wp_fist, p.pendingweapon);
    }

    [Fact]
    public void PlasmaAndBfgKeysDoNothingInShareware()
    {
        World world = Armed(weapontype_t.wp_pistol, 50);
        player_t p = P(world);
        p.weaponowned[(int)weapontype_t.wp_plasma] = p.weaponowned[(int)weapontype_t.wp_bfg] = true;
        Tics(world, 1, Change(weapontype_t.wp_plasma));
        Tics(world, 1, Change(weapontype_t.wp_bfg));
        Assert.Equal(weapontype_t.wp_nochange, p.pendingweapon);

        World doom2 = Load(OneRoom(), GameMode.commercial);
        UntilReady(doom2, weapontype_t.wp_pistol);
        player_t q = P(doom2);
        q.weaponowned[(int)weapontype_t.wp_plasma] = true;
        Tics(doom2, 1, Change(weapontype_t.wp_plasma));
        Assert.Equal(weapontype_t.wp_plasma, q.pendingweapon);
    }

    [Fact]
    public void TheShotgunKeyPicksTheSuperShotgunInDoom2()
    {
        World doom2 = Load(OneRoom(), GameMode.commercial);
        UntilReady(doom2, weapontype_t.wp_pistol);
        player_t q = P(doom2);
        q.weaponowned[(int)weapontype_t.wp_shotgun] = q.weaponowned[(int)weapontype_t.wp_supershotgun] = true;
        Tics(doom2, 1, Change(weapontype_t.wp_shotgun));
        Assert.Equal(weapontype_t.wp_supershotgun, q.pendingweapon);

        World doom1 = Armed(weapontype_t.wp_pistol, 50);
        P(doom1).weaponowned[(int)weapontype_t.wp_shotgun] = P(doom1).weaponowned[(int)weapontype_t.wp_supershotgun] = true;
        Tics(doom1, 1, Change(weapontype_t.wp_shotgun));
        Assert.Equal(weapontype_t.wp_shotgun, P(doom1).pendingweapon);
    }

    [Fact]
    public void AChangeWaitsForTheShotAndStopsTheRefire()
    {
        World world = Armed(weapontype_t.wp_pistol, 50);
        player_t p = P(world);
        p.weaponowned[(int)weapontype_t.wp_fist] = true;
        Tics(world, 1, buttoncode_t.BT_ATTACK);
        Tics(world, 1, (byte)(buttoncode_t.BT_ATTACK | Change(weapontype_t.wp_fist)));
        Assert.Equal((weapontype_t.wp_fist, statenum_t.S_PISTOL1), (p.pendingweapon, Weapon(world).state));
        Tics(world, 3 + 6 + 4 + 5, buttoncode_t.BT_ATTACK); // the shot goes; A_ReFire lets the change through
        Assert.Equal((49, 0, statenum_t.S_PISTOLDOWN), (p.ammo[0], p.refire, Weapon(world).state));
        UntilReady(world, weapontype_t.wp_fist);
    }

    [Fact]
    public void ASpecialEventHasNoOtherButtons()
    {
        World world = Armed(weapontype_t.wp_pistol, 50);
        player_t p = P(world);
        Tics(world, 1, (byte)(buttoncode_t.BT_SPECIAL | buttoncode_t.BT_ATTACK | buttoncode_t.BTS_PAUSE));
        Assert.Equal((statenum_t.S_PISTOL, 0), (Weapon(world).state, (int)p.cmd.buttons));
        Assert.Equal(statenum_t.S_PISTOL, Weapon(world).state);
    }

    // ---- death and levels ----

    [Fact]
    public void ADeadPlayersWeaponGoesDownAndStays()
    {
        World world = Armed(weapontype_t.wp_pistol, 50);
        player_t p = P(world);
        world.P_DamageMobj(p.mo!, null, null, 1000);
        Assert.Equal(playerstate_t.PST_DEAD, p.playerstate);
        Assert.Equal(statenum_t.S_PISTOLDOWN, Weapon(world).state); // P_KillMobj's P_DropWeapon
        // A_Lower at the bottom: a dead player's weapon stays there, in its state
        Weapon(world).sy = World.WEAPONBOTTOM;
        world.A_Lower(p, Weapon(world));
        Assert.Equal((statenum_t.S_PISTOLDOWN, World.WEAPONBOTTOM), (Weapon(world).state, Weapon(world).sy));
        // no health but still live (the tic it died): off
        p.playerstate = playerstate_t.PST_LIVE;
        world.A_Lower(p, Weapon(world));
        Assert.Equal(statenum_t.S_NULL, Weapon(world).state);
    }

    [Fact]
    public void ANewLevelBringsTheReadyWeaponUpAgain()
    {
        World world = Armed(weapontype_t.wp_shotgun, 8);
        world.G_PlayerFinishLevel(0);
        world.G_DoLoadLevel(world.level!);
        player_t p = P(world);
        Assert.Equal((weapontype_t.wp_shotgun, statenum_t.S_SGUNUP, statenum_t.S_NULL), (p.readyweapon, Weapon(world).state, Flash(world).state));
        UntilReady(world, weapontype_t.wp_shotgun);
    }

    [Fact]
    public void TheChecksumCoversThePsprites()
    {
        World a = Armed(weapontype_t.wp_pistol, 50), b = Armed(weapontype_t.wp_pistol, 50);
        Assert.Equal(a.Checksum(), b.Checksum());
        Weapon(b).sy++;
        Assert.NotEqual(a.Checksum(), b.Checksum());
        Weapon(b).sy--;
        P(b).extralight = 1;
        Assert.NotEqual(a.Checksum(), b.Checksum());
    }
}
