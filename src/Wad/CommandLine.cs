using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace IsoDoom.Wad;

/// <summary>
/// The game's command line (m_argv.c). <see cref="myargv"/>[0] is the program,
/// as in C; the Godot glue passes Godot's user arguments (those after
/// <c>--</c>) behind it.
/// </summary>
public sealed class CommandLine
{
    [SuppressMessage("Style", "IDE1006", Justification = "Vanilla name (m_argv.c)")]
    public string[] myargv { get; }
    [SuppressMessage("Style", "IDE1006", Justification = "Vanilla name (m_argv.c)")]
    public int myargc => myargv.Length;

    public CommandLine(string program, IEnumerable<string> args)
    {
        var all = new List<string> { program };
        all.AddRange(args);
        myargv = [.. all];
    }

    /// <summary>
    /// The index of <paramref name="check"/> (case-insensitive) when it is
    /// followed by at least <paramref name="num_args"/> arguments, else 0
    /// (m_argv.c: <c>M_CheckParmWithArgs</c>).
    /// </summary>
    [SuppressMessage("Style", "IDE1006", Justification = "Vanilla name (m_argv.c)")]
    public int M_CheckParmWithArgs(string check, int num_args)
    {
        for (int i = 1; i < myargc - num_args; i++)
        {
            if (string.Equals(check, myargv[i], StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
    }

    /// <summary>The index of <paramref name="check"/> (case-insensitive), or 0 (m_argv.c: <c>M_CheckParm</c>).</summary>
    [SuppressMessage("Style", "IDE1006", Justification = "Vanilla name (m_argv.c)")]
    public int M_CheckParm(string check) => M_CheckParmWithArgs(check, 0);

    /// <summary>
    /// The argument after the first of <paramref name="names"/> present, or null.
    /// Not in vanilla: lets <c>--iwad</c> stand for <c>-iwad</c>.
    /// </summary>
    public string? GetParmValue(params string[] names)
    {
        foreach (string name in names)
        {
            int p = M_CheckParmWithArgs(name, 1);
            if (p > 0)
                return myargv[p + 1];
        }
        return null;
    }

    /// <summary>
    /// The arguments after <paramref name="check"/> up to the next one that
    /// starts with <c>-</c>, as w_main.c's <c>W_ParseCommandLine</c> reads
    /// <c>-file</c>. Empty when <paramref name="check"/> is absent.
    /// </summary>
    public IReadOnlyList<string> GetParmList(string check)
    {
        var list = new List<string>();
        int p = M_CheckParmWithArgs(check, 1);
        if (p == 0)
            return list;
        while (++p != myargc && !myargv[p].StartsWith('-'))
            list.Add(myargv[p]);
        return list;
    }
}
