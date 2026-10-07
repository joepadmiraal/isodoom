using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IsoDoom.Wad;

/// <summary>
/// One OPL operator of a <c>GENMIDI</c> voice (i_oplmusic.c <c>genmidi_op_t</c>,
/// 6 bytes): the raw register values the driver writes. <see cref="Tremolo"/>
/// goes to register 0x20 (AM, vibrato, sustain, KSR, multiplier),
/// <see cref="Attack"/> to 0x60, <see cref="Sustain"/> to 0x80,
/// <see cref="Waveform"/> to 0xE0, and <see cref="Scale"/> (the key scale
/// level bits) with <see cref="Level"/> (the total level) to 0x40.
/// </summary>
public readonly record struct GenmidiOp(byte Tremolo, byte Attack, byte Sustain, byte Waveform, byte Scale, byte Level)
{
    /// <summary>The size of a <c>genmidi_op_t</c> in bytes.</summary>
    public const int Size = 6;

    internal static GenmidiOp Read(ReadOnlySpan<byte> b) => new(b[0], b[1], b[2], b[3], b[4], b[5]);
}

/// <summary>
/// One voice of a <c>GENMIDI</c> instrument (i_oplmusic.c <c>genmidi_voice_t</c>,
/// 16 bytes): the modulator, the feedback/connection byte (register 0xC0), the
/// carrier, an unused byte, and the note offset added to the played note.
/// </summary>
public readonly record struct GenmidiVoice(GenmidiOp Modulator, byte Feedback, GenmidiOp Carrier, byte Unused, short BaseNoteOffset)
{
    /// <summary>The size of a <c>genmidi_voice_t</c> in bytes.</summary>
    public const int Size = 16;

    internal static GenmidiVoice Read(ReadOnlySpan<byte> b) => new(
        GenmidiOp.Read(b),
        b[6],
        GenmidiOp.Read(b[7..]),
        b[13],
        BinaryPrimitives.ReadInt16LittleEndian(b[14..]));
}

/// <summary>
/// One <c>GENMIDI</c> instrument (i_oplmusic.c <c>genmidi_instr_t</c>, 36
/// bytes): its flags (<see cref="Genmidi.GENMIDI_FLAG_FIXED"/>,
/// <see cref="Genmidi.GENMIDI_FLAG_2VOICE"/>; the raw value, other bits kept),
/// the fine tuning of the second voice (128 is none), the note a fixed-pitch
/// instrument always plays, its two voices, and its name (from the lump's name
/// table, which i_oplmusic.c does not read).
/// </summary>
public sealed record GenmidiInstrument(ushort Flags, byte FineTuning, byte FixedNote, GenmidiVoice Voice0, GenmidiVoice Voice1, string Name)
{
    /// <summary>The size of a <c>genmidi_instr_t</c> in bytes.</summary>
    public const int Size = 4 + 2 * GenmidiVoice.Size;

    /// <summary>Plays <see cref="FixedNote"/> whatever note is asked (<c>GENMIDI_FLAG_FIXED</c>).</summary>
    public bool IsFixed => (Flags & Genmidi.GENMIDI_FLAG_FIXED) != 0;

    /// <summary>Plays both voices (<c>GENMIDI_FLAG_2VOICE</c>; i_oplmusic.c only in OPL3 mode or with the voice to spare).</summary>
    public bool IsTwoVoice => (Flags & Genmidi.GENMIDI_FLAG_2VOICE) != 0;

