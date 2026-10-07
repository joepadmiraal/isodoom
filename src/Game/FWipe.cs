using System;
using IsoDoom.Sim;

namespace IsoDoom.Game;

/// <summary>
/// f_wipe.c's melt (T7.1a, SPEC §12 T7.1a): the screen wipe d_main.c's
/// <c>D_Display</c> runs when the game state changes. The old screen slides
/// down in 160 columns two pixels wide, each starting after its own delay
/// (<see cref="y"/>, from <c>M_Random</c>), uncovering the new one. Vanilla
/// copies the screens into its frame buffer; here the melt is the columns'
/// offsets (<see cref="wipe_doMelt"/>) and <see cref="Draw"/> composes a
/// 320×200 frame from the two screens and the offsets (the tests', the
/// level check's), which the game scene's shader does over the whole window
/// (<c>WipeView</c>). No Godot types: the tests link it.
/// </summary>
public sealed class FWipe
{
    /// <summary>f_wipe.h <c>wipe_Melt</c>: the wipe d_main.c uses (<c>wipe_ColorXForm</c>, a palette fade, is unused).</summary>
    public const int wipe_Melt = 1;

    /// <summary>The columns: vanilla melts 16-bit pixels (<c>width / 2</c>).</summary>
    public const int COLUMNS = HudScreen.SCREENWIDTH / 2;

    /// <summary>
    /// f_wipe.c <c>y</c>: each column's offset in screen rows: below 0 the
    /// tics it still waits, then how far the old screen has slid down
    /// (<see cref="HudScreen.SCREENHEIGHT"/>: gone). <c>wipe_initMelt</c>
    /// sets up all 320 (drawing <c>M_Random</c> for each), the melt uses
    /// the first <see cref="COLUMNS"/>.
    /// </summary>
    public readonly int[] y = new int[HudScreen.SCREENWIDTH];

    /// <summary>f_wipe.c <c>go</c>: a wipe is under way (between <see cref="wipe_initMelt"/> and the <see cref="wipe_ScreenWipe"/> that ends it).</summary>
    public bool go;

    /// <summary>The wipes started (each <see cref="wipe_initMelt"/>), so the game scene sees a new one.</summary>
    public int count;

    /// <summary>The melt steps (<see cref="wipe_ScreenWipe"/> calls) of the current or last wipe.</summary>
    public int steps;

    /// <summary>
    /// f_wipe.c <c>wipe_initMelt</c>: the columns' starting offsets, 0 to −15
    /// apart by at most one from the column before (320 draws of
    /// <paramref name="mrandom"/>'s <c>M_Random</c>). Vanilla calls it from
    /// the first <see cref="wipe_ScreenWipe"/>, after a tic's wait in which
    /// no tic runs, so here it is at the wipe's start (<c>GameFlow.D_Display</c>).
    /// </summary>
    public void wipe_initMelt(DoomRandom mrandom)
    {
        int width = y.Length;
        y[0] = -(mrandom.M_Random() % 16);
        for (int i = 1; i < width; i++)
        {
            int r = (mrandom.M_Random() % 3) - 1;
            y[i] = y[i - 1] + r;
            if (y[i] > 0)
                y[i] = 0;
            else if (y[i] == -16)
                y[i] = -15;
        }
        go = true;
        steps = 0;
        count++;
    }

    /// <summary>
    /// f_wipe.c <c>wipe_doMelt</c> for <paramref name="ticks"/> tics: a
    /// waiting column waits one tic less; a moving one slides down by its
    /// offset + 1 while under 16 rows, then 8 rows a tic. True once a tic
    /// found nothing left to do (the screen shown is then the new one).
    /// </summary>
    public bool wipe_doMelt(int ticks)
    {
        const int height = HudScreen.SCREENHEIGHT;
        bool done = true;
        while (ticks-- > 0)
        {
            for (int i = 0; i < COLUMNS; i++)
            {
                if (y[i] < 0)
                {
                    y[i]++;
                    done = false;
                }
                else if (y[i] < height)
                {
                    int dy = y[i] < 16 ? y[i] + 1 : 8;
                    if (y[i] + dy >= height)
                        dy = height - y[i];
                    y[i] += dy;
                    done = false;
                }
            }
        }
        return done;
    }

    /// <summary>
    /// f_wipe.c <c>wipe_ScreenWipe</c> for <see cref="wipe_Melt"/> (after
    /// <see cref="wipe_initMelt"/>): a step of <paramref name="ticks"/> tics
    /// (d_main.c's wipe loop gives the tics since the last step, at least
    /// one); true when the wipe is over (<c>wipe_exitMelt</c>).
    /// </summary>
    public bool wipe_ScreenWipe(int ticks)
    {
        if (!go)
            return true;
        steps++;
        if (wipe_doMelt(ticks))
            go = false;
        return !go;
    }

    /// <summary>Ends the wipe under way at once (the option turned off, a new game state shown without one).</summary>
    public void Stop() => go = false;

    /// <summary>
    /// The frame the melt shows (vanilla's <c>wipe_scr</c> after the steps
    /// so far): in each column, the rows above its offset from
    /// <paramref name="end"/>, the rest <paramref name="start"/> slid down by
    /// it (a column still waiting shows <paramref name="start"/>). Screens are
    /// 320×200 palette indices, row-major.
    /// </summary>
    public void Draw(ReadOnlySpan<byte> start, ReadOnlySpan<byte> end, Span<byte> into)
    {
        const int width = HudScreen.SCREENWIDTH, height = HudScreen.SCREENHEIGHT;
        for (int c = 0; c < COLUMNS; c++)
        {
            int off = Math.Clamp(y[c], 0, height);
            for (int row = 0; row < height; row++)
            {
                int i = row * width + 2 * c;
                int from = row < off ? i : (row - off) * width + 2 * c;
                ReadOnlySpan<byte> source = row < off ? end : start;
                into[i] = source[from];
                into[i + 1] = source[from + 1];
            }
        }
    }

    /// <summary>The first <see cref="COLUMNS"/> offsets as the screen rows the old screen has slid down (0 while waiting), for the shader.</summary>
    public void Offsets(float[] into)
    {
        for (int c = 0; c < COLUMNS && c < into.Length; c++)
            into[c] = Math.Clamp(y[c], 0, HudScreen.SCREENHEIGHT);
    }
}
