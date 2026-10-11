using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Map;
using IsoDoom.Render;
using IsoDoom.Sim;

namespace IsoDoom.Game;

/// <summary>
/// The level check's item beam part (T6.13r, SPEC §12). On every map (no
/// renderer needed): the beamed items are exactly the drawn pickups the mode
/// beams, those the fog shows (none in an undiscovered sector), with
/// <c>keys</c> only keys, weapons and power-ups, with <c>off</c> none; after
/// the fog check's door opens, the items seen through it too. With a real
/// renderer, from the game camera's angle, each pickup alone (until the cases
/// below are found): a pickup that shows a quarter of its texels gets no beam
/// pixel; one hidden whole behind the level gets beam pixels in its kind's
/// colour above it and none where the item is drawn once cut open; cut open by
/// the cutaway (its centre at the item) it shows and gets no beam; with the
/// option off nothing changes; an item in an undiscovered sector under the
/// fog's <c>hide</c> gets no beam.
/// </summary>
public partial class LevelCheck
{
    private int _beamMaps, _beamItems, _beamDoorItems, _beamUndiscovered;

    // The beamed mobjs the scene's rule gives now, and the ones expected: drawn pickups the mode beams in a sector the fog shows.
    private (HashSet<mobj_t> Got, HashSet<mobj_t> Expected) BeamSets(FogOfWar? fog)
    {
        _scene.PresentWorld();
        _scene.UpdateItemBeams();
        var got = new HashSet<mobj_t>(_scene.Beams.Beams.Select(b => b.Mobj));
        var expected = new HashSet<mobj_t>();
        foreach (mobj_t mo in _scene.World!.Mobjs())
        {
            if (mo == _scene.PlayerMobj || !LevelScene.IsDrawn(mo) || (mo.flags & mobjflag_t.MF_SPECIAL) == 0 || !ItemKinds.Beamed(ItemKinds.KindOf(mo.type), _scene.ItemBeams))
                continue;
            if (fog is null || fog.IsDiscovered(mo.subsector.sector.Index))
                expected.Add(mo);
        }
        return (got, expected);
    }

    // Runs `body` with the fog hide and the beams under the check's camera, then puts them back.
    private void WithBeamsAndFog(FogStyle style, Action body)
    {
        (FogStyle oldStyle, ItemBeamMode oldMode) = (_scene.FogStyle, _scene.ItemBeams);
        _scene.FogStyle = style;
        _scene.FogEverywhere = true;
        _scene.BeamsEverywhere = true;
        try
        {
            body();
        }
        finally
        {
            _scene.FogStyle = oldStyle;
            _scene.ItemBeams = oldMode;
            _scene.FogEverywhere = false;
            _scene.BeamsEverywhere = false;
            _scene.UpdateFog();
            _scene.PresentWorld();
            _scene.UpdateItemBeams();
        }
    }

    private bool SameBeams(string what, HashSet<mobj_t> got, HashSet<mobj_t> expected)
    {
        if (got.SetEquals(expected))
            return true;
        mobj_t? extra = got.Except(expected).FirstOrDefault(), missing = expected.Except(got).FirstOrDefault();
        Fail($"{what}: {got.Count} item beams, {expected.Count} expected"
            + (extra is null ? "" : $"; {extra.type} in sector {extra.subsector.sector.Index} has one")
            + (missing is null ? "" : $"; {missing.type} in sector {missing.subsector.sector.Index} has none"));
        return false;
    }

