using System;
using Godot;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

/// <summary>
/// Shows a whole 320×200 screen of the game states (T7.1,
/// <see cref="ScreenGraphics"/>): the title loop's pages, the intermission
/// and the finale over a black backdrop that hides the level, or the pause
/// graphic over the level (see-through elsewhere). Square pixels at an
/// integer scale (<see cref="HudView.ScaleFor"/>, as the HUD), centred,
/// nearest filtered, through the palette given (vanilla's
/// <c>D_Display</c> sets palette 0 for every state but the level).
/// </summary>
public partial class ScreenView : CanvasLayer
{
    private readonly ColorRect _backdrop = new() { Name = "Backdrop", Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly TextureRect _rect = new()
    {
        Name = "Screen",
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.Scale,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };
    private Image? _image;
    private ImageTexture? _texture;
    private byte[] _rgba = Array.Empty<byte>();

    /// <summary>The screen drawn into (vanilla's whole 320×200 screen).</summary>
    public HudScreen Screen { get; } = new(0, HudScreen.SCREENHEIGHT);

    /// <summary>The scale in use (screen pixels per screen pixel of vanilla's).</summary>
    public int PixelScale { get; private set; } = 1;

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
    /// <paramref name="fixedScale"/> (0: the largest that fits).
    /// </summary>
    public void Show(Playpal playpal, int palette, bool opaque, int fixedScale)
    {
        Visible = true;
        _backdrop.Visible = opaque;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        PixelScale = fixedScale > 0 ? fixedScale : HudView.ScaleFor(size);
        if (_rgba.Length != Screen.Pixels.Length * 4)
            _rgba = new byte[Screen.Pixels.Length * 4];
        Screen.ToRgba(playpal.GetPalette(Math.Clamp(palette, 0, playpal.Count - 1)), _rgba);
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
        _rect.Size = new Vector2(Screen.Width, Screen.Height) * PixelScale;
        _rect.Position = new Vector2(MathF.Floor((size.X - _rect.Size.X) / 2), opaque ? MathF.Floor((size.Y - _rect.Size.Y) / 2) : 0);
    }

    /// <summary>The screen's texture rectangle (checks read it back).</summary>
    public TextureRect Rect => _rect;
}
