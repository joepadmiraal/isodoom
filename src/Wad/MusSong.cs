using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IsoDoom.Wad;

/// <summary>
/// The MUS event types: bits 4–6 of an event's descriptor byte (the MUS
/// format; mus2mid.c's <c>musevent</c> values shifted down by 4).
/// </summary>
public enum MusEventType : byte
{
    /// <summary>Release a note: one byte, the note (mus2mid.c <c>mus_releasekey</c>).</summary>
    ReleaseKey = 0,

    /// <summary>Play a note: the note, bit 7 set when a volume byte follows (<c>mus_presskey</c>).</summary>
    PressKey = 1,

    /// <summary>Pitch wheel: one byte, 128 the centre, 0 and 255 a tone down and up (<c>mus_pitchwheel</c>).</summary>
    PitchWheel = 2,

    /// <summary>A valueless controller: one byte, 10–15 (all sounds off … reset all controllers; <c>mus_systemevent</c>).</summary>
    SystemEvent = 3,

    /// <summary>Change a controller: the controller (0 is the instrument) and its value (<c>mus_changecontroller</c>).</summary>
    ChangeController = 4,

    /// <summary>End of measure (the MUS spec's type 5, no data; never in id's songs, and mus2mid.c refuses it).</summary>
    MeasureEnd = 5,

    /// <summary>Score end: no data; the song ends (or loops) here (<c>mus_scoreend</c>).</summary>
    ScoreEnd = 6,

    /// <summary>Type 7: undefined; refused.</summary>
    Unused = 7,
}

/// <summary>
/// One MUS event, with its raw bytes: <see cref="Descriptor"/> (bit 7: the
/// last event of its group, a delay follows; bits 4–6: <see cref="Type"/>;
/// bits 0–3: <see cref="Channel"/>) and up to two data bytes as stored, so a
/// port of mus2mid.c can convert it as the C does. <see cref="Time"/> is when
/// it plays, in tics (140 Hz) from the song's start; <see cref="Delay"/> the
/// delay after its group (only on a <see cref="Last"/> event; 0 otherwise);
/// <see cref="Offset"/> where its descriptor is in the lump.
/// </summary>
public readonly record struct MusEvent(int Offset, long Time, byte Descriptor, byte Data1, byte Data2, uint Delay)
{
    /// <summary>The event's type (descriptor bits 4–6).</summary>
    public MusEventType Type => (MusEventType)((Descriptor >> 4) & 7);

    /// <summary>The MUS channel, 0–15 (15 is percussion; mus2mid.c maps it to MIDI channel 9).</summary>
    public int Channel => Descriptor & 0x0F;

    /// <summary>The last event of its group: a delay follows it (descriptor bit 7).</summary>
    public bool Last => (Descriptor & 0x80) != 0;

    /// <summary>The data bytes this event has in the lump (0–2).</summary>
    public int DataBytes => Type switch
    {
        MusEventType.ReleaseKey or MusEventType.PitchWheel or MusEventType.SystemEvent => 1,
        MusEventType.PressKey => HasVolume ? 2 : 1,
        MusEventType.ChangeController => 2,
        _ => 0,
    };

    /// <summary>A press or release's note (<see cref="Data1"/> without bit 7).</summary>
    public int Note => Data1 & 0x7F;

    /// <summary>A press with its own volume byte (<see cref="Data1"/> bit 7); without one the channel's last volume plays.</summary>
    public bool HasVolume => Type == MusEventType.PressKey && (Data1 & 0x80) != 0;

    /// <summary>A press's volume byte as stored (mus2mid.c keeps its low 7 bits); 0 without one.</summary>
    public int Volume => HasVolume ? Data2 : 0;

    /// <summary>A system event's or controller change's controller number.</summary>
    public int Controller => Type is MusEventType.SystemEvent or MusEventType.ChangeController ? Data1 : 0;

    /// <summary>A controller change's value as stored (mus2mid.c clamps values with bit 7 to 127).</summary>
    public int Value => Type == MusEventType.ChangeController ? Data2 : 0;

    /// <summary>A pitch wheel's position, 0–255, 128 the centre.</summary>
    public int Bend => Type == MusEventType.PitchWheel ? Data1 : 0;
}

