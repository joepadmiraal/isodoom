//
// Copyright(C) 2005-2014 Simon Howard
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation; either version 2
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// DESCRIPTION:
//    Reading of MIDI files.
//
// C# port for IsoDoom (T7.8d) of Chocolate Doom's src/midifile.c at commit
// 895f581c5d91497bdda0516612da803fe5843e28: the file is read from a byte
// array instead of a temporary file (i_oplmusic.c writes the song to one and
// reads it back), with the C's names and behaviour: the tracks are read one
// after the other up to their end-of-track events, whatever their chunk
// sizes say; running status; four-byte variable lengths at most.

using System;
using System.Collections.Generic;

namespace IsoDoom.Audio;

/// <summary>midifile.h <c>midi_event_type_t</c>.</summary>
public enum midi_event_type_t
{
    MIDI_EVENT_NOTE_OFF = 0x80,
    MIDI_EVENT_NOTE_ON = 0x90,
    MIDI_EVENT_AFTERTOUCH = 0xA0,
    MIDI_EVENT_CONTROLLER = 0xB0,
    MIDI_EVENT_PROGRAM_CHANGE = 0xC0,
    MIDI_EVENT_CHAN_AFTERTOUCH = 0xD0,
    MIDI_EVENT_PITCH_BEND = 0xE0,
    MIDI_EVENT_SYSEX = 0xF0,
    MIDI_EVENT_SYSEX_SPLIT = 0xF7,
    MIDI_EVENT_META = 0xFF,
}

/// <summary>
/// midifile.h <c>midi_event_t</c>, its union flattened: a channel event's
/// <see cref="channel"/>, <see cref="param1"/>, <see cref="param2"/>; a meta
/// event's <see cref="meta_type"/> and <see cref="data"/>; a SysEx event's
/// <see cref="data"/>.
/// </summary>
public sealed class midi_event_t
{
    public uint delta_time;
    public midi_event_type_t event_type;
    public uint channel;
    public uint param1;
    public uint param2;
    public uint meta_type;
    public byte[] data = Array.Empty<byte>();
}

/// <summary>midifile.c <c>midi_track_t</c>.</summary>
public sealed class midi_track_t
{
    public uint data_len;
    public readonly List<midi_event_t> events = new();
    public int num_events => events.Count;
}

/// <summary>midifile.c <c>midi_track_iter_s</c>.</summary>
public sealed class midi_track_iter_t
{
    public midi_track_t track = null!;
    public uint position;
    public uint loop_point;
}

/// <summary>
/// midifile.c <c>midi_file_s</c> and its functions (<c>MIDI_LoadFile</c> is
/// <see cref="MIDI_LoadFile"/>, from bytes). i_oplmusic.c's song handle.
/// </summary>
public sealed class midi_file_t
{
    public const int MIDI_CHANNELS_PER_TRACK = 16;

    // midi_controller_t (the ones i_oplmusic.c uses)
    public const uint MIDI_CONTROLLER_VOLUME_MSB = 0x07;
    public const uint MIDI_CONTROLLER_PAN = 0x0A;
    public const uint MIDI_CONTROLLER_ALL_NOTES_OFF = 0x7B;

    // midi_meta_event_type_t (the ones i_oplmusic.c uses)
    public const uint MIDI_META_END_OF_TRACK = 0x2F;
    public const uint MIDI_META_SET_TEMPO = 0x51;

    // midi_header_t
    public ushort format_type;
    public ushort time_division;

    public midi_track_t[] tracks = Array.Empty<midi_track_t>();
    public uint num_tracks;

    /// <summary>The C's <c>FILE *</c>: the bytes and a position; <c>fgetc</c> past the end is <c>EOF</c>.</summary>
    private sealed class Stream
    {
        public readonly byte[] buf;
        public int position;

        public Stream(byte[] buf) => this.buf = buf;
    }

    // Read a single byte.  Returns false on error.
    private static bool ReadByte(out byte result, Stream stream)
    {
        if (stream.position >= stream.buf.Length)
        {
            result = 0;
            return false;
        }
        result = stream.buf[stream.position++];
        return true;
    }

