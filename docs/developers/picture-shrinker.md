[Help centre](../README.md) · The rules and SYNC.md · reference
<!-- help: dev / rules -->

# Picture shrinker for developers: the rules and SYNC.md

Picture shrinker has two versions that must shrink the same pictures in the same way: this Jellyfin plug-in and
Starboard's plug-in. Their shared rules are in one file, `picture-shrinker/rules.json`, and
[SYNC.md](../../picture-shrinker/SYNC.md) says what must match, how it is tested and how to change a rule.

## When would you use it

- You want to change a size, a check or the backup rules.
- You are changing the code of either version and need to know what must stay the same.
- You want to know what a version number means.

## The rules file

`picture-shrinker/rules.json`, rules version 1.2, holds:

| Key | What it sets |
|---|---|
| `kinds` | The eight picture types, in order, each with its folder, the start of its file name, its limit (height or width), its default size, its smallest and largest size, and whether it is shrunk by default |
| `shrink_all` | **Shrink all picture types**, active by default |
| `extensions`, `formats` | Only `.jpg`, `.jpeg`, `.png` and `.webp`; each picture keeps its format |
| `quality` | 85 by default, from 50 to 100, for JPEG and WebP |
| `rounding` | Half to even, so 500.5 pixels becomes 500 |
| `checks` | Decodes in full, right size, right format, smaller than the original |
| `keep_from_original` | The file's mode and owner |
| `backup` | No default choice (`keep` or `none`), asked after install and after the update to 1.2; the folder `picture-shrinker/originals`; never overwritten; at least 100 MB left free |
| `start_again` | The two kinds, what each deletes and keeps, the hidden name for pictures moved aside, checked every 10 minutes, put back after 24 hours, 50 ms between refreshes |
| `new_pictures` | Inactive by default; a picture waits 600 seconds |
| `run_limit_minutes` | 115 |
| `keep_runs`, `keep_failures_per_run` | 20 each |

## What must match, and how it is checked

- **Both test suites** compare their defaults and limits with `rules.json`.
- **The shared test pictures** in `picture-shrinker/tests/pictures`, with their expected results in `expected.json`,
  are shrunk by both versions: a backdrop already small enough, a PNG logo, a thumbnail, a broken picture and a
  cut-short one among them. `tools/make-test-pictures.py` makes them again.
- **Unit tests in each repository** cover the backup, Restore, Delete backup, Start again, new pictures and the
  figures.
- **starboard-plugins' `picture-shrinker-sync.yml` workflow** fetches this `rules.json` and the test pictures each
  week and on each change, and fails when its copy differs.

Two differences come from the tools each version runs on, and change neither which pictures are shrunk nor their
size: this version uses SkiaSharp, which Jellyfin already loads, and writes baseline JPEGs; Starboard's uses Pillow
and writes progressive JPEGs.

## Change a rule

1. Change `rules.json`, and raise `rules_version`.
2. If the change affects a test picture's result, change `tools/make-test-pictures.py` and run it, so
   `tests/pictures/expected.json` says the new result.
3. Change the code until the tests pass for both builds:

   ```bash
   JELLYFIN_TARGET=12 tools/dotnet.sh picture-shrinker test tests/Jellyfin.Plugin.PictureShrinker.Tests
   ```

   ```bash
   JELLYFIN_TARGET=10.11 tools/dotnet.sh picture-shrinker test tests/Jellyfin.Plugin.PictureShrinker.Tests
   ```

4. In starboard-plugins, run `python3 tools/sync_picture_rules.py`, which copies the rules and the test pictures.
5. Change the Starboard plug-in's code until `python3 -m unittest tests.test_picture_shrinker` passes there.
6. Release both, with the same first two numbers.

## Version numbers

- **The first two numbers are the rules version,** shared with Starboard's version: rules 1.2 is the Jellyfin version
  1.2.x.0 and the Starboard version 1.2.x.
- **The third number says which Jellyfin:** even for Jellyfin 10.11 (.NET 9, targetAbi 10.11.0.0), odd for Jellyfin
  12 (.NET 10, targetAbi 12.0.0.0). The Jellyfin 12 build is always one higher, because Jellyfin offers every version
  whose targetAbi is not newer than itself and installs the highest.
- **The fourth number is always 0.**
- A fix that changes nothing the rules cover raises only the third number.

## Why SkiaSharp

Jellyfin already loads SkiaSharp for its own pictures, with its native library for amd64 and arm64. The release zip
holds only the plug-in's own DLL, about 100 KB, so there is nothing native to ship and no second image library in
memory.

## Find out more

- [Build, test and release a plug-in](build-and-release.md)
- [Audio description for developers](audio-description.md)
- [SYNC.md on GitHub](../../picture-shrinker/SYNC.md)
- [rules.json on GitHub](../../picture-shrinker/rules.json)
