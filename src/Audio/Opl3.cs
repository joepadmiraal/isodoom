/* Nuked OPL3
 * Copyright (C) 2013-2020 Nuke.YKT
 *
 * This file is part of Nuked OPL3.
 *
 * Nuked OPL3 is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as
 * published by the Free Software Foundation, either version 2.1
 * of the License, or (at your option) any later version.
 *
 * Nuked OPL3 is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with Nuked OPL3. If not, see <https://www.gnu.org/licenses/>.

 *  Nuked OPL3 emulator.
 *  Thanks:
 *      MAME Development Team(Jarek Burczynski, Tatsuyuki Satoh):
 *          Feedback and Rhythm part calculation information.
 *      forums.submarine.org.uk(carbon14, opl3):
 *          Tremolo and phase generator calculation information.
 *      OPLx decapsulated(Matthew Gambrell, Olli Niemitalo):
 *          OPL2 ROMs.
 *      siliconpr0n.org(John McMaster, digshadow):
 *          YMF262 and VRC VII decaps and die shots.
 *
 * version: 1.8
 */

/* IsoDoom: a C# port of Nuked-OPL3's opl3.c/opl3.h at upstream commit
 * cfedb09 (2024-07-01; https://github.com/nukeykt/Nuked-OPL3), the chip
 * Chocolate Doom's opl/opl3.c reproduces bit for bit (Nuked-OPL3-fast
 * 1.8-fast.3; SPEC §12 T7.8a). Licensed as the original, LGPL-2.1-or-later
 * (THIRD-PARTY-NOTICES.md). Built with the defaults: OPL_ENABLE_STEREOEXT 0,
 * OPL_QUIRK_CHANNELSAMPLEDELAY 1. Integer-only like the C, so its samples
 * equal the C's (tools/OplRef, Opl3Tests). The C's pointers into the chip
 * (a slot's mod, a channel's out[]) are indices into opl3_chip.sig, which
 * holds every slot's out and fbmod and the zeromod; a slot's trem pointer is
 * a flag (the chip's tremolo or the zeromod). */

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IsoDoom.Audio;

/// <summary>opl3.h's <c>opl3_slot</c>: one operator.</summary>
public sealed class opl3_slot
{
    public opl3_channel channel = null!;
    public opl3_chip chip = null!;

    /// <summary>Index of the C's <c>int16_t out</c> in <see cref="opl3_chip.sig"/>.</summary>
    public int o_out;

    /// <summary>Index of the C's <c>int16_t fbmod</c> in <see cref="opl3_chip.sig"/>.</summary>
    public int o_fbmod;

    /// <summary>The C's <c>int16_t *mod</c>: an index into <see cref="opl3_chip.sig"/>.</summary>
    public int mod;

    public short prout;
    public ushort eg_rout;
    public ushort eg_out;
    public byte eg_inc;
    public byte eg_gen;
    public byte eg_rate;
    public byte eg_ksl;

    /// <summary>The C's <c>uint8_t *trem</c>: true points at the chip's tremolo, false at its zeromod.</summary>
    public bool trem;

    public byte reg_vib;
    public byte reg_type;
    public byte reg_ksr;
    public byte reg_mult;
    public byte reg_ksl;
    public byte reg_tl;
    public byte reg_ar;
    public byte reg_dr;
    public byte reg_sl;
    public byte reg_rr;
    public byte reg_wf;
    public byte key;
    public uint pg_reset;
    public uint pg_phase;
    public ushort pg_phase_out;
    public byte slot_num;
}

/// <summary>opl3.h's <c>opl3_channel</c>: two slots (four with its pair in 4-op mode).</summary>
public sealed class opl3_channel
{
    public readonly opl3_slot[] slotz = new opl3_slot[2];
    public opl3_channel? pair;
    public opl3_chip chip = null!;

    /// <summary>The C's <c>int16_t *out[4]</c>: indices into <see cref="opl3_chip.sig"/>.</summary>
    public opl3_out4 @out;

    public byte chtype;
    public ushort f_num;
    public byte block;
    public byte fb;
    public byte con;
    public byte alg;
    public byte ksv;
    public ushort cha, chb;
    public ushort chc, chd;
    public byte ch_num;
}

/// <summary>A channel's four output indices, stored in the channel (the C's array).</summary>
[InlineArray(4)]
public struct opl3_out4
{
    private int _e0;
}

/// <summary>opl3.h's <c>opl3_writebuf</c>: a register write queued by <see cref="Opl3.OPL3_WriteRegBuffered"/>.</summary>
public struct opl3_writebuf
{
    public ulong time;
    public ushort reg;
    public byte data;
}

/// <summary>
/// opl3.h's <c>opl3_chip</c>: a YMF262 (OPL3; OPL2 compatible until register
/// 0x105's NEW bit is set). <see cref="Opl3.OPL3_Reset"/> it before use.
/// </summary>
public sealed class opl3_chip
{
    /// <summary>Index of the C's <c>int16_t zeromod</c> in <see cref="sig"/>.</summary>
    public const int zeromod = 0;

    public readonly opl3_channel[] channel = new opl3_channel[18];
    public readonly opl3_slot[] slot = new opl3_slot[36];

    /// <summary>
    /// Not in the C: the 16-bit signals the C reaches by pointer: the zeromod
    /// (index 0, always 0), then each slot's out (1 + 2·n) and fbmod (2 + 2·n).
    /// </summary>
    public readonly short[] sig = new short[1 + 2 * 36];

    public ushort timer;
    public ulong eg_timer;
    public byte eg_timerrem;
    public byte eg_state;
    public byte eg_add;
    public byte eg_timer_lo;
    public byte newm;
    public byte nts;
    public byte rhy;
    public byte vibpos;
    public byte vibshift;
    public byte tremolo;
    public byte tremolopos;
    public byte tremoloshift;
    public uint noise;
    public readonly int[] mixbuff = new int[4];
    public byte rm_hh_bit2;
    public byte rm_hh_bit3;
    public byte rm_hh_bit7;
    public byte rm_hh_bit8;
    public byte rm_tc_bit3;
    public byte rm_tc_bit5;

    /* OPL3L */
    public int rateratio;
    public int samplecnt;
    public readonly short[] oldsamples = new short[4];
    public readonly short[] samples = new short[4];

    public ulong writebuf_samplecnt;
    public uint writebuf_cur;
    public uint writebuf_last;
    public ulong writebuf_lasttime;
    public readonly opl3_writebuf[] writebuf = new opl3_writebuf[Opl3.OPL_WRITEBUF_SIZE];

    public opl3_chip()
    {
        Opl3.OPL3_Reset(this, Opl3.OPL_RATE);
    }
}

/// <summary>opl3.c's functions (Nuked-OPL3 cfedb09), names kept.</summary>
public static class Opl3
{
    /// <summary>The chip's own sample rate: 14.318180 MHz / 288.</summary>
    public const uint OPL_RATE = 49716;

    public const int OPL_WRITEBUF_SIZE = 1024;
    public const int OPL_WRITEBUF_DELAY = 2;

    private const int RSM_FRAC = 10;

    /* Channel types */
    private const byte ch_2op = 0;
    private const byte ch_4op = 1;
    private const byte ch_4op2 = 2;
    private const byte ch_drum = 3;

    /* Envelope key types */
    private const byte egk_norm = 0x01;
    private const byte egk_drum = 0x02;