    // Read a variable-length value.
    private static bool ReadVariableLength(out uint result, Stream stream)
    {
        result = 0;

        for (int i = 0; i < 4; ++i)
        {
            if (!ReadByte(out byte b, stream))
                return false;

            // Insert the bottom seven bits from this byte.
            result <<= 7;
            result |= (uint)(b & 0x7f);

            // If the top bit is not set, this is the end.
            if ((b & 0x80) == 0)
                return true;
        }

        // Variable-length value too long: maximum of four bytes
        return false;
    }

    // Read a byte sequence into the data buffer.
    private static byte[]? ReadByteSequence(uint num_bytes, Stream stream)
    {
        // The C allocates first, then fails on the first byte missing.
        if (num_bytes > stream.buf.Length - stream.position)
        {
            stream.position = stream.buf.Length;
            return null;
        }
        byte[] result = stream.buf.AsSpan(stream.position, (int)num_bytes).ToArray();
        stream.position += (int)num_bytes;
        return result;
    }

    // Read a MIDI channel event.
    // two_param indicates that the event type takes two parameters
    // (three byte) otherwise it is single parameter (two byte)
    private static bool ReadChannelEvent(midi_event_t @event, byte event_type, bool two_param, Stream stream)
    {
        // Set basics:
        @event.event_type = (midi_event_type_t)(event_type & 0xf0);
        @event.channel = (uint)(event_type & 0x0f);

        // Read parameters:
        if (!ReadByte(out byte b, stream))
            return false;

        @event.param1 = b;

        // Second parameter:
        if (two_param)
        {
            if (!ReadByte(out b, stream))
                return false;

            @event.param2 = b;
        }

        return true;
    }

    // Read sysex event:
    private static bool ReadSysExEvent(midi_event_t @event, int event_type, Stream stream)
    {
        @event.event_type = (midi_event_type_t)event_type;

        if (!ReadVariableLength(out uint length, stream))
            return false;

        // Read the byte sequence:
        byte[]? data = ReadByteSequence(length, stream);
        if (data == null)
            return false;
        @event.data = data;
        return true;
    }

    // Read meta event:
    private static bool ReadMetaEvent(midi_event_t @event, Stream stream)
    {
        @event.event_type = midi_event_type_t.MIDI_EVENT_META;

        // Read meta event type:
        if (!ReadByte(out byte b, stream))
            return false;

        @event.meta_type = b;

        // Read length of meta event data:
        if (!ReadVariableLength(out uint length, stream))
            return false;

        // Read the byte sequence:
        byte[]? data = ReadByteSequence(length, stream);
        if (data == null)
            return false;
        @event.data = data;
        return true;
    }

    private static bool ReadEvent(midi_event_t @event, ref uint last_event_type, Stream stream)
    {
        if (!ReadVariableLength(out @event.delta_time, stream))
            return false;

        if (!ReadByte(out byte event_type, stream))
            return false;

        // All event types have their top bit set.  Therefore, if
        // the top bit is not set, it is because we are using the "same
        // as previous event type" shortcut to save a byte.  Skip back
        // a byte so that we read this byte again.
        if ((event_type & 0x80) == 0)
        {
            event_type = (byte)last_event_type;
            stream.position--;
        }
        else
        {
            last_event_type = event_type;
        }

        // Check event type:
        switch ((midi_event_type_t)(event_type & 0xf0))
        {
            // Two parameter channel events:
            case midi_event_type_t.MIDI_EVENT_NOTE_OFF:
            case midi_event_type_t.MIDI_EVENT_NOTE_ON:
            case midi_event_type_t.MIDI_EVENT_AFTERTOUCH:
            case midi_event_type_t.MIDI_EVENT_CONTROLLER:
            case midi_event_type_t.MIDI_EVENT_PITCH_BEND:
                return ReadChannelEvent(@event, event_type, true, stream);

            // Single parameter channel events:
            case midi_event_type_t.MIDI_EVENT_PROGRAM_CHANGE:
            case midi_event_type_t.MIDI_EVENT_CHAN_AFTERTOUCH:
                return ReadChannelEvent(@event, event_type, false, stream);
        }

        // Specific value?
        switch ((midi_event_type_t)event_type)
        {
            case midi_event_type_t.MIDI_EVENT_SYSEX:
            case midi_event_type_t.MIDI_EVENT_SYSEX_SPLIT:
                return ReadSysExEvent(@event, event_type, stream);

            case midi_event_type_t.MIDI_EVENT_META:
                return ReadMetaEvent(@event, stream);
        }

        // Unknown MIDI event type
        return false;
    }

