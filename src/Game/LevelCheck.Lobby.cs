using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Net;
using IsoDoom.Sim;

namespace IsoDoom.Game;

// T11.9: the multiplayer pages through the scene's input glue, with a second
// machine in this process (a NetSession of the check's own over ENet
// loopback): the scene hosts from the menus (Multiplayer → Host Game → Start
// Hosting, the lobby page), the check's machine joins, a start refused by the
// host's check shows as a message on the lobby page, the lobby's Start begins
// the game on both; then the scene joins the check's host from Join Game (the
// address typed), which starts it at its -players, and the address is kept;
// a host on another IWAD refuses the scene's join (a recent address picked
// with the pad's A), shown as a message back on the join page. With a real
// renderer the multiplayer pages are compared pixel by pixel (CheckLobbyDrawn).
// F12 (the harness's local-player key) is refused in both games: the local
// player stays (T11.8; kept by the user's decision: no spy view).
public partial class LevelCheck
{
    private static InputEventKey TypeEvent(char c) => Held(new InputEventKey
    {
        Keycode = c switch { '.' => Key.Period, ':' => Key.Colon, _ => OS.FindKeycodeFromString(c.ToString()) },
        PhysicalKeycode = c switch { '.' => Key.Period, ':' => Key.Colon, _ => OS.FindKeycodeFromString(c.ToString()) },
        Pressed = true,
        Unicode = c,
    });

    /// <summary>Frames (the scene polls its session in each) until <paramref name="done"/>, polling <paramref name="other"/> too; false after <paramref name="seconds"/>.</summary>
    private async Task<bool> NetFrames(Func<bool> done, NetSession? other, double seconds = 10)
    {
        ulong deadline = Time.GetTicksMsec() + (ulong)(seconds * 1000);
        while (!done())
        {
            if (Time.GetTicksMsec() > deadline)
                return false;
            other?.Update((long)Time.GetTicksUsec());
            await NextFrame();
        }
        return true;
    }

    private async Task CheckLobby()
    {
        LevelScene s = _scene;
        GameFlow flow = s.Flow;
        MMenu menu = flow.Menu;
        INetMenuHost host = s;
        int failures = _failures;
        string Step(string what) => $"lobby (T11.9): {what}: menu {menu.StateText()}, net {s.NetText()}";
        string addresses = s.GetSetting("net/addresses");
        s.SetSetting("net/port", "0"); // any free port: the lobby page shows it
        flow.D_StartTitle(null);
        menu.M_ClearMenus();

        // the scene hosts: Escape, M (Multiplayer), Enter, Enter (Host Game), S (Start Hosting), Enter
        foreach (Key key in new[] { Key.Escape, Key.M, Key.Enter, Key.H, Key.Enter })
            s.MenuEvent(KeyEvent(key));
        if (menu.StateName != "host")
        {
            Fail(Step("Multiplayer → Host Game"));
            return;
        }
        s.MenuEvent(KeyEvent(Key.Right)); // the mode: deathmatch
        s.MenuEvent(KeyEvent(Key.Left)); // and back: co-op
        s.MenuEvent(KeyEvent(Key.S));
        s.MenuEvent(KeyEvent(Key.Enter));
        if (menu.StateName != "lobby" || s.Net?.Server is null || host.Lobby is not { Host: true, FromMenus: true } || s.Net.Port <= 0)
        {
            Fail(Step("Start Hosting"));
            return;
        }
        int port = s.Net.Port;
        NetSession? join = null;
        try
        {
            join = s.CheckSession(NetOptions.Parse(["-connect", $"127.0.0.1:{port}"])!);
            if (!await NetFrames(() => host.Lobby?.Players.Count == 2 && s.Net.Server.CanStart && join.Client.State == NetClientState.Lobby, join))
                Fail(Step("the check's machine joining"));
            NetLobbyView lobby = host.Lobby!;
            if (lobby.Players.Select(p => p.Slot).SequenceEqual([0, 1]) is false || !lobby.Players[0].Local || lobby.MaxPlayers != 4 || lobby.MapName != s.DefaultMap())
                Fail(Step($"the lobby: {lobby}"));

            // a start the host's check refuses (T11.1a's) shows on the lobby page, which stays
            Func<byte, string?>? check = s.Net.CheckStart;
            s.Net.CheckStart = _ => "no co-op start for player 2 on " + s.DefaultMap();
            while (menu.itemOn != 0)
                s.MenuEvent(KeyEvent(Key.Up));
            s.MenuEvent(KeyEvent(Key.Enter)); // START GAME
            if (!await NetFrames(() => menu.StateName == "message", join, 3) || !menu.messageString.Replace('\n', ' ').Contains("the game can't start: no co-op start for player 2", StringComparison.Ordinal))
                Fail(Step("the refused start's message"));
            s.MenuEvent(KeyEvent(Key.Space));
            if (menu.StateName != "lobby" || s.Net.Server.Phase != NetServerPhase.Lobby)
                Fail(Step("back on the lobby page after the refusal"));
            s.Net.CheckStart = check;

            // the lobby's Start: the game begins on both machines, the lobby page closes
            s.MenuEvent(KeyEvent(Key.Enter));
            if (!await NetFrames(() => s.NetGame is not null && join.Client.State == NetClientState.InGame && menu.StateName == "closed", join))
                Fail(Step("the lobby's Start"));
            else if (s.World is not { netgame: true, consoleplayer: 0 } w || !w.playeringame[1] || s.Mesh?.Level.Name != s.DefaultMap())
                Fail(Step($"the game begun: {s.World?.consoleplayer}, {s.Mesh?.Level.Name}"));
            else if (s.LocalPlayerKey() || w.consoleplayer != 0 || s.PlayerMobj != w.players[0].mo)
                Fail(Step($"F12 in the netgame (refused: no spy view, the user's decision): local player {w.consoleplayer + 1}"));
        }
        finally
        {
            join?.Dispose();
            s.EndNetGameForCheck();
        }

        // the check's machine hosts (-players 2, the command line's start); the scene joins from Join Game with the address typed
        NetSession? other = null;
        try
        {
            other = s.CheckSession(NetOptions.Parse(["-host", "0", "-players", "2"])!);
            string address = $"127.0.0.1:{other.Port}";
            menu.M_ClearMenus();
            foreach (Key key in new[] { Key.Escape, Key.M, Key.Enter, Key.J, Key.Enter, Key.Enter })
                s.MenuEvent(KeyEvent(key));
            if (menu.StateName != "join" || !menu.AddressEnter)
                Fail(Step("Multiplayer → Join Game → Address"));
            foreach (char c in address)
                s.MenuEvent(TypeEvent(c));
            if (menu.JoinAddress != address)
                Fail(Step($"the typed address {menu.JoinAddress}, expected {address}"));
            s.MenuEvent(KeyEvent(Key.Enter));
            if (menu.StateName != "lobby" || host.Lobby is not { Host: false })
                Fail(Step("the join's lobby page"));
            if (!await NetFrames(() => s.NetGame is not null && other.Client.State == NetClientState.InGame && menu.StateName == "closed", other))
                Fail(Step("the joined game's start"));
            else if (s.World is not { netgame: true, consoleplayer: 1 })
                Fail(Step($"the joined game: local player {s.World?.consoleplayer}"));
            else if (s.LocalPlayerKey() || s.World.consoleplayer != 1)
                Fail(Step($"F12 in the joined game (refused): local player {s.World.consoleplayer + 1}"));
            if (s.GetSetting("net/addresses").Split(' ').FirstOrDefault() != address)
                Fail(Step($"the address kept: \"{s.GetSetting("net/addresses")}\""));
        }
        finally
        {
            other?.Dispose();
            s.EndNetGameForCheck();
        }

        // a host on another IWAD refuses the join: its address picked from the recent ones with the pad's A
        try
        {
            other = s.CheckSession(NetOptions.Parse(["-host", "0", "-players", "2"])!, otherIwad: true);
            s.SetSetting("net/addresses", $"127.0.0.1:{other.Port} {s.GetSetting("net/addresses")}");
            menu.M_ClearMenus();
            foreach (Key key in new[] { Key.Escape, Key.M, Key.Enter, Key.J, Key.Enter })
                s.MenuEvent(KeyEvent(key));
            while (menu.itemOn != 3 && menu.StateName == "join")
                s.MenuEvent(KeyEvent(Key.Down));
            s.MenuEvent(PadEvent(JoyButton.A));
            if (!await NetFrames(() => menu.StateName == "message", other))
                Fail(Step("the refused join's message"));
            else if (!menu.messageString.Replace('\n', ' ').StartsWith("can't join the game: the host plays another iwad.", StringComparison.Ordinal) || s.Net is not null || flow.netsession)
                Fail(Step("the refused join"));
            s.MenuEvent(PadEvent(JoyButton.A));
            if (menu.StateName != "join")
                Fail(Step("back on the join page"));
        }
        finally
        {
            other?.Dispose();
            s.EndNetGameForCheck();
            menu.M_ClearMenus();
            s.SetSetting("net/addresses", addresses);
            s.SetSetting("net/port", Settings.Find("net/port")!.Default);
        }
        if (_failures == failures)
            GD.Print("Level check: lobby (T11.9): hosted from the menus, joined by a second machine, a start refused and shown, the game begun on both, F12 refused in it; joined from Join Game with a typed address and kept; a join on another IWAD refused and shown");
    }

