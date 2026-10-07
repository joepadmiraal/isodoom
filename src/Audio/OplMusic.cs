//
// Copyright(C) 1993-1996 Id Software, Inc.
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
//   System interface for music.
//
// C# port for IsoDoom (T7.8d) of Chocolate Doom's OPL music at commit
// 895f581c5d91497bdda0516612da803fe5843e28: src/i_oplmusic.c whole (the
// sequencer over midifile.c's tracks, voices, instruments, volume, pan,
// pitch bends, the DMX versions' quirks), with what it calls of opl/:
// opl.c's OPL_WriteRegister and OPL_InitRegisters, opl_sdl.c's software
// driver (OPL_Mix_Callback, AdvanceTime, the callback queue's clock, pause
// and tempo adjustment, the timer registers kept off the chip) and
// opl_queue.c's heap. The chip is T7.8a's Nuked-OPL3 port (Opl3.cs), the
// MUS conversion mus2mid.c (Mus2Mid.cs), the MIDI reader midifile.c
// (MidiFile.cs). Not ported: the chip detection (OPL_Detect, which writes
// only the timer registers, never the chip), the hardware drivers, the
// debug messages (I_OPL_DevMessages). Integer-only as the C, but for
// MetaSetTempo's float factor (a MIDI file's tempo change), computed as the
// C computes it.

using System;

namespace IsoDoom.Audio;

/// <summary>i_sound.h <c>opl_driver_ver_t</c>: the DMX version whose OPL quirks to play (<c>I_SetOPLDriverVer</c>).</summary>
public enum opl_driver_ver_t
{
    opl_doom1_1_666,    // Doom 1 v1.666
    opl_doom2_1_666,    // Doom 2 v1.666, Hexen, Heretic
    opl_doom_1_9        // Doom v1.9, Strife
}

/// <summary>
/// Chocolate Doom's OPL music (i_oplmusic.c on opl_sdl.c's software driver,
/// T7.8d, SPEC §12): plays MUS and MIDI songs with a GENMIDI bank on an
/// emulated OPL3 (<see cref="Opl3"/>), the sequencer's timer driven by the
/// sample clock. <see cref="OPL_Mix_Callback"/> renders a block of stereo
/// samples, invoking the song's events at the samples where they fall, as
/// opl_sdl.c's SDL_mixer callback does; the <c>I_OPL_*</c> calls (and the
/// <see cref="IMusicDevice"/> calls, which are those) act between blocks.
/// Not thread-safe: a player rendering on its own thread must hold one lock
/// over every call (Chocolate Doom's <c>OPL_Lock</c> and the callback queue's
/// mutex, together).
/// </summary>
public sealed class OplMusic : IMusicDevice
{
    // --- opl.h ---------------------------------------------------------

    public const int OPL_NUM_OPERATORS = 21;
    public const int OPL_NUM_VOICES = 9;

    public const int OPL_REG_WAVEFORM_ENABLE = 0x01;
    public const int OPL_REG_TIMER1 = 0x02;
    public const int OPL_REG_TIMER2 = 0x03;
    public const int OPL_REG_TIMER_CTRL = 0x04;
    public const int OPL_REG_FM_MODE = 0x08;
    public const int OPL_REG_NEW = 0x105;

    // Operator registers (21 of each):
    public const int OPL_REGS_TREMOLO = 0x20;
    public const int OPL_REGS_LEVEL = 0x40;
    public const int OPL_REGS_ATTACK = 0x60;
    public const int OPL_REGS_SUSTAIN = 0x80;
    public const int OPL_REGS_WAVEFORM = 0xE0;

    // Voice registers (9 of each):
    public const int OPL_REGS_FREQ_1 = 0xA0;
    public const int OPL_REGS_FREQ_2 = 0xB0;
    public const int OPL_REGS_FEEDBACK = 0xC0;

    public const ulong OPL_SECOND = 1000 * 1000;

    // --- i_oplmusic.c --------------------------------------------------

    private const int MAXMIDLENGTH = 96 * 1024;
    private const int GENMIDI_NUM_INSTRS = 128;
    private const int GENMIDI_NUM_PERCUSSION = 47;
    private const ushort GENMIDI_FLAG_FIXED = 0x0001;  /* fixed pitch */
    private const ushort GENMIDI_FLAG_2VOICE = 0x0004; /* double voice (OPL3) */
    private const int PERCUSSION_LOG_LEN = 16;
    private const int MIDI_CHANNELS_PER_TRACK = midi_file_t.MIDI_CHANNELS_PER_TRACK;

    /// <summary>genmidi_op_t</summary>
    private struct genmidi_op_t
    {
        public byte tremolo, attack, sustain, waveform, scale, level;
    }

    /// <summary>genmidi_voice_t</summary>
    private struct genmidi_voice_t
    {
        public genmidi_op_t modulator;
        public byte feedback;
        public genmidi_op_t carrier;
        public byte unused;
        public short base_note_offset;
    }

    /// <summary>genmidi_instr_t, with its index in the lump's table (the C's pointer).</summary>
    private sealed class genmidi_instr_t
    {
        public int index;
        public ushort flags;
        public byte fine_tuning;
        public byte fixed_note;
        public readonly genmidi_voice_t[] voices = new genmidi_voice_t[2];
    }

    // Data associated with a channel of a track that is currently playing.
    private sealed class opl_channel_data_t
    {
        // Index in channels[] (the C compares the pointers).
        public int index;

        // The instrument currently used for this track.
        public genmidi_instr_t instrument = null!;

        // Volume level
        public int volume;
        public int volume_base;

        // Pan
        public int pan;

        // Pitch bend value:
        public int bend;
    }

    // Data associated with a track that is currently playing.
    private sealed class opl_track_data_t
    {
        // Track iterator used to read new events.
        public midi_track_iter_t iter = null!;
    }

    private sealed class opl_voice_t
    {
        // Index of this voice:
        public int index;

        // The operators used by this voice:
        public int op1, op2;

        // Array used by voice:
        public int array;

        // Currently-loaded instrument data
        public genmidi_instr_t? current_instr;

        // The voice number in the instrument to use.
        // This is normally set to zero; if this is a double voice
        // instrument, it may be one.
        public uint current_instr_voice;

        // The channel currently using this voice.
        public opl_channel_data_t? channel;

        // The midi key that this voice is playing.
        public uint key;

        // The note being played.  This is normally the same as
        // the key, but if the instrument is a fixed pitch
        // instrument, it is different.
        public uint note;

        // The frequency value being used.
        public uint freq;

        // The volume of the note being played on this channel.
        public uint note_volume;

        // The current volume (register value) that has been set for this channel.
        public uint car_volume;
        public uint mod_volume;

        // Pan.
        public uint reg_pan;

        // Priority.
        public uint priority;
    }

    // Operators used by the different voices.
    private static readonly int[][] voice_operators =
    {
        new[] { 0x00, 0x01, 0x02, 0x08, 0x09, 0x0a, 0x10, 0x11, 0x12 },
        new[] { 0x03, 0x04, 0x05, 0x0b, 0x0c, 0x0d, 0x13, 0x14, 0x15 }
    };

