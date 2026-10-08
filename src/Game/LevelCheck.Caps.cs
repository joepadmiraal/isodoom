using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Map;
using IsoDoom.Render;

namespace IsoDoom.Game;

/// <summary>
/// The level check's wall cap part (T6.13e, SPEC §12), with a real renderer:
/// the largest cap straight down at 1 unit per pixel, with the caps off and
/// on (pixels well inside it must show its wall's texture at the column along
/// the wall and the row behind it, lit as a floor of its sector at its
/// ceiling; pixels outside every cap must not change), and from the game
/// camera's angle a cap of a wall facing away (whose own quad is culled: its
/// cap must not be drawn) and one of a wall facing the camera (drawn); last
/// the island wall drawn furthest above its ceiling, side-on (every pixel
/// between its ceiling and the island's top against the CPU's wall texel and
/// light, and nothing there with the caps off).
/// </summary>
public partial class LevelCheck
{
    private async Task WallCapCheck(LevelMesh m)
    {
        if (m.Caps.Caps.Count == 0)
        {
            Fail($"{m.Level.Name}: no wall caps");
            return;
        }
        m.SetCutaway(new CutawaySettings { Style = CutawayStyle.Off });
        m.SetDoorLids(DoorLidMode.Off);
        WallCap largest = m.Caps.Caps.Where(c => Height(m, c) > 0).MaxBy(c => c.Pieces.Sum(Area))!;
        await CompareCapTopDown(m, largest, $"{m.Level.Name}: line {largest.Line}'s wall cap");
        await CompareCapsFromGameCamera(m);
        await CompareIslandWall(m);
        m.SetWallCaps(WallCapMode.Off);
    }

    // A cap's wall height (its sector's ceiling above its floor; lids off), map units.
    private static float Height(LevelMesh m, WallCap cap) =>
        (m.Level.Sectors[cap.Sector].CeilingHeight - m.Level.Sectors[cap.Sector].FloorHeight) / 65536f;

    // A cap's top with the lids off: its sector's ceiling, or its island's top.
    private static float CapTop(LevelMesh m, WallCap cap)
    {
        float ceiling = m.Level.Sectors[cap.Sector].CeilingHeight / 65536f;
        return cap.Island < 0 ? ceiling : Math.Max(ceiling, m.Caps.IslandTop(cap.Island, s => m.Level.Sectors[s].CeilingHeight / 65536f));
    }

