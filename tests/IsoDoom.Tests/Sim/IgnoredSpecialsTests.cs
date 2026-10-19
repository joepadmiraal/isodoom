using System.Collections.Generic;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// P7 (docs/PWAD-VALIDATION.md): content vanilla doesn't know is ignored as
/// vanilla ignores it. A line with a later port's special (Boom's, MBF's, or
/// none at all) does nothing crossed, used or shot, and thing options bits
/// above vanilla's five change nothing: the world plays tic for tic as the
/// same map without them. <see cref="VanillaIgnores"/> names what the level's
/// log line reports. No WAD needed (the PWAD sets' maps:
/// <c>PwadIgnoredContentTests</c>).
/// </summary>
public class IgnoredSpecialsTests
{
    private const int FRACUNIT = 1 << 16;

    // Specials vanilla has no case for: the two gaps in 1-141, Boom's, MBF's sky transfers (NEIS E3M6, E3M7),
    // NEIS E4M6's 307 (its ZDSPEC translation's), and a Boom generalized one.
    public static TheoryData<int> UnknownSpecials => [78, 85, 142, 242, 260, 271, 272, 307, 0x3000, 0x7FFF];

    /// <summary>Two rooms, the boundary between them carrying <paramref name="special"/> (tag 1, the east room tagged too), the player in the west room facing east, an imp with <paramref name="impFlags"/> in the east one.</summary>
    private static (World World, int Boundary) TwoRooms(int special, int impFlags = 7)
    {
        var map = TestMap.Strip(0, 0, 256, new TestMap.Room(256, 0, 128), new TestMap.Room(256, 0, 128));
        int boundary = map.Boundaries[1];
        map.Special(boundary, special, 1).SectorTag(1, 1).Player(200, 128).Thing(448, 64, 3001, 180, impFlags);
        return (map.Load(), boundary);
    }

    private static int Thinkers(World world)
    {
        int n = 0;
        for (thinker_t th = world.thinkercap.next; th != world.thinkercap; th = th.next)
            n++;
        return n;
    }

    /// <summary>
    /// The commands (null: the player placed 44 units east of the boundary
    /// facing west, in both worlds): shoot through the boundary from the west
    /// (<c>P_ShootSpecialLine</c>), use it from its back (<c>P_UseSpecialLine</c>,
    /// side 1), walk across it (<c>P_CrossSpecialLine</c>), use it from its
    /// front (side 0), shoot through it again and walk back across it.
    /// </summary>
    private static IEnumerable<ticcmd_t?> Commands()
    {
        for (int i = 0; i < 20; i++)
            yield return new ticcmd_t { buttons = buttoncode_t.BT_ATTACK };
        for (int i = 0; i < 10; i++)
            yield return new ticcmd_t();
        for (int i = 0; i < 6; i++)
            yield return new ticcmd_t { buttons = (i & 1) == 0 ? buttoncode_t.BT_USE : (byte)0 };
        for (int i = 0; i < 30; i++)
            yield return new ticcmd_t { forwardmove = 50 };
        yield return null;
        for (int i = 0; i < 6; i++)
            yield return new ticcmd_t { buttons = (i & 1) == 0 ? buttoncode_t.BT_USE : (byte)0 };
        for (int i = 0; i < 20; i++)
            yield return new ticcmd_t { buttons = buttoncode_t.BT_ATTACK };
        for (int i = 0; i < 30; i++)
            yield return new ticcmd_t { forwardmove = 50 };
    }

    /// <summary>Runs <paramref name="cmd"/> (or the placing) on <paramref name="world"/>.</summary>
    private static void Step(World world, ticcmd_t? cmd)
    {
        if (cmd is { } c)
            world.G_Ticker(c);
        else
            world.PlaceMobj(world.players[0].mo!, 300 * FRACUNIT, 128 * FRACUNIT, 0x80000000u);
    }

    [Theory]
    [MemberData(nameof(UnknownSpecials))]
    public void ALineWithASpecialVanillaDoesNotKnowDoesNothing(int special)
    {
        (World world, int boundary) = TwoRooms(special);
        (World plain, _) = TwoRooms(0);
        Assert.False(VanillaIgnores.IsVanillaLineSpecial(special));
        Assert.Equal(Thinkers(plain), Thinkers(world)); // P_SpawnSpecials spawned nothing for it
        Assert.Equal(plain.Checksum(), world.Checksum());

        int crossed = 0;
        int tic = 0;
        foreach (ticcmd_t? cmd in Commands())
        {
            mobj_t mo = world.players[0].mo!;
            int side = World.P_PointOnLineSide(mo.x, mo.y, world.lines[boundary]);
            Step(world, cmd);
            Step(plain, cmd);
            tic++;
            Assert.True(plain.Checksum() == world.Checksum(), $"special {special}: step {tic} differs from the map without it");
            Assert.Equal(Thinkers(plain), Thinkers(world));
            if (cmd is not null && World.P_PointOnLineSide(mo.x, mo.y, world.lines[boundary]) != side)
                crossed++;
        }
        Assert.Equal(2, crossed); // across and back
        Assert.True(world.players[0].mo!.x < 256 * FRACUNIT);
        Assert.Equal(special, (ushort)world.lines[boundary].special); // never cleared, as no special ran
        Assert.Equal(world.level.Lines[boundary].Special, world.lines[boundary].special);
        Assert.Equal(world.sides[world.lines[boundary].sidenum[0]].midtexture, plain.sides[plain.lines[boundary].sidenum[0]].midtexture);
        Assert.Equal(world.sectors[1].floorheight, plain.sectors[1].floorheight);
        Assert.Equal(world.sectors[1].ceilingheight, plain.sectors[1].ceilingheight);
        Assert.Equal(world.sectors[1].lightlevel, plain.sectors[1].lightlevel);
        Assert.Empty(world.Unported);
    }

