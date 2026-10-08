using System;
using System.Collections.Generic;
using Godot;
using IsoDoom.Sim;
using IsoDoom.Wad;

namespace IsoDoom.Audio;

/// <summary>
/// The sound effects' driver (T7.7, i_sdlsound.c's part): one
/// <see cref="AudioStreamPlayer"/> per channel of <see cref="SSound"/>, each
/// on a bus of its own (<c>Sfx0</c>…, with an <see cref="AudioEffectPanner"/>)
/// that sends to <see cref="SfxBus"/>, whose volume is the options' sound
/// volume. The <c>DS*</c> lumps are decoded on first use
/// (<see cref="DmxSound"/>) into 8-bit <see cref="AudioStreamWav"/>s at their
/// own sample rate. A channel's gains are Chocolate Doom's
/// (<see cref="SSound.Gains"/>): the panner's linear law, at half amplitude
/// in the centre, gives them exactly.
/// </summary>
public partial class SfxPlayer : Node, ISoundDevice
{
    /// <summary>The bus the channels' buses send to.</summary>
    public string SfxBus { get; }

    private readonly WadArchive _wad;
    private readonly AudioStreamPlayer[] _players;
    private readonly AudioEffectPanner[] _panners;
    private readonly Dictionary<string, (AudioStreamWav? Stream, double Seconds)> _streams = new(StringComparer.Ordinal);
    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);
    private bool _paused;

    /// <summary>s_sound.c <c>snd_SfxVolume</c> (0–127): the channels' volumes are shares of it (the bus applies it).</summary>
    public int SfxVolume { get; set; } = 64;

    public SfxPlayer(WadArchive wad, int channels, string sfxBus)
    {
        Name = "SfxPlayer";
        _wad = wad;
        SfxBus = sfxBus;
        _players = new AudioStreamPlayer[channels];
        _panners = new AudioEffectPanner[channels];
        for (int i = 0; i < channels; i++)
        {
            string bus = $"{sfxBus}{i}";
            _panners[i] = EnsureChannelBus(bus, sfxBus);
            _players[i] = new AudioStreamPlayer { Name = $"Channel{i}", Bus = bus };
            AddChild(_players[i]);
        }
    }

    public SfxPlayer() : this(null!, 0, "Sfx") { } // for Godot's reflection; never used

    /// <summary>Channel <paramref name="cnum"/>'s player (the checks).</summary>
    public AudioStreamPlayer Player(int cnum) => _players[cnum];

    /// <summary>Channel <paramref name="cnum"/>'s panner (the checks).</summary>
    public AudioEffectPanner Panner(int cnum) => _panners[cnum];

    // A channel's bus (made once, kept for the process: AudioServer's buses outlive the scenes) with its panner.
    private static AudioEffectPanner EnsureChannelBus(string name, string send)
    {
        int i = AudioServer.GetBusIndex(name);
        if (i < 0)
        {
            AudioServer.AddBus();
            i = AudioServer.BusCount - 1;
            AudioServer.SetBusName(i, name);
        }
        AudioServer.SetBusSend(i, send);
        for (int e = 0; e < AudioServer.GetBusEffectCount(i); e++)
        {
            if (AudioServer.GetBusEffect(i, e) is AudioEffectPanner existing)
                return existing;
        }
        var panner = new AudioEffectPanner();
        AudioServer.AddBusEffect(i, panner);
        return panner;
    }

    /// <summary>An 8-bit mono stream of a decoded DMX sound at its own rate.</summary>
    public static AudioStreamWav ToStream(DmxSound sound) => new()
    {
        Format = AudioStreamWav.FormatEnum.Format8Bits,
        MixRate = sound.SampleRate,
        Stereo = false,
        Data = sound.ToSigned8(),
    };

    /// <summary>
    /// The stream of <paramref name="sfx"/>'s lump (its link's for a link,
    /// i_sdlsound.c <c>GetSfxLumpName</c>) and its length, decoded once; a
    /// null stream when the WAD lacks the lump or DMX would not play it
    /// (warned once).
    /// </summary>
    public (AudioStreamWav? Stream, double Seconds) StreamFor(sfxenum_t sfx)
    {
        string name = SoundInfo.LumpName(sfx);
        if (_streams.TryGetValue(name, out (AudioStreamWav? Stream, double Seconds) cached))
            return cached;
        (AudioStreamWav?, double) entry = (null, 0);
        int lump = _wad.W_CheckNumForName(name);
        if (lump < 0)
        {
            // Doom II's sounds in Doom's IWADs: usual (told when one is to play, I_StartSound)
        }
        else if (DmxSound.TryDecode(_wad.W_CacheLumpNum(lump).Span, out string? error) is DmxSound sound)
            entry = (ToStream(sound), sound.Seconds);
        else
            Warn(name, error!);
        _streams[name] = entry;
        return entry;
    }

    private void Warn(string name, string why)
    {
        if (_warned.Add(name))
            GD.PushWarning($"Sound: {name}: {why}: not played");
    }

    /// <summary>The player's volume (dB) for volume <paramref name="vol"/> of <see cref="SfxVolume"/>: half amplitude at full (Chocolate Doom's centre; the panner doubles the near side).</summary>
    public float VolumeDb(int vol) =>
        Mathf.LinearToDb(0.5f * Math.Clamp(vol, 0, 127) / Math.Max(1, SfxVolume));

    /// <summary>The panner's pan for separation <paramref name="sep"/>: −1 left, 1 right (Chocolate Doom's <c>254 − sep</c> and <c>sep</c> gains).</summary>
    public static float Pan(int sep) => Math.Clamp((sep - 127) / 127f, -1f, 1f);

    // A stream still playing (or a wrapper not freed) at exit is reported as leaked: stop and free them.
    public override void _ExitTree()
    {
        foreach (AudioStreamPlayer player in _players)
        {
            player.Stop();
            player.Stream = null;
        }
        foreach ((AudioStreamWav? stream, _) in _streams.Values)
            stream?.Dispose();
        _streams.Clear();
    }

    public double I_StartSound(sfxenum_t sfx, int cnum, int vol, int sep)
    {
        (AudioStreamWav? stream, double seconds) = StreamFor(sfx);
        AudioStreamPlayer player = _players[cnum];
        player.Stop();
        if (stream is null)
        {
            if (_warned.Add(SoundInfo.LumpName(sfx)))
                GD.Print($"Sound: {SoundInfo.LumpName(sfx)}: not in the WAD or not a DMX sound: not played");
            return 0;
        }
        player.Stream = stream;
        I_UpdateSoundParams(cnum, vol, sep);
        player.Play();
        if (_paused)
            player.StreamPaused = true; // a playback is paused, not the player: only one that plays can be
        return seconds;
    }

    public void I_StopSound(int cnum) => _players[cnum].Stop();

    public void I_UpdateSoundParams(int cnum, int vol, int sep)
    {
        _players[cnum].VolumeDb = VolumeDb(vol);
        _panners[cnum].Pan = Pan(sep);
    }

    public void I_PauseSounds(bool paused)
    {
        _paused = paused;
        foreach (AudioStreamPlayer player in _players)
            player.StreamPaused = paused;
    }
}
