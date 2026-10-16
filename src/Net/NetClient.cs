using System;
using System.Collections.Generic;
using IsoDoom.Sim;

namespace IsoDoom.Net;

/// <summary>Where a <see cref="NetClient"/> is.</summary>
public enum NetClientState
{
    /// <summary>Not connected yet.</summary>
    Idle,

    /// <summary>Connecting, or waiting for the answer to its hello.</summary>
    Connecting,

    /// <summary>In the lobby, with a slot.</summary>
    Lobby,

    /// <summary>In the game: the tics come from the bundles.</summary>
    InGame,

    /// <summary>The server refused the hello (<see cref="NetClient.Refusal"/>).</summary>
    Refused,

    /// <summary>Disconnected: quit, the server gone, or a protocol error (<see cref="NetClient.Error"/>).</summary>
    Disconnected,
}

/// <summary>
/// A player's side of the lockstep protocol (T11.5; the protocol is described
/// at the top of <c>NetMessages.cs</c>), after Chocolate Doom's net_client.c.
/// In the game, each frame the caller:
/// <list type="number">
/// <item><see cref="Update"/>s it with its clock;</item>
/// <item>builds <see cref="CommandsDue"/> local commands, each for <see cref="MakeTic"/>, and passes each to <see cref="SendCommand"/>, then <see cref="Flush"/>es;</item>
/// <item>while <see cref="TicsReady"/>, takes a tic (<see cref="TryTakeTic"/>), runs it with the bundle's commands, and reports it (<see cref="TicRan"/>).</item>
/// </list>
/// The sim sees nothing from here but the commands.
/// </summary>
public sealed class NetClient
{
    /// <summary>How often the client pings the server, in microseconds.</summary>
    public const long PingIntervalUs = 100_000;

    /// <summary>The lag adjustment's limit: the local clock runs within ±this of real time.</summary>
    public const double MaxClockAdjust = 0.05;

    /// <summary>The lag adjustment's gain: the rate changes by this per second of clock error.</summary>
    public const double ClockGain = 1.0;

    private const int RingTics = 4 * NetProtocol.BackupTics;
    private const int ClockSamples = 8;

    private readonly INetTransport _transport;
    private readonly NetIdentity _identity;
    private readonly ulong _hostToken;
    private int _peer = -1;
    private long _nowUs;
    private long _lastUpdateUs = long.MinValue;
    private long _lastPingUs = long.MinValue;

    // The game clock: microseconds since the start, as this client reckons the server's.
    private long _clockUs;

    // Clock samples from the pongs: round trip, the server's clock estimated then, and the local time then.
    private readonly (int RttUs, long ServerUs, long LocalUs)[] _samples = new (int, long, long)[ClockSamples];
    private int _sampleCount;
    private int _sampleNext;

    // The commands queued by SendCommand, from _pendingFirst.
    private readonly List<ticcmd_t> _pending = [];
    private int _pendingFirst;

    // The bundles received, by tic (ring), and the consistancy of every player after each tic run.
    private readonly ticcmd_t[,] _bundles = new ticcmd_t[RingTics, NetProtocol.MaxPlayers];
    private readonly byte[] _bundleMasks = new byte[RingTics];
    private readonly short[,] _consistancy = new short[RingTics, NetProtocol.MaxPlayers];
    private bool _awaitingTicRan;
    private int _badTicToReport = -1;

