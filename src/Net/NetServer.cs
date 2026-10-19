using System;
using System.Collections.Generic;
using IsoDoom.Sim;

namespace IsoDoom.Net;

/// <summary>Where a <see cref="NetServer"/> is.</summary>
public enum NetServerPhase
{
    /// <summary>Players gather: hellos, slots, settings, ready.</summary>
    Lobby,

    /// <summary>The game runs: commands in, bundles out, checksums compared.</summary>
    InGame,
}

/// <summary>
/// The host's side of the lockstep protocol (T11.5; the protocol is described
/// at the top of <c>NetMessages.cs</c>): the lobby (slots 0–3, the host's own
/// client in slot 0), the game start, gathering every player's commands into
/// bundles broadcast in tic order, and comparing the checksum reports. After
/// Chocolate Doom's net_server.c. Driven by <see cref="Update"/>; no threads.
/// </summary>
public sealed class NetServer
{
    private readonly INetTransport _transport;
    private readonly NetIdentity _identity;
    private readonly int _maxPlayers;
    private readonly Dictionary<int, Peer> _peers = [];
    private readonly Peer?[] _slots = new Peer?[NetProtocol.MaxPlayers];
    private readonly List<ticcmd_t> _received = [];
    private readonly SortedDictionary<int, NetChecksumReport?[]> _reports = [];
    private long _nowUs;
    private long _startUs;
    private NetGameSettings _settings;

    // The game: per slot, the commands received (a ring of RingTics, each
    // entry's tic beside it: T11.12c, they may come out of order) and up to
    // which tic all of them came.
    private const int RingTics = NetProtocol.RingTics;
    private readonly ticcmd_t[,] _cmds = new ticcmd_t[NetProtocol.MaxPlayers, RingTics];
    private readonly int[,] _cmdTic = new int[NetProtocol.MaxPlayers, RingTics];
    private readonly int[] _receivedUpTo = new int[NetProtocol.MaxPlayers];

    // T11.12c: per slot, the last tic whose bundle the client acknowledged,
    // the last command ack sent to it, the last tic sent to it and when.
    private readonly int[] _bundleAck = new int[NetProtocol.MaxPlayers];
    private readonly int[] _ackSent = new int[NetProtocol.MaxPlayers];
    private readonly int[] _sentUpTo = new int[NetProtocol.MaxPlayers];
    private readonly long[] _lastSentUs = new long[NetProtocol.MaxPlayers];
    private readonly bool[] _heard = new bool[NetProtocol.MaxPlayers];
    private readonly int[] _quitTic = new int[NetProtocol.MaxPlayers];
    private readonly long[] _lastCommandUs = new long[NetProtocol.MaxPlayers];
    private readonly ushort[] _rttMs = new ushort[NetProtocol.MaxPlayers];
    private long _lastBundleUs;
    private byte _playerMask;
    private int _nextWindow;

    private sealed class Peer(int id)
    {
        public int Id { get; } = id;
        public int Slot { get; set; } = -1;
        public bool Ready { get; set; }
        public int RttUs { get; set; } = -1;
        public bool Gone { get; set; }

        /// <summary>T11.12a: the last <see cref="RttSamples"/> round trips it reported (a ring), µs.</summary>
        public int[] Rtts { get; } = new int[RttSamples];

        /// <summary>T11.12a: how many round trips it reported (the ring holds the last <see cref="RttSamples"/>).</summary>
        public int RttCount { get; set; }

        /// <summary>T11.9c: the player's name, cleaned (<see cref="NetProtocol.CleanName"/>).</summary>
        public string Name { get; set; } = "";

        /// <summary>T11.11: when the server last heard from it (any packet), µs.</summary>
        public long LastHeardUs { get; set; }
    }

