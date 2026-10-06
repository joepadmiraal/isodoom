using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T4.8: a route, a <c>ticcmd</c> sequence (input only) played by vanilla
/// (<c>tools/VanillaRef/routes.sh</c>, as a v1.9 demo) and by the sim, compared tic by tic.
/// <para>
/// A <c>.route</c> file (in <c>Routes/</c>) is text: <c>#</c> starts a
/// comment; header lines <c>iwad synthetic|doom1</c> (required),
/// <c>map ExMy</c> (default <c>E1M1</c>) and <c>skill 1-5</c> (default 3);
/// then one line per <c>ticcmd</c>: <c>FORWARD SIDE TURN BUTTONS [xCOUNT]</c>,
/// <c>forwardmove</c> and <c>sidemove</c> as signed bytes, <c>TURN</c> the
/// demo's signed angleturn byte (<c>angleturn = TURN &lt;&lt; 8</c>: a demo's
/// angleturn is 8 bits wide), <c>xCOUNT</c> repeats the line. The game has no
/// monsters (the demo's <c>nomonsters</c>) and player 1 alone.
/// </para>
/// <para>
/// The reference dump (<c>NAME.vanilla</c>; dump.c) has one line per tic,
/// after the tic: <c>leveltime</c>, the <c>ticcmd</c> vanilla read
/// (<c>forwardmove sidemove angleturn buttons</c>), then player 1's mobj
/// <c>x y z momx momy momz angle</c>, <c>viewz</c>, the <c>P_Random</c>
/// index, and the mobj's <c>state</c> and <c>tics</c>. For the synthetic
/// IWAD it is committed beside the route (generated content); for DOOM1.WAD
/// it is WAD-derived and lives in
/// <see cref="DumpDirEnvVar"/> (default <c>~/.cache/isodoom/vanilla-routes</c>).
/// </para>
/// </summary>
public sealed class VanillaRoute
{
    /// <summary>The directory of DOOM1.WAD's route dumps (routes.sh writes them there).</summary>
    public const string DumpDirEnvVar = "ISODOOM_VANILLA_ROUTES";

    /// <summary>The columns of a dump line.</summary>
    public static readonly string[] Columns =
        { "leveltime", "forwardmove", "sidemove", "angleturn", "buttons", "x", "y", "z", "momx", "momy", "momz", "angle", "viewz", "prndindex", "state", "tics" };

    public string Name { get; }
    public string Path { get; }
    /// <summary><c>synthetic</c> or <c>doom1</c>.</summary>
    public string Iwad { get; }
    public string Map { get; }
    public skill_t Skill { get; }
    public IReadOnlyList<ticcmd_t> Cmds { get; }

    private VanillaRoute(string path, string iwad, string map, skill_t skill, List<ticcmd_t> cmds)
    {
        Name = System.IO.Path.GetFileNameWithoutExtension(path);
        Path = path;
        Iwad = iwad;
        Map = map;
        Skill = skill;
        Cmds = cmds;
    }

    /// <summary>The routes directory (<c>tests/IsoDoom.Tests/Sim/Routes</c>).</summary>
    public static string Dir =>
        System.IO.Path.Combine(TestWads.RepoRoot ?? throw new InvalidOperationException("Repo root not found."),
            "tests", "IsoDoom.Tests", "Sim", "Routes");

    /// <summary>The names of every route in <see cref="Dir"/>, sorted.</summary>
    public static IEnumerable<string> Names() =>
        Directory.GetFiles(Dir, "*.route").Select(p => System.IO.Path.GetFileNameWithoutExtension(p)).Order(StringComparer.Ordinal);

    public static VanillaRoute Load(string name) => Parse(System.IO.Path.Combine(Dir, name + ".route"));

