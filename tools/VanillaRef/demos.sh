#!/bin/bash
# The vanilla demo reference (T6.13; dev tool). Usage:
#   tools/VanillaRef/demos.sh [--doom|--doom2|--tnt|--plutonia|--pwad SET] [--gameversion V] [DEMO1 DEMO2 DEMO3]
# Plays each demo lump of DOOM1.WAD (wads/ or $ISODOOM_DOOM1_WAD; default
# all three) in the vanilla reference (build.sh; $VANILLA_REF or
# ~/.cache/isodoom/vanilla-ref) with -playdemo and writes its per-tic dump
# (dump.c, the routes' format: tools/VanillaRef/routes.sh) as demoN.vanilla
# to $ISODOOM_VANILLA_ROUTES (default ~/.cache/isodoom/vanilla-routes).
# T10.6: with --doom2, Doom II's IWAD's (wads/doom2.wad, wads/DOOM2.WAD or
# $ISODOOM_DOOM2_WAD) as doom2-demoN.vanilla, the reference's -gameversion
# picked by the demo's version byte (v1.666's IWAD has v1.666's demos).
# T10.7: with --tnt or --plutonia, Final Doom's IWAD (wads/tnt.wad or
# wads/TNT.WAD, $ISODOOM_TNT_WAD; likewise plutonia, $ISODOOM_PLUTONIA_WAD)
# as tnt-demoN.vanilla / plutonia-demoN.vanilla, played as Final Doom's
# executable (-gameversion final, Chocolate Doom's default for them).
# --gameversion V plays them as that executable instead (1.9, ultimate,
# final, final2), the dumps named PREFIX-V-demoN.vanilla (T10.7:
# --doom2 --gameversion final, DemoSyncTests' doom2-final-demoN).
# M9: with --doom, The Ultimate Doom's IWAD (wads/DOOM.WAD or wads/doom.wad,
# $ISODOOM_DOOM_WAD) as doom-demoN.vanilla (default all four, DEMO4 E4's),
# played as ultimate (Chocolate Doom's default for retail).
# P2: with --pwad SET, a PWAD set of docs/PWAD-VALIDATION.md (pwads.py: its
# IWAD and its -file list from wads/pwads/SET or $ISODOOM_PWADS/SET) as
# SET-demoN.vanilla, by default every DEMOn lump of the set's PWADs (HR's
# DEMO5 and DEMO6 too, outside the attract loop). A Doom II set's demo plays
# as its version byte says (every set's are v1.9: -gameversion 1.9 on the
# v1.666 IWAD), an Ultimate Doom set's as ultimate. The reference gets a
# 32 MiB zone for them (-mb 32: its 6 MiB default runs out on AV's MAP20).
# WAD-derived: never commit them. DemoSyncTests compares the sim with them.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
ref=${VANILLA_REF:-$HOME/.cache/isodoom/vanilla-ref}/doomgeneric/doomgeneric
refdir=$(dirname "$(dirname "$ref")")
if [ ! -x "$ref" ] || [ "$(cat "$here/ref.patch" "$here/dump.c" | sha256sum)" != "$(cat "$refdir/ref.stamp" 2>/dev/null)" ]; then
    ref=$("$here/build.sh" "$refdir")
fi
iwad=$(realpath "${ISODOOM_DOOM1_WAD:-$repo/wads/DOOM1.WAD}")
prefix=
pwads=()
if [ "${1:-}" = --doom2 ]; then
    shift
    iwad=${ISODOOM_DOOM2_WAD:-$repo/wads/doom2.wad}
    [ -f "$iwad" ] || iwad=$repo/wads/DOOM2.WAD
    iwad=$(realpath "$iwad")
    prefix=doom2-
elif [ "${1:-}" = --doom ]; then
    shift
    iwad=${ISODOOM_DOOM_WAD:-$repo/wads/DOOM.WAD}
    [ -f "$iwad" ] || iwad=$repo/wads/doom.wad
    [ -f "$iwad" ] || { echo "No DOOM.WAD: put it in wads/ or set ISODOOM_DOOM_WAD" >&2; exit 1; }
    iwad=$(realpath "$iwad")
    prefix=doom-
    ultimate=1
    demo4=1
