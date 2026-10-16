# Handoff: T8.3d (heap corruption under load), in progress

You are continuing task **T8.3d** from `docs/TASKS.md` (read it, and SPEC §12's T8.3b and T8.3d rows). Branch: `t8.3d-heap-corruption`. Follow `CLAUDE.md`'s session workflow. This file is a handoff note: delete it before the final commit.

## Where things stand

**The cause was found and fixed in our code.** About 1 Doom II level check in 50–100 of the release export (10 at a time) aborted at a map load with `malloc_consolidate(): unaligned fastbin chunk detected`. A Godot 4.7.2 release template built with symbols caught it under gdb, in `ArrayMesh::add_surface_from_arrays`, every time. Meanwhile .NET's finalizer thread was freeing either a `Variant`'s packed array (`PackedArrayRefBase::destroy`) or a `Dictionary` (`Dictionary::_unref`).

The mechanism: Godot's C# binding passes a `Variant` argument as its bare `NativeVar`, with no `GC.KeepAlive`, and a non-scalar `Variant` frees its data in a finalizer. So in `arrays[(int)Mesh.ArrayType.Vertex] = c.Vertices.ToArray()`, the temporary's packed array can be freed during `godotsharp_variant_new_copy`, whose refcount increment then writes into a freed chunk. A null `blendShapes`/`lods` passed to `AddSurfaceFromArrays` likewise becomes the binding's own temporary `new Array<Array>()`/`new Dictionary()`. Collections left to the finalizer (`InputMap.ActionGetEvents`' arrays) also unreference their objects on the finalizer thread, racing the binding's unlocked GC-handle swap (`Handle is not initialized`; upstream godotengine/godot#112067).

**The fix (all uncommitted on the branch, and it builds):**
- `src/Render/GodotRefs.cs` (new) holds the rule's documentation and helpers: `SetSlot` (mesh array slot, disposes the Variant), `AddSurface` (passes empty blend shapes and LODs of ours, disposed), `ActionEvents` (disposes the array) and `LoadShader` (shaders cached for the run).
- `StringNames.SetParam` and `LevelMesh.SetParameter` dispose their `Variant`.
- Every `GD.Load<Shader>` goes through `LoadShader`; the mesh builders (LevelMesh, ItemBeams, MonsterMarkers, ThingSprites) use `SetSlot`/`AddSurface`.
- These are now `using` or held: the settings' and the IWAD config's `ConfigFile`s and string Variants, `DefaultBindings`, `Main.Version`, the level script's input events (`Parse`), the level check's events (`Held`), the labels' `LabelSettings`, inline materials and meshes, `WipeView`'s shader, the check's read-back `Image`s, and the `SurfaceTool`s.
- `tests/IsoDoom.Tests/Purity/GodotLifetimeScanner.cs` and `GodotLifetimeTests.cs` (new) run Cecil over the editor build's `.godot/mono/temp/bin/Debug/IsoDoom.dll` and fail on a temporary handed straight to a GodotSharp method. They found 73 such sites on the old build and 0 now.
- `AGENTS.md` got an invariant stating the rule, `docs/SPEC.md` a §12 row for T8.3d, and `docs/TASKS.md` has T8.3d ticked with a Done entry plus a new T8.3e (report upstream; a person has to file it). **The SPEC row and the TASKS entry contain the placeholders `RUNS_STRESS` and `RUNS_LOAD`: fill them in from the final numbers below.**

**Checks passed on the current code:** `dotnet build`, `dotnet test` (2808: 2797 passed, 11 skipped, the OPL C harness), `dotnet format --verify-no-changes --severity warn`, the headless level check (DOOM1, synthetic), the headless viewer check, and the windowed level check under Xvfb + lavapipe. The windowed viewer check failed once in 7 runs, while the machine was heavily loaded; its message wasn't captured and it passed all 6 retries.

## Measurements so far (Doom II level checks, `-iwad wads/doom2.wad`)

