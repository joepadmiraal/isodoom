using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>T3.5: where and in which frame map things spawn (<see cref="SpawnedThings"/>, p_mobj.c <c>P_SpawnMapThing</c>/<c>P_SpawnMobj</c>).</summary>
public class SpawnedThingsTests
{
    private const int FRACUNIT = 1 << 16;

    private static SpawnedThing[] Spawn(WadArchive wad, string map, GameMode mode, skill_t skill)
    {
        var level = Level.Load(wad, map);
        return SpawnedThings.Build(level, MapThingSpawning.SpawnList(level.Things, new SpawnSettings(mode, skill)));
    }

    [Fact]
    public void SyntheticE1M1()
    {
        var wad = new WadArchive([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);
        SpawnedThing[] things = Spawn(wad, "E1M1", GameMode.shareware, skill_t.sk_medium);
        Assert.Equal(11, things.Length); // every mobj; not the player start

        SpawnedThing barrel = things[0];
        Assert.Equal((64 * FRACUNIT, 64 * FRACUNIT, 0, statenum_t.S_BAR1, spritenum_t.SPR_BAR1, 0, false, 0),
            (barrel.x, barrel.y, barrel.z, barrel.state, barrel.sprite, barrel.frame, barrel.fullbright, barrel.Sector.Index));

        SpawnedThing imp = things[1]; // in the east room, raised 16
        Assert.Equal((16 * FRACUNIT, 0x80000000u, statenum_t.S_TROO_STND, spritenum_t.SPR_TROO, 0, 1),
            (imp.z, imp.angle, imp.state, imp.sprite, imp.frame, imp.Sector.Index));
        Assert.All(things.Skip(1).Take(8), t => Assert.Equal(spritenum_t.SPR_TROO, t.sprite));

        SpawnedThing lamp = things[9]; // FF_FULLBRIGHT, frame A
        Assert.Equal((spritenum_t.SPR_COLU, 0, true, -16 * FRACUNIT, 4), (lamp.sprite, lamp.frame, lamp.fullbright, lamp.z, lamp.Sector.Index)); // courtyard A, floor -16

        SpawnedThing hanging = things[10]; // MF_SPAWNCEILING: ONCEILINGZ in room C (ceiling 128), height 84
        Assert.Equal((spritenum_t.SPR_GOR2, 2, (128 - 84) * FRACUNIT), (hanging.sprite, hanging.Sector.Index, hanging.z));
    }

    [Fact]
    public void Doom1E1M1SpawnFramesExist()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom1());
        var sprites = Sprites.R_InitSprites(wad);
        SpawnedThing[] things = Spawn(wad, "E1M1", GameMode.shareware, skill_t.sk_medium);
        Assert.Equal(91, things.Length); // T3.2: 91 mobjs on skill 3, plus player 1
        Assert.All(things, t => Assert.True(t.frame < sprites.SpriteDefs[(int)t.sprite].NumFrames, $"{t.sprite} frame {t.frame}"));
        Assert.Contains(things, t => t.fullbright); // the lamps
        Assert.All(things, t => Assert.Equal(t.Sector.FloorHeight, t.z)); // E1M1 hangs nothing from its ceilings
    }

    [Fact]
    public void Doom2SpawnFramesExistOnEveryMapAndSkill()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom2());
        var sprites = Sprites.R_InitSprites(wad);
        int ceiling = 0;
        for (int m = 1; m <= 32; m++)
        {
            foreach (skill_t skill in new[] { skill_t.sk_easy, skill_t.sk_medium, skill_t.sk_hard })
            {
                foreach (SpawnedThing t in Spawn(wad, $"MAP{m:00}", GameMode.commercial, skill))
                {
                    Assert.True(t.frame < sprites.SpriteDefs[(int)t.sprite].NumFrames, $"MAP{m:00}: {t.sprite} frame {t.frame}");
                    mobjinfo_t info = Info.mobjinfo[(int)t.Spawn.Type];
                    bool onCeiling = (info.flags & mobjflag_t.MF_SPAWNCEILING) != 0;
                    Assert.Equal(onCeiling ? t.Sector.CeilingHeight - info.height : t.Sector.FloorHeight, t.z);
                    if (onCeiling)
                        ceiling++;
                }
            }
        }
        Assert.True(ceiling > 0, "no MF_SPAWNCEILING thing in Doom II");
    }
}
