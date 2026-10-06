using System;
using Godot;

namespace IsoDoom.Render;

/// <summary>How the cutaway removes walls (T3.4, SPEC §7.3; a presentation option). The shader's <c>cut_mode</c>.</summary>
public enum CutawayStyle
{
    /// <summary>Walls are drawn whole.</summary>
    Off = 0,

    /// <summary>Cut fragments are discarded (the default).</summary>
    Cut = 1,

    /// <summary>Half of the cut fragments are discarded in a 4×4 ordered dither (a screen door: the wall stays faintly visible).</summary>
    Dither = 2,
}

/// <summary>
/// What the cutaway shows inside a cut block (T3.4a, SPEC §12; a presentation
/// option). The shader's <c>cut_cap</c>.
/// </summary>
public enum CutawayCap
{
    /// <summary>Nothing: the inside of a block whose top is cut shows the void (T3.4).</summary>
    Off = 0,

    /// <summary>The block's cross-section at the cutoff: its floor flat, lit as its floor, as if the block were cut down to the stub its walls keep.</summary>
    Flat = 1,

    /// <summary>As <see cref="Flat"/>, <see cref="Cutaway.CapDarken"/> light levels darker, so the cut reads as a section rather than a floor.</summary>
    Dark = 2,
}

/// <summary>
/// Which thing billboards the cutaway cuts (T3.4b, SPEC §12; a presentation
/// option). The sprite shader's <c>cut_things</c>.
/// </summary>
public enum CutawayThings
{
    /// <summary>None: billboards are drawn whole (T3.5).</summary>
    Off = 0,

    /// <summary>Every thing but actors (<c>MF_SHOOTABLE</c>: monsters, barrels, the player), which must stay visible to be shot (the default).</summary>
    Decorations = 1,

    /// <summary>Every thing, actors too.</summary>
    All = 2,
}

/// <summary>The cutaway's presentation options (T3.4, SPEC §12): style, radius, cutoff height, whether the cursor ground point also cuts, the cap (T3.4a) and the things it cuts (T3.4b).</summary>
public sealed record CutawaySettings
{
    /// <summary>Defaults, tuned on E1M1, E1M8 and Doom II MAP15 with the game camera (SPEC §12 T3.4).</summary>
    public const float DefaultRadius = 80f, DefaultHeight = 32f;

    public CutawayStyle Style { get; init; } = CutawayStyle.Cut;

    /// <summary>Radius of the disc around the centre's view line, map units (on screen in orthographic: the hole's radius).</summary>
    public float Radius { get; init; } = DefaultRadius;

    /// <summary>Walls are cut only above this height over the centre's floor, map units.</summary>
    public float Height { get; init; } = DefaultHeight;

    /// <summary>Whether the cursor ground point cuts as the player does.</summary>
    public bool Cursor { get; init; }

    /// <summary>What a cut block shows inside (T3.4a).</summary>
    public CutawayCap Cap { get; init; } = CutawayCap.Dark;

    /// <summary>Which thing billboards are cut as walls are (T3.4b).</summary>
    public CutawayThings Things { get; init; } = CutawayThings.Decorations;
}

/// <summary>
/// The wall cutaway's rule (T3.4, SPEC §7.3), the CPU reference of the
/// <c>cut_hides</c> function in <c>shaders/level.gdshaderinc</c>. Positions
/// are map units (x, y, height), as the shader's.
/// <para>
/// A point <c>p</c> of a wall or floor is cut by a centre <c>c</c> (the
/// player's position on its floor, or the cursor ground point) when it lies
/// more than <see cref="CutawaySettings.Height"/> above <c>c</c>'s floor,
/// <c>c</c> is behind the surface's plane as seen from the camera, by more
/// than <see cref="PlaneMargin"/> (so walls
/// behind the player keep their height; a floor that high is always in
/// front of the anchor), and <c>p</c> is within
/// <see cref="CutawaySettings.Radius"/> of the line from the anchor
/// (<c>c</c> + <see cref="Anchor"/> up: the middle of a standing player)
/// towards the camera. The centre's own floor, and any floor or wall up to
/// the cutoff, is never cut (SPEC §12 T3.4).
/// </para>
/// </summary>
public static class Cutaway
{
    /// <summary>Height of the anchor above the centre's floor, map units: half the player's height (info.c <c>MT_PLAYER</c>, 56).</summary>
    public const float Anchor = 28f;

    /// <summary>
    /// The centre must be this far behind a surface's plane (map units) for
    /// the surface to be cut, so a wall in line with the centre (the plane
    /// through it, common on axis-aligned maps) is kept whole rather than
    /// cut by rounding (the shader's <c>CUT_PLANE_MARGIN</c>).
    /// </summary>
    public const float PlaneMargin = 1f;

    /// <summary>Light levels (of vanilla's 16) a <see cref="CutawayCap.Dark"/> cap loses against its floor (the shader's <c>CAP_DARKEN</c>).</summary>
    public const int CapDarken = 4;

