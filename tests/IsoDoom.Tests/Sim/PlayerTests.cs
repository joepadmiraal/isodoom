using System;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T4.5: p_user.c's player movement driven by <c>ticcmd</c>s on test maps
/// (<see cref="TestMap"/>), in vanilla mode (relative turning, walk and run
/// speeds, strafing, <c>MAXPLMOVE</c>, no control in the air, the view height
/// and bob) and with the twin-stick tweaks (absolute angle, movement in world
/// directions at one speed whatever the facing). Expected values come from
/// vanilla's arithmetic: in a tic <c>P_PlayerThink</c> thrusts first
/// (<c>mom += thrust</c>), then <c>P_XYMovement</c> moves by the momentum and
/// applies friction (<c>mom = FixedMul(mom, FRICTION)</c>).
/// </summary>
public class PlayerTests
{
    private const int FRACUNIT = 1 << 16;
    private static int F(int units) => units * FRACUNIT;

    private static mobj_t Player(World w) => w.players[0].mo!;

    private static readonly Tweaks _twinStick = Tweaks.Vanilla with { AbsoluteAiming = true, AbsoluteMovement = true };

    // A 2048×2048 room: room to walk in every direction from the middle.
    private static World BigRoom(int angle = 0, Tweaks? tweaks = null) =>
        TestMap.Strip(-1024, -1024, 1024, new TestMap.Room(2048, 0, 128)).Player(0, 0, angle).Load(tweaks: tweaks);

    private static int Cos(uint angle) => Tables.finecosine[(int)(angle >> Tables.ANGLETOFINESHIFT)];
    private static int Sin(uint angle) => Tables.finesine[(int)(angle >> Tables.ANGLETOFINESHIFT)];

    /// <summary>
    /// A model of a player walking in the open: per tic the thrust is added,
    /// the position moves by the momentum, then friction. Checks the world
    /// against it for <paramref name="tics"/> tics (with <c>P_XYMovement</c>'s
    /// <c>MAXMOVE</c> clamp and half steps).
    /// </summary>
    private static void WalkAndCheck(World w, ticcmd_t cmd, int thrustx, int thrusty, int tics)
    {
        mobj_t mo = Player(w);
        int x = mo.x, y = mo.y, momx = mo.momx, momy = mo.momy;
        for (int t = 1; t <= tics; t++)
        {
            momx = Math.Clamp(momx + thrustx, -World.MAXMOVE, World.MAXMOVE);
            momy = Math.Clamp(momy + thrusty, -World.MAXMOVE, World.MAXMOVE);
            if (momx > World.MAXMOVE / 2 || momy > World.MAXMOVE / 2)
            {
                // P_XYMovement's two halves (xmove / 2, then xmove >> 1: an odd positive move loses its last bit)
                x += momx / 2 + (momx >> 1);
                y += momy / 2 + (momy >> 1);
            }
            else
            {
                x += momx;
                y += momy;
            }
            momx = Fixed.FixedMul(momx, World.FRICTION);
            momy = Fixed.FixedMul(momy, World.FRICTION);
            w.G_Ticker(cmd);
            Assert.True((mo.x, mo.y, mo.momx, mo.momy) == (x, y, momx, momy),
                $"tic {t}: got ({mo.x}, {mo.y}) mom ({mo.momx}, {mo.momy}), expected ({x}, {y}) mom ({momx}, {momy})");
        }
    }

    /// <summary>The distance moved in the last tic.</summary>
    private static int Speed(int momx, int momy) => (int)Math.Sqrt((double)momx * momx + (double)momy * momy);

    // ---- vanilla: turning ----

