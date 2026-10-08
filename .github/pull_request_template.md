<!-- See CONTRIBUTING.md. Delete the lines that don't apply. -->

**What and why:**

**Task:** <!-- e.g. T7.4 from docs/TASKS.md, or none -->

- [ ] `dotnet format IsoDoom.sln --verify-no-changes --severity warn`, `dotnet build IsoDoom.sln` and `dotnet test` pass
- [ ] The level check passes (`godot --headless -- --level-check -iwad …`), if `src/Game`, `src/Render`, `src/Audio` or the shaders changed
- [ ] `actionlint` passes, if `.github/` changed
- [ ] `src/Sim` stays deterministic (no `float`/`double`, Godot types, `System.Random`, wall-clock time or `Dictionary`/`HashSet` iteration), and vanilla deviations sit behind a tweak flag
- [ ] No WAD data, or anything made from it, is committed
- [ ] New commands or options are documented in `AGENTS.md`, and design decisions are logged in SPEC §12
