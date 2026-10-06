using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using IsoDoom.Render;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

/// <summary>
/// Debug scene for browsing every graphic in the IWAD (T1.6, milestone M1):
/// wall textures, flats, sprites (by sprite, frame and rotation; mirrored
/// rotations drawn flipped), wall patches, the other patch-format graphics
/// (UI, menus, fonts, intermission) and the palette tables. Graphics are
/// uploaded as indexed RG8 textures and coloured by the palette shader, with a
/// light-level slider (COLORMAP), an invulnerability toggle and a PLAYPAL
/// palette selector. A second tab lists every lump of the merged archive
/// (<see cref="LumpDirectory"/>: name, size, file, namespace, kind; lumps a
/// later file overrides are greyed out); selecting a graphic lump shows it as
/// the graphic browser does, with the browser's category and entry switched to
/// it, and double-clicking it opens the browser tab. The IWAD comes from <see cref="WadLocator"/>; when none is found the
/// viewer opens a file picker and remembers the choice.
/// <para>
/// User arguments (after <c>--</c>): <c>--viewer-check</c> walks every
/// graphic and every row of the lump list, checks it uploads and, with a real renderer, that the drawn
/// pixels match the CPU palette conversion, then quits (exit code 1 on a
/// failure). <c>--viewer-screenshots=DIR</c> saves a few viewport captures
/// to DIR and quits. See <see cref="WadViewerCheck"/>.
/// </para>
/// </summary>
public partial class WadViewer : Control
{
    private static readonly string[] CategoryNames = { "Wall textures", "Flats", "Sprites", "Wall patches", "Other graphics", "Palette" };

    /// <summary>Lump list columns.</summary>
    public const int LumpColumnIndex = 0, LumpColumnName = 1, LumpColumnSize = 2, LumpColumnFile = 3, LumpColumnNamespace = 4, LumpColumnKind = 5;
    private static readonly string[] LumpColumnTitles = { "#", "Name", "Size", "File", "Namespace", "Kind" };
    private const int GraphicsTab = 0, LumpsTab = 1;
    private const float GraphicsTabWidth = 240, LumpsTabWidth = 700;

    private GraphicsCatalog? _catalog;
    private ShaderMaterial? _material;
    private FileDialog _picker = null!;

    private TabContainer _tabs = null!;
    private LineEdit _lumpFilter = null!;
    private OptionButton _lumpKind = null!;
    private Tree _lumpTree = null!;
    private IReadOnlyList<LumpEntry>? _lumps;
    private readonly List<int> _lumpRows = new(); // archive lump indices behind the tree rows
    private readonly Dictionary<int, TreeItem> _lumpItems = new();
    private bool _selectingLump;

    private OptionButton _category = null!;
    private LineEdit _filter = null!;
    private ItemList _list = null!;
    private HSlider _light = null!;
    private Label _lightLabel = null!;
    private CheckBox _invuln = null!;
    private SpinBox _palette = null!;
    private SpinBox _zoom = null!;
    private CheckBox _corrected = null!;
    private HBoxContainer _spriteBar = null!;
    private OptionButton _frame = null!;
    private OptionButton _rotation = null!;
    private Label _info = null!;
    private ScrollContainer _scroll = null!;
    private IndexedGraphicRect _main = null!;
    private GridContainer _strip = null!;

    private readonly List<int> _shown = new(); // catalog indices behind the list rows

    /// <summary>The catalog, or null when no IWAD could be loaded.</summary>
    public GraphicsCatalog? Catalog => _catalog;

    /// <summary>The picture currently shown in the main view.</summary>
    public GraphicView? CurrentView { get; private set; }

    /// <summary>The main view (for checks and captures).</summary>
    public IndexedGraphicRect MainView => _main;

    /// <summary>Every lump of the loaded archive, classified (the lump list's rows when unfiltered).</summary>
    public IReadOnlyList<LumpEntry>? Lumps => _lumps;

    /// <summary>The archive lump indices of the lump list's rows, in order.</summary>
    public IReadOnlyList<int> LumpRows => _lumpRows;

