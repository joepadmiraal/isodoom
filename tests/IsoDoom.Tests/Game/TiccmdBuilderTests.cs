using System;
using IsoDoom.Game;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Wad;
using IsoDoom.Tests.Support;
using Xunit;

namespace IsoDoom.Tests.Game;

/// <summary>
/// T4.6: the presentation's <c>G_BuildTiccmd</c> (<see cref="TiccmdBuilder"/>):
/// the <c>ticcmd</c> for every WASD combination, run and the run toggle, stick
/// directions and deflections, cursor positions and the right stick's aim,
/// the buttons, vanilla's command (forward, strafe, slow then fast turning),
/// and absolute aiming alone (the move against the new facing). Expected
/// moves come from a double-precision model of the screen direction.
/// </summary>
public class TiccmdBuilderTests
{
    private const int FRACUNIT = Fixed.FRACUNIT;

    /// <summary>The iso camera's screen up (IsoCamera.Yaw 45°): map north-west.</summary>
    private const uint IsoUp = Tables.ANG90 + Tables.ANG45;

    private static readonly Tweaks TwinStick = Tweaks.TopDown;

    private static ticcmd_t Build(TiccmdInput input, Tweaks? tweaks = null, uint screenUp = IsoUp, uint playerAngle = Tables.ANG90, TiccmdBuilder? builder = null) =>
        (builder ?? new TiccmdBuilder()).G_BuildTiccmd(input, tweaks ?? TwinStick, screenUp, 0, 0, playerAngle);

    private static TiccmdInput Keys(bool w = false, bool a = false, bool s = false, bool d = false, bool run = false) => new()
    {
        MoveX = (d ? 1 : 0) - (a ? 1 : 0),
        MoveY = (w ? 1 : 0) - (s ? 1 : 0),
        Run = run,
    };

