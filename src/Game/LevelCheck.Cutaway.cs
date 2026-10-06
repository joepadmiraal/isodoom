using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Map;
using IsoDoom.Render;

namespace IsoDoom.Game;

/// <summary>
/// The level check's cutaway part (T3.4, real renderer only): one view of the
/// rendered map through the game camera's orthographic angle, at 1 map unit
/// per pixel, with a cut centre just behind a tall one-sided wall facing the
/// camera (it must be cut), then just in front of it (it must stay whole). Each pixel in the cut disc is classified on the CPU by casting its
/// ray against every wall piece and floor (<see cref="Cutaway.Hides"/> for
/// each surface it meets, with a margin around every edge and every boundary
/// of the cut): pixels whose first surface is not cut must draw exactly as
/// with the cutaway off (walls outside the cut region, below the cutoff, or
/// in front of the centre, and every pixel outside the disc), and pixels where
/// every surface on the ray is cut must show the background (the wall draws
/// nothing above the cutoff there). The same view with the cursor ground point
/// as the only centre must equal it, and the dither must keep the uncut pixels
/// and clear about half of the cut ones.
/// <para>
/// The cap (T3.4a): a second view puts the centre just behind a raised floor
/// (a lower wall facing away from the camera, its back sector more than the
/// cutoff above). With the cap off, the cut floor's pixels show the
/// background; with it on, the pixels whose ray meets the cap plane (the
/// cutoff over the centre's floor) inside the raised sector's floor, past
/// surfaces that are all cut, show that sector's flat at that point, lit as
/// its floor (flat) or <see cref="Cutaway.CapDarken"/> levels darker (dark);
/// the rest as before. Both views compare their capped pixels, and the cursor
/// centre must give the same capped frame.
/// </para>
/// </summary>
public partial class LevelCheck
{
    /// <summary>Map units (= pixels here) kept clear of surface edges and of the cut's boundaries when classifying a pixel.</summary>
    private const float CutMargin = 1.5f;

    /// <summary>Flat texel coordinates this close (map units) to a texel edge are not compared on a cap.</summary>
    private const double CapTexelMargin = 0.05;

    private enum CutPixel : byte { Skip, Keep, Cleared, Capped }

    /// <summary>Where a capped pixel's ray meets the cap: the raised sector and the map point.</summary>
    private readonly record struct CapPoint(int Sector, double X, double Y, double Depth);

    /// <summary>
    /// A view's pixel classes over the disc's screen box, with the cap off
    /// (<see cref="Classes"/>) and on (<see cref="CapClasses"/>, where
    /// <see cref="CutPixel.Capped"/> pixels have their <see cref="CapPoints"/>);
    /// the cleared pixels (and those of them where a floor is cut); the kept
    /// pixels in the disc whose first surface is a wall, and those of them
    /// above the cutoff; the capped pixels.
    /// </summary>
    private sealed record CutClasses(CutPixel[] Classes, CutPixel[] CapClasses, CapPoint[] CapPoints, (int Left, int Top, int Right, int Bottom) Box,
        int Cleared, int KeptWalls, int KeptAbove, int ClearedFloors, int Capped);

    private readonly record struct CutQuad(Vector2 A, Vector2 Dir, float Length, Vector3 Normal, float Bottom, float Top, bool Masked, WallSection Section);

