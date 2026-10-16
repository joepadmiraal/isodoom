using System;
using System.Collections.Generic;
using System.Globalization;
using IsoDoom.Sim;

namespace IsoDoom.Game;

/// <summary>Whether this machine hosts a netgame, joins one, or plays alone.</summary>
public enum NetRole
{
    /// <summary>No netgame arguments: single player.</summary>
    None,

    /// <summary><c>-host</c> or <c>-server</c>: this machine runs the server and plays in slot 0.</summary>
    Host,

    /// <summary><c>-connect ADDR[:PORT]</c>: this machine joins a host.</summary>
    Join,
}

/// <summary>
/// The netgame's command-line arguments (T11.6, SPEC §12 "T11.6 ENet
/// transport"), Chocolate Doom's names where it has one:
/// <list type="bullet">
/// <item><c>-host [PORT]</c> or <c>-server</c> host (and play, slot 0);
/// <c>-port N</c> the port (both sides; default <see cref="DefaultPort"/>, 0 on
/// the host: any free one, printed); <c>-bind ADDR</c> the host's address to
/// listen on (default every interface; <c>127.0.0.1</c>: loopback only).</item>
/// <item><c>-connect ADDR[:PORT]</c> join (an IPv6 address as <c>[ADDR]:PORT</c>).</item>
/// <item><c>-players N</c> (1–4, default 2) the host starts once N players are in
/// and ready; <c>-netdelay N</c> the input delay in tics (1–35; default
/// measured from the round trips at the start, <see cref="DelayFromRtt"/>).</item>
/// <item>The host's game modes (T11.4's, carried in the Start): <c>-deathmatch</c>,
/// <c>-altdeath</c>, <c>-timer MINUTES</c>, <c>-avg</c> (20 minutes), <c>-respawn</c>, <c>-fast</c>,
/// <c>-nomonsters</c>; T11.4: <see cref="Modes"/> reads them for a game alone too.</item>
/// </list>
/// Plain C#: parsed from the user arguments (<c>OS.GetCmdlineUserArgs</c>) and tested without the engine.
/// </summary>
public sealed record NetOptions
{
    /// <summary>The port a host listens on and a join connects to by default: ZDoom's netgame port (SPEC §12 T11.6).</summary>
    public const int DefaultPort = 5029;

    /// <summary>The longest default input delay, in tics (the plan's 1–8).</summary>
    public const int MaxDefaultDelay = 8;

    /// <summary>Host, join or neither.</summary>
    public NetRole Role { get; init; }

    /// <summary>The port: the host's to listen on (0: any free one), the join's to connect to.</summary>
    public int Port { get; init; } = DefaultPort;

    /// <summary>The address the host listens on (<c>*</c>: every interface).</summary>
    public string BindAddress { get; init; } = "*";

    /// <summary>The host a join connects to.</summary>
    public string? ConnectAddress { get; init; }

    /// <summary>The players the host waits for before it starts (1–4).</summary>
    public int Players { get; init; } = 2;

    /// <summary>The input delay in tics, or null for the measured default.</summary>
    public int? Delay { get; init; }

    /// <summary>0 cooperative, 1 deathmatch (<c>-deathmatch</c>), 2 altdeath (<c>-altdeath</c>).</summary>
    public int Deathmatch { get; init; }

    /// <summary><c>-timer</c>'s minutes (<c>-avg</c>: 20), 0 none.</summary>
    public int TimerMinutes { get; init; }

    /// <summary><c>-respawn</c>.</summary>
    public bool Respawn { get; init; }

    /// <summary><c>-fast</c>.</summary>
    public bool Fast { get; init; }

    /// <summary><c>-nomonsters</c>.</summary>
    public bool NoMonsters { get; init; }

    /// <summary>
    /// The netgame options in <paramref name="args"/>, or null when it names
    /// neither <c>-host</c>/<c>-server</c> nor <c>-connect</c>. A bad value
    /// (or both roles) is an <see cref="ArgumentException"/> naming the argument.
    /// </summary>
    public static NetOptions? Parse(IReadOnlyList<string> args)
    {
        NetOptions o = ParseAll(args, out bool host, out int? port, out string? connect);
        if (host && connect is not null)
            throw new ArgumentException("-host/-server and -connect: host a game or join one, not both");
        if (!host && connect is null)
            return null;
        if (connect is not null && port == 0)
            throw new ArgumentException("-connect: port 0 (a join needs the host's port)");
        return o with
        {
            Role = host ? NetRole.Host : NetRole.Join,
            ConnectAddress = connect,
            Port = port ?? DefaultPort,
        };
    }

    /// <summary>
    /// T11.4: the game modes in <paramref name="args"/> (<c>-deathmatch</c>,
    /// <c>-altdeath</c>, <c>-timer</c>, <c>-avg</c>, <c>-respawn</c>,
    /// <c>-fast</c>, <c>-nomonsters</c>), for a game of this machine alone
    /// (<c>--level E1M1 -deathmatch</c>) as for a host's; <see cref="Role"/>
    /// is <see cref="NetRole.None"/>. A bad value is an <see cref="ArgumentException"/>.
    /// </summary>
    public static NetOptions Modes(IReadOnlyList<string> args) => ParseAll(args, out _, out _, out _);

