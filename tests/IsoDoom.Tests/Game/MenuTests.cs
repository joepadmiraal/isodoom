using System.Collections.Generic;
using IsoDoom.Game;
using IsoDoom.Sim;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Game;

/// <summary>
/// T7.2: m_menu.c's menus (<see cref="MMenu"/>) on <see cref="GameFlow"/>
/// with the game flow tests' host: navigation (wrapping, the gaps, the
/// letters, back and close), New Game → episode → skill starting the right
/// map for each game mode, the shareware game's message, Nightmare's
/// confirmation, the quit messages, End Game, the read-this screens, the
/// options, the save slots, the BFG Edition's patch names, the pad's and
/// the mouse's keys, the skull, and the world held under the menu.
/// </summary>
public class MenuTests
{
    private sealed class MenuHost : IMenuHost
    {
        public int ScreenSize { get; set; }
        public List<(string Text, bool DontFuckWithMe)> Messages { get; } = new();
        public List<sfxenum_t> Sounds { get; } = new();
        public int Quits;
        public string?[] Saves = new string?[6];

        public void PlayerMessage(string text, bool dontfuckwithme) => Messages.Add((text, dontfuckwithme));
        public void StartSound(sfxenum_t sfx) => Sounds.Add(sfx);
        public string? SaveDescription(int slot) => Saves[slot];
        public void I_Quit() => Quits++;
    }

    private static (GameFlow Flow, GameFlowTests.Host Host, MMenu Menu, MenuHost MenuHost) New(GameMode mode = GameMode.shareware)
    {
        (GameFlow flow, GameFlowTests.Host host) = GameFlowTests.New(mode);
        var menuHost = new MenuHost();
        flow.Menu.Host = menuHost;
        flow.D_StartTitle(null);
        return (flow, host, flow.Menu, menuHost);
    }

    private static void Keys(MMenu menu, params int[] keys)
    {
        foreach (int key in keys)
            menu.M_Responder(key);
    }

    private const int Esc = MMenu.KEY_ESCAPE, Enter = MMenu.KEY_ENTER, Up = MMenu.KEY_UPARROW, Down = MMenu.KEY_DOWNARROW;
    private const int Left = MMenu.KEY_LEFTARROW, Right = MMenu.KEY_RIGHTARROW, Back = MMenu.KEY_BACKSPACE;

    // ---- navigation ----

    [Fact]
    public void EscapeOpensTheMainMenuWhoseCursorWrapsAndSkipsTheGaps()
    {
        (_, _, MMenu menu, MenuHost host) = New();
        Assert.False(menu.M_Responder(Down)); // closed: only Escape opens it
        Assert.True(menu.M_Responder(Esc));
        Assert.Equal(("main", 0), (menu.StateName, (int)menu.itemOn));
        Assert.Equal(sfxenum_t.sfx_swtchn, host.Sounds[^1]);
        Assert.Equal(6, menu.MainDef.numitems);
        Keys(menu, Up);
        Assert.Equal(5, menu.itemOn); // wraps
        Keys(menu, Down);
        Assert.Equal(0, menu.itemOn);
        Assert.Equal(sfxenum_t.sfx_pstop, host.Sounds[^1]);

        // the options: the sliders' thermometer rows are gaps the cursor skips
        Keys(menu, 'o', Enter);
        Assert.Equal("options", menu.StateName);
        var seen = new List<int>();
        for (int i = 0; i < 7; i++)
        {
            Keys(menu, Down);
            seen.Add(menu.itemOn);
        }
        Assert.Equal(new[] { 1, 2, 3, 5, 7, 0, 1 }, seen);

        // a letter jumps to the next item with it (vanilla's 'm' finds MESSAGES then MOUSE SENSITIVITY)
        Keys(menu, 'm');
        Assert.Equal(5, menu.itemOn);
        Keys(menu, 'm');
        Assert.Equal(1, menu.itemOn);

        // back to the main menu on its last item, then Escape closes
        Keys(menu, Back);
        Assert.Equal(("main", 1), (menu.StateName, (int)menu.itemOn));
        Keys(menu, Esc);
        Assert.Equal("closed", menu.StateName);
        Assert.Equal(sfxenum_t.sfx_swtchx, host.Sounds[^1]);
        Keys(menu, Esc); // reopens on the item it was on
        Assert.Equal(("main", 1), (menu.StateName, (int)menu.itemOn));
    }

