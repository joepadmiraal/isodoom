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
#    game tic 70 on; both run its events; a route whose first 70 tics are
#    idle, with no event, plays from its tic 70 instead, `route ROUTE from=70`,
#    so its game is the netgame's from the first tic: coop-e1m1-client-exit,
#    whose fights depend on every tic's randomness), the host in the route's map, skill,
#    monsters and modes, through the intermission (skipped) into map NEXT;
#    then the host's server must have compared every checksum window with no
#    desync, and the client must have seen no consistency failure. T11.12b:
#    the host changes the input delay twice on the way (`netdelay 8` about
#    35 tics into the route, `netdelay 4` 105 tics later), as the adaptive
#    delay does on a link that slows and recovers.
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
#    of the last agreed window; then each shows the stop's message, and a key
#    takes it back to the title loop (the client first, the host 3 s later).
#  - --refuse (P11): the host waits in its lobby; the client, on its own PWAD
#    list (--client-file), joins it through the menus (Multiplayer, Join Game,
#    the address typed) and must be refused for its PWADs, the refusal shown
#    as a message over the join page and the join page back after a key.
#  - --lan [TICS] (T11.9d): the exchange, but the client finds the host by LAN
#    discovery instead of -connect: its Join Game browser (the level script's
#    `lan 1 30 join`) queries 127.0.0.1 and the broadcast addresses on the
#    discovery port (5028, which must be free here: the host answers on it,
#    bound to 127.0.0.1) and joins the game it lists.
# PWADs (P11): `--file PWAD` (repeated, in load order) loads them on both
# machines as -file; `--client-file PWAD` (repeated; `-` for none) gives the
# client its own list instead. --play and --kill and --desync without --file take the route's
# `pwad SET` header's list (tools/VanillaRef/pwads.py). They go before the mode.
# Usage:
#   tools/NetLoopback/loopback.sh IWAD [--file PWAD...] [--client-file PWAD...] MODE...
#   tools/NetLoopback/loopback.sh IWAD [TICS] [-- GAME... [-- CLIENT GAME...]]
#   tools/NetLoopback/loopback.sh IWAD --refuse [-- GAME... [-- CLIENT GAME...]]
#   tools/NetLoopback/loopback.sh IWAD --lan [TICS] [-- GAME... [-- CLIENT GAME...]]
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
    sed -n '2,63p' "$0" | sed 's/^# \{0,1\}//' >&2
    exit 2
fi
iwad=$(realpath "$1"); shift
tics=1000 route='' next='' mode=exchange victim=''
# (P11: the PWADs, both machines' and the client's own; bash 3.2 arrays)
files=() client_files=() client_own=
while [ $# -gt 0 ]; do
    case "$1" in
        --file) [ $# -ge 2 ] || { echo "loopback: --file PWAD" >&2; exit 2; }; files+=("$(realpath "$2")"); shift 2 ;;
        --client-file)
            [ $# -ge 2 ] || { echo "loopback: --client-file PWAD|-" >&2; exit 2; }
            client_own=yes
            [ "$2" = - ] || client_files+=("$(realpath "$2")")
            shift 2 ;;
        *) break ;;
    esac
