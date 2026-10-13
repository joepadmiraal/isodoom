using System;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

/// <summary>
/// am_map.c, the automap (T7.9, SPEC §7.6), as Chocolate Doom's (the
/// reference's; linuxdoom's but for the cheat's key): the level's lines in
/// vanilla's colours (one-sided red, a floor change brown, a ceiling change
/// yellow, teleporters dark red, secret doors as walls), only the lines seen
/// (<see cref="Line.ML_MAPPED"/>, which the fog of war's walk sets, T6.13l)
/// but those with <see cref="Line.ML_DONTDRAW"/>, the unseen ones grey with
/// the computer map (<see cref="powertype_t.pw_allmap"/>); the player's arrow,
/// follow mode, zoom, the whole map, panning, the grid and ten marks; and its
/// cheat <c>iddt</c> (every line, then the things too). Presentation only:
/// it reads the sim's world and changes nothing in it (its messages go to the
/// message line through <see cref="Message"/>; SPEC §12 T7.9). Draws palette
/// indices into a frame of <see cref="f_w"/> × <see cref="f_h"/> pixels
/// (<see cref="fb"/>), vanilla's 320 × 168 at the HUD's scale on a
/// 1280 × 800 window, wider on a wider one. No Godot types: the tests link it.
/// </summary>
public sealed class AmMap
{
    // For use if I do walls with outsides/insides
    public const int REDS = 256 - 5 * 16;
    public const int REDRANGE = 16;
    public const int BLUES = 256 - 4 * 16 + 8;
    public const int BLUERANGE = 8;
    public const int GREENS = 7 * 16;
    public const int GREENRANGE = 16;
    public const int GRAYS = 6 * 16;
    public const int GRAYSRANGE = 16;
    public const int BROWNS = 4 * 16;
    public const int BROWNRANGE = 16;
    public const int YELLOWS = 256 - 32 + 7;
    public const int YELLOWRANGE = 1;
    public const int BLACK = 0;
    public const int WHITE = 256 - 47;

    // Automap colors
    public const int BACKGROUND = BLACK;
    public const int YOURCOLORS = WHITE;
    public const int YOURRANGE = 0;
    public const int WALLCOLORS = REDS;
    public const int WALLRANGE = REDRANGE;
    public const int TSWALLCOLORS = GRAYS;
    public const int TSWALLRANGE = GRAYSRANGE;
    public const int FDWALLCOLORS = BROWNS;
    public const int FDWALLRANGE = BROWNRANGE;
    public const int CDWALLCOLORS = YELLOWS;
    public const int CDWALLRANGE = YELLOWRANGE;
    public const int THINGCOLORS = GREENS;
    public const int THINGRANGE = GREENRANGE;
    public const int SECRETWALLCOLORS = WALLCOLORS;
    public const int SECRETWALLRANGE = WALLRANGE;
    public const int GRIDCOLORS = GRAYS + GRAYSRANGE / 2;
    public const int GRIDRANGE = 0;
    public const int XHAIRCOLORS = GRAYS;

    // drawing stuff

    public const int AM_NUMMARKPOINTS = 10;

    /// <summary>scale on entry: <c>(fixed_t)(.2*FRACUNIT)</c>.</summary>
    public const int INITSCALEMTOF = 13107;

    /// <summary>how much the automap moves window per tic in frame-buffer coordinates; moves 140 pixels in 1 second.</summary>
    public const int F_PANINC = 4;

    /// <summary>how much zoom-in per tic; goes to 2x in 1 second: <c>(int)(1.02*FRACUNIT)</c>.</summary>
    public const int M_ZOOMIN = 66846;

    /// <summary>how much zoom-out per tic; pulls out to 0.5x in 1 second: <c>(int)(FRACUNIT/1.02)</c>.</summary>
    public const int M_ZOOMOUT = 64250;

    /// <summary>AM_LevelInit's <c>(int)(0.7*FRACUNIT)</c>.</summary>
    private const int SEVENTENTHS = 45875;

    /// <summary>p_local.h <c>PLAYERRADIUS</c>.</summary>
    public const int PLAYERRADIUS = 16 * Fixed.FRACUNIT;

    private const int FRACBITS = Fixed.FRACBITS, FRACUNIT = Fixed.FRACUNIT;

    // the following is crap
    private const short LINE_NEVERSEE = Line.ML_DONTDRAW;

    // d_englsh.h's messages
    public const string AMSTR_FOLLOWON = "Follow Mode ON";
    public const string AMSTR_FOLLOWOFF = "Follow Mode OFF";
    public const string AMSTR_GRIDON = "Grid ON";
    public const string AMSTR_GRIDOFF = "Grid OFF";
    public const string AMSTR_MARKEDSPOT = "Marked Spot";
    public const string AMSTR_MARKSCLEARED = "All Marks Cleared";

    /// <summary>d_event.h <c>evtype_t</c>'s key events (the responder's).</summary>
    public enum evtype_t { ev_keydown, ev_keyup }

    public struct fpoint_t
    {
        public int x, y;
    }

    public struct fline_t
    {
        public fpoint_t a, b;
    }

    public struct mpoint_t
    {
        public int x, y;
    }

    public struct mline_t
    {
        public mpoint_t a, b;

        public mline_t(int ax, int ay, int bx, int by)
        {
            a.x = ax;
            a.y = ay;
            b.x = bx;
            b.y = by;
        }
    }

    public struct islope_t
    {
        public int slp, islp;
    }

    //
    // The vector graphics for the automap.
    //  A line drawing of the player pointing right,
    //   starting from the middle.
    //
    private const int R = 8 * PLAYERRADIUS / 7;

