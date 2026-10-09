using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Map;
using IsoDoom.Render;
using IsoDoom.Sim;

namespace IsoDoom.Game;

// T6.13l: the fog of war. On every map (no renderer needed): after the load
// the player has looked (its start sector visible), no sector that no open
// line joins to it is visible (nor discovered, but next to one), every visible sector has a mapped line and
// every mapped line's sectors are discovered; the sector_fog texels are the
// fog's states; with the fog on (FogEverywhere: the check has no game
// camera) the drawn things are those the fog's rule lets through (an acting
// thing only in a visible sector, any other in a discovered one), with it
// off all of them. A closed door seen from in front of it is discovered, not
// visible; opened in the sim it shows the sector across it. The reborn's
// reload starts the fog afresh. With a real renderer: every floor top-down
// with the fog dim (an unseen one's pixels the CPU's dim mapping) and hide
// (the background), and a monster in a discovered room out of sight not
// drawn while one in sight is. CheckSaves checks the discovered set
// across a save and load.
public partial class LevelCheck
{
    private int _fogMaps, _fogDoors, _fogHiddenThings, _fogResets;
    private int _fogStartDiscovered, _fogReborns;

    private void CheckFog(LevelMesh m, string map)
    {
        if (_scene.World is null)
            return;
        if (_scene.Fog is not { } fog || _scene.PlayerMobj is not { } me)
        {
            Fail($"{map}: fog (T6.13l): no fog of war or no player");
            return;
        }
        Level level = m.Level;
        int n = level.Sectors.Length;
        int start = me.subsector.sector.Index;
        if (!fog.HasSeen || !fog.IsVisible(start))
            Fail($"{map}: fog (T6.13l): the player's start sector {start} is not visible after the load ({fog.State(start)})");
        // Out of sight for certain: not joined to the start sector through lines with an opening (REJECT is no
        // help: it is 3D sight, and the fog's is 2D, over ledges). Not even discovered unless next to one joined.
        bool[] joined = Joined(level, start);
        for (int s = 0; s < n; s++)
        {
            if (joined[s])
                continue;
            if (fog.IsVisible(s))
                Fail($"{map}: fog (T6.13l): sector {s} is visible, but no open line joins it to the start sector {start}");
            else if (fog.IsDiscovered(s) && !level.Sectors[s].Lines.Any(l => l.FrontSector is { } f && joined[f.Index] || l.BackSector is { } b && joined[b.Index]))
                Fail($"{map}: fog (T6.13l): sector {s} is discovered, but neither it nor a neighbour is joined to the start sector {start}");
        }
        if (n > 1 && Enumerable.Range(0, n).All(fog.IsDiscovered))
            Fail($"{map}: fog (T6.13l): every sector is discovered from the start");
        foreach (int s in Enumerable.Range(0, n).Where(fog.IsVisible))
        {
            if (!level.Sectors[s].Lines.Any(l => (l.Flags & Line.ML_MAPPED) != 0))
                Fail($"{map}: fog (T6.13l): visible sector {s} has no mapped line");
        }
        foreach (Line l in level.Lines.Where(l => (l.Flags & Line.ML_MAPPED) != 0))
        {
            if (l.FrontSector is { } f && !fog.IsDiscovered(f.Index) || l.BackSector is { } b && !fog.IsDiscovered(b.Index))
                Fail($"{map}: fog (T6.13l): mapped {l} has an undiscovered sector");
        }
        _fogStartDiscovered = fog.DiscoveredCount;
        _fogReborns = _scene.Reborns;

        // The shader's texels and the things.
        FogStyle style = _scene.FogStyle;
        _scene.FogStyle = FogStyle.Dim;
        _scene.FogEverywhere = true;
        try
        {
            _scene.UpdateFog();
            if (m.FogMode != FogStyle.Dim)
                Fail($"{map}: fog (T6.13l): the level shader's fog is {m.FogMode} with the fog dim");
            for (int s = 0; s < n; s++)
            {
                if (m.FogData(s) != fog.State(s))
                {
                    Fail($"{map}: fog (T6.13l): sector {s}'s sector_fog texel is {m.FogData(s)}, the fog says {fog.State(s)}");
                    break;
                }
            }
            CheckFogThings(map, fog, me);
        }
        finally
        {
            _scene.FogEverywhere = false;
            _scene.FogStyle = style;
            _scene.UpdateFog();
            _scene.PresentWorld();
        }
        if (m.FogMode != FogStyle.Off)
            Fail($"{map}: fog (T6.13l): the level shader's fog stays {m.FogMode} without the game camera");
        _fogMaps++;
    }

