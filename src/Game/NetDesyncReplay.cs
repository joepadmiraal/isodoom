using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using IsoDoom.Map;
using IsoDoom.Net;
using IsoDoom.Sim;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

/// <summary>
/// T11.11's follow-up (SPEC §12 "The desync tools"): replays one machine's
/// desync dumps (<c>NetDesyncLog.Write</c>): the world saved after the
/// last agreed window (<c>agreed-windowW.dsg</c>, <c>agreed-start.dsg</c>)
/// loaded on the dump's WAD, then every logged tic from the next one on with
/// the logged commands and players (<c>commands.txt</c>), as the netgame ran
/// them (<c>GameFlow.RunTic</c>'s quits, the special buttons' pause and
/// the cheats, <see cref="World.G_Ticker(ticcmd_t[])"/>). Each tic's
/// <see cref="World.Checksum"/> is set against the logged one
/// (<c>checksums.txt</c>) and its state dumped (<see cref="World.DumpState"/>)
/// into <c>replay.txt</c>: replayed on each machine's dumps (the same build
/// and WADs), <c>tools/NetDesync/compare.py</c> names the first tic and field
/// where the machines' replays, or a machine's replay and its own run, part.
/// Plain C# (the tool <c>tools/NetDesync</c> and the tests run it).
/// <para>
/// One level only: it stops at the level's end (an exit, <c>idclev</c>) or
/// where the log ends. It cannot know what a level script did to one
/// machine's world (a route's events, the forced <c>netdesync</c>) nor a
/// pause in force at the save (the flow's, not the world's): such a run
/// differs from its replay from that tic, which is what it is for.
/// </para>
/// </summary>
public static class NetDesyncReplay
{
    /// <summary>The replay's file, written beside the dumps by default.</summary>
    public const string FileName = "replay.txt";

    /// <summary>What a replay found.</summary>
    /// <param name="FirstTic">The first tic replayed (the one after the save).</param>
    /// <param name="LastTic">The last tic replayed (<paramref name="FirstTic"/> − 1 for none).</param>
    /// <param name="FirstMismatch">The first tic whose replayed checksum differs from the logged one, or null.</param>
    /// <param name="Stopped">Why the replay stopped before the log's end, or null.</param>
    public sealed record Result(int FirstTic, int LastTic, int? FirstMismatch, string? Stopped);

    /// <summary>A logged tic: its players and commands (<c>commands.txt</c>).</summary>
    public sealed record LoggedTic(int Tic, byte Mask, ticcmd_t[] Cmds);

    /// <summary>
    /// Replays the dumps in <paramref name="dir"/> on <paramref name="wad"/>
    /// (the game's IWAD and PWADs) and writes the replay to
    /// <paramref name="output"/> (default <c>DIR/replay.txt</c>).
    /// </summary>
    public static Result Run(WadArchive wad, string dir, string? output = null)
    {
        ArgumentNullException.ThrowIfNull(wad);
        ArgumentNullException.ThrowIfNull(dir);
        (int startTic, int player, string saveName) = ReadSummary(File.ReadAllText(Path.Combine(dir, "summary.txt")));
        if (!saveName.EndsWith(".dsg", StringComparison.Ordinal))
            throw new InvalidDataException($"{dir}: no agreed save kept ({saveName}): nothing to replay from");
        int window = saveName == "agreed-start.dsg" ? -1 // NetDesyncLog.StartWindow
            : int.Parse(saveName["agreed-window".Length..^".dsg".Length], NumberStyles.Integer, CultureInfo.InvariantCulture);
        int firstTic = startTic + (window + 1) * NetProtocol.WindowTics;

        SaveGameFile save = SaveGameFile.Read(File.ReadAllBytes(Path.Combine(dir, saveName)));
        Textures? textures = wad.W_CheckNumForName("TEXTURE1") >= 0 ? Textures.R_InitTextures(wad) : null;
        World world = save.LoadWorld(Level.Load(wad, save.Map), textures, PicAnims.FlatNames(wad));
        world.map31exists = wad.W_CheckNumForName("MAP31") >= 0;
        world.consoleplayer = player;

        Dictionary<int, ulong> logged = ReadChecksums(File.ReadAllLines(Path.Combine(dir, "checksums.txt")));
        List<LoggedTic> tics = ReadCommands(File.ReadAllLines(Path.Combine(dir, "commands.txt")));
        using var writer = new StreamWriter(output ?? Path.Combine(dir, FileName), false, new UTF8Encoding(false));
        writer.Write(string.Create(CultureInfo.InvariantCulture,
            $"# IsoDoom desync replay (T11.11): {saveName} ({save.Map}, leveltime {save.LevelTime}) from tic {firstTic}; per tic: tic T checksum C logged L, then World.DumpState\n"));
        return Replay(world, tics, logged, firstTic, writer);
    }

