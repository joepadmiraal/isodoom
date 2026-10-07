using System;
using System.Collections.Generic;
using System.IO;
using IsoDoom.Game;
using IsoDoom.Sim;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Game;

/// <summary>
/// T7.6: saving and loading through the game flow and the menus
/// (<see cref="GameFlow.G_DoSaveGame"/>, <see cref="GameFlow.G_DoLoadGame"/>,
/// the save and load menus, quicksave and quickload) on the synthetic IWAD:
/// a save goes with the next tic (<c>BTS_SAVEGAME</c>) to the slot's file, a
/// load from the menus or the title goes on as the game saved would have,
/// and a file that is no save, from another version, damaged or another
/// game's is refused with a message.
/// </summary>
public sealed class SaveLoadTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"isodoom-saves-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    /// <summary>The menus' host, its slots' descriptions the flow's, as the game scene's.</summary>
    private sealed class MenuHost : IMenuHost
    {
        private readonly GameFlow _flow;

        public MenuHost(GameFlow flow) => _flow = flow;

        public int ScreenSize { get; set; }
        public List<sfxenum_t> Sounds { get; } = new();

        public void PlayerMessage(string text, bool dontfuckwithme)
        {
        }

        public void StartSound(sfxenum_t sfx) => Sounds.Add(sfx);
        public string? SaveDescription(int slot) => _flow.SaveDescription(slot);

        public void I_Quit()
        {
        }
    }

    private (GameFlow Flow, GameFlowTests.Host Host, MMenu Menu, MenuHost MenuHost) New(GameMode mode = GameMode.shareware)
    {
        (GameFlow flow, GameFlowTests.Host host) = GameFlowTests.New(mode);
        flow.SaveDir = _dir;
        var menuHost = new MenuHost(flow);
        flow.Menu.Host = menuHost;
        flow.D_StartTitle(null);
        return (flow, host, flow.Menu, menuHost);
    }

    private static void Keys(MMenu menu, params int[] keys)
    {
        foreach (int key in keys)
            menu.M_Responder(key, key is >= 'a' and <= 'z' ? key : 0);
    }

    private const int Esc = MMenu.KEY_ESCAPE, Enter = MMenu.KEY_ENTER, Down = MMenu.KEY_DOWNARROW;

    // Back and forth near the start with turns and shots (no use: no exit): the world changes from tic to tic.
    private static ticcmd_t Cmd(int tic) => new()
    {
        forwardmove = (sbyte)(tic % 40 < 20 ? 25 : -25),
        sidemove = (sbyte)(tic % 30 < 15 ? 24 : -24),
        angleturn = (short)(tic % 90 < 30 ? 640 : 0),
        buttons = (byte)(tic % 37 == 0 ? buttoncode_t.BT_ATTACK : 0),
    };

    private static List<ulong> Play(GameFlow flow, World world, int from, int count)
    {
        var sums = new List<ulong>();
        for (int tic = from; tic < from + count; tic++)
        {
            flow.G_Ticker(Cmd(tic));
            sums.Add(world.Checksum());
        }
        return sums;
    }

    [Fact]
    public void TheSaveMenuSavesWithTheNextTicAndTheLoadMenuGoesOnFromThere()
    {
        (GameFlow flow, GameFlowTests.Host host, MMenu menu, _) = New();
        flow.G_InitNewMap(skill_t.sk_medium, "E1M2");
        World world = host.World!;
        Play(flow, world, 0, 100);

        // Save Game, slot 1, "AB"
        Keys(menu, Esc, 's', Enter, Down, Enter, 'a', 'b', Enter);
        Assert.Equal("closed", menu.StateName);
        Assert.True(flow.sendsave);
        Assert.False(File.Exists(flow.SaveGamePath(1)));
        flow.G_Ticker(Cmd(100)); // carries BTS_SAVEGAME; saved after it
        Assert.True(File.Exists(flow.SaveGamePath(1)));
        Assert.Equal(GameFlow.GGSAVED, world.players[0].message);
        Assert.Equal("AB", flow.SaveDescription(1));
        Assert.Null(flow.SaveDescription(0));
        Assert.Equal(101, world.leveltime);
        List<ulong> expected = Play(flow, world, 101, 200);

        // Load Game, slot 1: the game goes on as it did
        Keys(menu, Esc, 'l', Enter);
        Assert.Equal(("load", 0, 1), (menu.StateName, menu.LoadMenu[0].status, (int)menu.LoadMenu[1].status));
        Assert.Equal("AB", menu.savegamestrings[1]);
        Keys(menu, Down, Enter);
        Assert.Equal(gameaction_t.ga_loadgame, flow.gameaction);
        flow.G_DoGameActions();
        Assert.Equal("load E1M2", host.Loads[^1]);
        World loaded = host.World!;
        Assert.NotSame(world, loaded);
        Assert.Equal((101, gamestate_t.GS_LEVEL, true), (loaded.leveltime, flow.gamestate, flow.usergame));
        Assert.Equal(expected, Play(flow, loaded, 101, 200));
    }

    [Fact]
    public void ALoadFromTheTitleStartsTheGameAndMelts()
    {
        (GameFlow flow, GameFlowTests.Host host, MMenu menu, _) = New();
        flow.G_InitNewMap(skill_t.sk_hard, "E1M2");
        Play(flow, host.World!, 0, 50);
        flow.savegameslot = 4;
        flow.savedescription = "DIRECT";
        Assert.True(flow.G_DoSaveGame());
        List<ulong> expected = Play(flow, host.World!, 50, 100);

        flow.D_StartTitle(null);
        Assert.Null(host.World);
        flow.Wipes = true;
        flow.gameskill = skill_t.sk_baby;
        flow.G_LoadGame(4);
        flow.G_Ticker(Cmd(50)); // the load before the tic, as vanilla's G_Ticker
        Assert.True(flow.Wipe.go); // the title melts into the level
        Assert.Equal((gamestate_t.GS_LEVEL, skill_t.sk_hard, "E1M2"), (flow.gamestate, flow.gameskill, host.World!.level.Name));
        Assert.Null(flow.LoadRefused);
        Assert.Equal(expected[0], host.World.Checksum()); // and its first tic as the saved game's
        Assert.Equal("closed", menu.StateName);
    }

    [Fact]
    public void ALoadDuringALevelMeltsToo()
    {
        (GameFlow flow, GameFlowTests.Host host, _, _) = New();
        flow.G_InitNewMap(skill_t.sk_medium, "E1M2");
        Play(flow, host.World!, 0, 10);
        flow.savegameslot = 0;
        Assert.True(flow.G_DoSaveGame());
        flow.Wipes = true;
        Play(flow, host.World!, 10, 5);
        Assert.False(flow.Wipe.go);
        flow.G_LoadGame(0);
        flow.G_DoGameActions();
        Assert.Equal(GameFlow.GS_FORCEWIPE, flow.wipegamestate);
    }

    [Fact]
    public void SavingNeedsAGameOnALevel()
    {
        (GameFlow flow, GameFlowTests.Host host, MMenu menu, _) = New();
        flow.savegameslot = 0;
        Assert.False(flow.G_DoSaveGame()); // the title loop
        Assert.False(File.Exists(flow.SaveGamePath(0)));

        flow.G_InitNewMap(skill_t.sk_medium, "E1M2");
        flow.SaveDir = null;
        Assert.False(flow.G_DoSaveGame()); // nowhere to save
        Assert.Null(flow.SaveDescription(0));
        Assert.Null(host.World!.players[0].message);
        Keys(menu, Esc, 's', Enter, Enter, 'x', Enter); // the menus' save, nowhere to go
        flow.G_Ticker(default);
        Assert.Null(host.World!.players[0].message);
    }

    /// <summary>A file that is no save, from an older or a newer version, damaged, cut short, or another game's is refused with a message; the game goes on.</summary>
    [Fact]
    public void ABadSaveIsRefusedWithAMessage()
    {
        (GameFlow flow, GameFlowTests.Host host, MMenu menu, _) = New();
        flow.G_InitNewMap(skill_t.sk_medium, "E1M2");
        Play(flow, host.World!, 0, 20);
        flow.savegameslot = 0;
        Assert.True(flow.G_DoSaveGame());
        byte[] good = File.ReadAllBytes(flow.SaveGamePath(0)!);
        World before = host.World!;

        void Refused(byte[]? data, string message, string? description)
        {
            if (data is null)
                File.Delete(flow.SaveGamePath(2)!);
            else
                File.WriteAllBytes(flow.SaveGamePath(2)!, data);
            Assert.Equal(description, flow.SaveDescription(2));
            flow.G_LoadGame(2);
            flow.G_DoGameActions();
            Assert.Equal(message, flow.LoadRefused);
            Assert.True(menu.messageToPrint);
            Assert.Equal(message + "\n\n" + MMenu.PRESSKEY, menu.messageString);
            Assert.Same(before, host.World); // the game goes on
            Assert.Equal(gamestate_t.GS_LEVEL, flow.gamestate);
            Keys(menu, ' ');
        }

        Refused("not a save at all"u8.ToArray(), SaveGameFile.NOTASAVE, "?");
        byte[] older = (byte[])good.Clone();
        BitConverter.GetBytes(SaveGameFile.VERSION - 1).CopyTo(older, SaveGameFile.Magic.Length);
        Refused(older, SaveGameFile.OLDER, "");
        byte[] newer = (byte[])good.Clone();
        BitConverter.GetBytes(SaveGameFile.VERSION + 1).CopyTo(newer, SaveGameFile.Magic.Length);
        Refused(newer, SaveGameFile.NEWER, "");
        byte[] damaged = (byte[])good.Clone();
        damaged[good.Length / 2] ^= 0x40;
        Refused(damaged, SaveGameFile.DAMAGED, "");
        Refused(good[..(good.Length - 100)], SaveGameFile.DAMAGED, "");
        Refused(null, GameFlow.EMPTYSLOT, null);

        // another game's: the header's game mode, and a map the WAD lacks
        var save = SaveGameFile.Read(good);
        Refused(new SaveGameFile("", save.Map, GameMode.commercial, save.Skill, 0, save.State).ToBytes(), SaveGameFile.OTHERGAME, "");
        Refused(new SaveGameFile("", "E1M7", GameMode.shareware, save.Skill, 0, save.State).ToBytes(), SaveGameFile.OTHERGAME, "");
        // the state's map other than the header's, with a good hash: damaged
        Refused(new SaveGameFile("", "E1M1", GameMode.shareware, save.Skill, 0, save.State).ToBytes(), SaveGameFile.DAMAGED, "");

        // the good one still loads
        flow.G_LoadGame(0);
        flow.G_DoGameActions();
        Assert.Null(flow.LoadRefused);
        Assert.NotSame(before, host.World);
    }

    [Fact]
    public void QuicksaveAsksForASlotOnceThenSavesOverIt()
    {
        (GameFlow flow, GameFlowTests.Host host, MMenu menu, MenuHost menuHost) = New();
        Keys(menu, menu.key_menu_qsave); // no game: oof
        Assert.Equal(sfxenum_t.sfx_oof, menuHost.Sounds[^1]);
        Assert.Equal("closed", menu.StateName);
        Keys(menu, menu.key_menu_qload);
        Assert.Equal(MMenu.QSAVESPOT, menu.messageString);
        Keys(menu, ' ');

        flow.G_InitNewMap(skill_t.sk_medium, "E1M2");
        Play(flow, host.World!, 0, 30);
        Keys(menu, menu.key_menu_qsave); // the save menu, to pick the slot
        Assert.Equal(("save", -2), (menu.StateName, menu.quickSaveSlot));
        Keys(menu, Down, Down, Enter, 'q', Enter);
        Assert.Equal(2, menu.quickSaveSlot);
        flow.G_Ticker(Cmd(30));
        Assert.Equal("Q", flow.SaveDescription(2));

        Play(flow, host.World!, 31, 20);
        Keys(menu, menu.key_menu_qsave); // over it?
        Assert.Equal(string.Format(MMenu.QSPROMPT, "Q"), menu.messageString);
        Keys(menu, 'n');
        Assert.False(flow.sendsave);
        Keys(menu, menu.key_menu_qsave, 'y');
        Assert.True(flow.sendsave);
        flow.G_Ticker(Cmd(51));
        Assert.Equal(52, SaveGameFile.Read(File.ReadAllBytes(flow.SaveGamePath(2)!)).LevelTime);
        List<ulong> expected = Play(flow, host.World!, 52, 60);

        Keys(menu, menu.key_menu_qload);
        Assert.Equal(string.Format(MMenu.QLPROMPT, "Q"), menu.messageString);
        Keys(menu, 'y');
        flow.G_DoGameActions();
        Assert.Equal("load E1M2", host.Loads[^1]);
        Assert.Equal(expected, Play(flow, host.World!, 52, 60));
    }

    /// <summary>A pad can't type: an empty slot chosen with A gets the map and its time, which A saves.</summary>
    [Fact]
    public void APadSavesWithADefaultDescription()
    {
        (GameFlow flow, GameFlowTests.Host host, MMenu menu, _) = New();
        flow.G_InitNewMap(skill_t.sk_medium, "E1M2");
        Play(flow, host.World!, 0, 35 * 65 + 3);
        Keys(menu, Esc, 's', MMenu.KEY_PAD_ACCEPT);
        Assert.Equal("save", menu.StateName);
        Keys(menu, MMenu.KEY_PAD_ACCEPT);
        Assert.True(menu.saveStringEnter);
        Assert.Equal("E1M2 1:05", menu.savegamestrings[0]);
        Keys(menu, MMenu.KEY_PAD_ACCEPT);
        Assert.Equal((0, "E1M2 1:05", true), (flow.savegameslot, flow.savedescription, flow.sendsave));

        // the keyboard's Enter on an empty slot still starts it empty
        flow.G_Ticker(default);
        Keys(menu, Esc, 's', Enter, Down, Enter);
        Assert.Equal("", menu.savegamestrings[1]);
    }
}