    [Fact]
    public void AngleturnTurnsRelatively()
    {
        // angleturn << 16 is added each tic, wrapping (demos and G_BuildTiccmd turn this way).
        World w = BigRoom(90);
        mobj_t mo = Player(w);
        Assert.Equal(Tables.ANG90, mo.angle);
        uint angle = Tables.ANG90;
        short[] turns = [640, 640, 1280, -320, -32768, 32767, 0];
        foreach (short turn in turns)
        {
            w.G_Ticker(new ticcmd_t { angleturn = turn });
            angle = unchecked(angle + (uint)(turn << 16));
            Assert.Equal(angle, mo.angle);
        }
        // Turning does not move.
        Assert.Equal((0, 0, 0, 0), (mo.x, mo.y, mo.momx, mo.momy));

        // A full turn of slow turns: 65536 / 320 = 204.8 tics.
        w = BigRoom(0);
        for (int t = 0; t < 1024; t++)
            w.G_Ticker(new ticcmd_t { angleturn = Ticcmds.angleturn[2] });
        Assert.Equal(0u, Player(w).angle); // 1024 × 320 = 5 × 65536
    }

    [Fact]
    public void TurnsBeforeThrusting()
    {
        // P_MovePlayer turns first, so the thrust of the same tic goes along the new angle.
        World w = BigRoom(0);
        var cmd = new ticcmd_t { forwardmove = 25, angleturn = 16384 }; // a quarter turn
        w.G_Ticker(cmd);
        mobj_t mo = Player(w);
        Assert.Equal(Tables.ANG90, mo.angle);
        Assert.Equal((Fixed.FixedMul(25 * 2048, Cos(Tables.ANG90)), Fixed.FixedMul(25 * 2048, Sin(Tables.ANG90))),
            (mo.x, mo.y));
    }

    // ---- vanilla: speeds ----

    [Fact]
    public void WalkingAndRunningSpeeds()
    {
        // Facing east, forwardmove 25 (walk): a thrust of FixedMul(51200, finecosine[0]) = 51199
        // east and FixedMul(51200, finesine[0]) = 19 north a tic (vanilla's tables are half a
        // fine angle off). Tic 1 moves 51199, the momentum becomes 46399 after friction.
        Assert.Equal((65535, 25), (Cos(0), Sin(0)));
        World w = BigRoom(0);
        var walk = new ticcmd_t { forwardmove = Ticcmds.forwardmove[0] };
        w.G_Ticker(walk);
        Assert.Equal((51199, 19, 46399, 17), (Player(w).x, Player(w).y, Player(w).momx, Player(w).momy));

        // The model for 2 seconds; the speed tends to thrust × 0.90625 / 0.09375 (8.33 units a tic).
        WalkAndCheck(w, walk, 51199, 19, 69);
        int walkSpeed = Player(w).momx + Fixed.FixedMul(51199, World.FRICTION); // the next tic's move
        Assert.InRange(walkSpeed, 8 * FRACUNIT + FRACUNIT / 4, 8 * FRACUNIT + FRACUNIT / 3 + 1);

        // Running (forwardmove 50, MAXPLMOVE): twice the thrust, 16.67 units a tic.
        Assert.Equal(Ticcmds.MAXPLMOVE, Ticcmds.forwardmove[1]);
        w = BigRoom(0);
        var run = new ticcmd_t { forwardmove = Ticcmds.forwardmove[1] };
        WalkAndCheck(w, run, Fixed.FixedMul(50 * 2048, 65535), Fixed.FixedMul(50 * 2048, 25), 70);
        Assert.InRange(Player(w).momx, 15 * FRACUNIT, 15 * FRACUNIT + FRACUNIT / 8); // 16.67 × 0.90625 after friction

        // Backwards and facing other ways: the same along the facing.
        foreach (int facing in new[] { 45, 90, 135, 180, 225, 270, 315 })
        {
            uint a = Tables.ANG45 * (uint)(facing / 45);
            w = BigRoom(facing);
            WalkAndCheck(w, run, Fixed.FixedMul(50 * 2048, Cos(a)), Fixed.FixedMul(50 * 2048, Sin(a)), 20);
            w = BigRoom(facing);
            var back = new ticcmd_t { forwardmove = (sbyte)-Ticcmds.forwardmove[0] };
            WalkAndCheck(w, back, Fixed.FixedMul(-25 * 2048, Cos(a)), Fixed.FixedMul(-25 * 2048, Sin(a)), 20);
        }
    }

