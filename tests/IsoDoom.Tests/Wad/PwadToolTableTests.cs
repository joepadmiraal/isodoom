using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using IsoDoom.Tests.Support;
using Xunit;

namespace IsoDoom.Tests.Wad;

/// <summary>
/// P2: the vanilla reference's tools load the PWAD sets through their own
/// table (<c>tools/VanillaRef/pwads.py</c>: <c>compare.sh</c>, <c>demos.sh</c>
/// and <c>music.sh</c>'s <c>--pwad SET</c>, a route's <c>pwad SET</c>
/// header). It must name the same sets, IWADs and <c>-file</c> lists in the
/// same order as <see cref="PwadSets"/>. Needs no WAD. P10: so must RouteGen's
/// (<c>tools/RouteGen/PwadSet.cs</c>: the <c>SET-mapNN-…</c> routes).
/// </summary>
public sealed partial class PwadToolTableTests
{
    [GeneratedRegex(@"^\s*'(?<folder>[a-z0-9]+)': \('(?<iwad>doom2|doom)', \[(?<files>[^\]]*)\]\),\s*$", RegexOptions.Multiline)]
    private static partial Regex SetLine();

    [Fact]
    public void ThePythonTableMatchesPwadSets()
    {
        string path = Path.Combine(TestWads.RepoRoot!, "tools", "VanillaRef", "pwads.py");
        var rows = SetLine().Matches(File.ReadAllText(path))
            .Select(m => (
                Folder: m.Groups["folder"].Value,
                Iwad: m.Groups["iwad"].Value == "doom2" ? PwadIwad.Doom2 : PwadIwad.UltimateDoom,
                Files: string.Join(",", m.Groups["files"].Value.Split(',').Select(f => f.Trim().Trim('\'')))))
            .ToList();
        var expected = PwadSets.All.Select(s => (s.Folder, s.Iwad, Files: string.Join(",", s.Files))).ToList();
        Assert.Equal(expected, rows);
    }

    [GeneratedRegex(@"^\s*new\(""(?<folder>[a-z0-9]+)"", ""(?<title>[^""]+)"", ""(?<iwad>doom2|doom)"", \[(?<files>[^\]]*)\]\),\s*$", RegexOptions.Multiline)]
    private static partial Regex RouteGenLine();

    [Fact]
    public void RouteGensTableMatchesPwadSets()
    {
        string path = Path.Combine(TestWads.RepoRoot!, "tools", "RouteGen", "PwadSet.cs");
        var rows = RouteGenLine().Matches(File.ReadAllText(path))
            .Select(m => (
                Folder: m.Groups["folder"].Value,
                Title: m.Groups["title"].Value,
                Iwad: m.Groups["iwad"].Value == "doom2" ? PwadIwad.Doom2 : PwadIwad.UltimateDoom,
                Files: string.Join(",", m.Groups["files"].Value.Split(',').Select(f => f.Trim().Trim('"')))))
            .ToList();
        var expected = PwadSets.All.Select(s => (s.Folder, s.Title, s.Iwad, Files: string.Join(",", s.Files))).ToList();
        Assert.Equal(expected, rows);
    }
}