    /// <summary>The voice at <paramref name="index"/> (0 or 1), as i_oplmusic.c's <c>voices[index]</c>.</summary>
    public GenmidiVoice Voice(int index) => index switch
    {
        0 => Voice0,
        1 => Voice1,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    internal static GenmidiInstrument Read(ReadOnlySpan<byte> b, string name) => new(
        BinaryPrimitives.ReadUInt16LittleEndian(b),
        b[2],
        b[3],
        GenmidiVoice.Read(b[4..]),
        GenmidiVoice.Read(b[(4 + GenmidiVoice.Size)..]),
        name);
}

/// <summary>
/// The OPL instrument bank (<c>GENMIDI</c>, T7.8b), read as Chocolate Doom's
/// i_oplmusic.c <c>LoadInstrumentTable</c> does: the <c>#OPL_II#</c> header,
/// then 175 <c>genmidi_instr_t</c> (128 melodic instruments, the General MIDI
/// programs, then 47 percussion instruments for notes 35–81 of the percussion
/// channel), then 175 names of 32 bytes. Plain C#, no Godot types.
/// <para>
/// i_oplmusic.c checks only the header and reads the instruments without
/// checking the lump's size; here a lump too short for the 175 instruments is
/// refused (it would read past the lump). The names are not the driver's: a
/// lump that stops before or inside them is read with the names it has (the
/// rest empty) and <see cref="HasNames"/> false.
/// </para>
/// </summary>
public sealed class Genmidi
{
    // i_oplmusic.c
    public const string GENMIDI_HEADER = "#OPL_II#";
    public const int GENMIDI_NUM_INSTRS = 128;
    public const int GENMIDI_NUM_PERCUSSION = 47;
    public const ushort GENMIDI_FLAG_FIXED = 0x0001; // fixed pitch
    public const ushort GENMIDI_FLAG_2VOICE = 0x0004; // double voice (OPL3)

    /// <summary>The percussion channel's first note with an instrument (i_oplmusic.c: notes 35–81).</summary>
    public const int PercussionFirstNote = 35;

    /// <summary>The percussion channel's last note with an instrument.</summary>
    public const int PercussionLastNote = PercussionFirstNote + GENMIDI_NUM_PERCUSSION - 1;

    /// <summary>Every instrument: 128 melodic, then 47 percussion.</summary>
    public const int NumInstruments = GENMIDI_NUM_INSTRS + GENMIDI_NUM_PERCUSSION;

    /// <summary>The size of a name in the name table.</summary>
    public const int NameSize = 32;

    /// <summary>Where the instruments start (after the header).</summary>
    public const int InstrumentsOffset = 8;

    /// <summary>Where the names start.</summary>
    public const int NamesOffset = InstrumentsOffset + NumInstruments * GenmidiInstrument.Size;

    /// <summary>The size of a whole lump, names included (11908 bytes, as Doom's).</summary>
    public const int FullSize = NamesOffset + NumInstruments * NameSize;

    private readonly GenmidiInstrument[] _instruments;

    /// <summary>All 175 instruments, melodic then percussion.</summary>
    public IReadOnlyList<GenmidiInstrument> Instruments => _instruments;

    /// <summary>i_oplmusic.c <c>main_instrs</c>: the 128 melodic instruments, by MIDI program.</summary>
    public ReadOnlySpan<GenmidiInstrument> main_instrs => _instruments.AsSpan(0, GENMIDI_NUM_INSTRS);

    /// <summary>i_oplmusic.c <c>percussion_instrs</c>: the 47 percussion instruments, for notes 35–81.</summary>
    public ReadOnlySpan<GenmidiInstrument> percussion_instrs => _instruments.AsSpan(GENMIDI_NUM_INSTRS, GENMIDI_NUM_PERCUSSION);

    /// <summary>True when the lump holds the whole name table.</summary>
    public bool HasNames { get; }

    private Genmidi(GenmidiInstrument[] instruments, bool hasNames)
    {
        _instruments = instruments;
        HasNames = hasNames;
    }

    /// <summary>
    /// The percussion instrument of percussion channel note <paramref name="note"/>,
    /// or null outside 35–81 (i_oplmusic.c's <c>KeyOnEvent</c> plays nothing there).
    /// </summary>
    public GenmidiInstrument? Percussion(int note) =>
        note is >= PercussionFirstNote and <= PercussionLastNote ? _instruments[GENMIDI_NUM_INSTRS + note - PercussionFirstNote] : null;