    /// <summary>Every map, after <see cref="CheckFog"/>: the beamed items with the fog hide and off, and each mode.</summary>
    private void CheckBeamData(string map)
    {
        if (_scene.World is null || _scene.Fog is not { } fog || _scene.Things is null)
            return;
        WithBeamsAndFog(FogStyle.Hide, () =>
        {
            _scene.UpdateFog();
            foreach (ItemBeamMode mode in new[] { ItemBeamMode.All, ItemBeamMode.Keys, ItemBeamMode.Off })
            {
                _scene.ItemBeams = mode;
                (HashSet<mobj_t> got, HashSet<mobj_t> expected) = BeamSets(fog);
                if (!SameBeams($"{map}: item beams (T6.13r), {ItemKinds.Name(mode)}, fog hide", got, expected))
                    return;
                if (got.Any(mo => !fog.IsDiscovered(mo.subsector.sector.Index)))
                    Fail($"{map}: item beams (T6.13r): an item in an undiscovered sector has a beam");
                if (mode == ItemBeamMode.Keys && got.Any(mo => ItemKinds.KindOf(mo.type) is ItemKind.Health or ItemKind.Armor or ItemKind.Ammo))
                    Fail($"{map}: item beams (T6.13r): keys mode beams health, armor or ammo");
                if (mode == ItemBeamMode.Off && got.Count > 0)
                    Fail($"{map}: item beams (T6.13r): {got.Count} beams with the option off");
                if (mode == ItemBeamMode.All)
                {
                    _beamItems += got.Count;
                    _beamUndiscovered += _scene.World!.Mobjs().Count(mo => (mo.flags & mobjflag_t.MF_SPECIAL) != 0 && !fog.IsDiscovered(mo.subsector.sector.Index));
                }
            }
            _scene.FogStyle = FogStyle.Off;
            _scene.UpdateFog();
            _scene.ItemBeams = ItemBeamMode.All;
            (HashSet<mobj_t> all, HashSet<mobj_t> allExpected) = BeamSets(null);
            SameBeams($"{map}: item beams (T6.13r), all, fog off", all, allExpected);
        });
        _beamMaps++;
    }

    /// <summary>From <see cref="CheckFogDoorOpens"/> with the door open: the items across it (now seen) have beams.</summary>
    private void CheckBeamsThroughDoor(string where, FogOfWar fog, Sector there)
    {
        if (_scene.Things is null)
            return;
        WithBeamsAndFog(FogStyle.Hide, () =>
        {
            _scene.UpdateFog();
            _scene.ItemBeams = ItemBeamMode.All;
            (HashSet<mobj_t> got, HashSet<mobj_t> expected) = BeamSets(fog);
            if (SameBeams($"{where}: item beams (T6.13r) with the door open", got, expected))
                _beamDoorItems += got.Count(mo => mo.subsector.sector == _scene.World!.sectors[there.Index]);
        });
    }

    private void PrintBeams() =>
        GD.Print($"Level check: item beams (T6.13r): {_beamMaps} maps, {_beamItems} pickups beamed at the start with the fog hide "
            + $"({_beamUndiscovered} in undiscovered sectors not), keys and off modes; {_beamDoorItems} beamed across the fog check's opened doors");

    // One pickup's views: none of the things, the item alone, the item and its beam (counts of changed pixels).
    private sealed record BeamView(int ItemPixels, int BeamPixels, List<Vector2I> Item, List<Vector2I> Beam, byte[] ItemCapture, byte[] BeamCapture);

    private async Task<BeamView?> CaptureBeamView(ThingSprites things, int index, ItemBeams.Beam beam, string what)
    {
        things.IsolateSet([]);
        _scene.Beams.Set([]);
        byte[]? none = await Capture($"{what}, no things");
        things.Isolate(index);
        byte[]? item = await Capture($"{what}, the item");
        _scene.Beams.Set([beam]);
        byte[]? beamed = await Capture($"{what}, the item and its beam");
        _scene.Beams.Set([]);
        if (none is null || item is null || beamed is null)
            return null;
        Vector2I size = ViewSize();
        var itemPixels = new List<Vector2I>();
        var beamPixels = new List<Vector2I>();
        for (int y = 0; y < size.Y; y++)
        {
            for (int x = 0; x < size.X; x++)
            {
                var q = new Vector2I(x, y);
                if (!Same(q, item, none))
                    itemPixels.Add(q);
                if (!Same(q, beamed, item))
                    beamPixels.Add(q);
            }
        }
        _pixels += size.X * size.Y;
        return new BeamView(itemPixels.Count, beamPixels.Count, itemPixels, beamPixels, item, beamed);
    }

