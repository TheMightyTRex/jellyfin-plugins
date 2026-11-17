[Help centre](../README.md) · Each plug-in · reference
<!-- help: a11y / plugins -->

# Audio description's accessibility

Audio description is made for blind and partially sighted people: it marks the films and series with a described
sound track in every Jellyfin app, and plays that track for the people who want it. Its settings page is
built from Jellyfin's own controls. This page lists what it does for the people who watch, how the settings page
behaves, and its limits.

## When would you use it

- You, or someone in your household, is blind or partially sighted and wants described films.
- You use a keyboard, a screen reader or voice control, and want to know how the settings page behaves.

## For the people who watch

- **The tag works in every app.** Every film and episode with a described track, and its series, has the tag **Audio
  description**, and the films and series are in the **Audio description** collection. Every Jellyfin app shows tags
  and collections, televisions included, so nobody needs a special app to find them.
- **The described track can play by itself.** For each person ticked under **Play the described track**, Jellyfin
  asks their app to change to the described track when they start a title. Read
  [Play the described track for the people who want it](../audio-description/play-the-described-track.md).
- **An added track is marked as described:** titled such as **English (Audio Description)**, with the flag for people
  with a visual impairment, and never the default track, so people who do not want it are not given it.

## The settings page

- **Every control has a visible label** that is also its name for a screen reader and for voice control. Each hint is
  read with its control.
- **Groups have names:** **Find and tag**, **Play the described track**, **Add a track you have** and **Upload a
  description track**. The people under **Play the described track** are a named group of tick boxes.
- **Results are announced** in a polite live region, such as "Saved at 16:31" or what **Find and tag now** did.
- **Tracks in the folder is a table** with a caption and column headers: **Track**, **State**, **What happened** and
  **What you can do**. The state is a word, never a colour alone. A list of titles to choose from is named after its
  track, such as "Film or episode for The Long Field (2022).mp3".
- **The file chooser** is named **Choose a description track**, and choosing **Upload the track** with no file says
  so and moves the focus to the chooser.
- **Focus is visible:** a blue outline marks the control with the focus.
- **Two tabs, Settings and Titles with audio description,** are links in a named group, **Audio description pages**,
  and the open one is marked as the current page.

## Titles with audio description

- **Headings:** the page's title, then **Films** and **Series**, then a heading for each series.
- **Show** is a group of three radio buttons, **All**, **Films** and **Series**, each with its count. After a change,
  a polite live region says what is shown, such as "Showing 1 film."
- **Tables:** each has a caption and column headers, and each row's first cell is its header, the title as a link to
  its page. The status is a word, **Described**, and each series' status is a sentence, such as "2 of 3 episodes
  described", never a colour alone.
- Read [Titles with audio description](../audio-description/titles-with-audio-description.md).

## Limits

- **Not every Jellyfin app accepts the command to change the sound track.** In those apps, the person chooses the
  described track in the player; with **Set audio track based on previous item** active in their **Playback**
  settings, each next episode of a series plays with the track they chose.
- **The words decide what counts as described.** A described track named only "English 2" is not found: read
  [Audio description is not working](../fix/audio-description-problems.md).
- **Jellyfin's own Dashboard and players** work as Jellyfin makes them.

## How it is checked

Its words follow the design system's content guide, and a unit test checks the settings page's wording on every
build. The track words, the tag and the collection are shared with Starboard's version and tested with the same
examples in both. To report a problem, open an issue on
[the repository on GitHub](https://github.com/TheMightyTRex/jellyfin-plugins/issues).

## Find out more

- [Find the films and series with audio description](../audio-description/find-and-tag.md)
- [Picture shrinker's accessibility](picture-shrinker.md)
- [Install Audio description](../audio-description/install.md)
