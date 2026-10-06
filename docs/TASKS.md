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
  *Status:* workflow, export preset and `## Commands` added (branch `t0.3-ci`); build, test, headless export and the exported-binary smoke test pass locally, and `actionlint` is clean. Not ticked: the repo has no remote yet, so CI has not run. Tick after the first push shows a green run.
- [ ] **T0.4 Interactive editor check.** Open the project in the Godot editor with a real display (Wayland/X11 passthrough in the dev container) and run the main scene with F5; T0.1 only verified this headlessly. Fix any GPU/display issues in `.devcontainer/`.
  *Done when:* the editor opens without errors and the main scene runs in a window with the Forward+ renderer.

## M1 — WAD viewer

- [x] **T1.1 WAD reader.** Parse the header and lump directory; look lumps up by name; handle namespace markers (`S_`, `F_`, `P_` and their `SS_`/`FF_` variants); merge PWADs over the IWAD. SPEC §5.1.
  *Done when:* tests against `DOOM1.WAD` assert the lump count, the `IWAD` type, and that `E1M1`, `PLAYPAL` and `TROOA1` are found.
- [x] **T1.1a IWAD location.** Find the IWAD through a configurable path, the game folder and common install locations (Steam/GOG), with a file picker as the fallback, and load PWADs given on the command line (`-file`) through `WadArchive`. SPEC §5.1.
  *Done when:* the game starts with `DOOM1.WAD` from each search location in turn, and shows the picker when none is found.
  *Done:* `IsoDoom.Wad.IwadLocator` (d_iwad.c port, unit-tested on temp folders with a faked environment) searches `-iwad`, `ISODOOM_IWAD`/`ISODOOM_DOOM1_WAD`, the configured path (`user://settings.cfg`, written by the picker), the working directory, the game folder and its `wads/`, `DOOMWADDIR`/`DOOMWADPATH`, XDG folders, Steam (all libraries) and GOG folders/registry keys; `-file` PWADs load through `WadArchive`. The editor run and the exported binary both started with `DOOM1.WAD` from each Linux location, and the viewer opens the file picker when nothing is found (checked with a real display). The loading path is `WadViewer.LoadWad` for now. See SPEC §12.
- [ ] **T1.1b Optional shareware download.** SPEC §5.1: when no IWAD is found, offer to download the shareware `DOOM1.WAD` (v1.9, MD5 `f0cefca49926d00903cf57551d901abe`) next to the picker, if a stable, redistributable source exists and it is cheap (SPEC §12: otherwise deferred). Check the shareware licence terms first, verify the MD5, and save it to the game folder or `user://` and the configured path.
  *Done when:* with no IWAD anywhere, the download option fetches and verifies `DOOM1.WAD` and the game starts with it, or the task is closed with the reason logged in SPEC §12.
- [ ] **T1.1c IWAD search on real installs.** T1.1a's Windows (GOG/Steam registry keys, GOG Galaxy folder), macOS and Steam Deck paths are only unit-tested with faked folders. Check them against real installs (Steam Ultimate Doom/Doom II including the 2024 re-release folders, a GOG install, a Steam Deck SD-card library) and fix any folder names.
  *Done when:* the game finds the IWAD unaided on at least a Windows Steam or GOG install and a Steam Deck.
- [x] **T1.2 IWAD identification.** Detect shareware, registered, Ultimate, Doom II and Final Doom from the lumps present, as vanilla `IdentifyVersion` does. SPEC §2.
  *Done when:* `DOOM1.WAD` is detected as shareware, and the other modes are covered by synthetic test WADs.
