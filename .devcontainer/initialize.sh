#!/bin/sh
# Runs on the host before the container starts (devcontainer.json's
# initializeCommand). It makes sure every bind-mount source exists, so the
# container also starts without a desktop session (e.g. over SSH), while only
# the Wayland and PulseAudio sockets reach the container, not the rest of the
# host's runtime dir (D-Bus, the GPG and SSH agents, ...).
set -eu

run="${XDG_RUNTIME_DIR:?XDG_RUNTIME_DIR is not set}"
stage="$run/isodoom-devcontainer"
[ -d "$stage" ] || mkdir -m 0700 "$stage"

# Wayland: a hard link to the compositor's socket, mounted as a single file
# (a bind source must exist, the socket does not without a session). Without
# a socket the link is an empty file, which the `godot` wrapper's -S test
# skips (Godot then runs headless or under xvfb-run only). A restarted compositor leaves the link on the
# old socket: run `devcontainer up` (or reopen the container) again.
rm -f "$stage/wayland"
case "${WAYLAND_DISPLAY:-}" in
  "") sock="" ;;
  /*) sock="$WAYLAND_DISPLAY" ;;
  *) sock="$run/$WAYLAND_DISPLAY" ;;
esac
if [ -n "$sock" ] && [ -S "$sock" ]; then
  ln "$sock" "$stage/wayland"
else
  : > "$stage/wayland"
fi

# PulseAudio (PipeWire's pulse/native): the directory is mounted, so a sound
# server started (or restarted) after the container still shows up in it.
[ -d "$run/pulse" ] || mkdir -m 0700 "$run/pulse"
