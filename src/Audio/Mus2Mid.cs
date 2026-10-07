//
// Copyright(C) 1993-1996 Id Software, Inc.
// Copyright(C) 2005-2014 Simon Howard
// Copyright(C) 2006 Ben Ryves 2006
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
// mus2mid.c - Ben Ryves 2006 - http://benryves.com - benryves@benryves.com
// Use to convert a MUS file into a single track, type 0 MIDI file.
//
// C# port for IsoDoom (T7.8d) of Chocolate Doom's src/mus2mid.c at commit
// 895f581c5d91497bdda0516612da803fe5843e28, with its memio.c streams as a
// byte array and a growing buffer. Its names and quirks are kept, its
// statics too (channelvelocities, queuedtime: they carry over from one song
// to the next as in Chocolate Doom, so they live in a Mus2Mid that lives as
// long as its OplMusic).

using System;

namespace IsoDoom.Audio;

/// <summary>
/// mus2mid.c: a MUS lump to a type 0 MIDI file, exactly as Chocolate Doom
/// converts it before i_oplmusic.c plays it (T7.8d, SPEC §12). One instance
/// holds mus2mid.c's statics, which a conversion leaves for the next.
/// </summary>
public sealed class Mus2Mid
{
    private const int NUM_CHANNELS = 16;
    private const int MIDI_PERCUSSION_CHAN = 9;
    private const int MUS_PERCUSSION_CHAN = 15;

    // MUS event codes (musevent)
    private const int mus_releasekey = 0x00;
    private const int mus_presskey = 0x10;
    private const int mus_pitchwheel = 0x20;
    private const int mus_systemevent = 0x30;
    private const int mus_changecontroller = 0x40;
    private const int mus_scoreend = 0x60;

    // MIDI event codes (midievent)
    private const byte midi_releasekey = 0x80;
    private const byte midi_presskey = 0x90;
    private const byte midi_changecontroller = 0xB0;
    private const byte midi_changepatch = 0xC0;
    private const byte midi_pitchwheel = 0xE0;

    // Standard MIDI type 0 header + track header
    private static readonly byte[] midiheader =
    {
        (byte)'M', (byte)'T', (byte)'h', (byte)'d', // Main header
        0x00, 0x00, 0x00, 0x06, // Header size
        0x00, 0x00,             // MIDI type (0)
        0x00, 0x01,             // Number of tracks
        0x00, 0x46,             // Resolution
        (byte)'M', (byte)'T', (byte)'r', (byte)'k', // Start of track
        0x00, 0x00, 0x00, 0x00  // Placeholder for track length
    };

    // Cached channel velocities (static in the C: kept between songs)
    private readonly byte[] channelvelocities =
    {
        127, 127, 127, 127, 127, 127, 127, 127,
        127, 127, 127, 127, 127, 127, 127, 127
    };

    // Timestamps between sequences of MUS events (static in the C)
    private uint queuedtime;

    // Counter for the length of the track
    private uint tracksize;

    private static readonly byte[] controller_map =
    {
        0x00, 0x20, 0x01, 0x07, 0x0A, 0x0B, 0x5B, 0x5D,
        0x40, 0x43, 0x78, 0x7B, 0x7E, 0x7F, 0x79
    };

    private readonly int[] channel_map = new int[NUM_CHANNELS];

    /// <summary>memio.c's write stream (<c>mem_fopen_write</c>): a growing buffer with a position.</summary>
    private sealed class MemOut
    {
        public byte[] buf = new byte[1024];
        public int buflen;
        public int position;

        public void Write(byte b)
        {
            if (position == buf.Length)
                Array.Resize(ref buf, buf.Length * 2);
            buf[position++] = b;
            if (position > buflen)
                buflen = position;
        }
    }

    /// <summary>memio.c's read stream (<c>mem_fopen_read</c>) over the lump.</summary>
    private ref struct MemIn
    {
        public ReadOnlySpan<byte> buf;
        public int position;

        public bool ReadByte(out byte b)
        {
            if (position < buf.Length)
            {
                b = buf[position++];
                return true;
            }
            b = 0;
            return false;
        }

        public bool ReadShort(out ushort v)
        {
            if (buf.Length - position < 2)
            {
                v = 0;
                return false;
            }
            v = (ushort)(buf[position] | buf[position + 1] << 8);
            position += 2;
            return true;
        }
    }

