#!/usr/bin/env python3
"""IsoDoom vs vanilla, view by view (T2.9; dev tool). Usage:

    compare.py DIR IWAD [--images]

DIR holds vanilla's vN.ppm (dump.c: 320x200, cyan where vanilla draws a
ceiling or sky) and IsoDoom's oN.png (a 16:10 multiple of 320x200, void in
pure green, --level-background=00ff00). Each IsoDoom pixel at a vanilla
pixel's top-left corner (vanilla samples there) is compared, ignoring pixels
vanilla leaves cyan. A differing pixel counts as `near` when a vanilla pixel
within 2 pixels has its colour (sub-texel sampling, vanilla's fixed-point
wobble), as `dark`/`bright` when it is the same texel one or two COLORMAP
levels darker/brighter than a nearby vanilla pixel (a light-table step at a
slightly different distance), else as `other`; `blob` counts `other` pixels
in 4-connected groups of 12 or more (a misaligned texture shows there).
`holes` counts IsoDoom void pixels where vanilla drew all around (cracks, or
a 1-pixel ceiling strip vanilla rounds the other way), `specks` IsoDoom void
pixels with geometry all around them (T-junction sparkles). --images writes
dN.png: grey = equal, green = near/light, red = other, blue = vanilla
ceiling/sky.
"""
import os, struct, subprocess, sys, zlib
from collections import defaultdict

GREEN = (0, 255, 0)


def read_ppm(path):
    data = open(path, 'rb').read()
    parts = data.split(b'\n', 3)
    w, h = map(int, parts[1].split())
    return w, h, parts[3]


def read_png(path):
    d = open(path, 'rb').read()
    i, idat = 8, b''
    while i < len(d):
        n, t = struct.unpack('>I4s', d[i:i + 8])
        c = d[i + 8:i + 8 + n]
        i += 12 + n
        if t == b'IHDR':
            w, h, _, ct = struct.unpack('>IIBB', c[:10])
        elif t == b'IDAT':
            idat += c
    bpp = {2: 3, 6: 4}[ct]
    raw, stride = zlib.decompress(idat), w * bpp
    out, prev, pos = bytearray(), bytearray(stride), 0
    for _ in range(h):
        f, line = raw[pos], bytearray(raw[pos + 1:pos + 1 + stride])
        pos += 1 + stride
        for x in range(stride):
            a = line[x - bpp] if x >= bpp else 0
            b = prev[x]
            c = prev[x - bpp] if x >= bpp else 0
            if f == 1: line[x] = (line[x] + a) & 255
            elif f == 2: line[x] = (line[x] + b) & 255
            elif f == 3: line[x] = (line[x] + (a + b) // 2) & 255
            elif f == 4:
                pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2 * c)
                line[x] = (line[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        prev = line
        out += line
    return w, h, bpp, bytes(out)


def write_png(path, w, h, rgb):
    raw = b''.join(b'\0' + rgb[y * w * 3:(y + 1) * w * 3] for y in range(h))
    chunk = lambda t, d: struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)
    open(path, 'wb').write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0))
                           + chunk(b'IDAT', zlib.compress(raw, 6)) + chunk(b'IEND', b''))


def lump_tables(iwad):
    wad = open(iwad, 'rb').read()
    n, off = struct.unpack('<4xii', wad[:12])
    lumps = {}
    for k in range(n):
        p, sz, nm = struct.unpack('<ii8s', wad[off + 16 * k:off + 16 * k + 16])
        lumps.setdefault(nm.rstrip(b'\0').decode(), p)
    p = lumps['PLAYPAL']
    pal = [bytes(wad[p + 3 * i:p + 3 * i + 3]) for i in range(256)]
    p = lumps['COLORMAP']
    cm = [wad[p + 256 * m:p + 256 * m + 256] for m in range(34)]
    index = defaultdict(list)
    for i, c in enumerate(pal):
        index[c].append(i)
    shifts = defaultdict(set)  # (vanilla index, IsoDoom index) -> colormap steps
    for t in range(256):
        for m in range(32):
            for d in (-2, -1, 1, 2):
                if 0 <= m + d < 32 and cm[m][t] != cm[m + d][t]:
                    shifts[(cm[m][t], cm[m + d][t])].add(d)
    return index, shifts


