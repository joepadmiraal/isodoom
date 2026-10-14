using System;
using Godot;

namespace IsoDoom.Render;

/// <summary>
/// The crosshair mouse pointer (T7.12, not vanilla, SPEC §12): while the mouse
/// aims on a level under the game camera, the system's arrow becomes a small
/// crosshair centred on the point the cursor ground point picks, so the aim
/// reads at a glance. White arms with a black outline (seen on any floor),
/// a gap and a dot at the centre. Drawn at <see cref="BaseSize"/> pixels and
/// scaled whole for tall windows (<see cref="ScaleFor"/>). Presentation only.
/// </summary>
public static class CrosshairPointer
{
    /// <summary>The image's side at scale 1: arms out to <see cref="ArmEnd"/> pixels from the centre, an outline pixel beyond.</summary>
    public const int BaseSize = 2 * ArmEnd + 3;

    /// <summary>The first pixel of each arm from the centre (the gap inside it).</summary>
    public const int ArmStart = 4;

    /// <summary>The last pixel of each arm from the centre.</summary>
    public const int ArmEnd = 10;

    /// <summary>The whole scale for a window <paramref name="height"/> pixels tall: 1 up to 1199 rows, 2 at 1440, 3 at 2160.</summary>
    public static int ScaleFor(int height) => Math.Clamp((int)MathF.Round(height / 800f), 1, 4);

    /// <summary>The pointer's image at <paramref name="scale"/> (RGBA8, transparent around the shape).</summary>
    public static Image Build(int scale)
    {
        int c = BaseSize / 2;
        var white = new bool[BaseSize, BaseSize];
        white[c, c] = true;
        for (int r = ArmStart; r <= ArmEnd; r++)
        {
            white[c + r, c] = white[c - r, c] = true;
            white[c, c + r] = white[c, c - r] = true;
        }
        var image = Image.CreateEmpty(BaseSize * scale, BaseSize * scale, false, Image.Format.Rgba8);
        for (int y = 0; y < BaseSize; y++)
        {
            for (int x = 0; x < BaseSize; x++)
            {
                Color colour = white[x, y] ? Colors.White : Outlined(white, x, y) ? Colors.Black : Colors.Transparent;
                if (colour.A > 0)
                    image.FillRect(new Rect2I(x * scale, y * scale, scale, scale), colour);
            }
        }
        return image;
    }

    /// <summary>The hotspot at <paramref name="scale"/>: the centre pixel's block centre.</summary>
    public static Vector2 Hotspot(int scale) => new Vector2(BaseSize / 2 * scale + scale / 2, BaseSize / 2 * scale + scale / 2);

    private static bool Outlined(bool[,] white, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < BaseSize && ny < BaseSize && white[nx, ny])
                    return true;
            }
        }
        return false;
    }

    /// <summary>The texture the pointer shows, kept to free it before the renderer goes (<see cref="Apply"/>).</summary>
    private static ImageTexture? _texture;

    /// <summary>
    /// Shows the crosshair at <paramref name="scale"/> as the pointer, or with
    /// 0 gives the system's arrow back. Call it when either changes, and with
    /// 0 when the scene leaves the tree: Input and the display server hold the
    /// texture until it is freed here, so one left at quit outlives the
    /// RenderingServer and leaks its RID.
    /// </summary>
    public static void Apply(int scale)
    {
        ImageTexture? old = _texture;
        _texture = scale > 0 ? ImageTexture.CreateFromImage(Build(scale)) : null;
        if (_texture is null)
            Input.SetCustomMouseCursor(null);
        else
            Input.SetCustomMouseCursor(_texture, Input.CursorShape.Arrow, Hotspot(scale));
        old?.Dispose();
    }
}
