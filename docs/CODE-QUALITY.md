# Code quality tasks

Add style, formatting and analyzer checks that C# and Godot developers expect, and enforce them in CI. Each task is sized for one fresh Claude Code session. Start a session with: *"Do task Q3 from docs/CODE-QUALITY.md."*

The usual session workflow in `CLAUDE.md` applies, with one change: tick the task **here**, not in `TASKS.md`. Record design decisions in the decisions log, `SPEC.md` §12, and commit on a branch named after the task (e.g. `q3-editorconfig-format`).

Do the tasks in order. Q1, Q2 and Q6 are independent and can run at any time. Q3 and Q4 touch most files in the repo, so **merge every open branch into `main` before starting them**, or those branches will conflict everywhere.

Legend: `[ ]` todo, `[x]` done.

## Goals and ground rules

- **Follow mainstream conventions, not the current style.** The target is the [Godot C# style guide](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_style_guide.html), which builds on Microsoft's [.NET coding conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions). Where the two disagree, Godot's guide wins. Refactoring existing code to match is expected and welcome.
- **Exception: ported vanilla code keeps vanilla names.** The `CLAUDE.md` invariant still holds. Types such as `mobj_t` and `player_t`, functions such as `P_Random` and `A_Chase`, tables such as `mobjinfo`, and the lowercase public fields of ported structs (`mobj_t.momx`) are deliberately not .NET-style, so diffs against linuxdoom and Chocolate Doom stay easy. Naming rules must not flag them, and no task renames them. Formatting rules (whitespace, braces, usings, line breaks) do apply to ported code.
- **Use no third-party analyzers or tools.** Use only what the .NET SDK ships (`dotnet format`, the built-in Roslyn/.NET analyzers) plus `actionlint`, which is already pinned in the dev container. That avoids new license checks and dependency upkeep.
- **Make CI the gate.** Every check a contributor must pass runs in `.github/workflows/ci.yml`, and the same command is documented in `CLAUDE.md`'s *Commands*.
- **Change no behaviour.** All tests pass after every task, including the route, demo-sync and wipe tests where `wads/DOOM1.WAD` is present, and checksums stay identical. Run the level check (`godot --headless -- --level-check`) after any task that touches `src/Game`, `src/Render` or `src/Audio`.

Facts as of 2026-10-08: the repo has about 252 C# files and 87k lines. All files use file-scoped namespaces and spaces only. About 2,200 lines are longer than 120 characters. Private fields are mostly `_camelCase` (about 210, against 37 in plain `camelCase`). No script uses `[Export]`. `IsoDoom.sln` holds the Godot project (`IsoDoom.csproj`, Godot.NET.Sdk 4.7.2), Wad, Map, Sim, the tests and the two tools. `src/Directory.Build.props` only reaches the three libraries under `src/`. The test and tool projects repeat their settings.

---

- [x] **Q1 Warnings as errors in the Godot project.** `IsoDoom.csproj` is the only project without `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, so its warnings pile up unseen.
  1. Build with `dotnet build IsoDoom.csproj` and list the warnings it has today. As of 2026-10-08, CI's build and export jobs report two:
     - `src/Game/LevelScript.cs` (`joybutton`): `InputEventJoypadButton.Pressure` is obsolete, because the engine never sets it. Drop the assignment.
     - `src/Game/HudView.cs`: `HudView.Scale` hides `CanvasLayer.Scale`. Rename it (e.g. `HudScale`, or `PixelScale`) rather than adding `new`, because a node property that shadows Godot's is a trap for anyone reading it as the layer's transform. Update its users and the level check.
  2. Fix them in the code. Suppress one only when the fix would be wrong (for example, a warning from Godot's source generators), and then with a `<NoWarn>` entry or a `#pragma` that has a one-line reason.
  3. Enable `TreatWarningsAsErrors` in `IsoDoom.csproj`. Check that the `ExportRelease` configuration also builds, using the export command in `CLAUDE.md`, because Godot builds the project itself there.

  *Done when:* `dotnet build IsoDoom.sln` and the Linux export build with zero warnings, a deliberately added warning (an unused variable) fails the build, and all tests and the level check pass.

- [x] **Q2 Purity test: no Godot reference in the libraries.** SPEC §4 says Wad, Map and Sim never reference Godot. Today that holds only because their `.csproj` files lack the reference. Nothing would catch someone adding it.
  1. In `tests/IsoDoom.Tests/Purity/`, add a test that loads the compiled `IsoDoom.Wad`, `IsoDoom.Map` and `IsoDoom.Sim` assemblies with Mono.Cecil (as `SimPurityTests` does) and fails if any of them references `GodotSharp`, `GodotSharpEditor`, `Godot.SourceGenerators` or any assembly or namespace starting with `Godot`.
  2. Also fail if Sim references any assembly besides the BCL and `IsoDoom.Map`/`IsoDoom.Wad`. Use an allow-list, so a new dependency is a conscious decision.

  *Done when:* the test passes, and it fails when a `Godot` `PackageReference` is temporarily added to `IsoDoom.Sim.csproj` (revert that afterwards).

- [x] **Q3 `.editorconfig` and a one-time reformat.** Add a repo-root `.editorconfig` that encodes the Godot/.NET conventions. Reformat the whole codebase to it, and gate it in CI.
  1. **Write `.editorconfig`.** Read the Godot C# style guide and the .NET conventions linked above, and confirm each rule against them rather than from memory. Cover at least:
     - Files and whitespace: `root = true`, UTF-8 without BOM, LF, a final newline, trimmed trailing whitespace, 4-space indent for `*.cs`, and the usual indents for `*.csproj`/`*.props`/`*.json`/`*.yml`/`*.gd`/`*.tscn`.
     - Braces and layout: Allman braces (`csharp_new_line_before_open_brace = all`) and `else`/`catch`/`finally` on new lines.
     - Usings: `System` first, outside the namespace. File-scoped namespaces.
     - `var`: when the type is apparent (follow the Godot guide).
     - Expression-bodied members, pattern matching and null checks: the IDE defaults.
     - Line length: Godot recommends 100 characters. Roslyn can't enforce this, so set `max_line_length` for editors and leave existing long lines (mostly comments and tables) alone unless `dotnet format` wraps them.
     - Naming: `PascalCase` types, methods, properties, events, constants and public fields; `_camelCase` private fields; `camelCase` locals and parameters; `I`-prefixed interfaces. Leave the naming rules at `suggestion` for now. Q4 makes them enforced.
  2. **Generated and vendored code.** Exclude `.godot/`, `bin/`, `obj/`, `export/`, `doom_20230531/` and anything under `tools/VanillaRef`/`tools/OplRef` that isn't ours with `generated_code = true` sections. Check what `doom_20230531/` is first; it may need to be gitignored instead.
  3. **Reformat.** Run `dotnet format whitespace IsoDoom.sln`, then `dotnet format style IsoDoom.sln --severity info`, and check whether the Godot project restores and formats under `dotnet format`. Review the diff for anything semantic (`dotnet format style` can apply code fixes). Keep only fixes that are pure style, and revert any that touch Sim's arithmetic or evaluation order.
  4. **Commit in two parts.** First a commit with just `.editorconfig`, then the mechanical reformat in its own commit. Add the reformat commit's hash to a new `.git-blame-ignore-revs` file, and mention in `CLAUDE.md` that `git config blame.ignoreRevsFile .git-blame-ignore-revs` hides it from blame.
  5. **CI gate.** Add a step to the build job in `ci.yml` after restore: `dotnet format IsoDoom.sln --verify-no-changes --no-restore --severity warn`. Add the local command to `CLAUDE.md`'s *Commands* (`dotnet format IsoDoom.sln` fixes, `--verify-no-changes` checks).

  *Done when:* `dotnet format IsoDoom.sln --verify-no-changes` passes, all tests pass with unchanged checksums and the level check passes, CI has the format step (`actionlint` clean), and the decisions are in SPEC §12.

- [x] **Q4 Naming: enforce the conventions outside ported code.** Rename the non-ported code to the Q3 naming rules and make those rules errors, without touching vanilla names.
  1. **Classify the code.** A file is *ported* when it ports vanilla or Chocolate Doom logic and cites its source (`// p_enemy.c` and the like). That covers most of `src/Sim/`, plus files in `src/Game`, `src/Audio` and `src/Render` such as `FFinale`, `MMenu`, `SSound`, the status bar and the intermission. Grep for the citations and list the result.
  2. **Choose the mechanism.** The simplest option is `.editorconfig` sections, where file globs for ported code set `dotnet_diagnostic.IDE1006.severity = none` and the rest set it to `warning`. Partial classes such as `World.*.cs` that mix ported and new members may need `[SuppressMessage]` on the ported members or a split into ported and new files. Pick the option that keeps the ported files diffable, and record it in SPEC §12.
  3. **Rename the non-ported code.** Use Roslyn renames (the IDE's code fix, or `dotnet format style --diagnostics IDE1006`) rather than text replacement. Before renaming anything public in `src/Game`, `src/Render` or `src/Audio`, check that the name isn't used from outside C#: `.tscn`/`.tres` files (script properties, signal connections), `project.godot`, input actions, `user://settings.cfg` keys, the save format (`SaveGame.cs`, `World.SaveG.cs`: names must not change what is written), and `--level-script` commands. Leave those strings unchanged even when the C# identifier changes.
  4. If the rename is too large for one session, split it by area (libraries and tools; Game; Render, Audio and Assets; tests). Do the first part, then add the rest here as Q4a, Q4b and so on.
  5. Set `EnforceCodeStyleInBuild` (or rely on Q3's `dotnet format` step) so naming violations fail the build or CI.

  *Done when:* the naming rules are `warning` or `error` for non-ported code, `dotnet build` and `dotnet format --verify-no-changes` pass, no ported vanilla identifier was renamed, existing saves and settings files still load (save on `main` before the change, load after), and all tests and the level check pass.

  *Done:* `IDE1006` at warning; ported files exempt by glob at the end of `.editorconfig` (all of `src/Sim` but five files of our own, five Audio and eight Game files), 19 vanilla names in our own files marked with `[SuppressMessage]`, 155 declarations renamed with Roslyn's renamer, nothing split off. `EnforceCodeStyleInBuild` is left to Q5 (see its step 1); CI's format step is the gate meanwhile. Decisions in SPEC §12 (Q4).

- [x] **Q5 Built-in .NET analyzers.** Turn on the SDK's code-quality analyzers (`CAxxxx`) and code-style analyzers in the build, at a level the codebase can hold with warnings as errors.
  1. Add a repo-root `Directory.Build.props` with the shared settings (Q4 left `EnforceCodeStyleInBuild` to this step: it makes IDE0005, a warning since Q3, demand `GenerateDocumentationFile`, which brings the XML doc warnings, three ambiguous `cref`s (CS0419) as of Q4; fix them or decide how to treat CS15xx/CS17xx here): `AnalysisLevel` set to `latest`, `AnalysisMode` starting at `Recommended`, `EnforceCodeStyleInBuild`, `Nullable`, `LangVersion` and `TreatWarningsAsErrors`. Remove the duplicates from the test and tool `.csproj` files. Make `src/Directory.Build.props` import the root one (`<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />`). Confirm the Godot project (repo root) still builds and exports with the new props.
  2. Build and triage the warnings by rule ID. For each rule, fix the code, or set it to `suggestion`/`none` in `.editorconfig` with a comment saying why. Expected `none` rules for this project:
     - `CA1707` (underscores) and `CA1711`/`CA1716`/`CA1720` in ported code.
     - `CA1051` (public fields) for ported structs.
     - `CA5394` (insecure randomness): the game uses vanilla's `P_Random`/`M_Random` tables by design.
     - Globalization rules (`CA1303`/`CA1304`/`CA1305`/`CA1307`/`CA1309`/`CA1310`) only where the fix adds noise. `CultureInfo.InvariantCulture` in text parsing and formatting (WAD names, settings, saves, scripts) is a real fix; keep those.
     - Performance rules that would change Sim's evaluation order must not be auto-fixed. Fix them by hand, and only with the route and demo-sync tests green.
     - Q3 left some IDE rules at their default `suggestion` without applying them (SPEC §12, Q3): IDE0059 (dead stores), IDE0060 (unused parameters), IDE0066 (switch expressions), IDE0180 (tuple swaps), IDE0042 (deconstruction), IDE0220 (foreach casts), and IDE0031 (null propagation) in `src/Sim`. Before `EnforceCodeStyleInBuild` or a severity raise makes any of them count, decide each one: `none` for ported code (by path or with the Q4 classification) with a reason, or fix by hand where it is a real improvement.
  3. Raise `AnalysisMode` to `All` only if what remains is small. Otherwise record the level chosen and why in SPEC §12.
  4. If the warning count is too large for one session, enable the analyzers per project (tools and tests first, then Wad and Map, then Sim, then the Godot project). Do the first part and add the rest here as Q5a, Q5b and so on.

  *Done when:* every project builds with zero warnings at the chosen analysis level and warnings as errors, each disabled rule has a reason in `.editorconfig`, and all tests (checksums unchanged), the level check and the Linux export pass.

  *Done:* a root `Directory.Build.props` (framework, language, nullable, implicit usings off, deterministic, warnings as errors, `AnalysisLevel` latest, `AnalysisMode` **Recommended**, `EnforceCodeStyleInBuild`, `GenerateDocumentationFile` with the XML kept out of publishing) reaches every project; `src/Directory.Build.props` imports it and keeps only the libraries' `ExportRelease` optimisation, and the test, tool and Godot project files lost their copies (the Godot project keeps `TargetFramework`, which Godot's editor reads). Of 3,773 findings (plus 3,647 missing doc comments), all were triaged in one session: most were vanilla names and fields (CA1707, CA1051), turned off by the Q4 globs for ported code; the rest were fixed by hand (culture-invariant parsing and formatting, ordinal comparisons, public fields of our own types to properties, the Godot input overrides' `@event`, bad `cref`s, dead stores, unused parameters) or turned off with a reason in `.editorconfig`. `All` stays off (about 1,100 more findings, Q5a). Decisions in SPEC §12 (Q5).

- [ ] **Q5a Selected rules from `AnalysisMode` All.** Q5 stayed at `Recommended`: `All` adds about 1,100 findings (as of Q5: CA1062 403, CA2007 183, CA2000 117, CA1515 105, CA1307 52, CA2213 51, CA1819 39, CA1002 25, CA1308 21, CA1034 19, CA1033 14, CA1815 12, CA1814 12, CA1052 11, CA1823 10, CA1508 9, CA1032 8, CA5394 7, CA1030 7, and a few singles), most of them noise for this project: CA1062 (null checks on public arguments: nullable reference types already cover them), CA2007 (`ConfigureAwait`: Godot's continuations must come back to the main thread), CA1515 (internal types: Godot's node scripts and xunit's test classes are public), CA5394 (vanilla's random tables are the game's, not security).
  1. Turn on, one at a time at `warning` in `.editorconfig`, the rules that find real defects or real clean-ups: likely CA1307/CA1308/CA1309 (string comparison and casing), CA1823 (unused private fields), CA1508 (dead conditions), CA2000/CA2213 (disposal: check each against Godot's ownership of nodes and resources, which the scene tree frees), CA1052 (static holder types), CA1819 (array properties) outside ported code.
  2. Fix them, or set them `none` with a reason, as Q5 did; the ported files' sections keep vanilla code untouched.
  3. Record which `All` rules were taken and why in SPEC §12.

  *Done when:* every rule turned on builds with zero warnings, each one rejected has its reason in `.editorconfig` or SPEC §12, and all tests (checksums unchanged) and the level check pass.

- [x] **Q6 `actionlint` and up-to-date actions in CI.** The dev container pins `actionlint`, but nothing runs it on pull requests. The workflow's actions also age unnoticed: as of 2026-10-08, GitHub warns that `actions/upload-artifact@v5` targets the deprecated Node.js 20 runtime.
  1. Bump every action in `ci.yml` to its current major version that runs on Node.js 24: `upload-artifact`, and check `checkout`, `setup-dotnet` and `cache` too. Read each action's release notes for breaking changes (`upload-artifact` and `cache` have changed behaviour across majors).
  2. Add `.github/dependabot.yml` with the `github-actions` ecosystem (weekly), so future bumps arrive as pull requests. Dependabot is part of GitHub, so it is not a new dependency.
  3. Add a small job (or an early step in the build job) to `ci.yml` that installs the same `actionlint` version as `.devcontainer/Dockerfile`, checks its SHA-256 the same way, and runs it. It needs `shellcheck` on the runner (preinstalled on `ubuntu-latest`; verify).
  4. Keep the version in one place if practical (a comment in each pointing to the other is acceptable).
  5. Fix anything it reports.

  *Done when:* `actionlint` passes locally, the CI job runs it and fails on a deliberately broken workflow (try it locally, then revert), a CI run shows no Node.js deprecation annotation, Dependabot is configured, and `CLAUDE.md`'s CI paragraph mentions both.

- [x] **Q6a Confirm Q6 on GitHub.** Q6 could not run CI, so it checked the Node.js runtime only from each action's `action.yml` (`runs.using: node24` at `checkout@v7`, `setup-dotnet@v6`, `cache@v6` and `upload-artifact@v7`).
  1. On the first CI run of the Q6 branch, check that the *Lint workflows* job passes, that it printed the runner's `shellcheck` version, and that the run's summary shows no Node.js deprecation annotation.
  2. Push a commit with a deliberately broken workflow (e.g. `${{ github.nonexistent }}` in a `run:`) and check that the job fails, then drop it.
  3. Check that Dependabot is enabled for the repository (Insights → Dependency graph → Dependabot) and that it reads `.github/dependabot.yml` without errors.
  4. When an actionlint release knows the inputs of `checkout@v7`, `cache@v6` and `setup-dotnet@v6` (1.7.12 doesn't: SPEC §12, Q6), bump `ACTIONLINT_VERSION`/`ACTIONLINT_SHA256` in `.devcontainer/Dockerfile`.

  *Done when:* items 1–3 have been checked on GitHub.

  *Done:* items 1–3 checked on GitHub: the *Lint workflows* job passes with the runner's `shellcheck` version printed and no Node.js deprecation annotation, Dependabot reads `.github/dependabot.yml`, and a branch with `${{ github.nonexistent }}` in a `run:` failed the job (branch dropped). Item 4 moved to Q6b: as of 2026-10-08, 1.7.12 is still actionlint's latest release.

- [ ] **Q6b Bump actionlint when it knows the current actions.** Dependabot doesn't cover `actionlint`: it is pinned in `.devcontainer/Dockerfile`, which CI reads.
  1. When an actionlint release knows the inputs of `checkout@v7`, `cache@v6` and `setup-dotnet@v6` (1.7.12 doesn't: SPEC §12, Q6), bump `ACTIONLINT_VERSION`/`ACTIONLINT_SHA256` in `.devcontainer/Dockerfile` (the SHA-256 of `actionlint_VERSION_linux_amd64.tar.gz` from the release's checksums file).
  2. Rebuild the dev container and fix anything the new version reports.

  *Done when:* `actionlint` at the new version passes locally and in CI.

- [x] **Q7 Contributor docs.** Once Q1–Q6 are done, make the checks discoverable for outside contributors.
  1. Add `CONTRIBUTING.md` covering the toolchain (dev container or `global.json`'s SDK plus Godot 4.7.2 .NET), the checks a PR must pass and the commands to run them locally (`dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`, `actionlint`, the level check), the style (Godot C# guide, with the vanilla-names exception and why), the Sim invariants (pointing to `CLAUDE.md` and SPEC §6.1), and that WAD data never goes in the repo.
  2. Link it from `README.md`.
  3. Optionally add `.github/pull_request_template.md` with a short checklist.

  *Done when:* a contributor following `CONTRIBUTING.md` on a fresh clone can run every CI check locally, and the docs match `ci.yml`.

  *Done:* `CONTRIBUTING.md` (toolchain, game data, the CI checks with their local commands in `ci.yml`'s order, style and the vanilla-names exception, the Sim invariants, pull requests and the licence), linked from `README.md`, and `.github/pull_request_template.md`. Every command was run on a fresh clone of the branch; the export steps were not (no export templates locally), and the exported-build checks are offered as the same checks in the editor under Xvfb + lavapipe. `ci.yml`'s header and `AGENTS.md` say to keep it in sync. Decisions in SPEC §12 (Q7).