    /// <summary>
    /// The island wall drawn furthest above its ceiling (T6.13e), side-on at 1
    /// unit per pixel from just in front of it (the near and far planes keep
    /// only its plane): between its ceiling and the island's top it shows its
    /// middle texture going on upwards, lit as the wall, and nothing with the caps off.
    /// </summary>
    private async Task CompareIslandWall(LevelMesh m)
    {
        string map = m.Level.Name;
        Vector2I size = ViewSize();
        int w = size.X, h = size.Y;
        float Ceiling(int sector) => m.Level.Sectors[sector].CeilingHeight / 65536f;
        WallSection? best = null;
        float bestArea = 0;
        foreach (WallSection s in m.Walls.Sections)
        {
            if (!IsIslandWall(m, s))
                continue;
            float rise = m.Caps.IslandTop(m.Caps.IslandOf(s.Line.Index), Ceiling) - Ceiling(s.FrontSector.Index);
            float span = (float)Math.Sqrt((double)s.Line.Dx * s.Line.Dx + (double)s.Line.Dy * s.Line.Dy) / 65536f;
            if (rise > 8 && span > 8 && rise * span > bestArea && rise < h - 8 && span < w - 8)
                (best, bestArea) = (s, rise * span);
        }
        if (best is null)
        {
            GD.Print($"Level check: {map}: no island wall drawn above its ceiling");
            return;
        }
        WallSection wall = best;
        Sector sector = wall.FrontSector;
        float bottom = Ceiling(sector.Index), top = m.Caps.IslandTop(m.Caps.IslandOf(wall.Line.Index), Ceiling);
        double x1 = wall.Line.V1.X / 65536.0, y1 = wall.Line.V1.Y / 65536.0, x2 = wall.Line.V2.X / 65536.0, y2 = wall.Line.V2.Y / 65536.0;
        double length = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
        double dx = (x2 - x1) / length, dy = (y2 - y1) / length, nx = dy, ny = -dx; // towards the front sector
        // Looking at its front: V1 on the left, up is up, the camera 2 units in front.
        var basis = new Basis(new Vector3((float)dx, 0, (float)-dy), new Vector3(0, 1, 0), new Vector3((float)nx, 0, (float)-ny));
        double mx = (x1 + x2) / 2, my = (y1 + y2) / 2, mz = Math.Floor((bottom + top) / 2);
        Ortho(basis, new Vector3((float)(mx + 2 * nx), (float)(my + 2 * ny), (float)mz), 1, 3);
        m.SetWallCaps(WallCapMode.Off);
        byte[]? off = await Capture($"{map}: line {wall.Line.Index} side-on, caps off");
        m.SetWallCaps(WallCapMode.On);
        byte[]? on = await Capture($"{map}: line {wall.Line.Index} side-on, caps on");
        if (off is null || on is null)
            return;

        IsoDoom.Wad.Graphics.IndexedImage tex = ShownComposite(m, wall.Texture);
        double textureTop = wall.TextureTopFor(tex.Height << Fixed.FRACBITS) / 65536.0;
        int contrast = m.Pieces.Of(wall.Line, wall.Side)[0].Contrast;
        int compared = 0, bad = 0, badOff = 0;
        string first = "", firstOff = "";
        for (int py = 0; py < h; py++)
        {
            double z = mz + h / 2.0 - py - 0.5;
            if (z < bottom + 0.5 || z > top - 0.5)
                continue;
            for (int px = 0; px < w; px++)
            {
                double along = length / 2 + px + 0.5 - w / 2.0;
                if (along < 0.5 || along > length - 0.5)
                    continue;
                double column = wall.TextureOffset / 65536.0 + along, row = textureTop - z;
                if (Math.Abs(column - Math.Round(column)) < 1.0 / 64 || Math.Abs(row - Math.Round(row)) < 1.0 / 64)
                    continue;
                int i = (py * w + px) * 4;
                if (!NearBackground((off[i], off[i + 1], off[i + 2])) && badOff++ == 0)
                    firstOff = $"pixel ({px}, {py}), z {z}: drew {(off[i], off[i + 1], off[i + 2])} with the caps off, expected the background";
                int colormap = ExpectedColormap(m, true, sector.LightLevel, contrast, x1 + dx * along, y1 + dy * along, 2);
                if (colormap < 0)
                    continue;
                (int tc, int tr) = TextureWrap.WallTexel((int)Math.Floor(column), (int)Math.Floor(row), tex.Width, tex.Height, WallTextureTiling.Vanilla);
                (int R, int G, int B) expected = Shade(tex[tc, tr], colormap);
                compared++;
                if ((on[i], on[i + 1], on[i + 2]) != expected && bad++ == 0)
                    first = $"pixel ({px}, {py}), z {z}, column {column:F2}, row {row:F2}: drew {(on[i], on[i + 1], on[i + 2])}, expected {expected}";
            }
        }
        _pixels += compared;
        string what = $"{map}: line {wall.Line.Index} (island {m.Caps.IslandOf(wall.Line.Index)}) above its ceiling ({bottom} to {top})";
        if (bad > 0)
            Fail($"{what}: {bad} of {compared} pixel(s) differ, first at {first}");
        if (badOff > 0)
            Fail($"{what}: {badOff} pixel(s) drawn with the caps off, first at {firstOff}");
        if (compared < 50)
            Fail($"{what}: only {compared} pixels compared (at least 50 expected)");
        GD.Print($"Level check: {what}, side-on: {compared} pixels against the wall's texture going on upwards, none drawn with the caps off");
    }

    private static double Area((double X, double Y)[] p)
    {
        double sum = 0;
        for (int i = 0; i < p.Length; i++)
            sum += p[i].X * p[(i + 1) % p.Length].Y - p[(i + 1) % p.Length].X * p[i].Y;
        return Math.Abs(sum) / 2;
    }

    // Whether a point is inside a convex polygon of either winding (on an edge counts).
    private static bool InConvex((double X, double Y)[] p, double x, double y)
    {
        int sign = 0;
        for (int i = 0; i < p.Length; i++)
        {
            (double ax, double ay) = p[i];
            (double bx, double by) = p[(i + 1) % p.Length];
            double cross = (bx - ax) * (y - ay) - (by - ay) * (x - ax);
            int s = Math.Sign(cross);
            if (s == 0)
                continue;
            if (sign != 0 && s != sign)
                return false;
            sign = s;
        }
        return true;
    }

    private static bool InCap(WallCap cap, double x, double y) => cap.Pieces.Any(p => InConvex(p, x, y));

