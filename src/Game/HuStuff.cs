using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

/// <summary>
/// hu_stuff.c and hu_lib.c's message line (T6.11, SPEC §7.6): the console
/// player's last message (<c>player_t.message</c>, which reaches the
/// presentation as the sim's <c>se_message</c> events, T6.10) in the
/// <c>STCFN*</c> font at the top left of vanilla's screen, for
/// <see cref="HU_MSGTIMEOUT"/> tics; and (T7.9) the map's title
/// (<c>w_title</c>) at the automap's bottom left while it shows. No chat. No
/// Godot types: the tests link it.
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

    /// <summary>
    /// hu_stuff.c <c>HU_Start</c> (at each level start, after <c>ST_Start</c>):
    /// no message shows; the map title (T7.9, <c>w_title</c>) is the map's of
    /// <paramref name="mission"/> (Chocolate Doom's <c>logical_gamemission</c>),
    /// episode <paramref name="episode"/>, map <paramref name="map"/> (none
    /// for a map the tables lack, where vanilla reads past their end), with
    /// the BFG Edition's names for <paramref name="variant"/> (T10.1).
    /// </summary>
    public void HU_Start(GameMission mission = GameMission.none, int episode = 1, int map = 0, GameVariant variant = GameVariant.vanilla)
    {
        message_on = false;
        message_dontfuckwithme = false;
        message_nottobefuckedwith = false;
        text = "";
        title = HU_Title(mission, episode, map, variant);
        if (title.Length > HU_MAXLINELENGTH)
            title = title[..HU_MAXLINELENGTH]; // hu_lib.c HUlib_addCharToTextLine
    }

    /// <summary>The map title (<c>w_title</c>'s text; T7.9), drawn while the automap shows.</summary>
    public string title = "";

    /// <summary>
    /// hu_stuff.c's <c>HU_TITLE</c>, <c>HU_TITLE2</c>, <c>HU_TITLEP</c>,
    /// <c>HU_TITLET</c> (<see cref="mapnames"/>, <see cref="mapnames_commercial"/>)
    /// for the mission (<see cref="GameMission.none"/>: "Unknown level", as
    /// Chocolate Doom's default); empty when the index is past the table.
    /// Doom II's maps past 32 read on into Plutonia's names, as vanilla's
    /// (MAP33: <c>PHUSTR_1</c>); T10.1: the BFG Edition's
    /// (<paramref name="variant"/>) through Chocolate Doom's
    /// <c>DEH_AddStringReplacement</c>s (<see cref="DEH_String"/>).
    /// </summary>
    public static string HU_Title(GameMission mission, int episode, int map, GameVariant variant = GameVariant.vanilla)
    {
        int i = mission switch
        {
            GameMission.doom => (episode - 1) * 9 + map - 1,
            GameMission.doom2 => map - 1,
            GameMission.pack_plut => map - 1 + 32,
            GameMission.pack_tnt => map - 1 + 64,
            _ => -1,
        };
        if (mission == GameMission.none)
            return "Unknown level";
        if (mission == GameMission.doom)
            return i >= 0 && i < mapnames.Length && map >= 1 ? mapnames[i] : "";
        return map >= 1 && i < mapnames_commercial.Length ? DEH_String(mapnames_commercial[i], variant) : "";
    }

    /// <summary>
    /// T10.1: Chocolate Doom's <c>D_DoomMain</c> string replacements for the
    /// BFG Edition (<see cref="GameVariant.bfgedition"/>): its renamed secret
    /// levels (<c>HUSTR_31</c>, <c>HUSTR_32</c>) and its MAP33, whose name
    /// vanilla reads from <c>PHUSTR_1</c>; any other string, or variant, as it is.
    /// </summary>
    public static string DEH_String(string s, GameVariant variant) => variant != GameVariant.bfgedition ? s : s switch
    {
        HUSTR_31 => "level 31: idkfa",
        HUSTR_32 => "level 32: keen",
        PHUSTR_1 => "level 33: betray",
        _ => s,
    };

    /// <summary>d_englsh.h <c>HUSTR_31</c>, <c>HUSTR_32</c> and <c>PHUSTR_1</c>: the names the BFG Edition replaces (<see cref="DEH_String"/>).</summary>
    public const string HUSTR_31 = "level 31: wolfenstein", HUSTR_32 = "level 32: grosse", PHUSTR_1 = "level 1: congo";

    /// <summary>hu_stuff.h <c>HU_TITLEX</c>.</summary>
    public const int HU_TITLEX = 0;

    /// <summary>hu_stuff.h <c>HU_TITLEY</c>: <c>167 - SHORT(hu_font[0]->height)</c> (160 with id's font).</summary>
    public int HU_TITLEY => 167 - (hu_font[0]?.Height ?? 7);

    /// <summary>
    /// The map title as drawn by the last <see cref="HU_DrawTitle"/> (T7.9):
    /// vanilla's screen rows <see cref="HU_TITLEY"/> to the bottom (a glyph
    /// taller than the first one's draws over the status bar, as vanilla's),
    /// see-through where nothing is drawn.
    /// </summary>
    public HudScreen TitleScreen => _titleScreen ??= new HudScreen(HU_TITLEY, HudScreen.SCREENHEIGHT - HU_TITLEY);

    private HudScreen? _titleScreen;

    /// <summary>hu_stuff.c <c>HU_Drawer</c>'s <c>if (automapactive) HUlib_drawTextLine(&amp;w_title, false)</c>: the title into a cleared <see cref="TitleScreen"/>.</summary>
    public void HU_DrawTitle()
    {
        TitleScreen.Clear();
        HUlib_drawTextLine(TitleScreen, HU_TITLEX, HU_TITLEY, title);
    }

    /// <summary>Builtin map names (hu_stuff.c <c>mapnames</c>: DOOM shareware/registered/retail (Ultimate) names; d_englsh.h's).</summary>
    public static readonly string[] mapnames =
    [
        "E1M1: Hangar", "E1M2: Nuclear Plant", "E1M3: Toxin Refinery", "E1M4: Command Control", "E1M5: Phobos Lab",
        "E1M6: Central Processing", "E1M7: Computer Station", "E1M8: Phobos Anomaly", "E1M9: Military Base",
        "E2M1: Deimos Anomaly", "E2M2: Containment Area", "E2M3: Refinery", "E2M4: Deimos Lab", "E2M5: Command Center",
        "E2M6: Halls of the Damned", "E2M7: Spawning Vats", "E2M8: Tower of Babel", "E2M9: Fortress of Mystery",
        "E3M1: Hell Keep", "E3M2: Slough of Despair", "E3M3: Pandemonium", "E3M4: House of Pain", "E3M5: Unholy Cathedral",
        "E3M6: Mt. Erebus", "E3M7: Limbo", "E3M8: Dis", "E3M9: Warrens",
        "E4M1: Hell Beneath", "E4M2: Perfect Hatred", "E4M3: Sever The Wicked", "E4M4: Unruly Evil", "E4M5: They Will Repent",
        "E4M6: Against Thee Wickedly", "E4M7: And Hell Followed", "E4M8: Unto The Cruel", "E4M9: Fear",
        "NEWLEVEL", "NEWLEVEL", "NEWLEVEL", "NEWLEVEL", "NEWLEVEL", "NEWLEVEL", "NEWLEVEL", "NEWLEVEL", "NEWLEVEL",
    ];

    /// <summary>hu_stuff.c <c>mapnames_commercial</c>: Doom II's, Plutonia's and TNT's, 32 each (d_englsh.h's).</summary>
    public static readonly string[] mapnames_commercial =
    [
        // DOOM 2 map names.
        "level 1: entryway", "level 2: underhalls", "level 3: the gantlet", "level 4: the focus", "level 5: the waste tunnels",
        "level 6: the crusher", "level 7: dead simple", "level 8: tricks and traps", "level 9: the pit", "level 10: refueling base",
        "level 11: 'o' of destruction!", "level 12: the factory", "level 13: downtown", "level 14: the inmost dens",
        "level 15: industrial zone", "level 16: suburbs", "level 17: tenements", "level 18: the courtyard", "level 19: the citadel",
        "level 20: gotcha!", "level 21: nirvana", "level 22: the catacombs", "level 23: barrels o' fun", "level 24: the chasm",
        "level 25: bloodfalls", "level 26: the abandoned mines", "level 27: monster condo", "level 28: the spirit world",
        "level 29: the living end", "level 30: icon of sin", "level 31: wolfenstein", "level 32: grosse",
        // Plutonia WAD map names.
        "level 1: congo", "level 2: well of souls", "level 3: aztec", "level 4: caged", "level 5: ghost town",
        "level 6: baron's lair", "level 7: caughtyard", "level 8: realm", "level 9: abattoire", "level 10: onslaught",
        "level 11: hunted", "level 12: speed", "level 13: the crypt", "level 14: genesis", "level 15: the twilight",
        "level 16: the omen", "level 17: compound", "level 18: neurosphere", "level 19: nme", "level 20: the death domain",
        "level 21: slayer", "level 22: impossible mission", "level 23: tombstone", "level 24: the final frontier",
        "level 25: the temple of darkness", "level 26: bunker", "level 27: anti-christ", "level 28: the sewers",
        "level 29: odyssey of noises", "level 30: the gateway of hell", "level 31: cyberden", "level 32: go 2 it",
        // TNT WAD map names.
        "level 1: system control", "level 2: human bbq", "level 3: power control", "level 4: wormhole", "level 5: hanger",
        "level 6: open season", "level 7: prison", "level 8: metal", "level 9: stronghold", "level 10: redemption",
        "level 11: storage facility", "level 12: crater", "level 13: nukage processing", "level 14: steel works",
        "level 15: dead zone", "level 16: deepest reaches", "level 17: processing area", "level 18: mill",
        "level 19: shipping/respawning", "level 20: central processing", "level 21: administration center", "level 22: habitat",
        "level 23: lunar mining project", "level 24: quarry", "level 25: baron's den", "level 26: ballistyx",
        "level 27: mount pain", "level 28: heck", "level 29: river styx", "level 30: last call", "level 31: pharaoh",
        "level 32: caribbean",
    ];

    /// <summary>m_menu.c <c>showMessages</c>: the console player's messages show (the menu's options toggle it, T7.2).</summary>
    public bool showMessages = true;

    /// <summary>hu_stuff.c <c>message_dontfuckwithme</c>: the next message shows even with <see cref="showMessages"/> off, and stays until its time is up (the menu's "Messages ON/OFF").</summary>
    public bool message_dontfuckwithme;

    // hu_stuff.c message_nottobefuckedwith: the message showing is one of those; others wait for it to go.
    private bool message_nottobefuckedwith;

    /// <summary>
    /// hu_stuff.c <c>HU_Ticker</c> (after <c>ST_Ticker</c>, each tic): the
    /// counter runs down, and <paramref name="message"/> (the console
    /// player's message of the tic, or null) shows for
    /// <see cref="HU_MSGTIMEOUT"/> tics, the same text again too, when
    /// <see cref="showMessages"/> is on or it is
    /// <see cref="message_dontfuckwithme"/> (which no other message replaces
    /// while it shows). Chat is not ported.
    /// </summary>
    public void HU_Ticker(string? message)
    {
        // tick down message counter if message is up
        if (message_counter != 0 && --message_counter == 0)
        {
            message_on = false;
            message_nottobefuckedwith = false;
        }

        if (showMessages || message_dontfuckwithme)
        {
            // display message if necessary
            if (message is not null && (!message_nottobefuckedwith || message_dontfuckwithme))
            {
                // hu_lib.c HUlib_addMessageToSText: a new line of at most HU_MAXLINELENGTH characters
                text = message.Length > HU_MAXLINELENGTH ? message[..HU_MAXLINELENGTH] : message;
                message_on = true;
                message_counter = HU_MSGTIMEOUT;
                message_nottobefuckedwith = message_dontfuckwithme;
                message_dontfuckwithme = false;
            }
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
        HUlib_drawTextLine(Screen, HU_MSGX, HU_MSGY, text);
    }

    // hu_lib.c HUlib_drawTextLine (no cursor): the text, upper case, from x, y into screen
    private void HUlib_drawTextLine(HudScreen screen, int x, int y, string line)
    {
        foreach (char ch in line)
        {
            int c = char.ToUpperInvariant(ch) is char u && u <= 255 ? u : 0;
            if (c != ' ' && c >= HU_FONTSTART && c <= '_' && hu_font[c - HU_FONTSTART] is IndexedImage glyph)
            {
                int w = glyph.Width;
                if (x + w > HudScreen.SCREENWIDTH)
                    break;
                screen.V_DrawPatch(x, y, glyph);
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
