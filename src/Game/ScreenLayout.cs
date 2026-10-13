using System;

namespace IsoDoom.Game;

/// <summary>
/// T7.11: where vanilla's 320×200 screen shows in the viewport. A 4:3 CRT
/// showed mode 13h's 320×200 (16:10) with pixels 1.2 times taller than wide
/// (<see cref="PixelAspect"/>), as id's artists drew for; with
/// <c>4:3</c> (<c>hud/aspect</c>, the default) the views show it so,
/// <see cref="ScaleY"/> = 1.2 <see cref="ScaleX"/>, and with <c>square</c>
/// at square pixels (a whole scale, as before T7.11). <see cref="OriginX"/>,
/// <see cref="OriginY"/> is the viewport point of vanilla's screen point
/// 0, 0. Rectangles round their edges to whole viewport pixels
/// (<see cref="Rect"/>); a view placed at one maps through
/// <see cref="FromRect"/>, so its drawing and its mouse agree exactly.
/// No Godot types: the tests link it.
/// </summary>
public readonly record struct ScreenLayout(float ScaleX, float ScaleY, float OriginX, float OriginY)
{
    /// <summary>A 4:3 CRT's pixel at 320×200: (4/3) / (320/200) = 240/200 times taller than wide.</summary>
    public const float PixelAspect = 1.2f;

    /// <summary>
    /// The largest whole scale at which vanilla's 320×200 screen fits a
    /// <paramref name="width"/> × <paramref name="height"/> viewport (at
    /// least 1): 4 at 1280×800, 5 at 1920×1080 (<c>square</c>'s, and the
    /// HUD's scale across with <c>4:3</c>).
    /// </summary>
    public static int WholeScale(float width, float height) =>
        Math.Max(1, (int)Math.Min(width / HudScreen.SCREENWIDTH, height / HudScreen.SCREENHEIGHT));

    /// <summary>
    /// The HUD's (the status bar, the fullscreen HUD, the automap's title;
    /// the message line takes it with <see cref="OriginY"/> 0): it only has
    /// to fit across, so the whole scale k (<see cref="WholeScale"/>, or
    /// <paramref name="fixedScale"/> when above 0) across, centred, and k
    /// (<c>square</c>) or 1.2 k (<paramref name="aspect43"/>) down, vanilla's
    /// row 200 at the viewport's bottom: at 1280×800 the bar is 1280×154 (square: 1280×128).
    /// </summary>
    public static ScreenLayout ForHud(float width, float height, int fixedScale, bool aspect43)
    {
        int k = fixedScale > 0 ? fixedScale : WholeScale(width, height);
        float sy = aspect43 ? k * PixelAspect : k;
        return new ScreenLayout(k, sy, Floor((width - HudScreen.SCREENWIDTH * k) / 2), height - HudScreen.SCREENHEIGHT * sy);
    }

    /// <summary>
    /// The full screens' (the title loop, the intermission, the finale, the
    /// pause graphic, the menus): the whole screen fits, centred across and,
    /// with <paramref name="centre"/>, down (else at the top). <c>square</c>:
    /// <see cref="WholeScale"/>'s k both ways; <paramref name="aspect43"/>:
    /// k = min(W / 320, H / 240), not rounded (sharp bilinear keeps it crisp,
    /// <see cref="SharpBilinear"/>), at least 1, and 1.2 k down: 1067×800 at
    /// 1280×800, 1440×1080 at 1920×1080. A <paramref name="fixedScale"/>
    /// above 0 is k (clipped where it does not fit).
    /// </summary>
    public static ScreenLayout ForScreen(float width, float height, int fixedScale, bool aspect43, bool centre)
    {
        float k = fixedScale > 0 ? fixedScale
            : aspect43 ? Math.Max(1f, Math.Min(width / HudScreen.SCREENWIDTH, height / (HudScreen.SCREENHEIGHT * PixelAspect)))
            : WholeScale(width, height);
        float sy = aspect43 ? k * PixelAspect : k;
        return new ScreenLayout(k, sy, Floor((width - HudScreen.SCREENWIDTH * k) / 2),
            centre ? Floor((height - HudScreen.SCREENHEIGHT * sy) / 2) : 0);
    }

    /// <summary>The layout of a view placed at viewport rectangle <paramref name="rect"/> showing a <paramref name="width"/> × <paramref name="height"/> strip whose first row is screen row <paramref name="top"/>.</summary>
    public static ScreenLayout FromRect((int X, int Y, int W, int H) rect, int width, int height, int top = 0)
    {
        float sx = rect.W / (float)width, sy = rect.H / (float)height;
        return new ScreenLayout(sx, sy, rect.X, rect.Y - top * sy);
    }

    /// <summary>
    /// The viewport rectangle (whole pixels: each edge rounded) of the screen
    /// rectangle <paramref name="x"/>, <paramref name="y"/>,
    /// <paramref name="width"/> × <paramref name="height"/>.
    /// </summary>
    public (int X, int Y, int W, int H) Rect(int x, int y, int width, int height)
    {
        int x0 = Round(OriginX + x * ScaleX), x1 = Round(OriginX + (x + width) * ScaleX);
        int y0 = Round(OriginY + y * ScaleY), y1 = Round(OriginY + (y + height) * ScaleY);
        return (x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>The viewport point of screen point <paramref name="x"/>, <paramref name="y"/> (fractions inside a pixel: 0.5 is its centre).</summary>
    public (float X, float Y) ToViewport(float x, float y) => (OriginX + x * ScaleX, OriginY + y * ScaleY);

    /// <summary>The screen pixel under viewport point <paramref name="x"/>, <paramref name="y"/>.</summary>
    public (int X, int Y) ToScreen(float x, float y) =>
        ((int)MathF.Floor((x - OriginX) / ScaleX), (int)MathF.Floor((y - OriginY) / ScaleY));

    /// <summary>The viewport size of <paramref name="width"/> × <paramref name="height"/> screen pixels.</summary>
    public (float W, float H) Size(int width, int height) => (width * ScaleX, height * ScaleY);

    /// <summary>Whether vanilla's pixels show square (<c>square</c>; never with <c>4:3</c>).</summary>
    public bool Square => ScaleX == ScaleY;

    private static int Round(float v) => (int)MathF.Floor(v + 0.5f);

    // Rounded down, but for a float's error just under a whole number (1.2's: 200 × 4.8 is 960.00006).
    private static float Floor(float v) => MathF.Floor(v + 1e-3f);
}
