using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using IsoDoom.Sim;

namespace IsoDoom.Game;

// T7.3: the settings (Settings, LevelScene.Settings.cs) under the level check,
// which runs on the defaults and never reads or writes the user's file: every
// setting starts at its default; each changed to another value applies at
// once (the scene reads it back, and the frame cap, the volumes' buses, the
// mouse sensitivity, the aim assist in the world, the mesh's modes and the
// HUD are looked at); the options' text pages change one as a person's keys
// do; actions are rebound through the menus' wait for an input (a key, a pad
// button, a menu key); then the settings are written to a file of the
// check's own, reset, and read back from it as a restart would, and must be
// as they were; a reset puts every setting and binding back.
public partial class LevelCheck
{
    private void CheckSettings()
    {
        LevelScene s = _scene;
        int failures = _failures;
        if (s.SettingsPath is not null)
            Fail($"settings (T7.3): the level check reads and writes {s.SettingsPath}; it must run on the defaults");

        foreach (string action in GameInput.Actions())
        {
            if (!Settings.GameActions.Contains(action) && !Settings.MenuActions.Contains(action))
                Fail($"settings (T7.3): the action {action} is on no controls page");
        }

        // the defaults
        foreach (SettingDef def in Settings.Defs)
        {
            string now = s.GetSetting(def.Key);
            if (now != def.Default)
                Fail($"settings (T7.3): {def.Key} starts at \"{now}\", its default is \"{def.Default}\"");
        }
        foreach (string action in Settings.GameActions.Concat(Settings.MenuActions))
        {
            string now = s.GetSetting(Settings.BindingKey(action)), project = Binding.FormatList(LevelScene.DefaultBindings(action));
            if (now != project || project.Length == 0)
                Fail($"settings (T7.3): {action} is bound to \"{now}\", project.godot to \"{project}\"");
        }

        // each setting changed applies at once
        var changed = new Dictionary<string, string>();
        foreach (SettingDef def in Settings.Defs)
        {
            string value = def.Step(def.Default, 1);
            s.SetSetting(def.Key, value);
            changed[def.Key] = value;
            string now = s.GetSetting(def.Key);
            if (now != value)
                Fail($"settings (T7.3): {def.Key} set to \"{value}\" reads \"{now}\"");
        }
        CheckApplied(changed);

        // the options' text pages, as a person's keys reach them: Options, More Options, Sprites, Shadows → right
        MMenu menu = s.Menu;
        menu.M_ClearMenus();
        s.MenuEvent(KeyEvent(Key.Escape));
        s.MenuEvent(KeyEvent(Key.O));
        s.MenuEvent(KeyEvent(Key.Enter));
        s.MenuEvent(KeyEvent(Key.O));
        s.MenuEvent(KeyEvent(Key.Enter));
        s.MenuEvent(KeyEvent(Key.S));
        s.MenuEvent(KeyEvent(Key.Enter));
        string shadow = s.GetSetting("sprites/shadow");
        while (menu.StateName == "sprites" && menu.currentMenu.menuitems[menu.itemOn].text != "SHADOWS" && menu.itemOn < menu.currentMenu.numitems - 1)
            s.MenuEvent(KeyEvent(Key.Down));
        s.MenuEvent(KeyEvent(Key.Right));
        string expected = Settings.Find("sprites/shadow")!.Step(shadow, 1);
        if (menu.StateName != "sprites" || s.GetSetting("sprites/shadow") != expected || !string.Equals(s.SpriteOptions.Shadow.ToString(), expected, StringComparison.OrdinalIgnoreCase))
            Fail($"settings (T7.3): the sprites page's Shadows from \"{shadow}\" by the right arrow: {menu.StateText()}, the setting \"{s.GetSetting("sprites/shadow")}\", expected \"{expected}\"");
        changed["sprites/shadow"] = expected;

        // rebinding through the menus' wait: Use to F (Attack loses F), its pad input to Y, the menu key to F10
        s.MenuEvent(KeyEvent(Key.Backspace)); // the setup page
        s.MenuEvent(KeyEvent(Key.C));
        s.MenuEvent(KeyEvent(Key.Enter));
        s.MenuEvent(KeyEvent(Key.A));
        s.MenuEvent(KeyEvent(Key.Enter));
        if (menu.StateName != "actions")
            Fail($"settings (T7.3): C, A on the setup page: {menu.StateText()}, expected the actions page");
        Bind(Key.Enter, "attack", KeyEvent(Key.F));
        s.MenuEvent(KeyEvent(Key.Down));
        Bind(Key.Enter, "use", KeyEvent(Key.F));
        Bind(Key.Enter, "use", PadEvent(JoyButton.Y));
        bool useF = InputMap.EventIsAction(KeyEvent(Key.F), GameInput.Use, true), attackF = InputMap.EventIsAction(KeyEvent(Key.F), GameInput.Attack, true);
        bool useE = InputMap.EventIsAction(KeyEvent(Key.E), GameInput.Use, true), useY = InputMap.EventIsAction(PadEvent(JoyButton.Y), GameInput.Use, true);
        bool useA = InputMap.EventIsAction(PadEvent(JoyButton.A), GameInput.Use, true);
        if (!useF || attackF || useE || !useY || useA)
            Fail($"settings (T7.3): Use rebound to F and the pad's Y: use F {useF}, attack F {attackF} (taken), use E {useE}, use Y {useY}, use A {useA} (replaced)");
        s.MenuEvent(KeyEvent(Key.Delete));
        if (s.GetSetting(Settings.BindingKey(GameInput.Use)) != new Binding(BindingKind.PadButton, (long)JoyButton.Y).ToString())
            Fail($"settings (T7.3): Delete on Use left \"{s.GetSetting(Settings.BindingKey(GameInput.Use))}\", expected the pad's Y alone");
        Bind(Key.Enter, "use", KeyEvent(Key.F)); // back for the file
        s.MenuEvent(KeyEvent(Key.Enter));
        s.MenuEvent(KeyEvent(Key.Escape)); // Escape cancels the wait
        if (menu.WaitingBinding is not null || menu.StateName != "actions")
            Fail($"settings (T7.3): Escape did not cancel the wait for an input: {menu.StateText()}");
        s.MenuEvent(KeyEvent(Key.Backspace));
        s.MenuEvent(KeyEvent(Key.M));
        while (menu.StateName == "controls" && !menu.currentMenu.menuitems[menu.itemOn].text.StartsWith("MENU KEYS", StringComparison.Ordinal))
            s.MenuEvent(KeyEvent(Key.M));
        s.MenuEvent(KeyEvent(Key.Enter));
        if (menu.StateName != "menukeys")
            Fail($"settings (T7.3): the menu keys page: {menu.StateText()}");
        Bind(Key.Enter, GameInput.MenuOpen, KeyEvent(Key.F10));
        menu.M_ClearMenus();
        s.MenuEvent(KeyEvent(Key.Escape));
        bool escapeOpens = menu.Active;
        s.MenuEvent(KeyEvent(Key.F10));
        if (escapeOpens || !menu.Active)
            Fail($"settings (T7.3): the menu key rebound to F10: Escape opened the menus {escapeOpens}, F10 {menu.Active}");
        s.MenuEvent(KeyEvent(Key.F10));
        foreach (string action in new[] { GameInput.Attack, GameInput.Use, GameInput.MenuOpen })
            changed[Settings.BindingKey(action)] = s.GetSetting(Settings.BindingKey(action));

        // a restart: written to a file, everything reset, read back from it
        string path = Path.Combine(OS.GetUserDataDir(), $"level-check-settings-{System.Environment.ProcessId}.cfg");
        try
        {
            if (!LevelScene.SaveSettingsTo(path, s.SettingsValues))
            {
                Fail($"settings (T7.3): could not write {path}");
                return;
            }
            s.ResetSettings();
            foreach (string key in LevelScene.SettingKeys())
            {
                string now = s.GetSetting(key), def = s.SettingsValues.Value(key) ?? "";
                if (now != def || changed.TryGetValue(key, out string? c) && c == now)
                    Fail($"settings (T7.3): after the reset {key} is \"{now}\", its default \"{def}\"");
            }
            if (menu.Active)
                menu.M_ClearMenus();
            s.MenuEvent(KeyEvent(Key.Escape));
            if (!menu.Active)
                Fail("settings (T7.3): after the reset Escape does not open the menus");
            menu.M_ClearMenus();
            Dictionary<string, string>? loaded = LevelScene.LoadSettingsFrom(path);
            if (loaded is null)
            {
                Fail($"settings (T7.3): could not read {path} back");
                return;
            }
            s.ApplySettings(loaded, pin: false);
            foreach ((string key, string value) in changed)
            {
                string now = s.GetSetting(key);
                if (now != value)
                    Fail($"settings (T7.3): {key} was \"{value}\" before the restart, \"{now}\" after it (the file: \"{(loaded.TryGetValue(key, out string? f) ? f : "none")}\")");
            }
            CheckApplied(changed);
        }
        finally
        {
            File.Delete(path);
            s.ResetSettings();
            s.ApplySettings(null, pin: false);
            menu.M_ClearMenus();
        }
        if (_failures == failures)
            GD.Print($"Level check: settings (T7.3): {Settings.Defs.Length} settings and {Settings.GameActions.Length + Settings.MenuActions.Length} actions' bindings at their defaults; each changed applied at once; a page changed by the keys; Use, Attack and the menu key rebound through the menus; all kept through a file and a reset");
    }

