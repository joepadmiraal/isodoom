#!/usr/bin/env python3
"""Vanilla movement reference (T4.8; dev tool). Usage:

    routes.py REF DOOM1.WAD SYNTHETIC.WAD TESTMAPS OUTDIR ROUTE...

Plays each route (a .route file: a ticcmd sequence, format in
tests/IsoDoom.Tests/Sim/VanillaRoute.cs) in the vanilla reference (REF,
dump.c built by build.sh) as a v1.9 demo and writes the per-tic dump: NAME.vanilla
beside the route for a route on the synthetic IWAD or a test map (generated,
non-id content: committed), OUTDIR/NAME.vanilla for one on DOOM1.WAD
(WAD-derived: never committed). The synthetic map and the test maps
(TESTMAPS/NAME.wad, written by the tests' WritesTheTestMapPwads; T4.8a) play
as a -file over DOOM1.WAD, whose status bar and fonts the reference needs to
start; a test map's lump is E1M1. A route with the monsters header (T6.4)
plays without nomonsters, and its damage/alert (and, T6.5, rocket) lines go
to the reference as $DUMP_EVENTS (dump.c), run before their tics.
"""
import os, subprocess, sys, tempfile

SKILLS = range(1, 6)


def parse(path):
    """The route's header (iwad, map, skill, start) and its ticcmds (forwardmove, sidemove, turn, buttons)."""
    head = {'iwad': None, 'map': None, 'skill': 3, 'start': None, 'exit': None, 'monsters': False}
    cmds = []
    events = []  # T6.4: "TIC damage X Y AMOUNT" / "TIC alert" (/ T6.5 "TIC rocket"), TIC the 0-based tic they start
    for n, line in enumerate(open(path), 1):
        line = line.split('#', 1)[0].split()
        if not line:
            continue
        where = f'{path}:{n}'
        if line[0] == 'start':  # T5.6: X Y ANGLE, player 1's start instead of the map's
            if len(line) != 4:
                sys.exit(f'{where}: expected start X Y ANGLE')
            head['start'] = ' '.join(str(int(v)) for v in line[1:])
            continue
        if line[0] == 'exit':  # T5.9: normal|secret, the last tic leaves the level
            if len(line) != 2 or line[1] not in ('normal', 'secret'):
                sys.exit(f'{where}: expected exit normal|secret')
            head['exit'] = line[1]
            continue
        if line[0] == 'monsters':  # T6.4: the demo spawns monsters
            if len(line) != 1:
                sys.exit(f'{where}: expected monsters')
            head['monsters'] = True
            continue
        if line[0] == 'damage':  # T6.4: player 1 hurts a thing at the start of the next tic
            if len(line) != 4 or int(line[3]) < 1:
                sys.exit(f'{where}: expected damage X Y AMOUNT')
            events.append(f'{len(cmds)} damage {int(line[1])} {int(line[2])} {int(line[3])}')
            continue
        if line[0] == 'alert':  # T6.4: player 1 makes a noise at the start of the next tic
            if len(line) != 1:
                sys.exit(f'{where}: expected alert')
            events.append(f'{len(cmds)} alert')
            continue
        if line[0] == 'rocket':  # T6.5: player 1 fires a rocket at the start of the next tic
            if len(line) != 1:
                sys.exit(f'{where}: expected rocket')
            events.append(f'{len(cmds)} rocket')
            continue
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
    if head['iwad'] not in ('synthetic', 'doom1', 'testmap') or head['skill'] not in SKILLS:
        sys.exit(f'{path}: needs "iwad synthetic|doom1|testmap" and a skill of 1-5')
    if head['iwad'] == 'testmap' and not head['map']:
        sys.exit(f'{path}: "iwad testmap" needs "map NAME"')
    if events and int(events[-1].split()[0]) >= len(cmds):
        sys.exit(f'{path}: an event after the last tic')
    head['events'] = events
    return head, cmds


def demo(head, cmds):
    """A v1.9 demo lump: nomonsters unless the monsters header (T6.4), player 1 alone; angleturn is the turn byte << 8."""
    m = 'E1M1' if head['iwad'] == 'testmap' else (head['map'] or 'E1M1').upper()
    if len(m) != 4 or m[0] != 'E' or m[2] != 'M':
        sys.exit(f'{m}: only ExMy maps')
    data = bytearray([109, head['skill'] - 1, int(m[1]), int(m[3]), 0, 0, 0, 0 if head['monsters'] else 1, 0, 1, 0, 0, 0])
    for fwd, side, turn, buttons in cmds:
        data += bytes([fwd & 255, side & 255, turn & 255, buttons])
    return bytes(data + b'\x80')


def main():
    ref, doom1, synthetic, testmaps, outdir = sys.argv[1:6]
    outdir = os.path.abspath(outdir)
    os.makedirs(outdir, exist_ok=True)
    for route in sys.argv[6:]:
        head, cmds = parse(route)
        name = os.path.splitext(os.path.basename(route))[0]
        out = os.path.join(os.path.dirname(os.path.abspath(route)) if head['iwad'] != 'doom1' else outdir, name + '.vanilla')
        tmp = tempfile.mkdtemp()
        try:
            open(os.path.join(tmp, 'route.lmp'), 'wb').write(demo(head, cmds))
            args = [ref, '-iwad', doom1, '-playdemo', 'route.lmp', '-nosound', '-nomusic', '-nogui']
            if head['iwad'] == 'synthetic':
                args[3:3] = ['-file', synthetic]
            elif head['iwad'] == 'testmap':
                pwad = os.path.join(testmaps, head['map'] + '.wad')
                if not os.path.exists(pwad):
                    sys.exit(f'{route}: no test map {pwad} (RouteTestMaps.Maps)')
                args[3:3] = ['-file', os.path.abspath(pwad)]
            env = dict(os.environ, DUMP_TICS=out)
            env.pop('VIEWS', None)
            env.pop('DUMP_START', None)
            env.pop('DUMP_EVENTS', None)
            if head['start']:
                env['DUMP_START'] = head['start']
            if head['events']:
                env['DUMP_EVENTS'] = ';'.join(head['events'])
            r = subprocess.run(args, cwd=tmp, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True)
        finally:  # (no shutil: the dev container's Python is minimal)
            for root, dirs, files in os.walk(tmp, topdown=False):
                for f in files:
                    os.remove(os.path.join(root, f))
                os.rmdir(root)
        tics = sum(1 for _ in open(out)) if os.path.exists(out) else 0
        if r.returncode != 0 or tics != len(cmds):
            sys.exit(f'{route}: vanilla exited with {r.returncode} after {tics} of {len(cmds)} tics\n{r.stderr.strip()}')
        exits = [l.split()[-1] for l in open(out)]
        want = {None: '0', 'normal': '1', 'secret': '2'}[head['exit']]
        if exits[-1] != want or any(e != '0' for e in exits[:-1]):
            sys.exit(f'{route}: vanilla left the level at tic {next((i + 1 for i, e in enumerate(exits) if e != "0"), "-")} (exit {exits[-1]} at the last), the header says exit {head["exit"]}')
        print(f'{route}: {tics} tics -> {out}')


if __name__ == '__main__':
    main()