    // Frequency values to use for each note (the C's comments on the
    // octaves are left out; its last value is a buffer overrun, as there).
    private static readonly ushort[] frequency_curve =
    {
        0x133, 0x133, 0x134, 0x134, 0x135, 0x136, 0x136, 0x137,
        0x137, 0x138, 0x138, 0x139, 0x139, 0x13a, 0x13b, 0x13b,
        0x13c, 0x13c, 0x13d, 0x13d, 0x13e, 0x13f, 0x13f, 0x140,
        0x140, 0x141, 0x142, 0x142, 0x143, 0x143, 0x144, 0x144,
        0x145, 0x146, 0x146, 0x147, 0x147, 0x148, 0x149, 0x149,
        0x14a, 0x14a, 0x14b, 0x14c, 0x14c, 0x14d, 0x14d, 0x14e,
        0x14f, 0x14f, 0x150, 0x150, 0x151, 0x152, 0x152, 0x153,
        0x153, 0x154, 0x155, 0x155, 0x156, 0x157, 0x157, 0x158,
        0x158, 0x159, 0x15a, 0x15a, 0x15b, 0x15b, 0x15c, 0x15d,
        0x15d, 0x15e, 0x15f, 0x15f, 0x160, 0x161, 0x161, 0x162,
        0x162, 0x163, 0x164, 0x164, 0x165, 0x166, 0x166, 0x167,
        0x168, 0x168, 0x169, 0x16a, 0x16a, 0x16b, 0x16c, 0x16c,
        0x16d, 0x16e, 0x16e, 0x16f, 0x170, 0x170, 0x171, 0x172,
        0x172, 0x173, 0x174, 0x174, 0x175, 0x176, 0x176, 0x177,
        0x178, 0x178, 0x179, 0x17a, 0x17a, 0x17b, 0x17c, 0x17c,
        0x17d, 0x17e, 0x17e, 0x17f, 0x180, 0x181, 0x181, 0x182,
        0x183, 0x183, 0x184, 0x185, 0x185, 0x186, 0x187, 0x188,
        0x188, 0x189, 0x18a, 0x18a, 0x18b, 0x18c, 0x18d, 0x18d,
        0x18e, 0x18f, 0x18f, 0x190, 0x191, 0x192, 0x192, 0x193,
        0x194, 0x194, 0x195, 0x196, 0x197, 0x197, 0x198, 0x199,
        0x19a, 0x19a, 0x19b, 0x19c, 0x19d, 0x19d, 0x19e, 0x19f,
        0x1a0, 0x1a0, 0x1a1, 0x1a2, 0x1a3, 0x1a3, 0x1a4, 0x1a5,
        0x1a6, 0x1a6, 0x1a7, 0x1a8, 0x1a9, 0x1a9, 0x1aa, 0x1ab,
        0x1ac, 0x1ad, 0x1ad, 0x1ae, 0x1af, 0x1b0, 0x1b0, 0x1b1,
        0x1b2, 0x1b3, 0x1b4, 0x1b4, 0x1b5, 0x1b6, 0x1b7, 0x1b8,
        0x1b8, 0x1b9, 0x1ba, 0x1bb, 0x1bc, 0x1bc, 0x1bd, 0x1be,
        0x1bf, 0x1c0, 0x1c0, 0x1c1, 0x1c2, 0x1c3, 0x1c4, 0x1c4,
        0x1c5, 0x1c6, 0x1c7, 0x1c8, 0x1c9, 0x1c9, 0x1ca, 0x1cb,
        0x1cc, 0x1cd, 0x1ce, 0x1ce, 0x1cf, 0x1d0, 0x1d1, 0x1d2,
        0x1d3, 0x1d3, 0x1d4, 0x1d5, 0x1d6, 0x1d7, 0x1d8, 0x1d8,
        0x1d9, 0x1da, 0x1db, 0x1dc, 0x1dd, 0x1de, 0x1de, 0x1df,
        0x1e0, 0x1e1, 0x1e2, 0x1e3, 0x1e4, 0x1e5, 0x1e5, 0x1e6,
        0x1e7, 0x1e8, 0x1e9, 0x1ea, 0x1eb, 0x1ec, 0x1ed, 0x1ed,
        0x1ee, 0x1ef, 0x1f0, 0x1f1, 0x1f2, 0x1f3, 0x1f4, 0x1f5,
        0x1f6, 0x1f6, 0x1f7, 0x1f8, 0x1f9, 0x1fa, 0x1fb, 0x1fc,
        0x1fd, 0x1fe, 0x1ff, 0x200, 0x201, 0x201, 0x202, 0x203,
        0x204, 0x205, 0x206, 0x207, 0x208, 0x209, 0x20a, 0x20b,
        0x20c, 0x20d, 0x20e, 0x20f, 0x210, 0x210, 0x211, 0x212,
        0x213, 0x214, 0x215, 0x216, 0x217, 0x218, 0x219, 0x21a,
        0x21b, 0x21c, 0x21d, 0x21e, 0x21f, 0x220, 0x221, 0x222,
        0x223, 0x224, 0x225, 0x226, 0x227, 0x228, 0x229, 0x22a,
        0x22b, 0x22c, 0x22d, 0x22e, 0x22f, 0x230, 0x231, 0x232,
        0x233, 0x234, 0x235, 0x236, 0x237, 0x238, 0x239, 0x23a,
        0x23b, 0x23c, 0x23d, 0x23e, 0x23f, 0x240, 0x241, 0x242,
        0x244, 0x245, 0x246, 0x247, 0x248, 0x249, 0x24a, 0x24b,
        0x24c, 0x24d, 0x24e, 0x24f, 0x250, 0x251, 0x252, 0x253,
        0x254, 0x256, 0x257, 0x258, 0x259, 0x25a, 0x25b, 0x25c,
        0x25d, 0x25e, 0x25f, 0x260, 0x262, 0x263, 0x264, 0x265,
        0x266, 0x267, 0x268, 0x269, 0x26a, 0x26c, 0x26d, 0x26e,
        0x26f, 0x270, 0x271, 0x272, 0x273, 0x275, 0x276, 0x277,
        0x278, 0x279, 0x27a, 0x27b, 0x27d, 0x27e, 0x27f, 0x280,
        0x281, 0x282, 0x284, 0x285, 0x286, 0x287, 0x288, 0x289,
        0x28b, 0x28c, 0x28d, 0x28e, 0x28f, 0x290, 0x292, 0x293,
        0x294, 0x295, 0x296, 0x298, 0x299, 0x29a, 0x29b, 0x29c,
        0x29e, 0x29f, 0x2a0, 0x2a1, 0x2a2, 0x2a4, 0x2a5, 0x2a6,
        0x2a7, 0x2a9, 0x2aa, 0x2ab, 0x2ac, 0x2ae, 0x2af, 0x2b0,
        0x2b1, 0x2b2, 0x2b4, 0x2b5, 0x2b6, 0x2b7, 0x2b9, 0x2ba,
        0x2bb, 0x2bd, 0x2be, 0x2bf, 0x2c0, 0x2c2, 0x2c3, 0x2c4,
        0x2c5, 0x2c7, 0x2c8, 0x2c9, 0x2cb, 0x2cc, 0x2cd, 0x2ce,
        0x2d0, 0x2d1, 0x2d2, 0x2d4, 0x2d5, 0x2d6, 0x2d8, 0x2d9,
        0x2da, 0x2dc, 0x2dd, 0x2de, 0x2e0, 0x2e1, 0x2e2, 0x2e4,
        0x2e5, 0x2e6, 0x2e8, 0x2e9, 0x2ea, 0x2ec, 0x2ed, 0x2ee,
        0x2f0, 0x2f1, 0x2f2, 0x2f4, 0x2f5, 0x2f6, 0x2f8, 0x2f9,
        0x2fb, 0x2fc, 0x2fd, 0x2ff, 0x300, 0x302, 0x303, 0x304,
        0x306, 0x307, 0x309, 0x30a, 0x30b, 0x30d, 0x30e, 0x310,
        0x311, 0x312, 0x314, 0x315, 0x317, 0x318, 0x31a, 0x31b,
        0x31c, 0x31e, 0x31f, 0x321, 0x322, 0x324, 0x325, 0x327,
        0x328, 0x329, 0x32b, 0x32c, 0x32e, 0x32f, 0x331, 0x332,
        0x334, 0x335, 0x337, 0x338, 0x33a, 0x33b, 0x33d, 0x33e,
        0x340, 0x341, 0x343, 0x344, 0x346, 0x347, 0x349, 0x34a,
        0x34c, 0x34d, 0x34f, 0x350, 0x352, 0x353, 0x355, 0x357,
        0x358, 0x35a, 0x35b, 0x35d, 0x35e, 0x360, 0x361, 0x363,
        0x365, 0x366, 0x368, 0x369, 0x36b, 0x36c, 0x36e, 0x370,
        0x371, 0x373, 0x374, 0x376, 0x378, 0x379, 0x37b, 0x37c,
        0x37e, 0x380, 0x381, 0x383, 0x384, 0x386, 0x388, 0x389,
        0x38b, 0x38d, 0x38e, 0x390, 0x392, 0x393, 0x395, 0x397,
        0x398, 0x39a, 0x39c, 0x39d, 0x39f, 0x3a1, 0x3a2, 0x3a4,
        0x3a6, 0x3a7, 0x3a9, 0x3ab, 0x3ac, 0x3ae, 0x3b0, 0x3b1,
        0x3b3, 0x3b5, 0x3b7, 0x3b8, 0x3ba, 0x3bc, 0x3bd, 0x3bf,
        0x3c1, 0x3c3, 0x3c4, 0x3c6, 0x3c8, 0x3ca, 0x3cb, 0x3cd,
        0x3cf, 0x3d1, 0x3d2, 0x3d4, 0x3d6, 0x3d8, 0x3da, 0x3db,
        0x3dd, 0x3df, 0x3e1, 0x3e3, 0x3e4, 0x3e6, 0x3e8, 0x3ea,
        0x3ec, 0x3ed, 0x3ef, 0x3f1, 0x3f3, 0x3f5, 0x3f6, 0x3f8,
        0x3fa, 0x3fc, 0x3fe, 0x36c,
    };

