#!/bin/bash
# Plays every log of tools/OplRef/logs through the reference harnesses
# (build.sh: upstream Nuked-OPL3 and Chocolate Doom's copy) and checks their
# samples' SHA-256 against logs/SHA256SUMS, which Opl3Tests checks the C#
# port against (T7.8a). With --update, writes SHA256SUMS from the upstream
# harness instead (after checking both harnesses agree).
# Needs what build.sh needs.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
dir=${ISODOOM_OPL_REF:-$HOME/.cache/isodoom/opl-ref}
update=0
[ "${1:-}" = "--update" ] && update=1
"$here/build.sh" "$dir" >/dev/null
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
sums="$here/logs/SHA256SUMS"
status=0
: > "$tmp/new"
for log in "$here"/logs/*.log; do
    name=$(basename "$log" .log)
    "$dir/opl_ref" "$log" "$tmp/$name.raw"
    "$dir/opl_ref_choco" "$log" "$tmp/$name.choco.raw"
    a=$(sha256sum < "$tmp/$name.raw" | cut -d' ' -f1)
    b=$(sha256sum < "$tmp/$name.choco.raw" | cut -d' ' -f1)
    if [ "$a" != "$b" ]; then
        echo "$name: Chocolate Doom's opl3.c differs from upstream ($b, $a)"
        status=1
    fi
    echo "$a  $name" >> "$tmp/new"
    if [ $update = 0 ]; then
        want=$(awk -v n="$name" '$2 == n { print $1 }' "$sums" 2>/dev/null || true)
        if [ "$want" != "$a" ]; then
            echo "$name: $a, SHA256SUMS has ${want:-nothing}"
            status=1
        else
            echo "$name: ok ($(($(stat -c %s "$tmp/$name.raw") / 4)) samples)"
        fi
    fi
done
if [ $update = 1 ] && [ $status = 0 ]; then
    cp "$tmp/new" "$sums"
    cat "$sums"
fi
exit $status