    /*
        logsin table
    */
    private static ReadOnlySpan<ushort> logsinrom => /* a span over static data: no bounds checks on & 0xff */
    [
        0x859, 0x6c3, 0x607, 0x58b, 0x52e, 0x4e4, 0x4a6, 0x471,
        0x443, 0x41a, 0x3f5, 0x3d3, 0x3b5, 0x398, 0x37e, 0x365,
        0x34e, 0x339, 0x324, 0x311, 0x2ff, 0x2ed, 0x2dc, 0x2cd,
        0x2bd, 0x2af, 0x2a0, 0x293, 0x286, 0x279, 0x26d, 0x261,
        0x256, 0x24b, 0x240, 0x236, 0x22c, 0x222, 0x218, 0x20f,
        0x206, 0x1fd, 0x1f5, 0x1ec, 0x1e4, 0x1dc, 0x1d4, 0x1cd,
        0x1c5, 0x1be, 0x1b7, 0x1b0, 0x1a9, 0x1a2, 0x19b, 0x195,
        0x18f, 0x188, 0x182, 0x17c, 0x177, 0x171, 0x16b, 0x166,
        0x160, 0x15b, 0x155, 0x150, 0x14b, 0x146, 0x141, 0x13c,
        0x137, 0x133, 0x12e, 0x129, 0x125, 0x121, 0x11c, 0x118,
        0x114, 0x10f, 0x10b, 0x107, 0x103, 0x0ff, 0x0fb, 0x0f8,
        0x0f4, 0x0f0, 0x0ec, 0x0e9, 0x0e5, 0x0e2, 0x0de, 0x0db,
        0x0d7, 0x0d4, 0x0d1, 0x0cd, 0x0ca, 0x0c7, 0x0c4, 0x0c1,
        0x0be, 0x0bb, 0x0b8, 0x0b5, 0x0b2, 0x0af, 0x0ac, 0x0a9,
        0x0a7, 0x0a4, 0x0a1, 0x09f, 0x09c, 0x099, 0x097, 0x094,
        0x092, 0x08f, 0x08d, 0x08a, 0x088, 0x086, 0x083, 0x081,
        0x07f, 0x07d, 0x07a, 0x078, 0x076, 0x074, 0x072, 0x070,
        0x06e, 0x06c, 0x06a, 0x068, 0x066, 0x064, 0x062, 0x060,
        0x05e, 0x05c, 0x05b, 0x059, 0x057, 0x055, 0x053, 0x052,
        0x050, 0x04e, 0x04d, 0x04b, 0x04a, 0x048, 0x046, 0x045,
        0x043, 0x042, 0x040, 0x03f, 0x03e, 0x03c, 0x03b, 0x039,
        0x038, 0x037, 0x035, 0x034, 0x033, 0x031, 0x030, 0x02f,
        0x02e, 0x02d, 0x02b, 0x02a, 0x029, 0x028, 0x027, 0x026,
        0x025, 0x024, 0x023, 0x022, 0x021, 0x020, 0x01f, 0x01e,
        0x01d, 0x01c, 0x01b, 0x01a, 0x019, 0x018, 0x017, 0x017,
        0x016, 0x015, 0x014, 0x014, 0x013, 0x012, 0x011, 0x011,
        0x010, 0x00f, 0x00f, 0x00e, 0x00d, 0x00d, 0x00c, 0x00c,
        0x00b, 0x00a, 0x00a, 0x009, 0x009, 0x008, 0x008, 0x007,
        0x007, 0x007, 0x006, 0x006, 0x005, 0x005, 0x005, 0x004,
        0x004, 0x004, 0x003, 0x003, 0x003, 0x002, 0x002, 0x002,
        0x002, 0x001, 0x001, 0x001, 0x001, 0x001, 0x001, 0x001,
        0x000, 0x000, 0x000, 0x000, 0x000, 0x000, 0x000, 0x000,
    ];

    /*
        exp table
    */
    private static ReadOnlySpan<ushort> exprom =>
    [
        0x7fa, 0x7f5, 0x7ef, 0x7ea, 0x7e4, 0x7df, 0x7da, 0x7d4,
        0x7cf, 0x7c9, 0x7c4, 0x7bf, 0x7b9, 0x7b4, 0x7ae, 0x7a9,
        0x7a4, 0x79f, 0x799, 0x794, 0x78f, 0x78a, 0x784, 0x77f,
        0x77a, 0x775, 0x770, 0x76a, 0x765, 0x760, 0x75b, 0x756,
        0x751, 0x74c, 0x747, 0x742, 0x73d, 0x738, 0x733, 0x72e,
        0x729, 0x724, 0x71f, 0x71a, 0x715, 0x710, 0x70b, 0x706,
        0x702, 0x6fd, 0x6f8, 0x6f3, 0x6ee, 0x6e9, 0x6e5, 0x6e0,
        0x6db, 0x6d6, 0x6d2, 0x6cd, 0x6c8, 0x6c4, 0x6bf, 0x6ba,
        0x6b5, 0x6b1, 0x6ac, 0x6a8, 0x6a3, 0x69e, 0x69a, 0x695,
        0x691, 0x68c, 0x688, 0x683, 0x67f, 0x67a, 0x676, 0x671,
        0x66d, 0x668, 0x664, 0x65f, 0x65b, 0x657, 0x652, 0x64e,
        0x649, 0x645, 0x641, 0x63c, 0x638, 0x634, 0x630, 0x62b,
        0x627, 0x623, 0x61e, 0x61a, 0x616, 0x612, 0x60e, 0x609,
        0x605, 0x601, 0x5fd, 0x5f9, 0x5f5, 0x5f0, 0x5ec, 0x5e8,
        0x5e4, 0x5e0, 0x5dc, 0x5d8, 0x5d4, 0x5d0, 0x5cc, 0x5c8,
        0x5c4, 0x5c0, 0x5bc, 0x5b8, 0x5b4, 0x5b0, 0x5ac, 0x5a8,
        0x5a4, 0x5a0, 0x59c, 0x599, 0x595, 0x591, 0x58d, 0x589,
        0x585, 0x581, 0x57e, 0x57a, 0x576, 0x572, 0x56f, 0x56b,
        0x567, 0x563, 0x560, 0x55c, 0x558, 0x554, 0x551, 0x54d,
        0x549, 0x546, 0x542, 0x53e, 0x53b, 0x537, 0x534, 0x530,
        0x52c, 0x529, 0x525, 0x522, 0x51e, 0x51b, 0x517, 0x514,
        0x510, 0x50c, 0x509, 0x506, 0x502, 0x4ff, 0x4fb, 0x4f8,
        0x4f4, 0x4f1, 0x4ed, 0x4ea, 0x4e7, 0x4e3, 0x4e0, 0x4dc,
        0x4d9, 0x4d6, 0x4d2, 0x4cf, 0x4cc, 0x4c8, 0x4c5, 0x4c2,
        0x4be, 0x4bb, 0x4b8, 0x4b5, 0x4b1, 0x4ae, 0x4ab, 0x4a8,
        0x4a4, 0x4a1, 0x49e, 0x49b, 0x498, 0x494, 0x491, 0x48e,
        0x48b, 0x488, 0x485, 0x482, 0x47e, 0x47b, 0x478, 0x475,
        0x472, 0x46f, 0x46c, 0x469, 0x466, 0x463, 0x460, 0x45d,
        0x45a, 0x457, 0x454, 0x451, 0x44e, 0x44b, 0x448, 0x445,
        0x442, 0x43f, 0x43c, 0x439, 0x436, 0x433, 0x430, 0x42d,
        0x42a, 0x428, 0x425, 0x422, 0x41f, 0x41c, 0x419, 0x416,
        0x414, 0x411, 0x40e, 0x40b, 0x408, 0x406, 0x403, 0x400,
    ];

    /*
        freq mult table multiplied by 2

        1/2, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 10, 12, 12, 15, 15
    */
    private static ReadOnlySpan<byte> mt =>
    [
        1, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 20, 24, 24, 30, 30,
    ];

    /*
        ksl table
    */
    private static ReadOnlySpan<byte> kslrom =>
    [
        0, 32, 40, 45, 48, 51, 53, 55, 56, 58, 59, 60, 61, 62, 63, 64,
    ];

    private static ReadOnlySpan<byte> kslshift => [8, 1, 2, 0];

    /*
        envelope generator constants
    */
    private static ReadOnlySpan<byte> eg_incstep => /* [4][4], flattened */
    [
        0, 0, 0, 0,
        1, 0, 0, 0,
        1, 0, 1, 0,
        1, 1, 1, 0,
    ];

    /*
        address decoding
    */
    private static readonly sbyte[] ad_slot =
    {
        0, 1, 2, 3, 4, 5, -1, -1, 6, 7, 8, 9, 10, 11, -1, -1,
        12, 13, 14, 15, 16, 17, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
    };

    private static readonly byte[] ch_slot =
    {
        0, 1, 2, 6, 7, 8, 12, 13, 14, 18, 19, 20, 24, 25, 26, 30, 31, 32,
    };

