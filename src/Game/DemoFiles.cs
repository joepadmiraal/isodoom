using System;
using System.IO;
using IsoDoom.Wad;

namespace IsoDoom.Game;

/// <summary>
/// T7.14: where <c>-playdemo NAME</c> finds its demo, as Chocolate Doom's
/// d_main.c <c>D_DoomMain</c> does: the file <c>NAME</c> when it ends with
/// <c>.lmp</c> ("with Vanilla you have to specify the file without
/// extension, but make that optional"), else the file <c>NAME.lmp</c>; else
/// ("still continue trying to play the demo in the same way as Vanilla
/// Doom. This makes tricks like '-playdemo demo1' possible") the WAD's lump
/// <c>NAME</c>; and, not Chocolate Doom's, a file <c>NAME</c> with another
/// extension last (SPEC §12 T7.14). Plain C# (the tests link it).
/// </summary>
public static class DemoFiles
{
    /// <summary>
    /// The demo <paramref name="name"/>: its bytes and what it was (the file's
    /// full path, or <c>lump NAME</c>). A relative file name is resolved by
    /// <paramref name="resolve"/> (the game's: against <c>$PWD</c>,
    /// <c>WadLocator.ResolveUserPath</c>). Neither file nor lump is a
    /// <see cref="FileNotFoundException"/> with the player's message.
    /// </summary>
    public static (byte[] Data, string Source) Find(string name, WadArchive wad, Func<string, string>? resolve = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(wad);
        resolve ??= Path.GetFullPath;
        string lmp = name.EndsWith(".lmp", StringComparison.OrdinalIgnoreCase) ? name : name + ".lmp";
        if (File.Exists(resolve(lmp)))
            return (File.ReadAllBytes(resolve(lmp)), Path.GetFullPath(resolve(lmp)));
        string lump = name.ToUpperInvariant();
        if (lump.Length <= 8 && wad.W_CheckNumForName(lump) >= 0)
            return (wad.W_CacheLumpName(lump).ToArray(), $"lump {lump}");
        if (File.Exists(resolve(name)))
            return (File.ReadAllBytes(resolve(name)), Path.GetFullPath(resolve(name)));
        throw new FileNotFoundException($"no file {lmp}\nand no lump {lump}", name);
    }
}
