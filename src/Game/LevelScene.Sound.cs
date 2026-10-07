using System;
using System.Linq;
using Godot;
using IsoDoom.Audio;
using IsoDoom.Render;
using IsoDoom.Sim;
using IsoDoom.Wad;

namespace IsoDoom.Game;

// T7.7: the sound effects: s_sound.c (SSound, plain C#) fed by the sim's
// events and the menus', played by SfxPlayer (a Godot player per channel).
public partial class LevelScene
{
    /// <summary>T7.7: where the stereo separation's "ahead" is (<c>--level-sound-stereo</c>, SPEC §12 T7.7).</summary>
    public enum SoundStereo
    {
        /// <summary>The camera's screen-up direction: a sound to the left on screen is heard on the left (the default).</summary>
        Screen,

        /// <summary>The player's facing, as vanilla's (its view is the screen there).</summary>
        Facing,

        /// <summary>No separation: every sound in the centre.</summary>
        Off,
    }

    /// <summary>T7.7: s_sound.c's channels (null until the WAD is open).</summary>
    public SSound? Sound { get; private set; }

    /// <summary>T7.7: the channels' players.</summary>
    public SfxPlayer? SfxDevice { get; private set; }

    /// <summary>
    /// T7.8c: the music device: a stand-in that records what it is asked
    /// (<see cref="RecordingMusicDevice"/>) until T7.8e's OPL player takes
    /// its place here (<see cref="InitSound"/>).
    /// </summary>
    public IMusicDevice? MusicDevice { get; private set; }

    /// <summary>T7.7: <c>--level-sound-stereo=screen|facing|off</c>.</summary>
    public SoundStereo Stereo { get; set; } = SoundStereo.Screen;

    /// <summary>vanilla's <c>I_WaitVBL(105)</c> before quitting: the quit sound's time to play (105 of 70 Hz).</summary>
    public const double QuitWaitSeconds = 105 / 70.0;

    /// <summary>T7.7: the menus' Quit is waiting for its sound (<see cref="QuitWaitSeconds"/>); nothing runs meanwhile.</summary>
    public bool Quitting { get; private set; }

    /// <summary>T7.7: <c>--level-sound-stereo</c>'s value.</summary>
    public static SoundStereo ParseStereo(string value) => value switch
    {
        "screen" => SoundStereo.Screen,
        "facing" or "vanilla" => SoundStereo.Facing,
        "off" or "mono" => SoundStereo.Off,
        _ => throw new ArgumentException($"--level-sound-stereo: \"{value}\" (screen, facing or off)"),
    };

    /// <summary>Sets the sound up once the WAD is open and the <c>Sfx</c> bus exists (<see cref="InitSettings"/>).</summary>
    private void InitSound()
    {
        if (WadLocator.GetUserArg("--level-sound-stereo") is string stereo)
            Stereo = ParseStereo(stereo);
        SfxDevice = new SfxPlayer(Wad!, SSound.DefaultChannels, SfxBus);
        AddChild(SfxDevice);
        MusicDevice = new RecordingMusicDevice(); // T7.8c: T7.8e's OPL player goes here
        WadArchive wad = Wad!;
        Sound = new SSound(SfxDevice, SSound.DefaultChannels, MusicDevice,
            name => wad.W_CheckNumForName(name) is int lump and >= 0 ? wad.W_CacheLumpNum(lump) : (ReadOnlyMemory<byte>?)null)
        {
            commercial = GameMode == GameMode.commercial,
        };
        if (_flow is not null)
            _flow.Sound = Sound; // the flow changes the music and pauses it (T7.8c)
        SyncSoundVolume();
    }

    // m_menu.c M_SfxVol: S_SetSfxVolume(sfxVolume * 8); M_MusicVol: S_SetMusicVolume(musicVolume * 8) (T7.8c), on a change.
    private void SyncSoundVolume()
    {
        if (Sound is null || _flow is null)
            return;
        Sound.snd_SfxVolume = Menu.sfxVolume * 8;
        SfxDevice!.SfxVolume = Sound.snd_SfxVolume;
        Sound.mono = Stereo == SoundStereo.Off;
        if (Sound.snd_MusicVolume != Menu.musicVolume * 8)
            Sound.S_SetMusicVolume(Menu.musicVolume * 8);
    }

    /// <summary>
    /// T7.7: the listener: the console player's mobj (none on the title loop:
    /// sounds then play as from no origin) and the angle the separation is
    /// measured from (<see cref="Stereo"/>: the current camera's screen-up
    /// direction, the game camera's when none, or the player's facing).
    /// </summary>
    public sound_listener_t SoundListener()
    {
        mobj_t? mo = World is { } w && w.playeringame[w.consoleplayer] ? w.players[w.consoleplayer].mo : null;
        if (mo is null)
            return default;
        uint angle = Stereo == SoundStereo.Facing ? mo.angle : ScreenUpAngle();
        return new sound_listener_t(mo, mo.x, mo.y, angle);
    }

