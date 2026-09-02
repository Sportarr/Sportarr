# File Naming

Sportarr uses a TV show-style naming convention that works well with Plex, Jellyfin, Emby, and Kodi:

```
/data/Sports League/Season 2024/Sports League - s2024e12 - Event Title - 1080p.mkv
```

For fighting sports with multi-part episodes enabled:

```
Sports League - s2024e12 - pt1 - Event Title.mkv  (Early Prelims)
Sports League - s2024e12 - pt2 - Event Title.mkv  (Prelims)
Sports League - s2024e12 - pt3 - Event Title.mkv  (Main Card)
```

Customize the naming format in **Settings > Media Management**.

![Naming Settings](../images/naming-settings.png)

For the release title patterns Sportarr's parser understands per sport, see the [Release Naming reference](../RELEASE_NAMING.md).

## The Sportarr id token

`{Sportarr Id}` writes the event's id into the name as `sportarr-ev-2338110`. Imports and rescans read it back and match the file to its event exactly, so a renamed or moved file never lands on the wrong event. The media server agents do not use it at all. Plex, Jellyfin, Emby and Kodi match the show by the league folder name and each event by the season and episode numbers in the file name, which is why Sportarr keeps those numbers current on its own.

## Changing the format

A format change applies to files imported after it and to files you rename yourself, from a league page or from a season's file list, where the preview covers that season or the files you selected. Existing files are renamed on their own only when their season or episode number changes, so a renumbered season keeps its files identifiable in your media server. The same goes for folders: a change to the folder format, or a league renamed at the source, does not move existing files. Use Rename Files for that.
