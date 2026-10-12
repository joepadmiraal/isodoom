using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Map;
using IsoDoom.Render;

namespace IsoDoom.Game;

/// <summary>
/// The level check's height cue part (T6.13s, SPEC §12). On every map: every
/// ledge edge lies on a two-sided line's side, its sectors the line's, and the
/// mesh holds its triangles (<see cref="LevelMesh.LedgeTriangleCount"/>; the
/// vertices themselves are checked with the chunks); the edges the sim's
/// heights show at load (<see cref="LedgeEdges.Shows"/>) are counted. With a
/// real renderer: the longest edge shown, straight down at 1 unit per pixel,
/// with the cues off, edges, shade and both: its pixels show palette index
/// <see cref="LedgeEdges.ColourIndex"/> (its innermost <see cref="LedgeEdges.Rim"/>
/// units index 0) lit at <see cref="LedgeEdges.Brighten"/> light levels above
/// its floor's (at least <see cref="LedgeEdges.MinLight"/>), and the floor with
/// the cues off; pixels outside every shown edge do not change with the edges
/// on; and with the shading, a height reference 48 units below (then above)
/// the edge's floor, every floor around it is lit <see cref="LedgeEdges.ShadeLevels"/>
/// light levels brighter (or darker): 2 for the edge's floor.
/// </summary>
public partial class LevelCheck
{
    private int _ledgeMaps, _ledgeSides, _ledgesShown;

    /// <summary>A floor's height as the shader has it (its data texel), map units.</summary>
    private static double DataFloor(LevelMesh m, int sector) => m.SectorData(sector).R;

    /// <summary>A sector's light level as the shader has it (its data texel), 0–255.</summary>
    private static int DataLight(LevelMesh m, int sector) => Math.Clamp((int)(m.SectorData(sector).B + 0.5f), 0, 255);

    /// <summary>
    /// Every ledge edge of <paramref name="map"/> on a side of a two-sided line
    /// between two sectors, with its sectors; every such side with floor along
    /// it has one; the mesh's triangle count; the edges shown at load printed.
    /// </summary>
    private void CheckLedgeData(LevelMesh m, string map)
    {
        Level level = m.Level;
        var sides = new HashSet<(int Line, int Side)>();
        foreach (LedgeEdge edge in m.Ledges.Edges)
        {
            string what = $"{map}: line {edge.Line} side {edge.Side}'s ledge edge";
            Line line = level.Lines[edge.Line];
            Sector? sector = edge.Side == 0 ? line.FrontSector : line.BackSector, other = edge.Side == 0 ? line.BackSector : line.FrontSector;
            if (sector is null || other is null || sector == other)
                Fail($"{what}: not on a two-sided line between two sectors");
            else if (edge.Sector != sector.Index || edge.Other != other.Index)
                Fail($"{what}: on sector {edge.Sector} across from {edge.Other}, expected {sector.Index} across from {other.Index}");
            if (!sides.Add((edge.Line, edge.Side)))
                Fail($"{what}: twice");
            if (edge.Pieces.Count == 0)
                Fail($"{what}: no piece");
        }
        int twoSided = level.Lines.Count(l => l.FrontSector is not null && l.BackSector is not null && l.FrontSector != l.BackSector
            && (l.V1.X != l.V2.X || l.V1.Y != l.V2.Y));
        if (sides.Count > 2 * twoSided)
            Fail($"{map}: {sides.Count} ledge edges for {twoSided} two-sided lines");
        int triangles = m.Ledges.Edges.Sum(e => e.Pieces.Sum(p => p.Length - 2));
        if (triangles != m.LedgeTriangleCount)
            Fail($"{map}: {m.LedgeTriangleCount} ledge edge triangles, {triangles} expected");
        int shown = 0;
        if (_scene.World is { } world)
        {
            foreach (LedgeEdge edge in m.Ledges.Edges)
            {
                if (LedgeEdges.Shows(world.sectors[edge.Sector].floorheight / 65536.0, world.sectors[edge.Other].floorheight / 65536.0))
                    shown++;
                if (LedgeEdges.Shows(DataFloor(m, edge.Sector), DataFloor(m, edge.Other))
                    != LedgeEdges.Shows(world.sectors[edge.Sector].floorheight / 65536.0, world.sectors[edge.Other].floorheight / 65536.0))
                    Fail($"{map}: line {edge.Line} side {edge.Side}'s ledge edge: the data texels and the sim disagree whether it shows");
            }
        }
        else
            Fail($"{map}: no world for the ledge edges' heights");
        _ledgeMaps++;
        _ledgeSides += sides.Count;
        _ledgesShown += shown;
        GD.Print($"Level check: {map}: {sides.Count} ledge edges ({triangles} triangles) on {twoSided} two-sided lines, {shown} shown at load (drops over {LedgeEdges.MinDrop})");
    }

