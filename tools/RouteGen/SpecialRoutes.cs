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

    // (M9: doom-exmy-… on The Ultimate Doom's IWAD)
    // (P10: SET-mapNN-… or SET-exmy-… on a PWAD set of docs/PWAD-VALIDATION.md over its IWAD, PwadSet)
    private const string MapPart = @"(?:(?:(?<iwad>tnt|plutonia)-)?map(?<map>\d\d)|(?<iwad>doom)-(?<map>e[1-4]m[1-9])"
        + @"|(?<pwad>scythe|av|mm|d2twid|requiem|hr)-map(?<map>\d\d)|(?<pwad>dtwid|neis)-(?<map>e[1-4]m[0-9]))";

    private static readonly Regex _name = new($@"^{MapPart}-(?<kind>line|sector)(?<index>\d+)$", RegexOptions.CultureInvariant);

    // (P11: -2p after it, the co-op walk of player 2 for loopback.sh --play)
    private static readonly Regex _exitName = new($@"^{MapPart}-walk-exit(?<coop>-2p)?$", RegexOptions.CultureInvariant);

    /// <summary>P11: whether a walked exit route's name is the co-op one (<c>…-walk-exit-2p</c>), two players.</summary>
    public static bool IsCoopExitName(string name) => _exitName.Match(name) is { Success: true } m && m.Groups["coop"].Success;

    /// <summary>Whether <paramref name="name"/> is a walked exit route's name (<c>mapNN-walk-exit</c>; T10.7: <c>tnt-</c> or <c>plutonia-</c> before it on Final Doom's IWADs; M9: <c>doom-exmy-walk-exit</c> on The Ultimate Doom's).</summary>
    public static bool IsExitName(string name) => _exitName.IsMatch(name);

    /// <summary>
    /// <c>mapNN-walk-exit</c>: from the map's start, no monsters, the player walks to the
    /// normal exit (switch 11 or walk-over 52), through the manual doors on the way, picking
    /// up the keys it can reach while the exit is out of reach; fails when that does not do.
    /// T10.7: <c>tnt-mapNN-walk-exit</c> and <c>plutonia-mapNN-walk-exit</c> on Final Doom's IWADs
    /// (<see cref="IwadAndMap"/>).
    /// </summary>
    public static (string Header, Action<Steer, Action<string>> Run) MakeExit(string name)
    {
        (string iwad, string map) = IwadAndMap(name);
        bool coop = IsCoopExitName(name);
        string header = coop ? $"""
# {Game(iwad, name)} {map} in co-op (P11: the lockstep game of loopback.sh --play), two
# players, no monsters, no route events: both stand for 70 tics (the netgame's first 70,
# before loopback.sh's nettic 70: it plays the route from=70, so the game is this one);
# then player 2 (the client) walks from its own start to the exit, through the doors on
# the way (and the keys they need), and leaves the level, the last tic; player 1 (the
# host) stands at its start. Written by tools/RouteGen ({name}).
iwad {iwad}{PwadHeader(name)}
map {map}
players 2
exit normal

""" : $"""
# {Game(iwad, name)} {map} ({Task(iwad, name)}): from the map's start, no monsters: the player walks to the exit,
# through the doors on the way (and the keys they need), and leaves the level, the last
# tic. Written by tools/RouteGen ({name}).
iwad {iwad}{PwadHeader(name)}
map {map}
exit normal

""";
        void Run(Steer g, Action<string> log)
        {
            // T10.7: the route ends with the tic that leaves the level, wherever (an exit line crossed on the way to another)
            g.OnTic = () =>
            {
                if (g.World.gameaction == gameaction_t.ga_completed)
                    throw new LevelExitedException();
            };
            try
            {
                Walk(g, log);
            }
            catch (LevelExitedException)
            {
                log("left the level");
            }
        }
        void Walk(Steer g, Action<string> log)
        {
            g.TightCorners = true;
            if (coop)
            {
                // P11: the netgame's first 70 tics, both standing; then the client walks
                g.Player = 1;
                g.Wait(70);
            }
            g.Wait(1);
            var keysTried = new HashSet<mobj_t>();
            var switchesTried = new HashSet<int>();
            // T10.7: the route's hints first, in order (where greedy pressing goes astray)
            foreach (string hint in _exitHints.GetValueOrDefault(name, []))
            {
                if (hint.StartsWith("key:", StringComparison.Ordinal))
                {
                    mobj_t k = g.World.Mobjs().First(m => m.type.ToString() == hint[4..]);
                    g.GoToDoors(k.x / (double)FU, k.y / (double)FU, 4);
                    keysTried.Add(k);
                    log($"hint: key {k.type}");
                }
                else if (hint.StartsWith("walk:", StringComparison.Ordinal))
                {
                    // P10: walk:X,Y,TICS,SPEED walks straight towards X,Y (no planning: e.g. running across a gap)
                    double[] w = [.. hint[5..].Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture))];
                    g.Walk(w[0], w[1], (int)w[2], (int)w[3]);
                    log($"hint: walked to ({g.X:F0}, {g.Y:F0}, {g.Z:F0})");
                }
                else if (hint.StartsWith("exit:", StringComparison.Ordinal))
                {
                    // P10: exit:X,Y turns to X,Y and uses: the exit switch there, the route's last tic
                    double[] at = [.. hint[5..].Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture))];
                    g.Exit(at[0], at[1]);
                    return;
                }
                else if (hint.StartsWith("wait:", StringComparison.Ordinal))
                {
                    // P10: wait:TICS stands still
                    g.Wait(int.Parse(hint[5..], CultureInfo.InvariantCulture));
                    log("hint: waited");
                }
                else if (hint.StartsWith("goto:", StringComparison.Ordinal))
                {
                    // P10: goto:X,Y walks there through the manual doors (Steer.GoToDoors)
                    double[] at = [.. hint[5..].Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture))];
                    g.GoToDoors(at[0], at[1], 8);
                    log($"hint: went to ({g.X:F0}, {g.Y:F0}, {g.Z:F0})");
                }
                else if (hint.StartsWith("tele:", StringComparison.Ordinal))
                {
                    double[] at = [.. hint[5..].Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture))];
                    g.Teleport(at[0], at[1]);
                    log($"hint: teleported to ({g.X:F0}, {g.Y:F0})");
                }
                else if (!PressASwitch(g, log, switchesTried, int.Parse(hint, CultureInfo.InvariantCulture)))
                {
                    throw new InvalidOperationException($"hint line {hint}: out of reach");
                }
            }
            for (int round = 0; round < 24; round++)
            {
                foreach (line_t l in g.World.lines.Where(l => l.special is 11 or 52))
                {
                    double x1 = l.v1.X / (double)FU, y1 = l.v1.Y / (double)FU, x2 = l.v2.X / (double)FU, y2 = l.v2.Y / (double)FU;
                    double len = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
                    double mx = (x1 + x2) / 2, my = (y1 + y2) / 2, nx = (y2 - y1) / len, ny = -(x2 - x1) / len;
                    // T9.4: from the back when the front has no room (E2M9's exit line runs along its door's far side)
                    if (!new[] { 24.0, 32, 40 }.Any(d => g.Fits(mx + nx * d, my + ny * d)))
                        (nx, ny) = (-nx, -ny);
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
                    if (keysTried.Contains(k))
                        continue;
                    try
                    {
                        g.GoToDoors(k.x / (double)FU, k.y / (double)FU, 4);
                        keysTried.Add(k);
                        log($"key {k.type}");
                        got = true;
                        break;
                    }
                    catch (InvalidOperationException e)
                    {
                        log($"key {k.type} at ({k.x >> 16}, {k.y >> 16}): {e.Message.Split(';')[0]}");
                    }
                }
                // T10.7: else the nearest switch within reach not pressed yet, its movers waited for
                if (!got)
                    got = PressASwitch(g, log, switchesTried);
                if (!got)
                    break;
            }
            // ROUTEGEN_DEBUG="X,Y;X,Y": what the world is at those points now, and whether a path reaches them
            if (Environment.GetEnvironmentVariable("ROUTEGEN_DEBUG") is { Length: > 1 } points)
            {
                foreach (string pt in points.Split(';'))
                {
                    double px = double.Parse(pt.Split(',')[0], CultureInfo.InvariantCulture), py = double.Parse(pt.Split(',')[1], CultureInfo.InvariantCulture);
                    string path;
                    try
                    {
                        path = $"{g.Plan(px, py).Count} steps";
                    }
                    catch (InvalidOperationException e)
                    {
                        path = e.Message.Split(';')[0];
                    }
                    log($"{g.ProbeText(px, py)}: {path}");
                }
            }
            throw new InvalidOperationException("no way to the exit");
        }
        return (header, Run);
    }

    /// <summary>
    /// T10.7: the walked exit routes' hints: line indices to press or cross
    /// (<see cref="PressASwitch"/>), <c>key:MT_…</c> keys to pick up and
    /// <c>tele:X,Y</c> teleport pads to walk into (<see cref="Steer.Teleport"/>), in
    /// order, before the walker's own search. P10: <c>walk:X,Y,TICS,SPEED</c> walks straight
    /// towards a point (<see cref="Steer.Walk"/>), <c>goto:X,Y</c> plans there through the manual
    /// doors (<see cref="Steer.GoToDoors"/>) and <c>wait:TICS</c> stands, and <c>exit:X,Y</c> uses the exit switch there (the route's end).
    /// </summary>
    private static readonly Dictionary<string, string[]> _exitHints = new(StringComparer.Ordinal)
    {
        // the red skull (east, past the lifts and the doors these open), the red door to the
        // north room, the switch that lowers the blue skull's pillar, the blue door, the lift up and
        // the teleporter beside it to the exit's ledge
        // P10: Requiem's MAP01 starts on a pillar in a pit: run north across it
        ["requiem-map01-walk-exit"] = ["walk:-480,200,8,50"],
        // AV's MAP01: line 300 lowers the narrow lift 53 (24 deep, under the player's box): stand at its edge while it is down, up
        // with it; the yellow skull, the doors and lowered floors on the way east to line 715, which opens door 85 to the south;
        // down through it to line 607, which lowers the gate (sector 100) before the exit
        ["av-map01-walk-exit"] = ["300", "goto:604,-280", "wait:150", "key:MT_MISC8", "109", "293", "436", "708", "700", "705", "715", "goto:1028,-410", "goto:900,-420", "goto:604,-530", "607"],
        // D2TWID's MAP01: the planner finds no way to the exit room's turned walls; go to its middle, then step to the switch
        ["d2twid-map01-walk-exit"] = ["goto:-1150,-1720", "walk:-1195,-1795,14,10", "exit:-1201,-1803"],
        ["plutonia-map01-walk-exit"] = ["294", "379", "821", "981", "910", "key:MT_MISC8", "1099", "1114", "key:MT_MISC9", "659", "613", "tele:1696,-1184"],
    };

    // the manual doors (Steer.GoToDoors opens them) and the exits: not switches to try
    private static readonly HashSet<int> _notSwitches = [1, 26, 27, 28, 31, 32, 33, 34, 117, 118, 11, 51, 48, 85]; // (P10: and the scrolling walls, no switches)

    // the specials that close, lower ceilings or crush (p_spec.c, p_switch.c): never tried
    private static readonly HashSet<int> _closing = [3, 6, 16, 25, 41, 42, 43, 44, 49, 50, 73, 75, 76, 77, 107, 110, 113, 116, 141];

    /// <summary>
    /// T10.7: the walked exit's last resort: goes to the nearest use line (a
    /// switch: not a walk-over, shoot, manual door or exit special) or
    /// walk-over line (not a teleport or exit) within reach that it has not
    /// pressed or crossed (those out of reach are tried again later), none
    /// that closes or crushes, presses it from in front or walks across it,
    /// and waits for the movers it started (a door until it waits open, a
    /// lift until it waits down, then rides it up when it can get on; at most
    /// 10 s each); false when none is within reach. With <paramref name="only"/>, that line alone (a hint).
    /// </summary>
    private static bool PressASwitch(Steer g, Action<string> log, HashSet<int> tried, int only = -1)
    {
        var candidates = g.World.lines
            .Where(l => (only < 0 || l.Index == only) && l.special > 0 && !_shoot.Contains(l.special) && !_notSwitches.Contains(l.special) && !_closing.Contains(l.special)
                && !_teleports.Contains(l.special) && l.special is not (52 or 124) && !tried.Contains(l.Index) && HasKeyFor(g, l.special))
            .Select(l =>
            {
                double x1 = l.v1.X / (double)FU, y1 = l.v1.Y / (double)FU, x2 = l.v2.X / (double)FU, y2 = l.v2.Y / (double)FU;
                double len = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
                return (Line: l, Mx: (x1 + x2) / 2, My: (y1 + y2) / 2, Nx: (y2 - y1) / len, Ny: -(x2 - x1) / len);
            })
            .OrderBy(c => Math.Abs(c.Mx - g.X) + Math.Abs(c.My - g.Y))
            .ToList();
        foreach (var c in candidates)
        {
            bool walk = _walk.Contains(c.Line.special);
            foreach ((double d, int side) in walk ? new[] { (32.0, 1), (32.0, -1), (48.0, 1), (48.0, -1) } : [(24.0, 1), (32.0, 1), (40.0, 1)])
            {
                double sx = c.Mx + c.Nx * d * side, sy = c.My + c.Ny * d * side;
                double ex = c.Mx - c.Nx * 20 * side, ey = c.My - c.Ny * 20 * side; // (walk: just across it, the centre past the line)
                if (!g.Fits(sx, sy))
                    continue;
                try
                {
                    g.GoToDoors(sx, sy, 4);
                }
                catch (InvalidOperationException e)
                {
                    if (Environment.GetEnvironmentVariable("ROUTEGEN_DEBUG") is { Length: > 0 })
                        log($"line {c.Line.Index} from ({sx:F0}, {sy:F0}): {e.Message.Split(';')[0]}");
                    continue;
                }
                int special = c.Line.special, tag = c.Line.tag;
                tried.Add(c.Line.Index);
                var before = new HashSet<sector_t>(g.World.sectors.Where(sec => sec.specialdata is not null)); // (perpetual movers)
                if (walk)
                {
                    for (int t = 0; t < 30 && ((g.X - c.Mx) * c.Nx + (g.Y - c.My) * c.Ny) * side > 0; t++)
                        g.Tic(25, 0, g.TurnTo(ex, ey), 0);
                    g.Settle();
                }
                else
                {
                    g.Use(c.Mx, c.My);
                }
                // every mover it started (a staircase's steps are not tagged; a door that closes again: until it waits open; a lift that comes back up: until it waits down)
                for (int t = 0; t < 350 && g.World.sectors.Any(sec => sec.specialdata is not null and not vldoor_t { direction: 0 } and not plat_t { status: plat_e.waiting } && !before.Contains(sec)); t++)
                    g.Wait(1);
                // a lift it lowered, waiting down: onto it and up with it (Plutonia MAP01's 73 to the exit's ledge), if the player fits under the ceiling at its top
                foreach (sector_t lift in g.World.sectors.Where(sec => sec.specialdata is plat_t { status: plat_e.waiting } p && p.high + g.Mo.height <= sec.ceilingheight
                    && !before.Contains(sec)).ToList())
                {
                    double cx = lift.lines.Average(l => (l.v1.X + (double)l.v2.X) / 2) / FU, cy = lift.lines.Average(l => (l.v1.Y + (double)l.v2.Y) / 2) / FU;
                    if (!g.Fits(cx, cy))
                        continue;
                    try
                    {
                        g.GoToDoors(cx, cy, 8);
                    }
                    catch (InvalidOperationException)
                    {
                        continue;
                    }
                    for (int t = 0; t < 350 && lift.specialdata != null; t++)
                        g.Wait(1);
                    log($"rode lift sector {lift.Index} to {lift.floorheight >> 16}");
                    break;
                }
                log($"{(walk ? "crossed" : "switch")} line {c.Line.Index} (special {special}, tag {tag})");
                return true;
            }
        }
        return false;
    }

    /// <summary>Whether the player holds the key a locked switch (p_doors.c EV_DoLockedDoor: 99/133 blue, 134/135 red, 136/137 yellow) needs; true for the others.</summary>
    private static bool HasKeyFor(Steer g, int special)
    {
        bool[] c = g.World.players[g.Player].cards;
        return special switch
        {
            99 or 133 => c[(int)card_t.it_bluecard] || c[(int)card_t.it_blueskull],
            134 or 135 => c[(int)card_t.it_redcard] || c[(int)card_t.it_redskull],
            136 or 137 => c[(int)card_t.it_yellowcard] || c[(int)card_t.it_yellowskull],
            _ => true,
        };
    }

    /// <summary>Whether <paramref name="name"/> is a special route's name (<c>mapNN-lineK</c>, <c>mapNN-sectorK</c>; T10.7: <c>tnt-</c> or <c>plutonia-</c> before it on Final Doom's IWADs).</summary>
    public static bool IsName(string name) => _name.IsMatch(name);

    /// <summary>The game an <c>iwad</c> header names, for a route's comment.</summary>
    private static string Game(string iwad, string name = "") => PwadSet.Of(name) is { } set ? $"{set.Title} over {Game(iwad)}" : iwad switch
    {
        "tnt" => "TNT: Evilution",
        "plutonia" => "The Plutonia Experiment",
        "doom" => "The Ultimate Doom",
        _ => "Doom II",
    };

    /// <summary>The task that brought a route kind to an IWAD, for a route's comment.</summary>
    private static string Task(string iwad, string name = "") => PwadSet.Of(name) is not null ? "P10" : iwad switch
    {
        "doom2" => "T10.6",
        "doom" => "M9",
        _ => "T10.7",
    };

    /// <summary>T10.7: the IWAD (<c>doom2</c>, <c>tnt</c> or <c>plutonia</c>; M9: <c>doom</c>) and map of a special or walked exit route's name.</summary>
    public static (string Iwad, string Map) IwadAndMap(string name)
    {
        Match mt = _name.Match(name) is { Success: true } m ? m : _exitName.Match(name);
        string iwad = PwadSet.Of(name) is { } set ? set.Iwad : mt.Groups["iwad"].Value is { Length: > 0 } i ? i : "doom2";
        string map = mt.Groups["map"].Value;
        return (iwad, map.StartsWith('e') ? map.ToUpperInvariant() : "MAP" + map);
    }

    /// <summary>P10: the <c>pwad SET</c> header line of a route on a PWAD set (empty on an IWAD alone).</summary>
    private static string PwadHeader(string name) => PwadSet.Of(name) is { } set ? $"\npwad {set.Folder}" : "";

    /// <summary>The route <paramref name="name"/>: its header, start and steering, or an exception when it cannot be made.</summary>
    public static (string Header, (int X, int Y, int Angle) Start, Action<Steer, Action<string>> Run) Make(Func<WadArchive> wad, string name)
    {
        Match mt = _name.Match(name);
        (string iwad, string map) = IwadAndMap(name);
        int index = int.Parse(mt.Groups["index"].Value, CultureInfo.InvariantCulture);
        var probe = new Steer(wad(), map);
        return mt.Groups["kind"].Value == "line" ? LineRoute(wad, probe, map, name, index, iwad)
            : SectorRoute(probe, map, name, index, IsoDoom.Map.Level.Load(wad(), map).Sectors[index].Special, iwad);
    }

    private static (string, (int, int, int), Action<Steer, Action<string>>) LineRoute(Func<WadArchive> wad, Steer probe, string map, string name, int index, string iwad = "doom2")
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
                    // (T10.7: the switch exits too, 11 and 51: the use leaves the level, the route's last tic)
                    string exit = special is 52 or 124 or 11 or 51 ? $"exit {(special is 124 or 51 ? "secret" : "normal")}\n" : "";
                    string header = $"""
# {Game(iwad, name)} {map} ({Task(iwad, name)}): started beside line {index} (special {special}, tag {l.tag}), on its
# {(side > 0 ? "front" : "back")}, the player {kind} it, then waits for the movers it set going
# (at most {MaxWait} tics). Written by tools/RouteGen ({name}).
iwad {iwad}{PwadHeader(name)}
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
                        else if (exit.Length > 0)
                        {
                            g.Exit(tx, ty); // (T10.7: a switch exit)
                            log("left the level");
                            return;
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

    private static (string, (int, int, int), Action<Steer, Action<string>>) SectorRoute(Steer probe, string map, string name, int index, int special, string iwad = "doom2")
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
# {Game(iwad, name)} {map} ({Task(iwad, name)}): started {(outside ? "beside" : "in")} sector {index} (special {special}), the player stands
# there for {wait} tics. Written by tools/RouteGen ({name}).
iwad {iwad}{PwadHeader(name)}
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

/// <summary>T10.7: thrown by a walked exit route's tic hook once a tic left the level.</summary>
public sealed class LevelExitedException : Exception;
