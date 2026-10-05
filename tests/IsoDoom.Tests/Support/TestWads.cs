using System;
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

    private static readonly Lazy<string?> Doom1 = new(FindDoom1);
    private static readonly Lazy<string?> Doom1Problem = new(CheckDoom1);

    /// <summary>The repo root (the directory holding IsoDoom.sln), or null if not found.</summary>
    public static string? RepoRoot { get; } = FindRepoRoot();

    /// <summary>
    /// Full path of <c>wads/DOOM1.WAD</c> (or of <see cref="Doom1EnvVar"/> when set),
    /// or null when the file does not exist.
    /// </summary>
    public static string? Doom1Path => Doom1.Value;

    /// <summary>
    /// Returns the path of the shareware v1.9 DOOM1.WAD, or skips the calling
    /// test when it is absent or is a different WAD (wrong MD5).
    /// </summary>
    public static string RequireDoom1()
    {
        string? problem = Doom1Problem.Value;
        if (problem is not null)
            Assert.Skip(problem);
        return Doom1Path!;
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

        using FileStream stream = File.OpenRead(path);
        string md5 = Convert.ToHexStringLower(MD5.HashData(stream));
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
