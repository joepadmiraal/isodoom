using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T4.4: p_map.c's position checks, moves and wall sliding and p_mobj.c's
/// momentum movement, on small test maps (<see cref="TestMap"/>), tic by
/// tic against values worked out by hand from vanilla's arithmetic (the
/// derivations are in the comments). The cases no IWAD route covers are
/// also vanilla-generated routes on the same maps (T4.8a, <see cref="RouteTestMaps"/>).
/// Most tests push the player by setting its momentum, so the values test
/// p_map.c and p_mobj.c alone: with an empty <c>ticcmd</c>, <c>P_PlayerThink</c>
/// (T4.5) does not thrust, it only moves the view height. Walking by
/// <c>ticcmd</c> is in <see cref="PlayerTests"/>; the E1M1 wander here uses it.
/// </summary>
public class MovementTests
{
    private const int FRACUNIT = 1 << 16;
    private static int F(int units) => units * FRACUNIT;

    // FRICTION 0xe800 applied to 8 units: 8 * 0xe800 = 475136 (7.25 units), then
    // 430592, 390224, 353640, 320486, 290440 (each (m * 0xe800) >> 16).
    private const int Mom8 = 8 * FRACUNIT, Mom8F1 = 475136, Mom8F2 = 430592, Mom8F3 = 390224, Mom8F4 = 353640, Mom8F5 = 320486;

    private static mobj_t Player(World w) => w.players[0].mo!;

    private static void AssertPos(mobj_t mo, int x, int y, int z, int momx, int momy, int momz, string tic)
    {
        Assert.True((mo.x, mo.y, mo.z, mo.momx, mo.momy, mo.momz) == (x, y, z, momx, momy, momz),
            $"{tic}: got ({mo.x}, {mo.y}, {mo.z}) mom ({mo.momx}, {mo.momy}, {mo.momz}), expected ({x}, {y}, {z}) mom ({momx}, {momy}, {momz})");
    }

    // A 512×256 room.
    private static TestMap Room() => TestMap.Strip(0, 0, 256, new TestMap.Room(512, 0, 128));

    // Two rooms side by side, the boundary at x = 256.
    private static TestMap TwoRooms(int floor0, int ceiling0, int floor1, int ceiling1) =>
        TestMap.Strip(0, 0, 256, new TestMap.Room(256, floor0, ceiling0), new TestMap.Room(256, floor1, ceiling1));

    [Fact]
    public void TestMapsLoadWithTheirSectorsAndBlocks()
    {
        World w = TestMap.Strip(0, 0, 256, new TestMap.Room(256, 0, 128), new TestMap.Room(128, 24, 128), new TestMap.Room(128, 8, 100))
            .Player(100, 100).Load();
        Assert.Equal(0, w.R_PointInSubsector(F(100), F(10)).sector.Index);
        Assert.Equal(0, w.R_PointInSubsector(F(256), F(10)).sector.Index); // on the boundary: back (west)
        Assert.Equal(1, w.R_PointInSubsector(F(257), F(10)).sector.Index);
        Assert.Equal(2, w.R_PointInSubsector(F(500), F(250)).sector.Index);
        Assert.Equal(F(24), w.sectors[1].floorheight);
        Assert.Equal((F(100), F(100), 0), (Player(w).x, Player(w).y, Player(w).z));
        Assert.Equal(16 * FRACUNIT, Player(w).radius);
        Assert.Equal(56 * FRACUNIT, Player(w).height);
    }

    // ---- walking into a wall ----

