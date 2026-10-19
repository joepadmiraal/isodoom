using IsoDoom.Tests.Support;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// P6 (<c>docs/PWAD-VALIDATION.md</c>): the PWAD sets' demo lumps, read by the
/// vanilla-input adapter (<see cref="IsoDoom.Game.RouteFile.FromDemo"/>) and
/// played with every tweak off over the set's IWAD with the set loaded as
/// <c>-file</c>, stay in sync with the vanilla reference playing them so,
/// tic by tic to their end (or to their level's exit, where a demo was
/// recorded on into the intermission, which the dump does not hold), against
/// the dumps of <c>tools/VanillaRef/demos.sh --pwad SET</c> (WAD-derived, in
/// <see cref="VanillaRoute.DumpDirEnvVar"/>; each skips without its set, its
/// IWAD or its dump). What counts is that the sim and the reference agree on
/// every tic, not that the demo finishes as recorded: PWAD demos were often
/// recorded on another executable or with a DeHackEd patch loaded.
/// </summary>
/// <remarks>
/// D2TWID's demos are played without its DeHackEd patch, in the sim and in
/// the reference alike (neither reads <c>DEHACKED</c> yet), so they check the
/// sim against vanilla on D2TWID's maps, not the demos as recorded.
/// </remarks>
public class PwadDemoTests
{
    /// <summary>Each set's demo lumps (every <c>DEMOn</c> of its files: HR's DEMO5 and DEMO6 too, outside the attract loop).</summary>
    public static TheoryData<string, string> Demos()
    {
        var data = new TheoryData<string, string>();
        foreach ((string set, int count) in new[] { ("scythe", 3), ("requiem", 3), ("hr", 6), ("av", 3), ("mm", 3), ("dtwid", 3), ("neis", 4), ("d2twid", 3) })
        {
            for (int i = 1; i <= count; i++)
                data.Add(set, "DEMO" + i);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Demos))]
    public void StaysInSyncWithVanilla(string set, string demo) => VanillaRoute.Demo(demo, PwadSets.Get(set)).Check();
}
