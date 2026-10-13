using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using IsoDoom.Game;
using IsoDoom.Map;
using IsoDoom.Render;
using IsoDoom.Sim;
using IsoDoom.Tests.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;
using static IsoDoom.Game.AmMap;

namespace IsoDoom.Tests.Game;

/// <summary>
/// T7.9: am_map.c (<see cref="AmMap"/>) and the map title (hu_stuff.c's
/// <c>w_title</c>, <see cref="HuStuff.HU_DrawTitle"/>). Against vanilla: a
/// DOOM1.WAD route played in the sim and, at the dump's tics, the automap
/// opened, worked (the computer map, <c>iddt</c> twice, the grid and the
/// whole map, a zoom, follow off, a pan and two marks) and shot with the
/// keys <c>tools/VanillaRef/automap.sh</c> sends vanilla's <c>AM_Responder</c>,
/// on the lines vanilla had mapped; every frame's pixels (and the title's)
/// equal. Skips without DOOM1.WAD or the dump. Without a WAD: the line
/// colours and rules, the keys, the zoom limits, follow, the marks,
/// <c>iddt</c>, the clipping and the drawing, on the synthetic IWAD.
/// </summary>
public class AutomapTests
{
    // ---- against vanilla ----

    [Fact]
    public void MatchesVanillaAlongARoute()
    {
        const string Name = "e1m1-exit";
        var route = VanillaRoute.Load(Name);
        TestWads.RequireDoom1();
        string path = Path.Combine(VanillaRoute.DumpDir(), $"{Name}.am");
        if (!File.Exists(path))
            Assert.Skip($"No vanilla dump {path}: run tools/VanillaRef/automap.sh (or set {VanillaRoute.DumpDirEnvVar}).");
        string[] dump = [.. File.ReadAllLines(path).Where(l => l.Length > 0)];
        Assert.StartsWith("tics ", dump[0], StringComparison.Ordinal);
        var expected = new Dictionary<int, string[]>();
        foreach (string line in dump.Skip(1))
        {
            string[] w = line.Split(' ');
            expected[int.Parse(w[1], CultureInfo.InvariantCulture)] = w;
        }

        World world = route.NewWorld(out WadArchive wad);
        var am = new AmMap(wad) { World = world };
        var hu = new HuStuff(wad);
        hu.HU_Start(GameMission.doom, world.gameepisode, world.gamemap);
        var fog = new FogOfWar(world.level); // ours, to report how far its mapped lines are vanilla's
        fog.See(world.players[0].mo!.x, world.players[0].mo!.y);
        int shots = 0, vanillaMapped = 0, oursMissing = 0, oursExtra = 0;
        for (int tic = 0; tic < route.Cmds.Count && expected.Count > 0; tic++)
        {
            route.File.RunEvents(world, tic);
            world.G_Ticker(route.Cmds[tic]);
            fog.See(world.players[0].mo!.x, world.players[0].mo!.y);
            if (!expected.Remove(world.leveltime, out string[]? e))
                continue;
            mobj_t mo = world.players[0].mo!;
            Assert.Equal($"{e[2]} {e[3]} {e[4]}", $"{mo.x} {mo.y} {mo.angle}");

            // vanilla's mapped lines for the shots (its renderer's), ours kept aside
            short[] ours = [.. world.lines.Select(l => l.flags)];
            for (int i = 0; i < world.lines.Length; i++)
            {
                bool mapped = (Convert.ToInt32(e[5][i / 4].ToString(), 16) & (1 << (i % 4))) != 0;
                vanillaMapped += mapped ? 1 : 0;
                oursMissing += mapped && (ours[i] & Line.ML_MAPPED) == 0 ? 1 : 0;
                oursExtra += !mapped && (ours[i] & Line.ML_MAPPED) != 0 ? 1 : 0;
                world.lines[i].flags = (short)(mapped ? ours[i] | Line.ML_MAPPED : ours[i] & ~Line.ML_MAPPED);
            }

            var actual = new List<string>();
            void Shot(string what)
            {
                am.AM_Drawer();
                hu.HU_DrawTitle();
                actual.Add($"{what}:{Composed(am, hu):x8}");
            }
            Replay(am, world.players[0], Shot);
            Assert.Equal(e[6..], actual);
            shots += actual.Count;
            for (int i = 0; i < world.lines.Length; i++)
                world.lines[i].flags = ours[i];
        }
        Assert.True(expected.Count == 0, $"{Name}: the dump's tics {string.Join(',', expected.Keys)} were not reached");
        Assert.Equal(35, shots);
        // Ours (360°, every tic, the doors of the visible sectors) maps every line vanilla's 90° view drew, and more.
        Assert.True(oursMissing == 0, $"ours lacks {oursMissing} of the {vanillaMapped} lines vanilla mapped (extra {oursExtra})");
    }

