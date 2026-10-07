#!/usr/bin/env python3
"""Vanilla screen wipe reference (T7.1a; dev tool). Usage:

    wipes.py REF DOOM1.WAD OUTDIR ROUTE... [--images DIR STEPS]

Plays each DOOM1.WAD route (tests/IsoDoom.Tests/Sim/Routes) in the vanilla
reference (REF, dump.c built by build.sh) as a v1.9 demo (routes.py's) with
the game drawn (not -nodraw's nodrawers, so d_main.c's D_Display wipes),
followed by a tail of ticcmds: for a route that exits to an intermission,
BT_USE at intermission tics 20, 40 and 60 (the stats, the next location,
the end) and then the next level, TAIL tics in all; for one that ends an
episode (ExM8), the finale up to its end picture; for any other none.
Writes dump.c's $DUMP_WIPE dump to OUTDIR/NAME.wipe, its first line
"tail PRESSES LENGTH" (the tail's ticcmds: BT_USE at the 1-based tail tics
PRESSES, "-" for none, LENGTH tics). WAD-derived: never commit them.
With --images, the start and end screens of every wipe and the screens
after melt steps STEPS ("N,M,..." or "all") go to DIR (dump.c's
$DUMP_WIPE_DIR/$DUMP_WIPE_STEPS). WipeTests compares the game's wipes with
the dumps.
"""
import os, subprocess, sys, tempfile

sys.dont_write_bytecode = True  # no __pycache__ in the repo
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import routes  # noqa: E402

INTERMISSION = ([20, 40, 60], 200)  # skip the stats, the next location, then the next level's first tics
FINALE = ([], 1650)  # E1's text gives way to the end picture at finale tic 1571


def tail(head):
    if head['exit'] is None:
        return [], 0
    if (head['map'] or 'E1M1').upper().endswith('M8'):
        return FINALE
    return INTERMISSION


def main():
    args = sys.argv[1:]
    images = None
    if '--images' in args:
        i = args.index('--images')
        images = (os.path.abspath(args[i + 1]), args[i + 2])
        del args[i:i + 3]
    ref, doom1, outdir = args[:3]
    doom1 = os.path.abspath(doom1)
    outdir = os.path.abspath(outdir)
    os.makedirs(outdir, exist_ok=True)
    for route in args[3:]:
        head, cmds = routes.parse(route)
        if head['iwad'] != 'doom1':
            sys.exit(f'{route}: only DOOM1.WAD routes (the reference draws the game)')
        presses, length = tail(head)
        extra = [(0, 0, 0, 2 if t in presses else 0) for t in range(1, length + 1)]
        name = os.path.splitext(os.path.basename(route))[0]
        out = os.path.join(outdir, f'{name}.wipe')
        tmp = tempfile.mkdtemp()
        try:
            open(os.path.join(tmp, 'route.lmp'), 'wb').write(routes.demo(head, cmds + extra))
            env = dict(os.environ, DUMP_WIPE=out + '.tmp')
            for k in ('VIEWS', 'DUMP_TICS', 'DUMP_START', 'DUMP_EVENTS', 'DUMP_WI', 'DUMP_FI', 'DUMP_WIPE_DIR', 'DUMP_WIPE_STEPS'):
                env.pop(k, None)
            if head['start']:
                env['DUMP_START'] = head['start']
            if head['events']:
                env['DUMP_EVENTS'] = ';'.join(head['events'])
            if images:
                os.makedirs(images[0], exist_ok=True)
                env['DUMP_WIPE_DIR'], env['DUMP_WIPE_STEPS'] = images
            # -timedemo: singletics, D_Display after every tic (as vanilla's at 35 frames a second or more)
            r = subprocess.run([ref, '-iwad', doom1, '-timedemo', 'route.lmp', '-nosound', '-nomusic', '-nogui'],
                               cwd=tmp, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True)
        finally:  # (no shutil: the dev container's Python is minimal)
            for root, dirs, files in os.walk(tmp, topdown=False):
                for f in files:
                    os.remove(os.path.join(root, f))
                os.rmdir(root)
        if r.returncode != 0 or not os.path.exists(out + '.tmp'):
            sys.exit(f'{route}: vanilla exited with {r.returncode}\n{r.stderr.strip()}')
        lines = open(out + '.tmp').read().splitlines()
        os.remove(out + '.tmp')
        with open(out, 'w') as f:
            f.write(f'tail {",".join(map(str, presses)) or "-"} {length}\n')
            f.write('\n'.join(lines) + '\n')
        wipes = sum(1 for l in lines if l.startswith('wipe '))
        tics = sum(1 for l in lines if l.startswith('tic '))
        print(f'{route}: {tics} tics, {wipes} wipes -> {out}')


if __name__ == '__main__':
    main()