    // Mapping from MIDI volume level to OPL level value.
    private static readonly uint[] volume_mapping_table =
    {
        0, 1, 3, 5, 6, 8, 10, 11,
        13, 14, 16, 17, 19, 20, 22, 23,
        25, 26, 27, 29, 30, 32, 33, 34,
        36, 37, 39, 41, 43, 45, 47, 49,
        50, 52, 54, 55, 57, 59, 60, 61,
        63, 64, 66, 67, 68, 69, 71, 72,
        73, 74, 75, 76, 77, 79, 80, 81,
        82, 83, 84, 84, 85, 86, 87, 88,
        89, 90, 91, 92, 92, 93, 94, 95,
        96, 96, 97, 98, 99, 99, 100, 101,
        101, 102, 103, 103, 104, 105, 105, 106,
        107, 107, 108, 109, 109, 110, 110, 111,
        112, 112, 113, 113, 114, 114, 115, 115,
        116, 117, 117, 118, 118, 119, 119, 120,
        120, 121, 121, 122, 122, 123, 123, 123,
        124, 124, 125, 125, 126, 126, 127, 127,
    };

    private opl_driver_ver_t opl_drv_ver = opl_driver_ver_t.opl_doom_1_9;
    private readonly bool music_initialized;

    private int start_music_volume;
    private int current_music_volume;

    // GENMIDI lump instrument data: main_instrs are 0-127, percussion_instrs
    // 128-174, and on to 255 as the C's pointers would read past them (a
    // program change of a MIDI file can name up to 255).
    private readonly genmidi_instr_t[] instrs = new genmidi_instr_t[256];
    private genmidi_instr_t main_instrs(int i) => instrs[i];
    private genmidi_instr_t percussion_instrs(int i) => instrs[GENMIDI_NUM_INSTRS + i];

    // Voices:
    private readonly opl_voice_t[] voices = new opl_voice_t[OPL_NUM_VOICES * 2];
    private readonly opl_voice_t[] voice_free_list = new opl_voice_t[OPL_NUM_VOICES * 2];
    private readonly opl_voice_t[] voice_alloced_list = new opl_voice_t[OPL_NUM_VOICES * 2];
    private readonly opl_voice_t[] voice_updated_list = new opl_voice_t[OPL_NUM_VOICES * 2];     // PitchBendEvent's locals
    private readonly opl_voice_t[] voice_not_updated_list = new opl_voice_t[OPL_NUM_VOICES * 2];
    private int voice_free_num;
    private int voice_alloced_num;
    private readonly int opl_opl3mode;
    private readonly int num_opl_voices;

    // Data for each channel.
    private readonly opl_channel_data_t[] channels = new opl_channel_data_t[MIDI_CHANNELS_PER_TRACK];

    // Track data for playing tracks:
    private opl_track_data_t[]? tracks;
    private uint num_tracks;
    private uint running_tracks;
    private bool song_looping;

    // Tempo control variables
    private uint ticks_per_beat;
    private uint us_per_beat;

    // Mini-log of recently played percussion instruments:
    private readonly byte[] last_perc = new byte[PERCUSSION_LOG_LEN];
    private uint last_perc_count;

    // If true, OPL sound channels are reversed to their correct arrangement
    // (as intended by the MIDI standard) rather than the backwards one
    // used by DMX due to a bug.
    private readonly bool opl_stereo_correct;

    // mus2mid.c's statics.
    private readonly Mus2Mid mus2mid = new();

    // --- opl_sdl.c -----------------------------------------------------

    // Callbacks of the queue (the C's function pointers and their data).
    private enum callback_kind { TrackTimerCallback, RestartSong }

    // Queue of callbacks waiting to be invoked.
    private readonly opl_callback_queue_t callback_queue = new();

    // Current time, in us since startup:
    private ulong current_time;

    // If non-zero, playback is currently paused.
    private int opl_sdl_paused;

    // Time offset (in us) due to the fact that callbacks
    // were previously paused.
    private ulong pause_offset;

    // OPL software emulator structure.
    private readonly opl3_chip opl_chip = new();

    private readonly int mixing_freq;

    /// <summary>
    /// <c>I_OPL_InitMusic</c> on opl_sdl.c's driver at
    /// <paramref name="sampleRate"/> (the mixing rate: 44100, 48000, …):
    /// the chip reset (<c>OPL3_Reset</c> at that rate), its registers
    /// initialised (<c>OPL_InitRegisters</c>, OPL3 mode with
    /// <paramref name="opl3"/>: Chocolate Doom's <c>snd_dmxoption</c>
    /// <c>-opl3</c>, 18 voices; else OPL2, 9), the instruments read from
    /// <paramref name="genmidi"/> (the <c>GENMIDI</c> lump; as the C, the
    /// header is not checked; bytes past the lump read as 0), the voices
    /// freed. <paramref name="stereoCorrect"/> is <c>-reverse</c>
    /// (<c>opl_stereo_correct</c>); <paramref name="driverVersion"/> is
    /// <c>I_SetOPLDriverVer</c>'s (Doom 1.9's by default);
    /// <paramref name="registerWritten"/> is <see cref="RegisterWritten"/>
    /// from the init's writes on. The music volume
    /// starts at 0, as the C's: <see cref="I_OPL_SetMusicVolume"/> it (s_sound.c's
    /// <c>S_Init</c> does).
    /// </summary>
    public OplMusic(ReadOnlySpan<byte> genmidi, int sampleRate, bool opl3 = true, bool stereoCorrect = false,
        opl_driver_ver_t driverVersion = opl_driver_ver_t.opl_doom_1_9, Action<long, ushort, byte>? registerWritten = null)
    {
        RegisterWritten = registerWritten;
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        opl_drv_ver = driverVersion;

        // OPL_SDL_Init
        mixing_freq = sampleRate;
        opl_sdl_paused = 0;
        pause_offset = 0;
        current_time = 0;
        Opl3.OPL3_Reset(opl_chip, (uint)mixing_freq);

        if (opl3)
        {
            opl_opl3mode = 1;
            num_opl_voices = OPL_NUM_VOICES * 2;
        }
        else
        {
            opl_opl3mode = 0;
            num_opl_voices = OPL_NUM_VOICES;
        }

        opl_stereo_correct = stereoCorrect;

        for (int i = 0; i < channels.Length; i++)
            channels[i] = new opl_channel_data_t { index = i };
        for (int i = 0; i < voices.Length; i++)
            voices[i] = new opl_voice_t();

        // Initialize all registers.
        OPL_InitRegisters(opl_opl3mode);

        // Load instruments from GENMIDI lump:
        LoadInstrumentTable(genmidi);

        InitVoices();

        tracks = null;
        num_tracks = 0;
        music_initialized = true;
    }

    /// <summary>The mixing rate the chip renders at.</summary>
    public int SampleRate => mixing_freq;

    /// <summary>Whether the chip plays in OPL3 mode (18 voices, stereo pan), else OPL2 (9, mono).</summary>
    public bool Opl3Mode => opl_opl3mode != 0;

    /// <summary>The DMX version whose quirks play.</summary>
    public opl_driver_ver_t DriverVersion => opl_drv_ver;

    /// <summary>The stereo samples rendered so far (<see cref="OPL_Mix_Callback"/>).</summary>
    public long SamplesRendered { get; private set; }

    /// <summary>
    /// For the tests and the reference comparison: each write to the chip
    /// (<c>OPL3_WriteRegBuffered</c>), with <see cref="SamplesRendered"/> at
    /// that moment (the write lands before that output sample).
    /// </summary>
    public Action<long, ushort, byte>? RegisterWritten;

    /// <summary>
    /// For the tests (music_ref's <c>-nochip</c>): the chip is not clocked,
    /// the output is silence; the sequencer and the writes run as ever.
    /// </summary>
    internal bool ChipOff { get; set; }

    /// <summary>The music volume, 0–127 (<c>current_music_volume</c>).</summary>
    public int MusicVolume => current_music_volume;

    /// <summary>Whether the sequencer is paused (<c>OPL_SetPaused</c>).</summary>
    public bool Paused => opl_sdl_paused != 0;

    /// <summary>The voices in use (<c>voice_alloced_num</c>).</summary>
    public int VoicesInUse => voice_alloced_num;

