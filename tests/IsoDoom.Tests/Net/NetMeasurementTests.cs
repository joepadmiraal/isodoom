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
/// the game's default delay rule (<see cref="NetOptions.DelayFromRtt"/> of the
/// slowest round trip the server measured, as <see cref="NetSession"/> starts
/// the game). Protocol only (the commands' bytes do not depend on the world):
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
    /// ENet's and UDP/IPv4's bytes on top of a payload, per packet: IPv4 20,
    /// UDP 8, ENet's header 4 and a reliable send's command 6, and the 8-byte
    /// acknowledgement the receiver sends back for it (protocol.h).
    /// </summary>
    private const int WireOverhead = 20 + 8 + 4 + 6 + 8;

    private static readonly NetGameSettings _coop = new(
        new SpawnSettings(GameMode.shareware, skill_t.sk_medium, netgame: true), Tweaks.TopDown, Episode: 1, Map: 1);

    /// <summary>What one run measured.</summary>
    /// <param name="Delay">The input delay the rule gave, in tics.</param>
    /// <param name="MeasuredRttMs">The slowest round trip the server measured.</param>
    /// <param name="LagMeanMs">The remote players' felt input delay, mean.</param>
    /// <param name="LagMaxMs">… and worst.</param>
    /// <param name="HostLagMeanMs">The host's felt input delay, mean.</param>
    /// <param name="LateTics">Tics a remote player ran after their time (a bundle late).</param>
    /// <param name="UpBps">A remote player's upload, payload, bytes a second.</param>
    /// <param name="DownBps">The server's sending per player, payload, bytes a second.</param>
    /// <param name="UpPackets">A remote player's packets a second.</param>
    /// <param name="DownPackets">The server's packets a second per player.</param>
    /// <param name="Stalls">T11.12e: the stalls of the player held longest (the host's included).</param>
    public sealed record Measurement(int Delay, double MeasuredRttMs, double LagMeanMs, double LagMaxMs, double HostLagMeanMs, int LateTics,
        double UpBps, double DownBps, double UpPackets, double DownPackets, StallStats Stalls)
    {
        /// <summary>Upload with the headers (<see cref="WireOverhead"/>).</summary>
        public double UpWireBps => UpBps + (UpPackets * WireOverhead);

        /// <summary>Download with the headers.</summary>
        public double DownWireBps => DownBps + (DownPackets * WireOverhead);

        public override string ToString()
            => $"rtt {MeasuredRttMs:F0} ms, delay {Delay} ({Delay * 1000.0 / NetProtocol.TicRate:F0} ms): felt {LagMeanMs:F0} ms mean, {LagMaxMs:F0} max (host {HostLagMeanMs:F0}), {LateTics} late tics; " +
               $"up {UpBps:F0} B/s ({UpPackets:F1} packets/s, {UpWireBps:F0} B/s on the wire), down {DownBps:F0} B/s ({DownPackets:F1} packets/s, {DownWireBps:F0} on the wire); " +
               $"{Stalls}";
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
    public static Measurement Measure(int players, MemoryLinkOptions link, int seed, int delayOffset = 0, int? delay = null) => Measure(players, link, seed, out _, delayOffset, delay);

    /// <inheritdoc cref="Measure(int, MemoryLinkOptions, int, int, int?)"/>
    /// <param name="players">The players, the host's own included.</param>
    /// <param name="link">Each remote player's link.</param>
    /// <param name="seed">Seeds the network's jitter, spikes and loss.</param>
    /// <param name="harness">The harness, after the minute (its players' stalls and logs).</param>
    /// <param name="delayOffset">Added to the rule's delay.</param>
    /// <param name="delay">The delay to play at instead of the rule's.</param>
    public static Measurement Measure(int players, MemoryLinkOptions link, int seed, out LockstepHarness harness, int delayOffset = 0, int? delay = null)
    {
        var setups = new List<PlayerSetup> { new() };
        for (int i = 1; i < players; i++)
        {
            setups.Add(new PlayerSetup
            {
                Link = link,
                FrameUs = 16_667 + (1_000 * i),
                PhaseUs = 3_000 * i,
            });
        }
        var h = new LockstepHarness(_coop, setups, (slot, tic) => LockstepHarness.Wander(slot, tic, fire: true), seed: seed);
        harness = h;
        h.JoinAndReady();
        long maxRtt = Enumerable.Range(0, players).Max(h.Server.RttUsOf);
        int playDelay = delay ?? Math.Max(1, NetOptions.DelayFromRtt(maxRtt) + delayOffset);

        long upBytes0 = Remote(h).Sum(p => p.Transport.ReliableBytesSent + p.Transport.UnreliableBytesSent);
        long upPackets0 = Remote(h).Sum(p => p.Transport.PacketsSent);
        long downBytes0 = h.ServerTransport.ReliableBytesSent + h.ServerTransport.UnreliableBytesSent;
        long downPackets0 = h.ServerTransport.PacketsSent;
        long start = h.Network.NowUs;
        h.Play(playDelay, Tics, 120_000_000);
        double seconds = (h.Network.NowUs - start) / 1e6;

        Assert.Null(h.Server.Desync);
        var remote = Remote(h).ToList();
        var lags = remote.SelectMany(p => p.InputLagUs).ToList();
        double upBytes = remote.Sum(p => p.Transport.ReliableBytesSent + p.Transport.UnreliableBytesSent) - upBytes0;
        double upPackets = remote.Sum(p => p.Transport.PacketsSent) - upPackets0;
        double downBytes = h.ServerTransport.ReliableBytesSent + h.ServerTransport.UnreliableBytesSent - downBytes0;
        double downPackets = h.ServerTransport.PacketsSent - downPackets0;
        return new Measurement(playDelay, maxRtt / 1000.0, lags.Average() / 1000, lags.Max() / 1000.0, h.Players[0].InputLagUs.Average() / 1000,
            remote.Sum(p => p.LateTics),
            upBytes / remote.Count / seconds, downBytes / players / seconds,
            upPackets / remote.Count / seconds, downPackets / players / seconds,
            StallStats.Worst(h.Players));
    }

    private static IEnumerable<HarnessPlayer> Remote(LockstepHarness h) => h.Players.Skip(1);

    /// <summary>The default delay rule's delays for SPEC §12's pings: round trip in tics rounded up, plus one.</summary>
    [Theory]
    [InlineData(30, 3)]
    [InlineData(80, 4)]
    [InlineData(150, 7)]
    public void The_default_delay_for_each_ping(int pingMs, int delay)
        => Assert.Equal(delay, NetOptions.DelayFromRtt(pingMs * 1000L));

    /// <summary>
    /// At each ping, with the rule's delay: the felt delay is the delay's tics
    /// (and up to a frame), hardly a tic runs late, and a player's traffic stays well
    /// under the plan's 10 KB/s each way, headers included.
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
        double delayMs = m.Delay * 1000.0 / NetProtocol.TicRate;
        Assert.InRange(m.LagMeanMs, delayMs - 20, delayMs + 20);
        Assert.InRange(m.HostLagMeanMs, delayMs - 20, delayMs + 20);
        Assert.True(m.LagMaxMs <= delayMs + 40, $"felt up to {m.LagMaxMs:F0} ms with a delay of {delayMs:F0} ms");
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
    /// T11.12e: home wifi (<see cref="NetLinks.Wifi"/>) under the rule's delay,
    /// the baseline before T11.12a–d: the game's clocks and windows stay in
    /// sync, but the delay (from a round trip measured between the bursts)
    /// does not cover a spike, so the world holds a few times a second's
    /// burst: the freezes of T11.12's LAN test. Logged in SPEC §12 ("T11.12
    /// measurements"); T11.12a–d replace the bounds with their own.
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
        // the baseline: held 14-15 s a minute by the player held longest (SPEC §12)
        Assert.True(m.Stalls.HeldMsPerMinute > 5_000, $"held only {m.Stalls.HeldMsPerMinute:F0} ms a minute on wifi before T11.12a-d");
    }

    /// <summary>
    /// T11.12e: the manual fix the player readme gives, <c>-netdelay</c>, on
    /// the same wifi: each delay's stalls logged beside the rule's (SPEC §12),
    /// fewer and shorter as the delay grows.
    /// </summary>
    [Fact]
    public void A_longer_netdelay_on_home_wifi()
    {
        Measurement rule = Measure(2, NetLinks.Wifi, seed: 1);
        TestContext.Current.TestOutputHelper?.WriteLine($"rule: {rule}");
        Measurement previous = rule;
        foreach (int delay in new[] { 4, 6, 8 })
        {
            Measurement m = Measure(2, NetLinks.Wifi, seed: 1, delay: delay);
            TestContext.Current.TestOutputHelper?.WriteLine($"-netdelay {delay}: {m}");
            Assert.True(m.Stalls.TotalMs < previous.Stalls.TotalMs, $"-netdelay {delay}: held {m.Stalls.TotalMs:F0} ms, {previous.Stalls.TotalMs:F0} with less");
            previous = m;
        }
    }
}
