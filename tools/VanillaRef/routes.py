#!/usr/bin/env python3
"""Vanilla movement reference (T4.8; dev tool). Usage:

    routes.py REF DOOM1.WAD SYNTHETIC.WAD OUTDIR ROUTE...

Plays each route (a .route file: a ticcmd sequence, format in
tests/IsoDoom.Tests/Sim/VanillaRoute.cs) in the vanilla reference (REF,
dump.c built by build.sh) as a v1.9 demo and writes the per-tic dump: NAME.vanilla
beside the route for a route on the synthetic IWAD (generated, non-id
content: committed), OUTDIR/NAME.vanilla for one on DOOM1.WAD (WAD-derived:
never committed). The synthetic map plays as a -file over DOOM1.WAD, whose
status bar and fonts the reference needs to start.
"""
import os, subprocess, sys, tempfile

SKILLS = range(1, 6)


def parse(path):
    """The route's header (iwad, map, skill) and its ticcmds (forwardmove, sidemove, turn, buttons)."""
    head = {'iwad': None, 'map': 'E1M1', 'skill': 3}
    cmds = []
    for n, line in enumerate(open(path), 1):
        line = line.split('#', 1)[0].split()
        if not line:
            continue
        where = f'{path}:{n}'
        if line[0] in head:
            head[line[0]] = line[1] if line[0] != 'skill' else int(line[1])
            continue
        count = 1
        if line[-1].startswith('x'):
            count = int(line.pop()[1:])
        if len(line) != 4:
            sys.exit(f'{where}: expected FORWARD SIDE TURN BUTTONS [xCOUNT]')
        fwd, side, turn, buttons = map(int, line)
        if not (-128 <= fwd <= 127 and -128 <= side <= 127 and -128 <= turn <= 127 and 0 <= buttons <= 255):
            sys.exit(f'{where}: out of range')
        cmds += [(fwd, side, turn, buttons)] * count
    if head['iwad'] not in ('synthetic', 'doom1') or head['skill'] not in SKILLS:
        sys.exit(f'{path}: needs "iwad synthetic|doom1" and a skill of 1-5')
    return head, cmds


def demo(head, cmds):
    """A v1.9 demo lump: nomonsters, player 1 alone; angleturn is the turn byte << 8."""
    m = head['map'].upper()
    if len(m) != 4 or m[0] != 'E' or m[2] != 'M':
        sys.exit(f'{m}: only ExMy maps')
    data = bytearray([109, head['skill'] - 1, int(m[1]), int(m[3]), 0, 0, 0, 1, 0, 1, 0, 0, 0])
    for fwd, side, turn, buttons in cmds:
        data += bytes([fwd & 255, side & 255, turn & 255, buttons])
    return bytes(data + b'\x80')


def main():
    ref, doom1, synthetic, outdir = sys.argv[1:5]
    os.makedirs(outdir, exist_ok=True)
    for route in sys.argv[5:]:
        head, cmds = parse(route)
        name = os.path.splitext(os.path.basename(route))[0]
        out = os.path.join(os.path.dirname(route) if head['iwad'] == 'synthetic' else outdir, name + '.vanilla')
        tmp = tempfile.mkdtemp()
        try:
            open(os.path.join(tmp, 'route.lmp'), 'wb').write(demo(head, cmds))
            args = [ref, '-iwad', doom1, '-playdemo', 'route.lmp', '-nosound', '-nomusic', '-nogui']
            if head['iwad'] == 'synthetic':
                args[3:3] = ['-file', synthetic]
            env = dict(os.environ, DUMP_TICS=out)
            env.pop('VIEWS', None)
            r = subprocess.run(args, cwd=tmp, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True)
        finally:  # (no shutil: the dev container's Python is minimal)
            for root, dirs, files in os.walk(tmp, topdown=False):
                for f in files:
                    os.remove(os.path.join(root, f))
                os.rmdir(root)
        tics = sum(1 for _ in open(out)) if os.path.exists(out) else 0
        if r.returncode != 0 or tics != len(cmds):
            sys.exit(f'{route}: vanilla exited with {r.returncode} after {tics} of {len(cmds)} tics\n{r.stderr.strip()}')
        print(f'{route}: {tics} tics -> {out}')


if __name__ == '__main__':
    main()
