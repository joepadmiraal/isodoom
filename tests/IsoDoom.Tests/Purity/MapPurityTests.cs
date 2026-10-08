using System.Collections.Generic;
using System.Linq;
using IsoDoom.Map;
using Mono.Cecil;
using Xunit;

namespace IsoDoom.Tests.Purity;

/// <summary>
/// The map structures the sim runs on (<c>IsoDoom.Map</c>) follow the same
/// determinism rules as <c>IsoDoom.Sim</c> (SPEC §6.1, §12).
/// </summary>
public class MapPurityTests
{
    [Fact]
    public void MapAssemblyIsDeterministic()
    {
        using var module = ModuleDefinition.ReadModule(typeof(Level).Assembly.Location);
        Assert.Contains(module.Types, t => t.FullName == typeof(Level).FullName);

        IReadOnlyList<string> violations = DeterminismScanner.Scan(module);

        Assert.True(violations.Count == 0,
            $"IsoDoom.Map breaks the determinism rules (SPEC §6.1). {violations.Count} violation(s):\n  " +
            string.Join("\n  ", violations.Take(200)));
    }
}