    // ---- new game ----

    [Theory]
    [InlineData(GameMode.shareware, 3, 0, 2, "E1M1", skill_t.sk_medium)]
    [InlineData(GameMode.registered, 3, 2, 3, "E3M1", skill_t.sk_hard)]
    [InlineData(GameMode.retail, 4, 3, 0, "E4M1", skill_t.sk_baby)]
    [InlineData(GameMode.commercial, 0, 0, 1, "MAP01", skill_t.sk_easy)]
    public void NewGameEpisodeAndSkillStartTheRightMap(GameMode mode, int episodes, int episode, int skill, string map, skill_t expected)
    {
        (GameFlow flow, GameFlowTests.Host host, MMenu menu, _) = New(mode);
        Keys(menu, Esc, Enter);
        if (mode == GameMode.commercial)
            Assert.Equal("skill", menu.StateName); // Doom II has no episodes
        else
        {
            Assert.Equal(("episode", episodes), (menu.StateName, (int)menu.EpiDef.numitems));
            for (int i = 0; i < episode; i++)
                Keys(menu, Down);
            Keys(menu, Enter);
            Assert.Equal(("skill", 2), (menu.StateName, (int)menu.itemOn)); // Hurt Me Plenty first
        }
        while (menu.itemOn != skill)
            Keys(menu, menu.itemOn < skill ? Down : Up);
        Keys(menu, Enter);
        Assert.Equal("closed", menu.StateName);
        Assert.Equal(gameaction_t.ga_newgame, flow.gameaction);
        flow.G_Ticker(default);
        Assert.Equal("new " + map, host.Loads[^1]);
        Assert.Equal(expected, flow.gameskill);
        // the synthetic IWAD has E1M1 only of these: the others go back to the title loop
        Assert.Equal(host.HasLump(map) ? gamestate_t.GS_LEVEL : gamestate_t.GS_DEMOSCREEN, flow.gamestate);
    }

    [Fact]
    public void TheSharewaresOtherEpisodesShowTheOrderMessage()
    {
        (GameFlow flow, GameFlowTests.Host host, MMenu menu, _) = New();
        Keys(menu, Esc, Enter, Down, Enter);
        Assert.True(menu.messageToPrint);
        Assert.Equal(MMenu.SWSTRING, menu.messageString);
        Assert.False(menu.messageNeedsInput);
        Assert.Same(menu.ReadDef1, menu.currentMenu);
        Assert.True(menu.M_Responder('x')); // any key
        Assert.Equal("closed", menu.StateName); // vanilla closes the menus after any message
        flow.G_Ticker(default);
        Assert.Empty(host.Loads);
        Assert.Equal(gamestate_t.GS_DEMOSCREEN, flow.gamestate);
    }

    [Fact]
    public void NightmareAsksFirst()
    {
        (GameFlow flow, GameFlowTests.Host host, MMenu menu, _) = New();
        Keys(menu, Esc, Enter, Enter, Down, Down, Enter);
        Assert.Equal(MMenu.NIGHTMARE, menu.messageString);
        Assert.False(menu.M_Responder('x')); // only y, n, space and Escape answer it
        Assert.False(menu.M_Responder(Enter));
        Assert.True(menu.messageToPrint);
        Assert.True(menu.M_Responder('n'));
        Assert.Equal("closed", menu.StateName);
        Assert.Equal(gameaction_t.ga_nothing, flow.gameaction);

        Keys(menu, Esc, Enter, Enter);
        Assert.Equal(("skill", 4), (menu.StateName, (int)menu.itemOn)); // remembered
        Keys(menu, Enter, 'y');
        flow.G_Ticker(default);
        Assert.Equal("new E1M1", host.Loads[^1]);
        Assert.Equal(skill_t.sk_nightmare, flow.gameskill);
    }

