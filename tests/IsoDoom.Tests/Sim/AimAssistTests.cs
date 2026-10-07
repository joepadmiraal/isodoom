using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T6.7: the horizontal aim assist (SPEC §6.3 #2, §12 T6.7;
/// <see cref="World.P_AimAssist"/>, <see cref="Tweaks.AimAssistCone"/>):
/// the nearest visible, living target with a part in the cone, nothing
/// outside it, and vanilla results with the tweak off. The tweak is tested
/// on top of <see cref="Tweaks.Vanilla"/> (relative turning, so a
/// <c>ticcmd</c> without <c>angleturn</c> keeps the player facing east).
/// </summary>
public class AimAssistTests
{
    private const int FRACUNIT = 1 << 16;

    private static int F(int units) => units * FRACUNIT;

    private static Tweaks Assist(uint cone = Tweaks.DefaultAimAssistCone) => Tweaks.Vanilla with { AimAssistCone = cone };

    /// <summary>One long room, x 0..1280, y 0..512, floor 0, ceiling 128; the player at (64, 256) facing east.</summary>
    private static World Room(Tweaks tweaks) =>
        TestMap.Strip(0, 0, 512, new TestMap.Room(1280, 0, 128)).Player(64, 256).Load(tweaks: tweaks);

    private static mobj_t Player(World world) => world.players[0].mo!;

    /// <summary>A zombieman spawned now: it stands still for its spawn state's 10 tics.</summary>
    private static mobj_t Zombie(World world, int x, int y) =>
        world.P_SpawnMobj(F(x), F(y), World.ONFLOORZ, mobjtype_t.MT_POSSESSED);

    private static uint AngleTo(World world, mobj_t target) =>
        Tables.R_PointToAngle2(Player(world).x, Player(world).y, target.x, target.y);

    private static uint Snap(World world) => world.P_AimAssist(Player(world), 16 * 64 * FRACUNIT);

    // ---- the cone ----

    [Fact]
    public void SnapsToATargetInTheConeTheFacingMisses()
    {
        World world = Room(Assist());
        mobj_t z = Zombie(world, 664, 306); // 4.8° off at 600 units, its edge 2.9°; the facing passes 30 units beside it
        Assert.Null(TraceFacing(world));
        Assert.Equal(AngleTo(world, z), Snap(world));
        Assert.Same(z, world.linetarget);
    }

    [Fact]
    public void NothingOutsideTheCone()
    {
        World world = Room(Assist());
        mobj_t z = Zombie(world, 664, 340); // 8° off at 600 units, its edge 6.1°
        Assert.Equal(0u, Snap(world));
        // the strength option: a wider cone takes it
        world = Room(Assist(10 * Tables.ANG1));
        z = Zombie(world, 664, 340);
        Assert.Equal(AngleTo(world, z), Snap(world));
        // the cone is the same either side
        world = Room(Assist());
        Zombie(world, 664, 256 - 84);
        Assert.Equal(0u, Snap(world));
    }

    [Fact]
    public void ACloseTargetCountsWithItsWidth()
    {
        // 50 units ahead, 30 to the side: its centre is 31° off but its edge 14° (its half-width as seen from the player 17°)
        World world = Room(Assist());
        Zombie(world, 114, 286);
        Assert.Equal(0u, world.P_AimAssist(Player(world), World.MELEERANGE));
        world = Room(Assist(15 * Tables.ANG1));
        mobj_t z = Zombie(world, 114, 286);
        Assert.Equal(AngleTo(world, z), world.P_AimAssist(Player(world), World.MELEERANGE));
    }

    [Fact]
    public void PicksTheNearestTarget()
    {
        foreach (bool nearFirst in new[] { true, false })
        {
            World world = Room(Assist());
            mobj_t near = null!, far;
            if (nearFirst)
                near = Zombie(world, 464, 216); // 5.7° right at 400 units, its edge 2.9°
            far = Zombie(world, 664, 306); // 4.8° left at 600 units
            if (!nearFirst)
                near = Zombie(world, 464, 216);
            Assert.Equal(AngleTo(world, near), Snap(world));
            Assert.NotEqual(AngleTo(world, far), Snap(world));
        }
    }

    [Fact]
    public void KeepsTheFacingWhenItHitsATarget()
    {
        World world = Room(Assist());
        mobj_t ahead = Zombie(world, 864, 256); // straight ahead, 800 units
        Zombie(world, 464, 216); // nearer, in the cone
        Assert.Equal(0u, Snap(world));
        Assert.Same(ahead, world.linetarget);
    }

    [Fact]
    public void OnlyTargetsWithinTheAimRange()
    {
        World world = Room(Assist());
        Zombie(world, 1164, 256 + 70); // 1100 units, its edge 2.6° off: beyond 1024 units
        Assert.Equal(0u, Snap(world));
    }

    // ---- valid targets ----

    [Fact]
    public void SkipsTheDeadTheUnshootableAndPlayers()
    {
        World world = Room(Assist());
        mobj_t dead = Zombie(world, 464, 216);
        world.P_DamageMobj(dead, null, null, 1000);
        Assert.True(dead.health <= 0);
        mobj_t lamp = world.P_SpawnMobj(F(364), F(236), World.ONFLOORZ, mobjtype_t.MT_MISC31); // a floor lamp: solid, not shootable
        Assert.Equal(0, (int)(lamp.flags & mobjflag_t.MF_SHOOTABLE));
        mobj_t far = Zombie(world, 664, 306);
        Assert.Equal(AngleTo(world, far), Snap(world));

        // a thing with health but no MF_SHOOTABLE is no target either
        world = Room(Assist());
        mobj_t z = Zombie(world, 664, 306);
        z.flags &= ~mobjflag_t.MF_SHOOTABLE;
        Assert.Equal(0u, Snap(world));

        // another player is no target (co-op)
        world = Room(Assist());
        z = Zombie(world, 664, 306);
        z.player = new player_t();
        Assert.Equal(0u, Snap(world));
    }

    [Fact]
    public void SkipsATargetTheAimCannotSee()
    {
        // room A (the player), a 64-unit ledge, room C at floor 0 (hidden behind the ledge), room D at floor 120 (seen over it)
        World world = TestMap.Strip(0, 0, 256,
                new TestMap.Room(256, 0, 256), new TestMap.Room(64, 64, 256),
                new TestMap.Room(192, 0, 256), new TestMap.Room(512, 120, 256))
            .Player(64, 128).Load(tweaks: Assist());
        mobj_t hidden = Zombie(world, 400, 152); // 336 units, 4.1° left: nearest, behind the ledge
        mobj_t seen = Zombie(world, 600, 104); // 536 units, 2.6° right, on room D's floor
        Assert.Equal(F(120), seen.z);
        world.P_AimLineAttack(Player(world), AngleTo(world, hidden), 16 * 64 * FRACUNIT);
        Assert.Null(world.linetarget);
        Assert.Equal(AngleTo(world, seen), Snap(world));
        Assert.Same(seen, world.linetarget);

        // nothing seen in the cone: the facing
        world.P_RemoveMobj(seen);
        Assert.Equal(0u, Snap(world));
    }

    // ---- the tweak off ----

    [Fact]
    public void OffItDoesNothing()
    {
        World world = Room(Tweaks.Vanilla);
        Assert.False(world.tweaks.AimAssist);
        Zombie(world, 664, 306);
        mobj_t sentinel = Zombie(world, 300, 450);
        world.linetarget = sentinel;
        Assert.Equal(0u, Snap(world));
        Assert.Same(sentinel, world.linetarget); // no aim traced at all
        Assert.Equal(Tweaks.Vanilla, Tweaks.Vanilla with { AimAssistCone = 0 });
        Assert.Equal(Tweaks.DefaultAimAssistCone, Tweaks.TopDown.AimAssistCone);
    }

    // ---- the attacks ----

    /// <summary>The pistol up and ready in <paramref name="world"/>.</summary>
    private static void Ready(World world)
    {
        for (int i = 0; i < 100 && world.players[0].psprites[World.ps_weapon].state != statenum_t.S_PISTOL; i++)
            world.G_Ticker(new ticcmd_t());
        Assert.Equal(statenum_t.S_PISTOL, world.players[0].psprites[World.ps_weapon].state);
    }

    /// <summary>One pistol shot (fire held for a tic, then 11 tics).</summary>
    private static void Shoot(World world)
    {
        world.G_Ticker(new ticcmd_t { buttons = buttoncode_t.BT_ATTACK });
        for (int i = 0; i < 11; i++)
            world.G_Ticker(new ticcmd_t());
    }

    [Fact]
    public void ThePistolHitsWhatVanillaMisses()
    {
        foreach (bool assist in new[] { false, true })
        {
            World world = Room(assist ? Assist() : Tweaks.Vanilla);
            Ready(world);
            mobj_t z = Zombie(world, 664, 306);
            int spawnhealth = z.health;
            Shoot(world);
            if (assist)
                Assert.True(z.health < spawnhealth, "the snapped shot hits");
            else
                Assert.Equal(spawnhealth, z.health); // vanilla: the slope found 5.6° left, the bullet along the facing
            Assert.Equal(0u, Player(world).angle); // the player's facing is untouched
        }
    }

    [Fact]
    public void TheShotgunSpreadsAroundTheSnappedAngle()
    {
        World world = Room(Assist());
        world.players[0].weaponowned[(int)weapontype_t.wp_shotgun] = true;
        world.players[0].ammo[(int)ammotype_t.am_shell] = 10;
        Ready(world);
        world.players[0].pendingweapon = weapontype_t.wp_shotgun;
        for (int i = 0; i < 100 && world.players[0].psprites[World.ps_weapon].state != statenum_t.S_SGUN; i++)
            world.G_Ticker(new ticcmd_t());
        mobj_t z = Zombie(world, 464, 216);
        Shoot(world);
        Assert.True(z.health < z.info.spawnhealth);
    }

    [Theory]
    [InlineData(664, 400)] // outside the cone
    [InlineData(864, 256)] // straight ahead
    [InlineData(-1, -1)] // no target
    public void WithoutASnapTheShotIsVanillas(int x, int y)
    {
        World vanilla = Room(Tweaks.Vanilla), assisted = Room(Assist());
        foreach (World world in new[] { vanilla, assisted })
        {
            Ready(world);
            if (x >= 0)
                Zombie(world, x, y);
            Shoot(world);
            Shoot(world);
        }
        Assert.Equal(vanilla.Checksum(), assisted.Checksum());
        Assert.Equal(vanilla.random.prndindex, assisted.random.prndindex);
    }

    [Fact]
    public void TheRocketFliesAlongTheSnappedAngle()
    {
        World vanilla = Room(Tweaks.Vanilla);
        Zombie(vanilla, 664, 306);
        mobj_t rocket = vanilla.P_SpawnPlayerMissile(Player(vanilla), mobjtype_t.MT_ROCKET);
        Assert.Equal(1u << 26, rocket.angle); // vanilla: the facing's first miss, then 5.6° left

        World world = Room(Assist());
        mobj_t z = Zombie(world, 664, 306);
        rocket = world.P_SpawnPlayerMissile(Player(world), mobjtype_t.MT_ROCKET);
        Assert.Equal(AngleTo(world, z), rocket.angle);
        Assert.Equal(0u, Player(world).angle);
    }

    [Fact]
    public void ThePunchSnapsWithAWideCone()
    {
        World world = Room(Assist(15 * Tables.ANG1));
        world.players[0].psprites[World.ps_weapon].state = statenum_t.S_PUNCH; // only for the call; A_Punch reads no psprite
        mobj_t z = Zombie(world, 114, 286);
        world.A_Punch(world.players[0], world.players[0].psprites[World.ps_weapon]);
        Assert.Same(z, world.linetarget);
        Assert.True(z.health < z.info.spawnhealth);
        Assert.Equal(AngleTo(world, z), Player(world).angle); // A_Punch turns to what it hit, as vanilla
    }

    private static mobj_t? TraceFacing(World world)
    {
        world.P_AimLineAttack(Player(world), Player(world).angle, 16 * 64 * FRACUNIT);
        return world.linetarget;
    }
}
