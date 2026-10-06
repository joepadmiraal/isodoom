using System;
using Godot;

namespace IsoDoom.Render;

/// <summary>The game camera's projection (T3.3, SPEC §13 Q1; a presentation option).</summary>
public enum IsoProjection
{
    /// <summary>A true orthographic camera (the default; SPEC §12 T3.3).</summary>
    Orthographic,

    /// <summary>A narrow-FOV perspective camera (<see cref="IsoCamera.PerspectiveFov"/>).</summary>
    Perspective,
}

/// <summary>
/// The game camera (T3.3, SPEC §7.1): a fixed isometric-style camera with a
/// fixed yaw (<see cref="Yaw"/>, 45°) and pitch (<see cref="Pitch"/>, within
/// 45–60°), never rotating, centred on a target (the player mobj as drawn) with slight smoothing and a look-ahead towards the cursor
/// ground point. Ctrl + mouse wheel zooms within <see cref="MinViewUnits"/>–
/// <see cref="MaxViewUnits"/>; O switches orthographic / perspective. Not built
/// on <see cref="FreeFlyCamera"/>, which stays debug-only.
/// <para>
/// The zoom is the height of the view in map units at the focus point
/// (<see cref="ViewUnits"/>), so both projections frame the same area there:
/// the orthographic camera's <c>Size</c>, or the perspective camera's distance
/// for its fixed field of view.
/// </para>
/// </summary>
public partial class IsoCamera : Camera3D
{
    /// <summary>Yaw in degrees (Godot's Y rotation): the camera looks towards map north-west, so map north is up and to the right on screen (as the overview camera).</summary>
    public const float Yaw = 45f;

    /// <summary>Default pitch in degrees (down), and SPEC §7.1's range.</summary>
    public const float DefaultPitch = 55f, MinPitch = 45f, MaxPitch = 60f;

    /// <summary>Default view height at the focus point in map units, and the zoom limits.</summary>
    public const float DefaultViewUnits = 640f, MinViewUnits = 320f, MaxViewUnits = 1600f;

    /// <summary>Zoom factor per wheel step.</summary>
    public const float ZoomStep = 1.15f;

    /// <summary>The perspective prototype's vertical field of view, degrees.</summary>
    public const float PerspectiveFov = 25f;

    /// <summary>Distance of the orthographic camera from its focus, map units (above any vanilla geometry around the player).</summary>
    public const float OrthoDistanceUnits = 12800f;

    /// <summary>The look-ahead: this fraction of the target→cursor offset, at most <see cref="MaxLookAheadUnits"/> map units.</summary>
    public const float LookAheadFraction = 0.25f, MaxLookAheadUnits = 128f;

    /// <summary>Smoothing rate (1/s): the focus closes 1 − e^(−rate·dt) of its distance to the goal each frame.</summary>
    public const float SmoothingRate = 10f;

    /// <summary>The camera's pitch in degrees below the horizon.</summary>
    public float Pitch { get; private set; } = DefaultPitch;

    /// <summary>The view height at the focus point, map units (the zoom).</summary>
    public float ViewUnits { get; private set; } = DefaultViewUnits;

    /// <summary>The current projection.</summary>
    public IsoProjection Mode { get; private set; } = IsoProjection.Orthographic;

    /// <summary>The point the camera is centred on (Godot space): the target plus look-ahead, smoothed.</summary>
    public Vector3 Focus { get; private set; }

    /// <summary>
    /// The mouse cursor in viewport pixels, tracked from mouse motion events
    /// (so scripted motion moves it too; the screen centre until the mouse moves).
    /// </summary>
    public Vector2? Cursor { get; set; }

    /// <summary>Whether the camera reacts to input (off for checks).</summary>
    public bool InputEnabled { get; set; } = true;

    public override void _Ready()
    {
        KeepAspect = KeepAspectEnum.Height;
        Near = 0.5f;
        Far = 2000f;
        SetProjectionMode(Mode);
    }

    /// <summary>The view direction's basis: yaw <see cref="Yaw"/>, pitch −<see cref="Pitch"/>.</summary>
    public Basis ViewBasis => Basis.FromEuler(new Vector3(-Mathf.DegToRad(Pitch), Mathf.DegToRad(Yaw), 0));

