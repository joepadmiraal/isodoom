using IsoDoom.Map;
using IsoDoom.Wad;

namespace IsoDoom.Sim;

// p_inter.c: handling interactions (i.e., collisions), T5.8. The pickups of
// P_TouchSpecialThing (keys; health, armor and power-ups, SPEC §12 T5.8; the
// weapons, ammo and backpack wait for T6.8), P_DamageMobj (players only until
// T6.3) and P_KillMobj. Messages go to player_t.message (d_englsh.h), sounds
// to the sound events.
public sealed partial class World
{
    // d_englsh.h: the pickup messages.
    public const string GOTARMOR = "Picked up the armor.";
    public const string GOTMEGA = "Picked up the MegaArmor!";
    public const string GOTHTHBONUS = "Picked up a health bonus.";
    public const string GOTARMBONUS = "Picked up an armor bonus.";
    public const string GOTSTIM = "Picked up a stimpack.";
    public const string GOTMEDINEED = "Picked up a medikit that you REALLY need!";
    public const string GOTMEDIKIT = "Picked up a medikit.";
    public const string GOTSUPER = "Supercharge!";
    public const string GOTBLUECARD = "Picked up a blue keycard.";
    public const string GOTYELWCARD = "Picked up a yellow keycard.";
    public const string GOTREDCARD = "Picked up a red keycard.";
    public const string GOTBLUESKUL = "Picked up a blue skull key.";
    public const string GOTYELWSKUL = "Picked up a yellow skull key.";
    public const string GOTREDSKULL = "Picked up a red skull key.";
    public const string GOTINVUL = "Invulnerability!";
    public const string GOTBERSERK = "Berserk!";
    public const string GOTINVIS = "Partial Invisibility";
    public const string GOTSUIT = "Radiation Shielding Suit";
    public const string GOTMAP = "Computer Area Map";
    public const string GOTVISOR = "Light Amplification Visor";
    public const string GOTMSPHERE = "MegaSphere!";

    /// <summary>p_inter.c <c>BONUSADD</c>: the pickup flash.</summary>
    public const int BONUSADD = 6;

    // doomdef.h: power up durations.
    public const int INVULNTICS = 30 * SimInfo.TICRATE;
    public const int INVISTICS = 60 * SimInfo.TICRATE;
    public const int INFRATICS = 120 * SimInfo.TICRATE;
    public const int IRONTICS = 60 * SimInfo.TICRATE;

    /// <summary>p_local.h <c>BASETHRESHOLD</c>: follow a player exclusively for 3 seconds.</summary>
    public const int BASETHRESHOLD = 100;

    /// <summary>
    /// p_inter.c <c>P_GiveBody</c>: returns false if the body isn't needed at all
    /// (health at <see cref="player_t.MAXHEALTH"/> or more).
    /// </summary>
    public static bool P_GiveBody(player_t player, int num)
    {
        if (player.health >= player_t.MAXHEALTH)
            return false;

        player.health += num;
        if (player.health > player_t.MAXHEALTH)
            player.health = player_t.MAXHEALTH;
        player.mo!.health = player.health;

        return true;
    }

    /// <summary>
    /// p_inter.c <c>P_GiveArmor</c>: returns false if the armor is worse
    /// than the current armor.
    /// </summary>
    public static bool P_GiveArmor(player_t player, int armortype)
    {
        int hits = armortype * 100;
        if (player.armorpoints >= hits)
            return false; // don't pick up

        player.armortype = armortype;
        player.armorpoints = hits;

        return true;
    }

    /// <summary>p_inter.c <c>P_GiveCard</c>.</summary>
    public static void P_GiveCard(player_t player, card_t card)
    {
        if (player.cards[(int)card])
            return;

        player.bonuscount = BONUSADD;
        player.cards[(int)card] = true;
    }

    /// <summary>p_inter.c <c>P_GivePower</c>.</summary>
    public static bool P_GivePower(player_t player, powertype_t power)
    {
        int p = (int)power;
        if (power == powertype_t.pw_invulnerability)
        {
            player.powers[p] = INVULNTICS;
            return true;
        }

        if (power == powertype_t.pw_invisibility)
        {
            player.powers[p] = INVISTICS;
            player.mo!.flags |= mobjflag_t.MF_SHADOW;
            return true;
        }

        if (power == powertype_t.pw_infrared)
        {
            player.powers[p] = INFRATICS;
            return true;
        }

        if (power == powertype_t.pw_ironfeet)
        {
            player.powers[p] = IRONTICS;
            return true;
        }

        if (power == powertype_t.pw_strength)
        {
            P_GiveBody(player, 100);
            player.powers[p] = 1;
            return true;
        }

        if (player.powers[p] != 0)
            return false; // already got it

        player.powers[p] = 1;
        return true;
    }

