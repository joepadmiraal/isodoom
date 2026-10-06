using System;
using System.Collections.Generic;
using Godot;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Render;

/// <summary>
/// Things drawn as Y-axis billboards (T3.5, SPEC §7.5): one
/// <see cref="MultiMesh"/> instance per thing, drawn by
/// <c>shaders/sprite.gdshader</c> (<see cref="LevelMesh.SpriteMaterial"/>,
/// which shares the level's palette, light tables, light settings and
/// per-sector data) from the WAD's <see cref="SpriteAtlas"/>.
/// <list type="bullet">
/// <item><b>Size and anchor:</b> 1 map unit per patch pixel; the patch's
/// origin (<c>leftoffset</c>, <c>topoffset</c>) sits on the thing's position
/// at its <c>z</c> (the floor, or the ceiling minus its height). The quad
/// turns about the vertical axis to face the camera's view plane.</item>
/// <item><b>Rotation</b> (CPU, <see cref="UpdateRotations(Camera3D)"/>):
/// r_things.c <c>R_ProjectSprite</c>'s choice (<see cref="SpriteFrame.Select"/>)
/// with the camera as the viewpoint: the direction from the camera's position
/// to the thing in perspective, the camera's view direction in orthographic.
/// Mirrored rotations flip the columns inside the same rectangle.</item>
/// <item><b>Light</b> (shader, per instance as vanilla's per vissprite):
/// <c>scalelight</c> of the thing's sector light (no fake contrast) at the
/// distance walls use (<see cref="IsoDoom.Map.LightDiminishing"/>), measured
/// to the thing's position; <c>FF_FULLBRIGHT</c> frames colormap 0; a fixed
/// colormap beats both.</item>
/// <item><b>Rows below the origin</b> (most sprites have a few): vanilla draws
/// them over the floor. The quad is split at the origin's height and its lower
/// part is moved along each vertex's view ray towards the camera until it is
/// <see cref="PullMargin"/> above the thing's floor plane: it stays where it
/// is on screen but passes the depth test against the floor (SPEC §12 T3.5).</item>
/// <item><b>Readability</b> (T3.6, <see cref="SpriteSettings"/>, set on the
/// material by <see cref="LevelMesh.SetSprites"/>): the tilt towards the camera
/// (default full, with an upright billboard's depth), a one-texel outline
/// (default black) and blob shadows under actors (<see cref="Shadows"/>,
/// <see cref="Entry.ShadowRadius"/>; default off).</item>
/// </list>
/// Per-instance custom data: (atlas slot, or −1 to hide; flags: 1 flip, 2
/// full bright, 4 actor (T3.4b: the cutaway keeps it whole unless
/// <see cref="CutawayThings.All"/>); sector; radius in map units (T3.5a)). Positions and frames are set by the owner
/// (<see cref="SetEntries"/>, <see cref="SetEntry"/>).
/// </summary>
public partial class ThingSprites : MultiMeshInstance3D
{
    public const string ShaderPath = "res://shaders/sprite.gdshader";

    /// <summary>The sprite shader with the upright hiding (T3.6a, <see cref="SpriteHidden.Upright"/>).</summary>
    public const string HiddenShaderPath = "res://shaders/sprite_hidden.gdshader";

    /// <summary>How far above the floor plane (map units, along the view ray) the rows below a sprite's origin are drawn (the shader's <c>PULL_MARGIN</c>).</summary>
    public const float PullMargin = 1f;

    /// <summary>Custom data flags.</summary>
    public const int FlagFlip = 1, FlagFullBright = 2, FlagActor = 4;

    /// <summary>
    /// One thing: map position (x, y, z in map units), facing (BAM), sector
    /// index (light), sprite (<c>spritenum_t</c>), frame (0 = A), full bright,
    /// the radius of its blob shadow in map units (T3.6; 0: none), and whether
    /// it is an actor, which the cutaway cuts only with
    /// <see cref="CutawayThings.All"/> (T3.4b), and its <c>radius</c> in map
    /// units, by which the billboard is pulled towards the camera clear of
    /// walls behind it (T3.5a, <see cref="SpriteSettings.WallPull"/>).
    /// </summary>
    public readonly record struct Entry(Vector3 MapPosition, uint Angle, int Sector, int Sprite, int Frame, bool FullBright, float ShadowRadius = 0, bool Actor = false, float Radius = 0);

    public const string ShadowShaderPath = "res://shaders/sprite_shadow.gdshader";

