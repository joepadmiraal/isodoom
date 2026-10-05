using System.Collections.Generic;
using System.Linq;
using IsoDoom.Sim;
using Mono.Cecil;
using Xunit;

namespace IsoDoom.Tests.Purity;

/// <summary>Enforces the determinism rules of SPEC §6.1 on the compiled Sim assembly.</summary>
public class SimPurityTests
{
    [Fact]
    public void SimAssemblyIsDeterministic()
    {
        using ModuleDefinition module = ModuleDefinition.ReadModule(typeof(SimInfo).Assembly.Location);
        Assert.Contains(module.Types, t => t.FullName == typeof(SimInfo).FullName); // not scanning the wrong file

        IReadOnlyList<string> violations = DeterminismScanner.Scan(module);

        Assert.True(violations.Count == 0,
            $"IsoDoom.Sim breaks the determinism rules (SPEC §6.1): no float/double, System.Random, " +
            $"wall-clock time or Dictionary/HashSet iteration. {violations.Count} violation(s):\n  " +
            string.Join("\n  ", violations.Take(200)));
    }
}
