using System;
using System.Linq;
using IsoDoom.Game;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T6.8: every shareware pickup (p_inter.c <c>P_TouchSpecialThing</c>) gives
/// what vanilla gives, the power-ups run out as vanilla's, and st_stuff.c's
/// <c>ST_doPaletteStuff</c> (<see cref="StStuff"/>) picks vanilla's palette.
/// The expected values are worked out from p_inter.c, d_items.c and
/// st_stuff.c; the <c>testmap-pickups</c> route checks the same pickups, and
/// the palette, against the vanilla reference on every tic.
/// </summary>
public class PickupTests
{
    private static WadArchive Wad() => new(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) });

    private static World NewWorld(skill_t skill = skill_t.sk_medium)
    {
        var world = new World(new SpawnSettings(GameMode.shareware, skill), Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(Wad(), "E1M2"));
        return world;
    }

    private static mobjtype_t TypeOf(int doomednum) =>
        (mobjtype_t)Array.FindIndex(Info.mobjinfo, i => i.doomednum == doomednum);

    /// <summary>What a pickup leaves the player with (from a new player at 50 health).</summary>
    public sealed record Gives(int Health = 50, int Armor = 0, int ArmorType = 0, int Clip = 50, int Shell = 0, int Cell = 0, int Misl = 0,
        int MaxClip = 200, weapontype_t? Weapon = null, card_t? Card = null, powertype_t? Power = null, int PowerTics = 0,
        bool Item = false, int Bonus = World.BONUSADD, sfxenum_t Sound = sfxenum_t.sfx_itemup);

    // Every pickup thing whose sprite is in the shareware IWAD (no invulnerability, berserk,
    // cells, plasma gun, BFG, skull keys, megasphere or super shotgun), on skill 3.
    public static TheoryData<int, string, Gives> SharewarePickups => new()
    {
        { 2011, World.GOTSTIM, new Gives(Health: 60) },
        { 2012, World.GOTMEDIKIT, new Gives(Health: 75) },
        { 2014, World.GOTHTHBONUS, new Gives(Health: 51, Item: true) },
        { 2013, World.GOTSUPER, new Gives(Health: 150, Item: true, Sound: sfxenum_t.sfx_getpow) },
        { 2015, World.GOTARMBONUS, new Gives(Armor: 1, ArmorType: 1, Item: true) },
        { 2018, World.GOTARMOR, new Gives(Armor: 100, ArmorType: 1) },
        { 2019, World.GOTMEGA, new Gives(Armor: 200, ArmorType: 2) },
        { 2007, World.GOTCLIP, new Gives(Clip: 60) },
        { 2048, World.GOTCLIPBOX, new Gives(Clip: 100) },
        { 2008, World.GOTSHELLS, new Gives(Shell: 4) },
        { 2049, World.GOTSHELLBOX, new Gives(Shell: 20) },
        { 2010, World.GOTROCKET, new Gives(Misl: 1) },
        { 2046, World.GOTROCKBOX, new Gives(Misl: 5) },
        // The backpack doubles every maximum and gives a clip of each ammo (cells too).
        { 8, World.GOTBACKPACK, new Gives(Clip: 60, Shell: 4, Cell: 20, Misl: 1, MaxClip: 400) },
        // A weapon: two clips of its ammo, the weapon brought up; no bonus flash of its own outside netgames.
        { 2001, World.GOTSHOTGUN, new Gives(Shell: 8, Weapon: weapontype_t.wp_shotgun, Sound: sfxenum_t.sfx_wpnup) },
        { 2002, World.GOTCHAINGUN, new Gives(Clip: 70, Weapon: weapontype_t.wp_chaingun, Sound: sfxenum_t.sfx_wpnup) },
        { 2003, World.GOTLAUNCHER, new Gives(Misl: 2, Weapon: weapontype_t.wp_missile, Sound: sfxenum_t.sfx_wpnup) },
        { 2005, World.GOTCHAINSAW, new Gives(Weapon: weapontype_t.wp_chainsaw, Sound: sfxenum_t.sfx_wpnup) },
        // Keys: P_GiveCard sets the flash, P_TouchSpecialThing adds to it.
        { 5, World.GOTBLUECARD, new Gives(Card: card_t.it_bluecard, Bonus: 2 * World.BONUSADD) },
        { 6, World.GOTYELWCARD, new Gives(Card: card_t.it_yellowcard, Bonus: 2 * World.BONUSADD) },
        { 13, World.GOTREDCARD, new Gives(Card: card_t.it_redcard, Bonus: 2 * World.BONUSADD) },
        { 2024, World.GOTINVIS, new Gives(Power: powertype_t.pw_invisibility, PowerTics: World.INVISTICS, Item: true, Sound: sfxenum_t.sfx_getpow) },
        { 2025, World.GOTSUIT, new Gives(Power: powertype_t.pw_ironfeet, PowerTics: World.IRONTICS, Sound: sfxenum_t.sfx_getpow) },
        { 2026, World.GOTMAP, new Gives(Power: powertype_t.pw_allmap, PowerTics: 1, Item: true, Sound: sfxenum_t.sfx_getpow) },
        { 2045, World.GOTVISOR, new Gives(Power: powertype_t.pw_infrared, PowerTics: World.INFRATICS, Item: true, Sound: sfxenum_t.sfx_getpow) },
    };

    [Theory]
    [MemberData(nameof(SharewarePickups))]
    public void EverySharewarePickupGivesWhatVanillaGives(int doomednum, string message, Gives gives)
    {
        World world = NewWorld();
        player_t p = world.players[0];
        mobj_t me = p.mo!;
        p.health = me.health = 50;
        mobj_t thing = world.P_SpawnMobj(me.x, me.y, World.ONFLOORZ, TypeOf(doomednum));
        Assert.NotEqual((mobjflag_t)0, thing.flags & mobjflag_t.MF_SPECIAL);
        world.events.Clear();

        world.P_TouchSpecialThing(thing, me);

        Assert.Equal(think_t.REMOVED, thing.function);
        Assert.Equal(message, p.message);
        Assert.Equal((gives.Health, gives.Health), (p.health, me.health));
        Assert.Equal((gives.Armor, gives.ArmorType), (p.armorpoints, p.armortype));
        Assert.Equal(new[] { gives.Clip, gives.Shell, gives.Cell, gives.Misl }, p.ammo);
        int scale = gives.MaxClip / 200;
        Assert.Equal(new[] { 200 * scale, 50 * scale, 300 * scale, 50 * scale }, p.maxammo);
        Assert.Equal(scale == 2, p.backpack);
        for (int w = 0; w < (int)weapontype_t.NUMWEAPONS; w++)
            Assert.Equal(w is (int)weapontype_t.wp_fist or (int)weapontype_t.wp_pistol || w == (int?)gives.Weapon, p.weaponowned[w]);
        Assert.Equal(gives.Weapon ?? weapontype_t.wp_nochange, p.pendingweapon);
        for (int c = 0; c < (int)card_t.NUMCARDS; c++)
            Assert.Equal(c == (int?)gives.Card, p.cards[c]);
        for (int w = 0; w < (int)powertype_t.NUMPOWERS; w++)
            Assert.Equal(w == (int?)gives.Power ? gives.PowerTics : 0, p.powers[w]);
        Assert.Equal(gives.Power == powertype_t.pw_invisibility, (me.flags & mobjflag_t.MF_SHADOW) != 0);
        Assert.Equal(gives.Item ? 1 : 0, p.itemcount);
        Assert.Equal(gives.Bonus, p.bonuscount);
        Assert.Equal(new[] { gives.Sound }, world.StartedSounds().Select(s => s.sfx));
        Assert.Null(world.StartedSounds()[0].origin); // vanilla's S_StartSound(NULL, sound): heard everywhere
    }

    [Theory]
    [InlineData(skill_t.sk_baby, 2)]
    [InlineData(skill_t.sk_easy, 1)]
    [InlineData(skill_t.sk_hard, 1)]
    [InlineData(skill_t.sk_nightmare, 2)]
    public void AmmoIsDoubledOnBabyAndNightmare(skill_t skill, int factor)
    {
        World world = NewWorld(skill);
        player_t p = world.players[0];
        mobj_t me = p.mo!;
        foreach (int doomednum in new[] { 2008, 2046, 2001 })
            world.P_TouchSpecialThing(world.P_SpawnMobj(me.x, me.y, World.ONFLOORZ, TypeOf(doomednum)), me);
        Assert.Equal((12 * factor, 5 * factor), (p.ammo[(int)ammotype_t.am_shell], p.ammo[(int)ammotype_t.am_misl]));
    }

    [Fact]
    public void WhatIsNotNeededStays()
    {
        World world = NewWorld();
        player_t p = world.players[0];
        mobj_t me = p.mo!;
        bool Taken(int doomednum)
        {
            mobj_t thing = world.P_SpawnMobj(me.x, me.y, World.ONFLOORZ, TypeOf(doomednum));
            world.P_TouchSpecialThing(thing, me);
            return thing.function == think_t.REMOVED;
        }

        Assert.False(Taken(2011)); // a stimpack at full health
        Assert.False(Taken(2012)); // a medikit
        Assert.True(Taken(2019));
        Assert.False(Taken(2018)); // green armor over blue
        Assert.False(Taken(2019)); // blue at 200
        Assert.True(Taken(2026));
        Assert.False(Taken(2026)); // a second computer map
        Assert.True(Taken(2024));
        Assert.True(Taken(2024)); // a second blur sphere restarts it
        p.ammo[(int)ammotype_t.am_clip] = 200;
        Assert.False(Taken(2007)); // a clip at the maximum
        Assert.True(Taken(2001));
        p.ammo[(int)ammotype_t.am_shell] = 50;
        Assert.False(Taken(2001)); // a shotgun owned, shells full
        Assert.True(Taken(8));
        Assert.True(Taken(8)); // a second backpack: ammo only
        Assert.Equal(400, p.maxammo[(int)ammotype_t.am_clip]);
        Assert.True(Taken(2014)); // health bonuses go on to 200 ...
        p.health = me.health = 200;
        Assert.True(Taken(2014)); // ... and are taken there, giving nothing
        Assert.Equal(200, p.health);
        // A key held is taken again in single player (no message).
        Assert.True(Taken(5));
        p.message = null;
        Assert.True(Taken(5));
        Assert.Null(p.message);
    }

    [Fact]
    public void ThingsOutOfReachAndDeadPlayersTakeNothing()
    {
        World world = NewWorld();
        player_t p = world.players[0];
        mobj_t me = p.mo!;
        mobj_t thing = world.P_SpawnMobj(me.x, me.y, World.ONFLOORZ, TypeOf(2018));
        thing.z = me.z + me.height + 1; // above the player's head
        world.P_TouchSpecialThing(thing, me);
        thing.z = me.z - 9 * Fixed.FRACUNIT; // more than 8 below its feet
        world.P_TouchSpecialThing(thing, me);
        thing.z = me.z;
        me.health = 0;
        world.P_TouchSpecialThing(thing, me);
        Assert.NotEqual(think_t.REMOVED, thing.function);
        Assert.Equal(0, p.armorpoints);
    }

    // ---- the power-ups running out ----

    private static void Tics(World world, int tics)
    {
        for (int i = 0; i < tics; i++)
            world.G_Ticker(new ticcmd_t());
    }

    [Fact]
    public void TheBlurSphereRunsOut()
    {
        World world = NewWorld();
        player_t p = world.players[0];
        mobj_t me = p.mo!;
        world.P_TouchSpecialThing(world.P_SpawnMobj(me.x, me.y, World.ONFLOORZ, TypeOf(2024)), me);
        Tics(world, World.INVISTICS - 1);
        Assert.Equal(1, p.powers[(int)powertype_t.pw_invisibility]);
        Assert.NotEqual((mobjflag_t)0, me.flags & mobjflag_t.MF_SHADOW);
        Tics(world, 1);
        Assert.Equal(0, p.powers[(int)powertype_t.pw_invisibility]);
        Assert.Equal((mobjflag_t)0, me.flags & mobjflag_t.MF_SHADOW);
    }

    [Fact]
    public void TheSuitTintsGreenAndBlinksOut()
    {
        World world = NewWorld();
        player_t p = world.players[0];
        mobj_t me = p.mo!;
        world.P_TouchSpecialThing(world.P_SpawnMobj(me.x, me.y, World.ONFLOORZ, TypeOf(2025)), me);
        // The pickup's gold first: bonuscount 6 is palette 9 + (6 + 7) / 8 = 10, then the suit's green.
        Assert.Equal(StStuff.STARTBONUSPALS + 1, StStuff.ST_doPaletteStuff(p));
        Tics(world, World.BONUSADD);
        Assert.Equal(0, p.bonuscount);
        Assert.Equal(StStuff.RADIATIONPAL, StStuff.ST_doPaletteStuff(p));
        // Its last 4 s (4 × 32 tics) blink with bit 3 of the count.
        Tics(world, World.IRONTICS - World.BONUSADD - 4 * 32 - 1);
        Assert.Equal((4 * 32 + 1, StStuff.RADIATIONPAL), (p.powers[(int)powertype_t.pw_ironfeet], StStuff.ST_doPaletteStuff(p)));
        Tics(world, 1);
        Assert.Equal((4 * 32, 0), (p.powers[(int)powertype_t.pw_ironfeet], StStuff.ST_doPaletteStuff(p)));
        Tics(world, 128 - 15);
        Assert.Equal((15, StStuff.RADIATIONPAL), (p.powers[(int)powertype_t.pw_ironfeet], StStuff.ST_doPaletteStuff(p)));
        Tics(world, 8);
        Assert.Equal((7, 0), (p.powers[(int)powertype_t.pw_ironfeet], StStuff.ST_doPaletteStuff(p)));
        Tics(world, 7);
        Assert.Equal(0, p.powers[(int)powertype_t.pw_ironfeet]);
    }

    [Fact]
    public void TheVisorLightsEverythingAndBlinksOut()
    {
        World world = NewWorld();
        player_t p = world.players[0];
        mobj_t me = p.mo!;
        world.P_TouchSpecialThing(world.P_SpawnMobj(me.x, me.y, World.ONFLOORZ, TypeOf(2045)), me);
        Tics(world, 1);
        Assert.Equal(1, p.fixedcolormap); // colormap 1: nearly full bright
        Tics(world, World.INFRATICS - 1 - 4 * 32);
        Assert.Equal((4 * 32, 0), (p.powers[(int)powertype_t.pw_infrared], p.fixedcolormap));
        Tics(world, 128 - 8);
        Assert.Equal((8, 1), (p.powers[(int)powertype_t.pw_infrared], p.fixedcolormap));
        Tics(world, 8);
        Assert.Equal((0, 0), (p.powers[(int)powertype_t.pw_infrared], p.fixedcolormap));
    }

    [Fact]
    public void InvulnerabilityInvertsAndWinsOverTheVisor()
    {
        // Not in the shareware IWAD, but in the sim (registered and Doom II maps).
        World world = NewWorld();
        player_t p = world.players[0];
        mobj_t me = p.mo!;
        world.P_TouchSpecialThing(world.P_SpawnMobj(me.x, me.y, World.ONFLOORZ, TypeOf(2045)), me);
        world.P_TouchSpecialThing(world.P_SpawnMobj(me.x, me.y, World.ONFLOORZ, TypeOf(2022)), me);
        Assert.Equal(World.GOTINVUL, p.message);
        Tics(world, 1);
        Assert.Equal(World.INVERSECOLORMAP, p.fixedcolormap);
        Tics(world, World.INVULNTICS - 1 - 4 * 32);
        Assert.Equal(0, p.fixedcolormap); // blinking off: and the visor does not show through
        Tics(world, 4 * 32);
        Assert.Equal(1, p.fixedcolormap); // run out: the visor's again
    }

    // ---- st_stuff.c ST_doPaletteStuff ----

    [Theory]
    [InlineData(0, 0, 0, 0, 0)]
    // damagecount: palette 1 + (count + 7) / 8, at most 8
    [InlineData(1, 0, 0, 0, 2)]
    [InlineData(8, 0, 0, 0, 2)]
    [InlineData(9, 0, 0, 0, 3)]
    [InlineData(49, 0, 0, 0, 8)]
    [InlineData(56, 0, 0, 0, 8)]
    [InlineData(100, 0, 0, 0, 8)]
    // bonuscount: palette 9 + (count + 7) / 8, at most 12; red wins
    [InlineData(0, 1, 0, 0, 10)]
    [InlineData(0, 6, 0, 0, 10)]
    [InlineData(0, 9, 0, 0, 11)]
    [InlineData(0, 24, 0, 0, 12)]
    [InlineData(0, 100, 0, 0, 12)]
    [InlineData(3, 6, 0, 0, 2)]
    // the radiation suit: green while over 128 tics or bit 3 set; the flashes win
    [InlineData(0, 0, 129, 0, 13)]
    [InlineData(0, 0, 128, 0, 0)]
    [InlineData(0, 0, 8, 0, 13)]
    [InlineData(0, 0, 7, 0, 0)]
    [InlineData(0, 6, 2100, 0, 10)]
    [InlineData(1, 0, 2100, 0, 2)]
    // berserk: a red of 12 - strength / 64 fading out, stronger damage over it
    [InlineData(0, 0, 0, 1, 3)]
    [InlineData(0, 0, 0, 64, 3)]
    [InlineData(0, 0, 0, 5 * 64, 2)]
    [InlineData(0, 0, 0, 11 * 64, 2)]
    [InlineData(0, 0, 0, 12 * 64, 0)]
    [InlineData(0, 6, 0, 1000, 10)]
    [InlineData(20, 0, 0, 1, 4)]
    public void PaletteFlashesAreVanillas(int damagecount, int bonuscount, int ironfeet, int strength, int palette)
    {
        var p = new player_t();
        p.damagecount = damagecount;
        p.bonuscount = bonuscount;
        p.powers[(int)powertype_t.pw_ironfeet] = ironfeet;
        p.powers[(int)powertype_t.pw_strength] = strength;
        Assert.Equal(palette, StStuff.ST_doPaletteStuff(p));
    }
}
