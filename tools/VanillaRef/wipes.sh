#!/bin/bash
# The vanilla screen wipe reference (T7.1a; dev tool). Usage:
#   tools/VanillaRef/wipes.sh [ROUTE.route...] [--images DIR STEPS]
# Plays each DOOM1.WAD, Doom II, Final Doom or (M9) The Ultimate Doom route (default: e1m1-exit, e1m3-secret-exit,
# e1m8-exit, e1m8-death, and T10.1's Doom II flow: map01-exit, map06-exit,
# map15-secret-exit, map31-exit, map31-secret-exit, map32-exit, and T10.5's
# map30-icon, to the cast call, and T10.7's Final Doom MAP01s: tnt-map01-walk-exit,
# plutonia-map01-walk-exit, in tests/IsoDoom.Tests/Sim/Routes) in the vanilla
# reference (build.sh; $VANILLA_REF or ~/.cache/isodoom/vanilla-ref) with the
# game drawn (-timedemo: a D_Display after every tic, so it wipes), then the
# intermission, the next level or the finale (wipes.py), and writes dump.c's
# per-tic and per-wipe dump NAME.wipe to $ISODOOM_VANILLA_ROUTES (default
# ~/.cache/isodoom/vanilla-routes; WAD-derived: never commit them). With
# --images, every wipe's start and end screens and the screens after melt
# steps STEPS ("N,M,..." or "all") go to DIR (keep it out of the repo).
# WipeTests compares the game's wipes with the dumps. Needs DOOM1.WAD
# (wads/ or $ISODOOM_DOOM1_WAD), and Doom II's IWAD for its routes
# (wads/doom2.wad or $ISODOOM_DOOM2_WAD), Final Doom's for theirs (wads/ or
# $ISODOOM_TNT_WAD, $ISODOOM_PLUTONIA_WAD), The Ultimate Doom's for its (M9, iwad
# doom: wads/doom.wad or $ISODOOM_DOOM_WAD).
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
routes=() images=()
while [ $# -gt 0 ]; do
    if [ "$1" = --images ]; then images=(--images "$2" "$3"); shift 3; else routes+=("$1"); shift; fi
done
if [ ${#routes[@]} -eq 0 ]; then
    for n in e1m1-exit e1m3-secret-exit e1m8-exit e1m8-death map01-exit map06-exit map15-secret-exit map31-exit map31-secret-exit map32-exit map30-icon tnt-map01-walk-exit plutonia-map01-walk-exit; do routes+=("$repo/tests/IsoDoom.Tests/Sim/Routes/$n.route"); done
fi
python3 "$here/wipes.py" "$ref" "$doom1" "$out" "${routes[@]}" "${images[@]}"
