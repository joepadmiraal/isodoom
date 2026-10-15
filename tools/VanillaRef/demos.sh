#!/bin/bash
# The vanilla demo reference (T6.13; dev tool). Usage:
#   tools/VanillaRef/demos.sh [--doom2|--tnt|--plutonia] [--gameversion V] [DEMO1 DEMO2 DEMO3]
# Plays each demo lump of DOOM1.WAD (wads/ or $ISODOOM_DOOM1_WAD; default
# all three) in the vanilla reference (build.sh; $VANILLA_REF or
# ~/.cache/isodoom/vanilla-ref) with -playdemo and writes its per-tic dump
# (dump.c, the routes' format: tools/VanillaRef/routes.sh) as demoN.vanilla
# to $ISODOOM_VANILLA_ROUTES (default ~/.cache/isodoom/vanilla-routes).
# T10.6: with --doom2, Doom II's IWAD's (wads/doom2.wad, wads/DOOM2.WAD or
# $ISODOOM_DOOM2_WAD) as doom2-demoN.vanilla, the reference's -gameversion
# picked by the demo's version byte (v1.666's IWAD has v1.666's demos).
# T10.7: with --tnt or --plutonia, Final Doom's IWAD (wads/tnt.wad or
# wads/TNT.WAD, $ISODOOM_TNT_WAD; likewise plutonia, $ISODOOM_PLUTONIA_WAD)
# as tnt-demoN.vanilla / plutonia-demoN.vanilla, played as Final Doom's
# executable (-gameversion final, Chocolate Doom's default for them).
# --gameversion V plays them as that executable instead (1.9, ultimate,
# final, final2), the dumps named PREFIX-V-demoN.vanilla (T10.7:
# --doom2 --gameversion final, DemoSyncTests' doom2-final-demoN).
# WAD-derived: never commit them. DemoSyncTests compares the sim with them.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
ref=${VANILLA_REF:-$HOME/.cache/isodoom/vanilla-ref}/doomgeneric/doomgeneric
refdir=$(dirname "$(dirname "$ref")")
if [ ! -x "$ref" ] || [ "$(cat "$here/ref.patch" "$here/dump.c" | sha256sum)" != "$(cat "$refdir/ref.stamp" 2>/dev/null)" ]; then
    ref=$("$here/build.sh" "$refdir")
fi
iwad=$(realpath "${ISODOOM_DOOM1_WAD:-$repo/wads/DOOM1.WAD}")
prefix=
if [ "${1:-}" = --doom2 ]; then
    shift
    iwad=${ISODOOM_DOOM2_WAD:-$repo/wads/doom2.wad}
    [ -f "$iwad" ] || iwad=$repo/wads/DOOM2.WAD
    iwad=$(realpath "$iwad")
    prefix=doom2-
elif [ "${1:-}" = --tnt ] || [ "${1:-}" = --plutonia ]; then
    game=${1#--}
    shift
    upper=$(echo "$game" | tr '[:lower:]' '[:upper:]')
    var=ISODOOM_${upper}_WAD
    iwad=${!var:-$repo/wads/$game.wad}
    [ -f "$iwad" ] || iwad=$repo/wads/$upper.WAD
    [ -f "$iwad" ] || { echo "No $upper.WAD: put it in wads/ or set $var" >&2; exit 1; }
    iwad=$(realpath "$iwad")
    prefix=$game-
    final=1
fi
forced=
if [ "${1:-}" = --gameversion ]; then
    forced=$2
    shift 2
    prefix=$prefix$forced-
fi
out=$(realpath -m "${ISODOOM_VANILLA_ROUTES:-$HOME/.cache/isodoom/vanilla-routes}")
mkdir -p "$out"
if [ $# -eq 0 ]; then set -- DEMO1 DEMO2 DEMO3; fi
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
for demo in "$@"; do
    name=$(echo "$demo" | tr '[:upper:]' '[:lower:]')
    dump="$out/$prefix$name.vanilla"
    rm -f "$dump"
    # The demo's version byte picks the reference's -gameversion (T10.6).
    version=$(python3 -I -c '
import struct, sys
d = open(sys.argv[1], "rb").read()
n, off = struct.unpack("<ii", d[4:12])
for i in range(n):
    o, s, name = struct.unpack("<ii8s", d[off + 16 * i:off + 16 * i + 16])
    if name.rstrip(b"\0").decode().upper() == sys.argv[2].upper():
        print({106: "1.666", 107: "1.7", 108: "1.8", 109: "1.9"}.get(d[o], "1.9"))
        break
' "$iwad" "$demo")
    # The lump by name (no NAME.lmp in the working directory); dump.c's
    # patched G_ReadDemoTiccmd exits at the demo's marker.
    if [ -n "${final:-}" ]; then version=final; fi
    if [ -n "$forced" ]; then version=$forced; fi
    (cd "$tmp" && env -u VIEWS -u DUMP_START -u DUMP_EVENTS DUMP_TICS="$dump" \
        "$ref" -iwad "$iwad" -gameversion "$version" -playdemo "$name" -nosound -nomusic -nogui >/dev/null)
    echo "$demo: $(wc -l < "$dump") tics -> $dump"
done
