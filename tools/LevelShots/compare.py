#!/usr/bin/env python3
"""Compares two runs of shots.sh (dev tool). Usage:

    compare.py OLD_DIR NEW_DIR [--diff DIR]

For each MAP.png in both, prints how many pixels differ and exits 1 on any
difference (or a map missing from one side). With --diff writes DIR/MAP.png
for each differing map: the new shot at half brightness, the differing
pixels in magenta. No PIL: the dev container's Python is minimal.
"""
import os, sys

sys.dont_write_bytecode = True  # no __pycache__ in the repo
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'VanillaRef'))
from hud import read_png, write_png  # noqa: E402


HALF = bytes(c // 2 for c in range(256))


def main():
    args = sys.argv[1:]
    diff = None
    if '--diff' in args:
        i = args.index('--diff')
        diff = args[i + 1]
        del args[i:i + 2]
    if len(args) != 2:
        sys.exit(__doc__)
    old, new = args
    names = sorted(set(f for d in (old, new) for f in os.listdir(d) if f.endswith('.png')))
    failed = 0
    for name in names:
        a, b = os.path.join(old, name), os.path.join(new, name)
        if not os.path.exists(a) or not os.path.exists(b):
            print(f'{name}: only in {old if os.path.exists(a) else new}')
            failed = 1
            continue
        if open(a, 'rb').read() == open(b, 'rb').read():
            print(f'{name}: same')
            continue
        aw, ah, ap = read_png(a)
        bw, bh, bp = read_png(b)
        if (aw, ah) != (bw, bh):
            print(f'{name}: size {aw}x{ah} vs {bw}x{bh}')
            failed = 1
            continue
        if ap == bp:
            print(f'{name}: same')
            continue
        rows, count, stride = [], 0, bw * 3
        for y in range(bh):
            ra, rb = ap[y * stride:(y + 1) * stride], bp[y * stride:(y + 1) * stride]
            row = bytearray(rb.translate(HALF))
            if ra != rb:
                for i in range(0, stride, 3):
                    if ra[i:i + 3] != rb[i:i + 3]:
                        count += 1
                        row[i:i + 3] = b'\xff\x00\xff'
            rows.append(bytes(row))
        print(f'{name}: {count} pixels differ ({100 * count / (bw * bh):.3f}%)')
        failed = 1
        if diff:
            os.makedirs(diff, exist_ok=True)
            write_png(os.path.join(diff, name), bw, rows)
    sys.exit(failed)


if __name__ == '__main__':
    main()
