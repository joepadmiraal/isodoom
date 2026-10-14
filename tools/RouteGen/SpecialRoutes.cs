using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using IsoDoom.Sim;
using IsoDoom.Wad;

namespace IsoDoom.Tools.RouteGen;

/// <summary>
/// T10.6: routes that work one special where a map has it, started beside
/// it: <c>mapNN-lineK</c> uses (S, manual doors), walks across (W,
/// teleports, exits) or shoots (G) line K of Doom II's MAPNN from in front
/// of it, then waits for what it set going to finish (at most
/// <see cref="MaxWait"/> tics); <c>mapNN-sectorK</c> stands in sector K
/// (damage, lights; beside it for the 30-second door) for <see cref="SectorWait"/> tics
/// (<see cref="SectorDoorWait"/> for special 10). No monsters, skill 3.
/// <c>--specials MAP|all [SPECIAL,...]</c> picks, for each line special Doom
/// II uses and DOOM1.WAD does not (<see cref="Doom2Only"/>), the first line
/// where such a route triggers it, and writes those routes (T10.6:
/// Doom II's acceptance, checked against vanilla with <c>routes.sh</c>).
/// </summary>
public static class SpecialRoutes
{
    private const int FU = 1 << 16;

    /// <summary>Tics waited at most after the trigger for the sectors' movers to stop.</summary>
    public const int MaxWait = 450;

    /// <summary>Tics a sector route stands in its sector (two damage periods and a strobe's cycles).</summary>
    public const int SectorWait = 210;

    /// <summary>Tics for sector special 10 (p_spec.c <c>P_SpawnDoorCloseIn30</c>: 30 s, then the door closes).</summary>
    public const int SectorDoorWait = 35 * 30 + 140;

    /// <summary>The line specials Doom II's maps use and DOOM1.WAD's do not (125 and 126 work for monsters only).</summary>
    public static readonly int[] Doom2Only =
        [3, 4, 13, 15, 19, 30, 37, 38, 39, 42, 45, 47, 49, 52, 59, 60, 61, 67, 68, 71, 73, 74, 77, 83, 87, 89, 94, 99, 100, 102,
            105, 106, 109, 110, 112, 114, 115, 116, 117, 118, 119, 120, 123, 124, 127, 131, 134, 135, 136, 137, 138, 139, 140, 141];

    /// <summary>The sector specials Doom II's maps use and DOOM1.WAD's do not.</summary>
    public static readonly int[] Doom2OnlySectors = [4, 5, 10, 17];

    // p_spec.c P_CrossSpecialLine's specials (the W lines, teleports and exits among them).
    private static readonly HashSet<int> _walk =
    [
        2, 3, 4, 5, 6, 8, 10, 12, 13, 16, 17, 19, 22, 25, 30, 35, 36, 37, 38, 39, 40, 44, 52, 53, 54, 56, 57, 58, 59, 72, 73, 74, 75, 76,
        77, 79, 80, 81, 82, 83, 84, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 100, 104, 105, 106, 107, 108, 109, 110, 119, 120,
        121, 124, 125, 126, 128, 129, 130, 141,
    ];

    // p_spec.c P_ShootSpecialLine's (G lines).
    private static readonly HashSet<int> _shoot = [24, 46, 47];

    // the teleports: crossed from the front only
    private static readonly HashSet<int> _teleports = [39, 97, 125, 126];

    private static readonly Regex _name = new(@"^map(\d\d)-(line|sector)(\d+)$", RegexOptions.CultureInvariant);

    private static readonly Regex _exitName = new(@"^map(\d\d)-walk-exit$", RegexOptions.CultureInvariant);

    /// <summary>Whether <paramref name="name"/> is a walked exit route's name (<c>mapNN-walk-exit</c>).</summary>
    public static bool IsExitName(string name) => _exitName.IsMatch(name);

