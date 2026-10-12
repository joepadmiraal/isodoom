using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Map;
using IsoDoom.Render;
using IsoDoom.Sim;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

// T6.13t: monsters out of sight drawn darker. On every map (no renderer
// needed), after the reborn's reload: with the dim on (SightEverywhere: the
// check has no game camera) each drawn candidate's target is
// P_CheckSight(monster, player) ? 0 : the amount and, new, its dim is its
// target; nothing else is in the table (barrels, items, corpses, the
// player); the drawn billboards' custom data carry exactly those rows (the
// player's none); the sight checks leave the world's checksum alone. An imp
// spawned in sight is undimmed and one out of sight dimmed; the player placed
// beside the second sees it undimmed the same tic, while the first, out of
// sight now, stays lit for the grace and then steps darker. With the option
// off, or without the game camera, nothing is dimmed. With a real renderer
// (SpriteChecks): a thing dimmed, lit and full bright, drawn through the
// shifted colormap row (CompareSprite), and a fixed colormap still winning.
public partial class LevelCheck
{
    private int _sightMaps, _sightChecked, _sightDimmed, _sightPlaced, _sightFaded, _sightDrawn;

    private void CheckSightDim(LevelMesh m, string map)
    {
        if (_scene.World is not { } world || _scene.PlayerMobj is not { } me)
            return;
        int rows = _scene.SightDimRows;
        bool everywhere = _scene.SightEverywhere;
        (int sx, int sy, uint angle) = (me.x, me.y, me.angle);
        var spawned = new List<mobj_t>();
        int failures = _failures;
        try
        {
            _scene.SightDimRows = SightDim.DefaultRows;
            _scene.SightEverywhere = true;

            // An imp in sight beside the player, and one out of its sight (the largest sectors first).
            mobj_t? near = null, far = null;
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
            int tries = 0;
            foreach (SectorFloor f in m.Floors.BySector.Where(f => f.TriangleCount > 0 && f.Sector != start).OrderByDescending(f => f.Vertices.Count))
            {
                if (tries++ >= 64)
                    break;
                Sector s = m.Level.Sectors[f.Sector];
                if (s.CeilingHeight - s.FloorHeight < 64 << Fixed.FRACBITS)
                    continue;
                (int x, int y) = f.InteriorPoint();
                if (m.Level.R_PointInSubsector(x, y).Sector.Index != f.Sector)
                    continue;
                mobj_t imp = world.P_SpawnMobj(x, y, World.ONFLOORZ, mobjtype_t.MT_TROOP);
                spawned.Add(imp);
                if (!world.P_CheckSight(imp, me))
                {
                    far = imp;
                    break;
                }
            }

            // The sight checks between tics leave the sim's state alone.
            ulong sum = world.Checksum();
            _scene.SeeSight();
            if (world.Checksum() != sum)
                Fail($"{map}: sight dim (T6.13t): the sight checks changed the world's checksum");
            CheckSightTable(map, world, me, SightDim.DefaultRows, fresh: true);
            CheckSightDrawn(map, me);
            if (near is not null && _scene.SightDims.Dim(near) != 0)
                Fail($"{map}: sight dim (T6.13t): an imp in sight beside the player is dimmed {_scene.SightDims.Dim(near)}");
            if (far is not null && _scene.SightDims.Dim(far) != SightDim.DefaultRows)
                Fail($"{map}: sight dim (T6.13t): an imp out of sight in sector {far.subsector.sector.Index} is dimmed {_scene.SightDims.Dim(far)}, not {SightDim.DefaultRows}");

            // The player beside the far imp: undimmed at once; the near one, if it is out of sight now, fades after the grace.
            if (far is not null && PlaceBeside(m, far))
            {
                _sightPlaced++;
                if (_scene.SightDims.Dim(far) != 0)
                    Fail($"{map}: sight dim (T6.13t): placed beside the imp in sector {far.subsector.sector.Index}, it is still dimmed {_scene.SightDims.Dim(far)} that tic");
                if (near is not null && !world.P_CheckSight(near, _scene.PlayerMobj!))
                {
                    // (PlaceBeside may have looked from several spots: start from a tic with it in sight)
                    _scene.SightDims.Update([(near, true)]);
                    var dims = new List<int>();
                    for (int t = 0; t < SightDim.GraceTics + 1; t++)
                    {
                        _scene.SeeSight();
                        dims.Add(_scene.SightDims.Dim(near));
                    }
                    int[] expected = [.. Enumerable.Repeat(0, SightDim.GraceTics), SightDim.StepRows];
                    _sightFaded++;
                    if (!dims.SequenceEqual(expected))
                        Fail($"{map}: sight dim (T6.13t): the imp by the start out of sight now dims {string.Join(" ", dims)} over the tics, expected {string.Join(" ", expected)}");
                }
                CheckSightTable(map, world, _scene.PlayerMobj!, SightDim.DefaultRows, fresh: false);
            }

            // Off, and without the game camera: nothing dimmed.
            foreach ((int r, bool every, string what) in new[] { (0, true, "off"), (SightDim.DefaultRows, false, "without the game camera") })
            {
                _scene.SightDimRows = r;
                _scene.SightEverywhere = every;
                _scene.SeeSight();
                _scene.PresentWorld();
                int dimmed = Enumerable.Range(0, _scene.Things?.Entries.Count ?? 0).Count(i => DimOf(_scene.Things!.CustomData(i)) != 0);
                if (dimmed > 0 || _scene.SightDims.Count > 0)
                    Fail($"{map}: sight dim (T6.13t): {what}, {dimmed} billboard(s) dimmed, {_scene.SightDims.Count} thing(s) in the table");
            }
        }
        finally
        {
            foreach (mobj_t mo in spawned)
                world.P_RemoveMobj(mo);
            _scene.SightDimRows = rows;
            _scene.SightEverywhere = everywhere;
            _scene.PlacePlayer((float)(sx / 65536.0), (float)(sy / 65536.0), (float)(angle * (360.0 / 4294967296.0)));
            _scene.PresentWorld();
        }
        if (_failures == failures)
            _sightMaps++;
    }

