using System;
using System.Collections.Generic;
using Godot;
using IsoDoom.Sim;

namespace IsoDoom.Game;

// T7.2: the menus (MMenu, m_menu.c) in the game scene: their host (the HUD
// as the screen size, the menus' messages, quitting), the input (the keys
// as doomkeys.h's codes, the pad's buttons and directions, the mouse), and
// their screen over everything (MenuScreens).
public partial class LevelScene : IMenuHost
{
    /// <summary>T7.2: the menus' screen (see-through but for the read-this pages), over the HUD and the other screens.</summary>
    public ScreenView MenuScreens { get; } = new() { Name = "MenuScreens", Layer = 2 };

    // What the menu screen showed last (redrawn when it changes).
    private (int Changes, gamestate_t State, int Tic, bool Pause, Vector2 Size, int Palette)? _menuKey;

    // The menus' message for the next HU_Ticker (players[consoleplayer].message).
    private (string Text, bool DontFuckWithMe)? _menuMessage;

    // The pad's direction held in the menus (a doomkeys.h arrow, or 0) and the time to its next repeat.
    private int _padDirection;
    private double _padRepeat;

    // The menus were up: fire and use held since then don't reach the game until let go.
    private bool _menuButtonsHeld;

    // The read-this screens showed (D_Display's inhelpscreensstate): the status bar is redrawn whole when they go.
    private bool _helpShown, _redrawStatusBar;

    /// <summary>The pad's direction repeats after this long held, then every <see cref="PadRepeatSeconds"/>.</summary>
    public const double PadDelaySeconds = 0.4, PadRepeatSeconds = 0.12;

    /// <summary>The menus (m_menu.c, T7.2): the game's flow's.</summary>
    public MMenu Menu => Flow.Menu;

    int IMenuHost.ScreenSize
    {
        get => (int)Hud.Mode;
        set => Hud.Mode = (HudMode)Math.Clamp(value, 0, MMenu.ScreenSizes - 1);
    }

    void IMenuHost.PlayerMessage(string text, bool dontfuckwithme) => _menuMessage = (text, dontfuckwithme);

    void IMenuHost.StartSound(sfxenum_t sfx)
    {
        // S_StartSound(NULL, sfx): played from T7.7
    }

    string? IMenuHost.SaveDescription(int slot) => null; // the saves are T7.6's

    void IMenuHost.I_Quit()
    {
        GD.Print("Level: quit from the menu");
        GetTree().Quit();
    }

    /// <summary>Sets the menus up for this scene (once the flow exists).</summary>
    private void InitMenus()
    {
        Menu.Host = this;
        if (_graphics is not null)
            Menu.Graphics = _graphics;
    }