    /// <summary>T7.7: the map direction (BAM) that is up on screen: the current camera's view, or the game camera's (map north-west) without one.</summary>
    public uint ScreenUpAngle()
    {
        if (IsInsideTree() && GetViewport().GetCamera3D() is Camera3D camera)
            return ThingSprites.BamOf(ThingSprites.ViewDirection(camera));
        return ThingSprites.BamOf(-Basis.FromEuler(new Vector3(0, Mathf.DegToRad(IsoCamera.Yaw), 0)).Z);
    }

    /// <summary>T7.7: a started sound event to the channels (s_sound.c <c>S_StartSound</c>).</summary>
    private void StartSoundEvent(in sim_event_t e)
    {
        if (Sound is null)
            return;
        object? origin = (object?)e.sound.origin ?? e.sound.sector;
        Sound.S_StartSound(origin, e.x, e.y, e.sound.sfx, SoundListener());
    }

    /// <summary>
    /// T7.7, each frame (d_main.c's <c>S_UpdateSounds</c> after the tics): the
    /// volumes, the focus pause (everything holds, the music too; the game's
    /// pause, which holds only the music, is the flow's: T7.8c), the
    /// channels' time, then the channels follow their origins and the listener.
    /// </summary>
    public void UpdateSound(double delta)
    {
        if (Sound is null)
            return;
        SyncSoundVolume();
        Sound.gamemap = World?.gamemap ?? 0;
        Sound.PauseAll(FocusPaused);
        Sound.Advance(delta);
        Sound.S_UpdateSounds(SoundListener());
    }

    /// <summary>T7.7: the channels playing, one per line (the overlay, the level script's <c>channels</c>).</summary>
    public string ChannelsText()
    {
        if (Sound is not { } s)
            return "no sound";
        mobj_t? player = PlayerMobj;
        var lines = s.channels.Select((c, i) => c.sfxinfo is null ? null
            : $"ch{i} {SSound.SfxName(c.sfx)}@{SSound.OriginText(c.origin, player)} vol {c.vol}/{s.snd_SfxVolume} sep {c.sep} {c.remaining:0.00}s")
            .Where(l => l is not null);
        string text = string.Join("\n", lines);
        return text.Length == 0 ? "no channel playing" : text;
    }

    /// <summary>T7.8c: the music as the overlay and the level script show it: the song (<see cref="SSound.MusicText"/>) and the device's state.</summary>
    public string MusicText() => Sound is not { } s ? "no sound" : $"{s.MusicText()}; {MusicDevice}";

    /// <summary>A start as the overlay and the level script list it.</summary>
    public string StartText(in sound_start_t st) =>
        $"{SSound.SfxName(st.sfx)}@{SSound.OriginText(st.origin, PlayerMobj)}: "
        + st.result switch
        {
            sound_result_t.sr_played => $"ch{st.cnum} vol {st.vol} sep {st.sep}",
            sound_result_t.sr_nolump => $"ch{st.cnum}, no lump",
            sound_result_t.sr_inaudible => "out of hearing",
            _ => "no channel free",
        };

    /// <summary>
    /// T7.7: quits with exit code <paramref name="code"/> once the sounds are
    /// stopped and the audio server had time to free their playbacks (Godot
    /// reports those still playing at exit as leaked); the level script's end.
    /// </summary>
    public async System.Threading.Tasks.Task QuitQuietly(int code)
    {
        if (Sound is { } s && s.channels.Any(c => c.sfxinfo is not null))
        {
            s.StopChannels();
            await AudioSettled();
        }
        Sound?.S_StopMusic(); // i_sound.c I_ShutdownMusic (T7.8c)
        GetTree().Quit(code);
    }

    /// <summary>
    /// T7.7: waits 150 ms of wall-clock time (frames go on: <c>--fixed-fps</c>
    /// makes frames fast in a headless run), in which the audio server frees
    /// the playbacks stopped so far.
    /// </summary>
    public async System.Threading.Tasks.Task AudioSettled()
    {
        ulong until = Time.GetTicksMsec() + 150;
        while (Time.GetTicksMsec() < until)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>
    /// T7.7: m_menu.c <c>M_QuitResponse</c>'s <c>I_WaitVBL(105)</c>: the quit
    /// sound plays out (nothing else runs) before the game quits.
    /// </summary>
    private async void QuitAfterSound()
    {
        if (Quitting)
            return;
        Quitting = true;
        await ToSignal(GetTree().CreateTimer(QuitWaitSeconds, processAlways: true, processInPhysics: false, ignoreTimeScale: true), SceneTreeTimer.SignalName.Timeout);
        GetTree().Quit();
    }
}
