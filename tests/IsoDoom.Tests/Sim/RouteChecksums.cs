using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T8.6: the committed expected checksums of every route (and of the IWADs'
/// demos), so a change to the sim fails <c>dotnet test</c> without vanilla's
/// dumps: the synthetic IWAD's and the test maps' routes always (CI), the id
/// IWADs' whenever their WAD is there (their vanilla dumps are WAD-derived
/// and never committed, but these are hashes of sim state only).
/// <para>
/// <see cref="FileName"/> (in <see cref="VanillaRoute.Dir"/>) is text:
/// <c>#</c> comments; <c>iwad NAME MD5</c> lines, the IWAD each id IWAD's
/// entries were recorded on (a route on another version of it skips: Doom II
/// v1.666's and v1.9's maps differ); then one line per route or demo,
/// <c>NAME TICS HASH…</c>: a hash after every <see cref="Interval"/>th tic and
/// after the last. Each tic's state, <see cref="World.Checksum"/> and the
/// tic's dump line (<see cref="VanillaRoute.Line"/>: the player, the
/// sectors, textures, inventory, sounds, the HUD's state without its pixels,
/// the exit), is chained into a 64-bit FNV-1a and the chain is written folded
/// to 32 bits, 8 hex digits: a difference on any tic changes every later
/// hash, and the first differing hash names the second of the route it
/// happened in. <see cref="UpdateEnvVar"/> rewrites the entries the run plays.
/// </para>
/// </summary>
public static class RouteChecksums
{
    /// <summary>The checksums file, in <see cref="VanillaRoute.Dir"/>.</summary>
    public const string FileName = "checksums.txt";

    /// <summary>Set to <c>1</c> to rewrite the entries of the routes the run plays instead of checking them.</summary>
    public const string UpdateEnvVar = "ISODOOM_UPDATE_CHECKSUMS";

    /// <summary>Tics between two hashes (a second of game time).</summary>
    public const int Interval = 35;

    private const ulong FnvOffset = 14695981039346656037;
    private const ulong FnvPrime = 1099511628211;

    private static readonly object _lock = new();
    private static readonly Dictionary<string, string> _md5s = [];

    /// <summary>The path of <see cref="FileName"/>.</summary>
    public static string FilePath => Path.Combine(VanillaRoute.Dir, FileName);

    /// <summary>Whether <see cref="UpdateEnvVar"/> is set.</summary>
    public static bool Updating => Environment.GetEnvironmentVariable(UpdateEnvVar) is "1" or "true";

    /// <summary>The parsed file: the IWADs' MD5s and each entry's tics and hashes.</summary>
    public sealed record Entries(SortedDictionary<string, string> Iwads, SortedDictionary<string, (int Tics, string[] Hashes)> Routes);

    /// <summary>Reads <see cref="FilePath"/> (empty when absent).</summary>
    public static Entries Read()
    {
        var iwads = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var routes = new SortedDictionary<string, (int, string[])>(StringComparer.Ordinal);
        if (!File.Exists(FilePath))
            return new(iwads, routes);
        foreach (string raw in File.ReadAllLines(FilePath))
        {
            string line = raw.Split('#')[0].Trim();
            if (line.Length == 0)
                continue;
            string[] f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f[0] == "iwad" && f.Length == 3)
                iwads[f[1]] = f[2];
            else if (f.Length >= 3 && int.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out int tics))
                routes[f[0]] = (tics, f[2..]);
            else
                throw new FormatException($"{FilePath}: bad line \"{raw}\"");
        }
        return new(iwads, routes);
    }

    private static void Write(Entries entries)
    {
        var sb = new StringBuilder();
        sb.Append("# T8.6: the expected checksums of every route of this directory and of the IWADs' demos\n");
        sb.Append("# (tests/IsoDoom.Tests/Sim/RouteChecksums.cs). Hashes of sim state only, no WAD content.\n");
        sb.Append(CultureInfo.InvariantCulture, $"# NAME TICS HASH...: a hash every {Interval} tics and after the last, chained over every tic.\n");
        sb.Append(CultureInfo.InvariantCulture, $"# Rewrite them on purpose: {UpdateEnvVar}=1 dotnet test --filter \"FullyQualifiedName~RouteChecksumTests\"\n");
        sb.Append("# (the id IWADs' entries only where their WADs are: the others are kept as they are).\n");
        foreach ((string iwad, string md5) in entries.Iwads)
            sb.Append(CultureInfo.InvariantCulture, $"iwad {iwad} {md5}\n");
        foreach ((string name, (int tics, string[] hashes)) in entries.Routes)
            sb.Append(CultureInfo.InvariantCulture, $"{name} {tics} {string.Join(' ', hashes)}\n");
        File.WriteAllText(FilePath, sb.ToString());
    }

    /// <summary>The MD5 of the route's id IWAD (null for the synthetic IWAD and the test maps); skips without the WAD.</summary>
    public static string? IwadMd5(VanillaRoute route)
    {
        if (!route.IdWad)
            return null;
        string path = route.RequireWad();
        lock (_md5s)
        {
            if (!_md5s.TryGetValue(path, out string? md5))
                _md5s[path] = md5 = TestWads.Md5Of(path);
            return md5;
        }
    }

    /// <summary>The route's hashes: the chain after every <see cref="Interval"/>th tic and after the last.</summary>
    public static string[] Compute(VanillaRoute route)
    {
        var hashes = new List<string>();
        ulong chain = FnvOffset;
        foreach ((int tic, World world, _, _, string line) in route.Play(graphics: null))
        {
            Add(ref chain, world.Checksum());
            foreach (byte b in Encoding.UTF8.GetBytes(line))
                Add(ref chain, b);
            if ((tic + 1) % Interval == 0 || tic == route.Cmds.Count - 1)
                hashes.Add(((uint)(chain ^ (chain >> 32))).ToString("x8", CultureInfo.InvariantCulture));
        }
        return [.. hashes];
    }

    private static void Add(ref ulong h, ulong v)
    {
        for (int b = 0; b < 64; b += 8)
            Add(ref h, (byte)(v >> b));
    }

    private static void Add(ref ulong h, byte b)
    {
        unchecked
        {
            h ^= b;
            h *= FnvPrime;
        }
    }

    /// <summary>
    /// Plays <paramref name="route"/> and fails unless its hashes are the
    /// committed ones, naming the first second that differs; with
    /// <see cref="UpdateEnvVar"/> set, writes them instead. Skips when the
    /// route's IWAD is absent or is not the version the entry was recorded on.
    /// </summary>
    public static void Check(VanillaRoute route)
    {
        string? md5 = IwadMd5(route);
        string[] actual = Compute(route);
        int tics = route.Cmds.Count;
        if (Updating)
        {
            lock (_lock)
            {
                Entries entries = Read();
                if (md5 is not null)
                    entries.Iwads[route.Iwad] = md5;
                entries.Routes[route.Name] = (tics, actual);
                Write(entries);
            }
            return;
        }
        Entries committed = Read();
        string regen = $"if the change to the sim is deliberate, rerun with {UpdateEnvVar}=1 and commit {FileName}";
        if (md5 is not null && committed.Iwads.TryGetValue(route.Iwad, out string? recorded) && recorded != md5)
            Assert.Skip($"{route.Name}: the checksums were recorded on the {route.Iwad} IWAD with MD5 {recorded}, this one's is {md5}.");
        if (!committed.Routes.TryGetValue(route.Name, out (int Tics, string[] Hashes) expected))
            Assert.Fail($"{route.Name}: no committed checksums in {FileName}: run the tests with {UpdateEnvVar}=1 and commit it.");
        if (md5 is not null && !committed.Iwads.ContainsKey(route.Iwad))
            Assert.Fail($"{route.Name}: {FileName} has no \"iwad {route.Iwad}\" line: {regen}.");
        Assert.True(expected.Tics == tics, $"{route.Name}: the route has {tics} tics, {FileName} {expected.Tics}: {regen}.");
        for (int i = 0; i < Math.Max(expected.Hashes.Length, actual.Length); i++)
        {
            string e = i < expected.Hashes.Length ? expected.Hashes[i] : "-", a = i < actual.Length ? actual[i] : "-";
            if (e == a)
                continue;
            int first = i * Interval + 1, last = Math.Min((i + 1) * Interval, tics);
            Assert.Fail($"{route.Name}: the sim's state first differs from the committed checksums in tics {first}-{last} "
                + $"(hash {i + 1} of {actual.Length}: expected {e}, got {a}); {regen}.");
        }
    }

    /// <summary>The demos whose checksums are committed, as <see cref="VanillaRoute.Demo(string, string, IsoDoom.Wad.GameVersion?)"/> names them.</summary>
    public static readonly string[] Demos =
        [.. new[] { "", "doom2-", "doom2-final-", "tnt-", "plutonia-" }.SelectMany(p => new[] { 1, 2, 3 }.Select(n => $"{p}demo{n}")),
            .. new[] { 1, 2, 3, 4 }.Select(n => $"doom-demo{n}")]; // (M9: The Ultimate Doom's four)

    /// <summary>The demo <paramref name="name"/> of <see cref="Demos"/> as a route (skips without its IWAD).</summary>
    public static VanillaRoute Demo(string name)
    {
        string lump = name[^5..].ToUpperInvariant();
        return name[..^5] switch
        {
            "" => VanillaRoute.Demo(lump),
            "doom2-" => VanillaRoute.Demo(lump, "doom2"),
            "doom2-final-" => VanillaRoute.Demo(lump, "doom2", IsoDoom.Wad.GameVersion.exe_final),
            "tnt-" => VanillaRoute.Demo(lump, "tnt"),
            "plutonia-" => VanillaRoute.Demo(lump, "plutonia"),
            "doom-" => VanillaRoute.Demo(lump, "doom"),
            _ => throw new ArgumentException($"no demo {name}", nameof(name)),
        };
    }
}
