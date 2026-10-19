using System;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Net;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Net;

/// <summary>
/// T11.5: the lockstep protocol over the in-memory transport. Two to four
/// players play scripted input for 3500 tics under latency, jitter and loss;
/// every world stays checksum-equal, nobody runs a tic before its bundle
/// (<see cref="LockstepHarness"/> checks both every tic), the traffic and the
/// lead stay bounded, and a forced desync is announced at its window.
/// </summary>
public class LockstepTests
{
    private const int Tics = 3500;

    private static readonly NetGameSettings _coop = new(
        new SpawnSettings(GameMode.shareware, skill_t.sk_medium, netgame: true), Tweaks.TopDown, Episode: 1, Map: 1);

    private static WadArchive Synthetic() => new([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);

    /// <summary>Player i's link: the remote players' latencies spread over 0–<paramref name="maxLatencyMs"/> one way.</summary>
    private static List<PlayerSetup> Setups(int players, int maxLatencyMs, int jitterMs, double loss)
    {
        var setups = new List<PlayerSetup> { new() };
        for (int i = 1; i < players; i++)
        {
            setups.Add(new PlayerSetup
            {
                Link = new MemoryLinkOptions
                {
                    LatencyUs = maxLatencyMs * 1000L * i / (players - 1),
                    JitterUs = jitterMs * 1000L,
                    UnreliableLoss = loss,
                    ReliableLoss = loss / 2,
                },
                FrameUs = 16_667 + 2_000 * i,
                PhaseUs = 3_000 * i,
            });
        }
        return setups;
    }

    /// <summary>The input delay for a worst one-way latency: the round trip in tics, plus one, within 1–8.</summary>
    private static int DelayFor(int maxLatencyMs, int jitterMs) => Math.Clamp((2 * (maxLatencyMs + jitterMs) * 35 + 999) / 1000 + 1, 1, 8);

    private static void AssertPlayedWell(LockstepHarness h, int delay, int maxLatencyMs, int jitterMs, double loss, long startUs)
    {
        Assert.Null(h.Server.Desync);
        Assert.True(h.Server.WindowsCompared >= Tics / NetProtocol.WindowTics - 1, $"{h.Server.WindowsCompared} windows compared");
        long seconds = (h.Network.NowUs - startUs) / 1_000_000;
        // At 35 Hz, the game's time plus the round trip and a resend or two.
        Assert.True(seconds <= Tics / 35 + 2, $"{Tics} tics took {seconds} s");
        // The lead (commands sent past the last tic run): the delay, the worst
        // round trip in tics, and slack for jitter, resends and frames.
        int roundTrip = (2 * (maxLatencyMs + jitterMs) * 35 + 999) / 1000;
        int resends = loss > 0 ? 6 : 0;
        // T11.12b: the delay may have moved in the game; the longest it played at
        foreach (NetDelayChange change in h.Server.DelayChanges)
            delay = Math.Max(delay, change.To);
        foreach (HarnessPlayer p in h.Players)
        {
            Assert.Null(p.Client.Desync);
            Assert.Equal(-1, p.Client.FirstBadTic);
            Assert.True(p.MaxLead <= delay + roundTrip + resends + 4,
                $"player {p.Index}: lead {p.MaxLead} > delay {delay} + round trip {roundTrip} + {resends + 4}");
            // Throughput (T11.12c): a Commands message a tic (10 bytes) with
            // every command not acknowledged yet (8 bytes each: the round
            // trip's tics, frames' waits, lost acknowledgements), plus pings.
            int unacked = roundTrip + 4 + resends;
            double up = p.Transport.ReliableBytesSent + p.Transport.UnreliableBytesSent;
            Assert.True(up / seconds < 300 + 35 * (10 + 8 * unacked), $"player {p.Index} sent {up / seconds:F0} B/s");
            Assert.True(p.Transport.PacketsSent / (double)seconds < 2 * 35 + 12, $"player {p.Index} sent {p.Transport.PacketsSent / (double)seconds:F1} packets/s");
        }
        double down = (h.ServerTransport.ReliableBytesSent + h.ServerTransport.UnreliableBytesSent) / (double)seconds / h.Players.Count;
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{Tics} tics in {(h.Network.NowUs - startUs) / 1000} ms, delay {delay}, down {down:F0} B/s a player; " +
            string.Join("; ", h.Players.Select(p => $"player {p.Index}: lead {p.MaxLead}, {p.MaxTicsPerFrame} tics/frame max, up {p.Transport.ReliableBytesSent + p.Transport.UnreliableBytesSent} B, rtt {p.Client.RttUs / 1000} ms, rate {p.Client.ClockRate:F4}, clock error {p.Client.ClockErrorUs / 1000} ms")));
        // a Bundle message a tic to each player (10 bytes) with every tic it has not acknowledged (1 + 8 a player each)
        int roundTrips = (2 * (maxLatencyMs + jitterMs) * 35 + 999) / 1000 + 4 + (loss > 0 ? 6 : 0);
        Assert.True(down < 300 + 35 * (10 + (1 + 8 * h.Players.Count) * roundTrips), $"the server sent {down:F0} B/s a player");
    }

