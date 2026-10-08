using System;
using IsoDoom.Sim;
using IsoDoom.Wad;

namespace IsoDoom.Game;

/// <summary>
/// What the menus (<see cref="MMenu"/>) need from the game scene besides the
/// game's flow: the options they change, the console player's message, the
/// sounds, the save slots and quitting. The level scene implements it; the
/// tests fake it. Every member has a harmless default without one.
/// </summary>
public interface IMenuHost
{
    /// <summary>
    /// The options menu's screen size (m_menu.c <c>screenSize</c>): the HUD,
    /// 0 the status bar, 1 the fullscreen HUD, 2 none (the HUD's modes;
    /// SPEC §12 T7.2).
    /// </summary>
    int ScreenSize { get; set; }

    /// <summary>The menus' message for the console player (<c>players[consoleplayer].message</c>; with <paramref name="dontfuckwithme"/> shown even with the messages off, hu_stuff.c <c>message_dontfuckwithme</c>).</summary>
    void PlayerMessage(string text, bool dontfuckwithme);

    /// <summary>A menu sound (<c>S_StartSound(NULL, …)</c>; T7.7: the level scene plays it on its channels).</summary>
    void StartSound(sfxenum_t sfx);

    /// <summary>The description of save slot <paramref name="slot"/> (0–5), or null when it is empty (m_menu.c <c>M_ReadSaveStrings</c>; T7.6: <see cref="GameFlow.SaveDescription"/>).</summary>
    string? SaveDescription(int slot);

    /// <summary>i_system.c <c>I_Quit</c>: the game ends.</summary>
    void I_Quit();
}

/// <summary>
/// m_menu.c's menus (T7.2, SPEC §7.6), in plain C# (no Godot types; the
/// tests link it): the main menu (<c>M_DOOM</c>), New Game → episode
/// (<c>M_EPISOD</c>; the shareware game's "order the trilogy" message) →
/// skill (<c>M_NEWG</c>, <c>M_SKILL</c>, Nightmare's confirmation), the
/// options (end game, messages, detail, screen size, mouse sensitivity, the
/// sound volumes), load and save with their six slots, the read-this
/// screens, the quit messages, the skull (<c>M_SKULL1/2</c>), and the
/// message box. Version differences as Chocolate Doom's <c>M_Init</c> for
/// each game mode (the v1.9 executables; the Ultimate Doom's for retail),
/// and its BFG Edition workarounds (d_main.c: <c>M_GDHIGH</c>/<c>M_GDLOW</c>
/// → <c>M_MSGON</c>/<c>M_MSGOFF</c>, <c>M_SCRNSZ</c> → <c>M_DISP</c>).
/// Keys are doomkeys.h's codes and Chocolate Doom's default menu keys; the
/// gamepad and the mouse are added (not vanilla's: <see cref="KEY_PAD_ACCEPT"/>,
/// <see cref="KEY_PAD_CANCEL"/>, <see cref="M_MouseMove"/>, <see cref="M_MouseButton"/>;
/// SPEC §12 T7.2). Drawn by <see cref="M_Drawer"/> into vanilla's 320×200
/// screen. Not ported: the function keys (help, save, load, volume,
/// detail, end game, messages, quit, gamma; T7.6 ported quicksave and quickload), gamma,
/// the net game and demo checks (single
/// player only).
/// </summary>
public sealed partial class MMenu
{
    // doomkeys.h
    public const int KEY_RIGHTARROW = 0xae, KEY_LEFTARROW = 0xac, KEY_UPARROW = 0xad, KEY_DOWNARROW = 0xaf;
    public const int KEY_ESCAPE = 27, KEY_ENTER = 13, KEY_BACKSPACE = 0x7f;

    /// <summary>Not vanilla: the pad's A and the left mouse button: <see cref="key_menu_forward"/> in a menu, <see cref="key_menu_confirm"/> on a message.</summary>
    public const int KEY_PAD_ACCEPT = 0x100;

    /// <summary>Not vanilla: the pad's B and the right mouse button: <see cref="key_menu_back"/> in a menu, <see cref="key_menu_abort"/> on a message.</summary>
    public const int KEY_PAD_CANCEL = 0x101;

    // m_controls.c (Chocolate Doom): the menu keys' defaults
    public int key_menu_activate = KEY_ESCAPE;
    public int key_menu_up = KEY_UPARROW;
    public int key_menu_down = KEY_DOWNARROW;
    public int key_menu_left = KEY_LEFTARROW;
    public int key_menu_right = KEY_RIGHTARROW;
    public int key_menu_back = KEY_BACKSPACE;
    public int key_menu_forward = KEY_ENTER;
    public int key_menu_confirm = 'y';
    public int key_menu_abort = 'n';

    /// <summary>doomkeys.h <c>KEY_F6</c>, <c>KEY_F9</c>: Chocolate Doom's quicksave and quickload keys (T7.6; the game scene's <c>quicksave</c>/<c>quickload</c> actions).</summary>
    public const int KEY_F6 = 0x80 + 0x40, KEY_F9 = 0x80 + 0x43;

    // m_controls.c (Chocolate Doom): key_menu_qsave, key_menu_qload (T7.6)
    public int key_menu_qsave = KEY_F6;
    public int key_menu_qload = KEY_F9;

    public const int SKULLXOFF = -32;
    public const int LINEHEIGHT = 16;
    public const int SAVESTRINGSIZE = 24;

    // d_englsh.h
    public const string PRESSKEY = "press a key.";
    public const string PRESSYN = "press y or n.";
    public const string LOADNET = "you can't do load while in a net game!\n\n" + PRESSKEY;
    public const string SAVEDEAD = "you can't save if you aren't playing!\n\n" + PRESSKEY;
    public const string NEWGAME = "you can't start a new game\nwhile in a network game.\n\n" + PRESSKEY;
    public const string NIGHTMARE = "are you sure? this skill level\nisn't even remotely fair.\n\n" + PRESSYN;
    public const string SWSTRING = "this is the shareware version of doom.\n\nyou need to order the entire trilogy.\n\n" + PRESSKEY;
    public const string MSGOFF = "Messages OFF";
    public const string MSGON = "Messages ON";
    public const string NETEND = "you can't end a netgame!\n\n" + PRESSKEY;
    public const string ENDGAME = "are you sure you want to end the game?\n\n" + PRESSYN;
    public const string DOSY = "(press y to quit to dos.)";
    public const string DETAILHI = "High detail";
    public const string DETAILLO = "Low detail";
    public const string EMPTYSTRING = "empty slot";

    // d_englsh.h (T7.6)
    public const string QSPROMPT = "quicksave over your game named\n\n'{0}'?\n\n" + PRESSYN;
    public const string QLPROMPT = "do you want to quickload the game named\n\n'{0}'?\n\n" + PRESSYN;
    public const string QSAVESPOT = "you haven't picked a quicksave slot yet!\n\n" + PRESSKEY;
    public const string QLOADNET = "you can't quickload during a netgame!\n\n" + PRESSKEY;

    /// <summary>dstrings.h <c>NUM_QUITMESSAGES</c> (Chocolate Doom: eight for each game).</summary>
    public const int NUM_QUITMESSAGES = 8;

