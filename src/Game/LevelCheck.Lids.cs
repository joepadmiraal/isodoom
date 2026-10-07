using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Map;
using IsoDoom.Render;

namespace IsoDoom.Game;

/// <summary>
/// The level check's lid part (T6.13b, SPEC §12): the lid sectors' data
/// (every map, no renderer needed), and with a real renderer a closed door's
/// lid seen along the game camera's direction at 1 unit per pixel, from a
/// centre 24 units behind the door (on the side away from the camera), whole
/// and cut with its cap, then the same door half open (its ceiling moved
/// above the cutoff, as the sim moves it).
/// <para>
/// Each pixel of the cut disc's screen box is classified as in the cutaway
/// check (<see cref="ClassifyCut"/>), with the lid planes as surfaces: with
/// the cutaway off, pixels whose first surface is definitely a lid must show
/// the sector's ceiling flat at that point, lit as its floor, and pixels whose
/// ray meets no lid must draw as with the lids off; with the cutaway on, the
/// cutaway check's comparisons (<see cref="CompareCutView"/>) hold with the
/// lids drawn, the cap showing the ceiling flat where it caps a lid's solid.
/// </para>
/// </summary>
public partial class LevelCheck
{
    private int _lidMaps, _lidMoves;

    /// <summary>The lid height of each sector from the level's current ceilings (<see cref="DoorLids.LidHeight"/>; <see cref="DoorLids.None"/> without a lid).</summary>
    private static float[] LidHeights(LevelMesh m)
    {
        var heights = new float[m.Level.Sectors.Length];
        for (int i = 0; i < heights.Length; i++)
            heights[i] = m.Lids.LidHeight(i, n => (float)(m.Level.Sectors[n].CeilingHeight / 65536.0));
        return heights;
    }

    /// <summary>
    /// The lids' data (every map): a lid sector's lowest neighbour's ceiling
    /// raised and lowered, as the sim moves it, and back: the lid texel
    /// follows (<see cref="CheckSectorData"/> recomputes it).
    /// </summary>
    private void CheckLidData(LevelMesh m, string map)
    {
        foreach (int i in m.Lids.Sectors)
        {
            Sector door = m.Level.Sectors[i];
            Sector lowest = m.Lids.Neighbours(i).Select(n => m.Level.Sectors[n]).MinBy(n => n.CeilingHeight)!;
            int old = lowest.CeilingHeight;
            foreach (int by in new[] { 16, -8 })
            {
                lowest.CeilingHeight = old + (by << Fixed.FRACBITS);
                m.UpdateSectors();
                CheckSectorData(m, $"{map} (sector {lowest.Index}'s ceiling moved {by}, beside lid sector {door.Index})");
            }
            lowest.CeilingHeight = old;
            m.UpdateSectors();
            CheckSectorData(m, $"{map} (sector {lowest.Index}'s ceiling back)");
            _lidMoves++;
            return;
        }
    }

