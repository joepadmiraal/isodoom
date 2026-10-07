#!/bin/bash
# The vanilla intermission reference (T7.4; dev tool). Usage:
#   tools/VanillaRef/intermissions.sh [ROUTE.route[@PRESSES]...]
# Plays each DOOM1.WAD exit route (default: every one in
# tests/IsoDoom.Tests/Sim/Routes but E1M8's, whose exit goes to the finale)
# in the vanilla reference (build.sh; $VANILLA_REF or
# ~/.cache/isodoom/vanilla-ref) and then its intermission, with BT_USE
# pressed at the intermission tics of a schedule (intermissions.py: by
# default the stats counted out, NAME.wi, and skipped, NAME.wiskip), and writes
# dump.c's per-tic intermission dump to $ISODOOM_VANILLA_ROUTES (default
# ~/.cache/isodoom/vanilla-routes; WAD-derived: never commit them).
# IntermissionTests compares the game's intermission with them. Needs
# DOOM1.WAD (wads/ or $ISODOOM_DOOM1_WAD).
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
ref=${VANILLA_REF:-$HOME/.cache/isodoom/vanilla-ref}/doomgeneric/doomgeneric
refdir=$(dirname "$(dirname "$ref")")
# Build, or rebuild when ref.patch or dump.c changed since the last build.
if [ ! -x "$ref" ] || [ "$(cat "$here/ref.patch" "$here/dump.c" | sha256sum)" != "$(cat "$refdir/ref.stamp" 2>/dev/null)" ]; then
    ref=$("$here/build.sh" "$refdir")
fi
doom1=$(realpath "${ISODOOM_DOOM1_WAD:-$repo/wads/DOOM1.WAD}")
out=${ISODOOM_VANILLA_ROUTES:-$HOME/.cache/isodoom/vanilla-routes}
if [ $# -eq 0 ]; then
    for r in "$repo"/tests/IsoDoom.Tests/Sim/Routes/*.route; do
        body=$(sed -e 's/#.*//' "$r")
        if grep -qE '^ *iwad +doom1' <<< "$body" && grep -qE '^ *exit +' <<< "$body" && ! grep -qiE '^ *map +e[1-4]m8' <<< "$body"; then
            set -- "$@" "$r"
        fi
    done
fi
python3 "$here/intermissions.py" "$ref" "$doom1" "$out" "$@"
