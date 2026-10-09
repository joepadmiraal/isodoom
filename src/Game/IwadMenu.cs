using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using IsoDoom.Wad;

namespace IsoDoom.Game;

/// <summary>
/// T7.2: the IWAD selection menu, before the game opens (no WAD, so no
/// WAD graphics: Godot's own controls, large for a 7" screen and
/// navigable with the keyboard, the mouse and the pad's D-pad and A).
/// Shown when the search (<see cref="IwadLocator"/>, T1.1a) finds several
/// IWADs (<see cref="IwadLocator.D_FindAllIWADs"/>) and none was named
/// (<c>-iwad</c>, <c>ISODOOM_IWAD</c>), the last one chosen first; and when
/// none is found, with only the file picker (which T1.6 put in the WAD
/// viewer) and, T1.1b, the shareware download (<see cref="SharewareDownload"/>:
/// id's <c>doom19s.zip</c> from the idgames mirrors, its <c>DOOM1.WAD</c>
/// verified and kept in <c>user://wads</c>). The choice is checked (it must open and be an IWAD the game
/// accepts with the <c>-file</c> PWADs), saved as the configured IWAD
/// (<c>[wad] iwad</c> in <c>user://settings.cfg</c>), and the game opens on it.
/// </summary>
public partial class IwadMenu : Control
{
    private readonly IReadOnlyList<string> _iwads;
    private readonly string? _selected;
    private readonly string? _message;
    private readonly IReadOnlyList<string> _pwads;
    private readonly bool _offerDownload;
    private Label _error = null!;
    private Label _status = null!;
    private bool _downloading;
    private FileDialog _picker = null!;
    private readonly List<Button> _buttons = [];

    /// <summary>The game should open: the chosen IWAD is <see cref="WadLocator.ChosenIwad"/>.</summary>
    public event Action? Chosen;

    /// <param name="iwads">The IWADs found, in the search's order of preference.</param>
    /// <param name="selected">The one the search would take (focused first), or null.</param>
    /// <param name="message">Why the menu shows without a choice (none found), or null.</param>
    /// <param name="pwads">The <c>-file</c> PWADs the game will load with it.</param>
    /// <param name="offerDownload">T1.1b: offer the shareware download (when no IWAD is found).</param>
    public IwadMenu(IReadOnlyList<string> iwads, string? selected, string? message, IReadOnlyList<string> pwads, bool offerDownload = false)
    {
        _offerDownload = offerDownload;
        _iwads = iwads;
        _selected = selected;
        _message = message;
        _pwads = pwads;
        Name = "IwadMenu";
    }

