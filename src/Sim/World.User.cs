using IsoDoom.Map;
using IsoDoom.Wad;

namespace IsoDoom.Sim;

// p_user.c: player related stuff (movement, view height), and g_game.c
// G_Ticker's sim part.
public sealed partial class World
{
    /// <summary>p_user.c <c>INVERSECOLORMAP</c>: index of the special effects (INVUL inverse) map.</summary>
    public const int INVERSECOLORMAP = 32;

    /// <summary>p_user.c <c>MAXBOB</c>: 16 pixels of bob.</summary>
    public const int MAXBOB = 0x100000;

    /// <summary>
    /// p_user.c <c>onground</c>: whether the player thinking now stands on its
    /// floor (set by <see cref="P_MovePlayer"/>, read by <see cref="P_CalcHeight"/>;
    /// a player that does not move this tic keeps the last value, as vanilla's global).
    /// </summary>
    public bool onground;

    /// <summary>
    /// g_game.c <c>G_Ticker</c>'s level part: each player in the game gets its
    /// <c>ticcmd</c> (<paramref name="netcmds"/>, indexed by player), then
    /// <see cref="P_Ticker"/> runs the tic and <see cref="HU_TakeMessages"/>
    /// queues the messages it left (T6.10). Demo recording/playback, the
    /// consistency check and the game actions are the game loop's (T4.7, M7).
    /// </summary>
    public void G_Ticker(ticcmd_t[] netcmds)
    {
        for (int i = 0; i < MAXPLAYERS; i++)
        {
            if (playeringame[i])
                players[i].cmd = netcmds[i];
        }
        P_Ticker();
        HU_TakeMessages(); // HU_Ticker's message part (T6.10)
    }

    /// <summary><see cref="G_Ticker(ticcmd_t[])"/> for a game of one player (<see cref="consoleplayer"/>).</summary>
    public void G_Ticker(in ticcmd_t cmd)
    {
        players[consoleplayer].cmd = cmd;
        P_Ticker();
        HU_TakeMessages(); // HU_Ticker's message part (T6.10)
    }

    /// <summary>p_user.c <c>P_Thrust</c>: moves the given origin along a given angle.</summary>
    public static void P_Thrust(player_t player, uint angle, int move)
    {
        int a = (int)(angle >> Tables.ANGLETOFINESHIFT);

        player.mo!.momx += Fixed.FixedMul(move, Tables.finecosine[a]);
        player.mo.momy += Fixed.FixedMul(move, Tables.finesine[a]);
    }

    /// <summary>
    /// p_user.c <c>P_CalcHeight</c>: calculate the walking / running height
    /// adjustment. The bob (a quarter of the momentum squared, at most
    /// <see cref="MAXBOB"/>) swings <see cref="player_t.viewz"/> on the
    /// ground; a live player's <see cref="player_t.viewheight"/> moves by
    /// <see cref="player_t.deltaviewheight"/> back to <see cref="player_t.VIEWHEIGHT"/>
    /// after a step up or a squat (set by <see cref="P_ZMovement"/>), never
    /// below half of it. Off the ground (or with <see cref="player_t.CF_NOMOMENTUM"/>)
    /// the view is <c>z + viewheight</c>: vanilla clamps a first value to the
    /// ceiling and then overwrites it, kept as is.
    /// </summary>
    public void P_CalcHeight(player_t player)
    {
        mobj_t mo = player.mo!;

        // Regular movement bobbing
        // (needs to be calculated for gun swing
        // even if not on ground)
        // OPTIMIZE: tablify angle
        // Note: a LUT allows for effects
        //  like a ramp with low health.
        player.bob = Fixed.FixedMul(mo.momx, mo.momx) + Fixed.FixedMul(mo.momy, mo.momy);
        player.bob >>= 2;

        if (player.bob > MAXBOB)
            player.bob = MAXBOB;

        if ((player.cheats & player_t.CF_NOMOMENTUM) != 0 || !onground)
        {
            player.viewz = mo.z + player_t.VIEWHEIGHT;

            if (player.viewz > mo.ceilingz - 4 * Fixed.FRACUNIT)
                player.viewz = mo.ceilingz - 4 * Fixed.FRACUNIT;

            player.viewz = mo.z + player.viewheight;
            return;
        }

        int angle = (Tables.FINEANGLES / 20 * leveltime) & Tables.FINEMASK;
        int bob = Fixed.FixedMul(player.bob / 2, Tables.finesine[angle]);

        // move viewheight
        if (player.playerstate == playerstate_t.PST_LIVE)
        {
            player.viewheight += player.deltaviewheight;

            if (player.viewheight > player_t.VIEWHEIGHT)
            {
                player.viewheight = player_t.VIEWHEIGHT;
                player.deltaviewheight = 0;
            }

            if (player.viewheight < player_t.VIEWHEIGHT / 2)
            {
                player.viewheight = player_t.VIEWHEIGHT / 2;
                if (player.deltaviewheight <= 0)
                    player.deltaviewheight = 1;
            }

            if (player.deltaviewheight != 0)
            {
                player.deltaviewheight += Fixed.FRACUNIT / 4;
                if (player.deltaviewheight == 0)
                    player.deltaviewheight = 1;
            }
        }
        player.viewz = mo.z + player.viewheight + bob;

        if (player.viewz > mo.ceilingz - 4 * Fixed.FRACUNIT)
            player.viewz = mo.ceilingz - 4 * Fixed.FRACUNIT;
    }

