using System;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Sim;

namespace IsoDoom.Tools.RouteGen;

/// <summary>
/// T10.3: route events that let each monster show its attacks before it
/// dies. After every tic of a <see cref="Steer"/> it notes the tic each
/// monster first attacked (entered its melee or missile state); once
/// <see cref="Delay"/> (by type) tics have passed since, a <c>damage</c>
/// event hurts it by half its spawn health (its pain state, by
/// <c>painchance</c>), and <see cref="Delay"/> more tics later another
/// kills it (or the first kills it: <see cref="Hits"/>) (its health, so no extreme death: <see cref="Gib"/> adds its
/// spawn health for one). Monsters without a spawn point of their own (a
/// pain elemental's lost souls) cannot be named by an event and are left alone.
/// </summary>
public sealed class Brawler
{
    private readonly Steer _g;
    private readonly Dictionary<mobj_t, int> _first = [];
    private readonly Dictionary<mobj_t, int> _hits = [];

    /// <summary>The tics a monster of a type attacks (or chases, before the second hit) between hits.</summary>
    public Func<mobj_t, int> Delay { get; set; } = _ => 80;

    /// <summary>The hits that kill a monster of a type: 2 (half its health, then the rest) or 1.</summary>
    public Func<mobj_t, int> Hits { get; set; } = _ => 2;

    /// <summary>Whether a monster's killing hit gibs it (the extreme death).</summary>
    public Func<mobj_t, bool> Gib { get; set; } = _ => false;

    /// <summary>Whether it shoots (it keeps noting attacks while off).</summary>
    public bool Firing { get; set; } = true;

    /// <summary>Monsters killed by type, for the log.</summary>
    public SortedDictionary<string, int> Kills { get; } = new(StringComparer.Ordinal);

    /// <summary>The actions of the states entered (zero-tic ones included) by type, for the log.</summary>
    public SortedDictionary<string, SortedSet<string>> Attacks { get; } = new(StringComparer.Ordinal);

    public Brawler(Steer g)
    {
        _g = g;
        g.OnTic += Tic;
    }

    private static bool Monster(mobj_t m) => (m.flags & mobjflag_t.MF_COUNTKILL) != 0 || m.type == mobjtype_t.MT_SKULL; // (lost souls do not count as kills)

    private void Tic()
    {
        World w = _g.World;
        int now = _g.Cmds.Count;
        foreach (mobj_t m in w.Mobjs().Where(Monster))
        {
            if (m.health <= 0)
                continue;
            if (_prev.TryGetValue(m, out statenum_t prev) && prev != m.state)
            {
                // the zero-tic states run on the way from the last state (e.g. the pain elemental's A_PainAttack)
                statenum_t z = Info.states[(int)prev].nextstate;
                for (int i = 0; i < 8 && z != m.state && z != statenum_t.S_NULL && Info.states[(int)z].tics == 0; i++)
                {
                    Note(m, Info.states[(int)z].action);
                    z = Info.states[(int)z].nextstate;
                }
            }
            if (!_prev.TryGetValue(m, out statenum_t was) || was != m.state)
                Note(m, Info.states[(int)m.state].action);
            _prev[m] = m.state;
            if (!_first.ContainsKey(m) && (m.state == m.info.missilestate || m.state == m.info.meleestate))
                _first[m] = now;
        }
        if (!Firing || _g.Mo.health <= 0)
            return;
        foreach ((mobj_t m, int first) in _first.ToList())
        {
            if (m.health <= 0 || m.function == think_t.REMOVED)
            {
                if (_hits.Remove(m))
                    Kills[m.type.ToString()[3..]] = Kills.GetValueOrDefault(m.type.ToString()[3..]) + 1;
                _first.Remove(m);
                continue;
            }
            int hits = _hits.GetValueOrDefault(m);
            int most = Hits(m);
            if (now - first < Delay(m) * (hits + 1) || hits >= most)
                continue;
            // the event finds the first live shootable thing spawned there: only this one
            if (_g.Spawned(m.spawnpoint.X, m.spawnpoint.Y) != m)
            {
                _first.Remove(m);
                continue;
            }
            int amount = hits == 0 && most > 1 ? Math.Max(1, m.info.spawnhealth / 2) : m.health + (Gib(m) ? m.info.spawnhealth : 0);
            _g.Damage(m.spawnpoint.X, m.spawnpoint.Y, amount);
            _hits[m] = hits + 1;
        }
    }

    private readonly Dictionary<mobj_t, statenum_t> _prev = [];

    private void Note(mobj_t m, actionf_t action)
    {
        if (action == actionf_t.NULL)
            return;
        string type = m.type.ToString()[3..];
        if (!Attacks.TryGetValue(type, out SortedSet<string>? set))
            Attacks[type] = set = new(StringComparer.Ordinal);
        set.Add(action.ToString());
    }

    /// <summary>
    /// Kills every monster but those <paramref name="keep"/> picks with
    /// <c>damage</c> events of its health at the start of the next tic
    /// (asleep: so the route sees only the kept ones fight; their corpses stay).
    /// </summary>
    public void KillAllBut(Func<mobj_t, bool> keep)
    {
        foreach (mobj_t m in _g.World.Mobjs().Where(m => Monster(m) && m.health > 0 && !keep(m)).ToList())
        {
            if (_g.Spawned(m.spawnpoint.X, m.spawnpoint.Y) == m)
                _g.Damage(m.spawnpoint.X, m.spawnpoint.Y, m.health);
        }
    }

    /// <summary>Monsters still hunted (attacked, not dead).</summary>
    public int Left => _first.Keys.Count(m => m.health > 0 && m.function != think_t.REMOVED);

    /// <summary>Waits until <paramref name="done"/> or <paramref name="max"/> tics.</summary>
    public void Until(Func<bool> done, int max = 3000)
    {
        for (int i = 0; i < max && !done(); i++)
            _g.Wait(1);
    }

    public override string ToString() =>
        $"kills {string.Join(",", Kills.Select(k => $"{k.Key}:{k.Value}"))}; attacks {string.Join(" ", Attacks.Select(a => $"{a.Key}[{string.Join(",", a.Value)}]"))}";
}