    private void PrintLedges() =>
        GD.Print($"Level check: height cues (T6.13s): {_ledgeMaps} maps, {_ledgeSides} ledge edges on the sides of two-sided lines, "
            + $"{_ledgesShown} of them shown at load (their floor more than {LedgeEdges.MinDrop} above the floor across)");

    /// <summary>The render part: the longest edge shown, straight down, with every cue mode.</summary>
    private async Task LedgeCheck(LevelMesh m)
    {
        string map = m.Level.Name;
        HeightCueMode mode = m.HeightCues;
        float? reference = m.HeightReference;
        m.SetCutaway(new CutawaySettings { Style = CutawayStyle.Off });
        static double Length(LedgeEdge e) => e.Pieces.Sum(Area) / LedgeEdges.Width;
        LedgeEdge? edge = m.Ledges.Edges.Where(e => LedgeEdges.Shows(DataFloor(m, e.Sector), DataFloor(m, e.Other)) && !m.Lids.Has(e.Sector))
            .MaxBy(Length);
        if (edge is null)
        {
            GD.Print($"Level check: {map}: no ledge edge shown to view");
            return;
        }
        string what = $"{map}: line {edge.Line} side {edge.Side}'s ledge edge (sector {edge.Sector}, {DataFloor(m, edge.Sector) - DataFloor(m, edge.Other)} above sector {edge.Other})";
        await CompareLedgeTopDown(m, edge, what);
        m.SetHeightCues(mode);
        m.SetHeightReference(reference);
    }

