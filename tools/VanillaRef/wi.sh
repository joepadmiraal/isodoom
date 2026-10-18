#!/bin/bash
# IsoDoom's intermission vs vanilla's screenshots (T7.4; dev tool). Usage:
#   tools/VanillaRef/wi.sh DIR ROUTE.route "TIC,TIC,..." [--presses "N,M,..."] [--images]
# Plays a DOOM1.WAD exit route (tests/IsoDoom.Tests/Sim/Routes) and its
# intermission, with BT_USE pressed for one tic at the intermission tics of
# --presses (default 600: the stats counted out, then the next location), in
# the vanilla reference (intermissions.py with --images: dump.c writes
# DIR/wiTIC.ppm, the 320x200 screen after intermission tic TIC) and in the game
# scene offscreen (Xvfb + lavapipe, 1280x800: the screen at scale 4, square
# pixels as vanilla's, T7.11; `route
# FILE; gamestate intermission; cmd ...; gamestate intermission; shot` with
# the overlay hidden and no wipes, T7.1a: vanilla's run is not drawn:
# DIR/oTIC.png), then compares every pixel (wi.py;
# --images writes DIR/cTIC.png). The intermission's last tic is not comparable:
# the game shows the next level after it (its game action runs at the tic's
# end, SPEC §12 T7.1). Everything in DIR is WAD-derived: keep it out of the repo.
# Needs DOOM1.WAD (wads/ or $ISODOOM_DOOM1_WAD).
# T11.8's follow-up: a multi-player route (players N) plays as a netgame in
# both, the scene with --level-players=N and the header's -deathmatch,
# -altdeath, -timer and -gameversion (sceneargs.sh), the intermission's
# commands player 1's (the others idle, as the vanilla netdemo's); and a
# synthetic IWAD exit route (iwad synthetic) plays over DOOM1.WAD in both:
# vanilla's -file, the scene's DIR/doom1+synthetic.wad (mergewad.py: the same
# lump directory as one IWAD, since the shareware IWAD refuses -file); its
# vanilla dumps go to DIR too, never beside the route.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
dir=$(mkdir -p "$1" && cd "$1" && pwd); route=$(realpath "$2"); tics=$3; shift 3
presses=600
if [ "${1:-}" = --presses ]; then presses=$2; shift 2; fi
doom1=$(realpath "${ISODOOM_DOOM1_WAD:-$repo/wads/DOOM1.WAD}")
# shellcheck source=tools/VanillaRef/sceneargs.sh
. "$here/sceneargs.sh"
header() { scene_header "$route" "$1"; }
if { [ "$(header iwad)" != doom1 ] && [ "$(header iwad)" != synthetic ]; } || [ -z "$(header exit)" ]; then
    echo "$route: only DOOM1.WAD (or synthetic IWAD) exit routes" >&2; exit 1
fi
scene_route_args "$route"
iwad=$doom1 synthetic=()
if [ "$(header iwad)" = synthetic ]; then
    (cd "$repo" && dotnet run --project tools/SyntheticIwad -- "$dir/synthetic.wad" >/dev/null)
    python3 "$here/mergewad.py" "$dir/doom1+synthetic.wad" "$doom1" "$dir/synthetic.wad"
    iwad=$dir/doom1+synthetic.wad synthetic=(--synthetic "$dir/synthetic.wad")
fi
map=$(header map); map=${map:-E1M1}
skill=$(header skill); skill=${skill:-3}
monsters=off
if sed -e 's/#.*//' "$route" | grep -qw '^ *monsters'; then monsters=on; fi
ref=${VANILLA_REF:-$HOME/.cache/isodoom/vanilla-ref}/doomgeneric/doomgeneric
refdir=$(dirname "$(dirname "$ref")")
if [ ! -x "$ref" ] || [ "$(cat "$here/ref.patch" "$here/dump.c" | sha256sum)" != "$(cat "$refdir/ref.stamp" 2>/dev/null)" ]; then
    ref=$("$here/build.sh" "$refdir")
fi
rm -f "$dir"/wi*.ppm "$dir"/o*.png "$dir"/c*.png
python3 -B "$here/intermissions.py" "$ref" "$doom1" "$dir" "$route@$presses" --images "$dir" "$tics" "${synthetic[@]}" --all-in-outdir >/dev/null
IFS=',' read -ra list <<< "$tics"
IFS=',' read -ra down <<< "$presses"
for tic in "${list[@]}"; do
    # the intermission's ticcmds up to TIC: BT_USE (2) at the presses, nothing between
    cmds="" last=0
    for p in "${down[@]}"; do
        if [ "$p" -gt "$tic" ]; then break; fi
        if [ $((p - 1)) -gt "$last" ]; then cmds+="cmd 0 0 0 0 $((p - 1 - last)); "; fi
        cmds+="cmd 0 0 0 2 1; "; last=$p
    done
    if [ "$tic" -gt "$last" ]; then cmds+="cmd 0 0 0 0 $((tic - last)); "; fi
    (cd "$repo" && WAYLAND_DISPLAY='' VK_DRIVER_FILES=/usr/share/vulkan/icd.d/lvp_icd.json xvfb-run -a -s "-screen 0 1280x800x24" \
        godot --fixed-fps 35 --resolution 1280x800 -- -iwad "$iwad" --level "$map" "${scene_args[@]}" --level-tweaks=vanilla \
        --level-monsters=$monsters --level-skill="$skill" --level-wipe=off --level-hud-aspect=square --level-hud-bar-width=320 --level-hud-weapon-icon=off --level-hud-weapon-digit=off --level-palette-effects=screen \
        --level-script="route $route; gamestate intermission; ${cmds}gamestate intermission; tap F3; shot $dir/o$tic.png" >/dev/null 2>&1)
done
python3 "$here/wi.py" "$dir" "${list[@]}" "$@"
