using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using IsoDoom.Map;
using IsoDoom.Render;
using IsoDoom.Sim;

namespace IsoDoom.Game;

// T7.3: the settings (Settings, SPEC §8) in the game scene: each one's reader
// and applier, loading and saving user://settings.cfg (or --settings=FILE),
// the command line's overrides, the window, vsync, frame cap and audio buses,
// the bindings in the InputMap, and the options' text pages' host (ISetupHost).
public partial class LevelScene : ISetupHost
{
    /// <summary>The settings file of a normal run.</summary>
    public const string DefaultSettingsPath = WadLocator.SettingsPath;

    /// <summary>The audio buses the volumes set (T7.7's sounds and T7.8's music play on them).</summary>
    public const string SfxBus = "Sfx", MusicBus = "Music";

    /// <summary>How long the menus wait for an input to bind before giving up (a pad has no Escape).</summary>
    public const double BindingWaitSeconds = 8;

    /// <summary>
    /// The settings file in use: <c>--settings=FILE</c>, none with
    /// <c>--settings=off</c>, else <see cref="DefaultSettingsPath"/> but in
    /// headless runs and under the checks, the level script and screenshots,
    /// which run on the defaults and save nothing, so the user's settings
    /// never change their results. Null: nothing is read or written.
    /// </summary>
    public string? SettingsPath { get; private set; }

    /// <summary>The saved settings and what this run changed (<see cref="SettingValues"/>).</summary>
    public SettingValues SettingsValues { get; private set; } = new();

    private readonly Dictionary<string, (Func<string> Get, Action<string> Set)> _settingAccess = new(StringComparer.Ordinal);

    // The video settings (applied to the window unless headless or under the level check).
    private string _windowSize = "1280x800", _fullscreen = "off", _vsync = "on";

    // The game camera's settings when there is no game camera (the level check).
    private string _zoom = "640", _pitch = "55", _projection = "ortho";

    private double _bindingWait;

    /// <summary>Whether the settings touch the window (not headless, not under the level check, which keeps its window as it is).</summary>
    private static bool CanApplyVideo => DisplayServer.GetName() != "headless" && !IsCheckRun;

    private static string? ChooseSettingsPath()
    {
        if (WadLocator.GetUserArg("--settings") is string arg)
            return arg == "off" ? null : arg;
        bool debugRun = IsCheckRun || WadLocator.HasUserArg("--level-script") || WadLocator.HasUserArg("--level-screenshot")
            || WadLocator.HasUserArg("--viewer-check") || WadLocator.HasUserArg("--viewer-screenshots");
        return debugRun || DisplayServer.GetName() == "headless" ? null : DefaultSettingsPath;
    }

    /// <summary>Every setting key: <see cref="Settings.Defs"/>' and each rebindable action's.</summary>
    public static IEnumerable<string> SettingKeys() =>
        Settings.Defs.Select(d => d.Key).Concat(Settings.GameActions.Concat(Settings.MenuActions).Select(Settings.BindingKey));

    /// <summary>
    /// T7.3: reads the settings file and applies its values but those the
    /// command line gives (pinned: <see cref="SettingValues.Pin"/>); call once
    /// the flow, the menus, the game camera and the arguments are set up.
    /// </summary>
    private void InitSettings()
    {
        BuildSettingAccess();
        EnsureAudioBus(SfxBus);
        EnsureAudioBus(MusicBus);
        SettingsPath = ChooseSettingsPath();
        ApplySettings(SettingsPath is null ? null : LoadSettingsFrom(SettingsPath), pin: true);
        GetViewport().SizeChanged += WindowResized;
        SyncSettings(bindings: true);
        GD.Print(SettingsPath is null ? "Settings: defaults (no settings file)" : $"Settings: {ProjectSettings.GlobalizePath(SettingsPath)}");
    }

