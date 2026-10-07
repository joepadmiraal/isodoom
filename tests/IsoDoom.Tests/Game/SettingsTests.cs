using System;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Game;
using IsoDoom.Sim;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Game;

/// <summary>
/// T7.3: the settings (<see cref="Settings"/>, <see cref="SettingValues"/>,
/// <see cref="Binding"/>) and the options' text pages (<see cref="MMenu"/>'s
/// setup menus) with a fake host: stepping through the values, the file's
/// keeping only what differs from the defaults, the command line's pinning,
/// the bindings' format and rebinding, and the pages' navigation, changes,
/// rebinding wait, clearing and resets.
/// </summary>
public class SettingsTests
{
    // ---- the definitions ----

    [Fact]
    public void EverySettingHasAUniqueKeyAndItsDefaultAmongItsChoices()
    {
        Assert.Equal(Settings.Defs.Length, Settings.Defs.Select(d => d.Key).Distinct().Count());
        foreach (SettingDef def in Settings.Defs)
        {
            Assert.Contains(def.Default, def.Choices);
            Assert.True(def.Key.IndexOf('/') > 0, def.Key);
            Assert.All(def.Args, a => Assert.StartsWith("--level-", a));
        }
        Assert.Equal(Settings.GameActions.Length + Settings.MenuActions.Length,
            Settings.GameActions.Concat(Settings.MenuActions).Distinct().Count());
    }

    [Fact]
    public void NamesWrapAndNumbersStopAtTheEnds()
    {
        SettingDef cutaway = Settings.Find("gameplay/cutaway")!;
        Assert.Equal("dither", cutaway.Step("cut", 1));
        Assert.Equal("cut", cutaway.Step("off", 1)); // wraps
        Assert.Equal("off", cutaway.Step("cut", -1));
        Assert.Equal("cut", cutaway.Step("nonsense", 1)); // anything else: the first

        SettingDef zoom = Settings.Find("video/zoom")!;
        Assert.Equal("720", zoom.Step("640", 1));
        Assert.Equal("1600", zoom.Step("1600", 1)); // stops
        Assert.Equal("320", zoom.Step("320", -1));
        Assert.Equal("720", zoom.Step("713", 1)); // between the choices (the Ctrl+wheel zoom): the nearest that way
        Assert.Equal("640", zoom.Step("713", -1));

        SettingDef aim = Settings.Find("gameplay/aim_assist")!;
        Assert.Equal("off", aim.Step("1", -1));
        Assert.Equal("1", aim.Step("off", 1));
        Assert.Equal("off", aim.Step("off", -1));

        SettingDef size = Settings.Find("video/resolution")!;
        Assert.Equal("1366x768", size.Step("1280x800", 1));
        Assert.Equal("1366x768", size.Step("1300x700", 1)); // a window resized by hand
        Assert.Equal("1280x800", size.Step("1300x700", -1));

        SettingDef outline = Settings.Find("sprites/outline")!;
        Assert.Equal("off", outline.Step("0", 1));
        Assert.Equal("0", outline.Step("off", 1)); // two choices toggle
    }

    // ---- the values and the file ----

    [Fact]
    public void TheFileKeepsOnlyWhatDiffersFromTheDefaults()
    {
        var values = new SettingValues(new Dictionary<string, string> { ["gameplay/cutaway"] = "dither", ["hud/mode"] = "bar", ["future/thing"] = "x" });
        values.SetDefault("gameplay/cutaway", "cut");
        values.SetDefault("hud/mode", "bar");
        values.SetDefault("sprites/tilt", "full");
        Assert.Equal("dither", values.Value("gameplay/cutaway"));
        Assert.Equal("full", values.Value("sprites/tilt"));
        Assert.False(values.Observe("gameplay/cutaway", "dither")); // as loaded
        Assert.False(values.Observe("sprites/tilt", "full"));
        Assert.True(values.Observe("sprites/tilt", "half"));
        Assert.False(values.Observe("sprites/tilt", "half"));
        (var keep, var drop) = values.ToSave();
        Assert.Equal(new[] { ("gameplay/cutaway", "dither"), ("future/thing", "x"), ("sprites/tilt", "half") }, keep);
        Assert.Equal(new[] { "hud/mode" }, drop); // equal to its default: dropped
        Assert.True(values.Observe("sprites/tilt", "full"));
        Assert.Contains("sprites/tilt", values.ToSave().Drop);
        values.Reset("gameplay/cutaway");
        Assert.Equal("cut", values.Value("gameplay/cutaway"));
    }