    /// <summary>The keys dump.c's <c>dump_amtic</c> sends, with a shot where it takes one.</summary>
    private static void Replay(AmMap am, player_t p, Action<string> shot)
    {
        void Key(int key, bool down) => am.AM_Responder(down ? evtype_t.ev_keydown : evtype_t.ev_keyup, key, key);
        void Press(int key)
        {
            Key(key, true);
            Key(key, false);
        }
        void Hold(int key, int tics)
        {
            Key(key, true);
            for (int i = 0; i < tics; i++)
                am.AM_Ticker();
            Key(key, false);
        }
        void Type(string s)
        {
            foreach (char c in s)
                Press(c);
        }

        Press(KEY_TAB);
        shot("map");
        int allmap = p.powers[(int)powertype_t.pw_allmap];
        p.powers[(int)powertype_t.pw_allmap] = 1;
        shot("allmap");
        p.powers[(int)powertype_t.pw_allmap] = allmap;
        Type("iddt");
        shot("iddt1");
        Type("iddt");
        shot("iddt2");
        Press('g');
        Press('0');
        shot("whole");
        Press('0');
        Press('g');
        Hold('=', 10);
        shot("zoom");
        Press('f');
        Hold(MMenu.KEY_RIGHTARROW, 8);
        Press('m');
        Hold(MMenu.KEY_UPARROW, 5);
        Press('m');
        shot("pan");
        Hold('-', 10);
        Press('f');
        Type("iddt");
        Press(KEY_TAB);
    }

    /// <summary>The frame with the title over it (vanilla's screen rows 0–167), hashed as the dump does.</summary>
    private static uint Composed(AmMap am, HuStuff hu)
    {
        byte[] screen = (byte[])am.fb.Pixels.Clone();
        HudScreen t = hu.TitleScreen;
        for (int y = 0; y < t.Height && t.Top + y < am.f_h; y++)
        {
            for (int x = 0; x < t.Width; x++)
            {
                if (t.Opaque[y * t.Width + x] != 0)
                    screen[(t.Top + y) * am.f_w + x] = t.Pixels[y * t.Width + x];
            }
        }
        uint hash = 2166136261u;
        foreach (byte b in screen)
            hash = unchecked((hash ^ b) * 16777619u);
        return hash;
    }

    // ---- without a WAD: the synthetic IWAD ----

