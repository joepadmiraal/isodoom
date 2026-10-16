using System.Diagnostics;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Render;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Render;

/// <summary>
/// T6.13l: the fog of war's walk stays cheap (T11.10: four players' too); split from
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

            // T11.10: the shared co-op fog of four players, from the start and three other things far apart.
            (int X, int Y)[] four = [(x, y), .. level.Things.Where(t => t.Type > 4).OrderBy(t => t.X).Where((t, i) => i % 50 == 0).Take(3)
                .Select(t => (t.X << Fixed.FRACBITS, t.Y << Fixed.FRACBITS))];
            double ms4 = double.MaxValue;
            for (int batch = 0; batch < 10; batch++)
            {
                const int Runs = 10;
                long t0 = Stopwatch.GetTimestamp();
                for (int i = 0; i < Runs; i++)
                    fog.SeeAll(four);
                ms4 = System.Math.Min(ms4, Stopwatch.GetElapsedTime(t0).TotalMilliseconds / Runs);
            }
            _output.WriteLine($"{map}: {ms4:F3} ms for {four.Length} players");
            Assert.True(ms4 < four.Length * 1.0, $"{map}: {ms4:F3} ms for {four.Length} players");
        }
    }
}