    /// <summary>The drawn lid views (real renderer only); the lids are off for the other render checks.</summary>
    private async Task LidCheck(LevelMesh m)
    {
        string map = m.Level.Name;
        var settings = new CutawaySettings { Style = CutawayStyle.Cut, Cap = CutawayCap.Off };
        Basis basis = GameBasis(IsoCamera.DefaultPitch);
        Vector3 toCamera = Cutaway.ToMapAxes(basis.Z).Normalized();
        var toCameraFlat = new Vector2(toCamera.X, toCamera.Y).Normalized();
        Vector2I size = ViewSize();
        float[] lids = LidHeights(m);

        // Closed doors first, then any lid sector whose lid shows with room to open above the cutoff.
        IEnumerable<int> candidates = m.Lids.Sectors
            .Where(i => DoorLids.Shows(lids[i], m.Level.Sectors[i].CeilingHeight / 65536f)
                && lids[i] - m.Level.Sectors[i].FloorHeight / 65536f >= settings.Height + 16)
            .OrderBy(i => m.Level.Sectors[i].CeilingHeight > m.Level.Sectors[i].FloorHeight ? 1 : 0);
        int tried = 0;
        foreach (int i in candidates)
        {
            Sector door = m.Level.Sectors[i];
            foreach (Line line in door.Lines)
            {
                if (line.FrontSector is not Sector front || line.BackSector is not Sector back || front == back)
                    continue;
                Sector other = front == door ? back : front;
                var v1 = new Vector2(line.V1.X / 65536f, line.V1.Y / 65536f);
                var v2 = new Vector2(line.V2.X / 65536f, line.V2.Y / 65536f);
                float len = v1.DistanceTo(v2);
                if (len < 32)
                    continue;
                Vector2 dir = (v2 - v1) / len, n = new(dir.Y, -dir.X); // towards the front sector
                if (front == door)
                    n = -n;
                if (n.Dot(toCameraFlat) > -0.5f)
                    continue; // the door must be between the centre and the camera
                Vector2 mid = (v1 + v2) / 2;
                var centre = new Vector3(mid.X + n.X * 24, mid.Y + n.Y * 24, other.FloorHeight / 65536f);
                if (CursorGround.DrawnSectorAt(m, (int)(centre.X * 65536), (int)(centre.Y * 65536)) != other.Index || door.FloorHeight > other.FloorHeight)
                    continue;
                if (tried++ >= 40)
                    break;
                CutView(centre, basis, toCamera);
                CutClasses c = ClassifyCut(m, CutQuads(m), FloorHeights(m), centre, settings, toCamera, size.X, size.Y, lids);
                if (c.Capped < 50)
                    continue;
                string what = $"{map}: lid of sector {door.Index} (lid at {lids[i]:F0}, ceiling {door.CeilingHeight >> Fixed.FRACBITS}), centre ({centre.X:F0}, {centre.Y:F0}, {centre.Z:F0}) behind line {line.Index}";
                await CompareLidTopDown(m, door, lids, $"{map}: lid of sector {door.Index}");
                await CompareLidView(m, centre, c, settings, basis, toCamera, what);

                // The door half open, as the sim moves it: its ceiling above the cutoff, so the lid stays and caps nothing.
                int old = door.CeilingHeight;
                door.CeilingHeight = door.FloorHeight + ((int)settings.Height + 8 << Fixed.FRACBITS);
                m.UpdateSectors();
                float[] moved = LidHeights(m);
                CutClasses o = ClassifyCut(m, CutQuads(m), FloorHeights(m), centre, settings, toCamera, size.X, size.Y, moved);
                string opened = $"{map}: lid of sector {door.Index} opened to {door.CeilingHeight >> Fixed.FRACBITS}, centre ({centre.X:F0}, {centre.Y:F0}, {centre.Z:F0})";
                await CompareLidTopDown(m, door, moved, opened);
                if (o.Capped > 0)
                    Fail($"{opened}: {o.Capped} pixels classified as capped (none expected: the ceiling is above the cutoff)");
                else
                    await CompareLidView(m, centre, o, settings, basis, toCamera, opened);
                door.CeilingHeight = old;
                m.UpdateSectors();
                _lidMaps++;
                return;
            }
        }
        if (m.Lids.Sectors.Count == 0)
            GD.Print($"Level check: {map}: lids: no lid sector");
        else
            Fail($"{map}: lids: no view found (a lid sector {settings.Height + 16} units deep below its lid, a two-sided line onto a sector away from the game camera, "
                + "with at least 50 pixels capped from 24 units behind it)");
    }