    /// <summary>
    /// T7.2: m_menu.c <c>M_Responder</c> (and g_game.c <c>G_Responder</c>'s
    /// "any key on the title loop opens the menu") for an input event:
    /// keys as doomkeys.h's codes and the typed character; the pad's A and
    /// B (yes and no on a message) and Start (Escape; its directions are
    /// polled, <see cref="PollMenuPad"/>); the mouse's pointer moves the
    /// skull, its left button works the item under it, the right one goes
    /// back, the wheel moves up and down. On the title loop any key (but the
    /// function keys, the debug overlay's), mouse button or pad button
    /// opens the main menu. Returns whether the menus took the event.
    /// </summary>
    public bool MenuEvent(InputEvent e)
    {
        if (_flow is not { } flow)
            return false;
        MMenu menu = flow.Menu;
        bool title = flow.gamestate == gamestate_t.GS_DEMOSCREEN;
        switch (e)
        {
            case InputEventMouseMotion motion:
                if (menu.Active)
                {
                    Vector2I at = MenuScreens.ToScreen(motion.Position);
                    menu.M_MouseMove(at.X, at.Y);
                }
                return false;
            case InputEventMouseButton { Pressed: true } button:
                if (menu.Active)
                {
                    Vector2I at = MenuScreens.ToScreen(button.Position == Vector2.Zero ? GetViewport().GetMousePosition() : button.Position);
                    switch (button.ButtonIndex)
                    {
                        case MouseButton.Left: menu.M_MouseButton(true, at.X, at.Y); break;
                        case MouseButton.Right: menu.M_MouseButton(false, at.X, at.Y); break;
                        case MouseButton.WheelUp: menu.M_Responder(menu.key_menu_up); break;
                        case MouseButton.WheelDown: menu.M_Responder(menu.key_menu_down); break;
                    }
                    return true;
                }
                if (title && button.ButtonIndex is MouseButton.Left or MouseButton.Right or MouseButton.Middle)
                {
                    menu.M_StartControlPanel();
                    return true;
                }
                return false;
            case InputEventMouseButton:
                return menu.Active;
            case InputEventJoypadButton { Pressed: true } pad:
                {
                    int key = pad.ButtonIndex switch
                    {
                        JoyButton.A => MMenu.KEY_PAD_ACCEPT,
                        JoyButton.B => MMenu.KEY_PAD_CANCEL,
                        JoyButton.Start => menu.key_menu_activate,
                        _ => 0,
                    };
                    if (menu.Active)
                    {
                        if (key != 0)
                            menu.M_Responder(key);
                        return true;
                    }
                    if (key == menu.key_menu_activate)
                        return menu.M_Responder(key);
                    if (title && pad.ButtonIndex is not (JoyButton.DpadUp or JoyButton.DpadDown or JoyButton.DpadLeft or JoyButton.DpadRight))
                    {
                        menu.M_StartControlPanel();
                        return true;
                    }
                    return false;
                }
            case InputEventJoypadButton:
                return menu.Active;
            case InputEventKey { Pressed: true } key:
                {
                    if (key.Echo && !menu.Active)
                        return false;
                    if (KeyOf(key) == Key.Escape && Input.MouseMode == Input.MouseModeEnum.Captured)
                        return false; // the free-fly camera lets the mouse go first
                    int code = MenuKeyOf(key), ch = CharOf(key);
                    if ((code != 0 || ch != 0) && menu.M_Responder(code, ch))
                        return true;
                    if (title && !key.Echo && !IsFunctionKey(key))
                    {
                        menu.M_StartControlPanel();
                        return true;
                    }
                    return false;
                }
        }
        return false;
    }

    private static Key KeyOf(InputEventKey key) => key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;

    /// <summary>A key as doomkeys.h's code (the menu keys), a lower-case letter, a digit or a space; 0 for any other.</summary>
    public static int MenuKeyOf(InputEventKey key) => KeyOf(key) switch
    {
        Key.Escape => MMenu.KEY_ESCAPE,
        Key.Enter or Key.KpEnter => MMenu.KEY_ENTER,
        Key.Backspace => MMenu.KEY_BACKSPACE,
        Key.Up or Key.Kp8 => MMenu.KEY_UPARROW,
        Key.Down or Key.Kp2 => MMenu.KEY_DOWNARROW,
        Key.Left or Key.Kp4 => MMenu.KEY_LEFTARROW,
        Key.Right or Key.Kp6 => MMenu.KEY_RIGHTARROW,
        Key.Space => ' ',
        >= Key.A and <= Key.Z and var k => 'a' + (int)(k - Key.A),
        >= Key.Key0 and <= Key.Key9 and var k => '0' + (int)(k - Key.Key0),
        _ => 0,
    };

    /// <summary>The character a key types (for a save's description): its Unicode, else a scripted key's letter or digit.</summary>
    private static int CharOf(InputEventKey key)
    {
        if (key.Unicode is > 0 and < 128)
            return (int)key.Unicode;
        int code = MenuKeyOf(key);
        return code is >= ' ' and < 128 ? code : 0;
    }

    private static bool IsFunctionKey(InputEventKey key) => KeyOf(key) is >= Key.F1 and <= Key.F12;