    /// <summary>
    /// <paramref name="spawn"/> with these options' modes: their deathmatch
    /// and timer (<see cref="SpawnSettings.timelimit"/>) where they name one,
    /// <c>-nomonsters</c>, <c>-respawn</c> and <c>-fast</c> added.
    /// </summary>
    public SpawnSettings Apply(SpawnSettings spawn) => spawn with
    {
        deathmatch = Deathmatch != 0 ? Deathmatch : spawn.deathmatch,
        nomonsters = spawn.nomonsters || NoMonsters,
        respawnparm = spawn.respawnparm || Respawn,
        fastparm = spawn.fastparm || Fast,
        timelimit = TimerMinutes != 0 ? TimerMinutes : spawn.timelimit,
    };

    private static NetOptions ParseAll(IReadOnlyList<string> args, out bool host, out int? port, out string? connect)
    {
        ArgumentNullException.ThrowIfNull(args);
        host = false;
        port = null;
        connect = null;
        bool avg = false;
        var o = new NetOptions();
        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            string? next = i + 1 < args.Count ? args[i + 1] : null;
            switch (a)
            {
                case "-host":
                case "-server":
                    host = true;
                    // -host [PORT]: a value only when it is a number (else it is the next argument)
                    if (a == "-host" && next is not null && int.TryParse(next, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                        port = ParsePort(args[++i], "-host");
                    break;
                case "-port":
                    port = ParsePort(Value(args, ref i, a), a);
                    break;
                case "-bind":
                    o = o with { BindAddress = Value(args, ref i, a) };
                    break;
                case "-connect":
                    (connect, int? connectPort) = Address(Value(args, ref i, a));
                    if (connectPort is int cp)
                        port = cp;
                    break;
                case "-players":
                    o = o with { Players = Int(Value(args, ref i, a), a, 1, 4) };
                    break;
                case "-netdelay":
                    o = o with { Delay = Int(Value(args, ref i, a), a, 1, IsoDoom.Net.NetProtocol.MaxDelay) };
                    break;
                case "-deathmatch":
                    o = o with { Deathmatch = Math.Max(o.Deathmatch, 1) };
                    break;
                case "-altdeath":
                    o = o with { Deathmatch = 2 };
                    break;
                case "-timer":
                    o = o with { TimerMinutes = Int(Value(args, ref i, a), a, 0, ushort.MaxValue) };
                    break;
                case "-avg": // T11.4: 20 minutes, whatever -timer says (Chocolate Doom's d_main.c order)
                    avg = true;
                    break;
                case "-respawn":
                    o = o with { Respawn = true };
                    break;
                case "-fast":
                    o = o with { Fast = true };
                    break;
                case "-nomonsters":
                    o = o with { NoMonsters = true };
                    break;
                default:
                    break;
            }
        }
        return avg ? o with { TimerMinutes = 20 } : o;
    }

    /// <summary>
    /// The default input delay (SPEC §12 T11.6): the commands must reach the
    /// host and their bundle come back before their tic, so a round trip of
    /// <paramref name="maxRttUs"/> (the slowest player's) takes
    /// ⌈RTT / tic⌉ tics, plus one for the frames' polling; clamped to 1–<see cref="MaxDefaultDelay"/>.
    /// </summary>
    public static int DelayFromRtt(long maxRttUs)
    {
        const long TicUs = IsoDoom.Net.NetProtocol.MicrosecondsPerSecond / IsoDoom.Net.NetProtocol.TicRate;
        long tics = (Math.Max(0, maxRttUs) + TicUs - 1) / TicUs + 1;
        return (int)Math.Clamp(tics, 1, MaxDefaultDelay);
    }

    private static string Value(IReadOnlyList<string> args, ref int i, string name)
    {
        if (i + 1 >= args.Count)
            throw new ArgumentException($"{name}: a value is missing");
        return args[++i];
    }

    private static int Int(string s, string name, int min, int max)
        => int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n) && n >= min && n <= max
            ? n
            : throw new ArgumentException($"{name}: \"{s}\" ({min}-{max})");

    private static int ParsePort(string s, string name) => Int(s, name, 0, 65535);

    /// <summary><c>ADDR</c>, <c>ADDR:PORT</c>, <c>[IPV6]</c>, <c>[IPV6]:PORT</c> or a bare IPv6 address.</summary>
    private static (string Address, int? Port) Address(string s)
    {
        if (s.StartsWith('['))
        {
            int close = s.IndexOf(']', StringComparison.Ordinal);
            if (close < 2)
                throw new ArgumentException($"-connect: \"{s}\" ([ADDR]:PORT)");
            string rest = s[(close + 1)..];
            if (rest.Length == 0)
                return (s[1..close], null);
            if (rest[0] != ':')
                throw new ArgumentException($"-connect: \"{s}\" ([ADDR]:PORT)");
            return (s[1..close], Int(rest[1..], "-connect", 1, 65535));
        }
        int colon = s.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0 || s.IndexOf(':', colon + 1) >= 0)
            return s.Length > 0 ? (s, null) : throw new ArgumentException("-connect: an address is missing");
        if (colon == 0)
            throw new ArgumentException($"-connect: \"{s}\" (ADDR[:PORT])");
        return (s[..colon], Int(s[(colon + 1)..], "-connect", 1, 65535));
    }
}
