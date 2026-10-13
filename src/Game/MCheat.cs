using System;
using System.Collections.Generic;
using IsoDoom.Sim;
using IsoDoom.Wad;

namespace IsoDoom.Game;

/// <summary>
/// m_cheat.c's cheat sequence package (T7.9a), as linuxdoom-1.10's: a
/// <c>cheatseq_t</c>'s <see cref="sequence"/> holds the cheat's letters
/// scrambled (<see cref="SCRAMBLE"/>), then a 1 and a 0 per parameter
/// character when it takes some, then 0xff; the parameters typed are kept in
/// the sequence itself until <see cref="cht_GetParam"/> takes them.
/// </summary>
public sealed class cheatseq_t
{
    public readonly byte[] sequence;

    /// <summary>Vanilla's <c>p</c>: the index of the next byte to match (vanilla's starts null, then the sequence's start).</summary>
    public int p;

    public cheatseq_t(params byte[] sequence) => this.sequence = sequence;

    /// <summary>m_cheat.c <c>SCRAMBLE</c>: the bits of <paramref name="a"/> shuffled (bits 0 and 7, 1 and 6, 3 and 4 swapped).</summary>
    public static byte SCRAMBLE(int a) =>
        (byte)(((a & 1) << 7) + ((a & 2) << 5) + (a & 4) + ((a & 8) << 1)
            + ((a & 16) >> 1) + (a & 32) + ((a & 64) >> 5) + ((a & 128) >> 7));

    /// <summary>
    /// m_cheat.c <c>cht_CheckCheat</c>: <paramref name="key"/> typed; true
    /// when it ends the sequence. A key that is not the next letter starts it
    /// again (not itself checked as its first letter: "iidkfa" is no cheat);
    /// a parameter's place takes any key.
    /// </summary>
    public bool cht_CheckCheat(byte key)
    {
        bool rc = false;

        if (sequence[p] == 0)
            sequence[p++] = key;
        else if (SCRAMBLE(key) == sequence[p])
            p++;
        else
            p = 0;

        if (sequence[p] == 1)
            p++;
        else if (sequence[p] == 0xff) // end of sequence character
        {
            p = 0;
            rc = true;
        }

        return rc;
    }

    /// <summary>m_cheat.c <c>cht_GetParam</c>: the parameter characters typed, cleared from the sequence.</summary>
    public byte[] cht_GetParam()
    {
        var buffer = new List<byte>();
        int i = 0;
        while (sequence[i++] != 1)
        {
        }

        byte c;
        do
        {
            c = sequence[i];
            buffer.Add(c);
            sequence[i++] = 0;
        }
        while (c != 0 && sequence[i] != 0xff);

        return [.. buffer];
    }

    /// <summary>The cheat's text (the sequence unscrambled, a <c>#</c> per parameter character): the tests'.</summary>
    public string Text()
    {
        var text = new System.Text.StringBuilder();
        foreach (byte b in sequence)
        {
            if (b == 0xff)
                break;
            if (b == 1)
                continue;
            if (b == 0)
                text.Append('#');
            else
                text.Append((char)Unscramble(b));
        }
        return text.ToString();
    }

    private static int Unscramble(byte b)
    {
        for (int a = 0; a < 256; a++)
        {
            if (SCRAMBLE(a) == b)
                return a;
        }
        return 0;
    }
}

