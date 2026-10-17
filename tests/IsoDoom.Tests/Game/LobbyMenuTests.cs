using System.Collections.Generic;
using System.Linq;
using IsoDoom.Game;
using IsoDoom.Net;
using IsoDoom.Sim;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Game;

/// <summary>
/// T11.9: the multiplayer pages (<see cref="MMenu"/>'s, MMenu.Net.cs) on a
/// fake session host: Multiplayer on the main menu, Host Game's settings
/// (<c>net/*</c>) and its Start Hosting, Join Game's typed address and its
/// recent ones (a pad's A), the lobby page's Start (two players at least)
/// and Escape, the refusals and failures as messages back on their page,
/// the lobby closing when the game begins.
/// </summary>
public class LobbyMenuTests
{
    private sealed class SetupHost : ISetupHost
    {
        public readonly Dictionary<string, string> Values = Settings.Defs.ToDictionary(d => d.Key, d => d.Default);

        public string GetSetting(string key) => Values[key];
        public void SetSetting(string key, string value) => Values[key] = Settings.NetValue(Settings.Find(key)!, value);
        public string BindingText(string action) => "";
        public void ClearBindings(string action, bool pad) { }
        public void ResetControls() { }
        public void ResetSettings() { }
    }

    private sealed class NetHost : INetMenuHost
    {
        public NetLobbyView? Lobby { get; set; }
        public List<string> Calls { get; } = [];
        public string? HostError { get; set; }
        public string? JoinError { get; set; }

        public string? HostGame()
        {
            Calls.Add("host");
            if (HostError is null)
                Lobby = View(host: true);
            return HostError;
        }

        public string? JoinGame(string address)
        {
            Calls.Add("join " + address);
            if (JoinError is null)
                Lobby = View(host: false, address: address) with { State = NetClientState.Connecting, Players = [] };
            return JoinError;
        }

        public void LeaveGame()
        {
            Calls.Add("leave");
            Lobby = null;
        }

        public bool StartGame()
        {
            Calls.Add("start");
            return true;
        }
    }

    private static NetLobbyView View(bool host, string address = "127.0.0.1", int players = 1) =>
        new(host, true, address, 5029, NetClientState.Lobby, 4, [.. Enumerable.Range(0, players).Select(i => new NetLobbyPlayer(i, true, 1000 * i, i == (host ? 0 : 1)))],
            null, "E1M1", "DOOM1.WAD", null, null, null, 0, false, false);

    private static (GameFlow Flow, MMenu Menu, SetupHost Setup, NetHost Net) New()
    {
        (GameFlow flow, _) = GameFlowTests.New(GameMode.shareware);
        var setup = new SetupHost();
        var net = new NetHost();
        flow.Menu.SetupHost = setup;
        flow.Menu.NetHost = net;
        flow.D_StartTitle(null);
        return (flow, flow.Menu, setup, net);
    }

    private static void Keys(MMenu menu, params int[] keys)
    {
        foreach (int key in keys)
            menu.M_Responder(key);
    }

    private static void Type(MMenu menu, string text)
    {
        foreach (char c in text)
            menu.M_Responder(char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : 0, c);
    }

    private const int Esc = MMenu.KEY_ESCAPE, Enter = MMenu.KEY_ENTER, Up = MMenu.KEY_UPARROW, Down = MMenu.KEY_DOWNARROW;
    private const int Right = MMenu.KEY_RIGHTARROW, Back = MMenu.KEY_BACKSPACE;

    [Fact]
    public void MultiplayerLeadsToTheHostPageWhoseSettingsStepAndStartHosting()
    {
        (_, MMenu menu, SetupHost setup, NetHost net) = New();
        Keys(menu, Esc, 'm');
        Assert.Equal(("main", 1), (menu.StateName, (int)menu.itemOn));
        Assert.Equal("MULTIPLAYER", menu.MainMenu[1].text);
        Keys(menu, Enter);
        Assert.Equal("multiplayer", menu.StateName);
        Keys(menu, Enter);
        Assert.Equal("host", menu.StateName);

        // every host setting, then Start Hosting
        string[] labels = [.. Settings.OnPage(SettingPage.Net).Where(d => d.Key != "net/addresses").Select(d => d.Label)];
        Assert.Equal([.. labels, "", "START HOSTING"], menu.HostDef.menuitems.Select(i => i.text));
        Keys(menu, Right);
        Assert.Equal("deathmatch", setup.Values["net/mode"]);
        Assert.Contains("MODE: DEATHMATCH", menu.StateText());
        Keys(menu, Down, Right); // the map: the WAD's next
        Assert.Equal("E1M2", setup.Values["net/map"]);
        Assert.Equal("E1M2", menu.HostMap());
        Keys(menu, Down);
        Assert.Contains("SKILL: HURT ME PLENTY", menu.StateText());

        // Start Hosting: the lobby, with Start and Leave
        Keys(menu, 's', Enter);
        Assert.Equal(["host"], net.Calls);
        Assert.Equal("lobby", menu.StateName);
        Assert.Equal(["START GAME", "LEAVE"], menu.LobbyDef.menuitems.Select(i => i.text));

        // Start needs two players
        Keys(menu, Enter);
        Assert.Equal(sfxenum_t.sfx_oof, menu.LastSound);
        Assert.DoesNotContain("start", net.Calls);
        net.Lobby = View(host: true, players: 2);
        Keys(menu, Enter);
        Assert.Equal("start", net.Calls[^1]);

        // Escape leaves the lobby, back on the host page
        Keys(menu, Esc);
        Assert.Equal(("host", "leave"), (menu.StateName, net.Calls[^1]));
        Assert.Null(net.Lobby);
    }

