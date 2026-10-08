using System;
using IsoDoom.Map;
using IsoDoom.Wad;

namespace IsoDoom.Sim;

/// <summary>d_event.h <c>gameaction_t</c>: what the game loop does before the next tic.</summary>
public enum gameaction_t
{
    ga_nothing,
    ga_loadlevel,
    ga_newgame,
    ga_loadgame,
    ga_savegame,
    ga_playdemo,
    ga_completed,
    ga_victory,
    ga_worlddone,
    ga_screenshot,
}

/// <summary>d_player.h <c>wbplayerstruct_t</c>: a player's intermission stats.</summary>
public struct wbplayerstruct_t
{
    /// <summary>Whether the player is in game.</summary>
    public bool @in;
    // Player stats, kills, collected items etc.
    public int skills;
    public int sitems;
    public int ssecret;
    public int stime;
    public int[] frags;
    /// <summary>Current score on entry, modified on return.</summary>
    public int score;
}

/// <summary>
/// d_player.h <c>wbstartstruct_t</c>: what the intermission (M7) shows and
/// where the game goes next, filled by <see cref="World.G_DoCompleted"/>.
/// </summary>
public sealed class wbstartstruct_t
{
    /// <summary>Episode # (0-2).</summary>
    public int epsd;
    /// <summary>If true, splash the secret level.</summary>
    public bool didsecret;
    /// <summary>Previous and next levels, origin 0.</summary>
    public int last, next;
    public int maxkills;
    public int maxitems;
    public int maxsecret;
    public int maxfrags;
    /// <summary>The par time, in tics (T7.4: <see cref="World.pars"/>, <see cref="World.cpars"/>).</summary>
    public int partime;
    /// <summary>Index of this player in game.</summary>
    public int pnum;
    public readonly wbplayerstruct_t[] plyr = new wbplayerstruct_t[World.MAXPLAYERS];
}

// g_game.c's level flow (T5.8): the exits, the level's end and the next
// level. The game loop (the level scene now, M7's shell later) runs the game
// actions between tics, as G_Ticker does: ga_completed → G_DoCompleted (the
// intermission, M7, would show here) → G_WorldDone → load the level
// NextMapName names → G_DoWorldDone.
public sealed partial class World
{
    /// <summary>g_game.c <c>gameaction</c>: set by the exits, run by the game loop before the next tic.</summary>
    public gameaction_t gameaction;

    /// <summary>g_game.c <c>secretexit</c>: the level was left by a secret exit.</summary>
    public bool secretexit;

    /// <summary>
    /// doomstat.h <c>gameepisode</c>: the level's episode, from its name
    /// (<c>ExMy</c>: x; <c>MAPxx</c> and other names: 1).
    /// </summary>
    public int gameepisode;

    /// <summary>g_game.c <c>wminfo</c>: params for world map / intermission, set by <see cref="G_DoCompleted"/>.</summary>
    public readonly wbstartstruct_t wminfo = new();

    /// <summary>
    /// Whether the WAD has <c>MAP31</c> (vanilla's <c>W_CheckNumForName("map31")</c>
    /// in <see cref="G_SecretExitLevel"/>: a Doom II without the Wolfenstein
    /// levels has no secret exit). The game sets it from the WAD; true by default.
    /// </summary>
    public bool map31exists = true;

