using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IsoDoom.Tests.Support;
using Mono.Cecil;
using Mono.Cecil.Cil;
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

    [Fact]
    public void TheScanSeesTheNames()
    {
        // T8.3g: StringNames.Get, GameInput's actions and the level's sector names make StringNames (kept or disposed).
        using ModuleDefinition module = ReadGame();
        Assert.True(GodotLifetimeScanner.CountNameTemporaries(module) >= 3);
    }

    [Fact]
    public void AStringHandedAsANameIsFlagged()
    {
        // T8.3g: a string converted to a StringName or NodePath (or a new one) straight into a Godot method is
        // reported; one stored in a field first is not. Built as IL against the game's GodotSharp.dll.
        using ModuleDefinition game = ReadGame();
        TypeDefinition stringName = GodotType(game, "Godot.StringName"), nodePath = GodotType(game, "Godot.NodePath");
        TypeDefinition control = GodotType(game, "Godot.Control"), node = GodotType(game, "Godot.Node"), inputMap = GodotType(game, "Godot.InputMap");
        using var module = ModuleDefinition.CreateModule("NameProbe", new ModuleParameters { Kind = ModuleKind.Dll, AssemblyResolver = game.AssemblyResolver });
        var type = new TypeDefinition("Probe", "Names", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(type);
        var cache = new FieldDefinition("Cached", FieldAttributes.Static | FieldAttributes.Public, module.ImportReference(stringName));
        type.Fields.Add(cache);
        MethodReference Import(TypeDefinition t, string name, params string[] parameters) =>
            module.ImportReference(t.Methods.Single(m => m.Name == name && !m.HasGenericParameters
                && m.Parameters.Select(q => q.ParameterType.FullName).SequenceEqual(parameters)));
        MethodReference toStringName = module.ImportReference(stringName.Methods.Single(m => m.Name == "op_Implicit" && m.ReturnType.FullName == "Godot.StringName"));
        MethodReference toNodePath = module.ImportReference(nodePath.Methods.Single(m => m.Name == "op_Implicit" && m.ReturnType.FullName == "Godot.NodePath"));
        MethodReference newStringName = Import(stringName, ".ctor", "System.String");
        MethodReference themeOverride = Import(control, "AddThemeConstantOverride", "Godot.StringName", "System.Int32");
        MethodReference getNode = Import(node, "GetNode", "Godot.NodePath");
        MethodReference hasAction = Import(inputMap, "HasAction", "Godot.StringName");

        void Method(string name, TypeReference parameter, Action<ILProcessor> body)
        {
            var method = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition(parameter));
            type.Methods.Add(method);
            ILProcessor il = method.Body.GetILProcessor();
            body(il);
            il.Emit(OpCodes.Ret);
        }
        Method("Theme", module.ImportReference(control), il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "separation");
            il.Emit(OpCodes.Call, toStringName);
            il.Emit(OpCodes.Ldc_I4_8);
            il.Emit(OpCodes.Callvirt, themeOverride);
        });
        Method("Path", module.ImportReference(node), il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "Child");
            il.Emit(OpCodes.Call, toNodePath);
            il.Emit(OpCodes.Callvirt, getNode);
            il.Emit(OpCodes.Pop);
        });
        Method("Action", module.TypeSystem.String, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Newobj, newStringName);
            il.Emit(OpCodes.Call, hasAction);
            il.Emit(OpCodes.Pop);
        });
        Method("Cache", module.TypeSystem.String, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, toStringName);
            il.Emit(OpCodes.Stsfld, cache);
            il.Emit(OpCodes.Ldsfld, cache);
            il.Emit(OpCodes.Call, hasAction);
            il.Emit(OpCodes.Pop);
        });

        IReadOnlyList<string> violations = GodotLifetimeScanner.Scan(module);
        Assert.Collection(violations,
            v => Assert.Contains("Names::Action(System.String): a new StringName handed straight to InputMap.HasAction", v, StringComparison.Ordinal),
            v => Assert.Contains("Names::Path(Godot.Node): a NodePath from a String handed straight to Node.GetNode", v, StringComparison.Ordinal),
            v => Assert.Contains("Names::Theme(Godot.Control): a StringName from a String handed straight to Control.AddThemeConstantOverride", v, StringComparison.Ordinal));
    }

    private static TypeDefinition GodotType(ModuleDefinition game, string name) =>
        game.AssemblyResolver.Resolve(game.AssemblyReferences.Single(r => r.Name == "GodotSharp")).MainModule.GetType(name);
}
