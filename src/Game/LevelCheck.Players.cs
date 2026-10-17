using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
// T11.10a: for each local player, the teammate rings: one under each live
// teammate the fog shows, in its colour, none under the local player, none
// under a dead teammate, none with the option off; single player and
// deathmatch none (CheckDeathmatch). With a real renderer
// (TeammateRingCheck), a ring drawn from the game camera, every pixel against
// its colour blended over the frame without it.
public partial class LevelCheck
{
    private int _ringChecks;

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
            CheckCoopReborn(world, map, players); // T11.3
            // T11.10a: a game alone has no rings
            _scene.PlayersInGame = 1;
            _scene.LoadMap(map);
            _scene.RingsEverywhere = true;
            _scene.UpdateTeammateRings();
            if (_scene.TeammateRings.Rings.Count != 0)
                Fail($"players: {map}: teammate rings (T11.10a): {_scene.TeammateRings.Rings.Count} in a game alone");
            _scene.RingsEverywhere = false;
            GD.Print($"Level check: players (T11.10): {map} with players {string.Join(", ", players.Select(i => i + 1))} in co-op: each in its colour, "
                + $"the shared fog the union of their walks ({_scene.Fog?.VisibleCount} sectors visible, walk {_scene.FogMilliseconds:F3} ms); each made the local one: "
                + "its own sprite, the others drawn as actors whatever the fog, the status bar, face background, messages, palette flash, sound listener, automap arrow and fog viewpoints followed it, the checksum unchanged; "
                + $"the teammate rings (T11.10a) under every live teammate in its colour, none under the local player, a dead one or with the option off, nor in a game alone ({_ringChecks} rings checked)");
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
        CheckTeammateRings(world, map, k, players);
        // the fog looks from it first
        var points = new (int X, int Y)[World.MAXPLAYERS];
        if (LevelScene.FogViewpoints(world, points) == 0 || points[0] != (me.x, me.y))
            Fail($"{what}: the fog's first viewpoint is not its mobj");
    }

    // T11.10a: player k local: a ring under every other live player in the game (the fog shows teammates in co-op), in its colour, at its drawn feet.
    private void CheckTeammateRings(World world, string map, int k, int[] players)
    {
        string what = $"players: {map}: player {k + 1} local: teammate rings (T11.10a)";
        (bool ringing, bool everywhere) = (_scene.TeammateRinging, _scene.RingsEverywhere);
        try
        {
            _scene.TeammateRinging = true;
            _scene.RingsEverywhere = true;
            _scene.UpdateTeammateRings();
            int[] expected = [.. players.Where(i => i != k && world.players[i].playerstate == playerstate_t.PST_LIVE && _scene.FogShows(world.players[i].mo!))];
            int[] got = [.. _scene.TeammateRings.Rings.Select(r => r.Player)];
            if (!got.SequenceEqual(expected))
                Fail($"{what}: under players {string.Join(", ", got.Select(i => i + 1))}, expected {string.Join(", ", expected.Select(i => i + 1))}");
            if (expected.Length != players.Length - 1)
                Fail($"{what}: a teammate the fog hides in co-op or a dead one ({expected.Length} of {players.Length - 1})");
            foreach (Render.TeammateRings.Ring r in _scene.TeammateRings.Rings)
            {
                (Vector3 feet, _) = _scene.Interpolated(world.players[r.Player].mo!);
                if (r.Feet != feet || r.Colour != Render.TeammateRings.ColourOf(Playpal, r.Player))
                    Fail($"{what}: player {r.Player + 1}'s ring at {r.Feet} (feet {feet}) in {r.Colour.ToHtml(false)}");
                _ringChecks++;
            }
            if (_scene.TeammateRings.Rings.Select(r => r.Colour).Distinct().Count() != _scene.TeammateRings.Rings.Count)
                Fail($"{what}: two rings in the same colour");

            // a dead teammate: none under it
            int other = players.First(i => i != k);
            player_t q = world.players[other];
            playerstate_t state = q.playerstate;
            q.playerstate = playerstate_t.PST_DEAD;
            _scene.UpdateTeammateRings();
            q.playerstate = state;
            if (_scene.TeammateRings.Rings.Any(r => r.Player == other))
                Fail($"{what}: a ring under dead player {other + 1}");

            // the option off: none
            _scene.TeammateRinging = false;
            _scene.UpdateTeammateRings();
            if (_scene.TeammateRings.Rings.Count != 0)
                Fail($"{what}: {_scene.TeammateRings.Rings.Count} ring(s) with the option off");
            _scene.TeammateRinging = true;
            _scene.RingsEverywhere = false;
            _scene.UpdateTeammateRings();
            if (!_scene.IsoActive && _scene.TeammateRings.Rings.Count != 0)
                Fail($"{what}: {_scene.TeammateRings.Rings.Count} ring(s) without the game camera");
        }
        finally
        {
            (_scene.TeammateRinging, _scene.RingsEverywhere) = (ringing, everywhere);
            _scene.UpdateTeammateRings();
        }
    }

    /// <summary>
    /// T11.10a, with a real renderer: a teammate ring in each player's colour
    /// at player 1's feet from the game camera's angle (the things, the
    /// player's own sprite and the cutaway off). Every pixel the ring changes
    /// must lie on its annulus (the view ray through the pixel's centre meets
    /// the plane a unit above the floor between its radii) and be its colour
    /// at <see cref="Render.TeammateRings.RingAlpha"/> over the frame without
    /// it; most of the annulus's pixels must change.
    /// </summary>
    private async Task TeammateRingCheck(LevelMesh m)
    {
        string map = m.Level.Name;
        if (_scene.Things is not { } things || _scene.PlayerMobj is not { } me)
            return;
        Basis basis = GameBasis(IsoCamera.DefaultPitch);
        Vector3 toCamera = Cutaway.ToMapAxes(basis.Z).Normalized();
        Vector3 feet = Foot(me);
        bool playerShown = _scene.Player?.Visible ?? false, thingsShown = things.Visible;
        _scene.Player?.Visible = false;
        things.Visible = false;
        m.SetCutaway(new CutawaySettings { Style = CutawayStyle.Off });
        m.SetCutawayCentres(null, null);
        int compared = 0;
        try
        {
            CutView(feet, basis, toCamera);
            _scene.TeammateRings.Clear();
            byte[]? none = await Capture($"{map}: teammate rings (T11.10a), none");
            if (none is null)
                return;
            Vector2I size = ViewSize();
            for (int player = 0; player < World.MAXPLAYERS; player++)
            {
                Color colour = Render.TeammateRings.ColourOf(Playpal, player);
                string what = $"{map}: teammate ring (T11.10a) of player {player + 1} ({colour.ToHtml(false)}) at ({feet.X:F0}, {feet.Y:F0})";
                _scene.TeammateRings.Set([new Render.TeammateRings.Ring(player, feet, colour)]);
                byte[]? ringed = await Capture(what);
                if (ringed is null)
                    return;
                int changed = 0, astray = 0, wrong = 0, inside = 0, insideChanged = 0;
                string first = "";
                for (int y = 0; y < size.Y; y++)
                {
                    for (int x = 0; x < size.X; x++)
                    {
                        var q = new Vector2I(x, y);
                        Vector3 g = PlanePoint(new Vector2(x + 0.5f, y + 0.5f), feet.Z + AimMarker.Lift);
                        float d = new Vector2(g.X - feet.X, g.Y - feet.Y).Length();
                        bool on = d >= AimMarker.RingInner + 0.3f && d <= AimMarker.RingOuter - 0.3f;
                        bool same = Same(q, ringed, none);
                        if (on)
                        {
                            inside++;
                            if (!same)
                                insideChanged++;
                        }
                        if (same)
                            continue;
                        changed++;
                        if (d < AimMarker.RingInner - 0.3f || d > AimMarker.RingOuter + 0.3f)
                        {
                            astray++;
                            continue;
                        }
                        int i = (y * size.X + x) * 4;
                        if (!Blended(ringed, none, i, colour))
                        {
                            if (wrong++ == 0)
                                first = $"({x}, {y}): ({ringed[i]}, {ringed[i + 1]}, {ringed[i + 2]}) over ({none[i]}, {none[i + 1]}, {none[i + 2]})";
                        }
                    }
                }
                compared += size.X * size.Y;
                if (astray > 0 || wrong > 0 || inside == 0 || insideChanged * 2 < inside)
                    Fail($"{what}: {changed} pixels changed, {astray} off its annulus, {wrong} not its colour (first {first}); {insideChanged} of the annulus's {inside} drawn");
            }
            _pixels += compared;
            GD.Print($"Level check: {map}: teammate rings (T11.10a): a ring in each of the four players' colours from the game camera, {compared} pixels compared");
        }
        finally
        {
            _scene.TeammateRings.Clear();
            things.Visible = thingsShown;
            _scene.Player?.Visible = playerShown;
        }

        // The ring's colour at its alpha over the frame without it, blended in linear light (as the renderer does; ±3 for the rounding).
        static bool Blended(byte[] shot, byte[] under, int i, Color c)
        {
            float a = Render.TeammateRings.RingAlpha;
            Color ring = c.SrgbToLinear(), bg = Color.Color8(under[i], under[i + 1], under[i + 2]).SrgbToLinear();
            Color expected = new Color(a * ring.R + (1 - a) * bg.R, a * ring.G + (1 - a) * bg.G, a * ring.B + (1 - a) * bg.B).LinearToSrgb();
            return Math.Abs(shot[i] - expected.R * 255) <= 3 && Math.Abs(shot[i + 1] - expected.G * 255) <= 3 && Math.Abs(shot[i + 2] - expected.B * 255) <= 3;
        }
    }
}
