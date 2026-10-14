using System;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T10.4: p_enemy.c's Icon of Sin (<c>A_BrainAwake</c>, <c>A_BrainSpit</c>,
/// <c>A_SpawnSound</c>, <c>A_SpawnFly</c>, <c>A_BrainPain</c>,
/// <c>A_BrainScream</c>, <c>A_BrainExplode</c>, <c>A_BrainDie</c>) and the
/// brain targets in the saves and the checksum, on a synthetic
/// <see cref="TestMap"/>, without a WAD. The Doom II route
/// <c>map30-icon</c> (<see cref="VanillaRouteTests"/>,
/// <see cref="SaveGameTests"/>) wins MAP30 against vanilla tic by tic.
/// </summary>
public class IconOfSinTests
{
    private const int FRACUNIT = 1 << 16;

    private static int F(int units) => units * FRACUNIT;

    private static TestMap Map() => TestMap.Strip(0, 0, 1024, new TestMap.Room(1024, 0, 256)).Player(64, 64);

    private static World Room(skill_t skill = skill_t.sk_medium)
    {
        var wad = new WadArchive([WadFile.FromBytes(Map().Build(), "testmap.wad")]);
        var world = new World(new SpawnSettings(GameMode.commercial, skill), Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(wad, "E1M1"));
        return world;
    }

    private static mobj_t Spawn(World world, int x, int y, mobjtype_t type) =>
        world.P_SpawnMobj(F(x), F(y), World.ONFLOORZ, type);

    private static mobj_t[] All(World world, mobjtype_t type) => [.. world.Mobjs().Where(m => m.type == type)];

    /// <summary>The shooter at the room's south and three targets at its north, as MAP30's (in thinker order).</summary>
    private static (mobj_t Spit, mobj_t[] Targets) Icon(World world)
    {
        mobj_t spit = Spawn(world, 512, 100, mobjtype_t.MT_BOSSSPIT);
        mobj_t[] targets = [Spawn(world, 256, 900, mobjtype_t.MT_BOSSTARGET), Spawn(world, 512, 900, mobjtype_t.MT_BOSSTARGET), Spawn(world, 768, 900, mobjtype_t.MT_BOSSTARGET)];
        return (spit, targets);
    }

    [Fact]
    public void TheShooterWakesFindingEveryTargetInThinkerOrder()
    {
        World world = Room();
        (mobj_t spit, mobj_t[] targets) = Icon(world);
        world.braintargeton = 2;
        world.A_BrainAwake(spit);
        Assert.Equal(targets, world.braintargets);
        Assert.Equal(3, world.numbraintargets);
        Assert.Equal(0, world.braintargeton);
        Assert.Contains(world.StartedSounds(), s => s.sfx == sfxenum_t.sfx_bossit && s.origin == null);

        // the state chain: seen, awake (A_BrainAwake), then a spit every 150 tics
        Assert.Equal(statenum_t.S_BRAINEYESEE, Info.mobjinfo[(int)mobjtype_t.MT_BOSSSPIT].seestate);
        Assert.Equal(actionf_t.A_BrainAwake, Info.states[(int)statenum_t.S_BRAINEYESEE].action);
        Assert.Equal(actionf_t.A_BrainSpit, Info.states[(int)statenum_t.S_BRAINEYE1].action);
        Assert.Equal(statenum_t.S_BRAINEYE1, Info.states[(int)statenum_t.S_BRAINEYE1].nextstate);
    }

    [Fact]
    public void TheShooterSpitsACubeAtEachTargetInTurnTimedToReachIt()
    {
        World world = Room();
        (mobj_t spit, mobj_t[] targets) = Icon(world);
        world.A_BrainAwake(spit);
        for (int i = 0; i < 4; i++)
        {
            world.events.Clear();
            world.A_BrainSpit(spit);
            mobj_t cube = world.Mobjs().Last(m => m.type == mobjtype_t.MT_SPAWNSHOT);
            Assert.Same(targets[i % 3], cube.target);
            Assert.Equal((targets[i % 3].y - spit.y) / cube.momy / Info.states[(int)cube.state].tics, cube.reactiontime);
            Assert.Equal((i + 1) % 3, world.braintargeton);
            Assert.Contains(world.StartedSounds(), s => s.sfx == sfxenum_t.sfx_bospit && s.origin == null);
        }
        Assert.Equal(4, All(world, mobjtype_t.MT_SPAWNSHOT).Length);
    }

