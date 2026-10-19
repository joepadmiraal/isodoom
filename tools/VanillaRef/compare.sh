#!/bin/bash
# IsoDoom vs vanilla at eye level (T2.9; dev tool). Usage:
#   tools/VanillaRef/compare.sh DIR IWAD MAP "X Y ANGLE;X Y ANGLE;..." [--images] [--tic N] [--pwad SET [--tables]] [-- LEVEL-ARGS...]
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
# P2: --pwad SET loads a PWAD set of docs/PWAD-VALIDATION.md (pwads.py: its
# -file list from wads/pwads/SET or $ISODOOM_PWADS/SET) in both, over IWAD
# (pass the set's: wads/doom2.wad or wads/doom.wad; - picks it), and
# compare.py takes the palette and colormap from the last WAD that has them;
# the reference gets a 32 MiB zone then (-mb 32: limit-removing maps outgrow 6).
# P5: the height cues off (T6.13s's default shade lights every surface by its
# height above the player's feet, which stands at the map's start: a view on
# another floor height drew a level or two brighter than vanilla).
# P5: --tables (with --pwad) also renders vanilla with the set's PLAYPAL and
# COLORMAP renamed away (tables.py strip: copies in DIR/tables/wads), so with
# the IWAD's, into DIR/tables/, and tables.py diff reports, at the pixels where
# the two vanilla renders differ, whether IsoDoom drew the set's colour or the
# IWAD's (exit 1 when it drew the IWAD's).
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
dir=$(mkdir -p "$1" && cd "$1" && pwd); iwad=$2; map=${3^^}; views=$4; shift 4
images=""
tic=""
set=""
tables=""
while [ $# -gt 0 ]; do
    case "$1" in
        --images) images=--images; shift ;;
        --tic) tic=$2; shift 2 ;;
        --pwad) set=$2; shift 2 ;;
        --tables) tables=1; shift ;;
        *) break ;;
    esac
done
pwads=()
if [ -n "$set" ]; then
    wads=$(python3 -I "$here/pwads.py" "$set")
    mapfile -t pwads <<< "$wads"
    if [ "$iwad" = - ]; then iwad=${pwads[0]}; fi
    pwads=("${pwads[@]:1}")
fi
iwad=$(realpath "$iwad")
files=()
if [ ${#pwads[@]} -gt 0 ]; then files=(-file "${pwads[@]}"); refmem=(-mb 32); else refmem=(); fi
if [ "${1:-}" = "--" ]; then shift; fi
if [ -n "$tables" ] && [ ${#pwads[@]} -eq 0 ]; then echo "--tables needs --pwad" >&2; exit 1; fi
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
# The reference into OUTDIR with one environment setting and the -file list.
run_ref() {
    local out=$1 setting=$2; shift 2
    (cd "$out" && env VIEWS="$views" OUTDIR="$out" "$setting" "$ref" "${refmem[@]}" -iwad "$iwad" "$@" -warp "${warp[@]}" -nomonsters -nosound -nomusic >/dev/null 2>&1)
}
echo "$views" > "$dir/views.txt"
if [ -n "$tic" ]; then
    run_ref "$dir" DUMP_TIC="$tic" "${files[@]}"
    # N tics standing still (scripted: the world then holds), with no monsters as vanilla's -nomonsters.
    script="cmd 0 0 0 0 $tic; checksum; tap F3; fov vanilla"
    set -- --level-monsters=off --level-tweaks=vanilla "$@"
else
    run_ref "$dir" DUMP_STATIC=1 "${files[@]}"
    # The world holds still at its start (no tic runs): the map's lights, no animation (T5.7).
    script="sim scripted; tap F3; fov vanilla"
fi
IFS=';' read -ra list <<< "$views"
# At vanilla's eye height (the highest floor within the player's radius, plus 41).
for i in "${!list[@]}"; do script="$script; view ${list[$i]} $(cat "$dir/v$i.z"); shot $dir/o$i.png"; done
(cd "$repo" && WAYLAND_DISPLAY='' VK_DRIVER_FILES=/usr/share/vulkan/icd.d/lvp_icd.json xvfb-run -a -s "-screen 0 1280x800x24" \
    godot --fixed-fps 60 --resolution 1280x800 -- -iwad "$iwad" "${files[@]}" --level "$map" --level-camera=fly --level-light=camera \
    --level-background=00ff00 --level-things=off --level-masked-back=off --level-upper-walls=all --level-height-cues=off "$@" --level-script="$script" >/dev/null 2>&1)
python3 "$here/compare.py" "$dir" "$iwad" "${pwads[@]}" $images
if [ -n "$tables" ]; then
    rm -rf "$dir/tables" && mkdir -p "$dir/tables"
    mapfile -t stripped < <(python3 "$here/tables.py" strip "$dir/tables/wads" "${pwads[@]}")
    if [ -n "$tic" ]; then run_ref "$dir/tables" DUMP_TIC="$tic" -file "${stripped[@]}"
    else run_ref "$dir/tables" DUMP_STATIC=1 -file "${stripped[@]}"; fi
    python3 "$here/tables.py" diff "$dir" "$dir/tables"
fi
