# IsoDoom — Specification

An isometric, top-down remake of the classic *Doom*. It uses the original game data loaded at runtime from the player's own IWAD, much as [Isowulf](https://www.moddb.com/mods/isowulf) does for Wolfenstein 3D.

Status: draft v0.1 (2026-10-05)

---

## 1. Goals

- Play the original Doom maps from a fixed isometric camera, Diablo/Isowulf style.
- Use **only** the original assets (maps, textures, flats, sprites, sounds, music, palette, UI graphics), read from the WAD at runtime. No WAD content is shipped or converted ahead of time.
- Keep the original game's feel: same monsters, weapons, damage, AI behaviour, and map specials (doors, lifts, crushers, switches, teleporters, secrets, keys). Change things only where the camera makes it necessary (see §6.3).
- Use twin-stick controls: move with WASD, aim with the mouse.

### Non-goals (for v1)

- Shipping multiplayer or co-op in v1. Co-op is a **planned later goal**, though, so the sim is built deterministic from day one (see §6.1). If that becomes a significant burden during development, the decision will be revisited.
- Boom, MBF, UDMF, ZDoom or other extended map formats. Vanilla format only.
- Exact demo (.lmp) compatibility with vanilla Doom.
- Source-port extras such as jumping, crouching or freelook.

## 2. Target content (phased)

| Phase | IWAD | Notes |
|---|---|---|
| 1 | `DOOM1.WAD` (shareware, E1M1–E1M9) | Development baseline |
| 2 | `DOOM.WAD` (registered / Ultimate Doom, E1–E4) | Adds episode select, E2–E4 bosses, the Spider Mastermind and Cyberdemon |
| 3 | `DOOM2.WAD` (and later `TNT.WAD`, `PLUTONIA.WAD`) | MAPxx naming, Super Shotgun, new monsters, Icon of Sin, Doom II texture and intermission differences |
| Later | Vanilla-format PWADs, Freedoom | Load order IWAD + PWADs |

The game identifies the IWAD from its lumps (`E1M1` without `E2M1` means shareware, `MAP01` means Doom II, and so on) and enables the matching game mode, in the same way vanilla's `IdentifyVersion` does.

## 3. Platform & technology

- **Engine:** Godot 4.x, Forward+ (Compatibility renderer as a fallback).
- **Language:** C# (.NET 10, LTS) is the recommendation. Porting C game logic, doing fixed-point maths and parsing binary WADs are much more comfortable and faster in C# than in GDScript. GDScript stays available for UI and glue code. *Trade-off:* Godot 4 C# projects cannot currently export to the web (see §12).
- **Targets:** desktop only, with **Linux x86_64 as a first-class target, including the Steam Deck**. Windows is also supported, and macOS is best-effort. There is no web export.
  - Steam Deck: runs as a native Linux build (no Proton needed). Designed for 1280×800 (16:10). The UI and HUD are fully gamepad-navigable, and text and HUD are readable on a 7" screen.
  - The CI builds and smoke-tests the Linux export on every change.
- **License:** the gameplay simulation is ported from the id Software Doom source (GPL-2.0), so the project is **GPL-2.0-or-later**. Only code is distributed, never WAD data.
- **Shareware download:** if no IWAD is found, the game offers to download the shareware `DOOM1.WAD` v1.9 (the shareware license allows unmodified redistribution), as Isowulf does. It is verified by hash before use. This is low priority: if it turns out to be costly (for example, the official `doom19s.zip` holds a DOS self-extracting archive that has to be unpacked), it moves to after M8, and a "browse for WAD" dialog is used in the meantime.

## 4. Architecture overview

```
 ┌──────────────┐   ┌───────────────────┐   ┌────────────────────┐
 │  WAD layer   │──▶│  Asset decoders   │──▶│  Godot resources   │
 │ (lump dir,   │   │ palette, patches, │   │ Textures, atlases, │
 │  PWAD merge) │   │ flats, sprites,   │   │ AudioStreams,      │
 └──────────────┘   │ sounds, MUS→MIDI, │   │ materials          │
                    │ map lumps         │   └─────────┬──────────┘
                    └─────────┬─────────┘             │
                              ▼                       ▼
                    ┌───────────────────┐   ┌────────────────────┐
                    │  Simulation       │──▶│  Presentation      │
                    │  35 Hz tic loop,  │   │  Level mesh, sprite│
                    │  fixed-point,     │   │  billboards, iso   │
                    │  port of p_*.c    │   │  camera, cutaway,  │
                    │  (pure C#, no     │   │  HUD, menus, audio │
                    │   Godot types)    │   │  (interpolated)    │
                    └───────────────────┘   └────────────────────┘
```