    // Write timestamp to a MIDI file.
    private void WriteTime(uint time, MemOut midioutput)
    {
        uint buffer = time & 0x7F;
        byte writeval;

        while ((time >>= 7) != 0)
        {
            buffer <<= 8;
            buffer |= (time & 0x7F) | 0x80;
        }

        for (;;)
        {
            writeval = (byte)(buffer & 0xFF);
            midioutput.Write(writeval);
            ++tracksize;

            if ((buffer & 0x80) != 0)
            {
                buffer >>= 8;
            }
            else
            {
                queuedtime = 0;
                return;
            }
        }
    }

    // Write the end of track marker
    private void WriteEndTrack(MemOut midioutput)
    {
        WriteTime(queuedtime, midioutput);
        midioutput.Write(0xFF);
        midioutput.Write(0x2F);
        midioutput.Write(0x00);
        tracksize += 3;
    }

    // Write a key press event
    private void WritePressKey(byte channel, byte key, byte velocity, MemOut midioutput)
    {
        WriteTime(queuedtime, midioutput);
        midioutput.Write((byte)(midi_presskey | channel));
        midioutput.Write((byte)(key & 0x7F));
        midioutput.Write((byte)(velocity & 0x7F));
        tracksize += 3;
    }

    // Write a key release event
    private void WriteReleaseKey(byte channel, byte key, MemOut midioutput)
    {
        WriteTime(queuedtime, midioutput);
        midioutput.Write((byte)(midi_releasekey | channel));
        midioutput.Write((byte)(key & 0x7F));
        midioutput.Write(0);
        tracksize += 3;
    }

    // Write a pitch wheel/bend event
    private void WritePitchWheel(byte channel, short wheel, MemOut midioutput)
    {
        WriteTime(queuedtime, midioutput);
        midioutput.Write((byte)(midi_pitchwheel | channel));
        midioutput.Write((byte)(wheel & 0x7F));
        midioutput.Write((byte)((wheel >> 7) & 0x7F));
        tracksize += 3;
    }

    // Write a patch change event
    private void WriteChangePatch(byte channel, byte patch, MemOut midioutput)
    {
        WriteTime(queuedtime, midioutput);
        midioutput.Write((byte)(midi_changepatch | channel));
        midioutput.Write((byte)(patch & 0x7F));
        tracksize += 2;
    }

    // Write a valued controller change event
    private void WriteChangeController_Valued(byte channel, byte control, byte value, MemOut midioutput)
    {
        WriteTime(queuedtime, midioutput);
        midioutput.Write((byte)(midi_changecontroller | channel));
        midioutput.Write((byte)(control & 0x7F));

        // Quirk in vanilla DOOM? MUS controller values should be
        // 7-bit, not 8-bit.
        byte working = value; // & 0x7F;

        // Fix on said quirk to stop MIDI players from complaining that
        // the value is out of range:
        if ((working & 0x80) != 0)
            working = 0x7F;

        midioutput.Write(working);
        tracksize += 3;
    }

    // Write a valueless controller change event
    private void WriteChangeController_Valueless(byte channel, byte control, MemOut midioutput) =>
        WriteChangeController_Valued(channel, control, 0, midioutput);

    // Allocate a free MIDI channel.
    private int AllocateMIDIChannel()
    {
        // Find the current highest-allocated channel.
        int max = -1;
        for (int i = 0; i < NUM_CHANNELS; ++i)
        {
            if (channel_map[i] > max)
                max = channel_map[i];
        }

        // max is now equal to the highest-allocated MIDI channel.  We can
        // now allocate the next available channel.  This also works if
        // no channels are currently allocated (max=-1)
        int result = max + 1;

        // Don't allocate the MIDI percussion channel!
        if (result == MIDI_PERCUSSION_CHAN)
            ++result;

        return result;
    }

    // Given a MUS channel number, get the MIDI channel number to use
    // in the outputted file.
    private int GetMIDIChannel(int mus_channel, MemOut midioutput)
    {
        // Find the MIDI channel to use for this MUS channel.
        // MUS channel 15 is the percusssion channel.
        if (mus_channel == MUS_PERCUSSION_CHAN)
            return MIDI_PERCUSSION_CHAN;

        // If a MIDI channel hasn't been allocated for this MUS channel
        // yet, allocate the next free MIDI channel.
        if (channel_map[mus_channel] == -1)
        {
            channel_map[mus_channel] = AllocateMIDIChannel();

            // First time using the channel, send an "all notes off"
            // event. This fixes "The D_DDTBLU disease" described here:
            // https://www.doomworld.com/vb/source-ports/66802-the
            WriteChangeController_Valueless((byte)channel_map[mus_channel], 0x7b, midioutput);
        }

        return channel_map[mus_channel];
    }