    /// <summary>The callbacks waiting (the song's tracks and a restart).</summary>
    public int CallbacksQueued => (int)callback_queue.num_entries;

    /// <summary>
    /// The callbacks <c>OPL_Queue_Push</c> dropped because the queue was
    /// full (a MIDI file of more than 64 tracks; the C prints a line).
    /// </summary>
    public int CallbacksDropped => callback_queue.dropped;

    public override string ToString() =>
        $"OPL{(Opl3Mode ? 3 : 2)} {mixing_freq} Hz, {(I_OPL_MusicIsPlaying() ? "playing" : "stopped")}{(Paused ? " paused" : "")}, voices {voice_alloced_num}/{num_opl_voices}, volume {current_music_volume}";

    // --- opl.c ---------------------------------------------------------

    // Write an OPL register value (OPL_WriteRegister through opl_sdl.c's
    // OPL_SDL_PortWrite and WriteRegister; the ports' delay reads do nothing
    // there).
    private void OPL_WriteRegister(int reg, int value)
    {
        int reg_num = reg & 0x1ff;
        switch (reg_num)
        {
            // Timers; DBOPL does not do timer stuff itself. Nothing reads them
            // after the detection (not ported), so they are only left out of
            // the chip.
            case OPL_REG_TIMER1:
            case OPL_REG_TIMER2:
            case OPL_REG_TIMER_CTRL:
                break;

            default:
                RegisterWritten?.Invoke(SamplesRendered, (ushort)reg_num, (byte)value);
                Opl3.OPL3_WriteRegBuffered(opl_chip, (ushort)reg_num, (byte)value);
                break;
        }
    }

    // Initialize registers on startup
    private void OPL_InitRegisters(int opl3)
    {
        int r;

        // Initialize level registers
        for (r = OPL_REGS_LEVEL; r <= OPL_REGS_LEVEL + OPL_NUM_OPERATORS; ++r)
            OPL_WriteRegister(r, 0x3f);

        // Initialize other registers
        // These two loops write to registers that actually don't exist,
        // but this is what Doom does ...
        // Similarly, the <= is also intenational.
        for (r = OPL_REGS_ATTACK; r <= OPL_REGS_WAVEFORM + OPL_NUM_OPERATORS; ++r)
            OPL_WriteRegister(r, 0x00);

        // More registers ...
        for (r = 1; r < OPL_REGS_LEVEL; ++r)
            OPL_WriteRegister(r, 0x00);

        // Re-initialize the low registers:

        // Reset both timers and enable interrupts:
        OPL_WriteRegister(OPL_REG_TIMER_CTRL, 0x60);
        OPL_WriteRegister(OPL_REG_TIMER_CTRL, 0x80);

        // "Allow FM chips to control the waveform of each operator":
        OPL_WriteRegister(OPL_REG_WAVEFORM_ENABLE, 0x20);

        if (opl3 != 0)
        {
            OPL_WriteRegister(OPL_REG_NEW, 0x01);

            // Initialize level registers
            for (r = OPL_REGS_LEVEL; r <= OPL_REGS_LEVEL + OPL_NUM_OPERATORS; ++r)
                OPL_WriteRegister(r | 0x100, 0x3f);

            // Initialize other registers
            // These two loops write to registers that actually don't exist,
            // but this is what Doom does ...
            // Similarly, the <= is also intenational.
            for (r = OPL_REGS_ATTACK; r <= OPL_REGS_WAVEFORM + OPL_NUM_OPERATORS; ++r)
                OPL_WriteRegister(r | 0x100, 0x00);

            // More registers ...
            for (r = 1; r < OPL_REGS_LEVEL; ++r)
                OPL_WriteRegister(r | 0x100, 0x00);
        }

        // Keyboard split point on (?)
        OPL_WriteRegister(OPL_REG_FM_MODE, 0x40);

        if (opl3 != 0)
            OPL_WriteRegister(OPL_REG_NEW, 0x01);
    }

    // --- opl_sdl.c: the clock ------------------------------------------

    private void OPL_SetCallback(ulong us, callback_kind callback, int data) =>
        callback_queue.OPL_Queue_Push(callback, data, current_time - pause_offset + us);

    private void OPL_ClearCallbacks() => callback_queue.OPL_Queue_Clear();

    private void OPL_SetPaused(int paused) => opl_sdl_paused = paused;

    private void OPL_AdjustCallbacks(float factor) =>
        callback_queue.OPL_Queue_AdjustCallbacks(current_time, factor);

    private void Invoke(callback_kind callback, int data)
    {
        if (callback == callback_kind.TrackTimerCallback)
            TrackTimerCallback(data);
        else
            RestartSong();
    }

    // Advance time by the specified number of samples, invoking any
    // callback functions as appropriate.
    private void AdvanceTime(uint nsamples)
    {
        // Advance time.
        ulong us = ((ulong)nsamples * OPL_SECOND) / (ulong)mixing_freq;
        current_time += us;

        if (opl_sdl_paused != 0)
            pause_offset += us;

        // Are there callbacks to invoke now?  Keep invoking them
        // until there are no more left.
        while (!callback_queue.OPL_Queue_IsEmpty()
            && current_time >= callback_queue.OPL_Queue_Peek() + pause_offset)
        {
            // Pop the callback from the queue to invoke it.
            if (!callback_queue.OPL_Queue_Pop(out callback_kind callback, out int callback_data))
                break;

            Invoke(callback, callback_data);
        }
    }

    /// <summary>
    /// opl_sdl.c <c>OPL_Mix_Callback</c>: renders
    /// <c>stereo.Length / 2</c> stereo samples (left, right interleaved)
    /// into <paramref name="stereo"/> (overwritten; Chocolate Doom mixes them
    /// into SDL_mixer's output with clipping, which over silence is the same),
    /// running the song: up to each callback's time, then its events, so a
    /// register write lands on the sample its time falls on (rounded up).
    /// Chocolate Doom calls it once per SDL_mixer buffer; its clock advances
    /// by whole microseconds per run of samples, so the block sizes are part
    /// of the timing (a few microseconds of drift, as Chocolate Doom's).
    /// </summary>
    public void OPL_Mix_Callback(Span<short> stereo)
    {
        if ((stereo.Length & 1) != 0)
            throw new ArgumentException("stereo needs whole sample pairs", nameof(stereo));

        // Repeatedly call the OPL emulator update function until the buffer is
        // full.
        uint filled = 0;
        uint buffer_samples = (uint)(stereo.Length / 2);

        while (filled < buffer_samples)
        {
            ulong next_callback_time;
            ulong nsamples;

            // Work out the time until the next callback waiting in
            // the callback queue must be invoked.  We can then fill the
            // buffer with this many samples.
            if (opl_sdl_paused != 0 || callback_queue.OPL_Queue_IsEmpty())
            {
                nsamples = buffer_samples - filled;
            }
            else
            {
                next_callback_time = callback_queue.OPL_Queue_Peek() + pause_offset;

                nsamples = (next_callback_time - current_time) * (ulong)mixing_freq;
                nsamples = (nsamples + OPL_SECOND - 1) / OPL_SECOND;

                if (nsamples > buffer_samples - filled)
                    nsamples = buffer_samples - filled;
            }

            // Add emulator output to buffer.
            FillBuffer(stereo.Slice((int)filled * 2, (int)nsamples * 2), (uint)nsamples);
            filled += (uint)nsamples;

            // Invoke callbacks for this point in time.
            AdvanceTime((uint)nsamples);
        }
    }

    // Call the OPL emulator code to fill the specified buffer.
    private void FillBuffer(Span<short> buffer, uint nsamples)
    {
        if (nsamples == 0)
            return;
        if (ChipOff)
            buffer.Clear();
        else
            Opl3.OPL3_GenerateStream(opl_chip, buffer, nsamples);
        SamplesRendered += nsamples;
    }

    // --- i_oplmusic.c --------------------------------------------------