    // ---- the pad and the mouse ----

    [Fact]
    public void ThePadsButtonsWorkTheMenusAndAnswerMessages()
    {
        (GameFlow flow, GameFlowTests.Host host, MMenu menu, _) = New();
        Keys(menu, Esc, MMenu.KEY_PAD_ACCEPT);
        Assert.Equal("episode", menu.StateName);
        Keys(menu, MMenu.KEY_PAD_CANCEL);
        Assert.Equal("main", menu.StateName);
        Keys(menu, MMenu.KEY_PAD_ACCEPT, MMenu.KEY_PAD_ACCEPT, Down, Down, MMenu.KEY_PAD_ACCEPT);
        Assert.Equal(MMenu.NIGHTMARE, menu.messageString);
        Keys(menu, MMenu.KEY_PAD_CANCEL); // no
        Assert.Equal(gameaction_t.ga_nothing, flow.gameaction);
        Keys(menu, Esc, MMenu.KEY_PAD_ACCEPT, MMenu.KEY_PAD_ACCEPT, MMenu.KEY_PAD_ACCEPT, MMenu.KEY_PAD_ACCEPT); // yes
        flow.G_Ticker(default);
        Assert.Equal(("new E1M1", skill_t.sk_nightmare), (host.Loads[^1], flow.gameskill));
    }

    [Fact]
    public void TheMouseMovesTheSkullAndClicksTheItems()
    {
        (GameFlow flow, GameFlowTests.Host host, MMenu menu, MenuHost menuHost) = New();
        Keys(menu, Esc);
        // the main menu's items are at x 97, rows 64 + 16 i
        Assert.True(menu.M_MouseMove(120, 64 + 16 * 5 + 4));
        Assert.Equal(5, menu.itemOn);
        Assert.False(menu.M_MouseMove(120, 30)); // above the items: no change
        Assert.Equal(5, menu.itemOn);
        Assert.True(menu.M_MouseButton(true, 120, 64 + 16 + 4)); // Options
        Assert.Equal("options", menu.StateName);
        // the screen size thermometer (row 4) clicked at its third cell
        menuHost.ScreenSize = 0;
        Assert.True(menu.M_MouseButton(true, 60 + 8 + 2 * 8 + 3, 37 + 16 * 4 + 5));
        Assert.Equal(2, menuHost.ScreenSize);
        Assert.Equal(3, menu.itemOn);
        // the mouse sensitivity (row 6) at its first cell
        Assert.True(menu.M_MouseButton(true, 60 + 8 + 2, 37 + 16 * 6 + 5));
        Assert.Equal(0, menu.mouseSensitivity);
        Assert.True(menu.M_MouseButton(false, 0, 0)); // the right button goes back
        Assert.Equal("main", menu.StateName);
        // New Game, the first episode, Hurt Me Plenty by clicks
        menu.M_MouseButton(true, 120, 64 + 4);
        menu.M_MouseButton(true, 60, 63 + 4);
        menu.M_MouseButton(true, 60, 63 + 2 * 16 + 4);
        flow.G_Ticker(default);
        Assert.Equal(("new E1M1", skill_t.sk_medium), (host.Loads[^1], flow.gameskill));
    }

    // ---- quit, end game, read this ----

