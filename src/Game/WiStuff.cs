using System.Collections.Generic;
using System.Linq;
using IsoDoom.Sim;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

/// <summary>
/// wi_stuff.c's intermission (T7.4, SPEC §7.6; T11.7: the net game's and
/// deathmatch's stats too, <see cref="WI_updateNetgameStats"/>,
/// <see cref="WI_updateDeathmatchStats"/>): the stats counted up (<see cref="sp_state"/>,
/// <see cref="cnt_pause"/>, kills, items and secrets in %, the time and the
/// par), the world map's animations (<see cref="WI_updateAnimatedBack"/>),
/// the splats on the levels done and the flashing "you are here" pointer
/// (<see cref="stateenum_t.ShowNextLoc"/>), the level names, and the sounds
/// (<c>sfx_pistol</c>, <c>sfx_barexp</c>, <c>sfx_sgcock</c>: to
/// <see cref="GameFlow.S_StartSound"/>). Fire or use skip (<see cref="WI_checkForAccelerate"/>).
/// <c>M_Random</c> is the presentation's (<see cref="GameFlow.MRandom"/>).
/// Drawn into vanilla's 320×200 screen with the WAD's <c>WI*</c> patches; a
/// WAD without them (the synthetic IWAD) gets the message font's stand-ins.
/// No Godot types: the tests link it.
/// </summary>
public sealed class WiStuff
{
    private readonly GameFlow flow;

    public WiStuff(GameFlow flow) => this.flow = flow;

    /// <summary>wi_stuff.h <c>stateenum_t</c>.</summary>
    public enum stateenum_t
    {
        NoState = -1,
        StatCount,
        ShowNextLoc,
    }

    /// <summary>wi_stuff.c <c>animenum_t</c>.</summary>
    public enum animenum_t
    {
        ANIM_ALWAYS,
        ANIM_RANDOM,
        ANIM_LEVEL,
    }

    /// <summary>wi_stuff.c <c>anim_t</c>: an animation of the world map (another <c>anim_t</c> is p_spec.c's).</summary>
    public sealed class anim_t
    {
        public animenum_t type;
        /// <summary>Period in tics between animations.</summary>
        public int period;
        /// <summary>Number of animation frames.</summary>
        public int nanims;
        /// <summary>Location of animation.</summary>
        public int x, y;
        /// <summary>ALWAYS: n/a; RANDOM: period deviation (&lt;256); LEVEL: level.</summary>
        public int data1;
        /// <summary>ALWAYS: n/a; RANDOM: random base period; LEVEL: n/a.</summary>
        public int data2;
        /// <summary>The frames' patch names (<c>WIAxyyzz</c>).</summary>
        public readonly string?[] p = new string?[3];
        /// <summary>Next value of <see cref="bcnt"/> (used in conjunction with period).</summary>
        public int nexttic;
        /// <summary>Next frame number to animate (−1: none drawn).</summary>
        public int ctr;
    }

    // wi_stuff.c's ANIM(type, period, nanims, x, y, nexttic): the last goes to data1 (the level of ANIM_LEVEL)
    private static anim_t ANIM(animenum_t type, int period, int nanims, int x, int y, int data1) =>
        new() { type = type, period = period, nanims = nanims, x = x, y = y, data1 = data1 };

    /// <summary>wi_stuff.c <c>epsd0animinfo</c>, <c>epsd1animinfo</c>, <c>epsd2animinfo</c> (<c>anims</c>, a fresh copy per intermission).</summary>
    private static anim_t[][] NewAnims()
    {
        const int T3 = SimInfo.TICRATE / 3, T4 = SimInfo.TICRATE / 4;
        animenum_t a = animenum_t.ANIM_ALWAYS;
        animenum_t l = animenum_t.ANIM_LEVEL;
        return
        [
            [
                ANIM(a, T3, 3, 224, 104, 0), ANIM(a, T3, 3, 184, 160, 0), ANIM(a, T3, 3, 112, 136, 0),
                ANIM(a, T3, 3, 72, 112, 0), ANIM(a, T3, 3, 88, 96, 0), ANIM(a, T3, 3, 64, 48, 0),
                ANIM(a, T3, 3, 192, 40, 0), ANIM(a, T3, 3, 136, 16, 0), ANIM(a, T3, 3, 80, 16, 0),
                ANIM(a, T3, 3, 64, 24, 0),
            ],
            [
                ANIM(l, T3, 1, 128, 136, 1), ANIM(l, T3, 1, 128, 136, 2), ANIM(l, T3, 1, 128, 136, 3),
                ANIM(l, T3, 1, 128, 136, 4), ANIM(l, T3, 1, 128, 136, 5), ANIM(l, T3, 1, 128, 136, 6),
                ANIM(l, T3, 1, 128, 136, 7), ANIM(l, T3, 3, 192, 144, 8), ANIM(l, T3, 1, 128, 136, 8),
            ],
            [
                ANIM(a, T3, 3, 104, 168, 0), ANIM(a, T3, 3, 40, 136, 0), ANIM(a, T3, 3, 160, 96, 0),
                ANIM(a, T3, 3, 104, 80, 0), ANIM(a, T3, 3, 120, 32, 0), ANIM(a, T4, 3, 40, 0, 0),
            ],
        ];
    }

    /// <summary>wi_stuff.c <c>lnodes</c>: the levels' places on the world maps of episodes 1–3, by episode and map (origin 0).</summary>
    private static readonly (int x, int y)[][] lnodes =
    [
        // Episode 0 World Map
        [(185, 164), (148, 143), (69, 122), (209, 102), (116, 89), (166, 55), (71, 56), (135, 29), (71, 24)],
        // Episode 1 World Map should go here
        [(254, 25), (97, 50), (188, 64), (128, 78), (214, 92), (133, 130), (208, 136), (148, 140), (235, 158)],
        // Episode 2 World Map should go here
        [(156, 168), (48, 154), (174, 95), (265, 75), (130, 48), (279, 23), (198, 48), (140, 25), (281, 136)],
    ];

    // GLOBAL LOCATIONS
    private const int WI_TITLEY = 2;

    private const int WI_SPACINGY = 33;

    // SINGPLE-PLAYER STUFF
    private const int SP_STATSX = 50, SP_STATSY = 50;
    private const int SP_TIMEX = 16, SP_TIMEY = HudScreen.SCREENHEIGHT - 32;

    // NET GAME STUFF (NG_STATSX: NetgameStatsX)
    private const int NG_STATSY = 50;
    private const int NG_SPACINGX = 64;

