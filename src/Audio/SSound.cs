using System;
using System.Collections.Generic;
using IsoDoom.Sim;
using static IsoDoom.Map.Fixed;

namespace IsoDoom.Audio;

/// <summary>
/// The sound driver under <see cref="SSound"/> (i_sound.h's sound module,
/// T7.7): the Godot players in the game, a recorder in the tests. A channel
/// number is the handle (as Chocolate Doom's i_sdlsound.c).
/// </summary>
public interface ISoundDevice
{
    /// <summary>
    /// <c>I_StartSound</c>: plays <paramref name="sfx"/> (its lump, or its
    /// link's) on channel <paramref name="cnum"/> at volume <paramref name="vol"/>
    /// (0–127, of <see cref="SSound.snd_SfxVolume"/>) and separation <paramref name="sep"/>
    /// (0 left, 128 centre, 255 right). Returns how long it plays in seconds, or 0
    /// when it cannot (no lump, or not one DMX plays).
    /// </summary>
    double I_StartSound(sfxenum_t sfx, int cnum, int vol, int sep);

    /// <summary><c>I_StopSound</c>: stops channel <paramref name="cnum"/>.</summary>
    void I_StopSound(int cnum);

    /// <summary><c>I_UpdateSoundParams</c>: channel <paramref name="cnum"/>'s new volume and separation.</summary>
    void I_UpdateSoundParams(int cnum, int vol, int sep);

    /// <summary>Not vanilla: holds every channel where it is (the window lost its focus), or lets them go on.</summary>
    void I_PauseSounds(bool paused);
}

/// <summary>
/// The listener of <see cref="SSound"/>: vanilla's <c>players[consoleplayer].mo</c>
/// (<see cref="mo"/>, null without one: everything then plays as from no
/// origin), at map point <see cref="x"/>, <see cref="y"/> (fixed_t), facing
/// <see cref="angle"/> for the stereo separation (BAM; the game camera's
/// screen-up direction under the iso view, SPEC §12 T7.7).
/// </summary>
public readonly record struct sound_listener_t(mobj_t? mo, int x, int y, uint angle);

/// <summary>What an <see cref="SSound.S_StartSound"/> came to (for the overlay, the level script and the tests).</summary>
public enum sound_result_t
{
    /// <summary>Playing on a channel.</summary>
    sr_played,

    /// <summary>Too far (or a link's volume below 1): s_sound.c returns before taking a channel.</summary>
    sr_inaudible,

    /// <summary>Every channel busy with sounds of better (lower) priority.</summary>
    sr_nochannel,

    /// <summary>Took a channel, but the device cannot play it (no lump, or not a DMX sound): freed at the next update.</summary>
    sr_nolump,
}

/// <summary>A start <see cref="SSound"/> handled: the sound, its origin, where it was, and what came of it (channel, volume, separation).</summary>
public readonly record struct sound_start_t(sfxenum_t sfx, object? origin, int x, int y, sound_result_t result, int cnum, int vol, int sep);

/// <summary>What an <see cref="SSound.S_ChangeMusic"/> came to (T7.8c; the level script, the overlay, the tests).</summary>
public enum music_result_t
{
    /// <summary>The song started (<c>I_PlaySong</c>).</summary>
    mr_started,

    /// <summary>The song was playing already: not restarted (vanilla's <c>mus_playing == music</c>).</summary>
    mr_same,

    /// <summary>The WAD has no such song lump (vanilla's <c>I_Error</c>): silence.</summary>
    mr_nolump,

    /// <summary>The device cannot play the lump (<c>I_RegisterSong</c> gave no handle): silence.</summary>
    mr_unplayable,
}

/// <summary>
/// An <see cref="SSound.S_ChangeMusic"/> call (T7.8c): the song asked for
/// (<paramref name="musicnum"/>, before <c>mus_intro</c> becomes
/// <c>mus_introa</c>), <paramref name="looping"/>, the song it came to and
/// what came of it.
/// </summary>
public readonly record struct music_change_t(musicenum_t musicnum, bool looping, musicenum_t song, music_result_t result);

