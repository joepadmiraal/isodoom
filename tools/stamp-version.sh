#!/usr/bin/env bash
# Stamps a release version into the Godot project before an export (CI's
# export job, from the tag: `tools/stamp-version.sh v0.2.0-rc1`). The whole
# version (without its leading v) goes into project.godot's
# application/config/version, which the game prints at start; its numeric
# part (0.2.0) into the Windows exe's file and product version and the macOS
# bundle's versions, which take only digits and periods. Not committed: the
# repo's project keeps no version.
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: $0 VERSION   (e.g. v0.2.0, 0.2.0-rc1)" >&2
  exit 2
fi
version="${1#v}"
numeric="${version%%[-+]*}"
if ! [[ "$numeric" =~ ^[0-9]+(\.[0-9]+){0,2}$ ]]; then
  echo "$0: '$1' does not start with MAJOR[.MINOR[.PATCH]]" >&2
  exit 1
fi

root="$(cd "$(dirname "$0")/.." && pwd)"
project="$root/project.godot"
presets="$root/export_presets.cfg"

grep -q '^config/version=' "$project" && sed -i '/^config\/version=/d' "$project"
sed -i "s|^config/name=\"IsoDoom\"\$|&\nconfig/version=\"$version\"|" "$project"

# Windows (preset.1): file_version and product_version, after product_name.
sed -i '/^application\/\(file\|product\)_version=/d' "$presets"
sed -i "s|^application/product_name=\"IsoDoom\"\$|&\napplication/file_version=\"$numeric\"\napplication/product_version=\"$numeric\"|" "$presets"
# macOS (preset.2): short_version and version.
sed -i "s|^application/short_version=.*|application/short_version=\"$numeric\"|; s|^application/version=.*|application/version=\"$numeric\"|" "$presets"

grep -c "^config/version=\"$version\"\$" "$project" | grep -qx 1
test "$(grep -c "^application/[a-z_]*version=\"$numeric\"\$" "$presets")" -eq 4
echo "Version $version (numeric $numeric)"
