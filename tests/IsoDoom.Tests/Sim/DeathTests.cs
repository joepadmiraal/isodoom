using System;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T6.12: p_user.c <c>P_DeathThink</c> (the view falls, the dead player
/// turns towards its killer, the damage flash fades once it faces it, use
/// reborns) and g_game.c's single-player reborn (<c>G_DoReborn</c>:
/// <see cref="gameaction_t.ga_loadlevel"/>, the level reloaded with a fresh
/// player, <c>G_PlayerReborn</c>). The routes <c>testmap-death</c> and
/// <c>e1m8-death</c> check the same against vanilla tic by tic.
/// </summary>
public class DeathTests
{
    private const int FRACUNIT = 1 << 16;

    private static int F(int units) => units * FRACUNIT;

    private static player_t P(World world) => world.players[0];

    /// <summary>One room, x 0..512, y 0..512, floor 0, ceiling 128; the player at (64, 128) facing east.</summary>
    private static (World World, WadArchive Wad) Load(bool netgame = false)
    {
        TestMap map = TestMap.Strip(0, 0, 512, new TestMap.Room(512, 0, 128)).Player(64, 128);
        var wad = new WadArchive(new[] { WadFile.FromBytes(map.Build(), "testmap.wad") });
        var world = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium, netgame: netgame), Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(wad, "E1M1"));
        return (world, wad);
    }

    private static void Tic(World world, byte buttons = 0, sbyte forward = 0) =>
        world.G_Ticker(new ticcmd_t { buttons = buttons, forwardmove = forward });

    [Fact]
    public void TheDeadPlayersViewFallsAndItTurnsTowardsItsKiller()
    {
        (World world, _) = Load();
        player_t p = P(world);
        mobj_t mo = p.mo!;
        // An imp due north (R_PointToAngle2: ANG90 - 1): 18 turns of ANG5 (18 × ANG5 is ANG90 - 2), then the snap.
        // (No inflictor: no thrust, so the corpse lies still.)
        mobj_t imp = world.P_SpawnMobj(F(64), F(448), World.ONFLOORZ, mobjtype_t.MT_TROOP);
        world.P_DamageMobj(mo, null, imp, 1000);
        Assert.Equal(playerstate_t.PST_DEAD, p.playerstate);
        Assert.Same(imp, p.attacker);
        Assert.Equal(100, p.damagecount);
        int x = mo.x, y = mo.y;

        uint angle = 0;
        int viewheight = player_t.VIEWHEIGHT;
        for (int tic = 1; tic <= 60; tic++)
        {
            Tic(world, forward: 50); // a dead player does not move
            viewheight = Math.Max(viewheight - FRACUNIT, F(6));
            if (tic <= 18)
            {
                angle += World.ANG5;
                Assert.Equal(100, p.damagecount); // not facing the killer yet: the flash stays
            }
            else
            {
                angle = Tables.R_PointToAngle2(mo.x, mo.y, imp.x, imp.y);
                Assert.Equal(100 - (tic - 18), p.damagecount); // facing it: the flash fades
            }
            Assert.Equal(angle, mo.angle);
            Assert.Equal(viewheight, p.viewheight);
            Assert.Equal(0, p.deltaviewheight);
            Assert.Equal(mo.z + viewheight, p.viewz); // no momentum: no bob
        }
        Assert.Equal((x, y), (mo.x, mo.y));
        Assert.Equal(playerstate_t.PST_DEAD, p.playerstate);
        Assert.Equal(gameaction_t.ga_nothing, world.gameaction);
    }

    [Fact]
    public void TheDeadPlayerTurnsTheShortWay()
    {
        (World world, _) = Load();
        mobj_t mo = P(world).mo!;
        // facing east, the killer south-east: clockwise (the angle goes down)
        mobj_t imp = world.P_SpawnMobj(F(192), F(0) + F(1), World.ONFLOORZ, mobjtype_t.MT_TROOP);
        world.P_DamageMobj(mo, imp, imp, 1000);
        Tic(world);
        Assert.Equal(unchecked(0u - World.ANG5), mo.angle);
    }

    [Theory]
    [InlineData(false)] // no attacker (a damage floor, a crusher)
    [InlineData(true)] // the player itself (its own rocket's splash)
    public void WithoutAnotherKillerTheFlashFadesAtOnce(bool self)
    {
        (World world, _) = Load();
        player_t p = P(world);
        mobj_t mo = p.mo!;
        world.P_DamageMobj(mo, null, self ? mo : null, 30);
        world.P_DamageMobj(mo, null, self ? mo : null, 1000);
        Assert.Equal(playerstate_t.PST_DEAD, p.playerstate);
        uint angle = mo.angle;
        for (int tic = 1; tic <= 10; tic++)
        {
            Tic(world);
            Assert.Equal(100 - tic, p.damagecount);
            Assert.Equal(angle, mo.angle);
        }
    }

    [Fact]
    public void TheDeadPlayersWeaponGoesOnLowering()
    {
        (World world, _) = Load();
        player_t p = P(world);
        for (int i = 0; i < 40; i++)
            Tic(world); // the pistol up and ready
        pspdef_t weapon = p.psprites[World.ps_weapon];
        world.P_DamageMobj(p.mo!, null, null, 1000);
        Assert.Equal(statenum_t.S_PISTOLDOWN, weapon.state); // P_KillMobj's P_DropWeapon
        int sy = weapon.sy;
        Tic(world);
        Assert.Equal(sy + World.LOWERSPEED, weapon.sy); // P_DeathThink's P_MovePsprites: A_Lower
        for (int i = 0; i < 20; i++)
            Tic(world);
        Assert.Equal((statenum_t.S_PISTOLDOWN, World.WEAPONBOTTOM), (weapon.state, weapon.sy)); // down, and stays
    }

    [Fact]
    public void UseRebornsWithAFreshPlayerOnTheReloadedLevel()
    {
        (World world, WadArchive wad) = Load();
        int loadRandoms = world.random.prndindex; // the P_Random calls of loading the map
        player_t p = P(world);
        mobj_t old = p.mo!;
        // what a reborn takes away: weapons, ammo, armor, keys, powers, the level's counts
        p.weaponowned[(int)weapontype_t.wp_shotgun] = true;
        p.readyweapon = weapontype_t.wp_shotgun;
        p.ammo[(int)ammotype_t.am_shell] = 20;
        p.ammo[(int)ammotype_t.am_clip] = 120;
        p.backpack = true;
        p.armorpoints = 150;
        p.armortype = 2;
        p.cards[(int)card_t.it_bluecard] = true;
        p.powers[(int)powertype_t.pw_ironfeet] = 100;
        p.killcount = 3;
        p.itemcount = 2;
        p.secretcount = 1;
        p.didsecret = true;
        world.P_DamageMobj(old, null, null, 1000);
        Tic(world);
        Tic(world);
        Assert.Equal(gameaction_t.ga_nothing, world.gameaction);

        Tic(world, buttons: (byte)buttoncode_t.BT_USE);
        Assert.Equal(playerstate_t.PST_REBORN, p.playerstate);
        Assert.Equal(gameaction_t.ga_loadlevel, world.gameaction); // G_DoReborn, at the tic's end
        int rnd = world.random.prndindex;
        int gametic = world.gametic;

        // the game loop's ga_loadlevel: the same map afresh
        world.G_DoLoadLevel(Level.Load(wad, "E1M1"));
        Assert.Equal(gameaction_t.ga_nothing, world.gameaction);
        Assert.Same(p, world.players[0]);
        Assert.NotSame(old, p.mo);
        Assert.Equal((F(64), F(128), 0u), (p.mo!.x, p.mo.y, p.mo.angle));
        Assert.Equal(playerstate_t.PST_LIVE, p.playerstate);
        Assert.Equal((100, 100), (p.health, p.mo.health));
        Assert.Equal((0, 0), (p.armorpoints, p.armortype));
        Assert.Equal(new[] { true, true, false, false, false, false, false, false, false }, p.weaponowned);
        Assert.Equal((weapontype_t.wp_pistol, weapontype_t.wp_nochange), (p.readyweapon, p.pendingweapon));
        Assert.Equal(new[] { 50, 0, 0, 0 }, p.ammo);
        Assert.Equal(player_t.maxammo_table, p.maxammo);
        Assert.False(p.backpack);
        Assert.All(p.cards, c => Assert.False(c));
        Assert.All(p.powers, v => Assert.Equal(0, v));
        Assert.Equal((0, 0, 0), (p.killcount, p.itemcount, p.secretcount)); // P_SetupLevel's (G_PlayerReborn keeps them)
        Assert.False(p.didsecret); // G_PlayerReborn's memset
        Assert.Null(p.attacker);
        Assert.Equal((0, player_t.VIEWHEIGHT), (p.damagecount, p.viewheight));
        Assert.True(p.usedown && p.attackdown); // don't do anything immediately
        Assert.Equal(0, world.leveltime);
        Assert.Equal((rnd + loadRandoms) & 0xff, world.random.prndindex); // the random index goes on (G_InitNew alone clears it)
        Assert.Equal(gametic, world.gametic);
        Assert.Equal(statenum_t.S_PISTOLUP, p.psprites[World.ps_weapon].state);

        // use still held: nothing happens, and the player lives on
        Tic(world, buttons: (byte)buttoncode_t.BT_USE);
        Assert.Equal((playerstate_t.PST_LIVE, gameaction_t.ga_nothing), (p.playerstate, world.gameaction));
    }

    [Fact]
    public void ANetGamesRebornIsNotPortedYet()
    {
        (World world, _) = Load(netgame: true);
        player_t p = P(world);
        world.P_DamageMobj(p.mo!, null, null, 1000);
        Assert.Throws<NotSupportedException>(() => Tic(world, buttons: (byte)buttoncode_t.BT_USE));
    }

    [Fact]
    public void TheChecksumFollowsTheDeathTurn()
    {
        (World a, _) = Load();
        (World b, _) = Load();
        foreach (World w in new[] { a, b })
        {
            mobj_t imp = w.P_SpawnMobj(F(64), F(448), World.ONFLOORZ, mobjtype_t.MT_TROOP);
            w.P_DamageMobj(P(w).mo!, imp, imp, 1000);
        }
        Tic(a);
        Tic(b);
        Assert.Equal(a.Checksum(), b.Checksum());
        P(b).mo!.angle -= World.ANG5;
        Assert.NotEqual(a.Checksum(), b.Checksum());
    }
}
