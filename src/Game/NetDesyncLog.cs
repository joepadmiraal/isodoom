using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using IsoDoom.Net;
using IsoDoom.Sim;

namespace IsoDoom.Game;

/// <summary>
/// T11.11: what a netgame keeps for a desync's dumps (SPEC §12 T11.11): for
/// the last <see cref="KeptTics"/> tics, each tic's players and commands, its
/// <c>World.Checksum()</c> and the players' consistancy; and the world saved
/// (the save format, <see cref="SaveGameFile"/>) at the end of each of the
/// last <see cref="KeptWindows"/> checksum windows and at the game's start.
/// When the server announces a desync in window W, every window before W
/// agreed, so the save at W − 1's end (the start's for W = 0) is the last
/// state every machine shared: <see cref="Write"/> puts it, the checksums and
/// the commands in a directory of their own. Plain C#; the scene feeds it a
/// tic at a time (the level scene's <c>RunNetTic</c>).
/// </summary>
public sealed class NetDesyncLog
{
    /// <summary>The checksum windows whose end saves are kept (the desync's window lies a few behind the last tic run: the others' reports and a round trip).</summary>
    public const int KeptWindows = 8;

    /// <summary>The tics whose checksums and commands are kept.</summary>
    public const int KeptTics = KeptWindows * NetProtocol.WindowTics;

    /// <summary>The window number of the save made before the game's first tic.</summary>
    public const int StartWindow = -1;

    private readonly int[] _tics = new int[KeptTics];
    private readonly byte[] _masks = new byte[KeptTics];
    private readonly ticcmd_t[] _cmds = new ticcmd_t[KeptTics * NetProtocol.MaxPlayers];
    private readonly ulong[] _checksums = new ulong[KeptTics];
    private readonly short[] _consistancy = new short[KeptTics * NetProtocol.MaxPlayers];
    private readonly Kept[] _saves = new Kept[KeptWindows + 1];
    private int _count;
    private int _next;
    private int _saveNext;

    /// <summary>A log of the game <paramref name="start"/> began.</summary>
    public NetDesyncLog(NetStart start)
    {
        ArgumentNullException.ThrowIfNull(start);
        Start = start;
        for (int i = 0; i < _saves.Length; i++)
            _saves[i] = new Kept();
    }

    /// <summary>
    /// A kept save: the given bytes, or a world's state (<see cref="World.P_ArchiveGame"/>)
    /// in a stream reused from window to window (no garbage a window but the
    /// archive's own), made a save file's bytes only when the dumps are written.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "A MemoryStream holds no unmanaged resource; it lives as long as the log")]
    private sealed class Kept
    {
        public int Window = int.MinValue;
        public byte[]? Bytes;
        public readonly MemoryStream State = new();
        public bool HasState;
        public string Map = "";
        public IsoDoom.Wad.GameMode GameMode;
        public skill_t Skill;
        public int LevelTime;

        public bool Empty => Bytes is null && !HasState;

        public byte[] Save() => Bytes ?? new SaveGameFile(string.Create(CultureInfo.InvariantCulture, $"NET WINDOW {Window}"), Map, GameMode, Skill, LevelTime, State.ToArray()).ToBytes();
    }

    /// <summary>The game's Start.</summary>
    public NetStart Start { get; }

    /// <summary>The last tic logged (<see cref="NetStart.StartTic"/> − 1 before any).</summary>
    public int LastTic => _count == 0 ? Start.StartTic - 1 : _tics[(_next + KeptTics - 1) % KeptTics];

    /// <summary>Logs tic <paramref name="tic"/>: its players, every player's command, the world's checksum after it and the players' consistancy.</summary>
    public void Tic(int tic, byte mask, ReadOnlySpan<ticcmd_t> cmds, ulong checksum, ReadOnlySpan<short> consistancy)
    {
        if (cmds.Length < NetProtocol.MaxPlayers || consistancy.Length < NetProtocol.MaxPlayers)
            throw new ArgumentException("One command and one consistancy a player.");
        int i = _next;
        _tics[i] = tic;
        _masks[i] = mask;
        cmds[..NetProtocol.MaxPlayers].CopyTo(_cmds.AsSpan(i * NetProtocol.MaxPlayers, NetProtocol.MaxPlayers));
        _checksums[i] = checksum;
        consistancy[..NetProtocol.MaxPlayers].CopyTo(_consistancy.AsSpan(i * NetProtocol.MaxPlayers, NetProtocol.MaxPlayers));
        _next = (_next + 1) % KeptTics;
        _count = Math.Min(_count + 1, KeptTics);
    }