/// <summary>
/// s_sound.c (Chocolate Doom's, T7.7) in plain C#, no Godot types: the
/// channels (<see cref="snd_channels"/>, 8), their priorities
/// (<c>S_GetChannel</c>), a new sound from an origin stopping its last one,
/// the distance attenuation and stereo separation (<c>S_AdjustSoundParams</c>,
/// with the listener's angle the presentation's: <see cref="sound_listener_t.angle"/>),
/// the channels following their origins every frame (<c>S_UpdateSounds</c>)
/// and the per-level reset (<c>S_Start</c>'s sound part). Vanilla asks the
/// driver whether a channel still plays (<c>I_SoundIsPlaying</c>); here each
/// channel counts its sound's length down itself (<see cref="Advance"/>), so
/// the channels behave the same with any audio driver (Godot's dummy one
/// under <c>--headless</c> included). The sim's events feed it
/// (<see cref="World.events"/>: starts with their origin, stops for removed
/// mobjs); the menus', intermission's and finale's sounds come with no origin.
/// The music half (T7.8c): <see cref="S_ChangeMusic"/>, <see cref="S_StartMusic"/>,
/// <see cref="S_StopMusic"/>, the pause's <see cref="S_PauseSound"/> and
/// <see cref="S_ResumeSound"/>, <see cref="S_SetMusicVolume"/> and
/// <see cref="S_Start"/>'s song for the level, on an <see cref="IMusicDevice"/>;
/// the game flow calls them where vanilla's code does (<c>GameFlow.Sound</c>).
/// </summary>
public sealed class SSound
{
    // s_sound.c's constants.
    /// <summary>s_sound.c <c>S_CLIPPING_DIST</c>: no sound beyond 1200 units (but on map 8).</summary>
    public const int S_CLIPPING_DIST = 1200 * FRACUNIT;

    /// <summary>s_sound.c <c>S_CLOSE_DIST</c>: full volume within 200 units.</summary>
    public const int S_CLOSE_DIST = 200 * FRACUNIT;

    /// <summary>s_sound.c <c>S_ATTENUATOR</c>.</summary>
    public const int S_ATTENUATOR = (S_CLIPPING_DIST - S_CLOSE_DIST) >> FRACBITS;

    /// <summary>s_sound.c <c>S_STEREO_SWING</c>.</summary>
    public const int S_STEREO_SWING = 96 * FRACUNIT;

    /// <summary>s_sound.c <c>NORM_SEP</c>: centre.</summary>
    public const int NORM_SEP = 128;

    /// <summary>s_sound.c <c>snd_channels</c>' default.</summary>
    public const int DefaultChannels = 8;

    /// <summary>s_sound.c <c>channel_t</c>: <see cref="sfxinfo"/> null when free.</summary>
    public sealed class channel_t
    {
        public sfxinfo_t? sfxinfo;
        public sfxenum_t sfx;
        /// <summary>The origin: a <see cref="mobj_t"/>, a <see cref="sector_t"/> (its <c>soundorg</c>) or null.</summary>
        public object? origin;
        /// <summary>The volume and separation it plays at now.</summary>
        public int vol, sep;
        /// <summary>Not vanilla: the seconds its sound has left to play (<c>I_SoundIsPlaying</c> while above 0).</summary>
        public double remaining;
    }

    private readonly ISoundDevice? _device;
    private readonly IMusicDevice? _music;
    private readonly Func<string, ReadOnlyMemory<byte>?>? _lumps;

    /// <summary>s_sound.c <c>channels</c>.</summary>
    public readonly channel_t[] channels;

    /// <summary>s_sound.c <c>snd_channels</c>.</summary>
    public int snd_channels => channels.Length;

    /// <summary>s_sound.c <c>snd_SfxVolume</c>: 0–127 (m_menu.c sets <c>sfxVolume * 8</c>).</summary>
    public int snd_SfxVolume = 8 * 8;

    /// <summary>g_game.c <c>gamemap</c>: on map 8 (any episode, as vanilla) sounds carry over the whole map.</summary>
    public int gamemap;

    /// <summary>Not vanilla: every sound at the centre (no stereo separation).</summary>
    public bool mono;

    /// <summary>s_sound.c <c>mus_paused</c>: the pause's (<see cref="S_PauseSound"/>), for the music (T7.8).</summary>
    public bool mus_paused;

