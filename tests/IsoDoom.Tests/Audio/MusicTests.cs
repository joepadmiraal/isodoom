using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IsoDoom.Audio;
using IsoDoom.Game;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Game;
using IsoDoom.Tests.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Audio;

/// <summary>
/// T7.8c: s_sound.c's music half (<see cref="SSound.S_ChangeMusic"/> and the
/// rest) and its callers in the game flow. Against vanilla: DOOM1.WAD's
/// routes played through the game flow (a new game, E1M1's and E1M3's
/// secret exits through the intermission to the next level, E1M8's finale
/// to its end picture, a reborn) ask for the songs vanilla's
/// <c>S_ChangeMusic</c> calls do, at the same tics, in the same game states,
/// in the same order (<c>tools/VanillaRef/wipes.sh</c>'s <c>NAME.music</c>
/// dumps; skips without DOOM1.WAD or them). Without a WAD: the table, each
/// level's song, the device's calls, the call sites (the title loop, the
/// intermission, the finale, the pause, a loaded game) and a song the WAD lacks.
/// </summary>
public class MusicTests
{
    // ---- against vanilla ----

    public static TheoryData<string> Dumps()
    {
        var data = new TheoryData<string>();
        foreach (string r in WipeTests.Routes)
            data.Add(r);
        return data;
    }

    /// <summary>A route's game, its first level from <see cref="G_InitNew"/> (the demo's <c>G_InitNew</c>), with reborns and the next levels.</summary>
    private sealed class RouteHost : IGameHost
    {
        private readonly WadArchive wad;
        private readonly VanillaRoute route;
        private World? first;

        public RouteHost(VanillaRoute route, World world, WadArchive wad)
        {
            this.route = route;
            first = world;
            this.wad = wad;
        }

        public World? World { get; private set; }
        public bool HasLump(string name) => wad.W_CheckNumForName(name) >= 0;

        public bool G_InitNew(skill_t skill, string map)
        {
            World = first;
            first = null;
            return World is not null;
        }

        public bool G_DoLoadLevel()
        {
            World!.gameaction = gameaction_t.ga_loadlevel;
            return route.Reborn(World, wad);
        }

        public bool G_DoWorldDone(string map)
        {
            World!.G_DoWorldDone(Level.Load(wad, map));
            return true;
        }

        public void G_LevelTicker(in ticcmd_t cmd, bool paused) => World!.G_Ticker(cmd);

        public void LevelCompleted()
        {
        }

        public void EndGame(string? why) => World = null;
    }

    private static Func<string, ReadOnlyMemory<byte>?> Lumps(WadArchive wad) =>
        name => wad.W_CheckNumForName(name) is int lump and >= 0 ? wad.W_CacheLumpNum(lump) : (ReadOnlyMemory<byte>?)null;

    [Theory]
    [MemberData(nameof(Dumps))]
    public void TheSongsAreVanillas(string name)
    {
        VanillaRoute route = VanillaRoute.Load(name);
        TestWads.RequireDoom1();
        string? dir = Environment.GetEnvironmentVariable(VanillaRoute.DumpDirEnvVar);
        if (string.IsNullOrEmpty(dir))
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "isodoom", "vanilla-routes");
        string path = Path.Combine(dir, $"{name}.music");
        if (!File.Exists(path))
            Assert.Skip($"No vanilla dump {path}: run tools/VanillaRef/wipes.sh (or set {VanillaRoute.DumpDirEnvVar}).");
        string[] expected = File.ReadAllLines(path).Where(l => l.Length > 0).ToArray();
        string[] tail = expected[0].Split(' ');
        Assert.Equal("tail", tail[0]);
        HashSet<int> presses = tail[1] == "-" ? new() : tail[1].Split(',').Select(int.Parse).ToHashSet();
        var cmds = new List<ticcmd_t>(route.Cmds);
        for (int t = 1; t <= int.Parse(tail[2]); t++)
            cmds.Add(new ticcmd_t { buttons = presses.Contains(t) ? buttoncode_t.BT_USE : (byte)0 });

