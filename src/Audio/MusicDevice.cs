using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace IsoDoom.Audio;

/// <summary>
/// The music driver under <see cref="SSound"/>'s music half (i_sound.h's
/// music module, T7.8c): T7.8e's OPL player (<c>MusicPlayer</c>) in the
/// game, a <see cref="RecordingMusicDevice"/> in the tests. Calls come
/// from the game's thread, in vanilla's order: <see cref="I_RegisterSong"/>
/// then <see cref="I_PlaySong"/>; <see cref="I_StopSong"/> then
/// <see cref="I_UnRegisterSong"/>; a song paused is resumed before it is
/// stopped (s_sound.c <c>S_StopMusic</c>). <see cref="I_PlaySong"/> starts
/// a song from its beginning, not paused, whatever the last song's state
/// (SPEC §12 T7.8c).
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "Vanilla names (i_sound.h)")]
public interface IMusicDevice
{
    /// <summary>
    /// <c>I_RegisterSong</c>: the song lump <paramref name="lumpName"/>
    /// (<c>D_E1M1</c>, …) with its bytes (a MUS lump in the IWADs), made
    /// ready to play. Returns its handle, or null when the device cannot play
    /// it (not a song it reads): the song is then silent.
    /// </summary>
    object? I_RegisterSong(string lumpName, ReadOnlyMemory<byte> data);

    /// <summary><c>I_UnRegisterSong</c>: the song <paramref name="handle"/> is done with (stopped first).</summary>
    void I_UnRegisterSong(object handle);

    /// <summary><c>I_PlaySong</c>: plays <paramref name="handle"/> from its start, again and again with <paramref name="looping"/>, else once.</summary>
    void I_PlaySong(object handle, bool looping);

    /// <summary><c>I_StopSong</c>: stops the song playing (its notes off).</summary>
    void I_StopSong();

    /// <summary><c>I_PauseSong</c>: holds the song where it is (its notes off).</summary>
    void I_PauseSong();

    /// <summary><c>I_ResumeSong</c>: lets the song held by <see cref="I_PauseSong"/> go on.</summary>
    void I_ResumeSong();

    /// <summary><c>I_SetMusicVolume</c>: the music's volume, 0–127 (m_menu.c's <c>musicVolume * 8</c>).</summary>
    void I_SetMusicVolume(int volume);
}

/// <summary>
/// A <see cref="IMusicDevice"/> that plays nothing and records what it is
/// asked (T7.8c): the tests' device, and the record T7.8e's OPL player
/// (<c>MusicPlayer.Record</c>) keeps of its calls. Its state (<see cref="Song"/>, <see cref="Playing"/>,
/// <see cref="Paused"/>, <see cref="Looping"/>, <see cref="Volume"/>) is what
/// a real device would be doing; <see cref="Calls"/> keeps the last
/// <see cref="LogLength"/> calls, oldest first.
/// </summary>
public sealed class RecordingMusicDevice : IMusicDevice
{
    private sealed record Handle(string LumpName, int Length);

    private readonly List<string> _calls = [];

    /// <summary>How many calls <see cref="Calls"/> keeps.</summary>
    public const int LogLength = 64;

    /// <summary>The last calls, as <c>I_PlaySong D_E1M1 looping</c>, oldest first.</summary>
    public IReadOnlyList<string> Calls => _calls;

    /// <summary>How many calls since this was made.</summary>
    public int CallCount { get; private set; }

    /// <summary>The songs registered and not yet unregistered.</summary>
    public int Registered { get; private set; }

    /// <summary>The song lump playing (or held by a pause), null when none.</summary>
    public string? Song { get; private set; }

    /// <summary>Whether <see cref="Song"/> plays (not stopped).</summary>
    public bool Playing { get; private set; }

    /// <summary>Whether the song is held (<see cref="I_PauseSong"/>).</summary>
    public bool Paused { get; private set; }

    /// <summary>Whether <see cref="Song"/> loops.</summary>
    public bool Looping { get; private set; }

    /// <summary>The volume last set, 0–127.</summary>
    public int Volume { get; private set; } = -1;

    /// <summary>Lumps this device refuses to register (as a real device would refuse what it cannot read); none by default.</summary>
    public Func<string, ReadOnlyMemory<byte>, bool>? Refuse { get; set; }

    private void Record(string call)
    {
        CallCount++;
        if (_calls.Count == LogLength)
            _calls.RemoveAt(0);
        _calls.Add(call);
    }

    /// <inheritdoc/>
    public object? I_RegisterSong(string lumpName, ReadOnlyMemory<byte> data)
    {
        if (Refuse?.Invoke(lumpName, data) == true)
        {
            Record($"I_RegisterSong {lumpName} refused");
            return null;
        }
        Record($"I_RegisterSong {lumpName} {data.Length}");
        Registered++;
        return new Handle(lumpName, data.Length);
    }

    /// <inheritdoc/>
    public void I_UnRegisterSong(object handle)
    {
        Record($"I_UnRegisterSong {((Handle)handle).LumpName}");
        Registered--;
    }

    /// <inheritdoc/>
    public void I_PlaySong(object handle, bool looping)
    {
        var h = (Handle)handle;
        Record($"I_PlaySong {h.LumpName}{(looping ? " looping" : "")}");
        Song = h.LumpName;
        Playing = true;
        Paused = false;
        Looping = looping;
    }

    /// <inheritdoc/>
    public void I_StopSong()
    {
        Record("I_StopSong");
        Song = null;
        Playing = false;
        Paused = false;
    }

    /// <inheritdoc/>
    public void I_PauseSong()
    {
        Record("I_PauseSong");
        Paused = true;
    }

    /// <inheritdoc/>
    public void I_ResumeSong()
    {
        Record("I_ResumeSong");
        Paused = false;
    }

    /// <inheritdoc/>
    public void I_SetMusicVolume(int volume)
    {
        Record($"I_SetMusicVolume {volume}");
        Volume = volume;
    }

    /// <summary>The device's state, as the overlay shows it.</summary>
    public override string ToString() =>
        Song is null ? "stand-in: silent" : $"stand-in: {Song}{(Looping ? " looping" : " once")}{(Paused ? ", paused" : "")}, volume {Volume}";
}