    /// <summary>
    /// T7.2: the pad's directions in the menus (the D-pad and the left
    /// stick, any pad): a press moves once, holding repeats after
    /// <see cref="PadDelaySeconds"/> every <see cref="PadRepeatSeconds"/>.
    /// </summary>
    private void PollMenuPad(double delta)
    {
        if (_flow is not { } flow || !flow.Menu.Active)
        {
            _padDirection = 0;
            return;
        }
        int direction = PadDirection(flow.Menu);
        if (direction == 0)
        {
            _padDirection = 0;
            return;
        }
        if (direction != _padDirection)
        {
            _padDirection = direction;
            _padRepeat = PadDelaySeconds;
            flow.Menu.M_Responder(direction);
            return;
        }
        _padRepeat -= delta;
        if (_padRepeat <= 0)
        {
            _padRepeat += PadRepeatSeconds;
            flow.Menu.M_Responder(direction);
        }
    }

    private static int PadDirection(MMenu menu)
    {
        var devices = new List<int>(Input.GetConnectedJoypads());
        if (!devices.Contains(0))
            devices.Add(0); // the level script's pad
        foreach (int d in devices)
        {
            float x = Input.GetJoyAxis(d, JoyAxis.LeftX), y = Input.GetJoyAxis(d, JoyAxis.LeftY);
            if (Input.IsJoyButtonPressed(d, JoyButton.DpadUp) || y < -0.5f)
                return menu.key_menu_up;
            if (Input.IsJoyButtonPressed(d, JoyButton.DpadDown) || y > 0.5f)
                return menu.key_menu_down;
            if (Input.IsJoyButtonPressed(d, JoyButton.DpadLeft) || x < -0.5f)
                return menu.key_menu_left;
            if (Input.IsJoyButtonPressed(d, JoyButton.DpadRight) || x > 0.5f)
                return menu.key_menu_right;
        }
        return 0;
    }

    /// <summary>
    /// T7.2: the console player's input for a tic while the menus are up:
    /// none (vanilla's <c>M_Responder</c> takes the keys); after they close,
    /// fire and use held since stay off until let go (the click or the A that
    /// closed them is no shot or use).
    /// </summary>
    private TiccmdInput MenuFilter(TiccmdInput input)
    {
        if (_flow?.Menu.Active == true)
        {
            _menuButtonsHeld = true;
            return new TiccmdInput();
        }
        if (_menuButtonsHeld)
        {
            if (Input.IsActionPressed(GameInput.Attack) || Input.IsActionPressed(GameInput.Use))
                return input with { Attack = false, Use = false };
            _menuButtonsHeld = false;
        }
        return input;
    }

    /// <summary>
    /// T7.2: m_menu.c <c>M_Drawer</c> on <see cref="MenuScreens"/> when it
    /// changed, in the screens' palette (the level's in a level), centred;
    /// a read-this page over a black backdrop. When a read-this page goes,
    /// the status bar is redrawn whole (d_main.c <c>D_Display</c>'s
    /// <c>redrawsbar</c>).
    /// </summary>
    private void UpdateMenuScreens(GameFlow flow, int palette, bool pause)
    {
        MMenu menu = flow.Menu;
        if (!menu.Active || Playpal is null)
        {
            MenuScreens.Visible = false;
            _menuKey = null;
            HelpGone();
            return;
        }
        var key = (menu.Changes, flow.gamestate, flow.gametic, pause, GetViewport().GetVisibleRect().Size, palette);
        if (_menuKey == key && MenuScreens.Visible)
            return;
        _menuKey = key;
        HudScreen screen = MenuScreens.Screen;
        screen.Clear();
        menu.M_Drawer(screen);
        if (menu.inhelpscreens)
            _helpShown = true;
        else
            HelpGone();
        MenuScreens.Show(Playpal, palette, menu.inhelpscreens, Hud.FixedScale, centre: true);
    }

    private void HelpGone()
    {
        if (_helpShown)
            _redrawStatusBar = true; // D_Display: inhelpscreensstate && !inhelpscreens
        _helpShown = false;
    }
}