    /// <summary>Not vanilla: everything held (the window lost its focus, <see cref="PauseAll"/>).</summary>
    public bool AllPaused { get; private set; }

    /// <summary>The last <see cref="LogLength"/> starts, oldest first.</summary>
    public IReadOnlyList<sound_start_t> Log => _log;

    private readonly List<sound_start_t> _log = [];

    /// <summary>How many starts <see cref="Log"/> keeps.</summary>
    public const int LogLength = 64;

    /// <summary>How many starts came to each <see cref="sound_result_t"/> since this was made.</summary>
    public readonly int[] Results = new int[Enum.GetValues<sound_result_t>().Length];

    /// <summary>How many times each sound took a channel (<see cref="sound_result_t.sr_played"/> or <see cref="sound_result_t.sr_nolump"/>).</summary>
    public readonly int[] ChannelStarts = new int[(int)sfxenum_t.NUMSFX];

    /// <summary>
    /// The channels on <paramref name="device"/>, the music on
    /// <paramref name="music"/>, the song lumps from <paramref name="lumps"/>
    /// (a lump's bytes by name, null when the WAD lacks it; without it every
    /// song is silent).
    /// </summary>
    public SSound(ISoundDevice? device, int numChannels = DefaultChannels, IMusicDevice? music = null,
        Func<string, ReadOnlyMemory<byte>?>? lumps = null)
    {
        _device = device;
        _music = music;
        _lumps = lumps;
        channels = new channel_t[numChannels];
        for (int i = 0; i < numChannels; i++)
            channels[i] = new channel_t();
    }

    /// <summary>Where <paramref name="origin"/> is now: a mobj's position, a sector's <c>soundorg</c>.</summary>
    public static (int x, int y) OriginPosition(object origin) => origin switch
    {
        mobj_t mo => (mo.x, mo.y),
        sector_t sec => (sec.map.SoundOrgX, sec.map.SoundOrgY),
        _ => throw new ArgumentException($"not a sound origin: {origin}"),
    };

    /// <summary>s_sound.c <c>I_SoundIsPlaying</c>, from the channel's own count.</summary>
    public bool I_SoundIsPlaying(int cnum) => channels[cnum].remaining > 0;

    private void S_StopChannel(int cnum)
    {
        channel_t c = channels[cnum];
        if (c.sfxinfo is null)
            return;
        // stop the sound playing (the driver too when the channel's own count ran out: its clock may lag)
        _device?.I_StopSound(cnum);
        c.remaining = 0;
        c.sfxinfo = null;
        c.origin = null;
        c.sfx = sfxenum_t.sfx_None;
    }

    /// <summary>
    /// s_sound.c <c>S_Start</c> (p_setup.c <c>P_SetupLevel</c> calls it at
    /// every level load, map <paramref name="gamemap"/> of episode
    /// <paramref name="gameepisode"/>): "kill all playing sounds at start of
    /// level (trust me - a good idea)", then the level's music, looping
    /// (<see cref="LevelMusic"/>). A map the table has no song for (vanilla's
    /// <c>I_Error</c>) stops the music.
    /// </summary>
    public void S_Start(int gameepisode, int gamemap)
    {
        this.gamemap = gamemap;
        StopChannels();

        // start new music for the level
        mus_paused = false;
        _pausedByGame = false; // not vanilla: a song the pause held and S_ChangeMusic keeps goes on (SPEC §12 T7.8c)
        SetSongPaused(AllPaused);

        musicenum_t mnum = LevelMusic(commercial, gameepisode, gamemap);
        if (mnum <= musicenum_t.mus_None || mnum >= musicenum_t.NUMMUSIC)
        {
            S_StopMusic();
            return;
        }
        S_ChangeMusic(mnum, true);
    }

