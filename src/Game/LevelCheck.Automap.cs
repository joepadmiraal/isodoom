using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Map;
using IsoDoom.Sim;

namespace IsoDoom.Game;

// T7.9: the automap. On every map (no renderer needed): Tab through the
// scene's input glue opens it on the console player, the map's title is the
// mission's; the lines it draws are vanilla's rules' (seen and not
// ML_DONTDRAW; with the computer map every line not ML_DONTDRAW; with iddt
// every line, then the things too), every one drawn inside the frame (no line
// refused after the clipping) at its opening scale, zoomed fully in and out
// and over the whole map; Tab closes it; the console player's death closes it
// (CheckReborn's kill). Once, through the input glue on a new game: the
// pad's Y, follow, the pan keys (taken only when not following), the zoom
// keys taken from the HUD's debug keys, the grid's and the marks' messages
// on the message line, iddt typed with its keys taken from the debug keys,
// a new game closing it. With a real renderer: the frame shown under the
// status bar, every pixel its index's colour (opaque, and the overlay
// style's see-through background), the bar and the map's title shown with it.
public partial class LevelCheck
{
    private int _automapMaps, _automapLines, _automapDeaths;

    private static InputEventKey KeyUp(Key key) => new() { Keycode = key, PhysicalKeycode = key, Pressed = false };

    private void Press(Key key)
    {
        _scene.CheatEvent(KeyEvent(key));
        _scene.AutomapEvent(KeyEvent(key));
        _scene.CheatEvent(KeyUp(key));
        _scene.AutomapEvent(KeyUp(key));
    }

    private void CheckAutomapData(string map)
    {
        if (_scene.World is not { } world || _scene.PlayerMobj is not { } me || _scene.Automap is not { } am || _scene.MessageLine is not { } hu)
            return;
        string Step(string what) => $"{map}: automap (T7.9): {what}: {_scene.AutomapText()}";
        _automapMaps++;
        Press(Key.Tab);
        if (!am.automapactive || am.World != world)
        {
            Fail(Step("Tab did not open it"));
            return;
        }
        if (am.m_x + am.m_w / 2 - me.x is < -65536 or > 65536 || am.m_y + am.m_h / 2 - me.y is < -65536 or > 65536)
            Fail(Step($"not centred on the player at ({me.x >> 16}, {me.y >> 16})"));
        string title = HuStuff.HU_Title(_scene.Flow.gamemission, world.gameepisode, world.gamemap);
        if (hu.title != title)
            Fail(Step($"the title is \"{hu.title}\", expected \"{title}\""));

        player_t p = world.players[world.consoleplayer];
        int allmap = p.powers[(int)powertype_t.pw_allmap];
        int refused = am.fuck;
        bool Rule(int i, int cheating, bool computer)
        {
            short f = world.lines[i].flags;
            if (cheating != 0)
                return true;
            if ((f & Line.ML_DONTDRAW) != 0)
                return false;
            if ((f & Line.ML_MAPPED) != 0)
            {
                line_t l = world.lines[i];
                return l.backsector is null || l.special == 39 || (f & Line.ML_SECRET) != 0
                    || l.backsector.floorheight != l.frontsector!.floorheight || l.backsector.ceilingheight != l.frontsector.ceilingheight;
            }
            return computer;
        }
        void Lines(string what, bool computer)
        {
            for (int i = 0; i < world.lines.Length; i++)
            {
                _automapLines++;
                if (am.LineColor(i) >= 0 != Rule(i, am.cheating, computer))
                {
                    Fail(Step($"{what}: line {i} (flags {world.lines[i].flags}) {(am.LineColor(i) >= 0 ? "drawn" : "not drawn")}"));
                    return;
                }
            }
            am.AM_Drawer();
            if (am.fuck != refused)
                Fail(Step($"{what}: {am.fuck - refused} lines refused after the clipping"));
            refused = am.fuck;
        }
        p.powers[(int)powertype_t.pw_allmap] = 0;
        Lines("the lines seen", false);
        p.powers[(int)powertype_t.pw_allmap] = 1;
        Lines("the computer map", true);
        p.powers[(int)powertype_t.pw_allmap] = allmap;
        foreach (Key k in new[] { Key.I, Key.D, Key.D, Key.T })
            Press(k);
        if (am.cheating != 1)
            Fail(Step("iddt typed once"));
        Lines("iddt", false);
        foreach (Key k in new[] { Key.I, Key.D, Key.D, Key.T })
            Press(k);
        if (am.cheating != 2)
            Fail(Step("iddt typed twice"));
        am.AM_Drawer();
        if (!am.fb.Pixels.Contains((byte)AmMap.THINGCOLORS))
            Fail(Step("iddt's second step draws no thing"));
        // zoomed fully in and out, the whole map, panned to an edge: no line refused
        _scene.AutomapEvent(KeyEvent(Key.Equal));
        for (int i = 0; i < 600; i++)
            am.AM_Ticker();
        _scene.AutomapEvent(KeyUp(Key.Equal));
        if (am.scale_mtof != am.max_scale_mtof)
            Fail(Step("= held: not zoomed fully in"));
        Lines("zoomed in", false);
        _scene.AutomapEvent(KeyEvent(Key.Minus));
        for (int i = 0; i < 600; i++)
            am.AM_Ticker();
        _scene.AutomapEvent(KeyUp(Key.Minus));
        if (am.scale_mtof != am.min_scale_mtof)
            Fail(Step("- held: not zoomed fully out"));
        Lines("zoomed out", false);
        Press(Key.Key0);
        Press(Key.Key0);
        Press(Key.F);
        _scene.AutomapEvent(KeyEvent(Key.Right));
        for (int i = 0; i < 600; i++)
            am.AM_Ticker();
        _scene.AutomapEvent(KeyUp(Key.Right));
        if (am.m_x + am.m_w / 2 != am.max_x)
            Fail(Step("panned right: the window's centre is not at the map's edge"));
        Lines("panned", false);
        Press(Key.F);
        foreach (Key k in new[] { Key.I, Key.D, Key.D, Key.T })
            Press(k);
        if (am.cheating != 0 || am.followplayer == 0)
            Fail(Step("iddt typed a third time, follow on"));
        Press(Key.Tab);
        if (am.automapactive)
            Fail(Step("Tab did not close it"));
    }