    /// <summary>
    /// <c>mus2mid</c>: converts <paramref name="musinput"/> (a MUS lump; its
    /// header is not checked, as the C's <c>CHECK_MUS_HEADER</c> is off) to a
    /// MIDI file, or returns null where the C returns 1 (failure).
    /// </summary>
    public byte[]? mus2mid(ReadOnlySpan<byte> musinput)
    {
        var input = new MemIn { buf = musinput };
        var midioutput = new MemOut();
        int hitscoreend = 0;

        // Initialise channel map to mark all channels as unused.
        for (int channel = 0; channel < NUM_CHANNELS; ++channel)
            channel_map[channel] = -1;

        // Grab the header (ReadMusHeader: id, scorelength, scorestart, ...)
        if (input.buf.Length < 4)
            return null;
        input.position = 4;
        if (!input.ReadShort(out _) || !input.ReadShort(out ushort scorestart)
            || !input.ReadShort(out _) || !input.ReadShort(out _) || !input.ReadShort(out _))
            return null;

        // Seek to where the data is held (mem_fseek fails at or past the end)
        if (scorestart >= input.buf.Length)
            return null;
        input.position = scorestart;

        // So, we can assume the MUS file is faintly legit. Let's start
        // writing MIDI data...
        foreach (byte b in midiheader)
            midioutput.Write(b);
        tracksize = 0;

        // Now, process the MUS file:
        while (hitscoreend == 0)
        {
            byte eventdescriptor;

            // Handle a block of events:
            while (hitscoreend == 0)
            {
                // Fetch channel number and event code:
                if (!input.ReadByte(out eventdescriptor))
                    return null;

                byte channel = (byte)GetMIDIChannel(eventdescriptor & 0x0F, midioutput);
                int @event = eventdescriptor & 0x70;
                byte key, controllernumber, controllervalue;

                switch (@event)
                {
                    case mus_releasekey:
                        if (!input.ReadByte(out key))
                            return null;
                        WriteReleaseKey(channel, key, midioutput);
                        break;

                    case mus_presskey:
                        if (!input.ReadByte(out key))
                            return null;

                        if ((key & 0x80) != 0)
                        {
                            if (!input.ReadByte(out channelvelocities[channel]))
                                return null;
                            channelvelocities[channel] &= 0x7F;
                        }

                        WritePressKey(channel, key, channelvelocities[channel], midioutput);
                        break;

                    case mus_pitchwheel:
                        if (!input.ReadByte(out key))
                            break;
                        WritePitchWheel(channel, (short)(key * 64), midioutput);
                        break;

                    case mus_systemevent:
                        if (!input.ReadByte(out controllernumber))
                            return null;
                        if (controllernumber < 10 || controllernumber > 14)
                            return null;
                        WriteChangeController_Valueless(channel, controller_map[controllernumber], midioutput);
                        break;

                    case mus_changecontroller:
                        if (!input.ReadByte(out controllernumber))
                            return null;
                        if (!input.ReadByte(out controllervalue))
                            return null;

                        if (controllernumber == 0)
                        {
                            WriteChangePatch(channel, controllervalue, midioutput);
                        }
                        else
                        {
                            if (controllernumber < 1 || controllernumber > 9)
                                return null;
                            WriteChangeController_Valued(channel, controller_map[controllernumber], controllervalue, midioutput);
                        }
                        break;

                    case mus_scoreend:
                        hitscoreend = 1;
                        break;

                    default:
                        return null;
                }

                if ((eventdescriptor & 0x80) != 0)
                    break;
            }

            // Now we need to read the time code:
            if (hitscoreend == 0)
            {
                uint timedelay = 0;
                for (;;)
                {
                    if (!input.ReadByte(out byte working))
                        return null;

                    timedelay = timedelay * 128 + (uint)(working & 0x7F);
                    if ((working & 0x80) == 0)
                        break;
                }
                queuedtime += timedelay;
            }
        }

        // End of track
        WriteEndTrack(midioutput);

        // Write the track size into the stream
        midioutput.position = 18;
        midioutput.Write((byte)((tracksize >> 24) & 0xff));
        midioutput.Write((byte)((tracksize >> 16) & 0xff));
        midioutput.Write((byte)((tracksize >> 8) & 0xff));
        midioutput.Write((byte)(tracksize & 0xff));

        return midioutput.buf.AsSpan(0, midioutput.buflen).ToArray();
    }
}
