using System;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T11.1: worlds of several players, and worlds of the same game that differ
/// only in <see cref="World.consoleplayer"/> (as on the machines of a
/// netgame) staying in sync over scripted co-op play (<see cref="NetSim"/>).
/// </summary>
public sealed class NetSimTests
{
    // ---- the co-op test map ----

    // West to east (512 deep, ceilings 128): a room B (512 wide), a closed door D (16; D | A is a
    // DR door, special 1, used from A, the line's front) and the start room A (256), with the four co-op starts at x = 700 facing west, 128 units apart. In B, on each
    // player's line, a health bonus at x = 400 (MF_COUNTITEM) and a monster at x = 100 facing
    // west, away: zombiemen on players 1 and 3's lines, imps on 2 and 4's; on player 4's line
    // a shotgun at x = 300 only in a netgame (options bit 16).
    private static readonly int[] _lineY = [64, 192, 320, 448];

    // (T11.2: the map is RouteTestMaps' "coop", which the testmap-coop route plays against vanilla.)
    private static TestMap CoopMap() => RouteTestMaps.Coop();

    /// <summary>The co-op test map (as <c>E1M1</c>), with the four players' starts.</summary>
    internal static WadArchive CoopWad() => new([WadFile.FromBytes(CoopMap().Build(), "coop.wad")]);

    private static SpawnSettings Coop(int players, skill_t skill = skill_t.sk_medium) =>
        new(GameMode.shareware, skill, netgame: true, playersingame: NetSim.FirstPlayers(players));

    private static void GodMode(World world)
    {
        for (int i = 0; i < World.MAXPLAYERS; i++)
        {
            if (world.playeringame[i])
                world.players[i].cheats |= player_t.CF_GODMODE;
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void PlayersInGameComeFromTheSettings(int players)
    {
        var world = new World(Coop(players), Tweaks.Vanilla);
        Assert.Equal(Enumerable.Range(0, 4).Select(i => i < players), world.playeringame);
        world.G_DoLoadLevel(Level.Load(CoopWad(), "E1M1"));
        for (int i = 0; i < World.MAXPLAYERS; i++)
        {
            mobj_t? mo = world.players[i].mo;
            if (i >= players)
            {
                Assert.Null(mo);
                continue;
            }
            Assert.NotNull(mo);
            Assert.Equal((700 << Fixed.FRACBITS, _lineY[i] << Fixed.FRACBITS), (mo!.x, mo.y));
            Assert.Equal((mobjflag_t)(i << Info.MF_TRANSSHIFT), mo.flags & mobjflag_t.MF_TRANSLATION); // vanilla's colours
            Assert.Equal(playerstate_t.PST_LIVE, world.players[i].playerstate);
        }
        // the netgame's own thing (options bit 16)
        bool shotgun = false;
        foreach (mobj_t m in world.Mobjs())
            shotgun |= m.type == mobjtype_t.MT_SHOTGUN;
        Assert.True(shotgun);
    }

    [Fact]
    public void PlayersMaskIsChecked()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new World(Coop(1) with { playersingame = 0 }, Tweaks.Vanilla));
        Assert.Throws<ArgumentOutOfRangeException>(() => new World(Coop(1) with { playersingame = 16 }, Tweaks.Vanilla));
        var world = new World(Coop(1) with { playersingame = 0b1010 }, Tweaks.Vanilla);
        Assert.Equal([false, true, false, true], world.playeringame);
        Assert.Throws<ArgumentOutOfRangeException>(() => world.consoleplayer = 4);
    }

