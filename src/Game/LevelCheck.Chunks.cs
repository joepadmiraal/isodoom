using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Map;
using IsoDoom.Render;

namespace IsoDoom.Game;

/// <summary>
/// The level check's discard-free chunks (T8.4i, real renderer only): the
/// chunks no cut centre reaches draw with <see cref="LevelMesh.NoDiscardMaterial"/>
/// (<see cref="LevelMesh.ChooseChunkMaterials"/>, just before each frame).
/// From centres spread over the map (the player's and a cursor's, the cut
/// dithered and capped), seen from the game camera's angle in orthographic
/// and in perspective: each chunk's first surface has the material its
/// choice says, the choice is <see cref="LevelMesh.ChunkMayBeCut"/>'s, no
/// point of a discard-free chunk's floors (from its floor up to its top)
/// lies in a centre's disc above its cutoff (the CPU's rule, sampled), and
/// the frame is pixel for pixel the one drawn with the discard-free chunks'
/// material given the discarding shader (the same draw order; with every
/// chunk discarding, <see cref="LevelMesh.SplitChunks"/> off, a depth tie
/// between coplanar floors at a seam may go the other way: counted, SPEC
/// §12 T8.4i). Some chunk must switch
/// material between the views; while the fog dithers none is discard-free.
/// </summary>
public partial class LevelCheck
{
    /// <summary>The level shader with its discards, as a shader of its own (<see cref="ChunkSplitCheck"/>'s reference).</summary>
    private const string DiscardCopyCode = "shader_type spatial;\nrender_mode unshaded, cull_back, depth_draw_opaque, shadows_disabled, fog_disabled;\n#include \"res://shaders/level.gdshaderinc\"\n";

    private Shader? _discardCopy;

    /// <summary>How many centres the check spreads over the map.</summary>
    private const int ChunkSplitSpots = 10;

