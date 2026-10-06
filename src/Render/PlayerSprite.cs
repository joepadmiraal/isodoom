using System;
using Godot;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Render;

/// <summary>
/// The player's billboard in the level scene (T3.3's placeholder, driven by
/// the player mobj since T4.7): one entry drawn through the things' sprite
/// path (T3.5: <see cref="ThingSprites"/>, one instance; the level's sprite
/// atlas, palette shader and sprite light). The level scene sets it every
/// frame from the player mobj, interpolated between the last two tics
/// (<see cref="Set"/>): position, facing and the state's sprite frame
/// (<c>PLAY</c> standing or running). A node of its own, apart from the other
/// things, so the visibility measure (the level script's <c>visible</c>) can
/// hide it alone.
/// </summary>
public partial class PlayerSprite : Node3D
{
    /// <summary>info.c <c>SPR_PLAY</c>'s index in <c>sprnames</c>.</summary>
    public static readonly int SprPlay = Array.IndexOf(SpriteNames.sprnames, "PLAY");

    /// <summary>info.c <c>MT_PLAYER</c>'s radius (map units): its blob shadow's (T3.6) and its wall pull (T3.5a).</summary>
    public static readonly float ShadowRadius = IsoDoom.Sim.Info.mobjinfo[(int)IsoDoom.Sim.mobjtype_t.MT_PLAYER].radius / 65536f;

    private readonly ThingSprites _sprite = new() { Name = "Sprite" };
    private ThingSprites.Entry _entry = new(Vector3.Zero, 0, 0, SprPlay, 0, false, ShadowRadius, Actor: true, Radius: ShadowRadius);
    private bool _bound;

    /// <summary>The billboard's entry as last set (map units).</summary>
    public ThingSprites.Entry Entry => _entry;

    /// <summary>Position in map units (x, y).</summary>
    public Vector2 MapPosition => new(_entry.MapPosition.X, _entry.MapPosition.Y);

    /// <summary>The height of the feet (map units: the mobj's <c>z</c>).</summary>
    public float Z => _entry.MapPosition.Z;

    /// <summary>Facing, vanilla degrees (0 = east, counterclockwise).</summary>
    public float Angle => (float)(_entry.Angle * (360.0 / 4294967296.0));

    /// <summary>The index of the sector the player stands in (its light).</summary>
    public int Sector => _entry.Sector;

    /// <summary>The rotation (0–7, vanilla's <c>rot</c>) shown last.</summary>
    public new int Rotation { get; private set; }

    /// <summary>Whether the WAD has the entry's sprite frame (else nothing is drawn).</summary>
    public bool HasSprite { get; private set; }

    /// <summary>The billboard (one instance).</summary>
    public ThingSprites Sprite => _sprite;

    public PlayerSprite()
    {
        _sprite.SetEntries(new[] { _entry });
    }

    public override void _Ready() => AddChild(_sprite);

    /// <summary>Draws from <paramref name="atlas"/> with a level's <see cref="LevelMesh.SpriteMaterial"/> (call again for each new level).</summary>
    public void Bind(SpriteAtlas atlas, ShaderMaterial material, ShaderMaterial? shadowMaterial = null)
    {
        _sprite.Bind(atlas, material, shadowMaterial);
        _bound = true;
        HasSprite = _sprite.FrameOf(_entry) is not null;
        _sprite.SetEntry(0, _entry);
    }

    /// <summary>Shows <paramref name="entry"/> (map units; the player mobj as <c>LevelScene.ThingEntry</c> makes it).</summary>
    public void Set(ThingSprites.Entry entry)
    {
        _entry = entry;
        if (_bound)
        {
            HasSprite = _sprite.FrameOf(entry) is not null;
            _sprite.SetEntry(0, entry);
        }
    }

    /// <summary>The foot point in Godot space.</summary>
    public Vector3 Foot => new Vector3(_entry.MapPosition.X, _entry.MapPosition.Z, -_entry.MapPosition.Y) / LevelMesh.MapUnitsPerMetre;

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
        _sprite.UpdateRotations(camera);
        Vector3 d = camera.Projection == Camera3D.ProjectionType.Orthogonal
            ? ThingSprites.ViewDirection(camera)
            : Foot - camera.GlobalPosition;
        if (d.X * d.X + d.Z * d.Z < 1e-8f)
            d = ThingSprites.ViewDirection(camera);
        Rotation = Sprites.R_ProjectSpriteRotation(ThingSprites.BamOf(d), _entry.Angle);
    }
}