    [Theory]
    [InlineData(2, 0, 0, 0.0)]
    [InlineData(2, 100, 0, 0.0)]
    [InlineData(2, 50, 20, 0.05)]
    [InlineData(3, 100, 30, 0.1)]
    [InlineData(4, 25, 10, 0.02)]
    [InlineData(4, 100, 30, 0.1)]
    public void Protocol_alone_stays_in_step_under_latency_jitter_and_loss(int players, int maxLatencyMs, int jitterMs, double loss)
    {
        var h = new LockstepHarness(_coop, Setups(players, maxLatencyMs, jitterMs, loss), (slot, tic) => LockstepHarness.Wander(slot, tic, fire: true), seed: players * 100 + maxLatencyMs);
        h.JoinAndReady();
        int delay = DelayFor(maxLatencyMs, jitterMs);
        long start = h.Network.NowUs;
        h.Play(delay, Tics, 200_000_000);
        AssertPlayedWell(h, delay, maxLatencyMs, jitterMs, loss, start);
    }

    [Theory]
    [InlineData(2, 100, 20, 0.05)]
    [InlineData(4, 50, 30, 0.1)]
    public void Synthetic_worlds_stay_checksum_equal(int players, int maxLatencyMs, int jitterMs, double loss)
    {
        // The synthetic E1M1 has four starts (T11.5a): every player is in
        // game, walking, using and firing among its imps and barrel; no god
        // mode (T11.5b): players die (shot by each other too) and reborn.
        WadArchive wad = Synthetic();
        var h = new LockstepHarness(_coop, Setups(players, maxLatencyMs, jitterMs, loss), (slot, tic) => LockstepHarness.Wander(slot, tic, fire: true),
            start => LockstepHarness.MakeWorld(wad, start), seed: 7 + players);
        h.JoinAndReady();
        int delay = DelayFor(maxLatencyMs, jitterMs);
        long start = h.Network.NowUs;
        h.Play(delay, Tics, 200_000_000);
        AssertPlayedWell(h, delay, maxLatencyMs, jitterMs, loss, start);
        World first = h.Players[0].World!;
        Assert.All(h.Players, p => Assert.Equal(first.Checksum(), p.World!.Checksum()));
        Assert.All(h.Players, p => Assert.Equal(p.Client.Slot, p.World!.consoleplayer));
        for (int i = 0; i < players; i++)
        {
            Assert.True(first.playeringame[i]);
            Assert.NotNull(first.players[i].mo);
            Assert.Equal(0, first.players[i].cheats);
        }
        Assert.True(first.bodyqueslot > 0, $"nobody died and reborn ({first.bodyqueslot})"); // T11.5b: no god mode
    }

