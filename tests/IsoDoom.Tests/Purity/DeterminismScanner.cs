using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace IsoDoom.Tests.Purity;

/// <summary>
/// Scans compiled IL for constructs the deterministic sim must not use
/// (SPEC §6.1): floating point, <c>System.Random</c>, wall-clock time and
/// iteration over <c>Dictionary</c>/<c>HashSet</c>.
/// </summary>
/// <remarks>
/// Every type in the module is scanned, compiler-generated ones included
/// (lambdas, iterators, async state machines, records), because their IL runs
/// as part of the sim too. Type references are unwrapped through arrays,
/// pointers, by-refs, modifiers, generic instances and function pointers.
/// Limits: an unordered collection passed as a plain <c>IEnumerable</c>
/// (e.g. to LINQ) is not detected; that needs data-flow analysis.
/// </remarks>
public static class DeterminismScanner
{
    /// <summary>Types whose mere use is forbidden, with the reason.</summary>
    private static readonly Dictionary<string, string> ForbiddenTypes = new()
    {
        ["System.Single"] = "float",
        ["System.Double"] = "double",
        ["System.Half"] = "floating point (Half)",
        ["System.MathF"] = "floating point (MathF)",
        ["System.Numerics.Vector2"] = "floating point (Vector2)",
        ["System.Numerics.Vector3"] = "floating point (Vector3)",
        ["System.Numerics.Vector4"] = "floating point (Vector4)",
        ["System.Numerics.Quaternion"] = "floating point (Quaternion)",
        ["System.Numerics.Plane"] = "floating point (Plane)",
        ["System.Numerics.Matrix3x2"] = "floating point (Matrix3x2)",
        ["System.Numerics.Matrix4x4"] = "floating point (Matrix4x4)",
        ["System.Random"] = "System.Random (use P_Random/M_Random)",
        ["System.Security.Cryptography.RandomNumberGenerator"] = "non-deterministic randomness",
        ["System.HashCode"] = "System.HashCode (randomly seeded per process)",
        ["System.DateTime"] = "wall-clock time (DateTime)",
        ["System.DateTimeOffset"] = "wall-clock time (DateTimeOffset)",
        ["System.TimeProvider"] = "wall-clock time (TimeProvider)",
        ["System.Diagnostics.Stopwatch"] = "wall-clock time (Stopwatch)",
        ["System.Threading.Timer"] = "wall-clock time (Timer)",
        ["System.Timers.Timer"] = "wall-clock time (Timer)",
    };

    /// <summary>Individual members that are forbidden on otherwise allowed types.</summary>
    private static readonly Dictionary<string, string> ForbiddenMembers = new()
    {
        ["System.Environment::get_TickCount"] = "wall-clock time (Environment.TickCount)",
        ["System.Environment::get_TickCount64"] = "wall-clock time (Environment.TickCount64)",
        ["System.Guid::NewGuid"] = "non-deterministic randomness (Guid.NewGuid)",
        ["System.Guid::CreateVersion7"] = "non-deterministic randomness (Guid.CreateVersion7)",
    };

    /// <summary>Unordered collections whose enumeration order is not part of the sim's contract.</summary>
    private static readonly HashSet<string> UnorderedCollections = new(StringComparer.Ordinal)
    {
        "System.Collections.Generic.Dictionary`2",
        "System.Collections.Generic.Dictionary`2/KeyCollection",
        "System.Collections.Generic.Dictionary`2/ValueCollection",
        "System.Collections.Generic.HashSet`1",
        "System.Collections.Concurrent.ConcurrentDictionary`2",
        "System.Collections.Concurrent.ConcurrentBag`1",
        "System.Collections.Hashtable",
        "System.Collections.Frozen.FrozenDictionary`2",
        "System.Collections.Frozen.FrozenSet`1",
        "System.Collections.Immutable.ImmutableDictionary`2",
        "System.Collections.Immutable.ImmutableHashSet`1",
    };

    /// <summary>Scans every type in <paramref name="module"/> (optionally filtered) and returns the violations, sorted.</summary>
    public static IReadOnlyList<string> Scan(ModuleDefinition module, Func<TypeDefinition, bool>? include = null)
    {
        var violations = new SortedSet<string>(StringComparer.Ordinal);
        foreach (TypeDefinition type in AllTypes(module))
        {
            if (include is null || include(type))
                ScanType(type, violations);
        }
        return [.. violations];
    }

    private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
    {
        var stack = new Stack<TypeDefinition>(module.Types);
        while (stack.Count > 0)
        {
            TypeDefinition type = stack.Pop();
            yield return type;
            foreach (TypeDefinition nested in type.NestedTypes)
                stack.Push(nested);
        }
    }

