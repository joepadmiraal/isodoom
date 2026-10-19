using System;
using System.Collections.Generic;
using IsoDoom.Net;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Net;

/// <summary>One player of a <see cref="LockstepHarness"/>: its link and clock, and what it ran.</summary>
public sealed class HarnessPlayer
{
    internal HarnessPlayer(int index, NetClient client, MemoryTransport transport, PlayerSetup setup)
    {
        Index = index;
        Client = client;
        Transport = transport;
        Setup = setup;
    }

    /// <summary>Its index in the harness (0 the host).</summary>
    public int Index { get; }

    public NetClient Client { get; }

    public MemoryTransport Transport { get; }

    public PlayerSetup Setup { get; }

    /// <summary>Its world, made from the settings its Start carried (null for protocol-only runs).</summary>
    public World? World { get; internal set; }

    /// <summary>The tics it ran.</summary>
    public int TicsRun { get; internal set; }

    /// <summary>The most commands it had sent past its last tic run (its lead).</summary>
    public int MaxLead { get; internal set; }

    /// <summary>The most tics it ran in one frame.</summary>
    public int MaxTicsPerFrame { get; internal set; }

    /// <summary>The commands it sent, by tic (consistancy left out).</summary>
    internal Dictionary<int, ticcmd_t> Sent { get; } = [];

    /// <summary>T11.12: its clock's time when it made each of its commands, by tic.</summary>
    internal Dictionary<int, long> SentAtUs { get; } = [];

    /// <summary>
    /// T11.12: the input delay felt, per command of its own: from the frame it
    /// made the command (its input read) to the frame it ran that command's
    /// tic, in microseconds of its clock.
    /// </summary>
    public List<long> InputLagUs { get; } = [];

    /// <summary>
    /// T11.12d: per command of its own, from the frame it made the command to
    /// the frame its tic's bundle was in (every player's command for it at the
    /// server and the bundle back), in µs of its clock: the delay the game
    /// needs, below which the tic would wait (<see cref="InputLagUs"/> is the
    /// delay it plays at).
    /// </summary>
    public List<long> BundleWaitUs { get; } = [];

    /// <summary>T11.12d: the last tic whose bundle wait was noted.</summary>
    internal int BundleNoted { get; set; } = int.MinValue;

    /// <summary>T11.12: the tics it ran after their time (the clock already past them: a bundle came late).</summary>
    public int LateTics { get; internal set; }

    /// <summary>
    /// T11.12e: the stalls it saw (its game clock past the last bundle, the
    /// world held: <see cref="NetClient.LateUs"/>), each from when it began, in
    /// its clock's µs after its first frame in the game, and how long it held,
    /// to the frame the bundle came (a frame's resolution).
    /// </summary>
    public List<(long AtUs, long LengthUs)> Stalls { get; } = [];

    /// <summary>T11.12e: its clock at its first frame in the game (−1 before).</summary>
    public long GameStartUs { get; internal set; } = -1;

    /// <summary>T11.12e: its clock at its last frame.</summary>
    public long LastFrameUs { get; internal set; }

    /// <summary>T11.12e: when the stall under way began (its clock), −1 for none.</summary>
    public long StallStartUs { get; internal set; } = -1;

    /// <summary>T11.11: its machine is gone without a word (killed): it runs no frames, sends nothing, quits nothing.</summary>
    public bool Down { get; set; }

    /// <summary>T11.11: its game hangs but its network goes on: it updates (pings) and runs no tics and sends no commands.</summary>
    public bool Mute { get; set; }

    /// <summary>T11.11: the players who left the game in its world, with the tic they left before (<see cref="World.PlayerQuitGame"/>).</summary>
    public List<(int Player, int Tic)> Quits { get; } = [];
}

/// <summary>How a <see cref="LockstepHarness"/> player's machine behaves.</summary>
public sealed record PlayerSetup
{
    /// <summary>Its link (latency is one way; the host's server end adds none).</summary>
    public MemoryLinkOptions Link { get; init; } = MemoryLinkOptions.Perfect;

    /// <summary>Its clock's speed against real time (1.01: 1 % fast).</summary>
    public double ClockSkew { get; init; } = 1;

