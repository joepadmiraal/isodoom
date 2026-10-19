using System;
using System.Collections.Generic;
using IsoDoom.Game;
using IsoDoom.Net;
using IsoDoom.Sim;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Net;

/// <summary>
/// T11.6: the command line's netgame (<see cref="NetOptions"/>) and the
/// session it makes (<see cref="NetSession"/>, <see cref="NetExchange"/>),
/// over <see cref="MemoryTransport"/>s (the ENet transport runs in
/// <c>tools/NetLoopback/loopback.sh</c>, which needs the engine).
/// </summary>
public class NetSessionTests
{
    private static NetOptions Parse(params string[] args) => NetOptions.Parse(args)!;

    [Fact]
    public void Without_host_or_connect_there_is_no_netgame()
    {
        Assert.Null(NetOptions.Parse(["-iwad", "doom1.wad", "--level", "E1M1", "-players", "3", "-deathmatch"]));
    }

    [Fact]
    public void A_game_alone_takes_the_modes_too()
    {
        // T11.4: --level E1M1 -deathmatch, without -host
        NetOptions o = NetOptions.Modes(["--level", "E1M1", "-deathmatch", "-timer", "5", "-fast"]);
        Assert.Equal((NetRole.None, 1, 5, true, false), (o.Role, o.Deathmatch, o.TimerMinutes, o.Fast, o.Respawn));
        SpawnSettings s = o.Apply(new SpawnSettings(GameMode.shareware, skill_t.sk_medium, nomonsters: true));
        Assert.Equal((1, 5, true, true, false), (s.deathmatch, s.timelimit, s.fastparm, s.nomonsters, s.netgame));

        // -avg is 20 minutes whatever -timer says (Chocolate Doom's order); -altdeath wins over -deathmatch
        o = NetOptions.Modes(["-avg", "-timer", "3", "-altdeath", "-deathmatch"]);
        Assert.Equal((2, 20), (o.Deathmatch, o.TimerMinutes));
        Assert.Equal(new SpawnSettings(GameMode.shareware, skill_t.sk_medium),
            NetOptions.Modes(["--level", "E1M1"]).Apply(new SpawnSettings(GameMode.shareware, skill_t.sk_medium)));
    }

    [Fact]
    public void The_host_arguments()
    {
        NetOptions o = Parse("-host", "-players", "3", "--level", "E1M1");
        Assert.Equal(NetRole.Host, o.Role);
        Assert.Equal(NetOptions.DefaultPort, o.Port);
        Assert.Equal(3, o.Players);
        Assert.Equal("*", o.BindAddress);
        Assert.Null(o.Delay);

        o = Parse("-host", "6000", "-bind", "127.0.0.1", "-netdelay", "4", "-altdeath", "-timer", "10", "-respawn", "-fast", "-nomonsters");
        Assert.Equal((6000, "127.0.0.1", 4, 2, 10), (o.Port, o.BindAddress, o.Delay, o.Deathmatch, o.TimerMinutes));
        Assert.True(o.Respawn && o.Fast && o.NoMonsters);

        Assert.Equal((NetRole.Host, 7000), (Parse("-server", "-port", "7000").Role, Parse("-server", "-port", "7000").Port));
        Assert.Equal(0, Parse("-host", "0").Port); // any free port
        Assert.Equal(1, Parse("-host", "-deathmatch").Deathmatch);
        Assert.Equal(2, Parse("-host", "-altdeath", "-deathmatch").Deathmatch);
        Assert.Equal(2, Parse("-host").Players);
    }

    [Theory]
    [InlineData("10.0.0.2", "10.0.0.2", NetOptions.DefaultPort)]
    [InlineData("10.0.0.2:6000", "10.0.0.2", 6000)]
    [InlineData("example.org:5030", "example.org", 5030)]
    [InlineData("::1", "::1", NetOptions.DefaultPort)]
    [InlineData("[::1]", "::1", NetOptions.DefaultPort)]
    [InlineData("[fe80::1]:6001", "fe80::1", 6001)]
    public void The_connect_address(string arg, string address, int port)
    {
        NetOptions o = Parse("-connect", arg);
        Assert.Equal((NetRole.Join, address, port), (o.Role, o.ConnectAddress, o.Port));
    }

    [Fact]
    public void A_port_option_applies_to_a_join_without_one()
    {
        Assert.Equal(6100, Parse("-port", "6100", "-connect", "host").Port);
    }