Key rule: **the simulation does not depend on Godot.** It is a plain C# library that takes input commands (`ticcmd`) and produces world state. The presentation layer reads that state each frame and interpolates between tics. This keeps the sim testable and deterministic, and leaves room for networking later.

### 4.1 Proposed project layout

```
isodoom/
  project.godot
  src/
    Wad/           # WAD reader, lump directory, IWAD detection
    Assets/        # palette, colormap, patch/flat/texture composition, sprites, sounds, music
    Sim/           # deterministic game logic (port of p_*, info.c, m_random, tables)
    Map/           # map lump parsing, BSP, blockmap, reject, sector polygon building
    Render/        # level mesh builder, materials/shaders, sprites, camera, cutaway
    Game/          # game flow: menus, intermission, finale, save/load, options
    Audio/
  shaders/
  scenes/
  tests/           # GdUnit4 / xUnit tests for Wad, Map and Sim
  SPEC.md
```

## 5. WAD & asset handling

### 5.1 WAD layer
- Parse the IWAD and PWAD headers and lump directories. Later PWADs override lumps by name. Namespace markers (`S_START/S_END`, `F_START/F_END`, `P_START/P_END`, including the `SS_`/`FF_` variants) are respected.
- The IWAD is located through a configurable path, the game folder, or common install locations (Steam/GOG). If none is found, a file picker is shown (or the optional shareware download).

### 5.2 Decoders
| Data | Lumps | Output |
|---|---|---|
| Palette | `PLAYPAL` (14 palettes) | Palette texture. Palettes 1–13 drive the damage, pickup and radsuit screen tints |
| Light | `COLORMAP` (34 maps) | Colormap texture for the palette shader (see §7.4) |
| Wall textures | `TEXTURE1`/`TEXTURE2` + `PNAMES` + patches | Composite textures, stored as 8-bit palette indices |
| Flats | `F_START..F_END` | 64×64 index textures. Animated flats (`NUKAGE`, `FWATER`, …) come from the hardcoded animation table |
| Sprites | `S_START..S_END` | Frames with rotations 0–8 and mirrored pairs (e.g. `TROOA2A8`), plus offsets |
| UI graphics | `STBAR`, `STTNUM*`, `M_*`, `WI*`, `TITLEPIC`, `CREDIT`, `HELP*`, fonts `STCFN*` | Textures for the HUD, menus and intermission |
| Sounds | `DS*` (DMX format, 8-bit, usually 11025 Hz) | `AudioStreamWAV` |
| Music | `D_*` (MUS format) + `GENMIDI` (OPL instrument bank) | Played through OPL3 emulation (see §7.7) |
| Maps | `THINGS, LINEDEFS, SIDEDEFS, VERTEXES, SEGS, SSECTORS, NODES, SECTORS, REJECT, BLOCKMAP` | Map data structures |

All indexed graphics stay **8-bit indexed** on the GPU (R8 textures). The palette is applied in the shader. This allows authentic light diminishing through `COLORMAP`, palette flashes, and the invulnerability colormap.

## 6. Gameplay

### 6.1 Simulation core
- A fixed **35 Hz** tic loop. Rendering runs at the display refresh rate and interpolates actor positions between tics.
- **Fixed-point 16.16** arithmetic, the original `finesine`/`finetangent` tables, and the original `rndtable` (`P_Random`/`M_Random`). This gives faithful behaviour and determinism.
- Port of the relevant parts of the original source:
  - `info.c` (state, mobjinfo and sprite tables). This is the data-driven heart of monsters, weapons and items.
  - `p_mobj`, `p_map`, `p_maputl` (movement, collision, blockmap, line-of-sight via `REJECT` and `P_CheckSight`).
  - `p_enemy` (monster AI and action functions), `p_pspr` (weapon states and firing), `p_inter` (damage and pickups).
  - `p_spec`, `p_doors`, `p_plats`, `p_floor`, `p_ceilng`, `p_lights`, `p_switch`, `p_telept` (map specials).
  - `p_user` (player movement, adapted for twin-stick; see §6.3).
