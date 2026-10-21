using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IsoDoom.Render;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Render;

/// <summary>
/// T6.14c: the composites (<see cref="HeldWeaponComposer"/>, <see cref="HeldWeaponSet"/>): every
/// player's <c>PLAY</c> lump of frames A–G holding each weapon. With
/// <c>ISODOOM_COMPOSITE_SHEET=DIR</c> it writes the review sheets <c>composites-doom.png</c>,
/// <c>composites-final.png</c> (and a sheet a frame in <c>composites-doom/</c> and
/// <c>composites-final/</c>) and <c>compare.png</c> (WAD-derived: keep them out of the repository).
/// </summary>
public class HeldWeaponComposerTests
{
    public const string SheetEnvVar = "ISODOOM_COMPOSITE_SHEET";

    private static readonly Dictionary<string, (HeldWeaponSet Set, Dictionary<string, IndexedImage> Images, byte[] Palette, WadArchive Wad)> _cache = [];

    internal static (HeldWeaponSet Set, Dictionary<string, IndexedImage> Images, byte[] Palette, WadArchive Wad) Load(string path)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(path, out var loaded))
                return loaded;
            var wad = WadArchive.Open(path);
            HeldWeaponSet set = HeldWeaponSet.Build(wad);
            var images = new Dictionary<string, IndexedImage>(StringComparer.Ordinal);
            foreach (string name in HoldPoses.Lumps)
                images[name] = Patch.Decode(wad.W_CacheLumpName(name).Span, name);
            byte[] palette = Playpal.Load(wad).GetPalette(0).ToArray();
            return _cache[path] = (set, images, palette, wad);
        }
    }

    private static IEnumerable<(weapontype_t Weapon, string Lump, HeldComposite Composite)> All(HeldWeaponSet set)
    {
        for (int slot = 0; slot < (int)weapontype_t.NUMWEAPONS; slot++)
            foreach (string lump in HoldPoses.Lumps)
                if (set.Get((weapontype_t)slot, lump) is HeldComposite c)
                    yield return ((weapontype_t)slot, lump, c);
    }

    [Fact]
    public void Doom2HasEveryWeaponForEveryLump()
    {
        var (set, _, _, _) = Load(TestWads.RequireDoom2());
        Assert.Equal(9 * 35, set.Count);
        for (int slot = 0; slot < 9; slot++)
            Assert.True(set.Has((weapontype_t)slot));
    }

    [Fact]
    public void SharewareLacksTheWeaponsItHasNoArtFor()
    {
        var (set, _, _, _) = Load(TestWads.RequireDoom1());
        Assert.Equal(6 * 35, set.Count);
        Assert.False(set.Has(weapontype_t.wp_plasma));
        Assert.False(set.Has(weapontype_t.wp_bfg));
        Assert.False(set.Has(weapontype_t.wp_supershotgun));
        Assert.True(set.Has(weapontype_t.wp_fist));
        Assert.True(set.Has(weapontype_t.wp_pistol));
        Assert.Null(set.Get(weapontype_t.wp_bfg, "PLAYA1"));
        Assert.Null(set.Get(weapontype_t.wp_shotgun, "PLAYH0"));
    }

    [Fact]
    public void TheBodyStaysWhereTheStockLumpDrawsIt()
    {
        var (set, images, _, _) = Load(TestWads.RequireDoom2());
        foreach ((weapontype_t weapon, string lump, HeldComposite c) in All(set))
        {
            IndexedImage stock = images[lump];
            // The origin (and so the feet) where the stock lump has it.
            Assert.Equal(stock.LeftOffset, c.Image.LeftOffset - c.ShiftX);
            Assert.Equal(stock.TopOffset, c.Image.TopOffset - c.ShiftY);
            // The feet row: the stock lump's lowest row is drawn, and nothing below it.
            int lowest = (stock.Height - 1) + c.ShiftY;
            Assert.True(lowest < c.Image.Height, $"{weapon} {lump}: the feet row is cut");
        }
    }

    [Fact]
    public void TheBodyIsUnchangedOutsideTheRifleAndFlash()
    {
        var (set, images, _, _) = Load(TestWads.RequireDoom2());
        foreach ((weapontype_t weapon, string lump, HeldComposite c) in All(set))
        {
            // Empty hands: EmptyHandsAreTheFilledBodyWithFists.
            if (weapon == weapontype_t.wp_fist)
                continue;
            IndexedImage stock = images[lump];
            HoldPose pose = set.Poses[lump];
            for (int y = 0; y < stock.Height; y++)
                for (int x = 0; x < stock.Width; x++)
                {
                    int i = y * stock.Width + x;
                    if (stock.Opaque[i] == 0 || pose.Rifle[i] || pose.Flash[i])
                        continue;
                    int cx = x + c.ShiftX, cy = y + c.ShiftY;
                    bool inside = cx >= 0 && cy >= 0 && cx < c.Image.Width && cy < c.Image.Height;
                    int o = cy * c.Image.Width + cx;
                    CompositeTexel source = inside ? c.Sources[o] : CompositeTexel.None;
                    if (source == CompositeTexel.Body)
                    {
                        Assert.Equal(stock.Pixels[i], c.Image.Pixels[o]);
                        continue;
                    }
                    // In front, the fingers that wrapped the stock rifle (hand colours beside it)
                    // go where the weapon does not run under them: filled or cleared.
                    if (source is CompositeTexel.Fill or CompositeTexel.None && pose.Layer == HoldLayer.Front
                        && HoldPoses.IsHandColour(stock.Pixels[i]) && NextToRifle(pose, stock.Width, x, y))
                        continue;
                    // Only a front weapon, or the moved flash, covers the body; never the hands.
                    Assert.True(source == CompositeTexel.Flash || (source == CompositeTexel.Weapon && pose.Layer == HoldLayer.Front),
                        $"{weapon} {lump}: ({x}, {y}) is {source}");
                    Assert.False(source == CompositeTexel.Weapon && pose.Over[i], $"{weapon} {lump}: the weapon over a hand at ({x}, {y})");
                }
        }
    }

    [Fact]
    public void ABehindWeaponIsNeverOverTheBody()
    {
        var (set, images, _, _) = Load(TestWads.RequireDoom2());
        foreach ((_, string lump, HeldComposite c) in All(set))
        {
            HoldPose pose = set.Poses[lump];
            if (pose.Layer != HoldLayer.Behind)
                continue;
            IndexedImage stock = images[lump];
            (_, CompositeTexel[] body) = HeldWeaponComposer.RemoveRifle(stock, pose);
            for (int i = 0; i < body.Length; i++)
            {
                if (body[i] == CompositeTexel.None)
                    continue;
                int o = (i / stock.Width + c.ShiftY) * c.Image.Width + i % stock.Width + c.ShiftX;
                Assert.NotEqual(CompositeTexel.Weapon, c.Sources[o]);
            }
        }
    }

    [Fact]
    public void NoWeaponTexelIsInTheRamp()
    {
        var (set, _, _, _) = Load(TestWads.RequireDoom2());
        foreach ((weapontype_t weapon, string lump, HeldComposite c) in All(set))
        {
            for (int i = 0; i < c.Image.Pixels.Length; i++)
            {
                if (c.Sources[i] == CompositeTexel.Weapon)
                    Assert.False(c.Image.Pixels[i] is >= HeldWeapons.RampFirst and <= HeldWeapons.RampLast, $"{weapon} {lump}: a ramp texel");
                Assert.Equal(c.Sources[i] != CompositeTexel.None, c.Image.Opaque[i] != 0);
            }
        }
    }

    [Fact]
    public void EveryWeaponIsDrawnInEveryLump()
    {
        var (set, _, _, _) = Load(TestWads.RequireDoom2());
        foreach ((weapontype_t weapon, string lump, HeldComposite c) in All(set))
        {
            // A behind weapon may hide whole (the pistol in a back view), and so may one
            // pointing at the camera under the hands (the pistol in PLAYF1).
            if (weapon == weapontype_t.wp_fist || set.Poses[lump].Layer == HoldLayer.Behind || lump is "PLAYE1" or "PLAYF1")
                continue;
            int texels = 0;
            foreach (CompositeTexel s in c.Sources)
                texels += s == CompositeTexel.Weapon ? 1 : 0;
            Assert.True(texels >= 4, $"{weapon} {lump}: {texels} weapon texels");
        }
    }

    [Fact]
    public void TwoBuildsAreByteEqual()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom2());
        HeldWeaponSet a = HeldWeaponSet.Build(wad), b = HeldWeaponSet.Build(wad);
        foreach ((weapontype_t weapon, string lump, HeldComposite ca) in All(a))
        {
            HeldComposite cb = b.Get(weapon, lump)!;
            Assert.Equal(ca.Image.Width, cb.Image.Width);
            Assert.Equal(ca.Image.Height, cb.Image.Height);
            Assert.Equal(ca.Image.LeftOffset, cb.Image.LeftOffset);
            Assert.Equal(ca.Image.TopOffset, cb.Image.TopOffset);
            Assert.Equal(ca.Image.Pixels, cb.Image.Pixels);
            Assert.Equal(ca.Image.Opaque, cb.Image.Opaque);
        }
    }

    [Fact]
    public void EmptyHandsAreTheFilledBodyWithFists()
    {
        var (set, images, _, _) = Load(TestWads.RequireDoom2());
        foreach (string lump in HoldPoses.Lumps)
        {
            HeldComposite c = set.Get(weapontype_t.wp_fist, lump)!;
            IndexedImage stock = images[lump];
            HoldPose pose = set.Poses[lump];
            Assert.Equal(stock.LeftOffset, c.Image.LeftOffset - c.ShiftX);
            Assert.Equal(stock.TopOffset, c.Image.TopOffset - c.ShiftY);
            Assert.DoesNotContain(c.Sources.ToArray(), s => s is CompositeTexel.Weapon or CompositeTexel.Flash);
            int fist = c.Sources.ToArray().Count(s => s == CompositeTexel.Fist);
            if (pose.Layer == HoldLayer.Behind)
            {
                // Behind: no hands to replace, the filled body alone.
                Assert.Equal(0, fist);
                (byte[] pixels, CompositeTexel[] sources) = HeldWeaponComposer.RemoveRifle(stock, pose);
                for (int i = 0; i < sources.Length; i++)
                {
                    int cx = i % stock.Width + c.ShiftX, cy = i / stock.Width + c.ShiftY;
                    bool inside = cx >= 0 && cy >= 0 && cx < c.Image.Width && cy < c.Image.Height;
                    if (sources[i] == CompositeTexel.None)
                    {
                        Assert.True(!inside || c.Sources[cy * c.Image.Width + cx] == CompositeTexel.None, $"{lump}: {i}");
                        continue;
                    }
                    Assert.True(inside);
                    Assert.Equal(sources[i], c.Sources[cy * c.Image.Width + cx]);
                    Assert.Equal(pixels[i], c.Image.Pixels[cy * c.Image.Width + cx]);
                }
                continue;
            }
            // Front: a fist (or two) over the hands; the body's own texels elsewhere unchanged.
            Assert.True(fist >= 17, $"{lump}: {fist} fist texels");
            for (int y = 0; y < stock.Height; y++)
                for (int x = 0; x < stock.Width; x++)
                {
                    int cx = x + c.ShiftX, cy = y + c.ShiftY;
                    if (cx < 0 || cy < 0 || cx >= c.Image.Width || cy >= c.Image.Height)
                        continue;
                    int i = y * stock.Width + x, o = cy * c.Image.Width + cx;
                    if (c.Sources[o] == CompositeTexel.Body)
                        Assert.Equal(stock.Pixels[i], c.Image.Pixels[o]);
                    if (c.Sources[o] == CompositeTexel.Body || c.Sources[o] == CompositeTexel.Fill)
                        Assert.True(stock.Opaque[i] != 0, $"{lump}: ({x}, {y}) drawn outside the stock lump");
                }
        }
    }

    [Fact]
    public void TheWeaponLiesInTheHands()
    {
        // Every front pose names where each visible hand holds the stock rifle (HoldPose.Hands).
        // The drawn weapon, measured before the hands are layered over it, comes within 2 texels
        // (texel centre to the hand point) of each of them: through both hands, not above or
        // below them. The pistol, one-handed, needs only the rear hand. With empty hands a fist
        // is drawn for each hand.
        var (set, _, _, _) = Load(TestWads.RequireDoom2());
        var misses = new List<string>();
        foreach ((weapontype_t weapon, string lump, HeldComposite c) in All(set))
        {
            HoldPose pose = set.Poses[lump];
            if (pose.Layer != HoldLayer.Front)
                continue;
            Assert.True(pose.Hands.Length is 1 or 2, $"{lump}: a front pose names its hands");
            if (weapon == weapontype_t.wp_fist)
            {
                // A fist a hand (21 texels each; two hands' fists may overlap).
                Assert.InRange(c.Sources.ToArray().Count(s => s == CompositeTexel.Fist), 21, pose.Hands.Length * 21);
                continue;
            }
            IEnumerable<(double X, double Y)> hands = weapon == weapontype_t.wp_pistol ? pose.Hands.Take(1) : pose.Hands;
            foreach ((double hx, double hy) in hands)
            {
                double best = double.MaxValue;
                for (int i = 0; i < c.WeaponCoverage.Length; i++)
                {
                    if (!c.WeaponCoverage[i])
                        continue;
                    double dx = i % c.Image.Width + 0.5 - c.ShiftX - hx, dy = i / c.Image.Width + 0.5 - c.ShiftY - hy;
                    best = Math.Min(best, Math.Sqrt(dx * dx + dy * dy));
                }
                if (best > 2)
                    misses.Add($"{weapon} {lump} hand ({hx}, {hy}): nearest weapon texel {best:F1}");
            }
        }
        Assert.True(misses.Count == 0, string.Join("\n", misses));
    }

    private static bool NextToRifle(HoldPose pose, int width, int x, int y)
    {
        int height = pose.Rifle.Length / width;
        for (int ny = y - 1; ny <= y + 1; ny++)
            for (int nx = x - 1; nx <= x + 1; nx++)
                if (nx >= 0 && ny >= 0 && nx < width && ny < height && pose.Rifle[ny * width + nx])
                    return true;
        return false;
    }

    [Fact]
    public void TheFistArtIsOursAndRampSafe()
    {
        IndexedImage fist = HeldWeaponComposer.Fist();
        Assert.Equal(5, fist.Width);
        Assert.Equal(5, fist.Height);
        int opaque = 0;
        for (int i = 0; i < fist.Pixels.Length; i++)
        {
            if (fist.Opaque[i] == 0)
                continue;
            opaque++;
            Assert.False(fist.Pixels[i] is >= HeldWeapons.RampFirst and <= HeldWeapons.RampLast);
        }
        Assert.Equal(21, opaque);
    }

    [Fact]
    public void TheMovedFlashTouchesTheWeapon()
    {
        var (set, _, _, _) = Load(TestWads.RequireDoom2());
        foreach ((weapontype_t weapon, string lump, HeldComposite c) in All(set))
        {
            // The side views of frame F, where the weapon shows in the open.
            if (weapon == weapontype_t.wp_fist || lump is not ("PLAYF2F8" or "PLAYF3F7"))
                continue;
            if (weapon == weapontype_t.wp_chainsaw)
            {
                // The chainsaw has no muzzle flash.
                Assert.DoesNotContain(c.Sources.ToArray(), s => s == CompositeTexel.Flash);
                continue;
            }
            int w = c.Image.Width, h = c.Image.Height;
            bool touches = false;
            for (int i = 0; i < c.Sources.Length && !touches; i++)
            {
                if (c.Sources[i] != CompositeTexel.Flash)
                    continue;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = i % w + dx, y = i / w + dy;
                        touches |= x >= 0 && y >= 0 && x < w && y < h && c.Sources[y * w + x] == CompositeTexel.Weapon;
                    }
            }
            Assert.True(touches, $"{weapon} {lump}: the flash floats off the muzzle");
        }
    }

    [Fact]
    public void FinalDoomGetsTheSameComposites()
    {
        var (a, _, _, _) = Load(TestWads.RequireDoom2());
        var (b, _, _, _) = Load(TestWads.RequireTnt());
        foreach ((weapontype_t weapon, string lump, HeldComposite ca) in All(a))
        {
            HeldComposite cb = b.Get(weapon, lump)!;
            Assert.Equal(ca.Image.Pixels, cb.Image.Pixels);
            Assert.Equal(ca.Image.LeftOffset, cb.Image.LeftOffset);
        }
    }

    [Fact]
    public void WritesTheReviewSheets()
    {
        string? dir = Environment.GetEnvironmentVariable(SheetEnvVar);
        Assert.SkipWhen(string.IsNullOrEmpty(dir), $"Set {SheetEnvVar}=DIR to write the review sheets.");
        Directory.CreateDirectory(dir!);
        bool any = false;
        foreach ((string name, string? path) in new[] { ("doom", TestWads.Doom2Path ?? TestWads.DoomPath ?? TestWads.Doom1Path),
            ("final", TestWads.TntPath ?? TestWads.PlutoniaPath) })
        {
            if (path is null)
                continue;
            var loaded = Load(path);
            CompositeSheet.WriteAll(Path.Combine(dir!, $"composites-{name}.png"), loaded.Set, loaded.Images, loaded.Palette, loaded.Wad, Path.GetFileName(path));
            Directory.CreateDirectory(Path.Combine(dir!, $"composites-{name}"));
            foreach (char frame in "ABCDEFG")
                CompositeSheet.WriteFrame(Path.Combine(dir!, $"composites-{name}", $"frame-{frame}.png"), frame, loaded.Set, loaded.Images, loaded.Palette, loaded.Wad, Path.GetFileName(path));
            if (name == "doom")
                CompositeSheet.WriteCompare(Path.Combine(dir!, "compare.png"), loaded.Set, loaded.Images, loaded.Palette, loaded.Wad);
            any = true;
        }
        Assert.True(any, "No IWAD to draw.");
    }
}