elif [ "${1:-}" = --pwad ]; then
    set=$2
    shift 2
    wads=$(python3 -I "$here/pwads.py" "$set")
    mapfile -t pwads <<< "$wads"
    iwad=${pwads[0]}
    pwads=("${pwads[@]:1}")
    prefix=$set-
    if [ "$(python3 -I "$here/pwads.py" --iwad "$set")" = doom ]; then ultimate=1; fi
elif [ "${1:-}" = --tnt ] || [ "${1:-}" = --plutonia ]; then
    game=${1#--}
    shift
    upper=$(echo "$game" | tr '[:lower:]' '[:upper:]')
    var=ISODOOM_${upper}_WAD
    iwad=${!var:-$repo/wads/$game.wad}
    [ -f "$iwad" ] || iwad=$repo/wads/$upper.WAD
    [ -f "$iwad" ] || { echo "No $upper.WAD: put it in wads/ or set $var" >&2; exit 1; }
    iwad=$(realpath "$iwad")
    prefix=$game-
    final=1
fi
forced=
if [ "${1:-}" = --gameversion ]; then
    forced=$2
    shift 2
    prefix=$prefix$forced-
fi
out=$(realpath -m "${ISODOOM_VANILLA_ROUTES:-$HOME/.cache/isodoom/vanilla-routes}")
mkdir -p "$out"
if [ $# -eq 0 ] && [ ${#pwads[@]} -gt 0 ]; then
    # P2: the set's own demo lumps (DEMO1, DEMO2, ...), in name order.
    demos=$(python3 -I -B -c '
import re, sys
sys.path.insert(0, sys.argv[1])
from mergewad import lumps, lump_name
names = set()
for path in sys.argv[2:]:
    for raw, _ in lumps(path):
        name = lump_name(raw)
        if re.fullmatch(r"DEMO[0-9]+", name):
            names.add(name)
print(" ".join(sorted(names, key=lambda s: int(s[4:]))))
' "$here" "${pwads[@]}")
    read -ra list <<< "$demos"
    [ ${#list[@]} -gt 0 ] || { echo "PWAD set $set has no demo lumps" >&2; exit 1; }
    set -- "${list[@]}"
fi
if [ $# -eq 0 ]; then set -- DEMO1 DEMO2 DEMO3; if [ -n "${demo4:-}" ]; then set -- "$@" DEMO4; fi; fi
files=()
# P2: and a 32 MiB zone (-mb; the reference's 6 MiB default fails AV's MAP20).
if [ ${#pwads[@]} -gt 0 ]; then files=(-mb 32 -file "${pwads[@]}"); fi
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
for demo in "$@"; do
    name=$(echo "$demo" | tr '[:upper:]' '[:lower:]')
    dump="$out/$prefix$name.vanilla"
    rm -f "$dump"
    # The demo's version byte picks the reference's -gameversion (T10.6).
    # P2: the last lump of that name across the IWAD and the set's PWADs.
    version=$(python3 -I -B -c '
import sys
sys.path.insert(0, sys.argv[1])
from mergewad import lumps, lump_name
found = None
for path in sys.argv[3:]:
    for raw, data in lumps(path):
        if lump_name(raw) == sys.argv[2].upper():
            found = data
if not found:
    sys.exit("no demo lump " + sys.argv[2])
print({106: "1.666", 107: "1.7", 108: "1.8", 109: "1.9"}.get(found[0], "1.9"))
' "$here" "$demo" "$iwad" "${pwads[@]}")
    # The lump by name (no NAME.lmp in the working directory); dump.c's
    # patched G_ReadDemoTiccmd exits at the demo's marker.
    if [ -n "${final:-}" ]; then version=final; fi
    if [ -n "${ultimate:-}" ]; then version=ultimate; fi
    if [ -n "$forced" ]; then version=$forced; fi
    (cd "$tmp" && env -u VIEWS -u DUMP_START -u DUMP_EVENTS DUMP_TICS="$dump" \
        "$ref" -iwad "$iwad" "${files[@]}" -gameversion "$version" -playdemo "$name" -nosound -nomusic -nogui >/dev/null)
    echo "$demo: $(wc -l < "$dump") tics -> $dump"
done
