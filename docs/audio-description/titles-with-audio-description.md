[Help centre](../README.md) · Audio description · reference
<!-- help: plugins / audio-description -->

# Titles with audio description

**Titles with audio description** is a page of Audio description's that lists every film and every episode with a
described sound track, by series, each linking to its page in Jellyfin. It is the same page, with the same columns and
words, as **Titles with audio description** in the Starboard plug-in.

## When would you use it

- To find what can be watched with audio description, in one list, without opening each film.
- To check that a film or episode with a described track was found, and which track counts.
- To check how much of a series is described.

## Open it

1. Go to **Dashboard**, **Plugins**, **Installed**, select **Audio description**, then **Settings**.
2. Choose **Titles with audio description** in the tabs at the top of the page. **Settings** goes back to the settings.

## What it shows

The first line says how many films, and how many episodes in how many series, have a described sound track, and
when Find and tag last ran, such as "1 film and 2 episodes in 1 series have a described sound track. Checked 17
November at 04:20."

It then says how many it lists, newest first, such as "Showing the last 20 of 143 films and all 6 series, newest first,
with 11 described episodes."

**Show** chooses **All**, **Films** or **Series**, each with its count, such as **Films (1)**. The list changes at once.

### Films

A table with a row for each film, newest added first:

| Column | What it shows |
|---|---|
| **Title** | The film's name, a link to its page in Jellyfin |
| **Year** | Its year, or **Not known** |
| **Described track** | The name of its first described sound track, as Jellyfin shows it, such as "Audio Description - English - AAC - Stereo" |
| **Status** | **Described** |

### Series

A heading for each series, newest described episode first, with its year, then how many of its episodes are described and its described
track, such as "2 of 3 episodes described. Described track: English (Described)". When every episode is described,
it says so, such as "All 6 episodes described". Under it, a table of its described episodes in order:

| Column | What it shows |
|---|---|
| **Episode** | Its series and episode number and its name, such as "S1 E3: Part three", a link to its page in Jellyfin |
| **Described track** | The name of its first described sound track |
| **Status** | **Described** |

## Choose how many it lists

On the settings page, under **Titles with audio description**:

| Setting | What it does | Default |
|---|---|---|
| **Films to list** | **All**, or the **Last 10**, **Last 20**, **Last 50**, **Last 100** or **Last 500** films added to Jellyfin, newest first | Last 20 |
| **Series to list** | The same choices, for series, by their newest described episode. Each series listed still has all its described episodes | Last 20 |

Select **Save** at the end of the settings page, then choose **Titles with audio description** again.

## Where the list comes from

- It lists the films and episodes with the tag **Audio description** that still have a described track. Read
  [Find the films and series with audio description](find-and-tag.md) for how a track is found and when the tag is
  given.
- A film added since the last Find and tag is listed after the next one, which runs after each library scan and every
  night at 04:20. Select **Find and tag now** on the settings to list it straight away.
- It reads only as many tagged films and episodes as it lists, newest first, with their sound tracks and each listed
  series' number of episodes, only when the page opens. Nothing runs in the background for it.

## With a keyboard and a screen reader

- The page's parts are headings: **Titles with audio description**, then **Films** and **Series**, then each series.
- **Show** is a group of three radio buttons. After a change, a screen reader hears what is shown, such as "Showing 1
  film."
- Each table has a caption and column headers, and each row's first cell is its header, so a screen reader names the
  row by its title. The status is a word, never a colour alone.
- Each link names its title, such as "Harbour Lights" or "S1 E3: Part three".

## Find out more

- [Find the films and series with audio description](find-and-tag.md)
- [Audio description's accessibility](../accessibility/audio-description.md)
- [Audio description is not working](../fix/audio-description-problems.md)
