using System;
using IsoDoom.Sim;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

/// <summary>
/// st_stuff.c and st_lib.c (T6.11, SPEC §7.6): the status bar of the
/// console player, ported as vanilla. <see cref="ST_Start"/> at each level
/// (vanilla's <c>P_SpawnPlayer</c> calls it), <see cref="ST_Ticker"/> after
/// each tic (vanilla's <c>G_Ticker</c>, after <c>P_Ticker</c>), and
/// <see cref="ST_Drawer"/> draws the bar into <see cref="Screen"/> (rows
/// 168–199 of vanilla's screen, palette indices) as vanilla's does, the
/// whole bar after <see cref="ST_Start"/> and then what changed, with its
/// quirks. The face's random glances use
/// <c>M_Random</c> from the presentation's own <see cref="DoomRandom"/>
/// (<see cref="Random"/>), never the sim's: the status bar reads the sim's
/// <see cref="player_t"/> and changes nothing, so the sim's state and
/// checksum do not depend on it. No Godot types, so the tests link it (and
/// <see cref="ST_doPaletteStuff"/>, T6.8).
/// </summary>
public sealed class StStuff
{
    // st_stuff.c: the PLAYPAL palettes.
    public const int STARTREDPALS = 1;
    public const int STARTBONUSPALS = 9;
    public const int NUMREDPALS = 8;
    public const int NUMBONUSPALS = 4;
    /// <summary>Radiation suit, green shift.</summary>
    public const int RADIATIONPAL = 13;

    // st_stuff.h: the bar's size and place.
    public const int ST_HEIGHT = 32;
    public const int ST_WIDTH = HudScreen.SCREENWIDTH;
    public const int ST_Y = HudScreen.SCREENHEIGHT - ST_HEIGHT;

    // st_stuff.c
    public const int ST_X = 0;
    public const int ST_FX = 143;

    public const int ST_NUMPAINFACES = 5;
    public const int ST_NUMSTRAIGHTFACES = 3;
    public const int ST_NUMTURNFACES = 2;
    public const int ST_NUMSPECIALFACES = 3;
    public const int ST_FACESTRIDE = ST_NUMSTRAIGHTFACES + ST_NUMTURNFACES + ST_NUMSPECIALFACES;
    public const int ST_NUMEXTRAFACES = 2;
    public const int ST_NUMFACES = ST_FACESTRIDE * ST_NUMPAINFACES + ST_NUMEXTRAFACES;
    public const int ST_TURNOFFSET = ST_NUMSTRAIGHTFACES;
    public const int ST_OUCHOFFSET = ST_TURNOFFSET + ST_NUMTURNFACES;
    public const int ST_EVILGRINOFFSET = ST_OUCHOFFSET + 1;
    public const int ST_RAMPAGEOFFSET = ST_EVILGRINOFFSET + 1;
    public const int ST_GODFACE = ST_NUMPAINFACES * ST_FACESTRIDE;
    public const int ST_DEADFACE = ST_GODFACE + 1;
    public const int ST_FACESX = 143;
    public const int ST_FACESY = 168;
    public const int ST_EVILGRINCOUNT = 2 * SimInfo.TICRATE;
    public const int ST_STRAIGHTFACECOUNT = SimInfo.TICRATE / 2;
    public const int ST_TURNCOUNT = 1 * SimInfo.TICRATE;
    public const int ST_OUCHCOUNT = 1 * SimInfo.TICRATE;
    public const int ST_RAMPAGEDELAY = 2 * SimInfo.TICRATE;
    public const int ST_MUCHPAIN = 20;

    public const int ST_AMMOWIDTH = 3, ST_AMMOX = 44, ST_AMMOY = 171;
    public const int ST_HEALTHX = 90, ST_HEALTHY = 171;
    public const int ST_ARMSX = 111, ST_ARMSY = 172, ST_ARMSBGX = 104, ST_ARMSBGY = 168, ST_ARMSXSPACE = 12, ST_ARMSYSPACE = 10;
    public const int ST_FRAGSX = 138, ST_FRAGSY = 171, ST_FRAGSWIDTH = 2;
    public const int ST_ARMORX = 221, ST_ARMORY = 171;
    public const int ST_KEY0X = 239, ST_KEY0Y = 171, ST_KEY1X = 239, ST_KEY1Y = 181, ST_KEY2X = 239, ST_KEY2Y = 191;
    public const int ST_AMMO0WIDTH = 3, ST_AMMO0X = 288, ST_AMMO0Y = 173, ST_AMMO1X = 288, ST_AMMO1Y = 179,
        ST_AMMO2X = 288, ST_AMMO2Y = 191, ST_AMMO3X = 288, ST_AMMO3Y = 185;
    public const int ST_MAXAMMO0WIDTH = 3, ST_MAXAMMO0X = 314, ST_MAXAMMO0Y = 173, ST_MAXAMMO1X = 314, ST_MAXAMMO1Y = 179,
        ST_MAXAMMO2X = 314, ST_MAXAMMO2Y = 191, ST_MAXAMMO3X = 314, ST_MAXAMMO3Y = 185;

