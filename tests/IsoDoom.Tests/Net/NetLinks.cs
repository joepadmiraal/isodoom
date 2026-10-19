using System;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Net;

namespace IsoDoom.Tests.Net;

/// <summary>
/// T11.12e: the links <see cref="NetMeasurementTests"/> (and the tests of
/// T11.12a–d) put a remote player on. Each is one <see cref="MemoryLinkOptions"/>
/// for the remote end (the server's end is perfect, as in
/// <see cref="LockstepHarness"/>), so its latency, jitter, spikes and loss
/// apply to the packets both ways; vary one with <c>with</c>.
/// </summary>
public static class NetLinks
{
    /// <summary>
    /// An internet link: <paramref name="pingMs"/> round trip, 0–<paramref name="jitterMs"/>
    /// of jitter each way, <paramref name="loss"/> of the unreliable packets
    /// lost and half as many reliable ones resent (T11.12's cases).
    /// </summary>
    public static MemoryLinkOptions Internet(int pingMs, int jitterMs = 5, double loss = 0)
        => new() { LatencyUs = pingMs * 1000L / 2, JitterUs = jitterMs * 1000L, UnreliableLoss = loss, ReliableLoss = loss / 2 };

    /// <summary>
    /// Home wifi between two laptops with power saving on (T11.12's LAN test:
    /// pings of 5–100 ms): a round trip of 4–10 ms (2 ms each way and 0–3 ms
    /// of jitter), and bursts of spikes, one every 1–3 s lasting 0.2–0.6 s,
    /// in which every packet takes 25–75 ms more each way (a round trip of
    /// 50–150 ms more); 1 % of the packets lost both ways on the unreliable
    /// channel, and 1 % resent on the reliable one, arriving
    /// <see cref="MemoryLinkOptions.ResendUs"/> = 150 ms late (ENet's
    /// retransmit timeout, the smoothed round trip + 4 × its variance: about
    /// 20 + 4 × 30 ms on such a link) and holding back every reliable packet
    /// behind it.
    /// </summary>
    public static MemoryLinkOptions Wifi { get; } = new()
    {
        LatencyUs = 2_000,
        JitterUs = 3_000,
        BurstEveryUs = 2_000_000,
        BurstLengthUs = 400_000,
        SpikeMinUs = 25_000,
        SpikeMaxUs = 75_000,
        UnreliableLoss = 0.01,
        ReliableLoss = 0.01,
        ResendUs = 150_000,
    };
}

/// <summary>
/// T11.12e: the stalls a player saw (<see cref="HarnessPlayer.Stalls"/>):
/// how many, the longest and the time held in all, and the same a minute.
/// </summary>
/// <param name="Count">The stalls.</param>
/// <param name="LongestMs">The longest, in ms.</param>
/// <param name="TotalMs">The time held in all, in ms.</param>
/// <param name="Seconds">The game time they were counted over.</param>
public sealed record StallStats(int Count, double LongestMs, double TotalMs, double Seconds)
{
    /// <summary>Stalls a minute.</summary>
    public double CountPerMinute => Seconds > 0 ? Count * 60 / Seconds : 0;

    /// <summary>Time held a minute, in ms.</summary>
    public double HeldMsPerMinute => Seconds > 0 ? TotalMs * 60 / Seconds : 0;

    /// <summary>
    /// <paramref name="player"/>'s stalls that began <paramref name="afterUs"/>
    /// or later into its game (one still under way counted to its last frame).
    /// </summary>
    public static StallStats Of(HarnessPlayer player, long afterUs = 0)
    {
        var stalls = player.Stalls.Where(s => s.AtUs >= afterUs).Select(s => s.LengthUs).ToList();
        if (player.StallStartUs >= 0 && player.StallStartUs - player.GameStartUs >= afterUs)
            stalls.Add(player.LastFrameUs - player.StallStartUs);
        double seconds = player.GameStartUs < 0 ? 0 : Math.Max(0, player.LastFrameUs - player.GameStartUs - afterUs) / 1e6;
        return new StallStats(stalls.Count, stalls.Count > 0 ? stalls.Max() / 1000.0 : 0, stalls.Sum() / 1000.0, seconds);
    }

    /// <summary>The stalls of whichever of <paramref name="players"/> was held longest in all.</summary>
    public static StallStats Worst(IEnumerable<HarnessPlayer> players, long afterUs = 0)
        => players.Select(p => Of(p, afterUs)).OrderByDescending(s => s.TotalMs).ThenByDescending(s => s.Count).First();

    public override string ToString()
        => $"{CountPerMinute:F0} stalls a minute, longest {LongestMs:F0} ms, held {HeldMsPerMinute:F0} ms a minute";
}