    // Load instrument table from GENMIDI lump:
    private void LoadInstrumentTable(ReadOnlySpan<byte> lump)
    {
        // DMX does not check header
        const int GENMIDI_HEADER_LEN = 8; // strlen(GENMIDI_HEADER)
        Span<byte> b = stackalloc byte[36];
        for (int i = 0; i < instrs.Length; i++)
        {
            b.Clear();
            int at = GENMIDI_HEADER_LEN + 36 * i;
            if (at < lump.Length)
                lump.Slice(at, Math.Min(36, lump.Length - at)).CopyTo(b);
            var instr = new genmidi_instr_t
            {
                index = i,
                flags = (ushort)(b[0] | b[1] << 8),
                fine_tuning = b[2],
                fixed_note = b[3],
            };
            for (int v = 0; v < 2; v++)
            {
                ReadOnlySpan<byte> vb = b.Slice(4 + 16 * v, 16);
                instr.voices[v] = new genmidi_voice_t
                {
                    modulator = new genmidi_op_t { tremolo = vb[0], attack = vb[1], sustain = vb[2], waveform = vb[3], scale = vb[4], level = vb[5] },
                    feedback = vb[6],
                    carrier = new genmidi_op_t { tremolo = vb[7], attack = vb[8], sustain = vb[9], waveform = vb[10], scale = vb[11], level = vb[12] },
                    unused = vb[13],
                    base_note_offset = (short)(vb[14] | vb[15] << 8),
                };
            }
            instrs[i] = instr;
        }
    }

    // Get the next available voice from the freelist.
    private opl_voice_t? GetFreeVoice()
    {
        // None available?
        if (voice_free_num == 0)
            return null;

        // Remove from free list
        opl_voice_t result = voice_free_list[0];

        voice_free_num--;

        for (int i = 0; i < voice_free_num; i++)
            voice_free_list[i] = voice_free_list[i + 1];

        // Add to allocated list
        voice_alloced_list[voice_alloced_num++] = result;

        return result;
    }

    // Release a voice back to the freelist.
    private void ReleaseVoice(int index)
    {
        // Doom 2 1.666 OPL crash emulation.
        if (index >= voice_alloced_num)
        {
            voice_alloced_num = 0;
            voice_free_num = 0;
            return;
        }

        opl_voice_t voice = voice_alloced_list[index];

        VoiceKeyOff(voice);

        voice.channel = null;
        voice.note = 0;

        bool double_voice = voice.current_instr_voice != 0;

        // Remove from alloced list.
        voice_alloced_num--;

        for (int i = index; i < voice_alloced_num; i++)
            voice_alloced_list[i] = voice_alloced_list[i + 1];

        // Search to the end of the freelist (This is how Doom behaves!)
        voice_free_list[voice_free_num++] = voice;

        if (double_voice && opl_drv_ver < opl_driver_ver_t.opl_doom_1_9)
            ReleaseVoice(index);
    }

    // Load data to the specified operator
    private void LoadOperatorData(int @operator, in genmidi_op_t data, bool max_level, out uint volume)
    {
        // The scale and level fields must be combined for the level register.
        // For the carrier wave we always set the maximum level.
        int level = data.scale;

        if (max_level)
            level |= 0x3f;
        else
            level |= data.level;

        volume = (uint)level;

        OPL_WriteRegister(OPL_REGS_LEVEL + @operator, level);
        OPL_WriteRegister(OPL_REGS_TREMOLO + @operator, data.tremolo);
        OPL_WriteRegister(OPL_REGS_ATTACK + @operator, data.attack);
        OPL_WriteRegister(OPL_REGS_SUSTAIN + @operator, data.sustain);
        OPL_WriteRegister(OPL_REGS_WAVEFORM + @operator, data.waveform);
    }

    // Set the instrument for a particular voice.
    private void SetVoiceInstrument(opl_voice_t voice, genmidi_instr_t instr, uint instr_voice)
    {
        // Instrument already set for this channel?
        if (voice.current_instr == instr && voice.current_instr_voice == instr_voice)
            return;

        voice.current_instr = instr;
        voice.current_instr_voice = instr_voice;

        ref readonly genmidi_voice_t data = ref instr.voices[instr_voice];

        // Are we usind modulated feedback mode?
        bool modulating = (data.feedback & 0x01) == 0;

        // Doom loads the second operator first, then the first.
        // The carrier is set to minimum volume until the voice volume
        // is set in SetVoiceVolume (below).  If we are not using
        // modulating mode, we must set both to minimum volume.
        LoadOperatorData(voice.op2 | voice.array, data.carrier, true, out voice.car_volume);
        LoadOperatorData(voice.op1 | voice.array, data.modulator, !modulating, out voice.mod_volume);

        // Set feedback register that control the connection between the
        // two operators.  Turn on bits in the upper nybble; I think this
        // is for OPL3, where it turns on channel A/B.
        OPL_WriteRegister((OPL_REGS_FEEDBACK + voice.index) | voice.array, (int)(data.feedback | voice.reg_pan));

        // Calculate voice priority.
        voice.priority = (uint)(0x0f - (data.carrier.attack >> 4) + 0x0f - (data.carrier.sustain & 0x0f));
    }

    // volume_mapping_table[i]; past its end (a MIDI file's velocity above
    // 127, which the C reads out of bounds) the last value. Not vanilla.
    private static uint VolumeMapping(uint i) => volume_mapping_table[Math.Min(i, 127u)];

    private void SetVoiceVolume(opl_voice_t voice, uint volume)
    {
        voice.note_volume = volume;

        ref readonly genmidi_voice_t opl_voice = ref voice.current_instr!.voices[voice.current_instr_voice];

        // Multiply note volume and channel volume to get the actual volume.
        uint midi_volume = 2 * (VolumeMapping((uint)voice.channel!.volume) + 1);

        uint full_volume = (VolumeMapping(voice.note_volume) * midi_volume) >> 9;

        // The volume value to use in the register:
        uint car_volume = 0x3f - full_volume;

        // Update the volume register(s) if necessary.
        if (car_volume != (voice.car_volume & 0x3f))
        {
            voice.car_volume = car_volume | (voice.car_volume & 0xc0);

            OPL_WriteRegister((OPL_REGS_LEVEL + voice.op2) | voice.array, (int)voice.car_volume);

            // If we are using non-modulated feedback mode, we must set the
            // volume for both voices.
            if ((opl_voice.feedback & 0x01) != 0 && opl_voice.modulator.level != 0x3f)
            {
                uint mod_volume = opl_voice.modulator.level;
                if (mod_volume < car_volume)
                    mod_volume = car_volume;

                mod_volume |= voice.mod_volume & 0xc0;

                if (mod_volume != voice.mod_volume)
                {
                    voice.mod_volume = mod_volume;
                    OPL_WriteRegister((OPL_REGS_LEVEL + voice.op1) | voice.array,
                        (int)(mod_volume | (uint)(opl_voice.modulator.scale & 0xc0)));
                }
            }
        }
    }

    private void SetVoicePan(opl_voice_t voice, uint pan)
    {
        voice.reg_pan = pan;
        ref readonly genmidi_voice_t opl_voice = ref voice.current_instr!.voices[voice.current_instr_voice];

        OPL_WriteRegister((OPL_REGS_FEEDBACK + voice.index) | voice.array, (int)(opl_voice.feedback | pan));
    }

    // Initialize the voice table and freelist
    private void InitVoices()
    {
        // Start with an empty free list.
        voice_free_num = num_opl_voices;
        voice_alloced_num = 0;

        // Initialize each voice.
        for (int i = 0; i < num_opl_voices; ++i)
        {
            voices[i].index = i % OPL_NUM_VOICES;
            voices[i].op1 = voice_operators[0][i % OPL_NUM_VOICES];
            voices[i].op2 = voice_operators[1][i % OPL_NUM_VOICES];
            voices[i].array = (i / OPL_NUM_VOICES) << 8;
            voices[i].current_instr = null;

            // Add this voice to the freelist.
            voice_free_list[i] = voices[i];
        }
    }

    /// <summary><c>I_OPL_SetMusicVolume</c>: the music volume, 0–127 (the notes' levels change, not a gain).</summary>
    public void I_OPL_SetMusicVolume(int volume)
    {
        if (current_music_volume == volume)
            return;

        // Internal state variable.
        current_music_volume = volume;

        // Update the volume of all voices.
        for (int i = 0; i < MIDI_CHANNELS_PER_TRACK; ++i)
        {
            if (i == 15)
                SetChannelVolume(channels[i], (uint)volume, false);
            else
                SetChannelVolume(channels[i], (uint)channels[i].volume_base, false);
        }
    }

    private void VoiceKeyOff(opl_voice_t voice) =>
        OPL_WriteRegister((OPL_REGS_FREQ_2 + voice.index) | voice.array, (int)(voice.freq >> 8));

    private opl_channel_data_t TrackChannelForEvent(midi_event_t @event)
    {
        uint channel_num = @event.channel;

        // MIDI uses track #9 for percussion, but for MUS it's track #15
        // instead. Because DMX works on MUS data internally, we need to
        // swap back to the MUS version of the channel number.
        if (channel_num == 9)
            channel_num = 15;
        else if (channel_num == 15)
            channel_num = 9;

        return channels[channel_num];
    }