    public static VanillaRoute Parse(string path)
    {
        string? iwad = null;
        string map = "E1M1";
        int skill = 3;
        var cmds = new List<ticcmd_t>();
        int n = 0;
        foreach (string raw in File.ReadLines(path))
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
                    map = f[1].ToUpperInvariant();
                    continue;
                case "skill":
                    skill = int.Parse(f[1], CultureInfo.InvariantCulture);
                    continue;
            }
            int count = 1;
            if (f[^1].StartsWith('x'))
            {
                count = int.Parse(f[^1][1..], CultureInfo.InvariantCulture);
                f = f[..^1];
            }
            if (f.Length != 4)
                throw new FormatException($"{where}: expected FORWARD SIDE TURN BUTTONS [xCOUNT]");
            int[] v = f.Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            if (v[..3].Any(x => x < -128 || x > 127) || v[3] < 0 || v[3] > 255)
                throw new FormatException($"{where}: out of range");
            var cmd = new ticcmd_t
            {
                forwardmove = (sbyte)v[0],
                sidemove = (sbyte)v[1],
                angleturn = (short)(v[2] << 8),
                buttons = (byte)v[3],
            };
            for (int i = 0; i < count; i++)
                cmds.Add(cmd);
        }
        if (iwad is not ("synthetic" or "doom1") || skill < 1 || skill > 5)
            throw new FormatException($"{path}: needs \"iwad synthetic|doom1\" and a skill of 1-5");
        return new VanillaRoute(path, iwad, map, (skill_t)(skill - 1), cmds);
    }

    /// <summary>
    /// The reference dump's lines: the committed one for the synthetic IWAD,
    /// DOOM1.WAD's from <see cref="DumpDirEnvVar"/> (skips the test when DOOM1.WAD or the dump is absent).
    /// </summary>
    public string[] Reference()
    {
        string path;
        if (Iwad == "synthetic")
        {
            path = System.IO.Path.ChangeExtension(Path, ".vanilla");
        }
        else
        {
            TestWads.RequireDoom1();
            string? dir = Environment.GetEnvironmentVariable(DumpDirEnvVar);
            if (string.IsNullOrEmpty(dir))
                dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "isodoom", "vanilla-routes");
            path = System.IO.Path.Combine(dir, Name + ".vanilla");
            if (!File.Exists(path))
                Assert.Skip($"No vanilla dump {path}: run tools/VanillaRef/routes.sh (or set {DumpDirEnvVar}).");
        }
        return File.ReadAllLines(path).Where(l => l.Length > 0).ToArray();
    }

    /// <summary>A new game on the route's map with every tweak off (<see cref="Tweaks.Vanilla"/>), as the demo starts it.</summary>
    public World NewWorld()
    {
        WadArchive wad = Iwad == "synthetic"
            ? new WadArchive(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) })
            : WadArchive.Open(TestWads.RequireDoom1());
        var world = new World(new SpawnSettings(GameMode.shareware, Skill, nomonsters: true), Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(wad, Map));
        return world;
    }

    /// <summary>The sim's state after a tic, as a dump line.</summary>
    public static string Line(World world)
    {
        player_t p = world.players[world.consoleplayer];
        mobj_t mo = p.mo!;
        ticcmd_t c = p.cmd;
        return string.Join(' ', new long[]
        {
            world.leveltime, c.forwardmove, c.sidemove, c.angleturn, c.buttons,
            mo.x, mo.y, mo.z, mo.momx, mo.momy, mo.momz, mo.angle, p.viewz, world.random.prndindex, (long)mo.state, mo.tics,
        }.Select(v => v.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// Plays the route in a new world and fails at the first tic whose state
    /// differs from vanilla's, naming the differing columns.
    /// </summary>
    public void Check()
    {
        string[] expected = Reference();
        World world = NewWorld();
        Assert.True(expected.Length == Cmds.Count,
            $"{Name}: the dump has {expected.Length} tics, the route {Cmds.Count}: rerun tools/VanillaRef/routes.sh.");
        for (int tic = 0; tic < Cmds.Count; tic++)
        {
            world.G_Ticker(Cmds[tic]);
            string actual = Line(world);
            if (actual == expected[tic])
                continue;
            string[] e = expected[tic].Split(' '), a = actual.Split(' ');
            var msg = new StringBuilder();
            if (e[1..5].SequenceEqual(a[1..5]) && e[0] == a[0])
                msg.Append($"{Name}: tic {tic + 1} differs from vanilla");
            else
                msg.Append($"{Name}: tic {tic + 1}'s ticcmd differs from the dump's (stale dump? rerun tools/VanillaRef/routes.sh)");
            for (int i = 0; i < Columns.Length; i++)
            {
                if (i >= e.Length || e[i] != a[i])
                    msg.Append($"\n  {Columns[i]}: vanilla {(i < e.Length ? e[i] : "-")}, sim {a[i]}");
            }
            msg.Append($"\n  vanilla: {expected[tic]}\n  sim:     {actual}");
            msg.Append($"\n  sim overruns so far (not emulated): intercepts {world.interceptoverruns}, spechit {world.spechitoverruns}");
            Assert.Fail(msg.ToString());
        }
    }
}