    /// <summary>dstrings.c <c>doom1_endmsg</c>.</summary>
    public static readonly string[] doom1_endmsg =
    [
        "are you sure you want to\nquit this great game?",
        "please don't leave, there's more\ndemons to toast!",
        "let's beat it -- this is turning\ninto a bloodbath!",
        "i wouldn't leave if i were you.\ndos is much worse.",
        "you're trying to say you like dos\nbetter than me, right?",
        "don't leave yet -- there's a\ndemon around that corner!",
        "ya know, next time you come in here\ni'm gonna toast ya.",
        "go ahead and leave. see if i care.",
    ];

    /// <summary>dstrings.c <c>doom2_endmsg</c>.</summary>
    public static readonly string[] doom2_endmsg =
    [
        "are you sure you want to\nquit this great game?",
        "you want to quit?\nthen, thou hast lost an eighth!",
        "don't go now, there's a \ndimensional shambler waiting\nat the dos prompt!",
        "get outta here and go back\nto your boring programs.",
        "if i were your boss, i'd \n deathmatch ya in a minute!",
        "look, bud. you leave now\nand you forfeit your body count!",
        "just leave. when you come\nback, i'll be waiting with a bat.",
        "you're lucky i don't smack\nyou for thinking about leaving.",
    ];

    /// <summary>m_menu.c <c>quitsounds</c>: the quit sound (Doom), by <c>(gametic &gt;&gt; 2) &amp; 7</c>.</summary>
    public static readonly sfxenum_t[] quitsounds =
    [
        sfxenum_t.sfx_pldeth, sfxenum_t.sfx_dmpain, sfxenum_t.sfx_popain, sfxenum_t.sfx_slop,
        sfxenum_t.sfx_telept, sfxenum_t.sfx_posit1, sfxenum_t.sfx_posit3, sfxenum_t.sfx_sgtatk,
    ];

    /// <summary>m_menu.c <c>quitsounds2</c> (Doom II).</summary>
    public static readonly sfxenum_t[] quitsounds2 =
    [
        sfxenum_t.sfx_vilact, sfxenum_t.sfx_getpow, sfxenum_t.sfx_boscub, sfxenum_t.sfx_slop,
        sfxenum_t.sfx_skeswg, sfxenum_t.sfx_kntdth, sfxenum_t.sfx_bspact, sfxenum_t.sfx_sgtatk,
    ];

    /// <summary>m_menu.c <c>menuitem_t</c>, with a stand-in text for a WAD without the item's patch.</summary>
    public sealed class menuitem_t
    {
        /// <summary>0 = no cursor here, 1 = ok, 2 = arrows ok (a slider), −1 = a gap.</summary>
        public short status;
        public string name;
        public Action<int>? routine;
        public char alphaKey;
        public readonly string text;

        /// <summary>Not vanilla (T7.3): a text menu's item shows this value right of its text.</summary>
        public Func<string>? value;

        public menuitem_t(short status, string name, Action<int>? routine, char alphaKey, string text = "")
        {
            this.status = status;
            this.name = name;
            this.routine = routine;
            this.alphaKey = alphaKey;
            this.text = text;
        }
    }

    /// <summary>m_menu.c <c>menu_t</c>, named for the level script and the overlay.</summary>
    public sealed class menu_t
    {
        public readonly string Name;
        public short numitems;
        public menu_t? prevMenu;
        public menuitem_t[] menuitems;
        public Action? routine;
        public short x, y;
        public short lastOn;

        /// <summary>Not vanilla (T7.3): the options' text pages, items in the message font every <see cref="lineHeight"/> rows with their values, no skull.</summary>
        public bool textItems;

        /// <summary>The rows between items (<see cref="LINEHEIGHT"/> but on text pages).</summary>
        public short lineHeight = LINEHEIGHT;

        public menu_t(string name, short numitems, menu_t? prevMenu, menuitem_t[] menuitems, Action? routine, short x, short y, short lastOn)
        {
            Name = name;
            this.numitems = numitems;
            this.prevMenu = prevMenu;
            this.menuitems = menuitems;
            this.routine = routine;
            this.x = x;
            this.y = y;
            this.lastOn = lastOn;
        }
    }

    // m_menu.c's enums
    private const int newgame = 0, options = 1, loadgame = 2, savegame = 3, readthis = 4, quitdoom = 5, main_end = 6;
    private const int ep1 = 0, ep_end = 4;
    private const int hurtme = 2, nightmare = 4, newg_end = 5;
    private const int endgame = 0, messages = 1, detail = 2, scrnsize = 3, mousesens = 5, soundvol = 7, setup = 8, opt_end = 9;
    private const int sfx_vol = 0, music_vol = 2, sound_end = 4;
    private const int load_end = 6;

    public readonly menuitem_t[] MainMenu, EpisodeMenu, NewGameMenu, OptionsMenu, ReadMenu1, ReadMenu2, SoundMenu, LoadMenu, SaveMenu;
    public readonly menu_t MainDef, EpiDef, NewDef, OptionsDef, ReadDef1, ReadDef2, SoundDef, LoadDef, SaveDef;

    private readonly GameFlow flow;
    private readonly GameVariant gamevariant;