/// <summary>
/// A MUS song (<c>D_*</c> lumps, the DMX library's score format, T7.8b), read
/// as Chocolate Doom's mus2mid.c reads it: the header (<c>MUS\x1a</c>, score
/// length and start, primary and secondary channel counts, instrument count),
/// the instrument list after it, then from the score start the events in
/// groups, each group's last event followed by a delay (a big-endian
/// variable-length number, 7 bits a byte, bit 7 set on every byte but the
/// last), up to the score end. Plain C#, no Godot types.
/// <para>
/// Refused (null and a message from <see cref="TryRead"/>): no header, a
/// header, instrument list or score start beyond the lump, an event or delay
/// cut off by the lump's end before the score end (mus2mid.c fails there
/// too), and an event of type 7. mus2mid.c also refuses a measure end (type
/// 5), a system event outside 10–14 and a controller outside 0–9, which the
/// MUS format allows: those are read and named in <see cref="Mus2MidError"/>.
/// The score length is not checked (mus2mid.c ignores it and reads to the
/// score end); <see cref="ScoreEndOffset"/> says where the score end was.
/// </para>
/// </summary>
public sealed class MusSong
{
    /// <summary>The header's size in bytes (the instrument list follows it).</summary>
    public const int HeaderSize = 16;

    /// <summary>The sequencer's rate: DMX plays MUS at 140 tics a second.</summary>
    public const int TicRate = 140;

    /// <summary>The percussion channel.</summary>
    public const int PercussionChannel = 15;

    /// <summary>The number of MUS channels.</summary>
    public const int NumChannels = 16;

    private readonly byte[] _data;
    private readonly MusEvent[] _events;
    private readonly ushort[] _instruments;

    /// <summary>The whole lump as read.</summary>
    public ReadOnlySpan<byte> Data => _data;

    /// <summary>The header's score length in bytes.</summary>
    public int ScoreLength { get; }

    /// <summary>The header's score start: where the first event is.</summary>
    public int ScoreStart { get; }

    /// <summary>The header's primary channel count (channels 0 … count − 1, percussion not counted).</summary>
    public int PrimaryChannels { get; }

    /// <summary>The header's secondary channel count (channels from 10; 0 in Doom's songs).</summary>
    public int SecondaryChannels { get; }

    /// <summary>The instrument list: the programs (0–127) and percussion notes (135–181) the song uses, as stored.</summary>
    public IReadOnlyList<ushort> Instruments => _instruments;

    /// <summary>Every event up to and including the score end, in lump order.</summary>
    public IReadOnlyList<MusEvent> Events => _events;

    /// <summary>The events as a span (no copy).</summary>
    public ReadOnlySpan<MusEvent> EventSpan => _events;

    /// <summary>The song's length in tics: when the score end plays.</summary>
    public long LengthTics { get; }

    /// <summary>The song's length in seconds at <see cref="TicRate"/>.</summary>
    public double Seconds => (double)LengthTics / TicRate;

    /// <summary>Bit c set when channel c has an event other than the score end.</summary>
    public int ChannelsUsed { get; }

    /// <summary>The offset of the score end's descriptor in the lump.</summary>
    public int ScoreEndOffset { get; }

    /// <summary>Why Chocolate Doom's mus2mid.c would not convert this song (so i_oplmusic.c would not play it), or null.</summary>
    public string? Mus2MidError { get; }