    /// <summary>
    /// Whether centre <paramref name="centre"/>'s cap (T3.4a) covers map point
    /// <paramref name="xy"/> of a sector whose floor is at
    /// <paramref name="floor"/>: the floor is above the cutoff, and the cap
    /// point (at the cutoff height) is within the radius of the anchor's line
    /// towards the camera (the shader's kind 4; orthographic). It is drawn
    /// unless something nearer is drawn or the other centre cuts it.
    /// </summary>
    public static bool Caps(Vector2 xy, float floor, Vector3 centre, CutawaySettings settings, Vector3 toCamera)
    {
        float z = centre.Z + settings.Height;
        if (settings.Cap == CutawayCap.Off || settings.Style == CutawayStyle.Off || floor <= z)
            return false;
        var a = new Vector3(centre.X, centre.Y, centre.Z + Anchor);
        return Distance(new Vector3(xy.X, xy.Y, z), a, toCamera) < settings.Radius;
    }

    /// <summary>The light number (vanilla's, before clamping to 0–15) a cap adds to its sector's (T3.4a).</summary>
    public static int CapLightOffset(CutawayCap cap) => cap == CutawayCap.Dark ? -CapDarken : 0;

    /// <summary>Parses a cap name (<c>dark</c>, <c>flat</c>, <c>off</c>).</summary>
    public static CutawayCap ParseCap(string s) => s switch
    {
        "dark" => CutawayCap.Dark,
        "flat" => CutawayCap.Flat,
        "off" => CutawayCap.Off,
        _ => throw new ArgumentException($"--level-cutaway-cap: unknown cap \"{s}\" (dark, flat or off)"),
    };

    /// <summary>
    /// Whether centre <paramref name="centre"/> (x, y, floor height) cuts wall
    /// point <paramref name="p"/>. <paramref name="normal"/> is the surface's
    /// normal (either side; it is turned towards the camera here);
    /// <paramref name="toCamera"/> the unit direction towards the camera
    /// (orthographic: minus the view direction); in perspective pass the
    /// camera position as <paramref name="camera"/> instead (then
    /// <paramref name="toCamera"/> is ignored).
    /// </summary>
    public static bool Hides(Vector3 p, Vector3 normal, Vector3 centre, CutawaySettings settings, Vector3 toCamera, Vector3? camera = null)
    {
        if (p.Z <= centre.Z + settings.Height)
            return false;
        if (normal.Dot(camera is Vector3 cam ? cam - p : toCamera) < 0)
            normal = -normal;
        var a = new Vector3(centre.X, centre.Y, centre.Z + Anchor);
        if ((a - p).Dot(normal) > -PlaneMargin)
            return false;
        Vector3 axis = camera is Vector3 c ? (c - a).Normalized() : toCamera;
        return Distance(p, a, axis) < settings.Radius;
    }

    /// <summary>
    /// Whether centre <paramref name="centre"/> cuts point <paramref name="p"/>
    /// of a thing's billboard standing at <paramref name="foot"/> (T3.4b, the
    /// sprite shader's <c>cut_thing</c>; orthographic): as
    /// <see cref="Hides"/> for a wall through the foot whose normal is
    /// <paramref name="facing"/> (the billboard's horizontal normal towards
    /// the camera), with the plane test at the foot, so the whole thing is in
    /// front of the centre or not; <paramref name="p"/> is where the fragment
    /// writes its depth (on the upright quad with upright tilt depth, T3.6).
    /// <paramref name="actor"/> things are cut only with
    /// <see cref="CutawayThings.All"/>.
    /// </summary>
    public static bool HidesThing(Vector3 p, Vector3 foot, Vector3 facing, bool actor, Vector3 centre, CutawaySettings settings, Vector3 toCamera)
    {
        if (settings.Style == CutawayStyle.Off || settings.Things == CutawayThings.Off || (actor && settings.Things != CutawayThings.All))
            return false;
        if (p.Z <= centre.Z + settings.Height)
            return false;
        var a = new Vector3(centre.X, centre.Y, centre.Z + Anchor);
        if ((a - foot).Dot(facing) > -PlaneMargin)
            return false;
        return Distance(p, a, toCamera) < settings.Radius;
    }

    /// <summary>Parses which things the cutaway cuts (<c>decor</c>, <c>all</c>, <c>off</c>).</summary>
    public static CutawayThings ParseThings(string s) => s switch
    {
        "decor" or "decorations" => CutawayThings.Decorations,
        "all" => CutawayThings.All,
        "off" => CutawayThings.Off,
        _ => throw new ArgumentException($"--level-cutaway-things: unknown choice \"{s}\" (decor, all or off)"),
    };

    /// <summary>Distance of <paramref name="p"/> from the line through <paramref name="a"/> along the unit vector <paramref name="axis"/>.</summary>
    public static float Distance(Vector3 p, Vector3 a, Vector3 axis)
    {
        Vector3 d = p - a;
        return (d - axis * d.Dot(axis)).Length();
    }

    /// <summary>A Godot-space vector (x, height, −y) as map axes (x, y, height), scale unchanged.</summary>
    public static Vector3 ToMapAxes(Vector3 godot) => new(godot.X, -godot.Z, godot.Y);

    /// <summary>The shader's <c>cut_mode</c> for a style.</summary>
    public static int ShaderMode(CutawayStyle style) => (int)style;

    /// <summary>Parses a style name (<c>cut</c>, <c>dither</c>, <c>off</c>).</summary>
    public static CutawayStyle ParseStyle(string s) => s switch
    {
        "cut" => CutawayStyle.Cut,
        "dither" => CutawayStyle.Dither,
        "off" => CutawayStyle.Off,
        _ => throw new ArgumentException($"--level-cutaway: unknown style \"{s}\" (cut, dither or off)"),
    };
}
