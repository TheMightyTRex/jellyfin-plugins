# What is new

## 1.1.0.0

The first version for Jellyfin, with rules 1.1 (the same as the Starboard plug-in 1.1.0).

- **Shrinks Jellyfin's pictures:** posters to 900 pixels tall and photos of cast and crew to 400 pixels wide, both settings. Smaller pictures are left alone.
- **Checked before it replaces:** each smaller copy is written beside the original, opened again and decoded in full, and swapped in only when it has the right size and format and is smaller.
- **Backup first:** each original is copied to a backup folder before its first shrink. Restore original pictures puts them back; Delete backup frees the space when you are sure.
- **When you choose:** a task in Scheduled Tasks, every Sunday at 03:30 until you change it, at the lowest priority. New pictures can be shrunk soon after Jellyfin saves them (deactivated by default).
- **Space:** the pictures now, the backup and the space saved, on the settings page. A finished run sends no notification.
