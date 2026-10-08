#!/usr/bin/env python3
"""Draws IsoDoom's icon: a horned demon skull over an isometric pentagram tile
in hellfire, as original pixel art (no WAD data). Writes icon.png (the 64x64
pixel grid at 1024 px) to the repo root.

    python3 tools/Icon/make_icon.py
"""
import math
import os
import struct
import zlib

N = 64
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")

BAYER = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]


def hexc(s):
    return tuple(int(s[i:i + 2], 16) for i in (0, 2, 4))


OUTLINE = hexc("0b0503")
SKY = [hexc(c) for c in ("1a0402", "2a0603", "3f0904", "570e05", "6f1606")]
FIRE = [hexc(c) for c in ("6f1606", "a32008", "d43a0a", "f06a10", "fba62a", "ffe070", "fff8c8")]
BONE = [hexc(c) for c in ("3b2414", "5e3b22", "86603c", "ac8a5e", "cdb488", "ebdcb4", "fff6e0")]
HORN = [hexc(c) for c in ("1e1410", "33241a", "4d3828", "6b5038", "8c6c4c", "b8986c", "e0cca0")]
STONE_TOP = [hexc(c) for c in ("1c1a18", "2c2826", "3c3632", "4c4540")]
STONE_L = hexc("26201c")
STONE_R = hexc("16120f")
EYE = [hexc(c) for c in ("2a0000", "8c0a00", "e02000", "ff7a10", "ffe060")]
PENTA = [hexc(c) for c in ("6a0a04", "c41a08", "ff4a14")]


def dither(t, x, y, levels):
    """Picks one of `levels` shades for intensity t in [0, 1] with 4x4 ordered dithering."""
    t = min(max(t, 0.0), 1.0) * (levels - 1)
    i = int(t)
    f = t - i
    if f > (BAYER[y & 3][x & 3] + 0.5) / 16 and i < levels - 1:
        i += 1
    return i


def rounded(x, y, r=7):
    cx = min(max(x + 0.5, r), N - r)
    cy = min(max(y + 0.5, r), N - r)
    return (x + 0.5 - cx) ** 2 + (y + 0.5 - cy) ** 2 <= r * r


img = [[None] * N for _ in range(N)]

# Background: a dark red glow, brightest low in the middle, with flames rising from the floor.
for y in range(N):
    for x in range(N):
        if not rounded(x, y):
            continue
        d = math.hypot((x - 31.5) / 34, (y - 40) / 40)
        img[y][x] = SKY[dither(1 - d, x, y, len(SKY))]
for x in range(N):
    h = 22 + 7 * math.sin(x * 0.55) + 5 * math.sin(x * 1.3 + 1.7) + 3 * math.sin(x * 2.9 + 0.4)
    h += 8 * math.exp(-((x - 31.5) / 14) ** 2)
    base = 52
    for y in range(N):
        if not rounded(x, y):
            continue
        k = (base - y) / h
        if 0 <= k <= 1:
            # Hotter near the floor and in the middle.
            t = (1 - k) * 0.85 + 0.25 * math.exp(-((x - 31.5) / 18) ** 2) - 0.1
            img[y][x] = FIRE[dither(t, x, y, len(FIRE))]

# Foreground layer: everything that gets a black outline.
fg = [[None] * N for _ in range(N)]

# Isometric floor tile, 2:1, with a 4-pixel thick slab under it.
TCX, TCY, THW, THH, TD = 31.5, 50.0, 28.0, 11.0, 4


def in_diamond(x, y, cy=TCY):
    return abs(x + 0.5 - TCX) / THW + abs(y + 0.5 - cy) / THH <= 1


for y in range(N):
    for x in range(N):
        if in_diamond(x, y):
            ny = (y + 0.5 - TCY) / THH
            nx = (x + 0.5 - TCX) / THW
            fg[y][x] = STONE_TOP[dither(0.75 - 0.35 * ny - 0.25 * nx, x, y, len(STONE_TOP))]
        elif any(in_diamond(x, y, TCY + d) for d in range(1, TD + 1)) and y + 0.5 > TCY:
            fg[y][x] = STONE_L if x + 0.5 < TCX else STONE_R

