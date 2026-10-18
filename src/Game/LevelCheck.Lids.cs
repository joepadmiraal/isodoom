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
/// above the cutoff, as the sim moves it), and last the closed door from a
/// centre 24 units in front of it, whose lid behind the centre the cutaway
/// keeps whole.
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
    private int _lidMaps, _lidMoves, _lidEnds, _lidEndsShown, _lidEndsOntoVoid;

    /// <summary>The lid height of each sector from the level's current ceilings (<see cref="DoorLids.LidHeight"/>; <see cref="DoorLids.None"/> without a lid).</summary>
    private static float[] LidHeights(LevelMesh m)
    {
        float[] heights = new float[m.Level.Sectors.Length];
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

    /// <summary>
    /// The lids' ends (T6.13f, every map): each lid sector's drawn one-sided
    /// wall has its end (<see cref="DoorLids.HasEnd"/>; the chunk check
    /// compares its vertices), facing out of its sector (just across the line
    /// on its right its own sector's floor is drawn, on its left not: the
    /// void, but for a few maps' overlapping sectors), and
    /// spans from the sector's ceiling up to its lid as the shader evaluates
    /// them from the uploaded data: the lid's height while it shows, nothing
    /// while the sector is open above it; also with the lowest neighbour's
    /// ceiling lowered below the sector's (the end collapses) and back.
    /// </summary>
    private void CheckLidEndData(LevelMesh m, string map)
    {
        int ends = 0, shown = 0, facing = 0, badFacing = 0, nodeArtefacts = 0;
        string firstFacing = "", firstArtefact = "";
        float Ceiling(int s) => m.Level.Sectors[s].CeilingHeight / 65536f;
        // The shader's span of a lid end: (bottom, top), top == bottom when it collapses.
        (float Bottom, float Top) Span(int sector)
        {
            float bottom = m.SectorData(sector).G;
            (float height, int slot, _, _) = m.LidData(sector);
            return (bottom, m.LidMode == DoorLidMode.On && slot >= 0 ? Math.Max(height, bottom) : bottom);
        }
        foreach (WallSection s in m.Walls.Sections)
        {
            bool expected = LevelMesh.IsDrawn(s) && s.BackSector is null && s.Kind == WallSectionKind.Middle && m.Lids.Has(s.FrontSector.Index);
            if (m.Lids.HasEnd(s) != expected)
                Fail($"{map}: line {s.Line.Index}: HasEnd {!expected}, expected {expected}");
            if (!expected)
                continue;
            ends++;
            int sector = s.FrontSector.Index;
            foreach (WallPiece pc in m.Pieces.Of(s.Line, s.Side))
            {
                double ax = pc.A.X / 65536.0, ay = pc.A.Y / 65536.0, bx = pc.B.X / 65536.0, by = pc.B.Y / 65536.0;
                double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                if (len < 4)
                    continue;
                double mx = (ax + bx) / 2, my = (ay + by) / 2, nx = (by - ay) / len, ny = -(bx - ax) / len; // towards the sector
                int inside = CursorGround.DrawnSectorAt(m, (int)Math.Round((mx + nx) * 65536), (int)Math.Round((my + ny) * 65536));
                int outside = CursorGround.DrawnSectorAt(m, (int)Math.Round((mx - nx) * 65536), (int)Math.Round((my - ny) * 65536));
                // P3: a sector a unit thin there (AV MAP29 sector 559, lines 4066 and 4069): half a unit in
                if (inside != sector)
                {
                    int half = CursorGround.DrawnSectorAt(m, (int)Math.Round((mx + nx / 2) * 65536), (int)Math.Round((my + ny / 2) * 65536));
                    if (half == sector)
                        inside = half;
                }
                facing++;
                // P3: where the map's BSP leaves themselves put another sector just inside the line, or
                // the line's own sector just outside it (a node artefact, T2.2a, or a one-sided line
                // within its sector), the floor drawn there follows the leaves: the map's data, not the
                // end's facing (PWADs: Scythe MAP17 line 985, MAP22 line 109, MAP24 line 729; AV).
                if (inside != sector || outside == sector)
                {
                    int leafInside = m.Level.R_PointInSubsector((int)Math.Round((mx + nx) * 65536), (int)Math.Round((my + ny) * 65536)).Sector.Index;
                    int leafOutside = m.Level.R_PointInSubsector((int)Math.Round((mx - nx) * 65536), (int)Math.Round((my - ny) * 65536)).Sector.Index;
                    if (leafInside != sector || leafOutside == sector)
                    {
                        if (nodeArtefacts++ == 0)
                            firstArtefact = $"line {s.Line.Index}: BSP leaves of sectors {leafInside} on its right and {leafOutside} on its left (sector {sector})";
                        continue;
                    }
                }
                // Out of its sector (the void, or rarely another sector overlapping it: Doom II MAP17 line 728, MAP19 line 430).
                if ((inside != sector || outside == sector) && badFacing++ == 0)
                    firstFacing = $"line {s.Line.Index}: sector {inside} on its right, {outside} on its left (expected {sector} and not it)";
                if (outside < 0)
                    _lidEndsOntoVoid++;
            }
            (float bottom, float top) = Span(sector);
            float lid = m.Lids.LidHeight(sector, Ceiling);
            bool shows = m.LidMode == DoorLidMode.On && m.LidData(sector).Slot >= 0 && DoorLids.Shows(lid, Ceiling(sector));
            if (shows)
                shown++;
            if (Math.Abs(bottom - Ceiling(sector)) > 1e-3f || Math.Abs(top - (shows ? lid : Ceiling(sector))) > 1e-3f)
                Fail($"{map}: line {s.Line.Index}'s lid end spans {bottom}..{top}, expected {Ceiling(sector)}..{(shows ? lid : Ceiling(sector))}");
        }
        if (badFacing > 0)
            Fail($"{map}: {badFacing} of {facing} lid end piece(s) do not face the void from their sector, first {firstFacing}");
        if (nodeArtefacts > 0)
            GD.Print($"Level check: {map}: {nodeArtefacts} of {facing} lid end piece(s) lie where the BSP leaves put another sector inside them or theirs outside (node artefacts, T2.2a), first {firstArtefact}");

        // A lid end collapses when the lid no longer shows: the lowest neighbour's ceiling lowered to the sector's.
        WallSection? one = m.Walls.Sections.FirstOrDefault(s => m.Lids.HasEnd(s) && Span(s.FrontSector.Index) is var (b, t) && t > b);
        if (one is not null)
        {
            int sector = one.FrontSector.Index;
            Sector lowest = m.Lids.Neighbours(sector).Select(n => m.Level.Sectors[n]).MinBy(n => n.CeilingHeight)!;
            int old = lowest.CeilingHeight;
            lowest.CeilingHeight = m.Level.Sectors[sector].CeilingHeight;
            m.UpdateSectors();
            if (Span(sector) is var (b, t) && t != b)
                Fail($"{map}: line {one.Line.Index}'s lid end spans {b}..{t} with sector {lowest.Index}'s ceiling lowered to sector {sector}'s (collapsed expected)");
            lowest.CeilingHeight = old;
            m.UpdateSectors();
            if (Span(sector) is var (b2, t2) && t2 <= b2)
                Fail($"{map}: line {one.Line.Index}'s lid end collapsed with sector {lowest.Index}'s ceiling back");
        }
        _lidEnds += ends;
        _lidEndsShown += shown;
    }

    /// <summary>The drawn lid views (real renderer only); the lids are off for the other render checks.</summary>
    private async Task LidCheck(LevelMesh m)
    {
        string map = m.Level.Name;
        var settings = new CutawaySettings { Style = CutawayStyle.Cut, Cap = CutawayCap.Off, Doors = CutawayDoors.Cut };
        Basis basis = GameBasis(IsoCamera.DefaultPitch);
        Vector3 toCamera = Cutaway.ToMapAxes(basis.Z).Normalized();
        Vector2 toCameraFlat = new Vector2(toCamera.X, toCamera.Y).Normalized();
        Vector2I size = ViewSize();
        float[] lids = LidHeights(m);

        // Closed doors first, then any lid sector whose lid shows with room to open above the cutoff.
        IEnumerable<int> candidates = m.Lids.Sectors
            .Where(i => DoorLids.Shows(lids[i], m.Level.Sectors[i].CeilingHeight / 65536f)
                && lids[i] - m.Level.Sectors[i].FloorHeight / 65536f >= settings.Height + 16)
            .OrderBy(i => m.Level.Sectors[i].CeilingHeight > m.Level.Sectors[i].FloorHeight ? 1 : 0);
        // T6.13q: a door's view must also show the doors-kept rule (at least 50 pixels the cut view clears or caps
        // kept) and its lid (50 pixels with the cutaway off); a door with a wall of another sector cut in front of it
        // (E1M3's sector 2) is only a fallback, after one whose lid the walls hide (E1M3's sector 30).
        (Sector Door, Line Line, Vector3 Centre, CutClasses Cut, CutClasses? Kept)? hidden = null, fallback = null;
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
                if (!m.Lids.IsDoor(door.Index))
                {
                    await LidViews(m, door, line, centre, c, null, lids, settings, basis, toCamera, toCameraFlat);
                    return;
                }
                // T6.13d: with doors kept (the default), the same view keeps the door whole.
                CutClasses k = ClassifyCut(m, CutQuads(m), FloorHeights(m), centre, settings with { Doors = CutawayDoors.Keep }, toCamera, size.X, size.Y, lids);
                if (k.Capped == 0 && KeptPixels(c, k) < 50)
                {
                    fallback ??= (door, line, centre, c, null);
                    continue;
                }
                if (c.Lids < 50)
                {
                    hidden ??= (door, line, centre, c, k);
                    continue;
                }
                await LidViews(m, door, line, centre, c, k, lids, settings, basis, toCamera, toCameraFlat);
                return;
            }
        }
        if ((hidden ?? fallback) is var (fDoor, fLine, fCentre, fCut, fKept))
        {
            GD.Print(hidden is null
                ? $"Level check: {map}: lids: no door view keeps at least 50 pixels with doors kept (a wall of another sector is cut in front of each); "
                    + $"sector {fDoor.Index}'s view without its doors-kept view"
                : $"Level check: {map}: lids: no door view shows 50 lid pixels with the cutaway off and keeps 50 with doors kept; sector {fDoor.Index}'s keeps them");
            await LidViews(m, fDoor, fLine, fCentre, fCut, fKept, lids, settings, basis, toCamera, toCameraFlat);
            return;
        }
        if (m.Lids.Sectors.Count == 0)
            GD.Print($"Level check: {map}: lids: no lid sector");
        else
            Fail($"{map}: lids: no view found (a lid sector {settings.Height + 16} units deep below its lid, a two-sided line onto a sector away from the game camera, "
                + "with at least 50 pixels capped from 24 units behind it)");
    }

    /// <summary>The pixels the view with doors cut clears or caps (<paramref name="c"/>) and the one with doors kept keeps (<paramref name="k"/>).</summary>
    private static int KeptPixels(CutClasses c, CutClasses k)
    {
        int kept = 0;
        for (int p = 0; p < k.Classes.Length; p++)
        {
            if (k.Classes[p] == CutPixel.Keep && (c.Classes[p] is CutPixel.Cleared or CutPixel.Capped || c.CapClasses[p] == CutPixel.Capped))
                kept++;
        }
        return kept;
    }

    /// <summary>
    /// The lid views of <paramref name="door"/> from <paramref name="centre"/>
    /// behind <paramref name="line"/>: top-down, cut, with doors kept (when
    /// <paramref name="k"/> is given), half open, and from in front.
    /// </summary>
    private async Task LidViews(LevelMesh m, Sector door, Line line, Vector3 centre, CutClasses c, CutClasses? k, float[] lids,
        CutawaySettings settings, Basis basis, Vector3 toCamera, Vector2 toCameraFlat)
    {
        string map = m.Level.Name;
        Vector2I size = ViewSize();
        string what = $"{map}: lid of sector {door.Index} (lid at {lids[door.Index]:F0}, ceiling {door.CeilingHeight >> Fixed.FRACBITS}), centre ({centre.X:F0}, {centre.Y:F0}, {centre.Z:F0}) behind line {line.Index}";
        await CompareLidTopDown(m, door, lids, $"{map}: lid of sector {door.Index}");
        await CompareLidView(m, centre, c, settings, basis, toCamera, what);
        if (k is CutClasses kept)
        {
            int count = KeptPixels(c, kept);
            if (kept.Capped > 0 || count < 50)
                Fail($"{what}, doors kept: {kept.Capped} pixels capped (none expected), {count} pixels kept that the cut view clears or caps (at least 50 expected)");
            else
                await CompareLidView(m, centre, kept, settings with { Doors = CutawayDoors.Keep }, basis, toCamera, $"{what}, doors kept ({count} pixels kept that doors cut would clear or cap)");
        }
        else
            GD.Print($"Level check: {what}: {(m.Lids.IsDoor(door.Index) ? "no view with doors kept that keeps 50 pixels" : "not a door, no view with doors kept")}");

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
        await LidInFrontView(m, door, lids, settings, basis, toCamera, toCameraFlat);
        _lidMaps++;
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
    /// The same door seen from a centre 24 units in front of it, on the
    /// camera's side: its lid lies behind the centre and cannot hide it, so
    /// the cutaway must keep it whole, though it is above the cutoff inside
    /// the disc (a floor behind the anchor is not cut).
    /// </summary>
    private async Task LidInFrontView(LevelMesh m, Sector door, float[] lids, CutawaySettings settings, Basis basis, Vector3 toCamera, Vector2 toCameraFlat)
    {
        string map = m.Level.Name;
        Vector2I size = ViewSize();
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
            if (n.Dot(toCameraFlat) < 0.5f)
                continue; // the centre must be between the door and the camera
            Vector2 mid = (v1 + v2) / 2;
            var centre = new Vector3(mid.X + n.X * 24, mid.Y + n.Y * 24, other.FloorHeight / 65536f);
            if (CursorGround.DrawnSectorAt(m, (int)(centre.X * 65536), (int)(centre.Y * 65536)) != other.Index || door.FloorHeight > other.FloorHeight)
                continue;
            CutView(centre, basis, toCamera);
            CutClasses c = ClassifyCut(m, CutQuads(m), FloorHeights(m), centre, settings, toCamera, size.X, size.Y, lids);
            // Lid pixels above the cutoff inside the disc, which the cutaway keeps only because the lid is behind the centre.
            int behind = LidPixelsInDisc(c, lids, centre, settings, toCamera);
            string what = $"{map}: lid of sector {door.Index} (lid at {lids[door.Index]:F0}), centre ({centre.X:F0}, {centre.Y:F0}, {centre.Z:F0}) in front of line {line.Index}";
            if (behind < 50)
            {
                GD.Print($"Level check: {what}: only {behind} lid pixels in the disc, skipped");
                continue;
            }
            GD.Print($"Level check: {what}: {behind} lid pixels in the disc behind the centre, kept");
            await CompareLidView(m, centre, c, settings, basis, toCamera, what);
            return;
        }
        GD.Print($"Level check: {map}: lid of sector {door.Index}: no view from in front of it");
    }

    /// <summary>
    /// The lids' ends (T6.13f) at the sectors' current heights, as the
    /// classes cast rays against them: each piece of a lid sector's one-sided
    /// wall (<see cref="DoorLids.HasEnd"/>) whose lid shows, from its
    /// ceiling up to <paramref name="lids"/>' height, facing the void (its
    /// normal away from the sector).
    /// </summary>
    private static List<CutQuad> LidEndQuads(LevelMesh m, float[] lids)
    {
        var quads = new List<CutQuad>();
        foreach (WallSection s in m.Walls.Sections)
        {
            if (!m.Lids.HasEnd(s))
                continue;
            int sector = s.FrontSector.Index;
            float ceiling = s.FrontSector.CeilingHeight / 65536f;
            if (!DoorLids.Shows(lids[sector], ceiling) || m.LidData(sector).Slot < 0)
                continue;
            foreach (WallPiece pc in m.Pieces.Of(s.Line, s.Side))
            {
                var a = new Vector2(pc.A.X / 65536f, pc.A.Y / 65536f);
                var b = new Vector2(pc.B.X / 65536f, pc.B.Y / 65536f);
                float len = a.DistanceTo(b);
                if (len < 1e-3f)
                    continue;
                Vector2 dir = (b - a) / len;
                quads.Add(new CutQuad(a, dir, len, new Vector3(-dir.Y, dir.X, 0), ceiling, lids[sector], false, s));
            }
        }
        return quads;
    }

    /// <summary>
    /// The colour a lid's end (T6.13f) draws at <paramref name="e"/>: its
    /// wall's texture at the column along its line and the row its texture
    /// top gives (going on upwards past the ceiling), lit as the wall (its
    /// sector's light, fake contrast); null within a texel's edge or a light
    /// table step.
    /// </summary>
    private (int R, int G, int B)? LidEndPixel(LevelMesh m, EndPoint e)
    {
        WallSection s = e.Section!;
        Line line = s.Line;
        double lx = line.Dx / 65536.0, ly = line.Dy / 65536.0, length = Math.Sqrt(lx * lx + ly * ly);
        double column = s.TextureOffset / 65536.0 + ((e.X - line.V1.X / 65536.0) * lx + (e.Y - line.V1.Y / 65536.0) * ly) / length;
        IsoDoom.Wad.Graphics.IndexedImage tex = ShownComposite(m, s.Texture);
        double row = s.TextureTopFor(tex.Height << Fixed.FRACBITS) / 65536.0 - e.Z;
        if (Math.Abs(column - Math.Round(column)) < CapTexelMargin || Math.Abs(row - Math.Round(row)) < CapTexelMargin)
            return null;
        int colormap = ExpectedColormap(m, true, s.FrontSector.LightLevel, m.Pieces.Of(line, s.Side)[0].Contrast, e.X, e.Y, e.Depth);
        if (colormap < 0)
            return null;
        (int tc, int tr) = TextureWrap.WallTexel((int)Math.Floor(column), (int)Math.Floor(row), tex.Width, tex.Height, WallTextureTiling.Vanilla);
        return Shade(tex[tc, tr], colormap);
    }

    /// <summary>
    /// The lids' ends (T6.13f, real renderer only): a closed lid sector's
    /// one-sided wall facing away from the game camera (it has no cap, T6.13e;
    /// its end faces the camera), seen along the game camera's direction at 1
    /// unit per pixel with a cut centre inside the sector beside it: with the
    /// cutaway off, the pixels whose first surface is definitely its end (or
    /// another's) show the wall's texture going on upwards, lit as the wall,
    /// the rest as with the lids off (<see cref="CompareLidView"/>); then the
    /// cutaway's comparisons with the doors cut (the end cut above the cutoff
    /// as walls are, the lid's solid capped) and kept. At least 50 end pixels.
    /// </summary>
    private async Task LidEndCheck(LevelMesh m)
    {
        string map = m.Level.Name;
        var settings = new CutawaySettings { Style = CutawayStyle.Cut, Cap = CutawayCap.Off, Doors = CutawayDoors.Cut };
        Basis basis = GameBasis(IsoCamera.DefaultPitch);
        Vector3 toCamera = Cutaway.ToMapAxes(basis.Z).Normalized();
        Vector2 toCameraFlat = new Vector2(toCamera.X, toCamera.Y).Normalized();
        Vector2I size = ViewSize();
        float[] lids = LidHeights(m);
        int candidates = 0;
        foreach (WallSection s in m.Walls.Sections.Where(s => m.Lids.HasEnd(s))
            .OrderByDescending(s => lids[s.FrontSector.Index] - s.FrontSector.CeilingHeight / 65536f))
        {
            Sector sector = s.FrontSector;
            float ceiling = sector.CeilingHeight / 65536f, floor = sector.FloorHeight / 65536f;
            var v1 = new Vector2(s.Line.V1.X / 65536f, s.Line.V1.Y / 65536f);
            var v2 = new Vector2(s.Line.V2.X / 65536f, s.Line.V2.Y / 65536f);
            float len = v1.DistanceTo(v2);
            Vector2 dir = (v2 - v1) / len, n = new(dir.Y, -dir.X); // towards the sector
            if (len < 8 || lids[sector.Index] - ceiling < settings.Height + 16 || n.Dot(toCameraFlat) > -0.3f)
                continue; // a closed lid, deep enough to cut, its wall facing away from the camera
            if (candidates++ >= 40)
                break;
            // The centre inside the sector, 4 units in from the wall's middle, on its floor.
            Vector2 mid = (v1 + v2) / 2 + n * 4;
            var centre = new Vector3(mid.X, mid.Y, floor);
            CutView(centre, basis, toCamera);
            CutClasses c = ClassifyCut(m, CutQuads(m), FloorHeights(m), centre, settings, toCamera, size.X, size.Y, lids);
            if (c.Ends < 50)
                continue;
            string what = $"{map}: line {s.Line.Index}'s lid end (sector {sector.Index}, ceiling {ceiling:F0} to lid {lids[sector.Index]:F0}), centre ({centre.X:F0}, {centre.Y:F0}, {centre.Z:F0})";
            GD.Print($"Level check: {what}: {c.Ends} pixels whose first surface is a lid's end, {c.Capped} capped");
            await CompareLidView(m, centre, c, settings, basis, toCamera, what);
            CutClasses k = ClassifyCut(m, CutQuads(m), FloorHeights(m), centre, settings with { Doors = CutawayDoors.Keep }, toCamera, size.X, size.Y, lids);
            await CompareLidView(m, centre, k, settings with { Doors = CutawayDoors.Keep }, basis, toCamera, $"{what}, doors kept");
            return;
        }
        if (candidates == 0)
            GD.Print($"Level check: {map}: lid ends: no closed lid sector with a one-sided wall facing away from the game camera");
        else
            Fail($"{map}: lid ends: no view found (a closed lid sector's one-sided wall facing away from the game camera with at least 50 pixels of its end shown)");
    }

    /// <summary>The pixels whose first surface is definitely a lid above the cutoff inside the disc (not cut, or the lid would not be first).</summary>
    private static int LidPixelsInDisc(CutClasses c, float[] lids, Vector3 centre, CutawaySettings settings, Vector3 toCamera)
    {
        var anchor = new Vector3(centre.X, centre.Y, centre.Z + Cutaway.Anchor);
        int count = 0;
        for (int i = 0; i < c.LidStates.Length; i++)
        {
            if (c.LidStates[i] != 1)
                continue;
            CapPoint p = c.LidPoints[i];
            var q = new Vector3((float)p.X, (float)p.Y, lids[p.Sector]);
            if (q.Z > centre.Z + settings.Height && Cutaway.Distance(q, anchor, toCamera) < settings.Radius - CutMargin)
                count++;
        }
        return count;
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
        (int Left, int Top, int Right, int Bottom) box = classes.Box;
        int bw = box.Right - box.Left;
        int compared = 0, lidPixels = 0, badLid = 0, badKeep = 0, endPixels = 0, badEnd = 0;
        string firstLid = "", firstKeep = "", firstEnd = "";
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
                else if (classes.LidStates[i] == 3 && LidEndPixel(m, classes.EndPoints[i]) is { } expected)
                {
                    // T6.13f: a lid's end, its wall's texture going on upwards, lit as the wall.
                    compared++;
                    endPixels++;
                    EndPoint e = classes.EndPoints[i];
                    if (got != expected && badEnd++ == 0)
                        firstEnd = $"pixel ({px}, {py}), map ({e.X:F2}, {e.Y:F2}, {e.Z:F2}) on line {e.Section!.Line.Index}'s lid end: drew {got}, expected {expected}";
                }
            }
        }
        _pixels += compared;
        if (badLid > 0)
            Fail($"{what}: {badLid} of {lidPixels} lid pixel(s) differ, first at {firstLid}");
        if (badEnd > 0)
            Fail($"{what}: {badEnd} of {endPixels} lid end pixel(s) differ, first at {firstEnd}");
        if (badKeep > 0)
            Fail($"{what}: {badKeep} pixel(s) whose ray meets no lid changed with the lids on, first at {firstKeep}");
        GD.Print($"Level check: {what}: {compared} pixels compared with the cutaway off, {lidPixels} of them on a lid, {endPixels} on a lid's end, the rest as with the lids off");
        await CompareCutView(m, centre, classes, settings, basis, toCamera, $"{what}, lids on");
    }
}