    public static readonly mline_t[] player_arrow =
    [
        new(-R + R / 8, 0, R, 0), // -----
        new(R, 0, R - R / 2, R / 4), // ----->
        new(R, 0, R - R / 2, -R / 4),
        new(-R + R / 8, 0, -R - R / 8, R / 4), // >---->
        new(-R + R / 8, 0, -R - R / 8, -R / 4),
        new(-R + 3 * R / 8, 0, -R + R / 8, R / 4), // >>--->
        new(-R + 3 * R / 8, 0, -R + R / 8, -R / 4),
    ];

    public static readonly mline_t[] cheat_player_arrow =
    [
        new(-R + R / 8, 0, R, 0), // -----
        new(R, 0, R - R / 2, R / 6), // ----->
        new(R, 0, R - R / 2, -R / 6),
        new(-R + R / 8, 0, -R - R / 8, R / 6), // >----->
        new(-R + R / 8, 0, -R - R / 8, -R / 6),
        new(-R + 3 * R / 8, 0, -R + R / 8, R / 6), // >>----->
        new(-R + 3 * R / 8, 0, -R + R / 8, -R / 6),
        new(-R / 2, 0, -R / 2, -R / 6), // >>-d--->
        new(-R / 2, -R / 6, -R / 2 + R / 6, -R / 6),
        new(-R / 2 + R / 6, -R / 6, -R / 2 + R / 6, R / 4),
        new(-R / 6, 0, -R / 6, -R / 6), // >>-dd-->
        new(-R / 6, -R / 6, 0, -R / 6),
        new(0, -R / 6, 0, R / 4),
        new(R / 6, R / 4, R / 6, -R / 7), // >>-ddt->
        new(R / 6, -R / 7, R / 6 + R / 32, -R / 7 - R / 32),
        new(R / 6 + R / 32, -R / 7 - R / 32, R / 6 + R / 10, -R / 7),
    ];

    // (fixed_t)(-.867*R) etc. with R = FRACUNIT, truncated as C's casts
    public static readonly mline_t[] triangle_guy =
    [
        new(-56819, -32768, 56819, -32768),
        new(56819, -32768, 0, 65536),
        new(0, 65536, -56819, -32768),
    ];

    public static readonly mline_t[] thintriangle_guy =
    [
        new(-32768, -45875, 65536, 0),
        new(65536, 0, -32768, 45875),
        new(-32768, 45875, -32768, -45875),
    ];

    public int cheating;
    public int grid;

    private int leveljuststarted = 1; // kluge until AM_LevelInit() is called

    public bool automapactive;
    private int finit_width = HudScreen.SCREENWIDTH;
    private int finit_height = HudScreen.SCREENHEIGHT - 32;

    // location of window on screen
    public int f_x;
    public int f_y;

    // size of window on screen
    public int f_w;
    public int f_h;

    public int lightlev; // used for funky strobing effect

    /// <summary>pseudo-frame buffer: <see cref="f_w"/> × <see cref="f_h"/> palette indices (vanilla's <c>I_VideoBuffer</c>'s top rows).</summary>
    public HudScreen fb { get; private set; } = new(0, HudScreen.SCREENHEIGHT - 32, HudScreen.SCREENWIDTH);

    public int amclock;

    public mpoint_t m_paninc; // how far the window pans each tic (map coords)
    public int mtof_zoommul; // how far the window zooms in each tic (map coords)
    public int ftom_zoommul; // how far the window zooms in each tic (fb coords)

    public int m_x, m_y; // LL x,y where the window is on the map (map coords)
    public int m_x2, m_y2; // UR x,y where the window is on the map (map coords)

    //
    // width/height of window on map (map coords)
    //
    public int m_w;
    public int m_h;

    // based on level size
    public int min_x;
    public int min_y;
    public int max_x;
    public int max_y;

    public int max_w; // max_x-min_x,
    public int max_h; // max_y-min_y

    // based on player size
    public int min_w;
    public int min_h;

    public int min_scale_mtof; // used to tell when to stop zooming out
    public int max_scale_mtof; // used to tell when to stop zooming in

    // old stuff for recovery later
    private int old_m_w, old_m_h;
    private int old_m_x, old_m_y;

    // old location used by the Follower routine
    private mpoint_t f_oldloc;

    // used by MTOF to scale from map-to-frame-buffer coords
    public int scale_mtof = INITSCALEMTOF;

    // used by FTOM to scale from frame-buffer-to-map coords (=1/scale_mtof)
    public int scale_ftom;

    private player_t? plr; // the player represented by an arrow

    /// <summary>numbers used for marking by the automap (<c>AMMNUM0</c>–<c>9</c>; null where the WAD lacks one: drawn as nothing).</summary>
    public IndexedImage?[] marknums { get; } = new IndexedImage?[10];

    public readonly mpoint_t[] markpoints = new mpoint_t[AM_NUMMARKPOINTS]; // where the points are
    public int markpointnum; // next point to be assigned

    public int followplayer = 1; // specifies whether to follow the player around

    /// <summary>am_map.c's <c>cheat_amap</c>: <c>iddt</c>, scrambled (m_cheat.c's, T7.9a's reader).</summary>
    public readonly cheatseq_t cheat_amap = new(0xb2, 0x26, 0x26, 0x2e, 0xff);

    private bool stopped = true;

    // AM_Responder's statics
    private int bigstate;

    // AM_Start's statics
    private int lastlevel = -1, lastepisode = -1;

    // AM_updateLightLev's statics (the call is commented out in vanilla, as here)
    private int nexttic;
    private static readonly int[] litelevels = [0, 4, 7, 10, 12, 14, 15, 15];
    private int litelevelscnt;

    // ---- the game's side ----

    /// <summary>The level's world (its lines, vertexes, sectors and players): the game scene's, set before <see cref="AM_Start"/>.</summary>
    public World? World { get; set; }

