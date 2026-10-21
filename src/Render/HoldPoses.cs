using System;
using System.Collections.Generic;
using System.Globalization;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Render;

/// <summary>
/// T6.14a: the <see cref="HoldPose"/> of each <c>PLAY</c> lump of frames A–G (SPEC §12
/// "T6.14 Player weapons"). The committed data (<c>HoldPoses.Data.cs</c>) is ours, a few
/// numbers per lump: seed texels on the rifle and a box bounding it, rectangles added
/// to or cut from it, the hands' rectangles, the layer, the grip, a seed on frame F's
/// flash. The masks, the muzzle and the foreshortening are derived at load from the
/// WAD's own lump, so no pixel of id's is in the repository. Every lump's data holds
/// for one image only, checked by its <see cref="ContentHash"/>: DOOM1, The Ultimate
/// Doom, Doom II, TNT and Plutonia all draw the same 35 images (Final Doom re-encodes 24
/// of the lumps' posts, its pixels are the same), and a lump that matches none (a
/// PWAD's <c>PLAY</c>) gets no pose, so the game draws it stock.
/// </summary>
public static partial class HoldPoses
{
    /// <summary>The 35 lumps of frames A–G that have poses, in WAD order (rotations 6–8 are 4–2 mirrored).</summary>
    public static IReadOnlyList<string> Lumps => _order;

    /// <summary>
    /// The colours the rifle's flood fill crosses: the black and the grey ramp (0x00, 0x03–0x07,
    /// 0x50–0x6F). Not 0x01 and 0x02, the glove's darkest browns.
    /// </summary>
    public static bool IsRifleColour(byte index) => index is 0 or (>= 3 and <= 7) or (>= 0x50 and <= 0x6f);

    /// <summary>
    /// The colours of the hands and arms (the skin and gloves: 0x01, 0x02, 0x30–0x4F, 0x97) and,
    /// lit by frame F's flash, the oranges (0xD0–0xEF): the texels of an <c>over</c> rectangle
    /// that become <see cref="HoldPose.Over"/>.
    /// </summary>
    public static bool IsHandColour(byte index) => index is 1 or 2 or (>= 0x30 and <= 0x4f) or 0x97 or (>= 0xd0 and <= 0xef);

    /// <summary>The colours the muzzle flash's flood fill crosses: the yellows (0xA0–0xA7, 0xE0–0xE7) and the oranges (0xD0–0xDF, 0xE8–0xEF).</summary>
    public static bool IsFlashColour(byte index) => index is (>= 0xa0 and <= 0xa7) or (>= 0xd0 and <= 0xef);

    /// <summary>
    /// The pose of lump <paramref name="lump"/> drawn as <paramref name="image"/>, or null when
    /// the lump has no pose (not a frame A–G lump) or its image is not the one the data was
    /// made for (a PWAD's).
    /// </summary>
    public static HoldPose? For(string lump, IndexedImage image)
    {
        ArgumentNullException.ThrowIfNull(lump);
        ArgumentNullException.ThrowIfNull(image);
        if (!_specs.TryGetValue(lump.ToUpperInvariant(), out Spec? spec) || ContentHash(image) != spec.Hash)
            return null;
        return Derive(spec, image);
    }

    /// <summary>
    /// The poses of every <c>PLAY</c> lump of frames A–G in <paramref name="wad"/>, keyed by
    /// lump name. Lumps the WAD lacks, or whose image is not the known one, are left out;
    /// <paramref name="log"/> gets one line naming the latter (once per WAD, not per lump).
    /// </summary>
    public static Dictionary<string, HoldPose> Load(WadArchive wad, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(wad);
        var poses = new Dictionary<string, HoldPose>(StringComparer.Ordinal);
        var unknown = new List<string>();
        foreach (string name in _order)
        {
            WadLump? lump = wad.Find(name, LumpNamespace.Sprites) ?? wad.Find(name);
            if (lump is null)
                continue;
            IndexedImage image;
            try
            {
                image = Patch.Decode(lump.Data.Span, name);
            }
            catch (WadFormatException)
            {
                unknown.Add(name);
                continue;
            }
            if (For(name, image) is HoldPose pose)
                poses.Add(name, pose);
            else
                unknown.Add(name);
        }
        if (unknown.Count > 0)
            log?.Invoke($"player weapons: {unknown.Count} PLAY lump(s) match no known image, drawn stock: {string.Join(' ', unknown)}");
        return poses;
    }