    /// <summary>Chocolate Doom's s_sound.c <c>spmus</c>: the fourth episode's songs (the Ultimate Doom), by map.</summary>
    private static readonly musicenum_t[] spmus =
    [
        // Song - Who? - Where?
        musicenum_t.mus_e3m4, // American     e4m1
        musicenum_t.mus_e3m2, // Romero       e4m2
        musicenum_t.mus_e3m3, // Shawn        e4m3
        musicenum_t.mus_e1m5, // American     e4m4
        musicenum_t.mus_e2m7, // Tim          e4m5
        musicenum_t.mus_e2m4, // Romero       e4m6
        musicenum_t.mus_e2m6, // J.Anderson   e4m7 CHIRON.WAD
        musicenum_t.mus_e2m5, // Shawn        e4m8
        musicenum_t.mus_e1m9, // Tim          e4m9
    ];

    /// <summary>
    /// s_sound.c <c>S_Start</c>'s song for a level: Doom II's
    /// <c>mus_runnin + gamemap − 1</c>, else <c>mus_e1m1 + (gameepisode − 1) × 9
    /// + gamemap − 1</c>, the fourth episode's from <c>spmus</c>;
    /// <see cref="musicenum_t.mus_None"/> or beyond the table when there is none.
    /// </summary>
    public static musicenum_t LevelMusic(bool commercial, int gameepisode, int gamemap)
    {
        if (commercial)
            return musicenum_t.mus_runnin + gamemap - 1;
        if (gameepisode < 4)
            return musicenum_t.mus_e1m1 + (gameepisode - 1) * 9 + gamemap - 1;
        return gamemap >= 1 && gamemap <= spmus.Length ? spmus[gamemap - 1] : musicenum_t.mus_None;
    }

    /// <summary>Not vanilla: stops every channel (<see cref="S_Start"/>'s sound part; the scene's quit).</summary>
    public void StopChannels()
    {
        for (int cnum = 0; cnum < snd_channels; cnum++)
        {
            if (channels[cnum].sfxinfo is not null)
                S_StopChannel(cnum);
        }
    }

    /// <summary>s_sound.c <c>S_StopSound</c>: stops the first channel playing from <paramref name="origin"/> (null too: a sound with none).</summary>
    public void S_StopSound(object? origin)
    {
        for (int cnum = 0; cnum < snd_channels; cnum++)
        {
            if (channels[cnum].sfxinfo is not null && channels[cnum].origin == origin)
            {
                S_StopChannel(cnum);
                break;
            }
        }
    }

    /// <summary>s_sound.c <c>S_GetChannel</c>: a free channel, the origin's own, or one with a sound of no better priority; −1 if none.</summary>
    private int S_GetChannel(object? origin, sfxinfo_t sfxinfo, sfxenum_t sfx)
    {
        int cnum;
        // Find an open channel
        for (cnum = 0; cnum < snd_channels; cnum++)
        {
            if (channels[cnum].sfxinfo is null)
                break;
            else if (origin is not null && channels[cnum].origin == origin)
            {
                S_StopChannel(cnum);
                break;
            }
        }
        // None available
        if (cnum == snd_channels)
        {
            // Look for lower priority
            for (cnum = 0; cnum < snd_channels; cnum++)
            {
                if (channels[cnum].sfxinfo!.priority >= sfxinfo.priority)
                    break;
            }
            if (cnum == snd_channels)
                return -1; // No lower priority. Sorry, Charlie.
            S_StopChannel(cnum); // Otherwise, kick out lower priority.
        }
        channel_t c = channels[cnum];
        c.sfxinfo = sfxinfo;
        c.sfx = sfx;
        c.origin = origin;
        return cnum;
    }

