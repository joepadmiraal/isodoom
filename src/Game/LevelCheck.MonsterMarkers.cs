using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Map;
using IsoDoom.Render;
using IsoDoom.Sim;

namespace IsoDoom.Game;

// T6.13u: monster markers. On every map (no renderer needed), after the
// sight dim's check: with the markers on (MarkersEverywhere: the check has no
// game camera), the marked monsters are exactly the drawn candidates
// P_CheckSight gives a line of sight to the player (an imp spawned beside it
// among them), each with its own billboard left out of its actors; the sight
// checks leave the world's checksum alone, also with the sight dim off; with
// the option off, or without the game camera, none. With a real renderer
// (MonsterMarkerCheck), from the game camera's angle, an imp alone in turn at the
// places that hide it until one hidden whole and one in the open: the hidden
// one's marker draws red pixels over the vertical through its foot, all above
// its foot; the open one's none. A hidden one is required on DOOM1 E1M1 only:
// an imp is 56 high, and the synthetic E1M1's walls do not hide one whole.
public partial class LevelCheck
{
    private int _monsterMarkerMaps, _monsterMarkerMonsters;

    private void CheckMonsterMarkers(LevelMesh m, string map)
    {
        if (_scene.World is not { } world || _scene.PlayerMobj is not { } me)
            return;
        (bool on, bool everywhere, int rows) = (_scene.MonsterMarking, _scene.MarkersEverywhere, _scene.SightDimRows);
        var spawned = new List<mobj_t>();
        int failures = _failures;
        try
        {
            _scene.MonsterMarking = true;
            _scene.MarkersEverywhere = true;
            _scene.SightDimRows = 0; // the markers take the sight checks on their own
            mobj_t? near = null;
            int start = me.subsector.sector.Index;
            foreach ((int dx, int dy) in new[] { (64, 0), (-64, 0), (0, 64), (0, -64), (48, 48), (-48, -48) })
            {
                int x = me.x + (dx << Fixed.FRACBITS), y = me.y + (dy << Fixed.FRACBITS);
                if (m.Level.R_PointInSubsector(x, y).Sector.Index != start)
                    continue;
                mobj_t imp = world.P_SpawnMobj(x, y, World.ONFLOORZ, mobjtype_t.MT_TROOP);
                spawned.Add(imp);
                if (world.P_CheckSight(imp, me))
                {
                    near = imp;
                    break;
                }
            }
            ulong sum = world.Checksum();
            _scene.SeeSight();
            if (world.Checksum() != sum)
                Fail($"{map}: monster markers (T6.13u): the sight checks changed the world's checksum");
            _scene.PresentWorld();
            _scene.UpdateMonsterMarkers();
            var got = new HashSet<mobj_t>(_scene.Markers.Markers.Select(k => k.Mobj));
            var expected = new HashSet<mobj_t>(world.Mobjs().Where(mo =>
                LevelScene.SightCandidate(mo, me) && LevelScene.IsDrawn(mo) && _scene.FogShows(mo) && world.P_CheckSight(mo, me)));
            if (!got.SetEquals(expected))
                Fail($"{map}: monster markers (T6.13u): marked {string.Join(", ", got.Except(expected).Select(Describe))} but not {string.Join(", ", expected.Except(got).Select(Describe))}");
            if (near is not null && !got.Contains(near))
                Fail($"{map}: monster markers (T6.13u): the imp in sight beside the player is not marked");
            _monsterMarkerMonsters += got.Count;
            if (_scene.Things is { } things)
            {
                foreach (MonsterMarkers.Marker k in _scene.Markers.Markers)
                {
                    int i = DrawnIndex(k.Mobj);
                    if (i < 0 || k.Actor < 0 || k.MapPosition != things.Entries[i].MapPosition || k.Slot != things.ShownFrames[i].Slot)
                        Fail($"{map}: monster markers (T6.13u): {Describe(k.Mobj)}'s marker is not its billboard's (actor {k.Actor})");
                }
            }

            // Off, and without the game camera: none.
            foreach ((bool marking, bool every, string what) in new[] { (false, true, "off"), (true, false, "without the game camera") })
            {
                _scene.MonsterMarking = marking;
                _scene.MarkersEverywhere = every;
                _scene.SeeSight();
                _scene.UpdateMonsterMarkers();
                if (_scene.Markers.Markers.Count > 0)
                    Fail($"{map}: monster markers (T6.13u): {what}, {_scene.Markers.Markers.Count} marker(s)");
            }
        }
        finally
        {
            foreach (mobj_t mo in spawned)
                world.P_RemoveMobj(mo);
            (_scene.MonsterMarking, _scene.MarkersEverywhere, _scene.SightDimRows) = (on, everywhere, rows);
            _scene.SeeSight();
            _scene.PresentWorld();
            _scene.UpdateMonsterMarkers();
        }
        if (_failures == failures)
            _monsterMarkerMaps++;

        static string Describe(mobj_t mo) => $"{mo.type} at ({mo.x >> 16}, {mo.y >> 16})";
    }

