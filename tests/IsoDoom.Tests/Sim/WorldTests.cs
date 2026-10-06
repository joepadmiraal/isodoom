using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>T4.2: the <see cref="World"/>, its thinker list, mobjs, sector and block links, players and checksum.</summary>
public class WorldTests
{
    private const int FRACUNIT = 1 << 16;
    private static readonly SpawnSettings Medium = new(GameMode.shareware, skill_t.sk_medium);

    private static WadArchive Synthetic() => new(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) });

    private static World Load(WadArchive wad, string map, SpawnSettings settings)
    {
        var world = new World(settings, Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(wad, map));
        return world;
    }

    private static List<mobj_t> SectorList(sector_t sec)
    {
        var list = new List<mobj_t>();
        mobj_t? prev = null;
        for (mobj_t? m = sec.thinglist; m != null; m = m.snext)
        {
            Assert.Same(prev, m.sprev);
            list.Add(m);
            prev = m;
        }
        return list;
    }

    private static List<mobj_t> BlockList(World world, int block)
    {
        var list = new List<mobj_t>();
        mobj_t? prev = null;
        for (mobj_t? m = world.blocklinks[block]; m != null; m = m.bnext)
        {
            Assert.Same(prev, m.bprev);
            list.Add(m);
            prev = m;
        }
        return list;
    }

    private static int BlockOf(World world, mobj_t m)
    {
        int bx = (m.x - world.bmaporgx) >> Blockmap.MAPBLOCKSHIFT;
        int by = (m.y - world.bmaporgy) >> Blockmap.MAPBLOCKSHIFT;
        return bx >= 0 && bx < world.bmapwidth && by >= 0 && by < world.bmapheight ? by * world.bmapwidth + bx : -1;
    }

    /// <summary>
    /// Every mobj is in exactly the sector list of the subsector under it
    /// (unless <c>MF_NOSECTOR</c>) and the block list of the block under it
    /// (unless <c>MF_NOBLOCKMAP</c> or off the map), and the lists hold nothing else.
    /// </summary>
    internal static void CheckLinks(World world)
    {
        List<mobj_t> mobjs = world.Mobjs().ToList();
        var inSectors = world.sectors.SelectMany(SectorList).ToList();
        var inBlocks = Enumerable.Range(0, world.blocklinks.Length).SelectMany(b => BlockList(world, b)).ToList();
        Assert.Equal(inSectors.Count, inSectors.Distinct().Count());
        Assert.Equal(inBlocks.Count, inBlocks.Distinct().Count());

        foreach (mobj_t m in mobjs)
        {
            Assert.Same(world.R_PointInSubsector(m.x, m.y), m.subsector);
            Assert.Same(world.sectors[world.level.R_PointInSubsector(m.x, m.y).Sector.Index], m.subsector.sector);
            bool sectorLinked = (m.flags & mobjflag_t.MF_NOSECTOR) == 0;
            Assert.Equal(sectorLinked, SectorList(m.subsector.sector).Contains(m));
            int block = BlockOf(world, m);
            bool blockLinked = (m.flags & mobjflag_t.MF_NOBLOCKMAP) == 0 && block >= 0;
            if (block >= 0)
                Assert.Equal(blockLinked, BlockList(world, block).Contains(m));
        }
        Assert.Equal(mobjs.Count(m => (m.flags & mobjflag_t.MF_NOSECTOR) == 0), inSectors.Count);
        Assert.Equal(mobjs.Count(m => (m.flags & mobjflag_t.MF_NOBLOCKMAP) == 0 && BlockOf(world, m) >= 0), inBlocks.Count);
    }

    [Fact]
    public void SyntheticE1M1SpawnsTheT32List()
    {
        WadArchive wad = Synthetic();
        World world = Load(wad, "E1M1", Medium);
        List<mobj_t> mobjs = world.Mobjs().ToList();
        Assert.Equal(12, mobjs.Count); // T3.2/T3.5: 11 mobjs plus player 1, in THINGS order

        // The player.
        player_t p = world.players[0];
        mobj_t pm = mobjs[0];
        Assert.Same(pm, p.mo);
        Assert.Same(p, pm.player);
        Assert.Equal((mobjtype_t.MT_PLAYER, 0, 0, 0, Tables.ANG90, 100, statenum_t.S_PLAY, -1),
            (pm.type, pm.x, pm.y, pm.z, pm.angle, pm.health, pm.state, pm.tics));
        Assert.Equal((playerstate_t.PST_LIVE, 100, weapontype_t.wp_pistol, 50, player_t.VIEWHEIGHT, 0),
            (p.playerstate, p.health, p.readyweapon, p.ammo[(int)ammotype_t.am_clip], p.viewheight, p.viewz));
        // (P_SetupLevel's viewz = 1 is cleared by G_PlayerReborn's memset on the first level, as in vanilla)
        Assert.Equal(new MapThing(0, 0, 90, 1, 7), world.playerstarts[0]);
        Assert.Null(world.players[1].mo);

        // The rest as SpawnedThings (T3.5) places them.
        SpawnedThing[] expected = SpawnedThings.Build(world.level, MapThingSpawning.SpawnList(world.level.Things, Medium));
        Assert.Equal(expected.Length, mobjs.Count - 1);
        for (int i = 0; i < expected.Length; i++)
        {
            SpawnedThing e = expected[i];
            mobj_t m = mobjs[i + 1];
            Assert.Equal((e.Spawn.Type, e.x, e.y, e.z, e.angle, e.Sector.Index, e.state, e.sprite),
                (m.type, m.x, m.y, m.z, m.angle, m.subsector.sector.Index, m.state, m.sprite));
            Assert.Equal(e.Spawn.Thing, m.spawnpoint);
            Assert.Equal(m.subsector.sector.floorheight, m.floorz);
            Assert.Equal(m.subsector.sector.ceilingheight, m.ceilingz);
            int spawnTics = Info.states[(int)m.state].tics;
            Assert.True(spawnTics <= 0 ? m.tics == spawnTics : m.tics >= 1 && m.tics <= spawnTics, $"{m}: tics {m.tics}");
        }
        Assert.Equal(128 * FRACUNIT - 84 * FRACUNIT, mobjs[11].z); // ONCEILINGZ

        // Counts and P_Random: one per P_SpawnMobj (lastlook), one per random first tic count
        // (the barrel and the eight imps; the player, lamp and hanging body last forever).
        Assert.Equal((8, 0), (world.totalkills, world.totalitems));
        Assert.Equal(12 + 9, world.random.prndindex);

        // Sectors and blocks.
        CheckLinks(world);
        Assert.Equal(9, SectorList(world.sectors[0]).Count); // the west room: player, barrel, seven imps
        Assert.Equal(new[] { mobjs[2] }, SectorList(world.sectors[1]));
        Assert.Equal(new[] { mobjs[11] }, SectorList(world.sectors[2]));
        Assert.Equal(new[] { mobjs[10] }, SectorList(world.sectors[4]));
        // Block (1, 1) of the 10×3 map from (-136, -136): linked at the head, so newest first.
        Assert.Equal(new[] { mobjs[9], mobjs[8], mobjs[1], mobjs[0] }, BlockList(world, 1 * 10 + 1));
        Assert.Equal(new[] { mobjs[4], mobjs[3] }, BlockList(world, 1 * 10 + 0));
        Assert.Equal(new[] { mobjs[11] }, BlockList(world, 1 * 10 + 5));
    }

    [Theory]
    [InlineData(skill_t.sk_baby, 90)]
    [InlineData(skill_t.sk_easy, 90)]
    [InlineData(skill_t.sk_medium, 92)]
    [InlineData(skill_t.sk_hard, 116)]
    [InlineData(skill_t.sk_nightmare, 116)]
    public void Doom1E1M1Spawns(skill_t skill, int count)
    {
        WadArchive wad = WadArchive.Open(TestWads.RequireDoom1());
        var settings = new SpawnSettings(GameMode.shareware, skill);
        World world = Load(wad, "E1M1", settings);
        List<mobj_t> mobjs = world.Mobjs().ToList();
        Assert.Equal(count, mobjs.Count); // vanilla's thinker list (T3.2)
        Assert.Single(mobjs, m => m.type == mobjtype_t.MT_PLAYER);
        CheckLinks(world);

        SpawnedThing[] expected = SpawnedThings.Build(world.level, MapThingSpawning.SpawnList(world.level.Things, settings));
        Assert.Equal(expected.Select(e => (e.Spawn.Type, e.x, e.y, e.z, e.Sector.Index)),
            mobjs.Where(m => m.type != mobjtype_t.MT_PLAYER).Select(m => (m.type, m.x, m.y, m.z, m.subsector.sector.Index)));
        Assert.Equal(mobjs.Count(m => (m.flags & mobjflag_t.MF_COUNTKILL) != 0), world.totalkills);
        Assert.Equal(mobjs.Count(m => (m.flags & mobjflag_t.MF_COUNTITEM) != 0), world.totalitems);
        Assert.Equal(skill == skill_t.sk_nightmare ? 0 : 8, mobjs.First(m => m.type == mobjtype_t.MT_POSSESSED).reactiontime);
    }

    [Fact]
    public void Doom2EveryMapSpawnsAndRuns()
    {
        WadArchive wad = WadArchive.Open(TestWads.RequireDoom2());
        var settings = new SpawnSettings(GameMode.commercial, skill_t.sk_hard);
        for (int m = 1; m <= 32; m++)
        {
            World world = Load(wad, $"MAP{m:00}", settings);
            // Player 1 is the only one in the game; each of its starts spawns a player mobj (vanilla's voodoo dolls).
            Assert.Equal(MapThingSpawning.SpawnList(world.level.Things, settings).Count(s => s.Spawns && (s.Kind == MapThingSpawnKind.Mobj || s.PlayerNum == 0)),
                world.Mobjs().Count());
            for (int tic = 0; tic < 35; tic++)
                world.P_Ticker();
            CheckLinks(world);
        }
    }

    [Fact]
    public void Doom1E1M1RunsStably()
    {
        WadArchive wad = WadArchive.Open(TestWads.RequireDoom1());
        World a = Load(wad, "E1M1", Medium);
        World b = Load(wad, "E1M1", Medium);
        for (int tic = 0; tic < 35; tic++)
        {
            Assert.Equal(a.Checksum(), b.Checksum());
            a.P_Ticker();
            b.P_Ticker();
        }
        Assert.Equal(a.Checksum(), b.Checksum());
        CheckLinks(a);
    }

    [Fact]
    public void ThirtyFiveTicsKeepTheChecksumStableAcrossRuns()
    {
        WadArchive wad = Synthetic();
        ulong[] Run()
        {
            World world = Load(wad, "E1M1", Medium);
            var sums = new ulong[36];
            sums[0] = world.Checksum();
            for (int tic = 1; tic <= 35; tic++)
            {
                world.P_Ticker();
                sums[tic] = world.Checksum();
            }
            Assert.Equal(35, world.leveltime);
            return sums;
        }

        ulong[] first = Run();
        Assert.Equal(first, Run());
        Assert.Equal(36, first.Distinct().Count()); // leveltime alone changes it every tic

        // Two worlds side by side don't share state: running one leaves the other alone.
        World x = Load(wad, "E1M1", Medium);
        World y = Load(wad, "E1M1", Medium);
        for (int tic = 0; tic < 10; tic++)
            x.P_Ticker();
        Assert.Equal(first[0], y.Checksum());
        Assert.Equal(first[10], x.Checksum());
    }

    [Fact]
    public void ChecksumCoversTheState()
    {
        WadArchive wad = Synthetic();
        World world = Load(wad, "E1M1", Medium);
        ulong sum = world.Checksum();
        mobj_t imp = world.Mobjs().ElementAt(2);

        void Changes(System.Action change, System.Action undo)
        {
            change();
            Assert.NotEqual(sum, world.Checksum());
            undo();
            Assert.Equal(sum, world.Checksum());
        }

        Changes(() => imp.x++, () => imp.x--);
        Changes(() => imp.momy = 5, () => imp.momy = 0);
        Changes(() => imp.angle += 1, () => imp.angle -= 1);
        Changes(() => imp.health--, () => imp.health++);
        Changes(() => world.random.prndindex++, () => world.random.prndindex--);
        Changes(() => world.sectors[3].ceilingheight += FRACUNIT, () => world.sectors[3].ceilingheight -= FRACUNIT);
        Changes(() => world.sectors[0].lightlevel++, () => world.sectors[0].lightlevel--);
        player_t p = world.players[0];
        Changes(() => p.viewz++, () => p.viewz--);
        Changes(() => p.viewheight++, () => p.viewheight--);
        Changes(() => p.deltaviewheight++, () => p.deltaviewheight--);
        world.random.M_Random(); // not sim state
        Assert.Equal(sum, world.Checksum());
    }

    [Fact]
    public void SectorHeightsChangeTheLevelInPlace()
    {
        WadArchive wad = Synthetic();
        World world = Load(wad, "E1M1", Medium);
        world.sectors[3].ceilingheight = 72 * FRACUNIT; // the closed door opens
        Assert.Equal(72 * FRACUNIT, world.level.Sectors[3].CeilingHeight);
    }

    [Fact]
    public void ThinkerList()
    {
        var world = new World(Medium, Tweaks.Vanilla);
        var t = Enumerable.Range(0, 4).Select(_ => new thinker_t()).ToArray();
        foreach (thinker_t th in t)
            world.P_AddThinker(th);

        List<thinker_t> List()
        {
            var list = new List<thinker_t>();
            for (thinker_t th = world.thinkercap.next; th != world.thinkercap; th = th.next)
            {
                Assert.Same(th, th.next.prev);
                list.Add(th);
            }
            return list;
        }

        Assert.Equal(t, List());
        world.P_RemoveThinker(t[1]);
        world.P_RemoveThinker(t[3]);
        Assert.Equal(t, List()); // lazy: still linked until the list runs
        world.P_RunThinkers();
        Assert.Equal(new[] { t[0], t[2] }, List());
        Assert.Same(t[2], world.thinkercap.prev);

        world.P_InitThinkers();
        Assert.Empty(List());
    }

    [Fact]
    public void RemoveMobjUnlinks()
    {
        WadArchive wad = Synthetic();
        World world = Load(wad, "E1M1", Medium);
        mobj_t[] mobjs = world.Mobjs().ToArray();
        int block = 1 * 10 + 1; // player, barrel, imps 8 and 9
        world.P_RemoveMobj(mobjs[1]); // middle of its lists
        world.P_RemoveMobj(mobjs[9]); // head of its block list
        Assert.Equal(new[] { mobjs[8], mobjs[0] }, BlockList(world, block));
        Assert.DoesNotContain(mobjs[1], SectorList(world.sectors[0]));
        Assert.Equal(10, world.Mobjs().Count());
        world.P_RunThinkers();
        Assert.Equal(10, world.Mobjs().Count());
        CheckLinks(world);
    }

    [Fact]
    public void MovingAThingRelinksIt()
    {
        WadArchive wad = Synthetic();
        World world = Load(wad, "E1M1", Medium);
        mobj_t barrel = world.Mobjs().ElementAt(1);
        world.P_UnsetThingPosition(barrel);
        barrel.x = 320 * FRACUNIT; // into the east room, block (3, 1)
        world.P_SetThingPosition(barrel);
        Assert.Equal(1, barrel.subsector.sector.Index);
        Assert.Equal(barrel, BlockList(world, 1 * 10 + 3)[0]);
        CheckLinks(world);

        // Off the blockmap: in no block, but still in a sector.
        world.P_UnsetThingPosition(barrel);
        barrel.x = -200 * FRACUNIT;
        world.P_SetThingPosition(barrel);
        Assert.Null(barrel.bnext);
        Assert.Null(barrel.bprev);
        CheckLinks(world);
        world.P_RemoveMobj(barrel);
        CheckLinks(world);
    }

    [Fact]
    public void NoSectorNoBlockmapThings()
    {
        WadArchive wad = Synthetic();
        World world = Load(wad, "E1M1", Medium);
        mobj_t tele = world.P_SpawnMobj(64 * FRACUNIT, 0, World.ONFLOORZ, mobjtype_t.MT_TELEPORTMAN); // MF_NOSECTOR | MF_NOBLOCKMAP
        Assert.DoesNotContain(tele, SectorList(world.sectors[0]));
        Assert.Equal(0, tele.subsector.sector.Index);
        CheckLinks(world);
        world.P_RemoveMobj(tele);
        CheckLinks(world);
    }

    [Fact]
    public void SetMobjState()
    {
        WadArchive wad = Synthetic();
        World world = Load(wad, "E1M1", Medium);
        mobj_t imp = world.Mobjs().ElementAt(2);
        Assert.True(world.P_SetMobjState(imp, statenum_t.S_TROO_RUN1));
        Assert.Equal((statenum_t.S_TROO_RUN1, 3, spritenum_t.SPR_TROO, 0), (imp.state, imp.tics, imp.sprite, imp.frame));

        // A zero-tic state runs on into the next one: S_LIGHTDONE (0 tics) -> S_NULL removes the mobj.
        Assert.False(world.P_SetMobjState(imp, statenum_t.S_LIGHTDONE));
        Assert.Equal(statenum_t.S_NULL, imp.state);
        Assert.Equal(think_t.REMOVED, imp.function);
        Assert.DoesNotContain(imp, world.Mobjs());
        CheckLinks(world);
    }

    [Fact]
    public void StatesCycleOverTics()
    {
        WadArchive wad = Synthetic();
        World world = Load(wad, "E1M1", Medium);
        mobj_t barrel = world.Mobjs().ElementAt(1);
        int first = barrel.tics; // 1 + P_Random() % 6
        for (int i = 0; i < first; i++)
        {
            Assert.Equal(statenum_t.S_BAR1, barrel.state);
            world.P_Ticker();
        }
        Assert.Equal((statenum_t.S_BAR2, 6), (barrel.state, barrel.tics));
        for (int i = 0; i < 6; i++)
            world.P_Ticker();
        Assert.Equal(statenum_t.S_BAR1, barrel.state); // S_BAR2 -> S_BAR1
        Assert.Equal(-1, world.Mobjs().ElementAt(10).tics); // the lamp stays
    }

    [Fact]
    public void SecondPlayer()
    {
        WadArchive wad = Synthetic();
        var world = new World(Medium, Tweaks.Vanilla);
        world.playeringame[1] = true;
        world.G_DoLoadLevel(Level.Load(wad, "E1M1"));
        Assert.Null(world.players[1].mo); // the synthetic map has no player 2 start

        mobj_t? mo = world.P_SpawnMapThing(new MapThingSpawn(new MapThing(32, 32, 180, 2, 7), MapThingSpawnKind.PlayerStart, mobjtype_t.NUMMOBJTYPES, SpawnsPlayer: true));
        Assert.NotNull(mo);
        Assert.Same(mo, world.players[1].mo);
        Assert.Equal((mobjflag_t)(1 << Info.MF_TRANSSHIFT), mo!.flags & mobjflag_t.MF_TRANSLATION);
        Assert.Equal(Tables.ANG180, mo.angle);

        // Not in the game: nothing spawns, but the start is kept.
        Assert.Null(world.P_SpawnMapThing(new MapThingSpawn(new MapThing(32, 32, 0, 3, 7), MapThingSpawnKind.PlayerStart, mobjtype_t.NUMMOBJTYPES, SpawnsPlayer: true)));
        Assert.Equal(new MapThing(32, 32, 0, 3, 7), world.playerstarts[2]);
    }

    [Fact]
    public void PlayerRebornKeepsFragsAndCounts()
    {
        var world = new World(Medium, Tweaks.Vanilla);
        player_t p = world.players[0];
        p.frags[2] = 3;
        p.killcount = 5;
        p.armorpoints = 100;
        p.weaponowned[(int)weapontype_t.wp_shotgun] = true;
        world.G_PlayerReborn(0);
        player_t q = world.players[0];
        Assert.Same(p, q); // in place, as vanilla's memset
        Assert.Equal((3, 5, 0, false, true, true), (q.frags[2], q.killcount, q.armorpoints, q.weaponowned[(int)weapontype_t.wp_shotgun], q.attackdown, q.usedown));
        Assert.Equal(new[] { 200, 50, 300, 50 }, q.maxammo);
    }

    [Fact]
    public void PlayerClearZeroesEveryField()
    {
        var p = new player_t();
        foreach (FieldInfo f in typeof(player_t).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            object? value = f.FieldType switch
            {
                var t when t == typeof(int) => 7,
                var t when t == typeof(bool) => true,
                var t when t == typeof(string) => "x",
                var t when t.IsEnum => System.Enum.ToObject(t, 1),
                var t when t == typeof(mobj_t) => new mobj_t(),
                var t when t == typeof(ticcmd_t) => new ticcmd_t { forwardmove = 1, sidemove = 2, angleturn = 3, consistancy = 4, chatchar = 5, buttons = 6 },
                _ => null,
            };
            if (value != null)
                f.SetValue(p, value);
            else if (f.GetValue(p) is System.Array a)
                for (int i = 0; i < a.Length; i++)
                    a.SetValue(a.GetType().GetElementType() == typeof(bool) ? true : 7, i);
            else
                Assert.Fail($"player_t.{f.Name}: unknown field type {f.FieldType}");
        }
        p.Clear();
        foreach (FieldInfo f in typeof(player_t).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            object? v = f.GetValue(p);
            if (v is System.Array a)
                Assert.All(a.Cast<object>(), e => Assert.Equal(System.Activator.CreateInstance(e.GetType()), e));
            else
                Assert.True(v == null || v.Equals(System.Activator.CreateInstance(f.FieldType)), $"player_t.{f.Name} = {v}");
        }
    }

    [Fact]
    public void NewGameClearsRandomAndLevelKeepsIt()
    {
        WadArchive wad = Synthetic();
        World world = Load(wad, "E1M1", Medium);
        int index = world.random.prndindex;
        world.G_DoLoadLevel(Level.Load(wad, "E1M1")); // the next level: the index carries on
        Assert.Equal((index * 2) & 0xff, world.random.prndindex);
        Assert.Equal(12, world.Mobjs().Count());
        Assert.Equal(0, world.leveltime);
        CheckLinks(world);
    }

    [Fact]
    public void TweakFlags()
    {
        PropertyInfo[] flags = typeof(Tweaks).GetProperties().Where(p => p.PropertyType == typeof(bool)).ToArray();
        Assert.NotEmpty(flags);
        Assert.All(flags, f => Assert.False((bool)f.GetValue(Tweaks.Vanilla)!, f.Name));
        Assert.All(flags, f => Assert.True((bool)f.GetValue(Tweaks.TopDown)!, f.Name));
        Assert.Same(Tweaks.TopDown, new World(Medium, Tweaks.TopDown).tweaks);
    }
}
