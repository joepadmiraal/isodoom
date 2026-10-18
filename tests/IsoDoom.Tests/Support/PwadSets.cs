using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace IsoDoom.Tests.Support;

/// <summary>The IWAD a PWAD set is played over.</summary>
public enum PwadIwad
{
    /// <summary>Doom II (<see cref="TestWads.RequireDoom2"/>).</summary>
    Doom2,

    /// <summary>The Ultimate Doom (<see cref="TestWads.RequireUltimateDoom"/>).</summary>
    UltimateDoom,
}

/// <summary>
/// P1: one PWAD set of <c>docs/PWAD-VALIDATION.md</c>'s table: its folder
/// under <c>wads/pwads/</c>, its IWAD, its <c>-file</c> list in load order and
/// the maps the table names (in the order the game lists them).
/// </summary>
public sealed record PwadSet(string Folder, string Title, PwadIwad Iwad, IReadOnlyList<string> Files, IReadOnlyList<string> Maps)
{
    /// <summary>The set's directory (<see cref="PwadSets.Root"/>/<see cref="Folder"/>), or null without a root.</summary>
    public string? Directory => PwadSets.Root is string root ? Path.Combine(root, Folder) : null;

    /// <summary>The full paths of <see cref="Files"/>, in load order (<c>-file</c>'s).</summary>
    public IReadOnlyList<string> Paths => [.. Files.Select(f => Path.Combine(Directory ?? "", f))];

    /// <summary>Whether every file of the set is there.</summary>
    public bool IsPresent => Directory is not null && Paths.All(File.Exists);

    /// <summary>
    /// The IWAD's path and the set's files, or skips the calling test when the
    /// set or its IWAD is absent (as the IWAD tests skip without their WAD).
    /// </summary>
    public (string Iwad, string[] Pwads) Require()
    {
        if (!IsPresent)
        {
            string where = Directory ?? $"wads/pwads/{Folder} (repo root not found)";
            Assert.Skip($"PWAD set {Folder} ({Title}) not found in {where} (or set {PwadSets.EnvVar}); it needs {string.Join(", ", Files)}.");
        }
        string iwad = Iwad == PwadIwad.Doom2 ? TestWads.RequireDoom2() : TestWads.RequireUltimateDoom();
        return (iwad, [.. Paths]);
    }

    public override string ToString() => Folder;
}

/// <summary>
/// P1: the eight PWAD sets of <c>docs/PWAD-VALIDATION.md</c>, found in
/// <c>wads/pwads/FOLDER/</c> or <c>$ISODOOM_PWADS/FOLDER/</c>. WAD data never
/// lives in the repo: a test that needs a set calls <see cref="PwadSet.Require"/>,
/// which skips it when the set is absent.
/// </summary>
public static class PwadSets
{
    /// <summary>Environment variable naming the directory that holds the set folders (instead of <c>wads/pwads</c>).</summary>
    public const string EnvVar = "ISODOOM_PWADS";

    /// <summary>The directory holding the set folders: <see cref="EnvVar"/> when set, else <c>wads/pwads</c>; null without a repo root.</summary>
    public static string? Root { get; } =
        Environment.GetEnvironmentVariable(EnvVar) is { Length: > 0 } dir ? Path.GetFullPath(dir)
        : TestWads.RepoRoot is string repo ? Path.Combine(repo, "wads", "pwads")
        : null;

    private static string[] Doom2Maps(int last) => [.. Enumerable.Range(1, last).Select(m => $"MAP{m:00}")];

    private static string[] EpisodeMaps(int episodes) =>
        [.. Enumerable.Range(1, episodes).SelectMany(e => Enumerable.Range(1, 9).Select(m => $"E{e}M{m}"))];

    public static PwadSet Scythe { get; } = new("scythe", "Scythe", PwadIwad.Doom2, ["SCYTHE.WAD"], Doom2Maps(32));
    public static PwadSet AlienVendetta { get; } = new("av", "Alien Vendetta", PwadIwad.Doom2, ["AV.WAD", "AVMOVFIX.WAD"], Doom2Maps(32));
    public static PwadSet MementoMori { get; } = new("mm", "Memento Mori", PwadIwad.Doom2, ["MM.WAD", "MMMUS.WAD"], Doom2Maps(32));
    public static PwadSet Doom2TheWayIdDid { get; } = new("d2twid", "Doom 2 the Way id Did", PwadIwad.Doom2, ["D2TWID.wad"], Doom2Maps(33));
    public static PwadSet Requiem { get; } = new("requiem", "Requiem", PwadIwad.Doom2, ["REQUIEM.WAD", "REQMUS.WAD"], Doom2Maps(32));
    public static PwadSet HellRevealed { get; } = new("hr", "Hell Revealed", PwadIwad.Doom2, ["HR.WAD", "HRMUS.WAD"], Doom2Maps(32));
    public static PwadSet DoomTheWayIdDid { get; } = new("dtwid", "Doom the Way id Did", PwadIwad.UltimateDoom, ["DTWID.wad"], EpisodeMaps(3));

    /// <summary>NEIS: E1–E4, and E1M0 and E4M0 after E1M9 and E4M9 (its own order; the game lists them after the IWAD's maps).</summary>
    public static PwadSet NoEndInSight { get; } = new("neis", "No End in Sight", PwadIwad.UltimateDoom, ["NEIS.wad"],
        [.. EpisodeMaps(4), "E1M0", "E4M0"]);

    /// <summary>The eight sets, in the table's order.</summary>
    public static IReadOnlyList<PwadSet> All { get; } =
        [Scythe, AlienVendetta, MementoMori, Doom2TheWayIdDid, Requiem, HellRevealed, DoomTheWayIdDid, NoEndInSight];

    /// <summary>The set in folder <paramref name="folder"/>.</summary>
    public static PwadSet Get(string folder) =>
        All.FirstOrDefault(s => s.Folder == folder) ?? throw new ArgumentException($"no PWAD set {folder}", nameof(folder));

    /// <summary>The sets' folders, for a <c>[MemberData]</c> theory.</summary>
    public static TheoryData<string> Folders() => [.. All.Select(s => s.Folder)];
}
