using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Audio;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Audio;

/// <summary>A sound driver that records what <see cref="SSound"/> asks of it (T7.7).</summary>
internal sealed class RecordingDevice : ISoundDevice
{
    public readonly List<string> Calls = new();
    public readonly Dictionary<int, (sfxenum_t Sfx, int Vol, int Sep)> Playing = new();
    public Func<sfxenum_t, double> Seconds = _ => 1.0;
    public bool Paused;

    public double I_StartSound(sfxenum_t sfx, int cnum, int vol, int sep)
    {
        Calls.Add($"start {SSound.SfxName(sfx)} ch{cnum} {vol} {sep}");
        double s = Seconds(sfx);
        if (s > 0)
            Playing[cnum] = (sfx, vol, sep);
        return s;
    }

    public void I_StopSound(int cnum)
    {
        Calls.Add($"stop ch{cnum}");
        Playing.Remove(cnum);
    }

    public void I_UpdateSoundParams(int cnum, int vol, int sep)
    {
        Calls.Add($"update ch{cnum} {vol} {sep}");
        Playing[cnum] = (Playing[cnum].Sfx, vol, sep);
    }

    public void I_PauseSounds(bool paused) => Paused = paused;
}

/// <summary>
/// T7.7: the DMX decoding, the sound table and s_sound.c's channels,
/// attenuation, separation and priorities (<see cref="SSound"/>), with a
/// recording driver.
/// </summary>
public class SSoundTests
{
    private const int FRACUNIT = 1 << 16;

    private static int F(int units) => units * FRACUNIT;

    private static uint Deg(int degrees) => (uint)((ulong)(uint)degrees * 0x100000000UL / 360);

    private static byte[] Dmx(int rate, int samples, int? count = null, int format = 3)
    {
        byte[] lump = new byte[8 + 32 + samples];
        BinaryPrimitives.WriteUInt16LittleEndian(lump, (ushort)format);
        BinaryPrimitives.WriteUInt16LittleEndian(lump.AsSpan(2), (ushort)rate);
        BinaryPrimitives.WriteUInt32LittleEndian(lump.AsSpan(4), (uint)(count ?? samples + 32));
        for (int i = 0; i < 16; i++)
            lump[8 + i] = lump[8 + 16 + samples + i] = 128;
        for (int i = 0; i < samples; i++)
            lump[8 + 16 + i] = (byte)(i * 7);
        return lump;
    }

    [Fact]
    public void DecodesADmxSoundWithoutItsPads()
    {
        DmxSound s = DmxSound.Decode(Dmx(22050, 100));
        Assert.Equal(22050, s.SampleRate);
        Assert.Equal(100, s.Samples.Length);
        Assert.Equal(Enumerable.Range(0, 100).Select(i => (byte)(i * 7)), s.Samples);
        Assert.Equal(100 / 22050.0, s.Seconds, 9);
        // Godot's 8-bit samples are signed: 128 (silence) is 0.
        Assert.Equal((byte)0x80, s.ToSigned8()[0]);
        Assert.Equal((byte)(7 ^ 0x80), s.ToSigned8()[1]);
    }

    [Theory]
    [InlineData(17, null, 3)]  // 49 with the pads: the shortest DMX plays
    [InlineData(16, null, 3, false)] // 48: too short (i_sdlsound.c CacheSFX)
    [InlineData(100, 200, 3, false)] // the count runs beyond the lump
    [InlineData(100, null, 0, false)] // a PC speaker sound's format
    public void RejectsWhatDmxWouldNotPlay(int samples, int? count, int format, bool plays = true)
    {
        DmxSound? s = DmxSound.TryDecode(Dmx(11025, samples, count, format), out string? error);
        Assert.Equal(plays, s is not null);
        Assert.Equal(plays, error is null);
        if (!plays)
            Assert.Throws<WadFormatException>(() => DmxSound.Decode(Dmx(11025, samples, count, format)));
    }

