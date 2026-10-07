#!/bin/bash
# OPL music references (T7.8d; a dev tool, never shipped): plays songs
# through Chocolate Doom's own i_oplmusic.c, mus2mid.c, midifile.c and opl/
# (music_ref, built by build.sh) and writes each run's register-write log
# (opl_ref.c's format, so check.sh and Opl3Tests replay it too; its first
# line holds music_ref's options, which OplMusicTests replays through
# OplMusic) and the SHA-256 of its samples.
#
# Usage:
#   music.sh            the synthetic IWAD's runs (tools/SyntheticIwad, non-id
#                       content): tools/OplRef/logs/music-*.log, their lines
#                       in logs/SHA256SUMS (commit both)
#   music.sh WAD [DIR]  every D_* lump of WAD (DOOM1.WAD, doom2.wad, ...):
#                       DIR/D_NAME.log, the whole song and 10 s more, the chip
#                       not clocked (-nochip: the writes only), and for a few
#                       songs DIR/D_NAME.48k.log and .sha256, a minute with
#                       the samples. DIR defaults to
#                       $ISODOOM_OPL_MUSIC/NAME (~/.cache/isodoom/opl-music/
#                       doom1 for DOOM1.WAD). WAD-derived: never commit them.
# Needs what build.sh needs, python3 and (synthetic) the .NET SDK.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
ref=${ISODOOM_OPL_REF:-$HOME/.cache/isodoom/opl-ref}
"$here/build.sh" "$ref" >/dev/null
music_ref="$ref/music_ref"
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

# run NAME OUTDIR OPTIONS... : OUTDIR/NAME.log and the samples' hash on stdout
run() {
    local name=$1 out=$2
    shift 2
    "$music_ref" "$@" "$wad" "$out/$name.log" "$tmp/$name.raw" >"$tmp/$name.out" 2>"$tmp/$name.err" || { cat "$tmp/$name.err"; exit 1; }
    if grep -q "refused" "$tmp/$name.err"; then
        cat "$tmp/$name.err" >&2
    fi
    # The log replays through the chip harness to the same samples.
    "$ref/opl_ref" "$out/$name.log" "$tmp/$name.replay.raw"
    if ! cmp -s "$tmp/$name.raw" "$tmp/$name.replay.raw"; then
        echo "$name: the log does not replay to music_ref's samples" >&2
        exit 1
    fi
    sha256sum < "$tmp/$name.raw" | cut -d' ' -f1
}

if [ $# -eq 0 ]; then
    wad="$tmp/synthetic.wad"
    dotnet run --project "$here/../SyntheticIwad" -- "$wad" >/dev/null
    logs="$here/logs"
    sums="$logs/SHA256SUMS"
    declare -A runs=(
        [music-e1m1]="-rate 48000 -block 512 -volume 127 -length 432000 -at 0 play:D_E1M1:loop"
        [music-controls]="-rate 44100 -block 1024 -reverse -length 396900 -at 0 play:D_E1M1:loop -at 44100 volume:40 -at 88200 pause -at 110250 resume -at 132300 volume:127 -at 176400 play:D_INTRO:once -at 308700 play:D_E1M1:once -at 330750 pause -at 352800 play:D_INTRO:loop -at 385875 stop"
        [music-intro-opl2]="-opl2 -rate 49716 -block 300 -volume 100 -length 298296 -at 0 play:D_INTRO:loop"
        [music-doom1-1666]="-ver doom1-1.666 -rate 44100 -length 264600 -at 0 play:D_E1M1:loop"
        [music-doom2-1666-opl2]="-ver doom2-1.666 -opl2 -rate 48000 -length 288000 -at 0 play:D_E1M1:loop"
    )
    for name in $(printf '%s\n' "${!runs[@]}" | sort); do
        # shellcheck disable=SC2086
        sum=$(run "$name" "$logs" ${runs[$name]})
        grep -v "  $name\$" "$sums" > "$tmp/sums" || true
        echo "$sum  $name" >> "$tmp/sums"
        sort -k2 "$tmp/sums" > "$sums"
        echo "$name: $(grep -c '^[0-9]' "$logs/$name.log") writes, $sum"
    done
    exit 0
fi

wad=$1
base=$(basename "$wad")
base=${base%.*}
out=${2:-${ISODOOM_OPL_MUSIC:-$HOME/.cache/isodoom/opl-music}/${base,,}}
mkdir -p "$out"
# The song lumps and their lengths in seconds (MUS: the delays at 140 Hz;
# anything else: 5 minutes).
python3 -I - "$wad" > "$tmp/songs" <<'EOF'
import struct, sys
d = open(sys.argv[1], 'rb').read()
n, ofs = struct.unpack_from('<ii', d, 4)
songs = {}
for i in range(n):
    pos, size, name = struct.unpack_from('<ii8s', d, ofs + 16 * i)
    name = name.rstrip(b'\0').decode('ascii', 'replace').upper()
    if name.startswith('D_'):
        songs[name] = d[pos:pos + size]
for name, lump in sorted(songs.items()):
    secs = 300
    if lump[:4] == b'MUS\x1a':
        start = struct.unpack_from('<H', lump, 6)[0]
        p, tics = start, 0
        try:
            while True:
                ev = lump[p]; p += 1
                kind = (ev >> 4) & 7
                if kind == 6:
                    break
                p += {0: 1, 1: 1, 2: 1, 3: 1, 4: 2}.get(kind, 0)
                if kind == 1 and lump[p - 1] & 0x80:
                    p += 1
                if ev & 0x80:
                    t = 0
                    while True:
                        b = lump[p]; p += 1
                        t = t * 128 + (b & 0x7f)
                        if not b & 0x80:
                            break
                    tics += t
            secs = tics // 140 + 1
        except IndexError:
            pass
    print(name, secs + 10)
EOF
while read -r name secs; do
    "$music_ref" -nochip -rate 44100 -block 512 -length $((secs * 44100)) -at 0 "play:$name:loop" \
        "$wad" "$out/$name.log" - >"$tmp/out" 2>"$tmp/err" || { cat "$tmp/err"; exit 1; }
    if grep -q "refused" "$tmp/err"; then cat "$tmp/err" >&2; fi
    echo "$name: $(grep -c '^[0-9]' "$out/$name.log") writes in $secs s"
    case "$name" in
        D_E1M1|D_INTRO|D_INTROA|D_RUNNIN|D_DM2TTL|D_E2M1)
            sum=$(run "$name.48k" "$out" -rate 48000 -block 1024 -volume 100 -length $((60 * 48000)) -at 0 "play:$name:loop")
            echo "$sum" > "$out/$name.48k.sha256"
            echo "$name.48k: $(grep -c '^[0-9]' "$out/$name.48k.log") writes, $sum"
            ;;
    esac
done < "$tmp/songs"
