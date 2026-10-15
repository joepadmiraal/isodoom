using System;
using System.Collections.Generic;
using Godot;
using IsoDoom.Sim;

namespace IsoDoom.Render;

/// <summary>
/// Item beams (T6.13r, not vanilla, SPEC §12): under the game camera a wall
/// hides what stands up to about 0.7 × its height behind it, so pickups a
/// vanilla player in the room would see are missed. Each beamed pickup gets a
/// thin vertical ribbon facing the camera, rising <see cref="Height"/> units
/// from its foot in its kind's colour (<see cref="ItemKinds.ColorOf"/>), drawn
/// by <see cref="ShaderPath"/> only while the item's billboard is hidden whole:
/// the shader puts the patch's opaque texels where the sprite shader draws
/// them and tests them against the depth texture, so the cutaway, door lids,
/// wall caps and fog all count as they draw. The beam is depth-tested, so it
/// rises from behind the wall that hides its item. Actors (monsters, barrels,
/// the player) do not hide items: the shader leaves out the texels the
/// billboards of the <see cref="ActorsPerBeam"/> actors nearest the item cover
/// (<see cref="Set"/>), so a monster walking over an item in the open does not
/// beam it. One <see cref="MultiMesh"/>
/// instance per beam (<see cref="Set"/>); which items get one is the owner's
/// choice (pickups the fog shows, <see cref="ItemBeamMode"/>). Presentation
/// only: the sim never reads them.
/// </summary>
public partial class ItemBeams : MultiMeshInstance3D
{
    public const string ShaderPath = "res://shaders/item_beam.gdshader";

    /// <summary>
    /// How high a beam rises above its item's foot (map units). A wall of
    /// height h with the item d units behind it hides the beam up to about
    /// h + 1.43 d at the game camera's 55° pitch, so 256 still clears a
    /// 128-unit wall with the item 60 units behind it.
    /// </summary>
    public const float Height = 256f;

    /// <summary>A beam's half width (map units).</summary>
    public const float HalfWidth = 1.25f;

    /// <summary>
    /// One beam: its item (<paramref name="Mobj"/>, its type's
    /// <paramref name="Kind"/>), the item's foot (map units: x, y, height),
    /// the atlas slot its billboard shows (−1: no beam) and whether it is
    /// mirrored, its radius (the wall pull, as its billboard's), and its
    /// sector.
    /// </summary>
    public readonly record struct Beam(mobj_t Mobj, ItemKind Kind, Vector3 MapPosition, int Slot, bool Flip, float Radius, int Sector) : NearActors.IThing
    {
        int NearActors.IThing.Self => -1;
    }

    /// <summary>An actor's billboard (<see cref="ThingSprites.FlagActor"/>): its foot (map units: x, y, height) and radius (the wall pull, as its billboard's).</summary>
    public readonly record struct Actor(Vector3 MapPosition, float Radius);

    /// <summary>How many actors near each item the shader checks (the shader's <c>BEAM_ACTORS</c>).</summary>
    public const int ActorsPerBeam = 4;

    /// <summary>
    /// How near an item an actor must be (map units, horizontally) to be one of
    /// its <see cref="ActorsPerBeam"/>: one that covers the item on screen
    /// stands at most about 1.2 × its height in front of it at the game
    /// camera's pitch (a tilted billboard shows its full height), and the
    /// tallest (the spider mastermind, the cyberdemon) are about 110 high.
    /// </summary>
    public const float ActorReach = 160f;

    // T8.3b: held here, never fetched back from the node (Godot's wrapper of
    // it races with the finalizer thread; see ThingSprites).
    private readonly MultiMesh _multimesh;
    private readonly ShaderMaterial _material;
    private Beam[] _beams = [];
    private readonly NearActors _actors = new();
    private SpriteSettings? _sprites;

    /// <summary>The beams as last set.</summary>
    public IReadOnlyList<Beam> Beams => _beams;

    public ItemBeams()
    {
        CastShadow = ShadowCastingSetting.Off;
        _material = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath) };
        _material.SetShaderParameter("beam_height", Height);
        _material.SetShaderParameter("beam_half_width", HalfWidth);
        _material.SetShaderParameter("thing_actors", _actors.Texture);
        MaterialOverride = _material;
        Multimesh = _multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = QuadMesh(),
        };
    }

    /// <summary>The beam mesh: a quad, x −1…1 across, y 0 (foot) … 1 (top); the shader sizes it.</summary>
    private static ArrayMesh QuadMesh()
    {
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new[] { new Vector3(-1, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 2, 1, 0, 3, 2 };
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        const float Reach = 256f / LevelMesh.MapUnitsPerMetre;
        mesh.CustomAabb = new Aabb(new Vector3(-Reach, -Reach, -Reach), new Vector3(2 * Reach, 2 * Reach, 2 * Reach));
        return mesh;
    }

    /// <summary>Uses <paramref name="atlas"/>'s patches for the hidden test (the things' atlas, <see cref="ThingSprites.Bind"/>).</summary>
    public void Bind(SpriteAtlas atlas)
    {
        _material.SetShaderParameter("sprite_atlas", atlas.AtlasTexture);
        _material.SetShaderParameter("sprite_info", atlas.InfoTexture);
    }

    /// <summary>Takes the sprites' tilt, tilt depth, wall pull and upright hiding (<see cref="LevelMesh.SetSprites"/>), so the hidden test puts the billboard where it is drawn.</summary>
    public void SetSprites(SpriteSettings settings)
    {
        if (settings == _sprites)
            return;
        _sprites = settings;
        _material.SetShaderParameter("tilt", Math.Clamp(settings.Tilt, 0f, 1f));
        _material.SetShaderParameter("tilt_depth", (int)settings.TiltDepth);
        _material.SetShaderParameter("wall_pull", Math.Max(settings.WallPull, 0f));
        _material.SetShaderParameter("upright_hiding", settings.Hidden == SpriteHidden.Upright ? 1 : 0);
    }

    /// <summary>
    /// Replaces the beams (one instance each), writing only the instances that
    /// changed, with the <see cref="ActorsPerBeam"/> of <paramref name="actors"/>
    /// nearest each item within <see cref="ActorReach"/>, whose billboards do
    /// not hide it.
    /// </summary>
    public void Set(IReadOnlyList<Beam> beams, IReadOnlyList<Actor>? actors = null)
    {
        _actors.Set(beams, actors ?? []);
        bool resized = beams.Count != _beams.Length;
        if (resized)
        {
            _beams = new Beam[beams.Count];
            _multimesh.InstanceCount = beams.Count;
        }
        for (int i = 0; i < beams.Count; i++)
        {
            Beam b = beams[i];
            if (!resized && b == _beams[i])
                continue;
            _beams[i] = b;
            _multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, new Vector3(b.MapPosition.X, b.MapPosition.Z, -b.MapPosition.Y) / LevelMesh.MapUnitsPerMetre));
            _multimesh.SetInstanceCustomData(i, new Color(b.Slot, b.Flip ? ThingSprites.FlagFlip : 0, b.Radius, i));
            (float r, float g, float bl) = ItemKinds.ColorOf(b.Kind);
            _multimesh.SetInstanceColor(i, new Color(r, g, bl));
        }
    }

    /// <summary>Removes every beam (a level unloads).</summary>
    public void Clear() => Set([]);
}
