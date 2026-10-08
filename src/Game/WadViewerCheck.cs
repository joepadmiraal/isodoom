using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Render;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

/// <summary>
/// Automated checks for the WAD viewer (T1.6), started by user arguments:
/// <list type="bullet">
/// <item><c>--viewer-check</c>: selects every graphic through the viewer's
/// UI (every entry of every category, every sprite frame and rotation slot)
/// and checks that its RG8 upload holds the right bytes and size. With a real
/// renderer (not <c>--headless</c>) it also reads the drawn frame back and
/// compares every pixel of the graphic with the CPU palette conversion
/// (<see cref="IndexedImage.ToRgba"/>, the path the T1.3/T1.4 reference PNGs
/// use), mirrored when the view is flipped, under three lighting settings.
/// Transparent pixels must show the background. Then it walks every row of
/// the lump list (T1.6a): the list must hold every lump of the merged archive
/// in order with its name, size, file and namespace; selecting a graphic lump
/// (patch, flat, sprite, other graphic) must show exactly that lump's picture,
/// with the browser switched to its entry when it has one, and pass the same
/// upload and pixel checks; selecting any other lump must hide the main view.
/// Prints a summary and quits with exit code 1 on any failure.</item>
/// <item><c>--viewer-screenshots=DIR</c>: saves viewport captures of a few
/// showcase graphics to DIR (keep it out of the repo, or in a gitignored
/// folder: the images are WAD data) and quits.</item>
/// </list>
/// </summary>
public partial class WadViewerCheck : Node
{
    private readonly WadViewer _viewer;
    private int _failures;
    private readonly List<string> _failureLog = [];

    public WadViewerCheck(WadViewer viewer) => _viewer = viewer;

    public WadViewerCheck() => _viewer = null!; // for Godot's reflection; never used

    public override void _Ready() => _ = RunAsync();

    private bool CanCapture => DisplayServer.GetName() != "headless";

    private async Task RunAsync()
    {
        try
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            await NextFrame();
            if (WadLocator.HasUserArg("--viewer-check"))
            {
                await CheckAll();
                await CheckLumps();
            }
            if (WadLocator.GetUserArg("--viewer-screenshots") is string dir)
                await Screenshots(dir);
        }
        catch (Exception e)
        {
            Fail($"exception: {e}");
        }
        GD.Print(_failures == 0 ? "WAD viewer check: OK" : $"WAD viewer check: FAILED ({_failures} failure(s))");
        // T7.8e: the song preview's thread stopped and its playback freed before the quit (else reported as leaked)
        _viewer.Music?.StopThread();
        ulong until = Time.GetTicksMsec() + 150;
        while (Time.GetTicksMsec() < until)
            await NextFrame();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private async Task CheckAll()
    {
        GraphicsCatalog catalog = _viewer.Catalog ?? throw new InvalidOperationException("No WAD loaded.");
        bool gpu = CanCapture;
        Color background = UnusedColor(catalog.Playpal);
        _viewer.MainView.SolidBackground = background;
        _viewer.SetZoom(1);

        // (light, invulnerable, palette): full bright; dim; invulnerability with a red tint.
        (int, bool, int)[] settings = gpu
            ? [(255, false, 0), (100, false, 0), (255, true, Playpal.STARTREDPALS + 2)]
            : [(255, false, 0)];

        int expectedViews = 0;
        foreach (GraphicView _ in catalog.EnumerateAll())
            expectedViews++;

        foreach ((int light, bool invuln, int palette) in settings)
        {
            _viewer.SetLighting(light, invuln, palette);
            int views = 0, pixels = 0;
            foreach (GraphicCategory category in Enum.GetValues<GraphicCategory>())
            {
                _viewer.SelectCategory(category);
                for (int i = 0; i < catalog.Count(category); i++)
                {
                    if (category != GraphicCategory.Sprites)
                    {
                        _viewer.SelectEntry(i);
                        pixels += await CheckCurrent(gpu, background);
                        views++;
                        continue;
                    }
                    SpriteDef def = catalog.GetSpriteDef(i);
                    for (int frame = 0; frame < def.NumFrames; frame++)
                    {
                        for (int slot = 0; slot < (def.Frames[frame].Rotate ? 8 : 1); slot++)
                        {
                            _viewer.SelectEntry(i, frame, slot);
                            pixels += await CheckCurrent(gpu, background);
                            views++;
                        }
                    }
                }
            }
            if (views != expectedViews)
                Fail($"visited {views} views, the catalog has {expectedViews}");
            GD.Print($"WAD viewer check: light {light}{(invuln ? " invulnerable" : "")} palette {palette} "
                + $"(COLORMAP {_viewer.CurrentColormap}): {views} graphics uploaded"
                + (gpu ? $", {pixels} drawn pixels compared" : " (headless: upload only, no pixel readback)"));
        }
        _viewer.MainView.SolidBackground = null;
        _viewer.SetZoom(2);
        _viewer.SetLighting(255, false, 0);
    }

