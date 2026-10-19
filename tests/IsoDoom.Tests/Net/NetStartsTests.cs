using System;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Game;
using IsoDoom.Net;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Net;

/// <summary>
/// T11.1a, checked by T11.8 where the host starts the game: a co-op game is
/// refused when its first map lacks a start for a player in it (T11.1b: a
/// later map's is a warning, and the netgame ends there), a deathmatch game
/// when its start map has fewer than 4 deathmatch starts
/// (<see cref="NetStarts"/>), and the refused game never starts (<see cref="NetSession.CheckStart"/>).
/// </summary>
public sealed class NetStartsTests
{
    /// <summary>A WAD of map headers, each followed by a <c>THINGS</c> lump of the given thing types (the rest of a map is not read).</summary>
    private static WadArchive Maps(params (string Name, short[] Types)[] maps)
    {
        var w = new WadBuilder(WadType.Iwad);
        foreach ((string name, short[] types) in maps)
        {
            var things = new List<byte>();
            foreach (short type in types)
            {
                foreach (short v in new short[] { 0, 0, 90, type, 7 })
                    things.AddRange(BitConverter.GetBytes(v));
            }
            w.Lump(name).Lump("THINGS", [.. things]);
        }
        return new WadArchive([w.ToWadFile()]);
    }

    private static readonly short[] _all = [1, 2, 3, 4, 11, 11, 11, 11];

    [Fact]
    public void Doom_episodes_reach_their_later_maps_and_the_secret_map_until_its_exit()
    {
        WadArchive wad = Maps([.. Enumerable.Range(1, 9).Select(m => ($"E1M{m}", _all)), .. Enumerable.Range(1, 9).Select(m => ($"E3M{m}", _all))]);
        Assert.Equal(["E1M1", "E1M2", "E1M3", "E1M4", "E1M5", "E1M6", "E1M7", "E1M8", "E1M9"], NetStarts.ReachableMaps(wad, GameMode.registered, 1, 1));
        Assert.Equal(["E1M4", "E1M5", "E1M6", "E1M7", "E1M8"], NetStarts.ReachableMaps(wad, GameMode.registered, 1, 4)); // past E1M3's secret exit
        Assert.Equal(["E1M9", "E1M4", "E1M5", "E1M6", "E1M7", "E1M8"], NetStarts.ReachableMaps(wad, GameMode.registered, 1, 9));
        Assert.Equal(["E3M6", "E3M7", "E3M8", "E3M9"], NetStarts.ReachableMaps(wad, GameMode.registered, 3, 6));
        Assert.Equal(["E3M9", "E3M7", "E3M8"], NetStarts.ReachableMaps(wad, GameMode.registered, 3, 9));
        Assert.Empty(NetStarts.ReachableMaps(wad, GameMode.registered, 2, 1)); // the WAD has no E2
    }

    [Fact]
    public void Doom_II_reaches_the_secret_maps_from_the_first_fifteen()
    {
        WadArchive wad = Maps([.. Enumerable.Range(1, 32).Select(m => ($"MAP{m:00}", _all))]);
        Assert.Equal([.. Enumerable.Range(1, 32).Select(m => $"MAP{m:00}")], NetStarts.ReachableMaps(wad, GameMode.commercial, 1, 1));
        Assert.Equal([.. Enumerable.Range(16, 15).Select(m => $"MAP{m:00}")], NetStarts.ReachableMaps(wad, GameMode.commercial, 1, 16));
        Assert.Equal(["MAP31", "MAP32", .. Enumerable.Range(16, 15).Select(m => $"MAP{m:00}")], NetStarts.ReachableMaps(wad, GameMode.commercial, 1, 31));
        Assert.Equal(["MAP32", .. Enumerable.Range(16, 15).Select(m => $"MAP{m:00}")], NetStarts.ReachableMaps(wad, GameMode.commercial, 1, 32));
        Assert.Equal(["MAP30"], NetStarts.ReachableMaps(wad, GameMode.commercial, 1, 30));
    }

    [Fact]
    public void A_co_op_game_is_refused_when_its_first_map_lacks_a_players_start()
    {
        WadArchive wad = Maps(("E1M1", [1, 2, 3, 4]), ("E1M2", [1, 2]), ("E1M3", [1, 3]));
        Assert.Null(NetStarts.Refusal(wad, GameMode.registered, 1, 1, 0b1));
        Assert.Null(NetStarts.Refusal(wad, GameMode.registered, 1, 1, 0b1111)); // T11.1b: a later map's are not refused
        Assert.Equal("no co-op start for players 2 and 4 on E1M3", NetStarts.Refusal(wad, GameMode.registered, 1, 3, 0b1111));
        Assert.Equal("no co-op start for player 2 on E1M3", NetStarts.Refusal(wad, GameMode.registered, 1, 3, 0b11));
        Assert.Equal([2, 4], NetStarts.MissingStarts(wad, GameMode.registered, "E1M3", 0b1111));
    }

