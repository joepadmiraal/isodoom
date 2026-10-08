using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Wad;

/// <summary>
/// T7.8b: the <c>GENMIDI</c> OPL bank (<see cref="Genmidi"/>) and MUS songs
/// (<see cref="MusSong"/>) read as i_oplmusic.c and mus2mid.c read them;
/// malformed lumps refused with a message, never an exception.
/// </summary>
public class MusicLumpTests
{
    private static WadArchive Synthetic() => new([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);

    private static byte[] Lump(WadArchive wad, string name) => wad.W_CacheLumpName(name).ToArray();

    // ---- GENMIDI ----

    [Fact]
    public void ReadsTheSyntheticBank()
    {
        byte[] lump = Lump(Synthetic(), "GENMIDI");
        var g = Genmidi.Read(lump);
        Assert.True(g.HasNames);
        Assert.Equal(175, g.Instruments.Count);
        Assert.Equal(128, g.main_instrs.Length);
        Assert.Equal(47, g.percussion_instrs.Length);
        Assert.Equal("Synthetic 0", g.Instruments[0].Name);
        Assert.Equal("Synthetic drum 35", g.Instruments[128].Name);
        Assert.Same(g.Instruments[128], g.Percussion(35));
        Assert.Same(g.Instruments[174], g.Percussion(81));
        Assert.Null(g.Percussion(34));
        Assert.Null(g.Percussion(82));
        Assert.All(g.percussion_instrs.ToArray(), i => Assert.True(i.IsFixed));
        Assert.True(g.Instruments[3].IsTwoVoice);
        Assert.False(g.Instruments[3].IsFixed);
        Assert.Equal(16, g.Instruments.Count(i => i.IsTwoVoice));
        CheckBank(g);

        // The fields sit where genmidi_instr_t and genmidi_voice_t put them.
        GenmidiInstrument three = g.Instruments[3];
        ReadOnlySpan<byte> raw = lump.AsSpan(Genmidi.InstrumentsOffset + 3 * GenmidiInstrument.Size, GenmidiInstrument.Size);
        Assert.Equal(BinaryPrimitives.ReadUInt16LittleEndian(raw), three.Flags);
        Assert.Equal(raw[2], three.FineTuning);
        Assert.Equal(raw[3], three.FixedNote);
        Assert.Equal(new GenmidiOp(raw[4], raw[5], raw[6], raw[7], raw[8], raw[9]), three.Voice0.Modulator);
        Assert.Equal(raw[10], three.Voice0.Feedback);
        Assert.Equal(new GenmidiOp(raw[11], raw[12], raw[13], raw[14], raw[15], raw[16]), three.Voice0.Carrier);
        Assert.Equal(raw[17], three.Voice0.Unused);
        Assert.Equal(BinaryPrimitives.ReadInt16LittleEndian(raw[18..]), three.Voice0.BaseNoteOffset);
        Assert.Equal(12, three.Voice1.BaseNoteOffset);
        Assert.Equal(three.Voice1, three.Voice(1));
        Assert.Equal(-12, g.Instruments[12].Voice0.BaseNoteOffset);
        Assert.Contains("12: Synthetic 12", g.Describe());
        Assert.Contains("81: Synthetic drum 81 [note", g.Describe());
        Assert.StartsWith("OPL instruments, 128 melodic + 47 percussion, 16 two-voice, 47 fixed-note", g.Summary);
    }

    [Fact]
    public void RefusesABankWithoutItsHeaderOrInstruments()
    {
        byte[] lump = Lump(Synthetic(), "GENMIDI");
        for (int length = 0; length < Genmidi.NamesOffset; length += length < 16 ? 1 : 97)
        {
            Assert.Null(Genmidi.TryRead(lump.AsSpan(0, length), out string? error));
            Assert.NotNull(error);
        }
        Assert.Null(Genmidi.TryRead(lump.AsSpan(0, Genmidi.NamesOffset - 1), out _));
        Assert.Throws<WadFormatException>(() => Genmidi.Read(lump.AsSpan(0, 100)));

        byte[] wrong = (byte[])lump.Clone();
        wrong[7] = (byte)'X'; // "#OPL_IIX"
        Assert.Null(Genmidi.TryRead(wrong, out string? headerError));
        Assert.Equal("no #OPL_II# header", headerError);
    }

    [Fact]
    public void ReadsABankWithoutAllItsNames()
    {
        byte[] lump = Lump(Synthetic(), "GENMIDI");
        var none = Genmidi.Read(lump.AsSpan(0, Genmidi.NamesOffset)); // i_oplmusic.c never reads the names
        Assert.False(none.HasNames);
        Assert.All(none.Instruments, i => Assert.Equal("", i.Name));
        Assert.EndsWith(", names missing", none.Summary);

        var some = Genmidi.Read(lump.AsSpan(0, Genmidi.NamesOffset + 2 * Genmidi.NameSize + 5));
        Assert.False(some.HasNames);
        Assert.Equal("Synthetic 1", some.Instruments[1].Name);
        Assert.Equal("Synth", some.Instruments[2].Name);
        Assert.Equal("", some.Instruments[3].Name);
        Assert.Equal(Genmidi.Read(lump).Instruments[100].Voice0, some.Instruments[100].Voice0);
    }

    [Fact]
    public void ReadsGarbageAfterTheHeaderWithoutThrowing()
    {
        var random = new Random(78);
        for (int n = 0; n < 50; n++)
        {
            byte[] lump = new byte[random.Next(Genmidi.FullSize + 100)];
            random.NextBytes(lump);
            "#OPL_II#"u8[..Math.Min(8, lump.Length)].CopyTo(lump);
            var g = Genmidi.TryRead(lump, out string? error);
            Assert.Equal(lump.Length < Genmidi.NamesOffset, g is null);
            Assert.Equal(g is null, error is not null);
        }
    }

    // ---- MUS ----

    [Fact]
    public void ReadsTheSyntheticSong()
    {
        byte[] lump = Lump(Synthetic(), "D_E1M1");
        var song = MusSong.Read(lump);
        Assert.Equal(SyntheticIwad.SongEvents, song.Events.Count);
        Assert.Equal(SyntheticIwad.SongTics, song.LengthTics);
        Assert.Equal(SyntheticIwad.SongTics / 140.0, song.Seconds);
        Assert.Null(song.Mus2MidError);
        Assert.Equal(3, song.PrimaryChannels);
        Assert.Equal(0, song.SecondaryChannels);
        Assert.Equal([3, 33, 80, 135, 138, 142], song.Instruments);
        Assert.Equal(MusSong.HeaderSize + 12, song.ScoreStart);
        Assert.Equal(lump.Length - song.ScoreStart, song.ScoreLength);
        Assert.Equal(lump.Length - 1, song.ScoreEndOffset);
        Assert.Equal((1 << 0) | (1 << 1) | (1 << 2) | (1 << 15), song.ChannelsUsed);
        Assert.Equal("0-2, 15", song.ChannelList());
        Assert.Equal(lump, song.Data.ToArray());
        Assert.Equal($"MUS, {SyntheticIwad.SongEvents} events, 875 tics, 6.3 s at 140 Hz, channels 0-2, 15", song.Summary);

        // The first event: change controller 0 (instrument) on channel 0 to 33, at the score start.
        MusEvent first = song.Events[0];
        Assert.Equal((MusEventType.ChangeController, 0, 0, 33, 0L, song.ScoreStart, false), (first.Type, first.Channel, first.Controller, first.Value, first.Time, first.Offset, first.Last));
        Assert.Equal(2, first.DataBytes);

        // A press with a volume, then one without (the channel's last volume), each 35 tics apart.
        MusEvent[] lead = [.. song.Events.Where(e => e.Type == MusEventType.PressKey && e.Channel == 1)];
        Assert.Equal((48 + 12, true, 110, 0L), (lead[0].Note, lead[0].HasVolume, lead[0].Volume, lead[0].Time));
        Assert.Equal((64, false, 0, 35L), (lead[1].Note, lead[1].HasVolume, lead[1].Volume, lead[1].Time));
        Assert.Equal(1, lead[1].DataBytes);
        Assert.Equal(2, lead[0].DataBytes);

        // Each group's last event carries its delay; the others none.
        Assert.All(song.Events, e => Assert.True(e.Last || e.Delay == 0));
        long time = 0;
        foreach (MusEvent e in song.Events)
        {
            Assert.Equal(time, e.Time);
            time += e.Delay;
        }
        Assert.Contains(song.Events, e => e.Delay == 200); // two delay bytes

        MusEvent[] bends = [.. song.Events.Where(e => e.Type == MusEventType.PitchWheel)];
        Assert.Equal([160, 192, 128], bends.Select(e => e.Bend));
        MusEvent system = song.Events.Single(e => e.Type == MusEventType.SystemEvent);
        Assert.Equal((1, 11, SyntheticIwad.SongTics - 35L), (system.Channel, system.Controller, system.Time));
        MusEvent end = song.Events[^1];
        Assert.Equal((MusEventType.ScoreEnd, 0, (long)SyntheticIwad.SongTics), (end.Type, end.DataBytes, end.Time));
        CheckSong("D_E1M1", song);
    }

    [Fact]
    public void RefusesEveryTruncatedSong()
    {
        byte[] lump = Lump(Synthetic(), "D_E1M1");
        for (int length = 0; length < lump.Length; length++)
        {
            Assert.Null(MusSong.TryRead(lump.AsSpan(0, length), out string? error));
            Assert.False(string.IsNullOrEmpty(error));
        }
        Assert.Throws<WadFormatException>(() => MusSong.Read(lump.AsSpan(0, lump.Length - 1), "D_E1M1"));
    }

    [Fact]
    public void RefusesBadHeaders()
    {
        byte[] lump = Lump(Synthetic(), "D_E1M1");
        Assert.Null(MusSong.TryRead("MThd\0\0\0\x06"u8, out string? midi));
        Assert.Equal("no MUS header", midi);

        byte[] start = (byte[])lump.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(start.AsSpan(6), (ushort)lump.Length);
        Assert.Null(MusSong.TryRead(start, out string? startError));
        Assert.Contains("score start", startError);

        byte[] instruments = (byte[])lump.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(instruments.AsSpan(12), 60000);
        Assert.Null(MusSong.TryRead(instruments, out string? instrumentsError));
        Assert.Contains("instruments", instrumentsError);
    }

    [Fact]
    public void RefusesTypeSevenAndFlagsWhatMus2MidRefuses()
    {
        // Header with no instruments, then a score.
        static byte[] Song(params byte[] score)
        {
            byte[] lump = new byte[MusSong.HeaderSize + score.Length];
            "MUS\x1a"u8.CopyTo(lump);
            BinaryPrimitives.WriteUInt16LittleEndian(lump.AsSpan(4), (ushort)score.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(lump.AsSpan(6), MusSong.HeaderSize);
            score.CopyTo(lump, MusSong.HeaderSize);
            return lump;
        }

        Assert.Null(MusSong.TryRead(Song("p`"u8.ToArray()), out string? seven));
        Assert.Equal($"event type 7 (undefined) at byte {MusSong.HeaderSize}", seven);

        var measure = MusSong.Read(Song(0xD0, 10, 0x60)); // a measure end (type 5) with a delay of 10
        Assert.Equal((MusEventType.MeasureEnd, 0, 10L), (measure.Events[0].Type, measure.Events[0].DataBytes, measure.LengthTics));
        Assert.Equal("a measure end (type 5) at byte 16", measure.Mus2MidError);
        Assert.EndsWith("mus2mid.c refuses it: a measure end (type 5) at byte 16", measure.Summary);

        Assert.Equal("system event 15 at byte 16", MusSong.Read(Song(0x30, 15, 0x60)).Mus2MidError);
        Assert.Null(MusSong.Read(Song(0x30, 14, 0x60)).Mus2MidError);
        Assert.Equal("controller 10 at byte 16", MusSong.Read(Song(0x40, 10, 5, 0x60)).Mus2MidError);
        Assert.Null(MusSong.Read(Song(0x40, 9, 200, 0x60)).Mus2MidError); // mus2mid.c clamps the value

        // The score end stops the reading whatever follows; flagged, no delay is read.
        var flagged = MusSong.Read(Song(0x10, 60, 0xE0));
        Assert.Equal(2, flagged.Events.Count);
        Assert.Equal(0, flagged.LengthTics);
        Assert.Equal(1, flagged.ChannelsUsed);

        // A delay of four bytes (as mus2mid.c: 7 bits a byte, big-endian).
        var longDelay = MusSong.Read(Song(0x80, 60, 0x81, 0x80, 0x80, 0x00, 0x60));
        Assert.Equal(1L << 21, longDelay.LengthTics);
        Assert.Null(MusSong.TryRead(Song(0x80, 60, 0x81, 0x80), out string? cutDelay));
        Assert.Contains("delay", cutDelay);
    }

    [Fact]
    public void ReadsGarbageScoresWithoutThrowing()
    {
        var random = new Random(140);
        int read = 0;
        for (int n = 0; n < 2000; n++)
        {
            byte[] lump = new byte[random.Next(1, 300)];
            random.NextBytes(lump);
            "MUS\x1a"u8[..Math.Min(4, lump.Length)].CopyTo(lump);
            if (lump.Length >= 16 && n % 2 == 0)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(lump.AsSpan(6), 16); // a score start in the lump
                BinaryPrimitives.WriteUInt16LittleEndian(lump.AsSpan(12), 0);
            }
            var song = MusSong.TryRead(lump, out string? error);
            Assert.Equal(song is null, error is not null);
            if (song is not null)
            {
                read++;
                Assert.Equal(MusEventType.ScoreEnd, song.Events[^1].Type);
                Assert.NotNull(song.Summary);
                Assert.NotNull(song.Describe());
            }
        }
        Assert.True(read > 0);
    }

    [Fact]
    public void LumpListShowsTheBankAndSongs()
    {
        WadArchive wad = Synthetic();
        IReadOnlyList<LumpEntry> lumps = LumpDirectory.Build(wad);
        LumpEntry genmidi = lumps.Single(e => e.Lump.Name == "GENMIDI");
        Assert.Equal((LumpKind.Instruments, Genmidi.Read(genmidi.Lump.Data.Span).Summary), (genmidi.Kind, genmidi.Detail));
        LumpEntry song = lumps.Single(e => e.Lump.Name == "D_E1M1");
        Assert.Equal((LumpKind.Music, MusSong.Read(song.Lump.Data.Span).Summary), (song.Kind, song.Detail));
        Assert.Equal("MIDI", lumps.Single(e => e.Lump.Name == "D_INTRO").Detail);
    }

    // ---- The IWADs ----

    // DOOM1.WAD v1.9's songs: events (the score end included) and length in tics.
    private static readonly (string Name, int Events, long Tics)[] Doom1Songs =
    [
        ("D_E1M1", 5826, 13440), ("D_E1M2", 10847, 21751), ("D_E1M3", 7507, 38080), ("D_E1M4", 6270, 23893),
        ("D_E1M5", 3270, 22960), ("D_E1M6", 3332, 11760), ("D_E1M7", 2835, 21120), ("D_E1M8", 18113, 21280),
        ("D_E1M9", 7766, 19231), ("D_INTER", 9884, 28191), ("D_INTRO", 498, 960), ("D_VICTOR", 4532, 26880),
        ("D_INTROA", 214, 960),
    ];

    [Fact]
    public void ReadsEverySharewareSongAndTheBank()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom1());
        Assert.Equal(Doom1Songs.Select(s => s.Name), wad.Lumps.Where(l => l.Name.StartsWith("D_", StringComparison.Ordinal)).Select(l => l.Name));
        foreach ((string name, int events, long tics) in Doom1Songs)
        {
            var song = MusSong.Read(wad.W_CacheLumpName(name).Span, name);
            Assert.Equal((name, events, tics), (name, song.Events.Count, song.LengthTics));
            CheckSong(name, song);
        }
        Assert.Equal("0-2, 15", MusSong.Read(wad.W_CacheLumpName("D_E1M1").Span).ChannelList());

