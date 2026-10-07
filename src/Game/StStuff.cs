using IsoDoom.Sim;

namespace IsoDoom.Game;

/// <summary>
/// st_stuff.c, the parts the level scene needs before the status bar (T6.11):
/// the palette flashes (T6.8, SPEC §7.4). No Godot types, so the tests link
/// it (it reads the sim's <see cref="player_t"/> and changes nothing).
/// </summary>
public static class StStuff
{
    // st_stuff.c: the PLAYPAL palettes.
    public const int STARTREDPALS = 1;
    public const int STARTBONUSPALS = 9;
    public const int NUMREDPALS = 8;
    public const int NUMBONUSPALS = 4;
    /// <summary>Radiation suit, green shift.</summary>
    public const int RADIATIONPAL = 13;

    /// <summary>
    /// st_stuff.c <c>ST_doPaletteStuff</c>: the PLAYPAL palette for
    /// <paramref name="plyr"/>'s state (vanilla sets it with
    /// <c>I_SetPalette</c> when it changes; the caller does): red for
    /// <see cref="player_t.damagecount"/> (or the berserk's fading red,
    /// whichever is stronger), else gold for <see cref="player_t.bonuscount"/>,
    /// else green while the radiation suit lasts (blinking for its last 4 s),
    /// else 0. Chocolate Doom's Chex Quest branch (red shown as green) is
    /// left out: no Chex Quest IWAD is supported.
    /// </summary>
    public static int ST_doPaletteStuff(player_t plyr)
    {
        int palette;
        int cnt = plyr.damagecount;

        if (plyr.powers[(int)powertype_t.pw_strength] != 0)
        {
            // slowly fade the berzerk out
            int bzc = 12 - (plyr.powers[(int)powertype_t.pw_strength] >> 6);

            if (bzc > cnt)
                cnt = bzc;
        }

        if (cnt != 0)
        {
            palette = (cnt + 7) >> 3;

            if (palette >= NUMREDPALS)
                palette = NUMREDPALS - 1;

            palette += STARTREDPALS;
        }
        else if (plyr.bonuscount != 0)
        {
            palette = (plyr.bonuscount + 7) >> 3;

            if (palette >= NUMBONUSPALS)
                palette = NUMBONUSPALS - 1;

            palette += STARTBONUSPALS;
        }
        else if (plyr.powers[(int)powertype_t.pw_ironfeet] > 4 * 32
                 || (plyr.powers[(int)powertype_t.pw_ironfeet] & 8) != 0)
        {
            palette = RADIATIONPAL;
        }
        else
        {
            palette = 0;
        }

        return palette;
    }
}