    [Fact]
    public void StrafingAndStraferunning()
    {
        // sidemove thrusts along angle - ANG90 (to the right): facing north, east.
        World w = BigRoom(90);
        var strafe = new ticcmd_t { sidemove = Ticcmds.sidemove[1] }; // 40
        WalkAndCheck(w, strafe, Fixed.FixedMul(40 * 2048, Cos(0)), Fixed.FixedMul(40 * 2048, Sin(0)), 35);
        w = BigRoom(90);
        WalkAndCheck(w, new ticcmd_t { sidemove = (sbyte)-Ticcmds.sidemove[0] },
            Fixed.FixedMul(-24 * 2048, Cos(0)), Fixed.FixedMul(-24 * 2048, Sin(0)), 35);

        // Forward and side together add up: vanilla's straferunning is faster than running
        // (|(50, 40)| = 64 against 50), and the sim takes moves past MAXPLMOVE as they come
        // (G_BuildTiccmd clamps, demos can hold up to 127).
        w = BigRoom(90);
        var sr50 = new ticcmd_t { forwardmove = 50, sidemove = 40 };
        WalkAndCheck(w, sr50, Fixed.FixedMul(50 * 2048, Cos(Tables.ANG90)) + Fixed.FixedMul(40 * 2048, Cos(0)),
            Fixed.FixedMul(50 * 2048, Sin(Tables.ANG90)) + Fixed.FixedMul(40 * 2048, Sin(0)), 40);
        int srSpeed = Speed(Player(w).momx, Player(w).momy);
        w = BigRoom(90);
        WalkAndCheck(w, new ticcmd_t { forwardmove = 50 }, Fixed.FixedMul(50 * 2048, Cos(Tables.ANG90)), Fixed.FixedMul(50 * 2048, Sin(Tables.ANG90)), 40);
        int runSpeed = Speed(Player(w).momx, Player(w).momy);
        Assert.InRange(srSpeed * 50L, runSpeed * 63L, runSpeed * 65L);

        // (Past 15 units a tic it moves in two halves, past 30 the momentum is clamped: MovementTests.)
        w = BigRoom(90);
        WalkAndCheck(w, new ticcmd_t { forwardmove = 127 }, Fixed.FixedMul(127 * 2048, Cos(Tables.ANG90)), Fixed.FixedMul(127 * 2048, Sin(Tables.ANG90)), 25);
    }

    // ---- vanilla: walking frames, air control, reaction time ----

    [Fact]
    public void MoveInputStartsTheWalkingFrames()
    {
        World w = BigRoom(0);
        mobj_t mo = Player(w);
        Assert.Equal(statenum_t.S_PLAY, mo.state);
        w.G_Ticker(new ticcmd_t { angleturn = 640 }); // turning alone does not walk
        Assert.Equal(statenum_t.S_PLAY, mo.state);
        w.G_Ticker(new ticcmd_t { sidemove = 24 });
        // S_PLAY_RUN1 is entered with 4 tics, then P_MobjThinker counts one down.
        Assert.Equal((statenum_t.S_PLAY_RUN1, 3), (mo.state, mo.tics));
        for (int t = 0; t < 3; t++)
            w.G_Ticker(new ticcmd_t { sidemove = 24 });
        Assert.Equal(statenum_t.S_PLAY_RUN2, mo.state);
        // Letting go: friction until below STOPSPEED, then the standing frame.
        int tics = 0;
        while (mo.momx != 0 || mo.momy != 0)
        {
            w.G_Ticker(new ticcmd_t());
            tics++;
        }
        Assert.Equal(statenum_t.S_PLAY, mo.state);
        Assert.InRange(tics, 20, 60);
    }