    /// <summary>st_stuff.c <c>largeammo</c>: the ready weapon's ammo for a weapon without ammo ("n/a": drawn as nothing).</summary>
    public const int largeammo = 1994;

    /// <summary>
    /// The status bar's graphics (st_stuff.c <c>ST_loadGraphics</c>, st_lib.c
    /// <c>STlib_init</c>'s minus): null where the WAD lacks the lump (the
    /// synthetic IWAD has <c>STBAR</c> only), which then draws nothing.
    /// </summary>
    public sealed class Graphics
    {
        public IndexedImage?[] tallnum = new IndexedImage?[10];
        public IndexedImage? tallpercent;
        public IndexedImage?[] shortnum = new IndexedImage?[10];
        public IndexedImage?[] keys = new IndexedImage?[(int)card_t.NUMCARDS];
        public IndexedImage? armsbg;
        /// <summary>The arms: <c>[i][0]</c> grey (<c>STGNUMi+2</c>), <c>[i][1]</c> yellow (the short number).</summary>
        public IndexedImage?[][] arms = new IndexedImage?[6][];
        public IndexedImage? faceback;
        public IndexedImage? sbar;
        public IndexedImage?[] faces = new IndexedImage?[ST_NUMFACES];
        public IndexedImage? sttminus;

        /// <summary>st_stuff.c <c>ST_loadUnloadGraphics</c>' lumps from <paramref name="wad"/> (the face background of player <paramref name="consoleplayer"/>).</summary>
        public static Graphics Load(WadArchive wad, int consoleplayer = 0)
        {
            IndexedImage? Get(string name)
            {
                WadLump? lump = wad.Find(name);
                if (lump is null)
                    return null;
                try
                {
                    return Patch.Decode(lump.Data.Span, lump.Name);
                }
                catch (WadFormatException)
                {
                    return null;
                }
            }

            var g = new Graphics();
            for (int i = 0; i < 10; i++)
            {
                g.tallnum[i] = Get($"STTNUM{i}");
                g.shortnum[i] = Get($"STYSNUM{i}");
            }
            g.tallpercent = Get("STTPRCNT");
            for (int i = 0; i < (int)card_t.NUMCARDS; i++)
                g.keys[i] = Get($"STKEYS{i}");
            g.armsbg = Get("STARMS");
            for (int i = 0; i < 6; i++)
                g.arms[i] = [Get($"STGNUM{i + 2}"), g.shortnum[i + 2]];
            g.faceback = Get($"STFB{consoleplayer}");
            g.sbar = Get("STBAR");
            int facenum = 0;
            for (int i = 0; i < ST_NUMPAINFACES; i++)
            {
                for (int j = 0; j < ST_NUMSTRAIGHTFACES; j++)
                    g.faces[facenum++] = Get($"STFST{i}{j}");
                g.faces[facenum++] = Get($"STFTR{i}0"); // turn right
                g.faces[facenum++] = Get($"STFTL{i}0"); // turn left
                g.faces[facenum++] = Get($"STFOUCH{i}"); // ouch!
                g.faces[facenum++] = Get($"STFEVL{i}"); // evil grin ;)
                g.faces[facenum++] = Get($"STFKILL{i}"); // pissed off
            }
            g.faces[facenum++] = Get("STFGOD0");
            g.faces[facenum] = Get("STFDEAD0");
            g.sttminus = Get("STTMINUS");
            return g;
        }
    }

    public StStuff(Graphics graphics, DoomRandom random)
    {
        G = graphics;
        Random = random;
    }

    /// <summary>The bar's graphics.</summary>
    public Graphics G { get; }

    /// <summary>
    /// <c>M_Random</c>'s index (m_random.c <c>rndindex</c>): the
    /// presentation's, cleared at a new game as vanilla's <c>G_InitNew</c>
    /// does (<see cref="DoomRandom.M_ClearRandom"/>), not at each level.
    /// </summary>
    public DoomRandom Random { get; }

    /// <summary>The bar as drawn by the last <see cref="ST_Drawer"/>: vanilla's screen rows <see cref="ST_Y"/>–199.</summary>
    public HudScreen Screen { get; } = new(ST_Y, ST_HEIGHT);

