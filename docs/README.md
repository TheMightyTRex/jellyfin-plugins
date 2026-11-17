# Jellyfin plug-ins help centre

Help for the Jellyfin plug-ins by TheMightyTRexUK: two free plug-ins for Jellyfin 10.11 and Jellyfin 12, from one
repository address. You do not need Starboard for either. The help centre is also at
[themightytrex.github.io/jellyfin-plugins](https://themightytrex.github.io/jellyfin-plugins/).

- **Picture shrinker** makes the posters, backdrops, logos, photos of people and other pictures Jellyfin keeps
  smaller, so they take less space. Each smaller copy is checked before it replaces the original, and you choose
  whether the originals are backed up first.
- **Audio description** finds and tags the films and series with a described sound track, for blind and partially
  sighted people, plays that track for the people who choose it, and adds a description track you have. It is not
  released yet.

## Start here: add the repository

Add this address to Jellyfin once, and both plug-ins appear in its catalogue, with their updates:

```text
https://raw.githubusercontent.com/TheMightyTRex/jellyfin-plugins/main/manifest.json
```

In Jellyfin, go to **Dashboard**, **Plugins**, **Manage Repositories**, then **New Repository**. Read
[Add the repository to Jellyfin](start/add-the-repository.md) for every step, with pictures.

## Get started

- [Add the repository to Jellyfin](start/add-the-repository.md)
- [Move from the old Picture shrinker address](start/move-from-the-old-address.md)

## Use it

- [Use the plug-ins with Starboard](start/use-with-starboard.md)

## Plug-ins

### Picture shrinker

- [Install Picture shrinker](picture-shrinker/install.md)
- [Choose which pictures to shrink, and their sizes](picture-shrinker/choose-what-to-shrink.md)
- [Keep a backup, and put the original pictures back](picture-shrinker/backup-and-restore.md)
- [Start again with full-size pictures](picture-shrinker/start-again.md)
- [Choose when Picture shrinker runs](picture-shrinker/when-it-runs.md)
- [Check how much space you saved](picture-shrinker/space-saved.md)
- [Update Picture shrinker, or go back to an earlier version](picture-shrinker/update-and-roll-back.md)

### Audio description

- [Install Audio description](audio-description/install.md)
- [Find the films and series with audio description](audio-description/find-and-tag.md)
- [Play the described track for the people who want it](audio-description/play-the-described-track.md)
- [Add a description track you have](audio-description/add-a-track.md)

## Accessibility

- [Picture shrinker's accessibility](accessibility/picture-shrinker.md)
- [Audio description's accessibility](accessibility/audio-description.md)

## Fix a problem

- [A plug-in is not in Jellyfin's catalogue](fix/plug-in-not-in-the-catalogue.md)
- [Picture shrinker is not shrinking pictures](fix/picture-shrinker-problems.md)
- [Audio description is not working](fix/audio-description-problems.md)

## Developers

- [Picture shrinker for developers: the rules and SYNC.md](developers/picture-shrinker.md)
- [Audio description for developers](developers/audio-description.md)
- [Build, test and release a plug-in](developers/build-and-release.md)

## How this help is written

Every article follows the TRex Design System's help writing guide and content guide: British English, sentence case,
one task to an article, and at least 3 marked-up screenshots for every task, taken from a test copy of Jellyfin with
made-up films and people.
