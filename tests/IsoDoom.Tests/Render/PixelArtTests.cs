using System;
using System.Diagnostics;
using IsoDoom.Render;
using IsoDoom.Tests.Support;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Render;

/// <summary>T6.14b: the pixel-art resampler (<see cref="PixelArt"/>), RotSprite-style over Scale2x.</summary>
public class PixelArtTests
{
    // A pattern with no symmetry: every texel its own index, a few holes.
    private static IndexedImage Pattern(int w, int h)
    {
        byte[] pixels = new byte[w * h], opaque = new byte[w * h];
        for (int i = 0; i < w * h; i++)
        {
            pixels[i] = (byte)(i * 7 + 3);
            opaque[i] = (byte)(i % 5 == 2 ? 0 : 1);
        }
        return new IndexedImage(w, h, 0, 0, pixels, opaque);
    }

    private static void AssertSame(IndexedImage expected, IndexedImage actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        for (int i = 0; i < expected.Pixels.Length; i++)
        {
            Assert.Equal(expected.Opaque[i], actual.Opaque[i]);
            if (expected.Opaque[i] != 0)
                Assert.Equal(expected.Pixels[i], actual.Pixels[i]);
        }
    }

    [Fact]
    public void IdentityIsTheImageItself()
    {
        IndexedImage src = Pattern(13, 9);
        IndexedImage dst = PixelArt.Transform(src, (1, 2), (11, 2), (1, 2), (11, 2), 1, 13, 9);
        AssertSame(src, dst);
    }

    [Fact]
    public void IdentityOfARealSpriteIsTheSpriteItself()
    {
        // A 1-texel diagonal line, where Scale2x smooths the corners: the identity still keeps every texel.
        int w = 10, h = 10;
        byte[] pixels = new byte[w * h], opaque = new byte[w * h];
        for (int i = 0; i < w; i++)
        {
            pixels[i * w + i] = 40;
            opaque[i * w + i] = 1;
            if (i + 1 < w)
            {
                pixels[i * w + i + 1] = 41;
                opaque[i * w + i + 1] = 1;
            }
        }
        var src = new IndexedImage(w, h, 0, 0, pixels, opaque);
        AssertSame(src, PixelArt.Transform(src, (0, 0), (5, 5), (0, 0), (5, 5), 1, w, h));
    }

