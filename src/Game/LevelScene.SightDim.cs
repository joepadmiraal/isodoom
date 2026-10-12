using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using IsoDoom.Render;
using IsoDoom.Sim;

namespace IsoDoom.Game;

// T6.13t: monsters out of sight drawn darker (SPEC §12; not vanilla). After
// each tic, with the fog's sight walk, every drawn live monster is tested with
// vanilla's own rule for its attacks, P_CheckSight(monster, player), and one
// without a line of sight fades darker (SightDim). Under the game camera only.
public partial class LevelScene
{
    /// <summary>
    /// How many colormap rows a monster out of the player's sight is drawn
    /// darker by (<c>--level-sight-dim</c>, <c>sprites/sight_dim</c>, key Y;
    /// T6.13t, <see cref="Render.SightDim"/>): 0 is off, vanilla.
    /// </summary>
    public int SightDimRows
    {
        get => SightDims.Rows;
        set => SightDims.Rows = value;
    }

    /// <summary>Each monster's dim (T6.13t), as the last tic left it.</summary>
    public SightDim SightDims { get; } = new();

    /// <summary>The sight dim under any camera (the level check's, which has no game camera).</summary>
    public bool SightEverywhere { get; set; }

    /// <summary>Whether the sight dim applies now: rows set, under the game camera (or <see cref="SightEverywhere"/>).</summary>
    public bool SightDimActive => SightDims.Rows > 0 && (IsoActive || SightEverywhere);

    /// <summary>The last tic's sight checks: how many, and their cost in milliseconds.</summary>
    public (int Checked, double Milliseconds) SightCost { get; private set; }

    private readonly List<(object Thing, bool InSight)> _sightScratch = [];

    /// <summary>
    /// Whether <paramref name="mo"/> may be dimmed (T6.13t): not the console
    /// player's mobj <paramref name="me"/>, alive, shootable, and a chaser
    /// (a <c>seestate</c>: monsters, not barrels or corpses).
    /// </summary>
    public static bool SightCandidate(mobj_t mo, mobj_t me) =>
        mo != me && mo.health > 0 && (mo.flags & mobjflag_t.MF_SHOOTABLE) != 0 && mo.info.seestate != statenum_t.S_NULL;

    /// <summary>The colormap rows <paramref name="mo"/>'s billboard is dimmed by now (0 when the dim does not apply).</summary>
    public int SightDimOf(mobj_t mo) => SightDimActive && mo.health > 0 ? SightDims.Dim(mo) : 0;

    /// <summary>
    /// After each tic (and wherever the fog looks; the level check calls it as
    /// a tic would): each drawn candidate's line of sight to the console
    /// player, monster to player as its attacks test it, into
    /// <see cref="SightDims"/>. A dead player keeps the last result.
    /// </summary>
    public void SeeSight()
    {
        bool markers = MarkersActive; // T6.13u: the monster markers take the same checks
        if (!SightDimActive && !markers || World is not { } world || world.players[world.consoleplayer] is not { playerstate: playerstate_t.PST_LIVE, mo: { } me })
        {
            if (!SightDimActive)
                SightDims.Clear();
            if (!markers)
                _inSight.Clear();
            return;
        }
        long start = Stopwatch.GetTimestamp();
        _sightScratch.Clear();
        _inSight.Clear();
        foreach (mobj_t mo in world.Mobjs())
        {
            // P_CheckSight between tics changes no outcome of the sim (SPEC §12 T6.13t): it writes
            // validcount (only compared for equality), sightcounts (statistics) and its own globals.
            if (SightCandidate(mo, me) && IsDrawn(mo) && FogShows(mo))
            {
                bool inSight = world.P_CheckSight(mo, me);
                _sightScratch.Add((mo, inSight));
                if (inSight)
                    _inSight.Add(mo);
            }
        }
        if (SightDimActive)
            SightDims.Update(_sightScratch);
        else
            SightDims.Clear();
        SightCost = (_sightScratch.Count, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    /// <summary>The overlay's and the script's <c>sight</c> line: the amount, the dimmed and checked counts and the cost.</summary>
    public string SightText() => string.Create(CultureInfo.InvariantCulture,
        $"sight dim: {SightDim.Name(SightDims.Rows)}{(SightDims.Rows > 0 ? " rows" : "")}{(SightDims.Rows > 0 && !SightDimActive ? " (game camera only)" : "")} (Y), {SightDims.DimmedCount} dimmed of {SightCost.Checked} checked, {SightCost.Milliseconds:F3} ms");

    /// <summary>The script's <c>sight</c> lines: each candidate monster's type, position, sector, sight and dim.</summary>
    public IEnumerable<string> SightLines()
    {
        yield return SightText();
        if (World is not { } world || PlayerMobj is not { } me)
            yield break;
        foreach (mobj_t mo in world.Mobjs())
        {
            if (!SightCandidate(mo, me) || !IsDrawn(mo) || !FogShows(mo))
                continue;
            string sight = SightDims.InSight(mo) is bool b ? (b ? "in sight" : "out of sight") : "not checked";
            yield return string.Create(CultureInfo.InvariantCulture,
                $"  {mo.type} at ({mo.x >> 16}, {mo.y >> 16}, {mo.z >> 16}) sector {mo.subsector.sector.Index}: {sight}, dim {SightDimOf(mo)}");
        }
    }
}