    /// <summary>st_stuff.c <c>st_backing_screen</c>: the bar's background.</summary>
    private readonly HudScreen st_backing_screen = new(ST_Y, ST_HEIGHT);

    // ---- st_stuff.c state ----

    /// <summary>The main player in game.</summary>
    public player_t? plyr;
    private bool st_deathmatch;
    private bool st_netgame;
    public int st_clock;
    public bool st_statusbaron;
    public bool st_notdeathmatch;
    public bool st_armson;
    public bool st_fragson;
    public int st_fragscount;
    /// <summary>Used to use the appropriately pained face.</summary>
    public int st_oldhealth = -1;
    /// <summary>Used for the evil grin.</summary>
    public readonly bool[] oldweaponsowned = new bool[(int)weapontype_t.NUMWEAPONS];
    /// <summary>Count until the face changes.</summary>
    public int st_facecount;
    /// <summary>The current face index, used by <c>w_faces</c>.</summary>
    public int st_faceindex;
    /// <summary>The key type for each key box on the bar (−1 none).</summary>
    public readonly int[] keyboxes = new int[3];
    /// <summary>A random number per tic.</summary>
    public int st_randomnumber;
    /// <summary><c>w_ready.num</c>'s value: the ready weapon's ammo, or <see cref="largeammo"/>.</summary>
    public int w_ready_num;

    // ST_calcPainOffset's and ST_updateFaceWidget's statics: never reset (not by ST_Start either), as vanilla's.
    private int lastcalc;
    private int oldhealth = -1;
    /// <summary>ST_updateFaceWidget's <c>lastattackdown</c>.</summary>
    public int lastattackdown = -1;
    /// <summary>ST_updateFaceWidget's <c>priority</c>.</summary>
    public int priority;

    /// <summary>
    /// st_stuff.c <c>ST_Start</c> (<c>ST_initData</c>, <c>ST_createWidgets</c>;
    /// the widgets read the player when drawn): for <paramref name="player"/>, the console
    /// player, at each level start (and, T6.12, reborn). The palette is the
    /// caller's (<see cref="ST_doPaletteStuff"/>).
    /// </summary>
    public void ST_Start(player_t player, bool netgame = false, int deathmatch = 0)
    {
        plyr = player;
        st_netgame = netgame;
        st_deathmatch = deathmatch != 0;
        st_clock = 0;
        st_statusbaron = true;
        st_faceindex = 0;
        st_oldhealth = -1;
        for (int i = 0; i < oldweaponsowned.Length; i++)
            oldweaponsowned[i] = player.weaponowned[i];
        for (int i = 0; i < 3; i++)
            keyboxes[i] = -1;
        st_firsttime = true;
        UpdateReady();
        ST_createWidgets();
    }

    /// <summary>st_stuff.c <c>ST_calcPainOffset</c>.</summary>
    public int ST_calcPainOffset()
    {
        int health = plyr!.health > 100 ? 100 : plyr.health;
        if (health != oldhealth)
        {
            lastcalc = ST_FACESTRIDE * (((100 - health) * ST_NUMPAINFACES) / 101);
            oldhealth = health;
        }
        return lastcalc;
    }

