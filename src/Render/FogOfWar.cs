using System;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Render;

/// <summary>How the fog of war draws the sectors the player has not seen (T6.13l, SPEC §12; a presentation option). The shader's <c>fog_mode</c>.</summary>
public enum FogStyle
{
    /// <summary>No fog: everything is drawn, as vanilla's map data allows (the overview and free-fly cameras always).</summary>
    Off = 0,

    /// <summary>Unseen sectors are drawn dimmed (<see cref="FogOfWar.DimMap"/>): the layout shows, the detail does not.</summary>
    Dim = 1,

    /// <summary>Unseen sectors are not drawn at all: the void's colour shows (the default, the user's choice).</summary>
    Hide = 2,
}

/// <summary>
/// How dim draws an unseen surface (T6.13l): through COLORMAP row
/// <paramref name="Row"/> (0 bright to 31 dark), whatever its light, then
/// <paramref name="Grey"/> percent towards grey (<see cref="FogOfWar.DimMap"/>);
/// with <paramref name="Dither"/>, half its pixels (a 4x4 ordered dither) show the void.
/// </summary>
public readonly record struct FogDim(int Row, int Grey, bool Dither)
{
    /// <summary>The default look (the user's pick of three: dark grey).</summary>
    public static FogDim Default { get; } = new(20, 100, false);

    /// <summary>Reads <c>ROW,GREY[,dither]</c>.</summary>
    public static FogDim Parse(string value)
    {
        string[] parts = value.Split(',');
        if (parts.Length is < 2 or > 3 || !int.TryParse(parts[0], out int row) || row is < 0 or >= 32
            || !int.TryParse(parts[1], out int grey) || grey is < 0 or > 100 || (parts.Length == 3 && parts[2] != "dither"))
            throw new ArgumentException($"\"{value}\" (ROW,GREY[,dither]: row 0-31, grey 0-100)");
        return new FogDim(row, grey, parts.Length == 3);
    }

    public override string ToString() => $"{Row},{Grey}{(Dither ? ",dither" : "")}";
}

/// <summary>A sector's state in the fog of war (T6.13l): the <c>sector_fog</c> texel.</summary>
public enum SectorSight : byte
{
    /// <summary>Never seen this level.</summary>
    Unseen = 0,

    /// <summary>Seen at some time this level (one of its lines is mapped), not in sight now.</summary>
    Discovered = 1,

    /// <summary>In the player's sight now.</summary>
    Visible = 2,
}

/// <summary>
/// The fog of war (T6.13l, SPEC §12): which sectors the player sees now and
/// which it has seen this level. Pure C#, no Godot types (the tests link it).
/// <para>
/// <see cref="See"/> walks the BSP from the player's position as r_bsp.c's
/// <c>R_RenderBSPNode</c> does, over the full circle instead of the view's
/// 90°: <c>R_CheckBBox</c> skips the nodes whose box is hidden, and each seg
/// of a subsector it reaches is clipped as <c>R_AddLine</c> does, against an
/// occlusion buffer of <see cref="Columns"/> angular columns (vanilla's
/// <c>solidsegs</c> on a 360° screen): a one-sided line, or a two-sided one
/// whose opening has no height (the lower ceiling at or below the higher
/// floor: p_sight.c <c>P_CrossSubsector</c>'s test, which takes in
/// <c>R_AddLine</c>'s closed-door test and also closes a shut door whose
/// floor is above a neighbour's, which vanilla's renderer hides with its
/// per-column clipping instead), blocks; any other (a window, a step, an
/// open door) lets sight through.
/// Heights do not matter beyond that, as for vanilla's clipping. A seg facing
/// away (its span 180° or more) is skipped, and one narrower than a column
/// covers nothing, as vanilla drops a seg that falls between two screen
/// columns (so a sector seen through a sliver under 0.09° is not seen).
/// </para>
/// <para>
/// A seg with a column left open when it is reached is seen: its line gets
/// vanilla's <see cref="Line.ML_MAPPED"/> (r_segs.c <c>R_StoreWallRange</c>
/// sets it on each line the renderer draws; the automap, T7.9, draws those)
/// and its sector (the subsector's) is <see cref="SectorSight.Visible"/>, as
/// is the player's own. A sector is <see cref="SectorSight.Discovered"/> when
/// one of its lines is mapped, either side: so the mapped lines, which saves
/// keep (<c>P_ArchiveWorld</c>), are the whole discovered set
/// (<see cref="Discover"/> rebuilds it), and the sector across a seen line
/// (a closed door's) is discovered with it.
/// </para>
/// </summary>
public sealed class FogOfWar
{
    /// <summary>The occlusion buffer's angular columns over the full circle (a column is 360° / 4096, about 0.09°).</summary>
    public const int Columns = 1 << ColumnBits;

