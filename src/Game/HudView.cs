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
/// as vanilla's <c>I_SetPalette</c> tints them too, with square pixels at
/// an integer scale (<see cref="ScaleFor"/>), nearest filtered.
/// </summary>
public partial class HudView : CanvasLayer
{
    private readonly TextureRect _bar = NewRect("StatusBar");
    private readonly TextureRect _message = NewRect("MessageLine");
    private Image? _barImage, _messageImage;
    private ImageTexture? _barTexture, _messageTexture;
    private byte[] _barRgba = Array.Empty<byte>(), _messageRgba = Array.Empty<byte>();

    /// <summary>What shows (<c>--level-hud</c>; = and - keys).</summary>
    public HudMode Mode { get; set; } = HudMode.Bar;

    /// <summary>The scale (<c>--level-hud-scale</c>), or 0 for <see cref="ScaleFor"/>'s.</summary>
    public int FixedScale { get; set; }

    /// <summary>The scale in use (screen pixels per HUD pixel).</summary>
    public int Scale { get; private set; } = 1;

    /// <summary>The viewport pixels the status bar covers at the bottom (0 unless <see cref="HudMode.Bar"/> shows).</summary>
    public float BottomInset => Visible && Mode == HudMode.Bar ? StStuff.ST_HEIGHT * Scale : 0;

    /// <summary>The viewport pixels the message line's rows take at the top (where the debug overlay starts below).</summary>
    public float TopInset => Visible ? HuStuff.ScreenRows * Scale / 2 + 8 : 0;

    /// <summary>
    /// The largest whole scale at which vanilla's 320×200 screen fits the
    /// viewport (at least 1): 4 at 1280×800 (the bar 1280×128, the font's
    /// capitals 28 pixels tall), 5 at 1920×1080.
    /// </summary>
    public static int ScaleFor(Vector2 viewport) =>
        Math.Max(1, (int)Math.Min(viewport.X / HudScreen.SCREENWIDTH, viewport.Y / HudScreen.SCREENHEIGHT));

    public HudView()
    {
        Layer = 1; // over the debug overlay
        AddChild(_bar);
        AddChild(_message);
    }

    private static TextureRect NewRect(string name) => new()
    {
        Name = name,
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.Scale,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    /// <summary>
    /// Uploads <paramref name="st"/>'s bar (or fullscreen HUD) and
    /// <paramref name="hu"/>'s message line, as last drawn, through
    /// <paramref name="playpal"/>'s palette <paramref name="palette"/>, and
    /// lays them out for the viewport.
    /// </summary>
    public void Show(StStuff? st, HuStuff? hu, Playpal playpal, int palette)
    {
        Vector2 size = GetViewport().GetVisibleRect().Size;
        Scale = FixedScale > 0 ? FixedScale : ScaleFor(size);
        ReadOnlySpan<byte> pal = playpal.GetPalette(Math.Clamp(palette, 0, playpal.Count - 1));
        float left = MathF.Floor((size.X - HudScreen.SCREENWIDTH * Scale) / 2);

        HudScreen? strip = st is null || Mode == HudMode.Off ? null : Mode == HudMode.Bar ? st.Screen : st.FullScreen;
        _bar.Visible = strip is not null;
        if (strip is not null)
        {
            Upload(strip, pal, ref _barImage, ref _barTexture, ref _barRgba, _bar);
            _bar.Position = new Vector2(left, size.Y - StStuff.ST_HEIGHT * Scale);
            _bar.Size = new Vector2(HudScreen.SCREENWIDTH, StStuff.ST_HEIGHT) * Scale;
        }
        _message.Visible = hu is { message_on: true };
        if (hu is { message_on: true })
        {
            Upload(hu.Screen, pal, ref _messageImage, ref _messageTexture, ref _messageRgba, _message);
            _message.Position = new Vector2(Math.Max(left, 0), hu.Screen.Top * Scale);
            _message.Size = new Vector2(HudScreen.SCREENWIDTH, hu.Screen.Height) * Scale;
        }
    }

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