    private async Task CutawayCheck(LevelMesh m)
    {
        string map = m.Level.Name;
        var settings = new CutawaySettings { Style = CutawayStyle.Cut, Cap = CutawayCap.Off };
        Basis basis = Basis.FromEuler(new Vector3(-Mathf.DegToRad(IsoCamera.DefaultPitch), Mathf.DegToRad(IsoCamera.Yaw), 0));
        Vector3 toCamera = Cutaway.ToMapAxes(basis.Z).Normalized();
        var toCameraFlat = new Vector2(toCamera.X, toCamera.Y).Normalized();

        var quads = new List<CutQuad>();
        foreach (WallSection s in m.Walls.Sections)
        {
            if (!LevelMesh.IsDrawn(s))
                continue;
            (int sb, int st) = s.Span();
            if (st <= sb)
                continue;
            foreach (WallPiece pc in m.Pieces.Of(s.Line, s.Side))
            {
                var a = new Vector2(pc.A.X / 65536f, pc.A.Y / 65536f);
                var b = new Vector2(pc.B.X / 65536f, pc.B.Y / 65536f);
                float len = a.DistanceTo(b);
                if (len < 1e-3f)
                    continue;
                Vector2 dir = (b - a) / len;
                quads.Add(new CutQuad(a, dir, len, new Vector3(dir.Y, -dir.X, 0), sb / 65536f, st / 65536f, LevelMesh.IsMasked(s), s));
                // T3.1a: a one-sided masked middle's back face, seen from the other side.
                if (m.HasBackFace(s) && m.MaskedBacks == MaskedBackFaces.Mirrored)
                    quads.Add(new CutQuad(b, -dir, len, new Vector3(-dir.Y, dir.X, 0), sb / 65536f, st / 65536f, true, s));
            }
        }
        var heights = new SortedSet<int>();
        foreach (Sector s in m.Level.Sectors)
            heights.Add(s.FloorHeight);
        int[] floorHeights = new int[heights.Count];
        heights.CopyTo(floorHeights);
        Array.Reverse(floorHeights);

        // Candidates: tall one-sided walls facing the camera; the centre 24 units behind the middle (the
        // wall must be cut there) and 24 units in front of it (the wall must stay whole).
        Vector2I size = ViewSize();
        int w = size.X, h = size.Y;
        CutClasses? behind = null, inFront = null;
        Vector3 behindCentre = default, frontCentre = default;
        string? chosen = null;
        int tried = 0;
        foreach (WallSection s in m.Walls.Sections)
        {
            if (!LevelMesh.IsDrawn(s) || s.Kind != WallSectionKind.Middle || s.BackSector is not null)
                continue;
            (int sb, int st) = s.Span();
            var v1 = new Vector2(s.V1.X / 65536f, s.V1.Y / 65536f);
            var v2 = new Vector2(s.V2.X / 65536f, s.V2.Y / 65536f);
            float len = v1.DistanceTo(v2);
            if ((st - sb) / 65536f < settings.Height + 48 || len < 64)
                continue;
            Vector2 dir = (v2 - v1) / len, n = new(dir.Y, -dir.X);
            if (n.Dot(toCameraFlat) < 0.5f)
                continue;
            if (tried++ >= 40)
                break;
            Vector2 mid = (v1 + v2) / 2;
            float floor = s.FrontSector.FloorHeight / 65536f;
            var back = new Vector3(mid.X - n.X * 24, mid.Y - n.Y * 24, floor);
            var front = new Vector3(mid.X + n.X * 24, mid.Y + n.Y * 24, floor);
            CutView(back, basis, toCamera);
            CutClasses b = ClassifyCut(m, quads, floorHeights, back, settings, toCamera, w, h);
            if (b.Cleared < 100 || b.KeptWalls < 50)
                continue;
            CutView(front, basis, toCamera);
            CutClasses f = ClassifyCut(m, quads, floorHeights, front, settings, toCamera, w, h);
            if (f.KeptAbove < 100)
                continue;
            (behind, inFront, behindCentre, frontCentre) = (b, f, back, front);
            chosen = $"line {s.Line.Index} ({Textures.TextureDefs[s.Texture].Name})";
            break;
        }
        if (behind is null || inFront is null || chosen is null)
        {
            Fail($"{map}: cutaway: no view found (a one-sided wall facing the game camera, {settings.Height + 48} units tall and 64 long, "
                + "with at least 100 pixels cleared and 50 wall pixels kept in the cut disc from just behind it, and 100 wall pixels above the cutoff kept from just in front)");
            return;
        }
        await CompareCutView(m, behindCentre, behind, settings, basis, toCamera, $"{map}: cutaway, centre ({behindCentre.X:F0}, {behindCentre.Y:F0}, {behindCentre.Z:F0}) behind {chosen}");
        await CompareCutView(m, frontCentre, inFront, settings, basis, toCamera, $"{map}: cutaway, centre ({frontCentre.X:F0}, {frontCentre.Y:F0}, {frontCentre.Z:F0}) in front of {chosen}");

        // T3.4a: a centre just behind a raised floor (seen from the camera), whose floor is cut and capped.
        int raised = 0;
        foreach (WallSection s in m.Walls.Sections)
        {
            if (!LevelMesh.IsDrawn(s) || s.Kind != WallSectionKind.Lower || s.BackSector is not Sector high)
                continue;
            float floor = s.FrontSector.FloorHeight / 65536f, step = high.FloorHeight / 65536f - floor;
            var v1 = new Vector2(s.V1.X / 65536f, s.V1.Y / 65536f);
            var v2 = new Vector2(s.V2.X / 65536f, s.V2.Y / 65536f);
            float len = v1.DistanceTo(v2);
            if (step < settings.Height + 16 || len < 32)
                continue;
            Vector2 dir = (v2 - v1) / len, n = new(dir.Y, -dir.X);
            if (n.Dot(toCameraFlat) > -0.5f)
                continue; // the low side must be away from the camera, the raised floor in front of the centre
            Vector2 mid = (v1 + v2) / 2;
            var centre = new Vector3(mid.X + n.X * 24, mid.Y + n.Y * 24, floor);
            if (CursorGround.DrawnSectorAt(m, (int)(centre.X * 65536), (int)(centre.Y * 65536)) != s.FrontSector.Index)
                continue;
            if (raised++ >= 40)
                break;
            CutView(centre, basis, toCamera);
            CutClasses c = ClassifyCut(m, quads, floorHeights, centre, settings, toCamera, w, h);
            if (c.ClearedFloors < 50 || c.Capped < 50)
                continue;
            await CompareCutView(m, centre, c, settings, basis, toCamera,
                $"{map}: cutaway cap, centre ({centre.X:F0}, {centre.Y:F0}, {centre.Z:F0}) behind the raised floor of sector {high.Index} (line {s.Line.Index})");
            return;
        }
        if (raised > 0)
            Fail($"{map}: cutaway cap: no view found (a centre 24 units behind a raised floor's lower wall facing away from the game camera, "
                + "with at least 50 pixels cleared through the floor and 50 capped)");
        else
            GD.Print($"Level check: {map}: cutaway cap: no raised floor (over {settings.Height + 16} units, a lower wall facing away from the game camera) to cut");
    }

