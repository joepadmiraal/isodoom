using IsoDoom.Map;
using IsoDoom.Wad;

namespace IsoDoom.Sim;

// p_pspr.c: weapon sprite animation, weapon objects. Action functions for
// weapons (T6.1 stubbed them and their dispatch, T6.6 ports the psprite
// state machine and the shareware weapons; the super shotgun is T10.2's,
// the plasma rifle and the BFG T9.3's). Nothing draws the psprites (SPEC
// §6.3 #4): they run for the timing, the shots, the sounds and extralight.

/// <summary>p_pspr.h <c>pspdef_t</c>: a player sprite, the weapon (<see cref="World.ps_weapon"/>) or its flash (<see cref="World.ps_flash"/>).</summary>
public sealed class pspdef_t
{
    /// <summary>The psprite's state; <see cref="statenum_t.S_NULL"/> means not active (vanilla's NULL).</summary>
    public statenum_t state;
    public int tics;
    /// <summary>fixed_t.</summary>
    public int sx;
    /// <summary>fixed_t.</summary>
    public int sy;

    /// <summary>Every field back to zero (<c>memset</c>).</summary>
    public void Clear()
    {
        state = statenum_t.S_NULL;
        tics = sx = sy = 0;
    }
}

public sealed partial class World
{
    // p_pspr.h psprnum_t
    /// <summary>p_pspr.h <c>ps_weapon</c>: the weapon psprite.</summary>
    public const int ps_weapon = 0;
    /// <summary>p_pspr.h <c>ps_flash</c>: the muzzle flash psprite.</summary>
    public const int ps_flash = 1;
    /// <summary>p_pspr.h <c>NUMPSPRITES</c>.</summary>
    public const int NUMPSPRITES = 2;

    /// <summary>p_pspr.c <c>LOWERSPEED</c>.</summary>
    public const int LOWERSPEED = Fixed.FRACUNIT * 6;
    /// <summary>p_pspr.c <c>RAISESPEED</c>.</summary>
    public const int RAISESPEED = Fixed.FRACUNIT * 6;

    /// <summary>p_pspr.c <c>WEAPONBOTTOM</c>.</summary>
    public const int WEAPONBOTTOM = 128 * Fixed.FRACUNIT;
    /// <summary>p_pspr.c <c>WEAPONTOP</c>.</summary>
    public const int WEAPONTOP = 32 * Fixed.FRACUNIT;

    /// <summary>p_pspr.c <c>BFGCELLS</c>: plasma cells for a bfg attack.</summary>
    public const int BFGCELLS = 40;

    /// <summary>p_pspr.c <c>bulletslope</c>: the slope <see cref="P_BulletSlope"/> found, for <see cref="P_GunShot"/> (fixed_t).</summary>
    public int bulletslope;

    /// <summary>
    /// p_pspr.c <c>P_SetPsprite</c>: puts psprite <paramref name="position"/>
    /// of <paramref name="player"/> in state <paramref name="stnum"/> and runs
    /// on through zero-tic states, calling each one's action
    /// (<see cref="A_CallWeapon"/>); <see cref="statenum_t.S_NULL"/> turns it off.
    /// </summary>
    public void P_SetPsprite(player_t player, int position, statenum_t stnum)
    {
        pspdef_t psp = player.psprites[position];

        do
        {
            if (stnum == statenum_t.S_NULL)
            {
                // object removed itself
                psp.state = statenum_t.S_NULL;
                break;
            }

            ref readonly state_t state = ref Info.states[(int)stnum];
            psp.state = stnum;
            psp.tics = state.tics; // could be 0

            if (state.misc1 != 0)
            {
                // coordinate set
                psp.sx = state.misc1 << Fixed.FRACBITS;
                psp.sy = state.misc2 << Fixed.FRACBITS;
            }

            // Call action routine.
            // Modified handling.
            if (state.action != actionf_t.NULL)
            {
                A_CallWeapon(state.action, player, psp);
                if (psp.state == statenum_t.S_NULL)
                    break;
            }

            stnum = Info.states[(int)psp.state].nextstate;
        } while (psp.tics == 0);
        // an initial state of 0 could cycle through
    }

