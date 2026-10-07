using System;
using System.Linq;
using IsoDoom.Render;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Render;

/// <summary>T6.9: the CPU side of the fuzz (<see cref="Fuzz"/>), which the level check compares the shader with.</summary>
public class FuzzTests
{
    [Fact]
    public void TheTableIsVanillas()
    {
        // r_draw.c fuzzoffset: 50 entries, FUZZOFF 29 times, -FUZZOFF 21 times; its first row.
        Assert.Equal(Fuzz.FUZZTABLE, Fuzz.fuzzoffset.Length);
        Assert.Equal(29, Fuzz.fuzzoffset.Count(o => o == 1));
        Assert.Equal(21, Fuzz.fuzzoffset.Count(o => o == -1));
        Assert.Equal(new[] { 1, -1, 1, -1, 1, 1, -1 }, Fuzz.fuzzoffset[..7]);
    }

    [Fact]
    public void ThePhaseComesRoundEveryValue()
    {
        Assert.Equal(Fuzz.FUZZTABLE, Enumerable.Range(0, Fuzz.FUZZTABLE).Select(Fuzz.Phase).Distinct().Count());
        Assert.All(Enumerable.Range(0, 1000), t => Assert.InRange(Fuzz.Phase(t), 0, Fuzz.FUZZTABLE - 1));
    }

    [Fact]
    public void ARowReadsBelowOrWalksUpTheRunAbove()
    {
        // Column 0 of a patch 50 rows high: fuzzpos = phase + row. Phase 0, row 0: +FUZZOFF, the row below.
        Assert.Equal((1, 1), Fuzz.Source(0, 0, 0, 50, _ => true));
        // Row 1 (-FUZZOFF) under an opaque row 0 (which read row 1, +FUZZOFF): row 1's own unfuzzed pixel, darkened twice.
        Assert.Equal((0, 2), Fuzz.Source(0, 0, 1, 50, _ => true));
        // ... and with row 0 transparent (the top of the post): the row above, once.
        Assert.Equal((-1, 1), Fuzz.Source(0, 0, 1, 50, r => r >= 1));
        // Rows 17-20 are -FUZZOFF, row 16 +FUZZOFF: row 20 walks up to 16, which read row 17; five darkenings.
        Assert.Equal((-3, 5), Fuzz.Source(0, 0, 20, 50, _ => true));
        // Columns continue the position: column 1 starts at fuzzpos = height.
        Assert.Equal(Fuzz.Source(10, 0, 3, 50, _ => true), Fuzz.Source(0, 1, 13, 50, _ => true));
    }

    [Fact]
    public void TheNearestIndexIsTheLowestOfEquals()
    {
        var data = new byte[Playpal.PaletteSize * Playpal.NumPalettes];
        for (int i = 0; i < 256; i++)
        {
            data[i * 3] = (byte)i;
            data[i * 3 + 1] = (byte)(i / 2);
            data[i * 3 + 2] = 0;
        }
        // Index 200 duplicates index 100.
        data[200 * 3] = 100;
        data[200 * 3 + 1] = 50;
        Playpal playpal = Playpal.Decode(data);
        Assert.Equal(100, Fuzz.NearestIndex(playpal, 0, 100, 50, 0));
        Assert.Equal(37, Fuzz.NearestIndex(playpal, 0, 37, 18, 0));
        Assert.Equal(255, Fuzz.NearestIndex(playpal, 0, 255, 255, 255));
        // Palette 1 is all black: index 0.
        Assert.Equal(0, Fuzz.NearestIndex(playpal, 1, 90, 90, 90));
    }
}
