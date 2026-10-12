using System;
using System.Collections.Generic;
using IsoDoom.Map;

namespace IsoDoom.Render;

/// <summary>
/// The height cues (T6.13s, SPEC §12; a presentation option): what the level
/// draws to show heights under the fixed camera, where a ledge seen from above
/// reads as one more floor. The shader's <c>height_cues</c>, a bit mask.
/// </summary>
[Flags]
public enum HeightCueMode
{
    /// <summary>None: vanilla.</summary>
    Off = 0,

    /// <summary>A bright line along the top of each ledge (<see cref="LedgeEdges"/>; the default).</summary>
    Edges = 1,

    /// <summary>Floors lit by their height above or below the player's floor (<see cref="LedgeEdges.ShadeLevels"/>).</summary>
    Shade = 2,

    /// <summary>Both.</summary>
    Both = Edges | Shade,
}

/// <summary>
/// One side's ledge edge (T6.13s): the pieces of the strip of floor along a
/// two-sided line on that side, which the shader draws while that side's floor
/// is more than <see cref="LedgeEdges.MinDrop"/> above the other's. Map units, y up.
/// </summary>
/// <param name="Line">The linedef.</param>
/// <param name="Side">0 for the line's front side, 1 for its back.</param>
/// <param name="Sector">The side's sector, whose floor the edge lies on.</param>
/// <param name="Other">The sector across the line.</param>
/// <param name="X">The side's start (its first vertex as seen from the side), map units.</param>
/// <param name="Y">The side's start, map units.</param>
/// <param name="Dx">The unit direction along the side, with its sector on the right.</param>
/// <param name="Dy">The unit direction along the side.</param>
/// <param name="Pieces">Convex polygons, clockwise (interior on the right of each edge, as the floors), on the side's sector's floor within <see cref="LedgeEdges.Width"/> of the line.</param>
public sealed record LedgeEdge(int Line, int Side, int Sector, int Other, double X, double Y, double Dx, double Dy,
    IReadOnlyList<(double X, double Y)[]> Pieces)
{
    /// <summary>How far <paramref name="x"/>, <paramref name="y"/> lies along the side from its start.</summary>
    public double Along(double x, double y) => (x - X) * Dx + (y - Y) * Dy;

    /// <summary>How far <paramref name="x"/>, <paramref name="y"/> lies in from the line, into the side's sector.</summary>
    public double Inside(double x, double y) => (x - X) * Dy - (y - Y) * Dx;
}

/// <summary>
/// The ledge edges (T6.13s, SPEC §12): under the fixed iso camera a raised
/// floor seen from above reads as one more floor, the wall below its edge
/// hidden whenever it faces away, so the player cannot tell a ledge from a
/// step. Each side of every two-sided line gets a thin strip on its sector's
/// floor along the line, which the shader draws, in a bright colour, while
/// that floor is more than <see cref="MinDrop"/> above the floor across the
/// line (<see cref="Shows"/>): a drop the player could not step back up. The
/// heights are the shader's, so lifts and moving floors show their edges as
/// they move. Pure C#, no Godot types (the tests link it); <c>LevelMesh</c>
/// builds the triangles and the shader places them on the floor.
/// <para>
/// The other cue (<see cref="HeightCueMode.Shade"/>) is the shader's alone:
/// floors, lids, wall caps and switch markers are drawn <see cref="ShadeLevels"/>
/// light levels brighter or darker by their height above the player's floor,
/// darker no lower than <see cref="ShadeMinLight"/> (<see cref="ShadeLight"/>).
/// </para>
/// </summary>
public sealed class LedgeEdges
{
    /// <summary>The highest step the player climbs (p_map.c <c>P_TryMove</c>'s 24 units): only a higher drop is a ledge.</summary>
    public const int MinDrop = 24;

    /// <summary>The strip's width, map units: the shader's <c>LEDGE_WIDTH</c>.</summary>
    public const double Width = 3;

    /// <summary>The height above the floor (map units), against z-fighting: the shader's <c>LEDGE_LIFT</c> (under the switch markers').</summary>
    public const float Lift = 0.5f;

    /// <summary>Map units of height per light level of the shading (<see cref="ShadeLevels"/>): the shader's <c>SHADE_STEP</c>.</summary>
    public const int ShadeStep = 24;

    /// <summary>The most light levels the shading adds or takes: the shader's <c>SHADE_MAX</c>.</summary>
    public const int ShadeMax = 5;

    /// <summary>The light level (of 16) the shading darkens no lower than (<see cref="ShadeLight"/>), so a deep drop does not go black: the shader's <c>SHADE_MIN_LIGHT</c>.</summary>
    public const int ShadeMinLight = 8;

