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

    private const int RingTics = NetProtocol.RingTics;
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

    // T11.12c: the commands made (a ring by tic), those the server acknowledged
    // (up to _ackedByServer), the last tic sent, and what the last packet said and when.
    private readonly ticcmd_t[] _made = new ticcmd_t[RingTics];
    private int _ackedByServer;
    private int _sentUpTo;
    private int _ackSent;
    private long _lastSentUs = long.MinValue;

    // The bundles received, by tic (ring, each entry's tic beside it: T11.12c,
    // they may come out of order), and the consistancy of every player after each tic run.
    private readonly ticcmd_t[,] _bundles = new ticcmd_t[RingTics, NetProtocol.MaxPlayers];
    private readonly byte[] _bundleMasks = new byte[RingTics];
    private readonly int[] _bundleTics = new int[RingTics];
    private readonly short[,] _consistancy = new short[RingTics, NetProtocol.MaxPlayers];
    private bool _awaitingTicRan;
    private int _badTicToReport = -1;

    // T11.11: when the server was last heard from; the stall under way (since when, −1 none) and the recent stalls' starts.
    private long _lastHeardUs;
    private long _stallSinceUs = -1;
    private readonly Queue<long> _stalls = new();
    private readonly ushort[] _rttMs = [NetProtocol.NoRtt, NetProtocol.NoRtt, NetProtocol.NoRtt, NetProtocol.NoRtt];

    // T11.12b: per own command (rings by tic) the delay it was made at (0: made late, in a raise's batch),
    // when its tic's bundle was whole and when its tic was first due; the first tic of its own, the tics
    // marked due, the end of a raise's batch; the last second's longest need, stall and gap between polls, in tenths of a second.
    private readonly byte[] _madeDelay = new byte[RingTics];
    private readonly long[] _wholeUs = new long[RingTics];
    private readonly long[] _dueUs = new long[RingTics];
    private int _firstOwnTic;
    private int _dueThrough;
    private int _batchEnd;
    private const int Buckets = 10;
    private const long BucketUs = NetProtocol.MicrosecondsPerSecond / Buckets;
    private readonly (long Index, int Max)[] _needBuckets = NewBuckets();
    private readonly (long Index, int Max)[] _stallBuckets = NewBuckets();
    private readonly (long Index, int Max)[] _gapBuckets = NewBuckets();

    private static (long Index, int Max)[] NewBuckets()
    {
        var buckets = new (long Index, int Max)[Buckets];
        Array.Fill(buckets, (long.MinValue, 0));
        return buckets;
    }

    /// <summary>A client over <paramref name="transport"/> for a machine with <paramref name="identity"/>.</summary>
    /// <param name="transport">The transport.</param>
    /// <param name="identity">This machine's build and WADs, which must be the host's.</param>
    /// <param name="hostToken">The server's <see cref="NetServer.HostToken"/> for the host's own client (slot 0); 0 for the others.</param>
    /// <param name="name">T11.9c: the player's name, sent in the hello (the server cleans it; empty: <c>PLAYER N</c>).</param>
    public NetClient(INetTransport transport, NetIdentity identity, ulong hostToken = 0, string name = "")
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(name);
        _transport = transport;
        _identity = identity;
        _hostToken = hostToken;
        Name = name;
    }

    /// <summary>T11.9c: the name this player's hello carries.</summary>
    public string Name { get; }

    /// <summary>T11.9c: each slot's player name as the server last sent the lobby (<c>""</c> for an empty slot); kept through the game.</summary>
    public IReadOnlyList<string> LobbyNames { get; private set; } = ["", "", "", ""];

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

    /// <summary>
    /// T11.11: how long the client waits without a packet from the server
    /// before it gives the game up (µs; the pings keep a live link busy).
    /// </summary>
    public long TimeoutUs { get; set; } = NetProtocol.DefaultTimeoutUs;

    /// <summary>T11.11: the slots the server's bundles waited for at its last pong (bit i: slot i).</summary>
    public byte WaitingFor { get; private set; }

    /// <summary>T11.11: each slot's round trip as the server last reported it, in ms (<see cref="NetProtocol.NoRtt"/> unknown or empty).</summary>
    public IReadOnlyList<ushort> PlayerRttMs => _rttMs;

    /// <summary>
    /// T11.11: how long the game clock has been past the last bundle (a tic
    /// is due whose commands have not come: the world holds), in µs; 0 when
    /// not (or out of the game, or after a desync).
    /// </summary>
    public long LateUs => _stallSinceUs < 0 ? 0 : _nowUs - _stallSinceUs;

    /// <summary>T11.11: the stalls (the clock passing the last bundle) that began in the last second.</summary>
    public int StallsLastSecond
    {
        get
        {
            int count = 0;
            foreach (long at in _stalls)
            {
                if (_nowUs - at <= NetProtocol.MicrosecondsPerSecond)
                    count++;
            }
            return count;
        }
    }

    /// <summary>T11.11: whether the game stopped for the server's desync (<see cref="Desync"/>): no tic runs, no command goes.</summary>
    public bool Stopped => Desync is not null;

    /// <summary>The local clock's rate (1 = real time), set by the lag adjustment.</summary>
    public double ClockRate { get; private set; } = 1;

    /// <summary>The game clock's error last measured against the server's estimated clock, in microseconds (positive: behind).</summary>
    public long ClockErrorUs { get; private set; }

    /// <summary>The tic the next local command is for.</summary>
    public int MakeTic { get; private set; }

    /// <summary>
    /// T11.12b: the input delay now, in tics: the Start's, then each
    /// <c>Delay</c> message's from the host (it makes its commands up to its
    /// clock tic + this).
    /// </summary>
    public int Delay { get; private set; }

    /// <summary>T11.12b: the delay before the last change (the Start's when none).</summary>
    public int PreviousDelay { get; private set; }

    /// <summary>T11.12b: the tic the next command was for when the delay last changed (−1 none).</summary>
    public int DelayChangedTic { get; private set; } = -1;

    /// <summary>T11.12b: this client's clock (µs) when the delay last changed (<see cref="long.MinValue"/> none).</summary>
    public long DelayChangedUs { get; private set; } = long.MinValue;

    /// <summary>T11.12b: how many times the delay changed in this game.</summary>
    public int DelayChanges { get; private set; }

    /// <summary>
    /// T11.12b: the longest delay a tic of the last second needed, µs (−1
    /// none): for each tic of its own commands, the delay it was made at less
    /// how early its whole bundle was there for it (the first poll its clock
    /// had reached it; negative: it held), so the least delay at which it
    /// would have run on time. The ping carries it to the host, whose delay follows it.
    /// </summary>
    public int NeededDelayUs => BucketMax(_needBuckets, -1);

    /// <summary>T11.12b: the longest gap between two <see cref="Update"/>s of the last second, µs (about a frame; longer, a hitch of this machine's).</summary>
    public int LongestGapUs => BucketMax(_gapBuckets, 0);

    /// <summary>T11.12b: the longest stall of the last second (<see cref="LateUs"/>: one under way counts as far as it went), µs.</summary>
    public int LongestStallUs => Math.Max(BucketMax(_stallBuckets, 0), (int)Math.Min(LateUs, int.MaxValue));

    /// <summary>The tic the game clock has reached: tics up to it are due.</summary>
    public int ClockTic => Start is null ? 0 : Start.StartTic + (int)Math.Floor(_clockUs * (double)NetProtocol.TicRate / NetProtocol.MicrosecondsPerSecond);

    /// <summary>
    /// T11.8: the game clock in tics with its fraction (<see cref="ClockTic"/>
    /// is its floor): how far the presentation is past the last tic run (it
    /// draws between the last two tics, clamped at the last).
    /// </summary>
    public double ClockTics => Start is null ? 0 : Start.StartTic + _clockUs * (double)NetProtocol.TicRate / NetProtocol.MicrosecondsPerSecond;

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

    /// <summary>
    /// Leave: the server drops this player from the bundles after its last
    /// command (T11.12c: the Quit carries the commands it has not acknowledged).
    /// </summary>
    public void Quit()
    {
        if (State is NetClientState.Lobby or NetClientState.InGame or NetClientState.Connecting)
        {
            int first = 0, count = 0;
            if (State == NetClientState.InGame && !Stopped)
            {
                first = _ackedByServer + 1;
                count = Math.Max(0, MakeTic - first);
                for (int i = 0; i < count; i++)
                    _quitCmds[i] = _made[Ring(first + i)];
            }
            Send(NetMessages.EncodeQuit(first, _quitCmds.AsSpan(0, count)));
            _transport.Disconnect(_peer);
        }
        State = NetClientState.Disconnected;
    }

    private readonly ticcmd_t[] _quitCmds = new ticcmd_t[RingTics];

    /// <summary>A tic's place in the rings (negative tics too).</summary>
    private static int Ring(int tic) => (int)((uint)tic % RingTics);

    /// <summary>Poll the transport, handle what came and move the clock on, at local time <paramref name="nowUs"/> (µs).</summary>
    public void Update(long nowUs)
    {
        long dt = _lastUpdateUs == long.MinValue ? 0 : Math.Max(0, nowUs - _lastUpdateUs);
        _lastUpdateUs = nowUs;
        _nowUs = nowUs;
        Note(_gapBuckets, (int)Math.Min(dt, int.MaxValue)); // T11.12b: a hitch of this machine's
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
                    _lastHeardUs = nowUs;
                    Send(NetMessages.EncodeHello(_identity, _hostToken, Name));
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
                    _lastHeardUs = nowUs;
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
            _transport.Send(_peer, NetMessages.EncodePing(nowUs, RttUs, State == NetClientState.InGame ? NeededDelayUs : -1, LongestStallUs, LongestGapUs), NetChannel.Unreliable);
        }
        if (State is NetClientState.Lobby or NetClientState.InGame && nowUs - _lastHeardUs > TimeoutUs)
        {
            // T11.11: the server is gone without a word (a killed process, a cut link)
            Error = $"The host timed out (no word for {(nowUs - _lastHeardUs) / 1000} ms).";
            Log?.Invoke(Error);
            _transport.Disconnect(_peer);
            State = NetClientState.Disconnected;
        }
        if (State == NetClientState.InGame)
        {
            AdjustClock();
            MarkDue();
            TrackStalls();
        }
        else
            _stallSinceUs = -1;
    }

    /// <summary>
    /// T11.11: a stall begins when the game clock passes the last tic whose
    /// bundle came (the world must hold), and ends when the bundles catch up.
    /// </summary>
    private void TrackStalls()
    {
        bool stalled = !Stopped && ClockTic > BundleTic;
        if (stalled && _stallSinceUs < 0)
        {
            _stallSinceUs = _nowUs;
            _stalls.Enqueue(_nowUs);
        }
        else if (!stalled && _stallSinceUs >= 0)
        {
            Note(_stallBuckets, (int)Math.Min(_nowUs - _stallSinceUs, int.MaxValue)); // T11.12b
            _stallSinceUs = -1;
        }
        while (_stalls.Count > 0 && _nowUs - _stalls.Peek() > NetProtocol.MicrosecondsPerSecond)
            _stalls.Dequeue();
    }

    private void Handle(ReadOnlyMemory<byte> data)
    {
        var r = new NetReader(data);
        switch (r.Type)
        {
            case NetMessageType.Pong:
                {
                    long time = r.I64(), clock = r.I64();
                    byte waiting = r.U8();
                    for (int i = 0; i < NetProtocol.MaxPlayers; i++)
                        _rttMs[i] = r.U16();
                    r.End();
                    WaitingFor = waiting;
                    Pong(time, clock);
                    break;
                }
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
                {
                    NetLobby lobby = NetMessages.DecodeLobby(r);
                    LobbyOccupied = lobby.Occupied;
                    LobbyReady = lobby.Ready;
                    LobbyNames = lobby.Names;
                    break;
                }
            case NetMessageType.Start:
                Expect(NetClientState.Lobby, r.Type);
                BeginGame(NetMessages.DecodeStart(r));
                break;
            case NetMessageType.Bundle:
                // T11.12c: an unreliable bundle may overtake the Start (the server sends it again); none after a desync
                if (State == NetClientState.InGame && !Stopped)
                    Bundle(r);
                else if (State == NetClientState.Lobby)
                {
                    // T11.12c: an unreliable bundle that overtook the Start waits for it (the first tics are due at once)
                    if (_early.Count < EarlyBundles)
                        _early.Add(data);
                }
                else if (State != NetClientState.InGame)
                    throw new NetProtocolException($"{r.Type} when {State}.");
                break;
            case NetMessageType.Desync:
                Expect(NetClientState.InGame, r.Type);
                Desync = new NetDesync(r.I32(), r.I32(), r.I32());
                r.End(); // T11.11: the game stops: no more commands (CommandsDue, Flush)
                Log?.Invoke($"desync in window {Desync.Value.Window} (tics {Desync.Value.FirstTic}–{Desync.Value.LastTic}): stopped after tic {LastRanTic}");
                break;
            case NetMessageType.Delay:
                Expect(NetClientState.InGame, r.Type);
                {
                    (int delay, int hostTic) = NetMessages.DecodeDelay(r);
                    if (Stopped || delay == Delay)
                        break;
                    // T11.12b: at once; a raise makes the commands between due now, a lowering makes none until the clock catches up
                    Log?.Invoke($"delay {Delay} -> {delay} tic(s) from tic {MakeTic} (the host's at tic {hostTic})");
                    PreviousDelay = Delay;
                    if (delay > Delay)
                        _batchEnd = Math.Max(_batchEnd, MakeTic + delay - Delay); // T11.12b: made at once, late for their delay
                    Delay = delay;
                    DelayChangedTic = MakeTic;
                    DelayChangedUs = _nowUs;
                    DelayChanges++;
                    break;
                }
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
        Delay = PreviousDelay = start.Delay;
        DelayChangedTic = -1;
        DelayChangedUs = long.MinValue;
        DelayChanges = 0;
        _firstOwnTic = _batchEnd = MakeTic;
        _dueThrough = start.StartTic - 1;
        Array.Fill(_needBuckets, (long.MinValue, 0));
        _ackedByServer = MakeTic - 1; // the server made the first delay tics' commands itself
        _sentUpTo = MakeTic - 1;
        _ackSent = int.MinValue;
        _lastSentUs = long.MinValue;
        Array.Fill(_bundleTics, int.MinValue);
        BundleTic = start.StartTic - 1;
        NextTic = start.StartTic;
        LastRanTic = start.StartTic - 1;
        Log?.Invoke($"start: player {Slot + 1}, players {start.PlayerMask:x}, tic {start.StartTic}, delay {start.Delay}");
        foreach (ReadOnlyMemory<byte> early in _early)
        {
            var r = new NetReader(early);
            Bundle(r);
        }
        _early.Clear();
    }

    // T11.12c: the bundles that came before the Start (a few: the server sends them again).
    private const int EarlyBundles = 8;
    private readonly List<ReadOnlyMemory<byte>> _early = [];

    private void Bundle(NetReader r)
    {
        int ack = r.I32();
        int first = r.I32();
        int count = r.U8();
        if (count > NetProtocol.MaxTicsPerMessage)
            throw new NetProtocolException($"Bundle of {count} tics.");
        if (ack >= MakeTic)
            throw new NetProtocolException($"Bundle acknowledging command {ack}, the last made is {MakeTic - 1}.");
        _ackedByServer = Math.Max(_ackedByServer, ack);
        for (int t = first; t < first + count; t++)
        {
            byte mask = r.U8();
            if ((mask & ~Start!.PlayerMask) != 0)
                throw new NetProtocolException($"Bundle for tic {t} with players {mask:x} not in the game.");
            bool keep = t > BundleTic;
            if (keep && t - NextTic >= RingTics)
                throw new NetProtocolException($"Bundle for tic {t}, too far past tic {NextTic}.");
            int slot = Ring(t);
            for (int i = 0; i < NetProtocol.MaxPlayers; i++)
            {
                ticcmd_t cmd = (mask & (1 << i)) != 0 ? r.Ticcmd() : default;
                if (keep)
                    _bundles[slot, i] = cmd;
            }
            if (keep)
            {
                // T11.12c: a tic sent again is the same; one before an earlier gap is filled waits here
                _bundleMasks[slot] = mask;
                _bundleTics[slot] = t;
            }
        }
        r.End();
        while (_bundleTics[Ring(BundleTic + 1)] == BundleTic + 1)
        {
            BundleTic++;
            // T11.12b: a tic due already held: what it needed
            _wholeUs[Ring(BundleTic)] = _nowUs;
            if (BundleTic <= _dueThrough)
                NoteNeed(BundleTic, _dueUs[Ring(BundleTic)] - _nowUs);
        }
    }

    /// <summary>
    /// T11.12b: the tics the clock reached at this poll (after the bundles
    /// came: a tic whose bundle came in the same poll did not hold, as
    /// <see cref="TrackStalls"/> counts) are due now; those whole already
    /// note what they needed (they were early by the time since).
    /// </summary>
    private void MarkDue()
    {
        int to = ClockTic;
        if (to - _dueThrough > RingTics)
            _dueThrough = to - RingTics;
        for (int t = _dueThrough + 1; t <= to; t++)
        {
            _dueUs[Ring(t)] = _nowUs;
            if (t <= BundleTic)
                NoteNeed(t, _nowUs - _wholeUs[Ring(t)]);
        }
        _dueThrough = Math.Max(_dueThrough, to);
    }

    /// <summary>
    /// T11.12b: tic <paramref name="tic"/>'s bundle was whole
    /// <paramref name="earlyUs"/> before the tic was due (negative: it held so
    /// long): an own command made at a delay needed that delay less it.
    /// </summary>
    private void NoteNeed(int tic, long earlyUs)
    {
        if (tic < _firstOwnTic || tic >= MakeTic || _madeDelay[Ring(tic)] == 0)
            return;
        long need = _madeDelay[Ring(tic)] * (NetProtocol.MicrosecondsPerSecond / NetProtocol.TicRate) - earlyUs;
        Note(_needBuckets, (int)Math.Clamp(need, 0, int.MaxValue));
    }

    /// <summary>T11.12b: <paramref name="value"/> into the last second's buckets (one a tenth of a second, by this client's clock).</summary>
    private void Note((long Index, int Max)[] buckets, int value)
    {
        long index = _nowUs / BucketUs;
        ref (long Index, int Max) b = ref buckets[(int)((ulong)index % Buckets)];
        if (b.Index != index)
            b = (index, value);
        else if (value > b.Max)
            b.Max = value;
    }

    /// <summary>T11.12b: the largest value of the buckets of the last second (<paramref name="none"/> without one).</summary>
    private int BucketMax((long Index, int Max)[] buckets, int none)
    {
        long index = _nowUs / BucketUs;
        int max = none;
        foreach ((long i, int m) in buckets)
        {
            if (i > index - Buckets && i <= index && m > max)
                max = m;
        }
        return max;
    }

    /// <summary>How many local commands are due now (built for <see cref="MakeTic"/> on, and passed to <see cref="SendCommand"/>).</summary>
    public int CommandsDue
    {
        get
        {
            if (State != NetClientState.InGame || Stopped)
                return 0;
            // T11.12c: and never one the ring of commands made would lose an unacknowledged one for
            int target = Math.Min(Math.Min(ClockTic + Delay, LastRanTic + NetProtocol.BackupTics), _ackedByServer + RingTics);
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
        _made[Ring(MakeTic)] = cmd;
        _madeDelay[Ring(MakeTic)] = MakeTic < _batchEnd ? (byte)0 : (byte)Delay; // T11.12b: a raise's batch is made late: not a sample
        MakeTic++;
    }

    /// <summary>
    /// T11.12c: send the commands the server has not acknowledged, with the
    /// ack of the bundles: when there are new ones, and again every
    /// <see cref="NetProtocol.ResendUs"/> while some are unacknowledged or the
    /// ack is news (several messages when they are more than
    /// <see cref="NetProtocol.MaxTicsPerMessage"/>).
    /// </summary>
    public void Flush()
    {
        if (State != NetClientState.InGame || Stopped)
            return;
        bool fresh = MakeTic - 1 > _sentUpTo;
        bool due = (_lastSentUs == long.MinValue || _nowUs - _lastSentUs >= NetProtocol.ResendUs)
            && (_ackedByServer < MakeTic - 1 || _ackSent != BundleTic);
        if (!fresh && !due)
            return;
        int first = _ackedByServer + 1;
        Span<ticcmd_t> chunk = stackalloc ticcmd_t[NetProtocol.MaxTicsPerMessage];
        do
        {
            int count = Math.Min(MakeTic - first, NetProtocol.MaxTicsPerMessage);
            for (int i = 0; i < count; i++)
                chunk[i] = _made[Ring(first + i)];
            _transport.Send(_peer, NetMessages.EncodeCommands(BundleTic, first, chunk[..count]), NetChannel.Unreliable);
            first += count;
        }
        while (first < MakeTic);
        _sentUpTo = MakeTic - 1;
        _ackSent = BundleTic;
        _lastSentUs = _nowUs;
    }

    /// <summary>
    /// The tics that can run now: those whose bundle came and that the game
    /// clock has reached, in order (0 while waiting for a bundle).
    /// </summary>
    public int TicsReady
    {
        get
        {
            if (State != NetClientState.InGame || _awaitingTicRan || Stopped)
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
