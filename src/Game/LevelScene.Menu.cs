using System;
using System.Collections.Generic;
using System.Linq;
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

    // T7.7: S_StartSound(NULL, sfx): the menus', the intermission's and the finale's sounds, heard everywhere.
    // Not while quitting: vanilla's M_QuitResponse never returns (I_WaitVBL, I_Quit), so the menu's
    // sfx_swtchx after a message's routine never cuts the quit sound.
    void IMenuHost.StartSound(sfxenum_t sfx)
    {
        if (!Quitting)
            Sound?.S_StartSound(null, 0, 0, sfx, SoundListener());
    }

    string? IMenuHost.SaveDescription(int slot) => Flow.SaveDescription(slot); // T7.6

    void IMenuHost.I_Quit()
    {
        GD.Print("Level: quit from the menu");
        QuitAfterSound(); // T7.7: I_WaitVBL(105), the quit sound's time
    }

    /// <summary>Sets the menus up for this scene (once the flow exists).</summary>
    private void InitMenus()
    {
        Menu.Host = this;
        Menu.SetupHost = this; // T7.3: the options' text pages
        if (_graphics is not null)
            Menu.Graphics = _graphics;
        // T7.3: yes and no come from their actions (menu_yes, menu_no), so a rebound Y is only a letter
        Menu.key_menu_confirm = KeyMenuYes;
        Menu.key_menu_abort = KeyMenuNo;
    }

    /// <summary>T7.3: the menus' yes and no (<see cref="MMenu.key_menu_confirm"/>, <see cref="MMenu.key_menu_abort"/>) from the <c>menu_yes</c> and <c>menu_no</c> actions, apart from the letters.</summary>
    public const int KeyMenuYes = 0x102, KeyMenuNo = 0x103;

    /// <summary>
    /// T7.3: the menu key of a menu action's input event (<see cref="Settings.MenuActions"/>,
    /// rebindable): open/close, the arrows, select (Enter; the pad's
    /// <see cref="MMenu.KEY_PAD_ACCEPT"/>), back (Backspace; the pad's
    /// <see cref="MMenu.KEY_PAD_CANCEL"/>), yes and no, and (T7.6, game
    /// actions on the Actions page) quicksave and quickload; 0 for none. The pad's
    /// directions are polled (<see cref="PollMenuPad"/>), not taken here.
    /// </summary>
    public static int MenuActionKey(InputEvent e, MMenu menu)
    {
        bool pad = e is InputEventJoypadButton or InputEventJoypadMotion;
        bool Is(string action) => InputMap.HasAction(action) && e.IsActionPressed(action, allowEcho: true);
        if (Is(GameInput.MenuOpen))
            return menu.key_menu_activate;
        if (Is(GameInput.MenuSelect))
            return pad ? MMenu.KEY_PAD_ACCEPT : menu.key_menu_forward;
        if (Is(GameInput.MenuBack))
            return pad ? MMenu.KEY_PAD_CANCEL : menu.key_menu_back;
        if (Is(GameInput.MenuYes))
            return menu.key_menu_confirm;
        if (Is(GameInput.MenuNo))
            return menu.key_menu_abort;
        if (Is(GameInput.QuickSave))
            return menu.key_menu_qsave; // T7.6: F6 by default
        if (Is(GameInput.QuickLoad))
            return menu.key_menu_qload; // T7.6: F9 by default
        if (pad)
            return 0;
        if (Is(GameInput.MenuUp))
            return menu.key_menu_up;
        if (Is(GameInput.MenuDown))
            return menu.key_menu_down;
        if (Is(GameInput.MenuLeft))
            return menu.key_menu_left;
        if (Is(GameInput.MenuRight))
            return menu.key_menu_right;
        return 0;
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
        if (menu.WaitingBinding is { } binding)
            return BindingEvent(e, menu, binding); // T7.3: the next input binds
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
                    int key = MenuActionKey(pad, menu); // T7.3: the menu actions' (A, B, Start by default)
                    if (menu.Active)
                    {
                        if (key != 0)
                            menu.M_Responder(key);
                        else if (pad.ButtonIndex == JoyButton.X)
                            menu.ClearBinding(true); // T7.3: on a binding page, the pad's bindings go
                        return true;
                    }
                    if (key == menu.key_menu_activate || key == menu.key_menu_qsave || key == menu.key_menu_qload)
                        return menu.M_Responder(key); // T7.6: quicksave and quickload, if bound to the pad
                    if (title && !IsMenuDirection(pad))
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
                    if (menu.Active && !menu.saveStringEnter && KeyOf(key) == Key.Delete && menu.ClearBinding(false))
                        return true; // T7.3: on a binding page, the keyboard's and mouse's bindings go
                    // T7.3: the menu actions' keys (rebindable), else a letter, digit or space; typing a save's description takes the keys as they are
                    int code = menu.saveStringEnter ? MenuKeyOf(key) : MenuActionKey(key, menu), ch = CharOf(key);
                    if (code == 0)
                        code = LetterKeyOf(key);
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

    /// <summary>A letter (lower case), a digit or a space as typed; 0 for any other key.</summary>
    public static int LetterKeyOf(InputEventKey key)
    {
        int code = MenuKeyOf(key);
        return code is ' ' or (>= 'a' and <= 'z') or (>= '0' and <= '9') ? code : 0;
    }

    /// <summary>Whether a pad button is one of the menus' directions (<c>menu_up</c>… : polled, not an "any key" on the title).</summary>
    private static bool IsMenuDirection(InputEventJoypadButton pad) =>
        new[] { GameInput.MenuUp, GameInput.MenuDown, GameInput.MenuLeft, GameInput.MenuRight }.Any(a => InputMap.HasAction(a) && InputMap.EventIsAction(pad, a, true));

    /// <summary>A key as doomkeys.h's code (Chocolate Doom's default menu keys, for typing a save's description), a lower-case letter, a digit or a space; 0 for any other.</summary>
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
        if (_flow is { } f)
            TickBindingWait(f.Menu, delta); // T7.3
        if (_flow is not { } flow || !flow.Menu.Active || flow.Menu.WaitingBinding is not null)
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

    /// <summary>The menu direction a pad holds: the pad inputs bound to <c>menu_up</c>, <c>menu_down</c>, <c>menu_left</c>, <c>menu_right</c> (T7.3; the D-pad and the left stick by default).</summary>
    private static int PadDirection(MMenu menu)
    {
        var devices = new List<int>(Input.GetConnectedJoypads());
        if (!devices.Contains(0))
            devices.Add(0); // the level script's pad
        foreach ((string action, int key) in new[] { (GameInput.MenuUp, menu.key_menu_up), (GameInput.MenuDown, menu.key_menu_down), (GameInput.MenuLeft, menu.key_menu_left), (GameInput.MenuRight, menu.key_menu_right) })
        {
            if (!InputMap.HasAction(action))
                continue;
            foreach (InputEvent e in InputMap.ActionGetEvents(action))
            {
                foreach (int d in devices)
                {
                    if (e is InputEventJoypadButton b && Input.IsJoyButtonPressed(d, b.ButtonIndex)
                        || e is InputEventJoypadMotion m && Input.GetJoyAxis(d, m.Axis) * Math.Sign(m.AxisValue) > 0.5f)
                        return key;
                }
            }
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
        (int Changes, gamestate_t gamestate, int gametic, bool pause, Vector2 Size, int palette) key = (menu.Changes, flow.gamestate, flow.gametic, pause, GetViewport().GetVisibleRect().Size, palette);
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
