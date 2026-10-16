using System.Collections.Generic;
using System.IO;
using System.Linq;
using IsoDoom.Tests.Support;
using Mono.Cecil;
using Xunit;

namespace IsoDoom.Tests.Purity;

/// <summary>
/// T8.3d: the game assembly hands Godot no value that only a temporary owns
/// (<see cref="GodotLifetimeScanner"/>, SPEC §12 T8.3d). It reads the editor
/// build's <c>IsoDoom.dll</c> (<c>dotnet build IsoDoom.sln</c>, which CI runs
/// before the tests) and skips without one.
/// </summary>
public class GodotLifetimeTests
{
    private static string? GameAssembly =>
        TestWads.RepoRoot is string root && Path.Combine(root, ".godot", "mono", "temp", "bin", "Debug", "IsoDoom.dll") is var path
            && File.Exists(path)
            ? path
            : null;

    private static ModuleDefinition ReadGame()
    {
        string? path = GameAssembly;
        Assert.SkipWhen(path is null, "no .godot/mono/temp/bin/Debug/IsoDoom.dll: run `dotnet build IsoDoom.sln` first");
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(path)!); // GodotSharp.dll beside it
        return ModuleDefinition.ReadModule(path, new ReaderParameters { ReadSymbols = true, AssemblyResolver = resolver });
    }

    [Fact]
    public void NoTemporaryIsHandedToGodot()
    {
        using ModuleDefinition module = ReadGame();
        IReadOnlyList<string> violations = GodotLifetimeScanner.Scan(module);
        Assert.True(violations.Count == 0,
            "Godot values only a temporary owns are handed to Godot, which the finalizer thread can free during the call " +
            "(SPEC §12 T8.3d: dispose them after the call, src/Render/GodotRefs.cs). " +
            $"{violations.Count} violation(s):\n  " + string.Join("\n  ", violations.Take(200)));
    }

    [Fact]
    public void TheScanSeesTheVariants()
    {
        // The shader parameters and mesh arrays make non-scalar Variants (passed to GodotRefs' helpers):
        // a scan that finds none is looking at the wrong thing.
        using ModuleDefinition module = ReadGame();
        Assert.True(GodotLifetimeScanner.CountVariantTemporaries(module) >= 20);
    }
}
