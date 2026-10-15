using System;
using System.Diagnostics;
using System.Linq;
using IsoDoom.Game;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Game;

/// <summary>
/// T10.7: Final Doom (TNT: Evilution and The Plutonia Experiment) and the
/// executable emulated (Chocolate Doom's <c>gameversion</c>,
/// <see cref="GameVersions"/>). The version's effects are tested without the
/// IWADs (the teleport's in <c>TeleportTests</c> and the routes
/// <c>testmap-teleport-final</c>, <c>map20-teleport-final</c> and
/// <c>map05-skull-floor-final</c> against vanilla's <c>-gameversion final</c>);
/// the IWADs' own tests skip without them (<see cref="TestWads.TntEnvVar"/>,
/// <see cref="TestWads.PlutoniaEnvVar"/>, or <c>wads/tnt.wad</c>, <c>wads/plutonia.wad</c>).
/// </summary>
public class FinalDoomTests
{
    // ---- the version ----

    [Theory]
    [InlineData(GameMode.shareware, GameMission.doom, GameVersion.exe_doom_1_9)]
    [InlineData(GameMode.registered, GameMission.doom, GameVersion.exe_doom_1_9)]
    [InlineData(GameMode.retail, GameMission.doom, GameVersion.exe_ultimate)]
    [InlineData(GameMode.commercial, GameMission.doom2, GameVersion.exe_doom_1_9)]
    [InlineData(GameMode.commercial, GameMission.pack_tnt, GameVersion.exe_final)]
    [InlineData(GameMode.commercial, GameMission.pack_plut, GameVersion.exe_final)]
    public void EachIwadDefaultsToItsExecutable(GameMode mode, GameMission mission, GameVersion version)
    {
        Assert.Equal(version, GameVersions.Default(mode, mission));
        var iwad = new IwadInfo(mode, mission);
        Assert.Equal((version, iwad), GameVersions.D_SetGameVersion(iwad));
    }