    [Fact]
    public void WalkingIntoAWallStopsAtIt()
    {
        // Player at (480, 128) moving east at 8 units a tic; the east wall is at x = 512, so the
        // player's box (radius 16) fits up to x = 496 (box right edge on the line is no contact).
        World w = Room().Player(480, 128).Load();
        mobj_t mo = Player(w);
        mo.momx = Mom8;

        w.P_Ticker(); // 480 → 488, friction
        AssertPos(mo, F(488), F(128), 0, Mom8F1, 0, 0, "tic 1");
        w.P_Ticker(); // → 495.25
        AssertPos(mo, F(488) + Mom8F1, F(128), 0, Mom8F2, 0, 0, "tic 2");

        // Tic 3: 495.25 + 6.5703 crosses the wall: P_SlideMove. The lead corners trace from
        // x = 511.25 (y 112 and 144) and cross the wall at
        //   frac = FixedDiv(FixedMul(0.75u >> 8, -256u), FixedMul(-256u >> 8, 430592))
        //        = FixedDiv(-49152, -430592) = 7480,
        // fudged by 0x800 to 5432: the player moves FixedMul(430592, 5432) = 35689; the rest of
        // the move is along the wall (vertical: tmxmove = 0), so the momentum ends at 0.
        w.P_Ticker();
        int x = F(488) + Mom8F1 + 35689;
        AssertPos(mo, x, F(128), 0, 0, 0, 0, "tic 3");
        Assert.Equal(32492393, x);
        w.P_Ticker();
        AssertPos(mo, x, F(128), 0, 0, 0, 0, "tic 4");
    }

    // ---- sliding along a wall ----

    [Fact]
    public void SlidingAlongAWallAtAnAngle()
    {
        // As above with momy = 4 units: friction gives momy 237568, 215296. At tic 3 the lead
        // traces (y 151.625 and 119.625) cross the wall at the same frac 7480 (it does not depend
        // on momy): the partial move is (35689, FixedMul(215296, 5432) = 17844), the remainder
        // 65536 - 7480 = 58056 of momy, FixedMul(215296, 58056) = 190723, becomes the new momy
        // and is moved at once; then friction.
        World w = Room().Player(480, 128).Load();
        mobj_t mo = Player(w);
        mo.momx = Mom8;
        mo.momy = F(4);

        w.P_Ticker();
        AssertPos(mo, F(488), F(132), 0, Mom8F1, 237568, 0, "tic 1");
        w.P_Ticker();
        AssertPos(mo, 32456704, 8888320, 0, Mom8F2, 215296, 0, "tic 2"); // (495.25, 135.625)
        w.P_Ticker();
        int x = 32456704 + 35689;
        AssertPos(mo, x, 8888320 + 17844 + 190723, 0, 0, 172842, 0, "tic 3");
        // Then it keeps sliding north along the wall, slowing down.
        int[] ys = { 9269729, 9426367, 9568320, 9696964, 9813547 };
        int[] moms = { 156638, 141953, 128644, 116583, 105653 };
        for (int t = 0; t < ys.Length; t++)
        {
            w.P_Ticker();
            AssertPos(mo, x, ys[t], 0, 0, moms[t], 0, $"tic {t + 4}");
        }
    }

