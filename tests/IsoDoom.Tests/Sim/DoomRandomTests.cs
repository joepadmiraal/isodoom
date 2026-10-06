using IsoDoom.Sim;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>T4.1: m_random.c.</summary>
public class DoomRandomTests
{
    [Fact]
    public void Table_matches_m_random_c()
    {
        Assert.Equal(256, DoomRandom.rndtable.Length);
        int sum = 0;
        foreach (byte b in DoomRandom.rndtable)
            sum += b;
        Assert.Equal(32986, sum);
        Assert.Equal(0, DoomRandom.rndtable[0]);
        Assert.Equal(249, DoomRandom.rndtable[255]);
    }

    [Fact]
    public void First_P_Random_values_skip_entry_zero()
    {
        var r = new DoomRandom();
        int[] first = { 8, 109, 220, 222, 241, 149, 107, 75, 248, 254, 140, 16, 66, 74, 21, 211 };
        foreach (int v in first)
            Assert.Equal(v, r.P_Random());
        Assert.Equal(16, r.prndindex);
        Assert.Equal(0, r.rndindex);
    }

    [Fact]
    public void P_Random_wraps_after_256()
    {
        var r = new DoomRandom();
        for (int i = 0; i < 253; i++)
            r.P_Random();
        Assert.Equal(236, r.P_Random()); // call 254: entry 254
        Assert.Equal(249, r.P_Random()); // call 255: entry 255
        Assert.Equal(0, r.P_Random()); // call 256: entry 0, the index wrapped
        Assert.Equal(0, r.prndindex);
        Assert.Equal(8, r.P_Random()); // and round again
    }

    [Fact]
    public void P_Random_after_wrap_returns_entry_zero_then_repeats()
    {
        var r = new DoomRandom();
        var firstPass = new int[256];
        for (int i = 0; i < 256; i++)
            firstPass[i] = r.P_Random();
        Assert.Equal(0, r.prndindex);
        Assert.Equal(0, firstPass[255]); // the 256th call reads entry 0
        for (int i = 0; i < 256; i++)
            Assert.Equal(firstPass[i], r.P_Random());
    }

    [Fact]
    public void M_Random_has_its_own_index_and_M_ClearRandom_resets_both()
    {
        var r = new DoomRandom();
        Assert.Equal(8, r.M_Random());
        Assert.Equal(109, r.M_Random());
        Assert.Equal(8, r.P_Random());
        Assert.Equal(2, r.rndindex);
        Assert.Equal(1, r.prndindex);
        r.M_ClearRandom();
        Assert.Equal(0, r.rndindex);
        Assert.Equal(0, r.prndindex);
        Assert.Equal(8, r.P_Random());
        Assert.Equal(8, r.M_Random());
    }
}