    /// <summary>The lump selected in the lump list, or null.</summary>
    public LumpEntry? CurrentLump { get; private set; }

    /// <summary>The graphic browser's selected entry (catalog index), frame and rotation slot.</summary>
    public int? CurrentEntry => CurrentRow();
    public int CurrentFrame => Math.Max(0, _frame.Selected);
    public int CurrentSlot => Math.Max(0, _rotation.Selected);

    /// <summary>True when the main view shows a graphic (false for a non-graphic lump).</summary>
    public bool MainViewShown => _main.Visible;

    /// <summary>The text the info line shows.</summary>
    public string InfoText => _info.Text;

    /// <summary>The COLORMAP row and PLAYPAL palette the shader currently uses.</summary>
    public int CurrentColormap { get; private set; }
    public int CurrentPalette => (int)_palette.Value;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        TextureFilter = TextureFilterEnum.Nearest;
        BuildUi();

        IwadSearchResult found = WadLocator.Find(out IReadOnlyList<string> searched);
        if (found.Warning is not null)
            GD.PushWarning($"WAD viewer: {found.Warning}");
        if (found.Path is null)
        {
            string message = found.Error ?? "No IWAD found.";
            if (found.Source == IwadSource.None)
            {
                var existing = new List<string>();
                foreach (string dir in searched)
                {
                    if (Directory.Exists(dir))
                        existing.Add(dir);
                }
                GD.Print($"WAD viewer: searched {searched.Count} folders; these exist: {string.Join(", ", existing)}");
                message += " Choose your DOOM1.WAD, DOOM.WAD or DOOM2.WAD with \"Choose IWAD…\", put it in "
                    + $"{WadLocator.GameDirectories()[^1]}, set ISODOOM_IWAD, or pass -- -iwad PATH.";
            }
            ShowError(message);
            if (found.Source == IwadSource.None)
                OpenPicker();
            return;
        }
        GD.Print($"WAD viewer: IWAD from {Describe(found)}");
        LoadWad(found.Path, found.Pwads);

