#!/bin/bash
# The netgame's loopback test: two headless game processes on this machine, a
# host and a client, connect over ENet on 127.0.0.1 and finish the lobby
# handshake. Two modes:
#  - exchange (T11.6, the default): they exchange scripted command bundles for
#    TICS tics (default 1000, rounded up to whole 35-tic checksum windows)
#    without running them (the level script's `netexchange`); the host's
#    server compares every window (a hash of the bundles each machine took),
#    and both must print the same hash.
#  - --play ROUTE NEXT (T11.8): they play the game in lockstep. Both play the
#    multi-player ROUTE (each machine sends its own player's commands, from
#    game tic 70 on; both run its events), the host in the route's map, skill,
#    monsters and modes, through the intermission (skipped) into map NEXT;
#    then the host's server must have compared every checksum window with no
#    desync, and the client must have seen no consistency failure.
# Usage:
#   tools/NetLoopback/loopback.sh IWAD [TICS] [-- GAME COMMAND...]
#   tools/NetLoopback/loopback.sh IWAD --play ROUTE NEXT [-- GAME COMMAND...]
# GAME COMMAND runs the game (default `godot`, from the repo root), e.g.
# `-- export/linux/IsoDoom.x86_64`. The host listens on a free port of
# 127.0.0.1 only (`-host 0 -bind 127.0.0.1`: ENet picks it, the host prints
# it). Logs go to $NET_LOOPBACK_LOGS (default a temporary directory, kept on
# a failure). Exits 0 when both runs agree.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
if [ $# -lt 1 ] || [ "${1#-}" != "$1" ]; then
    sed -n '2,23p' "$0" | sed 's/^# \{0,1\}//' >&2
    exit 2
fi
iwad=$(realpath "$1"); shift
tics=1000 route='' next=''
if [ $# -gt 0 ] && [ "$1" = "--play" ]; then
    [ $# -ge 3 ] || { echo "loopback: --play ROUTE NEXT" >&2; exit 2; }
    route=$(realpath "$2") next=$3; shift 3
elif [ $# -gt 0 ] && [ "$1" != "--" ]; then
    tics=$1; shift
fi
game=(godot)
if [ $# -gt 0 ] && [ "$1" = "--" ]; then shift; [ $# -gt 0 ] && game=("$@"); fi
logs=${NET_LOOPBACK_LOGS:-$(mktemp -d)}
mkdir -p "$logs"
host_log=$logs/host.log client_log=$logs/client.log
host_args=(--level)
if [ -n "$route" ]; then
    # the route's header: its map, skill, monsters and modes are the host's game
    header() { sed -n "s/^$1 \(.*\)/\1/p" "$route" | head -n 1; }
    map=$(header map) skill=$(header skill) deathmatch=$(header deathmatch) timer=$(header timer)
    host_args=(--level "${map:-E1M1}" --level-skill="${skill:-3}" --level-tweaks=vanilla)
    if grep -q '^monsters' "$route"; then host_args+=(--level-monsters=on); else host_args+=(--level-monsters=off); fi
    case "${deathmatch:-0}" in 1) host_args+=(-deathmatch) ;; 2) host_args+=(-altdeath) ;; esac
    [ -n "$timer" ] && host_args+=(-timer "$timer")
    route_tics=$(grep -c '^-\?[0-9]' "$route" || true)
    script="net; netwait 2; nettic 70; route $route; until gamestate intermission 300; skip; until map $next 60; tics 70; netcheck; checksum; net"
    # the route (with its xcount lines, a longer run), the intermission and the start; at 35 Hz
    limit=$(( route_tics * 4 / 35 + 240 ))
    result_line='^Level script: netcheck:'
else
    script="net; netexchange $tics"
    # a minute and a half for the run: about 30 s of 35 Hz tics and the starts
    limit=$(( tics / 35 + 90 ))
    result_line='^Level script: netexchange:'
fi
cd "$repo"
# (the host waits a little after its check, so the client's ends first)
timeout "$limit" "${game[@]}" --headless -- -iwad "$iwad" -host 0 -bind 127.0.0.1 -players 2 "${host_args[@]}" \
    --level-script="$script${route:+; sleep 3}" >"$host_log" 2>&1 &
host_pid=$!
port=
for _ in $(seq 1 600); do
    port=$(sed -n 's/^Net: listening on 127\.0\.0\.1:\([0-9]*\),.*/\1/p' "$host_log" | head -n 1)
    [ -n "$port" ] && break
    kill -0 "$host_pid" 2>/dev/null || break
    sleep 0.1
done
if [ -z "$port" ]; then
    echo "loopback: the host did not listen (log: $host_log)" >&2
    tail -n 30 "$host_log" >&2
    kill "$host_pid" 2>/dev/null || true
    exit 1
fi
if [ -n "$route" ]; then what="playing $(basename "$route") into $next"; else what="$tics tics"; fi
echo "loopback: host on 127.0.0.1:$port, $what"
timeout "$limit" "${game[@]}" --headless -- -iwad "$iwad" -connect "127.0.0.1:$port" --level \
    --level-script="$script" >"$client_log" 2>&1 &
client_pid=$!
host_status=0 client_status=0
wait "$host_pid" || host_status=$?
wait "$client_pid" || client_status=$?
if [ -n "$route" ]; then
    # both in sync (the host's server compared every window they ran: the worlds' checksums agreed)
    result() { sed -n 's/^Level script: netcheck: \(in sync\),.*/\1/p' "$1" | head -n 1; }
else
    result() { sed -n 's/^Level script: netexchange: \([0-9]* of [0-9]* tics, hash [0-9a-f]*\).*/\1/p' "$1" | head -n 1; }
fi
host_result=$(result "$host_log") client_result=$(result "$client_log")
echo "loopback: host:   ${host_result:-no result} (exit $host_status)"
echo "loopback: client: ${client_result:-no result} (exit $client_status)"
grep -h "$result_line" "$host_log" "$client_log" || true
if [ "$host_status" -ne 0 ] || [ "$client_status" -ne 0 ] || [ -z "$host_result" ] || [ "$host_result" != "$client_result" ]; then
    echo "loopback: FAILED (logs: $logs)" >&2
    for f in "$host_log" "$client_log"; do echo "--- $f" >&2; grep -E '^(Net|Level script|ERROR)|rror' "$f" | tail -n 40 >&2 || true; done
    exit 1
fi
echo "loopback: OK"
[ -n "${NET_LOOPBACK_LOGS:-}" ] || rm -rf "$logs"