    /// <summary>
    /// <c>plr-&gt;message = …</c>: the automap's messages (follow, grid,
    /// marks) for the console player's message line (the game scene's
    /// menus' way in, T7.2: <c>HU_Ticker</c> takes it at the next tic).
    /// </summary>
    public Action<string>? Message { get; set; }

    /// <summary>
    /// <c>ST_Responder(&amp;st_notify)</c>'s <c>AM_MSGENTERED</c>: the status
    /// bar redraws whole at the next <c>ST_Drawer</c> (<c>st_firsttime</c>).
    /// <c>AM_Stop</c>'s <c>AM_MSGEXITED</c> never arrives (its <c>event_t</c>'s
    /// fields are in the wrong order, vanilla's), so it has none.
    /// </summary>
    public Action? Entered { get; set; }

    /// <summary>
    /// The frame's pixels per vanilla screen pixel (T7.9, the high detail
    /// option: the window's pixels): the pan step (<see cref="F_PANINC"/>)
    /// and the marks scale with it, so the map pans as fast and the marks
    /// show as big as at vanilla's resolution. 1 (vanilla's) by default.
    /// </summary>
    public int PixelScale { get; set; } = 1;

    /// <summary>T7.9's overlay style: <see cref="AM_clearFB"/> leaves the frame see-through, the level shows under the lines (not vanilla).</summary>
    public bool Overlay { get; set; }

    // doomkeys.h's codes of Chocolate Doom's default automap keys (m_controls.c); the game scene's glue sends them for the actions
    public const int KEY_TAB = 9;
    public int key_map_north = MMenu.KEY_UPARROW;
    public int key_map_south = MMenu.KEY_DOWNARROW;
    public int key_map_east = MMenu.KEY_RIGHTARROW;
    public int key_map_west = MMenu.KEY_LEFTARROW;
    public int key_map_zoomin = '=';
    public int key_map_zoomout = '-';
    public int key_map_toggle = KEY_TAB;
    public int key_map_maxzoom = '0';
    public int key_map_follow = 'f';
    public int key_map_grid = 'g';
    public int key_map_mark = 'm';
    public int key_map_clearmark = 'c';

    public AmMap(WadArchive? wad = null)
    {
        if (wad is null)
            return;
        // AM_loadPics (vanilla loads them at each AM_Start)
        for (int i = 0; i < 10; i++)
        {
            if (wad.Find($"AMMNUM{i}") is not { } lump)
                continue;
            try
            {
                marknums[i] = Patch.Decode(lump.Data.Span, lump.Name);
            }
            catch (WadFormatException)
            {
            }
        }
    }

    // translates between frame-buffer and map distances
    private int FTOM(int x) => Fixed.FixedMul(x << 16, scale_ftom);
    private int MTOF(int x) => Fixed.FixedMul(x, scale_mtof) >> 16;

    // translates between frame-buffer and map coordinates
    private int CXMTOF(int x) => f_x + MTOF(x - m_x);
    private int CYMTOF(int y) => f_y + (f_h - MTOF(y - m_y));

    private player_t Plr => plr ?? throw new InvalidOperationException("The automap has no player (AM_Start first).");

    private int panInc => F_PANINC * PixelScale;

    // Calculates the slope and slope according to the x-axis of a line
    // segment in map coordinates (with the upright y-axis n' all) so
    // that it can be used with the brain-dead drawing stuff.
    public static void AM_getIslope(in mline_t ml, out islope_t @is)
    {
        int dy = ml.a.y - ml.b.y;
        int dx = ml.b.x - ml.a.x;
        @is = default;
        if (dy == 0)
            @is.islp = dx < 0 ? -int.MaxValue : int.MaxValue;
        else
            @is.islp = Fixed.FixedDiv(dx, dy);
        if (dx == 0)
            @is.slp = dy < 0 ? -int.MaxValue : int.MaxValue;
        else
            @is.slp = Fixed.FixedDiv(dy, dx);
    }

    public void AM_activateNewScale()
    {
        m_x += m_w / 2;
        m_y += m_h / 2;
        m_w = FTOM(f_w);
        m_h = FTOM(f_h);
        m_x -= m_w / 2;
        m_y -= m_h / 2;
        m_x2 = m_x + m_w;
        m_y2 = m_y + m_h;
    }

    public void AM_saveScaleAndLoc()
    {
        old_m_x = m_x;
        old_m_y = m_y;
        old_m_w = m_w;
        old_m_h = m_h;
    }

    public void AM_restoreScaleAndLoc()
    {
        m_w = old_m_w;
        m_h = old_m_h;
        if (followplayer == 0)
        {
            m_x = old_m_x;
            m_y = old_m_y;
        }
        else
        {
            m_x = Plr.mo!.x - m_w / 2;
            m_y = Plr.mo!.y - m_h / 2;
        }
        m_x2 = m_x + m_w;
        m_y2 = m_y + m_h;

        // Change the scaling multipliers
        scale_mtof = Fixed.FixedDiv(f_w << FRACBITS, m_w);
        scale_ftom = Fixed.FixedDiv(FRACUNIT, scale_mtof);
    }

    /// <summary>adds a marker at the current location</summary>
    public void AM_addMark()
    {
        markpoints[markpointnum].x = m_x + m_w / 2;
        markpoints[markpointnum].y = m_y + m_h / 2;
        markpointnum = (markpointnum + 1) % AM_NUMMARKPOINTS;
    }