    private void KeyOffEvent(midi_event_t @event)
    {
        opl_channel_data_t channel = TrackChannelForEvent(@event);
        uint key = @event.param1;

        // Turn off voices being used to play this key.
        // If it is a double voice instrument there will be two.
        for (int i = 0; i < voice_alloced_num; i++)
        {
            if (voice_alloced_list[i].channel == channel && voice_alloced_list[i].key == key)
            {
                // Finished with this voice now.
                ReleaseVoice(i);
                i--;
            }
        }
    }

    // When all voices are in use, we must discard an existing voice to
    // play a new note.  Find and free an existing voice.  The channel
    // passed to the function is the channel for the new note to be
    // played.
    private void ReplaceExistingVoice()
    {
        // Check the allocated voices, if we find an instrument that is
        // of a lower priority to the new instrument, discard it.
        // If a voice is being used to play the second voice of an instrument,
        // use that, as second voices are non-essential.
        // Lower numbered MIDI channels implicitly have a higher priority
        // than higher-numbered channels, eg. MIDI channel 1 is never
        // discarded for MIDI channel 2.
        int result = 0;

        for (int i = 0; i < voice_alloced_num; i++)
        {
            if (voice_alloced_list[i].current_instr_voice != 0
             || voice_alloced_list[i].channel!.index >= voice_alloced_list[result].channel!.index)
                result = i;
        }

        ReleaseVoice(result);
    }

    // Alternate versions of ReplaceExistingVoice() used when emulating old
    // versions of the DMX library used in Doom 1.666, Heretic and Hexen.
    private void ReplaceExistingVoiceDoom1()
    {
        int result = 0;

        for (int i = 0; i < voice_alloced_num; i++)
        {
            if (voice_alloced_list[i].channel!.index > voice_alloced_list[result].channel!.index)
                result = i;
        }

        ReleaseVoice(result);
    }

    private void ReplaceExistingVoiceDoom2(opl_channel_data_t channel)
    {
        int result = 0;
        int priority = 0x8000;

        for (int i = 0; i < voice_alloced_num - 3; i++)
        {
            if (voice_alloced_list[i].priority < (uint)priority
             && voice_alloced_list[i].channel!.index >= channel.index)
            {
                priority = (int)voice_alloced_list[i].priority;
                result = i;
            }
        }

        ReleaseVoice(result);
    }

    private uint FrequencyForVoice(opl_voice_t voice)
    {
        int note = (int)voice.note;

        // Apply note offset.
        // Don't apply offset if the instrument is a fixed note instrument.
        ref readonly genmidi_voice_t gm_voice = ref voice.current_instr!.voices[voice.current_instr_voice];

        if ((voice.current_instr.flags & GENMIDI_FLAG_FIXED) == 0)
            note += gm_voice.base_note_offset;

        // Avoid possible overflow due to base note offset:
        while (note < 0)
            note += 12;

        while (note > 95)
            note -= 12;

        int freq_index = 64 + 32 * note + voice.channel!.bend;

        // If this is the second voice of a double voice instrument, the
        // frequency index can be adjusted by the fine tuning field.
        if (voice.current_instr_voice != 0)
            freq_index += (voice.current_instr.fine_tuning / 2) - 64;

        if (freq_index < 0)
            freq_index = 0;

        // The first 7 notes use the start of the table, while
        // consecutive notes loop around the latter part.
        if (freq_index < 284)
            return frequency_curve[freq_index];

        uint sub_index = (uint)(freq_index - 284) % (12 * 32);
        uint octave = (uint)(freq_index - 284) / (12 * 32);

        // Once the seventh octave is reached, things break down.
        // We can only go up to octave 7 as a maximum anyway (the OPL
        // register only has three bits for octave number), but for the
        // notes in octave 7, the first five bits have octave=7, the
        // following notes have octave=6.  This 7/6 pattern repeats in
        // following octaves (which are technically impossible to
        // represent anyway).
        if (octave >= 7)
            octave = 7;

        // Calculate the resulting register value to use for the frequency.
        return frequency_curve[sub_index + 284] | (octave << 10);
    }

    // Update the frequency that a voice is programmed to use.
    private void UpdateVoiceFrequency(opl_voice_t voice)
    {
        // Calculate the frequency to use for this voice and update it
        // if neccessary.
        uint freq = FrequencyForVoice(voice);

        if (voice.freq != freq)
        {
            OPL_WriteRegister((OPL_REGS_FREQ_1 + voice.index) | voice.array, (int)(freq & 0xff));
            OPL_WriteRegister((OPL_REGS_FREQ_2 + voice.index) | voice.array, (int)((freq >> 8) | 0x20));

            voice.freq = freq;
        }
    }

    // Program a single voice for an instrument.  For a double voice
    // instrument (GENMIDI_FLAG_2VOICE), this is called twice for each
    // key on event.
    private void VoiceKeyOn(opl_channel_data_t channel, genmidi_instr_t instrument, uint instrument_voice,
        uint note, uint key, uint volume)
    {
        if (opl_opl3mode == 0 && opl_drv_ver == opl_driver_ver_t.opl_doom1_1_666)
            instrument_voice = 0;

        // Find a voice to use for this new note.
        opl_voice_t? voice = GetFreeVoice();

        if (voice == null)
            return;

        voice.channel = channel;
        voice.key = key;

        // Work out the note to use.  This is normally the same as
        // the key, unless it is a fixed pitch instrument.
        if ((instrument.flags & GENMIDI_FLAG_FIXED) != 0)
            voice.note = instrument.fixed_note;
        else
            voice.note = note;

        voice.reg_pan = (uint)channel.pan;

        // Program the voice with the instrument data:
        SetVoiceInstrument(voice, instrument, instrument_voice);

        // Set the volume level.
        SetVoiceVolume(voice, volume);

        // Write the frequency value to turn the note on.
        voice.freq = 0;
        UpdateVoiceFrequency(voice);
    }

    private void KeyOnEvent(midi_event_t @event)
    {
        genmidi_instr_t instrument;
        uint note = @event.param1;
        uint key = @event.param1;
        uint volume = @event.param2;

        // A volume of zero means key off. Some MIDI tracks, eg. the ones
        // in AV.wad, use a second key on with a volume of zero to mean
        // key off.
        if (volume <= 0)
        {
            KeyOffEvent(@event);
            return;
        }

        // The channel.
        opl_channel_data_t channel = TrackChannelForEvent(@event);

        // Percussion channel is treated differently.
        if (@event.channel == 9)
        {
            if (key < 35 || key > 81)
                return;

            instrument = percussion_instrs((int)key - 35);

            last_perc[last_perc_count] = (byte)key;
            last_perc_count = (last_perc_count + 1) % PERCUSSION_LOG_LEN;
            note = 60;
        }
        else
        {
            instrument = channel.instrument;
        }

        bool double_voice = (instrument.flags & GENMIDI_FLAG_2VOICE) != 0;

        switch (opl_drv_ver)
        {
            case opl_driver_ver_t.opl_doom1_1_666:
                int voicenum = (double_voice ? 1 : 0) + 1;
                if (opl_opl3mode == 0)
                    voicenum = 1;
                while (voice_alloced_num > num_opl_voices - voicenum)
                    ReplaceExistingVoiceDoom1();

                // Find and program a voice for this instrument.  If this
                // is a double voice instrument, we must do this twice.
                if (double_voice)
                    VoiceKeyOn(channel, instrument, 1, note, key, volume);

                VoiceKeyOn(channel, instrument, 0, note, key, volume);
                break;

            case opl_driver_ver_t.opl_doom2_1_666:
                if (voice_alloced_num == num_opl_voices)
                    ReplaceExistingVoiceDoom2(channel);
                if (voice_alloced_num == num_opl_voices - 1 && double_voice)
                    ReplaceExistingVoiceDoom2(channel);

                // Find and program a voice for this instrument.  If this
                // is a double voice instrument, we must do this twice.
                if (double_voice)
                    VoiceKeyOn(channel, instrument, 1, note, key, volume);

                VoiceKeyOn(channel, instrument, 0, note, key, volume);
                break;

            default:
            case opl_driver_ver_t.opl_doom_1_9:
                if (voice_free_num == 0)
                    ReplaceExistingVoice();

                // Find and program a voice for this instrument.  If this
                // is a double voice instrument, we must do this twice.
                VoiceKeyOn(channel, instrument, 0, note, key, volume);

                if (double_voice)
                    VoiceKeyOn(channel, instrument, 1, note, key, volume);
                break;
        }
    }

