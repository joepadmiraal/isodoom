using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T5.6: p_telept.c <c>EV_Teleport</c> and p_map.c <c>P_TeleportMove</c>/<c>PIT_StompThing</c>
/// on the synthetic specials map's teleporter (E1M2: room T, sector 28, x 320..384, floor 0;
/// the pad P, sector 29, x 384..448, floor 8; all y 152..240; line 86 between them, T in
/// front, a WR teleport (97) of tag 11 into room R, sector 25, whose destination stands at
/// (64, 196) facing east; see <c>SyntheticIwad.BuildSpecialsMap</c>).
/// </summary>
public class TeleportTests
{
    private const int FRACUNIT = 1 << 16;
    private const int Line = SyntheticIwad.TeleportLine;
    private const int RoomR = SyntheticIwad.DoorSector - 1, RoomS = SyntheticIwad.DoorSector + 1;

    private static int F(int units) => units * FRACUNIT;

    private static uint Deg(int degrees) => (uint)((ulong)degrees * 0x100000000UL / 360);

    private static World Specials()
    {
        var wad = new WadArchive([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);
        var world = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium), Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(wad, "E1M2"));
        return world;
    }

    private static mobj_t Player(World world) => world.players[0].mo!;

    private static mobj_t Destination(World world) => world.Mobjs().Single(m => m.type == mobjtype_t.MT_TELEPORTMAN);

    private static mobj_t[] Fogs(World world) => [.. world.Mobjs().Where(m => m.type == mobjtype_t.MT_TFOG)];

    /// <summary>Walks the player east from room T (x = 360) until it leaves the room or <paramref name="tics"/> pass; returns the tics run.</summary>
    private static int WalkEast(World world, int forward = 25, int tics = 40)
    {
        mobj_t me = Player(world);
        for (int t = 1; t <= tics; t++)
        {
            int x = me.x;
            world.G_Ticker(new ticcmd_t { forwardmove = (sbyte)forward });
            if (System.Math.Abs((long)me.x - x) > F(64))
                return t;
        }
        return tics;
    }

    [Fact]
    public void ThePlayerCrossingTheFrontTeleportsToTheDestinationWithFogAtBothEnds()
    {
        World world = Specials();
        mobj_t me = Player(world);
        mobj_t dest = Destination(world);
        Assert.Equal((F(SyntheticIwad.TeleportDestX), F(SyntheticIwad.TeleportDestY), RoomR), (dest.x, dest.y, dest.subsector.sector.Index));
        world.PlaceMobj(me, F(360), F(196), Deg(0));
        me.angle = Deg(0);
        int tic = 0, oldx = 0, prnd = 0;
        for (; tic < 40; tic++)
        {
            oldx = me.x;
            prnd = world.random.prndindex;
            world.G_Ticker(new ticcmd_t { forwardmove = 25 });
            if (me.x < F(320))
                break;
        }
        Assert.True(tic < 40, "no teleport");
        // At the destination, on its floor, facing its way, still, frozen for 18 tics, not interpolated.
        Assert.Equal((dest.x, dest.y, 0, dest.angle), (me.x, me.y, me.z, me.angle));
        Assert.Equal((0, 0, 0), (me.momx, me.momy, me.momz));
        Assert.Equal(18, me.reactiontime);
        Assert.False(me.interp);
        Assert.Equal(RoomR, me.subsector.sector.Index);
        Assert.Equal((0, F(128)), (me.floorz, me.ceilingz));
        // Two fogs (two P_Random calls for their spawns): where the player was, and 20 units in front of the destination.
        Assert.Equal(prnd + 2, world.random.prndindex);
        mobj_t[] fogs = Fogs(world);
        Assert.Equal(2, fogs.Length);
        // (the source fog stands where the move that crossed the line put the player, past the line)
        Assert.InRange(fogs[0].x, F(384), F(384 + 16));
        Assert.True(fogs[0].x > oldx);
        Assert.InRange(fogs[0].y, F(195), F(197));
        Assert.Equal(F(8), fogs[0].z); // the player had stepped up onto the pad's edge (its box over the pad)
        // (finecosine[0] is 65535, finesine[0] 25: vanilla's table, sampled half a step in)
        Assert.Equal((F(64) + 20 * Tables.finecosine[0], F(196) + 20 * Tables.finesine[0], 0), (fogs[1].x, fogs[1].y, fogs[1].z));
        Assert.All(fogs, f => Assert.False(f.interp));
        Assert.Equal([(sfxenum_t.sfx_telept, fogs[0]), (sfxenum_t.sfx_telept, fogs[1])],
            world.StartedSounds().Select(s => (s.sfx, s.origin!)));
        // WR: the line keeps its special.
        Assert.Equal(97, world.lines[Line].special);
    }

    [Fact]
    public void ThePlayerCannotMoveForEighteenTicsAfterATeleport()
    {
        World world = Specials();
        mobj_t me = Player(world);
        world.PlaceMobj(me, F(360), F(196), Deg(0));
        WalkEast(world);
        Assert.Equal((F(64), 18), (me.x, me.reactiontime));
        for (int t = 0; t < 18; t++)
        {
            world.G_Ticker(new ticcmd_t { forwardmove = 25, angleturn = 1024 });
            Assert.Equal((F(64), F(196), Deg(0)), (me.x, me.y, me.angle));
            Assert.True(me.interp);
        }
        Assert.Equal(0, me.reactiontime);
        world.G_Ticker(new ticcmd_t { forwardmove = 25 });
        Assert.True(me.x > F(64), "the player moves again on the 19th tic");
    }

    [Fact]
    public void TheFogFadesAndIsRemoved()
    {
        World world = Specials();
        world.PlaceMobj(Player(world), F(360), F(196), Deg(0));
        WalkEast(world);
        mobj_t fog = Fogs(world)[1];
        Assert.Equal(spritenum_t.SPR_TFOG, fog.sprite);
        int tics = 0;
        while (Fogs(world).Length > 0 && tics < 200)
        {
            world.G_Ticker(new ticcmd_t());
            tics++;
        }
        Assert.InRange(tics, 60, 80);
    }

    [Fact]
    public void CrossingFromTheBackDoesNotTeleport()
    {
        World world = Specials();
        mobj_t me = Player(world);
        world.PlaceMobj(me, F(416), F(196), Deg(180));
        for (int t = 0; t < 20; t++)
            world.G_Ticker(new ticcmd_t { forwardmove = 25 });
        Assert.Equal(SyntheticIwad.TeleportRoom, me.subsector.sector.Index);
        Assert.Empty(Fogs(world));
        Assert.Equal(0, me.reactiontime);
    }

    [Fact]
    public void ARepeatableLineTeleportsAgainAndAOnceLineOnlyOnce()
    {
        World world = Specials();
        mobj_t me = Player(world);
        world.PlaceMobj(me, F(360), F(196), Deg(0));
        WalkEast(world);
        Assert.Equal(F(64), me.x);
        world.PlaceMobj(me, F(360), F(196), Deg(0));
        me.reactiontime = 0;
        WalkEast(world);
        Assert.Equal(F(64), me.x);

        world.lines[Line].special = 39; // W1
        world.PlaceMobj(me, F(360), F(196), Deg(0));
        me.reactiontime = 0;
        WalkEast(world);
        Assert.Equal(F(64), me.x);
        Assert.Equal(0, world.lines[Line].special);
        world.PlaceMobj(me, F(360), F(196), Deg(0));
        me.reactiontime = 0;
        WalkEast(world, tics: 20);
        Assert.Equal(SyntheticIwad.TeleportPad, me.subsector.sector.Index);
    }

    [Fact]
    public void MissilesAreNotTeleportedAndMonstersOnlyLinesSkipPlayers()
    {
        World world = Specials();
        line_t line = world.lines[Line];
        mobj_t rocket = world.P_SpawnMobj(F(370), F(196), F(32), mobjtype_t.MT_ROCKET);
        Assert.Equal(0, world.EV_Teleport(line, 0, rocket));
        Assert.Equal(F(370), rocket.x);

        // 126 (WR, monsters only): the player walks over it; a monster crossing it teleports, unfrozen.
        line.special = 126;
        mobj_t me = Player(world);
        world.PlaceMobj(me, F(360), F(196), Deg(0));
        WalkEast(world, tics: 20);
        Assert.Equal(SyntheticIwad.TeleportPad, me.subsector.sector.Index);
        mobj_t trooper = world.P_SpawnMobj(F(352), F(176), World.ONFLOORZ, mobjtype_t.MT_POSSESSED);
        int reaction = trooper.reactiontime;
        world.P_CrossSpecialLine(Line, 0, trooper);
        Assert.Equal((F(64), F(196), Deg(0)), (trooper.x, trooper.y, trooper.angle));
        Assert.Equal(reaction, trooper.reactiontime);
        Assert.Equal(126, line.special);
        Assert.Equal(2, Fogs(world).Length);
    }

    [Fact]
    public void NoDestinationNoTeleport()
    {
        World world = Specials();
        line_t line = world.lines[Line];
        mobj_t me = Player(world);
        line.tag = 99; // no sector
        Assert.Equal(0, world.EV_Teleport(line, 0, me));
        line.tag = SyntheticIwad.DoorTag; // the door sector: no destination in it
        Assert.Equal(0, world.EV_Teleport(line, 0, me));
        Assert.Empty(Fogs(world));
        Assert.Equal(0, world.EV_Teleport(world.lines[Line], 1, me)); // the back side
    }

    [Fact]
    public void TheFirstTaggedSectorWithADestinationWinsThenTheFirstDestinationInIt()
    {
        World world = Specials();
        line_t line = world.lines[Line];
        mobj_t me = Player(world);
        // Room S (sector 27) gets the tag and a destination; R (25) comes first in sector order.
        world.sectors[RoomS].tag = SyntheticIwad.TeleportTag;
        mobj_t inS = world.P_SpawnMobj(F(250), F(196), World.ONFLOORZ, mobjtype_t.MT_TELEPORTMAN);
        inS.angle = Deg(180);
        mobj_t secondInR = world.P_SpawnMobj(F(100), F(220), World.ONFLOORZ, mobjtype_t.MT_TELEPORTMAN);
        Assert.Equal(1, world.EV_Teleport(line, 0, me));
        Assert.Equal((F(64), F(196)), (me.x, me.y));
        // Without R's tag, S's.
        world.sectors[RoomR].tag = 0;
        Assert.Equal(1, world.EV_Teleport(line, 0, me));
        Assert.Equal((F(250), F(196), Deg(180)), (me.x, me.y, me.angle));
        // A removed destination is skipped (its thinker is no longer P_MobjThinker).
        world.sectors[RoomR].tag = SyntheticIwad.TeleportTag;
        world.P_RemoveMobj(Destination(world, F(64)));
        Assert.Equal(1, world.EV_Teleport(line, 0, me));
        Assert.Equal((secondInR.x, secondInR.y), (me.x, me.y));
    }

    private static mobj_t Destination(World world, int x) =>
        world.Mobjs().Single(m => m.type == mobjtype_t.MT_TELEPORTMAN && m.x == x);

    [Fact]
    public void ThePlayerTelefragsAndAMonsterIsStoppedExceptOnMap30()
    {
        World world = Specials();
        line_t line = world.lines[Line];
        mobj_t me = Player(world);
        // A barrel (shootable) on the destination: the player arrives and telefrags it (10000 damage, T6.3).
        mobj_t barrel = world.P_SpawnMobj(F(70), F(196), World.ONFLOORZ, mobjtype_t.MT_BARREL);
        Assert.Equal(1, world.EV_Teleport(line, 0, me));
        Assert.Equal((F(64), F(196)), (me.x, me.y));
        Assert.True(barrel.health <= 0);
        Assert.True((barrel.flags & mobjflag_t.MF_CORPSE) != 0);
        Assert.Equal(statenum_t.S_BEXP, barrel.state);

        // A monster is stopped by a shootable thing there (a new barrel): no move, no fog.
        world.PlaceMobj(me, F(250), F(196));
        barrel = world.P_SpawnMobj(F(70), F(196), World.ONFLOORZ, mobjtype_t.MT_BARREL);
        mobj_t trooper = world.P_SpawnMobj(F(352), F(196), World.ONFLOORZ, mobjtype_t.MT_POSSESSED);
        int fogs = Fogs(world).Length;
        Assert.Equal(0, world.EV_Teleport(line, 0, trooper));
        Assert.Equal(F(352), trooper.x);
        Assert.Equal(fogs, Fogs(world).Length);
        // On map 30 it telefrags too.
        world.gamemap = 30;
        Assert.Equal(1, world.EV_Teleport(line, 0, trooper));
        Assert.Equal(F(64), trooper.x);
        Assert.True(barrel.health <= 0);
        // A thing that is not shootable never blocks.
        world.gamemap = 2;
        world.P_RemoveMobj(barrel);
        world.P_SpawnMobj(F(64), F(200), World.ONFLOORZ, mobjtype_t.MT_MISC2); // a health bonus
        mobj_t other = world.P_SpawnMobj(F(352), F(176), World.ONFLOORZ, mobjtype_t.MT_POSSESSED);
        world.P_RemoveMobj(trooper);
        Assert.Equal(1, world.EV_Teleport(line, 0, other));
    }

    [Fact]
    public void TeleportMoveIgnoresLinesAndSetsTheHeightsOfThePoint()
    {
        World world = Specials();
        mobj_t me = Player(world);
        // Into the closed door sector (floor and ceiling 0): P_TeleportMove does not check heights or lines.
        Assert.True(world.P_TeleportMove(me, F(144), F(196)));
        Assert.Equal((F(144), F(196), SyntheticIwad.DoorSector, 0, 0), (me.x, me.y, me.subsector.sector.Index, me.floorz, me.ceilingz));
        Assert.Equal(0, world.numspechit);
    }

    [Theory]
    [InlineData("E1M1", 1)]
    [InlineData("e4m9", 9)]
    [InlineData("MAP30", 30)]
    [InlineData("MAP07", 7)]
    [InlineData("TEST", 0)]
    public void TheMapNumberComesFromTheName(string name, int gamemap) => Assert.Equal(gamemap, World.MapNumber(name));

    [Fact]
    public void TheLevelSetsGamemap() => Assert.Equal(2, Specials().gamemap);
}
