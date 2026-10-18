using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using IsoDoom.Sim;
using IsoDoom.Wad;

namespace IsoDoom.Game;

/// <summary>
/// A <c>.route</c> file (T4.8; the format is described at the tests'
/// <c>VanillaRoute</c>): a header (<c>iwad</c>, <c>map</c>, <c>skill</c>,
/// <c>start X Y ANGLE</c>, <c>exit normal|secret</c>, <c>monsters</c>,
/// T10.7: <c>gameversion NAME</c>) and one vanilla
/// <c>ticcmd</c> per tic (<c>FORWARD SIDE TURN BUTTONS [xCOUNT]</c>, a demo's
/// relative turn byte), between which (T6.4) <c>damage X Y AMOUNT</c>,
/// <c>alert</c> and (T6.5) <c>rocket</c> lines are <see cref="RouteEvent"/>s
/// run at the start of the next tic. Plain C#: the tests parse their routes with it, and
/// the level script's <c>route FILE</c> (T5.10) plays one in the level scene.
/// T11.2: a multi-player route has a <c>players N</c> header (2-4: players 1
/// to N in the game, a netgame) before its <c>ticcmd</c> lines, each of which
/// then holds one <c>ticcmd</c> per player separated by <c>|</c>
/// (<c>F S T B | F S T B [xCOUNT]</c>), and may have a <c>deathmatch 0|1|2</c>
/// header; its events name their player with a last <c>pN</c> (default
/// <c>p1</c>). The single-player format is unchanged. T11.4: a
/// <c>timer MINUTES</c> header is vanilla's <c>-timer</c> (the level ends
/// after that many minutes in deathmatch). P2: a <c>pwad SET</c> header
/// names a PWAD set of <c>docs/PWAD-VALIDATION.md</c> loaded over the
/// route's IWAD as <c>-file</c> (the tests' <c>PwadSets</c>, the vanilla
/// reference's <c>tools/VanillaRef/pwads.py</c>).
/// </summary>
public sealed class RouteFile
{
    /// <summary>The <c>monsters</c> header (T6.4): the demo spawns monsters (no <c>nomonsters</c>).</summary>
    public bool Monsters { get; private init; }
    /// <summary>The route's events (T6.4), in order, each before the tic of its <see cref="RouteEvent.Tic"/> (0-based).</summary>
    public IReadOnlyList<RouteEvent> Events { get; private set; } = [];

    /// <summary>
    /// T11.2: this route (or demo) with <paramref name="events"/> as its events:
    /// a demo carries none, so a route's commands read back from its demo
    /// (<see cref="ToDemo"/>, <see cref="FromDemo"/>) take the route's.
    /// </summary>
    public RouteFile WithEvents(IReadOnlyList<RouteEvent> events)
    {
        var copy = (RouteFile)MemberwiseClone();
        copy.Events = events;
        return copy;
    }

    /// <summary><c>synthetic</c>, <c>doom1</c>, <c>doom</c> (M9), <c>doom2</c>, <c>tnt</c>, <c>plutonia</c> or <c>testmap</c> (not checked here).</summary>
    public string? Iwad { get; private init; }
    /// <summary>P2: the <c>pwad</c> header, a PWAD set's folder (<c>av</c>, <c>dtwid</c>, …; not checked here), or null.</summary>
    public string? Pwad { get; private init; }
    /// <summary>The <c>map</c> header as written, or null.</summary>
    public string? Map { get; private init; }
    /// <summary>The <c>skill</c> header, 1-5 (default 3).</summary>
    public int Skill { get; private init; } = 3;
    /// <summary>The <c>start</c> header (map units, degrees), or null for the map's player 1 start (T5.6).</summary>
    public (int X, int Y, int Angle)? Start { get; private init; }
    /// <summary>The <c>exit</c> header (T5.9): 1 for <c>normal</c>, 2 for <c>secret</c>, 0 without one.</summary>
    public int Exit { get; private init; }
    /// <summary>Player 1's <c>ticcmd</c> of each tic (a demo's console player's); <see cref="NetCmds"/> has every player's.</summary>
    public IReadOnlyList<ticcmd_t> Cmds { get; private init; } = [];