    /// <summary>
    /// s_sound.c <c>S_AdjustSoundParams</c>: the volume and separation of a
    /// sound at (<paramref name="x"/>, <paramref name="y"/>) for
    /// <paramref name="listener"/>, false when it is not audible (beyond
    /// <see cref="S_CLIPPING_DIST"/> but on map 8, or a volume of 0).
    /// <paramref name="vol"/> comes in as the sound's full volume.
    /// </summary>
    public bool S_AdjustSoundParams(in sound_listener_t listener, int x, int y, ref int vol, out int sep)
    {
        // calculate the distance to sound origin and clip it if necessary
        int adx = Math.Abs(listener.x - x);
        int ady = Math.Abs(listener.y - y);
        // From _GG1_ p.428. Appox. eucledian distance fast.
        int approx_dist = adx + ady - ((adx < ady ? adx : ady) >> 1);
        sep = NORM_SEP;
        if (gamemap != 8 && approx_dist > S_CLIPPING_DIST)
            return false;

        // angle of source to listener
        uint angle = Tables.R_PointToAngle2(listener.x, listener.y, x, y);
        if (angle > listener.angle)
            angle -= listener.angle;
        else
            angle = unchecked(angle + (0xffffffff - listener.angle));
        angle >>= Tables.ANGLETOFINESHIFT;

        // stereo separation
        if (!mono)
            sep = 128 - (FixedMul(S_STEREO_SWING, Tables.finesine[(int)angle]) >> FRACBITS);

        // volume calculation
        if (approx_dist < S_CLOSE_DIST)
            vol = snd_SfxVolume;
        else if (gamemap == 8)
        {
            if (approx_dist > S_CLIPPING_DIST)
                approx_dist = S_CLIPPING_DIST;
            vol = 15 + (snd_SfxVolume - 15) * ((S_CLIPPING_DIST - approx_dist) >> FRACBITS) / S_ATTENUATOR;
        }
        else
        {
            // distance effect
            vol = snd_SfxVolume * ((S_CLIPPING_DIST - approx_dist) >> FRACBITS) / S_ATTENUATOR;
        }
        return vol > 0;
    }

    /// <summary>
    /// s_sound.c <c>S_StartSound</c>: <paramref name="sfx_id"/> from
    /// <paramref name="origin"/> (a <see cref="mobj_t"/>, a <see cref="sector_t"/>
    /// or null: heard everywhere at full volume), which was at
    /// (<paramref name="x"/>, <paramref name="y"/>) when it started (the
    /// event's position: its mobj may be gone). An origin's last sound stops
    /// (only if this one is audible, as vanilla's); a sound from the listener
    /// plays at full volume in the centre.
    /// </summary>
    public sound_start_t S_StartSound(object? origin, int x, int y, sfxenum_t sfx_id, in sound_listener_t listener)
    {
        if (sfx_id < sfxenum_t.sfx_pistol || sfx_id >= sfxenum_t.NUMSFX)
            throw new ArgumentOutOfRangeException(nameof(sfx_id), $"Bad sfx #: {(int)sfx_id}");
        sfxinfo_t sfx = SoundInfo.S_sfx[(int)sfx_id];
        int volume = snd_SfxVolume;
        int sep;

        // Initialize sound parameters
        if (sfx.link is not null)
        {
            volume += sfx.volume;
            if (volume < 1)
                return Record(new sound_start_t(sfx_id, origin, x, y, sound_result_t.sr_inaudible, -1, volume, NORM_SEP));
            if (volume > snd_SfxVolume)
                volume = snd_SfxVolume;
        }

        // Check to see if it is audible, and if not, modify the params
        if (origin is not null && listener.mo is not null && origin != listener.mo)
        {
            bool rc = S_AdjustSoundParams(listener, x, y, ref volume, out sep);
            if (x == listener.x && y == listener.y)
                sep = NORM_SEP;
            if (!rc)
                return Record(new sound_start_t(sfx_id, origin, x, y, sound_result_t.sr_inaudible, -1, volume, sep));
        }
        else
            sep = NORM_SEP;

        // kill old sound
        S_StopSound(origin);

        // try to find a channel
        int cnum = S_GetChannel(origin, sfx, sfx_id);
        if (cnum < 0)
            return Record(new sound_start_t(sfx_id, origin, x, y, sound_result_t.sr_nochannel, -1, volume, sep));

        channel_t c = channels[cnum];
        c.vol = volume;
        c.sep = sep;
        c.remaining = _device?.I_StartSound(sfx_id, cnum, volume, sep) ?? 0;
        ChannelStarts[(int)sfx_id]++;
        return Record(new sound_start_t(sfx_id, origin, x, y, c.remaining > 0 ? sound_result_t.sr_played : sound_result_t.sr_nolump, cnum, volume, sep));
    }

    private sound_start_t Record(sound_start_t start)
    {
        Results[(int)start.result]++;
        if (_log.Count == LogLength)
            _log.RemoveAt(0);
        _log.Add(start);
        return start;
    }

