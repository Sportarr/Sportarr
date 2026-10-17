# File Naming

Sportarr uses a TV show-style naming convention that works well with Plex, Jellyfin, Emby, and Kodi:

```
/data/Sports League/Season 2024/Sports League - s2024e12 - Event Title - 1080p - sportarr-ev-2338110.mkv
```

For fighting sports with multi-part episodes enabled:

```
Sports League - s2024e12 - pt1 - Event Title - sportarr-ev-2338110.mkv  (Early Prelims)
Sports League - s2024e12 - pt2 - Event Title - sportarr-ev-2338110.mkv  (Prelims)
Sports League - s2024e12 - pt3 - Event Title - sportarr-ev-2338110.mkv  (Main Card)
```

Customize the naming format in **Settings > Media Management**.

When Sportarr renames or moves a video, it also moves subtitle files beside it that share its old filename. For example, `Event.en.srt` follows `Event.mkv` to the new name and folder. Subtitles with a different base filename are left alone.

If the new subtitle name is already taken, Sportarr keeps the older file in a `.sportarr-conflicts` folder beside the video. Check that folder before removing any files from it.

![Naming Settings](../images/naming-settings.png)

## Grouping events within seasons

Under **Settings > Media Management > Folders**, enable **Create League Folders** and **Create Season Folders** to use **Group Events by Type or Session**. Sportarr then places recognized event types between the season folder and the optional event folder. For example, WWE events can use `WWE/Season 2026/RAW/` or `WWE/Season 2026/PLE/`. Other supported leagues can group fighting event types or motorsport sessions. Unrecognized types go in `Other`, while leagues without grouping rules keep their existing folder layout.

This setting is off by default and does not change episode numbers. It applies to new imports. To move existing files into the new layout, enable **Reorganize Folders on Rename** and click **Save Settings**. Then select the affected leagues on the Leagues page, choose **Rename Files**, review the path preview, and confirm.

For the release title patterns Sportarr's parser understands per sport, see the [Release Naming reference](../RELEASE_NAMING.md).

## The Sportarr id token

TV libraries use season and episode numbers to recognize a file as an episode. Sports events do not have a common event ID across those libraries, so Sportarr supplies one. `{Sportarr Id}` writes that event's ID into the filename as `sportarr-ev-2338110`. It works like a TVDB or IMDb ID for lookup. When that ID exists in Sportarr's catalog, imports and metadata agents can use it to select the exact event, even if episode numbering later changes.

Use **both** `{Season}{Episode}` and `{Sportarr Id}` for reliable matching in Plex, Jellyfin, and Emby TV libraries. Jellyfin and Emby must first recognize season and episode numbers before their Sportarr providers can read the ID. An ID alone is not a reliable replacement for TV-style episode naming. Numbers alone can match, but they depend on the right league and current episode order. Kodi can also read the ID from the `.nfo` file Sportarr writes next to the video.

The built-in naming presets include both markers, except **Original Filename**. That option preserves the release name, so its matching depends on what the release group supplied. If **Rename Events** is off, Sportarr also keeps the source filename. Settings warns about both cases without blocking them.

The preset dropdown shows the preset that matches your saved format. If you edited the format, or a built-in preset changed since you saved it, the dropdown shows **Custom format** and leaves your saved format alone. Select a current preset to replace it.

## Changing the format

A format change applies to files imported after it and to files you rename yourself, from a league page or from a season's file list, where the preview covers that season or the files you selected. Existing files are renamed on their own only when their season or episode number changes, so a renumbered season keeps its files identifiable in your media server. The same goes for folders: a change to the folder format, or a league renamed at the source, does not move existing files. Use Rename Files for that.