    /// <summary>
    /// Renders the view of <paramref name="centre"/> with the cutaway off, cut,
    /// cut around the cursor centre instead, and dithered, and compares them by
    /// the CPU classes.
    /// </summary>
    private async Task CompareCutView(LevelMesh m, Vector3 centre, CutClasses classes, CutawaySettings settings, Basis basis, Vector3 toCamera, string what)
    {
        Vector2I size = ViewSize();
        int w = size.X, h = size.Y;
        CutView(centre, basis, toCamera);
        m.SetCutawayCentres(centre, null);
        m.SetCutaway(settings with { Style = CutawayStyle.Off });
        byte[]? off = await Capture($"{what}, off");
        m.SetCutaway(settings);
        byte[]? cut = await Capture($"{what}, cut");
        m.SetCutawayCentres(null, centre);
        byte[]? cursor = await Capture($"{what}, cursor centre");
        m.SetCutawayCentres(centre, null);
        m.SetCutaway(settings with { Style = CutawayStyle.Dither });
        byte[]? dither = await Capture($"{what}, dither");
        // T3.4a: the cap, dark (around the player and then the cursor ground point) and flat.
        m.SetCutaway(settings with { Cap = CutawayCap.Dark });
        byte[]? dark = await Capture($"{what}, dark cap");
        m.SetCutawayCentres(null, centre);
        byte[]? darkCursor = await Capture($"{what}, dark cap, cursor centre");
        m.SetCutawayCentres(centre, null);
        m.SetCutaway(settings with { Cap = CutawayCap.Flat });
        byte[]? flat = await Capture($"{what}, flat cap");
        m.SetCutaway(new CutawaySettings { Style = CutawayStyle.Off });
        m.SetCutawayCentres(null, null);
        if (off is null || cut is null || cursor is null || dither is null || dark is null || darkCursor is null || flat is null)
            return;

        var box = classes.Box;
        int bw = box.Right - box.Left;
        long compared = 0;
        int badKeep = 0, badClear = 0, badCursor = 0, badDither = 0, ditherCleared = 0, offShowsBackground = 0;
        string firstKeep = "", firstClear = "";
        for (int py = 0; py < h; py++)
        {
            for (int px = 0; px < w; px++)
            {
                int p = (py * w + px) * 4;
                bool inBox = px >= box.Left && px < box.Right && py >= box.Top && py < box.Bottom;
                CutPixel cls = inBox ? classes.Classes[(py - box.Top) * bw + (px - box.Left)] : CutPixel.Keep;
                (int R, int G, int B) got = (cut[p], cut[p + 1], cut[p + 2]);
                (int R, int G, int B) before = (off[p], off[p + 1], off[p + 2]);
                if (cursor[p] != cut[p] || cursor[p + 1] != cut[p + 1] || cursor[p + 2] != cut[p + 2])
                    badCursor++;
                if (cls == CutPixel.Keep)
                {
                    compared++;
                    if (got != before && badKeep++ == 0)
                        firstKeep = $"pixel ({px}, {py}): drew {got}, {before} with the cutaway off";
                    if (dither[p] != off[p] || dither[p + 1] != off[p + 1] || dither[p + 2] != off[p + 2])
                        badDither++;
                }
                else if (cls == CutPixel.Cleared)
                {
                    compared++;
                    if (NearBackground(before))
                        offShowsBackground++;
                    if (!NearBackground(got) && badClear++ == 0)
                        firstClear = $"pixel ({px}, {py}): drew {got}, expected the background {_background}";
                    if (NearBackground((dither[p], dither[p + 1], dither[p + 2])))
                        ditherCleared++;
                }
            }
        }
        _pixels += compared;
        if (badKeep > 0)
            Fail($"{what}: {badKeep} pixel(s) whose first surface is not cut changed with the cutaway, first at {firstKeep}");
        if (badClear > 0)
            Fail($"{what}: {badClear} of {classes.Cleared} cut pixel(s) still draw a surface, first at {firstClear}");
        if (offShowsBackground > 0)
            Fail($"{what}: {offShowsBackground} pixel(s) classified as cut show the background with the cutaway off (no surface there)");
        if (badCursor > 0)
            Fail($"{what}: {badCursor} pixel(s) differ with the cursor ground point as the cut centre instead of the player");
        if (badDither > 0)
            Fail($"{what}: {badDither} uncut pixel(s) changed with the dither");
        double share = classes.Cleared == 0 ? 0.5 : (double)ditherCleared / classes.Cleared;
        if (share < 0.3 || share > 0.7)
            Fail($"{what}: the dither cleared {share:P0} of the cut pixels, expected about half");
        GD.Print($"Level check: {what}: {compared} pixels compared ({classes.Cleared} cut to the background, {classes.ClearedFloors} of them through a floor; uncut wall pixels in the disc: "
            + $"{classes.KeptWalls}, {classes.KeptAbove} of them above the cutoff; the rest as with the cutaway off); cursor centre identical"
            + (classes.Cleared > 0 ? $"; dither cleared {share:P0} of the cut pixels" : ""));

        foreach ((CutawayCap cap, byte[] frame) in new[] { (CutawayCap.Dark, dark), (CutawayCap.Flat, flat) })
            CompareCap(m, classes, cap, off, frame, $"{what}, {cap.ToString().ToLowerInvariant()} cap");
        int badCapCursor = 0;
        for (int i = 0; i < dark.Length; i++)
        {
            if (dark[i] != darkCursor[i])
                badCapCursor++;
        }
        if (badCapCursor > 0)
            Fail($"{what}: {badCapCursor} byte(s) differ with the cap around the cursor ground point instead of the player");
    }

