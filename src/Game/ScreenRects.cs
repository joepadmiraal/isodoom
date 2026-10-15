using Godot;
using IsoDoom.Render;

namespace IsoDoom.Game;

/// <summary>
/// T7.11: the texture rectangles that show vanilla's screen (the HUD, the
/// full screens, the automap's frame) placed at a layout's whole-pixel
/// rectangle (<see cref="ScreenLayout.Rect"/>): nearest filtered at a whole
/// scale, else through <see cref="SharpBilinear"/>'s shader, linearly
/// filtered, with premultiplied alpha.
/// </summary>
public static class ScreenRects
{
    private static Shader? _shader;

    /// <summary>A texture rectangle stretched over its size, nearest filtered, ignoring the mouse.</summary>
    public static TextureRect New(string name) => new()
    {
        Name = name,
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.Scale,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    /// <summary>
    /// Places <paramref name="node"/>, showing a <paramref name="width"/> ×
    /// <paramref name="height"/> texture, at viewport rectangle <paramref name="rect"/>.
    /// </summary>
    public static void Place(TextureRect node, (int X, int Y, int W, int H) rect, int width, int height)
    {
        node.Position = new Vector2(rect.X, rect.Y);
        node.Size = new Vector2(rect.W, rect.H);
        if (SharpBilinear.IsWhole(rect.W, width) && SharpBilinear.IsWhole(rect.H, height))
        {
            node.Material = null;
            node.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
            return;
        }
        if (node.Material is not ShaderMaterial material)
        {
            _shader ??= new Shader { Code = SharpBilinear.ShaderCode };
            node.Material = material = new ShaderMaterial { Shader = _shader };
        }
        material.SetParam("scale", new Vector2(rect.W / (float)width, rect.H / (float)height)); // (T8.3: a StringName made once)
        node.TextureFilter = CanvasItem.TextureFilterEnum.Linear;
    }

    /// <summary>The rectangle <paramref name="node"/> is placed at.</summary>
    public static (int X, int Y, int W, int H) RectOf(TextureRect node) =>
        ((int)node.Position.X, (int)node.Position.Y, (int)node.Size.X, (int)node.Size.Y);
}