    /// <summary>
    /// p_pspr.c <c>P_BringUpWeapon</c>: starts bringing the pending weapon up
    /// from the bottom of the screen (the ready weapon if none is pending).
    /// </summary>
    public void P_BringUpWeapon(player_t player)
    {
        if (player.pendingweapon == weapontype_t.wp_nochange)
            player.pendingweapon = player.readyweapon;

        if (player.pendingweapon == weapontype_t.wp_chainsaw)
            S_StartSound(player.mo, sfxenum_t.sfx_sawup);

        statenum_t newstate = Info.weaponinfo[(int)player.pendingweapon].upstate;

        player.pendingweapon = weapontype_t.wp_nochange;
        player.psprites[ps_weapon].sy = WEAPONBOTTOM;

        P_SetPsprite(player, ps_weapon, newstate);
    }

    /// <summary>
    /// p_pspr.c <c>P_CheckAmmo</c>: returns true if there is enough ammo to
    /// shoot; if not, selects the next weapon to use (vanilla's preferences)
    /// and lowers the current one.
    /// </summary>
    public bool P_CheckAmmo(player_t player)
    {
        ammotype_t ammo = Info.weaponinfo[(int)player.readyweapon].ammo;
        int count;

        // Minimal amount for one shot varies.
        if (player.readyweapon == weapontype_t.wp_bfg)
            count = BFGCELLS;
        else if (player.readyweapon == weapontype_t.wp_supershotgun)
            count = 2; // Double barrel.
        else
            count = 1; // Regular.

        // Some do not need ammunition anyway.
        // Return if current ammunition sufficient.
        if (ammo == ammotype_t.am_noammo || player.ammo[(int)ammo] >= count)
            return true;

        // Out of ammo, pick a weapon to change to.
        // Preferences are set here.
        do
        {
            if (player.weaponowned[(int)weapontype_t.wp_plasma]
                && player.ammo[(int)ammotype_t.am_cell] != 0
                && gamemode != GameMode.shareware)
            {
                player.pendingweapon = weapontype_t.wp_plasma;
            }
            else if (player.weaponowned[(int)weapontype_t.wp_supershotgun]
                     && player.ammo[(int)ammotype_t.am_shell] > 2
                     && gamemode == GameMode.commercial)
            {
                player.pendingweapon = weapontype_t.wp_supershotgun;
            }
            else if (player.weaponowned[(int)weapontype_t.wp_chaingun]
                     && player.ammo[(int)ammotype_t.am_clip] != 0)
            {
                player.pendingweapon = weapontype_t.wp_chaingun;
            }
            else if (player.weaponowned[(int)weapontype_t.wp_shotgun]
                     && player.ammo[(int)ammotype_t.am_shell] != 0)
            {
                player.pendingweapon = weapontype_t.wp_shotgun;
            }
            else if (player.ammo[(int)ammotype_t.am_clip] != 0)
            {
                player.pendingweapon = weapontype_t.wp_pistol;
            }
            else if (player.weaponowned[(int)weapontype_t.wp_chainsaw])
            {
                player.pendingweapon = weapontype_t.wp_chainsaw;
            }
            else if (player.weaponowned[(int)weapontype_t.wp_missile]
                     && player.ammo[(int)ammotype_t.am_misl] != 0)
            {
                player.pendingweapon = weapontype_t.wp_missile;
            }
            else if (player.weaponowned[(int)weapontype_t.wp_bfg]
                     && player.ammo[(int)ammotype_t.am_cell] > 40
                     && gamemode != GameMode.shareware)
            {
                player.pendingweapon = weapontype_t.wp_bfg;
            }
            else
            {
                // If everything fails.
                player.pendingweapon = weapontype_t.wp_fist;
            }
        } while (player.pendingweapon == weapontype_t.wp_nochange);

        // Now set appropriate weapon overlay.
        P_SetPsprite(player, ps_weapon, Info.weaponinfo[(int)player.readyweapon].downstate);

        return false;
    }

