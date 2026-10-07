#!/usr/bin/env python3
"""IsoDoom's HUD vs vanilla's along a route (T6.11; dev tool). Usage:

    hud.py DIR TIC... [--images]

Compares, for each TIC, vanilla's HUD (DIR/hudTIC.ppm, written by dump.c with
$DUMP_HUD_DIR/$DUMP_HUD_TICS: 320x48, the message line's rows 0-15, black
where nothing is drawn, over the status bar's rows 168-199, in the tic's
palette) with the level scene's screenshot after the same tic (DIR/oTIC.png,
1280x800: the HUD at scale 4, the bar at the bottom, the message line at the
top left): every status bar pixel, and the message line's pixels vanilla
draws. Prints the differing pixels per tic and exits 1 on any; with --images
writes DIR/cTIC.png: vanilla's HUD over IsoDoom's (cropped and scaled down
to 320 wide), differing pixels in magenta below. No PIL: the dev container's
Python is minimal.
"""
import struct, sys, zlib

SCALE = 4


def read_ppm(path):
    data = open(path, 'rb').read()
    parts = data.split(b'\n', 3)
    w, h = map(int, parts[1].split())
    return w, h, parts[3]


def read_png(path):
    """8-bit RGB or RGBA, non-interlaced (what Godot's SavePng writes); returns w, h, RGB bytes."""
    data = open(path, 'rb').read()
    assert data[:8] == b'\x89PNG\r\n\x1a\n', path
    pos, idat = 8, b''
    while pos < len(data):
        n, kind = struct.unpack('>I4s', data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + n]
        if kind == b'IHDR':
            w, h, depth, ctype, _, _, interlace = struct.unpack('>IIBBBBB', body)
            assert depth == 8 and ctype in (2, 6) and interlace == 0, f'{path}: unsupported PNG'
        elif kind == b'IDAT':
            idat += body
        pos += n + 12
    bpp = 3 if ctype == 2 else 4
    raw = zlib.decompress(idat)
    stride = w * bpp
    out = bytearray()
    prev = bytearray(stride)
    for y in range(h):
        f = raw[y * (stride + 1)]
        line = bytearray(raw[y * (stride + 1) + 1:(y + 1) * (stride + 1)])
        for i in range(stride):
            a = line[i - bpp] if i >= bpp else 0
            b = prev[i]
            c = prev[i - bpp] if i >= bpp else 0
            if f == 1:
                line[i] = (line[i] + a) & 255
            elif f == 2:
                line[i] = (line[i] + b) & 255
            elif f == 3:
                line[i] = (line[i] + (a + b) // 2) & 255
            elif f == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[i] = (line[i] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        prev = line
        for x in range(w):
            out += line[x * bpp:x * bpp + 3]
    return w, h, bytes(out)


def write_png(path, w, rows):
    raw = b''.join(b'\0' + r for r in rows)

    def chunk(t, d):
        return struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)
    open(path, 'wb').write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, len(rows), 8, 2, 0, 0, 0))
                           + chunk(b'IDAT', zlib.compress(raw)) + chunk(b'IEND', b''))


def main():
    args = [a for a in sys.argv[1:] if a != '--images']
    images = '--images' in sys.argv
    d, tics = args[0], args[1:]
    failed = 0
    for tic in tics:
        vw, vh, v = read_ppm(f'{d}/hud{tic}.ppm')
        ow, oh, o = read_png(f'{d}/o{tic}.png')
        left = (ow - vw * SCALE) // 2
        mine = bytearray(vw * vh * 3)
        for y in range(vh):
            # rows 0-15 at the top of the window, rows 16-47 (the bar) at its bottom; the centre of each scaled pixel
            sy = y * SCALE + SCALE // 2 if y < 16 else oh - (vh - y) * SCALE + SCALE // 2
            for x in range(vw):
                sx = left + x * SCALE + SCALE // 2
                mine[(y * vw + x) * 3:(y * vw + x) * 3 + 3] = o[(sy * ow + sx) * 3:(sy * ow + sx) * 3 + 3]
        bad = []
        drawn = 0
        for i in range(vw * vh):
            y = i // vw
            pv, pm = v[i * 3:i * 3 + 3], bytes(mine[i * 3:i * 3 + 3])
            if y < 16 and pv == b'\0\0\0':
                continue  # the message line draws nothing here: the level shows
            drawn += 1
            if pv != pm:
                bad.append(i)
        print(f'tic {tic}: {len(bad)} of {drawn} HUD pixels differ' + (f' (first at {bad[0] % vw},{bad[0] // vw})' if bad else ''))
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
