using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace IsoDoom.Game;

/// <summary>
/// Finds the IWAD for the WAD viewer (T1.6). A stopgap until T1.1a's full
/// search (Steam/GOG locations, file picker). Order:
/// <list type="number">
/// <item><c>--iwad PATH</c> or <c>--iwad=PATH</c> among the user arguments
/// (after <c>--</c> on the Godot command line);</item>
/// <item>the <c>ISODOOM_IWAD</c> environment variable;</item>
/// <item>the <c>ISODOOM_DOOM1_WAD</c> environment variable (the one the tests use);</item>
/// <item>an IWAD with a standard name in <c>wads/</c> of the project folder
/// (editor runs) or next to the executable (exports): <c>DOOM1.WAD</c>,
/// <c>DOOM.WAD</c>, <c>DOOMU.WAD</c>, <c>DOOM2.WAD</c>, <c>TNT.WAD</c>,
/// <c>PLUTONIA.WAD</c>, upper or lower case.</item>
/// </list>
/// An explicitly given path (argument or variable) that does not exist is
/// reported and not replaced by a fallback.
/// </summary>
public static class WadLocator
{
    private static readonly string[] IwadNames = { "DOOM1.WAD", "DOOM.WAD", "DOOMU.WAD", "DOOM2.WAD", "TNT.WAD", "PLUTONIA.WAD" };

    /// <summary>The IWAD path, or null with <paramref name="error"/> saying what was tried.</summary>
    public static string? Find(out string error)
    {
        string? explicitPath = GetUserArg("--iwad")
            ?? NonEmpty(System.Environment.GetEnvironmentVariable("ISODOOM_IWAD"))
            ?? NonEmpty(System.Environment.GetEnvironmentVariable("ISODOOM_DOOM1_WAD"));
        if (explicitPath is not null)
        {
            // Exported builds run with the executable's folder as the working
            // directory, so resolve a relative path against the shell's.
            string? pwd = System.Environment.GetEnvironmentVariable("PWD");
            if (!Path.IsPathRooted(explicitPath) && !string.IsNullOrEmpty(pwd))
                explicitPath = Path.Combine(pwd, explicitPath);
            error = File.Exists(explicitPath) ? "" : $"IWAD not found: {explicitPath}";
            return File.Exists(explicitPath) ? explicitPath : null;
        }

        var dirs = new List<string>();
        if (OS.HasFeature("editor"))
            dirs.Add(Path.Combine(ProjectSettings.GlobalizePath("res://"), "wads"));
        string? exeDir = Path.GetDirectoryName(OS.GetExecutablePath());
        if (!OS.HasFeature("editor") && exeDir is not null)
            dirs.Add(Path.Combine(exeDir, "wads"));

        foreach (string dir in dirs)
        {
            foreach (string name in IwadNames)
            {
                foreach (string candidate in new[] { name, name.ToLowerInvariant() })
                {
                    string path = Path.Combine(dir, candidate);
                    if (File.Exists(path))
                    {
                        error = "";
                        return path;
                    }
                }
            }
        }

        error = $"No IWAD found. Put DOOM1.WAD in {string.Join(" or ", dirs)}, set ISODOOM_IWAD, or pass -- --iwad PATH.";
        return null;
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

    private static string? NonEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
