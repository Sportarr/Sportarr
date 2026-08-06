# IPTV DVR Recording

!!! warning "Alpha feature"
    IPTV DVR functionality is in early alpha. Expect bugs, missing features, and rough edges while this is being developed. Use at your own risk and please report issues.

Sportarr includes experimental support for recording live sports events directly from IPTV streams using FFmpeg.

## What it does

- **IPTV source management** - add M3U playlists or Xtream Codes providers
- **Channel-to-league mapping** - map IPTV channels to leagues for automatic recording
- **Automatic DVR scheduling** - when you monitor an event, Sportarr can automatically schedule a recording if the league has a mapped channel (or a channel resolvable through EPG and broadcaster matching)
- **FFmpeg recording** - records streams in transport stream format
- **Auto-import** - completed recordings are imported into your event library
- **TV Guide** - EPG-style grid showing channels and programming with recordings highlighted
- **Filtered M3U/EPG export** - serve filtered playlists and EPG data to external IPTV apps

## Requirements

- FFmpeg installed and accessible in the system PATH (bundled in the Docker image)
- A working IPTV source, either an M3U playlist or Xtream Codes credentials

## Setup

1. Go to **Settings > IPTV Sources** and add your M3U playlist URL or Xtream Codes provider

    ![IPTV Sources](../images/iptv-sources.png)

2. Go to **Settings > IPTV Channels** to view imported channels and map them to leagues

    ![IPTV Channels](../images/iptv-channels.png)

3. Go to **Settings > DVR Recordings** to configure recording settings and view scheduled or completed recordings

    ![DVR Recordings](../images/dvr-recordings.png)

4. When you monitor an event whose league has a mapped channel, a recording is scheduled automatically

!!! tip "Keeping a league off DVR"
    Each league has an **Automatic DVR scheduling** toggle, available as an **Enable IPTV DVR** checkbox when adding the league and as its own toggle on the league detail page (DVR section) afterward. Turn it off to keep a league on indexer downloads only; the auto-scheduler will never resolve a channel or schedule recordings for it, including through EPG/broadcaster matching with no channel manually mapped, while manual recordings still work. This is what lets you run, say, Formula 1 through indexers only while recording football over IPTV.

## TV Guide

The TV Guide provides an EPG-style grid of your IPTV channels and their programming:

- **EPG sources** - add XMLTV EPG sources to populate programming
- **Time navigation** - browse in 6-hour increments
- **Filters** - show only scheduled recordings, sports channels, or enabled channels
- **DVR integration** - scheduled recordings are highlighted
- **Quick scheduling** - click any program to view details and schedule a recording

Access it from **IPTV > TV Guide** in the navigation.

## Filtered M3U/EPG export

Sportarr can serve filtered playlists and EPG data for external IPTV apps like TiviMate or IPTV Smarters:

- Filtered M3U: `http://your-server:1867/api/iptv/filtered.m3u`
- Filtered EPG: `http://your-server:1867/api/iptv/filtered.xml`

Optional query parameters:

| Parameter | Effect |
|---|---|
| `sportsOnly=true` | Only sports channels |
| `favoritesOnly=true` | Only favorite channels |
| `sourceId=X` | Only channels from a specific source |

The exports respect your channel settings: hidden channels are excluded and only enabled channels are included. Subscription URLs are shown in **Settings > IPTV Sources** under "External App Subscription URLs".

## Use Sportarr as a network tuner (Plex/Jellyfin/Emby Live TV)

Sportarr can also emulate a [SiliconDust HDHomeRun](https://www.silicondust.com/) network tuner. Instead of exporting a playlist for a third-party IPTV app, this lets Plex, Jellyfin, or Emby add Sportarr directly as a Live TV tuner and pull your enabled, sports-tagged IPTV channels as its channel lineup. This is always on - there's no setting to enable it, and it requires no additional configuration beyond having IPTV channels enabled under **Settings > IPTV Channels**.

Downstream players tune channels through Sportarr's own stream proxy, so the same channel enable/disable and favorites settings that control the M3U/EPG export above also control what shows up as a tuner channel.

### Plex

Plex's tuner setup screen relies on network discovery (SSDP), which Sportarr does not broadcast. Add it manually instead:

1. Go to **Settings > Live TV & DVR > Set Up Plex DVR**
2. If Plex doesn't auto-discover a tuner, choose the option to enter one manually and enter your Sportarr host and port, e.g. `your-server:1867`
3. Plex fetches the channel lineup and guide data from Sportarr the same way it would from a real HDHomeRun device

### Jellyfin

1. Go to **Dashboard > Live TV > Tuner Devices > Add**
2. Choose **HDHomeRun** as the tuner type
3. Enter the URL: `http://your-server:1867`

### Emby

1. Go to **Live TV > Setup > Tuner Devices > Add**
2. Choose **HDHomeRun** as the tuner type
3. Enter the IP address or URL of your Sportarr instance

### How it works

Sportarr implements the three endpoints the HDHomeRun HTTP API requires (`/discover.json`, `/lineup.json`, `/lineup_status.json`) plus `/device.xml` for Plex's manual-add fallback, all served unauthenticated on the main Sportarr port like a real tuner. No separate service or port is involved.

## Known limitations

- Recording quality depends entirely on your IPTV source
- Stream reconnection may not work reliably with all providers
- Limited error handling for stream failures
- No hardware acceleration support yet
- File size estimation is approximate
