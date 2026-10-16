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

    // The game: per slot, the commands received (a ring of RingTics) and up to which tic.
    private const int RingTics = 4 * NetProtocol.BackupTics;
    private readonly ticcmd_t[,] _cmds = new ticcmd_t[NetProtocol.MaxPlayers, RingTics];
    private readonly int[] _receivedUpTo = new int[NetProtocol.MaxPlayers];
    private readonly int[] _quitTic = new int[NetProtocol.MaxPlayers];
    private byte _playerMask;
    private int _nextWindow;

    private sealed class Peer(int id)
    {
        public int Id { get; } = id;
        public int Slot { get; set; } = -1;
        public bool Ready { get; set; }
        public int RttUs { get; set; } = -1;
        public bool Gone { get; set; }
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

    /// <summary>A line for each lobby or game event (joins, refusals, quits, the desync), for the log.</summary>
    public event Action<string>? Log;

    /// <summary>The last round trip the client in <paramref name="slot"/> reported, in microseconds (−1 none yet).</summary>
    public int RttUsOf(int slot) => _slots[slot]?.RttUs ?? -1;

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
        _playerMask = OccupiedMask;
        for (int i = 0; i < NetProtocol.MaxPlayers; i++)
        {
            _quitTic[i] = int.MaxValue;
            _receivedUpTo[i] = startTic + delay - 1;
            for (int t = startTic; t < startTic + delay; t++)
                _cmds[i, t % RingTics] = default;
        }
        for (int i = 0; i < NetProtocol.MaxPlayers; i++)
        {
            if (_slots[i] is Peer peer)
                Send(peer, NetMessages.EncodeStart(new NetStart(i, _playerMask, startTic, delay, _settings)));
        }
        Log?.Invoke($"start: players {_playerMask:x}, tic {startTic}, delay {delay}");
        SendBundles();
    }

    /// <summary>Poll the transport and handle what came, at time <paramref name="nowUs"/> (µs, the host's clock).</summary>
    public void Update(long nowUs)
    {
        _nowUs = nowUs;
        while (_transport.Poll(out NetEvent e))
        {
            switch (e.Kind)
            {
                case NetEventKind.Connected:
                    _peers[e.Peer] = new Peer(e.Peer);
                    break;
                case NetEventKind.Disconnected:
                    if (_peers.Remove(e.Peer, out Peer? gone))
                        Leave(gone, "disconnected");
                    break;
                case NetEventKind.Data:
                    if (_peers.TryGetValue(e.Peer, out Peer? peer) && !peer.Gone)
                    {
                        try
                        {
                            Handle(peer, e.Data);
                        }
                        catch (NetProtocolException ex)
                        {
                            Log?.Invoke($"peer {peer.Id} (slot {peer.Slot}) dropped: {ex.Message}");
                            Drop(peer);
                        }
                    }
                    break;
            }
        }
        if (Phase == NetServerPhase.InGame)
            SendBundles();
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
            long clock = Phase == NetServerPhase.InGame ? _nowUs - _startUs : long.MinValue;
            _transport.Send(peer.Id, NetMessages.EncodePong(clientTime, clock), NetChannel.Unreliable);
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
                r.End();
                Drop(peer);
                break;
            default:
                throw new NetProtocolException($"Unexpected {r.Type} from a client.");
        }
    }

    private void Hello(Peer peer, NetHello hello)
    {
        NetIdentity? id = hello.Identity;
        (NetRefusal reason, string detail)? refusal =
            hello.Protocol != NetProtocol.Version ? (NetRefusal.ProtocolMismatch, $"protocol {hello.Protocol}, the host's is {NetProtocol.Version}")
            : id!.Build != _identity.Build ? (NetRefusal.BuildMismatch, $"build {id.Build}, the host's is {_identity.Build}")
            : !string.Equals(id.IwadMd5, _identity.IwadMd5, StringComparison.OrdinalIgnoreCase) ? (NetRefusal.IwadMismatch, $"IWAD {id.IwadMd5}, the host's is {_identity.IwadMd5}")
            : !id.SamePwads(_identity) ? (NetRefusal.PwadMismatch, $"PWADs [{string.Join(", ", id.PwadMd5s)}], the host's are [{string.Join(", ", _identity.PwadMd5s)}]")
            : Phase != NetServerPhase.Lobby ? (NetRefusal.GameInProgress, "the game has started")
            : null;
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
        _slots[slot] = peer;
        Log?.Invoke($"peer {peer.Id} joined in slot {slot}");
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
        int first = NetMessages.DecodeCommands(r, _received);
        if (first != _receivedUpTo[slot] + 1)
            throw new NetProtocolException($"Commands for tic {first}, expected {_receivedUpTo[slot] + 1}.");
        int last = first + _received.Count - 1;
        if (last - CompleteTic >= RingTics)
            throw new NetProtocolException($"Commands for tic {last}, too far past tic {CompleteTic}.");
        for (int i = 0; i < _received.Count; i++)
            _cmds[slot, (first + i) % RingTics] = _received[i];
        _receivedUpTo[slot] = last;
    }

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

    /// <summary>Bundle and send every tic whose commands are all in, in tic order.</summary>
    private void SendBundles()
    {
        while (true)
        {
            int first = CompleteTic + 1;
            int count = 0;
            while (count < NetProtocol.MaxTicsPerMessage)
            {
                int tic = first + count;
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
                _bundleMasks[count] = mask;
                for (int i = 0; i < NetProtocol.MaxPlayers; i++)
                    _bundleCmds[count * NetProtocol.MaxPlayers + i] = (mask & (1 << i)) != 0 ? _cmds[i, tic % RingTics] : default;
                count++;
            }
            if (count == 0)
                return;
            CompleteTic = first + count - 1;
            Broadcast(NetMessages.EncodeBundle(first, count, _bundleMasks.AsSpan(0, count), _bundleCmds.AsSpan(0, count * NetProtocol.MaxPlayers)));
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

    /// <summary>A quit or a protocol error: the peer goes.</summary>
    private void Drop(Peer peer)
    {
        peer.Gone = true;
        _transport.Disconnect(peer.Id);
        _peers.Remove(peer.Id);
        Leave(peer, "quit");
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
            SendBundles();
            CompareWindows();
        }
    }

    private void BroadcastLobby() => Broadcast(NetMessages.EncodeLobby(OccupiedMask, ReadyMask));

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