    [Fact]
    public void NoControlInTheAir()
    {
        // Running off a ledge (floor 64 to 0 at x = 256): while z > floorz, P_MovePlayer does not
        // thrust and P_XYMovement applies no friction, so the momentum stays as it was.
        World w = TestMap.Strip(0, 0, 256, new TestMap.Room(256, 64, 192), new TestMap.Room(512, 0, 192)).Player(180, 128).Load();
        mobj_t mo = Player(w);
        var run = new ticcmd_t { forwardmove = 50 };
        while (mo.z == mo.floorz)
            w.G_Ticker(run);
        Assert.True(mo.x - mo.radius >= F(256));
        int momx = mo.momx, momy = mo.momy;
        while (mo.z > mo.floorz)
        {
            w.G_Ticker(run);
            Assert.False(w.onground);
            if (mo.z > mo.floorz)
                Assert.Equal((momx, momy), (mo.momx, mo.momy));
        }
        // Landed: the next tic thrusts again.
        w.G_Ticker(run);
        Assert.True(w.onground);
        Assert.Equal(Fixed.FixedMul(momx + Fixed.FixedMul(50 * 2048, 65535), World.FRICTION), mo.momx);
    }

    [Fact]
    public void ReactionTimeHoldsThePlayer()
    {
        // After a teleport (reactiontime 18) the player neither turns nor moves while it counts down.
        World w = BigRoom(0);
        mobj_t mo = Player(w);
        mo.reactiontime = 3;
        var cmd = new ticcmd_t { forwardmove = 50, angleturn = 640 };
        for (int t = 0; t < 3; t++)
            w.G_Ticker(cmd);
        Assert.Equal((0, 0, 0u, 0), (mo.x, mo.y, mo.angle, mo.reactiontime));
        w.G_Ticker(cmd);
        Assert.Equal(640u << 16, mo.angle);
        Assert.True(mo.x > 0);
    }

    // ---- vanilla: view height ----

    [Fact]
    public void ViewHeightAndBob()
    {
        World w = BigRoom(0);
        player_t p = w.players[0];
        mobj_t mo = Player(w);
        // Standing: viewz = z + 41 (the first tic replaces P_SetupLevel's placeholder).
        w.G_Ticker(new ticcmd_t());
        Assert.Equal((player_t.VIEWHEIGHT, 0, F(41)), (p.viewheight, p.bob, p.viewz));

        // Walking: P_CalcHeight runs after the thrust and before the move. Tic 1 (leveltime 1):
        // mom (51199, 19): bob = (FixedMul(51199, 51199) + 0) >> 2 = 39997 >> 2 = 9999, the
        // view swings by FixedMul(bob / 2, finesine[409 × leveltime]).
        var walk = new ticcmd_t { forwardmove = 25 };
        for (int t = 0; t < 20; t++)
        {
            int leveltime = w.leveltime;
            int momx = mo.momx + 51199, momy = mo.momy + 19;
            w.G_Ticker(walk);
            int bob = Math.Min((Fixed.FixedMul(momx, momx) + Fixed.FixedMul(momy, momy)) >> 2, World.MAXBOB);
            Assert.Equal(bob, p.bob);
            int swing = Fixed.FixedMul(bob / 2, Tables.finesine[(Tables.FINEANGLES / 20 * leveltime) & Tables.FINEMASK]);
            Assert.Equal(mo.z + player_t.VIEWHEIGHT + swing, p.viewz);
            if (t == 0)
                Assert.Equal(9999, bob);
        }

        // Running hard enough reaches MAXBOB (16 units: momentum ≥ 8 units a tic).
        for (int t = 0; t < 30; t++)
            w.G_Ticker(new ticcmd_t { forwardmove = 50 });
        Assert.Equal(World.MAXBOB, p.bob);

        // The view stays 4 units below the ceiling: a 44-unit ceiling over the player.
        w = TestMap.Strip(0, 0, 256, new TestMap.Room(256, 0, 44)).Player(100, 100).Load();
        w.G_Ticker(new ticcmd_t());
        Assert.Equal(F(40), w.players[0].viewz);
    }

