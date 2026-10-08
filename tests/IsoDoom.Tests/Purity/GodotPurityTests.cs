using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Wad;
using Mono.Cecil;
using Xunit;

namespace IsoDoom.Tests.Purity;

/// <summary>
/// The plain .NET libraries never reference Godot (SPEC §4), and the sim
/// depends on nothing beyond the BCL and the other two libraries (SPEC §12 Q2).
/// </summary>
/// <remarks>
/// Two layers, because the compiler drops an assembly reference that no code
/// uses: the compiled metadata (assembly references, type and member
/// references, attributes) catches Godot code, and the test's
/// <c>.deps.json</c>, which lists each project's package and project
/// dependencies whether used or not, catches a bare <c>PackageReference</c>.
/// </remarks>
public class GodotPurityTests
{
    /// <summary>The libraries checked; <see cref="AnchorType"/> names a type each defines.</summary>
    public static TheoryData<string> Libraries => new() { "IsoDoom.Wad", "IsoDoom.Map", "IsoDoom.Sim" };

    /// <summary>Non-BCL assemblies the sim may reference. Adding one is a conscious decision (SPEC §12 Q2).</summary>
    private static readonly HashSet<string> SimAllowedReferences = new(StringComparer.Ordinal)
    {
        "IsoDoom.Map",
        "IsoDoom.Wad",
    };

    private static Type AnchorType(string library) => library switch
    {
        "IsoDoom.Wad" => typeof(WadFile),
        "IsoDoom.Map" => typeof(Level),
        "IsoDoom.Sim" => typeof(SimInfo),
        _ => throw new ArgumentOutOfRangeException(nameof(library)),
    };

    private static bool IsGodot(string name) => name.StartsWith("Godot", StringComparison.OrdinalIgnoreCase);

    [Theory]
    [MemberData(nameof(Libraries))]
    public void AssemblyReferencesNoGodot(string library)
    {
        Type anchor = AnchorType(library);
        using ModuleDefinition module = ModuleDefinition.ReadModule(anchor.Assembly.Location);
        Assert.Equal(library, module.Assembly.Name.Name);
        Assert.Contains(module.Types, t => t.FullName == anchor.FullName);

        var violations = new SortedSet<string>(StringComparer.Ordinal);
        foreach (AssemblyNameReference reference in module.AssemblyReferences)
        {
            if (IsGodot(reference.Name))
                violations.Add($"assembly reference {reference.FullName}");
        }
        foreach (TypeReference type in module.GetTypeReferences())
        {
            if (IsGodot(type.Namespace) || IsGodot(type.Scope?.Name ?? ""))
                violations.Add($"type reference {type.FullName} ({type.Scope?.Name})");
        }
        foreach (MemberReference member in module.GetMemberReferences())
        {
            if (IsGodot(member.DeclaringType?.Namespace ?? ""))
                violations.Add($"member reference {member.FullName}");
        }
        foreach (TypeDefinition type in module.GetTypes())
        {
            if (IsGodot(type.Namespace))
                violations.Add($"type {type.FullName} defined in a Godot namespace");
        }
        foreach (CustomAttribute attribute in module.Assembly.CustomAttributes.Concat(module.CustomAttributes))
        {
            if (IsGodot(attribute.AttributeType.Namespace))
                violations.Add($"attribute {attribute.AttributeType.FullName}");
        }

        Assert.True(violations.Count == 0,
            $"{library} references Godot (SPEC §4: Wad, Map and Sim are plain .NET). " +
            $"{violations.Count} violation(s):\n  " + string.Join("\n  ", violations.Take(200)));
    }

    [Theory]
    [MemberData(nameof(Libraries))]
    public void DependenciesIncludeNoGodot(string library)
    {
        IReadOnlyList<string> closure = DependencyClosure(library);
        string[] godot = closure.Where(IsGodot).ToArray();
        Assert.True(godot.Length == 0,
            $"{library} depends on Godot packages (SPEC §4): {string.Join(", ", godot)}");
    }

    [Fact]
    public void SimReferencesOnlyBclMapAndWad()
    {
        using ModuleDefinition module = ModuleDefinition.ReadModule(typeof(SimInfo).Assembly.Location);
        string bcl = RuntimeEnvironment.GetRuntimeDirectory();
        string[] unexpected = module.AssemblyReferences
            .Select(r => r.Name)
            .Where(name => !SimAllowedReferences.Contains(name) && !File.Exists(Path.Combine(bcl, name + ".dll")))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.True(unexpected.Length == 0,
            "IsoDoom.Sim references assemblies outside the BCL and its allow-list " +
            $"({string.Join(", ", SimAllowedReferences)}): {string.Join(", ", unexpected)}. " +
            "Add a new dependency to the allow-list in GodotPurityTests only as a conscious decision.");

        string[] unexpectedDependencies = DependencyClosure("IsoDoom.Sim")
            .Where(name => !SimAllowedReferences.Contains(name))
            .ToArray();
        Assert.True(unexpectedDependencies.Length == 0,
            "IsoDoom.Sim has package or project dependencies outside its allow-list " +
            $"({string.Join(", ", SimAllowedReferences)}): {string.Join(", ", unexpectedDependencies)}.");
    }

    /// <summary>
    /// Every package and project <paramref name="library"/> depends on,
    /// directly or not, from the test's <c>.deps.json</c> (names only, sorted).
    /// </summary>
    private static IReadOnlyList<string> DependencyClosure(string library)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "IsoDoom.Tests.deps.json");
        using JsonDocument deps = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement target = deps.RootElement.GetProperty("targets").EnumerateObject().First().Value;

        // "Name/Version" -> dependency names
        var entries = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty entry in target.EnumerateObject())
        {
            string name = entry.Name[..entry.Name.IndexOf('/')];
            var dependencies = new List<string>();
            if (entry.Value.TryGetProperty("dependencies", out JsonElement list))
                dependencies.AddRange(list.EnumerateObject().Select(d => d.Name));
            entries[name] = dependencies;
        }
        Assert.True(entries.ContainsKey(library), $"{library} is missing from {path}");

        var closure = new SortedSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(entries[library]);
        while (pending.Count > 0)
        {
            string name = pending.Pop();
            if (!closure.Add(name))
                continue;
            if (entries.TryGetValue(name, out List<string>? next))
            {
                foreach (string dependency in next)
                    pending.Push(dependency);
            }
        }
        return closure.ToList();
    }
}
