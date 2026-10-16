# Plan: online multiplayer (M11)

Online co-op and deathmatch for up to four players, each on their own machine, in deterministic
lockstep over ENet. This file breaks the milestone into sessions. **Each session is one task, sized
for one fresh Claude Code instance.** Start one with:

> Do task T11.N from docs/TASKS.md (details in docs/plans/multiplayer.md).

CLAUDE.md's session workflow applies as for every task: read the task and the SPEC sections it
cites, do it, tick it in `docs/TASKS.md` (with a *Done (date):* paragraph and any follow-up tasks),
log the decisions the spec doesn't cover in SPEC §12, add any new command, argument or level-script
command to CLAUDE.md's *Commands* section, and commit on a branch named after the task (e.g.
`t11.1-n-player-world`).

---

## Decisions (the user's, 2026-10-16; logged in SPEC §12 "M11 multiplayer")

| Question | Decision |
|---|---|
| Topology | **Online only.** Each player runs the game on their own machine. Couch co-op (several pads, one screen) is not in this milestone: it may come later as its own plan. Nothing here should rule it out (the sim already takes one `ticcmd` per player, and the presentation's "local player" should be a parameter, not a constant). |
| Modes | **Co-op, deathmatch (1) and altdeath (2)**, with vanilla's rules: weapon stay, keys kept in co-op, all keys in deathmatch, frags, item respawn in altdeath, `-timer`/`-avg`, plus the existing skill, `-nomonsters`, `-respawn`, `-fast` options. |
| Players | **At most 4** (vanilla's `MAXPLAYERS`), so co-op uses the maps' starts 1–4 and vanilla's colours (green, indigo, brown, red). |
| Netcode | **Deterministic lockstep with input delay**, as vanilla and Chocolate Doom do it: tic *t* runs only once every player's `ticcmd` for *t* is in, local input is sent a few tics ahead, and checksums detect desyncs. No prediction and no rollback. (Rollback could be a later milestone on top of this; nothing here may close that off, e.g. `World` must stay snapshot-able through the save format.) |
| Transport | **Godot's ENet** (`ENetConnection`/`ENetPacketPeer`, UDP). The host listens on a port and the others join by `address:port`; over the internet the host forwards the port. No relay or NAT traversal, and no third-party service. |
| Sessions | **A lobby only.** Players gather before the game starts and nobody joins a game in progress. **No saves in a netgame** (the menus already refuse them, as vanilla's). A player who drops out leaves the game as in vanilla's `d_net.c`. |
| Fog of war | **Shared in co-op:** each machine walks the fog from every player and keeps the union, so a teammate's discoveries show for everyone (and on the automap). In deathmatch each machine walks from its own player only. Presentation only. |
| Chat | **None.** Vanilla's `chatchar` stays 0. |
| Vanilla compatibility | **No interop** with vanilla or Chocolate Doom netgames: our own protocol. The *sim* must still match vanilla for multi-player games, and that is tested through multi-player demos (T11.2). |

## What exists (inventory of 2026-10-16)

- **Sim, ported:**
  - `players[MAXPLAYERS=4]` and `playeringame` (`World.cs:150`), and `G_Ticker(ticcmd_t[] netcmds)` (`World.User.cs:31`).
  - `P_SpawnPlayer` for players 2–4 with the `MF_TRANSLATION` bits (`World.Mobj.cs:715`), and the player and deathmatch starts stored (`World.Mobj.cs:774`). `MapThingSpawn`'s NOTSINGLE/NOTDMATCH rules.
  - Frags (`World.Inter.cs:654`), weapon stay (`World.Inter.cs:165`), co-op keys (`World.Inter.cs:391`) and telefrags (`World.Telept.cs`).
  - The netgame paths of `A_Chase`, `lastlook` and the boss "all players dead" check.
  - The item respawn queue is filled (`World.Mobj.cs:656`) but never read.
- **Sim, missing:**
  - `G_DoReborn`'s netgame branch throws (`World.User.cs:86`). `G_CheckSpot`, the body queue and `G_DeathMatchSpawnPlayer` are not ported.
  - `P_SetupLevel` throws in deathmatch (`World.cs:243`).
  - `P_RespawnSpecials` is commented out (`World.Tick.cs:114`).
  - `levelTimer` is never set (`World.Spec.cs:243`).
  - `consistancy` is never computed.
- **Sim reads of `consoleplayer`** (a hard-coded 0, `World.cs:154`):
  - Pickup sounds (`World.Inter.cs:182, 626`), messages, and `wminfo.pnum`/`didsecret` (`World.Game.cs:178, 237, 262`) are presentation in effect, but differ per machine once `consoleplayer` does.
  - `viewz = 1` (`World.cs:251`) and kill credit outside a netgame (`World.Inter.cs:661`, vanilla's) do touch state.
- **Game layer:**
  - The tic loop is `LevelScene.RunTics` (`LevelScene.cs:1469`, a 35 Hz accumulator) → `GameFlow.G_Ticker(in ticcmd_t)` → `World.G_Ticker(cmd)`. It is single-player, and outside `GS_LEVEL` only the console player's command is stored (`GameFlow.cs:302`), though the intermission and finale already loop over every player's buttons.
  - The `ticcmd` builder (`TiccmdBuilder.cs:142`) uses only local data and puts everything into the command: absolute angle, weapon step, save and cheat specials. Cheats apply to `consoleplayer`, not their sender (`GameFlow.cs:494–575`).
  - Per-machine holds: the focus pause (`LevelScene.cs:477`), the menu hold (already off in a netgame) and the wipe (no tics while it melts).
  - The settings menu writes `world.tweaks.AimAssistCone` mid-game (`LevelScene.Settings.cs:585`), which a lockstep game must not do. A load overrides the saved cone too (`LevelScene.SaveGame.cs:126`).
- **Presentation:**
  - Everything is anchored on `PlayerMobj` (`LevelScene.cs:575`): the camera, cutaway, fog, sight dim, monster markers, glow, palette flashes, HUD, message filter, tracers, aim marker, sound listener and intermission.
  - Other players' mobjs are drawn as generic things **without colour translation**. Translation is missing from the renderer and shaders.
  - The automap's netgame player arrows and the status bar's frags/`STFB` are ported. The intermission's netgame screens are not (`WiStuff.cs:10, 500`).
- **Checksum** (`World.Checksum.cs`): mobjs, sectors, `prndindex` and per-player health, armor and weapons. It leaves out frags, cards, powers, `weaponowned`, the kill/item/secret counts and the item queue.
- **Networking:** none.

## Architecture (target)

```
 machine k                                                      host (also a player)
┌────────────────────────────────────────────┐                 ┌──────────────────────────┐
│ TiccmdBuilder ─ local cmd for tic t+delay ─┼──── ENet ──────▶│ NetServer: collects each │
│                                            │                 │ player's cmd for tic t,  │
│ NetClient ◀─ bundle {tic t: cmd[0..3]} ────┼◀─── ENet ───────┤ broadcasts the bundle    │
│     │                                      │                 │ (star, relayed)          │
│     ▼                                      │                 └──────────────────────────┘
│ GameFlow.G_Ticker(ticcmd_t[]) ─ World      │   every 35 tics each client sends World.Checksum();
│ presentation: local player = consoleplayer │   the host compares and stops the game on a desync
└────────────────────────────────────────────┘
```

- **`src/Net`** (new, plain `Microsoft.NET.Sdk` library like `src/Sim`, no Godot types, unit-tested in `tests/IsoDoom.Tests`):
  - The protocol (messages, serialization), the lobby state machine, the lockstep scheduler (`NetClient`, `NetServer`) and an `INetTransport` interface.
  - An in-memory transport with configurable latency, jitter, loss and reordering, for tests.
  - It references `IsoDoom.Sim` only for `ticcmd_t` and the settings types. It is **not** under the sim's determinism scan, but it must never feed anything into the sim except the commands. The deviation-free rule stays: the sim sees nothing but `ticcmd_t[]`.
- **`src/Game/Net*`:** the ENet transport (Godot's `ENetConnection`), the command-line arguments, the lobby menus and the `LevelScene`/`GameFlow` glue. Godot's lifetime rule (CLAUDE.md invariants) applies to every `Variant`/packet the ENet calls return.
- **Star topology through the host**, as Chocolate Doom's `net_server.c`: clients send their commands to the host, and the host sends each tic's full bundle to everyone. Use a reliable, ordered ENet channel for the lobby and the bundles; lockstep needs every command anyway. A second, unreliable channel is optional for pings.
- **Input delay:** `-netdelay N` tics. The default comes from measured round-trip time at the game's start, clamped to 1–8 (T11.6 decides; log it). A local cosmetic, the cursor and aim marker, may follow the mouse at once, but the player mobj moves only through the sim.
- **Clock:** each client makes one command per 35 Hz tic of its own clock, and the host paces the bundles. A client too far ahead or behind adjusts its tic rate a little (Chocolate Doom's `net_client.c` lag adjustment). When a bundle is late, the world holds and presentation keeps drawing the last state (no interpolation past it).
- **The game's settings travel in the lobby's start message:**
  - the IWAD's MD5 and the PWADs' (in order), protocol version and build version stamp;
  - `SpawnSettings` (mode, skill, netgame, deathmatch, nomonsters, respawn, fast, gameversion), episode and map;
  - the `Tweaks` (which every player must share: `Tweaks.cs:4`), the timer, the player slots, and `consoleplayer` per client.

  A mismatch in WADs or versions refuses the join with a message.

## Dependency graph and parallel lanes

```
Lane A (sim)          T11.1 ──▶ T11.2 ──▶ T11.3 ──┐
                                   └────▶ T11.4 ──┤
Lane B (net)          T11.5 ──▶ T11.6 ────────────┼──▶ T11.8 ──▶ T11.9 ──▶ T11.12 ──▶ T11.13
Lane C (presentation) T11.1 ──▶ T11.10 ───────────┤        └────▶ T11.11 ──┘
                                T11.2 ──▶ T11.7 ──┘
```

- **Start in parallel:** T11.1 and T11.5. Neither depends on the other.
- **After T11.1:** T11.2 and T11.10 can run in parallel.
- **After T11.2:** T11.3, T11.4 and T11.7 can run in parallel.
- **After T11.5:** T11.6.
- **T11.8 integration** needs T11.1 and T11.6. It does not need T11.3 or T11.4: co-op without deaths runs before them.
- **When sessions run at once,** each branches from the latest `main` that holds its dependencies. `docs/TASKS.md` and `SPEC.md` §12 will conflict at the end of the file: append, and resolve by keeping both entries.

---

## T11.1 N-player world and the local player

**Cites:** SPEC §6.1 (determinism rules), §12 T3.2 (spawn settings). This plan's *What exists*.

**Do:**
- **Players in the game come from the settings.**
  - Add the players to the game's settings: a `playeringame` mask in `SpawnSettings`, or a sibling record, whichever fits; log it.
  - `World`'s constructor sets `playeringame` from that mask instead of `consoleplayer` alone (`World.cs:54`).
  - Saves already store `playeringame`. Keep the format readable (bump `SaveGameFile.VERSION` if needed).
- **`consoleplayer` becomes a per-machine view index that the sim never uses for state.**
  - Make it a settable property of `World` (default 0), not saved and not checksummed.
  - Go through every sim read of it:
    - **Presentation in effect** (pickup sounds, messages, the turbo and `mypos` messages): keep them vanilla's, but make sure nothing they do reaches the checksum or the state other machines see.
    - **Vanilla state per machine** (`G_WorldDone`'s `didsecret`, `wminfo.pnum`): keep them vanilla's and comment that they are per machine, as in vanilla. They only choose the intermission's view and Doom II's secret text. Check that nothing else reads them.
    - **`G_DoLoadLevel`'s `viewz = 1`** (`World.cs:251`): prove `P_CalcHeight` overwrites it before any checksum, or else apply it to every player in game (and log the deviation behind no tweak: it is not observable). Add a test where two worlds with different `consoleplayer` stay checksum-equal.
  - Cheats are applied to their **sender** (`ST_Cheat(player, …)` from the player whose `ticcmd` carries `BTS_CHEAT`, `GameFlow.cs:494–575`), as vanilla's `G_Ticker` does for its specials. Cheats stay refused in a netgame (`World.Cheat.cs:72`), so this matters for correctness only.
- **`GameFlow.G_Ticker` takes `ticcmd_t[]`.**
  - Store every in-game player's command in every game state (`GameFlow.cs:302`), so the intermission and finale skips (`WiStuff.cs:451`, `FFinale.cs:106`) see every player.
  - Keep the single-command overload as a wrapper for the single-player path.
  - The specials (`BTS_PAUSE`, `BTS_SAVEGAME`, cheats) are read per player as vanilla's `G_Ticker` does.
- **Widen `World.Checksum()`:** frags, cards, powers, `weaponowned`, `ammo`/`maxammo`, `killcount`/`itemcount`/`secretcount`, `iquehead`/`iquetail`.
  - This changes every committed checksum: regenerate `Routes/checksums.txt` (`ISODOOM_UPDATE_CHECKSUMS=1`, CLAUDE.md) in this commit, and say so in the commit message.
  - First verify that only the hash's *coverage* changed: the routes must still match vanilla tic by tic.
- **A test harness for N players without a network.**
  - `tests/IsoDoom.Tests/Support/NetSim.cs`: builds K worlds of the same game, each with a different `consoleplayer`, and feeds all of them the same `ticcmd_t[]` per tic from scripted per-player inputs.
  - It asserts equal checksums every tic.
  - First tests, on the synthetic IWAD (CI) and DOOM1 when present: two and four players walking E1M1 from their co-op starts, picking up items, opening doors and shooting monsters, with every world in sync.
  - The single-player route checksums must not move beyond the widening above.

**Done when:** a world can be started with players 1–4 in game. K worlds with different `consoleplayer` stay checksum-equal over scripted co-op play. The single-player routes and demos still sync with vanilla. All tests pass.

---

## T11.2 Multi-player routes and demos against vanilla

**Cites:** CLAUDE.md *Vanilla movement routes*, *Vanilla demos*. SPEC §12 T4.8, T6.13.

Vanilla plays multi-player demos (`G_DoPlayDemo`: `playeringame[1..3]` in the header make it a `netgame`/`netdemo`, with the deathmatch, respawn, fast and nomonsters bytes). This is our check that the sim's netgame rules are vanilla's.

**Do:**
- **`RouteFile`:**
  - Accept multi-player demos (`FromDemo` rejects them today: `RouteFile.cs:212`).
  - Accept a multi-player route format: a `players N` header, a `deathmatch 0|1|2` header and per-tic lines with one `ticcmd` per player. Keep the single-player format as it is.
  - Route events (`damage`, `alert`, `rocket`) name their player, defaulting to the first.
- **`tools/VanillaRef`:**
  - Play these as v1.9 netdemos.
  - Extend `dump.c`'s per-tic line to every in-game player: position, health, armor, weapons, frags, cards, psprites. Keep the single-player dump format byte for byte, so existing dumps stay valid. Vanilla's `consoleplayer` is 0 in a demo; the dump does not depend on it.
- **`VanillaRouteTests` and `DemoSyncTests`** compare these tic by tic.
- **First routes:**
  - Co-op on DOOM1 E1M1 with two and four players (`coop-e1m1-2p`, `coop-e1m1-4p`: generated by RouteGen with each player steered separately, or written by a small script).
  - A synthetic IWAD one with a committed dump for CI.
  - Deathmatch routes wait for T11.4 (the sim throws in deathmatch today), but add the format's support now.
- **`RouteGen`:** add `--players N` (each extra player stands at its start or follows a simple steering script) if that is cheap. Otherwise write these routes by hand-steering and log it.

**Done when:** a two- and a four-player co-op route on DOOM1 E1M1 (and one on the synthetic IWAD, in CI) match vanilla tic by tic, with every player's state dumped.

---

## T11.3 Co-op reborn

**Cites:** g_game.c `G_DoReborn`, `G_CheckSpot`, `G_QueueBody` (`bodyque`, `BODYQUESIZE` 32), p_mobj.c `P_SpawnPlayer`. This plan's *What exists*.

**Do:**
- Port `G_DoReborn`'s netgame branch (`World.User.cs:86`):
  - The dead player's body stays (`G_QueueBody`, the body queue's oldest removed past 32).
  - The player reborns at its own start if `G_CheckSpot` allows, else at any free co-op start, else forced at its own (vanilla's order).
  - Keep `G_CheckSpot`'s fog and sound at the spot (the 1.9 `ss->sector` angle quirk as vanilla's; check Chocolate Doom's notes).
  - The level does not reload: `LevelScene.Reborn`'s reload stays the single-player path only (`LevelScene.cs:2062`, `GameFlow.cs:815`).
- **Presentation:** under the game camera, a reborn in a netgame snaps the local camera to the new mobj, and the fog keeps its discovered set.
- **Save format:** the body queue is saved (saves are refused in a netgame, but keep the world snapshot whole for a later rollback or drop-in).
- **Routes against vanilla (T11.2's format):**
  - co-op on E1M1 where player 2 dies to monsters and reborns at its start (`coop-e1m1-reborn`);
  - one where its start is blocked by another player, so it uses another start;
  - one with more than 32 deaths, if cheap (a `damage` event on a player).

**Done when:** co-op reborn routes match vanilla tic by tic, and the level scene reborns a co-op player without reloading the level (checked in `--level-check` or a level script with the N-player harness of T11.8; until then, an xunit test on the world).

---

## T11.4 Deathmatch and altdeath

**Cites:** p_setup.c (deathmatch start), g_game.c `G_DeathMatchSpawnPlayer`, p_mobj.c `P_RespawnSpecials`, p_spec.c `levelTimer` (`-timer`, `-avg`), `MF_NOTDMATCH`.

**Do:**
- **`P_SetupLevel` in deathmatch** (`World.cs:243`): spawn each in-game player at a random deathmatch start with `G_DeathMatchSpawnPlayer` (20 tries of `P_Random % starts` with `G_CheckSpot`, then forced). Reborn uses the same.
- **Altdeath:** `P_RespawnSpecials` (`World.Tick.cs:114`) reads the item queue (30 s, `MT_IFOG` and the item respawned, `MF_SPAWNCEILING` handled).
- **`-timer N` and `-avg`** set `levelTimer` and `levelTimeCount` (the exit when it runs out, `World.Spec.cs:814`), as settings of the game.
- **Settings and arguments:**
  - `SpawnSettings` (or T11.1's record) carries deathmatch, respawn, fast and the timer.
  - Add vanilla's arguments for a local test game: `-deathmatch`, `-altdeath`, `-timer N`, `-avg`, `-respawn`, `-fast` (with `--level`).
  - With one player a deathmatch game runs, as vanilla's `-deathmatch` alone does. That is handy for tests and screenshots.
- **Routes against vanilla:**
  - deathmatch on E1M1 with two players fragging each other (`dm-e1m1-2p`: spawns, frags, reborn at DM starts, weapon stay, all keys);
  - altdeath with an item respawning (`dm2-e1m1-respawn`);
  - a `-timer 1` exit.

**Done when:** these routes match vanilla tic by tic, and `--level E1M1 -deathmatch` plays.

---

## T11.5 The lockstep protocol (no transport)

**Cites:** this plan's *Architecture*. Chocolate Doom `net_client.c`, `net_server.c`, `net_structs.h` (GPL-2.0: port the ideas freely, keep its names where you port code, and cite the files).

**Do:**
- **Create `src/Net`** (`IsoDoom.Net.csproj`, added to `IsoDoom.sln`, `.gdignore`, the shared `Directory.Build.props`, `.editorconfig`'s rules). Plain .NET, no Godot.
- **`INetTransport`:**
  - connect/listen, send reliable or unreliable bytes to a peer, poll events (connected, data, disconnected);
  - plus `MemoryTransport` for tests, with latency, jitter, loss on the unreliable channel, and delivery order kept on the reliable one.
- **Messages,** with a compact binary serialization and a protocol version:
  - lobby: hello with versions and WAD hashes, slot assignment, settings, ready, start;
  - tics: a client's commands for tics *t..t+k*, and the host's bundles for tics *t..t+k* (resent while not acknowledged, if you use the unreliable channel; decide and log);
  - checksum reports, quit, ping.
- **`NetServer`:**
  - the lobby (slots 0–3, the host's own client in slot 0);
  - the game start, carrying the settings, `consoleplayer` per client and the start tic;
  - gathering commands, broadcasting bundles in tic order, comparing checksum reports (every 35 tics: `World.Checksum()` and vanilla's `consistancy`, `players[i].mo->x`, put into each command as vanilla's `G_BuildTiccmd` does), and announcing a desync with the first tic window that differs.
- **`NetClient`:**
  - sends its command for tic *t + delay* each local tic;
  - hands the game the next complete bundle when it is due;
  - reports how many tics it can run now (0 while waiting);
  - adjusts its local tic rate when it drifts (Chocolate's lag adjustment).
- **Tests** with `MemoryTransport` and T11.1's `NetSim` worlds:
  - 2–4 clients play scripted inputs over 3500 tics with 0–200 ms latency, jitter and loss;
  - every world stays checksum-equal;
  - nobody runs a tic before its bundle;
  - throughput stays bounded;
  - a forced desync (one world's `P_Random` index bumped) is reported at the right window;
  - a version or WAD mismatch is refused at hello.

**Done when:** these tests pass in CI, and the protocol is documented in a comment block at the top of the message file (and summarized in SPEC §12).

---

## T11.6 ENet transport and command line

**Cites:** Godot 4.7 `ENetConnection`, `ENetPacketPeer`. CLAUDE.md invariants (Godot lifetimes). T11.5.

**Do:**
- **`src/Game/NetEnetTransport.cs`:** `INetTransport` over Godot's `ENetConnection`.
  - Two channels: reliable ordered, and unreliable.
  - Polled once a frame from the main thread, or from a dedicated thread if a frame hitch would stall the tics; decide and log it.
  - Every packet copied out to `byte[]` at once, and every Godot object it returns disposed or held, per the lifetime rule. Extend `GodotLifetimeTests` if it doesn't already cover the new file.
- **Arguments** (Chocolate Doom's names where they exist):
  - `-server` or `-host [PORT]` to host; `-connect ADDR[:PORT]` to join (default port 5029, the Doom netgame port, unless taken by something; log it);
  - `-players N` (the host waits for N before starting), `-netdelay N`, and T11.4's mode arguments for the host.
  - A headless host that only relays is **not** in scope (the host is a player).
- **A loopback smoke test** runs the game twice headless on one machine with **no lobby UI yet**:
  - The host starts with `-host -players 2 --level E1M1` and the client with `-connect 127.0.0.1`; both play scripted input.
  - The game loop glue is T11.8's, so here the smoke test may drive `NetClient`/`NetServer` from a level-script command (`net`) to exchange a few hundred tics of commands without running them.
  - Add it to `tools/` as a script, and to CI if it runs in under a minute.

**Done when:** two headless game processes connect over ENet on loopback, finish the lobby handshake and exchange command bundles for 1000 tics with the same checksums of the exchanged data. The script is in CI, or the reason it isn't is logged.

---

## T11.7 Netgame intermission

**Cites:** wi_stuff.c `WI_initNetgameStats`/`WI_drawNetgameStats`, `WI_initDeathmatchStats`/`WI_drawDeathmatchStats`, the `STPB*` colour backgrounds. CLAUDE.md *Vanilla intermissions*.

**Do:**
- Port the co-op stats screen (kills, items, secrets, frags with `-deathmatch` 0 in a netgame) and the deathmatch frag table, including their counters, sounds and `M_Random` use (`WiStuff.cs:10, 500`).
- The local player is highlighted by `consoleplayer` (per machine, as vanilla's: not sim state).
- Extend `tools/VanillaRef/intermissions.sh` and `IntermissionTests` to T11.2's multi-player routes (co-op and, once T11.4 is in, deathmatch exits), compared tic by tic and pixel by pixel as the single-player intermission is.

**Done when:** the co-op and deathmatch intermissions match vanilla's dumps for a two- and a four-player route. (The deathmatch half needs T11.4: if it is not merged yet, finish co-op and leave the deathmatch half as a follow-up task.)

---

## T11.8 Lockstep game loop

**Cites:** this plan's *Architecture*. CLAUDE.md *Level scene*, *Game states*. T11.1, T11.6.

**Do:**
- **`LevelScene.RunTics`/`GameFlow`:** in a netgame the tics come from `NetClient`, not the 35 Hz accumulator. Each local tic builds a command (`TiccmdBuilder`, as now) and sends it. The scene runs every bundle that is due, at most `MaxTicsPerFrame` a frame.
  - While waiting, it holds and draws the last tic. Interpolation clamps at the last tic, with no extrapolation.
  - Single-player keeps its loop unchanged: route checksums and the level check must not move.
- **Per-machine holds become game-wide or go away in a netgame:**
  - the focus pause is off;
  - the menu does not hold the world (already, `GameFlow.cs:392`);
  - Pause sends `BTS_PAUSE`, which pauses everyone, as vanilla's;
  - the wipe: no tics while the local melt runs, as now. The others' tics wait in the queue, and lockstep absorbs it. Make sure the melt cannot stall the protocol's keep-alive.
- **Settings that are sim state are locked in a netgame:**
  - The aim assist cone (`LevelScene.Settings.cs:585`) comes from the host's game settings, and the menu item shows it greyed or refuses.
  - Any other `Tweaks` write gets the same treatment.
- **`consoleplayer` set from the slot.** Each machine's presentation follows its own player.
  - This is enough to *play*: T11.10 does the rest of the presentation.
  - The camera follow and HUD must at least use `consoleplayer`, not player 0.
- **Level-script commands for tests:**
  - `net` prints the slot, players, delay, ping, tics waiting and the last checksum exchange;
  - `netwait N` waits until N players are in;
  - the `cmd`/`step`/`checksum` commands keep working per machine, feeding the local player's commands.
- **Maps without enough co-op starts are refused (T11.1a, the user's decision):** the host does not start a co-op game on a map that lacks a co-op start for a player in game; the refusal is a message naming the map and the missing starts (T11.9 shows it on the lobby page). No spawning elsewhere. Settle and log whether the later maps the game can reach are checked too.
- **A two-process loopback test** (T11.6's script, now playing): host and client on E1M1 with monsters at skill 4.
  - The client walks to the exit with a route (the route's commands as its local input), and the host stands still or fires.
  - Both reach the intermission and the next map, and their checksums agree at every exchange.
  - In CI if under a minute.

**Done when:**
- two game processes play E1M1 together in lockstep, headless on loopback, through the intermission into E1M2, with no desync;
- by hand, two windows on one machine are playable: each player is moved by its own window's input and seen in the other;
- single-player is unchanged.

---

## T11.9 Multiplayer menus and lobby

**Cites:** CLAUDE.md *Game scene* (the menus, `MMenu.Setup.cs` text pages), T7.2/T7.3. T11.8.

**Do:**
- **A main-menu entry, *Multiplayer*,** reached from the title loop. It is a page in the menu font, like More Options. It has:
  - **Host Game:** mode (co-op, deathmatch, altdeath), episode/map, skill, monsters, respawn, fast, timer, port, number of players. Each starts at its last value: settings keys `net/*`.
  - **Join Game:** an address field typed with the keyboard, or picked from the last few addresses saved in settings. A pad can pick saved addresses.
- **The lobby page** shows the players joined (slot, colour, ping), the WAD and settings, and *Start* for the host once at least two are in. A client waits.
  - Escape leaves the lobby.
  - Refusals (version, WAD, full, wrong password: no passwords, unless trivial; log it) are shown as menu messages.
- The command-line arguments of T11.6 still work and skip the menus.
- Level-script commands (`tap …`, `menu NAME`) reach the new pages. `menu` knows their names (`multiplayer`, `host`, `join`, `lobby`).
- **Level check:** the pages are reached and worked through the input glue. A loopback host and join run in one process, with two `NetClient`s over `MemoryTransport` or ENet loopback, whichever the check can do.
- Update `docs/player-readme.txt` with how to host, port forwarding and how to join.

**Done when:** two people (or two windows) can host and join from the menus only, and start a co-op and a deathmatch game.

---

## T11.10 Presentation for several players

**Cites:** r_draw.c `R_InitTranslationTables`/`R_DrawTranslatedColumn` (the 0x70–0x7F green ramp remapped to grey, brown and red for players 2–4), st_stuff.c `STFB`, s_sound.c's listener. SPEC §12 T6.13l (fog), T6.13t (sight dim), T6.13u (markers). T11.1.

Can start after T11.1. It tests with T11.1's harness worlds in the level scene, using a debug argument such as `--level-players=N` that puts N players in game with idle commands for the others. Until T11.8 there is no network.

**Do:**
- **Colour translation** in the sprite shader (`shaders/sprite.gdshaderinc`) for `MF_TRANSLATION`: vanilla's three tables as a small lookup texture.
  - It applies to players' mobjs and their corpses, the status bar's face background (`STFB0–3`), the automap arrows (already coloured) and T11.7's `STPB*`.
  - The level check compares every pixel of each colour with the CPU's translation.
- **The "local player" is a parameter everywhere** (`PlayerMobj`, `LevelScene.cs:575`, reading `consoleplayer`):
  - camera follow, cutaway centre and height reference, the player light (own sprite), palette flashes, glow, tracers (yours only, plus the others' when `--level-tracers=all`), aim marker, cursor ground point, HUD, message filter, sound listener;
  - the sight dim and monster markers use the local player's sight;
  - item beams stay global.
- **Other players' sprites:**
  - drawn as actors (the cutaway keeps them whole, as monsters);
  - the fog draws them like monsters in co-op;
  - always drawn in co-op; in deathmatch, as monsters under `--level-fog-things`.
- **Shared fog in co-op:** `FogOfWar.See` walks from every live player in game each tic, into one discovered set. Watch the walk's cost with four players: the overlay line already times it, and SPEC §9's budget applies. In deathmatch, only the local player.
- **Sounds:** the sim's pickup sounds are only for the console player, as vanilla's (`World.Inter.cs:182`), so with T11.1's per-machine `consoleplayer` each machine hears its own pickups, as vanilla's. Others' weapon and pain sounds are positional already: check they play from their mobjs.
- **HUD:** the frags widget in deathmatch (ported: check it with real frags), and the netgame face background. The automap shows the other players' arrows, as vanilla's rules say (co-op only, unless `iddt`).
- **Optional, behind an option:** a small coloured ring or name tag under co-op teammates, to tell them apart under the iso camera. Ask the user before adding it, and log the answer.

**Done when:** in a 4-player harness game (no network), each player shows in its colour, every presentation feature follows the chosen local player (`--level-players=4` plus a debug key or argument to pick the local one), the shared fog unions all four, and the level check covers the translations and the local-player switch.

---

## T11.11 Drop-outs, desyncs and lag

**Cites:** d_net.c (a player quitting: `playeringame` false, "player N left the game"), Chocolate Doom's `net_client.c` (timeouts, lag adjustment). T11.8.

**Do:**
- **A client quits or times out** (ENet's disconnect, or no command for N seconds, configurable; log the default):
  - The host sends a quit for that slot at a tic. Every machine sets `playeringame` false at that tic, as vanilla's `d_net.c` does (the mobj stays; check what vanilla does with it and match it).
  - The message shows on the message line. The game goes on.
- **The host quits:** every client ends the game with a message, back to the title loop.
- **A desync:**
  - Every machine stops at the reported window with a message naming the first tic window.
  - Each writes a dump of its per-tic checksums and the last 35 tics' commands, plus a save of the world at the last agreed checksum (the save format), to `user://netdesync/`, for debugging. Say where in the log.
  - Add a level-script or debug-only way to force one (bump `P_Random`'s index), checked in the two-process test.
- **Lag:**
  - The overlay (F3) gets a `net:` line: slot, ping per player, delay, tics waiting, stalls in the last second.
  - When a bundle is more than a second late, a "waiting for player N" message shows over the view (vanilla shows nothing; log it).
- **Tests** with `MemoryTransport`: a client vanishes mid-game; the host vanishes; packet loss bursts; a long stall (5 s) then catching up without a desync. Plus T11.8's two-process script with a client killed mid-game.

**Done when:** these cases behave as above in the tests and in the two-process script.

---

## T11.12 Cross-platform and internet check

**Cites:** CLAUDE.md *Export*, *CI*. T11.9, T11.11.

**Do:**
- **The exports join each other:** Linux ↔ Windows ↔ macOS, on a LAN and over the internet with a forwarded port.
  - The sim is already deterministic across platforms by construction (fixed point). This checks it in practice.
  - Add a CI job pairing the Linux and Windows builds headless across runners, if GitHub Actions allows connections between jobs. If it doesn't, run a Windows host against a Linux client in one job on one runner with Wine, or log why not.
- **Firewall prompts** (Windows Defender on first host, the macOS incoming connection dialog): document them in `docs/player-readme.txt`.
- **Measure** the bandwidth per player (expected: well under 10 KB/s) and the input delay felt at 30, 80 and 150 ms ping. Log the numbers and the default delay rule in SPEC §12.

**Done when:** a Linux, a Windows and a macOS build have played a co-op map together over the internet, measured and logged. Needs people and machines; if an agent's session can't, do what it can (CI pairing, measurements on `MemoryTransport`) and leave the rest as a checklist in the task, as T10.6 does.

---

## T11.13 Multiplayer acceptance

**Cites:** SPEC §11 (M11's exit criterion), §13.

**Do:**
- People play, over the internet:
  - co-op E1 start to end with 2 and with 4 players (DOOM1), and some Doom II maps;
  - deathmatch and altdeath on E1M1–E1M3 and Doom II's MAP01 and MAP07, with `-timer`;
  - with pads and keyboard/mouse, and one player on a Steam Deck.
- Log what reads badly under the iso camera with several players: the camera only follows you, so teammates or opponents off-screen get no cue; the cutaway is only around you; the fog of war in deathmatch. Turn each into a follow-up task (e.g. off-screen teammate arrows at the view's edge, like the brain marker of T10.4).
- Update SPEC §1 (multiplayer leaves the non-goals), §11 (M11 done) and CLAUDE.md's *Commands*.

**Done when:** M11 is marked done in TASKS.md with the people's outcome logged, and follow-ups filed.

---

## Out of scope (possible later plans)

- Couch co-op on one screen (the user's "maybe later").
- Rollback netcode, and joining a game in progress (both would build on the save format as a world snapshot).
- Chat, a relay server or NAT traversal, a server browser, Steam networking.
- More than 4 players, and vanilla or Chocolate Doom network interop.
