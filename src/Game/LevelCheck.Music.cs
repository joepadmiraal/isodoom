using Godot;
using IsoDoom.Audio;
using IsoDoom.Sim;
using IsoDoom.Wad;

namespace IsoDoom.Game;

// T7.8c: the music changes through the scene (no renderer needed; the music
// device is a stand-in that records what it is asked until T7.8e): every
// map's song at its load (s_sound.c S_Start's table: the song asked for,
// its lump on the device, or silence when the WAD lacks it); then the title
// loop's song (D_INTROA for D_INTRO on an OPL device, Doom II's D_DM2TTL),
// a new game's, the game's pause and the focus pause holding it, the music
// volume, and a level load over the paused game letting it go.
public partial class LevelCheck
{
    private int _songsChecked;

    /// <summary>T7.8c: the song of <paramref name="map"/>, just loaded: <c>S_Start</c>'s, on the device when the WAD has its lump.</summary>
    private void CheckMapMusic(string map)
    {
        if (_scene.Sound is not { } sound || _scene.Wad is not { } wad)
            return;
        musicenum_t expected = SSound.LevelMusic(_scene.GameMode == GameMode.commercial, World.EpisodeNumber(map), World.MapNumber(map));
        bool inTable = expected > musicenum_t.mus_None && expected < musicenum_t.NUMMUSIC;
        if (!inTable)
        {
            if (sound.mus_playing != musicenum_t.mus_None)
                Fail($"music: {map}: {sound.MusicText()}, expected none (no song in the table)");
            return;
        }
        string lump = MusicInfo.LumpName(expected);
        bool hasLump = wad.W_CheckNumForName(lump) >= 0 && _scene.MusicDevice is { CanPlay: true };
        RecordingMusicDevice? device = _scene.MusicRecord;
        if (sound.mus_playing != expected || sound.MusicAudible != hasLump || !sound.mus_looping
            || device is not null && (hasLump ? device.Song != lump || !device.Playing || !device.Looping : device.Song is not null))
            Fail($"music: {map}: {_scene.MusicText()}, expected {SSound.MusicName(expected)} ({lump}{(hasLump ? "" : ", not in the WAD: silent")}, looping)");
        _songsChecked++;
    }

