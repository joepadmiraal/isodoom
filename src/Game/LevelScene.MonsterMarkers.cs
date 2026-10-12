using System;
using System.Collections.Generic;
using System.Globalization;
using IsoDoom.Render;
using IsoDoom.Sim;

namespace IsoDoom.Game;

// T6.13u: monster markers (SPEC §12; not vanilla). A live monster with a line
// of sight to the player (the sight dim's P_CheckSight after each tic, T6.13t)
// that the level hides whole under the game camera gets a small chevron on
// top of what hides it (MonsterMarkers; whether it shows is the GPU's).
public partial class LevelScene
{
    /// <summary>The monster markers under any camera (the level check's, which has no game camera).</summary>
    public bool MarkersEverywhere { get; set; }

    /// <summary>Whether the monster markers apply now: on, under the game camera (or <see cref="MarkersEverywhere"/>).</summary>
    public bool MarkersActive => MonsterMarking && (IsoActive || MarkersEverywhere);

    // The monsters P_CheckSight gave a line of sight to the player at the last tic.
    private readonly HashSet<mobj_t> _inSight = new(ReferenceEqualityComparer.Instance);

    /// <summary>Whether <paramref name="mo"/> had a line of sight to the player at the last tic's check (T6.13t's test; false when not checked).</summary>
    public bool InPlayerSight(mobj_t mo) => _inSight.Contains(mo);

    /// <summary>Parses <c>--level-monster-markers</c>'s and <c>sprites/monster_markers</c>' value.</summary>
    public static bool ParseMonsterMarkers(string value, string what) => value switch
    {
        "on" => true,
        "off" or "vanilla" => false,
        _ => throw new ArgumentException($"{what}: \"{value}\" (on or off)"),
    };

    /// <summary>
    /// The monster markers (T6.13u, <see cref="MonsterMarkers"/>): one for each
    /// drawn live monster (<see cref="SightCandidate"/>) that had a line of
    /// sight to the player at the last tic, from its billboard's place and
    /// frame (so after the rotations), under the game camera (or
    /// <see cref="MarkersEverywhere"/>) on a level. Every actor's billboard,
    /// the player's among them, is passed on, so other monsters in front do
    /// not hide a monster. Whether each shows is the shader's: only while its
    /// monster is hidden whole.
    /// </summary>
    public void UpdateMonsterMarkers()
    {
        _markerScratch.Clear();
        _markerActorScratch.Clear();
        if (Mesh is { } mesh)
            Markers.SetSprites(mesh.Sprites);
        if (MarkersActive && _showThings && Things is { } things && World is not null && PlayerMobj is { } me
            && GameState == gamestate_t.GS_LEVEL && _inSight.Count > 0)
        {
            IReadOnlyList<ThingSprites.Entry> entries = things.Entries;
            IReadOnlyList<ThingSprites.Shown> shown = things.ShownFrames;
            for (int i = 0; i < _drawn.Count && i < entries.Count; i++)
            {
                mobj_t mo = _drawn[i];
                int actor = -1;
                if (entries[i].Actor)
                {
                    actor = _markerActorScratch.Count;
                    _markerActorScratch.Add(new ItemBeams.Actor(entries[i].MapPosition, entries[i].Radius));
                }
                if (SightCandidate(mo, me) && _inSight.Contains(mo))
                    _markerScratch.Add(new MonsterMarkers.Marker(mo, entries[i].MapPosition, shown[i].Slot, shown[i].Flip, entries[i].Radius, entries[i].Sector, actor));
            }
            if (Player is { Visible: true } player)
                _markerActorScratch.Add(new ItemBeams.Actor(player.Entry.MapPosition, player.Entry.Radius));
        }
        Markers.Set(_markerScratch, _markerActorScratch);
    }

    /// <summary>The script's <c>markers</c> lines: each marked monster's type, position and sector (whether it shows is the GPU's).</summary>
    public IEnumerable<string> MarkersText()
    {
        yield return $"monster markers: {(MonsterMarking ? "on" : "off")}{(MonsterMarking && !MarkersActive ? " (game camera only)" : "")}, {Markers.Markers.Count} marked monster(s)";
        foreach (MonsterMarkers.Marker m in Markers.Markers)
            yield return string.Create(CultureInfo.InvariantCulture, $"  {m.Mobj.type} at ({m.MapPosition.X:F0}, {m.MapPosition.Y:F0}, {m.MapPosition.Z:F0}) sector {m.Sector}");
    }
}