    [Theory]
    [InlineData(skill_t.sk_baby, 2)]
    [InlineData(skill_t.sk_easy, 2)]
    [InlineData(skill_t.sk_medium, 4)]
    [InlineData(skill_t.sk_nightmare, 4)]
    public void OnTheEasySkillsOnlyEveryOtherSpitShoots(skill_t skill, int cubes)
    {
        World world = Room(skill);
        (mobj_t spit, _) = Icon(world);
        world.A_BrainAwake(spit);
        for (int i = 0; i < 4; i++)
            world.A_BrainSpit(spit);
        Assert.Equal(cubes, All(world, mobjtype_t.MT_SPAWNSHOT).Length);
        Assert.Equal(0, world.braineasy); // flipped four times
    }

    [Fact]
    public void WithoutTargetsTheShooterSpitsNothing()
    {
        World world = Room();
        mobj_t spit = Spawn(world, 512, 100, mobjtype_t.MT_BOSSSPIT);
        world.A_BrainAwake(spit);
        world.A_BrainSpit(spit); // vanilla: a crash (SPEC §12 T10.4)
        Assert.Empty(All(world, mobjtype_t.MT_SPAWNSHOT));
    }

    [Fact]
    public void ACubeFliesToItsTargetThenAMonsterComesOutThere()
    {
        World world = Room();
        (mobj_t spit, mobj_t[] targets) = Icon(world);
        world.A_BrainAwake(spit);
        world.A_BrainSpit(spit);
        mobj_t cube = world.Mobjs().Last(m => m.type == mobjtype_t.MT_SPAWNSHOT);
        int flight = cube.reactiontime;
        for (int i = 1; i < flight; i++)
        {
            world.A_SpawnSound(cube);
            Assert.Equal(flight - i, cube.reactiontime);
        }
        Assert.Contains(world.StartedSounds(), s => s.sfx == sfxenum_t.sfx_boscub && s.origin == cube);

        world.events.Clear();
        world.random.prndindex = PickIndex(0, 49); // an imp
        world.A_SpawnFly(cube);
        Assert.Equal(think_t.REMOVED, cube.function);
        mobj_t fire = Assert.Single(All(world, mobjtype_t.MT_SPAWNFIRE));
        Assert.Equal((targets[0].x, targets[0].y), (fire.x, fire.y));
        Assert.Contains(world.StartedSounds(), s => s.sfx == sfxenum_t.sfx_telept && s.origin == fire);
        mobj_t imp = Assert.Single(All(world, mobjtype_t.MT_TROOP));
        Assert.Same(world.players[0].mo, imp.target); // P_LookForPlayers all around: it saw the player,
        Assert.InRange(imp.x, targets[0].x - F(8), targets[0].x + F(8)); // and its see state's A_Chase took a step
        Assert.InRange(imp.y, targets[0].y - F(8), targets[0].y + F(8));
    }

    /// <summary>A <c>prndindex</c> after which <see cref="World.A_SpawnFly"/>'s pick (its second <c>P_Random</c>: the fire's <c>P_SpawnMobj</c> draws one first) is in [lo, hi].</summary>
    private static int PickIndex(int lo, int hi) =>
        Enumerable.Range(0, 256).First(i => DoomRandom.rndtable[(i + 2) & 0xff] is var r && r >= lo && r <= hi);

    [Theory]
    [InlineData(0, 49, mobjtype_t.MT_TROOP)]
    [InlineData(50, 89, mobjtype_t.MT_SERGEANT)]
    [InlineData(90, 119, mobjtype_t.MT_SHADOWS)]
    [InlineData(120, 129, mobjtype_t.MT_PAIN)]
    [InlineData(130, 159, mobjtype_t.MT_HEAD)]
    [InlineData(160, 161, mobjtype_t.MT_VILE)]
    [InlineData(162, 171, mobjtype_t.MT_UNDEAD)]
    [InlineData(172, 191, mobjtype_t.MT_BABY)]
    [InlineData(192, 221, mobjtype_t.MT_FATSO)]
    [InlineData(222, 245, mobjtype_t.MT_KNIGHT)]
    [InlineData(246, 255, mobjtype_t.MT_BRUISER)]
    public void TheMonsterIsPickedByOneRandomNumber(int lo, int hi, mobjtype_t type)
    {
        World world = Room();
        (mobj_t spit, _) = Icon(world);
        world.A_BrainAwake(spit);
        world.A_BrainSpit(spit);
        mobj_t cube = world.Mobjs().Last(m => m.type == mobjtype_t.MT_SPAWNSHOT);
        cube.reactiontime = 1;
        world.random.prndindex = PickIndex(lo, hi);
        world.A_SpawnFly(cube);
        Assert.Single(All(world, type));
    }