    /// <summary>
    /// The menus of <paramref name="flow"/>'s game (its <see cref="GameFlow.gamemode"/>)
    /// and <paramref name="gamevariant"/> (the BFG Edition's workarounds),
    /// set up as m_menu.c <c>M_Init</c>.
    /// </summary>
    public MMenu(GameFlow flow, GameVariant gamevariant = GameVariant.vanilla)
    {
        this.flow = flow;
        this.gamevariant = gamevariant;

        MainMenu =
        [
            new(1, "M_NGAME", M_NewGame, 'n', "NEW GAME"),
            new(1, "M_OPTION", M_Options, 'o', "OPTIONS"),
            new(1, "M_LOADG", M_LoadGame, 'l', "LOAD GAME"),
            new(1, "M_SAVEG", M_SaveGame, 's', "SAVE GAME"),
            // Another hickup with Special edition.
            new(1, "M_RDTHIS", M_ReadThis, 'r', "READ THIS!"),
            new(1, "M_QUITG", M_QuitDOOM, 'q', "QUIT GAME"),
        ];
        MainDef = new menu_t("main", main_end, null, MainMenu, M_DrawMainMenu, 97, 64, 0);

        EpisodeMenu =
        [
            new(1, "M_EPI1", M_Episode, 'k', "KNEE-DEEP IN THE DEAD"),
            new(1, "M_EPI2", M_Episode, 't', "THE SHORES OF HELL"),
            new(1, "M_EPI3", M_Episode, 'i', "INFERNO"),
            new(1, "M_EPI4", M_Episode, 't', "THY FLESH CONSUMED"),
        ];
        EpiDef = new menu_t("episode", ep_end, MainDef, EpisodeMenu, M_DrawEpisode, 48, 63, ep1);

        NewGameMenu =
        [
            new(1, "M_JKILL", M_ChooseSkill, 'i', "I'M TOO YOUNG TO DIE."),
            new(1, "M_ROUGH", M_ChooseSkill, 'h', "HEY, NOT TOO ROUGH."),
            new(1, "M_HURT", M_ChooseSkill, 'h', "HURT ME PLENTY."),
            new(1, "M_ULTRA", M_ChooseSkill, 'u', "ULTRA-VIOLENCE."),
            new(1, "M_NMARE", M_ChooseSkill, 'n', "NIGHTMARE!"),
        ];
        NewDef = new menu_t("skill", newg_end, EpiDef, NewGameMenu, M_DrawNewGame, 48, 63, hurtme);

        OptionsMenu =
        [
            new(1, "M_ENDGAM", M_EndGame, 'e', "END GAME"),
            new(1, "M_MESSG", M_ChangeMessages, 'm', "MESSAGES:"),
            new(1, "M_DETAIL", M_ChangeDetail, 'g', "GRAPHIC DETAIL:"),
            new(2, "M_SCRNSZ", M_SizeDisplay, 's', "SCREEN SIZE"),
            new(-1, "", null, '\0'),
            new(2, "M_MSENS", M_ChangeSensitivity, 'm', "MOUSE SENSITIVITY"),
            new(-1, "", null, '\0'),
            new(1, "M_SVOL", M_Sound, 's', "SOUND VOLUME"),
            new(1, "M_ISOSET", Setup, 'o', "MORE OPTIONS..."), // not vanilla (T7.3): the text pages; no such patch, the text shows
        ];
        OptionsDef = new menu_t("options", opt_end, MainDef, OptionsMenu, M_DrawOptions, 60, 37, 0);

        ReadMenu1 = [new(1, "", M_ReadThis2, '\0')];
        ReadDef1 = new menu_t("readthis1", 1, MainDef, ReadMenu1, M_DrawReadThis1, 280, 185, 0);

        ReadMenu2 = [new(1, "", M_FinishReadThis, '\0')];
        ReadDef2 = new menu_t("readthis2", 1, ReadDef1, ReadMenu2, M_DrawReadThis2, 330, 175, 0);

        SoundMenu =
        [
            new(2, "M_SFXVOL", M_SfxVol, 's', "SFX VOLUME"),
            new(-1, "", null, '\0'),
            new(2, "M_MUSVOL", M_MusicVol, 'm', "MUSIC VOLUME"),
            new(-1, "", null, '\0'),
        ];
        SoundDef = new menu_t("sound", sound_end, OptionsDef, SoundMenu, M_DrawSound, 80, 64, 0);

        LoadMenu = new menuitem_t[load_end];
        SaveMenu = new menuitem_t[load_end];
        for (int i = 0; i < load_end; i++)
        {
            LoadMenu[i] = new menuitem_t(1, "", M_LoadSelect, (char)('1' + i));
            SaveMenu[i] = new menuitem_t(1, "", M_SaveSelect, (char)('1' + i));
        }
        LoadDef = new menu_t("load", load_end, MainDef, LoadMenu, M_DrawLoad, 80, 54, 0);
        SaveDef = new menu_t("save", load_end, MainDef, SaveMenu, M_DrawSave, 80, 54, 0);

        SetupMenus(); // T7.3
        currentMenu = MainDef;
        M_Init();
    }

    /// <summary>The scene's side (options, messages, sounds, saves, quitting); null in tests without one.</summary>
    public IMenuHost? Host { get; set; }

    /// <summary>The graphics the menus draw with and measure their text in (the message font; set by the host, the default draws nothing).</summary>
    public ScreenGraphics Graphics { get; set; } = new(null, null);

    /// <summary>m_menu.c <c>menuactive</c>: a menu (or a message) is up.</summary>
    public bool menuactive;

    /// <summary>m_menu.c <c>messageToPrint</c>: a message shows (over everything; the menu itself is not drawn meanwhile).</summary>
    public bool messageToPrint;

    /// <summary>m_menu.c <c>messageString</c>.</summary>
    public string messageString = "";

    /// <summary>m_menu.c <c>messageNeedsInput</c>: only y, n, space or Escape answer it.</summary>
    public bool messageNeedsInput;

    private bool messageLastMenuActive;
    private Action<int>? messageRoutine;

    /// <summary>m_menu.c <c>currentMenu</c>.</summary>
    public menu_t currentMenu;

    /// <summary>m_menu.c <c>itemOn</c>: the item the skull is on.</summary>
    public short itemOn;

    /// <summary>m_menu.c <c>skullAnimCounter</c> and <c>whichSkull</c>: the skull blinks every 8 tics.</summary>
    public short skullAnimCounter = 10, whichSkull;

    private static readonly string[] skullName = ["M_SKULL1", "M_SKULL2"];

    /// <summary>m_menu.c <c>inhelpscreens</c>: the last <see cref="M_Drawer"/> drew a read-this screen (a full-screen picture).</summary>
    public bool inhelpscreens;

    /// <summary>m_menu.c <c>epi</c>: the episode chosen (0-based).</summary>
    public int epi;

    /// <summary>m_menu.c <c>showMessages</c>: the pickup and door messages show (1) or not.</summary>
    public int showMessages = 1;

    /// <summary>m_menu.c <c>detailLevel</c>: toggled with its message, changes nothing (no low detail in the iso view; SPEC §12 T7.2).</summary>
    public int detailLevel;

    /// <summary>m_menu.c <c>mouseSensitivity</c> (0–9; applied with the options, T7.3).</summary>
    public int mouseSensitivity = 5;

    /// <summary>s_sound.c <c>sfxVolume</c> and <c>musicVolume</c> (0–15; the sound's <c>snd_SfxVolume</c> is <c>sfxVolume * 8</c>, T7.7; the music's <c>S_SetMusicVolume(musicVolume * 8)</c>, T7.8c).</summary>
    public int sfxVolume = 8, musicVolume = 8;

    /// <summary>m_menu.c <c>quickSaveSlot</c>: the quicksave's slot; -1 none yet, -2 picking one in the save menu (T7.6).</summary>
    public int quickSaveSlot = -1;

    // Not vanilla: the key being answered came from the pad's A (or the mouse's left button): KEY_PAD_ACCEPT (T7.6).
    private bool _padAccept;

    // The save string entry (M_SaveSelect).
    public bool saveStringEnter;
    public int saveSlot;
    private int saveCharIndex;
    private string saveOldString = "";

    /// <summary>m_menu.c <c>savegamestrings</c>: the slots' descriptions as the load and save menus show them.</summary>
    public readonly string[] savegamestrings = new string[load_end];

    /// <summary>Counts every change the menus' drawing shows (the host redraws when it changes).</summary>
    public int Changes { get; private set; }

    /// <summary>The menu sound started last (<c>S_StartSound(NULL, …)</c>), for the tests.</summary>
    public sfxenum_t? LastSound { get; private set; }

    /// <summary>Whether something of the menus shows (a menu or a message).</summary>
    public bool Active => menuactive || messageToPrint;

