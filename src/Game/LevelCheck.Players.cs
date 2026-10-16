using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IsoDoom.Audio;
using IsoDoom.Render;
using IsoDoom.Sim;

namespace IsoDoom.Game;

// T11.10: the presentation for several players (no renderer needed). On the
// check's map, a game with every player whose co-op start it has
// (`--level-players=4`'s harness; skipped on a map with one start): each
// player's mobj carries its colour translation into its billboard's entry and
// custom data; the players spread over the map, the shared fog is the union
// of every player's walk; for each player made the local one in turn
// (SetLocalPlayer, F12's): the drawn player and its own sprite, the other
// players among the things (actors, in their colours, drawn whatever the fog
// in co-op), the status bar and its face background, the message filter, the
// palette flash, the sound listener (another player's sound positional, its
// own in the centre at full volume), the automap's arrow and the fog's
// viewpoints all follow it, and the world's checksum never moves. With a real
// renderer (SpriteChecks), a player's sprite drawn in each colour, every pixel
// against PlayerTranslation.
public partial class LevelCheck
{
    private void CheckPlayers()
    {
        string map = RenderMapName();
        int playersInGame = _scene.PlayersInGame, local = _scene.LocalPlayer;
        int failures = _failures;
        try
        {
            _scene.PlayersInGame = World.MAXPLAYERS;
            _scene.LocalPlayer = 0;
            _scene.LoadMap(map);
            if (_scene.World is not { } world || _scene.Mesh is not { } m || _scene.StatusBar is not { } st)
            {
                Fail($"players: {map}: no world");
                return;
            }
            int[] players = Enumerable.Range(0, World.MAXPLAYERS).Where(i => world.playeringame[i]).ToArray();
            if (players.Length < 2)
            {
                if (world.netgame)
                    Fail($"players: {map}: one player in the game, but a netgame");
                GD.Print($"Level check: players (T11.10): {map} has one co-op start: the local-player switch is not checked (the colour translation is, with a renderer)");
                return;
            }
            if (!world.netgame || world.consoleplayer != 0)
                Fail($"players: {map}: {players.Length} players in the game: netgame {world.netgame}, consoleplayer {world.consoleplayer}");
            foreach (int i in players)
            {
                if (world.players[i].mo is not { } mo)
                    Fail($"players: {map}: player {i + 1} has no mobj");
                else if (LevelScene.TranslationOf(mo) != i)
                    Fail($"players: {map}: player {i + 1}'s mobj has translation {LevelScene.TranslationOf(mo)}");
            }

            // the players spread over the map: each of the others at a thing far from player 1, in turn
            mobj_t first = world.players[players[0]].mo!;
            List<mobj_t> spots = [.. world.Mobjs().Where(o => o.player is null && LevelScene.IsDrawn(o))
                .OrderByDescending(o => Math.Abs((long)o.x - first.x) + Math.Abs((long)o.y - first.y))];
            for (int n = 1; n < players.Length && spots.Count > 0; n++)
            {
                mobj_t spot = spots[(n - 1) * spots.Count / players.Length];
                world.PlaceMobj(world.players[players[n]].mo!, spot.x, spot.y, null);
            }
            _scene.Tic(new ticcmd_t());
            CheckSharedFog(m, world, map);

            foreach (int k in players)
            {
                ulong before = world.Checksum();
                if (!_scene.SetLocalPlayer(k))
                {
                    Fail($"players: {map}: player {k + 1} could not be made the local one");
                    continue;
                }
                if (world.Checksum() != before)
                    Fail($"players: {map}: making player {k + 1} the local one changed the world's checksum");
                CheckLocalPlayer(world, st, map, k, players);
            }
            if (_scene.SetLocalPlayer(World.MAXPLAYERS) || (players.Length < World.MAXPLAYERS && _scene.SetLocalPlayer(Enumerable.Range(0, World.MAXPLAYERS).First(i => !world.playeringame[i]))))
                Fail($"players: {map}: a player not in the game was made the local one");
            _scene.SetLocalPlayer(players[0]);
            GD.Print($"Level check: players (T11.10): {map} with players {string.Join(", ", players.Select(i => i + 1))} in co-op: each in its colour, "
                + $"the shared fog the union of their walks ({_scene.Fog?.VisibleCount} sectors visible, walk {_scene.FogMilliseconds:F3} ms); each made the local one: "
                + "its own sprite, the others drawn as actors whatever the fog, the status bar, face background, messages, palette flash, sound listener, automap arrow and fog viewpoints followed it, the checksum unchanged");
        }
        finally
        {
            _scene.PlayersInGame = playersInGame;
            _scene.LocalPlayer = local;
            _scene.LoadMap(map);
            if (_failures > failures)
                GD.PrintErr($"Level check: players: {_failures - failures} failure(s)");
        }
    }

