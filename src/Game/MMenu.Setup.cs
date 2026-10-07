using System;
using System.Collections.Generic;
using IsoDoom.Sim;

namespace IsoDoom.Game;

/// <summary>
/// What the options' text pages (T7.3) need from the game scene: the
/// settings (<see cref="Settings"/>, read and changed at once, then saved),
/// the bindings' text, clearing and resetting them. The level scene
/// implements it; the tests fake it.
/// </summary>
public interface ISetupHost
{
    /// <summary>The value setting <paramref name="key"/> has now (its argument's form).</summary>
    string GetSetting(string key);

    /// <summary>Changes setting <paramref name="key"/> to <paramref name="value"/>: it applies at once and is saved.</summary>
    void SetSetting(string key, string value);

    /// <summary>What action <paramref name="action"/> is bound to, for the menu (capitals).</summary>
    string BindingText(string action);

    /// <summary>Unbinds action <paramref name="action"/>'s pad (<paramref name="pad"/>) or keyboard and mouse inputs.</summary>
    void ClearBindings(string action, bool pad);

    /// <summary>Every binding back to the defaults (<c>project.godot</c>'s).</summary>
    void ResetControls();

    /// <summary>Every setting and binding back to its default.</summary>
    void ResetSettings();
}

// T7.3: the options' text pages, reached from vanilla's options menu (its
// last item, "MORE OPTIONS..."): video, gameplay, sprites, HUD, sound (T7.8g) and the
// controls with their rebinding. Not vanilla: drawn in the message font,
// a value right of each item, ">" for the cursor.
public sealed partial class MMenu
{
    /// <summary>The text cursor's offset from a text page's items.</summary>
    public const int TEXTCURSORXOFF = -10;

    /// <summary>The rows between a text page's items.</summary>
    public const short TEXTLINEHEIGHT = 10;

    /// <summary>Where a text page's values start.</summary>
    public const int VALUEX = 176;

    private const short TextX = 18, TextY = 36;

    /// <summary>The scene's side of the text pages (settings and bindings); null in tests without one.</summary>
    public ISetupHost? SetupHost { get; set; }

    /// <summary>The action waiting for an input to bind (the glue takes the next key, mouse button or pad input), or null.</summary>
    public string? WaitingBinding { get; private set; }

    /// <summary>The label of <see cref="WaitingBinding"/>.</summary>
    public string WaitingLabel { get; private set; } = "";

    public menu_t SetupDef = null!, ControlsDef = null!;

    /// <summary>The settings pages, by page.</summary>
    public readonly Dictionary<SettingPage, menu_t> SettingDefs = new();

    /// <summary>The binding pages (<see cref="Settings.ControlPages"/>' order) and the action of each item.</summary>
    public readonly List<(menu_t Def, string[] Actions)> BindingDefs = new();

    public const string RESETALL = "reset every option and control\nto its default?\n\n" + PRESSYN;
    public const string RESETCONTROLS = "reset every control to its default?\n\n" + PRESSYN;

    private void SetupMenus()
    {
        var pages = new (string Name, string Title, SettingPage Page, char Key)[]
        {
            ("video", "VIDEO", SettingPage.Video, 'v'),
            ("gameplay", "GAMEPLAY", SettingPage.Gameplay, 'g'),
            ("sprites", "SPRITES", SettingPage.Sprites, 's'),
            ("hud", "HUD", SettingPage.Hud, 'h'),
            ("soundsetup", "SOUND", SettingPage.Sound, 'u'), // T7.8g: the volumes and the music's chip
        };

        var setupItems = new List<menuitem_t> { new(1, "", _ => M_SetupNextMenu(ControlsDef), 'c', "CONTROLS...") };
        SetupDef = TextMenu("setup", "MORE OPTIONS", OptionsDef, Array.Empty<menuitem_t>());
        foreach ((string name, string title, SettingPage page, char key) in pages)
        {
            menu_t def = TextMenu(name, title, SetupDef, SettingItems(page));
            SettingDefs[page] = def;
            setupItems.Add(new menuitem_t(1, "", _ => M_SetupNextMenu(def), key, title + "..."));
        }
        setupItems.Add(new menuitem_t(-1, "", null, '\0'));
        setupItems.Add(new menuitem_t(1, "", _ => M_StartMessage(RESETALL, M_ResetAllResponse, true), 'r', "RESET ALL TO DEFAULTS"));
        SetTextItems(SetupDef, setupItems.ToArray());

        var controls = new List<menuitem_t>(SettingItems(SettingPage.Controls));
        ControlsDef = TextMenu("controls", "CONTROLS", SetupDef, Array.Empty<menuitem_t>());
        controls.Add(new menuitem_t(-1, "", null, '\0'));
        foreach ((string title, (string Action, string Label)[] actions) in Settings.ControlPages)
        {
            var items = new menuitem_t[actions.Length];
            string[] names = new string[actions.Length];
            for (int i = 0; i < actions.Length; i++)
            {
                (string action, string label) = actions[i];
                names[i] = action;
                items[i] = new menuitem_t(1, "", _ => M_StartBinding(action, label), '\0', label)
                {
                    value = () => SetupHost?.BindingText(action) ?? "",
                };
            }
            menu_t def = TextMenu(title.Replace(" ", "").ToLowerInvariant(), title, ControlsDef, items);
            BindingDefs.Add((def, names));
            controls.Add(new menuitem_t(1, "", _ => M_SetupNextMenu(def), char.ToLowerInvariant(title[0]), title + "..."));
        }
        controls.Add(new menuitem_t(-1, "", null, '\0'));
        controls.Add(new menuitem_t(1, "", _ => M_StartMessage(RESETCONTROLS, M_ResetControlsResponse, true), 'r', "RESET CONTROLS"));
        SetTextItems(ControlsDef, controls.ToArray());
    }