    /// <summary>
    /// st_stuff.c <c>ST_updateFaceWidget</c>: the face states and their
    /// timing; the precedence is dead &gt; evil grin &gt; turned head &gt;
    /// straight ahead. Vanilla's quirk kept: the "ouch" face shows when the
    /// health went <em>up</em> by more than <see cref="ST_MUCHPAIN"/> while
    /// hurt (<c>plyr->health - st_oldhealth</c>), so in practice only on the
    /// first tic of a level (<see cref="st_oldhealth"/> −1).
    /// </summary>
    public void ST_updateFaceWidget()
    {
        player_t p = plyr!;
        if (priority < 10)
        {
            // dead
            if (p.health == 0)
            {
                priority = 9;
                st_faceindex = ST_DEADFACE;
                st_facecount = 1;
            }
        }

        if (priority < 9)
        {
            if (p.bonuscount != 0)
            {
                // picking up bonus
                bool doevilgrin = false;
                for (int i = 0; i < (int)weapontype_t.NUMWEAPONS; i++)
                {
                    if (oldweaponsowned[i] != p.weaponowned[i])
                    {
                        doevilgrin = true;
                        oldweaponsowned[i] = p.weaponowned[i];
                    }
                }
                if (doevilgrin)
                {
                    // evil grin if just picked up weapon
                    priority = 8;
                    st_facecount = ST_EVILGRINCOUNT;
                    st_faceindex = ST_calcPainOffset() + ST_EVILGRINOFFSET;
                }
            }
        }

        if (priority < 8)
        {
            if (p.damagecount != 0 && p.attacker is not null && p.attacker != p.mo)
            {
                // being attacked
                priority = 7;
                if (p.health - st_oldhealth > ST_MUCHPAIN)
                {
                    st_facecount = ST_TURNCOUNT;
                    st_faceindex = ST_calcPainOffset() + ST_OUCHOFFSET;
                }
                else
                {
                    uint badguyangle = Tables.R_PointToAngle2(p.mo!.x, p.mo.y, p.attacker.x, p.attacker.y);
                    uint diffang;
                    bool i;
                    if (badguyangle > p.mo.angle)
                    {
                        // whether right or left
                        diffang = unchecked(badguyangle - p.mo.angle);
                        i = diffang > Tables.ANG180;
                    }
                    else
                    {
                        // whether left or right
                        diffang = unchecked(p.mo.angle - badguyangle);
                        i = diffang <= Tables.ANG180;
                    } // confusing, aint it?

                    st_facecount = ST_TURNCOUNT;
                    st_faceindex = ST_calcPainOffset();
                    if (diffang < Tables.ANG45)
                        st_faceindex += ST_RAMPAGEOFFSET; // head-on
                    else if (i)
                        st_faceindex += ST_TURNOFFSET; // turn face right
                    else
                        st_faceindex += ST_TURNOFFSET + 1; // turn face left
                }
            }
        }

        if (priority < 7)
        {
            // getting hurt because of your own damn stupidity
            if (p.damagecount != 0)
            {
                if (p.health - st_oldhealth > ST_MUCHPAIN)
                {
                    priority = 7;
                    st_facecount = ST_TURNCOUNT;
                    st_faceindex = ST_calcPainOffset() + ST_OUCHOFFSET;
                }
                else
                {
                    priority = 6;
                    st_facecount = ST_TURNCOUNT;
                    st_faceindex = ST_calcPainOffset() + ST_RAMPAGEOFFSET;
                }
            }
        }

        if (priority < 6)
        {
            // rapid firing
            if (p.attackdown)
            {
                if (lastattackdown == -1)
                    lastattackdown = ST_RAMPAGEDELAY;
                else if (--lastattackdown == 0)
                {
                    priority = 5;
                    st_faceindex = ST_calcPainOffset() + ST_RAMPAGEOFFSET;
                    st_facecount = 1;
                    lastattackdown = 1;
                }
            }
            else
                lastattackdown = -1;
        }

        if (priority < 5)
        {
            // invulnerability
            if ((p.cheats & player_t.CF_GODMODE) != 0 || p.powers[(int)powertype_t.pw_invulnerability] != 0)
            {
                priority = 4;
                st_faceindex = ST_GODFACE;
                st_facecount = 1;
            }
        }

        // look left or look right if the facecount has timed out
        if (st_facecount == 0)
        {
            st_faceindex = ST_calcPainOffset() + (st_randomnumber % 3);
            st_facecount = ST_STRAIGHTFACECOUNT;
            priority = 0;
        }

        st_facecount--;
    }

    private void UpdateReady()
    {
        ammotype_t ammo = Info.weaponinfo[(int)plyr!.readyweapon].ammo;
        w_ready_num = ammo == ammotype_t.am_noammo ? largeammo : plyr.ammo[(int)ammo];
    }

    /// <summary>st_stuff.c <c>ST_updateWidgets</c> (the chat parts left out: no chat).</summary>
    public void ST_updateWidgets()
    {
        player_t p = plyr!;
        // must redirect the pointer if the ready weapon has changed.
        UpdateReady();

        // update keycard multiple widgets
        for (int i = 0; i < 3; i++)
        {
            keyboxes[i] = p.cards[i] ? i : -1;
            if (p.cards[i + 3])
                keyboxes[i] = i + 3;
        }

        // refresh everything if this is him coming back to life
        ST_updateFaceWidget();

        // used by the w_armsbg widget
        st_notdeathmatch = !st_deathmatch;
        // used by w_arms[] widgets
        st_armson = st_statusbaron && !st_deathmatch;
        // used by w_frags widget
        st_fragson = st_deathmatch && st_statusbaron;
        st_fragscount = 0;
        for (int i = 0; i < World.MAXPLAYERS; i++)
        {
            // (vanilla compares i with consoleplayer: the frags of the bar's own player count against)
            if (i != ConsolePlayer)
                st_fragscount += p.frags[i];
            else
                st_fragscount -= p.frags[i];
        }
    }

