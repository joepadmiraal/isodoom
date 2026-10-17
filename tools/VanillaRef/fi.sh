#!/bin/bash
# IsoDoom's finale vs vanilla's screenshots (T7.5; dev tool). Usage:
#   tools/VanillaRef/fi.sh DIR ROUTE.route "TIC,TIC,..." [--presses "N,M,..."] [--keys "N,M,..."] [--images]
# Plays a DOOM1.WAD route that ends the episode (E1M8's exit,
# tests/IsoDoom.Tests/Sim/Routes), or (T10.5) a Doom II one whose exit leads
# to a text screen or the cast call (MAP30's: map30-icon; its intermission
# skipped first, finales.py's), and its finale, with BT_USE pressed for one
# tic at the finale tics of --presses (default 52: skips nothing before Doom
# II, and Doom II's text) and (T10.5) a key pressed after the finale tics of
# --keys (the cast call's kills: dump.c's $DUMP_FI_KEYS; the game's `tap
# Space` between the tics), in the vanilla reference (finales.py with --images: dump.c writes
# DIR/fiTIC.ppm, the 320x200 screen after finale tic TIC) and in the game
# scene offscreen (Xvfb + lavapipe, 1280x800: the screen at scale 4, square
# pixels as vanilla's, T7.11; `route
# FILE; gamestate finale; cmd ...; gamestate finale; shot` with the overlay
# hidden and no wipes, T7.1a: vanilla's run is not drawn: DIR/oTIC.png),
# then compares every pixel (wi.py --prefix fi;
# --images writes DIR/cTIC.png). Everything in DIR is WAD-derived: keep it
# out of the repo. Needs DOOM1.WAD (wads/ or $ISODOOM_DOOM1_WAD), and Doom
# II's IWAD for its routes (wads/doom2.wad or $ISODOOM_DOOM2_WAD).
# T9.4: also The Ultimate Doom's ExM8 exit routes (iwad doom: wads/doom.wad or $ISODOOM_DOOM_WAD).
# T11.8's follow-up: a multi-player route (players N) plays as a netgame in
# both (vanilla's netdemo; the scene's --level-players=N with the header's
# -deathmatch, -altdeath, -timer and -gameversion, sceneargs.sh), the
# finale's commands player 1's, the others idle.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
dir=$(mkdir -p "$1" && cd "$1" && pwd); route=$(realpath "$2"); tics=$3; shift 3
presses=52 keys=""
while [ "${1:-}" = --presses ] || [ "${1:-}" = --keys ]; do
    if [ "$1" = --presses ]; then presses=$2; else keys=$2; fi
    shift 2
done
doom1=$(realpath "${ISODOOM_DOOM1_WAD:-$repo/wads/DOOM1.WAD}")
# shellcheck source=tools/VanillaRef/sceneargs.sh
. "$here/sceneargs.sh"
header() { scene_header "$route" "$1"; }
scene_route_args "$route" # T11.8's follow-up: a multi-player route's netgame (--level-players, -deathmatch, ...)
map=$(header map); map=${map:-E1M1}
iwad=$doom1 intermission=""
if [ "$(header iwad)" = doom2 ] && [ -n "$(header exit)" ]; then
    # T10.5: Doom II: its intermission first, as finales.py's (BT_USE at its tics 20 and 40, 50 tics)
    iwad=$(realpath "${ISODOOM_DOOM2_WAD:-$repo/wads/doom2.wad}")
    intermission="gamestate intermission; cmd 0 0 0 0 19; cmd 0 0 0 2 1; cmd 0 0 0 0 19; cmd 0 0 0 2 1; cmd 0 0 0 0 10; "
elif [ "$(header iwad)" = doom ] && [ -n "$(header exit)" ] && [[ "${map^^}" =~ M8$ ]]; then
    # T9.4: The Ultimate Doom's episode ends (E3's bunny scroll among them)
    iwad=$(realpath "${ISODOOM_DOOM_WAD:-$repo/wads/doom.wad}")
elif [ "$(header iwad)" != doom1 ] || [ -z "$(header exit)" ] || [[ ! "${map^^}" =~ M8$ ]]; then
    echo "$route: only DOOM1.WAD (or The Ultimate Doom's) routes that end the episode, or Doom II ones that end at a text screen" >&2; exit 1
fi
skill=$(header skill); skill=${skill:-3}
monsters=off
if sed -e 's/#.*//' "$route" | grep -qw '^ *monsters'; then monsters=on; fi
ref=${VANILLA_REF:-$HOME/.cache/isodoom/vanilla-ref}/doomgeneric/doomgeneric
refdir=$(dirname "$(dirname "$ref")")
if [ ! -x "$ref" ] || [ "$(cat "$here/ref.patch" "$here/dump.c" | sha256sum)" != "$(cat "$refdir/ref.stamp" 2>/dev/null)" ]; then
    ref=$("$here/build.sh" "$refdir")
fi
rm -f "$dir"/fi*.ppm "$dir"/o*.png "$dir"/c*.png
IFS=',' read -ra list <<< "$tics"
schedule=$presses
if [ -n "$keys" ]; then
    # T10.5: finales.py's "PRESSES;KEYS;LENGTH" (the cast call goes on until the demo's end)
    schedule="$presses;$keys;$(printf '%s\n' "${list[@]}" | sort -n | tail -1)"
fi
python3 "$here/finales.py" "$ref" "$doom1" "$dir" "$route@$schedule" --images "$dir" "$tics" >/dev/null
for tic in "${list[@]}"; do
    # the finale's ticcmds up to TIC: BT_USE (2) at the presses, nothing between; a key (T10.5) after the tics of --keys
    cmds=$(python3 - "$tic" "$presses" "$keys" <<'PY'
import sys
tic, presses, keys = int(sys.argv[1]), {int(p) for p in sys.argv[2].split(',') if p}, {int(k) for k in sys.argv[3].split(',') if k}
out, idle = [], 0
for t in range(1, tic + 1):
    if t in presses:
        if idle:
            out.append(f'cmd 0 0 0 0 {idle}')
        out.append('cmd 0 0 0 2 1')
        idle = 0
    else:
        idle += 1
    if t in keys and t < tic:
        if idle:
            out.append(f'cmd 0 0 0 0 {idle}')
        out.append('gamestate finale; tap Space')
        idle = 0
if idle:
    out.append(f'cmd 0 0 0 0 {idle}')
print(''.join(c + '; ' for c in out))
PY
)
    (cd "$repo" && WAYLAND_DISPLAY='' VK_DRIVER_FILES=/usr/share/vulkan/icd.d/lvp_icd.json xvfb-run -a -s "-screen 0 1280x800x24" \
        godot --fixed-fps 35 --resolution 1280x800 -- -iwad "$iwad" --level "$map" "${scene_args[@]}" --level-tweaks=vanilla \
        --level-monsters=$monsters --level-skill="$skill" --level-wipe=off --level-hud-aspect=square --level-hud-weapon-icon=off --level-hud-weapon-digit=off --level-palette-effects=screen \
        --level-script="route $route; ${intermission}gamestate finale; ${cmds}gamestate finale; tap F3; shot $dir/o$tic.png" >/dev/null 2>&1)
done
python3 "$here/wi.py" "$dir" "${list[@]}" --prefix "fi" "$@"