    /// <summary>
    /// With a real renderer, from the game camera's angle (the cutaway off,
    /// every thing but the one viewed hidden, the player's own sprite too):
    /// the map's pickups in turn until one hidden whole that the cutaway opens
    /// and one in the open are found. A map without them gets pickups of its
    /// own (a blue keycard, removed afterwards): just behind the walls facing
    /// the camera, the tallest first, and at player 1's start.
    /// </summary>
    private async Task BeamCheck(LevelMesh m)
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
        ItemBeamMode oldMode = _scene.ItemBeams;
        _scene.ItemBeams = ItemBeamMode.All;
        _scene.BeamsEverywhere = true;
        var spawned = new List<mobj_t>();
        int scanned = 0, visibleChecked = 0;
        (mobj_t Mobj, BeamView View, ItemBeams.Beam Beam)? hidden = null;
        mobj_t? open = null;
        try
        {
            _scene.PresentWorld();
            List<mobj_t> items = [.. world.Mobjs().Where(mo => (mo.flags & mobjflag_t.MF_SPECIAL) != 0 && DrawnIndex(mo) >= 0)];
            // E1M1's case (DOOM1): first, when there.
            items = [.. items.OrderBy(mo => map == "E1M1" && mo.type == E1M1BeamType && mo.x >> 16 == E1M1BeamX && mo.y >> 16 == E1M1BeamY ? 0 : 1)];
            foreach (mobj_t mo in items.Take(MaxBeamItems))
            {
                if (hidden is not null && open is not null)
                    break;
                if (!await ViewBeamItem(mo))
                    return;
            }
            // The map's own pickups lack a case: spawned ones.
            foreach ((double x, double y) in hidden is null ? BehindWallPoints(m, toCamera).Take(MaxBeamItems) : [])
            {
                if (hidden is not null)
                    break;
                if (!await ViewBeamItem(Spawn(x, y)))
                    return;
            }
            if (open is null && m.Level.PlayerStart(0) is MapThing start && !await ViewBeamItem(Spawn(start.X, start.Y)))
                return;
            if (hidden is not { } h)
            {
                Fail($"{map}: item beams (T6.13r): no pickup hidden whole that the cutaway opens among the {scanned} viewed");
                return;
            }
            if (open is null)
                Fail($"{map}: item beams (T6.13r): no pickup in the open among the {scanned} viewed");

            // Off: nothing changes (the scene's own rule, the item alone).
            Vector3 hf = Foot(h.Mobj);
            CutView(hf, basis, toCamera);
            things.Isolate(DrawnIndex(h.Mobj));
            _scene.ItemBeams = ItemBeamMode.Off;
            _scene.UpdateItemBeams();
            byte[]? off = await Capture($"{map}: item beam of {h.Mobj.type}, option off");
            if (off is not null)
            {
                Vector2I size = ViewSize();
                int changed = 0;
                for (int y = 0; y < size.Y; y++)
                {
                    for (int x = 0; x < size.X; x++)
                    {
                        if (!Same(new Vector2I(x, y), off, h.View.ItemCapture))
                            changed++;
                    }
                }
                _pixels += size.X * size.Y;
                if (_scene.Beams.Beams.Count > 0 || changed > 0)
                    Fail($"{map}: item beams (T6.13r): with the option off {_scene.Beams.Beams.Count} beams, {changed} pixels differ from the item alone");
            }
            _scene.ItemBeams = ItemBeamMode.All;
            string actors = await CheckBeamActors(open, h.Mobj);
            string undiscovered = await CheckBeamUndiscovered(m, things, basis, toCamera);
            GD.Print($"Level check: {map}: item beams (T6.13r): {scanned} pickups viewed alone from the game camera ({spawned.Count} spawned by the check), {visibleChecked} seen without a beam; "
                + $"{h.Mobj.type} at ({hf.X:F0}, {hf.Y:F0}) hidden whole, beamed ({h.View.BeamPixels} pixels), cut open without one; "
                + $"{(open is null ? "none" : $"{open.type} at ({open.x >> 16}, {open.y >> 16})")} in the open; option off: none; {actors}; {undiscovered}");
        }
        finally
        {
            foreach (mobj_t mo in spawned)
                world.P_RemoveMobj(mo);
            _scene.PresentWorld();
            things.Isolate(null);
            things.Visible = false;
            _scene.Player?.Visible = playerShown;
            _scene.Beams.Set([]);
            _scene.ItemBeams = oldMode;
            _scene.BeamsEverywhere = false;
            m.SetCutaway(new CutawaySettings { Style = CutawayStyle.Off });
            m.SetCutawayCentres(null, null);
        }

