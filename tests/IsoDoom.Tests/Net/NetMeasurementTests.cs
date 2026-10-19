using System;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Game;
using IsoDoom.Net;
using IsoDoom.Sim;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Net;

/// <summary>
/// T11.12: the bandwidth a player uses and the input delay felt at 30, 80 and
/// 150 ms of ping (round trip), measured on <see cref="MemoryTransport"/> with
/// the game's default delay rule (<see cref="NetOptions.ChooseDelay"/>, as
/// <see cref="NetSession"/> starts the game; T11.12a: the longest of each
/// player's round trips once each reported <see cref="NetOptions.MinRttSamples"/>). Protocol only (the commands' bytes do not depend on the world):
/// a host and one or three remote players, each remote one at that ping with
/// 5 ms of jitter each way, scripted commands that walk, turn, use and fire
/// every tic, a minute of play. T11.12e: and on home wifi
/// (<see cref="NetLinks.Wifi"/>: a low base, bursts of 50–150 ms spikes, a
/// little loss both ways), with the stalls a minute (<see cref="StallStats"/>)
/// beside the bandwidth and the delay. The numbers are logged in SPEC §12
/// ("T11.12 measurements"); the asserts hold them near what was logged.
/// </summary>
public class NetMeasurementTests
{
    private const int Tics = 35 * 60;

    /// <summary>
    /// ENet's and UDP/IPv4's bytes on top of a payload, per reliable packet:
    /// IPv4 20, UDP 8, ENet's header 4 and a reliable send's command 6, and
    /// the 8-byte acknowledgement the receiver sends back for it (protocol.h).
    /// </summary>
    private const int WireOverhead = 20 + 8 + 4 + 6 + 8;

    /// <summary>
    /// T11.12c: the same per unreliable (unsequenced) packet: IPv4 20, UDP 8,
    /// ENet's header 2 (no sent time without a reliable command) and an
    /// unsequenced send's command 8; nothing comes back.
    /// </summary>
    private const int UnreliableWireOverhead = 20 + 8 + 2 + 8;

    private static readonly NetGameSettings _coop = new(
        new SpawnSettings(GameMode.shareware, skill_t.sk_medium, netgame: true), Tweaks.TopDown, Episode: 1, Map: 1);

