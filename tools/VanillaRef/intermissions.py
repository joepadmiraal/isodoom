#!/usr/bin/env python3
"""Vanilla intermission reference (T7.4; dev tool). Usage:

    intermissions.py REF DOOM1.WAD OUTDIR ROUTE[@PRESSES]... [--images DIR TICS]
        [--synthetic SYNTHETIC.wad] [--testmaps DIR] [--all-in-outdir]

Plays each DOOM1.WAD, (T10.1) Doom II, (T10.7) Final Doom or (M9) The Ultimate Doom exit route (a .route file with an exit header,
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
game's intermission with the dumps tic by tic. T11.7: a multi-player route
(players N) plays as a netdemo; its intermission's commands are player 1's,
the others idle (routes.demo), and the dump carries every player's stats and
the net game's or deathmatch's counters (dump.c, ref.patch's WI_dumpState).
T11.7a: a route's timer header is the reference's -timer (a deathmatch timer
route's exit), its gameversion header -gameversion, and a synthetic or test
map route (with --synthetic, the synthetic IWAD; --testmaps, the test map
PWADs' directory: routes.sh's) plays with it as -file over DOOM1.WAD, as
routes.py plays them; its dumps are not WAD-derived and go beside the route
(commit them), as its .vanilla does. With --all-in-outdir (wi.sh's, T11.8's
follow-up) every dump goes to OUTDIR, the synthetic routes' too (a look,
not the committed dumps).
"""
import os, subprocess, sys, tempfile

sys.dont_write_bytecode = True  # no __pycache__ in the repo
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import routes  # noqa: E402

DEFAULT = '600'  # every E1 and Doom II exit route's stats are counted out by then
SKIP = '20,40,60'  # skips the counting, then the next location


def main():
    args = sys.argv[1:]
    images = None
    wads = {}
    all_in_outdir = '--all-in-outdir' in args
    args = [a for a in args if a != '--all-in-outdir']
    for opt in ('--synthetic', '--testmaps'):
        if opt in args:
            i = args.index(opt)
            wads[opt] = os.path.abspath(args[i + 1])
            del args[i:i + 2]
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
        if head['exit'] is None:
            sys.exit(f'{route}: only routes with an exit header')
        extra = routes.pwad_args(head)  # T11.7a: the reference's arguments routes.py gives the route too (P2: a PWAD set's)
        if head['iwad'] == 'synthetic':
            if '--synthetic' not in wads:
                sys.exit(f'{route}: a synthetic route needs --synthetic SYNTHETIC.wad')
            extra += ['-file', wads['--synthetic']]
        elif head['iwad'] == 'testmap':
            pwad = os.path.join(wads.get('--testmaps', ''), head['map'] + '.wad')
            if not os.path.exists(pwad):
                sys.exit(f'{route}: no test map {pwad} (--testmaps DIR, RouteTestMaps.Maps)')
            extra += ['-file', pwad]
        if head['gameversion']:
            extra += ['-gameversion', head['gameversion']]
        if head['timer']:
            extra += ['-timer', str(head['timer'])]
        routedir = outdir if routes.id_wad(head) or all_in_outdir else os.path.dirname(os.path.abspath(route))
        schedules = [p for p in presses.split('@') if p] if presses else [DEFAULT, SKIP]
        name = os.path.splitext(os.path.basename(route))[0]
        for n, schedule in enumerate(schedules):
            tics = [int(t) for t in schedule.split(',')]
            if any(t < 1 for t in tics):
                sys.exit(f'{spec}: intermission tics start at 1')
            inter = [(0, 0, 0, 2 if t in tics else 0) for t in range(1, max(tics) + 4 * 35 + 20)]
            suffix = '' if not presses and n == 0 else str(n) if presses else 'skip'
            out = os.path.join(routedir, f'{name}.wi{suffix}')
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
                r = subprocess.run([ref, '-iwad', routes.iwad(head, doom1)] + extra + ['-playdemo', 'route.lmp', '-nosound', '-nomusic', '-nogui'],
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