    private const int ColumnBits = 12;
    private const int ColumnShift = 32 - ColumnBits;
    private const uint HalfColumn = 1u << (ColumnShift - 1);

    // r_bsp.c checkcoord: the box corners that bound its silhouette, by the viewer's position against the box.
    private static readonly int[][] _checkcoord =
    [
        [3, 0, 2, 1],
        [3, 0, 2, 0],
        [3, 1, 2, 0],
        [0, 0, 0, 0],
        [2, 0, 2, 1],
        [0, 0, 0, 0],
        [3, 1, 3, 0],
        [0, 0, 0, 0],
        [2, 0, 3, 1],
        [2, 1, 3, 1],
        [2, 1, 3, 0],
    ];

    private readonly SectorSight[] _states;
    private readonly int[] _visibleStamp;
    // The occlusion buffer as a skip list: _next[c] leads to the first open column at or after c (Columns: none).
    private readonly int[] _next = new int[Columns + 1];
    private int _stamp;
    private bool _changed;
    private int _viewx;
    private int _viewy;

    public FogOfWar(Level level)
    {
        Level = level;
        _states = new SectorSight[level.Sectors.Length];
        _visibleStamp = new int[level.Sectors.Length];
        Discover();
    }

    public Level Level { get; }

    /// <summary>Each sector's state, by sector index.</summary>
    public ReadOnlySpan<SectorSight> States => _states;

    /// <summary>Counts up whenever a sector's state changes (the scene uploads <c>sector_fog</c> then).</summary>
    public int Version { get; private set; }

    /// <summary>The sectors <see cref="SectorSight.Visible"/> after the last <see cref="See"/>.</summary>
    public int VisibleCount { get; private set; }

    /// <summary>The sectors seen this level (<see cref="SectorSight.Discovered"/> or <see cref="SectorSight.Visible"/>).</summary>
    public int DiscoveredCount { get; private set; }

    /// <summary>Whether a <see cref="See"/> has run since the last <see cref="Discover"/> (else nothing is visible).</summary>
    public bool HasSeen { get; private set; }

    public SectorSight State(int sector) => _states[sector];

    /// <summary>Whether <paramref name="sector"/> is in sight now.</summary>
    public bool IsVisible(int sector) => _states[sector] == SectorSight.Visible;

    /// <summary>Whether <paramref name="sector"/> has been seen this level (visible now or not).</summary>
    public bool IsDiscovered(int sector) => _states[sector] != SectorSight.Unseen;

    /// <summary>
    /// Rebuilds the states from the level's mapped lines alone (a level's
    /// start, a loaded game): a sector is discovered when one of its lines
    /// has <see cref="Line.ML_MAPPED"/>; none is visible until <see cref="See"/>.
    /// </summary>
    public void Discover()
    {
        Array.Clear(_states);
        foreach (Line line in Level.Lines)
        {
            if ((line.Flags & Line.ML_MAPPED) != 0)
                MapLine(line);
        }
        VisibleCount = 0;
        HasSeen = false;
        Recount();
        Version++;
    }