    /// <summary>The console player's number (the frags sum; set by the caller, default 0).</summary>
    public int ConsolePlayer { get; set; }

    /// <summary>st_stuff.c <c>ST_Ticker</c>: once per tic, after the play simulation's.</summary>
    public void ST_Ticker()
    {
        if (plyr is null)
            return;
        st_clock++;
        st_randomnumber = Random.M_Random();
        ST_updateWidgets();
        st_oldhealth = plyr.health;
    }

    /// <summary>
    /// st_stuff.c <c>ST_doPaletteStuff</c>: the PLAYPAL palette for
    /// <paramref name="plyr"/>'s state (vanilla sets it with
    /// <c>I_SetPalette</c> when it changes; the caller does): red for
    /// <see cref="player_t.damagecount"/> (or the berserk's fading red,
    /// whichever is stronger), else gold for <see cref="player_t.bonuscount"/>,
    /// else green while the radiation suit lasts (blinking for its last 4 s),
    /// else 0. Chocolate Doom's Chex Quest branch (red shown as green) is
    /// left out: no Chex Quest IWAD is supported.
    /// </summary>
    public static int ST_doPaletteStuff(player_t plyr)
    {
        int palette;
        int cnt = plyr.damagecount;

        if (plyr.powers[(int)powertype_t.pw_strength] != 0)
        {
            // slowly fade the berzerk out
            int bzc = 12 - (plyr.powers[(int)powertype_t.pw_strength] >> 6);

            if (bzc > cnt)
                cnt = bzc;
        }

        if (cnt != 0)
        {
            palette = (cnt + 7) >> 3;

            if (palette >= NUMREDPALS)
                palette = NUMREDPALS - 1;

            palette += STARTREDPALS;
        }
        else if (plyr.bonuscount != 0)
        {
            palette = (plyr.bonuscount + 7) >> 3;

            if (palette >= NUMBONUSPALS)
                palette = NUMBONUSPALS - 1;

            palette += STARTBONUSPALS;
        }
        else if (plyr.powers[(int)powertype_t.pw_ironfeet] > 4 * 32
                 || (plyr.powers[(int)powertype_t.pw_ironfeet] & 8) != 0)
        {
            palette = RADIATIONPAL;
        }
        else
        {
            palette = 0;
        }

        return palette;
    }

    // ---- st_lib.c's widgets ----

    /// <summary>st_lib.h <c>st_number_t</c>: a right-aligned number of at most <see cref="width"/> digits; <see cref="num"/> reads its value.</summary>
    public sealed class st_number_t
    {
        public int x, y, width, oldnum;
        public Func<int> num = () => 0;
        public Func<bool> on = () => false;
        public IndexedImage?[] p = [];
    }

    /// <summary>st_lib.h <c>st_percent_t</c>: a number and a percent sign.</summary>
    public sealed class st_percent_t
    {
        public st_number_t n = new();
        public IndexedImage? p;
    }

    /// <summary>st_lib.h <c>st_multicon_t</c>: one icon of a list (none for −1), redrawn when it changes.</summary>
    public sealed class st_multicon_t
    {
        public int x, y, oldinum;
        public Func<int> inum = () => -1;
        public Func<bool> on = () => false;
        public IndexedImage?[] p = [];
    }

    /// <summary>st_lib.h <c>st_binicon_t</c>: an icon shown or not, redrawn when that changes.</summary>
    public sealed class st_binicon_t
    {
        public int x, y;
        public bool oldval;
        public Func<bool> val = () => false;
        public Func<bool> on = () => false;
        public IndexedImage? p;
    }

    /// <summary>st_stuff.c <c>st_firsttime</c>: <see cref="ST_Start"/> has just been called (the next draw refreshes).</summary>
    public bool st_firsttime;

    private st_number_t w_ready = new();
    private st_number_t w_frags = new();
    private st_percent_t w_health = new();
    private st_binicon_t w_armsbg = new();
    private readonly st_multicon_t[] w_arms = new st_multicon_t[6];
    private st_multicon_t w_faces = new();
    private readonly st_multicon_t[] w_keyboxes = new st_multicon_t[3];
    private st_percent_t w_armor = new();
    private readonly st_number_t[] w_ammo = new st_number_t[4];
    private readonly st_number_t[] w_maxammo = new st_number_t[4];

    private static st_number_t STlib_initNum(int x, int y, IndexedImage?[] pl, Func<int> num, Func<bool> on, int width) =>
        new() { x = x, y = y, oldnum = 0, width = width, num = num, on = on, p = pl };