    /// <summary>
    /// m_menu.c <c>M_Init</c> with Chocolate Doom's version checks: Doom II
    /// drops Read This! (Quit takes its place) and goes from New Game
    /// straight to the skills, with one help page (<c>HELP</c>); the
    /// episodes before the Ultimate Doom are three (shareware: the second
    /// and third show the "order the trilogy" message); the Ultimate Doom's
    /// Read This! shows its one page (<c>HELP1</c>) and goes back to the game.
    /// </summary>
    private void M_Init()
    {
        currentMenu = MainDef;
        menuactive = false;
        itemOn = currentMenu.lastOn;
        whichSkull = 0;
        skullAnimCounter = 10;
        messageToPrint = false;
        messageString = "";
        messageLastMenuActive = menuactive;

        // gameversion >= exe_ultimate: the retail game (Chocolate Doom's default for it)
        if (flow.gamemode == GameMode.retail)
        {
            MainMenu[readthis].routine = M_ReadThis2;
            ReadDef2.prevMenu = null;
        }

        if (flow.gamemode == GameMode.commercial)
        {
            MainMenu[readthis] = MainMenu[quitdoom];
            MainDef.numitems--;
            MainDef.y += 8;
            NewDef.prevMenu = MainDef;
            ReadDef1.routine = M_DrawReadThisCommercial;
            ReadDef1.x = 330;
            ReadDef1.y = 165;
            ReadMenu1[0].routine = M_FinishReadThis;
        }

        // Versions of doom.exe before the Ultimate Doom release only had three episodes.
        if (flow.gamemode != GameMode.retail)
            EpiDef.numitems--;

        for (int i = 0; i < load_end; i++)
            savegamestrings[i] = EMPTYSTRING;
    }

    /// <summary>
    /// deh_str.c <c>DEH_String</c> with Chocolate Doom's BFG Edition
    /// replacements (d_main.c): the BFG Edition's <c>M_GDHIGH</c> says
    /// "Fullscreen:" and is too wide, so the detail item shows ON/OFF;
    /// its <c>M_SCRNSZ</c> says "Gamepad:", so the screen size shows <c>M_DISP</c>.
    /// </summary>
    public string DEH_String(string name) => gamevariant != GameVariant.bfgedition ? name : name switch
    {
        "M_GDHIGH" => "M_MSGON",
        "M_GDLOW" => "M_MSGOFF",
        "M_SCRNSZ" => "M_DISP",
        _ => name,
    };

    private void S_StartSound(sfxenum_t sfx)
    {
        LastSound = sfx;
        Host?.StartSound(sfx);
    }

    // ---- read this! ----

    private void M_ReadThis(int choice) => M_SetupNextMenu(ReadDef1);

    private void M_ReadThis2(int choice) => M_SetupNextMenu(ReadDef2);

    private void M_FinishReadThis(int choice) => M_SetupNextMenu(MainDef);

    // ---- load and save (the slots; the saving and loading are GameFlow.G_DoSaveGame and G_DoLoadGame, T7.6) ----

    /// <summary>m_menu.c <c>M_ReadSaveStrings</c>: each slot's description, an empty one can't be loaded (status 0).</summary>
    private void M_ReadSaveStrings()
    {
        for (int i = 0; i < load_end; i++)
        {
            string? description = Host?.SaveDescription(i);
            savegamestrings[i] = description ?? EMPTYSTRING;
            LoadMenu[i].status = (short)(description is null ? 0 : 1);
        }
    }

    private void M_DrawLoad()
    {
        DrawPatch(72, 28, "M_LOADG", "LOAD GAME");
        for (int i = 0; i < load_end; i++)
        {
            M_DrawSaveLoadBorder(LoadDef.x, LoadDef.y + LINEHEIGHT * i);
            M_WriteText(LoadDef.x, LoadDef.y + LINEHEIGHT * i, savegamestrings[i]);
        }
    }

    /// <summary>m_menu.c <c>M_DrawSaveLoadBorder</c>.</summary>
    private void M_DrawSaveLoadBorder(int x, int y)
    {
        DrawPatch(x - 8, y + 7, "M_LSLEFT");
        for (int i = 0; i < 24; i++)
        {
            DrawPatch(x, y + 7, "M_LSCNTR");
            x += 8;
        }
        DrawPatch(x, y + 7, "M_LSRGHT");
    }

    /// <summary>m_menu.c <c>M_LoadSelect</c>: <c>G_LoadGame</c> (T7.6), the menus close.</summary>
    private void M_LoadSelect(int choice)
    {
        flow.G_LoadGame(choice);
        M_ClearMenus();
    }

    /// <summary>m_menu.c <c>M_LoadGame</c>.</summary>
    private void M_LoadGame(int choice)
    {
        if (flow.World?.netgame == true)
        {
            M_StartMessage(LOADNET, null, false);
            return;
        }
        M_SetupNextMenu(LoadDef);
        M_ReadSaveStrings();
    }

    private void M_DrawSave()
    {
        DrawPatch(72, 28, "M_SAVEG", "SAVE GAME");
        for (int i = 0; i < load_end; i++)
        {
            M_DrawSaveLoadBorder(LoadDef.x, LoadDef.y + LINEHEIGHT * i);
            M_WriteText(LoadDef.x, LoadDef.y + LINEHEIGHT * i, savegamestrings[i]);
        }
        if (saveStringEnter)
        {
            int i = M_StringWidth(savegamestrings[saveSlot]);
            M_WriteText(LoadDef.x + i, LoadDef.y + LINEHEIGHT * saveSlot, "_");
        }
    }

    /// <summary>m_menu.c <c>M_DoSave</c>: <c>G_SaveGame</c> (saved with the next tic, T7.6), the menus close; the slot picked for a quicksave is the quicksave's from now on.</summary>
    private void M_DoSave(int slot)
    {
        flow.G_SaveGame(slot, savegamestrings[slot]);
        M_ClearMenus();

        // PICK QUICKSAVE SLOT YET?
        if (quickSaveSlot == -2)
            quickSaveSlot = slot;
    }

    /// <summary>
    /// m_menu.c <c>M_SaveSelect</c>: the slot's description is typed (an
    /// empty slot's starts empty). Not vanilla (T7.6): a pad can't type, so
    /// an empty slot chosen with A (or the mouse's left button) starts with
    /// <see cref="DefaultSaveDescription"/>, which A then saves (or the
    /// keyboard edits).
    /// </summary>
    private void M_SaveSelect(int choice)
    {
        // we are going to be intercepting all chars
        saveStringEnter = true;
        saveSlot = choice;
        saveOldString = savegamestrings[choice];
        if (savegamestrings[choice] == EMPTYSTRING)
            savegamestrings[choice] = _padAccept ? DefaultSaveDescription() : "";
        saveCharIndex = savegamestrings[choice].Length;
    }

    /// <summary>
    /// Not vanilla (T7.6): the description a save gets when the pad chose an
    /// empty slot: the map and its time, as the intermission counts it
    /// (<c>E1M3 2:05</c>), upper case as the menu font draws.
    /// </summary>
    public string DefaultSaveDescription()
    {
        if (flow.World is not { level: not null } world)
            return "SAVE";
        int seconds = world.leveltime / SimInfo.TICRATE;
        return $"{world.level.Name.ToUpperInvariant()} {seconds / 60}:{seconds % 60:00}";
    }

    // ---- quicksave and quickload (T7.6) ----

    /// <summary>m_menu.c <c>M_QuickSaveResponse</c>.</summary>
    private void M_QuickSaveResponse(int key)
    {
        if (key == key_menu_confirm)
        {
            M_DoSave(quickSaveSlot);
            S_StartSound(sfxenum_t.sfx_swtchx);
        }
    }

