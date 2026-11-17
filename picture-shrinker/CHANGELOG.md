# Changelog

All notable changes to Picture shrinker for Jellyfin are listed here, newest first. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), with dates written as in the TRex Design System. Each
release has two builds, one for Jellyfin 12 and one for Jellyfin 10.11, and the version numbers follow SYNC.md, not
Semantic Versioning.

## [Unreleased]

## [1.2.5.0] for Jellyfin 12 and [1.2.4.0] for Jellyfin 10.11 (17 November 2026)

Rules 1.2 are unchanged, so the Starboard plug-in keeps the same rules. Wording from the design system audit of 17
November 2026.

### Changed

- **Shrink without a backup:** the backup choice that keeps no copy of the originals has this name. Under Original pictures, the two choices are Keep a backup copy of each original and Shrink without a backup. A choice you already made is kept.
- **Clearer wording on the settings page:** Shrink all picture types says Active and Deactivate, each part of the page is named instead of "above" or "below", the warning and the notice start with what they are, and the two confirmation dialogs put their main button first.
- **The time of the last save:** after Save, the settings page says when it saved, such as "Saved at 16:31".
- **A plain reason when a backup copy fails:** a picture that is not shrunk because its original could not be copied says to check that Jellyfin can write to the backup folder, then gives the technical details.

## [1.2.3.0] for Jellyfin 12 and [1.2.2.0] for Jellyfin 10.11 (17 November 2026)

**Choose a backup option after you install or update, or nothing is shrunk.** Open Dashboard, Plugins, Picture shrinker, then Settings. Under Original pictures, choose Keep a backup copy of each original or Just shrink, with no backup, then save. Updating from 1.1 asks again; a backup you already have is kept.

### Added

- **A reminder in the Activity list:** until you choose, Jellyfin's Dashboard shows "Picture shrinker is waiting for you to choose" in its Activity list when Jellyfin starts, and again at most once a day when a run or a new picture is skipped. Jellyfin has no way for a plug-in to show a pop-up, so this is where an administrator sees it. Once you choose, it writes nothing there.

### Changed

- **Shrink pictures shows as failed until you choose:** in Scheduled Tasks, the task ends as failed with the reason, instead of looking as if it worked. A finished run still sends no notification.

Rules 1.2 are unchanged, so the Starboard plug-in stays at 1.2.0.

## [1.2.1.0] for Jellyfin 12 and [1.2.0.0] for Jellyfin 10.11 (17 November 2026)

Rules 1.2, the same as the Starboard plug-in 1.2.0.

### Added

- **All picture types, each on by default, with Shrink all picture types; backdrops asked for by Lentzeris in issue #2.** Besides posters and photos of people, it now shrinks backdrops (1,920 pixels wide), thumbnails (1,280), logos (800), banners (1,000), disc art (500) and clear art (800), each with its own size. Shrink all picture types, on by default, shrinks every type; turn it off to tick the types yourself. PNG pictures such as logos keep their transparency.
- **Choose the backup:** Original pictures on the settings page has two choices, Keep a backup copy of each original and Just shrink, with no backup, and no default. Nothing is shrunk until you choose: a scheduled run, a new picture and Shrink now each say so, and the settings page shows a notice. Restore, Delete backup and Start again still work.
- **After the update from 1.1:** the old on and off backup setting is no longer used, so you choose again. A backup you already have is kept whichever you choose.
- **Start again: the pictures it shrinks:** deletes the backup and the records (your choice of backup is kept), moves each picture of every type it shrinks aside, whatever is ticked, and has Jellyfin download it again at full size. The old picture is kept until Jellyfin has a new one, and put back after a day if it has none.
- **Start again: every picture:** deletes the backup and the records, and has Jellyfin replace every picture of each library and each person, backdrops and logos included.
- **Space saved** says how many pictures are waiting for Jellyfin, replaced and put back.

## [1.1.1.0] for Jellyfin 12 and [1.1.0.0] for Jellyfin 10.11 (15 November 2026)

The first version for Jellyfin, with rules 1.1 (the same as the Starboard plug-in 1.1.0), in two builds from the same
source: 1.1.1.0 for Jellyfin 12 (.NET 10) and 1.1.0.0 for Jellyfin 10.11 (.NET 9). Jellyfin installs the one for its
own version.

### Added

- **Shrinks Jellyfin's pictures:** posters to 900 pixels tall and photos of cast and crew to 400 pixels wide, both settings. Smaller pictures are left alone.
- **Checked before it replaces:** each smaller copy is written beside the original, opened again and decoded in full, and swapped in only when it has the right size and format and is smaller.
- **Backup first:** each original is copied to a backup folder before its first shrink. Restore original pictures puts them back; Delete backup frees the space when you are sure.
- **When you choose:** a task in Scheduled Tasks, every Sunday at 03:30 until you change it, at the lowest priority. New pictures can be shrunk soon after Jellyfin saves them (deactivated by default).
- **Space:** the pictures now, the backup and the space saved, on the settings page. A finished run sends no notification.

[Unreleased]: https://github.com/TheMightyTRex/jellyfin-plugin-picture-shrinker/compare/v1.2.3.0...HEAD
[1.2.5.0]: https://github.com/TheMightyTRex/jellyfin-plugin-picture-shrinker/compare/v1.2.3.0...v1.2.5.0
[1.2.4.0]: https://github.com/TheMightyTRex/jellyfin-plugin-picture-shrinker/compare/v1.2.3.0...v1.2.5.0
[1.2.3.0]: https://github.com/TheMightyTRex/jellyfin-plugin-picture-shrinker/compare/v1.2.1.0...v1.2.3.0
[1.2.2.0]: https://github.com/TheMightyTRex/jellyfin-plugin-picture-shrinker/compare/v1.2.1.0...v1.2.3.0
[1.2.1.0]: https://github.com/TheMightyTRex/jellyfin-plugin-picture-shrinker/compare/v1.1.1.0...v1.2.1.0
[1.2.0.0]: https://github.com/TheMightyTRex/jellyfin-plugin-picture-shrinker/compare/v1.1.1.0...v1.2.1.0
[1.1.1.0]: https://github.com/TheMightyTRex/jellyfin-plugin-picture-shrinker/releases/tag/v1.1.1.0
[1.1.0.0]: https://github.com/TheMightyTRex/jellyfin-plugin-picture-shrinker/releases/tag/v1.1.1.0