    [Theory]
    [InlineData("-host", "-connect", "x")]
    [InlineData("-host", "-players", "5")]
    [InlineData("-host", "-players", "0")]
    [InlineData("-host", "-netdelay", "0")]
    [InlineData("-host", "-netdelay", "36")]
    [InlineData("-host", "-port", "70000")]
    [InlineData("-connect")]
    [InlineData("-connect", "x:")]
    [InlineData("-connect", "x:0")]
    [InlineData("-connect", "[::1")]
    [InlineData("-connect", "x", "-port", "0")]
    [InlineData("-host", "-timer")]
    public void Bad_arguments_are_refused(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => NetOptions.Parse(args));
    }

    [Theory]
    [InlineData(0, NetOptions.MinDefaultDelay)]
    [InlineData(1_000, 3)]
    [InlineData(23_809, 3)]
    [InlineData(23_810, 4)]
    [InlineData(100_000, 6)]
    [InlineData(10_000_000, NetOptions.MaxDefaultDelay)]
    public void The_default_delay_covers_the_round_trip(long rttUs, int delay)
    {
        Assert.Equal(delay, NetOptions.DelayFromRtt(rttUs));
    }

    [Theory]
    [InlineData("E1M1", 1, 1)]
    [InlineData("e4m9", 4, 9)]
    [InlineData("MAP07", 1, 7)]
    [InlineData("map32", 1, 32)]
    [InlineData(null, 1, 1)]
    [InlineData("TITLE", 1, 1)]
    public void The_first_map(string? map, int episode, int number)
    {
        Assert.Equal((episode, number), NetSession.MapNumbers(map));
    }

    [Fact]
    public void The_host_game_is_a_netgame_with_its_modes()
    {
        var spawn = new SpawnSettings(GameMode.commercial, skill_t.sk_hard, gameversion: GameVersion.exe_final);
        NetGameSettings s = NetSession.GameSettings(Parse("-host", "-altdeath", "-fast", "-timer", "20"), spawn, Tweaks.Vanilla, "MAP07");
        Assert.True(s.Spawn.netgame);
        Assert.Equal((2, true, false, false), (s.Spawn.deathmatch, s.Spawn.fastparm, s.Spawn.respawnparm, s.Spawn.nomonsters));
        Assert.Equal((skill_t.sk_hard, GameVersion.exe_final), (s.Spawn.gameskill, s.Spawn.gameversion));
        Assert.Equal((1, 7, 20), (s.Episode, s.Map, s.Spawn.timelimit));
        Assert.Equal(Tweaks.Vanilla, s.Tweaks);
    }

    [Fact]
    public void A_dev_build_stamp_names_the_sim_and_protocol()
    {
        Assert.Equal("v0.3.0", NetSession.BuildStamp("v0.3.0"));
        string dev = NetSession.BuildStamp("dev");
        Assert.Matches("^dev-[0-9a-f]{16}$", dev);
        Assert.Equal(dev, NetSession.BuildStamp("dev"));
    }

    private static readonly NetIdentity _identity = new("test", "f0cefca49926d00903cf57551d901abe", []);

    private static readonly NetGameSettings _settings = new(new SpawnSettings(GameMode.shareware, skill_t.sk_medium, netgame: true), Tweaks.TopDown, 1, 1);

    /// <summary>A host and joins on a memory network, the joins' links as given, one frame a millisecond.</summary>
    private sealed class Machines
    {
        public MemoryNetwork Network { get; } = new();
        public List<NetSession> Sessions { get; } = [];
        public List<NetExchange?> Exchanges { get; } = [];
        public List<string> Log { get; } = [];

        public Machines(NetOptions host, params MemoryLinkOptions[] joins)
        {
            int n = 0;
            Add(host, _identity, use => Network.CreateEndpoint(use == NetTransportUse.Server ? "127.0.0.1" : "host", seed: n++));
            for (int i = 0; i < joins.Length; i++)
            {
                MemoryLinkOptions link = joins[i];
                int seed = 100 + i;
                Add(Parse("-connect", $"127.0.0.1:{host.Port}"), _identity, _ => Network.CreateEndpoint($"join{seed}", link, seed));
            }
        }

        public void Add(NetOptions options, NetIdentity identity, Func<NetTransportUse, INetTransport> transports)
        {
            int index = Sessions.Count;
            Sessions.Add(new NetSession(options, identity, _settings, transports, line => Log.Add($"{index}: {line}")));
            Exchanges.Add(null);
        }

        public void RunUntil(Func<bool> until, long maxUs)
        {
            long end = Network.NowUs + maxUs;
            while (!until())
            {
                Assert.True(Network.NowUs < end, "not reached: " + string.Join("; ", Log));
                Network.Advance(1_000);
                for (int i = 0; i < Sessions.Count; i++)
                {
                    Sessions[i].Update(Network.NowUs);
                    Exchanges[i]?.Frame();
                }
            }
        }

        public void Exchange(int tics)
        {
            for (int i = 0; i < Sessions.Count; i++)
                Exchanges[i] = new NetExchange(Sessions[i].Client, tics);
        }
    }

    [Fact]
    public void The_host_waits_for_its_players_then_starts_with_the_measured_delay()
    {
        var m = new Machines(Parse("-host", "-players", "3"), new MemoryLinkOptions { LatencyUs = 20_000 }, new MemoryLinkOptions { LatencyUs = 45_000 });
        NetServer server = m.Sessions[0].Server!;
        // Two players in and ready: the host waits for the third.
        m.Sessions.RemoveAt(2);
        m.RunUntil(() => server.PlayerCount == 2 && server.CanStart, 2_000_000);
        m.RunUntil(() => m.Network.NowUs > 3_000_000, 4_000_000);
        Assert.Equal(NetServerPhase.Lobby, server.Phase);

        m.Add(Parse("-connect", "127.0.0.1"), _identity, _ => m.Network.CreateEndpoint("late", new MemoryLinkOptions { LatencyUs = 45_000 }, 7));
        m.RunUntil(() => m.Sessions.TrueForAll(s => s.Client.State == NetClientState.InGame), 5_000_000);
        // T11.12a: the slowest round trip is about 90 ms: with two frames' slack 5 tics, plus one
        Assert.Equal(6, server.Delay);
        Assert.Contains(m.Log, l => l.StartsWith("0: round trips: slowest player 3: ", StringComparison.Ordinal) && l.EndsWith("delay 6 tic(s)", StringComparison.Ordinal));
        Assert.Equal(0b111, m.Sessions[2].Client.Start!.PlayerMask);
        Assert.Equal([0, 1, 2], m.Sessions.ConvertAll(s => s.Client.Slot));
        Assert.True(m.Sessions[2].Client.Start!.Settings.Spawn.netgame);
    }

    [Fact]
    public void A_fixed_delay_is_used_as_given()
    {
        var m = new Machines(Parse("-host", "-netdelay", "7"), MemoryLinkOptions.Perfect);
        m.RunUntil(() => m.Sessions.TrueForAll(s => s.Client.State == NetClientState.InGame), 5_000_000);
        Assert.Equal(7, m.Sessions[1].Client.Start!.Delay);
    }

    [Fact]
    public void Two_machines_exchange_a_thousand_tics_with_the_same_hashes()
    {
        var link = new MemoryLinkOptions { LatencyUs = 15_000, JitterUs = 10_000, UnreliableLoss = 0.1, ReliableLoss = 0.05 };
        var m = new Machines(Parse("-host", "-players", "2"), link);
        m.Exchange(1000);
        Assert.Equal(1015, m.Exchanges[0]!.Tics);
        NetServer server = m.Sessions[0].Server!;
        m.RunUntil(() => m.Exchanges.TrueForAll(x => x!.Done) && server.WindowsCompared >= 29, 60_000_000);
        Assert.Null(server.Desync);
        Assert.Equal(m.Exchanges[0]!.Hash, m.Exchanges[1]!.Hash);
        Assert.Equal(-1, m.Sessions[1].Client.FirstBadTic);
        Assert.All(m.Sessions, s => Assert.Null(s.Failure));
    }

    [Fact]
    public void A_join_on_other_wads_fails_with_the_refusal()
    {
        var m = new Machines(Parse("-host"));
        m.Add(Parse("-connect", "127.0.0.1"), _identity with { IwadMd5 = "00000000000000000000000000000000" },
            _ => m.Network.CreateEndpoint("other", seed: 9));
        m.RunUntil(() => m.Sessions[1].Failure is not null, 2_000_000);
        Assert.Equal("refused: IwadMismatch", m.Sessions[1].Failure);
        Assert.Equal(1, m.Sessions[0].Server!.PlayerCount);
    }

    [Fact]
    public void The_hosts_end_disconnects_the_others()
    {
        var m = new Machines(Parse("-host", "-players", "2"), MemoryLinkOptions.Perfect);
        m.RunUntil(() => m.Sessions.TrueForAll(s => s.Client.State == NetClientState.InGame), 5_000_000);
        m.Sessions[0].Dispose();
        m.Sessions.RemoveAt(0);
        m.RunUntil(() => m.Sessions[0].Failure is not null, 2_000_000);
        Assert.StartsWith("disconnected", m.Sessions[0].Failure, StringComparison.Ordinal);
    }
}
