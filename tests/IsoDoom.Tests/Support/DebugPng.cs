using System;
using System.IO;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Support;

/// <summary>
/// Debug PNG export of index images for visual checks. Files go to
/// <c>TestResults/graphics/</c> under the repo root (gitignored: they are
/// WAD-derived and must never be committed).
/// </summary>
public static class DebugPng
{
    /// <summary>The export folder, created on demand; skips the test when the repo root is unknown.</summary>
    public static string RequireOutputDir()
    {
        Assert.SkipWhen(TestWads.RepoRoot is null, "Repo root not found; nowhere to write the debug PNGs.");
        string dir = Path.Combine(TestWads.RepoRoot!, "TestResults", "graphics");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Writes <paramref name="img"/> scaled by <paramref name="scale"/> and
    /// repeated <paramref name="tilesX"/> × <paramref name="tilesY"/> times (to
    /// show tiling seams). With <paramref name="checker"/>, transparent pixels
    /// become a magenta checkerboard so holes are easy to see; otherwise they
    /// stay transparent.
    /// </summary>
    public static void Write(string dir, string file, IndexedImage img, ReadOnlySpan<byte> palette,
        int scale = 4, bool checker = false, int tilesX = 1, int tilesY = 1)
    {
        byte[] one = img.ToRgba(palette);
        int tw = img.Width * tilesX, th = img.Height * tilesY;
        byte[] tiled = new byte[tw * th * 4];
        for (int y = 0; y < th; y++)
            for (int x = 0; x < tw; x++)
                one.AsSpan(((y % img.Height) * img.Width + x % img.Width) * 4, 4).CopyTo(tiled.AsSpan((y * tw + x) * 4, 4));

        byte[] rgba = PngWriter.Scale(tw, th, tiled, scale);
        int w = tw * scale, h = th * scale;
        if (checker)
        {
            for (int i = 0; i < w * h; i++)
            {
                if (rgba[i * 4 + 3] != 0)
                    continue;
                byte c = (((i % w) / 8 + (i / w) / 8) & 1) == 0 ? (byte)255 : (byte)200;
                rgba[i * 4] = c;
                rgba[i * 4 + 1] = 0;
                rgba[i * 4 + 2] = c;
                rgba[i * 4 + 3] = 255;
            }
        }
        string path = Path.Combine(dir, file);
        PngWriter.WriteRgba(path, w, h, rgba);
        Assert.True(new FileInfo(path).Length > 0);
    }
}