    // Each drawn candidate's target (and, fresh, its dim) against P_CheckSight; nothing else in the table.
    private void CheckSightTable(string map, World world, mobj_t me, int rows, bool fresh)
    {
        foreach (mobj_t mo in world.Mobjs())
        {
            bool candidate = LevelScene.SightCandidate(mo, me) && LevelScene.IsDrawn(mo) && _scene.FogShows(mo);
            if (!candidate)
            {
                if (_scene.SightDims.InSight(mo) is not null || _scene.SightDimOf(mo) != 0)
                    Fail($"{map}: sight dim (T6.13t): {mo.type} (health {mo.health}) is not a candidate but is in the table, dim {_scene.SightDimOf(mo)}");
                continue;
            }
            _sightChecked++;
            int target = world.P_CheckSight(mo, me) ? 0 : rows;
            if (_scene.SightDims.Target(mo) != target || (fresh && _scene.SightDimOf(mo) != target))
            {
                Fail($"{map}: sight dim (T6.13t): {mo.type} at ({mo.x >> 16}, {mo.y >> 16}) in sector {mo.subsector.sector.Index}: target {_scene.SightDims.Target(mo)}, dim {_scene.SightDimOf(mo)}, P_CheckSight says {target}");
                return;
            }
            if (target > 0)
                _sightDimmed++;
        }
    }

    // The billboards' custom data: each drawn mobj's dim, the player's none.
    private void CheckSightDrawn(string map, mobj_t me)
    {
        _scene.PresentWorld();
        if (_scene.Things is not { } things)
            return;
        IReadOnlyList<mobj_t> drawn = _scene.DrawnMobjs;
        for (int i = 0; i < drawn.Count; i++)
        {
            int dim = DimOf(things.CustomData(i));
            int expected = LevelScene.SightCandidate(drawn[i], me) ? _scene.SightDims.Dim(drawn[i]) : 0;
            if (dim != expected)
            {
                Fail($"{map}: sight dim (T6.13t): billboard {i} ({drawn[i].type}) carries dim {dim} in its custom data, expected {expected}");
                return;
            }
            if (dim > 0)
                _sightDrawn++;
        }
        if (_scene.Player is { } player && DimOf(player.Sprite.CustomData(0)) != 0)
            Fail($"{map}: sight dim (T6.13t): the player's own sprite is dimmed {DimOf(player.Sprite.CustomData(0))}");
    }

