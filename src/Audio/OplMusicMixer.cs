using System;
using System.Collections.Generic;

namespace IsoDoom.Audio;

/// <summary>
/// T7.8g: the OPL driver (<see cref="OplMusic"/>) that <c>MusicPlayer</c>'s
/// thread renders, and the switch between OPL2 and OPL3 modes while a song
/// plays (the music option, SPEC §12 T7.8g). The mode is fixed when a driver
/// is made (Chocolate Doom reads <c>snd_dmxoption</c> once, at
/// <c>I_OPL_InitMusic</c>), so a switch makes a new driver
/// (<see cref="CreateDriver"/>, outside any lock: it reads only the bank),
/// gives it the volume and the song the old one had (played again from its
/// start, paused if it was), and keys the old one off: the old chip renders
/// on for <see cref="CrossfadeSeconds"/>, faded to nothing under the new
/// one, so its notes' release is neither cut (a click) nor left sounding
/// (a stuck note). Plain C# (linked into the tests); not thread-safe: its
/// owner holds one lock over the game thread's calls and each
/// <see cref="Mix"/>, as <c>MusicPlayer</c> does.
/// </summary>
public sealed class OplMusicMixer
{
    /// <summary>How long the old driver is heard fading out after a switch.</summary>
    public const double CrossfadeSeconds = 0.03;

    private readonly byte[] _bank;
    private readonly List<(OplMusic Driver, int Left)> _old = []; // fading out (a second switch while one fades adds one)
    private int _oldFrames;
    private short[] _scratch = [];
    private midi_file_t? _song;
    private bool _looping;

    /// <summary>A mixer of the <c>GENMIDI</c> lump <paramref name="genmidi"/> at <paramref name="sampleRate"/>, OPL3 or OPL2 (<paramref name="opl3"/>).</summary>
    public OplMusicMixer(ReadOnlySpan<byte> genmidi, int sampleRate, bool opl3 = true)
    {
        _bank = genmidi.ToArray();
        SampleRate = sampleRate;
        Driver = new OplMusic(_bank, sampleRate, opl3);
    }

    /// <summary>The output rate.</summary>
    public int SampleRate { get; }

    /// <summary>The driver the game's calls go to.</summary>
    public OplMusic Driver { get; private set; }

    /// <summary>Whether <see cref="Driver"/> is in OPL3 mode.</summary>
    public bool Opl3 => Driver.Opl3Mode;

    /// <summary>Whether an old driver is still fading out after a switch.</summary>
    public bool Crossfading => _old.Count > 0;

    /// <summary>The switches so far.</summary>
    public int Switches { get; private set; }

    /// <summary>A new driver of this mixer's bank and rate in OPL3 or OPL2 mode (no lock needed: the bank never changes).</summary>
    public OplMusic CreateDriver(bool opl3) => new(_bank, SampleRate, opl3);

    // --- the game's calls (i_oplmusic.c's), remembering the song for a switch ---

    /// <summary><c>I_OPL_PlaySong</c>, the song remembered.</summary>
    public void PlaySong(midi_file_t? song, bool looping)
    {
        Driver.I_OPL_PlaySong(song, looping);
        if (song is not null)
        {
            _song = song;
            _looping = looping;
        }
    }

    /// <summary><c>I_OPL_StopSong</c>: no song to play again.</summary>
    public void StopSong()
    {
        Driver.I_OPL_StopSong();
        _song = null;
    }

    /// <summary>
    /// Switches to <paramref name="next"/> (from <see cref="CreateDriver"/>):
    /// the volume and the song carried over (from its start; a song that ended
    /// without looping stays ended; paused stays paused), the old driver keyed
    /// off and faded out under it. Returns false (and keeps the driver) when
    /// <paramref name="next"/> is in the mode the driver already has.
    /// </summary>
    public bool SwitchTo(OplMusic next)
    {
        OplMusic old = Driver;
        if (next == old || next.Opl3Mode == old.Opl3Mode)
            return false;
        next.I_OPL_SetMusicVolume(old.MusicVolume);
        if (_song is { } song && old.I_OPL_MusicIsPlaying() && (_looping || old.RunningTracks > 0))
        {
            next.I_OPL_PlaySong(song, _looping);
            if (old.Paused)
                next.I_OPL_PauseSong();
        }
        old.I_OPL_StopSong(); // every voice keyed off: the release rings out under the fade
        _oldFrames = Math.Max(1, (int)(CrossfadeSeconds * SampleRate));
        _old.Add((old, _oldFrames));
        Driver = next;
        Switches++;
        return true;
    }

    /// <summary>
    /// Renders <paramref name="stereo"/> (interleaved pairs): the driver's
    /// output, and after a switch the old driver's fading linearly to nothing
    /// over <see cref="CrossfadeSeconds"/>, added (saturated).
    /// </summary>
    public void Mix(Span<short> stereo)
    {
        Driver.OPL_Mix_Callback(stereo);
        if (_old.Count == 0)
            return;
        if (_scratch.Length != stereo.Length)
            _scratch = new short[stereo.Length];
        int frames = stereo.Length / 2;
        for (int o = _old.Count - 1; o >= 0; o--)
        {
            (OplMusic old, int fadeLeft) = _old[o];
            old.OPL_Mix_Callback(_scratch);
            for (int i = 0; i < frames && fadeLeft - i > 0; i++)
            {
                for (int c = 0; c < 2; c++)
                {
                    int k = 2 * i + c;
                    int v = stereo[k] + (int)((long)_scratch[k] * (fadeLeft - i) / _oldFrames);
                    stereo[k] = (short)Math.Clamp(v, short.MinValue, short.MaxValue);
                }
            }
            if (fadeLeft - frames <= 0)
                _old.RemoveAt(o); // heard out: dropped
            else
                _old[o] = (old, fadeLeft - frames);
        }
    }
}
