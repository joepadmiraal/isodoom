using System;
using System.Linq;
using IsoDoom.Game;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Game;

/// <summary>T5.10: <see cref="RouteFile"/>, the route parser the tests and the level script's <c>route</c> share.</summary>
public class RouteFileTests
{
    [Fact]
    public void ParsesTheHeaderAndTheTics()
    {
        var r = RouteFile.Parse(
        [
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
        ], "test.route");
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
        var r = RouteFile.Parse(["1 2 3 0"], "test.route");
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
        FormatException e = Assert.Throws<FormatException>(() => RouteFile.Parse(["iwad doom1", line], "bad.route"));
        Assert.Contains("bad.route", e.Message);
    }

    /// <summary>
    /// T10.7: the executable a route plays as, which the tests' routes and the
    /// level script's <c>route</c> share: the header's, else the IWAD's default
    /// (Final Doom's for <c>tnt</c> and <c>plutonia</c>), none without an IWAD.
    /// </summary>
    [Theory]
    [InlineData("iwad tnt", null, GameVersion.exe_final)]
    [InlineData("iwad plutonia", null, GameVersion.exe_final)]
    [InlineData("iwad tnt", "1.9", GameVersion.exe_doom_1_9)]
    [InlineData("iwad doom2", null, GameVersion.exe_doom_1_9)]
    [InlineData("iwad doom2", "final", GameVersion.exe_final)]
    [InlineData("iwad doom1", null, GameVersion.exe_doom_1_9)]
    [InlineData("iwad doom", null, GameVersion.exe_ultimate)]
    [InlineData("iwad doom", "1.9", GameVersion.exe_doom_1_9)]
    [InlineData("iwad synthetic", null, GameVersion.exe_doom_1_9)]
    [InlineData("iwad testmap", "ultimate", GameVersion.exe_ultimate)]
    [InlineData(null, null, null)]
    [InlineData(null, "final2", GameVersion.exe_final2)]
    public void ImpliesTheIwadsVersion(string? iwad, string? gameversion, GameVersion? expected)
    {
        string[] lines = [.. new[] { iwad, gameversion is null ? null : "gameversion " + gameversion }.OfType<string>(), "0 0 0 0"];
        RouteFile r = RouteFile.Parse(lines, "test.route");
        Assert.Equal(expected, r.ImpliedGameVersion);
    }

    /// <summary>
    /// T11.2: a multi-player route: the <c>players</c> and <c>deathmatch</c>
    /// headers, one <c>ticcmd</c> per player a line separated by <c>|</c>,
    /// events naming their player (<c>pN</c>), and <see cref="RouteFile.Lines"/>
    /// writing them back.
    /// </summary>
    [Fact]
    public void ParsesAMultiPlayerRoute()
    {
        string[] lines =
        [
            "iwad doom1", "players 3", "deathmatch 1", "monsters",
            "25 0 1 0 | -25 0 -1 1 | 0 24 0 2 x2",
            "damage 10 20 5 p2",
            "alert",
            "0 0 0 0 | 0 0 0 0 | 0 0 0 0",
            "rocket p3",
            "1 2 3 4 | 5 6 7 8 | 9 10 11 12",
        ];
        RouteFile r = RouteFile.Parse(lines, "test.route");
        Assert.Equal(0b111, r.PlayersInGame);
        Assert.True(r.Netgame);
        Assert.Equal(1, r.Deathmatch);
        Assert.Equal(4, r.NetCmds.Count);
        Assert.Equal(4, r.Cmds.Count);
        Assert.Equal(-25, r.NetCmds[1][1].forwardmove);
        Assert.Equal(-256, r.NetCmds[0][1].angleturn);
        Assert.Equal(24, r.NetCmds[1][2].sidemove);
        Assert.Equal(0, r.NetCmds[0][3].forwardmove); // a player not in the game: an empty command
        Assert.Equal(new sbyte[] { 25, 25, 0, 1 }, r.Cmds.Select(c => c.forwardmove));
        Assert.Equal(9, r.NetCmds[3][2].forwardmove);
        Assert.Equal(
            [new RouteEvent(2, RouteEventKind.Damage, 10, 20, 5, 1), new RouteEvent(2, RouteEventKind.Alert, 0, 0, 0), new RouteEvent(3, RouteEventKind.Rocket, 0, 0, 0, 2)],
            r.Events);
        string written = RouteFile.Lines(r.NetCmds, 3, r.Events);
        Assert.Equal(string.Join('\n', lines[4..]) + "\n", written);
        RouteFile back = RouteFile.Parse(["players 3", .. written.Split('\n')], "back.route");
        Assert.Equal(r.Events, back.Events);
        for (int t = 0; t < r.NetCmds.Count; t++)
            Assert.Equal(r.NetCmds[t], back.NetCmds[t]);
    }

    /// <summary>T11.2: a single-player route keeps its format, its commands at player 1's slot.</summary>
    [Fact]
    public void ASinglePlayerRouteIsPlayer1Alone()
    {
        RouteFile r = RouteFile.Parse(["iwad doom1", "25 0 0 0 x2"], "test.route");
        Assert.Equal(1, r.PlayersInGame);
        Assert.False(r.Netgame);
        Assert.Equal(2, r.NetCmds.Count);
        Assert.Equal(25, r.NetCmds[1][0].forwardmove);
        Assert.Equal("25 0 0 0 x2\n", RouteFile.Lines(r.NetCmds, 1, r.Events));
    }

    [Theory]
    [InlineData("players 5")]
    [InlineData("players 0")]
    [InlineData("players two")]
    [InlineData("deathmatch 3")]
    [InlineData("players 2\n25 0 0 0")]
    [InlineData("players 2\n25 0 0 0 0 0 0 0")]
    [InlineData("players 2\n25 0 0 0 | 0 0 0")]
    [InlineData("players 2\n25 0 0 0 | 0 0 0 0 | 0 0 0 0")]
    [InlineData("25 0 0 0 | 0 0 0 0")]
    [InlineData("25 0 0 0\nplayers 2")]
    [InlineData("players 2\nalert p3\n0 0 0 0 | 0 0 0 0")]
    [InlineData("alert p0\n0 0 0 0")]
    [InlineData("alert p2\n0 0 0 0")]
    public void RejectsBadMultiPlayerLines(string text)
    {
        Assert.Throws<FormatException>(() => RouteFile.Parse(["iwad doom1", .. text.Split('\n')], "bad.route"));
    }
}
