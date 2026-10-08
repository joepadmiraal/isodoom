using System;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T5.7: p_lights.c (the light thinkers <c>P_SpawnSpecials</c> starts, and
/// the line specials' <c>EV_LightTurnOn</c>, <c>EV_TurnTagLightsOff</c>,
/// <c>EV_StartLightStrobing</c>) and p_spec.c's animated textures and flats
/// (<c>P_InitPicAnims</c>, the translations of <c>P_UpdateSpecials</c>) on
/// the synthetic specials map (E1M2: alcove i is sector 1 + i, with the light
/// specials of <see cref="SyntheticIwad.AlcoveSpecial"/>, lights
/// <see cref="SyntheticIwad.AlcoveLight"/>; the corridor, sector 0, 160;
/// alcoves 3, 7 and 12 have tag 5). The thinkers' tic-by-tic behaviour and
/// their <c>P_Random</c> calls are checked against vanilla by the routes'
/// <c>lights</c> and <c>prndindex</c> columns (VanillaRouteTests).
/// </summary>
public class LightTests
{
    private static WadArchive Wad() => new([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);

    private static World Specials(Action<Level>? change = null)
    {
        WadArchive wad = Wad();
        var world = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium), Tweaks.Vanilla) { textures = Textures.R_InitTextures(wad) };
        world.P_InitPicAnims(world.textures, PicAnims.FlatNames(wad));
        var level = Level.Load(wad, "E1M2");
        change?.Invoke(level);
        world.G_DoLoadLevel(level);
        return world;
    }

    private static int Alcove(int i) => 1 + i;

    private static List<T> Thinkers<T>(World world) where T : thinker_t
    {
        var list = new List<T>();
        for (thinker_t th = world.thinkercap.next; th != world.thinkercap; th = th.next)
        {
            if (th is T t)
                list.Add(t);
        }
        return list;
    }

    /// <summary>A line with tag <paramref name="tag"/> (line 0, retagged) to trigger the EV_ functions with.</summary>
    private static line_t Tagged(World world, short tag)
    {
        world.lines[0].tag = tag;
        return world.lines[0];
    }

    [Fact]
    public void SpawnSpecialsStartsTheLightThinkers()
    {
        World world = Specials();
        sector_t S(int alcove) => world.sectors[Alcove(alcove)];

        lightflash_t flash = Assert.Single(Thinkers<lightflash_t>(world));
        Assert.Same(S(1), flash.sector);
        Assert.Equal((104, 96, 64, 7), (flash.maxlight, flash.minlight, flash.maxtime, flash.mintime)); // the darkest neighbour: alcove 0
        Assert.InRange(flash.count, 1, 65);

        List<strobe_t> strobes = Thinkers<strobe_t>(world);
        // Alcove 14's strobe hurt (4, T5.8) strobes fast too.
        Assert.Equal([S(2), S(4), S(6), S(8), S(14)], strobes.Select(s => s.sector));
        Assert.Equal([LightFlash.FASTDARK, LightFlash.SLOWDARK, LightFlash.SLOWDARK, LightFlash.FASTDARK, LightFlash.FASTDARK], strobes.Select(s => s.darktime));
        Assert.All(strobes, s => Assert.Equal(LightFlash.STROBEBRIGHT, s.brighttime));
        Assert.InRange(strobes[0].count, 1, 8); // not in sync: random
        Assert.Equal(1, strobes[2].count);      // in sync (12, 13)
        Assert.Equal(1, strobes[3].count);
        // Alcove 8 (light 96) has no darker neighbour: its strobe goes to 0.
        Assert.Equal((96, 0), (strobes[3].maxlight, strobes[3].minlight));

        glow_t glow = Assert.Single(Thinkers<glow_t>(world));
        Assert.Same(S(5), glow.sector);
        Assert.Equal((128, 136, -1), (glow.minlight, glow.maxlight, glow.direction));

        fireflicker_t fire = Assert.Single(Thinkers<fireflicker_t>(world));
        Assert.Same(S(9), fire.sector);
        Assert.Equal((104, 96 + 16, 4), (fire.maxlight, fire.minlight, fire.count)); // 16 over the darkest neighbour, alcove 8

        // Every light special is cleared but 4, death slime (alcove 14, T5.8).
        foreach (int i in new[] { 1, 2, 4, 5, 6, 8, 9 })
            Assert.Equal(0, S(i).special);
        Assert.Equal(4, S(SyntheticIwad.StrobeHurtAlcove).special);
        // The lights stay as the map has them until the first tic.
        for (int i = 0; i < SyntheticIwad.SpecialsAlcoves; i++)
            Assert.Equal(SyntheticIwad.AlcoveLight(i), S(i).lightlevel);
    }

    [Fact]
    public void TheSyncStrobeStepsBetweenItsLevels()
    {
        World world = Specials();
        sector_t s = world.sectors[Alcove(6)]; // sync strobe slow: 144, its darkest neighbour 136
        var levels = new List<int>();
        for (int tic = 0; tic < 1 + LightFlash.SLOWDARK + LightFlash.STROBEBRIGHT + 1; tic++)
        {
            world.G_Ticker(new ticcmd_t());
            levels.Add(s.lightlevel);
        }
        // Dark on the first tic for SLOWDARK tics, bright for STROBEBRIGHT, dark again.
        Assert.All(levels.Take(LightFlash.SLOWDARK), l => Assert.Equal(136, l));
        Assert.All(levels.Skip(LightFlash.SLOWDARK).Take(LightFlash.STROBEBRIGHT), l => Assert.Equal(144, l));
        Assert.Equal(136, levels[^1]);
    }

    [Fact]
    public void TheFireFlickersOnceEveryFourTics()
    {
        World world = Specials();
        sector_t s = world.sectors[Alcove(9)];
        fireflicker_t fire = Assert.Single(Thinkers<fireflicker_t>(world));
        for (int tic = 1; tic <= 12; tic++)
        {
            world.G_Ticker(new ticcmd_t());
            // Its own light (104) less 0-48 is always under its minimum (112): on the 4th tic, the minimum;
            // then the minimum again, or the maximum (104) when the roll takes nothing off.
            if (tic <= 4)
                Assert.Equal(tic < 4 ? 104 : 112, s.lightlevel);
            else
                Assert.Contains((int)s.lightlevel, new[] { 104, 112 });
            Assert.Equal(tic % 4 == 0 ? 4 : 4 - tic % 4, fire.count);
        }
    }

    [Fact]
    public void LightTurnOnSearchesTheBrightestNeighbourOnceAndKeepsIt()
    {
        // The corridor at 100: alcove 3 (120) finds alcove 4's 128; vanilla keeps that brightness for the
        // next tagged sectors instead of searching again (alcove 7's brightest neighbour is 144).
        World world = Specials(level => level.Sectors[0].LightLevel = 100);
        world.EV_LightTurnOn(Tagged(world, 5), 0);
        Assert.All([3, 7, 12], i => Assert.Equal(128, world.sectors[Alcove(i)].lightlevel));
        Assert.Equal(SyntheticIwad.AlcoveLight(6), world.sectors[Alcove(6)].lightlevel);

        world.EV_LightTurnOn(Tagged(world, 5), 255);
        Assert.All([3, 7, 12], i => Assert.Equal(255, world.sectors[Alcove(i)].lightlevel));
    }

    [Fact]
    public void TurnTagLightsOffTakesTheDarkestNeighbour()
    {
        World world = Specials();
        world.EV_TurnTagLightsOff(Tagged(world, 5));
        Assert.Equal(112, world.sectors[Alcove(3)].lightlevel);  // alcove 2
        Assert.Equal(96, world.sectors[Alcove(7)].lightlevel);   // alcove 8
        Assert.Equal(120, world.sectors[Alcove(12)].lightlevel); // alcove 11
    }

    [Fact]
    public void StartLightStrobingSkipsSectorsWithAMover()
    {
        World world = Specials();
        var mover = new vldoor_t();
        world.sectors[Alcove(7)].specialdata = mover;
        int before = Thinkers<strobe_t>(world).Count;
        world.EV_StartLightStrobing(Tagged(world, 5));
        var added = Thinkers<strobe_t>(world).Skip(before).ToList();
        Assert.Equal([world.sectors[Alcove(3)], world.sectors[Alcove(12)]], added.Select(s => s.sector));
        Assert.All(added, s => Assert.Equal(LightFlash.SLOWDARK, s.darktime));
        // A strobe sets no specialdata (vanilla): triggering again adds more.
        world.EV_StartLightStrobing(Tagged(world, 5));
        Assert.Equal(before + 4, Thinkers<strobe_t>(world).Count);
    }

    [Fact]
    public void PicAnimsFindTheSyntheticSequences()
    {
        WadArchive wad = Wad();
        var textures = Textures.R_InitTextures(wad);
        (List<string[]> tex, List<string[]> flats) = PicAnims.Sequences(textures, PicAnims.FlatNames(wad));
        Assert.Equal(new[] { new[] { "SLADRIP1", "SLADRIP2", "SLADRIP3" } }, tex);
        Assert.Equal(new[] { new[] { "NUKAGE1", "NUKAGE2", "NUKAGE3" }, ["LAVA1", "LAVA2", "LAVA3", "LAVA4"] }, flats);
        // No textures (a map without TEXTURE1): the flats only.
        Assert.All(PicAnims.Resolve(null, PicAnims.FlatNames(wad)), a => Assert.False(a.istexture));
    }

    [Fact]
    public void PicAnimsErrorsAsVanilla()
    {
        // A start without its end, and an end before its start.
        Assert.Throws<WadFormatException>(() => PicAnims.Resolve(null, ["NUKAGE1", "NUKAGE2"]));
        Assert.Throws<WadFormatException>(() => PicAnims.Resolve(null, ["NUKAGE3", "NUKAGE2", "NUKAGE1"]));
        // A start the WAD lacks: another episode's, skipped.
        Assert.Empty(PicAnims.Resolve(null, ["NUKAGE3", "FLOOR1"]));
    }

    [Fact]
    public void UpdateSpecialsRotatesTheTranslationsOnLeveltime()
    {
        World world = Specials();
        Assert.Equal(3, world.lastanim);
        int[] identity = [.. Enumerable.Range(0, world.texturetranslation.Length)];
        Assert.Equal(identity, world.texturetranslation); // before the first tic
        for (int tic = 0; tic < 40; tic++)
        {
            int leveltime = world.leveltime;
            world.G_Ticker(new ticcmd_t());
            for (int a = 0; a < world.lastanim; a++)
            {
                anim_t anim = world.anims[a]!;
                int[] translation = anim.istexture ? world.texturetranslation : world.flattranslation;
                for (int i = anim.basepic; i < anim.basepic + anim.numpics; i++)
                    Assert.Equal(anim.basepic + (leveltime / 8 + i) % anim.numpics, translation[i]);
            }
        }
        // Everything else stays itself.
        int sladrip = world.textures!.R_CheckTextureNumForName("SLADRIP1");
        for (int i = 0; i < world.texturetranslation.Length; i++)
        {
            if (i < sladrip || i > sladrip + 2)
                Assert.Equal(i, world.texturetranslation[i]);
        }
    }

    [Fact]
    public void Doom1HasTheNukageSequence()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom1());
        (List<string[]> tex, List<string[]> flats) = PicAnims.Sequences(Textures.R_InitTextures(wad), PicAnims.FlatNames(wad));
        Assert.Contains(flats, f => f.SequenceEqual(["NUKAGE1", "NUKAGE2", "NUKAGE3"]));
        Assert.All(tex.Concat(flats), seq => Assert.InRange(seq.Length, 2, 8));
        // Vanilla's flat numbers (firstflat = F_START + 1, the inner markers counted): the animations'
        // phase depends on them. DOOM1.WAD: F_START is lump 1206, NUKAGE1 lump 1258.
        List<string> names = PicAnims.FlatNames(wad);
        Assert.Equal("F1_START", names[0]);
        Assert.Equal(1258 - 1207, PicAnims.FlatNum(names, "NUKAGE1"));
    }
}