    [Fact]
    public void TheSoundTableIsSoundsC()
    {
        Assert.Equal((int)sfxenum_t.NUMSFX, SoundInfo.S_sfx.Length);
        for (int i = 1; i < SoundInfo.S_sfx.Length; i++)
            Assert.Equal(((sfxenum_t)i).ToString(), "sfx_" + SoundInfo.S_sfx[i].name);
        Assert.Equal(64, SoundInfo.S_sfx[(int)sfxenum_t.sfx_pistol].priority);
        Assert.Equal(118, SoundInfo.S_sfx[(int)sfxenum_t.sfx_sawidl].priority);
        Assert.Equal(32, SoundInfo.S_sfx[(int)sfxenum_t.sfx_telept].priority);
        Assert.Equal(sfxenum_t.sfx_pistol, SoundInfo.S_sfx[(int)sfxenum_t.sfx_chgun].link);
        Assert.Equal("DSPISTOL", SoundInfo.LumpName(sfxenum_t.sfx_chgun)); // a link plays its link's lump
        Assert.Equal("DSPOSIT1", SoundInfo.LumpName(sfxenum_t.sfx_posit1));
        Assert.Single(SoundInfo.S_sfx, s => s.link is not null);
    }

    [Fact]
    public void EverySharewareSoundHasALumpDmxPlays()
    {
        WadArchive wad = WadArchive.Open(TestWads.RequireDoom1());
        int found = 0;
        for (int i = 1; i < (int)sfxenum_t.NUMSFX; i++)
        {
            int lump = wad.W_CheckNumForName(SoundInfo.LumpName((sfxenum_t)i));
            if (lump < 0)
                continue; // Doom II's
            Assert.NotNull(DmxSound.TryDecode(wad.W_CacheLumpNum(lump).Span, out string? error));
            Assert.Null(error);
            found++;
        }
        Assert.Equal(56, found); // 55 DS lumps, sfx_chgun plays DSPISTOL; the rest are Doom II's
    }

    private static (SSound Sound, RecordingDevice Device, mobj_t Player) Setup(uint angle = 0)
    {
        var device = new RecordingDevice();
        var sound = new SSound(device) { snd_SfxVolume = 127, gamemap = 1 };
        var player = new mobj_t { x = 0, y = 0, angle = angle };
        return (sound, device, player);
    }

    private static sound_listener_t Listener(mobj_t player, uint? angle = null) => new(player, player.x, player.y, angle ?? player.angle);

    private static sound_start_t Start(SSound s, mobj_t player, object? origin, sfxenum_t sfx, uint? angle = null)
    {
        (int x, int y) = origin is null ? (0, 0) : SSound.OriginPosition(origin);
        return s.S_StartSound(origin, x, y, sfx, Listener(player, angle));
    }

    [Fact]
    public void ASoundWithNoOriginOrFromThePlayerPlaysAtFullVolumeInTheCentre()
    {
        (SSound s, RecordingDevice d, mobj_t player) = Setup();
        player.x = F(5000);
        Assert.Equal((sound_result_t.sr_played, 127, 128), Pick(Start(s, player, null, sfxenum_t.sfx_swtchn)));
        Assert.Equal((sound_result_t.sr_played, 127, 128), Pick(Start(s, player, player, sfxenum_t.sfx_pistol)));
        Assert.Equal(new[] { "start swtchn ch0 127 128", "start pistol ch1 127 128" }, d.Calls);
    }

    private static (sound_result_t, int, int) Pick(sound_start_t st) => (st.result, st.vol, st.sep);

    [Theory]
    [InlineData(100, 127)]
    [InlineData(199, 127)]
    [InlineData(700, 63)] // 127 × (1200 − 700) / 1000
    [InlineData(1100, 12)]
    [InlineData(1199, 0)] // 127 × 1 / 1000 rounds to 0: inaudible
    [InlineData(1201, 0)]
    public void TheVolumeFallsWithDistance(int units, int vol)
    {
        (SSound s, RecordingDevice d, mobj_t player) = Setup();
        var imp = new mobj_t { x = F(units), y = 0 };
        sound_start_t st = Start(s, player, imp, sfxenum_t.sfx_bgsit1);
        Assert.Equal(vol > 0 ? sound_result_t.sr_played : sound_result_t.sr_inaudible, st.result);
        if (vol > 0)
            Assert.Equal(vol, st.vol);
        else
            Assert.Empty(d.Calls); // no channel taken
    }