    /// <summary>
    /// The walk from map point (<paramref name="x"/>, <paramref name="y"/>)
    /// (fixed_t): the sectors in sight become <see cref="SectorSight.Visible"/>
    /// (and the ones that were but are not, <see cref="SectorSight.Discovered"/>),
    /// and the seen lines mapped.
    /// </summary>
    public void See(int x, int y)
    {
        _viewx = x;
        _viewy = y;
        _stamp++;
        for (int c = 0; c <= Columns; c++)
            _next[c] = c;
        _changed = false;
        if (Level.Nodes.Length == 0)
            Subsector(0);
        else
            RenderBSPNode(Level.Nodes.Length - 1);
        // The player's own sector, whatever its segs.
        Subsector own = Level.R_PointInSubsector(x, y);
        _visibleStamp[own.Sector.Index] = _stamp;

        int visible = 0;
        for (int s = 0; s < _states.Length; s++)
        {
            SectorSight state = _visibleStamp[s] == _stamp ? SectorSight.Visible
                : _states[s] != SectorSight.Unseen ? SectorSight.Discovered : SectorSight.Unseen;
            if (state == SectorSight.Visible)
                visible++;
            if (state != _states[s])
            {
                _states[s] = state;
                _changed = true;
            }
        }
        VisibleCount = visible;
        HasSeen = true;
        if (_changed)
        {
            Recount();
            Version++;
        }
    }

    private void Recount()
    {
        int discovered = 0;
        foreach (SectorSight state in _states)
        {
            if (state != SectorSight.Unseen)
                discovered++;
        }
        DiscoveredCount = discovered;
    }

    // Maps a line and discovers the sectors on both its sides.
    private void MapLine(Line line)
    {
        line.Flags |= Line.ML_MAPPED;
        DiscoverSector(line.FrontSector);
        DiscoverSector(line.BackSector);
    }

    private void DiscoverSector(Sector? sector)
    {
        if (sector is not null && _states[sector.Index] == SectorSight.Unseen)
        {
            _states[sector.Index] = SectorSight.Discovered;
            _changed = true;
        }
    }

    // r_bsp.c R_RenderBSPNode: front side first, the back side only when its box shows.
    private void RenderBSPNode(int bspnum)
    {
        while ((bspnum & Node.NF_SUBSECTOR) == 0)
        {
            Node bsp = Level.Nodes[bspnum];
            int side = Level.R_PointOnSide(_viewx, _viewy, bsp);
            RenderBSPNode(bsp.Children[side]);
            if (!CheckBBox(bsp.BBox[side ^ 1]))
                return;
            bspnum = bsp.Children[side ^ 1];
        }
        Subsector(bspnum == -1 ? 0 : bspnum & ~Node.NF_SUBSECTOR);
    }

    // r_bsp.c R_Subsector: every seg of the subsector clipped in turn.
    private void Subsector(int num)
    {
        Subsector sub = Level.Subsectors[num];
        int sector = sub.Sector.Index;
        for (int i = 0; i < sub.NumLines; i++)
        {
            Seg seg = Level.Segs[sub.FirstLine + i];
            if (AddLine(seg))
            {
                _visibleStamp[sector] = _stamp;
                if ((seg.LineDef.Flags & Line.ML_MAPPED) == 0)
                    MapLine(seg.LineDef);
            }
        }
    }

    // r_bsp.c R_AddLine on the full circle: whether some of the seg's columns were open.
    private bool AddLine(Seg seg)
    {
        uint angle1 = Tables.R_PointToAngle2(_viewx, _viewy, seg.V1.X, seg.V1.Y);
        uint angle2 = Tables.R_PointToAngle2(_viewx, _viewy, seg.V2.X, seg.V2.Y);
        // Back side, or on the line: not drawn.
        if (unchecked(angle1 - angle2) >= Tables.ANG180)
            return false;
        if (!ToColumns(angle1, angle2, out int first, out int count))
            return false;
        Sector front = seg.FrontSector;
        Sector? back = seg.BackSector;
        // p_sight.c P_CrossSubsector's test (openbottom >= opentop), which takes in R_AddLine's.
        bool solid = back is null || Math.Min(front.CeilingHeight, back.CeilingHeight) <= Math.Max(front.FloorHeight, back.FloorHeight);
        return Clip(first, count, solid);
    }