    /// <summary>
    /// <paramref name="door"/>'s lid straight from above (the lids and the
    /// cutaway's top surfaces are horizontal, its walls edge-on), 1 unit per
    /// pixel with pixel centres on texel centres, around the sector's box:
    /// pixels whose five points (<see cref="CutMargin"/> apart) are all in
    /// one sector with a lid drawn show its ceiling flat; pixels with none in
    /// such a sector draw as with the lids off. At least 50 lid pixels.
    /// </summary>
    private async Task CompareLidTopDown(LevelMesh m, Sector door, float[] lids, string what)
    {
        Vector2I size = ViewSize();
        int w = size.X, h = size.Y;
        SectorFloor floor = m.Floors.BySector.First(f => f.Sector == door.Index);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (PolygonVertex v in floor.Vertices)
        {
            (minX, maxX) = (Math.Min(minX, v.X >> Fixed.FRACBITS), Math.Max(maxX, (v.X >> Fixed.FRACBITS) + 1));
            (minY, maxY) = (Math.Min(minY, v.Y >> Fixed.FRACBITS), Math.Max(maxY, (v.Y >> Fixed.FRACBITS) + 1));
        }
        int cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
        int left = cx - w / 2, top = cy + h / 2;
        (int lowest, int highest) = HeightRange(m.Level);
        float cameraZ = Math.Max(highest, lids.Max()) + 64.5f;
        var basis = new Basis(new Vector3(1, 0, 0), new Vector3(0, 0, -1), new Vector3(0, 1, 0));
        Ortho(basis, new Vector3(left + w / 2f, top - h / 2f, cameraZ), 1, cameraZ - lowest + 64);
        m.SetCutaway(new CutawaySettings { Style = CutawayStyle.Off });
        m.SetDoorLids(DoorLidMode.Off);
        byte[]? off = await Capture($"{what}, top-down, lids off");
        m.SetDoorLids(DoorLidMode.On);
        byte[]? on = await Capture($"{what}, top-down, lids on");
        if (off is null || on is null)
            return;
        int compared = 0, lidPixels = 0, badLid = 0, badKeep = 0;
        string firstLid = "", firstKeep = "";
        for (int py = Math.Max(0, top - maxY - 16); py < Math.Min(h, top - minY + 16); py++)
        {
            for (int px = Math.Max(0, minX - 16 - left); px < Math.Min(w, maxX + 16 - left); px++)
            {
                double x = left + px + 0.5, y = top - py - 0.5;
                int inLid = 0, sector = -2;
                foreach ((double ox, double oy) in new[] { (0.0, 0.0), (CutMargin, 0.0), (-CutMargin, 0.0), (0.0, CutMargin), (0.0, -CutMargin) })
                {
                    int s = CursorGround.DrawnSectorAt(m, (int)Math.Round((x + ox) * 65536.0), (int)Math.Round((y + oy) * 65536.0));
                    if (s >= 0 && m.Lids.Has(s) && DoorLids.Shows(lids[s], m.Level.Sectors[s].CeilingHeight / 65536f))
                    {
                        inLid++;
                        sector = sector == -2 || sector == s ? s : -1;
                    }
                }
                int p = (py * w + px) * 4;
                (int R, int G, int B) got = (on[p], on[p + 1], on[p + 2]);
                if (inLid == 0)
                {
                    compared++;
                    if (got != (off[p], off[p + 1], off[p + 2]) && badKeep++ == 0)
                        firstKeep = $"pixel ({px}, {py}), map ({x}, {y}): drew {got}, {(off[p], off[p + 1], off[p + 2])} with the lids off";
                }
                else if (inLid == 5 && sector >= 0)
                {
                    Sector s = m.Level.Sectors[sector];
                    int colormap = ExpectedColormap(m, false, s.LightLevel, 0, x, y, cameraZ - lids[sector]);
                    if (colormap < 0)
                        continue;
                    (int col, int row) = TextureWrap.FlatTexel((int)Math.Floor(x * 65536), (int)Math.Floor(y * 65536));
                    (int R, int G, int B) expected = Shade(ShownFlat(m, s.CeilingPic)[col, row], colormap);
                    compared++;
                    lidPixels++;
                    if (got != expected && badLid++ == 0)
                        firstLid = $"pixel ({px}, {py}), map ({x}, {y}) in sector {sector}: drew {got}, expected {expected}";
                }
            }
        }
        _pixels += compared;
        if (badLid > 0)
            Fail($"{what}, top-down: {badLid} of {lidPixels} lid pixel(s) differ, first at {firstLid}");
        if (badKeep > 0)
            Fail($"{what}, top-down: {badKeep} pixel(s) outside the lids changed with the lids on, first at {firstKeep}");
        if (lidPixels < 50)
            Fail($"{what}, top-down: only {lidPixels} lid pixels compared (at least 50 expected)");
        GD.Print($"Level check: {what}, top-down: {compared} pixels compared, {lidPixels} of them on a lid, the rest as with the lids off");
    }

