# Dev image for IsoDoom: .NET 10 SDK + Godot 4 (.NET build), with the
# libraries the editor needs to open a window on the host's Wayland/X11
# display, render through Mesa (Vulkan/GL) and play audio through PulseAudio.
FROM mcr.microsoft.com/devcontainers/dotnet:2-10.0-noble

ARG GODOT_VERSION=4.7.2
ARG GODOT_RELEASE=stable
# Export templates are ~1 GB; only needed to build exports (M0 CI smoke test).
ARG INSTALL_EXPORT_TEMPLATES=false

# The base image ships a Yarn apt repo with an expired key; we do not need it.
RUN rm -f /etc/apt/sources.list.d/yarn.list \
    && apt-get update && export DEBIAN_FRONTEND=noninteractive \
    && apt-get install -y --no-install-recommends \
        unzip wget ca-certificates \
        # Display: X11 + Wayland (libdecor draws window decorations on GNOME)
        libx11-6 libxcursor1 libxinerama1 libxrandr2 libxi6 libxext6 libxrender1 \
        libwayland-client0 libwayland-cursor0 libwayland-egl1 libxkbcommon0 libdecor-0-0 libdecor-0-plugin-1-cairo \
        # GPU: Mesa GL + Vulkan (Forward+ needs Vulkan)
        libgl1 libegl1 libgl1-mesa-dri libvulkan1 mesa-vulkan-drivers vulkan-tools \
        # Audio, fonts, misc runtime deps
        libasound2t64 libpulse0 libfontconfig1 libdbus-1-3 libudev1 \
    && rm -rf /var/lib/apt/lists/*

RUN set -eux; \
    tag="${GODOT_VERSION}-${GODOT_RELEASE}"; \
    name="Godot_v${tag}_mono_linux_x86_64"; \
    wget -q "https://github.com/godotengine/godot/releases/download/${tag}/${name}.zip" -O /tmp/godot.zip; \
    unzip -q /tmp/godot.zip -d /opt; \
    mv "/opt/${name}" /opt/godot; \
    ln -s "/opt/godot/Godot_v${tag}_mono_linux.x86_64" /opt/godot/godot-bin; \
    rm /tmp/godot.zip

# `godot` wrapper: prefer native Wayland when a Wayland socket is available,
# otherwise fall back to Godot's default (X11).
RUN printf '%s\n' \
      '#!/bin/sh' \
      'if [ -n "$WAYLAND_DISPLAY" ] && [ -S "$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY" ]; then' \
      '  exec /opt/godot/godot-bin --display-driver wayland "$@"' \
      'fi' \
      'exec /opt/godot/godot-bin "$@"' \
      > /usr/local/bin/godot \
    && chmod +x /usr/local/bin/godot

# XDG_RUNTIME_DIR for the container user. Godot's Wayland backend requires it
# to be owned by the user with mode 0700; the host's Wayland and PulseAudio
# sockets are bind-mounted into it (see devcontainer.json).
RUN install -d -m 0700 -o vscode -g vscode /tmp/runtime-vscode

USER vscode
RUN set -eux; \
    if [ "$INSTALL_EXPORT_TEMPLATES" = "true" ]; then \
      tag="${GODOT_VERSION}-${GODOT_RELEASE}"; \
      dest="$HOME/.local/share/godot/export_templates/${GODOT_VERSION}.${GODOT_RELEASE}.mono"; \
      mkdir -p "$dest"; \
      wget -q "https://github.com/godotengine/godot/releases/download/${tag}/Godot_v${tag}_mono_export_templates.tpz" -O /tmp/templates.tpz; \
      unzip -q /tmp/templates.tpz -d /tmp/templates; \
      mv /tmp/templates/templates/* "$dest/"; \
      rm -rf /tmp/templates /tmp/templates.tpz; \
    fi
