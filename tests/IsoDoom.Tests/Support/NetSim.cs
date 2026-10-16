using System;
using System.IO;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Support;

/// <summary>
/// T11.1: a netgame without a network. K worlds of the same game, as K
/// machines would run it, each with another <see cref="World.consoleplayer"/>
/// (machine k shows player <c>ConsolePlayers[k]</c>), all fed the same
/// <c>ticcmd_t[]</c> each tic (vanilla's <c>netcmds</c>, one command per
/// player) through <see cref="World.G_Ticker(ticcmd_t[])"/>. <see cref="Tic"/>
/// asserts that every world's <see cref="World.Checksum"/> is the same after
/// each tic (the lockstep game's desync check, SPEC §6.1), and
/// <see cref="AssertSameState"/> compares the whole saved state.
/// </summary>
public sealed class NetSim
{
    /// <summary>
    /// The game on <paramref name="map"/> of <paramref name="wad"/> with
    /// <paramref name="settings"/> (its <see cref="SpawnSettings.playersingame"/>
    /// the players) and <paramref name="tweaks"/>, one world per entry of
    /// <paramref name="consoleplayers"/>, each with its own freshly loaded
    /// level. <paramref name="setup"/> runs on each world after its level
    /// loads (the same on every machine: e.g. god mode for scripted play).
    /// </summary>
    public NetSim(WadArchive wad, string map, SpawnSettings settings, Tweaks tweaks, int[] consoleplayers, Action<World>? setup = null)
    {
        Worlds = new World[consoleplayers.Length];
        for (int k = 0; k < consoleplayers.Length; k++)
        {
            var world = new World(settings, tweaks)
            {
                textures = wad.W_CheckNumForName("TEXTURE1") >= 0 ? Textures.R_InitTextures(wad) : null,
                map31exists = wad.W_CheckNumForName("MAP31") >= 0,
                consoleplayer = consoleplayers[k],
            };
            world.P_InitPicAnims(world.textures, PicAnims.FlatNames(wad));
            world.G_DoLoadLevel(Level.Load(wad, map));
            setup?.Invoke(world);
            Worlds[k] = world;
        }
        Checksum = AssertInSync();
    }

    /// <summary>The worlds, one per machine.</summary>
    public World[] Worlds { get; }

    /// <summary>The first world (machine 0's), for reading the shared state.</summary>
    public World World => Worlds[0];

    /// <summary>The worlds' checksum after the last tic (or the level's start).</summary>
    public ulong Checksum { get; private set; }

    /// <summary>The tics run.</summary>
    public int Tics { get; private set; }

    /// <summary>Runs one tic of <paramref name="netcmds"/> (indexed by player) on every world and checks they agree.</summary>
    public ulong Tic(ticcmd_t[] netcmds)
    {
        foreach (World world in Worlds)
            world.G_Ticker(netcmds);
        Tics++;
        return Checksum = AssertInSync();
    }

    /// <summary>
    /// Runs <paramref name="tics"/> tics, each player in the game's command
    /// from <paramref name="input"/> (tic, player), checking every tic.
    /// </summary>
    public void Run(int tics, Func<int, int, ticcmd_t> input)
    {
        var netcmds = new ticcmd_t[World.MAXPLAYERS];
        for (int t = 0; t < tics; t++)
        {
            for (int i = 0; i < World.MAXPLAYERS; i++)
                netcmds[i] = World.playeringame[i] ? input(Tics, i) : default;
            Tic(netcmds);
        }
    }

    /// <summary>Fails unless every world has the same <see cref="World.Checksum"/>; returns it.</summary>
    public ulong AssertInSync()
    {
        ulong h = Worlds[0].Checksum();
        for (int k = 1; k < Worlds.Length; k++)
        {
            ulong hk = Worlds[k].Checksum();
            if (hk != h)
                Assert.Fail($"desync at tic {Tics}: machine 0 (consoleplayer {Worlds[0].consoleplayer}) {h:x16}, machine {k} (consoleplayer {Worlds[k].consoleplayer}) {hk:x16}");
        }
        return h;
    }

    /// <summary>
    /// Fails unless every world's saved state (<see cref="World.P_ArchiveGame"/>:
    /// everything the sim keeps from one tic to the next) is the same bytes.
    /// </summary>
    public void AssertSameState()
    {
        byte[] first = Save(Worlds[0]);
        for (int k = 1; k < Worlds.Length; k++)
            Assert.True(first.AsSpan().SequenceEqual(Save(Worlds[k])), $"machine {k}'s state differs from machine 0's after tic {Tics}");
    }

    private static byte[] Save(World world)
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream))
            world.P_ArchiveGame(w);
        return stream.ToArray();
    }

    /// <summary>A mask of players 1 to <paramref name="count"/> (<see cref="SpawnSettings.playersingame"/>).</summary>
    public static int FirstPlayers(int count) => (1 << count) - 1;

    /// <summary>One machine per player of <paramref name="count"/>, each showing its own.</summary>
    public static int[] EachPlayer(int count)
    {
        int[] cps = new int[count];
        for (int i = 0; i < count; i++)
            cps[i] = i;
        return cps;
    }
}
