using System;
using System.Threading;
using Godot;

namespace IsoDoom.Audio;

/// <summary>
/// The game's music device (T7.8e, SPEC §7.7, §12 T7.8e): T7.8d's OPL driver
/// (<see cref="OplMusic"/>, Chocolate Doom's i_oplmusic.c on opl_sdl.c's
/// software chip) played through an <see cref="AudioStreamGenerator"/> on a
/// bus (the <c>Music</c> bus in the game) at the mix rate. A dedicated thread
/// renders <see cref="BlockFrames"/>-frame blocks (opl_sdl.c's mixing
/// callback: the song's events at the samples where they fall) and pushes
/// them into the generator's ring buffer whenever a block fits, so the
/// buffer stays full (<see cref="BufferSeconds"/>, a few tens of ms); the
/// game's thread never feeds it, so a slow frame or a level load does not
/// starve it. The chip renders on while no song plays (silence, or a
/// paused song's percussion ringing out), so stops and pauses are the
/// chip's own key-offs and release envelopes, without a click.
/// <para>
/// The game thread's <see cref="IMusicDevice"/> calls go to the driver under
/// one lock, which the thread also holds around each block
/// (<see cref="OplMusic"/> is not thread-safe; Chocolate Doom holds
/// <c>OPL_Lock</c> the same way). Every call is also recorded
/// (<see cref="Record"/>, a <see cref="RecordingMusicDevice"/>: the level
/// check and the overlay read what the device was asked). Without a
/// <c>GENMIDI</c> lump nothing plays: every song is refused (silence).
/// </para>
/// <para>
/// The volume is i_oplmusic.c's (<c>I_OPL_SetMusicVolume</c>: the notes'
/// levels, vanilla's); the bus stays at unity and is only muted at 0
/// (<c>LevelScene.UpdateAudioBuses</c>; SPEC §12 T7.8e).
/// </para>
/// </summary>
public partial class MusicPlayer : Node, IMusicDevice
{
    /// <summary>The frames rendered at once (opl_sdl.c's mixing callback's buffer: steady, as the driver's timing wants).</summary>
    public const int BlockFrames = 512;

    /// <summary>The generator's ring buffer by default, in seconds (Godot rounds it up to a power of two of frames: 4096 at 48 kHz, 85 ms).</summary>
    public const double DefaultBufferSeconds = 0.06;

    /// <summary>The fade before the thread stops (<see cref="FadeOut"/>): short enough to be heard as a stop, long enough not to click.</summary>
    public const double FadeSeconds = 0.02;

    private readonly object _lock = new();
    private readonly OplMusicMixer? _mixer; // the driver, and the OPL2/OPL3 switch (T7.8g)
    private bool _opl3;
    private readonly AudioStreamPlayer _player;
    private readonly AudioStreamGenerator _stream;
    private AudioStreamGeneratorPlayback? _playback;
    private Thread? _thread;
    private volatile bool _stop;
    private bool _refuseNext;

    // the fade: the frames left of it (-1: none), then silence
    private int _fadeLeft = -1;
    private int _fadeFrames;

    private long _skipsBase = -1; // the generator's skips once the buffer was first full
    private long _framesPushed;
    private long _renderTicks; // Stopwatch ticks spent rendering (under the lock)
    private long _blocks;
    private long _worstTicks; // the slowest block's render
    private double _recentLoad; // the render's share of a core, averaged over the last blocks
    private int _peak; // the largest sample since PeakLevel was last read

    /// <summary>What the device was asked, as the stand-in records it (its state: song, playing, paused, looping, volume).</summary>
    public RecordingMusicDevice Record { get; } = new();

    /// <summary>The mix rate the chip renders at (<see cref="AudioServer.GetMixRate"/>).</summary>
    public int MixRate { get; }

    /// <summary>The ring buffer asked for, in seconds (at least <see cref="DriverMinimumSeconds"/>).</summary>
    public double BufferSeconds { get; }

    /// <summary>Whether the device can play songs (the WAD has a <c>GENMIDI</c> bank).</summary>
    public bool CanPlay => _mixer is not null;

