using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using IsoDoom.Sim;
using IsoDoom.Tests.Sim;
using IsoDoom.Wad;

namespace IsoDoom.Tools.RouteGen;

/// <summary>
/// <c>dotnet run --project tools/RouteGen -- NAME [OUT.route]</c>: plays the
/// steering script NAME (<see cref="_scripts"/>) in the sim and writes its
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

    /// <summary>T10.1: Doom II's IWAD (<c>ISODOOM_DOOM2_WAD</c>, else <c>wads/doom2.wad</c> or <c>wads/DOOM2.WAD</c>).</summary>
    private static WadArchive Doom2() =>
        WadArchive.Open(Environment.GetEnvironmentVariable("ISODOOM_DOOM2_WAD") is { Length: > 0 } p ? p
            : File.Exists(Path.Combine(RepoRoot(), "wads", "doom2.wad")) ? Path.Combine(RepoRoot(), "wads", "doom2.wad") : Path.Combine(RepoRoot(), "wads", "DOOM2.WAD"));

    /// <summary>T10.7: Final Doom's IWAD (<c>tnt</c> or <c>plutonia</c>: <c>ISODOOM_TNT_WAD</c> / <c>ISODOOM_PLUTONIA_WAD</c>, else <c>wads/</c>, as the tests' <c>TestWads</c>).</summary>
    private static WadArchive FinalDoom(string which)
    {
        if (Environment.GetEnvironmentVariable(which == "tnt" ? "ISODOOM_TNT_WAD" : "ISODOOM_PLUTONIA_WAD") is { Length: > 0 } p)
            return WadArchive.Open(p);
        string upper = Path.Combine(RepoRoot(), "wads", which.ToUpperInvariant() + ".WAD");
        return WadArchive.Open(File.Exists(upper) ? upper : Path.Combine(RepoRoot(), "wads", which + ".wad"));
    }

    /// <summary>M9: The Ultimate Doom's IWAD (<c>ISODOOM_DOOM_WAD</c>, else <c>wads/DOOM.WAD</c> or <c>wads/doom.wad</c>, as the tests' <c>TestWads</c>).</summary>
    private static WadArchive Doom()
    {
        if (Environment.GetEnvironmentVariable("ISODOOM_DOOM_WAD") is { Length: > 0 } p)
            return WadArchive.Open(p);
        string upper = Path.Combine(RepoRoot(), "wads", "DOOM.WAD");
        return WadArchive.Open(File.Exists(upper) ? upper : Path.Combine(RepoRoot(), "wads", "doom.wad"));
    }

    /// <summary>The IWAD of an <c>iwad</c> header's name: <c>doom2</c>, <c>tnt</c>, <c>plutonia</c> or (M9) <c>doom</c>.</summary>
    private static Func<WadArchive> IwadNamed(string iwad) => iwad switch
    {
        "doom2" => Doom2,
        "doom" => Doom,
        _ => () => FinalDoom(iwad),
    };

    private static WadArchive Tnt() => FinalDoom("tnt");

    private static WadArchive Plutonia() => FinalDoom("plutonia");

    /// <summary>
    /// The IWAD of a map name: Doom II's for <c>MAPxx</c>, else DOOM1.WAD
    /// (<see cref="Steer.ModeOf(string)"/>); T10.7: <c>ROUTEGEN_IWAD=tnt|plutonia</c>
    /// gives Final Doom's for <c>MAPxx</c> (<c>--info</c>, <c>--probe</c>, …);
    /// M9: <c>ROUTEGEN_IWAD=doom</c> The Ultimate Doom's for <c>ExMy</c>, which
    /// E2-E4 maps get without it (DOOM1.WAD has only E1).
    /// </summary>
    private static WadArchive IwadOf(string map) =>
        Steer.ModeOf(map) != GameMode.commercial
            ? Environment.GetEnvironmentVariable("ROUTEGEN_IWAD") == "doom" || !map.StartsWith("E1", StringComparison.OrdinalIgnoreCase) ? Doom() : Doom1()
            : Environment.GetEnvironmentVariable("ROUTEGEN_IWAD") is "tnt" or "plutonia" ? FinalDoom(Environment.GetEnvironmentVariable("ROUTEGEN_IWAD")!)
            : Doom2();

    private static WadArchive Synthetic() =>
        new([WadFile.FromBytes(IsoDoom.Tools.SyntheticIwad.SyntheticIwad.Build(), IsoDoom.Tools.SyntheticIwad.SyntheticIwad.DefaultFileName)]);

    private static WadArchive TestMapWad(string name) =>
        new([WadFile.FromBytes(RouteTestMaps.Get(name).Build(), name + ".wad")]);

    private sealed record Script(Func<WadArchive> Wad, string Map, string Header, Action<Steer, Action<string>> Run, (int X, int Y, int Angle)? Start = null,
        IsoDoom.Sim.skill_t Skill = IsoDoom.Sim.skill_t.sk_medium, bool Monsters = false, int Players = 1, int Deathmatch = 0, int Timer = 0);

    /// <summary>
    /// T10.3: a duel with the monster spawned at (<paramref name="x"/>,
    /// <paramref name="y"/>): every other monster is killed asleep with route
    /// events on the first tic, a hurt of 1 wakes it (the deaf too), it fights
    /// the standing player until <paramref name="done"/> (on its
    /// <see cref="Brawler"/>'s attack log) or <paramref name="max"/> tics,
    /// then <paramref name="after"/> more tics; a route event takes half its
    /// health, 40 tics later another the rest, and 80 tics of its death end the route.
    /// </summary>
    private static Action<Steer, Action<string>> Duel(int x, int y, Func<Brawler, bool> done, int after = 30, int max = 1500) => (g, log) =>
    {
        var b = new Brawler(g) { Firing = false };
        b.KillAllBut(m => m.spawnpoint.X == x && m.spawnpoint.Y == y);
        g.Wait(1);
        IsoDoom.Sim.mobj_t mon = g.Spawned(x, y) ?? throw new InvalidOperationException($"nothing spawned at {x}, {y}");
        g.Damage(x, y, 1); // (deaf or not: wakes to the hurt)
        for (int t = 0; t < max && !done(b); t++)
        {
            g.Wait(1);
            if (t % 50 == 0)
                log($"h{g.Mo.health} {mon.type} {mon.state} ({mon.x >> 16},{mon.y >> 16}) h{mon.health}; {b}");
        }
        if (!done(b))
            throw new InvalidOperationException("the duel's attacks never all came: " + b);
        g.Wait(after);
        log("fought " + b);
        g.Damage(x, y, System.Math.Max(1, mon.info.spawnhealth / 2));
        g.Wait(40);
        log($"hurt h{g.Mo.health} {mon.state} h{mon.health}");
        if (mon.health > 0)
            g.Damage(x, y, mon.health);
        g.Wait(80);
        log($"done h{g.Mo.health} {mon.state}; {b}");
    };

    /// <summary>
    /// T9.2: a boss ending: <see cref="Duel"/>'s fight with the boss spawned at (<paramref name="x"/>,
    /// <paramref name="y"/>), every other monster killed asleep, until <paramref name="done"/>; then it is
    /// hurt and killed with route events and its <c>A_BossDeath</c> runs: with <paramref name="exit"/>
    /// the route ends with the tic that leaves the level (<c>G_ExitLevel</c>), else it waits for the
    /// movers of the sectors tagged 666 to stop (and 30 tics more).
    /// </summary>
    private static Action<Steer, Action<string>> BossEnd(int x, int y, Func<Brawler, bool> done, bool exit, int max = 1500) => (g, log) =>
    {
        var b = new Brawler(g) { Firing = false };
        b.KillAllBut(m => m.spawnpoint.X == x && m.spawnpoint.Y == y);
        g.Wait(1);
        IsoDoom.Sim.mobj_t boss = g.Spawned(x, y) ?? throw new InvalidOperationException($"nothing spawned at {x}, {y}");
        g.Damage(x, y, 1); // (deaf or not: wakes to the hurt)
        for (int t = 0; t < max && !done(b) && g.Mo.health > 0; t++)
        {
            g.Wait(1);
            if (t % 50 == 0)
                log($"h{g.Mo.health} {boss.type} {boss.state} ({boss.x >> 16},{boss.y >> 16}) h{boss.health}; {b}");
        }
        if (!done(b) || g.Mo.health <= 0)
            throw new InvalidOperationException($"the fight did not end as planned (player health {g.Mo.health}): " + b);
        log("fought " + b);
        g.Damage(x, y, boss.health / 2);
        g.Wait(20);
        log($"hurt h{g.Mo.health} {boss.state} h{boss.health}");
        if (boss.health > 0)
            g.Damage(x, y, boss.health);
        if (exit)
        {
            g.WaitExit(400);
            log($"left the level h{g.Mo.health}; {b}");
            return;
        }
        IsoDoom.Sim.sector_t[] tagged = [.. g.World.sectors.Where(sec => sec.tag == 666)];
        g.WaitUntil(() => tagged.Any(sec => sec.specialdata != null), 400);
        if (!tagged.Any(sec => sec.specialdata != null))
            throw new InvalidOperationException("A_BossDeath moved no sector tagged 666");
        log("666 moving " + g.Heights([.. tagged.Select(sec => sec.Index)]));
        foreach (IsoDoom.Sim.sector_t sec in tagged)
            g.WaitIdle(sec.Index);
        g.Wait(30);
        log("666 stopped " + g.Heights([.. tagged.Select(sec => sec.Index)]) + "; " + b);
    };

    /// <summary>
    /// T10.3 (map05-skull-floor), T9.2: the lost soul spawned at (<paramref name="x"/>, <paramref name="y"/>),
    /// every other monster killed asleep with route events, wakes to a hurt of 1 and charges the player, who
    /// steps aside while each charge flies, until two charges hit the floor (P_ZMovement's lost soul bounce);
    /// then it is hurt and killed with route events.
    /// </summary>
    private static Action<Steer, Action<string>> SkullFloor(int x, int y) => (g, log) =>
    {
        var b = new Brawler(g) { Firing = false };
        b.KillAllBut(m => m.spawnpoint.X == x && m.spawnpoint.Y == y);
        g.Wait(1);
        IsoDoom.Sim.mobj_t skull = g.Spawned(x, y)!;
        g.Damage(x, y, 1);
        int floorHits = 0, charges = 0, side = 1;
        for (int t = 0; t < 1200 && floorHits < 2 && g.Mo.health > 0; t++)
        {
            bool flying = (skull.flags & IsoDoom.Sim.mobjflag_t.MF_SKULLFLY) != 0;
            if (flying && skull.z <= skull.floorz)
                floorHits++;
            if (flying && t % 1 == 0)
            {
                charges++;
                g.Tic(0, 40 * side, 0, 0); // step aside
            }
            else
            {
                if (charges > 0)
                    side = -side;
                charges = 0;
                g.Tic(0, 0, g.TurnTo(skull.x / 65536.0, skull.y / 65536.0), 0);
            }
            if (t % 25 == 0)
                log($"h{g.Mo.health} skull {skull.state} z {skull.z >> 16} floor {skull.floorz >> 16} momz {skull.momz} flying {flying} floor hits {floorHits}");
        }
        log($"floor hits {floorHits}");
        if (floorHits == 0)
            throw new InvalidOperationException("the soul never hit the floor charging");
        g.Wait(10);
        g.Damage(x, y, 50);
        g.Wait(40);
        if (skull.health > 0)
            g.Damage(x, y, skull.health);
        g.Wait(60);
        log("done " + b);
    };

    /// <summary>T10.3: whether a <see cref="Brawler"/> logged every action of <paramref name="actions"/> for monster <paramref name="type"/> (<c>mobjtype_t</c> without <c>MT_</c>).</summary>
    private static Func<Brawler, bool> Seen(string type, params string[] actions) =>
        b => b.Attacks.TryGetValue(type, out SortedSet<string>? a) && actions.All(a.Contains);

    /// <summary>
    /// T11.2: co-op on DOOM1.WAD's E1M1 with <paramref name="players"/> players, steered one after
    /// another (the others stand): each opens door 4 if it is shut, goes through it and on into the
    /// nukage room across line 195, shooting the monsters that attacked with its own weapon.
    /// </summary>
    private static Action<Steer, Action<string>> CoopE1M1(int players) => (g, log) =>
    {
        var gun = new Gunner(g) { Guns = true };
        string State() => string.Join("; ", Enumerable.Range(0, players).Select(i =>
        {
            IsoDoom.Sim.player_t p = g.World.players[i];
            return $"p{i + 1} ({p.mo!.x >> 16},{p.mo.y >> 16}) h{p.health} a{p.armorpoints} {p.readyweapon} k{p.killcount} i{p.itemcount}";
        })) + "; " + g.Monsters();
        int[] lanes = [-2496, -2456, -2536, -2496];
        for (int i = 0; i < players; i++)
        {
            g.Player = i;
            log($"player {i + 1} to door 4: {State()}");
            g.GoTo(1500, lanes[i]);
            g.Door(4, 1544, lanes[i]);
            gun.Clear(300);
            g.GoTo(1600 + (48 * i), lanes[i]);
            gun.Clear(300);
            log($"player {i + 1} through door 4: {State()}");
        }
        // on the ledge above the nukage (floor -24), one spot each
        (int X, int Y)[] ledge = [(2900, -2900), (2980, -2900), (2900, -2800), (2980, -2800)];
        for (int i = 0; i < players; i++)
        {
            g.Player = i;
            g.GoToDoors(ledge[i].X, ledge[i].Y, 12);
            gun.Clear(300);
            log($"player {i + 1} on the ledge: {State()}");
            for (int k = 0; k < players; k++)
            {
                if (g.World.players[k].health <= 0)
                    throw new InvalidOperationException($"player {k + 1} died");
            }
        }
        // the last player down into the nukage across line 195 (the lift lowers)
        g.GoTo(2900, -3100, 12);
        gun.Clear(300);
        g.Wait(35);
        log($"done: {State()}; {gun}");
    };

    /// <summary>
    /// e1m1-exit's way (T5.5) for the player steered: from the start through
    /// door 4, the nukage room, doors 68 and 76, line 308, the lift and doors
    /// 76 and 81 to the exit switch (line 330), pressed on the last tic.
    /// T11.7: also a co-op player's (<see cref="CoopE1M1Exit"/>).
    /// </summary>
    private static void E1M1Exit(Steer g, Action<string> log)
    {
        // Door 4 (lines 151/152, x 1536..1552, y -2560..-2432), from the west.
        g.GoTo(1500, -2496);
        log("at door 4");
        g.Door(4, 1544, -2496);
        // Into the nukage room; across line 195 (2752,-3048)->(3048,-2880): the lift (sector 70) lowers.
        g.GoTo(1600, -2496);
        log("through door 4");
        g.GoTo(2900, -2900, 12);
        log("near line 195 " + g.Heights(70));
        g.GoTo(2900, -3100, 12);
        log("across line 195 " + g.Heights(70));
        // Door 68 (lines 247/248, x 2912..2944, y -3904..-3776): from the east side (front 56 of line 247).
        g.GoTo(2980, -3840, 6);
        log("at door 68 " + g.Heights(70, 68));
        g.Door(68, 2944, -3840);
        g.GoTo(2890, -3840);
        log("through door 68");
        g.GoTo(3008, -3990);
        log("at door 76 " + g.Heights(76));
        g.Door(76, 3008, -4024);
        g.GoTo(3008, -4100);
        log("through door 76");
        g.GoTo(3008, -4250);
        log("across line 308 " + g.Heights(59));
        // Back north to the lift: door 76 from the south, door 68 from the west.
        g.GoTo(3008, -4060, 6);
        log("at door 76 again " + g.Heights(76));
        g.Door(76, 3008, -4024);
        g.GoTo(3008, -3960);
        g.GoTo(2980, -2870, 12);
        log("north of line 195 again " + g.Heights(70, 59));
        g.WaitIdle(70);
        log("lift idle " + g.Heights(70, 59));
        g.Assume[70] = -48;
        g.GoTo(3552, -3872, 12, 50);
        g.Assume.Clear();
        log("on the lift " + g.Heights(70));
        g.WaitIdle(70);
        log("lift up " + g.Heights(70));
        g.GoTo(3650, -3700, 12);
        log("on the ledge");
        g.GoTo(3150, -3300, 12);
        log("in the nukage");
        g.GoTo(3008, -3990, 6);
        log("at door 76 (3) " + g.Heights(76));
        g.Door(76, 3008, -4024);
        g.GoTo(3008, -4100);
        g.GoTo(3008, -4600, 6);
        log("at door 81 " + g.Heights(59, 81));
        g.Door(81, 3008, -4640);
        g.GoTo(3008, -4700);
        g.GoTo(2960, -4768);
        log("at the exit switch");
        g.Face(2900, -4768);
        log("before press");
        g.Tic(0, 0, 0, 2); // the last tic: vanilla's G_ExitLevel ends the level (and the dump) here
        log("textures " + g.World.sides[g.World.lines[330].sidenum[0]].midtexture + " " + string.Join(",", g.World.Unported));
    }

    /// <summary>
    /// T11.7: co-op on DOOM1.WAD's E1M1 to its exit, with
    /// <paramref name="players"/> players, for the net game intermission:
    /// every monster dies asleep on the first tic to damage events, player 1
    /// credited with two of every <paramref name="players"/> + 1 and the
    /// others one each in turn (the kills differ); with <paramref name="frag"/>
    /// player 2 shoots player 3 dead with its pistol first (a frag: the
    /// intermission's frags column); then player 1 takes e1m1-exit's way
    /// (<see cref="E1M1Exit"/>) while the others stand.
    /// </summary>
    private static Action<Steer, Action<string>> CoopE1M1Exit(int players, bool frag) => (g, log) =>
    {
        string State() => string.Join("; ", Enumerable.Range(0, players).Select(i =>
        {
            IsoDoom.Sim.player_t p = g.World.players[i];
            return $"p{i + 1} ({p.mo!.x >> 16},{p.mo.y >> 16}) h{p.health} k{p.killcount} i{p.itemcount} s{p.secretcount} f{string.Join(",", p.frags)}";
        }));
        var monsters = g.World.Mobjs().Where(m => (m.flags & IsoDoom.Sim.mobjflag_t.MF_COUNTKILL) != 0 && m.health > 0).ToList();
        for (int k = 0; k < monsters.Count; k++)
        {
            IsoDoom.Sim.mobj_t m = monsters[k];
            if (g.Spawned(m.spawnpoint.X, m.spawnpoint.Y) != m)
                throw new InvalidOperationException($"two monsters spawned at {m.spawnpoint.X},{m.spawnpoint.Y}");
            g.Player = Math.Max(0, (k % (players + 1)) - 1);
            g.Damage(m.spawnpoint.X, m.spawnpoint.Y, m.health);
        }
        g.Player = 0;
        g.Wait(1);
        log($"monsters dead: {State()}");
        if (frag)
        {
            g.Player = 1;
            g.FireAt(g.World.players[2].mo!, 600);
            if (g.World.players[2].health > 0 || g.World.players[1].frags[2] != 1)
                throw new InvalidOperationException($"player 3 not fragged: {State()}");
            log($"player 3 fragged: {State()}");
            g.Player = 0;
        }
        E1M1Exit(g, log);
        log($"exit: {State()}");
    };

    /// <summary>T11.4: every player in the game's position, health, frags, weapon and cards, for the log.</summary>
    private static string Players(Steer g) => string.Join("; ", Enumerable.Range(0, g.Players).Select(i =>
    {
        IsoDoom.Sim.player_t p = g.World.players[i];
        return $"p{i + 1} ({p.mo!.x >> 16},{p.mo.y >> 16}) h{p.health} {p.playerstate} frags {string.Join(",", p.frags)} {p.readyweapon} owned {string.Join("", p.weaponowned.Select(o => o ? 1 : 0))}"
            + $" cards {string.Join("", p.cards.Select(c => c ? 1 : 0))} ammo {string.Join(",", p.ammo)}";
    }));

    /// <summary>
    /// T11.4: player <paramref name="hunter"/> walks towards player <paramref name="prey"/> (who stands)
    /// until about <paramref name="range"/> units from it, planning through it, then fires at it until it dies.
    /// </summary>
    private static void Hunt(Steer g, int hunter, int prey, double range = 128)
    {
        g.Player = hunter;
        g.PlanThroughPlayers = true;
        IsoDoom.Sim.mobj_t t = g.World.players[prey].mo!;
        double tx = t.x / 65536.0, ty = t.y / 65536.0;
        double D(double x, double y) => System.Math.Sqrt((x - tx) * (x - tx) + (y - ty) * (y - ty));
        if (D(g.X, g.Y) > range)
        {
            List<(double X, double Y)> path = g.PlanDoors(tx, ty);
            int k = path.Count - 1;
            while (k > 0 && D(path[k].X, path[k].Y) < range)
                k--;
            g.GoToDoors(path[k].X, path[k].Y, 16);
        }
        g.PlanThroughPlayers = false;
        g.FireAt(t, 600);
        if (t.health > 0)
            throw new InvalidOperationException($"player {prey + 1} survived player {hunter + 1}'s shots: {Players(g)}");
    }

    /// <summary>T11.4: the steered player steps 64-96 units off (x, y) (an item it took), east, west, north or south, where it fits.</summary>
    private static void StepOff(Steer g, double x, double y)
    {
        foreach ((double dx, double dy) in new[] { (96.0, 0.0), (-96.0, 0.0), (0.0, 96.0), (0.0, -96.0), (64.0, 0.0), (-64.0, 0.0), (0.0, 64.0), (0.0, -64.0) })
        {
            if (!g.Fits(x + dx, y + dy))
                continue;
            try
            {
                g.GoTo(x + dx, y + dy, 16);
                return;
            }
            catch (NoPathException)
            {
            }
        }
        throw new InvalidOperationException($"nowhere to step off ({x}, {y})");
    }

    /// <summary>T11.4: the steered player's planned path length to <paramref name="m"/> (grid steps), or int.MaxValue when there is none.</summary>
    private static int PathLength(Steer g, IsoDoom.Sim.mobj_t m)
    {
        try
        {
            return g.PlanDoors(m.x / 65536.0, m.y / 65536.0).Count;
        }
        catch (NoPathException)
        {
            return int.MaxValue;
        }
    }

    /// <summary>T11.4: dead player <paramref name="player"/> waits <paramref name="wait"/> tics, presses use and is reborn (a netgame's: at once, the next tic).</summary>
    private static void Reborn(Steer g, int player, int wait = 20)
    {
        g.Player = player;
        IsoDoom.Sim.mobj_t body = g.World.players[player].mo!;
        g.Wait(wait);
        g.Tic(0, 0, 0, IsoDoom.Sim.buttoncode_t.BT_USE);
        g.Wait(1);
        if (g.World.players[player].mo == body || g.World.players[player].health != 100)
            throw new InvalidOperationException($"player {player + 1} was not reborn: {Players(g)}");
    }

    /// <summary>
    /// T11.4: deathmatch with two players: player 1 takes the weapon nearest to it of
    /// <paramref name="weapons"/> (it stays: deathmatch 1), player 2 takes the same; player 1 hunts
    /// player 2 with it, player 2 is reborn at a deathmatch start, hunts player 1 with its pistol, and
    /// player 1 is reborn.
    /// </summary>
    private static Action<Steer, Action<string>> Deathmatch2P(params IsoDoom.Sim.mobjtype_t[] weapons) => (g, log) =>
    {
        g.Wait(1); // (G_PlayerReborn's usedown)
        log("spawned: " + Players(g));
        IsoDoom.Sim.mobj_t weapon = g.World.Mobjs().Where(m => weapons.Contains(m.type))
            .OrderBy(m => PathLength(g, m)).First();
        double wx = weapon.x / 65536.0, wy = weapon.y / 65536.0;
        for (int i = 0; i < 2; i++)
        {
            g.Player = i;
            g.GoToDoors(wx, wy, 4);
            g.Wait(2);
            log($"player {i + 1} took the {weapon.type} ({(weapon.function == IsoDoom.Sim.think_t.REMOVED ? "gone" : "stays")}): {Players(g)}");
            StepOff(g, wx, wy); // (off it, so the other can take it)
        }
        Hunt(g, 0, 1);
        log("player 1 fragged player 2: " + Players(g));
        Reborn(g, 1);
        log("player 2 reborn: " + Players(g));
        g.Wait(10);
        Hunt(g, 1, 0);
        log("player 2 fragged player 1: " + Players(g));
        Reborn(g, 0);
        log("player 1 reborn: " + Players(g));
        g.Player = 0;
        g.Wait(35);
        log("done " + Players(g));
    };

    /// <summary>
    /// T11.4: altdeath (deathmatch 2): player 1 takes the nearest of <paramref name="items"/> (it does
    /// not stay) and, once P_RespawnSpecials brought it back 30 s later (an item fog first), takes it again.
    /// </summary>
    private static Action<Steer, Action<string>> AltdeathRespawn(params IsoDoom.Sim.mobjtype_t[] items) => (g, log) =>
    {
        g.Wait(1);
        g.Player = 0;
        log("spawned: " + Players(g));
        IsoDoom.Sim.mobj_t item = g.World.Mobjs().Where(m => items.Contains(m.type))
            .OrderBy(m => PathLength(g, m)).First();
        IsoDoom.Sim.mobjtype_t type = item.type;
        double ix = item.x / 65536.0, iy = item.y / 65536.0;
        g.GoToDoors(ix, iy, 4);
        g.Wait(2);
        if (item.function != IsoDoom.Sim.think_t.REMOVED)
            throw new InvalidOperationException($"the {type} stayed: {Players(g)}");
        log($"took the {type}, queued at {g.World.iquetail}..{g.World.iquehead}: {Players(g)}");
        StepOff(g, ix, iy);
        if (type is IsoDoom.Sim.mobjtype_t.MT_CHAINGUN or IsoDoom.Sim.mobjtype_t.MT_SHOTGUN)
            g.Change(IsoDoom.Sim.weapontype_t.wp_pistol); // (so taking it again switches to it again)
        g.WaitUntil(() => g.World.Mobjs().Any(m => m.type == type && m.x == item.x && m.y == item.y), 1200);
        log($"respawned at tic {g.World.leveltime}: {Players(g)}");
        g.Wait(20);
        g.GoToDoors(ix, iy, 4);
        g.Wait(35);
        log("took it again: " + Players(g));
    };

    /// <summary>The steering scripts by route name.</summary>
    private static readonly SortedDictionary<string, Script> _scripts = new(StringComparer.Ordinal)
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