        // An actor does not hide an item: barons (the widest monsters on the floor in the
        // shareware) side by side on the open item, covering it whole, give it no beam, and on
        // and behind the hidden item they leave its beam.
        async Task<string> CheckBeamActors(mobj_t? seen, mobj_t behind)
        {
            Vector3 flat = new Vector3(toCamera.X, toCamera.Y, 0).Normalized();
            string result = "";
            foreach ((mobj_t? item, bool beamed) in new[] { (seen, false), (behind, true) })
            {
                if (item is null)
                    continue;
                Vector3 foot = Foot(item);
                CutView(foot, basis, toCamera);
                int covered = 0, kept = 0;
                foreach (int distance in _beamActorDistances)
                {
                    // In front of the open item, to cover it; on and behind the hidden one, which what hides it hides too.
                    int d = beamed ? -distance : distance;
                    List<(int X, int Y)> spots = [.. _beamActorLaterals.Select(l => ((int)((foot.X + flat.X * d - flat.Y * l) * 65536), (int)((foot.Y + flat.Y * d + flat.X * l) * 65536)))];
                    // Only where a baron fits (p_map.c P_CheckPosition): one overlapping a wall is drawn in front of it.
                    mobj_t probe = world.P_SpawnMobj(spots[0].X, spots[0].Y, World.ONFLOORZ, mobjtype_t.MT_BRUISER);
                    bool fit = spots.All(p => world.P_CheckPosition(probe, p.X, p.Y));
                    world.P_RemoveMobj(probe);
                    if (!fit)
                        continue;
                    List<mobj_t> barons = [.. spots.Select(p => world.P_SpawnMobj(p.X, p.Y, World.ONFLOORZ, mobjtype_t.MT_BRUISER))];
                    try
                    {
                        _scene.PresentWorld();
                        int i = DrawnIndex(item);
                        List<int> b = [.. barons.Select(DrawnIndex)];
                        if (i < 0 || b.Any(k => k < 0))
                            continue;
                        things.UpdateRotations(_scene.Camera);
                        _scene.UpdateItemBeams();
                        if (_scene.Beams.Beams.FirstOrDefault(x => x.Mobj == item) is not { Slot: >= 0 } beam)
                            continue;
                        List<ItemBeams.Actor> actors = [.. b.Select(k => new ItemBeams.Actor(things.Entries[k].MapPosition, things.Entries[k].Radius))];
                        string what = $"{map}: item beam (T6.13r) of {item.type} at ({foot.X:F0}, {foot.Y:F0}), barons {d} units in front of it";
                        things.IsolateSet(b);
                        _scene.Beams.Set([]);
                        byte[]? alone = await Capture($"{what}, the barons");
                        things.IsolateSet([i, .. b]);
                        byte[]? both = await Capture($"{what}, the barons and the item");
                        _scene.Beams.Set([beam], actors);
                        byte[]? lit = await Capture($"{what}, and its beam");
                        _scene.Beams.Set([]);
                        if (alone is null || both is null || lit is null)
                            return "no capture";
                        Vector2I size = ViewSize();
                        int itemPixels = 0, beamPixels = 0;
                        for (int y = 0; y < size.Y; y++)
                        {
                            for (int x = 0; x < size.X; x++)
                            {
                                var q = new Vector2I(x, y);
                                if (!Same(q, both, alone))
                                    itemPixels++;
                                if (!Same(q, lit, both))
                                    beamPixels++;
                            }
                        }
                        _pixels += size.X * size.Y;
                        if (beamed)
                        {
                            kept++;
                            if (beamPixels == 0)
                                Fail($"{what}: hidden, its beam drew nothing with the barons on it");
                        }
                        if (!beamed && itemPixels == 0)
                        {
                            covered++;
                            if (beamPixels > 0)
                                Fail($"{what}: in the open and covered whole by the barons, its beam drew {beamPixels} pixels");
                        }
                    }
                    finally
                    {
                        foreach (mobj_t baron in barons)
                            world.P_RemoveMobj(baron);
                        _scene.PresentWorld();
                    }
                }
                // The barons cover DOOM1's green armor whole; the synthetic IWAD's sprites are too sparse to.
                result += beamed ? $", the hidden one beamed with barons on it {kept} times" : $"{item.type} in the open covered whole by barons {covered} times without a beam";
            }
            return result.Length == 0 ? "no actor case" : result.TrimStart(',', ' ');
        }

