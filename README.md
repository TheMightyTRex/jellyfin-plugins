# Jellyfin plug-ins by TheMightyTRexUK

One repository address for every Jellyfin plug-in by TheMightyTRexUK. Add it to Jellyfin once and every plug-in here
appears in Jellyfin's catalogue, with its updates.

```
https://raw.githubusercontent.com/TheMightyTRex/jellyfin-plugins/main/manifest.json
```

You do not need Starboard: each plug-in works on any Jellyfin 10.11 or Jellyfin 12 server. GPLv3 licence.

> [!IMPORTANT]
> **Picture shrinker: after you install or update it, choose a backup option, or nothing is shrunk.** In Jellyfin,
> open **Dashboard**, **Plugins**, **Picture shrinker**, then **Settings**. Under **Original pictures**, choose **Keep a
> backup copy of each original** or **Shrink without a backup**, then **Save**. There is no default. Until you choose,
> the Dashboard's **Activity** list reminds you and **Shrink pictures** shows as failed in **Scheduled Tasks**.

## The plug-ins

| Plug-in | What it does | Folder |
|---|---|---|
| Picture shrinker | Shrinks the posters, backdrops, logos, photos of people and other pictures Jellyfin keeps, so they take less space. | [picture-shrinker](picture-shrinker) |
| Audio description | Finds and tags the films and series with a described sound track, plays it for the people who choose it, and adds a description track you have. Not released yet. | [audio-description](audio-description) |

Each plug-in has two builds, one for Jellyfin 12 (.NET 10) and one for Jellyfin 10.11 (.NET 9). Jellyfin installs the
one for its own version.

## Add the repository to Jellyfin

You need an administrator account.

1. Open Jellyfin in a web browser and sign in as an administrator.
2. Open the menu, then **Dashboard**, then **Plugins**.
3. Choose **Manage Repositories** (on Jellyfin 10.11: the **Repositories** tab), then **New Repository** (on 10.11:
   **+**). Fill in:
   - **Repository Name:** `TheMightyTRexUK`
   - **Repository URL:** `https://raw.githubusercontent.com/TheMightyTRex/jellyfin-plugins/main/manifest.json`
4. Choose **Add** (on 10.11: **Save**).
5. Back on **Plugins**, choose **Available** (on 10.11: the **Catalogue** tab). Each plug-in here is listed with its
   own picture. Open one, choose **Install**, then restart Jellyfin.

Each plug-in's README says how to set it up after the restart.

### If you added the old Picture shrinker address

Picture shrinker used to have its own address,
`https://raw.githubusercontent.com/TheMightyTRex/jellyfin-plugin-picture-shrinker/main/manifest.json`. It still
works for the versions it already lists, but new versions are listed only here. Add the address in 'Add the
repository to Jellyfin', then remove the old one under **Manage Repositories**. What you installed stays installed,
with its settings. Starboard makes this change for you the next time it offers Jellyfin plug-ins.

## How the repository is arranged

- `manifest.json`: the one list Jellyfin reads, with an entry for each plug-in and every version of it.
- One folder for each plug-in, such as `picture-shrinker`, with its own project, tests, README, CHANGELOG, build.yaml
  and logo (`images/logo.png`).
- `tools`: shared by every plug-in. `dotnet.sh` builds and tests in Docker, `package.sh` makes a release zip and its
  MD5 checksum, and `manifest.py` updates one plug-in's entry in `manifest.json`.
- `.github/workflows`: **Build and test** builds and tests every plug-in for both Jellyfin versions on each change.
  **Release** publishes one plug-in.

[CONTRIBUTING.md](CONTRIBUTING.md) says how to build, release and add a plug-in.

## Licence

GNU General Public Licence version 3 ([LICENSE](LICENSE)), as Jellyfin plug-ins need. Copyright 2026 TheMightyTRexUK.
