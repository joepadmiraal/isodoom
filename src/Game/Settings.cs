using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IsoDoom.Game;

/// <summary>The options menu's pages of settings (T7.3, SPEC §8).</summary>
public enum SettingPage { Video, Gameplay, Sprites, Hud, Controls, Sound }

/// <summary>
/// One setting of <c>user://settings.cfg</c> (T7.3): its key
/// (<c>section/name</c>), the options menu's label and page, its default and
/// the values the menu cycles through, all in the form its command-line
/// argument takes (<see cref="Args"/>: the argument overrides the saved value
/// for one run). Plain C# (the tests link it); the game scene reads and
/// applies the values (<c>LevelScene.Settings.cs</c>).
/// </summary>
public sealed record SettingDef(string Key, string Label, SettingPage Page, string Default, string[] Choices, params string[] Args)
{
    /// <summary>The config file's section (before the slash).</summary>
    public string Section => Key[..Key.IndexOf('/')];

    /// <summary>The config file's key in <see cref="Section"/> (after the slash).</summary>
    public string Name => Key[(Key.IndexOf('/') + 1)..];

    /// <summary>Whether the menu wraps round the choices (names; numbers stop at the ends).</summary>
    public bool Wraps => Choices.Length <= 2 || !Choices.All(c => Settings.Order(c) is not null || c is "off" or "auto");

    /// <summary>
    /// The value <paramref name="step"/> (+1 or −1) choices from
    /// <paramref name="current"/>: the next or previous choice, wrapping for
    /// names and stopping at the ends for numbers; a number between the
    /// choices goes to the nearest one that way, any other value to the first.
    /// </summary>
    public string Step(string current, int step)
    {
        int i = Array.IndexOf(Choices, current);
        int n = Choices.Length;
        if (i >= 0)
        {
            int next = i + Math.Sign(step);
            if (Wraps)
                return Choices[((next % n) + n) % n];
            return Choices[Math.Clamp(next, 0, n - 1)];
        }
        if (Settings.Order(current) is double v)
        {
            if (step > 0)
            {
                foreach (string c in Choices)
                {
                    if (Settings.Order(c) is double o && o > v)
                        return c;
                }
                return Choices[^1];
            }
            for (int k = n - 1; k >= 0; k--)
            {
                if (Settings.Order(Choices[k]) is double o && o < v)
                    return Choices[k];
            }
            return Choices[0];
        }
        return Choices[0];
    }
}

/// <summary>
/// T7.3: the settings the options menu shows and <c>user://settings.cfg</c>
/// keeps (SPEC §8), besides the key bindings (<see cref="Binding"/>, section
/// <c>controls</c>, one key per input action) and the IWAD (<c>[wad] iwad</c>,
/// <c>WadLocator</c>). Values are strings in their command-line argument's form.
/// </summary>
public static class Settings
{
    private static string[] Range(int from, int to, int step)
    {
        var list = new List<string>();
        for (int v = from; v <= to; v += step)
            list.Add(v.ToString(CultureInfo.InvariantCulture));
        return [.. list];
    }

    private static readonly string[] _onOff = ["on", "off"];

