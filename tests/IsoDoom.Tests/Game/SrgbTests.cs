using System;
using IsoDoom.Game;
using Xunit;

namespace IsoDoom.Tests.Game;

/// <summary>T8.4d: the sRGB conversions of the canvas in linear colour (HDR 2D under Godot's Mobile renderer).</summary>
public class SrgbTests
{
    [Fact]
    public void EveryByteComesBackFromItsLinearValue()
    {
        for (int v = 0; v < 256; v++)
            Assert.Equal(v, Srgb.ToByte(Srgb.ToLinear(v / 255.0)));
    }

    [Fact]
    public void ToByteRoundsTheEncodedValue()
    {
        for (int i = 0; i <= 10000; i++)
        {
            double x = i / 10000.0;
            double s = x <= 0.0031308 ? x * 12.92 : 1.055 * Math.Pow(x, 1 / 2.4) - 0.055;
            double encoded = s * 255;
            if (Math.Abs(encoded - Math.Floor(encoded) - 0.5) < 1e-6)
                continue; // a tie
            Assert.Equal((int)Math.Round(encoded), Srgb.ToByte(x));
        }
        Assert.Equal(0, Srgb.ToByte(-1));
        Assert.Equal(255, Srgb.ToByte(2));
    }
}
