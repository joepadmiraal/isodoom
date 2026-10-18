using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Sim;
using IsoDoom.Wad;

namespace IsoDoom.Game;

// T7.11: vanilla's screen at 4:3 (ScreenLayout, SharpBilinear). The rest of
// the check runs square (LevelScene.CheckAspect43 off), so its comparisons stay
// pixel for pixel; here, with it on: the layouts at four window sizes (the bar
// as wide as square's and 1.2 times taller, the full screens whole and centred,
// the automap's frame above the bar), the menus' mouse (a click at the centre
// of a skill item's block picks it), and with a real renderer the title page,
// the main menu over it, the status bar and the automap's frame drawn: the
// pixel at the centre of every vanilla pixel's block its palette colour exactly,
// every pixel within ±2 of the CPU's sharp bilinear.
public partial class LevelCheck
{
    private const int AspectTolerance = 2;

    /// <summary>The layouts at 1280×800, 1920×1080, 800×600 and 2560×1080 (21:9); no renderer needed.</summary>
    private void CheckAspectLayouts()
    {
        int checkedSizes = 0;
        foreach ((int w, int h) in new[] { (1280, 800), (1920, 1080), (800, 600), (2560, 1080) })
        {
            string what = $"aspect (T7.11) at {w}x{h}";
            int k = ScreenLayout.WholeScale(w, h);
            ScreenLayout hud = ScreenLayout.ForHud(w, h, 0, true), square = ScreenLayout.ForHud(w, h, 0, false);
            (int X, int Y, int W, int H) bar = hud.Rect(0, StStuff.ST_Y, HudScreen.SCREENWIDTH, StStuff.ST_HEIGHT);
            (int X, int Y, int W, int H) squareBar = square.Rect(0, StStuff.ST_Y, HudScreen.SCREENWIDTH, StStuff.ST_HEIGHT);
            int barHeight = (int)MathF.Floor(StStuff.ST_HEIGHT * ScreenLayout.PixelAspect * k + 0.5f);
            if (bar.W != HudScreen.SCREENWIDTH * k || bar.W != squareBar.W || bar.X != squareBar.X || bar.H != barHeight || bar.Y + bar.H != h
                || squareBar.H != StStuff.ST_HEIGHT * k)
                Fail($"{what}: the status bar at {bar} (square: {squareBar}), expected {HudScreen.SCREENWIDTH * k}x{barHeight} at the bottom");
            if (w == 1280 && h == 800 && bar != (0, 646, 1280, 154))
                Fail($"{what}: the status bar at {bar}, expected (0, 646, 1280, 154)");

            float fit = Math.Min(w / 320f, h / 240f);
            (int X, int Y, int W, int H) screen = ScreenLayout.ForScreen(w, h, 0, true, true).Rect(0, 0, HudScreen.SCREENWIDTH, HudScreen.SCREENHEIGHT);
            if (Math.Abs(screen.W - 320 * fit) > 1 || Math.Abs(screen.H - 240 * fit) > 1 || Math.Abs(2 * screen.X + screen.W - w) > 2 || Math.Abs(2 * screen.Y + screen.H - h) > 2
                || screen.X < 0 || screen.Y < 0 || screen.X + screen.W > w || screen.Y + screen.H > h)
                Fail($"{what}: the full screen at {screen}, expected {320 * fit:0.#}x{240 * fit:0.#} centred");
            if (w == 1280 && h == 800 && screen != (106, 0, 1067, 800))
                Fail($"{what}: the full screen at {screen}, expected (106, 0, 1067, 800)");

            (int fw, int fh, int pixelScale, (int X, int Y, int W, int H) frame) = LevelScene.AutomapFrameFor(hud, new Vector2(w, h), false);
            if (frame.Y + frame.H != bar.Y || frame.W != fw * k || fh != (int)(bar.Y / hud.ScaleY) || pixelScale != 1 || frame.Y < 0)
                Fail($"{what}: the automap's frame {fw}x{fh} at {frame}, expected {bar.Y / hud.ScaleY:0.#} rows over the bar's top {bar.Y}");
            (int hw, int hh, int highScale, (int X, int Y, int W, int H) high) = LevelScene.AutomapFrameFor(hud, new Vector2(w, h), true);
            if (high != (0, 0, w, bar.Y) || hw != w || hh != bar.Y || highScale != k)
                Fail($"{what}: the high detail automap's frame {hw}x{hh} at {high}, expected the window above the bar");

            // the mouse's mapping: every pixel's block centre back to that pixel
            ScreenLayout shown = ScreenLayout.FromRect(screen, HudScreen.SCREENWIDTH, HudScreen.SCREENHEIGHT);
            for (int y = 0; y < HudScreen.SCREENHEIGHT; y++)
            {
                for (int x = 0; x < HudScreen.SCREENWIDTH; x++)
                {
                    (float vx, float vy) = shown.ToViewport(x + 0.5f, y + 0.5f);
                    if (shown.ToScreen(vx, vy) != (x, y) || shown.ToScreen(MathF.Floor(vx) + 0.5f, MathF.Floor(vy) + 0.5f) != (x, y))
                    {
                        Fail($"{what}: the full screen's pixel ({x}, {y}) maps back to {shown.ToScreen(vx, vy)}");
                        y = HudScreen.SCREENHEIGHT;
                        break;
                    }
                }
            }
            checkedSizes++;
        }
        GD.Print($"Level check: aspect (T7.11): the layouts at {checkedSizes} window sizes (the bar 1280x154 at 1280x800, the full screens whole and centred, the automap above the bar)");
    }