| Build | Load | Result |
|---|---|---|
| old code, official template | 10 parallel, `ISODOOM_GC_STRESS=1` | 7 aborts / 100, 3 "Handle is not initialized" |
| old code, symbol template | 6 parallel, stress | 4 aborts / 120 (all at add_surface_from_arrays) |
| new code (minus last fixes), symbol template | 6 parallel, stress | 0 / 361 |
| new code (minus last fixes), official | 10 parallel, stress | 1 abort / 100 (`malloc(): unaligned fastbin chunk` at MAP04 load, no symbols), earlier batch also 1 SIGSEGV / 100 |
| **final code, official** | **10 parallel, no stress** | **100 / 100 OK** (and an earlier, nearly final batch: 100 / 100) |
| final code, symbol template | 8 parallel, stress | 200 / 200 clean |
| final code, official | 10 parallel, stress | **3 hangs / 300**, 0 aborts |

Under stress, every run fails the 1 s load budget (SPEC §9). That's expected: count a run as clean when its only failures are `loading took N ms, over the 1000 ms budget` lines plus the `slowest map` line (failure count = budget lines + 1).

## What is left

1. **The hang (only under `ISODOOM_GC_STRESS=1`, official template, about 1 in 100).** All 3 hangs came at the same point of the check: the cheats page, right after `idmus` ("Music Change") and the first `iddt from the cheats page, step 1` line. The gdb dump: the "GC stress" thread is running inside libcoreclr (a GC in progress, not waiting), the main thread and the "IsoDoom music" thread wait on the same futex (waiting for the GC to end), and the finalizer is idle. It looks like a GC stuck in the collection itself, possibly a corrupt managed heap. `dotnet-stack` got no answer from the runtime. It was never seen without stress, nor on the symbol template.
   - **Next step: an A/B test** to learn whether the last round of changes caused it. Run the pre-final build (the code before the read-back-Image/SurfaceTool `using`s; rebuild it from `git stash`/a worktree, or just revert those hunks in `LevelCheck*.cs`, `LevelScript.cs`, `WadViewerCheck.cs`, `AimMarker.cs` and `FlashGlow.cs`) against the final build, interleaved, about 150 stressed runs each, with the watchdog harness below. Earlier, the pre-final official build had 0 hangs in about 300 stressed runs.
   - Get coreclr symbols (`dotnet tool install -g dotnet-symbol; dotnet-symbol --symbols --timeout 14 -o DATA_DIR DATA_DIR/libcoreclr.so`; it timed out once at 240 s) so gdb names the GC's functions. Sample the hung GC thread more than once to tell a spin from a loop.
   - If the hang is pre-existing and appears only under the artificial 2 ms collect-and-wait stress, log it in SPEC §12 and as a follow-up task rather than blocking T8.3d. Its *Done when* says "100 loaded runs are clean", and they are.
2. Fill in `RUNS_STRESS`/`RUNS_LOAD` in `docs/SPEC.md` (the T8.3d row) and `docs/TASKS.md` (the T8.3d Done entry), mentioning the hang finding.
3. Run everything again (`dotnet build IsoDoom.sln`, `dotnet test`, `dotnet format IsoDoom.sln --verify-no-changes --severity warn`, the level and viewer checks), then commit on `t8.3d-heap-corruption` with the attribution line from the system prompt. Check that the docs' line counts weren't truncated.

## NUC setup

