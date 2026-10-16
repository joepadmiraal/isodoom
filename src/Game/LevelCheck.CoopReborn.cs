using System.Linq;
using Godot;
using IsoDoom.Map;
using IsoDoom.Sim;

namespace IsoDoom.Game;

// T11.3: a co-op reborn in the level scene (CheckPlayers' game, every player
// whose co-op start the check's map has; `--level-players=N`'s harness). The
// local player is killed and presses use: it reborns at its start without a
// reload (the same level, world and fog, leveltime going on), its body left
// (no longer its player's) in the body queue, a teleport fog at the start;
// the camera snaps to the new mobj, the status bar restarts for it and the
// fog keeps every sector it had discovered. Then another player dies and
// reborns at a start, the local player's camera and status bar left alone.
public partial class LevelCheck
{
    private void CheckCoopReborn(World world, string map, int[] players)
    {
        string what = $"co-op reborn: {map}";
        int local = world.consoleplayer;
        if (_scene.Fog is not { } fog || _scene.StatusBar is not { } st)
        {
            Fail($"{what}: no fog or status bar");
            return;
        }
        // the players off the starts (CheckPlayers spread them), the local one too: at the start it would stand on its own spot
        MapThing start = world.playerstarts[local]!.Value;
        var discovered = Enumerable.Range(0, fog.Level.Sectors.Length).Where(fog.IsDiscovered).ToArray();
        Level level = world.level;
        int reborns = _scene.Reborns, hudStarts = _scene.HudStarts, bodies = world.bodyqueslot, leveltime = world.leveltime;
        player_t p = world.players[local];
        mobj_t me = p.mo!;
        world.P_DamageMobj(me, null, null, 10000);
        for (int i = 0; i < 4; i++)
            _scene.Tic(new ticcmd_t());
        _scene.SnapPending = false; // (as a frame would after snapping)
        if (p.playerstate != playerstate_t.PST_DEAD)
        {
            Fail($"{what}: the killed local player is {p.playerstate}");
            return;
        }
        _scene.Tic(new ticcmd_t { buttons = buttoncode_t.BT_USE }); // PST_REBORN
        _scene.Tic(new ticcmd_t()); // G_DoReborn at the tic's start
        if (_scene.Reborns != reborns || world.level != level || _scene.World != world || _scene.Mesh?.Level != level
            || world.leveltime != leveltime + 6 || world.gameaction != gameaction_t.ga_nothing)
            Fail($"{what}: the level reloaded: reborns {_scene.Reborns - reborns}, same level {world.level == level}, leveltime {world.leveltime - leveltime} tics on");
        if (_scene.PlayerMobj is not { } mo2 || mo2 == me || p.playerstate != playerstate_t.PST_LIVE || p.health != 100
            || (mo2.x, mo2.y) != (start.X << 16, start.Y << 16))
        {
            Fail($"{what}: the local player was not reborn at its start: state {p.playerstate}, health {p.health}");
            return;
        }
        if (me.player is not null || me.function == think_t.REMOVED || world.bodyqueslot != bodies + 1 || world.bodyque[bodies % World.BODYQUESIZE] != me
            || !world.Mobjs().Any(o => o.type == mobjtype_t.MT_TFOG && (o.x, o.y) == FogAt(start)))
            Fail($"{what}: the body: its player {(me.player is null ? "none" : "kept")}, removed {me.function == think_t.REMOVED}, "
                + $"bodies queued {world.bodyqueslot - bodies}, a fog at the start {world.Mobjs().Any(o => o.type == mobjtype_t.MT_TFOG)}");
        if (!_scene.SnapPending)
            Fail($"{what}: the camera does not snap to the reborn player");
        if (st.plyr != p || _scene.HudStarts != hudStarts + 1)
            Fail($"{what}: the status bar was not restarted for the reborn player");
        if (discovered.FirstOrDefault(s => !fog.IsDiscovered(s), -1) is int lost and >= 0)
            Fail($"{what}: the fog forgot sector {lost}");
        _scene.SnapPending = false; // (as a frame would after snapping)

        // another player dies and reborns: at a start (its own unless something stands there: the monsters move), the local view left alone
        int other = players.First(i => i != local);
        player_t q = world.players[other];
        mobj_t them = q.mo!;
        world.P_DamageMobj(them, null, null, 10000);
        var cmds = new ticcmd_t[World.MAXPLAYERS];
        for (int i = 0; i < 3; i++)
            _scene.Tic(new ticcmd_t());
        cmds[other].buttons = buttoncode_t.BT_USE;
        _scene.Flow.G_Ticker(cmds);
        _scene.Tic(new ticcmd_t());
        if (q.mo is not { } theirs2 || theirs2 == them || !world.playerstarts.Any(t => t is { } s && (theirs2.x, theirs2.y) == (s.X << 16, s.Y << 16)) || them.player is not null
            || world.bodyqueslot != bodies + 2 || _scene.Reborns != reborns || _scene.PlayerMobj != mo2 || st.plyr != p || _scene.SnapPending || _scene.HudStarts != hudStarts + 1)
            Fail($"{what}: player {other + 1}'s reborn: {q.playerstate} at ({q.mo?.x >> 16}, {q.mo?.y >> 16}), "
                + $"bodies {world.bodyqueslot - bodies}, reborns {_scene.Reborns - reborns}, the local player kept {_scene.PlayerMobj == mo2}, snap {_scene.SnapPending}, status bar starts {_scene.HudStarts - hudStarts}");
        GD.Print($"Level check: co-op reborn (T11.3): {map}: players {local + 1} and {other + 1} reborn at starts without a reload, "
            + $"their bodies queued, the fog on the floor 20 units ahead, the camera snapped, the status bar restarted and the fog kept ({discovered.Length} sectors discovered)");
    }

    // G_CheckSpot's teleport fog for a start: 20 units ahead of its facing (the DOS executable's angles, World.G_CheckSpotFogOffset).
    private static (int X, int Y) FogAt(MapThing start)
    {
        (int xa, int ya) = World.G_CheckSpotFogOffset(start.Angle);
        return ((start.X << 16) + 20 * xa, (start.Y << 16) + 20 * ya);
    }
}