    /// <summary>
    /// p_pspr.c <c>P_FireWeapon</c>: with ammo enough, the player's attack
    /// frame, the weapon's attack state and a noise that wakes the monsters
    /// within reach (<see cref="P_NoiseAlert"/>).
    /// </summary>
    public void P_FireWeapon(player_t player)
    {
        if (!P_CheckAmmo(player))
            return;

        P_SetMobjState(player.mo!, statenum_t.S_PLAY_ATK1);
        statenum_t newstate = Info.weaponinfo[(int)player.readyweapon].atkstate;
        P_SetPsprite(player, ps_weapon, newstate);
        P_NoiseAlert(player.mo!, player.mo!);
    }

    /// <summary>p_pspr.c <c>P_DropWeapon</c>: player died, so put the weapon away.</summary>
    public void P_DropWeapon(player_t player)
    {
        P_SetPsprite(player, ps_weapon, Info.weaponinfo[(int)player.readyweapon].downstate);
    }

    /// <summary>
    /// p_pspr.c <c>P_SetupPsprites</c>: called at start of level for each
    /// player: removes the psprites and brings the ready weapon up.
    /// </summary>
    public void P_SetupPsprites(player_t player)
    {
        // remove all psprites
        for (int i = 0; i < NUMPSPRITES; i++)
            player.psprites[i].state = statenum_t.S_NULL;

        // spawn the gun
        player.pendingweapon = player.readyweapon;
        P_BringUpWeapon(player);
    }

    /// <summary>
    /// p_pspr.c <c>P_MovePsprites</c>: called every tic by the player
    /// thinking routine: counts each active psprite's tics down and moves it
    /// on; the flash follows the weapon's position.
    /// </summary>
    public void P_MovePsprites(player_t player)
    {
        for (int i = 0; i < NUMPSPRITES; i++)
        {
            pspdef_t psp = player.psprites[i];
            // a null state means not active
            if (psp.state != statenum_t.S_NULL)
            {
                // drop tic count and possibly change state

                // a -1 tic count never changes
                if (psp.tics != -1)
                {
                    psp.tics--;
                    if (psp.tics == 0)
                        P_SetPsprite(player, i, Info.states[(int)psp.state].nextstate);
                }
            }
        }

        player.psprites[ps_flash].sx = player.psprites[ps_weapon].sx;
        player.psprites[ps_flash].sy = player.psprites[ps_weapon].sy;
    }

    /// <summary>
    /// p_pspr.c <c>P_BulletSlope</c>: sets <see cref="bulletslope"/> to the
    /// slope of the first target found along the facing, else 5.6° (<c>1 &lt;&lt; 26</c>)
    /// left of it, else as much right; the last try's slope if none.
    /// </summary>
    public void P_BulletSlope(mobj_t mo)
    {
        // see which target is to be aimed at
        uint an = mo.angle;
        bulletslope = P_AimLineAttack(mo, an, 16 * 64 * Fixed.FRACUNIT);

        if (linetarget == null)
        {
            an = unchecked(an + (1u << 26));
            bulletslope = P_AimLineAttack(mo, an, 16 * 64 * Fixed.FRACUNIT);
            if (linetarget == null)
            {
                an = unchecked(an - (2u << 26));
                bulletslope = P_AimLineAttack(mo, an, 16 * 64 * Fixed.FRACUNIT);
            }
        }
    }

    /// <summary>
    /// p_pspr.c <c>P_GunShot</c>: one bullet of 5, 10 or 15 along the facing
    /// at <see cref="bulletslope"/>, spread by up to ±(255 &lt;&lt; 18) unless <paramref name="accurate"/>.
    /// </summary>
    public void P_GunShot(mobj_t mo, bool accurate)
    {
        int damage = 5 * (P_Random() % 3 + 1);
        uint angle = mo.angle;

        if (!accurate)
            angle = unchecked(angle + (uint)((P_Random() - P_Random()) << 18));

        P_LineAttack(mo, angle, MISSILERANGE, bulletslope, damage);
    }

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