    private void ProgramChangeEvent(midi_event_t @event)
    {
        // Set the instrument used on this channel.
        opl_channel_data_t channel = TrackChannelForEvent(@event);
        int instrument = (int)@event.param1;
        channel.instrument = main_instrs(instrument);

        // TODO: Look through existing voices that are turned on on this
        // channel, and change the instrument.
    }

    private void SetChannelVolume(opl_channel_data_t channel, uint volume, bool clip_start)
    {
        channel.volume_base = (int)volume;

        if (volume > (uint)current_music_volume)
            volume = (uint)current_music_volume;

        if (clip_start && volume > (uint)start_music_volume)
            volume = (uint)start_music_volume;

        channel.volume = (int)volume;

        // Update all voices that this channel is using.
        for (int i = 0; i < num_opl_voices; ++i)
        {
            if (voices[i].channel == channel)
                SetVoiceVolume(voices[i], voices[i].note_volume);
        }
    }

    private void SetChannelPan(opl_channel_data_t channel, uint pan)
    {
        // The DMX library has the stereo channels backwards, maybe because
        // Paul Radek had a Soundblaster card with the channels reversed, or
        // perhaps it was just a bug in the OPL3 support that was never
        // finished. By default we preserve this bug, but we also provide a
        // secret DMXOPTION to fix it.
        if (opl_stereo_correct)
            pan = 144 - pan;

        if (opl_opl3mode != 0)
        {
            uint reg_pan;
            if (pan >= 96)
                reg_pan = 0x10;
            else if (pan <= 48)
                reg_pan = 0x20;
            else
                reg_pan = 0x30;
            if (channel.pan != reg_pan)
            {
                channel.pan = (int)reg_pan;
                for (int i = 0; i < num_opl_voices; i++)
                {
                    if (voices[i].channel == channel)
                        SetVoicePan(voices[i], reg_pan);
                }
            }
        }
    }

    // Handler for the MIDI_CONTROLLER_ALL_NOTES_OFF channel event.
    private void AllNotesOff(opl_channel_data_t channel, uint param)
    {
        for (int i = 0; i < voice_alloced_num; i++)
        {
            if (voice_alloced_list[i].channel == channel)
            {
                // Finished with this voice now.
                ReleaseVoice(i);
                i--;
            }
        }
    }

    private void ControllerEvent(midi_event_t @event)
    {
        opl_channel_data_t channel = TrackChannelForEvent(@event);
        uint controller = @event.param1;
        uint param = @event.param2;

        switch (controller)
        {
            case midi_file_t.MIDI_CONTROLLER_VOLUME_MSB:
                SetChannelVolume(channel, param, true);
                break;

            case midi_file_t.MIDI_CONTROLLER_PAN:
                SetChannelPan(channel, param);
                break;

            case midi_file_t.MIDI_CONTROLLER_ALL_NOTES_OFF:
                AllNotesOff(channel, param);
                break;
        }
    }

    // Process a pitch bend event.
    private void PitchBendEvent(midi_event_t @event)
    {
        int voice_updated_num = 0;
        int voice_not_updated_num = 0;

        // Update the channel bend value.  Only the MSB of the pitch bend
        // value is considered: this is what Doom does.
        opl_channel_data_t channel = TrackChannelForEvent(@event);
        channel.bend = (int)@event.param2 - 64;

        // Update all voices for this channel.
        for (int i = 0; i < voice_alloced_num; ++i)
        {
            if (voice_alloced_list[i].channel == channel)
            {
                UpdateVoiceFrequency(voice_alloced_list[i]);
                voice_updated_list[voice_updated_num++] = voice_alloced_list[i];
            }
            else
            {
                voice_not_updated_list[voice_not_updated_num++] = voice_alloced_list[i];
            }
        }

        for (int i = 0; i < voice_not_updated_num; i++)
            voice_alloced_list[i] = voice_not_updated_list[i];

        for (int i = 0; i < voice_updated_num; i++)
            voice_alloced_list[i + voice_not_updated_num] = voice_updated_list[i];
    }

    private void MetaSetTempo(uint tempo)
    {
        OPL_AdjustCallbacks((float)us_per_beat / tempo);
        us_per_beat = tempo;
    }

    // Process a meta event.
    private void MetaEvent(midi_event_t @event)
    {
        byte[] data = @event.data;
        int data_len = data.Length;

        switch (@event.meta_type)
        {
            case midi_file_t.MIDI_META_SET_TEMPO:
                if (data_len == 3)
                    MetaSetTempo((uint)(data[0] << 16 | data[1] << 8 | data[2]));
                break;

            // Things we can just ignore, and the end of track, handled when
            // we run out of events in the track, see below.
            default:
                break;
        }
    }

    // Process a MIDI event from a track.
    private void ProcessEvent(midi_event_t @event)
    {
        switch (@event.event_type)
        {
            case midi_event_type_t.MIDI_EVENT_NOTE_OFF:
                KeyOffEvent(@event);
                break;

            case midi_event_type_t.MIDI_EVENT_NOTE_ON:
                KeyOnEvent(@event);
                break;

            case midi_event_type_t.MIDI_EVENT_CONTROLLER:
                ControllerEvent(@event);
                break;

            case midi_event_type_t.MIDI_EVENT_PROGRAM_CHANGE:
                ProgramChangeEvent(@event);
                break;

            case midi_event_type_t.MIDI_EVENT_PITCH_BEND:
                PitchBendEvent(@event);
                break;

            case midi_event_type_t.MIDI_EVENT_META:
                MetaEvent(@event);
                break;

            // SysEx events can be ignored.
            default:
                break;
        }
    }

    // Restart a song from the beginning.
    private void RestartSong()
    {
        running_tracks = num_tracks;

        start_music_volume = current_music_volume;

        for (int i = 0; i < num_tracks; ++i)
        {
            midi_file_t.MIDI_RestartIterator(tracks![i].iter);
            ScheduleTrack(i);
        }

        for (int i = 0; i < MIDI_CHANNELS_PER_TRACK; ++i)
            InitChannel(channels[i]);
    }

    // Callback function invoked when another event needs to be read from
    // a track.
    private void TrackTimerCallback(int track)
    {
        // Get the next event and process it.
        if (!midi_file_t.MIDI_GetNextEvent(tracks![track].iter, out midi_event_t? @event))
            return;

        ProcessEvent(@event!);

        // End of track?
        if (@event!.event_type == midi_event_type_t.MIDI_EVENT_META
         && @event.meta_type == midi_file_t.MIDI_META_END_OF_TRACK)
        {
            --running_tracks;

            // When all tracks have finished, restart the song.
            // Don't restart the song immediately, but wait for 5ms
            // before triggering a restart.  Otherwise it is possible
            // to construct an empty MIDI file that causes the game
            // to lock up in an infinite loop. (5ms should be short
            // enough not to be noticeable by the listener).
            if (running_tracks <= 0 && song_looping)
                OPL_SetCallback(5000, callback_kind.RestartSong, 0);

            return;
        }

        // Reschedule the callback for the next event in the track.
        ScheduleTrack(track);
    }

    private void ScheduleTrack(int track)
    {
        // Get the number of microseconds until the next event.
        uint nticks = midi_file_t.MIDI_GetDeltaTime(tracks![track].iter);
        ulong us = ((ulong)nticks * us_per_beat) / ticks_per_beat;

        // Set a timer to be invoked when the next event is
        // ready to play.
        OPL_SetCallback(us, callback_kind.TrackTimerCallback, track);
    }

    // Initialize a channel.
    private void InitChannel(opl_channel_data_t channel)
    {
        // TODO: Work out sensible defaults?
        channel.instrument = main_instrs(0);
        channel.volume = current_music_volume;
        channel.volume_base = 100;
        if (channel.volume > channel.volume_base)
            channel.volume = channel.volume_base;
        channel.pan = 0x30;
        channel.bend = 0;
    }

    // Start a MIDI track playing:
    private void StartTrack(midi_file_t file, uint track_num)
    {
        tracks![track_num] = new opl_track_data_t { iter = file.MIDI_IterateTrack(track_num) };

        // Schedule the first event.
        ScheduleTrack((int)track_num);
    }