- **Determinism rules** (these keep lockstep co-op possible later):
  - No `float`/`double` anywhere in `Sim/`. All positions, angles, speeds and momentum use fixed point or BAM angles, as in vanilla.
  - The only input to the sim is one `ticcmd` per player per tic. The absolute aim angle is quantised to a 16-bit angle before it enters the `ticcmd`.
  - No wall-clock time, no `System.Random`, and no iteration over unordered collections (`Dictionary`/`HashSet` ordering) in the sim. Thinkers are kept in a linked list, as in vanilla.
  - The sim supports N players from the start (a `players[]` array, as in vanilla), even though v1 runs only one.
  - A cheap **state checksum** is computed per tic (positions, health, `P_Random` index). It is used in tests now and for desync detection in co-op later.
  - An analyser or test enforces the no-float rule (for example, a reflection/IL scan of the `Sim` assembly).
- Skill levels 1–5, including Nightmare (fast monsters and respawn) and the `-nomonsters`-style options exposed in a menu.

### 6.2 Controls (twin-stick)
| Action | Default (KB+M) | Gamepad |
|---|---|---|
| Move | WASD (relative to the screen, not to facing) | Left stick |
| Aim | Mouse cursor on the ground plane | Right stick |
| Fire | LMB | RT |
| Use (open/switch) | E / Space | A |
| Weapon select | 1–7, mouse wheel | LB/RB |
| Run toggle | Shift | Left-stick click |
| Automap | Tab | Back |
| Menu | Esc | Start |

- The player's body angle follows the aim direction. Movement is independent of facing (strafing in all directions at full speed, using vanilla forward and side speeds normalised so diagonals are not faster than intended).
- Input is converted into a vanilla-style `ticcmd` (forward/side move plus absolute angle) before it reaches the sim.

### 6.3 Necessary deviations from vanilla ("top-down tweaks")
Each tweak is a named option in the sim, so the pure vanilla behaviour stays testable.

1. **Absolute aiming.** The player angle is set directly from the cursor each tic instead of being turned incrementally.
2. **Horizontal aim assist.** Hitscan and projectiles snap to the nearest valid target within a small cone (for example ±5°) around the cursor direction. Vanilla vertical autoaim (`P_AimLineAttack`) is kept, because the player cannot aim vertically.
3. **Use action.** The use trace starts from the player in the aim direction. If it hits nothing, the nearest usable line within `USERANGE` facing the player is used instead, so doors do not need pixel-precise aiming.
4. **No first-person weapon sprites.** The weapon state machine (`psprites`) still runs for timing, sounds and muzzle flash. The muzzle flash is shown as a light or sprite effect on the player in the world. The current weapon appears in the HUD.
5. **Visibility of enemies.** Monster AI still uses vanilla sight checks. The player sees everything not hidden by the cutaway, which matches Isowulf's "you see them before they see you".
6. **Spectre / partial invisibility.** Rendered with a shader-based fuzz effect that approximates the vanilla fuzz.

## 7. Presentation

### 7.1 Camera
- A **fixed isometric-style camera**, the same as Isowulf. It does not rotate. It is an orthographic (or narrow-FOV perspective, to be decided in prototyping) camera looking down at roughly 45–60° pitch and 45° yaw, centred on the player with slight smoothing and look-ahead towards the cursor.
- Zoom is adjustable within limits using the mouse wheel with a modifier, or through options.
- Scale: 1 Doom map unit = 1/32 m in Godot (a player is 56 units ≈ 1.75 m).

### 7.2 Level geometry
Built procedurally from map data at level load:

- **Walls:** for each linedef and sidedef:
  - One-sided: middle texture from floor to ceiling.
  - Two-sided: upper texture (where the ceiling difference is), lower texture (where the floor difference is), and an optional masked middle texture (grates, fences) with alpha cut.
  - Texture alignment follows vanilla rules: x/y offsets and the lower/upper unpegged flags (`ML_DONTPEGTOP`, `ML_DONTPEGBOTTOM`).