    private async Task CheckLumps()
    {
        GraphicsCatalog catalog = _viewer.Catalog ?? throw new InvalidOperationException("No WAD loaded.");
        IReadOnlyList<LumpEntry> lumps = _viewer.Lumps ?? throw new InvalidOperationException("No lump list.");
        WadArchive wad = catalog.Wad;
        bool gpu = CanCapture;
        Color background = UnusedColor(catalog.Playpal);
        _viewer.MainView.SolidBackground = background;
        _viewer.SetZoom(1);
        _viewer.SetLighting(255, false, 0);
        _viewer.ShowTab(lumps: true);
        _viewer.ClearLumpFilter();
        await NextFrame();

        // The list holds every lump, in archive order, with the right cells.
        if (lumps.Count != wad.NumLumps || _viewer.LumpRows.Count != wad.NumLumps)
            Fail($"lump list: {_viewer.LumpRows.Count} rows, {lumps.Count} entries, the archive has {wad.NumLumps} lumps");
        for (int row = 0; row < _viewer.LumpRows.Count; row++)
        {
            int i = _viewer.LumpRows[row];
            WadLump lump = wad.Lumps[i];
            if (i != row || !ReferenceEquals(lumps[i].Lump, lump)
                || _viewer.LumpCellText(i, WadViewer.LumpColumnName) != lump.Name
                || _viewer.LumpCellText(i, WadViewer.LumpColumnSize) != lump.Size.ToString()
                || _viewer.LumpCellText(i, WadViewer.LumpColumnFile) != lump.File.Name
                || _viewer.LumpCellText(i, WadViewer.LumpColumnNamespace) != LumpDirectory.NamespaceName(lump.Namespace)
                || !_viewer.LumpCellText(i, WadViewer.LumpColumnKind).StartsWith(LumpDirectory.KindName(lumps[i].Kind), StringComparison.Ordinal))
                Fail($"lump list row {row}: wrong lump or cells for #{i} {lump.Name}");
        }

        int viaBrowser = 0, direct = 0, tables = 0, others = 0, pixels = 0, sounds = 0, unplayable = 0, songs = 0, banks = 0;
        int songsPlayed = 0;
        int? firstSong = null;
        int[] kinds = new int[Enum.GetValues<LumpKind>().Length];
        foreach (int i in _viewer.LumpRows)
        {
            LumpEntry e = lumps[i];
            kinds[(int)e.Kind]++;
            int previews = _viewer.PreviewStarts;
            _viewer.SelectLump(i);
            string what = $"lump #{i} {e.Lump.Name} ({e.Kind})";
            if (!ReferenceEquals(_viewer.CurrentLump, e))
            {
                Fail($"{what}: not the selected lump");
                continue;
            }
            if (!_viewer.InfoText.Contains(e.Lump.Name, StringComparison.Ordinal))
                Fail($"{what}: the info line doesn't name it");
            GraphicLocation? loc = catalog.Locate(i);
            if (!e.IsGraphic && loc is null)
            {
                if (_viewer.MainViewShown || _viewer.CurrentView is not null)
                    Fail($"{what}: a non-graphic lump left a graphic shown");
                others++;
                // T7.8b: a MUS song's header and events, the OPL bank's instruments (or why not), as the readers give them
                string music = WadViewer.MusicText(e);
                if (music.Length > 0)
                {
                    if (!_viewer.InfoText.EndsWith(music, StringComparison.Ordinal))
                        Fail($"{what}: the info line does not show the song or the instruments");
                    if (e.Kind == LumpKind.Music && MusSong.TryRead(e.Lump.Data.Span, out _) is not null)
                        songs++;
                    else if (e.Kind == LumpKind.Instruments && Genmidi.TryRead(e.Lump.Data.Span, out _) is not null)
                        banks++;
                }
                // T7.8e: a song plays on the OPL player when selected (every MUS song mus2mid.c converts, and MIDI files), nothing else
                if (e.Kind == LumpKind.Music)
                {
                    bool convertible = MusSong.TryRead(e.Lump.Data.Span, out _) is { Mus2MidError: null } || LumpDirectory.MusicFormat(e.Lump.Data.Span) == "MIDI";
                    IsoDoom.Audio.RecordingMusicDevice? rec = _viewer.Music?.Record;
                    if (_viewer.PreviewSong is string playing)
                    {
                        if (playing != e.Lump.Name || rec is not { Playing: true, Looping: true } || rec.Song != e.Lump.Name)
                            Fail($"{what}: the song preview plays {playing} ({_viewer.Music})");
                        else
                        {
                            songsPlayed++;
                            firstSong ??= i;
                        }
                    }
                    else if (convertible && _viewer.Music is { CanPlay: true })
                        Fail($"{what}: no song preview started");
                }
                else if (_viewer.PreviewSong is not null)
                    Fail($"{what}: a song preview for a lump that is no song");
                // T7.7: a digitized sound plays as decoded (DMX's pads left out, 8-bit signed at its own rate); nothing else plays
                DmxSound? expected = e.Kind == LumpKind.Sound ? DmxSound.TryDecode(e.Lump.Data.Span, out _) : null;
                if (expected is null)
                {
                    if (_viewer.PreviewSound is not null)
                        Fail($"{what}: a sound preview for a lump that is no playable sound");
                    if (e.Kind == LumpKind.Sound && DmxSound.HasHeader(e.Lump.Data.Span))
                        unplayable++;
                    continue;
                }
                sounds++;
                AudioStreamWav? stream = _viewer.PreviewStream;
                if (_viewer.PreviewSound is null || stream is null || _viewer.PreviewStarts != previews + 1)
                    Fail($"{what}: no sound preview started");
                else if (stream.MixRate != expected.SampleRate || stream.Stereo || stream.Format != AudioStreamWav.FormatEnum.Format8Bits
                    || !stream.Data.AsSpan().SequenceEqual(expected.ToSigned8()))
                    Fail($"{what}: the preview's stream is not the lump's sound ({stream.MixRate} Hz, {stream.Data.Length} bytes; expected {expected.SampleRate} Hz, {expected.Samples.Length})");
                continue;
            }

            if (!_viewer.MainViewShown || _viewer.CurrentView is not GraphicView view)
            {
                Fail($"{what}: no graphic shown");
                continue;
            }
            if (loc is GraphicLocation l)
            {
                // Shown through the browser: its category and entry (and sprite frame/slot) point at it.
                if (_viewer.Category != l.Category || _viewer.CurrentEntry != l.Index
                    || (l.Category == GraphicCategory.Sprites && (_viewer.CurrentFrame != l.Frame || _viewer.CurrentSlot != l.Slot)))
                    Fail($"{what}: the browser shows {_viewer.Category} entry {_viewer.CurrentEntry}, expected {l}");
            }
            if (!e.IsGraphic)
            {
                tables++; // PLAYPAL/COLORMAP open their palette views
            }
            else
            {
                if (loc is null)
                    direct++;
                else
                    viaBrowser++;
                IndexedImage expected = e.Kind == LumpKind.Flat ? Flat.Decode(e.Lump.Data.Span, e.Lump.Name) : Patch.Decode(e.Lump.Data.Span, e.Lump.Name);
                IndexedImage shown = view.Image;
                if (view.Name != e.Lump.Name || view.Flip || shown.Width != expected.Width || shown.Height != expected.Height
                    || shown.LeftOffset != expected.LeftOffset || shown.TopOffset != expected.TopOffset
                    || !shown.Pixels.AsSpan().SequenceEqual(expected.Pixels) || !shown.Opaque.AsSpan().SequenceEqual(expected.Opaque))
                    Fail($"{what}: shows {view.Category}/{view.Name}{(view.Flip ? " flipped" : "")}, not the lump's picture");
            }
            pixels += await CheckCurrent(gpu, background);
        }
        string heard = await CheckSongHeard(firstSong);
        var kindSummary = new List<string>();
        foreach (LumpKind k in Enum.GetValues<LumpKind>())
        {
            if (kinds[(int)k] > 0)
                kindSummary.Add($"{kinds[(int)k]} {LumpDirectory.KindName(k)}");
        }
        GD.Print($"WAD viewer check: lump list: {_viewer.LumpRows.Count} lumps ({string.Join(", ", kindSummary)}); "
            + $"{viaBrowser + direct} graphic lumps shown ({viaBrowser} through the browser, {direct} directly), "
            + $"{tables} palette tables shown, {others} other lumps ({sounds} sounds played, {unplayable} digitized sounds DMX would not play, {songs} MUS songs and {banks} OPL banks read, {songsPlayed} songs played{heard})"
            + (gpu ? $", {pixels} drawn pixels compared" : " (headless: upload only)"));
        _viewer.ShowTab(lumps: false);
        _viewer.MainView.SolidBackground = null;
        _viewer.SetZoom(2);
    }