    private static (World World, AmMap Am, List<string> Messages) Synthetic(string map = "E1M1")
    {
        var wad = new WadArchive([WadFile.FromBytes(Tools.SyntheticIwad.SyntheticIwad.Build(), Tools.SyntheticIwad.SyntheticIwad.DefaultFileName)]);
        var world = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium, nomonsters: true), Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(wad, map));
        var messages = new List<string>();
        var am = new AmMap(wad) { World = world, Message = messages.Add };
        return (world, am, messages);
    }

    private static bool Down(AmMap am, int key, int ch = 0) => am.AM_Responder(evtype_t.ev_keydown, key, ch);

    private static bool Up(AmMap am, int key) => am.AM_Responder(evtype_t.ev_keyup, key);

    private static void Type(AmMap am, string s)
    {
        foreach (char c in s)
            Down(am, 0, c);
    }

    [Fact]
    public void TabOpensItCentredOnThePlayerAndTabClosesIt()
    {
        (World world, AmMap am, _) = Synthetic();
        bool entered = false;
        am.Entered = () => entered = true;
        Assert.False(Down(am, 'f')); // closed: only the toggle
        Assert.True(Down(am, KEY_TAB));
        Assert.True(am.automapactive && entered);
        Assert.Equal((320, 168), (am.f_w, am.f_h));
        mobj_t mo = world.players[0].mo!;
        Assert.Equal(mo.x - am.m_w / 2, am.m_x);
        Assert.Equal(mo.y - am.m_h / 2, am.m_y);
        // AM_LevelInit's scale: the whole map at 0.7, within the limits
        Assert.Equal(Fixed.FixedDiv(am.min_scale_mtof, 45875), am.scale_mtof);
        Assert.True(Down(am, KEY_TAB));
        Assert.False(am.automapactive);
    }

    [Fact]
    public void TheLinesShowAsVanillasRulesSay()
    {
        (World world, AmMap am, _) = Synthetic();
        Down(am, KEY_TAB);
        line_t[] lines = world.lines;
        foreach (line_t l in lines)
            l.flags &= ~Line.ML_MAPPED;
        Assert.All(Enumerable.Range(0, lines.Length), i => Assert.Equal(-1, am.LineColor(i))); // nothing seen

        int wall = Array.FindIndex(lines, l => l.backsector is null);
        int step = Array.FindIndex(lines, l => l.backsector is { } b && b.floorheight != l.frontsector!.floorheight);
        int ceiling = Array.FindIndex(lines, l => l.backsector is { } b && b.floorheight == l.frontsector!.floorheight
            && b.ceilingheight != l.frontsector.ceilingheight);
        Assert.True(wall >= 0 && step >= 0, $"synthetic lines: wall {wall}, step {step}");
        foreach (int i in new[] { wall, step })
            lines[i].flags |= Line.ML_MAPPED;
        Assert.Equal(WALLCOLORS, am.LineColor(wall));
        Assert.Equal(FDWALLCOLORS, am.LineColor(step));
        if (ceiling >= 0)
        {
            lines[ceiling].flags |= Line.ML_MAPPED;
            Assert.Equal(CDWALLCOLORS, am.LineColor(ceiling));
            lines[ceiling].flags &= ~Line.ML_MAPPED;
        }

        // a secret door shows as a wall (iddt: still a wall's colour, SECRETWALLCOLORS); a teleporter dark red
        lines[step].flags |= Line.ML_SECRET;
        Assert.Equal(WALLCOLORS, am.LineColor(step));
        lines[step].flags &= ~Line.ML_SECRET;
        short special = lines[step].special;
        lines[step].special = 39;
        Assert.Equal(WALLCOLORS + WALLRANGE / 2, am.LineColor(step));
        lines[step].special = special;

        // never seen: ML_DONTDRAW, but iddt shows it
        lines[wall].flags |= Line.ML_DONTDRAW;
        Assert.Equal(-1, am.LineColor(wall));

        // the computer map: the unseen lines grey (not ML_DONTDRAW's)
        player_t p = world.players[0];
        p.powers[(int)powertype_t.pw_allmap] = 1;
        int unseen = Array.FindIndex(lines, l => (l.flags & (Line.ML_MAPPED | Line.ML_DONTDRAW)) == 0);
        Assert.Equal(GRAYS + 3, am.LineColor(unseen));
        Assert.Equal(-1, am.LineColor(wall));
        Assert.Equal(FDWALLCOLORS, am.LineColor(step)); // seen ones in their colours
        p.powers[(int)powertype_t.pw_allmap] = 0;

        // the same heights on both sides: only with the cheat
        sector_t back = lines[step].backsector!, front = lines[step].frontsector!;
        (back.floorheight, back.ceilingheight) = (front.floorheight, front.ceilingheight);
        int flat = step;
        Assert.Equal(-1, am.LineColor(flat));

        // iddt: every line, the same heights grey, ML_DONTDRAW too
        Type(am, "iddt");
        Assert.Equal(1, am.cheating);
        Assert.Equal(TSWALLCOLORS, am.LineColor(flat));
        Assert.Equal(WALLCOLORS, am.LineColor(wall));
        Assert.All(Enumerable.Range(0, lines.Length), i => Assert.True(am.LineColor(i) >= 0));
    }

    [Fact]
    public void IddtCyclesThroughItsThreeStepsAndThingsShowAtTheSecond()
    {
        (_, AmMap am, _) = Synthetic();
        Type(am, "iddt");
        Assert.Equal(0, am.cheating); // closed: the cheat isn't read
        Down(am, KEY_TAB);
        Assert.False(Down(am, 0, 'i')); // the cheat's keys are not the automap's
        Type(am, "ddt");
        Assert.Equal(1, am.cheating);
        am.AM_Drawer();
        uint one = am.Hash();
        Type(am, "iddt");
        Assert.Equal(2, am.cheating);
        am.AM_Drawer();
        Assert.NotEqual(one, am.Hash()); // the things' triangles, green
        Assert.Contains((byte)THINGCOLORS, am.fb.Pixels);
        Type(am, "iddt");
        Assert.Equal(0, am.cheating);
        Type(am, "idxdt"); // a wrong key starts it again
        Assert.Equal(0, am.cheating);
    }

    [Fact]
    public void ZoomStopsAtItsLimitsAndTheWholeMapComesBack()
    {
        (_, AmMap am, _) = Synthetic();
        Down(am, KEY_TAB);
        Assert.True(Down(am, '='));
        for (int i = 0; i < 400; i++)
            am.AM_Ticker();
        Assert.Equal(am.max_scale_mtof, am.scale_mtof);
        Assert.False(Up(am, '='));
        int scale = am.scale_mtof;
        am.AM_Ticker();
        Assert.Equal(scale, am.scale_mtof); // the key let go: no zoom
        Down(am, '-');
        for (int i = 0; i < 400; i++)
            am.AM_Ticker();
        Assert.Equal(am.min_scale_mtof, am.scale_mtof);
        Up(am, '-');
        Down(am, '=');
        for (int i = 0; i < 20; i++)
            am.AM_Ticker();
        Up(am, '=');
        scale = am.scale_mtof;
        Down(am, '0'); // the whole map
        Assert.Equal(am.min_scale_mtof, am.scale_mtof);
        Down(am, '0'); // and back
        Assert.Equal(scale, am.scale_mtof);
    }

    [Fact]
    public void ThePanKeysAreTheGamesWhileFollowingAndPanOtherwise()
    {
        (World world, AmMap am, List<string> messages) = Synthetic();
        Down(am, KEY_TAB);
        Assert.False(Down(am, MMenu.KEY_RIGHTARROW)); // following: the game turns
        Assert.True(Down(am, 'f'));
        Assert.Equal(AMSTR_FOLLOWOFF, messages[^1]);
        int x = am.m_x;
        Assert.True(Down(am, MMenu.KEY_RIGHTARROW));
        am.AM_Ticker();
        Assert.True(am.m_x > x);
        Up(am, MMenu.KEY_RIGHTARROW);
        x = am.m_x;
        am.AM_Ticker();
        Assert.Equal(x, am.m_x);
        // panning stops at the map's edge (the window's centre within the vertexes' box)
        Down(am, MMenu.KEY_LEFTARROW);
        for (int i = 0; i < 2000; i++)
            am.AM_Ticker();
        Assert.Equal(am.min_x, am.m_x + am.m_w / 2);
        Up(am, MMenu.KEY_LEFTARROW);
        Down(am, 'f');
        Assert.Equal(AMSTR_FOLLOWON, messages[^1]);
        am.AM_Ticker(); // follow: back on the player
        mobj_t mo = world.players[0].mo!;
        Assert.InRange(am.m_x + am.m_w / 2 - mo.x, -2 * am.scale_ftom, 2 * am.scale_ftom);
    }

    [Fact]
    public void MarksAndTheGridHaveVanillasMessages()
    {
        (_, AmMap am, List<string> messages) = Synthetic();
        Down(am, KEY_TAB);
        Down(am, 'm');
        Assert.Equal("Marked Spot 0", messages[^1]);
        Down(am, 'm');
        Assert.Equal("Marked Spot 1", messages[^1]);
        Assert.Equal(am.m_x + am.m_w / 2, am.markpoints[0].x);
        Assert.Equal(2, am.markpointnum);
        Down(am, 'c');
        Assert.Equal(AMSTR_MARKSCLEARED, messages[^1]);
        Assert.All(am.markpoints, m => Assert.Equal(-1, m.x));
        Down(am, 'g');
        Assert.Equal(AMSTR_GRIDON, messages[^1]);
        am.AM_Drawer();
        Assert.Contains((byte)GRIDCOLORS, am.fb.Pixels);
        Down(am, 'g');
        Assert.Equal(AMSTR_GRIDOFF, messages[^1]);
        // ten marks, then the first again
        for (int i = 0; i < 11; i++)
            Down(am, 'm');
        Assert.Equal("Marked Spot 0", messages[^1]);
        Assert.Equal(1, am.markpointnum);
    }

    [Fact]
    public void TheMarksAndTheLevelStayWhileTheMapIsTheSame()
    {
        (World world, AmMap am, _) = Synthetic();
        Down(am, KEY_TAB);
        Down(am, 'm');
        Down(am, '=');
        for (int i = 0; i < 10; i++)
            am.AM_Ticker();
        Up(am, '=');
        int scale = am.scale_mtof;
        Down(am, KEY_TAB);
        Down(am, KEY_TAB); // AM_Start on the same map: no AM_LevelInit
        Assert.Equal(scale, am.scale_mtof);
        Assert.NotEqual(-1, am.markpoints[0].x);
        // another map: set up afresh
        (World other, _, _) = Synthetic("E1M2");
        am.World = other;
        am.AM_Stop();
        Down(am, KEY_TAB);
        Assert.Equal(-1, am.markpoints[0].x);
        Assert.Equal(Fixed.FixedDiv(am.min_scale_mtof, 45875) is int s && s > am.max_scale_mtof ? am.min_scale_mtof : Fixed.FixedDiv(am.min_scale_mtof, 45875), am.scale_mtof);
        Assert.NotSame(world, other);
    }

    [Fact]
    public void ItDrawsThePlayersArrowTheCrosshairAndClipsToTheFrame()
    {
        (World world, AmMap am, _) = Synthetic();
        foreach (line_t l in world.lines)
            l.flags &= ~Line.ML_MAPPED;
        Down(am, KEY_TAB);
        am.AM_Drawer();
        Assert.Equal((byte)XHAIRCOLORS, am.fb.Pixels[(am.f_w * (am.f_h + 1)) / 2]);
        Assert.Contains((byte)WHITE, am.fb.Pixels); // the arrow
        Assert.DoesNotContain((byte)WALLCOLORS, am.fb.Pixels); // nothing mapped
        Assert.All(am.fb.Opaque, o => Assert.Equal(1, o)); // vanilla's: a black frame
        am.Overlay = true;
        am.AM_Drawer();
        Assert.Equal(0, am.fb.Opaque[0]); // the overlay style: see-through but the lines
        Assert.Equal(1, am.fb.Opaque[(am.f_w * (am.f_h + 1)) / 2]);
        am.Overlay = false;

        // a line clipped at the frame's edges, Bresenham from end to end
        var fl = default(fline_t);
        int ml = am.m_x + am.m_w / 2;
        Assert.True(am.AM_clipMline(new mline_t(ml, am.m_y - (1 << 24), ml, am.m_y2 + (1 << 24)), ref fl));
        Assert.Equal((0, am.f_h - 1), (fl.a.y < fl.b.y ? fl.a.y : fl.b.y, fl.a.y < fl.b.y ? fl.b.y : fl.a.y));
        Assert.False(am.AM_clipMline(new mline_t(am.m_x - (1 << 24), am.m_y, am.m_x - (1 << 23), am.m_y2), ref fl)); // left of it
        am.AM_clearFB(BACKGROUND);
        am.AM_drawFline(new fline_t { a = new fpoint_t { x = 0, y = 0 }, b = new fpoint_t { x = 9, y = 3 } }, 1);
        Assert.Equal(10, am.fb.Pixels.Count(b => b == 1));
        Assert.Equal(1, am.fb.Pixels[3 * am.f_w + 9]);
        int refused = am.fuck;
        am.AM_drawFline(new fline_t { a = new fpoint_t { x = -1, y = 0 }, b = new fpoint_t { x = 9, y = 3 } }, 1);
        Assert.Equal(refused + 1, am.fuck);
    }

    [Fact]
    public void AWiderFrameKeepsTheZoomRangeAndTheCentre()
    {
        (_, AmMap am, _) = Synthetic();
        Down(am, KEY_TAB);
        int cx = am.m_x + am.m_w / 2, cy = am.m_y + am.m_h / 2;
        am.SetFrame(384, 184, 1);
        Assert.Equal((384, 184), (am.fb.Width, am.fb.Height));
        Assert.InRange(am.m_x + am.m_w / 2 - cx, -1, 1);
        Assert.InRange(am.m_y + am.m_h / 2 - cy, -1, 1);
        Assert.InRange(am.scale_mtof, am.min_scale_mtof, am.max_scale_mtof);
        am.AM_Drawer();
        Assert.Equal((byte)XHAIRCOLORS, am.fb.Pixels[(384 * 185) / 2]);
    }

    [Fact]
    public void NoIddtInDeathmatch()
    {
        (_, AmMap am, _) = Synthetic();
        Down(am, KEY_TAB);
        // AM_Responder reads deathmatch only (deathmatch spawning is not ported: no level)
        am.World = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium, deathmatch: 1, nomonsters: true), Tweaks.Vanilla);
        Type(am, "iddt");
        Assert.Equal(0, am.cheating);
    }

    // ---- T7.9c: turned to the game camera ----

    // The game camera's turn: it looks north-west (map angle 135°), so the map turns by −45°.
    private const uint CameraTurn = unchecked(0u - Tables.ANG45);

    // Whether a white pixel (the arrow's) is within a pixel of (x, y).
    private static bool WhiteNear(AmMap am, int x, int y)
    {
        for (int yy = y - 1; yy <= y + 1; yy++)
        {
            for (int xx = x - 1; xx <= x + 1; xx++)
            {
                if (am.fb.Pixels[yy * am.f_w + xx] == WHITE)
                    return true;
            }
        }
        return false;
    }

    [Fact]
    public void TurnedThePlayersArrowPointsUpTheScreenAlongTheCamerasView()
    {
        (World world, AmMap am, _) = Synthetic();
        foreach (line_t l in world.lines)
            l.flags &= ~Line.ML_MAPPED;
        mobj_t mo = world.players[0].mo!;
        mo.angle = Tables.ANG90 + Tables.ANG45; // facing north-west, as the camera looks
        Down(am, KEY_TAB);
        for (int i = 0; i < 200; i++)
            am.AM_Ticker();
        Down(am, '=');
        for (int i = 0; i < 400; i++)
            am.AM_Ticker(); // zoomed in all the way
        Up(am, '=');
        Down(am, '-');
        for (int i = 0; i < 70; i++)
            am.AM_Ticker(); // and out to a quarter: an arrow about 45 pixels long, within the frame
        Up(am, '-');

        // the arrow's tip, R ahead of the player: north up, up and left of the centre; turned, straight above it
        int tip = 8 * PLAYERRADIUS / 7;
        int Px(int units) => (int)((long)units * am.scale_mtof >> 32);
        int d = Px((int)(tip * 0.7071));
        am.AM_Drawer();
        Assert.True(WhiteNear(am, am.f_w / 2 - d, am.f_h / 2 - d), "north up: no tip up and left");
        Assert.False(WhiteNear(am, am.f_w / 2, am.f_h / 2 - Px(tip)), "north up: a tip straight up");

        am.Rotation = CameraTurn;
        Assert.Contains("turned 315°", am.StateText(), StringComparison.Ordinal);
        am.AM_Drawer();
        Assert.True(WhiteNear(am, am.f_w / 2, am.f_h / 2 - Px(tip)), "turned: no tip straight up");
        Assert.False(WhiteNear(am, am.f_w / 2 - d, am.f_h / 2 - d), "turned: a tip up and left");

        // a point north-west of the centre turns to straight up; the centre stays
        int cx = am.m_x + am.m_w / 2, cy = am.m_y + am.m_h / 2;
        int x = cx - (100 << 16), y = cy + (100 << 16);
        am.TurnPoint(ref x, ref y);
        Assert.InRange(x - cx, -6554, 6554); // within 0.1 unit: the tables sample half a step off
        Assert.InRange(y - cy, (int)(141.32 * 65536), (int)(141.52 * 65536));
        x = cx;
        y = cy;
        am.TurnPoint(ref x, ref y);
        Assert.Equal((cx, cy), (x, y));
    }

    [Fact]
    public void TurnedThePanKeysPanAlongTheScreen()
    {
        (_, AmMap am, _) = Synthetic();
        am.Rotation = CameraTurn;
        Down(am, KEY_TAB);
        Down(am, 'f');
        int x = am.m_x, y = am.m_y;
        Down(am, MMenu.KEY_UPARROW); // up the screen: north-west on the map
        am.AM_Ticker();
        Up(am, MMenu.KEY_UPARROW);
        int dx = am.m_x - x, dy = am.m_y - y;
        Assert.True(dx < 0 && dy > 0, $"pan ({dx}, {dy}), paninc {am.m_paninc.y}");
        Assert.True(Math.Abs(dx + dy) <= -dx / 500, $"pan ({dx}, {dy})"); // the tables' rounding
    }

    [Fact]
    public void TurnedTheWholeMapFitsTheTurnedLevel()
    {
        (World world, AmMap am, _) = Synthetic();
        Down(am, KEY_TAB);
        int north = am.min_scale_mtof;
        am.Rotation = CameraTurn; // changed while open: the zoom range again
        Assert.NotEqual(north, am.min_scale_mtof);
        Assert.InRange(am.scale_mtof, am.min_scale_mtof, am.max_scale_mtof);
        Down(am, '0');
        Assert.Equal(am.min_scale_mtof, am.scale_mtof);
        // every vertex turned spans no more than the frame at the whole map's scale
        double cos = Math.Cos(-Math.PI / 4), sin = Math.Sin(-Math.PI / 4);
        double[] xs = [.. world.level.Vertexes.Select(v => v.X * cos - v.Y * sin)];
        double[] ys = [.. world.level.Vertexes.Select(v => v.X * sin + v.Y * cos)];
        double scale = am.scale_mtof / 65536.0;
        Assert.True((xs.Max() - xs.Min()) * scale <= am.f_w * 65536 + 65536 && (ys.Max() - ys.Min()) * scale <= am.f_h * 65536 + 65536);
        am.Rotation = 0;
        Assert.Equal(north, am.min_scale_mtof);
    }

    [Fact]
    public void TurnedTheGridCoversTheFrame()
    {
        (_, AmMap am, _) = Synthetic();
        am.Rotation = CameraTurn;
        Down(am, KEY_TAB);
        Down(am, 'g');
        am.AM_Drawer();
        // a grid line in each corner's quarter of the frame
        foreach ((int x0, int y0) in new[] { (0, 0), (am.f_w * 3 / 4, 0), (0, am.f_h * 3 / 4), (am.f_w * 3 / 4, am.f_h * 3 / 4) })
        {
            bool found = false;
            for (int y = y0; y < y0 + am.f_h / 4 && !found; y++)
            {
                for (int x = x0; x < x0 + am.f_w / 4 && !found; x++)
                    found = am.fb.Pixels[y * am.f_w + x] == GRIDCOLORS;
            }
            Assert.True(found, $"no grid near ({x0}, {y0})");
        }
    }

    [Fact]
    public void TurnedTheMarksTurnWithTheMap()
    {
        (World world, AmMap am, _) = Synthetic();
        foreach (line_t l in world.lines)
            l.flags &= ~Line.ML_MAPPED;
        am.marknums[0] = new IndexedImage(5, 6, 0, 0, Enumerable.Repeat((byte)WHITE, 30).ToArray(), Enumerable.Repeat((byte)1, 30).ToArray()); // the synthetic IWAD has no AMMNUM0
        am.Rotation = CameraTurn;
        am.Overlay = true; // see-through: only what is drawn is opaque
        Down(am, KEY_TAB);
        Down(am, 'm');
        int cx = am.m_x + am.m_w / 2, cy = am.m_y + am.m_h / 2;
        int far = am.FTOM(60);
        am.markpoints[0].x = cx - far; // 60 pixels west of the centre
        am.markpoints[0].y = cy;
        int x = am.markpoints[0].x, y = am.markpoints[0].y;
        am.TurnPoint(ref x, ref y);
        Assert.True(Math.Abs(x - cx + (y - cy)) < (cx - x) / 500, $"({x - cx}, {y - cy})"); // west turned clockwise: up-left (the tables' rounding)
        am.AM_Drawer();
        // drawn up and left (42 pixels each way), not to the west
        int mx = am.f_w / 2 - 42, my = am.f_h / 2 - 42, wx = am.f_w / 2 - 60;
        bool Drawn(int x0, int y0) => Enumerable.Range(y0 - 2, 10).Any(yy => Enumerable.Range(x0 - 2, 10).Any(xx => am.fb.Opaque[yy * am.f_w + xx] != 0));
        Assert.True(Drawn(mx, my), "no mark up and left");
        Assert.False(Drawn(wx, am.f_h / 2), "a mark to the west");
    }

    // ---- the title ----

    [Theory]
    [InlineData(GameMission.doom, 1, 1, "E1M1: Hangar")]
    [InlineData(GameMission.doom, 4, 9, "E4M9: Fear")]
    [InlineData(GameMission.doom, 5, 1, "NEWLEVEL")]
    [InlineData(GameMission.doom2, 1, 1, "level 1: entryway")]
    [InlineData(GameMission.doom2, 1, 32, "level 32: grosse")]
    [InlineData(GameMission.doom2, 1, 33, "")]
    [InlineData(GameMission.pack_plut, 1, 1, "level 1: congo")]
    [InlineData(GameMission.pack_tnt, 1, 32, "level 32: caribbean")]
    [InlineData(GameMission.none, 1, 1, "Unknown level")]
    public void TheTitleIsTheMissionsMapName(GameMission mission, int episode, int map, string title)
    {
        Assert.Equal(title, HuStuff.HU_Title(mission, episode, map));
        var hu = new HuStuff(null);
        hu.HU_Start(mission, episode, map);
        Assert.Equal(title, hu.title);
    }
}
