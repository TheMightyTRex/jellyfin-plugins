# Building, releasing and adding a plug-in

## Build and test

The builds run in Docker, from the top of the repository. `JELLYFIN_TARGET` picks the build: `12` (the default) uses
the .NET 10 SDK, and `10.11` the .NET 9 SDK. The first argument is the plug-in's folder.

```
JELLYFIN_TARGET=12 tools/dotnet.sh picture-shrinker test tests/Jellyfin.Plugin.PictureShrinker.Tests
JELLYFIN_TARGET=10.11 tools/dotnet.sh picture-shrinker test tests/Jellyfin.Plugin.PictureShrinker.Tests
tools/package.sh picture-shrinker            # both zips of the newest release in the plug-in's CHANGELOG.md
python3 tools/manifest.py check              # manifest.json is in order
```

The zips go in `artifacts`, named `<folder>_<version>_jellyfin-<10.11 or 12>.zip`, each with its MD5 checksum.

## Version numbers

Each release has two builds from one source. The third number says which: even for Jellyfin 10.11 (targetAbi
10.11.0.0), odd for Jellyfin 12 (targetAbi 12.0.0.0), and the Jellyfin 12 build is always one higher, so Jellyfin 12
picks its own build. The fourth number is 0. Picture shrinker's SYNC.md explains why.

## Release a plug-in

Write the release in the plug-in's CHANGELOG.md, with a heading that names both versions, such as
`## [1.2.5.0] for Jellyfin 12 and [1.2.4.0] for Jellyfin 10.11 (17 November 2026)`. Then push a tag named by the
folder and the Jellyfin 12 version:

```
git tag picture-shrinker-v1.2.5.0 && git push origin picture-shrinker-v1.2.5.0
```

The **Release** workflow builds and tests both builds of that plug-in, makes both zips with their MD5 checksums,
creates one GitHub release with both, and adds both versions to that plug-in's entry in `manifest.json`. Every other
plug-in's entry stays as it is. Nothing else is needed: every Jellyfin with the repository offers the update.

## Add a plug-in

A plug-in is a folder at the top of the repository with a `build.yaml`. The tools and workflows find it from that
file, so they need no change. The folder has:

| File | What it is |
|---|---|
| `build.yaml` | Jellyfin's jprm format: `name`, `guid`, `owner`, `category`, `overview` (the short line in the catalogue), `description` (the longer text) and `artifacts` (the plug-in's DLL, such as `Jellyfin.Plugin.AudioDescription.dll`). `manifest.json` takes its words from here. |
| `Directory.Build.props` | `JellyfinTarget` (10.11 or 12) picks .NET 9 or .NET 10 and the Jellyfin packages, as in `picture-shrinker`. |
| The project folder, named as the DLL, such as `Jellyfin.Plugin.AudioDescription` | The plug-in's csproj and code. |
| `tests/<project>.Tests` | Its tests, run for both builds. |
| `images/logo.png` | Its own picture in Jellyfin's catalogue (16:9, such as 1280 by 720 pixels). Each plug-in has its own; there is no shared picture. |
| `tools/make-logo.py`, or the tool that draws the logo | So the logo can be drawn again. |
| `README.md`, `CHANGELOG.md` | How to use it, and what changed in each version. |
| `rules.json`, `SYNC.md` | Only when a Starboard plug-in follows the same rules, as Picture shrinker's do. |

Each plug-in's entry in `manifest.json` has its own `imageUrl`, its folder's `images/logo.png` on main, such as
`https://raw.githubusercontent.com/TheMightyTRex/jellyfin-plugins/main/picture-shrinker/images/logo.png`.
`tools/manifest.py` writes it and `python3 tools/manifest.py check` fails when an entry points anywhere else.

To bring in a plug-in that has its own git history, such as `audio-description`, keep that history with
`git subtree add --prefix=audio-description <its repository> main`, as Picture shrinker came in. Then move its
`manifest.json`, `.github` and shared tools out of the folder (the ones at the top replace them), add a row to the
table in README.md, and release it with a tag such as `audio-description-v1.0.1.0`. Its entry is added to
`manifest.json` by that first release.

## Writing

British English, sentence case, no em dashes and no contractions, as in the TRex Design System.
