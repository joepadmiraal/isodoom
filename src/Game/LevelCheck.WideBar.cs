using System;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Sim;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

// T6.13y: the status bar 400 wide (WideStatusBar). The rest of the check runs
// vanilla's 320 (LevelScene.CheckBarWidth), so its comparisons and T6.13x's
// stay as they were; here, with it 400: the layouts at four window sizes (the
// bar the window's width, no taller than vanilla's: at its scale and centred
// on a 21:9 window, the automap's frame on
// its top at the HUD's pixels), the HUD node showing the wide strip with the
// weapon's icon in it and T6.13x's icon above the bar hidden, and 320 giving
// T6.13x's layout back; with a real renderer, at 4:3, the wide bar with each
// weapon's icon (one dimmed) and over the automap with the map's title, every
// pixel within ±2 of the CPU's sharp bilinear and every screen pixel's block
// centre its palette colour exactly.
public partial class LevelCheck
{
    /// <summary>The wide bar's layouts and the HUD node's strips, 400 and back to 320; no renderer needed.</summary>
    private void CheckWideBarLayouts()
    {
        int checkedSizes = 0;
        foreach ((int w, int h) in new[] { (1280, 800), (1920, 1080), (800, 600), (2560, 1080) })
        {
            string what = $"wide status bar (T6.13y) at {w}x{h}";
            foreach (bool aspect43 in new[] { true, false })
            {
                ScreenLayout hud = ScreenLayout.ForHud(w, h, 0, aspect43), wide = ScreenLayout.ForWideBar(w, h, WideStatusBar.DefaultWidth, 0, aspect43);
                (int X, int Y, int W, int H) bar = wide.Rect(-WideStatusBar.BoxWidth, StStuff.ST_Y, WideStatusBar.DefaultWidth, StStuff.ST_HEIGHT);
                (int X, int Y, int W, int H) vanilla = hud.Rect(0, StStuff.ST_Y, HudScreen.SCREENWIDTH, StStuff.ST_HEIGHT);
                // the window's width, or at the HUD's scale centred where that is narrower (21:9), never taller than vanilla's
                bool fills = w / (float)WideStatusBar.DefaultWidth <= hud.ScaleX;
                if ((fills ? bar.X != 0 || bar.W != w : Math.Abs(2 * bar.X + bar.W - w) > 1 || bar.H != vanilla.H) || bar.Y + bar.H != h || bar.H > vanilla.H)
                    Fail($"{what}{(aspect43 ? "" : ", square")}: the bar at {bar}, expected {(fills ? "the window's width" : "centred")} at its bottom, no taller than vanilla's {vanilla}");
                if (aspect43 && w == 1280 && h == 800 && bar != (0, 677, 1280, 123))
                    Fail($"{what}: the bar at {bar}, expected (0, 677, 1280, 123)");
                (int fw, int fh, int pixelScale, (int X, int Y, int W, int H) frame) = LevelScene.AutomapFrameFor(hud, new Vector2(w, h), false, bar.Y);
                if (frame.Y + frame.H != bar.Y || frame.W != fw * (int)hud.ScaleX || fh != (int)(bar.Y / hud.ScaleY) || pixelScale != 1 || frame.Y < 0)
                    Fail($"{what}{(aspect43 ? "" : ", square")}: the automap's frame {fw}x{fh} at {frame}, expected {bar.Y / hud.ScaleY:0.#} rows of the HUD's pixels over the bar's top {bar.Y}");
                if (aspect43 && w == 1280 && h == 800 && fh != 141)
                    Fail($"{what}: the automap's frame is {fh} rows, expected 141");
                (int hw, int hh, _, (int X, int Y, int W, int H) high) = LevelScene.AutomapFrameFor(hud, new Vector2(w, h), true, bar.Y);
                if (high != (0, 0, w, bar.Y) || hw != w || hh != bar.Y)
                    Fail($"{what}: the high detail automap's frame {hw}x{hh} at {high}, expected the window above the bar");
            }
            checkedSizes++;
        }

        // the HUD node: the wide strip with the icon in it, then vanilla's with T6.13x's above it
        if (_scene.StatusBar is { } st && _scene.MessageLine is { } hu && _scene.WeaponIcon is { } icon && st.plyr is { } p && _scene.Wad is not null)
        {
            HudView view = _scene.Hud;
            HudMode mode = view.Mode;
            view.Mode = HudMode.Bar;
            byte[] pal = Playpal.GetPalette(0).ToArray();
            try
            {
                _scene.CheckBarWidth = WideStatusBar.DefaultWidth;
                if (view.WideBar is not { Width: WideStatusBar.DefaultWidth } wide)
                {
                    Fail($"wide status bar (T6.13y): the HUD has no 400-wide bar at {_scene.CheckBarWidth}");
                    return;
                }
                if (_scene.Wad.Find("STBAR") is { } lump && Patch.Decode(lump.Data.Span, lump.Name) is { Width: >= WideStatusBar.DefaultWidth } != wide.WadArt)
                    Fail($"wide status bar (T6.13y): the panels {(wide.WadArt ? "show" : "do not show")} the WAD's STBAR");
                var saved = ReadyWeapon(st, p, weapontype_t.wp_pistol);
                icon.Draw(st, HudMode.Bar);
                view.Show(st, hu, icon, pal);
                Vector2 size = view.GetViewport().GetVisibleRect().Size;
                (int X, int Y, int W, int H) expected = view.BarRect(view.BarLayoutFor(size));
                if (ScreenRects.RectOf(view.Bar) != expected || view.Bar.Texture?.GetWidth() != wide.Screen.Width || view.WeaponIconRect.Visible)
                    Fail($"wide status bar (T6.13y): the bar shows at {ScreenRects.RectOf(view.Bar)} ({view.Bar.Texture?.GetWidth()} wide), expected {expected} ({wide.Screen.Width}); "
                        + $"T6.13x's icon above it {(view.WeaponIconRect.Visible ? "shows" : "hidden")}");
                if (view.BarHeight != expected.H)
                    Fail($"wide status bar (T6.13y): the HUD's bar height {view.BarHeight}, expected {expected.H}");
                CheckWideStrip("wide status bar (T6.13y): the pistol", wide, icon.Bar ?? st.Screen, icon.Icon(weapontype_t.wp_pistol), false, icon);
                _scene.CheckBarWidth = HudScreen.SCREENWIDTH;
                view.Show(st, hu, icon, pal);
                if (view.WideBar is not null || ScreenRects.RectOf(view.Bar) != view.Layout.Rect(0, StStuff.ST_Y, HudScreen.SCREENWIDTH, StStuff.ST_HEIGHT)
                    || !view.WeaponIconRect.Visible)
                    Fail($"wide status bar (T6.13y): at 320 the bar shows at {ScreenRects.RectOf(view.Bar)}, T6.13x's icon {(view.WeaponIconRect.Visible ? "shows" : "hidden")}");
                Restore(st, p, weapontype_t.wp_pistol, saved);
            }
            finally
            {
                _scene.CheckBarWidth = HudScreen.SCREENWIDTH;
                view.Mode = mode;
            }
        }
        GD.Print($"Level check: wide status bar (T6.13y): the layouts at {checkedSizes} window sizes (the bar 1280x123 at 1280x800, the automap's frame 141 rows on its top), the HUD node's strips at 400 and 320");
    }

    /// <summary>The wide strip as last drawn against <paramref name="bar"/> in its middle, the panels' bevels and <paramref name="image"/> in the box (through the dim map when <paramref name="dim"/>).</summary>
    private void CheckWideStrip(string what, WideStatusBar wide, HudScreen bar, IndexedImage? image, bool dim, WeaponIcon icon)
    {
        HudScreen s = wide.Screen;
        int r = WideStatusBar.Resolution, bad = 0;
        for (int y = 0; y < StStuff.ST_HEIGHT; y++)
        {
            for (int x = 0; x < HudScreen.SCREENWIDTH; x++)
            {
                if (s.Pixels[y * r * s.Width + (wide.Side + x) * r] != bar.Pixels[y * bar.Width + x])
                    bad++;
            }
        }
        if (bad > 0)
            Fail($"{what}: {bad} pixels of the middle differ from vanilla's bar");
        if (image is null)
            return;
        if (wide.IconPlace(image) is not (int ix, int iy) || ix < (wide.Side - WideStatusBar.BoxWidth) * r || ix + image.Width > wide.Side * r)
        {
            Fail($"{what}: the icon is not in the weapon box");
            return;
        }
        bad = 0;
        for (int py = 0; py < image.Height; py++)
        {
            for (int px = 0; px < image.Width; px++)
            {
                if (image.IsOpaque(px, py) && s.Pixels[(iy + py) * s.Width + ix + px] != (dim ? icon.DimMap[image[px, py]] : image[px, py]))
                    bad++;
            }
        }
        if (bad > 0)
            Fail($"{what}: {bad} pixels of the icon differ from the sprite{(dim ? " dimmed" : "")}");
    }

    /// <summary>
    /// T6.13y, with a real renderer, from <see cref="CheckAspectDrawn"/> (4:3,
    /// the level hidden on <paramref name="under"/>'s colour): the wide bar
    /// with each weapon's icon, one coming up dimmed, and the automap over it
    /// with the map's title on its top; every pixel against the CPU's sharp
    /// bilinear, every screen pixel's block centre its palette colour.
    /// </summary>
    private async Task<(long Compared, int Worst)> CheckWideBarDrawn(StStuff st, HuStuff hu, AmMap am, byte[] under, byte[] pal)
    {
        if (_scene.WeaponIcon is not { } icon || st.plyr is not { } p)
            return (0, 0);
        HudView view = _scene.Hud;
        long compared = 0;
        int worst = 0, weapons = 0;
        string map = _scene.Mesh?.Level.Name ?? "?";
        _scene.CheckBarWidth = WideStatusBar.DefaultWidth;
        try
        {
            if (view.WideBar is not { } wide)
            {
                Fail("wide status bar (T6.13y): no wide bar at 400");
                return (0, 0);
            }
            Vector2I size = ViewSize();
            foreach (weapontype_t weapon in _allWeapons)
            {
                if (icon.Icon(weapon) is not { } image)
                    continue;
                bool dim = weapons++ == 1; // the second one coming up
                var saved = ReadyWeapon(st, p, weapon);
                if (dim)
                    p.psprites[World.ps_weapon].state = Info.weaponinfo[(int)weapon].upstate;
                icon.Draw(st, HudMode.Bar);
                view.Show(st, hu, icon, pal);
                string what = $"{map}: the wide status bar at 4:3 ({weapon}{(dim ? ", dimmed" : "")})";
                CheckWideStrip(what, wide, icon.Bar ?? st.Screen, image, dim, icon);
                (long c, int d) = await CompareWide(what, (byte[])under.Clone(), [(wide.Screen, ScreenRects.RectOf(view.Bar))], size, pal, view.BarLayout, wide);
                (compared, worst) = (compared + c, Math.Max(worst, d));
                Restore(st, p, weapon, saved);
            }

            // the automap over the wide bar: its frame's bottom on the bar's top, the map's title on that top
            Press(Key.Tab);
            view.AutomapShown = true;
            icon.Draw(st, HudMode.Bar);
            view.Show(st, hu, icon, pal);
            (int width, int height, int pixelScale, (int X, int Y, int W, int H) rect) =
                LevelScene.AutomapFrameFor(view.LayoutFor(new Vector2(size.X, size.Y)), new Vector2(size.X, size.Y), false, ScreenRects.RectOf(view.Bar).Y);
            am.Overlay = false;
            am.SetFrame(width, height, pixelScale);
            am.AM_Drawer();
            hu.HU_DrawTitle();
            _scene.AutomapScreen.Show(am.fb, pal, rect, backdrop: true);
            view.Show(st, hu, icon, pal);
            if (rect.Y + rect.H != ScreenRects.RectOf(view.Bar).Y)
                Fail($"{map}: the automap's frame {rect} does not end on the wide bar's top {ScreenRects.RectOf(view.Bar).Y}");
            (int X, int Y, int W, int H) title = ScreenRects.RectOf(view.Title);
            ScreenLayout titleLayout = LayoutOf(hu.TitleScreen, title);
            if (Math.Abs(titleLayout.ToViewport(0, StStuff.ST_Y).Y - ScreenRects.RectOf(view.Bar).Y) > 1)
                Fail($"{map}: the map's title's row {StStuff.ST_Y} at {titleLayout.ToViewport(0, StStuff.ST_Y).Y:0.#}, not on the wide bar's top {ScreenRects.RectOf(view.Bar).Y}");
            (long ac, int ad) = await CompareWide($"{map}: the automap over the wide status bar at 4:3", Black(size),
                [(am.fb, rect), (wide.Screen, ScreenRects.RectOf(view.Bar)), (hu.TitleScreen, title)], size, pal, view.BarLayout, wide);
            (compared, worst) = (compared + ac, Math.Max(worst, ad));
            Press(Key.Tab);
            _scene.AutomapScreen.Visible = false;
            view.AutomapShown = false;
        }
        finally
        {
            _scene.CheckBarWidth = HudScreen.SCREENWIDTH;
        }
        GD.Print($"Level check: wide status bar (T6.13y): {weapons} weapons' icons in the 400-wide bar and the automap over it drawn at 4:3, {compared} pixels compared, at most {worst} off the CPU's sharp bilinear");
        return (compared, worst);
    }

    // The frame against the layers drawn over `under` on the CPU, every pixel within the tolerance, and each
    // screen pixel of the wide bar (the last layer drawn over it excepted) at its block centre its palette colour exactly.
    private async Task<(long Compared, int Worst)> CompareWide(string what, byte[] under, (HudScreen Screen, (int X, int Y, int W, int H) Rect)[] layers,
        Vector2I size, byte[] pal, ScreenLayout barLayout, WideStatusBar wide)
    {
        foreach ((HudScreen screen, (int X, int Y, int W, int H) rect) in layers)
            SharpBilinear.Draw(under, size.X, size.Y, screen.ToRgba(pal), screen.Width, screen.Height, rect);
        if (await Capture(what) is not byte[] drawn)
            return (0, 0);
        int bad = 0, worst = 0, exact = 0;
        string first = "";
        for (int i = 0; i < drawn.Length; i += 4)
        {
            int d = Math.Max(Math.Abs(drawn[i] - under[i]), Math.Max(Math.Abs(drawn[i + 1] - under[i + 1]), Math.Abs(drawn[i + 2] - under[i + 2])));
            worst = Math.Max(worst, d);
            if (d > AspectTolerance && bad++ == 0)
                first = $"pixel ({i / 4 % size.X}, {i / 4 / size.X}): drawn ({drawn[i]}, {drawn[i + 1]}, {drawn[i + 2]}), the CPU's ({under[i]}, {under[i + 1]}, {under[i + 2]})";
        }
        HudScreen s = wide.Screen;
        int r = WideStatusBar.Resolution, barLayer = Array.FindIndex(layers, l => l.Screen == s);
        for (int y = 0; y < StStuff.ST_HEIGHT; y++)
        {
            for (int x = 0; x < wide.Width; x++)
            {
                int k = y * r * s.Width + x * r;
                byte c = s.Pixels[k];
                if (s.Pixels[k + 1] != c || s.Pixels[k + s.Width] != c || s.Pixels[k + s.Width + 1] != c)
                    continue; // the icon's own pixels at half the scale: within the tolerance only
                (int sx, int sy) = BlockCentre(barLayout, x - wide.Side, StStuff.ST_Y + y);
                if (sx < 0 || sy < 0 || sx >= size.X || sy >= size.Y || Covered(layers, barLayer, sx, sy))
                    continue;
                int i = (sy * size.X + sx) * 4;
                exact++;
                if (drawn[i] == pal[c * 3] && drawn[i + 1] == pal[c * 3 + 1] && drawn[i + 2] == pal[c * 3 + 2])
                    continue;
                if (bad++ == 0)
                    first = $"bar pixel ({x}, {y})'s block centre at ({sx}, {sy}): drawn ({drawn[i]}, {drawn[i + 1]}, {drawn[i + 2]}), expected ({pal[c * 3]}, {pal[c * 3 + 1]}, {pal[c * 3 + 2]})";
            }
        }
        if (exact == 0)
            Fail($"{what}: no block centre compared");
        if (bad > 0)
            Fail($"{what}: {bad} pixels and block centres differ; first: {first}");
        return (drawn.Length / 4, worst);
    }
}
