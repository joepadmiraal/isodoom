#!/usr/bin/env python3
"""The PWAD sets of docs/PWAD-VALIDATION.md for the dev tools (P2; dev tool). Usage:

    pwads.py SET          the set's IWAD, then each of its -file PWADs in load order, a path a line
    pwads.py --iwad SET   the set's IWAD kind: doom2 or doom (a route's iwad header)
    pwads.py --list       the sets' folders

The table is tests/IsoDoom.Tests/Support/PwadSets.cs's (P1; PwadToolTableTests
checks that the two agree). A set's files are in $ISODOOM_PWADS/SET/ or, by
default, wads/pwads/SET/; Doom II's IWAD is $ISODOOM_DOOM2_WAD or
wads/doom2.wad (DOOM2.WAD), The Ultimate Doom's $ISODOOM_DOOM_WAD or
wads/DOOM.WAD (doom.wad), as the tests find them. Exits 1, naming what is
missing, when the set or its IWAD is absent. Self-contained (no imports from
this directory), so the scripts run it with python3 -I; routes.py imports it.
"""
import os
import sys

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# folder: (IWAD kind, -file list in load order)
SETS = {
    'scythe': ('doom2', ['SCYTHE.WAD']),
    'av': ('doom2', ['AV.WAD', 'AVMOVFIX.WAD']),
    'mm': ('doom2', ['MM.WAD', 'MMMUS.WAD']),
    'd2twid': ('doom2', ['D2TWID.wad']),
    'requiem': ('doom2', ['REQUIEM.WAD', 'REQMUS.WAD']),
    'hr': ('doom2', ['HR.WAD', 'HRMUS.WAD']),
    'dtwid': ('doom', ['DTWID.wad']),
    'neis': ('doom', ['NEIS.wad']),
}


def kind(name):
    """The set's IWAD kind (doom2 or doom); exits on an unknown set."""
    if name not in SETS:
        sys.exit(f'unknown PWAD set {name!r}: one of {", ".join(SETS)}')
    return SETS[name][0]


def iwad_path(which):
    """The IWAD of kind doom2 or doom: its environment variable, else wads/ (as the tests' TestWads)."""
    env, names = {'doom2': ('ISODOOM_DOOM2_WAD', ('doom2.wad', 'DOOM2.WAD')),
                  'doom': ('ISODOOM_DOOM_WAD', ('DOOM.WAD', 'doom.wad'))}[which]
    if os.environ.get(env):
        return os.path.abspath(os.environ[env])
    for n in names:
        path = os.path.join(REPO, 'wads', n)
        if os.path.exists(path):
            return path
    sys.exit(f'No {names[0]}: put it in wads/ or set {env}')


def files(name):
    """The set's PWADs, absolute, in load order; exits when one is missing."""
    kind(name)
    root = os.path.abspath(os.environ['ISODOOM_PWADS']) if os.environ.get('ISODOOM_PWADS') else os.path.join(REPO, 'wads', 'pwads')
    paths = [os.path.join(root, name, f) for f in SETS[name][1]]
    missing = [p for p in paths if not os.path.exists(p)]
    if missing:
        sys.exit(f'PWAD set {name}: missing {", ".join(missing)} (wads/pwads/{name}/ or $ISODOOM_PWADS/{name}/)')
    return paths


def main():
    args = sys.argv[1:]
    if args == ['--list']:
        print('\n'.join(SETS))
    elif len(args) == 2 and args[0] == '--iwad':
        print(kind(args[1]))
    elif len(args) == 1 and not args[0].startswith('-'):
        wads = files(args[0])
        print(iwad_path(kind(args[0])))
        print('\n'.join(wads))
    else:
        sys.exit(__doc__)


if __name__ == '__main__':
    main()