    // DEATHMATCH STUFF
    private const int DM_MATRIXX = 42, DM_MATRIXY = 68;
    private const int DM_SPACINGX = 40;
    private const int DM_TOTALSX = 269;
    private const int DM_KILLERSX = 10, DM_KILLERSY = 100;
    private const int DM_VICTIMSX = 5, DM_VICTIMSY = 50;

    /// <summary>wi_stuff.c <c>SHOWNEXTLOCDELAY</c>: seconds the next location shows.</summary>
    public const int SHOWNEXTLOCDELAY = 4;

    /// <summary>wi_stuff.c <c>NUMMAPS</c>: maps per episode before Doom II.</summary>
    private const int NUMMAPS = 9;

    /// <summary>wi_stuff.c <c>NUMCMAPS</c>: Doom II's level names (<c>CWILV00</c>–<c>CWILV31</c>).</summary>
    private const int NUMCMAPS = 32;

    /// <summary>wi_stuff.c <c>state</c>: the intermission's stage.</summary>
    public stateenum_t state = stateenum_t.NoState;

    /// <summary>wi_stuff.c <c>wbs</c>: the level's stats and the next level (<see cref="World.wminfo"/>).</summary>
    public wbstartstruct_t? wbs;

    /// <summary>wi_stuff.c <c>plrs</c>: <see cref="wbs"/>'s players.</summary>
    public wbplayerstruct_t[] plrs => wbs!.plyr;

    /// <summary>wi_stuff.c <c>me</c>: the console player.</summary>
    public int me;

    /// <summary>wi_stuff.c <c>acceleratestage</c>: fire or use was pressed (skip to the next stage).</summary>
    public int acceleratestage;

    /// <summary>wi_stuff.c <c>cnt</c>: the tics the stage still lasts (<see cref="stateenum_t.ShowNextLoc"/>, <see cref="stateenum_t.NoState"/>).</summary>
    public int cnt;

    /// <summary>wi_stuff.c <c>bcnt</c>: the intermission's tics (the background animation's timing).</summary>
    public int bcnt;

    /// <summary>wi_stuff.c <c>sp_state</c>: the single player stats' stage (odd: a pause; 2 kills, 4 items, 6 secrets, 8 time and par counting; 10 done).</summary>
    public int sp_state;

    /// <summary>
    /// wi_stuff.c <c>cnt_kills</c>, <c>cnt_items</c>, <c>cnt_secret</c>, by
    /// player, in % (a single player's in [0], −1 before counting; T11.7: a
    /// net game's of each player in the game, from 0).
    /// </summary>
    public readonly int[] cnt_kills = new int[World.MAXPLAYERS], cnt_items = new int[World.MAXPLAYERS], cnt_secret = new int[World.MAXPLAYERS];

    /// <summary>wi_stuff.c <c>cnt_time</c>, <c>cnt_par</c> (seconds), <c>cnt_pause</c> (tics).</summary>
    public int cnt_time, cnt_par, cnt_pause;

    /// <summary>wi_stuff.c <c>ng_state</c> (T11.7): the net game stats' stage (odd: a pause; 2 kills, 4 items, 6 secrets, 8 frags; 10 done).</summary>
    public int ng_state;

    /// <summary>wi_stuff.c <c>cnt_frags</c> (T11.7): each player's frags counted so far in a net game.</summary>
    public readonly int[] cnt_frags = new int[World.MAXPLAYERS];

    /// <summary>
    /// wi_stuff.c <c>dofrags</c> (T11.7): whether the net game stats show the
    /// frags (some player's <see cref="WI_fragSum"/> is not 0). As vanilla's
    /// static it is never reset: once a net game intermission showed frags,
    /// every later one does.
    /// </summary>
    public int dofrags;

    /// <summary>wi_stuff.c <c>dm_state</c> (T11.7): the deathmatch stats' stage (odd: a pause; 2 the frags counting; 4 done).</summary>
    public int dm_state;

    /// <summary>wi_stuff.c <c>dm_frags</c> (T11.7): the deathmatch frag table counted so far, killer by victim.</summary>
    public readonly int[,] dm_frags = new int[World.MAXPLAYERS, World.MAXPLAYERS];

    /// <summary>wi_stuff.c <c>dm_totals</c> (T11.7): each player's frag total counted so far.</summary>
    public readonly int[] dm_totals = new int[World.MAXPLAYERS];

    /// <summary>wi_stuff.c <c>snl_pointeron</c>: the "you are here" pointer shows (kept from one intermission to the next, as vanilla's static).</summary>
    public bool snl_pointeron;

    /// <summary>The world map's animations of <see cref="wbs"/>'s episode (wi_stuff.c <c>anims[wbs->epsd]</c>), or none.</summary>
    public anim_t[] anims = [];

    /// <summary>The sounds the last <see cref="WI_Ticker"/> started (<c>S_StartSound(NULL, …)</c>), for the tests and the overlay.</summary>
    public readonly List<sfxenum_t> sounds = [];

    private World? world;

    /// <summary>
    /// wi_stuff.c <c>WI_Start</c>: the intermission of
    /// <paramref name="wbstartstruct"/> starts with the stats: deathmatch's,
    /// a net game's or a single player's (T11.7: <paramref name="world"/>'s
    /// <see cref="World.deathmatch"/> and <see cref="World.netgame"/>).
    /// </summary>
    public void WI_Start(wbstartstruct_t wbstartstruct, World world)
    {
        this.world = world;
        WI_initVariables(wbstartstruct);
        WI_loadData();

        if (deathmatch != 0)
            WI_initDeathmatchStats();
        else if (netgame)
            WI_initNetgameStats();
        else
            WI_initStats();
    }

    /// <summary>g_game.c <c>deathmatch</c> (T11.7): the intermission's world's.</summary>
    private int deathmatch => world?.deathmatch ?? 0;

    /// <summary>g_game.c <c>netgame</c> (T11.7): the intermission's world's.</summary>
    private bool netgame => world?.netgame ?? false;

    private bool playeringame(int i) => world!.playeringame[i];

    /// <summary>wi_stuff.c <c>WI_initVariables</c> (no stat is 0 of 0: the totals are at least 1).</summary>
    private void WI_initVariables(wbstartstruct_t wbstartstruct)
    {
        wbs = wbstartstruct;
        acceleratestage = 0;
        cnt = bcnt = 0;
        me = wbs.pnum;

        if (wbs.maxkills == 0)
            wbs.maxkills = 1;
        if (wbs.maxitems == 0)
            wbs.maxitems = 1;
        if (wbs.maxsecret == 0)
            wbs.maxsecret = 1;

        if (flow.gameversion < GameVersion.exe_ultimate)
        {
            if (wbs.epsd > 2)
                wbs.epsd -= 3;
        }
    }

