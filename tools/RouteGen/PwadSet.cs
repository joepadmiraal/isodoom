using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using IsoDoom.Wad;

namespace IsoDoom.Tools.RouteGen;

/// <summary>
/// P10: a PWAD set of <c>docs/PWAD-VALIDATION.md</c> (the tests' <c>PwadSets</c>,
/// which RouteGen cannot link: it needs xUnit), for the routes named
/// <c>SET-mapNN-…</c> or <c>SET-exmy-…</c>: its folder under <c>wads/pwads/</c>
/// (or <c>$ISODOOM_PWADS</c>), its IWAD's <c>iwad</c> header name and its
/// <c>-file</c> list in load order. <c>PwadToolTableTests</c> checks the table
/// against <c>PwadSets</c>.
/// </summary>
public sealed record PwadSet(string Folder, string Title, string Iwad, IReadOnlyList<string> Files)
{
    /// <summary>The eight sets, in the table's order.</summary>
    public static IReadOnlyList<PwadSet> All { get; } =
    [
        new("scythe", "Scythe", "doom2", ["SCYTHE.WAD"]),
        new("av", "Alien Vendetta", "doom2", ["AV.WAD", "AVMOVFIX.WAD"]),
        new("mm", "Memento Mori", "doom2", ["MM.WAD", "MMMUS.WAD"]),
        new("d2twid", "Doom 2 the Way id Did", "doom2", ["D2TWID.wad"]),
        new("requiem", "Requiem", "doom2", ["REQUIEM.WAD", "REQMUS.WAD"]),
        new("hr", "Hell Revealed", "doom2", ["HR.WAD", "HRMUS.WAD"]),
        new("dtwid", "Doom the Way id Did", "doom", ["DTWID.wad"]),
        new("neis", "No End in Sight", "doom", ["NEIS.wad"]),
    ];

    private static readonly Regex _prefix = new(@"^(?<set>[a-z0-9]+)-(?:map\d\d|e[1-4]m[0-9])-", RegexOptions.CultureInvariant);

    /// <summary>The set a route name starts with (<c>av-map01-walk-exit</c>), or null.</summary>
    public static PwadSet? Of(string name) =>
        _prefix.Match(name) is { Success: true } m ? All.FirstOrDefault(s => s.Folder == m.Groups["set"].Value) : null;

    /// <summary>The set's files over <paramref name="iwad"/>, as <c>-file</c> loads them.</summary>
    public WadArchive Over(WadArchive iwad, string repoRoot)
    {
        string root = Environment.GetEnvironmentVariable("ISODOOM_PWADS") is { Length: > 0 } dir ? dir : Path.Combine(repoRoot, "wads", "pwads");
        string[] paths = [.. Files.Select(f => Path.Combine(root, Folder, f))];
        if (paths.FirstOrDefault(p => !File.Exists(p)) is string missing)
            throw new FileNotFoundException($"PWAD set {Folder}: {missing} not found", missing);
        return new WadArchive([.. iwad.Files, .. paths.Select(WadFile.Open)]);
    }
}
