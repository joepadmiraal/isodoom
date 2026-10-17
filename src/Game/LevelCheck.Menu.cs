using System;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Render;
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
    private static InputEventKey KeyEvent(Key key) => Held(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });

    private static InputEventJoypadButton PadEvent(JoyButton button) => Held(new InputEventJoypadButton { ButtonIndex = button, Pressed = true });

    /// <summary>
    /// T8.3d: the events the check makes, held for its run. An event made for
    /// one call (<c>_scene.MenuEvent(KeyEvent(…))</c>) is otherwise only its
    /// wrapper's, which the finalizer thread could free during a native call
    /// on it (SPEC §12 T8.3d, <see cref="GodotRefs"/>).
    /// </summary>
    private static T Held<T>(T e)
        where T : InputEvent
    {
        _heldEvents.Add(e);
        return e;
    }

    private static readonly System.Collections.Generic.List<InputEvent> _heldEvents = [];

    /// <summary>The viewport point of vanilla's screen point <paramref name="x"/>, <paramref name="y"/> on the menu screen (its pixel's centre).</summary>
    private Vector2 MenuPoint(int x, int y)
    {
        (float vx, float vy) = _scene.MenuScreens.Layout.ToViewport(x + 0.5f, y + 0.5f);
        return new Vector2(vx, vy);
    }

    /// <summary>T7.11: the viewport pixel at the centre of screen pixel <paramref name="x"/>, <paramref name="y"/>'s block in <paramref name="layout"/>.</summary>
    private static (int X, int Y) BlockCentre(ScreenLayout layout, int x, int y)
    {
        (float vx, float vy) = layout.ToViewport(x + 0.5f, y + 0.5f);
        return ((int)MathF.Floor(vx), (int)MathF.Floor(vy));
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
        _scene.MenuEvent(Held(new InputEventMouseMotion { Position = at }));
        _scene.MenuEvent(Held(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at }));
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
        CheckCrosshairPointer(flow);
    }

    /// <summary>
    /// T7.12: the crosshair pointer's image (a white centre dot and arms with a
    /// gap, outlined black, transparent elsewhere, whole at scale 2) and when it
    /// shows: on the level under the game camera, not under the menu, not with
    /// the option off, not under the other cameras.
    /// </summary>
    private void CheckCrosshairPointer(GameFlow flow)
    {
        int c = CrosshairPointer.BaseSize / 2;
        foreach (int scale in new[] { 1, 2 })
        {
            Image image = CrosshairPointer.Build(scale);
            Color At(int x, int y) => image.GetPixel(x * scale + scale - 1, y * scale + scale - 1);
            Vector2 hotspot = CrosshairPointer.Hotspot(scale);
            string? wrong =
                image.GetWidth() != CrosshairPointer.BaseSize * scale || image.GetHeight() != image.GetWidth() ? $"size {image.GetSize()}"
                : (int)hotspot.X / scale != c || (int)hotspot.Y / scale != c ? $"hotspot {hotspot}"
                : At(c, c) != Colors.White ? $"centre {At(c, c)}"
                : At(c + 1, c).A != 1 || At(c + 1, c) != Colors.Black ? $"the gap's outline {At(c + 1, c)}"
                : At(c + 2, c).A != 0 ? $"the gap {At(c + 2, c)}"
                : At(c + 3, c) != Colors.Black ? $"the arm's outline {At(c + 3, c)}"
                : At(c + CrosshairPointer.ArmStart, c) != Colors.White || At(c, c - CrosshairPointer.ArmEnd) != Colors.White ? "an arm"
                : At(c + CrosshairPointer.ArmEnd + 1, c) != Colors.Black || At(c + 1, c + CrosshairPointer.ArmStart) != Colors.Black ? "the outline"
                : At(0, 0).A != 0 || At(c + 3, c + 3).A != 0 ? "the background"
                : null;
            if (wrong is not null)
                Fail($"the crosshair pointer (T7.12): at scale {scale}: {wrong}");
        }

        // the rule with the menus as they are (the check has no game camera of its own)
        bool Wanted(bool option, bool gameCamera) => LevelScene.CrosshairWanted(option, gameCamera, flow.gamestate, flow.Menu.Active);
        bool level = flow.gamestate == gamestate_t.GS_LEVEL && Wanted(true, true);
        bool off = Wanted(false, true), noCamera = Wanted(true, false);
        _scene.MenuEvent(KeyEvent(Key.Escape));
        bool menu = !flow.Menu.Active || Wanted(true, true);
        _scene.MenuEvent(KeyEvent(Key.Escape));
        bool scene = _scene.CrosshairWanted(flow) != (_scene.PointerCrosshair && _scene.IsoActive);
        if (!level || menu || off || noCamera || scene)
            Fail($"the crosshair pointer (T7.12): shown on the level {level} (expected true), under the menu {menu}, with the option off {off}, without the game camera {noCamera}, the scene's own rule differing {scene} (expected false)");
        else
            GD.Print("Level check: crosshair pointer (T7.12): its image at scales 1 and 2; shown on the level under the game camera, not under the menu, with the option off or under another camera");
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
        if (await CompareMenuScreen("the main menu over the title page") is not int here)
            return;
        ScreenLayout layout = _scene.MenuScreens.Layout;
        // T11.9b: Options → More Options, our own text page: over the title page dimmed
        foreach (Key key in new[] { Key.O, Key.Enter, Key.O, Key.Enter })
            _scene.MenuEvent(KeyEvent(key));
        _scene.UpdateScreens();
        if (menu.StateName != "setup")
            Fail($"the more options page (T11.9b): the menu shows {menu.StateText()}");
        else
            here += await CompareMenuScreen("the more options page over the title page, dimmed (T11.9b)", dimmed: true) ?? 0;
        menu.M_ClearMenus();
        _scene.UpdateScreens();
        if (_scene.MenuScreens.Visible)
            Fail("the menu screen still shows with the menus closed");
        _scene.Overlay.Visible = overlay;
        GD.Print($"Level check: menus (T7.2): the main menu over the title page at scale {layout.ScaleX}, and the more options page over it dimmed (T11.9b), {here} pixels compared");
    }

    /// <summary>
    /// Captures what shows and compares every pixel the menus drew on their
    /// screen (<see cref="MMenu.M_Drawer"/>, the CPU's) with the frame, in
    /// palette 0; T11.9b: and every other pixel of the window with the frame
    /// captured without the menus' layer, dimmed (<see cref="ScreenView.DimRgb"/>)
    /// when <paramref name="dimmed"/> (our own text pages) and the same when
    /// not (vanilla's patch menus). Returns the pixels compared (counted in
    /// the check's), or null when the capture failed or nothing was drawn.
    /// </summary>
    private async Task<int?> CompareMenuScreen(string what, bool dimmed = false)
    {
        if (await Capture(what) is not byte[] frame)
            return null;
        Vector2I size = ViewSize();
        HudScreen screen = _scene.MenuScreens.Screen;
        ScreenLayout layout = _scene.MenuScreens.Layout;
        if (_scene.MenuScreens.Dimmed != dimmed || _scene.Flow.Menu.TextPageDrawn != dimmed)
            Fail($"{what}: the window behind {(_scene.MenuScreens.Dimmed ? "dimmed" : "not dimmed")} (a text page drawn: {_scene.Flow.Menu.TextPageDrawn}), expected {(dimmed ? "dimmed" : "not dimmed")} (T11.9b)");
        _scene.MenuScreens.Visible = false;
        byte[]? under = await Capture($"{what}, without the menus");
        _scene.MenuScreens.Visible = true;
        if (under is null)
            return null;
        int behind = 0, badBehind = 0;
        string firstBehind = "";
        for (int y = 0; y < size.Y; y++)
        {
            for (int x = 0; x < size.X; x++)
            {
                (int mx, int my) = layout.ToScreen(x + 0.5f, y + 0.5f);
                if (mx >= 0 && my >= 0 && mx < screen.Width && my < screen.Height && screen.Opaque[my * screen.Width + mx] != 0)
                    continue;
                int i = (y * size.X + x) * 4;
                behind++;
                bool ok = true;
                for (int c = 0; c < 3; c++)
                {
                    int expected = dimmed ? ScreenView.DimRgb(under[i + c]) : under[i + c];
                    ok &= Math.Abs(frame[i + c] - expected) <= 1;
                }
                if (!ok && badBehind++ == 0)
                    firstBehind = $"({x}, {y}): ({frame[i]}, {frame[i + 1]}, {frame[i + 2]}) over ({under[i]}, {under[i + 1]}, {under[i + 2]})";
            }
        }
        if (badBehind > 0)
            Fail($"{what}: {badBehind} of the {behind} pixels behind the menu are not {(dimmed ? "dimmed" : "as without it")} (T11.9b); first {firstBehind}");
        _pixels += behind;
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
                (int sx, int sy) = BlockCentre(layout, x, y);
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
        {
            Fail($"{what}: nothing drawn");
            return null;
        }
        if (bad > 0)
            Fail($"{what}: {bad} of {here} pixels differ; first: {firstBad}");
        _pixels += here;
        return here + behind; // (T11.9b: the pixels behind the menu too)
    }
}