        var g = Genmidi.Read(wad.W_CacheLumpName("GENMIDI").Span);
        Assert.True(g.HasNames);
        Assert.Equal("Acoustic Grand Piano", g.Instruments[0].Name);
        Assert.Equal((33, 46), (g.Instruments.Count(i => i.IsTwoVoice), g.Instruments.Count(i => i.IsFixed)));
        CheckBank(g);
    }

    [Fact]
    public void ReadsEveryDoom2SongAndTheBank()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom2());
        WadLump[] songs = [.. wad.Lumps.Where(l => l.Name.StartsWith("D_", StringComparison.Ordinal))];
        Assert.True(songs.Length >= 35, $"{songs.Length} songs");
        foreach (WadLump l in songs)
        {
            var song = MusSong.TryRead(l.Data.Span, out string? error);
            Assert.True(song is not null, $"{l.Name}: {error}");
            CheckSong(l.Name, song!);
        }
        CheckBank(Genmidi.Read(wad.W_CacheLumpName("GENMIDI").Span));
    }

    // Sane totals for a song of the IWADs or ours: mus2mid.c converts it, the score as long as the header says,
    // the instrument list right after the header, notes, volumes and controllers in range, every channel declared.
    private static void CheckSong(string name, MusSong song)
    {
        Assert.True(song.Mus2MidError is null, $"{name}: {song.Mus2MidError}");
        Assert.Equal(MusSong.HeaderSize + 2 * song.Instruments.Count, song.ScoreStart);
        Assert.Equal(song.ScoreStart + song.ScoreLength - 1, song.ScoreEndOffset);
        Assert.True(song.LengthTics is > 0 and < 60 * 60 * MusSong.TicRate, $"{name}: {song.LengthTics} tics");
        Assert.Equal(1, song.Events.Count(e => e.Type == MusEventType.ScoreEnd));
        var pressed = new HashSet<(int, int)>();
        foreach (MusEvent e in song.Events)
        {
            Assert.True(e.Channel < song.PrimaryChannels || e.Channel == MusSong.PercussionChannel || e.Type == MusEventType.ScoreEnd,
                $"{name}: channel {e.Channel} of {song.PrimaryChannels} at byte {e.Offset}");
            switch (e.Type)
            {
                case MusEventType.PressKey:
                    Assert.InRange(e.Volume, 0, 127);
                    pressed.Add((e.Channel, e.Note));
                    break;
                case MusEventType.ReleaseKey:
                    Assert.InRange((int)e.Data1, 0, 127);
                    break;
                case MusEventType.ChangeController when e.Controller == 0:
                    Assert.InRange(e.Value, 0, 127); // an instrument of the bank
                    break;
                case MusEventType.ChangeController:
                    Assert.InRange(e.Value, 0, 127);
                    break;
            }
        }
        Assert.NotEmpty(pressed);
    }

    // Sane instruments: fixed notes and the notes the offsets make within MIDI's range, OPL register values in theirs.
    private static void CheckBank(Genmidi g)
    {
        foreach (GenmidiInstrument i in g.Instruments)
        {
            Assert.Equal(0, i.Flags & ~0x7); // fixed, 2 (unknown, in Doom's bank), two-voice
            if (i.IsFixed)
                Assert.InRange((int)i.FixedNote, 0, 127);
            int voices = i.IsTwoVoice ? 2 : 1;
            for (int v = 0; v < voices; v++)
            {
                GenmidiVoice voice = i.Voice(v);
                Assert.InRange((int)voice.BaseNoteOffset, -48, 48);
                Assert.InRange((int)voice.Feedback, 0, 15);
                foreach (GenmidiOp op in new[] { voice.Modulator, voice.Carrier })
                {
                    Assert.InRange((int)op.Level, 0, 63);
                    Assert.InRange((int)op.Waveform, 0, 7);
                    Assert.Equal(0, op.Scale & 0x3F);
                }
            }
        }
    }
}