    private async Task ChunkSplitCheck(LevelMesh m)
    {
        string map = m.Level.Name;
        MeshInstance3D?[] chunks = _scene.Chunks;
        var settings = new CutawaySettings { Style = CutawayStyle.Dither, Cap = CutawayCap.Dark, Doors = CutawayDoors.Cut };
        Basis basis = GameBasis(IsoCamera.DefaultPitch);
        Vector3 toCamera = Cutaway.ToMapAxes(basis.Z).Normalized();

        // Centres: the player's start, then floors spread over the sector list.
        var spots = new List<Vector3>();
        if (m.Level.PlayerStart(0) is MapThing start)
            spots.Add(new Vector3(start.X, start.Y, m.Level.R_PointInSubsector(start.X << 16, start.Y << 16).Sector.FloorHeight / 65536f));
        int step = Math.Max(1, m.Floors.BySector.Count / ChunkSplitSpots);
        for (int i = 0; i < m.Floors.BySector.Count && spots.Count < ChunkSplitSpots; i += step)
        {
            SectorFloor floor = m.Floors.BySector[i];
            if (floor.TriangleCount == 0)
                continue;
            (int x, int y) = floor.InteriorPoint();
            spots.Add(new Vector3(x / 65536f, y / 65536f, m.Level.Sectors[floor.Sector].FloorHeight / 65536f));
        }

        var plainSeen = new bool[chunks.Length];
        var cutSeen = new bool[chunks.Length];
        int views = 0, plainChunks = 0, cutChunks = 0, samples = 0, seamPixels = 0;
        try
        {
            m.SetCutaway(settings);
            foreach (Vector3 centre in spots)
            {
                var cursor = new Vector3(centre.X + 96, centre.Y - 64, centre.Z);
                foreach (bool perspective in new[] { false, true })
                {
                    Vector3 anchor = centre + new Vector3(0, 0, Cutaway.Anchor);
                    Vector3? eye = null;
                    if (perspective)
                    {
                        eye = anchor + toCamera * 900;
                        Camera3D cam = _scene.Camera;
                        cam.Projection = Camera3D.ProjectionType.Perspective;
                        cam.Fov = 40;
                        cam.Near = 1 / LevelMesh.MapUnitsPerMetre;
                        cam.Far = 8192 / LevelMesh.MapUnitsPerMetre;
                        Vector3 e = eye.Value;
                        cam.GlobalTransform = new Transform3D(basis, new Vector3(e.X, e.Z, -e.Y) / LevelMesh.MapUnitsPerMetre);
                        cam.Current = true;
                    }
                    else
                    {
                        CutView(centre, basis, toCamera);
                    }
                    m.SetCutawayCentres(centre, cursor);
                    string what = $"{map}: discard-free chunks (T8.4i), centre ({centre.X:F0}, {centre.Y:F0}, {centre.Z:F0}), {(perspective ? "perspective" : "orthographic")}";
                    m.SplitChunks = true;
                    byte[]? split = await Capture($"{what}, split");
                    if (split is null)
                        return;
                    // The choice as made for this frame (the camera and centres unchanged since).
                    Transform3D t = _scene.Camera.GlobalTransform;
                    Vector3 eyeMap = Cutaway.ToMapAxes(t.Origin) * LevelMesh.MapUnitsPerMetre;
                    Vector3 back = Cutaway.ToMapAxes(t.Basis.Z).Normalized();
                    for (int s = 0; s < chunks.Length; s++)
                    {
                        if (chunks[s] is not MeshInstance3D node || !m.ChunkHasSolid(s))
                            continue;
                        bool plain = m.ChunkDrawsPlain(s);
                        if (plain == m.ChunkMayBeCut(s, eyeMap, back, !perspective))
                            Fail($"{what}: chunk {s} draws {(plain ? "without" : "with")} discard, against LevelMesh.ChunkMayBeCut");
                        Material? over = node.GetSurfaceOverrideMaterial(0);
                        if (over != (plain ? m.NoDiscardMaterial : null))
                            Fail($"{what}: chunk {s}'s first surface's material is not the one chosen ({(plain ? "without" : "with")} discard)");
                        (plain ? plainSeen : cutSeen)[s] = true;
                        if (plain)
                        {
                            plainChunks++;
                            samples += CheckPlainChunk(m, s, centre, settings, toCamera, eye, what);
                            samples += CheckPlainChunk(m, s, cursor, settings, toCamera, eye, what);
                        }
                        else
                        {
                            cutChunks++;
                        }
                    }
                    // The reference: the same chunks in the same draw order, the discard-free material given the
                    // discarding shader (a copy made after it, so it sorts as the discard-free one: Godot draws the
                    // opaque surfaces by shader, then material, then mesh, and a depth tie between coplanar floors at a
                    // seam goes by that order).
                    Shader plainShader = m.NoDiscardMaterial.Shader;
                    m.NoDiscardMaterial.Shader = _discardCopy ??= new Shader { Code = DiscardCopyCode };
                    byte[]? reference = await Capture($"{what}, the discard-free chunks discarding");
                    m.NoDiscardMaterial.Shader = plainShader;
                    m.SplitChunks = false;
                    byte[]? whole = await Capture($"{what}, every chunk discarding");
                    m.SplitChunks = true;
                    if (reference is null || whole is null)
                        return;
                    int differ = 0, first = -1;
                    for (int p = 0; p < split.Length; p += 4)
                    {
                        if (split[p] != reference[p] || split[p + 1] != reference[p + 1] || split[p + 2] != reference[p + 2])
                        {
                            differ++;
                            first = first < 0 ? p : first;
                        }
                        if (split[p] != whole[p] || split[p + 1] != whole[p + 1] || split[p + 2] != whole[p + 2])
                            seamPixels++;
                    }
                    if (differ > 0)
                    {
                        int w = ViewSize().X;
                        Fail($"{what}: {differ} pixel(s) differ from the frame with the discard-free chunks discarding, first at ({first / 4 % w}, {first / 4 / w}): "
                            + $"({split[first]}, {split[first + 1]}, {split[first + 2]}) against ({reference[first]}, {reference[first + 1]}, {reference[first + 2]})");
                    }
                    views++;
                }
            }

            // While the fog dims with its dither, every chunk discards.
            FogStyle fog = m.FogMode;
            FogDim look = m.FogDimLook;
            m.SetFog(FogStyle.Dim);
            m.SetFogDim(look with { Dither = true });
            await Capture($"{map}: discard-free chunks (T8.4i), the fog dithering");
            for (int s = 0; s < chunks.Length; s++)
            {
                if (chunks[s] is not null && m.ChunkHasSolid(s) && m.ChunkDrawsPlain(s))
                    Fail($"{map}: discard-free chunks (T8.4i): chunk {s} draws without discard while the fog dithers");
            }
            m.SetFogDim(look);
            m.SetFog(fog);
        }
        finally
        {
            m.SplitChunks = true;
            m.SetCutaway(new CutawaySettings { Style = CutawayStyle.Off });
            m.SetCutawayCentres(null, null);
        }

        int switched = 0;
        for (int s = 0; s < chunks.Length; s++)
            switched += plainSeen[s] && cutSeen[s] ? 1 : 0;
        if (switched == 0)
            Fail($"{map}: discard-free chunks (T8.4i): no chunk switched material between the {views} views");
        GD.Print($"Level check: {map}: discard-free chunks (T8.4i): {views} views from {spots.Count} centres (orthographic and perspective, with a cursor centre), "
            + $"{plainChunks} chunks drawn without discard and {cutChunks} with, {switched} switching between views; every frame the same as with the discard-free chunks discarding "
            + $"(and {seamPixels} pixel(s) of all the views not as with every chunk discarding: depth ties at seams, in another draw order); {samples} points of the discard-free chunks outside every disc; none without discard while the fog dithers");
    }

    /// <summary>
    /// The CPU's rule over discard-free chunk <paramref name="sector"/>: no
    /// point of its floors' vertices, from the cutoff up to its top (its
    /// sector's floor, ceiling and lid as uploaded), lies within the radius of
    /// <paramref name="centre"/>'s anchor line (orthographic along
    /// <paramref name="toCamera"/>, or towards <paramref name="eye"/>).
    /// Returns the points tested.
    /// </summary>
    private int CheckPlainChunk(LevelMesh m, int sector, Vector3 centre, CutawaySettings settings, Vector3 toCamera, Vector3? eye, string what)
    {
        Color data = m.SectorData(sector);
        float top = Math.Max(Math.Max(data.R, data.G), m.LidData(sector).Height);
        float cutoff = centre.Z + settings.Height;
        if (top < cutoff)
            return 0;
        var a = new Vector3(centre.X, centre.Y, centre.Z + Cutaway.Anchor);
        Vector3 axis = eye is Vector3 e ? (e - a).Normalized() : toCamera;
        int tested = 0;
        foreach (SectorFloor floor in m.Floors.BySector)
        {
            if (floor.Sector != sector)
                continue;
            foreach (PolygonVertex v in floor.Vertices)
            {
                for (int k = 0; k <= 4; k++)
                {
                    var p = new Vector3(v.X / 65536f, v.Y / 65536f, cutoff + (top - cutoff) * k / 4);
                    tested++;
                    if (Cutaway.Distance(p, a, axis) < settings.Radius)
                    {
                        Fail($"{what}: chunk {sector} draws without discard, but its point {p} is in the disc of the centre at {centre}");
                        return tested;
                    }
                }
            }
        }
        return tested;
    }
}
