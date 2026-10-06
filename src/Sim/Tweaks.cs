namespace IsoDoom.Sim;

/// <summary>
/// The sim's deviations from vanilla behaviour (SPEC §6.3), one named flag
/// per tweak. <see cref="Vanilla"/> has every flag off, so vanilla behaviour
/// stays testable (tests and demo/route comparisons use it); the game uses
/// <see cref="TopDown"/>. The flags are part of the game's settings like the
/// skill: every player of a lockstep game must run the same ones. Tweaks that
/// only change the presentation (SPEC §6.3 #4–#6: no first-person weapon
/// sprites, enemy visibility, the fuzz shader) are not here.
/// </summary>
public sealed record Tweaks
{
    /// <summary>All off: pure vanilla behaviour.</summary>
    public static Tweaks Vanilla { get; } = new();

    /// <summary>All on: the twin-stick game.</summary>
    public static Tweaks TopDown { get; } = new()
    {
        AbsoluteAiming = true,
        AbsoluteMovement = true,
        AimAssist = true,
        UseFallback = true,
    };

    /// <summary>
    /// SPEC §6.3 #1: the <c>ticcmd</c> carries an absolute 16-bit angle that
    /// sets the player's angle, instead of vanilla's relative <c>angleturn</c> (T4.5).
    /// </summary>
    public bool AbsoluteAiming { get; init; }

    /// <summary>
    /// SPEC §6.2, §6.3 #1: movement independent of facing. The <c>ticcmd</c>'s
    /// <c>forwardmove</c> thrusts north (+y) and <c>sidemove</c> east (+x),
    /// as for a vanilla player facing north, whatever the player's angle; the
    /// builder turns the screen-relative input into that world vector and
    /// normalises its length (<see cref="Ticcmds.AbsoluteMove"/>, SPEC §12 T4.5).
    /// </summary>
    public bool AbsoluteMovement { get; init; }

    /// <summary>SPEC §6.3 #2: hitscans and projectiles snap horizontally to the nearest target in a small cone (T6.7).</summary>
    public bool AimAssist { get; init; }

    /// <summary>
    /// SPEC §6.3 #3: the use trace starts in the aim direction, and when it
    /// hits nothing the nearest usable line within <c>USERANGE</c> facing the player is used (T5.2).
    /// </summary>
    public bool UseFallback { get; init; }
}
