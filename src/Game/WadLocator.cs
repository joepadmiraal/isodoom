using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Wad;

namespace IsoDoom.Game;

/// <summary>
/// Godot glue for the IWAD search (T1.1a, SPEC §5.1). The search itself is
/// <see cref="IwadLocator"/> (plain C#, unit-tested); this fills its
/// <see cref="IwadSearchContext"/> from the process and keeps the configured
/// IWAD in <c>user://settings.cfg</c> (<c>[wad] iwad</c>), which the file
/// picker writes.
/// <para>
/// Command line: Godot's user arguments, i.e. those after <c>--</c>
/// (<c>godot -- -iwad doom2.wad -file mymap.wad</c>,
/// <c>IsoDoom.x86_64 -- -iwad …</c>).
/// </para>
/// </summary>
public static class WadLocator
{
    public const string SettingsPath = "user://settings.cfg";
    private const string Section = "wad";
    private const string IwadKey = "iwad";

    /// <summary>Runs the search with the process's command line, environment, folders and settings.</summary>
    public static IwadSearchResult Find(out IReadOnlyList<string> searchedDirs)
    {
        var locator = new IwadLocator(CreateContext());
        IwadSearchResult result = locator.D_FindIWAD();
        searchedDirs = locator.iwad_dirs;
        return result;
    }

    public static IwadSearchContext CreateContext() => new()
    {
        CommandLine = new CommandLine(OS.GetExecutablePath(), OS.GetCmdlineUserArgs()),
        CurrentDirectory = LaunchDirectory,
        GameDirectories = GameDirectories(),
        ConfiguredIwad = ChosenIwad ?? LoadConfiguredIwad(),
    };

    /// <summary>T7.2: the IWAD chosen in the IWAD menu this run (taken as the configured one, even when the settings can't be saved).</summary>
    public static string? ChosenIwad { get; set; }

    /// <summary>
    /// The game folder: the project folder in editor runs, the executable's
    /// folder in exports (and on macOS, T8.7b, the folder holding
    /// <c>IsoDoom.app</c>); each followed by its <c>wads/</c> subfolder
    /// (<see cref="IwadSearchContext.GameDirectoriesFor"/>).
    /// </summary>
    public static IReadOnlyList<string> GameDirectories()
    {
        string? root = OS.HasFeature("editor")
            ? ProjectSettings.GlobalizePath("res://")
            : Path.GetDirectoryName(OS.GetExecutablePath());
        return string.IsNullOrEmpty(root) ? [] : IwadSearchContext.GameDirectoriesFor(root);
    }

    /// <summary>
    /// T8.4b: the directory the game was launched from, against which relative
    /// path arguments resolve (<see cref="ResolveUserPath"/>). Godot's export
    /// templates change the working directory to the executable's folder (the
    /// bundle's <c>Resources</c> on macOS) before any C# runs, so an export
    /// takes the shell's <c>$PWD</c> where it is set (Linux, macOS; not
    /// Windows, where it stays the game's folder); the editor keeps the
    /// process's working directory, which is the shell's.
    /// </summary>
    public static string LaunchDirectory { get; } = FindLaunchDirectory();

    private static string FindLaunchDirectory()
    {
        string? pwd = OS.HasFeature("template") ? System.Environment.GetEnvironmentVariable("PWD") : null;
        return !string.IsNullOrEmpty(pwd) && Path.IsPathFullyQualified(pwd) && Directory.Exists(pwd) ? pwd : Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// T8.4b: a path argument (<c>--savedir</c>, <c>--settings</c>, a script's
    /// <c>route FILE</c>, …) made absolute against <see cref="LaunchDirectory"/>;
    /// Godot's <c>res://</c> and <c>user://</c> paths and absolute ones stay as they are.
    /// </summary>
    public static string ResolveUserPath(string path) =>
        path.Contains("://", StringComparison.Ordinal) || Path.IsPathRooted(path) ? path : Path.GetFullPath(path, LaunchDirectory);

    /// <summary>The IWAD path saved by the file picker, or null.</summary>
    public static string? LoadConfiguredIwad()
    {
        var config = new ConfigFile();
        if (config.Load(SettingsPath) != Error.Ok)
            return null;
        string value = config.GetValue(Section, IwadKey, "").AsString();
        return value.Length > 0 ? value : null;
    }

    /// <summary>Saves <paramref name="path"/> as the configured IWAD (searched before the folders on the next start).</summary>
    public static void SaveConfiguredIwad(string path)
    {
        var config = new ConfigFile();
        config.Load(SettingsPath); // keep other settings; a missing file is fine
        config.SetValue(Section, IwadKey, path);
        Error err = config.Save(SettingsPath);
        if (err != Error.Ok)
            GD.PushWarning($"Could not save {SettingsPath}: {err}");
    }

    /// <summary>T1.1b: where the shareware download puts <c>DOOM1.WAD</c> (<c>user://wads</c>: writable wherever the game is installed).</summary>
    public static string SharewareDirectory => ProjectSettings.GlobalizePath("user://wads");

    private static readonly Lazy<System.Net.Http.HttpClient> _http = new(() =>
    {
        var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IsoDoom", Main.Version));
        return http;
    });

    /// <summary>
    /// T1.1b: the shareware <c>DOOM1.WAD</c> in <see cref="SharewareDirectory"/>,
    /// downloaded and verified unless a verified copy is already there
    /// (<see cref="SharewareDownload"/>); throws <see cref="SharewareDownloadException"/>.
    /// The caller makes it the chosen IWAD.
    /// </summary>
    public static Task<string> DownloadShareware(IProgress<SharewareDownload.Progress>? progress = null, CancellationToken cancel = default) =>
        SharewareDownload.DownloadAsync(_http.Value, SharewareDirectory, progress, cancel: cancel);

    // T8.3: the user arguments, read once (they do not change while the game runs; the scene asks
    // for some every frame, and each OS.GetCmdlineUserArgs marshals the whole array anew).
    private static readonly Lazy<string[]> _userArgs = new(OS.GetCmdlineUserArgs);

    /// <summary>The value of <c>name VALUE</c> or <c>name=VALUE</c> among the user arguments, or null.</summary>
    public static string? GetUserArg(string name)
    {
        string[] args = _userArgs.Value;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == name && i + 1 < args.Length)
                return args[i + 1];
            if (IsNameEquals(args[i], name))
                return args[i][(name.Length + 1)..];
        }
        return null;
    }

    /// <summary>True if <paramref name="name"/> (alone or as <c>name=…</c>) is among the user arguments.</summary>
    public static bool HasUserArg(string name)
    {
        foreach (string a in _userArgs.Value)
        {
            if (a == name || IsNameEquals(a, name))
                return true;
        }
        return false;
    }

    // name=… (without building the string name + "=").
    private static bool IsNameEquals(string arg, string name) =>
        arg.Length > name.Length && arg[name.Length] == '=' && arg.StartsWith(name, StringComparison.Ordinal);
}
