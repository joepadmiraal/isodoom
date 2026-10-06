using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IsoDoom.Tests.Sim;
using IsoDoom.Wad;

namespace IsoDoom.Tools.RouteGen;

/// <summary>
/// <c>dotnet run --project tools/RouteGen -- NAME [OUT.route]</c>: plays the
/// steering script NAME (<see cref="Scripts"/>) in the sim and writes its
/// ticcmds as a route (default <c>tests/IsoDoom.Tests/Sim/Routes/NAME.route</c>),
/// after replaying them in a fresh world to check that the planning changed
/// nothing; then run <c>tools/VanillaRef/routes.sh</c> on it. The steps are
/// logged to stderr. DOOM1.WAD's scripts need the WAD (<c>wads/</c> or
/// <c>ISODOOM_DOOM1_WAD</c>); never commit their dumps (T4.8). T5.5.
/// </summary>
public static class Program
{
    private static string RepoRoot()
    {
        for (string? dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "IsoDoom.sln")))
                return dir;
        }
        for (string? dir = Directory.GetCurrentDirectory(); dir != null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "IsoDoom.sln")))
                return dir;
        }
        throw new InvalidOperationException("Repo root (IsoDoom.sln) not found.");
    }

    private static WadArchive Doom1() =>
        WadArchive.Open(Environment.GetEnvironmentVariable("ISODOOM_DOOM1_WAD") is { Length: > 0 } p ? p : Path.Combine(RepoRoot(), "wads", "DOOM1.WAD"));

    private static WadArchive TestMapWad(string name) =>
        new(new[] { WadFile.FromBytes(RouteTestMaps.Get(name).Build(), name + ".wad") });

    private sealed record Script(Func<WadArchive> Wad, string Map, string Header, Action<Steer, Action<string>> Run, (int X, int Y, int Angle)? Start = null);

    /// <summary>The steering scripts by route name.</summary>
    private static readonly SortedDictionary<string, Script> Scripts = new(StringComparer.Ordinal)
    {
        ["e1m1-exit"] = new(Doom1, "E1M1", """
# DOOM1.WAD E1M1 (T5.5): from the start to the exit switch, through every
# special on the way. The first door (sector 4); across line 195 (WR lift
# 88, tag 2: the lift, sector 70, lowers and comes back); the secret door
# (sector 68) and the door to the exit corridor (76); across line 308 (W1
# turbo lower 36: sector 59 lowers, opening the way to the lift). Back
# through door 76 to the nukage room, across line 195 again and on to the
# lift while it is down (running over the lowered sector 59); ride it up
# to the ledge (sector 62), drop into the nukage, then doors 76 and 81 and
# the exit switch (line 330, SW1STRTN -> SW2STRTN), the last tic. Written
# by tools/RouteGen (e1m1-exit).
iwad doom1

""", (g, Log) =>
        {
            // Door 4 (lines 151/152, x 1536..1552, y -2560..-2432), from the west.
            g.GoTo(1500, -2496);
            Log("at door 4");
            g.Door(4, 1544, -2496);
            // Into the nukage room; across line 195 (2752,-3048)->(3048,-2880): the lift (sector 70) lowers.
            g.GoTo(1600, -2496);
            Log("through door 4");
            g.GoTo(2900, -2900, 12);
            Log("near line 195 " + g.Heights(70));
            g.GoTo(2900, -3100, 12);
            Log("across line 195 " + g.Heights(70));
            // Door 68 (lines 247/248, x 2912..2944, y -3904..-3776): from the east side (front 56 of line 247).
            g.GoTo(2980, -3840, 6);
            Log("at door 68 " + g.Heights(70, 68));
            g.Door(68, 2944, -3840);
            g.GoTo(2890, -3840);
            Log("through door 68");
            g.GoTo(3008, -3990);
            Log("at door 76 " + g.Heights(76));
            g.Door(76, 3008, -4024);
            g.GoTo(3008, -4100);
            Log("through door 76");
            g.GoTo(3008, -4250);
            Log("across line 308 " + g.Heights(59));
            // Back north to the lift: door 76 from the south, door 68 from the west.
            g.GoTo(3008, -4060, 6);
            Log("at door 76 again " + g.Heights(76));
            g.Door(76, 3008, -4024);
            g.GoTo(3008, -3960);
            g.GoTo(2980, -2870, 12);
            Log("north of line 195 again " + g.Heights(70, 59));
            g.WaitIdle(70);
            Log("lift idle " + g.Heights(70, 59));
            g.Assume[70] = -48;
            g.GoTo(3552, -3872, 12, 50);
            g.Assume.Clear();
            Log("on the lift " + g.Heights(70));
            g.WaitIdle(70);
            Log("lift up " + g.Heights(70));
            g.GoTo(3650, -3700, 12);
            Log("on the ledge");
            g.GoTo(3150, -3300, 12);
            Log("in the nukage");
            g.GoTo(3008, -3990, 6);
            Log("at door 76 (3) " + g.Heights(76));
            g.Door(76, 3008, -4024);
            g.GoTo(3008, -4100);
            g.GoTo(3008, -4600, 6);
            Log("at door 81 " + g.Heights(59, 81));
            g.Door(81, 3008, -4640);
            g.GoTo(3008, -4700);
            g.GoTo(2960, -4768);
            Log("at the exit switch");
            g.Face(2900, -4768);
            Log("before press");
            g.Tic(0, 0, 0, 2); // the last tic: vanilla's G_ExitLevel ends the level (and the dump) here
            Log("textures " + g.w.sides[g.w.lines[330].sidenum[0]].midtexture + " " + string.Join(",", g.w.unported));
        }),
        ["e1m2-lifts"] = new(Doom1, "E1M2", """
# DOOM1.WAD E1M2 (T5.5): the two lifts near the start, both ways. Lift 1
# (sector 89, tag 1): into sector 85 across line 315 (WR 88), onto the
# lift while it is down, ride it up, off east across line 309 (WR 88: it
# lowers and comes back empty), back over it to 85 (it lowers again), then
# the SR switch line 313 (62) once it is idle. Lift 4 (sector 109, tag 4):
# across line 289 (WR 88) into sector 107, onto the lift while it is down,
# ride it up, off north across line 288 (WR 88), back down over it into
# sector 108, then the SR line 360 (62) once it is idle, and wait for it.
# Written by tools/RouteGen (e1m2-lifts).
iwad doom1
map E1M2

""", (g, Log) =>
        {
            // Lift 1 (sector 89, tag 1): into sector 85 across line 315 (WR 88).
            g.GoTo(-640, 100);
            Log("north of line 315 " + g.Heights(89));
            g.Assume[89] = 24;
            g.GoTo(-640, -64, 8, 25);
            g.Assume.Clear();
            Log("on lift 1 " + g.Heights(89));
            g.WaitIdle(89);
            Log("lift 1 up " + g.Heights(89));
            g.GoTo(-540, -64);
            Log("on sector 90 " + g.Heights(89));
            g.WaitIdle(89);
            Log("lift 1 idle " + g.Heights(89));
            g.GoTo(-640, 32);
            Log("in sector 85 " + g.Heights(89));
            g.WaitIdle(89);
            g.Use(-640, -10);
            Log("used line 313 " + g.Heights(89));
            g.Wait(10);
            Log("lift 1 lowering " + g.Heights(89));
            // Lift 4 (sector 109, tag 4): across line 289 (WR 88) into sector 107.
            g.GoTo(-100, 288, 8, 50);
            Log("east of line 289 " + g.Heights(109));
            g.GoTo(-220, 300);
            Log("in sector 107 " + g.Heights(109));
            g.Assume[109] = 64;
            g.GoTo(-192, 416);
            g.Assume.Clear();
            Log("on lift 4 " + g.Heights(109));
            g.WaitIdle(109);
            Log("lift 4 up " + g.Heights(109));
            g.GoTo(-160, 500);
            Log("in sector 34 " + g.Heights(109));
            g.WaitIdle(109);
            g.Assume[109] = 64;
            g.GoTo(-200, 350);
            g.Assume.Clear();
            Log("in sector 108 " + g.Heights(109));
            g.WaitIdle(109);
            g.Use(-200, 390);
            Log("used line 360 " + g.Heights(109));
            g.Wait(10);
            Log("lift 4 lowering " + g.Heights(109));
            g.WaitIdle(109);
            Log("lift 4 idle " + g.Heights(109));
        }),
        ["e1m3-lifts"] = new(Doom1, "E1M3", """
# DOOM1.WAD E1M3 (T5.5): out of the start through its open-stay door
# (sector 64) and the doors on the way (97, 176); the nukage bridge: the S1
# switch line 1020 (20, tag 16: sectors 48 and 49 rise to the next floor,
# 88, at half speed and take line 1020's front flat, FLOOR0_1), used from
# the nukage, riding it up. Then lift 4 (sector 168, tag 4): across line
# 179 (WR 88) into sector 122, onto the lift while it is down, ride it up,
# off north across line 181 (WR 88), back down over it into 122, and the
# SR line 178 (62) once it is idle. (E1M3's stairs, line 967, lie past
# the blue door: T5.9, once keys can be picked up.) Written by
# tools/RouteGen (e1m3-lifts).
iwad doom1
map E1M3

""", (g, Log) =>
        {
            // The nukage bridge: S1 raise to nearest and change (line 1020, 20, tag 16: sectors 48 and 49).
            g.GoToDoors(-1260, -768, 8, 50);
            Log("east of line 1020 " + g.Heights(48, 49) + " " + g.w.sectors[48].floorpic);
            g.Use(-1330, -768);
            Log("used line 1020 " + g.Heights(48, 49) + " " + g.w.sectors[48].floorpic + " special " + g.w.sectors[48].special);
            g.WaitUntil(() => g.w.sectors[48].specialdata == null && g.w.sectors[49].specialdata == null);
            Log("bridge up " + g.Heights(48, 49));
            // Lift 4 (sector 168, tag 4): across line 179 (WR 88) into sector 122.
            g.GoToDoors(-2144, -1930, 8, 50);
            Log("south of line 179 " + g.Heights(168));
            g.GoTo(-2144, -1850);
            Log("in sector 122 " + g.Heights(168));
            g.Assume[168] = 32;
            g.GoTo(-2144, -1760);
            g.Assume.Clear();
            Log("on lift 4 " + g.Heights(168));
            g.WaitIdle(168);
            Log("lift 4 up " + g.Heights(168));
            g.GoTo(-2144, -1650);
            Log("in sector 169 " + g.Heights(168));
            g.WaitIdle(168);
            g.Assume[168] = 32;
            g.GoTo(-2144, -1840);
            g.Assume.Clear();
            Log("back in sector 122 " + g.Heights(168));
            g.WaitIdle(168);
            g.Use(-2144, -1780);
            Log("used line 178 " + g.Heights(168));
            g.Wait(20);
            Log("lift 4 lowering " + g.Heights(168));
        }),
        ["e1m5-teleport"] = new(Doom1, "E1M5", """
# DOOM1.WAD E1M5 (T5.6): its teleporter (sector 56, the star of WR
# teleport lines 787-796, tag 5) lies in a closet whose door (sector 52)
# opens from inside only (monsters teleport out), so the route starts in
# the closet's room (sector 54): run north onto the star, crossing its
# lines from their front: to the destination (thing 281, sector 63) facing
# north, fog at both ends; forward held through the 18-tic freeze and 6
# tics more, then wait for the fog to fade. Written by tools/RouteGen
# (e1m5-teleport).
iwad doom1
map E1M5
""", (g, Log) =>
        {
            g.Teleport(-800, 1510, 50, hold: 24);
            Log("teleported");
            g.Wait(80);
            Log("fog gone: " + g.w.Mobjs().Count(m => m.type == IsoDoom.Sim.mobjtype_t.MT_TFOG));
        }, (-800, 1400, 90)),
        ["e1m8-teleport"] = new(Doom1, "E1M8", """
# DOOM1.WAD E1M8 (T5.6): the exit teleporter (the square of WR teleport
# lines 299-306, tag 3) is reached once the barons die (sector 30, tag
# 666), so the route starts south of it (sector 52): walk north across
# line 299 from its front: to the destination (thing 105, sector 66, the
# damage-and-exit sector of T5.8) facing north, fog at both ends; wait for
# the fog to fade. Written by tools/RouteGen (e1m8-teleport).
iwad doom1
map E1M8
""", (g, Log) =>
        {
            g.Teleport(448, 5120);
            Log("teleported");
            g.Wait(80);
            Log("fog gone: " + g.w.Mobjs().Count(m => m.type == IsoDoom.Sim.mobjtype_t.MT_TFOG));
        }, (448, 4980, 90)),
        ["testmap-lifts"] = new(() => TestMapWad("lifts"), "E1M1", """
# The lifts test map (T5.5, RouteTestMaps): east into the perpetual lift P
# across its W1 line (53: P_Random picks its first direction) and back;
# west to the SR lift switch (62, boundary 2), use it, step onto the lift
# L while it is down and ride it up; off west across the WR line (88,
# boundary 1) onto the ledge U (the lift lowers and comes back empty);
# back east onto the lift while it is down (crossing the WR line again:
# busy), ride it up and drop east into S; use the switch again (it lowers
# and comes back), the perpetual lift moving all along. Written by
# tools/RouteGen (testmap-lifts).
iwad testmap
map lifts

""", (g, Log) =>
        {
            g.GoTo(530, 128);
            Log("in P " + g.Heights(1, 3));
            g.GoTo(440, 128);
            g.GoTo(290, 128);
            Log("at the switch " + g.Heights(1, 3));
            g.Use(250, 128);
            Log("used " + g.Heights(1, 3));
            g.Assume[1] = 0;
            g.GoTo(224, 128);
            g.Assume.Clear();
            Log("on the lift " + g.Heights(1, 3));
            g.WaitIdle(1);
            Log("lift up " + g.Heights(1, 3));
            g.GoTo(120, 128);
            Log("on the ledge " + g.Heights(1, 3));
            g.WaitUntil(() => g.w.sectors[1].floorheight == 0);
            g.Assume[1] = 0;
            g.GoTo(224, 128);
            g.Assume.Clear();
            Log("on the lift again " + g.Heights(1, 3));
            g.WaitIdle(1);
            Log("lift up again " + g.Heights(1, 3));
            g.GoTo(300, 128);
            Log("in S " + g.Heights(1, 3));
            g.Use(250, 128);
            g.WaitIdle(1);
            Log("lift idle " + g.Heights(1, 3));
            g.Wait(50);
        }),
        ["testmap-teleport"] = new(() => TestMapWad("teleport"), "E1M1", """
# The teleport test map (T5.6, RouteTestMaps): run west from S across the
# WR teleport P1 | S (boundary 2, from its front): to B, facing west, fog
# at both ends; forward held through the 18-tic freeze. Walk west across
# the W1 teleport P2 | B (boundary 4): to A (its first destination), 48
# units up. East through P1, S (boundary 2 from behind: no teleport) into
# P2 (boundary 3 from behind), then west across boundary 3 from its
# front: to B. West again: across the spent W1 line into P2 and boundary 3:
# to B again. Wait for the fog to fade. Written by tools/RouteGen
# (testmap-teleport).
iwad testmap
map teleport

""", (g, Log) =>
        {
            g.Teleport(250, 128, 50, hold: 20);
            Log("in B");
            g.Teleport(500, 128);
            Log("in A");
            g.GoTo(450, 128);
            Log("in S");
            g.GoTo(608, 128);
            Log("in P2");
            g.Teleport(500, 128);
            Log("in B again");
            g.Teleport(500, 128, 50);
            Log("in B (3)");
            g.Wait(80);
            Log("fog " + string.Join(",", g.w.Mobjs().Where(m => m.type == IsoDoom.Sim.mobjtype_t.MT_TFOG).Select(m => $"{m.x >> 16}:{m.y >> 16}")));
        }),
        ["testmap-stairs"] = new(() => TestMapWad("stairs"), "E1M1", """
# The stairs test map (T5.5, RouteTestMaps): west across the W1 stairs
# line (8, boundary 7): the four steps rise by 8 a step at a quarter unit
# a tic (to 8, 16, 24 and 32 westwards); wait, climb them into room E
# across the W1 fast crusher line (6, boundary 2): the crusher room C's
# ceiling goes down to its floor + 8 and up again at 2 units a tic; wait
# a cycle. Written by tools/RouteGen
# (testmap-stairs).
iwad testmap
map stairs

""", (g, Log) =>
        {
            g.GoTo(540, 128);
            Log("in B " + g.Heights(2, 3, 4, 5));
            g.WaitUntil(() => Enumerable.Range(2, 4).All(k => g.w.sectors[k].specialdata == null));
            Log("stairs built " + g.Heights(2, 3, 4, 5));
            g.GoTo(190, 128);
            Log("in E " + g.Heights(0, 2));
            g.Wait(150);
            Log("crusher " + g.Heights(0));
        }),
    };

    public static int Main(string[] args)
    {
        if (args.Length is < 1 or > 2 || !Scripts.TryGetValue(args[0], out Script? script))
        {
            Console.Error.WriteLine($"usage: IsoDoom.RouteGen NAME [OUT.route]; NAME one of {string.Join(", ", Scripts.Keys)}");
            return 2;
        }
        string name = args[0];
        string path = args.Length > 1 ? args[1] : Path.Combine(RepoRoot(), "tests", "IsoDoom.Tests", "Sim", "Routes", name + ".route");
        var g = new Steer(script.Wad(), script.Map, script.Start);
        void Log(string s) => Console.Error.WriteLine($"[{g.w.leveltime}] ({g.X:F0},{g.Y:F0},{g.Z:F0}) {s}");
        script.Run(g, Log);
        Log($"done, {g.Cmds.Count} tics");

        // The planning must not have changed the play: replay the ticcmds in a fresh world.
        var replay = new Steer(script.Wad(), script.Map, script.Start);
        foreach (var c in g.Cmds)
            replay.w.G_Ticker(new IsoDoom.Sim.ticcmd_t { forwardmove = (sbyte)c.Forward, sidemove = (sbyte)c.Side, angleturn = (short)(c.Turn << 8), buttons = (byte)c.Buttons });
        if (replay.w.Checksum() != g.w.Checksum())
        {
            Console.Error.WriteLine("The replayed ticcmds end in another state: the planning changed the play.");
            return 1;
        }
        string header = script.Start is { } st ? script.Header.TrimEnd('\n') + $"\nstart {st.X} {st.Y} {st.Angle}\n\n" : script.Header;
        File.WriteAllText(path, g.Route(header));
        Console.WriteLine($"Wrote {path} ({g.Cmds.Count} tics)");
        return 0;
    }
}
