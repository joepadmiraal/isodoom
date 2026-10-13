using System;
using Godot;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

/// <summary>The level scene's HUD (T6.11, <c>--level-hud</c>): vanilla's status bar, the minimal fullscreen HUD, or none (the message line shows with all three).</summary>
public enum HudMode { Bar, Full, Off }

/// <summary>
/// Shows the HUD (T6.11, SPEC §7.6): the status bar (<see cref="StStuff"/>)
/// scaled at the bottom of the screen, or the minimal fullscreen HUD, and the
/// message line (<see cref="HuStuff"/>) at the top left, both from vanilla's
/// 320×200 screen (palette indices) through the current PLAYPAL palette,
/// as vanilla's <c>I_SetPalette</c> tints them too, at a whole scale across
/// (<see cref="ScaleFor"/>) and, T7.11, 1.2 times that down with
/// <see cref="Aspect43"/> (vanilla's pixels as a 4:3 CRT showed them,
/// <see cref="ScreenLayout.ForHud"/>; sharp bilinear, <see cref="ScreenRects"/>),
/// or square.
/// </summary>
public partial class HudView : CanvasLayer
{
    private readonly TextureRect _bar = ScreenRects.New("StatusBar");
    private readonly TextureRect _message = ScreenRects.New("MessageLine");
    private readonly TextureRect _title = ScreenRects.New("MapTitle");
    private Image? _barImage, _messageImage, _titleImage;
    private ImageTexture? _barTexture, _messageTexture, _titleTexture;
    private byte[] _barRgba = [], _messageRgba = [], _titleRgba = [];

    /// <summary>What shows (<c>--level-hud</c>; = and - keys).</summary>
    public HudMode Mode { get; set; } = HudMode.Bar;

    /// <summary>The scale (<c>--level-hud-scale</c>), or 0 for <see cref="ScaleFor"/>'s.</summary>
    public int FixedScale { get; set; }

    /// <summary>T7.11: vanilla's pixels 1.2 times taller than wide (<c>hud/aspect</c> <c>4:3</c>), or square.</summary>
    public bool Aspect43 { get; set; } = true;

    /// <summary>The scale across in use (viewport pixels per HUD pixel; down, <see cref="Layout"/>'s <see cref="ScreenLayout.ScaleY"/>).</summary>
    public int PixelScale => (int)Layout.ScaleX;

    /// <summary>T7.11: the layout in use (<see cref="ScreenLayout.ForHud"/>: vanilla's row 200 at the viewport's bottom).</summary>
    public ScreenLayout Layout { get; private set; } = new(1, 1, 0, 0);

    /// <summary>
    /// T7.9: the automap shows: the status bar shows whatever the
    /// <see cref="Mode"/> (st_stuff.c's <c>st_statusbaron = !fullscreen || automapactive</c>),
    /// and the map's title over its bottom left (hu_stuff.c's <c>w_title</c>).
    /// </summary>
    public bool AutomapShown { get; set; }

    /// <summary>The viewport pixels the status bar covers at the bottom (0 unless <see cref="HudMode.Bar"/> shows).</summary>
    public float BottomInset => Visible && (Mode == HudMode.Bar || AutomapShown) ? BarHeight : 0;

    /// <summary>The status bar's height in viewport pixels (154 at 1280×800 with <see cref="Aspect43"/>, 128 square).</summary>
    public int BarHeight => Layout.Rect(0, StStuff.ST_Y, HudScreen.SCREENWIDTH, StStuff.ST_HEIGHT).H;

    /// <summary>The viewport pixels the message line's rows take at the top (where the debug overlay starts below).</summary>
    public float TopInset => Visible ? HuStuff.ScreenRows * Layout.ScaleY / 2 + 8 : 0;

    /// <summary>
    /// The largest whole scale at which vanilla's 320×200 screen fits the
    /// viewport (at least 1): 4 at 1280×800 (the bar 1280×128, the font's
    /// capitals 28 pixels tall), 5 at 1920×1080 (<see cref="ScreenLayout.WholeScale"/>).
    /// </summary>
    public static int ScaleFor(Vector2 viewport) => ScreenLayout.WholeScale(viewport.X, viewport.Y);