    /// <summary>
    /// A capped frame against the classes with the cap on: uncut pixels as
    /// with the cutaway off, cleared ones the background, capped ones the
    /// raised sector's flat at the cap point, lit as its floor (with the
    /// cap's light offset).
    /// </summary>
    private void CompareCap(LevelMesh m, CutClasses classes, CutawayCap cap, byte[] off, byte[] frame, string what)
    {
        Vector2I size = ViewSize();
        int w = size.X, h = size.Y;
        var box = classes.Box;
        int bw = box.Right - box.Left;
        long compared = 0;
        int badKeep = 0, badClear = 0, badCap = 0, capped = 0;
        string firstKeep = "", firstClear = "", firstCap = "";
        for (int py = 0; py < h; py++)
        {
            for (int px = 0; px < w; px++)
            {
                int p = (py * w + px) * 4;
                bool inBox = px >= box.Left && px < box.Right && py >= box.Top && py < box.Bottom;
                int i = inBox ? (py - box.Top) * bw + (px - box.Left) : -1;
                CutPixel cls = i >= 0 ? classes.CapClasses[i] : CutPixel.Keep;
                (int R, int G, int B) got = (frame[p], frame[p + 1], frame[p + 2]);
                switch (cls)
                {
                    case CutPixel.Keep:
                        compared++;
                        if (got != (off[p], off[p + 1], off[p + 2]) && badKeep++ == 0)
                            firstKeep = $"pixel ({px}, {py}): drew {got}, {(off[p], off[p + 1], off[p + 2])} with the cutaway off";
                        break;
                    case CutPixel.Cleared:
                        compared++;
                        if (!NearBackground(got) && badClear++ == 0)
                            firstClear = $"pixel ({px}, {py}): drew {got}, expected the background {_background}";
                        break;
                    case CutPixel.Capped:
                        CapPoint c = classes.CapPoints[i];
                        Sector sector = m.Level.Sectors[c.Sector];
                        int colormap = ExpectedColormap(m, false, sector.LightLevel + 16 * Cutaway.CapLightOffset(cap), 0, c.X, c.Y, c.Depth);
                        if (colormap < 0)
                            break;
                        (int col, int row) = TextureWrap.FlatTexel((int)Math.Floor(c.X * 65536), (int)Math.Floor(c.Y * 65536));
                        (int R, int G, int B) expected = Shade(FlatImage(sector.FloorPic)[col, row], colormap);
                        compared++;
                        capped++;
                        if (got != expected && badCap++ == 0)
                            firstCap = $"pixel ({px}, {py}), map ({c.X:F2}, {c.Y:F2}) in sector {c.Sector}: drew {got}, expected {expected}";
                        break;
                }
            }
        }
        _pixels += compared;
        if (badKeep > 0)
            Fail($"{what}: {badKeep} pixel(s) whose first surface is not cut changed with the cutaway, first at {firstKeep}");
        if (badClear > 0)
            Fail($"{what}: {badClear} pixel(s) cut through to nothing still draw a surface, first at {firstClear}");
        if (badCap > 0)
            Fail($"{what}: {badCap} of {capped} capped pixel(s) differ, first at {firstCap}");
        GD.Print($"Level check: {what}: {compared} pixels compared, {capped} of them on the cap");
    }

    /// <summary>The scene camera looking along the game camera's direction at the cut centre's anchor, 1 map unit per pixel.</summary>
    private void CutView(Vector3 centre, Basis basis, Vector3 toCamera)
    {
        const float back = 4096;
        Vector3 anchor = centre + new Vector3(0, 0, Cutaway.Anchor);
        Ortho(basis, anchor + toCamera * back, 1, 2 * back);
    }

