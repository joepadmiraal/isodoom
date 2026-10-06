using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T4.7: the presentation's interpolation state on mobjs (not vanilla, SPEC
/// §12 T4.7): each tic starts by remembering every mobj's position and
/// facing (<see cref="World.P_StoreInterpolation"/>); a mobj spawned during a
/// tic, or moved by <see cref="World.PlaceMobj"/>, is not interpolated until
/// the next tic; none of it changes the sim.
/// </summary>
public class InterpolationTests
{
    private const int FRACUNIT = 1 << 16;

    private static World Room(Tweaks? tweaks = null) =>
        TestMap.Strip(-1024, -1024, 1024, new TestMap.Room(2048, 0, 128)).Player(0, 0, 90).Thing(256, 256, 2035).Load(tweaks: tweaks);

    [Fact]
    public void SpawnedMobjsAreNotInterpolatedBeforeTheirFirstTic()
    {
        World w = Room();
        Assert.All(w.Mobjs(), mo => Assert.False(mo.interp));
    }

    [Fact]
    public void EachTicRemembersWhereMobjsStarted()
    {
        World w = Room(Tweaks.TopDown);
        mobj_t mo = w.players[0].mo!;
        var cmd = new ticcmd_t { forwardmove = 50, angleturn = Ticcmds.AbsoluteAngle(Tables.ANG45) };
        for (int t = 0; t < 10; t++)
        {
            int x = mo.x, y = mo.y, z = mo.z;
            uint angle = mo.angle;
            w.G_Ticker(cmd);
            Assert.True(mo.interp);
            Assert.Equal((x, y, z, angle), (mo.oldx, mo.oldy, mo.oldz, mo.oldangle));
        }
        Assert.NotEqual(mo.oldy, mo.y); // moving north
        Assert.All(w.Mobjs(), m => Assert.True(m.interp));
    }

    [Fact]
    public void AMobjSpawnedDuringATicIsNotInterpolated()
    {
        World w = Room();
        w.G_Ticker(new ticcmd_t());
        mobj_t spawned = w.P_SpawnMobj(0, 512 * FRACUNIT, World.ONFLOORZ, mobjtype_t.MT_BARREL);
        Assert.False(spawned.interp);
        w.G_Ticker(new ticcmd_t());
        Assert.True(spawned.interp);
        Assert.Equal((spawned.x, spawned.y), (spawned.oldx, spawned.oldy));
    }

    [Fact]
    public void PlaceMobjMovesWithoutInterpolationOrMomentum()
    {
        World w = Room(Tweaks.TopDown);
        mobj_t mo = w.players[0].mo!;
        for (int t = 0; t < 5; t++)
            w.G_Ticker(new ticcmd_t { forwardmove = 50, angleturn = Ticcmds.AbsoluteAngle(Tables.ANG90) });
        w.PlaceMobj(mo, 500 * FRACUNIT, -300 * FRACUNIT, Tables.ANG180);
        Assert.False(mo.interp);
        Assert.Equal((500 * FRACUNIT, -300 * FRACUNIT, 0, Tables.ANG180), (mo.x, mo.y, mo.z, mo.angle));
        Assert.Equal((0, 0, 0), (mo.momx, mo.momy, mo.momz));
        Assert.Equal(mo.z + player_t.VIEWHEIGHT, w.players[0].viewz);
        Assert.Equal(statenum_t.S_PLAY, mo.state); // stopped running
        // Linked where it now is: found by a block search there.
        Assert.Same(w.R_PointInSubsector(mo.x, mo.y), mo.subsector);
        bool found = false;
        w.validcount++;
        int bx = (mo.x - w.bmaporgx) >> Blockmap.MAPBLOCKSHIFT, by = (mo.y - w.bmaporgy) >> Blockmap.MAPBLOCKSHIFT;
        w.P_BlockThingsIterator(bx, by, m => { found |= m == mo; return true; });
        Assert.True(found);
    }

    [Fact]
    public void InterpolationStateDoesNotChangeTheChecksum()
    {
        World a = Room(Tweaks.TopDown), b = Room(Tweaks.TopDown);
        var cmd = new ticcmd_t { forwardmove = 25, sidemove = 25, angleturn = Ticcmds.AbsoluteAngle(Tables.ANG45) };
        for (int t = 0; t < 35; t++)
        {
            a.G_Ticker(cmd);
            b.G_Ticker(cmd);
            foreach (mobj_t mo in b.Mobjs())
            {
                mo.oldx ^= 12345;
                mo.oldangle ^= 0x5555;
                mo.interp = !mo.interp;
            }
            Assert.Equal(a.Checksum(), b.Checksum());
        }
        Assert.Equal(a.Mobjs().Select(m => (m.x, m.y)), b.Mobjs().Select(m => (m.x, m.y)));
    }
}