    /// <summary>T7.11: the HUD's layout for a viewport of <paramref name="size"/>.</summary>
    public ScreenLayout LayoutFor(Vector2 size) => ScreenLayout.ForHud(size.X, size.Y, FixedScale, Aspect43);

    /// <summary>T7.11: the message line's layout for <paramref name="layout"/> (vanilla's row 0 at the viewport's top, never left of it).</summary>
    public static ScreenLayout MessageLayout(ScreenLayout layout) => layout with { OriginX = Math.Max(layout.OriginX, 0), OriginY = 0 };

    public HudView()
    {
        Layer = 1; // over the debug overlay
        AddChild(_bar);
        AddChild(_message);
        AddChild(_title);
    }

    /// <summary>
    /// Uploads <paramref name="st"/>'s bar (or fullscreen HUD) and
    /// <paramref name="hu"/>'s message line, as last drawn, through
    /// <paramref name="playpal"/>'s palette <paramref name="palette"/>, and
    /// lays them out for the viewport.
    /// </summary>
    public void Show(StStuff? st, HuStuff? hu, Playpal playpal, int palette) =>
        Show(st, hu, playpal.GetPalette(Math.Clamp(palette, 0, playpal.Count - 1)));

    /// <summary>As <see cref="Show(StStuff?, HuStuff?, Playpal, int)"/>, through palette <paramref name="pal"/> (768 bytes; T6.13v: a blended flash).</summary>
    public void Show(StStuff? st, HuStuff? hu, ReadOnlySpan<byte> pal)
    {
        Vector2 size = GetViewport().GetVisibleRect().Size;
        ScreenLayout layout = Layout = LayoutFor(size);
        ScreenLayout top = MessageLayout(layout);

        HudScreen? strip = st is null ? null : AutomapShown || Mode == HudMode.Bar ? st.Screen : Mode == HudMode.Full ? st.FullScreen : null;
        _bar.Visible = strip is not null;
        if (strip is not null)
        {
            Upload(strip, pal, ref _barImage, ref _barTexture, ref _barRgba, _bar);
            ScreenRects.Place(_bar, layout.Rect(0, strip.Top, strip.Width, strip.Height), strip.Width, strip.Height);
        }
        _message.Visible = hu is { message_on: true };
        if (hu is { message_on: true })
        {
            Upload(hu.Screen, pal, ref _messageImage, ref _messageTexture, ref _messageRgba, _message);
            ScreenRects.Place(_message, top.Rect(0, hu.Screen.Top, hu.Screen.Width, hu.Screen.Height), hu.Screen.Width, hu.Screen.Height);
        }
        _title.Visible = AutomapShown && hu is not null;
        if (AutomapShown && hu is not null)
        {
            // T7.9: vanilla's screen rows from the title's down, laid out as the bar's (row 168 at the bar's top)
            HudScreen title = hu.TitleScreen;
            Upload(title, pal, ref _titleImage, ref _titleTexture, ref _titleRgba, _title);
            ScreenRects.Place(_title, (top with { OriginY = layout.OriginY }).Rect(0, title.Top, title.Width, title.Height), title.Width, title.Height);
        }
    }

    /// <summary>T7.9: the map title's texture rectangle.</summary>
    public TextureRect Title => _title;

    private static void Upload(HudScreen screen, ReadOnlySpan<byte> pal, ref Image? image, ref ImageTexture? texture, ref byte[] rgba, TextureRect rect)
    {
        if (rgba.Length != screen.Pixels.Length * 4)
            rgba = new byte[screen.Pixels.Length * 4];
        screen.ToRgba(pal, rgba);
        if (image is null || texture is null)
        {
            image = Image.CreateFromData(screen.Width, screen.Height, false, Image.Format.Rgba8, rgba);
            texture = ImageTexture.CreateFromImage(image);
            rect.Texture = texture;
            return;
        }
        image.SetData(screen.Width, screen.Height, false, Image.Format.Rgba8, rgba);
        texture.Update(image);
    }

    /// <summary>The status bar's (or fullscreen HUD's) texture rectangle (the level check reads it back).</summary>
    public TextureRect Bar => _bar;

    /// <summary>The message line's texture rectangle.</summary>
    public TextureRect MessageLine => _message;
}
