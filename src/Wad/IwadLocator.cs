using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace IsoDoom.Wad;

/// <summary>The operating system whose install locations <see cref="IwadLocator"/> searches.</summary>
public enum IwadPlatform
{
    Linux,
    Windows,
    MacOS,
}

/// <summary>Where <see cref="IwadLocator.D_FindIWAD"/> found the IWAD.</summary>
public enum IwadSource
{
    /// <summary>Not found.</summary>
    None,
    /// <summary><c>-iwad PATH</c> on the command line.</summary>
    CommandLine,
    /// <summary>The <c>ISODOOM_IWAD</c> (or <c>ISODOOM_DOOM1_WAD</c>) environment variable.</summary>
    Environment,
    /// <summary>The configured path (the settings file, written by the file picker).</summary>
    Config,
    /// <summary>A search directory (<see cref="IwadSearchResult.SourceDetail"/> names it).</summary>
    Search,
}

/// <summary>
/// Everything <see cref="IwadLocator"/> reads from the outside world except the
/// file system, so tests can fake it. The Godot glue (<c>IsoDoom.Game.WadLocator</c>)
/// fills it from the process.
/// </summary>
public sealed class IwadSearchContext
{
    /// <summary>The command line (<c>-iwad</c>, <c>-file</c>).</summary>
    public CommandLine CommandLine { get; init; } = new("isodoom", []);

    /// <summary>Environment variables (null when unset).</summary>
    public Func<string, string?> GetEnv { get; init; } = Environment.GetEnvironmentVariable;

    /// <summary>The user's working directory: relative paths resolve against it, and it is searched first.</summary>
    public string CurrentDirectory { get; init; } = Directory.GetCurrentDirectory();

    /// <summary>The game folder(s), searched right after the working directory.</summary>
    public IReadOnlyList<string> GameDirectories { get; init; } = [];

    /// <summary>The configured IWAD path (from the settings file), or null.</summary>
    public string? ConfiguredIwad { get; init; }

    /// <summary>Selects the install locations to search.</summary>
    public IwadPlatform Platform { get; init; } = CurrentPlatform;

    /// <summary>
    /// Reads a string value under <c>HKEY_LOCAL_MACHINE</c> (32-bit view, where
    /// the Steam and GOG installers write) on Windows: (subkey, value name) →
    /// value or null.
    /// </summary>
    public Func<string, string, string?> ReadRegistry { get; init; } = WindowsRegistry.ReadLocalMachine;

    public static IwadPlatform CurrentPlatform =>
        OperatingSystem.IsWindows() ? IwadPlatform.Windows
        : OperatingSystem.IsMacOS() ? IwadPlatform.MacOS
        : IwadPlatform.Linux;
}

/// <summary>The outcome of <see cref="IwadLocator.D_FindIWAD"/>.</summary>
/// <param name="Path">The IWAD, or null when none was found (see <paramref name="Error"/>).</param>
/// <param name="Source">Where it came from.</param>
/// <param name="SourceDetail">The argument, variable or search directory it came from.</param>
/// <param name="Error">Why there is no IWAD, or null.</param>
/// <param name="Warning">A non-fatal problem (a configured IWAD that no longer exists), or null.</param>
/// <param name="Pwads">The <c>-file</c> PWADs, resolved, in load order (found or not).</param>
public sealed record IwadSearchResult(
    string? Path,
    IwadSource Source,
    string? SourceDetail,
    string? Error,
    string? Warning,
    IReadOnlyList<string> Pwads);

/// <summary>
/// Finds the IWAD and the <c>-file</c> PWADs (SPEC §5.1), after Chocolate
/// Doom's d_iwad.c (<c>D_FindIWAD</c>, <c>BuildIWADDirList</c>,
/// <c>D_FindWADByName</c>) and w_main.c (<c>-file</c>). Order:
/// <list type="number">
/// <item><c>-iwad PATH</c> (or <c>--iwad PATH</c>), then the <c>ISODOOM_IWAD</c>
/// and <c>ISODOOM_DOOM1_WAD</c> environment variables. A bare file name is also
/// looked for in the search directories. An explicit IWAD that doesn't exist is
/// an error, not replaced by a fallback (Chocolate Doom: "IWAD file not found").</item>
/// <item>The configured path (written by the file picker). If it no longer
/// exists, the search goes on with a warning.</item>
/// <item>The search directories (<see cref="iwad_dirs"/>), in order; the first
/// directory holding any IWAD name from <see cref="iwads"/> wins, and within a
/// directory the earliest name in <see cref="iwads"/> wins.</item>
/// </list>
/// Not found: the caller shows the file picker.
/// </summary>
public sealed class IwadLocator
{
    /// <summary>An IWAD file name the search recognises (d_iwad.c: <c>iwad_t</c>, name and description only).</summary>
    public sealed record IwadName(string Name, string Description);