    /// <summary>Keeps the save <paramref name="save"/> (a save file's bytes) made after window <paramref name="window"/>'s last tic (<see cref="StartWindow"/>: before the first tic).</summary>
    public void Window(int window, byte[] save)
    {
        ArgumentNullException.ThrowIfNull(save);
        Kept k = _saves[_saveNext];
        (k.Window, k.Bytes, k.HasState) = (window, save, false);
        _saveNext = (_saveNext + 1) % _saves.Length;
    }

    /// <summary>
    /// Keeps <paramref name="world"/>'s state after window <paramref name="window"/>'s
    /// last tic (<see cref="StartWindow"/>: before the first tic), as a save
    /// (<see cref="SaveGameFile.Write"/>'s, made when the dumps are written).
    /// </summary>
    public void Window(int window, World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        Kept k = _saves[_saveNext];
        (k.Window, k.Bytes, k.HasState) = (window, null, false);
        k.State.SetLength(0);
        using (var w = new BinaryWriter(k.State, Encoding.UTF8, leaveOpen: true))
            world.P_ArchiveGame(w);
        (k.HasState, k.Map, k.GameMode, k.Skill, k.LevelTime) = (true, world.level.Name, world.gamemode, world.gameskill, world.leveltime);
        _saveNext = (_saveNext + 1) % _saves.Length;
    }

    /// <summary>The save of the last window every machine agreed on before <paramref name="desync"/> (its window − 1, or the start), if still kept; else the latest kept before it, or null.</summary>
    public (int Window, byte[] Save)? AgreedSave(NetDesync desync)
    {
        Kept? latest = null;
        foreach (Kept k in _saves)
        {
            if (k.Empty)
                continue;
            if (k.Window == desync.Window - 1)
                return (k.Window, k.Save());
            if (k.Window < desync.Window && (latest is null || k.Window > latest.Window))
                latest = k;
        }
        return latest is null ? null : (latest.Window, latest.Save());
    }

