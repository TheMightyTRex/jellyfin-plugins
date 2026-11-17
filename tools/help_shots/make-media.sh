#!/usr/bin/env bash
# Made-up example media for the help centre's screenshots (17 November 2026), drawn with Jellyfin's own ffmpeg.
# Every film, series and person name here is made up. Run through heavy-run.
set -euo pipefail
cd /work
FF=/usr/lib/jellyfin-ffmpeg/ffmpeg
FONT=/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf
q() { $FF -hide_banner -loglevel error -y "$@"; }
film() { # name, seconds, described(0/1), colour
  local name="$1" secs="$2" ad="$3" c="$4" dir="media/films/$1"
  mkdir -p "$dir"
  if [ "$ad" = 1 ]; then
    q -f lavfi -i "color=c=$c:s=320x180:d=$secs,drawtext=fontfile=$FONT:text='$name':fontcolor=white:fontsize=16:x=(w-tw)/2:y=(h-th)/2" \
      -f lavfi -i "sine=frequency=440:duration=$secs" -f lavfi -i "sine=frequency=660:duration=$secs" \
      -map 0 -map 1 -map 2 -c:v libx264 -preset ultrafast -crf 35 -c:a aac -b:a 48k \
      -metadata:s:a:0 language=eng -metadata:s:a:0 title="English" \
      -metadata:s:a:1 language=eng -metadata:s:a:1 title="Audio Description" "$dir/$name.mkv"
  else
    q -f lavfi -i "color=c=$c:s=320x180:d=$secs,drawtext=fontfile=$FONT:text='$name':fontcolor=white:fontsize=16:x=(w-tw)/2:y=(h-th)/2" \
      -f lavfi -i "sine=frequency=440:duration=$secs" -map 0 -map 1 -c:v libx264 -preset ultrafast -crf 35 -c:a aac -b:a 48k \
      -metadata:s:a:0 language=eng -metadata:s:a:0 title="English" "$dir/$name.mkv"
  fi
}
film "Harbour Lights (2019)" 120 1 navy
film "The Long Field (2022)" 120 0 darkgreen
film "Northern Signal (2020)" 120 0 maroon
film "Paper Moons (2018)" 120 0 indigo
film "Copper Sky (2021)" 120 0 sienna
film "Winter Orchard (2017)" 120 0 teal
mkdir -p "media/series/Quiet Valley/Season 01"
q -f lavfi -i "color=c=olive:s=320x180:d=90" -f lavfi -i "sine=frequency=440:duration=90" -f lavfi -i "sine=frequency=550:duration=90" \
  -map 0 -map 1 -map 2 -c:v libx264 -preset ultrafast -crf 35 -c:a aac -b:a 48k -metadata:s:a:0 language=eng -metadata:s:a:0 title="English" \
  -metadata:s:a:1 language=eng -metadata:s:a:1 title="English (Described)" "media/series/Quiet Valley/Season 01/Quiet Valley S01E01.mkv"
q -f lavfi -i "color=c=olive:s=320x180:d=90" -f lavfi -i "sine=frequency=440:duration=90" -map 0 -map 1 -c:v libx264 -preset ultrafast -crf 35 \
  -c:a aac -b:a 48k -metadata:s:a:0 language=eng "media/series/Quiet Valley/Season 01/Quiet Valley S01E02.mkv"
# Description tracks to add: one the right length, one 45 seconds longer (another cut).
q -f lavfi -i "sine=frequency=330:duration=121" -c:a libmp3lame -b:a 32k "tracks/The Long Field (2022).mp3"
q -f lavfi -i "sine=frequency=330:duration=165" -c:a libmp3lame -b:a 32k "tracks/Northern Signal (2020).mp3"
# Large pictures, as a metadata source would give: posters 2000x3000, backdrops 3840x2160, thumbnails 1920x1080,
# logos 1600x500 PNG with transparency, photos of people 1365x2048.
pic() { # out, w, h, c0, c1, text (noise, so the sizes in bytes are like real pictures)
  q -f lavfi -i "gradients=s=$2x$3:c0=$4:c1=$5:duration=1:speed=0,noise=alls=10:allf=u,drawtext=fontfile=$FONT:text='$6':fontcolor=white:fontsize=$(( $2 / 14 )):x=(w-tw)/2:y=(h-th)/2" \
    -frames:v 1 -q:v 4 "$1"
}
i=0
for f in "Harbour Lights" "The Long Field" "Northern Signal" "Paper Moons" "Copper Sky" "Winter Orchard" "Quiet Valley"; do
  s=$(echo "$f" | tr 'A-Z ' 'a-z-'); cols=(0x1d3557:0xe63946 0x2a9d8f:0x264653 0x6a040f:0xffba08 0x3a0ca3:0x4cc9f0 0x9c6644:0xede0d4 0x005f73:0x94d2bd 0x606c38:0xdda15e)
  c0=${cols[$i]%%:*}; c1=${cols[$i]##*:}; i=$((i+1))
  pic "pictures/poster-$s.jpg" 2000 3000 $c0 $c1 "$f"
  pic "pictures/backdrop-$s.jpg" 3840 2160 $c1 $c0 "$f"
  pic "pictures/thumb-$s.jpg" 1920 1080 $c0 $c1 "$f"
  q -f lavfi -i "color=c=black@0.0:s=1600x500,format=rgba,drawtext=fontfile=$FONT:text='$f':fontcolor=white:fontsize=120:x=(w-tw)/2:y=(h-th)/2" \
    -frames:v 1 "pictures/logo-$s.png"
done
for p in "Alex Rowan" "Priya Hale" "Sam Ellery" "Jordan Vale" "Mara Ellison" "Tom Ashby"; do
  s=$(echo "$p" | tr 'A-Z ' 'a-z-')
  pic "pictures/person-$s.jpg" 1365 2048 0x264653 0x8ab17d "$p"
done
ls -R media tracks | head -40; du -sh pictures media
