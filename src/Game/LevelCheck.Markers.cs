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
/// </summary>
public partial class LevelCheck
{
    private int _markerMaps, _markerSides, _markersShown;

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
        _markerMaps++;
        _markerSides += sides.Count;
        _markersShown += shown.Count;
        if (sides.Count > 0)
            GD.Print($"Level check: {map}: {sides.Count} switch sides with markers, {shown.Count} facing away from the game camera (shown: lines {string.Join(" ", shown)})");
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
        List<SwitchMarker> largest = [.. m.Markers.Markers.OrderByDescending(k => k.Pieces.Sum(Area))];
        int changed = 0;
        SwitchMarker? shown = null;
        foreach (SwitchMarker k in largest.Where(k => SwitchMarkers.Shows(k, flat.X, flat.Y)).Take(10))
        {
            changed = await CompareMarkerFromGameCamera(m, k, basis, toCamera, true);
            if (changed >= 50)
            {
                shown = k;
                break;
            }
        }
        bool uncovered = false;
        if (shown is null)
        {
            // T10.5b: a marker can be hidden whole by what stands between it and the camera (Doom II
            // MAP01's only one, line 75's, a switch on the riser of a 64-unit block that hides it from
            // the camera's side): then it must draw once the cutaway cuts all of that away, to just
            // above the marker, with no cap.
            foreach (SwitchMarker k in largest.Where(k => SwitchMarkers.Shows(k, flat.X, flat.Y)).Take(10))
            {
                double cx = k.Pieces.Average(p => p.Average(v => v.X)), cy = k.Pieces.Average(p => p.Average(v => v.Y));
                m.SetCutaway(new CutawaySettings { Style = CutawayStyle.Cut, Cap = CutawayCap.Off, Doors = CutawayDoors.Cut, Height = 2 * SwitchMarkers.Lift });
                m.SetCutawayCentres(new Vector3((float)cx, (float)cy, k.Section.FrontSector.FloorHeight / 65536f), null);
                changed = await CompareMarkerFromGameCamera(m, k, basis, toCamera, true);
                m.SetCutawayCentres(null, null);
                m.SetCutaway(new CutawaySettings { Style = CutawayStyle.Off });
                if (changed >= 50)
                {
                    (shown, uncovered) = (k, true);
                    break;
                }
            }
        }
        if (shown is null && largest.Any(k => SwitchMarkers.Shows(k, flat.X, flat.Y)))
            Fail($"{map}: no switch marker of a wall facing away from the game camera drew 50 pixels (of the ten largest), also with what is in front of them cut away");
        SwitchMarker? facing = largest.FirstOrDefault(k => !SwitchMarkers.Shows(k, flat.X, flat.Y));
        int facingPixels = facing is null ? 0 : await CompareMarkerFromGameCamera(m, facing, basis, toCamera, false);
        string away = shown is null ? "none facing away"
            : $"line {shown.Section.Line.Index}'s (facing away) changed {changed} pixels{(uncovered ? " (hidden whole by what stands in front of it: drawn with that cut away to just above it)" : "")}";
        string towards = facing is null ? "none facing the camera" : $"line {facing.Section.Line.Index}'s (facing the camera) none of the {facingPixels} it alone covers";
        GD.Print($"Level check: {map}: switch markers from the game camera: {away}; {towards}");
    }

    // The pixels where only this marker would show, from the game camera centred on it: with
    // drawnExpected false they must not change with the markers on (returns how many there
    // are); with true, returns how many changed.
    private async Task<int> CompareMarkerFromGameCamera(LevelMesh m, SwitchMarker marker, Basis basis, Vector3 toCamera, bool drawnExpected)
    {
        float Z(SwitchMarker k) => k.Section.FrontSector.FloorHeight / 65536f + SwitchMarkers.Lift;
        double cx = marker.Pieces.Average(p => p.Average(v => v.X)), cy = marker.Pieces.Average(p => p.Average(v => v.Y));
        CutView(new Vector3((float)cx, (float)cy, Z(marker) - Cutaway.Anchor), basis, toCamera);
        Camera3D camera = _scene.Camera;
        List<(double X, double Y)[]> Project(SwitchMarker k) =>
            [.. k.Pieces.Select(p => p.Select(v => camera.UnprojectPosition(new Vector3((float)v.X, Z(k), (float)-v.Y) / LevelMesh.MapUnitsPerMetre))
                .Select(q => ((double)q.X, (double)q.Y)).ToArray())];
        List<(double X, double Y)[]> own = Project(marker);
        List<(double X, double Y)[]> others = [.. m.Markers.Markers.Where(k => k != marker).SelectMany(Project)];
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
}
