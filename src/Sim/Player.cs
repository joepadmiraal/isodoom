using IsoDoom.Map;

namespace IsoDoom.Sim;

// doomdef.h's game enums and d_player.h's player_t.

/// <summary>doomdef.h <c>card_t</c>: key cards and skulls.</summary>
public enum card_t
{
    it_bluecard,
    it_yellowcard,
    it_redcard,
    it_blueskull,
    it_yellowskull,
    it_redskull,
    NUMCARDS,
}

/// <summary>doomdef.h <c>weapontype_t</c>.</summary>
public enum weapontype_t
{
    wp_fist,
    wp_pistol,
    wp_shotgun,
    wp_chaingun,
    wp_missile,
    wp_plasma,
    wp_bfg,
    wp_chainsaw,
    wp_supershotgun,
    NUMWEAPONS,
    /// <summary>No pending weapon change.</summary>
    wp_nochange,
}

/// <summary>doomdef.h <c>ammotype_t</c>.</summary>
public enum ammotype_t
{
    /// <summary>Pistol / chaingun ammo.</summary>
    am_clip,
    /// <summary>Shotgun / double barreled shotgun.</summary>
    am_shell,
    /// <summary>Plasma rifle, BFG.</summary>
    am_cell,
    /// <summary>Missile launcher.</summary>
    am_misl,
    NUMAMMO,
    /// <summary>Unlimited for chainsaw / fist.</summary>
    am_noammo,
}

/// <summary>doomdef.h <c>powertype_t</c>.</summary>
public enum powertype_t
{
    pw_invulnerability,
    pw_strength,
    pw_invisibility,
    pw_ironfeet,
    pw_allmap,
    pw_infrared,
    NUMPOWERS,
}

/// <summary>d_player.h <c>playerstate_t</c>.</summary>
public enum playerstate_t
{
    /// <summary>Playing or camping.</summary>
    PST_LIVE,
    /// <summary>Dead on the ground, view follows killer.</summary>
    PST_DEAD,
    /// <summary>Ready to restart/respawn.</summary>
    PST_REBORN,
}

/// <summary>
/// d_player.h <c>player_t</c>: the player's state beyond its mobj. The
/// <c>ticcmd</c> is here since T4.4 (T4.5 drives the player with it); the psprites (T6.6) come with their task.
/// </summary>
public sealed class player_t
{
    /// <summary>p_local.h <c>MAXHEALTH</c>.</summary>
    public const int MAXHEALTH = 100;

    /// <summary>p_local.h <c>VIEWHEIGHT</c> (fixed_t).</summary>
    public const int VIEWHEIGHT = 41 * Fixed.FRACUNIT;

    /// <summary>p_inter.c <c>maxammo</c>: the maximum of each ammo type without a backpack.</summary>
    public static readonly int[] maxammo_table = { 200, 50, 300, 50 };

    public mobj_t? mo;
    public playerstate_t playerstate;
    public ticcmd_t cmd;

    /// <summary>Focal origin above r.z (fixed_t).</summary>
    public int viewz;
    /// <summary>Base height above floor for viewz (fixed_t).</summary>
    public int viewheight;
    /// <summary>Bob/squat speed (fixed_t).</summary>
    public int deltaviewheight;
    /// <summary>Bounded/scaled total momentum (fixed_t).</summary>
    public int bob;

    /// <summary>This is only used between levels, mo->health is used during levels.</summary>
    public int health;
    public int armorpoints;
    /// <summary>Armor type is 0-2.</summary>
    public int armortype;

    /// <summary>Power ups. invinc and invis are tic counters.</summary>
    public int[] powers = new int[(int)powertype_t.NUMPOWERS];
    public bool[] cards = new bool[(int)card_t.NUMCARDS];
    public bool backpack;

    /// <summary>Frags, kills of other players.</summary>
    public int[] frags = new int[World.MAXPLAYERS];
    public weapontype_t readyweapon;

    /// <summary>Is wp_nochange if not changing.</summary>
    public weapontype_t pendingweapon;

    public bool[] weaponowned = new bool[(int)weapontype_t.NUMWEAPONS];
    public int[] ammo = new int[(int)ammotype_t.NUMAMMO];
    public int[] maxammo = new int[(int)ammotype_t.NUMAMMO];

    /// <summary>True if button down last tic.</summary>
    public bool attackdown;
    public bool usedown;

    /// <summary>Bit flags, for cheats and debug (<see cref="CF_NOCLIP"/>…).</summary>
    public int cheats;

    // d_player.h cheat_t
    /// <summary>No clipping, walk through barriers.</summary>
    public const int CF_NOCLIP = 1;
    /// <summary>No damage, no health loss.</summary>
    public const int CF_GODMODE = 2;
    /// <summary>Not really a cheat, just a debug aid.</summary>
    public const int CF_NOMOMENTUM = 4;

    /// <summary>Refired shots are less accurate.</summary>
    public int refire;

    // For intermission stats.
    public int killcount;
    public int itemcount;
    public int secretcount;

    /// <summary>Hint messages.</summary>
    public string? message;

    // For screen flashing (red or bright).
    public int damagecount;
    public int bonuscount;

    /// <summary>Who did damage (NULL for floors/ceilings).</summary>
    public mobj_t? attacker;

    /// <summary>So gun flashes light up areas.</summary>
    public int extralight;

    /// <summary>Current PLAYPAL, ??? can be set to REDCOLORMAP for pain, etc.</summary>
    public int fixedcolormap;

    /// <summary>Player skin colorshift, 0-3 for which color to draw player.</summary>
    public int colormap;

    /// <summary>True if secret level has been done.</summary>
    public bool didsecret;

    /// <summary>
    /// <c>memset (p, 0, sizeof(*p))</c>: every field back to zero, in place, so
    /// references to the player stay valid (g_game.c <c>G_PlayerReborn</c>).
    /// </summary>
    public void Clear()
    {
        mo = null;
        playerstate = default;
        cmd = default;
        viewz = viewheight = deltaviewheight = bob = 0;
        health = armorpoints = armortype = 0;
        System.Array.Clear(powers);
        System.Array.Clear(cards);
        backpack = false;
        System.Array.Clear(frags);
        readyweapon = pendingweapon = default;
        System.Array.Clear(weaponowned);
        System.Array.Clear(ammo);
        System.Array.Clear(maxammo);
        attackdown = usedown = false;
        cheats = refire = 0;
        killcount = itemcount = secretcount = 0;
        message = null;
        damagecount = bonuscount = 0;
        attacker = null;
        extralight = fixedcolormap = colormap = 0;
        didsecret = false;
    }
}