    [Fact]
    public void ViewRecoversFromAStepUpAndASquat()
    {
        // Walking up a 24-unit step by ticcmd: P_ZMovement drops the view by the step, P_CalcHeight
        // raises it back to 41 (clamped at 20.5 first, then 1/4 faster each tic).
        World w = TestMap.Strip(0, 0, 256, new TestMap.Room(256, 0, 128), new TestMap.Room(256, 24, 128)).Player(200, 128).Load();
        player_t p = w.players[0];
        mobj_t mo = Player(w);
        var run = new ticcmd_t { forwardmove = 50 };
        while (mo.z == 0)
            w.G_Ticker(run);
        Assert.Equal(F(24), mo.z);
        Assert.Equal(F(17), p.viewheight);
        Assert.Equal(F(3), p.deltaviewheight);
        w.G_Ticker(run);
        Assert.Equal(F(41) / 2, p.viewheight);
        int tics = 1;
        while (p.viewheight < player_t.VIEWHEIGHT)
        {
            int before = p.viewheight;
            w.G_Ticker(run);
            Assert.True(p.viewheight > before);
            tics++;
        }
        Assert.Equal((7, 0), (tics, p.deltaviewheight));

        // A hard landing (momz -11: deltaviewheight -90112) squats the view and brings it back:
        // 41 - 1.375 = 39.625 (delta -1.125), 38.5 (-0.875), 37.625 (-0.625), 37 (-0.375),
        // 36.625 (-0.125), 36.5 (+0.125), 36.625, 37, 37.625, 38.5, 39.625 (1.375), 41 (1.625), then
        // 42.625 is clamped to 41 (0).
        w = TestMap.Strip(0, 0, 256, new TestMap.Room(256, 0, 192)).Player(100, 100).Load();
        p = w.players[0];
        mo = Player(w);
        mo.z = F(64); // falls 0, 2, 3, … 10 units to z 10, then lands at momz -11 (as MovementTests' ledge)
        while (mo.z > 0)
            w.G_Ticker(new ticcmd_t());
        Assert.Equal(-90112, p.deltaviewheight);
        int[] heights = [2596864, 2523136, 2465792, 2424832, 2400256, 2392064, 2400256, 2424832, 2465792, 2523136, 2596864, F(41), F(41)];
        for (int t = 0; t < heights.Length; t++)
        {
            w.G_Ticker(new ticcmd_t());
            Assert.True(heights[t] == p.viewheight, $"tic {t + 1}: viewheight {p.viewheight}, expected {heights[t]}");
            Assert.Equal(p.viewheight, p.viewz);
        }
        Assert.Equal(0, p.deltaviewheight);
    }

    // ---- vanilla: the rest of P_PlayerThink ----

    [Fact]
    public void NoclipCheatAndChainsawLunge()
    {
        World w = BigRoom(0);
        player_t p = w.players[0];
        mobj_t mo = Player(w);
        p.cheats |= player_t.CF_NOCLIP;
        w.G_Ticker(new ticcmd_t());
        Assert.NotEqual(0, (int)(mo.flags & mobjflag_t.MF_NOCLIP));
        p.cheats = 0;
        w.G_Ticker(new ticcmd_t());
        Assert.Equal(0, (int)(mo.flags & mobjflag_t.MF_NOCLIP));

        // MF_JUSTATTACKED (A_Saw): this tic's command becomes forwardmove 100, no turn, no strafe.
        mo.flags |= mobjflag_t.MF_JUSTATTACKED;
        w.G_Ticker(new ticcmd_t { forwardmove = -50, sidemove = 40, angleturn = 640 });
        Assert.Equal(0, (int)(mo.flags & mobjflag_t.MF_JUSTATTACKED));
        Assert.Equal((0u, Fixed.FixedMul(100 * 2048, 65535)), (mo.angle, mo.x));
        Assert.Equal((100, 0, 0), (p.cmd.forwardmove, p.cmd.sidemove, p.cmd.angleturn));
    }