/// <summary>T6.14c: the cost of building every composite (SPEC §9's 1 s load budget).</summary>
[Collection(TimingGroup.Name)]
public class HeldWeaponSetCostTests(ITestOutputHelper output)
{
    [Fact]
    public void Doom2sCompositesBuildWellInsideTheLoadBudget()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom2());
        HeldWeaponSet.Build(wad); // the JIT
        double best = double.MaxValue;
        for (int i = 0; i < 3; i++)
            best = Math.Min(best, HeldWeaponSet.Build(wad).BuildMilliseconds);
        output.WriteLine($"HeldWeaponSet.Build(doom2.wad): {best:F1} ms for 315 composites");
        Assert.True(best < 400, $"{best:F1} ms");
    }
}

/// <summary>
/// The T6.14c review sheets: per lump a row, the stock lump first and then each weapon slot's
/// composite, all 4× on a mid grey, the origin's column and row marked by a tick under each cell.
/// </summary>
internal static class CompositeSheet
{
    private const int Zoom = 4, Pad = 8, LabelH = 22;
    private static readonly (byte R, byte G, byte B) _background = (96, 96, 96), _cell = (128, 128, 128);

    private static readonly weapontype_t[] _slots =
    [
        weapontype_t.wp_fist, weapontype_t.wp_pistol, weapontype_t.wp_shotgun, weapontype_t.wp_supershotgun,
        weapontype_t.wp_chaingun, weapontype_t.wp_missile, weapontype_t.wp_plasma, weapontype_t.wp_bfg, weapontype_t.wp_chainsaw,
    ];