    [Theory]
    [InlineData(GameMode.shareware)]
    [InlineData(GameMode.commercial)]
    public void QuitShowsTheGamesQuitMessageByGametic(GameMode mode)
    {
        (GameFlow flow, _, MMenu menu, MenuHost host) = New(mode);
        flow.gametic = 13;
        Keys(menu, Esc, 'q');
        Assert.Equal(mode == GameMode.commercial ? 4 : 5, menu.itemOn);
        Keys(menu, Enter);
        string[] messages = mode == GameMode.commercial ? MMenu.doom2_endmsg : MMenu.doom1_endmsg;
        Assert.Equal(messages[13 % 8] + "\n\n" + MMenu.DOSY, menu.messageString);
        Assert.True(menu.messageNeedsInput);
        Keys(menu, ' '); // no
        Assert.Equal(0, host.Quits);
        Assert.Equal("closed", menu.StateName);
        Keys(menu, Esc, Enter, 'y');
        Assert.Equal(1, host.Quits);
        Assert.Equal((mode == GameMode.commercial ? MMenu.quitsounds2 : MMenu.quitsounds)[(13 >> 2) & 7], host.Sounds[^2]);
    }

    [Fact]
    public void EndGameNeedsAGameAndGoesBackToTheTitle()
    {
        (GameFlow flow, GameFlowTests.Host host, MMenu menu, MenuHost menuHost) = New();
        Keys(menu, Esc, 'o', Enter, Enter);
        Assert.Equal(sfxenum_t.sfx_oof, menuHost.Sounds[^2]); // no game: a grunt
        Assert.Equal("options", menu.StateName);
        Keys(menu, Esc);
        flow.G_InitNewMap(skill_t.sk_medium, "E1M1");
        Keys(menu, Esc, 'o', Enter, Enter);
        Assert.Equal(MMenu.ENDGAME, menu.messageString);
        Keys(menu, 'y');
        Assert.Equal(gamestate_t.GS_DEMOSCREEN, flow.gamestate);
        Assert.Null(host.World);
        Assert.Equal("closed", menu.StateName);
    }

    [Theory]
    [InlineData(GameMode.shareware, "readthis1 readthis2 main")]
    [InlineData(GameMode.registered, "readthis1 readthis2 main")]
    [InlineData(GameMode.retail, "readthis2 main")]
    public void ReadThisShowsTheGamesHelpPages(GameMode mode, string expected)
    {
        (_, _, MMenu menu, _) = New(mode);
        Keys(menu, Esc, 'r');
        Assert.Equal(4, menu.itemOn);
        var seen = new List<string>();
        do
        {
            Keys(menu, Enter);
            seen.Add(menu.StateName);
        } while (menu.StateName != "main" && seen.Count < 4);
        Assert.Equal(expected, string.Join(' ', seen));
    }

    [Fact]
    public void Doom2HasNoReadThisAndOneHelpPage()
    {
        (_, _, MMenu menu, _) = New(GameMode.commercial);
        Assert.Equal((5, 72), ((int)menu.MainDef.numitems, (int)menu.MainDef.y));
        Assert.Equal("M_QUITG", menu.MainMenu[4].name);
        Assert.Same(menu.MainDef, menu.NewDef.prevMenu);
        // the help page (vanilla reaches it with F1, not ported) goes back to the main menu
        menu.M_StartControlPanel();
        menu.M_SetupNextMenu(menu.ReadDef1);
        Keys(menu, Enter);
        Assert.Equal("main", menu.StateName);
    }

    // ---- the options ----

    [Fact]
    public void TheOptionsChangeTheirSettings()
    {
        (_, _, MMenu menu, MenuHost host) = New();
        Keys(menu, Esc, 'o', Enter, Down); // Messages
        Keys(menu, Enter);
        Assert.Equal((0, (MMenu.MSGOFF, true)), (menu.showMessages, host.Messages[^1]));
        Keys(menu, Enter);
        Assert.Equal((1, (MMenu.MSGON, true)), (menu.showMessages, host.Messages[^1]));
        Keys(menu, Down, Enter); // Graphic Detail: the message only
        Assert.Equal((1, (MMenu.DETAILLO, false)), (menu.detailLevel, host.Messages[^1]));
        Keys(menu, Down, Right, Right, Right); // Screen Size: the HUD's three modes
        Assert.Equal(2, host.ScreenSize);
        Keys(menu, Left);
        Assert.Equal(1, host.ScreenSize);
        Assert.Equal(sfxenum_t.sfx_stnmov, host.Sounds[^1]);
        Keys(menu, Down);
        for (int i = 0; i < 12; i++)
            Keys(menu, Right);
        Assert.Equal(9, menu.mouseSensitivity);
        Keys(menu, Down, Enter); // Sound Volume
        Assert.Equal("sound", menu.StateName);
        Keys(menu, Left, Down, Right, Right);
        Assert.Equal((7, 10), (menu.sfxVolume, menu.musicVolume));
        Keys(menu, Back);
        Assert.Equal(("options", 7), (menu.StateName, (int)menu.itemOn));
    }