# Flagstone seams on the tile, along both iso axes.
for y in range(N):
    for x in range(N):
        if not in_diamond(x, y):
            continue
        u = (x + 0.5 - TCX) / THW + (y + 0.5 - TCY) / THH  # -1..1 along one edge
        v = (x + 0.5 - TCX) / THW - (y + 0.5 - TCY) / THH
        if min(abs((u * 2) % 1 - 0.5), abs((v * 2) % 1 - 0.5)) > 0.47:
            fg[y][x] = STONE_TOP[0]

# A glowing pentagram on the tile, squashed onto the iso plane.
def seg_dist(px, py, ax, ay, bx, by):
    dx, dy = bx - ax, by - ay
    t = max(0, min(1, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
    return math.hypot(px - ax - t * dx, py - ay - t * dy)


R = 0.8
pts = []
for i in range(5):
    a = -math.pi / 2 + i * 2 * math.pi / 5
    pts.append((TCX + R * THW * math.cos(a) * 0.8, TCY + R * THH * math.sin(a) * 1.0))
star = [(pts[i], pts[(i + 2) % 5]) for i in range(5)]
for y in range(N):
    for x in range(N):
        if not in_diamond(x, y):
            continue
        px, py = x + 0.5, y + 0.5
        d = min(seg_dist(px, py, ax, ay, bx, by) for (ax, ay), (bx, by) in star)
        ring = abs(math.hypot((px - TCX) / (THW * 0.8), (py - TCY) / THH) - R * 1.08)
        if d < 0.6 or ring < 0.045:
            fg[y][x] = PENTA[2]
        elif d < 1.3 or ring < 0.1:
            fg[y][x] = PENTA[dither(0.35, x, y, 2)]

# The skull's shadow on the tile.
for y in range(N):
    for x in range(N):
        if in_diamond(x, y) and ((x + 0.5 - TCX) / 9) ** 2 + ((y + 0.5 - 46.5) / 2.4) ** 2 <= 1:
            fg[y][x] = OUTLINE

skull = [[None] * N for _ in range(N)]

# Horns: thick tapering curves from the temples, out and up.
def bez(p, t):
    (x0, y0), (x1, y1), (x2, y2), (x3, y3) = p
    m = 1 - t
    return (m ** 3 * x0 + 3 * m * m * t * x1 + 3 * m * t * t * x2 + t ** 3 * x3,
            m ** 3 * y0 + 3 * m * m * t * y1 + 3 * m * t * t * y2 + t ** 3 * y3)


LEFT_HORN = [(21.5, 19.5), (9.0, 19.0), (4.0, 13.0), (8.5, 6.0)]
for side in (0, 1):
    ctrl = LEFT_HORN if side == 0 else [(63 - x, y) for x, y in LEFT_HORN]
    samples = [bez(ctrl, i / 200) for i in range(201)]
    for y in range(N):
        for x in range(N):
            px, py = x + 0.5, y + 0.5
            best = None
            for i, (sx, sy) in enumerate(samples):
                t = i / 200
                r = 4.2 * (1 - t) ** 0.85 + 0.35
                d = math.hypot(px - sx, py - sy)
                if d <= r and (best is None or d / r < best[0]):
                    best = (d / r, t, px - sx, py - sy, r)
            if best is None:
                continue
            k, t, dx, dy, r = best
            # Lit from the upper left; ridged rings along the horn; paler towards the tip.
            light = 0.5 + 0.45 * (-dx - dy) / (r * 1.4) + 0.35 * t
            if math.sin(t * 38) > 0.75 and t < 0.8:
                light -= 0.25
            skull[y][x] = HORN[dither(light, x, y, len(HORN))]

# Cranium and jaw.
SCX = 31.5


def in_skull(x, y):
    px, py = x + 0.5, y + 0.5
    if ((px - SCX) / 14.5) ** 2 + ((py - 23) / 13.5) ** 2 <= 1:
        return True
    # Cheekbones narrowing into the jaw.
    if 26 <= py <= 43:
        hw = 12.5 - max(0, py - 33) * 0.6
        return abs(px - SCX) <= hw
    return False


mask = [[in_skull(x, y) for x in range(N)] for y in range(N)]
for y in range(N):
    xs = [x for x in range(N) if mask[y][x]]
    for x in xs:
        col = [yy for yy in range(N) if mask[yy][x]]
        nx = (x + 0.5 - (xs[0] + xs[-1] + 1) / 2) / ((xs[-1] - xs[0] + 1) / 2)
        ny = (y + 0.5 - (col[0] + col[-1] + 1) / 2) / ((col[-1] - col[0] + 1) / 2)
        nz = math.sqrt(max(0.0, 1 - min(1.0, nx * nx * 0.8 + ny * ny * 0.5)))
        light = 0.15 + 0.85 * max(0.0, -0.45 * nx - 0.5 * ny + 0.75 * nz)
        # Red underlighting from the fire.
        if ny > 0.3:
            light += 0.15 * (ny - 0.3)
        skull[y][x] = BONE[dither(light, x, y, len(BONE))]

# Angry brow: a dark ridge slanting down to the middle.
for x in range(N):
    for side in (-1, 1):
        u = (x + 0.5 - SCX) * side
        if 2 <= u <= 12:
            yb = 22.5 + (12 - u) * 0.28
            y = int(yb)
            if mask[y][x]:
                skull[y][x] = BONE[1]
            if mask[y - 1][x] and u > 3:
                skull[y - 1][x] = BONE[5]


def in_socket(px, py, side):
    u = (px - SCX) * side  # distance from the middle line
    if not 2.5 <= u <= 11.5:
        return False
    top = 23.5 + (11.5 - u) * 0.28
    bottom = 31.0 - abs(u - 7) * 0.25
    return top <= py <= bottom


for y in range(N):
    for x in range(N):
        px, py = x + 0.5, y + 0.5
        for side in (-1, 1):
            if in_socket(px, py, side):
                ex, ey = SCX + side * 7.5, 28.0
                g = math.hypot(px - ex, py - ey)
                skull[y][x] = EYE[4] if g < 1.1 else EYE[3] if g < 2.0 else EYE[2] if g < 2.9 else EYE[1] if g < 3.8 else EYE[0]

# Nose: an inverted heart.
for y in range(N):
    for x in range(N):
        px, py = x + 0.5, y + 0.5
        u = abs(px - SCX)
        if 31 <= py <= 36.5 and u <= (py - 31) * 0.55 + 0.6 and not (u < 0.6 and py > 35.5):
            skull[y][x] = OUTLINE if py > 32.5 else BONE[0]

# Teeth: a dark mouth line with gaps, and a lower row.
for y in (38, 39, 40, 41):
    for x in range(N):
        u = x + 0.5 - SCX
        if not mask[y][x] or abs(u) > 8.5:
            continue
        if y in (38, 41):
            skull[y][x] = BONE[6] if (x % 2 == 0) else BONE[4]
            if abs(u) > 7.5:
                skull[y][x] = BONE[2]
        elif y == 39:
            skull[y][x] = OUTLINE if x % 2 else BONE[3]
        elif y == 40:
            skull[y][x] = OUTLINE
    # Sunken cheeks beside the jaw.
for y in range(33, 41):
    for side in (-1, 1):
        x = int(SCX + side * (10.5 - max(0, y - 33) * 0.6))
        if 0 <= x < N and skull[y][x] is not None:
            skull[y][x] = BONE[1]


def outline(layer):
    out = [[None] * N for _ in range(N)]
    for y in range(N):
        for x in range(N):
            if layer[y][x] is not None:
                continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                xx, yy = x + dx, y + dy
                if 0 <= xx < N and 0 <= yy < N and layer[yy][xx] is not None:
                    out[y][x] = OUTLINE
                    break
    return out


SKULL_DY = 3  # the skull floats above the tile
skull = [skull[y + SKULL_DY] if y + SKULL_DY < N else [None] * N for y in range(N)]

for layer in (fg, skull):
    ol = outline(layer)
    for y in range(N):
        for x in range(N):
            if not rounded(x, y):
                continue
            if layer[y][x] is not None:
                img[y][x] = layer[y][x]
            elif ol[y][x] is not None:
                img[y][x] = ol[y][x]


def write_png(path, size):
    s = size // N
    rows = []
    for y in range(size):
        row = bytearray([0])
        for x in range(size):
            c = img[y // s][x // s]
            row += bytes(c + (255,)) if c else b"\0\0\0\0"
        rows.append(bytes(row))
    raw = b"".join(rows)

    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)))
        f.write(chunk(b"IDAT", zlib.compress(raw, 9)))
        f.write(chunk(b"IEND", b""))


write_png(os.path.join(ROOT, "icon.png"), 1024)