    [Fact]
    public void SlidingAlongADiagonalWall()
    {
        // A room cut by the diagonal line 2, (256, 512) → (512, 256) (x + y = 768, front to the
        // south-west). The player at (360, 360) moves east at 8 units a tic: tics 1 and 2 are free
        // (its box's north-east corner reaches x + y = 767.25); at tic 3 only the trace from the
        // corner (x + 16, y + 16) crosses the line.
        World w = TestMap.Polygon(0, 128, (0, 0), (0, 512), (256, 512), (512, 256), (512, 0)).Player(360, 360).Load();
        mobj_t mo = Player(w);
        line_t diagonal = w.lines[2];
        Assert.Equal(SlopeType.ST_NEGATIVE, diagonal.slopetype);
        mo.momx = Mom8;

        w.P_Ticker();
        w.P_Ticker();
        int x = F(368) + Mom8F1;
        AssertPos(mo, x, F(360), 0, Mom8F2, 0, 0, "tic 2");

        // Tic 3, by hand (p_map.c P_SlideMove, P_InterceptVector, P_HitSlideLine):
        int leadx = x + F(16), traily = F(360) + F(16);
        int den = Fixed.FixedMul(-F(256) >> 8, Mom8F2) - Fixed.FixedMul(F(256) >> 8, 0);
        int num = Fixed.FixedMul((F(256) - leadx) >> 8, -F(256)) + Fixed.FixedMul((traily - F(512)) >> 8, F(256));
        int frac = Fixed.FixedDiv(num, den);
        Assert.InRange(frac, FRACUNIT / 10, FRACUNIT / 8); // 0.75 of 6.57 units
        int best = frac - 0x800;
        x += Fixed.FixedMul(Mom8F2, best);
        int remainder = FRACUNIT - (best + 0x800);
        int tmxmove = Fixed.FixedMul(Mom8F2, remainder);
        // The line's angle is 315° (ANG270 + tantoangle[2048]); the move's is 0, so the angle
        // between them is ANG45, and the move's length (P_AproxDistance: tmxmove) is projected
        // on the line.
        Assert.Equal(0xE0000000u, Tables.R_PointToAngle2(0, 0, diagonal.dx, diagonal.dy));
        int newlen = Fixed.FixedMul(tmxmove, Tables.finecosine[0x20000000 >> Tables.ANGLETOFINESHIFT]);
        int momx = Fixed.FixedMul(newlen, Tables.finecosine[(int)(0xE0000000u >> Tables.ANGLETOFINESHIFT)]);
        int momy = Fixed.FixedMul(newlen, Tables.finesine[(int)(0xE0000000u >> Tables.ANGLETOFINESHIFT)]);
        Assert.True(momx > 0 && momy < 0); // along the line, south-east
        int y = F(360);
        x += momx;
        y += momy;
        momx = Fixed.FixedMul(momx, World.FRICTION);
        momy = Fixed.FixedMul(momy, World.FRICTION);

        w.P_Ticker();
        AssertPos(mo, x, y, 0, momx, momy, 0, "tic 3");

        for (int t = 4; t <= 8; t++)
        {
            x += momx;
            y += momy;
            momx = Fixed.FixedMul(momx, World.FRICTION);
            momy = Fixed.FixedMul(momy, World.FRICTION);
            w.P_Ticker();
            AssertPos(mo, x, y, 0, momx, momy, 0, $"tic {t}");
        }
        // It stays in front of the line, its box against it.
        Assert.Equal(0, World.P_PointOnLineSide(mo.x + mo.radius, mo.y + mo.radius, diagonal));
    }

    // ---- stepping up ----

    [Fact]
    public void SteppingUp24Units()
    {
        // From (236, 128) at 8 units a tic: tic 1 tries x = 244, whose box crosses the boundary at
        // 256 into the room with floor 24: tmfloorz 24, a step of exactly 24 is allowed. floorz
        // becomes 24 (z still 0, so friction applies); P_ZMovement then sees z < floorz: the view
        // drops by the step (viewheight 41 - 24 = 17) and starts rising (deltaviewheight
        // (41 - 17) >> 3 = 3), and z lands on the floor.
        World w = TwoRooms(0, 128, 24, 128).Player(236, 128).Load();
        mobj_t mo = Player(w);
        player_t p = w.players[0];
        Assert.Equal(player_t.VIEWHEIGHT, p.viewheight);
        mo.momx = Mom8;

        w.P_Ticker();
        AssertPos(mo, F(244), F(128), F(24), Mom8F1, 0, 0, "tic 1");
        Assert.Equal(F(24), mo.floorz);
        Assert.Equal(F(17), p.viewheight);
        Assert.Equal(F(3), p.deltaviewheight);

        w.P_Ticker();
        AssertPos(mo, F(244) + Mom8F1, F(128), F(24), Mom8F2, 0, 0, "tic 2");
        // P_CalcHeight (T4.5, before the mobj moves) raises the view again: below VIEWHEIGHT / 2
        // (20.5) it is clamped there, then it rises by deltaviewheight, which grows by 1/4 a
        // tic: 20.5 (3.25), 23.75 (3.5), 27.25 (3.75), 31 (4), 35 (4.25), 39.25 (4.5), then 41 (0).
        Assert.Equal(F(41) / 2, p.viewheight);
        Assert.Equal(F(3) + FRACUNIT / 4, p.deltaviewheight);
        int[] heights = { F(23) + 3 * FRACUNIT / 4, F(27) + FRACUNIT / 4, F(31), F(35), F(39) + FRACUNIT / 4, F(41) };
        int[] deltas = { F(3) + FRACUNIT / 2, F(3) + 3 * FRACUNIT / 4, F(4), F(4) + FRACUNIT / 4, F(4) + FRACUNIT / 2, 0 };
        for (int t = 0; t < heights.Length; t++)
        {
            w.P_Ticker();
            Assert.True((p.viewheight, p.deltaviewheight) == (heights[t], deltas[t]),
                $"tic {t + 3}: viewheight {p.viewheight}, deltaviewheight {p.deltaviewheight}");
        }
        w.P_Ticker();
        Assert.Equal((player_t.VIEWHEIGHT, 0), (p.viewheight, p.deltaviewheight));
    }