- **Floors:** the sector floor polygons are built by **clipping each subsector against its BSP partition lines** (the classic approach source ports use for vanilla nodes). The resulting convex polygons are fan-triangulated, and the triangles are grouped per sector. This is robust against the unclosed or malformed sectors found in vanilla maps.
- **Ceilings:** never rendered (see §7.3). Ceiling heights are still used for wall tops, the sim and crushers.
- **Moving sectors:** each sector's floor and its adjoining wall quads are kept in per-sector mesh chunks. Floor and ceiling heights are passed as per-sector values (a data texture or uniform buffer indexed by sector id). A vertex shader positions those vertices, so doors, lifts and crushers move without rebuilding the mesh.
- **Sky:** with no ceilings there is no sky to draw. Out-of-map void is black or a dark backdrop. Walls whose upper texture is sky (`F_SKY1` on both sides) are not drawn, as in vanilla.
- **Animated textures and flats, and scrolling walls** (linedef special 48) are handled in the shader through per-surface parameters.

### 7.3 Occlusion / cutaway
- No ceilings are drawn.
- Walls between the camera and the player (and, optionally, the cursor) are **cut down or faded**. In the wall shader, fragments within a screen-space or world-space radius of the player that lie in front of them, from the camera's view, are discarded above a cutoff height (e.g. 32 units above the player's floor) or dithered.
- Walls far from the player stay at full height so the level keeps its shape.
- To be tuned in prototyping: radius, cutoff height, and whether the cutaway follows the player only or also the cursor.

### 7.4 Lighting
- **Authentic mode (default):** a palette shader. The colour index is looked up in `COLORMAP`, using the sector light level and distance from the camera/player as in vanilla light diminishing (adapted, since view-distance-based diminishing makes less sense top-down: use distance from the player, or none).
- Sector light specials (flicker, strobe, glow, blink) come from `p_lights` and are updated per tic.
- Fullbright sprite frames (`FF_FULLBRIGHT`) ignore the sector light.
- Palette flashes: red when damaged, gold on pickups, green with the radsuit (`PLAYPAL` 1–13), applied as a post effect.
- *Optional* enhanced mode: real Godot lights for muzzle flashes, fireballs and plasma.

### 7.5 Sprites (monsters, items, projectiles, player)
- Rendered as Y-axis billboards (upright, facing the camera), sized from the patch dimensions and offsets at 1:1 map scale, and anchored at the thing's floor height.
- **Rotation selection:** for 8-rotation sprites, the frame is chosen from the angle between the actor's facing and the fixed camera's view direction, using the same formula as vanilla `R_ProjectSprite` with the camera as the viewpoint. Mirrored rotations flip the UV.
- The player is drawn with the `PLAY*` sprites. All weapons share the same body frames, as in vanilla multiplayer.
- Sector light applies through the palette shader. Partial invisibility uses the fuzz effect.
- Corpses, gibs and decorations remain as vanilla things.
- A blob shadow under actors is optional (for depth readability).

### 7.6 HUD & UI
- **HUD:** the original `STBAR` status bar (ammo, health, arms, face, armor, keys, ammo table), scaled at the bottom of the screen. A minimal "fullscreen" HUD is optional.
- Messages ("Picked up a shotgun.") use the original font (`STCFN*`).
- **Automap:** an overlay version of the vanilla automap, drawn from line data with the usual colours and secret and seen-lines rules.
- **Menus:** recreated in Godot with the original graphics (`M_DOOM`, `M_NGAME`, `M_SKULL1/2`, etc.): New Game → Episode → Skill, Options, Load/Save, Quit with its quit messages.
- **Intermission screens** (`WI*` graphics: kills, items, secrets, time, par) and the episode **finale text screens** and end sequences (`VICTORY2`, `ENDPIC`, the E3 bunny scroller, the Doom II cast call later).
- The title, demo loop and credits show `TITLEPIC`/`CREDIT`/`HELP`. Demo playback is **not** included.