    /// <summary>wi_stuff.c <c>WI_loadData</c>'s animations (the patches load by name when drawn: <see cref="ScreenGraphics.Patch"/>).</summary>
    private void WI_loadData()
    {
        anims = [];
        if (flow.gamemode == GameMode.commercial || wbs!.epsd > 2)
            return;
        anims = NewAnims()[wbs.epsd];
        for (int j = 0; j < anims.Length; j++)
        {
            anim_t a = anims[j];
            for (int i = 0; i < a.nanims; i++)
            {
                // MONDO HACK!
                if (wbs.epsd != 1 || j != 8)
                    a.p[i] = $"WIA{wbs.epsd}{j:00}{i:00}"; // animations
                else
                    a.p[i] = $"WIA1{4:00}{i:00}"; // HACK ALERT! (anims[1][4].p[i])
            }
        }
    }

    private int M_Random() => flow.MRandom.M_Random();

    private void S_StartSound(sfxenum_t sfx)
    {
        sounds.Add(sfx);
        flow.S_StartSound(sfx);
    }

    /// <summary>wi_stuff.c <c>WI_initAnimatedBack</c>.</summary>
    private void WI_initAnimatedBack()
    {
        foreach (anim_t a in anims)
        {
            // init variables
            a.ctr = -1;

            // specify the next time to draw it
            if (a.type == animenum_t.ANIM_ALWAYS)
                a.nexttic = bcnt + 1 + (M_Random() % a.period);
            else if (a.type == animenum_t.ANIM_RANDOM)
                a.nexttic = bcnt + 1 + a.data2 + (M_Random() % a.data1);
            else if (a.type == animenum_t.ANIM_LEVEL)
                a.nexttic = bcnt + 1;
        }
    }

    /// <summary>wi_stuff.c <c>WI_updateAnimatedBack</c>.</summary>
    private void WI_updateAnimatedBack()
    {
        for (int i = 0; i < anims.Length; i++)
        {
            anim_t a = anims[i];
            if (bcnt != a.nexttic)
                continue;
            switch (a.type)
            {
                case animenum_t.ANIM_ALWAYS:
                    if (++a.ctr >= a.nanims)
                        a.ctr = 0;
                    a.nexttic = bcnt + a.period;
                    break;

                case animenum_t.ANIM_RANDOM:
                    a.ctr++;
                    if (a.ctr == a.nanims)
                    {
                        a.ctr = -1;
                        a.nexttic = bcnt + a.data2 + (M_Random() % a.data1);
                    }
                    else
                        a.nexttic = bcnt + a.period;
                    break;

                case animenum_t.ANIM_LEVEL:
                    // gawd-awful hack for level anims
                    if (!(state == stateenum_t.StatCount && i == 7) && wbs!.next == a.data1)
                    {
                        a.ctr++;
                        if (a.ctr == a.nanims)
                            a.ctr--;
                        a.nexttic = bcnt + a.period;
                    }
                    break;
            }
        }
    }

    /// <summary>The players in the game, in order.</summary>
    private IEnumerable<int> PlayersInGame()
    {
        for (int i = 0; i < World.MAXPLAYERS; i++)
        {
            if (playeringame(i))
                yield return i;
        }
    }

    /// <summary>
    /// wi_stuff.c <c>WI_fragSum</c> (T11.7): player <paramref name="playernum"/>'s
    /// frags of the other players in the game, less its own (suicides: "JDC
    /// hack - negative frags").
    /// </summary>
    public int WI_fragSum(int playernum)
    {
        int frags = 0;

        for (int i = 0; i < World.MAXPLAYERS; i++)
        {
            if (playeringame(i) && i != playernum)
                frags += plrs[playernum].frags[i];
        }

        // JDC hack - negative frags.
        frags -= plrs[playernum].frags[playernum];
        // UNUSED if (frags < 0)
        //     frags = 0;

        return frags;
    }

    /// <summary>wi_stuff.c <c>WI_initDeathmatchStats</c> (T11.7).</summary>
    private void WI_initDeathmatchStats()
    {
        state = stateenum_t.StatCount;
        acceleratestage = 0;
        dm_state = 1;

        cnt_pause = SimInfo.TICRATE;

        for (int i = 0; i < World.MAXPLAYERS; i++)
        {
            if (playeringame(i))
            {
                for (int j = 0; j < World.MAXPLAYERS; j++)
                {
                    if (playeringame(j))
                        dm_frags[i, j] = 0;
                }

                dm_totals[i] = 0;
            }
        }

        WI_initAnimatedBack();
    }

    /// <summary>
    /// wi_stuff.c <c>WI_updateDeathmatchStats</c> (T11.7): after a second's
    /// pause every frag of the table counts a frag a tic towards its value
    /// (at most ±99), a pistol shot every 4 tics, an explosion when all are
    /// done; fire or use show them at once, then go on (<c>sfx_slop</c>).
    /// </summary>
    private void WI_updateDeathmatchStats()
    {
        WI_updateAnimatedBack();

        if (acceleratestage != 0 && dm_state != 4)
        {
            acceleratestage = 0;

            for (int i = 0; i < World.MAXPLAYERS; i++)
            {
                if (playeringame(i))
                {
                    for (int j = 0; j < World.MAXPLAYERS; j++)
                    {
                        if (playeringame(j))
                            dm_frags[i, j] = plrs[i].frags[j];
                    }

                    dm_totals[i] = WI_fragSum(i);
                }
            }

            S_StartSound(sfxenum_t.sfx_barexp);
            dm_state = 4;
        }

        if (dm_state == 2)
        {
            if ((bcnt & 3) == 0)
                S_StartSound(sfxenum_t.sfx_pistol);

            bool stillticking = false;

            for (int i = 0; i < World.MAXPLAYERS; i++)
            {
                if (playeringame(i))
                {
                    for (int j = 0; j < World.MAXPLAYERS; j++)
                    {
                        if (playeringame(j) && dm_frags[i, j] != plrs[i].frags[j])
                        {
                            if (plrs[i].frags[j] < 0)
                                dm_frags[i, j]--;
                            else
                                dm_frags[i, j]++;

                            if (dm_frags[i, j] > 99)
                                dm_frags[i, j] = 99;

                            if (dm_frags[i, j] < -99)
                                dm_frags[i, j] = -99;

                            stillticking = true;
                        }
                    }
                    dm_totals[i] = WI_fragSum(i);

                    if (dm_totals[i] > 99)
                        dm_totals[i] = 99;

                    if (dm_totals[i] < -99)
                        dm_totals[i] = -99;
                }
            }
            if (!stillticking)
            {
                S_StartSound(sfxenum_t.sfx_barexp);
                dm_state++;
            }
        }
        else if (dm_state == 4)
        {
            if (acceleratestage != 0)
            {
                S_StartSound(sfxenum_t.sfx_slop);

                if (flow.gamemode == GameMode.commercial)
                    WI_initNoState();
                else
                    WI_initShowNextLoc();
            }
        }
        else if ((dm_state & 1) != 0)
        {
            if (--cnt_pause == 0)
            {
                dm_state++;
                cnt_pause = SimInfo.TICRATE;
            }
        }
    }

