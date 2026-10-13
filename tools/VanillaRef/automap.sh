#!/bin/bash
# The vanilla automap reference (T7.9; dev tool). Usage:
#   tools/VanillaRef/automap.sh [ROUTE.route [TICS]] [--images DIR]
# Plays a DOOM1.WAD route (default e1m1-exit in tests/IsoDoom.Tests/Sim/Routes,
# at level tics 5,350,900,1500,2100; tic 1 runs before the reference has a screen) in the vanilla reference (build.sh;
# $VANILLA_REF or ~/.cache/isodoom/vanilla-ref) with the game drawn, and at
# each tic opens, works and shoots the automap (dump.c's $DUMP_AM): writes
# NAME.am to $ISODOOM_VANILLA_ROUTES (default ~/.cache/isodoom/vanilla-routes;
# WAD-derived: never commit them); with --images the shots go to DIR as PPMs
# (keep it out of the repo). AmMapTests compares the game's automap with the
# dump. Needs DOOM1.WAD (wads/ or $ISODOOM_DOOM1_WAD).
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
images=() rest=()
while [ $# -gt 0 ]; do
    if [ "$1" = --images ]; then images=(--images "$2"); shift 2; else rest+=("$1"); shift; fi
done
route=${rest[0]:-$repo/tests/IsoDoom.Tests/Sim/Routes/e1m1-exit.route}
tics=${rest[1]:-5,350,900,1500,2100}
python3 "$here/automap.py" "$ref" "$doom1" "$out" "$route" "$tics" "${images[@]}"
