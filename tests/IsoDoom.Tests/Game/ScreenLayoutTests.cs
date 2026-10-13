using System;
using IsoDoom.Game;
using Xunit;

namespace IsoDoom.Tests.Game;

/// <summary>
/// T7.11: vanilla's screen at 4:3 (<see cref="ScreenLayout"/>): the HUD as
/// wide as square's and 1.2 times taller, the full screens whole at a
/// fractional scale, centred; the mouse's mapping back; and
/// <see cref="SharpBilinear"/>: nearest exactly at a whole scale, each
/// texel's core exact and only its edges blended otherwise.
/// </summary>
public class ScreenLayoutTests
{
    public static TheoryData<int, int> Sizes => new() { { 1280, 800 }, { 1920, 1080 }, { 800, 600 }, { 2560, 1080 }, { 1366, 768 }, { 640, 400 } };

    [Theory]
    [InlineData(1280, 800, 4, 0, 646, 1280, 154)]
    [InlineData(1920, 1080, 5, 160, 888, 1600, 192)]
    [InlineData(800, 600, 2, 80, 523, 640, 77)]
    [InlineData(2560, 1080, 5, 480, 888, 1600, 192)]
    public void TheHudBarIsAsWideAsSquaresAndTaller(int w, int h, int k, int x, int y, int bw, int bh)
    {
        ScreenLayout hud = ScreenLayout.ForHud(w, h, 0, true), square = ScreenLayout.ForHud(w, h, 0, false);
        Assert.Equal(k, ScreenLayout.WholeScale(w, h));
        Assert.Equal((x, y, bw, bh), hud.Rect(0, 168, 320, 32));
        Assert.Equal((x, h - 32 * k, bw, 32 * k), square.Rect(0, 168, 320, 32));
        Assert.Equal(k * 1.2f, hud.ScaleY, 4);
        Assert.True(square.Square);
        Assert.False(hud.Square);
    }

