#!/usr/bin/env bash
# Builds the release zip for one version: only this plug-in's DLL and meta.json (Jellyfin already has SkiaSharp and
# everything else it needs), and prints the zip's MD5 checksum, which manifest.json carries.
# Usage: tools/package.sh 1.1.0.0   (runs the build in Docker under the heavy-job lock, or plain dotnet with LOCAL=1)
set -euo pipefail
version="${1:?give the version, such as 1.1.0.0}"
here="$(cd "$(dirname "$0")/.." && pwd)"
cd "$here"
out="artifacts/out"
rm -rf "$out" && mkdir -p "$out" artifacts
if [ "${LOCAL:-}" = 1 ]; then dn=dotnet; else dn=tools/dotnet.sh; fi
$dn publish Jellyfin.Plugin.PictureShrinker -c Release -o "$out" -p:Version="$version" -p:AssemblyVersion="$version" -p:FileVersion="$version" >/dev/null
stage="artifacts/stage"
rm -rf "$stage" && mkdir -p "$stage"
cp "$out/Jellyfin.Plugin.PictureShrinker.dll" "$stage/"
python3 tools/manifest.py meta "$version" > "$stage/meta.json"
zip="artifacts/picture-shrinker_${version}.zip"
rm -f "$zip"
(cd "$stage" && TZ=UTC touch -t 202611150000 ./* && zip -q -X -D "../$(basename "$zip")" ./*)
md5sum "$zip" | awk '{print $1}' > "$zip.md5"
echo "$zip $(cat "$zip.md5")"