    /// <summary>
    /// With a real renderer, from the game camera's angle (the cutaway off,
    /// every thing but the imp viewed hidden, the player's own sprite too): an
    /// imp spawned in turn at E1M1's case (DOOM1), just behind the walls facing
    /// the camera, the tallest first, and at player 1's start, until one hidden
    /// whole and one in the open are found. The hidden one's marker draws red
    /// pixels over the vertical through its foot, all above the foot; the open
    /// one's draws none. Only DOOM1 E1M1 must have a hidden one.
    /// </summary>
    private async Task MonsterMarkerCheck(LevelMesh m)
    {
        string map = m.Level.Name;
        if (_scene.Things is not { } things || _scene.World is not { } world || _scene.SpriteAtlas is not { } atlas)
            return;
        Basis basis = GameBasis(IsoCamera.DefaultPitch);
        Vector3 toCamera = Cutaway.ToMapAxes(basis.Z).Normalized();
        things.Visible = true;
        bool playerShown = _scene.Player?.Visible ?? false;
        _scene.Player?.Visible = false;
        m.SetCutaway(new CutawaySettings { Style = CutawayStyle.Off });
        m.SetCutawayCentres(null, null);
        int viewed = 0, hiddenPixels = 0;
        string? hidden = null, open = null;
        try
        {
            var spots = new List<(double X, double Y)>();
            // DOOM1 E1M1's case (T6.13r's health bonus, where an imp is hidden whole too): first, and then required.
            bool known = map == "E1M1" && world.Mobjs().Any(mo => mo.type == E1M1BeamType && mo.x >> 16 == E1M1BeamX && mo.y >> 16 == E1M1BeamY);
            if (known)
                spots.Add((E1M1BeamX, E1M1BeamY));
            spots.AddRange(BehindWallPoints(m, toCamera).Take(MaxBeamItems));
            if (m.Level.PlayerStart(0) is MapThing start)
                spots.Add((start.X, start.Y));
            foreach ((double x, double y) in spots)
            {
                if (hidden is not null && open is not null)
                    break;
                mobj_t imp = world.P_SpawnMobj((int)(x * 65536), (int)(y * 65536), World.ONFLOORZ, mobjtype_t.MT_TROOP);
                try
                {
                    if (!await ViewMarkedImp(imp))
                        return;
                }
                finally
                {
                    world.P_RemoveMobj(imp);
                    _scene.PresentWorld();
                }
            }
            // An imp is 56 high: a map whose walls facing the camera are too low or too thin to hide one whole (the synthetic E1M1) has no case.
            if (hidden is null && known)
                Fail($"{map}: monster markers (T6.13u): no imp hidden whole among the {viewed} viewed");
            if (open is null)
                Fail($"{map}: monster markers (T6.13u): no imp in the open among the {viewed} viewed");
            GD.Print($"Level check: {map}: monster markers (T6.13u): {viewed} imps viewed alone from the game camera; "
                + $"{hidden ?? "none"} hidden whole, marked ({hiddenPixels} pixels above it); {open ?? "none"} in the open, unmarked");
        }
        finally
        {
            things.Isolate(null);
            things.Visible = false;
            _scene.Player?.Visible = playerShown;
            _scene.Markers.Clear();
            m.SetCutaway(new CutawaySettings { Style = CutawayStyle.Off });
            m.SetCutawayCentres(null, null);
        }

        // One imp alone: seen (a quarter of its texels show) without a marker, or hidden whole with one above it.
        async Task<bool> ViewMarkedImp(mobj_t imp)
        {
            _scene.PresentWorld();
            int index = DrawnIndex(imp);
            if (index < 0)
                return true;
            Vector3 foot = Foot(imp);
            CutView(foot, basis, toCamera);
            things.UpdateRotations(_scene.Camera);
            ThingSprites.Entry entry = things.Entries[index];
            ThingSprites.Shown shown = things.ShownFrames[index];
            if (shown.Slot < 0)
                return true;
            var marker = new MonsterMarkers.Marker(imp, entry.MapPosition, shown.Slot, shown.Flip, entry.Radius, entry.Sector, 0);
            ItemBeams.Actor[] actors = [new(entry.MapPosition, entry.Radius)];
            string what = $"{map}: monster marker (T6.13u) of an imp at ({foot.X:F0}, {foot.Y:F0})";
            things.IsolateSet([]);
            _scene.Markers.Set([], []);
            byte[]? none = await Capture($"{what}, no things");
            things.Isolate(index);
            byte[]? alone = await Capture($"{what}, the imp");
            _scene.Markers.Set([marker], actors);
            byte[]? marked = await Capture($"{what}, the imp and its marker");
            _scene.Markers.Set([], []);
            if (none is null || alone is null || marked is null)
                return false;
            viewed++;
            Vector2I size = ViewSize();
            // The foot on screen: CutView centres the point Cutaway.Anchor above it, 1 map unit per pixel.
            float footY = size.Y / 2f + Cutaway.Anchor * Mathf.Cos(Mathf.DegToRad(IsoCamera.DefaultPitch));
            int impPixels = 0, markerPixels = 0, red = 0, astray = 0;
            for (int y = 0; y < size.Y; y++)
            {
                for (int x = 0; x < size.X; x++)
                {
                    var q = new Vector2I(x, y);
                    if (!Same(q, alone, none))
                        impPixels++;
                    if (Same(q, marked, alone))
                        continue;
                    markerPixels++;
                    int i = (y * size.X + x) * 4;
                    if (marked[i] >= 160 && marked[i + 1] <= 90 && marked[i + 2] <= 90)
                        red++;
                    if (Math.Abs(x + 0.5f - size.X / 2f) > 16 || y >= footY)
                        astray++;
                }
            }
            _pixels += size.X * size.Y;
            int texels = atlas.Images[shown.Slot].Opaque.Count(o => o != 0);
            if (impPixels * 4 >= texels)
            {
                if (markerPixels > 0)
                    Fail($"{what}: {impPixels} of its {texels} texels show, but its marker drew {markerPixels} pixels");
                open ??= $"the imp at ({foot.X:F0}, {foot.Y:F0})";
            }
            else if (impPixels == 0 && hidden is null)
            {
                if (red < 20 || astray > 0)
                    Fail($"{what}: hidden whole, its marker drew {markerPixels} pixels, {red} red, {astray} off the vertical through its foot or below it");
                hidden = $"the imp at ({foot.X:F0}, {foot.Y:F0})";
                hiddenPixels = markerPixels;
            }
            return true;
        }
    }

    private void PrintMonsterMarkers()
    {
        GD.Print($"Level check: monster markers (T6.13u): {_monsterMarkerMaps} maps: {_monsterMarkerMonsters} monsters in sight marked, as P_CheckSight (monster to player) gives them; none with it off or without the game camera, the world's checksum unchanged");
        if (_monsterMarkerMaps == 0)
            Fail("monster markers (T6.13u): no map checked");
    }
}