    [Fact]
    public void SavingNeedsAGameAndTakesADescription()
    {
        (GameFlow flow, _, MMenu menu, MenuHost host) = New();
        Keys(menu, Esc, 's', Enter);
        Assert.Equal(MMenu.SAVEDEAD, menu.messageString);
        Keys(menu, Esc); // any key: closed
        Keys(menu, Esc, 'l', Enter);
        Assert.Equal("load", menu.StateName);
        Assert.All(menu.savegamestrings, s => Assert.Equal(MMenu.EMPTYSTRING, s));
        Assert.All(menu.LoadMenu, item => Assert.Equal(0, item.status)); // nothing to load
        Keys(menu, Enter);
        Assert.Equal("load", menu.StateName);
        Keys(menu, Esc);

        flow.G_InitNewMap(skill_t.sk_medium, "E1M1");
        host.Saves[1] = "BEFORE THE DOOR";
        Keys(menu, Esc, 's', Enter);
        Assert.Equal("save", menu.StateName);
        Assert.Equal("BEFORE THE DOOR", menu.savegamestrings[1]);
        Keys(menu, Down, Enter);
        Assert.True(menu.saveStringEnter);
        Assert.Equal("BEFORE THE DOOR", menu.savegamestrings[1]); // an existing description is edited
        Keys(menu, Esc);
        Assert.Equal(("save", "BEFORE THE DOOR"), (menu.StateName, menu.savegamestrings[1]));
        Keys(menu, Up, Enter);
        Assert.Equal("", menu.savegamestrings[0]); // an empty slot starts empty
        menu.M_Responder('h', 'h');
        menu.M_Responder('i', 'i');
        menu.M_Responder(' ', ' ');
        menu.M_Responder('x', 'x');
        menu.M_Responder(Back);
        menu.M_Responder('1', '!');
        Assert.Equal("HI !", menu.savegamestrings[0]);
        Keys(menu, Enter);
        Assert.Equal((0, "HI !"), (flow.savegameslot, flow.savedescription));
        Assert.Equal("closed", menu.StateName);
    }

    // ---- versions, the skull, the world ----

    [Fact]
    public void TheBfgEditionUsesChocolateDoomsPatchWorkarounds()
    {
        (GameFlow flow, _) = GameFlowTests.New();
        var bfg = new MMenu(flow, GameVariant.bfgedition);
        Assert.Equal(("M_MSGON", "M_MSGOFF", "M_DISP", "M_DOOM"), (bfg.DEH_String("M_GDHIGH"), bfg.DEH_String("M_GDLOW"), bfg.DEH_String("M_SCRNSZ"), bfg.DEH_String("M_DOOM")));
        Assert.Equal("M_GDHIGH", flow.Menu.DEH_String("M_GDHIGH"));
    }

    [Fact]
    public void TheSkullBlinksEveryEightTics()
    {
        (GameFlow flow, _, MMenu menu, _) = New();
        var flips = new List<int>();
        int last = menu.whichSkull;
        for (int tic = 1; tic <= 30; tic++)
        {
            flow.G_Ticker(default);
            if (menu.whichSkull != last)
                flips.Add(tic);
            last = menu.whichSkull;
        }
        Assert.Equal(new[] { 10, 18, 26 }, flips);
    }