    /// <summary>Its frame time, in microseconds.</summary>
    public long FrameUs { get; init; } = 16_667;

    /// <summary>Its first frame's offset, in microseconds.</summary>
    public long PhaseUs { get; init; }

    /// <summary>Its identity, if not the host's (refusal tests).</summary>
    public NetIdentity? Identity { get; init; }
}

/// <summary>
/// T11.5's test bed: a <see cref="NetServer"/> and its players' <see cref="NetClient"/>s
/// over a <see cref="MemoryNetwork"/>, each player on its own clock and frame
/// rate, feeding scripted commands and running the bundles it takes in its
/// own <see cref="World"/> (or, protocol-only, a hash of the bundles). It
/// checks, every tic each player runs, that the tic is the next one, its
/// bundle went out from the server and its clock reached it, the bundle
/// carries exactly what each player sent for it, and every player's checksum
/// of it is the same (unless a test desyncs one on purpose).
/// </summary>
public sealed class LockstepHarness
{
    public const int Port = 5029;

    public static readonly NetIdentity Identity = new("test", "f0cefca49926d00903cf57551d901abe", []);

    private readonly Dictionary<int, ulong> _checksums = [];
    private readonly ticcmd_t[] _cmds = new ticcmd_t[NetProtocol.MaxPlayers];
    private readonly short[] _consistancy = new short[NetProtocol.MaxPlayers];
    private readonly Func<int, int, ticcmd_t> _script;
    private readonly Func<NetStart, World?>? _makeWorld;
    private readonly Dictionary<HarnessPlayer, long> _nextFrame = [];

    /// <summary>
    /// A server and a player for each of <paramref name="setups"/> (the first
    /// the host's own client), joined and ready in the lobby.
    /// </summary>
    /// <param name="settings">The game's settings.</param>
    /// <param name="setups">The players' machines.</param>
    /// <param name="script">The command player (slot) gives for a tic.</param>
    /// <param name="makeWorld">Makes a player's world from its Start; null runs no worlds.</param>
    /// <param name="seed">Seeds the network's jitter and loss.</param>
    public LockstepHarness(NetGameSettings settings, IReadOnlyList<PlayerSetup> setups, Func<int, int, ticcmd_t> script,
        Func<NetStart, World?>? makeWorld = null, int seed = 1)
    {
        _script = script;
        _makeWorld = makeWorld;
        Network = new MemoryNetwork();
        ServerTransport = Network.CreateEndpoint("host", MemoryLinkOptions.Perfect, seed);
        Server = new NetServer(ServerTransport, Identity, settings, hostToken: 0x1234_5678_9abc_def0);
        Server.Log += line => ServerLog.Add(line);
        Server.Listen(Port);
        for (int i = 0; i < setups.Count; i++)
        {
            PlayerSetup setup = setups[i];
            MemoryTransport end = Network.CreateEndpoint($"player{i}", setup.Link, seed * 31 + i + 1);
            var client = new NetClient(end, setup.Identity ?? Identity, i == 0 ? Server.HostToken : 0);
            var player = new HarnessPlayer(i, client, end, setup);
            Players.Add(player);
            _nextFrame[player] = setup.PhaseUs;
            client.Connect("host", Port);
        }
    }

    public MemoryNetwork Network { get; }

    public NetServer Server { get; }

    /// <summary>The server's end of the network.</summary>
    public MemoryTransport ServerTransport { get; }

    public List<HarnessPlayer> Players { get; } = [];

    public List<string> ServerLog { get; } = [];

    /// <summary>Whether a player's checksums may differ (a desync test).</summary>
    public bool AllowDesync { get; set; }

    /// <summary>A player whose world takes one extra <c>P_Random</c> before a tic (a forced desync).</summary>
    public (int Player, int Tic)? BumpRandom { get; set; }

    /// <summary>Runs on a player's world before each tic (player, tic): a test's own perturbation.</summary>
    public Action<HarnessPlayer, int>? BeforeTic { get; set; }

    /// <summary>The host's machine is gone: neither its server nor its client runs frames.</summary>
    public bool HostDown { get; set; }