    /// <summary>
    /// s_sound.c <c>S_PauseSound</c> (the game's pause, g_game.c <c>G_Ticker</c>'s
    /// <c>BTS_PAUSE</c>): vanilla pauses only the music; the sounds play out.
    /// </summary>
    public void S_PauseSound()
    {
        if (mus_playing != musicenum_t.mus_None && !mus_paused)
        {
            _pausedByGame = true;
            SetSongPaused(true); // I_PauseSong
            mus_paused = true;
        }
    }

    /// <summary>s_sound.c <c>S_ResumeSound</c>.</summary>
    public void S_ResumeSound()
    {
        if (mus_playing != musicenum_t.mus_None && mus_paused)
        {
            _pausedByGame = false;
            SetSongPaused(AllPaused); // I_ResumeSong (the focus pause holds it on)
            mus_paused = false;
        }
    }

    /// <summary>Not vanilla: holds every channel and the music where they are (the window's focus lost), or lets them go on.</summary>
    public void PauseAll(bool paused)
    {
        if (paused == AllPaused)
            return;
        AllPaused = paused;
        _device?.I_PauseSounds(paused);
        SetSongPaused(paused || _pausedByGame);
    }

    // ---- the music (T7.8c) ----

    /// <summary>s_sound.c <c>mus_playing</c>: the song playing (or silent: no lump, or one the device can't play), <see cref="musicenum_t.mus_None"/> for none.</summary>
    public musicenum_t mus_playing { get; private set; }

    /// <summary>Whether <see cref="mus_playing"/> loops (<see cref="S_ChangeMusic"/>'s <c>looping</c>).</summary>
    public bool mus_looping { get; private set; }

    /// <summary>The song asked for last (<see cref="S_ChangeMusic"/>'s <c>musicnum</c> before <c>mus_intro</c> becomes <c>mus_introa</c>).</summary>
    public musicenum_t mus_requested { get; private set; }

    /// <summary>Whether <see cref="mus_playing"/> plays on the device (else silent: no lump, or not one it plays).</summary>
    public bool MusicAudible => _handle is not null;

    /// <summary>Whether the device holds the song (the game's pause or the focus pause).</summary>
    public bool MusicHeld => _songPaused;

    /// <summary>s_sound.c's music volume (<see cref="S_SetMusicVolume"/>): 0–127, −1 before it is set.</summary>
    public int snd_MusicVolume { get; private set; } = -1;

    /// <summary>doomstat.h <c>gamemode == commercial</c> (Doom II): <see cref="S_Start"/>'s songs are Doom II's.</summary>
    public bool commercial;

    /// <summary>The last <see cref="LogLength"/> <see cref="S_ChangeMusic"/> calls, oldest first.</summary>
    public IReadOnlyList<music_change_t> MusicLog => _musicLog;

    private readonly List<music_change_t> _musicLog = [];

    /// <summary>How many <see cref="S_ChangeMusic"/> calls since this was made.</summary>
    public int MusicChanges { get; private set; }

    // music->handle of mus_playing (null: silent), whether the device holds the song,
    // and whether the game's pause holds it (S_PauseSound since the song started).
    private object? _handle;
    private music_result_t _songResult;
    private bool _songPaused, _pausedByGame;

    // The device's I_PauseSong / I_ResumeSong, only on a change.
    private void SetSongPaused(bool paused)
    {
        if (_handle is null || paused == _songPaused)
            return;
        _songPaused = paused;
        if (paused)
            _music?.I_PauseSong();
        else
            _music?.I_ResumeSong();
    }

    /// <summary>s_sound.c <c>S_SetMusicVolume</c> (m_menu.c <c>M_MusicVol</c>: <c>musicVolume * 8</c>): 0–127, else vanilla's <c>I_Error</c>.</summary>
    public void S_SetMusicVolume(int volume)
    {
        if (volume < 0 || volume > 127)
            throw new ArgumentOutOfRangeException(nameof(volume), $"Attempt to set music volume at {volume}");
        snd_MusicVolume = volume;
        _music?.I_SetMusicVolume(volume);
    }

