#!/usr/bin/env python3
"""IsoDoom's intermission vs vanilla's (T7.4; dev tool). Usage:

    wi.py DIR TIC... [--images]

Compares, for each TIC, vanilla's intermission screen (DIR/wiTIC.ppm, written
by dump.c with $DUMP_WI_DIR/$DUMP_WI_TICS: 320x200 in palette 0, cyan where
nothing is drawn) with the game scene's screenshot after the same
intermission tic (DIR/oTIC.png, 1280x800: the screen at scale 4), every
pixel. Prints the differing pixels per tic and exits 1 on any; with --images
writes DIR/cTIC.png: vanilla's screen over IsoDoom's (sampled back to
320x200), differing pixels in magenta below. No PIL: the dev container's
Python is minimal.
"""
import os, sys

sys.dont_write_bytecode = True  # no __pycache__ in the repo
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from hud import read_ppm, read_png, write_png  # noqa: E402

SCALE = 4


def main():
    args = [a for a in sys.argv[1:] if a != '--images']
    images = '--images' in sys.argv
    d, tics = args[0], args[1:]
    failed = 0
    for tic in tics:
        vw, vh, v = read_ppm(f'{d}/wi{tic}.ppm')
        ow, oh, o = read_png(f'{d}/o{tic}.png')
        left, top = (ow - vw * SCALE) // 2, (oh - vh * SCALE) // 2
        mine = bytearray(vw * vh * 3)
        for y in range(vh):
            sy = top + y * SCALE + SCALE // 2
            for x in range(vw):
                sx = left + x * SCALE + SCALE // 2
                mine[(y * vw + x) * 3:(y * vw + x) * 3 + 3] = o[(sy * ow + sx) * 3:(sy * ow + sx) * 3 + 3]
        bad = [i for i in range(vw * vh) if v[i * 3:i * 3 + 3] != bytes(mine[i * 3:i * 3 + 3])]
        print(f'tic {tic}: {len(bad)} of {vw * vh} pixels differ' + (f' (first at {bad[0] % vw},{bad[0] // vw})' if bad else ''))
        failed |= bool(bad)
        if images:
            rows = [v[y * vw * 3:(y + 1) * vw * 3] for y in range(vh)]
            rows += [bytes(mine[y * vw * 3:(y + 1) * vw * 3]) for y in range(vh)]
            badset = set(bad)
            rows += [b''.join(b'\xff\x00\xff' if y * vw + x in badset else b'\0\0\0' for x in range(vw)) for y in range(vh)]
            write_png(f'{d}/c{tic}.png', vw, rows)
    sys.exit(1 if failed else 0)


if __name__ == '__main__':
    main()
