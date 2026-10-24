using System;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Map;
using IsoDoom.Render;

namespace IsoDoom.Game;

/// <summary>
/// The level check's model of a GPU's leaky filtered copies (T8.4e, SPEC §12).
/// Under Godot's Mobile renderer a frame whose 3D view reads the screen texture
/// (the fuzz pass, the next pass of every sprite material) or the depth texture
/// (the item beams, the monster markers, the upright hiding) cannot tonemap in
/// a subpass: the view reaches the screen through a separate full-screen pass,
/// and the menus' dim reads the canvas's back buffer, another such copy. A
/// Raspberry Pi's V3D draws those copies with up to 1/128 of the neighbouring
/// pixels in each (lavapipe and Forward+ exactly), which the exact comparisons
/// see where a texel meets a different one. The check measures it
/// (<see cref="MeasureCopyLeak"/>) and only then lets the comparisons of the
/// frames it can reach accept it (<see cref="Leaked"/>), so everywhere else
/// they stay exact.
/// </summary>
public partial class LevelCheck
{
    /// <summary>The most of a neighbouring pixel <see cref="Leaked"/> takes, on each axis (linear colour): twice V3D's.</summary>
    private const double MaxLeak = 1.0 / 64;

    /// <summary>Whether this GPU's filtered copies leak (<see cref="MeasureCopyLeak"/>).</summary>
    private bool _copiesLeak;

    /// <summary>
    /// Straight down at player 1's start, things hidden (so Godot Mobile can
    /// tonemap in a subpass), then again with a small quad in a corner that
    /// reads the screen texture (so it cannot): any other pixel that changes
    /// is a copy's leak. Sets <see cref="_copiesLeak"/>.
    /// </summary>
    private async Task MeasureCopyLeak(LevelMesh m)
    {
        _copiesLeak = false;
        Vector2 origin = m.Level.PlayerStart(0) is MapThing start ? new Vector2(start.X, start.Y)
            : new Vector2(m.Bounds.GetCenter().X, -m.Bounds.GetCenter().Z) * LevelMesh.MapUnitsPerMetre;
        (int lowest, int highest) = HeightRange(m.Level);
        var basis = new Basis(new Vector3(1, 0, 0), new Vector3(0, 0, -1), new Vector3(0, 1, 0));
        Ortho(basis, new Vector3(origin.X, origin.Y, highest + 64.5f), 1, highest - lowest + 128);
        string what = $"{m.Level.Name}: filtered copies (T8.4e)";
        byte[]? plain = await Capture(what);
        // 4 pixels at (8, 8) from the top left corner, halfway down the view.
        const int Probe = 4, Corner = 8, Margin = 16;
        Vector2I size = ViewSize();
        using var shader = new Shader
        {
            Code = """
                shader_type spatial;
                render_mode unshaded, cull_disabled, shadows_disabled, fog_disabled;
                uniform sampler2D screen : hint_screen_texture, filter_nearest, repeat_disable;
                void fragment() {
                    ALBEDO = vec3(1.0, 0.0, 1.0) + texture(screen, SCREEN_UV).rgb * 0.0;
                }
                """,
        };
        using var material = new ShaderMaterial { Shader = shader };
        using var quad = new QuadMesh { Size = new Vector2(Probe, Probe) / LevelMesh.MapUnitsPerMetre };
        var probe = new MeshInstance3D { Mesh = quad, MaterialOverride = material };
        _scene.Camera.AddChild(probe);
        probe.Position = new Vector3(Corner - size.X / 2f, size.Y / 2f - Corner, -(highest - lowest + 128) / 2f) / LevelMesh.MapUnitsPerMetre;
        byte[]? read = await Capture($"{what}, the screen texture read");
        _scene.Camera.RemoveChild(probe);
        probe.Free();
        if (plain is null || read is null)
            return;
        int changed = 0, most = 0, probed = 0;
        for (int y = 0; y < size.Y; y++)
        {
            for (int x = 0; x < size.X; x++)
            {
                int i = (y * size.X + x) * 4;
                int d = Math.Max(Math.Abs(plain[i] - read[i]), Math.Max(Math.Abs(plain[i + 1] - read[i + 1]), Math.Abs(plain[i + 2] - read[i + 2])));
                if (x < Margin && y < Margin)
                {
                    probed += d > 0 ? 1 : 0;
                    continue;
                }
                if (d > 0)
                {
                    changed++;
                    most = Math.Max(most, d);
                }
            }
        }
        if (probed == 0)
        {
            Fail($"{what}: the probe reading the screen texture was not drawn");
            return;
        }
        _copiesLeak = changed > 0;
        GD.Print(_copiesLeak
            ? $"Level check: {what}: reading the screen texture changed {changed} other pixels, by at most {most}: the copies leak, the comparisons that see them accept up to {MaxLeak} of a neighbour"
            : $"Level check: {what}: reading the screen texture changed no other pixel: the copies are exact, so are the comparisons");
    }