    /*
        Envelope generator
    */

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short OPL3_EnvelopeCalcExp(uint level)
    {
        if (level > 0x1fff)
        {
            level = 0x1fff;
        }
        return (short)((exprom[(int)(level & 0xff)] << 1) >> (int)(level >> 8));
    }

    private static short OPL3_EnvelopeCalcSin0(ushort phase, ushort envelope)
    {
        ushort @out;
        ushort neg = 0;
        phase &= 0x3ff;
        if ((phase & 0x200) != 0)
        {
            neg = 0xffff;
        }
        if ((phase & 0x100) != 0)
        {
            @out = logsinrom[(phase & 0xff) ^ 0xff];
        }
        else
        {
            @out = logsinrom[phase & 0xff];
        }
        return (short)(OPL3_EnvelopeCalcExp((uint)(@out + (envelope << 3))) ^ neg);
    }

    private static short OPL3_EnvelopeCalcSin1(ushort phase, ushort envelope)
    {
        ushort @out;
        phase &= 0x3ff;
        if ((phase & 0x200) != 0)
        {
            @out = 0x1000;
        }
        else if ((phase & 0x100) != 0)
        {
            @out = logsinrom[(phase & 0xff) ^ 0xff];
        }
        else
        {
            @out = logsinrom[phase & 0xff];
        }
        return OPL3_EnvelopeCalcExp((uint)(@out + (envelope << 3)));
    }

    private static short OPL3_EnvelopeCalcSin2(ushort phase, ushort envelope)
    {
        ushort @out;
        phase &= 0x3ff;
        if ((phase & 0x100) != 0)
        {
            @out = logsinrom[(phase & 0xff) ^ 0xff];
        }
        else
        {
            @out = logsinrom[phase & 0xff];
        }
        return OPL3_EnvelopeCalcExp((uint)(@out + (envelope << 3)));
    }

    private static short OPL3_EnvelopeCalcSin3(ushort phase, ushort envelope)
    {
        ushort @out;
        phase &= 0x3ff;
        if ((phase & 0x100) != 0)
        {
            @out = 0x1000;
        }
        else
        {
            @out = logsinrom[phase & 0xff];
        }
        return OPL3_EnvelopeCalcExp((uint)(@out + (envelope << 3)));
    }

    private static short OPL3_EnvelopeCalcSin4(ushort phase, ushort envelope)
    {
        ushort @out;
        ushort neg = 0;
        phase &= 0x3ff;
        if ((phase & 0x300) == 0x100)
        {
            neg = 0xffff;
        }
        if ((phase & 0x200) != 0)
        {
            @out = 0x1000;
        }
        else if ((phase & 0x80) != 0)
        {
            @out = logsinrom[((phase ^ 0xff) << 1) & 0xff];
        }
        else
        {
            @out = logsinrom[(phase << 1) & 0xff];
        }
        return (short)(OPL3_EnvelopeCalcExp((uint)(@out + (envelope << 3))) ^ neg);
    }

    private static short OPL3_EnvelopeCalcSin5(ushort phase, ushort envelope)
    {
        ushort @out;
        phase &= 0x3ff;
        if ((phase & 0x200) != 0)
        {
            @out = 0x1000;
        }
        else if ((phase & 0x80) != 0)
        {
            @out = logsinrom[((phase ^ 0xff) << 1) & 0xff];
        }
        else
        {
            @out = logsinrom[(phase << 1) & 0xff];
        }
        return OPL3_EnvelopeCalcExp((uint)(@out + (envelope << 3)));
    }

    private static short OPL3_EnvelopeCalcSin6(ushort phase, ushort envelope)
    {
        ushort neg = 0;
        phase &= 0x3ff;
        if ((phase & 0x200) != 0)
        {
            neg = 0xffff;
        }
        return (short)(OPL3_EnvelopeCalcExp((uint)(envelope << 3)) ^ neg);
    }

    private static short OPL3_EnvelopeCalcSin7(ushort phase, ushort envelope)
    {
        ushort @out;
        ushort neg = 0;
        phase &= 0x3ff;
        if ((phase & 0x200) != 0)
        {
            neg = 0xffff;
            phase = (ushort)((phase & 0x1ff) ^ 0x1ff);
        }
        @out = (ushort)(phase << 3);
        return (short)(OPL3_EnvelopeCalcExp((uint)(@out + (envelope << 3))) ^ neg);
    }

    /// <summary>opl3.c's <c>envelope_sin[8]</c> table of functions, as a switch.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short envelope_sin(byte wf, ushort phase, ushort envelope)
    {
        switch (wf)
        {
            case 0: return OPL3_EnvelopeCalcSin0(phase, envelope);
            case 1: return OPL3_EnvelopeCalcSin1(phase, envelope);
            case 2: return OPL3_EnvelopeCalcSin2(phase, envelope);
            case 3: return OPL3_EnvelopeCalcSin3(phase, envelope);
            case 4: return OPL3_EnvelopeCalcSin4(phase, envelope);
            case 5: return OPL3_EnvelopeCalcSin5(phase, envelope);
            case 6: return OPL3_EnvelopeCalcSin6(phase, envelope);
            default: return OPL3_EnvelopeCalcSin7(phase, envelope);
        }
    }

    private const byte envelope_gen_num_attack = 0;
    private const byte envelope_gen_num_decay = 1;
    private const byte envelope_gen_num_sustain = 2;
    private const byte envelope_gen_num_release = 3;

