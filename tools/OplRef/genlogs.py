#!/usr/bin/env python3
"""Writes the synthetic OPL register-write logs of tools/OplRef/logs (T7.8a).

Each log is plain text (read by opl_ref.c and by Opl3Tests):
  # comment
  rate N        OPL3_Reset's rate; samples come from OPL3_GenerateStream
                (resampled). Without it: 49716 Hz and OPL3_Generate (the
                chip's own samples).
  length N      the stereo samples to render
  buffered      the writes go through OPL3_WriteRegBuffered, not OPL3_WriteReg
  SAMPLE REG VALUE
                before output sample SAMPLE (decimal), write VALUE to REG (hex)

Usage: genlogs.py [DIR] (default: the logs directory beside this script).
The content is generated here, not taken from any game.
"""
import os
import sys

# Operator offsets of channels 0-8 in a bank (the modulator; the carrier is +3).
MOD = [0x00, 0x01, 0x02, 0x08, 0x09, 0x0A, 0x10, 0x11, 0x12]

# F-numbers of a scale at block 4 (C D E F G A B C), 49716 Hz chip clock.
SCALE = [0x158, 0x182, 0x1B0, 0x1CA, 0x202, 0x241, 0x287, 0x2B0]


class Log:
    def __init__(self, name, comment, rate=None, buffered=False):
        self.name = name
        self.comment = comment
        self.rate = rate
        self.buffered = buffered
        self.writes = []
        self.t = 0

    def sec(self, s):
        return int(round(s * (self.rate or 49716)))

    def w(self, reg, val):
        self.writes.append((self.t, reg, val & 0xFF))

    def wait(self, s):
        self.t += self.sec(s)

    def op(self, bank, ch, which, am=0, vib=0, egt=1, ksr=0, mult=1, ksl=0, tl=0,
           ar=15, dr=4, sl=4, rr=6, wf=0):
        o = (0x100 if bank else 0) + MOD[ch] + (3 if which else 0)
        self.w(0x20 + o, (am << 7) | (vib << 6) | (egt << 5) | (ksr << 4) | mult)
        self.w(0x40 + o, (ksl << 6) | tl)
        self.w(0x60 + o, (ar << 4) | dr)
        self.w(0x80 + o, (sl << 4) | rr)
        self.w(0xE0 + o, wf)

    def conn(self, bank, ch, fb=0, con=0, pan=0x30):
        self.w((0x100 if bank else 0) + 0xC0 + ch, pan | (fb << 1) | con)

    def note(self, bank, ch, fnum, block, on=True):
        b = 0x100 if bank else 0
        self.w(b + 0xA0 + ch, fnum & 0xFF)
        self.w(b + 0xB0 + ch, (0x20 if on else 0) | (block << 2) | (fnum >> 8))

    def save(self, d):
        path = os.path.join(d, self.name + ".log")
        with open(path, "w") as f:
            for line in self.comment.strip().splitlines():
                f.write("# " + line + "\n")
            if self.rate:
                f.write(f"rate {self.rate}\n")
            f.write(f"length {self.t}\n")
            if self.buffered:
                f.write("buffered\n")
            for t, reg, val in self.writes:  # in time order already (stable)
                f.write(f"{t} {reg:03x} {val:02x}\n")
        print(path)


def waveforms():
    g = Log("waveforms", """
A note per waveform (0-7) in OPL3 mode, the carrier alone, then frequency
modulated with each feedback level; then OPL2 mode, where waveforms 4-7
fold to 0-3 (49716 Hz, OPL3_Generate).""")
    g.w(0x105, 0x01)
    for wf in range(8):
        g.op(0, 0, 0, tl=63)
        g.op(0, 0, 1, wf=wf, sl=2, dr=3)
        g.conn(0, 0, con=1)
        g.note(0, 0, SCALE[wf], 4)
        g.wait(0.15)
        g.note(0, 0, SCALE[wf], 4, on=False)
        g.wait(0.05)
    for wf in range(8):
        g.op(0, 1, 0, wf=wf, tl=24, mult=2)
        g.op(0, 1, 1, wf=(wf + 3) & 7, sl=1)
        g.conn(0, 1, fb=wf, con=0)
        g.note(0, 1, SCALE[7 - wf], 3)
        g.wait(0.15)
        g.note(0, 1, SCALE[7 - wf], 3, on=False)
        g.wait(0.05)
    g.w(0x105, 0x00)
    for wf in range(8):
        g.op(0, 2, 0, wf=wf, tl=30, mult=3)
        g.op(0, 2, 1, wf=wf)
        g.conn(0, 2, fb=4, con=wf & 1)
        g.note(0, 2, SCALE[wf], 5)
        g.wait(0.12)
        g.note(0, 2, SCALE[wf], 5, on=False)
        g.wait(0.03)
    g.wait(0.2)
    return g


