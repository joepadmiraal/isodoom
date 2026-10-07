#!/bin/bash
# The vanilla demo reference (T6.13; dev tool). Usage:
#   tools/VanillaRef/demos.sh [DEMO1 DEMO2 DEMO3]
# Plays each demo lump of DOOM1.WAD (wads/ or $ISODOOM_DOOM1_WAD; default
# all three) in the vanilla reference (build.sh; $VANILLA_REF or
# ~/.cache/isodoom/vanilla-ref) with -playdemo and writes its per-tic dump
# (dump.c, the routes' format: tools/VanillaRef/routes.sh) as demoN.vanilla
# to $ISODOOM_VANILLA_ROUTES (default ~/.cache/isodoom/vanilla-routes).
# WAD-derived: never commit them. DemoSyncTests compares the sim with them.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
ref=${VANILLA_REF:-$HOME/.cache/isodoom/vanilla-ref}/doomgeneric/doomgeneric
refdir=$(dirname "$(dirname "$ref")")
if [ ! -x "$ref" ] || [ "$(cat "$here/ref.patch" "$here/dump.c" | sha256sum)" != "$(cat "$refdir/ref.stamp" 2>/dev/null)" ]; then
    ref=$("$here/build.sh" "$refdir")
fi
doom1=$(realpath "${ISODOOM_DOOM1_WAD:-$repo/wads/DOOM1.WAD}")
out=$(realpath -m "${ISODOOM_VANILLA_ROUTES:-$HOME/.cache/isodoom/vanilla-routes}")
mkdir -p "$out"
if [ $# -eq 0 ]; then set -- DEMO1 DEMO2 DEMO3; fi
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
for demo in "$@"; do
    name=$(echo "$demo" | tr '[:upper:]' '[:lower:]')
    dump="$out/$name.vanilla"
    rm -f "$dump"
    # The lump by name (no NAME.lmp in the working directory); dump.c's
    # patched G_ReadDemoTiccmd exits at the demo's marker.
    (cd "$tmp" && env -u VIEWS -u DUMP_START -u DUMP_EVENTS DUMP_TICS="$dump" \
        "$ref" -iwad "$doom1" -playdemo "$name" -nosound -nomusic -nogui >/dev/null)
    echo "$demo: $(wc -l < "$dump") tics -> $dump"
done
