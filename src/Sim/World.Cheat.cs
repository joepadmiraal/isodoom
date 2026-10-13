using System.Globalization;

namespace IsoDoom.Sim;

// st_stuff.c ST_Responder's cheats on the player (T7.9a): what each one does
// to player_t, with d_englsh.h's messages. Vanilla's responder changes the
// player between the tics, as the keys come; here the cheat comes in the
// player's ticcmd (BT_SPECIAL | BTS_CHEAT, SPEC §12 T7.9a), so the sim's only
// input stays a ticcmd per player per tic. The reading of the keys is
// m_cheat.c's (src/Game/MCheat.cs); idclev and idmus are the game flow's.

/// <summary>
/// The cheats of st_stuff.c, numbered for <see cref="buttoncode_t.BTS_CHEAT"/>
/// (not vanilla's: its responder acts on the cheat sequence that matched).
/// </summary>
public enum cheat_t
{
    cht_none,
    /// <summary><c>iddqd</c>: god mode on or off.</summary>
    cht_god,
    /// <summary><c>idfa</c>: weapons, full ammo, blue armor.</summary>
    cht_ammonokey,
    /// <summary><c>idkfa</c>: <see cref="cht_ammonokey"/> and the keys.</summary>
    cht_ammo,
    /// <summary><c>idmus</c> and two digits: the song (the game flow's).</summary>
    cht_mus,
    /// <summary><c>idspispopd</c> (Doom) or <c>idclip</c> (the others): no clipping on or off.</summary>
    cht_noclip,
    /// <summary><c>idbeholdv</c>: <see cref="powertype_t.pw_invulnerability"/>; the next five follow <see cref="powertype_t"/>'s order.</summary>
    cht_powerup_v,
    /// <summary><c>idbeholds</c>: <see cref="powertype_t.pw_strength"/>.</summary>
    cht_powerup_s,
    /// <summary><c>idbeholdi</c>: <see cref="powertype_t.pw_invisibility"/>.</summary>
    cht_powerup_i,
    /// <summary><c>idbeholdr</c>: <see cref="powertype_t.pw_ironfeet"/>.</summary>
    cht_powerup_r,
    /// <summary><c>idbeholda</c>: <see cref="powertype_t.pw_allmap"/>.</summary>
    cht_powerup_a,
    /// <summary><c>idbeholdl</c>: <see cref="powertype_t.pw_infrared"/>.</summary>
    cht_powerup_l,
    /// <summary><c>idbehold</c>: the power-ups' menu (a message).</summary>
    cht_behold,
    /// <summary><c>idchoppers</c>: the chainsaw and a tic of invulnerability.</summary>
    cht_choppers,
    /// <summary><c>idmypos</c>: the player's angle and position (a message).</summary>
    cht_mypos,
    /// <summary><c>idclev</c> and two digits: a new game on that map (the game flow's).</summary>
    cht_clev,
    NUMCHEATS,
}

public sealed partial class World
{
    // d_englsh.h
    public const string STSTR_MUS = "Music Change";
    public const string STSTR_NOMUS = "IMPOSSIBLE SELECTION";
    public const string STSTR_DQDON = "Degreelessness Mode On";
    public const string STSTR_DQDOFF = "Degreelessness Mode Off";
    public const string STSTR_KFAADDED = "Very Happy Ammo Added";
    public const string STSTR_FAADDED = "Ammo (no keys) Added";
    public const string STSTR_NCON = "No Clipping Mode ON";
    public const string STSTR_NCOFF = "No Clipping Mode OFF";
    public const string STSTR_BEHOLD = "inVuln, Str, Inviso, Rad, Allmap, or Lite-amp";
    public const string STSTR_BEHOLDX = "Power-up Toggled";
    public const string STSTR_CHOPPERS = "... doesn't suck - GM";
    public const string STSTR_CLEV = "Changing Level...";

    /// <summary>
    /// st_stuff.c <c>ST_Responder</c>'s <c>!netgame &amp;&amp; gameskill != sk_nightmare</c>:
    /// whether the cheats on the player work in this game (<c>idclev</c> only needs <c>!netgame</c>).
    /// </summary>
    public bool ST_CheatsAllowed => !netgame && gameskill != skill_t.sk_nightmare;

    /// <summary>
    /// The ticcmd of a cheat (<see cref="buttoncode_t.BTS_CHEAT"/>): <paramref name="cheat"/>
    /// and its <paramref name="param"/> in place of the buttons, the move kept
    /// (as g_game.c's <c>sendpause</c> and <c>sendsave</c>).
    /// </summary>
    public static void ST_CheatTiccmd(ref ticcmd_t cmd, cheat_t cheat, byte param)
    {
        cmd.buttons = (byte)(buttoncode_t.BT_SPECIAL | buttoncode_t.BTS_CHEAT | ((int)cheat << buttoncode_t.BTS_CHEATSHIFT));
        cmd.chatchar = param;
    }

