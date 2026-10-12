using System;
using IsoDoom.Render;
using Xunit;

namespace IsoDoom.Tests.Render;

/// <summary>T6.13t: the sight dim's fade per tic (<see cref="SightDim"/>).</summary>
public class SightDimTests
{
    private static void Tic(SightDim dim, params (object Thing, bool InSight)[] things) => dim.Update(things);

    [Fact]
    public void NewThingsStartAtTheirTarget()
    {
        var dim = new SightDim();
        object seen = new(), unseen = new();
        Tic(dim, (seen, true), (unseen, false));
        Assert.Equal(0, dim.Dim(seen));
        Assert.Equal(SightDim.DefaultRows, dim.Dim(unseen));
        Assert.Equal(SightDim.DefaultRows, dim.Target(unseen));
        Assert.Equal(0, dim.Target(seen));
        Assert.Equal(1, dim.DimmedCount);
        Assert.Equal(2, dim.Count);
    }

    [Fact]
    public void ComingIntoSightUndimsAtOnce()
    {
        var dim = new SightDim();
        object mo = new();
        Tic(dim, (mo, false));
        Tic(dim, (mo, true));
        Assert.Equal(0, dim.Dim(mo));
        Assert.True(dim.InSight(mo));
    }

    [Fact]
    public void LeavingSightWaitsThenStepsUpToTheAmount()
    {
        var dim = new SightDim { Rows = 10 };
        object mo = new();
        Tic(dim, (mo, true));
        int[] expected = [0, 0, 0, 0, 2, 4, 6, 8, 10, 10, 10];
        foreach (int e in expected)
        {
            Tic(dim, (mo, false));
            Assert.Equal(e, dim.Dim(mo));
            Assert.Equal(10, dim.Target(mo));
        }
    }

    [Fact]
    public void AnOddAmountCapsTheLastStep()
    {
        var dim = new SightDim { Rows = 5 };
        object mo = new();
        Tic(dim, (mo, true));
        for (int i = 0; i < SightDim.GraceTics; i++)
            Tic(dim, (mo, false));
        int[] expected = [2, 4, 5, 5];
        foreach (int e in expected)
        {
            Tic(dim, (mo, false));
            Assert.Equal(e, dim.Dim(mo));
        }
    }

    [Fact]
    public void ABlinkWithinTheGraceStaysLit()
    {
        var dim = new SightDim();
        object mo = new();
        Tic(dim, (mo, true));
        for (int round = 0; round < 5; round++)
        {
            for (int i = 0; i < SightDim.GraceTics; i++)
            {
                Tic(dim, (mo, false));
                Assert.Equal(0, dim.Dim(mo));
            }
            Tic(dim, (mo, true));
            Assert.Equal(0, dim.Dim(mo));
        }
    }

    [Fact]
    public void ThingsLeftOutDropAndComeBackAtTheirTarget()
    {
        var dim = new SightDim();
        object a = new(), b = new();
        Tic(dim, (a, true), (b, true));
        Tic(dim, (b, true));
        Assert.Null(dim.InSight(a));
        Assert.Equal(0, dim.Dim(a));
        Assert.Equal(1, dim.Count);
        // Back (drawn again by the fog, say) out of sight: dimmed at once, no fade.
        Tic(dim, (a, false), (b, true));
        Assert.Equal(SightDim.DefaultRows, dim.Dim(a));
    }

    [Fact]
    public void ClearForgetsEverything()
    {
        var dim = new SightDim();
        object mo = new();
        Tic(dim, (mo, false));
        dim.Clear();
        Assert.Equal(0, dim.Count);
        Assert.Equal(0, dim.Dim(mo));
        Assert.Null(dim.InSight(mo));
    }

    [Fact]
    public void OffDimsNothing()
    {
        var dim = new SightDim();
        object mo = new();
        Tic(dim, (mo, false));
        dim.Rows = 0;
        Assert.Equal(0, dim.Dim(mo));
        Tic(dim, (mo, false));
        Assert.Equal(0, dim.Dim(mo));
        Assert.Equal(0, dim.Target(mo));
        Assert.Equal(0, dim.Count);
    }

    [Fact]
    public void ALowerAmountCapsAtOnceAndAHigherOneFadesUp()
    {
        var dim = new SightDim { Rows = 14 };
        object mo = new();
        Tic(dim, (mo, false));
        Assert.Equal(14, dim.Dim(mo));
        dim.Rows = 6;
        Assert.Equal(6, dim.Dim(mo));
        Tic(dim, (mo, false));
        dim.Rows = 10;
        Assert.Equal(6, dim.Dim(mo));
        Tic(dim, (mo, false));
        Assert.Equal(8, dim.Dim(mo));
        Tic(dim, (mo, false));
        Assert.Equal(10, dim.Dim(mo));
    }

    [Theory]
    [InlineData(0, 10, 10)]
    [InlineData(5, 10, 15)]
    [InlineData(25, 10, 29)]
    [InlineData(31, 10, 31)] // never lighter
    [InlineData(12, 0, 12)]
    public void DimmedRowStopsAtTheMaxRow(int row, int dim, int expected) =>
        Assert.Equal(expected, SightDim.DimmedRow(row, dim));

    [Theory]
    [InlineData("off", 0)]
    [InlineData("vanilla", 0)]
    [InlineData("0", 0)]
    [InlineData("6", 6)]
    [InlineData("10", 10)]
    [InlineData("31", 31)]
    public void Parses(string value, int rows)
    {
        Assert.Equal(rows, SightDim.Parse(value, "test"));
        Assert.Equal(rows == 0 ? "off" : value, SightDim.Name(rows));
    }

    [Theory]
    [InlineData("32")]
    [InlineData("-1")]
    [InlineData("dark")]
    public void RejectsOtherValues(string value) =>
        Assert.Throws<ArgumentException>(() => SightDim.Parse(value, "test"));
}
