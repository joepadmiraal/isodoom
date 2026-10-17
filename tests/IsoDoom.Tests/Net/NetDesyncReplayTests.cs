using System;
using System.IO;
using System.Linq;
using System.Text;
using IsoDoom.Game;
using IsoDoom.Net;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Net;

/// <summary>
/// T11.11's follow-up: the desync dumps replayed (<see cref="NetDesyncReplay"/>,
/// <c>tools/NetDesync</c>) and the stopped world dumped field by field
/// (<see cref="World.DumpState"/>, <c>state.txt</c>): two machines of a co-op
/// game on the synthetic IWAD, one taking a <c>P_Random</c> of its own (the
/// level script's <c>netdesync</c>); each machine's dumps replayed from the
/// last agreed save.
/// </summary>
public class NetDesyncReplayTests
{
    private static WadArchive Synthetic() => new([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);

    [Fact]
    public void A_replay_reproduces_the_machine_that_kept_to_its_commands_and_not_the_one_that_did_not()
    {
        WadArchive wad = Synthetic();
        var spawn = new SpawnSettings(GameMode.shareware, skill_t.sk_medium, netgame: true, playersingame: NetSim.FirstPlayers(3));
        var start = new NetStart(0, 0b111, 0, 2, new NetGameSettings(spawn, Tweaks.TopDown, Episode: 1, Map: 1));
        World[] worlds = [NetSim.MakeWorld(wad, "E1M1", spawn, Tweaks.TopDown, 0), NetSim.MakeWorld(wad, "E1M1", spawn, Tweaks.TopDown, 1)];
        NetDesyncLog[] logs = [new(start), new(start)];
        foreach ((World w, NetDesyncLog log) in worlds.Zip(logs))
            log.Window(NetDesyncLog.StartWindow, w);

        const int Bump = 100, Quit = 80;
        var cmds = new ticcmd_t[NetProtocol.MaxPlayers];
        var consistancy = new short[NetProtocol.MaxPlayers];
        for (int tic = 0; tic < 4 * NetProtocol.WindowTics; tic++)
        {
            byte mask = tic < Quit ? (byte)0b111 : (byte)0b011; // player 3 leaves before tic 80
            for (int i = 0; i < 3; i++)
            {
                cmds[i] = (mask & (1 << i)) != 0 ? LockstepHarness.Wander(i, tic, fire: true) : default;
                cmds[i].buttons &= unchecked((byte)~buttoncode_t.BT_USE); // no exit switch: one level
            }
            for (int k = 0; k < worlds.Length; k++)
            {
                World w = worlds[k];
                if (tic == Bump && k == 1)
                    w.P_Random(); // the forced desync: machine 2's world alone
                for (int i = 0; i < World.MAXPLAYERS; i++)
                {
                    if (w.playeringame[i] && (mask & (1 << i)) == 0)
                        w.PlayerQuitGame(i);
                }
                w.G_Ticker(cmds);
                logs[k].Tic(tic, mask, cmds, w.Checksum(), consistancy);
                if ((tic + 1) % NetProtocol.WindowTics == 0)
                    logs[k].Window(tic / NetProtocol.WindowTics, w);
            }
        }
        Assert.NotEqual(worlds[0].Checksum(), worlds[1].Checksum());
        Assert.Equal(gameaction_t.ga_nothing, worlds[0].gameaction);

        string root = Path.Combine(Path.GetTempPath(), $"isodoom-netdesync-replay-{Environment.ProcessId}-{Guid.NewGuid():N}");
        try
        {
            var desync = new NetDesync(2, 70, 104); // window 2: tic 100's
            var when = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Local);
            string[] dirs = [.. Enumerable.Range(0, 2).Select(k => logs[k].Write(root, desync, k, "test", -1, when, worlds[k]))];

            // the stopped world, field by field: the monsters the extra random moved since
            string[] s0 = File.ReadAllLines(Path.Combine(dirs[0], "state.txt")), s1 = File.ReadAllLines(Path.Combine(dirs[1], "state.txt"));
            Assert.Equal($"tic 139 checksum {worlds[0].Checksum():x16}", s0[0]);
            string[] differ = [.. s0.Zip(s1).Where(p => p.First != p.Second).Select(p => p.First)];
            Assert.StartsWith("tic 139 checksum ", differ[0], StringComparison.Ordinal);
            Assert.Contains(differ, l => l.StartsWith("mobj ", StringComparison.Ordinal));
            Assert.Contains("state.txt", File.ReadAllText(Path.Combine(dirs[0], "summary.txt")), StringComparison.Ordinal);

            // machine 1 kept to its commands: its replay from window 1's save reproduces it; machine 2's from tic 100 on does not
            NetDesyncReplay.Result r0 = NetDesyncReplay.Run(wad, dirs[0]);
            NetDesyncReplay.Result r1 = NetDesyncReplay.Run(wad, dirs[1]);
            Assert.Equal(new NetDesyncReplay.Result(70, 139, null, null), r0);
            Assert.Equal(new NetDesyncReplay.Result(70, 139, Bump, null), r1);
            string[] replay0 = File.ReadAllLines(Path.Combine(dirs[0], NetDesyncReplay.FileName));
            string[] replay1 = File.ReadAllLines(Path.Combine(dirs[1], NetDesyncReplay.FileName));
            Assert.StartsWith("# IsoDoom desync replay", replay0[0], StringComparison.Ordinal);
            Assert.StartsWith("tic 70 checksum ", replay0[1], StringComparison.Ordinal);
            Assert.Contains(replay1, l => l.StartsWith("tic 100 checksum ", StringComparison.Ordinal) && l.EndsWith(" DIFFERS", StringComparison.Ordinal));
            // the replays' states are the same (the same commands from the same save); the quit replayed (player 3 not dumped after it)
            Assert.Equal(replay0.Skip(1).Where(l => !l.StartsWith("tic ", StringComparison.Ordinal)), replay1.Skip(1).Where(l => !l.StartsWith("tic ", StringComparison.Ordinal)));
            Assert.DoesNotContain(replay0.SkipWhile(l => !l.StartsWith($"tic {Quit} ", StringComparison.Ordinal)), l => l.StartsWith("player 3 ", StringComparison.Ordinal));
            Assert.Contains(replay0.TakeWhile(l => !l.StartsWith($"tic {Quit} ", StringComparison.Ordinal)), l => l.StartsWith("player 3 ", StringComparison.Ordinal));
            var last = new StringBuilder();
            worlds[0].DumpState(last);
            Assert.Equal(last.ToString().TrimEnd('\n').Split('\n'), replay0.SkipWhile(l => !l.StartsWith("tic 139 ", StringComparison.Ordinal)).Skip(1));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_dump_files_parse()
    {
        Assert.Equal((0, 1, "agreed-window3.dsg"), NetDesyncReplay.ReadSummary(
            "player: 2 (slot 1)\ngame: E1M1, skill 3, deathmatch 1, players 3, start tic 0, delay 2\nsave: agreed-window3.dsg (the world)\n"));
        NetDesyncReplay.LoggedTic t = Assert.Single(NetDesyncReplay.ReadCommands(["# header", "699 5 | p1 25 0 699 0 0 7 | p3 -50 -24 -3 129 2 0"]));
        Assert.Equal((699, (byte)5), (t.Tic, t.Mask));
        Assert.Equal((sbyte)25, t.Cmds[0].forwardmove);
        Assert.Equal((short)7, t.Cmds[0].consistancy);
        Assert.Equal(((sbyte)-50, (sbyte)-24, (short)-3, (byte)129, (byte)2), (t.Cmds[2].forwardmove, t.Cmds[2].sidemove, t.Cmds[2].angleturn, t.Cmds[2].buttons, t.Cmds[2].chatchar));
        Assert.Equal(0x57481387124e5902UL, NetDesyncReplay.ReadChecksums(["# x", "141 57481387124e5902 28625 14812 0 0"])[141]);
    }
}