    /// <summary>T7.11: with 4:3, a click at the centre of Hurt Me Plenty's block on the skill menu picks it (through <see cref="ScreenView.ToScreen"/>).</summary>
    private void CheckAspectMouse()
    {
        GameFlow flow = _scene.Flow;
        MMenu menu = flow.Menu;
        _scene.CheckAspect43 = true;
        try
        {
            flow.D_StartTitle(null);
            _scene.MenuEvent(KeyEvent(Key.A));
            while (menu.itemOn != 0)
                _scene.MenuEvent(KeyEvent(Key.Up));
            _scene.MenuEvent(KeyEvent(Key.Enter));
            if (_scene.GameMode != GameMode.commercial)
                _scene.MenuEvent(KeyEvent(Key.Enter));
            if (menu.StateName != "skill")
            {
                Fail($"aspect (T7.11): the menus did not reach the skill menu: {menu.StateText()}");
                return;
            }
            _scene.UpdateScreens();
            ScreenLayout layout = _scene.MenuScreens.Layout;
            if (layout.Square)
                Fail($"aspect (T7.11): the menu screen is square with 4:3 on: {layout}");
            int x = menu.currentMenu.x + 20, y = menu.currentMenu.y + 2 * MMenu.LINEHEIGHT + 4;
            Vector2 at = MenuPoint(x, y);
            _scene.MenuEvent(Held(new InputEventMouseMotion { Position = at }));
            int hovered = menu.itemOn;
            _scene.MenuEvent(Held(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at }));
            if (hovered != 2 || menu.StateName != "closed" || flow.gameaction != gameaction_t.ga_newgame)
            {
                Fail($"aspect (T7.11): a click at {at} (menu point {x}, {y}, scale {layout.ScaleX:0.###}x{layout.ScaleY:0.###}) hovered item {hovered}, then {menu.StateText()}, {flow.gameaction}");
                return;
            }
            flow.G_DoGameActions();
            GD.Print($"Level check: aspect (T7.11): the mouse picked Hurt Me Plenty at 4:3 (scale {layout.ScaleX:0.###}x{layout.ScaleY:0.###})");
        }
        finally
        {
            _scene.CheckAspect43 = false;
            _scene.UpdateScreens();
        }
    }

    /// <summary>
    /// T7.11, with a real renderer: the title page, the main menu over it,
    /// the status bar and the automap's frame drawn at 4:3, against the
    /// CPU's composition (<see cref="SharpBilinear"/>).
    /// </summary>
    private async Task CheckAspectDrawn()
    {
        GameFlow flow = _scene.Flow;
        bool overlay = _scene.Overlay.Visible;
        _scene.Overlay.Visible = false;
        _scene.CheckAspect43 = true;
        long compared = 0;
        int worst = 0;
        try
        {
            byte[] pal = Playpal.GetPalette(0).ToArray();
            Vector2I size = ViewSize();

            // the title page, and the main menu over it
            flow.Menu.M_ClearMenus();
            flow.D_StartTitle(null);
            _scene.Tic(new ticcmd_t());
            _scene.UpdateScreens();
            var title = (_scene.Screens.Screen, ScreenRects.RectOf(_scene.Screens.Rect));
            await CompareAspect("the title page at 4:3", Black(size), [title], pal);
            flow.Menu.M_StartControlPanel();
            _scene.UpdateScreens();
            var main = (_scene.MenuScreens.Screen, ScreenRects.RectOf(_scene.MenuScreens.Rect));
            await CompareAspect("the main menu over the title page at 4:3", Black(size), [title, main], pal);
            flow.Menu.M_ClearMenus();

            // the status bar and the automap's frame, on a level with nothing else drawn
            _scene.StartNewGame();
            flow.G_DoGameActions();
            _scene.UpdateScreens();
            if (_scene.StatusBar is { } st && _scene.MessageLine is { } hu && _scene.Automap is { } am && _scene.PlayerMobj is not null)
            {
                Color background = _scene.Environment.BackgroundColor;
                (int ur, int ug, int ub) = UnusedColor(Playpal);
                _scene.Environment.BackgroundColor = Color.Color8((byte)ur, (byte)ug, (byte)ub);
                foreach (MeshInstance3D? chunk in _scene.Chunks)
                {
                    chunk?.Visible = false;
                }
                if (_scene.Things is { } things)
                    things.Visible = false;
                HudMode mode = _scene.Hud.Mode;
                _scene.Hud.Mode = HudMode.Bar;
                _scene.Hud.Visible = true;
                _scene.Tic(new ticcmd_t { angleturn = _scene.Tweaks.AbsoluteAiming ? Ticcmds.AbsoluteAngle(_scene.PlayerMobj.angle) : (short)0 });
                hu.message_on = false;
                _scene.Hud.Show(st, hu, Playpal, 0);
                ScreenLayout hud = _scene.Hud.Layout;
                var bar = (st.Screen, ScreenRects.RectOf(_scene.Hud.Bar));
                byte[] under = Filled(size, (byte)ur, (byte)ug, (byte)ub);
                await CompareAspect("the status bar at 4:3", under, [bar], pal);

                Press(Key.Tab);
                _scene.Hud.AutomapShown = true;
                (int width, int height, int pixelScale, (int X, int Y, int W, int H) rect) = LevelScene.AutomapFrameFor(hud, size, false);
                am.Overlay = false;
                am.SetFrame(width, height, pixelScale);
                am.AM_Drawer();
                hu.HU_DrawTitle();
                _scene.AutomapScreen.Show(am.fb, pal, rect, backdrop: true);
                _scene.Hud.Show(st, hu, Playpal, 0);
                var frame = (am.fb, rect);
                var mapTitle = (hu.TitleScreen, ScreenRects.RectOf(_scene.Hud.Title));
                await CompareAspect("the automap at 4:3", Black(size), [frame, bar, mapTitle], pal);
                Press(Key.Tab);
                _scene.AutomapScreen.Visible = false;
                _scene.Hud.AutomapShown = false;
                (long wideCompared, int wideWorst) = await CheckWideBarDrawn(st, hu, am, Filled(size, (byte)ur, (byte)ug, (byte)ub), pal); // T6.13y
                (compared, worst) = (compared + wideCompared, Math.Max(worst, wideWorst));
                _scene.Hud.Visible = false;
                _scene.Hud.Mode = mode;
                _scene.Environment.BackgroundColor = background;
                foreach (MeshInstance3D? chunk in _scene.Chunks)
                {
                    chunk?.Visible = true;
                }
                if (_scene.Things is { } shown)
                    shown.Visible = true;
            }
            else
                Fail("aspect (T7.11): no status bar, message line or automap on the new game");
        }
        finally
        {
            _scene.CheckAspect43 = false;
            _scene.Overlay.Visible = overlay;
            _scene.UpdateScreens();
        }
        _pixels += compared;
        GD.Print($"Level check: aspect (T7.11): the title page, the main menu, the status bar and the automap drawn at 4:3, {compared} pixels compared, at most {worst} off the CPU's sharp bilinear");

        // The frame against the layers drawn over `under` on the CPU: every pixel within the tolerance,
        // each opaque pixel's block centre (where no later layer draws) its palette colour exactly.
        async Task CompareAspect(string what, byte[] under, (HudScreen Screen, (int X, int Y, int W, int H) Rect)[] layers, byte[] palette)
        {
            Vector2I size = ViewSize();
            foreach ((HudScreen screen, (int X, int Y, int W, int H) rect) in layers)
            {
                ScreenLayout layout = LayoutOf(screen, rect);
                if (screen.Opaque.All(o => o == 0))
                {
                    Fail($"{what}: a layer draws nothing");
                    return;
                }
                if (layout.Square || rect.W == 0 || rect.H == 0)
                {
                    Fail($"{what}: a layer is square or empty at 4:3: {layout}, {rect}");
                    return;
                }
                SharpBilinear.Draw(under, size.X, size.Y, screen.ToRgba(palette), screen.Width, screen.Height, rect);
            }
            if (await Capture(what) is not byte[] drawn)
                return;
            int bad = 0, here = 0, exact = 0;
            string first = "";
            for (int i = 0; i < drawn.Length; i += 4)
            {
                int d = Math.Max(Math.Abs(drawn[i] - under[i]), Math.Max(Math.Abs(drawn[i + 1] - under[i + 1]), Math.Abs(drawn[i + 2] - under[i + 2])));
                worst = Math.Max(worst, d);
                here++;
                if (d > AspectTolerance && bad++ == 0)
                    first = $"pixel ({i / 4 % size.X}, {i / 4 / size.X}): drawn ({drawn[i]}, {drawn[i + 1]}, {drawn[i + 2]}), the CPU's ({under[i]}, {under[i + 1]}, {under[i + 2]})";
            }
            for (int l = 0; l < layers.Length; l++)
            {
                (HudScreen screen, (int X, int Y, int W, int H) rect) = layers[l];
                ScreenLayout layout = LayoutOf(screen, rect);
                if (Math.Min(layout.ScaleX, layout.ScaleY) < 2)
                    continue; // a block's centre is inside its sharp core only from scale 2
                for (int y = 0; y < screen.Height; y++)
                {
                    for (int x = 0; x < screen.Width; x++)
                    {
                        int k = y * screen.Width + x;
                        if (screen.Opaque[k] == 0)
                            continue;
                        (int sx, int sy) = BlockCentre(layout, x, screen.Top + y);
                        if (sx < 0 || sy < 0 || sx >= size.X || sy >= size.Y || Covered(layers, l, sx, sy))
                            continue;
                        int i = (sy * size.X + sx) * 4, c = screen.Pixels[k] * 3;
                        exact++;
                        if (drawn[i] == palette[c] && drawn[i + 1] == palette[c + 1] && drawn[i + 2] == palette[c + 2])
                            continue;
                        if (bad++ == 0)
                            first = $"pixel ({x}, {y})'s block centre at ({sx}, {sy}): drawn ({drawn[i]}, {drawn[i + 1]}, {drawn[i + 2]}), expected ({palette[c]}, {palette[c + 1]}, {palette[c + 2]})";
                    }
                }
            }
            if (exact == 0)
                Fail($"{what}: no block centre compared");
            if (bad > 0)
                Fail($"{what}: {bad} of {here} pixels and {exact} block centres differ; first: {first}");
            compared += here;
        }
    }

    // Whether a layer after `layer` draws an opaque pixel at viewport pixel x, y.
    private static bool Covered((HudScreen Screen, (int X, int Y, int W, int H) Rect)[] layers, int layer, int x, int y)
    {
        for (int l = layer + 1; l < layers.Length; l++)
        {
            (HudScreen screen, (int X, int Y, int W, int H) rect) = layers[l];
            (int sx, int sy) = LayoutOf(screen, rect).ToScreen(x + 0.5f, y + 0.5f);
            sy -= screen.Top;
            if (sx >= 0 && sy >= 0 && sx < screen.Width && sy < screen.Height && screen.Opaque[sy * screen.Width + sx] != 0)
                return true;
        }
        return false;
    }

    // A strip's layout as drawn at its rectangle (its first row screen row Top).
    private static ScreenLayout LayoutOf(HudScreen screen, (int X, int Y, int W, int H) rect) => ScreenLayout.FromRect(rect, screen.Width, screen.Height, screen.Top);

    private static byte[] Black(Vector2I size) => Filled(size, 0, 0, 0);

    private static byte[] Filled(Vector2I size, byte r, byte g, byte b)
    {
        byte[] rgba = new byte[size.X * size.Y * 4];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = r;
            rgba[i + 1] = g;
            rgba[i + 2] = b;
            rgba[i + 3] = 255;
        }
        return rgba;
    }
}
