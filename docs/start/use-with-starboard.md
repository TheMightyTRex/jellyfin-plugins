[Help centre](../README.md) · With Starboard · reference
<!-- help: use / starboard -->

# Use the plug-ins with Starboard

Both plug-ins also have a Starboard version, in Starboard's plug-in catalogue. You need Starboard for neither. If you
use Starboard, choose the Jellyfin versions: they add no container, so they use the least memory, and each Starboard
version stands aside when its Jellyfin version is installed.

## When would you use it

- You run Starboard, a dashboard that sets up Jellyfin and the apps around it, and want to know which version to
  install.
- You have a Starboard version already and want to move to the Jellyfin version.
- A Starboard plug-in's card says **Idle**, and you want to know why.

## The versions

| Jellyfin plug-in (this repository) | Starboard plug-in | What the Starboard plug-in does while the Jellyfin one is installed |
|---|---|---|
| Picture shrinker | Picture shrinker | Stays idle and says so on its card, so the two never shrink the same pictures |
| Audio description | Audio description | Leaves the tag, the collection and **Play the described track** to the Jellyfin version; its card says **Idle**. Its other parts, such as preferring releases with audio description in Reelix, carry on |
| Audio description | Audio description tracks | Adds nothing and its card says **Idle**, so the two never add the same track |

Each pair follows the same rules, kept in one `rules.json` in this repository: the same sizes and checks for
pictures, and the same words, tag, collection, file names and length check for audio description. A picture or a
track never depends on which version did the work. Read
[Picture shrinker for developers: the rules and SYNC.md](../developers/picture-shrinker.md).

## Which to choose

**Choose the Jellyfin versions.** Starboard's main design rule is to stay light on smaller servers, and the Jellyfin
versions run inside Jellyfin, so they add no container of their own.

| | Jellyfin version | Starboard version |
|---|---|---|
| Where it runs | Inside Jellyfin | Its own container beside Jellyfin |
| Memory at rest (Picture shrinker) | About 15 MB more than Jellyfin without it | 13 MB, its whole container |
| Works without Starboard | Yes, on any Jellyfin 10.11 or 12 | No |
| Settings | In Jellyfin's Dashboard, **Plugins** | In Starboard, **Settings**, **Plug-ins** |
| Find and tag (Audio description) | After each library scan and every night | Every night |
| Play the described track (Audio description) | As soon as playback starts | Up to about half a minute into the film |

A Starboard version keeps its memory apart from Jellyfin's, so a problem in it can never slow Jellyfin. Choose one
only if that matters more to you than the extra container.

## Install the Jellyfin version from Starboard

Starboard can install Picture shrinker in Jellyfin for you, in **Settings**, **Apps**, **Jellyfin**, under its
recommended Jellyfin plug-ins: choose **Install and activate** under **Picture shrinker**, then **Apply this choice**,
and restart Jellyfin when Starboard asks. Then choose a backup option in Jellyfin: read
[Install Picture shrinker](../picture-shrinker/install.md), steps 11 to 13.

When Starboard next offers Jellyfin plug-ins, it also replaces Picture shrinker's first repository address with the
new one. Read [Move from the old Picture shrinker address](move-from-the-old-address.md).

## Find out more

- [Add the repository to Jellyfin](add-the-repository.md)
- [Starboard's help centre](https://themightytrex.github.io/starboard/help/)
- [The Starboard plug-ins' help centre](https://themightytrex.github.io/starboard-plugins/help/)
- [Starboard on GitHub](https://github.com/TheMightyTRex/starboard)
