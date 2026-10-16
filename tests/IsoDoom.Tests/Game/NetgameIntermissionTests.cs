using System.Collections.Generic;
using System.Linq;
using IsoDoom.Game;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Game;

/// <summary>
/// T11.7: wi_stuff.c's net game and deathmatch stats (<see cref="WiStuff"/>)
/// without a WAD: the counting's timing and sounds, <c>dofrags</c>,
/// <c>WI_fragSum</c>, the deathmatch frag table, the skips and the drawing's
/// stand-ins. Against vanilla, tic by tic and pixel by pixel, the co-op exit
/// routes' intermissions are <see cref="IntermissionTests.MatchesVanilla"/>'s
/// (<c>coop-e1m1-exit-2p</c>, <c>coop-e1m1-exit-4p</c>); a deathmatch's waits
/// for T11.4's routes (T11.7a).
/// </summary>
public class NetgameIntermissionTests
{
    private static readonly WadArchive _synthetic = new([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);

    /// <summary>A world of the players in <paramref name="mask"/> (no level: the intermission reads only who is in the game).</summary>
    private static World NetWorld(int mask, int deathmatch = 0) =>
        new(new SpawnSettings(GameMode.shareware, skill_t.sk_medium, netgame: true, deathmatch: deathmatch, playersingame: mask), Tweaks.Vanilla);

    /// <summary>E1M1's end into E1M2, with each player's (kills, items, secrets) and frags.</summary>
    private static wbstartstruct_t Wbs(int maxkills, int maxitems, int maxsecret, int pnum, params (int K, int I, int S, int[] Frags)[] plyr)
    {
        var w = new wbstartstruct_t { epsd = 0, last = 0, next = 1, maxkills = maxkills, maxitems = maxitems, maxsecret = maxsecret, partime = 30 * SimInfo.TICRATE, pnum = pnum };
        for (int i = 0; i < World.MAXPLAYERS; i++)
        {
            (int k, int it, int s, int[] frags) = i < plyr.Length ? plyr[i] : (0, 0, 0, new int[World.MAXPLAYERS]);
            w.plyr[i] = new wbplayerstruct_t { skills = k, sitems = it, ssecret = s, stime = 100 * SimInfo.TICRATE, frags = frags };
        }
        return w;
    }

    private static int[] Frags(params int[] f) => [.. f, .. new int[World.MAXPLAYERS - f.Length]];

    /// <summary>Ticks the intermission until <paramref name="done"/>, logging each tic's sounds as BCNT:SOUND+SOUND.</summary>
    private static List<string> Run(WiStuff wi, System.Func<bool> done)
    {
        var log = new List<string>();
        for (int t = 0; !done(); t++)
        {
            Assert.True(t < 2000);
            wi.WI_Ticker();
            if (wi.sounds.Count > 0)
                log.Add($"{wi.bcnt}:{string.Join('+', wi.sounds.Select(s => s.ToString()[4..]))}");
        }
        return log;
    }

    private static string Pistols(int from, int to) => string.Join(' ', Enumerable.Range(from, to - from + 1).Where(b => (b & 3) == 0).Select(b => $"{b}:pistol"));

    /// <summary>
    /// The net game stats: every player in the game (here 1, 2 and 4) counts
    /// up together, 2% a tic from 0, a stat done when the last player's is,
    /// a second's pause between stats (<c>cnt_pause</c>, reset only after a
    /// pause), a pistol shot every 4 tics and an explosion as each ends; the
    /// frags one a tic to <c>WI_fragSum</c> (frags of the others less
    /// suicides: −1 for player 4), ended by a death cry; a press goes on.
    /// </summary>
    [Fact]
    public void TheNetGameStatsCountUpTogetherWithVanillasTimingAndSounds()
    {
        (GameFlow flow, _) = GameFlowTests.New();
        World world = NetWorld(0b1011);
        WiStuff wi = flow.Wi;
        wi.WI_Start(Wbs(10, 4, 2, 0,
            (5, 1, 2, Frags()), // 50% 25% 100%
            (2, 4, 0, Frags(0, 0, 0, 2)), // 20% 100% 0%, killed player 4 twice
            (9, 9, 9, Frags(9, 9, 9, 9)), // not in the game
            (3, 0, 1, Frags(0, 0, 0, 1))), world); // 30% 0% 50%, killed itself once
        Assert.Equal((WiStuff.stateenum_t.StatCount, 1, 1), (wi.state, wi.ng_state, wi.dofrags));
        Assert.Equal((0, 2, -1), (wi.WI_fragSum(0), wi.WI_fragSum(1), wi.WI_fragSum(3)));

        List<string> log = Run(wi, () => wi.StatsShown);
        // kills from bcnt 36 (2, 4 … 50 at 60), items from 96 (100% at 145), secrets from 181 (100% at 230), frags from 266 (2 at 267)
        Assert.Equal(string.Join(' ', Pistols(36, 59), "60:pistol+barexp", Pistols(96, 144), "145:barexp", Pistols(181, 229), "230:barexp", "267:pldeth"),
            string.Join(' ', log));
        Assert.Equal(302, wi.bcnt); // the last pause: 268 … 302
        Assert.Equal([50, 20, 0, 30], wi.cnt_kills);
        Assert.Equal([25, 100, 0, 0], wi.cnt_items);
        Assert.Equal([100, 0, 0, 50], wi.cnt_secret);
        Assert.Equal([0, 2, 0, -1], wi.cnt_frags);
        Assert.Contains("p4 kills 30% items 0% secret 50% frags -1", wi.StateText(), System.StringComparison.Ordinal);

        world.players[3].cmd.buttons = buttoncode_t.BT_USE; // any player's press
        wi.WI_Ticker();
        Assert.Equal((WiStuff.stateenum_t.ShowNextLoc, sfxenum_t.sfx_sgcock), (wi.state, wi.sounds.Single()));
    }

    /// <summary>Without frags the frags' stage is skipped (secrets' end: 6 → 9, the pause, 10); a press shows every count at once.</summary>
    [Fact]
    public void WithoutFragsTheirStageIsSkippedAndAPressShowsAll()
    {
        (GameFlow flow, _) = GameFlowTests.New();
        World world = NetWorld(0b11);
        WiStuff wi = flow.Wi;
        wi.WI_Start(Wbs(4, 1, 1, 1, (1, 1, 0, Frags()), (3, 0, 1, Frags())), world);
        Assert.Equal(0, wi.dofrags);
        List<string> log = Run(wi, () => wi.ng_state == 9);
        Assert.Equal("barexp", log[^1].Split(':')[1]);
        Assert.DoesNotContain(log, l => l.Contains("pldeth", System.StringComparison.Ordinal));

        wi.WI_Start(Wbs(4, 1, 1, 1, (1, 1, 0, Frags()), (3, 0, 1, Frags())), world);
        wi.WI_Ticker();
        world.players[0].cmd.buttons = buttoncode_t.BT_ATTACK;
        wi.WI_Ticker();
        Assert.Equal((10, sfxenum_t.sfx_barexp), (wi.ng_state, wi.sounds.Single()));
        Assert.Equal((25, 100, 0), (wi.cnt_kills[0], wi.cnt_items[0], wi.cnt_secret[0]));
        Assert.Equal((75, 0, 100), (wi.cnt_kills[1], wi.cnt_items[1], wi.cnt_secret[1]));
    }

    /// <summary>
    /// <c>dofrags</c> is vanilla's static, added to and never cleared: once
    /// an intermission showed the frags, every later one does, with or
    /// without frags.
    /// </summary>
    [Fact]
    public void TheFragsColumnStaysOnceShown()
    {
        (GameFlow flow, _) = GameFlowTests.New();
        World world = NetWorld(0b11);
        WiStuff wi = flow.Wi;
        wi.WI_Start(Wbs(1, 1, 1, 0, (0, 0, 0, Frags(0, 1)), (0, 0, 0, Frags())), world);
        Assert.Equal(1, wi.dofrags);
        wi.WI_Start(Wbs(1, 1, 1, 0, (0, 0, 0, Frags()), (0, 0, 0, Frags())), world);
        Assert.Equal(1, wi.dofrags);
        Run(wi, () => wi.StatsShown);
        Assert.Equal([0, 0], wi.cnt_frags.Take(2));
    }

    /// <summary>
    /// The deathmatch frag table: after a second's pause each frag of a
    /// player in the game counts one a tic towards its value, the totals
    /// (<c>WI_fragSum</c>, from the final frags) at once, a pistol shot every
    /// 4 tics and an explosion on the tic none moved; a press goes on with
    /// <c>sfx_slop</c>.
    /// </summary>
    [Fact]
    public void TheDeathmatchTableCountsUpWithVanillasTimingAndSounds()
    {
        (GameFlow flow, _) = GameFlowTests.New();
        World world = NetWorld(0b101, deathmatch: 1);
        WiStuff wi = flow.Wi;
        wi.WI_Start(Wbs(1, 1, 1, 0, (0, 0, 0, Frags(0, 7, 3)), (0, 0, 0, Frags(5, 5, 5)), (0, 0, 0, Frags(1, 0, 2))), world);
        Assert.Equal((WiStuff.stateenum_t.StatCount, 1), (wi.state, wi.dm_state));

        List<string> log = Run(wi, () => wi.dm_state == 3);
        // from bcnt 36: [0,2] 1 … 3 at 38, [2,0] 1 at 36, [2,2] 1, 2 at 37; at 39 none moved
        Assert.Equal("36:pistol 39:barexp", string.Join(' ', log));
        Assert.Equal((0, 3, 1, 2), (wi.dm_frags[0, 0], wi.dm_frags[0, 2], wi.dm_frags[2, 0], wi.dm_frags[2, 2]));
        Assert.Equal((0, 0), (wi.dm_frags[0, 1], wi.dm_frags[1, 1])); // player 2 is not in the game
        Assert.Equal((3, -1), (wi.dm_totals[0], wi.dm_totals[2]));

        Run(wi, () => wi.StatsShown);
        Assert.Equal(74, wi.bcnt); // the pause: 40 … 74
        world.players[0].cmd.buttons = buttoncode_t.BT_USE;
        wi.WI_Ticker();
        Assert.Equal((WiStuff.stateenum_t.ShowNextLoc, sfxenum_t.sfx_slop), (wi.state, wi.sounds.Single()));
    }

    /// <summary>
    /// Vanilla's clamp: a frag count over 99 stops at 99 but never matches,
    /// so the table keeps "counting" (a pistol shot every 4 tics) until a
    /// press; the press shows the true count (vanilla's).
    /// </summary>
    [Fact]
    public void AFragCountOver99CountsOnUntilAPress()
    {
        (GameFlow flow, _) = GameFlowTests.New();
        World world = NetWorld(0b11, deathmatch: 2);
        WiStuff wi = flow.Wi;
        wi.WI_Start(Wbs(1, 1, 1, 0, (0, 0, 0, Frags(0, 120)), (0, 0, 0, Frags())), world);
        List<string> log = Run(wi, () => wi.bcnt == 500);
        Assert.Equal((2, 99, 99), (wi.dm_state, wi.dm_frags[0, 1], wi.dm_totals[0]));
        Assert.All(log, l => Assert.EndsWith(":pistol", l));
        world.players[1].cmd.buttons = buttoncode_t.BT_USE;
        wi.WI_Ticker();
        Assert.Equal((4, 120, 120, sfxenum_t.sfx_barexp), (wi.dm_state, wi.dm_frags[0, 1], wi.dm_totals[0], wi.sounds.Single()));
    }

    /// <summary>The synthetic IWAD lacks the <c>WI*</c> and <c>STPB*</c> graphics: both screens draw their stand-ins without throwing.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void DrawsWithoutTheGraphics(int deathmatch)
    {
        (GameFlow flow, _) = GameFlowTests.New();
        World world = NetWorld(0b1111, deathmatch);
        WiStuff wi = flow.Wi;
        wi.WI_Start(Wbs(4, 4, 4, 2, (1, 2, 3, Frags(0, 1)), (4, 3, 2, Frags(1)), (2, 2, 2, Frags()), (0, 0, 0, Frags(0, 0, 0, 1))), world);
        var g = new ScreenGraphics(_synthetic, new HuStuff(_synthetic));
        var screen = new HudScreen(0, HudScreen.SCREENHEIGHT);
        for (int t = 0; t < 400; t++)
        {
            wi.WI_Ticker();
            wi.WI_Drawer(g, screen);
        }
        Assert.Equal(WiStuff.stateenum_t.StatCount, wi.state);
        Assert.Contains(screen.Opaque, o => o != 0);
    }

    /// <summary>
    /// The console player (<c>wbs->pnum</c>, per machine, not sim state) is
    /// the one marked: DOOM1.WAD's net game screen with <c>STFST01</c> over
    /// player 1's <c>STPB0</c>, or player 2's, differ only in those rows.
    /// </summary>
    [Fact]
    public void TheConsolePlayerIsMarked()
    {
        var wad = new WadArchive([WadFile.Open(TestWads.RequireDoom1())]);
        var g = new ScreenGraphics(wad, null);
        byte[] Draw(int pnum)
        {
            (GameFlow flow, _) = GameFlowTests.New();
            World world = NetWorld(0b11);
            flow.Wi.WI_Start(Wbs(1, 1, 1, pnum, (0, 0, 0, Frags()), (0, 0, 0, Frags())), world);
            flow.Wi.WI_Ticker();
            var screen = new HudScreen(0, HudScreen.SCREENHEIGHT);
            flow.Wi.WI_Drawer(g, screen);
            return [.. screen.Pixels];
        }
        byte[] a = Draw(0), b = Draw(1);
        int[] rows = [.. Enumerable.Range(0, a.Length).Where(i => a[i] != b[i]).Select(i => i / HudScreen.SCREENWIDTH).Distinct()];
        Assert.NotEmpty(rows);
        int top = 50 + g.Patch("WIOSTK")!.Height; // NG_STATSY + the kills patch: player 1's row; player 2's 33 lower
        Assert.All(rows, y => Assert.InRange(y, top, top + 33 + g.Patch("STPB1")!.Height - 1));
        Assert.Contains(rows, y => y < top + 33);
        Assert.Contains(rows, y => y >= top + 33);
    }
}
