using System;
using System.IO;
using IsoDoom.Render;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Render;

/// <summary>T6.14b: the weapons the players' sprites hold (<see cref="HeldWeapons"/>).</summary>
public class HeldWeaponTests
{
    /// <summary>Set to a directory to write the review sheet (<c>weapons.png</c>) there: WAD-derived, never commit it.</summary>
    public const string SheetEnvVar = "ISODOOM_WEAPON_SHEET";

    private static HeldWeapon?[] Load(string path, out byte[] palette)
    {
        var wad = WadArchive.Open(path);
        palette = Playpal.Load(wad).GetPalette(0).ToArray();
        return HeldWeapons.Load(wad, Playpal.Load(wad).Data.ToArray());
    }

    [Fact]
    public void ThePickupTableIsTheHudsToo()
    {
        Assert.Equal((int)weapontype_t.NUMWEAPONS, HeldWeapons.PickupSprites.Count);
        Assert.Null(HeldWeapons.PickupSprites[(int)weapontype_t.wp_fist]);
        Assert.Null(HeldWeapons.PickupSprites[(int)weapontype_t.wp_pistol]);
        Assert.Equal("SGN2A0", HeldWeapons.PickupSprites[(int)weapontype_t.wp_supershotgun]);
    }

    [Fact]
    public void ThePistolArtIsOursAndRampSafe()
    {
        IndexedImage pistol = HeldWeapons.Pistol();
        Assert.Equal(15, pistol.Width);
        Assert.Equal(8, pistol.Height);
        int opaque = 0;
        for (int i = 0; i < pistol.Pixels.Length; i++)
        {
            if (pistol.Opaque[i] == 0)
                continue;
            opaque++;
            Assert.False(pistol.Pixels[i] is >= HeldWeapons.RampFirst and <= HeldWeapons.RampLast);
        }
        // The slide's rows are solid end to end, the grip's corner below is clear.
        for (int x = 1; x < 15; x++)
        {
            Assert.True(pistol.IsOpaque(x, 1));
            Assert.True(pistol.IsOpaque(x, 2));
        }
        Assert.False(pistol.IsOpaque(14, 7));
        Assert.False(pistol.IsOpaque(0, 0));
        Assert.InRange(opaque, 50, 80);
    }

    [Fact]
    public void TheRampMapsOutOfTheRamp()
    {
        byte[] palette = new byte[768];
        for (int i = 0; i < 256; i++)
        {
            palette[i * 3] = (byte)i;
            palette[i * 3 + 1] = (byte)(255 - i);
            palette[i * 3 + 2] = 7;
        }
        byte[] map = HeldWeapons.RampRemap(palette);
        for (int i = 0; i < 256; i++)
        {
            if (i is >= 0x70 and <= 0x7f)
                Assert.Equal(i < 0x78 ? 0x6f : 0x80, map[i]); // the nearest, by RGB distance
            else
                Assert.Equal(i, map[i]);
        }
    }

    [Fact]
    public void SharewareHasNoPlasmaRifleBfgOrSuperShotgun()
    {
        HeldWeapon?[] weapons = Load(TestWads.RequireDoom1(), out _);
        Assert.Null(weapons[(int)weapontype_t.wp_fist]);
        Assert.Null(weapons[(int)weapontype_t.wp_plasma]);
        Assert.Null(weapons[(int)weapontype_t.wp_bfg]);
        Assert.Null(weapons[(int)weapontype_t.wp_supershotgun]);
        foreach (weapontype_t w in new[] { weapontype_t.wp_pistol, weapontype_t.wp_shotgun, weapontype_t.wp_chaingun, weapontype_t.wp_missile, weapontype_t.wp_chainsaw })
            Assert.NotNull(weapons[(int)w]);
        AssertSane(weapons);
    }

    [Fact]
    public void DoomTwoHasAllEight()
    {
        HeldWeapon?[] weapons = Load(TestWads.RequireDoom2(), out _);
        Assert.Null(weapons[(int)weapontype_t.wp_fist]);
        for (int i = 1; i < weapons.Length; i++)
            Assert.NotNull(weapons[i]);
        Assert.Equal(HeldWeapons.PistolName, weapons[(int)weapontype_t.wp_pistol]!.Source);
        Assert.Equal("BFUGA0", weapons[(int)weapontype_t.wp_bfg]!.Source);
        AssertSane(weapons);
    }