    private async Task CompareLedgeTopDown(LevelMesh m, LedgeEdge edge, string what)
    {
        Vector2I size = ViewSize();
        int w = size.X, h = size.Y;
        double minX = edge.Pieces.Min(p => p.Min(v => v.X)), maxX = edge.Pieces.Max(p => p.Max(v => v.X));
        double minY = edge.Pieces.Min(p => p.Min(v => v.Y)), maxY = edge.Pieces.Max(p => p.Max(v => v.Y));
        int left = (int)Math.Floor((minX + maxX) / 2) - w / 2, top = (int)Math.Floor((minY + maxY) / 2) + h / 2;
        (int lowest, int highest) = HeightRange(m.Level);
        float cameraZ = highest + 64.5f;
        var basis = new Basis(new Vector3(1, 0, 0), new Vector3(0, 0, -1), new Vector3(0, 1, 0));
        Ortho(basis, new Vector3(left + w / 2f, top - h / 2f, cameraZ), 1, cameraZ - lowest + 64);
        double floor = DataFloor(m, edge.Sector);
        m.SetHeightReference(null);
        m.SetHeightCues(HeightCueMode.Off);
        byte[]? off = await Capture($"{what}, top-down, height cues off");
        m.SetHeightCues(HeightCueMode.Edges);
        byte[]? on = await Capture($"{what}, top-down, edges");
        m.SetHeightReference((float)floor - 48);
        m.SetHeightCues(HeightCueMode.Both);
        byte[]? both = await Capture($"{what}, top-down, both, the reference 48 below");
        m.SetHeightCues(HeightCueMode.Shade);
        byte[]? below = await Capture($"{what}, top-down, shade, the reference 48 below");
        m.SetHeightReference((float)floor + 48);
        byte[]? above = await Capture($"{what}, top-down, shade, the reference 48 above");
        m.SetHeightReference(null);
        byte[]? noReference = await Capture($"{what}, top-down, shade, no reference");
        m.SetHeightCues(HeightCueMode.Off);
        if (off is null || on is null || both is null || below is null || above is null || noReference is null)
            return;

        // The edges the shader draws, near this one.
        var near = m.Ledges.Edges.Where(e => e != edge && LedgeEdges.Shows(DataFloor(m, e.Sector), DataFloor(m, e.Other))
            && e.Pieces.Any(p => p.Any(v => v.X > minX - 64 && v.X < maxX + 64 && v.Y > minY - 64 && v.Y < maxY + 64))).ToList();
        int light = DataLight(m, edge.Sector);
        int edgeLight = Math.Max((light >> LightTables.LIGHTSEGSHIFT) + LedgeEdges.Brighten, LedgeEdges.MinLight);
        Sector sector = m.Level.Sectors[edge.Sector];
        const double Margin = 0.1;
        (double, double)[] samples = [(0, 0), (Margin, Margin), (-Margin, Margin), (Margin, -Margin), (-Margin, -Margin)];
        int edgePixels = 0, rimPixels = 0, kept = 0, badEdge = 0, badBoth = 0, badOff = 0, badKeep = 0;
        string firstEdge = "", firstBoth = "", firstOff = "", firstKeep = "";
        for (int py = Math.Max(0, top - (int)maxY - 16); py < Math.Min(h, top - (int)minY + 16); py++)
        {
            for (int px = Math.Max(0, (int)minX - 16 - left); px < Math.Min(w, (int)maxX + 16 - left); px++)
            {
                double x = left + px + 0.5, y = top - py - 0.5;
                int inThis = 0, inOther = 0;
                foreach ((double ox, double oy) in samples)
                {
                    if (edge.Pieces.Any(p => InConvex(p, x + ox, y + oy)))
                        inThis++;
                    if (near.Any(e => e.Pieces.Any(p => InConvex(p, x + ox, y + oy))))
                        inOther++;
                }
                int p = (py * w + px) * 4;
                (int R, int G, int B) Pixel(byte[] frame) => (frame[p], frame[p + 1], frame[p + 2]);
                if (inThis == 0 && inOther == 0)
                {
                    kept++;
                    if (Pixel(on) != Pixel(off) && badKeep++ == 0)
                        firstKeep = $"pixel ({px}, {py}), map ({x}, {y}): drew {Pixel(on)}, {Pixel(off)} with the cues off";
                    continue;
                }
                if (inThis != samples.Length || inOther != 0)
                    continue;
                double inside = edge.Inside(x, y);
                if (Math.Abs(inside - (LedgeEdges.Width - LedgeEdges.Rim)) < Margin)
                    continue;
                bool rim = inside >= LedgeEdges.Width - LedgeEdges.Rim;
                byte index = (byte)(rim ? 0 : LedgeEdges.ColourIndex);
                double depth = cameraZ - (floor + LedgeEdges.Lift);
                int colormap = ExpectedColormap(m, false, edgeLight << LightTables.LIGHTSEGSHIFT, 0, x, y, depth);
                int shaded = ExpectedColormap(m, false, LedgeEdges.ShadeLight(edgeLight, floor, floor - 48) << LightTables.LIGHTSEGSHIFT, 0, x, y, depth);
                int floorColormap = ExpectedColormap(m, false, light, 0, x, y, cameraZ - floor);
                if (colormap < 0 || shaded < 0 || floorColormap < 0)
                    continue;
                (int col, int row) = TextureWrap.FlatTexel((int)Math.Round(x * 65536), (int)Math.Round(y * 65536));
                (int R, int G, int B) floorColour = Shade(ShownFlat(m, sector.FloorPic)[col, row], floorColormap);
                if (rim)
                    rimPixels++;
                else
                    edgePixels++;
                if (Pixel(on) != Shade(index, colormap) && badEdge++ == 0)
                    firstEdge = $"pixel ({px}, {py}), map ({x}, {y}), {inside:F2} in: drew {Pixel(on)}, expected index {index} at colormap {colormap}: {Shade(index, colormap)}";
                if (Pixel(both) != Shade(index, shaded) && badBoth++ == 0)
                    firstBoth = $"pixel ({px}, {py}), map ({x}, {y}), {inside:F2} in: drew {Pixel(both)}, expected index {index} at colormap {shaded}: {Shade(index, shaded)}";
                if (Pixel(off) != floorColour && badOff++ == 0)
                    firstOff = $"pixel ({px}, {py}), map ({x}, {y}): drew {Pixel(off)}, expected the floor {floorColour}";
            }
        }
        _pixels += edgePixels + rimPixels + kept;
        if (badEdge > 0)
            Fail($"{what}, top-down: {badEdge} of {edgePixels + rimPixels} edge pixel(s) differ, first at {firstEdge}");
        if (badBoth > 0)
            Fail($"{what}, top-down, both: {badBoth} of {edgePixels + rimPixels} edge pixel(s) differ, first at {firstBoth}");
        if (badOff > 0)
            Fail($"{what}, top-down, cues off: {badOff} of {edgePixels + rimPixels} pixel(s) under the edge do not show the floor, first at {firstOff}");
        if (badKeep > 0)
            Fail($"{what}, top-down: {badKeep} pixel(s) outside the shown edges changed with the edges on, first at {firstKeep}");
        if (edgePixels < 20 || rimPixels < 10)
            Fail($"{what}, top-down: only {edgePixels} bright and {rimPixels} rim pixels compared (at least 20 and 10 expected)");

        // The shading: every floor pixel around the edge, its sector's light shifted by its height against the reference.
        int[] grid = RasterFloors(m, left, top, w, h);
        bool[] uniform = Uniform(grid, w, h, 2);
        int shadePixels = 0, ownPixels = 0, badShade = 0;
        string firstShade = "";
        for (int py = Math.Max(0, top - (int)maxY - 64); py < Math.Min(h, top - (int)minY + 64); py++)
        {
            for (int px = Math.Max(0, (int)minX - 64 - left); px < Math.Min(w, (int)maxX + 64 - left); px++)
            {
                int i = py * w + px;
                if (!uniform[i] || grid[i] < 0)
                    continue;
                int s = grid[i];
                double x = left + px + 0.5, y = top - py - 0.5, height = DataFloor(m, s);
                (int col, int row) = TextureWrap.FlatTexel((int)Math.Round(x * 65536), (int)Math.Round(y * 65536));
                byte texel = ShownFlat(m, m.Level.Sectors[s].FloorPic)[col, row];
                int level = DataLight(m, s) >> LightTables.LIGHTSEGSHIFT;
                int p = i * 4;
                foreach ((byte[] frame, double? r, string name) in new[] { (below, (double?)(floor - 48), "48 below"), (above, floor + 48, "48 above"), (noReference, null, "none") })
                {
                    int shaded = r is double rv ? LedgeEdges.ShadeLight(level, height, rv) : level;
                    int colormap = ExpectedColormap(m, false, shaded << LightTables.LIGHTSEGSHIFT, 0, x, y, cameraZ - height);
                    if (colormap < 0)
                        continue;
                    shadePixels++;
                    if (s == edge.Sector)
                        ownPixels++;
                    (int R, int G, int B) expected = Shade(texel, colormap), got = (frame[p], frame[p + 1], frame[p + 2]);
                    if (got != expected && badShade++ == 0)
                        firstShade = $"sector {s} (floor {height}, light {level}) at pixel ({px}, {py}), map ({x}, {y}), reference {name}: drew {got}, expected light {shaded} at colormap {colormap}: {expected}";
                }
            }
        }
        _pixels += shadePixels;
        if (badShade > 0)
            Fail($"{what}, top-down, shade: {badShade} of {shadePixels} floor pixel(s) differ, first at {firstShade}");
        if (ownPixels < 150)
            Fail($"{what}, top-down, shade: only {ownPixels} pixels of the edge's floor compared (at least 150 expected)");
        GD.Print($"Level check: {what}, top-down: {edgePixels} bright and {rimPixels} rim pixels against the CPU (edges and both), the floor with the cues off, "
            + $"{kept} pixels around them as with the cues off; shade: {shadePixels} floor pixels at their height against a reference 48 below, 48 above and none "
            + $"({ownPixels} of the edge's floor, {LedgeEdges.ShadeLevels(floor, floor - 48)} and {LedgeEdges.ShadeLevels(floor, floor + 48)} levels)");
    }
}