    /// <summary>Whether <paramref name="lump"/> starts with <c>#OPL_II#</c>.</summary>
    public static bool HasHeader(ReadOnlySpan<byte> lump) =>
        lump.Length >= GENMIDI_HEADER.Length && lump[..GENMIDI_HEADER.Length].SequenceEqual("#OPL_II#"u8);

    /// <summary>Reads a <c>GENMIDI</c> lump, or returns null with the reason in <paramref name="error"/>.</summary>
    public static Genmidi? TryRead(ReadOnlySpan<byte> lump, out string? error)
    {
        error = null;
        if (!HasHeader(lump))
        {
            error = "no #OPL_II# header";
            return null;
        }
        if (lump.Length < NamesOffset)
        {
            error = $"{lump.Length} bytes, too short for {NumInstruments} instruments ({NamesOffset})";
            return null;
        }
        var instruments = new GenmidiInstrument[NumInstruments];
        for (int i = 0; i < NumInstruments; i++)
        {
            int nameAt = NamesOffset + i * NameSize;
            string name = nameAt < lump.Length ? ReadName(lump[nameAt..Math.Min(lump.Length, nameAt + NameSize)]) : "";
            instruments[i] = GenmidiInstrument.Read(lump.Slice(InstrumentsOffset + i * GenmidiInstrument.Size, GenmidiInstrument.Size), name);
        }
        return new Genmidi(instruments, lump.Length >= FullSize);
    }

    /// <summary><see cref="TryRead"/>, throwing <see cref="WadFormatException"/> when it fails.</summary>
    public static Genmidi Read(ReadOnlySpan<byte> lump, string name = "GENMIDI") =>
        TryRead(lump, out string? error) ?? throw new WadFormatException($"{name}: {error}");

    // A NUL-terminated name; bytes outside printable ASCII shown as '?'.
    private static string ReadName(ReadOnlySpan<byte> b)
    {
        int end = b.IndexOf((byte)0);
        if (end < 0)
            end = b.Length;
        var sb = new StringBuilder(end);
        foreach (byte c in b[..end])
            sb.Append(c is >= 0x20 and < 0x7F ? (char)c : '?');
        return sb.ToString().TrimEnd();
    }

    /// <summary>The lump list's detail: the instruments' count and kinds.</summary>
    public string Summary
    {
        get
        {
            int fixedNotes = 0, twoVoice = 0;
            foreach (GenmidiInstrument i in _instruments)
            {
                if (i.IsFixed)
                    fixedNotes++;
                if (i.IsTwoVoice)
                    twoVoice++;
            }
            return $"OPL instruments, {GENMIDI_NUM_INSTRS} melodic + {GENMIDI_NUM_PERCUSSION} percussion, {twoVoice} two-voice, {fixedNotes} fixed-note"
                + (HasNames ? "" : ", names missing");
        }
    }

    /// <summary>The viewer's text: every instrument's number (program, or percussion note) and name, with its flags.</summary>
    public string Describe()
    {
        var sb = new StringBuilder();
        sb.Append("Melodic (program: name): ");
        for (int i = 0; i < NumInstruments; i++)
        {
            if (i == GENMIDI_NUM_INSTRS)
                sb.Append("\nPercussion (note: name): ");
            else if (i > 0)
                sb.Append("; ");
            GenmidiInstrument instr = _instruments[i];
            int number = i < GENMIDI_NUM_INSTRS ? i : i - GENMIDI_NUM_INSTRS + PercussionFirstNote;
            sb.Append(number.ToString(CultureInfo.InvariantCulture)).Append(": ").Append(instr.Name.Length > 0 ? instr.Name : "(no name)");
            if (instr.IsTwoVoice)
                sb.Append(" [2 voices]");
            if (instr.IsFixed)
                sb.Append(" [note ").Append(instr.FixedNote.ToString(CultureInfo.InvariantCulture)).Append(']');
        }
        return sb.ToString();
    }
}
