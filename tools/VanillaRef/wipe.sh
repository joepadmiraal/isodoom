#!/bin/bash
# IsoDoom's screen wipes vs vanilla's screenshots (T7.1a; dev tool). Usage:
#   tools/VanillaRef/wipe.sh DIR ROUTE.route WIPE "STEP,STEP,..." [--images]
# Plays a DOOM1.WAD route (tests/IsoDoom.Tests/Sim/Routes) and wipes.py's tail
# (the intermission skipped and the next level, or the finale) in the vanilla
# reference drawn (wipes.py with --images: dump.c writes DIR/NAME.wipe and
# DIR/wipeN-sS.ppm, the 320x200 screen after melt step S of wipe N) and in
# the game scene offscreen (Xvfb + lavapipe, 1280x800: `wipe STEP WIPE; shot`
# holds the melt at each STEP of the same wipe: DIR/oSTEP.png), then compares the
# pixels the melt takes from a full screen in both (wipe.py; --images writes
# DIR/cSTEP.png). The game starts the route's map as a new game from the
# title loop (`newgame`), so its wipe 1 melts the title into the level as
# vanilla's melts its startup screen (not compared), and M_Random's index is
# vanilla's from then on. Everything in DIR is WAD-derived: keep it out of
# the repo.
# Needs DOOM1.WAD (wads/ or $ISODOOM_DOOM1_WAD).
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
dir=$(mkdir -p "$1" && cd "$1" && pwd); route=$(realpath "$2"); wipe=$3; steps=$4; shift 4
name=$(basename "$route" .route)
doom1=$(realpath "${ISODOOM_DOOM1_WAD:-$repo/wads/DOOM1.WAD}")
header() { sed -e 's/#.*//' "$route" | awk -v k="$1" '$1 == k { print $2; exit }'; }
if [ "$(header iwad)" != doom1 ]; then echo "$route: only DOOM1.WAD routes" >&2; exit 1; fi
if [ "$wipe" -lt 2 ]; then echo "wipe 1 is vanilla's from its startup screen: not comparable" >&2; exit 1; fi
map=$(header map); map=${map:-E1M1}
if [[ ! "${map^^}" =~ ^E1M([1-9])$ ]]; then echo "$route: only episode 1's maps" >&2; exit 1; fi
mapnum=${BASH_REMATCH[1]}
skill=$(header skill); skill=${skill:-3}
monsters=off
if sed -e 's/#.*//' "$route" | grep -qw '^ *monsters'; then monsters=on; fi
ref=${VANILLA_REF:-$HOME/.cache/isodoom/vanilla-ref}/doomgeneric/doomgeneric
refdir=$(dirname "$(dirname "$ref")")
if [ ! -x "$ref" ] || [ "$(cat "$here/ref.patch" "$here/dump.c" | sha256sum)" != "$(cat "$refdir/ref.stamp" 2>/dev/null)" ]; then
    ref=$("$here/build.sh" "$refdir")
fi
rm -f "$dir"/wipe*.ppm "$dir"/o*.png "$dir"/c*.png
python3 "$here/wipes.py" "$ref" "$doom1" "$dir" "$route" --images "$dir" "$steps" >/dev/null
# wipes.py's tail: the intermission skipped (use at its tics 20, 40, 60), then the next level; or the finale
tail="" next=""
if [ -n "$(header exit)" ]; then
    if [[ "${map^^}" =~ M8$ ]]; then
        tail="gamestate finale; cmd 0 0 0 0 1650; "
    else
        # (the intermission ends 10 tics after the last press: the next level drops the tics queued beyond, so they are queued
        # again; the wipes up to the intermission's are held before that)
        tail="gamestate intermission; cmd 0 0 0 0 19; cmd 0 0 0 2 1; cmd 0 0 0 0 19; cmd 0 0 0 2 1; cmd 0 0 0 0 19; cmd 0 0 0 2 1; cmd 0 0 0 0 140; "
        next="gamestate level; cmd 0 0 0 0 130; "
    fi
fi
shots=""
IFS=',' read -ra list <<< "$steps"
for s in "${list[@]}"; do shots+="wipe $s $wipe; shot $dir/o$s.png; "; done
if [ "$wipe" -le 2 ]; then script="${tail}${shots}wipe; ${next}"; else script="${tail}${next}${shots}wipe"; fi
(cd "$repo" && WAYLAND_DISPLAY='' VK_DRIVER_FILES=/usr/share/vulkan/icd.d/lvp_icd.json xvfb-run -a -s "-screen 0 1280x800x24" \
    godot --fixed-fps 35 --resolution 1280x800 -- -iwad "$doom1" --level-tweaks=vanilla \
    --level-monsters=$monsters --level-skill="$skill" --level-wipe=melt \
    --level-script="tap F3; newgame $skill 1 $mapnum; wipehold ${list[0]} $wipe; route $route; ${script}" >/dev/null 2>&1)
python3 "$here/wipe.py" "$dir" "$name" "$wipe" "${list[@]}" "$@"