    /// <summary>p_pspr.c <c>A_Light0</c>: no extra light.</summary>
    public void A_Light0(player_t player, pspdef_t psp)
    {
        player.extralight = 0;
    }

    /// <summary>
    /// p_pspr.c <c>A_WeaponReady</c>: the player is ready to fire or change
    /// weapons. Ends the player's attack frames, idles the chainsaw's sound,
    /// lowers the weapon for a pending change (or a dead player), fires
    /// (the rocket launcher and the BFG only on a new press), else bobs the
    /// weapon with the player's movement.
    /// </summary>
    public void A_WeaponReady(player_t player, pspdef_t psp)
    {
        mobj_t mo = player.mo!;

        // get out of attack state
        if (mo.state == statenum_t.S_PLAY_ATK1
            || mo.state == statenum_t.S_PLAY_ATK2)
        {
            P_SetMobjState(mo, statenum_t.S_PLAY);
        }

        if (player.readyweapon == weapontype_t.wp_chainsaw
            && psp.state == statenum_t.S_SAW)
        {
            S_StartSound(mo, sfxenum_t.sfx_sawidl);
        }

        // check for change
        //  if player is dead, put the weapon away
        if (player.pendingweapon != weapontype_t.wp_nochange || player.health == 0)
        {
            // change weapon
            //  (pending weapon should allready be validated)
            statenum_t newstate = Info.weaponinfo[(int)player.readyweapon].downstate;
            P_SetPsprite(player, ps_weapon, newstate);
            return;
        }

        // check for fire
        //  the missile launcher and bfg do not auto fire
        if ((player.cmd.buttons & buttoncode_t.BT_ATTACK) != 0)
        {
            if (!player.attackdown
                || (player.readyweapon != weapontype_t.wp_missile
                    && player.readyweapon != weapontype_t.wp_bfg))
            {
                player.attackdown = true;
                P_FireWeapon(player);
                return;
            }
        }
        else
            player.attackdown = false;

        // bob the weapon based on movement speed
        int angle = (128 * leveltime) & Tables.FINEMASK;
        psp.sx = Fixed.FRACUNIT + Fixed.FixedMul(player.bob, Tables.finecosine[angle]);
        angle &= Tables.FINEANGLES / 2 - 1;
        psp.sy = WEAPONTOP + Fixed.FixedMul(player.bob, Tables.finesine[angle]);
    }

    /// <summary>
    /// p_pspr.c <c>A_ReFire</c>: the player can re-fire the weapon without
    /// lowering it entirely (while fire is held and no change is pending),
    /// counting <see cref="player_t.refire"/>; else checks the ammo.
    /// </summary>
    public void A_ReFire(player_t player, pspdef_t psp)
    {
        // check for fire
        //  (if a weaponchange is pending, let it go through instead)
        if ((player.cmd.buttons & buttoncode_t.BT_ATTACK) != 0
            && player.pendingweapon == weapontype_t.wp_nochange
            && player.health != 0)
        {
            player.refire++;
            P_FireWeapon(player);
        }
        else
        {
            player.refire = 0;
            P_CheckAmmo(player);
        }
    }

    /// <summary>
    /// p_pspr.c <c>A_Lower</c>: lowers the current weapon, and changes
    /// weapon at the bottom (a dead player keeps it down).
    /// </summary>
    public void A_Lower(player_t player, pspdef_t psp)
    {
        psp.sy += LOWERSPEED;

        // Is already down.
        if (psp.sy < WEAPONBOTTOM)
            return;

        // Player is dead.
        if (player.playerstate == playerstate_t.PST_DEAD)
        {
            psp.sy = WEAPONBOTTOM;

            // don't bring weapon back up
            return;
        }

        // The old weapon has been lowered off the screen,
        // so change the weapon and start raising it
        if (player.health == 0)
        {
            // Player is dead, so keep the weapon off screen.
            P_SetPsprite(player, ps_weapon, statenum_t.S_NULL);
            return;
        }

        player.readyweapon = player.pendingweapon;

        P_BringUpWeapon(player);
    }