    [Fact]
    public void TheBrainsPainAndDeathAreHeardEverywhereAndItsDeathEndsTheLevel()
    {
        World world = Room();
        mobj_t brain = Spawn(world, 512, 900, mobjtype_t.MT_BOSSBRAIN);
        world.A_BrainPain(brain);
        Assert.Contains(world.StartedSounds(), s => s.sfx == sfxenum_t.sfx_bospn && s.origin == null);

        world.events.Clear();
        world.A_BrainScream(brain);
        mobj_t[] booms = All(world, mobjtype_t.MT_ROCKET);
        Assert.Equal(65, booms.Length); // every 8 units from 196 west to 320 east
        Assert.All(booms, b =>
        {
            Assert.Equal(brain.y - F(320), b.y);
            Assert.InRange(b.tics, 1, 10);
            Assert.True(b.state is statenum_t.S_BRAINEXPLODE1, $"{b.state}");
            Assert.InRange(b.z, 128, 128 + (255 * 2 * FRACUNIT)); // vanilla's 128, not 128 units
        });
        Assert.Equal(brain.x - F(196), booms.Min(b => b.x));
        Assert.Contains(world.StartedSounds(), s => s.sfx == sfxenum_t.sfx_bosdth && s.origin == null);

        // each explosion's last frame starts another
        world.A_BrainExplode(booms[0]);
        Assert.Equal(66, All(world, mobjtype_t.MT_ROCKET).Length);
        Assert.Equal(actionf_t.A_BrainExplode, Info.states[(int)statenum_t.S_BRAINEXPLODE3].action);

        Assert.Equal(gameaction_t.ga_nothing, world.gameaction);
        world.A_BrainDie(brain);
        Assert.Equal(gameaction_t.ga_completed, world.gameaction);
    }

    [Fact]
    public void TheBrainTargetsAreSavedAndInTheChecksum()
    {
        var wad = new WadArchive([WadFile.FromBytes(Map().Build(), "testmap.wad")]);
        var world = new World(new SpawnSettings(GameMode.commercial, skill_t.sk_easy), Tweaks.Vanilla)
        {
            textures = wad.W_CheckNumForName("TEXTURE1") >= 0 ? Textures.R_InitTextures(wad) : null,
        };
        world.P_InitPicAnims(world.textures, PicAnims.FlatNames(wad));
        world.G_DoLoadLevel(Level.Load(wad, "E1M1"));
        (mobj_t spit, mobj_t[] targets) = Icon(world);
        ulong asleep = world.Checksum();
        world.A_BrainAwake(spit);
        ulong awake = world.Checksum();
        Assert.NotEqual(asleep, awake);
        world.A_BrainSpit(spit); // easy flips to 1: a cube at the first target
        Assert.Equal(1, world.braintargeton);
        Assert.NotEqual(awake, world.Checksum());
        world.A_BrainSpit(spit); // easy flips to 0: nothing on skill 2
        Assert.Equal(1, world.braintargeton);
        Assert.Equal(0, world.braineasy);
        ulong flipped = world.Checksum();
        world.A_BrainSpit(spit); // a cube at the second target
        Assert.Equal(2, world.braintargeton);
        Assert.Equal(1, world.braineasy);
        Assert.NotEqual(flipped, world.Checksum());

        byte[] save = SaveGameTests.Save(world);
        World loaded = SaveGameTests.Load(save, wad);
        Assert.Equal(world.Checksum(), loaded.Checksum());
        Assert.Equal(3, loaded.numbraintargets);
        Assert.Equal(2, loaded.braintargeton);
        Assert.Equal(1, loaded.braineasy);
        Assert.Equal(targets.Select(t => (t.x, t.y)), loaded.braintargets.Select(t => (t.x, t.y)));
        Assert.All(loaded.braintargets, t => Assert.Contains(t, loaded.Mobjs()));

        // both go on alike: the next spit flips only, the one after shoots at the third target
        mobj_t loadedSpit = loaded.Mobjs().Single(m => m.type == mobjtype_t.MT_BOSSSPIT);
        for (int i = 0; i < 2; i++)
        {
            world.A_BrainSpit(spit);
            loaded.A_BrainSpit(loadedSpit);
            Assert.Equal(world.Checksum(), loaded.Checksum());
        }
        Assert.Equal(3, loaded.Mobjs().Count(m => m.type == mobjtype_t.MT_SPAWNSHOT));
        Assert.Equal(loaded.braintargets[2], loaded.Mobjs().Last(m => m.type == mobjtype_t.MT_SPAWNSHOT).target);
        Assert.True(save.AsSpan().SequenceEqual(SaveGameTests.Save(SaveGameTests.Load(save, wad))));
    }