    /// <summary>s_sound.c <c>S_StartMusic</c>: <paramref name="m_id"/> once (the title loop's).</summary>
    public music_change_t S_StartMusic(musicenum_t m_id) => S_ChangeMusic(m_id, false);

    /// <summary>
    /// s_sound.c <c>S_ChangeMusic</c> (Chocolate Doom's): song
    /// <paramref name="musicnum"/>, looping or once. <c>D_INTROA</c> plays for
    /// <c>mus_intro</c> when the WAD has it (Chocolate Doom's choice for its
    /// OPL music devices, which ours always is). The song playing is not
    /// restarted; another is stopped (<see cref="S_StopMusic"/>), and the
    /// lump <c>D_</c> + name is registered and played. A lump the WAD lacks
    /// (vanilla's <c>I_Error</c>) or one the device can't play leaves the
    /// music silent, the song still counted as playing (so asking for it
    /// again changes nothing).
    /// </summary>
    public music_change_t S_ChangeMusic(musicenum_t musicnum, bool looping)
    {
        musicenum_t requested = musicnum;
        // The Doom IWAD file has two versions of the intro music: d_intro
        // and d_introa.  The latter is used for OPL playback.
        if (musicnum == musicenum_t.mus_intro && _lumps?.Invoke(MusicInfo.LumpName(musicenum_t.mus_introa)) is not null)
            musicnum = musicenum_t.mus_introa;

        if (musicnum <= musicenum_t.mus_None || musicnum >= musicenum_t.NUMMUSIC)
            throw new ArgumentOutOfRangeException(nameof(musicnum), $"Bad music number {(int)musicnum}");
        mus_requested = requested;

        if (mus_playing == musicnum)
            return RecordMusic(new music_change_t(requested, looping, musicnum, music_result_t.mr_same));

        // shutdown old music
        S_StopMusic();

        string lump = MusicInfo.LumpName(musicnum);
        mus_playing = musicnum;
        mus_looping = looping;
        if (_lumps?.Invoke(lump) is not ReadOnlyMemory<byte> data)
            return RecordMusic(new music_change_t(requested, looping, musicnum, music_result_t.mr_nolump));
        _handle = _music?.I_RegisterSong(lump, data);
        if (_handle is null)
            return RecordMusic(new music_change_t(requested, looping, musicnum, _music is null ? music_result_t.mr_started : music_result_t.mr_unplayable));
        _music!.I_PlaySong(_handle, looping);
        _songPaused = false;
        SetSongPaused(AllPaused); // not vanilla: the focus pause holds a new song too
        return RecordMusic(new music_change_t(requested, looping, musicnum, music_result_t.mr_started));
    }

    private music_change_t RecordMusic(music_change_t change)
    {
        if (change.result != music_result_t.mr_same)
            _songResult = change.result;
        MusicChanges++;
        if (_musicLog.Count == LogLength)
            _musicLog.RemoveAt(0);
        _musicLog.Add(change);
        MusicChanged?.Invoke(change);
        return change;
    }

    /// <summary>Called at each <see cref="S_ChangeMusic"/> (the vanilla reference's dump point: the tests').</summary>
    public Action<music_change_t>? MusicChanged;

    /// <summary>
    /// s_sound.c <c>S_StopMusic</c>: the song playing stops (resumed first
    /// when held, as vanilla's) and is unregistered. <see cref="mus_paused"/>
    /// stays as it is (vanilla's).
    /// </summary>
    public void S_StopMusic()
    {
        if (mus_playing == musicenum_t.mus_None)
            return;
        if (_handle is not null)
        {
            SetSongPaused(false); // if (mus_paused) I_ResumeSong()
            _music!.I_StopSong();
            _music.I_UnRegisterSong(_handle);
        }
        _handle = null;
        _songPaused = _pausedByGame = false;
        mus_playing = musicenum_t.mus_None;
    }

    /// <summary>The song as the overlay and the level script show it: <c>e1m1 (D_E1M1, looping, playing)</c>, <c>none</c>.</summary>
    public string MusicText()
    {
        if (mus_playing == musicenum_t.mus_None)
            return "none";
        string state = MusicAudible ? (_songPaused ? "paused" : "playing")
            : _songResult == music_result_t.mr_unplayable ? "silent: the device can't play it"
            : _songResult == music_result_t.mr_nolump ? "silent: no lump"
            : "no device";
        return $"{MusicName(mus_playing)} ({MusicInfo.LumpName(mus_playing)}, {(mus_looping ? "looping" : "once")}, {state})"
            + (mus_requested != mus_playing ? $" for {MusicName(mus_requested)}" : "");
    }

