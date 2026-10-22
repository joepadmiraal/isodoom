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
    // T11.9b (the user's decision): the window dimmed behind our own text pages
    private readonly ColorRect _dim = new() { Name = StringNames.Get("Dim"), Color = new Color(0, 0, 0, DimAlpha), MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
    private readonly TextureRect _rect = ScreenRects.New("Screen");

    /// <summary>
    /// T11.9b: the opacity of the black over the whole window behind a dimmed
    /// screen (<see cref="Show(ReadOnlySpan{byte}, bool, int, bool, bool?, bool)"/>'s
    /// <c>dim</c>: our own text pages, SPEC §12 "A dim backdrop behind our text pages").
    /// </summary>
    public const float DimAlpha = 0.6f;

    /// <summary>T11.9b: whether the last <c>Show</c> dimmed what is behind the screen.</summary>
    public bool Dimmed => _dim.Visible;
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
        _dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_dim);
        AddChild(_rect);
    }

    /// <summary>
    /// Uploads <see cref="Screen"/> through <paramref name="playpal"/>'s
    /// palette <paramref name="palette"/> and shows it, with the black
    /// backdrop when <paramref name="opaque"/> (a full screen) and at
    /// <paramref name="fixedScale"/> (0: the largest that fits), vanilla's
    /// pixels 1.2 times taller than wide with <paramref name="aspect43"/>, centred
    /// (by default only <paramref name="opaque"/>, else at the top, as the
    /// pause graphic over the level) when <paramref name="centre"/>; with
    /// <paramref name="dim"/> (T11.9b) over the window dimmed by <see cref="DimAlpha"/>.
    /// </summary>
    public void Show(Playpal playpal, int palette, bool opaque, int fixedScale, bool aspect43, bool? centre = null, bool dim = false) =>
        Show(playpal.GetPalette(Math.Clamp(palette, 0, playpal.Count - 1)), opaque, fixedScale, aspect43, centre, dim);

    /// <summary>As <see cref="Show(Playpal, int, bool, int, bool, bool?, bool)"/>, through palette <paramref name="pal"/> (768 bytes; T6.13v: a blended flash).</summary>
    public void Show(ReadOnlySpan<byte> pal, bool opaque, int fixedScale, bool aspect43, bool? centre = null, bool dim = false)
    {
        Visible = true;
        _backdrop.Visible = opaque;
        _dim.Visible = dim && !opaque;
        if (_dim.Visible && Srgb.LinearCanvas && _dim.Material is null)
            _dim.Material = LinearDim();
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
    /// nearest at a whole scale); T11.9b: a dimmed screen over the image
    /// dimmed (<see cref="DimRgb"/>). Nothing when hidden.
    /// </summary>
    public void ComposeInto(Image image)
    {
        if (!Visible || _image is null)
            return;
        if (_backdrop.Visible)
            image.Fill(Colors.Black);
        int w = image.GetWidth(), h = image.GetHeight();
        byte[] data = image.GetData();
        if (_dim.Visible)
        {
            for (int i = 0; i < data.Length; i += 4)
            {
                data[i] = DimRgb(data[i]);
                data[i + 1] = DimRgb(data[i + 1]);
                data[i + 2] = DimRgb(data[i + 2]);
            }
        }
        SharpBilinear.Draw(data, w, h, _rgba, Screen.Width, Screen.Height, ScreenRects.RectOf(_rect));
        image.SetData(w, h, false, Image.Format.Rgba8, data);
    }

    /// <summary>
    /// T8.4d: the dim on a linear canvas (HDR 2D, <see cref="Srgb.LinearCanvas"/>),
    /// where blending black over it would darken linear colour: the screen
    /// behind darkened in sRGB values, as <see cref="DimRgb"/> without it.
    /// </summary>
    private static ShaderMaterial LinearDim()
    {
        _linearDimShader ??= new Shader
        {
            Code = $@"
shader_type canvas_item;
render_mode unshaded, blend_disabled;

uniform sampler2D screen : hint_screen_texture, filter_nearest, repeat_disable;

void fragment() {{
    vec3 c = texture(screen, SCREEN_UV).rgb;
    c = mix(c * 12.92, 1.055 * pow(max(c, vec3(0.0)), vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), c));
    c *= {(1 - DimAlpha).ToString("R", System.Globalization.CultureInfo.InvariantCulture)};
    COLOR = vec4(mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c)), 1.0);
}}
",
        };
        return new ShaderMaterial { Shader = _linearDimShader };
    }

    private static Shader? _linearDimShader;

    /// <summary>
    /// T11.9b: a colour channel (0–255, the framebuffer's sRGB value) under
    /// the dim: black at <see cref="DimAlpha"/> blended over it as the 2D
    /// renderer blends (in sRGB values), rounded.
    /// </summary>
    public static byte DimRgb(byte value) => (byte)MathF.Round(value * (1 - DimAlpha));

    /// <summary>The screen's texture rectangle (checks read it back).</summary>
    public TextureRect Rect => _rect;

    /// <summary>The point of vanilla's 320×200 screen under viewport point <paramref name="at"/> (where it was shown last).</summary>
    public Vector2I ToScreen(Vector2 at)
    {
        (int x, int y) = Layout.ToScreen(at.X, at.Y);
        return new Vector2I(x, y);
    }
}
