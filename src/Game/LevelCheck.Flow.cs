using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Sim;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

// T7.1: the game's flow (GameFlow). Once, after every map (no renderer
// needed): the title loop shows the WAD's pages in vanilla's order for
// vanilla's tics (no demos), each drawn as its patch; a new game from it
// starts the first map with M_Random cleared; the pause (a ticcmd's
// BTS_PAUSE) holds the world but not the status bar. With a real renderer
// the screens node shows the title page and the pause graphic over the
// level, every pixel compared with its palette colour. The exits' checks
// (T5.8, CheckExit) go through the intermission. T7.5: the finale of the
// game's first episode (or Doom II's first text screen) with vanilla's text,
// flat and timing, ending on its end picture (Doom II: skipped to the next
// map); with a real renderer its text and end picture are compared too.
public partial class LevelCheck
{
    private int _pagesShown;

    /// <summary>
    /// The title loop's pages for the WAD (d_main.c <c>D_DoAdvanceDemo</c>'s,
    /// the demos passed) with their tics, those the WAD lacks left out.
    /// </summary>
    private List<(string Page, int Tics)> ExpectedPages()
    {
        GameMode mode = _scene.GameMode;
        int title = mode == GameMode.commercial ? 35 * 11 : 170;
        var pages = new List<(string, int)> { ("TITLEPIC", title), ("CREDIT", 200) };
        pages.Add(mode == GameMode.commercial ? ("TITLEPIC", title) : mode == GameMode.retail ? ("CREDIT", 200) : ("HELP2", 200));
        pages.RemoveAll(p => !_scene.HasLump(p.Item1));
        if (pages.Count == 0)
            pages.Add(("TITLEPIC", title));
        return pages;
    }

    private void CheckGameFlow()
    {
        GameFlow flow = _scene.Flow;
        flow.D_StartTitle(null);
        if (_scene.World is not null || _scene.Mesh is not null || flow.gamestate != gamestate_t.GS_DEMOSCREEN || flow.pagetic != ExpectedPages()[0].Tics)
            Fail($"the title loop (D_StartTitle) did not drop the level or show its first page: {flow.StateText()}");
        var none = new ticcmd_t();
        _scene.Tic(none);
        List<(string Page, int Tics)> pages = ExpectedPages();
        var graphics = new ScreenGraphics(_scene.Wad, _scene.MessageLine);
        var screen = new HudScreen(0, HudScreen.SCREENHEIGHT);
        for (int i = 0; i < 2 * pages.Count; i++)
        {
            (string page, int tics) = pages[i % pages.Count];
            if (flow.gamestate != gamestate_t.GS_DEMOSCREEN || flow.pagename != page || flow.pagetic != tics - 1 || flow.usergame)
            {
                Fail($"the title loop's step {i}: {flow.StateText()}, expected {page} for {tics} tics");
                return;
            }
            screen.Clear();
            graphics.DrawPage(screen, page);
            if (graphics.Patch(page) is { } patch && !PageDrawn(screen, patch))
                Fail($"the title loop's page {page} is not drawn as its patch");
            _pagesShown++;
            // D_PageTicker: the page shows until its pagetic runs below 0; D_DoAdvanceDemo at the next tic's start
            for (int t = 0; t < tics + 1; t++)
                _scene.Tic(none);
        }
        GD.Print($"Level check: title loop (T7.1): {string.Join(", ", pages.ConvertAll(p => $"{p.Page} {p.Tics} tics"))}, twice round, each drawn as its patch");

        // a new game from the title: the first map, M_Random cleared
        _scene.MRandom.M_Random();
        _scene.StartNewGame();
        flow.G_DoGameActions();
        string first = _scene.DefaultMap();
        if (flow.gamestate != gamestate_t.GS_LEVEL || _scene.Mesh?.Level.Name != first || _scene.World is not { leveltime: 0 } world
            || _scene.PlayerMobj is null || _scene.MRandom.rndindex != 0 || !flow.usergame || world.gameskill != _scene.Skill)
        {
            Fail($"a new game from the title: {flow.StateText()} on {_scene.Mesh?.Level.Name} (expected {first}), M_Random index {_scene.MRandom.rndindex}");
            return;
        }

        // the pause: BTS_PAUSE in a ticcmd toggles it; the world holds still, the status bar ticks on
        var pause = new ticcmd_t { buttons = buttoncode_t.BT_SPECIAL | buttoncode_t.BTS_PAUSE };
        _scene.Tic(none);
        int leveltime = world.leveltime, clock = _scene.StatusBar?.st_clock ?? 0;
        _scene.Tic(pause);
        _scene.Tic(none);
        bool held = flow.paused && world.leveltime == leveltime && (_scene.StatusBar is null || _scene.StatusBar.st_clock == clock + 2);
        _scene.Tic(pause);
        if (!held || flow.paused || world.leveltime != leveltime + 1)
            Fail($"the pause: paused {flow.paused}, leveltime {leveltime} -> {world.leveltime} (expected held for two tics, then one tic)");
        else
            GD.Print($"Level check: new game (T7.1): the title's new game started {first} with M_Random cleared; the pause held the world for two tics, the status bar ran on");

        CheckFinale();
    }