    /// <summary>The model: speed × (cos, sin) of the screen direction (x right, y up) in the world, rounded per axis.</summary>
    private static (int forward, int side) Model(double x, double y, double speed, double screenUpDegrees)
    {
        double d = (screenUpDegrees - 90 + Math.Atan2(y, x) * 180 / Math.PI) * Math.PI / 180;
        return ((int)Math.Round(speed * Math.Sin(d), MidpointRounding.AwayFromZero), (int)Math.Round(speed * Math.Cos(d), MidpointRounding.AwayFromZero));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryWasdCombinationMovesAlongTheScreenAtOneSpeed(bool run)
    {
        int speed = run ? 50 : 25;
        for (int mask = 0; mask < 16; mask++)
        {
            bool w = (mask & 1) != 0, a = (mask & 2) != 0, s = (mask & 4) != 0, d = (mask & 8) != 0;
            ticcmd_t cmd = Build(Keys(w, a, s, d, run));
            int x = (d ? 1 : 0) - (a ? 1 : 0), y = (w ? 1 : 0) - (s ? 1 : 0);
            (int forward, int side) = x == 0 && y == 0 ? (0, 0) : Model(x, y, speed, 135);
            Assert.True(forward == cmd.forwardmove && side == cmd.sidemove, $"W{w} A{a} S{s} D{d}: ({cmd.forwardmove}, {cmd.sidemove}), expected ({forward}, {side})");
            Assert.Equal(Ticcmds.AbsoluteAngle(Tables.ANG90), cmd.angleturn); // the player's angle: nothing aimed
            Assert.Equal(0, cmd.buttons);
        }
    }

    [Fact]
    public void IsoKeysGoWhereTheScreenPoints()
    {
        // Screen up is map north-west, screen right north-east (map north is up and to the right).
        Assert.Equal((18, -18), Move(Build(Keys(w: true))));
        Assert.Equal((18, 18), Move(Build(Keys(d: true))));
        Assert.Equal((25, 0), Move(Build(Keys(w: true, d: true)))); // up-right = north, not faster
        Assert.Equal((-25, 0), Move(Build(Keys(s: true, a: true))));
        Assert.Equal((0, 50), Move(Build(Keys(s: true, d: true, run: true)))); // down-right = east
        Assert.Equal((0, 0), Move(Build(Keys(w: true, s: true)))); // opposites cancel
        // A camera looking north: W thrusts north, D east.
        Assert.Equal((25, 0), Move(Build(Keys(w: true), screenUp: Tables.ANG90)));
        Assert.Equal((0, 25), Move(Build(Keys(d: true), screenUp: Tables.ANG90)));
        Assert.Equal((18, 18), Move(Build(Keys(w: true, d: true), screenUp: Tables.ANG90)));
    }

    private static (int, int) Move(ticcmd_t cmd) => (cmd.forwardmove, cmd.sidemove);

    [Fact]
    public void ThePauseKeySendsOnlyThePause()
    {
        // T7.1: g_game.c's sendpause: the buttons become BT_SPECIAL | BTS_PAUSE, the move stays
        ticcmd_t cmd = Build(Keys(w: true) with { Attack = true, Use = true, Weapon = 3, Pause = true });
        Assert.Equal(buttoncode_t.BT_SPECIAL | buttoncode_t.BTS_PAUSE, cmd.buttons);
        Assert.Equal(Move(Build(Keys(w: true))), Move(cmd));
        Assert.Equal(buttoncode_t.BT_ATTACK, Build(new TiccmdInput { Attack = true }).buttons);
    }

    [Fact]
    public void RunToggleFlipsRun()
    {
        var b = new TiccmdBuilder();
        Assert.Equal((25, 0), Move(Build(Keys(w: true, d: true), builder: b)));
        Assert.Equal((50, 0), Move(Build(Keys(w: true, d: true) with { RunToggle = true }, builder: b)));
        Assert.True(b.RunToggled);
        Assert.Equal((50, 0), Move(Build(Keys(w: true, d: true), builder: b))); // stays on
        Assert.Equal((25, 0), Move(Build(Keys(w: true, d: true, run: true), builder: b))); // Shift walks while toggled
        Build(new TiccmdInput { RunToggle = true }, builder: b);
        Assert.False(b.RunToggled);
    }

    [Fact]
    public void StickDirectionsAndDeflection()
    {
        for (int i = 0; i < 32; i++)
        {
            double t = i * Math.PI / 16;
            float x = (float)Math.Cos(t), y = (float)Math.Sin(t);
            foreach (bool run in new[] { false, true })
            {
                ticcmd_t cmd = Build(new TiccmdInput { MoveX = x, MoveY = y, Run = run });
                int speed = run ? 50 : 25;
                (int forward, int side) = Model(x, y, speed, 135);
                Assert.InRange(cmd.forwardmove, forward - 1, forward + 1);
                Assert.InRange(cmd.sidemove, side - 1, side + 1);
                double length = Math.Sqrt(cmd.forwardmove * cmd.forwardmove + cmd.sidemove * cmd.sidemove);
                Assert.InRange(length, speed - 0.75, speed + 0.75);
                double angle = Math.Atan2(cmd.forwardmove, cmd.sidemove) * 180 / Math.PI;
                double expected = 45 + i * 180.0 / 16;
                Assert.InRange(Math.Abs(Math.IEEERemainder(angle - expected, 360)), 0, 2.0);
            }
        }
        // Half deflection: half speed (12.5 rounds up); a square gate's corner is clamped to full speed.
        Assert.Equal((13, 0), Move(Build(new TiccmdInput { MoveY = 0.5f }, screenUp: Tables.ANG90)));
        Assert.Equal((0, 25), Move(Build(new TiccmdInput { MoveX = 0.75f, MoveY = 0.75f }, screenUp: Tables.ANG45))); // up-right of north-east = east
        Assert.Equal((0, 0), Move(Build(new TiccmdInput { MoveX = 0.01f })));
    }

    [Theory]
    [InlineData(100, 0, 0x0000)]
    [InlineData(100, 100, 0x2000)]
    [InlineData(0, 100, 0x4000)]
    [InlineData(-100, 100, 0x6000)]
    [InlineData(-100, 0, 0x8000)]
    [InlineData(-100, -100, 0xa000)]
    [InlineData(0, -100, 0xc000)]
    [InlineData(100, -100, 0xe000)]
    [InlineData(300, 100, 3355)] // atan(1/3) = 18.43°
    public void CursorAimsFromThePlayer(int dx, int dy, int expected)
    {
        var b = new TiccmdBuilder();
        ticcmd_t cmd = b.G_BuildTiccmd(new TiccmdInput { CursorMoved = true, Cursor = (64 + dx, -32 + dy) }, TwinStick, IsoUp, 64 * FRACUNIT, -32 * FRACUNIT, Tables.ANG90);
        Assert.InRange((ushort)cmd.angleturn, expected - 3, expected + 3); // R_PointToAngle2's precision
        Assert.Equal(AimSource.Cursor, b.Aim);
    }

    [Fact]
    public void WithoutAnAimThePlayerKeepsItsAngle()
    {
        var b = new TiccmdBuilder();
        uint facing = Tables.ANG45 * 5;
        // Nothing aimed yet: the cursor (not moved) does not count.
        Assert.Equal(Ticcmds.AbsoluteAngle(facing), Build(new TiccmdInput { Cursor = (100, 0) }, playerAngle: facing, builder: b).angleturn);
        // The mouse moves: aim at the cursor, and keep tracking it on later tics as the player moves.
        Assert.Equal(0, Build(new TiccmdInput { CursorMoved = true, Cursor = (100, 0) }, playerAngle: facing, builder: b).angleturn);
        Assert.Equal(0x4000, b.G_BuildTiccmd(new TiccmdInput { Cursor = (100, 0) }, TwinStick, IsoUp, 100 * FRACUNIT, -50 * FRACUNIT, 0).angleturn);
        // Off the level, or on the player: the player's angle.
        Assert.Equal(Ticcmds.AbsoluteAngle(facing), Build(new TiccmdInput { Cursor = null }, playerAngle: facing, builder: b).angleturn);
        Assert.Equal(Ticcmds.AbsoluteAngle(facing), Build(new TiccmdInput { Cursor = (0.5f, 0.5f) }, playerAngle: facing, builder: b).angleturn);
        // A new level: the player's angle until something aims again.
        b.Reset();
        Assert.Equal(AimSource.None, b.Aim);
        Assert.Equal(Ticcmds.AbsoluteAngle(facing), Build(new TiccmdInput { Cursor = (100, 0) }, playerAngle: facing, builder: b).angleturn);
        // Rounding to 16 bits.
        Assert.Equal(0x1235, Build(new TiccmdInput(), playerAngle: 0x12348000).angleturn);
    }

    [Fact]
    public void RightStickAims()
    {
        var b = new TiccmdBuilder();
        Assert.Equal(0x6000, (ushort)Build(new TiccmdInput { AimY = 1, CursorMoved = true, Cursor = (100, 0) }, builder: b).angleturn); // up = north-west, over the mouse
        Assert.Equal(AimSource.Stick, b.Aim);
        Assert.Equal(0x2000, (ushort)Build(new TiccmdInput { AimX = 0.8f }, builder: b).angleturn); // right = north-east
        Assert.Equal(0x4000, (ushort)Build(new TiccmdInput { AimX = 0.6f, AimY = 0.6f }, builder: b).angleturn);
        Assert.Equal(0xc000, (ushort)Build(new TiccmdInput { AimX = -0.7f, AimY = -0.7f }, builder: b).angleturn); // down-left = south
        // Released (or below the threshold): the player keeps the angle it has.
        Assert.Equal(Ticcmds.AbsoluteAngle(0xc0000000), Build(new TiccmdInput { AimX = 0.3f, Cursor = (100, 0) }, playerAngle: 0xc0000000, builder: b).angleturn);
        Assert.Equal(AimSource.Stick, b.Aim);
        // The mouse takes over again.
        Assert.Equal(0, Build(new TiccmdInput { CursorMoved = true, Cursor = (100, 0) }, builder: b).angleturn);
        Assert.Equal(AimSource.Cursor, b.Aim);
    }

    [Fact]
    public void Buttons()
    {
        Assert.Equal(buttoncode_t.BT_ATTACK, Build(new TiccmdInput { Attack = true }).buttons);
        Assert.Equal(buttoncode_t.BT_USE, Build(new TiccmdInput { Use = true }).buttons);
        Assert.Equal(buttoncode_t.BT_ATTACK | buttoncode_t.BT_USE, Build(new TiccmdInput { Attack = true, Use = true }).buttons);
        for (int slot = 1; slot <= 8; slot++)
        {
            byte b = Build(new TiccmdInput { Weapon = slot }).buttons;
            Assert.Equal(buttoncode_t.BT_CHANGE, b & buttoncode_t.BT_CHANGE);
            Assert.Equal(slot - 1, (b & buttoncode_t.BT_WEAPONMASK) >> buttoncode_t.BT_WEAPONSHIFT);
        }
        Assert.Equal(0, Build(new TiccmdInput { Weapon = 9 }).buttons);
        Assert.Equal(buttoncode_t.BT_ATTACK, Build(new TiccmdInput { Attack = true }, Tweaks.Vanilla).buttons);
    }

    /// <summary>T6.6: the next/previous weapon (Chocolate Doom's <c>G_NextWeapon</c>) as <c>BT_CHANGE</c> with a weapon number.</summary>
    [Fact]
    public void NextAndPreviousWeapon()
    {
        var p = new player_t { readyweapon = weapontype_t.wp_pistol, pendingweapon = weapontype_t.wp_nochange };
        p.weaponowned[(int)weapontype_t.wp_fist] = p.weaponowned[(int)weapontype_t.wp_pistol] = true;
        weapontype_t Step(int direction, GameMode mode = GameMode.shareware, int slot = 0)
        {
            byte b = new TiccmdBuilder().G_BuildTiccmd(new TiccmdInput { WeaponStep = direction, Weapon = slot }, TwinStick, IsoUp, 0, 0, 0, p, mode).buttons;
            Assert.Equal(buttoncode_t.BT_CHANGE, b & buttoncode_t.BT_CHANGE);
            return (weapontype_t)((b & buttoncode_t.BT_WEAPONMASK) >> buttoncode_t.BT_WEAPONSHIFT);
        }
        Assert.Equal(weapontype_t.wp_fist, Step(1)); // wraps round past the weapons not owned
        Assert.Equal(weapontype_t.wp_fist, Step(-1));
        Assert.Equal(weapontype_t.wp_fist, Step(1, slot: 3)); // the step wins over a slot key
        p.weaponowned[(int)weapontype_t.wp_shotgun] = p.weaponowned[(int)weapontype_t.wp_missile] = true;
        Assert.Equal(weapontype_t.wp_shotgun, Step(1));
        Assert.Equal(weapontype_t.wp_fist, Step(-1));
        p.pendingweapon = weapontype_t.wp_shotgun; // from the pending weapon
        Assert.Equal(weapontype_t.wp_missile, Step(1));
        p.pendingweapon = weapontype_t.wp_nochange;
        // the chainsaw stands for the fist (weapon number 0), which it hides without berserk
        p.weaponowned[(int)weapontype_t.wp_chainsaw] = true;
        p.readyweapon = weapontype_t.wp_missile;
        Assert.Equal(weapontype_t.wp_fist, Step(1)); // the chainsaw's number
        p.readyweapon = weapontype_t.wp_chainsaw;
        Assert.Equal(weapontype_t.wp_missile, Step(-1)); // not the fist
        p.powers[(int)powertype_t.pw_strength] = 1;
        Assert.Equal(weapontype_t.wp_fist, Step(-1));
        p.powers[(int)powertype_t.pw_strength] = 0;
        // plasma and BFG only outside shareware, the super shotgun only in Doom II
        p.weaponowned[(int)weapontype_t.wp_plasma] = p.weaponowned[(int)weapontype_t.wp_supershotgun] = true;
        p.readyweapon = weapontype_t.wp_missile;
        Assert.Equal(weapontype_t.wp_fist, Step(1));
        Assert.Equal(weapontype_t.wp_plasma, Step(1, GameMode.registered));
        p.readyweapon = weapontype_t.wp_shotgun;
        Assert.Equal(weapontype_t.wp_missile, Step(1));
        Assert.Equal(weapontype_t.wp_shotgun, Step(1, GameMode.commercial)); // the super shotgun's number
        // without a player the step is ignored
        Assert.Equal(0, new TiccmdBuilder().G_BuildTiccmd(new TiccmdInput { WeaponStep = 1 }, TwinStick, IsoUp, 0, 0, 0).buttons);
    }

    [Fact]
    public void VanillaCommand()
    {
        var v = Tweaks.Vanilla;
        Assert.Equal((25, 0), Move(Build(Keys(w: true), v)));
        Assert.Equal((-50, 0), Move(Build(Keys(s: true, run: true), v)));
        Assert.Equal((0, -24), Move(Build(Keys(a: true), v)));
        Assert.Equal((25, 24), Move(Build(Keys(w: true, d: true), v))); // straferunning: no normalisation
        Assert.Equal((50, 40), Move(Build(Keys(w: true, d: true, run: true), v)));
        Assert.Equal((50, 40), Move(Build(new TiccmdInput { MoveY = 2, MoveX = 2, Run = true }, v))); // axes past full deflection are clamped
        Assert.Equal(0, Build(Keys(w: true), v).angleturn); // no aim: no turn
        Assert.Equal(0, Build(new TiccmdInput { CursorMoved = true, Cursor = (100, 100) }, v).angleturn);

        // Turning: slow for SLOWTURNTICS tics, then walk or run speed; left positive.
        var b = new TiccmdBuilder();
        for (int tic = 0; tic < 8; tic++)
            Assert.Equal(tic < Ticcmds.SLOWTURNTICS - 1 ? 320 : 640, Build(new TiccmdInput { TurnLeft = true }, v, builder: b).angleturn);
        Assert.Equal(-1280, Build(new TiccmdInput { TurnRight = true, Run = true }, v, builder: b).angleturn);
        Assert.Equal(0, Build(new TiccmdInput(), v, builder: b).angleturn); // released: turnheld back to 0
        Assert.Equal(-320, Build(new TiccmdInput { AimX = 1 }, v, builder: b).angleturn); // the right stick turns
        Assert.Equal(0, Build(new TiccmdInput { TurnLeft = true, TurnRight = true }, v, builder: b).angleturn);
    }

    [Fact]
    public void AbsoluteAimingAloneMovesAgainstTheNewFacing()
    {
        var aimOnly = Tweaks.Vanilla with { AbsoluteAiming = true };
        // Aiming east, W with a north-up screen: north is the player's left.
        ticcmd_t cmd = Build(new TiccmdInput { MoveY = 1, CursorMoved = true, Cursor = (100, 0) }, aimOnly, Tables.ANG90);
        Assert.Equal((0, -25), Move(cmd));
        Assert.Equal(0, cmd.angleturn);
        // Aiming north-east, D (east): 45° right of the facing.
        cmd = Build(new TiccmdInput { MoveX = 1, CursorMoved = true, Cursor = (100, 100) }, aimOnly, Tables.ANG90);
        Assert.Equal((18, 18), Move(cmd));
        // Absolute movement alone: world directions, vanilla turning.
        var moveOnly = Tweaks.Vanilla with { AbsoluteMovement = true };
        cmd = Build(new TiccmdInput { MoveY = 1, TurnLeft = true }, moveOnly, Tables.ANG90);
        Assert.Equal((25, 0), Move(cmd));
        Assert.Equal(320, cmd.angleturn);
    }

    /// <summary>
    /// The commands drive the sim: in each mode a held key moves the player the
    /// way the screen points (or vanilla's way), and the aim turns it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CommandsDriveThePlayerTheWayTheScreenPoints(bool aimOnly)
    {
        Tweaks tweaks = aimOnly ? Tweaks.Vanilla with { AbsoluteAiming = true } : TwinStick;
        World w = TestMap.Strip(-1024, -1024, 1024, new TestMap.Room(2048, 0, 128)).Player(0, 0, 0).Load(tweaks: tweaks);
        mobj_t mo = w.players[0].mo!;
        var b = new TiccmdBuilder();
        for (int tic = 0; tic < 20; tic++)
        {
            // W on the iso screen (north-west) while aiming at a fixed point due south.
            var input = new TiccmdInput { MoveY = 1, CursorMoved = tic == 0, Cursor = (mo.x / (float)FRACUNIT, -900) };
            w.G_Ticker(b.G_BuildTiccmd(input, tweaks, IsoUp, mo.x, mo.y, mo.angle));
        }
        Assert.True(mo.x < -60 * FRACUNIT && mo.y > 60 * FRACUNIT, $"({mo.x / (double)FRACUNIT}, {mo.y / (double)FRACUNIT})");
        Assert.InRange(mo.x + mo.y, -2 * FRACUNIT, 2 * FRACUNIT); // along the diagonal
        Assert.InRange(mo.angle, Tables.ANG270 - (1u << 16), Tables.ANG270 + (1u << 16));
    }
}