    /// <summary>
    /// The IWAD names in order of preference within one directory (d_iwad.c:
    /// <c>iwads[]</c>, plus linuxdoom's <c>doomu.wad</c>). Chocolate Doom and
    /// linuxdoom put DOOM II first; here the DOOM 1 IWADs come first, the full
    /// game before the shareware one, because DOOM 1 is the content supported
    /// first (SPEC §2). Revisit when DOOM II is playable (SPEC §12).
    /// </summary>
    public static readonly IReadOnlyList<IwadName> iwads =
    [
        new("doom.wad", "Doom"),
        new("doomu.wad", "The Ultimate Doom"),
        new("doom1.wad", "Doom Shareware"),
        new("doom2.wad", "Doom II"),
        new("doom2f.wad", "Doom II: L'Enfer sur Terre"),
        new("plutonia.wad", "Final Doom: Plutonia Experiment"),
        new("tnt.wad", "Final Doom: TNT: Evilution"),
        new("freedoom1.wad", "Freedoom: Phase 1"),
        new("freedoom2.wad", "Freedoom: Phase 2"),
        new("freedm.wad", "FreeDM"),
    ];

    /// <summary>Steam app folders (under <c>steamapps/common</c>) that hold IWADs, in search order.</summary>
    public static readonly IReadOnlyList<string> SteamSubdirs =
    [
        "Ultimate Doom/base",
        "Ultimate Doom/rerelease",
        "Doom 2/base",
        "Doom 2/rerelease",
        "Doom 2/finaldoombase",
        "Final Doom/base",
        "DOOM 3 BFG Edition/base/wads",
        "Master Levels of Doom/doom2",
    ];

    /// <summary>GOG registry keys (<c>HKLM\SOFTWARE\GOG.com\Games\…</c>, value <c>PATH</c>) from d_iwad.c's <c>root_path_keys</c>.</summary>
    public static readonly IReadOnlyList<string> GogRegistryKeys =
    [
        @"SOFTWARE\GOG.com\Games\1435827232", // The Ultimate Doom
        @"SOFTWARE\GOG.com\Games\1435848814", // Doom II
        @"SOFTWARE\GOG.com\Games\1435848742", // Final Doom
        @"SOFTWARE\GOG.com\Games\1135892318", // Doom 3: BFG Edition
    ];

    /// <summary>Subfolders of a GOG install that hold IWADs (d_iwad.c: <c>root_path_subdirs</c>, plus the re-release folders).</summary>
    public static readonly IReadOnlyList<string> GogSubdirs =
    [
        ".", "base", "rerelease", "Ultimate Doom", "Doom2", "Final Doom", "TNT", "Plutonia", "base/wads",
    ];

    private readonly IwadSearchContext _ctx;
    private List<string>? _iwadDirs;

    public IwadLocator(IwadSearchContext ctx) => _ctx = ctx;

    /// <summary>The directories searched for IWADs, in order (d_iwad.c: <c>iwad_dirs</c>).</summary>
    public IReadOnlyList<string> iwad_dirs => _iwadDirs ??= BuildIWADDirList();

    /// <summary>Finds the IWAD and resolves the <c>-file</c> PWADs (d_iwad.c: <c>D_FindIWAD</c>).</summary>
    public IwadSearchResult D_FindIWAD()
    {
        IReadOnlyList<string> pwads = FindPwads();

        string? iwadParm = _ctx.CommandLine.GetParmValue("-iwad", "--iwad");
        if (iwadParm is not null)
            return Explicit(iwadParm, IwadSource.CommandLine, "-iwad", pwads);
        foreach (string var in new[] { "ISODOOM_IWAD", "ISODOOM_DOOM1_WAD" })
        {
            string? value = _ctx.GetEnv(var);
            if (!string.IsNullOrEmpty(value))
                return Explicit(value, IwadSource.Environment, var, pwads);
        }

        string? warning = null;
        if (!string.IsNullOrEmpty(_ctx.ConfiguredIwad))
        {
            string? configured = M_FileCaseExists(Resolve(_ctx.ConfiguredIwad));
            if (configured is not null)
                return new IwadSearchResult(configured, IwadSource.Config, _ctx.ConfiguredIwad, null, null, pwads);
            warning = $"The configured IWAD no longer exists: {_ctx.ConfiguredIwad}";
        }

        foreach (string dir in iwad_dirs)
        {
            string? found = SearchDirectoryForIWAD(dir);
            if (found is not null)
                return new IwadSearchResult(found, IwadSource.Search, dir, null, warning, pwads);
        }
        return new IwadSearchResult(null, IwadSource.None, null, "No IWAD found.", warning, pwads);
    }

