using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T5.8: keys and the other pickups of <c>P_TouchSpecialThing</c>, the
/// player's special sectors (<c>P_PlayerInSpecialSector</c>: damage floors,
/// secrets, E1M8's exit floor) through the minimal <c>P_DamageMobj</c>, the
/// exits (<c>G_ExitLevel</c>, <c>G_SecretExitLevel</c>, lines 11, 51, 52 and
/// 124) and the level flow (<c>G_DoCompleted</c>, <c>G_WorldDone</c>,
/// <c>G_DoWorldDone</c>), on the synthetic maps (E1M2: the keys in the
/// corridor, the special alcoves, room S's exit switches, alcove 23's exit
/// lines; E1M1: the exit switch in front of the start).
/// </summary>
public class KeysDamageExitTests
{
    private const int FRACUNIT = 1 << 16;

    private static int F(int units) => units * FRACUNIT;

    private static uint Deg(int degrees) => (uint)((ulong)degrees * 0x100000000UL / 360);

    private static WadArchive Wad() => new(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) });

    /// <summary>
    /// A new game on <paramref name="map"/>; <paramref name="quiet"/> clears
    /// E1M2's light specials whose thinkers call <c>P_Random</c> every few
    /// tics (alcove 1 flickering, 9 fire flicker), so a test can count the calls.
    /// </summary>
    private static World NewWorld(string map = "E1M2", skill_t skill = skill_t.sk_medium, GameMode mode = GameMode.shareware, bool netgame = false, bool quiet = false)
    {
        var world = new World(new SpawnSettings(mode, skill, netgame), Tweaks.Vanilla);
        Level level = Level.Load(Wad(), map);
        if (quiet)
            level.Sectors[Alcove(1)].Special = level.Sectors[Alcove(9)].Special = 0;
        world.G_DoLoadLevel(level);
        return world;
    }

    private static player_t P(World world) => world.players[0];

    /// <summary>The queued message events' texts (T6.10).</summary>
    private static string?[] Messages(World world) =>
        world.events.Where(e => e.type == simevent_t.se_message).Select(e => e.message).ToArray();

    private static mobj_t Mo(World world) => world.players[0].mo!;

    private static int Alcove(int i) => 1 + i;

    /// <summary>Puts the player in the middle of alcove <paramref name="alcove"/>, on its floor.</summary>
    private static void InAlcove(World world, int alcove) => world.PlaceMobj(Mo(world), F(32 * alcove + 16), F(64), Deg(90));

    private static void Use(World world, int x, int y, int degrees)
    {
        world.PlaceMobj(Mo(world), F(x), F(y), Deg(degrees));
        world.P_UseLines(P(world));
    }

    private static void Tic(World world, int tics = 1, ticcmd_t cmd = default)
    {
        for (int i = 0; i < tics; i++)
            world.G_Ticker(cmd);
    }

    // ---- keys and pickups ----

    [Fact]
    public void WalkingOverTheKeysPicksThemUp()
    {
        World world = NewWorld();
        player_t p = P(world);
        mobj_t card = world.Mobjs().Single(m => m.type == mobjtype_t.MT_MISC4);
        mobj_t skull = world.Mobjs().Single(m => m.type == mobjtype_t.MT_MISC7);
        Assert.Equal((F(SyntheticIwad.BlueCardX), F(SyntheticIwad.KeysY)), (card.x, card.y));
        Assert.Equal((F(SyntheticIwad.YellowSkullX), F(SyntheticIwad.KeysY)), (skull.x, skull.y));
        Assert.All(p.cards, Assert.False);

        world.PlaceMobj(Mo(world), F(500), F(SyntheticIwad.KeysY), Deg(0));
        var forward = new ticcmd_t { forwardmove = 25 };
        int tics = 0;
        while (!p.cards[(int)card_t.it_bluecard] && tics++ < 35)
            Tic(world, 1, forward);
        Assert.True(p.cards[(int)card_t.it_bluecard]);
        // T6.10: the message is queued as an event after the tic and taken (HU_Ticker)
        Assert.Equal(new[] { World.GOTBLUECARD }, Messages(world));
        Assert.Null(p.message);
        Assert.Equal(think_t.REMOVED, card.function);
        Assert.Equal(sfxenum_t.sfx_itemup, Assert.Single(world.StartedSounds()).sfx);
        Assert.Null(Assert.Single(world.StartedSounds()).origin);
        Assert.True(p.bonuscount > 0);
        Assert.Equal(0, p.itemcount); // keys are no items

        tics = 0;
        while (!p.cards[(int)card_t.it_yellowskull] && tics++ < 35)
            Tic(world, 1, forward);
        Assert.Equal(new[] { World.GOTYELWSKUL }, Messages(world));
        Assert.Equal(think_t.REMOVED, skull.function);
        Assert.Equal(new[] { true, false, false, false, true, false }, p.cards);
    }

    [Fact]
    public void AKeyHeldAlreadyIsTakenSilentlyAndLeftInANetgame()
    {
        World world = NewWorld();
        player_t p = P(world);
        p.cards[(int)card_t.it_redcard] = true;
        mobj_t red = world.P_SpawnMobj(Mo(world).x, Mo(world).y, World.ONFLOORZ, mobjtype_t.MT_MISC5);
        world.P_TouchSpecialThing(red, Mo(world));
        Assert.Null(p.message);
        Assert.Equal(think_t.REMOVED, red.function);

        // Out of reach (above the player's head): nothing.
        mobj_t high = world.P_SpawnMobj(Mo(world).x, Mo(world).y, Mo(world).z + Mo(world).height + 1, mobjtype_t.MT_MISC9);
        world.P_TouchSpecialThing(high, Mo(world));
        Assert.False(p.cards[(int)card_t.it_blueskull]);

        // In a netgame keys stay for everyone.
        world = NewWorld(netgame: true);
        mobj_t blue = world.Mobjs().Single(m => m.type == mobjtype_t.MT_MISC4);
        world.P_TouchSpecialThing(blue, Mo(world));
        Assert.True(P(world).cards[(int)card_t.it_bluecard]);
        Assert.Equal(World.GOTBLUECARD, P(world).message);
        Assert.NotEqual(think_t.REMOVED, blue.function);
        Assert.Empty(world.StartedSounds());
    }

    [Fact]
    public void APickedUpKeyOpensItsLockedDoor()
    {
        World world = NewWorld();
        line_t door = world.lines[SyntheticIwad.DoorLineR];
        door.special = 26; // DR blue door
        sector_t doorSector = world.sectors[SyntheticIwad.DoorSector];

        Use(world, 100, 196, 0); // from room R, facing the door
        Assert.Null(doorSector.specialdata);
        Assert.Equal(World.PD_BLUEK, P(world).message);

        mobj_t card = world.Mobjs().Single(m => m.type == mobjtype_t.MT_MISC4);
        world.P_TouchSpecialThing(card, Mo(world));
        Use(world, 100, 196, 0);
        Assert.IsType<vldoor_t>(doorSector.specialdata);
    }

    [Fact]
    public void HealthArmorAndPowerUpsArePickedUp()
    {
        World world = NewWorld();
        player_t p = P(world);
        mobj_t me = Mo(world);
        mobj_t Touch(mobjtype_t type)
        {
            p.message = null;
            mobj_t thing = world.P_SpawnMobj(me.x, me.y, World.ONFLOORZ, type);
            world.P_TouchSpecialThing(thing, me);
            return thing;
        }

        // A stimpack at full health stays.
        Assert.NotEqual(think_t.REMOVED, Touch(mobjtype_t.MT_MISC10).function);
        Assert.Null(p.message);
        // A health bonus goes over 100; it counts as an item.
        Assert.Equal(think_t.REMOVED, Touch(mobjtype_t.MT_MISC2).function);
        Assert.Equal((101, 101, World.GOTHTHBONUS, 1), (p.health, me.health, p.message, p.itemcount));
        // Armor: green 100 (type 1), an armor bonus on top, blue armor 200 (type 2), green no longer taken.
        Touch(mobjtype_t.MT_MISC0);
        Assert.Equal((1, 100, World.GOTARMOR), (p.armortype, p.armorpoints, p.message));
        Touch(mobjtype_t.MT_MISC3);
        Assert.Equal((1, 101), (p.armortype, p.armorpoints));
        Touch(mobjtype_t.MT_MISC1);
        Assert.Equal((2, 200, World.GOTMEGA), (p.armortype, p.armorpoints, p.message));
        Assert.NotEqual(think_t.REMOVED, Touch(mobjtype_t.MT_MISC0).function);
        // Hurt: a medikit heals 25 (vanilla's "REALLY need" test comes after the healing).
        p.health = me.health = 20;
        Touch(mobjtype_t.MT_MISC11);
        Assert.Equal((45, World.GOTMEDIKIT), (p.health, p.message));
        // A soulsphere: +100 up to 200, the power-up sound.
        Touch(mobjtype_t.MT_MISC12);
        Assert.Equal((145, World.GOTSUPER), (p.health, p.message));
        Assert.Equal(sfxenum_t.sfx_getpow, world.StartedSounds()[^1].sfx);
        // The megasphere is Doom II's only.
        Assert.NotEqual(think_t.REMOVED, Touch(mobjtype_t.MT_MEGA).function);
        // Power-ups.
        Touch(mobjtype_t.MT_MISC14);
        Assert.Equal((World.IRONTICS, World.GOTSUIT), (p.powers[(int)powertype_t.pw_ironfeet], p.message));
        Touch(mobjtype_t.MT_INS);
        Assert.Equal(World.INVISTICS, p.powers[(int)powertype_t.pw_invisibility]);
        Assert.NotEqual((mobjflag_t)0, me.flags & mobjflag_t.MF_SHADOW);
        Touch(mobjtype_t.MT_MISC15);
        Assert.Equal(1, p.powers[(int)powertype_t.pw_allmap]);
        Assert.NotEqual(think_t.REMOVED, Touch(mobjtype_t.MT_MISC15).function); // already got it
        Touch(mobjtype_t.MT_MISC13);
        Assert.Equal((1, weapontype_t.wp_fist, World.GOTBERSERK), (p.powers[(int)powertype_t.pw_strength], p.pendingweapon, p.message));
        // Weapons and ammo (T6.1; StateMachineTests).
        Assert.Equal(think_t.REMOVED, Touch(mobjtype_t.MT_SHOTGUN).function);
        Assert.Equal(think_t.REMOVED, Touch(mobjtype_t.MT_CLIP).function);
    }

    // ---- damage floors ----

    [Theory]
    [InlineData(SyntheticIwad.HellslimeAlcove, 10)]
    [InlineData(SyntheticIwad.NukageAlcove, 5)]
    [InlineData(SyntheticIwad.SuperHellslimeAlcove, 20)]
    [InlineData(SyntheticIwad.StrobeHurtAlcove, 20)]
    public void DamageFloorsHurtEvery32Tics(int alcove, int damage)
    {
        World world = NewWorld(quiet: true);
        player_t p = P(world);
        InAlcove(world, alcove);
        Assert.Equal(SyntheticIwad.AlcoveSpecial(alcove), Mo(world).subsector.sector.special);

        // leveltime 0: damage, and the pain state's P_Random.
        int prnd = world.random.prndindex;
        Tic(world);
        Assert.Equal(100 - damage, p.health);
        Assert.Equal(100 - damage, Mo(world).health);
        Assert.Equal(damage - 1, p.damagecount); // counted down at the end of the tic
        Assert.Null(p.attacker);
        Assert.Equal((prnd + 1) & 0xff, world.random.prndindex);
        Assert.Equal(statenum_t.S_PLAY_PAIN, Mo(world).state);
        // The pain sound comes with S_PLAY_PAIN2 (A_Pain), 4 tics later.
        var heard = new System.Collections.Generic.List<sfxenum_t>();
        for (int i = 0; i < 31; i++)
        {
            Tic(world);
            heard.AddRange(world.StartedSounds().Where(e => e.origin == Mo(world)).Select(e => e.sfx));
        }
        Assert.Equal(new[] { sfxenum_t.sfx_plpain }, heard);
        Assert.Equal(statenum_t.S_PLAY, Mo(world).state);

        // Not again until leveltime 32.
        Assert.Equal(100 - damage, p.health);
        Tic(world);
        Assert.Equal(100 - 2 * damage, p.health);

        // In the air: nothing.
        world.leveltime = 64;
        Mo(world).z += F(1);
        Tic(world);
        Assert.Equal(100 - 2 * damage, p.health);
    }

    [Theory]
    [InlineData(SyntheticIwad.HellslimeAlcove, false)]
    [InlineData(SyntheticIwad.NukageAlcove, false)]
    [InlineData(SyntheticIwad.SuperHellslimeAlcove, true)]
    [InlineData(SyntheticIwad.StrobeHurtAlcove, true)]
    public void TheRadiationSuitProtects(int alcove, bool leaks)
    {
        World world = NewWorld(quiet: true);
        player_t p = P(world);
        p.powers[(int)powertype_t.pw_ironfeet] = World.IRONTICS;
        InAlcove(world, alcove);
        int hurt = 0, rolls = 0;
        for (int tic = 0; tic < 32 * 64; tic++)
        {
            int prnd = world.random.prndindex, health = p.health;
            world.leveltime = 0; // a damaging tic every time
            p.powers[(int)powertype_t.pw_ironfeet] = World.IRONTICS;
            Tic(world);
            int calls = (world.random.prndindex - prnd) & 0xff;
            if (p.health < health)
            {
                hurt++;
                Assert.Equal(health - 20, p.health);
                Assert.Equal(2, calls); // the suit's roll and the pain chance
            }
            else
                rolls += calls;
            p.health = Mo(world).health = 100;
        }
        if (leaks)
        {
            // P_Random() < 5: 5 of rndtable's 256 values (0, 1, 2, 3 and one more 3).
            Assert.InRange(hurt, 1, 32 * 64 / 10);
            Assert.True(rolls > 0);
        }
        else
            Assert.Equal((0, 0), (hurt, rolls));
    }

    [Fact]
    public void ArmorBabySkillAndGodMode()
    {
        World world = NewWorld();
        player_t p = P(world);
        p.armortype = 1;
        p.armorpoints = 100;
        InAlcove(world, SyntheticIwad.SuperHellslimeAlcove);
        Tic(world);
        Assert.Equal((100 - 14, 100 - 6), (p.health, p.armorpoints)); // green armor takes a third

        world = NewWorld();
        p = P(world);
        p.armortype = 2;
        p.armorpoints = 4;
        InAlcove(world, SyntheticIwad.SuperHellslimeAlcove);
        Tic(world);
        Assert.Equal((100 - 16, 0, 0), (p.health, p.armorpoints, p.armortype)); // blue takes half, but only what is left

        world = NewWorld(skill: skill_t.sk_baby);
        InAlcove(world, SyntheticIwad.HellslimeAlcove);
        Tic(world);
        Assert.Equal(95, P(world).health);

        world = NewWorld();
        P(world).cheats |= player_t.CF_GODMODE;
        InAlcove(world, SyntheticIwad.HellslimeAlcove);
        Tic(world);
        Assert.Equal(100, P(world).health);
        P(world).powers[(int)powertype_t.pw_invulnerability] = 100;
        P(world).cheats = 0;
        world.leveltime = 32;
        Tic(world);
        Assert.Equal(100, P(world).health);
    }

    [Fact]
    public void ThePlayerDiesOnADamageFloor()
    {
        World world = NewWorld();
        player_t p = P(world);
        mobj_t me = Mo(world);
        p.health = me.health = 5;
        InAlcove(world, SyntheticIwad.NukageAlcove);
        Tic(world);
        Assert.Equal((0, 0), (p.health, me.health));
        Assert.Equal(playerstate_t.PST_DEAD, p.playerstate);
        Assert.Equal(statenum_t.S_PLAY_DIE1, me.state);
        Assert.Equal((mobjflag_t)0, me.flags & (mobjflag_t.MF_SOLID | mobjflag_t.MF_SHOOTABLE));
        Assert.NotEqual((mobjflag_t)0, me.flags & mobjflag_t.MF_CORPSE);
        Assert.Equal(56 * FRACUNIT / 4, me.height);
        Assert.Equal(1, p.frags[0]); // an environment kill counts against the player
        Assert.Equal(statenum_t.S_PISTOLDOWN, p.psprites[World.ps_weapon].state); // P_DropWeapon (T6.6)

        // Dead: no more damage, no movement (P_DeathThink, T6.12: commands but use do nothing).
        int x = me.x;
        world.leveltime = 32;
        Tic(world, 1, new ticcmd_t { forwardmove = 50 });
        Assert.Equal(0, p.health);
        Assert.Equal(x, me.x);
        // The death states scream (A_PlayerScream) and fall to S_PLAY_DIE7.
        var heard = new System.Collections.Generic.List<sfxenum_t>();
        for (int i = 0; i < 70; i++)
        {
            Tic(world);
            heard.AddRange(world.StartedSounds().Where(e => e.origin == me).Select(e => e.sfx));
        }
        Assert.Equal(new[] { sfxenum_t.sfx_pldeth }, heard);
        Assert.Equal(statenum_t.S_PLAY_DIE7, me.state);
    }

    // ---- secrets ----

    [Fact]
    public void ASecretCountsOnce()
    {
        World world = NewWorld();
        player_t p = P(world);
        sector_t secret = world.sectors[Alcove(SyntheticIwad.SecretAlcove)];
        Assert.Equal((9, 1), ((int)secret.special, world.totalsecret));
        InAlcove(world, SyntheticIwad.SecretAlcove);
        Tic(world);
        Assert.Equal(1, p.secretcount);
        Assert.Equal(0, secret.special);
        Tic(world, 40);
        Assert.Equal(1, p.secretcount);
        Assert.Equal(100, p.health);
    }

    // ---- exits ----

    [Fact]
    public void TheExitFloorHurtsButNeverKillsAndExitsAt10()
    {
        World world = NewWorld();
        player_t p = P(world);
        p.cheats |= player_t.CF_GODMODE;
        p.health = Mo(world).health = 45;
        InAlcove(world, SyntheticIwad.ExitFloorAlcove);
        Tic(world);
        Assert.Equal(0, p.cheats & player_t.CF_GODMODE); // god mode is taken away
        Assert.Equal(25, p.health);
        Assert.Equal(gameaction_t.ga_nothing, world.gameaction);
        world.leveltime = 32;
        Tic(world);
        Assert.Equal(5, p.health);
        Assert.Equal(gameaction_t.ga_completed, world.gameaction);
        Assert.False(world.secretexit);

        // 20 damage on 15 health leaves 1.
        world = NewWorld();
        P(world).health = Mo(world).health = 15;
        InAlcove(world, SyntheticIwad.ExitFloorAlcove);
        Tic(world);
        Assert.Equal((1, 1), (P(world).health, Mo(world).health));
        Assert.Equal(playerstate_t.PST_LIVE, P(world).playerstate);
        Assert.Equal(gameaction_t.ga_completed, world.gameaction);
    }

    [Fact]
    public void TheExitSwitchesExit()
    {
        World world = NewWorld();
        Use(world, 260, 196, 0); // room S's east wall: S1 exit
        Assert.Equal(gameaction_t.ga_completed, world.gameaction);
        Assert.False(world.secretexit);
        Assert.Equal("SW2BRCOM", world.sides[world.lines[SyntheticIwad.ExitSwitchLine].sidenum[0]].midtexture);
        Assert.Equal(0, world.lines[SyntheticIwad.ExitSwitchLine].special);

        world = NewWorld();
        Use(world, 224, 212, 90); // room S's north wall: S1 secret exit
        Assert.Equal(gameaction_t.ga_completed, world.gameaction);
        Assert.True(world.secretexit);
        Assert.Equal("SW2BRCOM", world.sides[world.lines[SyntheticIwad.SecretExitSwitchLine].sidenum[0]].midtexture);

        // Doom II without MAP31: no secret exit, a plain one.
        world = NewWorld(mode: GameMode.commercial);
        world.map31exists = false;
        Use(world, 224, 212, 90);
        Assert.Equal(gameaction_t.ga_completed, world.gameaction);
        Assert.False(world.secretexit);
    }

    [Fact]
    public void TheExitLinesExitWhenWalkedOver()
    {
        // Into alcove 23 (a drop) over line 26: W1 exit.
        World world = NewWorld();
        mobj_t me = Mo(world);
        world.PlaceMobj(me, F(752), F(-24), Deg(90));
        Assert.True(world.P_TryMove(me, F(752), F(8)));
        Assert.Equal(gameaction_t.ga_completed, world.gameaction);
        Assert.False(world.secretexit);

        // From alcove 22 into 23 over line 75: secret exit (124).
        world = NewWorld();
        me = Mo(world);
        world.PlaceMobj(me, F(720), F(64), Deg(0));
        Assert.True(world.P_TryMove(me, F(744), F(64)));
        Assert.Equal(gameaction_t.ga_completed, world.gameaction);
        Assert.True(world.secretexit);
    }

    // ---- the level flow ----

    [Theory]
    [InlineData("E1M1", false, 2, gameaction_t.ga_worlddone)]
    [InlineData("E1M3", true, 9, gameaction_t.ga_worlddone)]
    [InlineData("E1M9", false, 4, gameaction_t.ga_worlddone)]
    [InlineData("E2M9", false, 6, gameaction_t.ga_worlddone)]
    [InlineData("E3M9", false, 7, gameaction_t.ga_worlddone)]
    [InlineData("E4M9", false, 3, gameaction_t.ga_worlddone)]
    [InlineData("E1M8", false, 0, gameaction_t.ga_victory)]
    public void TheNextLevelIsVanillas(string map, bool secret, int next, gameaction_t action)
    {
        World world = NewWorld("E1M2");
        world.gamemap = World.MapNumber(map);
        world.gameepisode = World.EpisodeNumber(map);
        if (secret)
            world.G_SecretExitLevel();
        else
            world.G_ExitLevel();
        world.G_DoCompleted();
        if (action == gameaction_t.ga_victory)
        {
            Assert.Equal(gameaction_t.ga_victory, world.gameaction);
            Assert.True(world.G_GameEnds());
            return;
        }
        Assert.Equal(gameaction_t.ga_nothing, world.gameaction);
        world.G_WorldDone();
        Assert.Equal(action, world.gameaction);
        Assert.False(world.G_GameEnds());
        Assert.Equal($"E{map[1]}M{next}", world.NextMapName());
        Assert.Equal(secret || map.EndsWith('9'), P(world).didsecret);
    }

    [Theory]
    [InlineData(1, false, "MAP02")]
    [InlineData(15, true, "MAP31")]
    [InlineData(15, false, "MAP16")]
    [InlineData(31, true, "MAP32")]
    [InlineData(31, false, "MAP16")]
    [InlineData(32, false, "MAP16")]
    public void Doom2sNextLevelIsVanillas(int map, bool secret, string next)
    {
        World world = NewWorld("E1M2", mode: GameMode.commercial);
        world.gamemap = map;
        world.gameepisode = 1;
        if (secret)
            world.G_SecretExitLevel();
        else
            world.G_ExitLevel();
        world.G_DoCompleted();
        world.G_WorldDone();
        Assert.Equal(next, world.NextMapName());
        world.gamemap = 30;
        Assert.True(world.G_GameEnds()); // the cast call
    }

    [Fact]
    public void TheSyntheticE1M1ExitsToE1M2KeepingThePlayer()
    {
        World world = NewWorld("E1M1");
        player_t p = P(world);
        Assert.Equal(11, world.lines[SyntheticIwad.E1M1ExitLine].special);
        p.health = Mo(world).health = 80;
        p.armortype = 1;
        p.armorpoints = 30;
        p.cards[(int)card_t.it_redcard] = true;
        p.powers[(int)powertype_t.pw_ironfeet] = 100;
        p.damagecount = 10;
        p.secretcount = 2;
        Tic(world, 5);
        Use(world, 0, 100, 90); // player 1 starts at (0, 0) facing the exit 128 units north
        Assert.Equal(gameaction_t.ga_completed, world.gameaction);

        world.G_DoCompleted();
        Assert.Equal(gameaction_t.ga_nothing, world.gameaction);
        Assert.All(p.cards, Assert.False);
        Assert.All(p.powers, v => Assert.Equal(0, v));
        Assert.Equal(0, p.damagecount);
        Assert.Equal((0, 1, 2, 0), (world.wminfo.epsd, world.wminfo.next, world.wminfo.plyr[0].ssecret, world.wminfo.maxsecret));
        Assert.Equal(5, world.wminfo.plyr[0].stime);
        world.G_WorldDone();
        Assert.Equal("E1M2", world.NextMapName());

        world.G_DoWorldDone(Level.Load(Wad(), world.NextMapName()));
        Assert.Equal(gameaction_t.ga_nothing, world.gameaction);
        Assert.Equal((2, 1, 0, "E1M2"), (world.gamemap, world.gameepisode, world.leveltime, world.level.Name));
        Assert.Same(p, P(world));
        Assert.Equal((80, 80, 1, 30), (p.health, Mo(world).health, p.armortype, p.armorpoints));
        Assert.Equal(0, p.secretcount);
        Assert.Equal((F(64), F(-64)), (Mo(world).x, Mo(world).y)); // E1M2's start
        Assert.Equal(1, world.totalsecret);
    }
}
