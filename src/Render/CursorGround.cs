using System;
using System.Collections.Generic;
using Godot;
using IsoDoom.Map;

namespace IsoDoom.Render;

/// <summary>
/// Where the mouse ray meets the floor (T3.3, SPEC §7.1): the <b>cursor ground
/// point</b> the look-ahead and (T4.6) the aim use.
/// <para>
/// The ray is tested against the drawn sector floors: for each distinct
/// floor height from the highest down, the ray's point at that height is
/// looked up in the floor triangles (<see cref="DrawnSectorAt"/>);
/// the first point whose drawn floor is at that height is the hit. The ray
/// descends, so it reaches a higher plane first: the highest such height is
/// the first floor along the ray. Walls are ignored (the cutaway, T3.4, opens
/// them around the player anyway). Over the void, or when the ray points
/// upwards, the point falls back to the plane at the target's floor height.
/// </para>
/// </summary>
public static class CursorGround
{
    /// <summary>A cursor ground point: Godot-space position, the sector whose floor was hit (−1 for the fallback plane).</summary>
    public readonly record struct Hit(Vector3 Point, int Sector)
    {
        /// <summary>Whether the ray hit a drawn floor (else it is on the fallback plane).</summary>
        public bool OnFloor => Sector >= 0;

        /// <summary>The point in map units (x, y, height).</summary>
        public Vector3 MapUnits => new(Point.X * LevelMesh.MapUnitsPerMetre, -Point.Z * LevelMesh.MapUnitsPerMetre, Point.Y * LevelMesh.MapUnitsPerMetre);
    }

    /// <summary>
    /// The cursor ground point for the ray from <paramref name="origin"/> along
    /// <paramref name="direction"/> (Godot space), against <paramref name="mesh"/>'s
    /// floors at their current heights, falling back to the plane at
    /// <paramref name="fallbackHeight"/> (map units). Null when the ray misses
    /// even that plane.
    /// </summary>
    public static Hit? Pick(LevelMesh mesh, Vector3 origin, Vector3 direction, float fallbackHeight)
    {
        if (direction.Y < 0)
        {
            var heights = new SortedSet<int>();
            foreach (Sector s in mesh.Level.Sectors)
                heights.Add(s.FloorHeight);
            foreach (int h in heights.Reverse())
            {
                if (AtHeight(origin, direction, h / 65536f) is not Vector3 p)
                    continue;
                int sector = DrawnSectorAt(mesh, ToFixed(p.X), ToFixed(-p.Z));
                if (sector >= 0 && mesh.Level.Sectors[sector].FloorHeight == h)
                    return new Hit(p, sector);
            }
        }
        return AtHeight(origin, direction, fallbackHeight) is Vector3 f ? new Hit(f, -1) : null;
    }

    /// <summary>The cursor ground point under viewport pixel <paramref name="screen"/> of <paramref name="camera"/>.</summary>
    public static Hit? Pick(LevelMesh mesh, Camera3D camera, Vector2 screen, float fallbackHeight) =>
        Pick(mesh, camera.ProjectRayOrigin(screen), camera.ProjectRayNormal(screen), fallbackHeight);

    /// <summary>The ray's point at height <paramref name="units"/> (map units), or null when it never gets there going forwards.</summary>
    public static Vector3? AtHeight(Vector3 origin, Vector3 direction, float units)
    {
        float y = units / LevelMesh.MapUnitsPerMetre;
        if (Math.Abs(direction.Y) < 1e-6f)
            return null;
        float t = (y - origin.Y) / direction.Y;
        if (t < 0)
            return null;
        Vector3 p = origin + direction * t;
        return new Vector3(p.X, y, p.Z);
    }

    /// <summary>
    /// The sector whose floor is drawn at (<paramref name="x"/>, <paramref name="y"/>)
    /// (fixed_t), or −1: as <see cref="FloorTriangles.SectorAt"/>, but only
    /// the triangles of the floor the BSP's subsector there is drawn in are
    /// tested (a drawn polygon lies inside its BSP leaf), so a lookup costs one
    /// sector's triangles instead of the whole map's.
    /// </summary>
    public static int DrawnSectorAt(LevelMesh mesh, int x, int y)
    {
        Subsector ss = mesh.Level.R_PointInSubsector(x, y);
        int sector = mesh.Floors.FloorSectorOf[ss.Index];
        if (sector < 0)
            return -1;
        SectorFloor floor = mesh.Floors.BySector[sector];
        var p = new PolygonVertex(x, y);
        for (int t = 0; t < floor.TriangleCount; t++)
        {
            PolygonVertex a = floor.Corner(t, 0), b = floor.Corner(t, 1), c = floor.Corner(t, 2);
            if (FloorTriangles.TwiceArea(a, b, p) >= 0 && FloorTriangles.TwiceArea(b, c, p) >= 0 && FloorTriangles.TwiceArea(c, a, p) >= 0)
                return sector;
        }
        return -1;
    }

    /// <summary>Godot metres to fixed_t map units, clamped to the fixed_t range.</summary>
    private static int ToFixed(float metres) =>
        (int)Math.Clamp(Math.Round(metres * LevelMesh.MapUnitsPerMetre * 65536.0), int.MinValue, int.MaxValue);
}
