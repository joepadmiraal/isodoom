using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using IsoDoom.Net;
using IsoDoom.Sim;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Net;

/// <summary>T11.5: the messages' encoding, and the in-memory transport's channels.</summary>
public class NetMessagesTests
{
    [Fact]
    public void The_protocol_has_vanillas_players()
        => Assert.Equal(World.MAXPLAYERS, NetProtocol.MaxPlayers);

    /// <summary>A value of <paramref name="type"/> other than its default, or null if the test does not know the type.</summary>
    private static object? NonDefault(Type type) => type switch
    {
        _ when type == typeof(bool) => true,
        _ when type == typeof(int) => 2,
        _ when type == typeof(uint) => 0x12345678u,
        // The second value: neither 0 nor a "none" at the end.
        _ when type.IsEnum => Enum.GetValues(type).GetValue(1),
        _ when Nullable.GetUnderlyingType(type) is Type inner && inner.IsEnum => Enum.GetValues(inner).GetValue(1),
        _ => null,
    };

    [Fact]
    public void Every_spawn_setting_and_tweak_travels()
    {
        // Each constructor parameter of SpawnSettings and each settable
        // property of Tweaks, set away from its default, must come back: a
        // setting added to either without the protocol fails here.
        ConstructorInfo ctor = typeof(SpawnSettings).GetConstructors().Single(c => c.GetParameters().Length > 2);
        object?[] args = [.. ctor.GetParameters().Select(p =>
            NonDefault(p.ParameterType) ?? throw new InvalidOperationException($"SpawnSettings.{p.Name}: add its type to the protocol and to this test"))];
        args[3] = 1; // deathmatch: 0–2
        args[Array.FindIndex(ctor.GetParameters(), p => p.Name == nameof(SpawnSettings.playersingame))] = 0b1101; // the Start's mask
        var spawn = (SpawnSettings)ctor.Invoke(args);

        var tweaks = new Tweaks();
        foreach (PropertyInfo property in typeof(Tweaks).GetProperties().Where(p => p.SetMethod is not null))
        {
            object value = NonDefault(property.PropertyType) ?? throw new InvalidOperationException($"Tweaks.{property.Name}: add its type to the protocol and to this test");
            property.SetValue(tweaks, value);
        }
        Assert.NotEqual(new Tweaks(), tweaks);

        var settings = new NetGameSettings(spawn, tweaks, Episode: 3, Map: 9);
        var start = new NetStart(2, 0b1101, 12345, 7, settings);
        NetStart back = NetMessages.DecodeStart(Reader(NetMessages.EncodeStart(start), NetMessageType.Start));
        Assert.Equal(start, back);
        Assert.Equal(spawn, back.Settings.Spawn);
        Assert.Equal(tweaks, back.Settings.Tweaks);
    }

    [Fact]
    public void A_start_whose_settings_players_differ_from_its_mask_is_refused()
    {
        // T11.5a: the Start's mask and its settings' playersingame are one fact.
        var settings = new NetGameSettings(new SpawnSettings(GameMode.shareware, skill_t.sk_medium, netgame: true, playersingame: 0b0011), Tweaks.Vanilla, 1, 1);
        byte[] good = NetMessages.EncodeStart(new NetStart(1, 0b0011, 0, 2, settings));
        Assert.Equal(0b0011, NetMessages.DecodeStart(Reader(good, NetMessageType.Start)).PlayerMask);
        byte[] bad = NetMessages.EncodeStart(new NetStart(1, 0b0111, 0, 2, settings));
        Assert.Throws<NetProtocolException>(() => NetMessages.DecodeStart(Reader(bad, NetMessageType.Start)));
    }

    [Fact]
    public void The_default_game_version_travels_as_none()
    {
        var settings = new NetGameSettings(new SpawnSettings(GameMode.retail, skill_t.sk_baby), Tweaks.Vanilla, 4, 1);
        var r = Reader(NetMessages.EncodeSettings(settings), NetMessageType.Settings);
        Assert.Equal(settings, r.Settings());
        r.End();
    }

    private static NetReader Reader(byte[] message, NetMessageType type)
    {
        var r = new NetReader(message);
        Assert.Equal(type, r.Type);
        return r;
    }

    [Fact]
    public void Commands_and_bundles_keep_every_field()
    {
        ticcmd_t[] cmds =
        [
            new() { forwardmove = -128, sidemove = 127, angleturn = short.MinValue, consistancy = short.MaxValue, chatchar = 255, buttons = 0x81 },
            new() { forwardmove = 50, sidemove = -40, angleturn = 1234, consistancy = -2, chatchar = 0, buttons = buttoncode_t.BT_ATTACK },
        ];
        byte[] message = NetMessages.EncodeCommands(1_000_000, cmds);
        Assert.Equal(1 + 4 + 1 + 2 * NetProtocol.TiccmdSize, message.Length);
        var into = new List<ticcmd_t>();
        Assert.Equal(1_000_000, NetMessages.DecodeCommands(Reader(message, NetMessageType.Commands), into));
        Assert.Equal(cmds, into);

        // A bundle of two tics: players 0 and 3, then 1 alone.
        var all = new ticcmd_t[2 * NetProtocol.MaxPlayers];
        all[0] = cmds[0];
        all[3] = cmds[1];
        all[NetProtocol.MaxPlayers + 1] = cmds[1];
        byte[] bundle = NetMessages.EncodeBundle(77, 2, [0b1001, 0b0010], all);
        Assert.Equal(1 + 4 + 1 + 2 + 3 * NetProtocol.TiccmdSize, bundle.Length);
        var r = Reader(bundle, NetMessageType.Bundle);
        Assert.Equal(77, r.I32());
        Assert.Equal(2, r.U8());
        Assert.Equal(0b1001, r.U8());
        Assert.Equal(cmds[0], r.Ticcmd());
        Assert.Equal(cmds[1], r.Ticcmd());
        Assert.Equal(0b0010, r.U8());
        Assert.Equal(cmds[1], r.Ticcmd());
        r.End();
    }