    /// <summary>
    /// wi_stuff.c <c>WI_initNetgameStats</c> (T11.7): the counts of the
    /// players in the game from 0, and <see cref="dofrags"/> (added to, never
    /// cleared: vanilla's).
    /// </summary>
    private void WI_initNetgameStats()
    {
        state = stateenum_t.StatCount;
        acceleratestage = 0;
        ng_state = 1;

        cnt_pause = SimInfo.TICRATE;

        for (int i = 0; i < World.MAXPLAYERS; i++)
        {
            if (!playeringame(i))
                continue;

            cnt_kills[i] = cnt_items[i] = cnt_secret[i] = cnt_frags[i] = 0;

            dofrags += WI_fragSum(i);
        }

        dofrags = dofrags != 0 ? 1 : 0;

        WI_initAnimatedBack();
    }

    /// <summary>
    /// wi_stuff.c <c>WI_updateNetgameStats</c> (T11.7): every player's kills,
    /// items and secrets count up together 2% a tic, then (with
    /// <see cref="dofrags"/>) the frags one a tic, a second's pause between
    /// them, a pistol shot every 4 tics and an explosion as each ends (the
    /// frags a death cry, <c>sfx_pldeth</c>); fire or use show them all at
    /// once, then go on.
    /// </summary>
    private void WI_updateNetgameStats()
    {
        WI_updateAnimatedBack();

        if (acceleratestage != 0 && ng_state != 10)
        {
            acceleratestage = 0;

            for (int i = 0; i < World.MAXPLAYERS; i++)
            {
                if (!playeringame(i))
                    continue;

                cnt_kills[i] = plrs[i].skills * 100 / wbs!.maxkills;
                cnt_items[i] = plrs[i].sitems * 100 / wbs.maxitems;
                cnt_secret[i] = plrs[i].ssecret * 100 / wbs.maxsecret;

                if (dofrags != 0)
                    cnt_frags[i] = WI_fragSum(i);
            }
            S_StartSound(sfxenum_t.sfx_barexp);
            ng_state = 10;
        }

        if (ng_state == 2)
        {
            if ((bcnt & 3) == 0)
                S_StartSound(sfxenum_t.sfx_pistol);

            bool stillticking = false;

            for (int i = 0; i < World.MAXPLAYERS; i++)
            {
                if (!playeringame(i))
                    continue;

                cnt_kills[i] += 2;

                if (cnt_kills[i] >= plrs[i].skills * 100 / wbs!.maxkills)
                    cnt_kills[i] = plrs[i].skills * 100 / wbs.maxkills;
                else
                    stillticking = true;
            }

            if (!stillticking)
            {
                S_StartSound(sfxenum_t.sfx_barexp);
                ng_state++;
            }
        }
        else if (ng_state == 4)
        {
            if ((bcnt & 3) == 0)
                S_StartSound(sfxenum_t.sfx_pistol);

            bool stillticking = false;

            for (int i = 0; i < World.MAXPLAYERS; i++)
            {
                if (!playeringame(i))
                    continue;

                cnt_items[i] += 2;
                if (cnt_items[i] >= plrs[i].sitems * 100 / wbs!.maxitems)
                    cnt_items[i] = plrs[i].sitems * 100 / wbs.maxitems;
                else
                    stillticking = true;
            }
            if (!stillticking)
            {
                S_StartSound(sfxenum_t.sfx_barexp);
                ng_state++;
            }
        }
        else if (ng_state == 6)
        {
            if ((bcnt & 3) == 0)
                S_StartSound(sfxenum_t.sfx_pistol);

            bool stillticking = false;

            for (int i = 0; i < World.MAXPLAYERS; i++)
            {
                if (!playeringame(i))
                    continue;

                cnt_secret[i] += 2;

                if (cnt_secret[i] >= plrs[i].ssecret * 100 / wbs!.maxsecret)
                    cnt_secret[i] = plrs[i].ssecret * 100 / wbs.maxsecret;
                else
                    stillticking = true;
            }

            if (!stillticking)
            {
                S_StartSound(sfxenum_t.sfx_barexp);
                ng_state += 1 + 2 * (dofrags == 0 ? 1 : 0);
            }
        }
        else if (ng_state == 8)
        {
            if ((bcnt & 3) == 0)
                S_StartSound(sfxenum_t.sfx_pistol);

            bool stillticking = false;

            for (int i = 0; i < World.MAXPLAYERS; i++)
            {
                if (!playeringame(i))
                    continue;

                cnt_frags[i] += 1;

                int fsum = WI_fragSum(i);
                if (cnt_frags[i] >= fsum)
                    cnt_frags[i] = fsum;
                else
                    stillticking = true;
            }

            if (!stillticking)
            {
                S_StartSound(sfxenum_t.sfx_pldeth);
                ng_state++;
            }
        }
        else if (ng_state == 10)
        {
            if (acceleratestage != 0)
            {
                S_StartSound(sfxenum_t.sfx_sgcock);
                if (flow.gamemode == GameMode.commercial)
                    WI_initNoState();
                else
                    WI_initShowNextLoc();
            }
        }
        else if ((ng_state & 1) != 0)
        {
            if (--cnt_pause == 0)
            {
                ng_state++;
                cnt_pause = SimInfo.TICRATE;
            }
        }
    }

    /// <summary>
    /// Whether the stats are all shown, waiting for a press to go on (T11.7:
    /// the stage of the game's kind of stats, <see cref="sp_state"/> or
    /// <see cref="ng_state"/> 10, <see cref="dm_state"/> 4).
    /// </summary>
    public bool StatsShown => deathmatch != 0 ? dm_state == 4 : netgame ? ng_state == 10 : sp_state == 10;

    /// <summary>wi_stuff.c <c>WI_initStats</c>.</summary>
    private void WI_initStats()
    {
        state = stateenum_t.StatCount;
        acceleratestage = 0;
        sp_state = 1;
        cnt_kills[0] = cnt_items[0] = cnt_secret[0] = -1;
        cnt_time = cnt_par = -1;
        cnt_pause = SimInfo.TICRATE;

        WI_initAnimatedBack();
    }

