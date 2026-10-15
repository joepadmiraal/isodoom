using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using Xunit;

namespace IsoDoom.Tests.Support;

/// <summary>
/// Locates the test WADs. WAD data never lives in the repo; tests that need a
/// WAD call <see cref="RequireDoom1"/>, which reports the test as skipped when
/// the file is absent.
/// </summary>
public static class TestWads
{
    /// <summary>Environment variable that overrides the DOOM1.WAD path (an absent path forces a skip).</summary>
    public const string Doom1EnvVar = "ISODOOM_DOOM1_WAD";

    /// <summary>MD5 of the shareware v1.9 DOOM1.WAD the tests are written against.</summary>
    public const string Doom1Md5 = "f0cefca49926d00903cf57551d901abe";

    private static readonly Lazy<string?> _doom1 = new(FindDoom1);
    private static readonly Lazy<string?> _doom1Problem = new(CheckDoom1);

    /// <summary>The repo root (the directory holding IsoDoom.sln), or null if not found.</summary>
    public static string? RepoRoot { get; } = FindRepoRoot();

    /// <summary>
    /// Full path of <c>wads/DOOM1.WAD</c> (or of <see cref="Doom1EnvVar"/> when set),
    /// or null when the file does not exist.
    /// </summary>
    public static string? Doom1Path => _doom1.Value;

    /// <summary>
    /// Returns the path of the shareware v1.9 DOOM1.WAD, or skips the calling
    /// test when it is absent or is a different WAD (wrong MD5).
    /// </summary>
    public static string RequireDoom1()
    {
        string? problem = _doom1Problem.Value;
        if (problem is not null)
            Assert.Skip(problem);
        return Doom1Path!;
    }

    /// <summary>Environment variable that overrides the DOOM2.WAD path (an absent path forces a skip).</summary>
    public const string Doom2EnvVar = "ISODOOM_DOOM2_WAD";

    private static readonly Lazy<string?> _doom2 = new(FindDoom2);

    /// <summary>
    /// Full path of a DOOM II IWAD: <see cref="Doom2EnvVar"/> when set, else
    /// <c>wads/DOOM2.WAD</c> or <c>wads/doom2.wad</c>; null when absent.
    /// </summary>
    public static string? Doom2Path => _doom2.Value;

    /// <summary>
    /// Returns the path of a DOOM II IWAD (any version; no MD5 check, so tests
    /// must only assert what every release has), or skips the calling test.
    /// </summary>
    public static string RequireDoom2()
    {
        if (Doom2Path is null)
        {
            string? overridePath = Environment.GetEnvironmentVariable(Doom2EnvVar);
            Assert.Skip(!string.IsNullOrEmpty(overridePath)
                ? $"DOOM2.WAD not found at {Path.GetFullPath(overridePath)} (from {Doom2EnvVar})."
                : $"DOOM2.WAD not found in wads/ (or set {Doom2EnvVar}); put a DOOM II IWAD there to run this test.");
        }
        return Doom2Path!;
    }

    /// <summary>MD5 of the DOOM II v1.666 IWAD; tests assert version-specific counts only for this file.</summary>
    public const string Doom2V1666Md5 = "30e3c2d0350b67bfbf47271970b74b2f";

    private static readonly Lazy<string?> _doom2Md5Value = new(() => Doom2Path is null ? null : Md5Of(Doom2Path));

    /// <summary>MD5 (lower-case hex) of <see cref="Doom2Path"/>, or null when there is no DOOM II IWAD.</summary>
    public static string? Doom2Md5 => _doom2Md5Value.Value;

    /// <summary>M9: environment variable that overrides the registered/Ultimate Doom IWAD's path (an absent path forces a skip).</summary>
    public const string DoomEnvVar = "ISODOOM_DOOM_WAD";

    private static readonly Lazy<string?> _doom = new(() => FindIwad(DoomEnvVar, "DOOM.WAD", "doom.wad"));

    /// <summary>M9: the registered or Ultimate Doom IWAD (<see cref="DoomEnvVar"/>, else <c>wads/DOOM.WAD</c> or <c>wads/doom.wad</c>), or null.</summary>
    public static string? DoomPath => _doom.Value;

    /// <summary>M9: the path of a registered or Ultimate Doom IWAD (any version; no MD5 check), or skips the calling test.</summary>
    public static string RequireDoom() => DoomPath ?? SkipMissing("DOOM.WAD", DoomEnvVar);

    /// <summary>
    /// T9.1: MD5 of The Ultimate Doom's 2024 re-release IWAD (the one with
    /// <c>GAMECONF</c> and <c>DMENUPIC</c>); the registered/Ultimate tests assert
    /// version-specific counts only for this file.
    /// </summary>
    public const string DoomUltimate2024Md5 = "3b37188f6337f15718b617c16e6e7a9c";

    private static readonly Lazy<string?> _doomMd5Value = new(() => DoomPath is null ? null : Md5Of(DoomPath));

    /// <summary>T9.1: MD5 (lower-case hex) of <see cref="DoomPath"/>, or null when there is no registered/Ultimate Doom IWAD.</summary>
    public static string? DoomMd5 => _doomMd5Value.Value;

    private static readonly Lazy<string?> _ultimateDoom = new(() =>
        DoomPath is string p && IsoDoom.Wad.IwadIdentification.D_IdentifyVersion(IsoDoom.Wad.WadFile.Open(p)).GameMode == IsoDoom.Wad.GameMode.retail ? p : null);

    /// <summary>M9: <see cref="DoomPath"/> when it is The Ultimate Doom (<c>retail</c>: with E4), else null (none, or a registered one).</summary>
    public static string? UltimateDoomPath => _ultimateDoom.Value;

