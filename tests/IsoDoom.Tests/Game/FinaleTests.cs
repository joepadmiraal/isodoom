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
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Game;

/// <summary>
/// T7.5: f_finale.c's finale (<see cref="FFinale"/>). Against vanilla: each
/// DOOM1.WAD route that ends the episode (E1M8's exit) played through the
/// game flow into its finale, which then runs tic by tic as vanilla's did with
/// the same presses, compared with the dump of
/// <c>tools/VanillaRef/finales.sh</c> (the episode, map, flat and text it
/// starts with; every tic's stage, count, the hash of every pixel drawn and
/// <c>M_Random</c>'s index) through the text and the end picture; skips
/// without DOOM1.WAD or the dump. Without a WAD: the texts and flats by game
/// mission, episode and map, the typing's speed, the end pictures and Doom
/// II's skip.
/// </summary>
public class FinaleTests
{
    // ---- against vanilla ----

    /// <summary>The DOOM1.WAD routes that end the episode (exit on ExM8).</summary>
    private static List<VanillaRoute> EndRoutes() =>
        [.. VanillaRoute.Names().Select(VanillaRoute.Load).Where(r => r.Iwad == "doom1" && r.Exit != 0 && r.Map.EndsWith("M8", StringComparison.OrdinalIgnoreCase))];

    public static TheoryData<string> Routes()
    {
        var data = new TheoryData<string>();
        foreach (VanillaRoute r in EndRoutes())
            data.Add(r.Name);
        return data;
    }

    [Fact]
    public void Episode1sEndHasAFinaleToCompare() =>
        Assert.Contains(EndRoutes(), r => r.Map == "E1M8" && r.Exit == 1);

    [Theory]
    [MemberData(nameof(Routes))]
    public void MatchesVanilla(string name)
    {
        var route = VanillaRoute.Load(name);
        TestWads.RequireDoom1();
        string? dir = Environment.GetEnvironmentVariable(VanillaRoute.DumpDirEnvVar);
        if (string.IsNullOrEmpty(dir))
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "isodoom", "vanilla-routes");
        string path = Path.Combine(dir, $"{name}.fi");
        if (!File.Exists(path))
            Assert.Skip($"No vanilla dump {path}: run tools/VanillaRef/finales.sh (or set {VanillaRoute.DumpDirEnvVar}).");
        string[] expected = [.. File.ReadAllLines(path).Where(l => l.Length > 0)];
        Assert.StartsWith("presses ", expected[0]);
        Assert.StartsWith("finale ", expected[1]);
        HashSet<int> presses = [.. expected[0]["presses ".Length..].Split(',').Select(int.Parse)];

        World world = route.NewWorld(out WadArchive wad);
        var mrandom = new DoomRandom(); // G_InitNew's M_ClearRandom
        var host = new IntermissionTests.RouteHost(world, wad, mrandom);
        var flow = new GameFlow(host, GameMode.shareware, mrandom, GameVariant.vanilla, GameMission.doom);
        for (int tic = 0; tic < route.Cmds.Count; tic++)
        {
            Assert.Equal(gamestate_t.GS_LEVEL, flow.gamestate);
            route.File.RunEvents(world, tic);
            flow.G_Ticker(route.Cmds[tic]);
        }
        Assert.Equal(gamestate_t.GS_FINALE, flow.gamestate); // no intermission after ExM8
        FFinale fi = flow.Finale;
        Assert.Equal(expected[1], $"finale {world.gameepisode} {world.gamemap} {fi.finaleflat} {fi.finaletext.Length} {TextHash(fi.finaletext):x8}");