    [Theory]
    [MemberData(nameof(UnknownSpecials))]
    public void ItsSpecialFunctionsDoNothing(int special)
    {
        (World world, int boundary) = TwoRooms(special);
        line_t line = world.lines[boundary];
        mobj_t player = world.players[0].mo!;
        mobj_t imp = System.Linq.Enumerable.First(world.Mobjs(), m => m.type == mobjtype_t.MT_TROOP);
        ulong before = world.Checksum();
        int thinkers = Thinkers(world);
        foreach (mobj_t thing in new[] { player, imp })
        {
            world.P_CrossSpecialLine(boundary, 0, thing);
            world.P_CrossSpecialLine(boundary, 1, thing);
            Assert.Equal(thing == player, world.P_UseSpecialLine(thing, line, 0)); // vanilla's return: true for a player's use of the front, whatever ran
            Assert.False(world.P_UseSpecialLine(thing, line, 1));
            world.P_ShootSpecialLine(thing, line);
        }
        Assert.Equal(before, world.Checksum());
        Assert.Equal(thinkers, Thinkers(world));
        Assert.Equal(special, (ushort)line.special);
    }

    [Fact]
    public void ThingFlagsAboveVanillasFiveChangeNothing()
    {
        // Boom's not in deathmatch (32), not in co-op (64), MBF's friendly (128) and the rest: vanilla reads 1-16 only.
        (World world, _) = TwoRooms(0, 7 | 32 | 64 | 128 | 0x100 | 0x4000);
        (World plain, _) = TwoRooms(0);
        Assert.Equal(plain.Checksum(), world.Checksum());
        Assert.Contains(world.Mobjs(), m => m.type == mobjtype_t.MT_TROOP);
        foreach (ticcmd_t? cmd in Commands())
        {
            Step(world, cmd);
            Step(plain, cmd);
            Assert.Equal(plain.Checksum(), world.Checksum());
        }
    }

    [Fact]
    public void TheLevelLogNamesWhatVanillaIgnores()
    {
        (World world, int boundary) = TwoRooms(272, 7 | 64 | 128);
        var ignores = VanillaIgnores.Of(world.level);
        Assert.False(ignores.IsEmpty);
        VanillaIgnores.Item item = Assert.Single(ignores.LineSpecials);
        Assert.Equal((272, 1), (item.Value, item.Count));
        Assert.Equal([boundary], item.First);
        Assert.Empty(ignores.SectorSpecials);
        Assert.Equal([64, 128], System.Linq.Enumerable.Select(ignores.ThingFlags, i => i.Value));
        Assert.Equal(
            $"line special 272 (MBF's sky transfer, flipped) on 1 line: {boundary}; thing flag 64 (Boom's not in co-op) on 1 thing: 1; thing flag 128 (MBF's friendly) on 1 thing: 1",
            ignores.Describe());

        // vanilla's own: nothing to say
        (World plain, _) = TwoRooms(48);
        Assert.True(VanillaIgnores.Of(plain.level).IsEmpty);
        Assert.Null(VanillaIgnores.Of(plain.level).Describe());

        // a sector special vanilla lacks (its I_Error when stood in), and the indices cut at four
        var map = TestMap.Strip(0, 0, 256, new TestMap.Room(64, 0, 128), new TestMap.Room(64, 0, 128), new TestMap.Room(64, 0, 128),
            new TestMap.Room(64, 0, 128), new TestMap.Room(64, 0, 128), new TestMap.Room(64, 0, 128));
        for (int i = 1; i < 6; i++)
            map.Special(map.Boundaries[i], 85);
        map.SectorSpecial(2, 6).Player(32, 128);
        string text = VanillaIgnores.Of(map.Load().level).Describe()!;
        Assert.Equal($"line special 85 (Boom's scroll right) on 5 lines: {map.Boundaries[1]}, {map.Boundaries[2]}, {map.Boundaries[3]}, {map.Boundaries[4]}, ...; "
            + "sector special 6 (vanilla's I_Error when a player stands in it) on 1 sector: 2", text);
    }

    [Fact]
    public void VanillasSpecialsAreTheOnesItsSwitchesName()
    {
        for (int s = 0; s <= 141; s++)
            Assert.Equal(s is not 78 and not 85, VanillaIgnores.IsVanillaLineSpecial(s));
        Assert.False(VanillaIgnores.IsVanillaLineSpecial(142));
        int[] sectors = [0, 1, 2, 3, 4, 5, 7, 8, 9, 10, 11, 12, 13, 14, 16, 17];
        for (int s = 0; s < 40; s++)
            Assert.Equal(System.Array.IndexOf(sectors, s) >= 0, VanillaIgnores.IsVanillaSectorSpecial(s));
    }
}