    /// <summary>
    /// T11.2: the players in the game as a mask (bit <c>i</c>: player <c>i + 1</c>;
    /// <see cref="SpawnSettings.playersingame"/>): the <c>players N</c> header's
    /// first N players, or a demo's <c>playeringame[]</c>; 1 (player 1 alone) by default.
    /// </summary>
    public int PlayersInGame { get; private init; } = 1;

    /// <summary>
    /// T11.2: whether the game is a netgame: as g_game.c's <c>G_DoPlayDemo</c>,
    /// player 2 in the game (<c>playeringame[1]</c>; a <c>players N</c> header of 2 or more).
    /// </summary>
    public bool Netgame => (PlayersInGame & 2) != 0;

    /// <summary>T11.2: the number of players in the game (<see cref="PlayersInGame"/>' bits).</summary>
    public int PlayerCount => System.Numerics.BitOperations.PopCount((uint)PlayersInGame);

    /// <summary>T11.2: the <c>deathmatch</c> header or a demo's byte: 0 co-op (or single player), 1 deathmatch, 2 altdeath.</summary>
    public int Deathmatch { get; private init; }

    /// <summary>T11.4: the <c>timer</c> header, <c>-timer</c>'s minutes (<see cref="SpawnSettings.timelimit"/>; 0 none; a demo has none).</summary>
    public int Timer { get; private init; }

    /// <summary>T11.2: a demo's <c>respawnparm</c> byte (routes have none).</summary>
    public bool Respawn { get; private init; }

    /// <summary>T11.2: a demo's <c>fastparm</c> byte (routes have none).</summary>
    public bool Fast { get; private init; }

    /// <summary>T11.2: a demo's <c>consoleplayer</c> byte (0-based; routes play as player 1's machine, 0).</summary>
    public int ConsolePlayer { get; private init; }

    /// <summary>
    /// T11.2: every player's <c>ticcmd</c> of each tic, <see cref="World.MAXPLAYERS"/>
    /// long (vanilla's <c>netcmds</c>; a player not in the game has an empty one),
    /// for <see cref="World.G_Ticker(ticcmd_t[])"/>. A single-player route's
    /// has <see cref="Cmds"/>' at index 0.
    /// </summary>
    public IReadOnlyList<ticcmd_t[]> NetCmds { get; private init; } = [];

    /// <summary>
    /// T11.2: the game's spawn settings in <paramref name="mode"/> on
    /// <paramref name="skill"/>: the players, netgame, deathmatch, respawn,
    /// fast and nomonsters as the route (or demo) has them.
    /// </summary>
    public SpawnSettings Settings(GameMode mode, skill_t skill, GameVersion? gameversion = null, GameMission gamemission = GameMission.none) =>
        new(mode, skill, netgame: Netgame, deathmatch: Deathmatch, nomonsters: !Monsters, respawnparm: Respawn, fastparm: Fast,
            gameversion: gameversion, gamemission: gamemission, playersingame: PlayersInGame, timelimit: Timer);

    /// <summary>
    /// The <c>gameversion</c> header (T10.7): the executable vanilla plays the
    /// route as (Chocolate Doom's <c>-gameversion</c> argument: <c>1.9</c>,
    /// <c>ultimate</c>, <c>final</c>, …; <see cref="GameVersions.Parse"/>), or
    /// null for the IWAD's default.
    /// </summary>
    public GameVersion? GameVersion { get; private init; }

    /// <summary>
    /// T10.7: the mission of the <c>iwad</c> header <paramref name="iwad"/>:
    /// Final Doom's for <c>tnt</c> and <c>plutonia</c>, Doom II's for
    /// <c>doom2</c>, Doom's for the others (<c>doom1</c>, <c>synthetic</c>, <c>testmap</c>).
    /// </summary>
    public static GameMission MissionOf(string iwad) => iwad switch
    {
        "tnt" => GameMission.pack_tnt,
        "plutonia" => GameMission.pack_plut,
        "doom2" => GameMission.doom2,
        _ => GameMission.doom,
    };

    /// <summary>
    /// T10.1, T10.7: the game mode of the <c>iwad</c> header <paramref name="iwad"/>:
    /// <c>commercial</c> for <c>doom2</c>, <c>tnt</c> and <c>plutonia</c>, (M9)
    /// The Ultimate Doom's <c>retail</c> for <c>doom</c>, else DOOM1.WAD's
    /// <c>shareware</c>. An executable before The Ultimate Doom's plays
    /// <c>retail</c> as <c>registered</c> (<see cref="GameVersions.D_SetGameVersion"/>).
    /// </summary>
    public static GameMode ModeOf(string iwad) => iwad switch
    {
        "doom2" or "tnt" or "plutonia" => GameMode.commercial,
        "doom" => GameMode.retail,
        _ => GameMode.shareware,
    };

