#!/usr/bin/env bash
# T7.14: -playdemo FILE.lmp end to end (a dev tool; CI runs it on the
# synthetic IWAD). Writes ROUTE as a demo (RouteGen --lmp, RouteFile.ToDemo),
# plays it with -playdemo (it must play to its end and quit with 0), then
# plays ROUTE itself in the level script (`route ROUTE; checksum`: its map,
# skill, monsters, players and modes) and checks that both end on the same
# world (leveltime and World.Checksum()). A route with route events, a
# `start` or a `timer` can't be a demo (RouteGen refuses it).
#
#   tools/DemoPlay/demoplay.sh IWAD ROUTE [-- GAME COMMAND]
#
# GAME COMMAND defaults to `godot` (the editor, from the repo root); e.g.
# `-- export/linux/IsoDoom.x86_64`. DEMOPLAY_LOGS=DIR keeps the logs. Ends
# "demoplay: OK".
set -euo pipefail

if [ $# -lt 2 ]; then
    echo "usage: $0 IWAD ROUTE [-- GAME COMMAND]" >&2
    exit 2
fi
iwad=$(cd "$(dirname "$1")" && pwd)/$(basename "$1")
route=$(cd "$(dirname "$2")" && pwd)/$(basename "$2")
shift 2
game=(godot)
if [ $# -gt 0 ]; then
    [ "$1" = "--" ] || { echo "$0: expected -- before the game command" >&2; exit 2; }
    shift
    game=("$@")
fi
repo=$(cd "$(dirname "$0")/../.." && pwd)
# shellcheck source=tools/VanillaRef/sceneargs.sh
. "$repo/tools/VanillaRef/sceneargs.sh"

dir=${DEMOPLAY_LOGS:-$(mktemp -d)}
mkdir -p "$dir"
name=$(basename "$route" .route)
lmp="$dir/$name.lmp"
dotnet run --project "$repo/tools/RouteGen" -- --lmp "$route" "$lmp"

# The demo: to its end, then the quit (singledemo)
rc=0
"${game[@]}" --headless --fixed-fps 35 -- -iwad "$iwad" -playdemo "$lmp" > "$dir/demo.log" 2>&1 || rc=$?
if [ "$rc" -ne 0 ]; then
    tail -20 "$dir/demo.log"
    echo "demoplay: FAILED: -playdemo exited $rc" >&2
    exit 1
fi
demo=$(sed -n 's/.*Demo: .* played to its end: .*, \(leveltime [0-9]* checksum [0-9a-f]*\).*/\1/p' "$dir/demo.log")
[ -n "$demo" ] || { tail -20 "$dir/demo.log"; echo "demoplay: FAILED: the demo did not play to its end" >&2; exit 1; }

# The route in the level script, in its game
map=$(scene_header "$route" map)
skill=$(scene_header "$route" skill)
monsters=off
if grep -q '^monsters' "$route"; then monsters=on; fi
scene_route_args "$route"
"${game[@]}" --headless --fixed-fps 35 -- -iwad "$iwad" --level "${map:-E1M1}" --level-skill="${skill:-3}" \
    --level-tweaks=vanilla --level-monsters="$monsters" "${scene_args[@]}" \
    --level-script="route $route; checksum" > "$dir/route.log" 2>&1 || rc=$?
if [ "$rc" -ne 0 ]; then
    tail -20 "$dir/route.log"
    echo "demoplay: FAILED: the route's level script exited $rc" >&2
    exit 1
fi
played=$(sed -n 's/.*Level script: checksum \(leveltime [0-9]* checksum [0-9a-f]*\).*/\1/p' "$dir/route.log")

echo "demoplay: $name: -playdemo $demo; route $played"
if [ "$demo" != "$played" ]; then
    echo "demoplay: FAILED: the demo and its route end on different worlds" >&2
    exit 1
fi
echo "demoplay: OK"