    private static string Short(weapontype_t w) => w switch
    {
        weapontype_t.wp_fist => "EMPTY HANDS",
        weapontype_t.wp_supershotgun => "SSG",
        weapontype_t.wp_missile => "ROCKET",
        _ => w.ToString()[3..].ToUpperInvariant(),
    };

    // A cell's size and the stock origin's place in it, so every cell of a row lines its body up.
    private static (int W, int H, int OriginX, int OriginY) Layout(IEnumerable<IndexedImage> images)
    {
        int left = 0, right = 0, top = 0, bottom = 0;
        foreach (IndexedImage i in images)
        {
            left = Math.Max(left, i.LeftOffset);
            right = Math.Max(right, i.Width - i.LeftOffset);
            top = Math.Max(top, i.TopOffset);
            bottom = Math.Max(bottom, i.Height - i.TopOffset);
        }
        return (left + right, top + bottom, left, top);
    }

    private static List<IndexedImage> RowImages(HeldWeaponSet set, Dictionary<string, IndexedImage> images, string lump)
    {
        var row = new List<IndexedImage> { images[lump] };
        foreach (weapontype_t w in _slots)
            if (set.Get(w, lump) is HeldComposite c)
                row.Add(c.Image);
        return row;
    }

    public static void WriteAll(string path, HeldWeaponSet set, Dictionary<string, IndexedImage> images, byte[] palette, WadArchive wad, string title) =>
        Write(path, HoldPoses.Lumps, set, images, palette, wad, $"{title} COMPOSITES");