    [Fact]
    public void NotSteppingUp25Units()
    {
        // With floor 25 the move fails (too big a step up) and P_SlideMove finds the boundary
        // blocking (PTR_SlideTraverse: openbottom - z > 24). The lead corner x = 252 crosses the
        // line at x = 256 halfway along its 8-unit trace:
        //   frac = FixedDiv(FixedMul(4u >> 8, 256u), FixedMul(256u >> 8, 8u)) = 32768,
        // fudged to 30720: a move of FixedMul(8u, 30720) = 245760 (3.75 units), to x = 239.75;
        // the rest, along a vertical line, is dropped.
        World w = TwoRooms(0, 128, 25, 128).Player(236, 128).Load();
        mobj_t mo = Player(w);
        mo.momx = Mom8;

        w.P_Ticker();
        AssertPos(mo, F(236) + 245760, F(128), 0, 0, 0, 0, "tic 1");
        Assert.Equal(0, mo.floorz);
        Assert.Equal(player_t.VIEWHEIGHT, w.players[0].viewheight);
        w.P_Ticker();
        AssertPos(mo, F(236) + 245760, F(128), 0, 0, 0, 0, "tic 2");
    }

    [Fact]
    public void TryMoveStepAndFitChecks()
    {
        World w = TwoRooms(0, 128, 24, 128).Player(200, 128).Load();
        mobj_t mo = Player(w);
        // 24 up: fine; with the thing's z 1 unit lower, the step is 25.
        Assert.True(w.P_TryMove(mo, F(250), F(128)));
        Assert.True(w.floatok);
        Assert.Equal(F(24), w.tmfloorz);
        Assert.Equal(0, w.tmdropoffz);
        Assert.True(w.P_TryMove(mo, F(200), F(128)));
        mo.z = -FRACUNIT;
        Assert.False(w.P_TryMove(mo, F(250), F(128)));
        Assert.True(w.floatok); // it would fit if it were higher
        mo.z = 0;

        // A non-player without MF_DROPOFF does not walk over a drop of more than 24 units, with it it does.
        mobj_t troop = w.P_SpawnMobj(F(200), F(64), World.ONFLOORZ, mobjtype_t.MT_POSSESSED);
        troop.z = F(24);
        Assert.Equal(0, (int)(troop.flags & mobjflag_t.MF_DROPOFF));
        Assert.True(w.P_TryMove(troop, F(250), F(64))); // a 24-unit drop
        w.sectors[1].floorheight = F(25);
        Assert.False(w.P_TryMove(troop, F(252), F(64))); // 25 (and its z is 24: a 1-unit step up)
        Assert.True(w.floatok);
        troop.flags |= mobjflag_t.MF_DROPOFF;
        Assert.True(w.P_TryMove(troop, F(252), F(64)));

        // ML_BLOCKMONSTERS blocks monsters, not players; ML_BLOCKING blocks both.
        w.sectors[1].floorheight = F(24);
        line_t boundary = w.lines[w.lines.Length - 1];
        Assert.True(w.P_TryMove(troop, F(200), F(64)));
        boundary.flags |= Line.ML_BLOCKMONSTERS;
        Assert.False(w.P_TryMove(troop, F(250), F(64)));
        Assert.False(w.floatok);
        Assert.True(w.P_TryMove(mo, F(250), F(128)));
        boundary.flags |= Line.ML_BLOCKING;
        Assert.False(w.P_TryMove(mo, F(245), F(128))); // its box still across the line
        Assert.Equal(F(250), mo.x);
    }

    // ---- walking off a ledge ----

