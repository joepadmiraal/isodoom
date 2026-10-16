using System.Linq;
using Godot;
using IsoDoom.Sim;

namespace IsoDoom.Game;

// T11.4: deathmatch in play (no renderer needed). On the check's map, when it
// has vanilla's four deathmatch starts (DOOM1's E1M1; the synthetic IWAD has
// none: skipped), a two-player deathmatch game as `--level-players=2
// -deathmatch` starts it: both players at deathmatch starts with every card,
// the fog walking from the local player alone; player 1 frags player 2 (the
// status bar's frags widget shows it), player 2, made the local one, presses
// use and is reborn at a deathmatch start without a reload (the same world
// and mesh, the body left), the status bar started for its new mobj, the
// other player drawn as the fog lets a monster be (T11.10's rules); then a
// game alone (vanilla's -deathmatch without a netgame) whose reborn reloads
// the level at a deathmatch start.
public partial class LevelCheck
{
    private void CheckDeathmatch()
    {
        string map = RenderMapName();
        int playersInGame = _scene.PlayersInGame, local = _scene.LocalPlayer;
        NetOptions modes = _scene.GameModes;
        int failures = _failures;
        try
        {
            _scene.LoadMap(map);
            int starts = _scene.Mesh?.Level.Things.Count(t => t.Type == 11) ?? 0;
            if (starts < 4)
            {
                GD.Print($"Level check: deathmatch (T11.4): {map} has {starts} deathmatch starts, fewer than vanilla's 4: not checked");
                return;
            }
            _scene.PlayersInGame = 2;
            _scene.LocalPlayer = 0;
            _scene.GameModes = NetOptions.Modes(["-deathmatch"]);
            _scene.LoadMap(map);
            if (_scene.World is not { } world || _scene.Mesh is not { } mesh || _scene.StatusBar is not { } st)
            {
                Fail($"deathmatch: {map}: no world");
                return;
            }
            int reborns = _scene.Reborns;
            if (!world.netgame || world.deathmatch != 1 || !world.playeringame[1])
            {
                Fail($"deathmatch: {map}: netgame {world.netgame}, deathmatch {world.deathmatch}, player 2 in game {world.playeringame[1]}");
                return;
            }
            for (int i = 0; i < 2; i++)
            {
                player_t p = world.players[i];
                if (p.mo is not { } mo || !world.deathmatchstarts.Any(s => s.X == mo.x >> 16 && s.Y == mo.y >> 16 && s.Type == i + 1))
                    Fail($"deathmatch: {map}: player {i + 1} not at a deathmatch start");
                if (!p.cards.All(c => c))
                    Fail($"deathmatch: {map}: player {i + 1} has not every card");
            }
            _scene.Tic(new ticcmd_t());
            if (LevelScene.FogViewpoints(world, new (int, int)[World.MAXPLAYERS]) != 1)
                Fail($"deathmatch: {map}: the fog does not walk from the local player alone");

            // player 1 frags player 2
            mobj_t body = world.players[1].mo!;
            world.P_DamageMobj(body, world.players[0].mo, world.players[0].mo, 10000);
            _scene.Tic(new ticcmd_t());
            if (world.players[0].frags[1] != 1 || !st.st_fragson || st.st_fragscount != 1)
                Fail($"deathmatch: {map}: after the frag: frags {world.players[0].frags[1]}, the widget on {st.st_fragson} showing {st.st_fragscount}");

            // player 2, the local one, is reborn without a reload
            _scene.SetLocalPlayer(1);
            for (int i = 0; i < 10; i++)
                _scene.Tic(new ticcmd_t());
            _scene.Tic(new ticcmd_t { buttons = buttoncode_t.BT_USE });
            _scene.Tic(new ticcmd_t());
            mobj_t? reborn = world.players[1].mo;
            if (_scene.Reborns != reborns || _scene.World != world || _scene.Mesh != mesh || world.leveltime < 10 || reborn is null || reborn == body || world.players[1].health != 100)
                Fail($"deathmatch: {map}: player 2's reborn: reloads {_scene.Reborns - reborns}, the same world {_scene.World == world}, mesh {_scene.Mesh == mesh}, leveltime {world.leveltime}, a new mobj {reborn is not null && reborn != body}, health {world.players[1].health}");
            else
            {
                if (!world.deathmatchstarts.Any(s => s.X == reborn.x >> 16 && s.Y == reborn.y >> 16))
                    Fail($"deathmatch: {map}: player 2 reborn at ({reborn.x >> 16}, {reborn.y >> 16}), not a deathmatch start");
                if (body.player is not null || !world.Mobjs().Contains(body))
                    Fail($"deathmatch: {map}: player 2's body did not stay");
                if (_scene.PlayerMobj != reborn || st.plyr != world.players[1] || st.st_fragscount != 0)
                    Fail($"deathmatch: {map}: after the reborn: the scene's player the reborn mobj {_scene.PlayerMobj == reborn}, the status bar's player 2 {st.plyr == world.players[1]}, frags widget {st.st_fragscount} (expected 0)");
            }
            mobj_t other = world.players[0].mo!;
            if (_scene.FogShows(other) != LevelScene.FogLets(_scene.Fog!, _scene.FogThings, other, 1))
                Fail($"deathmatch: {map}: the other player is not drawn as the fog lets a monster be");

            // a game alone: the reborn reloads the level, at a deathmatch start
            _scene.PlayersInGame = 1;
            _scene.LocalPlayer = 0;
            _scene.LoadMap(map);
            world = _scene.World!;
            if (world.netgame || world.deathmatch != 1)
                Fail($"deathmatch: {map}: alone: netgame {world.netgame}, deathmatch {world.deathmatch}");
            world.P_DamageMobj(world.players[0].mo!, null, null, 10000);
            for (int i = 0; i < 10; i++)
                _scene.Tic(new ticcmd_t());
            reborns = _scene.Reborns;
            _scene.Tic(new ticcmd_t { buttons = buttoncode_t.BT_USE });
            if (_scene.Reborns != reborns + 1 || world.leveltime != 0 || world.players[0].mo is not { } mo1 || world.players[0].health != 100
                || !world.deathmatchstarts.Any(s => s.X == mo1.x >> 16 && s.Y == mo1.y >> 16 && s.Type == 1) || !world.players[0].cards.All(c => c))
                Fail($"deathmatch: {map}: alone, the reborn did not reload the level at a deathmatch start (reloads {_scene.Reborns - reborns}, leveltime {world.leveltime})");
            GD.Print($"Level check: deathmatch (T11.4): {map}: two players at deathmatch starts with every card, the fog from the local player alone, "
                + "a frag on the status bar's frags widget, a netgame reborn at a deathmatch start without a reload (the body left, the HUD following the new mobj), "
                + "the other player drawn as a monster under the fog; alone, a reborn reloading the level at a deathmatch start");
        }
        finally
        {
            _scene.PlayersInGame = playersInGame;
            _scene.LocalPlayer = local;
            _scene.GameModes = modes;
            _scene.LoadMap(map);
            if (_failures > failures)
                GD.PrintErr($"Level check: deathmatch: {_failures - failures} failure(s)");
        }
    }
}
