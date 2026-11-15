#!/usr/bin/env bash
# Builds the release zip for one version: only this plug-in's DLL and meta.json (Jellyfin already has SkiaSharp and
# everything else it needs), and prints the zip's MD5 checksum, which manifest.json carries. The version says which
# Jellyfin it is built for (SYNC.md): an even third number is Jellyfin 10.11, an odd one Jellyfin 12.
# Usage: tools/package.sh 1.1.1.0   (runs the build in Docker under the heavy-job lock, or plain dotnet with LOCAL=1)
# Without a version it builds both of the newest release in CHANGELOG.md.
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
cd "$here"
if [ $# -eq 0 ]; then
  for v in $(python3 tools/manifest.py pair); do "$0" "$v"; done
  exit 0
fi
version="$1"
target="$(python3 tools/manifest.py target "$version")"
out="artifacts/out-$target"
rm -rf "$out" && mkdir -p "$out" artifacts
if [ "${LOCAL:-}" = 1 ]; then dn=dotnet; else dn="env JELLYFIN_TARGET=$target tools/dotnet.sh"; fi
$dn publish Jellyfin.Plugin.PictureShrinker -c Release -o "$out" -p:JellyfinTarget="$target" \
  -p:Version="$version" -p:AssemblyVersion="$version" -p:FileVersion="$version" >/dev/null
stage="artifacts/stage-$target"
rm -rf "$stage" && mkdir -p "$stage"
cp "$out/Jellyfin.Plugin.PictureShrinker.dll" "$stage/"
python3 tools/manifest.py meta "$version" > "$stage/meta.json"
zip="artifacts/picture-shrinker_${version}_jellyfin-${target}.zip"
rm -f "$zip"
(cd "$stage" && TZ=UTC touch -t 202611150000 ./* && zip -q -X -D "../$(basename "$zip")" ./*)
md5sum "$zip" | awk '{print $1}' > "$zip.md5"
echo "$zip $(cat "$zip.md5")"
