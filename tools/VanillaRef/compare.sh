#!/bin/bash
# IsoDoom vs vanilla at eye level (T2.9; dev tool). Usage:
#   tools/VanillaRef/compare.sh DIR IWAD MAP "X Y ANGLE;X Y ANGLE;..." [--images] [--tic N] [-- LEVEL-ARGS...]
# Renders each view with the vanilla reference renderer (build.sh; $VANILLA_REF
# or ~/.cache/isodoom/vanilla-ref) and with the level scene offscreen (Xvfb +
# lavapipe; free-fly camera at eye level, vanilla's field of view, camera-depth
# light, void in green, no sprites as vanilla's render has none), then compares
# them (compare.py). Writes vN.ppm, oN.png (and dN.png) to DIR: WAD-derived,
# keep them out of the repo. By default both show the map lights, no
# animations or scrolling; with --tic N (T5.7) both run N tics from the map's
# start with no monsters and the player standing at its start, and render the
# views then, with the light specials, animated textures and flats and
# scrolling walls as they are at that tic (the same in both when the sim
# matches vanilla: the light thinkers' P_Random calls included).
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
dir=$(mkdir -p "$1" && cd "$1" && pwd); iwad=$(realpath "$2"); map=${3^^}; views=$4; shift 4
images=""
tic=""
while [ $# -gt 0 ]; do
    case "$1" in
        --images) images=--images; shift ;;
        --tic) tic=$2; shift 2 ;;
        *) break ;;
    esac
done
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
if [ -n "$tic" ]; then
    (cd "$dir" && VIEWS="$views" OUTDIR="$dir" DUMP_TIC="$tic" "$ref" -iwad "$iwad" -warp "${warp[@]}" -nomonsters -nosound -nomusic >/dev/null 2>&1)
    # N tics standing still (scripted: the world then holds), with no monsters as vanilla's -nomonsters.
    script="cmd 0 0 0 0 $tic; checksum; tap F3; fov vanilla"
    set -- --level-monsters=off --level-tweaks=vanilla "$@"
else
    (cd "$dir" && VIEWS="$views" OUTDIR="$dir" DUMP_STATIC=1 "$ref" -iwad "$iwad" -warp "${warp[@]}" -nomonsters -nosound -nomusic >/dev/null 2>&1)
    # The world holds still at its start (no tic runs): the map's lights, no animation (T5.7).
    script="sim scripted; tap F3; fov vanilla"
fi
IFS=';' read -ra list <<< "$views"
# At vanilla's eye height (the highest floor within the player's radius, plus 41).
for i in "${!list[@]}"; do script="$script; view ${list[$i]} $(cat "$dir/v$i.z"); shot $dir/o$i.png"; done
(cd "$repo" && WAYLAND_DISPLAY='' VK_DRIVER_FILES=/usr/share/vulkan/icd.d/lvp_icd.json xvfb-run -a -s "-screen 0 1280x800x24" \
    godot --fixed-fps 60 --resolution 1280x800 -- -iwad "$iwad" --level "$map" --level-camera=fly --level-light=camera \
    --level-background=00ff00 --level-things=off --level-masked-back=off "$@" --level-script="$script" >/dev/null 2>&1)
python3 "$here/compare.py" "$dir" "$iwad" $images