    /// <summary>
    /// p_inter.c <c>P_TouchSpecialThing</c>: <paramref name="toucher"/> (a
    /// player: only players have <c>MF_PICKUP</c>) touches
    /// <paramref name="special"/>, identified by its sprite. Ported (T5.8):
    /// armor, the bonuses, the soul- and megasphere, the keys (left for
    /// everyone in a netgame), the medikits and the power-ups. Weapons, ammo
    /// and the backpack are left lying until T6.8 (vanilla's unknown thing is
    /// an <c>I_Error</c>; both return here). A picked up thing counts as an
    /// item when <c>MF_COUNTITEM</c>, is removed, flashes the screen
    /// (<see cref="player_t.bonuscount"/>) and plays its sound.
    /// </summary>
    public void P_TouchSpecialThing(mobj_t special, mobj_t toucher)
    {
        int delta = special.z - toucher.z;

        if (delta > toucher.height || delta < -8 * Fixed.FRACUNIT)
        {
            // out of reach
            return;
        }

        sfxenum_t sound = sfxenum_t.sfx_itemup;
        player_t player = toucher.player!;

        // Dead thing touching.
        // Can happen with a sliding player corpse.
        if (toucher.health <= 0)
            return;

        // Identify by sprite.
        switch (special.sprite)
        {
            // armor
            case spritenum_t.SPR_ARM1:
                if (!P_GiveArmor(player, 1))
                    return;
                player.message = GOTARMOR;
                break;

            case spritenum_t.SPR_ARM2:
                if (!P_GiveArmor(player, 2))
                    return;
                player.message = GOTMEGA;
                break;

            // bonus items
            case spritenum_t.SPR_BON1:
                player.health++; // can go over 100%
                if (player.health > 200)
                    player.health = 200;
                player.mo!.health = player.health;
                player.message = GOTHTHBONUS;
                break;

            case spritenum_t.SPR_BON2:
                player.armorpoints++; // can go over 100%
                if (player.armorpoints > 200)
                    player.armorpoints = 200;
                if (player.armortype == 0)
                    player.armortype = 1;
                player.message = GOTARMBONUS;
                break;

            case spritenum_t.SPR_SOUL:
                player.health += 100;
                if (player.health > 200)
                    player.health = 200;
                player.mo!.health = player.health;
                player.message = GOTSUPER;
                sound = sfxenum_t.sfx_getpow;
                break;

            case spritenum_t.SPR_MEGA:
                if (gamemode != GameMode.commercial)
                    return;
                player.health = 200;
                player.mo!.health = player.health;
                P_GiveArmor(player, 2);
                player.message = GOTMSPHERE;
                sound = sfxenum_t.sfx_getpow;
                break;

            // cards
            // leave cards for everyone
            case spritenum_t.SPR_BKEY:
                if (!player.cards[(int)card_t.it_bluecard])
                    player.message = GOTBLUECARD;
                P_GiveCard(player, card_t.it_bluecard);
                if (!netgame)
                    break;
                return;

            case spritenum_t.SPR_YKEY:
                if (!player.cards[(int)card_t.it_yellowcard])
                    player.message = GOTYELWCARD;
                P_GiveCard(player, card_t.it_yellowcard);
                if (!netgame)
                    break;
                return;

            case spritenum_t.SPR_RKEY:
                if (!player.cards[(int)card_t.it_redcard])
                    player.message = GOTREDCARD;
                P_GiveCard(player, card_t.it_redcard);
                if (!netgame)
                    break;
                return;

            case spritenum_t.SPR_BSKU:
                if (!player.cards[(int)card_t.it_blueskull])
                    player.message = GOTBLUESKUL;
                P_GiveCard(player, card_t.it_blueskull);
                if (!netgame)
                    break;
                return;

            case spritenum_t.SPR_YSKU:
                if (!player.cards[(int)card_t.it_yellowskull])
                    player.message = GOTYELWSKUL;
                P_GiveCard(player, card_t.it_yellowskull);
                if (!netgame)
                    break;
                return;

            case spritenum_t.SPR_RSKU:
                if (!player.cards[(int)card_t.it_redskull])
                    player.message = GOTREDSKULL;
                P_GiveCard(player, card_t.it_redskull);
                if (!netgame)
                    break;
                return;

            // medikits, heals
            case spritenum_t.SPR_STIM:
                if (!P_GiveBody(player, 10))
                    return;
                player.message = GOTSTIM;
                break;

            case spritenum_t.SPR_MEDI:
                if (!P_GiveBody(player, 25))
                    return;

                // (vanilla tests the health after giving it, so the first message never shows)
                if (player.health < 25)
                    player.message = GOTMEDINEED;
                else
                    player.message = GOTMEDIKIT;
                break;

            // power ups
            case spritenum_t.SPR_PINV:
                if (!P_GivePower(player, powertype_t.pw_invulnerability))
                    return;
                player.message = GOTINVUL;
                sound = sfxenum_t.sfx_getpow;
                break;

            case spritenum_t.SPR_PSTR:
                if (!P_GivePower(player, powertype_t.pw_strength))
                    return;
                player.message = GOTBERSERK;
                if (player.readyweapon != weapontype_t.wp_fist)
                    player.pendingweapon = weapontype_t.wp_fist;
                sound = sfxenum_t.sfx_getpow;
                break;

            case spritenum_t.SPR_PINS:
                if (!P_GivePower(player, powertype_t.pw_invisibility))
                    return;
                player.message = GOTINVIS;
                sound = sfxenum_t.sfx_getpow;
                break;

            case spritenum_t.SPR_SUIT:
                if (!P_GivePower(player, powertype_t.pw_ironfeet))
                    return;
                player.message = GOTSUIT;
                sound = sfxenum_t.sfx_getpow;
                break;

            case spritenum_t.SPR_PMAP:
                if (!P_GivePower(player, powertype_t.pw_allmap))
                    return;
                player.message = GOTMAP;
                sound = sfxenum_t.sfx_getpow;
                break;

            case spritenum_t.SPR_PVIS:
                if (!P_GivePower(player, powertype_t.pw_infrared))
                    return;
                player.message = GOTVISOR;
                sound = sfxenum_t.sfx_getpow;
                break;

            // ammo, weapons and the backpack: T6.8
            default:
                return;
        }

        if ((special.flags & mobjflag_t.MF_COUNTITEM) != 0)
            player.itemcount++;
        P_RemoveMobj(special);
        player.bonuscount += BONUSADD;
        if (player == players[consoleplayer])
            S_StartSound((mobj_t?)null, sound);
    }