    /// <summary>p_pspr.c <c>A_Raise</c>: raises the weapon; at the top it is ready.</summary>
    public void A_Raise(player_t player, pspdef_t psp)
    {
        psp.sy -= RAISESPEED;

        if (psp.sy > WEAPONTOP)
            return;

        psp.sy = WEAPONTOP;

        // The weapon has been raised all the way,
        //  so change to the ready state.
        statenum_t newstate = Info.weaponinfo[(int)player.readyweapon].readystate;

        P_SetPsprite(player, ps_weapon, newstate);
    }

    /// <summary>p_pspr.c <c>A_GunFlash</c>: the player's firing frame and the weapon's flash (the rocket launcher's).</summary>
    public void A_GunFlash(player_t player, pspdef_t psp)
    {
        P_SetMobjState(player.mo!, statenum_t.S_PLAY_ATK2);
        P_SetPsprite(player, ps_flash, Info.weaponinfo[(int)player.readyweapon].flashstate);
    }

    /// <summary>
    /// p_pspr.c <c>A_Punch</c>: the fist, 2–20 damage (×10 with berserk)
    /// within <see cref="MELEERANGE"/>, slightly spread; turns the player to
    /// what it hit (with <see cref="Tweaks.AbsoluteAiming"/> the next
    /// command's angle overrides it, SPEC §12 T6.6).
    /// </summary>
    public void A_Punch(player_t player, pspdef_t psp)
    {
        mobj_t mo = player.mo!;
        int damage = (P_Random() % 10 + 1) << 1;

        if (player.powers[(int)powertype_t.pw_strength] != 0)
            damage *= 10;

        uint angle = P_AimAssist(mo, MELEERANGE); // vanilla: mo->angle (SPEC §12 T6.7)
        angle = unchecked(angle + (uint)((P_Random() - P_Random()) << 18));
        int slope = P_AimLineAttack(mo, angle, MELEERANGE);
        P_LineAttack(mo, angle, MELEERANGE, slope, damage);

        // turn to face target
        if (linetarget != null)
        {
            S_StartSound(mo, sfxenum_t.sfx_punch);
            mo.angle = Tables.R_PointToAngle2(mo.x, mo.y, linetarget.x, linetarget.y);
        }
    }

    /// <summary>
    /// p_pspr.c <c>A_Saw</c>: the chainsaw, 2–20 damage within
    /// <see cref="MELEERANGE"/> + 1, slightly spread; on a hit it turns the
    /// player towards the target (by at most <c>ANG90/20</c> a call) and
    /// pulls it forward next tic (<see cref="mobjflag_t.MF_JUSTATTACKED"/>,
    /// <see cref="P_PlayerThink"/>).
    /// </summary>
    public void A_Saw(player_t player, pspdef_t psp)
    {
        mobj_t mo = player.mo!;
        int damage = 2 * (P_Random() % 10 + 1);
        uint angle = P_AimAssist(mo, MELEERANGE + 1); // vanilla: mo->angle (SPEC §12 T6.7)
        angle = unchecked(angle + (uint)((P_Random() - P_Random()) << 18));

        // use meleerange + 1 se the puff doesn't skip the flash
        int slope = P_AimLineAttack(mo, angle, MELEERANGE + 1);
        P_LineAttack(mo, angle, MELEERANGE + 1, slope, damage);

        if (linetarget == null)
        {
            S_StartSound(mo, sfxenum_t.sfx_sawful);
            return;
        }
        S_StartSound(mo, sfxenum_t.sfx_sawhit);

        // turn to face target
        angle = Tables.R_PointToAngle2(mo.x, mo.y, linetarget.x, linetarget.y);
        unchecked
        {
            // ANG90 is a signed int in tables.h: -ANG90/20 compares as 2^32 - ANG90/20.
            const uint turn = Tables.ANG90 / 20, snap = Tables.ANG90 / 21;
            if (angle - mo.angle > Tables.ANG180)
            {
                if (angle - mo.angle < (uint)-(int)turn)
                    mo.angle = angle + snap;
                else
                    mo.angle -= turn;
            }
            else
            {
                if (angle - mo.angle > turn)
                    mo.angle = angle - snap;
                else
                    mo.angle += turn;
            }
        }
        mo.flags |= mobjflag_t.MF_JUSTATTACKED;
    }