    [Fact]
    public void AHostThatCantListenSaysWhyOnTheHostPage()
    {
        (_, MMenu menu, _, NetHost net) = New();
        net.HostError = "cannot listen on *:5029 (ERR_CANT_CREATE)";
        Keys(menu, Esc, 'm', Enter, 'h', Enter, 's', Enter);
        Assert.Equal("message", menu.StateName);
        Assert.Contains("can't host the game: cannot listen on *:5029", menu.messageString.Replace('\n', ' '));
        Keys(menu, ' ');
        Assert.Equal("host", menu.StateName);
    }

    [Fact]
    public void JoinTypesAnAddressConnectsAndEscapeLeaves()
    {
        (_, MMenu menu, _, NetHost net) = New();
        Keys(menu, Esc, 'm', Enter, 'j', Enter);
        Assert.Equal(("join", 0), (menu.StateName, (int)menu.itemOn));
        Assert.Equal(["ADDRESS", "CONNECT"], menu.JoinDef.menuitems.Select(i => i.text));

        // typed, a character too many rubbed out, then Escape: the old address
        Keys(menu, Enter);
        Assert.True(menu.AddressEnter);
        Type(menu, "10.0.0.22");
        Keys(menu, Back);
        Assert.Equal("10.0.0.2", menu.JoinAddress);
        Assert.Contains("typing the address: \"10.0.0.2\"", menu.StateText());
        Type(menu, " /");
        Assert.Equal("10.0.0.2", menu.JoinAddress); // no space, no slash
        Keys(menu, Esc);
        Assert.Equal(("join", ""), (menu.StateName, menu.JoinAddress));

        // typed again, Enter connects: the lobby, a join's with Leave only
        Keys(menu, Enter);
        Type(menu, "10.0.0.2:5030");
        Keys(menu, Enter);
        Assert.Equal(["join 10.0.0.2:5030"], net.Calls);
        Assert.Equal("lobby", menu.StateName);
        Assert.Equal(["LEAVE"], menu.LobbyDef.menuitems.Select(i => i.text));

        // the pad's B leaves (back to the join page)
        Keys(menu, MMenu.KEY_PAD_CANCEL);
        Assert.Equal(("join", "leave"), (menu.StateName, net.Calls[^1]));

        // no address: refused
        Keys(menu, Enter); // the address
        for (int i = 0; i < 20; i++)
            Keys(menu, Back);
        Keys(menu, Enter);
        Assert.Equal(("join", sfxenum_t.sfx_oof), (menu.StateName, menu.LastSound));
        Assert.Equal(2, net.Calls.Count);
    }

    [Fact]
    public void ThePadPicksARecentAddress()
    {
        (_, MMenu menu, SetupHost setup, NetHost net) = New();
        setup.Values["net/addresses"] = "doom.example.org 192.168.1.20:5030";
        Keys(menu, Esc, 'm', Enter, 'j', Enter);
        Assert.Equal(["ADDRESS", "CONNECT", "", "doom.example.org", "192.168.1.20:5030"], menu.JoinDef.menuitems.Select(i => i.text));
        Assert.Equal("doom.example.org", menu.JoinAddress); // the last one used
        Keys(menu, Down, Down, Down); // past the gap
        Assert.Equal(4, menu.itemOn);
        Keys(menu, MMenu.KEY_PAD_ACCEPT);
        Assert.Equal(["join 192.168.1.20:5030"], net.Calls);
        Assert.Equal("lobby", menu.StateName);
    }

    [Fact]
    public void ARefusedJoinShowsWhyAndGoesBackToTheJoinPage()
    {
        (_, MMenu menu, _, NetHost net) = New();
        Keys(menu, Esc, 'm', Enter, 'j', Enter, Enter);
        Type(menu, "127.0.0.1");
        Keys(menu, Enter);
        Assert.Equal("lobby", menu.StateName);
        net.Lobby = net.Lobby! with { State = NetClientState.Refused, Failure = "refused: IwadMismatch", Refusal = NetRefusal.IwadMismatch };
        menu.NetTicker();
        Assert.Equal("message", menu.StateName);
        Assert.Equal("can't join the game: the host plays another iwad.", menu.messageString.Split("\n\n")[0].Replace('\n', ' '));
        Assert.Equal("leave", net.Calls[^1]); // a session from the menus ends
        Keys(menu, 'n');
        Assert.Equal("join", menu.StateName);

        // no answer at all
        Keys(menu, 'c', Enter);
        net.Lobby = net.Lobby! with { State = NetClientState.Disconnected, Failure = "disconnected" };
        menu.NetTicker();
        Assert.Equal("no answer from 127.0.0.1:5029.", menu.messageString.Split("\n\n")[0].Replace('\n', ' '));
    }