    /// <summary>
    /// p_inter.c <c>P_KillMobj</c>: <paramref name="target"/> becomes a
    /// corpse (not shootable, a quarter of its height), a killed monster
    /// counts for its killer, a player is dead (<see cref="playerstate_t.PST_DEAD"/>,
    /// counting an environment kill against itself), it enters its death
    /// (or, far enough below zero health, extreme death) state a few tics
    /// early, and the former humans drop their weapon. Only players are
    /// damaged before T6.3.
    /// </summary>
    public void P_KillMobj(mobj_t? source, mobj_t target)
    {
        target.flags &= ~(mobjflag_t.MF_SHOOTABLE | mobjflag_t.MF_FLOAT | mobjflag_t.MF_SKULLFLY);

        if (target.type != mobjtype_t.MT_SKULL)
            target.flags &= ~mobjflag_t.MF_NOGRAVITY;

        target.flags |= mobjflag_t.MF_CORPSE | mobjflag_t.MF_DROPOFF;
        target.height >>= 2;

        if (source?.player is { } killer)
        {
            // count for intermission
            if ((target.flags & mobjflag_t.MF_COUNTKILL) != 0)
                killer.killcount++;

            if (target.player is { } victim)
                killer.frags[System.Array.IndexOf(players, victim)]++;
        }
        else if (!netgame && (target.flags & mobjflag_t.MF_COUNTKILL) != 0)
        {
            // count all monster deaths,
            // even those caused by other monsters
            players[0].killcount++;
        }

        if (target.player is { } p)
        {
            // count environment kills against you
            if (source == null)
                p.frags[System.Array.IndexOf(players, p)]++;

            target.flags &= ~mobjflag_t.MF_SOLID;
            p.playerstate = playerstate_t.PST_DEAD;
            P_DropWeapon(p);
            // (the automap's AM_Stop: the presentation's)
        }

        if (target.health < -target.info.spawnhealth && target.info.xdeathstate != statenum_t.S_NULL)
            P_SetMobjState(target, target.info.xdeathstate);
        else
            P_SetMobjState(target, target.info.deathstate);
        target.tics -= P_Random() & 3;

        if (target.tics < 1)
            target.tics = 1;

        // Drop stuff.
        // This determines the kind of object spawned
        // during the death frame of a thing.
        mobjtype_t item;
        switch (target.type)
        {
            case mobjtype_t.MT_WOLFSS:
            case mobjtype_t.MT_POSSESSED:
                item = mobjtype_t.MT_CLIP;
                break;

            case mobjtype_t.MT_SHOTGUY:
                item = mobjtype_t.MT_SHOTGUN;
                break;

            case mobjtype_t.MT_CHAINGUY:
                item = mobjtype_t.MT_CHAINGUN;
                break;

            default:
                return;
        }

        mobj_t mo = P_SpawnMobj(target.x, target.y, ONFLOORZ, item);
        mo.flags |= mobjflag_t.MF_DROPPED; // special versions of items
    }