    /// <summary>Determines bounding box of all vertices, sets global variables controlling zoom range.</summary>
    public void AM_findMinMaxBoundaries()
    {
        min_x = min_y = int.MaxValue;
        max_x = max_y = -int.MaxValue;

        foreach (Vertex v in World!.level.Vertexes)
        {
            if (v.X < min_x)
                min_x = v.X;
            else if (v.X > max_x)
                max_x = v.X;

            if (v.Y < min_y)
                min_y = v.Y;
            else if (v.Y > max_y)
                max_y = v.Y;
        }

        max_w = max_x - min_x;
        max_h = max_y - min_y;

        min_w = 2 * PLAYERRADIUS; // const? never changed?
        min_h = 2 * PLAYERRADIUS;

        int a = Fixed.FixedDiv(f_w << FRACBITS, max_w);
        int b = Fixed.FixedDiv(f_h << FRACBITS, max_h);

        min_scale_mtof = a < b ? a : b;
        max_scale_mtof = Fixed.FixedDiv(f_h << FRACBITS, 2 * PLAYERRADIUS);
    }

    public void AM_changeWindowLoc()
    {
        if (m_paninc.x != 0 || m_paninc.y != 0)
        {
            followplayer = 0;
            f_oldloc.x = int.MaxValue;
        }

        m_x += m_paninc.x;
        m_y += m_paninc.y;

        if (m_x + m_w / 2 > max_x)
            m_x = max_x - m_w / 2;
        else if (m_x + m_w / 2 < min_x)
            m_x = min_x - m_w / 2;

        if (m_y + m_h / 2 > max_y)
            m_y = max_y - m_h / 2;
        else if (m_y + m_h / 2 < min_y)
            m_y = min_y - m_h / 2;

        m_x2 = m_x + m_w;
        m_y2 = m_y + m_h;
    }

    public void AM_initVariables()
    {
        automapactive = true;

        f_oldloc.x = int.MaxValue;
        amclock = 0;
        lightlev = 0;

        m_paninc.x = m_paninc.y = 0;
        ftom_zoommul = FRACUNIT;
        mtof_zoommul = FRACUNIT;

        m_w = FTOM(f_w);
        m_h = FTOM(f_h);

        // find player to center on initially
        World world = World!;
        if (world.playeringame[world.consoleplayer])
            plr = world.players[world.consoleplayer];
        else
        {
            plr = world.players[0];
            for (int pnum = 0; pnum < World.MAXPLAYERS; pnum++)
            {
                if (world.playeringame[pnum])
                {
                    plr = world.players[pnum];
                    break;
                }
            }
        }

        m_x = Plr.mo!.x - m_w / 2;
        m_y = Plr.mo!.y - m_h / 2;
        AM_changeWindowLoc();

        // for saving & restoring
        old_m_x = m_x;
        old_m_y = m_y;
        old_m_w = m_w;
        old_m_h = m_h;

        // inform the status bar of the change
        Entered?.Invoke();
    }

    public void AM_clearMarks()
    {
        for (int i = 0; i < AM_NUMMARKPOINTS; i++)
            markpoints[i].x = -1; // means empty
        markpointnum = 0;
    }

    /// <summary>should be called at the start of every level; right now, i figure it out myself</summary>
    public void AM_LevelInit()
    {
        leveljuststarted = 0;

        f_x = f_y = 0;
        f_w = finit_width;
        f_h = finit_height;
        if (fb.Width != f_w || fb.Height != f_h)
            fb = new HudScreen(0, f_h, f_w);

        AM_clearMarks();

        AM_findMinMaxBoundaries();
        scale_mtof = Fixed.FixedDiv(min_scale_mtof, SEVENTENTHS);
        if (scale_mtof > max_scale_mtof)
            scale_mtof = min_scale_mtof;
        scale_ftom = Fixed.FixedDiv(FRACUNIT, scale_mtof);
    }

    public void AM_Stop()
    {
        automapactive = false;
        stopped = true;
    }

    public void AM_Start()
    {
        if (!stopped)
            AM_Stop();
        stopped = false;
        World world = World!;
        if (lastlevel != world.gamemap || lastepisode != world.gameepisode)
        {
            AM_LevelInit();
            lastlevel = world.gamemap;
            lastepisode = world.gameepisode;
        }
        AM_initVariables();
    }

    /// <summary>
    /// g_game.c's <c>automapactive = false</c> (<c>G_DoCompleted</c>'s
    /// intermission, <c>F_StartFinale</c>, <c>G_InitNew</c>): the automap
    /// goes without <see cref="AM_Stop"/>, as vanilla's (the next
    /// <see cref="AM_Start"/> stops it first).
    /// </summary>
    public void Deactivate() => automapactive = false;

    /// <summary>
    /// Not vanilla (T7.9b, the cheats page): types <paramref name="keys"/>
    /// into <see cref="AM_Responder"/> as keys bound to no automap action,
    /// <see cref="cheat_amap"/> started afresh first (keys typed before don't
    /// spoil it). Read only while the automap shows and not in deathmatch, as
    /// vanilla's. Returns whether <c>iddt</c> matched (<see cref="cheating"/> stepped).
    /// </summary>
    public bool TypeCheat(string keys)
    {
        if (!automapactive)
            return false;
        int before = cheating;
        cheat_amap.p = 0;
        foreach (char k in keys)
            AM_Responder(evtype_t.ev_keydown, 0, k);
        return cheating != before;
    }

    /// <summary>set the window scale to the maximum size</summary>
    public void AM_minOutWindowScale()
    {
        scale_mtof = min_scale_mtof;
        scale_ftom = Fixed.FixedDiv(FRACUNIT, scale_mtof);
        AM_activateNewScale();
    }

    /// <summary>set the window scale to the minimum size</summary>
    public void AM_maxOutWindowScale()
    {
        scale_mtof = max_scale_mtof;
        scale_ftom = Fixed.FixedDiv(FRACUNIT, scale_mtof);
        AM_activateNewScale();
    }