    /// <summary>The driver (null without a bank); hold <see cref="Lock"/> to touch it (a mode switch replaces it).</summary>
    public OplMusic? Driver => _mixer?.Driver;

    /// <summary>The driver's mixer (null without a bank); hold <see cref="Lock"/> to touch it.</summary>
    public OplMusicMixer? Mixer => _mixer;

    /// <summary>
    /// T7.8g: whether the chip is an OPL3 (18 voices, the default) or an OPL2
    /// (9 voices: Chocolate Doom's default, the AdLib's sound); the option's
    /// value, kept without a bank too.
    /// </summary>
    public bool Opl3 => _opl3;

    /// <summary>The lock over <see cref="Driver"/>.</summary>
    public object Lock => _lock;

    /// <summary>The generator's ring buffer, in frames (0 until the stream plays).</summary>
    public int BufferFrames { get; private set; }

    /// <summary>The frames waiting in the ring buffer now.</summary>
    public int BufferedFrames => _playback is { } p && BufferFrames > 0 ? Math.Max(0, BufferFrames - p.GetFramesAvailable()) : 0;

    /// <summary>The frames the thread rendered and pushed so far.</summary>
    public long FramesPushed => Interlocked.Read(ref _framesPushed);

    /// <summary>
    /// The buffer underruns since the buffer was first full: the mixes that
    /// found it short and played silence (the generator's skips).
    /// </summary>
    public long Underruns => _playback is { } p && Interlocked.Read(ref _skipsBase) is long b and >= 0 ? p.GetSkips() - b : 0;

    /// <summary>The share of a core the thread spent rendering, over the frames pushed (their time at the mix rate).</summary>
    public double RenderLoad
    {
        get
        {
            long frames = FramesPushed;
            return frames == 0 ? 0 : (double)Interlocked.Read(ref _renderTicks) / System.Diagnostics.Stopwatch.Frequency / ((double)frames / MixRate);
        }
    }

    /// <summary>The largest sample (0–32768) rendered since the last read: the checks hear whether notes sound.</summary>
    public int TakePeak() => Interlocked.Exchange(ref _peak, 0);

    /// <summary>The share of a core the thread spends rendering now (averaged over about the last 2 s).</summary>
    public double RecentLoad => Volatile.Read(ref _recentLoad);

    /// <summary>The slowest block's render so far, in ms (the lock's wait included).</summary>
    public double WorstBlockMs => Interlocked.Read(ref _worstTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    /// <summary>Whether the thread runs.</summary>
    public bool Running => _thread is { IsAlive: true };

    /// <summary>Whether a fade (<see cref="FadeOut"/>) is over: the thread renders silence.</summary>
    public bool Faded => Volatile.Read(ref _fadeLeft) == 0;

    /// <summary>
    /// A player on <paramref name="bus"/> of the bank <paramref name="genmidi"/>
    /// (the <c>GENMIDI</c> lump, or null: no songs), its ring buffer
    /// <paramref name="bufferSeconds"/> long. It plays once in the tree.
    /// </summary>
    public MusicPlayer(ReadOnlyMemory<byte>? genmidi, string bus, double bufferSeconds = DefaultBufferSeconds, bool opl3 = true)
    {
        Name = "MusicPlayer";
        ProcessMode = ProcessModeEnum.Always;
        MixRate = (int)AudioServer.GetMixRate();
        BufferSeconds = Math.Max(bufferSeconds, DriverMinimumSeconds(MixRate));
        _opl3 = opl3;
        if (genmidi is { } bank)
            _mixer = new OplMusicMixer(bank.Span, MixRate, opl3); // Doom 1.9's driver (T7.8d)
        _stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = (float)BufferSeconds };
        _player = new AudioStreamPlayer { Name = "Music", Stream = _stream, Bus = bus };
        AddChild(_player);
        Record.Refuse = (_, _) => _refuseNext;
    }

    /// <summary>The frames Godot's dummy driver (headless runs) mixes at once, every 93 ms at 44.1 kHz (audio_driver_dummy.h's <c>buffer_frames</c>).</summary>
    public const int DummyDriverFrames = 4096;

