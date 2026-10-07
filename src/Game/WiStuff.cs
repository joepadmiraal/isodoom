using System;
using IsoDoom.Sim;
using IsoDoom.Wad;

namespace IsoDoom.Game;

/// <summary>
/// wi_stuff.c's intermission, a placeholder (T7.1) until T7.4 ports it whole:
/// vanilla's states and their timing for a single player
/// (<see cref="stateenum_t.StatCount"/> until fire or use,
/// <see cref="stateenum_t.ShowNextLoc"/> for <see cref="SHOWNEXTLOCDELAY"/>
/// seconds or until fire or use, <see cref="stateenum_t.NoState"/> for 10
/// tics, then <see cref="GameFlow.G_WorldDone"/>), drawn as a sketch: the
/// background, the level names and the stats as numbers in the message font,
/// all at once (no counting, animations, par, sounds or music). No Godot
/// types: the tests link it.
/// </summary>
public sealed class WiStuff
{
    private readonly GameFlow flow;

    public WiStuff(GameFlow flow) => this.flow = flow;

    /// <summary>wi_stuff.h <c>stateenum_t</c>.</summary>
    public enum stateenum_t
    {
        NoState = -1,
        StatCount,
        ShowNextLoc,
    }

    /// <summary>wi_stuff.c <c>SHOWNEXTLOCDELAY</c>: seconds the next location shows.</summary>
    public const int SHOWNEXTLOCDELAY = 4;

    /// <summary>wi_stuff.c <c>state</c>: the intermission's stage.</summary>
    public stateenum_t state = stateenum_t.NoState;

    /// <summary>wi_stuff.c <c>wbs</c>: the level's stats and the next level (<see cref="World.wminfo"/>).</summary>
    public wbstartstruct_t? wbs;

    /// <summary>wi_stuff.c <c>plrs</c>: <see cref="wbs"/>'s players.</summary>
    public wbplayerstruct_t[] plrs => wbs!.plyr;

    /// <summary>wi_stuff.c <c>me</c>: the console player.</summary>
    public int me;

    /// <summary>wi_stuff.c <c>acceleratestage</c>: fire or use was pressed (skip to the next stage).</summary>
    public int acceleratestage;

    /// <summary>wi_stuff.c <c>cnt</c>: the tics the stage still lasts (<see cref="stateenum_t.ShowNextLoc"/>, <see cref="stateenum_t.NoState"/>).</summary>
    public int cnt;

    /// <summary>wi_stuff.c <c>bcnt</c>: the intermission's tics.</summary>
    public int bcnt;

    private World? world;

    /// <summary>wi_stuff.c <c>WI_Start</c>: the intermission of <paramref name="wbstartstruct"/> starts with the stats.</summary>
    public void WI_Start(wbstartstruct_t wbstartstruct, World world)
    {
        this.world = world;
        WI_initVariables(wbstartstruct);
        WI_initStats();
    }

    /// <summary>wi_stuff.c <c>WI_initVariables</c> (no stat is 0 of 0: the totals are at least 1).</summary>
    private void WI_initVariables(wbstartstruct_t wbstartstruct)
    {
        wbs = wbstartstruct;
        me = wbs.pnum;
        acceleratestage = 0;
        cnt = bcnt = 0;

        if (wbs.maxkills == 0)
            wbs.maxkills = 1;
        if (wbs.maxitems == 0)
            wbs.maxitems = 1;
        if (wbs.maxsecret == 0)
            wbs.maxsecret = 1;
    }

    /// <summary>wi_stuff.c <c>WI_initStats</c> (placeholder: the counts show whole at once, vanilla's <c>sp_state</c> 10).</summary>
    private void WI_initStats()
    {
        state = stateenum_t.StatCount;
        acceleratestage = 0;
    }

    /// <summary>wi_stuff.c <c>WI_updateStats</c> (placeholder: done counting): fire or use goes on (Doom II straight to the end).</summary>
    private void WI_updateStats()
    {
        if (acceleratestage != 0)
        {
            // S_StartSound(0, sfx_sgcock): T7.4, T7.7
            if (flow.gamemode == GameMode.commercial)
                WI_initNoState();
            else
                WI_initShowNextLoc();
        }
    }

    /// <summary>wi_stuff.c <c>WI_initShowNextLoc</c>.</summary>
    private void WI_initShowNextLoc()
    {
        state = stateenum_t.ShowNextLoc;
        acceleratestage = 0;
        cnt = SHOWNEXTLOCDELAY * SimInfo.TICRATE;
    }

    /// <summary>wi_stuff.c <c>WI_updateShowNextLoc</c>.</summary>
    private void WI_updateShowNextLoc()
    {
        if (--cnt == 0 || acceleratestage != 0)
            WI_initNoState();
    }

    /// <summary>wi_stuff.c <c>WI_initNoState</c>.</summary>
    private void WI_initNoState()
    {
        state = stateenum_t.NoState;
        acceleratestage = 0;
        cnt = 10;
    }