    /// <summary>
    /// A 64-bit FNV-1a hash of the decoded image (size, offsets, every texel's mask and its
    /// index where opaque), as 16 hex digits: the same picture gives the same hash whatever
    /// the lump's post layout.
    /// </summary>
    public static string ContentHash(IndexedImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        ulong h = 14695981039346656037UL;
        void Add(int v)
        {
            for (int i = 0; i < 4; i++)
            {
                h ^= (byte)(v >> (8 * i));
                h *= 1099511628211UL;
            }
        }
        Add(image.Width);
        Add(image.Height);
        Add(image.LeftOffset);
        Add(image.TopOffset);
        for (int i = 0; i < image.Pixels.Length; i++)
        {
            bool opaque = image.Opaque[i] != 0;
            h ^= opaque ? (byte)1 : (byte)0;
            h *= 1099511628211UL;
            h ^= opaque ? image.Pixels[i] : (byte)0;
            h *= 1099511628211UL;
        }
        return h.ToString("x16", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The length of <paramref name="mask"/> along its principal axis, in texels (the
    /// projections of its texels' centres, end to end, plus one): the rifle's drawn length.
    /// </summary>
    public static double AxisLength(bool[] mask, int width)
    {
        Axis? axis = PrincipalAxis(mask, width);
        return axis is null ? 0 : axis.Value.Max - axis.Value.Min + 1;
    }

    /// <summary>
    /// The committed length frame <paramref name="frame"/>'s foreshortening divides by: the rifle's
    /// length in its <see cref="ReferenceRotation"/>'s lump.
    /// </summary>
    public static double ReferenceLength(char frame) => _refLength[frame];

    /// <summary>
    /// The rotation (<c>'1'</c>–<c>'5'</c>) whose rifle frame <paramref name="frame"/>'s length is measured
    /// against: the side view (3) in the aiming frames E and F, the longest view in the others,
    /// which hold the rifle across the chest (the front view, 1).
    /// </summary>
    public static char ReferenceRotation(char frame) => _refRotation[frame];

    /// <summary>The foreshortening's range.</summary>
    public const double MinForeshortening = 0.3, MaxForeshortening = 1;

    private static HoldPose Derive(Spec spec, IndexedImage image)
    {
        int w = image.Width, h = image.Height;
        bool InBox(Rect r, int x, int y) => x >= r.X0 && x <= r.X1 && y >= r.Y0 && y <= r.Y1;

        bool[] flash = Flood(image, spec.FlashSeeds, (x, y, c) => InBox(spec.FlashBox, x, y) && IsFlashColour(c));
        // The rifle: a flood from the seeds over the rifle's colours in the box; then, from it and
        // every opaque texel of the added rectangles (lit parts of the rifle, frame F's), a flood
        // that also crosses the added rectangles whatever their colour; then the cuts.
        bool[] rifle = Flood(image, spec.Seeds, (x, y, c) => InBox(spec.Box, x, y) && IsRifleColour(c));
        if (spec.Adds.Length > 0)
        {
            var seeds = new List<(int X, int Y)>();
            for (int i = 0; i < w * h; i++)
            {
                if (rifle[i] || (image.Opaque[i] != 0 && Array.Exists(spec.Adds, r => InBox(r, i % w, i / w))))
                    seeds.Add((i % w, i / w));
            }
            rifle = Flood(image, seeds, (x, y, c) => (InBox(spec.Box, x, y) && IsRifleColour(c)) || Array.Exists(spec.Adds, r => InBox(r, x, y)));
        }
        foreach (Rect r in spec.Cuts)
            Fill(image, rifle, r, false);
        var over = new bool[w * h];
        for (int i = 0; i < w * h; i++)
        {
            if (flash[i])
                rifle[i] = false;
        }
        foreach (Rect r in spec.Overs)
        {
            for (int y = Math.Max(0, r.Y0); y <= Math.Min(h - 1, r.Y1); y++)
                for (int x = Math.Max(0, r.X0); x <= Math.Min(w - 1, r.X1); x++)
                {
                    int i = y * w + x;
                    over[i] |= image.Opaque[i] != 0 && !rifle[i] && !flash[i] && IsHandColour(image.Pixels[i]);
                }
        }

        Axis axis = PrincipalAxis(rifle, w) ?? throw new InvalidOperationException($"{spec.Lump}: the rifle mask is empty.");
        (double X, double Y) Point(double t) => (axis.Cx + t * axis.Dx, axis.Cy + t * axis.Dy);
        // The axis's ends at the outer texels' edges.
        (double X, double Y) lo = Point(axis.Min - 0.5), hi = Point(axis.Max + 0.5);
        (double X, double Y) toward = spec.Tip ?? spec.Grip;
        double dLo = Dist2(lo, toward), dHi = Dist2(hi, toward);
        (double X, double Y) muzzle = spec.Muzzle ?? (spec.Tip is null ? (dLo >= dHi ? lo : hi) : (dLo <= dHi ? lo : hi));

        double length = axis.Max - axis.Min + 1;
        double fs = spec.Foreshortening ?? Math.Clamp(length / _refLength[spec.Lump[4]], MinForeshortening, MaxForeshortening);
        return new HoldPose(spec.Lump, rifle, flash, over, spec.Layer, spec.Grip, muzzle, fs) { Hands = spec.Hands };
    }

    private static double Dist2((double X, double Y) a, (double X, double Y) b) =>
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    private readonly record struct Axis(double Cx, double Cy, double Dx, double Dy, double Min, double Max);

    // The principal axis of the mask's texel centres (the covariance's main eigenvector),
    // with the extreme projections on it relative to the centroid.
    private static Axis? PrincipalAxis(bool[] mask, int width)
    {
        double sx = 0, sy = 0;
        int n = 0;
        for (int i = 0; i < mask.Length; i++)
        {
            if (!mask[i])
                continue;
            sx += i % width + 0.5;
            sy += i / width + 0.5;
            n++;
        }
        if (n == 0)
            return null;
        double cx = sx / n, cy = sy / n, cxx = 0, cyy = 0, cxy = 0;
        for (int i = 0; i < mask.Length; i++)
        {
            if (!mask[i])
                continue;
            double dx = i % width + 0.5 - cx, dy = i / width + 0.5 - cy;
            cxx += dx * dx;
            cyy += dy * dy;
            cxy += dx * dy;
        }
        double angle = 0.5 * Math.Atan2(2 * cxy, cxx - cyy);
        double ux = Math.Cos(angle), uy = Math.Sin(angle);
        double min = double.MaxValue, max = double.MinValue;
        for (int i = 0; i < mask.Length; i++)
        {
            if (!mask[i])
                continue;
            double t = (i % width + 0.5 - cx) * ux + (i / width + 0.5 - cy) * uy;
            min = Math.Min(min, t);
            max = Math.Max(max, t);
        }
        return new Axis(cx, cy, ux, uy, min, max);
    }

    // An 8-connected flood fill from the seeds over the opaque texels `inside` accepts.
    private static bool[] Flood(IndexedImage image, IReadOnlyList<(int X, int Y)> seeds, Func<int, int, byte, bool> inside)
    {
        int w = image.Width, h = image.Height;
        var mask = new bool[w * h];
        var stack = new Stack<(int X, int Y)>();
        foreach ((int X, int Y) s in seeds)
        {
            int i = s.Y * w + s.X;
            if (s.X < 0 || s.Y < 0 || s.X >= w || s.Y >= h || image.Opaque[i] == 0 || !inside(s.X, s.Y, image.Pixels[i]))
                throw new InvalidOperationException($"seed ({s.X}, {s.Y}) is not on a texel the fill crosses.");
            stack.Push(s);
        }
        while (stack.Count > 0)
        {
            (int x, int y) = stack.Pop();
            int i = y * w + x;
            if (mask[i])
                continue;
            mask[i] = true;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                        continue;
                    int j = ny * w + nx;
                    if (!mask[j] && image.Opaque[j] != 0 && inside(nx, ny, image.Pixels[j]))
                        stack.Push((nx, ny));
                }
        }
        return mask;
    }

    private static void Fill(IndexedImage image, bool[] mask, Rect r, bool value)
    {
        for (int y = Math.Max(0, r.Y0); y <= Math.Min(image.Height - 1, r.Y1); y++)
            for (int x = Math.Max(0, r.X0); x <= Math.Min(image.Width - 1, r.X1); x++)
            {
                int i = y * image.Width + x;
                if (image.Opaque[i] != 0)
                    mask[i] = value;
            }
    }

    // ---- The committed table (HoldPoses.Data.cs), parsed once. ----

    private readonly record struct Rect(int X0, int Y0, int X1, int Y1);

    private sealed record Spec(
        string Lump, string Hash, HoldLayer Layer, (int X, int Y)[] Seeds, Rect Box, Rect[] Adds, Rect[] Cuts,
        Rect[] Overs, (double X, double Y) Grip, (double X, double Y)? Tip, (double X, double Y)? Muzzle, (int X, int Y)[] FlashSeeds,
        Rect FlashBox, double? Foreshortening, (double X, double Y)[] Hands);

    private static readonly List<string> _order = [];
    private static readonly Dictionary<char, char> _refRotation = [];
    private static readonly Dictionary<string, Spec> _specs = Parse(out _refLength);
    private static readonly Dictionary<char, double> _refLength;

    private static Dictionary<string, Spec> Parse(out Dictionary<char, double> refLength)
    {
        var specs = new Dictionary<string, Spec>(StringComparer.Ordinal);
        refLength = [];
        foreach (string raw in Data.Split('\n'))
        {
            string line = raw.Trim();
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0)
                line = line[..comment].Trim();
            if (line.Length == 0)
                continue;
            string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens[0] == "ref")
            {
                foreach (string t in tokens[1..])
                {
                    string[] kv = t.Split('=', '@');
                    refLength[kv[0][0]] = double.Parse(kv[1], CultureInfo.InvariantCulture);
                    _refRotation[kv[0][0]] = kv[2][0];
                }
                continue;
            }
            string lump = tokens[0];
            string hash = "";
            HoldLayer layer = HoldLayer.Front;
            var seeds = new List<(int, int)>();
            var flashSeeds = new List<(int, int)>();
            Rect box = new(0, 0, 9999, 9999), flashBox = new(0, 0, 9999, 9999);
            var adds = new List<Rect>();
            var cuts = new List<Rect>();
            var overs = new List<Rect>();
            (double, double)? grip = null, tip = null, muzzle = null;
            var hands = new List<(double, double)>();
            double? fs = null;
            foreach (string t in tokens[1..])
            {
                if (t == "front" || t == "behind")
                {
                    layer = t == "front" ? HoldLayer.Front : HoldLayer.Behind;
                    continue;
                }
                int eq = t.IndexOf('=', StringComparison.Ordinal);
                if (eq < 0)
                    throw new FormatException($"{lump}: bad token {t}");
                string key = t[..eq], value = t[(eq + 1)..];
                switch (key)
                {
                    case "hash": hash = value; break;
                    case "seed": seeds.AddRange(Points(value)); break;
                    case "flash": flashSeeds.AddRange(Points(value)); break;
                    case "box": box = Rects(value)[0]; break;
                    case "fbox": flashBox = Rects(value)[0]; break;
                    case "add": adds.AddRange(Rects(value)); break;
                    case "cut": cuts.AddRange(Rects(value)); break;
                    case "over": overs.AddRange(Rects(value)); break;
                    case "grip": grip = Point(value); break;
                    case "tip": tip = Point(value); break;
                    case "muzzle": muzzle = Point(value); break;
                    case "hands": foreach (string h in value.Split(';')) hands.Add(Point(h)); break;
                    case "fs": fs = double.Parse(value, CultureInfo.InvariantCulture); break;
                    default: throw new FormatException($"{lump}: unknown key {key}");
                }
            }
            if (grip is null || seeds.Count == 0 || hash.Length != 16)
                throw new FormatException($"{lump}: a pose needs hash, seed and grip.");
            if (layer == HoldLayer.Behind && overs.Count > 0)
                throw new FormatException($"{lump}: a Behind pose has no hands over the weapon.");
            specs.Add(lump, new Spec(lump, hash, layer, [.. seeds], box, [.. adds], [.. cuts], [.. overs],
                grip.Value, tip, muzzle, [.. flashSeeds], flashBox, fs, [.. hands]));
            _order.Add(lump);
        }
        return specs;
    }

    private static IEnumerable<(int, int)> Points(string value)
    {
        foreach (string p in value.Split(';'))
        {
            string[] xy = p.Split(',');
            yield return (int.Parse(xy[0], CultureInfo.InvariantCulture), int.Parse(xy[1], CultureInfo.InvariantCulture));
        }
    }

    private static (double, double) Point(string value)
    {
        string[] xy = value.Split(',');
        return (double.Parse(xy[0], CultureInfo.InvariantCulture), double.Parse(xy[1], CultureInfo.InvariantCulture));
    }

    private static List<Rect> Rects(string value)
    {
        var rects = new List<Rect>();
        foreach (string p in value.Split(';'))
        {
            string[] c = p.Split(',');
            int At(int k) => int.Parse(c[k], CultureInfo.InvariantCulture);
            rects.Add(new Rect(At(0), At(1), At(2), At(3)));
        }
        return rects;
    }
}