    /// <summary>Every setting, in the menu's order within each page.</summary>
    public static readonly SettingDef[] Defs =
    [
        // video
        new("video/resolution", "WINDOW SIZE", SettingPage.Video, "1280x800",
            ["960x600", "1024x640", "1280x720", "1280x800", "1366x768", "1440x900", "1600x900", "1680x1050", "1920x1080", "1920x1200", "2560x1440", "2560x1600", "3840x2160"]),
        new("video/fullscreen", "FULLSCREEN", SettingPage.Video, "on", ["off", "on", "exclusive"], "--level-fullscreen"),
        new("video/vsync", "VSYNC", SettingPage.Video, "on", ["on", "off", "adaptive"]),
        new("video/frame_cap", "FRAME CAP", SettingPage.Video, "off", ["off", "30", "35", "60", "75", "90", "120", "144", "165", "240"]),
        new("video/zoom", "ZOOM (VIEW HEIGHT)", SettingPage.Video, "640", Range(320, 1600, 80), "--level-zoom"),
        new("video/pitch", "CAMERA PITCH", SettingPage.Video, "55", Range(45, 60, 1), "--level-pitch"),
        new("video/projection", "PROJECTION", SettingPage.Video, "ortho", ["ortho", "perspective"], "--level-projection"),
        new("video/wipe", "SCREEN WIPE", SettingPage.Video, "melt", ["melt", "off"], "--level-wipe"),

        // gameplay
        new("gameplay/aim_assist", "AIM ASSIST (DEGREES)", SettingPage.Gameplay, "5",
            ["off", "1", "2", "3", "4", "5", "6", "8", "10", "12", "15", "20", "25", "30", "45"], "--level-aim-assist", "--level-tweaks"),
        new("gameplay/cutaway", "CUTAWAY", SettingPage.Gameplay, "cut", ["cut", "dither", "off"], "--level-cutaway"),
        new("gameplay/cutaway_things", "CUTAWAY THINGS", SettingPage.Gameplay, "decor", ["decor", "all", "off"], "--level-cutaway-things"),
        new("gameplay/cutaway_cap", "CUTAWAY CAP", SettingPage.Gameplay, "dark", ["dark", "flat", "off"], "--level-cutaway-cap"),
        new("gameplay/cutaway_doors", "CUTAWAY DOORS", SettingPage.Gameplay, "keep", ["keep", "cut"], "--level-cutaway-doors"),
        new("gameplay/cutaway_radius", "CUTAWAY RADIUS", SettingPage.Gameplay, "80", Range(48, 160, 16), "--level-cutaway-radius"),
        new("gameplay/cutaway_cursor", "CURSOR CUTS TOO", SettingPage.Gameplay, "off", _onOff, "--level-cutaway-cursor"),
        new("gameplay/light", "LIGHT DIMINISHING", SettingPage.Gameplay, "player", ["player", "none", "camera"], "--level-light"),
        new("gameplay/door_lids", "DOOR LIDS", SettingPage.Gameplay, "on", _onOff, "--level-door-lids"),
        new("gameplay/wall_caps", "WALL TOPS", SettingPage.Gameplay, "on", _onOff, "--level-wall-caps"),
        new("gameplay/upper_walls", "UPPER WALLS", SettingPage.Gameplay, "doors", ["doors", "all"], "--level-upper-walls"),
        new("gameplay/masked_back", "MASKED WALLS BEHIND", SettingPage.Gameplay, "mirror", ["mirror", "off"], "--level-masked-back"),
        new("gameplay/weapon_light", "WEAPON FLASH LIGHT", SettingPage.Gameplay, "on", _onOff, "--level-weapon-light"),
        new("gameplay/palette_effects", "PALETTE FLASHES", SettingPage.Gameplay, "on", _onOff, "--level-palette-effects"),
        new("gameplay/tracers", "SHOT TRACERS", SettingPage.Gameplay, "player", ["player", "all", "off"], "--level-tracers"),
        new("gameplay/aim_marker", "AIM MARKER", SettingPage.Gameplay, "pad", ["pad", "on", "off"], "--level-aim-marker"),
        new("gameplay/fog", "FOG OF WAR", SettingPage.Gameplay, "hide", ["hide", "dim", "off"], "--level-fog"),
        new("gameplay/fog_things", "FOG MONSTERS", SettingPage.Gameplay, "seen", ["seen", "sight"], "--level-fog-things"),

        // sprites (readability)
        new("sprites/tilt", "TILT", SettingPage.Sprites, "full", ["full", "half", "off"], "--level-sprite-tilt"),
        new("sprites/tilt_depth", "TILT DEPTH", SettingPage.Sprites, "upright", ["upright", "tilted"], "--level-sprite-tilt-depth"),
        new("sprites/shadow", "SHADOWS", SettingPage.Sprites, "off", ["off", "blend", "dither"], "--level-sprite-shadow"),
        new("sprites/outline", "OUTLINE", SettingPage.Sprites, "0", ["0", "off"], "--level-sprite-outline"),
        new("sprites/wall_pull", "WALL PULL", SettingPage.Sprites, "16", ["off", "8", "16", "24", "32", "48", "64"], "--level-sprite-wall-pull"),
        new("sprites/hidden", "HIDE BEHIND WALLS", SettingPage.Sprites, "depth", ["depth", "upright"], "--level-sprite-hidden"),
        new("sprites/player_light", "PLAYER MIN LIGHT", SettingPage.Sprites, "128", ["off", "64", "96", "128", "160", "192", "255"], "--level-player-light"),
        new("sprites/fuzz", "FUZZ", SettingPage.Sprites, "on", _onOff, "--level-fuzz"),

        // HUD (vanilla's screen size, messages and detail too)
        new("hud/mode", "SCREEN", SettingPage.Hud, "bar", ["bar", "full", "off"], "--level-hud"),
        new("hud/scale", "SCALE", SettingPage.Hud, "auto", ["auto", "1", "2", "3", "4", "5", "6", "7", "8"], "--level-hud-scale"),
        new("hud/messages", "MESSAGES", SettingPage.Hud, "on", _onOff),
        new("hud/detail", "GRAPHIC DETAIL", SettingPage.Hud, "high", ["high", "low"]),

        // controls and sound (vanilla's options)
        new("controls/mouse_sensitivity", "MOUSE SENSITIVITY", SettingPage.Controls, "5", Range(0, 9, 1)),
        new("sound/sfx_volume", "SFX VOLUME", SettingPage.Sound, "8", Range(0, 15, 1)),
        new("sound/music_volume", "MUSIC VOLUME", SettingPage.Sound, "8", Range(0, 15, 1)),
        // T7.8g: the music's chip (More Options → Sound, with the volumes)
        new("sound/opl", "MUSIC CHIP", SettingPage.Sound, "opl3", ["opl3", "opl2"], "--level-opl"),
    ];