    /// <summary>
    /// Handle events (user inputs) in automap mode: <paramref name="key"/>
    /// is the event's key (<c>data1</c>: a <c>key_map_*</c> code for the
    /// automap's actions, 0 for any other key), <paramref name="ch"/> the
    /// character typed (<c>data2</c>, the cheat's; 0 for none). Returns
    /// whether the automap took the event (the game doesn't see it).
    /// </summary>
    public bool AM_Responder(evtype_t type, int key, int ch = 0)
    {
        bool rc = false;

        if (!automapactive)
        {
            if (type == evtype_t.ev_keydown && key == key_map_toggle)
            {
                AM_Start();
                rc = true;
            }
        }
        else if (type == evtype_t.ev_keydown)
        {
            rc = true;

            if (key == key_map_east) // pan right
            {
                if (followplayer == 0)
                    m_paninc.x = FTOM(panInc);
                else
                    rc = false;
            }
            else if (key == key_map_west) // pan left
            {
                if (followplayer == 0)
                    m_paninc.x = -FTOM(panInc);
                else
                    rc = false;
            }
            else if (key == key_map_north) // pan up
            {
                if (followplayer == 0)
                    m_paninc.y = FTOM(panInc);
                else
                    rc = false;
            }
            else if (key == key_map_south) // pan down
            {
                if (followplayer == 0)
                    m_paninc.y = -FTOM(panInc);
                else
                    rc = false;
            }
            else if (key == key_map_zoomout) // zoom out
            {
                mtof_zoommul = M_ZOOMOUT;
                ftom_zoommul = M_ZOOMIN;
            }
            else if (key == key_map_zoomin) // zoom in
            {
                mtof_zoommul = M_ZOOMIN;
                ftom_zoommul = M_ZOOMOUT;
            }
            else if (key == key_map_toggle)
            {
                bigstate = 0;
                AM_Stop();
            }
            else if (key == key_map_maxzoom)
            {
                bigstate = bigstate == 0 ? 1 : 0;
                if (bigstate != 0)
                {
                    AM_saveScaleAndLoc();
                    AM_minOutWindowScale();
                }
                else
                    AM_restoreScaleAndLoc();
            }
            else if (key == key_map_follow)
            {
                followplayer = followplayer == 0 ? 1 : 0;
                f_oldloc.x = int.MaxValue;
                Message?.Invoke(followplayer != 0 ? AMSTR_FOLLOWON : AMSTR_FOLLOWOFF);
            }
            else if (key == key_map_grid)
            {
                grid = grid == 0 ? 1 : 0;
                Message?.Invoke(grid != 0 ? AMSTR_GRIDON : AMSTR_GRIDOFF);
            }
            else if (key == key_map_mark)
            {
                Message?.Invoke($"{AMSTR_MARKEDSPOT} {markpointnum}");
                AM_addMark();
            }
            else if (key == key_map_clearmark)
            {
                AM_clearMarks();
                Message?.Invoke(AMSTR_MARKSCLEARED);
            }
            else
                rc = false;

            if (World!.settings.deathmatch == 0 && cheat_amap.cht_CheckCheat((byte)MCheat.KeyCode(ch)))
            {
                rc = false;
                cheating = (cheating + 1) % 3;
            }
        }
        else if (type == evtype_t.ev_keyup)
        {
            rc = false;

            if (key == key_map_east)
            {
                if (followplayer == 0)
                    m_paninc.x = 0;
            }
            else if (key == key_map_west)
            {
                if (followplayer == 0)
                    m_paninc.x = 0;
            }
            else if (key == key_map_north)
            {
                if (followplayer == 0)
                    m_paninc.y = 0;
            }
            else if (key == key_map_south)
            {
                if (followplayer == 0)
                    m_paninc.y = 0;
            }
            else if (key == key_map_zoomout || key == key_map_zoomin)
            {
                mtof_zoommul = FRACUNIT;
                ftom_zoommul = FRACUNIT;
            }
        }

        return rc;
    }

    /// <summary>Zooming</summary>
    public void AM_changeWindowScale()
    {
        // Change the scaling multipliers
        scale_mtof = Fixed.FixedMul(scale_mtof, mtof_zoommul);
        scale_ftom = Fixed.FixedDiv(FRACUNIT, scale_mtof);

        if (scale_mtof < min_scale_mtof)
            AM_minOutWindowScale();
        else if (scale_mtof > max_scale_mtof)
            AM_maxOutWindowScale();
        else
            AM_activateNewScale();
    }

    public void AM_doFollowPlayer()
    {
        mobj_t mo = Plr.mo!;
        if (f_oldloc.x != mo.x || f_oldloc.y != mo.y)
        {
            m_x = FTOM(MTOF(mo.x)) - m_w / 2;
            m_y = FTOM(MTOF(mo.y)) - m_h / 2;
            m_x2 = m_x + m_w;
            m_y2 = m_y + m_h;
            f_oldloc.x = mo.x;
            f_oldloc.y = mo.y;
        }
    }

    public void AM_updateLightLev()
    {
        // Change light level
        if (amclock > nexttic)
        {
            lightlev = litelevels[litelevelscnt++];
            if (litelevelscnt == litelevels.Length)
                litelevelscnt = 0;
            nexttic = amclock + 6 - (amclock % 6);
        }
    }

    /// <summary>Updates on Game Tick</summary>
    public void AM_Ticker()
    {
        if (!automapactive)
            return;

        amclock++;

        if (followplayer != 0)
            AM_doFollowPlayer();

        // Change the zoom if necessary
        if (ftom_zoommul != FRACUNIT)
            AM_changeWindowScale();

        // Change x,y location
        if (m_paninc.x != 0 || m_paninc.y != 0)
            AM_changeWindowLoc();

        // Update light level
        // AM_updateLightLev();
    }

    /// <summary>Clear automap frame buffer (T7.9's overlay style: see-through).</summary>
    public void AM_clearFB(int color)
    {
        Array.Fill(fb.Pixels, (byte)color);
        Array.Fill(fb.Opaque, Overlay ? (byte)0 : (byte)1);
    }

