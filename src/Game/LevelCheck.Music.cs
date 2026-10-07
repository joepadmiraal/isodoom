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
        bool hasLump = wad.W_CheckNumForName(lump) >= 0;
        var device = _scene.MusicDevice as RecordingMusicDevice;
        if (sound.mus_playing != expected || sound.MusicAudible != hasLump || !sound.mus_looping
            || device is not null && (hasLump ? device.Song != lump || !device.Playing || !device.Looping : device.Song is not null))
            Fail($"music: {map}: {_scene.MusicText()}, expected {SSound.MusicName(expected)} ({lump}{(hasLump ? "" : ", not in the WAD: silent")}, looping)");
        _songsChecked++;
    }

    private void CheckMusic()
    {
        if (_scene.Sound is not { } sound || _scene.MusicDevice is not RecordingMusicDevice device || _scene.Wad is not { } wad)
        {
            Fail("music: the scene has no music (or not the stand-in device)");
            return;
        }
        GameFlow flow = _scene.Flow;
        bool commercial = _scene.GameMode == GameMode.commercial;

        // the title loop's song, once
        flow.D_StartTitle(null);
        musicenum_t title = commercial ? musicenum_t.mus_dm2ttl
            : wad.W_CheckNumForName("D_INTROA") >= 0 ? musicenum_t.mus_introa : musicenum_t.mus_intro;
        bool titleLump = wad.W_CheckNumForName(MusicInfo.LumpName(title)) >= 0;
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
}
