using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IsoDoom.Game;
using IsoDoom.Sim;
using IsoDoom.Tests.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Game;

/// <summary>
/// T7.14: <c>-playdemo</c> through the game flow (<see cref="GameFlow.G_DeferedPlayDemo"/>,
/// <c>G_DoPlayDemo</c>, the demo's commands read each tic, <c>G_CheckDemoStatus</c>):
/// a route written as a <c>.lmp</c> (<see cref="RouteFile.ToDemoLump"/>) read
/// from disk plays its route's game tic for tic and ends; this machine's pause
/// holds it in sync; the menus hold nothing and a new game ends it; a demo the
/// sim can't play is refused with a message; where the demo is found
/// (<see cref="DemoFiles"/>) and the version byte it must carry.
/// </summary>
public sealed class DemoPlaybackTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("isodoom-demos-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ticcmd_t None => default;

    private static (GameFlow Flow, GameFlowTests.Host Host) New(WadArchive? wad = null)
    {
        var host = new GameFlowTests.Host(GameMode.shareware) { Wad = wad ?? new WadArchive([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]) };
        return (new GameFlow(host, GameMode.shareware, new DoomRandom()) { singledemo = true }, host);
    }

    // Writes the route NAME as DIR/NAME.lmp; returns its path without the extension (-playdemo's own way).
    private string WriteLmp(VanillaRoute route)
    {
        string path = Path.Combine(_dir, route.Name);
        File.WriteAllBytes(path + ".lmp", route.File.ToDemoLump());
        return path;
    }

    // Each tic's world checksum of the flow playing its demo to its end (no input).
    private static List<ulong> Play(GameFlow flow, GameFlowTests.Host host)
    {
        var sums = new List<ulong>();
        flow.Ticked = () => sums.Add(host.World!.Checksum());
        for (int i = 0; host.DemosEnded == 0 && i < 100_000; i++)
            flow.G_Ticker(None);
        return sums;
    }

    [Theory]
    [InlineData("synthetic-exit")]
    [InlineData("synthetic-doors")]
    [InlineData("synthetic-dm-exit-2p")] // a netdemo: two players, deathmatch, frags and reborns
    public void ALmpFromDiskPlaysItsRoutesGameToItsEnd(string name)
    {
        VanillaRoute route = VanillaRoute.Load(name);
        ulong[] expected = [.. route.Play(graphics: null).Select(t => t.World.Checksum())];
        (GameFlow flow, GameFlowTests.Host host) = New();
        flow.G_DeferedPlayDemo(WriteLmp(route));
        List<ulong> sums = Play(flow, host);
        Assert.Equal(1, host.DemosEnded); // singledemo: the quit
        Assert.True(flow.DemoEnded);
        Assert.False(flow.demoplayback);
        Assert.False(flow.usergame);
        Assert.Equal(expected, sums);
        Assert.Equal(route.File.PlayerCount > 1, host.World!.netgame);
        Assert.Equal(route.File.Deathmatch, host.World.deathmatch);
    }

    [Fact]
    public void ThisMachinesPauseHoldsTheDemoInSync()
    {
        VanillaRoute route = VanillaRoute.Load("synthetic-doors");
        ulong[] expected = [.. route.Play(graphics: null).Select(t => t.World.Checksum())];
        (GameFlow flow, GameFlowTests.Host host) = New();
        flow.G_DeferedPlayDemo(WriteLmp(route));
        int held = 0;
        var sums = new List<ulong>();
        flow.Ticked = () => sums.Add(host.World!.Checksum());
        var pause = new ticcmd_t { buttons = buttoncode_t.BT_SPECIAL | buttoncode_t.BTS_PAUSE };
        while (host.DemosEnded == 0)
        {
            bool toggle = sums.Count == 100 && (!flow.demopaused && held == 0 || flow.demopaused && held == 50);
            if (flow.demopaused)
                held++;
            int tic = flow.DemoTic;
            flow.G_Ticker(toggle ? pause : None);
            if (flow.demopaused)
                Assert.Equal(tic, flow.DemoTic); // the demo waits
        }
        Assert.Equal(51, held);
        Assert.Equal(expected, sums);
    }

    [Fact]
    public void TheMenusHoldNothingAndANewGameEndsTheDemo()
    {
        VanillaRoute route = VanillaRoute.Load("synthetic-doors");
        (GameFlow flow, GameFlowTests.Host host) = New();
        flow.G_DeferedPlayDemo(WriteLmp(route));
        flow.G_Ticker(None);
        flow.Menu.menuactive = true;
        int leveltime = host.World!.leveltime;
        flow.G_Ticker(None);
        Assert.Equal(leveltime + 1, host.World.leveltime); // vanilla's P_Ticker: menuactive && !demoplayback
        Assert.False(flow.InNetGame);
        flow.Menu.menuactive = false;
        flow.G_DeferedInitNew(skill_t.sk_medium, 1, 1);
        flow.G_Ticker(None);
        Assert.False(flow.demoplayback);
        Assert.True(flow.usergame);
        Assert.Equal(0, host.DemosEnded);
        Assert.Equal("new E1M1", host.Loads[^1]);
    }

    [Fact]
    public void TheMenusAllowANewGameDuringANetdemo()
    {
        (GameFlow flow, GameFlowTests.Host host) = New();
        flow.G_DeferedPlayDemo(WriteLmp(VanillaRoute.Load("synthetic-dm-exit-2p")));
        flow.G_Ticker(None);
        Assert.True(host.World!.netgame);
        Assert.True(flow.netdemo);
        Assert.False(flow.InNetGame); // m_menu.c: netgame && !demoplayback
    }

    public static TheoryData<string, string> Refusals() => new()
    {
        { "version", "Demo is from a different game version!\n(read 105, should be 106 to 109)" },
        { "-gameversion", "Demo is from a different game version!\n(read 108, should be 109)" },
        { "cut short", "no DEMOMARKER at the end" },
        { "map", "the demo is for E1M9,\nwhich this game lacks" },
        { "episode", "the demo is for E2M1,\nwhich this game lacks" },
        { "console player", "consoleplayer 1, players 1000: the console player must be in the game" },
        { "missing", "no file missing.lmp\nand no lump MISSING" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void ADemoTheSimCantPlayIsRefusedWithAMessage(string what, string message)
    {
        byte[] demo = VanillaRoute.Load("synthetic-exit").File.ToDemoLump();
        switch (what)
        {
            case "version": demo[0] = 105; break;
            case "-gameversion": demo[0] = 108; break;
            case "cut short": demo = demo[..^1]; break;
            case "map": demo[3] = 9; break;
            case "episode": demo[2] = 2; break;
            case "console player": demo[8] = 1; break;
        }
        string name = what == "missing" ? "missing" : what.Replace(' ', '-');
        if (what != "missing")
            File.WriteAllBytes(Path.Combine(_dir, name + ".lmp"), demo);
        (GameFlow flow, GameFlowTests.Host host) = New();
        if (what == "-gameversion")
            flow.demoversion = GameVersions.G_VanillaVersionCode(GameVersion.exe_doom_1_9); // -gameversion 1.9: its byte only
        flow.G_DeferedPlayDemo(what == "missing" ? name : Path.Combine(_dir, name)); // (missing: relative, against the working directory)
        flow.G_Ticker(None);
        Assert.Equal(message, flow.DemoRefused);
        Assert.Equal(1, host.DemosRefused);
        Assert.False(flow.demoplayback);
        Assert.Equal(gamestate_t.GS_DEMOSCREEN, flow.gamestate);
        Assert.True(flow.Menu.messageToPrint);
        Assert.Contains(message, flow.Menu.messageString, StringComparison.Ordinal);
        Assert.DoesNotContain(host.Loads, l => l.StartsWith("demo ", StringComparison.Ordinal));
    }

    [Fact]
    public void TheDemoIsTheFileNameLmpThenTheLumpThenTheFile()
    {
        var wad = new WadArchive([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);
        byte[] lump = wad.W_CacheLumpName("DEMO1").ToArray();
        string Resolve(string p) => Path.Combine(_dir, p);
        Assert.Equal(lump, DemoFiles.Find("demo1", wad, Resolve).Data); // vanilla's -playdemo demo1
        Assert.Equal("lump DEMO1", DemoFiles.Find("demo1", wad, Resolve).Source);
        File.WriteAllBytes(Resolve("demo1.lmp"), [1, 2, 3]);
        Assert.Equal([1, 2, 3], DemoFiles.Find("demo1", wad, Resolve).Data); // Chocolate Doom's: the file first
        File.WriteAllBytes(Resolve("Run.LMP"), [5]);
        Assert.Equal([5], DemoFiles.Find("Run.LMP", wad, Resolve).Data); // the extension in any case, the name as given
        File.WriteAllBytes(Resolve("run.dem"), [4]);
        Assert.Equal([4], DemoFiles.Find("run.dem", wad, Resolve).Data);
        Assert.Equal(Path.GetFullPath(Resolve("run.dem")), DemoFiles.Find("run.dem", wad, Resolve).Source);
        Assert.Throws<FileNotFoundException>(() => DemoFiles.Find("demo2", wad, Resolve));
    }

    [Fact]
    public void ADemoCarriesItsVersionsByte()
    {
        Assert.Equal(106, GameVersions.G_VanillaVersionCode(GameVersion.exe_doom_1_666));
        Assert.Equal(107, GameVersions.G_VanillaVersionCode(GameVersion.exe_doom_1_7));
        Assert.Equal(108, GameVersions.G_VanillaVersionCode(GameVersion.exe_doom_1_8));
        Assert.All([GameVersion.exe_doom_1_9, GameVersion.exe_ultimate, GameVersion.exe_final, GameVersion.exe_final2],
            v => Assert.Equal(109, GameVersions.G_VanillaVersionCode(v)));
        // the IWAD's own demos' (Chocolate Doom's InitGameVersion; W_CheckNumForName(...) > 0: never the WAD's first lump)
        WadArchive Iwad(byte version) => new([new WadBuilder(WadType.Iwad).Lump("PLAYPAL").Lump("DEMO1", version, 2, 1, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0x80).ToWadFile()]);
        Assert.Equal(106, GameVersions.IwadDemoVersion(Iwad(106)));
        Assert.Null(GameVersions.IwadDemoVersion(Iwad(3))); // v1.2's
        Assert.Equal(109, GameVersions.IwadDemoVersion(new WadArchive([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)])));
    }

    /// <summary>DOOM1.WAD's <c>DEMO1</c> by its lump name (vanilla's <c>-playdemo demo1</c>) plays to its end on the same world as its route.</summary>
    /// <summary>Without <c>-gameversion</c>, a demo of any version the sim plays (v1.666's to v1.9's) plays, whatever the IWAD's own demos are.</summary>
    [Theory]
    [InlineData(106)]
    [InlineData(107)]
    [InlineData(108)]
    [InlineData(109)]
    public void WithoutGameversionEveryVersionTheSimPlaysPlays(byte version)
    {
        VanillaRoute route = VanillaRoute.Load("synthetic-exit");
        byte[] demo = route.File.ToDemoLump();
        demo[0] = version;
        string path = Path.Combine(_dir, "v" + version);
        File.WriteAllBytes(path + ".lmp", demo);
        (GameFlow flow, GameFlowTests.Host host) = New();
        flow.iwaddemoversion = 106; // Doom II v1.666's IWAD: another version's demo plays, with a warning
        var log = new List<string>();
        flow.Log = log.Add;
        flow.G_DeferedPlayDemo(path);
        Assert.Equal(route.Cmds.Count, Play(flow, host).Count);
        Assert.Null(flow.DemoRefused);
        Assert.Equal(1, host.DemosEnded);
        Assert.Equal(version != 106, log.Exists(l => l.Contains("may go out of step", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheWadsDemoLumpPlaysToItsEnd()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom1(), []);
        VanillaRoute route = VanillaRoute.Demo("DEMO1");
        ulong last = route.Play(graphics: null).Last().World.Checksum();
        (GameFlow flow, GameFlowTests.Host host) = New(wad);
        flow.G_DeferedPlayDemo("demo1");
        List<ulong> sums = Play(flow, host);
        Assert.Equal(route.Cmds.Count, sums.Count);
        Assert.Equal(last, sums[^1]);
        Assert.Equal("demo E1M5", host.Loads[0]);
    }
}