    /// <summary>What one run measured.</summary>
    /// <param name="Delay">The input delay the rule gave, in tics.</param>
    /// <param name="MeasuredRttMs">The round trip the rule took (T11.12a: the slowest player's longest lately).</param>
    /// <param name="LagMeanMs">The remote players' felt input delay, mean.</param>
    /// <param name="LagMaxMs">… and worst.</param>
    /// <param name="HostLagMeanMs">The host's felt input delay, mean.</param>
    /// <param name="LateTics">Tics a remote player ran after their time (a bundle late).</param>
    /// <param name="UpBps">A remote player's upload, payload, bytes a second.</param>
    /// <param name="DownBps">The server's sending per player, payload, bytes a second.</param>
    /// <param name="UpPackets">A remote player's packets a second.</param>
    /// <param name="DownPackets">The server's packets a second per player.</param>
    /// <param name="Stalls">T11.12e: the stalls of the player held longest (the host's included).</param>
    /// <param name="UpUnreliablePackets">T11.12c: of <paramref name="UpPackets"/>, those on the unreliable channel.</param>
    /// <param name="DownUnreliablePackets">T11.12c: of <paramref name="DownPackets"/>, those on the unreliable channel.</param>
    /// <param name="WaitMeanMs">T11.12d: the remote players' bundle wait (<see cref="HarnessPlayer.BundleWaitUs"/>: a command made to its tic's bundle in), mean.</param>
    /// <param name="WaitP99Ms">… its 99th percentile.</param>
    /// <param name="HostWaitMeanMs">T11.12d: the host's bundle wait, mean.</param>
    /// <param name="FinalDelay">T11.12b: the delay at the end (<paramref name="Delay"/> is the start's).</param>
    /// <param name="MaxDelay">T11.12b: the longest delay it played at.</param>
    /// <param name="DelayChanges">T11.12b: how many times the host changed it.</param>
    /// <param name="MinDelay">T11.12b: the shortest delay it played at.</param>
    public sealed record Measurement(int Delay, double MeasuredRttMs, double LagMeanMs, double LagMaxMs, double HostLagMeanMs, int LateTics,
        double UpBps, double DownBps, double UpPackets, double DownPackets, StallStats Stalls, double UpUnreliablePackets, double DownUnreliablePackets,
        double WaitMeanMs = 0, double WaitP99Ms = 0, double HostWaitMeanMs = 0, int FinalDelay = 0, int MaxDelay = 0, int DelayChanges = 0, int MinDelay = 0)
    {
        /// <summary>Upload with the headers (<see cref="WireOverhead"/>, <see cref="UnreliableWireOverhead"/>).</summary>
        public double UpWireBps => UpBps + ((UpPackets - UpUnreliablePackets) * WireOverhead) + (UpUnreliablePackets * UnreliableWireOverhead);

        /// <summary>Download with the headers.</summary>
        public double DownWireBps => DownBps + ((DownPackets - DownUnreliablePackets) * WireOverhead) + (DownUnreliablePackets * UnreliableWireOverhead);

        public override string ToString()
            => $"rtt {MeasuredRttMs:F0} ms, delay {Delay} ({Delay * 1000.0 / NetProtocol.TicRate:F0} ms){(DelayChanges > 0 ? $", then {DelayChanges} changes, up to {MaxDelay}, {FinalDelay} at the end" : "")}: felt {LagMeanMs:F0} ms mean, {LagMaxMs:F0} max (host {HostLagMeanMs:F0}), {LateTics} late tics; " +
               $"up {UpBps:F0} B/s ({UpPackets:F1} packets/s, {UpWireBps:F0} B/s on the wire), down {DownBps:F0} B/s ({DownPackets:F1} packets/s, {DownWireBps:F0} on the wire); " +
               $"bundle wait {WaitMeanMs:F0} ms mean, {WaitP99Ms:F0} p99 (host {HostWaitMeanMs:F0}); {Stalls}";
    }

    /// <summary>Play a minute: a host and <paramref name="players"/> − 1 remote players at <paramref name="pingMs"/> round trip.</summary>
    /// <param name="players">The players, the host's own included.</param>
    /// <param name="pingMs">Each remote player's round trip to the host, in ms.</param>
    /// <param name="jitterMs">The remote links' jitter each way, in ms.</param>
    /// <param name="loss">The remote links' unreliable loss (half of it reliable resends).</param>
    /// <param name="delayOffset">Added to the rule's delay (a smaller delay than the rule's, to see what its margin buys).</param>
    public static Measurement Measure(int players, int pingMs, int jitterMs = 5, double loss = 0, int delayOffset = 0)
        => Measure(players, NetLinks.Internet(pingMs, jitterMs, loss), seed: pingMs + players, delayOffset: delayOffset);

    /// <summary>
    /// Play a minute: a host and <paramref name="players"/> − 1 remote players
    /// on <paramref name="link"/> (<see cref="NetLinks"/>; the server's end
    /// adds none: the remote link carries the whole one-way latency).
    /// </summary>
    /// <param name="players">The players, the host's own included.</param>
    /// <param name="link">Each remote player's link.</param>
    /// <param name="seed">Seeds the network's jitter, spikes and loss.</param>
    /// <param name="delayOffset">Added to the rule's delay.</param>
    /// <param name="delay">The delay to play at instead of the rule's (a later task's own rule, or <c>-netdelay</c>).</param>
    /// <param name="frameUs">T11.12d: every machine's frame time (the remote players' a millisecond longer each, so the frames drift).</param>
    /// <param name="configure">T11.12d: sets the harness up before the players join (<see cref="LockstepHarness.PollAfterFlush"/>, <see cref="LockstepHarness.ServerEveryStep"/>).</param>
    /// <param name="adaptive">T11.12b: whether the delay adapts in the game (false: as before T11.12b, or <c>-netdelay</c>).</param>
    public static Measurement Measure(int players, MemoryLinkOptions link, int seed, int delayOffset = 0, int? delay = null, long frameUs = 16_667, Action<LockstepHarness>? configure = null,
        bool adaptive = true)
        => Measure(players, link, seed, out _, delayOffset, delay, frameUs, configure, adaptive);

