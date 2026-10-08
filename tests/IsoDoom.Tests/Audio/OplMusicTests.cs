using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using IsoDoom.Audio;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Audio;

/// <summary>
/// A run of <c>tools/OplRef/music_ref</c> (Chocolate Doom's OPL music on a
/// stub SDL), read from the first line of its log (<c># music_ref OPTIONS</c>),
/// replayed through <see cref="OplMusic"/> the same way: the same calls
/// between the same blocks of samples.
/// </summary>
public sealed class MusicRefRun
{
    public int Rate = 44100;
    public int Block = 512;
    public int Length;
    public bool Opl3 = true;
    public bool Reverse;
    public bool NoChip;
    public int Volume = 64;
    public opl_driver_ver_t Version = opl_driver_ver_t.opl_doom_1_9;
    public readonly List<(int sample, string cmd)> Events = [];

    public static MusicRefRun Parse(string headerLine)
    {
        string[] a = headerLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (a.Length < 2 || a[0] != "#" || a[1] != "music_ref")
            throw new FormatException($"not a music_ref log: {headerLine}");
        var run = new MusicRefRun();
        for (int i = 2; i < a.Length; i++)
        {
            switch (a[i])
            {
                case "-rate": run.Rate = int.Parse(a[++i], CultureInfo.InvariantCulture); break;
                case "-block": run.Block = int.Parse(a[++i], CultureInfo.InvariantCulture); break;
                case "-length": run.Length = int.Parse(a[++i], CultureInfo.InvariantCulture); break;
                case "-opl2": run.Opl3 = false; break;
                case "-reverse": run.Reverse = true; break;
                case "-nochip": run.NoChip = true; break;
                case "-volume": run.Volume = int.Parse(a[++i], CultureInfo.InvariantCulture); break;
                case "-ver":
                    run.Version = a[++i] switch
                    {
                        "1.9" => opl_driver_ver_t.opl_doom_1_9,
                        "doom1-1.666" => opl_driver_ver_t.opl_doom1_1_666,
                        "doom2-1.666" => opl_driver_ver_t.opl_doom2_1_666,
                        _ => throw new FormatException(a[i]),
                    };
                    break;
                case "-at":
                    run.Events.Add((int.Parse(a[i + 1], CultureInfo.InvariantCulture), a[i + 2]));
                    i += 2;
                    break;
                default: throw new FormatException($"unknown option {a[i]}");
            }
        }
        if (run.Length == 0)
            run.Length = 10 * run.Rate;
        return run;
    }

    /// <summary>
    /// Plays the run on <paramref name="wad"/>'s GENMIDI and songs: the chip
    /// writes (sample, register, value) and the samples (none with
    /// <see cref="NoChip"/>).
    /// </summary>
    public (List<(long sample, ushort reg, byte val)> writes, short[] samples) Play(WadArchive wad)
    {
        var writes = new List<(long, ushort, byte)>();
        var music = new OplMusic(wad.W_CacheLumpName("GENMIDI").Span, Rate, Opl3, Reverse, Version, (s, r, v) => writes.Add((s, r, v)))
        {
            ChipOff = NoChip
        };
        music.I_OPL_SetMusicVolume(Volume);
        short[] samples = new short[NoChip ? 0 : 2 * Length];
        short[] scratch = new short[2 * Block];
        midi_file_t? handle = null;
        int done = 0, e = 0;
        for (; ; )
        {
            for (; e < Events.Count && Events[e].sample <= done; e++)
                Run(Events[e].cmd);
            if (done >= Length)
                break;
            int n = Block;
            if (e < Events.Count && Events[e].sample - done < n)
                n = Events[e].sample - done;
            n = Math.Min(n, Length - done);
            Span<short> buf = NoChip ? scratch.AsSpan(0, 2 * n) : samples.AsSpan(2 * done, 2 * n);
            music.OPL_Mix_Callback(buf);
            done += n;
        }
        Assert.Equal(Length, music.SamplesRendered);
        return (writes, samples);

        void Run(string cmd)
        {
            if (cmd.StartsWith("play:", StringComparison.Ordinal))
            {
                string[] f = cmd.Split(':');
                if (handle != null)
                {
                    music.I_OPL_StopSong();
                    music.I_OPL_UnRegisterSong(handle);
                    handle = null;
                }
                handle = music.I_OPL_RegisterSong(wad.W_CacheLumpName(f[1]).Span);
                if (handle != null)
                    music.I_OPL_PlaySong(handle, f.Length < 3 || f[2] != "once");
            }
            else if (cmd == "stop")
            {
                if (handle != null)
                {
                    music.I_OPL_StopSong();
                    music.I_OPL_UnRegisterSong(handle);
                    handle = null;
                }
            }
            else if (cmd == "pause")
                music.I_OPL_PauseSong();
            else if (cmd == "resume")
                music.I_OPL_ResumeSong();
            else if (cmd.StartsWith("volume:", StringComparison.Ordinal))
                music.I_OPL_SetMusicVolume(int.Parse(cmd[7..], CultureInfo.InvariantCulture));
            else
                throw new FormatException(cmd);
        }
    }