- [x] **T1.2a Modified-game checks and IWAD variants.** Vanilla `D_DoomMain` (d_main.c) refuses PWADs with the shareware IWAD ("You cannot -file with the shareware version. Register!") and checks that a registered IWAD holds the episode 2-3 lumps. Decide whether to port the shareware refusal as vanilla behaviour (with a tweak flag to allow PWADs, SPEC §6.3) or drop it, and wire the check into the loading path from T1.1a. Also detect Chocolate Doom's `gamevariant` (Freedoom via the `FREEDOOM` lump, FreeDM via `FREEDM`, BFG Edition via `DMENUPIC`) on top of `IwadInfo`. SPEC §2.
  *Done when:* tests on synthetic WADs cover the chosen PWAD policy and each variant.
  *Done:* vanilla policy kept, with no override: `ModifiedGame.D_CheckModifiedGame` (d_main.c) refuses PWADs with the shareware IWAD and rejects a "registered" IWAD missing vanilla's 23 lumps when PWADs are loaded; Freedoom/FreeDM skip both checks (Chocolate Doom). `WadViewer.LoadWad` runs it after `D_IdentifyVersion`. `IwadInfo.GameVariant` (`vanilla`, `freedoom`, `freedm`, `bfgedition`) comes from the IWAD's own lumps. Real-WAD tests: DOOM1 refuses a PWAD, DOOM II (`ISODOOM_DOOM2_WAD` or `wads/doom2.wad`) is `commercial`/`doom2`/`vanilla` and accepts one. See SPEC §12.
- [x] **T1.3 Palette, colormap and graphics decoders.** Decode `PLAYPAL`, `COLORMAP`, and patch-format graphics (column posts) into 8-bit index buffers plus offsets. Decode flats. SPEC §5.2.
  *Done when:* unit tests pass on known lump sizes and offsets, and a debug export of `TITLEPIC` and `TROOA1` to PNG looks right.
- [x] **T1.3a Tall patches (PWAD graphics).** `Patch.Decode` places posts at their absolute `topdelta`, as vanilla does, so patches taller than 254 pixels can't be expressed. Many PWADs use the DeePsea "tall patch" convention (a `topdelta` not greater than the previous post's is relative to it), which Chocolate Doom ignores but most source ports support. Decide whether to support it (it changes nothing for IWAD graphics) and log the choice in SPEC §12.
  *Done when:* a synthetic tall-patch test decodes to the chosen layout.
  *Note (from T1.4):* `Textures.DrawColumnInCache` walks the raw patch posts with absolute `topdelta` too; if tall patches are supported, change it to match.
  *Done:* supported by default (`PatchTopDeltaMode.Tall`) in `Patch.Decode`, `Patch.IsPatch` and `Textures` composition, with `PatchTopDeltaMode.Vanilla` for absolute `topdelta`s; DOOM1 and Doom II graphics decode and composite identically in both modes. See SPEC §12.
- [x] **T1.4 Wall texture composition.** Parse `PNAMES` and `TEXTURE1` (and `TEXTURE2` when present) and composite patches into textures, including vanilla quirks such as patches that run past the texture's edges.
  *Done when:* `STARTAN3`, `DOOR3` and the masked `MIDGRATE` are visually checked in a PNG export.
  *Note:* `MIDGRATE` is not in the shareware WAD (it is a registered/Doom II texture), so the shareware masked grates `BRNSMALC` and `BRNBIGC` were checked instead; see T1.4a.
- [x] **T1.4a Full-IWAD texture check.** With a registered/Ultimate Doom or Doom II IWAD (not in the repo; point a test at it with an environment variable like `ISODOOM_DOOM1_WAD`), check that every texture in TEXTURE1/TEXTURE2 composites, and visually check `MIDGRATE` and a few multi-patch masked midtextures in the `Doom1TextureTests.ExportDebugPngs`-style export. SPEC §5.2.
  *Done when:* the full-IWAD tests pass (skipping without the IWAD) and `MIDGRATE` shows transparent holes and no seams.
  *Done:* `Doom2TextureTests` (Doom II, `TestWads.RequireDoom2()`, any version; v1.666-specific counts and lists only for its MD5) composites all 428 textures in both composite modes; `MIDGRATE` (one 128×128 patch, `M1_1`) equals its patch hole for hole and tiles without seams in the checkerboard export (`DOOM2_TEX_*.png`). Doom II has no TEXTURE2, no masked flags and **no multi-patch masked midtexture** (all 11 see-through midtextures are single-patch), so multi-patch composition was checked on solid textures instead: every multi-patch texture is fully opaque except `SKINEDGE`, whose one hole is a missing pixel in patch `HELL8_1` (`SKY2` and `ZZZFACE3` have the same in their single patches). See SPEC §12.