    /// <summary>
    /// T10.7: the executable vanilla plays the route as (the reference's
    /// <c>-gameversion</c>): the <c>gameversion</c> header, else its IWAD's
    /// default (<see cref="GameVersions.Default"/>: Final Doom's for <c>tnt</c>
    /// and <c>plutonia</c>, The Ultimate Doom's for <c>doom</c>, M9, v1.9 for the others); null without an <c>iwad</c>
    /// header (a demo lump, <see cref="FromDemo"/>). The tests' routes and the
    /// level script's <c>route</c> both read it, so a script played as another
    /// version fails instead of drifting.
    /// </summary>
    public GameVersion? ImpliedGameVersion =>
        GameVersion ?? (Iwad is { } iwad ? GameVersions.Default(ModeOf(iwad), MissionOf(iwad)) : null);

    public static RouteFile Parse(string path) => Parse(File.ReadLines(path), path);

    /// <summary>Parses the route's lines; <paramref name="path"/> names it in errors (<see cref="FormatException"/>).</summary>
    public static RouteFile Parse(IEnumerable<string> lines, string path)
    {
        string? iwad = null, map = null, pwad = null;
        int skill = 3, exit = 0, players = 1, deathmatch = 0, timer = 0;
        (int, int, int)? start = null;
        bool monsters = false;
        GameVersion? gameversion = null;
        var cmds = new List<ticcmd_t[]>();
        var events = new List<RouteEvent>();
        int n = 0;
        foreach (string raw in lines)
        {
            n++;
            int hash = raw.IndexOf('#');
            string[] f = (hash >= 0 ? raw[..hash] : raw).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length == 0)
                continue;
            string where = $"{path}:{n}";
            switch (f[0])
            {
                case "iwad":
                    iwad = f[1];
                    continue;
                case "map":
                    map = f[1];
                    continue;
                case "pwad": // P2
                    if (f.Length != 2)
                        throw new FormatException($"{where}: expected pwad SET");
                    pwad = f[1];
                    continue;
                case "skill":
                    skill = Int(f[1]);
                    continue;
                case "exit":
                    exit = f.Length == 2 ? f[1] switch { "normal" => 1, "secret" => 2, _ => 0 } : 0;
                    if (exit == 0)
                        throw new FormatException($"{where}: expected exit normal|secret");
                    continue;
                case "start":
                    if (f.Length != 4)
                        throw new FormatException($"{where}: expected start X Y ANGLE");
                    start = (Int(f[1]), Int(f[2]), Int(f[3]));
                    continue;
                case "gameversion":
                    gameversion = f.Length == 2 ? GameVersions.Parse(f[1]) : null;
                    if (gameversion is null)
                        throw new FormatException($"{where}: expected gameversion "
                            + string.Join("|", System.Linq.Enumerable.Select(GameVersions.gameversions, v => v.Cmdline)));
                    continue;
                case "monsters":
                    if (f.Length != 1)
                        throw new FormatException($"{where}: expected monsters");
                    monsters = true;
                    continue;
                case "players": // T11.2
                    if (f.Length != 2 || !int.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out players) || players is < 1 or > World.MAXPLAYERS)
                        throw new FormatException($"{where}: expected players 1-{World.MAXPLAYERS}");
                    if (cmds.Count > 0 || events.Count > 0)
                        throw new FormatException($"{where}: the players header after a ticcmd or an event");
                    continue;
                case "deathmatch": // T11.2
                    if (f.Length != 2 || f[1] is not ("0" or "1" or "2"))
                        throw new FormatException($"{where}: expected deathmatch 0|1|2");
                    deathmatch = Int(f[1]);
                    continue;
                case "timer": // T11.4
                    if (f.Length != 2 || !int.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out timer) || timer is < 1 or > ushort.MaxValue)
                        throw new FormatException($"{where}: expected timer MINUTES (1-{ushort.MaxValue})");
                    continue;
                case "damage" or "alert" or "rocket":
                    events.Add(ParseEvent(f, cmds.Count, players, where));
                    continue;
            }
            int count = 1;
            if (f[^1].StartsWith('x'))
            {
                count = Int(f[^1][1..]);
                f = f[..^1];
            }
            // T11.2: one ticcmd per player in the game, separated by "|"
            bool separated = true;
            for (int k = 1; k < players && f.Length == (5 * players) - 1; k++)
                separated &= f[(5 * k) - 1] == "|";
            if (f.Length != (5 * players) - 1 || !separated)
            {
                throw new FormatException(players == 1 ? $"{where}: expected FORWARD SIDE TURN BUTTONS [xCOUNT]"
                    : $"{where}: expected {players} ticcmds FORWARD SIDE TURN BUTTONS separated by | [xCOUNT]");
            }
            var tic = new ticcmd_t[World.MAXPLAYERS];
            for (int k = 0; k < players; k++)
            {
                int[] v = [Int(f[5 * k]), Int(f[(5 * k) + 1]), Int(f[(5 * k) + 2]), Int(f[(5 * k) + 3])];
                for (int i = 0; i < 3; i++)
                {
                    if (v[i] < -128 || v[i] > 127)
                        throw new FormatException($"{where}: out of range");
                }
                if (v[3] < 0 || v[3] > 255)
                    throw new FormatException($"{where}: out of range");
                tic[k] = new ticcmd_t
                {
                    forwardmove = (sbyte)v[0],
                    sidemove = (sbyte)v[1],
                    angleturn = (short)(v[2] << 8), // a demo's angleturn byte
                    buttons = (byte)v[3],
                };
            }
            for (int i = 0; i < count; i++)
                cmds.Add(tic);
        }
        if (skill < 1 || skill > 5)
            throw new FormatException($"{path}: skill {skill} (1-5)");
        if (events.Count > 0 && events[^1].Tic >= cmds.Count)
            throw new FormatException($"{path}: an event after the last tic");
        var first = new ticcmd_t[cmds.Count];
        for (int i = 0; i < first.Length; i++)
            first[i] = cmds[i][0];
        return new RouteFile
        {
            Iwad = iwad,
            Pwad = pwad,
            Map = map,
            Skill = skill,
            Start = start,
            Exit = exit,
            Monsters = monsters,
            Events = events,
            GameVersion = gameversion,
            PlayersInGame = (1 << players) - 1,
            Deathmatch = deathmatch,
            Timer = timer,
            NetCmds = cmds,
            Cmds = first,
        };
    }

    /// <summary>
    /// A route event's line <paramref name="f"/> (T6.4): <c>damage X Y AMOUNT</c>,
    /// <c>alert</c> or <c>rocket</c>, before tic <paramref name="tic"/>, and
    /// (T11.2) a last <c>pN</c> naming its player (1 to <paramref name="players"/>; default <c>p1</c>).
    /// </summary>
    private static RouteEvent ParseEvent(string[] f, int tic, int players, string where)
    {
        int player = 0;
        if (f.Length > 1 && f[^1].StartsWith('p'))
        {
            if (!int.TryParse(f[^1][1..], NumberStyles.None, CultureInfo.InvariantCulture, out player) || player < 1 || player > players)
                throw new FormatException($"{where}: {f[^1]}: the event's player, p1-p{players}");
            player--;
            f = f[..^1];
        }
        switch (f[0])
        {
            case "damage":
                if (f.Length != 4 || Int(f[3]) < 1)
                    throw new FormatException($"{where}: expected damage X Y AMOUNT [pN] (AMOUNT at least 1)");
                return new RouteEvent(tic, RouteEventKind.Damage, Int(f[1]), Int(f[2]), Int(f[3]), player);
            case "alert":
                if (f.Length != 1)
                    throw new FormatException($"{where}: expected alert [pN]");
                return new RouteEvent(tic, RouteEventKind.Alert, 0, 0, 0, player);
            default:
                if (f.Length != 1)
                    throw new FormatException($"{where}: expected rocket [pN]");
                return new RouteEvent(tic, RouteEventKind.Rocket, 0, 0, 0, player);
        }
    }

    /// <summary>
    /// T11.2: the route's <c>ticcmd</c> and event lines as <see cref="Parse(IEnumerable{string}, string)"/>
    /// reads them: one line a tic of <paramref name="netcmds"/>' first
    /// <paramref name="players"/> commands (<c>|</c> between them; the turn
    /// is angleturn's high byte), a run of equal tics as one with <c>xCOUNT</c>,
    /// each of <paramref name="events"/> before its tic.
    /// </summary>
    public static string Lines(IReadOnlyList<ticcmd_t[]> netcmds, int players, IReadOnlyList<RouteEvent> events)
    {
        var sb = new System.Text.StringBuilder();
        for (int k = 0; k < netcmds.Count;)
        {
            foreach (RouteEvent e in events)
            {
                if (e.Tic == k)
                    sb.Append(e).Append('\n');
            }
            int n = 1;
            while (k + n < netcmds.Count && Same(netcmds[k + n], netcmds[k], players) && !HasEvent(events, k + n))
                n++;
            for (int i = 0; i < players; i++)
            {
                ticcmd_t c = netcmds[k][i];
                sb.Append(CultureInfo.InvariantCulture, $"{(i > 0 ? " | " : "")}{c.forwardmove} {c.sidemove} {c.angleturn >> 8} {c.buttons}");
            }
            if (n > 1)
                sb.Append(CultureInfo.InvariantCulture, $" x{n}");
            sb.Append('\n');
            k += n;
        }
        return sb.ToString();

        static bool Same(ticcmd_t[] a, ticcmd_t[] b, int players)
        {
            for (int i = 0; i < players; i++)
            {
                if (a[i].forwardmove != b[i].forwardmove || a[i].sidemove != b[i].sidemove || a[i].angleturn != b[i].angleturn || a[i].buttons != b[i].buttons)
                    return false;
            }
            return true;
        }

        static bool HasEvent(IReadOnlyList<RouteEvent> events, int tic)
        {
            foreach (RouteEvent e in events)
            {
                if (e.Tic == tic)
                    return true;
            }
            return false;
        }
    }

    /// <summary>The demo version <see cref="FromDemo"/> reads: v1.9's (g_game.c <c>VERSION</c>).</summary>
    public const int DemoVersion = 109;

    /// <summary>
    /// T10.6: the oldest demo version <see cref="FromDemo"/> also reads: v1.666's
    /// (Doom II v1.666's IWAD has its demos in it). v1.666 to v1.9 write the same
    /// header and ticcmds, and Chocolate Doom plays them alike (<c>-gameversion</c>
    /// changes only the version byte it expects for these), so the sim plays them as v1.9's.
    /// </summary>
    public const int OldestDemoVersion = 106;

    /// <summary>g_game.c <c>DEMOMARKER</c>: the byte that ends a demo's ticcmds.</summary>
    public const byte DEMOMARKER = 0x80;

    /// <summary>
    /// T6.13, the vanilla-input adapter: a v1.9 demo lump (e.g. DOOM1.WAD's
    /// <c>DEMO1</c>; T10.6: or v1.666's to v1.8's, <see cref="OldestDemoVersion"/>)
    /// as a route, as g_game.c's <c>G_DoPlayDemo</c> reads its
    /// header (version 109, skill, episode, map, deathmatch, respawn, fast,
    /// nomonsters, consoleplayer, playeringame[4]) and <c>G_ReadDemoTiccmd</c>
    /// each tic's <c>ticcmd</c> (<c>forwardmove</c>, <c>sidemove</c>, the
    /// angleturn byte, <c>buttons</c>) up to <see cref="DEMOMARKER"/>. The
    /// map is <c>ExMy</c>, or <c>MAPxx</c> with <paramref name="commercial"/>.
    /// T11.2: a multi-player demo (a netdemo when player 2 is in the game, as
    /// <c>G_DoPlayDemo</c> has it) has one <c>ticcmd</c> per player in the game
    /// each tic, in player order (<c>G_Ticker</c>'s <c>G_ReadDemoTiccmd</c> for each);
    /// its deathmatch, respawn and fast bytes are kept (<see cref="Settings"/>).
    /// A demo of another version, without its marker, without players or whose
    /// console player is not in the game is a <see cref="FormatException"/>.
    /// </summary>
    public static RouteFile FromDemo(ReadOnlySpan<byte> lump, string name, bool commercial = false)
    {
        if (lump.Length < 13)
            throw new FormatException($"{name}: a demo of {lump.Length} bytes (the header has 13)");
        if (lump[0] is < OldestDemoVersion or > DemoVersion)
            throw new FormatException($"{name}: demo version {lump[0]} (only v1.666's to v1.9's, {OldestDemoVersion}-{DemoVersion})");
        int skill = lump[1] + 1, episode = lump[2], map = lump[3];
        if (skill < 1 || skill > 5)
            throw new FormatException($"{name}: skill {lump[1]} (0-4)");
        if (lump[4] > 2)
            throw new FormatException($"{name}: deathmatch {lump[4]} (0-2)");
        int mask = 0;
        for (int i = 0; i < World.MAXPLAYERS; i++)
            mask |= lump[9 + i] != 0 ? 1 << i : 0;
        if (mask == 0 || lump[8] >= World.MAXPLAYERS || (mask & (1 << lump[8])) == 0)
            throw new FormatException($"{name}: consoleplayer {lump[8]}, players {lump[9]}{lump[10]}{lump[11]}{lump[12]}: the console player must be in the game");
        var cmds = new List<ticcmd_t[]>();
        int p = 13;
        bool partial = false;
        while (p < lump.Length && lump[p] != DEMOMARKER && !partial)
        {
            var tic = new ticcmd_t[World.MAXPLAYERS];
            for (int i = 0; i < World.MAXPLAYERS && !partial; i++)
            {
                if ((mask & (1 << i)) == 0)
                    continue;
                // G_ReadDemoTiccmd reads the marker before each player's command: a demo ends at any of them,
                // and vanilla's tic with it does not run
                if (p >= lump.Length || lump[p] == DEMOMARKER)
                {
                    partial = true;
                    break;
                }
                if (p + 4 > lump.Length)
                    throw new FormatException($"{name}: a ticcmd cut short at byte {p}");
                tic[i] = new ticcmd_t
                {
                    forwardmove = unchecked((sbyte)lump[p]),
                    sidemove = unchecked((sbyte)lump[p + 1]),
                    angleturn = (short)(unchecked((sbyte)lump[p + 2]) << 8), // G_ReadDemoTiccmd: ((unsigned char)*demo_p++)<<8
                    buttons = lump[p + 3],
                };
                p += 4;
            }
            if (!partial)
                cmds.Add(tic);
        }
        if (p >= lump.Length)
            throw new FormatException($"{name}: no DEMOMARKER at the end");
        var console = new ticcmd_t[cmds.Count];
        for (int i = 0; i < console.Length; i++)
            console[i] = cmds[i][lump[8]];
        return new RouteFile
        {
            Map = commercial ? string.Create(CultureInfo.InvariantCulture, $"MAP{map:00}") : string.Create(CultureInfo.InvariantCulture, $"E{episode}M{map}"),
            Skill = skill,
            Deathmatch = lump[4],
            Respawn = lump[5] != 0,
            Fast = lump[6] != 0,
            Monsters = lump[7] == 0,
            ConsolePlayer = lump[8],
            PlayersInGame = mask,
            NetCmds = cmds,
            Cmds = console,
        };
    }

    /// <summary>
    /// T11.2: the route as a v1.9 demo lump, as <c>tools/VanillaRef/routes.py</c>
    /// writes it for the reference (<see cref="FromDemo"/> reads it back):
    /// the header (skill, episode and map from <paramref name="episode"/> and
    /// <paramref name="map"/>, deathmatch, respawn, fast, nomonsters, the
    /// console player, <c>playeringame[]</c>), then each tic's commands of
    /// the players in the game (angleturn's high byte), then <see cref="DEMOMARKER"/>.
    /// </summary>
    public byte[] ToDemo(int episode, int map)
    {
        var data = new List<byte> { DemoVersion, (byte)(Skill - 1), (byte)episode, (byte)map, (byte)Deathmatch, (byte)(Respawn ? 1 : 0), (byte)(Fast ? 1 : 0), (byte)(Monsters ? 0 : 1), (byte)ConsolePlayer };
        for (int i = 0; i < World.MAXPLAYERS; i++)
            data.Add((byte)((PlayersInGame >> i) & 1));
        foreach (ticcmd_t[] tic in NetCmds)
        {
            for (int i = 0; i < World.MAXPLAYERS; i++)
            {
                if ((PlayersInGame & (1 << i)) != 0)
                    data.AddRange([unchecked((byte)tic[i].forwardmove), unchecked((byte)tic[i].sidemove), (byte)(tic[i].angleturn >> 8), tic[i].buttons]);
            }
        }
        data.Add(DEMOMARKER);
        return [.. data];
    }

    /// <summary>
    /// Runs the events of tic <paramref name="tic"/> (0-based) in
    /// <paramref name="world"/>, as the vanilla reference does before the
    /// players think in that tic's <c>P_Ticker</c>; call it before that tic's <c>G_Ticker</c>.
    /// </summary>
    public void RunEvents(World world, int tic)
    {
        foreach (RouteEvent e in Events)
        {
            if (e.Tic == tic)
                e.Run(world);
        }
    }

    private static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);
}