    /// <summary>
    /// Classifies the pixels of the cut disc's screen box (the rest is
    /// <see cref="CutPixel.Keep"/>).
    /// </summary>
    private CutClasses ClassifyCut(
        LevelMesh m, List<CutQuad> quads, int[] floorHeights, Vector3 centre, CutawaySettings settings, Vector3 toCamera, int w, int h)
    {
        Camera3D cam = _scene.Camera;
        Vector3 anchor = centre + new Vector3(0, 0, Cutaway.Anchor);
        Vector2 a = cam.UnprojectPosition(LevelMesh.ToGodot((int)(anchor.X * 65536), (int)(anchor.Y * 65536), anchor.Z));
        int reach = (int)Math.Ceiling(settings.Radius + 4);
        int left = Math.Max(0, (int)a.X - reach), right = Math.Min(w, (int)a.X + reach + 1);
        int top = Math.Max(0, (int)a.Y - reach), bottom = Math.Min(h, (int)a.Y + reach + 1);
        int bw = Math.Max(0, right - left), bh = Math.Max(0, bottom - top);
        var classes = new CutPixel[bw * bh];
        var capClasses = new CutPixel[bw * bh];
        var capPoints = new CapPoint[bw * bh];
        int cleared = 0, keptWalls = 0, keptAbove = 0, clearedFloors = 0, capped = 0;
        float capZ = centre.Z + settings.Height;
        Vector3 camera = Cutaway.ToMapAxes(cam.GlobalPosition) * LevelMesh.MapUnitsPerMetre;
        var hits = new List<(float T, bool Definite, int Cut, bool Wall, float Z)>();
        for (int py = top; py < bottom; py++)
        {
            for (int px = left; px < right; px++)
            {
                var screen = new Vector2(px + 0.5f, py + 0.5f);
                Vector3 o = Cutaway.ToMapAxes(cam.ProjectRayOrigin(screen)) * LevelMesh.MapUnitsPerMetre;
                Vector3 d = Cutaway.ToMapAxes(cam.ProjectRayNormal(screen)).Normalized();
                hits.Clear();
                int capState = 0; // T3.4a: 0 the ray meets no cap, 1 near a cap's edge, 2 a cap at capT
                float capT = 0;
                CapPoint capPoint = default;
                foreach (CutQuad q in quads)
                {
                    float denom = d.X * q.Normal.X + d.Y * q.Normal.Y;
                    if (denom >= -1e-6f)
                        continue; // seen from behind: culled
                    float t = ((q.A.X - o.X) * q.Normal.X + (q.A.Y - o.Y) * q.Normal.Y) / denom;
                    if (t <= 0)
                        continue;
                    Vector3 p = o + d * t;
                    float along = (p.X - q.A.X) * q.Dir.X + (p.Y - q.A.Y) * q.Dir.Y;
                    float margin = Math.Min(Math.Min(along, q.Length - along), Math.Min(p.Z - q.Bottom, q.Top - p.Z));
                    if (margin < -CutMargin)
                        continue;
                    hits.Add((t, margin > CutMargin && !q.Masked, CutState(p, q.Normal, centre, settings, toCamera), true, p.Z));
                }
                if (d.Z < 0)
                {
                    foreach (int fh in floorHeights)
                    {
                        float height = fh / 65536f;
                        float t = (height - o.Z) / d.Z;
                        if (t <= 0)
                            continue;
                        Vector3 p = o + d * t;
                        int matches = 0;
                        foreach ((float ox, float oy) in new[] { (0f, 0f), (CutMargin, 0f), (-CutMargin, 0f), (0f, CutMargin), (0f, -CutMargin) })
                        {
                            int s = CursorGround.DrawnSectorAt(m, (int)Math.Round((p.X + ox) * 65536.0), (int)Math.Round((p.Y + oy) * 65536.0));
                            if (s >= 0 && m.Level.Sectors[s].FloorHeight == fh)
                                matches++;
                        }
                        if (matches > 0)
                            hits.Add((t, matches == 5, CutState(p, new Vector3(0, 0, 1), centre, settings, toCamera), false, p.Z));
                    }

                    // T3.4a: the cap plane, inside a floor above it (all five points in one such sector) and the disc.
                    float tc = (capZ - o.Z) / d.Z;
                    if (tc > 0)
                    {
                        Vector3 q = o + d * tc;
                        int raised = 0, sector = -2;
                        foreach ((float ox, float oy) in new[] { (0f, 0f), (CutMargin, 0f), (-CutMargin, 0f), (0f, CutMargin), (0f, -CutMargin) })
                        {
                            int s = CursorGround.DrawnSectorAt(m, (int)Math.Round((q.X + ox) * 65536.0), (int)Math.Round((q.Y + oy) * 65536.0));
                            if (s >= 0 && m.Level.Sectors[s].FloorHeight / 65536f > capZ)
                            {
                                raised++;
                                sector = sector == -2 || sector == s ? s : -1;
                            }
                        }
                        float inside = settings.Radius - Cutaway.Distance(q, anchor, toCamera);
                        if (raised > 0 && inside > -CutMargin)
                        {
                            double fx = q.X - Math.Floor(q.X), fy = -q.Y - Math.Floor(-q.Y);
                            bool texel = fx > CapTexelMargin && fx < 1 - CapTexelMargin && fy > CapTexelMargin && fy < 1 - CapTexelMargin;
                            capState = raised == 5 && sector >= 0 && inside > CutMargin && texel ? 2 : 1;
                            capT = tc;
                            capPoint = new CapPoint(sector, q.X, q.Y, (q - camera).Dot(-toCamera));
                        }
                    }
                }
                hits.Sort((x, y) => x.T.CompareTo(y.T));
                int idx = (py - top) * bw + (px - left);

                // Keep: every surface up to the first definite one is definitely not cut.
                bool keep = true;
                bool firstIsWall = false, firstAbove = false;
                foreach (var hit in hits)
                {
                    if (hit.Cut != 0)
                    {
                        keep = false;
                        break;
                    }
                    if (hit.Definite)
                    {
                        firstIsWall = hit.Wall;
                        firstAbove = hit.Z > centre.Z + settings.Height + CutMargin;
                        break;
                    }
                }
                // Cleared: something is on the ray, and every surface on it is definitely cut.
                bool clear = hits.Count > 0 && hits.TrueForAll(x => x.Cut == 1) && hits.Exists(x => x.Definite);
                CutPixel cls = keep ? CutPixel.Keep : clear ? CutPixel.Cleared : CutPixel.Skip;
                classes[idx] = cls;

                // With the cap on: capped when every surface before the cap is definitely cut; kept when
                // the first definite surface comes before it and is kept; as with the cap off without one.
                CutPixel capClass = cls;
                if (capState > 0)
                {
                    int first = hits.FindIndex(x => x.Cut != 1);
                    if (first < 0 || hits[first].T > capT)
                        capClass = capState == 2 ? CutPixel.Capped : CutPixel.Skip;
                    else
                    {
                        int definite = hits.FindIndex(x => x.Definite);
                        capClass = keep && definite >= 0 && hits[definite].T < capT ? CutPixel.Keep : CutPixel.Skip;
                    }
                }
                capClasses[idx] = capClass;
                if (capClass == CutPixel.Capped)
                {
                    capPoints[idx] = capPoint;
                    capped++;
                }
                if (cls == CutPixel.Cleared)
                {
                    cleared++;
                    if (hits.Exists(x => !x.Wall))
                        clearedFloors++;
                }
                bool inDisc = Cutaway.Distance(o, anchor, toCamera) < settings.Radius; // the ray runs along the axis
                if (cls == CutPixel.Keep && firstIsWall && inDisc)
                {
                    keptWalls++;
                    if (firstAbove)
                        keptAbove++;
                }
            }
        }
        return new CutClasses(classes, capClasses, capPoints, (left, top, right, bottom), cleared, keptWalls, keptAbove, clearedFloors, capped);
    }

