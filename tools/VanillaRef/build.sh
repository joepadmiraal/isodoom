#!/bin/bash
# Builds the vanilla reference renderer (dump.c, T2.9) into DIR (default
# ~/.cache/isodoom/vanilla-ref): doomgeneric (GPL-2.0, a Chocolate Doom based
# port with vanilla's renderer) at a pinned commit, ref.patch, no window or
# sound. Prints the binary's path. Needs git, make and a C compiler.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
dir=${1:-$HOME/.cache/isodoom/vanilla-ref}
commit=dcb7a8dbc7a16ce3dda29382ac9aae9d77d21284
if [ ! -d "$dir/.git" ]; then
    git clone -q https://github.com/ozkl/doomgeneric.git "$dir"
fi
cd "$dir"
git checkout -q "$commit"
git checkout -q -- .
git apply "$here/ref.patch"
cp "$here/dump.c" doomgeneric/doomgeneric_dump.c
cd doomgeneric
sed -e 's/doomgeneric_xlib.o/doomgeneric_dump.o/' -e 's/-lX11//' -e 's/^CC=clang.*/CC=cc/' \
    -e 's/-DSNDSERV/-DSNDSERV -DDOOMGENERIC_RESX=320 -DDOOMGENERIC_RESY=200 -w/' Makefile > Makefile.dump
make -s -f Makefile.dump -j"$(nproc)" >/dev/null
echo "$dir/doomgeneric/doomgeneric"
