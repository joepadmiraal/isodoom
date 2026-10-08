namespace IsoDoom.Sim;

// d_items.h / d_items.c: the weapons' ammo and psprite states.

/// <summary>d_items.h <c>weaponinfo_t</c>: a weapon's ammo type and psprite states.</summary>
public readonly record struct weaponinfo_t(
    ammotype_t ammo,
    statenum_t upstate,
    statenum_t downstate,
    statenum_t readystate,
    statenum_t atkstate,
    statenum_t flashstate);

public static partial class Info
{
    /// <summary>d_items.c <c>weaponinfo</c>, in <see cref="weapontype_t"/> order.</summary>
    public static readonly weaponinfo_t[] weaponinfo =
    [
        // fist
        new(ammotype_t.am_noammo, statenum_t.S_PUNCHUP, statenum_t.S_PUNCHDOWN, statenum_t.S_PUNCH, statenum_t.S_PUNCH1, statenum_t.S_NULL),
        // pistol
        new(ammotype_t.am_clip, statenum_t.S_PISTOLUP, statenum_t.S_PISTOLDOWN, statenum_t.S_PISTOL, statenum_t.S_PISTOL1, statenum_t.S_PISTOLFLASH),
        // shotgun
        new(ammotype_t.am_shell, statenum_t.S_SGUNUP, statenum_t.S_SGUNDOWN, statenum_t.S_SGUN, statenum_t.S_SGUN1, statenum_t.S_SGUNFLASH1),
        // chaingun
        new(ammotype_t.am_clip, statenum_t.S_CHAINUP, statenum_t.S_CHAINDOWN, statenum_t.S_CHAIN, statenum_t.S_CHAIN1, statenum_t.S_CHAINFLASH1),
        // missile launcher
        new(ammotype_t.am_misl, statenum_t.S_MISSILEUP, statenum_t.S_MISSILEDOWN, statenum_t.S_MISSILE, statenum_t.S_MISSILE1, statenum_t.S_MISSILEFLASH1),
        // plasma rifle
        new(ammotype_t.am_cell, statenum_t.S_PLASMAUP, statenum_t.S_PLASMADOWN, statenum_t.S_PLASMA, statenum_t.S_PLASMA1, statenum_t.S_PLASMAFLASH1),
        // bfg 9000
        new(ammotype_t.am_cell, statenum_t.S_BFGUP, statenum_t.S_BFGDOWN, statenum_t.S_BFG, statenum_t.S_BFG1, statenum_t.S_BFGFLASH1),
        // chainsaw
        new(ammotype_t.am_noammo, statenum_t.S_SAWUP, statenum_t.S_SAWDOWN, statenum_t.S_SAW, statenum_t.S_SAW1, statenum_t.S_NULL),
        // super shotgun
        new(ammotype_t.am_shell, statenum_t.S_DSGUNUP, statenum_t.S_DSGUNDOWN, statenum_t.S_DSGUN, statenum_t.S_DSGUN1, statenum_t.S_DSGUNFLASH1),
    ];
}