    /// <summary>g_game.c <c>pars</c>: the par times of episodes 1–3 in seconds, by episode and map (T7.4).</summary>
    public static readonly int[,] pars =
    {
        { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
        { 0, 30, 75, 120, 90, 165, 180, 180, 30, 165 },
        { 0, 90, 90, 90, 120, 90, 360, 240, 30, 170 },
        { 0, 90, 45, 90, 150, 90, 90, 165, 30, 135 },
    };

    /// <summary>g_game.c <c>cpars</c>: DOOM II par times in seconds, by map less 1 (T7.4).</summary>
    public static readonly int[] cpars =
    [
        30, 90, 120, 120, 90, 150, 120, 120, 270, 90, //  1-10
        210, 150, 150, 150, 210, 150, 420, 150, 210, 150, // 11-20
        240, 150, 180, 150, 150, 300, 330, 420, 300, 180, // 21-30
        120, 30, // 31-32
    ];

    /// <summary>g_game.c <c>G_ExitLevel</c>.</summary>
    public void G_ExitLevel()
    {
        secretexit = false;
        gameaction = gameaction_t.ga_completed;
    }

    /// <summary>g_game.c <c>G_SecretExitLevel</c>: here we go to the secret level (if there is one).</summary>
    public void G_SecretExitLevel()
    {
        // IF NO WOLF3D LEVELS, NO SECRET EXIT!
        if (gamemode == GameMode.commercial && !map31exists)
            secretexit = false;
        else
            secretexit = true;
        gameaction = gameaction_t.ga_completed;
    }

    /// <summary>
    /// g_game.c <c>G_PlayerFinishLevel</c>: called when a player completes a
    /// level: the power-ups and keys are taken away, invisibility, gun
    /// flashes, the light amp and the palette effects stop.
    /// </summary>
    public void G_PlayerFinishLevel(int player)
    {
        player_t p = players[player];

        Array.Clear(p.powers);
        Array.Clear(p.cards);
        p.mo!.flags &= ~mobjflag_t.MF_SHADOW; // cancel invisibility
        p.extralight = 0; // cancel gun flashes
        p.fixedcolormap = 0; // cancel ir gogles
        p.damagecount = 0; // no palette changes
        p.bonuscount = 0;
    }

    /// <summary>
    /// g_game.c <c>G_DoCompleted</c>'s sim part (<see cref="gameaction_t.ga_completed"/>):
    /// every player finishes the level (<see cref="G_PlayerFinishLevel"/>);
    /// the end of an episode before Doom II (map 8) is
    /// <see cref="gameaction_t.ga_victory"/> (the finale, M7); otherwise
    /// <see cref="wminfo"/> gets the stats and the next level (origin 0): the
    /// secret level (map 9, or Doom II's 31 and 32), back from the secret
    /// level (E1M4, E2M6, E3M7, E4M3; Doom II's 16), or the next one. The
    /// automap's <c>AM_Stop</c> and the intermission's <c>WI_Start</c> are
    /// the presentation's (M7).
    /// </summary>
    public void G_DoCompleted()
    {
        gameaction = gameaction_t.ga_nothing;

        for (int i = 0; i < MAXPLAYERS; i++)
        {
            if (playeringame[i])
                G_PlayerFinishLevel(i); // take away cards and stuff
        }

        if (gamemode != GameMode.commercial)
        {
            switch (gamemap)
            {
                case 8:
                    gameaction = gameaction_t.ga_victory;
                    return;
                case 9:
                    for (int i = 0; i < MAXPLAYERS; i++)
                        players[i].didsecret = true;
                    break;
            }
        }

        wminfo.didsecret = players[consoleplayer].didsecret;
        wminfo.epsd = gameepisode - 1;
        wminfo.last = gamemap - 1;

        // wminfo.next is 0 biased, unlike gamemap
        if (gamemode == GameMode.commercial)
        {
            if (secretexit)
            {
                switch (gamemap)
                {
                    case 15: wminfo.next = 30; break;
                    case 31: wminfo.next = 31; break;
                }
            }
            else
            {
                switch (gamemap)
                {
                    case 31:
                    case 32: wminfo.next = 15; break;
                    default: wminfo.next = gamemap; break;
                }
            }
        }
        else
        {
            if (secretexit)
                wminfo.next = 8; // go to secret level
            else if (gamemap == 9)
            {
                // returning from secret level
                switch (gameepisode)
                {
                    case 1: wminfo.next = 3; break;
                    case 2: wminfo.next = 5; break;
                    case 3: wminfo.next = 6; break;
                    case 4: wminfo.next = 2; break;
                }
            }
            else
                wminfo.next = gamemap; // go to next level
        }

        wminfo.maxkills = totalkills;
        wminfo.maxitems = totalitems;
        wminfo.maxsecret = totalsecret;
        wminfo.maxfrags = 0;

        // Set par time. Doom episode 4 doesn't have a par time, so this
        // overflows into the cpars array. It's necessary to emulate this
        // for statcheck regression testing.
        // (Not vanilla: a map past the tables, which vanilla reads out of bounds, has no par.)
        if (gamemode == GameMode.commercial)
            wminfo.partime = SimInfo.TICRATE * (gamemap - 1 is >= 0 and < 32 ? cpars[gamemap - 1] : 0);
        else if (gameepisode < 4)
            wminfo.partime = SimInfo.TICRATE * (gameepisode >= 0 && gamemap is >= 0 and < 10 ? pars[gameepisode, gamemap] : 0);
        else
            wminfo.partime = SimInfo.TICRATE * (gamemap is >= 0 and < 32 ? cpars[gamemap] : 0);
        wminfo.pnum = consoleplayer;

        for (int i = 0; i < MAXPLAYERS; i++)
        {
            wminfo.plyr[i].@in = playeringame[i];
            wminfo.plyr[i].skills = players[i].killcount;
            wminfo.plyr[i].sitems = players[i].itemcount;
            wminfo.plyr[i].ssecret = players[i].secretcount;
            wminfo.plyr[i].stime = leveltime;
            wminfo.plyr[i].frags = (int[])players[i].frags.Clone();
        }
    }

    /// <summary>
    /// g_game.c <c>G_WorldDone</c> (the intermission is over):
    /// <see cref="gameaction_t.ga_worlddone"/>, and a secret exit marks the
    /// secret level done. Doom II's text screens between maps
    /// (<c>F_StartFinale</c> after maps 6, 11, 20, 30 and the secret exits
    /// of 15 and 31) are M7's; see <see cref="G_GameEnds"/>.
    /// </summary>
    public void G_WorldDone()
    {
        gameaction = gameaction_t.ga_worlddone;

        if (secretexit)
            players[consoleplayer].didsecret = true;
    }

    /// <summary>
    /// Not vanilla: whether the level just completed ends the game, so there
    /// is no next level: an episode's end before Doom II
    /// (<see cref="gameaction_t.ga_victory"/>) or Doom II's MAP30 (whose
    /// finale, the cast call, never returns).
    /// </summary>
    public bool G_GameEnds() =>
        gameaction == gameaction_t.ga_victory || (gamemode == GameMode.commercial && gamemap == 30);

    /// <summary>
    /// The map lump name of the level <see cref="G_DoCompleted"/> chose
    /// (<see cref="wminfo"/><c>.next</c>, origin 0): <c>MAPxx</c> for Doom II,
    /// else <c>ExMy</c> of <see cref="gameepisode"/> (g_game.c
    /// <c>G_DoLoadLevel</c>'s lump name).
    /// </summary>
    public string NextMapName() => MapName(gamemode, gameepisode, wminfo.next + 1);

    /// <summary>The map lump name of episode <paramref name="episode"/>, map <paramref name="map"/>: <c>MAPxx</c> for Doom II, else <c>ExMy</c>.</summary>
    public static string MapName(GameMode mode, int episode, int map) =>
        mode == GameMode.commercial ? $"MAP{map:00}" : $"E{episode}M{map}";

    /// <summary>
    /// g_game.c <c>G_DoWorldDone</c> (<see cref="gameaction_t.ga_worlddone"/>):
    /// the next level, <paramref name="level"/> (freshly loaded, the one
    /// <see cref="NextMapName"/> names), starts with the players as they
    /// left the last one (<see cref="G_DoLoadLevel"/>).
    /// </summary>
    public void G_DoWorldDone(Level level)
    {
        G_DoLoadLevel(level);
        gameaction = gameaction_t.ga_nothing;
    }

    /// <summary>The episode of a map lump name: <c>ExMy</c> → x, anything else 1 (<see cref="gameepisode"/>).</summary>
    public static int EpisodeNumber(string name)
    {
        name = name.ToUpperInvariant();
        if (name.Length == 4 && name[0] == 'E' && name[2] == 'M' && char.IsAsciiDigit(name[1]) && char.IsAsciiDigit(name[3]))
            return name[1] - '0';
        return 1;
    }
}
