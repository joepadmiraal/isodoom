#!/usr/bin/env python3
"""Vanilla intermission reference (T7.4; dev tool). Usage:

    intermissions.py REF DOOM1.WAD OUTDIR ROUTE[@PRESSES]... [--images DIR TICS]

Plays each DOOM1.WAD exit route (a .route file with an exit header,
tests/IsoDoom.Tests/Sim/Routes) in the vanilla reference (REF, dump.c built
by build.sh) as a v1.9 demo (routes.py's), followed by the intermission's
ticcmds: nothing but BT_USE pressed for one tic at each intermission tic
(bcnt, 1-based) of PRESSES ("N,M,..."), until the intermission ends. Writes
dump.c's $DUMP_WI dump, OUTDIR/NAME.wi for the default schedule
(DEFAULT: the stats counted out, then the next location shown until it ends
by itself) or OUTDIR/NAME.wiN for the Nth schedule given after the route
(ROUTE@20,40,60). WAD-derived: never commit them. With --images, the
screens of intermission tics TICS ("N,M,..." or "all") go to DIR/wiN.ppm
(dump.c's $DUMP_WI_DIR/$DUMP_WI_TICS). IntermissionTests compares the
game's intermission with the dumps tic by tic.
"""
import os, subprocess, sys, tempfile

sys.dont_write_bytecode = True  # no __pycache__ in the repo
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import routes  # noqa: E402

DEFAULT = '600'  # every E1 exit route's stats are counted out by then
SKIP = '20,40,60'  # skips the counting, then the next location


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
    for spec in args[3:]:
        route, _, presses = spec.partition('@')
        head, cmds = routes.parse(route)
        if head['iwad'] != 'doom1' or head['exit'] is None:
            sys.exit(f'{route}: only DOOM1.WAD routes with an exit header')
        schedules = [p for p in presses.split('@') if p] if presses else [DEFAULT, SKIP]
        name = os.path.splitext(os.path.basename(route))[0]
        for n, schedule in enumerate(schedules):
            tics = [int(t) for t in schedule.split(',')]
            if any(t < 1 for t in tics):
                sys.exit(f'{spec}: intermission tics start at 1')
            inter = [(0, 0, 0, 2 if t in tics else 0) for t in range(1, max(tics) + 4 * 35 + 20)]
            suffix = '' if not presses and n == 0 else str(n) if presses else 'skip'
            out = os.path.join(outdir, f'{name}.wi{suffix}')
            tmp = tempfile.mkdtemp()
            try:
                open(os.path.join(tmp, 'route.lmp'), 'wb').write(routes.demo(head, cmds + inter))
                env = dict(os.environ, DUMP_TICS=os.path.join(tmp, 'route.vanilla'), DUMP_WI=out, DUMP_WI_PRESSES=schedule)
                for k in ('VIEWS', 'DUMP_START', 'DUMP_EVENTS', 'DUMP_WI_DIR', 'DUMP_WI_TICS'):
                    env.pop(k, None)
                if head['start']:
                    env['DUMP_START'] = head['start']
                if head['events']:
                    env['DUMP_EVENTS'] = ';'.join(head['events'])
                if images:
                    os.makedirs(images[0], exist_ok=True)
                    env['DUMP_WI_DIR'], env['DUMP_WI_TICS'] = images
                r = subprocess.run([ref, '-iwad', doom1, '-playdemo', 'route.lmp', '-nosound', '-nomusic', '-nogui'],
                                   cwd=tmp, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True)
            finally:  # (no shutil: the dev container's Python is minimal)
                for root, dirs, files in os.walk(tmp, topdown=False):
                    for f in files:
                        os.remove(os.path.join(root, f))
                    os.rmdir(root)
            lines = open(out).read().splitlines() if os.path.exists(out) else []
            ended = len(lines) > 2 and int(lines[-1].split()[0]) == len(lines) - 2
            if r.returncode != 0 or not ended:
                sys.exit(f'{spec}: vanilla exited with {r.returncode} after {max(len(lines) - 2, 0)} intermission tics '
                         f'(the intermission must end within the schedule)\n{r.stderr.strip()}')
            print(f'{route} @{schedule}: {len(lines) - 2} intermission tics -> {out}')


if __name__ == '__main__':
    main()