""", E1M1Exit),
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

""", (g, log) =>
        {
            g.GoToDoors(1136, 352, 8, 50);
            log("red key " + string.Join(",", g.World.players[0].cards));
            g.GoToDoors(-1536, 2070, 8, 50);
            log("at line 777 " + g.Heights(124, 129));
            g.Use(-1536, 2112);
            g.WaitIdle(124);
            log("doors open " + g.Heights(124, 129));
            g.GoToDoors(-440, 2336, 8, 50);
            log("at the exit lift " + g.Heights(49));
            g.GoTo(-352, 2336);
            g.WaitUntil(() => g.World.sectors[49].floorheight == -232 * (1 << 16));
            log("lift down " + g.Heights(49));
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

""", (g, log) =>
        {
            g.TightCorners = true;
            bool Open(int sec) => g.World.sectors[sec].ceilingheight - g.World.sectors[sec].floorheight >= 64 * (1 << 16) && g.World.sectors[sec].specialdata is not IsoDoom.Sim.vldoor_t { direction: -1 };
            g.GoToDoors(32, 390, 8, 50);
            log("in sector 104 " + g.Heights(106));
            g.WaitUntil(() => Open(106));
            g.GoTo(152, 792, 8, 50);
            log("blue key " + string.Join(",", g.World.players[0].cards) + " " + g.Heights(106));
            g.GoToDoors(-1248, 1280, 8, 50);
            log("yellow key " + string.Join(",", g.World.players[0].cards));
            g.GoToDoors(-1088, 1500, 8, 50);
            log("at line 592 " + g.Heights(52));
            g.Use(-1088, 1560);
            g.WaitIdle(52);
            log("sector 52 raised " + g.Heights(52));
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

""", (g, log) =>
        {
            g.TightCorners = true;
            g.ShutDoors.UnionWith([52, 58]); // open from the other side only (58 is blue from this one)
            g.Avoid.UnionWith([55, 56]); // the teleporter (lines 787-796) in room 54
            g.GoToDoors(888, 300, 8, 50);
            log("across line 271 " + g.Heights(91));
            g.WaitIdle(91);
            log("sector 91 raised " + g.Heights(91));
            g.GoToDoors(688, 800, 8, 50);
            log("yellow key " + string.Join(",", g.World.players[0].cards));
            g.GoToDoors(-1130, 832, 8, 50);
            log("on sector 21 " + g.Heights(20, 35, 82));
            g.Use(-1060, 832);
            g.WaitIdle(82);
            log("door 82 open " + g.Heights(20, 35, 82));
            g.GoToDoors(-980, 730, 8, 50);
            log("south of line 478 " + g.Heights(12));
            g.Assume[12] = -104;
            g.GoTo(-980, 832, 8, 50);
            log("across line 478 " + g.Heights(12));
            g.GoTo(-800, 832);
            g.Assume.Clear();
            log("on lift 12 " + g.Heights(12));
            g.WaitIdle(12);
            log("lift 12 up " + g.Heights(12));
            g.GoToDoors(192, 1040, 8, 50);
            log("blue key " + string.Join(",", g.World.players[0].cards));
            g.GoTo(200, 950, 8, 50);
            g.Use(200, 890);
            g.WaitIdle(141);
            log("door 141 open " + g.Heights(141));
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

""", (g, log) =>
        {
            g.TightCorners = true;
            g.GoToDoors(1680, -1344, 8, 50);
            log("red key " + string.Join(",", g.World.players[0].cards));
            g.WaitUntil(() => g.World.sectors.Where(s => s.tag == 10).All(s => s.specialdata == null));
            log("tag 10 doors open " + g.Heights(195, 199, 201, 203, 241));
            g.WaitUntil(() => g.World.sectors[187].specialdata == null && g.World.sectors[187].ceilingheight > g.World.sectors[187].floorheight);
            log("door 187 open " + g.Heights(187));
            g.GoToDoors(-1536, -1728, 8, 50);
            log("blue key " + string.Join(",", g.World.players[0].cards));
            g.GoToDoors(2624, -2260, 8, 50);
            log("at line 438 " + g.Heights(242));
            g.Use(2624, -2320);
            g.WaitIdle(242);
            log("door 242 open " + g.Heights(242));
            g.GoToDoors(1088, -608, 8, 50);
            log("yellow key " + string.Join(",", g.World.players[0].cards));
            g.GoToDoors(-64, 2330, 8, 50);
            log("at line 822 " + g.Heights(37));
            g.Use(-64, 2400);
            g.WaitUntil(() => g.World.sectors[37].specialdata == null);
            log("door 37 open " + g.Heights(37));
            g.GoToDoors(-1488, 1384, 8, 50);
            log("at line 599 " + g.Heights(28));
            g.Use(-1440, 1384);
            g.WaitUntil(() => g.World.sectors[28].specialdata == null);
            log("door 28 open " + g.Heights(28));
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

""", (g, log) =>
        {
            g.TightCorners = true;
            g.GoToDoors(1344, -700, 8, 50);
            log("in sector 22 " + g.Heights(23));
            g.Assume[23] = 32;
            g.GoTo(1440, -704, 8, 50);
            g.Assume.Clear();
            log("on lift 23 " + g.Heights(23));
            g.WaitIdle(23);
            log("lift 23 up " + g.Heights(23));
            g.GoTo(1344, -192, 8, 50);
            log("yellow key " + string.Join(",", g.World.players[0].cards));
            g.GoToDoors(-1120, -120, 8, 50);
            log("east of line 248 " + g.Heights(70));
            g.Assume[70] = 48;
            g.GoTo(-1240, -60, 8, 50);
            log("across line 248 " + g.Heights(70));
            g.GoTo(-1376, -64, 8, 50);
            g.Assume.Clear();
            log("on lift 70 " + g.Heights(70));
            g.WaitIdle(70);
            log("lift 70 up " + g.Heights(70));
            g.GoTo(-1424, 384, 8, 50);
            log("red key " + string.Join(",", g.World.players[0].cards));
            g.GoTo(-1376, 40, 8, 50);
            g.GoTo(-1376, -64);
            log("across line 644 " + g.Heights(70));
            g.WaitUntil(() => g.World.sectors[70].floorheight == 48 * (1 << 16));
            g.Assume[70] = 48;
            g.GoTo(-1220, -80, 8, 50);
            g.Assume.Clear();
            log("off lift 70 " + g.Heights(70));
            g.GoToDoors(-990, 272, 8, 50);
            log("blue key " + string.Join(",", g.World.players[0].cards));
            g.GoToDoors(352, -1760, 8, 50);
            log("at line 762 " + g.Heights(104));
            g.Use(352, -1700);
            g.WaitIdle(104);
            log("door 104 open " + g.Heights(104));
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

""", (g, log) =>
        {
            g.TightCorners = true;
            g.GoTo(-88, -224);
            g.Use(-40, -224);
            g.WaitIdle(10);
            log("sector 10 lowered " + g.Heights(10));
            g.GoToDoors(416, 2390, 8, 50);
            log("at line 231 " + g.Heights(28));
            g.Use(416, 2450);
            g.WaitUntil(() => g.World.sectors[28].floorheight == -96 * (1 << 16));
            g.Assume[28] = -96;
            g.GoTo(416, 2464);
            g.Assume.Clear();
            log("on lift 28 " + g.Heights(28));
            g.WaitIdle(28);
            log("lift 28 up " + g.Heights(28));
            g.GoToDoors(416, 3300, 8, 50);
            log("at the wall");
        }),
        ["doom-e1m8-exit"] = new(Doom, "E1M8", """
# The Ultimate Doom E1M8 (T9.4): e1m8-exit's way on The Ultimate Doom's
# IWAD (its finale ends on CREDIT, not HELP2): from the arena, the stairs
# switch line 233, up and across the teleport line 299 into the
# damage-and-exit sector 66 (11) until its hits leave 10 health or less:
# G_ExitLevel, the last tic. Written by tools/RouteGen (doom-e1m8-exit).
iwad doom
map E1M8
exit normal
""", (g, log) =>
        {
            g.TightCorners = true;
            g.Wait(1);
            g.Use(320, 4440);
            g.WaitUntil(() => g.World.sectors.All(s => s.specialdata is not IsoDoom.Sim.floormove_t));
            g.GoTo(448, 4980, 8, 50);
            g.Teleport(448, 5120);
            g.WaitExit();
            log("health " + g.World.players[0].health);
        }, (320, 4384, 90)),
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
""", (g, log) =>
        {
            g.TightCorners = true;
            g.Wait(1); // G_PlayerReborn's usedown: use needs a tic without it first
            g.Use(320, 4440);
            g.WaitUntil(() => g.World.sectors.All(s => s.specialdata is not IsoDoom.Sim.floormove_t));
            log("stairs built " + g.Heights(53, 54, 60, 67));
            g.GoTo(448, 4980, 8, 50);
            log("on sector 52");
            g.Teleport(448, 5120);
            log("teleported, health " + g.World.players[0].health);
            g.WaitExit();
            log("health " + g.World.players[0].health);
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

""", (g, log) =>
        {
            g.TightCorners = true;
            g.GoToDoors(-576, 64, 8, 50);
            log("yellow key " + string.Join(",", g.World.players[0].cards));
            g.GoToDoors(1856, -1088, 8, 50);
            log("red key " + string.Join(",", g.World.players[0].cards));
            g.GoToDoors(-576, -1344, 4, 50); // barrels (y -1376) in front of the switch: use over them
            g.Walk(-576, -1400, 4);
            log("at line 362 " + g.Heights(107, 126, 130));
            g.Use(-576, -1430);
            g.WaitUntil(() => g.World.sectors[130].specialdata == null);
            log("tag 1 doors open " + g.Heights(107, 126, 130));
            g.GoToDoors(704, -1024, 8, 50);
            log("blue key " + string.Join(",", g.World.players[0].cards));
            g.GoToDoors(384, 1444, 8, 50);
            log("at line 567 " + g.Heights(17));
            g.Use(384, 1380);
            g.WaitIdle(17);
            log("sector 17 raised " + g.Heights(17));
            g.GoTo(200, 1450, 8, 50);
            log("across lines 570/571 " + g.Heights(48));
            g.WaitUntil(() => g.World.sectors[48].specialdata is IsoDoom.Sim.vldoor_t { direction: 0 });
            g.GoTo(224, 960, 8, 50);
            log("at line 587 " + g.Heights(49));
            g.Use(300, 960);
            g.WaitUntil(() => g.World.sectors[49].floorheight == 0);
            g.Assume[49] = 0;
            g.GoTo(288, 960);
            g.Assume.Clear();
            log("on lift 49 " + g.Heights(49));
            g.WaitIdle(49);
            log("lift 49 up " + g.Heights(49));
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

""", (g, log) =>
        {
            // Lift 1 (sector 89, tag 1): into sector 85 across line 315 (WR 88).
            g.GoTo(-640, 100);
            log("north of line 315 " + g.Heights(89));
            g.Assume[89] = 24;
            g.GoTo(-640, -64, 8, 25);
            g.Assume.Clear();
            log("on lift 1 " + g.Heights(89));
            g.WaitIdle(89);
            log("lift 1 up " + g.Heights(89));
            g.GoTo(-540, -64);
            log("on sector 90 " + g.Heights(89));
            g.WaitIdle(89);
            log("lift 1 idle " + g.Heights(89));
            g.GoTo(-640, 32);
            log("in sector 85 " + g.Heights(89));
            g.WaitIdle(89);
            g.Use(-640, -10);
            log("used line 313 " + g.Heights(89));
            g.Wait(10);
            log("lift 1 lowering " + g.Heights(89));
            // Lift 4 (sector 109, tag 4): across line 289 (WR 88) into sector 107.
            g.GoTo(-100, 288, 8, 50);
            log("east of line 289 " + g.Heights(109));
            g.GoTo(-220, 300);
            log("in sector 107 " + g.Heights(109));
            g.Assume[109] = 64;
            g.GoTo(-192, 416);
            g.Assume.Clear();
            log("on lift 4 " + g.Heights(109));
            g.WaitIdle(109);
            log("lift 4 up " + g.Heights(109));
            g.GoTo(-160, 500);
            log("in sector 34 " + g.Heights(109));
            g.WaitIdle(109);
            g.Assume[109] = 64;
            g.GoTo(-200, 350);
            g.Assume.Clear();
            log("in sector 108 " + g.Heights(109));
            g.WaitIdle(109);
            g.Use(-200, 390);
            log("used line 360 " + g.Heights(109));
            g.Wait(10);
            log("lift 4 lowering " + g.Heights(109));
            g.WaitIdle(109);
            log("lift 4 idle " + g.Heights(109));
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

""", (g, log) =>
        {
            g.GoToDoors(-160, -864, 8, 50);
            log("blue key " + string.Join(",", g.World.players[0].cards));
            g.GoToDoors(-290, -1600, 8, 50);
            g.GoTo(-240, -1600);
            log("across line 967 " + g.Heights(16, 17, 18, 13));
            g.WaitUntil(() => g.World.sectors.All(s => s.specialdata == null || s.specialdata is not IsoDoom.Sim.floormove_t));
            log("stairs built " + g.Heights(16, 17, 18, 13));
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

""", (g, log) =>
        {
            g.TightCorners = true;
            bool Open(int sec) => g.World.sectors[sec].ceilingheight - g.World.sectors[sec].floorheight >= 64 * (1 << 16) && g.World.sectors[sec].specialdata == null;
            g.GoToDoors(-1936, -2120, 8, 50);
            log("south of line 535 " + g.Heights(121));
            g.Use(-1936, -2060);
            g.WaitUntil(() => Open(121));
            log("door 121 open " + g.Heights(121));
            g.GoToDoors(-2220, -2190, 8, 50);
            log("in sector 120 " + g.Heights(165));
            g.Assume[165] = 128;
            g.GoTo(-2664, -1360, 6, 50);
            g.Assume.Clear();
            log("at lift 165 " + g.Heights(165));
            g.Walk(-2700, -1324, 3, 25);
            log("on lift 165 " + g.Heights(165, 167));
            g.WaitIdle(165);
            g.WaitUntil(() => Open(167));
            log("lift up, door 167 open " + g.Heights(165, 167));
            g.GoToDoors(-2416, -944, 4, 50);
            log("in sector 173 " + g.Heights(66));
            g.Use(-2380, -910);
            g.WaitIdle(66);
            log("nukage raised " + g.Heights(66));
            g.GoToDoors(-2080, -1250, 8, 50);
            g.GoTo(-2080, -1350, 8, 50);
            log("across line 462 " + g.Heights(171));
            g.Assume[171] = 112;
            g.GoTo(-2080, -1436);
            g.Assume.Clear();
            log("on lift 171 " + g.Heights(171));
            g.WaitIdle(171);
            log("lift 171 up " + g.Heights(171));
            g.Door(172, -2064, -1470);
            g.GoTo(-2144, -1700, 8, 50);
            log("in sector 169 " + g.Heights(168));
            g.Assume[168] = 32;
            g.GoTo(-2144, -1760);
            g.Assume.Clear();
            log("on lift 168 " + g.Heights(168));
            g.WaitUntil(() => g.World.sectors[168].floorheight == 32 * (1 << 16));
            g.Assume[168] = 32;
            g.GoToDoors(-1450, -2680, 8, 50);
            g.Assume.Clear();
            log("in sector 66 " + g.Heights(51));
            g.GoTo(-1580, -2760, 8, 50);
            log("across line 988 " + g.Heights(51));
            g.WaitUntil(() => Open(51));
            g.GoTo(-1504, -2300, 8, 50);
            g.GoTo(-1504, -2080, 8, 50);
            log("across line 54 " + g.Heights(53));
            g.WaitUntil(() => Open(53));
            g.GoTo(-1504, -1880, 8, 50);
            log("across line 88 " + g.Heights(33, 34));
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

""", (g, log) =>
        {
            // The nukage bridge: S1 raise to nearest and change (line 1020, 20, tag 16: sectors 48 and 49).
            g.GoToDoors(-1260, -768, 8, 50);
            log("east of line 1020 " + g.Heights(48, 49) + " " + g.World.sectors[48].floorpic);
            g.Use(-1330, -768);
            log("used line 1020 " + g.Heights(48, 49) + " " + g.World.sectors[48].floorpic + " special " + g.World.sectors[48].special);
            g.WaitUntil(() => g.World.sectors[48].specialdata == null && g.World.sectors[49].specialdata == null);
            log("bridge up " + g.Heights(48, 49));
            // Lift 4 (sector 168, tag 4): across line 179 (WR 88) into sector 122.
            g.GoToDoors(-2144, -1930, 8, 50);
            log("south of line 179 " + g.Heights(168));
            g.GoTo(-2144, -1850);
            log("in sector 122 " + g.Heights(168));
            g.Assume[168] = 32;
            g.GoTo(-2144, -1760);
            g.Assume.Clear();
            log("on lift 4 " + g.Heights(168));
            g.WaitIdle(168);
            log("lift 4 up " + g.Heights(168));
            g.GoTo(-2144, -1650);
            log("in sector 169 " + g.Heights(168));
            g.WaitIdle(168);
            g.Assume[168] = 32;
            g.GoTo(-2144, -1840);
            g.Assume.Clear();
            log("back in sector 122 " + g.Heights(168));
            g.WaitIdle(168);
            g.Use(-2144, -1780);
            log("used line 178 " + g.Heights(168));
            g.Wait(20);
            log("lift 4 lowering " + g.Heights(168));
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
""", (g, log) =>
        {
            g.Teleport(-800, 1510, 50, hold: 24);
            log("teleported");
            g.Wait(80);
            log("fog gone: " + g.World.Mobjs().Count(m => m.type == IsoDoom.Sim.mobjtype_t.MT_TFOG));
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
""", (g, log) =>
        {
            g.Teleport(448, 5120);
            log("teleported");
            g.Wait(80);
            log("fog gone: " + g.World.Mobjs().Count(m => m.type == IsoDoom.Sim.mobjtype_t.MT_TFOG));
        }, (448, 4980, 90)),
        ["synthetic-exit"] = new(Synthetic, "E1M1", """
# The synthetic E1M1 (T5.9): from the start north to its S1 exit switch
# (line 0, 11, 128 units ahead), the last tic, so the dump's exit column is
# checked in CI. Written by tools/RouteGen (synthetic-exit).
iwad synthetic
exit normal

""", (g, log) =>
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
""", (g, log) =>
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

""", (g, log) =>
        {
            g.GoTo(480, -96);
            log("cards " + string.Join(",", g.World.players[0].cards));
            g.GoTo(690, -96);
            log("cards " + string.Join(",", g.World.players[0].cards));
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

""", (g, log) =>
        {
            var gun = new Gunner(g) { Every = 6 };
            g.GoTo(1500, -2496);
            log("at door 4 " + g.Monsters());
            g.Door(4, 1544, -2496);
            gun.Clear(400);
            log("door 4 clear " + g.Monsters());
            g.GoTo(1600, -2496);
            gun.Clear(400);
            log("through door 4 " + g.Monsters());
            g.GoTo(2900, -2900, 12);
            gun.Clear(400);
            log("nukage room " + g.Monsters());
            g.GoTo(2900, -3100, 12);
            gun.Clear(400);
            log("across line 195 " + g.Monsters());
            g.GoTo(2980, -3840, 6);
            g.Door(68, 2944, -3840);
            gun.Clear(400);
            g.GoTo(2890, -3840);
            gun.Clear(400);
            log("through door 68 " + g.Monsters());
            g.GoTo(3008, -3990);
            g.Door(76, 3008, -4024);
            gun.Clear(400);
            log("door 76 " + g.Monsters());
            g.GoTo(3008, -4100);
            gun.Clear(400);
            log("through door 76 " + g.Monsters());
            g.Wait(100);
            gun.Clear(400);
            g.GoTo(3008, -4600, 6);
            gun.Clear(400);
            log("at door 81 " + g.Monsters());
            g.Door(81, 3008, -4640);
            g.Wait(60);
            gun.Clear(400);
            log("door 81 " + g.Monsters());
            log(gun.ToString());
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

""", (g, log) =>
        {
            var gun = new Gunner(g) { Guns = true };
            IsoDoom.Sim.player_t p = g.World.players[0];
            string State() => $"weapon {p.readyweapon} ammo {string.Join(",", p.ammo)} " + g.Monsters();
            g.GoTo(1500, -2496);
            log("at door 4 " + State());
            g.Door(4, 1544, -2496);
            gun.Clear(400);
            log("door 4 clear " + State());
            g.GoTo(1600, -2496);
            gun.Clear(400);
            log("through door 4 " + State());
            g.Wait(60);
            gun.Clear(400);
            log("waited " + State());
            // the shotgun a shotgun guy dropped: walk over it (the pickup switches to it)
            if (g.World.Mobjs().Where(m => m.type == IsoDoom.Sim.mobjtype_t.MT_SHOTGUN).OrderBy(m => System.Math.Abs(m.x - g.Mo.x) + System.Math.Abs(m.y - g.Mo.y)).FirstOrDefault() is { } sg)
            {
                g.GoTo(sg.x / 65536.0, sg.y / 65536.0, 4);
                g.Wait(30);
                log("shotgun " + State());
            }
            g.Wait(100);
            gun.Clear(400);
            log("waited " + State());
            log(gun.ToString());
        }, Skill: IsoDoom.Sim.skill_t.sk_hard, Monsters: true),
        ["coop-e1m1-2p"] = new(Doom1, "E1M1", """
# DOOM1.WAD E1M1 in co-op, two players, skill 3 with monsters (T11.2): a
# netgame (vanilla's netdemo). The players are steered one after another (a
# player not steered stands): each opens door 4 when it is shut and goes
# through it, then each walks to its spot on the ledge above the nukage room,
# shooting the monsters that attacked with its pistol (BT_ATTACK); the last
# drops into the nukage across line 195 (the lift lowers). Written by
# tools/RouteGen (coop-e1m1-2p).
iwad doom1
players 2
monsters

""", CoopE1M1(2), Monsters: true, Players: 2),
        ["synthetic-coop"] = new(Synthetic, "E1M1", """
# The synthetic E1M1 in co-op (T11.2), four players (T11.5a's starts 2-4
# around player 1's), skill 1 with monsters: six of the nine imps die asleep
# on the first tic, to damage events of players 1-4 in turn (pN: the kills are
# theirs); the three left (two around the start, the one east) wake, scratch
# and throw fireballs at the players, who take turns of 20 tics shooting the
# nearest that attacked with their pistols (BT_ATTACK; the others stand
# meanwhile) until none is left. Written by
# tools/RouteGen (synthetic-coop); its dump is committed (CI).
iwad synthetic
skill 1
players 4
monsters
""", (g, log) =>
        {
            var gun = new Gunner(g) { Guns = true };
            string State() => string.Join("; ", Enumerable.Range(0, 4).Select(i =>
            {
                IsoDoom.Sim.player_t p = g.World.players[i];
                return $"p{i + 1} ({p.mo!.x >> 16},{p.mo.y >> 16}) h{p.health} k{p.killcount} a{p.ammo[0]}";
            })) + "; " + g.Monsters();
            // six of the nine imps die asleep on the first tic, each to a damage event of another player (pN)
            (int X, int Y)[] spared = [(-80, 80), (80, -80), (320, 64)];
            var asleep = g.World.Mobjs().Where(m => m.type == IsoDoom.Sim.mobjtype_t.MT_TROOP && !spared.Contains((m.spawnpoint.X, m.spawnpoint.Y))).ToList();
            for (int k = 0; k < asleep.Count; k++)
            {
                g.Player = k % 4;
                g.Damage(asleep[k].spawnpoint.X, asleep[k].spawnpoint.Y, 100);
            }
            g.Player = 0;
            g.Wait(30);
            log("awake " + State());
            for (int turn = 0; turn < 60 && g.World.Mobjs().Any(m => m.type == IsoDoom.Sim.mobjtype_t.MT_TROOP && m.health > 0); turn++)
            {
                g.Player = turn % 4;
                gun.Clear(20);
                if (g.World.Mobjs().Any(m => m.type == IsoDoom.Sim.mobjtype_t.MT_TROOP && m.health > 0) && g.Cmds.Count % 20 != 0)
                    g.Wait(20 - (g.Cmds.Count % 20)); // (a turn without targets in sight)
                if (turn % 4 == 3)
                    log($"turn {turn}: {State()}");
                for (int k = 0; k < 4; k++)
                {
                    if (g.World.players[k].health <= 0)
                        throw new InvalidOperationException($"player {k + 1} died");
                }
            }
            g.Player = 0;
            g.Wait(35);
            log($"done {State()}; {gun}");
        }, Skill: IsoDoom.Sim.skill_t.sk_baby, Monsters: true, Players: 4),
        ["testmap-coop"] = new(() => TestMapWad("coop"), "E1M1", """
# The co-op test map (T11.2, RouteTestMaps "coop": T11.1's NetSimTests map),
# four players in co-op, skill 3 with monsters: player 1 opens the DR door
# (line D | A), then each player in turn walks west through it to its line's
# health bonus (player 4 first over the netgame-only shotgun, which it
# switches to) and shoots the monster on its line with its own weapon
# (BT_ATTACK); the others stand meanwhile. Written by tools/RouteGen
# (testmap-coop); its dump is committed (CI).
iwad testmap
map coop
players 4
monsters

""", (g, log) =>
        {
            int[] lanes = [64, 192, 320, 448];
            string State() => string.Join("; ", Enumerable.Range(0, 4).Select(i =>
            {
                IsoDoom.Sim.player_t p = g.World.players[i];
                return $"p{i + 1} ({p.mo!.x >> 16},{p.mo.y >> 16}) h{p.health} {p.readyweapon} k{p.killcount} i{p.itemcount}";
            })) + "; " + g.Monsters();
            int door = Array.FindIndex(g.World.sectors, s => s.ceilingheight == s.floorheight);
            g.Wait(1); // (G_PlayerReborn's usedown)
            g.GoTo(560, 64);
            g.Door(door, 520, 64);
            log("door open " + State());
            for (int i = 0; i < 4; i++)
            {
                g.Player = i;
                if (i == 3)
                    g.GoToDoors(300, lanes[i]); // the netgame's shotgun
                g.GoToDoors(400, lanes[i]);
                log($"player {i + 1} at its bonus: {State()}");
                IsoDoom.Sim.mobj_t? target = g.World.Mobjs().Where(m => (m.flags & IsoDoom.Sim.mobjflag_t.MF_COUNTKILL) != 0 && m.health > 0)
                    .OrderBy(m => System.Math.Abs((m.y >> 16) - lanes[i])).FirstOrDefault();
                if (target != null)
                {
                    g.FireAt(target);
                }
                else
                {
                    for (int t = 0; t < 40; t++)
                        g.Fire(0, lanes[i]); // nothing left: at the west wall
                    g.Wait(1);
                }
                log($"player {i + 1} fired: {State()}");
                for (int k = 0; k < 4; k++)
                {
                    if (g.World.players[k].health <= 0)
                        throw new InvalidOperationException($"player {k + 1} died");
                }
            }
            g.Player = 0;
            g.Wait(35);
            log("done " + State());
        }, Monsters: true, Players: 4),
        ["coop-e1m1-exit-2p"] = new(Doom1, "E1M1", """
# DOOM1.WAD E1M1 in co-op to its exit (T11.7: the net game intermission),
# two players, skill 3 with monsters: every monster dies asleep on the
# first tic to damage events, player 1 credited with two of every three,
# player 2 with the third; then player 1 takes e1m1-exit's way to the exit
# switch (the last tic) while player 2 stands. Written by tools/RouteGen
# (coop-e1m1-exit-2p).
iwad doom1
players 2
monsters
exit normal

""", CoopE1M1Exit(2, frag: false), Monsters: true, Players: 2),
        ["coop-e1m1-exit-4p"] = new(Doom1, "E1M1", """
# DOOM1.WAD E1M1 in co-op to its exit (T11.7: the net game intermission
# with its frags column), four players, skill 3 with monsters: every monster
# dies asleep on the first tic to damage events, player 1 credited with two
# of every five, players 2-4 with one each; player 2 shoots player 3 dead
# with its pistol (a frag, BT_ATTACK; player 3 stays dead); then player 1
# takes e1m1-exit's way to the exit switch (the last tic) while the others
# stand. Written by tools/RouteGen (coop-e1m1-exit-4p).
iwad doom1
players 4
monsters
exit normal

""", CoopE1M1Exit(4, frag: true), Monsters: true, Players: 4),
        ["coop-e1m1-4p"] = new(Doom1, "E1M1", """
# DOOM1.WAD E1M1 in co-op, four players, skill 3 with monsters (T11.2):
# coop-e1m1-2p's way for all four, each through door 4 (reopening it when it
# shut) and then to its spot on the ledge, shooting what attacked; player 4
# drops into the nukage across line 195. Written by tools/RouteGen
# (coop-e1m1-4p).
iwad doom1
players 4
monsters

""", CoopE1M1(4), Monsters: true, Players: 4),
        ["testmap-dm"] = new(() => TestMapWad("dm"), "E1M1", """
# The deathmatch test map (T11.4, RouteTestMaps "dm"), two players in
# deathmatch: both spawn at random deathmatch starts (G_DeathMatchSpawnPlayer)
# with every card; player 1 takes the nearest weapon, which stays
# (deathmatch 1), then player 2 takes it too; player 1 hunts player 2 with it
# (BT_ATTACK) and frags it, player 2 presses use and is reborn at a
# deathmatch start (G_CheckSpot's fog, the body queue), hunts player 1 with
# its pistol and frags it, and player 1 is reborn. Written by tools/RouteGen
# (testmap-dm); its dump is committed (CI).
iwad testmap
map dm
players 2
deathmatch 1

""", Deathmatch2P(IsoDoom.Sim.mobjtype_t.MT_SHOTGUN, IsoDoom.Sim.mobjtype_t.MT_CHAINGUN), Players: 2, Deathmatch: 1),
        ["testmap-altdeath"] = new(() => TestMapWad("dm"), "E1M1", """
# The deathmatch test map (T11.4) in altdeath (deathmatch 2), player 1 alone
# (vanilla's -altdeath without a netgame): it spawns at a random deathmatch
# start, takes the chaingun (which does not stay), switches back to the
# pistol, and 30 seconds later P_RespawnSpecials brings the chaingun back
# with an item fog; it takes it again. Written by tools/RouteGen
# (testmap-altdeath); its dump is committed (CI).
iwad testmap
map dm
deathmatch 2

""", AltdeathRespawn(IsoDoom.Sim.mobjtype_t.MT_CHAINGUN), Deathmatch: 2),
        ["dm-e1m1-2p"] = new(Doom1, "E1M1", """
# DOOM1.WAD E1M1 in deathmatch, two players, no monsters (T11.4): random
# deathmatch starts and every card; player 1 takes the nearest weapon, which
# stays (deathmatch 1), player 2 takes it too; player 1 hunts player 2 and
# frags it, player 2 is reborn at a deathmatch start, hunts player 1 with its
# pistol and frags it, and player 1 is reborn. Written by tools/RouteGen
# (dm-e1m1-2p).
iwad doom1
players 2
deathmatch 1

""", Deathmatch2P(IsoDoom.Sim.mobjtype_t.MT_SHOTGUN, IsoDoom.Sim.mobjtype_t.MT_CHAINGUN, IsoDoom.Sim.mobjtype_t.MT_MISC25,
            IsoDoom.Sim.mobjtype_t.MT_MISC26, IsoDoom.Sim.mobjtype_t.MT_MISC27, IsoDoom.Sim.mobjtype_t.MT_MISC28), Players: 2, Deathmatch: 1),
        ["dm2-e1m1-respawn"] = new(Doom1, "E1M1", """
# DOOM1.WAD E1M1 in altdeath (deathmatch 2), two players, no monsters
# (T11.4): player 1 takes the nearest weapon or box of ammo, which does not
# stay, and 30 seconds later P_RespawnSpecials brings it back with an item
# fog; player 1 takes it again. Written by tools/RouteGen (dm2-e1m1-respawn).
iwad doom1
players 2
deathmatch 2

""", AltdeathRespawn(IsoDoom.Sim.mobjtype_t.MT_SHOTGUN, IsoDoom.Sim.mobjtype_t.MT_CHAINGUN, IsoDoom.Sim.mobjtype_t.MT_CLIP,
            IsoDoom.Sim.mobjtype_t.MT_MISC17, IsoDoom.Sim.mobjtype_t.MT_MISC22, IsoDoom.Sim.mobjtype_t.MT_MISC23), Players: 2, Deathmatch: 2),
        ["dm-e1m1-timer"] = new(Doom1, "E1M1", """
# DOOM1.WAD E1M1 in deathmatch with -timer 1, two players, no monsters
# (T11.4): the players stand at their deathmatch starts until the level
# timer (P_SpawnSpecials' levelTimeCount, 60 * 35 tics) ends the level, the
# route's last tic. Written by tools/RouteGen (dm-e1m1-timer).
iwad doom1
players 2
deathmatch 1
timer 1
exit normal

""", (g, log) =>
        {
            log("spawned: " + Players(g));
            g.WaitExit(2200);
            log($"left at tic {g.World.leveltime}: " + Players(g));
        }, Players: 2, Deathmatch: 1, Timer: 1),
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

""", (g, log) =>
        {
            var gun = new Gunner(g) { Every = 8, Rockets = true };
            g.GoTo(1500, -2496);
            log("at door 4 " + g.Monsters());
            g.Door(4, 1544, -2496);
            gun.Clear(400);
            log("door 4 clear " + g.Monsters());
            g.GoTo(1600, -2496);
            gun.Clear(400);
            log("through door 4 " + g.Monsters());
            g.Wait(60);
            gun.Clear(400);
            log("waited " + g.Monsters());
            log(gun.ToString());
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
""", (g, log) =>
        {
            var gun = new Gunner(g) { Every = 6 };
            g.Wait(1);
            g.Alert();
            g.Wait(60);
            gun.Clear(400);
            log("start " + g.Monsters());
            g.GoToDoors(300, 300, 8, 25);
            gun.Clear(400);
            log("north " + g.Monsters());
            g.GoToDoors(1136, 352, 8, 25);
            gun.Clear(400);
            log("red key " + g.Monsters());
            g.Wait(150);
            gun.Clear(600);
            log("red key, waited " + g.Monsters());
            g.GoToDoors(300, 300, 8, 25);
            gun.Clear(400);
            log("north again " + g.Monsters());
            g.GoToDoors(-32, -240, 8, 25);
            g.Wait(100);
            gun.Clear(400);
            log("start again " + g.Monsters());
            log(gun.ToString());
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
""", (g, log) =>
        {
            var gun = new Gunner(g) { Amount = m => 75, Every = 6 };
            g.GoTo(-88, -224);
            g.Use(-40, -224);
            g.WaitIdle(10);
            log("sector 10 lowered " + g.Heights(10) + " " + g.Monsters());
            g.GoTo(250, -224);
            g.Wait(150);
            gun.Clear(600);
            g.Wait(100);
            gun.Clear(600);
            log("demons " + g.Monsters());
            log(gun.ToString());
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
""", (g, log) =>
        {
            var gun = new Gunner(g) { Amount = m => m.type == IsoDoom.Sim.mobjtype_t.MT_BRUISER ? 200 : 75, Every = 4 };
            g.TightCorners = true;
            g.Wait(1); // (G_PlayerReborn's usedown)
            g.Use(416, 2450);
            g.WaitUntil(() => g.World.sectors[28].floorheight == -96 * (1 << 16));
            g.Assume[28] = -96;
            g.GoTo(416, 2464);
            g.Assume.Clear();
            g.WaitIdle(28);
            log("lift 28 up " + g.Monsters());
            g.GoToDoors(416, 2600, 8, 25);
            log("hall " + g.Monsters());
            for (int i = 0; i < 20; i++)
            {
                g.Wait(40);
                log(g.Monsters() + " " + g.Heights(30) + " fogs " + g.World.Mobjs().Count(m => m.type == IsoDoom.Sim.mobjtype_t.MT_TFOG));
            }
            log(gun.ToString());
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
""", (g, log) =>
        {
            var gun = new Gunner(g) { Every = 8, Amount = m => 30 };
            for (int i = 0; i < 8; i++)
            {
                g.Wait(30);
                log(g.Monsters());
            }
            gun.Clear(600);
            log(g.Monsters());
            // the barrel (64, 64): shot, it explodes (A_Explode) and hurts the player
            g.Alert();
            g.Damage(64, 64, 20);
            g.Wait(40);
            log("barrel " + g.Monsters());
            log(gun.ToString());
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
""", (g, log) =>
        {
            var gun = new Gunner(g) { Amount = m => m.type == IsoDoom.Sim.mobjtype_t.MT_BRUISER ? 100 : 50, Every = 6 };
            g.TightCorners = true;
            g.Wait(1); // (G_PlayerReborn's usedown)
            g.Use(416, 2450);
            g.WaitUntil(() => g.World.sectors[28].floorheight == -96 * (1 << 16));
            g.Assume[28] = -96;
            g.GoTo(416, 2464);
            g.Assume.Clear();
            log("on lift 28 " + g.Heights(28));
            g.WaitIdle(28);
            log("lift 28 up " + g.Monsters());
            g.GoToDoors(416, 2600, 8, 25);
            log("hall " + g.Monsters());
            g.WaitUntil(() => !g.World.Mobjs().Any(m => m.type == IsoDoom.Sim.mobjtype_t.MT_BRUISER && m.health > 0), 1000);
            log("barons dead " + g.Monsters() + " " + g.Heights(30));
            g.WaitUntil(() => g.World.sectors[30].specialdata != null, 50);
            g.WaitIdle(30);
            log("wall down " + g.Heights(30));
            log(gun.ToString());
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

""", (g, log) =>
        {
            g.GoTo(530, 128);
            log("in P " + g.Heights(1, 3));
            g.GoTo(440, 128);
            g.GoTo(290, 128);
            log("at the switch " + g.Heights(1, 3));
            g.Use(250, 128);
            log("used " + g.Heights(1, 3));
            g.Assume[1] = 0;
            g.GoTo(224, 128);
            g.Assume.Clear();
            log("on the lift " + g.Heights(1, 3));
            g.WaitIdle(1);
            log("lift up " + g.Heights(1, 3));
            g.GoTo(120, 128);
            log("on the ledge " + g.Heights(1, 3));
            g.WaitUntil(() => g.World.sectors[1].floorheight == 0);
            g.Assume[1] = 0;
            g.GoTo(224, 128);
            g.Assume.Clear();
            log("on the lift again " + g.Heights(1, 3));
            g.WaitIdle(1);
            log("lift up again " + g.Heights(1, 3));
            g.GoTo(300, 128);
            log("in S " + g.Heights(1, 3));
            g.Use(250, 128);
            g.WaitIdle(1);
            log("lift idle " + g.Heights(1, 3));
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

""", (g, log) =>
        {
            g.Teleport(250, 128, 50, hold: 20);
            log("in B");
            g.Teleport(500, 128);
            log("in A");
            g.GoTo(450, 128);
            log("in S");
            g.GoTo(608, 128);
            log("in P2");
            g.Teleport(500, 128);
            log("in B again");
            g.Teleport(500, 128, 50);
            log("in B (3)");
            g.Wait(80);
            log("fog " + string.Join(",", g.World.Mobjs().Where(m => m.type == IsoDoom.Sim.mobjtype_t.MT_TFOG).Select(m => $"{m.x >> 16}:{m.y >> 16}")));
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

""", (g, log) =>
        {
            string Missiles() => string.Join(" ", g.World.Mobjs()
                .Where(m => m.type is IsoDoom.Sim.mobjtype_t.MT_ROCKET or IsoDoom.Sim.mobjtype_t.MT_TROOPSHOT or IsoDoom.Sim.mobjtype_t.MT_BARREL or IsoDoom.Sim.mobjtype_t.MT_TROOP)
                .Select(m => $"{m.type.ToString()[3..]}({m.x >> 16},{m.y >> 16},{m.z >> 16}) {m.state.ToString()[2..]} h{m.health}")) + $"; health {g.World.players[0].health}";
            g.Rocket();
            g.Wait(3);
            log("rocket west " + Missiles());
            g.Wait(1);
            log("rocket west, a tic later " + Missiles());
            g.Face(136, 600);
            g.Rocket();
            g.Wait(40);
            log("rocket north (barrel) " + Missiles());
            g.Rocket();
            g.Wait(40);
            log("rocket north (wall) " + Missiles());
            g.Face(600, 256);
            g.Alert();
            bool Ball() => g.World.Mobjs().Any(m => m.type == IsoDoom.Sim.mobjtype_t.MT_TROOPSHOT && (m.flags & IsoDoom.Sim.mobjflag_t.MF_MISSILE) != 0);
            for (int i = 0; i < 3; i++)
            {
                // a fireball comes: strafe out of its way (facing east: + is south)
                g.WaitUntil(Ball, 300);
                for (int k = 0; k < 22; k++)
                    g.Tic(0, i % 2 == 0 ? -40 : 40, 0, 0);
                log("strafed, the imp awake " + Missiles());
                g.Settle();
            }
            mobj_t imp = g.World.Mobjs().First(m => m.type == IsoDoom.Sim.mobjtype_t.MT_TROOP);
            for (int i = 0; i < 8 && imp.health > 0; i++)
            {
                g.Face(imp.x / 65536.0, imp.y / 65536.0);
                g.Rocket();
                for (int k = 0; k < 12; k++)
                    g.Tic(0, i % 2 == 0 ? 40 : -40, 0, 0);
                log("rocket at the imp " + Missiles());
            }
            g.Wait(30);
            g.GoTo(136, 256);
            g.Face(600, 256);
            g.Rocket();
            g.Wait(40);
            log("rocket east (wall) " + Missiles());
            g.GoTo(290, 256, 3);
            g.Face(0, g.Y);
            g.Rocket();
            g.Wait(20);
            log("rocket west from x 290 " + Missiles());
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

""", (g, log) =>
        {
            IsoDoom.Sim.player_t p = g.World.players[0];
            string State() => $"health {p.health} armor {p.armorpoints} weapon {p.readyweapon} pending {p.pendingweapon} psp {p.psprites[0].state} ammo {string.Join(",", p.ammo)}; "
                + string.Join(" ", g.World.Mobjs().Where(m => ((m.flags & IsoDoom.Sim.mobjflag_t.MF_COUNTKILL) != 0 || m.type == IsoDoom.Sim.mobjtype_t.MT_BARREL) && m.health > 0)
                    .Select(m => $"{m.type.ToString()[3..]}({m.x >> 16},{m.y >> 16}) h{m.health}"));
            void Ready(IsoDoom.Sim.weapontype_t wp) => g.WaitUntil(() => p.readyweapon == wp && p.pendingweapon == IsoDoom.Sim.weapontype_t.wp_nochange
                && p.psprites[0].state == IsoDoom.Sim.Info.weaponinfo[(int)wp].readystate, 200);
            double Dist(IsoDoom.Sim.mobj_t m) => System.Math.Sqrt(System.Math.Pow((m.x - g.Mo.x) / 65536.0, 2) + System.Math.Pow((m.y - g.Mo.y) / 65536.0, 2));
            IsoDoom.Sim.mobj_t? Nearest(double min = 0) => g.World.Mobjs()
                .Where(m => (m.flags & IsoDoom.Sim.mobjflag_t.MF_COUNTKILL) != 0 && m.health > 0 && g.World.P_CheckSight(g.Mo, m) && Dist(m) >= min)
                .OrderBy(Dist).FirstOrDefault();
            void KillAll(int max = 6)
            {
                for (int i = 0; i < max && Nearest() is { } m; i++)
                {
                    g.FireAt(m, 250);
                    log("shot " + State());
                }
            }
            void Door(int sector) => g.WaitUntil(() => g.World.sectors[sector].ceilingheight >= 100 * 65536, 200);

            // room A
            Ready(IsoDoom.Sim.weapontype_t.wp_pistol);
            log("pistol up " + State());
            g.Change(IsoDoom.Sim.weapontype_t.wp_fist);
            Ready(IsoDoom.Sim.weapontype_t.wp_fist);
            log("fist up " + State());
            g.GoTo(122, 256, 3);
            g.FireAt(g.Spawned(160, 256)!, 400);
            log("punched " + State());
            g.Change(IsoDoom.Sim.weapontype_t.wp_pistol);
            Ready(IsoDoom.Sim.weapontype_t.wp_pistol);
            while (Nearest() is { } m && Dist(m) < 300)
            {
                g.FireAt(m, 250);
                log("pistol " + State());
            }
            g.GoTo(400, 256);
            Ready(IsoDoom.Sim.weapontype_t.wp_shotgun);
            log("shotgun up " + State());
            KillAll();
            if (g.Spawned(410, 64) is { } barrel)
            {
                g.FireAt(barrel, 100);
                g.Wait(30);
                log("barrel " + State());
            }

            // room B
            g.GoTo(544, 256);
            Door(2);
            log("door 1 open " + State());
            g.GoTo(680, 256);
            Ready(IsoDoom.Sim.weapontype_t.wp_missile);
            log("launcher up " + State());
            if (Nearest(200) is { } far)
            {
                for (int i = 0; i < 120 && p.readyweapon == IsoDoom.Sim.weapontype_t.wp_missile; i++)
                    g.Fire(far.x / 65536.0, far.y / 65536.0);
                g.Tic(0, 0, 0, 0);
            }
            log("rockets " + State());
            g.GoTo(840, 256);
            Ready(IsoDoom.Sim.weapontype_t.wp_chaingun);
            log("chaingun up " + State());
            KillAll();

            // room C
            g.GoTo(1008, 256);
            Door(5);
            log("door 2 open " + State());
            g.GoTo(1100, 256);
            Ready(IsoDoom.Sim.weapontype_t.wp_chainsaw);
            log("chainsaw up " + State());
            for (int i = 0; i < 3 && Nearest() is { } m; i++)
            {
                if (Dist(m) > 60)
                    g.GoTo(m.x / 65536.0 - 44, m.y / 65536.0, 12);
                g.FireAt(m, 200);
                log("sawed " + State());
            }
            g.Change(IsoDoom.Sim.weapontype_t.wp_fist);
            g.Wait(10);
            log("key 1 " + State());
            for (int i = 0; i < 6; i++)
                g.Fire(1400, 256);
            g.Tic(0, 0, 0, IsoDoom.Sim.buttoncode_t.BT_ATTACK | IsoDoom.Sim.buttoncode_t.BT_CHANGE | ((int)IsoDoom.Sim.weapontype_t.wp_chaingun << IsoDoom.Sim.buttoncode_t.BT_WEAPONSHIFT));
            for (int i = 0; i < 12; i++)
                g.Tic(0, 0, 0, IsoDoom.Sim.buttoncode_t.BT_ATTACK);
            log("key 4 while sawing " + State());
            g.Tic(0, 0, 0, 0);
            Ready(IsoDoom.Sim.weapontype_t.wp_chaingun);
            for (int i = 0; i < 600 && p.readyweapon == IsoDoom.Sim.weapontype_t.wp_chaingun; i++)
                g.Fire(1400, 256);
            g.Wait(40);
            log("emptied " + State());
        }, Monsters: true),
        ["testmap-ssg"] = new(() => TestMapWad("ssg"), "E1M1", """
# The super shotgun test map (T10.2a, RouteTestMaps) with monsters, on
# DOOM1.WAD (shareware), so CI checks A_FireShotgun2 against vanilla without
# Doom II's IWAD: the shotgun is walked over (the pickup switches to it: 8
# shells) and fired once at the nearest monster (7 left, the noise wakes the
# room); the super shotgun is walked over (the pickup switches to it: 15),
# fired with fire held (A_CloseShotgun2's A_ReFire fires again), tapped, and
# at the monsters until they die, then into the east wall until one shell is
# left and A_CheckReload's P_CheckAmmo switches to the shotgun (shareware's
# P_CheckAmmo never picks the super shotgun), which fires the last shell
# before P_CheckAmmo switches to the pistol. Written by tools/RouteGen
# (testmap-ssg).
iwad testmap
map ssg
monsters

""", (g, log) =>
        {
            IsoDoom.Sim.player_t p = g.World.players[0];
            string State() => $"health {p.health} weapon {p.readyweapon} pending {p.pendingweapon} psp {p.psprites[0].state} ammo {string.Join(",", p.ammo)}; " + g.Monsters();
            void Ready(IsoDoom.Sim.weapontype_t wp) => g.WaitUntil(() => p.readyweapon == wp && p.pendingweapon == IsoDoom.Sim.weapontype_t.wp_nochange
                && p.psprites[0].state == IsoDoom.Sim.Info.weaponinfo[(int)wp].readystate, 200);
            double Dist(IsoDoom.Sim.mobj_t m) => System.Math.Sqrt(System.Math.Pow((m.x - g.Mo.x) / 65536.0, 2) + System.Math.Pow((m.y - g.Mo.y) / 65536.0, 2));
            IsoDoom.Sim.mobj_t? Nearest() => g.World.Mobjs()
                .Where(m => (m.flags & IsoDoom.Sim.mobjflag_t.MF_COUNTKILL) != 0 && m.health > 0 && g.World.P_CheckSight(g.Mo, m))
                .OrderBy(Dist).FirstOrDefault();
            const IsoDoom.Sim.weapontype_t Ssg = IsoDoom.Sim.weapontype_t.wp_supershotgun;
            int Shells() => p.ammo[(int)IsoDoom.Sim.ammotype_t.am_shell];

            Ready(IsoDoom.Sim.weapontype_t.wp_pistol);
            log("pistol up " + State());
            g.GoTo(160, 256, 4);
            Ready(IsoDoom.Sim.weapontype_t.wp_shotgun);
            log("shotgun up " + State());
            if (Nearest() is { } first)
                g.Fire(first.x / 65536.0, first.y / 65536.0);
            g.Tic(0, 0, 0, 0);
            Ready(IsoDoom.Sim.weapontype_t.wp_shotgun);
            log("shotgun fired " + State());
            g.GoTo(320, 256, 4);
            Ready(Ssg);
            log("super shotgun up " + State());
            // held: two shots (A_CloseShotgun2's A_ReFire)
            if (Nearest() is { } held)
            {
                int before = Shells();
                for (int i = 0; i < 120 && Shells() > before - 4; i++)
                    g.Fire(held.x / 65536.0, held.y / 65536.0);
                g.Tic(0, 0, 0, 0);
            }
            Ready(Ssg);
            log("held " + State());
            if (Nearest() is { } tapped)
                g.Fire(tapped.x / 65536.0, tapped.y / 65536.0);
            g.Tic(0, 0, 0, 0);
            Ready(Ssg);
            log("tapped " + State());
            for (int i = 0; i < 6 && Shells() >= 2 && Nearest() is { } m; i++)
            {
                g.FireAt(m, 200);
                if (p.readyweapon == Ssg)
                    Ready(Ssg);
                log("shot " + State());
            }
            for (int i = 0; i < 400 && p.readyweapon == Ssg; i++)
                g.Fire(1100, 256);
            g.Tic(0, 0, 0, 0);
            Ready(IsoDoom.Sim.weapontype_t.wp_shotgun);
            log("reload switched " + State());
            for (int i = 0; i < 200 && p.readyweapon == IsoDoom.Sim.weapontype_t.wp_shotgun; i++)
                g.Fire(1100, 300);
            g.Tic(0, 0, 0, 0);
            Ready(IsoDoom.Sim.weapontype_t.wp_pistol);
            log("out of shells " + State());
            if (p.health <= 0 || p.readyweapon != IsoDoom.Sim.weapontype_t.wp_pistol)
                throw new InvalidOperationException("the super shotgun route went wrong");
        }, Monsters: true),
        ["testmap-pickups"] = new(() => TestMapWad("pickups"), "E1M1", """
# The pickups test map (T6.8, RouteTestMaps) on skill 1 (ammo doubled,
# damage halved): stand in the damage room N for three hits (the red
# palette), walk east along the row of every shareware pickup (health,
# armor, ammo, the backpack, the weapons, the keys, the blur sphere, the
# computer map, the light amplification visor; the gold palette; a stimpack
# at full health, a green armor over blue and a second computer map stay)
# to the radiation suit, back west into N, unhurt there in the suit (the
# green palette). Written by tools/RouteGen (testmap-pickups).
iwad testmap
map pickups
skill 1

""", (g, log) =>
        {
            IsoDoom.Sim.player_t p = g.World.players[0];
            string Status() => $"health {p.health} armor {p.armorpoints}/{p.armortype} items {p.itemcount} damage {p.damagecount} bonus {p.bonuscount} colormap {p.fixedcolormap}";
            g.Wait(70);
            log("hurt " + Status());
            g.GoTo(1560, 128);
            log("at the suit " + Status());
            g.GoTo(96, 128);
            log("back in N " + Status());
            g.Wait(100);
            log("suited " + Status());
        }, Skill: IsoDoom.Sim.skill_t.sk_baby),
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

""", (g, log) =>
        {
            g.GoTo(540, 128);
            log("in B " + g.Heights(2, 3, 4, 5));
            g.WaitUntil(() => Enumerable.Range(2, 4).All(k => g.World.sectors[k].specialdata == null));
            log("stairs built " + g.Heights(2, 3, 4, 5));
            g.GoTo(190, 128);
            log("in E " + g.Heights(0, 2));
            g.Wait(150);
            log("crusher " + g.Heights(0));
        }),
        ["map01-exit"] = new(Doom2, "MAP01", """
# Doom II MAP01 (T10.1): from the start to the exit switch (line 162,
# 11), the last tic: the doors on the way opened. Written by
# tools/RouteGen (map01-exit).
iwad doom2
map MAP01
exit normal

""", (g, log) =>
        {
            g.GoToDoors(2656, 3550, 8, 50);
            log("at the exit switch");
            g.Exit(2656, 3600);
        }),
        ["map06-exit"] = new(Doom2, "MAP06", """
# Doom II MAP06 (T10.1): started beside the exit switch (line 638, 11),
# pressed on the second tic: the intermission, then Doom II's first text
# screen (C1TEXT) before MAP07. Written by tools/RouteGen (map06-exit).
iwad doom2
map MAP06
exit normal

""", (g, log) =>
        {
            g.Tic(0, 0, 0, 0);
            g.Exit(1240, 224);
        }, (1294, 224, 180)),
        ["tnt-map06-exit"] = new(Tnt, "MAP06", """
# TNT: Evilution MAP06 (T10.7): started in front of the switch (line 937,
# SR lift tag 13) that lowers the exit's sector 171 (384 units up); onto it
# while it waits down, across the walk-over exit (line 935, 52), the last
# tic: the intermission, then TNT's first text screen (T1TEXT) before MAP07.
# Written by tools/RouteGen (tnt-map06-exit).
iwad tnt
map MAP06
exit normal

""", (g, log) =>
        {
            g.Tic(0, 0, 0, 0); // (G_PlayerReborn's usedown)
            g.Use(-784, -216);
            g.WaitUntil(() => g.World.sectors[171].floorheight == g.World.sectors[166].floorheight, 300);
            log("lift down");
            g.WalkExit(-896, -288, 25, 60);
        }, (-784, -248, 90)),
        ["map02-ssg"] = new(Doom2, "MAP02", """
# Doom II MAP02 on skill 3 with monsters (T10.2): started on the super
# shotgun in its secret room (sector 62; the pickup switches to it), which
# shoots the nearest zombieman or shotgun guy in sight until it dies,
# turning towards it every tic (BT_ATTACK, held: A_CloseShotgun2's
# A_ReFire), then the last shells into the south wall until
# A_CheckReload's P_CheckAmmo switches to the pistol. Written by
# tools/RouteGen (map02-ssg).
iwad doom2
map MAP02
skill 3
monsters

""", (g, log) =>
        {
            IsoDoom.Sim.player_t p = g.World.players[0];
            string State() => $"health {p.health} armor {p.armorpoints} weapon {p.readyweapon} pending {p.pendingweapon} psp {p.psprites[0].state} ammo {string.Join(",", p.ammo)}; " + g.Monsters();
            double Dist(IsoDoom.Sim.mobj_t m) => System.Math.Sqrt(System.Math.Pow((m.x - g.Mo.x) / 65536.0, 2) + System.Math.Pow((m.y - g.Mo.y) / 65536.0, 2));
            IsoDoom.Sim.mobj_t? Nearest() => g.World.Mobjs()
                .Where(m => (m.flags & IsoDoom.Sim.mobjflag_t.MF_COUNTKILL) != 0 && m.health > 0 && g.World.P_CheckSight(g.Mo, m))
                .OrderBy(Dist).FirstOrDefault();
            g.Walk(3490, 2900, 20);
            g.WaitUntil(() => p.readyweapon == IsoDoom.Sim.weapontype_t.wp_supershotgun && p.pendingweapon == IsoDoom.Sim.weapontype_t.wp_nochange, 200);
            log("super shotgun " + State());
            for (int i = 0; i < 40 && p.health > 0; i++)
            {
                if (Nearest() is { } m)
                {
                    g.FireAt(m, 250);
                    log("shot " + State());
                    continue;
                }
                break;
            }
            // the last shells at the south wall, until A_CheckReload's P_CheckAmmo switches weapons
            for (int i = 0; i < 200 && p.readyweapon == IsoDoom.Sim.weapontype_t.wp_supershotgun; i++)
                g.Fire(g.X, g.Y - 400);
            g.Tic(0, 0, 0, 0);
            g.Wait(35);
            log("done " + State());
        }, (3520, 2904, 180), Skill: IsoDoom.Sim.skill_t.sk_medium, Monsters: true),
        ["map07-bosses"] = new(Doom2, "MAP07", """
# Doom II MAP07 on skill 1 with monsters (T10.3): the start room's switch
# (line 162, S1 lower tag 1) opens it. All but one mancubus and one
# arachnotron are killed asleep with route events on the first tic; a
# noise wakes those two, each fires its volleys (A_FatRaise, A_FatAttack1-3) or
# plasma (A_BspiAttack, A_BabyMetal's steps) for a while, then is hurt and
# killed with route events (T10.3's Brawler). The last mancubus'
# A_BossDeath lowers the walls tagged 666, the last arachnotron's raises
# the exit's sector 667 to its texture; the player climbs it to the exit
# switch (line 149), the last tic. Written by tools/RouteGen (map07-bosses).
iwad doom2
map MAP07
skill 1
monsters
exit normal

""", (g, log) =>
        {
            var b = new Brawler(g) { Delay = m => m.type == IsoDoom.Sim.mobjtype_t.MT_BABY ? 40 : 75, Hits = m => m.type == IsoDoom.Sim.mobjtype_t.MT_BABY ? 2 : 1 };
            // one mancubus and one arachnotron fight; the others die asleep at once (their A_BossDeath: not the last)
            foreach (IsoDoom.Sim.mobj_t m in g.World.Mobjs().Where(m => (m.type == IsoDoom.Sim.mobjtype_t.MT_FATSO && m.spawnpoint.X != -352)
                || (m.type == IsoDoom.Sim.mobjtype_t.MT_BABY && !(m.spawnpoint.X == 352 && m.spawnpoint.Y == -256))).ToList())
                g.Damage(m.spawnpoint.X, m.spawnpoint.Y, 1000);
            g.Wait(1); // (G_PlayerReborn's usedown)
            log("asleep dead " + g.Monsters());
            g.Use(24, -224);
            log("switch " + g.Heights(19));
            g.Alert();
            g.Wait(1);
            bool Bosses() => g.World.Mobjs().Any(m => (m.type == IsoDoom.Sim.mobjtype_t.MT_FATSO || m.type == IsoDoom.Sim.mobjtype_t.MT_BABY) && m.health > 0);
            for (int t = 0; t < 40 && Bosses() && g.Mo.health > 0; t++)
            {
                b.Until(() => !Bosses() || g.Mo.health <= 0, 100);
                log(g.Monsters());
            }
            log("bosses " + g.Monsters());
            g.WaitUntil(() => g.World.sectors[1].specialdata != null, 200);
            g.WaitIdle(1);
            log("raised " + g.Heights(1, 11, 15, 20, 25, 28) + "; " + b);
            g.GoTo(-608, -215, 4);
            log("at the exit");
            g.Exit(-608, -180);
        }, Skill: IsoDoom.Sim.skill_t.sk_baby, Monsters: true),
        ["doom-e2m8-cyber"] = new(Doom, "E2M8", """
# The Ultimate Doom E2M8 on skill 1 with monsters (T9.2): the Cyberdemon at (-1472, 576) wakes to a
# hurt of 1 and walks (A_Hoof, A_Metal) and fires rockets (A_CyberAttack) at the player, south of it;
# then it is hurt and killed with route events, and its A_BossDeath (E2M8: the last Cyberdemon)
# ends the level, the last tic. Written by tools/RouteGen (doom-e2m8-cyber).
iwad doom
map E2M8
skill 1
monsters
exit normal

""", BossEnd(-1472, 576, Seen("CYBORG", "A_Hoof", "A_Metal", "A_CyberAttack"), exit: true), (-1472, 64, 90), Skill: IsoDoom.Sim.skill_t.sk_baby, Monsters: true),
        ["doom-e3m8-spider"] = new(Doom, "E3M8", """
# The Ultimate Doom E3M8 on skill 1 with monsters (T9.2): the cacodemons killed asleep with route
# events, the Spider Mastermind at (1600, -768) wakes to a hurt of 1, walks (A_Metal) and fires
# (A_SPosAttack, A_SpidRefire) at the player; then it is hurt and killed with route events, and its
# A_BossDeath (E3M8: the last Spider Mastermind) ends the level, the last tic.
# Written by tools/RouteGen (doom-e3m8-spider).
iwad doom
map E3M8
skill 1
monsters
exit normal

""", BossEnd(1600, -768, Seen("SPIDER", "A_Metal", "A_SPosAttack", "A_SpidRefire"), exit: true), (1728, -1088, 112), Skill: IsoDoom.Sim.skill_t.sk_baby, Monsters: true),
        ["doom-e4m6-cyber"] = new(Doom, "E4M6", """
# The Ultimate Doom E4M6 on skill 1 with monsters (T9.2): every other monster killed asleep with
# route events, the Cyberdemon at (1584, -240) wakes to a hurt of 1 and fires rockets
# (A_CyberAttack) at the player, south of its cage; then it is hurt and killed with route events,
# and its A_BossDeath (E4M6: the last Cyberdemon) opens the doors tagged 666 (blazeOpen).
# Written by tools/RouteGen (doom-e4m6-cyber).
iwad doom
map E4M6
skill 1
monsters

""", BossEnd(1584, -240, Seen("CYBORG", "A_CyberAttack"), exit: false), (1584, -720, 90), Skill: IsoDoom.Sim.skill_t.sk_baby, Monsters: true),
        ["doom-e4m8-spider"] = new(Doom, "E4M8", """
# The Ultimate Doom E4M8 on skill 1 with monsters (T9.2): every other monster killed asleep with
# route events, the deaf Spider Mastermind at (-2720, 32) wakes to a hurt of 1 and fires
# (A_SPosAttack, A_SpidRefire) at the player, south of it; then it is hurt and killed with route
# events, and its A_BossDeath (E4M8: the last Spider Mastermind) lowers the floors tagged 666.
# Written by tools/RouteGen (doom-e4m8-spider).
iwad doom
map E4M8
skill 1
monsters

""", BossEnd(-2720, 32, Seen("SPIDER", "A_SPosAttack", "A_SpidRefire"), exit: false), (-2784, -416, 82), Skill: IsoDoom.Sim.skill_t.sk_baby, Monsters: true),
        ["doom-e2m1-skull-floor"] = new(Doom, "E2M1", """
# The Ultimate Doom E2M1 on skill 1 with monsters (T9.2): every monster but the lost soul at
# (-256, -464) killed asleep with route events on the first tic, a hurt of 1 wakes it. The player
# stands south of it in the corridor below (floor 0, the soul's 64) and steps aside while each
# charge flies (A_SkullAttack); a charge that hits the floor bounces before the landing zeroes its
# momz, as The Ultimate Doom's P_ZMovement (Chocolate Doom's correct_lost_soul_bounce, retail and
# Final Doom), unlike Doom II's map05-skull-floor; then it is hurt and killed with route events.
# Written by tools/RouteGen (doom-e2m1-skull-floor).
iwad doom
map E2M1
skill 1
monsters

""", SkullFloor(-256, -464), (-256, -784, 90), Skill: IsoDoom.Sim.skill_t.sk_baby, Monsters: true),
        ["map10-revenant"] = new(Doom2, "MAP10", """
# Doom II MAP10 on skill 1 with monsters (T10.3): every monster but the
# revenant at (1184, -32) killed asleep with route events on the first
# tic; the player, south-west of it in the open (sector 62), makes a
# noise: the revenant closes in and punches (A_SkelWhoosh, A_SkelFist);
# the player runs away south-east and turns: it fires homing missiles
# (A_SkelMissile; A_Tracer on gametic, its puffs and smoke); it is hurt
# and killed with route events. Written by tools/RouteGen (map10-revenant).
iwad doom2
map MAP10
skill 1
monsters

""", (g, log) =>
        {
            var b = new Brawler(g) { Firing = false };
            b.KillAllBut(m => m.spawnpoint.X == 1184 && m.spawnpoint.Y == -32);
            g.Wait(1);
            g.Alert();
            g.Wait(1);
            IsoDoom.Sim.mobj_t rev = g.Spawned(1184, -32)!;
            bool Seen(string action) => b.Attacks.TryGetValue("UNDEAD", out var a) && a.Contains(action);
            // it closes in: the fist
            g.WaitUntil(() => Seen("A_SkelFist") || g.Mo.health <= 0, 600);
            log("punched " + g.Monsters());
            // away: the missiles
            g.GoTo(1700, -700, 16, 50);
            log("away " + g.Monsters());
            g.Face(rev.x / 65536.0, rev.y / 65536.0);
            g.WaitUntil(() => Seen("A_SkelMissile") || g.Mo.health <= 0, 600);
            g.Wait(60);
            log("missile " + g.Monsters());
            g.Damage(1184, -32, 150);
            g.Wait(40);
            log("hurt " + g.Monsters());
            g.Damage(1184, -32, rev.health);
            g.Wait(70);
            log("done " + g.Monsters() + "; " + b);
        }, (1000, -250, 45), Skill: IsoDoom.Sim.skill_t.sk_baby, Monsters: true),
        ["map28-vile"] = new(Doom2, "MAP28", """
# Doom II MAP28 on skill 1 with monsters (T10.3): every monster but the
# arch-vile at (224, -1888) and the imp west of it killed asleep with
# route events on the first tic. The player stands west of them, out of
# their sight, and hurts both by 1 to wake them (the arch-vile is deaf):
# both hunt it; the imp is killed with a route event once it walks
# beside the arch-vile, which raises its corpse when its next step
# touches it (A_VileChase, PIT_VileCheck, S_VILE_HEAL); the imp hunts
# again and is killed again; the arch-vile is hurt and killed with route
# events. Its attacks are map28-vile-attack's. Written by tools/RouteGen
# (map28-vile).
iwad doom2
map MAP28
skill 1
monsters

""", (g, log) =>
        {
            var b = new Brawler(g) { Firing = false };
            b.KillAllBut(m => (m.spawnpoint.X == 224 && m.spawnpoint.Y == -1888) || (m.spawnpoint.X == -64 && m.spawnpoint.Y == -1888));
            g.Wait(1);
            IsoDoom.Sim.mobj_t vile = g.Spawned(224, -1888)!;
            IsoDoom.Sim.mobj_t imp = g.Spawned(-64, -1888)!;
            g.Damage(224, -1888, 1); // (deaf: wakes to the hurt)
            g.Damage(-64, -1888, 1);
            double D(IsoDoom.Sim.mobj_t p, IsoDoom.Sim.mobj_t q) => System.Math.Sqrt(System.Math.Pow((p.x - q.x) / 65536.0, 2) + System.Math.Pow((p.y - q.y) / 65536.0, 2));
            int raised = 0;
            void Tic()
            {
                g.Wait(1);
                if (vile.state == IsoDoom.Sim.statenum_t.S_VILE_HEAL1 && vile.tics == Info.states[(int)IsoDoom.Sim.statenum_t.S_VILE_HEAL1].tics - 1)
                    raised++;
            }
            // both hunt the player, out of sight: the imp dies beside the arch-vile, which raises it
            bool killed = false;
            for (int t = 0; t < 1500 && raised == 0; t++)
            {
                if (!killed && D(imp, vile) < 60)
                {
                    g.Damage(-64, -1888, imp.health);
                    killed = true;
                }
                Tic();
                if (t % 25 == 0)
                    log($"h{g.Mo.health} vile {vile.state} ({vile.x >> 16},{vile.y >> 16}) imp {imp.state} h{imp.health} ({imp.x >> 16},{imp.y >> 16}) raised {raised}");
            }
            if (raised == 0)
                throw new InvalidOperationException("no raise");
            for (int t = 0; t < 60; t++)
                Tic();
            log("raised " + g.Monsters());
            g.Damage(-64, -1888, imp.health);
            for (int t = 0; t < 60; t++)
                Tic();
            log("imp again " + g.Monsters());
            g.Damage(224, -1888, 350);
            g.Wait(40);
            log("hurt " + g.Monsters());
            g.Damage(224, -1888, vile.health);
            g.Wait(80);
            log($"done raised {raised}; " + g.Monsters());
        }, (-500, -1950, 0), Skill: IsoDoom.Sim.skill_t.sk_baby, Monsters: true),
        ["map28-vile-attack"] = new(Doom2, "MAP28", """
# Doom II MAP28 on skill 1 with monsters (T10.3): every monster but the
# arch-vile at (224, -1888) killed asleep with route events on the first
# tic. The player stands south-west of it in its sight and hurts it by 1
# to wake it (deaf): it attacks (A_VileStart, A_VileTarget's fire with
# A_StartFire, A_FireCrackle and A_Fire keeping in front of the player,
# then A_VileAttack's blast: the player thrown up, the fire's
# P_RadiusAttack), then it is hurt and killed with route events.
# Written by tools/RouteGen (map28-vile-attack).
iwad doom2
map MAP28
skill 1
monsters

""", (g, log) =>
        {
            var b = new Brawler(g) { Firing = false };
            b.KillAllBut(m => m.spawnpoint.X == 224 && m.spawnpoint.Y == -1888);
            g.Wait(1);
            IsoDoom.Sim.mobj_t vile = g.Spawned(224, -1888)!;
            g.Damage(224, -1888, 1); // (deaf: wakes to the hurt)
            bool Seen(string action) => b.Attacks.TryGetValue("VILE", out var a) && a.Contains(action);
            g.WaitUntil(() => Seen("A_VileAttack"), 600);
            log("attacked " + g.Monsters());
            g.Wait(20);
            g.Damage(224, -1888, 350);
            g.Wait(40);
            log("hurt " + g.Monsters());
            g.Damage(224, -1888, vile.health);
            g.Wait(80);
            log("done " + g.Monsters() + "; " + b);
        }, (44, -2196, 60), Skill: IsoDoom.Sim.skill_t.sk_baby, Monsters: true),
        ["map09-pain"] = new(Doom2, "MAP09", """
# Doom II MAP09 on skill 1 with monsters (T10.3): a duel with the pain
# elemental at (864, -480) (T10.3's Duel: every other monster killed
# asleep with route events on the first tic, a hurt of 1 wakes it): it
# spits lost souls (A_PainAttack, A_PainShootSkull) that charge the player
# (A_SkullAttack, MF_SKULLFLY) and bite; it is hurt and killed with route
# events, its death spitting three more (A_PainDie). Written by
# tools/RouteGen (map09-pain).
iwad doom2
map MAP09
skill 1
monsters

""", Duel(864, -480, Seen("PAIN", "A_PainAttack"), after: 120), (588, -468, 0), Skill: IsoDoom.Sim.skill_t.sk_baby, Monsters: true),
        ["map11-chaingunner"] = new(Doom2, "MAP11", """
# Doom II MAP11 on skill 1 with monsters (T10.3): a duel with the chaingunner at (-448, -2336)
# (every other monster killed asleep with route events on the first tic,
# a hurt of 1 wakes it): it fires bursts (A_CPosAttack, A_CPosRefire) at
# the player, then is hurt and killed with route events.
# Written by tools/RouteGen (map11-chaingunner).
iwad doom2
map MAP11
skill 1
monsters

""", Duel(-448, 2336, Seen("CHAINGUY", "A_CPosAttack", "A_CPosRefire"), after: 60), (-226, 2366, 180), Skill: IsoDoom.Sim.skill_t.sk_baby, Monsters: true),
        ["map06-knight"] = new(Doom2, "MAP06", """
# Doom II MAP06 on skill 1 with monsters (T10.3): a duel with the Hell knight at (-224, 1120)
# (every other monster killed asleep with route events on the first tic,
# a hurt of 1 wakes it): it throws its balls (A_BruisAttack's
# MT_BRUISERSHOT) at the player below, then is hurt and killed with route
# events.
# Written by tools/RouteGen (map06-knight).
iwad doom2
map MAP06
skill 1
monsters

""", Duel(-224, 1120, Seen("KNIGHT", "A_BruisAttack"), after: 60), (-482, 830, 45), Skill: IsoDoom.Sim.skill_t.sk_baby, Monsters: true),
        ["map31-wolfss"] = new(Doom2, "MAP31", """
# Doom II MAP31 on skill 1 with monsters (T10.3): a duel with the Wolfenstein SS at (-1184, -416)
# (every other monster killed asleep with route events on the first tic,
# a hurt of 1 wakes it): it fires bursts (A_CPosAttack, A_CPosRefire),
# then is hurt and killed with route events; it drops a clip.
# Written by tools/RouteGen (map31-wolfss).
iwad doom2
map MAP31
skill 1
monsters

""", Duel(-1184, -416, Seen("WOLFSS", "A_CPosAttack", "A_CPosRefire"), after: 60), (-1186, -834, 90), Skill: IsoDoom.Sim.skill_t.sk_baby, Monsters: true),
        ["map05-skull"] = new(Doom2, "MAP05", """
# Doom II MAP05 on skill 1 with monsters (T10.3): a duel with the lost soul at (2896, -352)
# (every other monster killed asleep with route events on the first tic,
# a hurt of 1 wakes it): it charges the player (A_SkullAttack,
# MF_SKULLFLY) and bites, then is hurt and killed with route events.
# Written by tools/RouteGen (map05-skull).
iwad doom2
map MAP05
skill 1
monsters

""", Duel(2896, -352, Seen("SKULL", "A_SkullAttack"), after: 60), (2894, -130, 270), Skill: IsoDoom.Sim.skill_t.sk_baby, Monsters: true),
        ["map08-caco"] = new(Doom2, "MAP08", """
# Doom II MAP08 on skill 1 with monsters (T10.3): a duel with the cacodemon at (1696, 3936)
# (every other monster killed asleep with route events on the first tic,
# a hurt of 1 wakes it): it spits balls (A_HeadAttack's MT_HEADSHOT) at
# the player, then is hurt and killed with route events.
# Written by tools/RouteGen (map08-caco).
iwad doom2
map MAP08
skill 1
monsters

""", Duel(1696, 3936, Seen("HEAD", "A_HeadAttack"), after: 60), (2014, 3838, 180), Skill: IsoDoom.Sim.skill_t.sk_baby, Monsters: true),
        ["map20-spider"] = new(Doom2, "MAP20", """
# Doom II MAP20 on skill 1 with monsters (T10.3): a duel with the Spider Mastermind at
# (1952, -2656) (every other monster killed asleep with route events on
# the first tic, a hurt of 1 wakes it): it walks (A_Metal) and fires
# (A_SPosAttack, A_SpidRefire) at the player, then is hurt and killed
# with route events (A_Scream at full volume).
# Written by tools/RouteGen (map20-spider).
iwad doom2
map MAP20
skill 1
monsters

""", Duel(1952, -2656, Seen("SPIDER", "A_Metal", "A_SPosAttack", "A_SpidRefire"), after: 20), (1790, -2818, 90), Skill: IsoDoom.Sim.skill_t.sk_baby, Monsters: true),
        ["map08-cyber"] = new(Doom2, "MAP08", """
# Doom II MAP08 on skill 1 with monsters (T10.3): a duel with the Cyberdemon at (1344, 2016)
# (every other monster killed asleep with route events on the first tic,
# a hurt of 1 wakes it): it walks (A_Hoof, A_Metal) and fires rockets
# (A_CyberAttack) at the player, then is hurt and killed with route
# events.
# Written by tools/RouteGen (map08-cyber).
iwad doom2
map MAP08
skill 1
monsters

""", Duel(1344, 2016, Seen("CYBORG", "A_Hoof", "A_Metal", "A_CyberAttack"), after: 20), (1630, 1822, 150), Skill: IsoDoom.Sim.skill_t.sk_baby, Monsters: true),
        ["map32-keens"] = new(Doom2, "MAP32", """
# Doom II MAP32 on skill 1 with monsters (T10.3): started south of the
# Keens' ring, every other monster (the Cyberdemon, the SS) killed asleep
# with route events on the first tic; each Commander Keen is hurt (its
# pain) and killed with route events; the last one's A_KeenDie opens the
# door ring (sector 16, tag 666) around the exit switch (map32-exit's).
# Written by tools/RouteGen (map32-keens).
iwad doom2
map MAP32
skill 1
monsters

""", (g, log) =>
        {
            var b = new Brawler(g) { Firing = false };
            b.KillAllBut(m => m.type == IsoDoom.Sim.mobjtype_t.MT_KEEN);
            g.Wait(1);
            foreach ((int x, int y) in new[] { (448, 8000), (320, 8128), (576, 8128), (448, 8256) })
            {
                g.Damage(x, y, 50);
                g.Wait(20);
                log($"keen {x},{y} hurt " + g.Heights(16));
                g.Damage(x, y, 50);
                g.Wait(40);
                log($"keen {x},{y} dead " + g.Heights(16));
            }
            g.WaitUntil(() => g.World.sectors[16].specialdata != null, 200);
            g.WaitIdle(16);
            log("door open " + g.Heights(16) + "; " + b);
            g.Wait(30);
        }, (446, 7710, 90), Skill: IsoDoom.Sim.skill_t.sk_baby, Monsters: true),
        ["map05-skull-floor"] = new(Doom2, "MAP05", """
# Doom II MAP05 on skill 1 with monsters (T10.3): every monster but the
# lost soul at (2848, -496) killed asleep with route events on the first
# tic, a hurt of 1 wakes it. The player stands below it (floor 80, the
# soul's 176) and steps aside while each charge flies (A_SkullAttack): a
# charge down from the soul's floor hits that floor at once, where Doom
# II's P_ZMovement does not bounce it (Chocolate Doom's
# correct_lost_soul_bounce: only from The Ultimate Doom on), then it is
# hurt and killed with route events. Written by
# tools/RouteGen (map05-skull-floor).
iwad doom2
map MAP05
skill 1
monsters

""", SkullFloor(2848, -496), (2686, -786, 45), Skill: IsoDoom.Sim.skill_t.sk_baby, Monsters: true),
        ["map07-ssg"] = new(Doom2, "MAP07", """
# Doom II MAP07 without monsters (T10.2), in the start room: the super
# shotgun north of the start walked over (the pickup switches to it: 8
# shells), fired at the west wall with fire held (A_CloseShotgun2's
# A_ReFire fires again), then once tapped, then until A_CheckReload's
# P_CheckAmmo switches to the pistol. Written by tools/RouteGen (map07-ssg).
iwad doom2
map MAP07

""", (g, log) =>
        {
            IsoDoom.Sim.player_t p = g.World.players[0];
            string State() => $"health {p.health} armor {p.armorpoints}/{p.armortype} weapon {p.readyweapon} pending {p.pendingweapon} psp {p.psprites[0].state} ammo {string.Join(",", p.ammo)}";
            void Ready(IsoDoom.Sim.weapontype_t wp) => g.WaitUntil(() => p.readyweapon == wp && p.pendingweapon == IsoDoom.Sim.weapontype_t.wp_nochange
                && p.psprites[0].state == IsoDoom.Sim.Info.weaponinfo[(int)wp].readystate, 200);
            Ready(IsoDoom.Sim.weapontype_t.wp_pistol);
            log("pistol up " + State());
            g.GoTo(64, -112, 4);
            Ready(IsoDoom.Sim.weapontype_t.wp_supershotgun);
            log("super shotgun up " + State());
            // held: two shots (A_CloseShotgun2's A_ReFire)
            for (int i = 0; i < 60; i++)
                g.Fire(g.X - 400, g.Y);
            g.Tic(0, 0, 0, 0);
            Ready(IsoDoom.Sim.weapontype_t.wp_supershotgun);
            log("held " + State());
            g.Fire(g.X - 400, g.Y - 100);
            Ready(IsoDoom.Sim.weapontype_t.wp_supershotgun);
            log("tapped " + State());
            for (int i = 0; i < 200 && p.readyweapon == IsoDoom.Sim.weapontype_t.wp_supershotgun; i++)
                g.Fire(g.X - 400, g.Y + 200);
            g.Tic(0, 0, 0, 0);
            Ready(IsoDoom.Sim.weapontype_t.wp_pistol);
            log("out of shells " + State());
        }),
        ["map07-plasma"] = new(Doom2, "MAP07", """
# Doom II MAP07 on skill 3 with monsters (T9.3): every monster but the
# mancubus spawned at (-864, 96) is killed asleep with route events on the
# first tic; started on the north-west mancubus' ledge (sector 21) beside
# the plasma rifle (-416, 32), walked over it (the pickup switches to it:
# 40 cells), the mancubus woken by the first shot and shot until it dies
# with fire held (A_FirePlasma, its flash's P_Random, A_ReFire), then the
# last cells into the west wall until P_CheckAmmo switches to the pistol.
# Written by tools/RouteGen (map07-plasma).
iwad doom2
map MAP07
skill 3
monsters

""", (g, log) =>
        {
            IsoDoom.Sim.player_t p = g.World.players[0];
            string State() => $"health {p.health} weapon {p.readyweapon} pending {p.pendingweapon} psp {p.psprites[0].state} ammo {string.Join(",", p.ammo)}; " + g.Monsters();
            foreach (IsoDoom.Sim.mobj_t m in g.World.Mobjs().Where(m => (m.flags & IsoDoom.Sim.mobjflag_t.MF_COUNTKILL) != 0
                && !(m.spawnpoint.X == -864 && m.spawnpoint.Y == 96)).ToList())
                g.Damage(m.spawnpoint.X, m.spawnpoint.Y, 1000);
            g.Wait(1);
            g.GoTo(-416, 32, 4);
            g.WaitUntil(() => p.readyweapon == IsoDoom.Sim.weapontype_t.wp_plasma && p.pendingweapon == IsoDoom.Sim.weapontype_t.wp_nochange
                && p.psprites[0].state == IsoDoom.Sim.Info.weaponinfo[(int)IsoDoom.Sim.weapontype_t.wp_plasma].readystate, 200);
            log("plasma rifle up " + State());
            IsoDoom.Sim.mobj_t fatso = g.Spawned(-864, 96) ?? throw new InvalidOperationException("no mancubus");
            g.FireAt(fatso, 400);
            log("mancubus " + State());
            for (int i = 0; i < 400 && p.readyweapon == IsoDoom.Sim.weapontype_t.wp_plasma; i++)
                g.Fire(g.X - 400, g.Y - 40);
            g.Tic(0, 0, 0, 0);
            g.Wait(35);
            log("out of cells " + State());
            if (fatso.health > 0 || p.readyweapon != IsoDoom.Sim.weapontype_t.wp_pistol)
                throw new InvalidOperationException("the plasma rifle did not do its job");
        }, (-432, 80, 270), Skill: IsoDoom.Sim.skill_t.sk_medium, Monsters: true),
        ["map24-bfg"] = new(Doom2, "MAP24", """
# Doom II MAP24 on skill 1 with monsters (T9.3): started on the BFG
# (-1312, -160), walked over (the pickup switches to it: 80 cells on skill
# 1; the demons south of it and the hell knight behind it wake), fired
# with fire held at the nearest monster in sight (A_BFGsound, A_FireBFG),
# the player turned west while each ball flies (A_BFGSpray's tracers fan
# out from the player along the ball's angle, not the player's: the
# second ball's kill the knight), then without cells P_CheckAmmo
# switches to the pistol. Written by tools/RouteGen (map24-bfg).
iwad doom2
map MAP24
skill 1
monsters

""", (g, log) =>
        {
            IsoDoom.Sim.player_t p = g.World.players[0];
            string State() => $"health {p.health} weapon {p.readyweapon} pending {p.pendingweapon} psp {p.psprites[0].state} ammo {string.Join(",", p.ammo)}; " + g.Monsters();
            void Ready(IsoDoom.Sim.weapontype_t wp) => g.WaitUntil(() => p.readyweapon == wp && p.pendingweapon == IsoDoom.Sim.weapontype_t.wp_nochange
                && p.psprites[0].state == IsoDoom.Sim.Info.weaponinfo[(int)wp].readystate, 300);
            int Balls() => g.World.Mobjs().Count(m => m.type == IsoDoom.Sim.mobjtype_t.MT_BFG);
            int Sprays() => g.World.Mobjs().Count(m => m.type == IsoDoom.Sim.mobjtype_t.MT_EXTRABFG);
            g.Wait(1);
            g.GoTo(-1312, -168, 4);
            Ready(IsoDoom.Sim.weapontype_t.wp_bfg);
            log("bfg up " + State());
            double Dist(IsoDoom.Sim.mobj_t m) => System.Math.Sqrt(System.Math.Pow((m.x - g.Mo.x) / 65536.0, 2) + System.Math.Pow((m.y - g.Mo.y) / 65536.0, 2));
            IsoDoom.Sim.mobj_t? Nearest() => g.World.Mobjs()
                .Where(m => (m.flags & IsoDoom.Sim.mobjflag_t.MF_COUNTKILL) != 0 && m.health > 0 && g.World.P_CheckSight(g.Mo, m))
                .OrderBy(Dist).FirstOrDefault();
            int sprays = 0;
            while (p.ammo[(int)IsoDoom.Sim.ammotype_t.am_cell] >= IsoDoom.Sim.World.BFGCELLS && Nearest() is { } target)
            {
                // fire held at the nearest monster until the ball flies; then the player turns away (west) while it flies
                for (int i = 0; i < 200 && Balls() == 0; i++)
                    g.Fire(target.x / 65536.0, target.y / 65536.0);
                log($"ball at {target.type} " + State());
                for (int i = 0; i < 200 && Balls() > 0; i++)
                {
                    g.Face(g.X - 400, g.Y);
                    sprays = System.Math.Max(sprays, Sprays());
                }
                log($"burst, {sprays} tracers hit so far " + State());
                g.Tic(0, 0, 0, 0);
            }
            if (sprays == 0)
                throw new InvalidOperationException("no tracer hit");
            Ready(IsoDoom.Sim.weapontype_t.wp_pistol);
            g.Wait(35);
            log("done " + State());
        }, (-1300, -150, 270), Skill: IsoDoom.Sim.skill_t.sk_baby, Monsters: true),
        ["map31-megasphere"] = new(Doom2, "MAP31", """
# Doom II MAP31 without monsters (T10.2): started east of the megasphere
# (2720, -784), walked over it: 200 health and blue armor (P_GiveArmor 2).
# Written by tools/RouteGen (map31-megasphere).
iwad doom2
map MAP31

""", (g, log) =>
        {
            IsoDoom.Sim.player_t p = g.World.players[0];
            g.GoTo(2720, -784, 4);
            g.Wait(10);
            log($"megasphere health {p.health} armor {p.armorpoints}/{p.armortype}");
            if (p.health != 200)
                throw new InvalidOperationException("no megasphere");
        }, (2840, -784, 180)),
        ["map11-exit"] = new(Doom2, "MAP11", """
# Doom II MAP11 (T10.1): started beside the exit switch (line 832, 11),
# pressed on the second tic: the intermission, then the text screen C2TEXT
# before MAP12. Written by tools/RouteGen (map11-exit).
iwad doom2
map MAP11
exit normal

""", (g, log) =>
        {
            g.Tic(0, 0, 0, 0);
            g.Exit(2420, 2592);
        }, (2362, 2592, 0)),
        ["map15-secret-exit"] = new(Doom2, "MAP15", """
# Doom II MAP15 (T10.1): started in the nukage west of the secret exit's
# platform (sector 197, inside the W1 secret exit lines 1666-1669, 124):
# east onto it, the last tic: the intermission, the text screen C5TEXT,
# then MAP31. Written by tools/RouteGen (map15-secret-exit).
iwad doom2
map MAP15
exit secret

""", (g, log) =>
        {
            g.WalkExit(-416, -4256);
        }, (-480, -4256, 0)),
        ["map20-exit"] = new(Doom2, "MAP20", """
# Doom II MAP20 (T10.1): started west of the exit pit (the W1 exit lines
# 993-996, 52, around x 4008..4120, y -704..-576): east into it, the last
# tic: the intermission, the text screen C3TEXT, then MAP21. Written by
# tools/RouteGen (map20-exit).
iwad doom2
map MAP20
exit normal

""", (g, log) =>
        {
            g.WalkExit(4064, -640);
        }, (3930, -640, 0)),
        ["map31-exit"] = new(Doom2, "MAP31", """
# Doom II MAP31 (T10.1): started beside the exit switch (line 549, 11),
# pressed on the second tic: the intermission, then back to MAP16 (no text
# screen). Written by tools/RouteGen (map31-exit).
iwad doom2
map MAP31
exit normal

""", (g, log) =>
        {
            g.Tic(0, 0, 0, 0);
            g.Exit(-2210, -704);
        }, (-2270, -704, 0)),
        ["map31-secret-exit"] = new(Doom2, "MAP31", """
# Doom II MAP31 (T10.1): started beside the secret exit switch (line 552,
# 51), pressed on the second tic: the intermission, the text screen C6TEXT,
# then MAP32. Written by tools/RouteGen (map31-secret-exit).
iwad doom2
map MAP31
exit secret

""", (g, log) =>
        {
            g.Tic(0, 0, 0, 0);
            g.Exit(-4320, -1216);
        }, (-4258, -1216, 180)),
        ["map32-exit"] = new(Doom2, "MAP32", """
# Doom II MAP32 (T10.1): started north of the exit switch (line 212, 11),
# inside the Keens' shut door ring (sector 16, tag 666; no monsters to open
# it: the start header places the player without a collision check, as
# vanilla's reference does), pressed facing south on the second tic: the
# intermission, then back to MAP16 (no text screen). Written by
# tools/RouteGen (map32-exit).
iwad doom2
map MAP32
exit normal

""", (g, log) =>
        {
            g.Tic(0, 0, 0, 0);
            g.Exit(448, 8100);
        }, (448, 8176, 270)),
        ["map30-icon"] = new(Doom2, "MAP30", """
# Doom II MAP30 on skill 1 with monsters (T10.4): from the start, the
# rocket launcher and rockets, through the teleporter (line 88) into the
# arena; up the lifts (lines 46, 47) to the switch (line 40, raise floor
# 512: the pillar, sector 12, rises to 416). The shooter (A_BrainAwake,
# A_BrainSpit every other time on skill 1) spits cubes (A_SpawnSound,
# A_SpawnFly) whose monsters come out at the brain targets. Down in the
# pit, each side of the pillar lowers it (lines 41-44, SR lift); riding
# it up, the player fires a rocket north (BT_ATTACK) timed to leave at a
# height that flies through the Icon's forehead (sector 9) and bursts on
# the wall behind the brain (A_BrainPain), until it dies (A_BrainScream,
# A_BrainExplode); A_BrainDie ends the level, the last tic. Written by
# tools/RouteGen (map30-icon).
iwad doom2
map MAP30
skill 1
monsters
exit normal

""", (g, log) =>
        {
            IsoDoom.Sim.player_t p = g.World.players[0];
            IsoDoom.Sim.mobj_t brain = g.World.Mobjs().First(m => m.type == IsoDoom.Sim.mobjtype_t.MT_BOSSBRAIN);
            string State() => $"health {p.health} armor {p.armorpoints} weapon {p.readyweapon} rockets {p.ammo[(int)IsoDoom.Sim.ammotype_t.am_misl]} bullets {p.ammo[(int)IsoDoom.Sim.ammotype_t.am_clip]} brain {brain.health} targets {g.World.numbraintargets}; " + g.Monsters();
            double Dist(IsoDoom.Sim.mobj_t m) => System.Math.Sqrt(System.Math.Pow((m.x - g.Mo.x) / 65536.0, 2) + System.Math.Pow((m.y - g.Mo.y) / 65536.0, 2));
            IsoDoom.Sim.mobj_t? Nearest() => g.World.Mobjs()
                .Where(m => (m.flags & IsoDoom.Sim.mobjflag_t.MF_COUNTKILL) != 0 && m.health > 0 && Dist(m) < 1400 && g.World.P_CheckSight(g.Mo, m))
                .OrderBy(Dist).FirstOrDefault();
            void Wield(IsoDoom.Sim.weapontype_t w)
            {
                if (p.readyweapon == w && p.pendingweapon == IsoDoom.Sim.weapontype_t.wp_nochange)
                    return;
                g.Change(w);
                g.WaitUntil(() => p.readyweapon == w && p.pendingweapon == IsoDoom.Sim.weapontype_t.wp_nochange && p.psprites[0].state == (w == IsoDoom.Sim.weapontype_t.wp_chaingun ? IsoDoom.Sim.statenum_t.S_CHAIN : IsoDoom.Sim.statenum_t.S_MISSILE), 100);
            }
            // the spawned monsters in sight, with the chaingun
            void Fight()
            {
                for (int i = 0; i < 40 && Nearest() is { } m && p.health > 0; i++)
                {
                    Wield(IsoDoom.Sim.weapontype_t.wp_chaingun);
                    log($"fighting {m.type} ({m.x >> 16},{m.y >> 16}) " + State());
                    g.FireAt(m, 200);
                }
            }
            void Leg(System.Action a, bool fight = true)
            {
                for (int t = 0; ; t++)
                {
                    if (fight || t > 0)
                        Fight();
                    try
                    {
                        a();
                        return;
                    }
                    catch (InvalidOperationException e) when (t < 6)
                    {
                        log("retrying: " + e.Message);
                    }
                }
            }
            // the chaingun and bullets, the launcher and rockets, then the teleporter
            g.GoTo(2784, -864, 12);
            g.GoTo(2816, -800, 12);
            g.GoTo(2976, -864, 12);
            g.GoTo(2880, -800, 12);
            log("armed " + State());
            g.Teleport(2848, -600);
            log("teleported " + State());
            // the lifts up to the switch
            Leg(() => g.GoTo(1990, 210, 6));
            g.Use(2030, 170);
            g.WaitUntil(() => g.World.sectors[13].floorheight == 0, 300);
            g.Walk(2060, 140, 20, 15);
            g.WaitUntil(() => g.World.sectors[13].floorheight == 128 << 16, 400);
            log("lift 13 up " + State());
            Leg(() => g.GoTo(3584, 30, 8));
            g.Use(3584, 0);
            g.WaitUntil(() => g.World.sectors[14].floorheight == 128 << 16, 300);
            g.Walk(3584, -40, 20, 15);
            g.WaitUntil(() => g.World.sectors[14].floorheight == 256 << 16, 400);
            log("lift 14 up " + State());
            Leg(() => g.GoTo(2880, -400, 8));
            g.Use(2880, -460);
            log("switch " + g.Heights(12) + " " + State());
            // down into the pit, beside the pillar's south side
            Leg(() => g.GoTo(2880, 700, 12));
            for (int t = 0; t < 20 && g.World.sectors[12].floorheight != 416 << 16; t++)
            {
                Fight();
                g.Wait(20);
            }
            g.WaitUntil(() => g.World.sectors[12].floorheight == 416 << 16, 800);
            log("pillar up " + State());
            for (int ride = 0; ride < 12 && brain.health > 0; ride++)
            {
                Leg(() => g.GoTo(2880, 736, 6), fight: false);
                Wield(IsoDoom.Sim.weapontype_t.wp_missile);
                g.Use(2880, 800);
                g.WaitUntil(() => g.World.sectors[12].floorheight == -128 << 16, 400);
                g.GoTo(2880, 832, 6);
                log($"riding {ride} " + State());
                g.WaitUntil(() => g.Mo.z >= 328 << 16, 400);
                g.Tic(0, 0, g.TurnTo(2880, 2000), IsoDoom.Sim.buttoncode_t.BT_ATTACK);
                g.WaitUntil(() => g.World.sectors[12].specialdata == null, 400);
                log($"fired {ride} " + State());
                if (brain.health <= 0)
                    break;
                g.Walk(2880, 600, 12, 25);
            }
            log("brain dead " + State());
            g.WaitExit(400);
        }, Skill: IsoDoom.Sim.skill_t.sk_baby, Monsters: true),
    };

    public static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--probe")
        {
            var probe = new Steer(IwadOf(args[1]), args[1]);
            foreach (string pt in args[2].Split(';'))
                Console.WriteLine(probe.ProbeText(double.Parse(pt.Split(',')[0], CultureInfo.InvariantCulture), double.Parse(pt.Split(',')[1], CultureInfo.InvariantCulture)));
            return 0;
        }
        if (args.Length == 6 && args[0] == "--sight")
        {
            // T10.3: --sight MAP SKILL X Y RADIUS: spots within RADIUS of the monster spawned at X, Y where the player fits and that monster sees it
            int skill = int.Parse(args[2], CultureInfo.InvariantCulture), mx = int.Parse(args[3], CultureInfo.InvariantCulture), my = int.Parse(args[4], CultureInfo.InvariantCulture), r = int.Parse(args[5], CultureInfo.InvariantCulture);
            var sg = new Steer(IwadOf(args[1]), args[1], (IsoDoom.Sim.skill_t)(skill - 1), monsters: true);
            IsoDoom.Sim.mobj_t mon = sg.Spawned(mx, my) ?? throw new InvalidOperationException($"no monster spawned at {mx}, {my}");
            for (int y = my - r; y <= my + r; y += 32)
            {
                for (int x = mx - r; x <= mx + r; x += 32)
                {
                    RouteStart.Place(sg.World, x, y, 0);
                    if (sg.World.P_CheckPosition(sg.Mo, sg.Mo.x, sg.Mo.y) && sg.World.P_CheckSight(mon, sg.Mo))
                        Console.WriteLine($"{x},{y} floor {sg.Mo.floorz >> 16} sector {Array.IndexOf(sg.World.sectors, sg.Mo.subsector.sector)} dist {(int)Math.Sqrt((double)(x - mx) * (x - mx) + (double)(y - my) * (y - my))}");
                }
            }
            // and a map of the spots on stderr, north up: S seen, . fits unseen, # blocked, M the monster
            for (int y = my + r; y >= my - r; y -= 32)
            {
                var row = new System.Text.StringBuilder();
                for (int x = mx - r; x <= mx + r; x += 32)
                {
                    RouteStart.Place(sg.World, x, y, 0);
                    row.Append(Math.Abs(x - mx) < 16 && Math.Abs(y - my) < 16 ? 'M' : !sg.World.P_CheckPosition(sg.Mo, sg.Mo.x, sg.Mo.y) ? '#' : sg.World.P_CheckSight(mon, sg.Mo) ? 'S' : '.');
                }
                Console.Error.WriteLine($"{y,6} {row}");
            }
            return 0;
        }
        if (args.Length is 2 or 3 && args[0] == "--monsters")
            return MapInfo.PrintMonsters(IwadOf(args[1]), args[1], args.Length == 3 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 3);
        if (args.Length is 2 or 3 && args[0] == "--info")
            return MapInfo.Print(IwadOf(args[1]), args[1], args.Length == 3 ? [.. args[2].Split(',').Select(int.Parse)] : null);
        if (args.Length is 2 or 3 && args[0] == "--specials")
            return SpecialRoutes.Find(Doom2, args[1], args.Length == 3 ? args[2] : null);
        if (args.Length is 1 or 2 && SpecialRoutes.IsName(args[0]))
        {
            // T10.6: a special's route (mapNN-lineK, mapNN-sectorK), started beside it
            // (T10.7: tnt-mapNN-lineK, plutonia-mapNN-lineK on Final Doom's IWADs)
            (string specialIwad, string specialMap) = SpecialRoutes.IwadAndMap(args[0]);
            Func<WadArchive> specialWad = IwadNamed(specialIwad);
            (string specialHeader, (int X, int Y, int Angle) specialStart, Action<Steer, Action<string>> specialRun) = SpecialRoutes.Make(specialWad, args[0]);
            _scripts[args[0]] = new Script(specialWad, specialMap, specialHeader, specialRun, specialStart);
        }
        if (args.Length is 1 or 2 && SpecialRoutes.IsExitName(args[0]))
        {
            // T10.6: a map walked from its start to the exit (mapNN-walk-exit)
            (string exitHeader, Action<Steer, Action<string>> exitRun) = SpecialRoutes.MakeExit(args[0]);
            (string exitIwad, string exitMap) = SpecialRoutes.IwadAndMap(args[0]);
            _scripts[args[0]] = new Script(IwadNamed(exitIwad), exitMap, exitHeader, exitRun);
        }
        if (args.Length is < 1 or > 2 || !_scripts.TryGetValue(args[0], out Script? script))
        {
            Console.Error.WriteLine($"usage: IsoDoom.RouteGen NAME [OUT.route]; NAME one of {string.Join(", ", _scripts.Keys)}");
            return 2;
        }
        string name = args[0];
        string path = args.Length > 1 ? args[1] : Path.Combine(RepoRoot(), "tests", "IsoDoom.Tests", "Sim", "Routes", name + ".route");
        // T10.3: ROUTEGEN_START="X,Y,ANGLE" tries the script from another start (to search for one; the route keeps it)
        if (Environment.GetEnvironmentVariable("ROUTEGEN_START") is { Length: > 0 } tryStart)
        {
            int[] v = [.. tryStart.Split(',').Select(t => int.Parse(t, CultureInfo.InvariantCulture))];
            script = script with { Start = (v[0], v[1], v[2]) };
        }
        var g = new Steer(script.Wad(), script.Map, script.Start, script.Skill, script.Monsters, script.Players, script.Deathmatch, script.Timer);
        void Log(string s) => Console.Error.WriteLine($"[{g.World.leveltime}] ({g.X:F0},{g.Y:F0},{g.Z:F0}) {s}");
        script.Run(g, Log);
        Log($"done, {g.Cmds.Count} tics");

        // The planning must not have changed the play: replay the ticcmds in a fresh world.
        var replay = new Steer(script.Wad(), script.Map, script.Start, script.Skill, script.Monsters, script.Players, script.Deathmatch, script.Timer);
        replay.Events.AddRange(g.Events);
        foreach (IsoDoom.Sim.ticcmd_t[] c in g.NetCmds)
            replay.Tic(c); // (with the events; T11.2: every player's commands)
        if (replay.World.Checksum() != g.World.Checksum())
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