    private MusSong(byte[] data, int scoreLength, int scoreStart, int primary, int secondary, ushort[] instruments,
        MusEvent[] events, long length, int channels, int scoreEnd, string? mus2midError)
    {
        _data = data;
        ScoreLength = scoreLength;
        ScoreStart = scoreStart;
        PrimaryChannels = primary;
        SecondaryChannels = secondary;
        _instruments = instruments;
        _events = events;
        LengthTics = length;
        ChannelsUsed = channels;
        ScoreEndOffset = scoreEnd;
        Mus2MidError = mus2midError;
    }

    /// <summary>Whether <paramref name="lump"/> starts with <c>MUS\x1a</c>.</summary>
    public static bool HasHeader(ReadOnlySpan<byte> lump) =>
        lump.Length >= 4 && lump[0] == 'M' && lump[1] == 'U' && lump[2] == 'S' && lump[3] == 0x1A;

    /// <summary>Reads a MUS lump, or returns null with the reason in <paramref name="error"/>.</summary>
    public static MusSong? TryRead(ReadOnlySpan<byte> lump, out string? error)
    {
        error = null;
        if (!HasHeader(lump))
        {
            error = "no MUS header";
            return null;
        }
        if (lump.Length < HeaderSize)
        {
            error = $"{lump.Length} bytes, shorter than the header";
            return null;
        }
        int scoreLength = BinaryPrimitives.ReadUInt16LittleEndian(lump[4..]);
        int scoreStart = BinaryPrimitives.ReadUInt16LittleEndian(lump[6..]);
        int primary = BinaryPrimitives.ReadUInt16LittleEndian(lump[8..]);
        int secondary = BinaryPrimitives.ReadUInt16LittleEndian(lump[10..]);
        int instrumentCount = BinaryPrimitives.ReadUInt16LittleEndian(lump[12..]);
        if (HeaderSize + 2 * instrumentCount > lump.Length)
        {
            error = $"{instrumentCount} instruments, beyond the lump";
            return null;
        }
        if (scoreStart >= lump.Length)
        {
            error = $"score start {scoreStart}, beyond the lump's {lump.Length} bytes";
            return null;
        }
        var instruments = new ushort[instrumentCount];
        for (int i = 0; i < instrumentCount; i++)
            instruments[i] = BinaryPrimitives.ReadUInt16LittleEndian(lump[(HeaderSize + 2 * i)..]);

        var events = new List<MusEvent>();
        string? mus2midError = null;
        int channels = 0;
        long time = 0;
        int p = scoreStart;
        while (true)
        {
            // A group of events, the last flagged
            MusEvent e;
            while (true)
            {
                if (p >= lump.Length)
                {
                    error = $"cut off at byte {p} before the score end";
                    return null;
                }
                int offset = p;
                byte descriptor = lump[p++];
                var type = (MusEventType)((descriptor >> 4) & 7);
                byte d1 = 0, d2 = 0;
                int need = type switch
                {
                    MusEventType.ReleaseKey or MusEventType.PitchWheel or MusEventType.SystemEvent or MusEventType.PressKey => 1,
                    MusEventType.ChangeController => 2,
                    MusEventType.Unused => -1,
                    _ => 0,
                };
                if (need < 0)
                {
                    error = $"event type 7 (undefined) at byte {offset}";
                    return null;
                }
                if (need > 0)
                {
                    if (p + need > lump.Length)
                    {
                        error = $"cut off inside the event at byte {offset}";
                        return null;
                    }
                    d1 = lump[p++];
                    if (need > 1)
                        d2 = lump[p++];
                    if (type == MusEventType.PressKey && (d1 & 0x80) != 0)
                    {
                        if (p >= lump.Length)
                        {
                            error = $"cut off inside the event at byte {offset}";
                            return null;
                        }
                        d2 = lump[p++];
                    }
                }
                mus2midError ??= type switch
                {
                    MusEventType.MeasureEnd => $"a measure end (type 5) at byte {offset}",
                    MusEventType.SystemEvent when d1 is < 10 or > 14 => $"system event {d1} at byte {offset}",
                    MusEventType.ChangeController when d1 > 9 => $"controller {d1} at byte {offset}",
                    _ => null,
                };
                e = new MusEvent(offset, time, descriptor, d1, d2, 0);
                if (type == MusEventType.ScoreEnd)
                {
                    // mus2mid.c stops here, flag or not: no delay read
                    events.Add(e);
                    return new MusSong(lump.ToArray(), scoreLength, scoreStart, primary, secondary, instruments,
                        events.ToArray(), time, channels, offset, mus2midError);
                }
                channels |= 1 << e.Channel;
                if (e.Last)
                    break;
                events.Add(e);
            }

            // mus2mid.c: timedelay = timedelay * 128 + (working & 0x7F) until a byte without bit 7 (unsigned, wrapping)
            uint delay = 0;
            while (true)
            {
                if (p >= lump.Length)
                {
                    error = $"cut off inside a delay at byte {p}";
                    return null;
                }
                byte working = lump[p++];
                delay = unchecked(delay * 128 + (uint)(working & 0x7F));
                if ((working & 0x80) == 0)
                    break;
            }
            events.Add(e with { Delay = delay });
            time += delay;
        }
    }

