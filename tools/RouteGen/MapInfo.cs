using System;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Sim;
using IsoDoom.Wad;

namespace IsoDoom.Tools.RouteGen;

/// <summary>
/// <c>--info MAP</c>: prints what a route through a DOOM1.WAD map needs to
/// know (T5.9): player 1's start, the special lines (index, special, tag,
/// ends, front/back sectors), the special and tagged sectors (heights and
/// bounding box) and the keys, in map units.
/// </summary>
public static class MapInfo
{
    /// <summary>
    /// T6.4: <c>--monsters MAP [SKILL]</c>: the monsters and barrels spawned on
    /// DOOM1 MAP at SKILL (1-5, default 3), in thinker order: type, spawn point
    /// (a route's <c>damage X Y</c>), angle, deafness, health and sector.
    /// </summary>
    public static int PrintMonsters(WadArchive wad, string map, int skill)
    {
        var g = new Steer(wad, map, (skill_t)(skill - 1), monsters: true);
        Console.WriteLine($"start ({g.X:F0}, {g.Y:F0}) sector {g.Mo.subsector!.sector.Index}");
        foreach (mobj_t m in g.w.Mobjs().Where(m => (m.flags & (mobjflag_t.MF_COUNTKILL | mobjflag_t.MF_SHOOTABLE)) != 0 && m != g.Mo))
        {
            string deaf = (m.flags & mobjflag_t.MF_AMBUSH) != 0 ? " deaf" : "";
            Console.WriteLine($"{m.type} at ({m.spawnpoint.X}, {m.spawnpoint.Y}) angle {m.spawnpoint.Angle}{deaf} health {m.health} sector {m.subsector.sector.Index}");
        }
        return 0;
    }

    public static int Print(WadArchive wad, string map, int[]? only = null)
    {
        var g = new Steer(wad, map);
        World w = g.w;
        Console.WriteLine($"start ({g.X:F0}, {g.Y:F0}) angle {(double)g.Mo.angle / 4294967296.0 * 360:F0} sector {g.Mo.subsector!.sector.Index}");
        foreach (line_t l in w.lines.Where(l => l.special != 0))
        {
            Console.WriteLine($"line {l.Index}: special {l.special} tag {l.tag} ({l.v1.X >> 16},{l.v1.Y >> 16})-({l.v2.X >> 16},{l.v2.Y >> 16}) front {l.frontsector?.Index} back {l.backsector?.Index}");
        }
        if (only != null)
        {
            foreach (int i in only)
            {
                sector_t s = w.sectors[i];
                var ls = w.lines.Where(l => l.frontsector == s || l.backsector == s).ToList();
                int x0 = ls.Min(l => Math.Min(l.v1.X, l.v2.X)) >> 16, x1 = ls.Max(l => Math.Max(l.v1.X, l.v2.X)) >> 16;
                int y0 = ls.Min(l => Math.Min(l.v1.Y, l.v2.Y)) >> 16, y1 = ls.Max(l => Math.Max(l.v1.Y, l.v2.Y)) >> 16;
                IEnumerable<string> nb = ls.Select(l => l.frontsector == s ? l.backsector : l.frontsector).Where(n => n != null).Select(n => $"{n!.Index}({n.floorheight >> 16}/{n.ceilingheight >> 16})").Distinct();
                foreach (line_t l in ls)
                    Console.WriteLine($"  line {l.Index}: ({l.v1.X >> 16},{l.v1.Y >> 16})-({l.v2.X >> 16},{l.v2.Y >> 16}) front {l.frontsector?.Index} back {l.backsector?.Index} special {l.special}");
                Console.WriteLine($"sector {i}: floor {s.floorheight >> 16} ceil {s.ceilingheight >> 16} special {s.special} tag {s.tag} box ({x0},{y0})-({x1},{y1}) next {string.Join(" ", nb)}");
            }
            return 0;
        }
        foreach (sector_t s in w.sectors.Where(s => s.special != 0 || s.tag != 0))
        {
            var ls = w.lines.Where(l => l.frontsector == s || l.backsector == s).ToList();
            int x0 = ls.Min(l => Math.Min(l.v1.X, l.v2.X)) >> 16, x1 = ls.Max(l => Math.Max(l.v1.X, l.v2.X)) >> 16;
            int y0 = ls.Min(l => Math.Min(l.v1.Y, l.v2.Y)) >> 16, y1 = ls.Max(l => Math.Max(l.v1.Y, l.v2.Y)) >> 16;
            Console.WriteLine($"sector {s.Index}: floor {s.floorheight >> 16} ceil {s.ceilingheight >> 16} special {s.special} tag {s.tag} box ({x0},{y0})-({x1},{y1})");
        }
        foreach (mobj_t m in w.Mobjs().Where(m => m.type is >= mobjtype_t.MT_MISC4 and <= mobjtype_t.MT_MISC9))
            Console.WriteLine($"key {m.type} at ({m.x >> 16}, {m.y >> 16}) sector {m.subsector!.sector.Index}");
        foreach (mobj_t m in w.Mobjs().Where(m => (m.flags & mobjflag_t.MF_SOLID) != 0 && m != g.Mo))
            Console.WriteLine($"solid {m.type} at ({m.x >> 16}, {m.y >> 16}) radius {m.radius >> 16} sector {m.subsector!.sector.Index}");
        return 0;
    }
}
