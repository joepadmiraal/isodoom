using System;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T6.1: the state machine (<c>P_SetMobjState</c>, <c>P_MobjThinker</c>)
/// with every action of <c>states[]</c> dispatched (mobj actions through
/// <see cref="World.A_Call"/>, psprite actions through
/// <see cref="World.A_CallWeapon"/>), the Nightmare respawn, and the ammo,
/// weapon and backpack pickups that the vanilla routes' <c>things</c> and
/// <c>inventory</c> columns need. The spawn-state animations of every map
/// thing are checked against vanilla along every route (<see cref="VanillaRoute"/>).
/// </summary>
public class StateMachineTests
{
    private const int FRACUNIT = 1 << 16;

    private static int F(int units) => units * FRACUNIT;

    private static WadArchive Wad() => new(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) });

    private static World NewWorld(skill_t skill = skill_t.sk_medium, string map = "E1M1")
    {
        var world = new World(new SpawnSettings(GameMode.shareware, skill), Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(Wad(), map));
        return world;
    }

    private static void Tic(World world, int tics = 1)
    {
        for (int i = 0; i < tics; i++)
            world.G_Ticker(default(ticcmd_t));
    }

    private static IEnumerable<actionf_t> Actions() =>
        Enum.GetValues<actionf_t>().Where(a => a != actionf_t.NULL);

    // ---- dispatch ----

    [Fact]
    public void EveryActionHasExactlyOneDispatch()
    {
        World world = NewWorld();
        mobj_t mo = world.players[0].mo!;
        foreach (actionf_t action in Actions())
        {
            var psp = new pspdef_t { state = statenum_t.S_CHAIN1 }; // (A_FireCGun's flash follows the frame)
            if (World.IsWeaponAction(action))
            {
                world.A_CallWeapon(action, world.players[0], psp);
                Assert.Throws<InvalidOperationException>(() => world.A_Call(action, mo));
            }
            else
            {
                world.A_Call(action, mo);
                Assert.Throws<InvalidOperationException>(() => world.A_CallWeapon(action, world.players[0], psp));
            }
        }
    }

    /// <summary>The states a thing type or a weapon can reach, following <c>nextstate</c>.</summary>
    private static HashSet<statenum_t> Reachable(IEnumerable<statenum_t> starts)
    {
        var seen = new HashSet<statenum_t>();
        var todo = new Stack<statenum_t>(starts);
        while (todo.Count > 0)
        {
            statenum_t s = todo.Pop();
            if (s == statenum_t.S_NULL || !seen.Add(s))
                continue;
            todo.Push(Info.states[(int)s].nextstate);
        }
        return seen;
    }

    [Fact]
    public void MobjStatesHaveMobjActionsAndWeaponStatesWeaponActions()
    {
        HashSet<statenum_t> mobj = Reachable(Info.mobjinfo.SelectMany(i => new[]
        {
            i.spawnstate, i.seestate, i.painstate, i.meleestate, i.missilestate, i.deathstate, i.xdeathstate, i.raisestate,
        }).Append(statenum_t.S_BRAINEXPLODE1)); // set by A_BrainScream's rockets
        HashSet<statenum_t> weapon = Reachable(Info.weaponinfo.SelectMany(w => new[]
        {
            w.upstate, w.downstate, w.readystate, w.atkstate, w.flashstate,
        }));

        Assert.Empty(mobj.Intersect(weapon));
        Assert.All(mobj, s => Assert.False(World.IsWeaponAction(Info.states[(int)s].action), $"{s}"));
        Assert.All(weapon, s => Assert.True(Info.states[(int)s].action == actionf_t.NULL || World.IsWeaponAction(Info.states[(int)s].action), $"{s}"));

        // Every action of states[] is used by one side or the other.
        var used = mobj.Concat(weapon).Select(s => Info.states[(int)s].action).ToHashSet();
        Assert.All(Actions(), a => Assert.Contains(a, used));
    }

    [Fact]
    public void EnteringAStateRunsItsAction()
    {
        World world = NewWorld();
        mobj_t imp = world.Mobjs().First(m => m.type == mobjtype_t.MT_TROOP);
        Assert.True((imp.flags & mobjflag_t.MF_SOLID) != 0);

        Assert.True(world.P_SetMobjState(imp, statenum_t.S_TROO_DIE4)); // A_Fall
        Assert.True((imp.flags & mobjflag_t.MF_SOLID) == 0);
    }

    // ---- state tics ----

    [Fact]
    public void TheBarrelCyclesThroughItsSpawnStates()
    {
        World world = NewWorld();
        mobj_t barrel = world.Mobjs().Single(m => m.type == mobjtype_t.MT_BARREL);
        Assert.Equal(statenum_t.S_BAR1, barrel.state);
        Assert.InRange(barrel.tics, 1, 6); // P_SpawnMapThing's random first tic count

        Tic(world, barrel.tics);
        for (int cycle = 0; cycle < 4; cycle++)
        {
            Assert.Equal(cycle % 2 == 0 ? statenum_t.S_BAR2 : statenum_t.S_BAR1, barrel.state);
            Assert.Equal(6, barrel.tics);
            Assert.Equal(cycle % 2 == 0 ? 1 : 0, barrel.frame);
            Tic(world, 6);
        }
    }

    [Fact]
    public void AStateThatLastsForeverStays()
    {
        World world = NewWorld();
        mobj_t lamp = world.Mobjs().Single(m => m.type == mobjtype_t.MT_MISC31);
        Assert.Equal((statenum_t.S_COLU, -1), (lamp.state, lamp.tics));
        Tic(world, 100);
        Assert.Equal((statenum_t.S_COLU, -1), (lamp.state, lamp.tics));
    }

    // ---- Nightmare respawn ----

    /// <summary>Kills the synthetic E1M1's imp at (320, 64) and runs tics until it respawns (or <paramref name="tics"/> pass); returns the tics run.</summary>
    private static int KillAndWait(World world, out mobj_t corpse, int tics = 35 * 120)
    {
        corpse = world.Mobjs().Single(m => m.type == mobjtype_t.MT_TROOP && m.spawnpoint.X == 320);
        world.P_KillMobj(null, corpse);
        for (int i = 1; i <= tics; i++)
        {
            Tic(world);
            if (corpse.function == think_t.REMOVED)
                return i;
        }
        return -1;
    }

    [Fact]
    public void OnNightmareACorpseRespawnsAtItsSpot()
    {
        World world = NewWorld(skill_t.sk_nightmare);
        int imps = world.Mobjs().Count(m => m.type == mobjtype_t.MT_TROOP);
        int tics = KillAndWait(world, out mobj_t corpse);

        // The death states take 8 + 8 + 6 + 6 tics (less 0-3) before the
        // corpse lasts forever, then 12 seconds, then a 32nd tic and P_Random <= 4.
        Assert.InRange(tics, 25 + 12 * 35, 35 * 120);
        Assert.Equal(0, (world.leveltime - 1) & 31);
        Assert.Equal(statenum_t.S_TROO_DIE5, corpse.state);

        mobj_t imp = world.Mobjs().Last(m => m.type == mobjtype_t.MT_TROOP);
        Assert.NotSame(corpse, imp);
        Assert.Equal(imps, world.Mobjs().Count(m => m.type == mobjtype_t.MT_TROOP));
        Assert.Equal((F(320), F(64)), (imp.x, imp.y));
        Assert.Equal(corpse.spawnpoint, imp.spawnpoint);
        Assert.Equal(Tables.ANG180, imp.angle);
        Assert.Equal(18, imp.reactiontime);
        Assert.True((imp.flags & mobjflag_t.MF_SOLID) != 0);

        // Teleport fogs at both spots (here the same: the corpse did not move), with their sounds.
        mobj_t[] fogs = world.Mobjs().Where(m => m.type == mobjtype_t.MT_TFOG).ToArray();
        Assert.Equal(2, fogs.Length);
        Assert.All(fogs, f => Assert.Equal((F(320), F(64)), (f.x, f.y)));
        Assert.Equal(2, world.sounds.Count(s => s.sfx == sfxenum_t.sfx_telept));
    }

    [Fact]
    public void BelowNightmareCorpsesStay()
    {
        World world = NewWorld(skill_t.sk_hard);
        Assert.Equal(-1, KillAndWait(world, out mobj_t corpse, 35 * 60));
        Assert.Equal(statenum_t.S_TROO_DIE5, corpse.state);
        Assert.Equal(0, corpse.movecount);
    }

    [Fact]
    public void ABlockedSpotDelaysTheRespawn()
    {
        World world = NewWorld(skill_t.sk_nightmare);
        mobj_t player = world.players[0].mo!;
        world.PlaceMobj(player, F(320), F(64));
        // T6.4: the other monsters would wake and push the player off the spot
        foreach (mobj_t m in world.Mobjs().Where(m => (m.flags & mobjflag_t.MF_COUNTKILL) != 0 && m.spawnpoint.X != 320).ToList())
            world.P_RemoveMobj(m);
        Assert.Equal(-1, KillAndWait(world, out mobj_t corpse, 35 * 60));
        Assert.True(corpse.movecount >= 12 * 35);

        world.PlaceMobj(player, F(0), F(0));
        for (int i = 0; i < 35 * 60 && corpse.function != think_t.REMOVED; i++)
            Tic(world);
        Assert.Equal(think_t.REMOVED, corpse.function);
    }

    // ---- ammo, weapon and backpack pickups ----

    private static mobj_t Touch(World world, mobjtype_t type, bool dropped = false)
    {
        mobj_t player = world.players[0].mo!;
        mobj_t item = world.P_SpawnMobj(player.x, player.y, World.ONFLOORZ, type);
        if (dropped)
            item.flags |= mobjflag_t.MF_DROPPED;
        world.P_TouchSpecialThing(item, player);
        return item;
    }

    [Fact]
    public void ClipsAndBoxesGiveTheirAmmo()
    {
        World world = NewWorld();
        player_t p = world.players[0];
        Assert.Equal(50, p.ammo[(int)ammotype_t.am_clip]);

        mobj_t clip = Touch(world, mobjtype_t.MT_CLIP);
        Assert.Equal(think_t.REMOVED, clip.function);
        Assert.Equal((60, World.GOTCLIP), (p.ammo[(int)ammotype_t.am_clip], p.message));
        Touch(world, mobjtype_t.MT_CLIP, dropped: true); // half a clip
        Assert.Equal(65, p.ammo[(int)ammotype_t.am_clip]);
        Touch(world, mobjtype_t.MT_MISC22); // 4 shells
        Assert.Equal(4, p.ammo[(int)ammotype_t.am_shell]);

        p.ammo[(int)ammotype_t.am_clip] = 195;
        Touch(world, mobjtype_t.MT_CLIP);
        Assert.Equal(200, p.ammo[(int)ammotype_t.am_clip]); // up to the maximum
        mobj_t full = Touch(world, mobjtype_t.MT_CLIP);
        Assert.NotEqual(think_t.REMOVED, full.function); // left lying
    }

    [Theory]
    [InlineData(skill_t.sk_baby, 20)]
    [InlineData(skill_t.sk_easy, 10)]
    [InlineData(skill_t.sk_nightmare, 20)]
    public void BabyAndNightmareDoubleTheAmmo(skill_t skill, int clip)
    {
        World world = NewWorld(skill);
        world.players[0].ammo[(int)ammotype_t.am_clip] = 0;
        Touch(world, mobjtype_t.MT_CLIP);
        Assert.Equal(clip, world.players[0].ammo[(int)ammotype_t.am_clip]);
    }

    [Fact]
    public void AmmoAfterNoneSwitchesFromTheFist()
    {
        World world = NewWorld();
        player_t p = world.players[0];
        p.readyweapon = weapontype_t.wp_fist;
        p.pendingweapon = weapontype_t.wp_nochange;
        p.ammo[(int)ammotype_t.am_clip] = 0;
        Touch(world, mobjtype_t.MT_CLIP);
        Assert.Equal(weapontype_t.wp_pistol, p.pendingweapon);

        // Ammo while some is left does not switch.
        p.pendingweapon = weapontype_t.wp_nochange;
        Touch(world, mobjtype_t.MT_CLIP);
        Assert.Equal(weapontype_t.wp_nochange, p.pendingweapon);
    }

    [Fact]
    public void WeaponsGiveTheWeaponAndAmmo()
    {
        World world = NewWorld();
        player_t p = world.players[0];

        mobj_t shotgun = Touch(world, mobjtype_t.MT_SHOTGUN);
        Assert.Equal(think_t.REMOVED, shotgun.function);
        Assert.True(p.weaponowned[(int)weapontype_t.wp_shotgun]);
        Assert.Equal(weapontype_t.wp_shotgun, p.pendingweapon);
        Assert.Equal(8, p.ammo[(int)ammotype_t.am_shell]); // two clips
        Assert.Equal(World.GOTSHOTGUN, p.message);
        Assert.Contains(world.sounds, s => s.sfx == sfxenum_t.sfx_wpnup);

        Touch(world, mobjtype_t.MT_SHOTGUN, dropped: true); // ammo only, one clip
        Assert.Equal(12, p.ammo[(int)ammotype_t.am_shell]);

        p.ammo[(int)ammotype_t.am_shell] = p.maxammo[(int)ammotype_t.am_shell];
        mobj_t again = Touch(world, mobjtype_t.MT_SHOTGUN);
        Assert.NotEqual(think_t.REMOVED, again.function); // nothing needed: left lying
    }

    [Fact]
    public void TheBackpackDoublesTheMaximumOnce()
    {
        World world = NewWorld();
        player_t p = world.players[0];
        Touch(world, mobjtype_t.MT_MISC24);
        Assert.True(p.backpack);
        Assert.Equal(new[] { 400, 100, 600, 100 }, p.maxammo);
        Assert.Equal(new[] { 60, 4, 20, 1 }, p.ammo);
        Assert.Equal(World.GOTBACKPACK, p.message);

        Touch(world, mobjtype_t.MT_MISC24);
        Assert.Equal(new[] { 400, 100, 600, 100 }, p.maxammo);
        Assert.Equal(new[] { 70, 8, 40, 2 }, p.ammo);
    }
}
