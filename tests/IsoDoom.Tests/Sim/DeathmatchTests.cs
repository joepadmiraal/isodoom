using System;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T11.4: deathmatch and altdeath on the sim's side: the deathmatch starts
/// (<see cref="World.G_DeathMatchSpawnPlayer"/>), the netgame's and the
/// single player's reborn, <c>-timer</c>, and worlds of other console
/// players in sync. The routes against vanilla are <c>testmap-dm</c>,
/// <c>testmap-altdeath</c> (CI) and DOOM1's <c>dm-e1m1-2p</c>,
/// <c>dm2-e1m1-respawn</c> and <c>dm-e1m1-timer</c> (<see cref="VanillaRouteTests"/>).
/// </summary>
public sealed class DeathmatchTests
{
    private static WadArchive DmWad() => new([WadFile.FromBytes(RouteTestMaps.Deathmatch().Build(), "dm.wad")]);

    // RouteTestMaps.Deathmatch's deathmatch starts
    private static readonly (int X, int Y)[] _starts = [(128, 128), (896, 128), (896, 384), (128, 384), (512, 256)];

    private static World NewWorld(int deathmatch, int players = 1, int timelimit = 0)
    {
        var world = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium, netgame: players > 1, deathmatch: deathmatch, nomonsters: true,
            playersingame: NetSim.FirstPlayers(players), timelimit: timelimit), Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(DmWad(), "E1M1"));
        return world;
    }

    private static bool AtAStart(mobj_t mo) => _starts.Contains((mo.x >> Fixed.FRACBITS, mo.y >> Fixed.FRACBITS));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(1, 4)]
    public void PlayersSpawnAtDeathmatchStartsWithEveryCard(int deathmatch, int players)
    {
        World world = NewWorld(deathmatch, players);
        Assert.Equal(5, world.deathmatchstarts.Count);
        for (int i = 0; i < players; i++)
        {
            mobj_t mo = world.players[i].mo!;
            Assert.True(AtAStart(mo), $"player {i + 1} at ({mo.x >> 16}, {mo.y >> 16})");
            Assert.All(world.players[i].cards, Assert.True);
            for (int k = 0; k < i; k++)
                Assert.False(mo.x == world.players[k].mo!.x && mo.y == world.players[k].mo!.y, "two players on one start");
            // the start taken is marked with the player's number (vanilla's deathmatchstarts[i].type)
            Assert.Contains(world.deathmatchstarts, s => s.Type == i + 1 && s.X == mo.x >> 16 && s.Y == mo.y >> 16);
        }
        Assert.True(world.random.prndindex > 0, "G_DeathMatchSpawnPlayer draws P_Random");
        // the keycard (MF_NOTDMATCH) is left out, the netgame's rocket launcher only in a netgame
        Assert.DoesNotContain(world.Mobjs(), m => m.type == mobjtype_t.MT_MISC4);
        Assert.Equal(players > 1, world.Mobjs().Any(m => m.type == mobjtype_t.MT_MISC27));
    }

    [Fact]
    public void FewerThanFourDeathmatchStartsIsVanillasError()
    {
        var world = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium, deathmatch: 1), Tweaks.Vanilla);
        var e = Assert.Throws<WadFormatException>(() => world.G_DoLoadLevel(Level.Load(NetSimTests.CoopWad(), "E1M1")));
        Assert.Contains("Only 0 deathmatch spots, 4 required", e.Message);
    }

    [Fact]
    public void ANetgamesDeadPlayerIsRebornAtADeathmatchStartAndItsBodyStays()
    {
        World world = NewWorld(1, players: 2);
        mobj_t body = world.players[1].mo!;
        world.P_DamageMobj(body, null, world.players[0].mo, 1000);
        Assert.Equal(1, world.players[0].frags[1]);
        var cmds = new ticcmd_t[World.MAXPLAYERS];
        for (int i = 0; i < 10; i++)
            world.G_Ticker(cmds);
        cmds[1].buttons = buttoncode_t.BT_USE;
        world.G_Ticker(cmds); // P_DeathThink: PST_REBORN
        Assert.Equal(playerstate_t.PST_REBORN, world.players[1].playerstate);
        Assert.Same(body, world.players[1].mo); // (vanilla's reborn runs at the next G_Ticker's start)
        cmds[1].buttons = 0;
        world.G_Ticker(cmds);
        mobj_t mo = world.players[1].mo!;
        Assert.NotSame(body, mo);
        Assert.True(AtAStart(mo));
        Assert.Equal(100, world.players[1].health);
        Assert.Null(body.player); // the corpse, disassociated, stays in the body queue
        Assert.Contains(body, world.Mobjs());
        Assert.Equal((1, body), (world.bodyqueslot, world.bodyque[0]));
        Assert.Equal(gameaction_t.ga_nothing, world.gameaction); // no reload in a netgame
        Assert.Equal(1, world.players[0].frags[1]); // frags kept
        Assert.Contains(world.Mobjs(), m => m.type == mobjtype_t.MT_TFOG); // G_CheckSpot's fog
    }

    [Fact]
    public void ASinglePlayersDeathmatchRebornReloadsTheLevelAtADeathmatchStart()
    {
        World world = NewWorld(1);
        world.P_DamageMobj(world.players[0].mo!, null, null, 1000);
        world.G_Ticker(new ticcmd_t { buttons = buttoncode_t.BT_USE });
        Assert.Equal(gameaction_t.ga_loadlevel, world.gameaction); // vanilla's -deathmatch alone: not a netgame
        world.G_DoLoadLevel(Level.Load(DmWad(), "E1M1"));
        Assert.True(AtAStart(world.players[0].mo!));
        Assert.Equal(100, world.players[0].health);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(0, false)]
    public void TheTimerEndsADeathmatchLevel(int deathmatch, bool ends)
    {
        // -timer 1: 60 * 35 tics, in deathmatch only (P_SpawnSpecials)
        World world = NewWorld(deathmatch, timelimit: 1);
        Assert.Equal(ends, world.levelTimer);
        var cmd = default(ticcmd_t);
        for (int tic = 1; tic < 2100; tic++)
        {
            world.G_Ticker(cmd);
            Assert.Equal(gameaction_t.ga_nothing, world.gameaction);
        }
        world.G_Ticker(cmd);
        Assert.Equal(ends ? gameaction_t.ga_completed : gameaction_t.ga_nothing, world.gameaction);
        Assert.False(world.secretexit);
    }

    [Fact]
    public void AltdeathRespawnsAnItemThirtySecondsAfterItIsTaken()
    {
        World world = NewWorld(2);
        mobj_t clip = world.Mobjs().Single(m => m.type == mobjtype_t.MT_CLIP);
        RouteStart.Place(world, (clip.x >> 16) - 32, clip.y >> 16, 0);
        var cmd = default(ticcmd_t);
        for (int i = 0; i < 35 && clip.function != think_t.REMOVED; i++)
            world.G_Ticker(new ticcmd_t { forwardmove = 25 });
        Assert.Equal(think_t.REMOVED, clip.function);
        Assert.Equal(1, world.iquehead);
        RouteStart.Place(world, 128, 256, 0);
        int taken = world.leveltime - 1;
        while (world.leveltime - taken < 30 * 35)
        {
            world.G_Ticker(cmd);
            Assert.DoesNotContain(world.Mobjs(), m => m.type == mobjtype_t.MT_CLIP);
        }
        world.G_Ticker(cmd);
        mobj_t back = world.Mobjs().Single(m => m.type == mobjtype_t.MT_CLIP);
        Assert.Equal((clip.x, clip.y, clip.spawnpoint), (back.x, back.y, back.spawnpoint));
        Assert.Contains(world.Mobjs(), m => m.type == mobjtype_t.MT_IFOG && m.x == clip.x && m.y == clip.y);
        Assert.Equal(world.iquehead, world.iquetail);
    }

    [Fact]
    public void ADeathmatchStaysInSyncOnEveryMachine()
    {
        // testmap-dm's commands (frags and reborns at deathmatch starts) on worlds of either console player:
        // G_CheckSpot's sound reads consoleplayer's view (a sound only)
        var route = VanillaRoute.Load("testmap-dm");
        var sim = new NetSim(DmWad(), "E1M1", route.File.Settings(GameMode.shareware, skill_t.sk_medium), Tweaks.Vanilla, NetSim.EachPlayer(2));
        foreach (ticcmd_t[] cmds in route.NetCmds)
            sim.Tic(cmds);
        sim.AssertSameState();
        Assert.Equal((1, 1), (sim.World.players[0].frags[1], sim.World.players[1].frags[0]));
        Assert.Equal(2, sim.World.bodyqueslot);
    }
}
