using System;
using System.Collections.Generic;
using System.Globalization;
using IsoDoom.Net;
using IsoDoom.Sim;

namespace IsoDoom.Game;

/// <summary>
/// T11.9: what the multiplayer pages need from the game scene: hosting a
/// game with the host page's settings (<c>net/*</c>), joining one, leaving
/// the lobby, the lobby's state and the host's Start. The level scene
/// implements it over its <see cref="NetSession"/>; the tests fake it.
/// </summary>
public interface INetMenuHost
{
    /// <summary>Hosts a game from the <c>net/*</c> settings (the host page's); null when it listens, else why not.</summary>
    string? HostGame();

    /// <summary>Joins the game at <paramref name="address"/> (<c>ADDR[:PORT]</c>, as <c>-connect</c>); null when it is under way, else why not.</summary>
    string? JoinGame(string address);

    /// <summary>Leaves the lobby: the session ends (the host's ends the others' too) and the game is single player again.</summary>
    void LeaveGame();

    /// <summary>The session's lobby as this machine knows it, or null without one.</summary>
    NetLobbyView? Lobby { get; }

    /// <summary>The host's Start (<see cref="NetSession.RequestStart"/>): false when it can't ask yet.</summary>
    bool StartGame();
}

/// <summary>
/// T11.9: what the lobby page shows: who hosts or where this machine joined,
/// the session's state, the players (slot, readiness, round trip), the game's
/// settings and WAD, and why it ended or can't start.
/// </summary>
/// <param name="Host">This machine hosts.</param>
/// <param name="FromMenus">The menus made the session (not the command line: it ends with a failure).</param>
/// <param name="Address">The host's address (this machine's own for the host).</param>
/// <param name="Port">The host's port.</param>
/// <param name="State">This machine's client's state.</param>
/// <param name="MaxPlayers">The game's slots.</param>
/// <param name="Players">The players in the lobby.</param>
/// <param name="Settings">The game the host starts (a join's once the host sent it), or null.</param>
/// <param name="MapName">Its first map, or null.</param>
/// <param name="Wad">The IWAD (and PWADs) every machine plays.</param>
/// <param name="Failure">Why the session ended, or null.</param>
/// <param name="Refusal">Why the host refused this machine, or null.</param>
/// <param name="StartRefusal">Why the host does not start with these players (T11.1a), or null.</param>
/// <param name="StartRefusals">How many times the host refused to start (each shows once).</param>
/// <param name="Starting">The host's Start was asked for, the game not begun yet.</param>
/// <param name="InGame">The game began.</param>
public sealed record NetLobbyView(bool Host, bool FromMenus, string Address, int Port, NetClientState State, int MaxPlayers,
    IReadOnlyList<NetLobbyPlayer> Players, NetGameSettings? Settings, string? MapName, string Wad,
    string? Failure, NetRefusal? Refusal, string? StartRefusal, int StartRefusals, bool Starting, bool InGame);

// T11.9: the multiplayer pages (not vanilla's: its netgames came from the
// command line, or the setup program): Multiplayer (from the main menu) →
// Host Game (the game's settings, each at its last value: net/*) or Join Game
// (an address typed, or one of the last few picked, which a pad can) → the
// lobby page (the players, the game, the host's Start; Escape leaves it).
// Drawn in the message font as the options' text pages (T7.3).
public sealed partial class MMenu
{
    /// <summary>T11.9: the scene's side of the multiplayer pages; null in tests without one (the pages then refuse).</summary>
    public INetMenuHost? NetHost { get; set; }

    /// <summary>T11.9: the Multiplayer page (Host Game, Join Game).</summary>
    public menu_t MultiplayerDef { get; private set; } = null!;

    /// <summary>T11.9: the Host Game page.</summary>
    public menu_t HostDef { get; private set; } = null!;

    /// <summary>T11.9: the Join Game page.</summary>
    public menu_t JoinDef { get; private set; } = null!;

    /// <summary>T11.9: the lobby page.</summary>
    public menu_t LobbyDef { get; private set; } = null!;

    /// <summary>T11.9: Join Game's address is being typed (as a save's description).</summary>
    public bool AddressEnter { get; private set; }

    private string _address = "", _addressOld = "";

    /// <summary>T11.9: Join Game's address.</summary>
    public string JoinAddress => _address;

    /// <summary>Whether the keys type text (a save's description, T11.9's address): the menu keys are the typed keys then.</summary>
    public bool Typing => saveStringEnter || AddressEnter;