    /// <summary>
    /// wi_stuff.c <c>WI_updateStats</c>: kills, items and secrets count up 2%
    /// a tic, the time and the par 3 s a tic, a pistol shot every 4 tics and
    /// an explosion as each ends, a second's pause between them; fire or use
    /// show them all at once, then go on (Doom II straight to the end).
    /// </summary>
    private void WI_updateStats()
    {
        WI_updateAnimatedBack();

        wbplayerstruct_t p = plrs[me];
        if (acceleratestage != 0 && sp_state != 10)
        {
            acceleratestage = 0;
            cnt_kills[0] = p.skills * 100 / wbs!.maxkills;
            cnt_items[0] = p.sitems * 100 / wbs.maxitems;
            cnt_secret[0] = p.ssecret * 100 / wbs.maxsecret;
            cnt_time = p.stime / SimInfo.TICRATE;
            cnt_par = wbs.partime / SimInfo.TICRATE;
            S_StartSound(sfxenum_t.sfx_barexp);
            sp_state = 10;
        }

        if (sp_state == 2)
        {
            cnt_kills[0] += 2;

            if ((bcnt & 3) == 0)
                S_StartSound(sfxenum_t.sfx_pistol);

            if (cnt_kills[0] >= p.skills * 100 / wbs!.maxkills)
            {
                cnt_kills[0] = p.skills * 100 / wbs.maxkills;
                S_StartSound(sfxenum_t.sfx_barexp);
                sp_state++;
            }
        }
        else if (sp_state == 4)
        {
            cnt_items[0] += 2;

            if ((bcnt & 3) == 0)
                S_StartSound(sfxenum_t.sfx_pistol);

            if (cnt_items[0] >= p.sitems * 100 / wbs!.maxitems)
            {
                cnt_items[0] = p.sitems * 100 / wbs.maxitems;
                S_StartSound(sfxenum_t.sfx_barexp);
                sp_state++;
            }
        }
        else if (sp_state == 6)
        {
            cnt_secret[0] += 2;

            if ((bcnt & 3) == 0)
                S_StartSound(sfxenum_t.sfx_pistol);

            if (cnt_secret[0] >= p.ssecret * 100 / wbs!.maxsecret)
            {
                cnt_secret[0] = p.ssecret * 100 / wbs.maxsecret;
                S_StartSound(sfxenum_t.sfx_barexp);
                sp_state++;
            }
        }
        else if (sp_state == 8)
        {
            if ((bcnt & 3) == 0)
                S_StartSound(sfxenum_t.sfx_pistol);

            cnt_time += 3;

            if (cnt_time >= p.stime / SimInfo.TICRATE)
                cnt_time = p.stime / SimInfo.TICRATE;

            cnt_par += 3;

            if (cnt_par >= wbs!.partime / SimInfo.TICRATE)
            {
                cnt_par = wbs.partime / SimInfo.TICRATE;

                if (cnt_time >= p.stime / SimInfo.TICRATE)
                {
                    S_StartSound(sfxenum_t.sfx_barexp);
                    sp_state++;
                }
            }
        }
        else if (sp_state == 10)
        {
            if (acceleratestage != 0)
            {
                S_StartSound(sfxenum_t.sfx_sgcock);

                if (flow.gamemode == GameMode.commercial)
                    WI_initNoState();
                else
                    WI_initShowNextLoc();
            }
        }
        else if ((sp_state & 1) != 0)
        {
            if (--cnt_pause == 0)
            {
                sp_state++;
                cnt_pause = SimInfo.TICRATE;
            }
        }
    }

    /// <summary>wi_stuff.c <c>WI_initShowNextLoc</c>.</summary>
    private void WI_initShowNextLoc()
    {
        state = stateenum_t.ShowNextLoc;
        acceleratestage = 0;
        cnt = SHOWNEXTLOCDELAY * SimInfo.TICRATE;

        WI_initAnimatedBack();
    }

    /// <summary>wi_stuff.c <c>WI_updateShowNextLoc</c>: the pointer flashes (on 20 tics of 32).</summary>
    private void WI_updateShowNextLoc()
    {
        WI_updateAnimatedBack();

        if (--cnt == 0 || acceleratestage != 0)
            WI_initNoState();
        else
            snl_pointeron = (cnt & 31) < 20;
    }

    /// <summary>wi_stuff.c <c>WI_initNoState</c>.</summary>
    private void WI_initNoState()
    {
        state = stateenum_t.NoState;
        acceleratestage = 0;
        cnt = 10;
    }

    /// <summary>
    /// wi_stuff.c <c>WI_updateNoState</c>: then the intermission ends
    /// (<see cref="GameFlow.G_WorldDone"/>; Chocolate Doom no longer calls
    /// <c>WI_End</c> here: the drawer runs until the game state changes).
    /// </summary>
    private void WI_updateNoState()
    {
        WI_updateAnimatedBack();

        if (--cnt == 0)
            flow.G_WorldDone();
    }

    /// <summary>
    /// wi_stuff.c <c>WI_checkForAccelerate</c>: a press of fire or use (not
    /// held from before: <see cref="player_t.attackdown"/>,
    /// <see cref="player_t.usedown"/>) by any player skips to the next stage.
    /// </summary>
    private void WI_checkForAccelerate()
    {
        for (int i = 0; i < World.MAXPLAYERS; i++)
        {
            if (!world!.playeringame[i])
                continue;
            player_t player = world.players[i];
            if ((player.cmd.buttons & buttoncode_t.BT_ATTACK) != 0)
            {
                if (!player.attackdown)
                    acceleratestage = 1;
                player.attackdown = true;
            }
            else
                player.attackdown = false;
            if ((player.cmd.buttons & buttoncode_t.BT_USE) != 0)
            {
                if (!player.usedown)
                    acceleratestage = 1;
                player.usedown = true;
            }
            else
                player.usedown = false;
        }
    }

    /// <summary>wi_stuff.c <c>WI_Ticker</c>: updates stuff each tick (the music at its first, T7.8c: <c>mus_inter</c>, Doom II <c>mus_dm2int</c>).</summary>
    public void WI_Ticker()
    {
        sounds.Clear();

        // counter for general background animation
        bcnt++;

        if (bcnt == 1)
        {
            // intermission music
            if (flow.gamemode == GameMode.commercial)
                flow.S_ChangeMusic(musicenum_t.mus_dm2int, true);
            else
                flow.S_ChangeMusic(musicenum_t.mus_inter, true);
        }

        // check for button presses to skip delays
        WI_checkForAccelerate();

        switch (state)
        {
            case stateenum_t.StatCount:
                if (deathmatch != 0)
                    WI_updateDeathmatchStats();
                else if (netgame)
                    WI_updateNetgameStats();
                else
                    WI_updateStats();
                break;
            case stateenum_t.ShowNextLoc:
                WI_updateShowNextLoc();
                break;
            case stateenum_t.NoState:
                WI_updateNoState();
                break;
        }
    }

