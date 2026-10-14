using System;
using IsoDoom.Sim;

namespace IsoDoom.Game;

/// <summary>
/// T10.4 (not vanilla, SPEC §12): the Icon of Sin's shot cue. In vanilla
/// the player aims at the brain through the Icon's forehead in first
/// person, timing the lift (MAP30's pillar) so a rocket leaves at a height
/// that passes the hole; under the iso camera the hole cannot be seen, so the
/// brain's marker (<c>LevelScene.UpdateMonsterMarkers</c>) lights up
/// while a missile fired now would reach the brain. Presentation only, in
/// doubles over the sim's current heights (it reads, never writes the world):
/// the missile flies flat from 32 units above the shooter's feet, 8 high,
/// along its facing (autoaim left out), stops at the first line it cannot
/// pass (one-sided, or an opening its height does not fit), and the brain
/// counts as reached when the missile passes through it on the way or its
/// burst (<c>P_RadiusAttack</c>'s reach, 128 units from the brain's edge,
/// box distance) touches it.
/// </summary>
public static class IconAim
{
    private const double Fu = 65536.0;

    /// <summary>The missile's height above the shooter's feet (<c>P_SpawnPlayerMissile</c>: 4 × 8 units).</summary>
    public const double MissileZ = 32;

    /// <summary>The rocket's height and radius (<c>mobjinfo[MT_ROCKET]</c>).</summary>
    public const double MissileHeight = 8, MissileRadius = 11;

    /// <summary>The rocket's burst (<c>A_Explode</c>: 128 damage, falling off a unit a unit).</summary>
    public const double Reach = 128;

    /// <summary>How far a stopped missile stands back from the line it hit (it stays where its last step began; a rocket steps 20 units).</summary>
    public const double StopBack = 10;

    /// <summary>
    /// The tics from pressing fire with <paramref name="player"/>'s rocket
    /// launcher ready to the rocket leaving (its attack states before
    /// <c>A_FireMissile</c>: 8); 0 with another weapon, or the launcher not ready.
    /// </summary>
    public static int LaunchLead(player_t player)
    {
        if (player.readyweapon != weapontype_t.wp_missile || player.pendingweapon != weapontype_t.wp_nochange
            || player.psprites[0].state != Info.weaponinfo[(int)weapontype_t.wp_missile].readystate)
            return 0;
        int tics = 0;
        for (statenum_t s = Info.weaponinfo[(int)weapontype_t.wp_missile].atkstate; s != statenum_t.S_NULL && tics < 100; s = Info.states[(int)s].nextstate)
        {
            if (Info.states[(int)s].action == actionf_t.A_FireMissile)
                return tics;
            tics += Info.states[(int)s].tics;
        }
        return 0;
    }

    /// <summary>The farthest the trace looks.</summary>
    public const double MaxRange = 8192;

    /// <summary>
    /// Whether a missile <paramref name="shooter"/> fires along its facing
    /// <paramref name="leadTics"/> tics from now would reach
    /// <paramref name="brain"/> (see the class): its height then is guessed
    /// from its last tic's rise (<see cref="mobj_t.oldz"/>; on a lift, the lift's).
    /// </summary>
    public static bool ClearShot(World world, mobj_t shooter, mobj_t brain, int leadTics = 0)
    {
        double px = shooter.x / Fu, py = shooter.y / Fu;
        double rise = shooter.interp ? (shooter.z - (double)shooter.oldz) / Fu : 0;
        double z = (shooter.z / Fu) + (rise * leadTics) + MissileZ, top = z + MissileHeight;
        double a = shooter.angle / 4294967296.0 * 2 * Math.PI;
        double dx = Math.Cos(a), dy = Math.Sin(a);
        double stop = Stop(world, px, py, dx, dy, z, top);

        double bx = brain.x / Fu, by = brain.y / Fu, bz = brain.z / Fu, br = brain.radius / Fu;
        // a direct hit: the missile's box meets the brain's on the way, at its height
        double along = ((bx - px) * dx) + ((by - py) * dy);
        if (along > 0 && along < stop)
        {
            double cx = px + (dx * along) - bx, cy = py + (dy * along) - by;
            if (Math.Max(Math.Abs(cx), Math.Abs(cy)) < br + MissileRadius && top > bz && z < bz + (brain.height / Fu))
                return true;
        }
        if (stop >= MaxRange)
            return false;
        // the burst where it stopped
        double back = Math.Max(0, stop - StopBack);
        double sx = px + (dx * back), sy = py + (dy * back);
        return Math.Max(Math.Abs(sx - bx), Math.Abs(sy - by)) - br < Reach;
    }

    /// <summary>
    /// The distance along (<paramref name="dx"/>, <paramref name="dy"/>) from
    /// (<paramref name="px"/>, <paramref name="py"/>) to the first line a
    /// missile from <paramref name="z"/> to <paramref name="top"/> cannot pass
    /// (<see cref="MaxRange"/> when none).
    /// </summary>
    public static double Stop(World world, double px, double py, double dx, double dy, double z, double top)
    {
        double stop = MaxRange;
        foreach (line_t l in world.lines)
        {
            double x1 = l.v1.X / Fu, y1 = l.v1.Y / Fu;
            double ex = (l.v2.X / Fu) - x1, ey = (l.v2.Y / Fu) - y1;
            double den = (dx * ey) - (dy * ex);
            if (Math.Abs(den) < 1e-9)
                continue;
            double t = (((x1 - px) * ey) - ((y1 - py) * ex)) / den; // along the facing
            double u = (((x1 - px) * dy) - ((y1 - py) * dx)) / den; // along the line
            if (t <= 0 || t >= stop || u < 0 || u > 1)
                continue;
            if (l.frontsector is not { } front || l.backsector is not { } back)
            {
                stop = t;
                continue;
            }
            double floor = Math.Max(front.floorheight, back.floorheight) / Fu;
            double ceiling = Math.Min(front.ceilingheight, back.ceilingheight) / Fu;
            if (z < floor || top > ceiling)
                stop = t;
        }
        return stop;
    }
}
