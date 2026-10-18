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
start; a test map's lump is E1M1. A Doom II route (iwad doom2, T10.1) plays
on Doom II's IWAD (doom2_wad(): $ISODOOM_DOOM2_WAD, else wads/doom2.wad or
wads/DOOM2.WAD) and its dump goes to OUTDIR too; T10.7: likewise a Final Doom route
(iwad tnt|plutonia: $ISODOOM_TNT_WAD / $ISODOOM_PLUTONIA_WAD, else wads/), and a
gameversion header (1.9, ultimate, final, ...) is the reference's -gameversion
(Final Doom's IWADs default to final, as Chocolate Doom). M9: an iwad doom route
plays on The Ultimate Doom's IWAD (doom_wad(): $ISODOOM_DOOM_WAD, else
wads/doom.wad or wads/DOOM.WAD; ExMy of E1-E4, as ultimate by default) and its dump goes to OUTDIR. A route with the monsters header (T6.4)
plays without nomonsters, and its damage/alert (and, T6.5, rocket) lines go
to the reference as $DUMP_EVENTS (dump.c), run before their tics.
T11.2: a route with "players N" (2-4) plays as a v1.9 netdemo (players 1 to N
in the game: G_DoPlayDemo makes it a netgame) with its "deathmatch 0|1|2"
byte, each tic's line holding one ticcmd per player separated by "|"; an
event's last "pN" names its player (dump.c's DUMP_EVENTS "TIC KIND ... pN").
Its dump has a column per player in the game after the exit column (dump.c's
dump_player). T11.4: a "timer MINUTES" header is the reference's -timer.
P2: a "pwad SET" header loads a PWAD set of docs/PWAD-VALIDATION.md over the
route's IWAD (iwad doom2 or doom, the set's: pwads.py) as -file, its dump in
OUTDIR (WAD-derived), with a 32 MiB zone (-mb 32; the reference's 6 MiB runs out on AV's MAP20).
"""
import os, subprocess, sys, tempfile

import pwads

SKILLS = range(1, 6)
GAMEVERSIONS = ('1.666', '1.7', '1.8', '1.9', 'ultimate', 'final', 'final2')  # T10.7: the sim's (GameVersions.gameversions)
REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def doom2_wad():
    """T10.1: Doom II's IWAD: $ISODOOM_DOOM2_WAD, else wads/doom2.wad or wads/DOOM2.WAD (as the tests' TestWads)."""
    env = os.environ.get('ISODOOM_DOOM2_WAD')
    if env:
        return os.path.abspath(env)
    for name in ('doom2.wad', 'DOOM2.WAD'):
        path = os.path.join(REPO, 'wads', name)
        if os.path.exists(path):
            return path
    sys.exit('No Doom II IWAD: put doom2.wad in wads/ or set ISODOOM_DOOM2_WAD')


def final_wad(which):
    """T10.7: Final Doom's IWAD (tnt or plutonia): $ISODOOM_TNT_WAD / $ISODOOM_PLUTONIA_WAD, else wads/ (as the tests' TestWads)."""
    env = os.environ.get('ISODOOM_TNT_WAD' if which == 'tnt' else 'ISODOOM_PLUTONIA_WAD')
    if env:
        return os.path.abspath(env)
    for name in (which + '.wad', which.upper() + '.WAD'):
        path = os.path.join(REPO, 'wads', name)
        if os.path.exists(path):
            return path
    sys.exit(f'No {which.upper()}.WAD: put it in wads/ or set ISODOOM_{which.upper()}_WAD')


def doom_wad():
    """M9: The Ultimate Doom's IWAD: $ISODOOM_DOOM_WAD, else wads/doom.wad or wads/DOOM.WAD (as the tests' TestWads)."""
    env = os.environ.get('ISODOOM_DOOM_WAD')
    if env:
        return os.path.abspath(env)
    for name in ('DOOM.WAD', 'doom.wad'):
        path = os.path.join(REPO, 'wads', name)
        if os.path.exists(path):
            return path
    sys.exit('No DOOM.WAD: put The Ultimate Doom\'s doom.wad in wads/ or set ISODOOM_DOOM_WAD')


COMMERCIAL = ('doom2', 'tnt', 'plutonia')
EXIT = 32  # the dump's exit column (VanillaRoute.Columns)


def iwad(head, doom1):
    """The IWAD vanilla plays a route on: Doom II's for iwad doom2, Final Doom's for tnt/plutonia (T10.7), else DOOM1.WAD (the others go over it as -file)."""
    if head['iwad'] == 'doom2':
        return doom2_wad()
    if head['iwad'] in ('tnt', 'plutonia'):
        return final_wad(head['iwad'])
    if head['iwad'] == 'doom':  # M9
        return doom_wad()
    return doom1


def pwad_args(head):
    """P2: the reference's arguments for a route's pwad header: a 32 MiB zone (-mb 32; its 6 MiB default runs
    out on AV's MAP20) and the set's -file list (intermissions.py, finales.py and wipes.py add them too)."""
    return ['-mb', '32', '-file'] + pwads.files(head['pwad']) if head['pwad'] else []


def id_wad(head):
    """Whether the route's dumps are WAD-derived (DOOM1.WAD's or Doom II's: never committed)."""
    return head['iwad'] in ('doom1', 'doom') + COMMERCIAL


def parse(path):
    """The route's header (iwad, map, skill, start) and its ticcmds (forwardmove, sidemove, turn, buttons)."""
    head = {'iwad': None, 'map': None, 'skill': 3, 'start': None, 'exit': None, 'monsters': False, 'gameversion': None,
            'players': 1, 'deathmatch': 0, 'timer': 0, 'pwad': None}
    cmds = []  # a (FORWARD, SIDE, TURN, BUTTONS) a tic; T11.2: with players N > 1, a tuple of N of them a tic
    events = []  # T6.4: "TIC damage X Y AMOUNT" / "TIC alert" (/ T6.5 "TIC rocket"), TIC the 0-based tic they start
    for n, line in enumerate(open(path), 1):
        line = line.split('#', 1)[0].split()
        if not line:
            continue
        where = f'{path}:{n}'
        if line[0] == 'players':  # T11.2: players 1 to N in the game (a netgame from 2)
            if len(line) != 2 or not line[1].isdigit() or not 1 <= int(line[1]) <= 4 or cmds or events:
                sys.exit(f'{where}: expected players 1-4, before the ticcmds and events')
            head['players'] = int(line[1])
            continue
        if line[0] == 'deathmatch':  # T11.2: 0 co-op, 1 deathmatch, 2 altdeath
            if len(line) != 2 or line[1] not in ('0', '1', '2'):
                sys.exit(f'{where}: expected deathmatch 0|1|2')
            head['deathmatch'] = int(line[1])
            continue
        if line[0] == 'pwad':  # P2: a PWAD set (pwads.SETS) as -file over the route's IWAD
            if len(line) != 2 or line[1] not in pwads.SETS:
                sys.exit(f'{where}: expected pwad {"|".join(pwads.SETS)}')
            head['pwad'] = line[1]
            continue
        if line[0] == 'timer':  # T11.4: vanilla's -timer MINUTES (the level ends after them in deathmatch)
            if len(line) != 2 or not line[1].isdigit() or not 1 <= int(line[1]) <= 65535:
                sys.exit(f'{where}: expected timer MINUTES')
            head['timer'] = int(line[1])
            continue
        player = 0  # T11.2: an event's player, a last pN (default p1), dump.c's "TIC pN ..."
        if line[0] in ('damage', 'alert', 'rocket') and len(line) > 1 and line[-1].startswith('p'):
            player = int(line.pop()[1:]) - 1
            if not 0 <= player < head['players']:
                sys.exit(f'{where}: the event\'s player p1-p{head["players"]}')
        ptag = f' p{player + 1}' if player else ''  # dump.c's DUMP_EVENTS: "TIC KIND ... [pN]"
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
        if line[0] == 'gameversion':  # T10.7: the reference's -gameversion
            if len(line) != 2 or line[1] not in GAMEVERSIONS:
                sys.exit(f'{where}: expected gameversion {"|".join(GAMEVERSIONS)}')
            head['gameversion'] = line[1]
            continue
        if line[0] == 'monsters':  # T6.4: the demo spawns monsters
            if len(line) != 1:
                sys.exit(f'{where}: expected monsters')
            head['monsters'] = True
            continue
        if line[0] == 'damage':  # T6.4: player 1 hurts a thing at the start of the next tic
            if len(line) != 4 or int(line[3]) < 1:
                sys.exit(f'{where}: expected damage X Y AMOUNT')
            events.append(f'{len(cmds)} damage {int(line[1])} {int(line[2])} {int(line[3])}{ptag}')
            continue
        if line[0] == 'alert':  # T6.4: player 1 makes a noise at the start of the next tic
            if len(line) != 1:
                sys.exit(f'{where}: expected alert')
            events.append(f'{len(cmds)} alert{ptag}')
            continue
        if line[0] == 'rocket':  # T6.5: player 1 fires a rocket at the start of the next tic
            if len(line) != 1:
                sys.exit(f'{where}: expected rocket')
            events.append(f'{len(cmds)} rocket{ptag}')
            continue
        if line[0] in head:
            head[line[0]] = line[1] if line[0] != 'skill' else int(line[1])
            continue
        count = 1
        if line[-1].startswith('x'):
            count = int(line.pop()[1:])
        players = head['players']
        if len(line) != 5 * players - 1 or any(line[5 * k - 1] != '|' for k in range(1, players)):
            sys.exit(f'{where}: expected FORWARD SIDE TURN BUTTONS [xCOUNT]' if players == 1
                     else f'{where}: expected {players} ticcmds FORWARD SIDE TURN BUTTONS separated by | [xCOUNT]')
        tic = []
        for k in range(players):
            fwd, side, turn, buttons = map(int, line[5 * k:5 * k + 4])
            if not (-128 <= fwd <= 127 and -128 <= side <= 127 and -128 <= turn <= 127 and 0 <= buttons <= 255):
                sys.exit(f'{where}: out of range')
            tic.append((fwd, side, turn, buttons))
        cmds += [tic[0] if players == 1 else tuple(tic)] * count
    if head['iwad'] not in ('synthetic', 'doom1', 'doom', 'testmap') + COMMERCIAL or head['skill'] not in SKILLS:
        sys.exit(f'{path}: needs "iwad synthetic|doom1|doom|doom2|tnt|plutonia|testmap" and a skill of 1-5')
    if head['pwad'] and pwads.kind(head['pwad']) != head['iwad']:
        sys.exit(f'{path}: "pwad {head["pwad"]}" plays over "iwad {pwads.kind(head["pwad"])}"')
    if head['iwad'] == 'testmap' and not head['map']:
        sys.exit(f'{path}: "iwad testmap" needs "map NAME"')
    if events and int(events[-1].split()[0]) >= len(cmds):
        sys.exit(f'{path}: an event after the last tic')
    head['events'] = events
    return head, cmds


def demo(head, cmds):
    """A v1.9 demo lump: nomonsters unless the monsters header (T6.4), player 1 alone; angleturn is the turn byte << 8.
    Doom II's (T10.1): MAPxx, episode 1. T11.2: players 1 to N in the game (a netdemo from 2: G_DoPlayDemo
    makes it a netgame when playeringame[1] is set) and the deathmatch byte, each tic's N ticcmds in player
    order; a tic of one ticcmd (a tail the other scripts append) is player 1's, the others idle."""
    if head['iwad'] in COMMERCIAL:
        m = (head['map'] or 'MAP01').upper()
        if len(m) != 5 or not m.startswith('MAP') or not m[3:].isdigit():
            sys.exit(f'{m}: Doom II routes play MAPxx maps')
        episode, mapnum = 1, int(m[3:])
    else:
        m = 'E1M1' if head['iwad'] == 'testmap' else (head['map'] or 'E1M1').upper()
        if len(m) != 4 or m[0] != 'E' or m[2] != 'M' or m[1] not in ('1234' if head['iwad'] == 'doom' else '123') or m[3] not in '123456789':
            sys.exit(f'{m}: only ExMy maps (E4 with iwad doom, M9)')
        episode, mapnum = int(m[1]), int(m[3])
    players = head.get('players', 1)
    data = bytearray([109, head['skill'] - 1, episode, mapnum, head.get('deathmatch', 0), 0, 0, 0 if head['monsters'] else 1, 0]
                     + [1 if i < players else 0 for i in range(4)])
    for tic in cmds:
        if isinstance(tic[0], int):
            tic = [tic] + [(0, 0, 0, 0)] * (players - 1)
        for fwd, side, turn, buttons in tic:
            data += bytes([fwd & 255, side & 255, turn & 255, buttons])
    return bytes(data + b'\x80')


def main():
    ref, doom1, synthetic, testmaps, outdir = sys.argv[1:6]
    outdir = os.path.abspath(outdir)
    os.makedirs(outdir, exist_ok=True)
    for route in sys.argv[6:]:
        head, cmds = parse(route)
        name = os.path.splitext(os.path.basename(route))[0]
        out = os.path.join(os.path.dirname(os.path.abspath(route)) if not id_wad(head) else outdir, name + '.vanilla')
        tmp = tempfile.mkdtemp()
        try:
            open(os.path.join(tmp, 'route.lmp'), 'wb').write(demo(head, cmds))
            args = [ref, '-iwad', iwad(head, doom1), '-playdemo', 'route.lmp', '-nosound', '-nomusic', '-nogui']
            if head['gameversion']:  # T10.7
                args += ['-gameversion', head['gameversion']]
            if head['timer']:  # T11.4
                args += ['-timer', str(head['timer'])]
            if head['pwad']:  # P2
                args[3:3] = pwad_args(head)
            elif head['iwad'] == 'synthetic':
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
        exits = [l.split()[EXIT] for l in open(out)]  # T11.2: the players' columns of a multi-player game follow it
        want = {None: '0', 'normal': '1', 'secret': '2'}[head['exit']]
        if exits[-1] != want or any(e != '0' for e in exits[:-1]):
            sys.exit(f'{route}: vanilla left the level at tic {next((i + 1 for i, e in enumerate(exits) if e != "0"), "-")} (exit {exits[-1]} at the last), the header says exit {head["exit"]}')
        print(f'{route}: {tics} tics -> {out}')


if __name__ == '__main__':
    main()