    /// <summary>A client over <paramref name="transport"/> for a machine with <paramref name="identity"/>.</summary>
    /// <param name="transport">The transport.</param>
    /// <param name="identity">This machine's build and WADs, which must be the host's.</param>
    /// <param name="hostToken">The server's <see cref="NetServer.HostToken"/> for the host's own client (slot 0); 0 for the others.</param>
    public NetClient(INetTransport transport, NetIdentity identity, ulong hostToken = 0)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(identity);
        _transport = transport;
        _identity = identity;
        _hostToken = hostToken;
    }

    /// <summary>Where it is.</summary>
    public NetClientState State { get; private set; }

    /// <summary>Why the server refused, once <see cref="NetClientState.Refused"/>.</summary>
    public NetRefusal? Refusal { get; private set; }

    /// <summary>The refusal's or the protocol error's text.</summary>
    public string? Error { get; private set; }

    /// <summary>This client's slot (its player), −1 before the welcome.</summary>
    public int Slot { get; private set; } = -1;

    /// <summary>The slots the server opened.</summary>
    public int MaxPlayers { get; private set; }

    /// <summary>The game's settings as the server last sent them.</summary>
    public NetGameSettings? Settings { get; private set; }

    /// <summary>The lobby's taken slots.</summary>
    public byte LobbyOccupied { get; private set; }

    /// <summary>The lobby's ready slots.</summary>
    public byte LobbyReady { get; private set; }

    /// <summary>The game's start (settings, <c>consoleplayer</c>, players, start tic, delay), once in game.</summary>
    public NetStart? Start { get; private set; }

    /// <summary>The server's desync announcement, if one came.</summary>
    public NetDesync? Desync { get; private set; }

    /// <summary>The first tic whose bundle carried a consistancy that differs from this client's (−1 none).</summary>
    public int FirstBadTic { get; private set; } = -1;

    /// <summary>The last round trip to the server, in microseconds (−1 none yet).</summary>
    public int RttUs { get; private set; } = -1;

    /// <summary>The local clock's rate (1 = real time), set by the lag adjustment.</summary>
    public double ClockRate { get; private set; } = 1;

    /// <summary>The game clock's error last measured against the server's estimated clock, in microseconds (positive: behind).</summary>
    public long ClockErrorUs { get; private set; }

    /// <summary>The tic the next local command is for.</summary>
    public int MakeTic { get; private set; }

    /// <summary>The tic the game clock has reached: tics up to it are due.</summary>
    public int ClockTic => Start is null ? 0 : Start.StartTic + (int)Math.Floor(_clockUs * (double)NetProtocol.TicRate / NetProtocol.MicrosecondsPerSecond);

    /// <summary>The last tic whose bundle came.</summary>
    public int BundleTic { get; private set; }

    /// <summary>The next tic to run.</summary>
    public int NextTic { get; private set; }

    /// <summary>The last tic run (<see cref="TicRan"/>).</summary>
    public int LastRanTic { get; private set; }

    /// <summary>The first tic each slot is out of the game from (<see cref="int.MaxValue"/>: in, or never was).</summary>
    public IReadOnlyList<int> QuitTics => _quitTics;

    private readonly int[] _quitTics = [int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue];

    /// <summary>A line for each event (welcome, start, quits, desync, errors), for the log.</summary>
    public event Action<string>? Log;

    /// <summary>Connect to the server at <paramref name="address"/>:<paramref name="port"/>; the hello follows the connection.</summary>
    public void Connect(string address, int port)
    {
        if (State != NetClientState.Idle)
            throw new InvalidOperationException($"Connect when {State}.");
        State = NetClientState.Connecting;
        _peer = _transport.Connect(address, port);
    }

    /// <summary>Tell the server whether this player is ready to start.</summary>
    public void SetReady(bool ready)
    {
        if (State != NetClientState.Lobby)
            throw new InvalidOperationException($"Ready when {State}.");
        Send(NetMessages.EncodeReady(ready));
    }

    /// <summary>Leave: the server drops this player from the bundles after its last command.</summary>
    public void Quit()
    {
        if (State is NetClientState.Lobby or NetClientState.InGame or NetClientState.Connecting)
        {
            Flush();
            Send(NetMessages.EncodeQuit());
            _transport.Disconnect(_peer);
        }
        State = NetClientState.Disconnected;
    }

    /// <summary>Poll the transport, handle what came and move the clock on, at local time <paramref name="nowUs"/> (µs).</summary>
    public void Update(long nowUs)
    {
        long dt = _lastUpdateUs == long.MinValue ? 0 : Math.Max(0, nowUs - _lastUpdateUs);
        _lastUpdateUs = nowUs;
        _nowUs = nowUs;
        if (State == NetClientState.InGame)
            _clockUs += (long)Math.Round(dt * ClockRate);
        Flush();
        while (State is not (NetClientState.Disconnected or NetClientState.Idle) && _transport.Poll(out NetEvent e))
        {
            if (e.Peer != _peer)
                continue;
            switch (e.Kind)
            {
                case NetEventKind.Connected:
                    Send(NetMessages.EncodeHello(_identity, _hostToken));
                    break;
                case NetEventKind.Disconnected:
                    if (State != NetClientState.Refused)
                    {
                        Error ??= "The server is gone.";
                        State = NetClientState.Disconnected;
                        Log?.Invoke("disconnected");
                    }
                    break;
                case NetEventKind.Data:
                    try
                    {
                        Handle(e.Data);
                    }
                    catch (NetProtocolException ex)
                    {
                        Error = ex.Message;
                        Log?.Invoke($"protocol error: {ex.Message}");
                        _transport.Disconnect(_peer);
                        State = NetClientState.Disconnected;
                    }
                    break;
            }
        }
        if (State is NetClientState.Lobby or NetClientState.InGame && (_lastPingUs == long.MinValue || nowUs - _lastPingUs >= PingIntervalUs))
        {
            _lastPingUs = nowUs;
            _transport.Send(_peer, NetMessages.EncodePing(nowUs, RttUs), NetChannel.Unreliable);
        }
        if (State == NetClientState.InGame)
            AdjustClock();
    }

    private void Handle(ReadOnlyMemory<byte> data)
    {
        var r = new NetReader(data);
        switch (r.Type)
        {
            case NetMessageType.Pong:
                Pong(r.I64(), r.I64());
                r.End();
                break;
            case NetMessageType.Refuse:
                Refusal = (NetRefusal)r.U8();
                Error = r.Text();
                r.End();
                State = NetClientState.Refused;
                Log?.Invoke($"refused: {Refusal}: {Error}");
                break;
            case NetMessageType.Welcome:
                Expect(NetClientState.Connecting, r.Type);
                Slot = r.U8();
                MaxPlayers = r.U8();
                r.End();
                if (Slot >= NetProtocol.MaxPlayers)
                    throw new NetProtocolException($"Welcome to slot {Slot}.");
                State = NetClientState.Lobby;
                Log?.Invoke($"welcome: slot {Slot} of {MaxPlayers}");
                break;
            case NetMessageType.Settings:
                Expect(NetClientState.Lobby, r.Type);
                Settings = r.Settings();
                r.End();
                break;
            case NetMessageType.Lobby:
                Expect(NetClientState.Lobby, r.Type);
                LobbyOccupied = r.U8();
                LobbyReady = r.U8();
                r.End();
                break;
            case NetMessageType.Start:
                Expect(NetClientState.Lobby, r.Type);
                BeginGame(NetMessages.DecodeStart(r));
                break;
            case NetMessageType.Bundle:
                Expect(NetClientState.InGame, r.Type);
                Bundle(r);
                break;
            case NetMessageType.Desync:
                Expect(NetClientState.InGame, r.Type);
                Desync = new NetDesync(r.I32(), r.I32(), r.I32());
                r.End();
                Log?.Invoke($"desync in window {Desync.Value.Window} (tics {Desync.Value.FirstTic}–{Desync.Value.LastTic})");
                break;
            case NetMessageType.PlayerQuit:
                Expect(NetClientState.InGame, r.Type);
                int slot = r.U8();
                int tic = r.I32();
                r.End();
                if (slot >= NetProtocol.MaxPlayers)
                    throw new NetProtocolException($"PlayerQuit of slot {slot}.");
                _quitTics[slot] = tic;
                Log?.Invoke($"player {slot + 1} left the game at tic {tic}");
                break;
            default:
                throw new NetProtocolException($"Unexpected {r.Type} from the server.");
        }
    }

    private void Expect(NetClientState state, NetMessageType type)
    {
        if (State != state)
            throw new NetProtocolException($"{type} when {State}.");
    }

    private void BeginGame(NetStart start)
    {
        if (start.ConsolePlayer != Slot)
            throw new NetProtocolException($"Start for player {start.ConsolePlayer}, this client is in slot {Slot}.");
        Start = start;
        Settings = start.Settings;
        State = NetClientState.InGame;
        // The server's clock started when it sent the Start, half a round trip ago.
        _clockUs = RttUs > 0 ? RttUs / 2 : 0;
        _sampleCount = 0;
        ClockRate = 1;
        MakeTic = start.StartTic + start.Delay;
        BundleTic = start.StartTic - 1;
        NextTic = start.StartTic;
        LastRanTic = start.StartTic - 1;
        Log?.Invoke($"start: player {Slot + 1}, players {start.PlayerMask:x}, tic {start.StartTic}, delay {start.Delay}");
    }

    private void Bundle(NetReader r)
    {
        int first = r.I32();
        int count = r.U8();
        if (first != BundleTic + 1 || count is 0 or > NetProtocol.MaxTicsPerMessage)
            throw new NetProtocolException($"Bundle of {count} tics from {first}, expected tic {BundleTic + 1}.");
        for (int t = first; t < first + count; t++)
        {
            if (t - NextTic >= RingTics)
                throw new NetProtocolException($"Bundle for tic {t}, too far past tic {NextTic}.");
            byte mask = r.U8();
            if ((mask & ~Start!.PlayerMask) != 0)
                throw new NetProtocolException($"Bundle for tic {t} with players {mask:x} not in the game.");
            int slot = t % RingTics;
            _bundleMasks[slot] = mask;
            for (int i = 0; i < NetProtocol.MaxPlayers; i++)
                _bundles[slot, i] = (mask & (1 << i)) != 0 ? r.Ticcmd() : default;
        }
        r.End();
        BundleTic = first + count - 1;
    }

    /// <summary>How many local commands are due now (built for <see cref="MakeTic"/> on, and passed to <see cref="SendCommand"/>).</summary>
    public int CommandsDue
    {
        get
        {
            if (State != NetClientState.InGame)
                return 0;
            int target = Math.Min(ClockTic + Start!.Delay, LastRanTic + NetProtocol.BackupTics);
            return Math.Max(0, target - MakeTic + 1);
        }
    }

    /// <summary>
    /// Queue this player's command for <see cref="MakeTic"/> (and move it on);
    /// <see cref="Flush"/> or the next <see cref="Update"/> sends it. Its
    /// consistancy is set here (vanilla's <c>G_BuildTiccmd</c>).
    /// </summary>
    public void SendCommand(ticcmd_t cmd)
    {
        if (CommandsDue == 0)
            throw new InvalidOperationException($"No command is due (tic {MakeTic}, clock {ClockTic}, ran {LastRanTic}).");
        int source = MakeTic - NetProtocol.ConsistancyLag;
        cmd.consistancy = source >= Start!.StartTic ? _consistancy[source % RingTics, Slot] : (short)0;
        if (_pending.Count == 0)
            _pendingFirst = MakeTic;
        _pending.Add(cmd);
        MakeTic++;
        if (_pending.Count == NetProtocol.MaxTicsPerMessage)
            Flush();
    }

    /// <summary>Send the queued commands as one message.</summary>
    public void Flush()
    {
        if (_pending.Count == 0)
            return;
        if (State == NetClientState.InGame)
            Send(NetMessages.EncodeCommands(_pendingFirst, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pending)));
        _pending.Clear();
    }

    /// <summary>
    /// The tics that can run now: those whose bundle came and that the game
    /// clock has reached, in order (0 while waiting for a bundle).
    /// </summary>
    public int TicsReady
    {
        get
        {
            if (State != NetClientState.InGame || _awaitingTicRan)
                return 0;
            return Math.Max(0, Math.Min(BundleTic, ClockTic) - NextTic + 1);
        }
    }

    /// <summary>
    /// Take the next tic to run, if <see cref="TicsReady"/>: its number, the
    /// players in game (bit i: slot i) and each one's command in
    /// <paramref name="cmds"/> (<see cref="NetProtocol.MaxPlayers"/> long; the
    /// others zeroed). Report it with <see cref="TicRan"/> before taking the next.
    /// </summary>
    public bool TryTakeTic(Span<ticcmd_t> cmds, out int tic, out byte playerMask)
    {
        if (cmds.Length < NetProtocol.MaxPlayers)
            throw new ArgumentException("One command a player.", nameof(cmds));
        if (_awaitingTicRan)
            throw new InvalidOperationException($"Tic {NextTic - 1} was taken and not reported (TicRan).");
        tic = NextTic;
        playerMask = 0;
        if (TicsReady == 0)
            return false;
        int slot = tic % RingTics;
        playerMask = _bundleMasks[slot];
        int source = tic - NetProtocol.ConsistancyLag;
        for (int i = 0; i < NetProtocol.MaxPlayers; i++)
        {
            cmds[i] = _bundles[slot, i];
            // g_game.c G_Ticker's consistency check: the sender's view of its player against ours.
            if ((playerMask & (1 << i)) != 0 && source >= Start!.StartTic
                && cmds[i].consistancy != _consistancy[source % RingTics, i] && FirstBadTic < 0)
            {
                FirstBadTic = tic;
                _badTicToReport = tic;
                Log?.Invoke($"consistency failure at tic {tic}: player {i + 1}'s {cmds[i].consistancy}, ours {_consistancy[source % RingTics, i]}");
            }
        }
        NextTic++;
        _awaitingTicRan = true;
        return true;
    }

    /// <summary>
    /// Report tic <paramref name="tic"/> run: <c>World.Checksum()</c> after it,
    /// and each player's consistancy after it (vanilla's: the player mobj's
    /// <c>x</c>, or the <c>P_Random</c> index without a mobj; 0 not in game).
    /// After a checksum window's last tic this sends the window's report.
    /// </summary>
    public void TicRan(int tic, ulong checksum, ReadOnlySpan<short> consistancy)
    {
        if (!_awaitingTicRan || tic != NextTic - 1)
            throw new InvalidOperationException($"TicRan({tic}), the tic taken is {NextTic - 1}{(_awaitingTicRan ? "" : " and was reported")}.");
        if (consistancy.Length != NetProtocol.MaxPlayers)
            throw new ArgumentException("One consistancy a player.", nameof(consistancy));
        _awaitingTicRan = false;
        LastRanTic = tic;
        for (int i = 0; i < NetProtocol.MaxPlayers; i++)
            _consistancy[tic % RingTics, i] = consistancy[i];
        int sinceStart = tic - Start!.StartTic;
        if ((sinceStart + 1) % NetProtocol.WindowTics == 0)
        {
            var report = new NetChecksumReport(sinceStart / NetProtocol.WindowTics, checksum, consistancy.ToArray(), _badTicToReport);
            _badTicToReport = -1;
            Send(NetMessages.EncodeChecksum(report));
        }
    }

    private void Pong(long pingTimeUs, long serverClockUs)
    {
        int rtt = (int)Math.Clamp(_nowUs - pingTimeUs, 0, int.MaxValue);
        RttUs = rtt;
        if (State != NetClientState.InGame || serverClockUs == long.MinValue)
            return;
        _samples[_sampleNext] = (rtt, serverClockUs + rtt / 2, _nowUs);
        _sampleNext = (_sampleNext + 1) % ClockSamples;
        _sampleCount = Math.Min(_sampleCount + 1, ClockSamples);
    }

    /// <summary>
    /// Chocolate Doom's lag adjustment (net_client.c), our way: the server's
    /// clock now, estimated from the recent pong with the shortest round
    /// trip (the least queueing), against the local game clock; the clock's
    /// rate follows the difference, within ±<see cref="MaxClockAdjust"/>.
    /// </summary>
    private void AdjustClock()
    {
        if (_sampleCount == 0)
            return;
        int best = 0;
        for (int i = 1; i < _sampleCount; i++)
        {
            if (_samples[i].RttUs < _samples[best].RttUs)
                best = i;
        }
        (_, long serverUs, long localUs) = _samples[best];
        ClockErrorUs = serverUs + (_nowUs - localUs) - _clockUs;
        ClockRate = 1 + Math.Clamp(ClockErrorUs * ClockGain / NetProtocol.MicrosecondsPerSecond, -MaxClockAdjust, MaxClockAdjust);
    }

    private void Send(byte[] message) => _transport.Send(_peer, message, NetChannel.Reliable);
}