    /// <summary>Screen right and screen up projected onto the ground (unit vectors in Godot's XZ plane).</summary>
    public Vector3 GroundRight => new Vector3(ViewBasis.X.X, 0, ViewBasis.X.Z).Normalized();

    /// <inheritdoc cref="GroundRight"/>
    public Vector3 GroundUp => new Vector3(-ViewBasis.Z.X, 0, -ViewBasis.Z.Z).Normalized();

    public void SetPitch(float degrees)
    {
        Pitch = Math.Clamp(degrees, MinPitch, MaxPitch);
        UpdateTransform();
    }

    public void SetViewUnits(float units)
    {
        ViewUnits = Math.Clamp(units, MinViewUnits, MaxViewUnits);
        UpdateTransform();
    }

    public void SetProjectionMode(IsoProjection mode)
    {
        Mode = mode;
        Projection = mode == IsoProjection.Orthographic ? ProjectionType.Orthogonal : ProjectionType.Perspective;
        Fov = PerspectiveFov;
        UpdateTransform();
    }

    /// <summary>Centres the camera on <paramref name="focus"/> at once (a new map, a teleport).</summary>
    public void Snap(Vector3 focus)
    {
        Focus = focus;
        UpdateTransform();
    }

    /// <summary>
    /// Moves the focus towards <paramref name="target"/> plus the look-ahead
    /// towards <paramref name="cursorGround"/> (horizontal only), smoothed over
    /// <paramref name="delta"/> seconds.
    /// </summary>
    public void Follow(Vector3 target, Vector3? cursorGround, double delta)
    {
        Vector3 goal = target + LookAhead(target, cursorGround);
        float t = 1f - MathF.Exp(-SmoothingRate * (float)delta);
        Focus = Focus.Lerp(goal, Math.Clamp(t, 0f, 1f));
        UpdateTransform();
    }

    /// <summary>The look-ahead offset (Godot space, horizontal) for a target and cursor ground point.</summary>
    public static Vector3 LookAhead(Vector3 target, Vector3? cursorGround)
    {
        if (cursorGround is not Vector3 c)
            return Vector3.Zero;
        var offset = new Vector3(c.X - target.X, 0, c.Z - target.Z) * LookAheadFraction;
        float max = MaxLookAheadUnits / LevelMesh.MapUnitsPerMetre;
        return offset.Length() > max ? offset.Normalized() * max : offset;
    }

    /// <summary>The camera's distance from the focus, metres.</summary>
    public float Distance => Mode == IsoProjection.Orthographic
        ? OrthoDistanceUnits / LevelMesh.MapUnitsPerMetre
        : ViewUnits / LevelMesh.MapUnitsPerMetre / 2f / MathF.Tan(Mathf.DegToRad(PerspectiveFov) / 2f);

    private void UpdateTransform()
    {
        Size = ViewUnits / LevelMesh.MapUnitsPerMetre;
        Basis basis = ViewBasis;
        Transform = new Transform3D(basis, Focus + basis.Z * Distance);
        Near = Mode == IsoProjection.Orthographic ? 1f : 0.5f;
        Far = Distance + 1000f;
    }

    /// <summary>The cursor (or the screen centre without one), viewport pixels.</summary>
    public Vector2 CursorOrCentre => Cursor ?? GetViewport().GetVisibleRect().Size / 2;

    private bool Active => InputEnabled && Current && IsInsideTree();

    public override void _Input(InputEvent e)
    {
        // Track the cursor before anything consumes the motion (a GUI over the view still moves the aim).
        if (e is InputEventMouseMotion motion && InputEnabled)
            Cursor = motion.Position;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Active)
            return;
        switch (e)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } mb
                when mb.CtrlPressed || Input.IsPhysicalKeyPressed(Key.Ctrl):
                SetViewUnits(ViewUnits / MathF.Pow(ZoomStep, mb.ButtonIndex == MouseButton.WheelUp ? 1 : -1));
                break;
            case InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.O }:
                SetProjectionMode(Mode == IsoProjection.Orthographic ? IsoProjection.Perspective : IsoProjection.Orthographic);
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }
}
