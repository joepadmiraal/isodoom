using System;
using System.Collections.Generic;

namespace IsoDoom.Net;

/// <summary>T11.12b: a change of the input delay the host decided: when, at which complete tic, from what to what, and why.</summary>
/// <param name="AtUs">The server's time, µs.</param>
/// <param name="Tic">The server's <c>CompleteTic</c> then.</param>
/// <param name="From">The delay before, in tics.</param>
/// <param name="To">The delay after, in tics.</param>
/// <param name="Why">The log's reason.</param>
public readonly record struct NetDelayChange(long AtUs, int Tic, int From, int To, string Why);

/// <summary>
/// T11.12b: the host's adaptive input delay (SPEC §12 "T11.12b The delay
/// adapts in the game"). Fed with each player's ping reports (the longest
/// delay a tic of its last second needed, and its longest gap between two
/// polls: <see cref="NetClient.NeededDelayUs"/>, <see cref="NetClient.LongestGapUs"/>)
/// and the round trips, it raises the delay once a player's tics held, and
/// lowers it a tic at a time after a calm spell.
/// <list type="bullet">
/// <item><b>Raise:</b> when a player's world held past its next frame (a
/// stall longer than one and a half of its gaps between polls: a single
/// frame's hold is the stutter of jitter, which the stall counts ignore too,
/// and which a delay raised for it would cost every tic) and a tic of the
/// last <see cref="RaiseWindowUs"/> needed more than the delay, to what it
/// needed (rounded up to a tic), but never past the round trips' rule
/// (<see cref="Rule"/>, the start's) + <see cref="RttBoundTics"/> (a link that
/// went silent for a while, whose tics needed its outage, not its latency)
/// nor <see cref="MaxDelay"/>; not within <see cref="HitchHoldUs"/> of a
/// player's hitch (a gap of <see cref="HitchUs"/> or more between two of its
/// polls: a level load, which held every tic and which no delay helps).</item>
/// <item><b>Lower:</b> by one tic, once <see cref="CalmUs"/> passed since the
/// last raise and <see cref="StepDownUs"/> since the last change, when every
/// player reported through the whole calm window and the most any tic there
/// needed, with a tic of headroom, is below the delay; never under
/// <see cref="MinDelay"/>.</item>
/// </list>
/// Plain arithmetic on the reports, no clock of its own; the sim sees none of it.
/// </summary>
public sealed class NetDelayControl
{
    /// <summary>Microseconds per tic.</summary>
    public const long TicUs = NetProtocol.MicrosecondsPerSecond / NetProtocol.TicRate;

    /// <summary>
    /// The frames' slack the start's rule allows on top of a round trip (T11.12a's
    /// <c>NetOptions.PollSlackUs</c>: two 60 Hz frames, or one at 30 fps: a
    /// command is made up to a frame after its time).
    /// </summary>
    public const long SlackUs = 2 * NetProtocol.MicrosecondsPerSecond / 60;

    /// <summary>The window of the reports a raise looks at (each report is itself its sender's last second).</summary>
    public const long RaiseWindowUs = 1_000_000;

    /// <summary>How long after the last raise the delay may come down.</summary>
    public const long CalmUs = 8_000_000;

    /// <summary>How long after any change a step down may follow.</summary>
    public const long StepDownUs = 2_000_000;

    /// <summary>A gap between two of a machine's polls this long or longer is a hitch of its own (a level load, a slow frame: under 10 fps).</summary>
    public const long HitchUs = 100_000;

    /// <summary>How long after a hitch report no raise follows (the reports of the second after it carry its held tics).</summary>
    public const long HitchHoldUs = 1_500_000;

    /// <summary>The least delay it lowers to: T11.12a's minimum (86 ms).</summary>
    public const int MinDelay = 3;

    /// <summary>The most it raises to: about half a second (past that the stalls are better than the lag).</summary>
    public const int MaxDelay = 17;

    /// <summary>A raise goes no further than the round trips' rule and this.</summary>
    public const int RttBoundTics = 2;

    private const int ReportRing = 160;

    private readonly long[,] _at = new long[NetProtocol.MaxPlayers, ReportRing];
    private readonly int[,] _need = new int[NetProtocol.MaxPlayers, ReportRing];
    private readonly int[] _count = new int[NetProtocol.MaxPlayers];
    private readonly List<NetDelayChange> _changes = [];
    private long _lastRaiseUs;
    private long _lastChangeUs;
    private long _lastHitchUs = long.MinValue;
    private long _lastHeldUs = long.MinValue;

    /// <summary>The delay now, in tics.</summary>
    public int Delay { get; private set; }

    /// <summary>The changes so far, oldest first.</summary>
    public IReadOnlyList<NetDelayChange> Changes => _changes;

    /// <summary><paramref name="us"/> in tics, rounded up.</summary>
    public static int Tics(long us) => (int)((Math.Max(0, us) + TicUs - 1) / TicUs);

    /// <summary>
    /// The start's rule (T11.12a's default delay, unclamped) for a round trip
    /// of <paramref name="rttUs"/>: ⌈(RTT + <see cref="SlackUs"/>) / tic⌉ + 1.
    /// </summary>
    public static int Rule(long rttUs) => Tics(rttUs + SlackUs) + 1;

    /// <summary>Starts the game's control at <paramref name="delay"/> (the Start's), at <paramref name="nowUs"/>.</summary>
    public void Begin(int delay, long nowUs)
    {
        Delay = delay;
        _lastRaiseUs = _lastChangeUs = nowUs;
        _lastHitchUs = _lastHeldUs = long.MinValue;
        Array.Clear(_count);
        _changes.Clear();
    }

