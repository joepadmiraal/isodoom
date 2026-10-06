using System;
using System.Collections.Generic;

namespace IsoDoom.Map;

/// <summary>
/// One wall quad's footprint (T2.9): from <see cref="A"/> to <see cref="B"/>
/// in map space (fixed_t), with the front sector on the right. Its texture
/// columns at the two ends (fixed_t, the distance along the linedef side as
/// vanilla counts it: the seg's <c>offset</c> plus the distance from the
/// seg's start; add the sidedef's <c>textureoffset</c>), its fake contrast
/// (r_segs.c, per seg) and the seg it belongs to.
/// </summary>
public readonly record struct WallPiece(PolygonVertex A, PolygonVertex B, int ColumnA, int ColumnB, int Contrast, int Seg);

/// <summary>
/// The quads every linedef side's wall sections are drawn with (T2.9), plain
/// integer C# under the determinism scan like <see cref="WallSections"/>.
/// <para>
/// <b>Per seg, on the floor's corners.</b> Vanilla draws a wall per seg, and
/// the node builder's seg split vertices are rounded to whole units, so a
/// side's segs can bend off its linedef (DOOM1 E1M1 line 461 is split at
/// (3672, −3032), 0.4 units off the line). The floor follows the segs, so a
/// quad per linedef side leaves a crack between the wall and the floor
/// there. Each seg is therefore drawn along its floor edge,
/// <see cref="FloorTriangles.SegChains"/>: one piece between each two
/// consecutive corners, so the wall's bottom edge (and a lower wall's top
/// edge, which meets the back sector's floor on the same corners) shares the
/// floor triangles' corners exactly, with no T-junction. Where two
/// consecutive segs' chains do not meet (their floor edges lie on different
/// lines, up to <see cref="SubsectorPolygons.SegSnapEpsilon"/> apart), a
/// connecting piece is added in both directions (so it is seen from either
/// side). The same is done where two sides with the same front sector meet
/// at a vertex and their floor edges end at different points (the
/// connector is appended to the side that ends there). A seg with no
/// floor edge (its subsector polygon has none along it) is drawn from its
/// own vertexes.
/// </para>
/// <para>
/// <b>Texture columns</b> follow r_segs.c <c>R_StoreWallRange</c>
/// (<c>rw_offset = sidedef->textureoffset + curline->offset</c>, plus the
/// distance along the seg): the seg's <c>offset</c> plus the corner's
/// distance from the seg's start, projected on the seg.
/// </para>
/// </summary>
public sealed class WallPieces
{
    private static readonly WallPiece[] NoPieces = Array.Empty<WallPiece>();
    private readonly WallPiece[][] _pieces; // per line * 2 + side

    private WallPieces(WallPiece[][] pieces) => _pieces = pieces;

    /// <summary>The pieces of <paramref name="line"/>'s side <paramref name="side"/>, in seg order (empty when no seg runs along it).</summary>
    public IReadOnlyList<WallPiece> Of(Line line, int side) => _pieces[line.Index * 2 + side];

    /// <summary>The number of connecting pieces (both directions counted) the level needed.</summary>
    public int Connectors { get; private set; }