    /// <summary>
    /// Takes <paramref name="loaded"/> (a settings file's values, or none) as
    /// the saved settings and applies them, but (with <paramref name="pin"/>)
    /// those the command line gives, which stay as the arguments set them.
    /// A value that can't be applied is dropped with a warning.
    /// </summary>
    public void ApplySettings(IReadOnlyDictionary<string, string>? loaded, bool pin)
    {
        SettingsValues = new SettingValues(loaded);
        foreach (SettingDef def in Settings.Defs)
            SettingsValues.SetDefault(def.Key, def.Default);
        foreach (string action in Settings.GameActions.Concat(Settings.MenuActions))
            SettingsValues.SetDefault(Settings.BindingKey(action), Binding.FormatList(DefaultBindings(action)));

        foreach (string key in SettingKeys())
        {
            SettingDef? def = Settings.Find(key);
            if (pin && def is not null && def.Args.Any(WadLocator.HasUserArg))
            {
                SettingsValues.Pin(key, _settingAccess[key].Get());
                continue;
            }
            if (SettingsValues.Saved(key) is not string saved)
                continue;
            try
            {
                _settingAccess[key].Set(saved);
            }
            catch (Exception e) when (e is ArgumentException or FormatException)
            {
                GD.PushWarning($"{SettingsPath}: {key} = \"{saved}\": {e.Message}; the default is used");
                SettingsValues.Reset(key);
            }
        }
        ApplyWindow();
        ApplyVsync();
        UpdateAudioBuses();
        if (_flow is not null)
            TiccmdBuilder.MouseSensitivity = Menu.mouseSensitivity;
    }

    /// <summary>
    /// T7.3: notes what changed in the settings since the last call (the
    /// menus, the debug keys, the camera's zoom) and saves the file when
    /// something did. Every frame; the bindings only with <paramref name="bindings"/>
    /// (they change only through <see cref="BindAction"/> and the resets).
    /// </summary>
    public void SyncSettings(bool bindings = false)
    {
        if (_settingAccess.Count == 0)
            return;
        bool dirty = false;
        foreach (SettingDef def in Settings.Defs)
            dirty |= SettingsValues.Observe(def.Key, _settingAccess[def.Key].Get());
        if (bindings)
        {
            foreach (string action in Settings.GameActions.Concat(Settings.MenuActions))
                dirty |= SettingsValues.Observe(Settings.BindingKey(action), _settingAccess[Settings.BindingKey(action)].Get());
        }
        if (_flow is not null)
            TiccmdBuilder.MouseSensitivity = Menu.mouseSensitivity;
        UpdateAudioBuses();
        if (dirty)
            SaveSettings();
    }

    /// <summary>Writes the settings that differ from their defaults to <see cref="SettingsPath"/> (keeping its other sections, the IWAD's).</summary>
    public void SaveSettings()
    {
        if (SettingsPath is null)
            return;
        SaveSettingsTo(SettingsPath, SettingsValues);
    }

    /// <summary>Writes <paramref name="values"/> to <paramref name="path"/> (its other sections kept). Returns whether it worked.</summary>
    public static bool SaveSettingsTo(string path, SettingValues values)
    {
        var config = new ConfigFile();
        config.Load(path); // a missing file is fine
        (List<(string Key, string Value)> keep, List<string> drop) = values.ToSave();
        foreach ((string key, string value) in keep)
            config.SetValue(key[..key.IndexOf('/')], key[(key.IndexOf('/') + 1)..], value);
        foreach (string key in drop)
        {
            string section = key[..key.IndexOf('/')], name = key[(key.IndexOf('/') + 1)..];
            if (config.HasSectionKey(section, name))
                config.EraseSectionKey(section, name);
        }
        Error err = config.Save(path);
        if (err != Error.Ok)
            GD.PushWarning($"Could not save {path}: {err}");
        return err == Error.Ok;
    }

