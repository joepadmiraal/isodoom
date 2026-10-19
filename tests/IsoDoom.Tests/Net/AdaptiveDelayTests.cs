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
/// T11.12b: the input delay adapts in the game (<see cref="NetDelayControl"/>,
/// SPEC §12 "T11.12b The delay adapts in the game"): the host raises it when
/// the players' bundle waits no longer fit it and lowers it a tic at a time
/// after a calm spell; <c>-netdelay</c> pins it. Protocol only on
/// <see cref="MemoryTransport"/>; the harness checks every tic's bundle and
/// checksum on every player as before.
/// </summary>
public class AdaptiveDelayTests
{
    private static readonly NetGameSettings _coop = new(
        new SpawnSettings(GameMode.shareware, skill_t.sk_medium, netgame: true), Tweaks.TopDown, Episode: 1, Map: 1);

    private static void Log(string line) => TestContext.Current.TestOutputHelper?.WriteLine(line);

    /// <summary>A host and <paramref name="players"/> − 1 remote players on <paramref name="link"/>, in the lobby with their round trips known.</summary>
    private static LockstepHarness Lobby(int players, MemoryLinkOptions link, int seed, long frameUs = 16_667)
    {
        var setups = new List<PlayerSetup> { new() { FrameUs = frameUs } };
        for (int i = 1; i < players; i++)
            setups.Add(new PlayerSetup { Link = link, FrameUs = frameUs + (1_000 * i), PhaseUs = 3_000 * i });
        var h = new LockstepHarness(_coop, setups, (slot, tic) => LockstepHarness.Wander(slot, tic, fire: true), seed: seed);
        h.JoinAndReady();
        return h;
    }

    /// <summary>The host's rule at the start, as <see cref="NetSession"/> picks it.</summary>
    private static int StartDelay(LockstepHarness h)
    {
        NetDelayChoice? choice = null;
        h.RunUntil(() => (choice = NetOptions.ChooseDelay(h.Server, waited: false)) is not null, NetSession.RttWaitUs, "the round trips");
        Log(choice!.ToString());
        return choice.Delay;
    }

    /// <summary>Start the game and run until every player is in it.</summary>
    private static void Start(LockstepHarness h, int delay, bool adaptive = true)
    {
        h.Server.Start(delay, adaptive: adaptive);
        h.RunUntil(() => h.Players.TrueForAll(p => p.Client.State == NetClientState.InGame), 5_000_000, "everyone in the game");
    }

    /// <summary>Run <paramref name="us"/> of network time.</summary>
    private static void Run(LockstepHarness h, long us)
    {
        long end = h.Network.NowUs + us;
        h.RunUntil(() => h.Network.NowUs >= end, us + 1_000_000, "the time");
    }

    /// <summary>The stalls of every player that began in [<paramref name="fromUs"/>, <paramref name="toUs"/>) of its game, each player's held time summed apart.</summary>
    private static (int Count, double LongestMs, double WorstHeldMs) Stalls(LockstepHarness h, long fromUs, long toUs)
    {
        int count = 0;
        double longest = 0, worst = 0;
        foreach (HarnessPlayer p in h.Players)
        {
            var stalls = p.Stalls.Where(s => s.AtUs >= fromUs && s.AtUs < toUs).ToList();
            if (p.StallStartUs >= 0 && p.StallStartUs - p.GameStartUs >= fromUs && p.StallStartUs - p.GameStartUs < toUs)
                stalls.Add((p.StallStartUs - p.GameStartUs, p.LastFrameUs - p.StallStartUs));
            count += stalls.Count;
            if (stalls.Count > 0)
                longest = Math.Max(longest, stalls.Max(s => s.LengthUs) / 1000.0);
            worst = Math.Max(worst, stalls.Sum(s => s.LengthUs) / 1000.0);
        }
        return (count, longest, worst);
    }

    private static void LogChanges(LockstepHarness h, long gameUs)
    {
        foreach (NetDelayChange c in h.Server.DelayChanges)
            Log($"  {(c.AtUs - gameUs) / 1e6:F2} s: delay {c.From} -> {c.To} at tic {c.Tic}: {c.Why}");
    }