    [Theory]
    [InlineData(2, 100, 20, 0.05)]
    [InlineData(4, 100, 30, 0.1)]
    public void Doom1_coop_worlds_stay_checksum_equal(int players, int maxLatencyMs, int jitterMs, double loss)
    {
        // DOOM1 E1M1 has four starts: every player is in game, with monsters, walking, using and firing, dying and reborn (T11.5b).
        WadArchive wad = new([WadFile.Open(TestWads.RequireDoom1())]);
        var h = new LockstepHarness(_coop, Setups(players, maxLatencyMs, jitterMs, loss), (slot, tic) => LockstepHarness.Wander(slot, tic, fire: true),
            start => LockstepHarness.MakeWorld(wad, start), seed: 11 + players);
        h.JoinAndReady();
        int delay = DelayFor(maxLatencyMs, jitterMs);
        long start = h.Network.NowUs;
        h.Play(delay, Tics, 200_000_000);
        AssertPlayedWell(h, delay, maxLatencyMs, jitterMs, loss, start);
        World first = h.Players[0].World!;
        Assert.All(h.Players, p => Assert.Equal(first.Checksum(), p.World!.Checksum()));
        for (int i = 0; i < players; i++)
        {
            Assert.NotNull(first.players[i].mo);
            Assert.Equal(0, first.players[i].cheats);
        }
        Assert.True(first.bodyqueslot > 0, $"nobody died and reborn ({first.bodyqueslot})"); // T11.5b: no god mode
    }

    [Theory]
    [InlineData(false, 7 * 35 + 12)]
    [InlineData(false, 20 * 35)]
    [InlineData(true, 7 * 35 + 12)]
    [InlineData(true, 30 * 35 + 34)]
    public void A_forced_desync_is_announced_at_its_window(bool worlds, int bumpTic)
    {
        WadArchive wad = Synthetic();
        var h = new LockstepHarness(_coop, Setups(3, 50, 20, 0.05), (slot, tic) => LockstepHarness.Wander(slot, tic, fire: true),
            worlds ? start => LockstepHarness.MakeWorld(wad, start) : null, seed: 5)
        {
            AllowDesync = true,
            BumpRandom = (2, bumpTic),
        };
        h.JoinAndReady();
        h.Server.Start(4);
        h.RunUntil(() => h.Players.TrueForAll(p => p.Client.Desync is not null), 60_000_000, "the desync announced");
        int window = bumpTic / NetProtocol.WindowTics;
        Assert.Equal(new NetDesync(window, window * 35, window * 35 + 34), h.Server.Desync);
        Assert.All(h.Players, p => Assert.Equal(h.Server.Desync, p.Client.Desync));
        Assert.Equal(window, h.Server.WindowsCompared - 1);
    }

    [Fact]
    public void A_moved_player_fails_the_others_consistancy_check()
    {
        // vanilla's per-tic check: player 1's mobj moves a unit in its own
        // world only during tic 5, so its command for tic 5 + ConsistancyLag
        // carries an x the others' worlds do not have.
        WadArchive wad = Synthetic();
        var h = new LockstepHarness(_coop, Setups(3, 20, 5, 0), (slot, tic) => LockstepHarness.Wander(slot, tic, fire: false),
            start => LockstepHarness.MakeWorld(wad, start), seed: 3)
        {
            AllowDesync = true,
            BeforeTic = (p, tic) =>
            {
                // T11.11: and back before tic 6, so window 0's checksums agree (a desync stops
                // every machine: the consistancy check must find it first, in window 1)
                if (p.Index == 0 && tic == 5)
                    p.World!.players[0].mo!.x += 1;
                if (p.Index == 0 && tic == 6)
                    p.World!.players[0].mo!.x -= 1;
            },
        };
        h.JoinAndReady();
        h.Server.Start(3);
        h.RunUntil(() => h.Players.TrueForAll(p => p.Client.Desync is not null), 20_000_000, "the desync");
        Assert.Equal(1, h.Server.Desync?.Window);
        Assert.Equal(-1, h.Players[0].Client.FirstBadTic);
        Assert.Equal(5 + NetProtocol.ConsistancyLag, h.Players[1].Client.FirstBadTic);
        Assert.Equal(5 + NetProtocol.ConsistancyLag, h.Players[2].Client.FirstBadTic);
    }

