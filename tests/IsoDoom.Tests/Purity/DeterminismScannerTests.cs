using System;
using System.Collections.Generic;
using IsoDoom.Tests.Purity.Fixtures;
using Mono.Cecil;
using Xunit;

namespace IsoDoom.Tests.Purity;

/// <summary>Proves the scanner catches each forbidden construct, so a green SimPurityTests means something.</summary>
public class DeterminismScannerTests
{
    private static readonly ModuleDefinition TestModule =
        ModuleDefinition.ReadModule(typeof(DeterminismScannerTests).Assembly.Location);

    private static IReadOnlyList<string> ScanFixture(Type fixture) =>
        DeterminismScanner.Scan(TestModule, t => IsOrIsNestedIn(t, fixture.FullName!));

    private static bool IsOrIsNestedIn(TypeDefinition type, string fullName)
    {
        for (TypeDefinition? t = type; t is not null; t = t.DeclaringType)
        {
            if (t.FullName == fullName)
                return true;
        }
        return false;
    }

    [Fact]
    public void CleanCodePasses() => Assert.Empty(ScanFixture(typeof(Clean)));

    [Theory]
    [InlineData(typeof(FloatField), "float")]
    [InlineData(typeof(FloatProperty), "float")]
    [InlineData(typeof(DoubleLocal), "double")]
    [InlineData(typeof(DoubleLocal), "floating-point opcode")]
    [InlineData(typeof(FloatLiteral), "floating-point opcode")]
    [InlineData(typeof(FloatInLambda), "floating-point opcode")]
    [InlineData(typeof(FloatInIterator), "double")]
    [InlineData(typeof(RandomUse), "System.Random")]
    [InlineData(typeof(DateTimeUse), "DateTime")]
    [InlineData(typeof(TickCountUse), "Environment.TickCount")]
    [InlineData(typeof(StopwatchUse), "Stopwatch")]
    [InlineData(typeof(DictionaryIteration), "unordered collection")]
    [InlineData(typeof(DictionaryKeysIteration), "unordered collection")]
    [InlineData(typeof(HashSetIteration), "unordered collection")]
    public void ForbiddenConstructIsReported(Type fixture, string expected)
    {
        IReadOnlyList<string> violations = ScanFixture(fixture);
        Assert.Contains(violations, v => v.Contains(expected, StringComparison.Ordinal));
        Assert.All(violations, v => Assert.Contains(fixture.Name, v, StringComparison.Ordinal));
    }
}
