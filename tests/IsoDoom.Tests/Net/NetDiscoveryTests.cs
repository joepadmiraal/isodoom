using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using IsoDoom.Game;
using IsoDoom.Net;
using IsoDoom.Sim;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Net;

/// <summary>
/// T11.9d: LAN discovery (<c>NetDiscovery.cs</c>): the query and reply
/// formats, a responder answering a browser on loopback, entries expiring,
/// the compatibility reasons, the rate limit and a taken port. Every socket
/// is on an ephemeral port of 127.0.0.1 (never the fixed 5028: the tests run
/// in parallel), each browser aimed at its responders.
/// </summary>
public class NetDiscoveryTests
{
    private static readonly NetIdentity _identity = new("test", "f0cefca49926d00903cf57551d901abe", []);

    private static NetDiscoveryReply Reply(string name = "JOEP", ulong instance = 7, bool started = false, int players = 1, int max = 4) =>
        new(0, instance, NetProtocol.Version, _identity.Build, _identity.IwadMd5, "DOOM1.WAD", 0, NetDiscovery.PwadHash([]), name, 5029,
            started, players, max, 0, "E1M1", 3);

    /// <summary>Polls until <paramref name="done"/> (2 s at most), the clock at <paramref name="nowUs"/>: loopback datagrams take a moment.</summary>
    private static bool Pump(Func<bool> done, long nowUs, params Action<long>[] updates)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 2000)
        {
            foreach (Action<long> update in updates)
                update(nowUs);
            if (done())
                return true;
            Thread.Sleep(2);
        }
        return false;
    }

    private static List<IPEndPoint> At(params NetDiscoveryResponder[] responders) => [.. responders.Select(r => new IPEndPoint(IPAddress.Loopback, r.Port))];

    [Fact]
    public void AQueryIsItsSizeAndCarriesItsNonce()
    {
        byte[] query = NetDiscovery.EncodeQuery(0xdeadbeef);
        Assert.Equal(NetDiscovery.QuerySize, query.Length);
        Assert.Equal("ISDQ"u8.ToArray(), query[..4]);
        Assert.True(NetDiscovery.TryDecodeQuery(query, out uint nonce));
        Assert.Equal(0xdeadbeefu, nonce);
    }

    [Fact]
    public void AQueryOfAnotherSizeMagicOrVersionIsIgnored()
    {
        byte[] query = NetDiscovery.EncodeQuery(1);
        Assert.False(NetDiscovery.TryDecodeQuery(query.AsSpan(0, NetDiscovery.QuerySize - 1), out _));
        Assert.False(NetDiscovery.TryDecodeQuery([.. query, 0], out _));
        byte[] magic = (byte[])query.Clone();
        magic[0] = (byte)'X';
        Assert.False(NetDiscovery.TryDecodeQuery(magic, out _));
        byte[] version = (byte[])query.Clone();
        version[4] = NetDiscovery.Version + 1;
        Assert.False(NetDiscovery.TryDecodeQuery(version, out _));
        Assert.False(NetDiscovery.TryDecodeReply(query, out _)); // a query is no reply
    }

    [Fact]
    public void AReplyRoundTrips()
    {
        NetDiscoveryReply reply = Reply() with
        {
            Nonce = 42,
            Instance = 0x0123456789abcdef,
            PwadCount = 2,
            PwadHash = NetDiscovery.PwadHash(["00112233445566778899aabbccddeeff", "ffeeddccbbaa99887766554433221100"]),
            GamePort = 10666,
            Started = true,
            Players = 3,
            Deathmatch = 2,
            MapName = "MAP07",
            Skill = 5,
        };
        byte[] bytes = NetDiscovery.EncodeReply(reply);
        Assert.Equal("ISDR"u8.ToArray(), bytes[..4]);
        Assert.True(NetDiscovery.TryDecodeReply(bytes, out NetDiscoveryReply? back));
        Assert.Equal(reply, back);
        Assert.False(NetDiscovery.TryDecodeReply(bytes.AsSpan(0, bytes.Length - 1), out _));
        Assert.False(NetDiscovery.TryDecodeReply([.. bytes, 0], out _));
        byte[] version = (byte[])bytes.Clone();
        version[4] = NetDiscovery.Version + 1;
        Assert.False(NetDiscovery.TryDecodeReply(version, out _));
    }

    [Fact]
    public void AReplyIsNeverBiggerThanTheQueryTheNameCutFirst()
    {
        NetDiscoveryReply big = Reply(name: new string('N', 200)) with { MapName = new string('M', 100), IwadName = new string('I', 200) };
        byte[] bytes = NetDiscovery.EncodeReply(big);
        Assert.True(bytes.Length <= NetDiscovery.QuerySize);
        Assert.True(NetDiscovery.TryDecodeReply(bytes, out NetDiscoveryReply? back));
        Assert.Equal("", back.HostName); // the name went first, then the map; the IWAD's name kept what fits
        Assert.Equal("", back.MapName);
        Assert.StartsWith("III", back.IwadName, StringComparison.Ordinal);
        Assert.Equal(NetDiscovery.QuerySize, bytes.Length);

        // a name only a little too long loses only its end
        NetDiscoveryReply near = Reply() with { IwadName = new string('I', 100), Build = new string('b', 60), HostName = new string('N', 40) };
        Assert.True(NetDiscovery.TryDecodeReply(NetDiscovery.EncodeReply(near), out NetDiscoveryReply? cut));
        Assert.Equal(NetDiscovery.QuerySize - 63 - 60 - 100 - 4, cut.HostName.Length);
        Assert.Equal("E1M1", cut.MapName);

        Assert.Throws<ArgumentException>(() => NetDiscovery.EncodeReply(Reply() with { Build = new string('b', 250) }));
    }

    [Fact]
    public void ThePwadHashFollowsTheListAndItsOrder()
    {
        string a = "00112233445566778899aabbccddeeff", b = "ffeeddccbbaa99887766554433221100";
        Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", NetDiscovery.PwadHash([])); // MD5 of nothing
        Assert.Equal(NetDiscovery.PwadHash([a, b]), NetDiscovery.PwadHash([a.ToUpperInvariant(), b]));
        Assert.NotEqual(NetDiscovery.PwadHash([a, b]), NetDiscovery.PwadHash([b, a]));
        Assert.NotEqual(NetDiscovery.PwadHash([a]), NetDiscovery.PwadHash([a, b]));
    }

    [Theory]
    [InlineData("protocol", NetRefusal.ProtocolMismatch, "ANOTHER BUILD")]
    [InlineData("build", NetRefusal.BuildMismatch, "ANOTHER BUILD")]
    [InlineData("iwad", NetRefusal.IwadMismatch, "ANOTHER IWAD (DOOM2.WAD)")]
    [InlineData("pwads", NetRefusal.PwadMismatch, "OTHER PWADS")]
    [InlineData("pwadorder", NetRefusal.PwadMismatch, "OTHER PWADS")]
    [InlineData("started", NetRefusal.GameInProgress, "STARTED")]
    [InlineData("full", NetRefusal.GameFull, "FULL")]
    [InlineData("none", null, null)]
    public void EachIncompatibilityHasItsReason(string what, NetRefusal? refusal, string? reason)
    {
        string a = "00112233445566778899aabbccddeeff", b = "ffeeddccbbaa99887766554433221100";
        var self = _identity with { PwadMd5s = [a, b] };
        NetDiscoveryReply reply = Reply(players: 3) with { PwadCount = 2, PwadHash = NetDiscovery.PwadHash([a, b]) };
        reply = what switch
        {
            "protocol" => reply with { Protocol = NetProtocol.Version - 1, Build = "other" },
            "build" => reply with { Build = "other" },
            "iwad" => reply with { IwadMd5 = new string('0', 32), IwadName = "doom2.wad", PwadCount = 0 },
            "pwads" => reply with { PwadCount = 1, PwadHash = NetDiscovery.PwadHash([a]) },
            "pwadorder" => reply with { PwadHash = NetDiscovery.PwadHash([b, a]), Started = true },
            "started" => reply with { Started = true, Players = 4 },
            "full" => reply with { Players = 4 },
            _ => reply,
        };
        Assert.Equal((refusal, reason), NetDiscovery.Compatibility(reply, self));
    }

    [Fact]
    public void TheServersHelloAndTheBrowserShareTheOrder()
    {
        // the hello's checks, in its order (NetServer.Hello's), with the full game last
        Assert.Equal(NetRefusal.ProtocolMismatch, NetIdentity.Refusal(1, "x", "y", false, true, true, _identity));
        Assert.Equal(NetRefusal.BuildMismatch, NetIdentity.Refusal(NetProtocol.Version, "x", "y", false, true, true, _identity));
        Assert.Equal(NetRefusal.IwadMismatch, NetIdentity.Refusal(NetProtocol.Version, "test", "y", false, true, true, _identity));
        Assert.Equal(NetRefusal.PwadMismatch, NetIdentity.Refusal(NetProtocol.Version, "test", _identity.IwadMd5.ToUpperInvariant(), false, true, true, _identity));
        Assert.Equal(NetRefusal.GameInProgress, NetIdentity.Refusal(NetProtocol.Version, "test", _identity.IwadMd5, true, true, true, _identity));
        Assert.Equal(NetRefusal.GameFull, NetIdentity.Refusal(NetProtocol.Version, "test", _identity.IwadMd5, true, false, true, _identity));
        Assert.Null(NetIdentity.Refusal(NetProtocol.Version, "test", _identity.IwadMd5, true, false, false, _identity));
    }

    [Fact]
    public void AResponderAnswersABrowsersQuery()
    {
        using var responder = new NetDiscoveryResponder(IPAddress.Loopback, 0, () => Reply());
        using var browser = new NetDiscoveryBrowser(_identity, At(responder));
        Assert.NotEqual(NetDiscovery.Port, responder.Port);
        Assert.True(Pump(() => browser.Entries.Count == 1, 0, responder.Update, browser.Update));
        NetDiscoveryEntry entry = browser.Entries[0];
        Assert.Equal(("JOEP", "127.0.0.1:5029", true, null), (entry.Reply.HostName, entry.JoinAddress, entry.Compatible, entry.Reason));
        Assert.Equal(1, responder.Answered);
        Assert.True(browser.QueriesSent >= 1);
    }

    [Fact]
    public void TwoRespondersAreListedByName()
    {
        using var zed = new NetDiscoveryResponder(IPAddress.Loopback, 0, () => Reply(name: "ZED", instance: 1));
        using var ann = new NetDiscoveryResponder(IPAddress.Loopback, 0, () => Reply(name: "ANN", instance: 2, started: true));
        using var browser = new NetDiscoveryBrowser(_identity, At(zed, ann));
        Assert.True(Pump(() => browser.Entries.Count == 2, 0, zed.Update, ann.Update, browser.Update));
        Assert.Equal(["ANN", "ZED"], browser.Entries.Select(e => e.Reply.HostName));
        Assert.Equal("STARTED", browser.Entries[0].Reason);
        Assert.True(browser.Entries[1].Compatible);
    }

    [Fact]
    public void AHostNotHeardFromExpiresAndAChangeShows()
    {
        int players = 1;
        using var responder = new NetDiscoveryResponder(IPAddress.Loopback, 0, () => Reply(players: players));
        using var browser = new NetDiscoveryBrowser(_identity, At(responder));
        Assert.True(Pump(() => browser.Entries.Count == 1, 0, responder.Update, browser.Update));
        int changes = browser.Changes;

        // the next query (1.5 s on) sees the second player
        players = 2;
        long t = NetDiscovery.QueryIntervalUs;
        Assert.True(Pump(() => browser.Entries.Count == 1 && browser.Entries[0].Reply.Players == 2, t, responder.Update, browser.Update));
        Assert.True(browser.Changes > changes);

        // the host gone: kept until 5 s after its last reply, then dropped
        responder.Dispose();
        browser.Update(t + NetDiscovery.ExpireUs - 1);
        Assert.Single(browser.Entries);
        browser.Update(t + NetDiscovery.ExpireUs);
        Assert.Empty(browser.Entries);
    }

    [Fact]
    public void AReplyToAnotherBrowsersNonceIsIgnored()
    {
        using var responder = new NetDiscoveryResponder(IPAddress.Loopback, 0, () => Reply());
        using var browser = new NetDiscoveryBrowser(_identity, []); // queries nobody
        using var other = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        other.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        // a reply sent to the browser's port unasked: another nonce
        other.SendTo(NetDiscovery.EncodeReply(Reply() with { Nonce = 1 }), new IPEndPoint(IPAddress.Loopback, browser.Port));
        Thread.Sleep(50);
        browser.Update(0);
        Assert.Empty(browser.Entries);
    }

    [Fact]
    public void AResponderAnswersOneSourceAFewTimesASecond()
    {
        using var responder = new NetDiscoveryResponder(IPAddress.Loopback, 0, () => Reply());
        using var asker = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        asker.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var to = new IPEndPoint(IPAddress.Loopback, responder.Port);
        for (int i = 0; i < 10; i++)
            asker.SendTo(NetDiscovery.EncodeQuery((uint)i), to);
        Assert.True(Pump(() => responder.Answered + responder.Limited == 10, 0, responder.Update));
        Assert.Equal((NetDiscovery.RepliesPerSecond, 10 - NetDiscovery.RepliesPerSecond), (responder.Answered, responder.Limited));

        // a second later it answers again; junk is never answered
        asker.SendTo(new byte[NetDiscovery.QuerySize], to);
        asker.SendTo(NetDiscovery.EncodeQuery(99), to);
        Assert.True(Pump(() => responder.Answered == NetDiscovery.RepliesPerSecond + 1, NetProtocol.MicrosecondsPerSecond, responder.Update));
        Thread.Sleep(20);
        responder.Update(NetProtocol.MicrosecondsPerSecond);
        Assert.Equal(NetDiscovery.RepliesPerSecond + 1, responder.Answered);
    }

    [Fact]
    public void ATakenPortThrowsAndTheHostsSessionGoesOnUnseen()
    {
        using var taken = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        taken.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)taken.LocalEndPoint!).Port;
        Assert.Throws<SocketException>(() => new NetDiscoveryResponder(IPAddress.Loopback, port, () => Reply()));

        var network = new MemoryNetwork();
        var settings = new NetGameSettings(new SpawnSettings(GameMode.shareware, skill_t.sk_medium, netgame: true), Tweaks.TopDown, 1, 1);
        var logs = new List<string>();
        using var host = new NetSession(NetOptions.Parse(["-host", "-bind", "127.0.0.1"])! with { PlayerName = "joep" }, _identity, settings,
            use => network.CreateEndpoint(use == NetTransportUse.Server ? "127.0.0.1" : "host"), logs.Add);
        host.AnswerDiscovery("DOOM1.WAD", port);
        Assert.Null(host.Discovery);
        Assert.NotNull(host.DiscoveryError);
        Assert.Contains(logs, l => l.StartsWith($"not visible on the LAN: port {port}", StringComparison.Ordinal));
        host.Update(0); // the session goes on
    }

    [Fact]
    public void AHostsSessionAnswersWithItsGame()
    {
        var network = new MemoryNetwork();
        var settings = new NetGameSettings(new SpawnSettings(GameMode.shareware, skill_t.sk_hard, netgame: true, deathmatch: 1), Tweaks.TopDown, 1, 3);
        using var host = new NetSession(NetOptions.Parse(["-host", "-bind", "127.0.0.1", "-players", "3"])! with { PlayerName = "joep" }, _identity, settings,
            use => network.CreateEndpoint(use == NetTransportUse.Server ? "127.0.0.1" : "host"));
        host.AnswerDiscovery("DOOM1.WAD", 0);
        Assert.NotNull(host.Discovery);
        using var browser = new NetDiscoveryBrowser(_identity, [new IPEndPoint(IPAddress.Loopback, host.Discovery!.Port)]);
        long t = 0;
        Assert.True(Pump(() => browser.Entries.Count == 1 && browser.Entries[0].Reply.Players == 1, 0, _ => host.Update(t += 10_000), _ => browser.Update(t)));
        NetDiscoveryReply r = browser.Entries[0].Reply;
        Assert.Equal(("JOEP", "DOOM1.WAD", "E1M3", 1, 4, 1, 3, false, host.Port), (r.HostName, r.IwadName, r.MapName, r.Deathmatch, r.Skill, r.Players, r.MaxPlayers, r.Started, r.GamePort));
        Assert.True(browser.Entries[0].Compatible);

        // another IWAD's machine sees it greyed
        using var other = new NetDiscoveryBrowser(_identity with { IwadMd5 = new string('0', 32) }, [new IPEndPoint(IPAddress.Loopback, host.Discovery.Port)]);
        Assert.True(Pump(() => other.Entries.Count == 1, 0, _ => host.Update(t += 10_000), _ => other.Update(t)));
        Assert.Equal("ANOTHER IWAD (DOOM1.WAD)", other.Entries[0].Reason);
    }

    [Fact]
    public void TheHostsAddressesAreItsLanOnesOrLoopback()
    {
        Assert.Equal(["127.0.0.1:5029"], NetSession.HostAddresses("127.0.0.1", 5029));
        Assert.Equal(["[::1]:5029"], NetSession.HostAddresses("::1", 5029));
        List<string> all = NetSession.HostAddresses("*", 5030);
        Assert.InRange(all.Count, 1, 2);
        Assert.All(all, a => Assert.EndsWith(":5030", a, StringComparison.Ordinal));
        List<(IPAddress Address, IPAddress Broadcast)> lan = NetDiscovery.LanAddresses();
        if (lan.Count == 0)
            Assert.Equal(["127.0.0.1:5030"], all);
        Assert.All(lan, l => Assert.False(IPAddress.IsLoopback(l.Address) || NetDiscovery.IsLinkLocal(l.Address)));
        List<IPEndPoint> targets = NetDiscovery.DefaultTargets(4000);
        Assert.Equal(new IPEndPoint(IPAddress.Broadcast, 4000), targets[0]);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 4000), targets[^1]);
    }
}