    // ---- drawing ----

    /// <summary>
    /// wi_stuff.c <c>WI_Drawer</c>: the stats (<see cref="WI_drawStats"/>),
    /// the next location (<see cref="WI_drawShowNextLoc"/>) or, at the end,
    /// the same with the pointer on (<c>WI_drawNoState</c>).
    /// </summary>
    public void WI_Drawer(ScreenGraphics g, HudScreen screen)
    {
        screen.Clear();
        if (wbs is null)
            return;
        switch (state)
        {
            case stateenum_t.StatCount:
                if (deathmatch != 0)
                    WI_drawDeathmatchStats(g, screen);
                else if (netgame)
                    WI_drawNetgameStats(g, screen);
                else
                    WI_drawStats(g, screen);
                break;
            case stateenum_t.ShowNextLoc:
                WI_drawShowNextLoc(g, screen);
                break;
            case stateenum_t.NoState:
                // WI_drawNoState
                snl_pointeron = true;
                WI_drawShowNextLoc(g, screen);
                break;
        }
    }

    /// <summary>The background (<c>WIMAPn</c>; Doom II and, from The Ultimate Doom's executable on, the fourth episode <c>INTERPIC</c>).</summary>
    private string Background =>
        flow.gamemode == GameMode.commercial || flow.gameversion >= GameVersion.exe_ultimate && wbs!.epsd == 3 ? "INTERPIC" : $"WIMAP{wbs!.epsd}";

    /// <summary>wi_stuff.c <c>WI_slamBackground</c>.</summary>
    private void WI_slamBackground(ScreenGraphics g, HudScreen screen) => screen.V_DrawPatch(0, 0, g.Patch(Background));

    /// <summary>wi_stuff.c <c>lnames</c>: the level name graphic of map <paramref name="map"/> (origin 0).</summary>
    private string LevelName(int map) =>
        flow.gamemode == GameMode.commercial ? $"CWILV{map:00}" : $"WILV{wbs!.epsd}{map}";

    /// <summary>The level's name in the message font, without its graphic.</summary>
    private string MapName(int map) => World.MapName(flow.gamemode, wbs!.epsd + 1, map + 1);

    /// <summary>
    /// A patch centred across the screen with its top at <paramref name="y"/>
    /// (<c>V_DrawPatch((SCREENWIDTH - width) / 2, y, …)</c>, its offsets
    /// applied), or <paramref name="text"/> without it; returns the patch's
    /// height (the font's without it).
    /// </summary>
    private static int DrawCentred(ScreenGraphics g, HudScreen screen, int y, string patch, string text)
    {
        if (g.Patch(patch) is { } p)
        {
            screen.V_DrawPatch((HudScreen.SCREENWIDTH - p.Width) / 2, y, p);
            return p.Height;
        }
        g.DrawText(screen, (HudScreen.SCREENWIDTH - g.TextWidth(text)) / 2, y, text);
        return g.FontHeight;
    }

    /// <summary>wi_stuff.c <c>WI_drawLF</c>: draws "&lt;Levelname&gt; Finished!".</summary>
    private void WI_drawLF(ScreenGraphics g, HudScreen screen)
    {
        int y = WI_TITLEY;

        if (flow.gamemode != GameMode.commercial || wbs!.last < NUMCMAPS)
        {
            // draw <LevelName>
            int height = DrawCentred(g, screen, y, LevelName(wbs!.last), MapName(wbs.last));

            // draw "Finished!"
            y += 5 * height / 4;

            DrawCentred(g, screen, y, "WIF", "FINISHED");
        }
        // MAP33: nothing is displayed (and past it vanilla bombs out)
    }

    /// <summary>wi_stuff.c <c>WI_drawEL</c>: draws "Entering &lt;LevelName&gt;".</summary>
    private void WI_drawEL(ScreenGraphics g, HudScreen screen)
    {
        int y = WI_TITLEY;

        // draw "Entering"
        DrawCentred(g, screen, y, "WIENTER", "ENTERING");

        // draw level
        IndexedImage? next = g.Patch(LevelName(wbs!.next));
        y += 5 * (next?.Height ?? g.FontHeight) / 4;

        DrawCentred(g, screen, y, LevelName(wbs.next), MapName(wbs.next));
    }

    /// <summary>
    /// wi_stuff.c <c>WI_drawOnLnode</c>: the first of <paramref name="c"/>
    /// (patch names) that fits on the screen at level <paramref name="n"/>'s
    /// place; nothing when none does (vanilla prints a debug line).
    /// </summary>
    private void WI_drawOnLnode(ScreenGraphics g, HudScreen screen, int n, string?[] c)
    {
        (int x, int y) node = lnodes[wbs!.epsd][n];
        bool fits = false;
        int i = 0;
        IndexedImage? patch;
        do
        {
            patch = c[i] is { } name ? g.Patch(name) : null;
            if (patch is null)
                return; // a WAD without the graphic (the synthetic IWAD)
            int left = node.x - patch.LeftOffset;
            int top = node.y - patch.TopOffset;
            int right = left + patch.Width;
            int bottom = top + patch.Height;

            if (left >= 0 && right < HudScreen.SCREENWIDTH && top >= 0 && bottom < HudScreen.SCREENHEIGHT)
                fits = true;
            else
                i++;
        }
        while (!fits && i != 2 && c[i] is not null);

        if (fits && i < 2)
            screen.V_DrawPatch(node.x, node.y, patch);
    }

    private static readonly string?[] splat = ["WISPLAT", null];
    private static readonly string?[] yah = ["WIURH0", "WIURH1"];

    /// <summary>wi_stuff.c <c>WI_drawAnimatedBack</c>.</summary>
    private void WI_drawAnimatedBack(ScreenGraphics g, HudScreen screen)
    {
        foreach (anim_t a in anims)
        {
            if (a.ctr >= 0 && a.p[a.ctr] is { } name)
                screen.V_DrawPatch(a.x, a.y, g.Patch(name));
        }
    }