/// <summary>
/// st_stuff.c <c>ST_Responder</c>'s reading of the cheats (T7.9a): every key
/// pressed on a level that the menus did not take (vanilla's <c>G_Responder</c>
/// passes them all, the game's keys too) goes through the cheat sequences in
/// vanilla's order, so each sequence's state is vanilla's; a cheat matched is
/// what <see cref="Check"/> returns, for the game flow to send in the next
/// <c>ticcmd</c> (SPEC §12 T7.9a). Its effects are the sim's
/// (<see cref="World.ST_Cheat"/>) and the flow's (<c>idclev</c>, <c>idmus</c>).
/// </summary>
public sealed class MCheat
{
    // st_stuff.c's sequences, scrambled.
    public readonly cheatseq_t cheat_mus = new(0xb2, 0x26, 0xb6, 0xae, 0xea, 1, 0, 0, 0xff);
    public readonly cheatseq_t cheat_choppers = new(0xb2, 0x26, 0xe2, 0x32, 0xf6, 0x2a, 0x2a, 0xa6, 0x6a, 0xea, 0xff); // id...
    public readonly cheatseq_t cheat_god = new(0xb2, 0x26, 0x26, 0xaa, 0x26, 0xff); // iddqd
    public readonly cheatseq_t cheat_ammo = new(0xb2, 0x26, 0xf2, 0x66, 0xa2, 0xff); // idkfa
    public readonly cheatseq_t cheat_ammonokey = new(0xb2, 0x26, 0x66, 0xa2, 0xff); // idfa

    // Smashing Pumpkins Into Samml Piles Of Putried Debris.
    public readonly cheatseq_t cheat_noclip = new(0xb2, 0x26, 0xea, 0x2a, 0xb2, // idspispopd
        0xea, 0x2a, 0xf6, 0x2a, 0x26, 0xff);

    public readonly cheatseq_t cheat_commercial_noclip = new(0xb2, 0x26, 0xe2, 0x36, 0xb2, 0x2a, 0xff); // idclip

    public readonly cheatseq_t[] cheat_powerup =
    [
        new(0xb2, 0x26, 0x62, 0xa6, 0x32, 0xf6, 0x36, 0x26, 0x6e, 0xff), // beholdv
        new(0xb2, 0x26, 0x62, 0xa6, 0x32, 0xf6, 0x36, 0x26, 0xea, 0xff), // beholds
        new(0xb2, 0x26, 0x62, 0xa6, 0x32, 0xf6, 0x36, 0x26, 0xb2, 0xff), // beholdi
        new(0xb2, 0x26, 0x62, 0xa6, 0x32, 0xf6, 0x36, 0x26, 0x6a, 0xff), // beholdr
        new(0xb2, 0x26, 0x62, 0xa6, 0x32, 0xf6, 0x36, 0x26, 0xa2, 0xff), // beholda
        new(0xb2, 0x26, 0x62, 0xa6, 0x32, 0xf6, 0x36, 0x26, 0x36, 0xff), // beholdl
        new(0xb2, 0x26, 0x62, 0xa6, 0x32, 0xf6, 0x36, 0x26, 0xff), // behold
    ];

    public readonly cheatseq_t cheat_clev = new(0xb2, 0x26, 0xe2, 0x36, 0xa6, 0x6e, 1, 0, 0, 0xff); // idclev

    // my position cheat
    public readonly cheatseq_t cheat_mypos = new(0xb2, 0x26, 0xb6, 0xba, 0x2a, 0xf6, 0xea, 0xff); // idmypos

    /// <summary>The parameter of a cheat whose two characters are not both digits (vanilla computes with them; SPEC §12 T7.9a).</summary>
    public const byte NOTDIGITS = 0xff;

