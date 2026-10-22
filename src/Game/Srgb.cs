using System;

namespace IsoDoom.Game;

/// <summary>
/// sRGB and linear colour (T8.4d), for the canvas in linear colour under
/// Godot's Mobile renderer (<see cref="LinearCanvas"/>, set by
/// <c>ScreenCapture.UseExactColours</c>), with the screen's own conversion's
/// rounding.
/// </summary>
public static class Srgb
{
    /// <summary>
    /// Linear values at which each sRGB byte rounds up to the next: byte v for
    /// x in [<c>_thresholds[v - 1]</c>, <c>_thresholds[v]</c>).
    /// </summary>
    private static readonly double[] _thresholds = BuildThresholds();

    /// <summary>
    /// Whether the root viewport has HDR 2D: 2D drawing is then in linear
    /// colour, so our canvas shaders that blend or output sRGB values
    /// themselves (the menus' dim, the sharp bilinear filter, the melt)
    /// convert, to draw as without it.
    /// </summary>
    public static bool LinearCanvas { get; set; }

    /// <summary>The linear value of sRGB value <paramref name="s"/> (0–1).</summary>
    public static double ToLinear(double s) => s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);

    /// <summary>The sRGB byte of linear value <paramref name="x"/>, rounded as the screen's 8-bit conversion rounds.</summary>
    public static byte ToByte(double x)
    {
        int index = Array.BinarySearch(_thresholds, x);
        // Not found: the complement of the first threshold above x, so the count of thresholds below it.
        return (byte)(index >= 0 ? index + 1 : ~index);
    }

    private static double[] BuildThresholds()
    {
        double[] t = new double[255];
        for (int v = 0; v < t.Length; v++)
            t[v] = ToLinear((v + 0.5) / 255.0);
        return t;
    }
}
