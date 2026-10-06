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
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T4.8: a route, a <c>ticcmd</c> sequence (input only) played by vanilla
/// (<c>tools/VanillaRef/routes.sh</c>, as a v1.9 demo) and by the sim, compared tic by tic.
/// <para>
/// A <c>.route</c> file (in <c>Routes/</c>) is text: <c>#</c> starts a
/// comment; header lines <c>iwad synthetic|doom1|testmap</c> (required),
/// <c>map ExMy</c> (default <c>E1M1</c>; for <c>testmap</c> the name of a
/// <see cref="RouteTestMaps"/> map, required, T4.8a), <c>skill 1-5</c> (default 3) and
/// <c>start X Y ANGLE</c> (T5.6: player 1 starts at map point X, Y facing ANGLE
/// degrees instead of at its map start: <see cref="RouteStart.Place"/>);
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
/// index, and the mobj's <c>state</c> and <c>tics</c>, then (T5.3) the
/// <c>sectors</c> whose floor or ceiling height differs from the map's
/// <c>SECTORS</c> lump, as <c>SECTOR:FLOOR:CEILING</c> (fixed_t) joined by
/// commas, or <c>-</c> for none (doors, lifts), then (T5.4) the
/// <c>textures</c> of sidedefs that differ from the map's <c>SIDEDEFS</c>
/// lump, as <c>SIDE:PART:NAME</c> (<c>PART</c> <c>t</c>, <c>m</c> or
/// <c>b</c>) joined by commas, or <c>-</c> for none (switches), then (T5.6) the
/// teleport <c>fogs</c> (<c>MT_TFOG</c> mobjs) in thinker order as
/// <c>X:Y:Z:STATE</c> joined by commas, or <c>-</c> for none. For the synthetic
/// IWAD and the test maps it is committed beside the route (generated content); for DOOM1.WAD
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
        { "leveltime", "forwardmove", "sidemove", "angleturn", "buttons", "x", "y", "z", "momx", "momy", "momz", "angle", "viewz", "prndindex", "state", "tics", "sectors", "textures", "fogs" };

    public string Name { get; }
    public string Path { get; }
    /// <summary><c>synthetic</c>, <c>doom1</c> or <c>testmap</c>.</summary>
    public string Iwad { get; }
    public string Map { get; }
    public skill_t Skill { get; }
    /// <summary>The <c>start</c> header (map units, degrees), or null for the map's player 1 start (T5.6).</summary>
    public (int X, int Y, int Angle)? Start { get; }
    public IReadOnlyList<ticcmd_t> Cmds { get; }

    private VanillaRoute(string path, string iwad, string map, skill_t skill, (int, int, int)? start, List<ticcmd_t> cmds)
    {
        Start = start;
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
        string? map = null;
        int skill = 3;
        (int, int, int)? start = null;
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
                    map = f[1];
                    continue;
                case "skill":
                    skill = int.Parse(f[1], CultureInfo.InvariantCulture);
                    continue;
                case "start":
                    if (f.Length != 4)
                        throw new FormatException($"{where}: expected start X Y ANGLE");
                    start = (int.Parse(f[1], CultureInfo.InvariantCulture), int.Parse(f[2], CultureInfo.InvariantCulture), int.Parse(f[3], CultureInfo.InvariantCulture));
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
        if (iwad is not ("synthetic" or "doom1" or "testmap") || skill < 1 || skill > 5)
            throw new FormatException($"{path}: needs \"iwad synthetic|doom1|testmap\" and a skill of 1-5");
        if (iwad == "testmap")
        {
            if (map is null || !RouteTestMaps.Maps.ContainsKey(map))
                throw new FormatException($"{path}: \"iwad testmap\" needs \"map NAME\" of RouteTestMaps.Maps");
        }
        else
        {
            map = (map ?? "E1M1").ToUpperInvariant();
        }
        return new VanillaRoute(path, iwad, map, (skill_t)(skill - 1), start, cmds);
    }

    /// <summary>
    /// The reference dump's lines: the committed one for the synthetic IWAD,
    /// DOOM1.WAD's from <see cref="DumpDirEnvVar"/> (skips the test when DOOM1.WAD or the dump is absent).
    /// </summary>
    public string[] Reference()
    {
        string path;
        if (Iwad != "doom1")
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
        WadArchive wad = Iwad switch
        {
            "synthetic" => new WadArchive(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) }),
            "testmap" => new WadArchive(new[] { WadFile.FromBytes(RouteTestMaps.Get(Map).Build(), Map + ".wad") }),
            _ => WadArchive.Open(TestWads.RequireDoom1()),
        };
        var world = new World(new SpawnSettings(GameMode.shareware, Skill, nomonsters: true), Tweaks.Vanilla)
        {
            textures = wad.W_CheckNumForName("TEXTURE1") >= 0 ? Textures.R_InitTextures(wad) : null,
        };
        world.G_DoLoadLevel(Level.Load(wad, Iwad == "testmap" ? "E1M1" : Map));
        if (Start is { } s)
            RouteStart.Place(world, s.X, s.Y, s.Angle);
        return world;
    }

    /// <summary>
    /// The sim's state after a tic, as a dump line; <paramref name="mapHeights"/>
    /// are the sectors' floor and ceiling heights as the map has them
    /// (<see cref="MapHeights"/>), <paramref name="mapTextures"/> the sidedefs'
    /// textures (<see cref="MapTextures"/>).
    /// </summary>
    public static string Line(World world, (int Floor, int Ceiling)[] mapHeights, string[] mapTextures)
    {
        player_t p = world.players[world.consoleplayer];
        mobj_t mo = p.mo!;
        ticcmd_t c = p.cmd;
        string fields = string.Join(' ', new long[]
        {
            world.leveltime, c.forwardmove, c.sidemove, c.angleturn, c.buttons,
            mo.x, mo.y, mo.z, mo.momx, mo.momy, mo.momz, mo.angle, p.viewz, world.random.prndindex, (long)mo.state, mo.tics,
        }.Select(v => v.ToString(CultureInfo.InvariantCulture)));
        var moved = new List<string>();
        for (int i = 0; i < world.sectors.Length; i++)
        {
            sector_t sec = world.sectors[i];
            if (sec.floorheight != mapHeights[i].Floor || sec.ceilingheight != mapHeights[i].Ceiling)
                moved.Add(string.Create(CultureInfo.InvariantCulture, $"{i}:{sec.floorheight}:{sec.ceilingheight}"));
        }
        string[] textures = MapTextures(world);
        var changed = new List<string>();
        for (int i = 0; i < textures.Length; i++)
        {
            if (!string.Equals(textures[i], mapTextures[i], StringComparison.OrdinalIgnoreCase))
                changed.Add(string.Create(CultureInfo.InvariantCulture, $"{i / 3}:{"tmb"[i % 3]}:{textures[i].ToUpperInvariant()}"));
        }
        var fogs = new List<string>();
        foreach (mobj_t m in world.Mobjs())
        {
            if (m.type == mobjtype_t.MT_TFOG)
                fogs.Add(string.Create(CultureInfo.InvariantCulture, $"{m.x}:{m.y}:{m.z}:{(int)m.state}"));
        }
        return fields + " " + (moved.Count == 0 ? "-" : string.Join(',', moved)) + " " + (changed.Count == 0 ? "-" : string.Join(',', changed))
            + " " + (fogs.Count == 0 ? "-" : string.Join(',', fogs));
    }

    /// <summary>The sidedefs' textures, top, middle and bottom of each in sidedef order (before the first tic: the map's).</summary>
    public static string[] MapTextures(World world) =>
        world.sides.SelectMany(s => new[] { s.toptexture, s.midtexture, s.bottomtexture }).ToArray();

    /// <summary>The sectors' floor and ceiling heights (fixed_t) of a world before its first tic: the map's.</summary>
    public static (int Floor, int Ceiling)[] MapHeights(World world) =>
        world.sectors.Select(s => (s.floorheight, s.ceilingheight)).ToArray();

    /// <summary>
    /// Plays the route in a new world and fails at the first tic whose state
    /// differs from vanilla's, naming the differing columns.
    /// </summary>
    public void Check()
    {
        string[] expected = Reference();
        World world = NewWorld();
        (int, int)[] mapHeights = MapHeights(world);
        string[] mapTextures = MapTextures(world);
        Assert.True(expected.Length == Cmds.Count,
            $"{Name}: the dump has {expected.Length} tics, the route {Cmds.Count}: rerun tools/VanillaRef/routes.sh.");
        for (int tic = 0; tic < Cmds.Count; tic++)
        {
            world.G_Ticker(Cmds[tic]);
            string actual = Line(world, mapHeights, mapTextures);
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