    [Theory]
    [InlineData(1280, 800, 106, 0, 1067, 800)]
    [InlineData(1920, 1080, 240, 0, 1440, 1080)]
    [InlineData(800, 600, 0, 0, 800, 600)]
    [InlineData(2560, 1080, 560, 0, 1440, 1080)]
    [InlineData(1280, 1024, 0, 32, 1280, 960)]
    public void TheFullScreensFitWholeAtFourByThree(int w, int h, int x, int y, int sw, int sh)
    {
        Assert.Equal((x, y, sw, sh), ScreenLayout.ForScreen(w, h, 0, true, true).Rect(0, 0, 320, 200));
        // not centred down (the pause graphic over the level): at the top
        Assert.Equal((x, 0, sw, sh), ScreenLayout.ForScreen(w, h, 0, true, false).Rect(0, 0, 320, 200));
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void SquareIsTheWholeScaleAsBefore(int w, int h)
    {
        int k = ScreenLayout.WholeScale(w, h);
        int x = (int)MathF.Floor((w - 320 * k) / 2f), y = (int)MathF.Floor((h - 200 * k) / 2f);
        Assert.Equal((x, y, 320 * k, 200 * k), ScreenLayout.ForScreen(w, h, 0, false, true).Rect(0, 0, 320, 200));
        Assert.Equal((x, 0, 320 * k, 200 * k), ScreenLayout.ForScreen(w, h, 0, false, false).Rect(0, 0, 320, 200));
    }

    [Fact]
    public void AFixedScaleIsTheScaleAcross()
    {
        ScreenLayout hud = ScreenLayout.ForHud(1280, 800, 2, true), screen = ScreenLayout.ForScreen(1280, 800, 3, true, true);
        Assert.Equal((320, 723, 640, 77), hud.Rect(0, 168, 320, 32));
        Assert.Equal(3f, screen.ScaleX);
        Assert.Equal(3.6f, screen.ScaleY, 4);
        Assert.Equal((160, 40, 960, 720), screen.Rect(0, 0, 320, 200));
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void TheMouseMapsBackToThePixelUnderIt(int w, int h)
    {
        foreach (bool aspect in new[] { true, false })
        {
            (int X, int Y, int W, int H) rect = ScreenLayout.ForScreen(w, h, 0, aspect, true).Rect(0, 0, 320, 200);
            ScreenLayout shown = ScreenLayout.FromRect(rect, 320, 200);
            for (int y = 0; y < 200; y++)
            {
                for (int x = 0; x < 320; x++)
                {
                    (float vx, float vy) = shown.ToViewport(x + 0.5f, y + 0.5f);
                    Assert.Equal((x, y), shown.ToScreen(vx, vy));
                    Assert.Equal((x, y), shown.ToScreen(MathF.Floor(vx) + 0.5f, MathF.Floor(vy) + 0.5f)); // the viewport pixel's centre
                }
            }
            // every viewport pixel inside the rectangle maps to a screen pixel
            for (int py = rect.Y; py < rect.Y + rect.H; py++)
            {
                (int sx, int sy) = shown.ToScreen(rect.X + 0.5f, py + 0.5f);
                Assert.InRange(sy, 0, 199);
                Assert.Equal(0, sx);
            }
        }
    }

    private static byte[] Texture(int w, int h, Random random, bool holes)
    {
        byte[] rgba = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            if (holes && random.Next(3) == 0)
                continue; // see-through: 0, 0, 0, 0 (premultiplied)
            rgba[i * 4] = (byte)random.Next(256);
            rgba[i * 4 + 1] = (byte)random.Next(256);
            rgba[i * 4 + 2] = (byte)random.Next(256);
            rgba[i * 4 + 3] = 255;
        }
        return rgba;
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    [InlineData(3, 5)]
    [InlineData(5, 6)]
    public void AtAWholeScaleItIsNearest(int sx, int sy)
    {
        var random = new Random(711);
        int w = 13, h = 7;
        byte[] rgba = Texture(w, h, random, true);
        (int X, int Y, int W, int H) rect = (5, 3, w * sx, h * sy);
        Assert.True(SharpBilinear.IsWhole(rect.W, w) && SharpBilinear.IsWhole(rect.H, h));
        for (int py = rect.Y; py < rect.Y + rect.H; py++)
        {
            for (int px = rect.X; px < rect.X + rect.W; px++)
            {
                int i = (((py - rect.Y) / sy) * w + (px - rect.X) / sx) * 4;
                Assert.Equal(((float)rgba[i], (float)rgba[i + 1], (float)rgba[i + 2], (float)rgba[i + 3]), SharpBilinear.Sample(rgba, w, h, rect, px, py));
            }
        }
    }

    [Theory]
    [InlineData(4, 4.8)]
    [InlineData(3.334, 4)]
    [InlineData(4.5, 5.4)]
    public void AtAFractionalScaleEachTexelsCoreIsExact(double sx, double sy)
    {
        var random = new Random(1211);
        int w = 17, h = 9;
        byte[] rgba = Texture(w, h, random, false);
        (int X, int Y, int W, int H) rect = (2, 1, (int)Math.Round(w * sx), (int)Math.Round(h * sy));
        ScreenLayout layout = ScreenLayout.FromRect(rect, w, h);
        int blended = 0;
        for (int ty = 0; ty < h; ty++)
        {
            for (int tx = 0; tx < w; tx++)
            {
                // the block's centre: the texel exactly
                (float vx, float vy) = layout.ToViewport(tx + 0.5f, ty + 0.5f);
                int i = (ty * w + tx) * 4;
                (float R, float G, float B, float A) c = SharpBilinear.Sample(rgba, w, h, rect, (int)MathF.Floor(vx), (int)MathF.Floor(vy));
                Assert.Equal(rgba[i], c.R, 3);
                Assert.Equal(rgba[i + 1], c.G, 3);
                Assert.Equal(rgba[i + 2], c.B, 3);
            }
        }
        // only the pixels on a block's edge blend, with a texel next to it
        for (int py = rect.Y; py < rect.Y + rect.H; py++)
        {
            for (int px = rect.X; px < rect.X + rect.W; px++)
            {
                (float R, float G, float B, float A) c = SharpBilinear.Sample(rgba, w, h, rect, px, py);
                (int tx, int ty) = layout.ToScreen(px + 0.5f, py + 0.5f);
                int i = (ty * w + tx) * 4;
                if (Math.Abs(c.R - rgba[i]) > 1e-3 || Math.Abs(c.G - rgba[i + 1]) > 1e-3)
                    blended++;
            }
        }
        Assert.InRange(blended, 1, rect.W * rect.H / 2);
    }

    [Fact]
    public void SeeThroughPixelsBlendWithoutAFringe()
    {
        // a white texel beside a see-through one over a white background: premultiplied, the edge stays white
        byte[] rgba = [255, 255, 255, 255, 0, 0, 0, 0];
        (int X, int Y, int W, int H) rect = (0, 0, 9, 1);
        byte[] dst = new byte[9 * 4];
        Array.Fill(dst, (byte)255);
        SharpBilinear.Draw(dst, 9, 1, rgba, 2, 1, rect);
        Assert.All(dst, b => Assert.Equal(255, b));
    }
}
