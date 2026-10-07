using System;
using IsoDoom.Sim;
using IsoDoom.Wad;

namespace IsoDoom.Game;

/// <summary>
/// f_finale.c's finale (T7.5; Chocolate Doom's, the vanilla reference's):
/// <see cref="F_StartFinale"/> picks the text and flat of
/// <see cref="textscreens"/> by game mission, episode and map;
/// <see cref="F_Ticker"/> shows the text for
/// <c>strlen(finaletext) * TEXTSPEED + TEXTWAIT</c> tics (Doom II's text
/// screens between maps until any button after 50 tics), then the end
/// picture (<see cref="F_Drawer"/>: E1 <c>HELP2</c>, the retail game's
/// <c>CREDIT</c>; E2 <c>VICTORY2</c>; E4 <c>ENDPIC</c>), which stays, as
/// vanilla's, until the menu (T7.2) starts another game or ends this one.
/// E3's bunny scroll (<c>PFUB2</c> shown still until T9.4) and Doom II's cast
/// call (M10, the title loop until then) are later tasks'; the music
/// (<c>mus_victor</c>, <c>mus_read_m</c>, E3's <c>mus_bunny</c>) changes as
/// vanilla's (T7.8c; the cast call's <c>mus_evil</c> is M10's). The
/// end picture forces a wipe (T7.1a). No Godot types: the tests link it.
/// </summary>
public sealed partial class FFinale
{
    private readonly GameFlow flow;

    public FFinale(GameFlow flow) => this.flow = flow;

    /// <summary>f_finale.c <c>TEXTSPEED</c>: tics per character.</summary>
    public const int TEXTSPEED = 3;

    /// <summary>f_finale.c <c>TEXTWAIT</c>: tics the whole text stays.</summary>
    public const int TEXTWAIT = 250;

    /// <summary>f_finale.c <c>finalestage_t</c>: the text, the end picture (<c>F_STAGE_ARTSCREEN</c>), the cast call (M10's).</summary>
    public const int F_STAGE_TEXT = 0, F_STAGE_ARTSCREEN = 1, F_STAGE_CAST = 2;

    /// <summary>f_finale.c <c>finalestage</c> (<see cref="F_STAGE_TEXT"/>, …).</summary>
    public int finalestage;

    /// <summary>f_finale.c <c>finalecount</c>: the stage's tics.</summary>
    public int finalecount;

    /// <summary>f_finale.c <c>finaletext</c> (from <see cref="textscreens"/>).</summary>
    public string finaletext = "";

    /// <summary>f_finale.c <c>finaleflat</c>: the flat behind the text.</summary>
    public string finaleflat = "";

    private int gameepisode, gamemap;

    /// <summary>
    /// f_finale.c <c>F_StartFinale</c>: the text screen after map
    /// <paramref name="map"/> of episode <paramref name="episode"/> (Doom II:
    /// maps 6, 11, 20, 30 and the secret exits of 15 and 31), its text and
    /// flat from <see cref="textscreens"/> for the flow's
    /// <see cref="GameFlow.gamemission"/> (Chocolate Doom's
    /// <c>logical_gamemission</c>). Vanilla has no text for any other map
    /// (a null <c>finaletext</c>): a stand-in here. The music (T7.8c):
    /// <c>mus_victor</c>, or <c>mus_read_m</c> for any other mission.
    /// </summary>
    public void F_StartFinale(int episode, int map)
    {
        if (flow.gamemission == GameMission.doom)
            flow.S_ChangeMusic(musicenum_t.mus_victor, true);
        else
            flow.S_ChangeMusic(musicenum_t.mus_read_m, true);

        gameepisode = episode;
        gamemap = map;
        GameMission mission = flow.gamemission;
        finaletext = flow.gamemode == GameMode.commercial ? $"MAP{map:00} COMPLETE." : $"EPISODE {episode} COMPLETE.";
        finaleflat = "F_SKY1";
        foreach (textscreen_t screen in textscreens)
        {
            if (mission == screen.mission && (mission != GameMission.doom || episode == screen.episode) && map == screen.level)
            {
                finaletext = screen.text;
                finaleflat = screen.background;
            }
        }
        finalestage = F_STAGE_TEXT;
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

        if (finalestage == F_STAGE_TEXT && finalecount > finaletext.Length * TEXTSPEED + TEXTWAIT)
        {
            finalecount = 0;
            finalestage = F_STAGE_ARTSCREEN;
            flow.wipegamestate = GameFlow.GS_FORCEWIPE; // force a wipe (T7.1a)
            if (gameepisode == 3)
                flow.S_StartMusic(musicenum_t.mus_bunny);
        }
        // finalestage 1 (the end picture) stays until the menu starts a new game or ends this one (T7.2), as vanilla's
    }

    /// <summary>f_finale.c <c>F_StartCast</c>: Doom II's cast call is M10's; the game ends here (the title loop).</summary>
    private void F_StartCast() => flow.D_StartTitle($"MAP{gamemap:00} completed: the end of the game (the cast call is M10's)");

    /// <summary>
    /// f_finale.c <c>F_Drawer</c>: the text screen (<see cref="F_TextWrite"/>)
    /// or the episode's end picture (<c>F_ArtScreenDrawer</c>).
    /// </summary>
    public void F_Drawer(ScreenGraphics g, HudScreen screen)
    {
        screen.Clear();
        if (finalestage == F_STAGE_TEXT)
            F_TextWrite(g, screen);
        else
            F_ArtScreenDrawer(g, screen);
    }

    /// <summary>The end picture of the episode (<c>F_ArtScreenDrawer</c>), or null (vanilla draws nothing).</summary>
    public string? ArtScreen() => gameepisode switch
    {
        1 => flow.gamemode == GameMode.retail ? "CREDIT" : "HELP2",
        2 => "VICTORY2",
        3 => "PFUB2", // F_BunnyScroll: T9.4
        4 => "ENDPIC",
        _ => null,
    };

    /// <summary>f_finale.c <c>F_ArtScreenDrawer</c>: the end picture at 0, 0 (E3's bunny scroll shown still until T9.4).</summary>
    private void F_ArtScreenDrawer(ScreenGraphics g, HudScreen screen)
    {
        if (ArtScreen() is { } page)
            g.DrawPage(screen, page);
    }

    /// <summary>
    /// f_finale.c <c>F_TextWrite</c>: the flat tiled over the screen, then
    /// <c>(finalecount - 10) / TEXTSPEED</c> characters of the text (a line
    /// break counts as one) in the message font from 10, 10, 11 rows a line;
    /// a character the font lacks (a space) moves on 4, and a line stops at
    /// the screen's right edge.
    /// </summary>
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
            if (!g.HasGlyph(ch))
            {
                cx += 4;
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