    /// <summary>The blob shadows (T3.6): one instance per entry, drawn with the shadow material given to <see cref="Bind"/>.</summary>
    public MultiMeshInstance3D Shadows { get; } = new()
    {
        Name = "Shadows",
        CastShadow = ShadowCastingSetting.Off,
        Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = ShadowMesh(),
        },
    };

    /// <summary>What an instance shows: atlas slot (−1: nothing), flip, rotation slot (0–7; 0 for rotation-0 frames).</summary>
    public readonly record struct Shown(int Slot, bool Flip, int Rot);

    private SpriteAtlas? _atlas;
    private Entry[] _entries = Array.Empty<Entry>();
    private Shown[] _shown = Array.Empty<Shown>();
    private Color[] _custom = Array.Empty<Color>();
    private int? _isolated;
    private (bool Ortho, Vector3 Forward, Vector3 Position)? _view;

    /// <summary>The entries as last set.</summary>
    public IReadOnlyList<Entry> Entries => _entries;

    /// <summary>The frame each instance shows (after the last <see cref="UpdateRotations(Camera3D)"/>).</summary>
    public IReadOnlyList<Shown> ShownFrames => _shown;

    /// <summary>
    /// The custom data last written to instance <paramref name="i"/> (kept on
    /// the CPU: the headless renderer stores no instance data).
    /// </summary>
    public Color CustomData(int i) => _custom[i];

    /// <summary>The position instance <paramref name="i"/> was given (Godot space).</summary>
    public Vector3 InstancePosition(int i) => ToGodot(_entries[i].MapPosition);

    /// <summary>Entries whose sprite or frame the WAD lacks (vanilla <c>I_Error</c>s; drawn as nothing).</summary>
    public int MissingFrames { get; private set; }

    public ThingSprites()
    {
        CastShadow = ShadowCastingSetting.Off;
        Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = QuadMesh(),
        };
        AddChild(Shadows);
    }

    /// <summary>The shadow mesh: a horizontal square, x and z in −1…1 (the shader sizes it).</summary>
    private static ArrayMesh ShadowMesh()
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(1, 0, 1), new Vector3(-1, 0, 1) };
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 0, 2, 3 };
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        const float reach = 128f / LevelMesh.MapUnitsPerMetre;
        mesh.CustomAabb = new Aabb(new Vector3(-reach, -reach, -reach), new Vector3(2 * reach, 2 * reach, 2 * reach));
        return mesh;
    }

    /// <summary>
    /// The billboard mesh: two quads in "sprite space" (VERTEX.x: 0 left, 1
    /// right; VERTEX.y: 0 bottom row, 1 the origin's height, 2 top row;
    /// VERTEX.z: 1 for the lower quad, the rows below the origin). The shader
    /// turns them into map units.
    /// </summary>
    private static ArrayMesh QuadMesh()
    {
        var vertices = new[]
        {
            new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1),
            new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 2, 0), new Vector3(0, 2, 0),
        };
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6 };
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        // The shader sizes and moves the quads (sprites up to 256 units, the lower part pulled up to 128 units).
        const float reach = 512f / LevelMesh.MapUnitsPerMetre;
        mesh.CustomAabb = new Aabb(new Vector3(-reach, -reach, -reach), new Vector3(2 * reach, 2 * reach, 2 * reach));
        return mesh;
    }

    /// <summary>
    /// Uses <paramref name="atlas"/> and <paramref name="material"/> (a level's
    /// <see cref="LevelMesh.SpriteMaterial"/>, given the atlas textures), and
    /// <paramref name="shadowMaterial"/> (<see cref="LevelMesh.ShadowMaterial"/>)
    /// for the blob shadows.
    /// </summary>
    public void Bind(SpriteAtlas atlas, ShaderMaterial material, ShaderMaterial? shadowMaterial = null)
    {
        _atlas = atlas;
        Shadows.MaterialOverride = shadowMaterial;
        material.SetShaderParameter("sprite_atlas", atlas.AtlasTexture);
        material.SetShaderParameter("sprite_info", atlas.InfoTexture);
        MaterialOverride = material;
        Refresh();
    }

    /// <summary>Replaces every entry (one instance each).</summary>
    public void SetEntries(IReadOnlyList<Entry> entries)
    {
        _entries = new Entry[entries.Count];
        for (int i = 0; i < entries.Count; i++)
            _entries[i] = entries[i];
        _shown = new Shown[_entries.Length];
        _custom = new Color[_entries.Length];
        Multimesh.InstanceCount = _entries.Length;
        Shadows.Multimesh.InstanceCount = _entries.Length;
        for (int i = 0; i < _entries.Length; i++)
            WriteTransform(i);
        MissingFrames = 0;
        for (int i = 0; i < _entries.Length; i++)
        {
            if (FrameOf(_entries[i]) is null)
                MissingFrames++;
        }
        Refresh();
    }

    /// <summary>Changes entry <paramref name="i"/> (position, facing, frame…).</summary>
    public void SetEntry(int i, Entry entry)
    {
        Entry old = _entries[i];
        _entries[i] = entry;
        if (old.MapPosition != entry.MapPosition)
            WriteTransform(i);
        if (old != entry)
            UpdateInstance(i, force: true);
    }

    /// <summary>Shows only instance <paramref name="index"/> (null: all), for checks.</summary>
    public void Isolate(int? index)
    {
        _isolated = index;
        Refresh();
    }

    /// <summary>The sprite frame an entry shows, or null when the WAD lacks it.</summary>
    public SpriteFrame? FrameOf(Entry e)
    {
        if (_atlas is null || e.Sprite < 0 || e.Sprite >= _atlas.Sprites.NumSprites)
            return null;
        IReadOnlyList<SpriteFrame> frames = _atlas.Sprites.SpriteDefs[e.Sprite].Frames;
        return e.Frame >= 0 && e.Frame < frames.Count ? frames[e.Frame] : null;
    }

    /// <summary>Picks every instance's rotation for <paramref name="camera"/>: its position in perspective, its view direction in orthographic.</summary>
    public void UpdateRotations(Camera3D camera) =>
        UpdateRotations(camera.Projection == Camera3D.ProjectionType.Orthogonal, ViewDirection(camera), camera.GlobalPosition);

    /// <summary>
    /// Picks every instance's rotation for a camera at <paramref name="position"/>
    /// (Godot space) looking along <paramref name="forward"/>: orthographic
    /// cameras use the forward direction for every thing, perspective ones the
    /// direction from the position to each thing.
    /// </summary>
    public void UpdateRotations(bool ortho, Vector3 forward, Vector3 position)
    {
        _view = (ortho, forward, position);
        for (int i = 0; i < _entries.Length; i++)
            UpdateInstance(i);
    }

    /// <summary>
    /// The camera's view direction for rotations: its forward axis, or when it
    /// looks straight down, the top of the screen ("ahead").
    /// </summary>
    public static Vector3 ViewDirection(Camera3D camera)
    {
        Vector3 d = -camera.GlobalBasis.Z;
        return d.X * d.X + d.Z * d.Z < 1e-8f ? -camera.GlobalBasis.Y : d;
    }

    /// <summary>
    /// The BAM angle of a Godot-space direction's horizontal part as a map
    /// direction (map x = X, map y = −Z), vanilla's <c>R_PointToAngle</c> of
    /// that offset (computed with floats here: a presentation choice).
    /// </summary>
    public static uint BamOf(Vector3 godotDirection) => BamOfMap(godotDirection.X, -godotDirection.Z);

    /// <summary>The BAM angle of map direction (<paramref name="dx"/>, <paramref name="dy"/>).</summary>
    public static uint BamOfMap(double dx, double dy)
    {
        double turns = Math.Atan2(dy, dx) / (2 * Math.PI);
        if (turns < 0)
            turns += 1;
        return unchecked((uint)(long)Math.Round(turns * 4294967296.0));
    }

    /// <summary>Vanilla degrees (any value) as BAM.</summary>
    public static uint BamOfDegrees(double degrees)
    {
        double turns = degrees / 360.0;
        turns -= Math.Floor(turns);
        return unchecked((uint)(long)Math.Round(turns * 4294967296.0));
    }

    /// <summary>The direction from the viewpoint to entry <paramref name="i"/> for the last view (BAM).</summary>
    public uint ViewAngle(int i)
    {
        if (_view is not { } v)
            return 0;
        if (v.Ortho)
            return BamOf(v.Forward);
        Vector3 p = ToGodot(_entries[i].MapPosition);
        Vector3 d = p - v.Position;
        return d.X * d.X + d.Z * d.Z < 1e-8f ? BamOf(v.Forward) : BamOf(d);
    }

    private static Vector3 ToGodot(Vector3 map) =>
        new Vector3(map.X, map.Z, -map.Y) / LevelMesh.MapUnitsPerMetre;

    private void WriteTransform(int i)
    {
        var t = new Transform3D(Basis.Identity, ToGodot(_entries[i].MapPosition));
        Multimesh.SetInstanceTransform(i, t);
        Shadows.Multimesh.SetInstanceTransform(i, t);
    }

    private void Refresh()
    {
        for (int i = 0; i < _entries.Length; i++)
            UpdateInstance(i, force: true);
    }

    private void UpdateInstance(int i, bool force = false)
    {
        Entry e = _entries[i];
        Shown shown = new(-1, false, 0);
        if (FrameOf(e) is SpriteFrame frame)
        {
            (int lump, bool flip, int rot) = frame.Select(ViewAngle(i), e.Angle);
            shown = new Shown(_atlas!.SlotOf(lump), flip, rot);
        }
        if (!force && shown == _shown[i])
            return;
        _shown[i] = shown;
        bool hidden = _isolated is int only && only != i;
        int flags = (shown.Flip ? FlagFlip : 0) | (e.FullBright ? FlagFullBright : 0) | (e.Actor ? FlagActor : 0);
        _custom[i] = new Color(hidden ? -1 : shown.Slot, flags, e.Sector, e.Radius);
        Multimesh.SetInstanceCustomData(i, _custom[i]);
        Shadows.Multimesh.SetInstanceCustomData(i, new Color(hidden || shown.Slot < 0 ? 0 : e.ShadowRadius, 0, 0, 0));
    }
}
