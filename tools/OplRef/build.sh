#!/bin/bash
# Builds the OPL reference harness (T7.8a; a dev tool, never shipped) into
# DIR (default ~/.cache/isodoom/opl-ref): opl_ref.c on Nuked-OPL3
# (LGPL-2.1-or-later) at the pinned upstream commit, and opl_ref_choco on
# Chocolate Doom's opl/opl3.c (Nuked-OPL3-fast, bit-exact to that commit) at
# its pinned commit, so check.sh can show both give the same samples.
# T7.8d adds music_ref: music_ref.c on Chocolate Doom's own i_oplmusic.c,
# mus2mid.c, midifile.c, memio.c and opl/ (opl.c, opl_sdl.c, opl_queue.c,
# opl3.c) at the same commit, with SDL stubbed (stub/), for music.sh.
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
# T7.8d: the music driver's sources (GPL-2.0-or-later), into chocolate-src/.
src="$dir/chocolate-src"
mkdir -p "$src/src" "$src/opl"
for f in src/i_oplmusic.c src/mus2mid.c src/mus2mid.h src/midifile.c src/midifile.h \
         src/memio.c src/memio.h src/i_sound.h src/doomtype.h src/d_mode.h \
         opl/opl.c opl/opl.h opl/opl_internal.h opl/opl_sdl.c opl/opl_queue.c opl/opl_queue.h; do
    if [ ! -s "$src/$f" ] || [ "$(cat "$src/.commit" 2>/dev/null)" != "$choco_commit" ]; then
        curl -fsSL "https://raw.githubusercontent.com/chocolate-doom/chocolate-doom/$choco_commit/$f" -o "$src/$f"
    fi
done
echo "$choco_commit" > "$src/.commit"
inc=(-I"$here/stub" -I"$src/src" -I"$src/opl" -I"$dir/chocolate")
obj="$dir/music_ref.obj"
mkdir -p "$obj"
cc -O2 -std=gnu99 -w "${inc[@]}" -DOPL3_WriteRegBuffered=ref_WriteRegBuffered \
    -DOPL3_GenerateStream=ref_GenerateStream -c "$src/opl/opl_sdl.c" -o "$obj/opl_sdl.o"
for f in src/i_oplmusic.c src/mus2mid.c src/midifile.c src/memio.c opl/opl.c opl/opl_queue.c; do
    cc -O2 -std=gnu99 -w "${inc[@]}" -c "$src/$f" -o "$obj/$(basename "$f" .c).o"
done
cc -O2 -std=gnu99 -w "${inc[@]}" -c "$dir/chocolate/opl3.c" -o "$obj/opl3.o"
cc -O2 -std=gnu99 -w "${inc[@]}" -c "$here/music_ref.c" -o "$obj/music_ref.o"
cc "$obj"/*.o -o "$dir/music_ref"
echo "$dir/opl_ref"