    /// <summary>Every IWAD in the search directories, in preference order, without duplicates (d_iwad.c: <c>D_FindAllIWADs</c>).</summary>
    public IReadOnlyList<string> D_FindAllIWADs()
    {
        var result = new List<string>();
        var seen = new HashSet<string>(PathComparer);
        foreach (string dir in iwad_dirs)
        {
            foreach (IwadName iwad in iwads)
            {
                string? path = M_FileCaseExists(Path.Combine(dir, iwad.Name));
                if (path is not null && seen.Add(Path.GetFullPath(path)))
                    result.Add(path);
            }
        }
        return result;
    }

    /// <summary>
    /// <paramref name="name"/> as given (relative to the working directory), or
    /// else in a search directory; null when not found (d_iwad.c: <c>D_FindWADByName</c>).
    /// </summary>
    public string? D_FindWADByName(string name)
    {
        string? direct = M_FileCaseExists(Resolve(name));
        if (direct is not null)
            return direct;
        if (Path.IsPathRooted(name))
            return null;

        foreach (string dir in iwad_dirs)
        {
            // As in Chocolate Doom, a "directory" in DOOMWADPATH may name an IWAD file directly.
            if (File.Exists(dir) && string.Equals(Path.GetFileName(dir), name, StringComparison.OrdinalIgnoreCase))
                return dir;
            string? path = M_FileCaseExists(Path.Combine(dir, name));
            if (path is not null)
                return path;
        }
        return null;
    }

    /// <summary>
    /// <see cref="D_FindWADByName"/>, or <paramref name="name"/> resolved against
    /// the working directory when not found, so the loader reports it
    /// (d_iwad.c: <c>D_TryFindWADByName</c>).
    /// </summary>
    public string D_TryFindWADByName(string name) => D_FindWADByName(name) ?? Resolve(name);

    /// <summary>The first IWAD name from <see cref="iwads"/> present in <paramref name="dir"/>, or null (d_iwad.c: <c>SearchDirectoryForIWAD</c>).</summary>
    public static string? SearchDirectoryForIWAD(string dir)
    {
        if (!Directory.Exists(dir))
        {
            // A DOOMWADPATH entry may name an IWAD file.
            if (File.Exists(dir) && IsIwadName(Path.GetFileName(dir)))
                return dir;
            return null;
        }
        foreach (IwadName iwad in iwads)
        {
            string? path = M_FileCaseExists(Path.Combine(dir, iwad.Name));
            if (path is not null)
                return path;
        }
        return null;
    }