    /// <summary>
    /// <c>ST_Responder</c>'s cheats for key <paramref name="key"/> (a
    /// doomkeys.h code: a lower-case letter or a digit as typed; any other
    /// key starts the sequences again), in vanilla's order: with
    /// <paramref name="allowed"/> (<c>!netgame &amp;&amp; gameskill != sk_nightmare</c>)
    /// the cheats on the player, <c>idmus</c> and the power-ups (else their
    /// sequences don't read the key, as vanilla's); then, with
    /// <paramref name="clev"/> (<c>!netgame</c>), <c>idclev</c>. The noclip
    /// cheat is <c>idspispopd</c> for Doom's mission, <c>idclip</c> for the
    /// others (<paramref name="mission"/>, Chocolate Doom's as the DOS
    /// executables). Adds the cheats matched to <paramref name="matched"/>
    /// with their parameter (two digits as a number, 0–99, else <see cref="NOTDIGITS"/>).
    /// </summary>
    public void Check(byte key, bool allowed, bool clev, GameMission mission, List<(cheat_t Cheat, byte Param)> matched)
    {
        if (allowed)
        {
            if (cheat_god.cht_CheckCheat(key))
                matched.Add((cheat_t.cht_god, 0));
            else if (cheat_ammonokey.cht_CheckCheat(key))
                matched.Add((cheat_t.cht_ammonokey, 0));
            else if (cheat_ammo.cht_CheckCheat(key))
                matched.Add((cheat_t.cht_ammo, 0));
            else if (cheat_mus.cht_CheckCheat(key))
                matched.Add((cheat_t.cht_mus, Digits(cheat_mus.cht_GetParam())));
            else if (mission == GameMission.doom && cheat_noclip.cht_CheckCheat(key)
                || mission != GameMission.doom && cheat_commercial_noclip.cht_CheckCheat(key))
                matched.Add((cheat_t.cht_noclip, 0));

            for (int i = 0; i < 6; i++)
            {
                if (cheat_powerup[i].cht_CheckCheat(key))
                    matched.Add((cheat_t.cht_powerup_v + i, 0));
            }

            if (cheat_powerup[6].cht_CheckCheat(key))
                matched.Add((cheat_t.cht_behold, 0));
            else if (cheat_choppers.cht_CheckCheat(key))
                matched.Add((cheat_t.cht_choppers, 0));
            else if (cheat_mypos.cht_CheckCheat(key))
                matched.Add((cheat_t.cht_mypos, 0));
        }

        if (clev && cheat_clev.cht_CheckCheat(key))
            matched.Add((cheat_t.cht_clev, Digits(cheat_clev.cht_GetParam())));
    }

    /// <summary>
    /// How far into a cheat the keys typed are: the most bytes any sequence
    /// matched (0 when none is under way; 2 after "id", which every cheat starts with).
    /// </summary>
    public int Typed()
    {
        int typed = 0;
        foreach (cheatseq_t c in All())
            typed = Math.Max(typed, c.p);
        return typed;
    }

    /// <summary>Every sequence starts again (vanilla's state at start-up).</summary>
    public void Reset()
    {
        foreach (cheatseq_t c in All())
        {
            if (Array.IndexOf(c.sequence, (byte)1) is int one and >= 0)
            {
                for (int i = one + 1; c.sequence[i] != 0xff; i++)
                    c.sequence[i] = 0;
            }
            c.p = 0;
        }
    }

    private IEnumerable<cheatseq_t> All()
    {
        yield return cheat_mus;
        yield return cheat_choppers;
        yield return cheat_god;
        yield return cheat_ammo;
        yield return cheat_ammonokey;
        yield return cheat_noclip;
        yield return cheat_commercial_noclip;
        foreach (cheatseq_t c in cheat_powerup)
            yield return c;
        yield return cheat_clev;
        yield return cheat_mypos;
    }

    private static byte Digits(byte[] buf) =>
        buf.Length >= 2 && buf[0] is >= (byte)'0' and <= (byte)'9' && buf[1] is >= (byte)'0' and <= (byte)'9'
            ? (byte)((buf[0] - '0') * 10 + buf[1] - '0') : NOTDIGITS;

    /// <summary>
    /// A key as <see cref="Check"/> takes it: a letter (lower case, as
    /// vanilla's key codes: Shift doesn't change them) or a digit as its
    /// character; any other key a code no sequence holds (vanilla's other key
    /// codes, which start the sequences again). The game scene's glue maps Godot's keys to it.
    /// </summary>
    public static byte KeyCode(int doomKey) => doomKey is (>= 'a' and <= 'z') or (>= '0' and <= '9') ? (byte)doomKey : (byte)0x80;
}
