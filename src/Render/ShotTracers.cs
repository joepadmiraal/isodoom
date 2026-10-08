using System.Collections.Generic;
using Godot;

namespace IsoDoom.Render;

/// <summary>Which hitscans draw a tracer (<see cref="ShotTracers"/>, <c>--level-tracers</c>).</summary>
public enum TracerMode
{
    /// <summary>No tracers: vanilla.</summary>
    Off,

    /// <summary>The console player's shots only.</summary>
    Player,

    /// <summary>Every hitscan: the player's and the monsters'.</summary>
    All,
}

/// <summary>
/// Shot tracers (not vanilla, SPEC §12): a short-lived line along each
/// hitscan the sim fired (its <c>se_shot</c> events), from the shooter's edge
/// to its puff or blood (or the sky, or the end of its range), so a
/// twin-stick player sees where the shots go. Each is a ribbon facing the
/// camera, faint at the shooter and bright where it stopped, fading out (slowly, then fast) over
/// <see cref="Lifetime"/> seconds; drawn over everything, as the shot is seen
/// from above. Presentation only: the sim never reads them.
/// </summary>
public partial class ShotTracers : MeshInstance3D
{
    /// <summary>How long a tracer shows (seconds).</summary>
    public const float Lifetime = 0.25f;

    /// <summary>A tracer's half width (map units).</summary>
    public const float HalfWidth = 0.75f;

    /// <summary>The colour of the player's tracers.</summary>
    public static readonly Color PlayerColor = new(1f, 0.95f, 0.6f);

    /// <summary>The colour of the monsters' tracers.</summary>
    public static readonly Color MonsterColor = new(1f, 0.35f, 0.2f);

    private readonly record struct Tracer(Vector3 From, Vector3 To, Color Color, float Age);

    private readonly List<Tracer> _tracers = [];
    private readonly ImmediateMesh _mesh = new();

    /// <summary>The tracers showing.</summary>
    public int Count => _tracers.Count;

    public ShotTracers()
    {
        Mesh = _mesh;
        CastShadow = ShadowCastingSetting.Off;
        MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = true,
            RenderPriority = 1,
        };
    }

    /// <summary>Adds a tracer from <paramref name="from"/> to <paramref name="to"/> (map units: x, y, height).</summary>
    public void Add(Vector3 from, Vector3 to, Color color) =>
        _tracers.Add(new Tracer(ToGodot(from), ToGodot(to), color, 0));

    /// <summary>Removes every tracer (a level unloads).</summary>
    public void Clear()
    {
        _tracers.Clear();
        _mesh.ClearSurfaces();
    }

    /// <summary>Ages the tracers by <paramref name="delta"/> seconds, drops the old ones and rebuilds the ribbons facing <paramref name="camera"/>.</summary>
    public void Update(double delta, Camera3D? camera)
    {
        _mesh.ClearSurfaces();
        for (int i = _tracers.Count - 1; i >= 0; i--)
        {
            Tracer t = _tracers[i] with { Age = _tracers[i].Age + (float)delta };
            if (t.Age >= Lifetime)
                _tracers.RemoveAt(i);
            else
                _tracers[i] = t;
        }
        if (_tracers.Count == 0 || camera is null)
            return;

        float halfWidth = HalfWidth / LevelMesh.MapUnitsPerMetre;
        Vector3 forward = -camera.GlobalBasis.Z;
        bool ortho = camera.Projection == Camera3D.ProjectionType.Orthogonal;
        bool begun = false;
        foreach (Tracer t in _tracers)
        {
            Vector3 d = t.To - t.From;
            Vector3 view = ortho ? forward : (t.From + t.To) / 2 - camera.GlobalPosition;
            Vector3 side = d.Cross(view);
            if (side.LengthSquared() < 1e-12f)
                continue;
            side = side.Normalized() * halfWidth;
            if (!begun)
                _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
            begun = true;
            float age = t.Age / Lifetime, fade = 1 - age * age;
            Color tail = t.Color with { A = 0.25f * fade }, head = t.Color with { A = fade };
            Vertex(t.From - side, tail);
            Vertex(t.From + side, tail);
            Vertex(t.To + side, head);
            Vertex(t.From - side, tail);
            Vertex(t.To + side, head);
            Vertex(t.To - side, head);
        }
        if (begun)
            _mesh.SurfaceEnd();
    }

    private void Vertex(Vector3 p, Color c)
    {
        _mesh.SurfaceSetColor(c);
        _mesh.SurfaceAddVertex(p);
    }

    private static Vector3 ToGodot(Vector3 units) =>
        new(units.X / LevelMesh.MapUnitsPerMetre, units.Z / LevelMesh.MapUnitsPerMetre, -units.Y / LevelMesh.MapUnitsPerMetre);
}