    [Theory]
    [InlineData("doom")]
    [InlineData("tnt")]
    [InlineData("plutonia")]
    public void EveryOtherIwadHasAllEight(string iwad)
    {
        string path = iwad == "doom" ? TestWads.RequireDoom() : TestWads.RequireFinalDoom(iwad);
        HeldWeapon?[] weapons = Load(path, out _);
        // The registered and Ultimate Doom have no super shotgun.
        for (int i = 1; i < weapons.Length; i++)
            Assert.Equal(iwad != "doom" || i != (int)weapontype_t.wp_supershotgun, weapons[i] is not null);
        AssertSane(weapons);
    }

    // No texel in the green ramp, the anchors inside the image, the muzzle right of the grip, a sensible scale.
    private static void AssertSane(HeldWeapon?[] weapons)
    {
        for (int i = 0; i < weapons.Length; i++)
        {
            if (weapons[i] is not HeldWeapon w)
                continue;
            Assert.Equal((weapontype_t)i, w.Weapon);
            IndexedImage img = w.Image;
            for (int t = 0; t < img.Pixels.Length; t++)
                if (img.Opaque[t] != 0)
                    Assert.False(img.Pixels[t] is >= 0x70 and <= 0x7f, $"{w.Source}: texel {t} is {img.Pixels[t]:X2}, in the ramp");
            Assert.InRange(w.Grip.X, 0, img.Width);
            Assert.InRange(w.Grip.Y, 0, img.Height);
            Assert.InRange(w.Muzzle.X, 0, img.Width);
            Assert.InRange(w.Muzzle.Y, 0, img.Height);
            Assert.True(w.Muzzle.X > w.Grip.X, $"{w.Source}: the muzzle points right of the grip");
            Assert.InRange(w.Scale, 0.3, 1);
            Assert.InRange(w.Length * w.Scale, 8, 40); // texels of a 56-unit marine
        }
    }

    [Fact]
    public void ReviewSheet()
    {
        string? dir = Environment.GetEnvironmentVariable(SheetEnvVar);
        Assert.SkipWhen(string.IsNullOrEmpty(dir), $"set {SheetEnvVar}=DIR to write the review sheet");
        var wad = WadArchive.Open(TestWads.RequireDoom2());
        byte[] palette = Playpal.Load(wad).GetPalette(0).ToArray();
        HeldWeapon?[] weapons = HeldWeapons.Load(wad, Playpal.Load(wad).Data.ToArray());
        Directory.CreateDirectory(dir!);
        WeaponSheet.Write(Path.Combine(dir!, "weapons.png"), wad, palette, weapons);
    }
}

/// <summary>
/// The T6.14b review sheet: a row per weapon: its source enlarged with the grip
/// (green) and muzzle (red) marked, then the marine's side view (PLAYF3F7
/// flipped to face right, as the weapons do) alone and with the weapon at its
/// scale pasted over its hands (the stock rifle still there: a size check, not
/// the composite, which is T6.14c's), then the weapon at 0°, 25° and 60° up and
/// at 0° foreshortened to 0.4, every cell at the same zoom.
/// </summary>
internal static class WeaponSheet
{
    private const int Zoom = 5, SourceZoom = 8, CellW = 76, CellH = 56, Pad = 6, LabelH = 12;
    private static readonly (byte R, byte G, byte B) _background = (96, 96, 96), _cell = (112, 112, 112);