    /// <summary>Builds the pieces of every linedef side of <paramref name="level"/> along <paramref name="floors"/>' seg chains.</summary>
    public static WallPieces Build(Level level, FloorTriangles floors)
    {
        if (floors.SegChains.Count != level.Segs.Length)
            throw new ArgumentException("The floors were built for another level.", nameof(floors));
        var segs = new List<Seg>?[level.Lines.Length * 2];
        foreach (Seg seg in level.Segs)
            (segs[seg.LineDef.Index * 2 + seg.Side] ??= new List<Seg>()).Add(seg);
        var pieces = new WallPiece[segs.Length][];
        int connectors = 0;
        var list = new List<WallPiece>();
        for (int i = 0; i < segs.Length; i++)
        {
            List<Seg>? side = segs[i];
            if (side is null)
            {
                pieces[i] = NoPieces;
                continue;
            }
            side.Sort((a, b) => a.Offset != b.Offset ? a.Offset.CompareTo(b.Offset) : a.Index.CompareTo(b.Index));
            list.Clear();
            PolygonVertex? previous = null;
            int previousColumn = 0;
            foreach (Seg seg in side)
            {
                PolygonVertex[] chain = floors.SegChains[seg.Index];
                if (chain.Length < 2)
                {
                    if (seg.V1.X == seg.V2.X && seg.V1.Y == seg.V2.Y)
                        continue;
                    chain = new[] { new PolygonVertex(seg.V1.X, seg.V1.Y), new PolygonVertex(seg.V2.X, seg.V2.Y) };
                }
                int contrast = LightTables.FakeContrast(seg.V1.X, seg.V1.Y, seg.V2.X, seg.V2.Y);
                var columns = new int[chain.Length];
                for (int k = 0; k < chain.Length; k++)
                    columns[k] = Column(seg, chain[k]);
                if (previous is PolygonVertex p && p != chain[0])
                {
                    list.Add(new WallPiece(p, chain[0], previousColumn, columns[0], contrast, seg.Index));
                    list.Add(new WallPiece(chain[0], p, columns[0], previousColumn, contrast, seg.Index));
                    connectors += 2;
                }
                for (int k = 0; k + 1 < chain.Length; k++)
                    list.Add(new WallPiece(chain[k], chain[k + 1], columns[k], columns[k + 1], contrast, seg.Index));
                previous = chain[^1];
                previousColumn = columns[^1];
            }
            pieces[i] = list.ToArray();
        }

        // Where two sides with the same front sector meet at a vertex, the floor edges along
        // them can end at different points (each seg's own projection): bridge them.
        var starts = new List<(int Sector, PolygonVertex Start)>?[level.Vertexes.Length];
        foreach (Line line in level.Lines)
        {
            for (int side = 0; side < 2; side++)
            {
                WallPiece[] p = pieces[line.Index * 2 + side];
                if (p.Length > 0 && line.SideNum[side] >= 0)
                    (starts[(side == 0 ? line.V1 : line.V2).Index] ??= new()).Add((level.Sides[line.SideNum[side]].Sector.Index, p[0].A));
            }
        }
        foreach (Line line in level.Lines)
        {
            for (int side = 0; side < 2; side++)
            {
                WallPiece[] p = pieces[line.Index * 2 + side];
                if (p.Length == 0 || line.SideNum[side] < 0 || starts[(side == 0 ? line.V2 : line.V1).Index] is not { } there)
                    continue;
                int sector = level.Sides[line.SideNum[side]].Sector.Index;
                WallPiece last = p[^1];
                Seg seg = level.Segs[last.Seg];
                list.Clear();
                list.AddRange(p);
                foreach ((int otherSector, PolygonVertex start) in there)
                {
                    if (otherSector != sector || start == last.B || list.Exists(q => q.A == last.B && q.B == start))
                        continue;
                    int column = Column(seg, start);
                    list.Add(new WallPiece(last.B, start, last.ColumnB, column, last.Contrast, last.Seg));
                    list.Add(new WallPiece(start, last.B, column, last.ColumnB, last.Contrast, last.Seg));
                    connectors += 2;
                }
                if (list.Count != p.Length)
                    pieces[line.Index * 2 + side] = list.ToArray();
            }
        }
        return new WallPieces(pieces) { Connectors = connectors };
    }

    /// <summary>
    /// fixed_t: the texture column of point <paramref name="p"/> on
    /// <paramref name="seg"/> without the sidedef's offset: the seg's
    /// <c>offset</c> plus the distance of <paramref name="p"/>'s projection
    /// from the seg's start (rounded to the nearest fixed_t).
    /// </summary>
    public static int Column(Seg seg, PolygonVertex p)
    {
        long dx = (long)seg.V2.X - seg.V1.X, dy = (long)seg.V2.Y - seg.V1.Y;
        Int128 length = ISqrt((UInt128)((Int128)dx * dx + (Int128)dy * dy));
        if (length == 0)
            return seg.Offset;
        Int128 along = (Int128)((long)p.X - seg.V1.X) * dx + (Int128)((long)p.Y - seg.V1.Y) * dy;
        Int128 d = along >= 0 ? (along + length / 2) / length : -((-along + length / 2) / length);
        return (int)Math.Clamp((long)(seg.Offset + d), int.MinValue, int.MaxValue);
    }

    /// <summary>The integer square root of <paramref name="n"/> rounded to the nearest integer.</summary>
    public static Int128 ISqrt(UInt128 n)
    {
        if (n == 0)
            return 0;
        // Newton's method from a power of two above the root.
        int bits = 128 - (int)UInt128.LeadingZeroCount(n);
        UInt128 x = (UInt128)1 << ((bits + 1) / 2);
        while (true)
        {
            UInt128 y = (x + n / x) >> 1;
            if (y >= x)
                break;
            x = y;
        }
        // x = floor(sqrt(n)); round: x + 1 when n - x² > x (i.e. n >= (x + ½)²).
        return (Int128)(n - x * x > x ? x + 1 : x);
    }
}
