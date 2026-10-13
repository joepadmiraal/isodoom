#!/bin/bash
# Screenshots of every DOOM1.WAD map, whole (dev tool), for comparing visual
# changes. Usage:
#   tools/LevelShots/shots.sh DIR [--top] [--size WxH] [--maps "E1M1 E1M5 ..."] [-- GAME ARGS...]
# Renders each map (default E1M1-E1M9) in the level scene offscreen (Xvfb +
# lavapipe) under the overview camera, which frames the whole level (iso, or
# straight down with --top), after one tic, with the overlay hidden, and
# saves DIR/MAP.png (default size 7680x4800, about a texel a pixel). GAME
# ARGS go to the game as they are, e.g.
# `-- --level-light=none --level-cutaway=off`. The game's
# settings are the defaults (a script run reads no settings file), so the
# shots change only when the code does. Compare two runs with
# tools/LevelShots/compare.py. Everything in DIR is WAD-derived: keep it out
# of the repo (screenshots/ is gitignored).
# Needs DOOM1.WAD (wads/ or $ISODOOM_DOOM1_WAD) and a built project.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
if [ $# -lt 1 ] || [ "${1#-}" != "$1" ]; then
    sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//' >&2
    exit 2
fi
dir=$(mkdir -p "$1" && cd "$1" && pwd); shift
size=7680x4800 maps="E1M1 E1M2 E1M3 E1M4 E1M5 E1M6 E1M7 E1M8 E1M9" view=()
while [ $# -gt 0 ]; do
    case $1 in
        --top) view=(--level-view=top); shift ;;
        --size) size=$2; shift 2 ;;
        --maps) maps=$2; shift 2 ;;
        --) shift; break ;;
        *) echo "unknown option $1" >&2; exit 2 ;;
    esac
done
doom1=$(realpath "${ISODOOM_DOOM1_WAD:-$repo/wads/DOOM1.WAD}")
failed=0
for map in $maps; do
    rm -f "$dir/$map.png"
    # one tic so the world (lights, fog walk, billboards) is as a game's first
    if (cd "$repo" && WAYLAND_DISPLAY='' VK_DRIVER_FILES=/usr/share/vulkan/icd.d/lvp_icd.json xvfb-run -a -s "-screen 0 ${size}x24" \
        godot --fixed-fps 35 --resolution "$size" -- -iwad "$doom1" --level "$map" --level-camera=overview \
        --level-wipe=off "${view[@]}" "$@" \
        --level-script="cmd 0 0 0 0 1; tics 1; tap F3; shot $dir/$map.png" >"$dir/$map.log" 2>&1) && [ -f "$dir/$map.png" ]; then
        echo "$map: $dir/$map.png"
        rm -f "$dir/$map.log"
    else
        echo "$map: FAILED (see $dir/$map.log)" >&2
        failed=1
    fi
done
exit $failed
