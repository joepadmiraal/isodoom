using System.Diagnostics.CodeAnalysis;

namespace IsoDoom.Render;

/// <summary>T6.14a: whether a held weapon is drawn in front of the player's body or behind it.</summary>
public enum HoldLayer
{
    /// <summary>The weapon over the body, the pose's <see cref="HoldPose.Over"/> texels (the hands) over the weapon.</summary>
    Front,

    /// <summary>The weapon under the body: only what sticks out past it shows.</summary>
    Behind,
}

/// <summary>
/// T6.14a: where the stock rifle is in one <c>PLAY</c> lump of frames A–G, and how a
/// weapon replacing it is held (<c>docs/plans/player-weapons.md</c>'s contract,
/// SPEC §12 "T6.14 Player weapons"). Coordinates are in the lump's texel space:
/// x right, y down, (0, 0) the top-left texel's corner, so a texel's centre is
/// (x + 0.5, y + 0.5). The masks are <c>Width * Height</c>, row-major, as
/// <see cref="IsoDoom.Wad.Graphics.IndexedImage.Pixels"/>.
/// </summary>
/// <param name="Lump">The lump's name, e.g. <c>PLAYE2E8</c>.</param>
/// <param name="Rifle">The stock rifle's texels (removed from the composite).</param>
/// <param name="Flash">The muzzle flash's texels (frame F only; removed, and redrawn at the new muzzle).</param>
/// <param name="Over">Body texels drawn over a <see cref="HoldLayer.Front"/> weapon (the hands on the grip); none for <see cref="HoldLayer.Behind"/>.</param>
/// <param name="Layer">The weapon in front of the body or behind it.</param>
/// <param name="Grip">Where the rear hand holds the weapon.</param>
/// <param name="Muzzle">The rifle's muzzle end: the far end of the rifle mask's principal axis from <paramref name="Grip"/>.</param>
/// <param name="Foreshortening">0.3–1: the rifle's drawn length over its length in the frame's longest view (the side views in the aiming frames E and F; SPEC §12 T6.14).</param>
[SuppressMessage("Performance", "CA1819", Justification = "The plan's contract between T6.14a–c: masks as arrays")]
public sealed record HoldPose(
    string Lump,
    bool[] Rifle,
    bool[] Flash,
    bool[] Over,
    HoldLayer Layer,
    (double X, double Y) Grip,
    (double X, double Y) Muzzle,
    double Foreshortening)
{
    /// <summary>
    /// T6.14c: where each visible hand holds the rifle (ours, by eye; front poses only): the
    /// rear hand first, which takes the weapon's grip, then the forward hand, if one shows,
    /// which the weapon's axis passes through (SPEC §12 T6.14). Empty: the grip alone.
    /// </summary>
    [SuppressMessage("Performance", "CA1819", Justification = "A pose's few points, as its masks")]
    public (double X, double Y)[] Hands { get; init; } = [];
}