    private static void OPL3_EnvelopeUpdateKSL(opl3_slot slot)
    {
        short ksl = (short)((kslrom[(slot.channel.f_num >> 6) & 15] << 2)
                          - ((0x08 - slot.channel.block) << 5));
        if (ksl < 0)
        {
            ksl = 0;
        }
        slot.eg_ksl = (byte)ksl;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OPL3_EnvelopeCalc(opl3_slot slot)
    {
        opl3_chip chip = slot.chip;
        byte nonzero;
        byte rate;
        byte rate_hi;
        byte rate_lo;
        byte reg_rate = 0;
        byte ks;
        byte eg_shift, shift;
        ushort eg_rout;
        short eg_inc;
        byte eg_off;
        byte reset = 0;
        slot.eg_out = (ushort)(slot.eg_rout + (slot.reg_tl << 2)
                             + (slot.eg_ksl >> kslshift[slot.reg_ksl & 3]) + (slot.trem ? chip.tremolo : 0));
        if (slot.key == 0 && slot.eg_gen == envelope_gen_num_release && slot.eg_rout == 0x1ff)
        {
            /* Not in the C, same result: a slot keyed off and fully released
               stays so (below: no reset, eg_off, eg_rout 0x1ff, no increment,
               eg_gen release), so only pg_reset changes. */
            slot.pg_reset = 0;
            return;
        }
        if (slot.key != 0 && slot.eg_gen == envelope_gen_num_release)
        {
            reset = 1;
            reg_rate = slot.reg_ar;
        }
        else
        {
            switch (slot.eg_gen)
            {
                case envelope_gen_num_attack:
                    reg_rate = slot.reg_ar;
                    break;
                case envelope_gen_num_decay:
                    reg_rate = slot.reg_dr;
                    break;
                case envelope_gen_num_sustain:
                    if (slot.reg_type == 0)
                    {
                        reg_rate = slot.reg_rr;
                    }
                    break;
                case envelope_gen_num_release:
                    reg_rate = slot.reg_rr;
                    break;
            }
        }
        slot.pg_reset = reset;
        ks = (byte)(slot.channel.ksv >> ((slot.reg_ksr ^ 1) << 1));
        nonzero = (byte)(reg_rate != 0 ? 1 : 0);
        rate = (byte)(ks + (reg_rate << 2));
        rate_hi = (byte)(rate >> 2);
        rate_lo = (byte)(rate & 0x03);
        if ((rate_hi & 0x10) != 0)
        {
            rate_hi = 0x0f;
        }
        eg_shift = (byte)(rate_hi + chip.eg_add);
        shift = 0;
        if (nonzero != 0)
        {
            if (rate_hi < 12)
            {
                if (chip.eg_state != 0)
                {
                    switch (eg_shift)
                    {
                        case 12:
                            shift = 1;
                            break;
                        case 13:
                            shift = (byte)((rate_lo >> 1) & 0x01);
                            break;
                        case 14:
                            shift = (byte)(rate_lo & 0x01);
                            break;
                        default:
                            break;
                    }
                }
            }
            else
            {
                shift = (byte)((rate_hi & 0x03) + eg_incstep[(rate_lo * 4 + chip.eg_timer_lo) & 15]);
                if ((shift & 0x04) != 0)
                {
                    shift = 0x03;
                }
                if (shift == 0)
                {
                    shift = chip.eg_state;
                }
            }
        }
        eg_rout = slot.eg_rout;
        eg_inc = 0;
        eg_off = 0;
        /* Instant attack */
        if (reset != 0 && rate_hi == 0x0f)
        {
            eg_rout = 0x00;
        }
        /* Envelope off */
        if ((slot.eg_rout & 0x1f8) == 0x1f8)
        {
            eg_off = 1;
        }
        if (slot.eg_gen != envelope_gen_num_attack && reset == 0 && eg_off != 0)
        {
            eg_rout = 0x1ff;
        }
        switch (slot.eg_gen)
        {
            case envelope_gen_num_attack:
                if (slot.eg_rout == 0)
                {
                    slot.eg_gen = envelope_gen_num_decay;
                }
                else if (slot.key != 0 && shift > 0 && rate_hi != 0x0f)
                {
                    eg_inc = (short)(~slot.eg_rout >> (4 - shift));
                }
                break;
            case envelope_gen_num_decay:
                if ((slot.eg_rout >> 4) == slot.reg_sl)
                {
                    slot.eg_gen = envelope_gen_num_sustain;
                }
                else if (eg_off == 0 && reset == 0 && shift > 0)
                {
                    eg_inc = (short)(1 << (shift - 1));
                }
                break;
            case envelope_gen_num_sustain:
            case envelope_gen_num_release:
                if (eg_off == 0 && reset == 0 && shift > 0)
                {
                    eg_inc = (short)(1 << (shift - 1));
                }
                break;
        }
        slot.eg_rout = (ushort)((eg_rout + eg_inc) & 0x1ff);
        /* Key off */
        if (reset != 0)
        {
            slot.eg_gen = envelope_gen_num_attack;
        }
        if (slot.key == 0)
        {
            slot.eg_gen = envelope_gen_num_release;
        }
    }

    private static void OPL3_EnvelopeKeyOn(opl3_slot slot, byte type)
    {
        slot.key |= type;
    }

    private static void OPL3_EnvelopeKeyOff(opl3_slot slot, byte type)
    {
        slot.key &= (byte)~type;
    }

    /*
        Phase Generator
    */

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OPL3_PhaseGenerate(opl3_slot slot)
    {
        opl3_chip chip;
        ushort f_num;
        uint basefreq;
        byte rm_xor, n_bit;
        uint noise;
        ushort phase;

        chip = slot.chip;
        f_num = slot.channel.f_num;
        if (slot.reg_vib != 0)
        {
            sbyte range;
            byte vibpos;

            range = (sbyte)((f_num >> 7) & 7);
            vibpos = chip.vibpos;

            if ((vibpos & 3) == 0)
            {
                range = 0;
            }
            else if ((vibpos & 1) != 0)
            {
                range >>= 1;
            }
            range >>= chip.vibshift;

            if ((vibpos & 4) != 0)
            {
                range = (sbyte)-range;
            }
            f_num = (ushort)(f_num + range);
        }
        basefreq = (uint)(f_num << slot.channel.block) >> 1;
        phase = (ushort)(slot.pg_phase >> 9);
        if (slot.pg_reset != 0)
        {
            slot.pg_phase = 0;
        }
        slot.pg_phase += (basefreq * (uint)mt[slot.reg_mult & 15]) >> 1;
        /* Rhythm mode */
        noise = chip.noise;
        slot.pg_phase_out = phase;
        if (slot.slot_num == 13) /* hh */
        {
            chip.rm_hh_bit2 = (byte)((phase >> 2) & 1);
            chip.rm_hh_bit3 = (byte)((phase >> 3) & 1);
            chip.rm_hh_bit7 = (byte)((phase >> 7) & 1);
            chip.rm_hh_bit8 = (byte)((phase >> 8) & 1);
        }
        if (slot.slot_num == 17 && (chip.rhy & 0x20) != 0) /* tc */
        {
            chip.rm_tc_bit3 = (byte)((phase >> 3) & 1);
            chip.rm_tc_bit5 = (byte)((phase >> 5) & 1);
        }
        if ((chip.rhy & 0x20) != 0)
        {
            rm_xor = (byte)((chip.rm_hh_bit2 ^ chip.rm_hh_bit7)
                          | (chip.rm_hh_bit3 ^ chip.rm_tc_bit5)
                          | (chip.rm_tc_bit3 ^ chip.rm_tc_bit5));
            switch (slot.slot_num)
            {
                case 13: /* hh */
                    slot.pg_phase_out = (ushort)(rm_xor << 9);
                    if ((rm_xor ^ (noise & 1)) != 0)
                    {
                        slot.pg_phase_out |= 0xd0;
                    }
                    else
                    {
                        slot.pg_phase_out |= 0x34;
                    }
                    break;
                case 16: /* sd */
                    slot.pg_phase_out = (ushort)(((uint)chip.rm_hh_bit8 << 9)
                                               | ((chip.rm_hh_bit8 ^ (noise & 1)) << 8));
                    break;
                case 17: /* tc */
                    slot.pg_phase_out = (ushort)((rm_xor << 9) | 0x80);
                    break;
                default:
                    break;
            }
        }
        n_bit = (byte)(((noise >> 14) ^ noise) & 0x01);
        chip.noise = (noise >> 1) | ((uint)n_bit << 22);
    }

    /*
        Slot
    */

    private static void OPL3_SlotWrite20(opl3_slot slot, byte data)
    {
        slot.trem = ((data >> 7) & 0x01) != 0;
        slot.reg_vib = (byte)((data >> 6) & 0x01);
        slot.reg_type = (byte)((data >> 5) & 0x01);
        slot.reg_ksr = (byte)((data >> 4) & 0x01);
        slot.reg_mult = (byte)(data & 0x0f);
    }

    private static void OPL3_SlotWrite40(opl3_slot slot, byte data)
    {
        slot.reg_ksl = (byte)((data >> 6) & 0x03);
        slot.reg_tl = (byte)(data & 0x3f);
        OPL3_EnvelopeUpdateKSL(slot);
    }

    private static void OPL3_SlotWrite60(opl3_slot slot, byte data)
    {
        slot.reg_ar = (byte)((data >> 4) & 0x0f);
        slot.reg_dr = (byte)(data & 0x0f);
    }

    private static void OPL3_SlotWrite80(opl3_slot slot, byte data)
    {
        slot.reg_sl = (byte)((data >> 4) & 0x0f);
        if (slot.reg_sl == 0x0f)
        {
            slot.reg_sl = 0x1f;
        }
        slot.reg_rr = (byte)(data & 0x0f);
    }

    private static void OPL3_SlotWriteE0(opl3_slot slot, byte data)
    {
        slot.reg_wf = (byte)(data & 0x07);
        if (slot.chip.newm == 0x00)
        {
            slot.reg_wf &= 0x03;
        }
    }

    /// <summary>
    /// Not in the C: <see cref="envelope_sin"/>'s result when the exponential
    /// is 0: the <c>neg</c> of waveforms 0, 4, 6 and 7 (-1), else 0.
    /// </summary>
    private static short OPL3_SlotSign(byte wf, ushort phase)
    {
        phase &= 0x3ff;
        switch (wf)
        {
            case 0:
            case 6:
            case 7:
                return (short)((phase & 0x200) != 0 ? -1 : 0);
            case 4:
                return (short)((phase & 0x300) == 0x100 ? -1 : 0);
            default:
                return 0;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OPL3_SlotGenerate(opl3_slot slot, short[] sig)
    {
        ref short sig0 = ref MemoryMarshal.GetArrayDataReference(sig); /* the indices are the chip's own: in range */
        ushort phase = (ushort)(slot.pg_phase_out + Unsafe.Add(ref sig0, slot.mod));
        if (slot.eg_out >= 0x180)
        {
            /* Not in the C, same result: from this attenuation on the level
               passed to OPL3_EnvelopeCalcExp is at least 0xc00, whose result
               is 0, so the output is the waveform's sign alone. */
            Unsafe.Add(ref sig0, slot.o_out) = OPL3_SlotSign(slot.reg_wf, phase);
            return;
        }
        Unsafe.Add(ref sig0, slot.o_out) = envelope_sin(slot.reg_wf, phase, slot.eg_out);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OPL3_SlotCalcFB(opl3_slot slot, short[] sig)
    {
        ref short sig0 = ref MemoryMarshal.GetArrayDataReference(sig);
        short @out = Unsafe.Add(ref sig0, slot.o_out);
        if (slot.channel.fb != 0x00)
        {
            Unsafe.Add(ref sig0, slot.o_fbmod) = (short)((slot.prout + @out) >> (0x09 - slot.channel.fb));
        }
        else
        {
            Unsafe.Add(ref sig0, slot.o_fbmod) = 0;
        }
        slot.prout = @out;
    }

    /*
        Channel
    */

    private static void OPL3_ChannelUpdateRhythm(opl3_chip chip, byte data)
    {
        opl3_channel channel6;
        opl3_channel channel7;
        opl3_channel channel8;
        byte chnum;

        chip.rhy = (byte)(data & 0x3f);
        if ((chip.rhy & 0x20) != 0)
        {
            channel6 = chip.channel[6];
            channel7 = chip.channel[7];
            channel8 = chip.channel[8];
            channel6.@out[0] = channel6.slotz[1].o_out;
            channel6.@out[1] = channel6.slotz[1].o_out;
            channel6.@out[2] = opl3_chip.zeromod;
            channel6.@out[3] = opl3_chip.zeromod;
            channel7.@out[0] = channel7.slotz[0].o_out;
            channel7.@out[1] = channel7.slotz[0].o_out;
            channel7.@out[2] = channel7.slotz[1].o_out;
            channel7.@out[3] = channel7.slotz[1].o_out;
            channel8.@out[0] = channel8.slotz[0].o_out;
            channel8.@out[1] = channel8.slotz[0].o_out;
            channel8.@out[2] = channel8.slotz[1].o_out;
            channel8.@out[3] = channel8.slotz[1].o_out;
            for (chnum = 6; chnum < 9; chnum++)
            {
                chip.channel[chnum].chtype = ch_drum;
            }
            OPL3_ChannelSetupAlg(channel6);
            OPL3_ChannelSetupAlg(channel7);
            OPL3_ChannelSetupAlg(channel8);
            /* hh */
            if ((chip.rhy & 0x01) != 0)
            {
                OPL3_EnvelopeKeyOn(channel7.slotz[0], egk_drum);
            }
            else
            {
                OPL3_EnvelopeKeyOff(channel7.slotz[0], egk_drum);
            }
            /* tc */
            if ((chip.rhy & 0x02) != 0)
            {
                OPL3_EnvelopeKeyOn(channel8.slotz[1], egk_drum);
            }
            else
            {
                OPL3_EnvelopeKeyOff(channel8.slotz[1], egk_drum);
            }
            /* tom */
            if ((chip.rhy & 0x04) != 0)
            {
                OPL3_EnvelopeKeyOn(channel8.slotz[0], egk_drum);
            }
            else
            {
                OPL3_EnvelopeKeyOff(channel8.slotz[0], egk_drum);
            }
            /* sd */
            if ((chip.rhy & 0x08) != 0)
            {
                OPL3_EnvelopeKeyOn(channel7.slotz[1], egk_drum);
            }
            else
            {
                OPL3_EnvelopeKeyOff(channel7.slotz[1], egk_drum);
            }
            /* bd */
            if ((chip.rhy & 0x10) != 0)
            {
                OPL3_EnvelopeKeyOn(channel6.slotz[0], egk_drum);
                OPL3_EnvelopeKeyOn(channel6.slotz[1], egk_drum);
            }
            else
            {
                OPL3_EnvelopeKeyOff(channel6.slotz[0], egk_drum);
                OPL3_EnvelopeKeyOff(channel6.slotz[1], egk_drum);
            }
        }
        else
        {
            for (chnum = 6; chnum < 9; chnum++)
            {
                chip.channel[chnum].chtype = ch_2op;
                OPL3_ChannelSetupAlg(chip.channel[chnum]);
                OPL3_EnvelopeKeyOff(chip.channel[chnum].slotz[0], egk_drum);
                OPL3_EnvelopeKeyOff(chip.channel[chnum].slotz[1], egk_drum);
            }
        }
    }

    private static void OPL3_ChannelWriteA0(opl3_channel channel, byte data)
    {
        if (channel.chip.newm != 0 && channel.chtype == ch_4op2)
        {
            return;
        }
        channel.f_num = (ushort)((channel.f_num & 0x300) | data);
        channel.ksv = (byte)((channel.block << 1)
                           | ((channel.f_num >> (0x09 - channel.chip.nts)) & 0x01));
        OPL3_EnvelopeUpdateKSL(channel.slotz[0]);
        OPL3_EnvelopeUpdateKSL(channel.slotz[1]);
        if (channel.chip.newm != 0 && channel.chtype == ch_4op)
        {
            opl3_channel pair = channel.pair!;
            pair.f_num = channel.f_num;
            pair.ksv = channel.ksv;
            OPL3_EnvelopeUpdateKSL(pair.slotz[0]);
            OPL3_EnvelopeUpdateKSL(pair.slotz[1]);
        }
    }

    private static void OPL3_ChannelWriteB0(opl3_channel channel, byte data)
    {
        if (channel.chip.newm != 0 && channel.chtype == ch_4op2)
        {
            return;
        }
        channel.f_num = (ushort)((channel.f_num & 0xff) | ((data & 0x03) << 8));
        channel.block = (byte)((data >> 2) & 0x07);
        channel.ksv = (byte)((channel.block << 1)
                           | ((channel.f_num >> (0x09 - channel.chip.nts)) & 0x01));
        OPL3_EnvelopeUpdateKSL(channel.slotz[0]);
        OPL3_EnvelopeUpdateKSL(channel.slotz[1]);
        if (channel.chip.newm != 0 && channel.chtype == ch_4op)
        {
            opl3_channel pair = channel.pair!;
            pair.f_num = channel.f_num;
            pair.block = channel.block;
            pair.ksv = channel.ksv;
            OPL3_EnvelopeUpdateKSL(pair.slotz[0]);
            OPL3_EnvelopeUpdateKSL(pair.slotz[1]);
        }
    }

    private static void OPL3_ChannelSetupAlg(opl3_channel channel)
    {
        const int zeromod = opl3_chip.zeromod;
        if (channel.chtype == ch_drum)
        {
            if (channel.ch_num == 7 || channel.ch_num == 8)
            {
                channel.slotz[0].mod = zeromod;
                channel.slotz[1].mod = zeromod;
                return;
            }
            switch (channel.alg & 0x01)
            {
                case 0x00:
                    channel.slotz[0].mod = channel.slotz[0].o_fbmod;
                    channel.slotz[1].mod = channel.slotz[0].o_out;
                    break;
                case 0x01:
                    channel.slotz[0].mod = channel.slotz[0].o_fbmod;
                    channel.slotz[1].mod = zeromod;
                    break;
            }
            return;
        }
        if ((channel.alg & 0x08) != 0)
        {
            return;
        }
        if ((channel.alg & 0x04) != 0)
        {
            opl3_channel pair = channel.pair!;
            pair.@out[0] = zeromod;
            pair.@out[1] = zeromod;
            pair.@out[2] = zeromod;
            pair.@out[3] = zeromod;
            switch (channel.alg & 0x03)
            {
                case 0x00:
                    pair.slotz[0].mod = pair.slotz[0].o_fbmod;
                    pair.slotz[1].mod = pair.slotz[0].o_out;
                    channel.slotz[0].mod = pair.slotz[1].o_out;
                    channel.slotz[1].mod = channel.slotz[0].o_out;
                    channel.@out[0] = channel.slotz[1].o_out;
                    channel.@out[1] = zeromod;
                    channel.@out[2] = zeromod;
                    channel.@out[3] = zeromod;
                    break;
                case 0x01:
                    pair.slotz[0].mod = pair.slotz[0].o_fbmod;
                    pair.slotz[1].mod = pair.slotz[0].o_out;
                    channel.slotz[0].mod = zeromod;
                    channel.slotz[1].mod = channel.slotz[0].o_out;
                    channel.@out[0] = pair.slotz[1].o_out;
                    channel.@out[1] = channel.slotz[1].o_out;
                    channel.@out[2] = zeromod;
                    channel.@out[3] = zeromod;
                    break;
                case 0x02:
                    pair.slotz[0].mod = pair.slotz[0].o_fbmod;
                    pair.slotz[1].mod = zeromod;
                    channel.slotz[0].mod = pair.slotz[1].o_out;
                    channel.slotz[1].mod = channel.slotz[0].o_out;
                    channel.@out[0] = pair.slotz[0].o_out;
                    channel.@out[1] = channel.slotz[1].o_out;
                    channel.@out[2] = zeromod;
                    channel.@out[3] = zeromod;
                    break;
                case 0x03:
                    pair.slotz[0].mod = pair.slotz[0].o_fbmod;
                    pair.slotz[1].mod = zeromod;
                    channel.slotz[0].mod = pair.slotz[1].o_out;
                    channel.slotz[1].mod = zeromod;
                    channel.@out[0] = pair.slotz[0].o_out;
                    channel.@out[1] = channel.slotz[0].o_out;
                    channel.@out[2] = channel.slotz[1].o_out;
                    channel.@out[3] = zeromod;
                    break;
            }
        }
        else
        {
            switch (channel.alg & 0x01)
            {
                case 0x00:
                    channel.slotz[0].mod = channel.slotz[0].o_fbmod;
                    channel.slotz[1].mod = channel.slotz[0].o_out;
                    channel.@out[0] = channel.slotz[1].o_out;
                    channel.@out[1] = zeromod;
                    channel.@out[2] = zeromod;
                    channel.@out[3] = zeromod;
                    break;
                case 0x01:
                    channel.slotz[0].mod = channel.slotz[0].o_fbmod;
                    channel.slotz[1].mod = zeromod;
                    channel.@out[0] = channel.slotz[0].o_out;
                    channel.@out[1] = channel.slotz[1].o_out;
                    channel.@out[2] = zeromod;
                    channel.@out[3] = zeromod;
                    break;
            }
        }
    }

    private static void OPL3_ChannelUpdateAlg(opl3_channel channel)
    {
        channel.alg = channel.con;
        if (channel.chip.newm != 0)
        {
            if (channel.chtype == ch_4op)
            {
                opl3_channel pair = channel.pair!;
                pair.alg = (byte)(0x04 | (channel.con << 1) | pair.con);
                channel.alg = 0x08;
                OPL3_ChannelSetupAlg(pair);
            }
            else if (channel.chtype == ch_4op2)
            {
                opl3_channel pair = channel.pair!;
                channel.alg = (byte)(0x04 | (pair.con << 1) | channel.con);
                pair.alg = 0x08;
                OPL3_ChannelSetupAlg(channel);
            }
            else
            {
                OPL3_ChannelSetupAlg(channel);
            }
        }
        else
        {
            OPL3_ChannelSetupAlg(channel);
        }
    }

    private static void OPL3_ChannelWriteC0(opl3_channel channel, byte data)
    {
        channel.fb = (byte)((data & 0x0e) >> 1);
        channel.con = (byte)(data & 0x01);
        OPL3_ChannelUpdateAlg(channel);
        if (channel.chip.newm != 0)
        {
            channel.cha = (ushort)(((data >> 4) & 0x01) != 0 ? 0xffff : 0);
            channel.chb = (ushort)(((data >> 5) & 0x01) != 0 ? 0xffff : 0);
            channel.chc = (ushort)(((data >> 6) & 0x01) != 0 ? 0xffff : 0);
            channel.chd = (ushort)(((data >> 7) & 0x01) != 0 ? 0xffff : 0);
        }
        else
        {
            channel.cha = channel.chb = 0xffff;
            // TODO: Verify on real chip if DAC2 output is disabled in compat mode
            channel.chc = channel.chd = 0;
        }
    }

    private static void OPL3_ChannelKeyOn(opl3_channel channel)
    {
        if (channel.chip.newm != 0)
        {
            if (channel.chtype == ch_4op)
            {
                opl3_channel pair = channel.pair!;
                OPL3_EnvelopeKeyOn(channel.slotz[0], egk_norm);
                OPL3_EnvelopeKeyOn(channel.slotz[1], egk_norm);
                OPL3_EnvelopeKeyOn(pair.slotz[0], egk_norm);
                OPL3_EnvelopeKeyOn(pair.slotz[1], egk_norm);
            }
            else if (channel.chtype == ch_2op || channel.chtype == ch_drum)
            {
                OPL3_EnvelopeKeyOn(channel.slotz[0], egk_norm);
                OPL3_EnvelopeKeyOn(channel.slotz[1], egk_norm);
            }
        }
        else
        {
            OPL3_EnvelopeKeyOn(channel.slotz[0], egk_norm);
            OPL3_EnvelopeKeyOn(channel.slotz[1], egk_norm);
        }
    }

    private static void OPL3_ChannelKeyOff(opl3_channel channel)
    {
        if (channel.chip.newm != 0)
        {
            if (channel.chtype == ch_4op)
            {
                opl3_channel pair = channel.pair!;
                OPL3_EnvelopeKeyOff(channel.slotz[0], egk_norm);
                OPL3_EnvelopeKeyOff(channel.slotz[1], egk_norm);
                OPL3_EnvelopeKeyOff(pair.slotz[0], egk_norm);
                OPL3_EnvelopeKeyOff(pair.slotz[1], egk_norm);
            }
            else if (channel.chtype == ch_2op || channel.chtype == ch_drum)
            {
                OPL3_EnvelopeKeyOff(channel.slotz[0], egk_norm);
                OPL3_EnvelopeKeyOff(channel.slotz[1], egk_norm);
            }
        }
        else
        {
            OPL3_EnvelopeKeyOff(channel.slotz[0], egk_norm);
            OPL3_EnvelopeKeyOff(channel.slotz[1], egk_norm);
        }
    }

    private static void OPL3_ChannelSet4Op(opl3_chip chip, byte data)
    {
        byte bit;
        int chnum;
        for (bit = 0; bit < 6; bit++)
        {
            chnum = bit;
            if (bit >= 3)
            {
                chnum += 9 - 3;
            }
            if (((data >> bit) & 0x01) != 0)
            {
                chip.channel[chnum].chtype = ch_4op;
                chip.channel[chnum + 3].chtype = ch_4op2;
                OPL3_ChannelUpdateAlg(chip.channel[chnum]);
            }
            else
            {
                chip.channel[chnum].chtype = ch_2op;
                chip.channel[chnum + 3].chtype = ch_2op;
                OPL3_ChannelUpdateAlg(chip.channel[chnum]);
                OPL3_ChannelUpdateAlg(chip.channel[chnum + 3]);
            }
        }
    }

    private static short OPL3_ClipSample(int sample)
    {
        if (sample > 32767)
        {
            sample = 32767;
        }
        else if (sample < -32768)
        {
            sample = -32768;
        }
        return (short)sample;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OPL3_ProcessSlot(opl3_slot slot, short[] sig)
    {
        OPL3_SlotCalcFB(slot, sig);
        OPL3_EnvelopeCalc(slot);
        OPL3_PhaseGenerate(slot);
        OPL3_SlotGenerate(slot, sig);
    }

    /// <summary>OPL3_Generate4Ch's first mix loop: every channel's outputs summed for outputs A and C.</summary>
    private static void OPL3_MixLeft(opl3_chip chip, short[] sig)
    {
        opl3_channel[] channels = chip.channel;
        ref short sig0 = ref MemoryMarshal.GetArrayDataReference(sig);
        int mix0 = 0, mix1 = 0;
        for (int ii = 0; ii < 18; ii++)
        {
            opl3_channel channel = channels[ii];
            ref opl3_out4 @out = ref channel.@out;
            short accm = (short)(Unsafe.Add(ref sig0, @out[0]) + Unsafe.Add(ref sig0, @out[1])
                               + Unsafe.Add(ref sig0, @out[2]) + Unsafe.Add(ref sig0, @out[3]));
            mix0 += (short)(accm & channel.cha);
            mix1 += (short)(accm & channel.chc);
        }
        chip.mixbuff[0] = mix0;
        chip.mixbuff[2] = mix1;
    }

    /// <summary>OPL3_Generate4Ch's second mix loop: outputs B and D.</summary>
    private static void OPL3_MixRight(opl3_chip chip, short[] sig)
    {
        opl3_channel[] channels = chip.channel;
        ref short sig0 = ref MemoryMarshal.GetArrayDataReference(sig);
        int mix0 = 0, mix1 = 0;
        for (int ii = 0; ii < 18; ii++)
        {
            opl3_channel channel = channels[ii];
            ref opl3_out4 @out = ref channel.@out;
            short accm = (short)(Unsafe.Add(ref sig0, @out[0]) + Unsafe.Add(ref sig0, @out[1])
                               + Unsafe.Add(ref sig0, @out[2]) + Unsafe.Add(ref sig0, @out[3]));
            mix0 += (short)(accm & channel.chb);
            mix1 += (short)(accm & channel.chd);
        }
        chip.mixbuff[1] = mix0;
        chip.mixbuff[3] = mix1;
    }

    /// <summary>One sample at the chip's rate (49716 Hz) on all four outputs (A, B, C, D: A left, B right).</summary>
    public static void OPL3_Generate4Ch(opl3_chip chip, Span<short> buf4)
    {
        opl3_slot[] slots = chip.slot;
        short[] sig = chip.sig;
        int ii;
        byte shift = 0;

        buf4[1] = OPL3_ClipSample(chip.mixbuff[1]);
        buf4[3] = OPL3_ClipSample(chip.mixbuff[3]);

        /* OPL_QUIRK_CHANNELSAMPLEDELAY */
        for (ii = 0; ii < 15; ii++)
        {
            OPL3_ProcessSlot(slots[ii], sig);
        }

        OPL3_MixLeft(chip, sig);

        for (ii = 15; ii < 18; ii++)
        {
            OPL3_ProcessSlot(slots[ii], sig);
        }

        buf4[0] = OPL3_ClipSample(chip.mixbuff[0]);
        buf4[2] = OPL3_ClipSample(chip.mixbuff[2]);

        for (ii = 18; ii < 33; ii++)
        {
            OPL3_ProcessSlot(slots[ii], sig);
        }

        OPL3_MixRight(chip, sig);

        for (ii = 33; ii < 36; ii++)
        {
            OPL3_ProcessSlot(slots[ii], sig);
        }

        if ((chip.timer & 0x3f) == 0x3f)
        {
            chip.tremolopos = (byte)((chip.tremolopos + 1) % 210);
        }
        if (chip.tremolopos < 105)
        {
            chip.tremolo = (byte)(chip.tremolopos >> chip.tremoloshift);
        }
        else
        {
            chip.tremolo = (byte)((210 - chip.tremolopos) >> chip.tremoloshift);
        }

        if ((chip.timer & 0x3ff) == 0x3ff)
        {
            chip.vibpos = (byte)((chip.vibpos + 1) & 7);
        }

        chip.timer++;

        if (chip.eg_state != 0)
        {
            while (shift < 13 && ((chip.eg_timer >> shift) & 1) == 0)
            {
                shift++;
            }
            if (shift > 12)
            {
                chip.eg_add = 0;
            }
            else
            {
                chip.eg_add = (byte)(shift + 1);
            }
            chip.eg_timer_lo = (byte)(chip.eg_timer & 0x3u);
        }

        if (chip.eg_timerrem != 0 || chip.eg_state != 0)
        {
            if (chip.eg_timer == 0xfffffffffUL)
            {
                chip.eg_timer = 0;
                chip.eg_timerrem = 1;
            }
            else
            {
                chip.eg_timer++;
                chip.eg_timerrem = 0;
            }
        }

        chip.eg_state ^= 1;

        while (true)
        {
            ref opl3_writebuf writebuf = ref chip.writebuf[chip.writebuf_cur];
            if (writebuf.time > chip.writebuf_samplecnt)
            {
                break;
            }
            if ((writebuf.reg & 0x200) == 0)
            {
                break;
            }
            writebuf.reg &= 0x1ff;
            OPL3_WriteReg(chip, writebuf.reg, writebuf.data);
            chip.writebuf_cur = (chip.writebuf_cur + 1) % OPL_WRITEBUF_SIZE;
        }
        chip.writebuf_samplecnt++;
    }

    /// <summary>One stereo sample (left, right) at the chip's rate (49716 Hz).</summary>
    public static void OPL3_Generate(opl3_chip chip, Span<short> buf)
    {
        Span<short> samples = stackalloc short[4];
        OPL3_Generate4Ch(chip, samples);
        buf[0] = samples[0];
        buf[1] = samples[1];
    }

    /// <summary>One sample on all four outputs at the rate given to <see cref="OPL3_Reset"/> (linear interpolation).</summary>
    public static void OPL3_Generate4ChResampled(opl3_chip chip, Span<short> buf4)
    {
        while (chip.samplecnt >= chip.rateratio)
        {
            chip.oldsamples[0] = chip.samples[0];
            chip.oldsamples[1] = chip.samples[1];
            chip.oldsamples[2] = chip.samples[2];
            chip.oldsamples[3] = chip.samples[3];
            OPL3_Generate4Ch(chip, chip.samples);
            chip.samplecnt -= chip.rateratio;
        }
        buf4[0] = (short)((chip.oldsamples[0] * (chip.rateratio - chip.samplecnt)
                         + chip.samples[0] * chip.samplecnt) / chip.rateratio);
        buf4[1] = (short)((chip.oldsamples[1] * (chip.rateratio - chip.samplecnt)
                         + chip.samples[1] * chip.samplecnt) / chip.rateratio);
        buf4[2] = (short)((chip.oldsamples[2] * (chip.rateratio - chip.samplecnt)
                         + chip.samples[2] * chip.samplecnt) / chip.rateratio);
        buf4[3] = (short)((chip.oldsamples[3] * (chip.rateratio - chip.samplecnt)
                         + chip.samples[3] * chip.samplecnt) / chip.rateratio);
        chip.samplecnt += 1 << RSM_FRAC;
    }

    /// <summary>One stereo sample (left, right) at the rate given to <see cref="OPL3_Reset"/>.</summary>
    public static void OPL3_GenerateResampled(opl3_chip chip, Span<short> buf)
    {
        Span<short> samples = stackalloc short[4];
        OPL3_Generate4ChResampled(chip, samples);
        buf[0] = samples[0];
        buf[1] = samples[1];
    }

    /// <summary>
    /// Resets the chip (every register 0, OPL2 mode) for output at
    /// <paramref name="samplerate"/> Hz through the <c>Resampled</c>/<c>Stream</c>
    /// functions (the plain <c>Generate</c> ones run at 49716 Hz whatever it is).
    /// </summary>
    public static void OPL3_Reset(opl3_chip chip, uint samplerate)
    {
        opl3_slot slot;
        opl3_channel channel;
        byte slotnum;
        byte channum;
        byte local_ch_slot;

        /* memset(chip, 0, sizeof(opl3_chip)) */
        chip.timer = 0;
        chip.eg_timer = 0;
        chip.eg_timerrem = 0;
        chip.eg_state = 0;
        chip.eg_add = 0;
        chip.eg_timer_lo = 0;
        chip.newm = 0;
        chip.nts = 0;
        chip.rhy = 0;
        chip.vibpos = 0;
        chip.vibshift = 0;
        chip.tremolo = 0;
        chip.tremolopos = 0;
        chip.tremoloshift = 0;
        chip.noise = 0;
        Array.Clear(chip.sig);
        Array.Clear(chip.mixbuff);
        chip.rm_hh_bit2 = chip.rm_hh_bit3 = chip.rm_hh_bit7 = chip.rm_hh_bit8 = 0;
        chip.rm_tc_bit3 = chip.rm_tc_bit5 = 0;
        chip.rateratio = 0;
        chip.samplecnt = 0;
        Array.Clear(chip.oldsamples);
        Array.Clear(chip.samples);
        chip.writebuf_samplecnt = 0;
        chip.writebuf_cur = 0;
        chip.writebuf_last = 0;
        chip.writebuf_lasttime = 0;
        Array.Clear(chip.writebuf);

        for (slotnum = 0; slotnum < 36; slotnum++)
        {
            slot = chip.slot[slotnum] = new opl3_slot();
            slot.chip = chip;
            slot.o_out = 1 + 2 * slotnum;
            slot.o_fbmod = 2 + 2 * slotnum;
            slot.mod = opl3_chip.zeromod;
            slot.eg_rout = 0x1ff;
            slot.eg_out = 0x1ff;
            slot.eg_gen = envelope_gen_num_release;
            slot.trem = false;
            slot.slot_num = slotnum;
        }
        for (channum = 0; channum < 18; channum++)
        {
            channel = chip.channel[channum] = new opl3_channel();
        }
        for (channum = 0; channum < 18; channum++)
        {
            channel = chip.channel[channum];
            local_ch_slot = ch_slot[channum];
            channel.slotz[0] = chip.slot[local_ch_slot];
            channel.slotz[1] = chip.slot[local_ch_slot + 3];
            chip.slot[local_ch_slot].channel = channel;
            chip.slot[local_ch_slot + 3].channel = channel;
            if ((channum % 9) < 3)
            {
                channel.pair = chip.channel[channum + 3];
            }
            else if ((channum % 9) < 6)
            {
                channel.pair = chip.channel[channum - 3];
            }
            channel.chip = chip;
            channel.@out[0] = opl3_chip.zeromod;
            channel.@out[1] = opl3_chip.zeromod;
            channel.@out[2] = opl3_chip.zeromod;
            channel.@out[3] = opl3_chip.zeromod;
            channel.chtype = ch_2op;
            channel.cha = 0xffff;
            channel.chb = 0xffff;
            channel.ch_num = channum;
            OPL3_ChannelSetupAlg(channel);
        }
        chip.noise = 1;
        chip.rateratio = (int)((samplerate << RSM_FRAC) / 49716);
        chip.tremoloshift = 4;
        chip.vibshift = 1;
    }

    /// <summary>Writes <paramref name="v"/> to register <paramref name="reg"/> at once (0x000–0x0ff: bank 0; 0x100–0x1ff: bank 1).</summary>
    public static void OPL3_WriteReg(opl3_chip chip, ushort reg, byte v)
    {
        int high = (reg >> 8) & 0x01;
        int regm = reg & 0xff;
        switch (regm & 0xf0)
        {
            case 0x00:
                if (high != 0)
                {
                    switch (regm & 0x0f)
                    {
                        case 0x04:
                            OPL3_ChannelSet4Op(chip, v);
                            break;
                        case 0x05:
                            chip.newm = (byte)(v & 0x01);
                            break;
                    }
                }
                else
                {
                    switch (regm & 0x0f)
                    {
                        case 0x08:
                            chip.nts = (byte)((v >> 6) & 0x01);
                            break;
                    }
                }
                break;
            case 0x20:
            case 0x30:
                if (ad_slot[regm & 0x1f] >= 0)
                {
                    OPL3_SlotWrite20(chip.slot[18 * high + ad_slot[regm & 0x1f]], v);
                }
                break;
            case 0x40:
            case 0x50:
                if (ad_slot[regm & 0x1f] >= 0)
                {
                    OPL3_SlotWrite40(chip.slot[18 * high + ad_slot[regm & 0x1f]], v);
                }
                break;
            case 0x60:
            case 0x70:
                if (ad_slot[regm & 0x1f] >= 0)
                {
                    OPL3_SlotWrite60(chip.slot[18 * high + ad_slot[regm & 0x1f]], v);
                }
                break;
            case 0x80:
            case 0x90:
                if (ad_slot[regm & 0x1f] >= 0)
                {
                    OPL3_SlotWrite80(chip.slot[18 * high + ad_slot[regm & 0x1f]], v);
                }
                break;
            case 0xe0:
            case 0xf0:
                if (ad_slot[regm & 0x1f] >= 0)
                {
                    OPL3_SlotWriteE0(chip.slot[18 * high + ad_slot[regm & 0x1f]], v);
                }
                break;
            case 0xa0:
                if ((regm & 0x0f) < 9)
                {
                    OPL3_ChannelWriteA0(chip.channel[9 * high + (regm & 0x0f)], v);
                }
                break;
            case 0xb0:
                if (regm == 0xbd && high == 0)
                {
                    chip.tremoloshift = (byte)((((v >> 7) ^ 1) << 1) + 2);
                    chip.vibshift = (byte)(((v >> 6) & 0x01) ^ 1);
                    OPL3_ChannelUpdateRhythm(chip, v);
                }
                else if ((regm & 0x0f) < 9)
                {
                    OPL3_ChannelWriteB0(chip.channel[9 * high + (regm & 0x0f)], v);
                    if ((v & 0x20) != 0)
                    {
                        OPL3_ChannelKeyOn(chip.channel[9 * high + (regm & 0x0f)]);
                    }
                    else
                    {
                        OPL3_ChannelKeyOff(chip.channel[9 * high + (regm & 0x0f)]);
                    }
                }
                break;
            case 0xc0:
                if ((regm & 0x0f) < 9)
                {
                    OPL3_ChannelWriteC0(chip.channel[9 * high + (regm & 0x0f)], v);
                }
                break;
        }
    }

    /// <summary>
    /// Queues a register write as a real chip takes it: at least
    /// <see cref="OPL_WRITEBUF_DELAY"/> chip samples after the last one, applied
    /// by the generation (Chocolate Doom's opl_sdl.c writes through this).
    /// </summary>
    public static void OPL3_WriteRegBuffered(opl3_chip chip, ushort reg, byte v)
    {
        ulong time1, time2;
        uint writebuf_last;

        writebuf_last = chip.writebuf_last;
        ref opl3_writebuf writebuf = ref chip.writebuf[writebuf_last];

        if ((writebuf.reg & 0x200) != 0)
        {
            OPL3_WriteReg(chip, (ushort)(writebuf.reg & 0x1ff), writebuf.data);

            chip.writebuf_cur = (writebuf_last + 1) % OPL_WRITEBUF_SIZE;
            chip.writebuf_samplecnt = writebuf.time;
        }

        writebuf.reg = (ushort)(reg | 0x200);
        writebuf.data = v;
        time1 = chip.writebuf_lasttime + OPL_WRITEBUF_DELAY;
        time2 = chip.writebuf_samplecnt;

        if (time1 < time2)
        {
            time1 = time2;
        }

        writebuf.time = time1;
        chip.writebuf_lasttime = time1;
        chip.writebuf_last = (writebuf_last + 1) % OPL_WRITEBUF_SIZE;
    }

    /// <summary><paramref name="numsamples"/> samples on all four outputs at the reset's rate: A/B interleaved in <paramref name="sndptr1"/>, C/D in <paramref name="sndptr2"/>.</summary>
    public static void OPL3_Generate4ChStream(opl3_chip chip, Span<short> sndptr1, Span<short> sndptr2, uint numsamples)
    {
        Span<short> samples = stackalloc short[4];

        for (int i = 0; i < numsamples; i++)
        {
            OPL3_Generate4ChResampled(chip, samples);
            sndptr1[2 * i] = samples[0];
            sndptr1[2 * i + 1] = samples[1];
            sndptr2[2 * i] = samples[2];
            sndptr2[2 * i + 1] = samples[3];
        }
    }

    /// <summary><paramref name="numsamples"/> stereo samples (left, right interleaved) at the reset's rate into <paramref name="sndptr"/>.</summary>
    public static void OPL3_GenerateStream(opl3_chip chip, Span<short> sndptr, uint numsamples)
    {
        Span<short> samples = stackalloc short[4];

        for (int i = 0; i < numsamples; i++)
        {
            OPL3_Generate4ChResampled(chip, samples);
            sndptr[2 * i] = samples[0];
            sndptr[2 * i + 1] = samples[1];
        }
    }
}