    [Fact]
    public void TheHostsStartRefusalShowsOnTheLobbyPage()
    {
        (_, MMenu menu, _, NetHost net) = New();
        Keys(menu, Esc, 'm', Enter, Enter, 's', Enter);
        net.Lobby = View(host: true, players: 3) with { StartRefusal = "no co-op start for player 3 on E1M2", StartRefusals = 1 };
        menu.NetTicker();
        Assert.Equal("message", menu.StateName);
        Assert.Contains("the game can't start: no co-op start for player 3 on E1M2", menu.messageString.Replace('\n', ' '));
        Assert.Contains('\n', menu.Wrap(new string('a', 60) + " " + new string('b', 60))); // (a line over 300 columns)
        Keys(menu, ' ');
        Assert.Equal("lobby", menu.StateName);
        menu.NetTicker(); // shown once
        Assert.Equal("lobby", menu.StateName);

        // a player who leaves (a quit, -nettimeout's drop) is noted on the page
        Assert.Null(menu.LobbyNote);
        net.Lobby = net.Lobby with { Players = [.. net.Lobby.Players.Where(p => p.Slot != 2)] };
        menu.NetTicker();
        Assert.Equal(("lobby", "PLAYER 3 LEFT THE LOBBY"), (menu.StateName, menu.LobbyNote));
    }

    [Fact]
    public void TheLobbyClosesWhenTheGameBegins()
    {
        (GameFlow flow, MMenu menu, _, NetHost net) = New();
        Keys(menu, Esc, 'm', Enter, Enter, 's', Enter);
        net.Lobby = View(host: true, players: 2) with { InGame = true };
        menu.NetTicker();
        Assert.Equal("closed", menu.StateName);

        // Multiplayer in the game: a message
        flow.netsession = true; // (the scene's, with its session)
        Keys(menu, Esc, 'm', Enter);
        Assert.Equal(("message", MMenu.NETALREADY), (menu.StateName, menu.messageString));
    }

    [Fact]
    public void TheNetSettingsKeepOnlyWhatTheyTake()
    {
        SettingDef port = Settings.Find("net/port")!, map = Settings.Find("net/map")!, addresses = Settings.Find("net/addresses")!;
        Assert.Equal("0", Settings.NetValue(port, "0")); // any free port
        Assert.Equal("12345", Settings.NetValue(port, "12345"));
        Assert.Throws<System.ArgumentException>(() => Settings.NetValue(port, "70000"));
        Assert.Equal("MAP07", Settings.NetValue(map, "map07"));
        Assert.Throws<System.ArgumentException>(() => Settings.NetValue(map, "E5M1"));
        Assert.Throws<System.ArgumentException>(() => Settings.NetValue(Settings.Find("net/mode")!, "ctf"));
        Assert.Equal("a.example [::1]:5030", Settings.NetValue(addresses, " a.example  [::1]:5030 A.EXAMPLE"));
        Assert.Throws<System.ArgumentException>(() => Settings.NetValue(addresses, "a/b"));
        Assert.Equal("c a b", Settings.RememberAddress("a b", "c"));
        Assert.Equal("b a", Settings.RememberAddress("a b", "b"));
        Assert.Equal("f a b c d", Settings.RememberAddress("a b c d e", "f")); // the oldest goes
        Assert.All(Settings.OnPage(SettingPage.Net), d => Assert.Equal(d.Default, Settings.NetValue(d, d.Default)));
    }

    [Fact]
    public void ACommandLineSessionsLobbyOpensAndMultiplayerReturnsToIt()
    {
        (_, MMenu menu, _, NetHost net) = New();
        net.Lobby = View(host: false, players: 2) with { FromMenus = false };
        menu.ShowLobby();
        Assert.Equal(("lobby", true), (menu.StateName, menu.menuactive));
        Keys(menu, Up); // the lobby's one item
        Keys(menu, MMenu.KEY_ESCAPE);
        Assert.Equal("multiplayer", menu.StateName);
        Assert.Equal("leave", net.Calls[^1]);
        net.Lobby = View(host: false, players: 2) with { FromMenus = false };
        Keys(menu, Back, Up);
        while (menu.itemOn != 1)
            Keys(menu, Down);
        Keys(menu, Enter);
        Assert.Equal("lobby", menu.StateName);

        // a command-line session's failure shows, the session left to its script
        net.Lobby = net.Lobby with { Failure = "disconnected", State = NetClientState.Disconnected };
        menu.NetTicker();
        Assert.Equal("message", menu.StateName);
        Assert.Equal("leave", net.Calls[^1]); // (only Escape's)
        Assert.Single(net.Calls);
    }
}
