using System;
using IsoDoom.Render;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Render;

/// <summary>T6.13v: the gentler palette flashes' blend, glow and settings (<see cref="PaletteFlash"/>).</summary>
public class PaletteFlashTests
{
    private static readonly (byte R, byte G, byte B) _red = (255, 0, 0), _gold = (215, 186, 69), _green = (0, 255, 0);

    // A PLAYPAL made as st_stuff.c's palettes were: palette 0 moved a fraction f towards a tint
    // (red n/9 for 1–8, gold (n−8)/8 for 9–12, green 1/8 for 13), every colour of palette 0 different.
    private static Playpal MakePlaypal((byte R, byte G, byte B) red, (byte R, byte G, byte B) gold, (byte R, byte G, byte B) green)
    {
        var data = new byte[Playpal.NumPalettes * Playpal.PaletteSize];
        for (int i = 0; i < 256; i++)
        {
            data[i * 3] = (byte)i;
            data[i * 3 + 1] = (byte)(i * 7 % 256);
            data[i * 3 + 2] = (byte)(255 - i);
        }
        for (int n = 1; n < Playpal.NumPalettes; n++)
        {
            ((byte R, byte G, byte B) tint, double f) = n switch
            {
                <= 8 => (red, n / 9.0),
                <= 12 => (gold, (n - 8) / 8.0),
                _ => (green, 1 / 8.0),
            };
            for (int i = 0; i < 256; i++)
            {
                for (int c = 0; c < 3; c++)
                {
                    byte p0 = data[i * 3 + c], t = c == 0 ? tint.R : c == 1 ? tint.G : tint.B;
                    data[n * Playpal.PaletteSize + i * 3 + c] = (byte)Math.Round(p0 + (t - p0) * f);
                }
            }
        }
        return Playpal.Decode(data);
    }

    private static void AssertNear((byte R, byte G, byte B) expected, PaletteFlash.Glow got, int tolerance)
    {
        Assert.InRange(got.R, expected.R - tolerance, expected.R + tolerance);
        Assert.InRange(got.G, expected.G - tolerance, expected.G + tolerance);
        Assert.InRange(got.B, expected.B - tolerance, expected.B + tolerance);
    }

    [Fact]
    public void BlendRunsFromPaletteZeroToTheFlash()
    {
        Playpal playpal = MakePlaypal(_red, _gold, _green);
        Assert.Equal(playpal.GetPalette(4).ToArray(), PaletteFlash.Blend(playpal, 4, 100));
        Assert.Equal(playpal.GetPalette(0).ToArray(), PaletteFlash.Blend(playpal, 4, 0));
        byte[] half = PaletteFlash.Blend(playpal, 4, 50);
        ReadOnlySpan<byte> p0 = playpal.GetPalette(0), p4 = playpal.GetPalette(4);
        for (int i = 0; i < Playpal.PaletteSize; i++)
            Assert.Equal((p0[i] * 50 + p4[i] * 50 + 50) / 100, half[i]);
        Assert.Equal(playpal.GetPalette(0).ToArray(), PaletteFlash.Blend(playpal, 0, 30));
    }

    [Fact]
    public void GlowTakesItsColourFromPlaypalAndItsLevelFromThePaletteNumber()
    {
        Playpal playpal = MakePlaypal(_red, _gold, _green);
        Assert.Equal(default, PaletteFlash.GlowOf(playpal, 0));
        Assert.Equal(default, PaletteFlash.GlowOf(playpal, 14)); // (not in the WAD)
        for (int n = 1; n <= 8; n++)
        {
            PaletteFlash.Glow g = PaletteFlash.GlowOf(playpal, n);
            AssertNear(_red, g, 3);
            Assert.Equal(n / 8f, g.Level);
        }
        for (int n = 9; n <= 12; n++)
        {
            PaletteFlash.Glow g = PaletteFlash.GlowOf(playpal, n);
            Assert.Equal(PaletteFlash.PickupTint, (g.R, g.G, g.B)); // white, not gold
            Assert.Equal((n - 8) / 4f, g.Level);
        }
        PaletteFlash.Glow suit = PaletteFlash.GlowOf(playpal, 13);
        AssertNear(_green, suit, 4);
        Assert.Equal(PaletteFlash.RadiationLevel, suit.Level);
    }

