using System;

namespace IsoDoom.Render;

/// <summary>
/// What a tilted billboard's depth is (T3.6, a presentation option; the
/// sprite shader's <c>tilt_depth</c>).
/// </summary>
public enum SpriteTiltDepth
{
    /// <summary>
    /// The default: the tilted quad's vertices are moved along their view rays
    /// back onto the upright billboard's plane, so the sprite looks tilted (it
    /// is drawn at the tilted size) but sorts as an upright one: walls and
    /// raised floors behind the thing never hide it.
    /// </summary>
    Upright = 0,

    /// <summary>The quad really leans back: its top goes into walls and raised floors behind the thing.</summary>
    Tilted = 1,
}

/// <summary>How blob shadows under actors are drawn (T3.6, SPEC §7.5; the shadow shader's <c>shadow_mode</c>).</summary>
public enum SpriteShadowStyle
{
    /// <summary>No shadows.</summary>
    Off = 0,

    /// <summary>A black disc blended at <see cref="SpriteSettings.ShadowOpacity"/>, fading out at the rim (leaves the palette).</summary>
    Blend = 1,

    /// <summary>Black pixels in a 4×4 ordered dither (a screen door: palette colours only).</summary>
    Dither = 2,
}

/// <summary>
/// The thing sprites' readability options (T3.6, SPEC §7.5, §12, §13 Q3):
/// billboard tilt towards the camera, blob shadows and outlines, and the
/// pull away from walls behind (T3.5a). Each is a presentation option; the
/// defaults are the ones chosen in T3.6 and T3.5a.
/// </summary>
public sealed record SpriteSettings
{
    /// <summary>The default outline: palette index 0 (black in DOOM's PLAYPAL; SPEC §12 T3.6).</summary>
    public const int DefaultOutline = 0;

    /// <summary>The default tilt (SPEC §12 T3.6).</summary>
    public const float DefaultTilt = 1f;

    /// <summary>
    /// How far billboards turn towards the camera about their horizontal
    /// axis, as a fraction of the camera's elevation seen from the thing:
    /// 0 upright (vanilla's Y-axis billboard, foreshortened by the pitch
    /// like the walls), 1 facing the camera (the patch at its drawn
    /// proportions). A horizontal view is never affected.
    /// </summary>
    public float Tilt { get; init; } = DefaultTilt;

    /// <summary>What depth a tilted billboard has.</summary>
    public SpriteTiltDepth TiltDepth { get; init; } = SpriteTiltDepth.Upright;

    /// <summary>
    /// The default wall pull (SPEC §12 T3.5a): <c>MT_PLAYER</c>'s radius, the
    /// smallest of any monster or the player, so every actor is pulled alike
    /// and they keep their order by depth.
    /// </summary>
    public const float DefaultWallPull = 16f;

    /// <summary>The longest wall pull along the view ray, in horizontal pulls (the shader's <c>MAX_WALL_PULL</c>; a view nearly straight down).</summary>
    public const float MaxWallPullFactor = 4f;

    /// <summary>
    /// The wall pull (T3.5a), map units; 0 for none. Every vertex of a
    /// billboard moves along its view ray towards the camera until it is
    /// min(the thing's <c>radius</c>, this) nearer horizontally (at most
    /// <see cref="MaxWallPullFactor"/> times that along the ray): the same
    /// place on screen, but a wall the thing stands against behind it no
    /// longer cuts the billboard (vanilla draws such a sprite whole). A wall
    /// in front that the thing collides with is at least its radius away (box
    /// distance, at least radius × √2 along the camera's horizontal direction
    /// at the 45° yaw), so it still hides it.
    /// </summary>
    public float WallPull { get; init; } = DefaultWallPull;

    /// <summary>Blob shadows under actors (things with <c>MF_SHOOTABLE</c>, and the player).</summary>
    public SpriteShadowStyle Shadow { get; init; } = SpriteShadowStyle.Off;

    /// <summary>The blended shadow's opacity at its centre.</summary>
    public float ShadowOpacity { get; init; } = 0.5f;

    /// <summary>A one-texel outline around sprites in this palette index (lit as the sprite), or −1 for none.</summary>
    public int Outline { get; init; } = DefaultOutline;

    /// <summary>Parses a tilt: a fraction 0–1, or <c>off</c> (0) / <c>half</c> (0.5) / <c>full</c> (1).</summary>
    public static float ParseTilt(string s) => s switch
    {
        "off" or "none" => 0f,
        "half" => 0.5f,
        "full" or "on" => 1f,
        _ => float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) && f >= 0 && f <= 1
            ? f
            : throw new ArgumentException($"--level-sprite-tilt: \"{s}\" (0-1, off, half or full)"),
    };

    /// <summary>Parses a tilt depth: <c>upright</c> or <c>tilted</c>.</summary>
    public static SpriteTiltDepth ParseTiltDepth(string s) => s switch
    {
        "upright" => SpriteTiltDepth.Upright,
        "tilted" => SpriteTiltDepth.Tilted,
        _ => throw new ArgumentException($"--level-sprite-tilt-depth: \"{s}\" (upright or tilted)"),
    };

    /// <summary>Parses a wall pull: <c>off</c> (0), or the most units a billboard is pulled, 0–64.</summary>
    public static float ParseWallPull(string s) => s == "off"
        ? 0f
        : float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) && f >= 0 && f <= 64
            ? f
            : throw new ArgumentException($"--level-sprite-wall-pull: \"{s}\" (off or 0-64 map units)");

    /// <summary>Parses a shadow style: <c>off</c>, <c>blend</c> or <c>dither</c>.</summary>
    public static SpriteShadowStyle ParseShadow(string s) => s switch
    {
        "off" => SpriteShadowStyle.Off,
        "blend" or "on" => SpriteShadowStyle.Blend,
        "dither" => SpriteShadowStyle.Dither,
        _ => throw new ArgumentException($"--level-sprite-shadow: \"{s}\" (off, blend or dither)"),
    };

    /// <summary>Parses an outline: <c>off</c>, or a palette index 0–255.</summary>
    public static int ParseOutline(string s) => s == "off"
        ? -1
        : int.TryParse(s, out int i) && i >= 0 && i <= 255
            ? i
            : throw new ArgumentException($"--level-sprite-outline: \"{s}\" (off or a palette index 0-255)");
}
