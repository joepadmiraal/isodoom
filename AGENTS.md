# IsoDoom

An isometric top-down remake of Doom in Godot 4 + C#. It loads the original WAD at runtime. The full design is in `docs/SPEC.md`. Read only the sections your task cites.

## Session workflow

Each session completes **one task** from `docs/TASKS.md`:

1. Read the task and the `SPEC.md` sections it cites.
2. Do the work. The task is done when its *Done when* criterion holds and all tests pass.
3. In `TASKS.md`: tick the task, and add any follow-up tasks you discovered under the right milestone.
4. When you make a design decision the spec doesn't cover, add it to the decisions log in `SPEC.md` §12.
5. Commit on a branch named after the task (e.g. `t1.2-wad-reader`).

## Commands

Run from the repo root. Versions are pinned in `docs/SPEC.md` §12; the .NET SDK comes from `global.json`.

- **Build:** `dotnet build IsoDoom.sln`
- **Test:** `dotnet test`. Tests that need a real WAD skip themselves without it (as in CI); see *Test data*.
- **Run:** `godot` opens the editor (F5 runs the main scene, currently the WAD viewer). `godot --headless --quit-after 60` runs the main scene headlessly for 60 frames; drop `--headless` for a window. On a fresh checkout, run `godot --headless --import` first.
  - Game arguments go after `--`: `godot -- -iwad wads/doom2.wad -file my.wad`. Without `-iwad` (or `ISODOOM_IWAD`), the IWAD search of `src/Wad/IwadLocator.cs` runs (saved path, working dir, `wads/`, `DOOMWADDIR`, Steam/GOG, …; SPEC §12) and a file picker opens when nothing is found. The shareware IWAD refuses `-file`, as in vanilla.