    // The drawn mobjs with the fog on: exactly those its rule lets through; with it off, all.
    private void CheckFogThings(string map, FogOfWar fog, mobj_t me)
    {
        _scene.PresentWorld();
        var drawn = new HashSet<mobj_t>(_scene.DrawnMobjs);
        int all = 0;
        foreach (mobj_t mo in _scene.World!.Mobjs().Where(mo => mo != me && LevelScene.IsDrawn(mo)))
        {
            all++;
            int s = mo.subsector.sector.Index;
            bool shown = LevelScene.FogActs(mo) ? fog.IsVisible(s) : fog.IsDiscovered(s);
            if (drawn.Contains(mo) != shown)
            {
                Fail($"{map}: fog (T6.13l): {mo.type} in sector {s} ({fog.State(s)}) is {(shown ? "not " : "")}drawn");
                return;
            }
            if (!shown && LevelScene.FogActs(mo))
                _fogHiddenThings++;
        }
        _scene.FogStyle = FogStyle.Off;
        _scene.PresentWorld();
        if (_scene.DrawnMobjs.Count != all)
            Fail($"{map}: fog (T6.13l): with the fog off {_scene.DrawnMobjs.Count} of {all} things drawn");
        _scene.FogStyle = FogStyle.Dim;
    }

    /// <summary>
    /// A closed door (a door sector with no height) seen from in front of it,
    /// 24 units off its line's middle: discovered, not visible; opened in the
    /// sim (<see cref="LevelScene.MovePlane"/>), it and the sector across it
    /// are visible. The door is closed again and the player put back.
    /// </summary>
    private void CheckFogDoor(LevelMesh m, string map)
    {
        if (_scene.World is null || _scene.Fog is not { } fog || _scene.PlayerMobj is not { } me || _scene.World.players[_scene.World.consoleplayer].playerstate != playerstate_t.PST_LIVE)
            return;
        Level level = m.Level;
        foreach (Sector door in level.Sectors.Where(s => m.Lids.IsDoor(s.Index) && s.CeilingHeight <= s.FloorHeight))
        {
            foreach (Line near in door.Lines.Where(l => l.BackSector is not null))
            {
                Sector here = near.FrontSector == door ? near.BackSector! : near.FrontSector!;
                Line? far = door.Lines.FirstOrDefault(l => l != near && l.BackSector is not null && (l.FrontSector == door ? l.BackSector : l.FrontSector) != here
                    && (l.FrontSector == door ? l.BackSector : l.FrontSector)!.CeilingHeight > (l.FrontSector == door ? l.BackSector : l.FrontSector)!.FloorHeight);
                if (far is null || here.CeilingHeight <= here.FloorHeight)
                    continue;
                Sector there = far.FrontSector == door ? far.BackSector! : far.FrontSector!;
                // 24 units off the line's middle, on here's side (the front side is the right).
                double dx = near.Dx / 65536.0, dy = near.Dy / 65536.0, length = Math.Sqrt(dx * dx + dy * dy);
                double side = near.FrontSector == here ? 1 : -1;
                double px = (near.V1.X + near.V2.X) / 131072.0 + side * 24 * dy / length, py = (near.V1.Y + near.V2.Y) / 131072.0 - side * 24 * dx / length;
                if (level.R_PointInSubsector((int)(px * 65536), (int)(py * 65536)).Sector != here)
                    continue;
                CheckFogDoorOpens(map, fog, me, door, here, there, (float)px, (float)py);
                return;
            }
        }
    }

    private void CheckFogDoorOpens(string map, FogOfWar fog, mobj_t me, Sector door, Sector here, Sector there, float px, float py)
    {
        float x = (float)(me.x / 65536.0), y = (float)(me.y / 65536.0);
        _scene.PlacePlayer(px, py);
        string where = $"{map}: fog (T6.13l): at ({px:F0}, {py:F0}) in sector {here.Index} before door sector {door.Index}";
        if (!fog.IsVisible(here.Index) || fog.State(door.Index) != SectorSight.Discovered)
            Fail($"{where}: sector {here.Index} {fog.State(here.Index)}, the closed door {fog.State(door.Index)} (expected visible, discovered)");
        int floor = door.FloorHeight >> Fixed.FRACBITS;
        int open = Math.Min(here.CeilingHeight, there.CeilingHeight) >> Fixed.FRACBITS;
        _scene.MovePlane(door.Index, true, open, 16);
        var cmd = new ticcmd_t();
        for (int i = 0; i < 64 && door.CeilingHeight >> Fixed.FRACBITS < open; i++)
            _scene.Tic(cmd);
        _scene.Tic(cmd);
        if (!fog.IsVisible(door.Index) || !fog.IsVisible(there.Index))
            Fail($"{where}: opened in the sim to {door.CeilingHeight >> Fixed.FRACBITS}: the door {fog.State(door.Index)}, sector {there.Index} across it {fog.State(there.Index)} (expected both visible)");
        else
            _fogDoors++;
        _scene.MovePlane(door.Index, true, floor, 16);
        for (int i = 0; i < 64 && door.CeilingHeight > door.FloorHeight; i++)
            _scene.Tic(cmd);
        if (_scene.World!.players[_scene.World.consoleplayer].playerstate == playerstate_t.PST_LIVE)
            _scene.PlacePlayer(x, y);
    }