    /// <summary>Whether <see cref="Cutaway.Hides"/> cuts the surface point: 0 definitely not, 1 definitely, 2 within <see cref="CutMargin"/> of a boundary of the rule.</summary>
    private static int CutState(Vector3 p, Vector3 normal, Vector3 centre, CutawaySettings settings, Vector3 toCamera)
    {
        if (normal.Dot(toCamera) < 0)
            normal = -normal;
        Vector3 a = centre + new Vector3(0, 0, Cutaway.Anchor);
        float above = p.Z - (centre.Z + settings.Height);
        float behind = -((a - p).Dot(normal) + Cutaway.PlaneMargin); // > 0: the centre is behind the plane by more than the margin
        float inside = settings.Radius - Cutaway.Distance(p, a, toCamera);
        if (above < -CutMargin || behind < -CutMargin || inside < -CutMargin)
            return 0;
        if (above > CutMargin && behind > CutMargin && inside > CutMargin)
        {
            if (!Cutaway.Hides(p, normal, centre, settings, toCamera))
                throw new InvalidOperationException("Cutaway.Hides disagrees with the check's margins");
            return 1;
        }
        return 2;
    }

    /// <summary>The tallest patch (rows above the origin) the thing cutaway view takes: the shader moves a full-tilt vertex at most 256 units back onto the upright plane (T3.6).</summary>
    private const int ThingCutMaxTop = 140;