    private enum Outcode { LEFT = 1, RIGHT = 2, BOTTOM = 4, TOP = 8 }

    /// <summary>
    /// Automap clipping of lines. Based on Cohen-Sutherland clipping
    /// algorithm but with a slightly faster reject and precalculated slopes.
    /// If the speed is needed, use a hash algorithm to handle the common cases.
    /// </summary>
    public bool AM_clipMline(in mline_t ml, ref fline_t fl)
    {
        const int LEFT = (int)Outcode.LEFT, RIGHT = (int)Outcode.RIGHT, BOTTOM = (int)Outcode.BOTTOM, TOP = (int)Outcode.TOP;
        int outcode1 = 0;
        int outcode2 = 0;
        int outside;

        fpoint_t tmp = default;
        int dx;
        int dy;

        int DOOUTCODE(int mx, int my)
        {
            int oc = 0;
            if (my < 0)
                oc |= TOP;
            else if (my >= f_h)
                oc |= BOTTOM;
            if (mx < 0)
                oc |= LEFT;
            else if (mx >= f_w)
                oc |= RIGHT;
            return oc;
        }

        // do trivial rejects and outcodes
        if (ml.a.y > m_y2)
            outcode1 = TOP;
        else if (ml.a.y < m_y)
            outcode1 = BOTTOM;

        if (ml.b.y > m_y2)
            outcode2 = TOP;
        else if (ml.b.y < m_y)
            outcode2 = BOTTOM;

        if ((outcode1 & outcode2) != 0)
            return false; // trivially outside

        if (ml.a.x < m_x)
            outcode1 |= LEFT;
        else if (ml.a.x > m_x2)
            outcode1 |= RIGHT;

        if (ml.b.x < m_x)
            outcode2 |= LEFT;
        else if (ml.b.x > m_x2)
            outcode2 |= RIGHT;

        if ((outcode1 & outcode2) != 0)
            return false; // trivially outside

        // transform to frame-buffer coordinates.
        fl.a.x = CXMTOF(ml.a.x);
        fl.a.y = CYMTOF(ml.a.y);
        fl.b.x = CXMTOF(ml.b.x);
        fl.b.y = CYMTOF(ml.b.y);

        outcode1 = DOOUTCODE(fl.a.x, fl.a.y);
        outcode2 = DOOUTCODE(fl.b.x, fl.b.y);

        if ((outcode1 & outcode2) != 0)
            return false;

        while ((outcode1 | outcode2) != 0)
        {
            // may be partially inside box
            // find an outside point
            if (outcode1 != 0)
                outside = outcode1;
            else
                outside = outcode2;

            // clip to each side
            if ((outside & TOP) != 0)
            {
                dy = fl.a.y - fl.b.y;
                dx = fl.b.x - fl.a.x;
                tmp.x = fl.a.x + unchecked(dx * fl.a.y) / dy;
                tmp.y = 0;
            }
            else if ((outside & BOTTOM) != 0)
            {
                dy = fl.a.y - fl.b.y;
                dx = fl.b.x - fl.a.x;
                tmp.x = fl.a.x + unchecked(dx * (fl.a.y - f_h)) / dy;
                tmp.y = f_h - 1;
            }
            else if ((outside & RIGHT) != 0)
            {
                dy = fl.b.y - fl.a.y;
                dx = fl.b.x - fl.a.x;
                tmp.y = fl.a.y + unchecked(dy * (f_w - 1 - fl.a.x)) / dx;
                tmp.x = f_w - 1;
            }
            else if ((outside & LEFT) != 0)
            {
                dy = fl.b.y - fl.a.y;
                dx = fl.b.x - fl.a.x;
                tmp.y = fl.a.y + unchecked(dy * -fl.a.x) / dx;
                tmp.x = 0;
            }
            else
            {
                tmp.x = 0;
                tmp.y = 0;
            }

            if (outside == outcode1)
            {
                fl.a = tmp;
                outcode1 = DOOUTCODE(fl.a.x, fl.a.y);
            }
            else
            {
                fl.b = tmp;
                outcode2 = DOOUTCODE(fl.b.x, fl.b.y);
            }

            if ((outcode1 & outcode2) != 0)
                return false; // trivially outside
        }

        return true;
    }

    /// <summary>The lines <see cref="AM_drawFline"/> refused (out of the frame after clipping: vanilla's "fuck" count on stderr).</summary>
    public int fuck { get; private set; }

    /// <summary>Classic Bresenham w/ whatever optimizations needed for speed</summary>
    public void AM_drawFline(in fline_t fl, int color)
    {
        // For debugging only
        if (fl.a.x < 0 || fl.a.x >= f_w
            || fl.a.y < 0 || fl.a.y >= f_h
            || fl.b.x < 0 || fl.b.x >= f_w
            || fl.b.y < 0 || fl.b.y >= f_h)
        {
            fuck++;
            return;
        }

        byte[] pixels = fb.Pixels, opaque = fb.Opaque;
        void PUTDOT(int xx, int yy, int cc)
        {
            pixels[yy * f_w + xx] = (byte)cc;
            opaque[yy * f_w + xx] = 1;
        }

        int dx = fl.b.x - fl.a.x;
        int ax = 2 * (dx < 0 ? -dx : dx);
        int sx = dx < 0 ? -1 : 1;

        int dy = fl.b.y - fl.a.y;
        int ay = 2 * (dy < 0 ? -dy : dy);
        int sy = dy < 0 ? -1 : 1;

        int x = fl.a.x;
        int y = fl.a.y;
        int d;

        if (ax > ay)
        {
            d = ay - ax / 2;
            while (true)
            {
                PUTDOT(x, y, color);
                if (x == fl.b.x)
                    return;
                if (d >= 0)
                {
                    y += sy;
                    d -= ax;
                }
                x += sx;
                d += ay;
            }
        }
        else
        {
            d = ax - ay / 2;
            while (true)
            {
                PUTDOT(x, y, color);
                if (y == fl.b.y)
                    return;
                if (d >= 0)
                {
                    x += sx;
                    d -= ay;
                }
                y += sy;
                d += ax;
            }
        }
    }