    public static void WriteFrame(string path, char frame, HeldWeaponSet set, Dictionary<string, IndexedImage> images, byte[] palette, WadArchive wad, string title) =>
        Write(path, [.. HoldPoses.Lumps.Where(l => l[4] == frame)], set, images, palette, wad, $"{title} FRAME {frame}");

    private static void Write(string path, IReadOnlyList<string> lumps, HeldWeaponSet set, Dictionary<string, IndexedImage> images, byte[] palette, WadArchive wad, string title)
    {
        var all = lumps.SelectMany(l => RowImages(set, images, l)).ToList();
        (int cw, int ch, int ox, int oy) = Layout(all);
        int columns = 1 + _slots.Count(set.Has);
        int cellW = cw * Zoom + Pad, rowH = ch * Zoom + LabelH + Pad;
        int width = Pad + columns * cellW, height = Pad + LabelH * 2 + lumps.Count * rowH;
        byte[] rgba = new byte[width * height * 4];
        Fill(rgba, width, 0, 0, width, height, _background);
        var font = new SheetFont(wad);
        font.Draw(rgba, width, Pad, Pad, title.ToUpperInvariant(), palette, 2);
        int y0 = Pad + LabelH * 2;
        foreach (string lump in lumps)
        {
            int x0 = Pad, col = 0;
            void Cell(IndexedImage image, string label)
            {
                Fill(rgba, width, x0, y0, cw * Zoom, ch * Zoom, _cell);
                Blit(rgba, width, x0 + (ox - image.LeftOffset) * Zoom, y0 + (oy - image.TopOffset) * Zoom, image, palette, Zoom);
                // The origin: a dark tick at the cell's foot under it.
                Fill(rgba, width, x0 + ox * Zoom - 1, y0 + ch * Zoom - 3, 2, 3, (32, 32, 32));
                font.Draw(rgba, width, x0, y0 + ch * Zoom + 2, label, palette, 2);
                x0 += cellW;
                col++;
            }
            Cell(images[lump], lump);
            foreach (weapontype_t w in _slots)
            {
                if (set.Has(w) && set.Get(w, lump) is HeldComposite c)
                    Cell(c.Image, Short(w));
            }
            y0 += rowH;
        }
        PngWriter.WriteRgba(path, width, height, rgba);
    }