    /// <summary>T7.9: opens the automap before <see cref="CheckReborn"/>'s kill (p_inter.c's <c>AM_Stop</c>: "don't die in auto map").</summary>
    private void OpenAutomapForDeath()
    {
        if (_scene.Automap is { automapactive: false })
            Press(Key.Tab);
    }

    /// <summary>T7.9: the console player's death closed it.</summary>
    private void CheckAutomapClosedByDeath(string map)
    {
        if (_scene.Automap is not { } am)
            return;
        _automapDeaths++;
        if (am.automapactive)
            Fail($"{map}: automap (T7.9): the player died with it open, and it is still open");
    }

    /// <summary>T7.9: the automap through the input glue on a new game (the pad's, follow, the pan and zoom keys, the messages, iddt, a new game closing it).</summary>
    private void CheckAutomap()
    {
        GameFlow flow = _scene.Flow;
        _scene.StartNewGame(skill_t.sk_medium);
        flow.G_DoGameActions();
        if (_scene.World is not { } world || _scene.Automap is not { } am || _scene.PlayerMobj is null)
        {
            Fail("automap (T7.9): no new game");
            return;
        }
        string Step(string what) => $"automap (T7.9): {what}: {_scene.AutomapText()}, message line \"{_scene.HudMessage}\"";
        var y = new InputEventJoypadButton { ButtonIndex = JoyButton.Y, Pressed = true };
        if (!_scene.AutomapEvent(y) || !am.automapactive)
            Fail(Step("the pad's Y did not open it"));
        _scene.AutomapEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.Y, Pressed = false });

        // following: the arrows are the game's; not following, the automap's
        if (_scene.AutomapEvent(KeyEvent(Key.Right)))
            Fail(Step("following, the right arrow was taken from the game"));
        _scene.AutomapEvent(KeyUp(Key.Right));
        if (!_scene.AutomapEvent(KeyEvent(Key.F)))
            Fail(Step("F not taken"));
        _scene.AutomapEvent(KeyUp(Key.F));
        _scene.Tic(default);
        if (am.followplayer != 0 || _scene.HudMessage != AmMap.AMSTR_FOLLOWOFF)
            Fail(Step("F: follow not off, or no message"));
        int x = am.m_x;
        var stick = new InputEventJoypadMotion { Axis = JoyAxis.RightX, AxisValue = 1 };
        if (!_scene.AutomapEvent(stick))
            Fail(Step("not following, the right stick was not taken"));
        _scene.Tic(default);
        _scene.AutomapEvent(new InputEventJoypadMotion { Axis = JoyAxis.RightX, AxisValue = 0 });
        int moved = am.m_x - x;
        _scene.Tic(default);
        if (moved <= 0 || am.m_x - x != moved)
            Fail(Step($"the right stick panned {moved >> 16} units in its tic, then {(am.m_x - x - moved) >> 16} after it was let go"));
        // the zoom keys are the automap's, not the HUD's (= and - are its debug keys)
        if (!_scene.AutomapEvent(KeyEvent(Key.Equal)))
            Fail(Step("= not taken"));
        int scale = am.scale_mtof;
        _scene.Tic(default);
        _scene.AutomapEvent(KeyUp(Key.Equal));
        if (am.scale_mtof <= scale)
            Fail(Step("= held a tic: no zoom"));
        var dpad = new InputEventJoypadButton { ButtonIndex = JoyButton.DpadDown, Pressed = true };
        _scene.AutomapEvent(dpad);
        scale = am.scale_mtof;
        _scene.Tic(default);
        _scene.AutomapEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.DpadDown, Pressed = false });
        if (am.scale_mtof >= scale)
            Fail(Step("the D-pad's down held a tic: no zoom out"));
        // the grid's and the marks' messages
        foreach ((Key key, string message) in new[] { (Key.G, AmMap.AMSTR_GRIDON), (Key.M, AmMap.AMSTR_MARKEDSPOT + " 0"), (Key.C, AmMap.AMSTR_MARKSCLEARED), (Key.G, AmMap.AMSTR_GRIDOFF) })
        {
            Press(key);
            _scene.Tic(default);
            if (_scene.HudMessage != message)
                Fail(Step($"{key}: expected the message \"{message}\""));
        }
        // iddt: the keys past "id" are not debug keys (T would tilt the sprites)
        foreach (Key k in new[] { Key.I, Key.D, Key.D, Key.T })
        {
            bool taken = _scene.CheatEvent(KeyEvent(k)) | _scene.AutomapEvent(KeyEvent(k));
            if (k == Key.T && !taken)
                Fail(Step("iddt's T was left to the debug keys"));
        }
        if (am.cheating != 1)
            Fail(Step("iddt not taken"));
        // a new game closes it (g_game.c G_InitNew's automapactive = false)
        _scene.StartNewGame(skill_t.sk_medium);
        flow.G_DoGameActions();
        _scene.Tic(default);
        if (am.automapactive || ReferenceEquals(_scene.World, world))
            Fail(Step("a new game left it open"));
        GD.Print($"Level check: automap (T7.9): the pad's Y, follow, the right stick's pan, = and the D-pad's zoom, the grid's and the marks' messages, iddt's keys and a new game checked");
    }

    private void PrintAutomap()
    {
        GD.Print($"Level check: automap (T7.9): {_automapMaps} maps opened it with Tab on the player, the title the mission's; {_automapLines} lines drawn as vanilla's rules say "
            + $"(seen, the computer map, iddt), none refused after the clipping (opening, zoomed in and out, the whole map, panned to an edge); iddt's things drawn; "
            + $"{_automapDeaths} deaths closed it");
        if (_automapMaps == 0)
            Fail("automap (T7.9): no map opened it");
    }

    /// <summary>
    /// T7.9, with a real renderer: the automap shown under the status bar at
    /// the HUD's scale, every frame pixel's centre its index's colour in
    /// palette 0, vanilla's black and the overlay style (see-through: the
    /// background shows where it draws nothing), with the bar and the map's title.
    /// </summary>
    private async Task CheckAutomapDrawn()
    {
        if (_scene.World is null || _scene.Automap is not { } am || _scene.PlayerMobj is null || _scene.MessageLine is not { } hu)
            return;
        string map = _scene.Mesh?.Level.Name ?? "?";
        bool overlayShown = _scene.Overlay.Visible;
        _scene.Overlay.Visible = false;
        Color background = _scene.Environment.BackgroundColor;
        (int ur, int ug, int ub) = UnusedColor(Playpal);
        _scene.Environment.BackgroundColor = Color.Color8((byte)ur, (byte)ug, (byte)ub);
        foreach (MeshInstance3D? chunk in _scene.Chunks)
        {
            chunk?.Visible = false;
        }
        if (_scene.Things is { } things)
            things.Visible = false;
        Press(Key.Tab);
        foreach (Key k in new[] { Key.I, Key.D, Key.D, Key.T })
            Press(k); // every line
        hu.message_on = false; // (the message line would cover the frame's top)
        int compared = 0;
        foreach (bool overlay in new[] { false, true })
        {
            Vector2I size = ViewSize();
            int scale = HudView.ScaleFor(size);
            am.Overlay = overlay;
            am.SetFrame(size.X / scale, (size.Y - StStuff.ST_HEIGHT * scale) / scale, 1);
            am.AM_Drawer();
            hu.HU_DrawTitle();
            var at = new Vector2(MathF.Floor((size.X - am.f_w * scale) / 2f), size.Y - StStuff.ST_HEIGHT * scale - am.f_h * scale);
            byte[] pal = Playpal.GetPalette(0).ToArray();
            _scene.AutomapScreen.Show(am.fb, pal, at, scale, backdrop: !overlay);
            _scene.Hud.AutomapShown = true;
            _scene.Hud.Visible = true;
            _scene.Hud.Show(_scene.StatusBar, hu, Playpal, 0);
            string what = $"{map}: the automap ({(overlay ? "overlay" : "opaque")})";
            if (await Capture(what) is not byte[] frame)
                continue;
            int bad = 0;
            string first = "";
            HudScreen title = hu.TitleScreen;
            int titleLeft = (int)MathF.Floor((size.X - HudScreen.SCREENWIDTH * scale) / 2f), titleTop = size.Y - (HudScreen.SCREENHEIGHT - title.Top) * scale;
            for (int fy = 0; fy < am.f_h; fy++)
            {
                for (int fx = 0; fx < am.f_w; fx++)
                {
                    int sx = (int)at.X + fx * scale + scale / 2, sy = (int)at.Y + fy * scale + scale / 2;
                    if (sx < 0 || sy < 0 || sx >= size.X || sy >= size.Y)
                        continue;
                    // the title over it
                    int tx = (sx - Math.Max(titleLeft, 0)) / scale, ty = (sy - titleTop) / scale;
                    if (sx >= Math.Max(titleLeft, 0) && ty >= 0 && tx < title.Width && ty < title.Height && title.Opaque[ty * title.Width + tx] != 0)
                        continue;
                    int k = fy * am.f_w + fx, i = (sy * size.X + sx) * 4;
                    (int r, int g, int b) = am.fb.Opaque[k] != 0 ? (pal[am.fb.Pixels[k] * 3], pal[am.fb.Pixels[k] * 3 + 1], pal[am.fb.Pixels[k] * 3 + 2]) : (ur, ug, ub);
                    compared++;
                    if (frame[i] == r && frame[i + 1] == g && frame[i + 2] == b)
                        continue;
                    if (bad++ == 0)
                        first = $"frame pixel ({fx}, {fy}) at ({sx}, {sy}): drawn ({frame[i]}, {frame[i + 1]}, {frame[i + 2]}), expected ({r}, {g}, {b})";
                }
            }
            if (bad > 0)
                Fail($"{what}: {bad} pixels differ; first: {first}");
            if (!_scene.Hud.Bar.Visible || hu.title.Length > 0 && !_scene.Hud.Title.Visible)
                Fail($"{what}: the status bar or the map's title is not shown with it");
        }
        _pixels += compared;
        am.Overlay = false;
        foreach (Key k in new[] { Key.I, Key.D, Key.D, Key.T, Key.I, Key.D, Key.D, Key.T })
            Press(k); // iddt back off
        Press(Key.Tab);
        _scene.AutomapScreen.Visible = false;
        _scene.Hud.AutomapShown = false;
        _scene.Hud.Visible = false;
        GD.Print($"Level check: {map}: automap (T7.9): the frame at the HUD's scale under the status bar, opaque and overlay, {compared} pixels compared");
        _scene.Overlay.Visible = overlayShown;
        _scene.Environment.BackgroundColor = background;
        foreach (MeshInstance3D? chunk in _scene.Chunks)
        {
            chunk?.Visible = true;
        }
        if (_scene.Things is { } shown)
            shown.Visible = true;
    }
}