    [Fact]
    public void PowerCountersAndColormaps()
    {
        World w = BigRoom(0);
        player_t p = w.players[0];
        mobj_t mo = Player(w);
        p.powers[(int)powertype_t.pw_strength] = 1;
        p.powers[(int)powertype_t.pw_invulnerability] = 4 * 32 + 2;
        p.powers[(int)powertype_t.pw_invisibility] = 2;
        p.powers[(int)powertype_t.pw_infrared] = 10;
        p.powers[(int)powertype_t.pw_ironfeet] = 5;
        p.damagecount = 3;
        p.bonuscount = 1;
        mo.flags |= mobjflag_t.MF_SHADOW;

        w.G_Ticker(new ticcmd_t());
        Assert.Equal(2, p.powers[(int)powertype_t.pw_strength]); // counts up
        Assert.Equal(4 * 32 + 1, p.powers[(int)powertype_t.pw_invulnerability]);
        Assert.Equal(World.INVERSECOLORMAP, p.fixedcolormap);
        Assert.NotEqual(0, (int)(mo.flags & mobjflag_t.MF_SHADOW));
        Assert.Equal((2, 0), (p.damagecount, p.bonuscount));

        w.G_Ticker(new ticcmd_t());
        Assert.Equal(0, (int)(mo.flags & mobjflag_t.MF_SHADOW)); // invisibility ran out
        // 128 left: not > 4 × 32, and 128 & 8 == 0: the inverse map blinks off.
        Assert.Equal((4 * 32, 0), (p.powers[(int)powertype_t.pw_invulnerability], p.fixedcolormap));
        w.G_Ticker(new ticcmd_t()); // 127: & 8
        Assert.Equal(World.INVERSECOLORMAP, p.fixedcolormap);

        // Infrared alone: colormap 1 while > 128 or bit 3 set; 7 left now.
        p.powers[(int)powertype_t.pw_invulnerability] = 0;
        w.G_Ticker(new ticcmd_t());
        Assert.Equal((6, 0), (p.powers[(int)powertype_t.pw_infrared], p.fixedcolormap));
        p.powers[(int)powertype_t.pw_infrared] = 9;
        w.G_Ticker(new ticcmd_t());
        Assert.Equal((8, 1), (p.powers[(int)powertype_t.pw_infrared], p.fixedcolormap));
        Assert.Equal(0, p.powers[(int)powertype_t.pw_ironfeet]);
    }

    [Fact]
    public void GTickerCopiesTheCommandsOfPlayersInTheGame()
    {
        World w = BigRoom(0);
        var cmds = new ticcmd_t[World.MAXPLAYERS];
        cmds[0].forwardmove = 25;
        cmds[1].forwardmove = 50; // player 2 is not in the game
        w.G_Ticker(cmds);
        Assert.Equal(25, w.players[0].cmd.forwardmove);
        Assert.Equal(0, w.players[1].cmd.forwardmove);
        Assert.Equal(51199, Player(w).x);
        Assert.Equal(1, w.leveltime);
    }

    // ---- vanilla: walls by ticcmd ----

    [Fact]
    public void RunningIntoAWallAndAlongIt()
    {
        // Running east at the 512×256 room's east wall: the player ends against it (x ≤ 496) and
        // stays there while the command holds; running north-east it slides north along it to the
        // corner (y ≤ 240).
        World w = TestMap.Strip(0, 0, 256, new TestMap.Room(512, 0, 128)).Player(100, 128).Load();
        mobj_t mo = Player(w);
        var run = new ticcmd_t { forwardmove = 50 };
        for (int t = 0; t < 60; t++)
        {
            w.G_Ticker(run);
            Assert.True(mo.x <= F(496) && w.P_CheckPosition(mo, mo.x, mo.y), $"tic {t}: x {mo.x}");
        }
        Assert.True(mo.x > F(495));
        Assert.InRange(mo.y, F(128), F(128) + FRACUNIT); // the 19-a-tic drift north

        w.G_Ticker(new ticcmd_t { angleturn = 8192 }); // face north-east
        for (int t = 0; t < 60; t++)
            w.G_Ticker(run);
        Assert.InRange(mo.x, F(495), F(496));
        Assert.True(mo.y > F(238) && mo.y <= F(240), $"y {mo.y}");
    }

