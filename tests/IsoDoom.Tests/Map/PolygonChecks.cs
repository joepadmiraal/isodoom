using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using IsoDoom.Map;
using IsoDoom.Tests.Support;
using Xunit;

namespace IsoDoom.Tests.Map;

/// <summary>
/// Checks for <see cref="SubsectorPolygons"/> (T2.2): sector areas against the
/// linedefs, and a scanline render that compares polygon coverage with the
/// sector the linedefs put each sample point in.
/// </summary>
public static class PolygonChecks
{
    private const double Unit = Fixed.FRACUNIT;

    /// <summary>
    /// The area of each sector in square map units, from its linedefs: the
    /// shoelace sum over the line sides facing it (front sides as drawn, back
    /// sides reversed), so lines with the sector on both sides cancel and holes
    /// subtract. Only meaningful for sectors whose sides close.
    /// </summary>
    public static double[] SectorAreasFromLines(Level map)
    {
        double[] twice = new double[map.Sectors.Length];
        foreach (Line l in map.Lines)
        {
            double x1 = l.V1.X / Unit, y1 = l.V1.Y / Unit, x2 = l.V2.X / Unit, y2 = l.V2.Y / Unit;
            double cross = x1 * y2 - x2 * y1;
            // Interior on the right (clockwise, y up) gives a negative shoelace sum.
            if (l.FrontSector is not null)
                twice[l.FrontSector.Index] -= cross;
            if (l.BackSector is not null)
                twice[l.BackSector.Index] += cross;
        }
        return [.. twice.Select(t => t / 2)];
    }

    /// <summary>Area of each sector's polygons (square map units).</summary>
    public static double[] SectorAreasFromPolygons(Level map, SubsectorPolygons polys) =>
        [.. polys.BySector.Select(ss => ss.Sum(i => PolygonArea(polys.Polygons[i])))];

    public static double PolygonArea(PolygonVertex[] p) => (double)SubsectorPolygons.TwiceArea(p) / 2 / Unit / Unit;

    /// <summary>Structural checks on every polygon: convex, clockwise, inside the start box, one per subsector.</summary>
    public static void CheckShapes(Level map, SubsectorPolygons polys)
    {
        Assert.Equal(map.Subsectors.Length, polys.Polygons.Count);
        Assert.Equal(map.Sectors.Length, polys.BySector.Count);
        var seen = new HashSet<int>();
        for (int s = 0; s < polys.BySector.Count; s++)
        {
            foreach (int i in polys.BySector[s])
            {
                Assert.True(seen.Add(i));
                Assert.Same(map.Sectors[s], map.Subsectors[i].Sector);
            }
        }
        for (int i = 0; i < polys.Polygons.Count; i++)
        {
            PolygonVertex[] p = polys.Polygons[i];
            if (p.Length == 0)
            {
                Assert.DoesNotContain(i, seen);
                continue;
            }
            Assert.Contains(i, seen);
            Assert.True(p.Length >= 3, $"subsector {i}: {p.Length} corners");
            Assert.True(SubsectorPolygons.TwiceArea(p) > 0, $"subsector {i} is not clockwise");
            for (int k = 0; k < p.Length; k++)
            {
                PolygonVertex a = p[k], b = p[(k + 1) % p.Length], c = p[(k + 2) % p.Length];
                Assert.NotEqual(a, b);
                // Clockwise: every turn is to the right or straight on. Rounding corners to
                // fixed_t and the builder's on-line epsilon may bend a nearly straight run by
                // a hair: b may lie up to ConvexTolerance left of the line a→c.
                Int128 turn = (Int128)(b.X - a.X) * (c.Y - b.Y) - (Int128)(b.Y - a.Y) * (c.X - b.X);
                double ac = Math.Sqrt(Sq(c.X - a.X) + Sq(c.Y - a.Y));
                Assert.True(turn <= 0 || (double)turn / ac <= ConvexTolerance * Unit,
                    $"subsector {i} is not convex at corner {(k + 1) % p.Length}: {a} {b} {c}");
            }
        }
    }

    private static double Sq(double v) => v * v;