    /// <summary>Checks the current view's upload and, when possible, its drawn pixels. Returns pixels compared.</summary>
    /// <summary>
    /// T7.8e: the first song played again for a second of wall time: the OPL
    /// player's thread renders it at the mix rate (the dummy driver mixes at
    /// real time headless), notes sound, and its ring buffer never runs dry.
    /// </summary>
    private async Task<string> CheckSongHeard(int? row)
    {
        if (row is not int i || _viewer.Music is not { } player)
            return "";
        _viewer.SelectLump(i);
        long pushed = player.FramesPushed;
        player.TakePeak();
        ulong t0 = Time.GetTicksUsec();
        while (Time.GetTicksUsec() - t0 < 1_000_000)
            await NextFrame();
        double elapsed = (Time.GetTicksUsec() - t0) / 1e6;
        double rendered = (player.FramesPushed - pushed) / (double)player.MixRate;
        int peak = player.TakePeak();
        string name = _viewer.PreviewSong ?? "?";
        if (rendered < elapsed - 0.25 || rendered > elapsed + 0.25 || peak < 256 || player.Underruns != 0)
            Fail($"song preview {name}: {rendered:0.00} s rendered in {elapsed:0.00} s, peak {peak} ({player})");
        _viewer.SelectLump(i == 0 ? 1 : 0); // stops it
        return $"; {name} heard for {elapsed:0.0} s: {rendered:0.00} s rendered, peak {peak}, {player.Underruns} underruns";
    }