    /// <summary>
    /// T11.12d: each machine's frame polls its transport again after its
    /// commands are flushed and before it takes its tics (the host's server
    /// first), as the game's frame does (<c>NetSession.Service</c>); false is
    /// the frame before T11.12d, polled once at its start.
    /// </summary>
    public bool PollAfterFlush { get; set; } = true;

    /// <summary>
    /// T11.12d's measurement of a network thread: the server polled and its
    /// bundles sent every <see cref="StepUs"/> (a thread of its own servicing
    /// ENet every millisecond), not only in the host's frames.
    /// </summary>
    public bool ServerEveryStep { get; set; }

    /// <summary>Every frame's step of the network, in microseconds.</summary>
    public long StepUs { get; set; } = 1_000;

    /// <summary>Run until <paramref name="until"/> holds, or fail after <paramref name="maxUs"/> of network time.</summary>
    public void RunUntil(Func<bool> until, long maxUs, string what)
    {
        long end = Network.NowUs + maxUs;
        while (!until())
        {
            Assert.True(Network.NowUs < end, $"{what}: not reached after {maxUs / 1000} ms (server log: {string.Join("; ", ServerLog)})");
            Step();
        }
    }

    /// <summary>Gather everyone in the lobby, ready.</summary>
    public void JoinAndReady()
    {
        RunUntil(() => Players.TrueForAll(p => p.Client.State == NetClientState.Lobby), 5_000_000, "everyone in the lobby");
        foreach (HarnessPlayer p in Players)
            p.Client.SetReady(true);
        RunUntil(() => Server.CanStart && Server.PlayerCount == Players.Count, 5_000_000, "everyone ready");
        // A second of pings, so the clients know their round trips.
        long t = Network.NowUs + 1_000_000;
        RunUntil(() => Network.NowUs >= t, 2_000_000, "pings");
    }

    /// <summary>Start the game and run until every player still in it ran <paramref name="tics"/> tics.</summary>
    public void Play(int delay, int tics, long maxUs)
    {
        Server.Start(delay);
        RunUntil(() => Players.TrueForAll(p => p.Client.State == NetClientState.InGame), 5_000_000, "everyone in the game");
        // T11.12e: a player who ran its tics waits for the others, so every world ends on the same tic
        _ticLimit = tics;
        try
        {
            RunUntil(() => Players.TrueForAll(p => p.Client.State != NetClientState.InGame || p.TicsRun >= tics), maxUs, $"{tics} tics");
        }
        finally
        {
            _ticLimit = int.MaxValue;
        }
    }

    private int _ticLimit = int.MaxValue;

    /// <summary>One step of network time; each machine whose frame is due runs it.</summary>
    public void Step()
    {
        Network.Advance(StepUs);
        long now = Network.NowUs;
        if (ServerEveryStep && !HostDown)
            Server.Update(now);
        foreach (HarnessPlayer p in Players)
        {
            if (_nextFrame[p] > now || (HostDown && p.Index == 0) || p.Down)
                continue;
            _nextFrame[p] += p.Setup.FrameUs;
            if (p.Index == 0 && !ServerEveryStep)
                Server.Update(now);
            Frame(p, (long)(now * p.Setup.ClockSkew) + p.Setup.PhaseUs, now);
        }
    }

