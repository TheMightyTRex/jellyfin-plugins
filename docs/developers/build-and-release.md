[Help centre](../README.md) · Build and release · reference
<!-- help: dev / build -->

# Build, test and release a plug-in

Every plug-in is a folder at the top of the repository, and the shared `tools` build, test and package each one for
both Jellyfin versions. A release is one tag. [CONTRIBUTING.md](../../CONTRIBUTING.md) has the full rules.

## When would you use it

- You want to build or test a plug-in on your own computer.
- You are releasing a new version.
- You are adding a plug-in to the repository.

## How the repository is arranged

| Part | What it is |
|---|---|
| `manifest.json` | The one list Jellyfin reads, with an entry for each plug-in and every version of it |
| `picture-shrinker`, `audio-description` | One folder for each plug-in: its project, tests, README, CHANGELOG, `build.yaml` and logo |
| `tools/dotnet.sh` | Builds and tests in Docker, with the .NET SDK for the Jellyfin chosen |
| `tools/package.sh` | Makes a release zip and its MD5 checksum |
| `tools/manifest.py` | Updates one plug-in's entry in `manifest.json`, and checks it |
| `docs`, `help-centre.json` | This help centre |
| `.github/workflows` | **Build and test** on each change, **Release** for one plug-in, and **Help centre** for these pages |

## Build and test

The builds run in Docker, from the top of the repository. `JELLYFIN_TARGET` picks the build: `12`, the default, uses
the .NET 10 SDK, and `10.11` the .NET 9 SDK.

```bash
JELLYFIN_TARGET=12 tools/dotnet.sh picture-shrinker test tests/Jellyfin.Plugin.PictureShrinker.Tests
```

```bash
JELLYFIN_TARGET=10.11 tools/dotnet.sh picture-shrinker test tests/Jellyfin.Plugin.PictureShrinker.Tests
```

To make both zips of the newest release in the plug-in's `CHANGELOG.md`:

```bash
tools/package.sh picture-shrinker
```

The zips go in `artifacts`, named `<folder>_<version>_jellyfin-<10.11 or 12>.zip`, each with its MD5 checksum.

## Release

1. Write the release in the plug-in's `CHANGELOG.md`, with a heading naming both versions, such as
   `## [1.2.5.0] for Jellyfin 12 and [1.2.4.0] for Jellyfin 10.11 (17 November 2026)`.
2. Push a tag named by the folder and the Jellyfin 12 version, such as `picture-shrinker-v1.2.5.0`.

The **Release** workflow builds and tests both builds, makes both zips and their checksums, creates one GitHub release
with both, and adds both versions to that plug-in's entry in `manifest.json`. Every Jellyfin with the repository then
offers the update.

**A warning in every release note:** when people must know something after installing or updating, such as Picture
shrinker's backup choice, put it in `<plug-in>/jellyfin-notice.json`. `tools/manifest.py` puts it at the top of that
version's notes and every later one.

## Add a plug-in

A plug-in is a folder with a `build.yaml`, a `Directory.Build.props`, its project folder, its tests, its own logo in
`images/logo.png` (16:9, such as 1,280 by 720 pixels), a README and a CHANGELOG. The tools and workflows find it from
`build.yaml`, so they need no change. Add a row to the README's table, help articles under `docs`, and a group for it
in `help-centre.json`.

## Check and build this help centre

```bash
python3 tools/check_help.py
```

```bash
python3 <design-system>/tools/help_centre/build.py --config help-centre.json --out _site --strict
```

`check_help.py` checks the links, the layout of each article, the screenshots and the words.
`tools/help_shots` retakes the screenshots from a throwaway Jellyfin with made-up data.

## Find out more

- [Picture shrinker for developers: the rules and SYNC.md](picture-shrinker.md)
- [Audio description for developers](audio-description.md)
- [CONTRIBUTING.md on GitHub](../../CONTRIBUTING.md)
