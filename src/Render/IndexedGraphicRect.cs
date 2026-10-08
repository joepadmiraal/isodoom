using Godot;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Render;

/// <summary>
/// Shows one <see cref="IndexedImage"/> through the palette shader at an
/// integer zoom, over a checkerboard (or a solid colour) so transparent
/// pixels are visible. Mirrored sprite rotations are drawn with
/// <see cref="TextureRect.FlipH"/>, i.e. flipped UVs, as the renderer will.
/// </summary>
public partial class IndexedGraphicRect : Control
{
    private static ImageTexture? _checker;

    private readonly TextureRect _background;
    private readonly ColorRect _solid;
    private readonly TextureRect _image;
    private int _zoom = 1;
    private Vector2I _size;

    public IndexedGraphicRect()
    {
        SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        SizeFlagsVertical = SizeFlags.ShrinkBegin;
        TextureFilter = TextureFilterEnum.Nearest;

        _background = new TextureRect
        {
            Texture = _checker ??= CreateChecker(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Tile,
        };
        _solid = new ColorRect { Visible = false };
        _image = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = TextureFilterEnum.Nearest,
        };
        foreach (Control c in new Control[] { _background, _solid, _image })
        {
            c.SetAnchorsPreset(LayoutPreset.FullRect);
            c.MouseFilter = MouseFilterEnum.Ignore;
            AddChild(c);
        }
    }

    /// <summary>The rectangle the graphic itself covers (for checks).</summary>
    public TextureRect ImageRect => _image;

    /// <summary>Shows <paramref name="image"/> (uploaded as an RG8 texture) with <paramref name="material"/>.</summary>
    public void SetGraphic(IndexedImage image, bool flip, ShaderMaterial material)
    {
        _image.Texture = IndexedTextures.CreateTexture(image);
        _image.Material = material;
        _image.FlipH = flip;
        _size = new Vector2I(image.Width, image.Height);
        UpdateSize();
    }

    public int Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Mathf.Max(1, value);
            UpdateSize();
        }
    }

    /// <summary>Null shows the checkerboard; a colour shows a solid background.</summary>
    public Color? SolidBackground
    {
        get => _solid.Visible ? _solid.Color : null;
        set
        {
            _solid.Visible = value.HasValue;
            _background.Visible = !value.HasValue;
            if (value.HasValue)
                _solid.Color = value.Value;
        }
    }

    private void UpdateSize()
    {
        CustomMinimumSize = new Vector2(_size.X * _zoom, _size.Y * _zoom);
        Size = CustomMinimumSize;
    }

    private static ImageTexture CreateChecker()
    {
        var img = Image.CreateEmpty(16, 16, false, Image.Format.Rgb8);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
                img.SetPixel(x, y, ((x / 8) + (y / 8)) % 2 == 0 ? new Color(0.24f, 0.24f, 0.28f) : new Color(0.33f, 0.33f, 0.38f));
        }
        return ImageTexture.CreateFromImage(img);
    }
}
