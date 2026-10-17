using System;
using System.Collections.Generic;
using IsoDoom.Game;
using IsoDoom.Net;
using IsoDoom.Sim;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Net;

/// <summary>
/// T11.9: the session's start policy for a game hosted from the menus
/// (<see cref="NetSession.AutoStart"/> off: the lobby page's Start,
/// <see cref="NetSession.RequestStart"/>, with two players at least), the
/// host's start refusals counted for the menus, and the lobby's players as
/// the host and a join see them; over <see cref="MemoryTransport"/>s.
/// </summary>
public class NetSessionStartTests
{
    private static readonly NetIdentity _identity = new("test", "f0cefca49926d00903cf57551d901abe", []);

    private static readonly NetGameSettings _settings = new(new SpawnSettings(GameMode.shareware, skill_t.sk_medium, netgame: true), Tweaks.TopDown, 1, 1);

    private sealed class Machines
    {
        public MemoryNetwork Network { get; } = new();
        public List<NetSession> Sessions { get; } = [];

        public Machines(bool autoStart, int players, int joins)
        {
            int n = 0;
            Sessions.Add(new NetSession(NetOptions.Parse(["-host", "-players", players.ToString(System.Globalization.CultureInfo.InvariantCulture)])!, _identity, _settings,
                use => Network.CreateEndpoint(use == NetTransportUse.Server ? "127.0.0.1" : "host", seed: n++))
            { AutoStart = autoStart });
            for (int i = 0; i < joins; i++)
                Join(new MemoryLinkOptions { LatencyUs = 10_000 * (i + 1) });
        }

        public NetSession Join(MemoryLinkOptions link)
        {
            int seed = 100 + Sessions.Count;
            var session = new NetSession(NetOptions.Parse(["-connect", "127.0.0.1"])!, _identity, _settings, _ => Network.CreateEndpoint($"join{seed}", link, seed));
            Sessions.Add(session);
            return session;
        }

        public void RunUntil(Func<bool> until, long maxUs)
        {
            long end = Network.NowUs + maxUs;
            while (!until())
            {
                Assert.True(Network.NowUs < end, "not reached");
                Run(1_000);
            }
        }

        public void Run(long us)
        {
            for (long t = 0; t < us; t += 1_000)
            {
                Network.Advance(1_000);
                foreach (NetSession s in Sessions)
                    s.Update(Network.NowUs);
            }
        }
    }

    [Fact]
    public void AGameHostedFromTheMenusStartsOnlyWithTheLobbysStart()
    {
        var m = new Machines(autoStart: false, players: 2, joins: 0);
        NetSession host = m.Sessions[0];
        NetServer server = host.Server!;
        m.RunUntil(() => server.PlayerCount == 1 && server.CanStart, 1_000_000);
        Assert.False(host.RequestStart()); // the host alone
        Assert.False(host.StartRequested);

        m.Join(MemoryLinkOptions.Perfect);
        m.RunUntil(() => server.PlayerCount == 2 && server.CanStart, 2_000_000);
        m.Run(4_000_000); // two in, -players 2: the command line's would have started
        Assert.Equal(NetServerPhase.Lobby, server.Phase);

        Assert.True(host.RequestStart());
        Assert.True(host.StartRequested);
        m.RunUntil(() => m.Sessions.TrueForAll(s => s.Client.State == NetClientState.InGame), 5_000_000);
        Assert.False(host.StartRequested);
        Assert.Equal(0b11, m.Sessions[1].Client.Start!.PlayerMask);
        Assert.False(host.RequestStart()); // started
    }

    [Fact]
    public void TheLobbysStartStartsWithFewerThanThePlayersSlots()
    {
        var m = new Machines(autoStart: false, players: 4, joins: 1);
        NetServer server = m.Sessions[0].Server!;
        m.RunUntil(() => server.PlayerCount == 2 && server.CanStart, 2_000_000);
        Assert.True(m.Sessions[0].RequestStart());
        m.RunUntil(() => m.Sessions.TrueForAll(s => s.Client.State == NetClientState.InGame), 5_000_000);
        Assert.Equal(0b11, m.Sessions[1].Client.Start!.PlayerMask);
    }

