# What is new

## 1.2.1.0 for Jellyfin 12 and 1.2.0.0 for Jellyfin 10.11

Rules 1.2, the same as the Starboard plug-in 1.2.0.

Added:

- **All picture types, each on by default, with Shrink all picture types; backdrops asked for by Lentzeris in issue #2.** Besides posters and photos of people, it now shrinks backdrops (1,920 pixels wide), thumbnails (1,280), logos (800), banners (1,000), disc art (500) and clear art (800), each with its own size. Shrink all picture types, on by default, shrinks every type; turn it off to tick the types yourself. PNG pictures such as logos keep their transparency.
- **Choose the backup:** Original pictures on the settings page has two choices, Keep a backup copy of each original and Just shrink, with no backup, and no default. Nothing is shrunk until you choose: a scheduled run, a new picture and Shrink now each say so, and the settings page shows a notice. Restore, Delete backup and Start again still work.
- **After the update from 1.1:** the old on and off backup setting is no longer used, so you choose again. A backup you already have is kept whichever you choose.
- **Start again: the pictures it shrinks:** deletes the backup and the records (your choice of backup is kept), moves each picture of every type it shrinks aside, whatever is ticked, and has Jellyfin download it again at full size. The old picture is kept until Jellyfin has a new one, and put back after a day if it has none.
- **Start again: every picture:** deletes the backup and the records, and has Jellyfin replace every picture of each library and each person, backdrops and logos included.
- **Space saved** says how many pictures are waiting for Jellyfin, replaced and put back.

## 1.1.1.0 for Jellyfin 12 and 1.1.0.0 for Jellyfin 10.11

The first version for Jellyfin, with rules 1.1 (the same as the Starboard plug-in 1.1.0), in two builds from the same
source: 1.1.1.0 for Jellyfin 12 (.NET 10) and 1.1.0.0 for Jellyfin 10.11 (.NET 9). Jellyfin installs the one for its
own version.

- **Shrinks Jellyfin's pictures:** posters to 900 pixels tall and photos of cast and crew to 400 pixels wide, both settings. Smaller pictures are left alone.
- **Checked before it replaces:** each smaller copy is written beside the original, opened again and decoded in full, and swapped in only when it has the right size and format and is smaller.
- **Backup first:** each original is copied to a backup folder before its first shrink. Restore original pictures puts them back; Delete backup frees the space when you are sure.
- **When you choose:** a task in Scheduled Tasks, every Sunday at 03:30 until you change it, at the lowest priority. New pictures can be shrunk soon after Jellyfin saves them (deactivated by default).
- **Space:** the pictures now, the backup and the space saved, on the settings page. A finished run sends no notification.