    /// <summary>True if <paramref name="fileName"/> is one of <see cref="iwads"/> (any case; d_iwad.c: <c>D_IsIWADName</c>).</summary>
    public static bool IsIwadName(string fileName)
    {
        foreach (IwadName iwad in iwads)
        {
            if (string.Equals(iwad.Name, fileName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>The <see cref="iwads"/> description for a file name, or null.</summary>
    public static string? DescriptionForName(string fileName)
    {
        foreach (IwadName iwad in iwads)
        {
            if (string.Equals(iwad.Name, fileName, StringComparison.OrdinalIgnoreCase))
                return iwad.Description;
        }
        return null;
    }

    /// <summary>
    /// <paramref name="path"/> if it exists as a file, else with its file name in
    /// lower case, upper case or capitalised; null if none exists
    /// (m_misc.c: <c>M_FileCaseExists</c>).
    /// </summary>
    public static string? M_FileCaseExists(string path)
    {
        if (File.Exists(path))
            return path;
        string? dir = Path.GetDirectoryName(path);
        string name = Path.GetFileName(path);
        if (name.Length == 0)
            return null;
        string lower = name.ToLowerInvariant();
        foreach (string candidate in new[] { lower, name.ToUpperInvariant(), char.ToUpperInvariant(lower[0]) + lower[1..] })
        {
            string p = string.IsNullOrEmpty(dir) ? candidate : Path.Combine(dir, candidate);
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    /// <summary>
    /// The search directories, in order (d_iwad.c: <c>BuildIWADDirList</c>):
    /// the working directory; the game folders; <c>DOOMWADDIR</c>; the
    /// <c>DOOMWADPATH</c> entries; then the platform's install locations
    /// (<see cref="AddPlatformDirs"/>). Duplicates are dropped.
    /// </summary>
    private List<string> BuildIWADDirList()
    {
        var dirs = new List<string>();
        var seen = new HashSet<string>(PathComparer);
        void Add(string? dir)
        {
            if (!string.IsNullOrEmpty(dir) && seen.Add(dir))
                dirs.Add(dir);
        }

        Add(_ctx.CurrentDirectory);
        foreach (string dir in _ctx.GameDirectories)
            Add(Resolve(dir));
        string? doomwaddir = _ctx.GetEnv("DOOMWADDIR");
        if (!string.IsNullOrEmpty(doomwaddir))
            Add(Resolve(doomwaddir));
        foreach (string dir in SplitPath(_ctx.GetEnv("DOOMWADPATH")))
            Add(Resolve(dir));
        foreach (string dir in AddPlatformDirs())
            Add(dir);
        return dirs;
    }

    /// <summary>
    /// Install locations. Linux: XDG data directories (<c>games/doom</c>, <c>doom</c>),
    /// then Steam (native, Flatpak and Snap), then GOG-style folders. Windows:
    /// GOG registry keys, Steam (registry and default folder), then GOG
    /// folders. macOS: Steam. Steam covers every library in
    /// <c>libraryfolders.vdf</c> (e.g. a Steam Deck SD card).
    /// </summary>
    private IEnumerable<string> AddPlatformDirs()
    {
        string? home = _ctx.Platform == IwadPlatform.Windows ? _ctx.GetEnv("USERPROFILE") : _ctx.GetEnv("HOME");
        var steamRoots = new List<string>();
        var gogRoots = new List<string>();

        switch (_ctx.Platform)
        {
            case IwadPlatform.Linux:
                {
                    string? dataHome = _ctx.GetEnv("XDG_DATA_HOME");
                    if (string.IsNullOrEmpty(dataHome) && home is not null)
                        dataHome = Path.Combine(home, ".local", "share");
                    if (!string.IsNullOrEmpty(dataHome))
                        yield return Path.Combine(dataHome, "games", "doom");
                    string? dataDirs = _ctx.GetEnv("XDG_DATA_DIRS");
                    if (string.IsNullOrEmpty(dataDirs))
                        dataDirs = "/usr/local/share:/usr/share";
                    foreach (string d in SplitPath(dataDirs))
                    {
                        yield return Path.Combine(d, "games", "doom");
                        yield return Path.Combine(d, "doom");
                    }
                    if (home is not null)
                    {
                        steamRoots.Add(Path.Combine(home, ".steam", "root"));
                        steamRoots.Add(Path.Combine(home, ".steam", "steam"));
                        steamRoots.Add(Path.Combine(home, ".local", "share", "Steam"));
                        steamRoots.Add(Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"));
                        steamRoots.Add(Path.Combine(home, "snap", "steam", "common", ".local", "share", "Steam"));
                        gogRoots.Add(Path.Combine(home, "GOG Games"));
                        gogRoots.Add(Path.Combine(home, "Games", "Heroic"));
                        gogRoots.Add(Path.Combine(home, "Games"));
                    }
                    break;
                }
            case IwadPlatform.Windows:
                {
                    foreach (string key in GogRegistryKeys)
                    {
                        string? root = _ctx.ReadRegistry(key, "PATH");
                        if (string.IsNullOrEmpty(root))
                            continue;
                        foreach (string sub in GogSubdirs)
                            yield return sub == "." ? root : Path.Combine(root, sub);
                    }
                    string? steam = _ctx.ReadRegistry(@"SOFTWARE\Valve\Steam", "InstallPath");
                    if (!string.IsNullOrEmpty(steam))
                        steamRoots.Add(steam);
                    string? programFiles = _ctx.GetEnv("ProgramFiles(x86)");
                    if (!string.IsNullOrEmpty(programFiles))
                    {
                        steamRoots.Add(Path.Combine(programFiles, "Steam"));
                        gogRoots.Add(Path.Combine(programFiles, "GOG Galaxy", "Games"));
                    }
                    string? systemDrive = _ctx.GetEnv("SystemDrive");
                    if (!string.IsNullOrEmpty(systemDrive))
                        gogRoots.Add(Path.Combine(systemDrive + Path.DirectorySeparatorChar, "GOG Games"));
                    break;
                }
            case IwadPlatform.MacOS:
                if (home is not null)
                    steamRoots.Add(Path.Combine(home, "Library", "Application Support", "Steam"));
                break;
        }

        foreach (string library in SteamLibraries(steamRoots))
        {
            foreach (string sub in SteamSubdirs)
                yield return Path.Combine(library, "steamapps", "common", sub);
        }
        foreach (string dir in GogFolders(gogRoots))
            yield return dir;
    }

    /// <summary>Each Steam root, followed by the other libraries its <c>libraryfolders.vdf</c> lists.</summary>
    private IEnumerable<string> SteamLibraries(List<string> roots)
    {
        var seen = new HashSet<string>(PathComparer);
        foreach (string root in roots)
        {
            if (seen.Add(root))
                yield return root;
            foreach (string vdf in new[] { Path.Combine(root, "steamapps", "libraryfolders.vdf"), Path.Combine(root, "config", "libraryfolders.vdf") })
            {
                string text;
                try
                {
                    if (!File.Exists(vdf))
                        continue;
                    text = File.ReadAllText(vdf);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                foreach (string library in ParseSteamLibraryFolders(text))
                {
                    if (seen.Add(library))
                        yield return library;
                }
            }
        }
    }

    /// <summary>The <c>"path"</c> values of a Steam <c>libraryfolders.vdf</c> (with <c>\\</c> unescaped).</summary>
    public static IReadOnlyList<string> ParseSteamLibraryFolders(string vdf)
    {
        var result = new List<string>();
        foreach (Match m in Regex.Matches(vdf, "\"path\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase))
            result.Add(Regex.Replace(m.Groups[1].Value, @"\\(.)", "$1"));
        return result;
    }

    /// <summary>
    /// For each GOG-style root (an install folder of GOG Galaxy, Heroic,
    /// minigalaxy and the like), the <see cref="GogSubdirs"/> of every child
    /// folder whose name contains "doom" (any case), in ordinal name order.
    /// </summary>
    private static IEnumerable<string> GogFolders(List<string> roots)
    {
        foreach (string root in roots)
        {
            string[] children;
            try
            {
                if (!Directory.Exists(root))
                    continue;
                children = Directory.GetDirectories(root);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            Array.Sort(children, StringComparer.Ordinal);
            foreach (string child in children)
            {
                if (!Path.GetFileName(child).Contains("doom", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (string sub in GogSubdirs)
                    yield return sub == "." ? child : Path.Combine(child, sub);
            }
        }
    }

    private IwadSearchResult Explicit(string value, IwadSource source, string detail, IReadOnlyList<string> pwads)
    {
        string? path = D_FindWADByName(value);
        return path is not null
            ? new IwadSearchResult(path, source, detail, null, null, pwads)
            : new IwadSearchResult(null, source, detail, $"IWAD file '{value}' not found ({detail})!", null, pwads);
    }

    /// <summary>The <c>-file</c> PWADs (w_main.c: <c>W_ParseCommandLine</c>), each through <see cref="D_TryFindWADByName"/>.</summary>
    private IReadOnlyList<string> FindPwads()
    {
        var result = new List<string>();
        foreach (string name in _ctx.CommandLine.GetParmList("-file"))
            result.Add(D_TryFindWADByName(name));
        return result;
    }

    private string Resolve(string path) => Path.IsPathRooted(path) ? path : Path.Combine(_ctx.CurrentDirectory, path);

    private IEnumerable<string> SplitPath(string? list) =>
        string.IsNullOrEmpty(list)
            ? []
            : list.Split(_ctx.Platform == IwadPlatform.Windows ? ';' : ':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private StringComparer PathComparer =>
        _ctx.Platform == IwadPlatform.Linux ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
}

/// <summary>Registry access for <see cref="IwadSearchContext.ReadRegistry"/>; null on other systems.</summary>
public static class WindowsRegistry
{
    public static string? ReadLocalMachine(string subkey, string valueName)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            using var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry32);
            using Microsoft.Win32.RegistryKey? key = hklm.OpenSubKey(subkey);
            return key?.GetValue(valueName) as string;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