    /// <summary>A music_ref log: its run and its writes.</summary>
    public static (MusicRefRun run, List<(long sample, ushort reg, byte val)> writes) Load(string path)
    {
        string[] lines = File.ReadAllLines(path);
        MusicRefRun run = Parse(lines[0]);
        var writes = new List<(long, ushort, byte)>();
        foreach (string line in lines.Skip(1))
        {
            if (line.Length == 0 || !char.IsDigit(line[0]))
                continue;
            string[] f = line.Split(' ');
            writes.Add((long.Parse(f[0], CultureInfo.InvariantCulture),
                ushort.Parse(f[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(f[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
        }
        return (run, writes);
    }

    /// <summary>Asserts the two write sequences are the same, naming the first difference.</summary>
    public static void AssertSameWrites(string name, List<(long sample, ushort reg, byte val)> want, List<(long sample, ushort reg, byte val)> got)
    {
        int n = Math.Min(want.Count, got.Count);
        for (int i = 0; i < n; i++)
        {
            if (want[i] != got[i])
                Assert.Fail($"{name}: write {i} of {want.Count}: Chocolate Doom's {Fmt(want[i])}, OplMusic's {Fmt(got[i])}");
        }
        Assert.True(want.Count == got.Count, $"{name}: Chocolate Doom wrote {want.Count} times, OplMusic {got.Count}");

        static string Fmt((long sample, ushort reg, byte val) w) => $"{w.sample} {w.reg:x} {w.val:x}";
    }
}

/// <summary>
/// T7.8d: Chocolate Doom's OPL music (i_oplmusic.c, mus2mid.c, midifile.c,
/// opl_sdl.c, opl_queue.c) ported as <see cref="OplMusic"/>, against the C
/// itself (<c>tools/OplRef/music_ref</c>, SPEC §12 T7.8d): every chip write
/// at the same sample and the same samples, on the synthetic IWAD's songs
/// (committed logs, always) and on every song of DOOM1.WAD and doom2.wad
/// (<c>tools/OplRef/music.sh WAD</c>'s logs; skipped without them).
/// </summary>
public class OplMusicTests
{
    private readonly ITestOutputHelper _output;

    public OplMusicTests(ITestOutputHelper output) => _output = output;

    /// <summary>Where <c>music.sh WAD</c> writes: <c>$ISODOOM_OPL_MUSIC</c>, default <c>~/.cache/isodoom/opl-music</c>.</summary>
    public static string MusicRefDir =>
        Environment.GetEnvironmentVariable("ISODOOM_OPL_MUSIC") is { Length: > 0 } d
            ? d
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "isodoom", "opl-music");

    private static WadArchive Synthetic() => new([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);

    private static Dictionary<string, string> Sums() =>
        File.ReadAllLines(Path.Combine(OplLog.Dir, "SHA256SUMS"))
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(f => f.Length == 2)
            .ToDictionary(f => f[1], f => f[0]);

    public static TheoryData<string> SyntheticLogs()
    {
        var data = new TheoryData<string>();
        foreach (string name in OplLog.Names().Where(n => n.StartsWith("music-", StringComparison.Ordinal)))
            data.Add(name);
        return data;
    }

    [Fact]
    public void TheSyntheticRunsCoverTheDriver()
    {
        string[] names = [.. OplLog.Names().Where(n => n.StartsWith("music-", StringComparison.Ordinal))];
        Assert.Equal(new[] { "music-controls", "music-doom1-1666", "music-doom2-1666-opl2", "music-e1m1", "music-intro-opl2" }, names);
        MusicRefRun[] runs = [.. names.Select(n => MusicRefRun.Load(Path.Combine(OplLog.Dir, n + ".log")).run)];
        Assert.Contains(runs, r => !r.Opl3);
        Assert.Contains(runs, r => r.Reverse);
        Assert.Contains(runs, r => r.Version == opl_driver_ver_t.opl_doom1_1_666);
        Assert.Contains(runs, r => r.Version == opl_driver_ver_t.opl_doom2_1_666);
        Assert.Contains(runs, r => r.Events.Any(e => e.cmd.Contains("D_INTRO")));
        foreach (string cmd in new[] { "pause", "resume", "stop", "volume:40" })
            Assert.Contains(runs, r => r.Events.Any(e => e.cmd == cmd));
    }

    /// <summary>
    /// Each committed run of the synthetic IWAD (<c>tools/OplRef/music.sh</c>):
    /// OplMusic writes the chip as Chocolate Doom does, write for write at
    /// the same sample, and its samples hash as Chocolate Doom's
    /// (<c>logs/SHA256SUMS</c>; <c>check.sh</c> shows the log replays to them
    /// on both C chips, <see cref="Opl3Tests"/> on the C# one).
    /// </summary>
    [Theory]
    [MemberData(nameof(SyntheticLogs))]
    public void SyntheticSongsPlayAsChocolateDoom(string name)
    {
        (MusicRefRun run, List<(long, ushort, byte)> want) = MusicRefRun.Load(Path.Combine(OplLog.Dir, name + ".log"));
        (List<(long, ushort, byte)> got, short[] samples) = run.Play(Synthetic());
        MusicRefRun.AssertSameWrites(name, want, got);
        Assert.Contains(samples, s => s != 0);
        Assert.Equal(Sums()[name], OplLog.Sha256(samples));
        _output.WriteLine($"{name}: {want.Count} writes, {run.Length} samples, the same");
    }

    public static TheoryData<string, string> WadSongs()
    {
        var data = new TheoryData<string, string>();
        foreach (string game in new[] { "doom1", "doom2" })
        {
            string dir = Path.Combine(MusicRefDir, game);
            if (Directory.Exists(dir))
            {
                foreach (string f in Directory.GetFiles(dir, "D_*.log").OrderBy(f => f, StringComparer.Ordinal))
                    data.Add(game, Path.GetFileNameWithoutExtension(f));
            }
        }
        if (data.Count == 0)
            data.Add("doom1", "D_E1M1");
        return data;
    }

    private static WadArchive? GameWad(string game) =>
        (game == "doom1" ? TestWads.Doom1Path : TestWads.Doom2Path) is { } path ? WadArchive.Open(path) : null;

    /// <summary>
    /// Every song of DOOM1.WAD and doom2.wad against Chocolate Doom
    /// (<c>tools/OplRef/music.sh WAD</c>): the whole song looped once more
    /// (the chip not clocked: the writes only, at the same samples), and for
    /// a few songs a minute with the samples (<c>NAME.48k</c>). Skipped
    /// without the WAD or the logs.
    /// </summary>
    [Theory]
    [MemberData(nameof(WadSongs))]
    public void WadSongsPlayAsChocolateDoom(string game, string name)
    {
        string path = Path.Combine(MusicRefDir, game, name + ".log");
        Assert.SkipWhen(!File.Exists(path), $"no {path} (tools/OplRef/music.sh WAD)");
        WadArchive? wad = GameWad(game);
        Assert.SkipWhen(wad == null, $"no {game} WAD");
        (MusicRefRun run, List<(long, ushort, byte)> want) = MusicRefRun.Load(path);
        (List<(long, ushort, byte)> got, short[] samples) = run.Play(wad!);
        MusicRefRun.AssertSameWrites($"{game} {name}", want, got);
        string sha = Path.ChangeExtension(path, ".sha256");
        if (!run.NoChip && File.Exists(sha))
            Assert.Equal(File.ReadAllText(sha).Trim(), OplLog.Sha256(samples));
        _output.WriteLine($"{game} {name}: {want.Count} writes over {run.Length / (double)run.Rate:0.#} s, the same{(run.NoChip ? "" : ", samples the same")}");
    }

    // ---- without the reference ----

    private static OplMusic New(int rate = 48000, bool opl3 = true) =>
        new(Synthetic().W_CacheLumpName("GENMIDI").Span, rate, opl3);

    private static byte[] Song(string name = "D_E1M1") => Synthetic().W_CacheLumpName(name).ToArray();

    private static short[] Render(OplMusic m, int samples, int block = 512)
    {
        short[] buf = new short[2 * samples];
        for (int done = 0; done < samples; done += block)
            m.OPL_Mix_Callback(buf.AsSpan(2 * done, 2 * Math.Min(block, samples - done)));
        return buf;
    }

    private static int Peak(ReadOnlySpan<short> s)
    {
        int peak = 0;
        foreach (short x in s)
            peak = Math.Max(peak, Math.Abs((int)x));
        return peak;
    }

    [Fact]
    public void InitWritesTheRegistersAtSampleZero()
    {
        var writes = new List<(long, ushort, byte)>();
        byte[] gen = Synthetic().W_CacheLumpName("GENMIDI").ToArray();
        var opl2 = new OplMusic(gen, 44100, opl3: false);
        Assert.False(opl2.Opl3Mode);
        var m = new OplMusic(gen, 44100) { RegisterWritten = null };
        Assert.True(m.Opl3Mode);
        Assert.Equal(opl_driver_ver_t.opl_doom_1_9, m.DriverVersion);
        Assert.Equal(0, m.MusicVolume);
        Assert.False(m.I_OPL_MusicIsPlaying());
        Assert.Equal(0, m.VoicesInUse);
        // The init's writes are the music_ref logs' first (OPL3 mode: both
        // banks and OPL_REG_NEW; the timer registers kept off the chip).
        (_, List<(long sample, ushort reg, byte val)> log) = MusicRefRun.Load(Path.Combine(OplLog.Dir, "music-e1m1.log"));
        Assert.Contains(log, w => w is { sample: 0, reg: 0x105, val: 1 });
        Assert.DoesNotContain(log, w => w.reg is 2 or 3 or 4);
        // Silence until a song plays.
        Assert.Equal(0, Peak(Render(m, 4800)));
    }

    [Fact]
    public void ASongPlaysOnlyWithAVolume()
    {
        OplMusic m = New();
        midi_file_t? song = m.I_OPL_RegisterSong(Song());
        Assert.NotNull(song);
        m.I_OPL_PlaySong(song, looping: true);
        Assert.True(m.I_OPL_MusicIsPlaying());
        Assert.InRange(Peak(Render(m, 48000)), 0, 300); // current_music_volume starts at 0, as the C's: the carriers at their least (0x3f, -47 dB)
        m.I_OPL_SetMusicVolume(127);
        int loud = Peak(Render(m, 48000));
        Assert.True(loud > 1000, $"peak {loud}");
        m.I_OPL_SetMusicVolume(0);
        Render(m, 24000);
        int quiet = Peak(Render(m, 48000));
        Assert.True(quiet < loud / 4, $"peak {quiet} at volume 0, {loud} at 127"); // the carriers at their least
    }

    [Fact]
    public void PauseHoldsTheSongAndPlayUnpauses()
    {
        OplMusic m = New();
        var writes = new List<long>();
        m.RegisterWritten = (s, _, _) => writes.Add(s);
        m.I_OPL_SetMusicVolume(100);
        midi_file_t song = m.I_OPL_RegisterSong(Song())!;
        m.I_OPL_PlaySong(song, true);
        Render(m, 48000);
        m.I_OPL_PauseSong();
        Assert.True(m.Paused);
        long pausedAt = m.SamplesRendered;
        writes.Clear();
        Render(m, 96000);
        Assert.Empty(writes); // no event while paused (the key offs came with the pause)
        m.I_OPL_ResumeSong();
        Assert.False(m.Paused);
        Render(m, 48000);
        Assert.NotEmpty(writes);
        Assert.True(writes.Min() > pausedAt);
        // I_PlaySong starts unpaused (DMX's, which s_sound.c relies on).
        m.I_OPL_PauseSong();
        m.I_OPL_StopSong();
        m.I_OPL_PlaySong(song, true);
        Assert.False(m.Paused);
    }

    [Fact]
    public void StopReleasesEveryVoice()
    {
        OplMusic m = New();
        m.I_OPL_SetMusicVolume(127);
        midi_file_t song = m.I_OPL_RegisterSong(Song())!;
        m.I_OPL_PlaySong(song, true);
        for (int i = 0; i < 300 && m.VoicesInUse == 0; i++)
            Render(m, 480);
        Assert.True(m.VoicesInUse > 0);
        m.I_OPL_StopSong();
        Assert.Equal(0, m.VoicesInUse);
        Assert.Equal(0, m.CallbacksQueued);
        Assert.False(m.I_OPL_MusicIsPlaying());
        Render(m, 3 * 48000);
        Assert.True(Peak(Render(m, 4800)) < 50, "still sounding after the stop");
    }

    [Fact]
    public void ASongPlayedOnceEndsAndALoopedOneGoesOn()
    {
        // The synthetic D_E1M1 lasts 875 tics at 140 Hz (6.25 s).
        foreach (bool looping in new[] { false, true })
        {
            OplMusic m = New();
            m.I_OPL_SetMusicVolume(127);
            m.I_OPL_PlaySong(m.I_OPL_RegisterSong(Song()), looping);
            var writes = new List<long>();
            Render(m, 7 * 48000);
            m.RegisterWritten = (s, _, _) => writes.Add(s);
            Render(m, 2 * 48000);
            if (looping)
                Assert.NotEmpty(writes);
            else
                Assert.Empty(writes);
            Assert.Equal(looping ? 1 : 0, m.CallbacksQueued); // the track's next event; none when over
        }
    }

    [Fact]
    public void RefusesWhatChocolateDoomRefuses()
    {
        OplMusic m = New();
        Assert.Null(m.I_OPL_RegisterSong([]));
        Assert.Null(m.I_OPL_RegisterSong([1, 2, 3]));
        Assert.Null(m.I_OPL_RegisterSong("MThd\0\0\0\u0006\0\u0002\0\u0001\0`"u8)); // MIDI type 2
        // A MUS song mus2mid.c refuses (a measure end, type 5) and one cut off.
        byte[] song = Song();
        Assert.NotNull(m.I_OPL_RegisterSong(song));
        int start = song[6] | song[7] << 8;
        byte[] measureEnd = (byte[])song.Clone();
        measureEnd[start] = 0x50;
        Assert.Null(m.I_OPL_RegisterSong(measureEnd));
        Assert.Null(m.I_OPL_RegisterSong(song.AsSpan(0, song.Length - 1)));
        // The MUS header is not checked (CHECK_MUS_HEADER is off).
        byte[] noHeader = (byte[])song.Clone();
        noHeader[0] = (byte)'X';
        Assert.NotNull(m.I_OPL_RegisterSong(noHeader));
        // Every truncation of the songs: refused or read, never an exception.
        foreach (string name in new[] { "D_E1M1", "D_INTRO" })
        {
            byte[] s = Song(name);
            for (int n = 0; n < s.Length; n++)
                m.I_OPL_RegisterSong(s.AsSpan(0, n));
        }
        // Garbage: never an exception, also when played.
        var rng = new Random(7);
        for (int i = 0; i < 200; i++)
        {
            byte[] junk = new byte[rng.Next(1, 400)];
            rng.NextBytes(junk);
            if (i % 2 == 0)
            {
                ReadOnlySpan<byte> head = "MThd\0\0\0\u0006\0\u0001\0\u0001\0`MTrk\0\0\0\0"u8;
                head[..Math.Min(head.Length, junk.Length)].CopyTo(junk);
            }
            midi_file_t? f = m.I_OPL_RegisterSong(junk);
            if (f != null)
            {
                m.I_OPL_SetMusicVolume(127);
                m.I_OPL_PlaySong(f, true);
                Render(m, 4800);
                m.I_OPL_StopSong();
            }
        }
    }

    [Fact]
    public void Mus2MidKeepsItsVelocitiesFromSongToSong()
    {
        // mus2mid.c's channelvelocities are static: a note without a volume
        // takes the last one its channel had, also in the last song.
        var conv = new Mus2Mid();
        byte[] song = Song();
        byte[] first = conv.mus2mid(song)!;
        Assert.Equal(first, conv.mus2mid(song)); // the synthetic song sets each channel's volume before its first note
        byte[] fresh = new Mus2Mid().mus2mid(song)!;
        Assert.Equal(first, fresh);
        // A song whose first note has no volume: 127 at first, then what the last song left.
        byte[] bare = [(byte)'M', (byte)'U', (byte)'S', 0x1a, 6, 0, 16, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0x10, 60, 0x60];
        var conv2 = new Mus2Mid();
        Assert.Equal(127, Velocity(conv2.mus2mid(bare)!));
        byte[] loud = [(byte)'M', (byte)'U', (byte)'S', 0x1a, 7, 0, 16, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0x10, 0x80 | 60, 33, 0x60];
        Assert.Equal(33, Velocity(conv2.mus2mid(loud)!));
        Assert.Equal(33, Velocity(conv2.mus2mid(bare)!));

        // MIDI channel 0's all-notes-off (D_DDTBLU's fix), then the note on.
        static int Velocity(byte[] mid)
        {
            midi_file_t f = midi_file_t.MIDI_LoadFile(mid)!;
            Assert.Equal(70u, f.MIDI_GetFileTimeDivision());
            midi_event_t on = f.tracks[0].events.First(e => e.event_type == midi_event_type_t.MIDI_EVENT_NOTE_ON);
            return (int)on.param2;
        }
    }

    [Fact]
    public void FloatToUInt64IsGccs()
    {
        // MetaSetTempo's OPL_Queue_AdjustCallbacks: (uint64_t) (offset / factor),
        // as x86-64 GCC computes it (tools/OplRef's harness agrees on the logs).
        Assert.Equal(18446744073709551613UL, OplMusic.FloatToUInt64(-3f));
        Assert.Equal(5UL, OplMusic.FloatToUInt64(5f));
        Assert.Equal(18446744073708551616UL, OplMusic.FloatToUInt64(-1000000f));
        Assert.Equal(9223372036854775808UL, OplMusic.FloatToUInt64(9223372036854775807f));
        Assert.Equal(9223372036854775808UL, OplMusic.FloatToUInt64(float.NaN));
        Assert.Equal(0UL, OplMusic.FloatToUInt64(float.PositiveInfinity));
        Assert.Equal(9223372036854775808UL, OplMusic.FloatToUInt64(float.NegativeInfinity));
        Assert.Equal(2000000UL, OplMusic.FloatToUInt64(-1000000f / -0.5f));
    }

    [Fact]
    public void BlockSizesMoveTheEventsByASampleAtMost()
    {
        // opl_sdl.c's clock adds whole microseconds per run of samples, so
        // the blocks are part of the timing, as in Chocolate Doom: the same
        // writes, a sample apart at most over a whole song.
        List<(long s, ushort r, byte v)> Writes(int block)
        {
            OplMusic m = New();
            m.ChipOff = true;
            var w = new List<(long, ushort, byte)>();
            m.RegisterWritten = (s, r, v) => w.Add((s, r, v));
            m.I_OPL_SetMusicVolume(100);
            m.I_OPL_PlaySong(m.I_OPL_RegisterSong(Song()), true);
            Render(m, 8 * 48000, block);
            return w;
        }
        List<(long s, ushort r, byte v)> a = Writes(512), b = Writes(441);
        Assert.Equal(a.Select(x => (x.r, x.v)), b.Select(x => (x.r, x.v)));
        Assert.All(a.Zip(b), p => Assert.InRange(p.First.s - p.Second.s, -1, 1));
    }

    [Fact]
    public void PlaysThroughSSound()
    {
        // OplMusic is an IMusicDevice: s_sound.c's music half drives it.
        OplMusic m = New();
        IMusicDevice device = m;
        object? h = device.I_RegisterSong("D_E1M1", Song());
        Assert.IsType<midi_file_t>(h);
        device.I_SetMusicVolume(64);
        device.I_PlaySong(h!, true);
        Assert.True(Peak(Render(m, 48000)) > 500);
        device.I_PauseSong();
        Assert.True(m.Paused);
        device.I_ResumeSong();
        device.I_StopSong();
        device.I_UnRegisterSong(h!);
        Assert.False(m.I_OPL_MusicIsPlaying());
        Assert.Contains("OPL3 48000 Hz, stopped", m.ToString());
    }

    /// <summary>
    /// The cost of a minute of DOOM1's D_E1M1 (or the synthetic song) at
    /// 48 kHz, the sequencer and the chip (Release: <c>dotnet test -c
    /// Release --filter-method *OplMusicTests.OneMinuteOfMusic</c>; printed).
    /// </summary>
    [Fact]
    public void OneMinuteOfMusic()
    {
        WadArchive wad = TestWads.Doom1Path is { } p ? WadArchive.Open(p) : Synthetic();
        var m = new OplMusic(wad.W_CacheLumpName("GENMIDI").Span, 48000);
        m.I_OPL_SetMusicVolume(100);
        m.I_OPL_PlaySong(m.I_OPL_RegisterSong(wad.W_CacheLumpName("D_E1M1").Span), true);
        short[] buf = new short[2 * 1024];
        var sw = Stopwatch.StartNew();
        for (int done = 0; done < 60 * 48000; done += 1024)
            m.OPL_Mix_Callback(buf);
        sw.Stop();
        _output.WriteLine($"a minute of {(TestWads.Doom1Path != null ? "DOOM1" : "synthetic")} D_E1M1 in {sw.Elapsed.TotalSeconds:0.00} s: {sw.Elapsed.TotalSeconds / 60 * 100:0.0}% of a core");
    }
}
