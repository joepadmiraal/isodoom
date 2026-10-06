# IsoDoom

An isometric top-down remake of Doom in Godot 4 + C#. It loads the original WAD at runtime. The full design is in `SPEC.md`. Read only the sections your task cites.

## Session workflow

Each session completes **one task** from `TASKS.md`:

1. Read the task and the `SPEC.md` sections it cites.
2. Do the work. The task is done when its *Done when* criterion holds and all tests pass.
3. In `TASKS.md`: tick the task, and add any follow-up tasks you discovered under the right milestone.
4. When you make a design decision the spec doesn't cover, add it to the decisions log in `SPEC.md` §12.
5. Commit on a branch named after the task (e.g. `t1.2-wad-reader`).

## Commands

Run from the repo root. Versions are pinned in `SPEC.md` §12; the .NET SDK comes from `global.json`.

- **Build:** `dotnet build IsoDoom.sln`
- **Test:** `dotnet test`. Tests that need `wads/DOOM1.WAD` skip themselves without it (as in CI); set `ISODOOM_DOOM1_WAD=/path/to/DOOM1.WAD` to use a WAD elsewhere.
- **Run:** `godot` opens the editor (F5 runs the main scene). `godot --headless --quit-after 60` runs the main scene headlessly for 60 frames; drop `--headless` for a window. On a fresh checkout, run `godot --headless --import` first.
- **Export (Linux x86_64):** `mkdir -p export/linux && godot --headless --export-release "Linux" export/linux/IsoDoom.x86_64`. Needs the 4.7.2 .NET export templates in `~/.local/share/godot/export_templates/4.7.2.stable.mono/` (build the dev container with `INSTALL_EXPORT_TEMPLATES=true`, or see `.github/workflows/ci.yml`). Smoke test: `export/linux/IsoDoom.x86_64 --headless --quit-after 60`.
- **CI:** `.github/workflows/ci.yml` runs build + test, then the export and smoke test, and uploads `export/linux/` as the `IsoDoom-linux-x86_64` artifact.

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

`wads/DOOM1.WAD` is the shareware v1.9 WAD (MD5 `f0cefca49926d00903cf57551d901abe`). Tests that need it skip themselves when the file is absent.

`wads/doom2.wad` (any Doom II version; set `ISODOOM_DOOM2_WAD=/path/to/DOOM2.WAD` to use one elsewhere) enables the Doom II tests, which skip themselves without it.

## References

- Vanilla source: `id-Software/DOOM` on GitHub (`linuxdoom-1.10`). Chocolate Doom covers vanilla-accurate fixes and OPL music (`i_oplmusic.c`).
- WAD and lump formats: the Doom Wiki and the *Unofficial Doom Specs*.
