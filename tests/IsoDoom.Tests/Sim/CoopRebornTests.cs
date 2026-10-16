using System;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T11.3: g_game.c's net game reborn (<c>G_DoReborn</c>'s co-op branch,
/// <c>G_CheckSpot</c>, the body queue) on the co-op test map (four starts
/// at x = 700, 128 units apart, facing west). The routes
/// <c>synthetic-coop-reborn</c>, <c>testmap-coop-reborn</c>,
/// <c>coop-e1m1-reborn</c>, <c>coop-e1m1-blocked</c> and
/// <c>coop-e1m1-bodyque</c> compare it with vanilla tic by tic.
/// </summary>
public sealed class CoopRebornTests
{
    private static readonly int[] _lineY = [64, 192, 320, 448];

    private static World CoopWorld(int players = 4) =>
        NetSim.MakeWorld(NetSimTests.CoopWad(), "E1M1", new SpawnSettings(GameMode.shareware, skill_t.sk_medium, netgame: true, nomonsters: true,
            playersingame: NetSim.FirstPlayers(players)), Tweaks.Vanilla, 0);

    private static readonly ticcmd_t[] _idle = new ticcmd_t[World.MAXPLAYERS];

    // Kills player i, then presses its use: it is PST_REBORN after that tic, reborn at the next's start.
    private static mobj_t KillAndUse(World world, int i)
    {
        mobj_t body = world.players[i].mo!;
        world.P_DamageMobj(body, null, null, 10000);
        world.G_Ticker(_idle);
        Assert.Equal(playerstate_t.PST_DEAD, world.players[i].playerstate);
        var use = new ticcmd_t[World.MAXPLAYERS];
        use[i].buttons = buttoncode_t.BT_USE;
        world.G_Ticker(use);
        Assert.Equal(playerstate_t.PST_REBORN, world.players[i].playerstate);
        Assert.Same(body, world.players[i].mo); // vanilla's G_Ticker reborns at the next tic's start
        Assert.Equal(gameaction_t.ga_nothing, world.gameaction); // no reload in a net game
        return body;
    }

    private static (int X, int Y) StartOf(int i) => (700 << Fixed.FRACBITS, _lineY[i] << Fixed.FRACBITS);

    [Fact]
    public void ACoopPlayerRebornsAtItsStartAndItsBodyStays()
    {
        World world = CoopWorld();
        Level level = world.level;
        world.PlaceMobj(world.players[1].mo!, 300 << Fixed.FRACBITS, _lineY[1] << Fixed.FRACBITS);
        int mobjs = world.Mobjs().Count();
        mobj_t body = KillAndUse(world, 1);
        world.G_Ticker(_idle);
        player_t p = world.players[1];
        mobj_t mo = p.mo!;
        Assert.NotSame(body, mo);
        Assert.Same(level, world.level);
        Assert.Equal((playerstate_t.PST_LIVE, 100, weapontype_t.wp_pistol), (p.playerstate, p.health, p.readyweapon));
        Assert.Equal(StartOf(1), (mo.x, mo.y));
        Assert.Equal((mobjflag_t)(1 << Info.MF_TRANSSHIFT), mo.flags & mobjflag_t.MF_TRANSLATION);
        Assert.Null(body.player); // the body is no longer its player's
        Assert.NotEqual(think_t.REMOVED, body.function);
        Assert.Equal(1, world.bodyqueslot);
        Assert.Same(body, world.bodyque[0]);
        // a teleport fog: west-facing start, the DOS executable's angle (Chocolate Doom's emulation)
        (int xa, int ya) = World.G_CheckSpotFogOffset(180);
        mobj_t fog = Assert.Single(world.Mobjs(), m => m.type == mobjtype_t.MT_TFOG);
        Assert.Equal((StartOf(1).X + (20 * xa), StartOf(1).Y + (20 * ya)), (fog.x, fog.y));
        Assert.Equal(mobjs + 2, world.Mobjs().Count()); // the new player and the fog
        Assert.Contains(world.events, e => e.type == simevent_t.se_startsound && e.sound.sfx == sfxenum_t.sfx_telept && e.sound.origin == fog);
    }

