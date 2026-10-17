#!/usr/bin/env python3
"""One IWAD of an IWAD and PWADs, as vanilla's -file loads them (T11.8's follow-up; dev tool). Usage:

    mergewad.py OUT.wad IWAD PWAD...

Writes OUT.wad, an IWAD whose lump directory is the IWAD's followed by each
PWAD's, in order: the directory vanilla's w_wad.c builds for `-iwad IWAD
-file PWAD...`, so every lump lookup (the last of a name) finds the same
lump. wi.sh and fi.sh give it to the game scene for the synthetic IWAD's
routes, which the vanilla reference plays as -file over DOOM1.WAD and the
game cannot (the shareware IWAD refuses -file, as vanilla's d_main.c).
OUT.wad is WAD-derived: keep it out of the repo.
"""
import struct
import sys


def lumps(path):
    data = open(path, 'rb').read()
    ident, count, offset = struct.unpack_from('<4sii', data, 0)
    if ident not in (b'IWAD', b'PWAD'):
        sys.exit(f'{path}: not a WAD')
    for i in range(count):
        pos, size, name = struct.unpack_from('<ii8s', data, offset + 16 * i)
        yield name, data[pos:pos + size]


def main():
    if len(sys.argv) < 4:
        sys.exit(__doc__)
    out, wads = sys.argv[1], sys.argv[2:]
    entries = [entry for path in wads for entry in lumps(path)]
    body = bytearray()
    directory = bytearray()
    for name, data in entries:
        directory += struct.pack('<ii8s', 12 + len(body), len(data), name)
        body += data
    with open(out, 'wb') as f:
        f.write(struct.pack('<4sii', b'IWAD', len(entries), 12 + len(body)))
        f.write(body)
        f.write(directory)


if __name__ == '__main__':
    main()
