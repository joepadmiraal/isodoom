using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Godot;

// Reproducer: Godot's generated C# bindings keep no argument alive across the
// native call. A Variant argument is passed as its bare native value, a
// collection as its NativeValue, and a null optional collection becomes a
// temporary the binding makes itself, so in optimized code (a release export)
// the managed owner is unreachable during the call, and a GC on another
// thread can finalize it, freeing what the engine is reading.
//
// Run (release export): out/repro.x86_64 --headless -- [--case=CASE] [--iterations=N] [--triangles=N] [--no-gc]
// (--triangles=N: each mesh has 1 to N triangles, default 512)
//   CASE  natural   the plain code: all three temporaries exposed (default)
//         variant   only the Variant temporary in `arrays[slot] = Vector3[]`
//         array     only the Array argument of AddSurfaceFromArrays
//         defaults  only the binding's own temporaries for null blendShapes/lods
//         fixed     everything kept alive with GC.KeepAlive: expected clean
// A thread runs GC.Collect(); GC.WaitForPendingFinalizers() in a loop meanwhile
// (--no-gc: none, the control).
// Exits 0 if every mesh came out as built, 1 otherwise (or crashes).
public partial class Main : Node
{
    private static volatile bool _stop;

    public override void _Ready()
    {
        string mode = "natural";
        int iterations = 100_000;
        int maxTriangles = 512;
        bool stress = true;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--case=", StringComparison.Ordinal))
                mode = arg["--case=".Length..];
            else if (arg.StartsWith("--iterations=", StringComparison.Ordinal))
                iterations = int.Parse(arg["--iterations=".Length..]);
            else if (arg == "--no-gc")
                stress = false;
            else if (arg.StartsWith("--triangles=", StringComparison.Ordinal))
                maxTriangles = int.Parse(arg["--triangles=".Length..]);
        }
        GD.Print($"case {mode}, {iterations} meshes of up to {maxTriangles} triangles, {OS.GetName()}, Godot {Engine.GetVersionInfo()["string"]}, .NET {System.Environment.Version}, debug build: {OS.IsDebugBuild()}");

        var gc = new Thread(() =>
        {
            while (!_stop)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }) { IsBackground = true };
        if (stress)
            gc.Start();

        int bad = Meshes(iterations, maxTriangles, mode);

        _stop = true;
        if (stress)
            gc.Join();
        GD.Print(bad == 0 ? $"OK: {iterations} meshes as built" : $"FAILED: {bad} of {iterations} meshes wrong");
        GetTree().Quit(bad == 0 ? 0 : 1);
    }

    private static int Meshes(int iterations, int maxTriangles, string mode)
    {
        int bad = 0;
        for (int i = 0; i < iterations; i++)
        {
            int vertices = 3 * (1 + i % maxTriangles);
            using var mesh = new ArrayMesh();
            Build(mesh, vertices, mode);
            if (mesh.GetSurfaceCount() != 1 || mesh.SurfaceGetArrayLen(0) != vertices)
            {
                if (bad++ < 10)
                    GD.PrintErr($"mesh {i}: {mesh.GetSurfaceCount()} surfaces, {(mesh.GetSurfaceCount() > 0 ? mesh.SurfaceGetArrayLen(0) : 0)} vertices, expected 1 and {vertices}");
            }
        }
        return bad;
    }

    private static void Build(ArrayMesh mesh, int count, string mode)
    {
        switch (mode)
        {
            case "natural": Natural(mesh, count); break;
            case "variant": OnlyVariant(mesh, count); break;
            case "array": OnlyArray(mesh, count); break;
            case "defaults": OnlyDefaults(mesh, count); break;
            case "fixed": Fixed(mesh, count); break;
            default: throw new ArgumentException($"unknown case {mode}");
        }
    }

    // How one would write it: nothing is kept alive.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Natural(ArrayMesh mesh, int count)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = Vertices(count); // a temporary Variant
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays); // arrays, and the binding's own blendShapes and lods
    }

    // Only the implicit Variant in the indexer's store is exposed.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void OnlyVariant(ArrayMesh mesh, int count)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = Vertices(count);
        var blendShapes = new Godot.Collections.Array<Godot.Collections.Array>();
        var lods = new Godot.Collections.Dictionary();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, blendShapes, lods);
        GC.KeepAlive(arrays);
        GC.KeepAlive(blendShapes);
        GC.KeepAlive(lods);
    }

    // Only the Array argument of AddSurfaceFromArrays is exposed.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void OnlyArray(ArrayMesh mesh, int count)
    {
        var blendShapes = new Godot.Collections.Array<Godot.Collections.Array>();
        var lods = new Godot.Collections.Dictionary();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, Arrays(count), blendShapes, lods);
        GC.KeepAlive(blendShapes);
        GC.KeepAlive(lods);
    }

    // Only the binding's own temporaries for the null blendShapes and lods are exposed.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void OnlyDefaults(ArrayMesh mesh, int count)
    {
        Godot.Collections.Array arrays = Arrays(count);
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        GC.KeepAlive(arrays);
    }

    // Everything kept alive across the call: expected clean.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Fixed(ArrayMesh mesh, int count)
    {
        Godot.Collections.Array arrays = Arrays(count);
        var blendShapes = new Godot.Collections.Array<Godot.Collections.Array>();
        var lods = new Godot.Collections.Dictionary();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, blendShapes, lods);
        GC.KeepAlive(arrays);
        GC.KeepAlive(blendShapes);
        GC.KeepAlive(lods);
    }

    // A surface's arrays with the vertices stored through a Variant kept alive.
    private static Godot.Collections.Array Arrays(int count)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        Variant vertices = Vertices(count);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        vertices.Dispose();
        return arrays;
    }

    private static Vector3[] Vertices(int count)
    {
        var v = new Vector3[count];
        for (int i = 0; i < count; i++)
            v[i] = new Vector3(i, i % 3, -i);
        return v;
    }
}
