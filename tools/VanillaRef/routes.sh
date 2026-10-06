#!/bin/bash
# The vanilla movement reference (T4.8; dev tool). Usage:
#   tools/VanillaRef/routes.sh [ROUTE.route...]
# Plays each route (default: every tests/IsoDoom.Tests/Sim/Routes/*.route) in
# the vanilla reference (build.sh; $VANILLA_REF or ~/.cache/isodoom/vanilla-ref)
# and writes its per-tic dump (routes.py): beside the route for the synthetic
# IWAD's routes (commit those), to $ISODOOM_VANILLA_ROUTES (default
# ~/.cache/isodoom/vanilla-routes) for DOOM1.WAD's (WAD-derived: never commit
# them). VanillaRouteTests compares the sim with them. Needs DOOM1.WAD
# (wads/ or $ISODOOM_DOOM1_WAD) for both.
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
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
(cd "$repo" && dotnet run --project tools/SyntheticIwad -- "$tmp/synthetic.wad" >/dev/null)
if [ $# -eq 0 ]; then set -- "$repo"/tests/IsoDoom.Tests/Sim/Routes/*.route; fi
python3 "$here/routes.py" "$ref" "$doom1" "$tmp/synthetic.wad" "$out" "$@"
