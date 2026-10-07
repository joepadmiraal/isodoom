using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Sim;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Tools.RouteGen;

/// <summary>
/// Steers player 1 of a vanilla world (no monsters, every tweak off, as
/// <c>VanillaRoute.NewWorld</c>) with ticcmds and records them. Movement is
/// planned by A* over an 8-unit grid of <see cref="World.P_CheckPosition"/>
/// probes (a step of at most 24 units, room for the player's height), then
/// followed by aiming at the farthest path point in a clear straight line and
/// thrusting (forward and side) towards the velocity that coasts to a stop at
/// the goal. Planning only reads the world (the probes leave nothing a later
/// tic reads); <see cref="Assume"/> heights are put back before the next tic.
/// Doubles are fine here: this is a tool, the sim only sees the ticcmds.
/// </summary>
public sealed class Steer
{
    private const int FU = 1 << 16;
    private const int Grid = 8;

    public readonly World w;
    public readonly List<(int Forward, int Side, int Turn, int Buttons)> Cmds = new();

    /// <summary>Sector floor heights (units) the planner assumes (a lift that will be down when the player gets there).</summary>
    public readonly Dictionary<int, int> Assume = new();

    /// <summary>Sector ceiling heights (units) the planner assumes (doors <see cref="GoToDoors"/> opens).</summary>
    public readonly Dictionary<int, int> AssumeCeil = new();

    /// <summary>
    /// T5.9: a diagonal grid step whose orthogonal neighbours are blocked is
    /// allowed when the player fits at its midpoint (corners tighter than the
    /// grid, e.g. E1M3's sector 157). Off for the routes written before.
    /// </summary>
    public bool TightCorners;

    /// <summary>Prints the steering every 10 tics.</summary>
    public bool Verbose;

    public Steer(WadArchive wad, string map, skill_t skill = skill_t.sk_medium)
    {
        w = new World(new SpawnSettings(GameMode.shareware, skill, nomonsters: true), Tweaks.Vanilla)
        {
            textures = wad.W_CheckNumForName("TEXTURE1") >= 0 ? Textures.R_InitTextures(wad) : null,
        };
        w.G_DoLoadLevel(Level.Load(wad, map));
    }

    /// <summary>T5.6: a route's <c>start X Y ANGLE</c> (<see cref="RouteStart.Place"/>), before the first tic.</summary>
    public Steer(WadArchive wad, string map, (int X, int Y, int Angle)? start) : this(wad, map)
    {
        if (start is { } s)
            RouteStart.Place(w, s.X, s.Y, s.Angle);
    }

    public mobj_t Mo => w.players[0].mo!;
    public double X => Mo.x / (double)FU;
    public double Y => Mo.y / (double)FU;
    public double Z => Mo.z / (double)FU;

    /// <summary>Runs one tic with this ticcmd (turn: the demo's angleturn byte) and records it.</summary>
    public void Tic(int forward, int side, int turn, int buttons)
    {
        Cmds.Add((forward, side, turn, buttons));
        w.G_Ticker(new ticcmd_t { forwardmove = (sbyte)forward, sidemove = (sbyte)side, angleturn = (short)(turn << 8), buttons = (byte)buttons });
    }

    public void Wait(int tics)
    {
        for (int i = 0; i < tics; i++)
            Tic(0, 0, 0, 0);
    }

    public void WaitUntil(Func<bool> condition, int max = 2000)
    {
        for (int i = 0; i < max && !condition(); i++)
            Tic(0, 0, 0, 0);
        if (!condition())
            throw new InvalidOperationException($"WaitUntil timed out at tic {w.leveltime}");
    }

    /// <summary>Waits until <paramref name="sector"/> has no mover.</summary>
    public void WaitIdle(int sector) => WaitUntil(() => w.sectors[sector].specialdata == null);

    /// <summary>Waits until the player stands still on the floor.</summary>
    public void Settle()
    {
        while (Math.Abs(Mo.momx) > FU / 4 || Math.Abs(Mo.momy) > FU / 4 || Mo.z != Mo.floorz)
            Tic(0, 0, 0, 0);
    }

