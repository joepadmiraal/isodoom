using System;
using Godot;

namespace IsoDoom.Render;

/// <summary>The fixed overview camera of the level scene (T2.5; T2.7 adds the free-fly one).</summary>
public static class LevelCamera
{
    /// <summary>Isometric-style overview: yaw and pitch of the camera, in degrees (SPEC §7.1: 45° yaw, 45–60° pitch).</summary>
    public const float IsoYaw = 45f, IsoPitch = -55f;

    /// <summary>
    /// Makes <paramref name="camera"/> an orthographic camera framing
    /// <paramref name="bounds"/> (Godot space) for a viewport of the given aspect
    /// ratio (width / height): looking straight down with map north up when
    /// <paramref name="topDown"/>, else from the isometric angle.
    /// </summary>
    public static void FrameOverview(Camera3D camera, Aabb bounds, float aspect, bool topDown)
    {
        Basis basis = topDown
            ? Basis.FromEuler(new Vector3(-Mathf.Pi / 2, 0, 0))
            : Basis.FromEuler(new Vector3(Mathf.DegToRad(IsoPitch), Mathf.DegToRad(IsoYaw), 0));
        Vector3 min = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = -min;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = bounds.GetEndpoint(i);
            var local = new Vector3(basis.X.Dot(corner), basis.Y.Dot(corner), basis.Z.Dot(corner));
            min = min.Min(local);
            max = max.Max(local);
        }
        Vector3 size = max - min;
        Vector3 centre = (min + max) / 2;
        float distance = size.Z / 2 + 10;
        camera.Projection = Camera3D.ProjectionType.Orthogonal;
        camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
        camera.Size = Math.Max(1f, Math.Max(size.Y, size.X / Math.Max(aspect, 0.01f)) * 1.04f);
        camera.Near = 0.05f;
        camera.Far = size.Z + 20;
        camera.GlobalTransform = new Transform3D(basis, basis.X * centre.X + basis.Y * centre.Y + basis.Z * (centre.Z + distance));
    }
}
