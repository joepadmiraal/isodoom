using IsoDoom.Net;
using Xunit;

namespace IsoDoom.Tests.Net;

/// <summary>
/// T11.12b: <see cref="NetDelayControl"/>'s rules on reports made by hand
/// (two players, a report from each every 100 ms as the pings bring them).
/// </summary>
public class NetDelayControlTests
{
    private const byte Two = 0b11;
    private const long Ping = NetClient.PingIntervalUs;
    private const int Frame = 16_667;
    private static readonly int[] _rtt = [20_000, 20_000, -1, -1];

    private static long Tic(int tics) => tics * NetDelayControl.TicUs;

    /// <summary>Reports from both players every ping from <paramref name="fromUs"/> for <paramref name="us"/>, deciding after each; the last change or null.</summary>
    private static NetDelayChange? Run(NetDelayControl c, long fromUs, long us, int needUs, int stallUs = 0, int gapUs = Frame, int[]? rtt = null)
    {
        NetDelayChange? last = null;
        for (long t = fromUs; t < fromUs + us; t += Ping)
        {
            c.Report(0, t, needUs, stallUs, gapUs);
            c.Report(1, t, needUs, stallUs, gapUs);
            last = c.Decide(t, Two, rtt ?? _rtt, (int)(t / NetDelayControl.TicUs)) ?? last;
        }
        return last;
    }

    [Fact]
    public void A_stall_past_a_frame_raises_to_what_the_tics_needed()
    {
        var c = new NetDelayControl();
        c.Begin(4, 0);
        // held a frame only: jitter's stutter, no raise
        Assert.Null(Run(c, 0, 1_000_000, (int)Tic(5), stallUs: Frame, rtt: [150_000, 150_000, -1, -1]));
        // held two frames, a tic needed 6.5 tics: 7
        NetDelayChange? up = Run(c, 1_000_000, 200_000, (int)Tic(6) + 14_000, stallUs: 2 * Frame, rtt: [150_000, 150_000, -1, -1]);
        Assert.Equal((4, 7), (up!.Value.From, up.Value.To));
        Assert.Equal(7, c.Delay);
    }

    [Fact]
    public void The_round_trips_bound_a_raise_after_an_outage()
    {
        var c = new NetDelayControl();
        c.Begin(3, 0);
        // a 2 s outage on a 20 ms link: the tics needed 2 s, the round trips' rule + 2 is the most
        Run(c, 0, 300_000, 2_000_000, stallUs: 2_000_000);
        Assert.Equal(NetDelayControl.Rule(20_000) + NetDelayControl.RttBoundTics, c.Delay);
        Assert.True(c.Delay < NetDelayControl.MaxDelay);
    }

    [Fact]
    public void A_hitch_holds_the_raises_for_a_while()
    {
        var c = new NetDelayControl();
        c.Begin(3, 0);
        c.Report(0, 0, (int)Tic(3), 0, 400_000); // a level load: a gap of 400 ms
        Assert.Null(Run(c, 100_000, NetDelayControl.HitchHoldUs - 200_000, (int)Tic(6), stallUs: 300_000, rtt: [150_000, 150_000, -1, -1]));
        Assert.NotNull(Run(c, NetDelayControl.HitchHoldUs, 300_000, (int)Tic(6), stallUs: 300_000, rtt: [150_000, 150_000, -1, -1]));
    }

    [Fact]
    public void It_comes_down_a_tic_at_a_time_after_a_calm_spell_and_not_below_the_least()
    {
        var c = new NetDelayControl();
        c.Begin(8, 0);
        // the calm spell first: nothing for 8 s
        Assert.Null(Run(c, 0, NetDelayControl.CalmUs - Ping, (int)Tic(1)));
        NetDelayChange? down = Run(c, NetDelayControl.CalmUs, Ping, (int)Tic(1));
        Assert.Equal((8, 7), (down!.Value.From, down.Value.To));
        // then a step each StepDownUs, to MinDelay
        Run(c, NetDelayControl.CalmUs + Ping, 20_000_000, (int)Tic(1));
        Assert.Equal(NetDelayControl.MinDelay, c.Delay);
        Assert.Equal(8 - NetDelayControl.MinDelay, c.Changes.Count);
        for (int i = 1; i < c.Changes.Count; i++)
            Assert.True(c.Changes[i].AtUs - c.Changes[i - 1].AtUs >= NetDelayControl.StepDownUs);
    }

    [Fact]
    public void It_stays_while_the_tics_need_within_a_tic_of_it_or_a_player_is_silent()
    {
        var c = new NetDelayControl();
        c.Begin(6, 0);
        // the tics needed 5 tics: 6 is the least with a tic of headroom
        Assert.Null(Run(c, 0, 20_000_000, (int)Tic(5)));
        // a player whose pings stopped coming: no lowering on half the story
        var d = new NetDelayControl();
        d.Begin(6, 0);
        for (long t = 0; t < 20_000_000; t += Ping)
        {
            d.Report(0, t, (int)Tic(1), 0, Frame);
            Assert.Null(d.Decide(t, Two, _rtt, 0));
        }
    }
}
