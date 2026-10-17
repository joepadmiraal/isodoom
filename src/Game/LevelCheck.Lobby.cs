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
// T11.9d: the check's hosts answer LAN discovery on free ports of their own
// (the scene's too), and Join Game's browser is aimed at them: the page lists
// the check's host by name and the pad's A joins it; the other-IWAD host is
// listed greyed, ANOTHER IWAD, and picking it shows why back on the page.
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

    /// <summary>T11.9d: the Join Game page's LAN row of <paramref name="name"/>, or null.</summary>
    private static MMenu.menuitem_t? LanRow(MMenu menu, string name) =>
        menu.LanBrowser is null ? null : menu.JoinDef.menuitems.Take(menu.JoinDef.numitems).FirstOrDefault(i => i.status == 1 && i.text == name);

    /// <summary>T11.9d: the cursor moved down to <paramref name="item"/> on the current page (false if it isn't there).</summary>
    private bool CursorTo(MMenu menu, MMenu.menuitem_t item)
    {
        for (int i = 0; i < 20 && menu.currentMenu.menuitems[menu.itemOn] != item; i++)
            _scene.MenuEvent(KeyEvent(Key.Down));
        return menu.currentMenu.menuitems[menu.itemOn] == item;
    }

    private async Task CheckLobby()
    {
        LevelScene s = _scene;
        GameFlow flow = s.Flow;
        MMenu menu = flow.Menu;
        INetMenuHost host = s;
        int failures = _failures;
        string Step(string what) => $"lobby (T11.9): {what}: menu {menu.StateText()}, net {s.NetText()}";
        string addresses = s.GetSetting("net/addresses"), playerName = s.GetSetting("net/name");
        s.SetSetting("net/port", "0"); // any free port: the lobby page shows it
        s.DiscoveryPort = 0; // T11.9d: the scene's host answers LAN discovery on a free port too
        flow.D_StartTitle(null);
        menu.M_ClearMenus();

        // T11.9c: the scene's player name typed on the Multiplayer page (P, Enter, the name, Enter); the check's machine is CHECK
        const string Name = "DOOMGUY 2";
        foreach (Key key in new[] { Key.Escape, Key.M, Key.Enter, Key.P, Key.Enter })
            s.MenuEvent(KeyEvent(key));
        if (!menu.NameEnter)
            Fail(Step("Multiplayer → Player Name"));
        for (int i = 0; i < Settings.MaxNameLength; i++)
            s.MenuEvent(KeyEvent(Key.Backspace));
        foreach (char c in "doomguy 2")
            s.MenuEvent(c == ' ' ? Held(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = true, Unicode = ' ' }) : TypeEvent(c));
        s.MenuEvent(KeyEvent(Key.Enter));
        if (menu.NameEnter || s.GetSetting("net/name") != Name)
            Fail(Step($"the typed name: \"{s.GetSetting("net/name")}\", expected \"{Name}\""));

        // the scene hosts: H (Host Game), Enter, then S (Start Hosting), Enter
        foreach (Key key in new[] { Key.H, Key.Enter })
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
        // T11.9d: the host's lobby page shows where the others reach it, and it answers LAN discovery
        if (host.Lobby.HostAddresses is not { Count: > 0 } where || !where.All(a => a.EndsWith($":{s.Net.Port}", StringComparison.Ordinal))
            || menu.HostingLine()?.StartsWith("HOSTING ON " + where[0], StringComparison.Ordinal) != true || s.Net.Discovery is null)
            Fail(Step($"the host's addresses [{string.Join(", ", host.Lobby.HostAddresses ?? [])}], {menu.HostingLine()}, discovery {s.Net.Discovery?.Port}: {s.Net.DiscoveryError} (T11.9d)"));
        int port = s.Net.Port;
        NetSession? join = null;
        try
        {
            join = s.CheckSession(NetOptions.Parse(["-connect", $"127.0.0.1:{port}"])!);
            if (!await NetFrames(() => host.Lobby?.Players.Count == 2 && s.Net.Server.CanStart && join.Client.State == NetClientState.Lobby
                && join.LobbyPlayers().Count == 2, join))
                Fail(Step("the check's machine joining"));
            NetLobbyView lobby = host.Lobby!;
            if (lobby.Players.Select(p => p.Slot).SequenceEqual([0, 1]) is false || !lobby.Players[0].Local || lobby.MaxPlayers != 4 || lobby.MapName != s.DefaultMap())
                Fail(Step($"the lobby: {lobby}"));
            // T11.9c: both machines' lobby pages name both players
            CheckNames("the host's lobby page", lobby.Players.Select(p => p.Name), [Name, "CHECK"]);
            CheckNames("the check's machine's lobby", join.LobbyPlayers().Select(p => p.Name), [Name, "CHECK"]);

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
            // T11.9c: the names on both lobbies, before the host starts (at its -players: it may start at once)
            if (!await NetFrames(() => host.Lobby?.Players.Count == 2 && host.Lobby.Players.All(p => p.Name.Length > 0) || s.NetGame is not null, other))
                Fail(Step("the join's lobby with both players"));
            else
            {
                CheckNames("the join's lobby page", host.Lobby!.Players.Select(p => p.Name), ["CHECK", Name]);
                CheckNames("the check's host's lobby", other.LobbyPlayers().Select(p => p.Name), ["CHECK", Name]);
            }
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

        // T11.9d: the check's host found on the LAN: Join Game lists it by name, compatible, and the pad's A joins it
        try
        {
            other = s.CheckSession(NetOptions.Parse(["-host", "0", "-players", "2"])!, name: "LANHOST", discovery: true);
            if (other.Discovery is null)
                Fail(Step($"the check's host not answering discovery: {other.DiscoveryError} (T11.9d)"));
            else
            {
                s.DiscoveryTargets = [new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, other.Discovery.Port)];
                menu.M_ClearMenus();
                foreach (Key key in new[] { Key.Escape, Key.M, Key.Enter, Key.J, Key.Enter })
                    s.MenuEvent(KeyEvent(key));
                if (!await NetFrames(() => LanRow(menu, "LANHOST") is not null, other, 5))
                    Fail(Step("Join Game listing the check's host (T11.9d)"));
                else if (LanRow(menu, "LANHOST") is not { grey: false } row || row.value?.Invoke() != $"{s.DefaultMap()} COOP 1/2")
                    Fail(Step($"the check's host's row: {LanRow(menu, "LANHOST")?.value?.Invoke()}, grey {LanRow(menu, "LANHOST")?.grey} (T11.9d)"));
                else if (!CursorTo(menu, row))
                    Fail(Step("the cursor on the check's host's row (T11.9d)"));
                else
                {
                    s.MenuEvent(PadEvent(JoyButton.A));
                    if (menu.StateName != "lobby" || host.Lobby is not { Host: false })
                        Fail(Step("the LAN game joined with the pad's A (T11.9d)"));
                    else if (!await NetFrames(() => s.NetGame is not null && other.Client.State == NetClientState.InGame && menu.LanBrowser is null, other))
                        Fail(Step("the LAN game's start (T11.9d)"));
                    else if (s.GetSetting("net/addresses").Split(' ').FirstOrDefault() != $"127.0.0.1:{other.Port}")
                        Fail(Step($"the LAN game's address kept: \"{s.GetSetting("net/addresses")}\" (T11.9d)"));
                }
            }
        }
        finally
        {
            other?.Dispose();
            s.EndNetGameForCheck();
        }

        // a host on another IWAD refuses the join: T11.9d: listed greyed (ANOTHER IWAD), picked: why, back on the page;
        // then its address picked from the recent ones with the pad's A
        try
        {
            other = s.CheckSession(NetOptions.Parse(["-host", "0", "-players", "2"])!, otherIwad: true, discovery: true);
            s.DiscoveryTargets = other.Discovery is { } d ? [new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, d.Port)] : [];
            string recent = $"127.0.0.1:{other.Port}";
            s.SetSetting("net/addresses", $"{recent} {s.GetSetting("net/addresses")}");
            menu.M_ClearMenus();
            foreach (Key key in new[] { Key.Escape, Key.M, Key.Enter, Key.J, Key.Enter })
                s.MenuEvent(KeyEvent(key));
            if (!await NetFrames(() => LanRow(menu, "CHECK") is not null, other, 5) || LanRow(menu, "CHECK") is not { grey: true } greyed)
                Fail(Step($"the other IWAD's host listed greyed: {LanRow(menu, "CHECK")?.grey} (T11.9d)"));
            else if (greyed.value?.Invoke() != "ANOTHER IWAD (OTHER.WAD)" || !CursorTo(menu, greyed))
                Fail(Step($"the other IWAD's row: {greyed.value?.Invoke()} (T11.9d)"));
            else
            {
                s.MenuEvent(PadEvent(JoyButton.A));
                if (menu.StateName != "message" || menu.messageString.Replace('\n', ' ').StartsWith("can't join check's game: another iwad (other.wad).", StringComparison.Ordinal) is false
                    || s.Net is not null || menu.LastSound != sfxenum_t.sfx_oof)
                    Fail(Step($"the greyed LAN game picked: \"{menu.messageString}\", {menu.LastSound} (T11.9d)"));
                s.MenuEvent(PadEvent(JoyButton.A));
                if (menu.StateName != "join")
                    Fail(Step("back on the join page after the greyed game (T11.9d)"));
            }
            if (menu.currentMenu.menuitems.Take(menu.currentMenu.numitems).FirstOrDefault(i => i.text == recent) is not { } recentRow || !CursorTo(menu, recentRow))
                Fail(Step($"the recent address {recent} on the join page"));
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
            s.DiscoveryTargets = null;
            s.DiscoveryPort = IsoDoom.Net.NetDiscovery.Port;
            s.SetSetting("net/addresses", addresses);
            s.SetSetting("net/port", Settings.Find("net/port")!.Default);
            s.SetSetting("net/name", playerName);
        }
        if (_failures == failures)
            GD.Print("Level check: lobby (T11.9): hosted from the menus, joined by a second machine, a start refused and shown, the game begun on both, F12 refused in it; joined from Join Game with a typed address and kept; a join on another IWAD refused and shown; T11.9c: the player name typed, both players named on both machines' lobbies; T11.9d: the host's addresses shown, the LAN's host listed and joined with the pad's A, the other IWAD's greyed and refused");

        void CheckNames(string where, System.Collections.Generic.IEnumerable<string> names, string[] expected)
        {
            if (!names.SequenceEqual(expected))
                Fail(Step($"{where}: names [{string.Join(", ", names)}], expected [{string.Join(", ", expected)}] (T11.9c)"));
        }
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
        s.DiscoveryPort = 0; // T11.9d
        int pixels = 0;
        try
        {
            menu.M_ClearMenus();
            foreach (Key key in new[] { Key.Escape, Key.M, Key.Enter })
                s.MenuEvent(KeyEvent(key));
            s.UpdateScreens();
            pixels += await CompareMenuScreen("the multiplayer page (T11.9)", dimmed: true) ?? 0;
            s.MenuEvent(KeyEvent(Key.H)); // Host Game (the page may be on Join Game: its last item)
            s.MenuEvent(KeyEvent(Key.Enter));
            s.UpdateScreens();
            pixels += await CompareMenuScreen("the host page (T11.9)", dimmed: true) ?? 0;
            s.MenuEvent(KeyEvent(Key.S));
            s.MenuEvent(KeyEvent(Key.Enter));
            if (!await NetFrames(() => ((INetMenuHost)s).Lobby?.Players.Count == 1, null, 3))
                Fail($"lobby (T11.9): the lobby page drawn: the host alone not in its lobby ({s.NetText()})");
            s.UpdateScreens();
            pixels += await CompareMenuScreen("the lobby page (T11.9)", dimmed: true) ?? 0;
            s.EndNetGameForCheck();

            // T11.9d: the join page listing a LAN game (and one greyed: another IWAD's)
            using NetSession lan = s.CheckSession(NetOptions.Parse(["-host", "0", "-players", "2"])!, name: "LANHOST", discovery: true);
            using NetSession greyed = s.CheckSession(NetOptions.Parse(["-host", "0", "-players", "2"])!, otherIwad: true, discovery: true);
            s.DiscoveryTargets = [.. new[] { lan, greyed }.Where(n => n.Discovery is not null).Select(n => new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, n.Discovery!.Port))];
            menu.M_ClearMenus();
            foreach (Key key in new[] { Key.Escape, Key.M, Key.Enter, Key.J, Key.Enter })
                s.MenuEvent(KeyEvent(key));
            ulong deadline = Time.GetTicksMsec() + 5000;
            while ((LanRow(menu, "LANHOST") is null || LanRow(menu, "CHECK") is null) && Time.GetTicksMsec() < deadline)
            {
                lan.Update((long)Time.GetTicksUsec());
                greyed.Update((long)Time.GetTicksUsec());
                await NextFrame();
            }
            if (LanRow(menu, "LANHOST") is null || LanRow(menu, "CHECK") is null)
                Fail("lobby (T11.9d): the join page drawn: the LAN games not listed");
            s.UpdateScreens();
            pixels += await CompareMenuScreen("the join page with LAN games (T11.9d)", dimmed: true) ?? 0;
        }
        finally
        {
            s.EndNetGameForCheck();
            s.DiscoveryTargets = null;
            s.DiscoveryPort = IsoDoom.Net.NetDiscovery.Port;
            menu.M_ClearMenus();
            s.SetSetting("net/port", Settings.Find("net/port")!.Default);
            s.Overlay.Visible = overlay;
        }
        GD.Print($"Level check: lobby (T11.9): the multiplayer, host, lobby and (T11.9d) join pages drawn over the window dimmed (T11.9b), {pixels} pixels compared");
    }
}