    /// <summary>
    /// p_inter.c <c>P_DamageMobj</c>: damages <paramref name="target"/>
    /// (<paramref name="inflictor"/> is the thing that caused the damage:
    /// a missile, a creature or null for slime and crushers;
    /// <paramref name="source"/> the thing to target after taking damage:
    /// a creature or null). Ported for players (T5.8): half damage on
    /// <see cref="skill_t.sk_baby"/>, the thrust away from the inflictor, the
    /// E1M8 exit sector (11) that never kills, god mode and invulnerability
    /// below 1000, the armor's share, <see cref="player_t.health"/>,
    /// <see cref="player_t.damagecount"/>, death (<see cref="P_KillMobj"/>),
    /// the pain chance (its <c>P_Random</c>) and the target change. Other
    /// targets take no damage until T6.3.
    /// </summary>
    public void P_DamageMobj(mobj_t target, mobj_t? inflictor, mobj_t? source, int damage)
    {
        if ((target.flags & mobjflag_t.MF_SHOOTABLE) == 0)
            return; // shouldn't happen...

        if (target.health <= 0)
            return;

        player_t? player = target.player;
        if (player == null)
            return; // monsters and barrels: T6.3

        if ((target.flags & mobjflag_t.MF_SKULLFLY) != 0)
            target.momx = target.momy = target.momz = 0;

        if (player != null && gameskill == skill_t.sk_baby)
            damage >>= 1; // take half damage in trainer mode

        // Some close combat weapons should not
        // inflict thrust and push the victim out of reach,
        // thus kick away unless using the chainsaw.
        if (inflictor != null
            && (target.flags & mobjflag_t.MF_NOCLIP) == 0
            && (source == null
                || source.player == null
                || source.player.readyweapon != weapontype_t.wp_chainsaw))
        {
            uint ang = Tables.R_PointToAngle2(inflictor.x, inflictor.y, target.x, target.y);

            // (vanilla's int overflow for big damage, e.g. a telefrag, kept)
            int thrust = unchecked(damage * (Fixed.FRACUNIT >> 3) * 100 / target.info.mass);

            // make fall forwards sometimes
            if (damage < 40
                && damage > target.health
                && target.z - inflictor.z > 64 * Fixed.FRACUNIT
                && (P_Random() & 1) != 0)
            {
                ang = unchecked(ang + Tables.ANG180);
                thrust *= 4;
            }

            int a = (int)(ang >> Tables.ANGLETOFINESHIFT);
            target.momx += Fixed.FixedMul(thrust, Tables.finecosine[a]);
            target.momy += Fixed.FixedMul(thrust, Tables.finesine[a]);
        }

        // player specific
        if (player != null)
        {
            // end of game hell hack
            if (target.subsector.sector.special == 11 && damage >= target.health)
                damage = target.health - 1;

            // Below certain threshold,
            // ignore damage in GOD mode, or with INVUL power.
            if (damage < 1000
                && ((player.cheats & player_t.CF_GODMODE) != 0
                    || player.powers[(int)powertype_t.pw_invulnerability] != 0))
            {
                return;
            }

            if (player.armortype != 0)
            {
                int saved;
                if (player.armortype == 1)
                    saved = damage / 3;
                else
                    saved = damage / 2;

                if (player.armorpoints <= saved)
                {
                    // armor is used up
                    saved = player.armorpoints;
                    player.armortype = 0;
                }
                player.armorpoints -= saved;
                damage -= saved;
            }
            player.health -= damage; // mirror mobj health here for Dave
            if (player.health < 0)
                player.health = 0;

            player.attacker = source;
            player.damagecount += damage; // add damage after armor / invuln

            if (player.damagecount > 100)
                player.damagecount = 100; // teleport stomp does 10k points...

            // I_Tactile: no force feedback
        }

        // do the damage
        target.health -= damage;
        if (target.health <= 0)
        {
            P_KillMobj(source, target);
            return;
        }

        if (P_Random() < target.info.painchance && (target.flags & mobjflag_t.MF_SKULLFLY) == 0)
        {
            target.flags |= mobjflag_t.MF_JUSTHIT; // fight back!

            P_SetMobjState(target, target.info.painstate);
        }

        target.reactiontime = 0; // we're awake now...

        if ((target.threshold == 0 || target.type == mobjtype_t.MT_VILE)
            && source != null && source != target
            && source.type != mobjtype_t.MT_VILE)
        {
            // if not intent on another player,
            // chase after this one
            target.target = source;
            target.threshold = BASETHRESHOLD;
            if (target.state == target.info.spawnstate && target.info.seestate != statenum_t.S_NULL)
                P_SetMobjState(target, target.info.seestate);
        }
    }
}