    /// <summary>A server over <paramref name="transport"/> for machines sharing <paramref name="identity"/>.</summary>
    /// <param name="transport">The transport (listening is <see cref="Listen"/>).</param>
    /// <param name="identity">The host's build and WADs; a hello with others is refused.</param>
    /// <param name="settings">The game's settings, until <see cref="Settings"/> changes them.</param>
    /// <param name="hostToken">The secret the host's own client's hello carries to take slot 0 (nonzero).</param>
    /// <param name="maxPlayers">The slots open, 1–4.</param>
    public NetServer(INetTransport transport, NetIdentity identity, NetGameSettings settings, ulong hostToken, int maxPlayers = NetProtocol.MaxPlayers)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfZero(hostToken);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPlayers, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxPlayers, NetProtocol.MaxPlayers);
        _transport = transport;
        _identity = identity;
        _settings = settings;
        HostToken = hostToken;
        _maxPlayers = maxPlayers;
    }

    /// <summary>The token the host's own client passes to take slot 0.</summary>
    public ulong HostToken { get; }

    /// <summary>Lobby or game.</summary>
    public NetServerPhase Phase { get; private set; }

    /// <summary>The game's settings; a change in the lobby goes to every client.</summary>
    public NetGameSettings Settings
    {
        get => _settings;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (Phase != NetServerPhase.Lobby)
                throw new InvalidOperationException("The settings are fixed once the game starts.");
            _settings = value;
            Broadcast(NetMessages.EncodeSettings(value));
        }
    }

    /// <summary>The slots taken (bit i: slot i).</summary>
    public byte OccupiedMask
    {
        get
        {
            byte mask = 0;
            for (int i = 0; i < NetProtocol.MaxPlayers; i++)
            {
                if (_slots[i] is not null)
                    mask |= (byte)(1 << i);
            }
            return mask;
        }
    }

    /// <summary>The slots whose player is ready.</summary>
    public byte ReadyMask
    {
        get
        {
            byte mask = 0;
            for (int i = 0; i < NetProtocol.MaxPlayers; i++)
            {
                if (_slots[i] is { Ready: true })
                    mask |= (byte)(1 << i);
            }
            return mask;
        }
    }

    /// <summary>The players in the lobby.</summary>
    public int PlayerCount => System.Numerics.BitOperations.PopCount(OccupiedMask);

    /// <summary>Whether <see cref="Start"/> may run: in the lobby, the host's client in, every player ready.</summary>
    public bool CanStart => Phase == NetServerPhase.Lobby && _slots[0] is not null && OccupiedMask == ReadyMask;

    /// <summary>The game's first tic (after <see cref="Start"/>).</summary>
    public int StartTic { get; private set; }

    /// <summary>The input delay, in tics (after <see cref="Start"/>).</summary>
    public int Delay { get; private set; }

    /// <summary>The last tic whose bundle went out (<see cref="StartTic"/> − 1 before any).</summary>
    public int CompleteTic { get; private set; } = -1;

    /// <summary>The first desync found, once announced.</summary>
    public NetDesync? Desync { get; private set; }

    /// <summary>The windows compared and found equal.</summary>
    public int WindowsCompared => _nextWindow;

    /// <summary>
    /// T11.11: how long the server waits without a packet from a peer, or for
    /// the command of a player the next bundle waits for, before it drops it
    /// (µs; the host's own client in slot 0 is never dropped so).
    /// </summary>
    public long TimeoutUs { get; set; } = NetProtocol.DefaultTimeoutUs;

    /// <summary>
    /// T11.11: the players in game (bit i: slot i) whose command for the next
    /// tic (<see cref="CompleteTic"/> + 1) has not come: the ones the bundles
    /// wait for. 0 in the lobby and after a desync.
    /// </summary>
    public byte WaitingMask
    {
        get
        {
            if (Phase != NetServerPhase.InGame || Desync is not null)
                return 0;
            int tic = CompleteTic + 1;
            byte mask = MaskAt(tic), waiting = 0;
            for (int i = 0; i < NetProtocol.MaxPlayers; i++)
            {
                if ((mask & (1 << i)) != 0 && _receivedUpTo[i] < tic)
                    waiting |= (byte)(1 << i);
            }
            return waiting;
        }
    }

    /// <summary>A line for each lobby or game event (joins, refusals, quits, the desync), for the log.</summary>
    public event Action<string>? Log;

    /// <summary>T11.9c: the name of the player in <paramref name="slot"/> (cleaned), or <c>""</c> for an empty slot.</summary>
    public string NameOf(int slot) => _slots[slot]?.Name ?? "";

    /// <summary>The last round trip the client in <paramref name="slot"/> reported, in microseconds (−1 none yet).</summary>
    public int RttUsOf(int slot) => _slots[slot]?.RttUs ?? -1;

    /// <summary>
    /// T11.12a: how many of the clients' round trip reports the server keeps
    /// per slot (a ping's every <see cref="NetClient.PingIntervalUs"/>: the
    /// last 6.4 s), for the game's default delay (its <c>NetOptions.ChooseDelay</c>).
    /// </summary>
    public const int RttSamples = 64;

    /// <summary>
    /// T11.12a: the round trips the client in <paramref name="slot"/> reported
    /// lately (the last <see cref="RttSamples"/> at most, oldest first), µs; empty for none.
    /// </summary>
    public int[] RttSamplesOf(int slot)
    {
        if (_slots[slot] is not { } p)
            return [];
        int n = Math.Min(p.RttCount, RttSamples);
        var samples = new int[n];
        for (int i = 0; i < n; i++)
            samples[i] = p.Rtts[(p.RttCount - n + i) % RttSamples];
        return samples;
    }

    /// <summary>Accept connections on <paramref name="port"/>.</summary>
    public void Listen(int port) => _transport.Listen(port);

    /// <summary>
    /// Start the game: every player in the lobby is in it, with the input
    /// delay <paramref name="delay"/> and the first tic <paramref name="startTic"/>.
    /// Tics <paramref name="startTic"/> to <paramref name="startTic"/> +
    /// <paramref name="delay"/> − 1 are empty commands, bundled at once.
    /// </summary>
    public void Start(int delay, int startTic = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(delay, NetProtocol.MaxDelay);
        if (!CanStart)
            throw new InvalidOperationException("Not every player is ready.");
        Phase = NetServerPhase.InGame;
        StartTic = startTic;
        Delay = delay;
        CompleteTic = startTic - 1;
        _startUs = _nowUs;
        _lastBundleUs = _nowUs;
        _playerMask = OccupiedMask;
        // The players in game are the Start's mask (T11.5a: one source of truth).
        _settings = _settings with { Spawn = _settings.Spawn with { playersingame = _playerMask } };
        for (int i = 0; i < NetProtocol.MaxPlayers; i++)
        {
            _quitTic[i] = int.MaxValue;
            _lastCommandUs[i] = _nowUs;
            _receivedUpTo[i] = startTic + delay - 1;
            _bundleAck[i] = startTic - 1;
            _ackSent[i] = int.MinValue;
            _sentUpTo[i] = startTic - 1;
            _lastSentUs[i] = long.MinValue;
            _heard[i] = false;
            for (int t = 0; t < RingTics; t++)
                _cmdTic[i, t] = int.MinValue;
            for (int t = startTic; t < startTic + delay; t++)
            {
                _cmds[i, Ring(t)] = default;
                _cmdTic[i, Ring(t)] = t;
            }
        }
        for (int i = 0; i < NetProtocol.MaxPlayers; i++)
        {
            if (_slots[i] is Peer peer)
                Send(peer, NetMessages.EncodeStart(new NetStart(i, _playerMask, startTic, delay, _settings)));
        }
        Log?.Invoke($"start: players {_playerMask:x}, tic {startTic}, delay {delay}");
        CompleteTics();
        SendTics();
    }

    /// <summary>A tic's place in the rings (negative tics too).</summary>
    private static int Ring(int tic) => (int)((uint)tic % RingTics);

    /// <summary>Poll the transport and handle what came, at time <paramref name="nowUs"/> (µs, the host's clock).</summary>
    public void Update(long nowUs)
    {
        _nowUs = nowUs;
        while (_transport.Poll(out NetEvent e))
        {
            switch (e.Kind)
            {
                case NetEventKind.Connected:
                    _peers[e.Peer] = new Peer(e.Peer) { LastHeardUs = nowUs };
                    break;
                case NetEventKind.Disconnected:
                    if (_peers.Remove(e.Peer, out Peer? gone))
                        Leave(gone, "disconnected");
                    break;
                case NetEventKind.Data:
                    if (_peers.TryGetValue(e.Peer, out Peer? peer) && !peer.Gone)
                    {
                        peer.LastHeardUs = nowUs;
                        try
                        {
                            Handle(peer, e.Data);
                        }
                        catch (NetProtocolException ex)
                        {
                            Log?.Invoke($"peer {peer.Id} (slot {peer.Slot}) dropped: {ex.Message}");
                            Drop(peer, "protocol error");
                        }
                    }
                    break;
            }
        }
        CheckTimeouts();
        if (Phase == NetServerPhase.InGame)
        {
            CompleteTics();
            SendTics();
        }
    }

    /// <summary>
    /// T11.11: drop a peer not heard from for <see cref="TimeoutUs"/>, and a
    /// player whose command the next bundle has waited for that long (from
    /// the later of its last command and the last bundle sent, so a player
    /// held back by another's lateness is not counted late). After the
    /// polling, so what came during a hitch of this machine counts; never the
    /// host's own client (slot 0: the same machine).
    /// </summary>
    private void CheckTimeouts()
    {
        List<(Peer Peer, string Why)>? late = null;
        foreach (Peer peer in _peers.Values)
        {
            if (peer.Gone || peer.Slot == 0)
                continue;
            if (_nowUs - peer.LastHeardUs > TimeoutUs)
                (late ??= []).Add((peer, $"timed out: no word for {(_nowUs - peer.LastHeardUs) / 1000} ms"));
            else if (Phase == NetServerPhase.InGame && Desync is null && peer.Slot > 0 && (WaitingMask & (1 << peer.Slot)) != 0
                && _nowUs - Math.Max(_lastCommandUs[peer.Slot], _lastBundleUs) > TimeoutUs)
                (late ??= []).Add((peer, $"timed out: no command for {(_nowUs - Math.Max(_lastCommandUs[peer.Slot], _lastBundleUs)) / 1000} ms"));
        }
        if (late is null)
            return;
        foreach ((Peer peer, string why) in late)
        {
            Log?.Invoke($"peer {peer.Id} (slot {peer.Slot}) {why}");
            Drop(peer, why);
        }
    }

    private void Handle(Peer peer, ReadOnlyMemory<byte> data)
    {
        var r = new NetReader(data);
        if (r.Type == NetMessageType.Ping)
        {
            long clientTime = r.I64();
            int rtt = r.I32();
            r.End();
            peer.RttUs = rtt;
            if (rtt >= 0)
                peer.Rtts[peer.RttCount++ % RttSamples] = rtt;
            long clock = Phase == NetServerPhase.InGame ? _nowUs - _startUs : long.MinValue;
            for (int i = 0; i < NetProtocol.MaxPlayers; i++)
                _rttMs[i] = _slots[i] is { RttUs: >= 0 } p ? (ushort)Math.Min(p.RttUs / 1000, NetProtocol.NoRtt - 1) : NetProtocol.NoRtt;
            _transport.Send(peer.Id, NetMessages.EncodePong(clientTime, clock, WaitingMask, _rttMs), NetChannel.Unreliable);
            return;
        }
        if (peer.Slot < 0)
        {
            if (r.Type != NetMessageType.Hello)
                throw new NetProtocolException($"{r.Type} before the hello.");
            Hello(peer, NetMessages.DecodeHello(r));
            return;
        }
        switch (r.Type)
        {
            case NetMessageType.Ready:
                bool ready = r.U8() != 0;
                r.End();
                if (Phase == NetServerPhase.Lobby && peer.Ready != ready)
                {
                    peer.Ready = ready;
                    BroadcastLobby();
                }
                break;
            case NetMessageType.Commands:
                if (Phase != NetServerPhase.InGame)
                    throw new NetProtocolException("Commands before the start.");
                Commands(peer, r);
                break;
            case NetMessageType.Checksum:
                if (Phase != NetServerPhase.InGame)
                    throw new NetProtocolException("Checksum before the start.");
                Checksum(peer, NetMessages.DecodeChecksum(r));
                break;
            case NetMessageType.Quit:
                // T11.12c: its last commands come with it (the unreliable ones may be lost)
                if (Phase == NetServerPhase.InGame && (_playerMask & (1 << peer.Slot)) != 0 && _quitTic[peer.Slot] == int.MaxValue)
                {
                    _received.Clear();
                    int first = NetMessages.DecodeQuit(r, _received);
                    Store(peer.Slot, first);
                }
                Drop(peer, "quit");
                break;
            default:
                throw new NetProtocolException($"Unexpected {r.Type} from a client.");
        }
    }

    private void Hello(Peer peer, NetHello hello)
    {
        NetIdentity? id = hello.Identity;
        // T11.9d: the comparison the LAN browser makes too (NetIdentity.Refusal); the full game is the slots' below
        (NetRefusal reason, string detail)? refusal = NetIdentity.Refusal(hello.Protocol, id?.Build, id?.IwadMd5, id?.SamePwads(_identity) ?? false,
            Phase != NetServerPhase.Lobby, full: false, _identity) switch
        {
            NetRefusal.ProtocolMismatch => (NetRefusal.ProtocolMismatch, $"protocol {hello.Protocol}, the host's is {NetProtocol.Version}"),
            NetRefusal.BuildMismatch => (NetRefusal.BuildMismatch, $"build {id!.Build}, the host's is {_identity.Build}"),
            NetRefusal.IwadMismatch => (NetRefusal.IwadMismatch, $"IWAD {id!.IwadMd5}, the host's is {_identity.IwadMd5}"),
            NetRefusal.PwadMismatch => (NetRefusal.PwadMismatch, $"PWADs [{string.Join(", ", id!.PwadMd5s)}], the host's are [{string.Join(", ", _identity.PwadMd5s)}]"),
            NetRefusal.GameInProgress => (NetRefusal.GameInProgress, "the game has started"),
            _ => null,
        };
        int slot = -1;
        if (refusal is null)
        {
            if (hello.HostToken == HostToken)
                slot = _slots[0] is null ? 0 : -1;
            else
            {
                for (int i = 1; i < _maxPlayers && slot < 0; i++)
                {
                    if (_slots[i] is null)
                        slot = i;
                }
            }
            if (slot < 0)
                refusal = (NetRefusal.GameFull, $"{_maxPlayers} players already");
        }
        if (refusal is (NetRefusal reason, string detail))
        {
            Log?.Invoke($"peer {peer.Id} refused: {reason}: {detail}");
            _transport.Send(peer.Id, NetMessages.EncodeRefuse(reason, detail), NetChannel.Reliable);
            peer.Gone = true;
            _transport.Disconnect(peer.Id);
            return;
        }
        peer.Slot = slot;
        peer.Name = NetProtocol.CleanName(hello.Name, slot);
        _slots[slot] = peer;
        Log?.Invoke($"peer {peer.Id} joined in slot {slot} as {peer.Name}");
        Send(peer, NetMessages.EncodeWelcome(slot, _maxPlayers));
        Send(peer, NetMessages.EncodeSettings(_settings));
        BroadcastLobby();
    }

    private void Commands(Peer peer, NetReader r)
    {
        int slot = peer.Slot;
        if ((_playerMask & (1 << slot)) == 0 || _quitTic[slot] != int.MaxValue)
            throw new NetProtocolException("Commands from a player not in the game.");
        _received.Clear();
        int first = NetMessages.DecodeCommands(r, _received, out int ack);
        if (ack > CompleteTic)
            throw new NetProtocolException($"Commands acknowledging tic {ack}, the bundles are at {CompleteTic}.");
        if (!_heard[slot])
        {
            // its first word in the game: the bundles sent before may have
            // overtaken its Start and been dropped, so they go again at once
            _heard[slot] = true;
            _sentUpTo[slot] = Math.Min(_sentUpTo[slot], ack);
        }
        _bundleAck[slot] = Math.Max(_bundleAck[slot], ack);
        Store(slot, first);
    }

    /// <summary>
    /// T11.12c: keep the commands in <see cref="_received"/> (for tics
    /// <paramref name="first"/> on) from <paramref name="slot"/>'s player:
    /// those it had sent already are skipped, any that come before an earlier
    /// one's gap is filled wait in the ring, and the contiguous run
    /// (<see cref="_receivedUpTo"/>) moves on.
    /// </summary>
    private void Store(int slot, int first)
    {
        // a command's ring entry must not reach a tic a bundle may still be resent for (SendTics' FellBehindTics)
        int limit = CompleteTic + RingTics - FellBehindTics;
        for (int i = 0; i < _received.Count; i++)
        {
            int tic = first + i;
            if (tic <= _receivedUpTo[slot])
                continue;
            if (tic >= limit)
                throw new NetProtocolException($"Commands for tic {tic}, too far past tic {CompleteTic}.");
            _cmds[slot, Ring(tic)] = _received[i];
            _cmdTic[slot, Ring(tic)] = tic;
        }
        int before = _receivedUpTo[slot];
        while (_cmdTic[slot, Ring(_receivedUpTo[slot] + 1)] == _receivedUpTo[slot] + 1)
            _receivedUpTo[slot]++;
        if (_receivedUpTo[slot] != before)
            _lastCommandUs[slot] = _nowUs;
    }

    /// <summary>
    /// T11.12c: how far a client's bundle acknowledgement may lag the last
    /// complete tic before the server gives it up (its ring could no longer
    /// hold the tics to resend): half the ring. The lead limit keeps it within
    /// <see cref="NetProtocol.BackupTics"/> + 1 (NetMessages.cs, TICS), so only a
    /// broken client gets there.
    /// </summary>
    private const int FellBehindTics = RingTics / 2;

    /// <summary>The players in game at <paramref name="tic"/>.</summary>
    private byte MaskAt(int tic)
    {
        byte mask = 0;
        for (int i = 0; i < NetProtocol.MaxPlayers; i++)
        {
            if ((_playerMask & (1 << i)) != 0 && tic < _quitTic[i])
                mask |= (byte)(1 << i);
        }
        return mask;
    }

    private readonly byte[] _bundleMasks = new byte[NetProtocol.MaxTicsPerMessage];
    private readonly ticcmd_t[] _bundleCmds = new ticcmd_t[NetProtocol.MaxTicsPerMessage * NetProtocol.MaxPlayers];

    /// <summary>Move <see cref="CompleteTic"/> on over every tic whose commands are all in, in tic order.</summary>
    private void CompleteTics()
    {
        if (Desync is not null)
            return; // T11.11: nothing after a desync: every machine stops
        int before = CompleteTic;
        while (true)
        {
            int tic = CompleteTic + 1;
            byte mask = MaskAt(tic);
            if (mask == 0)
                break;
            bool complete = true;
            for (int i = 0; i < NetProtocol.MaxPlayers && complete; i++)
            {
                if ((mask & (1 << i)) != 0 && _receivedUpTo[i] < tic)
                    complete = false;
            }
            if (!complete)
                break;
            CompleteTic = tic;
        }
        if (CompleteTic != before)
            _lastBundleUs = _nowUs;
    }

    /// <summary>
    /// T11.12c: send each player still in the game the bundles of the
    /// complete tics it has not acknowledged, with the ack of its commands:
    /// when there are new tics, and again every <see cref="NetProtocol.ResendUs"/>
    /// while it lacks some or the ack is news.
    /// </summary>
    private void SendTics()
    {
        if (Desync is not null)
            return;
        List<Peer>? behind = null;
        for (int slot = 0; slot < NetProtocol.MaxPlayers; slot++)
        {
            if (_slots[slot] is not Peer peer || (_playerMask & (1 << slot)) == 0 || _quitTic[slot] != int.MaxValue)
                continue;
            bool fresh = CompleteTic > _sentUpTo[slot];
            bool due = _nowUs - _lastSentUs[slot] >= NetProtocol.ResendUs
                && (_bundleAck[slot] < CompleteTic || _ackSent[slot] != _receivedUpTo[slot]);
            if (!fresh && !due)
                continue;
            if (CompleteTic - _bundleAck[slot] > FellBehindTics)
            {
                (behind ??= []).Add(peer);
                continue;
            }
            int first = _bundleAck[slot] + 1;
            do
            {
                int count = Math.Min(CompleteTic - first + 1, NetProtocol.MaxTicsPerMessage);
                for (int t = 0; t < count; t++)
                {
                    int tic = first + t;
                    byte mask = MaskAt(tic);
                    _bundleMasks[t] = mask;
                    for (int i = 0; i < NetProtocol.MaxPlayers; i++)
                        _bundleCmds[t * NetProtocol.MaxPlayers + i] = (mask & (1 << i)) != 0 ? _cmds[i, Ring(tic)] : default;
                }
                _transport.Send(peer.Id, NetMessages.EncodeBundle(_receivedUpTo[slot], first, count, _bundleMasks.AsSpan(0, count),
                    _bundleCmds.AsSpan(0, count * NetProtocol.MaxPlayers)), NetChannel.Unreliable);
                first += count;
            }
            while (first <= CompleteTic);
            _sentUpTo[slot] = CompleteTic;
            _ackSent[slot] = _receivedUpTo[slot];
            _lastSentUs[slot] = _nowUs;
        }
        if (behind is null)
            return;
        foreach (Peer peer in behind)
        {
            Log?.Invoke($"peer {peer.Id} (slot {peer.Slot}) dropped: its bundles fell {CompleteTic - _bundleAck[peer.Slot]} tics behind");
            Drop(peer, "fell behind");
        }
    }

    private void Checksum(Peer peer, NetChecksumReport report)
    {
        if (Desync is not null)
            return; // announced: no more comparing
        if (report.Window < _nextWindow || (StartTic + (long)(report.Window + 1) * NetProtocol.WindowTics) - 1 > CompleteTic)
        {
            if (report.Window < _nextWindow)
                return; // compared already (or after the desync)
            throw new NetProtocolException($"Checksum for window {report.Window}, whose tics are not all bundled.");
        }
        if (!_reports.TryGetValue(report.Window, out NetChecksumReport?[]? reports))
            _reports[report.Window] = reports = new NetChecksumReport?[NetProtocol.MaxPlayers];
        if (reports[peer.Slot] is not null)
            throw new NetProtocolException($"Two checksums for window {report.Window}.");
        reports[peer.Slot] = report;
        CompareWindows();
    }

    /// <summary>Compare each window, in order, once every player still in the game reported it.</summary>
    private void CompareWindows()
    {
        while (Desync is null && _reports.TryGetValue(_nextWindow, out NetChecksumReport?[]? reports))
        {
            NetChecksumReport? first = null;
            bool differ = false;
            for (int i = 0; i < NetProtocol.MaxPlayers; i++)
            {
                bool expected = _slots[i] is not null && (_playerMask & (1 << i)) != 0 && _quitTic[i] == int.MaxValue;
                NetChecksumReport? report = reports[i];
                if (report is null)
                {
                    if (expected)
                        return; // still waiting for it
                    continue;
                }
                if (report.FirstBadTic >= 0)
                    differ = true;
                if (first is null)
                    first = report;
                else if (!first.SameState(report))
                    differ = true;
            }
            _reports.Remove(_nextWindow);
            if (differ)
            {
                int firstTic = StartTic + _nextWindow * NetProtocol.WindowTics;
                Desync = new NetDesync(_nextWindow, firstTic, firstTic + NetProtocol.WindowTics - 1);
                Log?.Invoke($"desync in window {_nextWindow} (tics {firstTic}–{firstTic + NetProtocol.WindowTics - 1})");
                Broadcast(NetMessages.EncodeDesync(Desync.Value));
                _reports.Clear();
            }
            _nextWindow++;
        }
    }

    /// <summary>A quit, a protocol error or a timeout (<paramref name="why"/>): the peer goes.</summary>
    private void Drop(Peer peer, string why)
    {
        peer.Gone = true;
        _transport.Disconnect(peer.Id);
        _peers.Remove(peer.Id);
        Leave(peer, why);
    }

    private void Leave(Peer peer, string why)
    {
        int slot = peer.Slot;
        if (slot < 0 || _slots[slot] != peer)
            return;
        _slots[slot] = null;
        if (Phase == NetServerPhase.Lobby)
        {
            Log?.Invoke($"slot {slot} left the lobby ({why})");
            BroadcastLobby();
            return;
        }
        if ((_playerMask & (1 << slot)) != 0 && _quitTic[slot] == int.MaxValue)
        {
            // vanilla's d_net.c: the player is out from the first tic it sent no command for.
            _quitTic[slot] = Math.Max(_receivedUpTo[slot] + 1, CompleteTic + 1);
            Log?.Invoke($"slot {slot} left the game at tic {_quitTic[slot]} ({why})");
            Broadcast(NetMessages.EncodePlayerQuit(slot, _quitTic[slot]));
            CompleteTics();
            CompareWindows();
        }
    }

    private readonly string[] _names = new string[NetProtocol.MaxPlayers];

    private void BroadcastLobby()
    {
        for (int i = 0; i < NetProtocol.MaxPlayers; i++)
            _names[i] = NameOf(i);
        Broadcast(NetMessages.EncodeLobby(OccupiedMask, ReadyMask, _names));
    }

    private void Broadcast(byte[] message)
    {
        for (int i = 0; i < NetProtocol.MaxPlayers; i++)
        {
            if (_slots[i] is Peer peer)
                Send(peer, message);
        }
    }

    private void Send(Peer peer, byte[] message) => _transport.Send(peer.Id, message, NetChannel.Reliable);
}