    /// <summary>The mock-up's layout (<c>sheet-side.png</c>): a row idle (PLAYA2A8 mirrored, facing right), a row firing (PLAYF3F7 mirrored) and one from the front (PLAYA1), a column per weapon, the stock first.</summary>
    private const int CompareZoom = 4;

    public static void WriteCompare(string path, HeldWeaponSet set, Dictionary<string, IndexedImage> images, byte[] palette, WadArchive wad)
    {
        weapontype_t[] order =
[
    weapontype_t.wp_supershotgun, weapontype_t.wp_shotgun, weapontype_t.wp_chaingun, weapontype_t.wp_missile,
            weapontype_t.wp_plasma, weapontype_t.wp_bfg, weapontype_t.wp_chainsaw, weapontype_t.wp_pistol, weapontype_t.wp_fist,
        ];
        (string Lump, bool Mirror, string Name)[] rows = [("PLAYA2A8", true, "IDLE"), ("PLAYF3F7", true, "FIRE"), ("PLAYA1", false, "FRONT")];
        var all = rows.SelectMany(r => RowImages(set, images, r.Lump).Select(i => r.Mirror ? Mirror(i) : i)).ToList();
        (int cw, int ch, int ox, int oy) = Layout(all);
        cw += 8;
        ch += 8;
        int cellW = cw * CompareZoom + Pad, rowH = ch * CompareZoom + LabelH + Pad;
        int width = Pad + (1 + order.Length) * cellW, height = Pad + rows.Length * rowH;
        byte[] rgba = new byte[width * height * 4];
        Fill(rgba, width, 0, 0, width, height, _background);
        var font = new SheetFont(wad);
        int y0 = Pad;
        foreach ((string lump, bool mirror, string name) in rows)
        {
            int x0 = Pad;
            void Cell(IndexedImage image, string label)
            {
                if (mirror)
                    image = Mirror(image);
                Fill(rgba, width, x0, y0, cw * CompareZoom, ch * CompareZoom, _cell);
                Blit(rgba, width, x0 + (ox + 4 - image.LeftOffset) * CompareZoom, y0 + (oy + 4 - image.TopOffset) * CompareZoom, image, palette, CompareZoom);
                font.Draw(rgba, width, x0, y0 + ch * CompareZoom + 2, label, palette, 2);
                x0 += cellW;
            }
            Cell(images[lump], $"STOCK {name}");
            foreach (weapontype_t w in order)
            {
                if (set.Get(w, lump) is HeldComposite c)
                    Cell(c.Image, $"{Short(w)} {name}");
            }
            y0 += rowH;
        }
        PngWriter.WriteRgba(path, width, height, rgba);
    }