    /// <summary>
    /// The level made the episode's last (map 8; Doom II: map 6) and left, as
    /// <see cref="CheckGameFlow"/>'s new game has it: the finale (Doom II: after
    /// the intermission) with f_finale.c's text and flat for the WAD's mission,
    /// or null when the game did not get there.
    /// </summary>
    private FFinale? StartFinale()
    {
        GameFlow flow = _scene.Flow;
        bool commercial = flow.gamemode == GameMode.commercial;
        if (_scene.World is not { } world)
        {
            _scene.StartNewGame();
            flow.G_DoGameActions();
            if (_scene.World is not { } w)
            {
                Fail("the finale: no new game");
                return null;
            }
            world = w;
        }
        world.gamemap = commercial ? 6 : 8;
        world.G_ExitLevel();
        _scene.Tic(new ticcmd_t());
        if (commercial)
        {
            // the intermission first: fire for all the stats, fire again for its end
            for (int t = 0; t < 20 && flow.gamestate == gamestate_t.GS_INTERMISSION; t++)
                _scene.Tic(new ticcmd_t { buttons = t % 2 == 0 ? buttoncode_t.BT_ATTACK : (byte)0 });
        }
        if (flow.gamestate != gamestate_t.GS_FINALE)
        {
            Fail($"the finale: after map {world.gamemap}'s exit the game is at {flow.StateText()}");
            return null;
        }
        return flow.Finale;
    }

    /// <summary>
    /// T7.5 (headless): <see cref="StartFinale"/>'s text and flat are
    /// vanilla's for the WAD's mission, the text shows for its length × 3 +
    /// 250 tics whatever is pressed, then the end picture (HELP2, the retail
    /// game's CREDIT) stays, drawn as its patch; Doom II's text is skipped by
    /// a press after 50 tics, to MAP07.
    /// </summary>
    private void CheckFinale()
    {
        GameFlow flow = _scene.Flow;
        if (StartFinale() is not { } fi)
            return;
        bool commercial = flow.gamemode == GameMode.commercial;
        FFinale.textscreen_t vanilla = FFinale.textscreens.FirstOrDefault(t => t.mission == flow.gamemission && t.episode == 1 && t.level == (commercial ? 6 : 8));
        if (vanilla.text is null || fi.finaletext != vanilla.text || fi.finaleflat != vanilla.background)
        {
            Fail($"the finale: text \"{fi.finaletext[..Math.Min(20, fi.finaletext.Length)]}…\" on {fi.finaleflat}, not {flow.gamemission}'s");
            return;
        }
        int textTics = fi.finaletext.Length * FFinale.TEXTSPEED + FFinale.TEXTWAIT;
        var use = new ticcmd_t { buttons = buttoncode_t.BT_USE };
        if (commercial)
        {
            for (int t = fi.finalecount; t <= 50; t++)
                _scene.Tic(use); // not seen before finalecount 51
            bool held = flow.gamestate == gamestate_t.GS_FINALE;
            _scene.Tic(new ticcmd_t());
            _scene.Tic(use);
            if (!held || flow.gamestate != gamestate_t.GS_LEVEL || _scene.Mesh?.Level.Name != "MAP07")
                Fail($"the finale: Doom II's text {(held ? "was not skipped to MAP07" : "was skipped before 50 tics")}: {flow.StateText()} on {_scene.Mesh?.Level.Name}");
            else
                GD.Print($"Level check: finale (T7.5): MAP06's text on {fi.finaleflat} ({fi.finaletext.Length} characters), skipped after 50 tics to MAP07");
            return;
        }
        while (fi.finalestage == FFinale.F_STAGE_TEXT && fi.finalecount < textTics)
            _scene.Tic(fi.finalecount % 7 == 0 ? use : new ticcmd_t()); // presses skip nothing before Doom II
        bool onTime = fi.finalestage == FFinale.F_STAGE_TEXT;
        _scene.Tic(new ticcmd_t());
        if (!onTime || fi.finalestage != FFinale.F_STAGE_ARTSCREEN || fi.finalecount != 0)
        {
            Fail($"the finale: the text did not give way to the end picture after {textTics} tics: {flow.StateText()}");
            return;
        }
        for (int t = 0; t < 100; t++)
            _scene.Tic(t % 2 == 0 ? use : new ticcmd_t { buttons = buttoncode_t.BT_ATTACK });
        string page = flow.gamemode == GameMode.retail ? "CREDIT" : "HELP2";
        var graphics = new ScreenGraphics(_scene.Wad, _scene.MessageLine);
        var screen = new HudScreen(0, HudScreen.SCREENHEIGHT);
        fi.F_Drawer(graphics, screen);
        if (flow.gamestate != gamestate_t.GS_FINALE || fi.ArtScreen() != page)
            Fail($"the finale: the end picture did not stay ({flow.StateText()}, {fi.ArtScreen()}, expected {page})");
        else if (graphics.Patch(page) is { } patch && !PageDrawn(screen, patch))
            Fail($"the finale: the end picture {page} is not drawn as its patch");
        else
            GD.Print($"Level check: finale (T7.5): E1M8's text on {fi.finaleflat} ({fi.finaletext.Length} characters) for {textTics} tics, then {page}, which stays");
    }

