using System.Collections.Generic;
using System.Globalization;
using IsoDoom.Render;
using IsoDoom.Sim;

namespace IsoDoom.Game;

// T6.13u: monster markers (SPEC §12; not vanilla). A live monster with a line
// of sight to the player (the sight dim's P_CheckSight after each tic, T6.13t)
// that the level hides whole under the game camera gets a small chevron on
// top of what hides it, or a beam rising from behind it as the item beams'
// (MonsterMarkers; whether it shows is the GPU's).
public partial class LevelScene
{
    /// <summary>The monster markers under any camera (the level check's, which has no game camera).</summary>
    public bool MarkersEverywhere { get; set; }

    /// <summary>Whether the monster markers apply now: not off, under the game camera (or <see cref="MarkersEverywhere"/>).</summary>
    public bool MarkersActive => MonsterMarking != MonsterMarkerMode.Off && (IsoActive || MarkersEverywhere);

    /// <summary>
    /// T10.4: whether the Icon of Sin's brain gets a marker whatever the
    /// player's sight (<c>--level-brain-marker</c>, <c>sprites/brain_marker</c>;
    /// SPEC §12 T10.4), lit while a rocket fired now would reach it, the
    /// launcher's delay and the player's rise counted (<see cref="IconAim"/>).
    /// </summary>
    public bool BrainMarking { get; set; } = true;

    /// <summary>Whether the brain markers apply now: on, under the game camera (or <see cref="MarkersEverywhere"/>).</summary>
    public bool BrainMarksActive => BrainMarking && (IsoActive || MarkersEverywhere);

    /// <summary>T10.4: whether <paramref name="mo"/> is a live Icon of Sin's brain (<see cref="mobjtype_t.MT_BOSSBRAIN"/>).</summary>
    public static bool IsLiveBrain(mobj_t mo) => mo.type == mobjtype_t.MT_BOSSBRAIN && mo.health > 0;

    /// <summary>T10.4: how many brain markers are lit (a shot would reach the brain), as last set.</summary>
    public int HotBrains { get; private set; }

    /// <summary>Parses <c>--level-brain-marker</c>'s and <c>sprites/brain_marker</c>' value.</summary>
    public static bool ParseBrainMarker(string value, string what) => value switch
    {
        "on" => true,
        "off" or "vanilla" => false,
        _ => throw new System.ArgumentException($"{what}: \"{value}\" (on or off)"),
    };

    // The monsters P_CheckSight gave a line of sight to the player at the last tic.
    private readonly HashSet<mobj_t> _inSight = new(ReferenceEqualityComparer.Instance);

    /// <summary>Whether <paramref name="mo"/> had a line of sight to the player at the last tic's check (T6.13t's test; false when not checked).</summary>
    public bool InPlayerSight(mobj_t mo) => _inSight.Contains(mo);

    /// <summary>Parses <c>--level-monster-markers</c>'s and <c>sprites/monster_markers</c>' value (<see cref="MonsterMarkers.Parse"/>).</summary>
    public static MonsterMarkerMode ParseMonsterMarkers(string value, string what) => MonsterMarkers.Parse(value, what);

    /// <summary>
    /// The monster markers (T6.13u, <see cref="MonsterMarkers"/>): one for each
    /// drawn live monster (<see cref="SightCandidate"/>) that had a line of
    /// sight to the player at the last tic, from its billboard's place and
    /// frame (so after the rotations), under the game camera (or
    /// <see cref="MarkersEverywhere"/>) on a level. Every actor's billboard,
    /// the player's among them, is passed on, so other monsters in front do
    /// not hide a monster. Whether each shows is the shader's: only while its
    /// monster is hidden whole. T10.4: a live Icon of Sin's brain
    /// (<see cref="BrainMarking"/>) gets a chevron whatever the sight
    /// (<see cref="BrainMarkers"/>: the Icon's face stands far taller than a
    /// beam), in <see cref="MonsterMarkers.HotColor"/> while a rocket fired
    /// now would reach it (<see cref="IconAim.ClearShot"/>).
    /// </summary>
    public void UpdateMonsterMarkers()
    {
        _markerScratch.Clear();
        _markerActorScratch.Clear();
        _brainScratch.Clear();
        HotBrains = 0;
        if (Mesh is { } mesh)
        {
            Markers.SetSprites(mesh.Sprites);
            BrainMarkers.SetSprites(mesh.Sprites);
        }
        if (MonsterMarking != MonsterMarkerMode.Off)
            Markers.Mode = MonsterMarking;
        bool monsters = MarkersActive && _inSight.Count > 0, brains = BrainMarksActive;
        if ((monsters || brains) && _showThings && Things is { } things && World is { } world && PlayerMobj is { } me
            && GameState == gamestate_t.GS_LEVEL)
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
                if (monsters && SightCandidate(mo, me) && _inSight.Contains(mo))
                {
                    _markerScratch.Add(new MonsterMarkers.Marker(mo, entries[i].MapPosition, shown[i].Slot, shown[i].Flip, entries[i].Radius, entries[i].Sector, actor));
                }
                else if (brains && IsLiveBrain(mo))
                {
                    bool hot = me.health > 0 && IconAim.ClearShot(world, me, mo, IconAim.LaunchLead(world.players[world.consoleplayer]));
                    HotBrains += hot ? 1 : 0;
                    _brainScratch.Add(new MonsterMarkers.Marker(mo, entries[i].MapPosition, shown[i].Slot, shown[i].Flip, entries[i].Radius, entries[i].Sector, actor, hot));
                }
            }
            if (Player is { Visible: true } player)
                _markerActorScratch.Add(new ItemBeams.Actor(player.Entry.MapPosition, player.Entry.Radius));
        }
        Markers.Set(_markerScratch, _markerActorScratch);
        BrainMarkers.Set(_brainScratch, _markerActorScratch);
    }

    /// <summary>The script's <c>markers</c> lines: each marked monster's type, position and sector (whether it shows is the GPU's).</summary>
    public IEnumerable<string> MarkersText()
    {
        yield return $"monster markers: {MonsterMarkers.ModeName(MonsterMarking)}{(MonsterMarking != MonsterMarkerMode.Off && !MarkersActive ? " (game camera only)" : "")}, brain marker {(BrainMarking ? "on" : "off")}, {Markers.Markers.Count + BrainMarkers.Markers.Count} marked monster(s)";
        foreach (MonsterMarkers.Marker m in System.Linq.Enumerable.Concat(Markers.Markers, BrainMarkers.Markers))
            yield return string.Create(CultureInfo.InvariantCulture, $"  {m.Mobj.type} at ({m.MapPosition.X:F0}, {m.MapPosition.Y:F0}, {m.MapPosition.Z:F0}) sector {m.Sector}{(m.Hot ? " clear shot" : "")}");
    }
}