    [Fact]
    public void WalkingOffALedgeAndFalling()
    {
        // From (236, 128) on a floor at 64 towards a floor at 0 past x = 256. While the box still
        // touches the boundary (box left < 256) the floor under it is the ledge (tmfloorz 64,
        // tmdropoffz 0; the player has MF_DROPOFF), so tics 1–5 slide with friction. At tic 6 the box
        // clears the line (x = 274.06): floorz 0, z 64, so no friction from now on while airborne;
        // P_ZMovement starts the fall with momz = -2 (twice GRAVITY), then -1 a tic.
        World w = TwoRooms(64, 192, 0, 192).Player(236, 128).Load();
        mobj_t mo = Player(w);
        player_t p = w.players[0];
        Assert.Equal(F(64), mo.z);
        mo.momx = Mom8;

        int[] xs = { 15990784, 16465920, 16896512, 17286736, 17640376 };
        int[] moms = { Mom8F1, Mom8F2, Mom8F3, Mom8F4, Mom8F5 };
        for (int t = 0; t < 5; t++)
        {
            w.P_Ticker();
            AssertPos(mo, xs[t], F(128), F(64), moms[t], 0, 0, $"tic {t + 1}");
            Assert.Equal(F(64), mo.floorz);
        }

        // Tics 6–15: x += 320486 each tic; z 64, 62, 59, 55, 50, 44, 37, 29, 20, 10; momz -2 … -11.
        int x = 17640376;
        int z = F(64), momz = 0;
        for (int t = 6; t <= 15; t++)
        {
            x += Mom8F5;
            z += momz;
            momz = momz == 0 ? -2 * FRACUNIT : momz - FRACUNIT;
            w.P_Ticker();
            AssertPos(mo, x, F(128), z, Mom8F5, 0, momz, $"tic {t}");
            Assert.Equal(0, mo.floorz);
        }
        Assert.Equal(F(10), z);

        // Tic 16: z = 10 - 11 < 0: it lands. Landing faster than 8 units a tic squats the view:
        // deltaviewheight = momz >> 3 = -11u >> 3 = -90112.
        Assert.Equal(0, p.deltaviewheight);
        w.P_Ticker();
        x += Mom8F5;
        AssertPos(mo, x, F(128), 0, Mom8F5, 0, 0, "tic 16");
        Assert.Equal(-90112, p.deltaviewheight);

        // Tic 17: on the ground again, friction.
        w.P_Ticker();
        AssertPos(mo, x + Mom8F5, F(128), 0, 290440, 0, 0, "tic 17");
    }

    [Fact]
    public void ASoftLandingDoesNotSquat()
    {
        // Falling from 12 units: momz -2, -3, -4, -5 at z 12, 10, 7, 3; then lands at momz -5 > -8.
        World w = Room().Player(100, 100).Load();
        mobj_t mo = Player(w);
        mo.z = F(12);
        int[] zs = { F(12), F(10), F(7), F(3), 0 };
        int[] momzs = { -2 * FRACUNIT, -3 * FRACUNIT, -4 * FRACUNIT, -5 * FRACUNIT, 0 };
        for (int t = 0; t < zs.Length; t++)
        {
            w.P_Ticker();
            AssertPos(mo, F(100), F(100), zs[t], 0, 0, momzs[t], $"tic {t + 1}");
        }
        Assert.Equal(0, w.players[0].deltaviewheight);
    }

    // ---- things ----