    /// <summary><see cref="TryRead"/>, throwing <see cref="WadFormatException"/> when it fails.</summary>
    public static MusSong Read(ReadOnlySpan<byte> lump, string name = "MUS") =>
        TryRead(lump, out string? error) ?? throw new WadFormatException($"{name}: {error}");

    /// <summary>The channels used, as "0-5, 15".</summary>
    public string ChannelList()
    {
        var parts = new List<string>();
        for (int c = 0; c < NumChannels; c++)
        {
            if ((ChannelsUsed & (1 << c)) == 0)
                continue;
            int end = c;
            while (end + 1 < NumChannels && (ChannelsUsed & (1 << (end + 1))) != 0)
                end++;
            parts.Add(end == c ? c.ToString(CultureInfo.InvariantCulture) : $"{c}-{end}");
            c = end;
        }
        return parts.Count == 0 ? "none" : string.Join(", ", parts);
    }

    /// <summary>The lump list's detail: events, length in tics and seconds, channels.</summary>
    public string Summary =>
        string.Create(CultureInfo.InvariantCulture,
            $"MUS, {_events.Length} events, {LengthTics} tics, {Seconds:0.0} s at {TicRate} Hz, channels {ChannelList()}")
        + (Mus2MidError is null ? "" : $", mus2mid.c refuses it: {Mus2MidError}");

    /// <summary>The viewer's text: the header and what the events hold.</summary>
    public string Describe()
    {
        int notes = 0, maxNote = -1, minNote = 128;
        var counts = new int[8];
        foreach (MusEvent e in _events)
        {
            counts[(int)e.Type]++;
            if (e.Type == MusEventType.PressKey)
            {
                notes++;
                minNote = Math.Min(minNote, e.Note);
                maxNote = Math.Max(maxNote, e.Note);
            }
        }
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"Score: {ScoreLength} bytes from byte {ScoreStart}, ends at byte {ScoreEndOffset}; ");
        sb.Append(CultureInfo.InvariantCulture, $"{PrimaryChannels} primary and {SecondaryChannels} secondary channels; ");
        sb.Append("instruments ").Append(_instruments.Length == 0 ? "none" : string.Join(", ", _instruments)).Append('\n');
        sb.Append(CultureInfo.InvariantCulture, $"Events: {counts[1]} notes played");
        if (notes > 0)
            sb.Append(CultureInfo.InvariantCulture, $" (notes {minNote}-{maxNote})");
        sb.Append(CultureInfo.InvariantCulture, $", {counts[0]} released, {counts[2]} pitch wheel, {counts[3]} system, {counts[4]} controller, {counts[5]} measure end");
        return sb.ToString();
    }
}
