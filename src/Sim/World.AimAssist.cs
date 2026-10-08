using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using IsoDoom.Map;

namespace IsoDoom.Sim;

// The horizontal aim assist (SPEC §6.3 #2, T6.7; not vanilla): behind
// Tweaks.AimAssistCone. The player's attacks (p_pspr.c P_BulletSlope and
// P_GunShot through A_FirePistol, A_FireShotgun and A_FireCGun; A_Punch,
// A_Saw; p_mobj.c P_SpawnPlayerMissile) start from the angle P_AimAssist
// picks instead of the player's facing; everything after that, the vertical
// autoaim (P_AimLineAttack) included, stays vanilla.
public sealed partial class World
{
    // Candidates of the current P_AimAssist, nearest first (reused, no allocation per shot).
    private readonly List<mobj_t> _aimCandidates = [];
    private readonly List<int> _aimCandidateDist = [];
    private readonly List<uint> _aimCandidateOff = [];

    /// <summary>
    /// The aim assist's horizontal snap (SPEC §6.3 #2, §12 T6.7): the angle
    /// the player's mobj <paramref name="mo"/> attacks along over
    /// <paramref name="distance"/> (fixed_t; the attack's own aim range).
    /// Without <see cref="Tweaks.AimAssistCone"/> it is <c>mo.angle</c> and
    /// nothing else happens (vanilla). With it: when
    /// <see cref="P_AimLineAttack"/> along the facing finds a target, the
    /// facing stays (the player aimed at something); else the angle to the
    /// centre of the nearest (<see cref="P_AproxDistance"/>, then the smaller
    /// angle off the facing, then thinker order) shootable, living non-player
    /// thing with some part within ±<see cref="Tweaks.AimAssistCone"/> of the
    /// facing (its centre within the cone widened by its radius's angle as
    /// seen from <paramref name="mo"/>) and its edge within
    /// <paramref name="distance"/>, whose aim line
    /// (<see cref="P_AimLineAttack"/> along that angle) finds a target: a thing
    /// behind a wall, or out of vanilla's vertical aim window, is skipped. The
    /// facing when none does. Leaves <see cref="linetarget"/> and the other
    /// aim globals as its last aim left them; the vanilla aim after it sets them again.
    /// </summary>
    [SuppressMessage("Naming", "CA1707", Justification = "Named as the p_*.c functions whose angle it stands in for (SPEC §12 T6.7)")]
    public uint P_AimAssist(mobj_t mo, int distance)
    {
        uint cone = tweaks.AimAssistCone;
        if (cone == 0)
            return mo.angle;

        P_AimLineAttack(mo, mo.angle, distance);
        if (linetarget != null)
            return mo.angle;

        _aimCandidates.Clear();
        _aimCandidateDist.Clear();
        _aimCandidateOff.Clear();
        foreach (mobj_t th in Mobjs())
        {
            if (th == mo || th.player != null || th.health <= 0
                || (th.flags & mobjflag_t.MF_SHOOTABLE) == 0)
                continue;
            int dist = P_AproxDistance(th.x - mo.x, th.y - mo.y);
            if (dist - th.radius > distance)
                continue;
            uint off = unchecked(Tables.R_PointToAngle2(mo.x, mo.y, th.x, th.y) - mo.angle);
            uint absOff = off > Tables.ANG180 ? unchecked(0u - off) : off;
            // in the cone when any part of it is: its centre within the cone widened by its half-width as seen from mo
            uint halfWidth = dist > 0 ? Tables.R_PointToAngle2(0, 0, dist, th.radius) : Tables.ANG90;
            if (absOff > halfWidth && absOff - halfWidth > cone)
                continue;

            // insert after every candidate at least as near (and as straight ahead): stable, so ties keep thinker order
            int i = _aimCandidates.Count;
            while (i > 0 && (_aimCandidateDist[i - 1] > dist
                || (_aimCandidateDist[i - 1] == dist && _aimCandidateOff[i - 1] > absOff)))
                i--;
            _aimCandidates.Insert(i, th);
            _aimCandidateDist.Insert(i, dist);
            _aimCandidateOff.Insert(i, absOff);
        }

        foreach (mobj_t th in _aimCandidates)
        {
            uint an = Tables.R_PointToAngle2(mo.x, mo.y, th.x, th.y);
            P_AimLineAttack(mo, an, distance);
            if (linetarget != null)
            {
                _aimCandidates.Clear();
                return an;
            }
        }
        _aimCandidates.Clear();
        return mo.angle;
    }

    /// <summary>
    /// p_pspr.c's hitscan weapons with the aim assist: <see cref="P_BulletSlope"/>
    /// and <paramref name="shots"/> × <see cref="P_GunShot"/> (both read
    /// <c>mo.angle</c>) with the player's angle turned to <see cref="P_AimAssist"/>'s
    /// for the shot only (SPEC §12 T6.7): vanilla's calls when the tweak is off.
    /// </summary>
    private void P_BulletSlopeAndShoot(mobj_t mo, int shots, bool accurate)
    {
        uint facing = mo.angle;
        mo.angle = P_AimAssist(mo, 16 * 64 * Fixed.FRACUNIT);
        P_BulletSlope(mo);
        for (int i = 0; i < shots; i++)
            P_GunShot(mo, accurate);
        mo.angle = facing;
    }
}