    [Fact]
    public void Hellos_and_checksums_round_trip()
    {
        var identity = new NetIdentity("0.2.0-rc1", "F0CEFCA49926D00903CF57551D901ABE", ["0123456789abcdef0123456789abcdef"]);
        NetHello hello = NetMessages.DecodeHello(Reader(NetMessages.EncodeHello(identity, 0xfeedface), NetMessageType.Hello));
        Assert.Equal(NetProtocol.Version, hello.Protocol);
        Assert.Equal(0xfeedfaceUL, hello.HostToken);
        Assert.Equal("0.2.0-rc1", hello.Identity!.Build);
        Assert.Equal("f0cefca49926d00903cf57551d901abe", hello.Identity.IwadMd5);
        Assert.True(hello.Identity.SamePwads(identity));
        Assert.Equal("", hello.Name);
        Assert.Equal("Joep 🎮", NetMessages.DecodeHello(Reader(NetMessages.EncodeHello(identity, 1, "Joep 🎮"), NetMessageType.Hello)).Name);

        var report = new NetChecksumReport(41, 0xdeadbeefcafef00d, [1, -2, 3, short.MinValue], 1440);
        NetChecksumReport back = NetMessages.DecodeChecksum(Reader(NetMessages.EncodeChecksum(report), NetMessageType.Checksum));
        Assert.Equal(report.Window, back.Window);
        Assert.Equal(report.FirstBadTic, back.FirstBadTic);
        Assert.True(report.SameState(back));
    }

    [Fact]
    public void Lobbies_carry_the_occupied_slots_names_in_slot_order()
    {
        byte[] lobby = NetMessages.EncodeLobby(0b1010, 0b0010, ["ignored", "JOEP", "", "PLAYER 4"]);
        NetLobby back = NetMessages.DecodeLobby(Reader(lobby, NetMessageType.Lobby));
        Assert.Equal(0b1010, back.Occupied);
        Assert.Equal(0b0010, back.Ready);
        Assert.Equal(["", "JOEP", "", "PLAYER 4"], back.Names);
        // a name missing for an occupied slot is malformed
        Assert.Throws<NetProtocolException>(() => NetMessages.DecodeLobby(Reader(NetMessages.EncodeLobby(0b0011, 0, ["A", "", "", ""])[..^1], NetMessageType.Lobby)));
    }

    [Theory]
    [InlineData("joep", 0, "JOEP")]
    [InlineData("  a very long player name  ", 1, "A VERY LONG PLA")]
    [InlineData("Zoë {x}", 2, "ZO? ?X?")]
    [InlineData("🎮🎮", 0, "??")]
    [InlineData("", 1, "PLAYER 2")]
    [InlineData("   ", 3, "PLAYER 4")]
    [InlineData("12345678901234 X", 0, "12345678901234")]
    public void The_server_cleans_names(string name, int slot, string expected) =>
        Assert.Equal(expected, NetProtocol.CleanName(name, slot));

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { (byte)NetMessageType.Commands, 0, 0, 0 })]
    [InlineData(new byte[] { (byte)NetMessageType.Commands, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { (byte)NetMessageType.Settings, 9, 0, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0 })]
    public void Malformed_messages_throw(byte[] message)
    {
        Assert.Throws<NetProtocolException>(() =>
        {
            var r = new NetReader(message);
            if (r.Type == NetMessageType.Commands)
                NetMessages.DecodeCommands(r, []);
            else
                r.Settings();
        });
    }

    [Fact]
    public void The_reliable_channel_keeps_order_under_jitter_and_loss_and_the_unreliable_one_loses()
    {
        var network = new MemoryNetwork();
        MemoryTransport a = network.CreateEndpoint("a", new MemoryLinkOptions { LatencyUs = 20_000, JitterUs = 30_000, ReliableLoss = 0.2, UnreliableLoss = 0.25 }, seed: 1);
        MemoryTransport b = network.CreateEndpoint("b", new MemoryLinkOptions { LatencyUs = 5_000, JitterUs = 10_000 }, seed: 2);
        b.Listen(1);
        int peer = a.Connect("b", 1);
        var reliable = new List<int>();
        var unreliable = new List<int>();
        bool connected = false;
        for (int i = 0; i < 2000; i++)
        {
            if (connected)
            {
                a.Send(peer, BitConverter.GetBytes(i), NetChannel.Reliable);
                a.Send(peer, BitConverter.GetBytes(i), NetChannel.Unreliable);
            }
            network.Advance(1000);
            while (a.Poll(out NetEvent e))
                connected |= e.Kind == NetEventKind.Connected;
            while (b.Poll(out NetEvent e))
            {
                if (e.Kind == NetEventKind.Data)
                    (e.Channel == NetChannel.Reliable ? reliable : unreliable).Add(BitConverter.ToInt32(e.Data.Span));
            }
        }
        network.Advance(10_000_000);
        while (b.Poll(out NetEvent e))
        {
            if (e.Kind == NetEventKind.Data)
                (e.Channel == NetChannel.Reliable ? reliable : unreliable).Add(BitConverter.ToInt32(e.Data.Span));
        }
        Assert.True(connected);
        int first = reliable[0];
        Assert.Equal(Enumerable.Range(first, 2000 - first), reliable);
        Assert.InRange(unreliable.Count, (2000 - first) * 0.65, (2000 - first) * 0.85);
        Assert.False(unreliable.SequenceEqual(unreliable.Order()), "jitter should reorder some unreliable packets");
    }
}
