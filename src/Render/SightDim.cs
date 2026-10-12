using System;
using System.Collections.Generic;
using System.Globalization;

namespace IsoDoom.Render;

/// <summary>
/// Monsters out of sight drawn darker (T6.13t, SPEC §12; a presentation
/// option, not vanilla): each live monster's dim, in colormap rows, from
/// whether it has a line of sight to the player (vanilla's
/// <c>P_CheckSight</c>, monster to player, the rule that lets it attack),
/// faded per tic so walking past a doorframe does not make it blink.
/// <list type="bullet">
/// <item><b>Into sight:</b> undimmed at once, the same tic.</item>
/// <item><b>Out of sight:</b> after <see cref="GraceTics"/> tics, the dim
/// steps up <see cref="StepRows"/> rows a tic to <see cref="Rows"/>.</item>
/// <item><b>New</b> (first drawn, or drawn again): at its target, no fade.</item>
/// <item>A thing not given in a tic's <see cref="Update"/> drops out.</item>
/// </list>
/// Plain C# (no Godot types), keyed by reference, so it is unit tested. The
/// shader adds the rows to the sprite's colormap row, up to
/// <see cref="MaxRow"/> (<c>shaders/sprite.gdshaderinc</c>).
/// </summary>
public sealed class SightDim
{
    /// <summary>The default amount, in colormap rows.</summary>
    public const int DefaultRows = 10;

    /// <summary>The largest amount: the 5 bits it has in the sprite's flags (<c>ThingSprites.FlagSightDimShift</c>).</summary>
    public const int MaxRows = 31;

    /// <summary>The tics a monster stays lit after leaving sight.</summary>
    public const int GraceTics = 4;

    /// <summary>The rows the dim grows by per tic after the grace.</summary>
    public const int StepRows = 2;

    /// <summary>The darkest colormap row the dim pushes a sprite to (the shader's <c>SIGHT_DIM_MAX_ROW</c>): a dimmed monster in a dark room stays faintly visible.</summary>
    public const int MaxRow = 29;

    private struct State
    {
        public int Dim;
        public int OutTics;
        public bool InSight;
    }

    private Dictionary<object, State> _states = new(ReferenceEqualityComparer.Instance);
    private Dictionary<object, State> _next = new(ReferenceEqualityComparer.Instance);
    private int _rows = DefaultRows;

    /// <summary>The amount out of sight, in colormap rows (0: off, vanilla; nothing is dimmed).</summary>
    public int Rows
    {
        get => _rows;
        set
        {
            _rows = Math.Clamp(value, 0, MaxRows);
            if (_rows == 0)
                Clear();
        }
    }

    /// <summary>The things in the table (the last <see cref="Update"/>'s candidates).</summary>
    public int Count => _states.Count;

    /// <summary>How many of them are dimmed now.</summary>
    public int DimmedCount
    {
        get
        {
            int n = 0;
            foreach (State s in _states.Values)
                n += Math.Min(s.Dim, _rows) > 0 ? 1 : 0;
            return n;
        }
    }

    /// <summary>Forgets every thing (a level loaded or unloaded, the option off).</summary>
    public void Clear()
    {
        _states.Clear();
        _next.Clear();
    }

    /// <summary>
    /// One tic: <paramref name="candidates"/> are the things that may be
    /// dimmed now, each with whether it has a line of sight to the player.
    /// Any other thing drops out. Nothing with <see cref="Rows"/> 0.
    /// </summary>
    public void Update(IEnumerable<(object Thing, bool InSight)> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (_rows == 0)
        {
            Clear();
            return;
        }
        _next.Clear();
        foreach ((object thing, bool inSight) in candidates)
        {
            State s;
            if (!_states.TryGetValue(thing, out State old))
                s = inSight ? default : new State { Dim = _rows, OutTics = GraceTics };
            else if (inSight)
                s = default;
            else
            {
                s = old;
                s.OutTics++;
                if (s.OutTics > GraceTics)
                    s.Dim = Math.Min(Math.Min(s.Dim, _rows) + StepRows, _rows);
            }
            s.InSight = inSight;
            _next[thing] = s;
        }
        (_states, _next) = (_next, _states);
        _next.Clear();
    }

    /// <summary>The rows <paramref name="thing"/> is dimmed by now (0 when it is not in the table).</summary>
    public int Dim(object thing) => _rows > 0 && _states.TryGetValue(thing, out State s) ? Math.Min(s.Dim, _rows) : 0;

    /// <summary>Whether <paramref name="thing"/> had a line of sight to the player at the last <see cref="Update"/> (null: not in the table).</summary>
    public bool? InSight(object thing) => _states.TryGetValue(thing, out State s) ? s.InSight : null;

    /// <summary>The dim <paramref name="thing"/> fades towards: 0 in sight, <see cref="Rows"/> out of it (0 when not in the table).</summary>
    public int Target(object thing) => _rows > 0 && _states.TryGetValue(thing, out State s) && !s.InSight ? _rows : 0;

    /// <summary>The colormap row a sprite drawn through <paramref name="row"/> is drawn through dimmed by <paramref name="dim"/> rows (the shader's rule): pushed down at most to <see cref="MaxRow"/>, never lighter.</summary>
    public static int DimmedRow(int row, int dim) => dim <= 0 ? row : Math.Max(row, Math.Min(row + dim, MaxRow));

    /// <summary>Parses <c>--level-sight-dim</c>'s and <c>sprites/sight_dim</c>'s value: rows 1–31, or <c>off</c> (0).</summary>
    public static int Parse(string value, string name)
    {
        if (value is "off" or "vanilla" or "0")
            return 0;
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int rows) && rows is > 0 and <= MaxRows)
            return rows;
        throw new ArgumentException($"{name}: \"{value}\" (colormap rows 1-{MaxRows}, or off)");
    }

    /// <summary>The setting's value for <paramref name="rows"/>.</summary>
    public static string Name(int rows) => rows <= 0 ? "off" : rows.ToString(CultureInfo.InvariantCulture);
}