    /// <summary>
    /// T10.4's shot cue (<see cref="IsoDoom.Game.IconAim"/>): on MAP30's route
    /// each rocket the player fires from the rising pillar reaches the brain,
    /// and the cue says so from where and how high it left, and, the
    /// launcher's delay counted, when fire was pressed; 40 units lower or
    /// higher, or turned aside, it says no (skips without Doom II).
    /// </summary>
    [Fact]
    public void TheShotCueAgreesWithTheRoutesRockets()
    {
        var route = VanillaRoute.Load("map30-icon");
        World world = route.NewWorld(out _);
        mobj_t brain = world.Mobjs().Single(m => m.type == mobjtype_t.MT_BOSSBRAIN);
        mobj_t me = world.players[0].mo!;
        var seen = new System.Collections.Generic.HashSet<mobj_t>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
        int rockets = 0, hits = 0, presses = 0, health = brain.health;
        for (int tic = 0; tic < route.Cmds.Count; tic++)
        {
            int lead = IsoDoom.Game.IconAim.LaunchLead(world.players[0]);
            if ((route.Cmds[tic].buttons & buttoncode_t.BT_ATTACK) != 0 && lead > 0)
            {
                // the cue as the player saw it when pressing fire, facing as the tic turns it: the launcher's 8 tics and the pillar's rise counted
                presses++;
                Assert.Equal(8, lead);
                mobj_t aimed = new() { x = me.x, y = me.y, z = me.z, oldz = me.oldz, interp = me.interp, angle = unchecked(me.angle + (uint)(route.Cmds[tic].angleturn << 16)) };
                Assert.True(IsoDoom.Game.IconAim.ClearShot(world, aimed, brain, lead), $"tic {tic}: fire pressed at z {me.z >> 16}");
                Assert.False(IsoDoom.Game.IconAim.ClearShot(world, aimed, brain), $"tic {tic}: without the lead");
            }
            route.File.RunEvents(world, tic);
            world.G_Ticker(route.Cmds[tic]);
            if (brain.health < health)
            {
                hits++;
                health = brain.health;
            }
            foreach (mobj_t r in world.Mobjs().Where(m => m.type == mobjtype_t.MT_ROCKET && m.target == me && seen.Add(m)).ToList())
            {
                rockets++;
                // where it left: P_SpawnPlayerMissile's 32 units up, before P_CheckMissileSpawn's half step
                mobj_t from = new() { x = r.x - (r.momx >> 1), y = r.y - (r.momy >> 1), z = r.z - (r.momz >> 1) - F(32), angle = r.angle };
                Assert.True(IsoDoom.Game.IconAim.ClearShot(world, from, brain), $"tic {tic}: the rocket from z {from.z >> 16}");
                foreach (int dz in new[] { -40, 40 })
                    Assert.False(IsoDoom.Game.IconAim.ClearShot(world, new mobj_t { x = from.x, y = from.y, z = from.z + F(dz), angle = from.angle }, brain), $"tic {tic}: {dz} units up");
                Assert.False(IsoDoom.Game.IconAim.ClearShot(world, new mobj_t { x = from.x, y = from.y, z = from.z, angle = from.angle + Tables.ANG45 }, brain), $"tic {tic}: turned");
            }
        }
        Assert.Equal(3, presses);
        Assert.Equal(3, rockets);
        Assert.Equal(3, hits);
        Assert.Equal(gameaction_t.ga_completed, world.gameaction); // A_BrainDie
    }
}