    private void CheckMusic()
    {
        if (_scene.Sound is not { } sound || _scene.MusicRecord is not { } device || _scene.Wad is not { } wad)
        {
            Fail("music: the scene has no music device");
            return;
        }
        GameFlow flow = _scene.Flow;
        bool commercial = _scene.GameMode == GameMode.commercial;

        // the title loop's song, once
        flow.D_StartTitle(null);
        musicenum_t title = commercial ? musicenum_t.mus_dm2ttl
            : wad.W_CheckNumForName("D_INTROA") >= 0 ? musicenum_t.mus_introa : musicenum_t.mus_intro;
        bool titleLump = wad.W_CheckNumForName(MusicInfo.LumpName(title)) >= 0 && _scene.MusicDevice!.CanPlay;
        if (sound.mus_playing != title || sound.mus_looping || titleLump && device.Song != MusicInfo.LumpName(title))
            Fail($"music: the title loop: {_scene.MusicText()}, expected {SSound.MusicName(title)} once");

        // a new game: its first map's song
        _scene.StartNewGame();
        flow.G_DoGameActions();
        if (_scene.World is not { } world)
        {
            Fail("music: no new game");
            return;
        }
        CheckMapMusic(world.level.Name);

        // the game's pause holds the song (and only the song); the focus pause too
        var none = new ticcmd_t();
        var pause = new ticcmd_t { buttons = buttoncode_t.BT_SPECIAL | buttoncode_t.BTS_PAUSE };
        bool audible = sound.MusicAudible;
        _scene.Tic(pause);
        bool held = sound.mus_paused && (!audible || device.Paused);
        _scene.Tic(pause);
        if (!held || sound.mus_paused || audible && device.Paused)
            Fail($"music: the pause: held {held}, then {_scene.MusicText()}");
        sound.PauseAll(true);
        held = !audible || device.Paused;
        sound.PauseAll(false);
        if (!held || audible && device.Paused)
            Fail($"music: the focus pause: held {held}, then {_scene.MusicText()}");

        // the music volume: m_menu.c's musicVolume * 8 on the device
        int volume = _scene.Menu.musicVolume;
        _scene.Menu.musicVolume = volume == 15 ? 14 : volume + 1;
        _scene.UpdateSound(0);
        bool followed = sound.snd_MusicVolume == _scene.Menu.musicVolume * 8 && device.Volume == sound.snd_MusicVolume;
        if (_scene.MusicDevice?.Driver is { } opl)
            lock (_scene.MusicDevice.Lock)
                followed &= opl.MusicVolume == sound.snd_MusicVolume; // T7.8e: the OPL driver's volume (the notes' levels)
        _scene.Menu.musicVolume = volume;
        _scene.UpdateSound(0);
        if (!followed || device.Volume != volume * 8)
            Fail($"music: the volume: {device.Volume}, expected {volume * 8} (musicVolume {volume} × 8)");

        // a level loaded over the paused game (G_InitNew's S_ResumeSound): its song plays
        _scene.Tic(pause);
        _scene.LoadMap(world.level.Name);
        if (sound.mus_paused || flow.paused || audible && device.Paused)
            Fail($"music: a new game over the pause: {_scene.MusicText()}");

        _scene.Tic(none);
        GD.Print($"Level check: music (T7.8c): {_songsChecked} levels' songs, the title's ({SSound.MusicName(title)}), the pause, the focus pause and the volume; device: {device.CallCount} calls, {device.Registered} song registered");
    }