    [Fact]
    public void BlockedByAThing()
    {
        // A barrel (radius 10) at (240, 128): the player (radius 16) is blocked while
        // |x - 240| < 26, so it can stand at x <= 214. From 200 at 8 units a tic: tic 1 to 208;
        // tic 2 tries 215.25: blocked, P_SlideMove finds no line and stairsteps (moves 0 in y),
        // momentum keeps decaying by friction; tic 3 tries 208 + 6.57: blocked; tic 4 tries
        // 208 + 5.95 = 213.95: free. Then 213.95 + 5.40 is blocked for good.
        World w = Room().Player(200, 128).Thing(240, 128, 2035).Load();
        mobj_t mo = Player(w);
        mo.momx = Mom8;

        int[] xs = { F(208), F(208), F(208), 14021712, 14021712, 14021712 };
        int[] moms = { Mom8F1, Mom8F2, Mom8F3, Mom8F4, Mom8F5, 290440 };
        for (int t = 0; t < xs.Length; t++)
        {
            w.P_Ticker();
            AssertPos(mo, xs[t], F(128), 0, moms[t], 0, 0, $"tic {t + 1}");
        }

        // P_CheckPosition: blocked exactly when the boxes overlap.
        Assert.True(w.P_CheckPosition(mo, F(214), F(128)));
        Assert.False(w.P_CheckPosition(mo, F(214) + 1, F(128)));
        Assert.False(w.P_CheckPosition(mo, F(214) + 1, F(128) + F(25)));
        Assert.True(w.P_CheckPosition(mo, F(214) + 1, F(128) + F(26)));

        // Non-solid things don't block: a pickup (MF_SPECIAL) and scenery without MF_SOLID.
        World w2 = Room().Player(200, 128).Thing(220, 128, 2011).Thing(200, 150, 10).Load(); // stimpack, bloody mess
        Assert.True(w2.P_CheckPosition(Player(w2), F(220), F(128)));
        Assert.True(w2.P_CheckPosition(Player(w2), F(200), F(150)));
    }

    // ---- low ceilings ----

    [Fact]
    public void FittingUnderALowCeiling()
    {
        // The player is 56 units tall: a ceiling at 56 lets it in (tmceilingz - tmfloorz = 56,
        // tmceilingz - z = 56), at 55 it does not, and it slides up to the boundary as with the
        // 25-unit step.
        World w = TwoRooms(0, 128, 0, 56).Player(236, 128).Load();
        mobj_t mo = Player(w);
        mo.momx = Mom8;
        w.P_Ticker();
        AssertPos(mo, F(244), F(128), 0, Mom8F1, 0, 0, "tic 1");
        Assert.Equal(F(56), mo.ceilingz);
        for (int t = 0; t < 10; t++)
            w.P_Ticker();
        Assert.True(mo.x > F(272) && mo.ceilingz == F(56) && mo.z == 0);

        w = TwoRooms(0, 128, 0, 55).Player(236, 128).Load();
        mo = Player(w);
        mo.momx = Mom8;
        w.P_Ticker();
        AssertPos(mo, F(236) + 245760, F(128), 0, 0, 0, 0, "tic 1 (55)");
        Assert.Equal(F(128), mo.ceilingz);
        Assert.False(w.P_TryMove(mo, F(250), F(128)));
        Assert.False(w.floatok); // doesn't fit at all

        // A raised floor under a ceiling: 24 up into a 79-unit room leaves 55.
        w = TwoRooms(0, 128, 24, 79).Player(236, 128).Load();
        Assert.False(w.P_TryMove(Player(w), F(250), F(128)));
        w = TwoRooms(0, 128, 24, 80).Player(236, 128).Load();
        Assert.True(w.P_TryMove(Player(w), F(250), F(128)));
        Assert.Equal((F(24), F(80)), (Player(w).floorz, Player(w).ceilingz));
    }

    // ---- p_mobj.c details ----

    [Fact]
    public void StopSpeedAndTheWalkingFrame()
    {
        World w = Room().Player(100, 100).Load();
        mobj_t mo = Player(w);
        player_t p = w.players[0];
        Assert.True(w.P_SetMobjState(mo, statenum_t.S_PLAY_RUN2));

        // Below STOPSPEED on both axes without move input: it moves, then stops and stands.
        mo.momx = World.STOPSPEED - 1;
        mo.momy = -(World.STOPSPEED - 1);
        w.P_Ticker();
        AssertPos(mo, F(100) + World.STOPSPEED - 1, F(100) - (World.STOPSPEED - 1), 0, 0, 0, 0, "stop");
        Assert.Equal(statenum_t.S_PLAY, mo.state);

        // With move input it slows down by friction instead (after P_MovePlayer's thrust east,
        // T4.5: FixedMul(25 * 2048, finecosine[0]) = 51199, finesine[0] gives 19 north), and walks.
        p.cmd.forwardmove = 25;
        mo.momx = World.STOPSPEED - 1;
        w.P_Ticker();
        Assert.Equal(Fixed.FixedMul(World.STOPSPEED - 1 + 51199, World.FRICTION), mo.momx);
        Assert.Equal(Fixed.FixedMul(19, World.FRICTION), mo.momy);
        Assert.Equal(statenum_t.S_PLAY_RUN1, mo.state);

        // At STOPSPEED friction applies (the test is strict).
        p.cmd.forwardmove = 0;
        mo.momx = World.STOPSPEED;
        mo.momy = 0;
        w.P_Ticker();
        Assert.Equal(Fixed.FixedMul(World.STOPSPEED, World.FRICTION), mo.momx);

        // CF_NOMOMENTUM stops at once.
        p.cheats |= player_t.CF_NOMOMENTUM;
        mo.momx = F(5);
        int x = mo.x;
        w.P_Ticker();
        Assert.Equal((x + F(5), 0), (mo.x, mo.momx));
    }