    [Fact]
    public void ConsolePlayerIsNotState()
    {
        // A new game: the reborn players' viewz is 0 (G_PlayerReborn), P_CalcHeight's after a tic.
        var net = new NetSim(CoopWad(), "E1M1", Coop(4), Tweaks.Vanilla, [0, 1, 2, 3]);
        Assert.All(net.World.players, p => Assert.Equal(0, p.viewz));
        net.AssertSameState();
        net.Run(1, (_, _) => default);
        Assert.All(net.World.players, p => Assert.Equal(p.mo!.z + p.viewheight, p.viewz));
        net.AssertSameState();

        // The next level, the players alive: P_SetupLevel's viewz = 1 is every player's, not just consoleplayer's.
        foreach (World world in net.Worlds)
            world.G_DoLoadLevel(Level.Load(CoopWad(), "E1M1"));
        Assert.All(net.World.players, p => Assert.Equal(1, p.viewz));
        net.AssertInSync();
        net.AssertSameState();

        // Single player: player 1's alone, as vanilla's.
        var single = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium), Tweaks.Vanilla);
        single.G_DoLoadLevel(Level.Load(CoopWad(), "E1M1"));
        single.G_Ticker(new ticcmd_t());
        single.G_DoLoadLevel(Level.Load(CoopWad(), "E1M1"));
        Assert.Equal([1, 0, 0, 0], single.players.Select(p => p.viewz));
    }

    /// <summary>
    /// A player's command on the co-op map: west to x = 250 (through the door
    /// player 1 opens at tic 40), then standing, facing west and firing on
    /// and off; player 2 asks for the shotgun at tic 200 (it has none).
    /// </summary>
    private static ticcmd_t CoopInput(World world, int tic, int player)
    {
        var cmd = new ticcmd_t();
        bool walk = world.players[player].mo!.x > 250 << Fixed.FRACBITS;
        if (world.tweaks.AbsoluteMovement)
        {
            cmd.sidemove = (sbyte)(walk ? -25 : 0); // west
            cmd.angleturn = Ticcmds.AbsoluteAngle(Tables.ANG180);
        }
        else
            cmd.forwardmove = (sbyte)(walk ? 25 : 0); // facing west from the start
        if (tic == 40 && player == 0)
            cmd.buttons |= buttoncode_t.BT_USE;
        if (tic >= 100 && (tic + 7 * player) % 30 < 20)
            cmd.buttons |= buttoncode_t.BT_ATTACK;
        if (tic == 200 && player == 1)
            cmd.buttons = buttoncode_t.BT_CHANGE | (int)weapontype_t.wp_shotgun << buttoncode_t.BT_WEAPONSHIFT;
        return cmd;
    }

    public static TheoryData<int, bool> CoopCases => new() { { 2, false }, { 4, false }, { 2, true }, { 4, true } };

    [Theory]
    [MemberData(nameof(CoopCases))]
    public void CoopOnTheTestMapStaysInSync(int players, bool topdown)
    {
        var net = new NetSim(CoopWad(), "E1M1", Coop(players), topdown ? Tweaks.TopDown : Tweaks.Vanilla, NetSim.EachPlayer(players), GodMode);
        World world = net.World;
        net.Run(120, (tic, player) => CoopInput(world, tic, player));
        Assert.True(world.sectors[1].ceilingheight > 64 << Fixed.FRACBITS, "the door opened");
        net.Run(580, (tic, player) => CoopInput(world, tic, player));
        net.AssertSameState();
        for (int i = 0; i < players; i++)
        {
            player_t p = world.players[i];
            Assert.True(p.mo!.x < 400 << Fixed.FRACBITS, $"player {i + 1} went through the door");
            Assert.Equal(1, p.itemcount); // its health bonus
        }
        Assert.True(world.players.Take(players).Sum(p => p.killcount) >= 1, "the players killed a monster");
        Assert.Equal(players == 4, world.players[3].weaponowned[(int)weapontype_t.wp_shotgun]); // the netgame's shotgun
    }

    [Fact]
    public void ACoopGameGoesOnAfterALoad()
    {
        // Saves keep the players in game (SaveGameFile.VERSION 5), and a loaded world on another
        // machine (another consoleplayer) goes on in sync: the state stays snapshot-able.
        var net = new NetSim(CoopWad(), "E1M1", Coop(4) with { playersingame = 0b1011 }, Tweaks.Vanilla, [0], GodMode);
        World world = net.World;
        net.Run(150, (tic, player) => CoopInput(world, tic, player));
        World loaded = SaveGameTests.Load(SaveGameTests.Save(world), CoopWad());
        loaded.consoleplayer = 3;
        Assert.Equal(world.settings.playersingame, loaded.settings.playersingame);
        Assert.Equal([true, true, false, true], loaded.playeringame);
        Assert.Equal(world.Checksum(), loaded.Checksum());
        var netcmds = new ticcmd_t[World.MAXPLAYERS];
        for (int tic = 150; tic < 450; tic++)
        {
            for (int i = 0; i < World.MAXPLAYERS; i++)
                netcmds[i] = world.playeringame[i] ? CoopInput(world, tic, i) : default;
            world.G_Ticker(netcmds);
            loaded.G_Ticker(netcmds);
            Assert.Equal(world.Checksum(), loaded.Checksum());
        }
    }

    // ---- DOOM1 E1M1 ----

    /// <summary>
    /// Player 1 plays <c>e1m1-exit</c> (doors, the lift, the nukage), the
    /// others wander from their starts by a fixed pseudo-random script,
    /// firing and using, all in god mode, with monsters, for the route's length.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void CoopOnDoom1E1M1StaysInSync(int players)
    {
        var wad = WadArchive.Open(TestWads.RequireDoom1());
        VanillaRoute route = VanillaRoute.Load("e1m1-exit");
        var net = new NetSim(wad, "E1M1", Coop(players, skill_t.sk_hard), Tweaks.Vanilla, NetSim.EachPlayer(players), GodMode);
        World world = net.World;
        int[] ceilings = [.. world.sectors.Select(s => s.ceilingheight)];
        var opened = new System.Collections.Generic.SortedSet<int>();
        net.Run(route.Cmds.Count, (tic, player) =>
        {
            if (player == 0)
            {
                foreach (sector_t s in world.sectors)
                {
                    if (s.ceilingheight > ceilings[s.Index])
                        opened.Add(s.Index);
                }
                return route.Cmds[tic];
            }
            uint r = Hash((uint)(tic / 20), (uint)player); // a new intent every 20 tics
            var cmd = new ticcmd_t
            {
                forwardmove = (sbyte)((r & 3) == 0 ? -25 : 25),
                angleturn = (short)((r >> 2 & 7) switch { 0 => 640, 1 => -640, _ => 0 }),
                buttons = (byte)(((r >> 5 & 1) != 0 && tic % 8 < 4 ? buttoncode_t.BT_ATTACK : 0) | (tic % 20 == 10 ? buttoncode_t.BT_USE : 0)),
            };
            return cmd;
        });
        net.AssertSameState();
        Assert.Contains(4, opened); // the first door
        Assert.True(world.players.Take(players).Sum(p => p.itemcount) >= players, "items picked up");
        Assert.True(world.players.Take(players).Sum(p => p.killcount) >= 1, "monsters killed");
        Assert.All(world.players.Take(players), p => Assert.Equal(playerstate_t.PST_LIVE, p.playerstate));
    }

    private static uint Hash(uint a, uint b)
    {
        uint h = a * 0x9E3779B1u ^ b * 0x85EBCA77u;
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        return h;
    }
}
