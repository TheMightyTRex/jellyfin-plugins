[Help centre](../README.md) · Adding the plug-ins · troubleshooting
<!-- help: fix / adding -->

# A plug-in is not in Jellyfin's catalogue

A plug-in by TheMightyTRexUK is missing from **Available** (on a **Plugins** page with tabs, the **Catalogue** tab), or a newer
version does not appear. Follow the chart to find the cause.

```flow
title: Find your answer
start: The plug-in is not in Available
q: Is TheMightyTRexUK in Manage Repositories?
  No -> 1
  Yes -> q2
q2: Is the address exactly the one in this help?
  No -> 2
  Yes -> q3
q3: Which plug-in is missing?
  Audio description -> 3
  Picture shrinker -> q4
q4: Is your Jellyfin 10.11 or 12?
  No, an earlier or later one -> 4
  Yes -> 5
1: Add the repository #1-add-the-repository
2: Correct the address #2-correct-the-address
3: Audio description is not released yet #3-audio-description-is-not-released-yet
4: Use Jellyfin 10.11 or 12 #4-use-jellyfin-1011-or-12
5: Let Jellyfin read the repository again #5-let-jellyfin-read-the-repository-again
```

## 1. Add the repository

Jellyfin lists only the plug-ins of the repositories it has. Follow
[Add the repository to Jellyfin](../start/add-the-repository.md). If you added Picture shrinker's first address, read
[Move from the old Picture shrinker address](../start/move-from-the-old-address.md): it does not list new versions.

## 2. Correct the address

1. Go to **Dashboard**, **Plugins**, then **Manage Repositories**.
2. Compare the address with this one, letter for letter. A space at the start or end, or a missing part, stops it
   working:

   ```text
   https://raw.githubusercontent.com/TheMightyTRex/jellyfin-plugins/main/manifest.json
   ```

3. If it differs, select the delete button beside it, then add it again with **New Repository**.

## 3. Audio description is not released yet

<!-- not-released: audio-description -->
Audio description is not in the repository yet, so no Jellyfin lists it. Picture shrinker is listed.

## 4. Use Jellyfin 10.11 or 12

Each plug-in has a build for Jellyfin 10.11 and one for Jellyfin 12, and Jellyfin lists only builds for its own
version or earlier. To find your version, go to **Dashboard**: it is under the server name in the Dashboard's menu.
Jellyfin 10.10 and earlier are not supported. Update Jellyfin to use the plug-ins.

## 5. Let Jellyfin read the repository again

Jellyfin reads each repository from the internet when the **Plugins** page opens.

1. Check that the server can reach the internet: other plug-ins, from **Jellyfin Stable**, are listed in
   **Available**.
2. Go to another page and back to **Plugins**, then select **Available** again.
3. If a newer version is missing, run **Update Plugins**, under **Application** in **Scheduled Tasks**, and restart
   Jellyfin.
4. If it is still missing, restart Jellyfin.

## Find out more

- [Add the repository to Jellyfin](../start/add-the-repository.md)
- [Update Picture shrinker, or go back to an earlier version](../picture-shrinker/update-and-roll-back.md)
- [Audio description is not working](audio-description-problems.md)