    /// <summary>Clip lines, draw visible part sof lines.</summary>
    public void AM_drawMline(in mline_t ml, int color)
    {
        fline_t fl = default;
        if (AM_clipMline(ml, ref fl))
            AM_drawFline(fl, color); // draws it on frame buffer using fb coords
    }

    /// <summary>Draws flat (floor/ceiling tile) aligned grid lines.</summary>
    public void AM_drawGrid(int color)
    {
        World world = World!;
        const int units = Blockmap.MAPBLOCKUNITS << FRACBITS;

        // Figure out start of vertical gridlines
        int start = m_x;
        if ((start - world.bmaporgx) % units != 0)
            start += units - ((start - world.bmaporgx) % units);
        int end = m_x + m_w;

        // draw vertical gridlines
        mline_t ml = default;
        ml.a.y = m_y;
        ml.b.y = m_y + m_h;
        for (int x = start; x < end; x += units)
        {
            ml.a.x = x;
            ml.b.x = x;
            AM_drawMline(ml, color);
        }

        // Figure out start of horizontal gridlines
        start = m_y;
        if ((start - world.bmaporgy) % units != 0)
            start += units - ((start - world.bmaporgy) % units);
        end = m_y + m_h;

        // draw horizontal gridlines
        ml.a.x = m_x;
        ml.b.x = m_x + m_w;
        for (int y = start; y < end; y += units)
        {
            ml.a.y = y;
            ml.b.y = y;
            AM_drawMline(ml, color);
        }
    }

    /// <summary>
    /// The colour <see cref="AM_drawWalls"/> draws line <paramref name="i"/>
    /// in (its rules, apart from the clipping), or −1 when it isn't drawn.
    /// </summary>
    public int LineColor(int i)
    {
        line_t line = World!.lines[i];
        if (cheating != 0 || (line.flags & Line.ML_MAPPED) != 0)
        {
            if ((line.flags & LINE_NEVERSEE) != 0 && cheating == 0)
                return -1;
            if (line.backsector is null)
                return WALLCOLORS + lightlev;
            if (line.special == 39) // teleporters
                return WALLCOLORS + WALLRANGE / 2;
            if ((line.flags & Line.ML_SECRET) != 0) // secret door
                return cheating != 0 ? SECRETWALLCOLORS + lightlev : WALLCOLORS + lightlev;
            if (line.backsector.floorheight != line.frontsector!.floorheight)
                return FDWALLCOLORS + lightlev; // floor level change
            if (line.backsector.ceilingheight != line.frontsector.ceilingheight)
                return CDWALLCOLORS + lightlev; // ceiling level change
            if (cheating != 0)
                return TSWALLCOLORS + lightlev;
            return -1;
        }
        if (Plr.powers[(int)powertype_t.pw_allmap] != 0 && (line.flags & LINE_NEVERSEE) == 0)
            return GRAYS + 3;
        return -1;
    }

    /// <summary>Determines visible lines, draws them. This is LineDef based, not LineSeg based.</summary>
    public void AM_drawWalls()
    {
        line_t[] lines = World!.lines;
        for (int i = 0; i < lines.Length; i++)
        {
            int color = LineColor(i);
            if (color < 0)
                continue;
            AM_drawMline(new mline_t(lines[i].v1.X, lines[i].v1.Y, lines[i].v2.X, lines[i].v2.Y), color);
        }
    }

    /// <summary>Rotation in 2D. Used to rotate player arrow line character.</summary>
    public static void AM_rotate(ref int x, ref int y, uint a)
    {
        int tmpx = Fixed.FixedMul(x, Tables.finecosine[(int)(a >> Tables.ANGLETOFINESHIFT)])
            - Fixed.FixedMul(y, Tables.finesine[(int)(a >> Tables.ANGLETOFINESHIFT)]);

        y = Fixed.FixedMul(x, Tables.finesine[(int)(a >> Tables.ANGLETOFINESHIFT)])
            + Fixed.FixedMul(y, Tables.finecosine[(int)(a >> Tables.ANGLETOFINESHIFT)]);

        x = tmpx;
    }

    public void AM_drawLineCharacter(mline_t[] lineguy, int scale, uint angle, int color, int x, int y)
    {
        foreach (mline_t guy in lineguy)
        {
            int ax = guy.a.x, ay = guy.a.y;
            if (scale != 0)
            {
                ax = Fixed.FixedMul(scale, ax);
                ay = Fixed.FixedMul(scale, ay);
            }
            if (angle != 0)
                AM_rotate(ref ax, ref ay, angle);
            ax += x;
            ay += y;

            int bx = guy.b.x, by = guy.b.y;
            if (scale != 0)
            {
                bx = Fixed.FixedMul(scale, bx);
                by = Fixed.FixedMul(scale, by);
            }
            if (angle != 0)
                AM_rotate(ref bx, ref by, angle);
            bx += x;
            by += y;

            AM_drawMline(new mline_t(ax, ay, bx, by), color);
        }
    }

    private static readonly int[] their_colors = [GREENS, GRAYS, BROWNS, REDS];