    // Read and check the track chunk header
    private static bool ReadTrackHeader(midi_track_t track, Stream stream)
    {
        if (stream.buf.Length - stream.position < 8)
        {
            stream.position = stream.buf.Length;
            return false;
        }
        ReadOnlySpan<byte> h = stream.buf.AsSpan(stream.position, 8);
        stream.position += 8;

        if (h[0] != 'M' || h[1] != 'T' || h[2] != 'r' || h[3] != 'k')
            return false;

        track.data_len = (uint)(h[4] << 24 | h[5] << 16 | h[6] << 8 | h[7]);
        return true;
    }

    private static bool ReadTrack(midi_track_t track, Stream stream)
    {
        // Read the header:
        if (!ReadTrackHeader(track, stream))
            return false;

        // Then the events:
        uint last_event_type = 0;

        for (;;)
        {
            // Read the next event:
            var @event = new midi_event_t();
            if (!ReadEvent(@event, ref last_event_type, stream))
                return false;

            track.events.Add(@event);

            // End of track?
            if (@event.event_type == midi_event_type_t.MIDI_EVENT_META
             && @event.meta_type == MIDI_META_END_OF_TRACK)
                break;
        }

        return true;
    }

    /// <summary>
    /// <c>MIDI_LoadFile</c> on the file's bytes: the file, or null where the
    /// C returns NULL (a bad header, not type 0 or 1, no tracks, a track cut
    /// off or with an unknown event).
    /// </summary>
    public static midi_file_t? MIDI_LoadFile(byte[] bytes)
    {
        var file = new midi_file_t();
        var stream = new Stream(bytes);

        // Read MIDI file header (ReadFileHeader)
        if (bytes.Length < 14)
            return null;
        ReadOnlySpan<byte> h = bytes.AsSpan(0, 14);
        stream.position = 14;
        uint chunk_size = (uint)(h[4] << 24 | h[5] << 16 | h[6] << 8 | h[7]);
        if (h[0] != 'M' || h[1] != 'T' || h[2] != 'h' || h[3] != 'd' || chunk_size != 6)
            return null;

        file.format_type = (ushort)(h[8] << 8 | h[9]);
        file.num_tracks = (uint)(h[10] << 8 | h[11]);
        file.time_division = (ushort)(h[12] << 8 | h[13]);

        if ((file.format_type != 0 && file.format_type != 1) || file.num_tracks < 1)
            return null;

        // Read all tracks (ReadAllTracks)
        file.tracks = new midi_track_t[file.num_tracks];
        for (int i = 0; i < file.num_tracks; ++i)
        {
            file.tracks[i] = new midi_track_t();
            if (!ReadTrack(file.tracks[i], stream))
                return null;
        }

        return file;
    }

    // Get the number of tracks in a MIDI file.
    public uint MIDI_NumTracks() => num_tracks;

    // Start iterating over the events in a track.
    public midi_track_iter_t MIDI_IterateTrack(uint track) =>
        new() { track = tracks[track], position = 0, loop_point = 0 };

    // Get the time until the next MIDI event in a track.
    public static uint MIDI_GetDeltaTime(midi_track_iter_t iter) =>
        iter.position < iter.track.num_events ? iter.track.events[(int)iter.position].delta_time : 0;

    // Get a pointer to the next MIDI event.
    public static bool MIDI_GetNextEvent(midi_track_iter_t iter, out midi_event_t? @event)
    {
        if (iter.position < iter.track.num_events)
        {
            @event = iter.track.events[(int)iter.position];
            ++iter.position;
            return true;
        }
        @event = null;
        return false;
    }

    public uint MIDI_GetFileTimeDivision()
    {
        short result = (short)time_division;

        // Negative time division indicates SMPTE time and must be handled
        // differently.
        if (result < 0)
            return (uint)(-(result / 256) * (result & 0xFF));
        return (uint)result;
    }

    public static void MIDI_RestartIterator(midi_track_iter_t iter)
    {
        iter.position = 0;
        iter.loop_point = 0;
    }
}