def envelopes():
    g = Log("envelopes", """
Envelopes in OPL2 mode on nine channels at once: attack, decay, sustain
level and release rates from slowest to instant, sustaining and percussive
(EG type), key scaling of rate (KSR) and level (KSL 1-3) across blocks, the
note select bit (register 08), key on again during a release, a zero attack
rate, and level changes under a held note (48000 Hz, resampled).""", rate=48000)
    sets = [
        dict(ar=15, dr=0, sl=0, rr=15, egt=1),
        dict(ar=1, dr=1, sl=8, rr=1, egt=1),
        dict(ar=4, dr=8, sl=15, rr=4, egt=0),
        dict(ar=12, dr=12, sl=3, rr=12, egt=0, ksr=1),
        dict(ar=8, dr=5, sl=6, rr=7, egt=1, ksl=1, tl=10),
        dict(ar=10, dr=2, sl=2, rr=3, egt=1, ksl=2, tl=4),
        dict(ar=13, dr=7, sl=10, rr=9, egt=0, ksl=3, ksr=1),
        dict(ar=0, dr=4, sl=4, rr=4, egt=1),
        dict(ar=14, dr=15, sl=12, rr=2, egt=1, tl=20),
    ]
    for ch, s in enumerate(sets):
        g.op(0, ch, 0, mult=1, tl=40, ar=s["ar"], dr=s["dr"], sl=s["sl"], rr=s["rr"])
        g.op(0, ch, 1, **s)
        g.conn(0, ch, fb=2, con=ch & 1)
    for block in (1, 4, 7):
        for ch in range(9):
            g.note(0, ch, SCALE[ch % 8], block)
        g.wait(0.6)
        for ch in range(9):
            g.note(0, ch, SCALE[ch % 8], block, on=False)
        g.wait(0.15)
        # key on again in the release (the C's reset path), then off
        for ch in range(0, 9, 2):
            g.note(0, ch, SCALE[ch % 8], block)
        g.wait(0.2)
        for ch in range(0, 9, 2):
            g.note(0, ch, SCALE[ch % 8], block, on=False)
        g.wait(0.3)
        g.w(0x08, 0x40 if block == 1 else 0x00)
    g.note(0, 4, SCALE[2], 4)
    for tl in range(0, 64, 9):
        g.w(0x40 + MOD[4] + 3, (1 << 6) | tl)
        g.wait(0.05)
    g.note(0, 4, SCALE[2], 4, on=False)
    g.wait(0.5)
    return g


def vibtrem():
    g = Log("vibtrem", """
Tremolo (AM) and vibrato on long notes with each of register BD's depth
bit combinations, on the modulator, the carrier and both, plus a frequency
sweep (44100 Hz, resampled).""", rate=44100)
    combos = [(1, 0), (0, 1), (1, 1)]
    for ch, (am, vib) in enumerate(combos):
        g.op(0, ch, 0, am=am, vib=0, tl=28, mult=2)
        g.op(0, ch, 1, am=am, vib=vib, mult=1, dr=1, sl=1, rr=5)
        g.conn(0, ch, fb=3)
    g.op(0, 3, 0, am=0, vib=1, tl=18, mult=1)
    g.op(0, 3, 1, am=0, vib=0, mult=1)
    g.conn(0, 3, fb=1)
    for bd in (0x00, 0x40, 0x80, 0xC0):
        g.w(0xBD, bd)
        for ch in range(4):
            g.note(0, ch, SCALE[ch * 2], 4 + (ch & 1))
        g.wait(0.6)
        for ch in range(4):
            g.note(0, ch, SCALE[ch * 2], 4 + (ch & 1), on=False)
        g.wait(0.1)
    g.w(0xBD, 0xC0)
    for i in range(0, 1024, 16):
        g.note(0, 2, i, 5)
        g.wait(0.01)
    g.note(0, 2, 1008, 5, on=False)
    g.wait(0.4)
    return g


