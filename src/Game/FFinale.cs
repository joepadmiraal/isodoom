using System;
using IsoDoom.Sim;
using IsoDoom.Wad;

namespace IsoDoom.Game;

/// <summary>
/// f_finale.c's finale, a placeholder (T7.1) until T7.5 ports it: vanilla's
/// stages and timing (<see cref="F_Ticker"/>: the text screen for
/// <c>strlen(finaletext) * TEXTSPEED + TEXTWAIT</c> tics, then the end
/// picture; Doom II's text screens between maps skipped after 50 tics by any
/// button) with the episode's flat but a stand-in text, and the end
/// pictures (E1: <c>HELP2</c>, the retail game <c>CREDIT</c>; E2
/// <c>VICTORY2</c>; E3 <c>PFUB2</c> without the scroll; E4 <c>ENDPIC</c>).
/// The end picture stays, as vanilla's, until the menu (T7.2) starts another
/// game or ends this one. Doom II's cast call (M10) ends at the title loop. No Godot types: the tests link it.
/// </summary>
public sealed class FFinale
{
    private readonly GameFlow flow;

    public FFinale(GameFlow flow) => this.flow = flow;

    /// <summary>f_finale.c <c>TEXTSPEED</c>: tics per character.</summary>
    public const int TEXTSPEED = 3;

    /// <summary>f_finale.c <c>TEXTWAIT</c>: tics the whole text stays.</summary>
    public const int TEXTWAIT = 250;

    /// <summary>f_finale.c <c>finalestage</c>: 0 the text, 1 the end picture (2, the cast call, is M10's).</summary>
    public int finalestage;

    /// <summary>f_finale.c <c>finalecount</c>: the stage's tics.</summary>
    public int finalecount;

    /// <summary>f_finale.c <c>finaletext</c> (a stand-in until T7.5's texts).</summary>
    public string finaletext = "";

    /// <summary>f_finale.c <c>finaleflat</c>: the flat behind the text.</summary>
    public string finaleflat = "";

    private int gameepisode, gamemap;

    /// <summary>
    /// f_finale.c <c>F_StartFinale</c>: the text screen of episode
    /// <paramref name="episode"/>'s end, or Doom II's after map
    /// <paramref name="map"/> (vanilla's flats; the texts are T7.5's, the
    /// music, <c>mus_victor</c>/<c>mus_read_m</c>, T7.8's).
    /// </summary>
    public void F_StartFinale(int episode, int map)
    {
        gameepisode = episode;
        gamemap = map;
        if (flow.gamemode == GameMode.commercial)
        {
            finaleflat = map switch
            {
                6 => "SLIME16",
                11 => "RROCK14",
                20 => "RROCK07",
                30 => "RROCK17",
                15 => "RROCK13",
                31 => "RROCK19",
                _ => "F_SKY1",
            };
            finaletext = $"MAP{map:00} COMPLETE.";
        }
        else
        {
            finaleflat = episode switch
            {
                1 => "FLOOR4_8",
                2 => "SFLR6_1",
                3 => "MFLR8_4",
                4 => "MFLR8_3",
                _ => "F_SKY1",
            };
            finaletext = $"EPISODE {episode} COMPLETE.";
        }
        finalestage = 0;
        finalecount = 0;
    }

    /// <summary>
    /// f_finale.c <c>F_Ticker</c>: Doom II's text is skipped by any button
    /// after 50 tics (the next level, or after MAP30 the cast call, M10:
    /// the end here); otherwise the text shows its time, then the end picture.
    /// </summary>
    public void F_Ticker()
    {
        World world = flow.World!;

        // check for skipping
        if (flow.gamemode == GameMode.commercial && finalecount > 50)
        {
            // go on to the next level
            int i;
            for (i = 0; i < World.MAXPLAYERS; i++)
            {
                if (world.playeringame[i] && world.players[i].cmd.buttons != 0)
                    break;
            }
            if (i < World.MAXPLAYERS)
            {
                if (gamemap == 30)
                {
                    F_StartCast();
                    return;
                }
                flow.gameaction = gameaction_t.ga_worlddone;
            }
        }

        // advance animation
        finalecount++;

        if (flow.gamemode == GameMode.commercial)
            return;

        if (finalestage == 0 && finalecount > finaletext.Length * TEXTSPEED + TEXTWAIT)
        {
            finalecount = 0;
            finalestage = 1;
            // wipegamestate = -1 (a wipe: T7.1a); E3's mus_bunny: T7.8
        }
        // finalestage 1 (the end picture) stays until the menu starts a new game or ends this one (T7.2), as vanilla's
    }

    /// <summary>f_finale.c <c>F_StartCast</c>: Doom II's cast call is M10's; the game ends here (the title loop).</summary>
    private void F_StartCast() => flow.D_StartTitle($"MAP{gamemap:00} completed: the end of the game (the cast call is M10's)");

    /// <summary>
    /// f_finale.c <c>F_Drawer</c>: the text screen (<c>F_TextWrite</c>: the
    /// flat tiled, the text typed out at <see cref="TEXTSPEED"/> from
    /// 10, 10 in the message font) or the episode's end picture.
    /// </summary>
    public void F_Drawer(ScreenGraphics g, HudScreen screen)
    {
        screen.Clear();
        if (finalestage == 0)
        {
            F_TextWrite(g, screen);
            return;
        }
        string page = gameepisode switch
        {
            1 => flow.gamemode == GameMode.retail ? "CREDIT" : "HELP2",
            2 => "VICTORY2",
            3 => "PFUB2", // F_BunnyScroll: T9.x
            _ => "ENDPIC",
        };
        g.DrawPage(screen, page);
    }

    /// <summary>f_finale.c <c>F_TextWrite</c>.</summary>
    private void F_TextWrite(ScreenGraphics g, HudScreen screen)
    {
        // erase the entire screen to a tiled background
        g.TileFlat(screen, finaleflat);

        // draw some of the text onto the screen
        int cx = 10, cy = 10;
        int count = (finalecount - 10) / TEXTSPEED;
        if (count < 0)
            count = 0;
        foreach (char ch in finaletext)
        {
            if (count-- == 0)
                break;
            if (ch == '\n')
            {
                cx = 10;
                cy += 11;
                continue;
            }
            int w = g.CharWidth(ch);
            if (cx + w > HudScreen.SCREENWIDTH)
                break;
            g.DrawChar(screen, cx, cy, ch);
            cx += w;
        }
    }
}
