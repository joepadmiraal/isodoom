using System;
using System.Collections.Generic;
using System.IO;
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
        CurrentDirectory = UserWorkingDirectory(),
        GameDirectories = GameDirectories(),
        ConfiguredIwad = ChosenIwad ?? LoadConfiguredIwad(),
    };

    /// <summary>T7.2: the IWAD chosen in the IWAD menu this run (taken as the configured one, even when the settings can't be saved).</summary>
    public static string? ChosenIwad { get; set; }

    /// <summary>
    /// The game folder: the project folder in editor runs, the executable's
    /// folder in exports; each followed by its <c>wads/</c> subfolder.
    /// </summary>
    public static IReadOnlyList<string> GameDirectories()
    {
        string? root = OS.HasFeature("editor")
            ? ProjectSettings.GlobalizePath("res://")
            : Path.GetDirectoryName(OS.GetExecutablePath());
        if (string.IsNullOrEmpty(root))
            return Array.Empty<string>();
        root = root.TrimEnd('/', '\\');
        return new[] { root, Path.Combine(root, "wads") };
    }

    /// <summary>
    /// The shell's working directory. Exported builds run with the executable's
    /// folder as the process working directory, so prefer <c>$PWD</c>.
    /// </summary>
    private static string UserWorkingDirectory()
    {
        string? pwd = System.Environment.GetEnvironmentVariable("PWD");
        return !string.IsNullOrEmpty(pwd) && Directory.Exists(pwd) ? pwd : Directory.GetCurrentDirectory();
    }

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

    /// <summary>The value of <c>name VALUE</c> or <c>name=VALUE</c> among the user arguments, or null.</summary>
    public static string? GetUserArg(string name)
    {
        string[] args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == name && i + 1 < args.Length)
                return args[i + 1];
            if (args[i].StartsWith(name + "=", StringComparison.Ordinal))
                return args[i][(name.Length + 1)..];
        }
        return null;
    }

    /// <summary>True if <paramref name="name"/> (alone or as <c>name=…</c>) is among the user arguments.</summary>
    public static bool HasUserArg(string name) =>
        Array.Exists(OS.GetCmdlineUserArgs(), a => a == name || a.StartsWith(name + "=", StringComparison.Ordinal));
}