    /// <inheritdoc cref="Measure(int, MemoryLinkOptions, int, int, int?, long, Action{LockstepHarness}?, bool)"/>
    /// <param name="players">The players, the host's own included.</param>
    /// <param name="link">Each remote player's link.</param>
    /// <param name="seed">Seeds the network's jitter, spikes and loss.</param>
    /// <param name="harness">The harness, after the minute (its players' stalls and logs).</param>
    /// <param name="delayOffset">Added to the rule's delay.</param>
    /// <param name="delay">The delay to play at instead of the rule's.</param>
    /// <param name="frameUs">T11.12d: every machine's frame time.</param>
    /// <param name="configure">T11.12d: sets the harness up.</param>
    /// <param name="adaptive">T11.12b: whether the delay adapts in the game.</param>
    public static Measurement Measure(int players, MemoryLinkOptions link, int seed, out LockstepHarness harness, int delayOffset = 0, int? delay = null,
        long frameUs = 16_667, Action<LockstepHarness>? configure = null, bool adaptive = true)
    {
        var setups = new List<PlayerSetup> { new() { FrameUs = frameUs } };
        for (int i = 1; i < players; i++)
        {
            setups.Add(new PlayerSetup
            {
                Link = link,
                FrameUs = frameUs + (1_000 * i),
                PhaseUs = 3_000 * i,
            });
        }
        var h = new LockstepHarness(_coop, setups, (slot, tic) => LockstepHarness.Wander(slot, tic, fire: true), seed: seed);
        configure?.Invoke(h);
        harness = h;
        h.JoinAndReady();
        // T11.12a: the host's rule, as NetSession picks it: once every player reported enough round trips
        NetDelayChoice? choice = null;
        h.RunUntil(() => (choice = NetOptions.ChooseDelay(h.Server, waited: false)) is not null, NetSession.RttWaitUs, "the round trips");
        h.ServerLog.Add(choice!.ToString());
        TestContext.Current.TestOutputHelper?.WriteLine(choice.ToString());
        int playDelay = delay ?? Math.Max(1, choice.Delay + delayOffset);

        long upBytes0 = Remote(h).Sum(p => p.Transport.ReliableBytesSent + p.Transport.UnreliableBytesSent);
        long upPackets0 = Remote(h).Sum(p => p.Transport.PacketsSent);
        long upUnreliable0 = Remote(h).Sum(p => p.Transport.UnreliablePacketsSent);
        long downUnreliable0 = h.ServerTransport.UnreliablePacketsSent;
        long downBytes0 = h.ServerTransport.ReliableBytesSent + h.ServerTransport.UnreliableBytesSent;
        long downPackets0 = h.ServerTransport.PacketsSent;
        long start = h.Network.NowUs;
        h.Play(playDelay, Tics, 120_000_000, adaptive);
        double seconds = (h.Network.NowUs - start) / 1e6;

        Assert.Null(h.Server.Desync);
        var remote = Remote(h).ToList();
        var lags = remote.SelectMany(p => p.InputLagUs).ToList();
        var waits = remote.SelectMany(p => p.BundleWaitUs).Order().ToList();
        double upBytes = remote.Sum(p => p.Transport.ReliableBytesSent + p.Transport.UnreliableBytesSent) - upBytes0;
        double upPackets = remote.Sum(p => p.Transport.PacketsSent) - upPackets0;
        double downBytes = h.ServerTransport.ReliableBytesSent + h.ServerTransport.UnreliableBytesSent - downBytes0;
        double downPackets = h.ServerTransport.PacketsSent - downPackets0;
        return new Measurement(playDelay, choice.RttUs / 1000.0, lags.Average() / 1000, lags.Max() / 1000.0, h.Players[0].InputLagUs.Average() / 1000,
            remote.Sum(p => p.LateTics),
            upBytes / remote.Count / seconds, downBytes / players / seconds,
            upPackets / remote.Count / seconds, downPackets / players / seconds,
            StallStats.Worst(h.Players),
            (remote.Sum(p => p.Transport.UnreliablePacketsSent) - upUnreliable0) / remote.Count / seconds,
            (h.ServerTransport.UnreliablePacketsSent - downUnreliable0) / players / seconds,
            waits.Average() / 1000, waits[(int)(waits.Count * 0.99)] / 1000.0, h.Players[0].BundleWaitUs.Average() / 1000,
            h.Server.Delay, h.Server.DelayChanges.Select(c => c.To).Append(playDelay).Max(), h.Server.DelayChanges.Count,
            h.Server.DelayChanges.Select(c => c.To).Append(playDelay).Min());
    }

