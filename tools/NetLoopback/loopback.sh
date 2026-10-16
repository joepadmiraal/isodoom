#!/bin/bash
# The netgame's loopback smoke test (T11.6): two headless game processes on
# this machine, a host and a client, connect over ENet on 127.0.0.1, finish
# the lobby handshake and exchange scripted command bundles for TICS tics
# (default 1000, rounded up to whole 35-tic checksum windows) without running
# them (the level script's `netexchange`; the game loop's glue is T11.8's).
# The host's server compares every checksum window (a hash of the bundles
# each machine took); the script then checks both runs passed and printed
# the same hash. Usage:
#   tools/NetLoopback/loopback.sh IWAD [TICS] [-- GAME COMMAND...]
# GAME COMMAND runs the game (default `godot`, from the repo root), e.g.
# `-- export/linux/IsoDoom.x86_64`. The host listens on a free port of
# 127.0.0.1 only (`-host 0 -bind 127.0.0.1`: ENet picks it, the host prints
# it). Logs go to $NET_LOOPBACK_LOGS (default a temporary directory, kept on
# a failure). Exits 0 when both runs agree.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
if [ $# -lt 1 ] || [ "${1#-}" != "$1" ]; then
    sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//' >&2
    exit 2
fi
iwad=$(realpath "$1"); shift
tics=1000
if [ $# -gt 0 ] && [ "$1" != "--" ]; then tics=$1; shift; fi
game=(godot)
if [ $# -gt 0 ] && [ "$1" = "--" ]; then shift; [ $# -gt 0 ] && game=("$@"); fi
logs=${NET_LOOPBACK_LOGS:-$(mktemp -d)}
mkdir -p "$logs"
host_log=$logs/host.log client_log=$logs/client.log
# a minute and a half for the run: about 30 s of 35 Hz tics and the starts
limit=$(( tics / 35 + 90 ))
cd "$repo"
timeout "$limit" "${game[@]}" --headless -- -iwad "$iwad" -host 0 -bind 127.0.0.1 -players 2 --level \
    --level-script="net; netexchange $tics" >"$host_log" 2>&1 &
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
echo "loopback: host on 127.0.0.1:$port, $tics tics"
timeout "$limit" "${game[@]}" --headless -- -iwad "$iwad" -connect "127.0.0.1:$port" --level \
    --level-script="net; netexchange $tics" >"$client_log" 2>&1 &
client_pid=$!
host_status=0 client_status=0
wait "$host_pid" || host_status=$?
wait "$client_pid" || client_status=$?
result() { sed -n 's/^Level script: netexchange: \([0-9]* of [0-9]* tics, hash [0-9a-f]*\).*/\1/p' "$1" | head -n 1; }
host_result=$(result "$host_log") client_result=$(result "$client_log")
echo "loopback: host:   ${host_result:-no result} (exit $host_status)"
echo "loopback: client: ${client_result:-no result} (exit $client_status)"
grep -h '^Level script: netexchange:' "$host_log" "$client_log" || true
if [ "$host_status" -ne 0 ] || [ "$client_status" -ne 0 ] || [ -z "$host_result" ] || [ "$host_result" != "$client_result" ]; then
    echo "loopback: FAILED (logs: $logs)" >&2
    for f in "$host_log" "$client_log"; do echo "--- $f" >&2; grep -E '^(Net|Level script|ERROR)|rror' "$f" | tail -n 40 >&2 || true; done
    exit 1
fi
echo "loopback: OK"
[ -n "${NET_LOOPBACK_LOGS:-}" ] || rm -rf "$logs"
