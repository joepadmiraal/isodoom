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
        // T6.5: missiles and sky walls. West to east: a room W (64 wide, ceiling 36, F_SKY1), the
        // start room S (512 wide, ceiling 128) and a room E (64 wide, ceiling 36, F_SKY1), 512 deep.
        // A missile 32 units up (top 40) is blocked by both low sky ceilings: across W | S
        // (boundary 1, W behind it) it vanishes (p_mobj.c's sky hack reads the back sector), across
        // S | E (boundary 2, S behind it) it explodes. The player at (136, 256) facing west (a
        // rocket's 20-unit steps from there end with its centre still in S, see SPEC §12 T6.5), a
        // barrel at (136, 448), an imp at (480, 256) facing east (away from the player).
        ["missiles"] = Missiles,
        // T6.6: the player's weapons. West to east (512 deep, ceilings 128): the start room A
        // (512 wide), a room T1 (64), a closed door D1 (16, tag 1), a room B (384), a room T2 (64), a
        // closed door D2 (16, tag 2) and a room C (256); A | T1 (boundary 1) and B | T2 (boundary 4)
        // are W1 open-door lines (2) for D1 and D2. The closed doors keep each room's monsters
        // from seeing and hearing the shots of the room before. The player at (64, 256) facing
        // east; along y = 256 a blue armor (96), a shotgun (400), a soul sphere (544), a rocket
        // launcher (680), a chaingun (840), a soul sphere (1008) and a chainsaw (1100). Monsters,
        // each facing away from the player's way: in A zombiemen at (160, 256) facing east and
        // (300, 448) facing north, an imp at (450, 64) facing south with a barrel at (410, 64);
        // in B an imp at (940, 448) facing north and a zombieman at (760, 64) facing south; in C
        // zombiemen at (1250, 256) facing east and (1250, 400) facing north.
        ["weapons"] = Weapons,
        // T6.8: every shareware pickup. West to east (256 deep, ceilings 128): the start room N
        // (192 wide, special 5: 10 damage every 32 tics) and a room P (1408). The player at
        // (96, 128) facing east; along y = 128 in P, 48 units apart from x = 240: a stimpack, a
        // medikit, two health bonuses, a soul sphere, a stimpack (left: health over 100), an armor
        // bonus, a green armor, a blue armor, a green armor (left: blue is better), a clip, a box
        // of bullets, shells, a box of shells, a rocket, a box of rockets, a backpack, a shotgun,
        // a chaingun, a rocket launcher, a chainsaw, the blue, yellow and red keycards, a blur
        // sphere, two computer maps (the second left), a light amplification visor, and a
        // radiation suit at (1570, 128).
        ["pickups"] = Pickups,
    };

    /// <summary>The pickups along y = 128 in <see cref="Pickups"/>' room P, west to east (doomednums), from x = 240 every 48 units.</summary>
    public static readonly int[] PickupRow =
    {
        2011, 2012, 2014, 2014, 2013, 2011, 2015, 2018, 2019, 2018, 2007, 2048, 2008, 2049, 2010, 2046, 8,
        2001, 2002, 2003, 2005, 5, 6, 13, 2024, 2026, 2026, 2045,
    };

    private static TestMap Pickups()
    {
        TestMap map = TestMap.Strip(0, 0, 256, new TestMap.Room(192, 0, 128), new TestMap.Room(1408, 0, 128));
        map.SectorSpecial(0, 5);
        for (int i = 0; i < PickupRow.Length; i++)
            map.Thing(240 + 48 * i, 128, PickupRow[i]);
        return map.Thing(1570, 128, 2025).Player(96, 128, 0);
    }

    private static TestMap Weapons()
    {
        TestMap map = TestMap.Strip(0, 0, 512,
            new TestMap.Room(512, 0, 128), new TestMap.Room(64, 0, 128), new TestMap.Room(16, 0, 0), new TestMap.Room(384, 0, 128),
            new TestMap.Room(64, 0, 128), new TestMap.Room(16, 0, 0), new TestMap.Room(256, 0, 128));
        map.SectorTag(2, 1).SectorTag(5, 2);
        map.Special(map.Boundaries[1], 2, 1).Special(map.Boundaries[4], 2, 2);
        return map.Thing(96, 256, 2019).Thing(400, 256, 2001).Thing(544, 256, 2013).Thing(680, 256, 2003).Thing(840, 256, 2002)
            .Thing(1008, 256, 2013).Thing(1100, 256, 2005)
            .Thing(160, 256, 3004, 0).Thing(300, 448, 3004, 90).Thing(450, 64, 3001, 270).Thing(410, 64, 2035)
            .Thing(940, 448, 3001, 90).Thing(760, 64, 3004, 270)
            .Thing(1250, 256, 3004, 0).Thing(1250, 400, 3004, 90)
            .Player(64, 256, 0);
    }

    private static TestMap Missiles()
    {
        TestMap map = TestMap.Strip(0, 0, 512,
            new TestMap.Room(64, 0, 36), new TestMap.Room(512, 0, 128), new TestMap.Room(64, 0, 36));
        map.CeilingPic(0, "F_SKY1").CeilingPic(2, "F_SKY1");
        return map.Thing(136, 448, 2035).Thing(480, 256, 3001, 0).Player(136, 256, 180);
    }

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
