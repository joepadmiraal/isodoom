# Plan: player names and LAN discovery (M11, from T11.9)

Today a join must type the host's exact address. This plan lets a client find hosts on its local
network by itself, and gives every player a name to tell them apart. Two sessions, each one task
sized for one fresh Claude Code instance, in this order:

> Do task T11.9c from docs/TASKS.md (details in docs/plans/lan-discovery.md).

> Do task T11.9d from docs/TASKS.md (details in docs/plans/lan-discovery.md).

CLAUDE.md's session workflow applies as for every task: read the task, this plan and the SPEC
sections it cites, do it, tick it in `docs/TASKS.md` (with a *Done (date):* paragraph and any
follow-up tasks), log the decisions in SPEC §12, add every new argument, setting and level-script
command to CLAUDE.md's *Commands* section (the *Netgame* paragraph), update
`docs/player-readme.txt` where players see the change, and commit on a branch named after the task
(`t11.9c-player-name`, `t11.9d-lan-discovery`).

---

## Decisions (the user's, 2026-10-17)

The first task (T11.9c) logs them in SPEC §12 as one row, "Player names and LAN discovery".

| Question | Decision |
|---|---|
| Discovery scheme | **The client queries, the hosts answer** (Chocolate Doom's `net_query.c` LAN search): a join sends a small UDP query to the broadcast address on a fixed discovery port; every host listening there answers the sender directly. Not host beacons (two clients on one machine would fight over the port) and not mDNS/DNS-SD (too much for a 4-player LAN finder). |
| Host's addresses | **The host's lobby page also shows its LAN addresses** (`192.168.1.23:5029`), so a player can read one out when discovery can't work (guest Wi-Fi, another subnet, the internet). |
| When to search | **Automatically**, the whole time Join Game shows. No "Search" item. |
| Hosts a join can't play with | **Shown greyed** with the reason (another build, another IWAD or PWADs, full, started). Picking one plays `sfx_oof` and shows the reason as a message back on the page. They are not hidden. |
| IP version | **IPv4 only** for now. IPv6 (multicast to `ff02::1`) may come later; nothing here should rule it out. |
| Player names | **A player name option** (`net/name`), sent in the hello. It shows on the lobby page's slots and as the host's name in the LAN list. |

Still out of scope (the multiplayer plan's list): an internet server browser or master server, a
relay, NAT traversal, chat.

## What exists (inventory of 2026-10-17)

- **Protocol** (`src/Net`, plain .NET, no Godot reference; `IsoDoom.Net.csproj`):
  `NetMessages.cs`'s comment block is the wire format. `NetProtocol.Version = 3`.
  - Hello: protocol, build, IWAD MD5, PWAD MD5s, host token (`EncodeHello`, `NetIdentity`).
  - Lobby: occupied and ready masks (`EncodeLobby`).
  - `NetServer.Hello` refuses on protocol, build, IWAD, PWADs, `GameInProgress` or `GameFull`
    (`NetRefusal`).
  - The host's own client says hello like the others, so its name arrives the same way.
- **Session** (`src/Game/NetSession.cs`): the host's `NetServer` plus its own client, over ENet
  (`NetEnetTransport`, polled once a frame on the main thread). `NetOptions.BindAddress` defaults
  to `*`, and `127.0.0.1` means loopback only. `NetSession.Identity(version, iwad, pwads)` builds
  the identity.
- **Menus** (`src/Game/MMenu.Net.cs`, text pages as T7.3's `MMenu.Setup.cs`):
  - Multiplayer → Host Game (the `net/*` settings) or Join Game.
  - Join Game's items: ADDRESS (typed: `AddressEnter`/`AddressKey`, filtered by
    `Settings.ValidAddress`), CONNECT, then the last 5 addresses (`net/addresses`, each joining
    when picked).
  - The lobby page draws a `NetLobbyView` (players as `NetLobbyPlayer`: slot, colour, ping,
    readiness).
  - `M_StartMessage(…, back)` shows a message back on a page.
  - Text pages dim the window behind them (T11.9b).
- **Settings** (`src/Game/Settings.cs`): `net/*` keys on `SettingPage.Net`; `net/port` and
  `net/addresses` are special-cased in `NetValue`/validation. Only values that differ from the
  defaults are saved.
- **Level script and check:** `lobby`, `netwait`, `menu join|lobby`, `type ADDRESS`. The level
  check's multiplayer section (T11.9) runs a second `NetSession` in process over ENet loopback:
  host from the menus, join with a typed address, and a host on another IWAD refusing a recent
  address.

---

## T11.9c Player names

**Cites:** this plan's *Decisions*. SPEC §12 "T11.5 lockstep protocol", "Multiplayer menus and
lobby". CLAUDE.md *Netgame*.

**Work:**
- **Setting** `net/name`:
  - Default `""`, meaning "the OS user name" (`Environment.UserName`), resolved when used, so the
    setting's default stays a constant and headless runs stay machine-independent where it
    matters. Falls back to `PLAYER` when the user name has nothing drawable.
  - At most 15 characters, upper-cased, only what the menu font (`STCFN033`–`STCFN095`) draws, plus
    space. `Settings.ValidName` checks it, as `ValidAddress` checks an address.
- **Menu:** a `PLAYER NAME` item on the Multiplayer page, above Host Game, showing the name.
  - Enter types it, as Join Game's address: Backspace, Escape restores the old name, Enter keeps
    it. Lower case typed shows upper case.
  - `MMenu.Typing` covers it, so the glue sends typed characters.
  - An empty name typed means the default.
- **Protocol** (bump `NetProtocol.Version` to 4; update the comment block):
  - Hello gains the name (string) after the host token.
  - Lobby gains a name per occupied slot, in slot order.
  - The server cleans each name: truncate to 15, replace what the font can't draw with `?`, and
    use `PLAYER N` (N the slot + 1) for an empty name.
  - **Duplicate names are allowed:** the slot colour tells them apart.
  - Names are lobby-only. Nothing in the Start, the bundles or the sim changes, so no checksum
    can.
- **Presentation:**
  - The lobby page's slot rows show `NAME` beside the colour.
  - `NetLobbyPlayer` gets a `Name`.
  - The level script's `lobby` and `net` print the names.
  - The desync `summary.txt` names each slot's player.
  - Vanilla's "Player N left the game" message stays vanilla's: the sim knows no names.
- **Command line:** `-name NAME` (game argument, after `--`) overrides `net/name` for the run
  without saving, as `--level-*` overrides do. It is needed so `loopback.sh` and two-window tests
  can tell the players apart.

**Tests:**
- `NetMessages` round trips of the new Hello and Lobby.
- The server's cleaning (long, undrawable, empty and duplicate names).
- A protocol-3 hello refused with `ProtocolMismatch`.
- `SettingsTests` for `net/name` (default, validation, saved).
- The level check's multiplayer section: the host's and join's names on both lobby pages (typed
  through the input glue on one side, `-name` or the setting on the other).
- With a renderer, the lobby page comparison still passes, updated for the names.

**Done when:** two processes from the menus (`loopback.sh` or the level check) each show both
players' names on their lobby page, and all tests pass.

---

## T11.9d LAN discovery and the host's addresses

**Cites:** this plan's *Decisions*. Chocolate Doom `net_query.c` (`NET_LANQuery`,
`NET_Query_*`; GPL-2.0: port ideas freely, cite it). SPEC §12 "Multiplayer menus and lobby".
CLAUDE.md *Netgame*. After T11.9c (the reply carries the host's name).

**Wire format:** our own, separate from the lockstep protocol and from ENet. ENet owns the game
port's socket and drops packets it doesn't know, so discovery has a port of its own.
- **Discovery port:** `NetDiscovery.Port = 5028`, fixed (just below the game's default 5029; the
  Host Game page's port range starts at 5029). Every message starts with a 4-byte magic and a
  discovery version (u8), little-endian as `NetMessages`.
- **Query** (`ISDQ`):
  - A nonce (u32).
  - Zero padding to `NetDiscovery.QuerySize` (256 bytes), so a reply is never bigger than the
    query that asked for it: no amplification.
  - A responder ignores anything else: wrong size, magic or version.
- **Reply** (`ISDR`), at most `QuerySize` bytes, truncating the name and the map if it must:
  - the nonce echoed;
  - an instance id (u64, random per session, for deduplication);
  - the lockstep protocol version (u16) and the build (string);
  - the IWAD MD5 and the IWAD's file name (string, for display: `DOOM2.WAD`);
  - the PWAD count (u8) and one MD5 over the PWAD MD5s in load order, which the client compares
    with the same hash of its own list;
  - the host's name (T11.9c) and the game port (u16);
  - the phase (u8: lobby or started), players in (u8) and max players (u8);
  - the mode (u8), the first map's name (string) and the skill (u8).

**Work:**
- **`src/Net/NetDiscovery.cs`** (plain .NET `System.Net.Sockets`, not Godot's `PacketPeerUDP`:
  testable in `dotnet test`, and out of the Godot lifetime rules):
  - Encode and decode, both pure.
  - A `NetDiscoveryResponder`: a UDP socket bound to the discovery port, non-blocking, polled
    from `NetSession.Update` (no threads). It answers each valid query from the server's current
    state.
    - At most a few replies a second per source address.
    - Bound on the same address as the game: `-bind 127.0.0.1` answers loopback only, `*` binds
      `0.0.0.0`.
    - If the port is taken (another host on this machine, or something else), it logs
      `Net: not visible on the LAN: port 5028 …` and the session goes on.
  - A `NetDiscoveryBrowser`: a socket on an ephemeral port with `EnableBroadcast`, polled by its
    owner. Every 1.5 s it sends the query to:
    - `255.255.255.255`;
    - each up IPv4 interface's subnet broadcast address (`NetworkInterface` unicast addresses and
      `IPv4Mask`; skip loopback and `169.254/16`), because the limited broadcast leaves only by
      the default interface on some systems;
    - `127.0.0.1`, so a host on the same machine is found too.
  - The browser's entries:
    - Keyed by the instance id. Its address is the reply's source address, preferring a
      non-loopback one.
    - Dropped after 5 s without a reply.
    - Sorted by name, then address, and capped at 6.
  - The ports and targets can be injected for the tests and the level check (responder on port 0,
    the browser aimed at it).
- **Compatibility:** the browser marks each entry against this machine's `NetIdentity`, in the
  hello's order: protocol, build, IWAD, PWADs, started, full. Each gives a short reason:
  `ANOTHER BUILD`, `ANOTHER IWAD (DOOM2.WAD)`, `OTHER PWADS`, `STARTED`, `FULL`. Share the
  comparison with `NetServer.Hello` rather than writing it twice.
- **The responder's lifetime:** a host's `NetSession` (command line or menus) runs one from
  `Listen` until the session ends, answering in the lobby and after the Start (the list shows it
  greyed, `STARTED`). No switch to turn it off: it only answers queries from the LAN, and
  `-bind 127.0.0.1` keeps it local.
- **Join Game page** (`MMenu.Net.cs`):
  - The page runs a browser while it shows, created on entering and disposed on leaving (as the
    lobby, the menus own it; `INetHost` gets what it needs).
  - Items: ADDRESS, CONNECT, then a `LOCAL GAMES` heading (not selectable), then one row per entry,
    then the recent addresses.
  - A row reads `NAME  MAP MODE 2/4`, e.g. `JOEP  E1M1 COOP 2/4`.
  - While none has answered, one greyed `SEARCHING...` row shows.
  - Rows update in place as replies come, keeping the skull on the same entry (by instance id)
    where it can.
  - Picking a compatible entry joins `ADDRESS:PORT` as the recent addresses do: it goes into
    `net/addresses` once in a lobby.
  - Picking a greyed entry plays `sfx_oof` and shows the reason as a message back on the page.
  - **Greyed rows:** text pages have no greyed item yet. Add one to `MMenu.Setup.cs`'s text items
    (drawn darker, e.g. through a `COLORMAP` row as T6.13t's sight dim, or at half alpha), and use
    it for the heading and `SEARCHING...` too.
  - Keep the page within the screen at the HUD's scale and at 4:3 (T7.11).
- **The host's addresses on the lobby page:**
  - `HOSTING ON 192.168.1.23:5029`: the first one or two up, non-loopback, non-link-local IPv4
    addresses (the same `NetworkInterface` walk, shared). `127.0.0.1` only when there are none or
    with `-bind 127.0.0.1`.
  - A line `NOT VISIBLE ON THE LAN` when the responder could not bind.
  - The log (`Net: listening on …`) prints the addresses too.
- **Level script:** `lan [N [SECONDS]]` runs a browser, unless Join Game's runs, until it lists
  N entries (default 1; 30 s). It prints each entry (name, address:port, IWAD, map, mode, players,
  phase, compatible or the reason) and fails after SECONDS.
- **Level check** (the multiplayer section, T11.9):
  - Run the check's host with its responder on a port of its own, and Join Game's browser aimed
    at it.
  - Check that the page lists the host by name, compatible, and that picking it with the pad's A
    joins the lobby.
  - Check that the other-IWAD host is listed greyed `ANOTHER IWAD`, and that picking it shows the
    message back on the page.
  - With a renderer, compare the Join Game page with a listed entry, as the other multiplayer
    pages are compared.
- **Loopback script:** `tools/NetLoopback/loopback.sh` gains a `--lan` mode. The client runs
  `lan 1` and joins the address it found instead of `-connect`, so CI checks discovery on the
  exported build, at least on Linux.
  - Broadcast may not reach loopback on CI runners, but the browser's `127.0.0.1` target does.
  - Update `CONTRIBUTING.md` and the PR template if CI changes.
- **Platforms:**
  - **Windows:** the firewall prompt covers the program, so both sockets.
  - **macOS 15+:** asks for *Local Network* access the first time an app sends to the LAN. Check
    whether Godot's macOS export preset has a privacy text for it (`NSLocalNetworkUsageDescription`)
    and set one if so. If not, note it in the player readme.
  - Add both prompts to T11.12's LAN checklist.
- **Player readme:** Join Game lists the games on your network. If yours isn't there (another
  subnet, guest Wi-Fi, a firewall), type the address the host's lobby page shows.

**Tests:**
- `NetDiscoveryTests`, no Godot, on loopback with ephemeral ports:
  - encode and decode round trips;
  - wrong magic, version or size ignored;
  - a reply never bigger than the query, the name truncated to fit;
  - the PWAD hash;
  - a responder answering a browser's query;
  - entries expiring;
  - two responders listed;
  - each compatibility reason;
  - the rate limit;
  - a responder whose port is taken failing softly.
- Don't use the fixed port 5028 in tests: they run in parallel.

**Done when:** a join's Join Game page lists a host on the same machine (the level check and
`loopback.sh --lan`) and on another machine of the LAN (left for a person in T11.12's checklist:
note it there), compatible hosts join with one press, incompatible ones show greyed with their
reason, the host's lobby page shows its LAN address, and all tests pass.