    private static st_percent_t STlib_initPercent(int x, int y, IndexedImage?[] pl, Func<int> num, Func<bool> on, IndexedImage? percent) =>
        new() { n = STlib_initNum(x, y, pl, num, on, 3), p = percent };

    private static st_multicon_t STlib_initMultIcon(int x, int y, IndexedImage?[] il, Func<int> inum, Func<bool> on) =>
        new() { x = x, y = y, oldinum = -1, inum = inum, on = on, p = il };

    private static st_binicon_t STlib_initBinIcon(int x, int y, IndexedImage? i, Func<bool> val, Func<bool> on) =>
        new() { x = x, y = y, oldval = false, val = val, on = on, p = i };

    /// <summary>st_stuff.c <c>ST_createWidgets</c> (the widgets read the player's fields when drawn, as vanilla's pointers).</summary>
    private void ST_createWidgets()
    {
        player_t p = plyr!;
        // ready weapon ammo
        w_ready = STlib_initNum(ST_AMMOX, ST_AMMOY, G.tallnum, () => w_ready_num, () => st_statusbaron, ST_AMMOWIDTH);
        // health percentage
        w_health = STlib_initPercent(ST_HEALTHX, ST_HEALTHY, G.tallnum, () => p.health, () => st_statusbaron, G.tallpercent);
        // arms background
        w_armsbg = STlib_initBinIcon(ST_ARMSBGX, ST_ARMSBGY, G.armsbg, () => st_notdeathmatch, () => st_statusbaron);
        // weapons owned
        for (int i = 0; i < 6; i++)
        {
            int w = i + 1;
            w_arms[i] = STlib_initMultIcon(ST_ARMSX + (i % 3) * ST_ARMSXSPACE, ST_ARMSY + (i / 3) * ST_ARMSYSPACE, G.arms[i],
                () => p.weaponowned[w] ? 1 : 0, () => st_armson);
        }
        // frags sum
        w_frags = STlib_initNum(ST_FRAGSX, ST_FRAGSY, G.tallnum, () => st_fragscount, () => st_fragson, ST_FRAGSWIDTH);
        // faces
        w_faces = STlib_initMultIcon(ST_FACESX, ST_FACESY, G.faces, () => st_faceindex, () => st_statusbaron);
        // armor percentage - should be colored later
        w_armor = STlib_initPercent(ST_ARMORX, ST_ARMORY, G.tallnum, () => p.armorpoints, () => st_statusbaron, G.tallpercent);
        // keyboxes 0-2
        (int X, int Y)[] keys = [(ST_KEY0X, ST_KEY0Y), (ST_KEY1X, ST_KEY1Y), (ST_KEY2X, ST_KEY2Y)];
        for (int i = 0; i < 3; i++)
        {
            int k = i;
            w_keyboxes[i] = STlib_initMultIcon(keys[i].X, keys[i].Y, G.keys, () => keyboxes[k], () => st_statusbaron);
        }
        // ammo count (all four kinds), max ammo count (all four kinds)
        (int X, int Y)[] ammo = [(ST_AMMO0X, ST_AMMO0Y), (ST_AMMO1X, ST_AMMO1Y), (ST_AMMO2X, ST_AMMO2Y), (ST_AMMO3X, ST_AMMO3Y)];
        (int X, int Y)[] max = [(ST_MAXAMMO0X, ST_MAXAMMO0Y), (ST_MAXAMMO1X, ST_MAXAMMO1Y), (ST_MAXAMMO2X, ST_MAXAMMO2Y), (ST_MAXAMMO3X, ST_MAXAMMO3Y)];
        for (int i = 0; i < 4; i++)
        {
            int a = i;
            w_ammo[i] = STlib_initNum(ammo[i].X, ammo[i].Y, G.shortnum, () => p.ammo[a], () => st_statusbaron, ST_AMMO0WIDTH);
            w_maxammo[i] = STlib_initNum(max[i].X, max[i].Y, G.shortnum, () => p.maxammo[a], () => st_statusbaron, ST_MAXAMMO0WIDTH);
        }
    }