    /// <summary>
    /// m_menu.c <c>M_QuickSave</c>: in a game the user started, on a level,
    /// asks to save over the quicksave's slot, or, without one yet, opens the
    /// save menu to pick it.
    /// </summary>
    public void M_QuickSave()
    {
        if (!flow.usergame)
        {
            S_StartSound(sfxenum_t.sfx_oof);
            return;
        }

        if (flow.gamestate != gamestate_t.GS_LEVEL)
            return;

        if (quickSaveSlot < 0)
        {
            M_StartControlPanel();
            M_ReadSaveStrings();
            M_SetupNextMenu(SaveDef);
            quickSaveSlot = -2; // means to pick a slot now
            return;
        }
        M_StartMessage(string.Format(System.Globalization.CultureInfo.InvariantCulture, QSPROMPT, savegamestrings[quickSaveSlot]), M_QuickSaveResponse, true);
    }

    /// <summary>m_menu.c <c>M_QuickLoadResponse</c>.</summary>
    private void M_QuickLoadResponse(int key)
    {
        if (key == key_menu_confirm)
        {
            M_LoadSelect(quickSaveSlot);
            S_StartSound(sfxenum_t.sfx_swtchx);
        }
    }

    /// <summary>m_menu.c <c>M_QuickLoad</c>: asks to load the quicksave's slot, or says there is none yet.</summary>
    public void M_QuickLoad()
    {
        if (flow.World?.netgame == true)
        {
            M_StartMessage(QLOADNET, null, false);
            return;
        }

        if (quickSaveSlot < 0)
        {
            M_StartMessage(QSAVESPOT, null, false);
            return;
        }
        M_ReadSaveStrings();
        M_StartMessage(string.Format(System.Globalization.CultureInfo.InvariantCulture, QLPROMPT, savegamestrings[quickSaveSlot]), M_QuickLoadResponse, true);
    }

    /// <summary>m_menu.c <c>M_SaveGame</c>: only in a game the user started, on a level.</summary>
    private void M_SaveGame(int choice)
    {
        if (!flow.usergame)
        {
            M_StartMessage(SAVEDEAD, null, false);
            return;
        }
        if (flow.gamestate != gamestate_t.GS_LEVEL)
            return;
        M_SetupNextMenu(SaveDef);
        M_ReadSaveStrings();
    }

    // ---- the read-this screens ----

    /// <summary>Chocolate Doom's <c>M_DrawReadThis1</c> (the v1.9 executables'): <c>HELP2</c>.</summary>
    private void M_DrawReadThis1()
    {
        inhelpscreens = true;
        DrawPatch(0, 0, "HELP2");
    }

    /// <summary>Chocolate Doom's <c>M_DrawReadThis2</c>: <c>HELP1</c>.</summary>
    private void M_DrawReadThis2()
    {
        inhelpscreens = true;
        DrawPatch(0, 0, "HELP1");
    }

    /// <summary>Chocolate Doom's <c>M_DrawReadThisCommercial</c>: Doom II's one page, <c>HELP</c>.</summary>
    private void M_DrawReadThisCommercial()
    {
        inhelpscreens = true;
        DrawPatch(0, 0, "HELP");
    }

    // ---- the sound volume menu ----

    private void M_DrawSound()
    {
        DrawPatch(60, 38, "M_SVOL", "SOUND VOLUME");
        M_DrawThermo(SoundDef.x, SoundDef.y + LINEHEIGHT * (sfx_vol + 1), 16, sfxVolume);
        M_DrawThermo(SoundDef.x, SoundDef.y + LINEHEIGHT * (music_vol + 1), 16, musicVolume);
    }

    private void M_Sound(int choice) => M_SetupNextMenu(SoundDef);

    private void M_SfxVol(int choice)
    {
        switch (choice)
        {
            case 0:
                if (sfxVolume != 0)
                    sfxVolume--;
                break;
            case 1:
                if (sfxVolume < 15)
                    sfxVolume++;
                break;
        }
        // S_SetSfxVolume(sfxVolume * 8): the level scene takes it every frame (T7.7)
    }

    private void M_MusicVol(int choice)
    {
        switch (choice)
        {
            case 0:
                if (musicVolume != 0)
                    musicVolume--;
                break;
            case 1:
                if (musicVolume < 15)
                    musicVolume++;
                break;
        }
        // S_SetMusicVolume(musicVolume * 8): the game scene's, each frame the volume changed (LevelScene.SyncSoundVolume, T7.8c)
    }

    // ---- the main menu, new game, episodes, skills ----

    private void M_DrawMainMenu() => DrawPatch(94, 2, "M_DOOM");

    private void M_DrawNewGame()
    {
        DrawPatch(96, 14, "M_NEWG", "NEW GAME");
        DrawPatch(54, 38, "M_SKILL", "CHOOSE SKILL LEVEL:");
    }

    /// <summary>m_menu.c <c>M_NewGame</c>: the episodes, or Doom II's skills.</summary>
    private void M_NewGame(int choice)
    {
        if (flow.World?.netgame == true)
        {
            M_StartMessage(NEWGAME, null, false);
            return;
        }
        M_SetupNextMenu(flow.gamemode == GameMode.commercial ? NewDef : EpiDef);
    }

    private void M_DrawEpisode() => DrawPatch(54, 38, "M_EPISOD", "WHICH EPISODE?");

    private void M_VerifyNightmare(int key)
    {
        if (key != key_menu_confirm)
            return;
        flow.G_DeferedInitNew(skill_t.sk_nightmare, epi + 1, 1);
        M_ClearMenus();
    }

    /// <summary>m_menu.c <c>M_ChooseSkill</c>: a new game on episode <see cref="epi"/>'s first map (<c>G_DeferedInitNew</c>); Nightmare asks first.</summary>
    private void M_ChooseSkill(int choice)
    {
        if (choice == nightmare)
        {
            M_StartMessage(NIGHTMARE, M_VerifyNightmare, true);
            return;
        }
        flow.G_DeferedInitNew((skill_t)choice, epi + 1, 1);
        M_ClearMenus();
    }

    /// <summary>m_menu.c <c>M_Episode</c> (Chocolate Doom's): the shareware game's other episodes show the "order the trilogy" message (and the read-this menu behind it).</summary>
    private void M_Episode(int choice)
    {
        if (flow.gamemode == GameMode.shareware && choice != 0)
        {
            M_StartMessage(SWSTRING, null, false);
            M_SetupNextMenu(ReadDef1);
            return;
        }
        epi = choice;
        M_SetupNextMenu(NewDef);
    }

    // ---- the options ----

    private static readonly string[] detailNames = ["M_GDHIGH", "M_GDLOW"];
    private static readonly string[] msgNames = ["M_MSGOFF", "M_MSGON"];

    /// <summary>The thermometer cells of the screen size: the HUD's three modes (vanilla's nine view sizes; SPEC §12 T7.2).</summary>
    public const int ScreenSizes = 3;

    private void M_DrawOptions()
    {
        DrawPatch(108, 15, "M_OPTTTL", "OPTIONS");
        DrawPatch(OptionsDef.x + 175, OptionsDef.y + LINEHEIGHT * detail, detailNames[detailLevel], detailLevel == 0 ? "HIGH" : "LOW");
        DrawPatch(OptionsDef.x + 120, OptionsDef.y + LINEHEIGHT * messages, msgNames[showMessages], showMessages != 0 ? "ON" : "OFF");
        M_DrawThermo(OptionsDef.x, OptionsDef.y + LINEHEIGHT * (mousesens + 1), 10, mouseSensitivity);
        M_DrawThermo(OptionsDef.x, OptionsDef.y + LINEHEIGHT * (scrnsize + 1), ScreenSizes, screenSize);
    }

