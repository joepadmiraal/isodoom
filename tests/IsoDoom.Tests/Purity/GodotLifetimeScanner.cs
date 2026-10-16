using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace IsoDoom.Tests.Purity;

/// <summary>
/// T8.3d: scans the game assembly's IL for a Godot value that only a
/// temporary owns handed straight to Godot (SPEC §12 T8.3d,
/// <c>src/Render/GodotRefs.cs</c>). Godot's C# binding passes an argument's
/// native value bare and keeps nothing alive across the call, and these values
/// free their native data in a finalizer, so .NET's finalizer thread can free
/// one during the call that uses it (the heap corruption T8.3d found).
/// </summary>
/// <remarks>
/// A temporary is a non-scalar <c>Godot.Variant</c> made by a conversion, a
/// <c>GD.Load</c>/<c>ResourceLoader.Load</c> result, a new
/// <c>RefCounted</c> or Godot collection (after its object initializer's
/// setters), a <c>RefCounted</c> made by a Godot factory
/// (<c>Image.CreateFromData</c>, <c>ImageTexture.CreateFromImage</c>, …), or
/// (T8.3g) a new <c>StringName</c> or <c>NodePath</c>, the implicit
/// conversion from a <c>string</c> included;
/// it is reported when the next call it meets is a GodotSharp method. Also
/// reported: a null Godot collection argument (the binding makes its own
/// temporary for it) and <c>InputMap.ActionGetEvents</c> outside
/// <c>GodotRefs</c> (its array, left to the finalizer, unreferences the
/// events on the finalizer thread). Limits: a temporary followed by another
/// argument that calls a method is not followed past that call, and data
/// flow through locals is not tracked (a local's last use is as exposed:
/// <c>using</c> it is the rule, which this scan can't check).
/// </remarks>
public static class GodotLifetimeScanner
{
    /// <summary>Variant payloads that own no native data (godot_variant's inline types).</summary>
    private static readonly HashSet<string> _scalars = new(StringComparer.Ordinal)
    {
        "System.Boolean", "System.Char", "System.SByte", "System.Byte", "System.Int16", "System.UInt16",
        "System.Int32", "System.UInt32", "System.Int64", "System.UInt64", "System.Single", "System.Double",
        "Godot.Vector2", "Godot.Vector2I", "Godot.Vector3", "Godot.Vector3I", "Godot.Vector4", "Godot.Vector4I",
        "Godot.Rect2", "Godot.Rect2I", "Godot.Plane", "Godot.Quaternion", "Godot.Color", "Godot.Rid",
    };

    /// <summary>Scans <paramref name="module"/>'s methods (generated serialization code aside) and returns the violations, sorted.</summary>
    public static IReadOnlyList<string> Scan(ModuleDefinition module)
    {
        var violations = new SortedSet<string>(StringComparer.Ordinal);
        foreach (TypeDefinition type in module.GetTypes())
        {
            foreach (MethodDefinition method in type.Methods)
            {
                if (method.HasBody && !IsGenerated(method))
                    ScanMethod(method, violations);
            }
        }
        return [.. violations];
    }

