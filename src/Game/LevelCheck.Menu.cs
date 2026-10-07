using System;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Sim;
using IsoDoom.Wad;

namespace IsoDoom.Game;

// T7.2: the menus (MMenu) through the scene's input glue (LevelScene.MenuEvent),
// as a person's keys, pad and mouse reach them: on the title loop any key opens
// the main menu, Escape closes it, the pad's Start opens it, A and Enter go to
// the episodes (Doom II: the skills), the shareware game's second episode shows
// vanilla's message, a click on Hurt Me Plenty (from the menu screen's scale
// and place) starts the first map on that skill; in a level the world holds
// while the menu is up. With a real renderer the main menu over the title page
// is compared pixel by pixel (CheckMenuDrawn).
public partial class LevelCheck
{
    private static InputEventKey KeyEvent(Key key) => new() { Keycode = key, PhysicalKeycode = key, Pressed = true };

    private static InputEventJoypadButton PadEvent(JoyButton button) => new() { ButtonIndex = button, Pressed = true };

    /// <summary>The viewport point of vanilla's screen point <paramref name="x"/>, <paramref name="y"/> on the menu screen (its pixel's centre).</summary>
    private Vector2 MenuPoint(int x, int y)
    {
        ScreenView s = _scene.MenuScreens;
        return s.Rect.Position + new Vector2(x * s.PixelScale + s.PixelScale / 2f, y * s.PixelScale + s.PixelScale / 2f);
    }

    private void CheckMenus()
    {
        GameFlow flow = _scene.Flow;
        MMenu menu = flow.Menu;
        flow.D_StartTitle(null);
        string Step(string what) => $"the menus (T7.2): {what}: {menu.StateText()}";

        _scene.MenuEvent(KeyEvent(Key.A));
        if (menu.StateName != "main")
        {
            Fail(Step("a key on the title loop did not open the main menu"));
            return;
        }
        _scene.MenuEvent(KeyEvent(Key.Escape));
        if (menu.StateName != "closed")
            Fail(Step("Escape did not close the menu"));
        _scene.MenuEvent(PadEvent(JoyButton.Start));
        if (menu.StateName != "main")
            Fail(Step("the pad's Start did not open the menu"));
        while (menu.itemOn != 0)
            _scene.MenuEvent(KeyEvent(Key.Up));
        _scene.MenuEvent(PadEvent(JoyButton.A));
        bool commercial = _scene.GameMode == GameMode.commercial;
        if (menu.StateName != (commercial ? "skill" : "episode"))
            Fail(Step("the pad's A on New Game"));
        if (!commercial)
        {
            if (_scene.GameMode == GameMode.shareware)
            {
                _scene.MenuEvent(KeyEvent(Key.Down));
                _scene.MenuEvent(KeyEvent(Key.Enter));
                if (menu.messageString != MMenu.SWSTRING)
                    Fail(Step("the shareware game's second episode"));
                _scene.MenuEvent(KeyEvent(Key.Space));
                _scene.MenuEvent(KeyEvent(Key.Escape));
                _scene.MenuEvent(KeyEvent(Key.Enter));
                _scene.MenuEvent(KeyEvent(Key.Up));
            }
            _scene.MenuEvent(KeyEvent(Key.Enter));
            if (menu.StateName != "skill")
                Fail(Step("Enter on the first episode"));
        }

        // the mouse: hover and click Hurt Me Plenty (the skill menu's item 2) where the menu screen shows it
        _scene.UpdateScreens();
        int x = menu.currentMenu.x + 20, y = menu.currentMenu.y + 2 * MMenu.LINEHEIGHT + 4;
        Vector2 at = MenuPoint(x, y);
        _scene.MenuEvent(new InputEventMouseMotion { Position = at });
        _scene.MenuEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        if (menu.StateName != "closed" || flow.gameaction != gameaction_t.ga_newgame)
        {
            Fail(Step($"a click at {at} (menu point {x}, {y}) on Hurt Me Plenty"));
            return;
        }
        flow.G_DoGameActions();
        string first = _scene.DefaultMap();
        if (flow.gamestate != gamestate_t.GS_LEVEL || _scene.Mesh?.Level.Name != first || _scene.World is not { } world || world.gameskill != skill_t.sk_medium)
        {
            Fail($"the menus (T7.2): the skill menu started {_scene.Mesh?.Level.Name} on {_scene.World?.gameskill}, expected {first} on sk_medium");
            return;
        }

        // the world holds under the menu (p_tick.c), the status bar ticks on
        var none = new ticcmd_t();
        _scene.Tic(none);
        int leveltime = world.leveltime;
        _scene.MenuEvent(KeyEvent(Key.Escape));
        _scene.Tic(none);
        _scene.Tic(none);
        bool held = world.leveltime == leveltime;
        _scene.MenuEvent(KeyEvent(Key.Escape));
        _scene.Tic(none);
        if (!held || world.leveltime != leveltime + 1 || menu.Active)
            Fail($"the menus (T7.2): the world under the menu: leveltime {leveltime} -> {world.leveltime}, expected held for two tics, then one tic");
        else
            GD.Print($"Level check: menus (T7.2): opened by a key, Escape and the pad's Start; the pad's A{(commercial ? "" : ", Enter")}{(_scene.GameMode == GameMode.shareware ? ", the shareware message" : "")} and a click on Hurt Me Plenty started {first}; the world held under the menu");
        _scene.UpdateScreens();
    }

    /// <summary>
    /// T7.2, with a real renderer: the main menu over the title page on the
    /// menu screen: each drawn pixel's centre must be its index's colour in
    /// palette 0 (the menu's layer is over the page's).
    /// </summary>
    private async Task CheckMenuDrawn()
    {
        GameFlow flow = _scene.Flow;
        MMenu menu = flow.Menu;
        bool overlay = _scene.Overlay.Visible;
        _scene.Overlay.Visible = false;
        flow.D_StartTitle(null);
        _scene.Tic(new ticcmd_t());
        menu.M_StartControlPanel();
        _scene.UpdateScreens();
        if (!_scene.MenuScreens.Visible)
        {
            Fail("the main menu: the menu screen is hidden");
            return;
        }
        if (await Capture("the main menu over the title page") is not byte[] frame)
            return;
        Vector2I size = ViewSize();
        HudScreen screen = _scene.MenuScreens.Screen;
        int scale = _scene.MenuScreens.PixelScale;
        Vector2 at = _scene.MenuScreens.Rect.Position;
        byte[] pal = Playpal.GetPalette(0).ToArray();
        int bad = 0, here = 0;
        string firstBad = "";
        for (int y = 0; y < screen.Height; y++)
        {
            for (int x = 0; x < screen.Width; x++)
            {
                int k = y * screen.Width + x;
                if (screen.Opaque[k] == 0)
                    continue;
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
        if (here == 0)
            Fail("the main menu: nothing drawn");
        if (bad > 0)
            Fail($"the main menu: {bad} of {here} pixels differ; first: {firstBad}");
        menu.M_ClearMenus();
        _scene.UpdateScreens();
        if (_scene.MenuScreens.Visible)
            Fail("the menu screen still shows with the menus closed");
        _scene.Overlay.Visible = overlay;
        _pixels += here;
        GD.Print($"Level check: menus (T7.2): the main menu over the title page at scale {scale}, {here} pixels compared");
    }
}