    private static void ScanType(TypeDefinition type, ISet<string> violations)
    {
        string where = type.FullName;
        if (type.BaseType is not null)
            CheckType(type.BaseType, where + " (base type)", violations);
        foreach (InterfaceImplementation iface in type.Interfaces)
            CheckType(iface.InterfaceType, where + " (interface)", violations);
        CheckGenericParameters(type.GenericParameters, where, violations);

        foreach (FieldDefinition field in type.Fields)
            CheckType(field.FieldType, $"{where}::{field.Name} (field)", violations);
        foreach (PropertyDefinition property in type.Properties)
            CheckType(property.PropertyType, $"{where}::{property.Name} (property)", violations);
        foreach (EventDefinition evt in type.Events)
            CheckType(evt.EventType, $"{where}::{evt.Name} (event)", violations);
        foreach (MethodDefinition method in type.Methods)
            ScanMethod(method, $"{where}::{method.Name}", violations);
    }

    private static void ScanMethod(MethodDefinition method, string where, ISet<string> violations)
    {
        CheckType(method.ReturnType, where + " (return type)", violations);
        foreach (ParameterDefinition parameter in method.Parameters)
            CheckType(parameter.ParameterType, $"{where} (parameter {parameter.Name})", violations);
        CheckGenericParameters(method.GenericParameters, where, violations);

        if (!method.HasBody)
            return;

        foreach (VariableDefinition variable in method.Body.Variables)
            CheckType(variable.VariableType, $"{where} (local V_{variable.Index})", violations);

        foreach (Instruction instruction in method.Body.Instructions)
        {
            string at = $"{where} (IL_{instruction.Offset:x4}: {instruction.OpCode.Name})";
            switch (instruction.OpCode.Code)
            {
                case Code.Ldc_R4:
                case Code.Ldc_R8:
                case Code.Conv_R4:
                case Code.Conv_R8:
                case Code.Conv_R_Un:
                case Code.Ckfinite:
                    violations.Add($"{at}: floating-point opcode");
                    break;
            }

            switch (instruction.Operand)
            {
                case MethodReference callee:
                    CheckMethodReference(callee, at, violations);
                    break;
                case FieldReference field:
                    CheckType(field.DeclaringType, at, violations);
                    CheckType(field.FieldType, at, violations);
                    break;
                case TypeReference typeRef:
                    CheckType(typeRef, at, violations);
                    break;
                case CallSite site:
                    CheckType(site.ReturnType, at, violations);
                    foreach (ParameterDefinition p in site.Parameters)
                        CheckType(p.ParameterType, at, violations);
                    break;
            }
        }
    }

    private static void CheckMethodReference(MethodReference callee, string at, ISet<string> violations)
    {
        CheckType(callee.DeclaringType, at, violations);
        CheckType(callee.ReturnType, at, violations);
        foreach (ParameterDefinition p in callee.Parameters)
            CheckType(p.ParameterType, at, violations);
        if (callee is GenericInstanceMethod generic)
        {
            foreach (TypeReference argument in generic.GenericArguments)
                CheckType(argument, at, violations);
        }

        string declaring = callee.DeclaringType.GetElementType().FullName;
        if (ForbiddenMembers.TryGetValue($"{declaring}::{callee.Name}", out string? reason))
            violations.Add($"{at}: {reason} via {callee.FullName}");
        if (callee.Name == "GetEnumerator" && UnorderedCollections.Contains(declaring))
            violations.Add($"{at}: iteration over unordered collection {declaring}");
    }

    private static void CheckGenericParameters(IEnumerable<GenericParameter> parameters, string where, ISet<string> violations)
    {
        foreach (GenericParameter parameter in parameters)
        {
            foreach (GenericParameterConstraint constraint in parameter.Constraints)
                CheckType(constraint.ConstraintType, $"{where} (constraint on {parameter.Name})", violations);
        }
    }

    private static void CheckType(TypeReference? type, string where, ISet<string> violations)
    {
        foreach (TypeReference part in Flatten(type))
        {
            if (ForbiddenTypes.TryGetValue(part.FullName, out string? reason))
                violations.Add($"{where}: {reason} ({part.FullName})");
        }
    }

    /// <summary>Yields a type and every type it is composed of.</summary>
    private static IEnumerable<TypeReference> Flatten(TypeReference? type)
    {
        if (type is null)
            yield break;

        switch (type)
        {
            case GenericInstanceType generic:
                foreach (TypeReference t in Flatten(generic.ElementType))
                    yield return t;
                foreach (TypeReference argument in generic.GenericArguments)
                    foreach (TypeReference t in Flatten(argument))
                        yield return t;
                yield break;
            case IModifierType modified:
                foreach (TypeReference t in Flatten(modified.ModifierType))
                    yield return t;
                foreach (TypeReference t in Flatten(modified.ElementType))
                    yield return t;
                yield break;
            case FunctionPointerType fnptr:
                foreach (TypeReference t in Flatten(fnptr.ReturnType))
                    yield return t;
                foreach (ParameterDefinition p in fnptr.Parameters)
                    foreach (TypeReference t in Flatten(p.ParameterType))
                        yield return t;
                yield break;
            case TypeSpecification spec: // array, pointer, by-ref, pinned, sentinel
                foreach (TypeReference t in Flatten(spec.ElementType))
                    yield return t;
                yield break;
            case GenericParameter:
                yield break;
            default:
                yield return type;
                if (type.DeclaringType is not null)
                    foreach (TypeReference t in Flatten(type.DeclaringType))
                        yield return t;
                yield break;
        }
    }
}