    private void M_Options(int choice) => M_SetupNextMenu(OptionsDef);

    /// <summary>m_menu.c <c>M_ChangeMessages</c>: the toggle's own message shows either way.</summary>
    private void M_ChangeMessages(int choice)
    {
        showMessages = 1 - showMessages;
        Host?.PlayerMessage(showMessages == 0 ? MSGOFF : MSGON, true);
    }

    private void M_EndGameResponse(int key)
    {
        if (key != key_menu_confirm)
            return;
        currentMenu.lastOn = itemOn;
        M_ClearMenus();
        flow.D_StartTitle(null);
    }

    /// <summary>m_menu.c <c>M_EndGame</c>: only in a game the user started.</summary>
    private void M_EndGame(int choice)
    {
        if (!flow.usergame)
        {
            S_StartSound(sfxenum_t.sfx_oof);
            return;
        }
        if (flow.World?.netgame == true)
        {
            M_StartMessage(NETEND, null, false);
            return;
        }
        M_StartMessage(ENDGAME, M_EndGameResponse, true);
    }

    private void M_QuitResponse(int key)
    {
        if (key != key_menu_confirm)
            return;
        if (flow.World?.netgame != true)
        {
            S_StartSound((flow.gamemode == GameMode.commercial ? quitsounds2 : quitsounds)[(flow.gametic >> 2) & 7]);
            // I_WaitVBL(105): the host waits for the sound before quitting (T7.7)
        }
        Host?.I_Quit();
    }

    /// <summary>Chocolate Doom's <c>M_SelectEndMessage</c>: Doom's or Doom II's quit messages by <c>gametic</c>.</summary>
    public string M_SelectEndMessage() =>
        (flow.gamemode == GameMode.commercial ? doom2_endmsg : doom1_endmsg)[flow.gametic % NUM_QUITMESSAGES];

    /// <summary>m_menu.c <c>M_QuitDOOM</c>.</summary>
    private void M_QuitDOOM(int choice) => M_StartMessage(M_SelectEndMessage() + "\n\n" + DOSY, M_QuitResponse, true);

    private void M_ChangeSensitivity(int choice)
    {
        switch (choice)
        {
            case 0:
                if (mouseSensitivity != 0)
                    mouseSensitivity--;
                break;
            case 1:
                if (mouseSensitivity < 9)
                    mouseSensitivity++;
                break;
        }
    }

    /// <summary>m_menu.c <c>M_ChangeDetail</c>: the toggle and its message only (no low detail; SPEC §12 T7.2).</summary>
    private void M_ChangeDetail(int choice)
    {
        detailLevel = 1 - detailLevel;
        Host?.PlayerMessage(detailLevel == 0 ? DETAILHI : DETAILLO, false);
    }

    private int _screenSize;

    /// <summary>m_menu.c <c>screenSize</c>: the host's HUD (<see cref="IMenuHost.ScreenSize"/>), 0 to <see cref="ScreenSizes"/> − 1.</summary>
    public int screenSize
    {
        get => Host?.ScreenSize ?? _screenSize;
        set
        {
            _screenSize = value;
            Host?.ScreenSize = value;
        }
    }

    /// <summary>m_menu.c <c>M_SizeDisplay</c>: the HUD one step (vanilla's view size; <c>R_SetViewSize</c> is the host's).</summary>
    private void M_SizeDisplay(int choice)
    {
        switch (choice)
        {
            case 0:
                if (screenSize > 0)
                    screenSize--;
                break;
            case 1:
                if (screenSize < ScreenSizes - 1)
                    screenSize++;
                break;
        }
    }

    // ---- menu functions ----

    /// <summary>m_menu.c <c>M_DrawThermo</c>.</summary>
    private void M_DrawThermo(int x, int y, int thermWidth, int thermDot)
    {
        int xx = x;
        DrawPatch(xx, y, "M_THERML");
        xx += 8;
        for (int i = 0; i < thermWidth; i++)
        {
            DrawPatch(xx, y, "M_THERMM");
            xx += 8;
        }
        DrawPatch(xx, y, "M_THERMR");
        DrawPatch(x + 8 + thermDot * 8, y, "M_THERMO", "I");
    }

    /// <summary>m_menu.c <c>M_StartMessage</c>.</summary>
    public void M_StartMessage(string text, Action<int>? routine, bool input)
    {
        messageLastMenuActive = menuactive;
        messageToPrint = true;
        messageString = text;
        messageRoutine = routine;
        messageNeedsInput = input;
        menuactive = true;
        Changes++;
    }

    /// <summary>m_menu.c <c>M_StringWidth</c>: in the message font (4 for a character it lacks).</summary>
    public int M_StringWidth(string text) => Graphics.TextWidth(text);

    /// <summary>m_menu.c <c>M_StringHeight</c>: the font's height for each line.</summary>
    public int M_StringHeight(string text)
    {
        int height = Graphics.FontHeight;
        int h = height;
        foreach (char c in text)
        {
            if (c == '\n')
                h += height;
        }
        return h;
    }

    /// <summary>m_menu.c <c>M_WriteText</c>: upper case, a new line every 12 rows, stops at the screen's right edge.</summary>
    public void M_WriteText(int x, int y, string text)
    {
        if (_screen is null)
            return;
        int cx = x, cy = y;
        foreach (char c in text)
        {
            if (c == '\n')
            {
                cx = x;
                cy += 12;
                continue;
            }
            int u = char.ToUpperInvariant(c);
            if (u - HuStuff.HU_FONTSTART < 0 || u - HuStuff.HU_FONTSTART >= HuStuff.HU_FONTSIZE)
            {
                cx += 4;
                continue;
            }
            int w = Graphics.CharWidth(c);
            if (cx + w > HudScreen.SCREENWIDTH)
                break;
            Graphics.DrawChar(_screen, cx, cy, c);
            cx += w;
        }
    }

    // ---- the responder ----

    /// <summary>
    /// m_menu.c <c>M_Responder</c> for a key press: <paramref name="key"/>
    /// (doomkeys.h's code, or a lower-case letter, digit or space, or
    /// <see cref="KEY_PAD_ACCEPT"/>/<see cref="KEY_PAD_CANCEL"/>) and the
    /// character typed, <paramref name="ch"/> (for a save's description; 0
    /// for none). Returns whether the menus took it. Closed, only
    /// <see cref="key_menu_activate"/> opens them (the title loop's "any key"
    /// is <see cref="M_StartControlPanel"/>, g_game.c <c>G_Responder</c>).
    /// </summary>
    public bool M_Responder(int key, int ch = 0)
    {
        bool taken = Respond(key, ch);
        if (taken)
            Changes++;
        return taken;
    }

