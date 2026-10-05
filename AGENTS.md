# IsoDoom

An isometric top-down remake of Doom in Godot 4 + C#. It loads the original WAD at runtime. The full design is in `SPEC.md`. Read only the sections your task cites.

## Session workflow

Each session completes **one task** from `TASKS.md`:

1. Read the task and the `SPEC.md` sections it cites.
2. Do the work. The task is done when its *Done when* criterion holds and all tests pass.
3. In `TASKS.md`: tick the task, and add any follow-up tasks you discovered under the right milestone.
4. When you make a design decision the spec doesn't cover, add it to the decisions log in `SPEC.md` §12.
5. Commit on a branch named after the task (e.g. `t1.2-wad-reader`).

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

## References

- Vanilla source: `id-Software/DOOM` on GitHub (`linuxdoom-1.10`). Chocolate Doom covers vanilla-accurate fixes and OPL music (`i_oplmusic.c`).
- WAD and lump formats: the Doom Wiki and the *Unofficial Doom Specs*.