    private int TurnTo(double x, double y)
    {
        double want = Math.Atan2(y - Y, x - X);
        long wantBam = (long)Math.Round(want / (2 * Math.PI) * 4294967296.0);
        int diff = unchecked((int)((uint)wantBam - Mo.angle));
        return Math.Clamp((int)Math.Round(diff / 16777216.0), -127, 127);
    }

    /// <summary>Turns towards (x, y) in one tic.</summary>
    public void Face(double x, double y) => Tic(0, 0, TurnTo(x, y), 0);

    /// <summary>Faces (x, y) and presses use for one tic, then releases it for one.</summary>
    public void Use(double x, double y)
    {
        Tic(0, 0, TurnTo(x, y), 2);
        Tic(0, 0, 0, 0);
    }

    /// <summary>T5.9: waits until a tic leaves the level (e.g. E1M8's damage-and-exit sector 11): the route's last tic.</summary>
    public void WaitExit(int max = 2000)
    {
        for (int i = 0; i < max && w.gameaction != gameaction_t.ga_completed; i++)
            Tic(0, 0, 0, 0);
        if (w.gameaction != gameaction_t.ga_completed)
            throw new InvalidOperationException($"WaitExit: no exit by tic {w.leveltime}");
    }

    /// <summary>
    /// T5.9: walks straight towards (x, y) at <paramref name="speed"/> for
    /// <paramref name="tics"/> tics (no planning: e.g. into a gap narrower
    /// than the planner's grid), then settles.
    /// </summary>
    public void Walk(double x, double y, int tics, int speed = 10)
    {
        for (int i = 0; i < tics; i++)
            Tic(speed, 0, TurnTo(x, y), 0);
        Settle();
    }

    /// <summary>
    /// T5.9: turns towards (x, y) and, on the next tic, presses use: the
    /// route's last tic (an exit switch; vanilla's demo leaves the level there).
    /// Fails unless the sim left the level.
    /// </summary>
    public void Exit(double x, double y)
    {
        Face(x, y);
        Tic(0, 0, 0, 2);
        if (w.gameaction != gameaction_t.ga_completed)
            throw new InvalidOperationException($"Exit: no exit at ({X:F1}, {Y:F1}) by tic {w.leveltime}");
    }

    /// <summary>
    /// Opens the door of <paramref name="sector"/> (use, facing (fx, fy),
    /// unless it is open and not closing) and waits until it is open.
    /// </summary>
    public void Door(int sector, double fx, double fy)
    {
        sector_t sec = w.sectors[sector];
        bool Open() => IsOpen(sec);
        if (!Open())
            Use(fx, fy);
        WaitUntil(() => sec.ceilingheight - sec.floorheight >= 72 * FU || sec.specialdata is vldoor_t { direction: 0 }
            || (sec.specialdata == null && sec.ceilingheight - sec.floorheight >= 56 * FU));
    }

    /// <summary>
    /// Whether a door sector is open and not closing: 64 units high, or (T5.9)
    /// waiting at the top, high enough for the player (a door lower than 64,
    /// e.g. E1M3's sector 174: using it again would close it).
    /// </summary>
    private bool IsOpen(sector_t sec) =>
        sec.specialdata is not vldoor_t { direction: -1 }
        && (sec.ceilingheight - sec.floorheight >= 64 * FU
            || (sec.specialdata is vldoor_t { direction: 0 } && sec.ceilingheight - sec.floorheight >= Mo.height));

    /// <summary>"F S T B [xN]" lines with the header (comments, iwad, map) first.</summary>
    public string Route(string header)
    {
        var sb = new StringBuilder(header);
        sb.Append("# forwardmove sidemove turn buttons [xcount]; turn << 8 = angleturn\n");
        for (int k = 0; k < Cmds.Count;)
        {
            int n = 1;
            while (k + n < Cmds.Count && Cmds[k + n] == Cmds[k])
                n++;
            var c = Cmds[k];
            sb.Append($"{c.Forward} {c.Side} {c.Turn} {c.Buttons}");
            if (n > 1)
                sb.Append($" x{n}");
            sb.Append('\n');
            k += n;
        }
        return sb.ToString();
    }

