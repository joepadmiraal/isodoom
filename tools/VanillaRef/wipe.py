#!/usr/bin/env python3
"""IsoDoom's screen wipes vs vanilla's (T7.1a; dev tool). Usage:

    wipe.py DIR NAME WIPE STEP... [--images]

Compares, for each melt STEP of vanilla's wipe WIPE (from 1) of route NAME,
vanilla's screen after that step (DIR/wipeWIPE-sSTEP.ppm, written by dump.c
with $DUMP_WIPE_DIR/$DUMP_WIPE_STEPS: 320x200 in palette 0) with the game
scene's screenshot held at the same step (DIR/oSTEP.png, 1280x800: vanilla's
screen at scale 4, a column 8 pixels wide, a row 4 high), every pixel the
melt takes from a full screen in both: the columns' offsets come from the
dump (DIR/NAME.wipe: the wipe's starting columns, its melt steps), the
screens' game states from its tic lines; pixels from the level (the iso
view, not vanilla's) are left out, as is the first wipe (vanilla's from its
startup screen). Prints the differing pixels per step and exits 1 on any;
with --images writes DIR/cSTEP.png: vanilla's screen over IsoDoom's (sampled
back to 320x200), differing pixels in magenta and pixels not compared in
grey below. No PIL: the dev container's Python is minimal.
"""
import os, sys

sys.dont_write_bytecode = True  # no __pycache__ in the repo
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from hud import read_ppm, read_png, write_png  # noqa: E402

W, H, SCALE = 320, 200, 4


def wipe_of(path, n):
    """Wipe n's starting columns, the game states of its start and end screens and its melt steps' tics.
    The start screen is what D_Display showed after the tic before the wipe's (vanilla's after every tic)."""
    lines = [l.split() for l in open(path) if l.strip()]
    wipes = [i for i, f in enumerate(lines) if f[0] == 'wipe']
    if n > len(wipes):
        sys.exit(f'{path}: {len(wipes)} wipes, no wipe {n}')
    i = wipes[n - 1]
    states = [int(f[2]) for f in lines[:i] if f[0] == 'tic']
    ticks = []
    for f in lines[i + 1:]:
        if f[0] != 'melt':
            break
        ticks.append(int(f[1]))
    return [int(v) for v in lines[i][4:]], states[-2], int(lines[i][2]), ticks


def melt(y, ticks):
    """f_wipe.c wipe_doMelt for each step's tics: the 160 columns' offsets after them."""
    y = list(y[:160])
    for t in ticks:
        for _ in range(t):
            for i in range(160):
                if y[i] < 0:
                    y[i] += 1
                elif y[i] < H:
                    dy = y[i] + 1 if y[i] < 16 else 8
                    y[i] += min(dy, H - y[i])
    return y


def main():
    args = sys.argv[1:]
    images = '--images' in args
    args = [a for a in args if a != '--images']
    d, name, n, steps = args[0], args[1], int(args[2]), [int(s) for s in args[3:]]
    if n < 2:
        sys.exit('wipe 1 is vanilla\'s from its startup screen: not comparable')
    path = f'{d}/{name}.wipe'
    y0, start_state, end_state, ticks = wipe_of(path, n)
    failed = 0
    for step in steps:
        y = melt(y0, ticks[:step])
        vw, vh, v = read_ppm(f'{d}/wipe{n}-s{step}.ppm')
        ow, oh, o = read_png(f'{d}/o{step}.png')
        if (ow, oh) != (W * SCALE, H * SCALE):
            sys.exit(f'{d}/o{step}.png: {ow}x{oh}, expected {W * SCALE}x{H * SCALE}')
        mine = bytearray(W * H * 3)
        compared = [False] * (W * H)
        for py in range(H):
            for px in range(W):
                k = py * W + px
                mine[k * 3:k * 3 + 3] = o[((py * SCALE + SCALE // 2) * ow + px * SCALE + SCALE // 2) * 3:][:3]
                from_end = py < max(y[px // 2], 0)
                compared[k] = (end_state if from_end else start_state) != 0  # not the level
        bad = [k for k in range(W * H) if compared[k] and v[k * 3:k * 3 + 3] != bytes(mine[k * 3:k * 3 + 3])]
        here = sum(compared)
        print(f'wipe {n} step {step}: {len(bad)} of {here} pixels differ' + (f' (first at {bad[0] % W},{bad[0] // W})' if bad else ''))
        failed |= bool(bad) or here == 0
        if images:
            rows = [v[r * W * 3:(r + 1) * W * 3] for r in range(H)]
            rows += [bytes(mine[r * W * 3:(r + 1) * W * 3]) for r in range(H)]
            badset = set(bad)
            rows += [b''.join(b'\xff\x00\xff' if r * W + x in badset else b'\0\0\0' if compared[r * W + x] else b'\x40\x40\x40'
                              for x in range(W)) for r in range(H)]
            write_png(f'{d}/c{step}.png', W, rows)
    sys.exit(1 if failed else 0)


if __name__ == '__main__':
    main()