    /// <summary>Reads <paramref name="path"/>'s settings (not the IWAD's) as <see cref="InitSettings"/> does, or null when it can't.</summary>
    public static Dictionary<string, string>? LoadSettingsFrom(string path)
    {
        var config = new ConfigFile();
        if (config.Load(path) != Error.Ok)
            return null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string section in config.GetSections())
        {
            if (section == "wad")
                continue;
            foreach (string key in config.GetSectionKeys(section))
                values[$"{section}/{key}"] = config.GetValue(section, key).AsString();
        }
        return values;
    }

    // ---- ISetupHost ----

    /// <inheritdoc/>
    public string GetSetting(string key) =>
        _settingAccess.TryGetValue(key, out var access) ? access.Get() : throw new ArgumentException($"no setting \"{key}\"");

    /// <inheritdoc/>
    public void SetSetting(string key, string value)
    {
        if (!_settingAccess.TryGetValue(key, out var access))
            throw new ArgumentException($"no setting \"{key}\"");
        access.Set(value);
        SyncSettings(bindings: key.StartsWith("controls/", StringComparison.Ordinal) && Settings.Find(key) is null);
    }

    /// <inheritdoc/>
    public string BindingText(string action)
    {
        if (!InputMap.HasAction(action))
            return "-";
        var names = BindingsOf(action).OrderBy(b => b.IsPad).Select(BindingName).ToList();
        return names.Count == 0 ? "-" : string.Join(", ", names);
    }

    /// <inheritdoc/>
    public void ClearBindings(string action, bool pad)
    {
        SetBindings(action, BindingsOf(action).Where(b => b.IsPad != pad));
        SyncSettings(bindings: true);
    }

    /// <inheritdoc/>
    public void ResetControls()
    {
        foreach (string action in Settings.GameActions.Concat(Settings.MenuActions))
            SetBindings(action, DefaultBindings(action));
        SyncSettings(bindings: true);
        GD.Print("Settings: controls reset");
    }

    /// <inheritdoc/>
    public void ResetSettings()
    {
        foreach (SettingDef def in Settings.Defs)
            _settingAccess[def.Key].Set(def.Default);
        foreach (string action in Settings.GameActions.Concat(Settings.MenuActions))
            SetBindings(action, DefaultBindings(action));
        SyncSettings(bindings: true);
        GD.Print("Settings: every setting reset");
    }

    // ---- the bindings ----

    /// <summary>The bindings of <paramref name="action"/> in the <see cref="InputMap"/>.</summary>
    public static List<Binding> BindingsOf(string action)
    {
        var list = new List<Binding>();
        if (!InputMap.HasAction(action))
            return list;
        foreach (InputEvent e in InputMap.ActionGetEvents(action))
        {
            if (BindingOf(e) is Binding b)
                list.Add(b);
        }
        return list;
    }

    /// <summary><paramref name="action"/>'s bindings in <c>project.godot</c> (whatever the run changed).</summary>
    public static List<Binding> DefaultBindings(string action)
    {
        var list = new List<Binding>();
        if (ProjectSettings.GetSetting("input/" + action).AsGodotDictionary() is { } d && d.TryGetValue("events", out Variant events))
        {
            foreach (Variant v in events.AsGodotArray())
            {
                if (v.AsGodotObject() is InputEvent e && BindingOf(e) is Binding b)
                    list.Add(b);
            }
        }
        return list;
    }

    /// <summary>The binding an input event makes (a key by its physical code), or null for another event (or a pad axis barely moved).</summary>
    public static Binding? BindingOf(InputEvent e) => e switch
    {
        InputEventKey k => new Binding(BindingKind.Key, (long)(k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode)),
        InputEventMouseButton m => new Binding(BindingKind.Mouse, (long)m.ButtonIndex),
        InputEventJoypadButton j => new Binding(BindingKind.PadButton, (long)j.ButtonIndex),
        InputEventJoypadMotion a when a.AxisValue != 0 => new Binding(BindingKind.PadAxis, (long)a.Axis, Math.Sign(a.AxisValue)),
        _ => null,
    };

    /// <summary>The input event of a binding (any pad).</summary>
    public static InputEvent EventOf(Binding b) => b.Kind switch
    {
        BindingKind.Key => new InputEventKey { PhysicalKeycode = (Key)b.Code },
        BindingKind.Mouse => new InputEventMouseButton { ButtonIndex = (MouseButton)b.Code },
        BindingKind.PadButton => new InputEventJoypadButton { Device = -1, ButtonIndex = (JoyButton)b.Code },
        _ => new InputEventJoypadMotion { Device = -1, Axis = (JoyAxis)b.Code, AxisValue = b.Sign },
    };

    /// <summary>A binding's name for the menu (capitals: the menu font has no others).</summary>
    public static string BindingName(Binding b) => b.Kind switch
    {
        BindingKind.Key => OS.GetKeycodeString((Key)b.Code).ToUpperInvariant(),
        BindingKind.Mouse => Binding.MouseName(b.Code),
        BindingKind.PadButton => Binding.PadButtonName(b.Code),
        _ => Binding.PadAxisName(b.Code, b.Sign),
    };

    private static void SetBindings(string action, IEnumerable<Binding> bindings)
    {
        if (!InputMap.HasAction(action))
            return;
        List<Binding> list = bindings.ToList(); // before the erase: it may read the action's events
        InputMap.ActionEraseEvents(action);
        foreach (Binding b in list)
            InputMap.ActionAddEvent(action, EventOf(b));
    }

    /// <summary>
    /// T7.3: binds <paramref name="input"/> to <paramref name="action"/> (it
    /// replaces the action's bindings of the same kind of device, and other
    /// actions of its group, the game's or the menus', lose it), then saves.
    /// </summary>
    public void BindAction(string action, Binding input)
    {
        string[] group = Settings.MenuActions.Contains(action) ? Settings.MenuActions : Settings.GameActions;
        var table = group.ToDictionary(a => a, BindingsOf);
        List<string> lost = Binding.Rebind(table, group, action, input);
        SetBindings(action, table[action]);
        foreach (string other in lost)
            SetBindings(other, table[other]);
        SyncSettings(bindings: true);
        GD.Print($"Settings: {action} bound to {BindingName(input)}{(lost.Count > 0 ? $" (taken from {string.Join(", ", lost)})" : "")}");
    }

    /// <summary>
    /// T7.3: while the menus wait for an input to bind, the next key (Escape
    /// cancels), mouse button, pad button or pad axis pushed past half way
    /// binds to the action. Returns whether the event was taken.
    /// </summary>
    private bool BindingEvent(InputEvent e, MMenu menu, string action)
    {
        Binding? input = null;
        switch (e)
        {
            case InputEventKey { Pressed: true, Echo: false } key:
                if ((key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode) == Key.Escape)
                {
                    menu.M_CancelBinding();
                    return true;
                }
                input = BindingOf(key);
                break;
            case InputEventMouseButton { Pressed: true } or InputEventJoypadButton { Pressed: true }:
                input = BindingOf(e);
                break;
            case InputEventJoypadMotion motion when Math.Abs(motion.AxisValue) >= 0.6f:
                input = BindingOf(motion);
                break;
            case InputEventKey or InputEventMouseButton or InputEventJoypadButton:
                return true;
            default:
                return false;
        }
        if (input is Binding b)
        {
            BindAction(action, b);
            menu.M_BindingDone();
            _menuButtonsHeld = true; // the input bound is no shot or use
        }
        return true;
    }

    /// <summary>The wait for an input to bind gives up after <see cref="BindingWaitSeconds"/>.</summary>
    private void TickBindingWait(MMenu menu, double delta)
    {
        if (menu.WaitingBinding is null)
        {
            _bindingWait = 0;
            return;
        }
        _bindingWait += delta;
        if (_bindingWait >= BindingWaitSeconds)
        {
            menu.M_CancelBinding();
            _bindingWait = 0;
        }
    }

    /// <summary>Whether <paramref name="e"/> is one of the game's bound inputs (the debug keys then leave it to the game).</summary>
    public static bool IsGameInput(InputEvent e)
    {
        foreach (string action in Settings.GameActions)
        {
            if (InputMap.HasAction(action) && InputMap.EventIsAction(e, action, true))
                return true;
        }
        return false;
    }

    // ---- the window and the audio ----

    private void ApplyWindow()
    {
        if (!CanApplyVideo)
            return;
        DisplayServer.WindowMode mode = _fullscreen switch
        {
            "on" => DisplayServer.WindowMode.Fullscreen,
            "exclusive" => DisplayServer.WindowMode.ExclusiveFullscreen,
            _ => DisplayServer.WindowMode.Windowed,
        };
        DisplayServer.WindowMode now = DisplayServer.WindowGetMode();
        if (mode == DisplayServer.WindowMode.Windowed)
        {
            if (now is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen)
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            Vector2I size = ParseSize(_windowSize);
            if (DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Windowed && DisplayServer.WindowGetSize() != size)
            {
                DisplayServer.WindowSetSize(size);
                int screen = DisplayServer.WindowGetCurrentScreen();
                Rect2I usable = DisplayServer.ScreenGetUsableRect(screen);
                DisplayServer.WindowSetPosition(usable.Position + (usable.Size - size) / 2);
            }
        }
        else if (now != mode)
            DisplayServer.WindowSetMode(mode);
    }

    private void ApplyVsync()
    {
        if (!CanApplyVideo)
            return;
        DisplayServer.WindowSetVsyncMode(_vsync switch
        {
            "off" => DisplayServer.VSyncMode.Disabled,
            "adaptive" => DisplayServer.VSyncMode.Adaptive,
            _ => DisplayServer.VSyncMode.Enabled,
        });
    }

    // A window resized by hand keeps its size for the next start.
    private void WindowResized()
    {
        if (!CanApplyVideo || _fullscreen != "off" || DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed)
            return;
        Vector2I size = DisplayServer.WindowGetSize();
        _windowSize = $"{size.X}x{size.Y}";
    }

    private static Vector2I ParseSize(string s)
    {
        int x = s.IndexOf('x');
        if (x > 0 && int.TryParse(s[..x], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)
            && int.TryParse(s[(x + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)
            && w >= 320 && h >= 200 && w <= 16384 && h <= 16384)
            return new Vector2I(w, h);
        throw new ArgumentException($"video/resolution: \"{s}\" (WIDTHxHEIGHT, at least 320x200)");
    }

    private static void EnsureAudioBus(string name)
    {
        if (AudioServer.GetBusIndex(name) >= 0)
            return;
        AudioServer.AddBus();
        int i = AudioServer.BusCount - 1;
        AudioServer.SetBusName(i, name);
        AudioServer.SetBusSend(i, "Master");
    }

    /// <summary>The volume (0–15, vanilla's) as a bus's: silent at 0, else its share of full linear amplitude.</summary>
    public static float VolumeDb(int volume) => volume <= 0 ? -80f : Mathf.LinearToDb(volume / 15f);

    /// <summary>
    /// T7.8e: the music volume (0–15) as the <see cref="MusicBus"/>'s: unity,
    /// muted at 0; the volume itself is i_oplmusic.c's
    /// (<c>I_OPL_SetMusicVolume</c>, the notes' levels, vanilla's), which at 0
    /// leaves the notes at the chip's −47 dB (SPEC §12 T7.8e).
    /// </summary>
    public static float MusicBusDb(int volume) => volume <= 0 ? -80f : 0f;

    /// <summary>
    /// The volumes (vanilla's <c>sfxVolume</c>, <c>musicVolume</c>) on their
    /// buses (<see cref="SfxBus"/>; <see cref="MusicBus"/> only mutes at 0,
    /// <see cref="MusicBusDb"/>).
    /// </summary>
    private void UpdateAudioBuses()
    {
        if (_flow is null)
            return;
        SetBusVolume(SfxBus, Menu.sfxVolume, VolumeDb(Menu.sfxVolume));
        SetBusVolume(MusicBus, Menu.musicVolume, MusicBusDb(Menu.musicVolume));
    }

    private static void SetBusVolume(string bus, int volume, float db)
    {
        int i = AudioServer.GetBusIndex(bus);
        if (i < 0)
            return;
        if (AudioServer.GetBusVolumeDb(i) != db)
            AudioServer.SetBusVolumeDb(i, db);
        AudioServer.SetBusMute(i, volume <= 0);
    }

    // ---- each setting's reader and applier ----

    private static string Int(float f) => ((int)MathF.Round(f)).ToString(CultureInfo.InvariantCulture);

    private static string OnOff(bool on) => on ? "on" : "off";

    private static bool ParseOnOff(string s, string what) => s switch
    {
        "on" => true,
        "off" => false,
        _ => throw new ArgumentException($"{what}: \"{s}\" (on or off)"),
    };

    private static int ParseInt(string s, string what, int min, int max) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) && i >= min && i <= max
            ? i
            : throw new ArgumentException($"{what}: \"{s}\" ({min}-{max})");

    private static string Choice(string s, string what, params string[] choices) =>
        Array.IndexOf(choices, s) >= 0 ? s : throw new ArgumentException($"{what}: \"{s}\" ({string.Join(", ", choices)})");

    /// <summary>The aim assist's half-cone in degrees (BAM to the nearest hundredth), or off.</summary>
    private static string AimAssistText(uint cone) => cone == 0
        ? "off"
        : Math.Round(cone * 360.0 / 4294967296.0, 2).ToString(CultureInfo.InvariantCulture);

    private void BuildSettingAccess()
    {
        _settingAccess.Clear();
        void Add(string key, Func<string> get, Action<string> set) => _settingAccess[key] = (get, set);

        // video
        Add("video/resolution", () => _windowSize, s =>
        {
            Vector2I size = ParseSize(s);
            _windowSize = $"{size.X}x{size.Y}";
            ApplyWindow();
        });
        Add("video/fullscreen", () => _fullscreen, s =>
        {
            _fullscreen = Choice(s, "video/fullscreen", "off", "on", "exclusive");
            ApplyWindow();
        });
        Add("video/vsync", () => _vsync, s =>
        {
            _vsync = Choice(s, "video/vsync", "on", "off", "adaptive");
            ApplyVsync();
        });
        Add("video/frame_cap", () => Engine.MaxFps == 0 ? "off" : Engine.MaxFps.ToString(CultureInfo.InvariantCulture),
            s => Engine.MaxFps = s == "off" ? 0 : ParseInt(s, "video/frame_cap", 10, 1000));
        Add("video/zoom", () => Iso is { } iso ? Int(iso.ViewUnits) : _zoom, s =>
        {
            float v = Math.Clamp(ParseFloat(s, "video/zoom"), IsoCamera.MinViewUnits, IsoCamera.MaxViewUnits);
            _zoom = Int(v);
            Iso?.SetViewUnits(v);
        });
        Add("video/pitch", () => Iso is { } iso ? Int(iso.Pitch) : _pitch, s =>
        {
            float v = Math.Clamp(ParseFloat(s, "video/pitch"), IsoCamera.MinPitch, IsoCamera.MaxPitch);
            _pitch = Int(v);
            Iso?.SetPitch(v);
        });
        Add("video/projection", () => Iso is { } iso ? (iso.Mode == IsoProjection.Orthographic ? "ortho" : "perspective") : _projection, s =>
        {
            _projection = Choice(s, "video/projection", "ortho", "perspective");
            Iso?.SetProjectionMode(_projection == "ortho" ? IsoProjection.Orthographic : IsoProjection.Perspective);
        });

        Add("video/wipe", () => ScreenWipes ? "melt" : "off", s => ScreenWipes = ParseWipe(s, "video/wipe"));

        // gameplay
        Add("gameplay/aim_assist", () => AimAssistText(Tweaks.AimAssistCone), s =>
        {
            uint cone = ParseAimAssistCone(s);
            Tweaks = Tweaks with { AimAssistCone = cone };
            if (World is { } world)
                world.tweaks = world.tweaks with { AimAssistCone = cone }; // at once, between the tics
        });
        Add("gameplay/cutaway", () => Cutaway.Style.ToString().ToLowerInvariant(), s => Cutaway = Cutaway with { Style = Render.Cutaway.ParseStyle(s) });
        Add("gameplay/cutaway_things", () => ThingsName(Cutaway.Things), s => Cutaway = Cutaway with { Things = Render.Cutaway.ParseThings(s) });
        Add("gameplay/cutaway_cap", () => Cutaway.Cap.ToString().ToLowerInvariant(), s => Cutaway = Cutaway with { Cap = Render.Cutaway.ParseCap(s) });
        Add("gameplay/cutaway_radius", () => Int(Cutaway.Radius), s => Cutaway = Cutaway with { Radius = ParseInt(s, "gameplay/cutaway_radius", 0, 1024) });
        Add("gameplay/cutaway_cursor", () => OnOff(Cutaway.Cursor), s => Cutaway = Cutaway with { Cursor = ParseOnOff(s, "gameplay/cutaway_cursor") });
        Add("gameplay/light", () => _lightMode.ToString().ToLowerInvariant(), s =>
        {
            _lightMode = s switch
            {
                "none" => LightDiminishing.None,
                "camera" => LightDiminishing.Camera,
                "player" => LightDiminishing.Player,
                _ => throw new ArgumentException($"gameplay/light: \"{s}\" (player, none or camera)"),
            };
            Mesh?.SetLightDiminishing(_lightMode);
        });
        Add("gameplay/door_lids", () => OnOff(DoorLids == DoorLidMode.On), s =>
        {
            DoorLids = ParseOnOff(s, "gameplay/door_lids") ? DoorLidMode.On : DoorLidMode.Off;
            Mesh?.SetDoorLids(DoorLids);
        });
        Add("gameplay/masked_back", () => MaskedBacks == MaskedBackFaces.Mirrored ? "mirror" : "off", s =>
        {
            MaskedBacks = ParseMaskedBacks(s);
            Mesh?.SetMaskedBackFaces(MaskedBacks);
        });
        Add("gameplay/weapon_light", () => OnOff(WeaponLight), s => WeaponLight = ParseOnOff(s, "gameplay/weapon_light"));
        Add("gameplay/palette_effects", () => OnOff(PaletteEffects), s => PaletteEffects = ParseOnOff(s, "gameplay/palette_effects"));

        // sprites
        Add("sprites/tilt", () => SpriteOptions.Tilt switch
        {
            1f => "full",
            0.5f => "half",
            0f => "off",
            float t => t.ToString(CultureInfo.InvariantCulture),
        }, s => SpriteOptions = SpriteOptions with { Tilt = SpriteSettings.ParseTilt(s) });
        Add("sprites/tilt_depth", () => SpriteOptions.TiltDepth.ToString().ToLowerInvariant(), s => SpriteOptions = SpriteOptions with { TiltDepth = SpriteSettings.ParseTiltDepth(s) });
        Add("sprites/shadow", () => SpriteOptions.Shadow.ToString().ToLowerInvariant(), s => SpriteOptions = SpriteOptions with { Shadow = SpriteSettings.ParseShadow(s) });
        Add("sprites/outline", () => SpriteOptions.Outline < 0 ? "off" : SpriteOptions.Outline.ToString(CultureInfo.InvariantCulture),
            s => SpriteOptions = SpriteOptions with { Outline = SpriteSettings.ParseOutline(s) });
        Add("sprites/wall_pull", () => SpriteOptions.WallPull <= 0 ? "off" : Int(SpriteOptions.WallPull), s =>
        {
            SpriteOptions = SpriteOptions with { WallPull = SpriteSettings.ParseWallPull(s) };
            if (SpriteOptions.WallPull > 0)
                _wallPull = SpriteOptions.WallPull; // what the P key turns back on
        });
        Add("sprites/hidden", () => SpriteOptions.Hidden.ToString().ToLowerInvariant(), s => SpriteOptions = SpriteOptions with { Hidden = SpriteSettings.ParseHidden(s) });
        Add("sprites/player_light", () => SpriteOptions.PlayerLight <= 0 ? "off" : SpriteOptions.PlayerLight.ToString(CultureInfo.InvariantCulture), s =>
        {
            SpriteOptions = SpriteOptions with { PlayerLight = SpriteSettings.ParsePlayerLight(s) };
            if (SpriteOptions.PlayerLight > 0)
                _playerLight = SpriteOptions.PlayerLight; // what the B key turns back on
        });
        Add("sprites/fuzz", () => OnOff(SpriteOptions.Fuzz), s => SpriteOptions = SpriteOptions with { Fuzz = SpriteSettings.ParseFuzz(s) });

        // HUD
        Add("hud/mode", () => Hud.Mode.ToString().ToLowerInvariant(), s => Hud.Mode = Choice(s, "hud/mode", "bar", "full", "off") switch
        {
            "full" => HudMode.Full,
            "off" => HudMode.Off,
            _ => HudMode.Bar,
        });
        Add("hud/scale", () => Hud.FixedScale == 0 ? "auto" : Hud.FixedScale.ToString(CultureInfo.InvariantCulture),
            s => Hud.FixedScale = s == "auto" ? 0 : ParseInt(s, "hud/scale", 1, 16));
        Add("hud/messages", () => OnOff(Menu.showMessages != 0), s => Menu.showMessages = ParseOnOff(s, "hud/messages") ? 1 : 0);
        Add("hud/detail", () => Menu.detailLevel == 0 ? "high" : "low", s => Menu.detailLevel = Choice(s, "hud/detail", "high", "low") == "high" ? 0 : 1);

        // controls and sound
        Add("controls/mouse_sensitivity", () => Menu.mouseSensitivity.ToString(CultureInfo.InvariantCulture),
            s => Menu.mouseSensitivity = ParseInt(s, "controls/mouse_sensitivity", 0, 9));
        Add("sound/sfx_volume", () => Menu.sfxVolume.ToString(CultureInfo.InvariantCulture), s => Menu.sfxVolume = ParseInt(s, "sound/sfx_volume", 0, 15));
        Add("sound/music_volume", () => Menu.musicVolume.ToString(CultureInfo.InvariantCulture), s => Menu.musicVolume = ParseInt(s, "sound/music_volume", 0, 15));

        foreach (string action in Settings.GameActions.Concat(Settings.MenuActions))
        {
            string a = action;
            Add(Settings.BindingKey(a), () => Binding.FormatList(BindingsOf(a)),
                s => SetBindings(a, Binding.ParseList(s) ?? throw new ArgumentException($"controls/{a}: \"{s}\" is not a list of bindings")));
        }

        foreach (SettingDef def in Settings.Defs)
        {
            if (!_settingAccess.ContainsKey(def.Key))
                throw new InvalidOperationException($"setting {def.Key} has no reader");
        }
    }
}