    /// <summary>M9: the path of The Ultimate Doom's IWAD (any version: v1.9ud, the 2024 re-release, …), or skips the calling test (none, or a registered DOOM.WAD without E4).</summary>
    public static string RequireUltimateDoom()
    {
        if (UltimateDoomPath is string p)
            return p;
        if (DoomPath is not null)
            Assert.Skip($"{DoomPath} is not The Ultimate Doom (no E4M1): the iwad doom routes need it.");
        return RequireDoom();
    }

    /// <summary>T10.7: environment variable that overrides the TNT.WAD path (an absent path forces a skip).</summary>
    public const string TntEnvVar = "ISODOOM_TNT_WAD";

    /// <summary>T10.7: environment variable that overrides the PLUTONIA.WAD path (an absent path forces a skip).</summary>
    public const string PlutoniaEnvVar = "ISODOOM_PLUTONIA_WAD";

    private static readonly Lazy<string?> _tnt = new(() => FindIwad(TntEnvVar, "TNT.WAD", "tnt.wad"));
    private static readonly Lazy<string?> _plutonia = new(() => FindIwad(PlutoniaEnvVar, "PLUTONIA.WAD", "plutonia.wad"));

    /// <summary>T10.7: Final Doom's TNT: Evilution IWAD (<see cref="TntEnvVar"/>, else <c>wads/TNT.WAD</c> or <c>wads/tnt.wad</c>), or null.</summary>
    public static string? TntPath => _tnt.Value;

    /// <summary>T10.7: Final Doom's Plutonia IWAD (<see cref="PlutoniaEnvVar"/>, else <c>wads/PLUTONIA.WAD</c> or <c>wads/plutonia.wad</c>), or null.</summary>
    public static string? PlutoniaPath => _plutonia.Value;

    /// <summary>T10.7: the path of TNT.WAD (any version), or skips the calling test.</summary>
    public static string RequireTnt() => TntPath ?? SkipMissing("TNT.WAD", TntEnvVar);

    /// <summary>T10.7: the path of PLUTONIA.WAD (any version), or skips the calling test.</summary>
    public static string RequirePlutonia() => PlutoniaPath ?? SkipMissing("PLUTONIA.WAD", PlutoniaEnvVar);

    /// <summary>T10.7: the path of the Final Doom IWAD <paramref name="iwad"/> (<c>tnt</c> or <c>plutonia</c>), or skips the calling test.</summary>
    public static string RequireFinalDoom(string iwad) => iwad switch
    {
        "tnt" => RequireTnt(),
        "plutonia" => RequirePlutonia(),
        _ => throw new ArgumentException($"not a Final Doom IWAD: {iwad}", nameof(iwad)),
    };

    private static string SkipMissing(string name, string envVar)
    {
        string? overridePath = Environment.GetEnvironmentVariable(envVar);
        Assert.Skip(!string.IsNullOrEmpty(overridePath)
            ? $"{name} not found at {Path.GetFullPath(overridePath)} (from {envVar})."
            : $"{name} not found in wads/ (or set {envVar}); put it there to run this test.");
        return "";
    }

    private static string? FindIwad(string envVar, params string[] names)
    {
        string? overridePath = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrEmpty(overridePath))
        {
            string full = Path.GetFullPath(overridePath);
            return File.Exists(full) ? full : null;
        }
        if (RepoRoot is null)
            return null;
        foreach (string name in names)
        {
            string path = Path.Combine(RepoRoot, "wads", name);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    [SuppressMessage("Security", "CA5351", Justification = "Identifies a WAD by its well-known MD5; no security use")]
    internal static string Md5Of(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(MD5.HashData(stream));
    }

    private static string? FindDoom2()
    {
        string? overridePath = Environment.GetEnvironmentVariable(Doom2EnvVar);
        if (!string.IsNullOrEmpty(overridePath))
        {
            string full = Path.GetFullPath(overridePath);
            return File.Exists(full) ? full : null;
        }
        if (RepoRoot is null)
            return null;
        foreach (string name in new[] { "DOOM2.WAD", "doom2.wad" })
        {
            string path = Path.Combine(RepoRoot, "wads", name);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    private static string? FindDoom1()
    {
        string? overridePath = Environment.GetEnvironmentVariable(Doom1EnvVar);
        string? path = !string.IsNullOrEmpty(overridePath)
            ? Path.GetFullPath(overridePath)
            : RepoRoot is null ? null : Path.Combine(RepoRoot, "wads", "DOOM1.WAD");
        return path is not null && File.Exists(path) ? path : null;
    }

    private static string? CheckDoom1()
    {
        string? path = Doom1Path;
        if (path is null)
        {
            string? overridePath = Environment.GetEnvironmentVariable(Doom1EnvVar);
            string where = !string.IsNullOrEmpty(overridePath)
                ? $"{Path.GetFullPath(overridePath)} (from {Doom1EnvVar})"
                : RepoRoot is null ? "wads/DOOM1.WAD (repo root not found)" : Path.Combine(RepoRoot, "wads", "DOOM1.WAD");
            return $"DOOM1.WAD not found at {where}; put the shareware v1.9 WAD there to run this test.";
        }

        string md5 = Md5Of(path);
        return md5 == Doom1Md5
            ? null
            : $"{path} is not the shareware v1.9 DOOM1.WAD (MD5 {md5}, expected {Doom1Md5}).";
    }

    private static string? FindRepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "IsoDoom.sln")))
                return dir.FullName;
        }
        return null;
    }
}