    /// <summary>
    /// <c>mapNN-walk-exit</c>: from the map's start, no monsters, the player walks to the
    /// normal exit (switch 11 or walk-over 52), through the manual doors on the way, picking
    /// up the keys it can reach while the exit is out of reach; fails when that does not do.
    /// </summary>
    public static (string Header, Action<Steer, Action<string>> Run) MakeExit(string name)
    {
        string map = "MAP" + _exitName.Match(name).Groups[1].Value;
        string header = $"""
# Doom II {map} (T10.6): from the map's start, no monsters: the player walks to the exit,
# through the doors on the way (and the keys they need), and leaves the level, the last
# tic. Written by tools/RouteGen ({name}).
iwad doom2
map {map}
exit normal

""";
        void Run(Steer g, Action<string> log)
        {
            g.TightCorners = true;
            g.Wait(1);
            var keysTried = new HashSet<mobj_t>();
            for (int round = 0; round < 8; round++)
            {
                foreach (line_t l in g.World.lines.Where(l => l.special is 11 or 52))
                {
                    double x1 = l.v1.X / (double)FU, y1 = l.v1.Y / (double)FU, x2 = l.v2.X / (double)FU, y2 = l.v2.Y / (double)FU;
                    double len = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
                    double mx = (x1 + x2) / 2, my = (y1 + y2) / 2, nx = (y2 - y1) / len, ny = -(x2 - x1) / len;
                    foreach (double d in new[] { 24.0, 32, 40 })
                    {
                        double sx = mx + nx * d, sy = my + ny * d;
                        if (!g.Fits(sx, sy))
                            continue;
                        try
                        {
                            g.GoToDoors(sx, sy, 4);
                        }
                        catch (InvalidOperationException e)
                        {
                            log($"exit line {l.Index} from ({sx:F0}, {sy:F0}): {e.Message.Split(';')[0]}");
                            break;
                        }
                        log($"at exit line {l.Index}");
                        if (l.special == 52)
                            g.WalkExit(mx - nx * 32, my - ny * 32, 25, 60);
                        else
                            g.Exit(mx, my);
                        return;
                    }
                }
                // the keys within reach
                bool got = false;
                foreach (mobj_t k in g.World.Mobjs().Where(m => m.type is >= mobjtype_t.MT_MISC4 and <= mobjtype_t.MT_MISC9).ToList())
                {
                    if (!keysTried.Add(k))
                        continue;
                    try
                    {
                        g.GoToDoors(k.x / (double)FU, k.y / (double)FU, 4);
                        log($"key {k.type}");
                        got = true;
                        break;
                    }
                    catch (InvalidOperationException e)
                    {
                        log($"key {k.type} at ({k.x >> 16}, {k.y >> 16}): {e.Message.Split(';')[0]}");
                    }
                }
                if (!got)
                    break;
            }
            throw new InvalidOperationException("no way to the exit");
        }
        return (header, Run);
    }

    /// <summary>Whether <paramref name="name"/> is a special route's name (<c>mapNN-lineK</c>, <c>mapNN-sectorK</c>).</summary>
    public static bool IsName(string name) => _name.IsMatch(name);

    /// <summary>The route <paramref name="name"/>: its header, start and steering, or an exception when it cannot be made.</summary>
    public static (string Header, (int X, int Y, int Angle) Start, Action<Steer, Action<string>> Run) Make(Func<WadArchive> wad, string name)
    {
        Match mt = _name.Match(name);
        string map = "MAP" + mt.Groups[1].Value;
        int index = int.Parse(mt.Groups[3].Value, CultureInfo.InvariantCulture);
        var probe = new Steer(wad(), map);
        return mt.Groups[2].Value == "line" ? LineRoute(wad, probe, map, name, index)
            : SectorRoute(probe, map, name, index, IsoDoom.Map.Level.Load(wad(), map).Sectors[index].Special);
    }