    [Fact]
    public void APwadsPlaypalChangesTheGlow()
    {
        Playpal playpal = MakePlaypal((0, 0, 255), (0, 255, 255), (255, 0, 255));
        AssertNear((0, 0, 255), PaletteFlash.GlowOf(playpal, 5), 3);
        AssertNear((255, 0, 255), PaletteFlash.GlowOf(playpal, 13), 4);

        // Flash palettes no different from palette 0: no glow.
        var same = new byte[Playpal.NumPalettes * Playpal.PaletteSize];
        for (int n = 0; n < Playpal.NumPalettes; n++)
            MakePlaypal(_red, _gold, _green).GetPalette(0).CopyTo(same.AsSpan(n * Playpal.PaletteSize));
        Assert.Equal(0f, PaletteFlash.GlowOf(Playpal.Decode(same), 8).Level);
    }

    [Fact]
    public void Doom1sFlashesGlowRedGoldAndGreen()
    {
        Playpal playpal = Playpal.Load(WadArchive.Open(TestWads.RequireDoom1()));
        PaletteFlash.Glow red = PaletteFlash.GlowOf(playpal, 8), gold = PaletteFlash.GlowOf(playpal, 12), suit = PaletteFlash.GlowOf(playpal, 13);
        AssertNear(_red, red, 3);
        Assert.Equal(PaletteFlash.PickupTint, (gold.R, gold.G, gold.B));
        Assert.True(suit.G > 200 && suit.R < 40 && suit.B < 40, $"the suit's glow {suit}");
    }

    [Fact]
    public void ThePlayersPickupPaletteIsWhiteByTheGoldPalettesFraction()
    {
        Playpal playpal = MakePlaypal(_red, _gold, _green);
        Assert.Equal(PaletteFlash.Blend(playpal, 4, 50), PaletteFlash.PlayerPalette(playpal, 4, 50)); // red as the screen's
        ReadOnlySpan<byte> p0 = playpal.GetPalette(0);
        foreach ((int n, int strength) in new[] { (9, 100), (12, 100), (12, 50) })
        {
            byte[] white = PaletteFlash.PlayerPalette(playpal, n, strength);
            double f = (n - 8) / 8.0 * strength / 100;
            for (int i = 0; i < Playpal.PaletteSize; i++)
                Assert.InRange(white[i], p0[i] + (255 - p0[i]) * f - 1.5, p0[i] + (255 - p0[i]) * f + 1.5);
        }
    }

    [Fact]
    public void StyleAndStrengthParse()
    {
        Assert.Equal(PaletteFlashStyle.Screen, PaletteFlash.Parse("screen", "x"));
        Assert.Equal(PaletteFlashStyle.Screen, PaletteFlash.Parse("on", "x")); // before T6.13v
        Assert.Equal(PaletteFlashStyle.Player, PaletteFlash.Parse("player", "x"));
        Assert.Equal(PaletteFlashStyle.Off, PaletteFlash.Parse("off", "x"));
        Assert.Contains("--level-palette-effects", Assert.Throws<ArgumentException>(() => PaletteFlash.Parse("dim", "--level-palette-effects")).Message);
        Assert.Equal("player", PaletteFlash.Name(PaletteFlashStyle.Player));
        Assert.Equal(10, PaletteFlash.ParseStrength("10", "x"));
        Assert.Equal(55, PaletteFlash.ParseStrength("55", "x"));
        Assert.Equal(100, PaletteFlash.ParseStrength("100", "x"));
        foreach (string bad in new[] { "0", "9", "101", "half", "" })
            Assert.Contains("--level-flash-strength", Assert.Throws<ArgumentException>(() => PaletteFlash.ParseStrength(bad, "--level-flash-strength")).Message);
    }
}
