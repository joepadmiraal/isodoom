using System;
using System.Collections.Generic;
using IsoDoom.Tests.Support;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T4.8a: the <see cref="TestMap"/>s a route can name (<c>iwad testmap</c>,
/// <c>map NAME</c>): T4.4's hand-worked <see cref="MovementTests"/> cases
/// that no IWAD route covers. The tests write each as a PWAD
/// (<see cref="VanillaRouteTests.WritesTheTestMapPwads"/>, run by
/// <c>tools/VanillaRef/routes.sh</c>), which the reference plays as
/// <c>-file</c> over DOOM1.WAD; the map lump is <c>E1M1</c>.
/// </summary>
public static class RouteTestMaps
{
    // Two 256×256 rooms side by side, the boundary at x = 256 (MovementTests.TwoRooms);
    // the player at (128, 128) facing east.
    private static TestMap TwoRooms(int floor1, int ceiling1) =>
        TestMap.Strip(0, 0, 256, new TestMap.Room(256, 0, 128), new TestMap.Room(256, floor1, ceiling1)).Player(128, 128);

    /// <summary>The maps by name (ordinal order).</summary>
    public static readonly IReadOnlyDictionary<string, Func<TestMap>> Maps = new SortedDictionary<string, Func<TestMap>>(StringComparer.Ordinal)
    {
        // A 25-unit step up: refused (MovementTests.NotSteppingUp25Units).
        ["step25"] = () => TwoRooms(25, 128),
        // A ceiling at 56 lets the 56-unit player in, one at 55 does not (FittingUnderALowCeiling).
        ["ceiling56"] = () => TwoRooms(0, 56),
        ["ceiling55"] = () => TwoRooms(0, 55),
        // A raised floor under a low ceiling: 24 up into a 79-unit room leaves 55, into an 80-unit one 56.
        ["raised79"] = () => TwoRooms(24, 79),
        ["raised80"] = () => TwoRooms(24, 80),
        // 20 rooms 2 units wide between two 100-unit ones, every boundary a walk-over special:
        // the player's box touches up to 15 of them, past vanilla's 8 (spechit overrun;
        // SpecialLinesAreCollected). Facing east at (50, 128).
        ["spechit"] = Spechit,
    };

    private static TestMap Spechit()
    {
        var rooms = new TestMap.Room[22];
        rooms[0] = new TestMap.Room(100, 0, 128);
        for (int i = 1; i < 21; i++)
            rooms[i] = new TestMap.Room(2, 0, 128);
        rooms[21] = new TestMap.Room(100, 0, 128);
        TestMap map = TestMap.Strip(0, 0, 256, rooms);
        for (int i = 1; i < rooms.Length; i++)
            map.Special(map.Boundaries[i], 88);
        return map.Player(50, 128);
    }

    /// <summary>The map named <paramref name="name"/>.</summary>
    public static TestMap Get(string name) =>
        Maps.TryGetValue(name, out Func<TestMap>? make) ? make() : throw new KeyNotFoundException($"No route test map \"{name}\" (RouteTestMaps.Maps).");
}
