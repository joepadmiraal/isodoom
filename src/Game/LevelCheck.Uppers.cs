using System;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Map;
using IsoDoom.Render;

namespace IsoDoom.Game;

/// <summary>
/// The level check's upper wall part (SPEC §12, <see cref="UpperWallMode"/>),
/// with a real renderer: the largest upper wall over a sector that does not
/// keep it (<see cref="DoorLids.KeepsUppers"/>) side-on, drawn with every
/// upper wall and nothing (the background) with only the doors' kept; and the
/// largest one over a sector that keeps it (a door, a lintel), the same both ways.
/// </summary>
public partial class LevelCheck
{
    private async Task UpperWallCheck(LevelMesh m)
    {
        string map = m.Level.Name;
        Vector2I size = ViewSize();
        WallSection? dropped = null, kept = null;
        float droppedArea = 0, keptArea = 0;
        foreach (WallSection s in m.Walls.Sections)
        {
            if (s.Kind != WallSectionKind.Upper || s.BackSector is not Sector back || !LevelMesh.IsDrawn(s))
                continue;
            (int bottom, int top) = s.Span();
            float height = (top - bottom) / 65536f, length = SectionLength(s);
            if (height <= 8 || length <= 8 || height >= size.Y - 8 || length >= size.X - 8)
                continue;
            if (m.Lids.KeepsUppers(back.Index, n => m.Level.Sectors[n].CeilingHeight / 65536f))
            {
                if (height * length > keptArea)
                    (kept, keptArea) = (s, height * length);
            }
            else if (height * length > droppedArea)
            {
                (dropped, droppedArea) = (s, height * length);
            }
        }
        if (dropped is null)
            GD.Print($"Level check: {map}: no upper wall over a sector that does not keep it");
        else
            await CompareUpperWall(m, dropped, true);
        if (kept is null)
            GD.Print($"Level check: {map}: no upper wall over a sector that keeps it");
        else
            await CompareUpperWall(m, kept, false);
        m.SetUpperWalls(UpperWallMode.All);
    }

    private static float SectionLength(WallSection s) =>
        (float)Math.Sqrt((double)s.Line.Dx * s.Line.Dx + (double)s.Line.Dy * s.Line.Dy) / 65536f;

    // An upper wall side-on at 1 unit per pixel from just in front of it (the near and far
    // planes keep only its plane), with every upper wall and with the doors' only: with
    // dropped, every pixel of it is drawn, then the background; else the same both ways.
    private async Task CompareUpperWall(LevelMesh m, WallSection wall, bool dropped)
    {
        string map = m.Level.Name;
        Vector2I size = ViewSize();
        int w = size.X, h = size.Y;
        (int b, int t) = wall.Span();
        float bottom = b / 65536f, top = t / 65536f;
        // Seen from its front sector: V1 on the left for the line's front side, V2 for its back.
        (Vertex a, Vertex c) = wall.Side == 0 ? (wall.Line.V1, wall.Line.V2) : (wall.Line.V2, wall.Line.V1);
        double x1 = a.X / 65536.0, y1 = a.Y / 65536.0, x2 = c.X / 65536.0, y2 = c.Y / 65536.0;
        double length = SectionLength(wall);
        double dx = (x2 - x1) / length, dy = (y2 - y1) / length, nx = dy, ny = -dx; // towards the front sector
        var basis = new Basis(new Vector3((float)dx, 0, (float)-dy), new Vector3(0, 1, 0), new Vector3((float)nx, 0, (float)-ny));
        double mx = (x1 + x2) / 2, my = (y1 + y2) / 2, mz = Math.Floor((bottom + top) / 2);
        Ortho(basis, new Vector3((float)(mx + 2 * nx), (float)(my + 2 * ny), (float)mz), 1, 3);
        string what = $"{map}: line {wall.Line.Index} side {wall.Side}'s upper wall ({bottom} to {top}) over sector {wall.BackSector!.Index}";
        m.SetUpperWalls(UpperWallMode.All);
        byte[]? all = await Capture($"{what}, every upper wall");
        m.SetUpperWalls(UpperWallMode.Doors);
        byte[]? doors = await Capture($"{what}, the doors' upper walls");
        if (all is null || doors is null)
            return;

        int compared = 0, bad = 0;
        string first = "";
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
                int i = (py * w + px) * 4;
                (int R, int G, int B) withAll = (all[i], all[i + 1], all[i + 2]), withDoors = (doors[i], doors[i + 1], doors[i + 2]);
                compared++;
                bool ok = dropped ? !NearBackground(withAll) && NearBackground(withDoors) : withAll == withDoors;
                if (!ok && bad++ == 0)
                    first = $"pixel ({px}, {py}), z {z}: drew {withAll} with every upper wall, {withDoors} with the doors' only";
            }
        }
        _pixels += compared;
        string expected = dropped ? "drawn with every upper wall and dropped with the doors' only" : "kept the same both ways";
        if (bad > 0)
            Fail($"{what}: {bad} of {compared} pixel(s) not {expected}, first at {first}");
        if (compared < 50)
            Fail($"{what}: only {compared} pixels compared (at least 50 expected)");
        GD.Print($"Level check: {what}, side-on: {compared} pixels {expected}");
    }
}