/// <summary>A <see cref="RouteEvent"/>'s kind (T6.4).</summary>
public enum RouteEventKind
{
    /// <summary><c>damage X Y AMOUNT</c>: player 1 hurts a thing as a shot would.</summary>
    Damage,

    /// <summary><c>alert</c>: player 1 makes a noise as a shot would.</summary>
    Alert,

    /// <summary><c>rocket</c> (T6.5): player 1 fires a rocket (<c>P_SpawnPlayerMissile(mo, MT_ROCKET)</c>, as <c>A_FireMissile</c> without the ammo).</summary>
    Rocket,
}

/// <summary>
/// T6.4: a scripted act of player 1 in a route, run at the start of tic
/// <see cref="Tic"/> (0-based) before the players think, in the vanilla
/// reference (<c>dump.c</c>'s <c>dump_pretic</c>) and the sim alike. Before
/// the player's weapons were ported (T6.6; <c>BT_ATTACK</c> fires them since)
/// these stood in for its shots, so that monster routes can wake and kill
/// monsters; they stay for scripted cases:
/// <see cref="RouteEventKind.Damage"/> calls <c>P_DamageMobj(thing, mo, mo, AMOUNT)</c>
/// (<c>mo</c> the player's mobj) on the first mobj in thinker order that
/// was spawned at map point (<see cref="X"/>, <see cref="Y"/>)
/// (<c>spawnpoint</c>) and is shootable and alive;
/// <see cref="RouteEventKind.Alert"/> calls <c>P_NoiseAlert(mo, mo)</c>, as
/// <c>P_FireWeapon</c> does (SPEC §12 T6.4); <see cref="RouteEventKind.Rocket"/>
/// calls <c>P_SpawnPlayerMissile(mo, MT_ROCKET)</c>, as <c>A_FireMissile</c>
/// does, without using ammo or making a noise (SPEC §12 T6.5).
/// T11.2: <see cref="Player"/> (0-based; a route line's last <c>pN</c>, default
/// player 1) is the player whose mobj acts.
/// </summary>
public readonly record struct RouteEvent(int Tic, RouteEventKind Kind, int X, int Y, int Amount, int Player = 0)
{
    /// <summary>Runs the event; throws <see cref="InvalidOperationException"/> when no thing fits a damage event.</summary>
    public void Run(World world)
    {
        world.G_DoNetReborns(); // T11.3: vanilla's G_Ticker reborns before P_Ticker's dump_pretic runs the events
        mobj_t mo = world.players[Player].mo ?? throw new InvalidOperationException($"RouteEvent: no player {Player + 1}");
        if (Kind == RouteEventKind.Alert)
        {
            world.P_NoiseAlert(mo, mo);
            return;
        }
        if (Kind == RouteEventKind.Rocket)
        {
            world.P_SpawnPlayerMissile(mo, mobjtype_t.MT_ROCKET);
            return;
        }
        foreach (mobj_t m in world.Mobjs())
        {
            if (m.spawnpoint.X == X && m.spawnpoint.Y == Y && (m.flags & mobjflag_t.MF_SHOOTABLE) != 0 && m.health > 0)
            {
                world.P_DamageMobj(m, mo, mo, Amount);
                return;
            }
        }
        throw new InvalidOperationException($"RouteEvent: tic {Tic + 1}: nothing shootable spawned at ({X}, {Y}) to damage");
    }

    /// <summary>The event's route line.</summary>
    public override string ToString() => Kind switch
    {
        RouteEventKind.Alert => "alert",
        RouteEventKind.Rocket => "rocket",
        _ => string.Create(CultureInfo.InvariantCulture, $"damage {X} {Y} {Amount}"),
    } + (Player > 0 ? string.Create(CultureInfo.InvariantCulture, $" p{Player + 1}") : "");
}
