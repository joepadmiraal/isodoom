#!/bin/bash
# IsoDoom's HUD vs vanilla's along a route (T6.11; dev tool). Usage:
#   tools/VanillaRef/hud.sh DIR ROUTE.route "TIC,TIC,..." [--images]
# Plays a DOOM1.WAD route (tests/IsoDoom.Tests/Sim/Routes) in the vanilla
# reference (routes.sh with $DUMP_HUD_DIR/$DUMP_HUD_TICS: dump.c writes
# DIR/hudTIC.ppm, the message line and the status bar after each TIC) and in
# the level scene offscreen (Xvfb + lavapipe, 1280x800: the HUD at scale 4;
# `route FILE TIC; tics TIC; shot` with the overlay hidden and no wipes,
# T7.1a, whose M_Random draws vanilla's undrawn run lacks: DIR/oTIC.png),
# then compares the HUD pixels (hud.py; --images writes DIR/cTIC.png).
# Everything in DIR is WAD-derived: keep it out of the repo. Needs DOOM1.WAD
# (wads/ or $ISODOOM_DOOM1_WAD).
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
dir=$(mkdir -p "$1" && cd "$1" && pwd); route=$(realpath "$2"); tics=$3; shift 3
doom1=$(realpath "${ISODOOM_DOOM1_WAD:-$repo/wads/DOOM1.WAD}")
header() { sed -e 's/#.*//' "$route" | awk -v k="$1" '$1 == k { print $2; exit }'; }
if [ "$(header iwad)" != doom1 ]; then echo "$route: only DOOM1.WAD routes (the level scene plays them)" >&2; exit 1; fi
map=$(header map); map=${map:-E1M1}
skill=$(header skill); skill=${skill:-3}
monsters=off
if sed -e 's/#.*//' "$route" | grep -qw '^ *monsters'; then monsters=on; fi
rm -f "$dir"/hud*.ppm "$dir"/o*.png "$dir"/c*.png
DUMP_HUD_DIR="$dir" DUMP_HUD_TICS="$tics" ISODOOM_VANILLA_ROUTES="$dir" "$here/routes.sh" "$route" >/dev/null
IFS=',' read -ra list <<< "$tics"
for tic in "${list[@]}"; do
    (cd "$repo" && WAYLAND_DISPLAY='' VK_DRIVER_FILES=/usr/share/vulkan/icd.d/lvp_icd.json xvfb-run -a -s "-screen 0 1280x800x24" \
        godot --fixed-fps 35 --resolution 1280x800 -- -iwad "$doom1" --level "$map" --level-tweaks=vanilla \
        --level-monsters=$monsters --level-skill="$skill" --level-wipe=off \
        --level-script="route $route $tic; tics $tic; tap F3; shot $dir/o$tic.png" >/dev/null 2>&1)
done
python3 "$here/hud.py" "$dir" "${list[@]}" "$@"