    private bool Respond(int key, int ch)
    {
        _padAccept = key == KEY_PAD_ACCEPT;
        // T7.3: waiting for an input to bind (the glue takes it): Escape cancels
        if (WaitingBinding is not null)
        {
            if (key == KEY_ESCAPE)
                CancelBinding();
            return true;
        }

        // Save Game string input
        if (saveStringEnter)
        {
            // not vanilla: the pad's A (or the left button) saves, B (or the right button) cancels
            if (key == KEY_PAD_ACCEPT)
                key = KEY_ENTER;
            else if (key == KEY_PAD_CANCEL)
                key = KEY_ESCAPE;
            if (key == KEY_BACKSPACE)
            {
                if (saveCharIndex > 0)
                {
                    saveCharIndex--;
                    savegamestrings[saveSlot] = savegamestrings[saveSlot][..saveCharIndex];
                }
            }
            else if (key == KEY_ESCAPE)
            {
                saveStringEnter = false;
                savegamestrings[saveSlot] = saveOldString;
            }
            else if (key == KEY_ENTER)
            {
                saveStringEnter = false;
                if (savegamestrings[saveSlot].Length > 0)
                    M_DoSave(saveSlot);
            }
            else
            {
                int c = char.ToUpperInvariant((char)ch);
                if (c != ' ' && (c - HuStuff.HU_FONTSTART < 0 || c - HuStuff.HU_FONTSTART >= HuStuff.HU_FONTSIZE))
                    return true;
                if (c >= 32 && c <= 127 && saveCharIndex < SAVESTRINGSIZE - 1
                    && M_StringWidth(savegamestrings[saveSlot]) < (SAVESTRINGSIZE - 2) * 8)
                {
                    savegamestrings[saveSlot] += (char)c;
                    saveCharIndex++;
                }
            }
            return true;
        }

        // Not vanilla: the pad's A and B (and the mouse's buttons) answer a message or work the menu.
        if (key == KEY_PAD_ACCEPT)
            key = messageToPrint ? key_menu_confirm : key_menu_forward;
        else if (key == KEY_PAD_CANCEL)
            key = messageToPrint ? key_menu_abort : key_menu_back;

        // Take care of any messages that need input
        if (messageToPrint)
        {
            if (messageNeedsInput && key != ' ' && key != KEY_ESCAPE && key != key_menu_confirm && key != key_menu_abort)
                return false;

            menuactive = messageLastMenuActive;
            messageToPrint = false;
            messageRoutine?.Invoke(key);

            menuactive = false;
            S_StartSound(sfxenum_t.sfx_swtchx);
            return true;
        }

        // Pop-up menu?
        if (!menuactive)
        {
            // F-Keys (T7.6: quicksave and quickload only; the others are not ported, SPEC §12 T7.3)
            if (key == key_menu_qsave)
            {
                S_StartSound(sfxenum_t.sfx_swtchn);
                M_QuickSave();
                return true;
            }
            if (key == key_menu_qload)
            {
                S_StartSound(sfxenum_t.sfx_swtchn);
                M_QuickLoad();
                return true;
            }

            if (key == key_menu_activate)
            {
                M_StartControlPanel();
                S_StartSound(sfxenum_t.sfx_swtchn);
                return true;
            }
            return false;
        }

        // Keys usable within menu
        if (key == key_menu_down)
        {
            // Move down to next item
            do
            {
                if (itemOn + 1 > currentMenu.numitems - 1)
                    itemOn = 0;
                else
                    itemOn++;
                S_StartSound(sfxenum_t.sfx_pstop);
            } while (currentMenu.menuitems[itemOn].status == -1);
            return true;
        }
        if (key == key_menu_up)
        {
            // Move back up to previous item
            do
            {
                if (itemOn == 0)
                    itemOn = (short)(currentMenu.numitems - 1);
                else
                    itemOn--;
                S_StartSound(sfxenum_t.sfx_pstop);
            } while (currentMenu.menuitems[itemOn].status == -1);
            return true;
        }
        if (key == key_menu_left)
        {
            // Slide slider left
            if (currentMenu.menuitems[itemOn].routine is { } routine && currentMenu.menuitems[itemOn].status == 2)
            {
                S_StartSound(sfxenum_t.sfx_stnmov);
                routine(0);
            }
            return true;
        }
        if (key == key_menu_right)
        {
            // Slide slider right
            if (currentMenu.menuitems[itemOn].routine is { } routine && currentMenu.menuitems[itemOn].status == 2)
            {
                S_StartSound(sfxenum_t.sfx_stnmov);
                routine(1);
            }
            return true;
        }
        if (key == key_menu_forward)
        {
            // Activate menu item
            if (currentMenu.menuitems[itemOn].routine is { } routine && currentMenu.menuitems[itemOn].status != 0)
            {
                currentMenu.lastOn = itemOn;
                if (currentMenu.menuitems[itemOn].status == 2)
                {
                    routine(1); // right arrow
                    S_StartSound(sfxenum_t.sfx_stnmov);
                }
                else
                {
                    routine(itemOn);
                    S_StartSound(sfxenum_t.sfx_pistol);
                }
            }
            return true;
        }
        if (key == key_menu_activate)
        {
            // Deactivate menu
            currentMenu.lastOn = itemOn;
            M_ClearMenus();
            S_StartSound(sfxenum_t.sfx_swtchx);
            return true;
        }
        if (key == key_menu_back)
        {
            // Go back to previous menu
            currentMenu.lastOn = itemOn;
            if (currentMenu.prevMenu is { } prev)
            {
                currentMenu = prev;
                itemOn = currentMenu.lastOn;
                S_StartSound(sfxenum_t.sfx_swtchn);
            }
            return true;
        }

        // Keyboard shortcut?
        if (key > 0 && key < 0x80)
        {
            for (int i = itemOn + 1; i < currentMenu.numitems; i++)
            {
                if (currentMenu.menuitems[i].alphaKey == key)
                {
                    itemOn = (short)i;
                    S_StartSound(sfxenum_t.sfx_pstop);
                    return true;
                }
            }
            for (int i = 0; i <= itemOn; i++)
            {
                if (currentMenu.menuitems[i].alphaKey == key)
                {
                    itemOn = (short)i;
                    S_StartSound(sfxenum_t.sfx_pstop);
                    return true;
                }
            }
        }
        return false;
    }

    // ---- the mouse (not vanilla's, whose mouse moved the skull with its motion; SPEC §12 T7.2) ----

    /// <summary>The item of the current menu at screen point <paramref name="x"/>, <paramref name="y"/> (320×200), or −1.</summary>
    public int ItemAt(int x, int y)
    {
        if (!menuactive || messageToPrint || WaitingBinding is not null || x < currentMenu.x + (currentMenu.textItems ? TEXTCURSORXOFF : SKULLXOFF) || x >= HudScreen.SCREENWIDTH)
            return -1;
        int row = y - currentMenu.y + (currentMenu.textItems ? 1 : 3);
        if (row < 0)
            return -1;
        int i = row / currentMenu.lineHeight;
        return i < currentMenu.numitems && currentMenu.menuitems[i].status != -1 ? i : -1;
    }

    /// <summary>The pointer moved to screen point <paramref name="x"/>, <paramref name="y"/>: the skull goes to the item under it.</summary>
    public bool M_MouseMove(int x, int y)
    {
        if (saveStringEnter || inhelpscreens)
            return false;
        int i = ItemAt(x, y);
        if (i < 0 || i == itemOn)
            return false;
        itemOn = (short)i;
        S_StartSound(sfxenum_t.sfx_pstop);
        Changes++;
        return true;
    }

