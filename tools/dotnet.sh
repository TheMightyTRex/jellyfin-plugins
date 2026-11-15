#!/usr/bin/env bash
# Runs a dotnet command in the .NET SDK container for one Jellyfin: JELLYFIN_TARGET=10.11 uses the .NET 9 SDK,
# JELLYFIN_TARGET=12 (the default) the .NET 10 SDK, and passes -p:JellyfinTarget to match. One heavy job at a time,
# with memory and processors capped. Caches go in BUILD_CACHE (default /mnt/pool/build-cache/dash-433).
# Example: JELLYFIN_TARGET=10.11 tools/dotnet.sh test tests/Jellyfin.Plugin.PictureShrinker.Tests
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
target="${JELLYFIN_TARGET:-12}"
case "$target" in
  10.11) sdk=9.0 ;;
  12) sdk=10.0 ;;
  *) echo "JELLYFIN_TARGET must be 10.11 or 12" >&2; exit 2 ;;
esac
cache="${BUILD_CACHE:-/mnt/pool/build-cache/dash-433}"
# The pool (mergerfs) cannot memory-map files, which NuGet and MSBuild need: use the same folder on its own disk.
mkdir -p "$cache"
for d in /mnt/disks/*/pool /mnt/disks/*/PoolPart.*; do
  if [ "${cache#/mnt/pool/}" != "$cache" ] && [ -d "$d/${cache#/mnt/pool/}" ]; then cache="$d/${cache#/mnt/pool/}"; break; fi
done
lock="${HEAVY_LOCK:-/mnt/pool/build-cache/heavy.lock}"
mkdir -p "$cache/nuget"
cmd="$1"; shift
exec flock "$lock" docker run --rm --memory 3g --memory-swap 3g --cpus 4 \
  --user "$(id -u):$(id -g)" -e HOME=/home/build -e DOTNET_CLI_HOME=/home/build -e NUGET_PACKAGES=/nuget \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 -e DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
  -v "$cache/nuget:/nuget" --tmpfs /home/build:exec,uid=$(id -u),gid=$(id -g) -v "$here:/src" -w /src \
  "mcr.microsoft.com/dotnet/sdk:$sdk" dotnet "$cmd" -p:JellyfinTarget="$target" "$@"