- **Level scene:** `godot -- --level E1M1` (or `--level MAP01`; IWAD as above) shows the map as a textured mesh from a fixed overview camera instead of the viewer. Debug arguments: `--level-view=top` (straight down; default iso), `--level-focus=SECTOR`, `--level-tiling=size` (default `vanilla`), `--level-sector-floor=SECTOR:HEIGHT[,…]`, `--level-light=player|none|camera` (light diminishing, SPEC §12 T2.8; default `player`), `--level-light-origin=X,Y` (the player position for it, map units; default the free-fly pivot or player 1's start), `--level-background=RRGGBB` (the void's colour; one in no palette, e.g. `00ff00`, makes cracks stand out), and `--level-screenshot=FILE.png` (capture and quit; needs a real renderer, e.g. the offscreen Xvfb + lavapipe command below; never commit the images).
  - **Level scene keys:** Tab switches between the overview and the free-fly debug camera (`--level-camera=fly` starts in it), Home jumps to player 1's start, PgDn/PgUp load the next/previous map of the WAD, L cycles the light diminishing mode, F1 lists the controls, F3 hides the overlay (map, FPS, camera position in map units, the sector and subsector under the camera). Free-fly: click to capture the mouse (Esc releases), mouse look, W/A/S/D move, E/Space up, Q/C down, Shift ×4, Alt ×¼, wheel = speed, Ctrl+wheel = FOV or ortho size, O = perspective/orthographic.
  - **Scripted input** (no keyboard needed): `--level-script="click; look 0 200; hold W 60; tap PageDown; print; shot FILE.png"` feeds the commands through `Input.ParseInputEvent` and quits (full list in `src/Game/LevelScript.cs`); put `--fixed-fps 60` before `--` for frame-exact movement. `fov vanilla; view X Y ANGLE` puts the free-fly camera where a vanilla player standing at map point X, Y facing ANGLE (degrees) would see (eye level, vanilla's 90° field in a 16:10 window), for comparing with vanilla screenshots (with `--level-light=camera`).
- **Viewer check:** `godot --headless -- --viewer-check` walks every graphic and every lump in the viewer and checks the uploads; it exits 1 on a failure. With a real renderer it also compares every drawn pixel: run it windowed, or offscreen with `WAYLAND_DISPLAY= VK_DRIVER_FILES=/usr/share/vulkan/icd.d/lvp_icd.json xvfb-run -a -s "-screen 0 1280x720x24" godot -- --viewer-check`. Add `--viewer-screenshots=DIR` to save captures (never commit WAD-derived images).
- **Level check:** `godot --headless -- --level-check` loads every map of the IWAD in the level scene and checks the meshes, texture binding and per-sector data against the levels, plus a floor moved through the data texture; it exits 1 on a failure. With a real renderer (windowed, or offscreen as the viewer check) it also renders one map (`--level MAP`, default `E1M1`/`MAP01`) top-down, side-on at every axis-aligned wall and obliquely at the moved floor, and compares drawn pixels with the CPU texel lookup and light mapping (about 30 s for DOOM1 E1M1 under lavapipe, 4 s for the synthetic IWAD). It also fails a map that takes over 1 s to load (SPEC §9) and prints the slowest.
- **Vanilla comparison:** `tools/VanillaRef/compare.sh DIR wads/DOOM1.WAD E1M1 "1056 -3616 90;1056 -3616 0" [--images]` renders each view (map X Y, angle in degrees, at eye level) with vanilla's renderer (doomgeneric, GPL-2.0, pinned and patched by `tools/VanillaRef/build.sh` into `~/.cache/isodoom/vanilla-ref`; a dev tool, never shipped) and in the level scene offscreen, and compares them pixel by pixel (SPEC §12, T2.9). DIR gets WAD-derived images: keep it out of the repo.
- **Synthetic IWAD:** `dotnet run --project tools/SyntheticIwad -- OUT.wad` writes a small IWAD of generated, non-id content covering every lump kind; CI runs the viewer check against it.
- **Export (Linux x86_64):** `mkdir -p export/linux && godot --headless --export-release "Linux" export/linux/IsoDoom.x86_64`. Needs the 4.7.2 .NET export templates in `~/.local/share/godot/export_templates/4.7.2.stable.mono/` (build the dev container with `INSTALL_EXPORT_TEMPLATES=true`, or see `.github/workflows/ci.yml`). Smoke test: `export/linux/IsoDoom.x86_64 --headless --quit-after 60`.
- **CI:** `.github/workflows/ci.yml` runs build + test, generates the synthetic IWAD, runs the viewer and level checks in the editor (headless), exports and smoke-tests the Linux build, runs both checks on the exported build under Xvfb + lavapipe, and uploads `export/linux/` as the `IsoDoom-linux-x86_64` artifact. Lint it with `actionlint` (in the dev container; it runs `shellcheck` on the scripts).

## Invariants

- **`Sim/` is deterministic** (so lockstep co-op is possible later):
  - pure C#, with no Godot types;
  - fixed-point/BAM maths only, no `float`/`double`;
  - no `System.Random`, wall-clock time, or iteration over `Dictionary`/`HashSet`;
  - the only input is a `ticcmd` per player per tic.

  The presentation layer converts to floats.
- **WAD data stays out of the repo.** All assets load from the user's WAD at runtime. `wads/` is gitignored.
- **Ported vanilla logic keeps vanilla names** (`P_Random`, `A_Chase`, `mobjinfo`), with a comment citing the source file (e.g. `// p_enemy.c`). This keeps it easy to diff against the original.
- **Deviations from vanilla behaviour sit behind a named tweak flag** (SPEC §6.3), so pure vanilla behaviour stays testable.
- **The license is GPL-2.0-or-later.** Porting id Software or Chocolate Doom code is fine. Check license compatibility before adding any other dependency.

## Test data

`wads/DOOM1.WAD` is the shareware v1.9 WAD (MD5 `f0cefca49926d00903cf57551d901abe`; set `ISODOOM_DOOM1_WAD=/path/to/DOOM1.WAD` to use one elsewhere). Tests that need it skip themselves when the file is absent.

`wads/doom2.wad` (any Doom II version; set `ISODOOM_DOOM2_WAD=/path/to/DOOM2.WAD` to use one elsewhere) enables the Doom II tests, which skip themselves without it.

## References

- Vanilla source: `id-Software/DOOM` on GitHub (`linuxdoom-1.10`). Chocolate Doom covers vanilla-accurate fixes and OPL music (`i_oplmusic.c`).
- WAD and lump formats: the Doom Wiki and the *Unofficial Doom Specs*.