    private async Task<int> CheckCurrent(bool gpu, Color background)
    {
        GraphicView view = _viewer.CurrentView ?? throw new InvalidOperationException("Nothing shown.");
        IndexedImage img = view.Image;
        string what = $"{view.Category}/{view.Name}{(view.Flip ? " (flipped)" : "")}";

        // Upload: the texture and the RG8 bytes behind it.
        if (_viewer.MainView.ImageRect.Texture is not Texture2D tex || tex.GetWidth() != img.Width || tex.GetHeight() != img.Height)
        {
            Fail($"{what}: texture missing or wrong size");
            return 0;
        }
        Image upload = IndexedTextures.CreateImage(img);
        byte[] data = upload.GetData();
        if (upload.GetFormat() != Image.Format.Rg8 || data.Length != img.Width * img.Height * 2)
        {
            Fail($"{what}: bad RG8 image");
            return 0;
        }
        for (int p = 0; p < img.Pixels.Length; p++)
        {
            if (data[p * 2] != img.Pixels[p] || data[p * 2 + 1] != (img.Opaque[p] != 0 ? 255 : 0))
            {
                Fail($"{what}: RG8 byte mismatch at pixel {p}");
                return 0;
            }
        }
        if (!gpu)
            return 0;

        // Drawn pixels against the CPU conversion.
        await NextFrame();
        await NextFrame();
        Image frame = _viewer.GetViewport().GetTexture().GetImage();
        Rect2 rect = _viewer.MainView.ImageRect.GetGlobalRect();
        int ox = (int)rect.Position.X, oy = (int)rect.Position.Y;
        if (ox < 0 || oy < 0 || ox + img.Width > frame.GetWidth() || oy + img.Height > frame.GetHeight()
            || (int)rect.Size.X != img.Width || (int)rect.Size.Y != img.Height)
        {
            Fail($"{what}: drawn at {rect}, not fully inside the {frame.GetWidth()}x{frame.GetHeight()} viewport at zoom 1");
            return 0;
        }

        GraphicsCatalog catalog = _viewer.Catalog!;
        byte[] rgba = img.ToRgba(catalog.Playpal.GetPalette(_viewer.CurrentPalette), catalog.Colormap.GetMap(_viewer.CurrentColormap));
        int bgR = (int)Math.Round(background.R * 255), bgG = (int)Math.Round(background.G * 255), bgB = (int)Math.Round(background.B * 255);
        int bad = 0;
        string firstBad = "";
        for (int y = 0; y < img.Height; y++)
        {
            for (int x = 0; x < img.Width; x++)
            {
                int sx = view.Flip ? img.Width - 1 - x : x;
                int s = (y * img.Width + sx) * 4;
                (int r, int g, int b) = rgba[s + 3] != 0 ? (rgba[s], rgba[s + 1], rgba[s + 2]) : (bgR, bgG, bgB);
                Color c = frame.GetPixel(ox + x, oy + y);
                if (c.R8 != r || c.G8 != g || c.B8 != b)
                {
                    if (bad++ == 0)
                        firstBad = $"({x},{y}) drew {c.R8},{c.G8},{c.B8}, expected {r},{g},{b}";
                }
            }
        }
        if (bad > 0)
            Fail($"{what}: {bad} pixel(s) differ, first at {firstBad}");
        return img.Width * img.Height;
    }

