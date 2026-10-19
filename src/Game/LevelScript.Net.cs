using System;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Net;
using IsoDoom.Sim;

namespace IsoDoom.Game;

// T11.8: the level script in a netgame. The commands that change the world on
// this machine alone are refused (NetRefused); `netwait N [SECONDS]` waits for
// the game to begin with N players, `nettic T` puts the next queued commands
// (a route's) at game tic T, `until map|gamestate NAME [SECONDS]` waits for the
// game (another machine may drive it), `netcheck [SECONDS]` fails on a desync,
// and `kill [K]` (single machine only) kills a player before the next tic.
public partial class LevelScript
{
    // A nettic was given for the next route (QueueRoute needs one in a netgame).
    private bool _netTic;

    /// <summary>
    /// The commands that change this machine's world outside its commands
    /// (a desync in a netgame), refused there: placing, spawning, powers,
    /// debug moves, new games, loads and saves, demos, the local player, kills.
    /// </summary>
    private static bool NetRefused(string command) => command is "place" or "walkto" or "tour" or "visible" or "spawn" or "wake" or "power"
        or "flash" or "missile" or "plane" or "newgame" or "title" or "save" or "load" or "demo" or "local" or "kill";

    /// <summary>
    /// <c>netwait N [SECONDS]</c>: waits until the netgame's game began with
    /// N players or more and its first map loaded; fails on a refusal (the
    /// hello's, or the host's start check, T11.1a), a disconnection or after
    /// SECONDS (default 60).
    /// </summary>
    private async Task<int> NetWait(int players, int seconds)
    {
        if (_scene.Net is not { } net)
        {
            GD.PrintErr("Level script: netwait: no netgame (-host or -connect)");
            return 1;
        }
        ulong deadline = Time.GetTicksMsec() + (ulong)seconds * 1000;
        while (Time.GetTicksMsec() < deadline && net.Failure is null && net.StartRefusal is null
            && !(_scene.NetGame is { } start && System.Numerics.BitOperations.PopCount(start.PlayerMask) >= players && _scene.World is not null))
            await Frames(1);
        GD.Print($"Level script: netwait: {_scene.NetText()}");
        if (_scene.NetGame is { } s && System.Numerics.BitOperations.PopCount(s.PlayerMask) >= players && _scene.World is not null)
            return 0;
        GD.PrintErr($"Level script: netwait: no game of {players} player(s): {net.StartRefusal ?? net.Failure ?? "timeout"}");
        return 1;
    }

    /// <summary><c>nettic T</c>: the scripted commands queued next go to game tic T (from the game's first, 0) on; fails when this machine already sent T's.</summary>
    private int NetTic(int tic)
    {
        _scene.ScriptedTics = true;
        if (_scene.SetNetScriptTic(tic))
        {
            _netTic = true;
            return 0;
        }
        GD.PrintErr($"Level script: nettic: tic {tic} is past ({_scene.NetText()})");
        return 1;
    }

