using System;
using System.Collections.Generic;
using IsoDoom.Net;
using IsoDoom.Sim;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Net;

/// <summary>T11.5: the lobby: slots, the hello's checks, ready and start.</summary>
public class LobbyTests
{
    private static readonly NetGameSettings _settings = new(new SpawnSettings(GameMode.shareware, skill_t.sk_medium, netgame: true), Tweaks.TopDown, 1, 1);

    private static ticcmd_t Idle(int slot, int tic) => default;

    private static LockstepHarness Harness(params PlayerSetup[] setups) => new(_settings, setups, Idle);

    [Theory]
    [InlineData("other build", null, NetRefusal.BuildMismatch)]
    [InlineData(null, "00000000000000000000000000000000", NetRefusal.IwadMismatch)]
    public void A_hello_with_another_build_or_iwad_is_refused(string? build, string? iwad, NetRefusal expected)
    {
        NetIdentity other = LockstepHarness.Identity with
        {
            Build = build ?? LockstepHarness.Identity.Build,
            IwadMd5 = iwad ?? LockstepHarness.Identity.IwadMd5,
        };
        LockstepHarness h = Harness(new PlayerSetup(), new PlayerSetup { Identity = other, Link = new MemoryLinkOptions { LatencyUs = 30_000 } });
        h.RunUntil(() => h.Players[1].Client.State == NetClientState.Refused && h.Players[0].Client.State == NetClientState.Lobby, 2_000_000, "the refusal");
        Assert.Equal(expected, h.Players[1].Client.Refusal);
        Assert.Equal(1, h.Server.PlayerCount);
        h.RunUntil(() => false || h.Network.NowUs > 3_000_000, 4_000_000, "a while");
        Assert.Equal(NetClientState.Refused, h.Players[1].Client.State);
    }

    [Fact]
    public void Pwads_must_match_in_their_order()
    {
        var host = LockstepHarness.Identity with { PwadMd5s = ["11111111111111111111111111111111", "22222222222222222222222222222222"] };
        var swapped = host with { PwadMd5s = ["22222222222222222222222222222222", "11111111111111111111111111111111"] };
        var network = new MemoryNetwork();
        var server = new NetServer(network.CreateEndpoint("host"), host, _settings, hostToken: 1);
        server.Listen(LockstepHarness.Port);
        var same = new NetClient(network.CreateEndpoint("a"), host with { });
        var other = new NetClient(network.CreateEndpoint("b"), swapped);
        same.Connect("host", LockstepHarness.Port);
        other.Connect("host", LockstepHarness.Port);
        for (int i = 0; i < 10; i++)
        {
            network.Advance(1000);
            server.Update(network.NowUs);
            same.Update(network.NowUs);
            other.Update(network.NowUs);
        }
        Assert.Equal(NetClientState.Lobby, same.State);
        Assert.Equal(1, same.Slot);
        Assert.Equal(NetRefusal.PwadMismatch, other.Refusal);
    }

    [Fact]
    public void Another_protocol_is_refused_reading_only_its_version()
    {
        var network = new MemoryNetwork();
        var server = new NetServer(network.CreateEndpoint("host"), LockstepHarness.Identity, _settings, hostToken: 1);
        server.Listen(LockstepHarness.Port);
        MemoryTransport raw = network.CreateEndpoint("raw");
        int peer = raw.Connect("host", LockstepHarness.Port);
        network.Advance(1);
        server.Update(network.NowUs);
        Assert.True(raw.Poll(out NetEvent connected));
        Assert.Equal(NetEventKind.Connected, connected.Kind);
        // A future version's hello: the type byte, its version, then whatever it carries.
        raw.Send(peer, [(byte)NetMessageType.Hello, NetProtocol.Version + 1, 0, 0xde, 0xad], NetChannel.Reliable);
        network.Advance(1);
        server.Update(network.NowUs);
        Assert.True(raw.Poll(out NetEvent refusal));
        var r = new NetReader(refusal.Data);
        Assert.Equal(NetMessageType.Refuse, r.Type);
        Assert.Equal(NetRefusal.ProtocolMismatch, (NetRefusal)r.U8());
        Assert.True(raw.Poll(out NetEvent gone));
        Assert.Equal(NetEventKind.Disconnected, gone.Kind);
        Assert.Equal(0, server.PlayerCount);
    }

