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

P12e: demos.sh and ../OplRef/music.sh read WAD directories with lumps() and
lump_name(), importing this file by its path (`python3 -I -B` with its folder
put first on sys.path), so the directory is parsed in one place.
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


def lump_name(raw):
    """A directory entry's 8-byte name as W_CheckNumForName compares it: to its NUL, upper case."""
    return raw.split(b'\0')[0].decode('ascii', 'replace').upper()


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