    /// <summary>
    /// p_user.c <c>P_MovePlayer</c>: turns the player by <c>angleturn</c>
    /// (with <see cref="Tweaks.AbsoluteAiming"/>: sets its angle from it) and,
    /// on the ground only, thrusts it by <c>forwardmove</c>/<c>sidemove</c>
    /// × 2048 along and right of its facing (with
    /// <see cref="Tweaks.AbsoluteMovement"/>: north and east). Move input
    /// starts the walking frames from <see cref="statenum_t.S_PLAY"/>.
    /// </summary>
    public void P_MovePlayer(player_t player)
    {
        ref ticcmd_t cmd = ref player.cmd;
        mobj_t mo = player.mo!;

        if (tweaks.AbsoluteAiming)
            mo.angle = (uint)(ushort)cmd.angleturn << 16; // SPEC §6.3 #1
        else
            mo.angle = unchecked(mo.angle + (uint)(cmd.angleturn << 16));

        // Do not let the player control movement
        //  if not onground.
        onground = mo.z <= mo.floorz;

        if (cmd.forwardmove != 0 && onground)
        {
            if (tweaks.AbsoluteMovement)
                mo.momy += cmd.forwardmove * 2048; // north (SPEC §12 T4.5)
            else
                P_Thrust(player, mo.angle, cmd.forwardmove * 2048);
        }

        if (cmd.sidemove != 0 && onground)
        {
            if (tweaks.AbsoluteMovement)
                mo.momx += cmd.sidemove * 2048; // east
            else
                P_Thrust(player, unchecked(mo.angle - Tables.ANG90), cmd.sidemove * 2048);
        }

        if ((cmd.forwardmove != 0 || cmd.sidemove != 0)
            && mo.state == statenum_t.S_PLAY)
        {
            P_SetMobjState(mo, statenum_t.S_PLAY_RUN1);
        }
    }

