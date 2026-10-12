using System;
using System.Collections.Generic;
using Godot;
using IsoDoom.Sim;

namespace IsoDoom.Render;

/// <summary>How the monster markers (T6.13u, <see cref="MonsterMarkers"/>) look: none, a chevron over the hiding wall, or a beam as the item beams' (T6.13r).</summary>
public enum MonsterMarkerMode
{
    Off,
    Chevron,
    Beam,
}

/// <summary>
/// Monster markers (T6.13u, not vanilla, SPEC §12): under the game camera a
/// wall hides what stands up to about 0.7 × its height behind it, so a monster
/// that a vanilla player in the room would see, and that can see and attack
/// the player, may be hidden whole. Each marked monster (the owner's choice:
/// drawn, alive, with a line of sight to the player, vanilla's
/// <c>P_CheckSight</c>, T6.13t) gets a small chevron pointing down at it,
/// drawn by <see cref="ShaderPath"/>, or (<see cref="MonsterMarkerMode.Beam"/>)
/// an item beam in <see cref="MarkerColor"/>, rising from behind the wall
/// (<see cref="ItemBeams.ShaderPath"/>), only while its billboard is hidden whole
/// (the item beams' test, <c>shaders/billboard_hidden.gdshaderinc</c>, with
/// the monster itself left out of the actors near it), on top of what hides
/// it. One <see cref="MultiMesh"/> instance per marker. Presentation only: the
/// sim never reads them.
/// </summary>
public partial class MonsterMarkers : MultiMeshInstance3D
{
    public const string ShaderPath = "res://shaders/monster_marker.gdshader";

    /// <summary>The markers' colour (sRGB).</summary>
    public static readonly Color MarkerColor = new(0.95f, 0.12f, 0.08f);

    /// <summary>
    /// One marker: its monster, the monster's foot (map units: x, y, height),
    /// the atlas slot its billboard shows (−1: no marker) and whether it is
    /// mirrored, its radius (the wall pull, as its billboard's), its sector,
    /// and its index in the actors given to <see cref="Set"/> (−1: none).
    /// </summary>
    public readonly record struct Marker(mobj_t Mobj, Vector3 MapPosition, int Slot, bool Flip, float Radius, int Sector, int Actor);

    private readonly ShaderMaterial _chevron, _beam;
    private readonly NearActors _actors = new();
    private Marker[] _markers = [];
    private SpriteSettings? _sprites;
    private MonsterMarkerMode _mode = MonsterMarkerMode.Chevron;

    /// <summary>The markers as last set.</summary>
    public IReadOnlyList<Marker> Markers => _markers;

    public MonsterMarkers()
    {
        CastShadow = ShadowCastingSetting.Off;
        _chevron = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath) };
        _chevron.SetShaderParameter("thing_actors", _actors.Texture);
        _beam = new ShaderMaterial { Shader = GD.Load<Shader>(ItemBeams.ShaderPath) };
        _beam.SetShaderParameter("thing_actors", _actors.Texture);
        _beam.SetShaderParameter("beam_height", ItemBeams.Height);
        _beam.SetShaderParameter("beam_half_width", ItemBeams.HalfWidth);
        MaterialOverride = _chevron;
        Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = QuadMesh(),
        };
    }

    /// <summary>
    /// The markers' look: a chevron or a beam (the owner sets no markers when
    /// off). Both shaders take the same instances: the beam's quad is the
    /// chevron's, x −1…1 across, y 0 (foot) … 1 (top).
    /// </summary>
    public MonsterMarkerMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            MaterialOverride = value == MonsterMarkerMode.Beam ? _beam : _chevron;
        }
    }

    /// <summary>The marker mesh: a quad, x −1…1 across, y 0 (tip, or the beam's foot) … 1 (top); the shader sizes and places it (the chevron up to 320 units above the foot, the beam <see cref="ItemBeams.Height"/>).</summary>
    private static ArrayMesh QuadMesh()
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new[] { new Vector3(-1, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 2, 1, 0, 3, 2 };
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        const float Reach = 320f / LevelMesh.MapUnitsPerMetre;
        mesh.CustomAabb = new Aabb(new Vector3(-Reach, -Reach, -Reach), new Vector3(2 * Reach, 2 * Reach, 2 * Reach));
        return mesh;
    }

    /// <summary>Uses <paramref name="atlas"/>'s patches for the hidden test (the things' atlas, <see cref="ThingSprites.Bind"/>).</summary>
    public void Bind(SpriteAtlas atlas)
    {
        foreach (ShaderMaterial material in new[] { _chevron, _beam })
        {
            material.SetShaderParameter("sprite_atlas", atlas.AtlasTexture);
            material.SetShaderParameter("sprite_info", atlas.InfoTexture);
        }
    }

    /// <summary>Takes the sprites' tilt, tilt depth, wall pull and upright hiding, so the hidden test puts the billboard where it is drawn.</summary>
    public void SetSprites(SpriteSettings settings)
    {
        if (settings == _sprites)
            return;
        _sprites = settings;
        foreach (ShaderMaterial material in new[] { _chevron, _beam })
        {
            material.SetShaderParameter("tilt", Math.Clamp(settings.Tilt, 0f, 1f));
            material.SetShaderParameter("tilt_depth", (int)settings.TiltDepth);
            material.SetShaderParameter("wall_pull", Math.Max(settings.WallPull, 0f));
            material.SetShaderParameter("upright_hiding", settings.Hidden == SpriteHidden.Upright ? 1 : 0);
        }
    }

    /// <summary>
    /// Replaces the markers (one instance each), writing only the instances
    /// that changed, with the actors of <paramref name="actors"/> nearest
    /// each monster (but itself), whose billboards do not hide it.
    /// </summary>
    public void Set(IReadOnlyList<Marker> markers, IReadOnlyList<ItemBeams.Actor> actors)
    {
        _actors.Set(markers.Count, i => markers[i].MapPosition, actors, i => markers[i].Actor);
        bool resized = markers.Count != _markers.Length;
        if (resized)
        {
            _markers = new Marker[markers.Count];
            Multimesh.InstanceCount = markers.Count;
        }
        for (int i = 0; i < markers.Count; i++)
        {
            Marker m = markers[i];
            if (!resized && m == _markers[i])
                continue;
            _markers[i] = m;
            Multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, new Vector3(m.MapPosition.X, m.MapPosition.Z, -m.MapPosition.Y) / LevelMesh.MapUnitsPerMetre));
            Multimesh.SetInstanceCustomData(i, new Color(m.Slot, m.Flip ? ThingSprites.FlagFlip : 0, m.Radius, i));
            Multimesh.SetInstanceColor(i, MarkerColor);
        }
    }

    /// <summary>The name of <paramref name="mode"/> (the option's value).</summary>
    public static string ModeName(MonsterMarkerMode mode) => mode.ToString().ToLowerInvariant();

    /// <summary>Parses <c>--level-monster-markers</c>'s and <c>sprites/monster_markers</c>' value (<c>on</c>, before the beam style: the chevron).</summary>
    public static MonsterMarkerMode Parse(string value, string what) => value switch
    {
        "beam" => MonsterMarkerMode.Beam,
        "chevron" or "on" => MonsterMarkerMode.Chevron,
        "off" or "vanilla" => MonsterMarkerMode.Off,
        _ => throw new ArgumentException($"{what}: \"{value}\" (beam, chevron or off)"),
    };

    /// <summary>Removes every marker (a level unloads).</summary>
    public void Clear() => Set([], []);
}