    [Fact]
    public void ACommandLineValueIsNotSavedUntilTheSettingChanges()
    {
        var values = new SettingValues(new Dictionary<string, string> { ["hud/mode"] = "full" });
        values.SetDefault("hud/mode", "bar");
        values.Pin("hud/mode", "off"); // --level-hud=off for this run
        Assert.True(values.IsPinned("hud/mode"));
        Assert.False(values.Observe("hud/mode", "off"));
        Assert.Equal(new[] { ("hud/mode", "full") }, values.ToSave().Keep); // the file keeps its own
        Assert.True(values.Observe("hud/mode", "bar")); // changed in the menu: saved, no longer pinned
        Assert.False(values.IsPinned("hud/mode"));
        Assert.Contains("hud/mode", values.ToSave().Drop);
    }

    // ---- the bindings ----

    [Fact]
    public void BindingsReadAndWriteTheirListInOneOrder()
    {
        var list = new List<Binding>
        {
            new(BindingKind.PadAxis, 5, 1), new(BindingKind.Mouse, 1), new(BindingKind.Key, 87),
            new(BindingKind.PadButton, 0), new(BindingKind.PadAxis, 1, -1), new(BindingKind.Key, 32),
        };
        string text = Binding.FormatList(list);
        Assert.Equal("key:32 key:87 mouse:1 padbutton:0 padaxis:1- padaxis:5+", text);
        Assert.Equal(text, Binding.FormatList(Binding.ParseList(text)!));
        Assert.Empty(Binding.ParseList("")!);
        Assert.Null(Binding.ParseList("key:87 joystick:1"));
        Assert.Null(Binding.Parse("padaxis:3"));
        Assert.Null(Binding.Parse("key:-4"));
        Assert.Equal("LS UP", Binding.PadAxisName(1, -1));
        Assert.Equal("PAD RT", Binding.PadAxisName(5, 1));
        Assert.Equal("PAD A", Binding.PadButtonName(0));
        Assert.Equal("MOUSE LEFT", Binding.MouseName(1));
    }

    [Fact]
    public void RebindingReplacesTheSameDeviceAndTakesTheInputFromTheGroup()
    {
        var e = new Binding(BindingKind.Key, 'E');
        var space = new Binding(BindingKind.Key, ' ');
        var a = new Binding(BindingKind.PadButton, 0);
        var f = new Binding(BindingKind.Key, 'F');
        var table = new Dictionary<string, List<Binding>>
        {
            ["use"] = new() { e, space, a },
            ["attack"] = new() { new Binding(BindingKind.Mouse, 1), new Binding(BindingKind.PadAxis, 5, 1) },
            ["menu_select"] = new() { new Binding(BindingKind.Key, 13), a },
        };
        string[] game = { "use", "attack" };

        Assert.Empty(Binding.Rebind(table, game, "use", f));
        Assert.Equal(new[] { a, f }, table["use"]); // E and Space replaced, the pad's A kept

        var rb = new Binding(BindingKind.PadButton, 10);
        Assert.Empty(Binding.Rebind(table, game, "attack", rb));
        Assert.Equal(new[] { new Binding(BindingKind.Mouse, 1), rb }, table["attack"]); // RT replaced

        Assert.Equal(new[] { "attack" }, Binding.Rebind(table, game, "use", rb));
        Assert.Equal(new[] { f, rb }, table["use"]);
        Assert.Equal(new[] { new Binding(BindingKind.Mouse, 1) }, table["attack"]);
        Assert.Contains(a, table["menu_select"]); // another group keeps it
    }

    // ---- the options' text pages ----

    private sealed class SetupHost : ISetupHost
    {
        public readonly Dictionary<string, string> Values = Settings.Defs.ToDictionary(d => d.Key, d => d.Default);
        public readonly List<(string Action, bool Pad)> Cleared = new();
        public int ControlResets, Resets;

        public string GetSetting(string key) => Values[key];
        public void SetSetting(string key, string value) => Values[key] = value;
        public string BindingText(string action) => action.ToUpperInvariant();
        public void ClearBindings(string action, bool pad) => Cleared.Add((action, pad));
        public void ResetControls() => ControlResets++;
        public void ResetSettings() => Resets++;
    }

    private const int Esc = MMenu.KEY_ESCAPE, Enter = MMenu.KEY_ENTER, Up = MMenu.KEY_UPARROW, Down = MMenu.KEY_DOWNARROW;
    private const int Left = MMenu.KEY_LEFTARROW, Right = MMenu.KEY_RIGHTARROW, Back = MMenu.KEY_BACKSPACE;

    private static (MMenu Menu, SetupHost Host) NewMenu()
    {
        (GameFlow flow, _) = GameFlowTests.New(GameMode.shareware);
        var host = new SetupHost();
        flow.Menu.SetupHost = host;
        flow.D_StartTitle(null);
        return (flow.Menu, host);
    }