    /// <summary>
    /// <c>netcheck [SECONDS]</c>: on the host, waits until the server compared
    /// every whole checksum window this machine ran (at most SECONDS, default
    /// 30); everywhere, fails on a desync, a consistency failure or a session
    /// that ended. Prints the session.
    /// </summary>
    private async Task<int> NetCheck(int seconds)
    {
        if (_scene.Net is not { } net || net.Client.Start is not { } start)
        {
            GD.PrintErr("Level script: netcheck: no netgame under way");
            return 1;
        }
        int windows = (net.Client.LastRanTic - start.StartTic + 1) / NetProtocol.WindowTics;
        ulong deadline = Time.GetTicksMsec() + (ulong)seconds * 1000;
        while (net.Server is { } server && server.WindowsCompared < windows && server.Desync is null && net.Failure is null && Time.GetTicksMsec() < deadline)
            await Frames(1);
        NetDesync? desync = net.Server?.Desync ?? net.Client.Desync;
        string compared = net.Server is { } srv ? $"{srv.WindowsCompared} window(s) compared of {windows}" : $"{windows} window(s) run";
        bool ok = desync is null && net.Client.FirstBadTic < 0 && net.Failure is null && (net.Server is null || net.Server.WindowsCompared >= windows);
        GD.Print($"Level script: netcheck: {(ok ? "in sync" : "FAILED")}, {compared}; {_scene.NetText()}");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// <c>until map NAME [SECONDS]</c> or <c>until gamestate STATE [SECONDS]</c>:
    /// waits (frames) until the scene shows map NAME, or the game is in STATE
    /// (<c>level</c>, <c>intermission</c>, <c>finale</c>, <c>demoscreen</c>);
    /// fails after SECONDS (default 120) or when the netgame's session ended.
    /// For a netgame, where another machine may drive the game.
    /// </summary>
    private async Task<int> Until(string what, string name, int seconds)
    {
        if (what is not ("map" or "gamestate"))
            throw new ArgumentException($"until: \"{what}\" (map or gamestate)");
        bool Reached() => what == "map"
            ? string.Equals(_scene.Mesh?.Level.Name, name, StringComparison.OrdinalIgnoreCase) && _scene.GameState == gamestate_t.GS_LEVEL
            : string.Equals(GameFlow.StateName(_scene.GameState), name, StringComparison.OrdinalIgnoreCase);
        ulong deadline = Time.GetTicksMsec() + (ulong)seconds * 1000;
        while (!Reached() && Time.GetTicksMsec() < deadline && _scene.Net?.Failure is null)
            await Frames(1);
        GD.Print($"Level script: until {what} {name}: {(Reached() ? "reached" : "NOT reached")} at tic {_scene.TicsRun}, map {_scene.Mesh?.Level.Name ?? "none"}, game {_scene.Flow.StateText()}");
        return Reached() ? 0 : 1;
    }

    /// <summary>
    /// T11.11: <c>netdesync</c>: before this machine's next tic its world alone
    /// takes a <c>P_Random</c> (vanilla's random index bumped), so the
    /// netgame desyncs on purpose: the test of the desync's stop and dumps.
    /// </summary>
    private int NetDesync()
    {
        if (_scene.NetGame is null)
        {
            GD.PrintErr("Level script: netdesync: no netgame under way");
            return 1;
        }
        _scene.ForceNetDesync();
        return 0;
    }

    /// <summary>
    /// T11.12b: <c>netdelay N</c> (the host's): its server announces input
    /// delay N now, as an adaptive change would (each machine makes its
    /// commands up to its clock + N from then on), so a script can check that
    /// a change in a real game stays in sync and keeps a route's commands on
    /// their tics. Fails on a join or out of the game.
    /// </summary>
    private int NetDelay(int delay)
    {
        if (_scene.NetGame is null || _scene.Net?.Server is not { } server)
        {
            GD.PrintErr("Level script: netdelay: not the host of a netgame under way");
            return 1;
        }
        if (delay is < 1 or > IsoDoom.Net.NetProtocol.MaxDelay)
            throw new ArgumentException($"netdelay: {delay} (1-{IsoDoom.Net.NetProtocol.MaxDelay})");
        bool changed = server.SetDelay(delay, "the level script's netdelay");
        GD.Print($"Level script: netdelay {delay}: {(changed ? "announced" : "unchanged")} at tic {server.CompleteTic}");
        return 0;
    }

    /// <summary>
    /// T11.11: <c>netuntil desync [SECONDS]</c> waits until this machine
    /// stopped for a desync and wrote its dumps; <c>netuntil left N
    /// [SECONDS]</c> until player N left the game (its <c>playeringame</c>
    /// false in this machine's world); <c>netuntil ended [SECONDS]</c> until
    /// the netgame ended under this machine (the host gone) and the title
    /// loop shows. Fails after SECONDS (default 60). Prints the session.
    /// </summary>
    private async Task<int> NetUntil(string[] w)
    {
        string what = w[1];
        int player = what == "left" ? Int(w[2]) - 1 : -1;
        int seconds = w.Length > (what == "left" ? 3 : 2) ? Int(w[what == "left" ? 3 : 2]) : 60;
        Func<bool> reached = what switch
        {
            "desync" => () => _scene.NetStoppedDesync is not null,
            "left" when player is >= 0 and < World.MAXPLAYERS => () => _scene.World is { } world && !world.playeringame[player],
            "ended" => () => _scene.NetEnded is not null && _scene.GameState == gamestate_t.GS_DEMOSCREEN,
            _ => throw new ArgumentException($"netuntil: \"{string.Join(' ', w[1..])}\" (desync, left N or ended)"),
        };
        ulong deadline = Time.GetTicksMsec() + (ulong)seconds * 1000;
        while (!reached() && Time.GetTicksMsec() < deadline)
            await Frames(1);
        bool ok = reached();
        string state = what switch
        {
            "desync" => _scene.NetStoppedDesync is { } d ? $"desync in window {d.Window} (tics {d.FirstTic}-{d.LastTic}), dumps {_scene.NetDesyncDumps ?? "none"}" : "no desync",
            "ended" => _scene.NetEnded is { } why ? $"ended ({why}), game {_scene.Flow.StateText()}" : "not ended",
            _ => $"player {player + 1} {(ok ? "left" : "still in the game")} at tic {_scene.TicsRun}",
        };
        GD.Print($"Level script: netuntil {what}: {(ok ? "reached" : "NOT reached")}: {state}; {_scene.NetText()}");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// <c>kill [K]</c> (T11.8, from T11.3; not in a netgame): before the next
    /// tic player K (1–4; default the local one) takes 10000 damage from nowhere
    /// (<c>P_DamageMobj</c>, as a telefrag's), so a script can see it die and,
    /// with its use, reborn (a netgame's at a start, T11.3; single player's
    /// with the level reloaded).
    /// </summary>
    private void Kill(int? player)
    {
        _scene.BeforeNextTic(world =>
        {
            int p = player ?? world.consoleplayer;
            if (p is >= 0 and < World.MAXPLAYERS && world.playeringame[p] && world.players[p].mo is { health: > 0 } mo)
                world.P_DamageMobj(mo, null, null, 10000);
            else
                GD.PrintErr($"Level script: kill: player {p + 1} is not alive in the game");
        });
    }
}
