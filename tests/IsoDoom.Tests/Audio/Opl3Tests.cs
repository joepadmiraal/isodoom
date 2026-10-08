using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using IsoDoom.Audio;
using IsoDoom.Tests.Support;
using Xunit;

namespace IsoDoom.Tests.Audio;

/// <summary>
/// A register-write log of <c>tools/OplRef/logs</c> (format in
/// <c>tools/OplRef/genlogs.py</c>) and its playback through the C# chip, as
/// <c>tools/OplRef/opl_ref.c</c> plays it through the C.
/// </summary>
public sealed class OplLog
{
    public string Name { get; set; } = "";
    public uint Rate { get; set; }
    public int Length { get; set; }
    public bool Buffered { get; set; }
    public List<(int sample, ushort reg, byte val)> Writes { get; } = [];

    public static string Dir => Path.Combine(TestWads.RepoRoot ?? throw new InvalidOperationException("no repo root"), "tools", "OplRef", "logs");

    public static IEnumerable<string> Names() =>
        Directory.GetFiles(Dir, "*.log").Select(f => Path.GetFileNameWithoutExtension(f)).OrderBy(n => n, StringComparer.Ordinal);

    public static OplLog Load(string name)
    {
        var log = new OplLog { Name = name };
        foreach (string raw in File.ReadAllLines(Path.Combine(Dir, name + ".log")))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (f[0])
            {
                case "rate": log.Rate = uint.Parse(f[1], CultureInfo.InvariantCulture); break;
                case "length": log.Length = int.Parse(f[1], CultureInfo.InvariantCulture); break;
                case "buffered": log.Buffered = true; break;
                default:
                    log.Writes.Add((int.Parse(f[0], CultureInfo.InvariantCulture),
                        ushort.Parse(f[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        byte.Parse(f[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                    break;
            }
        }
        return log;
    }

    /// <summary>The stereo samples (left, right interleaved), on a new chip or <paramref name="chip"/> reset.</summary>
    public short[] Render(opl3_chip? chip = null)
    {
        chip ??= new opl3_chip();
        Opl3.OPL3_Reset(chip, Rate != 0 ? Rate : Opl3.OPL_RATE);
        short[] output = new short[2 * Length];
        int w = 0;
        for (int i = 0; i < Length; i++)
        {
            for (; w < Writes.Count && Writes[w].sample <= i; w++)
            {
                if (Buffered)
                    Opl3.OPL3_WriteRegBuffered(chip, Writes[w].reg, Writes[w].val);
                else
                    Opl3.OPL3_WriteReg(chip, Writes[w].reg, Writes[w].val);
            }
            Span<short> buf = output.AsSpan(2 * i, 2);
            if (Rate != 0)
                Opl3.OPL3_GenerateStream(chip, buf, 1);
            else
                Opl3.OPL3_Generate(chip, buf);
        }
        return output;
    }

    /// <summary>SHA-256 of the samples as opl_ref.c writes them (signed 16-bit little-endian).</summary>
    public static string Sha256(short[] samples)
    {
        byte[] bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            bytes[2 * i] = (byte)samples[i];
            bytes[2 * i + 1] = (byte)(samples[i] >> 8);
        }
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}

/// <summary>
/// T7.8a: the C# port of Nuked-OPL3 (<see cref="Opl3"/>) against the C
/// (SPEC §12 T7.8a): every committed log's samples hash as the C reference's
/// (<c>tools/OplRef/logs/SHA256SUMS</c>, written by <c>tools/OplRef/check.sh</c>),
/// and sample for sample against the harness itself when it is built
/// (<c>tools/OplRef/build.sh</c>; skipped without it).
/// </summary>
public class Opl3Tests
{
    private readonly ITestOutputHelper _output;

    public Opl3Tests(ITestOutputHelper output) => _output = output;

    /// <summary>The reference harness's directory: <c>$ISODOOM_OPL_REF</c>, default <c>~/.cache/isodoom/opl-ref</c>.</summary>
    public const string RefDirEnvVar = "ISODOOM_OPL_REF";

    private static Dictionary<string, string> Sums() =>
        File.ReadAllLines(Path.Combine(OplLog.Dir, "SHA256SUMS"))
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(f => f.Length == 2)
            .ToDictionary(f => f[1], f => f[0]);

    public static TheoryData<string> Logs()
    {
        var data = new TheoryData<string>();
        foreach (string name in OplLog.Names())
            data.Add(name);
        return data;
    }

    [Fact]
    public void EveryLogHasAReferenceHash()
    {
        Dictionary<string, string> sums = Sums();
        Assert.Equal(OplLog.Names().ToArray(), sums.Keys.OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.True(sums.Count >= 5);
    }

    [Theory]
    [MemberData(nameof(Logs))]
    public void SamplesHashAsTheCReference(string name)
    {
        var log = OplLog.Load(name);
        short[] samples = log.Render();
        Assert.Equal(log.Length * 2, samples.Length);
        Assert.Contains(samples, s => s != 0);
        Assert.Equal(Sums()[name], OplLog.Sha256(samples));
    }

    [Theory]
    [MemberData(nameof(Logs))]
    public void SamplesEqualTheCHarness(string name)
    {
        string? dir = Environment.GetEnvironmentVariable(RefDirEnvVar);
        if (string.IsNullOrEmpty(dir))
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "isodoom", "opl-ref");
        string exe = Path.Combine(dir, "opl_ref");
        if (!File.Exists(exe))
            Assert.Skip($"No OPL reference harness {exe}: run tools/OplRef/build.sh (or set {RefDirEnvVar}).");

        string raw = Path.Combine(Path.GetTempPath(), $"isodoom-opl-{name}-{Environment.ProcessId}.raw");
        try
        {
            using (Process p = Process.Start(new ProcessStartInfo(exe) { ArgumentList = { Path.Combine(OplLog.Dir, name + ".log"), raw } })!)
            {
                p.WaitForExit();
                Assert.Equal(0, p.ExitCode);
            }
            byte[] bytes = File.ReadAllBytes(raw);
            short[] mine = OplLog.Load(name).Render();
            Assert.Equal(bytes.Length / 2, mine.Length);
            for (int i = 0; i < mine.Length; i++)
            {
                short c = (short)(bytes[2 * i] | (bytes[2 * i + 1] << 8));
                if (c != mine[i])
                    Assert.Fail($"{name}: sample {i / 2} ({(i % 2 == 0 ? "left" : "right")}): C {c}, C# {mine[i]}");
            }
        }
        finally
        {
            File.Delete(raw);
        }
    }

    [Fact]
    public void ResetChipIsSilentAndResetsEverything()
    {
        var chip = new opl3_chip();
        Span<short> buf = stackalloc short[2];
        for (int i = 0; i < 1000; i++)
        {
            Opl3.OPL3_Generate(chip, buf);
            Assert.Equal(0, buf[0]);
            Assert.Equal(0, buf[1]);
        }
        // A chip reset after a log plays it as a new one does.
        var log = OplLog.Load("opl3-4op");
        string first = OplLog.Sha256(log.Render(chip));
        Assert.Equal(first, OplLog.Sha256(log.Render(chip)));
        Assert.Equal(first, OplLog.Sha256(log.Render()));
    }

    [Theory]
    [InlineData(48000u, 988)]
    [InlineData(44100u, 908)]
    [InlineData(49716u, 1024)]
    public void ResamplerRunsTheChipAtItsOwnRate(uint rate, int rateratio)
    {
        var chip = new opl3_chip();
        Opl3.OPL3_Reset(chip, rate);
        Assert.Equal(rateratio, chip.rateratio);
        short[] buf = new short[2 * (int)rate];
        Opl3.OPL3_GenerateStream(chip, buf, rate);
        // The chip runs rateratio's 1024ths of a sample per output sample: 49716 a second, but for its rounding.
        long chipSamples = (long)chip.writebuf_samplecnt;
        long expected = (long)rate * 1024 / rateratio;
        Assert.InRange(chipSamples, expected - 1, expected + 1);
        Assert.InRange(expected, 49716 - 50, 49716 + 60);
    }

    /// <summary>
    /// SPEC §12 T7.8a's timing: a minute of output at 48 kHz in OPL3 mode
    /// with all 18 channels sounding (vibrato and tremolo on). Nuked-OPL3
    /// processes every slot every sample whatever it plays, so this is its
    /// cost for any song. Run in Release for the logged figure
    /// (<c>dotnet test -c Release --filter Opl3Tests.OneMinuteOfOpl3Output</c>);
    /// here it only has to keep up with real time.
    /// </summary>
    [Fact]
    public void OneMinuteOfOpl3Output()
    {
        var chip = new opl3_chip();
        Opl3.OPL3_Reset(chip, 48000);
        Opl3.OPL3_WriteReg(chip, 0x105, 0x01);
        Opl3.OPL3_WriteReg(chip, 0x104, 0x09);
        Opl3.OPL3_WriteReg(chip, 0xBD, 0xC0);
        int[] mods = [0x00, 0x01, 0x02, 0x08, 0x09, 0x0A, 0x10, 0x11, 0x12];
        for (int ch = 0; ch < 18; ch++)
        {
            int bank = ch < 9 ? 0 : 0x100, c = ch % 9;
            foreach (int op in new[] { mods[c], mods[c] + 3 })
            {
                Opl3.OPL3_WriteReg(chip, (ushort)(bank + 0x20 + op), 0xE1);
                Opl3.OPL3_WriteReg(chip, (ushort)(bank + 0x40 + op), (byte)(op == mods[c] ? 20 : 4));
                Opl3.OPL3_WriteReg(chip, (ushort)(bank + 0x60 + op), 0xF2);
                Opl3.OPL3_WriteReg(chip, (ushort)(bank + 0x80 + op), 0x24);
                Opl3.OPL3_WriteReg(chip, (ushort)(bank + 0xE0 + op), (byte)(ch & 7));
            }
            Opl3.OPL3_WriteReg(chip, (ushort)(bank + 0xC0 + c), (byte)(0x30 | ((ch & 7) << 1)));
            Opl3.OPL3_WriteReg(chip, (ushort)(bank + 0xA0 + c), (byte)(0x40 + ch * 9));
            Opl3.OPL3_WriteReg(chip, (ushort)(bank + 0xB0 + c), (byte)(0x20 | (4 << 2) | 1));
        }
        const int Rate = 48000, Block = 512, Seconds = 60;
        short[] buf = new short[2 * Block];
        for (int done = 0; done < Rate; done += Block)
            Opl3.OPL3_GenerateStream(chip, buf, Block); // a second of warm-up: the JIT's optimized code, as a music thread runs
        long sum = 0;
        var clock = Stopwatch.StartNew();
        for (int done = 0; done < Rate * Seconds; done += Block)
        {
            Opl3.OPL3_GenerateStream(chip, buf, Block);
            sum += buf[0];
        }
        clock.Stop();
        double s = clock.Elapsed.TotalSeconds;
#if DEBUG
        const string Config = "Debug";
#else
        const string Config = "Release";
#endif
        _output.WriteLine($"{Config}: {Seconds} s of OPL3 output at {Rate} Hz in {s:F3} s = {100 * s / Seconds:F2}% of a core (checksum {sum})");
        Assert.True(s < Seconds, $"slower than real time: {s:F1} s for {Seconds} s");
    }
}
