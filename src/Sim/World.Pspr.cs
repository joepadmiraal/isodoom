namespace IsoDoom.Sim;

// p_pspr.c: the action functions of the weapon (psprite) states. T6.1 stubs
// them and their dispatch; T6.6 ports the psprite state machine that calls
// them (P_SetPsprite) and fills them in.

/// <summary>p_pspr.h <c>pspdef_t</c>: a player sprite, the weapon or its flash (T6.6 runs them).</summary>
public sealed class pspdef_t
{
    /// <summary>The psprite's state; <see cref="statenum_t.S_NULL"/> means not active (vanilla's NULL).</summary>
    public statenum_t state;
    public int tics;
    /// <summary>fixed_t.</summary>
    public int sx;
    /// <summary>fixed_t.</summary>
    public int sy;
}

public sealed partial class World
{
    /// <summary>
    /// Whether <paramref name="action"/> is a psprite action, called with the
    /// player and its psprite (<see cref="A_CallWeapon"/>) instead of a mobj
    /// (<see cref="A_Call"/>): the weapon states' actions. Not vanilla, which
    /// tells them apart only by the function pointer's signature.
    /// </summary>
    public static bool IsWeaponAction(actionf_t action) => action switch
    {
        actionf_t.A_Light0
            or actionf_t.A_WeaponReady
            or actionf_t.A_Lower
            or actionf_t.A_Raise
            or actionf_t.A_Punch
            or actionf_t.A_ReFire
            or actionf_t.A_FirePistol
            or actionf_t.A_Light1
            or actionf_t.A_FireShotgun
            or actionf_t.A_Light2
            or actionf_t.A_FireShotgun2
            or actionf_t.A_CheckReload
            or actionf_t.A_OpenShotgun2
            or actionf_t.A_LoadShotgun2
            or actionf_t.A_CloseShotgun2
            or actionf_t.A_FireCGun
            or actionf_t.A_GunFlash
            or actionf_t.A_FireMissile
            or actionf_t.A_Saw
            or actionf_t.A_FirePlasma
            or actionf_t.A_BFGsound
            or actionf_t.A_FireBFG => true,
        _ => false,
    };

    /// <summary>
    /// Vanilla's call through <c>state->action.acp2</c> (p_pspr.c
    /// <c>P_SetPsprite</c>, T6.6): runs a weapon state's action. A mobj
    /// action never comes here: no weapon state has one, so it throws.
    /// </summary>
    public void A_CallWeapon(actionf_t action, player_t player, pspdef_t psp)
    {
        switch (action)
        {
            case actionf_t.A_Light0:
                A_Light0(player, psp);
                break;
            case actionf_t.A_WeaponReady:
                A_WeaponReady(player, psp);
                break;
            case actionf_t.A_Lower:
                A_Lower(player, psp);
                break;
            case actionf_t.A_Raise:
                A_Raise(player, psp);
                break;
            case actionf_t.A_Punch:
                A_Punch(player, psp);
                break;
            case actionf_t.A_ReFire:
                A_ReFire(player, psp);
                break;
            case actionf_t.A_FirePistol:
                A_FirePistol(player, psp);
                break;
            case actionf_t.A_Light1:
                A_Light1(player, psp);
                break;
            case actionf_t.A_FireShotgun:
                A_FireShotgun(player, psp);
                break;
            case actionf_t.A_Light2:
                A_Light2(player, psp);
                break;
            case actionf_t.A_FireShotgun2:
                A_FireShotgun2(player, psp);
                break;
            case actionf_t.A_CheckReload:
                A_CheckReload(player, psp);
                break;
            case actionf_t.A_OpenShotgun2:
                A_OpenShotgun2(player, psp);
                break;
            case actionf_t.A_LoadShotgun2:
                A_LoadShotgun2(player, psp);
                break;
            case actionf_t.A_CloseShotgun2:
                A_CloseShotgun2(player, psp);
                break;
            case actionf_t.A_FireCGun:
                A_FireCGun(player, psp);
                break;
            case actionf_t.A_GunFlash:
                A_GunFlash(player, psp);
                break;
            case actionf_t.A_FireMissile:
                A_FireMissile(player, psp);
                break;
            case actionf_t.A_Saw:
                A_Saw(player, psp);
                break;
            case actionf_t.A_FirePlasma:
                A_FirePlasma(player, psp);
                break;
            case actionf_t.A_BFGsound:
                A_BFGsound(player, psp);
                break;
            case actionf_t.A_FireBFG:
                A_FireBFG(player, psp);
                break;
            default:
                throw new System.InvalidOperationException($"{action} is not a weapon action.");
        }
    }

