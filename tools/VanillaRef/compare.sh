#!/bin/bash
# IsoDoom vs vanilla at eye level (T2.9; dev tool). Usage:
#   tools/VanillaRef/compare.sh DIR IWAD MAP "X Y ANGLE;X Y ANGLE;..." [--images] [-- LEVEL-ARGS...]
# Renders each view with the vanilla reference renderer (build.sh; $VANILLA_REF
# or ~/.cache/isodoom/vanilla-ref) and with the level scene offscreen (Xvfb +
# lavapipe; free-fly camera at eye level, vanilla's field of view, camera-depth
# light, void in green), then compares them (compare.py). Writes vN.ppm, oN.png
# (and dN.png) to DIR: WAD-derived, keep them out of the repo.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
dir=$(mkdir -p "$1" && cd "$1" && pwd); iwad=$(realpath "$2"); map=${3^^}; views=$4; shift 4
images=""
if [ "${1:-}" = "--images" ]; then images=--images; shift; fi
if [ "${1:-}" = "--" ]; then shift; fi
ref=${VANILLA_REF:-$HOME/.cache/isodoom/vanilla-ref}/doomgeneric/doomgeneric
refdir=$(dirname "$(dirname "$ref")")
# Build, or rebuild when ref.patch or dump.c changed since the last build.
if [ ! -x "$ref" ] || [ "$(cat "$here/ref.patch" "$here/dump.c" | sha256sum)" != "$(cat "$refdir/ref.stamp" 2>/dev/null)" ]; then
    ref=$("$here/build.sh" "$refdir")
fi
if [[ $map =~ ^E([1-9])M([1-9])$ ]]; then warp=("${BASH_REMATCH[1]}" "${BASH_REMATCH[2]}")
elif [[ $map =~ ^MAP([0-9][0-9])$ ]]; then warp=("$((10#${BASH_REMATCH[1]}))")
else echo "unknown map $map" >&2; exit 1; fi
rm -f "$dir"/v*.ppm "$dir"/v*.z "$dir"/o*.png "$dir"/d*.png
echo "$views" > "$dir/views.txt"
(cd "$dir" && VIEWS="$views" OUTDIR="$dir" DUMP_STATIC=1 "$ref" -iwad "$iwad" -warp "${warp[@]}" -nomonsters -nosound -nomusic >/dev/null 2>&1)
script="tap F3; fov vanilla"
IFS=';' read -ra list <<< "$views"
# At vanilla's eye height (the highest floor within the player's radius, plus 41).
for i in "${!list[@]}"; do script="$script; view ${list[$i]} $(cat "$dir/v$i.z"); shot $dir/o$i.png"; done
(cd "$repo" && WAYLAND_DISPLAY='' VK_DRIVER_FILES=/usr/share/vulkan/icd.d/lvp_icd.json xvfb-run -a -s "-screen 0 1280x800x24" \
    godot --fixed-fps 60 --resolution 1280x800 -- -iwad "$iwad" --level "$map" --level-camera=fly --level-light=camera \
    --level-background=00ff00 "$@" --level-script="$script" >/dev/null 2>&1)
python3 "$here/compare.py" "$dir" "$iwad" $images