    /// <summary>wi_stuff.c <c>WI_drawShowNextLoc</c>: the splats on the levels done, the flashing pointer on the next, "Entering" it.</summary>
    private void WI_drawShowNextLoc(ScreenGraphics g, HudScreen screen)
    {
        WI_slamBackground(g, screen);

        // draw animated background
        WI_drawAnimatedBack(g, screen);

        if (flow.gamemode != GameMode.commercial)
        {
            if (wbs!.epsd > 2)
            {
                WI_drawEL(g, screen);
                return;
            }

            int last = wbs.last == 8 ? wbs.next - 1 : wbs.last;

            // draw a splat on taken cities.
            for (int i = 0; i <= last; i++)
                WI_drawOnLnode(g, screen, i, splat);

            // splat the secret level?
            if (wbs.didsecret)
                WI_drawOnLnode(g, screen, 8, splat);

            // draw flashing ptr
            if (snl_pointeron)
                WI_drawOnLnode(g, screen, wbs.next, yah);
        }

        // draws which level you are entering..
        if (flow.gamemode != GameMode.commercial || wbs!.next != 30)
            WI_drawEL(g, screen);
    }

    /// <summary>The <c>WINUM*</c> digits' width (<c>WINUM0</c>'s; the message font's without it).</summary>
    private static int NumWidth(ScreenGraphics g) => g.Patch("WINUM0")?.Width ?? g.CharWidth('0');

    /// <summary>
    /// wi_stuff.c <c>WI_drawNum</c>: <paramref name="n"/> ending at
    /// <paramref name="x"/>, in at least <paramref name="digits"/> digits
    /// (or as many as needed when negative); returns the new x (1994 draws nothing).
    /// </summary>
    private static int WI_drawNum(ScreenGraphics g, HudScreen screen, int x, int y, int n, int digits)
    {
        int fontwidth = NumWidth(g);

        if (digits < 0)
        {
            if (n == 0)
            {
                // make variable-length zeros 1 digit long
                digits = 1;
            }
            else
            {
                // figure out # of digits in #
                digits = 0;
                for (int temp = n; temp != 0; temp /= 10)
                    digits++;
            }
        }

        bool neg = n < 0;
        if (neg)
            n = -n;

        // if non-number, do not draw it
        if (n == 1994)
            return 0;

        // draw the new number
        while (digits-- > 0)
        {
            x -= fontwidth;
            g.DrawLabel(screen, x, y, $"WINUM{n % 10}", ((char)('0' + n % 10)).ToString());
            n /= 10;
        }

        // draw a minus sign if necessary
        if (neg)
            g.DrawLabel(screen, x -= 8, y, "WIMINUS", "-");

        return x;
    }

    /// <summary>wi_stuff.c <c>WI_drawPercent</c>: <paramref name="p"/>% ending at <paramref name="x"/> (nothing below 0).</summary>
    private static void WI_drawPercent(ScreenGraphics g, HudScreen screen, int x, int y, int p)
    {
        if (p < 0)
            return;

        g.DrawLabel(screen, x, y, "WIPCNT", "%");
        WI_drawNum(g, screen, x, y, p, -1);
    }

    /// <summary>wi_stuff.c <c>WI_drawTime</c>: display level completion time and par, or "sucks" message if overflow.</summary>
    private static void WI_drawTime(ScreenGraphics g, HudScreen screen, int x, int y, int t)
    {
        if (t < 0)
            return;

        if (t <= 61 * 59)
        {
            int colonwidth = g.Patch("WICOLON")?.Width ?? g.CharWidth(':');
            int div = 1;
            do
            {
                int n = t / div % 60;
                x = WI_drawNum(g, screen, x, y, n, 2) - colonwidth;
                div *= 60;

                // draw
                if (div == 60 || t / div != 0)
                    g.DrawLabel(screen, x, y, "WICOLON", ":");
            }
            while (t / div != 0);
        }
        else
        {
            // "sucks"
            IndexedImage? sucks = g.Patch("WISUCKS");
            g.DrawLabel(screen, x - (sucks?.Width ?? g.TextWidth("SUCKS")), y, "WISUCKS", "SUCKS");
        }
    }

    /// <summary>wi_stuff.c <c>WI_drawStats</c>: the single player stats as counted so far.</summary>
    private void WI_drawStats(ScreenGraphics g, HudScreen screen)
    {
        // line height
        int lh = 3 * (g.Patch("WINUM0")?.Height ?? g.FontHeight) / 2;

        WI_slamBackground(g, screen);

        // draw animated background
        WI_drawAnimatedBack(g, screen);

        WI_drawLF(g, screen);

        g.DrawLabel(screen, SP_STATSX, SP_STATSY, "WIOSTK", "KILLS");
        WI_drawPercent(g, screen, HudScreen.SCREENWIDTH - SP_STATSX, SP_STATSY, cnt_kills[0]);

        g.DrawLabel(screen, SP_STATSX, SP_STATSY + lh, "WIOSTI", "ITEMS");
        WI_drawPercent(g, screen, HudScreen.SCREENWIDTH - SP_STATSX, SP_STATSY + lh, cnt_items[0]);

        g.DrawLabel(screen, SP_STATSX, SP_STATSY + 2 * lh, "WISCRT2", "SECRET");
        WI_drawPercent(g, screen, HudScreen.SCREENWIDTH - SP_STATSX, SP_STATSY + 2 * lh, cnt_secret[0]);

        g.DrawLabel(screen, SP_TIMEX, SP_TIMEY, "WITIME", "TIME");
        WI_drawTime(g, screen, HudScreen.SCREENWIDTH / 2 - SP_TIMEX, SP_TIMEY, cnt_time);

        if (wbs!.epsd < 3)
        {
            g.DrawLabel(screen, HudScreen.SCREENWIDTH / 2 + SP_TIMEX, SP_TIMEY, "WIPAR", "PAR");
            WI_drawTime(g, screen, HudScreen.SCREENWIDTH - SP_TIMEX, SP_TIMEY, cnt_par);
        }
    }

    /// <summary>A patch's width, or <paramref name="text"/>'s in the message font without it.</summary>
    private static int LabelWidth(ScreenGraphics g, string patch, string text) => g.Patch(patch)?.Width ?? g.TextWidth(text);

    /// <summary>
    /// wi_stuff.c <c>p[i]</c> (T11.7): player <paramref name="i"/>'s coloured
    /// background, <c>STPB0</c>–<c>STPB3</c> (patches of their own: no
    /// translation), or its number in the message font without it.
    /// </summary>
    private static (string Patch, string Text) PlayerBack(int i) => ($"STPB{i}", $"{i + 1}");

    /// <summary>wi_stuff.c <c>items</c>: <c>WIOSTI</c>, or a net game's <c>WIOBJ</c> where the WAD has it (Chocolate Doom: the French IWAD's).</summary>
    private string ItemsPatch(ScreenGraphics g) => netgame && deathmatch == 0 && g.Patch("WIOBJ") is not null ? "WIOBJ" : "WIOSTI";