    /// <summary>The cheat <paramref name="cmd"/> carries (<see cref="ST_CheatTiccmd"/>), or <see cref="cheat_t.cht_none"/>.</summary>
    public static cheat_t ST_TiccmdCheat(in ticcmd_t cmd) =>
        (cmd.buttons & (buttoncode_t.BT_SPECIAL | buttoncode_t.BT_SPECIALMASK)) == (buttoncode_t.BT_SPECIAL | buttoncode_t.BTS_CHEAT)
        && (cmd.buttons & buttoncode_t.BTS_CHEATMASK) >> buttoncode_t.BTS_CHEATSHIFT is var c && c < (int)cheat_t.NUMCHEATS
            ? (cheat_t)c : cheat_t.cht_none;

    /// <summary>
    /// st_stuff.c <c>ST_Responder</c>'s cheats on player <paramref name="playernum"/>
    /// (all but <c>idmus</c> and <c>idclev</c>, the game flow's): as vanilla's,
    /// only in a single player game not on nightmare (<see cref="ST_CheatsAllowed"/>),
    /// with its message. Returns whether the cheat was one of these and acted.
    /// </summary>
    public bool ST_Cheat(int playernum, cheat_t cheat)
    {
        player_t plyr = players[playernum];
        if (!ST_CheatsAllowed || !playeringame[playernum])
            return false;
        switch (cheat)
        {
            case cheat_t.cht_god:
                // 'dqd' cheat for toggleable god mode
                plyr.cheats ^= player_t.CF_GODMODE;
                if ((plyr.cheats & player_t.CF_GODMODE) != 0)
                {
                    if (plyr.mo is not null)
                        plyr.mo.health = 100;

                    plyr.health = 100;
                    plyr.message = STSTR_DQDON;
                }
                else
                    plyr.message = STSTR_DQDOFF;
                return true;

            case cheat_t.cht_ammonokey:
            case cheat_t.cht_ammo:
                // 'fa' cheat for killer fucking arsenal; 'kfa' cheat for key full ammo
                plyr.armorpoints = 200;
                plyr.armortype = 2;

                for (int i = 0; i < (int)weapontype_t.NUMWEAPONS; i++)
                    plyr.weaponowned[i] = true;

                for (int i = 0; i < (int)ammotype_t.NUMAMMO; i++)
                    plyr.ammo[i] = plyr.maxammo[i];

                if (cheat == cheat_t.cht_ammo)
                {
                    for (int i = 0; i < (int)card_t.NUMCARDS; i++)
                        plyr.cards[i] = true;
                }

                plyr.message = cheat == cheat_t.cht_ammo ? STSTR_KFAADDED : STSTR_FAADDED;
                return true;

            case cheat_t.cht_noclip:
                plyr.cheats ^= player_t.CF_NOCLIP;

                if ((plyr.cheats & player_t.CF_NOCLIP) != 0)
                    plyr.message = STSTR_NCON;
                else
                    plyr.message = STSTR_NCOFF;
                return true;

            case >= cheat_t.cht_powerup_v and <= cheat_t.cht_powerup_l:
                {
                    // 'behold?' power-up cheats
                    var i = (powertype_t)(cheat - cheat_t.cht_powerup_v);
                    if (plyr.powers[(int)i] == 0)
                        P_GivePower(plyr, i);
                    else if (i != powertype_t.pw_strength)
                        plyr.powers[(int)i] = 1;
                    else
                        plyr.powers[(int)i] = 0;

                    plyr.message = STSTR_BEHOLDX;
                    return true;
                }

            case cheat_t.cht_behold:
                // 'behold' power-up menu
                plyr.message = STSTR_BEHOLD;
                return true;

            case cheat_t.cht_choppers:
                // 'choppers' invulnerability & chainsaw
                plyr.weaponowned[(int)weapontype_t.wp_chainsaw] = true;
                plyr.powers[(int)powertype_t.pw_invulnerability] = 1; // vanilla's "= true": one tic
                plyr.message = STSTR_CHOPPERS;
                return true;

            case cheat_t.cht_mypos:
                {
                    // 'mypos' for player position (the console player's, as vanilla's)
                    mobj_t? mo = players[consoleplayer].mo;
                    if (mo is null)
                        return false;
                    plyr.message = string.Create(CultureInfo.InvariantCulture,
                        $"ang=0x{mo.angle:x};x,y=(0x{unchecked((uint)mo.x):x},0x{unchecked((uint)mo.y):x})");
                    return true;
                }
        }
        return false;
    }
}
