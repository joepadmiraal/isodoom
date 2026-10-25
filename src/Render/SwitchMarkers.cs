using System;
using System.Collections.Generic;
using IsoDoom.Map;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Render;

/// <summary>Whether the switches on walls facing away from the camera get a marker on the floor (T6.13p, SPEC §12; a presentation option). The shader's <c>switch_markers</c>.</summary>
public enum SwitchMarkerMode
{
    /// <summary>No markers: vanilla, a switch on a wall facing away is never seen.</summary>
    Off = 0,

    /// <summary>Markers on (the default).</summary>
    On = 1,
}

/// <summary>
/// One switch side's marker (T6.13p): the pieces of the strip of floor in
/// front of it, and what places and textures them. Map units, y up.
/// </summary>
/// <param name="Section">The switch's wall section (its sidedef, part, sectors and the planes its span is between).</param>
/// <param name="X">The side's start (the section's <c>V1</c>), map units.</param>
/// <param name="Y">The side's start (the section's <c>V1</c>), map units.</param>
/// <param name="Dx">The unit direction from the section's <c>V1</c> to <c>V2</c>.</param>
/// <param name="Dy">The unit direction from the section's <c>V1</c> to <c>V2</c>.</param>
/// <param name="Length">The side's length, map units.</param>
/// <param name="Rows">The texture rows the marker shows (<see cref="SwitchMarkers.Band"/>): the far edge shows <c>Top</c>, the wall's foot the row above <c>Bottom</c>.</param>
/// <param name="Pieces">Convex polygons, clockwise (interior on the right of each edge, as the floors), on the side's front sector's floor within <see cref="Depth"/> in front of the wall; for a <paramref name="Top"/> marker on its back sector's floor within <see cref="Depth"/> behind it.</param>
/// <param name="Top">A top marker (T10.6a): a lower switch's on the top of the raised sector behind its wall (<see cref="SwitchMarkers.Tops"/>).</param>
public sealed record SwitchMarker(WallSection Section, double X, double Y, double Dx, double Dy, double Length, (int Top, int Bottom) Rows,
    IReadOnlyList<(double X, double Y)[]> Pieces, bool Top = false)
{
    /// <summary>The sector whose floor the marker lies on: the side's, or a top marker's the one behind the wall.</summary>
    public int Sector => Top ? Section.BackSector!.Index : Section.FrontSector.Index;

    /// <summary>How far the marker reaches in front of the wall, map units: one unit per texture row.</summary>
    public int Depth => Rows.Bottom - Rows.Top;

    /// <summary>The unit normal into the front sector (the side's right), map axes.</summary>
    public (double X, double Y) Normal => (Dy, -Dx);

    /// <summary>How far <paramref name="x"/>, <paramref name="y"/> lies along the side from its start.</summary>
    public double Along(double x, double y) => (x - X) * Dx + (y - Y) * Dy;

    /// <summary>How far <paramref name="x"/>, <paramref name="y"/> lies in front of the wall, into its sector.</summary>
    public double InFront(double x, double y) => (x - X) * Dy - (y - Y) * Dx;

    /// <summary>
    /// The texture column at <paramref name="x"/>, <paramref name="y"/> without the sidedef's offset:
    /// the wall's columns mirrored about the side's middle, so the camera, which
    /// sees the marker from behind the wall, reads the texture as a player in front of it does.
    /// </summary>
    public double Column(double x, double y) => Length - Along(x, y);

    /// <summary>
    /// The texture row at <paramref name="x"/>, <paramref name="y"/>: the wall laid back onto the floor, its rows going up away from the wall;
    /// a top marker's, the wall folded back over its top edge onto the top, its rows going down into the block from <c>Rows.Top</c> at the edge.
    /// </summary>
    public double Row(double x, double y) => Top ? Rows.Top - InFront(x, y) : Rows.Bottom - InFront(x, y);
}

/// <summary>
/// The switch markers (T6.13p, SPEC §12): the fixed game camera looks
/// north-west, so a switch on a wall facing north or west is never drawn (its
/// face points away and is culled). Each switch side gets a marker, a decal
/// on the floor in front of it showing the switch's texture (its current one,
/// so a pressed switch shows its other texture), which the shader draws only
/// while the wall faces away from the camera (<see cref="Shows"/>). Pure C#,
/// no Godot types (the tests link it); <c>LevelMesh</c> builds the marker
/// triangles from it and the shader places them on the floor.
/// <para>
/// <b>Which sides:</b> every drawn wall section (an upper, lower or middle
/// texture) whose texture is one of the switch textures given (p_switch.c
/// <c>alphSwitchList</c>'s for the game), with or without a special: it looks
/// like a switch to a player in front of it. A side with more than one takes
/// the first of its upper, middle and lower texture, the order of
/// p_switch.c <c>P_ChangeSwitchTexture</c>'s tests.
/// </para>
/// <para>
/// <b>Shape:</b> the strip along the whole side, as deep as the switch's
/// rows (<see cref="Band"/>: the rows where the pair's two textures differ,
/// at least <see cref="MinDepth"/> and at most <see cref="MaxDepth"/>), one
/// unit per texel as on the wall, clipped to the side's sector's floor
/// (its subsector polygons), so a marker never covers another floor or the void.
/// </para>
/// </summary>
public sealed class SwitchMarkers
{
    /// <summary>The shallowest marker, map units (texture rows): a switch shorter than this shows rows around it.</summary>
    public const int MinDepth = 16;

