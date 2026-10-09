using System;
using Godot;

namespace IsoDoom.Render;

/// <summary>When the aim marker shows (<see cref="AimMarker"/>, <c>--level-aim-marker</c>).</summary>
public enum AimMarkerMode
{
    /// <summary>Never: vanilla.</summary>
    Off,

    /// <summary>Unless the mouse cursor aims (its own point shows where): the pad's sticks and the keys.</summary>
    Pad,

    /// <summary>Always.</summary>
    On,
}

/// <summary>
/// The aim marker (not vanilla, SPEC §12): a thin ring on the floor around
/// the player's feet with a chevron outside it pointing along the player's
/// facing, so a twin-stick player (the Steam Deck's right stick) sees where it
/// aims; the sprite's 8 rotations only tell the facing to within 22.5°
/// either way. Built once around the origin pointing east; the scene moves
/// and turns it to the drawn (interpolated) player each frame. Depth-tested,
/// just above the floor, so the player's sprite covers the ring's far half.
/// Presentation only: the sim never reads it.
/// </summary>
public partial class AimMarker : MeshInstance3D
{
    /// <summary>The ring's inner and outer radius (map units; the player's radius is 16).</summary>
    public const float RingInner = 24f, RingOuter = 26f;

    /// <summary>The chevron's back (at its notch and wings) and tip, from the centre (map units).</summary>
    public const float ChevronBack = 32f, ChevronTip = 52f;

    /// <summary>The chevron's half width at its wings (map units).</summary>
    public const float ChevronHalfWidth = 12f;

    /// <summary>The height above the floor (map units), against z-fighting.</summary>
    public const float Lift = 1f;

    /// <summary>The marker's colour (the player's tracers', <see cref="ShotTracers.PlayerColor"/>).</summary>
    public static readonly Color MarkerColor = ShotTracers.PlayerColor;

    private const int RingSegments = 48;

    public AimMarker()
    {
        CastShadow = ShadowCastingSetting.Off;
        Mesh = Build();
        MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
    }

    /// <summary>Puts the marker at <paramref name="feet"/> (map units: x, y, height) pointing along <paramref name="angle"/> (BAM, 0 = east).</summary>
    public void Place(Vector3 feet, uint angle)
    {
        float s = 1 / LevelMesh.MapUnitsPerMetre;
        Position = new Vector3(feet.X * s, (feet.Z + Lift) * s, -feet.Y * s);
        Rotation = new Vector3(0, (float)(angle * (2 * Math.PI / 4294967296.0)), 0); // about Godot's up: east (+X) turns to north (−Z)
    }

    private static ArrayMesh Build()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float s = 1 / LevelMesh.MapUnitsPerMetre;
        Color ring = MarkerColor with { A = 0.45f }, chevron = MarkerColor with { A = 0.9f };

        // The ring: an annulus of quads (Godot space: map x = X, map y = −Z).
        for (int i = 0; i < RingSegments; i++)
        {
            float a0 = i * Mathf.Tau / RingSegments, a1 = (i + 1) * Mathf.Tau / RingSegments;
            Vector3 i0 = Ground(a0, RingInner), o0 = Ground(a0, RingOuter), i1 = Ground(a1, RingInner), o1 = Ground(a1, RingOuter);
            Triangle(st, ring, i0, o0, o1);
            Triangle(st, ring, i0, o1, i1);
        }

        // The chevron, pointing east: two triangles from the tip to its wings and the notch between them.
        var tip = new Vector3(ChevronTip * s, 0, 0);
        var notch = new Vector3((ChevronBack + 6f) * s, 0, 0);
        var left = new Vector3(ChevronBack * s, 0, -ChevronHalfWidth * s);
        var right = new Vector3(ChevronBack * s, 0, ChevronHalfWidth * s);
        Triangle(st, chevron, tip, left, notch);
        Triangle(st, chevron, tip, notch, right);
        return st.Commit();

        Vector3 Ground(float a, float r) => new(Mathf.Cos(a) * r * s, 0, -Mathf.Sin(a) * r * s);
    }

    private static void Triangle(SurfaceTool st, Color c, Vector3 a, Vector3 b, Vector3 d)
    {
        st.SetColor(c);
        st.AddVertex(a);
        st.SetColor(c);
        st.AddVertex(b);
        st.SetColor(c);
        st.AddVertex(d);
    }
}
