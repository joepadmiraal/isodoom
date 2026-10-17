using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IsoDoom.Game;
using IsoDoom.Net;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Net;

/// <summary>
/// T11.11: drop-outs, desyncs and lag over the in-memory transport. A client
/// that vanishes (killed: no quit, no disconnect) or goes silent is dropped
/// after the timeout and leaves the game at a tic every world applies
/// (<see cref="World.PlayerQuitGame"/>); a host that vanishes times its
/// clients out; loss bursts and a five-second stall are caught up without a
/// desync or a drop; after a desync nobody runs another tic; the pongs carry
/// the players' pings and whom the bundles wait for.
/// </summary>
public class DropoutTests
{
    private static readonly NetGameSettings _coop = new(
        new SpawnSettings(GameMode.shareware, skill_t.sk_medium, netgame: true), Tweaks.TopDown, Episode: 1, Map: 1);

    private static WadArchive Synthetic() => new([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);

    private static List<PlayerSetup> Setups(int players, int latencyMs, int jitterMs = 5, double loss = 0)
    {
        var setups = new List<PlayerSetup> { new() };
        for (int i = 1; i < players; i++)
        {
            setups.Add(new PlayerSetup
            {
                Link = new MemoryLinkOptions { LatencyUs = latencyMs * 1000L, JitterUs = jitterMs * 1000L, UnreliableLoss = loss, ReliableLoss = loss / 2 },
                FrameUs = 16_667 + 1_500 * i,
                PhaseUs = 2_000 * i,
            });
        }
        return setups;
    }

    private static LockstepHarness Worlds(int players, int latencyMs, int seed, double loss = 0)
    {
        WadArchive wad = Synthetic();
        return new LockstepHarness(_coop, Setups(players, latencyMs, loss: loss), (slot, tic) => LockstepHarness.Wander(slot, tic, fire: true),
            start => LockstepHarness.MakeWorld(wad, start), seed);
    }

    [Fact]
    public void A_client_that_vanishes_is_timed_out_and_leaves_every_world_at_one_tic()
    {
        LockstepHarness h = Worlds(3, 30, seed: 21);
        h.JoinAndReady();
        h.Server.Start(4);
        h.RunUntil(() => h.Players.TrueForAll(p => p.TicsRun >= 400), 30_000_000, "400 tics");
        HarnessPlayer gone = h.Players[2];
        int slot = gone.Client.Slot;
        int lastSent = gone.Client.MakeTic - 1;
        long vanished = h.Network.NowUs;
        gone.Down = true; // killed: no quit, no disconnect

        // the others hold, then go on once the server has dropped it
        h.RunUntil(() => h.Players[0].Client.QuitTics[slot] != int.MaxValue, NetProtocol.DefaultTimeoutUs + 2_000_000, "the drop");
        long waited = h.Network.NowUs - vanished;
        Assert.InRange(waited, NetProtocol.DefaultTimeoutUs, NetProtocol.DefaultTimeoutUs + 1_000_000);
        Assert.Contains(h.ServerLog, line => line.Contains($"(slot {slot}) timed out", StringComparison.Ordinal));
        int quitTic = h.Players[0].Client.QuitTics[slot];
        Assert.Equal(lastSent + 1, quitTic); // the first tic it sent no command for
        int ranBefore = h.Players[0].TicsRun;
        h.RunUntil(() => h.Players[0].TicsRun >= ranBefore + 700 && h.Players[1].TicsRun >= ranBefore + 700, 60_000_000, "700 tics after the drop");

        Assert.Null(h.Server.Desync);
        foreach (HarnessPlayer p in h.Players.Take(2))
        {
            Assert.Equal(quitTic, p.Client.QuitTics[slot]);
            Assert.Equal([(slot, quitTic)], p.Quits); // the bundle without it: every world, the same tic
            World w = p.World!;
            Assert.False(w.playeringame[slot]);
            Assert.Equal(2, Enumerable.Range(0, 3).Count(i => w.playeringame[i]));
            Assert.NotNull(w.players[slot].mo); // vanilla's: the mobj stays
        }
        Assert.Equal(h.Players[0].World!.Checksum(), h.Players[1].World!.Checksum());
        Assert.True(h.Server.WindowsCompared >= (ranBefore + 700) / 35 - 2, $"{h.Server.WindowsCompared} windows compared");
    }

    [Fact]
    public void A_client_that_sends_no_commands_is_dropped_though_it_pings()
    {
        var h = new LockstepHarness(_coop, Setups(2, 20), (slot, tic) => LockstepHarness.Wander(slot, tic, fire: false), seed: 22);
        h.JoinAndReady();
        h.Server.TimeoutUs = 3_000_000;
        h.Server.Start(3);
        h.RunUntil(() => h.Players.TrueForAll(p => p.TicsRun >= 200), 20_000_000, "200 tics");
        h.Players[1].Mute = true; // its game hangs, its network goes on (two players: it is in slot 1)
        h.RunUntil(() => h.Players[0].Client.QuitTics[1] != int.MaxValue, 5_000_000, "the drop");
        Assert.Contains(h.ServerLog, line => line.Contains("(slot 1) timed out: no command for", StringComparison.Ordinal));
        int ran = h.Players[0].TicsRun;
        h.RunUntil(() => h.Players[0].TicsRun >= ran + 100, 10_000_000, "the host alone");
        // the dropped client hears of it: the server disconnected it
        h.RunUntil(() => h.Players[1].Client.State == NetClientState.Disconnected, 2_000_000, "the client disconnected");
    }

    [Fact]
    public void A_host_that_vanishes_times_its_clients_out()
    {
        var h = new LockstepHarness(_coop, Setups(3, 30), (slot, tic) => LockstepHarness.Wander(slot, tic, fire: false), seed: 23);
        h.JoinAndReady();
        h.Server.Start(3);
        h.RunUntil(() => h.Players.TrueForAll(p => p.TicsRun >= 100), 10_000_000, "100 tics");
        long vanished = h.Network.NowUs;
        h.HostDown = true; // no dispose: nothing tells the clients
        h.RunUntil(() => h.Players.Skip(1).All(p => p.Client.State == NetClientState.Disconnected), NetProtocol.DefaultTimeoutUs + 1_000_000, "the clients timed out");
        Assert.True(h.Network.NowUs - vanished >= NetProtocol.DefaultTimeoutUs);
        Assert.All(h.Players.Skip(1), p => Assert.StartsWith("The host timed out", p.Client.Error, StringComparison.Ordinal));
    }

    [Fact]
    public void Loss_bursts_keep_the_worlds_in_sync()
    {
        LockstepHarness h = Worlds(3, 40, seed: 24, loss: 0.02);
        h.JoinAndReady();
        h.Server.Start(5);
        var random = new Random(24);
        long nextBurst = h.Network.NowUs + 1_000_000;
        int bursts = 0;
        h.RunUntil(() =>
        {
            if (h.Network.NowUs >= nextBurst)
            {
                // a burst: one player's link (or the host's) down for 100-700 ms
                MemoryTransport end = random.Next(4) is int k && k < 3 ? h.Players[k].Transport : h.ServerTransport;
                end.Block(100_000 + random.Next(600_000));
                nextBurst = h.Network.NowUs + 1_000_000 + random.Next(1_000_000);
                bursts++;
            }
            return h.Players.TrueForAll(p => p.TicsRun >= 2100);
        }, 200_000_000, "2100 tics");
        Assert.True(bursts >= 20, $"{bursts} bursts");
        Assert.Null(h.Server.Desync);
        Assert.All(h.Players, p => Assert.All(p.Client.QuitTics, t => Assert.Equal(int.MaxValue, t)));
        Assert.True(h.Server.WindowsCompared >= 2100 / 35 - 2);
        Assert.Equal(h.Players[0].World!.Checksum(), h.Players[2].World!.Checksum());
    }

    [Fact]
    public void A_five_second_stall_is_caught_up_without_a_desync()
    {
        LockstepHarness h = Worlds(3, 30, seed: 25);
        h.JoinAndReady();
        h.Server.Start(4);
        h.RunUntil(() => h.Players.TrueForAll(p => p.TicsRun >= 300), 30_000_000, "300 tics");
        int stalled = h.Players[1].Client.Slot;
        h.Players[1].Transport.Block(5_000_000); // its link is down for 5 s
        long maxLate = 0;
        byte waitingWhenLate = 0;
        int maxStalls = 0;
        h.RunUntil(() =>
        {
            NetClient host = h.Players[0].Client;
            maxLate = Math.Max(maxLate, host.LateUs);
            if (host.LateUs is > 2_000_000 and < 3_000_000)
                waitingWhenLate = host.WaitingFor; // mid-stall (at its end the others' commands may lag a moment too)
            maxStalls = Math.Max(maxStalls, host.StallsLastSecond);
            return h.Players.TrueForAll(p => p.TicsRun >= 300 + 5 * 35 + 700);
        }, 60_000_000, "the catch-up");
        Assert.InRange(maxLate, 4_000_000, 6_000_000); // the host's world held for about the stall
        Assert.Equal(1 << stalled, waitingWhenLate); // ... waiting for it, as the pongs said
        Assert.True(maxStalls >= 1);
        Assert.Null(h.Server.Desync);
        Assert.All(h.Players, p => Assert.All(p.Client.QuitTics, t => Assert.Equal(int.MaxValue, t)));
        Assert.All(h.Players, p => Assert.Equal(-1, p.Client.FirstBadTic));
        // caught up: the clocks run together again (the stalled player ran its backlog at once)
        Assert.All(h.Players, p => Assert.InRange(p.Client.LateUs, 0, 200_000));
        Assert.True(h.Players[1].MaxTicsPerFrame > 35, $"player 2 ran at most {h.Players[1].MaxTicsPerFrame} tics a frame");
    }

    [Fact]
    public void After_a_desync_nobody_runs_another_tic()
    {
        LockstepHarness h = Worlds(3, 30, seed: 26);
        h.AllowDesync = true;
        h.BumpRandom = (1, 140);
        h.JoinAndReady();
        h.Server.Start(4);
        h.RunUntil(() => h.Players.TrueForAll(p => p.Client.Desync is not null), 30_000_000, "the desync");
        Assert.Equal(4, h.Server.Desync!.Value.Window);
        int[] ran = h.Players.Select(p => p.TicsRun).ToArray();
        int complete = h.Server.CompleteTic;
        long until = h.Network.NowUs + 3_000_000;
        h.RunUntil(() => h.Network.NowUs >= until, 4_000_000, "three seconds more");
        Assert.Equal(ran, h.Players.Select(p => p.TicsRun).ToArray());
        Assert.Equal(complete, h.Server.CompleteTic);
        Assert.All(h.Players, p =>
        {
            Assert.True(p.Client.Stopped);
            Assert.Equal(0, p.Client.CommandsDue);
            Assert.Equal(0, p.Client.TicsReady);
            Assert.Equal(NetClientState.InGame, p.Client.State); // stopped, not gone (no timeouts after it)
        });
    }

    [Fact]
    public void The_pongs_carry_the_pings_and_whom_the_bundles_wait_for()
    {
        var h = new LockstepHarness(_coop, Setups(3, 40, jitterMs: 0), (slot, tic) => default, seed: 27);
        h.JoinAndReady();
        h.Server.Start(4);
        h.RunUntil(() => h.Players.TrueForAll(p => p.TicsRun >= 100), 10_000_000, "100 tics");
        foreach (HarnessPlayer p in h.Players)
        {
            Assert.InRange(p.Client.PlayerRttMs[0], 0, 20); // the host's own client: no latency
            Assert.InRange(p.Client.PlayerRttMs[1], 75, 140); // 40 ms each way, and the frames
            Assert.InRange(p.Client.PlayerRttMs[2], 75, 140);
            Assert.Equal(NetProtocol.NoRtt, p.Client.PlayerRttMs[3]);
            Assert.Equal(0, p.Client.StallsLastSecond);
        }
        h.Players[2].Mute = true;
        int muted = 1 << h.Players[2].Client.Slot;
        h.RunUntil(() => h.Players[0].Client.LateUs > 1_500_000, 5_000_000, "the host late");
        Assert.Equal(muted, h.Players[0].Client.WaitingFor);
        Assert.Equal(muted, h.Players[1].Client.WaitingFor);
        Assert.Equal(muted, h.Server.WaitingMask);
    }

    [Fact]
    public void Quitting_is_the_same_tic_in_every_world_and_the_console_players_message()
    {
        // NetSim: two machines of a 3-player co-op game; player 3 leaves before tic 50
        WadArchive wad = Synthetic();
        var settings = new SpawnSettings(GameMode.shareware, skill_t.sk_medium, netgame: true, playersingame: NetSim.FirstPlayers(3));
        var sim = new NetSim(wad, "E1M1", settings, Tweaks.TopDown, [0, 1]);
        sim.Run(50, (tic, player) => LockstepHarness.Wander(player, tic, fire: false));
        mobj_t mo = sim.World.players[2].mo!;
        foreach (World w in sim.Worlds)
            w.PlayerQuitGame(2);
        var cmds = new ticcmd_t[World.MAXPLAYERS];
        cmds[0] = LockstepHarness.Wander(0, 50, false);
        cmds[1] = LockstepHarness.Wander(1, 50, false);
        sim.Tic(cmds);
        foreach (World w in sim.Worlds)
        {
            Assert.False(w.playeringame[2]);
            sim_event_t message = Assert.Single(w.events, e => e.type == simevent_t.se_message);
            Assert.Equal((w.consoleplayer, "Player 3 left the game"), (message.player, message.message));
            Assert.Null(w.players[w.consoleplayer].message); // taken in the tic: the state stays the same on every machine
        }
        Assert.Same(mo, sim.World.players[2].mo);
        Assert.Contains(mo, sim.World.Mobjs()); // the mobj stays in the world
        sim.AssertSameState();
        sim.Run(200, (tic, player) => LockstepHarness.Wander(player, tic, fire: true));
        sim.AssertSameState();
    }

    [Fact]
    public void The_desync_dumps_hold_the_last_tics_and_the_last_agreed_save()
    {
        var start = new NetStart(1, 0b11, 0, 3, _coop with { Spawn = _coop.Spawn with { playersingame = 0b11 } });
        var log = new NetDesyncLog(start);
        var cmds = new ticcmd_t[NetProtocol.MaxPlayers];
        var consistancy = new short[NetProtocol.MaxPlayers];
        log.Window(NetDesyncLog.StartWindow, [1, 2, 3]);
        for (int tic = 0; tic < 20 * 35; tic++)
        {
            cmds[0] = new ticcmd_t { forwardmove = 25, angleturn = (short)tic };
            cmds[1] = new ticcmd_t { sidemove = -24, buttons = 1 };
            consistancy[0] = (short)tic;
            consistancy[1] = (short)-tic;
            log.Tic(tic, 0b11, cmds, (ulong)tic * 0x1000_0001UL, consistancy);
            if ((tic + 1) % 35 == 0)
                log.Window(tic / 35, [(byte)(tic / 35)]);
        }
        Assert.Equal(20 * 35 - 1, log.LastTic);
        (int agreed, byte[] save) = log.AgreedSave(new NetDesync(18, 18 * 35, 18 * 35 + 34))!.Value;
        Assert.Equal((17, 17), (agreed, save.Single()));
        Assert.Null(log.AgreedSave(new NetDesync(3, 105, 139))); // gone: none before it kept
        Assert.Equal(13, log.AgreedSave(new NetDesync(14, 490, 524))!.Value.Window);

        // a world's state kept: a save file made when asked, whose world loads
        World world = NetSim.MakeWorld(Synthetic(), "E1M1", start.Settings.Spawn, start.Settings.Tweaks, 1);
        log.Window(20, world);
        world.G_Ticker(new ticcmd_t[World.MAXPLAYERS]); // later changes are not in it
        (int kept, byte[] file) = log.AgreedSave(new NetDesync(21, 735, 769))!.Value;
        SaveGameFile read = SaveGameFile.Read(file);
        Assert.Equal((20, "E1M1", 0, "NET WINDOW 20"), (kept, read.Map, read.LevelTime, read.Description));

        string root = Path.Combine(Path.GetTempPath(), $"isodoom-netdesync-test-{Environment.ProcessId}-{Guid.NewGuid():N}");
        try
        {
            string dir = log.Write(root, new NetDesync(18, 630, 664), 1, "test", 650, new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Local),
                names: ["JOEP", "", "", ""]);
            Assert.Equal(Path.Combine(root, "20261017-120000-player2"), dir);
            Assert.Equal([17], File.ReadAllBytes(Path.Combine(dir, "agreed-window17.dsg")));
            string[] checksums = File.ReadAllLines(Path.Combine(dir, "checksums.txt"));
            Assert.Equal(1 + NetDesyncLog.KeptTics, checksums.Length);
            Assert.Equal($"{699} {699UL * 0x1000_0001UL:x16} 699 -699 0 0", checksums[^1]);
            string[] commands = File.ReadAllLines(Path.Combine(dir, "commands.txt"));
            Assert.Equal("699 3 | p1 25 0 699 0 0 0 | p2 0 -24 0 1 0 0", commands[^1]);
            string summary = File.ReadAllText(Path.Combine(dir, "summary.txt"));
            Assert.Contains("desync: window 18, tics 630-664", summary, StringComparison.Ordinal);
            Assert.Contains("first consistency failure seen here: 650", summary, StringComparison.Ordinal);
            Assert.Contains("save: agreed-window17.dsg", summary, StringComparison.Ordinal);
            Assert.Contains("names: 1 JOEP, 2 PLAYER 2", summary, StringComparison.Ordinal); // T11.9c: the lobby's names, or PLAYER N
            // a second dump in the same second gets a directory of its own
            Assert.Equal(dir + "-2", log.Write(root, new NetDesync(18, 630, 664), 1, "test", -1, new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Local)));

            // window 0: the start's save
            var early = new NetDesyncLog(start);
            early.Window(NetDesyncLog.StartWindow, [9]);
            string first = early.Write(root, new NetDesync(0, 0, 34), 0, "test", -1, new DateTime(2026, 10, 17, 12, 0, 1, DateTimeKind.Local));
            Assert.Equal([9], File.ReadAllBytes(Path.Combine(first, "agreed-start.dsg")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_timeout_argument_and_the_overlay_texts()
    {
        Assert.Equal(10, NetOptions.Parse(["-host"])!.TimeoutSeconds);
        NetOptions o = NetOptions.Parse(["-connect", "10.0.0.2", "-nettimeout", "4"])!;
        Assert.Equal((4, 4_000_000L), (o.TimeoutSeconds, o.TimeoutUs));
        Assert.Throws<ArgumentException>(() => NetOptions.Parse(["-host", "-nettimeout", "0"]));
        Assert.Throws<ArgumentException>(() => NetOptions.Parse(["-host", "-nettimeout", "601"]));

        Assert.Equal("Waiting for player 2...", NetSession.WaitingText(0b0010, 0));
        Assert.Equal("Waiting for players 1, 3 and 4...", NetSession.WaitingText(0b1101, 1));
        Assert.Equal("Waiting for the host...", NetSession.WaitingText(0, 1));
        Assert.Equal("Waiting for the host...", NetSession.WaitingText(0b0010, 1)); // only this machine: its own link
    }

    [Fact]
    public void The_sessions_time_out_with_the_arguments_and_show_the_net_line()
    {
        var network = new MemoryNetwork();
        NetIdentity identity = LockstepHarness.Identity;
        NetOptions hostOptions = NetOptions.Parse(["-host", "-players", "2", "-nettimeout", "2"])!;
        var host = new NetSession(hostOptions, identity, _coop, use => network.CreateEndpoint(use == NetTransportUse.Server ? "127.0.0.1" : "host"));
        var join = new NetSession(NetOptions.Parse(["-connect", "127.0.0.1", "-nettimeout", "2"])!, identity, _coop, _ => network.CreateEndpoint("join"));
        Assert.Equal(2_000_000, host.Server!.TimeoutUs);
        Assert.Equal(2_000_000, join.Client.TimeoutUs);
        var cmds = new ticcmd_t[NetProtocol.MaxPlayers];
        var consistancy = new short[NetProtocol.MaxPlayers];
        void Frame(NetSession s)
        {
            s.Update(network.NowUs);
            NetClient c = s.Client;
            while (c.CommandsDue > 0)
                c.SendCommand(default);
            c.Flush();
            while (c.TryTakeTic(cmds, out int tic, out _))
                c.TicRan(tic, 0, consistancy);
        }
        for (int i = 0; i < 6000 && join.Client.LastRanTic < 100; i++)
        {
            network.Advance(1000);
            if (i % 16 == 0)
            {
                Frame(host);
                Frame(join);
            }
        }
        Assert.True(join.Client.LastRanTic >= 100, $"the join ran to tic {join.Client.LastRanTic}");
        string line = host.OverlayText();
        Assert.StartsWith("net: host, player 1, ping 1: ", line, StringComparison.Ordinal);
        Assert.Contains(" 2: ", line, StringComparison.Ordinal);
        Assert.Contains(", delay ", line, StringComparison.Ordinal);
        Assert.Contains("stalls ", line, StringComparison.Ordinal);

        // the join's machine is killed: the host drops it after 2 s and goes on alone
        for (int i = 0; i < 3000; i++)
        {
            network.Advance(1000);
            if (i % 16 == 0)
                Frame(host);
        }
        Assert.NotEqual(int.MaxValue, host.Client.QuitTics[1]);
        Assert.Contains("2: left at tic", host.OverlayText(), StringComparison.Ordinal);
        host.Dispose();
        join.Dispose();
    }
}