    [Theory]
    [InlineData("1.666", GameVersion.exe_doom_1_666)]
    [InlineData("1.7", GameVersion.exe_doom_1_7)]
    [InlineData("1.8", GameVersion.exe_doom_1_8)]
    [InlineData("1.9", GameVersion.exe_doom_1_9)]
    [InlineData("ultimate", GameVersion.exe_ultimate)]
    [InlineData("final", GameVersion.exe_final)]
    [InlineData("FINAL2", GameVersion.exe_final2)]
    public void GameversionNamesAnExecutable(string cmdline, GameVersion version)
    {
        Assert.Equal(version, GameVersions.Parse(cmdline));
        Assert.Equal(cmdline.ToLowerInvariant(), GameVersions.Cmdline(version));
        Assert.True(GameVersions.Emulated(version));
        Assert.Equal(version, GameVersions.D_SetGameVersion(new IwadInfo(GameMode.commercial, GameMission.doom2), cmdline).Version);
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("1.5")]
    [InlineData("hacx")]
    [InlineData("chex")]
    [InlineData("doom")]
    public void ExecutablesNotEmulatedAreRefused(string cmdline)
    {
        Assert.Null(GameVersions.Parse(cmdline));
        var e = Assert.Throws<ArgumentException>(() => GameVersions.D_SetGameVersion(new IwadInfo(GameMode.shareware, GameMission.doom), cmdline));
        Assert.Contains("final (Final Doom)", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(GameVersion.exe_doom_1_2, false)]
    [InlineData(GameVersion.exe_doom_1_5, false)]
    [InlineData(GameVersion.exe_hacx, false)]
    [InlineData(GameVersion.exe_chex, false)]
    [InlineData(GameVersion.exe_final, true)]
    public void OnlyThePortedExecutablesAreEmulated(GameVersion version, bool emulated) =>
        Assert.Equal(emulated, GameVersions.Emulated(version));

    /// <summary>d_main.c <c>InitGameVersion</c>: older executables play The Ultimate Doom as registered and Final Doom as Doom II.</summary>
    [Theory]
    [InlineData(GameMode.retail, GameMission.doom, "1.9", GameMode.registered, GameMission.doom)]
    [InlineData(GameMode.retail, GameMission.doom, "final", GameMode.retail, GameMission.doom)]
    [InlineData(GameMode.commercial, GameMission.pack_tnt, "1.9", GameMode.commercial, GameMission.doom2)]
    [InlineData(GameMode.commercial, GameMission.pack_plut, "ultimate", GameMode.commercial, GameMission.doom2)]
    [InlineData(GameMode.commercial, GameMission.pack_plut, "final2", GameMode.commercial, GameMission.pack_plut)]
    [InlineData(GameMode.commercial, GameMission.doom2, "final", GameMode.commercial, GameMission.doom2)]
    public void OlderExecutablesDemoteTheIwad(GameMode mode, GameMission mission, string cmdline, GameMode playedMode, GameMission playedMission)
    {
        IwadInfo played = GameVersions.D_SetGameVersion(new IwadInfo(mode, mission), cmdline).Iwad;
        Assert.Equal((playedMode, playedMission), (played.GameMode, played.GameMission));
    }

    // ---- the flow by version ----

    private static GameFlow Flow(GameMode mode, GameVersion version, GameMission mission = GameMission.none) =>
        new(new GameFlowTests.Host(mode), mode, new DoomRandom(), GameVariant.vanilla, mission, version);

    /// <summary>
    /// d_main.c <c>D_DoAdvanceDemo</c>'s fifth step: <c>CREDIT</c> from The
    /// Ultimate Doom's executable on, else <c>HELP2</c> (Doom II: <c>TITLEPIC</c>);
    /// f_finale.c's E1 end picture likewise.
    /// </summary>
    [Theory]
    [InlineData(GameMode.registered, GameVersion.exe_doom_1_9, "HELP2")]
    [InlineData(GameMode.registered, GameVersion.exe_ultimate, "CREDIT")]
    [InlineData(GameMode.shareware, GameVersion.exe_final, "CREDIT")]
    [InlineData(GameMode.retail, GameVersion.exe_ultimate, "CREDIT")]
    public void TheCreditPageFollowsTheVersion(GameMode mode, GameVersion version, string page)
    {
        var host = new GameFlowTests.Host(mode);
        host.Extra.UnionWith(["CREDIT", "HELP2"]);
        var flow = new GameFlow(host, mode, new DoomRandom(), GameVariant.vanilla, GameMission.doom, version);
        flow.D_StartTitle(null);
        for (int step = 0; step < 2; step++)
        {
            for (int t = flow.pagetic; t >= -1; t--)
                flow.G_Ticker(default);
        }
        Assert.Equal((4, page), (flow.demosequence, flow.pagename));
        flow.Finale.F_StartFinale(1, 8);
        Assert.Equal(page, flow.Finale.ArtScreen());
    }

    /// <summary>
    /// The title loop as Final Doom's first executable: The Ultimate Doom's
    /// seven steps (its <c>DEMO4</c>, which vanilla's bombs out on; passed
    /// here with the other demos), so the pages come as Doom II's.
    /// </summary>
    [Fact]
    public void FinalDoomsTitleLoopShowsDoom2sPages()
    {
        var host = new GameFlowTests.Host(GameMode.commercial);
        host.Extra.UnionWith(["CREDIT"]);
        var flow = new GameFlow(host, GameMode.commercial, new DoomRandom(), GameVariant.vanilla, GameMission.pack_tnt);
        Assert.Equal(GameVersion.exe_final, flow.gameversion);
        flow.D_StartTitle(null);
        var shown = new System.Collections.Generic.List<string>();
        for (int round = 0; round < 6; round++)
        {
            shown.Add($"{flow.pagename}:{flow.demosequence}");
            for (int t = flow.pagetic; t >= -1; t--)
                flow.G_Ticker(default);
        }
        Assert.Equal("TITLEPIC:0 CREDIT:2 TITLEPIC:4 TITLEPIC:0 CREDIT:2 TITLEPIC:4", string.Join(' ', shown));
    }

    /// <summary>m_menu.c <c>M_Init</c>: four episodes from The Ultimate Doom's executable on, three before.</summary>
    [Theory]
    [InlineData(GameMode.registered, GameVersion.exe_doom_1_9, 3)]
    [InlineData(GameMode.registered, GameVersion.exe_ultimate, 4)]
    [InlineData(GameMode.retail, GameVersion.exe_ultimate, 4)]
    [InlineData(GameMode.retail, GameVersion.exe_final, 4)]
    public void TheEpisodeMenuFollowsTheVersion(GameMode mode, GameVersion version, int episodes) =>
        Assert.Equal(episodes, (int)Flow(mode, version).Menu.EpiDef.numitems);

    /// <summary>T10.7: the flow's version is the IWAD's default without one.</summary>
    [Fact]
    public void TheFlowDefaultsToTheIwadsVersion()
    {
        Assert.Equal(GameVersion.exe_final, new GameFlow(new GameFlowTests.Host(GameMode.commercial), GameMode.commercial, new DoomRandom(), GameVariant.vanilla, GameMission.pack_plut).gameversion);
        Assert.Equal(GameVersion.exe_doom_1_9, new GameFlow(new GameFlowTests.Host(GameMode.commercial), GameMode.commercial, new DoomRandom()).gameversion);
        Assert.Equal(GameVersion.exe_ultimate, new GameFlow(new GameFlowTests.Host(GameMode.retail), GameMode.retail, new DoomRandom()).gameversion);
    }

    /// <summary>d_englsh.h's first and last map names of TNT and Plutonia (hu_stuff.c <c>HU_TITLET</c>, <c>HU_TITLEP</c>).</summary>
    [Theory]
    [InlineData(GameMission.pack_tnt, 1, "level 1: system control")]
    [InlineData(GameMission.pack_tnt, 31, "level 31: pharaoh")]
    [InlineData(GameMission.pack_plut, 1, "level 1: congo")]
    [InlineData(GameMission.pack_plut, 30, "level 30: the gateway of hell")]
    [InlineData(GameMission.pack_plut, 32, "level 32: go 2 it")]
    public void TheMapNamesAreFinalDooms(GameMission mission, int map, string title) =>
        Assert.Equal(title, HuStuff.HU_Title(mission, 1, map, GameVariant.vanilla, GameVersion.exe_final));

    /// <summary>The saves keep the version: a game saved as Final Doom's first executable loads as it.</summary>
    [Fact]
    public void TheVersionIsSaved()
    {
        var wad = new WadArchive([WadFile.FromBytes(IsoDoom.Tools.SyntheticIwad.SyntheticIwad.Build(), "s.wad")]);
        var world = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium, gameversion: GameVersion.exe_final), Tweaks.Vanilla);
        world.P_InitPicAnims(null, PicAnims.FlatNames(wad));
        world.G_DoLoadLevel(Level.Load(wad, "E1M1"));
        var save = SaveGameFile.Read(SaveGameFile.Write(world, "final"));
        World loaded = save.LoadWorld(Level.Load(wad, "E1M1"), null, PicAnims.FlatNames(wad));
        Assert.Equal(GameVersion.exe_final, loaded.gameversion);
        Assert.Equal(world.Checksum(), loaded.Checksum());
    }

    // ---- the IWADs (skip without them) ----

    public static TheoryData<string> Iwads() => new("tnt", "plutonia");

    /// <summary>The IWAD is identified by its lumps (<c>REDTNT2</c>, <c>CAMO1</c>) and plays as Final Doom's executable.</summary>
    [Theory]
    [MemberData(nameof(Iwads))]
    public void TheIwadIsIdentified(string iwad)
    {
        var wad = WadArchive.Open(TestWads.RequireFinalDoom(iwad));
        IwadInfo info = IwadIdentification.D_IdentifyVersion(wad);
        Assert.Equal((GameMode.commercial, iwad == "tnt" ? GameMission.pack_tnt : GameMission.pack_plut), (info.GameMode, info.GameMission));
        Assert.Equal(GameVersion.exe_final, GameVersions.D_SetGameVersion(info).Version);
        for (int map = 1; map <= 32; map++)
            Assert.True(wad.W_CheckNumForName($"MAP{map:00}") >= 0, $"MAP{map:00}");
    }

    /// <summary>Every map loads within SPEC §9's second and runs 35 tics with its monsters, as Final Doom's executable.</summary>
    [Theory]
    [MemberData(nameof(Iwads))]
    public void EveryMapLoadsAndPlays(string iwad)
    {
        var wad = WadArchive.Open(TestWads.RequireFinalDoom(iwad));
        Textures textures = Textures.R_InitTextures(wad);
        for (int map = 1; map <= 32; map++)
        {
            var clock = Stopwatch.StartNew();
            var world = new World(new SpawnSettings(GameMode.commercial, skill_t.sk_hard, gameversion: GameVersion.exe_final), Tweaks.Vanilla)
            {
                textures = textures,
                map31exists = true,
            };
            world.P_InitPicAnims(textures, PicAnims.FlatNames(wad));
            world.G_DoLoadLevel(Level.Load(wad, $"MAP{map:00}"));
            Assert.True(clock.Elapsed.TotalSeconds < 1, $"MAP{map:00} took {clock.Elapsed.TotalMilliseconds:F0} ms");
            for (int t = 0; t < 35; t++)
                world.G_Ticker(new ticcmd_t { forwardmove = 25 });
            Assert.True(world.players[0].mo is not null, $"MAP{map:00}: no player");
        }
    }

    /// <summary>
    /// The IWAD's demos (<c>DEMO1</c>–<c>DEMO3</c>, v1.9's) stay in sync with
    /// vanilla's reference played as Final Doom's executable
    /// (<c>tools/VanillaRef/demos.sh --tnt</c>, <c>--plutonia</c>: dumps
    /// <c>tnt-demoN.vanilla</c>, <c>plutonia-demoN.vanilla</c>); skips without
    /// the IWAD or the dump.
    /// </summary>
    [Theory]
    [InlineData("tnt", "DEMO1")]
    [InlineData("tnt", "DEMO2")]
    [InlineData("tnt", "DEMO3")]
    [InlineData("plutonia", "DEMO1")]
    [InlineData("plutonia", "DEMO2")]
    [InlineData("plutonia", "DEMO3")]
    public void TheDemosStayInSyncWithVanilla(string iwad, string demo)
    {
        VanillaRoute route = VanillaRoute.Demo(demo, iwad);
        Assert.Equal(GameVersion.exe_final, route.GameVersion);
        route.Check();
    }

    /// <summary>The routes on Final Doom's IWADs (<c>iwad tnt|plutonia</c> in <c>Routes/</c>, played as <c>final</c>), if any.</summary>
    [Fact]
    public void RoutesOnTheIwadsPlayAsFinalDoom()
    {
        foreach (VanillaRoute route in VanillaRoute.Names().Select(VanillaRoute.Load).Where(r => r.Iwad is "tnt" or "plutonia"))
        {
            Assert.True(route.Commercial);
            Assert.Equal(route.File.GameVersion ?? GameVersion.exe_final, route.GameVersion);
        }
    }
}
