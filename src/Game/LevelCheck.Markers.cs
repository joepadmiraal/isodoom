using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Map;
using IsoDoom.Render;

namespace IsoDoom.Game;

/// <summary>
/// The level check's switch marker part (T6.13p, SPEC §12). On every map:
/// every switch side (a drawn section with a switch texture of the game's
/// pairs) has a marker, shown from the game camera exactly when its wall faces
/// away from it. With a real renderer: one marker straight down at 1 unit per
/// pixel (E1M3's exit, line 982, when the map is E1M3; else the largest whose
/// wall faces away, or the largest), with the markers off and on, before and
/// after its switch changes texture as a use does (pixels well inside it must
/// show the switch texture's texel at its column and row, lit as a floor of its
/// sector just above its floor; pixels outside every marker must not change),
/// and from the game camera's angle a marker of a wall facing away (drawn) and
/// one of a wall facing the camera (not drawn).
/// T10.6a: on every map, every lower switch side with a sector behind it has a
/// top marker on that sector's floor, shown from the game camera exactly when
/// its wall faces away and its rise hides more than half its floor marker
/// (<see cref="SwitchMarkers.TopShows"/>; the floor markers so hidden are not
/// looked for from the game camera). With a real renderer, from the game
/// camera, the largest shown top markers draw their texels lit as a floor of
/// their sector, every pixel only they cover against the CPU, and the largest
/// whose block a cut centre in front of its switch cuts draws on the cutaway's
/// dark cap, every pixel in the cut disc only it covers against the CPU.
/// T10.6e: an upper switch over a door with a lid has a top marker too, on the
/// door's lid only (<see cref="SwitchMarker.Lid"/>); with a renderer the
/// largest shown is viewed on the lid with the cutaway off, on, the doors cut
/// (on the cap) and the door open, and not drawn with the lids off
/// (<see cref="CompareDoorTopMarkersFromGameCamera"/>).
/// </summary>
public partial class LevelCheck
{
    private int _markerMaps, _markerSides, _markersShown, _topMarkers, _topMarkersShown, _doorTopMarkers;

    /// <summary>The marker the renderer check views on E1M3: its exit switch, whose wall faces north (the task's case).</summary>
    private const int E1M3ExitLine = 982;

    // The direction towards the game camera, flattened (map axes).
    private static Vector2 GameCameraFlat()
    {
        Vector3 toCamera = Cutaway.ToMapAxes(GameBasis(IsoCamera.DefaultPitch).Z).Normalized();
        return new Vector2(toCamera.X, toCamera.Y).Normalized();
    }