    private static int DimOf(Color custom) => ((int)Math.Round(custom.G) >> ThingSprites.FlagSightDimShift) & SightDim.MaxRows;

    // Puts the player 48 units beside `mo` in its sector, where it sees `mo` (true), or back where it was (false).
    private bool PlaceBeside(LevelMesh m, mobj_t mo)
    {
        int sector = mo.subsector.sector.Index;
        foreach ((int dx, int dy) in new[] { (48, 0), (-48, 0), (0, 48), (0, -48), (34, 34), (-34, -34), (34, -34), (-34, 34) })
        {
            int x = mo.x + (dx << Fixed.FRACBITS), y = mo.y + (dy << Fixed.FRACBITS);
            if (m.Level.R_PointInSubsector(x, y).Sector.Index != sector)
                continue;
            _scene.PlacePlayer((float)(x / 65536.0), (float)(y / 65536.0));
            if (_scene.World!.P_CheckSight(mo, _scene.PlayerMobj!))
                return true;
        }
        return false;
    }

    /// <summary>
    /// With a real renderer: thing <paramref name="lit"/> (lit by its sector,
    /// brightened so the dim shows) and <paramref name="bright"/> (full
    /// bright) dimmed by the default amount, drawn through the shifted row
    /// (<see cref="CompareSprite"/> applies <see cref="SightDim.DimmedRow"/>),
    /// and a fixed colormap still winning.
    /// </summary>
    private async Task<int> CheckSightDimPixels(LevelMesh m, ThingSprites things, SpriteAtlas atlas, int lit, int bright, Basis basis)
    {
        string map = m.Level.Name;
        int compared = 0;
        Sector sector = m.Level.Sectors[things.Entries[lit].Sector];
        short light = sector.LightLevel;
        sector.LightLevel = 192;
        m.UpdateSectors();
        foreach ((int i, string what) in new[] { (lit, "lit, sector light 192"), (bright, things.Entries[bright].FullBright ? "full bright" : "lit") }.DistinctBy(c => c.Item1))
        {
            ThingSprites.Entry original = things.Entries[i];
            things.SetEntry(i, original with { SightDim = SightDim.DefaultRows });
            if (DimOf(things.CustomData(i)) != SightDim.DefaultRows)
                Fail($"{map}: thing {i}: sight dim {SightDim.DefaultRows} carried as {DimOf(things.CustomData(i))}");
            compared += await CompareSprite(m, things, atlas, i, basis, $"{map}: thing {i}, {what}, dimmed out of sight {SightDim.DefaultRows} rows");
            if (i == bright)
            {
                m.SetColormapOverride(Colormap.INVERSECOLORMAP);
                compared += await CompareSprite(m, things, atlas, i, basis, $"{map}: thing {i}, dimmed out of sight, fixed colormap {Colormap.INVERSECOLORMAP}");
                m.SetColormapOverride(-1);
            }
            things.SetEntry(i, original);
        }
        sector.LightLevel = light;
        m.UpdateSectors();
        return compared;
    }

    private void PrintSightDim()
    {
        GD.Print($"Level check: sight dim (T6.13t): {_sightMaps} maps: {_sightChecked} monsters checked against P_CheckSight (monster to player), {_sightDimmed} out of sight dimmed, "
            + $"{_sightDrawn} dimmed billboards' custom data; {_sightPlaced} maps placed the player beside an imp out of sight (undimmed that tic), {_sightFaded} with the imp by the start out of sight then (lit for the grace, then a step darker); "
            + "nothing dimmed with it off or without the game camera, the world's checksum unchanged");
        if (_sightMaps == 0)
            Fail("sight dim (T6.13t): no map checked");
    }
}
