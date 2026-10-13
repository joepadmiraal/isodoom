using System;
using System.Globalization;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Render;

/// <summary>Where the palette flashes show (T6.13v, <c>--level-palette-effects</c>).</summary>
public enum PaletteFlashStyle
{
    /// <summary>The whole view, as vanilla's (at full strength, vanilla's palettes exactly).</summary>
    Screen,

    /// <summary>On and around the player only: its own sprite in the flash palette (a pickup's white, not gold) and a glow on the floor at its feet; the view stays on palette 0.</summary>
    Player,

    /// <summary>None, and no power-up colormaps either (the photosensitivity switch).</summary>
    Off,
}

/// <summary>
/// Gentler palette flashes (T6.13v, SPEC §12; a presentation option, not
/// vanilla): st_stuff.c's flash palette (<c>ST_doPaletteStuff</c>, unchanged)
/// weakened by a strength, a palette blended from palette 0 towards it
/// (<see cref="Blend(ReadOnlySpan{byte}, ReadOnlySpan{byte}, int, Span{byte})"/>),
/// and the <see cref="PaletteFlashStyle.Player"/> style's palette
/// (<see cref="PlayerPalette"/>) and glow (<see cref="GlowOf"/>), in which a
/// pickup's flash is white instead of gold. Plain C# (no Godot types), so it is unit tested.
/// </summary>
public static class PaletteFlash
{
    /// <summary>Full strength, in percent: the flash palette itself (vanilla's).</summary>
    public const int FullStrength = 100;

    /// <summary>The weakest strength, in percent.</summary>
    public const int MinStrength = 10;

    /// <summary>The <see cref="PaletteFlashStyle.Player"/> style's pickup colour (sRGB): white, not the gold palettes' tint (the user's pick).</summary>
    public static readonly (byte R, byte G, byte B) PickupTint = (255, 255, 255);

    /// <summary>The radiation suit's glow level (its single palette has no steps).</summary>
    public const float RadiationLevel = 0.25f;

    /// <summary>Parses <c>--level-palette-effects</c>'s and <c>gameplay/palette_effects</c>' value (<c>on</c>, before T6.13v: the screen).</summary>
    public static PaletteFlashStyle Parse(string value, string what) => value switch
    {
        "screen" or "on" or "vanilla" => PaletteFlashStyle.Screen,
        "player" => PaletteFlashStyle.Player,
        "off" => PaletteFlashStyle.Off,
        _ => throw new ArgumentException($"{what}: \"{value}\" (screen, player or off)"),
    };

    /// <summary>The style's name, as the setting and the argument take it.</summary>
    public static string Name(PaletteFlashStyle style) => style.ToString().ToLowerInvariant();

    /// <summary>Parses <c>--level-flash-strength</c>'s and <c>gameplay/flash_strength</c>' value: a percentage, <see cref="MinStrength"/>–<see cref="FullStrength"/>.</summary>
    public static int ParseStrength(string value, string what) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v >= MinStrength && v <= FullStrength
            ? v
            : throw new ArgumentException($"{what}: \"{value}\" (a percentage, {MinStrength} to {FullStrength})");

    /// <summary>
    /// Palette <paramref name="normal"/> blended towards <paramref name="flash"/>
    /// (768 bytes each) by <paramref name="strength"/> percent, each byte
    /// rounded to the nearest (so at <see cref="FullStrength"/> the flash
    /// palette exactly, at 0 the normal one).
    /// </summary>
    public static void Blend(ReadOnlySpan<byte> normal, ReadOnlySpan<byte> flash, int strength, Span<byte> into)
    {
        int s = Math.Clamp(strength, 0, FullStrength);
        for (int i = 0; i < Playpal.PaletteSize; i++)
            into[i] = (byte)((normal[i] * (FullStrength - s) + flash[i] * s + FullStrength / 2) / FullStrength);
    }

    /// <summary>PLAYPAL palette <paramref name="palette"/> blended from palette 0 by <paramref name="strength"/> percent (<see cref="Blend(ReadOnlySpan{byte}, ReadOnlySpan{byte}, int, Span{byte})"/>).</summary>
    public static byte[] Blend(Playpal playpal, int palette, int strength)
    {
        var into = new byte[Playpal.PaletteSize];
        Blend(playpal.GetPalette(0), playpal.GetPalette(Math.Clamp(palette, 0, playpal.Count - 1)), strength, into);
        return into;
    }

    /// <summary>The <see cref="PaletteFlashStyle.Player"/> style's glow: its colour (sRGB bytes) and level (0–1, before the strength).</summary>
    public readonly record struct Glow(byte R, byte G, byte B, float Level);

