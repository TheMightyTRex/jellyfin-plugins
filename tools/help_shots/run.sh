#!/usr/bin/env bash
# Retakes the help centre's screenshots (docs/images) from a throwaway Jellyfin with made-up data, marked up with the
# design system's tools/annotate.js to its SCREENSHOTS.md. 17 November 2026.
#
#   1. Make a work folder on a disk where SQLite works, with media/, pictures/, tracks/ and config/ in it, and run
#      make-media.sh inside the Jellyfin image (Jellyfin's own ffmpeg draws made-up films, tracks and pictures).
#   2. Start lscr.io/linuxserver/jellyfin on port 18900 with config/ and media/, run setup.py (made-up people, two
#      libraries with no internet metadata, the pictures), theme.py (light theme) and repo.py add.
#   3. Run each phase in order, putting the server in the state the phase's name says between them:
#        tools/help_shots/run.sh <work folder> 1-before-install
#      then add the built plug-ins to config/data/plugins and restart, and so on. ONLY=<part of a file name> retakes
#      some; JF=http://127.0.0.1:18901 points at a second Jellyfin (phase 6, Jellyfin 10.11).
#   4. Look at every picture, then copy the ones the articles use into docs/images.
#
# The admin password is a test value made for the throwaway server: <work folder>/admin-password, never a real one.
# Heavy: run it through the server's heavy-job wrapper, capped at 3 GB and 4 processors as here.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
work="$(realpath "${1:?the work folder}")"; set="${2:?the phase, such as 1-before-install}"; shift 2
ds="${DESIGN_SYSTEM:-$here/../../../design-system}"
envs=(-e "SET=$set"); for e in "$@"; do envs+=(-e "$e"); done
[ -n "${ONLY:-}" ] && envs+=(-e "ONLY=$ONLY")
[ -n "${JF:-}" ] && envs+=(-e "JF=$JF")
cache="${PLAYWRIGHT_NPM_CACHE:-/mnt/pool/build-cache/npm-playwright-1.63}"
mkdir -p "$work/shots"
docker run --rm --memory 3g --memory-swap 3g --cpus 4 --network host -u "$(id -u):$(id -g)" -e HOME=/tmp -e OUT=/work/shots "${envs[@]}" \
  -v "$work:/work" -v "$here:/tools:ro" -v "$ds/tools/annotate.js:/annotate.js:ro" ${cache:+-v "$cache:/cache:ro"} \
  mcr.microsoft.com/playwright:v1.63.0-noble \
  sh -c "mkdir -p /tmp/s && cp /tools/*.mjs /tmp/s/ && cd /tmp/s && if [ -d /cache/node_modules/playwright ]; then ln -s /cache/node_modules node_modules; else npm init -y >/dev/null && npm i --silent playwright@1.63.0 >/dev/null; fi && node shoot.mjs"
echo "Screenshots in $work/shots. Look at each one before copying it into docs/images."