    /// <summary>The deepest marker, map units (texture rows): a taller switch shows its middle rows.</summary>
    public const int MaxDepth = 32;

    /// <summary>The height above the floor (map units), against z-fighting: the shader's <c>SWITCH_MARKER_LIFT</c>.</summary>
    public const float Lift = 1f;

    // A wall whose normal points towards the camera by less than this (its dot product with the direction towards the camera) counts as edge-on.
    private const double Parallel = 1e-3;

    // Pieces smaller than this (square map units) are dropped.
    private const double MinArea = 1e-3;

    private SwitchMarkers(List<SwitchMarker> markers, List<SwitchMarker> tops) => (Markers, Tops) = (markers, tops);

    /// <summary>The markers on the floor in front of each switch, in line order, front side first (only sides with a piece left).</summary>
    public IReadOnlyList<SwitchMarker> Markers { get; }

    /// <summary>
    /// The top markers (T10.6a, SPEC §12), in line order: a switch on a lower
    /// texture (a riser, a counter's or a computer block's face) also gets the
    /// same strip mirrored to its wall's other side, on the raised sector's floor
    /// behind it (clipped to it), read as the wall folded back over its top edge
    /// (<see cref="SwitchMarker.Row"/>), since the raised sector hides the floor
    /// marker from the camera's side. The shader shows one while the rise hides
    /// more than half its floor marker (and places it on the sector's lid where
    /// that shows).
    /// </summary>
    public IReadOnlyList<SwitchMarker> Tops { get; }

    /// <summary>Every marker: <see cref="Markers"/>, then <see cref="Tops"/>, the order of the level mesh's.</summary>
    public IEnumerable<SwitchMarker> All
    {
        get
        {
            foreach (SwitchMarker k in Markers)
                yield return k;
            foreach (SwitchMarker k in Tops)
                yield return k;
        }
    }

    /// <summary>
    /// Whether a marker shows for its wall seen with the direction towards the
    /// camera <paramref name="toCameraX"/>, <paramref name="toCameraY"/> (map
    /// axes, flattened, at most a unit long): the shader's test. Unless the wall faces the camera
    /// (whose quad is drawn then): facing away, edge-on, or seen straight down.
    /// </summary>
    public static bool Shows(SwitchMarker marker, double toCameraX, double toCameraY) =>
        marker.Normal.X * toCameraX + marker.Normal.Y * toCameraY <= Parallel;

    /// <summary>
    /// Whether a top marker (<see cref="Tops"/>, T10.6a) shows, the shader's
    /// test: its wall does not face the camera (<see cref="Shows"/>) and the
    /// raised top, <paramref name="rise"/> above the switch's floor, hides
    /// more than half of its floor marker's depth from a camera in the unit
    /// direction <paramref name="toCameraX"/>, <paramref name="toCameraY"/>,
    /// <paramref name="toCameraZ"/> (map axes, z up).
    /// </summary>
    public static bool TopShows(SwitchMarker marker, double rise, double toCameraX, double toCameraY, double toCameraZ) =>
        Shows(marker, toCameraX, toCameraY) && Hidden(marker, rise, toCameraX, toCameraY, toCameraZ) > 0.5 * marker.Depth;

    /// <summary>How far in front of its wall a rise of <paramref name="rise"/> hides the floor from the camera (<see cref="TopShows"/>).</summary>
    public static double Hidden(SwitchMarker marker, double rise, double toCameraX, double toCameraY, double toCameraZ) =>
        rise * Math.Max(-(marker.Normal.X * toCameraX + marker.Normal.Y * toCameraY), 0) / Math.Max(toCameraZ, 1e-4);

