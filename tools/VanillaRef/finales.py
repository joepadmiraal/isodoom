#!/usr/bin/env python3
"""Vanilla finale reference (T7.5; dev tool). Usage:

    finales.py REF DOOM1.WAD OUTDIR ROUTE[@PRESSES]... [--images DIR TICS]

Plays each DOOM1.WAD route that ends an episode (an exit header on ExM8,
tests/IsoDoom.Tests/Sim/Routes), or (T10.1) each Doom II route whose exit
leads to a text screen (MAP06, MAP11 and MAP20's exits, MAP15's and MAP31's
secret exits), in the vanilla reference (REF, dump.c built by build.sh) as
a v1.9 demo (routes.py's), followed for Doom II by its intermission skipped
(INTERMISSION: BT_USE at its tics 20 and 40, 50 tics, the dump's first line
"intermission 20,40 50"), then the finale's ticcmds: nothing but BT_USE pressed for one tic at each finale tic (1-based)
of PRESSES ("N,M,..."; default DEFAULT: presses, which skip nothing before
Doom II, through the text and on the end picture), until the end picture
has shown 35 tics, or for Doom II until the press that skips its text
(by default at finale tic 52, the first that may: NAME.fi; and READ, once
the longest text is typed out: NAME.firead). Writes dump.c's $DUMP_FI dump, OUTDIR/NAME.fi for the
default schedule or OUTDIR/NAME.fiN for the Nth schedule given after the
route (ROUTE@20,40,60). WAD-derived: never commit them. With --images, the
screens of finale tics TICS ("N,M,..." or "all") go to DIR/fiN.ppm
(dump.c's $DUMP_FI_DIR/$DUMP_FI_TICS). FinaleTests compares the game's
finale with the dumps tic by tic.

T10.5: MAP30's exit (a Doom II route) leads to the cast call: its schedules
are "PRESSES;KEYS;LENGTH" (CAST: NAME.fi, CAST_READ: NAME.firead): BT_USE at
the finale tics PRESSES (the first after tic 51 starts the cast call, a later
one starts it over), a key pressed after each finale tic of KEYS (dump.c's
$DUMP_FI_KEYS: F_Responder, which kills the member shown), LENGTH finale tics
in all; the dump's first line then reads "presses PRESSES keys KEYS".
"""
import os, subprocess, sys, tempfile

sys.dont_write_bytecode = True  # no __pycache__ in the repo
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import routes  # noqa: E402

DEFAULT = '1,51,52,300,1000,1580'  # E1's text (440 characters) is typed out by finale tic 1330 and gives way to the end picture at 1571
LONGEST = 4 * 1000 * 3  # finale tics enough for any text (under 1000 characters) and the end picture
READ = '2000'  # T10.1: Doom II's longest text (C2TEXT, 617 characters) is typed out by finale tic 1852
INTERMISSION = ([20, 40], 50)  # T10.1: Doom II's intermission before its text: the stats, then its end at tic 50


def cast_keys(start, count):
    """T10.5: the keys of the cast schedule: COUNT kills from finale tic START, 210-240 tics apart (a death lasts
    at most about 110 tics: the next member walks and attacks before its key), each fourth one followed by
    another 3 tics later, which the dying member eats."""
    keys, t = [], start
    for k in range(count):
        t += 210 + 15 * (k % 3)
        keys.append(t)
        if k % 4 == 1:
            keys.append(t + 3)
    return keys


# T10.5: MAP30's text to the cast call: skipped at finale tic 52, every member killed in turn past the hero
# back to the zombieman, then a press (a mouse click's or a pad's button: any ticcmd button) starts the cast
# over; and read to the end (2000), two kills, a press. "PRESSES;KEYS;LENGTH" (finale tics).
_KEYS = cast_keys(52, 18)
CAST = f'52,{_KEYS[-1] + 120};{",".join(map(str, _KEYS))};{_KEYS[-1] + 300}'
CAST_READ = '2000,2330;2100,2103,2250;2450'


def cast(head):
    """T10.5: whether a Doom II route's exit leads to the cast call (MAP30's text)."""
    return (head['map'] or 'MAP01').upper() == 'MAP30' and head['exit'] == 'normal'