    /// <summary>Whether <paramref name="palette"/> is one of the gold pickup palettes (st_stuff.c <c>STARTBONUSPALS</c>).</summary>
    public static bool IsBonus(int palette) => palette is >= Playpal.STARTBONUSPALS and < Playpal.STARTBONUSPALS + Playpal.NUMBONUSPALS;

    /// <summary>
    /// The palette the player's own sprite is drawn in with the
    /// <see cref="PaletteFlashStyle.Player"/> style: as
    /// <see cref="Blend(Playpal, int, int)"/>, but a pickup's gold palette
    /// becomes palette 0 moved towards <see cref="PickupTint"/> by the same
    /// fraction as the gold palette moves towards gold (<see cref="Tint"/>),
    /// times <paramref name="strength"/> percent, each byte rounded.
    /// </summary>
    public static byte[] PlayerPalette(Playpal playpal, int palette, int strength)
    {
        if (!IsBonus(palette) || palette >= playpal.Count)
            return Blend(playpal, palette, strength);
        var into = new byte[Playpal.PaletteSize];
        ReadOnlySpan<byte> p0 = playpal.GetPalette(0);
        double f = Tint(playpal, palette).F * Math.Clamp(strength, 0, FullStrength) / FullStrength;
        ReadOnlySpan<byte> tint = [PickupTint.R, PickupTint.G, PickupTint.B];
        for (int i = 0; i < Playpal.PaletteSize; i++)
            into[i] = (byte)Math.Clamp(Math.Round(p0[i] + (tint[i % 3] - p0[i]) * f), 0, 255);
        return into;
    }

    /// <summary>
    /// The glow for flash palette <paramref name="palette"/>: red 1–8 at level
    /// n/8, gold 9–12 at (n−8)/4 (in <see cref="PickupTint"/>, white), the
    /// radiation suit's 13 at <see cref="RadiationLevel"/>; none (level 0) for
    /// palette 0, one the WAD lacks, or one that does not differ from palette
    /// 0. The red and green are the colours the palettes tint towards, from
    /// PLAYPAL itself (so a PWAD's changes them as it would vanilla's flashes;
    /// <see cref="Tint"/>).
    /// </summary>
    public static Glow GlowOf(Playpal playpal, int palette)
    {
        if (palette <= 0 || palette >= playpal.Count)
            return default;
        float level = palette switch
        {
            >= Playpal.STARTREDPALS and < Playpal.STARTREDPALS + Playpal.NUMREDPALS => (float)palette / Playpal.NUMREDPALS,
            >= Playpal.STARTBONUSPALS and < Playpal.STARTBONUSPALS + Playpal.NUMBONUSPALS => (float)(palette - Playpal.STARTBONUSPALS + 1) / Playpal.NUMBONUSPALS,
            Playpal.RADIATIONPAL => RadiationLevel,
            _ => 0f,
        };
        if (level <= 0)
            return default;
        (byte R, byte G, byte B, double F) t = Tint(playpal, palette);
        if (t.F < 0.02)
            return default; // (no tint to show)
        return IsBonus(palette) ? new Glow(PickupTint.R, PickupTint.G, PickupTint.B, level) : new Glow(t.R, t.G, t.B, level);
    }

    /// <summary>
    /// The colour flash palette <paramref name="palette"/> tints towards and
    /// by how much: st_stuff.c's palettes are palette 0 moved a fraction f
    /// towards a colour T, each channel <c>p = (1 − f) p0 + f T</c>, fitted
    /// over the 256 colours by least squares.
    /// </summary>
    private static (byte R, byte G, byte B, double F) Tint(Playpal playpal, int palette)
    {
        ReadOnlySpan<byte> p0 = playpal.GetPalette(0), pn = playpal.GetPalette(palette);
        Span<double> slope = stackalloc double[3], intercept = stackalloc double[3];
        for (int c = 0; c < 3; c++)
        {
            double sx = 0, sy = 0, sxx = 0, sxy = 0;
            for (int i = 0; i < 256; i++)
            {
                double x = p0[i * 3 + c], y = pn[i * 3 + c];
                sx += x;
                sy += y;
                sxx += x * x;
                sxy += x * y;
            }
            double variance = sxx - sx * sx / 256;
            slope[c] = variance > 0 ? (sxy - sx * sy / 256) / variance : 1;
            intercept[c] = (sy - slope[c] * sx) / 256;
        }
        double f = 1 - (slope[0] + slope[1] + slope[2]) / 3;
        if (f < 0.02)
            return (0, 0, 0, f);
        static byte Channel(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
        return (Channel(intercept[0] / f), Channel(intercept[1] / f), Channel(intercept[2] / f), f);
    }
}
