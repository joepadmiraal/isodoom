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
/// its far half. T6.13w: a pickup's flash is a ripple instead
/// (<see cref="SetRipple"/>): a white line circle (<see cref="RippleWidth"/>
/// units wide, crisp) growing from the player's feet to the aim marker's ring (<see cref="RippleRadius"/>), then
/// fading. Presentation only: the sim never reads it.
/// </summary>
public partial class FlashGlow : MeshInstance3D
{
    /// <summary>The disc's radius (map units: a little wider than the aim marker's ring).</summary>
    public const float Radius = 40f;

    /// <summary>The height above the floor (map units), against z-fighting (under the aim marker's).</summary>
    public const float Lift = 0.5f;

    /// <summary>The alpha at the centre at full level and strength.</summary>
    public const float MaxAlpha = 0.6f;

    /// <summary>T6.13w: the pickup ripple's full radius (map units: the aim marker's ring, <see cref="AimMarker.RingOuter"/>).</summary>
    public const float RippleRadius = AimMarker.RingOuter;

    /// <summary>T6.13w: the tics the ripple grows over (eased out), then the tics it fades over at its full radius.</summary>
    public const int RippleGrowTics = 8, RippleFadeTics = 4;

    /// <summary>T6.13w: the ripple's alpha at full strength (white over the floor).</summary>
    public const float RippleAlpha = 0.3f;

    /// <summary>T6.13w: the ripple's line width (map units, inwards from its radius; the whole disc while it is smaller).</summary>
    public const float RippleWidth = 3f;

    /// <summary>The glow's shader: the disc's falloff in its vertex alpha, times <c>glow_colour</c>.</summary>
    public const string ShaderPath = "res://shaders/flash_glow.gdshader";

    private const int Segments = 32;

    private readonly ShaderMaterial _material;

    public FlashGlow()
    {
        CastShadow = ShadowCastingSetting.Off;
        using ArrayMesh mesh = Build(); // T8.3d: held across the setter, then disposed (GodotRefs)
        Mesh = mesh;
        MaterialOverride = _material = new ShaderMaterial { Shader = GodotRefs.LoadShader(ShaderPath) };
    }

    /// <summary>The colour and alpha (sRGB, at the centre) as last set (<see cref="SetGlow"/>, <see cref="SetRipple"/>).</summary>
    public Color GlowColor { get; private set; }

    /// <summary>The disc's radius as last set (map units): <see cref="Radius"/> for the glow, the ripple's own for it.</summary>
    public float CurrentRadius { get; private set; } = Radius;

    /// <summary>T6.13w: whether the disc is the pickup ripple (a line circle), not the soft glow.</summary>
    public bool IsRipple { get; private set; }

    /// <summary>Sets the glow's colour (sRGB bytes) and its alpha at the centre (0–1): the soft disc of <see cref="Radius"/>.</summary>
    public void SetGlow(byte r, byte g, byte b, float alpha) => Set(Color.Color8(r, g, b), alpha, Radius, ripple: false);

    /// <summary>T6.13w: sets the pickup ripple: a white circle, <paramref name="alpha"/> over its line (0–1), <paramref name="radius"/> map units.</summary>
    public void SetRipple(float alpha, float radius) => Set(Colors.White, alpha, radius, ripple: true);

    /// <summary>
    /// T6.13w: the pickup ripple <paramref name="tics"/> tics after the pickup
    /// (a fraction between tics for the frames between them): its radius, eased
    /// out from 0 to <see cref="RippleRadius"/> over <see cref="RippleGrowTics"/>,
    /// and its alpha (0–1, before the strength), 1 while it grows, then down to
    /// 0 over <see cref="RippleFadeTics"/>; none (alpha 0) before or after.
    /// </summary>
    public static (float Radius, float Alpha) Ripple(double tics)
    {
        if (tics < 0 || tics >= RippleGrowTics + RippleFadeTics)
            return (0, 0);
        double t = Math.Min(tics / RippleGrowTics, 1);
        float radius = (float)(RippleRadius * (1 - (1 - t) * (1 - t)));
        float alpha = tics <= RippleGrowTics ? 1 : (float)(1 - (tics - RippleGrowTics) / RippleFadeTics);
        return (radius, alpha);
    }

    private void Set(Color colour, float alpha, float radius, bool ripple)
    {
        var c = colour with { A = Math.Clamp(alpha, 0f, 1f) };
        if (ripple != IsRipple || radius != CurrentRadius)
        {
            IsRipple = ripple;
            CurrentRadius = radius;
            float r = Math.Max(radius, 0.01f), k = r / Radius;
            Scale = new Vector3(k, 1, k);
            _material.SetShaderParameter("crisp", ripple);
            _material.SetShaderParameter("ring_width", ripple ? Math.Min(RippleWidth / r, 1f) : 1f);
        }
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
        using var st = new SurfaceTool(); // T8.3d: disposed (so held) after Commit (GodotRefs)
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