    private void Frame(HarnessPlayer p, long localUs, long now)
    {
        NetClient c = p.Client;
        if (c.State is NetClientState.Disconnected or NetClientState.Refused)
            return;
        NetClientState before = c.State;
        c.Update(localUs);
        NoteBundles(p, localUs);
        if (c.State != NetClientState.InGame)
            return;
        if (before != NetClientState.InGame)
        {
            p.GameStartUs = localUs;
            if (_makeWorld is not null)
                p.World = _makeWorld(c.Start!);
        }
        p.LastFrameUs = localUs;
        TrackStall(p, localUs);
        if (p.Mute)
            return;
        while (c.CommandsDue > 0)
        {
            ticcmd_t cmd = _script(c.Slot, c.MakeTic);
            p.Sent[c.MakeTic] = cmd;
            p.SentAtUs[c.MakeTic] = localUs;
            c.SendCommand(cmd);
        }
        c.Flush();
        p.MaxLead = Math.Max(p.MaxLead, c.MakeTic - 1 - c.LastRanTic);
        if (PollAfterFlush)
        {
            // T11.12d: as the game's frame (NetSession.Service): the server, then the client, again
            if (p.Index == 0 && !ServerEveryStep)
                Server.Update(now);
            c.Update(localUs);
            if (c.State != NetClientState.InGame)
                return;
            NoteBundles(p, localUs);
        }

        int ran = 0;
        while (p.TicsRun < _ticLimit && c.TryTakeTic(_cmds, out int tic, out byte mask))
        {
            Assert.Equal(c.Start!.StartTic + p.TicsRun, tic);
            Assert.True(tic <= Server.CompleteTic, $"player {p.Index} ran tic {tic} before its bundle went out (server at {Server.CompleteTic})");
            Assert.True(tic <= c.ClockTic, $"player {p.Index} ran tic {tic} before its clock reached it ({c.ClockTic})");
            for (int i = 0; i < NetProtocol.MaxPlayers; i++)
            {
                if ((mask & (1 << i)) == 0)
                    continue;
                ticcmd_t expected = tic < c.Start.StartTic + c.Start.Delay ? default : Players[SlotOwner(i)].Sent[tic];
                Assert.True(SameCommand(expected, _cmds[i]), $"tic {tic}: player {i}'s command in player {p.Index}'s bundle is not what it sent");
            }
            ulong checksum = RunTic(p, tic, mask);
            if (_checksums.TryGetValue(tic, out ulong other))
            {
                if (!AllowDesync)
                    Assert.True(other == checksum, $"tic {tic}: player {p.Index}'s checksum {checksum:x16} differs from {other:x16}");
            }
            else
                _checksums[tic] = checksum;
            c.TicRan(tic, checksum, _consistancy);
            if (p.SentAtUs.Remove(tic, out long madeUs))
                p.InputLagUs.Add(localUs - madeUs);
            if (c.ClockTic > tic)
                p.LateTics++;
            p.TicsRun++;
            ran++;
        }
        p.MaxTicsPerFrame = Math.Max(p.MaxTicsPerFrame, ran);
    }

    /// <summary>T11.12d: the bundle waits of the player's own commands whose bundles came since the last frame.</summary>
    private static void NoteBundles(HarnessPlayer p, long localUs)
    {
        NetClient c = p.Client;
        if (c.State != NetClientState.InGame)
            return;
        int from = Math.Max(p.BundleNoted + 1, c.Start!.StartTic);
        for (int t = from; t <= c.BundleTic; t++)
        {
            if (p.SentAtUs.TryGetValue(t, out long madeUs))
                p.BundleWaitUs.Add(localUs - madeUs);
        }
        p.BundleNoted = Math.Max(p.BundleNoted, c.BundleTic);
    }

    /// <summary>T11.12e: a stall begins where the client's began and ends at the frame its <see cref="NetClient.LateUs"/> is back to 0.</summary>
    private static void TrackStall(HarnessPlayer p, long localUs)
    {
        long late = p.Client.LateUs;
        if (late > 0)
        {
            if (p.StallStartUs < 0)
                p.StallStartUs = localUs - late;
        }
        else if (p.StallStartUs >= 0)
        {
            p.Stalls.Add((p.StallStartUs - p.GameStartUs, localUs - p.StallStartUs));
            p.StallStartUs = -1;
        }
    }

    private int SlotOwner(int slot) => Players.FindIndex(q => q.Client.Slot == slot);

    private static bool SameCommand(ticcmd_t a, ticcmd_t b)
        => a.forwardmove == b.forwardmove && a.sidemove == b.sidemove && a.angleturn == b.angleturn && a.buttons == b.buttons && a.chatchar == b.chatchar;