    /// <summary>
    /// p_user.c <c>P_PlayerThink</c>: the noclip
    /// cheat, the chainsaw's run forward (<see cref="mobjflag_t.MF_JUSTATTACKED"/>),
    /// <see cref="P_MovePlayer"/> (not while <see cref="mobj_t.reactiontime"/>
    /// counts down after a teleport), <see cref="P_CalcHeight"/>, the power-up
    /// and palette counters and the fixed colormaps, and the use button
    /// (<see cref="P_UseLines"/> once per press, <see cref="player_t.usedown"/>).
    /// The player's special sectors (<see cref="P_PlayerInSpecialSector"/>, T5.8),
    /// the weapon change from <c>BT_CHANGE</c> and <see cref="P_MovePsprites"/> (T6.6).
    /// Still to come: <c>P_DeathThink</c> (T6.12).
    /// </summary>
    public void P_PlayerThink(player_t player)
    {
        mobj_t mo = player.mo!;

        // fixme: do this in the cheat code
        if ((player.cheats & player_t.CF_NOCLIP) != 0)
            mo.flags |= mobjflag_t.MF_NOCLIP;
        else
            mo.flags &= ~mobjflag_t.MF_NOCLIP;

        // chain saw run forward
        ref ticcmd_t cmd = ref player.cmd;
        if ((mo.flags & mobjflag_t.MF_JUSTATTACKED) != 0)
        {
            // With the twin-stick tweaks the same lunge: keep the angle, thrust along it (SPEC §12 T4.5).
            cmd.angleturn = tweaks.AbsoluteAiming ? unchecked((short)(mo.angle >> 16)) : (short)0;
            if (tweaks.AbsoluteMovement)
            {
                Ticcmds.AbsoluteMove(ref cmd, mo.angle, 0xc800 / 512);
            }
            else
            {
                cmd.forwardmove = 0xc800 / 512;
                cmd.sidemove = 0;
            }
            mo.flags &= ~mobjflag_t.MF_JUSTATTACKED;
        }

        if (player.playerstate == playerstate_t.PST_DEAD)
        {
            // P_DeathThink (player); (T6.12)
            return;
        }

        // Move around.
        // Reactiontime is used to prevent movement
        //  for a bit after a teleport.
        if (mo.reactiontime != 0)
            mo.reactiontime--;
        else
            P_MovePlayer(player);

        P_CalcHeight(player);

        if (mo.subsector.sector.special != 0)
            P_PlayerInSpecialSector(player);

        // Check for weapon change.

        // A special event has no other buttons.
        if ((cmd.buttons & buttoncode_t.BT_SPECIAL) != 0)
            cmd.buttons = 0;

        if ((cmd.buttons & buttoncode_t.BT_CHANGE) != 0)
        {
            // The actual changing of the weapon is done
            //  when the weapon psprite can do it
            //  (read: not in the middle of an attack).
            var newweapon = (weapontype_t)((cmd.buttons & buttoncode_t.BT_WEAPONMASK) >> buttoncode_t.BT_WEAPONSHIFT);

            if (newweapon == weapontype_t.wp_fist
                && player.weaponowned[(int)weapontype_t.wp_chainsaw]
                && !(player.readyweapon == weapontype_t.wp_chainsaw
                     && player.powers[(int)powertype_t.pw_strength] != 0))
            {
                newweapon = weapontype_t.wp_chainsaw;
            }

            if (gamemode == GameMode.commercial
                && newweapon == weapontype_t.wp_shotgun
                && player.weaponowned[(int)weapontype_t.wp_supershotgun]
                && player.readyweapon != weapontype_t.wp_supershotgun)
            {
                newweapon = weapontype_t.wp_supershotgun;
            }

            if (player.weaponowned[(int)newweapon]
                && newweapon != player.readyweapon)
            {
                // Do not go to plasma or BFG in shareware,
                //  even if cheated.
                if ((newweapon != weapontype_t.wp_plasma
                     && newweapon != weapontype_t.wp_bfg)
                    || gamemode != GameMode.shareware)
                {
                    player.pendingweapon = newweapon;
                }
            }
        }

        // check for use
        if ((cmd.buttons & buttoncode_t.BT_USE) != 0)
        {
            if (!player.usedown)
            {
                P_UseLines(player);
                player.usedown = true;
            }
        }
        else
            player.usedown = false;

        // cycle psprites
        P_MovePsprites(player);

        // Counters, time dependend power ups.

        // Strength counts up to diminish fade.
        if (player.powers[(int)powertype_t.pw_strength] != 0)
            player.powers[(int)powertype_t.pw_strength]++;

        if (player.powers[(int)powertype_t.pw_invulnerability] != 0)
            player.powers[(int)powertype_t.pw_invulnerability]--;

        if (player.powers[(int)powertype_t.pw_invisibility] != 0)
        {
            if (--player.powers[(int)powertype_t.pw_invisibility] == 0)
                mo.flags &= ~mobjflag_t.MF_SHADOW;
        }

        if (player.powers[(int)powertype_t.pw_infrared] != 0)
            player.powers[(int)powertype_t.pw_infrared]--;

        if (player.powers[(int)powertype_t.pw_ironfeet] != 0)
            player.powers[(int)powertype_t.pw_ironfeet]--;

        if (player.damagecount != 0)
            player.damagecount--;

        if (player.bonuscount != 0)
            player.bonuscount--;

        // Handling colormaps.
        if (player.powers[(int)powertype_t.pw_invulnerability] != 0)
        {
            if (player.powers[(int)powertype_t.pw_invulnerability] > 4 * 32
                || (player.powers[(int)powertype_t.pw_invulnerability] & 8) != 0)
                player.fixedcolormap = INVERSECOLORMAP;
            else
                player.fixedcolormap = 0;
        }
        else if (player.powers[(int)powertype_t.pw_infrared] != 0)
        {
            if (player.powers[(int)powertype_t.pw_infrared] > 4 * 32
                || (player.powers[(int)powertype_t.pw_infrared] & 8) != 0)
            {
                // almost full bright
                player.fixedcolormap = 1;
            }
            else
                player.fixedcolormap = 0;
        }
        else
            player.fixedcolormap = 0;
    }
}