    [Fact]
    public void ABlockedStartSendsThePlayerToTheFirstFreeOne()
    {
        World world = CoopWorld();
        // player 1 stands on player 3's start, player 3 dies elsewhere
        world.PlaceMobj(world.players[2].mo!, 300 << Fixed.FRACBITS, _lineY[2] << Fixed.FRACBITS);
        world.PlaceMobj(world.players[0].mo!, StartOf(2).X, StartOf(2).Y);
        KillAndUse(world, 2);
        world.G_Ticker(_idle);
        mobj_t mo = world.players[2].mo!;
        Assert.Equal(StartOf(0), (mo.x, mo.y)); // player 1's start, free now
        Assert.Equal((mobjflag_t)(2 << Info.MF_TRANSSHIFT), mo.flags & mobjflag_t.MF_TRANSLATION); // as player 3, coloured as itself
        Assert.Same(world.players[2], mo.player);
        Assert.Equal((short)1, world.playerstarts[0]!.Value.Type); // the start itself is not changed
        Assert.Equal(1, world.bodyqueslot);
    }

    [Fact]
    public void WithEveryStartBlockedThePlayerSpawnsInsideSomething()
    {
        World world = CoopWorld();
        // player 2 dies away from the starts; players 1, 3 and 4 stand on starts 2, 1 and 3, a barrel on start 4
        world.PlaceMobj(world.players[1].mo!, 300 << Fixed.FRACBITS, _lineY[1] << Fixed.FRACBITS);
        world.PlaceMobj(world.players[0].mo!, StartOf(1).X, StartOf(1).Y);
        world.PlaceMobj(world.players[2].mo!, StartOf(0).X, StartOf(0).Y);
        world.PlaceMobj(world.players[3].mo!, StartOf(2).X, StartOf(2).Y);
        world.P_SpawnMobj(StartOf(3).X, StartOf(3).Y, World.ONFLOORZ, mobjtype_t.MT_BARREL);
        KillAndUse(world, 1);
        world.G_Ticker(_idle);
        mobj_t mo = world.players[1].mo!;
        Assert.Equal(StartOf(1), (mo.x, mo.y)); // its own start, inside player 1
        Assert.Equal(0, world.bodyqueslot); // G_CheckSpot never passed: no body queued, no fog
        Assert.DoesNotContain(world.Mobjs(), m => m.type == mobjtype_t.MT_TFOG);
    }

    [Fact]
    public void TheBodyQueueRemovesTheOldestPast32()
    {
        World world = CoopWorld(2);
        world.PlaceMobj(world.players[0].mo!, 300 << Fixed.FRACBITS, _lineY[0] << Fixed.FRACBITS);
        var bodies = new System.Collections.Generic.List<mobj_t>();
        for (int n = 0; n < World.BODYQUESIZE + 2; n++)
        {
            bodies.Add(KillAndUse(world, 1));
            world.G_Ticker(_idle);
        }
        Assert.Equal(World.BODYQUESIZE + 2, world.bodyqueslot);
        Assert.Equal(think_t.REMOVED, bodies[0].function);
        Assert.Equal(think_t.REMOVED, bodies[1].function);
        Assert.All(bodies.Skip(2), b => Assert.NotEqual(think_t.REMOVED, b.function));
        Assert.Equal(World.BODYQUESIZE, world.Mobjs().Count(m => m.type == mobjtype_t.MT_PLAYER && m.player is null));
        Assert.Same(bodies[^1], world.bodyque[(World.BODYQUESIZE + 1) % World.BODYQUESIZE]);

        // a new level clears the queue
        world.G_DoLoadLevel(Level.Load(NetSimTests.CoopWad(), "E1M1"));
        Assert.Equal(0, world.bodyqueslot);
        Assert.All(world.bodyque, b => Assert.Null(b));
    }

