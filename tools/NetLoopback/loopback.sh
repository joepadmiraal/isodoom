#!/usr/bin/env bash
# The netgame's loopback test: two headless game processes on this machine, a
# host and a client, connect over ENet on 127.0.0.1 and finish the lobby
# handshake. Four modes:
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
#  - --kill client|host ROUTE (T11.11): they play ROUTE as --play does, and
#    after 210 game tics the client's (or the host's) process is killed
#    (SIGKILL: no quit, no disconnect). Killing the client: the host must drop
#    it after the timeout (-nettimeout 5), player 2 must leave the host's game
#    and the host go on alone in sync (70 more tics, every window compared).
#    Killing the host: the client must time out and end the game at the title
#    loop.
#  - --desync ROUTE (T11.11): they play ROUTE, and after 140 game tics the
#    client forces a desync (`netdesync`: its world alone takes a P_Random);
#    both must stop at the same window and write their dumps (`--netdesync`,
#    in the logs' directory), each with its checksums, commands and the save
#    of the last agreed window.
# Usage:
#   tools/NetLoopback/loopback.sh IWAD [TICS] [-- GAME... [-- CLIENT GAME...]]
#   tools/NetLoopback/loopback.sh IWAD --play ROUTE NEXT [-- GAME... [-- CLIENT GAME...]]
#   tools/NetLoopback/loopback.sh IWAD --kill client|host ROUTE [-- GAME...]
#   tools/NetLoopback/loopback.sh IWAD --desync ROUTE [-- GAME...]
# GAME runs the game (default `godot`, from the repo root), e.g.
# `-- export/linux/IsoDoom.x86_64`; a second `--` gives the client its own
# (T11.12: two platforms' builds on one machine, e.g. `-- wine
# export/windows/IsoDoom.exe -- export/linux/IsoDoom.x86_64`, a
# Windows host under Wine and a Linux client). Paths given to a Windows build
# are made Windows paths: `cygpath -m` under Git Bash (T11.6a), `Z:` + the
# path under Wine. Bash 3.2 (macOS's) runs it, and no `timeout` is needed (a
# watchdog of its own stops a game after the mode's limit). The host listens on a free port of
# 127.0.0.1 only (`-host 0 -bind 127.0.0.1`: ENet picks it, the host prints
# it). Logs go to $NET_LOOPBACK_LOGS (default a temporary directory, kept on
# a failure). Exits 0 when both runs agree.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
if [ $# -lt 1 ] || [ "${1#-}" != "$1" ]; then
    sed -n '2,43p' "$0" | sed 's/^# \{0,1\}//' >&2
    exit 2
fi
iwad=$(realpath "$1"); shift
tics=1000 route='' next='' mode=exchange victim=''
if [ $# -gt 0 ] && [ "$1" = "--play" ]; then
    [ $# -ge 3 ] || { echo "loopback: --play ROUTE NEXT" >&2; exit 2; }
    mode=play route=$(realpath "$2") next=$3; shift 3
elif [ $# -gt 0 ] && [ "$1" = "--kill" ]; then
    if [ $# -lt 3 ] || { [ "$2" != client ] && [ "$2" != host ]; }; then echo "loopback: --kill client|host ROUTE" >&2; exit 2; fi
    mode=kill victim=$2 route=$(realpath "$3"); shift 3
elif [ $# -gt 0 ] && [ "$1" = "--desync" ]; then
    [ $# -ge 2 ] || { echo "loopback: --desync ROUTE" >&2; exit 2; }
    mode=desync route=$(realpath "$2"); shift 2
elif [ $# -gt 0 ] && [ "$1" != "--" ]; then
    tics=$1; shift
fi
host_game=(godot)
if [ $# -gt 0 ] && [ "$1" = "--" ]; then
    shift
    host_game=()
    while [ $# -gt 0 ] && [ "$1" != "--" ]; do host_game+=("$1"); shift; done
    [ ${#host_game[@]} -gt 0 ] || host_game=(godot)
fi
client_game=("${host_game[@]}")
if [ $# -gt 0 ] && [ "$1" = "--" ]; then shift; [ $# -gt 0 ] && client_game=("$@"); fi
# A path as the game reads it: a Windows build (an .exe) takes a Windows path,
# made with cygpath under Git Bash or as Wine's drive Z: under Wine; others
# take it as it is. The first word of a game command says which.
native() { # GAME_FIRST_WORD PATH
    case "$1" in
        wine|wine64|*/wine|*/wine64) echo "Z:$2" ;;
        *.exe) if command -v cygpath >/dev/null; then cygpath -m "$2"; else echo "$2"; fi ;;
        *) echo "$2" ;;
    esac
}
# Stop process PID (a game) at once: on Windows (Git Bash) through taskkill,
# since a native process does not take the signal; the console wrapper's job
# object takes the game with it.
stop() { # PID [SIGNAL]
    local winpid
    if [ -r "/proc/$1/winpid" ] && command -v taskkill >/dev/null; then
        winpid=$(cat "/proc/$1/winpid")
        taskkill //F //T //PID "$winpid" >/dev/null 2>&1 || true
    fi
    kill "-${2:-KILL}" "$1" 2>/dev/null || true
}
# The watchdog: TERM process PID after LIMIT seconds (KILL 5 s later); it
# returns within a second of the process ending by itself.
watchdog() { # PID LIMIT
    local t=0
    while kill -0 "$1" 2>/dev/null; do
        if [ "$t" -ge "$2" ]; then
            echo "loopback: process $1 ran over ${2}s: stopped" >&2
            stop "$1" TERM; sleep 5; stop "$1" KILL
            return
        fi
        sleep 1; t=$((t + 1))
    done
}
logs=${NET_LOOPBACK_LOGS:-$(mktemp -d)}
mkdir -p "$logs"
logs=$(realpath "$logs")
host_first=${host_game[0]} client_first=${client_game[0]}
host_log=$logs/host.log client_log=$logs/client.log
host_args=(--level) client_args=()
start="net; netwait 2"
if [ -n "$route" ]; then
    # the route's header: its map, skill, monsters and modes are the host's game
    header() { sed -n "s/^$1 \(.*\)/\1/p" "$route" | head -n 1; }
    map=$(header map) skill=$(header skill) deathmatch=$(header deathmatch) timer=$(header timer)
    host_args=(--level "${map:-E1M1}" --level-skill="${skill:-3}" --level-tweaks=vanilla)
    if grep -q '^monsters' "$route"; then host_args+=(--level-monsters=on); else host_args+=(--level-monsters=off); fi
    case "${deathmatch:-0}" in 1) host_args+=(-deathmatch) ;; 2) host_args+=(-altdeath) ;; esac
    [ -n "$timer" ] && host_args+=(-timer "$timer")
    route_tics=$(grep -cE '^-?[0-9]' "$route" || true)
    start="net; netwait 2; nettic 70; route ROUTE"
fi
# (the victim of --kill waits here to be killed; the marker is its own line)
doomed="tics 210; print; sleep 600"
case $mode in
    play)
        host_script="$start; until gamestate intermission 300; skip; until map $next 60; tics 70; netcheck; checksum; net"
        client_script=$host_script
        # the route (with its xcount lines, a longer run), the intermission and the start; at 35 Hz
        limit=$(( route_tics * 4 / 35 + 240 ))
        result_line='^Level script: netcheck:'
        ;;
    kill)
        host_args+=(-nettimeout 5) client_args+=(-nettimeout 5)
        if [ "$victim" = client ]; then
            host_script="$start; netuntil left 2 60; tics 70; netcheck; checksum; print"
            client_script="$start; $doomed"
            result_line='^(Level script: (netuntil|netcheck)|Net: (server: (peer|slot)|player))'
        else
            host_script="$start; $doomed"
            client_script="$start; netuntil ended 60; print"
            result_line='^(Level script: netuntil|Net: (client: (The host|disconnected)|the netgame ended))'
        fi
        limit=150
        ;;
    desync)
        host_args+=(--netdesync="$(native "$host_first" "$logs/desync")") client_args+=(--netdesync="$(native "$client_first" "$logs/desync")")
        host_script="$start; netuntil desync 60; print; sleep 3"
        client_script="$start; tics 140; netdesync; netuntil desync 60; print"
        result_line='^(Level script: netuntil|Net: (forced desync|desync))'
        limit=150
        ;;
    *)
        host_script="net; netexchange $tics"
        client_script=$host_script
        # a minute and a half for the run: about 30 s of 35 Hz tics and the starts
        limit=$(( tics / 35 + 90 ))
        result_line='^Level script: netexchange:'
        ;;
esac
# (the host waits a little after its check, so the client's ends first)
if [ "$mode" = play ]; then host_script="$host_script; sleep 3"; fi
# each side's paths as its build reads them
host_script=${host_script//ROUTE/$(native "$host_first" "$route")}
client_script=${client_script//ROUTE/$(native "$client_first" "$route")}
cd "$repo"
"${host_game[@]}" --headless -- -iwad "$(native "$host_first" "$iwad")" -host 0 -bind 127.0.0.1 -players 2 "${host_args[@]}" \
    --level-script="$host_script" >"$host_log" 2>&1 &
host_pid=$!
watchdog "$host_pid" "$limit" >/dev/null &
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
    stop "$host_pid"
    exit 1
fi
case $mode in
    play) what="playing $(basename "$route") into $next" ;;
    kill) what="playing $(basename "$route"), the $victim killed after 210 tics" ;;
    desync) what="playing $(basename "$route"), the client desyncing after 140 tics" ;;
    *) what="$tics tics" ;;
esac
echo "loopback: host on 127.0.0.1:$port, $what"
# (${a[@]+"${a[@]}"}: an empty array under `set -u` in bash 3.2)
"${client_game[@]}" --headless -- -iwad "$(native "$client_first" "$iwad")" -connect "127.0.0.1:$port" --level ${client_args[@]+"${client_args[@]}"} \
    --level-script="$client_script" >"$client_log" 2>&1 &
client_pid=$!
watchdog "$client_pid" "$limit" >/dev/null &
host_status=0 client_status=0
if [ "$mode" = kill ]; then
    # SIGKILL the victim once it played its tics
    if [ "$victim" = client ]; then victim_pid=$client_pid victim_log=$client_log; else victim_pid=$host_pid victim_log=$host_log; fi
    killed=
    for _ in $(seq 1 1200); do
        if grep -q '^Level script: sleep 600' "$victim_log"; then
            stop "$victim_pid"
            killed=yes
            echo "loopback: killed the $victim (SIGKILL)"
            break
        fi
        kill -0 "$victim_pid" 2>/dev/null || break
        sleep 0.1
    done
    if [ -z "$killed" ]; then
        echo "loopback: the $victim did not reach its tics (log: $victim_log)" >&2
        stop "$host_pid"; stop "$client_pid"
    fi
fi
wait "$host_pid" || host_status=$?
wait "$client_pid" || client_status=$?
ok=yes
case $mode in
    play)
        # both in sync (the host's server compared every window they ran: the worlds' checksums agreed)
        result() { sed -n 's/^Level script: netcheck: \(in sync\),.*/\1/p' "$1" | head -n 1; }
        host_result=$(result "$host_log") client_result=$(result "$client_log")
        [ "$host_status" -eq 0 ] && [ "$client_status" -eq 0 ] && [ -n "$host_result" ] && [ "$host_result" = "$client_result" ] || ok=
        ;;
    kill)
        if [ "$victim" = client ]; then
            survivor_status=$host_status
            host_result=$(sed -n 's/^Level script: netcheck: \(in sync\),.*/\1/p' "$host_log" | head -n 1)
            client_result="killed (exit $client_status)"
            grep -q '^Level script: netuntil left: reached' "$host_log" || ok=
            grep -q 'left the game' "$host_log" || ok=
            [ "$host_result" = "in sync" ] || ok=
        else
            survivor_status=$client_status
            host_result="killed (exit $host_status)"
            client_result=$(sed -n 's/^Level script: netuntil ended: \(reached\): \(.*\);.*/\1: \2/p' "$client_log" | head -n 1)
            [ -n "$client_result" ] || ok=
        fi
        [ -n "${killed:-}" ] && [ "$survivor_status" -eq 0 ] || ok=
        ;;
    desync)
        result() { sed -n 's/^Level script: netuntil desync: reached: \(desync in window [0-9]*\).*/\1/p' "$1" | head -n 1; }
        host_result=$(result "$host_log") client_result=$(result "$client_log")
        [ "$host_status" -eq 0 ] && [ "$client_status" -eq 0 ] && [ -n "$host_result" ] && [ "$host_result" = "$client_result" ] || ok=
        # each machine's dumps: summary, checksums, commands and the last agreed save
        dumps=0
        for dir in "$logs"/desync/*-player*; do
            [ -d "$dir" ] || continue
            for f in summary.txt checksums.txt commands.txt; do [ -s "$dir/$f" ] || ok=; done
            compgen -G "$dir/agreed-*.dsg" >/dev/null || ok=
            dumps=$((dumps + 1))
        done
        [ "$dumps" -eq 2 ] || ok=
        echo "loopback: $dumps dump directories in $logs/desync"
        ;;
    *)
        result() { sed -n 's/^Level script: netexchange: \([0-9]* of [0-9]* tics, hash [0-9a-f]*\).*/\1/p' "$1" | head -n 1; }
        host_result=$(result "$host_log") client_result=$(result "$client_log")
        [ "$host_status" -eq 0 ] && [ "$client_status" -eq 0 ] && [ -n "$host_result" ] && [ "$host_result" = "$client_result" ] || ok=
        ;;
esac
echo "loopback: host:   ${host_result:-no result} (exit $host_status)"
echo "loopback: client: ${client_result:-no result} (exit $client_status)"
grep -hE "$result_line" "$host_log" "$client_log" || true
if [ -z "$ok" ]; then
    echo "loopback: FAILED (logs: $logs)" >&2
    for f in "$host_log" "$client_log"; do echo "--- $f" >&2; grep -E '^(Net|Level script|ERROR)|rror' "$f" | tail -n 40 >&2 || true; done
    exit 1
fi
echo "loopback: OK"
[ -n "${NET_LOOPBACK_LOGS:-}" ] || rm -rf "$logs"