    /// <summary>The song's name without <c>mus_</c> (<c>e1m1</c>, <c>intro</c>; <c>none</c>).</summary>
    public static string MusicName(musicenum_t music) => music == musicenum_t.mus_None ? "none" : MusicInfo.S_music[(int)music].name;

    /// <summary>Not vanilla: <paramref name="seconds"/> of play pass (the driver's mixing, vanilla's <c>I_SoundIsPlaying</c>); nothing while <see cref="AllPaused"/>.</summary>
    public void Advance(double seconds)
    {
        if (AllPaused || seconds <= 0)
            return;
        foreach (channel_t c in channels)
        {
            if (c.remaining > 0)
                c.remaining = Math.Max(0, c.remaining - seconds);
        }
    }

    /// <summary>
    /// s_sound.c <c>S_UpdateSounds</c> (each frame, d_main.c <c>D_DoomLoop</c>):
    /// frees the channels whose sound ended, and moves the others' volume and
    /// separation with their origin (a mobj moves; the listener too),
    /// stopping those out of hearing.
    /// </summary>
    public void S_UpdateSounds(in sound_listener_t listener)
    {
        for (int cnum = 0; cnum < snd_channels; cnum++)
        {
            channel_t c = channels[cnum];
            sfxinfo_t? sfx = c.sfxinfo;
            if (sfx is null)
                continue;
            if (!I_SoundIsPlaying(cnum))
            {
                // if channel is allocated but sound has stopped, free it
                S_StopChannel(cnum);
                continue;
            }
            // initialize parameters
            int volume = snd_SfxVolume;
            int sep = NORM_SEP;
            if (sfx.link is not null)
            {
                volume += sfx.volume;
                if (volume < 1)
                {
                    S_StopChannel(cnum);
                    continue;
                }
                if (volume > snd_SfxVolume)
                    volume = snd_SfxVolume;
            }
            // check non-local sounds for distance clipping or modify their params
            if (c.origin is not null && listener.mo is not null && listener.mo != c.origin)
            {
                (int x, int y) = OriginPosition(c.origin);
                if (!S_AdjustSoundParams(listener, x, y, ref volume, out sep))
                    S_StopChannel(cnum);
                else if (volume != c.vol || sep != c.sep)
                {
                    c.vol = volume;
                    c.sep = sep;
                    _device?.I_UpdateSoundParams(cnum, volume, sep);
                }
            }
        }
    }

    /// <summary>
    /// Chocolate Doom's i_sdlsound.c <c>I_SDL_UpdateSoundParams</c> stereo
    /// law: the left and right gains (0–1) of a sound at volume
    /// <paramref name="vol"/> (0–127) and separation <paramref name="sep"/>:
    /// <c>(254 − sep) × vol / 127</c> and <c>sep × vol / 127</c> of 255 (the
    /// centre at about half amplitude each side).
    /// </summary>
    public static (double Left, double Right) Gains(int vol, int sep)
    {
        int left = Math.Clamp((254 - sep) * vol / 127, 0, 255);
        int right = Math.Clamp(sep * vol / 127, 0, 255);
        return (left / 255.0, right / 255.0);
    }

    /// <summary>A channel as the overlay and the level script list it: the sound, its origin, volume, separation and time left.</summary>
    public static string OriginText(object? origin, mobj_t? player) => origin switch
    {
        null => "-",
        sector_t sec => $"sector {sec.Index}",
        mobj_t mo when mo == player => "player",
        mobj_t mo => mo.type.ToString().Replace("MT_", "", StringComparison.Ordinal).ToLowerInvariant(),
        _ => origin.ToString() ?? "?",
    };

    /// <summary>The sound's name without <c>sfx_</c>.</summary>
    public static string SfxName(sfxenum_t sfx) => sfx.ToString().Replace("sfx_", "", StringComparison.Ordinal);
}
