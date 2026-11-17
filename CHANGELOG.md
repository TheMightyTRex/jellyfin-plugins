# Changelog

All notable changes to Audio description for Jellyfin are listed here, newest first. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), with dates written as in the TRex Design System. Each
release has two builds, one for Jellyfin 12 and one for Jellyfin 10.11, and the version numbers follow SYNC.md, not
Semantic Versioning.

## [Unreleased]

## [1.0.1.0] for Jellyfin 12 and [1.0.0.0] for Jellyfin 10.11 (17 November 2026)

Rules 1.0, the same as the Starboard plug-ins Audio description 1.3.0 and Audio description tracks 1.2.0.

### Added

- **Find and tag:** every night at 04:20 and after each library scan, each film and episode with a described sound track, and its series, gets the tag Audio description, and the tag comes off when the track goes. Every Jellyfin app, televisions included, can filter by it.
- **The Audio description collection:** active by default, the films and series with a described track are in one collection.
- **Play the described track:** for each person ticked on the settings page, inactive until chosen. When they start a title with a described track and another track plays, Jellyfin asks their app to change to it. Not every app accepts this; the log says when one does not.
- **Add a track you have:** a description track in the folder, or uploaded on the settings page, is matched to its film or episode by its name, its length compared with the film's, and added next to the film as English (Audio Description) when they agree within 5 seconds. The sound is copied without converting it, with Jellyfin's own ffmpeg. Yes and No on the settings page put the original away or take the track off. Nothing is ever deleted.