        World world = route.NewWorld(out WadArchive wad);
        var host = new RouteHost(route, world, wad);
        var flow = new GameFlow(host, GameMode.shareware, new DoomRandom(), GameVariant.vanilla, GameMission.doom);
        var device = new RecordingMusicDevice();
        flow.Sound = new SSound(null, SSound.DefaultChannels, device, Lumps(wad));
        var actual = new List<string>();
        flow.Sound.MusicChanged = c => actual.Add($"music {flow.gametic} {(int)flow.gamestate} {MusicInfo.S_music[(int)c.musicnum].name} {(c.looping ? 1 : 0)}");

        flow.G_InitNewMap(route.Skill, route.MapLump); // the demo's G_InitNew
        for (int tic = 0; tic < cmds.Count && flow.World is not null; tic++)
        {
            if (tic < route.Cmds.Count)
                route.File.RunEvents(flow.World, tic);
            flow.G_Ticker(cmds[tic]);
        }
        Assert.Equal(expected.Skip(1), actual);
        Assert.True(actual.Count >= 2, $"{name}: {actual.Count} music changes");
        // and the device plays the last song asked for, from its lump
        string last = expected[^1].Split(' ')[3];
        Assert.Equal("D_" + last.ToUpperInvariant(), device.Song);
        Assert.True(device.Playing && device.Looping && !device.Paused);
        Assert.Equal(1, device.Registered);
    }

    [Fact]
    public void EveryDoom1LevelsSongIsInTheWad()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom1());
        for (int map = 1; map <= 9; map++)
        {
            musicenum_t song = SSound.LevelMusic(false, 1, map);
            Assert.True(wad.W_CheckNumForName(MusicInfo.LumpName(song)) >= 0, MusicInfo.LumpName(song));
        }
        foreach (musicenum_t song in new[] { musicenum_t.mus_intro, musicenum_t.mus_introa, musicenum_t.mus_inter, musicenum_t.mus_victor })
            Assert.True(wad.W_CheckNumForName(MusicInfo.LumpName(song)) >= 0, MusicInfo.LumpName(song));
        // the title plays D_INTROA, the OPL version (Chocolate Doom's for its OPL devices)
        var s = new SSound(null, SSound.DefaultChannels, new RecordingMusicDevice(), Lumps(wad));
        Assert.Equal(musicenum_t.mus_introa, s.S_StartMusic(musicenum_t.mus_intro).song);
    }

    [Fact]
    public void EveryDoom2LevelsSongIsInTheWad()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom2());
        for (int map = 1; map <= 32; map++)
        {
            musicenum_t song = SSound.LevelMusic(true, 1, map);
            Assert.True(wad.W_CheckNumForName(MusicInfo.LumpName(song)) >= 0, MusicInfo.LumpName(song));
        }
        foreach (musicenum_t song in new[] { musicenum_t.mus_dm2ttl, musicenum_t.mus_dm2int, musicenum_t.mus_read_m })
            Assert.True(wad.W_CheckNumForName(MusicInfo.LumpName(song)) >= 0, MusicInfo.LumpName(song));
    }

    // ---- the table and the levels' songs ----

    [Fact]
    public void TheTableIsSoundsCs()
    {
        Assert.Equal((int)musicenum_t.NUMMUSIC, MusicInfo.S_music.Length);
        for (int i = 1; i < (int)musicenum_t.NUMMUSIC; i++)
            Assert.Equal(((musicenum_t)i).ToString(), "mus_" + MusicInfo.S_music[i].name);
        Assert.Equal("D_E1M1", MusicInfo.LumpName(musicenum_t.mus_e1m1));
        Assert.Equal("D_DM2TTL", MusicInfo.LumpName(musicenum_t.mus_dm2ttl));
    }

    [Theory]
    [InlineData(false, 1, 1, musicenum_t.mus_e1m1)]
    [InlineData(false, 1, 9, musicenum_t.mus_e1m9)]
    [InlineData(false, 2, 3, musicenum_t.mus_e2m3)]
    [InlineData(false, 3, 9, musicenum_t.mus_e3m9)]
    [InlineData(false, 4, 1, musicenum_t.mus_e3m4)] // Chocolate Doom's spmus
    [InlineData(false, 4, 4, musicenum_t.mus_e1m5)]
    [InlineData(false, 4, 9, musicenum_t.mus_e1m9)]
    [InlineData(true, 1, 1, musicenum_t.mus_runnin)]
    [InlineData(true, 1, 15, musicenum_t.mus_runni2)]
    [InlineData(true, 1, 32, musicenum_t.mus_ultima)]
    public void EachLevelHasVanillasSong(bool commercial, int episode, int map, musicenum_t song) =>
        Assert.Equal(song, SSound.LevelMusic(commercial, episode, map));

    // ---- S_ChangeMusic and the device ----

    private static (SSound Sound, RecordingMusicDevice Device) New(params string[] lumps)
    {
        var device = new RecordingMusicDevice();
        var have = lumps.Length > 0 ? lumps.ToHashSet() : null;
        var s = new SSound(null, SSound.DefaultChannels, device,
            name => have is null || have.Contains(name) ? new ReadOnlyMemory<byte>(new byte[name.Length]) : (ReadOnlyMemory<byte>?)null);
        return (s, device);
    }

    [Fact]
    public void ASongPlaysFromItsLumpAndIsNotRestarted()
    {
        (SSound s, RecordingMusicDevice d) = New();
        Assert.Equal(music_result_t.mr_started, s.S_ChangeMusic(musicenum_t.mus_e1m1, true).result);
        Assert.Equal(new[] { "I_RegisterSong D_E1M1 6", "I_PlaySong D_E1M1 looping" }, d.Calls);
        Assert.Equal((musicenum_t.mus_e1m1, true), (s.mus_playing, s.mus_looping));
        Assert.Equal(music_result_t.mr_same, s.S_ChangeMusic(musicenum_t.mus_e1m1, false).result);
        Assert.Equal(2, d.CallCount); // the same song goes on
        s.S_StartMusic(musicenum_t.mus_inter);
        Assert.Equal(new[] { "I_StopSong", "I_UnRegisterSong D_E1M1", "I_RegisterSong D_INTER 7", "I_PlaySong D_INTER" }, d.Calls.Skip(2));
        Assert.Equal(("D_INTER", false, 1), (d.Song, d.Looping, d.Registered));
        s.S_StopMusic();
        Assert.Equal((musicenum_t.mus_None, null, 0), (s.mus_playing, d.Song, d.Registered));
        Assert.Equal("none", s.MusicText());
        Assert.Throws<ArgumentOutOfRangeException>(() => s.S_ChangeMusic(musicenum_t.NUMMUSIC, true)); // vanilla's I_Error
        Assert.Throws<ArgumentOutOfRangeException>(() => s.S_SetMusicVolume(128));
        s.S_SetMusicVolume(64);
        Assert.Equal(("I_SetMusicVolume 64", 64), (d.Calls[^1], s.snd_MusicVolume));
    }

    [Fact]
    public void TheTitleSongIsIntroaWhenTheWadHasIt()
    {
        (SSound s, _) = New("D_INTRO", "D_INTROA");
        Assert.Equal(new music_change_t(musicenum_t.mus_intro, false, musicenum_t.mus_introa, music_result_t.mr_started), s.S_StartMusic(musicenum_t.mus_intro));
        Assert.Equal(music_result_t.mr_same, s.S_StartMusic(musicenum_t.mus_intro).result);
        Assert.Equal("introa (D_INTROA, once, playing) for intro", s.MusicText());
        (s, _) = New("D_INTRO");
        Assert.Equal(musicenum_t.mus_intro, s.S_StartMusic(musicenum_t.mus_intro).song);
    }

    [Fact]
    public void ASongTheWadLacksIsSilence()
    {
        (SSound s, RecordingMusicDevice d) = New("D_E1M1", "D_E1M3");
        s.S_ChangeMusic(musicenum_t.mus_e1m1, true);
        Assert.Equal(music_result_t.mr_nolump, s.S_ChangeMusic(musicenum_t.mus_e1m2, true).result);
        Assert.Equal((musicenum_t.mus_e1m2, false, null), (s.mus_playing, s.MusicAudible, d.Song)); // the last song stopped
        Assert.Equal(music_result_t.mr_same, s.S_ChangeMusic(musicenum_t.mus_e1m2, true).result);
        Assert.Equal("e1m2 (D_E1M2, looping, silent: no lump)", s.MusicText());
        s.S_PauseSound();
        s.S_ResumeSound();
        s.PauseAll(true);
        s.PauseAll(false);
        Assert.Equal(4, d.CallCount); // nothing to pause on the device
        // a lump the device can't play: silence too
        d.Refuse = (name, _) => name == "D_E1M3";
        Assert.Equal(music_result_t.mr_unplayable, s.S_ChangeMusic(musicenum_t.mus_e1m3, true).result);
        Assert.Equal("e1m3 (D_E1M3, looping, silent: the device can't play it)", s.MusicText());
        Assert.Equal(music_result_t.mr_started, s.S_ChangeMusic(musicenum_t.mus_e1m1, true).result);
        Assert.Equal((1, "D_E1M1"), (d.Registered, d.Song));
        // no lumps at all (a WAD without music): every song silent
        var silent = new SSound(null, SSound.DefaultChannels, d);
        Assert.Equal(music_result_t.mr_nolump, silent.S_ChangeMusic(musicenum_t.mus_e1m1, true).result);
    }

    [Fact]
    public void ThePauseAndTheFocusHoldTheSong()
    {
        (SSound s, RecordingMusicDevice d) = New();
        s.S_PauseSound();
        Assert.False(s.mus_paused); // nothing playing: nothing paused, as vanilla's
        s.S_ChangeMusic(musicenum_t.mus_e1m1, true);
        s.S_PauseSound();
        Assert.True(s.mus_paused && d.Paused);
        s.PauseAll(true);
        s.PauseAll(false);
        Assert.True(d.Paused); // the focus back, the game's pause still holds it
        s.S_ResumeSound();
        Assert.True(!s.mus_paused && !d.Paused);
        s.PauseAll(true);
        Assert.True(d.Paused);
        s.S_PauseSound();
        s.S_ResumeSound();
        Assert.True(d.Paused); // the focus pause still holds it
        s.S_ChangeMusic(musicenum_t.mus_e1m2, true);
        Assert.Equal(new[] { "I_ResumeSong", "I_StopSong", "I_UnRegisterSong D_E1M1", "I_RegisterSong D_E1M2 6", "I_PlaySong D_E1M2 looping", "I_PauseSong" }, d.Calls.TakeLast(6));
        s.PauseAll(false);
        Assert.False(d.Paused);
        // a song paused by the game is resumed before it stops (S_StopMusic), mus_paused staying (vanilla's)
        s.S_PauseSound();
        s.S_StartMusic(musicenum_t.mus_intro);
        Assert.Equal(new[] { "I_PauseSong", "I_ResumeSong", "I_StopSong" }, d.Calls.TakeLast(6).Take(3));
        Assert.True(s.mus_paused && !d.Paused);
        // a level's start: no sound, not paused, its song
        s.S_Start(1, 3);
        Assert.Equal((musicenum_t.mus_e1m3, false, "D_E1M3", 3), (s.mus_playing, s.mus_paused, d.Song, s.gamemap));
        s.S_Start(1, 0); // a map the table has no song for: the music stops
        Assert.Equal((musicenum_t.mus_None, null), (s.mus_playing, d.Song));
    }

    // ---- the callers ----

    private static (GameFlow Flow, GameFlowTests.Host Host, SSound Sound, RecordingMusicDevice Device) Flow(GameMode mode = GameMode.shareware)
    {
        (GameFlow flow, GameFlowTests.Host host) = GameFlowTests.New(mode);
        host.Extra.UnionWith(new[] { "CREDIT", "HELP2" });
        (SSound s, RecordingMusicDevice d) = New();
        s.commercial = mode == GameMode.commercial;
        flow.Sound = s;
        return (flow, host, s, d);
    }

    private static readonly ticcmd_t None = default;
    private static readonly ticcmd_t Use = new() { buttons = buttoncode_t.BT_USE };
    private static readonly ticcmd_t Pause = new() { buttons = buttoncode_t.BT_SPECIAL | buttoncode_t.BTS_PAUSE };

    [Theory]
    [InlineData(GameMode.shareware, "intro")]
    [InlineData(GameMode.retail, "intro")]
    [InlineData(GameMode.commercial, "dm2ttl,dm2ttl")]
    public void TheTitleLoopPlaysItsSongOnce(GameMode mode, string expected)
    {
        (GameFlow flow, _, SSound s, RecordingMusicDevice d) = Flow(mode);
        var asked = new List<string>();
        s.MusicChanged = c => asked.Add($"{SSound.MusicName(c.musicnum)}{(c.looping ? " looping" : "")}");
        flow.D_StartTitle(null);
        for (int i = 0; i < 2000; i++)
            flow.G_Ticker(None);
        // d_main.c D_DoAdvanceDemo: S_StartMusic at the first step (Doom II's fifth too), once: the song goes on around the loop
        string[] once = expected.Split(',');
        Assert.Equal(Enumerable.Repeat(once, 10).SelectMany(o => o).Take(asked.Count), asked);
        Assert.True(asked.Count >= once.Length * 2, string.Join(" ", asked));
        Assert.Equal(2, d.CallCount); // registered and played once, not restarted
        Assert.False(d.Looping);
    }

    [Fact]
    public void ANewGameTheIntermissionAndTheNextLevelChangeTheSong()
    {
        (GameFlow flow, GameFlowTests.Host host, SSound s, _) = Flow();
        var asked = new List<string>();
        s.MusicChanged = c => asked.Add($"{flow.gametic} {flow.gamestate} {SSound.MusicName(c.musicnum)}");
        flow.D_StartTitle(null);
        flow.G_Ticker(None);
        flow.G_DeferedInitNew(skill_t.sk_medium, 1, 1);
        flow.G_Ticker(None);
        host.World!.G_ExitLevel();
        int tics = 0;
        while (flow.gamestate != gamestate_t.GS_LEVEL || host.World.level.Name != "E1M2")
        {
            flow.G_Ticker(tics % 2 == 0 ? None : Use);
            Assert.True(++tics < 100);
        }
        // the new game's at the start of the tic (G_InitNew), the intermission's at its first tic (WI_Ticker's bcnt 1)
        Assert.Equal(new[] { "0 GS_DEMOSCREEN intro", "1 GS_LEVEL e1m1", "2 GS_INTERMISSION inter", $"{2 + tics} GS_LEVEL e1m2" }, asked);
        Assert.Equal(musicenum_t.mus_e1m2, s.mus_playing);
        // a reborn: the same song goes on
        host.World.gameaction = gameaction_t.ga_loadlevel;
        flow.G_DoGameActions();
        Assert.Equal("e1m2", asked[^1].Split(' ')[2]);
        Assert.Equal(music_result_t.mr_same, s.MusicLog[^1].result);
    }

    [Fact]
    public void TheIntermissionPlaysDoom2sSong()
    {
        (GameFlow flow, GameFlowTests.Host host, SSound s, _) = Flow(GameMode.commercial);
        flow.G_InitNewMap(skill_t.sk_medium, "E1M1");
        Assert.Equal(musicenum_t.mus_runnin, s.mus_playing);
        host.World!.gamemap = 5;
        host.World.G_ExitLevel();
        flow.G_Ticker(None);
        flow.G_Ticker(None);
        Assert.Equal((gamestate_t.GS_INTERMISSION, musicenum_t.mus_dm2int, true), (flow.gamestate, s.mus_playing, s.mus_looping));
    }

    [Theory]
    [InlineData(GameMode.shareware, GameMission.doom, 1, musicenum_t.mus_victor, musicenum_t.mus_victor)]
    [InlineData(GameMode.registered, GameMission.doom, 3, musicenum_t.mus_victor, musicenum_t.mus_bunny)]
    [InlineData(GameMode.commercial, GameMission.doom2, 1, musicenum_t.mus_read_m, musicenum_t.mus_read_m)]
    public void TheFinalePlaysItsSongs(GameMode mode, GameMission mission, int episode, musicenum_t text, musicenum_t end)
    {
        (GameFlow flow, GameFlowTests.Host host, SSound s, _) = Flow(mode);
        if (mission != GameMission.doom || mode != GameMode.shareware)
        {
            // the flow's mission is the constructor's: a flow of its own
            var h = new GameFlowTests.Host(mode);
            flow = new GameFlow(h, mode, new DoomRandom(), GameVariant.vanilla, mission) { Sound = s };
            host = h;
        }
        flow.G_InitNewMap(skill_t.sk_medium, "E1M1");
        World world = host.World!;
        world.gameepisode = episode;
        world.gamemap = mode == GameMode.commercial ? 6 : 8;
        world.G_ExitLevel();
        flow.G_Ticker(None);
        if (mode == GameMode.commercial)
        {
            // Doom II: the intermission, then its text screen
            for (int i = 0; i < 40 && flow.gamestate == gamestate_t.GS_INTERMISSION; i++)
                flow.G_Ticker(i % 2 == 0 ? None : Use);
        }
        Assert.Equal(gamestate_t.GS_FINALE, flow.gamestate);
        Assert.Equal((text, true), (s.mus_playing, s.mus_looping));
        if (mode == GameMode.commercial)
            return;
        int textTics = flow.Finale.finaletext.Length * FFinale.TEXTSPEED + FFinale.TEXTWAIT;
        for (int i = 0; i < textTics; i++)
            flow.G_Ticker(None);
        Assert.Equal(1, flow.Finale.finalestage);
        Assert.Equal(end, s.mus_playing); // E3's bunny once (S_StartMusic), the others' song on
        Assert.Equal(end != text ? false : true, s.mus_looping);
    }

    [Fact]
    public void ThePauseHoldsTheSongAndANewGameOrALoadLetsItGo()
    {
        string dir = Path.Combine(Path.GetTempPath(), "isodoom-music-" + Guid.NewGuid().ToString("N"));
        try
        {
            (GameFlow flow, GameFlowTests.Host host, SSound s, RecordingMusicDevice d) = Flow();
            flow.SaveDir = dir;
            flow.G_InitNewMap(skill_t.sk_medium, "E1M2");
            flow.savegameslot = 1;
            Assert.True(flow.G_DoSaveGame());
            flow.G_Ticker(Pause);
            Assert.True(flow.paused && s.mus_paused && d.Paused);
            flow.G_Ticker(Pause);
            Assert.True(!flow.paused && !s.mus_paused && !d.Paused);
            // a new game while paused: G_InitNew's S_ResumeSound, then the level's song
            flow.G_Ticker(Pause);
            flow.G_InitNewMap(skill_t.sk_medium, "E1M1");
            Assert.True(!flow.paused && !s.mus_paused && !d.Paused);
            Assert.Equal("D_E1M1", d.Song);
            // a load while paused: the same, the save's level's song
            flow.G_Ticker(Pause);
            flow.G_LoadGame(1);
            flow.G_DoGameActions();
            Assert.Null(flow.LoadRefused);
            Assert.True(!flow.paused && !s.mus_paused && !d.Paused);
            Assert.Equal((musicenum_t.mus_e1m2, "D_E1M2"), (s.mus_playing, d.Song));
            // End Game while paused (the title loop does not resume, as vanilla's): the title song plays
            flow.G_Ticker(Pause);
            flow.D_StartTitle(null);
            Assert.Equal(("D_INTROA", false), (d.Song, d.Paused));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }
}