- [ ] **T1.4b Registered/Ultimate Doom texture check.** Doom II has no `TEXTURE2`, so real `TEXTURE2` data is only covered by the synthetic tests. With an Ultimate Doom (or registered) IWAD (environment variable, as for Doom II), check that every TEXTURE1/TEXTURE2 texture composites, and export a few of its multi-patch textures. SPEC §5.2.
  *Done when:* the Doom 1 full-IWAD tests pass (skipping without the IWAD).
- [x] **T1.5 Sprite indexing.** Group sprite lumps into sprite, frame and rotation sets, including mirrored pairs (`TROOA2A8`) and rotation 0. SPEC §5.2, §7.5.
  *Done when:* tests on `TROO`, `PLAY` and `BAR1` assert the frame and rotation counts and the flip flags.
  *Note (from T1.1):* `WadArchive` merges the sprite namespace by whole lump name only. Chocolate Doom's w_merge.c also lets a PWAD sprite replace a single frame/rotation of an IWAD lump (e.g. a PWAD `TROOA2` drops the `A2` half of the IWAD's `TROOA2A8`). Handle that here, when frames are indexed.
  *Done:* `Sprites.R_InitSprites` (r_things.c) indexes the sprites named in info.c's `sprnames` (`SpriteNames`), and PWADs replace IWAD sprites per frame/rotation slot; see SPEC §12.
- [x] **T1.5a Full-IWAD sprite check.** With a registered/Ultimate Doom or Doom II IWAD (environment variable, as in T1.4a), check that `Sprites.R_InitSprites` indexes all 138 `sprnames` without errors (Doom II has every sprite, so this also checks the ported name table) and that every frame lump decodes. SPEC §5.2.
  *Done when:* the full-IWAD tests pass (skipping without the IWAD).
  *Done:* `Doom2SpriteTests` (Doom II, `TestWads.RequireDoom2()`, any version; v1.666 totals only for its MD5) indexes all 138 `sprnames` without errors; every sprite has frames and every one of the 1381 sprite lumps is used (so no Doom II sprite is missing from the name table), 628 frames in all. Every slot's lump name matches its frame, rotation and flip, Doom II-only sprites (`VILE`, `CYBR`, `SPID`, `SSWV`, `KEEN`, `BBRN`, …) have vanilla's frame counts and rotating frames, and every frame decodes. Nothing in the name table or the indexer needed fixing. See SPEC §12.
- [ ] **T1.5b Registered/Ultimate Doom sprite check.** With an Ultimate Doom (or registered) IWAD (environment variable, as for Doom II), check that `Sprites.R_InitSprites` indexes it without errors, that every sprite lump is used, that the Doom 1 sprites (`CYBR`, `SPID`, `BOSS`, …) have frames and the Doom II-only ones (`VILE`, `SSWV`, `KEEN`, `BBRN`, `FATT`, …) have none, and that every frame decodes. SPEC §5.2.
  *Done when:* the Doom 1 full-IWAD tests pass (skipping without the IWAD).