    /// <summary>
    /// Whether pixel (<paramref name="px"/>, <paramref name="py"/>),
    /// <paramref name="got"/>, is <paramref name="expected"/> with a little of a
    /// neighbour in it, where this GPU's copies leak (<see cref="_copiesLeak"/>;
    /// never elsewhere): within a level of a bilinear sample of
    /// <paramref name="frame"/> (<paramref name="width"/> × <paramref name="height"/>)
    /// with <paramref name="expected"/> in place of the pixel, at most
    /// <see cref="MaxLeak"/> of the way to a neighbour on each axis (linear
    /// colour), made a byte by <paramref name="output"/> (default the screen's
    /// sRGB rounding).
    /// </summary>
    private bool Leaked(byte[] frame, int width, int height, int px, int py, (int R, int G, int B) got, (int R, int G, int B) expected,
        Func<double, int>? output = null)
    {
        if (!_copiesLeak)
            return false;
        const int Steps = 16;
        Func<double, int> toByte = output ?? (x => IsoDoom.Game.Srgb.ToByte(x));
        (double R, double G, double B) e = Linear(expected);
        foreach (int dx in (ReadOnlySpan<int>)[-1, 1])
        {
            foreach (int dy in (ReadOnlySpan<int>)[-1, 1])
            {
                // A bilinear sample a little towards (dx, dy): the pixel, its neighbours across and down, and the one diagonally.
                (double R, double G, double B) h = Linear(At(px + dx, py)), v = Linear(At(px, py + dy)), d = Linear(At(px + dx, py + dy));
                for (int i = 0; i <= Steps; i++)
                {
                    double wx = MaxLeak * i / Steps;
                    for (int j = 0; j <= Steps; j++)
                    {
                        double wy = MaxLeak * j / Steps;
                        double we = (1 - wx) * (1 - wy), wh = wx * (1 - wy), wv = (1 - wx) * wy, wd = wx * wy;
                        if (Near(got.R, e.R * we + h.R * wh + v.R * wv + d.R * wd) && Near(got.G, e.G * we + h.G * wh + v.G * wv + d.G * wd)
                            && Near(got.B, e.B * we + h.B * wh + v.B * wv + d.B * wd))
                        {
                            return true;
                        }
                    }
                }
            }
        }
        return false;

        (int R, int G, int B) At(int x, int y)
        {
            int q = (Math.Clamp(y, 0, height - 1) * width + Math.Clamp(x, 0, width - 1)) * 4;
            return (frame[q], frame[q + 1], frame[q + 2]);
        }
        static (double R, double G, double B) Linear((int R, int G, int B) c) =>
            (IsoDoom.Game.Srgb.ToLinear(c.R / 255.0), IsoDoom.Game.Srgb.ToLinear(c.G / 255.0), IsoDoom.Game.Srgb.ToLinear(c.B / 255.0));
        bool Near(int got, double linear) => Math.Abs(got - toByte(linear)) <= 1;
    }

