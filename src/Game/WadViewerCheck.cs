using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Render;
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
/// Transparent pixels must show the background. Prints a summary and quits
/// with exit code 1 on any failure.</item>
/// <item><c>--viewer-screenshots=DIR</c>: saves viewport captures of a few
/// showcase graphics to DIR (keep it out of the repo, or in a gitignored
/// folder: the images are WAD data) and quits.</item>
/// </list>
/// </summary>
public partial class WadViewerCheck : Node
{
    private readonly WadViewer _viewer;
    private int _failures;
    private readonly List<string> _failureLog = new();

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
                await CheckAll();
            if (WadLocator.GetUserArg("--viewer-screenshots") is string dir)
                await Screenshots(dir);
        }
        catch (Exception e)
        {
            Fail($"exception: {e}");
        }
        GD.Print(_failures == 0 ? "WAD viewer check: OK" : $"WAD viewer check: FAILED ({_failures} failure(s))");
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
        var settings = gpu
            ? new[] { (255, false, 0), (100, false, 0), (255, true, Playpal.STARTREDPALS + 2) }
            : new[] { (255, false, 0) };

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

    /// <summary>Checks the current view's upload and, when possible, its drawn pixels. Returns pixels compared.</summary>
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
                Fail($"screenshot: {category}/{name} not found");
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
