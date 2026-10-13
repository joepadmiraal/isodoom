#!/usr/bin/env python3
"""Vanilla automap reference (T7.9; dev tool). Usage:

    automap.py REF DOOM1.WAD OUTDIR ROUTE TICS [--images DIR]

Plays a DOOM1.WAD route (tests/IsoDoom.Tests/Sim/Routes) in the vanilla
reference (REF, dump.c built by build.sh) as a v1.9 demo (routes.py's) with
the game drawn (-timedemo: D_Display renders the view after every tic, which
maps the lines it draws, ML_MAPPED) and writes dump.c's $DUMP_AM dump of the
level tics TICS ("N,M,...", leveltime) to OUTDIR/NAME.am, its first line
"tics TICS": at each, the player, the lines mapped and the automap's shots
(dump.c). With --images the shots also go to DIR as amTIC-NAME.ppm.
WAD-derived: never commit them. AmMapTests replays the same automap keys on
the sim's world with these mapped lines and compares the frames.
"""
import os, subprocess, sys, tempfile

sys.dont_write_bytecode = True  # no __pycache__ in the repo
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import routes  # noqa: E402


def main():
    args = sys.argv[1:]
    images = None
    if '--images' in args:
        i = args.index('--images')
        images = os.path.abspath(args[i + 1])
        del args[i:i + 2]
    ref, doom1, outdir, route, tics = args
    doom1 = os.path.abspath(doom1)
    outdir = os.path.abspath(outdir)
    os.makedirs(outdir, exist_ok=True)
    head, cmds = routes.parse(route)
    if head['iwad'] != 'doom1':
        sys.exit(f'{route}: only DOOM1.WAD routes (the reference draws the game)')
    name = os.path.splitext(os.path.basename(route))[0]
    out = os.path.join(outdir, f'{name}.am')
    tmp = tempfile.mkdtemp()
    try:
        open(os.path.join(tmp, 'route.lmp'), 'wb').write(routes.demo(head, cmds))
        env = dict(os.environ, DUMP_AM=out + '.tmp', DUMP_AM_TICS=tics)
        for k in ('VIEWS', 'DUMP_TICS', 'DUMP_START', 'DUMP_EVENTS', 'DUMP_WI', 'DUMP_FI', 'DUMP_WIPE', 'DUMP_MUSIC', 'DUMP_AM_DIR'):
            env.pop(k, None)
        if head['start']:
            env['DUMP_START'] = head['start']
        if head['events']:
            env['DUMP_EVENTS'] = ';'.join(head['events'])
        if images:
            os.makedirs(images, exist_ok=True)
            env['DUMP_AM_DIR'] = images
        # -timedemo: singletics, D_Display after every tic (as vanilla's at 35 frames a second or more)
        r = subprocess.run([ref, '-iwad', doom1, '-timedemo', 'route.lmp', '-nosound', '-nomusic', '-nogui'],
                           cwd=tmp, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True)
    finally:  # (no shutil: the dev container's Python is minimal)
        for root, dirs, files in os.walk(tmp, topdown=False):
            for f in files:
                os.remove(os.path.join(root, f))
            os.rmdir(root)
    if not os.path.exists(out + '.tmp'):
        sys.exit(f'{route}: vanilla exited with {r.returncode} and wrote no automap dump\n{r.stderr.strip()}')
    lines = open(out + '.tmp').read().splitlines()
    os.remove(out + '.tmp')
    want = len(tics.split(','))
    if len(lines) != want:
        sys.exit(f'{route}: {len(lines)} of {want} tics dumped (a tic past the route?)')
    with open(out, 'w') as f:
        f.write(f'tics {tics}\n')
        f.write('\n'.join(lines) + '\n')
    print(f'{route}: {len(lines)} tics -> {out}')


if __name__ == '__main__':
    main()