def compare(args):
    folder, i, images, (index, shifts) = args
    vw, vh, v = read_ppm(f'{folder}/v{i}.ppm')
    ow, oh, bpp, o = read_png(f'{folder}/o{i}.png')
    s = ow // vw
    none = b'\x00\xff\xff'
    vpix = lambda x, y: v[(y * vw + x) * 3:(y * vw + x) * 3 + 3]
    is_void = lambda p: o[p] < 8 and o[p + 1] > 247 and o[p + 2] < 8

    def light(vc, oc):
        ds = set()
        for iv in index.get(vc, ()):
            for io in index.get(oc, ()):
                ds |= shifts.get((iv, io), set())
        return ds

    st = defaultdict(int)
    other = bytearray(vw * vh)
    img = bytearray()
    for y in range(vh):
        for x in range(vw):
            vc = vpix(x, y)
            if vc == none:
                img += bytes((0, 0, 90))
                continue
            p = (y * s * ow + x * s) * bpp
            oc = o[p:p + 3]
            st['drawn'] += 1
            if oc == vc:
                st['equal'] += 1
                img += bytes(c // 3 for c in vc)
                continue
            near = [vpix(xx, yy) for yy in range(max(0, y - 2), min(vh, y + 3)) for xx in range(max(0, x - 2), min(vw, x + 3))]
            if oc in near:
                st['near'] += 1
                img += bytes((0, 120, 0))
                continue
            ds = set()
            for c in near:
                ds |= light(c, oc)
            if ds and all(d < 0 for d in ds):
                st['dark'] += 1
                img += bytes((0, 120, 0))
            elif ds and all(d > 0 for d in ds):
                st['bright'] += 1
                img += bytes((0, 120, 0))
            elif not ds:
                st['other'] += 1
                other[y * vw + x] = 1
                img += bytes((255, 0, 0))
            else:
                st['near'] += 1
                img += bytes((0, 120, 0))
    seen = bytearray(vw * vh)
    for start in range(vw * vh):
        if other[start] and not seen[start]:
            stack, comp = [start], 0
            seen[start] = 1
            while stack:
                c = stack.pop()
                comp += 1
                cx, cy = c % vw, c // vw
                for nx, ny in ((cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)):
                    if 0 <= nx < vw and 0 <= ny < vh and other[ny * vw + nx] and not seen[ny * vw + nx]:
                        seen[ny * vw + nx] = 1
                        stack.append(ny * vw + nx)
            if comp >= 12:
                st['blob'] += comp
    for y in range(oh):
        for x in range(ow):
            p = (y * ow + x) * bpp
            if not is_void(p):
                continue
            vx, vy = x // s, y // s
            if all(vpix(xx, yy) != none for yy in range(max(0, vy - 1), min(vh, vy + 2)) for xx in range(max(0, vx - 1), min(vw, vx + 2))):
                st['holes'] += 1
            if 0 < x < ow - 1 and 0 < y < oh - 1 and not any(
                    is_void(((y + dy) * ow + x + dx) * bpp) for dy in (-1, 0, 1) for dx in (-1, 0, 1) if dx or dy):
                st['specks'] += 1
    if images:
        big = bytearray()
        for y in range(vh):
            row = b''.join(bytes(img[(y * vw + x) * 3:(y * vw + x) * 3 + 3]) * 4 for x in range(vw))
            big += row * 4
        write_png(f'{folder}/d{i}.png', vw * 4, vh * 4, bytes(big))
    return i, dict(st)


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    folder, iwad, images = sys.argv[1], sys.argv[2], '--images' in sys.argv
    views = sorted(int(f[1:-4]) for f in os.listdir(folder) if f.startswith('v') and f.endswith('.ppm'))
    if '--worker' in sys.argv:  # one share of the views, a line each
        k, n = map(int, sys.argv[sys.argv.index('--worker') + 1].split('/'))
        tables = lump_tables(iwad)
        for i in views[k::n]:
            i, st = compare((folder, i, images, tables))
            print(i, ' '.join(f'{k}={v}' for k, v in st.items()), flush=True)
        return
    names = open(f'{folder}/views.txt').read().strip().strip(';').split(';') if os.path.exists(f'{folder}/views.txt') else []
    n = os.cpu_count() or 1
    workers = [subprocess.Popen([sys.executable, __file__] + sys.argv[1:] + ['--worker', f'{k}/{n}'], stdout=subprocess.PIPE, text=True)
               for k in range(n)]
    results = []
    for w in workers:
        out, _ = w.communicate()
        if w.returncode != 0:
            sys.exit(f'compare.py: a worker failed ({w.returncode})')
        for line in out.splitlines():
            i, *fields = line.split()
            results.append((int(i), {k: int(v) for k, v in (f.split('=') for f in fields)}))
    keys = ('drawn', 'equal', 'near', 'dark', 'bright', 'other', 'blob', 'holes', 'specks')
    total = defaultdict(int)
    for i, st in sorted(results):
        print(f'v{i} [{names[i] if i < len(names) else ""}] ' + ' '.join(f'{k} {st.get(k, 0)}' for k in keys))
        for k in keys:
            total[k] += st.get(k, 0)
    print(f'total ({len(results)} views): ' + ' '.join(f'{k} {total[k]}' for k in keys))


if __name__ == '__main__':
    main()
