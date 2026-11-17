[Help centre](../README.md) · Audio description · troubleshooting
<!-- help: fix / audio-description -->

# Audio description is not working

A film with a described track has no tag, the described track does not play by itself, or a track you added was not
added. Follow the chart to find the cause.

```flow
title: Find your answer
start: Audio description is not doing what you expect
q: What is wrong?
  A described film has no tag -> q2
  The described track does not play by itself -> q3
  A track I added was not added -> 5
q2: Has Find and tag run since the film came in?
  No, or I am not sure -> 1
  Yes -> 2
q3: Is the person ticked under Play the described track?
  No -> 3
  Yes -> 4
1: Run Find and tag #1-run-find-and-tag
2: Check the sound track's title #2-check-the-sound-tracks-title
3: Tick the person #3-tick-the-person
4: Choose the track in the app #4-choose-the-track-in-the-app
5: Read the track's state #5-read-the-tracks-state
```

## 1. Run Find and tag

Find and tag runs every night at 04:20 and after each library scan, so a new film may not be tagged yet.

1. Go to **Dashboard**, **Plugins**, **Installed**, select **Audio description**, then **Settings**.
2. Select **Find and tag now**.
3. Read **Last run**: it says what it checked, tagged and took the tag off. "Did not finish" gives the reason.

If Jellyfin has not scanned the film since its file changed, scan the library first: **Dashboard**, then **Scan All
Libraries**.

## 2. Check the sound track's title

A track counts as audio description only when its title, the title Jellyfin shows or its comment has one of the words
in [How a track is found](../audio-description/find-and-tag.md#how-a-track-is-found), such as **Audio Description**,
**Described** or **AD**, and no **commentary**.

1. On the film's page, open **Audio** and read the tracks' names.
2. If the described track has a name such as "English 2" or "Stereo", the plug-in cannot tell it is described. Give
   the track a title with one of the words, with a tool such as MKVToolNix, then scan the library and run Find and tag
   again.

## 3. Tick the person

**Play the described track** is inactive for everyone until you tick someone.

1. On Audio description's settings, under **Play the described track**, tick the person.
2. Select **Save**.

Read [Play the described track for the people who want it](../audio-description/play-the-described-track.md).

## 4. Choose the track in the app

Not every Jellyfin app accepts the command to change the sound track. When an app does not, Jellyfin's log says so,
with the app's name: "does not accept the command to change the sound track".

1. In that app, choose the described track in the player, or in **Audio** on the title's page.
2. In the person's **Settings**, **Playback**, activate **Set audio track based on previous item**, so each next
   episode of a series plays with the track they chose.

The plug-in also changes nothing when the described track is already playing, or when the person moves to another
title within 2 seconds of starting.

## 5. Read the track's state

On Audio description's settings, under **Tracks in the folder**, each track has a **State** and **What happened**:

- **Lengths differ:** the track is for another cut of the film. Find one for your cut, or line it up yourself.
- **No match** or **Choose the title:** rename the file as the film and its year, such as `The Long Field (2022).mp3`,
  or a series with its episode, such as `Quiet Valley S01E03.mp3`; or choose the right title, then **Check against
  this title**.
- **Out of reach:** Jellyfin cannot open the film's file, so it cannot add a track next to it. Check that Jellyfin's
  user can write in the film's folder.
- **Cannot read:** the file is not sound the plug-in can read. Check that it plays, then put it in the folder again.
- **Cannot read film:** the film's length could not be read. Check that the film plays in Jellyfin.
- **Not added:** **What happened** gives the reason.

Read [Add a description track you have](../audio-description/add-a-track.md#the-states) for every state.

## Other things that happen

- **Starboard's Audio description plug-ins say Idle:** that is expected while this one is installed. Read
  [Use the plug-ins with Starboard](../start/use-with-starboard.md).
- **A tag you did not want:** a title with no described track loses the tag at the next run, even one you added
  yourself. Use another tag name for your own.

## Find out more

- [Find the films and series with audio description](../audio-description/find-and-tag.md)
- [Add a description track you have](../audio-description/add-a-track.md)
- [Audio description's accessibility](../accessibility/audio-description.md)