    /// <summary>
    /// A mouse button at screen point <paramref name="x"/>, <paramref name="y"/>:
    /// the left one (<paramref name="left"/>) works the item under it as
    /// <see cref="KEY_PAD_ACCEPT"/> (a slider's thermometer takes the cell
    /// clicked), answers yes to a message, turns a read-this page; the right
    /// one is <see cref="KEY_PAD_CANCEL"/>. Returns whether the menus took it.
    /// </summary>
    public bool M_MouseButton(bool left, int x, int y)
    {
        if (!Active)
            return false;
        if (!left)
            return M_Responder(KEY_PAD_CANCEL);
        if (WaitingBinding is not null)
            return true; // the glue binds the button
        if (messageToPrint || saveStringEnter || currentMenu == ReadDef1 || currentMenu == ReadDef2)
            return M_Responder(saveStringEnter ? KEY_ENTER : KEY_PAD_ACCEPT);
        if (!currentMenu.textItems && Thermo(x, y) is (int item, int cell, int value))
        {
            itemOn = (short)item;
            Action<int> routine = currentMenu.menuitems[item].routine!;
            S_StartSound(sfxenum_t.sfx_stnmov);
            for (int n = 0; n < 16 && value != cell; n++)
            {
                routine(cell > value ? 1 : 0);
                value = cell > value ? value + 1 : value - 1;
            }
            Changes++;
            return true;
        }
        int i = ItemAt(x, y);
        if (i < 0)
            return true; // nothing under the pointer
        M_MouseMove(x, y);
        return M_Responder(KEY_PAD_ACCEPT);
    }

    /// <summary>The slider whose thermometer (the row below it) is at <paramref name="x"/>, <paramref name="y"/>: its item, the cell there and its value.</summary>
    private (int Item, int Cell, int Value)? Thermo(int x, int y)
    {
        int row = y - currentMenu.y;
        if (row < 0)
            return null;
        int i = row / LINEHEIGHT - 1;
        if (i < 0 || i >= currentMenu.numitems || currentMenu.menuitems[i].status != 2)
            return null;
        (int width, int value) = SliderOf(currentMenu.menuitems[i]);
        int cell = (x - (currentMenu.x + 8)) / 8;
        if (x < currentMenu.x || cell > width)
            return null;
        return (i, Math.Clamp(cell, 0, width - 1), value);
    }

    private (int Width, int Value) SliderOf(menuitem_t item) => item.routine == M_SizeDisplay ? (ScreenSizes, screenSize)
        : item.routine == M_ChangeSensitivity ? (10, mouseSensitivity)
        : item.routine == M_SfxVol ? (16, sfxVolume)
        : (16, musicVolume);

    // ---- the drawer ----

    private HudScreen? _screen;

    private void DrawPatch(int x, int y, string name, string? text = null)
    {
        if (_screen is null)
            return;
        if (Graphics.Patch(DEH_String(name)) is { } patch)
            _screen.V_DrawPatch(x, y, patch);
        else if (text is not null)
            M_WriteText(x, y, text);
    }

    /// <summary>
    /// m_menu.c <c>M_Drawer</c> into <paramref name="screen"/> (not cleared:
    /// the menus draw over what shows): a message centred alone, or the
    /// menu's own drawing, its items and the skull. A WAD without an item's
    /// patch shows its stand-in text.
    /// </summary>
    public void M_Drawer(HudScreen screen)
    {
        _screen = screen;
        inhelpscreens = false;
        try
        {
            // Horiz. & Vertically center string and print it.
            if (messageToPrint)
            {
                string[] lines = messageString.Split('\n');
                int y = HudScreen.SCREENHEIGHT / 2 - M_StringHeight(messageString) / 2;
                foreach (string line in lines)
                {
                    int x = HudScreen.SCREENWIDTH / 2 - M_StringWidth(line) / 2;
                    M_WriteText(x, y, line);
                    y += Graphics.FontHeight;
                }
                return;
            }

            if (!menuactive)
                return;

            if (WaitingBinding is not null || currentMenu.textItems)
            {
                DrawTextMenu(); // T7.3
                return;
            }

            currentMenu.routine?.Invoke(); // call Draw routine

            // DRAW MENU
            int mx = currentMenu.x, my = currentMenu.y;
            for (int i = 0; i < currentMenu.numitems; i++)
            {
                menuitem_t item = currentMenu.menuitems[i];
                if (item.name.Length > 0)
                    DrawPatch(mx, my, item.name, item.text);
                my += LINEHEIGHT;
            }

            // DRAW SKULL
            DrawPatch(mx + SKULLXOFF, currentMenu.y - 5 + itemOn * LINEHEIGHT, skullName[whichSkull], whichSkull == 0 ? ">" : "");
        }
        finally
        {
            _screen = null;
        }
    }

    /// <summary>m_menu.c <c>M_ClearMenus</c>.</summary>
    public void M_ClearMenus()
    {
        menuactive = false;
        Changes++;
    }

    /// <summary>m_menu.c <c>M_SetupNextMenu</c>.</summary>
    public void M_SetupNextMenu(menu_t menudef)
    {
        currentMenu = menudef;
        itemOn = currentMenu.lastOn;
        Changes++;
    }

    /// <summary>m_menu.c <c>M_StartControlPanel</c>: the main menu opens on its last item.</summary>
    public void M_StartControlPanel()
    {
        // intro might call this repeatedly
        if (menuactive)
            return;
        menuactive = true;
        currentMenu = MainDef;
        itemOn = currentMenu.lastOn;
        Changes++;
    }

    /// <summary>m_menu.c <c>M_Ticker</c> (each tic, before <c>G_Ticker</c>): the skull blinks.</summary>
    public void M_Ticker()
    {
        if (--skullAnimCounter <= 0)
        {
            whichSkull ^= 1;
            skullAnimCounter = 8;
            if (Active)
                Changes++;
        }
    }

    /// <summary>What shows, for the overlay and the level script: the menu and its item, a message, or closed.</summary>
    public string StateText() =>
        messageToPrint ? $"message \"{messageString.Replace("\n", " ")}\""
        : !menuactive ? "closed"
        : WaitingBinding is { } waiting ? $"binding, waiting for an input for {waiting}"
        : currentMenu.textItems ? $"{currentMenu.Name}, item {itemOn} ({currentMenu.menuitems[itemOn].text}{(currentMenu.menuitems[itemOn].value is { } v ? $": {v()}" : "")})"
        : saveStringEnter ? $"{currentMenu.Name}, typing in slot {saveSlot + 1}: \"{savegamestrings[saveSlot]}\""
        : $"{currentMenu.Name}, item {itemOn}{(currentMenu.menuitems[itemOn].name is { Length: > 0 } n ? $" ({DEH_String(n)})" : "")}";

    /// <summary>The name of what shows (<see cref="menu_t.Name"/>, <c>message</c> or <c>closed</c>), as the level script's <c>menu NAME</c>.</summary>
    public string StateName => messageToPrint ? "message" : !menuactive ? "closed" : WaitingBinding is not null ? "binding" : currentMenu.Name;
}