    private menu_t TextMenu(string name, string title, menu_t prev, menuitem_t[] items)
    {
        var def = new menu_t(name, (short)items.Length, prev, items, null, TextX, TextY, 0)
        {
            textItems = true,
            lineHeight = TEXTLINEHEIGHT,
        };
        def.routine = () => M_DrawTextTitle(title, def);
        return def;
    }

    private static void SetTextItems(menu_t def, menuitem_t[] items)
    {
        def.menuitems = items;
        def.numitems = (short)items.Length;
    }

    /// <summary>A page's items: each setting as a slider (left and right step through its values; select steps on).</summary>
    private menuitem_t[] SettingItems(SettingPage page)
    {
        SettingDef[] defs = Settings.OnPage(page);
        var items = new menuitem_t[defs.Length];
        for (int i = 0; i < defs.Length; i++)
        {
            SettingDef def = defs[i];
            items[i] = new menuitem_t(2, "", choice => M_StepSetting(def, choice == 0 ? -1 : 1), '\0', def.Label)
            {
                value = () => Settings.Display(SetupHost?.GetSetting(def.Key) ?? def.Default),
            };
        }
        return items;
    }

    /// <summary>Steps setting <paramref name="def"/> one value on (or back) through the host: it applies at once.</summary>
    private void M_StepSetting(SettingDef def, int step)
    {
        if (SetupHost is not { } host)
            return;
        host.SetSetting(def.Key, def.Step(host.GetSetting(def.Key), step));
    }

    private void M_Setup(int choice) => M_SetupNextMenu(SetupDef);

    private void M_ResetAllResponse(int key)
    {
        if (key == key_menu_confirm)
            SetupHost?.ResetSettings();
    }

    private void M_ResetControlsResponse(int key)
    {
        if (key == key_menu_confirm)
            SetupHost?.ResetControls();
    }

    /// <summary>The next input binds to <paramref name="action"/> (the glue's <see cref="M_BindingDone"/>; Escape cancels).</summary>
    private void M_StartBinding(string action, string label)
    {
        WaitingBinding = action;
        WaitingLabel = label;
        Changes++;
    }

    /// <summary>The glue bound the input (or gave up): the page shows again.</summary>
    public void M_BindingDone()
    {
        WaitingBinding = null;
        S_StartSound(sfxenum_t.sfx_pistol);
        Changes++;
    }

    /// <summary>Escape (or the wait's time out): nothing is bound.</summary>
    public void M_CancelBinding()
    {
        WaitingBinding = null;
        S_StartSound(sfxenum_t.sfx_swtchx);
        Changes++;
    }

    /// <summary>The action of the binding page item the cursor is on, or null.</summary>
    public string? ActionOn()
    {
        if (!menuactive || messageToPrint)
            return null;
        foreach ((menu_t def, string[] actions) in BindingDefs)
        {
            if (def == currentMenu && itemOn >= 0 && itemOn < actions.Length)
                return actions[itemOn];
        }
        return null;
    }

    /// <summary>Clears the pad's (<paramref name="pad"/>) or the keyboard's and mouse's bindings of the item the cursor is on (Delete, the pad's X). Returns whether there was one.</summary>
    public bool M_ClearBinding(bool pad)
    {
        if (WaitingBinding is not null || ActionOn() is not { } action)
            return false;
        SetupHost?.ClearBindings(action, pad);
        S_StartSound(sfxenum_t.sfx_stnmov);
        Changes++;
        return true;
    }

    private void M_DrawTextTitle(string title, menu_t def)
    {
        M_WriteText(HudScreen.SCREENWIDTH / 2 - M_StringWidth(title) / 2, 16, title);
        bool bindings = BindingDefs.Exists(b => b.Def == def);
        string hint = bindings ? "ENTER: BIND   DEL / PAD X: CLEAR" : def.menuitems.Length > 0 && Array.Exists(def.menuitems, i => i.status == 2) ? "LEFT / RIGHT: CHANGE" : "";
        if (hint.Length > 0)
            M_WriteText(HudScreen.SCREENWIDTH / 2 - M_StringWidth(hint) / 2, 188, hint);
    }

    /// <summary>A text page (T7.3): its title, each item's text and value, the cursor; or the wait for an input to bind.</summary>
    private void M_DrawTextMenu()
    {
        if (WaitingBinding is not null)
        {
            string text = "PRESS A KEY, MOUSE BUTTON\nOR PAD INPUT FOR\n\n" + WaitingLabel + "\n\nESCAPE CANCELS";
            int y = HudScreen.SCREENHEIGHT / 2 - M_StringHeight(text) / 2;
            foreach (string line in text.Split('\n'))
            {
                M_WriteText(HudScreen.SCREENWIDTH / 2 - M_StringWidth(line) / 2, y, line);
                y += Graphics.FontHeight;
            }
            return;
        }
        currentMenu.routine?.Invoke();
        int my = currentMenu.y;
        for (int i = 0; i < currentMenu.numitems; i++)
        {
            menuitem_t item = currentMenu.menuitems[i];
            if (item.status != -1)
            {
                M_WriteText(currentMenu.x, my, item.text);
                if (item.value is { } value)
                    M_WriteText(VALUEX, my, value());
            }
            my += currentMenu.lineHeight;
        }
        M_WriteText(currentMenu.x + TEXTCURSORXOFF, currentMenu.y + itemOn * currentMenu.lineHeight, ">");
    }
}