    [Fact]
    public void ANinetyDegreeRotationIsExact()
    {
        int w = 7, h = 4;
        IndexedImage src = Pattern(w, h);
        // Rotate clockwise (y down) about the origin corner, then move into a h × w image:
        // source (x, y) → destination (h − y, x); the source x axis maps onto the destination y axis.
        IndexedImage dst = PixelArt.Transform(src, (0, 0), (w, 0), (h, 0), (h, w), 1, h, w);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int di = x * h + (h - 1 - y), si = y * w + x;
                Assert.Equal(src.Opaque[si], dst.Opaque[di]);
                if (src.Opaque[si] != 0)
                    Assert.Equal(src.Pixels[si], dst.Pixels[di]);
            }
        }
        // And 180°.
        IndexedImage half = PixelArt.Transform(src, (0, 0), (w, 0), (w, h), (0, h), 1, w, h);
        for (int i = 0; i < w * h; i++)
        {
            Assert.Equal(src.Opaque[i], half.Opaque[w * h - 1 - i]);
            if (src.Opaque[i] != 0)
                Assert.Equal(src.Pixels[i], half.Pixels[w * h - 1 - i]);
        }
    }

    [Fact]
    public void HalfScaleOfACheckerKeepsItsIndices()
    {
        int w = 16, h = 16;
        byte[] pixels = new byte[w * h], opaque = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                pixels[y * w + x] = ((x / 2 + y / 2) & 1) == 0 ? (byte)0x50 : (byte)0xb0;
                opaque[y * w + x] = 1;
            }
        var src = new IndexedImage(w, h, 0, 0, pixels, opaque);
        IndexedImage dst = PixelArt.Transform(src, (0, 0), (16, 0), (0, 0), (8, 0), 1, 8, 8);
        for (int i = 0; i < dst.Pixels.Length; i++)
        {
            Assert.Equal(1, dst.Opaque[i]);
            Assert.Contains(dst.Pixels[i], new byte[] { 0x50, 0xb0 });
        }
        // Rotated by 30° too, never a blend.
        IndexedImage turned = PixelArt.Transform(src, (0, 0), (16, 0), (4, 4), (4 + 6.928, 4 - 4), 1, 16, 16);
        for (int i = 0; i < turned.Pixels.Length; i++)
            if (turned.Opaque[i] != 0)
                Assert.Contains(turned.Pixels[i], new byte[] { 0x50, 0xb0 });
    }

    [Fact]
    public void TheMuzzleLandsOnDstTo()
    {
        // A 40 × 9 bar of index 10 with a 4 × 3 marker of index 200 at its right end: (36–40, 3–6).
        int w = 40, h = 9;
        byte[] pixels = new byte[w * h], opaque = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool marker = x >= 36 && y is >= 3 and < 6;
                pixels[y * w + x] = marker ? (byte)200 : (byte)10;
                opaque[y * w + x] = 1;
            }
        var src = new IndexedImage(w, h, 0, 0, pixels, opaque);
        (double X, double Y) grip = (4, 4.5), muzzle = (38, 4.5);
        var rng = new Random(1406);
        for (int n = 0; n < 50; n++)
        {
            double angle = rng.NextDouble() * 2 * Math.PI, scale = 0.5 + rng.NextDouble();
            double along = n % 3 == 0 ? 0.4 + 0.6 * rng.NextDouble() : 1;
            (double X, double Y) from = (60.3, 59.7);
            double len = (muzzle.X - grip.X) * scale;
            (double X, double Y) to = (from.X + len * Math.Cos(angle), from.Y - len * Math.Sin(angle));
            // Map puts the muzzle at dstFrom + along × (dstTo − dstFrom).
            (double X, double Y) expected = (from.X + along * (to.X - from.X), from.Y + along * (to.Y - from.Y));
            (double X, double Y) mapped = PixelArt.Map(muzzle, grip, muzzle, from, to, along);
            Assert.True(Math.Abs(mapped.X - expected.X) < 1e-9 && Math.Abs(mapped.Y - expected.Y) < 1e-9);
            (double X, double Y) mappedGrip = PixelArt.Map(grip, grip, muzzle, from, to, along);
            Assert.True(Math.Abs(mappedGrip.X - from.X) < 1e-9 && Math.Abs(mappedGrip.Y - from.Y) < 1e-9);

            // And the pixels: the marker texels' centroid lies within half a texel of where
            // the marker's centre maps (the marker's own centre, (38, 4.5), is the muzzle).
            IndexedImage dst = PixelArt.Transform(src, grip, muzzle, from, to, along, 120, 120);
            double sx = 0, sy = 0;
            int count = 0;
            for (int y = 0; y < dst.Height; y++)
                for (int x = 0; x < dst.Width; x++)
                    if (dst.Opaque[y * dst.Width + x] != 0 && dst.Pixels[y * dst.Width + x] == 200)
                    {
                        sx += x + 0.5;
                        sy += y + 0.5;
                        count++;
                    }
            Assert.True(count > 0, $"angle {angle}: the marker vanished");
            double cx = sx / count, cy = sy / count;
            Assert.True(Math.Abs(cx - expected.X) <= 0.5 + 0.5 / scale && Math.Abs(cy - expected.Y) <= 0.5 + 0.5 / scale,
                $"angle {angle}, scale {scale}, along {along}: marker at ({cx:F2}, {cy:F2}), expected ({expected.X:F2}, {expected.Y:F2})");
        }
    }

    [Fact]
    public void TransparentOutsideAndDeterministic()
    {
        IndexedImage src = Pattern(20, 6);
        IndexedImage a = PixelArt.Transform(src, (2, 3), (18, 3), (30, 30), (40, 20), 0.7, 64, 64);
        IndexedImage b = PixelArt.Transform(Pattern(20, 6), (2, 3), (18, 3), (30, 30), (40, 20), 0.7, 64, 64);
        Assert.Equal(a.Pixels, b.Pixels);
        Assert.Equal(a.Opaque, b.Opaque);
        Assert.Equal(0, a.Opaque[0]);
        Assert.Equal(0, a.Opaque[63 * 64 + 63]);
    }

    [Fact]
    public void Scale2xRoundsADiagonalStep()
    {
        // EPX: P with A = C = 1 (above and left) and B = D = 2 gets corner 0 from A and 3 from D.
        short[] src = [9, 1, 9, 1, 5, 2, 9, 2, 9];
        short[] dst = PixelArt.Scale2x(src, 3, 3);
        int o = 1 * 2 * 6 + 1 * 2; // texel (1, 1)'s 2 × 2
        Assert.Equal(1, dst[o]);
        Assert.Equal(5, dst[o + 1]);
        Assert.Equal(5, dst[o + 6]);
        Assert.Equal(2, dst[o + 7]);
    }

}

/// <summary>T6.14b: the resampler's cost at load (SPEC §9), timed alone.</summary>
[Collection(TimingGroup.Name)]
public class PixelArtCostTests
{
    private static IndexedImage Pattern(int w, int h)
    {
        byte[] pixels = new byte[w * h], opaque = new byte[w * h];
        for (int i = 0; i < w * h; i++)
        {
            pixels[i] = (byte)(i * 7 + 3);
            opaque[i] = (byte)(i % 5 == 2 ? 0 : 1);
        }
        return new IndexedImage(w, h, 0, 0, pixels, opaque);
    }

    [Fact]
    public void ThreeHundredFifteenCompositesAreCheap()
    {
        // The compositor's load (SPEC §9): 9 weapons × 35 PLAY lumps, each a weapon of
        // about 62 × 16 into an 80 × 70 canvas. Target: under 100 ms for all.
        IndexedImage[] weapons = new IndexedImage[9];
        for (int i = 0; i < weapons.Length; i++)
            weapons[i] = Pattern(54 + i, 12 + i);
        var watch = Stopwatch.StartNew();
        for (int n = 0; n < 315; n++)
        {
            IndexedImage w = weapons[n % 9];
            double t = n * 0.37;
            PixelArt.Transform(w, (10, 8), (w.Width, 6), (30, 35), (30 + 30 * Math.Cos(t), 35 - 30 * Math.Sin(t)), 0.3 + 0.7 * (n % 4) / 3.0, 80, 70);
        }
        watch.Stop();
        TestContext.Current.SendDiagnosticMessage($"PixelArt: 315 composites in {watch.Elapsed.TotalMilliseconds:F1} ms");
        Console.WriteLine($"PixelArt: 315 composites in {watch.Elapsed.TotalMilliseconds:F1} ms");
        Assert.True(watch.Elapsed.TotalMilliseconds < 100, $"315 transforms took {watch.Elapsed.TotalMilliseconds:F1} ms");
    }
}
