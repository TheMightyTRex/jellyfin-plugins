[Help centre](../README.md) · Each plug-in · reference
<!-- help: a11y / plugins -->

# Picture shrinker's accessibility

Picture shrinker's settings page is built from Jellyfin's own controls, so it works with a keyboard, a screen reader
and Jellyfin's display settings in the same way as the rest of the Dashboard. This page lists what it adds, and its
limits.

## When would you use it

- You use a keyboard, a screen reader, voice control or a magnifier, and want to know how the settings page behaves.
- You need to know where Picture shrinker tells you things, as it sends no notification.

## The settings page

- **Every control has a visible label** that is also its name for a screen reader and for voice control, such as
  **Shrink all picture types** or **Posters no taller than (pixels)**. Each one's hint is read with it.
- **Groups have names:** **What to shrink**, **Original pictures**, **Picture types**, **Sizes**, **Backup folder**
  and **When to shrink** are each a group with its own name, and **Space**, **Start again** and **Last runs** are
  headings, so a screen reader can move between them.
- **The two backup choices are radio buttons** with no default, so neither is announced as chosen until you choose.
  The notice at the start of the page links to **Original pictures** and moves the focus there.
- **Buttons that cannot be used say why.** **Delete backup**, **Shrink now** and the two **Start again** buttons stay
  in the Tab order while they cannot be used, and their reason, such as "There is no backup to delete", is read with
  them.
- **Results are announced.** "Saved at 16:31", "Measured again." and what each action did are written in a polite live
  region, so a screen reader reads them without moving the focus. A problem with the folders is written as an alert.
- **Dialogs keep the focus.** **Delete the backup?** and the **Start again** questions open with the focus on
  **Cancel**, and Escape closes them. The focus goes back to the button that opened them.
- **Last runs is a table** with column headers, in a region you can reach with Tab and scroll with the arrow keys on a
  narrow page.
- **Focus is visible:** a blue outline marks the control with the focus.
- **Text and dates** use the English (United Kingdom) format, such as "Tue, 17 Nov 2026, 14:56".

## Where it tells you things

- **Before you choose a backup option:** a notice on the settings page, an entry in the Dashboard's **Activity** list
  (Jellyfin has no way for a plug-in to show a pop-up), and **Shrink pictures** shown as failed in **Scheduled Tasks**.
- **After a run:** only the figures on the settings page. It sends no notification.

## Limits

- **Jellyfin's own Dashboard** around the settings page, such as **Scheduled Tasks** and the trigger dialog, is
  Jellyfin's, and works as Jellyfin makes it.
- **The page follows Jellyfin's theme.** The warning and notice borders use a fixed amber, and the figures sit on a
  light grey band, in every theme.
- **Smaller pictures carry less detail.** For someone who uses a magnifier on posters or photos of people, choose
  larger sizes: read [Choose which pictures to shrink, and their sizes](../picture-shrinker/choose-what-to-shrink.md).

## How it is checked

Its words follow the design system's content guide: British English, sentence case, and labels that say what they do.
A unit test checks the settings page's wording on every build. To report a problem, open an issue on
[the repository on GitHub](https://github.com/TheMightyTRex/jellyfin-plugins/issues).

## Find out more

- [Install Picture shrinker](../picture-shrinker/install.md)
- [Audio description's accessibility](audio-description.md)
- [Picture shrinker is not shrinking pictures](../fix/picture-shrinker-problems.md)