    // ---- twin-stick tweaks ----

    [Fact]
    public void TopDownTweaksIncludeTheTwinStickOnes()
    {
        Assert.True(Tweaks.TopDown.AbsoluteAiming);
        Assert.True(Tweaks.TopDown.AbsoluteMovement);
        Assert.False(Tweaks.Vanilla.AbsoluteAiming);
        Assert.False(Tweaks.Vanilla.AbsoluteMovement);
    }

    [Fact]
    public void AbsoluteAimingSetsTheAngle()
    {
        World w = BigRoom(90, _twinStick);
        mobj_t mo = Player(w);
        // angleturn is the angle's upper 16 bits: each tic sets it, a repeated command does not turn further.
        uint[] angles = [0, Tables.ANG90, Tables.ANG180 + 12345678, Tables.ANG270, 0xffffffffu, 0x7fff8000u, Tables.ANG45 * 3];
        foreach (uint a in angles)
        {
            var cmd = new ticcmd_t { angleturn = Ticcmds.AbsoluteAngle(a) };
            uint expected = unchecked((a + 0x8000) & 0xffff0000u); // rounded to the nearest 16-bit angle
            w.G_Ticker(cmd);
            Assert.Equal(expected, mo.angle);
            w.G_Ticker(cmd);
            Assert.Equal(expected, mo.angle);
        }
        Assert.Equal((short)-32768, Ticcmds.AbsoluteAngle(Tables.ANG180));
        Assert.Equal((short)0, Ticcmds.AbsoluteAngle(0xffff8000u)); // rounds up past 360°

        // Absolute aiming alone keeps vanilla's facing-relative movement: thrust along the set angle.
        w = BigRoom(0, Tweaks.Vanilla with { AbsoluteAiming = true });
        w.G_Ticker(new ticcmd_t { forwardmove = 25, angleturn = Ticcmds.AbsoluteAngle(Tables.ANG90) });
        Assert.Equal((Fixed.FixedMul(51200, Cos(Tables.ANG90)), Fixed.FixedMul(51200, Sin(Tables.ANG90))), (Player(w).x, Player(w).y));
    }

    [Fact]
    public void AbsoluteMovementIgnoresTheFacing()
    {
        // forwardmove thrusts north, sidemove east, exactly (forwardmove × 2048), whatever the facing.
        foreach (int facing in new[] { 0, 45, 90, 135, 180, 225, 270, 315 })
        {
            World w = BigRoom(facing, Tweaks.Vanilla with { AbsoluteMovement = true });
            WalkAndCheck(w, new ticcmd_t { forwardmove = 35, sidemove = -35 }, -35 * 2048, 35 * 2048, 35);
            Assert.Equal(Tables.ANG45 * (uint)(facing / 45), Player(w).angle); // and does not turn
        }
    }