        mobj_t Spawn(double x, double y)
        {
            mobj_t mo = world.P_SpawnMobj((int)(x * 65536), (int)(y * 65536), World.ONFLOORZ, mobjtype_t.MT_MISC4);
            spawned.Add(mo);
            _scene.PresentWorld();
            return mo;
        }

        // One pickup alone: seen (a quarter of its texels show) without a beam, or hidden whole with one that the cutaway opens.
        async Task<bool> ViewBeamItem(mobj_t mo)
        {
            int index = DrawnIndex(mo);
            if (index < 0)
                return true;
            Vector3 foot = Foot(mo);
            CutView(foot, basis, toCamera);
            things.UpdateRotations(_scene.Camera);
            _scene.UpdateItemBeams();
            if (_scene.Beams.Beams.FirstOrDefault(b => b.Mobj == mo) is not { Slot: >= 0 } beam)
                return true;
            string what = $"{map}: item beam (T6.13r) of {mo.type} at ({foot.X:F0}, {foot.Y:F0})";
            if (await CaptureBeamView(things, index, beam, what) is not { } view)
                return false;
            scanned++;
            int texels = atlas.Images[beam.Slot].Opaque.Count(o => o != 0);
            if (view.ItemPixels * 4 >= texels)
            {
                visibleChecked++;
                if (view.BeamPixels > 0)
                    Fail($"{what}: {view.ItemPixels} of its {texels} texels show, but its beam drew {view.BeamPixels} pixels");
                else
                    open ??= mo;
            }
            else if (view.ItemPixels == 0 && view.BeamPixels >= 30 && hidden is null && await CheckBeamCutOpen(m, things, index, beam, foot, view, what))
            {
                hidden = (mo, view, beam);
            }
            return true;
        }
    }

    /// <summary>How far in front of an item (map units) the render check stands three barons side by side (within 48 they stand on it).</summary>
    private static readonly int[] _beamActorDistances = [0, 16, 32, 40, 48];

    /// <summary>The three barons' places across the view (map units).</summary>
    private static readonly int[] _beamActorLaterals = [-16, 0, 16];

    private static Vector3 Foot(mobj_t mo) => new((float)(mo.x / 65536.0), (float)(mo.y / 65536.0), (float)(mo.z / 65536.0));

    /// <summary>
    /// Points 24 units behind the middle of each drawn upper, lower or one-sided
    /// wall section facing the game camera (its normal within 60° of the
    /// direction towards it) that is at least 64 units high, the tallest first,
    /// on a floor with room for a pickup in a sector other than the wall's.
    /// </summary>
    private static IEnumerable<(double X, double Y)> BehindWallPoints(LevelMesh m, Vector3 toCamera)
    {
        var flat = new Vector2(toCamera.X, toCamera.Y).Normalized();
        var points = new List<(double X, double Y, int Height)>();
        foreach (WallSection s in m.Walls.Sections)
        {
            if (!LevelMesh.IsDrawn(s) || s.Kind == WallSectionKind.MaskedMiddle)
                continue;
            (int bottom, int top) = s.Span();
            int height = (top - bottom) >> Fixed.FRACBITS;
            double dx = (s.V2.X - (double)s.V1.X) / 65536, dy = (s.V2.Y - (double)s.V1.Y) / 65536, length = Math.Sqrt(dx * dx + dy * dy);
            if (height < 64 || length < 16)
                continue;
            double nx = dy / length, ny = -dx / length; // into the front sector, towards the camera when it faces it
            if (nx * flat.X + ny * flat.Y < 0.5)
                continue;
            double x = (s.V1.X + (double)s.V2.X) / 131072 - 24 * nx, y = (s.V1.Y + (double)s.V2.Y) / 131072 - 24 * ny;
            // On a floor (not the void behind a one-sided wall), as the floor triangles say.
            int at = m.Floors.SectorAt((int)(x * 65536), (int)(y * 65536));
            if (at < 0 || at == s.FrontSector.Index || m.Level.Sectors[at].CeilingHeight - m.Level.Sectors[at].FloorHeight < 32 * Fixed.FRACUNIT)
                continue;
            points.Add((x, y, height));
        }
        return points.OrderByDescending(p => p.Height).Select(p => (p.X, p.Y));
    }

    private int DrawnIndex(mobj_t mo)
    {
        IReadOnlyList<mobj_t> drawn = _scene.DrawnMobjs;
        for (int i = 0; i < drawn.Count; i++)
        {
            if (drawn[i] == mo)
                return i;
        }
        return -1;
    }

    /// <summary>The most pickups a map's render check views alone.</summary>
    private const int MaxBeamItems = 60;

    /// <summary>
    /// DOOM1 E1M1's case (T6.13r), viewed first there: the health bonus in the
    /// start room's east end, behind the pillar and wall by the nukage pool.
    /// </summary>
    private const mobjtype_t E1M1BeamType = mobjtype_t.MT_MISC2;

    private const int E1M1BeamX = 1312, E1M1BeamY = -3520;

    // A hidden item's beam: in its kind's colour, none where the item shows once cut open, some above that;
    // cut open (the cutaway's centre at the item, doors cut too) the item shows and its beam does not.
    private async Task<bool> CheckBeamCutOpen(LevelMesh m, ThingSprites things, int index, ItemBeams.Beam beam, Vector3 foot, BeamView hidden, string what)
    {
        m.SetCutawayCentres(foot, null);
        m.SetCutaway(new CutawaySettings { Style = CutawayStyle.Cut, Doors = CutawayDoors.Cut }); // a door's walls may be what hides it (the synthetic E1M1's)
        BeamView? cut = await CaptureBeamView(things, index, beam, $"{what}, cut open");
        m.SetCutaway(new CutawaySettings { Style = CutawayStyle.Off });
        m.SetCutawayCentres(null, null);
        if (cut is null || cut.ItemPixels < 20)
            return false; // the cutaway does not open it: try another
        if (cut.BeamPixels > 0)
            Fail($"{what}: cut open by the cutaway ({cut.ItemPixels} item pixels), its beam still drew {cut.BeamPixels} pixels");
        var shown = new HashSet<Vector2I>(cut.Item);
        int inside = hidden.Beam.Count(shown.Contains);
        int top = cut.Item.Min(q => q.Y);
        int above = hidden.Beam.Count(q => q.Y < top);
        if (inside > 0)
            Fail($"{what}: hidden, its beam drew {inside} pixels where the item shows once cut open (the wall must hide its foot)");
        if (above < 20)
            Fail($"{what}: hidden, only {above} beam pixels above the item (at least 20 expected)");
        // Each beam pixel is the kind's colour over what is drawn behind it (the item-alone capture), blended in
        // linear light at some opacity (1 below its fade).
        (float r, float g, float b) = ItemKinds.ColorOf(beam.Kind);
        double[] colour = [Linear(r), Linear(g), Linear(b)];
        int w = ViewSize().X, wrong = 0;
        string firstWrong = "";
        foreach (Vector2I q in hidden.Beam)
        {
            int i = (q.Y * w + q.X) * 4;
            double[] got = [.. Enumerable.Range(0, 3).Select(c => Linear(hidden.BeamCapture[i + c] / 255.0))];
            double[] under = [.. Enumerable.Range(0, 3).Select(c => Linear(hidden.ItemCapture[i + c] / 255.0))];
            int k = Enumerable.Range(0, 3).MaxBy(c => Math.Abs(colour[c] - under[c]));
            double alpha = Math.Clamp((got[k] - under[k]) / (colour[k] - under[k]), 0, 1);
            bool fits = Enumerable.Range(0, 3).All(c => Math.Abs(Srgb(under[c] + alpha * (colour[c] - under[c])) - hidden.BeamCapture[i + c]) <= 3);
            if (!fits && wrong++ == 0)
                firstWrong = $"pixel ({q.X}, {q.Y}): drew {(hidden.BeamCapture[i], hidden.BeamCapture[i + 1], hidden.BeamCapture[i + 2])} over {(hidden.ItemCapture[i], hidden.ItemCapture[i + 1], hidden.ItemCapture[i + 2])}";
        }
        if (wrong > 0)
            Fail($"{what}: {wrong} of its {hidden.BeamPixels} beam pixels are not its kind's colour {(r, g, b)} over what is behind, first at {firstWrong}");
        return true;
    }

    private static double Linear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double Srgb(double c) => 255 * (c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055);


    // Under the fog's hide, an item in an undiscovered sector: no beam on the CPU, and no pixel in its column changes with the beams on.
    private async Task<string> CheckBeamUndiscovered(LevelMesh m, ThingSprites things, Basis basis, Vector3 toCamera)
    {
        if (_scene.Fog is not { } fog)
            return "no fog";
        string map = m.Level.Name;
        mobj_t? item = _scene.World!.Mobjs().FirstOrDefault(mo => (mo.flags & mobjflag_t.MF_SPECIAL) != 0 && LevelScene.IsDrawn(mo) && !fog.IsDiscovered(mo.subsector.sector.Index));
        if (item is null)
            return "no pickup in an undiscovered sector";
        FogStyle style = _scene.FogStyle;
        _scene.FogStyle = FogStyle.Hide;
        _scene.FogEverywhere = true;
        try
        {
            Vector3 foot = new((float)(item.x / 65536.0), (float)(item.y / 65536.0), (float)(item.z / 65536.0));
            CutView(foot, basis, toCamera);
            _scene.UpdateFog();
            _scene.PresentWorld();
            things.Isolate(null);
            things.UpdateRotations(_scene.Camera);
            _scene.UpdateItemBeams();
            string what = $"{map}: item beam (T6.13r) of {item.type} at ({foot.X:F0}, {foot.Y:F0}) in undiscovered sector {item.subsector.sector.Index}, fog hide";
            if (_scene.DrawnMobjs.Contains(item) || _scene.Beams.Beams.Any(b => b.Mobj == item))
            {
                Fail($"{what}: drawn or beamed");
                return "";
            }
            List<ItemBeams.Beam> beams = [.. _scene.Beams.Beams];
            byte[]? on = await Capture($"{what}, beams on");
            _scene.Beams.Set([]);
            byte[]? off = await Capture($"{what}, beams off");
            if (on is null || off is null)
                return "";
            Camera3D camera = _scene.Camera;
            Vector2 at = camera.UnprojectPosition(new Vector3(foot.X, foot.Z, -foot.Y) / LevelMesh.MapUnitsPerMetre);
            if (beams.Any(b => Math.Abs(camera.UnprojectPosition(new Vector3(b.MapPosition.X, b.MapPosition.Z, -b.MapPosition.Y) / LevelMesh.MapUnitsPerMetre).X - at.X) < 8))
                return $"{item.type} in undiscovered sector {item.subsector.sector.Index} not beamed (another beam in its column: pixels not compared)";
            Vector2I size = ViewSize();
            var column = new List<Vector2I>();
            for (int y = Math.Max(0, (int)at.Y - 200); y < Math.Min(size.Y, (int)at.Y + 8); y++)
            {
                for (int x = Math.Max(0, (int)at.X - 4); x < Math.Min(size.X, (int)at.X + 5); x++)
                    column.Add(new Vector2I(x, y));
            }
            (int changed, string first) = ChangedPixels(column, on, off, "the beams off");
            _pixels += column.Count;
            if (changed > 0)
                Fail($"{what}: {changed} pixels of its column changed with the beams on, first at {first}");
            return $"{item.type} in undiscovered sector {item.subsector.sector.Index} not beamed, its column unchanged ({column.Count} pixels)";
        }
        finally
        {
            _scene.FogStyle = style;
            _scene.FogEverywhere = false;
            _scene.UpdateFog();
            _scene.PresentWorld();
        }
    }
}
