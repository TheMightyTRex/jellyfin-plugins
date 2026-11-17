#!/usr/bin/env bash
# Builds the release zip for one version of one plug-in: only the plug-in's own DLL and meta.json (Jellyfin already has
# everything else it needs), and prints the zip's MD5 checksum, which manifest.json carries. The version says which
# Jellyfin it is built for: an even third number is Jellyfin 10.11, an odd one Jellyfin 12.
# Usage: tools/package.sh picture-shrinker 1.2.5.0   (runs the build in Docker through tools/dotnet.sh, or plain
# dotnet with LOCAL=1). Without a version it builds both of the newest release in the plug-in's CHANGELOG.md.
# The zip is artifacts/<plug-in>_<version>_jellyfin-<10.11 or 12>.zip, with its checksum in the same name plus .md5.
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
cd "$here"
plugin="${1:?the first argument is the plug-in folder, such as picture-shrinker}"
if [ $# -lt 2 ]; then
  for v in $(python3 tools/manifest.py "$plugin" pair); do "$0" "$plugin" "$v"; done
  exit 0
fi
version="$2"
target="$(python3 tools/manifest.py "$plugin" target "$version")"
project="$(python3 tools/manifest.py "$plugin" project)"
out="artifacts/out-$plugin-$target"
rm -rf "$out" && mkdir -p "$out" artifacts
if [ "${LOCAL:-}" = 1 ]; then
  (cd "$plugin" && dotnet publish "$project" -c Release -o "$here/$out" -p:JellyfinTarget="$target" \
    -p:Version="$version" -p:AssemblyVersion="$version" -p:FileVersion="$version" >/dev/null)
else
  JELLYFIN_TARGET="$target" tools/dotnet.sh "$plugin" publish "$project" -c Release -o "/src/$out" \
    -p:Version="$version" -p:AssemblyVersion="$version" -p:FileVersion="$version" >/dev/null
fi
stage="artifacts/stage-$plugin-$target"
rm -rf "$stage" && mkdir -p "$stage"
cp "$out/$project.dll" "$stage/"
python3 tools/manifest.py "$plugin" meta "$version" > "$stage/meta.json"
zip="artifacts/${plugin}_${version}_jellyfin-${target}.zip"
rm -f "$zip"
(cd "$stage" && TZ=UTC touch -t 202611150000 ./* && zip -q -X -D "../$(basename "$zip")" ./*)
md5sum "$zip" | awk '{print $1}' > "$zip.md5"
echo "$zip $(cat "$zip.md5")"