    /// <summary>"SECTOR:FLOOR/CEILING" in units, for the log.</summary>
    public string Heights(params int[] sectors) =>
        string.Join(" ", sectors.Select(s => $"{s}:{w.sectors[s].floorheight / FU}/{w.sectors[s].ceilingheight / FU}"));

    // ---- planning ----

    private readonly record struct Cell(bool Ok, int Floor, int Ceil);

    private readonly Dictionary<(int, int), Cell> _cache = new();

    /// <summary>T5.9 (<c>--probe</c>): whether the player fits at (x, y) (units), and the floor and ceiling there.</summary>
    public string ProbeText(double x, double y)
    {
        Cell c = ProbeAt((int)(x * FU), (int)(y * FU));
        return $"({x}, {y}): {(c.Ok ? "fits" : "blocked")} floor {c.Floor / FU} ceiling {c.Ceil / FU}";
    }

    private Cell ProbeAt(int x, int y)
    {
        if (Avoid.Count > 0)
        {
            int r = Mo.radius;
            foreach ((int ax, int ay) in new[] { (x, y), (x - r, y - r), (x + r, y - r), (x - r, y + r), (x + r, y + r) })
            {
                if (Avoid.Contains(w.R_PointInSubsector(ax, ay).sector.Index))
                    return new Cell(false, 0, 0);
            }
        }
        // T5.9: without MF_PICKUP, or PIT_CheckThing would pick up the items the probe touches
        mobjflag_t flags = Mo.flags;
        Mo.flags &= ~mobjflag_t.MF_PICKUP;
        try
        {
            return w.P_CheckPosition(Mo, x, y) && w.tmceilingz - w.tmfloorz >= Mo.height
                ? new Cell(true, w.tmfloorz, w.tmceilingz)
                : new Cell(false, w.tmfloorz, w.tmceilingz);
        }
        finally
        {
            Mo.flags = flags;
        }
    }

    private Cell Probe(int gx, int gy)
    {
        if (!_cache.TryGetValue((gx, gy), out Cell c))
            _cache[(gx, gy)] = c = ProbeAt(gx * Grid * FU, gy * Grid * FU);
        return c;
    }

    private bool Step(Cell a, Cell b) => b.Ok && b.Floor - a.Floor <= 24 * FU && b.Ceil - a.Floor >= Mo.height;

    /// <summary>Runs <paramref name="action"/> with the <see cref="Assume"/>d heights, then puts the real ones back.</summary>
    private T Assuming<T>(Func<T> action)
    {
        var floors = Assume.Keys.ToDictionary(k => k, k => w.sectors[k].floorheight);
        var ceilings = AssumeCeil.Keys.ToDictionary(k => k, k => w.sectors[k].ceilingheight);
        foreach (var (k, v) in Assume)
            w.sectors[k].floorheight = v * FU;
        foreach (var (k, v) in AssumeCeil)
            w.sectors[k].ceilingheight = v * FU;
        try
        {
            return action();
        }
        finally
        {
            foreach (var (k, v) in floors)
                w.sectors[k].floorheight = v;
            foreach (var (k, v) in ceilings)
                w.sectors[k].ceilingheight = v;
        }
    }

    /// <summary>A grid path (units) from the player to (tx, ty), the target last.</summary>
    public List<(double X, double Y)> Plan(double tx, double ty) => Assuming(() => PlanNow(tx, ty));

