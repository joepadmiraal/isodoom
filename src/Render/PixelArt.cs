using System;
using System.Runtime.CompilerServices;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Render;

/// <summary>
/// A RotSprite-style resampler for palette art (T6.14b, SPEC §12 "T6.14
/// Player weapons"): rotates, scales and foreshortens an
/// <see cref="IndexedImage"/> without blending, so the result is still
/// palette indices. The source is first enlarged 8× by Scale2x (EPX) three
/// times, which rounds the stair steps of diagonal lines, then every
/// destination texel's centre is mapped back into the 8× image and takes the
/// texel there (nearest). A thin line rotated by 25° so stays one line instead
/// of breaking into dashes, as plain nearest sampling of the original does.
/// <para>
/// Deterministic on every platform: the affine is computed once from the
/// given points with <c>+ − × ÷</c> and <c>Math.Sqrt</c> only (IEEE 754
/// correctly rounded, no <c>Math.Sin</c>/<c>Cos</c>, whose results may differ
/// between runtimes), rounded to 32.32 fixed point, and every texel is then
/// located with integer arithmetic alone. No Godot types: the tests link it.
/// </para>
/// </summary>
public static class PixelArt
{
    /// <summary>The enlargement before sampling (three Scale2x passes).</summary>
    public const int Factor = 8;

    /// <summary>The colour value a transparent texel has inside the enlarged image.</summary>
    private const short Clear = -1;

    private const int FracBits = 32;

    private static readonly ConditionalWeakTable<IndexedImage, Enlarged> _cache = [];

    /// <summary>
    /// An image enlarged <see cref="Factor"/>× by Scale2x, palette
    /// indices with −1 for transparent. Built once per image and kept as long
    /// as the image is (so the 35 composites of one weapon enlarge it once).
    /// </summary>
    public sealed class Enlarged
    {
        internal Enlarged(int width, int height, short[] texels)
        {
            Width = width;
            Height = height;
            Texels = texels;
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>Row-major palette indices, −1 where transparent.</summary>
        internal short[] Texels { get; }

        /// <summary>The texel at (<paramref name="x"/>, <paramref name="y"/>): a palette index, or −1 for transparent.</summary>
        public short this[int x, int y] => Texels[y * Width + x];
    }

    /// <summary><paramref name="src"/> enlarged <see cref="Factor"/>× (cached per image object).</summary>
    public static Enlarged Enlarge(IndexedImage src) => _cache.GetValue(src, Build);

    private static Enlarged Build(IndexedImage src)
    {
        int w = src.Width, h = src.Height;
        short[] texels = new short[w * h];
        for (int i = 0; i < texels.Length; i++)
            texels[i] = src.Opaque[i] != 0 ? src.Pixels[i] : Clear;
        for (int pass = 0; pass < 3; pass++)
        {
            texels = Scale2x(texels, w, h);
            w *= 2;
            h *= 2;
        }
        return new Enlarged(w, h, texels);
    }