    [Fact]
    public void The_lobby_warns_of_a_later_map_lacking_a_players_start()
    {
        WadArchive wad = Maps(("E1M1", [1, 2, 3, 4]), ("E1M2", [1, 2]), ("E1M3", [1, 3]));
        Assert.Null(NetStarts.Warning(wad, GameMode.registered, 1, 1, 0b1));
        Assert.Equal("E1M3 lacks player 2's start: the game will end there", NetStarts.Warning(wad, GameMode.registered, 1, 1, 0b11));
        Assert.Equal("E1M2 lacks players 3 and 4's starts and E1M3 lacks players 2 and 4's starts: the game will end at the first of them",
            NetStarts.Warning(wad, GameMode.registered, 1, 1, 0b1111));
        Assert.Equal("E1M3 lacks player 2's start: the game will end there", NetStarts.Warning(wad, GameMode.registered, 1, 2, 0b11));
        Assert.Null(NetStarts.Warning(wad, GameMode.registered, 1, 3, 0b11)); // the first map's is a refusal, not a warning
        Assert.Null(NetStarts.Warning(wad, GameMode.registered, 1, 1, 0b11, deathmatch: true));
    }

    [Fact]
    public void A_map_lacking_a_start_for_a_player_in_the_game_is_where_the_netgame_ends()
    {
        WadArchive wad = Maps(("E1M1", [1, 2, 3, 4]), ("E1M3", [1, 3]));
        Assert.Null(NetStarts.LevelRefusal(wad, GameMode.registered, "E1M1", [true, true, true, true]));
        Assert.Equal("no co-op start for player 2", NetStarts.LevelRefusal(wad, GameMode.registered, "E1M3", [true, true, false, false]));
        Assert.Equal("no co-op start for players 2 and 4", NetStarts.LevelRefusal(wad, GameMode.registered, "E1M3", [true, true, true, true]));
        Assert.Null(NetStarts.LevelRefusal(wad, GameMode.registered, "E1M3", [true, false, true, false])); // player 2 left the game (T11.11)
    }