    // r_bsp.c R_CheckBBox on the full circle: whether some of the box's silhouette is open.
    private bool CheckBBox(int[] bspcoord)
    {
        int boxx = _viewx <= bspcoord[BBox.BOXLEFT] ? 0 : _viewx < bspcoord[BBox.BOXRIGHT] ? 1 : 2;
        int boxy = _viewy >= bspcoord[BBox.BOXTOP] ? 0 : _viewy > bspcoord[BBox.BOXBOTTOM] ? 1 : 2;
        int boxpos = (boxy << 2) + boxx;
        if (boxpos == 5)
            return true;
        int[] check = _checkcoord[boxpos];
        uint angle1 = Tables.R_PointToAngle2(_viewx, _viewy, bspcoord[check[0]], bspcoord[check[1]]);
        uint angle2 = Tables.R_PointToAngle2(_viewx, _viewy, bspcoord[check[2]], bspcoord[check[3]]);
        // Sitting on a line.
        if (unchecked(angle1 - angle2) >= Tables.ANG180)
            return true;
        if (!ToColumns(angle1, angle2, out int first, out int count))
            return false;
        int end = first + count;
        if (end <= Columns)
            return Find(first) < end;
        return Find(first) < Columns || Find(0) < end - Columns;
    }

    // The columns whose centres lie from angle2 round to angle1 (anticlockwise): the first and how many.
    private static bool ToColumns(uint angle1, uint angle2, out int first, out int count)
    {
        first = (int)(unchecked(angle2 + HalfColumn) >> ColumnShift);
        int end = (int)(unchecked(angle1 + HalfColumn) >> ColumnShift);
        count = (end - first) & (Columns - 1);
        return count > 0;
    }

    // Whether any of the columns first .. first + count - 1 (round the circle) is open; with solid, closes them.
    private bool Clip(int first, int count, bool solid)
    {
        int end = first + count;
        if (end <= Columns)
            return ClipRange(first, end, solid);
        bool a = ClipRange(first, Columns, solid);
        bool b = ClipRange(0, end - Columns, solid);
        return a || b;
    }

    // r_bsp.c R_ClipSolidWallSegment / R_ClipPassWallSegment on columns first .. end - 1.
    private bool ClipRange(int first, int end, bool solid)
    {
        int c = Find(first);
        if (c >= end)
            return false;
        if (solid)
        {
            while (c < end)
            {
                _next[c] = c + 1;
                c = Find(c + 1);
            }
        }
        return true;
    }

    // The first open column at or after c (Columns: none), halving the path as it goes.
    private int Find(int c)
    {
        while (_next[c] != c)
        {
            _next[c] = _next[_next[c]];
            c = _next[c];
        }
        return c;
    }

    /// <summary>
    /// The dim mapping (T6.13l): for each palette index an unseen surface's
    /// texel has (after its light), the index it is drawn as:
    /// the texel through COLORMAP row <paramref name="row"/> (a fixed dark one,
    /// whatever its light), then towards its grey by <paramref name="grey"/>
    /// percent (0 keeps the colour, 100 grey), to the nearest colour of
    /// PLAYPAL's first palette (<see cref="Fuzz.NearestIndex"/>). The shader
    /// looks it up in <c>fog_dim</c>; the level check compares with it.
    /// </summary>
    public static byte[] DimMap(Playpal playpal, Colormap colormap, int row, int grey)
    {
        var map = new byte[256];
        ReadOnlySpan<byte> dark = colormap.GetMap(row);
        for (int i = 0; i < 256; i++)
        {
            (byte r, byte g, byte b) = playpal.GetColor(0, dark[i]);
            // ITU-R BT.601 luma, in integers.
            int y = (299 * r + 587 * g + 114 * b) / 1000;
            map[i] = (byte)Fuzz.NearestIndex(playpal, 0, r + (y - r) * grey / 100, g + (y - g) * grey / 100, b + (y - b) * grey / 100);
        }
        return map;
    }
}