        var g = new ScreenGraphics(wad, new HuStuff(wad));
        var screen = new HudScreen(0, HudScreen.SCREENHEIGHT);
        for (int t = 1; t < expected.Length - 1; t++)
        {
            Assert.Equal(gamestate_t.GS_FINALE, flow.gamestate);
            flow.G_Ticker(new ticcmd_t { buttons = presses.Contains(t) ? buttoncode_t.BT_USE : (byte)0 });
            fi.F_Drawer(g, screen);
            string actual = $"{fi.finalestage} {fi.finalecount} {screen.Hash():x8} {mrandom.rndindex}";
            if (actual != expected[t + 1])
                Assert.Fail($"{name}.fi: finale tic {t} differs from vanilla\n  vanilla: {expected[t + 1]}\n  game:    {actual}");
        }
        // the end picture stays (vanilla's: until the menu ends the game or starts another)
        Assert.Equal((gamestate_t.GS_FINALE, FFinale.F_STAGE_ARTSCREEN, "HELP2"), (flow.gamestate, fi.finalestage, fi.ArtScreen()));
    }

    /// <summary>dump.c's text hash: the 32-bit FNV-1a of the text's bytes.</summary>
    private static uint TextHash(string text)
    {
        uint hash = 2166136261u;
        foreach (char c in text)
            hash = (hash ^ (byte)c) * 16777619u;
        return hash;
    }

    // ---- without a WAD ----

    private static readonly WadArchive _synthetic = new([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);

    /// <summary>A flow on the synthetic IWAD's E1M1 left as if it were map <paramref name="map"/> of episode <paramref name="episode"/>, at its finale.</summary>
    private static (GameFlow Flow, GameFlowTests.Host Host) AtFinale(GameMode mode, GameMission mission, int episode, int map, bool secret = false)
    {
        var host = new GameFlowTests.Host(mode);
        var flow = new GameFlow(host, mode, new DoomRandom(), GameVariant.vanilla, mission);
        flow.G_InitNewMap(skill_t.sk_medium, "E1M1");
        World world = host.World!;
        world.gameepisode = episode;
        world.gamemap = map;
        if (secret)
            world.G_SecretExitLevel();
        else
            world.G_ExitLevel();
        flow.G_Ticker(default);
        if (mode == GameMode.commercial)
        {
            // Doom II: the intermission first (fire: all the stats; fire: its end)
            flow.G_Ticker(new ticcmd_t { buttons = buttoncode_t.BT_ATTACK });
            flow.G_Ticker(default);
            flow.G_Ticker(new ticcmd_t { buttons = buttoncode_t.BT_ATTACK });
            for (int t = 0; t < 10 && flow.gamestate == gamestate_t.GS_INTERMISSION; t++)
                flow.G_Ticker(default);
        }
        return (flow, host);
    }

    [Theory]
    [InlineData(GameMode.shareware, GameMission.doom, 1, 8, "FLOOR4_8", nameof(FFinale.E1TEXT))]
    [InlineData(GameMode.registered, GameMission.doom, 2, 8, "SFLR6_1", nameof(FFinale.E2TEXT))]
    [InlineData(GameMode.registered, GameMission.doom, 3, 8, "MFLR8_4", nameof(FFinale.E3TEXT))]
    [InlineData(GameMode.retail, GameMission.doom, 4, 8, "MFLR8_3", nameof(FFinale.E4TEXT))]
    [InlineData(GameMode.retail, GameMission.none, 1, 8, "FLOOR4_8", nameof(FFinale.E1TEXT))]
    [InlineData(GameMode.commercial, GameMission.doom2, 1, 6, "SLIME16", nameof(FFinale.C1TEXT))]
    [InlineData(GameMode.commercial, GameMission.none, 1, 11, "RROCK14", nameof(FFinale.C2TEXT))]
    [InlineData(GameMode.commercial, GameMission.doom2, 1, 20, "RROCK07", nameof(FFinale.C3TEXT))]
    [InlineData(GameMode.commercial, GameMission.doom2, 1, 30, "RROCK17", nameof(FFinale.C4TEXT))]
    [InlineData(GameMode.commercial, GameMission.pack_tnt, 1, 6, "SLIME16", nameof(FFinale.T1TEXT))]
    [InlineData(GameMode.commercial, GameMission.pack_tnt, 1, 30, "RROCK17", nameof(FFinale.T4TEXT))]
    [InlineData(GameMode.commercial, GameMission.pack_plut, 1, 20, "RROCK07", nameof(FFinale.P3TEXT))]
    public void TheTextAndFlatAreVanillasForTheMissionEpisodeAndMap(GameMode mode, GameMission mission, int episode, int map, string flat, string text)
    {
        (GameFlow flow, _) = AtFinale(mode, mission, episode, map);
        Assert.Equal(gamestate_t.GS_FINALE, flow.gamestate);
        Assert.Equal(flat, flow.Finale.finaleflat);
        Assert.Equal((string)typeof(FFinale).GetField(text)!.GetValue(null)!, flow.Finale.finaletext);
        // before Doom II the exit's tic runs ga_completed, ga_victory and then the finale's first tic, as vanilla's
        Assert.Equal((FFinale.F_STAGE_TEXT, mode == GameMode.commercial ? 0 : 1), (flow.Finale.finalestage, flow.Finale.finalecount));
    }

    [Theory]
    [InlineData(GameMission.doom2, 15, nameof(FFinale.C5TEXT), "RROCK13")]
    [InlineData(GameMission.doom2, 31, nameof(FFinale.C6TEXT), "RROCK19")]
    [InlineData(GameMission.pack_plut, 15, nameof(FFinale.P5TEXT), "RROCK13")]
    public void Doom2sSecretExitsShowTheirTexts(GameMission mission, int map, string text, string flat)
    {
        (GameFlow flow, _) = AtFinale(GameMode.commercial, mission, 1, map, secret: true);
        Assert.Equal(gamestate_t.GS_FINALE, flow.gamestate);
        Assert.Equal((string)typeof(FFinale).GetField(text)!.GetValue(null)!, flow.Finale.finaletext);
        Assert.Equal(flat, flow.Finale.finaleflat);
    }

    [Fact]
    public void TheTextsAreVanillas()
    {
        // d_englsh.h's, spot-checked: the lengths decide the finale's time
        Assert.Equal(440, FFinale.E1TEXT.Length);
        Assert.StartsWith("Once you beat the big badasses and\n", FFinale.E1TEXT);
        Assert.EndsWith("sequel, Inferno!\n", FFinale.E1TEXT);
        Assert.Contains("you rappel down to  the surface of\n", FFinale.E2TEXT);
        Assert.EndsWith("door with you ...", FFinale.E3TEXT);
        Assert.EndsWith("next stop, hell on earth!", FFinale.E4TEXT);
        Assert.Contains("MESSAGE FROM SPACE: \"SENSORS HAVE LOCATED\n", FFinale.C2TEXT);
        Assert.Equal(22, FFinale.textscreens.Length);
        // every line fits vanilla's 320-pixel screen from x = 10 (in the shareware font's widths, at most 8)
        foreach (FFinale.textscreen_t s in FFinale.textscreens)
            Assert.All(s.text.Split('\n'), line => Assert.True(line.Length <= 44, $"{s.mission} {s.level}: {line}"));
    }

    /// <summary>
    /// f_finale.c's typing: a character every <see cref="FFinale.TEXTSPEED"/>
    /// tics after 10 (a line break counts), from 10, 10, 11 rows a line, a
    /// space 4 wide; the text for its length × 3 + 250 tics, then the end picture.
    /// </summary>
    [Fact]
    public void TheTextIsTypedOutAtVanillasSpeed()
    {
        // a font of 6×7 glyphs whose pixels are their character's code
        WadArchive wad = _synthetic;
        var hu = new HuStuff(null);
        for (int i = 0; i < HuStuff.HU_FONTSIZE; i++)
        {
            hu.hu_font[i] = new IndexedImage(6, 7, 0, 0, [.. Enumerable.Repeat((byte)(i + HuStuff.HU_FONTSTART), 6 * 7)], [.. Enumerable.Repeat((byte)1, 6 * 7)]);
        }
        var g = new ScreenGraphics(wad, hu);
        (GameFlow flow, _) = AtFinale(GameMode.shareware, GameMission.doom, 1, 8);
        FFinale fi = flow.Finale;
        var screen = new HudScreen(0, HudScreen.SCREENHEIGHT);

        // "Once you beat the big badasses and\n": after tic 10 + 3 × 6, six characters: "Once y" ("Once" 4 glyphs, a space, "y")
        Assert.Equal(1, fi.finalecount);
        for (int t = 1; t < 10 + 3 * 6; t++)
            flow.G_Ticker(default);
        Assert.Equal(28, fi.finalecount);
        fi.F_Drawer(g, screen);
        Assert.Equal((byte)'O', screen.Pixels[10 * 320 + 10]);
        Assert.Equal((byte)'E', screen.Pixels[10 * 320 + 10 + 3 * 6]);
        Assert.Equal((byte)'Y', screen.Pixels[10 * 320 + 10 + 4 * 6 + 4]); // after the space's 4
        Assert.NotEqual((byte)'O', screen.Pixels[10 * 320 + 10 + 5 * 6 + 4]); // the next character not yet
        flow.G_Ticker(default);
        flow.G_Ticker(default);
        fi.F_Drawer(g, screen);
        Assert.NotEqual((byte)'O', screen.Pixels[10 * 320 + 10 + 5 * 6 + 4]);
        flow.G_Ticker(default);
        fi.F_Drawer(g, screen);
        Assert.Equal((byte)'O', screen.Pixels[10 * 320 + 10 + 5 * 6 + 4]); // "Once yo"

        // the second line at 10, 21 once the first (35 characters with its break) is out
        while (fi.finalecount < 10 + 3 * 36)
            flow.G_Ticker(default);
        fi.F_Drawer(g, screen);
        Assert.Equal((byte)'C', screen.Pixels[21 * 320 + 10]);

        // the text for 440 × 3 + 250 tics, then the end picture
        while (fi.finalecount < 440 * 3 + 250)
            flow.G_Ticker(new ticcmd_t { buttons = buttoncode_t.BT_USE }); // presses skip nothing before Doom II
        Assert.Equal(FFinale.F_STAGE_TEXT, fi.finalestage);
        flow.G_Ticker(default);
        Assert.Equal((FFinale.F_STAGE_ARTSCREEN, 0), (fi.finalestage, fi.finalecount));
    }

    [Theory]
    [InlineData(GameMode.shareware, 1, "HELP2")]
    [InlineData(GameMode.registered, 1, "HELP2")] // vanilla's v1.9 registered game too: only the retail game's is CREDIT
    [InlineData(GameMode.retail, 1, "CREDIT")]
    [InlineData(GameMode.registered, 2, "VICTORY2")]
    [InlineData(GameMode.retail, 4, "ENDPIC")]
    public void TheEndPictureIsVanillas(GameMode mode, int episode, string page)
    {
        (GameFlow flow, _) = AtFinale(mode, GameMission.doom, episode, 8);
        FFinale fi = flow.Finale;
        while (fi.finalestage == FFinale.F_STAGE_TEXT)
            flow.G_Ticker(default);
        Assert.Equal(page, fi.ArtScreen());
        Assert.Equal(gamestate_t.GS_FINALE, flow.gamestate);
    }

    [Fact]
    public void Doom2sTextIsSkippedAfter50TicsOnly()
    {
        (GameFlow flow, GameFlowTests.Host host) = AtFinale(GameMode.commercial, GameMission.doom2, 1, 6);
        Assert.Equal(gamestate_t.GS_FINALE, flow.gamestate);
        for (int t = 0; t < 2000; t++)
            flow.G_Ticker(t <= 50 ? new ticcmd_t { buttons = buttoncode_t.BT_USE } : default); // a press up to finalecount 51 is not seen
        Assert.Equal((gamestate_t.GS_FINALE, FFinale.F_STAGE_TEXT), (flow.gamestate, flow.Finale.finalestage)); // Doom II's text stays
        flow.G_Ticker(new ticcmd_t { buttons = buttoncode_t.BT_ATTACK });
        Assert.Equal(gamestate_t.GS_DEMOSCREEN, flow.gamestate); // MAP07: not in the synthetic IWAD
        Assert.Contains("MAP07", host.Ended);
    }
}
