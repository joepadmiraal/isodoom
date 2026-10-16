using System;
using System.Linq;
using IsoDoom.Game;
using IsoDoom.Sim;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T6.13 (SPEC §10's stretch): DOOM1.WAD's demos, read by the vanilla-input
/// adapter (<see cref="RouteFile.FromDemo"/>) and played with every tweak off,
/// stay in sync with vanilla to their end, tic by tic, against the
/// reference's dumps (<c>tools/VanillaRef/demos.sh</c>: WAD-derived, in
/// <see cref="VanillaRoute.DumpDirEnvVar"/>; skips without DOOM1.WAD or the dump).
/// </summary>
public class DemoSyncTests
{
    public static TheoryData<string> Demos() => new("DEMO1", "DEMO2", "DEMO3");

    [Theory]
    [MemberData(nameof(Demos))]
    public void StaysInSyncWithVanilla(string demo) => VanillaRoute.Demo(demo).Check();

    /// <summary>
    /// T10.6: Doom II's demos (any version's IWAD: v1.666's are version 106, played by
    /// the reference with <c>-gameversion 1.666</c>), to their end; dumps
    /// <c>doom2-demoN.vanilla</c> (<c>tools/VanillaRef/demos.sh --doom2</c>).
    /// </summary>
    [Theory]
    [MemberData(nameof(Demos))]
    public void Doom2StaysInSyncWithVanilla(string demo) => VanillaRoute.Demo(demo, doom2: true).Check();

    /// <summary>
    /// T10.7: Doom II's demos played as Final Doom's first executable
    /// (<c>-gameversion final</c>: its teleports keep the height, its lost
    /// souls bounce), to their end against the reference played so
    /// (<c>tools/VanillaRef/demos.sh --doom2 --gameversion final</c>: dumps
    /// <c>doom2-final-demoN.vanilla</c>). DEMO3 (MAP26) differs from v1.9's
    /// from tic 253 (its monsters' teleports).
    /// </summary>
    [Theory]
    [MemberData(nameof(Demos))]
    public void Doom2AsFinalDoomStaysInSyncWithVanilla(string demo) =>
        VanillaRoute.Demo(demo, "doom2", IsoDoom.Wad.GameVersion.exe_final).Check();

    /// <summary>
    /// M9: The Ultimate Doom's four demos (E1M5, E2M2, E3M5, E4M2), played as
    /// <c>ultimate</c>, to their end; dumps <c>doom-demoN.vanilla</c>
    /// (<c>tools/VanillaRef/demos.sh --doom</c>). Skips without The Ultimate Doom's IWAD.
    /// </summary>
    [Theory]
    [InlineData("DEMO1")]
    [InlineData("DEMO2")]
    [InlineData("DEMO3")]
    [InlineData("DEMO4")]
    public void UltimateDoomStaysInSyncWithVanilla(string demo) => VanillaRoute.Demo(demo, "doom").Check();

    /// <summary>DOOM1.WAD v1.9's demos: their maps, Hurt Me Plenty, monsters, and their lengths.</summary>
    [Theory]
    [InlineData("DEMO1", "E1M5", 5026)]
    [InlineData("DEMO2", "E1M3", 3836)]
    [InlineData("DEMO3", "E1M7", 2134)]
    public void ReadsTheDemoHeaders(string demo, string map, int tics)
    {
        var route = VanillaRoute.Demo(demo);
        Assert.Equal(map, route.Map);
        Assert.Equal(skill_t.sk_medium, route.Skill);
        Assert.True(route.Monsters);
        Assert.Equal(tics, route.Cmds.Count);
    }

    /// <summary>g_game.c's demo format: the header, 4 bytes a tic, the marker; what the sim cannot play is refused.</summary>
    [Fact]
    public void ReadsAV19Demo()
    {
        byte[] lump = [109, 3, 1, 2, 0, 0, 0, 1, 0, 1, 0, 0, 0, 25, 0xe8, 0xff, 1, 0x32, 0, 0x7f, 6, RouteFile.DEMOMARKER];
        var r = RouteFile.FromDemo(lump, "test");
        Assert.Equal("E1M2", r.Map);
        Assert.Equal(4, r.Skill);
        Assert.False(r.Monsters);
        Assert.Equal(new sbyte[] { 25, 0x32 }, r.Cmds.Select(c => c.forwardmove));
        Assert.Equal(new sbyte[] { -24, 0 }, r.Cmds.Select(c => c.sidemove));
        Assert.Equal(new short[] { -256, 127 << 8 }, r.Cmds.Select(c => c.angleturn));
        Assert.Equal(new byte[] { 1, 6 }, r.Cmds.Select(c => c.buttons));
        Assert.Equal("MAP02", RouteFile.FromDemo(lump, "test", commercial: true).Map);

        Assert.Throws<FormatException>(() => RouteFile.FromDemo(lump.AsSpan(..^1), "no marker"));
        Assert.Throws<FormatException>(() => RouteFile.FromDemo(lump.AsSpan(..12), "short"));
        byte[] old = (byte[])lump.Clone();
        old[0] = 110;
        Assert.Throws<FormatException>(() => RouteFile.FromDemo(old, "version"));
        old[0] = 105;
        Assert.Throws<FormatException>(() => RouteFile.FromDemo(old, "version"));
        old[0] = 106; // T10.6: v1.666's (Doom II v1.666's demos), read as v1.9's
        Assert.Equal(2, RouteFile.FromDemo(old, "v1.666").Cmds.Count);
        // T11.2: the console player must be in the game; deathmatch 0-2
        byte[] bad = (byte[])lump.Clone();
        bad[8] = 1;
        Assert.Throws<FormatException>(() => RouteFile.FromDemo(bad, "consoleplayer"));
        bad = (byte[])lump.Clone();
        bad[9] = 0;
        Assert.Throws<FormatException>(() => RouteFile.FromDemo(bad, "no players"));
        bad = (byte[])lump.Clone();
        bad[4] = 3;
        Assert.Throws<FormatException>(() => RouteFile.FromDemo(bad, "deathmatch"));
    }

    /// <summary>
    /// T11.2: a multi-player demo (g_game.c's netdemo): the header's players,
    /// deathmatch, respawn, fast and console player kept, each tic one
    /// <c>ticcmd</c> per player in the game in player order, a tic cut short
    /// by the marker dropped (vanilla's demo ends there).
    /// </summary>
    [Fact]
    public void ReadsAMultiPlayerDemo()
    {
        byte[] lump = [109, 2, 1, 1, 1, 1, 1, 0, 2, 1, 0, 1, 1,
            25, 0, 1, 0, /**/ 0, 24, 0, 1, /**/ 50, 0, 0, 2,
            1, 0, 0, 0, /**/ 2, 0, 0, 0, /**/ 3, 0, 0, 0,
            4, 0, 0, 0, RouteFile.DEMOMARKER];
        var r = RouteFile.FromDemo(lump, "test");
        Assert.Equal(0b1101, r.PlayersInGame);
        Assert.False(r.Netgame); // G_DoPlayDemo: playeringame[1] makes the netgame
        Assert.Equal(3, r.PlayerCount);
        Assert.Equal((1, true, true, 2), (r.Deathmatch, r.Respawn, r.Fast, r.ConsolePlayer));
        Assert.True(r.Monsters);
        Assert.Equal(2, r.NetCmds.Count);
        Assert.Equal(new sbyte[] { 25, 0, 0, 50 }, r.NetCmds[0].Select(c => c.forwardmove));
        Assert.Equal(24, r.NetCmds[0][2].sidemove);
        Assert.Equal(50, r.NetCmds[0][3].forwardmove);
        Assert.Equal(new sbyte[] { 1, 0, 2, 3 }, r.NetCmds[1].Select(c => c.forwardmove));
        Assert.Equal(new sbyte[] { 0, 2 }, r.Cmds.Select(c => c.forwardmove)); // the console player's (player 3)
        SpawnSettings s = r.Settings(IsoDoom.Wad.GameMode.shareware, skill_t.sk_medium);
        Assert.Equal((0b1101, false, 1, true, true, false), (s.playersingame, s.netgame, s.deathmatch, s.respawnparm, s.fastparm, s.nomonsters));
        Assert.Equal(lump.AsSpan(..^5).ToArray().Append(RouteFile.DEMOMARKER), r.ToDemo(1, 1));
    }

    /// <summary>
    /// T11.2: every multi-player route written as the netdemo the vanilla
    /// reference plays (<see cref="RouteFile.ToDemo"/>, <c>routes.py</c>'s)
    /// and read back by the vanilla-input adapter (<see cref="RouteFile.FromDemo"/>)
    /// gives the route's commands, and plays in sync with the route's vanilla dump.
    /// T11.4: the deathmatch routes too, the deathmatch byte read back (a demo has
    /// no <c>-timer</c>, as vanilla's: <c>dm-e1m1-timer</c> is not among them).
    /// </summary>
    [Theory]
    [InlineData("synthetic-coop")]
    [InlineData("testmap-coop")]
    [InlineData("coop-e1m1-2p")]
    [InlineData("coop-e1m1-4p")]
    [InlineData("testmap-dm")]
    [InlineData("dm-e1m1-2p")]
    [InlineData("dm2-e1m1-respawn")]
    public void AMultiPlayerRouteAsANetdemoStaysInSyncWithVanilla(string name)
    {
        var route = VanillaRoute.Load(name);
        Assert.True(route.File.Netgame);
        var demo = RouteFile.FromDemo(route.File.ToDemo(1, 1), name);
        Assert.Equal(route.PlayersInGame, demo.PlayersInGame);
        Assert.Equal(route.File.Deathmatch, demo.Deathmatch);
        Assert.Equal(route.NetCmds.Count, demo.NetCmds.Count);
        for (int t = 0; t < demo.NetCmds.Count; t++)
            Assert.Equal(route.NetCmds[t], demo.NetCmds[t]);
        VanillaRoute.FromDemo(route, demo.WithEvents(route.File.Events)).Check(); // (the events are the reference's, not the demo's)
    }
}