done
if [ $# -gt 0 ] && [ "$1" = "--refuse" ]; then
    mode=refuse; shift
    [ -n "$client_own" ] || { echo "loopback: --refuse needs the client's own list (--client-file)" >&2; exit 2; }
elif [ $# -gt 0 ] && [ "$1" = "--play" ]; then
    [ $# -ge 3 ] || { echo "loopback: --play ROUTE NEXT" >&2; exit 2; }
    mode=play route=$(realpath "$2") next=$3; shift 3
elif [ $# -gt 0 ] && [ "$1" = "--kill" ]; then
    if [ $# -lt 3 ] || { [ "$2" != client ] && [ "$2" != host ]; }; then echo "loopback: --kill client|host ROUTE" >&2; exit 2; fi
    mode=kill victim=$2 route=$(realpath "$3"); shift 3
elif [ $# -gt 0 ] && [ "$1" = "--desync" ]; then
    [ $# -ge 2 ] || { echo "loopback: --desync ROUTE" >&2; exit 2; }
    mode=desync route=$(realpath "$2"); shift 2
elif [ $# -gt 0 ] && [ "$1" = "--lan" ]; then
    mode=lan; shift
    if [ $# -gt 0 ] && [ "$1" != "--" ]; then tics=$1; shift; fi
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
    # (an empty path, the exchange's route, stays empty: cygpath refuses it)
    [ -n "$2" ] || { echo; return; }
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
    from=""
    # (routes.py's parse: the first 70 tics idle for every player, and no route events)
    if command -v python3 >/dev/null && PYTHONDONTWRITEBYTECODE=1 python3 - "$repo/tools/VanillaRef" "$route" <<'PY'
import sys
sys.path.insert(0, sys.argv[1])
import routes
head, cmds = routes.parse(sys.argv[2])
idle = len(cmds) > 70 and all(all(v == 0 for c in ([t] if isinstance(t[0], int) else t) for v in c) for t in cmds[:70])
sys.exit(0 if idle and not head['events'] else 1)
PY
    then from=" from=70"; fi
    start="net; netwait 2; nettic 70; route ROUTE$from"
    # P11: a route on a PWAD set (its `pwad SET` header) loads the set on both machines, unless --file says otherwise
    pwad=$(header pwad)
    if [ -n "$pwad" ] && [ ${#files[@]} -eq 0 ] && [ -z "$client_own" ]; then
        set_files=$(python3 -I "$repo/tools/VanillaRef/pwads.py" "$pwad") || { echo "loopback: the route's PWAD set $pwad is missing" >&2; exit 1; }
        first=yes
        while IFS= read -r f; do
            if [ -n "$first" ]; then first=; continue; fi # (its IWAD: the one given)
            files+=("$f")
        done <<EOF2
$set_files
EOF2
    fi
fi
[ -n "$client_own" ] || client_files=(${files[@]+"${files[@]}"})
# each side's -file list, its paths as its build reads them
host_files=() client_file_args=()
if [ ${#files[@]} -gt 0 ]; then
    host_files=(-file)
    for f in "${files[@]}"; do host_files+=("$(native "$host_first" "$f")"); done
fi
if [ ${#client_files[@]} -gt 0 ]; then
    client_file_args=(-file)
    for f in "${client_files[@]}"; do client_file_args+=("$(native "$client_first" "$f")"); done
fi
client_level=(--level)
# (the victim of --kill waits here to be killed; the marker is its own line)
doomed="tics 210; print; sleep 600"
case $mode in
    play)
        # T11.12b: the host moves the delay mid-route (up to 8, then down to 4: what the adaptive delay does on a
        # slow link), and the game must stay in sync and the route's commands on their tics
        client_script="$start; until gamestate intermission 300; skip; until map $next 60; tics 70; netcheck; checksum; net"
        host_script="$start; tics 105; netdelay 8; tics 105; netdelay 4; until gamestate intermission 300; skip; until map $next 60; tics 70; netcheck; checksum; net"
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
        host_script="$start; netuntil desync 60; print; menu message; sleep 3; tap Space; netuntil ended 10"
        client_script="$start; tics 140; netdesync; netuntil desync 60; print; menu message; tap Space; netuntil ended 10"
        result_line='^(Level script: netuntil|Net: (forced desync|desync|the stopped netgame))'
        limit=150
        ;;
    refuse)
        # the host waits in its lobby (stopped once the client is done); the client joins through the menus
        host_script="net; sleep 600"
        client_script="tap Escape; tap M; tap Enter; tap J; tap Enter; menu join; tap Enter; type 127.0.0.1:PORT; tap Enter; sleep 4; menu message; tap Space; menu join; net"
        client_level=()
        limit=90
        result_line='^(Level script: menu: (message|join)|Net: (server: peer .* refused|client: refused))'
        ;;
    lan)
        host_script="net; netexchange $tics"
        client_script="lan 1 30 join; net; netexchange $tics"
        limit=$(( tics / 35 + 120 ))
        result_line='^(Level script: (netexchange|lan):|Net: visible on the LAN)'
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
"${host_game[@]}" --headless -- -iwad "$(native "$host_first" "$iwad")" ${host_files[@]+"${host_files[@]}"} -host 0 -bind 127.0.0.1 -players 2 "${host_args[@]}" \
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
    lan) what="$tics tics, the client finding the host on the LAN" ;;
    refuse) what="the client joining on other PWADs" ;;
    *) what="$tics tics" ;;
esac
connect=(-connect "127.0.0.1:$port")
case $mode in lan|refuse) connect=() ;; esac
client_script=${client_script//PORT/$port}
[ ${#files[@]} -eq 0 ] || echo "loopback: host PWADs: ${files[*]}"
[ -z "$client_own" ] || echo "loopback: client PWADs: ${client_files[*]:-none}"
echo "loopback: host on 127.0.0.1:$port, $what"
# (${a[@]+"${a[@]}"}: an empty array under `set -u` in bash 3.2)
"${client_game[@]}" --headless -- -iwad "$(native "$client_first" "$iwad")" ${client_file_args[@]+"${client_file_args[@]}"} ${connect[@]+"${connect[@]}"} \
    ${client_level[@]+"${client_level[@]}"} ${client_args[@]+"${client_args[@]}"} \
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
if [ "$mode" = refuse ]; then
    # the host waits for nothing more once the client is done
    wait "$client_pid" || client_status=$?
    stop "$host_pid" TERM
    wait "$host_pid" 2>/dev/null || true
    client_pid=
fi
wait "$host_pid" 2>/dev/null || host_status=$?
[ -z "$client_pid" ] || wait "$client_pid" || client_status=$?
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
        # each machine back at the title loop after the stop's message (the user's decision)
        for log in "$host_log" "$client_log"; do
            grep -q '^Level script: netuntil ended: reached' "$log" || ok=
        done
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
    refuse)
        # the host refused the hello for its PWADs, and the client's join page showed it
        host_result=$(sed -n 's/^Net: server: peer [0-9]* refused: \(PwadMismatch\):.*/\1/p' "$host_log" | head -n 1)
        client_result=$(sed -n 's/^Level script: menu: message "\(.*\)"$/\1/p' "$client_log" | head -n 1)
        host_status=0 # (stopped by us)
        [ "$client_status" -eq 0 ] && [ "$host_result" = PwadMismatch ] || ok=
        case "$client_result" in *"other pwads"*) ;; *) ok= ;; esac
        # back on the join page after the message's key, no session left
        [ "$(grep -c '^Level script: menu: join' "$client_log")" -ge 2 ] || ok=
        ;;
    *)
        result() { sed -n 's/^Level script: netexchange: \([0-9]* of [0-9]* tics, hash [0-9a-f]*\).*/\1/p' "$1" | head -n 1; }
        host_result=$(result "$host_log") client_result=$(result "$client_log")
        [ "$host_status" -eq 0 ] && [ "$client_status" -eq 0 ] && [ -n "$host_result" ] && [ "$host_result" = "$client_result" ] || ok=
        # --lan: the host answered on the discovery port and the client joined the game it found there
        if [ "$mode" = lan ]; then
            grep -q "^Level script: lan: joining .* at 127\.0\.0\.1:$port\$" "$client_log" || ok=
        fi
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