    /// <summary>
    /// A ping's report from <paramref name="slot"/> at <paramref name="nowUs"/>:
    /// the longest delay a tic of its last second needed (<paramref name="needUs"/>, −1 none),
    /// its longest stall (<paramref name="stallUs"/>) and longest gap between
    /// two polls (<paramref name="gapUs"/>: about its frame) of its last second.
    /// </summary>
    public void Report(int slot, long nowUs, int needUs, int stallUs, int gapUs)
    {
        if (gapUs >= HitchUs)
            _lastHitchUs = nowUs;
        else if (stallUs * 2L > gapUs * 3L)
            _lastHeldUs = nowUs; // held past its next frame: a stall the player sees (one frame's is jitter's stutter)
        if (needUs < 0)
            return;
        int i = _count[slot]++ % ReportRing;
        _at[slot, i] = nowUs;
        _need[slot, i] = needUs;
    }

    /// <summary>
    /// The decision at <paramref name="nowUs"/> for the players in
    /// <paramref name="playing"/> (bit i: slot i), whose longest round trips
    /// lately are <paramref name="rttUs"/> (by slot, −1 unknown): the new
    /// delay, or null to keep it. <paramref name="tic"/> is the server's for the record.
    /// </summary>
    public NetDelayChange? Decide(long nowUs, byte playing, ReadOnlySpan<int> rttUs, int tic)
    {
        if (playing == 0)
            return null;
        // raise: a player held past a frame lately, and a tic of the last second needed more
        if (_lastHeldUs != long.MinValue && nowUs - _lastHeldUs < RaiseWindowUs
            && (_lastHitchUs == long.MinValue || nowUs - _lastHitchUs >= HitchHoldUs))
        {
            (int slot, int need) = Longest(playing, nowUs - RaiseWindowUs, nowUs, out _);
            if (need >= 0 && Tics(need) > Delay)
            {
                int rtt = -1;
                for (int s = 0; s < NetProtocol.MaxPlayers; s++)
                {
                    if ((playing & (1 << s)) != 0)
                        rtt = Math.Max(rtt, rttUs[s]);
                }
                int target = Math.Min(Tics(need), MaxDelay);
                if (rtt >= 0)
                    target = Math.Min(target, Rule(rtt) + RttBoundTics);
                if (target > Delay)
                {
                    string why = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                        $"player {slot + 1}'s tics needed up to {need / 1000.0:F0} ms, round trips up to {Math.Max(0, rtt) / 1000.0:F0} ms");
                    _lastRaiseUs = nowUs;
                    return Change(nowUs, tic, target, why);
                }
            }
        }
        // lower: a tic at a time, after a calm spell every player reported through
        if (Delay > MinDelay && nowUs - _lastRaiseUs >= CalmUs && nowUs - _lastChangeUs >= StepDownUs)
        {
            (int slot, int need) = Longest(playing, nowUs - CalmUs, nowUs, out bool covered);
            if (covered && need >= 0 && Tics(need) + 1 < Delay)
            {
                string why = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"the tics needed up to {need / 1000.0:F0} ms (player {slot + 1}) for {CalmUs / 1_000_000} s");
                return Change(nowUs, tic, Delay - 1, why);
            }
        }
        return null;
    }

    /// <summary>
    /// T11.12b: the delay set to <paramref name="delay"/> from outside (the
    /// level script's <c>netdelay</c>, a test of the change in a real game), as a raise: the calm spell starts again.
    /// </summary>
    public NetDelayChange Set(long nowUs, int tic, int delay, string why)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(delay, NetProtocol.MaxDelay);
        _lastRaiseUs = nowUs;
        return Change(nowUs, tic, delay, why);
    }

    private NetDelayChange Change(long nowUs, int tic, int delay, string why)
    {
        var change = new NetDelayChange(nowUs, tic, Delay, delay, why);
        _changes.Add(change);
        Delay = delay;
        _lastChangeUs = nowUs;
        return change;
    }

    /// <summary>
    /// The longest need reported in [<paramref name="fromUs"/>, <paramref name="toUs"/>]
    /// by the players in <paramref name="playing"/>, and whose (−1, −1: none);
    /// <paramref name="covered"/>: every one of them reported through the
    /// window (a report in its first second, and one a fifth of a second on average).
    /// </summary>
    private (int Slot, int Wait) Longest(byte playing, long fromUs, long toUs, out bool covered)
    {
        covered = true;
        int bestSlot = -1, best = -1;
        long need = (toUs - fromUs) / NetClient.PingIntervalUs / 2;
        for (int s = 0; s < NetProtocol.MaxPlayers; s++)
        {
            if ((playing & (1 << s)) == 0)
                continue;
            int n = Math.Min(_count[s], ReportRing), inWindow = 0;
            long oldest = long.MaxValue;
            for (int k = 0; k < n; k++)
            {
                int i = (_count[s] - 1 - k) % ReportRing;
                long at = _at[s, i];
                if (at < fromUs)
                    break;
                if (at > toUs)
                    continue;
                inWindow++;
                oldest = at;
                if (_need[s, i] > best)
                {
                    best = _need[s, i];
                    bestSlot = s;
                }
            }
            if (inWindow < need || oldest > fromUs + NetProtocol.MicrosecondsPerSecond)
                covered = false;
        }
        return (bestSlot, best);
    }
}