    /// <summary>The setting with key <paramref name="key"/>, or null.</summary>
    public static SettingDef? Find(string key) => Array.Find(Defs, d => d.Key == key);

    /// <summary>The settings of <paramref name="page"/>, in order.</summary>
    public static SettingDef[] OnPage(SettingPage page) => Array.FindAll(Defs, d => d.Page == page);

    /// <summary>A value's place among numbers (<c>W</c>x<c>H</c> sorts by width, then height), or null for a name.</summary>
    public static double? Order(string value)
    {
        int x = value.IndexOf('x');
        if (x > 0 && int.TryParse(value[..x], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)
            && int.TryParse(value[(x + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h))
            return w * 100000.0 + h;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
    }

    /// <summary>How the menu shows a value (the menu font has capitals only).</summary>
    public static string Display(string value) => value.ToUpperInvariant();

    /// <summary>The input actions the controls pages rebind, each with its label (the actions of <c>project.godot</c>, T4.6, and the menus').</summary>
    public static readonly (string Title, (string Action, string Label)[] Actions)[] ControlPages =
    [
        ("MOVEMENT", new[]
        {
            ("move_up", "MOVE UP"), ("move_down", "MOVE DOWN"), ("move_left", "MOVE LEFT"), ("move_right", "MOVE RIGHT"),
            ("run", "RUN (HOLD)"), ("run_toggle", "RUN (TOGGLE)"),
            ("aim_up", "AIM UP"), ("aim_down", "AIM DOWN"), ("aim_left", "AIM LEFT"), ("aim_right", "AIM RIGHT"),
            ("turn_left", "TURN LEFT (VANILLA)"), ("turn_right", "TURN RIGHT (VANILLA)"),
        }),
        ("ACTIONS", new[]
        {
            ("attack", "FIRE"), ("use", "USE"), ("weapon_next", "NEXT WEAPON"), ("weapon_prev", "PREVIOUS WEAPON"),
            ("weapon_1", "WEAPON 1"), ("weapon_2", "WEAPON 2"), ("weapon_3", "WEAPON 3"), ("weapon_4", "WEAPON 4"),
            ("weapon_5", "WEAPON 5"), ("weapon_6", "WEAPON 6"), ("weapon_7", "WEAPON 7"), ("weapon_8", "WEAPON 8"),
            ("pause", "PAUSE"), ("quicksave", "QUICKSAVE"), ("quickload", "QUICKLOAD"),
        }),
        ("MENU KEYS", new[]
        {
            ("menu_open", "OPEN/CLOSE MENU"), ("menu_up", "UP"), ("menu_down", "DOWN"), ("menu_left", "LEFT"), ("menu_right", "RIGHT"),
            ("menu_select", "SELECT"), ("menu_back", "BACK"), ("menu_yes", "YES"), ("menu_no", "NO"),
        }),
    ];

    /// <summary>The menus' input actions (Chocolate Doom's <c>key_menu_*</c>, rebindable since T7.3).</summary>
    public static readonly string[] MenuActions = [.. ControlPages[2].Actions.Select(a => a.Action)];

    /// <summary>The game's input actions on the controls pages.</summary>
    public static readonly string[] GameActions = [.. ControlPages.Take(2).SelectMany(p => p.Actions).Select(a => a.Action)];

    /// <summary>The settings key of action <paramref name="action"/>'s bindings.</summary>
    public static string BindingKey(string action) => "controls/" + action;
}

/// <summary>
/// T7.3: the saved settings and what a run changes in them (plain C#,
/// tested). Loaded from the file; each setting's default is known
/// (<see cref="SetDefault"/>), the file keeps only values that differ from
/// it. A setting given on the command line is pinned (<see cref="Pin"/>):
/// the run uses the argument's value but the file keeps its own, until the
/// setting changes during the run (the menu, a key), which saves it.
/// </summary>
public sealed class SettingValues
{
    private readonly Dictionary<string, string> _saved = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _defaults = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _pinned = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];

    /// <summary>The values read from the file (key <c>section/name</c>, value), any order.</summary>
    public SettingValues(IEnumerable<KeyValuePair<string, string>>? loaded = null)
    {
        if (loaded is null)
            return;
        foreach ((string key, string value) in loaded)
        {
            _saved[key] = value;
            Remember(key);
        }
    }

    private void Remember(string key)
    {
        if (!_order.Contains(key))
            _order.Add(key);
    }

    /// <summary>The saved value of <paramref name="key"/> (null when the file has none).</summary>
    public string? Saved(string key) => _saved.TryGetValue(key, out string? v) ? v : null;

    /// <summary>The value of <paramref name="key"/> the file means: its own, else the default.</summary>
    public string? Value(string key) => Saved(key) ?? (_defaults.TryGetValue(key, out string? d) ? d : null);

    /// <summary>The default of <paramref name="key"/> (what the game uses with no file).</summary>
    public void SetDefault(string key, string value)
    {
        _defaults[key] = value;
        Remember(key);
    }

    /// <summary>The run uses <paramref name="value"/> from the command line: the file keeps its own until it changes.</summary>
    public void Pin(string key, string value)
    {
        _pinned[key] = value;
        Remember(key);
    }

    /// <summary>Whether <paramref name="key"/> is pinned by the command line.</summary>
    public bool IsPinned(string key) => _pinned.ContainsKey(key);

    /// <summary>
    /// The game uses <paramref name="current"/> for <paramref name="key"/>
    /// now. Returns whether the file's value changed (the caller saves): a
    /// pinned setting changes once it leaves the argument's value.
    /// </summary>
    public bool Observe(string key, string current)
    {
        Remember(key);
        if (_pinned.TryGetValue(key, out string? pin))
        {
            if (pin == current)
                return false;
            _pinned.Remove(key);
        }
        if (Value(key) == current)
            return false;
        _saved[key] = current;
        return true;
    }

    /// <summary>Back to the default (the file drops the key); unpins it.</summary>
    public void Reset(string key)
    {
        _pinned.Remove(key);
        _saved.Remove(key);
    }

    /// <summary>
    /// What the file keeps: each value that differs from its default (key,
    /// value), and the keys to drop (equal to their default), in the order
    /// first seen.
    /// </summary>
    public (List<(string Key, string Value)> Keep, List<string> Drop) ToSave()
    {
        var keep = new List<(string, string)>();
        var drop = new List<string>();
        foreach (string key in _order)
        {
            string? saved = Saved(key);
            if (saved is not null && !(_defaults.TryGetValue(key, out string? d) && d == saved))
                keep.Add((key, saved));
            else
                drop.Add(key);
        }
        return (keep, drop);
    }
}

/// <summary>What a binding is: a key, a mouse button, a pad button, or one direction of a pad axis.</summary>
public enum BindingKind { Key, Mouse, PadButton, PadAxis }

/// <summary>
/// T7.3: one input bound to an action (Godot's <c>InputEvent</c> in the game;
/// plain C# here for the settings file and the tests): a key by its physical
/// key code, a mouse button, a pad button (any pad), or a pad axis's
/// direction (<see cref="Sign"/> ±1). Saved as <c>key:87</c>,
/// <c>mouse:1</c>, <c>padbutton:0</c>, <c>padaxis:5+</c>, a list space-separated.
/// </summary>
public readonly record struct Binding(BindingKind Kind, long Code, int Sign = 0)
{
    /// <summary>A pad's (a pad's and the keyboard's or mouse's bindings are rebound apart).</summary>
    public bool IsPad => Kind is BindingKind.PadButton or BindingKind.PadAxis;

    public override string ToString() => Kind switch
    {
        BindingKind.Key => $"key:{Code}",
        BindingKind.Mouse => $"mouse:{Code}",
        BindingKind.PadButton => $"padbutton:{Code}",
        _ => $"padaxis:{Code}{(Sign < 0 ? '-' : '+')}",
    };

    /// <summary>Parses one binding (<see cref="ToString"/>'s form), or null.</summary>
    public static Binding? Parse(string s)
    {
        int colon = s.IndexOf(':');
        if (colon < 0)
            return null;
        string kind = s[..colon], code = s[(colon + 1)..];
        int sign = 0;
        if (kind == "padaxis")
        {
            if (code.Length < 2 || code[^1] is not ('+' or '-'))
                return null;
            sign = code[^1] == '-' ? -1 : 1;
            code = code[..^1];
        }
        if (!long.TryParse(code, NumberStyles.Integer, CultureInfo.InvariantCulture, out long c) || c < 0)
            return null;
        return kind switch
        {
            "key" => new Binding(BindingKind.Key, c),
            "mouse" => new Binding(BindingKind.Mouse, c),
            "padbutton" => new Binding(BindingKind.PadButton, c),
            "padaxis" => new Binding(BindingKind.PadAxis, c, sign),
            _ => null,
        };
    }

    /// <summary>A list in a canonical order (keys, mouse, pad buttons, pad axes; by code), space-separated.</summary>
    public static string FormatList(IEnumerable<Binding> bindings) =>
        string.Join(" ", bindings.Distinct().OrderBy(b => b.Kind).ThenBy(b => b.Code).ThenBy(b => b.Sign).Select(b => b.ToString()));

    /// <summary>Parses a list (<see cref="FormatList"/>'s); null when any of it is not a binding.</summary>
    public static List<Binding>? ParseList(string s)
    {
        var list = new List<Binding>();
        foreach (string part in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Parse(part) is not Binding b)
                return null;
            list.Add(b);
        }
        return list;
    }