    /// <summary>The menu's buttons (the IWADs', then the download when offered, browse and quit), for checks.</summary>
    public IReadOnlyList<Button> Buttons => _buttons;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        EnsurePadUi();
        var backdrop = new ColorRect { Color = Colors.Black, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(backdrop);
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var centre = new CenterContainer();
        AddChild(centre);
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var box = new VBoxContainer();
        centre.AddChild(box);

        float height = GetViewport().GetVisibleRect().Size.Y;
        int text = Math.Max(16, (int)(height / 26)); // about 31 px at 800 rows: readable on a 7" 1280×800 screen
        box.AddThemeConstantOverride("separation", text / 2);
        box.AddChild(new Label
        {
            Text = "IsoDoom",
            HorizontalAlignment = HorizontalAlignment.Center,
            LabelSettings = new LabelSettings { FontSize = text * 2, FontColor = new Color(0.8f, 0.1f, 0.1f) },
        });
        float wrap = Math.Min(GetViewport().GetVisibleRect().Size.X * 0.8f, 1200);
        box.AddChild(new Label
        {
            Text = _message ?? "Choose a game",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(wrap, 0),
            LabelSettings = new LabelSettings { FontSize = text },
        });

        Button? focus = null;
        foreach (string path in _iwads)
        {
            string name = IwadLocator.DescriptionForName(Path.GetFileName(path)) ?? Path.GetFileName(path);
            Button button = AddButton(box, $"{name}    ({path})", text, () => Choose(path));
            if (focus is null || PathsEqual(path, _selected))
                focus = button;
        }
        if (_offerDownload)
        {
            Button download = AddButton(box, "Download the shareware episode (DOOM1.WAD, 2.4 MB from idgames)", text, Download);
            focus ??= download;
        }
        Button browse = AddButton(box, "Browse for an IWAD…", text, Browse);
        AddButton(box, "Quit", text, () => GetTree().Quit());
        _status = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            LabelSettings = new LabelSettings { FontSize = Math.Max(14, text * 3 / 4) },
        };
        box.AddChild(_status);
        _error = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(wrap, 0),
            LabelSettings = new LabelSettings { FontSize = Math.Max(14, text * 3 / 4), FontColor = new Color(1, 0.6f, 0.4f) },
        };
        box.AddChild(_error);

        _picker = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile, // sets a default title, so set Title after it
            ModeOverridesTitle = false,
            Title = "Choose an IWAD (DOOM1.WAD, DOOM.WAD, DOOM2.WAD, …)",
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = ["*.wad;WAD files"],
            UseNativeDialog = true, // falls back to Godot's own dialog where the platform has none
        };
        _picker.FileSelected += Choose;
        AddChild(_picker);

        (focus ?? browse).CallDeferred(Control.MethodName.GrabFocus);
        GD.Print($"IWAD menu: {_iwads.Count} IWAD(s) found{(_message is null ? "" : $" ({_message})")}: {string.Join(", ", _iwads)}");
    }

    /// <summary>
    /// The pad for Godot's controls: A presses the focused button
    /// (<c>ui_accept</c>), the left stick moves the focus as the D-pad does
    /// (<c>ui_up</c>, <c>ui_down</c>), where the input map lacks them.
    /// </summary>
    private static void EnsurePadUi()
    {
        Add("ui_accept", new InputEventJoypadButton { ButtonIndex = JoyButton.A });
        Add("ui_up", new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = -1 });
        Add("ui_down", new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = 1 });

        static void Add(string action, InputEvent e)
        {
            if (!InputMap.HasAction(action))
                return;
            foreach (InputEvent existing in InputMap.ActionGetEvents(action))
            {
                if (existing.IsMatch(e))
                    return;
            }
            InputMap.ActionAddEvent(action, e);
        }
    }

    private static bool PathsEqual(string a, string? b) =>
        b is not null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private Button AddButton(VBoxContainer box, string text, int size, Action pressed)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.All, Alignment = HorizontalAlignment.Left };
        button.AddThemeFontSizeOverride("font_size", size);
        button.Pressed += pressed;
        button.MouseEntered += () => button.GrabFocus(); // the pointer moves the focus, as the game menus' skull
        box.AddChild(button);
        _buttons.Add(button);
        return button;
    }

    /// <summary>
    /// T1.1b: downloads and verifies the shareware <c>DOOM1.WAD</c>
    /// (<see cref="WadLocator.DownloadShareware"/>), showing its progress, and
    /// chooses it; on a failure says why and leaves the menu as it was.
    /// Quit stays live meanwhile.
    /// </summary>
    private async void Download()
    {
        if (_downloading)
            return;
        _downloading = true;
        Button quit = _buttons[^1];
        foreach (Button button in _buttons)
            button.Disabled = button != quit;
        quit.GrabFocus();
        _error.Text = "";
        _status.Text = "Downloading…";
        // Progress<T> reports on the scene's synchronization context (the main thread).
        var progress = new Progress<SharewareDownload.Progress>(p => _status.Text = p.Total is > 0
            ? $"Downloading from {p.Mirror.Host}… {p.Received * 100 / p.Total.Value}%"
            : $"Downloading from {p.Mirror.Host}… {p.Received / 1024} KB");
        try
        {
            string path = await WadLocator.DownloadShareware(progress);
            _status.Text = "";
            GD.Print($"IWAD menu: the shareware {SharewareDownload.FileName} is verified in {path}");
            Choose(path);
        }
        catch (SharewareDownloadException e)
        {
            _status.Text = "";
            _error.Text = e.Message;
            GD.PrintErr($"IWAD menu: {e.Message}");
        }
        finally
        {
            _downloading = false;
            if (IsInsideTree())
            {
                foreach (Button button in _buttons)
                    button.Disabled = false;
            }
        }
    }

    private void Browse()
    {
        if (DisplayServer.GetName() == "headless")
            return;
        _picker.PopupCenteredRatio(0.7f);
    }

    /// <summary>Checks <paramref name="path"/> (it opens, with the PWADs, as an IWAD the game accepts), saves it and opens the game; else says why.</summary>
    public void Choose(string path)
    {
        try
        {
            var wad = WadArchive.Open(path, [.. _pwads]);
            IwadInfo info = IwadIdentification.D_IdentifyVersion(wad);
            ModifiedGame.D_CheckModifiedGame(wad, info);
        }
        catch (Exception e) when (e is WadFormatException or ModifiedGameException or IOException or UnauthorizedAccessException)
        {
            _error.Text = $"{path}: {e.Message}";
            GD.PrintErr($"IWAD menu: {path}: {e.Message}");
            return;
        }
        WadLocator.ChosenIwad = path;
        WadLocator.SaveConfiguredIwad(path);
        GD.Print($"IWAD menu: chose {path} (saved as the configured IWAD in {WadLocator.SettingsPath})");
        Chosen?.Invoke();
    }
}