def text_screen(head):
    """T10.1: whether a Doom II route's exit leads to a text screen (g_game.c G_WorldDone; MAP30's is the cast call's, T10.5)."""
    m = (head['map'] or 'MAP01').upper()
    return m in ('MAP06', 'MAP11', 'MAP20') and head['exit'] == 'normal' or m in ('MAP15', 'MAP31') and head['exit'] == 'secret'


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
        doom2 = head['iwad'] == 'doom2'
        if doom2 and (head['exit'] is None or not text_screen(head) and not cast(head)):
            sys.exit(f'{route}: only Doom II routes whose exit leads to a text screen or the cast call')
        if not doom2 and (head['iwad'] != 'doom1' or head['exit'] is None or not (head['map'] or 'E1M1').upper().endswith('M8')):
            sys.exit(f'{route}: only DOOM1.WAD routes with an exit header on an episode\'s last map')
        schedules = [p for p in presses.split('@') if p] if presses else [CAST, CAST_READ] if doom2 and cast(head) \
            else [DEFAULT, READ] if doom2 else [DEFAULT]
        wi = [(0, 0, 0, 2 if t in INTERMISSION[0] else 0) for t in range(1, INTERMISSION[1] + 1)] if doom2 else []
        name = os.path.splitext(os.path.basename(route))[0]
        for n, schedule in enumerate(schedules):
            parts = schedule.split(';')  # T10.5: "PRESSES[;KEYS[;LENGTH]]"
            tics = [int(t) for t in parts[0].split(',')]
            keys = parts[1] if len(parts) > 1 else ''
            length = int(parts[2]) if len(parts) > 2 else max(max(tics), LONGEST)
            if any(t < 1 for t in tics):
                sys.exit(f'{spec}: finale tics start at 1')
            fin = [(0, 0, 0, 2 if t in tics else 0) for t in range(1, length + 1)]
            suffix = str(n) if presses else '' if n == 0 else 'read'
            out = os.path.join(outdir, f'{name}.fi{suffix}')
            tmp = tempfile.mkdtemp()
            try:
                open(os.path.join(tmp, 'route.lmp'), 'wb').write(routes.demo(head, cmds + wi + fin))
                env = dict(os.environ, DUMP_TICS=os.path.join(tmp, 'route.vanilla'), DUMP_FI=out, DUMP_FI_PRESSES=parts[0], DUMP_FI_KEYS=keys)
                for k in ('VIEWS', 'DUMP_START', 'DUMP_EVENTS', 'DUMP_FI_DIR', 'DUMP_FI_TICS', 'DUMP_WI'):
                    env.pop(k, None)
                if head['start']:
                    env['DUMP_START'] = head['start']
                if head['events']:
                    env['DUMP_EVENTS'] = ';'.join(head['events'])
                if images:
                    os.makedirs(images[0], exist_ok=True)
                    env['DUMP_FI_DIR'], env['DUMP_FI_TICS'] = images
                r = subprocess.run([ref, '-iwad', routes.iwad(head, doom1), '-playdemo', 'route.lmp', '-nosound', '-nomusic', '-nogui'],
                                   cwd=tmp, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True)
            finally:  # (no shutil: the dev container's Python is minimal)
                for root, dirs, files in os.walk(tmp, topdown=False):
                    for f in files:
                        os.remove(os.path.join(root, f))
                    os.rmdir(root)
            lines = open(out).read().splitlines() if os.path.exists(out) else []
            if doom2 and cast(head):  # T10.5: the cast call goes on until the demo's end
                ended = len(lines) - 2 == length and lines[-1].split()[0] == '2'
                with open(out, 'w') as f:
                    f.write(f'intermission {",".join(map(str, INTERMISSION[0]))} {INTERMISSION[1]}\n' + ''.join(l + '\n' for l in lines))
            elif doom2:  # skipped by the first press after finale tic 51 (finalecount > 50), on that tic
                skip = min([t for t in tics if t > 51], default=0)
                ended = len(lines) - 2 == skip and lines[-1].split()[0] == '0'
                with open(out, 'w') as f:
                    f.write(f'intermission {",".join(map(str, INTERMISSION[0]))} {INTERMISSION[1]}\n' + ''.join(l + '\n' for l in lines))
            else:
                ended = len(lines) > 2 and lines[-1].split()[0] == '1'
            if r.returncode != 0 or not ended:
                sys.exit(f'{spec}: vanilla exited with {r.returncode} after {max(len(lines) - 2, 0)} finale tics '
                         f'(the end picture must show, or Doom II\'s text be skipped)\n{r.stderr.strip()}')
            print(f'{route} @{schedule[:60]}: {len(lines) - 2} finale tics -> {out}')


if __name__ == '__main__':
    main()