    /// <summary>
    /// The things the cutaway cuts (T3.4b, real renderer only): the tallest
    /// thing standing on its floor (at most <see cref="ThingCutMaxTop"/> rows
    /// above its origin) alone, the level hidden, full tilt with upright depth,
    /// seen along the game camera's direction at 1 unit per pixel, with a cut
    /// disc of radius 24 (so its edge crosses the billboard; the default 80 in
    /// front, so only the plane test keeps it whole) and a cut
    /// centre 24 units behind it (on its floor, then 64 below it: a thing on a
    /// raised floor in front of the player) and 24 units in front of it. Each
    /// drawn pixel of the frame with the cutaway off is classified on the CPU by
    /// <see cref="Cutaway.HidesThing"/> at its ray's point on the upright quad
    /// (with <see cref="CutMargin"/> around the cutoff and the disc's edge): cut
    /// pixels must show the background, the rest draw as with the cutaway off.
    /// As a decoration it is cut, as an actor only with
    /// <see cref="CutawayThings.All"/>, never with things off; the dither keeps
    /// the uncut pixels and clears about half of the cut ones; the cursor centre
    /// gives the same frame; from the centre in front nothing is cut.
    /// </summary>
    private async Task CheckThingCutaway(LevelMesh m, ThingSprites things, SpriteAtlas atlas)
    {
        string map = m.Level.Name;
        Basis basis = GameBasis(IsoCamera.DefaultPitch);
        Vector3 toCamera = Cutaway.ToMapAxes(basis.Z).Normalized();
        var ground = new Vector2(toCamera.X, toCamera.Y).Normalized();
        var facing = new Vector3(ground.X, ground.Y, 0);
        things.UpdateRotations(true, -basis.Z, Vector3.Zero);

        int thing = -1, tallest = 0;
        for (int i = 0; i < things.Entries.Count; i++)
        {
            ThingSprites.Entry e = things.Entries[i];
            ThingSprites.Shown sh = things.ShownFrames[i];
            if (sh.Slot < 0 || e.MapPosition.Z != m.Level.Sectors[e.Sector].FloorHeight / 65536f)
                continue;
            int top = atlas.Images[sh.Slot].TopOffset;
            if (top > tallest && top <= ThingCutMaxTop)
            {
                tallest = top;
                thing = i;
            }
        }
        if (thing < 0)
        {
            Fail($"{map}: thing cutaway: no thing on a floor at most {ThingCutMaxTop} rows tall");
            return;
        }
        ThingSprites.Entry original = things.Entries[thing];
        Vector3 foot = original.MapPosition;
        SpriteSettings sprites = m.Sprites;
        foreach (MeshInstance3D? chunk in _scene.Chunks)
        {
            if (chunk is not null)
                chunk.Visible = false;
        }
        things.Isolate(thing);
        m.SetSprites(sprites with { Tilt = 1, TiltDepth = SpriteTiltDepth.Upright });
        // Behind it a radius of 24, so the disc's edge crosses the billboard (an imp reaches about 35 units from the anchor
        // on screen); in front of it the default, so the disc covers the billboard and only the plane test keeps it.
        var narrow = new CutawaySettings { Style = CutawayStyle.Cut, Cap = CutawayCap.Off, Things = CutawayThings.Decorations, Radius = 24 };
        string name = $"{map}: thing cutaway, thing {thing} ({SpriteName(original)}, {tallest} rows) at ({foot.X:F0}, {foot.Y:F0}, {foot.Z:F0})";

        var behind = new Vector3(foot.X - ground.X * 24, foot.Y - ground.Y * 24, foot.Z);
        var below = behind with { Z = foot.Z - 64 };
        var inFront = new Vector3(foot.X + ground.X * 24, foot.Y + ground.Y * 24, foot.Z);
        foreach ((Vector3 centre, string where, CutawaySettings decor) in new[]
            { (behind, "centre 24 behind", narrow), (below, "centre 24 behind and 64 below", narrow), (inFront, "centre 24 in front", narrow with { Radius = CutawaySettings.DefaultRadius }) })
        {
            string what = $"{name}, {where}";
            CutView(centre, basis, toCamera);
            m.SetCutawayCentres(centre, null);
            things.SetEntry(thing, original with { Actor = false });
            m.SetCutaway(decor with { Style = CutawayStyle.Off });
            byte[]? off = await Capture($"{what}, off");
            if (off is null)
                break;
            // The CPU classes of the frame's drawn pixels.
            Camera3D cam = _scene.Camera;
            Vector2I size = ViewSize();
            var classes = new CutPixel[size.X * size.Y];
            int drawn = 0, cleared = 0, kept = 0, keptAbove = 0;
            for (int py = 0; py < size.Y; py++)
            {
                for (int px = 0; px < size.X; px++)
                {
                    int i = py * size.X + px, b = i * 4;
                    if (NearBackground((off[b], off[b + 1], off[b + 2])))
                    {
                        classes[i] = CutPixel.Keep;
                        continue;
                    }
                    drawn++;
                    var screen = new Vector2(px + 0.5f, py + 0.5f);
                    Vector3 o = Cutaway.ToMapAxes(cam.ProjectRayOrigin(screen)) * LevelMesh.MapUnitsPerMetre;
                    Vector3 d = Cutaway.ToMapAxes(cam.ProjectRayNormal(screen)).Normalized();
                    Vector3 p = o + d * ((foot - o).Dot(facing) / d.Dot(facing));
                    var a = new Vector3(centre.X, centre.Y, centre.Z + Cutaway.Anchor);
                    float distance = Cutaway.Distance(p, a, toCamera);
                    if (Math.Abs(p.Z - (centre.Z + decor.Height)) < CutMargin || Math.Abs(distance - decor.Radius) < CutMargin)
                    {
                        classes[i] = CutPixel.Skip;
                        continue;
                    }
                    bool cut = Cutaway.HidesThing(p, foot, facing, false, centre, decor, toCamera);
                    classes[i] = cut ? CutPixel.Cleared : CutPixel.Keep;
                    if (cut)
                        cleared++;
                    else
                        kept++;
                    if (!cut && p.Z > centre.Z + decor.Height)
                        keptAbove++;
                }
            }
            m.SetCutaway(decor);
            byte[]? cutFrame = await Capture($"{what}, decoration");
            m.SetCutawayCentres(null, centre);
            byte[]? cursor = await Capture($"{what}, decoration, cursor centre");
            m.SetCutawayCentres(centre, null);
            m.SetCutaway(decor with { Style = CutawayStyle.Dither });
            byte[]? dither = await Capture($"{what}, decoration, dither");
            m.SetCutaway(decor with { Things = CutawayThings.Off });
            byte[]? thingsOff = await Capture($"{what}, things off");
            things.SetEntry(thing, original with { Actor = true });
            m.SetCutaway(decor);
            byte[]? actor = await Capture($"{what}, actor");
            m.SetCutaway(decor with { Things = CutawayThings.All });
            byte[]? actorAll = await Capture($"{what}, actor, all");
            if (cutFrame is null || cursor is null || dither is null || thingsOff is null || actor is null || actorAll is null)
                break;

            int ditherCleared = 0;
            foreach ((byte[] frame, bool cuts, string label) in new[]
                { (cutFrame, true, "decoration"), (cursor, true, "decoration, cursor centre"), (thingsOff, false, "things off"), (actor, false, "actor"), (actorAll, true, "actor, all") })
            {
                int bad = 0;
                string first = "";
                for (int i = 0; i < classes.Length; i++)
                {
                    int b = i * 4;
                    (int R, int G, int B) got = (frame[b], frame[b + 1], frame[b + 2]);
                    bool ok = classes[i] switch
                    {
                        CutPixel.Skip => true,
                        CutPixel.Cleared when cuts => NearBackground(got),
                        _ => got == (off[b], off[b + 1], off[b + 2]),
                    };
                    if (!ok && bad++ == 0)
                        first = $"pixel ({i % size.X}, {i / size.X}): drew {got}, class {classes[i]}, {(off[b], off[b + 1], off[b + 2])} with the cutaway off";
                }
                if (bad > 0)
                    Fail($"{what}, {label}: {bad} pixel(s) wrong, first at {first}");
            }
            int badDither = 0;
            for (int i = 0; i < classes.Length; i++)
            {
                int b = i * 4;
                (int R, int G, int B) got = (dither[b], dither[b + 1], dither[b + 2]);
                if (classes[i] == CutPixel.Keep && got != (off[b], off[b + 1], off[b + 2]))
                    badDither++;
                else if (classes[i] == CutPixel.Cleared && NearBackground(got))
                    ditherCleared++;
            }
            if (badDither > 0)
                Fail($"{what}: {badDither} uncut pixel(s) changed with the dither");
            double share = cleared == 0 ? 0.5 : (double)ditherCleared / cleared;
            if (share < 0.3 || share > 0.7)
                Fail($"{what}: the dither cleared {share:P0} of the cut pixels, expected about half");
            bool front = centre == inFront;
            if (drawn == 0)
                Fail($"{what}: nothing drawn");
            else if (front ? cleared > 0 : cleared < 50)
                Fail($"{what}: {cleared} pixel(s) classified as cut ({(front ? "none expected in front of the centre" : "at least 50 expected")})");
            else if (centre == behind && kept - keptAbove < 50)
                Fail($"{what}: only {kept - keptAbove} pixel(s) kept below the cutoff (at least 50 expected)");
            else if (centre == below && keptAbove < 50)
                Fail($"{what}: only {keptAbove} pixel(s) kept outside the disc (at least 50 expected: its edge must cross the billboard)");
            _pixels += drawn * 6;
            GD.Print($"Level check: {what}: {drawn} sprite pixels, {cleared} cut, {kept} kept ({keptAbove} above the cutoff); cut as a decoration (also around the cursor) and as an actor with all, "
                + $"kept as an actor and with things off{(cleared > 0 ? $", dither cleared {share:P0}" : "")}");
        }
        things.SetEntry(thing, original);
        m.SetCutaway(new CutawaySettings { Style = CutawayStyle.Off });
        m.SetCutawayCentres(null, null);
        m.SetSprites(sprites);
        foreach (MeshInstance3D? chunk in _scene.Chunks)
        {
            if (chunk is not null)
                chunk.Visible = true;
        }
    }
}