    /// <summary>
    /// One Scale2x (EPX, Andrea Mazzoleni's) pass: each texel P becomes 2×2,
    /// a corner taking the colour of its two edge neighbours where they agree
    /// and the other two do not. Outside the image is transparent.
    /// </summary>
    public static short[] Scale2x(short[] src, int w, int h)
    {
        short[] dst = new short[w * h * 4];
        int dw = w * 2;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                short p = src[y * w + x];
                short a = y > 0 ? src[(y - 1) * w + x] : Clear;      // above
                short b = x < w - 1 ? src[y * w + x + 1] : Clear;    // right
                short c = x > 0 ? src[y * w + x - 1] : Clear;        // left
                short d = y < h - 1 ? src[(y + 1) * w + x] : Clear;  // below
                short e0 = p, e1 = p, e2 = p, e3 = p;
                if (a != d && c != b)
                {
                    if (c == a)
                        e0 = a;
                    if (a == b)
                        e1 = b;
                    if (c == d)
                        e2 = c;
                    if (d == b)
                        e3 = d;
                }
                int o = y * 2 * dw + x * 2;
                dst[o] = e0;
                dst[o + 1] = e1;
                dst[o + dw] = e2;
                dst[o + dw + 1] = e3;
            }
        }
        return dst;
    }

    /// <summary>
    /// Maps <paramref name="src"/> so <paramref name="srcFrom"/> lands on
    /// <paramref name="dstFrom"/> and <paramref name="srcTo"/> on
    /// <paramref name="dstTo"/> (a rotation and a uniform scale, never a
    /// mirror), then scales it along that axis (the line through
    /// <paramref name="dstFrom"/> towards <paramref name="dstTo"/>) by
    /// <paramref name="along"/> about <paramref name="dstFrom"/>, so
    /// <paramref name="srcTo"/> ends at <c>dstFrom + along × (dstTo − dstFrom)</c>
    /// (<see cref="Map"/>). Draws into a new <paramref name="width"/> ×
    /// <paramref name="height"/> image (offsets 0), transparent where nothing
    /// lands; texel coordinates as <see cref="IndexedImage"/>'s, (0, 0) the
    /// top-left texel's corner.
    /// </summary>
    public static IndexedImage Transform(IndexedImage src, (double X, double Y) srcFrom, (double X, double Y) srcTo,
        (double X, double Y) dstFrom, (double X, double Y) dstTo, double along, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        double[] m = Inverse(srcFrom, srcTo, dstFrom, dstTo, along);
        byte[] pixels = new byte[width * height];
        byte[] opaque = new byte[width * height];

        // Destination (x, y)'s centre in source texels: srcFrom + M · (centre − dstFrom).
        // Right angles at unit scale and no foreshortening sample the original
        // itself: there is nothing to smooth, and an 8× block's corners are
        // Scale2x's, so a texel would otherwise turn into a neighbour's colour.
        bool rightAngle = along == 1 && IsUnitRightAngle(m);
        int factor = rightAngle ? 1 : Factor;
        (int sw, int sh) = rightAngle ? (src.Width, src.Height) : (src.Width * Factor, src.Height * Factor);
        short[] texels = rightAngle ? Original(src) : Enlarge(src).Texels;

        // In fixed point (FracBits), in units of the sampled image's texels.
        long ox = Fixed(factor * (srcFrom.X + m[0] * (0.5 - dstFrom.X) + m[1] * (0.5 - dstFrom.Y)));
        long oy = Fixed(factor * (srcFrom.Y + m[2] * (0.5 - dstFrom.X) + m[3] * (0.5 - dstFrom.Y)));
        long xx = Fixed(factor * m[0]), xy = Fixed(factor * m[1]);
        long yx = Fixed(factor * m[2]), yy = Fixed(factor * m[3]);
        for (int y = 0; y < height; y++)
        {
            long rx = ox + y * xy, ry = oy + y * yy;
            for (int x = 0; x < width; x++)
            {
                long sx = (rx + x * xx) >> FracBits, sy = (ry + x * yx) >> FracBits;
                if (sx < 0 || sy < 0 || sx >= sw || sy >= sh)
                    continue;
                short t = texels[sy * sw + sx];
                if (t == Clear)
                    continue;
                pixels[y * width + x] = (byte)t;
                opaque[y * width + x] = 1;
            }
        }
        return new IndexedImage(width, height, 0, 0, pixels, opaque);
    }

    /// <summary>Where <see cref="Transform"/> puts source point <paramref name="p"/> (destination texels).</summary>
    public static (double X, double Y) Map((double X, double Y) p, (double X, double Y) srcFrom, (double X, double Y) srcTo,
        (double X, double Y) dstFrom, (double X, double Y) dstTo, double along)
    {
        double[] m = Inverse(srcFrom, srcTo, dstFrom, dstTo, along);
        // Invert the 2×2 matrix M.
        double det = m[0] * m[3] - m[1] * m[2];
        double dx = p.X - srcFrom.X, dy = p.Y - srcFrom.Y;
        return (dstFrom.X + (m[3] * dx - m[1] * dy) / det, dstFrom.Y + (-m[2] * dx + m[0] * dy) / det);
    }

    /// <summary>
    /// The linear part M of the destination→source map, row-major
    /// (<c>src − srcFrom = M · (dst − dstFrom)</c>): undo the foreshortening
    /// along v̂ (the destination axis), rotate v̂ back onto û (the source axis)
    /// and scale by |u| / |v|.
    /// </summary>
    private static double[] Inverse((double X, double Y) srcFrom, (double X, double Y) srcTo,
        (double X, double Y) dstFrom, (double X, double Y) dstTo, double along)
    {
        if (!(along > 0) || double.IsInfinity(along))
            throw new ArgumentOutOfRangeException(nameof(along), "The foreshortening must be positive.");
        double ux = srcTo.X - srcFrom.X, uy = srcTo.Y - srcFrom.Y;
        double vx = dstTo.X - dstFrom.X, vy = dstTo.Y - dstFrom.Y;
        double lu = Math.Sqrt(ux * ux + uy * uy), lv = Math.Sqrt(vx * vx + vy * vy);
        if (!(lu > 0) || !(lv > 0))
            throw new ArgumentException("The source and destination points must differ.");
        ux /= lu;
        uy /= lu;
        vx /= lv;
        vy /= lv;
        double s = lu / lv;
        // d ↦ s · [ (d·v̂) / along · û + (d·v̂⊥) · û⊥ ], with ⊥ = (−y, x).
        double k = s / along;
        return
        [
            k * vx * ux + s * vy * uy, k * vy * ux - s * vx * uy,
            k * vx * uy - s * vy * ux, k * vy * uy + s * vx * ux,
        ];
    }

    // Each row and column of M one ±1 and one 0, exactly.
    private static bool IsUnitRightAngle(double[] m) =>
        (m[1] == 0 && m[2] == 0 && Math.Abs(m[0]) == 1 && Math.Abs(m[3]) == 1)
        || (m[0] == 0 && m[3] == 0 && Math.Abs(m[1]) == 1 && Math.Abs(m[2]) == 1);

    private static short[] Original(IndexedImage src)
    {
        short[] texels = new short[src.Width * src.Height];
        for (int i = 0; i < texels.Length; i++)
            texels[i] = src.Opaque[i] != 0 ? src.Pixels[i] : Clear;
        return texels;
    }

    private static long Fixed(double v) => (long)Math.Round(v * (1L << FracBits), MidpointRounding.ToEven);
}
