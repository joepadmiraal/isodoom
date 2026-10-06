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
/// Presentation only: floats, no collision. Drawn as a Y-axis billboard
/// <see cref="Sprite3D"/> in colormap 0 (full bright) through PLAYPAL, 1 map
/// unit per patch pixel, with the patch's offsets; T3.5's sprite path
/// (atlas, palette shader, sprite light) replaces this.
/// </summary>
public partial class PlayerPlaceholder : Node3D
{
    /// <summary>Speeds in map units per second: vanilla's top walking and running speeds (forwardmove 25 / 50 with friction: about 7.6 and 15.1 units per tic).</summary>
    public const float WalkSpeed = 264f, RunSpeed = 529f;

    private readonly Sprite3D _sprite = new()
    {
        Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
        PixelSize = 1f / LevelMesh.MapUnitsPerMetre,
        Centered = false,
        Shaded = false,
        AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        Name = "Sprite",
    };

    private readonly ImageTexture?[] _rotations = new ImageTexture?[8];
    private readonly IndexedImage?[] _images = new IndexedImage?[8];
    private readonly bool[] _flip = new bool[8];
    private Level? _level;

    /// <summary>Position in map units (x, y) and the floor height under it (map units).</summary>
    public Vector2 MapPosition { get; private set; }

    public float FloorHeight { get; private set; }

    /// <summary>Facing, vanilla degrees (0 = east, counterclockwise).</summary>
    public float Angle { get; set; }

    /// <summary>The sector under the placeholder (<see cref="Level.R_PointInSubsector"/>).</summary>
    public Sector? Sector { get; private set; }

    /// <summary>The rotation (0–7, vanilla's <c>rot</c>) shown last.</summary>
    public int Rotation { get; private set; }

    /// <summary>Whether a <c>PLAY</c> sprite was found (else nothing is drawn).</summary>
    public bool HasSprite { get; private set; }

    public override void _Ready() => AddChild(_sprite);

    /// <summary>Loads <c>PLAY</c> frame A's rotations from <paramref name="wad"/> through palette 0 of <paramref name="playpal"/>.</summary>
    public void LoadSprite(WadArchive wad, Sprites sprites, Playpal playpal)
    {
        HasSprite = false;
        if (sprites.Find("PLAY") is not { NumFrames: > 0 } play)
            return;
        SpriteFrame frame = play.Frames[0];
        for (int r = 0; r < 8; r++)
        {
            int lump = frame.Lump[r];
            if (lump < 0)
                continue;
            WadLump l = wad.Lumps[lump];
            IndexedImage image = Patch.Decode(l.Data.Span, l.Name);
            _images[r] = image;
            _flip[r] = frame.Flip[r];
            _rotations[r] = ImageTexture.CreateFromImage(Image.CreateFromData(image.Width, image.Height, false, Image.Format.Rgba8, image.ToRgba(playpal.GetPalette(0))));
        }
        HasSprite = _rotations[0] is not null;
        ShowRotation(0);
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
        Position = LevelMesh.ToGodot(fx, fy, FloorHeight);
    }

    /// <summary>The foot point in Godot space.</summary>
    public Vector3 Foot => Position;

    /// <summary>
    /// r_things.c <c>R_ProjectSprite</c>'s rotation for a viewer looking
    /// from map angle <paramref name="viewToThing"/> (degrees: the direction
    /// from the viewpoint to the thing) at a thing facing <paramref name="thingAngle"/>:
    /// <c>rot = (ang - thing->angle + (unsigned)(ANG45/2)*9) >> 29</c>.
    /// </summary>
    public static int RotationFor(float viewToThing, float thingAngle)
    {
        uint ang = DegreesToBam(viewToThing), angle = DegreesToBam(thingAngle);
        const uint ANG45 = 0x20000000;
        return (int)(unchecked(ang - angle + (ANG45 / 2) * 9) >> 29);
    }

    private static uint DegreesToBam(float degrees) =>
        unchecked((uint)(long)Math.Round(Mathf.PosMod(degrees, 360f) / 360.0 * 4294967296.0));

    /// <summary>Shows the rotation <paramref name="camera"/> sees: from its position in perspective, along its view direction in orthographic.</summary>
    public void FaceCamera(Camera3D camera)
    {
        Vector3 d = camera.Projection == Camera3D.ProjectionType.Orthogonal
            ? -camera.GlobalBasis.Z
            : GlobalPosition - camera.GlobalPosition;
        if (d.X * d.X + d.Z * d.Z < 1e-8f)
            d = -camera.GlobalBasis.Y; // straight down: the top of the screen is "ahead"
        ShowRotation(RotationFor(Mathf.RadToDeg(MathF.Atan2(-d.Z, d.X)), Angle));
    }

    private void ShowRotation(int rot)
    {
        Rotation = rot;
        if (_images[rot] is not IndexedImage image)
        {
            _sprite.Texture = null;
            return;
        }
        _sprite.Texture = _rotations[rot];
        _sprite.FlipH = _flip[rot];
        // The patch's origin (leftoffset, topoffset) sits on the foot point. A flipped patch keeps the
        // same rectangle and mirrors its columns inside it, as r_things.c R_ProjectSprite does.
        _sprite.Offset = new Vector2(-image.LeftOffset, image.TopOffset - image.Height);
    }

    private static int ToFixed(float units) => (int)Math.Clamp(Math.Round(units * 65536.0), int.MinValue, int.MaxValue);
}