    /// <summary>p_pspr.c <c>A_Light0</c>: no extra light. A stub until T6.6.</summary>
    public void A_Light0(player_t player, pspdef_t psp)
    {
    }

    /// <summary>p_pspr.c <c>A_WeaponReady</c>: the weapon ready: bob, fire or change. A stub until T6.6.</summary>
    public void A_WeaponReady(player_t player, pspdef_t psp)
    {
    }

    /// <summary>p_pspr.c <c>A_Lower</c>: lower the weapon, then raise the pending one. A stub until T6.6.</summary>
    public void A_Lower(player_t player, pspdef_t psp)
    {
    }

    /// <summary>p_pspr.c <c>A_Raise</c>: raise the weapon until ready. A stub until T6.6.</summary>
    public void A_Raise(player_t player, pspdef_t psp)
    {
    }

    /// <summary>p_pspr.c <c>A_Punch</c>: the fist. A stub until T6.6.</summary>
    public void A_Punch(player_t player, pspdef_t psp)
    {
    }

    /// <summary>p_pspr.c <c>A_ReFire</c>: fire again while the button is held. A stub until T6.6.</summary>
    public void A_ReFire(player_t player, pspdef_t psp)
    {
    }

    /// <summary>p_pspr.c <c>A_FirePistol</c>: the pistol. A stub until T6.6.</summary>
    public void A_FirePistol(player_t player, pspdef_t psp)
    {
    }

    /// <summary>p_pspr.c <c>A_Light1</c>: extra light 1. A stub until T6.6.</summary>
    public void A_Light1(player_t player, pspdef_t psp)
    {
    }

    /// <summary>p_pspr.c <c>A_FireShotgun</c>: the shotgun. A stub until T6.6.</summary>
    public void A_FireShotgun(player_t player, pspdef_t psp)
    {
    }

    /// <summary>p_pspr.c <c>A_Light2</c>: extra light 2. A stub until T6.6.</summary>
    public void A_Light2(player_t player, pspdef_t psp)
    {
    }

    /// <summary>p_pspr.c <c>A_FireShotgun2</c>: the super shotgun. A stub until T10.2.</summary>
    public void A_FireShotgun2(player_t player, pspdef_t psp)
    {
    }

    /// <summary>p_pspr.c <c>A_CheckReload</c>: the super shotgun's ammo check. A stub until T10.2.</summary>
    public void A_CheckReload(player_t player, pspdef_t psp)
    {
    }

    /// <summary>p_pspr.c <c>A_OpenShotgun2</c>: the super shotgun opening. A stub until T10.2.</summary>
    public void A_OpenShotgun2(player_t player, pspdef_t psp)
    {
    }

    /// <summary>p_pspr.c <c>A_LoadShotgun2</c>: the super shotgun loading. A stub until T10.2.</summary>
    public void A_LoadShotgun2(player_t player, pspdef_t psp)
    {
    }

    /// <summary>p_pspr.c <c>A_CloseShotgun2</c>: the super shotgun closing. A stub until T10.2.</summary>
    public void A_CloseShotgun2(player_t player, pspdef_t psp)
    {
    }

    /// <summary>p_pspr.c <c>A_FireCGun</c>: the chaingun. A stub until T6.6.</summary>
    public void A_FireCGun(player_t player, pspdef_t psp)
    {
    }

    /// <summary>p_pspr.c <c>A_GunFlash</c>: the muzzle flash. A stub until T6.6.</summary>
    public void A_GunFlash(player_t player, pspdef_t psp)
    {
    }

    /// <summary>p_pspr.c <c>A_FireMissile</c>: the rocket launcher. A stub until T6.6.</summary>
    public void A_FireMissile(player_t player, pspdef_t psp)
    {
    }

    /// <summary>p_pspr.c <c>A_Saw</c>: the chainsaw. A stub until T6.6.</summary>
    public void A_Saw(player_t player, pspdef_t psp)
    {
    }

    /// <summary>p_pspr.c <c>A_FirePlasma</c>: the plasma rifle. A stub until T9.3.</summary>
    public void A_FirePlasma(player_t player, pspdef_t psp)
    {
    }

    /// <summary>p_pspr.c <c>A_BFGsound</c>: the BFG's charge sound. A stub until T9.3.</summary>
    public void A_BFGsound(player_t player, pspdef_t psp)
    {
    }

    /// <summary>p_pspr.c <c>A_FireBFG</c>: the BFG. A stub until T9.3.</summary>
    public void A_FireBFG(player_t player, pspdef_t psp)
    {
    }

}