### 7.7 Audio
- Sound effects use positional 2D/3D audio from the actor's position relative to the player, with vanilla's sound limits and priorities approximated. Per-sector sound propagation for monster alerting stays in the sim (`P_NoiseAlert`).
- **Music (authentic OPL, default):** the `D_*` MUS lumps are played directly through an emulated **OPL3 (AdLib/Sound Blaster FM)** chip, using the instrument bank in the WAD's `GENMIDI` lump. This reproduces the sound of the original DOS release and uses only original assets.
  - Emulator: a C# port of **Nuked-OPL3** (LGPL-2.1, GPL-compatible). If it costs too much CPU, a lighter emulator (DOSBox DBOPL) is the fallback.
  - Driver: a port of Chocolate Doom's `i_oplmusic.c` (GPL-2.0), which handles the MUS sequencer, voice allocation and OPL2/OPL3 modes.
  - Output: a C# `AudioStreamGenerator` fed from a dedicated thread at 44.1/48 kHz with a small ring buffer.
- **Music (optional, later):** a General MIDI mode through a software synth such as MeltySynth, with a user-supplied SF2 soundfont, for players who prefer the SC-55/GUS sound.

## 8. Game flow & persistence
- Episode/map progression, secret exits (E1M3 → E1M9, etc.), and end-of-episode finales.
- Par times from the vanilla tables.
- **Save/load:** a serialised sim state in a custom format (vanilla-compatible save files are not a goal). Quicksave and quickload are included.
- **Options:** controls/rebinding, mouse sensitivity, audio volumes, video (resolution, fullscreen, vsync, zoom), gameplay tweaks (aim-assist strength, cutaway style, authentic vs enhanced lighting).
- Cheats: `iddqd`, `idkfa`, `idclip`, `idclev`, `iddt` (automap). These are cheap to port and useful for testing.

## 9. Performance targets
- 60+ fps at 1080p on integrated graphics for all Doom 1 maps.
- **Steam Deck:** a locked 60 fps at 1280×800 (native Linux build) on all Doom 1 and Doom II maps, with headroom for battery life (a 40/30 fps cap option).
- Level load (WAD parse, mesh build) under 1 s per map.
- The sim stays under 2 ms per tic with a full map (e.g. E1M9 or MAP29-class monster counts).

## 10. Testing
- **Unit tests** for the WAD reader, lump namespaces, decoders (palette, patch, flat composition) and map parsing, run against `DOOM1.WAD` when it is present.
- **Sim tests** with tweaks disabled: fixed-point maths and table checks, `P_Random` sequence, line-special behaviour on small synthetic maps.
- *Stretch:* verify the sim core against vanilla demos (`DEMO1–3` in `DOOM1.WAD`) with all tweaks off and a vanilla-input adapter. A demo that stays in sync is strong proof that the port is faithful.
- Visual checks with a debug overlay showing sector ids, linedef ids and blockmap cells.

## 11. Milestones

| # | Milestone | Done when |
|---|---|---|
| M0 | Project skeleton | Godot 4 C# project, CI build, tests run |
| M1 | WAD viewer | Loads `DOOM1.WAD`; lists lumps; shows palette, textures, flats and sprites in a debug scene |
| M2 | Static level render | E1M1 is rendered as a 3D mesh with correct textures, alignment, light levels and the palette shader; free-fly debug camera |
| M3 | Iso camera + cutaway | Fixed iso camera following a placeholder; walls cut away; masked midtextures; things rendered as billboards with rotations |
| M4 | Sim core: movement | 35 Hz tic loop; twin-stick player with vanilla movement and collision (blockmap, step-up height, dropoffs); interpolation |
| M5 | Map specials | Doors, lifts, switches, teleporters, light specials, damage floors, exits; keys; E1M1–E1M9 completable without monsters |
| M6 | Monsters & weapons | `info.c` states, AI, all Doom 1 shareware weapons and monsters, pickups, damage, aim assist; status bar HUD |
| M7 | Game shell | Menus, skill and episode select, intermission, finale, save/load, options, sound effects, OPL music, shareware download (if cheap; see §3) |
| M8 | Shareware complete | All of E1 playable start to finish on desktop Linux and the Steam Deck; polish pass |
| M9 | Ultimate Doom | E2–E4, bosses, registered-only content |
| M10 | Doom II | Doom II monsters and weapons, MAPxx flow, cast call, Icon of Sin; then Final Doom |

## 12. Decisions log