    [Fact]
    public void OnMap8SoundsCarryOverTheWholeMap()
    {
        (SSound s, _, mobj_t player) = Setup();
        s.gamemap = 8;
        // straight ahead is 129, not 128: the angle wraps to 0xffffffff, whose finesine is just below 0 (as vanilla's)
        Assert.Equal((sound_result_t.sr_played, 15, 129), Pick(Start(s, player, new mobj_t { x = 0, y = F(-5000) }, sfxenum_t.sfx_brssit, Deg(270))));
        Assert.Equal(15 + 112 * 500 / 1000, Start(s, player, new mobj_t { x = F(700) }, sfxenum_t.sfx_bgsit1).vol);
    }

    [Theory]
    // Facing east (0): a sound to the north is on the left, south on the right, ahead and behind in the centre.
    [InlineData(0, 0, 500, 32)]
    [InlineData(0, 0, -500, 224)]
    [InlineData(0, 500, 0, 128)]
    [InlineData(0, -500, 0, 128)]
    // The game camera's screen-up is map north-west (135°): map west is up and to the left on screen,
    // map north up and to the right, map south-west straight left.
    [InlineData(135, -500, 0, 60)]
    [InlineData(135, 0, 500, 196)]
    [InlineData(135, -500, -500, 32)]
    [InlineData(135, 500, 500, 224)]
    public void TheSeparationIsTheSideOfTheListenersAngle(int angle, int x, int y, int sep)
    {
        (SSound s, _, mobj_t player) = Setup(Deg(angle));
        sound_start_t st = Start(s, player, new mobj_t { x = F(x), y = F(y) }, sfxenum_t.sfx_posact);
        Assert.Equal(sound_result_t.sr_played, st.result);
        Assert.InRange(st.sep, sep - 1, sep + 1);
        s.mono = true;
        Assert.Equal(128, Start(s, player, new mobj_t { x = F(x), y = F(y) }, sfxenum_t.sfx_posact).sep);
    }

    [Fact]
    public void ANewSoundFromAnOriginStopsItsLastOne()
    {
        (SSound s, RecordingDevice d, mobj_t player) = Setup();
        var imp = new mobj_t { x = F(300) };
        Assert.Equal(0, Start(s, player, imp, sfxenum_t.sfx_bgsit1).cnum);
        Assert.Equal(1, Start(s, player, new mobj_t { x = F(-300) }, sfxenum_t.sfx_posit1).cnum);
        Assert.Equal(0, Start(s, player, imp, sfxenum_t.sfx_claw).cnum);
        Assert.Equal(new[] { "start bgsit1 ch0 114 129", "start posit1 ch1 114 128", "stop ch0", "start claw ch0 114 129" }, d.Calls);
        Assert.Equal(2, s.channels.Count(c => c.sfxinfo is not null));
    }

    [Fact]
    public void ASoundWithNoOriginStopsTheFirstOtherWithNone()
    {
        // s_sound.c S_StopSound(NULL) matches the first channel with no origin (vanilla's menus: one at a time).
        (SSound s, RecordingDevice d, mobj_t player) = Setup();
        Start(s, player, null, sfxenum_t.sfx_pstop);
        Start(s, player, null, sfxenum_t.sfx_pistol);
        Assert.Equal(new[] { "start pstop ch0 127 128", "stop ch0", "start pistol ch0 127 128" }, d.Calls);
    }

    [Fact]
    public void WithEveryChannelBusyOnlyABetterOrEqualPriorityTakesOne()
    {
        (SSound s, RecordingDevice d, mobj_t player) = Setup();
        for (int i = 0; i < SSound.DefaultChannels; i++)
            Assert.Equal(i, Start(s, player, new mobj_t { x = F(10 + i) }, sfxenum_t.sfx_pistol).cnum); // priority 64
        // posit1 (98) is worse: no channel.
        Assert.Equal(sound_result_t.sr_nochannel, Start(s, player, new mobj_t { x = F(50) }, sfxenum_t.sfx_posit1).result);
        // the same priority (shotgn, 64) and a better one (telept, 32) take the first channel whose priority is no better.
        Assert.Equal(0, Start(s, player, new mobj_t { x = F(60) }, sfxenum_t.sfx_shotgn).cnum);
        Assert.Equal(0, Start(s, player, new mobj_t { x = F(70) }, sfxenum_t.sfx_telept).cnum);
        // now channel 0 holds telept (32): rxplod (70) is worse than every channel
        Assert.Equal(sound_result_t.sr_nochannel, Start(s, player, new mobj_t { x = F(80) }, sfxenum_t.sfx_rxplod).result);
        Assert.Equal(new[] { 0, 0 }, new[] { s.Results[(int)sound_result_t.sr_inaudible], s.Results[(int)sound_result_t.sr_nolump] });
        Assert.Equal(2, s.Results[(int)sound_result_t.sr_nochannel]);
        Assert.Contains("stop ch0", d.Calls);
    }