    private static IEnumerable<HarnessPlayer> Remote(LockstepHarness h) => h.Players.Skip(1);

    /// <summary>
    /// The default delay rule's delays for SPEC §12's pings (T11.12a): round
    /// trip and two frames in tics rounded up, plus one (the measured round
    /// trips, frame waits and jitter included, give a tic or so more).
    /// </summary>
    [Theory]
    [InlineData(30, 4)]
    [InlineData(80, 5)]
    [InlineData(150, 8)]
    public void The_default_delay_for_each_ping(int pingMs, int delay)
        => Assert.Equal(delay, NetOptions.DelayFromRtt(pingMs * 1000L));

    /// <summary>
    /// At each ping, with the rule's delay: the felt delay is the delay's tics
    /// (and up to a frame), hardly a tic runs late, and a player's traffic stays well
    /// under the plan's 10 KB/s each way, headers included. T11.12b: the delay
    /// then adapts (down a tic at 30 ms, where the rule's margin is more than
    /// the waits need; up a tic at 150 ms with 4 players, past the rule's
    /// cap of 8), so the felt delay is checked against the delays it played at.
    /// </summary>
    [Theory]
    [InlineData(2, 30)]
    [InlineData(2, 80)]
    [InlineData(2, 150)]
    [InlineData(4, 30)]
    [InlineData(4, 80)]
    [InlineData(4, 150)]
    public void Bandwidth_and_input_delay_at_internet_pings(int players, int pingMs)
    {
        Measurement m = Measure(players, pingMs);
        TestContext.Current.TestOutputHelper?.WriteLine($"{players} players, ping {pingMs} ms: {m}");
        // The rule from the measured round trip: the ping, the jitter, and the
        // frame each end waits before it reads a packet (the game polls its
        // transport once a frame), about 20-30 ms more than the ping.
        Assert.InRange(m.MeasuredRttMs, pingMs, pingMs + 2 * 5 + 2 * 18);
        Assert.Equal(NetOptions.DelayFromRtt((long)(m.MeasuredRttMs * 1000)), m.Delay);
        double delayMs = m.MinDelay * 1000.0 / NetProtocol.TicRate, maxDelayMs = m.MaxDelay * 1000.0 / NetProtocol.TicRate;
        Assert.InRange(m.LagMeanMs, delayMs - 20, maxDelayMs + 20);
        Assert.InRange(m.HostLagMeanMs, delayMs - 20, maxDelayMs + 20);
        Assert.True(m.LagMaxMs <= maxDelayMs + 40, $"felt up to {m.LagMaxMs:F0} ms with a delay of {delayMs:F0}-{maxDelayMs:F0} ms");
        Assert.True(m.MaxDelay - m.MinDelay <= 2, $"the delay moved from {m.MinDelay} to {m.MaxDelay}");
        // a rare late bundle (jitter on top of a slow frame), under 1 % of the tics: a held frame, not a stall
        Assert.True(m.LateTics <= Tics / 100 * (players - 1), $"{m.LateTics} tics ran late");
        Assert.True(m.UpWireBps < 10_000, $"up {m.UpWireBps:F0} B/s");
        Assert.True(m.DownWireBps < 10_000, $"down {m.DownWireBps:F0} B/s");
    }

