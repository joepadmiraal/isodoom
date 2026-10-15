using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Godot;

namespace IsoDoom.Game;

/// <summary>
/// T8.3: compiles the sim's code ahead of its first call. The JIT compiles a
/// method at its first call, so the first tics that take a new path (the
/// first tics of the first map, a monster's first attack, a first death)
/// took up to 6–10 ms (SPEC §12 T4.9, T8.3). At start, a background thread
/// asks the runtime to compile (<see cref="RuntimeHelpers.PrepareMethod(RuntimeMethodHandle)"/>)
/// every non-generic method of the plain assemblies (Sim, Map, Wad) and of
/// the presentation's types that run with each tic. Nothing of them runs: the
/// sim, its random index and its sounds never see it, so it cannot change a
/// checksum. A tic that calls a method being compiled waits for that one
/// compilation, as it would have compiled it itself.
/// </summary>
public static class JitWarmup
{
    private static int _started;

    /// <summary>The presentation's types whose code runs with each tic (the status bar, the message line, the sounds, the ticcmds).</summary>
    private static readonly Type[] _gameTypes =
    [
        typeof(StStuff), typeof(HuStuff), typeof(TiccmdBuilder), typeof(GameFlow), typeof(Audio.SSound),
    ];

    /// <summary>Methods compiled and the time taken, once done (else null).</summary>
    public static (int Methods, double Ms)? Result { get; private set; }

    /// <summary>Starts the warm-up thread (once per process).</summary>
    public static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;
        var thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "JIT warm-up" };
        thread.Start();
    }

    private static void Run()
    {
        long start = Stopwatch.GetTimestamp();
        int methods = 0;
        foreach (Assembly assembly in new[] { typeof(Sim.World).Assembly, typeof(Map.Level).Assembly, typeof(Wad.WadArchive).Assembly })
        {
            foreach (Type type in assembly.GetTypes())
                methods += Prepare(type);
        }
        foreach (Type type in _gameTypes)
        {
            methods += Prepare(type);
            foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                methods += Prepare(nested);
        }
        Result = (methods, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        GD.Print($"JIT warm-up: {methods} methods compiled in {Result.Value.Ms:F0} ms (in the background)");
    }

    // Compiles every method and constructor declared by type (none of a generic type: they need type arguments).
    private static int Prepare(Type type)
    {
        if (type.ContainsGenericParameters)
            return 0;
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        int n = 0;
        foreach (MethodBase method in type.GetMethods(All))
            n += Prepare(method);
        foreach (MethodBase method in type.GetConstructors(All))
            n += Prepare(method);
        return n;
    }

    private static int Prepare(MethodBase method)
    {
        if (method.IsAbstract || method.ContainsGenericParameters
            || (method.MethodImplementationFlags & (MethodImplAttributes.InternalCall | MethodImplAttributes.Runtime)) != 0)
            return 0;
        try
        {
            RuntimeHelpers.PrepareMethod(method.MethodHandle);
            return 1;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or InvalidOperationException or TypeLoadException or BadImageFormatException)
        {
            return 0; // (a method the runtime cannot compile ahead: it compiles at its first call as before)
        }
    }
}