    [Fact]
    public void MaxMoveClampsAndSplitsPositiveMoves()
    {
        // Momentum is clamped to 30 units; a positive component over 15 moves in two halves
        // (xmove / 2, then the rest), a negative one in one step (vanilla only checks > MAXMOVE/2).
        World w = Room().Player(100, 128).Load();
        mobj_t mo = Player(w);
        mo.momx = F(40);
        w.P_Ticker();
        AssertPos(mo, F(130), F(128), 0, Fixed.FixedMul(World.MAXMOVE, World.FRICTION), 0, 0, "+40");

        mo.momx = F(-40);
        w.P_Ticker();
        AssertPos(mo, F(100), F(128), 0, Fixed.FixedMul(-World.MAXMOVE, World.FRICTION), 0, 0, "-40");

        // Two halves: the first one is taken, the second blocked. A 31-unit momentum (clamped to 30)
        // from x = 470: the first half to 485, the second (to 500 > 496) slides up to the wall, with
        // P_SlideMove tracing the whole momentum.
        mo.momx = F(31);
        Assert.True(w.P_TryMove(mo, F(470), F(128)));
        w.P_Ticker();
        Assert.True(mo.x > F(495) && mo.x <= F(496) && mo.momx == 0);
    }

    [Fact]
    public void SpecialLinesAreCollected()
    {
        // The boundary is a walk-over special: touching it puts it in spechit; P_TryMove's crossing
        // loop leaves numspechit at -1, as vanilla.
        TestMap map = TwoRooms(0, 128, 0, 128);
        map.Special(map.Boundaries[1], 88);
        World w = map.Player(200, 128).Load();
        mobj_t mo = Player(w);
        line_t boundary = w.lines[map.Boundaries[1]];
        Assert.True(w.P_CheckPosition(mo, F(250), F(128)));
        Assert.Equal(1, w.numspechit);
        Assert.Same(boundary, w.spechit[0]);
        Assert.True(w.P_CheckPosition(mo, F(200), F(128)));
        Assert.Equal(0, w.numspechit);
        Assert.True(w.P_TryMove(mo, F(250), F(128)));
        Assert.Equal(-1, w.numspechit);
        Assert.Equal(0, w.spechitoverruns);

        // More than MAXSPECIALCROSS special lines under one box: 20 rooms 2 units wide.
        var rooms = new TestMap.Room[22];
        rooms[0] = new TestMap.Room(100, 0, 128);
        for (int i = 1; i < 21; i++)
            rooms[i] = new TestMap.Room(2, 0, 128);
        rooms[21] = new TestMap.Room(100, 0, 128);
        map = TestMap.Strip(0, 0, 256, rooms);
        for (int i = 1; i < rooms.Length; i++)
            map.Special(map.Boundaries[i], 88);
        w = map.Player(50, 128).Load();
        // The box 104–136 crosses the boundaries at 106, 108, … 134 (its edges on a line are no contact): 15,
        // all listed in one block, in line order.
        Assert.True(w.P_CheckPosition(Player(w), F(120), F(128)));
        Assert.Equal(15, w.numspechit);
        Assert.Equal(15 - World.MAXSPECIALCROSS, w.spechitoverruns);
        for (int i = 0; i < 15; i++)
            Assert.Equal(F(106 + 2 * i), w.spechit[i]!.v1.X);
    }