    /// <summary>
    /// Every switch side of <paramref name="map"/> has a marker on its floor,
    /// which shows from the game camera when its wall faces away from it (its
    /// normal off the direction towards the camera by more than 90°) and not
    /// when it faces the camera; the mesh holds every marker's triangles.
    /// </summary>
    private void CheckSwitchMarkerData(LevelMesh m, string map)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string off, string on) in LevelScene.SwitchPairs(_scene.GameMode))
        {
            names.Add(off);
            names.Add(on);
        }
        var bySide = m.Markers.Markers.ToDictionary(k => (k.Section.Line.Index, k.Section.Side));
        Vector2 flat = GameCameraFlat();
        var sides = new SortedSet<(int Line, int Side)>();
        foreach (WallSection s in m.Walls.Sections)
        {
            if (LevelMesh.IsDrawn(s) && names.Contains(SwitchMarkers.TextureName(s)) && (s.V1.X != s.V2.X || s.V1.Y != s.V2.Y))
                sides.Add((s.Line.Index, s.Side));
        }
        var shown = new List<int>();
        int noFloor = 0;
        foreach ((int line, int side) in sides)
        {
            string what = $"{map}: line {line} side {side}'s switch";
            if (!bySide.TryGetValue((line, side), out SwitchMarker? marker))
            {
                // P3: no floor of its sector in front of it because the map's BSP leaf there is
                // another sector's (a node artefact, T2.2a: AV MAP31 line 1141): the map's data.
                WallSection sw = m.Walls.Sections.First(k => k.Line.Index == line && k.Side == side);
                double ldx = (sw.V2.X - (double)sw.V1.X) / 65536, ldy = (sw.V2.Y - (double)sw.V1.Y) / 65536, llen = Math.Sqrt(ldx * ldx + ldy * ldy);
                double px = (sw.V1.X + sw.V2.X) / 131072.0 + 4 * ldy / llen, py = (sw.V1.Y + sw.V2.Y) / 131072.0 - 4 * ldx / llen;
                int leaf = m.Level.R_PointInSubsector((int)Math.Round(px * 65536), (int)Math.Round(py * 65536)).Sector.Index;
                if (leaf != sw.FrontSector.Index)
                {
                    GD.Print($"Level check: {what}: no marker, the BSP leaf 4 units in front of it is sector {leaf}'s, not its sector {sw.FrontSector.Index}'s (a node artefact, T2.2a)");
                    noFloor++;
                }
                else
                    Fail($"{what}: no marker");
                continue;
            }
            // The wall's normal into its sector, from its section's ends.
            WallSection s = marker.Section;
            double dx = (s.V2.X - (double)s.V1.X) / 65536, dy = (s.V2.Y - (double)s.V1.Y) / 65536, length = Math.Sqrt(dx * dx + dy * dy);
            double facing = (dy * flat.X - dx * flat.Y) / length; // > 0: towards the camera
            bool expected = facing <= 1e-3;
            if (SwitchMarkers.Shows(marker, flat.X, flat.Y) != expected)
                Fail($"{what}: its marker {(expected ? "does not show" : "shows")}, its wall faces {(expected ? "away from" : "towards")} the game camera ({facing:F3})");
            if (expected)
                shown.Add(line);
            if (marker.Depth is < SwitchMarkers.MinDepth or > SwitchMarkers.MaxDepth && marker.Depth != m.Textures.TextureDefs[s.Texture].Height)
                Fail($"{what}: its marker is {marker.Depth} deep");
        }
        if (bySide.Count + noFloor != sides.Count)
            Fail($"{map}: {bySide.Count} switch markers for {sides.Count} switch sides");
        int triangles = m.Markers.Markers.Sum(k => k.Pieces.Sum(p => p.Length - 2));
        if (triangles != m.SwitchMarkerTriangleCount)
            Fail($"{map}: {m.SwitchMarkerTriangleCount} switch marker triangles, {triangles} expected");

        // T10.6a: a top marker for exactly the lower switch sides with a sector behind them, in it, as deep as the floor marker.
        Vector3 toCamera = Cutaway.ToMapAxes(GameBasis(IsoCamera.DefaultPitch).Z).Normalized();
        var tops = m.Markers.Tops.ToDictionary(k => (k.Section.Line.Index, k.Section.Side));
        var topsShown = new List<int>();
        var doorTops = new List<int>();
        foreach ((int line, int side) in sides)
        {
            WallSection s = bySide.TryGetValue((line, side), out SwitchMarker? marker) ? marker.Section
                : tops.TryGetValue((line, side), out SwitchMarker? only) ? only.Section : m.Walls.Sections.First(k => k.Line.Index == line && k.Side == side);
            string what = $"{map}: line {line} side {side}'s switch";
            // T10.6e: and the upper switches over a door with a lid, on its lid.
            bool overDoor = SwitchMarkers.IsOverDoor(s, m.Lids);
            bool expected = (s.Kind == WallSectionKind.Lower && s.BackSector is not null) || overDoor;
            if (!tops.TryGetValue((line, side), out SwitchMarker? top))
            {
                if (expected)
                    Fail($"{what}: a {s.Kind} switch with sector {s.BackSector!.Index} behind it, but no top marker");
                continue;
            }
            if (!expected)
                Fail($"{what}: a top marker on a {s.Kind} switch{(s.BackSector is null ? " with no sector behind it" : " not over a door")}");
            else if (top.Lid != overDoor)
                Fail($"{what}: its top marker is {(top.Lid ? "" : "not ")}on a lid only, the {s.Kind} switch is {(overDoor ? "" : "not ")}over a door");
            else if (!top.Top || top.Sector != s.BackSector!.Index || top.Section != s || marker is not null && top.Rows != marker.Rows)
                Fail($"{what}: its top marker is on sector {top.Sector} (rows {top.Rows}), expected sector {s.BackSector!.Index}");
            else if (top.Pieces.Any(p => p.Any(v => top.InFront(v.X, v.Y) > 1e-6 || top.InFront(v.X, v.Y) < -top.Depth - 1e-6)))
                Fail($"{what}: its top marker reaches outside the {top.Depth} units behind its wall");
            if (TopShows(m, top, toCamera))
                topsShown.Add(line);
            if (top.Lid)
                doorTops.Add(line);
        }
        if (tops.Count != sides.Count(k => tops.ContainsKey(k)))
            Fail($"{map}: {tops.Count} top markers, {sides.Count(k => tops.ContainsKey(k))} of them on switch sides");
        int topTriangles = m.Markers.Tops.Sum(k => k.Pieces.Sum(p => p.Length - 2));
        if (topTriangles != m.SwitchTopTriangleCount)
            Fail($"{map}: {m.SwitchTopTriangleCount} switch top marker triangles, {topTriangles} expected");

        _markerMaps++;
        _markerSides += sides.Count;
        _markersShown += shown.Count;
        _topMarkers += tops.Count;
        _topMarkersShown += topsShown.Count;
        _doorTopMarkers += doorTops.Count;
        if (sides.Count > 0)
        {
            GD.Print($"Level check: {map}: {sides.Count} switch sides with markers, {shown.Count} facing away from the game camera (shown: lines {string.Join(" ", shown)}); "
                + $"{tops.Count} top markers, {topsShown.Count} shown (lines {string.Join(" ", topsShown)}), {doorTops.Count} of them on doors' lids (lines {string.Join(" ", doorTops)})");
        }
    }

    /// <summary>T10.6a: the height a top marker lies on, as uploaded: its sector's lid while the lid shows (the shader's rule, kind 5's), else its floor; T10.6e: a door's lid marker at its other sector's ceiling where that is higher.</summary>
    private static float TopSurface(LevelMesh m, SwitchMarker k)
    {
        if (!LidShows(m, k.Sector))
            return m.SectorData(k.Sector).R;
        // T10.6e: a door's at the top of its other face where that is higher.
        float lid = m.LidData(k.Sector).Height;
        return k.Lid && k.Beyond >= 0 ? Math.Max(lid, m.SectorData(k.Beyond).G) : lid;
    }

    // The height a marker is drawn at (with the cutaway off).
    private static float MarkerZ(LevelMesh m, SwitchMarker k) => (k.Top ? TopSurface(m, k) : m.SectorData(k.Sector).R) + SwitchMarkers.Lift;

    // T10.6a: whether a top marker shows from a camera in the direction toCamera (map axes): the shader's rule on the uploaded heights
    // (T10.6e: a door's only while its lid shows).
    private static bool TopShows(LevelMesh m, SwitchMarker k, Vector3 toCamera) => (!k.Lid || LidShows(m, k.Sector))
        && SwitchMarkers.TopShows(k, TopSurface(m, k) - m.SectorData(k.Section.FrontSector.Index).R, toCamera.X, toCamera.Y, toCamera.Z);

    // Whether a sector's lid shows, as uploaded: the shader's rule (kind 5's).
    private static bool LidShows(LevelMesh m, int sector)
    {
        (float lid, int slot, _, _) = m.LidData(sector);
        return m.LidMode == DoorLidMode.On && slot >= 0 && m.SectorData(sector).G < lid;
    }

    private async Task SwitchMarkerCheck(LevelMesh m)
    {
        string map = m.Level.Name;
        if (m.Markers.Markers.Count == 0)
        {
            GD.Print($"Level check: {map}: no switch markers to view");
            return;
        }
        m.SetCutaway(new CutawaySettings { Style = CutawayStyle.Off });
        Vector2 flat = GameCameraFlat();
        SwitchMarker marker = m.Markers.Markers.FirstOrDefault(k => map == "E1M3" && k.Section.Line.Index == E1M3ExitLine)
            ?? m.Markers.Markers.Where(k => SwitchMarkers.Shows(k, flat.X, flat.Y)).MaxBy(k => k.Pieces.Sum(Area))
            ?? m.Markers.Markers.MaxBy(k => k.Pieces.Sum(Area))!;
        WallSection s = marker.Section;
        string what = $"{map}: line {s.Line.Index}'s switch marker";
        await CompareMarkerTopDown(m, marker, $"{what} ({SwitchMarkers.TextureName(s)})");

        // Used: the switch shows its other texture (p_switch.c P_ChangeSwitchTexture, by name), and so does its marker.
        string current = SwitchMarkers.TextureName(s);
        (string off, string on) = LevelScene.SwitchPairs(_scene.GameMode).First(p => p.Off.Equals(current, StringComparison.OrdinalIgnoreCase)
            || p.On.Equals(current, StringComparison.OrdinalIgnoreCase));
        string other = off.Equals(current, StringComparison.OrdinalIgnoreCase) ? on : off;
        string old = SetPartTexture(s, other);
        m.UpdateSectors();
        await CompareMarkerTopDown(m, marker, $"{what} after a use ({other})");
        SetPartTexture(s, old);
        m.UpdateSectors();

        await CompareMarkersFromGameCamera(m, flat);
        await CompareTopMarkersFromGameCamera(m);
        await CompareDoorTopMarkersFromGameCamera(m); // T10.6e
        m.SetSwitchMarkers(SwitchMarkerMode.Off);
    }

    private static bool InMarker(SwitchMarker marker, double x, double y) => marker.Pieces.Any(p => InConvex(p, x, y));

    private async Task CompareMarkerTopDown(LevelMesh m, SwitchMarker marker, string what)
    {
        Vector2I size = ViewSize();
        int w = size.X, h = size.Y;
        double minX = marker.Pieces.Min(p => p.Min(v => v.X)), maxX = marker.Pieces.Max(p => p.Max(v => v.X));
        double minY = marker.Pieces.Min(p => p.Min(v => v.Y)), maxY = marker.Pieces.Max(p => p.Max(v => v.Y));
        int left = (int)Math.Floor((minX + maxX) / 2) - w / 2, top = (int)Math.Floor((minY + maxY) / 2) + h / 2;
        (int lowest, int highest) = HeightRange(m.Level);
        float cameraZ = highest + 64.5f;
        var basis = new Basis(new Vector3(1, 0, 0), new Vector3(0, 0, -1), new Vector3(0, 1, 0));
        Ortho(basis, new Vector3(left + w / 2f, top - h / 2f, cameraZ), 1, cameraZ - lowest + 64);
        m.SetSwitchMarkers(SwitchMarkerMode.Off);
        byte[]? off = await Capture($"{what}, top-down, markers off");
        m.SetSwitchMarkers(SwitchMarkerMode.On);
        byte[]? on = await Capture($"{what}, top-down, markers on");
        if (off is null || on is null)
            return;

        WallSection s = marker.Section;
        Sector sector = s.FrontSector;
        IsoDoom.Wad.Graphics.IndexedImage tex = ShownComposite(m, Textures.R_CheckTextureNumForName(SwitchMarkers.TextureName(s)));
        float z = sector.FloorHeight / 65536f + SwitchMarkers.Lift;
        var near = m.Markers.Markers.Where(k => k.Pieces.Any(p => p.Any(v => v.X > minX - 64 && v.X < maxX + 64 && v.Y > minY - 64 && v.Y < maxY + 64))).ToList();
        int compared = 0, markerPixels = 0, badMarker = 0, badKeep = 0;
        string firstMarker = "", firstKeep = "";
        (double, double)[] samples = [(0, 0), (CutMargin, 0), (-CutMargin, 0), (0, CutMargin), (0, -CutMargin)];
        for (int py = Math.Max(0, top - (int)maxY - 16); py < Math.Min(h, top - (int)minY + 16); py++)
        {
            for (int px = Math.Max(0, (int)minX - 16 - left); px < Math.Min(w, (int)maxX + 16 - left); px++)
            {
                double x = left + px + 0.5, y = top - py - 0.5;
                int inThis = 0, inOther = 0;
                foreach ((double ox, double oy) in samples)
                {
                    foreach (SwitchMarker k in near)
                    {
                        if (InMarker(k, x + ox, y + oy))
                        {
                            if (k == marker)
                                inThis++;
                            else
                                inOther++;
                        }
                    }
                }
                int p = (py * w + px) * 4;
                (int R, int G, int B) got = (on[p], on[p + 1], on[p + 2]);
                if (inThis == 0 && inOther == 0)
                {
                    compared++;
                    if (got != (off[p], off[p + 1], off[p + 2]) && badKeep++ == 0)
                        firstKeep = $"pixel ({px}, {py}), map ({x}, {y}): drew {got}, {(off[p], off[p + 1], off[p + 2])} with the markers off";
                }
                else if (inThis == samples.Length && inOther == 0)
                {
                    double column = s.TextureOffset / 65536.0 + marker.Column(x, y), row = marker.Row(x, y);
                    if (Math.Abs(column - Math.Round(column)) < 1.0 / 64 || Math.Abs(row - Math.Round(row)) < 1.0 / 64)
                        continue;
                    int colormap = ExpectedColormap(m, false, sector.LightLevel, 0, x, y, cameraZ - z);
                    if (colormap < 0)
                        continue;
                    (int tc, int tr) = TextureWrap.WallTexel((int)Math.Floor(column), (int)Math.Floor(row), tex.Width, tex.Height, WallTextureTiling.Vanilla);
                    (int R, int G, int B) expected = Shade(tex[tc, tr], colormap);
                    compared++;
                    markerPixels++;
                    if (got != expected && badMarker++ == 0)
                        firstMarker = $"pixel ({px}, {py}), map ({x}, {y}), column {column:F2}, row {row:F2}: drew {got}, expected {expected}";
                }
            }
        }
        _pixels += compared;
        if (badMarker > 0)
            Fail($"{what}, top-down: {badMarker} of {markerPixels} marker pixel(s) differ, first at {firstMarker}");
        if (badKeep > 0)
            Fail($"{what}, top-down: {badKeep} pixel(s) outside the markers changed with the markers on, first at {firstKeep}");
        if (markerPixels < 50)
            Fail($"{what}, top-down: only {markerPixels} marker pixels compared (at least 50 expected)");
        GD.Print($"Level check: {what}, top-down: {compared} pixels compared, {markerPixels} of them on the marker against the texture, the rest as with the markers off");
    }

    /// <summary>
    /// From the game camera's angle: a marker of a wall facing away changes
    /// the pixels only it covers (the first of the ten largest that is not
    /// hidden), and the largest of a wall facing the camera changes none (the
    /// shader collapses it: the wall itself shows the switch).
    /// </summary>
    private async Task CompareMarkersFromGameCamera(LevelMesh m, Vector2 flat)
    {
        string map = m.Level.Name;
        Basis basis = GameBasis(IsoCamera.DefaultPitch);
        Vector3 toCamera = Cutaway.ToMapAxes(basis.Z).Normalized();
        // T10.6a: a marker whose top marker shows is hidden by its block (Doom II MAP01's line 75): the top marker's check views it.
        var topShown = m.Markers.Tops.Where(k => TopShows(m, k, toCamera)).Select(k => (k.Section.Line.Index, k.Section.Side)).ToHashSet();
        List<SwitchMarker> largest = [.. m.Markers.Markers.OrderByDescending(k => k.Pieces.Sum(Area))];
        List<SwitchMarker> open = [.. largest.Where(k => SwitchMarkers.Shows(k, flat.X, flat.Y) && !topShown.Contains((k.Section.Line.Index, k.Section.Side)))];
        int changed = 0;
        SwitchMarker? shown = null;
        foreach (SwitchMarker k in open.Take(10))
        {
            changed = await CompareMarkerFromGameCamera(m, k, basis, toCamera, true);
            if (changed >= 50)
            {
                shown = k;
                break;
            }
        }
        if (shown is null && open.Count > 0)
            Fail($"{map}: no switch marker of a wall facing away from the game camera (and no top marker shown) drew 50 pixels (of the ten largest)");
        SwitchMarker? facing = largest.FirstOrDefault(k => !SwitchMarkers.Shows(k, flat.X, flat.Y));
        int facingPixels = facing is null ? 0 : await CompareMarkerFromGameCamera(m, facing, basis, toCamera, false);
        string away = shown is null ? $"none facing away{(topShown.Count > 0 ? $" but those under {topShown.Count} top marker(s)" : "")}"
            : $"line {shown.Section.Line.Index}'s (facing away) changed {changed} pixels";
        string towards = facing is null ? "none facing the camera" : $"line {facing.Section.Line.Index}'s (facing the camera) none of the {facingPixels} it alone covers";
        GD.Print($"Level check: {map}: switch markers from the game camera: {away}; {towards}");
    }

    // The pixels where only this marker would show, from the game camera centred on it: with
    // drawnExpected false they must not change with the markers on (returns how many there
    // are); with true, returns how many changed.
    private async Task<int> CompareMarkerFromGameCamera(LevelMesh m, SwitchMarker marker, Basis basis, Vector3 toCamera, bool drawnExpected)
    {
        float Z(SwitchMarker k) => MarkerZ(m, k);
        double cx = marker.Pieces.Average(p => p.Average(v => v.X)), cy = marker.Pieces.Average(p => p.Average(v => v.Y));
        CutView(new Vector3((float)cx, (float)cy, Z(marker) - Cutaway.Anchor), basis, toCamera);
        Camera3D camera = _scene.Camera;
        List<(double X, double Y)[]> Project(SwitchMarker k) =>
            [.. k.Pieces.Select(p => p.Select(v => camera.UnprojectPosition(new Vector3((float)v.X, Z(k), (float)-v.Y) / LevelMesh.MapUnitsPerMetre))
                .Select(q => ((double)q.X, (double)q.Y)).ToArray())];
        List<(double X, double Y)[]> own = Project(marker);
        List<(double X, double Y)[]> others = [.. m.Markers.All.Where(k => k != marker).SelectMany(Project)];
        Vector2I size = ViewSize();
        var alone = new List<Vector2I>();
        for (int py = Math.Max(0, (int)own.Min(p => p.Min(v => v.Y))); py < Math.Min(size.Y, (int)own.Max(p => p.Max(v => v.Y)) + 1); py++)
        {
            for (int px = Math.Max(0, (int)own.Min(p => p.Min(v => v.X))); px < Math.Min(size.X, (int)own.Max(p => p.Max(v => v.X)) + 1); px++)
            {
                double x = px + 0.5, y = py + 0.5;
                if (own.Any(p => InConvex(p, x, y)) && !others.Any(p => InConvex(p, x, y)))
                    alone.Add(new Vector2I(px, py));
            }
        }
        string what = $"{m.Level.Name}: line {marker.Section.Line.Index}'s switch marker from the game camera";
        m.SetSwitchMarkers(SwitchMarkerMode.Off);
        byte[]? off = await Capture($"{what}, markers off");
        m.SetSwitchMarkers(SwitchMarkerMode.On);
        byte[]? on = await Capture($"{what}, markers on");
        if (off is null || on is null)
            return 0;
        (int changed, string first) = ChangedPixels(alone, on, off, "the markers off");
        _pixels += alone.Count;
        if (!drawnExpected && changed > 0)
            Fail($"{what}: its wall faces the camera, but {changed} of its {alone.Count} pixel(s) changed with the markers on, first at {first}");
        if (!drawnExpected && alone.Count < 20)
            Fail($"{what}: only {alone.Count} pixels to compare (at least 20 expected)");
        return drawnExpected ? changed : alone.Count;
    }

    /// <summary>
    /// T10.6a: from the game camera, the four largest top markers shown
    /// (<see cref="TopShows"/>) with the cutaway off, and the largest whose top
    /// is above the cutoff of a cut centre 8 units in front of its switch (a
    /// player at it) with the cutaway cutting with its dark cap: the pixels
    /// only the marker covers (in the cut disc with the cutaway) show its
    /// texel at the point of its plane there, lit as a floor of its sector (and
    /// darkened as the cap), or nothing changes where something stands in
    /// front of it; at least 50 must show it.
    /// </summary>
    private async Task CompareTopMarkersFromGameCamera(LevelMesh m)
    {
        string map = m.Level.Name;
        Basis basis = GameBasis(IsoCamera.DefaultPitch);
        Vector3 toCamera = Cutaway.ToMapAxes(basis.Z).Normalized();
        List<SwitchMarker> shown = [.. m.Markers.Tops.Where(k => TopShows(m, k, toCamera)).OrderByDescending(k => k.Pieces.Sum(Area))];
        if (shown.Count == 0)
        {
            GD.Print($"Level check: {map}: no top switch marker shown from the game camera to view");
            return;
        }
        foreach (SwitchMarker k in shown.Take(4))
            await CompareTopMarkerFromGameCamera(m, k, basis, toCamera, null, false);
        float Front(SwitchMarker k) => m.SectorData(k.Section.FrontSector.Index).R;
        if (shown.FirstOrDefault(k => TopSurface(m, k) > Front(k) + CutawaySettings.DefaultHeight + 1 && !m.Lids.IsDoor(k.Sector)) is SwitchMarker tall)
            await CompareTopMarkerFromGameCamera(m, tall, basis, toCamera, new CutawaySettings { Style = CutawayStyle.Cut, Cap = CutawayCap.Dark }, true);
        else
            GD.Print($"Level check: {map}: no top switch marker shown whose block the cutaway cuts");
    }

    /// <summary>
    /// T10.6e: with the lids on, from the game camera, the largest top marker
    /// shown of an upper switch over a door (<see cref="SwitchMarker.Lid"/>),
    /// on the door's lid: with the cutaway off; with the default cutaway
    /// (doors kept) around a centre in front of its switch, the door and its
    /// marker whole; with the doors cut, on the dark cap of the cut door; and
    /// with the door open (its ceiling 4 below its lid, as p_doors.c opens
    /// it), still on the lid. Every pixel only it covers against the CPU
    /// texel and light, at least 50 drawn each time; and with the lids off,
    /// none of them changes.
    /// </summary>
    private async Task CompareDoorTopMarkersFromGameCamera(LevelMesh m)
    {
        string map = m.Level.Name;
        Basis basis = GameBasis(IsoCamera.DefaultPitch);
        Vector3 toCamera = Cutaway.ToMapAxes(basis.Z).Normalized();
        DoorLidMode lidMode = m.LidMode;
        m.SetDoorLids(DoorLidMode.On);
        SwitchMarker? k = m.Markers.Tops.Where(t => t.Lid && TopShows(m, t, toCamera)).MaxBy(t => t.Pieces.Sum(Area));
        if (k is null)
        {
            GD.Print($"Level check: {map}: no top switch marker on a door's lid shown from the game camera to view");
            m.SetDoorLids(lidMode);
            return;
        }
        await CompareTopMarkerFromGameCamera(m, k, basis, toCamera, null, false, ", on its door's lid");
        await CompareTopMarkerFromGameCamera(m, k, basis, toCamera, new CutawaySettings(), false, ", the cutaway on around a centre in front of it, the door kept");
        float front = m.SectorData(k.Section.FrontSector.Index).R;
        if (TopSurface(m, k) > front + CutawaySettings.DefaultHeight + 1 && m.SectorData(k.Sector).G <= front + CutawaySettings.DefaultHeight)
        {
            await CompareTopMarkerFromGameCamera(m, k, basis, toCamera, new CutawaySettings { Doors = CutawayDoors.Cut, Cap = CutawayCap.Dark }, true,
                ", the doors cut: on the cut door's cap");
        }
        else
            GD.Print($"Level check: {map}: line {k.Section.Line.Index}'s door does not span the cutoff of a centre in front of its switch: no view with the doors cut");

        // Open, as p_doors.c opens it: 4 below its lid, which stays as the lintel's top, the marker on it.
        Sector door = m.Level.Sectors[k.Sector];
        int old = door.CeilingHeight;
        door.CeilingHeight = ((int)m.LidData(k.Sector).Height - 4) << Fixed.FRACBITS;
        m.UpdateSectors();
        await CompareTopMarkerFromGameCamera(m, k, basis, toCamera, null, false, $", its door open to {door.CeilingHeight >> Fixed.FRACBITS}");
        door.CeilingHeight = old;
        m.UpdateSectors();

        // The lids off: no lid, so no marker (the shader collapses it): where it lay, nothing changes with the markers on.
        float lid = TopSurface(m, k);
        m.SetDoorLids(DoorLidMode.Off);
        await CompareTopMarkerFromGameCamera(m, k, basis, toCamera, null, false, ", the lids off: not drawn", lid);
        m.SetDoorLids(lidMode);
    }

    // With noneAt, the marker must not be drawn: the pixels only it would cover at that height must not change with the markers on.
    private async Task CompareTopMarkerFromGameCamera(LevelMesh m, SwitchMarker k, Basis basis, Vector3 toCamera, CutawaySettings? cutaway, bool onCap,
        string label = "", float? noneAt = null)
    {
        WallSection s = k.Section;
        Sector sector = m.Level.Sectors[k.Sector];
        CutawaySettings settings = cutaway ?? new CutawaySettings();
        bool cut = onCap;
        float front = m.SectorData(s.FrontSector.Index).R;
        // A cut centre 8 units in front of the middle of the switch, on its floor (a player using it).
        double mx = k.X + k.Dx * k.Length / 2 + k.Normal.X * 8, my = k.Y + k.Dy * k.Length / 2 + k.Normal.Y * 8;
        var centre = new Vector3((float)mx, (float)my, front);
        float z = (noneAt ?? (cut ? front + settings.Height : TopSurface(m, k))) + SwitchMarkers.Lift;
        string what = $"{m.Level.Name}: line {s.Line.Index}'s top switch marker from the game camera{(cut && label.Length == 0 ? ", its block cut by a centre in front of it" : label)}";
        double cx = k.Pieces.Average(p => p.Average(v => v.X)), cy = k.Pieces.Average(p => p.Average(v => v.Y));
        CutView(new Vector3((float)cx, (float)cy, z - Cutaway.Anchor), basis, toCamera);
        Camera3D camera = _scene.Camera;
        List<List<Vector2>> Project(SwitchMarker marker, float height) =>
            [.. marker.Pieces.Select(p => p.Select(v => camera.UnprojectPosition(new Vector3((float)v.X, height, (float)-v.Y) / LevelMesh.MapUnitsPerMetre)).ToList())];
        List<List<Vector2>> own = Project(k, z);
        // T10.6e: a door's lid marker hides its own floor marker, lower and beyond the door from the camera, where both are
        // (the door's cap copy lies above the floor marker on screen); and a marker whose wall faces the camera is never drawn
        // (the door's other face's, on the same lid: Doom II MAP09's line 209 behind 210).
        Vector2 flat = new Vector2(toCamera.X, toCamera.Y).Normalized();
        List<List<Vector2>> others = [.. m.Markers.All.Where(o => o != k && (!k.Lid || o.Top || o.Section != s) && SwitchMarkers.Shows(o, flat.X, flat.Y))
            .SelectMany(o => Project(o, MarkerZ(m, o)))];
        static bool In(List<Vector2> p, float x, float y) => InConvex([.. p.Select(v => ((double)v.X, (double)v.Y))], x, y);
        Vector2 anchor = camera.UnprojectPosition(LevelMesh.ToGodot((int)(centre.X * 65536), (int)(centre.Y * 65536), centre.Z + Cutaway.Anchor));
        Vector2I size = ViewSize();
        var alone = new List<Vector2I>();
        int minY = (int)own.Min(p => p.Min(v => v.Y)), maxY = (int)own.Max(p => p.Max(v => v.Y)), minX = (int)own.Min(p => p.Min(v => v.X)), maxX = (int)own.Max(p => p.Max(v => v.X));
        for (int py = Math.Max(0, minY); py < Math.Min(size.Y, maxY + 1); py++)
        {
            for (int px = Math.Max(0, minX); px < Math.Min(size.X, maxX + 1); px++)
            {
                float x = px + 0.5f, y = py + 0.5f;
                if (cut && (new Vector2(x, y) - anchor).Length() >= settings.Radius - 3)
                    continue;
                bool inside = true;
                foreach ((float ox, float oy) in new[] { (0f, 0f), (2f, 0f), (-2f, 0f), (0f, 2f), (0f, -2f) })
                {
                    inside &= own.Any(p => In(p, x + ox, y + oy)) && !others.Any(p => In(p, x + ox, y + oy));
                    if (!inside)
                        break;
                }
                if (inside)
                    alone.Add(new Vector2I(px, py));
            }
        }
        if (cutaway is not null)
        {
            m.SetCutawayCentres(centre, null);
            m.SetCutaway(settings);
        }
        m.SetSwitchMarkers(SwitchMarkerMode.Off);
        byte[]? off = await Capture($"{what}, markers off");
        m.SetSwitchMarkers(SwitchMarkerMode.On);
        byte[]? on = await Capture($"{what}, markers on");
        if (cutaway is not null)
        {
            m.SetCutawayCentres(null, null);
            m.SetCutaway(new CutawaySettings { Style = CutawayStyle.Off });
        }
        if (off is null || on is null)
            return;
        if (noneAt is not null)
        {
            (int changed, string firstChanged) = ChangedPixels(alone, on, off, "the markers off");
            _pixels += alone.Count;
            if (changed > 0)
                Fail($"{what}: {changed} of its {alone.Count} pixel(s) changed with the markers on, first at {firstChanged}");
            if (alone.Count < 20)
                Fail($"{what}: only {alone.Count} pixels to compare (at least 20 expected)");
            GD.Print($"Level check: {what}: none of its {alone.Count} pixels changed");
            return;
        }

        IsoDoom.Wad.Graphics.IndexedImage tex = ShownComposite(m, Textures.R_CheckTextureNumForName(SwitchMarkers.TextureName(s)));
        Vector3 eye = Cutaway.ToMapAxes(camera.GlobalTransform.Origin) * LevelMesh.MapUnitsPerMetre;
        int light = sector.LightLevel + (cut ? 16 * Cutaway.CapLightOffset(settings.Cap) : 0);
        int w = size.X, drawn = 0, hidden = 0, bad = 0;
        string first = "";
        foreach (Vector2I q in alone)
        {
            Vector3 p = PlanePoint(new Vector2(q.X + 0.5f, q.Y + 0.5f), z);
            double column = s.TextureOffset / 65536.0 + k.Column(p.X, p.Y), row = k.Row(p.X, p.Y);
            if (Math.Abs(column - Math.Round(column)) < 1.0 / 64 || Math.Abs(row - Math.Round(row)) < 1.0 / 64)
                continue;
            int colormap = ExpectedColormap(m, false, light, 0, p.X, p.Y, (eye - p).Dot(toCamera));
            if (colormap < 0)
                continue;
            (int tc, int tr) = TextureWrap.WallTexel((int)Math.Floor(column), (int)Math.Floor(row), tex.Width, tex.Height, WallTextureTiling.Vanilla);
            (int R, int G, int B) expected = Shade(tex[tc, tr], colormap);
            int i = (q.Y * w + q.X) * 4;
            (int R, int G, int B) got = (on[i], on[i + 1], on[i + 2]);
            if (got == expected)
                drawn++;
            else if (got == (off[i], off[i + 1], off[i + 2]))
                hidden++;
            else if (bad++ == 0)
                first = $"pixel ({q.X}, {q.Y}), map ({p.X:F1}, {p.Y:F1}), column {column:F2}, row {row:F2}: drew {got}, expected {expected}";
        }
        _pixels += drawn + hidden + bad;
        if (bad > 0)
            Fail($"{what}: {bad} of {drawn + hidden + bad} pixel(s) differ, first at {first}");
        if (drawn < 50)
            Fail($"{what}: only {drawn} pixels drew it ({hidden} hidden; at least 50 expected)");
        GD.Print($"Level check: {what}: {drawn} pixels against the texture ({SwitchMarkers.TextureName(s)}), {hidden} hidden");
    }
}
