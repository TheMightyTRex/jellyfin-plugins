[Help centre](../README.md) · The rules and SYNC.md · reference
<!-- help: dev / rules -->

# Audio description for developers

Audio description has a Jellyfin version, this plug-in, and two Starboard plug-ins: Audio description, and Audio
description tracks. They must find the same tracks, give the same tag, and name and check added tracks in the same
way, so their shared rules are in one file, `audio-description/rules.json`, and
[SYNC.md](../../audio-description/SYNC.md) says what must match and how to change a rule.

## When would you use it

- You want to change the words that mean audio description, the tag, the collection or the length check.
- You are changing the code of either version and need to know what must stay the same.

## What must match

| What | Where it is set |
|---|---|
| The words that mean audio description, in a track's title, display title or comment, and the words that rule a track out | `rules.json`, `described_track`, with shared examples both test suites run |
| The tag **Audio description**, on films, episodes and series with at least one described episode, taken off when the track goes | `rules.json`, `tag`, `tagged` |
| The collection **Audio description**, active by default, holding films and series | `rules.json`, `collection` |
| Find and tag after each library scan, active by default | `rules.json`, `after_library_scan` |
| Play the described track: per person, inactive until chosen; Jellyfin's SetAudioStreamIndex command; only when another track plays; only to apps that accept it | `rules.json`, `play_described_track` |
| How a file name is read: a film and its year, or a series with S01E03 or 1x03 | `rules.json`, `tracks.names`, with shared examples |
| The length check: within 5 seconds by default, from 1 to 30 | `rules.json`, `tracks.tolerance_seconds` |
| The new track: `<film>.Audio Description.<language>.mka` next to the film, titled `<Language> (Audio Description)`, the visual impaired flag, not the default, the sound copied without converting; twelve languages | `rules.json`, `tracks` |
| Never replace, never delete: the Confirmed, Taken off and Unzipped folders; zips of at most 500 files and 4 GB | `rules.json`, `tracks.folders`, `tracks.zip` |

## Differences that come from where each runs

- **The tools:** this version uses Jellyfin's own ffprobe and ffmpeg; Starboard's uses mkvmerge in its own image.
  Both copy the sound into Matroska without converting it.
- **When Find and tag runs:** here, after each library scan and every night; in Starboard, every night.
- **Play the described track:** here, as soon as playback starts; in Starboard, up to about half a minute in.
- **Uploads:** up to 1 GB here; up to 60 MB in Starboard.
- **Only in Starboard:** preferring releases with audio description in Reelix, and links to sources of tracks.

When this version is installed, Starboard's plug-ins find its id, `7a591954-9c27-4ba7-80c3-dc5f0a140692`, in
Jellyfin's list of plug-ins and stay idle for the parts this one does.

## Change a rule

1. Change `rules.json`, and raise `rules_version`.
2. Change the code until the tests pass for both builds:

   ```bash
   JELLYFIN_TARGET=12 tools/dotnet.sh audio-description test tests/Jellyfin.Plugin.AudioDescription.Tests
   ```

   ```bash
   JELLYFIN_TARGET=10.11 tools/dotnet.sh audio-description test tests/Jellyfin.Plugin.AudioDescription.Tests
   ```

3. In starboard-plugins, run `python3 tools/sync_audio_description_rules.py`, which copies `rules.json` to both
   Starboard plug-ins.
4. Change the Starboard plug-ins' code until their tests pass.
5. Release both. starboard-plugins' `audio-description-sync.yml` workflow fails while the copies differ.

## Version numbers

- **Rules 1.0** is the Jellyfin version 1.0.x.0: 1.0.0.0 for Jellyfin 10.11 and 1.0.1.0 for Jellyfin 12. The
  Starboard plug-ins keep their own numbers (Audio description 1.3.0, Audio description tracks 1.2.0) and say the
  rules version in their notes.
- A change to `rules.json` raises the second number of the Jellyfin version and the minor number of both Starboard
  plug-ins.
- The third number says which Jellyfin, as for Picture shrinker; the fourth is always 0.
- The only difference between the two Jellyfin builds in code is the list of people: `IUserManager.Users` in 10.11,
  `GetUsers()` in 12.

## Find out more

- [Picture shrinker for developers: the rules and SYNC.md](picture-shrinker.md)
- [Build, test and release a plug-in](build-and-release.md)
- [SYNC.md on GitHub](../../audio-description/SYNC.md)
- [rules.json on GitHub](../../audio-description/rules.json)
