using IsoDoom.Map;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Map;

/// <summary>
/// P3: map data the PWAD sets' maps have that vanilla loads (runs in CI, on
/// <see cref="TestMap"/>'s maps).
/// </summary>
public class PwadMapDataTests
{
    private static TestMap Room() =>
        TestMap.Polygon(0, 128, (0, 0), (0, 256), (256, 256), (256, 0)).Player(128, 128);

    private static Level Load(TestMap map) =>
        Level.Load(new WadArchive([WadFile.FromBytes(map.Build(), "testmap.wad")]), "E1M1");

    /// <summary>A sidedef no linedef uses naming sector 65535 (Memento Mori MAP16, Requiem MAP03): vanilla never looks, so the map loads.</summary>
    [Fact]
    public void UnusedSidedefWithAMissingSectorLoads()
    {
        Level level = Load(Room().ExtraSide(0xFFFF));
        Assert.Equal(5, level.Sides.Length);
        Assert.All(level.Lines, l => Assert.Equal(0, l.FrontSector!.Index));
        Room().ExtraSide(0xFFFF).Load(); // and the sim's world takes it
    }

    /// <summary>A linedef that uses such a sidedef is still refused, naming both.</summary>
    [Fact]
    public void UsedSidedefWithAMissingSectorIsRefused()
    {
        WadFormatException e = Assert.Throws<WadFormatException>(() => Load(Room().ExtraSide(0xFFFF, onLine: 2)));
        Assert.Equal("Map E1M1: sidedef 4 (of linedef 2) refers to sector 65535, but there are 1.", e.Message);
    }
}
