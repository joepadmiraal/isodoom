# Tasks

Each task is sized for one fresh Claude Code session. Start a session with: *"Do task T1.2 from TASKS.md."*

Only the near milestones are broken down into tasks. When a milestone's tasks are nearly done, a session breaks down the next milestone first (that is a task in itself). Milestone definitions are in SPEC §11.

Legend: `[ ]` todo, `[x]` done.

## M0 — Project skeleton

- [x] **T0.1 Toolchain + Godot project.** Install, or document how to install, the latest stable Godot 4.x .NET build and the .NET SDK it requires, on Linux. Create the Godot C# project with the folder layout from SPEC §4.1. Split `Sim` and `Wad`/`Map` into plain .NET class libraries (`net10.0`, no Godot reference) referenced by the Godot project, so the sim's purity is enforced by the compiler. Record the pinned versions in SPEC §12.
  *Done when:* the project opens in Godot, runs an empty main scene, and `dotnet build` succeeds from the CLI.
- [x] **T0.2 Test setup.** Add an xUnit test project for the plain libraries, with a helper that locates `wads/DOOM1.WAD` and skips a test when the file is absent. Add a test that scans the `Sim` assembly and fails if it uses `float`, `double`, `System.Random` or `DateTime` (SPEC §6.1).
  *Done when:* `dotnet test` runs green and the purity test fails when a float is temporarily introduced in `Sim`.
- [ ] **T0.3 CI + commands.** Add a GitHub Actions workflow that runs build and tests, plus a headless Godot export for Linux x86_64. Add a `## Commands` section to `AGENTS.md` listing build, test, run and export.
  *Done when:* CI is green on a pushed branch.
- [ ] **T0.4 Interactive editor check.** Open the project in the Godot editor with a real display (Wayland/X11 passthrough in the dev container) and run the main scene with F5; T0.1 only verified this headlessly. Fix any GPU/display issues in `.devcontainer/`.
  *Done when:* the editor opens without errors and the main scene runs in a window with the Forward+ renderer.

## M1 — WAD viewer

- [ ] **T1.1 WAD reader.** Parse the header and lump directory; look lumps up by name; handle namespace markers (`S_`, `F_`, `P_` and their `SS_`/`FF_` variants); merge PWADs over the IWAD. SPEC §5.1.
  *Done when:* tests against `DOOM1.WAD` assert the lump count, the `IWAD` type, and that `E1M1`, `PLAYPAL` and `TROOA1` are found.
- [ ] **T1.2 IWAD identification.** Detect shareware, registered, Ultimate, Doom II and Final Doom from the lumps present, as vanilla `IdentifyVersion` does. SPEC §2.
  *Done when:* `DOOM1.WAD` is detected as shareware, and the other modes are covered by synthetic test WADs.
- [ ] **T1.3 Palette, colormap and graphics decoders.** Decode `PLAYPAL`, `COLORMAP`, and patch-format graphics (column posts) into 8-bit index buffers plus offsets. Decode flats. SPEC §5.2.
  *Done when:* unit tests pass on known lump sizes and offsets, and a debug export of `TITLEPIC` and `TROOA1` to PNG looks right.
- [ ] **T1.4 Wall texture composition.** Parse `PNAMES` and `TEXTURE1` (and `TEXTURE2` when present) and composite patches into textures, including vanilla quirks such as patches that run past the texture's edges.
  *Done when:* `STARTAN3`, `DOOR3` and the masked `MIDGRATE` are visually checked in a PNG export.
- [ ] **T1.5 Sprite indexing.** Group sprite lumps into sprite, frame and rotation sets, including mirrored pairs (`TROOA2A8`) and rotation 0. SPEC §5.2, §7.5.
  *Done when:* tests on `TROO`, `PLAY` and `BAR1` assert the frame and rotation counts and the flip flags.
- [ ] **T1.6 Godot WAD viewer scene.** Upload index textures as R8, add a palette shader using `PLAYPAL` and `COLORMAP`, and build a debug scene for browsing textures, flats, sprites and UI graphics, with a light-level slider.
  *Done when:* every graphic in `DOOM1.WAD` can be browsed with correct colours.

## M2 — Static level render

- [ ] **T2.1 Map lump parsing.** Parse `THINGS`, `LINEDEFS`, `SIDEDEFS`, `VERTEXES`, `SEGS`, `SSECTORS`, `NODES`, `SECTORS`, `BLOCKMAP` and `REJECT` into fixed-point structures in `Map/`.
  *Done when:* tests assert the E1M1 element counts and the player 1 start position.
- [ ] **T2.2 Subsector polygons.** Build convex subsector polygons by clipping against the BSP partition lines, then group them per sector. SPEC §7.2.
  *Done when:* for every map in E1, the polygon area per sector matches the sector area within tolerance, and there are no gaps in a debug 2D render.
- **T2.3 onward:** break these down when M1 is nearly done (wall mesh, floor mesh, per-sector height data, palette lighting, free-fly camera).

## M3 to M10

Not broken down yet. See SPEC §11.
