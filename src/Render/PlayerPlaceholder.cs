using System;
using Godot;
using IsoDoom.Map;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Render;

/// <summary>
/// The player stand-in of the level scene (T3.3), replaced by the player mobj
/// in T4.7: a <c>PLAY</c> sprite (frame A, the rotation vanilla's
/// r_things.c <c>R_ProjectSprite</c> picks for the camera) standing on the
/// floor of <see cref="Level.R_PointInSubsector"/> under its position.
/// Presentation only: floats, no collision. Drawn through the things' sprite
/// path (T3.5: <see cref="ThingSprites"/>, one instance; the level's sprite
/// atlas, palette shader and sprite light).
/// </summary>
public partial class PlayerPlaceholder : Node3D
{
    /// <summary>Speeds in map units per second: vanilla's top walking and running speeds (forwardmove 25 / 50 with friction: about 7.6 and 15.1 units per tic).</summary>
    public const float WalkSpeed = 264f, RunSpeed = 529f;

    /// <summary>info.c <c>SPR_PLAY</c>'s index in <c>sprnames</c>.</summary>
    private static readonly int SprPlay = Array.IndexOf(SpriteNames.sprnames, "PLAY");

    /// <summary>info.c <c>MT_PLAYER</c>'s radius (map units): its blob shadow's (T3.6).</summary>
    public static readonly float ShadowRadius = IsoDoom.Sim.Info.mobjinfo[(int)IsoDoom.Sim.mobjtype_t.MT_PLAYER].radius / 65536f;

    private readonly ThingSprites _sprite = new() { Name = "Sprite" };
    private Level? _level;
    private bool _bound;

    /// <summary>Position in map units (x, y) and the floor height under it (map units).</summary>
    public Vector2 MapPosition { get; private set; }

    public float FloorHeight { get; private set; }

    /// <summary>Facing, vanilla degrees (0 = east, counterclockwise).</summary>
    public float Angle { get; set; }

    /// <summary>The sector under the placeholder (<see cref="Level.R_PointInSubsector"/>).</summary>
    public Sector? Sector { get; private set; }

    /// <summary>The rotation (0–7, vanilla's <c>rot</c>) shown last.</summary>
    public new int Rotation { get; private set; }

    /// <summary>Whether a <c>PLAY</c> sprite was found (else nothing is drawn).</summary>
    public bool HasSprite { get; private set; }

    /// <summary>The billboard (one instance).</summary>
    public ThingSprites Sprite => _sprite;

    public PlayerPlaceholder()
    {
        _sprite.SetEntries(new[] { Entry() });
    }

    public override void _Ready() => AddChild(_sprite);

    /// <summary>Draws <c>PLAY</c> frame A from <paramref name="atlas"/> with a level's <see cref="LevelMesh.SpriteMaterial"/> (call again for each new level).</summary>
    public void Bind(SpriteAtlas atlas, ShaderMaterial material, ShaderMaterial? shadowMaterial = null)
    {
        _sprite.Bind(atlas, material, shadowMaterial);
        _bound = true;
        HasSprite = _sprite.FrameOf(Entry()) is not null;
        UpdateEntry();
    }

    /// <summary>Puts the placeholder at map point (<paramref name="x"/>, <paramref name="y"/>) (map units) on <paramref name="level"/>'s floor there.</summary>
    public void Place(Level level, float x, float y)
    {
        _level = level;
        MapPosition = new Vector2(x, y);
        UpdateFloor();
    }

    /// <summary>Moves by (<paramref name="dx"/>, <paramref name="dy"/>) map units (no collision).</summary>
    public void Move(float dx, float dy)
    {
        MapPosition += new Vector2(dx, dy);
        UpdateFloor();
    }

    /// <summary>Re-reads the floor height under the placeholder (sectors move).</summary>
    public void UpdateFloor()
    {
        if (_level is null)
            return;
        int fx = ToFixed(MapPosition.X), fy = ToFixed(MapPosition.Y);
        Sector = _level.R_PointInSubsector(fx, fy).Sector;
        FloorHeight = Sector.FloorHeight / 65536f;
        UpdateEntry();
    }

    /// <summary>The foot point in Godot space.</summary>
    public Vector3 Foot => LevelMesh.ToGodot(ToFixed(MapPosition.X), ToFixed(MapPosition.Y), FloorHeight);

    /// <summary>
    /// r_things.c <c>R_ProjectSprite</c>'s rotation for a viewer looking
    /// from map angle <paramref name="viewToThing"/> (degrees: the direction
    /// from the viewpoint to the thing) at a thing facing <paramref name="thingAngle"/>
    /// (<see cref="Sprites.R_ProjectSpriteRotation"/>).
    /// </summary>
    public static int RotationFor(float viewToThing, float thingAngle) =>
        Sprites.R_ProjectSpriteRotation(ThingSprites.BamOfDegrees(viewToThing), ThingSprites.BamOfDegrees(thingAngle));

    /// <summary>Shows the rotation <paramref name="camera"/> sees: from its position in perspective, along its view direction in orthographic.</summary>
    public void FaceCamera(Camera3D camera)
    {
        UpdateEntry();
        _sprite.UpdateRotations(camera);
        Vector3 d = camera.Projection == Camera3D.ProjectionType.Orthogonal
            ? ThingSprites.ViewDirection(camera)
            : Foot - camera.GlobalPosition;
        if (d.X * d.X + d.Z * d.Z < 1e-8f)
            d = ThingSprites.ViewDirection(camera);
        Rotation = RotationFor(Mathf.RadToDeg(MathF.Atan2(-d.Z, d.X)), Angle);
    }

    private ThingSprites.Entry Entry() =>
        new(new Vector3(MapPosition.X, MapPosition.Y, FloorHeight), ThingSprites.BamOfDegrees(Angle), Sector?.Index ?? 0, SprPlay, 0, false, ShadowRadius);

    private void UpdateEntry()
    {
        if (_bound)
            _sprite.SetEntry(0, Entry());
    }

    private static int ToFixed(float units) => (int)Math.Clamp(Math.Round(units * 65536.0), int.MinValue, int.MaxValue);
}
