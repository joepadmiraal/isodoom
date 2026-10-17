# Sourced by wi.sh and fi.sh (T11.8's follow-up; dev tool): the game scene's
# arguments for a route's game, from its header, as routes.py gives the
# vanilla reference the same game: `players N` (a netgame from 2: the level
# script's `route` plays every player's column under --level-players=N),
# `deathmatch 1|2` (-deathmatch, -altdeath), `timer MINUTES` (-timer) and
# `gameversion NAME` (-gameversion). Sets the array scene_args.
# shellcheck shell=bash
scene_args=()
scene_header() { sed -e 's/#.*//' "$1" | awk -v k="$2" '$1 == k { print $2; exit }'; }
scene_route_args() {
    local route=$1 players deathmatch timer version
    players=$(scene_header "$route" players)
    deathmatch=$(scene_header "$route" deathmatch)
    timer=$(scene_header "$route" timer)
    version=$(scene_header "$route" gameversion)
    scene_args=()
    if [ -n "$players" ] && [ "$players" -gt 1 ]; then scene_args+=(--level-players="$players"); fi
    case "${deathmatch:-0}" in
        0) ;;
        1) scene_args+=(-deathmatch) ;;
        2) scene_args+=(-altdeath) ;;
        *) echo "$route: deathmatch $deathmatch (0, 1 or 2)" >&2; return 1 ;;
    esac
    if [ -n "$timer" ]; then scene_args+=(-timer "$timer"); fi
    if [ -n "$version" ]; then scene_args+=(-gameversion "$version"); fi
}