    /// <summary>
    /// wi_stuff.c <c>WI_drawDeathmatchStats</c> (T11.7): the frag table,
    /// killers down and victims across, each player's <c>STPB*</c> on both
    /// sides, the console player's (<see cref="me"/>) marked with the
    /// status bar's faces (<c>STFDEAD0</c> across, <c>STFST01</c> down), and
    /// the totals.
    /// </summary>
    private void WI_drawDeathmatchStats(ScreenGraphics g, HudScreen screen)
    {
        WI_slamBackground(g, screen);

        // draw animated background
        WI_drawAnimatedBack(g, screen);
        WI_drawLF(g, screen);

        // draw stat titles (top line)
        g.DrawLabel(screen, DM_TOTALSX - LabelWidth(g, "WIMSTT", "TOTAL") / 2, DM_MATRIXY - WI_SPACINGY + 10, "WIMSTT", "TOTAL");

        g.DrawLabel(screen, DM_KILLERSX, DM_KILLERSY, "WIKILRS", "KILLERS");
        g.DrawLabel(screen, DM_VICTIMSX, DM_VICTIMSY, "WIVCTMS", "VICTIMS");

        // draw P?
        int x = DM_MATRIXX + DM_SPACINGX;
        int y = DM_MATRIXY;

        for (int i = 0; i < World.MAXPLAYERS; i++)
        {
            if (playeringame(i))
            {
                (string patch, string text) = PlayerBack(i);
                int half = LabelWidth(g, patch, text) / 2;
                g.DrawLabel(screen, x - half, DM_MATRIXY - WI_SPACINGY, patch, text);

                g.DrawLabel(screen, DM_MATRIXX - half, y, patch, text);

                if (i == me)
                {
                    screen.V_DrawPatch(x - half, DM_MATRIXY - WI_SPACINGY, g.Patch("STFDEAD0"));

                    screen.V_DrawPatch(DM_MATRIXX - half, y, g.Patch("STFST01"));
                }
            }
            // (vanilla's WIBP* of the players not in the game: commented out)
            x += DM_SPACINGX;
            y += WI_SPACINGY;
        }

        // draw stats
        y = DM_MATRIXY + 10;
        int w = NumWidth(g);

        for (int i = 0; i < World.MAXPLAYERS; i++)
        {
            x = DM_MATRIXX + DM_SPACINGX;

            if (playeringame(i))
            {
                for (int j = 0; j < World.MAXPLAYERS; j++)
                {
                    if (playeringame(j))
                        WI_drawNum(g, screen, x + w, y, dm_frags[i, j], 2);

                    x += DM_SPACINGX;
                }
                WI_drawNum(g, screen, DM_TOTALSX + w, y, dm_totals[i], 2);
            }
            y += WI_SPACINGY;
        }
    }

    /// <summary>
    /// wi_stuff.c <c>NG_STATSX</c>: the net game stats' left edge, by the
    /// console player's face (<c>STFST01</c>) and further right without the
    /// frags' column.
    /// </summary>
    private int NG_STATSX(ScreenGraphics g) => 32 + (g.Patch("STFST01")?.Width ?? 24) / 2 + 32 * (dofrags == 0 ? 1 : 0);

    /// <summary>
    /// wi_stuff.c <c>WI_drawNetgameStats</c> (T11.7): a row a player in the
    /// game, its <c>STPB*</c> (the console player's, <see cref="me"/>, with
    /// the face <c>STFST01</c> over it), its kills, items and secrets in %
    /// and, with <see cref="dofrags"/>, its frags.
    /// </summary>
    private void WI_drawNetgameStats(ScreenGraphics g, HudScreen screen)
    {
        int pwidth = LabelWidth(g, "WIPCNT", "%");
        int statsx = NG_STATSX(g);

        WI_slamBackground(g, screen);

        // draw animated background
        WI_drawAnimatedBack(g, screen);

        WI_drawLF(g, screen);

        // draw stat titles (top line)
        string items = ItemsPatch(g);
        g.DrawLabel(screen, statsx + NG_SPACINGX - LabelWidth(g, "WIOSTK", "KILLS"), NG_STATSY, "WIOSTK", "KILLS");

        g.DrawLabel(screen, statsx + 2 * NG_SPACINGX - LabelWidth(g, items, "ITEMS"), NG_STATSY, items, "ITEMS");

        g.DrawLabel(screen, statsx + 3 * NG_SPACINGX - LabelWidth(g, "WIOSTS", "SCRT"), NG_STATSY, "WIOSTS", "SCRT");

        if (dofrags != 0)
            g.DrawLabel(screen, statsx + 4 * NG_SPACINGX - LabelWidth(g, "WIFRGS", "FRGS"), NG_STATSY, "WIFRGS", "FRGS");

        // draw stats
        int y = NG_STATSY + (g.Patch("WIOSTK")?.Height ?? g.FontHeight);

        for (int i = 0; i < World.MAXPLAYERS; i++)
        {
            if (!playeringame(i))
                continue;

            int x = statsx;
            (string patch, string text) = PlayerBack(i);
            int pw = LabelWidth(g, patch, text);
            g.DrawLabel(screen, x - pw, y, patch, text);

            if (i == me)
                screen.V_DrawPatch(x - pw, y, g.Patch("STFST01"));

            x += NG_SPACINGX;
            WI_drawPercent(g, screen, x - pwidth, y + 10, cnt_kills[i]);
            x += NG_SPACINGX;
            WI_drawPercent(g, screen, x - pwidth, y + 10, cnt_items[i]);
            x += NG_SPACINGX;
            WI_drawPercent(g, screen, x - pwidth, y + 10, cnt_secret[i]);
            x += NG_SPACINGX;

            if (dofrags != 0)
                WI_drawNum(g, screen, x, y + 10, cnt_frags[i], -1);

            y += WI_SPACINGY;
        }
    }

    /// <summary>The intermission's state as the overlay and the level script show it.</summary>
    public string StateText() => state switch
    {
        stateenum_t.StatCount when deathmatch != 0 => $"deathmatch stats, dm_state {dm_state}: totals {string.Join(' ', PlayersInGame().Select(i => dm_totals[i]))}",
        stateenum_t.StatCount when netgame => $"net game stats, ng_state {ng_state}: "
            + string.Join("; ", PlayersInGame().Select(i => $"p{i + 1} kills {cnt_kills[i]}% items {cnt_items[i]}% secret {cnt_secret[i]}%" + (dofrags != 0 ? $" frags {cnt_frags[i]}" : ""))),
        stateenum_t.StatCount => $"stats, sp_state {sp_state}: kills {cnt_kills[0]}% items {cnt_items[0]}% secret {cnt_secret[0]}% time {cnt_time} s par {cnt_par} s",
        stateenum_t.ShowNextLoc => $"next location, {cnt} tics",
        _ => $"ending, {cnt} tics",
    };
}
