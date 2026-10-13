using System;
using Godot;

namespace IsoDoom.Render;

/// <summary>
/// The palette flash around the player (T6.13v, <see cref="PaletteFlashStyle.Player"/>;
/// not vanilla, SPEC §12): a soft disc on the floor at the player's feet in
/// the flash's colour (<see cref="PaletteFlash.GlowOf"/>), its alpha the
/// flash's level times the strength, fading out to its rim. Built once
/// around the origin; the scene moves it to the drawn (interpolated) player
/// each frame, as the aim marker (<see cref="AimMarker"/>). Depth-tested, just
/// above the floor, drawn after the opaque pass, so the player's sprite covers
/// its far half. Presentation only: the sim never reads it.
/// </summary>
public partial class FlashGlow : MeshInstance3D
{
    /// <summary>The disc's radius (map units: a little wider than the aim marker's ring).</summary>
    public const float Radius = 40f;

    /// <summary>The height above the floor (map units), against z-fighting (under the aim marker's).</summary>
    public const float Lift = 0.5f;

    /// <summary>The alpha at the centre at full level and strength.</summary>
    public const float MaxAlpha = 0.6f;

    /// <summary>The glow's shader: the disc's falloff in its vertex alpha, times <c>glow_colour</c>.</summary>
    public const string ShaderPath = "res://shaders/flash_glow.gdshader";

    private const int Segments = 32;

    private readonly ShaderMaterial _material;

    public FlashGlow()
    {
        CastShadow = ShadowCastingSetting.Off;
        Mesh = Build();
        MaterialOverride = _material = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath) };
    }

    /// <summary>The colour and alpha (sRGB, at the centre) as last set (<see cref="SetGlow"/>).</summary>
    public Color GlowColor { get; private set; }

    /// <summary>Sets the glow's colour (sRGB bytes) and its alpha at the centre (0–1).</summary>
    public void SetGlow(byte r, byte g, byte b, float alpha)
    {
        var c = Color.Color8(r, g, b) with { A = Math.Clamp(alpha, 0f, 1f) };
        if (c == GlowColor)
            return;
        GlowColor = c;
        _material.SetShaderParameter("glow_colour", c);
    }

    /// <summary>Puts the glow at <paramref name="feet"/> (map units: x, y, height).</summary>
    public void Place(Vector3 feet)
    {
        float s = 1 / LevelMesh.MapUnitsPerMetre;
        Position = new Vector3(feet.X * s, (feet.Z + Lift) * s, -feet.Y * s);
    }

    private static ArrayMesh Build()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float s = Radius / LevelMesh.MapUnitsPerMetre;
        Color centre = Colors.White, rim = Colors.White with { A = 0 };
        for (int i = 0; i < Segments; i++)
        {
            float a0 = i * Mathf.Tau / Segments, a1 = (i + 1) * Mathf.Tau / Segments;
            st.SetColor(centre);
            st.AddVertex(Vector3.Zero);
            st.SetColor(rim);
            st.AddVertex(new Vector3(Mathf.Cos(a0) * s, 0, -Mathf.Sin(a0) * s));
            st.SetColor(rim);
            st.AddVertex(new Vector3(Mathf.Cos(a1) * s, 0, -Mathf.Sin(a1) * s));
        }
        return st.Commit();
    }
}