    private List<(double X, double Y)> PlanNow(double tx, double ty)
    {
        _cache.Clear();
        var start = ((int)Math.Round(X / Grid), (int)Math.Round(Y / Grid));
        var end = ((int)Math.Round(tx / Grid), (int)Math.Round(ty / Grid));
        if (start == end)
            return new List<(double, double)> { (tx, ty) };
        var open = new PriorityQueue<(int, int), double>();
        var cost = new Dictionary<(int, int), double> { [start] = 0 };
        var from = new Dictionary<(int, int), (int, int)>();
        open.Enqueue(start, 0);
        int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 }, dy = { 0, 0, 1, -1, 1, -1, 1, -1 };
        while (open.Count > 0)
        {
            var cur = open.Dequeue();
            if (cur == end)
                break;
            Cell cc = cur == start ? new Cell(true, Mo.floorz, Mo.ceilingz) : Probe(cur.Item1, cur.Item2);
            for (int k = 0; k < 8; k++)
            {
                var nb = (cur.Item1 + dx[k], cur.Item2 + dy[k]);
                if (!Step(cc, Probe(nb.Item1, nb.Item2)))
                    continue;
                // diagonals: both orthogonal neighbours too
                if (k >= 4 && (!Step(cc, Probe(cur.Item1 + dx[k], cur.Item2)) || !Step(cc, Probe(cur.Item1, cur.Item2 + dy[k])))
                    && !(TightCorners && Step(cc, ProbeAt((cur.Item1 * 2 + dx[k]) * Grid / 2 * FU, (cur.Item2 * 2 + dy[k]) * Grid / 2 * FU))))
                    continue;
                double c = cost[cur] + (k >= 4 ? 1.4142 : 1);
                if (!cost.TryGetValue(nb, out double old) || c < old)
                {
                    cost[nb] = c;
                    from[nb] = cur;
                    open.Enqueue(nb, c + Math.Sqrt(Math.Pow(nb.Item1 - end.Item1, 2) + Math.Pow(nb.Item2 - end.Item2, 2)));
                }
            }
        }
        if (!from.ContainsKey(end))
        {
            var reached = new SortedSet<int>(cost.Keys.Select(p => w.R_PointInSubsector(p.Item1 * Grid * FU, p.Item2 * Grid * FU).sector.Index));
            throw new NoPathException(
                $"No path from ({X:F0}, {Y:F0}) to ({tx}, {ty}) at tic {w.leveltime}; reachable sectors: {string.Join(",", reached)}");
        }
        var path = new List<(double, double)>();
        for (var p = end; p != start; p = from[p])
            path.Add((p.Item1 * Grid, p.Item2 * Grid));
        path.Reverse();
        return path;
    }

    /// <summary>Whether the player could walk straight from (x0, y0) to (x1, y1) (probes every 4 units).</summary>
    private bool Clear(double x0, double y0, double x1, double y1)
    {
        double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        int n = Math.Max(1, (int)(len / 4));
        var prev = new Cell(true, Mo.floorz, Mo.ceilingz);
        for (int i = 1; i <= n; i++)
        {
            Cell c = ProbeAt((int)((x0 + (x1 - x0) * i / n) * FU), (int)((y0 + (y1 - y0) * i / n) * FU));
            if (!Step(prev, c))
                return false;
            prev = c;
        }
        return true;
    }

    private double Dist((double X, double Y) p) => Math.Sqrt((p.X - X) * (p.X - X) + (p.Y - Y) * (p.Y - Y));

    /// <summary>
    /// Moves to (x, y), within <paramref name="tol"/> units, and settles;
    /// <paramref name="speed"/> is the largest forwardmove (25 walks, 50 runs).
    /// Replans when it makes no progress.
    /// </summary>
    public void GoTo(double x, double y, double tol = 8, int speed = 25)
    {
        double vmax = speed * 0.906 / 0.094 / 32; // about the terminal speed (units a tic)
        for (int attempt = 0; attempt < 20; attempt++)
        {
            List<(double X, double Y)> path = Plan(x, y);
            int i = 0, stuck = 0, blocked = 0;
            double best = double.MaxValue;
            while (true)
            {
                double dGoal = Math.Sqrt((x - X) * (x - X) + (y - Y) * (y - Y));
                double vx = Mo.momx / (double)FU, vy = Mo.momy / (double)FU;
                if (dGoal <= tol && vx * vx + vy * vy < 1)
                {
                    Settle();
                    return;
                }
                // the nearest path point ahead, so passing a point moves on
                int nearest = i;
                for (int k = i; k < Math.Min(path.Count, i + 48); k++)
                {
                    if (Dist(path[k]) < Dist(path[nearest]))
                        nearest = k;
                }
                i = nearest;
                while (i < path.Count - 1 && Dist(path[i]) < 12)
                    i++;
                // aim at the farthest path point in a clear straight line (with the assumed heights)
                int j = Assuming(() =>
                {
                    for (int k = Math.Min(path.Count - 1, i + 48); k > i; k--)
                    {
                        if (Clear(X, Y, path[k].X, path[k].Y))
                            return k;
                    }
                    return i;
                });
                (double X, double Y) target = j == path.Count - 1 ? (x, y) : path[j];
                if (blocked > 4)
                {
                    // wedged between grid points: back to the nearest one first
                    target = (Math.Round(X / Grid) * Grid, Math.Round(Y / Grid) * Grid);
                    if (Dist(target) < 0.3)
                        blocked = 0;
                }
                double tdx = target.X - X, tdy = target.Y - Y;
                double td = Math.Max(1e-6, Math.Sqrt(tdx * tdx + tdy * tdy));
                // the velocity that coasts to a stop at the goal (friction 0xe800 a tic)
                double vdes = Math.Min(vmax, Math.Max(0.5, dGoal * 0.094 / 0.906 * 0.9));
                // next tic's velocity is (V + thrust) * friction
                double ax = tdx / td * vdes / 0.90625 - vx, ay = tdy / td * vdes / 0.90625 - vy;
                int turn = TurnTo(target.X, target.Y);
                double th = (Mo.angle + (uint)(turn << 24)) / 4294967296.0 * 2 * Math.PI;
                double fwd = (ax * Math.Cos(th) + ay * Math.Sin(th)) * 32, side = (ax * Math.Sin(th) - ay * Math.Cos(th)) * 32;
                int f = (int)Math.Round(Math.Clamp(fwd, -speed, speed));
                int s = (int)Math.Round(Math.Clamp(side, -speed * 0.8, speed * 0.8));
                int ox = Mo.x, oy = Mo.y;
                Tic(f, s, turn, 0);
                if (Math.Abs(Mo.x - ox) + Math.Abs(Mo.y - oy) < FU / 8 && (f != 0 || s != 0))
                    blocked++;
                if (Verbose && w.leveltime % 10 == 0)
                    Console.Error.WriteLine($"    [{w.leveltime}] ({X:F0},{Y:F0},{Z:F0}) f {f} s {s} t {turn} target ({target.X:F0},{target.Y:F0})");
                if (dGoal < best - 0.5)
                {
                    best = dGoal;
                    stuck = 0;
                }
                else if (++stuck > 40)
                {
                    break; // replan
                }
            }
            Settle();
        }
        throw new InvalidOperationException($"GoTo ({x}, {y}): stuck at ({X:F1}, {Y:F1}) at tic {w.leveltime}");
    }

    /// <summary>
    /// T5.6: walks (<paramref name="speed"/> 25) or runs (50) straight
    /// towards (x, y) until the player teleports (moves over 64 units in a
    /// tic), then keeps pressing forward for <paramref name="hold"/> tics (the
    /// <c>reactiontime</c> freeze ignores them for 18) and settles. Fails
    /// after <paramref name="max"/> tics without a teleport.
    /// </summary>
    public void Teleport(double x, double y, int speed = 25, int hold = 0, int max = 300)
    {
        for (int i = 0; i < max; i++)
        {
            int ox = Mo.x, oy = Mo.y;
            Tic(speed, 0, TurnTo(x, y), 0);
            if (Math.Abs((long)Mo.x - ox) + Math.Abs((long)Mo.y - oy) > 64L * FU)
            {
                for (int k = 0; k < hold; k++)
                    Tic(speed, 0, 0, 0);
                Settle();
                return;
            }
        }
        throw new InvalidOperationException($"Teleport towards ({x}, {y}): no teleport at ({X:F1}, {Y:F1}) by tic {w.leveltime}");
    }

    /// <summary>T5.9: sectors the player's box must not touch (e.g. a teleporter on the way: E1M5's sector 56).</summary>
    public readonly HashSet<int> Avoid = new();

    /// <summary>T5.6: door sectors <see cref="GoToDoors"/> must not plan through (e.g. a door that opens from the other side only).</summary>
    public readonly HashSet<int> ShutDoors = new();

    private static readonly int[] ManualDoors = { 1, 31, 117, 118 };

    /// <summary>
    /// T5.9: whether <see cref="GoToDoors"/> may open a door of line special
    /// <paramref name="special"/>: the manual doors without keys, and the
    /// locked ones (26-28, 32-34) once the player holds their card or skull.
    /// </summary>
    private bool CanOpen(int special)
    {
        bool[] c = w.players[0].cards;
        return ManualDoors.Contains(special) || special switch
        {
            26 or 32 => c[(int)card_t.it_bluecard] || c[(int)card_t.it_blueskull],
            27 or 34 => c[(int)card_t.it_yellowcard] || c[(int)card_t.it_yellowskull],
            28 or 33 => c[(int)card_t.it_redcard] || c[(int)card_t.it_redskull],
            _ => false,
        };
    }

    /// <summary>
    /// <see cref="GoTo"/>, opening the manual doors without keys (specials 1,
    /// 31, 117 and 118) and the locked ones whose key the player holds (T5.9) on the way: plans as if they were open, stops in front
    /// of the first closed one on the path, opens it, and plans again.
    /// </summary>
    public void GoToDoors(double x, double y, double tol = 8, int speed = 25)
    {
        for (int round = 0; round < 20; round++)
        {
            var doors = new HashSet<int>();
            AssumeCeil.Clear();
            foreach (line_t l in w.lines)
            {
                if (CanOpen(l.special) && l.backsector is { } d && !ShutDoors.Contains(d.Index) && doors.Add(d.Index))
                    AssumeCeil[d.Index] = (World.P_FindLowestCeilingSurrounding(d) >> 16) - 4;
            }
            List<(double X, double Y)> path = Plan(x, y);
            AssumeCeil.Clear();
            int hit = path.FindIndex(p =>
            {
                sector_t sec = w.R_PointInSubsector((int)(p.X * FU), (int)(p.Y * FU)).sector;
                return doors.Contains(sec.Index)
                    && !IsOpen(sec);
            });
            try
            {
                if (hit < 0)
                {
                    GoTo(x, y, tol, speed);
                    return;
                }
                var before = path[Math.Max(0, hit - 5)];
                if (Dist(before) > 4)
                    GoTo(before.X, before.Y, 6, speed);
            }
            catch (NoPathException e) when (hit < 0 || Dist(path[hit]) >= 48)
            {
                // T5.9: a door that was open when planned closed on the way: plan again
                Console.Error.WriteLine($"    [{w.leveltime}] ({X:F0},{Y:F0}) replanning: {e.Message.Split(';')[0]}");
                continue;
            }
            catch (NoPathException)
            {
                // T5.9: already at the door (its closing blocks the last steps): open it from here
            }
            int door = w.R_PointInSubsector((int)(path[hit].X * FU), (int)(path[hit].Y * FU)).sector.Index;
            Console.Error.WriteLine($"    [{w.leveltime}] ({X:F0},{Y:F0}) opening door {door} at ({path[hit].X:F0},{path[hit].Y:F0})");
            Door(door, path[hit].X, path[hit].Y);
        }
        throw new InvalidOperationException("GoToDoors: more than 20 doors");
    }
}

/// <summary>T5.9: <see cref="Steer.Plan"/> found no path (e.g. a door closed on the way).</summary>
public sealed class NoPathException(string message) : InvalidOperationException(message);
