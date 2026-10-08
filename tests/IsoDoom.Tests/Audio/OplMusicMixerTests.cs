using System;
using IsoDoom.Audio;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Audio;

/// <summary>
/// T7.8g: <see cref="OplMusicMixer"/>, the OPL2/OPL3 switch under the music
/// option: unswitched it is the driver, bit for bit; a switch plays the song
/// again from its start on the other chip (exactly a fresh driver's output
/// once the old one has faded), keeps the volume and the pause, leaves an
/// ended song ended, keys every old voice off, and does not click.
/// </summary>
public class OplMusicMixerTests
{
    private const int Rate = 48000, Block = 512;

    private static readonly WadArchive Wad = new([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);

    private static byte[] Bank => Wad.W_CacheLumpName("GENMIDI").ToArray();

    private static midi_file_t Song(OplMusic m) => m.I_OPL_RegisterSong(Wad.W_CacheLumpName("D_E1M1").Span)!;

    private static short[] Render(Action<Span<short>> mix, int blocks)
    {
        short[] all = new short[blocks * Block * 2];
        for (int b = 0; b < blocks; b++)
            mix(all.AsSpan(b * Block * 2, Block * 2));
        return all;
    }

    private static OplMusicMixer Playing(bool opl3, bool looping = true, int volume = 100)
    {
        var mixer = new OplMusicMixer(Bank, Rate, opl3);
        mixer.Driver.I_OPL_SetMusicVolume(volume);
        mixer.PlaySong(Song(mixer.Driver), looping);
        return mixer;
    }

    private static int MaxStep(ReadOnlySpan<short> s)
    {
        int max = 0;
        for (int i = 2; i < s.Length; i++)
            max = Math.Max(max, Math.Abs(s[i] - s[i - 2]));
        return max;
    }

    [Fact]
    public void UnswitchedItIsTheDriver()
    {
        OplMusicMixer mixer = Playing(opl3: true);
        var plain = new OplMusic(Bank, Rate);
        plain.I_OPL_SetMusicVolume(100);
        plain.I_OPL_PlaySong(Song(plain), true);
        Assert.Equal(Render(plain.OPL_Mix_Callback, 100), Render(mixer.Mix, 100));
        Assert.False(mixer.SwitchTo(mixer.CreateDriver(opl3: true))); // the same mode: nothing to do
        Assert.Equal(0, mixer.Switches);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ASwitchPlaysTheSongAgainOnTheOtherChip(bool fromOpl3)
    {
        OplMusicMixer mixer = Playing(fromOpl3, volume: 90);
        short[] before = Render(mixer.Mix, 200); // about 2 s in: notes sounding
        OplMusic old = mixer.Driver;
        Assert.True(old.VoicesInUse > 0);

        Assert.True(mixer.SwitchTo(mixer.CreateDriver(!fromOpl3)));
        Assert.Equal(!fromOpl3, mixer.Opl3);
        Assert.Equal(90, mixer.Driver.MusicVolume);
        Assert.True(mixer.Driver.I_OPL_MusicIsPlaying());
        Assert.Equal(0, old.VoicesInUse); // every old note keyed off: none stuck
        Assert.True(mixer.Crossfading);
        short[] after = Render(mixer.Mix, 300);
        Assert.False(mixer.Crossfading); // the old chip heard out and dropped

        // once the old chip has faded, exactly a fresh driver playing the song from its start
        var fresh = new OplMusic(Bank, Rate, !fromOpl3);
        fresh.I_OPL_SetMusicVolume(90);
        fresh.I_OPL_PlaySong(Song(fresh), true);
        short[] want = Render(fresh.OPL_Mix_Callback, 300);
        int faded = (int)Math.Ceiling(OplMusicMixer.CrossfadeSeconds * Rate / Block) * Block * 2;
        Assert.Equal(want.AsSpan(faded).ToArray(), after.AsSpan(faded).ToArray());

        // no click at the switch: the step across it no larger than the song's own steps
        short[] seam = new short[8];
        before.AsSpan(before.Length - 4).CopyTo(seam);
        after.AsSpan(0, 4).CopyTo(seam.AsSpan(4));
        Assert.True(MaxStep(seam) <= MaxStep(before), $"step {MaxStep(seam)} at the switch, {MaxStep(before)} in the song");
    }

    [Fact]
    public void APausedSongStaysPausedAndResumesFromItsStart()
    {
        OplMusicMixer mixer = Playing(opl3: true);
        Render(mixer.Mix, 100);
        mixer.Driver.I_OPL_PauseSong();
        Render(mixer.Mix, 100); // the percussion rings out
        Assert.True(mixer.SwitchTo(mixer.CreateDriver(opl3: false)));
        Assert.True(mixer.Driver.Paused);
        Assert.Equal(0, mixer.Driver.VoicesInUse);
        Render(mixer.Mix, 10);
        mixer.Driver.I_OPL_ResumeSong();
        Assert.False(mixer.Driver.Paused);
        Assert.True(mixer.Driver.I_OPL_MusicIsPlaying());
    }

    [Fact]
    public void AnEndedOrStoppedSongIsNotPlayedAgain()
    {
        // the synthetic D_E1M1 lasts 6.25 s: played once, it has ended by 7 s
        OplMusicMixer mixer = Playing(opl3: true, looping: false);
        Render(mixer.Mix, 7 * Rate / Block);
        Assert.Equal(0, mixer.Driver.RunningTracks);
        Assert.True(mixer.SwitchTo(mixer.CreateDriver(opl3: false)));
        Assert.False(mixer.Driver.I_OPL_MusicIsPlaying());

        OplMusicMixer stopped = Playing(opl3: true);
        Render(stopped.Mix, 50);
        stopped.StopSong();
        Assert.True(stopped.SwitchTo(stopped.CreateDriver(opl3: false)));
        Assert.False(stopped.Driver.I_OPL_MusicIsPlaying());
        Assert.True(stopped.SwitchTo(stopped.CreateDriver(opl3: true))); // and back, mid-crossfade
        Assert.Equal(2, stopped.Switches);
    }
}
