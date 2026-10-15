using Xunit;

namespace IsoDoom.Tests.Support;

/// <summary>
/// T8.6a: the xunit collection of the tests that time themselves against SPEC §9's budgets
/// (<c>Stopwatch</c>): run alone, after the parallel tests, so the rest of
/// the suite does not inflate their times.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingGroup
{
    public const string Name = "Timing";
}