    /// <summary>
    /// The lid view of <paramref name="centre"/>: with the cutaway off, the
    /// lids against the CPU and the frame with them off; then the cutaway
    /// check's comparisons with the lids on.
    /// </summary>
    private async Task CompareLidView(LevelMesh m, Vector3 centre, CutClasses classes, CutawaySettings settings, Basis basis, Vector3 toCamera, string what)
    {
        Vector2I size = ViewSize();
        int w = size.X;
        CutView(centre, basis, toCamera);
        m.SetCutawayCentres(centre, null);
        m.SetCutaway(settings with { Style = CutawayStyle.Off });
        m.SetDoorLids(DoorLidMode.Off);
        byte[]? off = await Capture($"{what}, lids off");
        m.SetDoorLids(DoorLidMode.On);
        byte[]? on = await Capture($"{what}, lids on");
        if (off is null || on is null)
            return;
        var box = classes.Box;
        int bw = box.Right - box.Left;
        int compared = 0, lidPixels = 0, badLid = 0, badKeep = 0;
        string firstLid = "", firstKeep = "";
        for (int py = box.Top; py < box.Bottom; py++)
        {
            for (int px = box.Left; px < box.Right; px++)
            {
                int i = (py - box.Top) * bw + (px - box.Left), p = (py * w + px) * 4;
                (int R, int G, int B) got = (on[p], on[p + 1], on[p + 2]);
                if (classes.LidStates[i] == 0)
                {
                    compared++;
                    if (got != (off[p], off[p + 1], off[p + 2]) && badKeep++ == 0)
                        firstKeep = $"pixel ({px}, {py}): drew {got}, {(off[p], off[p + 1], off[p + 2])} with the lids off";
                }
                else if (classes.LidStates[i] == 1)
                {
                    CapPoint c = classes.LidPoints[i];
                    Sector sector = m.Level.Sectors[c.Sector];
                    int colormap = ExpectedColormap(m, false, sector.LightLevel, 0, c.X, c.Y, c.Depth);
                    if (colormap < 0)
                        continue;
                    (int col, int row) = TextureWrap.FlatTexel((int)Math.Floor(c.X * 65536), (int)Math.Floor(c.Y * 65536));
                    (int R, int G, int B) expected = Shade(ShownFlat(m, sector.CeilingPic)[col, row], colormap);
                    compared++;
                    lidPixels++;
                    if (got != expected && badLid++ == 0)
                        firstLid = $"pixel ({px}, {py}), map ({c.X:F2}, {c.Y:F2}) in sector {c.Sector}: drew {got}, expected {expected}";
                }
            }
        }
        _pixels += compared;
        if (badLid > 0)
            Fail($"{what}: {badLid} of {lidPixels} lid pixel(s) differ, first at {firstLid}");
        if (badKeep > 0)
            Fail($"{what}: {badKeep} pixel(s) whose ray meets no lid changed with the lids on, first at {firstKeep}");
        GD.Print($"Level check: {what}: {compared} pixels compared with the cutaway off, {lidPixels} of them on a lid, the rest as with the lids off");
        await CompareCutView(m, centre, classes, settings, basis, toCamera, $"{what}, lids on");
    }
}
