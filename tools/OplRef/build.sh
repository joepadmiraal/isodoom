#!/bin/bash
# Builds the OPL reference harness (T7.8a; a dev tool, never shipped) into
# DIR (default ~/.cache/isodoom/opl-ref): opl_ref.c on Nuked-OPL3
# (LGPL-2.1-or-later) at the pinned upstream commit, and opl_ref_choco on
# Chocolate Doom's opl/opl3.c (Nuked-OPL3-fast, bit-exact to that commit) at
# its pinned commit, so check.sh can show both give the same samples.
# Prints the harness's path. Needs git, curl and a C compiler.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
dir=${1:-$HOME/.cache/isodoom/opl-ref}
nuked_commit=cfedb09efc03f1d7b5fc1f04dd449d77d8c49d50
choco_commit=895f581c5d91497bdda0516612da803fe5843e28
mkdir -p "$dir"
if [ ! -d "$dir/Nuked-OPL3/.git" ]; then
    git clone -q https://github.com/nukeykt/Nuked-OPL3.git "$dir/Nuked-OPL3"
fi
git -C "$dir/Nuked-OPL3" checkout -q "$nuked_commit"
mkdir -p "$dir/chocolate"
for f in opl3.c opl3.h wf_rom.h; do
    if [ ! -s "$dir/chocolate/$f" ] || [ "$(cat "$dir/chocolate/.commit" 2>/dev/null)" != "$choco_commit" ]; then
        curl -fsSL "https://raw.githubusercontent.com/chocolate-doom/chocolate-doom/$choco_commit/opl/$f" -o "$dir/chocolate/$f"
    fi
done
echo "$choco_commit" > "$dir/chocolate/.commit"
cc -O2 -std=c99 -w -I"$dir/Nuked-OPL3" "$here/opl_ref.c" "$dir/Nuked-OPL3/opl3.c" -o "$dir/opl_ref"
cc -O2 -std=c99 -w -I"$dir/chocolate" "$here/opl_ref.c" "$dir/chocolate/opl3.c" -o "$dir/opl_ref_choco"
echo "$dir/opl_ref"