    /// <summary>After the reborn's reload (T6.12): a fresh fog, as much discovered as from the start after the load.</summary>
    private void CheckFogReborn(string map)
    {
        if (_scene.Reborns == _fogReborns || _scene.Fog is not { } fog)
            return;
        if (fog.DiscoveredCount != _fogStartDiscovered)
            Fail($"{map}: fog (T6.13l): after the reborn {fog.DiscoveredCount} sectors are discovered, {_fogStartDiscovered} were from the start");
        else
            _fogResets++;
    }

    // The sectors joined to start through two-sided lines whose opening has a height (what sight may cross).
    private static bool[] Joined(Level level, int start)
    {
        bool[] joined = new bool[level.Sectors.Length];
        var queue = new Queue<int>([start]);
        joined[start] = true;
        while (queue.Count > 0)
        {
            foreach (Line l in level.Sectors[queue.Dequeue()].Lines)
            {
                if (l.FrontSector is not { } f || l.BackSector is not { } b
                    || Math.Min(f.CeilingHeight, b.CeilingHeight) <= Math.Max(f.FloorHeight, b.FloorHeight))
                    continue;
                foreach (Sector s in new[] { f, b }.Where(s => !joined[s.Index]))
                {
                    joined[s.Index] = true;
                    queue.Enqueue(s.Index);
                }
            }
        }
        return joined;
    }

    private void PrintFog()
    {
        GD.Print($"Level check: fog of war (T6.13l): {_fogMaps} maps looked from the start (no sector seen that no open line joins to it, the mapped lines' sectors discovered, sector_fog as the fog says), "
            + $"{_fogHiddenThings} acting things out of sight not drawn; {_fogDoors} maps opened a closed door in the sim and saw across it; {_fogResets} reborns started the fog afresh");
        if (_fogMaps == 0 || _fogDoors == 0)
            Fail($"fog (T6.13l): {_fogMaps} maps checked, {_fogDoors} doors opened");
    }

    /// <summary>The discovered sectors of the scene's fog (empty without one).</summary>
    private bool[] FogDiscovered() => _scene.Fog is { } fog ? [.. Enumerable.Range(0, fog.Level.Sectors.Length).Select(fog.IsDiscovered)] : [];

    // ---- With a real renderer ----

    /// <summary>
    /// Every floor top-down with the fog dim and hide (<see cref="CheckFloorsTopDown"/>
    /// with the fog's states), then two imps: one by the player, one in a
    /// discovered room out of its sight. Top-down over each, the frame with
    /// it and without it differ where it is drawn: the first always, the
    /// second only with the fog off.
    /// </summary>
    private async Task FogDrawnCheck(LevelMesh m)
    {
        string map = m.Level.Name;
        if (_scene.Fog is not { } fog || _scene.World is not { } world || _scene.PlayerMobj is not { } me)
        {
            Fail($"{map}: fog drawn (T6.13l): no fog or no player");
            return;
        }
        FogStyle style = _scene.FogStyle;
        (DoorLidMode lids, WallCapMode caps) = (m.LidMode, m.CapMode);
        bool thingsShown = _scene.Things?.Visible ?? false;
        _scene.FogEverywhere = true;
        try
        {
            if (Enumerable.Range(0, m.Level.Sectors.Length).All(fog.IsDiscovered))
                Fail($"{map}: fog drawn (T6.13l): nothing unseen to draw");
            // The floors alone, as the other top-down passes.
            m.SetDoorLids(DoorLidMode.Off);
            m.SetWallCaps(WallCapMode.Off);
            long dim = _fogDimPixels;
            foreach (FogStyle s in new[] { FogStyle.Dim, FogStyle.Hide })
            {
                _scene.FogStyle = s;
                _scene.UpdateFog();
                await CheckFloorsTopDown(m, $"fog {s.ToString().ToLowerInvariant()}", fog: fog);
            }
            if (_fogDimPixels == dim)
                Fail($"{map}: fog drawn (T6.13l): no unseen floor pixel compared with the dim mapping");
            GD.Print($"Level check: {map}: fog drawn (T6.13l): {_fogDimPixels - dim} unseen floor pixels drawn as the dim mapping ({m.FogDimLook}), hidden with hide");
            m.SetDoorLids(lids);
            m.SetWallCaps(caps);
            _scene.FogStyle = FogStyle.Dim;
            _scene.UpdateFog();
            if (_scene.Things is { } things)
                things.Visible = true;
            await FogThingsDrawn(m, fog, world, me);
        }
        finally
        {
            _scene.FogEverywhere = false;
            _scene.FogStyle = style;
            _scene.UpdateFog();
            _scene.PresentWorld();
            m.SetDoorLids(lids);
            m.SetWallCaps(caps);
            if (_scene.Things is { } things)
                things.Visible = thingsShown;
        }
    }