    private async Task CompareCapTopDown(LevelMesh m, WallCap cap, string what)
    {
        Vector2I size = ViewSize();
        int w = size.X, h = size.Y;
        double minX = cap.Pieces.Min(p => p.Min(v => v.X)), maxX = cap.Pieces.Max(p => p.Max(v => v.X));
        double minY = cap.Pieces.Min(p => p.Min(v => v.Y)), maxY = cap.Pieces.Max(p => p.Max(v => v.Y));
        int left = (int)Math.Floor((minX + maxX) / 2) - w / 2, top = (int)Math.Floor((minY + maxY) / 2) + h / 2;
        (int lowest, int highest) = HeightRange(m.Level);
        float cameraZ = highest + 64.5f;
        var basis = new Basis(new Vector3(1, 0, 0), new Vector3(0, 0, -1), new Vector3(0, 1, 0));
        Ortho(basis, new Vector3(left + w / 2f, top - h / 2f, cameraZ), 1, cameraZ - lowest + 64);
        m.SetWallCaps(WallCapMode.Off);
        byte[]? off = await Capture($"{what}, top-down, caps off");
        m.SetWallCaps(WallCapMode.On);
        byte[]? on = await Capture($"{what}, top-down, caps on");
        if (off is null || on is null)
            return;

        Sector sector = m.Level.Sectors[cap.Sector];
        IsoDoom.Map.Side side = m.Level.Sides[cap.Side];
        IsoDoom.Wad.Graphics.IndexedImage tex = ShownComposite(m, Textures.R_CheckTextureNumForName(side.MidTexture));
        float capTop = CapTop(m, cap);
        var near = m.Caps.Caps.Where(c => c.Pieces.Any(p => p.Any(v => v.X > minX - 64 && v.X < maxX + 64 && v.Y > minY - 64 && v.Y < maxY + 64))).ToList();
        int compared = 0, capPixels = 0, badCap = 0, badKeep = 0;
        string firstCap = "", firstKeep = "";
        (double, double)[] samples = [(0, 0), (CutMargin, 0), (-CutMargin, 0), (0, CutMargin), (0, -CutMargin)];
        for (int py = Math.Max(0, top - (int)maxY - 16); py < Math.Min(h, top - (int)minY + 16); py++)
        {
            for (int px = Math.Max(0, (int)minX - 16 - left); px < Math.Min(w, (int)maxX + 16 - left); px++)
            {
                double x = left + px + 0.5, y = top - py - 0.5;
                int inThis = 0, inOther = 0;
                foreach ((double ox, double oy) in samples)
                {
                    foreach (WallCap c in near)
                    {
                        if (InCap(c, x + ox, y + oy))
                        {
                            if (c == cap)
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
                        firstKeep = $"pixel ({px}, {py}), map ({x}, {y}): drew {got}, {(off[p], off[p + 1], off[p + 2])} with the caps off";
                }
                else if (inThis == samples.Length && inOther == 0)
                {
                    double column = side.TextureOffset / 65536.0 + cap.Along(x, y), row = cap.Behind(x, y);
                    if (Math.Abs(column - Math.Round(column)) < 1.0 / 64 || Math.Abs(row - Math.Round(row)) < 1.0 / 64)
                        continue;
                    int colormap = ExpectedColormap(m, false, sector.LightLevel, 0, x, y, cameraZ - capTop);
                    if (colormap < 0)
                        continue;
                    (int tc, int tr) = TextureWrap.WallTexel((int)Math.Floor(column), (int)Math.Floor(row), tex.Width, tex.Height, WallTextureTiling.Vanilla);
                    (int R, int G, int B) expected = Shade(tex[tc, tr], colormap);
                    compared++;
                    capPixels++;
                    if (got != expected && badCap++ == 0)
                        firstCap = $"pixel ({px}, {py}), map ({x}, {y}), column {column:F2}, row {row:F2}: drew {got}, expected {expected}";
                }
            }
        }
        _pixels += compared;
        if (badCap > 0)
            Fail($"{what}, top-down: {badCap} of {capPixels} cap pixel(s) differ, first at {firstCap}");
        if (badKeep > 0)
            Fail($"{what}, top-down: {badKeep} pixel(s) outside the caps changed with the caps on, first at {firstKeep}");
        if (capPixels < 50)
            Fail($"{what}, top-down: only {capPixels} cap pixels compared (at least 50 expected)");
        GD.Print($"Level check: {what}, top-down: {compared} pixels compared, {capPixels} of them on the cap, the rest as with the caps off");
    }

    /// <summary>
    /// From the game camera's angle: the largest cap of a wall facing away
    /// changes no pixel where it alone would be (its wall's quad is culled,
    /// and the shader collapses its cap), and a cap of a wall facing the
    /// camera changes some (the first of the ten largest that is not hidden).
    /// </summary>
    private async Task CompareCapsFromGameCamera(LevelMesh m)
    {
        string map = m.Level.Name;
        Basis basis = GameBasis(IsoCamera.DefaultPitch);
        Vector3 toCamera = Cutaway.ToMapAxes(basis.Z).Normalized();
        var flat = new Vector2(toCamera.X, toCamera.Y).Normalized();
        double Facing(WallCap c) => -(c.Normal.X * flat.X + c.Normal.Y * flat.Y);
        List<WallCap> drawn = m.Caps.Caps.Where(c => Height(m, c) > 0).OrderByDescending(c => c.Pieces.Sum(Area)).ToList();
        WallCap? away = drawn.FirstOrDefault(c => Facing(c) < -0.3);
        if (away is null)
        {
            Fail($"{map}: no wall cap facing away from the game camera");
            return;
        }
        int awayPixels = await CompareCapFromGameCamera(m, away, basis, toCamera, false);
        int changed = 0;
        WallCap? shown = null;
        foreach (WallCap c in drawn.Where(c => Facing(c) > 0.3).Take(10))
        {
            changed = await CompareCapFromGameCamera(m, c, basis, toCamera, true);
            if (changed >= 50)
            {
                shown = c;
                break;
            }
        }
        if (shown is null)
            Fail($"{map}: no wall cap facing the game camera drew 50 pixels (of the ten largest)");
        GD.Print($"Level check: {map}: wall caps from the game camera: line {away.Line}'s (facing away) checked on {awayPixels} pixels it alone covers; "
            + $"line {shown?.Line}'s (facing the camera) changed {changed}");
    }

    // The pixels where only this cap would show, from the game camera centred on it: with
    // drawnExpected false they must not change with the caps on (returns how many there
    // are); with true, returns how many changed.
    private async Task<int> CompareCapFromGameCamera(LevelMesh m, WallCap cap, Basis basis, Vector3 toCamera, bool drawnExpected)
    {
        Vector2I size = ViewSize();
        int w = size.X, h = size.Y;
        double cx = cap.Pieces.Average(p => p.Average(v => v.X)), cy = cap.Pieces.Average(p => p.Average(v => v.Y));
        float Top(WallCap c) => CapTop(m, c);
        CutView(new Vector3((float)cx, (float)cy, Top(cap) - Cutaway.Anchor), basis, toCamera);
        Camera3D camera = _scene.Camera;
        List<Vector2> Project((double X, double Y)[] piece, float z) =>
            [.. piece.Select(v => camera.UnprojectPosition(new Vector3((float)v.X, z, (float)-v.Y) / LevelMesh.MapUnitsPerMetre))];
        List<List<Vector2>> own = [.. cap.Pieces.Select(p => Project(p, Top(cap)))];
        float minX = own.Min(p => p.Min(v => v.X)), maxX = own.Max(p => p.Max(v => v.X));
        float minY = own.Min(p => p.Min(v => v.Y)), maxY = own.Max(p => p.Max(v => v.Y));
        var others = new List<List<Vector2>>();
        foreach (WallCap c in m.Caps.Caps)
        {
            if (c == cap)
                continue;
            foreach ((double X, double Y)[] piece in c.Pieces)
            {
                List<Vector2> q = Project(piece, Top(c));
                if (q.Max(v => v.X) >= minX - 4 && q.Min(v => v.X) <= maxX + 4 && q.Max(v => v.Y) >= minY - 4 && q.Min(v => v.Y) <= maxY + 4)
                    others.Add(q);
            }
        }
        m.SetWallCaps(WallCapMode.Off);
        byte[]? off = await Capture($"{m.Level.Name}: line {cap.Line}'s wall cap from the game camera, caps off");
        m.SetWallCaps(WallCapMode.On);
        byte[]? on = await Capture($"{m.Level.Name}: line {cap.Line}'s wall cap from the game camera, caps on");
        if (off is null || on is null)
            return 0;
        static bool In(List<Vector2> p, float x, float y) => InConvex([.. p.Select(v => ((double)v.X, (double)v.Y))], x, y);
        int pixels = 0, changed = 0;
        string first = "";
        for (int py = Math.Max(0, (int)minY); py < Math.Min(h, (int)maxY + 1); py++)
        {
            for (int px = Math.Max(0, (int)minX); px < Math.Min(w, (int)maxX + 1); px++)
            {
                float x = px + 0.5f, y = py + 0.5f;
                bool alone = true;
                foreach ((float ox, float oy) in new[] { (0f, 0f), (2f, 0f), (-2f, 0f), (0f, 2f), (0f, -2f) })
                {
                    alone &= own.Any(p => In(p, x + ox, y + oy)) && !others.Any(p => In(p, x + ox, y + oy));
                    if (!alone)
                        break;
                }
                if (!alone)
                    continue;
                pixels++;
                int i = (py * w + px) * 4;
                if ((on[i], on[i + 1], on[i + 2]) != (off[i], off[i + 1], off[i + 2]) && changed++ == 0)
                    first = $"pixel ({px}, {py}): drew {(on[i], on[i + 1], on[i + 2])}, {(off[i], off[i + 1], off[i + 2])} with the caps off";
            }
        }
        _pixels += pixels;
        if (!drawnExpected && changed > 0)
            Fail($"{m.Level.Name}: line {cap.Line}'s wall cap faces away from the game camera, but {changed} of its {pixels} pixel(s) changed with the caps on, first at {first}");
        return drawnExpected ? changed : pixels;
    }
}