    /// <summary>
    /// The rows a switch pair's marker shows, <c>[Top, Bottom)</c>: those where
    /// the two textures differ (the switch), grown evenly to
    /// <see cref="MinDepth"/> or cut evenly to <see cref="MaxDepth"/>, within
    /// the shorter texture; its middle rows when they do not differ.
    /// </summary>
    public static (int Top, int Bottom) Band(IndexedImage off, IndexedImage on)
    {
        int height = Math.Min(off.Height, on.Height), width = Math.Min(off.Width, on.Width);
        int top = height, bottom = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int a = y * off.Width + x, b = y * on.Width + x;
                if (off.Pixels[a] != on.Pixels[b] || off.Opaque[a] != on.Opaque[b])
                {
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y + 1);
                    break;
                }
            }
        }
        if (top >= bottom)
            (top, bottom) = (height / 2, height / 2);
        int depth = Math.Min(Math.Max(bottom - top, MinDepth), Math.Min(MaxDepth, height));
        // Centred on the switch (rounding up), then moved inside the texture.
        top = Math.Clamp((top + bottom - depth + 1) / 2, 0, height - depth);
        return (top, top + depth);
    }

    /// <summary>
    /// Builds the markers of a level: <paramref name="walls"/>
    /// its wall sections, <paramref name="polygons"/> its subsector polygons,
    /// <paramref name="rows"/> the switch textures (both of each pair, by name,
    /// any case) and the rows their marker shows (<see cref="Band"/>).
    /// </summary>
    public static SwitchMarkers Build(WallSections walls, SubsectorPolygons polygons, IReadOnlyDictionary<string, (int Top, int Bottom)> rows)
    {
        // Per side: its switch section, the first of upper, middle, lower.
        var bySide = new Dictionary<(int Line, int Side), WallSection>();
        foreach (WallSection s in walls.Sections)
        {
            if (s.Texture == 0 || !rows.ContainsKey(TextureName(s)) || s.V1.X == s.V2.X && s.V1.Y == s.V2.Y)
                continue;
            if (!bySide.TryGetValue((s.Line.Index, s.Side), out WallSection? had) || Order(s.Kind) < Order(had.Kind))
                bySide[(s.Line.Index, s.Side)] = s;
        }
        var keys = new List<(int Line, int Side)>(bySide.Keys);
        keys.Sort();

        var markers = new List<SwitchMarker>();
        var tops = new List<SwitchMarker>();
        foreach ((int Line, int Side) key in keys)
        {
            WallSection s = bySide[key];
            double x1 = s.V1.X / 65536.0, y1 = s.V1.Y / 65536.0, x2 = s.V2.X / 65536.0, y2 = s.V2.Y / 65536.0;
            double length = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
            double dx = (x2 - x1) / length, dy = (y2 - y1) / length;
            (int Top, int Bottom) band = rows[TextureName(s)];
            int depth = band.Bottom - band.Top;
            (double X, double Y) Point(double along, double inFront) => (x1 + along * dx + inFront * dy, y1 + along * dy - inFront * dx);
            // The strip in front of the side (its right), clockwise.
            List<(double X, double Y)[]> pieces = Clip([Point(0, 0), Point(length, 0), Point(length, depth), Point(0, depth)], polygons, s.FrontSector.Index);
            if (pieces.Count > 0)
                markers.Add(new SwitchMarker(s, x1, y1, dx, dy, length, band, pieces));
            // T10.6a: a lower switch's top marker, the strip behind the side (its left), clockwise.
            if (s.Kind == WallSectionKind.Lower && s.BackSector is not null)
            {
                List<(double X, double Y)[]> top = Clip([Point(0, 0), Point(0, -depth), Point(length, -depth), Point(length, 0)], polygons, s.BackSector.Index);
                if (top.Count > 0)
                    tops.Add(new SwitchMarker(s, x1, y1, dx, dy, length, band, top, true));
            }
        }
        return new SwitchMarkers(markers, tops);
    }

    // A strip clipped to a sector's floor (its subsector polygons): the pieces left.
    private static List<(double X, double Y)[]> Clip(List<(double X, double Y)> strip, SubsectorPolygons polygons, int sector)
    {
        var pieces = new List<(double X, double Y)[]>();
        foreach (int sub in polygons.BySector[sector])
        {
            PolygonVertex[] floor = polygons.Polygons[sub];
            List<(double X, double Y)> piece = strip;
            for (int k = 0; k < floor.Length && piece.Count >= 3; k++)
            {
                double ax = floor[k].X / 65536.0, ay = floor[k].Y / 65536.0;
                double bx = floor[(k + 1) % floor.Length].X / 65536.0, by = floor[(k + 1) % floor.Length].Y / 65536.0;
                // Clockwise: the interior is on the right of each edge.
                piece = WallCaps.Clip(piece, (by - ay, ax - bx, -((by - ay) * ax + (ax - bx) * ay)));
            }
            if (piece.Count >= 3 && Math.Abs(WallCaps.Area(piece)) > MinArea)
                pieces.Add([.. piece]);
        }
        return pieces;
    }

    /// <summary>The texture name of a section's part of its sidedef.</summary>
    public static string TextureName(WallSection s) => s.Kind switch
    {
        WallSectionKind.Upper => s.SideDef.TopTexture,
        WallSectionKind.Lower => s.SideDef.BottomTexture,
        _ => s.SideDef.MidTexture,
    };

    // p_switch.c P_ChangeSwitchTexture's order: upper, middle, lower.
    private static int Order(WallSectionKind kind) => kind switch
    {
        WallSectionKind.Upper => 0,
        WallSectionKind.Lower => 2,
        _ => 1,
    };
}