def opl3_4op():
    g = Log("opl3-4op", """
OPL3 mode: 4-op channels in both banks with each of the four algorithms
and feedback, stereo output bits (left, right, both, outputs C and D only),
2-op channels of bank 1 panned, writes to a 4-op channel's second half
(ignored), algorithms changed and 4-op turned off under held notes, and
over 1024 writes in one sample, all through OPL3_WriteRegBuffered
(48000 Hz, resampled).""", rate=48000, buffered=True)
    g.w(0x105, 0x01)
    g.w(0x104, 0x3F)
    pans = [0x10, 0x20, 0x30, 0x30, 0x10, 0x20]
    chans = [(0, 0), (0, 1), (0, 2), (1, 0), (1, 1), (1, 2)]
    for i, (bank, ch) in enumerate(chans):
        alg = i & 3
        for half, c in enumerate((ch, ch + 3)):
            g.op(bank, c, 0, tl=20 + 4 * half, mult=1 + half, wf=i % 8)
            g.op(bank, c, 1, tl=8 * half, mult=2 - half, wf=(i + 2) % 8, dr=2, sl=3)
        g.conn(bank, ch, fb=i + 1, con=alg >> 1, pan=pans[i])
        g.conn(bank, ch + 3, fb=0, con=alg & 1, pan=pans[i])
    for i, (bank, ch) in enumerate(chans):
        g.note(bank, ch, SCALE[i], 4)
        g.wait(0.25)
    g.note(0, 3, SCALE[7], 6)
    g.wait(0.3)
    for i, (bank, ch) in enumerate(chans):
        alg = (i + 1) & 3
        g.conn(bank, ch, fb=7 - i, con=alg >> 1, pan=0xF0)
        g.conn(bank, ch + 3, fb=0, con=alg & 1, pan=0xC0 if i == 5 else 0x30)
        g.wait(0.05)
    g.wait(0.3)
    g.w(0x104, 0x05)
    g.wait(0.3)
    for i, (bank, ch) in enumerate(chans):
        g.note(bank, ch, SCALE[i], 4, on=False)
    g.note(0, 3, SCALE[7], 6, on=False)
    g.wait(0.2)
    for i, ch in enumerate((6, 7, 8)):
        g.op(1, ch, 0, tl=26, mult=1, wf=i + 4)
        g.op(1, ch, 1, mult=1, wf=i, dr=2, sl=2)
        g.conn(1, ch, fb=4, con=0, pan=[0x10, 0x20, 0x30][i])
        g.note(1, ch, SCALE[i + 3], 3 + i)
    g.wait(0.5)
    for k in range(1100):
        g.w(0x100 + 0xA0 + 6 + (k % 3), (k * 7) & 0xFF)
    g.wait(0.5)
    for i, ch in enumerate((6, 7, 8)):
        g.note(1, ch, SCALE[i + 3], 3 + i, on=False)
    g.wait(0.3)
    return g


def rhythm():
    g = Log("rhythm", """
Rhythm mode in OPL2 mode: bass drum, snare, tom, cymbal and hi-hat alone
and together (the noise and the hi-hat and cymbal phase bits), the bass
drum's other algorithm and the depth bits, then rhythm mode off with notes
on its channels (49716 Hz, OPL3_Generate).""")
    g.op(0, 6, 0, tl=10, mult=0, ar=15, dr=6, sl=10, rr=6, egt=0)
    g.op(0, 6, 1, tl=0, mult=0, ar=15, dr=5, sl=10, rr=6, egt=0)
    g.conn(0, 6, fb=6, con=0)
    g.op(0, 7, 0, tl=4, mult=1, ar=15, dr=8, sl=8, rr=8, egt=0)   # hh
    g.op(0, 7, 1, tl=2, mult=1, ar=15, dr=7, sl=7, rr=7, egt=0)   # sd
    g.op(0, 8, 0, tl=6, mult=2, ar=15, dr=6, sl=6, rr=6, egt=0)   # tom
    g.op(0, 8, 1, tl=4, mult=1, ar=15, dr=9, sl=9, rr=9, egt=0)   # tc
    g.note(0, 6, 0x150, 2, on=False)
    g.note(0, 7, 0x1C0, 3, on=False)
    g.note(0, 8, 0x0F0, 4, on=False)
    for bits in (0x10, 0x08, 0x04, 0x02, 0x01, 0x11, 0x0A, 0x1F, 0x05, 0x13):
        g.w(0xBD, 0x20 | bits)
        g.wait(0.15)
        g.w(0xBD, 0x20)
        g.wait(0.05)
    g.conn(0, 6, fb=6, con=1)
    g.note(0, 7, 0x2A0, 5, on=False)
    g.note(0, 8, 0x180, 6, on=False)
    for bits in (0x10, 0x1F, 0x03):
        g.w(0xBD, 0xE0 | bits)
        g.wait(0.15)
        g.w(0xBD, 0xE0)
        g.wait(0.05)
    g.w(0xBD, 0x3F)
    g.wait(0.1)
    g.w(0xBD, 0x00)
    for ch in (6, 7, 8):
        g.note(0, ch, SCALE[ch - 3], 4)
    g.wait(0.4)
    for ch in (6, 7, 8):
        g.note(0, ch, SCALE[ch - 3], 4, on=False)
    g.wait(0.3)
    return g


def main():
    d = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "logs")
    os.makedirs(d, exist_ok=True)
    for f in (waveforms, envelopes, vibtrem, opl3_4op, rhythm):
        f().save(d)


if __name__ == "__main__":
    main()