    private static void Keys(MMenu menu, params int[] keys)
    {
        foreach (int key in keys)
            menu.M_Responder(key);
    }

    [Fact]
    public void TheSoundPageSwitchesTheMusicChip()
    {
        // T7.8g: More Options → Sound (U): the volumes and the chip, OPL3 by default, toggled by left and right
        SettingDef opl = Settings.Find("sound/opl")!;
        Assert.Equal((SettingPage.Sound, "opl3"), (opl.Page, opl.Default));
        Assert.Equal(new[] { "--level-opl" }, opl.Args);
        Assert.True(opl.Wraps);

        (MMenu menu, SetupHost host) = NewMenu();
        Keys(menu, Esc, 'o', Enter, 'o', Enter, 'u', Enter);
        Assert.Equal("soundsetup", menu.StateName);
        Assert.Equal(new[] { "SFX VOLUME", "MUSIC VOLUME", "MUSIC CHIP" }, menu.currentMenu.menuitems.Select(i => i.text));
        menu.itemOn = 2;
        Assert.Contains("MUSIC CHIP: OPL3", menu.StateText());
        Keys(menu, Right);
        Assert.Equal("opl2", host.Values["sound/opl"]);
        Keys(menu, Right);
        Assert.Equal("opl3", host.Values["sound/opl"]);
        Keys(menu, Left);
        Assert.Equal("opl2", host.Values["sound/opl"]);
        menu.itemOn = 1;
        Keys(menu, Left);
        Assert.Equal("7", host.Values["sound/music_volume"]);
    }

    [Fact]
    public void MoreOptionsReachesEveryPageAndChangesTheirSettings()
    {
        (MMenu menu, SetupHost host) = NewMenu();
        Keys(menu, Esc, 'o', Enter, 'o', Enter);
        Assert.Equal(("setup", 0), (menu.StateName, (int)menu.itemOn));
        var pages = new List<string>();
        for (int i = 0; i < menu.SetupDef.numitems; i++)
        {
            if (menu.SetupDef.menuitems[i].status != 1 || menu.SetupDef.menuitems[i].text.StartsWith("RESET", StringComparison.Ordinal))
                continue;
            menu.itemOn = (short)i;
            Keys(menu, Enter);
            pages.Add(menu.StateName);
            Keys(menu, Back);
        }
        Assert.Equal(new[] { "controls", "video", "gameplay", "sprites", "hud", "soundsetup" }, pages);

        // gameplay: the cutaway steps right and left (and Enter steps on), at once through the host
        Keys(menu, 'g', Enter);
        Assert.Equal("gameplay", menu.StateName);
        menu.itemOn = (short)Array.FindIndex(menu.SettingDefs[SettingPage.Gameplay].menuitems, i => i.text == "CUTAWAY");
        Keys(menu, Right);
        Assert.Equal("dither", host.Values["gameplay/cutaway"]);
        Keys(menu, Enter);
        Assert.Equal("off", host.Values["gameplay/cutaway"]);
        Keys(menu, Left);
        Assert.Equal("dither", host.Values["gameplay/cutaway"]);
        Assert.Contains("CUTAWAY: DITHER", menu.StateText());

        // every setting of every page is on a page, each once
        var shown = new List<string>();
        foreach (MMenu.menuitem_t item in menu.SettingDefs.Values.SelectMany(d => d.menuitems))
            shown.Add(item.text);
        shown.AddRange(menu.ControlsDef.menuitems.Select(i => i.text));
        foreach (SettingDef def in Settings.Defs)
            Assert.Single(shown, def.Label);

        // back to the options, then the main menu
        Keys(menu, Back, Back);
        Assert.Equal("options", menu.StateName);
    }