    [Fact]
    public void AChannelFollowsItsOriginAndStopsOutOfHearing()
    {
        (SSound s, RecordingDevice d, mobj_t player) = Setup(Deg(90));
        var imp = new mobj_t { x = F(300) };
        Start(s, player, imp, sfxenum_t.sfx_bgact);
        Assert.True(s.channels[0].sep > 200); // east of a listener facing north: right
        imp.x = F(-700); // walks to the west
        s.Advance(0.1);
        s.S_UpdateSounds(Listener(player));
        Assert.Equal((sfxenum_t.sfx_bgact, 63), (d.Playing[0].Sfx, d.Playing[0].Vol));
        Assert.True(d.Playing[0].Sep < 60); // now on the left
        // the listener turns; a sound from the listener or with no origin does not move
        Start(s, player, player, sfxenum_t.sfx_oof);
        s.S_UpdateSounds(Listener(player, Deg(270)));
        Assert.True(d.Playing[0].Sep > 200);
        Assert.Equal((127, 128), (d.Playing[1].Vol, d.Playing[1].Sep));
        imp.x = F(-1300); // out of hearing: stopped
        s.S_UpdateSounds(Listener(player));
        Assert.Null(s.channels[0].sfxinfo);
        Assert.False(d.Playing.ContainsKey(0));
        // the removed mobj's stop (P_RemoveMobj): S_StopSound(origin)
        s.S_StopSound(player);
        Assert.Empty(d.Playing);
    }

    [Fact]
    public void AChannelIsFreedWhenItsSoundEndsAndHoldsWhilePaused()
    {
        (SSound s, RecordingDevice d, mobj_t player) = Setup();
        d.Seconds = sfx => sfx == sfxenum_t.sfx_radio ? 0 : 0.5; // no lump for radio
        Start(s, player, null, sfxenum_t.sfx_itemup);
        Assert.Equal(sound_result_t.sr_nolump, Start(s, player, player, sfxenum_t.sfx_radio).result);
        s.S_UpdateSounds(Listener(player));
        Assert.Null(s.channels[1].sfxinfo); // freed at the next update, as i_sdlsound.c's handle −1
        s.PauseAll(true);
        Assert.True(d.Paused);
        s.Advance(1);
        s.S_UpdateSounds(Listener(player));
        Assert.NotNull(s.channels[0].sfxinfo);
        s.PauseAll(false);
        s.Advance(0.3);
        s.S_UpdateSounds(Listener(player));
        Assert.NotNull(s.channels[0].sfxinfo);
        s.Advance(0.3);
        s.S_UpdateSounds(Listener(player));
        Assert.Null(s.channels[0].sfxinfo);
        Assert.Equal("stop ch0", d.Calls[^1]); // it ended by itself: the driver is told too (its clock may lag)
        // the game's pause holds the music only (s_sound.c S_PauseSound; none playing: nothing to pause, as vanilla's)
        s.S_PauseSound();
        Assert.False(s.mus_paused);
        Assert.False(s.AllPaused);
    }

    [Fact]
    public void ALevelStartStopsEverySound()
    {
        (SSound s, RecordingDevice d, mobj_t player) = Setup();
        Start(s, player, null, sfxenum_t.sfx_itemup);
        Start(s, player, player, sfxenum_t.sfx_pistol);
        s.S_Start(1, 1);
        Assert.All(s.channels, c => Assert.Null(c.sfxinfo));
        Assert.Empty(d.Playing);
    }

    [Fact]
    public void TheGainsAreChocolateDooms()
    {
        (double l, double r) = SSound.Gains(127, 128);
        Assert.Equal(126 / 255.0, l, 9);
        Assert.Equal(128 / 255.0, r, 9);
        (l, r) = SSound.Gains(127, 32);
        Assert.Equal(222 / 255.0, l, 9);
        Assert.Equal(32 / 255.0, r, 9);
        Assert.Equal((0.0, 0.0), SSound.Gains(0, 128));
    }
}
