#!/usr/bin/env python3
"""Compare two machines' netgame desync dumps (T11.11's follow-up).

usage: compare.py DIR_A DIR_B [--lines N]

Each DIR is one machine's dump directory (`user://netdesync/TIME-playerN/`,
or `--netdesync=DIR`'s), as the game writes it on a desync: `summary.txt`,
`checksums.txt` (tic, World.Checksum(), the four players' consistancy),
`commands.txt` (tic, players, every player's command) and the world after
the last agreed window (`agreed-windowW.dsg`), and the world the game stopped
with (`state.txt`, field by field: `World.DumpState`).

It lines the two logs up by tic and names the first tic whose checksum (or a
player's consistancy) differs, and the first whose commands differ (they
never should: the server sends every machine the same bundles). When both
directories also hold `replay.txt` (`dotnet run --project tools/NetDesync --
replay WAD DIR` on each, on the machine that wrote it), it names the first
tic where the replays differ and prints that tic's differing state lines
field by field (`World.DumpState`: the mobjs, sectors and players), and for
each machine the first tic where its replay differs from its own run (a
machine whose own run its replay does not reproduce did something the
commands do not say: a level script, or nondeterminism on that machine). The
stopped states (`state.txt`) are compared with each other and with each
machine's replay at that tic, naming the fields its run changed apart.

Exit code: 0 when nothing differs, 1 when something does, 2 on an error.
"""

import argparse
import os
import sys


def read_summary(d):
    path = os.path.join(d, "summary.txt")
    out = {}
    with open(path, encoding="utf-8") as f:
        for line in f:
            if ": " in line:
                key, value = line.strip().split(": ", 1)
                out[key] = value
    return out


def read_table(d, name):
    """{tic: [fields after the tic]} of checksums.txt or commands.txt, in tic order."""
    rows = {}
    with open(os.path.join(d, name), encoding="utf-8") as f:
        for line in f:
            line = line.rstrip("\n")
            if not line or line.startswith("#"):
                continue
            tic, _, rest = line.partition(" ")
            rows[int(tic)] = rest
    return rows


def read_replay(d):
    """{tic: (checksum, logged, [state lines])} of replay.txt, or None."""
    path = os.path.join(d, "replay.txt")
    if not os.path.exists(path):
        return None
    tics = {}
    cur = None
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.rstrip("\n")
            if not line or line.startswith("#"):
                continue
            if line.startswith("tic "):
                f_ = line.split()
                cur = int(f_[1])
                tics[cur] = (f_[3], f_[5], [])
            elif cur is not None:
                tics[cur][2].append(line)
    return tics


def read_state(d):
    """(tic, checksum, [state lines]) of state.txt (the world the game stopped with), or None."""
    path = os.path.join(d, "state.txt")
    if not os.path.exists(path):
        return None
    with open(path, encoding="utf-8") as f:
        lines = [x.rstrip("\n") for x in f if x.strip()]
    head = lines[0].split()
    return int(head[1]), head[3], lines[1:]


def first_difference(a, b):
    common = sorted(set(a) & set(b))
    for tic in common:
        if a[tic] != b[tic]:
            return tic, common
    return None, common


def fields(line):
    """('mobj 3', {name: value}) of a World.DumpState line."""
    parts = line.split()
    head = []
    kv = {}
    for p in parts:
        if "=" in p:
            k, v = p.split("=", 1)
            kv[k] = v
        elif not kv:
            head.append(p)
    return " ".join(head), kv


def state_diff(la, lb, limit):
    """The differing lines of two tics' state dumps, field by field."""
    a = dict(fields(x) for x in la)
    b = dict(fields(x) for x in lb)
    order = [h for h, _ in map(fields, la)] + [h for h, _ in map(fields, lb) if h not in a]
    out = []
    for head in order:
        fa, fb = a.get(head), b.get(head)
        if fa == fb:
            continue
        if fa is None or fb is None:
            out.append(f"  {head}: only in {'B' if fa is None else 'A'}")
        else:
            diffs = [f"{k} {fa.get(k)} vs {fb.get(k)}" for k in list(fa) + [k for k in fb if k not in fa] if fa.get(k) != fb.get(k)]
            kind = fa.get("type") or fb.get("type")
            out.append(f"  {head}{f' ({kind})' if kind else ''}: " + ", ".join(diffs))
        if len(out) >= limit:
            out.append("  ...")
            break
    return out


