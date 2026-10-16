# Godot C# binding lifetime reproducer (T8.3e)

A standalone Godot 4.7.2 .NET project, not part of `IsoDoom.sln`, for the upstream
report of T8.3b/T8.3d's bug: Godot's generated C# bindings keep no argument alive
across the native call, so a GC on another thread can finalize, and free, what the
engine is reading. The issue text is in `ISSUE.md`.

`Main.cs` builds `ArrayMesh`es in a loop (`AddSurfaceFromArrays`) while a thread runs
`GC.Collect(); GC.WaitForPendingFinalizers()`, then checks each mesh. Run it from a
release export (the editor's Debug build keeps locals alive to the end of their
method, which hides the bug):

```sh
cd tools/GodotLifetimeRepro
godot --headless --path . --import
mkdir -p out && godot --headless --path . --export-release Linux out/repro.x86_64
out/repro.x86_64 --headless -- --case=defaults      # aborts (heap corruption) within a second or two
out/repro.x86_64 --headless -- --case=fixed         # OK: 100000 meshes as built
# ReadyToRun, as IsoDoom's exports (T8.3a), where the Array argument fails too:
mkdir -p out-r2r && ISODOOM_R2R=1 godot --headless --path . --export-release Linux out-r2r/repro.x86_64
out-r2r/repro.x86_64 --headless -- --case=array
```

Cases (`--case=`): `natural` (the plain code), `variant`, `array`, `defaults` (each
exposing one temporary only), `fixed` (all kept alive with `GC.KeepAlive`); `--no-gc`
drops the collecting thread (the control), `--iterations=N`, `--triangles=N`. Exit
code 0 when every mesh came out as built. Results are in `ISSUE.md`.

To attach it to the issue: `git archive --format=zip -o GodotLifetimeRepro.zip HEAD:tools/GodotLifetimeRepro`
from the repo root (the project alone, no IsoDoom code; GPL-2.0-or-later as the repo,
and it holds no WAD data).
