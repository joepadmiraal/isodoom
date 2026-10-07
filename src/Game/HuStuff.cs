using System;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

/// <summary>
/// hu_stuff.c and hu_lib.c's message line (T6.11, SPEC §7.6): the console
/// player's last message (<c>player_t.message</c>, which reaches the
/// presentation as the sim's <c>se_message</c> events, T6.10) in the
/// <c>STCFN*</c> font at the top left of vanilla's screen, for
/// <see cref="HU_MSGTIMEOUT"/> tics. No chat, no map title (the automap's,
/// M8). No Godot types: the tests link it.
/// </summary>
public sealed class HuStuff
{
    /// <summary>hu_stuff.h <c>HU_FONTSTART</c>: the first font character.</summary>
    public const char HU_FONTSTART = '!';

    /// <summary>hu_stuff.h <c>HU_FONTEND</c>: the last font character.</summary>
    public const char HU_FONTEND = '_';

    /// <summary>hu_stuff.h <c>HU_FONTSIZE</c>.</summary>
    public const int HU_FONTSIZE = HU_FONTEND - HU_FONTSTART + 1;

    public const int HU_MSGX = 0;
    public const int HU_MSGY = 0;

    /// <summary>hu_stuff.h <c>HU_MSGTIMEOUT</c>: how long a message shows, in tics.</summary>
    public const int HU_MSGTIMEOUT = 4 * Sim.SimInfo.TICRATE;

    /// <summary>hu_lib.h <c>HU_MAXLINELENGTH</c>: a text line's most characters.</summary>
    public const int HU_MAXLINELENGTH = 80;

    /// <summary>The rows of the screen <see cref="Screen"/> covers (the font is 7 or 8 rows tall).</summary>
    public const int ScreenRows = 16;

    /// <summary>The font, <c>STCFN033</c>…<c>STCFN095</c> (null where the WAD lacks one: drawn as a space).</summary>
    public IndexedImage?[] hu_font { get; } = new IndexedImage?[HU_FONTSIZE];

    public HuStuff(WadArchive? wad)
    {
        if (wad is null)
            return;
        // hu_stuff.c HU_Init
        for (int i = 0; i < HU_FONTSIZE; i++)
        {
            string name = $"STCFN{HU_FONTSTART + i:D3}";
            if (wad.Find(name) is not { } lump)
                continue;
            try
            {
                hu_font[i] = Patch.Decode(lump.Data.Span, lump.Name);
            }
            catch (WadFormatException)
            {
            }
        }
    }

    /// <summary>The message line as drawn by the last <see cref="HU_Drawer"/>: vanilla's screen rows 0–15, see-through where nothing is drawn.</summary>
    public HudScreen Screen { get; } = new(0, ScreenRows);

    /// <summary>hu_stuff.c <c>message_on</c>: whether the message shows.</summary>
    public bool message_on;

    /// <summary>hu_stuff.c <c>message_counter</c>: the tics it still shows (not reset by <see cref="HU_Start"/>, as vanilla's).</summary>
    public int message_counter;

    /// <summary>The message line's text (<c>w_message</c>'s current line, at most <see cref="HU_MAXLINELENGTH"/> characters).</summary>
    public string text = "";

    /// <summary>The message while it shows, else null.</summary>
    public string? Message => message_on ? text : null;

    /// <summary>hu_stuff.c <c>HU_Start</c> (at each level start, after <c>ST_Start</c>): no message shows.</summary>
    public void HU_Start()
    {
        message_on = false;
        text = "";
    }

    /// <summary>
    /// hu_stuff.c <c>HU_Ticker</c> (after <c>ST_Ticker</c>, each tic): the
    /// counter runs down, and <paramref name="message"/> (the console
    /// player's message of the tic, or null) shows for
    /// <see cref="HU_MSGTIMEOUT"/> tics, the same text again too.
    /// <c>showMessages</c> is on; no message is
    /// <c>message_dontfuckwithme</c> (only chat and the menus' are), so
    /// every message replaces the last.
    /// </summary>
    public void HU_Ticker(string? message)
    {
        // tick down message counter if message is up
        if (message_counter != 0 && --message_counter == 0)
            message_on = false;

        // display message if necessary
        if (message is not null)
        {
            // hu_lib.c HUlib_addMessageToSText: a new line of at most HU_MAXLINELENGTH characters
            text = message.Length > HU_MAXLINELENGTH ? message[..HU_MAXLINELENGTH] : message;
            message_on = true;
            message_counter = HU_MSGTIMEOUT;
        }
    }

    /// <summary>
    /// hu_stuff.c <c>HU_Drawer</c> (hu_lib.c <c>HUlib_drawSText</c>,
    /// <c>HUlib_drawTextLine</c>): the message, upper case, at
    /// <see cref="HU_MSGX"/>, <see cref="HU_MSGY"/> into a cleared
    /// <see cref="Screen"/>; a character outside the font (or a space)
    /// advances 4 pixels; the line stops at the screen's right edge.
    /// </summary>
    public void HU_Drawer()
    {
        Screen.Clear();
        if (!message_on)
            return;
        int x = HU_MSGX;
        foreach (char ch in text)
        {
            int c = char.ToUpperInvariant(ch) is char u && u <= 255 ? u : 0;
            if (c != ' ' && c >= HU_FONTSTART && c <= '_' && hu_font[c - HU_FONTSTART] is IndexedImage glyph)
            {
                int w = glyph.Width;
                if (x + w > HudScreen.SCREENWIDTH)
                    break;
                Screen.V_DrawPatch(x, HU_MSGY, glyph);
                x += w;
            }
            else
            {
                x += 4;
                if (x >= HudScreen.SCREENWIDTH)
                    break;
            }
        }
    }

    /// <summary>The 32-bit FNV-1a of the text's bytes (the dump's message hash; dump.c's).</summary>
    public static uint TextHash(string s)
    {
        uint hash = 2166136261u;
        foreach (char c in s)
            hash = unchecked((hash ^ (byte)c) * 16777619u);
        return hash;
    }
}