    // The scene's fog after the tic: the union of each live player's own walk (fresh fogs on the same level).
    private void CheckSharedFog(LevelMesh m, World world, string map)
    {
        if (_scene.Fog is not { } fog)
        {
            Fail($"players: {map}: no fog");
            return;
        }
        var union = new bool[m.Level.Sectors.Length];
        int alone = 0;
        for (int i = 0; i < World.MAXPLAYERS; i++)
        {
            if (!world.playeringame[i] || world.players[i].mo is not { } mo)
                continue;
            var own = new FogOfWar(m.Level);
            own.See(mo.x, mo.y);
            alone = Math.Max(alone, own.VisibleCount);
            for (int s = 0; s < union.Length; s++)
                union[s] |= own.IsVisible(s);
        }
        int differ = Enumerable.Range(0, union.Length).Count(s => union[s] != fog.IsVisible(s));
        if (differ > 0)
            Fail($"players: {map}: the shared fog differs from the union of the players' walks in {differ} sectors");
        if (fog.VisibleCount <= alone)
            GD.Print($"Level check: players: {map}: the players' walks overlap (union {fog.VisibleCount} sectors, the largest alone {alone})");
        if (LevelScene.FogViewpoints(world, new (int, int)[World.MAXPLAYERS]) != world.playeringame.Count(p => p))
            Fail($"players: {map}: the fog does not look from every live player in co-op");
        // a teammate is drawn whatever the fog (in co-op); in deathmatch as a monster
        var unseen = new FogOfWar(m.Level);
        Array.Clear(union);
        mobj_t other = world.players[Enumerable.Range(0, World.MAXPLAYERS).First(i => i != world.consoleplayer && world.playeringame[i])].mo!;
        if (!LevelScene.FogLets(unseen, FogThings.Sight, other, 0) || LevelScene.FogLets(unseen, FogThings.Sight, other, 1))
            Fail($"players: {map}: an unseen teammate: drawn {LevelScene.FogLets(unseen, FogThings.Sight, other, 0)} in co-op, {LevelScene.FogLets(unseen, FogThings.Sight, other, 1)} in deathmatch (expected true, false)");
    }

