# Keeping the audio description plug-ins in step

There are two versions:

- **This one, for Jellyfin**: a Jellyfin plug-in that runs inside Jellyfin, in two builds from the same source, one
  for Jellyfin 10.11 and one for Jellyfin 12. It works on any Jellyfin 10.11 or 12 server, with or without Starboard.
- **The Starboard ones**: two Starboard plug-ins in
  [TheMightyTRex/starboard-plugins](https://github.com/TheMightyTRex/starboard-plugins): `audio-description` (find
  and tag, play the described track, Reelix and the sources) and `audio-description-tracks` (add a track you have).
  They run in their own small containers beside Jellyfin and reach it through its API with the key Starboard keeps.

They must find the same tracks, give the same tag, and name and check added tracks in the same way, so a library never
depends on which version did the work.

## What must match

| What | Where it is set | How it is checked |
|---|---|---|
| The words that mean audio description, in a track's title, display title or comment, and the words that rule a track out (commentary) | `rules.json`, `described_track` | Both test suites run the shared examples (`described_track.examples`) |
| The tag, **Audio description**, on films, episodes and series with at least one described episode; taken off when the track goes | `rules.json`, `tag`, `tagged` | Unit tests in each repository |
| The collection, **Audio description**, active by default, holding films and series (not episodes) | `rules.json`, `collection` | Unit tests in each repository |
| Find and tag after each library scan, active by default | `rules.json`, `after_library_scan` | The Jellyfin version follows Jellyfin's Scan Media Library task; Starboard checks nightly, as it cannot follow the scan |
| Play the described track: per person, inactive until chosen; Jellyfin's SetAudioStreamIndex command with the stream's Index; only when another track plays; only to apps that say they accept it | `rules.json`, `play_described_track` | Unit tests in each repository |
| How a track's file name is read: a film and its year, or a series with S01E03 or 1x03 | `rules.json`, `tracks.names` | Both test suites run the shared examples (`tracks.names.examples`) |
| The length check: within 5 seconds by default, 1 to 30 | `rules.json`, `tracks.tolerance_seconds` | Unit tests in each repository |
| The new track: `<film>.Audio Description.<language>.mka` next to the film, titled `<Language> (Audio Description)`, the visual impaired flag, not the default track, the sound copied without converting; the twelve languages | `rules.json`, `tracks` | Unit tests in each repository |
| Never replace a track that is there, never delete: Confirmed, Taken off and Unzipped folders; zips of at most 500 files and 4 GB | `rules.json`, `tracks.folders`, `tracks.zip`, `never_replace`, `never_delete` | Unit tests in each repository |
| The words for each track's state (Added, Lengths differ, Choose the title and so on) | Code | Unit tests in each repository |

Differences that come from where each runs, which do not change what is found or added:

- **The tools:** this version uses Jellyfin's own ffprobe and ffmpeg; Starboard's uses mkvmerge (MKVToolNix) in its
  own image. Both copy the sound without converting it into Matroska, so the length is exact before it is compared.
- **When Find and tag runs:** this version runs after each library scan and every night; Starboard's runs every
  night, as Starboard cannot follow Jellyfin's scan.
- **Play the described track:** this version hears playback start inside Jellyfin; Starboard's hears Starboard's
  `playback.started` event, which comes from Starboard's own check of Jellyfin, so the change can come up to about
  half a minute into the film.
- **Uploads:** up to 1 GB here; up to 60 MB in Starboard, its limit for a file.
- **The folder:** here, `audio-description` in Jellyfin's data folder unless another is chosen, checked every 5
  minutes; in Starboard, `audio-description` in the media folder, checked when its page opens and every night.
- **Only in Starboard:** preferring releases with audio description in Reelix, and the links to AudioVault,
  YouDescribe, MovieReading and Earcatch.

When this version is installed, Starboard's two plug-ins stay idle for the parts this one does (they ask Jellyfin's
list of plug-ins for this one's id, `7a591954-9c27-4ba7-80c3-dc5f0a140692`), so the two never tag, switch tracks or
add the same track twice.

## How to change a rule

1. Change `rules.json` here, and raise `rules_version` (read 'Version numbers').
2. Change the code here until `dotnet test` passes for both builds
   (from the top of jellyfin-plugins, `JELLYFIN_TARGET=12 tools/dotnet.sh audio-description test
   tests/Jellyfin.Plugin.AudioDescription.Tests`, and again with `JELLYFIN_TARGET=10.11`).
3. In starboard-plugins, run `python3 tools/sync_audio_description_rules.py`, which copies `rules.json` to
   `audio-description/rules.json` and `audio-description-tracks/rules.json`.
4. Change the Starboard plug-ins' code until `python3 -m unittest tests.test_audio_description
   tests.test_audio_description_tracks` passes.
5. Release both, with the same first two numbers.

The starboard-plugins workflow `audio-description-sync.yml` fetches this `rules.json` each week and on each change,
and fails when its copies differ.

## Version numbers

The Jellyfin version shares its first two numbers with the rules version:

- **Rules 1.0** is the Jellyfin version **1.0.x.0** (1.0.0.0 for Jellyfin 10.11, 1.0.1.0 for Jellyfin 12). The
  Starboard plug-ins kept their own numbers when they took up the rules (Audio description 1.3.0, Audio description
  tracks 1.2.0) and say the rules version they follow in their notes.
- A change to `rules.json` raises the second number of the Jellyfin version and the minor number of both Starboard
  plug-ins.
- A fix that changes nothing the rules cover raises only the third number, in the version that needs it.
- The fourth number of the Jellyfin version is always 0 (Jellyfin plug-in versions have four numbers).

### The two Jellyfin builds

Jellyfin 12 runs on .NET 10 and its plug-ins must be built for it; Jellyfin 10.11 runs on .NET 9. Every Jellyfin part
this plug-in uses (scheduled tasks, library and session events, collections, the settings page) is the same between
them, except the list of people (`IUserManager.Users` in 10.11, `GetUsers()` in 12). So one source makes two builds
(`JellyfinTarget` in `Directory.Build.props`), and the third number says which:

| Third number | Build | .NET | Built against | targetAbi |
|---|---|---|---|---|
| Even, such as 1.0.**0**.0 | Jellyfin 10.11 | 9 | Jellyfin.Controller 10.11.0 | 10.11.0.0 |
| Odd, such as 1.0.**1**.0 | Jellyfin 12 | 10 | Jellyfin.Controller 12.0.0 | 12.0.0.0 |

Jellyfin lists every version whose targetAbi is not newer than itself and installs the highest, so the Jellyfin 12
build is always one higher than the 10.11 build of the same release, and the two are released together.
