using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IsoDoom.Render;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Render;

/// <summary>
/// T6.14a: the hold poses (<see cref="HoldPoses"/>): where the stock rifle is in each <c>PLAY</c>
/// lump of frames A–G, for both IWAD sets. With <c>ISODOOM_HOLDPOSE_SHEET=DIR</c> it also writes
/// the review sheets <c>DIR/poses-doom.png</c> and <c>DIR/poses-final.png</c> (WAD-derived: keep
/// them out of the repository).
/// </summary>
public class HoldPoseTests
{
    public const string SheetEnvVar = "ISODOOM_HOLDPOSE_SHEET";

    private static readonly Dictionary<string, (Dictionary<string, HoldPose> Poses, Dictionary<string, IndexedImage> Images)> _cache = [];

    private static (Dictionary<string, HoldPose> Poses, Dictionary<string, IndexedImage> Images) Load(string path)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(path, out var loaded))
                return loaded;
            var wad = WadArchive.Open(path);
            var logs = new List<string>();
            Dictionary<string, HoldPose> poses = HoldPoses.Load(wad, logs.Add);
            Assert.Empty(logs);
            var images = new Dictionary<string, IndexedImage>(StringComparer.Ordinal);
            foreach (string name in HoldPoses.Lumps)
                images[name] = Patch.Decode(wad.W_CacheLumpName(name).Span, name);
            return _cache[path] = (poses, images);
        }
    }

    private static string RequireIwad(string iwad) => iwad switch
    {
        "doom1" => TestWads.RequireDoom1(),
        "doom" => TestWads.RequireDoom(),
        "doom2" => TestWads.RequireDoom2(),
        "tnt" => TestWads.RequireTnt(),
        "plutonia" => TestWads.RequirePlutonia(),
        _ => throw new ArgumentException(iwad, nameof(iwad)),
    };

    public static TheoryData<string> Iwads => new("doom1", "doom", "doom2", "tnt", "plutonia");

    // The lump of frame `frame` whose first rotation is `rotation` (PLAYA2A8 for 'A', '2').
    private static string LumpOf(char frame, char rotation) =>
        HoldPoses.Lumps.Single(n => n[4] == frame && n[5] == rotation);

    [Fact]
    public void TheTableHasEveryFrameAtoGLumpOnce()
    {
        Assert.Equal(35, HoldPoses.Lumps.Count);
        Assert.Equal(HoldPoses.Lumps.Count, HoldPoses.Lumps.Distinct().Count());
        foreach (char frame in "ABCDEFG")
        {
            // Rotations 6–8 are 4–2 mirrored: they are the same lumps, not entries of their own
            // (the renderer mirrors the whole composite).
            Assert.Equal([$"PLAY{frame}1", $"PLAY{frame}2{frame}8", $"PLAY{frame}3{frame}7", $"PLAY{frame}4{frame}6", $"PLAY{frame}5"],
                HoldPoses.Lumps.Where(n => n[4] == frame));
        }
    }

    [Theory]
    [MemberData(nameof(Iwads))]
    public void EveryLumpOfTheIwadGetsAPose(string iwad)
    {
        var (poses, images) = Load(RequireIwad(iwad));
        Assert.Equal(HoldPoses.Lumps.Order(StringComparer.Ordinal), poses.Keys.Order(StringComparer.Ordinal));
        foreach ((string name, HoldPose pose) in poses)
        {
            IndexedImage image = images[name];
            int n = image.Width * image.Height;
            Assert.Equal(name, pose.Lump);
            Assert.Equal(n, pose.Rifle.Length);
            Assert.Equal(n, pose.Flash.Length);
            Assert.Equal(n, pose.Over.Length);
            for (int i = 0; i < n; i++)
            {
                if (pose.Rifle[i] || pose.Flash[i] || pose.Over[i])
                    Assert.True(image.Opaque[i] != 0, $"{name}: a mask on a transparent texel");
                Assert.False(pose.Rifle[i] && pose.Flash[i], $"{name}: rifle and flash overlap");
                Assert.False(pose.Over[i] && (pose.Rifle[i] || pose.Flash[i]), $"{name}: a hand texel on the rifle or flash");
            }
        }
    }

    [Fact]
    public void ThePosesAreTheSameInEveryIwad()
    {
        // DOOM1, The Ultimate Doom and Doom II share their PLAY lumps; TNT and Plutonia re-encode 24 of
        // them, with the same pixels: one table, the same poses everywhere.
        string[] present = [.. new[] { TestWads.Doom1Path, TestWads.DoomPath, TestWads.Doom2Path, TestWads.TntPath, TestWads.PlutoniaPath }
            .Where(p => p is not null).Select(p => p!)];
        Assert.SkipWhen(present.Length < 2, "Needs two of the IWADs.");
        var first = Load(present[0]).Poses;
        foreach (string path in present[1..])
        {
            var other = Load(path).Poses;
            foreach (string name in HoldPoses.Lumps)
            {
                HoldPose a = first[name], b = other[name];
                Assert.Equal(a.Rifle, b.Rifle);
                Assert.Equal(a.Flash, b.Flash);
                Assert.Equal(a.Over, b.Over);
                Assert.Equal(a.Layer, b.Layer);
                Assert.Equal(a.Grip, b.Grip);
                Assert.Equal(a.Muzzle, b.Muzzle);
                Assert.Equal(a.Foreshortening, b.Foreshortening);
            }
        }
    }

    [Fact]
    public void FinalDoomsReencodedLumpsDrawTheSamePictures()
    {
        string tnt = TestWads.RequireTnt(), doom1 = TestWads.RequireDoom1();
        var wadT = WadArchive.Open(tnt);
        var wad1 = WadArchive.Open(doom1);
        int reencoded = 0;
        foreach (string name in HoldPoses.Lumps)
        {
            ReadOnlySpan<byte> t = wadT.W_CacheLumpName(name).Span, d = wad1.W_CacheLumpName(name).Span;
            if (!t.SequenceEqual(d))
                reencoded++;
            Assert.Equal(HoldPoses.ContentHash(Patch.Decode(d, name)), HoldPoses.ContentHash(Patch.Decode(t, name)));
        }
        Assert.Equal(24, reencoded);
    }

    [Fact]
    public void EachRifleMaskIsAFewPlausiblePieces()
    {
        var (poses, images) = Load(TestWads.RequireDoom1());
        foreach (string name in HoldPoses.Lumps)
        {
            HoldPose pose = poses[name];
            IndexedImage image = images[name];
            int count = pose.Rifle.Count(b => b);
            Assert.InRange(count, 4, 300);
            // A front pose shows the rifle (frame F's front views but for the flash).
            if (pose.Layer == HoldLayer.Front && !(name[4] == 'F' && name[5] == '1'))
                Assert.True(count >= 40, $"{name}: {count} rifle texels");
            // The rifle is one piece, or two where the arm crosses it, and at most a piece the hand
            // splits off below (the magazine, aiming): each seeded on its own.
            List<int> sizes = Components(pose.Rifle, image.Width);
            Assert.True(sizes.Count <= 3, $"{name}: {sizes.Count} components ({string.Join(", ", sizes)})");
            Assert.True(sizes.Max() >= count / 3, $"{name}: no main piece ({string.Join(", ", sizes)})");
        }
    }

    [Fact]
    public void TheMasksSitWhereTheFrameSays()
    {
        var (poses, _) = Load(TestWads.RequireDoom1());
        foreach ((string name, HoldPose pose) in poses)
        {
            bool flash = pose.Flash.Any(b => b);
            // Frame F (firing) only, and seen in its front and side views.
            Assert.Equal(name[4] == 'F' && name[5] is '1' or '2' or '3' or '4', flash);
            // Behind poses have no hands over the weapon.
            if (pose.Layer == HoldLayer.Behind)
                Assert.DoesNotContain(true, pose.Over);
            else
                Assert.Contains(true, pose.Over);
            // The back views hold it behind the body; the front and side views in front.
            Assert.Equal(name[5] is '4' or '5' ? HoldLayer.Behind : HoldLayer.Front, pose.Layer);
        }
    }

    [Fact]
    public void TheMuzzleIsFartherOutThanTheGripInTheSideViews()
    {
        var (poses, images) = Load(TestWads.RequireDoom1());
        foreach (char frame in "ABCDEFG")
        {
            string name = LumpOf(frame, '3');
            HoldPose pose = poses[name];
            double centre = images[name].LeftOffset; // the thing's origin: the body's centre column
            Assert.True(Math.Abs(pose.Muzzle.X - centre) > Math.Abs(pose.Grip.X - centre),
                $"{name}: muzzle {pose.Muzzle} grip {pose.Grip} centre {centre}");
        }
    }

    [Fact]
    public void TheForeshorteningIsOneInTheReferenceViewAndLessFacingAway()
    {
        var (poses, images) = Load(TestWads.RequireDoom1());
        foreach (char frame in "ABCDEFG")
        {
            char refRotation = HoldPoses.ReferenceRotation(frame);
            HoldPose reference = poses[LumpOf(frame, refRotation)];
            double length = HoldPoses.AxisLength(reference.Rifle, images[reference.Lump].Width);
            Assert.Equal(HoldPoses.ReferenceLength(frame), length, 0.01);
            Assert.Equal(1, reference.Foreshortening);
            if (frame is 'E' or 'F')
            {
                // Aiming forward: full length side on, shorter towards or away from the camera.
                Assert.Equal('3', refRotation);
                Assert.True(poses[LumpOf(frame, '1')].Foreshortening < 1, $"{frame}1");
            }
            else
            {
                // Held across the chest: the reference is the longest view, the front one.
                foreach (char r in "12345")
                    Assert.True(HoldPoses.AxisLength(poses[LumpOf(frame, r)].Rifle, images[LumpOf(frame, r)].Width) <= length + 1e-9);
            }
            Assert.True(poses[LumpOf(frame, '5')].Foreshortening < 1, $"{frame}5");
            foreach (char r in "12345")
                Assert.InRange(poses[LumpOf(frame, r)].Foreshortening, HoldPoses.MinForeshortening, HoldPoses.MaxForeshortening);
        }
    }

    [Fact]
    public void AnUnknownLumpOrImageGetsNoPose()
    {
        var (_, images) = Load(TestWads.RequireDoom1());
        IndexedImage a1 = images["PLAYA1"];
        Assert.NotNull(HoldPoses.For("PLAYA1", a1));
        Assert.NotNull(HoldPoses.For("playa1", a1));
        Assert.Null(HoldPoses.For("PLAYH0", a1));
        Assert.Null(HoldPoses.For("PLAYA8", a1));
        Assert.Null(HoldPoses.For("PLAYA2A8", a1)); // another lump's picture
        byte[] pixels = (byte[])a1.Pixels.Clone();
        int i = Array.FindIndex(a1.Opaque, o => o != 0);
        pixels[i] ^= 1;
        Assert.Null(HoldPoses.For("PLAYA1", new IndexedImage(a1.Width, a1.Height, a1.LeftOffset, a1.TopOffset, pixels, a1.Opaque)));
        Assert.Null(HoldPoses.For("PLAYA1", new IndexedImage(a1.Width, a1.Height, a1.LeftOffset + 1, a1.TopOffset, a1.Pixels, a1.Opaque)));
    }

    [Fact]
    public void APwadsPlayLumpsAreLeftOutWithOneLogLine()
    {
        string doom1 = TestWads.RequireDoom1();
        byte[] patch = EncodePatch(3, 2, [0x70, 0x71, 0x72, 0x73, 0x74, 0x75]);
        WadFile pwad = new WadBuilder().Markers("S_START").Lump("PLAYA1", patch).Lump("PLAYB1", patch).Markers("S_END").ToWadFile("play.wad");
        var wad = new WadArchive([WadFile.Open(doom1), pwad]);
        var logs = new List<string>();
        Dictionary<string, HoldPose> poses = HoldPoses.Load(wad, logs.Add);
        Assert.Equal(33, poses.Count);
        Assert.DoesNotContain("PLAYA1", poses.Keys);
        Assert.DoesNotContain("PLAYB1", poses.Keys);
        string line = Assert.Single(logs);
        Assert.Contains("PLAYA1 PLAYB1", line, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSyntheticIwadHasNoPlayLumpsToPose()
    {
        var wad = new WadArchive([WadFile.FromBytes(IsoDoom.Tools.SyntheticIwad.SyntheticIwad.Build(), "synthetic.wad")]);
        var logs = new List<string>();
        Dictionary<string, HoldPose> poses = HoldPoses.Load(wad, logs.Add);
        // Whatever PLAY lumps it has are its own pictures: no pose, one line at most.
        Assert.Empty(poses);
        Assert.True(logs.Count <= 1);
    }

    [Fact]
    public void WritesTheReviewSheets()
    {
        string? dir = Environment.GetEnvironmentVariable(SheetEnvVar);
        Assert.SkipWhen(string.IsNullOrEmpty(dir), $"Set {SheetEnvVar}=DIR to write the review sheets.");
        Directory.CreateDirectory(dir!);
        bool any = false;
        foreach ((string file, string? path) in new[] { ("poses-doom.png", TestWads.Doom1Path ?? TestWads.Doom2Path ?? TestWads.DoomPath),
            ("poses-final.png", TestWads.TntPath ?? TestWads.PlutoniaPath) })
        {
            if (path is null)
                continue;
            var (poses, images) = Load(path);
            byte[] palette = Playpal.Load(WadArchive.Open(path)).GetPalette(0).ToArray();
            ReviewSheet.Write(Path.Combine(dir!, file), poses, images, palette, Path.GetFileName(path));
            any = true;
        }
        Assert.True(any, "No IWAD to draw.");
    }

    // ---- Helpers ----

    private static List<int> Components(bool[] mask, int width)
    {
        var seen = new bool[mask.Length];
        var sizes = new List<int>();
        int height = mask.Length / width;
        for (int s = 0; s < mask.Length; s++)
        {
            if (!mask[s] || seen[s])
                continue;
            int size = 0;
            var stack = new Stack<int>();
            stack.Push(s);
            seen[s] = true;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                size++;
                int x = i % width, y = i / width;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                            continue;
                        int j = ny * width + nx;
                        if (mask[j] && !seen[j])
                        {
                            seen[j] = true;
                            stack.Push(j);
                        }
                    }
            }
            sizes.Add(size);
        }
        return sizes;
    }

    // A patch of one post per column, every texel opaque.
    private static byte[] EncodePatch(int width, int height, byte[] pixels)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((short)width);
        bw.Write((short)height);
        bw.Write((short)0);
        bw.Write((short)0);
        int columnStart = 8 + 4 * width, columnSize = 4 + height + 1;
        for (int x = 0; x < width; x++)
            bw.Write(columnStart + x * columnSize);
        for (int x = 0; x < width; x++)
        {
            bw.Write((byte)0);
            bw.Write((byte)height);
            bw.Write((byte)0);
            for (int y = 0; y < height; y++)
                bw.Write(pixels[y * width + x]);
            bw.Write((byte)0);
            bw.Write((byte)0xff);
        }
        bw.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// The review sheet: for every lump (a row a frame, a column a rotation) the stock lump 6×, then
    /// the same with the rifle mask in magenta, the flash in cyan, the hands (Over) in yellow, the
    /// grip a green dot, the muzzle a red one joined to it, and its name, layer and foreshortening.
    /// </summary>
    private static class ReviewSheet
    {
        private const int Scale = 6, Gap = 12, LabelHeight = 40;
        private static readonly byte[] _background = [96, 96, 96], _transparent = [72, 72, 72];

        public static void Write(string path, Dictionary<string, HoldPose> poses, Dictionary<string, IndexedImage> images, byte[] palette, string title)
        {
            int cellW = 2 * 52 * Scale + Gap * 3, cellH = 56 * Scale + LabelHeight + Gap;
            int width = 5 * cellW + Gap, height = 7 * cellH + 40;
            var rgb = new byte[width * height * 3];
            for (int i = 0; i < width * height; i++)
                _background.CopyTo(rgb, i * 3);
            Text(rgb, width, Gap, 12, $"{title.ToUpperInvariant()}  HOLD POSES  RIFLE MAGENTA  FLASH CYAN  HANDS YELLOW  GRIP GREEN  MUZZLE RED", 3);
            for (int f = 0; f < 7; f++)
            {
                for (int r = 0; r < 5; r++)
                {
                    string name = HoldPoses.Lumps[f * 5 + r];
                    IndexedImage image = images[name];
                    poses.TryGetValue(name, out HoldPose? pose);
                    int ox = Gap + r * cellW, oy = 40 + f * cellH;
                    Draw(rgb, width, ox, oy, image, palette, null);
                    int ax = ox + image.Width * Scale + Gap;
                    Draw(rgb, width, ax, oy, image, palette, pose);
                    string label = pose is null ? $"{name} NO POSE"
                        : $"{name} {pose.Layer.ToString().ToUpperInvariant()} FS {pose.Foreshortening.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}";
                    Text(rgb, width, ox, oy + image.Height * Scale + 8, label, 3);
                }
            }
            PngWriter.WriteRgba(path, width, height, ToRgba(rgb));
        }

        private static byte[] ToRgba(byte[] rgb)
        {
            var rgba = new byte[rgb.Length / 3 * 4];
            for (int i = 0; i < rgb.Length / 3; i++)
            {
                rgba[i * 4] = rgb[i * 3];
                rgba[i * 4 + 1] = rgb[i * 3 + 1];
                rgba[i * 4 + 2] = rgb[i * 3 + 2];
                rgba[i * 4 + 3] = 255;
            }
            return rgba;
        }

        private static void Draw(byte[] rgb, int width, int ox, int oy, IndexedImage image, byte[] palette, HoldPose? pose)
        {
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                {
                    int i = y * image.Width + x;
                    byte[] c;
                    if (image.Opaque[i] == 0)
                        c = _transparent;
                    else
                    {
                        c = [palette[image.Pixels[i] * 3], palette[image.Pixels[i] * 3 + 1], palette[image.Pixels[i] * 3 + 2]];
                        if (pose is not null)
                        {
                            if (pose.Rifle[i])
                                c = Mix(c, [255, 0, 255]);
                            else if (pose.Flash[i])
                                c = Mix(c, [0, 255, 255]);
                            else if (pose.Over[i])
                                c = Mix(c, [255, 255, 0]);
                        }
                    }
                    for (int sy = 0; sy < Scale; sy++)
                        for (int sx = 0; sx < Scale; sx++)
                            Put(rgb, width, ox + x * Scale + sx, oy + y * Scale + sy, c);
                }
            if (pose is null)
                return;
            (double gx, double gy) = (ox + pose.Grip.X * Scale, oy + pose.Grip.Y * Scale);
            (double mx, double my) = (ox + pose.Muzzle.X * Scale, oy + pose.Muzzle.Y * Scale);
            int steps = (int)Math.Ceiling(Math.Max(Math.Abs(mx - gx), Math.Abs(my - gy)));
            for (int s = 0; s <= steps; s++)
            {
                double t = steps == 0 ? 0 : (double)s / steps;
                Put(rgb, width, (int)Math.Round(gx + (mx - gx) * t), (int)Math.Round(gy + (my - gy) * t), [255, 255, 255]);
            }
            Dot(rgb, width, gx, gy, [0, 220, 0]);
            Dot(rgb, width, mx, my, [255, 0, 0]);
        }

        private static byte[] Mix(byte[] c, byte[] tint) =>
            [(byte)((c[0] * 2 + tint[0] * 3) / 5), (byte)((c[1] * 2 + tint[1] * 3) / 5), (byte)((c[2] * 2 + tint[2] * 3) / 5)];

        private static void Dot(byte[] rgb, int width, double cx, double cy, byte[] c)
        {
            for (int dy = -6; dy <= 6; dy++)
                for (int dx = -6; dx <= 6; dx++)
                {
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d <= 6)
                        Put(rgb, width, (int)Math.Round(cx) + dx, (int)Math.Round(cy) + dy, d > 4 ? [0, 0, 0] : c);
                }
        }

        private static void Put(byte[] rgb, int width, int x, int y, byte[] c)
        {
            int height = rgb.Length / 3 / width;
            if (x < 0 || y < 0 || x >= width || y >= height)
                return;
            c.CopyTo(rgb, (y * width + x) * 3);
        }

        // A 3×5 font for the labels: upper case, digits and a few signs.
        private static readonly Dictionary<char, string> _font = new()
        {
            ['A'] = "010101111101101",
            ['B'] = "110101110101110",
            ['C'] = "011100100100011",
            ['D'] = "110101101101110",
            ['E'] = "111100110100111",
            ['F'] = "111100110100100",
            ['G'] = "011100101101011",
            ['H'] = "101101111101101",
            ['I'] = "111010010010111",
            ['J'] = "001001001101010",
            ['K'] = "101101110101101",
            ['L'] = "100100100100111",
            ['M'] = "101111111101101",
            ['N'] = "110101101101101",
            ['O'] = "010101101101010",
            ['P'] = "110101110100100",
            ['Q'] = "010101101110011",
            ['R'] = "110101110101101",
            ['S'] = "011100010001110",
            ['T'] = "111010010010010",
            ['U'] = "101101101101111",
            ['V'] = "101101101101010",
            ['W'] = "101101111111101",
            ['X'] = "101101010101101",
            ['Y'] = "101101010010010",
            ['Z'] = "111001010100111",
            ['0'] = "111101101101111",
            ['1'] = "010110010010111",
            ['2'] = "110001010100111",
            ['3'] = "110001010001110",
            ['4'] = "101101111001001",
            ['5'] = "111100110001110",
            ['6'] = "011100110101010",
            ['7'] = "111001010010010",
            ['8'] = "111101111101111",
            ['9'] = "010101011001110",
            ['.'] = "000000000000010",
            ['-'] = "000000111000000",
            [' '] = "000000000000000",
        };

        private static void Text(byte[] rgb, int width, int x, int y, string text, int scale)
        {
            foreach (char ch in text)
            {
                if (_font.TryGetValue(ch, out string? glyph))
                {
                    for (int gy = 0; gy < 5; gy++)
                        for (int gx = 0; gx < 3; gx++)
                        {
                            if (glyph[gy * 3 + gx] != '1')
                                continue;
                            for (int sy = 0; sy < scale; sy++)
                                for (int sx = 0; sx < scale; sx++)
                                    Put(rgb, width, x + gx * scale + sx, y + gy * scale + sy, [255, 255, 255]);
                        }
                }
                x += 4 * scale;
            }
        }
    }
}