    [Fact]
    public void A_Doom_II_co_op_game_starts_on_map01_and_is_warned_of_a_map_lacking_a_start()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom2());
        Assert.Null(NetStarts.Refusal(wad, GameMode.commercial, 1, 1, 0b11)); // T11.1b: v1.666's MAP29 refused every first map before
        // v1.666's MAP29 has player 1's start only (only what this release lacks is asserted)
        IReadOnlyList<int> missing = NetStarts.MissingStarts(wad, GameMode.commercial, "MAP29", 0b11);
        if (missing.Count > 0)
        {
            Assert.Equal("MAP29 lacks player 2's start: the game will end there", NetStarts.Warning(wad, GameMode.commercial, 1, 1, 0b11));
            Assert.Equal("no co-op start for player 2 on MAP29", NetStarts.Refusal(wad, GameMode.commercial, 1, 29, 0b11));
            Assert.Equal("no co-op start for player 2", NetStarts.LevelRefusal(wad, GameMode.commercial, "MAP29", [true, true, false, false]));
            Assert.Null(NetStarts.Warning(wad, GameMode.commercial, 1, 30, 0b11)); // MAP30 ends the game
        }
        else
            Assert.Null(NetStarts.Warning(wad, GameMode.commercial, 1, 1, 0b11));
    }

    [Fact]
    public void A_deathmatch_game_is_refused_when_its_start_map_has_fewer_than_four_deathmatch_starts()
    {
        WadArchive wad = Maps(("E1M1", [1, 11, 11, 11, 11]), ("E1M2", [1, 11, 11, 11]));
        Assert.Null(NetStarts.Refusal(wad, GameMode.registered, 1, 1, 0b1111, deathmatch: true)); // a later map's are not checked
        Assert.Equal("fewer than 4 deathmatch starts on E1M2 (3)", NetStarts.Refusal(wad, GameMode.registered, 1, 2, 0b1111, deathmatch: true));
        Assert.Equal(3, NetStarts.DeathmatchStarts(wad, GameMode.registered, "E1M2"));
        Assert.Null(NetStarts.Refusal(wad, GameMode.registered, 1, 1, 0b1)); // (co-op: player 1's start is there)
    }

    [Fact]
    public void Outside_Doom_II_the_things_after_a_Doom_II_monster_are_not_read()
    {
        WadArchive wad = Maps(("E1M1", [1, 65, 2]));
        Assert.Equal([2], NetStarts.MissingStarts(wad, GameMode.registered, "E1M1", 0b11)); // P_LoadThings stops at the chaingunner
        Assert.Empty(NetStarts.MissingStarts(wad, GameMode.commercial, "E1M1", 0b11));
    }

    [Fact]
    public void The_synthetic_iwad_starts_two_to_four_players_in_co_op()
    {
        var wad = new WadArchive([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);
        Assert.Equal(["E1M1", "E1M2"], NetStarts.ReachableMaps(wad, GameMode.shareware, 1, 1));
        Assert.Null(NetStarts.Refusal(wad, GameMode.shareware, 1, 1, 0b1111));
        Assert.Null(NetStarts.Refusal(wad, GameMode.shareware, 1, 1, 0b1111, deathmatch: true));
    }

    [Fact]
    public void Doom1_starts_four_players_on_every_map()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom1());
        Assert.Null(NetStarts.Refusal(wad, GameMode.shareware, 1, 1, 0b1111));
    }

    [Fact]
    public void A_Doom_II_deathmatch_starts_on_map01_though_map28_has_two_deathmatch_starts()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom2());
        int count = NetStarts.DeathmatchStarts(wad, GameMode.commercial, "MAP28"); // 2 in v1.666 (only what every release has is asserted)
        Assert.Null(NetStarts.Refusal(wad, GameMode.commercial, 1, 1, 0b11, deathmatch: true));
        if (count < 4)
            Assert.Equal($"fewer than 4 deathmatch starts on MAP28 ({count})", NetStarts.Refusal(wad, GameMode.commercial, 1, 28, 0b11, deathmatch: true));
    }

    [Fact]
    public void A_warned_game_starts_and_the_host_logs_the_warning()
    {
        var network = new MemoryNetwork();
        var identity = new NetIdentity("test", "f0cefca49926d00903cf57551d901abe", []);
        var settings = new NetGameSettings(new SpawnSettings(GameMode.commercial, skill_t.sk_medium, netgame: true), Tweaks.TopDown, 1, 1);
        var log = new List<string>();
        int n = 0;
        var host = new NetSession(NetOptions.Parse(["-host", "-players", "2"])!, identity, settings,
            use => network.CreateEndpoint(use == NetTransportUse.Server ? "127.0.0.1" : "host", seed: n++), log.Add);
        host.CheckStart = _ => null;
        host.CheckWarning = mask => mask == 0b11 ? "MAP29 lacks player 2's start: the game will end there" : null;
        var join = new NetSession(NetOptions.Parse(["-connect", $"127.0.0.1:{host.Port}"])!, identity, settings, _ => network.CreateEndpoint("join", seed: 7));
        for (int t = 0; t < 5000 && join.Client.State != NetClientState.InGame; t++)
        {
            network.Advance(1_000);
            host.Update(network.NowUs);
            join.Update(network.NowUs);
        }
        Assert.Equal(NetClientState.InGame, join.Client.State);
        Assert.Null(host.StartRefusal);
        Assert.Equal("MAP29 lacks player 2's start: the game will end there", host.StartWarning);
        Assert.Single(log, l => l == "warning: MAP29 lacks player 2's start: the game will end there");
        Assert.Contains("the game starts, but MAP29 lacks player 2's start: the game will end there", log);
        host.Dispose();
        join.Dispose();
    }

    [Fact]
    public void A_refused_game_never_starts_and_the_host_says_why()
    {
        var network = new MemoryNetwork();
        var identity = new NetIdentity("test", "f0cefca49926d00903cf57551d901abe", []);
        var settings = new NetGameSettings(new SpawnSettings(GameMode.shareware, skill_t.sk_medium, netgame: true), Tweaks.TopDown, 1, 1);
        var log = new List<string>();
        int n = 0;
        var host = new NetSession(NetOptions.Parse(["-host", "-players", "2"])!, identity, settings,
            use => network.CreateEndpoint(use == NetTransportUse.Server ? "127.0.0.1" : "host", seed: n++), log.Add);
        var masks = new List<byte>();
        host.CheckStart = mask =>
        {
            masks.Add(mask);
            return mask == 0b11 ? "no co-op start for player 2 on E1M1" : null;
        };
        var join = new NetSession(NetOptions.Parse(["-connect", $"127.0.0.1:{host.Port}"])!, identity, settings, _ => network.CreateEndpoint("join", seed: 7));
        for (int t = 0; t < 5000; t++)
        {
            network.Advance(1_000);
            host.Update(network.NowUs);
            join.Update(network.NowUs);
        }
        Assert.Equal(NetServerPhase.Lobby, host.Server!.Phase);
        Assert.Equal(NetClientState.Lobby, join.Client.State);
        Assert.Equal("no co-op start for player 2 on E1M1", host.StartRefusal);
        Assert.Equal([(byte)0b11], masks); // checked once for these players
        Assert.Contains(log, l => l.Contains("the game cannot start: no co-op start for player 2 on E1M1", StringComparison.Ordinal));
        host.Dispose();
        join.Dispose();
    }
}