    /// <summary>
    /// The shortest ring buffer the output driver allows: two of its periods
    /// (the driver mixes a period at once; the dummy driver's is 4096 frames,
    /// a real one's about <see cref="AudioServer.GetOutputLatency"/>) and two
    /// blocks, so a mix never finds it short while a block is under way.
    /// </summary>
    public static double DriverMinimumSeconds(int mixRate)
    {
        double period = AudioServer.GetDriverName() == "Dummy" ? (double)DummyDriverFrames / mixRate : AudioServer.GetOutputLatency();
        return 2 * period + 2.0 * BlockFrames / mixRate;
    }

    public MusicPlayer() : this(null, "Master") { } // for Godot's reflection; never used

    public override void _Ready()
    {
        _player.Play();
        _playback = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();
        BufferFrames = _playback.GetFramesAvailable(); // empty: all of it
        _stop = false;
        _thread = new Thread(Run) { Name = "IsoDoom music", IsBackground = true, Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public override void _ExitTree() => StopThread();

    /// <summary>Stops the thread (and the stream), waiting for it: the scene's end, the quit.</summary>
    public void StopThread()
    {
        _stop = true;
        _thread?.Join();
        _thread = null;
        if (IsInsideTree())
            _player.Stop();
        _playback?.Dispose(); // the wrapper's reference, else reported as leaked at exit
        _playback = null;
    }

    /// <summary>
    /// Fades the output to silence over <see cref="FadeSeconds"/> (the thread
    /// renders on, silent, until it stops): before a quit, so the stream's end
    /// does not click.
    /// </summary>
    public void FadeOut()
    {
        lock (_lock)
        {
            if (_fadeLeft < 0)
            {
                _fadeFrames = Math.Max(1, (int)(FadeSeconds * MixRate));
                _fadeLeft = _fadeFrames;
            }
        }
    }

    /// <summary>
    /// T7.8g: switches the chip to OPL3 or OPL2 (<paramref name="opl3"/>) at
    /// once: a new driver, made off the lock, takes the volume and the song
    /// (played again from its start) under it, while the old one's notes are
    /// keyed off and fade out beneath (<see cref="OplMusicMixer.SwitchTo"/>):
    /// no click, no stuck note, and the render thread only ever sees one
    /// driver per block. Returns whether the mode changed.
    /// </summary>
    public bool SetOpl3(bool opl3)
    {
        if (opl3 == _opl3)
            return false;
        _opl3 = opl3;
        if (_mixer is null)
            return true;
        OplMusic next = _mixer.CreateDriver(opl3);
        lock (_lock)
            _mixer.SwitchTo(next);
        return true;
    }

    /// <summary>The wall-clock time a fade takes to be heard out: the fade and the ring buffer behind it.</summary>
    public double FadeOutSeconds => FadeSeconds + (BufferFrames > 0 ? (double)BufferFrames / MixRate : BufferSeconds) + 0.02;

    private void Run()
    {
        var block = new short[BlockFrames * 2];
        var frames = new Vector2[BlockFrames];
        while (!_stop)
        {
            AudioStreamGeneratorPlayback? playback = _playback;
            if (playback is null)
                break;
            if (playback.GetFramesAvailable() < BlockFrames)
            {
                if (Interlocked.Read(ref _skipsBase) < 0)
                    Interlocked.Exchange(ref _skipsBase, playback.GetSkips());
                Thread.Sleep(2);
                continue;
            }
            int fadeFrom, fadeLeft;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            lock (_lock)
            {
                if (_mixer is { } mixer)
                    mixer.Mix(block);
                else
                    Array.Clear(block);
                fadeFrom = _fadeFrames;
                fadeLeft = _fadeLeft;
                if (_fadeLeft > 0)
                    _fadeLeft = Math.Max(0, _fadeLeft - BlockFrames);
            }
            long took = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            Interlocked.Add(ref _renderTicks, took);
            // the load over about the last 2 s (256 blocks at 48 kHz), the JIT's warm-up left behind
            double load = took / (double)System.Diagnostics.Stopwatch.Frequency / ((double)BlockFrames / MixRate);
            Volatile.Write(ref _recentLoad, _recentLoad + (load - _recentLoad) / 256);
            if (took > Interlocked.Read(ref _worstTicks))
                Interlocked.Exchange(ref _worstTicks, took);
            const float scale = 1f / 32768f;
            int peak = 0;
            for (int i = 0; i < BlockFrames; i++)
            {
                peak = Math.Max(peak, Math.Max(Math.Abs((int)block[2 * i]), Math.Abs((int)block[2 * i + 1])));
                float gain = fadeLeft < 0 ? 1f : fadeLeft - i <= 0 ? 0f : (float)(fadeLeft - i) / fadeFrom;
                frames[i] = new Vector2(block[2 * i] * scale * gain, block[2 * i + 1] * scale * gain);
            }
            if (peak > Volatile.Read(ref _peak))
                Interlocked.Exchange(ref _peak, peak); // the only writer but TakePeak's reset: a lost peak is harmless
            playback.PushBuffer(frames);
            Interlocked.Add(ref _framesPushed, BlockFrames);
            _blocks++;
        }
    }

    // --- IMusicDevice: each call recorded, then the driver's under the lock ---

    private sealed record Handle(object Recorded, midi_file_t Song);

    /// <inheritdoc/>
    public object? I_RegisterSong(string lumpName, ReadOnlyMemory<byte> data)
    {
        midi_file_t? song;
        lock (_lock)
            song = _mixer?.Driver.I_OPL_RegisterSong(data.Span);
        _refuseNext = song is null;
        object? recorded = Record.I_RegisterSong(lumpName, data);
        _refuseNext = false;
        return recorded is null || song is null ? null : new Handle(recorded, song);
    }

    /// <inheritdoc/>
    public void I_UnRegisterSong(object handle)
    {
        var h = (Handle)handle;
        Record.I_UnRegisterSong(h.Recorded);
        lock (_lock)
            _mixer?.Driver.I_OPL_UnRegisterSong(h.Song);
    }

    /// <inheritdoc/>
    public void I_PlaySong(object handle, bool looping)
    {
        var h = (Handle)handle;
        Record.I_PlaySong(h.Recorded, looping);
        lock (_lock)
            _mixer?.PlaySong(h.Song, looping);
    }

    /// <inheritdoc/>
    public void I_StopSong()
    {
        Record.I_StopSong();
        lock (_lock)
            _mixer?.StopSong();
    }

    /// <inheritdoc/>
    public void I_PauseSong()
    {
        Record.I_PauseSong();
        lock (_lock)
            _mixer?.Driver.I_OPL_PauseSong();
    }

    /// <inheritdoc/>
    public void I_ResumeSong()
    {
        Record.I_ResumeSong();
        lock (_lock)
            _mixer?.Driver.I_OPL_ResumeSong();
    }

    /// <inheritdoc/>
    public void I_SetMusicVolume(int volume)
    {
        Record.I_SetMusicVolume(volume);
        lock (_lock)
            _mixer?.Driver.I_OPL_SetMusicVolume(volume);
    }

    /// <summary>The driver's state under the lock (<see cref="OplMusic.ToString"/>), or why it is silent.</summary>
    public string DriverText()
    {
        if (_mixer is null)
            return "no GENMIDI: silent";
        lock (_lock)
            return _mixer.Driver.ToString();
    }

    /// <summary>The overlay's: the driver, the song asked for, the ring buffer's level, the underruns, the render cost.</summary>
    public override string ToString() =>
        $"{DriverText()}; buffer {BufferedFrames * 1000.0 / MixRate:0}/{BufferFrames * 1000.0 / MixRate:0} ms, underruns {Underruns}, "
        + $"{FramesPushed / (double)MixRate:0.0} s rendered, {RenderLoad * 100:0.0}% of a core ({RecentLoad * 100:0.0}% now, worst block {WorstBlockMs:0.0} ms){(Running ? "" : ", stopped")}";
}