    /// <summary>A mouse button's name (Godot's <c>MouseButton</c> numbers).</summary>
    public static string MouseName(long button) => button switch
    {
        1 => "MOUSE LEFT",
        2 => "MOUSE RIGHT",
        3 => "MOUSE MIDDLE",
        4 => "WHEEL UP",
        5 => "WHEEL DOWN",
        6 => "WHEEL LEFT",
        7 => "WHEEL RIGHT",
        8 => "MOUSE 4",
        9 => "MOUSE 5",
        _ => $"MOUSE {button}",
    };

    /// <summary>A pad button's name (Godot's <c>JoyButton</c> numbers, SDL's layout).</summary>
    public static string PadButtonName(long button) => button switch
    {
        0 => "PAD A",
        1 => "PAD B",
        2 => "PAD X",
        3 => "PAD Y",
        4 => "PAD BACK",
        5 => "PAD GUIDE",
        6 => "PAD START",
        7 => "PAD LS",
        8 => "PAD RS",
        9 => "PAD LB",
        10 => "PAD RB",
        11 => "DPAD UP",
        12 => "DPAD DOWN",
        13 => "DPAD LEFT",
        14 => "DPAD RIGHT",
        _ => $"PAD {button}",
    };

    /// <summary>A pad axis direction's name (Godot's <c>JoyAxis</c> numbers; Y down is positive).</summary>
    public static string PadAxisName(long axis, int sign) => axis switch
    {
        0 => sign < 0 ? "LS LEFT" : "LS RIGHT",
        1 => sign < 0 ? "LS UP" : "LS DOWN",
        2 => sign < 0 ? "RS LEFT" : "RS RIGHT",
        3 => sign < 0 ? "RS UP" : "RS DOWN",
        4 => "PAD LT",
        5 => "PAD RT",
        _ => $"AXIS {axis}{(sign < 0 ? '-' : '+')}",
    };

    /// <summary>
    /// Binds <paramref name="input"/> to <paramref name="action"/> in
    /// <paramref name="table"/>: it replaces the action's bindings of the same
    /// kind of device (keyboard and mouse, or pad) and leaves the other
    /// actions of <paramref name="group"/> (the game's, or the menus') that
    /// had it. Returns the actions that lost it.
    /// </summary>
    public static List<string> Rebind(IDictionary<string, List<Binding>> table, IEnumerable<string> group, string action, Binding input)
    {
        var lost = new List<string>();
        foreach (string other in group)
        {
            if (other != action && table.TryGetValue(other, out List<Binding>? list) && list.RemoveAll(b => b == input) > 0)
                lost.Add(other);
        }
        if (!table.TryGetValue(action, out List<Binding>? mine))
            table[action] = mine = [];
        mine.RemoveAll(b => b.IsPad == input.IsPad);
        mine.Add(input);
        return lost;
    }
}
