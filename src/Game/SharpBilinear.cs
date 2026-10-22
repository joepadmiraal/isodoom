using System;

namespace IsoDoom.Game;

/// <summary>
/// T7.11: sharp-bilinear scaling, Chocolate Doom's fix for a scale that is
/// not whole (vanilla's rows at 4:3, <see cref="ScreenLayout"/>): as if
/// scaled up by a whole number with nearest filtering and then filtered
/// linearly to the final size, in one step. Inside each texel's block the
/// texel shows exactly; only the viewport pixels on a block's edge blend with
/// the neighbour, so strokes stay even and crisp. At a whole scale it is
/// nearest filtering exactly. <see cref="ShaderCode"/> is the views' shader;
/// <see cref="Sample"/> mirrors it on the CPU (a full screen composed for the
/// wipe's start, the level check's comparisons). Textures are RGBA8 with
/// premultiplied alpha (<see cref="HudScreen.ToRgba"/>'s see-through pixels
/// are 0, 0, 0, 0), blended so (<see cref="Over"/>): no dark fringe where the
/// level shows beside them. No Godot types: the tests link it.
/// </summary>
public static class SharpBilinear
{
    /// <summary>The canvas-item shader (its <c>scale</c>: viewport pixels per texel; the node filters linearly).</summary>
    public const string ShaderCode = @"
shader_type canvas_item;
render_mode blend_premul_alpha;

uniform vec2 scale = vec2(1.0); // viewport pixels per texel
// T8.4d: the canvas is in linear colour (HDR 2D, Srgb.LinearCanvas), so TEXTURE reads linear
// and the hardware's blend would mix linear colour: blend the four texels' sRGB values instead, as without it
uniform bool srgb_blend = false;

vec3 to_srgb(vec3 c) {
    return mix(c * 12.92, 1.055 * pow(max(c, vec3(0.0)), vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), c));
}

vec4 texel_srgb(sampler2D tex, ivec2 at, ivec2 size) {
    vec4 t = texelFetch(tex, clamp(at, ivec2(0), size - 1), 0);
    return vec4(to_srgb(t.rgb), t.a);
}

