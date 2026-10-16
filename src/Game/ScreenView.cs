using System;
using Godot;
using IsoDoom.Render;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

/// <summary>
/// Shows a whole 320×200 screen of the game states (T7.1,
/// <see cref="ScreenGraphics"/>): the title loop's pages, the intermission
/// and the finale over a black backdrop that hides the level, or the pause
/// graphic over the level (see-through elsewhere), centred, at
/// <see cref="ScreenLayout.ForScreen"/>'s layout (T7.11: with 4:3, the
/// whole screen at 320×240's proportions, sharp bilinear; square, a whole
/// scale as the HUD's), through the palette given (vanilla's
/// <c>D_Display</c> sets palette 0 for every state but the level).
/// </summary>
public partial class ScreenView : CanvasLayer
{
    private readonly ColorRect _backdrop = new() { Name = StringNames.Get("Backdrop"), Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly TextureRect _rect = ScreenRects.New("Screen");
    private Image? _image;
    private ImageTexture? _texture;
    private byte[] _rgba = [];

    /// <summary>The screen drawn into (vanilla's whole 320×200 screen).</summary>
    public HudScreen Screen { get; } = new(0, HudScreen.SCREENHEIGHT);

    /// <summary>T7.11: the layout shown last (the screen's rectangle, <see cref="ScreenLayout.FromRect"/>).</summary>
    public ScreenLayout Layout { get; private set; } = new(1, 1, 0, 0);

    public ScreenView()
    {
        Visible = false;
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_backdrop);
        AddChild(_rect);
    }

    /// <summary>
    /// Uploads <see cref="Screen"/> through <paramref name="playpal"/>'s
    /// palette <paramref name="palette"/> and shows it, with the black
    /// backdrop when <paramref name="opaque"/> (a full screen) and at
    /// <paramref name="fixedScale"/> (0: the largest that fits), vanilla's
    /// pixels 1.2 times taller than wide with <paramref name="aspect43"/>, centred
    /// (by default only <paramref name="opaque"/>, else at the top, as the
    /// pause graphic over the level) when <paramref name="centre"/>.
    /// </summary>
    public void Show(Playpal playpal, int palette, bool opaque, int fixedScale, bool aspect43, bool? centre = null) =>
        Show(playpal.GetPalette(Math.Clamp(palette, 0, playpal.Count - 1)), opaque, fixedScale, aspect43, centre);

    /// <summary>As <see cref="Show(Playpal, int, bool, int, bool, bool?)"/>, through palette <paramref name="pal"/> (768 bytes; T6.13v: a blended flash).</summary>
    public void Show(ReadOnlySpan<byte> pal, bool opaque, int fixedScale, bool aspect43, bool? centre = null)
    {
        Visible = true;
        _backdrop.Visible = opaque;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        if (_rgba.Length != Screen.Pixels.Length * 4)
            _rgba = new byte[Screen.Pixels.Length * 4];
        Screen.ToRgba(pal, _rgba);
        if (_image is null || _texture is null)
        {
            _image = Image.CreateFromData(Screen.Width, Screen.Height, false, Image.Format.Rgba8, _rgba);
            _texture = ImageTexture.CreateFromImage(_image);
            _rect.Texture = _texture;
        }
        else
        {
            _image.SetData(Screen.Width, Screen.Height, false, Image.Format.Rgba8, _rgba);
            _texture.Update(_image);
        }
        (int X, int Y, int W, int H) rect = ScreenLayout.ForScreen(size.X, size.Y, fixedScale, aspect43, centre ?? opaque).Rect(0, 0, Screen.Width, Screen.Height);
        ScreenRects.Place(_rect, rect, Screen.Width, Screen.Height);
        Layout = ScreenLayout.FromRect(rect, Screen.Width, Screen.Height);
    }

    /// <summary>
    /// T7.1a: draws what this view showed last into <paramref name="image"/>
    /// (RGBA8, the viewport's size) as the renderer does: the black backdrop,
    /// then the screen at its rectangle (T7.11: <see cref="SharpBilinear"/>,
    /// nearest at a whole scale). Nothing when hidden.
    /// </summary>
    public void ComposeInto(Image image)
    {
        if (!Visible || _image is null)
            return;
        if (_backdrop.Visible)
            image.Fill(Colors.Black);
        int w = image.GetWidth(), h = image.GetHeight();
        byte[] data = image.GetData();
        SharpBilinear.Draw(data, w, h, _rgba, Screen.Width, Screen.Height, ScreenRects.RectOf(_rect));
        image.SetData(w, h, false, Image.Format.Rgba8, data);
    }

    /// <summary>The screen's texture rectangle (checks read it back).</summary>
    public TextureRect Rect => _rect;

    /// <summary>The point of vanilla's 320×200 screen under viewport point <paramref name="at"/> (where it was shown last).</summary>
    public Vector2I ToScreen(Vector2 at)
    {
        (int x, int y) = Layout.ToScreen(at.X, at.Y);
        return new Vector2I(x, y);
    }
}