| Date | Decision |
|---|---|
| 2026-10-05 | Godot 4 + C#, desktop only. Linux (Steam Deck) is a first-class target |
| 2026-10-05 | Fixed isometric camera (no rotation), no ceilings, wall cutaway |
| 2026-10-05 | Faithful port of the vanilla sim, with switchable top-down tweaks |
| 2026-10-05 | Music via OPL3 emulation + `GENMIDI` (authentic); a GM soundfont mode is optional later |
| 2026-10-05 | Shareware auto-download if cheap to implement, otherwise deferred |
| 2026-10-05 | The sim is deterministic and float-free to keep co-op possible; revisit if it gets in the way |
| 2026-10-05 | Target .NET 10 (LTS, supported to Nov 2028) instead of .NET 8 (support ends Nov 2026). Godot's C# libraries target .NET 8 but run fine in a `net10.0` project; Godot-generated `.csproj` files default to `net8.0` and must be changed |
| 2026-10-05 | **Pinned toolchain (T0.1):** Godot **4.7.2-stable .NET** (`Godot_v4.7.2-stable_mono_linux_x86_64`, the latest stable at the time), `Godot.NET.Sdk/4.7.2`, .NET SDK **10.0.401** (runtime 10.0.12), `net10.0` everywhere. Install on Linux: the dev container (`.devcontainer/Dockerfile`) builds on `mcr.microsoft.com/devcontainers/dotnet:2-10.0-noble` and unpacks the Godot .NET zip from the GitHub release into `/opt/godot`, with a `godot` wrapper in `/usr/local/bin`. Without the container: install the .NET 10 SDK (Microsoft's packages or `dotnet-install.sh`) and unzip the same Godot .NET build anywhere on `PATH`. Bump versions by changing `GODOT_VERSION` in `devcontainer.json`/`Dockerfile` and the SDK version in `IsoDoom.csproj` together |
| 2026-10-05 | **Project split:** the repo root is the Godot project (`project.godot`, `IsoDoom.csproj`, `IsoDoom.sln`). `src/Wad`, `src/Map` and `src/Sim` are plain `Microsoft.NET.Sdk` class libraries (`IsoDoom.Wad`, `IsoDoom.Map`, `IsoDoom.Sim`) with no Godot reference, so a Godot type in them is a compile error. References: `Map → Wad`, `Sim → Map`, and the Godot project references all three. `Assets`, `Render`, `Game` and `Audio` stay in the Godot project. `IsoDoom.csproj` excludes `src/{Wad,Map,Sim}/**` and `tests/**` from its default glob. Shared library settings (nullable, warnings as errors, no implicit usings) live in `src/Directory.Build.props` |
| 2026-10-05 | `IsoDoom.sln` keeps Godot's solution configurations (`Debug`, `ExportDebug`, `ExportRelease`); the plain libraries map them to `Debug`/`Debug`/`Release`, so Godot export builds work |
| 2026-10-05 | `.gdignore` files keep Godot from scanning `src/Wad`, `src/Map`, `src/Sim`, `tests`, `wads` and `doom_20230531` (no `.import`/`.uid` files; the libraries hold no Godot scripts). Only the `.gdignore` markers in `wads/` and `doom_20230531/` are tracked |

## 13. Open questions / risks

1. **Ortho vs perspective camera.** True orthographic camera or a narrow-FOV perspective camera? Decide in M3 prototyping.
2. **Light diminishing.** View-distance diminishing does not map naturally to top-down. Options: player-distance based, flat sector light only, or light radius around the player. Decide in M2/M3.
3. **Sprite readability.** Doom sprites are drawn for eye-level views. Seen from above at a 45–60° pitch they may look "flat". Mitigations: billboard tilt toward the camera, blob shadows, outlines.
4. **Tall-room readability.** Very high walls (E1M8, Doom II city maps) may need a lower cutaway threshold or per-area camera tweaks.
5. **Aim on gamepad / Steam Deck.** Right-stick aiming loses cursor distance. Aim assist may need to be stronger on a pad, possibly with a lock-on toggle. Test on a Deck from M6 onward.
6. **Determinism cost.** The fixed-point-only sim makes the presentation layer convert every frame, and it rules out Godot physics for gameplay. Track this; if it becomes painful, revisit the co-op goal.