    /// <summary>How many non-scalar Variants <paramref name="module"/> makes at all (so a clean scan is not a vacuous one).</summary>
    public static int CountVariantTemporaries(ModuleDefinition module) =>
        module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody && !IsGenerated(m))
            .Sum(m => m.Body.Instructions.Count(i => VariantPayload(i) is string p && !IsScalar(p, i)));

    /// <summary>T8.3g: how many <c>StringName</c>s and <c>NodePath</c>s <paramref name="module"/> makes at all (the cached ones included).</summary>
    public static int CountNameTemporaries(ModuleDefinition module) =>
        module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody && !IsGenerated(m))
            .Sum(m => m.Body.Instructions.Count(i => NamePayload(i) is not null));

    private static void ScanMethod(MethodDefinition method, SortedSet<string> violations)
    {
        Mono.Collections.Generic.Collection<Instruction> code = method.Body.Instructions;
        for (int i = 0; i < code.Count; i++)
        {
            Instruction ins = code[i];
            string? what = Temporary(ins);
            if (what is not null)
            {
                int next = ins.OpCode == OpCodes.Newobj ? SkipInitializer(code, i, ((MethodReference)ins.Operand).DeclaringType) : i + 1;
                MethodReference? consumer = NextCall(code, next);
                if (consumer is not null && IsGodotSharp(consumer.DeclaringType))
                    violations.Add($"{Where(method, ins)}: {what} handed straight to {consumer.DeclaringType.Name}.{consumer.Name}");
            }
            if (ins.Operand is MethodReference callee && IsGodotSharp(callee.DeclaringType))
            {
                if (callee.DeclaringType.FullName == "Godot.InputMap" && callee.Name == "ActionGetEvents"
                    && method.DeclaringType.FullName != "IsoDoom.Render.GodotRefs")
                    violations.Add($"{Where(method, ins)}: InputMap.ActionGetEvents outside GodotRefs.ActionEvents");
                if (callee.Parameters.Any(p => p.ParameterType.FullName.StartsWith("Godot.Collections.", StringComparison.Ordinal))
                    && NullArgument(code, i))
                    violations.Add($"{Where(method, ins)}: a null collection argument to {callee.DeclaringType.Name}.{callee.Name} (the binding makes a temporary)");
            }
        }
    }

    /// <summary>What <paramref name="ins"/> makes that only a temporary owns, or null.</summary>
    private static string? Temporary(Instruction ins)
    {
        if (VariantPayload(ins) is string payload)
            return IsScalar(payload, ins) ? null : $"a Variant of {payload}";
        if (ins.Operand is not MethodReference m)
            return null;
        if (NamePayload(ins) is string name)
            return name;
        TypeReference made = ins.OpCode == OpCodes.Newobj ? m.DeclaringType : ReturnType(m);
        if (ins.OpCode == OpCodes.Newobj)
        {
            if (made.FullName.StartsWith("Godot.Collections.", StringComparison.Ordinal) || IsRefCounted(made))
                return $"a new {made.Name}";
            return null;
        }
        if (ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt)
            return null;
        string declaring = m.DeclaringType.FullName;
        if ((declaring == "Godot.GD" || declaring == "Godot.ResourceLoader") && m.Name == "Load")
            return $"a loaded {made.Name}";
        // A Godot static factory's RefCounted (Image.CreateFromData, ImageTexture.CreateFromImage, ...): only ours.
        if (IsGodotSharp(m.DeclaringType) && !m.HasThis && m.Name.StartsWith("Create", StringComparison.Ordinal) && IsRefCounted(made))
            return $"a new {made.Name} ({m.DeclaringType.Name}.{m.Name})";
        return null;
    }

    /// <summary>
    /// T8.3g: a new <c>StringName</c> or <c>NodePath</c> at <paramref name="ins"/> (a constructor, or the
    /// implicit conversion from a <c>string</c>, which is one), described, or null. Their wrappers free
    /// their native data in a finalizer, and the binding passes them bare, as it does Variants.
    /// </summary>
    private static string? NamePayload(Instruction ins)
    {
        if (ins.Operand is not MethodReference m || m.DeclaringType.FullName is not ("Godot.StringName" or "Godot.NodePath"))
            return null;
        if (ins.OpCode == OpCodes.Newobj)
            return $"a new {m.DeclaringType.Name}";
        return m.Name == "op_Implicit" && ReturnType(m).FullName == m.DeclaringType.FullName
            ? $"a {m.DeclaringType.Name} from a {m.Parameters[0].ParameterType.Name}"
            : null;
    }

    /// <summary>The payload type of a <c>Godot.Variant</c> conversion at <paramref name="ins"/>, or null.</summary>
    private static string? VariantPayload(Instruction ins)
    {
        if (ins.Operand is not MethodReference m || m.DeclaringType.FullName != "Godot.Variant" || ReturnType(m).FullName != "Godot.Variant")
            return null;
        if (m.Name is not ("op_Implicit" or "From") && !m.Name.StartsWith("CreateFrom", StringComparison.Ordinal))
            return null;
        if (m is GenericInstanceMethod generic)
            return generic.GenericArguments[0].FullName;
        return m.Parameters.Count == 1 ? m.Parameters[0].ParameterType.FullName : null;
    }

    private static bool IsScalar(string payload, Instruction ins)
    {
        if (_scalars.Contains(payload))
            return true;
        // Variant.From<TEnum>: an enum is stored as an int.
        return ins.Operand is GenericInstanceMethod generic && Resolve(generic.GenericArguments[0])?.IsEnum == true;
    }

    /// <summary>The instruction after a <c>newobj</c>'s object initializer (<c>dup</c>, the value, <c>set_X</c> on the new object, repeated).</summary>
    private static int SkipInitializer(Mono.Collections.Generic.Collection<Instruction> code, int newobj, TypeReference type)
    {
        int j = SkipNops(code, newobj + 1);
        while (j < code.Count && code[j].OpCode == OpCodes.Dup)
        {
            int k = j + 1;
            while (k < code.Count && !(code[k].Operand is MethodReference s && s.Name.StartsWith("set_", StringComparison.Ordinal) && IsSameOrBase(type, s.DeclaringType)))
                k++;
            j = SkipNops(code, k + 1);
        }
        return j;
    }

    private static int SkipNops(Mono.Collections.Generic.Collection<Instruction> code, int j)
    {
        while (j < code.Count && code[j].OpCode == OpCodes.Nop)
            j++;
        return j;
    }

    /// <summary>The first method <paramref name="from"/> or a later instruction calls (or constructs), before a store, branch or return.</summary>
    private static MethodReference? NextCall(Mono.Collections.Generic.Collection<Instruction> code, int from)
    {
        for (int j = from; j < code.Count; j++)
        {
            Instruction ins = code[j];
            if (ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt || ins.OpCode == OpCodes.Newobj)
                return (MethodReference)ins.Operand;
            if (ins.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch or FlowControl.Return or FlowControl.Throw
                || ins.OpCode.Code.ToString().StartsWith("St", StringComparison.Ordinal))
                return null;
        }
        return null;
    }

    /// <summary>Whether a <c>ldnull</c> is among the arguments loaded for the call at <paramref name="call"/> (back to the previous call).</summary>
    private static bool NullArgument(Mono.Collections.Generic.Collection<Instruction> code, int call)
    {
        for (int j = call - 1; j >= 0 && call - j <= 12; j--)
        {
            if (code[j].OpCode.FlowControl == FlowControl.Call)
                return false;
            if (code[j].OpCode == OpCodes.Ldnull)
                return true;
        }
        return false;
    }

    private static TypeReference ReturnType(MethodReference m) =>
        m.ReturnType is GenericParameter { Type: GenericParameterType.Method } p && m is GenericInstanceMethod g
            ? g.GenericArguments[p.Position]
            : m.ReturnType;

    private static bool IsGodotSharp(TypeReference type) => type.Scope?.Name is "GodotSharp" or "GodotSharp.dll";

    private static bool IsRefCounted(TypeReference type)
    {
        for (TypeDefinition? t = Resolve(type); t is not null; t = t.BaseType is null ? null : Resolve(t.BaseType))
        {
            if (t.FullName == "Godot.RefCounted")
                return true;
        }
        return false;
    }

    private static bool IsSameOrBase(TypeReference type, TypeReference candidate)
    {
        for (TypeDefinition? t = Resolve(type); t is not null; t = t.BaseType is null ? null : Resolve(t.BaseType))
        {
            if (t.FullName == candidate.FullName)
                return true;
        }
        return false;
    }

    private static TypeDefinition? Resolve(TypeReference type)
    {
        try
        {
            return type.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return null;
        }
    }

    /// <summary>The Godot source generators' code (script serialization, ...), run only on an editor reload.</summary>
    private static bool IsGenerated(MethodDefinition method) =>
        method.DebugInformation.SequencePoints.FirstOrDefault()?.Document.Url is string url
        && url.Contains("Godot.SourceGenerators", StringComparison.Ordinal);

    private static string Where(MethodDefinition method, Instruction ins)
    {
        SequencePoint? point = null;
        for (Instruction? i = ins; i is not null && point is null; i = i.Previous)
            point = method.DebugInformation.GetSequencePoint(i);
        if (point is null)
            return method.FullName;
        string file = point.Document.Url.Replace('\\', '/');
        int src = file.IndexOf("/src/", StringComparison.Ordinal);
        return $"{(src >= 0 ? file[(src + 1)..] : file)}:{point.StartLine}";
    }
}