    /// <summary>Whether every opaque pixel of <paramref name="patch"/> at 0, 0 (less its offsets) is on <paramref name="screen"/>.</summary>
    private static bool PageDrawn(HudScreen screen, IndexedImage patch)
    {
        for (int y = 0; y < patch.Height; y++)
        {
            for (int x = 0; x < patch.Width; x++)
            {
                int sx = x - patch.LeftOffset, sy = y - patch.TopOffset;
                if (!patch.IsOpaque(x, y) || sx < 0 || sy < 0 || sx >= screen.Width || sy >= screen.Height)
                    continue;
                int i = sy * screen.Width + sx;
                if (screen.Opaque[i] == 0 || screen.Pixels[i] != patch[x, y])
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// T7.1, with a real renderer: the screens node shows the title loop's
    /// first page centred over a black backdrop, the intermission (T7.4: its
    /// stats counting, then its next location), the pause graphic over
    /// the level (hidden again once unpaused) and the finale (T7.5: its text
    /// half typed, then its end picture; Doom II: the text): each screen pixel's centre must be its index's colour in
    /// palette 0, the backdrop black around the page.
    /// </summary>
    private async Task CheckScreensDrawn()
    {
        GameFlow flow = _scene.Flow;
        bool overlay = _scene.Overlay.Visible;
        _scene.Overlay.Visible = false;
        foreach (string kind in new[] { "title", "intermission", "pause", "finale" })
        {
            bool title = kind == "title";
            string what;
            if (title)
            {
                flow.D_StartTitle(null);
                _scene.Tic(new ticcmd_t());
                what = $"the title loop's {flow.pagename}";
            }
            else if (kind == "intermission")
            {
                // T7.4: the intermission's stats counting (a new game's first map left at once), then its next location
                _scene.StartNewGame();
                flow.G_DoGameActions();
                if (_scene.World is not { } world)
                {
                    Fail("the intermission: no new game");
                    continue;
                }
                world.G_ExitLevel();
                for (int t = 0; t < 50; t++)
                    _scene.Tic(new ticcmd_t());
                what = $"the intermission ({flow.StateText()})";
            }
            else if (kind == "finale")
            {
                _scene.Tic(new ticcmd_t { buttons = buttoncode_t.BT_SPECIAL | buttoncode_t.BTS_PAUSE }); // unpause
                _scene.UpdateScreens();
                if (_scene.Screens.Visible)
                    Fail("the screens node still shows over the unpaused level");
                // T7.5: the finale's text half typed (Doom II's after its intermission), then (before Doom II) its end picture
                if (StartFinale() is not { } fi)
                    continue;
                while (fi.finalecount < 400)
                    _scene.Tic(new ticcmd_t());
                _scene.UpdateScreens();
                await CompareScreen($"the finale's text ({flow.StateText()})", false);
                if (flow.gamemode != GameMode.commercial)
                {
                    fi.finalecount = fi.finaletext.Length * FFinale.TEXTSPEED + FFinale.TEXTWAIT; // the text's last tic
                    _scene.Tic(new ticcmd_t());
                    _scene.Tic(new ticcmd_t());
                    if (fi.ArtScreen() is not { } page || _scene.Wad?.W_CheckNumForName(page) < 0)
                        continue; // (the synthetic IWAD has no end picture: nothing drawn)
                }
                what = $"the finale ({flow.StateText()})";
            }
            else
            {
                if (flow.gamestate == gamestate_t.GS_INTERMISSION)
                {
                    // the intermission's next location (fire: all the stats, fire again: the next location), compared too
                    _scene.Tic(new ticcmd_t { buttons = buttoncode_t.BT_ATTACK });
                    _scene.Tic(new ticcmd_t());
                    _scene.Tic(new ticcmd_t { buttons = buttoncode_t.BT_ATTACK });
                    _scene.Tic(new ticcmd_t());
                    _scene.UpdateScreens();
                    // (a WAD without the graphics, the synthetic IWAD, may draw nothing here: its font lacks the stand-ins' letters)
                    if (_scene.Screens.Screen.Opaque.Any(o => o != 0) || _scene.Wad?.W_CheckNumForName("WIMAP0") >= 0)
                        await CompareScreen($"the intermission ({flow.StateText()})", false);
                }
                _scene.StartNewGame();
                flow.G_DoGameActions();
                _scene.Tic(new ticcmd_t { buttons = buttoncode_t.BT_SPECIAL | buttoncode_t.BTS_PAUSE });
                what = "the pause graphic over the level";
            }
            _scene.UpdateScreens();
            await CompareScreen(what, title);
        }
        _scene.Overlay.Visible = overlay;
        GD.Print($"Level check: screens (T7.1, T7.4, T7.5): the title page, the intermission (counting, the next location), the finale (text, end picture) and the pause graphic at scale {_scene.Screens.PixelScale}, {_screenPixels} pixels compared");
    }

    private int _screenPixels;

    /// <summary>
    /// The screens node's frame against its 320×200 screen (<see cref="CheckScreensDrawn"/>):
    /// each drawn pixel's centre must be its index's colour in palette 0;
    /// with <paramref name="title"/>, the backdrop above the page black.
    /// </summary>
    private async Task CompareScreen(string what, bool title)
    {
        if (!_scene.Screens.Visible)
        {
            Fail($"{what}: the screens node is hidden");
            return;
        }
        if (await Capture(what) is not byte[] frame)
            return;
        Vector2I size = ViewSize();
        HudScreen screen = _scene.Screens.Screen;
        int scale = _scene.Screens.PixelScale;
        Vector2 at = _scene.Screens.Rect.Position;
        byte[] pal = Playpal.GetPalette(0).ToArray();
        int bad = 0, here = 0;
        string firstBad = "";
        for (int y = 0; y < screen.Height; y++)
        {
            for (int x = 0; x < screen.Width; x++)
            {
                int k = y * screen.Width + x;
                if (screen.Opaque[k] == 0)
                    continue; // the backdrop (black) or the level shows
                int sx = (int)at.X + x * scale + scale / 2, sy = (int)at.Y + y * scale + scale / 2;
                if (sx < 0 || sy < 0 || sx >= size.X || sy >= size.Y)
                    continue;
                int i = (sy * size.X + sx) * 4;
                (int r, int g, int b) = (pal[screen.Pixels[k] * 3], pal[screen.Pixels[k] * 3 + 1], pal[screen.Pixels[k] * 3 + 2]);
                here++;
                if (frame[i] == r && frame[i + 1] == g && frame[i + 2] == b)
                    continue;
                if (bad++ == 0)
                    firstBad = $"pixel ({x}, {y}) at ({sx}, {sy}): drawn ({frame[i]}, {frame[i + 1]}, {frame[i + 2]}), expected ({r}, {g}, {b})";
            }
        }
        if (title && at.Y > 0)
        {
            // the backdrop above the page
            int i = ((int)at.Y / 2 * size.X + size.X / 2) * 4;
            if (frame[i] != 0 || frame[i + 1] != 0 || frame[i + 2] != 0)
                Fail($"{what}: the backdrop above the page is ({frame[i]}, {frame[i + 1]}, {frame[i + 2]}), not black");
        }
        if (here == 0)
            Fail($"{what}: nothing drawn");
        if (bad > 0)
            Fail($"{what}: {bad} of {here} pixels differ; first: {firstBad}");
        _screenPixels += here;
        _pixels += here;
    }
}
