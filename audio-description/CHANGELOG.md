# Changelog

All notable changes to Audio description for Jellyfin are listed here, newest first. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), with dates written as in the TRex Design System. Each
release has two builds, one for Jellyfin 12 and one for Jellyfin 10.11, and the version numbers follow SYNC.md, not
Semantic Versioning.

## [Unreleased]

## [1.0.5.0] for Jellyfin 12 and [1.0.4.0] for Jellyfin 10.11 (18 November 2026)

Rules 1.0, unchanged. The same as the Starboard plug-in Audio description 1.5.0.

### Added

- **Films to list and Series to list:** on the settings page, under Titles with audio description, how many of each the Titles with audio description tab lists: All, or the last 10, 20, 50, 100 or 500 added to Jellyfin, newest first. Last 20 by default. A series counts by its newest described episode and still lists all its described episodes. Only that many are read from the library.
- **The tab says how many it shows,** such as "Showing the last 20 of 143 films and all 6 series, newest first", and where to change it.

### Changed

- **Newest first:** films and series are listed by when they were added to Jellyfin, newest first.

## [1.0.3.0] for Jellyfin 12 and [1.0.2.0] for Jellyfin 10.11 (17 November 2026)

Rules 1.0, unchanged. The same page as the Starboard plug-in Audio description 1.4.0.

### Added

- **Titles with audio description:** a page beside Settings, chosen from the tabs at the top of both pages, that lists every film and every described episode, by series, from the tags Find and tag gives. Films have their year, described track and status; each series has how many of its episodes are described, such as 3 of 6 episodes described, and its episodes in order. Show All, Films or Series, each with its count. Each title opens its page in Jellyfin. The same page, columns and words as in the Starboard plug-in.

### Fixed

- **Later Jellyfin 10.11 releases, such as 10.11.11:** the settings page loads again. They changed how a plug-in asks for the list of people, so the page could not show its figures, people or tracks.

## [1.0.1.0] for Jellyfin 12 and [1.0.0.0] for Jellyfin 10.11 (17 November 2026)

Rules 1.0, the same as the Starboard plug-ins Audio description 1.3.0 and Audio description tracks 1.2.0.

### Added

- **Find and tag:** every night at 04:20 and after each library scan, each film and episode with a described sound track, and its series, gets the tag Audio description, and the tag comes off when the track goes. Every Jellyfin app, televisions included, can filter by it.
- **The Audio description collection:** active by default, the films and series with a described track are in one collection.
- **Play the described track:** for each person ticked on the settings page, inactive until chosen. When they start a title with a described track and another track plays, Jellyfin asks their app to change to it. Not every app accepts this; the log says when one does not.
- **Add a track you have:** a description track in the folder, or uploaded on the settings page, is matched to its film or episode by its name, its length compared with the film's, and added next to the film as English (Audio Description) when they agree within 5 seconds. The sound is copied without converting it, with Jellyfin's own ffmpeg. Yes and No on the settings page put the original away or take the track off. Nothing is ever deleted.