    [Fact]
    public void ARefusedStartIsCountedAndEndsTheRequest()
    {
        var m = new Machines(autoStart: false, players: 3, joins: 1);
        NetSession host = m.Sessions[0];
        int checks = 0;
        host.CheckStart = mask => { checks++; return mask == 0b11 ? "no co-op start for player 2 on E1M1" : null; };
        NetServer server = host.Server!;
        m.RunUntil(() => server.PlayerCount == 2 && server.CanStart, 2_000_000);
        Assert.True(host.RequestStart());
        m.RunUntil(() => host.StartRefusals == 1, 1_000_000);
        Assert.False(host.StartRequested);
        Assert.Equal("no co-op start for player 2 on E1M1", host.StartRefusal);
        m.Run(500_000);
        Assert.Equal((1, 1), (host.StartRefusals, checks)); // not asked again: not checked again

        // asked again: checked again, refused again
        Assert.True(host.RequestStart());
        m.RunUntil(() => host.StartRefusals == 2, 1_000_000);

        // a third player: the game can start
        m.Join(MemoryLinkOptions.Perfect);
        m.RunUntil(() => server.PlayerCount == 3 && server.CanStart, 2_000_000);
        Assert.True(host.RequestStart());
        m.RunUntil(() => server.Phase == NetServerPhase.InGame, 5_000_000);
        Assert.Null(host.StartRefusal);
    }

    [Fact]
    public void TheCommandLinesStartRefusalsAreCountedToo()
    {
        var m = new Machines(autoStart: true, players: 2, joins: 0);
        NetSession host = m.Sessions[0];
        host.CheckStart = _ => "fewer than 4 deathmatch starts on E1M1 (3)";
        m.Join(MemoryLinkOptions.Perfect);
        m.RunUntil(() => host.StartRefusals == 1, 2_000_000);
        m.Run(500_000);
        Assert.Equal(1, host.StartRefusals);
        Assert.Equal(NetServerPhase.Lobby, host.Server!.Phase);
    }

    [Fact]
    public void TheHostAndAJoinSeeEveryPlayersPing()
    {
        var m = new Machines(autoStart: false, players: 4, joins: 2);
        NetServer server = m.Sessions[0].Server!;
        m.RunUntil(() => server.PlayerCount == 3 && server.CanStart && server.RttUsOf(1) >= 0 && server.RttUsOf(2) >= 0
            && m.Sessions[1].Client.RttUs >= 0 && m.Sessions[1].Client.LobbyOccupied == 0b111
            && m.Sessions[1].LobbyPlayers().TrueForAll(p => p.RttUs >= 0), 3_000_000);
        List<NetLobbyPlayer> host = m.Sessions[0].LobbyPlayers();
        Assert.Equal([0, 1, 2], host.ConvertAll(p => p.Slot));
        Assert.All(host, p => Assert.True(p.Ready && p.RttUs >= 0));
        Assert.Equal([true, false, false], host.ConvertAll(p => p.Local));
        Assert.InRange(host[2].RttUs, host[1].RttUs, int.MaxValue); // 40 ms against 20 ms

        List<NetLobbyPlayer> join = m.Sessions[1].LobbyPlayers();
        Assert.Equal([0, 1, 2], join.ConvertAll(p => p.Slot));
        Assert.Equal(m.Sessions[1].Client.RttUs, join[1].RttUs); // its own, exact
        Assert.InRange(join[2].RttUs, join[1].RttUs, int.MaxValue); // the others' from the host's pong (T11.11), in ms
        Assert.Equal([false, true, false], join.ConvertAll(p => p.Local));
    }
}
