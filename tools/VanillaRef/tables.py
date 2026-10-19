#!/usr/bin/env python3
"""A PWAD's own PLAYPAL and COLORMAP against the IWAD's (P5; dev tool). Usage:

    tables.py strip OUTDIR PWAD...      copies each PWAD to OUTDIR with its PLAYPAL
                                        and COLORMAP renamed (XLAYPAL, XOLORMAP), so
                                        the IWAD's are found instead; prints the copies
    tables.py diff DIR TABLEDIR         per view: the pixels where vanilla's render
                                        with the set's tables (DIR/vN.ppm) and with the
                                        IWAD's (TABLEDIR/vN.ppm) differ, and at those
                                        pixels whether IsoDoom's (DIR/oN.png) is the
                                        first, the second or neither

compare.sh --tables runs both (with --pwad): a view whose colours the PWAD's
tables change only tells whether IsoDoom uses them at the pixels where they
differ from the IWAD's. `neither` there counts the usual sub-texel and
light-step differences (compare.py's near/dark/bright). Exits 1 when IsoDoom
matches the IWAD-table render at any such pixel and the set's at no more.
"""
import os
import struct
import sys

sys.dont_write_bytecode = True  # no __pycache__ in the repo
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from compare import read_png, read_ppm  # noqa: E402

TABLES = (b'PLAYPAL', b'COLORMAP')


def strip(outdir, pwads):
    os.makedirs(outdir, exist_ok=True)
    for path in pwads:
        wad = bytearray(open(path, 'rb').read())
        n, off = struct.unpack('<4xii', wad[:12])
        for k in range(n):
            p = off + 16 * k + 8
            if bytes(wad[p:p + 8]).split(b'\0')[0].upper() in TABLES:
                wad[p] = ord('X')
        out = os.path.join(outdir, os.path.basename(path))
        open(out, 'wb').write(wad)
        print(out)


def diff(folder, tabledir):
    views = sorted(int(f[1:-4]) for f in os.listdir(folder) if f.startswith('v') and f.endswith('.ppm'))
    total = [0, 0, 0, 0]
    for i in views:
        vw, vh, own = read_ppm(f'{folder}/v{i}.ppm')
        _, _, iwad = read_ppm(f'{tabledir}/v{i}.ppm')
        ow, _, bpp, o = read_png(f'{folder}/o{i}.png')
        s = ow // vw
        k = [0, 0, 0, 0]
        for y in range(vh):
            for x in range(vw):
                q = (y * vw + x) * 3
                a, b = own[q:q + 3], iwad[q:q + 3]
                if a == b:
                    continue
                k[0] += 1
                p = (y * s * ow + x * s) * bpp
                c = o[p:p + 3]
                k[1 if c == a else 2 if c == b else 3] += 1
        print(f'v{i}: tables differ at {k[0]} pixels: IsoDoom = the set\'s {k[1]}, = the IWAD\'s {k[2]}, neither {k[3]}')
        total = [t + u for t, u in zip(total, k)]
    print(f'tables total ({len(views)} views): differ {total[0]} set\'s {total[1]} IWAD\'s {total[2]} neither {total[3]}')
    if total[2] > 0 and total[2] >= total[1]:
        sys.exit('tables.py: IsoDoom drew the IWAD\'s PLAYPAL/COLORMAP colours')


def main():
    if len(sys.argv) >= 3 and sys.argv[1] == 'strip':
        strip(sys.argv[2], sys.argv[3:])
    elif len(sys.argv) == 4 and sys.argv[1] == 'diff':
        diff(sys.argv[2], sys.argv[3])
    else:
        sys.exit(__doc__)


if __name__ == '__main__':
    main()