- The repo: fetch the branch (the user pushes it from the laptop). Copy the IWADs into `wads/` (`DOOM1.WAD`, `doom2.wad`, and optionally `doom.wad`, `TNT.WAD`, `PLUTONIA.WAD`). Install the Godot 4.7.2 .NET editor and export templates (`~/.local/share/godot/export_templates/4.7.2.stable.mono/`; the dev container's Dockerfile has them with `INSTALL_EXPORT_TEMPLATES=true`). Install gdb.
- Export: `godot --headless --import` once, then `mkdir -p export/linux && godot --headless --export-release "Linux" export/linux/IsoDoom.x86_64`.
- **Symbol template** (about 25 min on 12 cores): `git clone --depth 1 --branch 4.7.2-stable https://github.com/godotengine/godot.git ~/godot-src`, install scons (or unpack scons 4.9.1's source and run `PYTHONPATH=~/scons-src python3 -c "import SCons.Script; SCons.Script.main()"`) and `pkg-config`, then:
  `scons -j12 platform=linuxbsd target=template_release module_mono_enabled=yes debug_symbols=yes x11=no wayland=no vulkan=no opengl3=no alsa=no pulseaudio=no udev=no dbus=no speechd=no fontconfig=no accesskit=no`
  To use it, make a folder holding an export's `IsoDoom.pck` and `data_IsoDoom_linuxbsd_x86_64/`, and **hardlink** (not symlink: Godot finds its pck through `/proc/self/exe`) `bin/godot.linuxbsd.template_release.x86_64.mono` there as `IsoDoom.x86_64`.

### The watchdog harness (`rundog.sh`)

```bash
#!/bin/bash
# rundog.sh N [STRESS]: one Doom II level check of $EXE under gdb (stops on SIGABRT/SIGSEGV); a run
# still going after $LIMIT s (default 900) gets SIGINT, so gdb dumps every thread. Log: $OUTDIR/N.log
N=$1
cd /path/to/isodoom
export ISODOOM_GC_STRESS=${2:-}
gdb -q -batch -nx \
  -ex "set pagination off" -ex "set confirm off" \
  -ex "handle all nostop noprint pass" -ex "handle SIGABRT stop print" -ex "handle SIGSEGV stop print" -ex "handle SIGINT stop print nopass" \
  -ex run -ex "info threads" -ex "thread apply all bt 60" -ex kill \
  --args "$EXE" --headless -- --level-check -iwad wads/doom2.wad \
  > "$OUTDIR/$N.log" 2>&1 &
gdbpid=$!
for ((i = 0; i < ${LIMIT:-900}; i++)); do kill -0 $gdbpid 2>/dev/null || break; sleep 1; done
if kill -0 $gdbpid 2>/dev/null; then
  echo "WATCHDOG: still running after ${LIMIT:-900} s" >> "$OUTDIR/$N.log"
  game=$(pgrep -P $gdbpid -x IsoDoom.x86_64 | head -1)
  [ -n "$game" ] && TMPDIR=/tmp timeout 120 ~/.dotnet/tools/dotnet-stack report -p $game > "$OUTDIR/$N.stack" 2>&1
  kill -INT $gdbpid
  for ((i = 0; i < 120; i++)); do kill -0 $gdbpid 2>/dev/null || break; sleep 1; done
  kill -9 $gdbpid 2>/dev/null
fi
wait $gdbpid
echo "run $N exit $?" >> "$OUTDIR/$N.log"
```

Run a batch with: `OUTDIR=$PWD/runs EXE=/path/IsoDoom.x86_64 seq 1 150 | xargs -P 10 -I{} ./rundog.sh {} 1` (drop the `1` for no stress). Then: `grep -l "received signal\|WATCHDOG\|Handle is not" runs/*.log`.

## Pitfalls hit so far

- **Don't `pkill -f`/`pgrep -f` on a pattern that appears in your own command line**: it kills your own shell (exit 144). Use `pgrep -x IsoDoom.x86_64`.
- `ptrace_scope` is 1, so gdb can't attach to a running process: launch it under gdb. Core dumps may go to the host's systemd-coredump. The exports' `createdump` lacks its exec bit (T8.3c).
- gdb must *stop* on SIGSEGV, or crashes pass unnoticed. The level check raises no spurious SIGSEGV.
- A process that aborts with the official template then hangs (its crash handler deadlocks in malloc), so the watchdog catches it either way.
- The windowed checks: `WAYLAND_DISPLAY= VK_DRIVER_FILES=/usr/share/vulkan/icd.d/lvp_icd.json xvfb-run -a -s "-screen 0 1280x720x24" godot -- --level-check -iwad wads/DOOM1.WAD` (and `--viewer-check`).
- Timing tests in `dotnet test` can fail on a loaded machine: run the suite when no batch is running.
- In the Release export, a local is dead after its last read, so `var x = new RefCounted…(); godotCall(x);` is as exposed as an inline temporary. `GodotLifetimeTests` can't see locals: use `using` (or a field) by review.