    /// <summary>
    /// The colours pixel (<paramref name="px"/>, <paramref name="py"/>) of
    /// <paramref name="frame"/> may take through a leaky copy (none where the
    /// copies are exact): bilinear samples up to <see cref="MaxLeak"/> of the
    /// way towards its neighbours, as <see cref="Leaked"/> takes them, rounded
    /// to sRGB bytes (each once).
    /// </summary>
    private System.Collections.Generic.HashSet<(int R, int G, int B)> LeakedSamples(byte[] frame, int width, int height, int px, int py)
    {
        var samples = new System.Collections.Generic.HashSet<(int R, int G, int B)>();
        if (!_copiesLeak)
            return samples;
        const int Steps = 8;
        (double R, double G, double B) e = Linear(At(px, py));
        foreach (int dx in (ReadOnlySpan<int>)[-1, 1])
        {
            foreach (int dy in (ReadOnlySpan<int>)[-1, 1])
            {
                (double R, double G, double B) h = Linear(At(px + dx, py)), v = Linear(At(px, py + dy)), d = Linear(At(px + dx, py + dy));
                for (int i = 0; i <= Steps; i++)
                {
                    double wx = MaxLeak * i / Steps;
                    for (int j = 0; j <= Steps; j++)
                    {
                        double wy = MaxLeak * j / Steps;
                        double we = (1 - wx) * (1 - wy), wh = wx * (1 - wy), wv = (1 - wx) * wy, wd = wx * wy;
                        samples.Add((IsoDoom.Game.Srgb.ToByte(e.R * we + h.R * wh + v.R * wv + d.R * wd), IsoDoom.Game.Srgb.ToByte(e.G * we + h.G * wh + v.G * wv + d.G * wd),
                            IsoDoom.Game.Srgb.ToByte(e.B * we + h.B * wh + v.B * wv + d.B * wd)));
                    }
                }
            }
        }
        return samples;

        (int R, int G, int B) At(int x, int y)
        {
            int q = (Math.Clamp(y, 0, height - 1) * width + Math.Clamp(x, 0, width - 1)) * 4;
            return (frame[q], frame[q + 1], frame[q + 2]);
        }
        static (double R, double G, double B) Linear((int R, int G, int B) c) =>
            (IsoDoom.Game.Srgb.ToLinear(c.R / 255.0), IsoDoom.Game.Srgb.ToLinear(c.G / 255.0), IsoDoom.Game.Srgb.ToLinear(c.B / 255.0));
    }

    /// <summary>
    /// Whether pixel <paramref name="i"/> (a byte offset) of two captures of
    /// the view is the same, or one is the other with a copy's leak
    /// (<see cref="Leaked"/>, each frame its own neighbours), or both are one
    /// colour with a leak each (over different neighbours: a sprite over the
    /// level and alone over the background).
    /// </summary>
    private bool SameOrLeaked(byte[] a, byte[] b, int i)
    {
        (int R, int G, int B) ca = (a[i], a[i + 1], a[i + 2]), cb = (b[i], b[i + 1], b[i + 2]);
        if (ca == cb)
            return true;
        if (!_copiesLeak)
            return false;
        Vector2I size = ViewSize();
        int px = i / 4 % size.X, py = i / 4 / size.X;
        return Leaked(a, size.X, size.Y, px, py, ca, cb) || Leaked(b, size.X, size.Y, px, py, cb, ca) || BothLeaked();

        // Both leaky, each towards its own neighbours: the two apart by at most what each can take
        // (MaxLeak of its largest difference from a neighbour, linear colour), and a level.
        bool BothLeaked()
        {
            for (int c = 0; c < 3; c++)
            {
                double la = Lin(a[i + c]), lb = Lin(b[i + c]);
                double room = MaxLeak * (Contrast(a, c, la) + Contrast(b, c, lb))
                    + Math.Max(Lin(Math.Min(a[i + c] + 1, 255)) - la, la - Lin(Math.Max(a[i + c] - 1, 0)));
                if (Math.Abs(la - lb) > room)
                    return false;
            }
            return true;
        }
        double Contrast(byte[] f, int c, double centre)
        {
            double most = 0;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = Math.Clamp(px + dx, 0, size.X - 1), y = Math.Clamp(py + dy, 0, size.Y - 1);
                    most = Math.Max(most, Math.Abs(Lin(f[(y * size.X + x) * 4 + c]) - centre));
                }
            }
            return most;
        }
        static double Lin(int v) => IsoDoom.Game.Srgb.ToLinear(v / 255.0);
    }
}
