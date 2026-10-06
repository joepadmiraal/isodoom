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
        // T5.5: lifts. West to east: a ledge U (floor 64), the lift L (tag 1, 64), the start room S
        // (0) and a perpetual lift P (tag 5, 0) between S and a room Q (48). L | U (boundary 1) is a
        // WR lift line (88), S | L (boundary 2) an SR lift switch (62, used from S), P | S
        // (boundary 3) a W1 perpetual lift (53). The player in S facing east.
        ["lifts"] = Lifts,
        // T5.5: stairs and a crusher. West to east: a crusher room C (tag 3, floor 128, out of
        // reach), a room E (32, another flat: the stairs stop there), four steps (0, the last tag
        // 2), a room B and the start room A (0). A | B (boundary 7) is a W1 build-stairs line (8):
        // the steps rise to 32, 24, 16 and 8 westwards; step 1 | E (boundary 2) a W1 fast crusher
        // (6). The player in A facing west.
        ["stairs"] = Stairs,
        // T5.6: teleporters. West to east: a room A (floor 32, tag 1; two teleport destinations,
        // the first at (64, 64) facing east, the second at (192, 192) facing north), a room P1, the
        // start room S, a room P2 (floors 0) and a room B (-16, tag 2; a destination at (800, 128)
        // facing west). P1 | S (boundary 2) and S | P2 (boundary 3) are WR teleports (97, tag 2:
        // to B) from their east sides, P2 | B (boundary 4) a W1 teleport (39, tag 1: to A) from B.
        // The player in S facing west.
        ["teleport"] = Teleport,
    };

    private static TestMap Teleport()
    {
        TestMap map = TestMap.Strip(0, 0, 256,
            new TestMap.Room(256, 32, 160), new TestMap.Room(64, 0, 160), new TestMap.Room(256, 0, 160),
            new TestMap.Room(64, 0, 160), new TestMap.Room(256, -16, 160));
        map.SectorTag(0, 1).SectorTag(4, 2);
        map.Special(map.Boundaries[2], 97, 2).Special(map.Boundaries[3], 97, 2).Special(map.Boundaries[4], 39, 1);
        return map.Thing(64, 64, 14, 0).Thing(192, 192, 14, 90).Thing(800, 128, 14, 180).Player(448, 128, 180);
    }

    private static TestMap Lifts()
    {
        TestMap map = TestMap.Strip(0, 0, 256,
            new TestMap.Room(192, 64, 192), new TestMap.Room(64, 64, 192), new TestMap.Room(256, 0, 192),
            new TestMap.Room(64, 0, 192), new TestMap.Room(128, 48, 192));
        map.SectorTag(1, 1).SectorTag(3, 5);
        map.Special(map.Boundaries[1], 88, 1).Special(map.Boundaries[2], 62, 1).Special(map.Boundaries[3], 53, 5);
        return map.Player(448, 128);
    }

    private static TestMap Stairs()
    {
        TestMap map = TestMap.Strip(0, 0, 256,
            new TestMap.Room(128, 128, 256), new TestMap.Room(128, 32, 256),
            new TestMap.Room(64, 0, 256), new TestMap.Room(64, 0, 256), new TestMap.Room(64, 0, 256), new TestMap.Room(64, 0, 256),
            new TestMap.Room(64, 0, 256), new TestMap.Room(192, 0, 256));
        map.SectorTag(0, 3).SectorTag(5, 2).FloorPic(1, "FLOOR5_2");
        map.Special(map.Boundaries[7], 8, 2).Special(map.Boundaries[2], 6, 3);
        return map.Player(672, 128, 180);
    }

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
