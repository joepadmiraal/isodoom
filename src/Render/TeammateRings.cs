using System;
using System.Collections.Generic;
using Godot;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Render;

/// <summary>
/// The teammate rings (T11.10a, the user's decision; not vanilla, SPEC §12):
/// in co-op, a thin ring on the floor around each teammate's feet (not the
/// local player's, which has the aim marker), in that player's translation
/// colour (<see cref="ColourOf"/>), so the players tell each other apart
/// under the iso camera. Styled as the aim marker's ring (T6.13k: the same
/// radii, lift and shader, which pulls it towards the camera so the things
/// lying on the floor do not cover it while the player's own sprite still
/// covers its far half), without the chevron. Presentation only.
/// </summary>
public partial class TeammateRings : Node3D
{
    /// <summary>The ring's opacity (the aim marker's ring is 0.45 in the tracers' yellow; a teammate's colour can be grey or brown on a grey or brown floor).</summary>
    public const float RingAlpha = 0.75f;

    /// <summary>The palette index on the players' green ramp (0x70–0x7F) the ring's colour is taken from, through the player's translation.</summary>
    public const byte RampIndex = 0x74;

    private const int RingSegments = 48;

    /// <summary>A ring: the player (0–3) it is under, its feet (map units: x, y, height) and its colour.</summary>
    public readonly record struct Ring(int Player, Vector3 Feet, Color Colour);

    private readonly List<Ring> _rings = [];
    private readonly List<MeshInstance3D> _meshes = [];
    private readonly List<Color> _meshColours = [];

    /// <summary>The rings shown, as last set.</summary>
    public IReadOnlyList<Ring> Rings => _rings;

    /// <summary>
    /// Player <paramref name="player"/>'s ring colour: the green ramp's
    /// <see cref="RampIndex"/> through its translation table
    /// (<see cref="PlayerTranslation"/>: green, grey, brown, red) in palette 0,
    /// opaque.
    /// </summary>
    public static Color ColourOf(Playpal palette, int player)
    {
        (byte r, byte g, byte b) = palette.GetColor(0, PlayerTranslation.Translate(RampIndex, player & 3));
        return Color.Color8(r, g, b);
    }

    /// <summary>Shows <paramref name="rings"/> (the others hidden).</summary>
    public void Set(ReadOnlySpan<Ring> rings)
    {
        _rings.Clear();
        foreach (Ring r in rings)
            _rings.Add(r);
        float s = 1 / LevelMesh.MapUnitsPerMetre;
        for (int i = 0; i < _rings.Count; i++)
        {
            Ring ring = _rings[i];
            if (i == _meshes.Count)
            {
                var mesh = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                using var material = new ShaderMaterial { Shader = GodotRefs.LoadShader(AimMarker.ShaderPath) }; // T8.3d: held across the setter
                mesh.MaterialOverride = material;
                AddChild(mesh);
                _meshes.Add(mesh);
                _meshColours.Add(default);
            }
            MeshInstance3D m = _meshes[i];
            if (_meshColours[i] != ring.Colour) // (a new mesh's default colour is see-through: never a ring's)
            {
                using ArrayMesh built = Build(ring.Colour); // T8.3d: held across the setter, then disposed (GodotRefs)
                m.Mesh = built;
                _meshColours[i] = ring.Colour;
            }
            m.Position = new Vector3(ring.Feet.X * s, (ring.Feet.Z + AimMarker.Lift) * s, -ring.Feet.Y * s);
            m.Visible = true;
        }
        for (int i = _rings.Count; i < _meshes.Count; i++)
            _meshes[i].Visible = false;
    }

    /// <summary>Hides every ring.</summary>
    public void Clear() => Set([]);

    // The aim marker's ring (AimMarker.RingInner to RingOuter) around the origin, in one colour.
    private static ArrayMesh Build(Color colour)
    {
        var st = new SurfaceTool();
        using var keepSt = GodotRefs.Keep(st); // T8.3d: alive to the scope's end, not disposed (GodotRefs.Keep)
        st.Begin(Mesh.PrimitiveType.Triangles);
        float s = 1 / LevelMesh.MapUnitsPerMetre;
        Color c = colour.SrgbToLinear() with { A = RingAlpha }; // the vertex colours are linear: the palette colour on screen, blended in linear light
        for (int i = 0; i < RingSegments; i++)
        {
            float a0 = i * Mathf.Tau / RingSegments, a1 = (i + 1) * Mathf.Tau / RingSegments;
            Vector3 i0 = Ground(a0, AimMarker.RingInner), o0 = Ground(a0, AimMarker.RingOuter), i1 = Ground(a1, AimMarker.RingInner), o1 = Ground(a1, AimMarker.RingOuter);
            Triangle(i0, o0, o1);
            Triangle(i0, o1, i1);
        }
        return st.Commit();

        Vector3 Ground(float a, float r) => new(MathF.Cos(a) * r * s, 0, -MathF.Sin(a) * r * s);

        void Triangle(Vector3 a, Vector3 b, Vector3 d)
        {
            foreach (Vector3 v in (ReadOnlySpan<Vector3>)[a, b, d])
            {
                st.SetColor(c);
                st.AddVertex(v);
            }
        }
    }
}