    /// <summary>
    /// Replays <paramref name="tics"/> from <paramref name="firstTic"/> on
    /// <paramref name="world"/> (the state after the tic before it), checking
    /// each tic's checksum against <paramref name="logged"/> and writing the
    /// tics to <paramref name="writer"/>.
    /// </summary>
    public static Result Replay(World world, IReadOnlyList<LoggedTic> tics, IReadOnlyDictionary<int, ulong> logged, int firstTic, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(tics);
        ArgumentNullException.ThrowIfNull(logged);
        ArgumentNullException.ThrowIfNull(writer);
        int expected = firstTic, last = firstTic - 1;
        int? mismatch = null;
        string? stopped = null;
        bool paused = false;
        var dump = new StringBuilder();
        foreach (LoggedTic t in tics)
        {
            if (t.Tic < firstTic)
                continue;
            if (t.Tic != expected)
            {
                stopped = string.Create(CultureInfo.InvariantCulture, $"tic {expected} is not in the log");
                break;
            }
            // GameFlow.RunTic: the players the tic leaves out quit first (Chocolate Doom's RunTic)
            for (int i = 0; i < World.MAXPLAYERS; i++)
            {
                if (world.playeringame[i] && (t.Mask & (1 << i)) == 0)
                    world.PlayerQuitGame(i);
            }
            // GameFlow.G_Ticker: the cheats (ST_DoCheat), then the pause, then the level's tic
            for (int i = 0; i < World.MAXPLAYERS; i++)
            {
                if (!world.playeringame[i] || world.players[i].mo is null)
                    continue;
                cheat_t cheat = World.ST_TiccmdCheat(t.Cmds[i]);
                if (cheat == cheat_t.cht_clev)
                    stopped = string.Create(CultureInfo.InvariantCulture, $"player {i + 1}'s idclev at tic {t.Tic} changes the level");
                else if (cheat is not (cheat_t.cht_none or cheat_t.cht_mus))
                    world.ST_Cheat(i, cheat);
            }
            if (stopped is not null)
                break;
            for (int i = 0; i < World.MAXPLAYERS; i++)
            {
                if (world.playeringame[i] && (t.Cmds[i].buttons & (buttoncode_t.BT_SPECIAL | buttoncode_t.BT_SPECIALMASK)) == (buttoncode_t.BT_SPECIAL | buttoncode_t.BTS_PAUSE))
                    paused = !paused;
            }
            if (!paused)
                world.G_Ticker(t.Cmds);
            world.HU_TakeMessages();
            world.events.Clear();

            ulong checksum = world.Checksum();
            bool known = logged.TryGetValue(t.Tic, out ulong log);
            if (known && log != checksum)
                mismatch ??= t.Tic;
            writer.Write(string.Create(CultureInfo.InvariantCulture,
                $"tic {t.Tic} checksum {checksum:x16} logged {(known ? log.ToString("x16", CultureInfo.InvariantCulture) : "none")}{(known && log != checksum ? " DIFFERS" : "")}\n"));
            dump.Clear();
            world.DumpState(dump);
            writer.Write(dump);
            last = t.Tic;
            expected++;
            if (world.gameaction != gameaction_t.ga_nothing)
            {
                stopped = string.Create(CultureInfo.InvariantCulture, $"the level ended at tic {t.Tic} ({world.gameaction})");
                break;
            }
        }
        return new Result(firstTic, last, mismatch, stopped);
    }

    /// <summary>Reads <c>summary.txt</c>'s start tic, player (0-based) and save name.</summary>
    public static (int StartTic, int Player, string Save) ReadSummary(string summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        int startTic = -1, player = 0;
        string save = "none kept";
        foreach (string line in summary.Split('\n'))
        {
            string l = line.Trim();
            if (l.StartsWith("player: ", StringComparison.Ordinal))
                player = int.Parse(l["player: ".Length..].Split(' ')[0], NumberStyles.Integer, CultureInfo.InvariantCulture) - 1;
            else if (l.StartsWith("game: ", StringComparison.Ordinal) && l.IndexOf("start tic ", StringComparison.Ordinal) is int at and >= 0)
                startTic = int.Parse(l[(at + "start tic ".Length)..].Split(',')[0], NumberStyles.Integer, CultureInfo.InvariantCulture);
            else if (l.StartsWith("save: ", StringComparison.Ordinal))
                save = l["save: ".Length..].Split(' ')[0];
        }
        if (startTic < 0)
            throw new InvalidDataException("summary.txt: no start tic");
        return (startTic, player, save);
    }

    /// <summary>Reads <c>checksums.txt</c>: each tic's checksum.</summary>
    public static Dictionary<int, ulong> ReadChecksums(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var checksums = new Dictionary<int, ulong>();
        foreach (string line in lines)
        {
            if (line.StartsWith('#') || line.Length == 0)
                continue;
            string[] f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            checksums[int.Parse(f[0], CultureInfo.InvariantCulture)] = ulong.Parse(f[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        return checksums;
    }

    /// <summary>Reads <c>commands.txt</c>: each tic's players and commands, in order.</summary>
    public static List<LoggedTic> ReadCommands(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var tics = new List<LoggedTic>();
        foreach (string line in lines)
        {
            if (line.StartsWith('#') || line.Length == 0)
                continue;
            string[] parts = line.Split('|');
            string[] head = parts[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cmds = new ticcmd_t[World.MAXPLAYERS];
            for (int k = 1; k < parts.Length; k++)
            {
                string[] f = parts[k].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                int p = int.Parse(f[0].AsSpan(1), CultureInfo.InvariantCulture) - 1;
                cmds[p] = new ticcmd_t
                {
                    forwardmove = sbyte.Parse(f[1], CultureInfo.InvariantCulture),
                    sidemove = sbyte.Parse(f[2], CultureInfo.InvariantCulture),
                    angleturn = short.Parse(f[3], CultureInfo.InvariantCulture),
                    buttons = byte.Parse(f[4], CultureInfo.InvariantCulture),
                    chatchar = byte.Parse(f[5], CultureInfo.InvariantCulture),
                    consistancy = short.Parse(f[6], CultureInfo.InvariantCulture),
                };
            }
            tics.Add(new LoggedTic(int.Parse(head[0], CultureInfo.InvariantCulture), byte.Parse(head[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture), cmds));
        }
        return tics;
    }
}