    /// <summary>
    /// Writes the dumps into a new directory under <paramref name="root"/>
    /// (named for the time and <paramref name="player"/>) and returns its path:
    /// <c>summary.txt</c> (the desync, the game, the tics run, the files),
    /// <c>checksums.txt</c> (a line a tic: tic, checksum, the four players'
    /// consistancy), <c>commands.txt</c> (a line a tic: tic, players, each
    /// player's <c>forwardmove sidemove angleturn buttons chatchar
    /// consistancy</c>) for the tics kept, and the save at the last agreed
    /// window (<c>agreed-windowW.dsg</c>, <c>agreed-start.dsg</c>; the save format),
    /// and (T11.11's follow-up) with <paramref name="world"/>, the world the
    /// game stopped with, field by field (<c>state.txt</c>: the last tic, its
    /// checksum, <see cref="World.DumpState"/>), for <c>tools/NetDesync/compare.py</c>.
    /// T11.9c: <paramref name="names"/>, each slot's player name (the lobby's), names the players in the summary.
    /// T11.12b: <paramref name="delay"/> and <paramref name="delayChanges"/>, the input delay when the game stopped and how often it changed (0: the Start's, unchanged).
    /// </summary>
    public string Write(string root, NetDesync desync, int player, string build, int firstBadTic, DateTime now, World? world = null,
        IReadOnlyList<string>? names = null, int delay = 0, int delayChanges = 0)
    {
        ArgumentNullException.ThrowIfNull(root);
        string dir = Path.Combine(root, string.Create(CultureInfo.InvariantCulture, $"{now:yyyyMMdd-HHmmss}-player{player + 1}"));
        for (int n = 2; Directory.Exists(dir); n++)
            dir = Path.Combine(root, string.Create(CultureInfo.InvariantCulture, $"{now:yyyyMMdd-HHmmss}-player{player + 1}-{n}"));
        Directory.CreateDirectory(dir);

        var checksums = new StringBuilder("# tic checksum consistancy1 consistancy2 consistancy3 consistancy4 (after the tic)\n");
        var commands = new StringBuilder("# tic players(mask) then per player in the game: pN forwardmove sidemove angleturn buttons chatchar consistancy\n");
        int first = (_next + KeptTics - _count) % KeptTics;
        for (int k = 0; k < _count; k++)
        {
            int i = (first + k) % KeptTics;
            checksums.Append(CultureInfo.InvariantCulture, $"{_tics[i]} {_checksums[i]:x16}");
            for (int p = 0; p < NetProtocol.MaxPlayers; p++)
                checksums.Append(CultureInfo.InvariantCulture, $" {_consistancy[i * NetProtocol.MaxPlayers + p]}");
            checksums.Append('\n');
            commands.Append(CultureInfo.InvariantCulture, $"{_tics[i]} {_masks[i]:x}");
            for (int p = 0; p < NetProtocol.MaxPlayers; p++)
            {
                if ((_masks[i] & (1 << p)) == 0)
                    continue;
                ticcmd_t c = _cmds[i * NetProtocol.MaxPlayers + p];
                commands.Append(CultureInfo.InvariantCulture, $" | p{p + 1} {c.forwardmove} {c.sidemove} {c.angleturn} {c.buttons} {c.chatchar} {c.consistancy}");
            }
            commands.Append('\n');
        }
        File.WriteAllText(Path.Combine(dir, "checksums.txt"), checksums.ToString());
        File.WriteAllText(Path.Combine(dir, "commands.txt"), commands.ToString());
        if (world is not null)
        {
            var state = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"tic {LastTic} checksum {world.Checksum():x16}\n"));
            world.DumpState(state);
            File.WriteAllText(Path.Combine(dir, "state.txt"), state.ToString());
        }

        string saveName = "none kept";
        if (AgreedSave(desync) is (int window, byte[] save))
        {
            saveName = window == StartWindow ? "agreed-start.dsg" : string.Create(CultureInfo.InvariantCulture, $"agreed-window{window}.dsg");
            File.WriteAllBytes(Path.Combine(dir, saveName), save);
        }
        NetGameSettings s = Start.Settings;
        var named = new List<string>();
        for (int p = 0; p < NetProtocol.MaxPlayers; p++)
        {
            if ((Start.PlayerMask & (1 << p)) != 0)
                named.Add(string.Create(CultureInfo.InvariantCulture, $"{p + 1} {(names is not null && p < names.Count && names[p].Length > 0 ? names[p] : NetProtocol.CleanName(null, p))}"));
        }
        string summary = string.Create(CultureInfo.InvariantCulture,
            $"""
            IsoDoom netgame desync dump (T11.11)
            build: {build}
            player: {player + 1} (slot {player})
            names: {string.Join(", ", named)}
            game: {NetSession.MapName(s)}, skill {(int)s.Spawn.gameskill + 1}, deathmatch {s.Spawn.deathmatch}, players {Start.PlayerMask:x}, start tic {Start.StartTic}, delay {Start.Delay}
            delay: {(Start.Adaptive ? "adaptive" : "fixed")}, {(delay > 0 ? delay : Start.Delay)} tic(s) when the game stopped, {delayChanges} change(s) (only commands carry it: replay needs nothing more)
            desync: window {desync.Window}, tics {desync.FirstTic}-{desync.LastTic} (the first window whose checksums differed; every window before it agreed)
            first consistency failure seen here: {(firstBadTic < 0 ? "none" : firstBadTic.ToString(CultureInfo.InvariantCulture))}
            tics logged: {(_count == 0 ? "none" : $"{_tics[first]}-{LastTic}")} (the game stopped after tic {LastTic})
            save: {saveName}{(saveName.EndsWith(".dsg", StringComparison.Ordinal) ? " (the world after the last agreed window; the save format, SaveGameFile)" : "")}
            files: checksums.txt, commands.txt{(world is not null ? ", state.txt" : "")}

            """);
        File.WriteAllText(Path.Combine(dir, "summary.txt"), summary);
        return dir;
    }
}