    /// <summary>st_lib.c <c>STlib_drawNum</c>: clears the number's area from the backing screen and draws the number (nothing for <see cref="largeammo"/>).</summary>
    private void STlib_drawNum(st_number_t n)
    {
        if (n.p[0] is not IndexedImage zero)
            return;
        int numdigits = n.width;
        int num = n.num();
        int w = zero.Width, h = zero.Height;
        n.oldnum = num;
        bool neg = num < 0;
        if (neg)
        {
            if (numdigits == 2 && num < -9)
                num = -9;
            else if (numdigits == 3 && num < -99)
                num = -99;
            num = -num;
        }

        // clear the area
        int x = n.x - numdigits * w;
        Screen.V_CopyRect(st_backing_screen, x, n.y, w * numdigits, h);

        // if non-number, do not draw it
        if (num == largeammo)
            return;

        x = n.x;
        // in the special case of 0, you draw 0
        if (num == 0)
            Screen.V_DrawPatch(x - w, n.y, n.p[0]);

        // draw the new number
        while (num != 0 && numdigits-- != 0)
        {
            x -= w;
            Screen.V_DrawPatch(x, n.y, n.p[num % 10]);
            num /= 10;
        }

        // draw a minus sign if necessary
        if (neg)
            Screen.V_DrawPatch(x - 8, n.y, G.sttminus);
    }

    /// <summary>st_lib.c <c>STlib_updateNum</c>.</summary>
    private void STlib_updateNum(st_number_t n)
    {
        if (n.on())
            STlib_drawNum(n);
    }

    /// <summary>st_lib.c <c>STlib_updatePercent</c>: the percent sign on a refresh, then the number.</summary>
    private void STlib_updatePercent(st_percent_t per, bool refresh)
    {
        if (refresh && per.n.on())
            Screen.V_DrawPatch(per.n.x, per.n.y, per.p);
        STlib_updateNum(per.n);
    }

    /// <summary>
    /// st_lib.c <c>STlib_updateMultIcon</c>: when the icon changed (or on a
    /// refresh) and is not −1, the old icon's rectangle back from the
    /// backing screen, then the new icon. Vanilla's quirks kept: the
    /// backing screen holds the bar alone (not the arms background drawn
    /// over it), so the transparent pixels of an arms number redrawn show
    /// the bar; an icon that becomes −1 stays drawn.
    /// </summary>
    private void STlib_updateMultIcon(st_multicon_t mi, bool refresh)
    {
        int inum = mi.inum();
        if (mi.on() && (mi.oldinum != inum || refresh) && inum != -1)
        {
            if (mi.oldinum != -1 && mi.p[mi.oldinum] is IndexedImage old)
                Screen.V_CopyRect(st_backing_screen, mi.x - old.LeftOffset, mi.y - old.TopOffset, old.Width, old.Height);
            Screen.V_DrawPatch(mi.x, mi.y, mi.p[inum]);
            mi.oldinum = inum;
        }
    }

    /// <summary>st_lib.c <c>STlib_updateBinIcon</c>: when the value changed (or on a refresh), the icon or the backing screen under it.</summary>
    private void STlib_updateBinIcon(st_binicon_t bi, bool refresh)
    {
        bool val = bi.val();
        if (bi.on() && (bi.oldval != val || refresh))
        {
            if (val)
                Screen.V_DrawPatch(bi.x, bi.y, bi.p);
            else if (bi.p is { } icon)
                Screen.V_CopyRect(st_backing_screen, bi.x - icon.LeftOffset, bi.y - icon.TopOffset, icon.Width, icon.Height);
            bi.oldval = val;
        }
    }

    /// <summary>st_stuff.c <c>ST_refreshBackground</c>: the bar (and in a netgame the face background) to the backing screen, copied to the screen.</summary>
    private void ST_refreshBackground()
    {
        if (st_statusbaron)
        {
            st_backing_screen.Clear();
            st_backing_screen.V_DrawPatch(ST_X, ST_Y, G.sbar);
            if (st_netgame)
                st_backing_screen.V_DrawPatch(ST_FX, ST_Y, G.faceback);
            Screen.V_CopyRect(st_backing_screen, ST_X, ST_Y, ST_WIDTH, ST_HEIGHT);
        }
    }

    /// <summary>st_stuff.c <c>ST_drawWidgets</c>.</summary>
    private void ST_drawWidgets(bool refresh)
    {
        // used by w_arms[] widgets
        st_armson = st_statusbaron && !st_deathmatch;
        // used by w_frags widget
        st_fragson = st_deathmatch && st_statusbaron;

        STlib_updateNum(w_ready);
        for (int i = 0; i < 4; i++)
        {
            STlib_updateNum(w_ammo[i]);
            STlib_updateNum(w_maxammo[i]);
        }
        STlib_updatePercent(w_health, refresh);
        STlib_updatePercent(w_armor, refresh);
        STlib_updateBinIcon(w_armsbg, refresh);
        for (int i = 0; i < 6; i++)
            STlib_updateMultIcon(w_arms[i], refresh);
        STlib_updateMultIcon(w_faces, refresh);
        for (int i = 0; i < 3; i++)
            STlib_updateMultIcon(w_keyboxes[i], refresh);
        STlib_updateNum(w_frags);
    }