    public static void Write(string path, WadArchive wad, byte[] palette, HeldWeapon?[] weapons)
    {
        IndexedImage? marine = Sprite(wad, "PLAYF3F7") is IndexedImage m ? Flip(m) : null;
        double[] angles = [0, 25, 60, 0];
        double[] alongs = [1, 1, 1, 0.4];
        int sourceW = 64 * SourceZoom, cellPx = CellW * Zoom;
        int width = Pad + sourceW + Pad + 2 * (52 * Zoom + Pad) + angles.Length * (cellPx + Pad);
        int rowH = Math.Max(CellH * Zoom, 36 * SourceZoom) + LabelH * 2 + Pad;
        int count = 0;
        foreach (HeldWeapon? w in weapons)
            if (w is not null)
                count++;
        int height = Pad + LabelH * 2 + count * (rowH + Pad);
        byte[] rgba = new byte[width * height * 4];
        Fill(rgba, width, 0, 0, width, height, _background);

        var font = new Font(wad);
        string[] heads = ["SOURCE, GRIP GREEN, MUZZLE RED", "PLAYF3F7 FLIPPED", "WEAPON OVER IT", "0 DEG", "25 DEG", "60 DEG", "0 DEG, ALONG 0.4"];
        int[] headX = new int[heads.Length];
        headX[0] = Pad;
        headX[1] = Pad + sourceW + Pad;
        headX[2] = headX[1] + 52 * Zoom + Pad;
        for (int i = 0; i < angles.Length; i++)
            headX[3 + i] = headX[2] + 52 * Zoom + Pad + i * (cellPx + Pad);
        for (int i = 0; i < heads.Length; i++)
            font.Draw(rgba, width, headX[i], Pad, heads[i], palette, 2);

        int y0 = Pad + LabelH * 2;
        foreach (HeldWeapon? weapon in weapons)
        {
            if (weapon is not HeldWeapon w)
                continue;
            font.Draw(rgba, width, Pad, y0, $"{w.Weapon} {w.Source} SCALE {w.Scale:F2} LENGTH {w.Length * w.Scale:F1}".ToUpperInvariant(), palette, 2);
            int top = y0 + LabelH * 2;
            // The source, enlarged, with its anchors.
            Blit(rgba, width, Pad, top, w.Image, palette, SourceZoom);
            Dot(rgba, width, Pad + (int)(w.Grip.X * SourceZoom), top + (int)(w.Grip.Y * SourceZoom), (0, 255, 0));
            Dot(rgba, width, Pad + (int)(w.Muzzle.X * SourceZoom), top + (int)(w.Muzzle.Y * SourceZoom), (255, 0, 0));
            if (marine is not null)
            {
                Blit(rgba, width, headX[1], top, marine, palette, Zoom);
                Blit(rgba, width, headX[2], top, marine, palette, Zoom);
                (double X, double Y) hand = (_hand.X, _hand.Y), tip = (_hand.X + w.Length * w.Scale, _hand.Y);
                Blit(rgba, width, headX[2], top, PixelArt.Transform(w.Image, w.Grip, w.Muzzle, hand, tip, 1, marine.Width, marine.Height), palette, Zoom);
            }
            for (int i = 0; i < angles.Length; i++)
            {
                int x0 = headX[3 + i];
                Fill(rgba, width, x0, top, cellPx, CellH * Zoom, _cell);
                (double X, double Y) from = (20, 34);
                double a = angles[i] * Math.PI / 180, len = w.Length * w.Scale;
                (double X, double Y) to = (from.X + len * Math.Cos(a), from.Y - len * Math.Sin(a));
                IndexedImage drawn = PixelArt.Transform(w.Image, w.Grip, w.Muzzle, from, to, alongs[i], CellW, CellH);
                Blit(rgba, width, x0, top, drawn, palette, Zoom);
                (double X, double Y) muzzle = PixelArt.Map(w.Muzzle, w.Grip, w.Muzzle, from, to, alongs[i]);
                Dot(rgba, width, x0 + (int)(from.X * Zoom), top + (int)(from.Y * Zoom), (0, 255, 0));
                Dot(rgba, width, x0 + (int)(muzzle.X * Zoom), top + (int)(muzzle.Y * Zoom), (255, 0, 0));
            }
            y0 += rowH + Pad;
        }
        PngWriter.WriteRgba(path, width, height, rgba);
    }

    /// <summary>Where the flipped PLAYF3F7's rear hand holds the stock rifle (by eye).</summary>
    private static readonly (double X, double Y) _hand = (26, 21);

    private static IndexedImage Flip(IndexedImage m)
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

    private static IndexedImage? Sprite(WadArchive wad, string name) =>
        wad.Find(name) is { } lump ? Patch.Decode(lump.Data.Span, lump.Name) : null;

    private static void Fill(byte[] rgba, int width, int x0, int y0, int w, int h, (byte R, byte G, byte B) c)
    {
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                int o = (y * width + x) * 4;
                rgba[o] = c.R;
                rgba[o + 1] = c.G;
                rgba[o + 2] = c.B;
                rgba[o + 3] = 255;
            }
    }

    private static void Blit(byte[] rgba, int width, int x0, int y0, IndexedImage img, byte[] palette, int zoom)
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

    private static void Dot(byte[] rgba, int width, int x, int y, (byte R, byte G, byte B) c)
    {
        Fill(rgba, width, x - 2, y - 2, 5, 5, (0, 0, 0));
        Fill(rgba, width, x - 1, y - 1, 3, 3, c);
    }

    // The HUD font (STCFN033–STCFN095), for the labels.
    private sealed class Font(WadArchive wad)
    {
        private readonly IndexedImage?[] _glyphs = Glyphs(wad);

        private static IndexedImage?[] Glyphs(WadArchive wad)
        {
            var glyphs = new IndexedImage?[96];
            for (int c = 33; c < 96; c++)
                glyphs[c] = Sprite(wad, $"STCFN{c:D3}");
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
