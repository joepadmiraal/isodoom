using System;
using IsoDoom.Sim;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

/// <summary>
/// f_finale.c's finale (T7.5; Chocolate Doom's, the vanilla reference's):
/// <see cref="F_StartFinale"/> picks the text and flat of
/// <see cref="textscreens"/> by game mission, episode and map;
/// <see cref="F_Ticker"/> shows the text for
/// <c>strlen(finaletext) * TEXTSPEED + TEXTWAIT</c> tics (Doom II's text
/// screens between maps until any button after 50 tics), then the end
/// picture (<see cref="F_Drawer"/>: E1 <c>HELP2</c>, from The Ultimate Doom's executable on
/// <c>CREDIT</c>; E2 <c>VICTORY2</c>; E3 the bunny scroll, <see cref="F_BunnyScroll"/>, T9.4;
/// E4 <c>ENDPIC</c>), which stays, as
/// vanilla's, until the menu (T7.2) starts another game or ends this one.
/// Doom II's cast call after MAP30 is <c>FFinale.Cast.cs</c> (T10.5); the music
/// (<c>mus_victor</c>, <c>mus_read_m</c>, E3's <c>mus_bunny</c>, the cast
/// call's <c>mus_evil</c>) changes as vanilla's (T7.8c). The
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

    /// <summary>f_finale.c <c>finalestage_t</c>: the text, the end picture (<c>F_STAGE_ARTSCREEN</c>), the cast call (T10.5).</summary>
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
    /// after 50 tics (the next level, or after MAP30 the cast call, T10.5,
    /// which any button starts over, as vanilla's); otherwise the text shows
    /// its time, then the end picture. The cast call runs its own tic
    /// (<see cref="F_CastTicker"/>).
    /// </summary>
    public void F_Ticker()
    {
        World world = flow.World!;
        sounds.Clear();
        sounds.AddRange(_respondersounds); // T10.5: a key's death sound, between the tics
        _respondersounds.Clear();

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
                    F_StartCast();
                else
                    flow.gameaction = gameaction_t.ga_worlddone;
            }
        }

        // advance animation
        finalecount++;

        if (finalestage == F_STAGE_CAST)
        {
            F_CastTicker();
            return;
        }

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
        if (finalestage == F_STAGE_ARTSCREEN && gameepisode == 3)
            F_BunnySound();
    }

    // f_finale.c F_BunnyScroll's static laststage: the "THE END" letters shown so far
    private int _laststage;

    /// <summary>
    /// The sound half of f_finale.c <c>F_BunnyScroll</c> (T9.4): from
    /// <c>finalecount</c> 1180 a letter of <c>END1</c>–<c>END6</c> every 5
    /// tics, each with <c>sfx_pistol</c> (no origin). Vanilla starts it in
    /// the drawer, which runs after every tic (the reference's dump draws
    /// after each, D_Display after the last); here after the tic, so the
    /// sound is the tic's as in vanilla's dump and the drawer stays pure
    /// (SPEC §12 T9.4). <c>laststage</c> goes back to 0 while <c>END0</c> shows.
    /// </summary>
    private void F_BunnySound()
    {
        if (finalecount < 1130)
            return;
        if (finalecount < 1180)
        {
            _laststage = 0;
            return;
        }
        int stage = Math.Min((finalecount - 1180) / 5, 6);
        if (stage > _laststage)
        {
            S_StartSound(sfxenum_t.sfx_pistol, sounds);
            _laststage = stage;
        }
    }

    /// <summary>
    /// f_finale.c <c>F_BunnyScroll</c> (T9.4, E3's end): <c>PFUB2</c> and
    /// <c>PFUB1</c> side by side, 640 columns, scrolled from <c>PFUB1</c>
    /// (until <c>finalecount</c> 230) to <c>PFUB2</c> (from 870; a wide one's
    /// middle 320 columns) a column every 2 tics, drawn column by column (<see cref="F_DrawPatchCol"/>);
    /// from 1130 <c>END0</c> over it, from 1180 <c>END1</c>–<c>END6</c>,
    /// a stage every 5 tics, centred as a 13 × 8 block of 8 × 8.
    /// </summary>
    private void F_BunnyScroll(ScreenGraphics g, HudScreen screen)
    {
        IndexedImage? p1 = g.Patch("PFUB2"), p2 = g.Patch("PFUB1");
        // T9.4: a PFUB2 wider than the screen (the 2024 re-release's 560-wide one) scrolls by its
        // middle 320 columns, as V_DrawPatch centres it: the scroll ends on the classic picture (SPEC §12 T9.4)
        int shift = p1 is null ? 0 : HudScreen.WideShift(p1);
        int scrolled = 320 - (finalecount - 230) / 2;
        if (scrolled > 320)
            scrolled = 320;
        if (scrolled < 0)
            scrolled = 0;
        for (int x = 0; x < HudScreen.SCREENWIDTH; x++)
        {
            if (x + scrolled < 320)
                F_DrawPatchCol(screen, x, p1, x + scrolled + shift);
            else
                F_DrawPatchCol(screen, x, p2, x + scrolled - 320);
        }
        if (finalecount < 1130)
            return;
        int stage = finalecount < 1180 ? 0 : Math.Min((finalecount - 1180) / 5, 6);
        screen.V_DrawPatch((HudScreen.SCREENWIDTH - 13 * 8) / 2, (HudScreen.SCREENHEIGHT - 8 * 8) / 2, g.Patch($"END{stage}"));
    }

    /// <summary>
    /// f_finale.c <c>F_DrawPatchCol</c>: column <paramref name="col"/> of
    /// <paramref name="patch"/> at screen column <paramref name="x"/> from
    /// the top, its offsets ignored. A missing patch or column draws nothing.
    /// </summary>
    private static void F_DrawPatchCol(HudScreen screen, int x, IndexedImage? patch, int col)
    {
        if (patch is null)
            return;
        if (col < 0 || col >= patch.Width)
            return;
        for (int py = 0; py < patch.Height; py++)
        {
            int sy = py - screen.Top;
            if (sy < 0 || sy >= screen.Height || !patch.IsOpaque(col, py))
                continue;
            screen.Pixels[sy * screen.Width + x] = patch[col, py];
            screen.Opaque[sy * screen.Width + x] = 1;
        }
    }

    /// <summary>
    /// f_finale.c <c>F_Drawer</c>: the cast call (<see cref="F_CastDrawer"/>, T10.5),
    /// the text screen (<see cref="F_TextWrite"/>) or the episode's end
    /// picture (<c>F_ArtScreenDrawer</c>).
    /// </summary>
    public void F_Drawer(ScreenGraphics g, HudScreen screen)
    {
        screen.Clear();
        if (finalestage == F_STAGE_CAST)
            F_CastDrawer(g, screen);
        else if (finalestage == F_STAGE_TEXT)
            F_TextWrite(g, screen);
        else
            F_ArtScreenDrawer(g, screen);
    }

    /// <summary>The end picture of the episode (<c>F_ArtScreenDrawer</c>), or null (vanilla draws nothing).</summary>
    public string? ArtScreen() => gameepisode switch
    {
        1 => flow.gameversion >= GameVersion.exe_ultimate ? "CREDIT" : "HELP2",
        2 => "VICTORY2",
        3 => "PFUB2", // (F_BunnyScroll: PFUB2 and PFUB1 scrolled, T9.4)
        4 => "ENDPIC",
        _ => null,
    };

    /// <summary>f_finale.c <c>F_ArtScreenDrawer</c>: the end picture at 0, 0, or E3's bunny scroll (<see cref="F_BunnyScroll"/>).</summary>
    private void F_ArtScreenDrawer(ScreenGraphics g, HudScreen screen)
    {
        if (gameepisode == 3)
            F_BunnyScroll(g, screen);
        else if (ArtScreen() is { } page)
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