    /// <summary>
    /// T7.8e: the OPL player's thread plays a song for a few seconds of wall
    /// time (headless too: the dummy driver mixes at real time): the frames
    /// pushed keep pace with the mix rate, notes sound (and stop under the
    /// pause), and the ring buffer never ran dry.
    /// </summary>
    private async System.Threading.Tasks.Task CheckMusicPlayback()
    {
        if (_scene.MusicDevice is not { } player || _scene.Sound is not { } sound)
        {
            Fail("music playback: no music device");
            return;
        }
        if (!player.Running)
        {
            Fail($"music playback: the thread does not run ({player})");
            return;
        }
        if (!player.CanPlay)
        {
            GD.Print($"Level check: music playback (T7.8e): no GENMIDI, silent ({player})");
            return;
        }
        // the title's song (D_INTROA or D_INTRO, Doom II's D_DM2TTL), from its start
        _scene.Flow.D_StartTitle(null);
        if (!sound.MusicAudible)
        {
            GD.Print($"Level check: music playback (T7.8e): the title has no song to play ({_scene.MusicText()})");
            return;
        }
        int volume = _scene.Menu.musicVolume;
        if (volume == 0)
            _scene.Menu.musicVolume = 8;
        _scene.UpdateSound(0);

        const double Seconds = 3;
        long pushed0 = player.FramesPushed;
        player.TakePeak();
        ulong t0 = Time.GetTicksUsec();
        while (Time.GetTicksUsec() - t0 < (ulong)(Seconds * 1e6))
            await NextFrame();
        double elapsed = (Time.GetTicksUsec() - t0) / 1e6;
        double rendered = (player.FramesPushed - pushed0) / (double)player.MixRate;
        int peak = player.TakePeak();
        // short of the wall clock with the buffer full: the driver drains slowly (T8.4a); or a Debug build over half a core
        string slowDriver = rendered >= elapsed - 0.25 ? "" : player.KeepsUp ? " (the audio driver drained slower than real time, the buffer full)"
            : player.SlowBuild ? " (behind in a Debug build over half a core: not failed)" : "";
        if ((rendered < elapsed - 0.25 && slowDriver.Length == 0) || rendered > elapsed + 0.25)
            Fail($"music playback: {rendered:0.00} s rendered in {elapsed:0.00} s of wall time ({player})");
        if (peak < 256)
            Fail($"music playback: the song is silent (peak {peak}; {player})");

        // the pause keys the melodic voices off: the sequencer holds; then it goes on
        sound.S_PauseSound();
        bool held;
        lock (player.Lock)
            held = player.Driver!.Paused;
        sound.S_ResumeSound();
        bool resumed;
        lock (player.Lock)
            resumed = !player.Driver!.Paused;
        if (!held || !resumed)
            Fail($"music playback: the pause: held {held}, resumed {resumed} ({player})");

        // T7.8g: the option switches the chip while the song plays: the song again from its start on an OPL2, heard, then back
        string title = SSound.MusicName(sound.mus_playing);
        // (on the first map's song, looping: the title's plays once and may have ended)
        _scene.Flow.S_ChangeMusic(_scene.GameMode == GameMode.commercial ? musicenum_t.mus_runnin : musicenum_t.mus_e1m1, true);
        string sw = "no song to switch";
        if (sound.MusicAudible)
        {
            string chip = _scene.GetSetting("sound/opl");
            sw = $"{SSound.MusicName(sound.mus_playing)}: " + await CheckOplSwitch(player, sound, chip == "opl3" ? "opl2" : "opl3");
            sw += ", " + await CheckOplSwitch(player, sound, chip);
        }

        // a Debug build over half a core: underruns reported, not failed (the export's check fails on them)
        string slowBuild = player.Underruns != 0 && player.SlowBuild ? $" ({player.Underruns} underrun(s) in a Debug build over half a core: not failed)" : "";
        if (player.Underruns != 0 && slowBuild.Length == 0)
            Fail($"music playback: {player.Underruns} buffer underrun(s) ({player})");
        _scene.Menu.musicVolume = volume;
        _scene.UpdateSound(0);
        GD.Print($"Level check: music playback (T7.8e): {title} for {elapsed:0.0} s, {rendered:0.00} s rendered{slowDriver}, peak {peak}; switched (T7.8g): {sw}{slowBuild}; {player}");
    }

    /// <summary>
    /// T7.8g: the music option set to <paramref name="chip"/> through the
    /// scene while a song plays: the driver changes mode at once, plays the
    /// song again (from its start, the volume kept, the old chip faded out
    /// under it), notes sound within a second, and the buffer never runs dry.
    /// </summary>
    private async System.Threading.Tasks.Task<string> CheckOplSwitch(MusicPlayer player, SSound sound, string chip)
    {
        int volume;
        lock (player.Lock)
            volume = player.Driver!.MusicVolume;
        _scene.SetSetting("sound/opl", chip);
        bool opl3, playing;
        int volumeAfter;
        lock (player.Lock)
        {
            opl3 = player.Driver!.Opl3Mode;
            playing = player.Driver.I_OPL_MusicIsPlaying();
            volumeAfter = player.Driver.MusicVolume;
        }
        if (opl3 != (chip == "opl3") || !playing || volumeAfter != volume || !sound.MusicAudible)
            Fail($"music playback: the chip switched to {chip}: OPL3 {opl3}, playing {playing}, volume {volumeAfter} (was {volume}) ({player})");
        player.TakePeak();
        ulong t0 = Time.GetTicksUsec();
        while (Time.GetTicksUsec() - t0 < 1_000_000)
            await NextFrame();
        int peak = player.TakePeak();
        bool fading;
        lock (player.Lock)
            fading = player.Mixer!.Crossfading;
        if (peak < 256 || fading)
            Fail($"music playback: after the switch to {chip} the song is silent (peak {peak}) or the old chip still fades ({fading}) ({player})");
        return $"{chip} peak {peak}";
    }
}