    private static (string, (int, int, int), Action<Steer, Action<string>>) LineRoute(Func<WadArchive> wad, Steer probe, string map, string name, int index)
    {
        line_t l = probe.World.lines[index];
        int special = l.special;
        if (special == 0)
            throw new InvalidOperationException($"{map} line {index} has no special");
        double x1 = l.v1.X / (double)FU, y1 = l.v1.Y / (double)FU, x2 = l.v2.X / (double)FU, y2 = l.v2.Y / (double)FU;
        double len = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
        double mx = (x1 + x2) / 2, my = (y1 + y2) / 2;
        // the front side is on the right of v1 -> v2
        double nx = (y2 - y1) / len, ny = -(x2 - x1) / len;
        bool walk = _walk.Contains(special), shoot = _shoot.Contains(special);
        // W lines but teleports trigger from either side; the rest from the front only
        int[] sides = walk && !_teleports.Contains(special) ? [1, -1] : [1];
        double[] dists = walk ? [32, 48, 64, 24, 96] : shoot ? [64, 96, 128, 48] : [32, 40, 24, 48, 56];
        double[] along = [0, -0.25, 0.25, -0.4, 0.4];
        foreach (int side in sides)
        {
            foreach (double d in dists)
            {
                foreach (double a in along)
                {
                    double sx = mx + (x2 - x1) * a + nx * d * side, sy = my + (y2 - y1) * a + ny * d * side;
                    // on that side of the line: in its sector to walk across, or (a switch in an
                    // alcove) in the sector before it to use or shoot it
                    if (!probe.Fits(sx, sy) || ((sx - x1) * nx + (sy - y1) * ny) * side <= 0
                        || (walk && probe.World.R_PointInSubsector((int)(sx * FU), (int)(sy * FU)).sector != (side > 0 ? l.frontsector : l.backsector)))
                        continue;
                    double tx = mx + (x2 - x1) * a, ty = my + (y2 - y1) * a; // the point of the line faced
                    int angle = (int)Math.Round(Math.Atan2(ty - sy, tx - sx) * 180 / Math.PI);
                    angle = (angle + 360) % 360;
                    string kind = walk ? "walks across" : shoot ? "shoots" : "uses";
                    string exit = special is 52 or 124 ? $"exit {(special == 124 ? "secret" : "normal")}\n" : "";
                    string header = $"""
# Doom II {map} (T10.6): started beside line {index} (special {special}, tag {l.tag}), on its
# {(side > 0 ? "front" : "back")}, the player {kind} it, then waits for the movers it set going
# (at most {MaxWait} tics). Written by tools/RouteGen ({name}).
iwad doom2
map {map}
{exit}
""";
                    var start = ((int)Math.Round(sx), (int)Math.Round(sy), angle);
                    void Run(Steer g, Action<string> log)
                    {
                        line_t line = g.World.lines[index];
                        g.Tic(0, 0, 0, 0); // (G_PlayerReborn's usedown: the first tic's use does nothing)
                        string before = State(g, line);
                        if (walk)
                        {
                            double ex = tx - nx * 48 * side, ey = ty - ny * 48 * side;
                            if (exit.Length > 0)
                            {
                                g.WalkExit(ex, ey, 25, 60);
                                log("left the level");
                                return;
                            }
                            for (int t = 0; t < 40 && State(g, line) == before; t++)
                                g.Tic(25, 0, g.TurnTo(ex, ey), 0);
                            g.Tic(0, 0, 0, 0);
                        }
                        else if (shoot)
                        {
                            for (int t = 0; t < 30 && State(g, line) == before; t++)
                                g.Fire(tx, ty);
                            g.Tic(0, 0, 0, 0);
                        }
                        else
                        {
                            g.Tic(0, 0, g.TurnTo(tx, ty), 2); // use (BT_USE), as Steer.Use, the state taken before the release
                        }
                        string after = State(g, line);
                        if (!walk && !shoot)
                            g.Tic(0, 0, 0, 0);
                        log($"before: {before}");
                        log($"after:  {after}");
                        if (after == before)
                            throw new InvalidOperationException($"line {index} (special {special}): nothing happened");
                        for (int t = 0; t < MaxWait && g.World.sectors.Any(s => s.specialdata != null); t++)
                            g.Tic(0, 0, 0, 0);
                        g.Wait(10);
                        log($"done:   {State(g, line)}");
                    }
                    // the first start from which it works
                    try
                    {
                        Run(new Steer(wad(), map, start), _ => { });
                    }
                    catch (InvalidOperationException e)
                    {
                        Console.Error.WriteLine($"    {name} from ({start.Item1}, {start.Item2}): {e.Message}");
                        continue;
                    }
                    return (header, start, Run);
                }
            }
        }
        throw new InvalidOperationException($"{map} line {index} (special {special}): no place to start beside it from which it works");
    }

    private static (string, (int, int, int), Action<Steer, Action<string>>) SectorRoute(Steer probe, string map, string name, int index, int special)
    {
        sector_t s = probe.World.sectors[index]; // (P_SpawnSpecials clears some specials: the map's)
        if (special == 0)
            throw new InvalidOperationException($"{map} sector {index} has no special");
        var lines = probe.World.lines.Where(l => l.frontsector == s || l.backsector == s).ToList();
        int x0 = lines.Min(l => Math.Min(l.v1.X, l.v2.X)) >> 16, x1 = lines.Max(l => Math.Max(l.v1.X, l.v2.X)) >> 16;
        int y0 = lines.Min(l => Math.Min(l.v1.Y, l.v2.Y)) >> 16, y1 = lines.Max(l => Math.Max(l.v1.Y, l.v2.Y)) >> 16;
        int cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
        // the fitting point of the sector nearest its box's centre, on an 8-unit grid; for
        // special 10, outside it: a door closing onto the player goes back up to the
        // topheight P_SpawnDoorCloseIn30 never sets (Z_Malloc's garbage in vanilla)
        bool outside = special == 10;
        int margin = outside ? 96 : 0;
        (int X, int Y)? best = null;
        for (int y = y0 - margin; y <= y1 + margin; y += 8)
        {
            for (int x = x0 - margin; x <= x1 + margin; x += 8)
            {
                if ((probe.World.R_PointInSubsector(x * FU, y * FU).sector == s) != outside && probe.Fits(x, y)
                    && (best is not { } b || (long)(x - cx) * (x - cx) + (long)(y - cy) * (y - cy) < (long)(b.X - cx) * (b.X - cx) + (long)(b.Y - cy) * (b.Y - cy)))
                    best = (x, y);
            }
        }
        if (best is not { } p)
            throw new InvalidOperationException($"{map} sector {index}: no place to stand in it");
        int wait = special == 10 ? SectorDoorWait : SectorWait;
        string header = $"""
# Doom II {map} (T10.6): started {(outside ? "beside" : "in")} sector {index} (special {special}), the player stands
# there for {wait} tics. Written by tools/RouteGen ({name}).
iwad doom2
map {map}

""";
        void Run(Steer g, Action<string> log)
        {
            g.Wait(wait);
            log($"health {g.World.players[0].health}, sector {index}: light {g.World.sectors[index].lightlevel}, floor {g.World.sectors[index].floorheight >> 16} ceiling {g.World.sectors[index].ceilingheight >> 16}");
        }
        return (header, (p.X, p.Y, 0), Run);
    }