    private int _seenStartRefusals;
    private bool _failureShown;

    // T11.9 (T11.11's follow-up): the lobby's slots last seen, and who left it (a quit or -nettimeout's drop)
    private int _lobbySlots;
    private string? _lobbyNote;

    /// <summary>T11.9: the rows of the lobby page's items (below its players).</summary>
    public const short LobbyItemsY = 152;

    /// <summary>T11.9: the players' colours (hu_stuff.c's <c>HUSTR_PLRGREEN</c>… for the chat's names).</summary>
    public static readonly string[] PlayerColours = ["GREEN", "INDIGO", "BROWN", "RED"];

    // d_englsh.h's skill names, as the host page shows them
    private static readonly string[] _skillNames = ["I'M TOO YOUNG TO DIE", "HEY, NOT TOO ROUGH", "HURT ME PLENTY", "ULTRA-VIOLENCE", "NIGHTMARE!"];

    /// <summary>T11.9: the message when Multiplayer is picked in a game that is already a netgame.</summary>
    public const string NETALREADY = "you are in a netgame already.\n\n" + PRESSKEY;

    private void NetMenus()
    {
        MultiplayerDef = TextMenu("multiplayer", "MULTIPLAYER", MainDef,
        [
            new(1, "", _ => M_SetupNextMenu(HostDef), 'h', "HOST GAME..."),
            new(1, "", _ => JoinPage(), 'j', "JOIN GAME..."),
        ]);
        HostDef = TextMenu("host", "HOST GAME", MultiplayerDef, HostItems());
        SettingDefs[SettingPage.Net] = HostDef;
        JoinDef = TextMenu("join", "JOIN GAME", MultiplayerDef, []);
        JoinDef.routine = DrawJoin;
        JoinItems();
        LobbyDef = new menu_t("lobby", 0, MultiplayerDef, [], null, TextX, LobbyItemsY, 0)
        {
            textItems = true,
            lineHeight = TEXTLINEHEIGHT,
        };
        LobbyDef.routine = DrawLobby;
    }

    private string NetSetting(string key) => SetupHost?.GetSetting(key) ?? Settings.Find(key)!.Default;