    /// <summary>
    /// The rule's margin (its + 1 and the measured round trip's frame waits):
    /// two tics less than the rule's delay at 150 ms still plays (the lag
    /// adjustment keeps the clocks), but a quarter of the tics come late (the
    /// view holds a frame); logged beside the rule's.
    /// </summary>
    [Fact]
    public void A_delay_below_the_rule_runs_tics_late()
    {
        Measurement rule = Measure(2, 150, jitterMs: 15);
        Measurement less = Measure(2, 150, jitterMs: 15, delayOffset: -2);
        TestContext.Current.TestOutputHelper?.WriteLine($"rule: {rule}");
        TestContext.Current.TestOutputHelper?.WriteLine($"rule − 2: {less}");
        Assert.True(less.LateTics > rule.LateTics, $"{less.LateTics} late tics with 2 less, {rule.LateTics} with the rule's");
    }

    /// <summary>
    /// T11.12e: home wifi (<see cref="NetLinks.Wifi"/>) under the rule's delay.
    /// Before T11.12a (one ping, + 1 tic: delay 2–3) the world held 14–15 s a
    /// minute; T11.12a's rule (the longest of 3 s of round trips, + 2 frames,
    /// + 1: delay 6–8 here) holds it under a second a minute, and what was left
    /// was the reliable channel's resends (150 ms each) of the commands and
    /// bundles: T11.12c sends those unreliable, and the world holds a stall or
    /// two a minute, none longer than the round trip (the reliable resends now
    /// delay only the checksums and the lobby). Without the reliable resends
    /// nothing holds after the first second. Logged in SPEC §12
    /// ("T11.12 measurements"); T11.12d: none after the first second.
    /// </summary>
    [Theory]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 3)]
    [InlineData(4, 1)]
    public void Stalls_on_home_wifi(int players, int seed)
    {
        Measurement m = Measure(players, NetLinks.Wifi, seed, out LockstepHarness h);
        TestContext.Current.TestOutputHelper?.WriteLine($"{players} players, wifi, seed {seed}: {m}");
        foreach (HarnessPlayer p in h.Players)
            TestContext.Current.TestOutputHelper?.WriteLine($"  player {p.Index}: {StallStats.Of(p)}");
        Assert.All(h.Players, p => Assert.True(p.TicsRun >= Tics));
        Assert.True(m.UpWireBps < 10_000, $"up {m.UpWireBps:F0} B/s");
        Assert.True(m.DownWireBps < 10_000, $"down {m.DownWireBps:F0} B/s");
        // T11.12a: under a second a minute (0.4-0.7 s logged), from 14-15 s before; T11.12c: what
        // was left were the reliable resends of the commands and bundles, now unreliable: 0-35 ms
        // logged, and none longer than the round trip
        Assert.True(m.Stalls.HeldMsPerMinute < 100, $"held {m.Stalls.HeldMsPerMinute:F0} ms a minute on wifi");
        Assert.True(m.Stalls.LongestMs <= m.MeasuredRttMs, $"a stall of {m.Stalls.LongestMs:F0} ms on wifi, the round trip {m.MeasuredRttMs:F0} ms");
        // T11.12d: the host's own command bundled in its frame: none after the first second (0-1 of 35 ms before)
        Assert.Equal(0, StallStats.Worst(h.Players, afterUs: 1_000_000).Count);

        // the same wifi without the reliable resends: none after the first second
        Measurement noResends = Measure(players, NetLinks.Wifi with { ReliableLoss = 0 }, seed, out LockstepHarness h2);
        TestContext.Current.TestOutputHelper?.WriteLine($"  without resends: {noResends}");
        StallStats after = StallStats.Worst(h2.Players, afterUs: 1_000_000);
        Assert.True(after.Count == 0, $"without resends: {after}");
    }

    /// <summary>
    /// T11.12e: the manual fix the player readme gives, <c>-netdelay</c>, on
    /// the same wifi: each delay's stalls logged beside the rule's (SPEC §12),
    /// fewer and shorter as the delay grows (T11.12a: the rule's is 6 here;
    /// T11.12c: from 6 on none, so no fewer).
    /// </summary>
    [Fact]
    public void A_longer_netdelay_on_home_wifi()
    {
        Measurement rule = Measure(2, NetLinks.Wifi, seed: 1);
        TestContext.Current.TestOutputHelper?.WriteLine($"rule: {rule}");
        Measurement? previous = null;
        foreach (int delay in new[] { 3, 4, 6, 8 })
        {
            Measurement m = Measure(2, NetLinks.Wifi, seed: 1, delay: delay);
            TestContext.Current.TestOutputHelper?.WriteLine($"-netdelay {delay}: {m}");
            if (previous is not null)
                Assert.True(m.Stalls.TotalMs < previous.Stalls.TotalMs || m.Stalls.Count <= 1, $"-netdelay {delay}: held {m.Stalls.TotalMs:F0} ms, {previous.Stalls.TotalMs:F0} with less");
            previous = m;
        }
    }

    /// <summary>
    /// T11.12c's <i>Done when</i>: on a lossy link at a 30 ms round trip
    /// (<see cref="NetLinks.Lossy"/>: 5 % of the unreliable packets lost) a
    /// minute plays in sync (the harness compares every tic's checksum) with
    /// no stall after the first second longer than a round trip (the one the
    /// rule took, the frames' waits included): a lost command or bundle is
    /// carried again by the next. With loss bursts on top
    /// (<see cref="NetLinks.LossBursts"/>: every packet lost for 30–90 ms
    /// every few seconds) a burst costs a round trip: no stall longer than the
    /// longest burst and a round trip.
    /// </summary>
    [Theory]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 3)]
    [InlineData(4, 1)]
    public void Lost_packets_hold_no_longer_than_a_round_trip(int players, int seed)
    {
        Measurement m = Measure(players, NetLinks.Lossy, seed, out LockstepHarness h);
        TestContext.Current.TestOutputHelper?.WriteLine($"{players} players, lossy, seed {seed}: {m}");
        StallStats after = StallStats.Worst(h.Players, afterUs: 1_000_000);
        TestContext.Current.TestOutputHelper?.WriteLine($"  after the first second: {after}");
        Assert.All(h.Players, p => Assert.True(p.TicsRun >= Tics));
        Assert.All(h.Players, p => Assert.True(StallStats.Of(p, afterUs: 1_000_000).LongestMs <= m.MeasuredRttMs,
            $"player {p.Index}: {StallStats.Of(p, afterUs: 1_000_000)}, the round trip {m.MeasuredRttMs:F0} ms"));
        Assert.True(m.UpWireBps < 10_000, $"up {m.UpWireBps:F0} B/s");
        Assert.True(m.DownWireBps < 10_000, $"down {m.DownWireBps:F0} B/s");

        Measurement bursts = Measure(players, NetLinks.LossBursts, seed, out LockstepHarness h2);
        TestContext.Current.TestOutputHelper?.WriteLine($"  with loss bursts: {bursts}");
        foreach (HarnessPlayer p in h2.Players)
            TestContext.Current.TestOutputHelper?.WriteLine($"    player {p.Index}: {StallStats.Of(p, afterUs: 1_000_000)}");
        Assert.All(h2.Players, p => Assert.True(p.TicsRun >= Tics));
        Assert.All(h2.Players, p => Assert.True(StallStats.Of(p, afterUs: 1_000_000).LongestMs <= NetLinks.LongestLossBurstMs + bursts.MeasuredRttMs,
            $"player {p.Index} with loss bursts: {StallStats.Of(p, afterUs: 1_000_000)}, the round trip {bursts.MeasuredRttMs:F0} ms"));
    }

    /// <summary>
    /// T11.12a's <i>Done when</i>: on a jittery link (<see cref="NetLinks.Jittery"/>:
    /// 5–100 ms each way, bursts of spikes, no loss) the default delay, picked
    /// from the lobby's round trips (logged), plays a minute with no stall
    /// after the first second.
    /// </summary>
    [Theory]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 3)]
    [InlineData(4, 1)]
    public void The_default_delay_covers_a_jittery_link(int players, int seed)
    {
        Measurement m = Measure(players, NetLinks.Jittery, seed, out LockstepHarness h);
        TestContext.Current.TestOutputHelper?.WriteLine($"{players} players, jittery, seed {seed}: {m}");
        Assert.Contains(h.ServerLog, l => l.StartsWith("round trips: slowest player ", StringComparison.Ordinal) && l.EndsWith($"delay {m.Delay} tic(s)", StringComparison.Ordinal));
        StallStats after = StallStats.Worst(h.Players, afterUs: 1_000_000);
        TestContext.Current.TestOutputHelper?.WriteLine($"after the first second: {after}");
        Assert.Equal(0, after.Count);
    }

    /// <summary>
    /// T11.12d's <i>Done when</i>, with every machine's frames at 60 and at 30
    /// fps on an internet link (5 ms of jitter each way) at 2 (a LAN), 30 and
    /// 80 ms of ping, two players at the rule's delay. The input delay a tic
    /// needs is its bundle wait (<see cref="HarnessPlayer.BundleWaitUs"/>: a
    /// command made to its tic's bundle in, every command for it at the
    /// server and back: the felt delay at the least delay that does not
    /// hold). Polled once a frame (before T11.12d) the host's own command
    /// reached its server (over loopback) only at the host's next frame;
    /// polled again after the commands are flushed it is bundled in the frame
    /// it is made: the host's wait is down by about a frame (half to two
    /// thirds of one on average, the wait being a frame's phase), the remote
    /// player's no worse (a little better on a LAN, where the host's command
    /// came last). The rule's delay is the same: the pings never saw that hop.
    /// A network thread (<see cref="LockstepHarness.ServerEveryStep"/>: the
    /// server serviced every millisecond) is measured beside: the remote
    /// player's command no longer waits for the host's frame, so its wait is
    /// down too and its pings measure a frame less (the rule a tic less at
    /// 30 fps); SPEC §12 T11.12d says why it is not built.
    /// </summary>
    [Theory]
    [InlineData(16_667, 2)]
    [InlineData(16_667, 30)]
    [InlineData(16_667, 80)]
    [InlineData(33_333, 2)]
    [InlineData(33_333, 30)]
    [InlineData(33_333, 80)]
    public void Polling_again_after_the_commands_saves_the_hosts_frame(long frameUs, int pingMs)
    {
        MemoryLinkOptions link = NetLinks.Internet(pingMs, pingMs < 10 ? 1 : 5);
        Measurement before = Measure(2, link, seed: 1, frameUs: frameUs, configure: h => h.PollAfterFlush = false);
        Measurement after = Measure(2, link, seed: 1, frameUs: frameUs);
        Measurement thread = Measure(2, link, seed: 1, frameUs: frameUs, configure: h => h.ServerEveryStep = true);
        double frameMs = frameUs / 1000.0;
        TestContext.Current.TestOutputHelper?.WriteLine($"{1000 / frameMs:F0} fps, ping {pingMs} ms: before T11.12d: {before}");
        TestContext.Current.TestOutputHelper?.WriteLine($"  polled again: {after}");
        TestContext.Current.TestOutputHelper?.WriteLine($"  a network thread: {thread}");
        Assert.True(before.HostWaitMeanMs - after.HostWaitMeanMs >= 0.4 * frameMs,
            $"the host's wait {before.HostWaitMeanMs:F1} -> {after.HostWaitMeanMs:F1} ms at a frame of {frameMs:F1} ms");
        Assert.True(after.WaitMeanMs <= before.WaitMeanMs + 0.5, $"the remote wait {before.WaitMeanMs:F1} -> {after.WaitMeanMs:F1} ms");
        Assert.True(after.WaitP99Ms <= before.WaitP99Ms + 1, $"the remote wait's p99 {before.WaitP99Ms:F1} -> {after.WaitP99Ms:F1} ms");
        Assert.Equal(before.Delay, after.Delay);
        Assert.Equal(0, after.Stalls.Count);
        Assert.True(thread.WaitMeanMs < after.WaitMeanMs, $"the remote wait with a thread {thread.WaitMeanMs:F1} ms, polled again {after.WaitMeanMs:F1}");
        Assert.True(thread.Delay <= after.Delay);
    }
}