        if (_catalog is not null && IsCheckRun)
            AddChild(new WadViewerCheck(this));
    }

    private static bool IsCheckRun => WadLocator.HasUserArg("--viewer-check") || WadLocator.HasUserArg("--viewer-screenshots");

    private static string Describe(IwadSearchResult r) => r.Source switch
    {
        IwadSource.CommandLine => "the command line (-iwad)",
        IwadSource.Environment => $"the {r.SourceDetail} environment variable",
        IwadSource.Config => $"the configured path ({WadLocator.SettingsPath})",
        IwadSource.Search => $"the search directory {r.SourceDetail}",
        _ => "nowhere",
    };

    /// <summary>Loads an IWAD (plus PWADs) into the viewer; false, with the error shown, if it can't be loaded.</summary>
    private bool LoadWad(string path, IReadOnlyList<string> pwads)
    {
        GraphicsCatalog catalog;
        IwadInfo info;
        try
        {
            var archive = WadArchive.Open(path, [.. pwads]);
            info = IwadIdentification.D_IdentifyVersion(archive);
            ModifiedGame.D_CheckModifiedGame(archive, info);
            catalog = GraphicsCatalog.Load(archive);
        }
        catch (Exception e) when (e is WadFormatException or ModifiedGameException or IOException or UnauthorizedAccessException or KeyNotFoundException)
        {
            ShowError($"Could not load {path}: {e.Message}");
            return false;
        }

        _catalog = catalog;
        _lumps = LumpDirectory.Build(catalog.Wad);
        CurrentLump = null;
        _lumpFilter.Editable = true;
        _lumpKind.Disabled = false;
        _lumpFilter.Text = "";
        _lumpKind.Select(0);
        RebuildLumpTree();
        _material = IndexedTextures.CreatePaletteMaterial(
            IndexedTextures.CreatePlaypalTexture(_catalog.Playpal),
            IndexedTextures.CreateColormapTexture(_catalog.Colormap));
        _palette.MaxValue = _catalog.Playpal.Count - 1;
        _category.Disabled = false;
        _filter.Editable = true;
        _catalog.CompositeMode = _corrected.ButtonPressed ? TextureCompositeMode.Corrected : TextureCompositeMode.Vanilla;
        string files = pwads.Count == 0 ? "" : $" + {string.Join(", ", pwads)}";
        GD.Print($"WAD viewer: {path}{files} ({_catalog.Wad.NumLumps} lumps): {info}");

        UpdateLight();
        SelectCategory(GraphicCategory.Graphics);
        return true;
    }

    /// <summary>Shows the IWAD file picker (not in headless or check runs, where nobody can answer it).</summary>
    private void OpenPicker()
    {
        if (IsCheckRun)
            return;
        if (DisplayServer.GetName() == "headless")
        {
            GD.Print("WAD viewer: no display, so no IWAD file picker");
            return;
        }
        GD.Print("WAD viewer: showing the IWAD file picker");
        _picker.PopupCenteredRatio(0.7f);
    }

    private void OnIwadPicked(string path)
    {
        if (!LoadWad(path, Array.Empty<string>()))
            return;
        WadLocator.SaveConfiguredIwad(path);
        GD.Print($"WAD viewer: saved {path} as the configured IWAD in {WadLocator.SettingsPath}");
    }

    // ---- Selection API (used by the UI and by WadViewerCheck) ----

    public GraphicCategory Category => (GraphicCategory)_category.Selected;

    /// <summary>Shows a category's list (filter cleared) and selects its first entry.</summary>
    public void SelectCategory(GraphicCategory category)
    {
        _category.Select((int)category);
        _filter.Text = "";
        RebuildList();
    }

    /// <summary>Shows entry <paramref name="index"/> of the current category; for sprites also picks frame and rotation slot.</summary>
    public void SelectEntry(int index, int frame = 0, int slot = 0)
    {
        int row = _shown.IndexOf(index);
        if (row < 0)
            throw new ArgumentOutOfRangeException(nameof(index), "Entry is filtered out.");
        _list.Select(row);
        _list.EnsureCurrentIsVisible();
        OnEntrySelected(index, frame, slot);
    }

    /// <summary>Shows the graphic browser tab (true) or the lump list tab (false).</summary>
    public void ShowTab(bool lumps) => _tabs.CurrentTab = lumps ? LumpsTab : GraphicsTab;

    /// <summary>Clears the lump list's filters, so it shows every lump.</summary>
    public void ClearLumpFilter()
    {
        _lumpFilter.Text = "";
        _lumpKind.Select(0);
        RebuildLumpTree();
    }

    /// <summary>The text of a lump list cell (<c>LumpColumn*</c>) for archive lump <paramref name="index"/>.</summary>
    public string LumpCellText(int index, int column) => _lumpItems[index].GetText(column);

    /// <summary>Selects archive lump <paramref name="index"/> in the lump list, as a click does.</summary>
    public void SelectLump(int index)
    {
        if (!_lumpItems.TryGetValue(index, out TreeItem? item))
            throw new ArgumentOutOfRangeException(nameof(index), "Lump is filtered out.");
        _selectingLump = true;
        try
        {
            item.Select(0);
        }
        finally
        {
            _selectingLump = false;
        }
        _lumpTree.ScrollToItem(item);
        OnLumpSelected(index);
    }

    /// <summary>Sets the light level (0–255) and invulnerability toggle and the PLAYPAL palette.</summary>
    public void SetLighting(int light, bool invulnerable, int palette)
    {
        _light.SetValueNoSignal(light);
        _invuln.SetPressedNoSignal(invulnerable);
        _palette.SetValueNoSignal(palette);
        UpdateLight();
    }

    public void SetZoom(int zoom) => _zoom.Value = zoom;

    // ---- UI ----

    private void BuildUi()
    {
        var bg = new ColorRect { Color = new Color(0.12f, 0.12f, 0.14f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 8);
        AddChild(margin);

        var split = new HBoxContainer();
        split.AddThemeConstantOverride("separation", 8);
        margin.AddChild(split);

        // Left: a graphic browser tab (category, filter, list) and a lump list tab.
        _tabs = new TabContainer { CustomMinimumSize = new Vector2(GraphicsTabWidth, 0) };
        split.AddChild(_tabs);
        var left = new VBoxContainer { Name = "Graphics" };
        _tabs.AddChild(left);
        _category = new OptionButton();
        foreach (string name in CategoryNames)
            _category.AddItem(name);
        _category.ItemSelected += _ => { _filter.Text = ""; RebuildList(); };
        left.AddChild(_category);
        _filter = new LineEdit { PlaceholderText = "Filter by name", ClearButtonEnabled = true };
        _filter.TextChanged += _ => RebuildList();
        left.AddChild(_filter);
        _list = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill };
        _list.ItemSelected += row => OnEntrySelected(_shown[(int)row], 0, 0);
        left.AddChild(_list);

        var lumps = new VBoxContainer { Name = "Lumps" };
        _tabs.AddChild(lumps);
        var lumpFilters = new HBoxContainer();
        lumps.AddChild(lumpFilters);
        _lumpFilter = new LineEdit { PlaceholderText = "Filter by name", ClearButtonEnabled = true, Editable = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _lumpFilter.TextChanged += _ => RebuildLumpTree();
        lumpFilters.AddChild(_lumpFilter);
        _lumpKind = new OptionButton { Disabled = true, TooltipText = "Show only lumps of this kind" };
        _lumpKind.AddItem("All kinds");
        foreach (LumpKind kind in Enum.GetValues<LumpKind>())
            _lumpKind.AddItem(LumpDirectory.KindName(kind));
        _lumpKind.ItemSelected += _ => RebuildLumpTree();
        lumpFilters.AddChild(_lumpKind);
        _lumpTree = new Tree
        {
            Columns = LumpColumnTitles.Length,
            ColumnTitlesVisible = true,
            HideRoot = true,
            SelectMode = Tree.SelectModeEnum.Row,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            TooltipText = "Select a graphic to show it; double-click to open it in the Graphics tab",
        };
        int[] widths = { 60, 104, 64, 120, 84, 0 };
        for (int c = 0; c < LumpColumnTitles.Length; c++)
        {
            _lumpTree.SetColumnTitle(c, LumpColumnTitles[c]);
            _lumpTree.SetColumnExpand(c, widths[c] == 0);
            _lumpTree.SetColumnClipContent(c, true);
            if (widths[c] > 0)
                _lumpTree.SetColumnCustomMinimumWidth(c, widths[c]);
        }
        _lumpTree.ItemSelected += () =>
        {
            if (!_selectingLump && _lumpTree.GetSelected() is TreeItem item)
                OnLumpSelected(item.GetMetadata(0).AsInt32());
        };
        _lumpTree.ItemActivated += () =>
        {
            if (_catalog is not null && _lumpTree.GetSelected() is TreeItem item && _catalog.Locate(item.GetMetadata(0).AsInt32()) is not null)
                ShowTab(lumps: false);
        };
        lumps.AddChild(_lumpTree);
        _tabs.TabChanged += OnTabChanged; // after the tabs exist: adding the first one emits it

        // Right: controls, sprite controls, info, view.
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        split.AddChild(right);

        var controls = new HBoxContainer();
        controls.AddThemeConstantOverride("separation", 12);
        right.AddChild(controls);
        controls.AddChild(new Label { Text = "Light" });
        _light = new HSlider { MinValue = 0, MaxValue = 255, Step = 1, Value = 255, CustomMinimumSize = new Vector2(200, 0), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _light.ValueChanged += _ => UpdateLight();
        controls.AddChild(_light);
        _lightLabel = new Label { CustomMinimumSize = new Vector2(150, 0) };
        controls.AddChild(_lightLabel);
        _invuln = new CheckBox { Text = "Invulnerability" };
        _invuln.Toggled += _ => UpdateLight();
        controls.AddChild(_invuln);
        controls.AddChild(new Label { Text = "PLAYPAL" });
        _palette = new SpinBox { MinValue = 0, MaxValue = 13, Step = 1, Value = 0 };
        _palette.ValueChanged += _ => UpdateLight();
        controls.AddChild(_palette);
        controls.AddChild(new Label { Text = "Zoom" });
        _zoom = new SpinBox { MinValue = 1, MaxValue = 8, Step = 1, Value = 2, Suffix = "x" };
        _zoom.ValueChanged += v => ApplyZoom((int)v);
        controls.AddChild(_zoom);
        _corrected = new CheckBox { Text = "Corrected composites", TooltipText = "TextureCompositeMode.Corrected (Boom) instead of Vanilla" };
        _corrected.Toggled += on =>
        {
            if (_catalog is null)
                return;
            _catalog.CompositeMode = on ? TextureCompositeMode.Corrected : TextureCompositeMode.Vanilla;
            if (Category == GraphicCategory.Textures && CurrentRow() is int idx)
                OnEntrySelected(idx, 0, 0);
        };
        controls.AddChild(_corrected);
        var choose = new Button { Text = "Choose IWAD…", TooltipText = "Pick an IWAD file; it is remembered for the next start" };
        choose.Pressed += () => _picker.PopupCenteredRatio(0.7f);
        controls.AddChild(choose);

        _picker = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile, // sets a default title, so set Title after it
            ModeOverridesTitle = false,
            Title = "Choose an IWAD (DOOM1.WAD, DOOM.WAD, DOOM2.WAD, …)",
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = ["*.wad;WAD files"],
            UseNativeDialog = true, // falls back to Godot's own dialog where the platform has none
        };
        _picker.FileSelected += OnIwadPicked;
        AddChild(_picker);

        _spriteBar = new HBoxContainer { Visible = false };
        _spriteBar.AddThemeConstantOverride("separation", 12);
        right.AddChild(_spriteBar);
        _spriteBar.AddChild(new Label { Text = "Frame" });
        _frame = new OptionButton();
        _frame.ItemSelected += f => { if (CurrentRow() is int idx) OnEntrySelected(idx, (int)f, Math.Max(0, _rotation.Selected)); };
        _spriteBar.AddChild(_frame);
        _spriteBar.AddChild(new Label { Text = "Rotation" });
        _rotation = new OptionButton();
        _rotation.ItemSelected += r => { if (CurrentRow() is int idx) OnEntrySelected(idx, _frame.Selected, (int)r); };
        _spriteBar.AddChild(_rotation);

        _info = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        right.AddChild(_info);

        _scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddChild(_scroll);
        var viewBox = new VBoxContainer();
        viewBox.AddThemeConstantOverride("separation", 16);
        _scroll.AddChild(viewBox);
        _main = new IndexedGraphicRect();
        viewBox.AddChild(_main);
        _strip = new GridContainer { Columns = 8, Visible = false };
        _strip.AddThemeConstantOverride("h_separation", 12);
        viewBox.AddChild(_strip);
    }

    private void ShowError(string message)
    {
        GD.Print($"WAD viewer: {message}");
        _info.Text = message;
        if (_catalog is null) // a failed pick keeps the WAD already shown
        {
            _category.Disabled = true;
            _filter.Editable = false;
        }
        if (IsCheckRun)
        {
            GD.PrintErr("WAD viewer check: FAILED (no WAD loaded)");
            GetTree().Quit(1);
        }
    }

    private int? CurrentRow()
    {
        int[] sel = _list.GetSelectedItems();
        return sel.Length == 0 ? null : _shown[sel[0]];
    }

    private void RebuildList()
    {
        _list.Clear();
        _shown.Clear();
        if (_catalog is null)
            return;
        GraphicCategory category = Category;
        string filter = _filter.Text.Trim().ToUpperInvariant();
        for (int i = 0; i < _catalog.Count(category); i++)
        {
            string name = _catalog.GetName(category, i);
            if (filter.Length > 0 && !name.Contains(filter, StringComparison.Ordinal))
                continue;
            string text = category == GraphicCategory.Sprites ? $"{name}  ({_catalog.GetSpriteDef(i).NumFrames} frames)" : name;
            _list.AddItem(text);
            _shown.Add(i);
        }
        _spriteBar.Visible = category == GraphicCategory.Sprites;
        if (_shown.Count > 0)
            SelectEntry(_shown[0]);
        else
            _info.Text = "No matches.";
    }

    private void OnEntrySelected(int index, int frame, int slot)
    {
        if (_catalog is null || _material is null)
            return;
        GraphicCategory category = Category;
        ClearStrip();
        _spriteBar.Visible = category == GraphicCategory.Sprites;
        if (category != GraphicCategory.Sprites)
        {
            Show(_catalog.Get(category, index));
            return;
        }

        SpriteDef def = _catalog.GetSpriteDef(index);
        frame = Math.Clamp(frame, 0, def.NumFrames - 1);
        SpriteFrame f = def.Frames[frame];
        slot = f.Rotate ? Math.Clamp(slot, 0, 7) : 0;

        _frame.Clear();
        for (int i = 0; i < def.NumFrames; i++)
            _frame.AddItem($"{(char)('A' + i)}{(def.Frames[i].Rotate ? "" : " (rot 0)")}");
        _frame.Select(frame);
        _rotation.Clear();
        if (f.Rotate)
        {
            for (int r = 1; r <= 8; r++)
                _rotation.AddItem(r.ToString());
        }
        else
        {
            _rotation.AddItem("0 (all angles)");
        }
        _rotation.Select(slot);

        Show(_catalog.GetSprite(index, frame, slot));

        // All eight rotations of a rotating frame, mirrored ones flipped.
        if (f.Rotate)
        {
            for (int r = 0; r < 8; r++)
            {
                GraphicView v = _catalog.GetSprite(index, frame, r);
                var cell = new VBoxContainer();
                cell.AddChild(new Label { Text = $"{r + 1}: {v.Name}{(v.Flip ? " (flip)" : "")}" });
                var rect = new IndexedGraphicRect { Zoom = (int)_zoom.Value };
                rect.SetGraphic(v.Image, v.Flip, _material);
                cell.AddChild(rect);
                _strip.AddChild(cell);
            }
            _strip.Visible = true;
        }
    }

    private void Show(GraphicView view)
    {
        CurrentView = view;
        _main.Visible = true;
        _main.SetGraphic(view.Image, view.Flip, _material!);
        _main.Zoom = (int)_zoom.Value;
        _info.Text = view.Label;
    }

    private void OnTabChanged(long tab)
    {
        _tabs.CustomMinimumSize = new Vector2(tab == LumpsTab ? LumpsTabWidth : GraphicsTabWidth, 0);
        // Back in the browser: show its selection again (the lump list may have shown something else).
        if (tab == GraphicsTab && CurrentRow() is int idx)
            OnEntrySelected(idx, CurrentFrame, CurrentSlot);
    }

    private void RebuildLumpTree()
    {
        _lumpTree.Clear();
        _lumpRows.Clear();
        _lumpItems.Clear();
        if (_lumps is null)
            return;
        TreeItem root = _lumpTree.CreateItem();
        string filter = _lumpFilter.Text.Trim().ToUpperInvariant();
        LumpKind? kind = _lumpKind.Selected > 0 ? (LumpKind)(_lumpKind.Selected - 1) : null;
        var dim = new Color(0.55f, 0.55f, 0.55f);
        foreach (LumpEntry e in _lumps)
        {
            if (filter.Length > 0 && !e.Lump.Name.Contains(filter, StringComparison.Ordinal))
                continue;
            if (kind is LumpKind k && e.Kind != k)
                continue;
            TreeItem item = _lumpTree.CreateItem(root);
            item.SetMetadata(0, e.Index);
            item.SetText(LumpColumnIndex, e.Index.ToString());
            item.SetText(LumpColumnName, e.Lump.Name);
            item.SetText(LumpColumnSize, e.Lump.Size.ToString());
            item.SetTextAlignment(LumpColumnSize, HorizontalAlignment.Right);
            item.SetText(LumpColumnFile, e.Lump.File.Name);
            item.SetText(LumpColumnNamespace, LumpDirectory.NamespaceName(e.Lump.Namespace));
            item.SetText(LumpColumnKind, KindText(e));
            if (e.IsOverridden)
            {
                for (int c = 0; c < LumpColumnTitles.Length; c++)
                    item.SetCustomColor(c, dim);
                item.SetTooltipText(LumpColumnKind, $"Not used: overridden by {e.OverriddenBy!.Name} in {e.OverriddenBy.File.Name}");
            }
            _lumpRows.Add(e.Index);
            _lumpItems[e.Index] = item;
        }
    }

    private static string KindText(LumpEntry e)
    {
        string text = LumpDirectory.KindName(e.Kind);
        if (e.Detail.Length > 0)
            text += $" ({e.Detail})";
        if (e.OverriddenBy is WadLump by)
            text += $", overridden by {by.File.Name}";
        return text;
    }

    private void OnLumpSelected(int index)
    {
        if (_catalog is null || _lumps is null)
            return;
        LumpEntry e = _lumps[index];
        CurrentLump = e;
        string line = $"Lump #{e.Index} {e.Lump.Name}: {e.Lump.Size} bytes, {e.Lump.File.Name} entry {e.Lump.Index}, "
            + $"{LumpDirectory.NamespaceName(e.Lump.Namespace)} namespace, {KindText(e)}"
            + (e.OverriddenBy is WadLump by ? $" (#{_lumps.First(x => ReferenceEquals(x.Lump, by)).Index} {by.Name})" : "");

        if (_catalog.Locate(index) is GraphicLocation loc)
        {
            // Switch the graphic browser to it (its list only, not the tab) and show it from there.
            if (Category != loc.Category || _filter.Text.Length > 0)
                SelectCategory(loc.Category);
            SelectEntry(loc.Index, loc.Frame, loc.Slot);
            _info.Text = $"{line}\n{_info.Text}";
            return;
        }

        ClearStrip();
        _spriteBar.Visible = false;
        if (e.IsGraphic)
        {
            string note = e.OverriddenBy is WadLump o ? $"overridden by {o.File.Name}, so no list shows it" : "not used by any sprite frame";
            try
            {
                Show(_catalog.ViewLump(e.Lump, note));
                _info.Text = $"{line}\n{_info.Text}";
                return;
            }
            catch (WadFormatException ex)
            {
                line += $"\nCannot decode it: {ex.Message}";
            }
        }
        CurrentView = null;
        _main.Visible = false;
        _info.Text = line;
    }

    private void ClearStrip()
    {
        foreach (Node child in _strip.GetChildren())
        {
            _strip.RemoveChild(child);
            child.QueueFree();
        }
        _strip.Visible = false;
    }

    private void ApplyZoom(int zoom)
    {
        _main.Zoom = zoom;
        foreach (Node cell in _strip.GetChildren())
        {
            foreach (Node child in cell.GetChildren())
            {
                if (child is IndexedGraphicRect rect)
                    rect.Zoom = zoom;
            }
        }
    }

    private void UpdateLight()
    {
        int light = (int)_light.Value;
        CurrentColormap = _invuln.ButtonPressed ? Colormap.INVERSECOLORMAP : IndexedTextures.ViewerLightToColormap(light);
        _lightLabel.Text = _invuln.ButtonPressed ? $"{light} (COLORMAP {CurrentColormap})" : $"{light} → COLORMAP {CurrentColormap}";
        if (_material is null)
            return;
        IndexedTextures.SetColormap(_material, CurrentColormap);
        IndexedTextures.SetPalette(_material, (int)_palette.Value);
    }
}