def main():
    ap = argparse.ArgumentParser(description="Compare two machines' netgame desync dumps.")
    ap.add_argument("a")
    ap.add_argument("b")
    ap.add_argument("--lines", type=int, default=20, help="most differing state lines shown (default 20)")
    args = ap.parse_args()
    try:
        sa, sb = read_summary(args.a), read_summary(args.b)
        ca, cb = read_table(args.a, "checksums.txt"), read_table(args.b, "checksums.txt")
        ma, mb = read_table(args.a, "commands.txt"), read_table(args.b, "commands.txt")
        ra, rb = read_replay(args.a), read_replay(args.b)
        ta, tb = read_state(args.a), read_state(args.b)
    except (OSError, ValueError, IndexError) as e:
        print(f"compare.py: {e}", file=sys.stderr)
        return 2

    differs = False
    print(f"A: {args.a}: player {sa.get('player', '?')}, build {sa.get('build', '?')}")
    print(f"B: {args.b}: player {sb.get('player', '?')}, build {sb.get('build', '?')}")
    for key in ("build", "game", "desync"):
        if sa.get(key) != sb.get(key):
            print(f"note: the {key} lines differ:\n  A: {sa.get(key)}\n  B: {sb.get(key)}")
    print(f"desync: {sa.get('desync', '?')}")

    tic, common = first_difference(ca, cb)
    if not common:
        print("checksums: no tic in both logs")
    elif tic is None:
        print(f"checksums: the same for tics {common[0]}-{common[-1]}")
    else:
        differs = True
        where = " (the first tic in both logs: earlier tics are not kept in both)" if tic == common[0] else ""
        print(f"checksums: first differing tic {tic}{where}")
        print(f"  A: {ca[tic]}\n  B: {cb[tic]}")
        prev = [t for t in common if t < tic]
        if prev:
            print(f"  the same up to tic {prev[-1]}")

    ctic, ccommon = first_difference(ma, mb)
    if ctic is not None:
        differs = True
        print(f"commands: first differing tic {ctic} (the bundles should be the same on every machine)")
        print(f"  A: {ma[ctic]}\n  B: {mb[ctic]}")
    elif ccommon:
        print(f"commands: the same for tics {ccommon[0]}-{ccommon[-1]}")

    for name, r in (("A", ra), ("B", rb)):
        if r is None:
            continue
        own = next((t for t in sorted(r) if r[t][1] != "none" and r[t][0] != r[t][1]), None)
        span = f"tics {min(r)}-{max(r)}" if r else "no tic"
        if own is None:
            print(f"replay {name}: {span}, reproduces its own run")
        else:
            differs = True
            print(f"replay {name}: {span}, differs from its own run from tic {own} (the run did something its commands do not say)")
    if ra is not None and rb is not None:
        rcommon = sorted(set(ra) & set(rb))
        rtic = next((t for t in rcommon if ra[t][0] != rb[t][0]), None)
        if not rcommon:
            print("replays: no tic in both")
        elif rtic is None:
            print(f"replays: the same for tics {rcommon[0]}-{rcommon[-1]}")
        else:
            differs = True
            print(f"replays: first differing tic {rtic} (A {ra[rtic][0]}, B {rb[rtic][0]}); its state lines that differ:")
            for line in state_diff(ra[rtic][2], rb[rtic][2], args.lines):
                print(line)
    if ta is not None and tb is not None:
        if ta[0] != tb[0]:
            print(f"stopped states: A after tic {ta[0]}, B after tic {tb[0]}: not compared")
        elif ta[1] == tb[1]:
            print(f"stopped states (tic {ta[0]}): the same")
        else:
            differs = True
            print(f"stopped states (tic {ta[0]}): the lines that differ:")
            for line in state_diff(ta[2], tb[2], args.lines):
                print(line)
    for name, t, r in (("A", ta, ra), ("B", tb, rb)):
        if t is None or r is None or t[0] not in r or r[t[0]][0] == t[1]:
            continue
        differs = True
        print(f"{name}'s run against its replay at tic {t[0]} (where its run left its commands):")
        for line in state_diff(t[2], r[t[0]][2], args.lines):
            print(line)
    if ra is None and rb is None:
        print("(no replay.txt: run `dotnet run --project tools/NetDesync -- replay WAD DIR` on each to compare their states field by field)")
    return 1 if differs else 0


if __name__ == "__main__":
    sys.exit(main())
