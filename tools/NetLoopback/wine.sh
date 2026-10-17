#!/usr/bin/env bash
# T11.12: the pinned Wine that runs the Windows export on Linux for the
# cross-platform loopback (a Windows host and a Linux client on one machine,
# and the other way round; CI's test-cross job). Downloads Kron4ek's
# Wine-Builds release (vanilla Wine, amd64 in WoW64 mode: 64-bit only, no
# 32-bit libraries needed) once into $ISODOOM_WINE_DIR (default
# ~/.cache/isodoom/wine), checks its SHA-256, and prints the path of its
# `wine`. No root needed. A dev tool, never shipped (Wine is LGPL-2.1).
# Usage:
#   wine=$(tools/NetLoopback/wine.sh)
#   WINEPREFIX=/tmp/prefix WINEDEBUG=-all tools/NetLoopback/loopback.sh IWAD 1000 \
#       -- "$wine" export/windows/IsoDoom.exe -- export/linux/IsoDoom.x86_64
# Run IsoDoom.exe itself under Wine, not IsoDoom.console.exe: Wine gives the
# game the shell's stdout, and the console wrapper outlived the game after a
# networked run (it never exited).
set -euo pipefail
version=11.19
sha256=a1fe858b45d12c9b16b91c7601d9b934f68430f7b6b95e240212ab6e2a198d8a
name=wine-$version-amd64-wow64
dir=${ISODOOM_WINE_DIR:-$HOME/.cache/isodoom/wine}
if [ ! -x "$dir/$name/bin/wine" ]; then
    mkdir -p "$dir"
    curl -fsSL --retry 3 -o "$dir/$name.tar.xz" \
        "https://github.com/Kron4ek/Wine-Builds/releases/download/$version/$name.tar.xz"
    echo "$sha256  $dir/$name.tar.xz" | sha256sum -c - >&2
    tar -xJf "$dir/$name.tar.xz" -C "$dir"
    rm "$dir/$name.tar.xz"
fi
echo "$dir/$name/bin/wine"
