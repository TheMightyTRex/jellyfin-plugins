# Keeping the two picture shrinkers in step

There are two versions of the picture shrinker:

- **This one, for Jellyfin**: a Jellyfin plug-in that runs inside Jellyfin, in two builds from the same source, one
  for Jellyfin 10.11 and one for Jellyfin 12. It works on any Jellyfin 10.11 or 12 server, with or without Starboard.
- **The Starboard one**: a Starboard plug-in in
  [TheMightyTRex/starboard-plugins](https://github.com/TheMightyTRex/starboard-plugins/tree/main/picture-shrinker),
  which runs in its own small container beside Jellyfin.

They must shrink the same pictures in the same way, so a picture never depends on which version shrank it.

## What must match

| What | Where it is set | How it is checked |
|---|---|---|
| The sizes: posters 900 pixels tall, photos of people 400 pixels wide, with their limits | `rules.json`, `kinds` | Both test suites compare their defaults and limits with `rules.json` |
| Which pictures: `poster.*` under `library`, anything under `People`, only `.jpg`, `.jpeg`, `.png` and `.webp`, never hidden files | `rules.json`, `kinds`, `extensions`, `skip_hidden` | The shared test pictures |
| Quality 85 for JPEG and WebP, PNG lossless, each picture keeps its format | `rules.json`, `quality`, `formats` | The shared test pictures |
| Rounding half to even, so 500.5 pixels becomes 500 | `rules.json`, `rounding` | The shared test pictures (`library/7b/7b8c/poster.jpg`) |
| The checks before a replace: decodes in full, right size, right format, smaller than the original | `rules.json`, `checks` | The shared test pictures (a broken and a cut-short picture) |
| Keep the original's mode and owner | `rules.json`, `keep_from_original` | Unit tests in each repository |
| Backup before the first shrink, activated by default, never overwritten, at least 100 MB left free, no backup means no shrink | `rules.json`, `backup` | Unit tests in each repository |
| Restore puts back byte-identical originals, keeps the backup, and takes them off the space saved | Code | Unit tests in each repository |
| Delete backup removes only the pictures in the backup folder, never follows a link, is refused during a run | Code | Unit tests in each repository |
| New pictures: deactivated by default, wait 10 minutes | `rules.json`, `new_pictures` | Both test suites compare their defaults with `rules.json` |
| A run stops after 115 minutes and the next carries on | `rules.json`, `run_limit_minutes` | Both test suites compare with `rules.json` |
| A finished run sends no notification; only the figures change | Code | A test in each repository |
| The Space figures: the pictures now, the backup and the space saved | Code | Unit tests in each repository |

Two differences come from the tools each version runs on, and do not change which pictures are shrunk or their size:

- The Jellyfin version uses SkiaSharp, which Jellyfin already has; the Starboard version uses Pillow. Both resize with
  a high-quality filter, but the bytes of a shrunk picture are not identical between them.
- Pillow writes progressive JPEGs; SkiaSharp writes baseline JPEGs. Both open everywhere Jellyfin runs.

When the Jellyfin version is installed, the Starboard version stays idle (it asks Jellyfin's list of plug-ins), so the
two never shrink the same pictures.

## How to change a rule

1. Change `rules.json` here, and raise `rules_version` (see version numbers below).
2. If the change affects a test picture's result, change `tools/make-test-pictures.py` and run it, so
   `tests/pictures/expected.json` says the new result.
3. Change the code here until `dotnet test` passes for both builds
   (`JELLYFIN_TARGET=12 tools/dotnet.sh test tests/Jellyfin.Plugin.PictureShrinker.Tests`, and again with
   `JELLYFIN_TARGET=10.11`; GitHub's **Build and test** workflow runs both).
4. In starboard-plugins, run `python3 tools/sync_picture_rules.py`, which copies `rules.json` to
   `picture-shrinker/rules.json` and `tests/pictures` to `tests/picture-shrinker-shared`.
5. Change the Starboard plug-in's code until `python3 -m unittest tests.test_picture_shrinker` passes.
6. Release both, with the same first two numbers.

The starboard-plugins workflow `picture-shrinker-sync.yml` fetches this repository's `rules.json` and test pictures
each week and on each change, and fails when its copy differs.

## Version numbers

Both versions share the first two numbers, which are the rules version:

- **Rules 1.1** is the Jellyfin version **1.1.x.0** and the Starboard version **1.1.x**.
- A change to `rules.json` or to what a run does raises the second number in both, such as 1.2.
- A fix that changes nothing the rules cover raises only the third number, in the version that needs it.
- The fourth number of the Jellyfin version is always 0 (Jellyfin plug-in versions have four numbers).

### The two Jellyfin builds

Jellyfin 12 runs on .NET 10 and its plug-ins must be built for it; Jellyfin 10.11 runs on .NET 9. The code is the
same: every Jellyfin part this plug-in uses (scheduled tasks, library events, the settings page, Jellyfin's folders,
the plug-in's settings) is unchanged between them. So one source makes two builds (`JellyfinTarget` in
`Directory.Build.props`), and the third number says which:

| Third number | Build | .NET | Built against | targetAbi in manifest.json |
|---|---|---|---|---|
| Even, such as 1.1.**0**.0 | Jellyfin 10.11 | 9 | Jellyfin.Controller 10.11.0, SkiaSharp 3.116.1 | 10.11.0.0 |
| Odd, such as 1.1.**1**.0 | Jellyfin 12 | 10 | Jellyfin.Controller 12.0.0, SkiaSharp 3.119.4 | 12.0.0.0 |

Why this way:

- Jellyfin lists every version in a repository whose targetAbi is not newer than itself, and installs or updates to
  the highest version number among them. It has no upper limit. So Jellyfin 10.11 sees only the 10.11 build, and
  Jellyfin 12 sees both and must find its own build to be the higher one. An odd third number for the 12 build, one
  above the 10.11 build, does that.
- The two builds are always released together, from one tag named by the 12 build (`v1.1.1.0` makes 1.1.0.0 and
  1.1.1.0). A 10.11 build released alone would be higher than the last 12 build and Jellyfin 12 would offer it.
- The first two numbers stay the rules version, as before, and the fourth stays 0.
- Each build targets the first release of its Jellyfin line (10.11.0, 12.0.0), so it suits every later release in
  that line, as Jellyfin's own plug-ins do (their Jellyfin 12 builds all say targetAbi 12.0.0.0).

The next fix is 1.1.2.0 for 10.11 and 1.1.3.0 for 12, tagged `v1.1.3.0`. If a later Jellyfin needs a third build,
this scheme has to change; until then, Starboard offers the plug-in on Jellyfin 10.11 and 12 only.