    private menuitem_t[] HostItems()
    {
        var items = new List<menuitem_t>();
        foreach (SettingDef def in Settings.OnPage(SettingPage.Net))
        {
            SettingDef d = def;
            Action<int> step = choice => StepSetting(d, choice == 0 ? -1 : 1);
            Func<string> value = () => Settings.Display(NetSetting(d.Key));
            switch (d.Key)
            {
                case "net/addresses":
                    continue; // Join Game's
                case "net/map":
                    step = StepHostMap;
                    value = HostMap;
                    break;
                case "net/mode":
                    value = () => ModeName(NetSetting(d.Key) switch { "deathmatch" => 1, "altdeath" => 2, _ => 0 });
                    break;
                case "net/skill":
                    value = () => int.TryParse(NetSetting(d.Key), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n is >= 1 and <= 5 ? _skillNames[n - 1] : "?";
                    break;
                case "net/port":
                    value = () => NetSetting(d.Key) is "0" ? "ANY" : NetSetting(d.Key);
                    break;
            }
            items.Add(new menuitem_t(2, "", step, '\0', d.Label) { value = value });
        }
        items.Add(new menuitem_t(-1, "", null, '\0'));
        items.Add(new menuitem_t(1, "", _ => HostGo(), 's', "START HOSTING"));
        return [.. items];
    }

    private static string ModeName(int deathmatch) => deathmatch switch { 1 => "DEATHMATCH", 2 => "ALTDEATH", _ => "CO-OP" };

    /// <summary>T11.9: the host page's map: <c>net/map</c> when the WAD has it, else the WAD's first.</summary>
    public string HostMap()
    {
        List<string> maps = flow.HostMaps();
        string map = NetSetting("net/map");
        return maps.Contains(map) ? map : maps.Count > 0 ? maps[0] : map;
    }

    private void StepHostMap(int choice)
    {
        List<string> maps = flow.HostMaps();
        if (maps.Count == 0 || SetupHost is not { } host)
            return;
        host.SetSetting("net/map", maps[Step(maps.IndexOf(HostMap()), maps.Count, choice)]);
    }

    /// <summary>T11.9: Start Hosting: the session listens, the lobby page shows; else why not, as a message back on the page.</summary>
    private void HostGo()
    {
        if (NetHost is null)
        {
            _itemSound = sfxenum_t.sfx_oof;
            return;
        }
        if (NetHost.HostGame() is { } error)
        {
            M_StartMessage(Wrap("can't host the game: " + error) + "\n\n" + PRESSKEY, null, false, HostDef);
            return;
        }
        ShowLobby(HostDef);
    }

    /// <summary>T11.9: the Join Game page, its address the last one used.</summary>
    private void JoinPage()
    {
        AddressEnter = false;
        JoinItems();
        string[] recent = RecentAddresses();
        _address = recent.Length > 0 ? recent[0] : _address;
        M_SetupNextMenu(JoinDef);
    }

    private string[] RecentAddresses() => NetSetting("net/addresses").Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The Join Game page's items: the address (Enter types it), Connect, and the last few addresses (each joins).</summary>
    private void JoinItems()
    {
        var items = new List<menuitem_t>
        {
            new(1, "", _ => StartAddress(), 'a', "ADDRESS"),
            new(1, "", _ => { if (!Connect(_address)) _itemSound = sfxenum_t.sfx_oof; }, 'c', "CONNECT"),
        };
        string[] recent = RecentAddresses();
        if (recent.Length > 0)
            items.Add(new menuitem_t(-1, "", null, '\0'));
        foreach (string address in recent)
        {
            string a = address;
            items.Add(new menuitem_t(1, "", _ => { if (!Connect(a)) _itemSound = sfxenum_t.sfx_oof; }, '\0', a));
        }
        SetTextItems(JoinDef, [.. items]);
        if (JoinDef.lastOn >= items.Count)
            JoinDef.lastOn = 0;
    }

    private void StartAddress()
    {
        AddressEnter = true;
        _addressOld = _address;
        Changes++;
    }

    /// <summary>T11.9: a key while the address is typed: Backspace, Escape (the old one back), Enter (connects), else the character typed.</summary>
    private bool AddressKey(int key, int ch)
    {
        // as a save's description: the pad's A (or the left button) is Enter, B (or the right button) Escape
        if (key == KEY_PAD_ACCEPT)
            key = KEY_ENTER;
        else if (key == KEY_PAD_CANCEL)
            key = KEY_ESCAPE;
        if (key == KEY_BACKSPACE)
        {
            if (_address.Length > 0)
                _address = _address[..^1];
        }
        else if (key == KEY_ESCAPE)
        {
            AddressEnter = false;
            _address = _addressOld;
        }
        else if (key == KEY_ENTER)
        {
            AddressEnter = false;
            if (!Connect(_address))
                S_StartSound(sfxenum_t.sfx_oof);
        }
        else if (ch is > ' ' and < 127 && Settings.ValidAddress(_address + (char)ch))
            _address += (char)ch;
        return true;
    }

    /// <summary>T11.9: joins <paramref name="address"/>: the lobby page shows (connecting), or why not as a message; false for no address.</summary>
    private bool Connect(string address)
    {
        if (NetHost is null || !Settings.ValidAddress(address))
            return false;
        _address = address;
        if (NetHost.JoinGame(address) is { } error)
        {
            M_StartMessage(Wrap("can't join the game: " + error) + "\n\n" + PRESSKEY, null, false, JoinDef);
            return true;
        }
        ShowLobby(JoinDef);
        return true;
    }

    /// <summary>T11.9: the main menu's Multiplayer: the lobby when a session waits in it, a message in a netgame, else the Multiplayer page.</summary>
    private void Multiplayer(int choice)
    {
        if (NetHost?.Lobby is { InGame: false })
        {
            ShowLobby(MultiplayerDef);
            return;
        }
        if (flow.InNetGame)
        {
            M_StartMessage(NETALREADY, null, false);
            return;
        }
        M_SetupNextMenu(MultiplayerDef);
    }

    /// <summary>T11.9: the lobby page of the session that waits (the command line's opens it too), Escape going back to <paramref name="back"/>.</summary>
    public void ShowLobby(menu_t? back = null)
    {
        bool host = NetHost?.Lobby is { Host: true };
        menuitem_t[] items = host
            ? [new(1, "", _ => StartLobbyGame(), 's', "START GAME"), new(1, "", _ => LeaveLobby(), 'l', "LEAVE")]
            : [new(1, "", _ => LeaveLobby(), 'l', "LEAVE")];
        SetTextItems(LobbyDef, items);
        LobbyDef.prevMenu = back ?? MultiplayerDef;
        LobbyDef.lastOn = 0;
        _seenStartRefusals = NetHost?.Lobby?.StartRefusals ?? 0;
        _failureShown = false;
        _lobbySlots = SlotsOf(NetHost?.Lobby);
        _lobbyNote = null;
        menuactive = true;
        M_SetupNextMenu(LobbyDef);
    }

    /// <summary>T11.9: the host's Start (refused, <c>sfx_oof</c>, with fewer than two players in).</summary>
    private void StartLobbyGame()
    {
        if (NetHost?.Lobby is not { Host: true } lobby || lobby.Players.Count < NetSession.MinPlayers || !NetHost.StartGame())
            _itemSound = sfxenum_t.sfx_oof;
    }

    /// <summary>T11.9: Escape, back or Leave on the lobby page: the session ends, the page it came from shows.</summary>
    public void LeaveLobby()
    {
        NetHost?.LeaveGame();
        M_SetupNextMenu(LobbyDef.prevMenu ?? MultiplayerDef);
        S_StartSound(sfxenum_t.sfx_swtchx);
    }

    /// <summary>
    /// T11.9: each frame, after the session's poll: the lobby page closes when
    /// the game begins; a session that ended before it (refused, no answer,
    /// the host gone) shows why as a message, back on the page it came from
    /// (a session from the menus is left); each refusal of the host's start
    /// (T11.1a: a map without the players' starts) shows as a message on the lobby page.
    /// </summary>
    public void NetTicker()
    {
        if (NetHost?.Lobby is not { } lobby)
            return;
        if (lobby.InGame)
        {
            if (menuactive && !messageToPrint && (currentMenu == LobbyDef || currentMenu == MultiplayerDef || currentMenu == HostDef || currentMenu == JoinDef))
                M_ClearMenus();
            return;
        }
        if (lobby.Failure is not null)
        {
            if (_failureShown)
                return;
            _failureShown = true;
            menu_t? back = lobby.FromMenus ? (lobby.Host ? HostDef : JoinDef) : null;
            if (lobby.FromMenus)
                NetHost.LeaveGame();
            AddressEnter = false;
            M_StartMessage(FailureMessage(lobby) + "\n\n" + PRESSKEY, null, false, back);
            return;
        }
        int slots = SlotsOf(lobby);
        int left = _lobbySlots & ~slots;
        if (left != 0)
        {
            _lobbyNote = $"PLAYER {System.Numerics.BitOperations.TrailingZeroCount(left) + 1} LEFT THE LOBBY";
            Changes++;
        }
        _lobbySlots = slots;
        if (lobby.StartRefusals != _seenStartRefusals)
        {
            _seenStartRefusals = lobby.StartRefusals;
            M_StartMessage(Wrap("the game can't start: " + lobby.StartRefusal) + "\n\n" + PRESSKEY, null, false, LobbyDef);
        }
    }

    private static int SlotsOf(NetLobbyView? lobby)
    {
        int mask = 0;
        if (lobby is not null)
        {
            foreach (NetLobbyPlayer p in lobby.Players)
                mask |= 1 << p.Slot;
        }
        return mask;
    }

    /// <summary>T11.9: the lobby page's note of a player who left (a quit or a timeout), or null.</summary>
    public string? LobbyNote => _lobbyNote;

    /// <summary>T11.9: why a session ended, for its message.</summary>
    public string FailureMessage(NetLobbyView lobby)
    {
        ArgumentNullException.ThrowIfNull(lobby);
        string text = lobby.Refusal switch
        {
            NetRefusal.ProtocolMismatch or NetRefusal.BuildMismatch => "the host runs another version of isodoom.",
            NetRefusal.IwadMismatch => "the host plays another iwad.",
            NetRefusal.PwadMismatch => "the host plays other pwads.",
            NetRefusal.GameFull => "the game is full.",
            NetRefusal.GameInProgress => "the game has started already.",
            _ when lobby.Host => "the netgame ended: " + lobby.Failure,
            _ when lobby.State is NetClientState.Connecting or NetClientState.Idle || lobby.Players.Count == 0 => $"no answer from {lobby.Address}:{lobby.Port}.",
            _ => "the host left the game.",
        };
        return Wrap(lobby.Refusal is null ? text : "can't join the game: " + text);
    }

    /// <summary>T11.9: <paramref name="text"/> broken into lines at spaces that fit vanilla's screen (300 columns of the message font).</summary>
    public string Wrap(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = new List<string>();
        foreach (string paragraph in text.Split('\n'))
        {
            string line = "";
            foreach (string word in paragraph.Split(' '))
            {
                string next = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && M_StringWidth(next) > 300)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                    line = next;
            }
            lines.Add(line);
        }
        return string.Join('\n', lines);
    }