    [Fact]
    public void TheMenuHoldsTheWorldButNotALevelsFirstTic()
    {
        (GameFlow flow, GameFlowTests.Host host, MMenu menu, _) = New();
        flow.G_InitNewMap(skill_t.sk_medium, "E1M1");
        World world = host.World!;
        Keys(menu, Esc);
        flow.G_Ticker(default); // a new game's player is reborn (viewz 0): held at once, as vanilla's
        flow.G_Ticker(default);
        Assert.Equal(0, world.leveltime);
        world.players[world.consoleplayer].viewz = 1; // a level's start (P_SetupLevel) without a reborn: its first tic runs
        flow.G_Ticker(default);
        Assert.Equal(1, world.leveltime);
        flow.G_Ticker(default);
        Assert.Equal(1, world.leveltime);
        Keys(menu, Esc);
        flow.G_Ticker(default);
        Assert.Equal(2, world.leveltime);
    }

    // ---- drawing ----

    [Fact]
    public void TheDrawerDrawsTheMenuAndMessagesWithTheWadsGraphics()
    {
        var wad = new WadArchive(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) });
        var hu = new HuStuff(wad);
        var g = new ScreenGraphics(wad, hu);
        (_, _, MMenu menu, _) = New();
        menu.Graphics = g;
        var screen = new HudScreen(0, HudScreen.SCREENHEIGHT);
        menu.M_Drawer(screen);
        Assert.DoesNotContain((byte)1, screen.Opaque); // closed: nothing
        Keys(menu, Esc);
        menu.M_Drawer(screen);
        IndexedImage logo = g.Patch("M_DOOM")!;
        int x = 94 - logo.LeftOffset, y = 2 - logo.TopOffset;
        for (int py = 0; py < logo.Height; py++)
        {
            for (int px = 0; px < logo.Width; px++)
            {
                if (logo.IsOpaque(px, py))
                    Assert.Equal(logo[px, py], screen.Pixels[(y + py) * 320 + x + px]);
            }
        }
        // a message alone, centred: "ABC" (the synthetic font's glyphs) on two lines
        screen.Clear();
        menu.M_StartMessage("abc\nabc", null, false);
        menu.M_Drawer(screen);
        int width = g.TextWidth("ABC"), height = g.FontHeight;
        int top = 100 - 2 * height / 2;
        IndexedImage a = hu.hu_font['A' - HuStuff.HU_FONTSTART]!;
        int ax = 160 - width / 2 - a.LeftOffset, ay = top - a.TopOffset;
        bool found = false;
        for (int py = 0; py < a.Height && !found; py++)
        {
            for (int px = 0; px < a.Width; px++)
            {
                if (!a.IsOpaque(px, py))
                    continue;
                Assert.Equal(a[px, py], screen.Pixels[(ay + py) * 320 + ax + px]);
                Assert.Equal(a[px, py], screen.Pixels[(ay + height + py) * 320 + ax + px]);
                found = true;
            }
        }
        Assert.True(found);
        Assert.Equal(MMenu.LINEHEIGHT, 16);
    }

    // ---- the message line's toggle ----

    [Fact]
    public void MessagesOffHidesAllButTheMenusOwn()
    {
        var hu = new HuStuff(null);
        hu.showMessages = false;
        hu.HU_Ticker("Picked up a clip.");
        Assert.Null(hu.Message);
        hu.message_dontfuckwithme = true;
        hu.HU_Ticker(MMenu.MSGOFF);
        Assert.Equal(MMenu.MSGOFF, hu.Message);
        hu.showMessages = true;
        hu.HU_Ticker("Picked up a clip."); // the menu's message stays until its time is up
        Assert.Equal(MMenu.MSGOFF, hu.Message);
        for (int i = 0; i < HuStuff.HU_MSGTIMEOUT; i++)
            hu.HU_Ticker(null);
        Assert.Null(hu.Message);
        hu.HU_Ticker("Picked up a clip.");
        Assert.Equal("Picked up a clip.", hu.Message);
    }
}
