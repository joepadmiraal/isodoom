using System;
using Godot;

namespace IsoDoom.Render;

/// <summary>
/// The free-fly debug camera of the level scene (T2.7, SPEC §11 M2). Debug
/// only: the game camera (M3) is separate and must not build on it.
/// <para>
/// Controls (only while <see cref="InputEnabled"/> and the camera is
/// <c>Current</c>): click to capture the mouse, then the mouse looks around
/// (Esc releases it); W/A/S/D move (along the view direction in perspective,
/// level in orthographic), E/Space up, Q/C down; Shift ×4 speed, Alt ×¼;
/// mouse wheel changes the speed, Ctrl + wheel the field of view (perspective)
/// or the view size (orthographic); O toggles perspective/orthographic.
/// </para>
/// <para>
/// The camera's <see cref="Pivot"/> is the point it flies; in orthographic
/// mode the camera itself sits <see cref="OrthoBackoff"/> behind it along the
/// view direction, so the view stays centred on the pivot and nothing near it
/// is cut by the near plane (as an iso camera centred on the player would).
/// </para>
/// </summary>
public partial class FreeFlyCamera : Camera3D
{
    /// <summary>Default speed, map units per second.</summary>
    public const float DefaultSpeed = 512f;
    public const float MinSpeed = 16f, MaxSpeed = 16384f;

    /// <summary>Speed factor per mouse-wheel step, and the Shift/Alt modifiers.</summary>
    public const float WheelStep = 1.25f, FastFactor = 4f, SlowFactor = 0.25f;

    /// <summary>Mouse look, degrees per pixel of mouse motion.</summary>
    public const float DegreesPerPixel = 0.15f;

    public const float DefaultFov = 75f, MinFov = 10f, MaxFov = 120f;

    /// <summary>Default orthographic view height, map units (SPEC §7.1's iso camera shows about this much).</summary>
    public const float DefaultOrthoUnits = 768f;

    /// <summary>Distance of the orthographic camera behind its pivot, in metres.</summary>
    public const float OrthoBackoff = 400f;

    /// <summary>Speed in map units per second (before the Shift/Alt modifiers).</summary>
    public float Speed { get; set; } = DefaultSpeed;

    /// <summary>Yaw (0 = map north, positive turns left, as Godot's Y rotation) and pitch, in degrees.</summary>
    public float Yaw { get; private set; }
    public float Pitch { get; private set; }

    /// <summary>The point the camera flies, in Godot space (metres).</summary>
    public Vector3 Pivot { get; private set; }

    /// <summary>Whether the camera reacts to input (off for checks, which drive another camera).</summary>
    public bool InputEnabled { get; set; } = true;

    public bool IsOrthographic => Projection == ProjectionType.Orthogonal;

    public override void _Ready()
    {
        Fov = DefaultFov;
        Size = DefaultOrthoUnits / LevelMesh.MapUnitsPerMetre;
        KeepAspect = KeepAspectEnum.Height;
        Near = 0.05f;
        Far = 1500f;
        UpdateTransform();
    }

    /// <summary>Puts the pivot at <paramref name="pivot"/> (Godot space) looking along <paramref name="yaw"/>/<paramref name="pitch"/> (degrees).</summary>
    public void Place(Vector3 pivot, float yaw, float pitch)
    {
        Pivot = pivot;
        Yaw = Mathf.Wrap(yaw, -180f, 180f);
        Pitch = Math.Clamp(pitch, -89f, 89f);
        UpdateTransform();
    }

    /// <summary>
    /// Yaw in degrees for a vanilla angle in degrees (map space: 0 = east,
    /// counterclockwise): map x → +X and map y → −Z, and yaw 0 looks along −Z
    /// (map north).
    /// </summary>
    public static float YawForMapAngle(float degrees) => degrees - 90f;

    /// <summary>The vanilla angle (degrees, 0 = east, counterclockwise, 0–360) the camera faces.</summary>
    public float MapAngle => Mathf.PosMod(Yaw + 90f, 360f);

    public void SetOrthographic(bool ortho)
    {
        Projection = ortho ? ProjectionType.Orthogonal : ProjectionType.Perspective;
        UpdateTransform();
    }

    private Basis ViewBasis => Basis.FromEuler(new Vector3(Mathf.DegToRad(Pitch), Mathf.DegToRad(Yaw), 0));

    private void UpdateTransform()
    {
        Basis basis = ViewBasis;
        Vector3 origin = IsOrthographic ? Pivot + basis.Z * OrthoBackoff : Pivot;
        Transform = new Transform3D(basis, origin);
    }

    private bool Active => InputEnabled && Current && IsInsideTree();

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Active)
            return;
        switch (e)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } mb:
                Wheel(mb.ButtonIndex == MouseButton.WheelUp ? 1 : -1, mb.CtrlPressed || Input.IsPhysicalKeyPressed(Key.Ctrl));
                break;
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured:
                Place(Pivot, Yaw - motion.Relative.X * DegreesPerPixel, Pitch - motion.Relative.Y * DegreesPerPixel);
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                if (key.PhysicalKeycode == Key.Escape && Input.MouseMode == Input.MouseModeEnum.Captured)
                    Input.MouseMode = Input.MouseModeEnum.Visible;
                else if (key.PhysicalKeycode == Key.O)
                    SetOrthographic(!IsOrthographic);
                else
                    return;
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    /// <summary>One wheel step: speed, or with <paramref name="zoom"/> the field of view / orthographic size.</summary>
    private void Wheel(int steps, bool zoom)
    {
        float factor = MathF.Pow(WheelStep, steps);
        if (!zoom)
            Speed = Math.Clamp(Speed * factor, MinSpeed, MaxSpeed);
        else if (IsOrthographic)
            Size = Math.Clamp(Size / factor, 64f / LevelMesh.MapUnitsPerMetre, 32768f / LevelMesh.MapUnitsPerMetre);
        else
            Fov = Math.Clamp(Fov / factor, MinFov, MaxFov);
    }

    public override void _Process(double delta)
    {
        if (!Active)
            return;
        var move = new Vector3(
            Axis(Key.D, Key.A),
            Axis(Key.E, Key.Q) + Axis(Key.Space, Key.C),
            Axis(Key.S, Key.W));
        if (move == Vector3.Zero)
            return;
        float speed = Speed;
        if (Input.IsPhysicalKeyPressed(Key.Shift))
            speed *= FastFactor;
        if (Input.IsPhysicalKeyPressed(Key.Alt))
            speed *= SlowFactor;
        Place(Pivot + Step(move, (float)delta * speed / LevelMesh.MapUnitsPerMetre), Yaw, Pitch);
    }

    /// <summary>
    /// The pivot's displacement for <paramref name="move"/> (x right, y up,
    /// z back, each −1…1) over <paramref name="metres"/>: forward and back
    /// along the view direction in perspective, level in orthographic (where
    /// moving along the view changes nothing on screen); up and down along +Y.
    /// </summary>
    private Vector3 Step(Vector3 move, float metres)
    {
        Basis yawOnly = Basis.FromEuler(new Vector3(0, Mathf.DegToRad(Yaw), 0));
        Vector3 back = IsOrthographic ? yawOnly.Z : ViewBasis.Z;
        Vector3 v = yawOnly.X * move.X + Vector3.Up * Math.Clamp(move.Y, -1f, 1f) + back * move.Z;
        return v.LengthSquared() > 1 ? v.Normalized() * metres : v * metres;
    }

    private static float Axis(Key positive, Key negative) =>
        (Input.IsPhysicalKeyPressed(positive) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(negative) ? 1f : 0f);
}
