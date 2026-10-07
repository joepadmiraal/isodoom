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

    private static WadArchive Synthetic() =>
        new(new[] { WadFile.FromBytes(IsoDoom.Tools.SyntheticIwad.SyntheticIwad.Build(), IsoDoom.Tools.SyntheticIwad.SyntheticIwad.DefaultFileName) });

    private static WadArchive TestMapWad(string name) =>
        new(new[] { WadFile.FromBytes(RouteTestMaps.Get(name).Build(), name + ".wad") });

    private sealed record Script(Func<WadArchive> Wad, string Map, string Header, Action<Steer, Action<string>> Run, (int X, int Y, int Angle)? Start = null,
        IsoDoom.Sim.skill_t Skill = IsoDoom.Sim.skill_t.sk_medium, bool Monsters = false);

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
exit normal

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
        ["e1m2-exit"] = new(Doom1, "E1M2", """
# DOOM1.WAD E1M2 (T5.9): from the start to the exit switch. East for the
# red keycard (1136, 352), back west through the red door (sector 99) and
# north to the exit lift (sector 49, tag 13): crossing its WR lines (88)
# lowers it to the exit room (sector 48); off it while it is down and the
# exit switch (line 873, 11), the last tic. Written by tools/RouteGen
# (e1m2-exit).
iwad doom1
map E1M2
exit normal

""", (g, Log) =>
        {
            g.GoToDoors(1136, 352, 8, 50);
            Log("red key " + string.Join(",", g.w.players[0].cards));
            g.GoToDoors(-1536, 2070, 8, 50);
            Log("at line 777 " + g.Heights(124, 129));
            g.Use(-1536, 2112);
            g.WaitIdle(124);
            Log("doors open " + g.Heights(124, 129));
            g.GoToDoors(-440, 2336, 8, 50);
            Log("at the exit lift " + g.Heights(49));
            g.GoTo(-352, 2336);
            g.WaitUntil(() => g.w.sectors[49].floorheight == -232 * (1 << 16));
            Log("lift down " + g.Heights(49));
            g.Assume[49] = -232;
            g.GoTo(-280, 2336);
            g.Assume.Clear();
            g.Exit(-240, 2336);
        }),
        ["e1m4-exit"] = new(Doom1, "E1M4", """
# DOOM1.WAD E1M4 (T5.9): from the start to the exit switch (line 554,
# 11), the last tic. Into sector 104 across the WR door lines (90, tag 1:
# the doors 10, 106, 133, 138 around the cage open), through door 106 to
# the blue keycard (sector 134, inside the WR open-stay lines 348-351);
# the blue door (sector 27) to the yellow keycard (sector 13); the S1
# switch line 592 (18, tag 2: sector 52 rises to the next floor, 192);
# the yellow door (sector 41), over sector 52, door 58 and the switch.
# Written by tools/RouteGen (e1m4-exit).
iwad doom1
map E1M4
exit normal

""", (g, Log) =>
        {
            g.TightCorners = true;
            bool Open(int sec) => g.w.sectors[sec].ceilingheight - g.w.sectors[sec].floorheight >= 64 * (1 << 16) && g.w.sectors[sec].specialdata is not IsoDoom.Sim.vldoor_t { direction: -1 };
            g.GoToDoors(32, 390, 8, 50);
            Log("in sector 104 " + g.Heights(106));
            g.WaitUntil(() => Open(106));
            g.GoTo(152, 792, 8, 50);
            Log("blue key " + string.Join(",", g.w.players[0].cards) + " " + g.Heights(106));
            g.GoToDoors(-1248, 1280, 8, 50);
            Log("yellow key " + string.Join(",", g.w.players[0].cards));
            g.GoToDoors(-1088, 1500, 8, 50);
            Log("at line 592 " + g.Heights(52));
            g.Use(-1088, 1560);
            g.WaitIdle(52);
            Log("sector 52 raised " + g.Heights(52));
            g.GoToDoors(-1560, 1888, 8, 50);
            g.Exit(-1620, 1888);
        }),
        ["e1m5-exit"] = new(Doom1, "E1M5", """
# DOOM1.WAD E1M5 (T5.9): from the start to the exit switch (line 409,
# 11), the last tic. Across the W1 line 271 (22, tag 6: sector 91 rises
# to 56), the yellow keycard (sector 92); through the yellow door (15)
# down into the nukage pit (sector 19) onto the pillar 21, across its WR
# lines (98, tag 1: sectors 20 and 35 lower), the S1 switch line 189
# (103, tag 2: door 82 opens); back up on the lift 12 (WR lines 478/479,
# 88, tag 3); through door 82 to the blue keycard (sector 130), the S1
# switch line 420 (103, tag 7: door 141 opens), the blue door (109) and
# the switch. The teleporter closet's door (sector 52) opens from inside
# only; the route keeps clear of the teleporter (sector 56). Written by
# tools/RouteGen (e1m5-exit).
iwad doom1
map E1M5
exit normal

""", (g, Log) =>
        {
            g.TightCorners = true;
            g.ShutDoors.UnionWith(new[] { 52, 58 }); // open from the other side only (58 is blue from this one)
            g.Avoid.UnionWith(new[] { 55, 56 }); // the teleporter (lines 787-796) in room 54
            g.GoToDoors(888, 300, 8, 50);
            Log("across line 271 " + g.Heights(91));
            g.WaitIdle(91);
            Log("sector 91 raised " + g.Heights(91));
            g.GoToDoors(688, 800, 8, 50);
            Log("yellow key " + string.Join(",", g.w.players[0].cards));
            g.GoToDoors(-1130, 832, 8, 50);
            Log("on sector 21 " + g.Heights(20, 35, 82));
            g.Use(-1060, 832);
            g.WaitIdle(82);
            Log("door 82 open " + g.Heights(20, 35, 82));
            g.GoToDoors(-980, 730, 8, 50);
            Log("south of line 478 " + g.Heights(12));
            g.Assume[12] = -104;
            g.GoTo(-980, 832, 8, 50);
            Log("across line 478 " + g.Heights(12));
            g.GoTo(-800, 832);
            g.Assume.Clear();
            Log("on lift 12 " + g.Heights(12));
            g.WaitIdle(12);
            Log("lift 12 up " + g.Heights(12));
            g.GoToDoors(192, 1040, 8, 50);
            Log("blue key " + string.Join(",", g.w.players[0].cards));
            g.GoTo(200, 950, 8, 50);
            g.Use(200, 890);
            g.WaitIdle(141);
            Log("door 141 open " + g.Heights(141));
            g.GoToDoors(-288, 2460, 8, 50);
            g.Exit(-288, 2520);
        }),
        ["e1m6-exit"] = new(Doom1, "E1M6", """
# DOOM1.WAD E1M6 (T5.9): from the start to the exit switch (line 627,
# 11), the last tic. The red keycard (sector 185) inside the W1 door
# lines 1319-1322 (2, tag 10: five doors open); entering crossed line 206
# (16, tag 9: door 187 closes for 30 s), so wait for it; the red doors
# (174, 175) to the blue keycard (sector 179); the S1 switch line 438
# (103, tag 4: door 242 opens), the blue door (214) to the yellow keycard
# (sector 208); the S1 switch line 822 (103, tag 1: door 37 opens), across
# line 868 (76, tag 1: door 37 closes for 30 s), down to the big room
# (sector 20), the S1 switch line 599 (103, tag 3: door 28 opens), door
# 30 and the switch. Written by tools/RouteGen (e1m6-exit).
iwad doom1
map E1M6
exit normal

""", (g, Log) =>
        {
            g.TightCorners = true;
            g.GoToDoors(1680, -1344, 8, 50);
            Log("red key " + string.Join(",", g.w.players[0].cards));
            g.WaitUntil(() => g.w.sectors.Where(s => s.tag == 10).All(s => s.specialdata == null));
            Log("tag 10 doors open " + g.Heights(195, 199, 201, 203, 241));
            g.WaitUntil(() => g.w.sectors[187].specialdata == null && g.w.sectors[187].ceilingheight > g.w.sectors[187].floorheight);
            Log("door 187 open " + g.Heights(187));
            g.GoToDoors(-1536, -1728, 8, 50);
            Log("blue key " + string.Join(",", g.w.players[0].cards));
            g.GoToDoors(2624, -2260, 8, 50);
            Log("at line 438 " + g.Heights(242));
            g.Use(2624, -2320);
            g.WaitIdle(242);
            Log("door 242 open " + g.Heights(242));
            g.GoToDoors(1088, -608, 8, 50);
            Log("yellow key " + string.Join(",", g.w.players[0].cards));
            g.GoToDoors(-64, 2330, 8, 50);
            Log("at line 822 " + g.Heights(37));
            g.Use(-64, 2400);
            g.WaitUntil(() => g.w.sectors[37].specialdata == null);
            Log("door 37 open " + g.Heights(37));
            g.GoToDoors(-1488, 1384, 8, 50);
            Log("at line 599 " + g.Heights(28));
            g.Use(-1440, 1384);
            g.WaitUntil(() => g.w.sectors[28].specialdata == null);
            Log("door 28 open " + g.Heights(28));
            g.GoToDoors(-1920, 2016, 8, 50);
            g.Exit(-1860, 2016);
        }),
        ["e1m7-exit"] = new(Doom1, "E1M7", """
# DOOM1.WAD E1M7 (T5.9): from the start to the exit switch (line 810,
# 11), the last tic. Across line 396 (WR lift 88, tag 1) onto lift 23,
# ridden up to the yellow keycard (sector 47); across line 248 (WR lift
# 88, tag 9) onto lift 70, up to the red keycard (sector 73), back down
# on it (line 644); the red door (79) to the blue keycard, picked up from
# the pit (sector 77) below its 32-unit ledge (sector 76); the blue door
# (89), the S1 switch line 762 (103, tag 14: door 104 opens), door 114
# and the switch. Written by tools/RouteGen (e1m7-exit).
iwad doom1
map E1M7
exit normal

""", (g, Log) =>
        {
            g.TightCorners = true;
            g.GoToDoors(1344, -700, 8, 50);
            Log("in sector 22 " + g.Heights(23));
            g.Assume[23] = 32;
            g.GoTo(1440, -704, 8, 50);
            g.Assume.Clear();
            Log("on lift 23 " + g.Heights(23));
            g.WaitIdle(23);
            Log("lift 23 up " + g.Heights(23));
            g.GoTo(1344, -192, 8, 50);
            Log("yellow key " + string.Join(",", g.w.players[0].cards));
            g.GoToDoors(-1120, -120, 8, 50);
            Log("east of line 248 " + g.Heights(70));
            g.Assume[70] = 48;
            g.GoTo(-1240, -60, 8, 50);
            Log("across line 248 " + g.Heights(70));
            g.GoTo(-1376, -64, 8, 50);
            g.Assume.Clear();
            Log("on lift 70 " + g.Heights(70));
            g.WaitIdle(70);
            Log("lift 70 up " + g.Heights(70));
            g.GoTo(-1424, 384, 8, 50);
            Log("red key " + string.Join(",", g.w.players[0].cards));
            g.GoTo(-1376, 40, 8, 50);
            g.GoTo(-1376, -64);
            Log("across line 644 " + g.Heights(70));
            g.WaitUntil(() => g.w.sectors[70].floorheight == 48 * (1 << 16));
            g.Assume[70] = 48;
            g.GoTo(-1220, -80, 8, 50);
            g.Assume.Clear();
            Log("off lift 70 " + g.Heights(70));
            g.GoToDoors(-990, 272, 8, 50);
            Log("blue key " + string.Join(",", g.w.players[0].cards));
            g.GoToDoors(352, -1760, 8, 50);
            Log("at line 762 " + g.Heights(104));
            g.Use(352, -1700);
            g.WaitIdle(104);
            Log("door 104 open " + g.Heights(104));
            g.GoToDoors(1920, -2304, 8, 50);
            g.Exit(1980, -2304);
        }),
        ["e1m8-arena"] = new(Doom1, "E1M8", """
# DOOM1.WAD E1M8 (T5.9): from the start as far as the map goes without
# monsters (e1m8-exit goes on from the arena): the S1 lower-floor switch
# line 141 (23, tag 1: sector 10, the start closet's wall, lowers to 48),
# the door 21, the SR lift line 231 (62, tag 6: lift 28 lowers to the
# tunnel 26), ridden up, through sector 27 into the hall 31 up to the
# wall (sector 30, tag 666) that the barons' death would lower into the
# arena. Written by tools/RouteGen (e1m8-arena).
iwad doom1
map E1M8

""", (g, Log) =>
        {
            g.TightCorners = true;
            g.GoTo(-88, -224);
            g.Use(-40, -224);
            g.WaitIdle(10);
            Log("sector 10 lowered " + g.Heights(10));
            g.GoToDoors(416, 2390, 8, 50);
            Log("at line 231 " + g.Heights(28));
            g.Use(416, 2450);
            g.WaitUntil(() => g.w.sectors[28].floorheight == -96 * (1 << 16));
            g.Assume[28] = -96;
            g.GoTo(416, 2464);
            g.Assume.Clear();
            Log("on lift 28 " + g.Heights(28));
            g.WaitIdle(28);
            Log("lift 28 up " + g.Heights(28));
            g.GoToDoors(416, 3300, 8, 50);
            Log("at the wall");
        }),
        ["e1m8-exit"] = new(Doom1, "E1M8", """
# DOOM1.WAD E1M8 (T5.9): the way to the exit opens when the barons die
# (A_BossDeath lowers the pillar, sector 30, tag 666, into the arena,
# sector 29), which cannot happen without monsters, in vanilla too; so the
# route starts in the arena south of the stairs (SPEC 12 T5.9): the S1
# stairs switch line 233 (7, tag 9: sectors 53 to 67 rise 8 a step), up
# them onto sector 52, north across the WR teleport line 299 (97, tag 3)
# from its front: to the destination in the damage-and-exit sector 66
# (11), and wait there until its 20-unit hits leave 10 health or less:
# G_ExitLevel, the last tic. Written by tools/RouteGen (e1m8-exit).
iwad doom1
map E1M8
exit normal
""", (g, Log) =>
        {
            g.TightCorners = true;
            g.Wait(1); // G_PlayerReborn's usedown: use needs a tic without it first
            g.Use(320, 4440);
            g.WaitUntil(() => g.w.sectors.All(s => s.specialdata is not IsoDoom.Sim.floormove_t));
            Log("stairs built " + g.Heights(53, 54, 60, 67));
            g.GoTo(448, 4980, 8, 50);
            Log("on sector 52");
            g.Teleport(448, 5120);
            Log("teleported, health " + g.w.players[0].health);
            g.WaitExit();
            Log("health " + g.w.players[0].health);
        }, (320, 4384, 90)),
        ["e1m9-exit"] = new(Doom1, "E1M9", """
# DOOM1.WAD E1M9 (T5.9): from the start to the exit switch (line 551,
# 11), the last tic. The yellow keycard (sector 88); the yellow door
# (142) to the red keycard (sector 132, inside the W1 door lines 524-527,
# 2, tag 8); the red doors (111, 103) to the S1 switch line 362 (103,
# tag 1: doors 107, 126, 130 open), used over the barrels in front of
# it; the blue keycard (sector 129); the blue door (27), down into the
# nukage (sector 21) to the S1 switch line 567 (20, tag 9: the nukage
# 17 rises to 88); across the WR door lines 570/571 (90, tag 11: door 48
# opens), the SR lift line 587 (62, tag 10: lift 49), ridden up, door 51
# and the switch. Written by tools/RouteGen (e1m9-exit).
iwad doom1
map E1M9
exit normal

""", (g, Log) =>
        {
            g.TightCorners = true;
            g.GoToDoors(-576, 64, 8, 50);
            Log("yellow key " + string.Join(",", g.w.players[0].cards));
            g.GoToDoors(1856, -1088, 8, 50);
            Log("red key " + string.Join(",", g.w.players[0].cards));
            g.GoToDoors(-576, -1344, 4, 50); // barrels (y -1376) in front of the switch: use over them
            g.Walk(-576, -1400, 4);
            Log("at line 362 " + g.Heights(107, 126, 130));
            g.Use(-576, -1430);
            g.WaitUntil(() => g.w.sectors[130].specialdata == null);
            Log("tag 1 doors open " + g.Heights(107, 126, 130));
            g.GoToDoors(704, -1024, 8, 50);
            Log("blue key " + string.Join(",", g.w.players[0].cards));
            g.GoToDoors(384, 1444, 8, 50);
            Log("at line 567 " + g.Heights(17));
            g.Use(384, 1380);
            g.WaitIdle(17);
            Log("sector 17 raised " + g.Heights(17));
            g.GoTo(200, 1450, 8, 50);
            Log("across lines 570/571 " + g.Heights(48));
            g.WaitUntil(() => g.w.sectors[48].specialdata is IsoDoom.Sim.vldoor_t { direction: 0 });
            g.GoTo(224, 960, 8, 50);
            Log("at line 587 " + g.Heights(49));
            g.Use(300, 960);
            g.WaitUntil(() => g.w.sectors[49].floorheight == 0);
            g.Assume[49] = 0;
            g.GoTo(288, 960);
            g.Assume.Clear();
            Log("on lift 49 " + g.Heights(49));
            g.WaitIdle(49);
            Log("lift 49 up " + g.Heights(49));
            g.GoToDoors(456, 1328, 8, 50);
            g.Exit(400, 1384);
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
        ["e1m3-exit"] = new(Doom1, "E1M3", """
# DOOM1.WAD E1M3 (T5.9): from the start to the exit switch (line 982,
# 11), the last tic. Through the keyless doors for the blue keycard (on
# the raised pad, sector 28), back through the blue door (sector 61) to
# the hall (sector 7), across the W1 stairs line 967 (8, tag 14: the
# steps from sector 16 rise 8 a step), up the stairs, door 2 and the
# switch. Written by tools/RouteGen (e1m3-exit).
iwad doom1
map E1M3
exit normal

""", (g, Log) =>
        {
            g.GoToDoors(-160, -864, 8, 50);
            Log("blue key " + string.Join(",", g.w.players[0].cards));
            g.GoToDoors(-290, -1600, 8, 50);
            g.GoTo(-240, -1600);
            Log("across line 967 " + g.Heights(16, 17, 18, 13));
            g.WaitUntil(() => g.w.sectors.All(s => s.specialdata == null || s.specialdata is not IsoDoom.Sim.floormove_t));
            Log("stairs built " + g.Heights(16, 17, 18, 13));
            g.GoToDoors(704, -1720, 8, 50);
            g.Exit(704, -1770);
        }),
        ["e1m3-secret-exit"] = new(Doom1, "E1M3", """
# DOOM1.WAD E1M3 (T5.9): from the start to the secret exit switch (line
# 785, 51, in sector 35), the last tic. Its way (sectors 51-53, 32, 31)
# starts in the start's nukage (sector 66, floor 0), 64 units below the
# secret door 51, which the S1 switch line 360 (20, tag 27) far north-west
# raises: the S1 door switch line 535 (103, tag 10: sector 121); up the
# steps to line 116 (WR lift 88, tag 2: sectors 133 and 165 lower); onto
# lift 165 while it is down across line 231 (W1 door 2, tag 3: sector
# 167 opens), ride it up; through door 167 to sector 173 and its switch
# (line 360: the nukage 66 rises to 64, half speed); back through the
# secret door 174, across line 462 (WR lift 88, tag 7: lift 171 lowers),
# ride it up, door 172 (line 459), across line 181 (WR 88, tag 4: lift
# 168 lowers), ride it down; back south to the raised nukage across line
# 988 (W1 door 2, tag 12: the secret door 51 opens), north through it
# across line 54 (W1 door 2, tag 48: 53 opens), across line 88 (W1 door 2,
# tag 51: 33 and 34 open), door 45 and the switch. Written by
# tools/RouteGen (e1m3-secret-exit).
iwad doom1
map E1M3
exit secret

""", (g, Log) =>
        {
            g.TightCorners = true;
            bool Open(int sec) => g.w.sectors[sec].ceilingheight - g.w.sectors[sec].floorheight >= 64 * (1 << 16) && g.w.sectors[sec].specialdata == null;
            g.GoToDoors(-1936, -2120, 8, 50);
            Log("south of line 535 " + g.Heights(121));
            g.Use(-1936, -2060);
            g.WaitUntil(() => Open(121));
            Log("door 121 open " + g.Heights(121));
            g.GoToDoors(-2220, -2190, 8, 50);
            Log("in sector 120 " + g.Heights(165));
            g.Assume[165] = 128;
            g.GoTo(-2664, -1360, 6, 50);
            g.Assume.Clear();
            Log("at lift 165 " + g.Heights(165));
            g.Walk(-2700, -1324, 3, 25);
            Log("on lift 165 " + g.Heights(165, 167));
            g.WaitIdle(165);
            g.WaitUntil(() => Open(167));
            Log("lift up, door 167 open " + g.Heights(165, 167));
            g.GoToDoors(-2416, -944, 4, 50);
            Log("in sector 173 " + g.Heights(66));
            g.Use(-2380, -910);
            g.WaitIdle(66);
            Log("nukage raised " + g.Heights(66));
            g.GoToDoors(-2080, -1250, 8, 50);
            g.GoTo(-2080, -1350, 8, 50);
            Log("across line 462 " + g.Heights(171));
            g.Assume[171] = 112;
            g.GoTo(-2080, -1436);
            g.Assume.Clear();
            Log("on lift 171 " + g.Heights(171));
            g.WaitIdle(171);
            Log("lift 171 up " + g.Heights(171));
            g.Door(172, -2064, -1470);
            g.GoTo(-2144, -1700, 8, 50);
            Log("in sector 169 " + g.Heights(168));
            g.Assume[168] = 32;
            g.GoTo(-2144, -1760);
            g.Assume.Clear();
            Log("on lift 168 " + g.Heights(168));
            g.WaitUntil(() => g.w.sectors[168].floorheight == 32 * (1 << 16));
            g.Assume[168] = 32;
            g.GoToDoors(-1450, -2680, 8, 50);
            g.Assume.Clear();
            Log("in sector 66 " + g.Heights(51));
            g.GoTo(-1580, -2760, 8, 50);
            Log("across line 988 " + g.Heights(51));
            g.WaitUntil(() => Open(51));
            g.GoTo(-1504, -2300, 8, 50);
            g.GoTo(-1504, -2080, 8, 50);
            Log("across line 54 " + g.Heights(53));
            g.WaitUntil(() => Open(53));
            g.GoTo(-1504, -1880, 8, 50);
            Log("across line 88 " + g.Heights(33, 34));
            g.WaitUntil(() => Open(33));
            g.GoToDoors(-1088, -1500, 8, 50);
            g.Exit(-1088, -1560);
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
        ["synthetic-exit"] = new(Synthetic, "E1M1", """
# The synthetic E1M1 (T5.9): from the start north to its S1 exit switch
# (line 0, 11, 128 units ahead), the last tic, so the dump's exit column is
# checked in CI. Written by tools/RouteGen (synthetic-exit).
iwad synthetic
exit normal

""", (g, Log) =>
        {
            g.GoTo(0, 80);
            g.Exit(0, 140);
        }),
        ["synthetic-secret-exit"] = new(Synthetic, "E1M2", """
# The synthetic E1M2 (T5.9): starts in room S facing its north wall's S1
# secret exit switch (line 84, 51) and uses it, the last tic (the first
# tic releases use: G_PlayerReborn's usedown). Written by tools/RouteGen
# (synthetic-secret-exit).
iwad synthetic
map E1M2
exit secret
""", (g, Log) =>
        {
            g.Wait(1);
            g.Exit(224, 260);
        }, (224, 212, 90)),
        ["synthetic-keys"] = new(Synthetic, "E1M2", """
# The synthetic E1M2 (T5.8): from the start east along the corridor over
# the blue keycard (560, -96) and the yellow skull key (624, -96), so the
# dump's cards column checks both pickups (the items vanish, the sound and
# the message are not dumped). Written by tools/RouteGen (synthetic-keys).
iwad synthetic
map E1M2

""", (g, Log) =>
        {
            g.GoTo(480, -96);
            Log("cards " + string.Join(",", g.w.players[0].cards));
            g.GoTo(690, -96);
            Log("cards " + string.Join(",", g.w.players[0].cards));
            g.Settle();
        }),
        ["e1m1-monsters"] = new(Doom1, "E1M1", """
# DOOM1.WAD E1M1 on skill 4 with monsters (T6.4): e1m1-exit's way from
# the start through door 4, the nukage room, doors 68, 76 and 81. The
# zombiemen, shotgun guys and imps wake (by sight, or the shots' noise),
# chase and attack (hitscans, scratches, fireballs); each is shot dead
# with route events once it attacked (alert + damage, standing in for
# the player's weapons), a barrel next to them too. Written by
# tools/RouteGen (e1m1-monsters).
iwad doom1
skill 4
monsters

""", (g, Log) =>
        {
            var gun = new Gunner(g) { Every = 6 };
            g.GoTo(1500, -2496);
            Log("at door 4 " + g.Monsters());
            g.Door(4, 1544, -2496);
            gun.Clear(400);
            Log("door 4 clear " + g.Monsters());
            g.GoTo(1600, -2496);
            gun.Clear(400);
            Log("through door 4 " + g.Monsters());
            g.GoTo(2900, -2900, 12);
            gun.Clear(400);
            Log("nukage room " + g.Monsters());
            g.GoTo(2900, -3100, 12);
            gun.Clear(400);
            Log("across line 195 " + g.Monsters());
            g.GoTo(2980, -3840, 6);
            g.Door(68, 2944, -3840);
            gun.Clear(400);
            g.GoTo(2890, -3840);
            gun.Clear(400);
            Log("through door 68 " + g.Monsters());
            g.GoTo(3008, -3990);
            g.Door(76, 3008, -4024);
            gun.Clear(400);
            Log("door 76 " + g.Monsters());
            g.GoTo(3008, -4100);
            gun.Clear(400);
            Log("through door 76 " + g.Monsters());
            g.Wait(100);
            gun.Clear(400);
            g.GoTo(3008, -4600, 6);
            gun.Clear(400);
            Log("at door 81 " + g.Monsters());
            g.Door(81, 3008, -4640);
            g.Wait(60);
            gun.Clear(400);
            Log("door 81 " + g.Monsters());
            Log(gun.ToString());
        }, Skill: IsoDoom.Sim.skill_t.sk_hard, Monsters: true),
        ["e1m1-weapons"] = new(Doom1, "E1M1", """
# DOOM1.WAD E1M1 on skill 4 with monsters (T6.6): e1m1-monsters' way
# from the start through door 4, the monsters that attacked shot with the
# player's own weapons (BT_ATTACK, turning towards the nearest one in
# sight every tic; no route events): the pistol, then the shotgun a
# shotgun guy dropped (walked over: the pickup switches to it). Written by
# tools/RouteGen (e1m1-weapons).
iwad doom1
skill 4
monsters

""", (g, Log) =>
        {
            var gun = new Gunner(g) { Guns = true };
            IsoDoom.Sim.player_t p = g.w.players[0];
            string State() => $"weapon {p.readyweapon} ammo {string.Join(",", p.ammo)} " + g.Monsters();
            g.GoTo(1500, -2496);
            Log("at door 4 " + State());
            g.Door(4, 1544, -2496);
            gun.Clear(400);
            Log("door 4 clear " + State());
            g.GoTo(1600, -2496);
            gun.Clear(400);
            Log("through door 4 " + State());
            g.Wait(60);
            gun.Clear(400);
            Log("waited " + State());
            // the shotgun a shotgun guy dropped: walk over it (the pickup switches to it)
            if (g.w.Mobjs().Where(m => m.type == IsoDoom.Sim.mobjtype_t.MT_SHOTGUN).OrderBy(m => System.Math.Abs(m.x - g.Mo.x) + System.Math.Abs(m.y - g.Mo.y)).FirstOrDefault() is { } sg)
            {
                g.GoTo(sg.x / 65536.0, sg.y / 65536.0, 4);
                g.Wait(30);
                Log("shotgun " + State());
            }
            g.Wait(100);
            gun.Clear(400);
            Log("waited " + State());
            Log(gun.ToString());
        }, Skill: IsoDoom.Sim.skill_t.sk_hard, Monsters: true),
        ["e1m1-rockets"] = new(Doom1, "E1M1", """
# DOOM1.WAD E1M1 on skill 4 with monsters (T6.5): e1m1-monsters' way
# through door 4, but the monsters that attacked are shot with rocket events
# (P_SpawnPlayerMissile, aimed as vanilla; the player faces the target a
# tic before) when they are in sight at least 200 units away, else with
# damage events: rockets fly, hit monsters, walls and barrels and
# explode (A_Explode's blast), the imps' fireballs fly and hit. Written
# by tools/RouteGen (e1m1-rockets).
iwad doom1
skill 4
monsters

""", (g, Log) =>
        {
            var gun = new Gunner(g) { Every = 8, Rockets = true };
            g.GoTo(1500, -2496);
            Log("at door 4 " + g.Monsters());
            g.Door(4, 1544, -2496);
            gun.Clear(400);
            Log("door 4 clear " + g.Monsters());
            g.GoTo(1600, -2496);
            gun.Clear(400);
            Log("through door 4 " + g.Monsters());
            g.Wait(60);
            gun.Clear(400);
            Log("waited " + g.Monsters());
            Log(gun.ToString());
        }, Skill: IsoDoom.Sim.skill_t.sk_hard, Monsters: true),
        ["e1m2-monsters"] = new(Doom1, "E1M2", """
# DOOM1.WAD E1M2 on skill 4 with monsters (T6.4): a shot's noise at the
# start, north through door 116 and east to the red keycard, a wait there
# and back to the start: zombiemen and imps wake, chase and attack and are
# shot dead with route events once they attacked; the shotgun guys wake
# and chase. Written by tools/RouteGen (e1m2-monsters).
iwad doom1
map E1M2
skill 4
monsters
""", (g, Log) =>
        {
            var gun = new Gunner(g) { Every = 6 };
            g.Wait(1);
            g.Alert();
            g.Wait(60);
            gun.Clear(400);
            Log("start " + g.Monsters());
            g.GoToDoors(300, 300, 8, 25);
            gun.Clear(400);
            Log("north " + g.Monsters());
            g.GoToDoors(1136, 352, 8, 25);
            gun.Clear(400);
            Log("red key " + g.Monsters());
            g.Wait(150);
            gun.Clear(600);
            Log("red key, waited " + g.Monsters());
            g.GoToDoors(300, 300, 8, 25);
            gun.Clear(400);
            Log("north again " + g.Monsters());
            g.GoToDoors(-32, -240, 8, 25);
            g.Wait(100);
            gun.Clear(400);
            Log("start again " + g.Monsters());
            Log(gun.ToString());
        }, Skill: IsoDoom.Sim.skill_t.sk_hard, Monsters: true),
        ["e1m8-demons"] = new(Doom1, "E1M8", """
# DOOM1.WAD E1M8 on skill 2 with monsters (T6.4): the start closet's
# floor switch (line 141) lowers its wall, the four demons east wake and
# come; the player steps down to them (sector 1) and shoots each dead
# with route events once it attacked. Written by tools/RouteGen
# (e1m8-demons).
iwad doom1
map E1M8
skill 2
monsters
""", (g, Log) =>
        {
            var gun = new Gunner(g) { Amount = m => 75, Every = 6 };
            g.GoTo(-88, -224);
            g.Use(-40, -224);
            g.WaitIdle(10);
            Log("sector 10 lowered " + g.Heights(10) + " " + g.Monsters());
            g.GoTo(250, -224);
            g.Wait(150);
            gun.Clear(600);
            g.Wait(100);
            gun.Clear(600);
            Log("demons " + g.Monsters());
            Log(gun.ToString());
        }, Skill: IsoDoom.Sim.skill_t.sk_easy, Monsters: true),
        ["e1m8-nightmare"] = new(Doom1, "E1M8", """
# DOOM1.WAD E1M8 on Nightmare (skill 5) with monsters (T6.4): as
# e1m8-barons, with fast monsters (the spectres' halved run, attack and
# pain states, 20-unit baron balls, attacks between steps) and Nightmare
# respawns (fogs; a baron comes back and is killed again). Written by
# tools/RouteGen (e1m8-nightmare).
iwad doom1
map E1M8
skill 5
monsters
""", (g, Log) =>
        {
            var gun = new Gunner(g) { Amount = m => m.type == IsoDoom.Sim.mobjtype_t.MT_BRUISER ? 200 : 75, Every = 4 };
            g.TightCorners = true;
            g.Wait(1); // (G_PlayerReborn's usedown)
            g.Use(416, 2450);
            g.WaitUntil(() => g.w.sectors[28].floorheight == -96 * (1 << 16));
            g.Assume[28] = -96;
            g.GoTo(416, 2464);
            g.Assume.Clear();
            g.WaitIdle(28);
            Log("lift 28 up " + g.Monsters());
            g.GoToDoors(416, 2600, 8, 25);
            Log("hall " + g.Monsters());
            for (int i = 0; i < 20; i++)
            {
                g.Wait(40);
                Log(g.Monsters() + " " + g.Heights(30) + " fogs " + g.w.Mobjs().Count(m => m.type == IsoDoom.Sim.mobjtype_t.MT_TFOG));
            }
            Log(gun.ToString());
        }, (416, 2390, 90), IsoDoom.Sim.skill_t.sk_nightmare, true),
        ["synthetic-monsters"] = new(Synthetic, "E1M1", """
# The synthetic E1M1 on skill 1 with monsters (T6.4): the eight imps
# around the start and the one east wake, scratch and throw fireballs and
# are shot dead with route events once they attacked; then the barrel is
# shot: it explodes (A_Explode) and hurts the player. Written by
# tools/RouteGen (synthetic-monsters).
iwad synthetic
skill 1
monsters
""", (g, Log) =>
        {
            var gun = new Gunner(g) { Every = 8, Amount = m => 30 };
            for (int i = 0; i < 8; i++)
            {
                g.Wait(30);
                Log(g.Monsters());
            }
            gun.Clear(600);
            Log(g.Monsters());
            // the barrel (64, 64): shot, it explodes (A_Explode) and hurts the player
            g.Alert();
            g.Damage(64, 64, 20);
            g.Wait(40);
            Log("barrel " + g.Monsters());
            Log(gun.ToString());
        }, Skill: IsoDoom.Sim.skill_t.sk_baby, Monsters: true),
        ["e1m8-barons"] = new(Doom1, "E1M8", """
# DOOM1.WAD E1M8 on skill 4 with monsters (T6.4): starts in the tunnel
# at the lift switch (line 231), rides lift 28 up and crosses line 176
# (W1 doors, tag 5): the barons' cages open; the barons and the spectres
# of hall 31 attack (balls, claws, bites) and are shot dead with route
# events; the last baron's A_BossDeath lowers the wall (sector 30, tag
# 666) to the arena, -136. Written by tools/RouteGen (e1m8-barons).
iwad doom1
map E1M8
skill 4
monsters
""", (g, Log) =>
        {
            var gun = new Gunner(g) { Amount = m => m.type == IsoDoom.Sim.mobjtype_t.MT_BRUISER ? 100 : 50, Every = 6 };
            g.TightCorners = true;
            g.Wait(1); // (G_PlayerReborn's usedown)
            g.Use(416, 2450);
            g.WaitUntil(() => g.w.sectors[28].floorheight == -96 * (1 << 16));
            g.Assume[28] = -96;
            g.GoTo(416, 2464);
            g.Assume.Clear();
            Log("on lift 28 " + g.Heights(28));
            g.WaitIdle(28);
            Log("lift 28 up " + g.Monsters());
            g.GoToDoors(416, 2600, 8, 25);
            Log("hall " + g.Monsters());
            g.WaitUntil(() => g.w.Mobjs().Count(m => m.type == IsoDoom.Sim.mobjtype_t.MT_BRUISER && m.health > 0) == 0, 1000);
            Log("barons dead " + g.Monsters() + " " + g.Heights(30));
            g.WaitUntil(() => g.w.sectors[30].specialdata != null, 50);
            g.WaitIdle(30);
            Log("wall down " + g.Heights(30));
            Log(gun.ToString());
        }, (416, 2390, 90), IsoDoom.Sim.skill_t.sk_hard, true),
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
        ["testmap-missiles"] = new(() => TestMapWad("missiles"), "E1M1", """
# The missiles test map (T6.5, RouteTestMaps) with monsters: rocket
# events (P_SpawnPlayerMissile, standing in for the rocket launcher until
# T6.6). A rocket west hits the low sky wall W | S (boundary 1) and
# vanishes (the sky hack: W, behind the line, has a sky ceiling, and the
# rocket's centre is still in S); one north hits the barrel (it
# explodes), one more the north wall; then a noise (alert) wakes the imp
# and it throws fireballs while the player strafes up and down the room
# (the missed ones vanish on the sky wall behind); it is shot with
# rockets (aimed at it, 5.6 degrees off at most) until it dies; a rocket
# east explodes on the low sky wall S | E (boundary 2: S, behind it, has
# no sky ceiling, vanilla's one-sided hack); a last one west from x 290
# explodes on W | S: its 20-unit steps put its centre past the line, in
# W, whose ceiling then is no lower than the missile's way (no
# ceilingline). Written by tools/RouteGen (testmap-missiles).
iwad testmap
map missiles
monsters

""", (g, Log) =>
        {
            string Missiles() => string.Join(" ", g.w.Mobjs()
                .Where(m => m.type is IsoDoom.Sim.mobjtype_t.MT_ROCKET or IsoDoom.Sim.mobjtype_t.MT_TROOPSHOT or IsoDoom.Sim.mobjtype_t.MT_BARREL or IsoDoom.Sim.mobjtype_t.MT_TROOP)
                .Select(m => $"{m.type.ToString()[3..]}({m.x >> 16},{m.y >> 16},{m.z >> 16}) {m.state.ToString()[2..]} h{m.health}")) + $"; health {g.w.players[0].health}";
            g.Rocket();
            g.Wait(3);
            Log("rocket west " + Missiles());
            g.Wait(1);
            Log("rocket west, a tic later " + Missiles());
            g.Face(136, 600);
            g.Rocket();
            g.Wait(40);
            Log("rocket north (barrel) " + Missiles());
            g.Rocket();
            g.Wait(40);
            Log("rocket north (wall) " + Missiles());
            g.Face(600, 256);
            g.Alert();
            bool Ball() => g.w.Mobjs().Any(m => m.type == IsoDoom.Sim.mobjtype_t.MT_TROOPSHOT && (m.flags & IsoDoom.Sim.mobjflag_t.MF_MISSILE) != 0);
            for (int i = 0; i < 3; i++)
            {
                // a fireball comes: strafe out of its way (facing east: + is south)
                g.WaitUntil(Ball, 300);
                for (int k = 0; k < 22; k++)
                    g.Tic(0, i % 2 == 0 ? -40 : 40, 0, 0);
                Log("strafed, the imp awake " + Missiles());
                g.Settle();
            }
            var imp = g.w.Mobjs().First(m => m.type == IsoDoom.Sim.mobjtype_t.MT_TROOP);
            for (int i = 0; i < 8 && imp.health > 0; i++)
            {
                g.Face(imp.x / 65536.0, imp.y / 65536.0);
                g.Rocket();
                for (int k = 0; k < 12; k++)
                    g.Tic(0, i % 2 == 0 ? 40 : -40, 0, 0);
                Log("rocket at the imp " + Missiles());
            }
            g.Wait(30);
            g.GoTo(136, 256);
            g.Face(600, 256);
            g.Rocket();
            g.Wait(40);
            Log("rocket east (wall) " + Missiles());
            g.GoTo(290, 256, 3);
            g.Face(0, g.Y);
            g.Rocket();
            g.Wait(20);
            Log("rocket west from x 290 " + Missiles());
        }, Monsters: true),
        ["testmap-weapons"] = new(() => TestMapWad("weapons"), "E1M1", """
# The weapons test map (T6.6, RouteTestMaps) with monsters: every shareware
# weapon fired with BT_ATTACK and changed with BT_CHANGE, as vanilla's
# psprites run them. In room A the pistol comes up; weapon key 1 lowers it
# and raises the fist, which punches the zombieman ahead to death (the
# noise wakes the room); key 2 brings the pistol back, which shoots the
# monsters near (held: refire); the shotgun is walked over (the pickup
# switches to it) and shoots the rest, then the barrel (it explodes). Across
# the W1 line the door to room B opens; the rocket launcher is walked over
# and fires its two rockets at the nearest monster at least 200 units
# away (held: A_ReFire), then
# P_CheckAmmo switches to the shotgun; the chaingun is walked over and
# shoots the rest. Across the next W1 line the door to room C opens; the
# chainsaw is walked over and saws the zombiemen (the pull forward); key 1
# keeps it (the chainsaw stands for the fist); key 4 while sawing changes
# to the chaingun after the stroke, which empties into the east wall until
# P_CheckAmmo switches again. Written by tools/RouteGen (testmap-weapons).
iwad testmap
map weapons
monsters

""", (g, Log) =>
        {
            IsoDoom.Sim.player_t p = g.w.players[0];
            string State() => $"health {p.health} armor {p.armorpoints} weapon {p.readyweapon} pending {p.pendingweapon} psp {p.psprites[0].state} ammo {string.Join(",", p.ammo)}; "
                + string.Join(" ", g.w.Mobjs().Where(m => ((m.flags & IsoDoom.Sim.mobjflag_t.MF_COUNTKILL) != 0 || m.type == IsoDoom.Sim.mobjtype_t.MT_BARREL) && m.health > 0)
                    .Select(m => $"{m.type.ToString()[3..]}({m.x >> 16},{m.y >> 16}) h{m.health}"));
            void Ready(IsoDoom.Sim.weapontype_t wp) => g.WaitUntil(() => p.readyweapon == wp && p.pendingweapon == IsoDoom.Sim.weapontype_t.wp_nochange
                && p.psprites[0].state == IsoDoom.Sim.Info.weaponinfo[(int)wp].readystate, 200);
            double Dist(IsoDoom.Sim.mobj_t m) => System.Math.Sqrt(System.Math.Pow((m.x - g.Mo.x) / 65536.0, 2) + System.Math.Pow((m.y - g.Mo.y) / 65536.0, 2));
            IsoDoom.Sim.mobj_t? Nearest(double min = 0) => g.w.Mobjs()
                .Where(m => (m.flags & IsoDoom.Sim.mobjflag_t.MF_COUNTKILL) != 0 && m.health > 0 && g.w.P_CheckSight(g.Mo, m) && Dist(m) >= min)
                .OrderBy(Dist).FirstOrDefault();
            void KillAll(int max = 6)
            {
                for (int i = 0; i < max && Nearest() is { } m; i++)
                {
                    g.FireAt(m, 250);
                    Log("shot " + State());
                }
            }
            void Door(int sector) => g.WaitUntil(() => g.w.sectors[sector].ceilingheight >= 100 * 65536, 200);

            // room A
            Ready(IsoDoom.Sim.weapontype_t.wp_pistol);
            Log("pistol up " + State());
            g.Change(IsoDoom.Sim.weapontype_t.wp_fist);
            Ready(IsoDoom.Sim.weapontype_t.wp_fist);
            Log("fist up " + State());
            g.GoTo(122, 256, 3);
            g.FireAt(g.Spawned(160, 256)!, 400);
            Log("punched " + State());
            g.Change(IsoDoom.Sim.weapontype_t.wp_pistol);
            Ready(IsoDoom.Sim.weapontype_t.wp_pistol);
            while (Nearest() is { } m && Dist(m) < 300)
            {
                g.FireAt(m, 250);
                Log("pistol " + State());
            }
            g.GoTo(400, 256);
            Ready(IsoDoom.Sim.weapontype_t.wp_shotgun);
            Log("shotgun up " + State());
            KillAll();
            if (g.Spawned(410, 64) is { } barrel)
            {
                g.FireAt(barrel, 100);
                g.Wait(30);
                Log("barrel " + State());
            }

            // room B
            g.GoTo(544, 256);
            Door(2);
            Log("door 1 open " + State());
            g.GoTo(680, 256);
            Ready(IsoDoom.Sim.weapontype_t.wp_missile);
            Log("launcher up " + State());
            if (Nearest(200) is { } far)
            {
                for (int i = 0; i < 120 && p.readyweapon == IsoDoom.Sim.weapontype_t.wp_missile; i++)
                    g.Fire(far.x / 65536.0, far.y / 65536.0);
                g.Tic(0, 0, 0, 0);
            }
            Log("rockets " + State());
            g.GoTo(840, 256);
            Ready(IsoDoom.Sim.weapontype_t.wp_chaingun);
            Log("chaingun up " + State());
            KillAll();

            // room C
            g.GoTo(1008, 256);
            Door(5);
            Log("door 2 open " + State());
            g.GoTo(1100, 256);
            Ready(IsoDoom.Sim.weapontype_t.wp_chainsaw);
            Log("chainsaw up " + State());
            for (int i = 0; i < 3 && Nearest() is { } m; i++)
            {
                if (Dist(m) > 60)
                    g.GoTo(m.x / 65536.0 - 44, m.y / 65536.0, 12);
                g.FireAt(m, 200);
                Log("sawed " + State());
            }
            g.Change(IsoDoom.Sim.weapontype_t.wp_fist);
            g.Wait(10);
            Log("key 1 " + State());
            for (int i = 0; i < 6; i++)
                g.Fire(1400, 256);
            g.Tic(0, 0, 0, IsoDoom.Sim.buttoncode_t.BT_ATTACK | IsoDoom.Sim.buttoncode_t.BT_CHANGE | ((int)IsoDoom.Sim.weapontype_t.wp_chaingun << IsoDoom.Sim.buttoncode_t.BT_WEAPONSHIFT));
            for (int i = 0; i < 12; i++)
                g.Tic(0, 0, 0, IsoDoom.Sim.buttoncode_t.BT_ATTACK);
            Log("key 4 while sawing " + State());
            g.Tic(0, 0, 0, 0);
            Ready(IsoDoom.Sim.weapontype_t.wp_chaingun);
            for (int i = 0; i < 600 && p.readyweapon == IsoDoom.Sim.weapontype_t.wp_chaingun; i++)
                g.Fire(1400, 256);
            g.Wait(40);
            Log("emptied " + State());
        }, Monsters: true),
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
        if (args.Length == 3 && args[0] == "--probe")
        {
            var probe = new Steer(Doom1(), args[1]);
            foreach (string pt in args[2].Split(';'))
                Console.WriteLine(probe.ProbeText(double.Parse(pt.Split(',')[0]), double.Parse(pt.Split(',')[1])));
            return 0;
        }
        if (args.Length is 2 or 3 && args[0] == "--monsters")
            return MapInfo.PrintMonsters(Doom1(), args[1], args.Length == 3 ? int.Parse(args[2]) : 3);
        if (args.Length is 2 or 3 && args[0] == "--info")
            return MapInfo.Print(Doom1(), args[1], args.Length == 3 ? args[2].Split(',').Select(int.Parse).ToArray() : null);
        if (args.Length is < 1 or > 2 || !Scripts.TryGetValue(args[0], out Script? script))
        {
            Console.Error.WriteLine($"usage: IsoDoom.RouteGen NAME [OUT.route]; NAME one of {string.Join(", ", Scripts.Keys)}");
            return 2;
        }
        string name = args[0];
        string path = args.Length > 1 ? args[1] : Path.Combine(RepoRoot(), "tests", "IsoDoom.Tests", "Sim", "Routes", name + ".route");
        var g = new Steer(script.Wad(), script.Map, script.Start, script.Skill, script.Monsters);
        void Log(string s) => Console.Error.WriteLine($"[{g.w.leveltime}] ({g.X:F0},{g.Y:F0},{g.Z:F0}) {s}");
        script.Run(g, Log);
        Log($"done, {g.Cmds.Count} tics");

        // The planning must not have changed the play: replay the ticcmds in a fresh world.
        var replay = new Steer(script.Wad(), script.Map, script.Start, script.Skill, script.Monsters);
        replay.Events.AddRange(g.Events);
        foreach (var c in g.Cmds)
            replay.Tic(c.Forward, c.Side, c.Turn, c.Buttons); // (with the events)
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
