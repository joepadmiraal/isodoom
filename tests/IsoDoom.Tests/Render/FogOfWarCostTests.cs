using System.Diagnostics;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Render;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Render;

/// <summary>
/// T6.13l: the fog of war's walk stays cheap; split from
/// <see cref="FogOfWarTests"/> into the <see cref="TimingGroup"/> (T8.6a).
/// </summary>
[Collection(TimingGroup.Name)]
public class FogOfWarCostTests
{
    private readonly ITestOutputHelper _output;

    public FogOfWarCostTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void TheWalkIsCheapOnDoom2sBiggestMaps()
    {
        string wad = TestWads.RequireDoom2();
        foreach (string map in new[] { "MAP18", "MAP27" })
        {
            Level level = Level.Load(WadArchive.Open(wad), map);
            MapThing start = level.PlayerStart(0)!.Value;
            var fog = new FogOfWar(level);
            int x = start.X << Fixed.FRACBITS, y = start.Y << Fixed.FRACBITS;
            for (int i = 0; i < 20; i++)
                fog.See(x, y); // warm up
            // Open every closed sector too, the worst case: sight goes everywhere.
            foreach (Sector s in level.Sectors.Where(s => s.CeilingHeight <= s.FloorHeight))
                s.CeilingHeight = s.FloorHeight + (128 << Fixed.FRACBITS);
            // The best of ten batches: the machine may be busy.
            double ms = double.MaxValue;
            for (int batch = 0; batch < 10; batch++)
            {
                const int Runs = 20;
                long t0 = Stopwatch.GetTimestamp();
                for (int i = 0; i < Runs; i++)
                    fog.See(x, y);
                ms = System.Math.Min(ms, Stopwatch.GetElapsedTime(t0).TotalMilliseconds / Runs);
            }
            _output.WriteLine($"{map}: {ms:F3} ms a walk");
            // SPEC §9's frame budget is ~16 ms; the target is 0.2 ms, with a margin for a slow test machine.
            Assert.True(ms < 1.0, $"{map}: {ms:F3} ms a walk");
        }
    }
}