    [Fact]
    public void TheControlsPagesWaitForAnInputToBindAndClear()
    {
        (MMenu menu, SetupHost host) = NewMenu();
        Keys(menu, Esc, 'o', Enter, 'o', Enter, 'c', Enter);
        Assert.Equal("controls", menu.StateName);
        Keys(menu, Right, Right); // mouse sensitivity
        Assert.Equal("7", host.Values["controls/mouse_sensitivity"]);
        Keys(menu, 'a', Enter); // ACTIONS...
        Assert.Equal("actions", menu.StateName);
        Assert.Equal("attack", menu.ActionOn());
        Assert.Contains("FIRE: ATTACK", menu.StateText());
        Keys(menu, Enter);
        Assert.Equal(("binding", "attack"), (menu.StateName, menu.WaitingBinding));
        Assert.True(menu.M_Responder(Down)); // the glue takes the input: the menus ignore it
        Assert.Equal("attack", menu.WaitingBinding);
        Keys(menu, Esc);
        Assert.Equal(("actions", null), (menu.StateName, menu.WaitingBinding));
        Keys(menu, Enter);
        menu.M_BindingDone();
        Assert.Equal("actions", menu.StateName);

        Keys(menu, Down);
        Assert.True(menu.M_ClearBinding(true));
        Assert.Equal(("use", true), host.Cleared[^1]);

        // menu keys: one page, every menu action
        Keys(menu, Back, 'm', Enter);
        Assert.Equal("menukeys", menu.StateName);
        Assert.Equal(Settings.MenuActions.Length, menu.currentMenu.numitems);
        Keys(menu, Back);
        Assert.False(menu.M_ClearBinding(false)); // not a binding page

        // reset the controls: asks first
        menu.itemOn = (short)(menu.ControlsDef.numitems - 1);
        Keys(menu, Enter);
        Assert.Equal(MMenu.RESETCONTROLS, menu.messageString);
        Keys(menu, menu.key_menu_abort);
        Assert.Equal(0, host.ControlResets);
        Keys(menu, Esc, Enter);
        Assert.Equal("options", menu.StateName); // vanilla: a message closes the menus, Escape opens the main one, Enter on Options
        Keys(menu, 'o', Enter, 'c', Enter);
        menu.itemOn = (short)(menu.ControlsDef.numitems - 1);
        Keys(menu, Enter, menu.key_menu_confirm);
        Assert.Equal(1, host.ControlResets);
    }

    [Fact]
    public void ResetAllAsksFirst()
    {
        (MMenu menu, SetupHost host) = NewMenu();
        Keys(menu, Esc, 'o', Enter, 'o', Enter, 'r', Enter);
        Assert.Equal(MMenu.RESETALL, menu.messageString);
        Keys(menu, menu.key_menu_confirm);
        Assert.Equal((1, "closed"), (host.Resets, menu.StateName));
    }

    [Fact]
    public void TheTextPagesDrawTheirItemsValuesAndTheWait()
    {
        (GameFlow flow, _) = GameFlowTests.New(GameMode.shareware);
        flow.Menu.SetupHost = new SetupHost();
        MMenu menu = flow.Menu;
        var wad = new WadArchive(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) });
        menu.Graphics = new ScreenGraphics(wad, new HuStuff(wad));
        Keys(menu, Esc, 'o', Enter, 'o', Enter, 'g', Enter); // the synthetic font has A, B and C: CUTAWAY, CUT
        var screen = new HudScreen(0, HudScreen.SCREENHEIGHT);
        menu.M_Drawer(screen);
        Assert.Equal("gameplay", menu.StateName);
        int top = menu.currentMenu.y + MMenu.TEXTLINEHEIGHT; // item 1, CUTAWAY
        bool Drawn(int x0, int x1)
        {
            for (int y = top; y < top + MMenu.TEXTLINEHEIGHT; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    if (screen.Opaque[y * screen.Width + x] != 0)
                        return true;
                }
            }
            return false;
        }
        Assert.True(Drawn(menu.currentMenu.x, MMenu.VALUEX)); // the item's label
        Assert.True(Drawn(MMenu.VALUEX, screen.Width)); // its value
        Assert.Equal(1, menu.ItemAt(menu.currentMenu.x + 10, menu.currentMenu.y + MMenu.TEXTLINEHEIGHT + 2));

        Keys(menu, Back, 'c', Enter, 'a', Enter, Enter);
        Assert.NotNull(menu.WaitingBinding);
        screen.Clear();
        menu.M_Drawer(screen);
        Assert.Contains(screen.Opaque, b => b != 0);
        Assert.Equal(-1, menu.ItemAt(100, 100));
    }

    [Fact]
    public void TheMouseSensitivityScalesVanillasMouseTurning()
    {
        var builder = new TiccmdBuilder();
        var input = new TiccmdInput { MouseX = 10 };
        // g_game.c: mousex = 10 * (5 + 5) / 10 = 10; angleturn -= mousex * 8
        Assert.Equal(-80, builder.G_BuildTiccmd(input, Tweaks.Vanilla, 0, 0, 0, 0).angleturn);
        builder.MouseSensitivity = 9;
        Assert.Equal(-112, builder.G_BuildTiccmd(input, Tweaks.Vanilla, 0, 0, 0, 0).angleturn); // 10 * 14 / 10 = 14
        builder.MouseSensitivity = 0;
        Assert.Equal(40, builder.G_BuildTiccmd(input with { MouseX = -10 }, Tweaks.Vanilla, 0, 0, 0, 0).angleturn);
        // the twin-stick game aims at the cursor: the motion turns nothing
        Assert.Equal(Ticcmds.AbsoluteAngle(Tables.ANG90), builder.G_BuildTiccmd(input, Tweaks.TopDown, Tables.ANG90, 0, 0, Tables.ANG90).angleturn);
    }
}