    /// <summary>Run <paramref name="tic"/> in the player's world (or hash its bundle) and fill <see cref="_consistancy"/>.</summary>
    private ulong RunTic(HarnessPlayer p, int tic, byte mask)
    {
        World? world = p.World;
        if (world is null)
        {
            // Protocol only: a hash of every bundle so far stands in for the world.
            ulong h = _protocolHash.GetValueOrDefault(p.Index, 14695981039346656037UL);
            if (BumpRandom is (int bumped, int bumpTic) && bumped == p.Index && bumpTic == tic)
                h ^= 0x5555;
            for (int i = 0; i < NetProtocol.MaxPlayers; i++)
            {
                h = (h ^ (ulong)(ushort)_cmds[i].angleturn ^ ((ulong)(byte)_cmds[i].forwardmove << 16) ^ ((ulong)_cmds[i].buttons << 24) ^ ((ulong)mask << 32)) * 1099511628211UL;
                _consistancy[i] = (mask & (1 << i)) != 0 ? (short)(h >> 48) : (short)0;
            }
            _protocolHash[p.Index] = h;
            return h;
        }
        if (BumpRandom is (int player, int at) && player == p.Index && at == tic)
            world.P_Random();
        BeforeTic?.Invoke(p, tic);
        // T11.11: Chocolate Doom d_net.c RunTic: a player in the game the bundle leaves out quits first
        for (int i = 0; i < NetProtocol.MaxPlayers; i++)
        {
            if (world.playeringame[i] && (mask & (1 << i)) == 0)
            {
                world.PlayerQuitGame(i);
                p.Quits.Add((i, tic));
            }
        }
        world.G_Ticker(_cmds);
        for (int i = 0; i < NetProtocol.MaxPlayers; i++)
            _consistancy[i] = Consistancy(world, i);
        return world.Checksum();
    }

    private readonly Dictionary<int, ulong> _protocolHash = [];

    /// <summary>g_game.c G_Ticker's consistancy: the player mobj's x, or the random index without one; 0 not in game.</summary>
    public static short Consistancy(World world, int player)
    {
        if (!world.playeringame[player])
            return 0;
        return unchecked((short)(world.players[player].mo is mobj_t mo ? mo.x : world.random.prndindex));
    }

    /// <summary>
    /// A player's world from <paramref name="start"/> on <paramref name="wad"/>:
    /// T11.1's <see cref="NetSim.MakeWorld"/> with the Start's settings (the
    /// players in its mask in game through <see cref="SpawnSettings.playersingame"/>,
    /// which <c>DecodeStart</c> holds equal to it) and its <c>consoleplayer</c>,
    /// no cheats: players die and reborn (T11.3, T11.5b).
    /// </summary>
    public static World MakeWorld(WadArchive wad, NetStart start)
    {
        NetGameSettings s = start.Settings;
        Assert.Equal(start.PlayerMask, s.Spawn.playersingame);
        string map = s.Spawn.gamemode == GameMode.commercial ? $"MAP{s.Map:00}" : $"E{s.Episode}M{s.Map}";
        return NetSim.MakeWorld(wad, map, s.Spawn, s.Tweaks, start.ConsolePlayer);
    }

    /// <summary>
    /// A scripted player: per 35-tic stretch, a walk direction and speed, a
    /// turn, now and then use (and with <paramref name="fire"/>, fire),
    /// different for each player and stretch (a hash, so every run is the same).
    /// </summary>
    public static ticcmd_t Wander(int slot, int tic, bool fire)
    {
        uint h = Hash((uint)slot, (uint)(tic / 35));
        var cmd = new ticcmd_t
        {
            forwardmove = (sbyte)((int)(h % 101) - 50),
            sidemove = (sbyte)((int)((h >> 8) % 81) - 40),
            angleturn = (short)((int)((h >> 16) % 1025) - 512),
        };
        uint b = Hash((uint)slot + 7, (uint)tic);
        if (b % 17 == 0)
            cmd.buttons |= buttoncode_t.BT_USE;
        if (fire && b % 5 == 0)
            cmd.buttons |= buttoncode_t.BT_ATTACK;
        return cmd;
    }

    private static uint Hash(uint a, uint b)
    {
        uint h = (a * 0x9E3779B1u) ^ (b * 0x85EBCA77u);
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        return h;
    }
}