    [Fact]
    public void TheBodyQueueIsSaved()
    {
        World world = CoopWorld(2);
        world.PlaceMobj(world.players[0].mo!, 300 << Fixed.FRACBITS, _lineY[0] << Fixed.FRACBITS);
        for (int n = 0; n < World.BODYQUESIZE - 1; n++)
        {
            KillAndUse(world, 1);
            world.G_Ticker(_idle);
        }
        // saved with a player dead and about to reborn (PST_REBORN): the load reborns it at its next tic
        KillAndUse(world, 1);
        World loaded = SaveGameTests.Load(SaveGameTests.Save(world), NetSimTests.CoopWad());
        Assert.Equal(world.bodyqueslot, loaded.bodyqueslot);
        Assert.Equal(world.Checksum(), loaded.Checksum());
        for (int n = 0; n < 4; n++)
        {
            world.G_Ticker(_idle);
            loaded.G_Ticker(_idle);
            Assert.Equal(world.Checksum(), loaded.Checksum());
            KillAndUse(world, 1);
            KillAndUse(loaded, 1);
            Assert.Equal(world.Checksum(), loaded.Checksum());
        }
        Assert.Equal(World.BODYQUESIZE + 3, loaded.bodyqueslot);
        Assert.Equal(world.Mobjs().Count(), loaded.Mobjs().Count());
        Assert.Equal(World.BODYQUESIZE, loaded.Mobjs().Count(m => m.type == mobjtype_t.MT_PLAYER && m.player is null));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(45, 1024)]
    [InlineData(90, 2048)]
    [InlineData(135, 3072)]
    public void TheFogIsAheadOfTheStartBelow180(int angle, int an)
    {
        Assert.Equal((Tables.finecosine[an], Tables.finesine[an]), World.G_CheckSpotFogOffset(angle));
        Assert.Equal(World.G_CheckSpotFogOffset(angle), World.G_CheckSpotFogOffset(angle + 44)); // angle / 45
    }

    [Fact]
    public void TheFogReadsBeforeTheTablesFrom180()
    {
        // the DOS executable's negative index: finecosine[-n] and finesine[-n] are the entries before them
        Assert.Equal((Tables.finetangent[2048], Tables.finetangent[0]), World.G_CheckSpotFogOffset(180));
        Assert.Equal((Tables.finetangent[3072], Tables.finetangent[1024]), World.G_CheckSpotFogOffset(225));
        Assert.Equal((Tables.finesine[0], Tables.finetangent[2048]), World.G_CheckSpotFogOffset(270));
        Assert.Equal((Tables.finesine[1024], Tables.finetangent[3072]), World.G_CheckSpotFogOffset(315));
        Assert.Throws<WadFormatException>(() => World.G_CheckSpotFogOffset(360));
        Assert.Throws<WadFormatException>(() => World.G_CheckSpotFogOffset(-45));
    }

    [Fact]
    public void BeforeAPlayerHasAMobjOnlyEarlierPlayersOnTheSpotBlockIt()
    {
        // deathmatch's first spawn (G_DeathMatchSpawnPlayer, T11.4): no body, so no position check, no queue, no fog
        World world = CoopWorld();
        mobj_t? mo = world.players[2].mo;
        world.players[2].mo = null;
        var onPlayer1 = new MapThing(700, 64, 180, 1, 7);
        var free = new MapThing(100, 64, 180, 1, 7);
        Assert.False(world.G_CheckSpot(2, onPlayer1));
        Assert.True(world.G_CheckSpot(2, free));
        Assert.Equal(0, world.bodyqueslot);
        world.players[2].mo = mo;
        mobj_t? first = world.players[0].mo;
        world.players[0].mo = null;
        Assert.True(world.G_CheckSpot(0, new MapThing(700, 192, 180, 1, 7))); // on player 2, but no player before player 1
        world.players[0].mo = first;
    }
}