    [Theory]
    [InlineData(0.98, 1.02)]
    [InlineData(1.02, 0.98)]
    public void Drifting_clocks_are_pulled_to_the_hosts(double skew1, double skew2)
    {
        var setups = Setups(3, 40, 10, 0.02);
        setups[1] = setups[1] with { ClockSkew = skew1, FrameUs = 7_000 };
        setups[2] = setups[2] with { ClockSkew = skew2, FrameUs = 33_333 };
        var h = new LockstepHarness(_coop, setups, (slot, tic) => LockstepHarness.Wander(slot, tic, fire: true), seed: 9);
        h.JoinAndReady();
        int delay = DelayFor(40, 10);
        long start = h.Network.NowUs;
        h.Play(delay, Tics, 200_000_000);
        AssertPlayedWell(h, delay, 40, 10, 0.02, start);
        // Each clock runs at the host's rate: its own seconds are skew times as
        // long (within the jitter's noise on the last measurement).
        Assert.InRange(h.Players[1].Client.ClockRate * skew1, 0.985, 1.015);
        Assert.InRange(h.Players[2].Client.ClockRate * skew2, 0.985, 1.015);
        Assert.All(h.Players, p => Assert.InRange(p.Client.ClockErrorUs, -40_000, 40_000));
    }

    [Fact]
    public void A_player_who_quits_leaves_the_bundles_after_its_last_command()
    {
        var h = new LockstepHarness(_coop, Setups(3, 30, 10, 0.02), (slot, tic) => LockstepHarness.Wander(slot, tic, fire: true), seed: 4);
        h.JoinAndReady();
        h.Server.Start(4);
        h.RunUntil(() => h.Players[1].TicsRun >= 500, 60_000_000, "500 tics");
        int sent = h.Players[1].Client.MakeTic - 1;
        h.Players[1].Client.Quit();
        h.RunUntil(() => h.Players[0].TicsRun >= 1500 && h.Players[2].TicsRun >= 1500, 60_000_000, "1500 tics after the quit");
        Assert.Equal(sent + 1, h.Players[0].Client.QuitTics[1]);
        Assert.Equal(sent + 1, h.Players[2].Client.QuitTics[1]);
        Assert.Null(h.Server.Desync);
        Assert.True(h.Server.WindowsCompared >= 1500 / 35 - 2);
        Assert.Contains(h.ServerLog, line => line.Contains("slot 1 left the game", StringComparison.Ordinal));
    }

    [Fact]
    public void The_host_quitting_disconnects_everyone()
    {
        var h = new LockstepHarness(_coop, Setups(2, 30, 0, 0), (slot, tic) => LockstepHarness.Wander(slot, tic, fire: false), seed: 2);
        h.JoinAndReady();
        h.Server.Start(3);
        h.RunUntil(() => h.Players[1].TicsRun >= 100, 10_000_000, "100 tics");
        h.HostDown = true;
        h.ServerTransport.Dispose();
        h.RunUntil(() => h.Players[1].Client.State == NetClientState.Disconnected, 5_000_000, "the client disconnected");
        Assert.Equal("The server is gone.", h.Players[1].Client.Error);
    }

    [Fact]
    public void The_settings_travel_with_the_start()
    {
        var settings = new NetGameSettings(
            new SpawnSettings(GameMode.commercial, skill_t.sk_hard, netgame: true, deathmatch: 2, nomonsters: true, respawnparm: true, fastparm: true,
                gameversion: GameVersion.exe_final, gamemission: GameMission.pack_tnt, timelimit: 20),
            Tweaks.TopDown with { AimAssistCone = 3 * Tables.ANG1 }, Episode: 1, Map: 7);
        var h = new LockstepHarness(_coop, Setups(3, 20, 5, 0), (slot, tic) => default, seed: 6);
        h.RunUntil(() => h.Players.TrueForAll(p => p.Client.State == NetClientState.Lobby), 5_000_000, "the lobby");
        Assert.All(h.Players, p => Assert.Equal(_coop, p.Client.Settings));
        h.Server.Settings = settings;
        h.JoinAndReady();
        Assert.All(h.Players, p => Assert.Equal(settings, p.Client.Settings));
        h.Server.Start(2, startTic: 100);
        h.RunUntil(() => h.Players.TrueForAll(p => p.TicsRun >= 70), 10_000_000, "70 tics");
        for (int i = 0; i < h.Players.Count; i++)
        {
            NetStart start = h.Players[i].Client.Start!;
            // T11.5a: the Start sets the settings' players in game to its mask.
            Assert.Equal(new NetStart(i, 0b111, 100, 2, settings with { Spawn = settings.Spawn with { playersingame = 0b111 } }, Adaptive: true), start);
        }
        Assert.Throws<InvalidOperationException>(() => h.Server.Settings = _coop);
    }
}