    /// <summary>
    /// p_pspr.c <c>A_FirePistol</c>: a bullet (accurate unless refiring),
    /// the player's firing frame, the flash and one clip of ammo.
    /// </summary>
    public void A_FirePistol(player_t player, pspdef_t psp)
    {
        S_StartSound(player.mo, sfxenum_t.sfx_pistol);

        P_SetMobjState(player.mo!, statenum_t.S_PLAY_ATK2);
        player.ammo[(int)Info.weaponinfo[(int)player.readyweapon].ammo]--;

        P_SetPsprite(player, ps_flash, Info.weaponinfo[(int)player.readyweapon].flashstate);

        // P_BulletSlope; P_GunShot (SPEC §12 T6.7: along the aim assist's angle)
        P_BulletSlopeAndShoot(player.mo!, 1, player.refire == 0);
    }

    /// <summary>p_pspr.c <c>A_Light1</c>: extra light 1 (the flash).</summary>
    public void A_Light1(player_t player, pspdef_t psp)
    {
        player.extralight = 1;
    }

    /// <summary>p_pspr.c <c>A_FireShotgun</c>: seven spread pellets, the flash and one shell.</summary>
    public void A_FireShotgun(player_t player, pspdef_t psp)
    {
        S_StartSound(player.mo, sfxenum_t.sfx_shotgn);
        P_SetMobjState(player.mo!, statenum_t.S_PLAY_ATK2);

        player.ammo[(int)Info.weaponinfo[(int)player.readyweapon].ammo]--;

        P_SetPsprite(player, ps_flash, Info.weaponinfo[(int)player.readyweapon].flashstate);

        // P_BulletSlope; 7 × P_GunShot (SPEC §12 T6.7: along the aim assist's angle)
        P_BulletSlopeAndShoot(player.mo!, 7, false);
    }

    /// <summary>p_pspr.c <c>A_Light2</c>: extra light 2 (the flash).</summary>
    public void A_Light2(player_t player, pspdef_t psp)
    {
        player.extralight = 2;
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

    /// <summary>
    /// p_pspr.c <c>A_FireCGun</c>: a bullet (accurate on the first shot of a
    /// burst), the flash matching the frame (<c>S_CHAINFLASH1</c> or 2) and
    /// one clip; nothing but the sound without ammo.
    /// </summary>
    public void A_FireCGun(player_t player, pspdef_t psp)
    {
        S_StartSound(player.mo, sfxenum_t.sfx_pistol);

        if (player.ammo[(int)Info.weaponinfo[(int)player.readyweapon].ammo] == 0)
            return;

        P_SetMobjState(player.mo!, statenum_t.S_PLAY_ATK2);
        player.ammo[(int)Info.weaponinfo[(int)player.readyweapon].ammo]--;

        P_SetPsprite(player, ps_flash,
            Info.weaponinfo[(int)player.readyweapon].flashstate
            + (psp.state - statenum_t.S_CHAIN1));

        // P_BulletSlope; P_GunShot (SPEC §12 T6.7: along the aim assist's angle)
        P_BulletSlopeAndShoot(player.mo!, 1, player.refire == 0);
    }

    /// <summary>
    /// p_pspr.c <c>A_FireMissile</c>: the rocket launcher, a rocket and one
    /// of its ammo (T6.5).
    /// </summary>
    public void A_FireMissile(player_t player, pspdef_t psp)
    {
        player.ammo[(int)Info.weaponinfo[(int)player.readyweapon].ammo]--;
        P_SpawnPlayerMissile(player.mo!, mobjtype_t.MT_ROCKET);
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
