using System.Collections.Generic;

namespace IsoDoom.Sim;

/// <summary>Not vanilla: the kind of a <see cref="sim_event_t"/> (T6.10).</summary>
public enum simevent_t
{
    /// <summary>s_sound.c <c>S_StartSound</c>: <see cref="sim_event_t.sound"/> starts.</summary>
    se_startsound,

    /// <summary>s_sound.c <c>S_StopSound</c>: whatever <see cref="sim_event_t.sound"/>'s origin plays stops (p_mobj.c <c>P_RemoveMobj</c>).</summary>
    se_stopsound,

    /// <summary>A player's message (<see cref="player_t.message"/>, taken after the tic as hu_stuff.c <c>HU_Ticker</c> does).</summary>
    se_message,
}

/// <summary>
/// Not vanilla: the sound part of a <see cref="sim_event_t"/>.
/// <see cref="origin"/> is the mobj it comes from, <see cref="sector"/> the
/// sector whose <c>soundorg</c> it comes from (a door, a lift, a switch);
/// both null for a sound with no origin (vanilla's <c>S_StartSound(NULL, …)</c>:
/// heard everywhere at full volume). <see cref="sfx"/> is
/// <see cref="sfxenum_t.sfx_None"/> for a stop.
/// </summary>
public readonly record struct sound_event_t(sfxenum_t sfx, mobj_t? origin, sector_t? sector);

/// <summary>
/// Not vanilla (T6.10, SPEC §12): an event from the sim to the presentation,
/// where vanilla's game code calls the sound and HUD code directly. Output
/// only: the sim never reads them, so whether anything listens changes
/// nothing (<see cref="World.Checksum"/> included). <see cref="tic"/> is the
/// <see cref="World.leveltime"/> it happened in (the tic it belongs to: an
/// event made between tics, e.g. by a route's events, belongs to the next
/// tic). <see cref="x"/>/<see cref="y"/> (fixed_t) are the sound origin's
/// position when it started (the mobj's, or the sector's <c>soundorg</c>;
/// 0 for none), so a sound keeps a place after its mobj is gone.
/// <see cref="player"/> is the message's player (−1 otherwise).
/// </summary>
public readonly record struct sim_event_t(simevent_t type, int tic, sound_event_t sound, int x, int y, int player, string? message);

// s_sound.c's calls from the game code, and the HUD's messages, as events (T6.10).
public sealed partial class World
{
    /// <summary>
    /// Not vanilla (T6.10): the event queue, in the order the sim made the
    /// events. The presentation drains it after each tic
    /// (<see cref="DrainEvents"/>); events nobody drained are dropped when
    /// the next tic starts (<see cref="P_Ticker"/>) or a level loads
    /// (<see cref="P_SetupLevel"/>), so it holds at most one tic's worth.
    /// Not in the checksum: the sim never reads it.
    /// </summary>
    public readonly List<sim_event_t> events = new();

    /// <summary>
    /// Moves the queued events to <paramref name="into"/> (appended, in
    /// order) and empties the queue; returns how many moved. The presentation's
    /// read, after each tic.
    /// </summary>
    public int DrainEvents(List<sim_event_t> into)
    {
        int n = events.Count;
        into.AddRange(events);
        events.Clear();
        return n;
    }

    /// <summary>The sounds started among the queued events (<see cref="simevent_t.se_startsound"/>), in order (tests, the overlay).</summary>
    public List<sound_event_t> StartedSounds()
    {
        var list = new List<sound_event_t>();
        foreach (sim_event_t e in events)
        {
            if (e.type == simevent_t.se_startsound)
                list.Add(e.sound);
        }
        return list;
    }

    // Not vanilla: drops the events of earlier tics (P_Ticker, before the tic).
    private void DropStaleEvents()
    {
        int keep = 0;
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i].tic >= leveltime)
                events[keep++] = events[i];
        }
        events.RemoveRange(keep, events.Count - keep);
    }

    /// <summary>s_sound.c <c>S_StartSound</c> from a mobj (or none: heard everywhere): queues the event.</summary>
    public void S_StartSound(mobj_t? origin, sfxenum_t sfx_id) =>
        events.Add(new sim_event_t(simevent_t.se_startsound, leveltime, new sound_event_t(sfx_id, origin, null),
            origin?.x ?? 0, origin?.y ?? 0, -1, null));

    /// <summary>
    /// s_sound.c <c>S_StartSound</c> from a sector's sound origin
    /// (<c>(mobj_t *)&amp;sec-&gt;soundorg</c>, the middle of its lines' bounding box): queues the event.
    /// </summary>
    public void S_StartSound(sector_t soundorg, sfxenum_t sfx_id) =>
        events.Add(new sim_event_t(simevent_t.se_startsound, leveltime, new sound_event_t(sfx_id, null, soundorg),
            soundorg.map.SoundOrgX, soundorg.map.SoundOrgY, -1, null));

    /// <summary>
    /// s_sound.c <c>S_StopSound</c>: queues the event (vanilla stops the
    /// channels playing from <paramref name="origin"/>). Only
    /// <see cref="P_RemoveMobj"/> calls it, as vanilla's game code.
    /// </summary>
    public void S_StopSound(mobj_t origin) =>
        events.Add(new sim_event_t(simevent_t.se_stopsound, leveltime, new sound_event_t(sfxenum_t.sfx_None, origin, null),
            origin.x, origin.y, -1, null));

    /// <summary>
    /// hu_stuff.c <c>HU_Ticker</c>'s message part, the sim's side (T6.10;
    /// g_game.c <c>G_Ticker</c> runs it after <c>P_Ticker</c>): a message a
    /// player's tic left in <see cref="player_t.message"/> is queued as an
    /// event and taken (set back to null, as vanilla's <c>plr-&gt;message = 0</c>),
    /// so the same message again is queued again. Vanilla takes the console
    /// player's only (the others' are never shown); the sim takes every
    /// player's, each for its own HUD. Nothing in the sim reads the message.
    /// </summary>
    public void HU_TakeMessages()
    {
        for (int i = 0; i < MAXPLAYERS; i++)
        {
            player_t p = players[i];
            if (!playeringame[i] || p.message is not string message)
                continue;
            events.Add(new sim_event_t(simevent_t.se_message, leveltime - 1, default, 0, 0, i, message));
            p.message = null;
        }
    }
}