- [x] **T1.6 Godot WAD viewer scene.** Upload index textures as R8, add a palette shader using `PLAYPAL` and `COLORMAP`, and build a debug scene for browsing textures, flats, sprites and UI graphics, with a light-level slider.
  *Done when:* every graphic in `DOOM1.WAD` can be browsed with correct colours.
  *Note (from T1.5):* browse sprites through `Sprites` (sprite → frame → rotation) so mirrored rotations show flipped, as the renderer will draw them.
  *Done:* `scenes/WadViewer.tscn` (opened by the main scene for now) browses wall textures, flats, sprites (sprite → frame → rotation, all 8 rotations side by side, mirrored ones flipped), wall patches, the 320 other patch-format graphics and the palette tables, uploaded as RG8 index textures and coloured by `shaders/palette.gdshader` (PLAYPAL + COLORMAP), with light, invulnerability, PLAYPAL and zoom controls. `godot -- --viewer-check` walks all 1276 views; with a real renderer it reads every drawn pixel back and compares it with the CPU palette conversion under three lighting settings (all match on DOOM1.WAD). See SPEC §12.
- [ ] **T1.6a Lump list in the viewer.** SPEC §11 M1 also says "lists lumps": add a lump directory view to the WAD viewer (name, size, file, namespace, and what the lump is: graphic, map, sound, music, …), jumping to the graphic view for graphics.
  *Done when:* every lump of `DOOM1.WAD` is listed, and selecting a graphic lump shows it.
- [ ] **T1.6b Viewer check in CI.** `--viewer-check` needs a WAD, so CI can't run it against DOOM1. Run it headlessly in CI against a small synthetic IWAD (built by a test helper or a script; PLAYPAL, COLORMAP, PNAMES/TEXTURE1, a few patches, flats and sprites), or against Freedoom if its BSD licence and download size are acceptable.
  *Done when:* CI runs the viewer check and fails on a broken upload.

## M2 — Static level render

- [ ] **T2.1 Map lump parsing.** Parse `THINGS`, `LINEDEFS`, `SIDEDEFS`, `VERTEXES`, `SEGS`, `SSECTORS`, `NODES`, `SECTORS`, `BLOCKMAP` and `REJECT` into fixed-point structures in `Map/`.
  *Done when:* tests assert the E1M1 element counts and the player 1 start position.
- [ ] **T2.2 Subsector polygons.** Build convex subsector polygons by clipping against the BSP partition lines, then group them per sector. SPEC §7.2.
  *Done when:* for every map in E1, the polygon area per sector matches the sector area within tolerance, and there are no gaps in a debug 2D render.
- **T2.3 onward:** break these down when M1 is nearly done (wall mesh, floor mesh, per-sector height data, palette lighting, free-fly camera).
  *Note (from T1.4):* for the wall mesh, vanilla tiles textures horizontally by `texturewidthmask` (the largest power of two not above the width) and vertically by 128 (`R_DrawColumn`'s `& 127`), so a texture whose height is not 128 (`DOOR3` is 72 tall) shows "tutti-frutti" garbage or a wrong wrap when a wall is taller than it. Decide whether to reproduce that or tile by the real size, and log it in SPEC §12. Also decide how solid (one-sided) walls treat the transparent pixels of a composite (`Textures.R_GenerateComposite` keeps holes; vanilla draws garbage there). Doom II has single missing pixels in the solid textures `SKINEDGE` (73,36), `SKY2` (134,59 and 134,64) and `ZZZFACE3` (204,94), from their patch data (T1.4a): vanilla draws a wrong colour there, not a hole, so a solid wall must not show the background through them.

## M3 to M10

Not broken down yet. See SPEC §11.

*Note (from T1.2a):* `IwadInfo.GameVariant` is detected but nothing acts on it yet. When the menus and intermission text are ported, add Chocolate Doom's BFG Edition workarounds (d_main.c: the menu lumps `M_GDHIGH`/`M_GDLOW` → `M_MSGON`/`M_MSGOFF` and `M_SCRNSZ` → `M_DISP`, the renamed MAP31/MAP32 names and MAP33) and Freedoom's own level names and text (its `DEHACKED` lump, once DeHackEd is supported).

*Note (from T1.1a):* when the game shell (M7) is broken down, add an IWAD selection menu for when several IWADs are found (`IwadLocator.D_FindAllIWADs`), and revisit the in-folder IWAD preference (DOOM 1 before DOOM II, SPEC §12) once DOOM II is playable.
