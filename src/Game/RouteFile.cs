using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using IsoDoom.Sim;

namespace IsoDoom.Game;

/// <summary>
/// A <c>.route</c> file (T4.8; the format is described at the tests'
/// <c>VanillaRoute</c>): a header (<c>iwad</c>, <c>map</c>, <c>skill</c>,
/// <c>start X Y ANGLE</c>, <c>exit normal|secret</c>) and one vanilla
/// <c>ticcmd</c> per tic (<c>FORWARD SIDE TURN BUTTONS [xCOUNT]</c>, a demo's
/// relative turn byte). Plain C#: the tests parse their routes with it, and
/// the level script's <c>route FILE</c> (T5.10) plays one in the level scene.
/// </summary>
public sealed class RouteFile
{
    /// <summary><c>synthetic</c>, <c>doom1</c> or <c>testmap</c> (not checked here).</summary>
    public string? Iwad { get; private init; }
    /// <summary>The <c>map</c> header as written, or null.</summary>
    public string? Map { get; private init; }
    /// <summary>The <c>skill</c> header, 1-5 (default 3).</summary>
    public int Skill { get; private init; } = 3;
    /// <summary>The <c>start</c> header (map units, degrees), or null for the map's player 1 start (T5.6).</summary>
    public (int X, int Y, int Angle)? Start { get; private init; }
    /// <summary>The <c>exit</c> header (T5.9): 1 for <c>normal</c>, 2 for <c>secret</c>, 0 without one.</summary>
    public int Exit { get; private init; }
    public IReadOnlyList<ticcmd_t> Cmds { get; private init; } = Array.Empty<ticcmd_t>();

    public static RouteFile Parse(string path) => Parse(File.ReadLines(path), path);

    /// <summary>Parses the route's lines; <paramref name="path"/> names it in errors (<see cref="FormatException"/>).</summary>
    public static RouteFile Parse(IEnumerable<string> lines, string path)
    {
        string? iwad = null, map = null;
        int skill = 3, exit = 0;
        (int, int, int)? start = null;
        var cmds = new List<ticcmd_t>();
        int n = 0;
        foreach (string raw in lines)
        {
            n++;
            int hash = raw.IndexOf('#');
            string[] f = (hash >= 0 ? raw[..hash] : raw).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length == 0)
                continue;
            string where = $"{path}:{n}";
            switch (f[0])
            {
                case "iwad":
                    iwad = f[1];
                    continue;
                case "map":
                    map = f[1];
                    continue;
                case "skill":
                    skill = Int(f[1]);
                    continue;
                case "exit":
                    exit = f.Length == 2 ? f[1] switch { "normal" => 1, "secret" => 2, _ => 0 } : 0;
                    if (exit == 0)
                        throw new FormatException($"{where}: expected exit normal|secret");
                    continue;
                case "start":
                    if (f.Length != 4)
                        throw new FormatException($"{where}: expected start X Y ANGLE");
                    start = (Int(f[1]), Int(f[2]), Int(f[3]));
                    continue;
            }
            int count = 1;
            if (f[^1].StartsWith('x'))
            {
                count = Int(f[^1][1..]);
                f = f[..^1];
            }
            if (f.Length != 4)
                throw new FormatException($"{where}: expected FORWARD SIDE TURN BUTTONS [xCOUNT]");
            int[] v = { Int(f[0]), Int(f[1]), Int(f[2]), Int(f[3]) };
            for (int i = 0; i < 3; i++)
            {
                if (v[i] < -128 || v[i] > 127)
                    throw new FormatException($"{where}: out of range");
            }
            if (v[3] < 0 || v[3] > 255)
                throw new FormatException($"{where}: out of range");
            var cmd = new ticcmd_t
            {
                forwardmove = (sbyte)v[0],
                sidemove = (sbyte)v[1],
                angleturn = (short)(v[2] << 8), // a demo's angleturn byte
                buttons = (byte)v[3],
            };
            for (int i = 0; i < count; i++)
                cmds.Add(cmd);
        }
        if (skill < 1 || skill > 5)
            throw new FormatException($"{path}: skill {skill} (1-5)");
        return new RouteFile { Iwad = iwad, Map = map, Skill = skill, Start = start, Exit = exit, Cmds = cmds };
    }

    private static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);
}