    [Fact]
    public void MissilesExplodeOnWallsExceptAgainstTheSky()
    {
        World w = Room().Player(100, 128).Load();
        mobj_t rocket = w.P_SpawnMobj(F(480), F(128), F(32), mobjtype_t.MT_ROCKET);
        rocket.momx = F(30); // two halves: to 495, then 510 (radius 11: blocked)
        int index = w.random.prndindex;
        w.P_Ticker();
        Assert.Equal(0, (int)(rocket.flags & mobjflag_t.MF_MISSILE));
        Assert.Equal((0, 0, 0), (rocket.momx, rocket.momy, rocket.momz));
        Assert.Equal(statenum_t.S_EXPLODE1, rocket.state);
        int rnd = DoomRandom.rndtable[(index + 1) & 0xff];
        // P_ExplodeMissile's 0–3 tics off, then the tic's own state countdown
        Assert.Equal(System.Math.Max(1, Info.states[(int)statenum_t.S_EXPLODE1].tics - (rnd & 3)) - 1, rocket.tics);
        Assert.Equal(F(495), rocket.x); // a missile does not slide
        Assert.Equal(think_t.P_MobjThinker, rocket.function);

        // Flying from a sky room into a lower ceiling: the line's back sector is the sky room,
        // so the rocket vanishes instead.
        TestMap map = TwoRooms(0, 128, 0, 40).CeilingPic(0, World.SKYFLATNAME);
        w = map.Player(100, 128).Load();
        rocket = w.P_SpawnMobj(F(230), F(128), F(48), mobjtype_t.MT_ROCKET);
        rocket.momx = F(20);
        w.P_Ticker();
        Assert.Equal(think_t.REMOVED, rocket.function);
        Assert.Same(w.lines[map.Boundaries[1]], w.ceilingline);
    }

    [Fact]
    public void ThingsStandStillWithoutMomentum()
    {
        // A level without input keeps every mobj where it spawned (z on the floor, no momentum).
        World w = TwoRooms(0, 128, 24, 128).Player(100, 128).Thing(300, 100, 2035).Thing(300, 200, 3004).Load();
        var before = new System.Collections.Generic.List<(int, int, int)>();
        foreach (mobj_t mo in w.Mobjs())
            before.Add((mo.x, mo.y, mo.z));
        for (int t = 0; t < 35; t++)
            w.P_Ticker();
        var after = new System.Collections.Generic.List<(int, int, int)>();
        foreach (mobj_t mo in w.Mobjs())
            after.Add((mo.x, mo.y, mo.z));
        Assert.Equal(before, after);
    }

    [Fact]
    public void Doom1E1M1PlayerWandersDeterministically()
    {
        // The player walking by ticcmd (forwardmove 64: a thrust of 2 units, turning by
        // angleturn 728, about 4° a tic) for 20 seconds bounces around the start room and
        // beyond: two worlds stay identical, the links hold, and it always stands where
        // P_CheckPosition allows, on or above its floor.
        WadArchive wad = WadArchive.Open(TestWads.RequireDoom1());
        World Load()
        {
            var world = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium), Tweaks.Vanilla);
            world.G_DoLoadLevel(Level.Load(wad, "E1M1"));
            return world;
        }
        World a = Load(), b = Load();
        int startx = Player(a).x, starty = Player(a).y;
        var cmd = new ticcmd_t { forwardmove = 64, angleturn = 728 };
        int far = 0;
        for (int tic = 0; tic < 700; tic++)
        {
            a.G_Ticker(cmd);
            b.G_Ticker(cmd);
            Assert.Equal(a.Checksum(), b.Checksum());
            mobj_t p = Player(a);
            Assert.True(p.z >= p.floorz && p.z + p.height <= p.ceilingz, $"tic {tic}: z {p.z} outside {p.floorz}–{p.ceilingz}");
            Assert.True(a.P_CheckPosition(p, p.x, p.y), $"tic {tic}: {p} stuck");
            Assert.Same(a.R_PointInSubsector(p.x, p.y), p.subsector);
            far = System.Math.Max(far, World.P_AproxDistance(p.x - startx, p.y - starty));
        }
        WorldTests.CheckLinks(a);
        Assert.True(far > F(64), $"moved only {far >> 16} units");
    }
}