    private void DrawJoin()
    {
        DrawTextTitle("JOIN GAME", JoinDef);
        // the address right of its item, wider than a value: it is typed
        string address = (_address + (AddressEnter ? "_" : "")).ToUpperInvariant();
        M_WriteText(JoinDef.x + 72, JoinDef.y, address);
        if (JoinDef.numitems > 2)
            M_WriteText(JoinDef.x, JoinDef.y + 2 * JoinDef.lineHeight, "RECENT:");
        string hint = AddressEnter ? "ENTER: CONNECT   ESCAPE: CANCEL" : "ENTER ON ADDRESS: TYPE IT";
        M_WriteText(HudScreen.SCREENWIDTH / 2 - M_StringWidth(hint) / 2, TEXTHINTY, hint);
    }

    /// <summary>T11.9: the lobby page: who hosts, the game and WAD, the players (slot, colour, ping), what it waits for.</summary>
    private void DrawLobby()
    {
        M_WriteText(HudScreen.SCREENWIDTH / 2 - M_StringWidth("LOBBY") / 2, 16, "LOBBY");
        if (NetHost?.Lobby is not { } lobby)
            return;
        int x = TextX, y = 30;
        string where = lobby.Host ? $"HOSTING ON PORT {lobby.Port}"
            : lobby.State is NetClientState.Connecting or NetClientState.Idle ? $"CONNECTING TO {lobby.Address}:{lobby.Port}..."
            : $"JOINED {lobby.Address}:{lobby.Port}";
        M_WriteText(x, y, where.ToUpperInvariant());
        M_WriteText(x, y += 10, lobby.Wad.ToUpperInvariant());
        if (lobby.Settings is { } s)
        {
            SpawnSettings sp = s.Spawn;
            string first = $"{lobby.MapName}   {ModeName(sp.deathmatch)}   SKILL {(int)sp.gameskill + 1}" + (sp.timelimit > 0 ? $"   {sp.timelimit} MIN" : "");
            string second = $"MONSTERS {(sp.nomonsters ? "OFF" : "ON")}" + (sp.respawnparm ? "   RESPAWN" : "") + (sp.fastparm ? "   FAST" : "");
            M_WriteText(x, y += 10, first);
            M_WriteText(x, y += 10, second);
        }
        else
            y += 20;
        y += 14;
        const int ColourX = 128, PingX = 208;
        M_WriteText(x, y, "PLAYER");
        M_WriteText(ColourX, y, "COLOUR");
        M_WriteText(PingX, y, "PING");
        for (int slot = 0; slot < Math.Clamp(lobby.MaxPlayers, 1, NetProtocol.MaxPlayers); slot++)
        {
            y += 10;
            int i = -1;
            for (int k = 0; k < lobby.Players.Count; k++)
            {
                if (lobby.Players[k].Slot == slot)
                    i = k;
            }
            if (i < 0)
            {
                M_WriteText(x, y, $"{slot + 1}  -");
                continue;
            }
            NetLobbyPlayer p = lobby.Players[i];
            M_WriteText(x, y, $"{slot + 1}{(slot == 0 ? "  HOST" : "")}{(p.Local ? "  (YOU)" : "")}");
            M_WriteText(ColourX, y, PlayerColours[slot]);
            M_WriteText(PingX, y, p.RttUs < 0 ? "-" : $"{(p.RttUs + 500) / 1000} MS");
        }
        string status = lobby.State is NetClientState.Connecting or NetClientState.Idle ? "WAITING FOR THE HOST'S ANSWER"
            : !lobby.Host ? "WAITING FOR THE HOST TO START"
            : lobby.Starting ? "STARTING..."
            : lobby.Players.Count < NetSession.MinPlayers ? "WAITING FOR PLAYERS TO JOIN"
            : "START WHEN EVERYONE IS IN";
        M_WriteText(x, LobbyItemsY - 14, status);
        if (_lobbyNote is { } note)
            M_WriteText(x, LobbyItemsY - 26, note);
        const string Hint = "ESCAPE: LEAVE THE LOBBY";
        M_WriteText(HudScreen.SCREENWIDTH / 2 - M_StringWidth(Hint) / 2, TEXTHINTY, Hint);
    }
}
