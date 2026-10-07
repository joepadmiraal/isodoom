using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using IsoDoom.Sim;

namespace IsoDoom.Game;

/// <summary>
/// A <c>.route</c> file (T4.8; the format is described at the tests'
/// <c>VanillaRoute</c>): a header (<c>iwad</c>, <c>map</c>, <c>skill</c>,
/// <c>start X Y ANGLE</c>, <c>exit normal|secret</c>, <c>monsters</c>) and one vanilla
/// <c>ticcmd</c> per tic (<c>FORWARD SIDE TURN BUTTONS [xCOUNT]</c>, a demo's
/// relative turn byte), between which (T6.4) <c>damage X Y AMOUNT</c>,
/// <c>alert</c> and (T6.5) <c>rocket</c> lines are <see cref="RouteEvent"/>s
/// run at the start of the next tic. Plain C#: the tests parse their routes with it, and
/// the level script's <c>route FILE</c> (T5.10) plays one in the level scene.
/// </summary>
public sealed class RouteFile
{
    /// <summary>The <c>monsters</c> header (T6.4): the demo spawns monsters (no <c>nomonsters</c>).</summary>
    public bool Monsters { get; private init; }
    /// <summary>The route's events (T6.4), in order, each before the tic of its <see cref="RouteEvent.Tic"/> (0-based).</summary>
    public IReadOnlyList<RouteEvent> Events { get; private init; } = Array.Empty<RouteEvent>();

    /// <summary><c>synthetic</c>, <c>doom1</c> or <c>testmap</c> (not checked here).</summary>
    public string? Iwad { get; private init; }
    /// <summary>The <c>map</c> header as written, or null.</summary>
    public string? Map { get; private init; }
    /// <summary>The <c>skill</c> header, 1-5 (default 3).</summary>
    public int Skill { get; private init; } = 3;
    /// <summary>The <c>start</c> header (map units, degrees), or null for the map's player 1 start (T5.6).</summary>
    public (int X, int Y, int Angle)? Start { get; private init; }
    /// <summary>The <c>exit</c> header (T5.9): 1 for <c>normal</c>, 2 for <c>secret</c>, 0 without one.</summary>
    public int Exit { get; private init; }
    public IReadOnlyList<ticcmd_t> Cmds { get; private init; } = Array.Empty<ticcmd_t>();

    public static RouteFile Parse(string path) => Parse(File.ReadLines(path), path);

    /// <summary>Parses the route's lines; <paramref name="path"/> names it in errors (<see cref="FormatException"/>).</summary>
    public static RouteFile Parse(IEnumerable<string> lines, string path)
    {
        string? iwad = null, map = null;
        int skill = 3, exit = 0;
        (int, int, int)? start = null;
        bool monsters = false;
        var cmds = new List<ticcmd_t>();
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
                case "monsters":
                    if (f.Length != 1)
                        throw new FormatException($"{where}: expected monsters");
                    monsters = true;
                    continue;
                case "damage":
                    if (f.Length != 4 || Int(f[3]) < 1)
                        throw new FormatException($"{where}: expected damage X Y AMOUNT (AMOUNT at least 1)");
                    events.Add(new RouteEvent(cmds.Count, RouteEventKind.Damage, Int(f[1]), Int(f[2]), Int(f[3])));
                    continue;
                case "alert":
                    if (f.Length != 1)
                        throw new FormatException($"{where}: expected alert");
                    events.Add(new RouteEvent(cmds.Count, RouteEventKind.Alert, 0, 0, 0));
                    continue;
                case "rocket":
                    if (f.Length != 1)
                        throw new FormatException($"{where}: expected rocket");
                    events.Add(new RouteEvent(cmds.Count, RouteEventKind.Rocket, 0, 0, 0));
                    continue;
            }
            int count = 1;
            if (f[^1].StartsWith('x'))
            {
                count = Int(f[^1][1..]);
                f = f[..^1];
            }
            if (f.Length != 4)
                throw new FormatException($"{where}: expected FORWARD SIDE TURN BUTTONS [xCOUNT]");
            int[] v = { Int(f[0]), Int(f[1]), Int(f[2]), Int(f[3]) };
            for (int i = 0; i < 3; i++)
            {
                if (v[i] < -128 || v[i] > 127)
                    throw new FormatException($"{where}: out of range");
            }
            if (v[3] < 0 || v[3] > 255)
                throw new FormatException($"{where}: out of range");
            var cmd = new ticcmd_t
            {
                forwardmove = (sbyte)v[0],
                sidemove = (sbyte)v[1],
                angleturn = (short)(v[2] << 8), // a demo's angleturn byte
                buttons = (byte)v[3],
            };
            for (int i = 0; i < count; i++)
                cmds.Add(cmd);
        }
        if (skill < 1 || skill > 5)
            throw new FormatException($"{path}: skill {skill} (1-5)");
        if (events.Count > 0 && events[^1].Tic >= cmds.Count)
            throw new FormatException($"{path}: an event after the last tic");
        return new RouteFile { Iwad = iwad, Map = map, Skill = skill, Start = start, Exit = exit, Cmds = cmds, Monsters = monsters, Events = events };
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
/// </summary>
public readonly record struct RouteEvent(int Tic, RouteEventKind Kind, int X, int Y, int Amount)
{
    /// <summary>Runs the event; throws <see cref="InvalidOperationException"/> when no thing fits a damage event.</summary>
    public void Run(World world)
    {
        mobj_t mo = world.players[world.consoleplayer].mo ?? throw new InvalidOperationException("RouteEvent: no player");
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
    };
}