    private async Task FogThingsDrawn(LevelMesh m, FogOfWar fog, World world, mobj_t me)
    {
        string map = m.Level.Name;
        int start = me.subsector.sector.Index;
        (int sx, int sy) = (me.x, me.y);
        // A sector from which the start sector is out of sight, by a walk on a fresh copy of the level (no side effects).
        Level copy = Level.Load(_scene.Wad!, map);
        SectorFloor? away = null;
        foreach (SectorFloor f in m.Floors.BySector.Where(f => f.TriangleCount > 0 && f.Sector != start).OrderByDescending(f => f.Vertices.Count))
        {
            Sector s = m.Level.Sectors[f.Sector];
            if (s.CeilingHeight - s.FloorHeight < 64 << Fixed.FRACBITS)
                continue;
            (int x, int y) = f.InteriorPoint();
            if (m.Level.R_PointInSubsector(x, y).Sector.Index != f.Sector)
                continue;
            var trial = new FogOfWar(copy);
            trial.See(x, y);
            if (!trial.IsVisible(start))
            {
                away = f;
                break;
            }
        }
        if (away is null)
        {
            Fail($"{map}: fog drawn (T6.13l): no sector out of sight of the start");
            return;
        }
        (int ax, int ay) = away.InteriorPoint();
        _scene.PlacePlayer((float)(ax / 65536.0), (float)(ay / 65536.0));
        if (fog.State(start) != SectorSight.Discovered || !fog.IsVisible(away.Sector))
        {
            Fail($"{map}: fog drawn (T6.13l): from sector {away.Sector} the start sector is {fog.State(start)}, sector {away.Sector} {fog.State(away.Sector)}");
            return;
        }
        // An imp 48 units east of the player (in sight), and one at the start (discovered, out of sight).
        (int X, int Y, string What)[] spots = [(ax + (48 << Fixed.FRACBITS), ay, "in sight"), (sx, sy, "out of sight")];
        if (m.Level.R_PointInSubsector(spots[0].X, spots[0].Y).Sector.Index != away.Sector)
            spots[0] = (ax - (48 << Fixed.FRACBITS), ay, "in sight");
        foreach ((int x, int y, string what) in spots)
        {
            foreach (FogStyle style in new[] { FogStyle.Dim, FogStyle.Off })
            {
                _scene.FogStyle = style;
                _scene.UpdateFog();
                mobj_t imp = world.P_SpawnMobj(x, y, World.ONFLOORZ, mobjtype_t.MT_TROOP);
                _scene.PresentWorld();
                Sector s = imp.subsector.sector.map;
                float z = (float)(s.FloorHeight / 65536.0);
                var basis = new Basis(new Vector3(1, 0, 0), new Vector3(0, 0, -1), new Vector3(0, 1, 0));
                Ortho(basis, new Vector3((float)(x / 65536.0), (float)(y / 65536.0), z + 256.5f), 1, 512);
                string where = $"{map}: fog drawn (T6.13l): an imp {what} in sector {s.Index} ({fog.State(s.Index)}), fog {style.ToString().ToLowerInvariant()}";
                byte[]? with = await Capture(where);
                world.P_RemoveMobj(imp);
                _scene.PresentWorld();
                byte[]? without = await Capture(where);
                if (with is null || without is null)
                    return;
                int differ = 0;
                for (int i = 0; i < with.Length; i += 4)
                {
                    if (with[i] != without[i] || with[i + 1] != without[i + 1] || with[i + 2] != without[i + 2])
                        differ++;
                }
                bool drawn = what == "in sight" || style == FogStyle.Off;
                if ((differ > 0) != drawn)
                    Fail($"{where}: {differ} pixels differ with it and without it, expected it {(drawn ? "" : "not ")}drawn");
                _pixels += with.Length / 4;
            }
        }
        _scene.FogStyle = FogStyle.Dim;
        _scene.PlacePlayer((float)(sx / 65536.0), (float)(sy / 65536.0));
        GD.Print($"Level check: {map}: fog drawn (T6.13l): an imp in sight drawn and one in the discovered start sector {start} out of sight from sector {away.Sector} not (drawn with the fog off)");
    }
}
