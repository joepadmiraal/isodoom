using System;
using System.Collections.Generic;
using Godot;

namespace IsoDoom.Render;

/// <summary>
/// T8.3d: Godot values handed to native code outlive the call. Godot's C#
/// binding passes a <see cref="Variant"/> argument as its bare native value
/// and keeps nothing alive across the call, and a <see cref="Variant"/> that
/// owns native data (a string, a packed array, an object, a collection: all
/// but the scalars) frees it in a finalizer. So a temporary
/// <see cref="Variant"/> (an implicit conversion in an argument or an array
/// store) can be finalized by .NET's finalizer thread while the engine copies
/// it: a packed array freed under <c>godotsharp_variant_new_copy</c> had its
/// refcount bumped in freed memory, the heap corruption T8.3d chased
/// (<c>malloc_consolidate(): unaligned fastbin chunk detected</c> at a level
/// build). The rule (SPEC §12 T8.3d): a <see cref="Variant"/>, a
/// <c>Godot.Collections</c> collection, a <see cref="RefCounted"/> wrapper
/// or (T8.3g) a <see cref="StringName"/> or <see cref="NodePath"/> (a
/// <c>string</c> converted to one included: go through
/// <see cref="StringNames.Get"/>, or <c>using</c>) made or returned for one
/// call is disposed (or, for a wrapper we keep,
/// held) on the main thread after its last use, never left to the finalizer
/// while Godot may use it; a read-back <see cref="Image"/> or a
/// <see cref="SurfaceTool"/> in a local is kept alive to its scope's end
/// instead (<see cref="Keep"/>). Main thread only.
/// </summary>
public static class GodotRefs
{
    private static readonly Dictionary<string, Shader> _shaders = new(StringComparer.Ordinal);

    /// <summary>
    /// Stores <paramref name="value"/> in an <see cref="ArrayMesh"/> surface's
    /// arrays, then disposes it (the array holds its own copy).
    /// </summary>
    public static void SetSlot(this Godot.Collections.Array arrays, Mesh.ArrayType slot, Variant value)
    {
        using (value)
            arrays[(int)slot] = value;
    }

    /// <summary>
    /// <see cref="ArrayMesh.AddSurfaceFromArrays"/> of triangles, with empty
    /// blend shapes and LODs of ours, disposed after the call: for a null one
    /// the binding makes a temporary collection itself, which the finalizer
    /// thread could free during the call (a <c>Dictionary::_unref</c> there
    /// was T8.3d's other abort).
    /// </summary>
    public static void AddSurface(this ArrayMesh mesh, Godot.Collections.Array arrays, Mesh.ArrayFormat flags = 0)
    {
        var blendShapes = new Godot.Collections.Array<Godot.Collections.Array>();
        using var blendShapesArray = (Godot.Collections.Array)blendShapes; // the typed array's own, disposable
        using var lods = new Godot.Collections.Dictionary();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, blendShapes, lods, flags);
    }

    /// <summary>
    /// <see cref="InputMap.ActionGetEvents"/>, its array disposed: left to the
    /// finalizer, it unreferenced its events on the finalizer thread, racing the
    /// main thread's GC-handle swaps of the same events inside Godot's binding
    /// (<c>Handle is not initialized</c> in <c>SwapGCHandleForType</c>).
    /// </summary>
    public static List<InputEvent> ActionEvents(string action)
    {
        Godot.Collections.Array<InputEvent> events = InputMap.ActionGetEvents(StringNames.Get(action));
        using var array = (Godot.Collections.Array)events; // the typed array's own, disposable
        return [.. events];
    }

    /// <summary>
    /// Keeps <paramref name="value"/>'s wrapper alive to the end of a
    /// <c>using</c> scope without disposing it: in an optimized build a local
    /// is dead after its last read, so a <see cref="RefCounted"/> only a local
    /// owns (a read-back <see cref="Image"/>, a <see cref="SurfaceTool"/>)
    /// could be finalized, and freed, during its last native call. For these
    /// locals the rule's <c>using</c> of the wrapper itself was measured worse
    /// (SPEC §12 T8.3d): disposing them, mostly the dummy renderer's read-back
    /// images, which Godot also holds and returns as the same object each
    /// call, came with a corrupt strong GC handle under
    /// <c>ISODOOM_GC_STRESS</c>, 7 Doom II checks in 450, against 0 in 450
    /// kept alive.
    /// </summary>
    public static Kept Keep(GodotObject? value) => new(value);

    /// <summary><see cref="Keep"/>'s scope: its end reads the value, so the wrapper lives until then.</summary>
    public readonly struct Kept(GodotObject? value) : IDisposable
    {
        public void Dispose() => GC.KeepAlive(value);
    }

    /// <summary>
    /// The shader at <paramref name="path"/>, loaded once and held for the
    /// run. Godot's <c>ResourceCache</c> keeps a loaded resource by a bare
    /// pointer, so a shader whose last material went away had only its C#
    /// wrapper left: <c>GD.Load</c> taking it back from the cache raced the
    /// finalizer thread freeing that wrapper (and its shader), and a shader
    /// loaded straight into a material's setter could be freed during the
    /// call. Held here, no shader wrapper is ever finalized.
    /// </summary>
    public static Shader LoadShader(string path)
    {
        if (!_shaders.TryGetValue(path, out Shader? shader))
            _shaders[path] = shader = GD.Load<Shader>(path);
        return shader;
    }
}
