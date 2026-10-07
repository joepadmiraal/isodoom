#!/usr/bin/env python3
"""Vanilla finale reference (T7.5; dev tool). Usage:

    finales.py REF DOOM1.WAD OUTDIR ROUTE[@PRESSES]... [--images DIR TICS]

Plays each DOOM1.WAD route that ends an episode (an exit header on ExM8,
tests/IsoDoom.Tests/Sim/Routes) in the vanilla reference (REF, dump.c built
by build.sh) as a v1.9 demo (routes.py's), followed by the finale's
ticcmds: nothing but BT_USE pressed for one tic at each finale tic (1-based)
of PRESSES ("N,M,..."; default DEFAULT: presses, which skip nothing before
Doom II, through the text and on the end picture), until the end picture
has shown 35 tics. Writes dump.c's $DUMP_FI dump, OUTDIR/NAME.fi for the
default schedule or OUTDIR/NAME.fiN for the Nth schedule given after the
route (ROUTE@20,40,60). WAD-derived: never commit them. With --images, the
screens of finale tics TICS ("N,M,..." or "all") go to DIR/fiN.ppm
(dump.c's $DUMP_FI_DIR/$DUMP_FI_TICS). FinaleTests compares the game's
finale with the dumps tic by tic.
"""
import os, subprocess, sys, tempfile

sys.dont_write_bytecode = True  # no __pycache__ in the repo
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import routes  # noqa: E402

DEFAULT = '1,51,52,300,1000,1580'  # E1's text (440 characters) is typed out by finale tic 1330 and gives way to the end picture at 1571
LONGEST = 4 * 1000 * 3  # finale tics enough for any text (under 1000 characters) and the end picture


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
        if head['iwad'] != 'doom1' or head['exit'] is None or not (head['map'] or 'E1M1').upper().endswith('M8'):
            sys.exit(f'{route}: only DOOM1.WAD routes with an exit header on an episode\'s last map')
        schedules = [p for p in presses.split('@') if p] if presses else [DEFAULT]
        name = os.path.splitext(os.path.basename(route))[0]
        for n, schedule in enumerate(schedules):
            tics = [int(t) for t in schedule.split(',')]
            if any(t < 1 for t in tics):
                sys.exit(f'{spec}: finale tics start at 1')
            fin = [(0, 0, 0, 2 if t in tics else 0) for t in range(1, max(max(tics), LONGEST) + 1)]
            suffix = '' if not presses else str(n)
            out = os.path.join(outdir, f'{name}.fi{suffix}')
            tmp = tempfile.mkdtemp()
            try:
                open(os.path.join(tmp, 'route.lmp'), 'wb').write(routes.demo(head, cmds + fin))
                env = dict(os.environ, DUMP_TICS=os.path.join(tmp, 'route.vanilla'), DUMP_FI=out, DUMP_FI_PRESSES=schedule)
                for k in ('VIEWS', 'DUMP_START', 'DUMP_EVENTS', 'DUMP_FI_DIR', 'DUMP_FI_TICS', 'DUMP_WI'):
                    env.pop(k, None)
                if head['start']:
                    env['DUMP_START'] = head['start']
                if head['events']:
                    env['DUMP_EVENTS'] = ';'.join(head['events'])
                if images:
                    os.makedirs(images[0], exist_ok=True)
                    env['DUMP_FI_DIR'], env['DUMP_FI_TICS'] = images
                r = subprocess.run([ref, '-iwad', doom1, '-playdemo', 'route.lmp', '-nosound', '-nomusic', '-nogui'],
                                   cwd=tmp, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True)
            finally:  # (no shutil: the dev container's Python is minimal)
                for root, dirs, files in os.walk(tmp, topdown=False):
                    for f in files:
                        os.remove(os.path.join(root, f))
                    os.rmdir(root)
            lines = open(out).read().splitlines() if os.path.exists(out) else []
            ended = len(lines) > 2 and lines[-1].split()[0] == '1'
            if r.returncode != 0 or not ended:
                sys.exit(f'{spec}: vanilla exited with {r.returncode} after {max(len(lines) - 2, 0)} finale tics '
                         f'(the end picture must show)\n{r.stderr.strip()}')
            print(f'{route} @{schedule}: {len(lines) - 2} finale tics -> {out}')


if __name__ == '__main__':
    main()