    /// <summary>T11.9: the Multiplayer, Host Game and lobby pages as drawn (a session hosted alone for the lobby), every pixel against the CPU's.</summary>
    private async Task CheckLobbyDrawn()
    {
        LevelScene s = _scene;
        GameFlow flow = s.Flow;
        MMenu menu = flow.Menu;
        bool overlay = s.Overlay.Visible;
        s.Overlay.Visible = false;
        flow.D_StartTitle(null);
        s.Tic(new ticcmd_t());
        s.SetSetting("net/port", "0");
        int pixels = 0;
        try
        {
            menu.M_ClearMenus();
            foreach (Key key in new[] { Key.Escape, Key.M, Key.Enter })
                s.MenuEvent(KeyEvent(key));
            s.UpdateScreens();
            pixels += await CompareMenuScreen("the multiplayer page (T11.9)") ?? 0;
            s.MenuEvent(KeyEvent(Key.H)); // Host Game (the page may be on Join Game: its last item)
            s.MenuEvent(KeyEvent(Key.Enter));
            s.UpdateScreens();
            pixels += await CompareMenuScreen("the host page (T11.9)") ?? 0;
            s.MenuEvent(KeyEvent(Key.S));
            s.MenuEvent(KeyEvent(Key.Enter));
            if (!await NetFrames(() => ((INetMenuHost)s).Lobby?.Players.Count == 1, null, 3))
                Fail($"lobby (T11.9): the lobby page drawn: the host alone not in its lobby ({s.NetText()})");
            s.UpdateScreens();
            pixels += await CompareMenuScreen("the lobby page (T11.9)") ?? 0;
        }
        finally
        {
            s.EndNetGameForCheck();
            menu.M_ClearMenus();
            s.SetSetting("net/port", Settings.Find("net/port")!.Default);
            s.Overlay.Visible = overlay;
        }
        GD.Print($"Level check: lobby (T11.9): the multiplayer, host and lobby pages drawn, {pixels} pixels compared");
    }
}