    /// <summary>
    /// st_stuff.c <c>ST_Drawer(false, refresh)</c> without the palette (the
    /// caller's, <see cref="ST_doPaletteStuff"/>): the bar into
    /// <see cref="Screen"/>, which keeps what was drawn as vanilla's screen
    /// does: after <see cref="ST_Start"/> (or with <paramref name="refresh"/>,
    /// vanilla's <c>redrawsbar</c>: a menu or wipe over it) the whole bar,
    /// else only the numbers and the icons that changed. Drawing again
    /// without a tic in between changes nothing. See-through before the
    /// first <see cref="ST_Start"/>.
    /// </summary>
    public void ST_Drawer(bool refresh = false)
    {
        if (plyr is null)
            return;
        st_statusbaron = true;
        st_firsttime = st_firsttime || refresh;
        // If just after ST_Start(), refresh all
        if (st_firsttime)
        {
            st_firsttime = false;
            ST_refreshBackground();
            ST_drawWidgets(true);
        }
        // Otherwise, update as little as possible
        else
            ST_drawWidgets(false);
    }

    /// <summary>
    /// The minimal fullscreen HUD (T6.11, SPEC §7.6's option; not vanilla,
    /// which has none: its fullscreen view shows nothing) as drawn by the
    /// last <see cref="ST_DrawFullscreen"/>: the bar's widgets without the
    /// bar, over the view (see-through elsewhere), rows 168–199.
    /// </summary>
    public HudScreen FullScreen { get; } = new(ST_Y, ST_HEIGHT);

    /// <summary>The fullscreen HUD's places: health and armor at the left, the face in the middle, the keys and the ready weapon's ammo at the right.</summary>
    public const int FS_Y = 180, FS_HEALTHX = 52, FS_ARMORX = 124, FS_KEYSX = 238, FS_KEYSY = 186, FS_AMMOX = 316;

    /// <summary>
    /// Draws <see cref="FullScreen"/> from the state <see cref="ST_Ticker"/>
    /// keeps: the health and armor percentages and the ready weapon's ammo
    /// in the tall numbers (nothing for a weapon without ammo), the face
    /// where the bar has it, and the key boxes side by side.
    /// </summary>
    public void ST_DrawFullscreen()
    {
        HudScreen screen = FullScreen;
        screen.Clear();
        if (plyr is not { } p)
            return;
        void Number(int right, int num)
        {
            if (G.tallnum[0] is not IndexedImage zero)
                return;
            int x = right;
            bool neg = num < 0;
            num = Math.Min(Math.Abs(num), 999);
            do
            {
                x -= zero.Width;
                screen.V_DrawPatch(x, FS_Y, G.tallnum[num % 10]);
                num /= 10;
            }
            while (num != 0);
            if (neg)
                screen.V_DrawPatch(x - 8, FS_Y, G.sttminus);
        }
        Number(FS_HEALTHX, p.health);
        screen.V_DrawPatch(FS_HEALTHX, FS_Y, G.tallpercent);
        Number(FS_ARMORX, p.armorpoints);
        screen.V_DrawPatch(FS_ARMORX, FS_Y, G.tallpercent);
        if (st_faceindex >= 0 && st_faceindex < G.faces.Length)
            screen.V_DrawPatch(ST_FACESX, ST_FACESY, G.faces[st_faceindex]);
        for (int i = 0; i < 3; i++)
        {
            if (keyboxes[i] >= 0)
                screen.V_DrawPatch(FS_KEYSX + i * 10, FS_KEYSY, G.keys[keyboxes[i]]);
        }
        if (w_ready_num != largeammo)
            Number(FS_AMMOX, w_ready_num);
    }

    /// <summary>The faces' lump names in <see cref="st_faceindex"/> order (for the overlay and tests).</summary>
    public static string FaceName(int index)
    {
        if (index == ST_GODFACE)
            return "STFGOD0";
        if (index == ST_DEADFACE)
            return "STFDEAD0";
        int pain = index / ST_FACESTRIDE, k = index % ST_FACESTRIDE;
        return k switch
        {
            < ST_NUMSTRAIGHTFACES => $"STFST{pain}{k}",
            ST_TURNOFFSET => $"STFTR{pain}0",
            ST_TURNOFFSET + 1 => $"STFTL{pain}0",
            ST_OUCHOFFSET => $"STFOUCH{pain}",
            ST_EVILGRINOFFSET => $"STFEVL{pain}",
            _ => $"STFKILL{pain}",
        };
    }
}
