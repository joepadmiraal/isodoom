using System;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Sim;

namespace IsoDoom.Tools.RouteGen;

/// <summary>
/// T6.4: the player's stand-in gun for monster routes (until T6.6 ports the
/// weapons): after every tic of a <see cref="Steer"/> it notes the monsters
/// that attacked (entered their melee or missile state) and, every
/// <see cref="Every"/> tics, "shoots" the nearest of them that is still
/// alive: an <c>alert</c> (the shot's noise, as <c>P_FireWeapon</c>) and a
/// <c>damage</c> event of 5, 10 or 15 in turn (a pistol's). A live barrel
/// next to an awake monster and away from the player is shot first, so
/// monsters die in explosions too. Monsters only die after they attacked:
/// the route sees each wake, chase and attack first.
/// </summary>
public sealed class Gunner
{
    private const int FU = 1 << 16;

    private readonly Steer _g;
    private readonly HashSet<mobj_t> _attacked = new();
    private int _wait;
    private int _shots;

    /// <summary>Tics between shots.</summary>
    public int Every = 10;

    /// <summary>The damage of a shot at a monster (default: 5, 10 or 15 in turn, a pistol's).</summary>
    public Func<mobj_t, int>? Amount;

    /// <summary>Whether it shoots (it keeps noting attacks while off).</summary>
    public bool Firing = true;

    /// <summary>Kills (dead monsters it shot at) by type, for the log.</summary>
    public readonly SortedDictionary<string, int> Kills = new(StringComparer.Ordinal);

    /// <summary>Monster types that attacked, for the log.</summary>
    public readonly SortedSet<string> Attackers = new(StringComparer.Ordinal);

    private readonly HashSet<mobj_t> _shot = new();

    /// <summary>How far (units) from the player a barrel must be to be shot.</summary>
    public double BarrelSafe = 200;

    /// <summary>Barrels shot, for the log.</summary>
    public int Barrels;

    public Gunner(Steer g)
    {
        _g = g;
        g.OnTic += Tic;
    }

    private static bool Monster(mobj_t m) => (m.flags & mobjflag_t.MF_COUNTKILL) != 0;

    private static bool Awake(mobj_t m) =>
        m.state != m.info.spawnstate && m.state != Info.states[(int)m.info.spawnstate].nextstate;

    private double Dist(mobj_t a, mobj_t b) => Math.Sqrt(Math.Pow((a.x - b.x) / (double)FU, 2) + Math.Pow((a.y - b.y) / (double)FU, 2));

    private void Tic()
    {
        World w = _g.w;
        mobj_t me = _g.Mo;
        foreach (mobj_t m in w.Mobjs().Where(Monster))
        {
            if (m.health > 0 && (m.state == m.info.missilestate || m.state == m.info.meleestate))
            {
                _attacked.Add(m);
                Attackers.Add(m.type.ToString());
            }
        }
        foreach (mobj_t m in _shot.Where(m => m.health <= 0).ToList())
        {
            _shot.Remove(m);
            Kills[m.type.ToString()] = Kills.GetValueOrDefault(m.type.ToString()) + 1;
        }
        if (!Firing || me.health <= 0 || --_wait > 0)
            return;
        var awake = w.Mobjs().Where(m => Monster(m) && m.health > 0 && Awake(m)).ToList();
        mobj_t? barrel = w.Mobjs().FirstOrDefault(b => b.type == mobjtype_t.MT_BARREL && b.health > 0
            && Dist(b, me) > BarrelSafe && awake.Any(m => _attacked.Contains(m) && Dist(b, m) < 90));
        mobj_t? target = barrel ?? _attacked.Where(m => m.health > 0 && m.function != think_t.REMOVED)
            .OrderBy(m => Dist(m, me)).FirstOrDefault();
        if (target == null)
            return;
        // the event finds the first live shootable thing spawned there: make sure it is this one
        if (_g.Spawned(target.spawnpoint.X, target.spawnpoint.Y) != target)
        {
            _attacked.Remove(target);
            return;
        }
        _g.Alert();
        int amount = 5 * (1 + _shots++ % 3);
        _g.Damage(target.spawnpoint.X, target.spawnpoint.Y, barrel != null ? 20 : Amount?.Invoke(target) ?? amount);
        if (barrel == null)
            _shot.Add(target);
        else
            Barrels++;
        _wait = Every;
    }

    /// <summary>Waits until no live monster that attacked is left, or <paramref name="max"/> tics.</summary>
    public void Clear(int max = 1000)
    {
        for (int i = 0; i < max && _attacked.Any(m => m.health > 0 && m.function != think_t.REMOVED); i++)
            _g.Wait(1);
    }

    public override string ToString() =>
        $"attackers {string.Join(",", Attackers)}; kills {string.Join(",", Kills.Select(k => $"{k.Key}:{k.Value}"))}; barrels shot {Barrels}";
}
