using System;
using Godot;

namespace IsoDoom.Game;

/// <summary>
/// Shows the automap's frame (T7.9, <see cref="AmMap.fb"/>) through the
/// level's current palette, nearest filtered: at the HUD's scale (vanilla's
/// pixels; 320 × 168 at 1280 × 800) or one window pixel each (the high
/// detail option), over the level and under the status bar, its see-through
/// pixels (the overlay style's background) showing the level. Opaque, a
/// black backdrop fills the window around it (beside a status bar narrower
/// than the window), as vanilla's screen is black there.
/// </summary>
public partial class AutomapView : CanvasLayer
{
    private readonly ColorRect _backdrop = new() { Name = "Backdrop", Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly TextureRect _rect = new()
    {
        Name = "Frame",
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.Scale,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };
    private Image? _image;
    private ImageTexture? _texture;
    private byte[] _rgba = [];

    public AutomapView()
    {
        Visible = false;
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_backdrop);
        AddChild(_rect);
    }

    /// <summary>The frame's texture rectangle (the level check reads it back).</summary>
    public TextureRect Rect => _rect;

    /// <summary>The viewport pixels per frame pixel shown last.</summary>
    public int PixelScale { get; private set; } = 1;

    /// <summary>
    /// Uploads <paramref name="frame"/> through palette <paramref name="pal"/>
    /// (768 bytes) and shows it at <paramref name="at"/> (viewport pixels),
    /// each pixel a square of <paramref name="scale"/>; with <paramref name="backdrop"/>
    /// (vanilla's opaque look) over black.
    /// </summary>
    public void Show(HudScreen frame, ReadOnlySpan<byte> pal, Vector2 at, int scale, bool backdrop = true)
    {
        Visible = true;
        _backdrop.Visible = backdrop;
        PixelScale = scale;
        if (_rgba.Length != frame.Pixels.Length * 4)
            _rgba = new byte[frame.Pixels.Length * 4];
        frame.ToRgba(pal, _rgba);
        if (_image is null || _texture is null || _image.GetWidth() != frame.Width || _image.GetHeight() != frame.Height)
        {
            _image = Image.CreateFromData(frame.Width, frame.Height, false, Image.Format.Rgba8, _rgba);
            _texture = ImageTexture.CreateFromImage(_image);
            _rect.Texture = _texture;
        }
        else
        {
            _image.SetData(frame.Width, frame.Height, false, Image.Format.Rgba8, _rgba);
            _texture.Update(_image);
        }
        _rect.Position = at;
        _rect.Size = new Vector2(frame.Width, frame.Height) * scale;
    }
}
