# Contributing to IsoDoom

Thanks for helping. IsoDoom is an isometric remake of Doom in Godot 4 and C#. It loads the original game data from your own WAD at runtime. This page covers what you need to send a pull request that passes CI. The full design is in [`docs/SPEC.md`](docs/SPEC.md), the work queue in [`docs/TASKS.md`](docs/TASKS.md), and every command and invariant in [`AGENTS.md`](AGENTS.md) (`CLAUDE.md` links to the same file).

## Toolchain

The easiest route is the **dev container** (`.devcontainer/`). Open the repo in VS Code with the Dev Containers extension, or use any devcontainer-compatible tool. It has the .NET SDK, Godot, `actionlint`, `shellcheck`, Xvfb and Mesa's software Vulkan driver. To export builds, build it with `INSTALL_EXPORT_TEMPLATES=true`.

To set up without the container (Linux x86_64 is what CI and the scripts assume), install:

- **The .NET SDK** named in [`global.json`](global.json): 10.0.401, or a later 10.0 feature band.
- **Godot 4.7.2 stable, .NET edition** ("mono", not the standard build), as `godot` on your `PATH`. The version must match `IsoDoom.csproj`'s `Godot.NET.Sdk`.
- Optional: **actionlint 1.7.12** with **shellcheck**, to lint the workflow (the version and SHA-256 are pinned in `.devcontainer/Dockerfile`). You also need **xvfb-run** and **mesa-vulkan-drivers** (lavapipe) for the checks with a real renderer, and the 4.7.2 .NET **export templates** to export.

## Game data

**No WAD data goes in the repo**: no WAD files, and no images, sounds or dumps made from them. `wads/` is gitignored. Put your own copy there:

- `wads/DOOM1.WAD` (the shareware v1.9 WAD) turns on the tests that need real data. Without it they skip themselves, as in CI.
- `wads/doom2.wad` turns on the Doom II tests.

CI uses a generated **synthetic IWAD** instead. It has no id data, and you can make one yourself (see below).

## Checks a pull request must pass

CI ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) runs the steps below. Run them from the repo root before you push. On a fresh clone, run them in this order.

```bash
# Lint the workflow (actionlint also runs shellcheck on its scripts).
actionlint

# Formatting and code style (.editorconfig), naming included.
# `dotnet format IsoDoom.sln` fixes most of what it reports (rename by hand).
dotnet format IsoDoom.sln --verify-no-changes --severity warn

# Build: warnings are errors, and the SDK's analyzers run in the build.
dotnet build IsoDoom.sln

# Tests (WAD tests skip without wads/DOOM1.WAD).
dotnet test

# The game checks in the Godot editor, headless, on the synthetic IWAD.
# Import once on a fresh clone, after the build.
godot --headless --import
dotnet run --project tools/SyntheticIwad -- /tmp/isodoom-synthetic.wad
godot --headless -- --viewer-check -iwad /tmp/isodoom-synthetic.wad   # ends "WAD viewer check: OK"
godot --headless -- --level-check -iwad /tmp/isodoom-synthetic.wad    # ends "Level check: OK"
godot --headless --fixed-fps 35 -- -iwad /tmp/isodoom-synthetic.wad --level E1M1 \
  --level-tweaks=vanilla --level-monsters=off \
  --level-script="route tests/IsoDoom.Tests/Sim/Routes/synthetic-exit.route; gamestate intermission; skip; map E1M2 1; route tests/IsoDoom.Tests/Sim/Routes/synthetic-secret-exit.route; map E1M2 2; skip; gamestate demoscreen"
```

Each command exits non-zero on a failure.

CI then exports the Linux build and smoke-tests it, and exports the Windows build and runs its smoke test, both checks and the route play headless on a Windows runner (through `IsoDoom.console.exe`), and the same for the macOS build on an Apple Silicon and an Intel macOS runner. It also runs both checks again on the exported build with a real renderer (Xvfb and lavapipe), which compares the drawn pixels. To run the same checks locally without exporting, use the editor:

```bash
WAYLAND_DISPLAY= VK_DRIVER_FILES=/usr/share/vulkan/icd.d/lvp_icd.json \
  xvfb-run -a -s "-screen 0 1280x720x24" \
  godot --audio-driver Dummy -- --level-check -iwad /tmp/isodoom-synthetic.wad
```

The output should report `drawn pixels compared`. Use `--viewer-check` the same way. Run these checks when you change `src/Game`, `src/Render`, `src/Audio` or the shaders. To export, see *Export* in `AGENTS.md`. To run the checks against your own WAD, swap `-iwad wads/DOOM1.WAD` in for the synthetic one.

## Code style

- **The [Godot C# style guide](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_style_guide.html)**, which builds on Microsoft's [.NET conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions), is encoded in [`.editorconfig`](.editorconfig). That means Allman braces, 4 spaces, file-scoped namespaces, `System` usings first, `PascalCase` for types and members, `_camelCase` for private fields, and `camelCase` for locals and parameters. Your editor picks the rules up, and `dotnet format` applies them.
- **The analyzers** (the SDK's own `CAxxxx` and `IDExxxx` rules at `AnalysisMode` Recommended, set in [`Directory.Build.props`](Directory.Build.props)) run in every build, and warnings are errors. Fix a finding rather than suppressing it. If a suppression really is right, give it a one-line reason. The rules turned off project-wide are listed in `.editorconfig`, each with its reason.
- **Ported vanilla code keeps vanilla names.** Code ported from linuxdoom or Chocolate Doom keeps names such as `P_Random`, `A_Chase`, `mobj_t`, `mobjinfo` and `mobj_t.momx`, with a comment citing its source file (`// p_enemy.c`). This keeps diffs against the original easy to read. The naming rules skip the ported files listed at the end of `.editorconfig`. If you add a ported file, add it to that list. A vanilla name in one of our own files gets `[SuppressMessage("Style", "IDE1006", Justification = "Vanilla name (file.c)")]`. Formatting rules apply to ported code too.
- **Deviations from vanilla behaviour** sit behind a named tweak flag (SPEC §6.3), so pure vanilla behaviour stays testable.
- `git config blame.ignoreRevsFile .git-blame-ignore-revs` hides the one-time reformat from `git blame`.

## The simulation is deterministic

`src/Sim` must give the same result on every machine, so lockstep co-op stays possible. The rules are in `AGENTS.md` (*Invariants*) and [SPEC §6.1](docs/SPEC.md#61-simulation-core):

- pure C#, with no Godot types (`Purity` tests check this);
- fixed-point or BAM maths only, with no `float` or `double`;
- no `System.Random`, no wall-clock time, and no iteration over `Dictionary` or `HashSet`;
- the only input is one `ticcmd` per player per tic.

A change to the sim must keep the route, demo-sync and wipe tests passing tic for tic. With `wads/DOOM1.WAD` present, `dotnet test` compares the sim with vanilla's own dumps.

## Pull requests

- Keep each pull request to one change, and say which task in `docs/TASKS.md` it does, if any.
- Record a design decision the spec doesn't cover in the decisions log, [SPEC §12](docs/SPEC.md#12-decisions-log).
- When you add a command, a check or an option, document it in `AGENTS.md`'s *Commands*.
- **Licence:** IsoDoom is GPL-2.0-or-later, and contributions are accepted under the same licence. Porting id Software or Chocolate Doom code is fine. Before you add any other dependency, check that its licence is compatible; code that ships goes in `THIRD-PARTY-NOTICES.md`.