    /// <summary>
    /// <see cref="Level.R_PointInSubsector"/> puts the centroid of every
    /// polygon (rounded to fixed_t) in that polygon's subsector, so the floor
    /// drawn is the floor the sim stands things on. Polygons thinner than a
    /// unit are skipped: vanilla's <c>R_PointOnSide</c> drops the partition
    /// direction's fraction bits, so it may round a point that close across.
    /// </summary>
    public static void CheckCentroidsInBsp(Level map, SubsectorPolygons polys)
    {
        var wrong = new List<string>();
        for (int i = 0; i < polys.Polygons.Count; i++)
        {
            PolygonVertex[] p = polys.Polygons[i];
            if (p.Length == 0)
                continue;
            double cx = 0, cy = 0, a2 = 0;
            for (int k = 0; k < p.Length; k++)
            {
                PolygonVertex u = p[k], v = p[(k + 1) % p.Length];
                double cross = (double)u.X / Unit * (v.Y / Unit) - (double)v.X / Unit * (u.Y / Unit);
                a2 += cross;
                cx += (u.X + v.X) / Unit * cross;
                cy += (u.Y + v.Y) / Unit * cross;
            }
            cx /= 3 * a2;
            cy /= 3 * a2;
            double perimeter = 0;
            for (int k = 0; k < p.Length; k++)
                perimeter += Math.Sqrt(Sq((p[(k + 1) % p.Length].X - p[k].X) / Unit) + Sq((p[(k + 1) % p.Length].Y - p[k].Y) / Unit));
            if (Math.Abs(a2) / perimeter < 1)
                continue; // thinner than about a unit
            Subsector bsp = map.R_PointInSubsector((int)Math.Round(cx * Unit), (int)Math.Round(cy * Unit));
            if (bsp.Index != i)
                wrong.Add($"subsector {i}: centroid ({cx:F2}, {cy:F2}) is in subsector {bsp.Index}");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    /// <summary>Map units a corner may stray to the outside of its neighbours' line (see <see cref="CheckShapes"/>).</summary>
    public const double ConvexTolerance = 1.0 / 128;

    /// <summary>
    /// Length (map units) of each sector's boundary: the lines with it on one
    /// side only. Node builders round split vertices to whole units, which
    /// moves subsector edges by up to about 0.7 units, so the polygon area may
    /// stray from the linedef area by about that times this length.
    /// </summary>
    public static double[] SectorBoundaryLengths(Level map)
    {
        double[] len = new double[map.Sectors.Length];
        foreach (Line l in map.Lines)
        {
            if (l.FrontSector == l.BackSector)
                continue;
            double d = Math.Sqrt(Sq(l.Dx / Unit) + Sq(l.Dy / Unit));
            if (l.FrontSector is not null)
                len[l.FrontSector.Index] += d;
            if (l.BackSector is not null)
                len[l.BackSector.Index] += d;
        }
        return len;
    }

    /// <summary>
    /// Whether each sector's boundary closes: directed with the sector on the
    /// right, every vertex has as many boundary edges leaving as arriving.
    /// The linedef area of an unclosed sector means nothing.
    /// </summary>
    public static bool[] SectorsClosed(Level map)
    {
        var balance = new Dictionary<(int sector, int vertex), int>();
        void Edge(Sector s, Vertex from, Vertex to)
        {
            balance[(s.Index, from.Index)] = balance.GetValueOrDefault((s.Index, from.Index)) + 1;
            balance[(s.Index, to.Index)] = balance.GetValueOrDefault((s.Index, to.Index)) - 1;
        }
        foreach (Line l in map.Lines)
        {
            if (l.FrontSector == l.BackSector)
                continue;
            if (l.FrontSector is not null)
                Edge(l.FrontSector, l.V1, l.V2);
            if (l.BackSector is not null)
                Edge(l.BackSector, l.V2, l.V1);
        }
        bool[] closed = [.. Enumerable.Repeat(true, map.Sectors.Length)];
        foreach (((int sector, int _), int b) in balance)
        {
            if (b != 0)
                closed[sector] = false;
        }
        return closed;
    }

    /// <summary>Area units per unit of sector boundary length allowed between polygon and linedef area.</summary>
    public const double BoundaryTolerance = 0.75;

    /// <summary>Area (square units) always allowed.</summary>
    public const double AbsTolerance = 16;

    /// <summary>A sector whose polygon area is off by more than the tolerance.</summary>
    public sealed record AreaMismatch(int Sector, double Polygons, double Lines, bool Closed, bool BspArtefact)
    {
        public override string ToString() =>
            $"sector {Sector}: polygons {Polygons:F1}, lines {Lines:F1} (diff {Math.Abs(Polygons - Lines):F1})" +
            (Closed ? "" : ", not closed") + (BspArtefact ? ", node artefact" : "");
    }

    /// <summary>
    /// Per-sector area comparison (tolerance: <see cref="AbsTolerance"/> plus
    /// <see cref="BoundaryTolerance"/> times the boundary length). Each
    /// mismatch says whether the sector is closed and whether
    /// <paramref name="coverage"/> found node artefacts involving it.
    /// </summary>
    public static List<AreaMismatch> AreaMismatches(Level map, SubsectorPolygons polys, Coverage coverage)
    {
        double[] fromLines = SectorAreasFromLines(map);
        double[] fromPolys = SectorAreasFromPolygons(map, polys);
        double[] boundary = SectorBoundaryLengths(map);
        bool[] closed = SectorsClosed(map);
        var bad = new List<AreaMismatch>();
        for (int s = 0; s < fromLines.Length; s++)
        {
            double diff = Math.Abs(fromPolys[s] - fromLines[s]);
            if (diff > AbsTolerance + BoundaryTolerance * boundary[s])
                bad.Add(new AreaMismatch(s, fromPolys[s], fromLines[s], closed[s], coverage.ArtefactSectors.Contains(s)));
        }
        return bad;
    }

    /// <summary>Result of <see cref="Render"/>: counts of sample points per outcome.</summary>
    public sealed record Coverage(
        int Samples, int Covered, int Gaps, int Overlaps, int WrongSector, int Spill, int NearLine,
        int Artefacts, int UnclosedGaps, IReadOnlySet<int> ArtefactSectors, IReadOnlySet<int> ArtefactSubsectors,
        string Png, IReadOnlyList<string> Notes)
    {
        public int Failures => Gaps + Overlaps + WrongSector + Spill;

        public override string ToString() =>
            $"{Samples} samples, {Covered} covered; gaps {Gaps}, overlaps {Overlaps}, wrong sector {WrongSector}, spill {Spill}; " +
            $"node artefacts {Artefacts} (subsectors {string.Join(", ", ArtefactSubsectors.Order())}; " +
            $"sectors {string.Join(", ", ArtefactSectors.Order())}); uncovered in unclosed sectors {UnclosedGaps}; " +
            $"ignored within {NearLineTolerance} units of a linedef: {NearLine}; {Png}" + Describe(Notes);
    }

    /// <summary>
    /// Mismatches this close to a linedef (map units) are ignored: seg lines
    /// run between rounded split vertices, and a seg along an edge the cell
    /// already has (within <see cref="SubsectorPolygons.SegSnapEpsilon"/>) does not clip.
    /// </summary>
    public const double NearLineTolerance = 2.5;

    /// <summary>
    /// Renders the polygons into a grid of sample points (one per pixel,
    /// <paramref name="step"/> map units apart) and compares each with the
    /// sector the linedefs put it in: the side facing it of the first line a
    /// ray towards +x crosses (none, or a missing side: the void).
    /// <list type="bullet">
    /// <item>No polygon where the lines say there is a sector: a <b>gap</b> (red),
    /// unless that sector does not close (<see cref="SectorsClosed"/>; dark red),
    /// since the ray test means nothing there.</item>
    /// <item>More than one polygon: an <b>overlap</b> (white).</item>
    /// <item>A polygon of another sector (magenta) or in the void (yellow): a
    /// <b>node artefact</b> when <see cref="Level.R_PointInSubsector"/> puts the
    /// point in that very subsector too (the node builder's leaf really covers
    /// it, so the sim agrees; drawn darker), otherwise a <b>wrong sector</b> or
    /// <b>spill</b> failure.</item>
    /// </list>
    /// Mismatches within <see cref="NearLineTolerance"/> of a linedef are drawn
    /// grey and not counted. Writes a debug PNG to <c>TestResults/maps/</c>
    /// (gitignored; WAD-derived): sectors in hashed colours, void black, at
    /// most <paramref name="maxImageSize"/> pixels a side (a pixel shows the
    /// worst sample of its block); <paramref name="drawLines"/> adds the
    /// linedefs in blue.
    /// </summary>
    public static Coverage Render(Level map, SubsectorPolygons polys, string file, double step = 2,
        (double minX, double minY, double maxX, double maxY)? window = null, bool drawLines = false, int maxImageSize = 2048)
    {
        double minX = map.Vertexes.Min(v => v.X) / Unit - 8, maxX = map.Vertexes.Max(v => v.X) / Unit + 8;
        double minY = map.Vertexes.Min(v => v.Y) / Unit - 8, maxY = map.Vertexes.Max(v => v.Y) / Unit + 8;
        if (window is { } win)
            (minX, minY, maxX, maxY) = win;
        int w = (int)Math.Ceiling((maxX - minX) / step), h = (int)Math.Ceiling((maxY - minY) / step);

        (Line l, double x1, double y1, double x2, double y2)[] lines = [.. map.Lines.Select(l => (l, x1: l.V1.X / Unit, y1: l.V1.Y / Unit, x2: l.V2.X / Unit, y2: l.V2.Y / Unit))];
        var shapes = new List<(int ss, double[] xs, double[] ys, double y0, double y1)>();
        foreach (int[] group in polys.BySector)
        {
            foreach (int i in group)
            {
                PolygonVertex[] p = polys.Polygons[i];
                double[] xs = [.. p.Select(v => v.X / Unit)], ys = [.. p.Select(v => v.Y / Unit)];
                shapes.Add((i, xs, ys, ys.Min(), ys.Max()));
            }
        }

        var notes = new List<string>();
        void Note(string kind, double x, double y, int expected, int ss)
        {
            if (notes.Count >= 12)
                return;
            Subsector bsp = map.R_PointInSubsector((int)Math.Round(x * Unit), (int)Math.Round(y * Unit));
            notes.Add($"{kind} at ({x:F1}, {y:F1}): lines say sector {expected}, polygon of subsector {ss}, " +
                $"BSP says subsector {bsp.Index} (sector {bsp.Sector.Index})");
        }

        // The image shrinks by a whole factor to fit maxImageSize; each pixel shows the
        // most important sample of its block (gap > overlap > other failure > artefact >
        // ignored > fine), so a one-sample gap still shows.
        int shrink = Math.Max(1, (w + maxImageSize - 1) / maxImageSize);
        shrink = Math.Max(shrink, (h + maxImageSize - 1) / maxImageSize);
        int iw = (w + shrink - 1) / shrink, ih = (h + shrink - 1) / shrink;
        byte[] rgba = new byte[iw * ih * 4];
        byte[] rank = new byte[iw * ih];
        void Plot(int i, int j, (byte r, byte g, byte b) colour, byte priority)
        {
            int p = (j / shrink) * iw + i / shrink;
            if (rank[p] != 0 && rank[p] >= priority)
                return;
            rank[p] = priority;
            (rgba[p * 4], rgba[p * 4 + 1], rgba[p * 4 + 2], rgba[p * 4 + 3]) = (colour.r, colour.g, colour.b, 255);
        }

        int samples = 0, covered = 0, gaps = 0, overlaps = 0, wrong = 0, spill = 0, near = 0, artefacts = 0, unclosedGaps = 0;
        bool[] closed = SectorsClosed(map);
        var artefactSectors = new HashSet<int>();
        var artefactSubsectors = new HashSet<int>();
        int[] count = new int[w];
        int[] ssAt = new int[w];
        var crossings = new List<(double x, int sector)>();
        for (int j = 0; j < h; j++)
        {
            // Odd offsets keep sample rows off vertexes and axis-aligned lines.
            double y = maxY - (j + 0.5) * step + 0.0137;
            crossings.Clear();
            foreach ((Line? l, double x1, double y1, double x2, double y2) in lines)
            {
                if ((y1 <= y) == (y2 <= y))
                    continue;
                double x = x1 + (y - y1) * (x2 - x1) / (y2 - y1);
                // Seen from the west, a line going down (south) shows its front (right) side.
                Sector? facing = y2 < y1 ? l.FrontSector : l.BackSector;
                crossings.Add((x, facing?.Index ?? -1));
            }
            crossings.Sort((a, b) => a.x.CompareTo(b.x));

            Array.Clear(count);
            Array.Fill(ssAt, -1);
            foreach ((int ss, double[]? xs, double[]? ys, double y0, double y1) in shapes)
            {
                if (y < y0 || y >= y1)
                    continue;
                double lo = double.MaxValue, hi = double.MinValue;
                for (int k = 0; k < xs.Length; k++)
                {
                    int n = (k + 1) % xs.Length;
                    if ((ys[k] <= y) == (ys[n] <= y))
                        continue;
                    double x = xs[k] + (y - ys[k]) * (xs[n] - xs[k]) / (ys[n] - ys[k]);
                    lo = Math.Min(lo, x);
                    hi = Math.Max(hi, x);
                }
                if (lo > hi)
                    continue;
                // Pixel i samples x = minX + (i + 0.5) * step + 0.0071; take lo <= x < hi.
                int first = Math.Max(0, (int)Math.Ceiling((lo - minX - 0.0071) / step - 0.5));
                for (int i = first; i < w; i++)
                {
                    double x = minX + (i + 0.5) * step + 0.0071;
                    if (x >= hi)
                        break;
                    if (x < lo)
                        continue;
                    count[i]++;
                    ssAt[i] = ss;
                }
            }

            int c = 0;
            for (int i = 0; i < w; i++)
            {
                double x = minX + (i + 0.5) * step + 0.0071;
                while (c < crossings.Count && crossings[c].x <= x)
                    c++;
                int expected = c < crossings.Count ? crossings[c].sector : -1;
                int ss = ssAt[i];
                int sector = ss < 0 ? -1 : map.Subsectors[ss].Sector.Index;
                samples++;
                (byte r, byte g, byte b) colour = count[i] == 0 ? ((byte)0, (byte)0, (byte)0) : SectorColour(sector);
                byte priority = 1;
                if (count[i] > 1)
                {
                    colour = (255, 255, 255);
                    if (!IsNearLine(lines, x, y)) { overlaps++; priority = 5; Note("overlap", x, y, expected, ss); } else { near++; priority = 2; colour = NearLineColour; }
                }
                else if (count[i] == 0 && expected != -1)
                {
                    colour = (255, 0, 0);
                    if (IsNearLine(lines, x, y)) { near++; priority = 2; colour = NearLineColour; }
                    else if (!closed[expected]) { unclosedGaps++; colour = (128, 0, 0); priority = 3; }
                    else { gaps++; priority = 6; Note("gap", x, y, expected, ss); }
                }
                else if (count[i] == 1 && sector != expected)
                {
                    colour = expected == -1 ? ((byte)255, (byte)255, (byte)0) : ((byte)255, (byte)0, (byte)255);
                    priority = 4;
                    if (IsNearLine(lines, x, y))
                    {
                        near++;
                        priority = 2;
                        colour = NearLineColour;
                    }
                    else if (map.R_PointInSubsector((int)Math.Round(x * Unit), (int)Math.Round(y * Unit)).Index == ss)
                    {
                        artefacts++;
                        artefactSubsectors.Add(ss);
                        artefactSectors.Add(sector);
                        if (expected != -1)
                            artefactSectors.Add(expected);
                        colour = ((byte)(colour.r / 2), (byte)(colour.g / 2), (byte)(colour.b / 2));
                        priority = 3;
                    }
                    else if (expected == -1)
                    {
                        spill++;
                        Note("spill", x, y, expected, ss);
                    }
                    else
                    {
                        wrong++;
                        Note("wrong sector", x, y, expected, ss);
                    }
                }
                else if (count[i] == 1)
                    covered++;
                Plot(i, j, colour, priority);
            }
        }

        if (drawLines)
        {
            foreach ((Line _, double x1, double y1, double x2, double y2) in lines)
            {
                int n = (int)(Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1)) / step * 2) + 1;
                for (int k = 0; k <= n; k++)
                {
                    int i = (int)((x1 + (x2 - x1) * k / n - minX) / step), j = (int)((maxY - (y1 + (y2 - y1) * k / n)) / step);
                    if (i >= 0 && i < w && j >= 0 && j < h)
                        Plot(i, j, (0, 160, 255), 7);
                }
            }
        }

        Assert.SkipWhen(TestWads.RepoRoot is null, "Repo root not found; nowhere to write the debug render.");
        string dir = Path.Combine(TestWads.RepoRoot!, "TestResults", "maps");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, file);
        PngWriter.WriteRgba(path, iw, ih, rgba);
        return new Coverage(samples, covered, gaps, overlaps, wrong, spill, near, artefacts, unclosedGaps, artefactSectors, artefactSubsectors, path, notes);
    }

    private static bool IsNearLine((Line l, double x1, double y1, double x2, double y2)[] lines, double x, double y)
    {
        foreach ((Line _, double x1, double y1, double x2, double y2) in lines)
        {
            double dx = x2 - x1, dy = y2 - y1;
            double t = Math.Clamp(((x - x1) * dx + (y - y1) * dy) / (dx * dx + dy * dy), 0, 1);
            if (Sq(x1 + t * dx - x) + Sq(y1 + t * dy - y) <= NearLineTolerance * NearLineTolerance)
                return true;
        }
        return false;
    }

    // Ignored mismatches next to a linedef.
    private static readonly (byte, byte, byte) NearLineColour = (90, 90, 90);

    private static (byte, byte, byte) SectorColour(int sector)
    {
        uint hsh = (uint)sector * 2654435761u;
        // Mid-range channels: away from black (void) and the saturated failure colours.
        return ((byte)(60 + (hsh & 0x7F)), (byte)(60 + ((hsh >> 8) & 0x9F)), (byte)(60 + ((hsh >> 16) & 0x9F)));
    }

    /// <summary>A short report of the worst area differences, for failure messages.</summary>
    public static string Describe(IEnumerable<string> lines, int max = 20)
    {
        var sb = new StringBuilder();
        foreach (string l in lines.Take(max))
            sb.Append("\n  ").Append(l);
        return sb.ToString();
    }
}
