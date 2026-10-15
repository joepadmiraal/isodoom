using System;
using System.Collections.Generic;
using Godot;

namespace IsoDoom.Render;

/// <summary>
/// The actors near each of a set of things, as the hidden test of
/// <c>shaders/billboard_hidden.gdshaderinc</c> reads them (its
/// <c>thing_actors</c>): texel (k, row) the k-th of the
/// <see cref="ItemBeams.ActorsPerBeam"/> actors nearest the row's thing within
/// <see cref="ItemBeams.ActorReach"/>, its foot from the thing's (Godot space)
/// and its radius (−1: none). Shared by the item beams (T6.13r) and the
/// monster markers (T6.13u).
/// </summary>
public sealed class NearActors
{
    private Image _image = Image.CreateEmpty(ItemBeams.ActorsPerBeam, 1, false, Image.Format.Rgbaf);
    private readonly (float Distance, int Index)[] _nearest = new (float, int)[ItemBeams.ActorsPerBeam];

    /// <summary>The texture the shader reads.</summary>
    public ImageTexture Texture { get; }

    public NearActors()
    {
        _image.Fill(new Color(0, 0, 0, -1));
        Texture = ImageTexture.CreateFromImage(_image);
    }

    /// <summary>A thing with a row: its foot (map units) and its own actor among the actors (−1: none).</summary>
    public interface IThing
    {
        Vector3 MapPosition { get; }

        int Self { get; }
    }

    /// <summary>
    /// One row per thing of <paramref name="things"/> (each's foot in map units)
    /// with its nearest of <paramref name="actors"/>, leaving out its own
    /// (<see cref="IThing.Self"/>: the thing's own billboard, when it is an
    /// actor). T8.3: generic over the things' struct, so the call every frame
    /// makes no closure or boxing.
    /// </summary>
    public void Set<T>(IReadOnlyList<T> things, IReadOnlyList<ItemBeams.Actor> actors)
        where T : struct, IThing
    {
        ArgumentNullException.ThrowIfNull(things);
        ArgumentNullException.ThrowIfNull(actors);
        int count = things.Count;
        int rows = Math.Max(count, 1);
        bool resized = _image.GetHeight() != rows;
        if (resized)
            _image = Image.CreateEmpty(ItemBeams.ActorsPerBeam, rows, false, Image.Format.Rgbaf);
        _image.Fill(new Color(0, 0, 0, -1));
        for (int i = 0; i < count; i++)
        {
            int found = 0;
            T t = things[i];
            int own = t.Self;
            Vector3 thing = t.MapPosition;
            for (int a = 0; a < actors.Count; a++)
            {
                if (a == own)
                    continue;
                Vector3 p = actors[a].MapPosition;
                float d = new Vector2(p.X - thing.X, p.Y - thing.Y).LengthSquared();
                if (d > ItemBeams.ActorReach * ItemBeams.ActorReach)
                    continue;
                // Insert by distance, the nearest first; when full, the farthest drops out.
                if (found == ItemBeams.ActorsPerBeam && d >= _nearest[found - 1].Distance)
                    continue;
                int k = found < ItemBeams.ActorsPerBeam ? found++ : ItemBeams.ActorsPerBeam - 1;
                while (k > 0 && _nearest[k - 1].Distance > d)
                {
                    _nearest[k] = _nearest[k - 1];
                    k--;
                }
                _nearest[k] = (d, a);
            }
            for (int k = 0; k < found; k++)
            {
                ItemBeams.Actor actor = actors[_nearest[k].Index];
                Vector3 offset = (actor.MapPosition - thing) / LevelMesh.MapUnitsPerMetre;
                _image.SetPixel(k, i, new Color(offset.X, offset.Z, -offset.Y, actor.Radius));
            }
        }
        if (resized)
            Texture.SetImage(_image);
        else
            Texture.Update(_image);
    }
}