    // What a trigger changes: the line's special and textures, the movers, heights, lights and
    // flats of the sectors it acts on (its tag's, or the sector behind a manual door), a
    // teleport (the player's reactiontime), the tic's messages and the level's exit.
    private static string State(Steer g, line_t line)
    {
        World w = g.World;
        long h = 17;
        foreach (sector_t s in w.sectors.Where(s => line.tag != 0 ? s.tag == line.tag : s == line.backsector))
        {
            h = (h * 31) + s.floorheight;
            h = (h * 31) + s.ceilingheight;
            h = (h * 31) + s.lightlevel;
            h = (h * 31) + StringComparer.Ordinal.GetHashCode(s.floorpic);
            h = (h * 31) + (s.specialdata == null ? 0 : 1);
        }
        side_t side = w.sides[line.sidenum[0]];
        string messages = string.Join("|", w.events.Where(e => e.type == simevent_t.se_message).Select(e => e.message));
        // a teleport: the player's fog (P_Teleport's at its new place) and its move
        int teleported = w.players[0].mo is { } mo && mo.reactiontime > 0 ? 1 : 0;
        return $"special {line.special} textures {side.toptexture}/{side.midtexture}/{side.bottomtexture} sectors {h:x} teleport {teleported} msg {messages} {w.gameaction}";
    }

    /// <summary>
    /// <c>--specials MAP|all [SPECIAL,...]</c>: for each special (default <see cref="Doom2Only"/>
    /// and <see cref="Doom2OnlySectors"/> as <c>sN</c>), tries its lines (sectors) on the map
    /// (or every Doom II map) in order and prints the first route name that works.
    /// </summary>
    public static int Find(Func<WadArchive> wad, string which, string? specials)
    {
        string[] maps = which == "all" ? [.. Enumerable.Range(1, 32).Select(i => $"MAP{i:00}")] : [which.ToUpperInvariant()];
        IEnumerable<string> wanted = specials?.Split(',') ?? Doom2Only.Select(i => i.ToString(CultureInfo.InvariantCulture)).Concat(Doom2OnlySectors.Select(i => $"s{i}"));
        foreach (string sp in wanted)
        {
            bool sector = sp.StartsWith('s');
            int number = int.Parse(sector ? sp[1..] : sp, CultureInfo.InvariantCulture);
            string? found = null;
            int tries = 0;
            foreach (string map in maps)
            {
                var probe = new Steer(wad(), map);
                IEnumerable<int> indices = sector
                    ? IsoDoom.Map.Level.Load(wad(), map).Sectors.Select((s, i) => (s, i)).Where(t => t.s.Special == number).Select(t => t.i)
                    : probe.World.lines.Where(l => l.special == number).Select(l => l.Index);
                foreach (int i in indices)
                {
                    if (tries++ >= 12)
                        break;
                    string name = $"{map.ToLowerInvariant()}-{(sector ? "sector" : "line")}{i}";
                    try
                    {
                        (string _, (int X, int Y, int Angle) start, Action<Steer, Action<string>> run) = Make(wad, name);
                        var g = new Steer(wad(), map, start);
                        run(g, _ => { });
                        found = name;
                        break;
                    }
                    catch (InvalidOperationException e)
                    {
                        Console.Error.WriteLine($"  {name}: {e.Message}");
                    }
                }
                if (found != null || tries >= 12)
                    break;
            }
            Console.WriteLine($"{sp} {found ?? "-"}");
        }
        return 0;
    }
}