    /// <summary>On the current menu item, <paramref name="start"/> starts the wait and <paramref name="input"/> binds to <paramref name="action"/>.</summary>
    private void Bind(Key start, string action, InputEvent input)
    {
        MMenu menu = _scene.Menu;
        _scene.MenuEvent(KeyEvent(start));
        if (menu.WaitingBinding != action)
        {
            Fail($"settings (T7.3): {start} did not wait for an input for {action}: {menu.StateText()}");
            return;
        }
        _scene.MenuEvent(input);
        if (menu.WaitingBinding is not null)
            Fail($"settings (T7.3): {input.AsText()} was not bound to {action}: {menu.StateText()}");
    }

    /// <summary>The changed settings' effects where the scene has more than the value read back.</summary>
    private void CheckApplied(Dictionary<string, string> changed)
    {
        LevelScene s = _scene;
        void Expect(string key, bool ok, string what)
        {
            if (changed.TryGetValue(key, out string? value) && !ok)
                Fail($"settings (T7.3): {key} = \"{value}\" did not apply: {what}");
        }
        Expect("video/frame_cap", Engine.MaxFps == int.Parse(changed["video/frame_cap"], CultureInfo.InvariantCulture), $"Engine.MaxFps {Engine.MaxFps}");
        foreach ((string key, string bus) in new[] { ("sound/sfx_volume", LevelScene.SfxBus), ("sound/music_volume", LevelScene.MusicBus) })
        {
            int i = AudioServer.GetBusIndex(bus);
            if (!changed.TryGetValue(key, out string? value))
                continue;
            int volume = int.Parse(value, CultureInfo.InvariantCulture);
            // T7.8e: the music bus only mutes; the volume is the OPL driver's (the notes' levels)
            float db = bus == LevelScene.MusicBus ? LevelScene.MusicBusDb(volume) : LevelScene.VolumeDb(volume);
            Expect(key, i >= 0 && Mathf.IsEqualApprox(AudioServer.GetBusVolumeDb(i), db), $"bus {bus} at {(i >= 0 ? AudioServer.GetBusVolumeDb(i) : float.NaN)} dB");
        }
        Expect("controls/mouse_sensitivity", s.TiccmdBuilder.MouseSensitivity == int.Parse(changed["controls/mouse_sensitivity"], CultureInfo.InvariantCulture), $"the builder's {s.TiccmdBuilder.MouseSensitivity}");
        if (s.World is { } world)
            Expect("gameplay/aim_assist", world.tweaks.AimAssistCone == s.Tweaks.AimAssistCone && world.tweaks.AimAssistCone != Tweaks.DefaultAimAssistCone, $"the world's cone {world.tweaks.AimAssistCone}");
        if (s.Mesh is { } mesh)
        {
            Expect("gameplay/light", string.Equals(mesh.LightMode.ToString(), changed["gameplay/light"], StringComparison.OrdinalIgnoreCase), $"the mesh's {mesh.LightMode}");
            Expect("gameplay/masked_back", (mesh.MaskedBacks == Render.MaskedBackFaces.Mirrored ? "mirror" : "off") == changed["gameplay/masked_back"], $"the mesh's {mesh.MaskedBacks}");
        }
        Expect("hud/mode", string.Equals(s.Hud.Mode.ToString(), changed["hud/mode"], StringComparison.OrdinalIgnoreCase), $"the HUD's {s.Hud.Mode}");
        if (s.MusicDevice is { } music && changed.TryGetValue("sound/opl", out string? opl))
        {
            // T7.8g: the player (and its driver, with a bank) switched at once
            bool opl3 = opl == "opl3", driver;
            lock (music.Lock)
                driver = music.Driver?.Opl3Mode ?? opl3;
            Expect("sound/opl", music.Opl3 == opl3 && driver == opl3, $"the player's OPL3 {music.Opl3}, its driver's {driver}");
        }
        Expect("hud/messages", (s.Menu.showMessages != 0 ? "on" : "off") == changed["hud/messages"], $"showMessages {s.Menu.showMessages}");
    }
}