    public void AM_drawPlayers()
    {
        World world = World!;
        player_t p0 = Plr;
        if (!world.netgame)
        {
            if (cheating != 0)
                AM_drawLineCharacter(cheat_player_arrow, 0, p0.mo!.angle, WHITE, p0.mo.x, p0.mo.y);
            else
                AM_drawLineCharacter(player_arrow, 0, p0.mo!.angle, WHITE, p0.mo.x, p0.mo.y);
            return;
        }

        int their_color = -1;
        for (int i = 0; i < World.MAXPLAYERS; i++)
        {
            their_color++;
            player_t p = world.players[i];

            if (world.settings.deathmatch != 0 && p != p0)
                continue;

            if (!world.playeringame[i] || p.mo is null)
                continue;

            int color = p.powers[(int)powertype_t.pw_invisibility] != 0 ? 246 // *close* to black
                : their_colors[their_color];

            AM_drawLineCharacter(player_arrow, 0, p.mo.angle, color, p.mo.x, p.mo.y);
        }
    }

    public void AM_drawThings(int colors, int colorrange)
    {
        foreach (sector_t sector in World!.sectors)
        {
            for (mobj_t? t = sector.thinglist; t is not null; t = t.snext)
                AM_drawLineCharacter(thintriangle_guy, 16 << FRACBITS, t.angle, colors + lightlev, t.x, t.y);
        }
    }

    public void AM_drawMarks()
    {
        int scale = PixelScale;
        for (int i = 0; i < AM_NUMMARKPOINTS; i++)
        {
            if (markpoints[i].x != -1)
            {
                //      w = SHORT(marknums[i]->width);
                //      h = SHORT(marknums[i]->height);
                int w = 5 * scale; // because something's wrong with the wad, i guess
                int h = 6 * scale; // because something's wrong with the wad, i guess
                int fx = CXMTOF(markpoints[i].x);
                int fy = CYMTOF(markpoints[i].y);
                if (fx >= f_x && fx <= f_w - w && fy >= f_y && fy <= f_h - h)
                    V_DrawPatch(fx, fy, marknums[i], scale);
            }
        }
    }

    // v_video.c V_DrawPatch into the frame, each patch pixel a square of scale pixels (T7.9's high detail); pixels outside dropped
    private void V_DrawPatch(int x, int y, IndexedImage? patch, int scale)
    {
        if (patch is null)
            return;
        x -= patch.LeftOffset * scale;
        y -= patch.TopOffset * scale;
        for (int py = 0; py < patch.Height * scale; py++)
        {
            int sy = y + py;
            if (sy < 0 || sy >= f_h)
                continue;
            for (int px = 0; px < patch.Width * scale; px++)
            {
                int sx = x + px;
                if (sx < 0 || sx >= f_w || !patch.IsOpaque(px / scale, py / scale))
                    continue;
                fb.Pixels[sy * f_w + sx] = patch[px / scale, py / scale];
                fb.Opaque[sy * f_w + sx] = 1;
            }
        }
    }

    public void AM_drawCrosshair(int color)
    {
        int i = (f_w * (f_h + 1)) / 2; // single point for now
        fb.Pixels[i] = (byte)color;
        fb.Opaque[i] = 1;
    }

    public void AM_Drawer()
    {
        if (!automapactive)
            return;

        AM_clearFB(BACKGROUND);
        if (grid != 0)
            AM_drawGrid(GRIDCOLORS);
        AM_drawWalls();
        AM_drawPlayers();
        if (cheating == 2)
            AM_drawThings(THINGCOLORS, THINGRANGE);
        AM_drawCrosshair(XHAIRCOLORS);

        AM_drawMarks();
    }

    // ---- not vanilla's: the window's size (T7.9) ----

    /// <summary>
    /// The frame's size, <paramref name="width"/> × <paramref name="height"/>
    /// (vanilla's <c>finit_width</c>, <c>finit_height</c>: 320 × 168), and
    /// <paramref name="pixelScale"/> (<see cref="PixelScale"/>). Vanilla's
    /// is fixed; here the window may change size: once a level was set up
    /// (<see cref="AM_LevelInit"/>) the zoom range is worked out again for
    /// the new frame, the scale kept within it and the window centred where it was.
    /// </summary>
    public void SetFrame(int width, int height, int pixelScale)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        PixelScale = Math.Max(pixelScale, 1);
        finit_width = width;
        finit_height = height;
        if (leveljuststarted != 0 || World is null || f_w == width && f_h == height)
            return;
        f_w = width;
        f_h = height;
        fb = new HudScreen(0, f_h, f_w);
        AM_findMinMaxBoundaries();
        if (scale_mtof < min_scale_mtof)
            scale_mtof = min_scale_mtof;
        else if (scale_mtof > max_scale_mtof)
            scale_mtof = max_scale_mtof;
        scale_ftom = Fixed.FixedDiv(FRACUNIT, scale_mtof);
        AM_activateNewScale();
    }

    /// <summary>The frame's FNV-1a hash (the reference dump's: every pixel's palette index, row by row).</summary>
    public uint Hash()
    {
        uint hash = 2166136261u;
        foreach (byte b in fb.Pixels)
            hash = unchecked((hash ^ b) * 16777619u);
        return hash;
    }

    /// <summary>The automap's state for the overlay and the level script.</summary>
    public string StateText()
    {
        if (!automapactive)
            return "automap: off (Tab)";
        int drawn = 0;
        if (World is { } world && plr is not null)
        {
            for (int i = 0; i < world.lines.Length; i++)
                drawn += LineColor(i) >= 0 ? 1 : 0;
        }
        int marks = Array.FindAll(markpoints, m => m.x != -1).Length;
        return FormattableString.Invariant(
            $"automap: on, {(followplayer != 0 ? "follow" : "free")}, {(grid != 0 ? "grid, " : "")}{(cheating != 0 ? $"iddt {cheating}, " : "")}{(bigstate != 0 ? "whole map, " : "")}")
            + FormattableString.Invariant($"scale {scale_mtof / 65536.0:0.###} px/unit, window ({m_x >> FRACBITS}, {m_y >> FRACBITS}) {m_w >> FRACBITS}x{m_h >> FRACBITS}, {drawn} lines drawn, {marks} marks, frame {f_w}x{f_h}");
    }
}
