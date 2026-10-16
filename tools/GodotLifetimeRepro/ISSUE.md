# Draft: upstream issue for godotengine/godot (T8.3e)

To file at <https://github.com/godotengine/godot/issues/new?template=bug_report.yml>
(a bug report; the form's fields are the headings below), with
`GodotLifetimeRepro.zip` attached (`README.md` says how to make it). Searched on
2026-10-16: no issue covers the arguments. #124016 is the same class of bug for
`this` (`Array.Count`). The handle-swap race is the open #112067: the comment for it
is at the end.

---

**Title:** C#: generated bindings don't keep arguments alive across the native call; a GC during the call frees them (heap corruption in `AddSurfaceFromArrays`)

**Tested versions**

- Reproducible in v4.7.2.stable.mono.official [ed1daf0bf] (Linux x86_64 release export, .NET 8.0.31), JIT and ReadyToRun (`PublishReadyToRun`).
- Not tested on master. Debug builds hide it (the JIT keeps locals alive to the end of their method).

**System information**

Linux x86_64 (glibc), 8 cores, headless (`--headless`, dummy renderer).

**Issue description**

The generated engine method bindings hand each argument to the `godot_icall_*` as a bare native value read out of its managed wrapper. Nothing keeps the wrapper reachable afterwards. `ArrayMesh.AddSurfaceFromArrays` in 4.7.2's `GodotSharp.dll` (decompiled):

```cs
public void AddSurfaceFromArrays(PrimitiveType primitive, Godot.Collections.Array arrays,
    Array<Godot.Collections.Array> blendShapes = null, Dictionary lods = null, ArrayFormat flags = 0)
{
    NativeCalls.godot_icall_5_206(MethodBind7, GodotObject.GetPtr(this), (int)primitive,
        (godot_array)(arrays ?? new Godot.Collections.Array()).NativeValue,
        (godot_array)(blendShapes ?? new Array<Godot.Collections.Array>()).NativeValue,
        (godot_dictionary)(lods ?? new Dictionary()).NativeValue, (int)flags);
}
```

In optimized code a wrapper is unreachable once its `NativeValue` has been read. A GC during the call queues its finalizer, and the finalizer thread then runs `Array.Dispose(false)` / `Dictionary.Dispose(false)` while the engine is still using the value. As the last reference, that frees the `ArrayPrivate`/`DictionaryPrivate` under `add_surface_from_arrays`. Three forms of it:

1. **The binding's own temporaries**: with `blendShapes`/`lods` left null, the binding makes `new Array<Array>()` and `new Dictionary()` that nothing but the call holds. The caller can't protect these.
2. **A collection argument**: `arrays`, unless the caller adds `GC.KeepAlive(arrays)` after the call.
3. **No second thread needed**: in the plain code (`natural` below, with `--no-gc`), the binding's `new Array<Array>()` for the null `blendShapes` is allocated *after* `arrays`' pointer has been read. When that allocation triggers a GC, the finalizer thread frees `arrays` while `add_surface_from_arrays` runs. Plain single-threaded code that builds meshes crashes this way.

The same pattern applies, by the same code, to object arguments (`GodotObject.GetPtr(x)`, a `RefCounted` that only the argument holds) and to `Variant` arguments (`value.NativeVar`; a non-scalar `Variant` frees its data in its `Disposer`'s finalizer). That includes the hand-written `Array` indexer setter, `*ptr = value.CopyNativeVariant()`, which an implicit conversion such as `arrays[(int)Mesh.ArrayType.Vertex] = vertices` feeds a temporary. The MRP didn't catch the `Variant` form on its own (0 in 20 runs, below); its window is one short native call.

Symptoms: `ERROR: Condition "p_arrays.size() != ARRAY_MAX" is true.` (a freed array's size read as garbage; the surface is refused), then glibc's heap checks abort (`malloc_consolidate(): unaligned fastbin chunk detected`, `malloc(): unsorted double linked list corrupted`, `corrupted double-linked list`), or a SIGSEGV. A 4.7.2 `template_release` built with `debug_symbols=yes`, under gdb, case `defaults`:

```
Thread 1 "repro.x86_64" received signal SIGABRT  (malloc(): unsorted double linked list corrupted)
#13 RenderingServer::mesh_create_surface_data_from_arrays (..., p_arrays=..., p_blend_shapes=..., p_lods=..., p_compress_format=0) at servers/rendering/rendering_server.cpp:1298
#14 ArrayMesh::add_surface_from_arrays (..., p_arrays=..., p_blend_shapes=..., p_lods=..., p_flags=...) at scene/resources/mesh.cpp:1826
#17 MethodBindT<Mesh::PrimitiveType, Array const&, TypedArray<Array> const&, Dictionary const&, BitField<Mesh::ArrayFormat> >::ptrcall (...) at ./core/object/method_bind_common.h:108
#18 ?? (managed)

Thread 17 ".NET Finalizer"
#2  _int_free (...) at ./malloc/malloc.c:4644
#4  memdelete<DictionaryPrivate> (...) at ./core/os/memory.h:166
#5  Dictionary::_unref (this=...) at core/variant/dictionary.cpp:360
#6  ?? (managed: the binding's `lods` temporary's finalizer)
```

We found this in a game (a Doom engine port, GPL) that builds level meshes with `AddSurfaceFromArrays` from C#. Its release export failed 5 times in 100 full level checks of Doom II (32 map loads each, 10 checks at a time, no forced GCs): 3 missing surfaces (`ARRAY_MAX`) and 2 heap-corruption aborts. With the symbols build, gdb put every abort in `ArrayMesh::add_surface_from_arrays`, the finalizer thread in `PackedArrayRefBase::destroy` or `Dictionary::_unref`. Our workaround is to keep every argument alive (`GC.KeepAlive` or `using` after the call, explicit disposed `blendShapes`/`lods`, `Variant`s made and disposed explicitly). Since then: 0 aborts in 100 such checks.

**Suggested fix:** in the generated code, keep each argument's managed owner alive until the call returns, and dispose the temporaries the binding makes itself:

```cs
var arraysArg = arrays ?? new Godot.Collections.Array();
var blendShapesArg = blendShapes ?? new Array<Godot.Collections.Array>();
var lodsArg = lods ?? new Dictionary();
NativeCalls.godot_icall_5_206(MethodBind7, GodotObject.GetPtr(this), (int)primitive,
    (godot_array)arraysArg.NativeValue, (godot_array)blendShapesArg.NativeValue,
    (godot_dictionary)lodsArg.NativeValue, (int)flags);
GC.KeepAlive(arraysArg); GC.KeepAlive(blendShapesArg); GC.KeepAlive(lodsArg);
// and dispose blendShapesArg/lodsArg when they were made here
```

Likewise `GC.KeepAlive` for `GetPtr(x)` arguments (and `this`, as #124016 suggests) and an internal keep-alive of a `Variant` argument's disposer, in the `NativeCalls` icalls and in the hand-written collection and `Variant` code that passes `NativeValue`/`NativeVar` to native functions.

**Steps to reproduce**

The MRP's one script, `Main.cs`, builds 100,000 `ArrayMesh`es of 1–512 triangles with `AddSurfaceFromArrays` while a background thread loops on `GC.Collect(); GC.WaitForPendingFinalizers();`. It checks each mesh's surface count and vertex count, then exits 0 if all are right.

```sh
godot --headless --path . --import
mkdir -p out && godot --headless --path . --export-release Linux out/repro.x86_64
out/repro.x86_64 --headless -- --case=defaults     # aborts within a second or two
out/repro.x86_64 --headless -- --case=fixed        # OK: 100000 meshes as built
```

`--case=` picks which temporaries are left unprotected; all else is kept alive with `GC.KeepAlive`:

- `natural`: the plain code, nothing kept alive;
- `variant`: only the indexer store's implicit `Variant`;
- `array`: only the `arrays` argument;
- `defaults`: only the binding's own `blendShapes`/`lods`;
- `fixed`: none.

`--no-gc` drops the collecting thread. `ISODOOM_R2R=1` in the environment at export sets `PublishReadyToRun`.

Results, 10 runs each (exit codes 134 = abort, 139 = SIGSEGV, 132 = SIGILL):

| case | JIT export | ReadyToRun export |
|---|---|---|
| `natural` | 10 crashed (5 with `ARRAY_MAX` first) | 10 crashed (4 with `ARRAY_MAX`) |
| `array` | 10 crashed (3 with `ARRAY_MAX`) | 10 crashed (4 with `ARRAY_MAX`) |
| `defaults` | 10 aborted | 10 aborted |
| `variant` | 0 crashed | 0 crashed |
| `fixed` | 1 SIGSEGV (see below) | 0 crashed |
| `natural --no-gc` | 3 of 3 crashed (9 of 12 more under gdb) | 3 of 3 crashed |
| `defaults --no-gc` | 0 of 3 | 0 of 3 |

`fixed` crashed once in 110 JIT runs (10 here, 40 more and 60 under gdb with symbols, all clean). It was a bare SIGSEGV with no output, so its cause is open; it may be #124016 (`Array.Count`/`Resize` on `this`).

**Minimal reproduction project (MRP)**

GodotLifetimeRepro.zip (attached)

---

## Comment for #112067

> Another data point, from a 4.7.2 release export (Linux x86_64) under a thread forcing `GC.Collect(); GC.WaitForPendingFinalizers()` every 2 ms. In a full run of our game's level checks, `Handle is not initialized` in `ScriptManagerBridge.SwapGCHandleForType` was logged in 3 runs of 100, from unrelated calls (`InputMap.ActionGetEvents`, `ResourceLoader.Load`, `GodotObject.Finalize`). The typed arrays `InputMap.ActionGetEvents` returned were left to the finalizer, which unreferenced their `InputEvent`s on the finalizer thread while the main thread's calls on the same events went through `_instance_binding_reference_callback` (`csharp_script.cpp`). That function clears the binding's handle and calls `SwapGCHandleForType` without any lock shared with `godotsharp_internal_refcounted_disposed` on the finalizer thread. Disposing those arrays on the main thread made the error go away (0 in 300 runs). A minimal loop over `InputMap.ActionGetEvents` alone didn't reproduce it (0 in 3 × 300,000 calls). Related: #(the new issue), where arguments are freed by the finalizer during the native call.