    /// <summary>wi_stuff.c <c>WI_updateNoState</c>: then the intermission ends (<see cref="GameFlow.G_WorldDone"/>).</summary>
    private void WI_updateNoState()
    {
        if (--cnt == 0)
        {
            WI_End();
            flow.G_WorldDone();
        }
    }

    /// <summary>wi_stuff.c <c>WI_End</c> (<c>WI_unloadData</c>: nothing to free).</summary>
    private void WI_End() => state = stateenum_t.NoState;

    /// <summary>
    /// wi_stuff.c <c>WI_checkForAccelerate</c>: a press of fire or use (not
    /// held from before: <see cref="player_t.attackdown"/>,
    /// <see cref="player_t.usedown"/>) by any player skips to the next stage.
    /// </summary>
    private void WI_checkForAccelerate()
    {
        for (int i = 0; i < World.MAXPLAYERS; i++)
        {
            if (!world!.playeringame[i])
                continue;
            player_t player = world.players[i];
            if ((player.cmd.buttons & buttoncode_t.BT_ATTACK) != 0)
            {
                if (!player.attackdown)
                    acceleratestage = 1;
                player.attackdown = true;
            }
            else
                player.attackdown = false;
            if ((player.cmd.buttons & buttoncode_t.BT_USE) != 0)
            {
                if (!player.usedown)
                    acceleratestage = 1;
                player.usedown = true;
            }
            else
                player.usedown = false;
        }
    }

    /// <summary>wi_stuff.c <c>WI_Ticker</c>: the stage's tic (the music, <c>mus_inter</c>, is T7.8's; the animated background T7.4's).</summary>
    public void WI_Ticker()
    {
        // counter for general background animation
        bcnt++;

        // check for button presses to skip delays
        WI_checkForAccelerate();

        switch (state)
        {
            case stateenum_t.StatCount:
                WI_updateStats();
                break;
            case stateenum_t.ShowNextLoc:
                WI_updateShowNextLoc();
                break;
            case stateenum_t.NoState:
                WI_updateNoState();
                break;
        }
    }

    // wi_stuff.c's places
    private const int SP_STATSX = 50, SP_STATSY = 50, SP_TIMEX = 16, SP_TIMEY = HudScreen.SCREENHEIGHT - 32;

    /// <summary>
    /// wi_stuff.c <c>WI_Drawer</c>, a sketch: the background (<c>WIMAPn</c>,
    /// Doom II and the retail game's fourth episode <c>INTERPIC</c>), then
    /// the level just finished (<c>WILVxy</c>/<c>CWILVxx</c> over <c>WIF</c>)
    /// with kills, items and secrets in % and the time, or the next level
    /// (<c>WIENTER</c> over its name). A missing graphic is written in the
    /// message font instead.
    /// </summary>
    public void WI_Drawer(ScreenGraphics g, HudScreen screen)
    {
        screen.Clear();
        if (wbs is null)
            return;
        string background = flow.gamemode == GameMode.commercial || (flow.gamemode == GameMode.retail && wbs.epsd == 3)
            ? "INTERPIC" : $"WIMAP{wbs.epsd}";
        g.DrawPage(screen, background);

        if (state == stateenum_t.StatCount)
        {
            int y = g.DrawCentred(screen, 2, LevelName(wbs.last), MapName(wbs.last));
            g.DrawCentred(screen, y, "WIF", "FINISHED");
            wbplayerstruct_t p = plrs[me];
            int lh = 3 * g.FontHeight / 2 + 6;
            Stat(g, screen, SP_STATSY, "WIOSTK", "KILLS", $"{p.skills * 100 / wbs.maxkills}%");
            Stat(g, screen, SP_STATSY + lh, "WIOSTI", "ITEMS", $"{p.sitems * 100 / wbs.maxitems}%");
            Stat(g, screen, SP_STATSY + 2 * lh, "WISCRT2", "SECRET", $"{p.ssecret * 100 / wbs.maxsecret}%");
            int seconds = p.stime / SimInfo.TICRATE;
            g.DrawLabel(screen, SP_TIMEX, SP_TIMEY, "WITIME", "TIME");
            g.DrawTextRight(screen, HudScreen.SCREENWIDTH / 2 - SP_TIMEX, SP_TIMEY, $"{seconds / 60}:{seconds % 60:00}");
        }
        else
        {
            int y = g.DrawCentred(screen, 2, "WIENTER", "ENTERING");
            g.DrawCentred(screen, y, LevelName(wbs.next), MapName(wbs.next));
        }
    }

    private static void Stat(ScreenGraphics g, HudScreen screen, int y, string patch, string text, string value)
    {
        g.DrawLabel(screen, SP_STATSX, y, patch, text);
        g.DrawTextRight(screen, HudScreen.SCREENWIDTH - SP_STATSX, y, value);
    }

    /// <summary>wi_stuff.c <c>lnames</c>: the level name graphic of map <paramref name="map"/> (origin 0).</summary>
    private string LevelName(int map) =>
        flow.gamemode == GameMode.commercial ? $"CWILV{map:00}" : $"WILV{wbs!.epsd}{map}";

    private string MapName(int map) => World.MapName(flow.gamemode, wbs!.epsd + 1, map + 1);
}