    /// <summary>
    /// T11.12b's <i>Done when</i>: the link goes from a 10 ms round trip to
    /// 120 ms mid-game: the world holds for at most a second (each player's
    /// stalls summed), then plays on without stalls at the raised delay, and
    /// the delay comes back down when the link does, without a stall on the
    /// way; every tic in sync (the harness compares each).
    /// </summary>
    [Theory]
    [InlineData(2, 1, 16_667)]
    [InlineData(2, 2, 16_667)]
    [InlineData(2, 3, 16_667)]
    [InlineData(4, 1, 16_667)]
    [InlineData(2, 1, 33_333)]
    public void The_delay_follows_a_link_that_slows_and_recovers(int players, int seed, long frameUs)
    {
        MemoryLinkOptions fast = NetLinks.Internet(10, jitterMs: 2), slow = NetLinks.Internet(120, jitterMs: 5);
        LockstepHarness h = Lobby(players, fast, seed, frameUs);
        int start = StartDelay(h);
        Start(h, start);
        long game = h.Network.NowUs;
        Run(h, 20_000_000);
        int calm = h.Server.Delay;
        Assert.True(calm <= start, $"delay {calm} on the 10 ms link, started at {start}");
        var (before, _, _) = Stalls(h, 1_000_000, long.MaxValue);
        Assert.Equal(0, before);

        foreach (HarnessPlayer p in h.Players.Skip(1))
            p.Transport.Options = slow;
        long slowAt = h.Network.NowUs - game;
        Run(h, 30_000_000);
        int raised = h.Server.Delay;
        var transition = Stalls(h, slowAt - 1_000_000, slowAt + 3_000_000);
        var slowAfter = Stalls(h, slowAt + 3_000_000, long.MaxValue);
        Log($"{players} players, seed {seed}, {1e6 / frameUs:F0} fps: start delay {start}, {calm} at 20 s; 10 -> 120 ms: delay {raised}, " +
            $"{transition.Count} stalls in the transition, the longest {transition.LongestMs:F0} ms, held at most {transition.WorstHeldMs:F0} ms; after it {slowAfter.Count}");
        Assert.True(raised >= NetDelayControl.Tics(120_000) + 1, $"delay {raised} at a 120 ms round trip");
        Assert.True(transition.WorstHeldMs <= 1000, $"held {transition.WorstHeldMs:F0} ms in the transition");
        Assert.Equal(0, slowAfter.Count);

        foreach (HarnessPlayer p in h.Players.Skip(1))
            p.Transport.Options = fast;
        long fastAt = h.Network.NowUs - game;
        Run(h, 40_000_000);
        var lowering = Stalls(h, fastAt, long.MaxValue);
        LogChanges(h, game);
        Log($"  back to 10 ms: delay {h.Server.Delay}, {lowering.Count} stalls");
        Assert.True(h.Server.Delay <= calm, $"delay {h.Server.Delay} back on the 10 ms link ({calm} before the slowdown, {start} at the start)");
        Assert.Equal(0, lowering.Count);
        Assert.Null(h.Server.Desync);
        Assert.All(h.Players, p => Assert.Equal(h.Server.Delay, p.Client.Delay));
    }

    /// <summary>
    /// <c>-netdelay</c> pins the delay: the same slowdown stalls on to the end
    /// at the delay the game started with, and no <c>Delay</c> goes.
    /// </summary>
    [Fact]
    public void A_fixed_delay_does_not_move()
    {
        LockstepHarness h = Lobby(2, NetLinks.Internet(10, jitterMs: 2), seed: 1);
        Start(h, 3, adaptive: false);
        Assert.False(h.Players[1].Client.Start!.Adaptive);
        Run(h, 5_000_000);
        h.Players[1].Transport.Options = NetLinks.Internet(120, jitterMs: 5);
        Run(h, 10_000_000);
        Assert.Equal(3, h.Server.Delay);
        Assert.Empty(h.Server.DelayChanges);
        Assert.All(h.Players, p => Assert.Equal(3, p.Client.Delay));
        var stalls = Stalls(h, 4_000_000, long.MaxValue);
        Log($"-netdelay 3 at 120 ms: {stalls.Count} stalls, the longest {stalls.LongestMs:F0} ms, held up to {stalls.WorstHeldMs:F0} ms in 10 s");
        Assert.True(stalls.WorstHeldMs > 2000, "a 120 ms link at delay 3 stalls on");
    }
    /// <summary>
    /// T11.12d's left-over case: home wifi (<see cref="NetLinks.Wifi"/>) with
    /// every machine at 30 fps, where the lobby's pings missed the bursts on
    /// seed 2 and the start's delay held 0.7 s a minute. With the delay fixed
    /// at the start's pick (before T11.12b) and adapting, a minute each;
    /// logged in SPEC §12 ("T11.12 measurements", after T11.12b).
    /// </summary>
    [Theory]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 3)]
    [InlineData(4, 1)]
    public void Home_wifi_at_30_fps(int players, int seed)
    {
        NetMeasurementTests.Measurement before = NetMeasurementTests.Measure(players, NetLinks.Wifi, seed, out LockstepHarness hb, frameUs: 33_333, adaptive: false);
        NetMeasurementTests.Measurement after = NetMeasurementTests.Measure(players, NetLinks.Wifi, seed, out LockstepHarness ha, frameUs: 33_333);
        Log($"{players} players, wifi at 30 fps, seed {seed}: fixed: {before}");
        Log($"  adaptive: {after}");
        LogChanges(ha, 0);
        StallStats late = StallStats.Worst(ha.Players, afterUs: 10_000_000);
        Log($"  adaptive, after the first 10 s: {late}");
        Assert.All(ha.Players, p => Assert.True(p.TicsRun >= 35 * 60));
        // no worse than the fixed delay, and once raised (the first bursts) no stall
        Assert.True(after.Stalls.TotalMs <= before.Stalls.TotalMs + 1, $"held {after.Stalls.TotalMs:F0} ms adapting, {before.Stalls.TotalMs:F0} fixed");
        Assert.Equal(0, late.Count);
        _ = hb;
    }
}