    // Player k the local one: everything that follows it.
    private void CheckLocalPlayer(World world, StStuff st, string map, int k, int[] players)
    {
        player_t p = world.players[k];
        mobj_t me = p.mo!;
        string what = $"players: {map}: player {k + 1} local";
        if (world.consoleplayer != k || _scene.PlayerMobj != me)
            Fail($"{what}: consoleplayer {world.consoleplayer}, the scene's player mobj {(_scene.PlayerMobj == me ? "its" : "another's")}");
        if (_scene.Player is { } own && (own.Entry.Translation != k || !own.Entry.Own
            || Math.Abs(own.MapPosition.X - me.x / 65536f) > 0.01f || Math.Abs(own.MapPosition.Y - me.y / 65536f) > 0.01f))
            Fail($"{what}: its own sprite: translation {own.Entry.Translation}, own {own.Entry.Own}, at ({own.MapPosition.X}, {own.MapPosition.Y})");
        // the others among the things: actors, in their colours; not this one
        if (_scene.Things is { } things)
        {
            if (_scene.DrawnMobjs.Contains(me))
                Fail($"{what}: drawn among the things as well as its own sprite");
            foreach (int i in players)
            {
                if (i == k)
                    continue;
                int at = _scene.DrawnMobjs.ToList().IndexOf(world.players[i].mo!);
                if (at < 0)
                {
                    Fail($"{what}: player {i + 1} is not drawn");
                    continue;
                }
                ThingSprites.Entry e = things.Entries[at];
                int flags = (int)Math.Round(things.CustomData(at).G);
                if (e.Translation != i || !e.Actor || (flags >> ThingSprites.FlagTranslationShift & 3) != i || (flags & ThingSprites.FlagOwn) != 0)
                    Fail($"{what}: player {i + 1}'s billboard: translation {e.Translation}, actor {e.Actor}, flags {flags}");
            }
        }
        // the status bar and its face background
        if (st.plyr != p || st.ConsolePlayer != k)
            Fail($"{what}: the status bar is for {(st.plyr == p ? "it" : "another player")}, ConsolePlayer {st.ConsolePlayer}");
        // the message filter and the palette flash: another player's message and damage are not shown
        int other = players.First(i => i != k);
        player_t q = world.players[other];
        PaletteFlashStyle style = _scene.PaletteFlashes;
        _scene.PaletteFlashes = PaletteFlashStyle.Screen;
        const string Theirs = "T11.10: another player's message", Mine = "T11.10: the local player's message";
        (p.damagecount, q.damagecount, q.message) = (0, 20, Theirs);
        _scene.Tic(new ticcmd_t());
        _scene.UpdatePaletteEffects();
        if (_scene.HudMessage == Theirs || _scene.Mesh!.Palette != 0)
            Fail($"{what}: player {other + 1}'s message (\"{_scene.HudMessage}\") or damage (palette {_scene.Mesh!.Palette}) shown");
        (p.damagecount, q.damagecount, p.message) = (20, 0, Mine);
        _scene.Tic(new ticcmd_t());
        _scene.UpdatePaletteEffects();
        if (_scene.HudMessage != Mine || _scene.Mesh!.Palette == 0)
            Fail($"{what}: its own message (\"{_scene.HudMessage}\") or damage (palette {_scene.Mesh!.Palette}) not shown");
        p.damagecount = 0;
        _scene.PaletteFlashes = style;
        _scene.UpdatePaletteEffects();
        // the sound listener: another player's sound 300 units west, at vanilla's volume on the left; its own in the centre
        if (_scene.SoundListener().mo != me)
            Fail($"{what}: the sound listener is not its mobj");
        if (_scene.Sound is { } sound)
        {
            mobj_t them = q.mo!;
            (int x, int y) = (them.x, them.y);
            world.PlaceMobj(them, me.x - 300 * 65536, me.y, null);
            world.S_StartSound(them, sfxenum_t.sfx_pistol);
            world.S_StartSound(me, sfxenum_t.sfx_shotgn);
            _scene.Tic(new ticcmd_t());
            SSound.channel_t? theirs = sound.channels.FirstOrDefault(c => c.origin == them && c.sfx == sfxenum_t.sfx_pistol);
            SSound.channel_t? mine = sound.channels.FirstOrDefault(c => c.origin == me && c.sfx == sfxenum_t.sfx_shotgn);
            int full = sound.snd_SfxVolume;
            if (theirs is null || theirs.vol >= full || theirs.sep >= SSound.NORM_SEP)
                Fail($"{what}: player {other + 1}'s sound 300 units west: {(theirs is null ? "no channel" : $"volume {theirs.vol}, separation {theirs.sep}")}");
            if (mine is null || mine.vol != full || mine.sep != SSound.NORM_SEP)
                Fail($"{what}: its own sound: {(mine is null ? "no channel" : $"volume {mine.vol}, separation {mine.sep}")} (expected {full}, {SSound.NORM_SEP})");
            world.PlaceMobj(them, x, y, null);
            _scene.UpdateSound(10);
            _scene.UpdateSound(0);
        }
        // the automap follows it (started again when it shows)
        if (_scene.Automap is { } am)
        {
            am.AM_Start();
            _scene.SetLocalPlayer(other);
            bool followsOther = am.FollowedPlayer == q;
            _scene.SetLocalPlayer(k);
            if (!followsOther || am.FollowedPlayer != p)
                Fail($"{what}: the automap does not follow the local player across a switch");
            am.AM_Stop();
        }
        // the fog looks from it first
        var points = new (int X, int Y)[World.MAXPLAYERS];
        if (LevelScene.FogViewpoints(world, points) == 0 || points[0] != (me.x, me.y))
            Fail($"{what}: the fog's first viewpoint is not its mobj");
    }
}
