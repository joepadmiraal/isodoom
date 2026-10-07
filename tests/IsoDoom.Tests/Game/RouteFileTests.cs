using System;
using IsoDoom.Game;
using Xunit;

namespace IsoDoom.Tests.Game;

/// <summary>T5.10: <see cref="RouteFile"/>, the route parser the tests and the level script's <c>route</c> share.</summary>
public class RouteFileTests
{
    [Fact]
    public void ParsesTheHeaderAndTheTics()
    {
        RouteFile r = RouteFile.Parse(new[]
        {
            "# a comment",
            "iwad doom1",
            "map E1M8 # trailing comment",
            "skill 4",
            "exit secret",
            "start 320 4384 90",
            "",
            "25 -1 -2 0",
            "0 0 0 2 x3",
            "-128 127 127 255",
        }, "test.route");
        Assert.Equal("doom1", r.Iwad);
        Assert.Equal("E1M8", r.Map);
        Assert.Equal(4, r.Skill);
        Assert.Equal(2, r.Exit);
        Assert.Equal((320, 4384, 90), r.Start);
        Assert.Equal(5, r.Cmds.Count);
        Assert.Equal(25, r.Cmds[0].forwardmove);
        Assert.Equal(-1, r.Cmds[0].sidemove);
        Assert.Equal(-2 << 8, r.Cmds[0].angleturn); // a demo's angleturn byte
        for (int i = 1; i <= 3; i++)
            Assert.Equal(2, r.Cmds[i].buttons);
        Assert.Equal(-128, r.Cmds[4].forwardmove);
        Assert.Equal(127 << 8, r.Cmds[4].angleturn);
        Assert.Equal(255, r.Cmds[4].buttons);
    }

    [Fact]
    public void DefaultsWithoutHeaders()
    {
        RouteFile r = RouteFile.Parse(new[] { "1 2 3 0" }, "test.route");
        Assert.Null(r.Iwad);
        Assert.Null(r.Map);
        Assert.Equal(3, r.Skill);
        Assert.Equal(0, r.Exit);
        Assert.Null(r.Start);
        Assert.Single(r.Cmds);
    }

    [Theory]
    [InlineData("1 2 3")]
    [InlineData("128 0 0 0")]
    [InlineData("0 0 0 256")]
    [InlineData("exit maybe")]
    [InlineData("start 1 2")]
    [InlineData("skill 6")]
    public void RejectsBadLines(string line)
    {
        var e = Assert.Throws<FormatException>(() => RouteFile.Parse(new[] { "iwad doom1", line }, "bad.route"));
        Assert.Contains("bad.route", e.Message);
    }
}