    /// <summary><c>I_OPL_PlaySong</c>: plays <paramref name="handle"/> (from <see cref="I_OPL_RegisterSong"/>) from its start, unpaused.</summary>
    public void I_OPL_PlaySong(midi_file_t? handle, bool looping)
    {
        if (!music_initialized || handle == null)
            return;

        midi_file_t file = handle;

        // Allocate track data.
        tracks = new opl_track_data_t[file.MIDI_NumTracks()];

        num_tracks = file.MIDI_NumTracks();
        running_tracks = num_tracks;
        song_looping = looping;

        ticks_per_beat = file.MIDI_GetFileTimeDivision();

        // Default is 120 bpm.
        // TODO: this is wrong
        us_per_beat = 500 * 1000;

        start_music_volume = current_music_volume;

        for (uint i = 0; i < num_tracks; ++i)
            StartTrack(file, i);

        for (int i = 0; i < MIDI_CHANNELS_PER_TRACK; ++i)
            InitChannel(channels[i]);

        // If the music was previously paused, it needs to be unpaused; playing
        // a new song implies that we turn off pause. This matches vanilla
        // behavior of the DMX library, and some of the higher-level code in
        // s_sound.c relies on this.
        OPL_SetPaused(0);
    }

    /// <summary><c>I_OPL_PauseSong</c>: holds the sequencer; the melodic voices keyed off (percussion rings out), as vanilla.</summary>
    public void I_OPL_PauseSong()
    {
        if (!music_initialized)
            return;

        // Pause OPL callbacks.
        OPL_SetPaused(1);

        // Turn off all main instrument voices (not percussion).
        // This is what Vanilla does.
        for (int i = 0; i < num_opl_voices; ++i)
        {
            if (voices[i].channel != null && voices[i].current_instr!.index < GENMIDI_NUM_INSTRS)
                VoiceKeyOff(voices[i]);
        }
    }

    /// <summary><c>I_OPL_ResumeSong</c>.</summary>
    public void I_OPL_ResumeSong()
    {
        if (!music_initialized)
            return;

        OPL_SetPaused(0);
    }

    /// <summary><c>I_OPL_StopSong</c>: the song's callbacks cleared and every voice released.</summary>
    public void I_OPL_StopSong()
    {
        if (!music_initialized)
            return;

        // Stop all playback.
        OPL_ClearCallbacks();

        // Free all voices.
        for (int i = 0; i < MIDI_CHANNELS_PER_TRACK; ++i)
            AllNotesOff(channels[i], 0);

        // Free all track data.
        tracks = null;
        num_tracks = 0;
    }

    /// <summary><c>I_OPL_UnRegisterSong</c>: nothing to free here.</summary>
    public void I_OPL_UnRegisterSong(midi_file_t? handle)
    {
    }

    // Determine whether memory block is a .mid file
    private static bool IsMid(ReadOnlySpan<byte> mem) =>
        mem.Length > 4 && mem[0] == 'M' && mem[1] == 'T' && mem[2] == 'h' && mem[3] == 'd';

    /// <summary>
    /// <c>I_OPL_RegisterSong</c>: a MIDI file (<c>MThd</c>, under 96 KiB)
    /// read as it is, anything else converted from MUS (<c>mus2mid</c>, which
    /// does not check the header), then read (<c>MIDI_LoadFile</c>). Null
    /// when the conversion or the reading fails (the C prints "Failed to load
    /// MID."), or (not vanilla, whose division by zero would crash the
    /// sequencer) when the file's time division is 0.
    /// </summary>
    public midi_file_t? I_OPL_RegisterSong(ReadOnlySpan<byte> data)
    {
        if (!music_initialized)
            return null;

        byte[]? mid;
        if (IsMid(data) && data.Length < MAXMIDLENGTH)
            mid = data.ToArray();
        else
            mid = mus2mid.mus2mid(data); // Assume a MUS file and try to convert

        midi_file_t? result = mid == null ? null : midi_file_t.MIDI_LoadFile(mid);
        if (result != null && result.MIDI_GetFileTimeDivision() == 0)
            result = null;
        return result;
    }

    /// <summary><c>I_OPL_MusicIsPlaying</c>.</summary>
    public bool I_OPL_MusicIsPlaying() => music_initialized && num_tracks > 0;

    // --- IMusicDevice (i_sound.c's music_module_t calls) -------------------

    object? IMusicDevice.I_RegisterSong(string lumpName, ReadOnlyMemory<byte> data) => I_OPL_RegisterSong(data.Span);

    void IMusicDevice.I_UnRegisterSong(object handle) => I_OPL_UnRegisterSong(handle as midi_file_t);

    void IMusicDevice.I_PlaySong(object handle, bool looping) => I_OPL_PlaySong(handle as midi_file_t, looping);

    void IMusicDevice.I_StopSong() => I_OPL_StopSong();

    void IMusicDevice.I_PauseSong() => I_OPL_PauseSong();

    void IMusicDevice.I_ResumeSong() => I_OPL_ResumeSong();

    void IMusicDevice.I_SetMusicVolume(int volume) => I_OPL_SetMusicVolume(volume);

    // --- opl_queue.c ---------------------------------------------------

    // Queue of waiting callbacks, stored in a binary min heap, so that we
    // can always get the first callback.
    private sealed class opl_callback_queue_t
    {
        private const int MAX_OPL_QUEUE = 64;

        private struct opl_queue_entry_t
        {
            public callback_kind callback;
            public int data;
            public ulong time;
        }

        private readonly opl_queue_entry_t[] entries = new opl_queue_entry_t[MAX_OPL_QUEUE];
        public uint num_entries;
        public int dropped;

        public bool OPL_Queue_IsEmpty() => num_entries == 0;

        public void OPL_Queue_Clear() => num_entries = 0;

        public void OPL_Queue_Push(callback_kind callback, int data, ulong time)
        {
            if (num_entries >= MAX_OPL_QUEUE)
            {
                // OPL_Queue_Push: Exceeded maximum callbacks
                dropped++;
                return;
            }

            // Add to last queue entry.
            int entry_id = (int)num_entries;
            ++num_entries;

            // Shift existing entries down in the heap.
            while (entry_id > 0)
            {
                int parent_id = (entry_id - 1) / 2;

                // Is the heap condition satisfied?
                if (time >= entries[parent_id].time)
                    break;

                // Move the existing entry down in the heap.
                entries[entry_id] = entries[parent_id];

                // Advance to the parent.
                entry_id = parent_id;
            }

            // Insert new callback data.
            entries[entry_id].callback = callback;
            entries[entry_id].data = data;
            entries[entry_id].time = time;
        }

        public bool OPL_Queue_Pop(out callback_kind callback, out int data)
        {
            // Empty?
            if (num_entries <= 0)
            {
                callback = default;
                data = 0;
                return false;
            }

            // Store the result:
            callback = entries[0].callback;
            data = entries[0].data;

            // Decrease the heap size, and keep pointer to the last entry in
            // the heap, which must now be percolated down from the top.
            --num_entries;
            opl_queue_entry_t entry = entries[num_entries];

            // Percolate down.
            int i = 0;
            for (;;)
            {
                int child1 = i * 2 + 1;
                int child2 = i * 2 + 2;
                int next_i;

                if (child1 < num_entries && entries[child1].time < entry.time)
                {
                    // Left child is less than entry.
                    // Use the minimum of left and right children.
                    if (child2 < num_entries && entries[child2].time < entries[child1].time)
                        next_i = child2;
                    else
                        next_i = child1;
                }
                else if (child2 < num_entries && entries[child2].time < entry.time)
                {
                    // Right child is less than entry.  Go down the right side.
                    next_i = child2;
                }
                else
                {
                    // Finished percolating.
                    break;
                }

                // Percolate the next value up and advance.
                entries[i] = entries[next_i];
                i = next_i;
            }

            // Store the old last-entry at its new position.
            entries[i] = entry;

            return true;
        }

        public ulong OPL_Queue_Peek() => num_entries > 0 ? entries[0].time : 0;

        public void OPL_Queue_AdjustCallbacks(ulong time, float factor)
        {
            for (int i = 0; i < num_entries; ++i)
            {
                long offset = (long)(entries[i].time - time);
                entries[i].time = time + FloatToUInt64(offset / factor);
            }
        }
    }

    /// <summary>
    /// C's <c>(uint64_t)</c> of a float as x86-64 GCC compiles it (cvttss2si,
    /// with 2^63 subtracted first at or above it): a negative value wraps,
    /// NaN or one out of range gives 2^63. .NET's own conversion saturates.
    /// </summary>
    internal static ulong FloatToUInt64(float f)
    {
        const float two63 = 9223372036854775808f;
        static long Cvtt(float x) =>
            float.IsNaN(x) || x >= two63 || x < -two63 ? long.MinValue : (long)x;
        if (f >= two63)
            return (ulong)Cvtt(f - two63) ^ 0x8000000000000000UL;
        return (ulong)Cvtt(f);
    }
}