    private async Task Screenshots(string dir)
    {
        if (!CanCapture)
        {
            Fail("--viewer-screenshots needs a real renderer (run without --headless)");
            return;
        }
        GraphicsCatalog catalog = _viewer.Catalog ?? throw new InvalidOperationException("No WAD loaded.");
        Directory.CreateDirectory(dir);

        async Task Shot(string file, GraphicCategory category, string name, int frame = 0, int slot = 0,
            int light = 255, bool invuln = false, int palette = 0, int zoom = 2)
        {
            _viewer.SelectCategory(category);
            int index = -1;
            for (int i = 0; i < catalog.Count(category); i++)
            {
                if (catalog.GetName(category, i) == name)
                    index = i;
            }
            if (index < 0)
            {
                // The showcase list is DOOM1's; other IWADs lack some entries (Doom II has no BRNBIGC).
                GD.Print($"WAD viewer: screenshot {file} skipped, {category}/{name} is not in this WAD");
                return;
            }
            _viewer.SetLighting(light, invuln, palette);
            _viewer.SetZoom(zoom);
            _viewer.SelectEntry(index, frame, slot);
            await NextFrame();
            await NextFrame();
            string path = Path.Combine(dir, file);
            Error err = _viewer.GetViewport().GetTexture().GetImage().SavePng(path);
            if (err != Error.Ok)
                Fail($"screenshot {path}: {err}");
            else
                GD.Print($"WAD viewer: saved {path}");
        }

        await Shot("titlepic.png", GraphicCategory.Graphics, "TITLEPIC");
        await Shot("stbar.png", GraphicCategory.Graphics, "STBAR", zoom: 3);
        await Shot("m_doom.png", GraphicCategory.Graphics, "M_DOOM", zoom: 4);
        await Shot("stcfn065.png", GraphicCategory.Graphics, "STCFN065", zoom: 8);
        await Shot("startan3.png", GraphicCategory.Textures, "STARTAN3", zoom: 3);
        await Shot("door3.png", GraphicCategory.Textures, "DOOR3", zoom: 4);
        await Shot("brnbigc.png", GraphicCategory.Textures, "BRNBIGC", zoom: 4);
        await Shot("nukage1.png", GraphicCategory.Flats, "NUKAGE1", zoom: 4);
        await Shot("troo_a_rot2.png", GraphicCategory.Sprites, "TROO", 0, 1);
        await Shot("troo_a_rot8.png", GraphicCategory.Sprites, "TROO", 0, 7);
        await Shot("troo_a_light128.png", GraphicCategory.Sprites, "TROO", 0, 0, light: 128, zoom: 4);
        await Shot("troo_a_invuln.png", GraphicCategory.Sprites, "TROO", 0, 0, invuln: true, zoom: 4);
        await Shot("troo_a_red.png", GraphicCategory.Sprites, "TROO", 0, 0, palette: Playpal.STARTREDPALS + 3, zoom: 4);
        await Shot("play_a.png", GraphicCategory.Sprites, "PLAY", 0, 2);
        await Shot("playpal.png", GraphicCategory.Palette, GraphicsCatalog.PlaypalView, zoom: 16);
        await Shot("colormap.png", GraphicCategory.Palette, GraphicsCatalog.ColormapView, zoom: 3);

        async Task LumpShot(string file, string name)
        {
            int index = catalog.Wad.W_CheckNumForName(name);
            if (index < 0)
            {
                Fail($"screenshot: lump {name} not found");
                return;
            }
            _viewer.ShowTab(lumps: true);
            _viewer.ClearLumpFilter();
            _viewer.SetLighting(255, false, 0);
            _viewer.SetZoom(3);
            _viewer.SelectLump(index);
            await NextFrame();
            await NextFrame();
            string path = Path.Combine(dir, file);
            Error err = _viewer.GetViewport().GetTexture().GetImage().SavePng(path);
            if (err != Error.Ok)
                Fail($"screenshot {path}: {err}");
            else
                GD.Print($"WAD viewer: saved {path}");
        }

        await LumpShot("lumps_trooa1.png", "TROOA1");
        await LumpShot("lumps_demo1.png", "DEMO1");
        _viewer.ShowTab(lumps: false);
        _viewer.SetLighting(255, false, 0);
        _viewer.SetZoom(2);
    }

    /// <summary>A background colour that no PLAYPAL colour equals, so a missing pixel can't pass as drawn.</summary>
    private static Color UnusedColor(Playpal playpal)
    {
        var used = new HashSet<int>();
        for (int p = 0; p < playpal.Count; p++)
        {
            for (int i = 0; i < 256; i++)
            {
                (byte r, byte g, byte b) = playpal.GetColor(p, i);
                used.Add((r << 16) | (g << 8) | b);
            }
        }
        for (int g = 1; g < 256; g++)
        {
            int rgb = (255 << 16) | (g << 8) | 254;
            if (!used.Contains(rgb))
                return Color.Color8(255, (byte)g, 254);
        }
        throw new InvalidOperationException("Every candidate colour is in PLAYPAL.");
    }

    private void Fail(string message)
    {
        _failures++;
        if (_failureLog.Count < 50)
        {
            _failureLog.Add(message);
            GD.PrintErr($"WAD viewer check: {message}");
        }
    }

    private SignalAwaiter NextFrame() => CanCapture
        ? ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw)
        : ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
}