    [Fact]
    public void EveryMoveDirectionAtTheSameSpeedWhateverTheFacing()
    {
        // The builder's encoding (Ticcmds.AbsoluteMove) gives each world direction a vector of
        // length forwardmove[run] (25 or 50) within the rounding of each axis to whole units; the
        // sim moves the same way for every facing. 32 directions × 8 facings.
        foreach (bool runs in new[] { false, true })
        {
            int speed = Ticcmds.TwinStickSpeed(runs);
            Assert.Equal(runs ? 50 : 25, speed);
            for (int d = 0; d < 32; d++)
            {
                uint direction = (uint)d << 27;
                var cmd = new ticcmd_t();
                Ticcmds.AbsoluteMove(ref cmd, direction, speed);
                double length = Math.Sqrt(cmd.forwardmove * cmd.forwardmove + cmd.sidemove * cmd.sidemove);
                Assert.InRange(length, speed - 0.71, speed + 0.71);
                double off = Math.Atan2(cmd.forwardmove, cmd.sidemove) - d * Math.PI / 16;
                off = Math.IEEERemainder(off, 2 * Math.PI);
                Assert.InRange(Math.Abs(off), 0, 0.71 / speed + 0.001);

                (int, int, int, int)? first = null;
                for (int facing = 0; facing < 360; facing += 45)
                {
                    World w = BigRoom(facing, _twinStick);
                    cmd.angleturn = Ticcmds.AbsoluteAngle(Tables.ANG45 * (uint)(facing / 45));
                    WalkAndCheck(w, cmd, cmd.sidemove * 2048, cmd.forwardmove * 2048, 10);
                    mobj_t mo = Player(w);
                    (int x, int y, int momx, int momy) state = (mo.x, mo.y, mo.momx, mo.momy);
                    first ??= state;
                    Assert.Equal(first, state);
                }
            }
        }

        // Keyboard diagonals: (18, 18) walking and (35, 35) running, no faster than straight
        // (vanilla's straferunning diagonal is |(50, 40)| = 64).
        var diag = new ticcmd_t();
        Ticcmds.AbsoluteMove(ref diag, Tables.ANG45, 50);
        Assert.Equal((35, 35), (diag.forwardmove, diag.sidemove));
        Ticcmds.AbsoluteMove(ref diag, Tables.ANG45 * 3, 25);
        Assert.Equal((18, -18), (diag.forwardmove, diag.sidemove));
        Ticcmds.AbsoluteMove(ref diag, Tables.ANG270, 50);
        Assert.Equal((-50, 0), (diag.forwardmove, diag.sidemove));
        Ticcmds.AbsoluteMove(ref diag, 0, 50);
        Assert.Equal((0, 50), (diag.forwardmove, diag.sidemove));
    }

    [Fact]
    public void TwinStickTerminalSpeeds()
    {
        // Running in any direction tends to the vanilla running speed straight ahead, 16.67 units a tic.
        for (int d = 0; d < 8; d++)
        {
            World w = BigRoom(0, _twinStick);
            var cmd = new ticcmd_t { angleturn = Ticcmds.AbsoluteAngle(Tables.ANG45 * 5) };
            Ticcmds.AbsoluteMove(ref cmd, Tables.ANG45 * (uint)d, Ticcmds.TwinStickSpeed(true));
            for (int t = 0; t < 45; t++)
                w.G_Ticker(cmd);
            mobj_t mo = Player(w);
            int move = Speed(mo.momx + cmd.sidemove * 2048, mo.momy + cmd.forwardmove * 2048);
            Assert.InRange(move, 16 * FRACUNIT, 17 * FRACUNIT);
            Assert.Equal(Tables.ANG45 * 5, mo.angle);
        }
    }

    [Fact]
    public void TwinStickChainsawLungeGoesAlongTheFacing()
    {
        World w = BigRoom(0, _twinStick);
        mobj_t mo = Player(w);
        uint aim = Tables.ANG45 * 3; // north-west
        w.G_Ticker(new ticcmd_t { angleturn = Ticcmds.AbsoluteAngle(aim) });
        int x = mo.x, y = mo.y;
        mo.flags |= mobjflag_t.MF_JUSTATTACKED;
        w.G_Ticker(new ticcmd_t { angleturn = Ticcmds.AbsoluteAngle(0), forwardmove = -50 });
        Assert.Equal(aim, mo.angle); // the lunge keeps the angle
        var lunge = new ticcmd_t();
        Ticcmds.AbsoluteMove(ref lunge, aim, 100);
        Assert.Equal((71, -71), (lunge.forwardmove, lunge.sidemove));
        Assert.Equal((x - 71 * 2048, y + 71 * 2048), (mo.x, mo.y));
    }
}
