using IsoDoom.Sim;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Render;

/// <summary>
/// A weapon as the players' sprites hold it (T6.14b, SPEC §12 "T6.14 Player
/// weapons"): its side view pointing right, ramp-safe (no texel in the player
/// translation's green ramp, 0x70–0x7F, so a teammate's colour never tints
/// it), where the hand holds it and where its muzzle ends (texel coordinates,
/// (0, 0) the top-left texel's corner), and how big it is drawn: texels of the
/// player's sprite per texel of <see cref="Image"/>. No Godot types.
/// </summary>
public sealed record HeldWeapon(
    weapontype_t Weapon,
    IndexedImage Image,
    (double X, double Y) Grip,
    (double X, double Y) Muzzle,
    double Scale)
{
    /// <summary>Where <see cref="Image"/> comes from: the WAD's pickup sprite (<c>SHOTA0</c>, …) or <see cref="HeldWeapons.PistolName"/>.</summary>
    public string Source { get; init; } = "";

    /// <summary>Grip to muzzle in the image's texels.</summary>
    public double Length
    {
        get
        {
            double dx = Muzzle.X - Grip.X, dy = Muzzle.Y - Grip.Y;
            return System.Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