    /// <summary>The edge's palette index (PLAYPAL's light grey): the shader's <c>LEDGE_INDEX</c>.</summary>
    public const int ColourIndex = 80;

    /// <summary>The light levels an edge is lit above its floor: the shader's <c>LEDGE_BRIGHTEN</c>.</summary>
    public const int Brighten = 4;

    /// <summary>The lowest light level (of 16) an edge is lit at: the shader's <c>LEDGE_MIN_LIGHT</c>.</summary>
    public const int MinLight = 10;

    /// <summary>The innermost part of the strip drawn in palette index 0 (a dark rim), map units: the shader's <c>LEDGE_RIM</c>.</summary>
    public const double Rim = 1;

    // Pieces smaller than this (square map units) are dropped.
    private const double MinArea = 1e-3;

    private LedgeEdges(List<LedgeEdge> edges) => Edges = edges;

    /// <summary>The edges, in line order, front side first (only sides with a piece left).</summary>
    public IReadOnlyList<LedgeEdge> Edges { get; }

    /// <summary>Whether an edge on a floor at <paramref name="floor"/> shows with the floor across its line at <paramref name="other"/> (map units): the shader's test.</summary>
    public static bool Shows(double floor, double other) => floor - other > MinDrop;

    /// <summary>
    /// The light levels the shading adds to a surface at <paramref name="height"/>
    /// with the player's floor at <paramref name="reference"/> (map units): one per
    /// <see cref="ShadeStep"/>, rounded to nearest as the shader's <c>floor(x + 0.5)</c>,
    /// at most <see cref="ShadeMax"/> either way.
    /// </summary>
    public static int ShadeLevels(double height, double reference) =>
        Math.Clamp((int)Math.Floor((height - reference) / ShadeStep + 0.5), -ShadeMax, ShadeMax);

    /// <summary>
    /// Light number <paramref name="light"/> (of 16) of a surface at <paramref name="height"/>
    /// shaded with the player's floor at <paramref name="reference"/>: <see cref="ShadeLevels"/>
    /// added, but darkened no lower than <see cref="ShadeMinLight"/>, and not at all when
    /// already below it (the shader's <c>shade_light</c>).
    /// </summary>
    public static int ShadeLight(int light, double height, double reference)
    {
        int shade = ShadeLevels(height, reference);
        return shade < 0 ? Math.Max(light + shade, Math.Min(light, ShadeMinLight)) : light + shade;
    }

    /// <summary>The name of <paramref name="mode"/> as the option and the setting take it.</summary>
    public static string Name(HeightCueMode mode) => mode.ToString().ToLowerInvariant();

    /// <summary>Parses <c>edges</c>, <c>shade</c>, <c>both</c> or <c>off</c> (<paramref name="what"/> names the option in the error).</summary>
    public static HeightCueMode Parse(string value, string what) => value switch
    {
        "edges" => HeightCueMode.Edges,
        "shade" => HeightCueMode.Shade,
        "both" => HeightCueMode.Both,
        "off" or "vanilla" => HeightCueMode.Off,
        _ => throw new ArgumentException($"{what}: \"{value}\" (edges, shade, both or off)"),
    };

    /// <summary>Builds the edges of <paramref name="level"/> from its subsector polygons <paramref name="polygons"/>.</summary>
    public static LedgeEdges Build(Level level, SubsectorPolygons polygons)
    {
        var edges = new List<LedgeEdge>();
        foreach (Line line in level.Lines)
        {
            if (line.BackSector is not Sector back || line.FrontSector is not Sector front || back == front)
                continue;
            if (line.V1.X == line.V2.X && line.V1.Y == line.V2.Y)
                continue;
            for (int side = 0; side < 2; side++)
            {
                // The front side's sector is on the right going V1 → V2, the back side's going V2 → V1.
                Vertex a = side == 0 ? line.V1 : line.V2, b = side == 0 ? line.V2 : line.V1;
                Sector sector = side == 0 ? front : back, other = side == 0 ? back : front;
                double x1 = a.X / 65536.0, y1 = a.Y / 65536.0, x2 = b.X / 65536.0, y2 = b.Y / 65536.0;
                double length = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
                double dx = (x2 - x1) / length, dy = (y2 - y1) / length;
                (double X, double Y) Point(double along, double inside) => (x1 + along * dx + inside * dy, y1 + along * dy - inside * dx);
                List<(double X, double Y)> strip = [Point(0, 0), Point(length, 0), Point(length, Width), Point(0, Width)];
                var pieces = new List<(double X, double Y)[]>();
                foreach (int sub in polygons.BySector[sector.Index])
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
                if (pieces.Count > 0)
                    edges.Add(new LedgeEdge(line.Index, side, sector.Index, other.Index, x1, y1, dx, dy, pieces));
            }
        }
        return new LedgeEdges(edges);
    }
}