void fragment() {
    vec2 size = 1.0 / TEXTURE_PIXEL_SIZE;
    vec2 texel = UV * size;
    vec2 range = 0.5 - 0.5 / scale;
    vec2 d = fract(texel) - 0.5;
    vec2 f = (d - clamp(d, -range, range)) * scale + 0.5;
    if (!srgb_blend) {
        COLOR = texture(TEXTURE, (floor(texel) + f) / size);
    } else {
        vec2 p = floor(texel) + f - 0.5;
        ivec2 i = ivec2(floor(p));
        vec2 w = p - floor(p);
        ivec2 n = ivec2(size);
        vec4 c = mix(mix(texel_srgb(TEXTURE, i, n), texel_srgb(TEXTURE, i + ivec2(1, 0), n), w.x),
            mix(texel_srgb(TEXTURE, i + ivec2(0, 1), n), texel_srgb(TEXTURE, i + ivec2(1, 1), n), w.x), w.y);
        COLOR = vec4(mix(c.rgb / 12.92, pow((c.rgb + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c.rgb)), c.a);
    }
}
";

    /// <summary>Whether <paramref name="pixels"/> viewport pixels show <paramref name="texels"/> texels at a whole scale (nearest filtering then is the same).</summary>
    public static bool IsWhole(int pixels, int texels) => texels > 0 && pixels % texels == 0;

    /// <summary>
    /// The colour (premultiplied RGBA, 0–255, unrounded) the shader draws at
    /// viewport pixel <paramref name="px"/>, <paramref name="py"/> for the
    /// <paramref name="width"/> × <paramref name="height"/> texture
    /// <paramref name="rgba"/> shown over viewport rectangle <paramref name="rect"/>
    /// (the pixel need not be inside it: the edges clamp).
    /// </summary>
    public static (float R, float G, float B, float A) Sample(ReadOnlySpan<byte> rgba, int width, int height, (int X, int Y, int W, int H) rect, int px, int py)
    {
        (int x0, int x1, float ax) = Axis(px - rect.X, rect.W, width);
        (int y0, int y1, float ay) = Axis(py - rect.Y, rect.H, height);
        int i00 = (y0 * width + x0) * 4, i10 = (y0 * width + x1) * 4, i01 = (y1 * width + x0) * 4, i11 = (y1 * width + x1) * 4;
        float w00 = (1 - ax) * (1 - ay), w10 = ax * (1 - ay), w01 = (1 - ax) * ay, w11 = ax * ay;
        float Channel(ReadOnlySpan<byte> t, int c) => t[i00 + c] * w00 + t[i10 + c] * w10 + t[i01 + c] * w01 + t[i11 + c] * w11;
        return (Channel(rgba, 0), Channel(rgba, 1), Channel(rgba, 2), Channel(rgba, 3));
    }

    // One axis: the viewport pixel `p` (from the rectangle's edge) of `pixels` showing `texels`: the two texels sampled and the second's weight.
    private static (int T0, int T1, float Weight) Axis(int p, int pixels, int texels)
    {
        if (IsWhole(pixels, texels))
        {
            int nearest = Math.Clamp((int)Math.Floor(p / (double)(pixels / texels)), 0, texels - 1); // exactly
            return (nearest, nearest, 0);
        }
        float scale = pixels / (float)texels;
        float texel = (p + 0.5f) / pixels * texels;
        float range = 0.5f - 0.5f / scale;
        float d = texel - MathF.Floor(texel) - 0.5f;
        float f = (d - Math.Clamp(d, -range, range)) * scale + 0.5f;
        float t = MathF.Floor(texel) + f - 0.5f; // the linear sample's position between texel centres
        int t0 = (int)MathF.Floor(t);
        float w = t - t0;
        return (Math.Clamp(t0, 0, texels - 1), Math.Clamp(t0 + 1, 0, texels - 1), w);
    }

    /// <summary>
    /// <c>blend_premul_alpha</c>: <paramref name="src"/> over the RGBA8
    /// pixel at <paramref name="dst"/>[<paramref name="i"/>] (its alpha kept), rounded.
    /// T8.4d: on a linear canvas (<see cref="Srgb.LinearCanvas"/>) the
    /// blend is of linear colour, the shader's sRGB result turned linear.
    /// </summary>
    public static void Over(byte[] dst, int i, (float R, float G, float B, float A) src)
    {
        float keep = 1 - src.A / 255f;
        if (Srgb.LinearCanvas)
        {
            dst[i] = LinearOver(src.R, dst[i], keep);
            dst[i + 1] = LinearOver(src.G, dst[i + 1], keep);
            dst[i + 2] = LinearOver(src.B, dst[i + 2], keep);
            return;
        }
        dst[i] = ToByte(src.R + dst[i] * keep);
        dst[i + 1] = ToByte(src.G + dst[i + 1] * keep);
        dst[i + 2] = ToByte(src.B + dst[i + 2] * keep);
    }

    /// <summary>
    /// Draws the texture <paramref name="rgba"/> (<paramref name="width"/> ×
    /// <paramref name="height"/>) over viewport rectangle <paramref name="rect"/>
    /// into <paramref name="dst"/> (RGBA8, <paramref name="viewWidth"/> ×
    /// <paramref name="viewHeight"/>) as the shader and its blending do.
    /// </summary>
    public static void Draw(byte[] dst, int viewWidth, int viewHeight, ReadOnlySpan<byte> rgba, int width, int height, (int X, int Y, int W, int H) rect)
    {
        for (int y = Math.Max(rect.Y, 0); y < Math.Min(rect.Y + rect.H, viewHeight); y++)
        {
            for (int x = Math.Max(rect.X, 0); x < Math.Min(rect.X + rect.W, viewWidth); x++)
            {
                (float R, float G, float B, float A) c = Sample(rgba, width, height, rect, x, y);
                if (c.A > 0)
                    Over(dst, (y * viewWidth + x) * 4, c);
            }
        }
    }

    private static byte LinearOver(float src, byte dst, float keep) =>
        Srgb.ToByte(Srgb.ToLinear(src / 255.0) + Srgb.ToLinear(dst / 255.0) * keep);

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Floor(v + 0.5f), 0, 255);
}