    private static IndexedImage Mirror(IndexedImage m)
    {
        byte[] pixels = new byte[m.Pixels.Length], opaque = new byte[m.Pixels.Length];
        for (int y = 0; y < m.Height; y++)
            for (int x = 0; x < m.Width; x++)
            {
                pixels[y * m.Width + x] = m[m.Width - 1 - x, y];
                opaque[y * m.Width + x] = m.Opaque[y * m.Width + m.Width - 1 - x];
            }
        return new IndexedImage(m.Width, m.Height, m.Width - m.LeftOffset, m.TopOffset, pixels, opaque);
    }

    internal static void Fill(byte[] rgba, int width, int x0, int y0, int w, int h, (byte R, byte G, byte B) c)
    {
        int height = rgba.Length / 4 / width;
        for (int y = Math.Max(0, y0); y < Math.Min(height, y0 + h); y++)
            for (int x = Math.Max(0, x0); x < Math.Min(width, x0 + w); x++)
            {
                int o = (y * width + x) * 4;
                rgba[o] = c.R;
                rgba[o + 1] = c.G;
                rgba[o + 2] = c.B;
                rgba[o + 3] = 255;
            }
    }

    internal static void Blit(byte[] rgba, int width, int x0, int y0, IndexedImage img, byte[] palette, int zoom)
    {
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                if (!img.IsOpaque(x, y))
                    continue;
                int p = img[x, y];
                Fill(rgba, width, x0 + x * zoom, y0 + y * zoom, zoom, zoom, (palette[p * 3], palette[p * 3 + 1], palette[p * 3 + 2]));
            }
    }

    // The HUD font (STCFN033–STCFN095), for the labels.
    private sealed class SheetFont(WadArchive wad)
    {
        private readonly IndexedImage?[] _glyphs = Glyphs(wad);

        private static IndexedImage?[] Glyphs(WadArchive wad)
        {
            var glyphs = new IndexedImage?[96];
            for (int c = 33; c < 96; c++)
                glyphs[c] = wad.Find($"STCFN{c:D3}") is { } lump ? Patch.Decode(lump.Data.Span, lump.Name) : null;
            return glyphs;
        }

        public void Draw(byte[] rgba, int width, int x, int y, string text, byte[] palette, int zoom)
        {
            foreach (char ch in text)
            {
                int c = ch;
                if (c < 96 && _glyphs[c] is IndexedImage g)
                {
                    Blit(rgba, width, x, y, g, palette, zoom);
                    x += (g.Width + 1) * zoom;
                }
                else
                {
                    x += 4 * zoom;
                }
            }
        }
    }
}
