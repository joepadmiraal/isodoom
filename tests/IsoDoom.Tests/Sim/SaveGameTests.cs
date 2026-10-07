using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T7.6: a game saved mid-route (<see cref="World.P_ArchiveGame"/>), loaded
/// into a new world (<see cref="World.P_UnArchiveGame"/>) and played on
/// gets the same checksum on every tic as the run that was never saved, and
/// saves the same bytes, on every map of DOOM1.WAD's first episode (its exit
/// routes, the fights and the demos, with monsters), the synthetic IWAD's
/// and the test maps' routes (without the WAD, in CI). Damaged saves are refused.
/// </summary>
public class SaveGameTests
{
    /// <summary>Saves <paramref name="world"/> as the game does (the start, then the state).</summary>
    public static byte[] Save(World world)
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream))
            world.P_ArchiveGame(w);
        return stream.ToArray();
    }

    /// <summary>Loads a save into a new world on its map freshly loaded from <paramref name="wad"/>, as the game does.</summary>
    public static World Load(byte[] save, WadArchive wad)
    {
        using var r = new BinaryReader(new MemoryStream(save));
        SaveGameStart start = World.P_ReadSaveStart(r);
        var world = new World(start.settings, start.tweaks)
        {
            textures = wad.W_CheckNumForName("TEXTURE1") >= 0 ? Textures.R_InitTextures(wad) : null,
        };
        world.P_InitPicAnims(world.textures, PicAnims.FlatNames(wad));
        world.P_UnArchiveGame(r, Level.Load(wad, start.map));
        Assert.Equal(save.Length, r.BaseStream.Position);
        return world;
    }

    public static TheoryData<string> Routes() => new(VanillaRoute.Names().Where(n =>
    {
        VanillaRoute r = VanillaRoute.Load(n);
        // every DOOM1 map's exit route and fight, and every route without the WAD
        return r.Iwad != "doom1" || r.Exit != 0 || r.Monsters;
    }));

    [Theory]
    [MemberData(nameof(Routes))]
    public void ARouteGoesOnAfterALoad(string route) => Check(VanillaRoute.Load(route));

    [Theory]
    [InlineData("DEMO1")]
    [InlineData("DEMO2")]
    [InlineData("DEMO3")]
    public void ADemoGoesOnAfterALoad(string demo) => Check(VanillaRoute.Demo(demo));

    /// <summary>
    /// T7.6's *Done when*: every DOOM1.WAD episode 1 map is saved, loaded and
    /// played on (<see cref="ARouteGoesOnAfterALoad"/> runs them all; this
    /// checks the list covers E1M1–E1M9).
    /// </summary>
    [Fact]
    public void EveryEpisode1MapIsCovered()
    {
        var maps = Routes().Select(r => VanillaRoute.Load(r.Data)).Where(r => r.Iwad == "doom1").Select(r => r.Map).ToHashSet();
        for (int m = 1; m <= 9; m++)
            Assert.Contains($"E1M{m}", maps);
    }

    /// <summary>
    /// Plays <paramref name="route"/> through once, saving at its start, at
    /// a quarter, half and three quarters of it, and just before its end;
    /// then loads each save, checks it saves again the same bytes, plays on
    /// from it and compares every tic's checksum (and, every second and at
    /// the end, the whole saved state) with the run that never stopped.
    /// </summary>
    private static void Check(VanillaRoute route)
    {
        World world = route.NewWorld(out WadArchive wad);
        int n = route.Cmds.Count;
        var at = new SortedSet<int> { 0, n / 4, n / 2, 3 * n / 4, Math.Max(0, n - 2) };
        var saves = new Dictionary<int, byte[]>();
        var checksums = new ulong[n];
        var states = new Dictionary<int, byte[]>();
        for (int tic = 0; tic < n; tic++)
        {
            route.Reborn(world, wad);
            if (at.Contains(tic) && world.gameaction == gameaction_t.ga_nothing)
                saves[tic] = Save(world);
            route.File.RunEvents(world, tic);
            world.G_Ticker(route.Cmds[tic]);
            checksums[tic] = world.Checksum();
            if ((tic % 35 == 0 || tic == n - 1) && world.gameaction == gameaction_t.ga_nothing)
                states[tic] = Save(world);
        }
        Assert.NotEmpty(saves);

        foreach (int from in at)
        {
            if (!saves.TryGetValue(from, out byte[]? save))
                continue;
            World loaded = Load(save, wad);
            Assert.True(save.AsSpan().SequenceEqual(Save(loaded)), $"{route.Name}: the save at tic {from} loads into another state");
            for (int tic = from; tic < n; tic++)
            {
                route.Reborn(loaded, wad);
                route.File.RunEvents(loaded, tic);
                loaded.G_Ticker(route.Cmds[tic]);
                Assert.True(checksums[tic] == loaded.Checksum(),
                    $"{route.Name}: saved before tic {from + 1} and loaded, tic {tic + 1}'s checksum differs");
                if (states.TryGetValue(tic, out byte[]? state) && loaded.gameaction == gameaction_t.ga_nothing)
                    Assert.True(state.AsSpan().SequenceEqual(Save(loaded)), $"{route.Name}: saved before tic {from + 1} and loaded, tic {tic + 1}'s state differs");
            }
        }
    }

    /// <summary>A save cut short, with a byte changed in its data, or for a map with other sectors is refused with a <see cref="SaveGameException"/>.</summary>
    [Fact]
    public void ADamagedSaveIsRefused()
    {
        VanillaRoute route = VanillaRoute.Load("synthetic-monsters");
        World world = route.NewWorld(out WadArchive wad);
        for (int tic = 0; tic < 100; tic++)
            world.G_Ticker(route.Cmds[tic]);
        byte[] save = Save(world);
        Load(save, wad); // whole: loads

        foreach (int cut in new[] { 0, 3, 40, save.Length / 2, save.Length - 1 })
            Assert.Throws<SaveGameException>(() => Load(save[..cut], wad));

        // the map name another map's
        byte[] other = (byte[])save.Clone();
        int name = IndexOf(other, "E1M1"u8);
        other[name + 3] = (byte)'2';
        Assert.Throws<SaveGameException>(() => Load(other, wad));

        // the thinker count garbage
        int table = name + 4;
        byte[] count = (byte[])save.Clone();
        count[table + 3] = 0x7f;
        Assert.Throws<SaveGameException>(() => Load(count, wad));
    }

    private static int IndexOf(byte[] data, ReadOnlySpan<byte> what)
    {
        int i = data.AsSpan().IndexOf(what);
        Assert.True(i >= 0);
        return i;
    }

    /// <summary>A missile whose shooter was removed still points at it: the removed mobj is saved with it, out of the thinker list.</summary>
    [Fact]
    public void ARemovedTargetIsKept()
    {
        VanillaRoute route = VanillaRoute.Load("synthetic-monsters");
        World world = route.NewWorld(out WadArchive wad);
        world.G_Ticker(route.Cmds[0]);
        mobj_t player = world.players[0].mo!;
        mobj_t imp = world.P_SpawnMobj(player.x + 128 * Fixed.FRACUNIT, player.y, World.ONFLOORZ, mobjtype_t.MT_TROOP);
        mobj_t ball = world.P_SpawnMobj(player.x + 64 * Fixed.FRACUNIT, player.y, player.z + 32 * Fixed.FRACUNIT, mobjtype_t.MT_TROOPSHOT);
        ball.target = imp;
        world.P_RemoveMobj(imp);
        world.G_Ticker(new ticcmd_t()); // unlinks the imp
        Assert.DoesNotContain(imp, world.Mobjs());

        World loaded = Load(Save(world), wad);
        mobj_t loadedBall = loaded.Mobjs().Single(m => m.type == mobjtype_t.MT_TROOPSHOT);
        Assert.NotNull(loadedBall.target);
        Assert.Equal(mobjtype_t.MT_TROOP, loadedBall.target!.type);
        Assert.DoesNotContain(loadedBall.target, loaded.Mobjs());
        Assert.Equal(world.Checksum(), loaded.Checksum());
    }
}
