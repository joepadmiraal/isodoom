using System.Collections.Generic;

namespace IsoDoom.Sim;

/// <summary>
/// Not vanilla: a sound the sim started (vanilla calls <c>S_StartSound</c>
/// directly), for the presentation to play (T5.3; T6.10 defines the event
/// queue). <see cref="origin"/> is the mobj it comes from,
/// <see cref="sector"/> the sector whose <c>soundorg</c> it comes from (a
/// door or a lift); both null for a sound with no origin (vanilla's
/// <c>S_StartSound(NULL, …)</c>, the player's own).
/// </summary>
public readonly record struct sound_event_t(sfxenum_t sfx, mobj_t? origin, sector_t? sector);

// The sounds the sim starts, as events (a stub list until T6.10).
public sealed partial class World
{
    /// <summary>
    /// Not vanilla: the sounds started since the start of the current tic
    /// (<see cref="P_Ticker"/> clears it first, as does
    /// <see cref="P_SetupLevel"/>), in order. Not in the checksum: the sim
    /// never reads it.
    /// </summary>
    public readonly List<sound_event_t> sounds = new();

    /// <summary>s_sound.c <c>S_StartSound</c> from a mobj (or none): records the event.</summary>
    public void S_StartSound(mobj_t? origin, sfxenum_t sfx_id) => sounds.Add(new sound_event_t(sfx_id, origin, null));

    /// <summary>
    /// s_sound.c <c>S_StartSound</c> from a sector's sound origin
    /// (<c>(mobj_t *)&amp;sec-&gt;soundorg</c>, the middle of its bounding box): records the event.
    /// </summary>
    public void S_StartSound(sector_t soundorg, sfxenum_t sfx_id) => sounds.Add(new sound_event_t(sfx_id, null, soundorg));
}