    [Fact]
    public void The_host_takes_slot_0_and_the_others_fill_from_1()
    {
        // The host's client joins last; the others still start at slot 1.
        LockstepHarness h = Harness(
            new PlayerSetup { Link = new MemoryLinkOptions { LatencyUs = 50_000 } },
            new PlayerSetup { Link = new MemoryLinkOptions { LatencyUs = 10_000 } },
            new PlayerSetup());
        h.RunUntil(() => h.Players.TrueForAll(p => p.Client.State == NetClientState.Lobby), 2_000_000, "the lobby");
        Assert.Equal([0, 2, 1], h.Players.ConvertAll(p => p.Client.Slot));
        Assert.Equal(0b111, h.Server.OccupiedMask);
        Assert.All(h.Players, p => Assert.Equal(0b111, p.Client.LobbyOccupied));
    }

    [Fact]
    public void A_full_game_and_a_game_under_way_refuse()
    {
        var network = new MemoryNetwork();
        var server = new NetServer(network.CreateEndpoint("host"), LockstepHarness.Identity, _settings, hostToken: 42, maxPlayers: 2);
        server.Listen(LockstepHarness.Port);
        var clients = new List<NetClient>
        {
            new(network.CreateEndpoint("h"), LockstepHarness.Identity, 42),
            new(network.CreateEndpoint("a"), LockstepHarness.Identity),
            new(network.CreateEndpoint("b"), LockstepHarness.Identity),
        };
        void Run()
        {
            for (int i = 0; i < 5; i++)
            {
                network.Advance(1000);
                server.Update(network.NowUs);
                foreach (NetClient c in clients)
                    c.Update(network.NowUs);
            }
        }
        foreach (NetClient c in clients)
            c.Connect("host", LockstepHarness.Port);
        Run();
        Assert.Equal(NetClientState.Lobby, clients[0].State);
        Assert.Equal(NetClientState.Lobby, clients[1].State);
        Assert.Equal(NetRefusal.GameFull, clients[2].Refusal);

        Assert.False(server.CanStart);
        clients[0].SetReady(true);
        Run();
        Assert.False(server.CanStart);
        Assert.Throws<InvalidOperationException>(() => server.Start(2));
        clients[1].SetReady(true);
        Run();
        Assert.Equal(0b11, clients[0].LobbyReady);
        Assert.True(server.CanStart);
        server.Start(2);
        Run();
        Assert.All(clients.GetRange(0, 2), c => Assert.Equal(NetClientState.InGame, c.State));
        // T11.5a: the settings' players in game are the Start's mask.
        Assert.All(clients.GetRange(0, 2), c => Assert.Equal(0b11, c.Settings!.Spawn.playersingame));
        Assert.Equal(0b11, server.Settings.Spawn.playersingame);

        var late = new NetClient(network.CreateEndpoint("late"), LockstepHarness.Identity);
        clients.Add(late);
        late.Connect("host", LockstepHarness.Port);
        Run();
        Assert.Equal(NetRefusal.GameInProgress, late.Refusal);
    }

    [Fact]
    public void A_lobby_player_who_leaves_frees_its_slot()
    {
        LockstepHarness h = Harness(new PlayerSetup(), new PlayerSetup(), new PlayerSetup());
        h.RunUntil(() => h.Players.TrueForAll(p => p.Client.State == NetClientState.Lobby), 2_000_000, "the lobby");
        h.Players[1].Client.Quit();
        h.RunUntil(() => h.Players[0].Client.LobbyOccupied == 0b101, 2_000_000, "slot 1 free");
        Assert.Equal(2, h.Server.PlayerCount);
    }

    [Fact]
    public void A_malformed_message_drops_its_sender()
    {
        var network = new MemoryNetwork();
        var server = new NetServer(network.CreateEndpoint("host"), LockstepHarness.Identity, _settings, hostToken: 1);
        server.Listen(LockstepHarness.Port);
        var log = new List<string>();
        server.Log += log.Add;
        var client = new NetClient(network.CreateEndpoint("c"), LockstepHarness.Identity, 1);
        client.Connect("host", LockstepHarness.Port);
        for (int i = 0; i < 3; i++)
        {
            network.Advance(1000);
            server.Update(network.NowUs);
            client.Update(network.NowUs);
        }
        Assert.Equal(1, server.PlayerCount);
        // A Ready message one byte short.
        MemoryTransport raw = network.CreateEndpoint("raw");
        int peer = raw.Connect("host", LockstepHarness.Port);
        network.Advance(1);
        server.Update(network.NowUs);
        raw.Send(peer, NetMessages.EncodeHello(LockstepHarness.Identity, 0), NetChannel.Reliable);
        raw.Send(peer, [(byte)NetMessageType.Ready], NetChannel.Reliable);
        network.Advance(1);
        server.Update(network.NowUs);
        Assert.Contains(log, line => line.Contains("dropped", StringComparison.Ordinal));
        Assert.Equal(1, server.PlayerCount);
    }
}
