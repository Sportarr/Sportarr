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

1. Go to **IPTV > Options > Providers** and add your M3U playlist URL or Xtream Codes provider. Sportarr syncs the provider and applies automatic channel and guide matching.

2. Go to **IPTV > Channels** to review the imported lineup. The default view shows every channel. Use **Needs attention** for channels Sportarr could not finish automatically, and open **Manage** only when you need bulk or manual tools.

3. Go to **IPTV > Options > Recording** to set the recording path, padding, and concurrency. Less common encoding, hardware, naming, retention, reconnect, and catchup controls stay under **Advanced**.

4. Use **IPTV > Recordings** to switch between upcoming, active, completed, and recordings that need attention. Manual scheduling and bulk management are available from **More**.

5. When you monitor an event whose league has a mapped channel, a recording is scheduled automatically.

External tools can change a scheduled recording's channel, fallback channels,
time window, or quality through the [assignment API](../APPLICATION_API.md#scheduled-dvr-assignments).
The update leaves other recording fields intact. Send `expectedChannelId` to
reject a stale channel choice.

!!! tip "Keeping a league off DVR"
    Each league has an **Automatic DVR scheduling** toggle, available as an **Enable IPTV DVR** checkbox when adding the league and as its own toggle on the league detail page (DVR section) afterward. Turn it off to keep a league on indexer downloads only; the auto-scheduler will never resolve a channel or schedule recordings for it, including through EPG/broadcaster matching with no channel manually mapped, while manual recordings still work. This is what lets you run, say, Formula 1 through indexers only while recording football over IPTV.

## Live channel preview

Channel previews start through the normal stream proxy. When network or media
playback continues to fail after the bounded browser retry budget, Sportarr
automatically tries FFmpeg HLS in stream-copy mode. It does not automatically
enable video normalization because normalization re-encodes the stream and uses
more CPU.

Open **Playback details** for manual recovery and diagnostics. It contains
**Retry**, **Restart FFmpeg**, **Live edge**, the live playback profile, and the
normalization control.

- **Low latency** keeps a smaller buffer and follows the live edge closely.
- **Balanced** is the default for normal playback.
- **Resilient** uses longer buffers and more retries for unstable sources.

The selected profile applies only to the current browser session. It changes
browser buffering and retry behavior without restarting FFmpeg.

Enable **Normalize FFmpeg video** when a source still stutters or produces
invalid segments in the default stream-copy mode. Normalization re-encodes the
video as H.264 and uses more CPU. Concurrent viewers share one compatible
FFmpeg preview process. Closing one viewer no longer stops playback for the
others.

## Live event timing

**IPTV > Options > Recording > Advanced** has controls for event-linked live captures.
They use fresh source livescores for the exact event and league. They do not
infer a final result from EPG times, a missing scoreboard, or a disconnected stream.

- **Overtime Guard** extends a recording in ten-minute steps when the scheduled
  end plus padding arrives and the source still reports play in progress.
- **Early Finish Guard** is off by default. When enabled, it stops a recording
  before its scheduled end only after two separate source fetches confirm a
  final result at least one minute apart.
- **Post-Event Buffer (Minutes)** keeps recording after the first final
  observation. The default is five minutes, with a range of zero through 60.
  Zero still requires the second fresh confirmation. This buffer replaces the
  remaining scheduled padding only when the early-finish check succeeds.

Repeated reads of the same cached snapshot do not count as another confirmation.
Source observations older than two minutes, missing observations, conflicting
status fields, and results for another event or league cannot stop a recording
early. New live or inconclusive evidence resets the confirmation period.
Restarting Sportarr also starts confirmation again.

If the source does not cover an event, Sportarr keeps the normal schedule and
padding. Scheduled stops still respect Overtime Guard when fresh in-progress
evidence is available. Unlinked manual recordings and catchup downloads are
excluded from Early Finish Guard. A confirmed early finish uses the normal
finalization and import process.

Live status requires the metadata API's league livescore support. Older or custom
API servers that return no source observations leave the normal schedule in place.
The source's retrieval timestamp records when data was fetched; it cannot
guarantee that the provider's result is correct or that an IPTV stream has no delay.
Choose a buffer that allows for your stream's delay and desired postgame coverage.

Channel selection still uses your preferences, EPG matches, broadcaster names,
and league mappings. Live scores establish event progress, not what is visible
on the selected channel.

Event TV listings supply all known broadcasters for an event. Sportarr combines
their network, channel, and streaming-service names when matching your IPTV
lineup. Duplicate names are removed. An empty or unavailable event lookup keeps
the existing broadcast information. A successful event lookup takes precedence
over the broader daily listings during that sync.

TheSportsDB channel IDs identify broadcasters in its catalog. They are not the
channel numbers or stream IDs assigned by your IPTV provider. Your channel
preferences and EPG matches still apply, and a broadcast listing cannot confirm
what is currently playing on a stream.

**Auto Map** and **Sync Now** also use these event broadcasters when suggesting
channels for a league. The mapper checks events from the previous seven days
through the next fourteen days. A matching broadcaster can create a suggestion
without a known network association or EPG match. Repeated events strengthen
the evidence, while duplicate listings for one event count only once.

Matching uses channel names and guide names, with common region prefixes and
quality labels removed. Channel numbers and `+` remain significant, so a
listing for one numbered or premium channel does not identify another. Manual
mappings and exclusions remain in place. The mapping explanation identifies
the event-broadcast evidence. Run Auto Map or Sync Now after the TV schedule
sync to refresh these suggestions; broadcaster data does not trigger a separate
remapping job or prove what is currently on the channel.

## Stream reconnection

The **Stream Reconnection** section of **IPTV > Options > Recording > Advanced** controls how a recording rides out a dropped or slow stream.

- **Enable auto-reconnect** tells ffmpeg to retry when the stream drops. Retrying a failure during connection setup needs an ffmpeg build of 4.4 or newer. Older builds still retry once the stream has started.
- **Max Retry Wait** (5 to 300 seconds) caps the wait between retries. Waits grow from one second up to this cap, and retries stop once the next wait would pass it. Raise it for sources that refuse a cold stream for the first few seconds.
- **Read Timeout** (0 to 120 seconds) bounds how long ffmpeg waits for stream data, to catch a dead source faster than the recording watchdog's two minutes. 0 sets no limit and is the default. If your sources start cold streams slowly, keep it at 0 or above the slowest start you see, or the recording aborts during that wait.

## TV Guide

The TV Guide provides an EPG-style grid of your IPTV channels and their programming:

- **EPG sources** - add XMLTV EPG sources to populate programming
- **Time navigation** - browse in 6-hour increments
- **Filters** - show only scheduled recordings, sports channels, or enabled channels
- **DVR integration** - scheduled recordings are highlighted
- **Quick scheduling** - click any program to view details and schedule a recording

Access it from **IPTV > Guide** in the navigation.

EPG downloads are limited to 256 MB by default. Change **EPG download limit
(MB)** under **IPTV > Options > Advanced > Refresh schedule and limits** when a
provider supplies a larger XMLTV file. The allowed range is 1 through 512 MB.
A separate 512 MB limit still applies after decompression to protect Sportarr
from damaged or unexpectedly large compressed guides.

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

The exports respect your channel options. Hidden channels are excluded and only enabled channels are included. Subscription URLs are shown under **IPTV > Options > Advanced > External App Subscription**.

## Use Sportarr as a network tuner (Plex/Jellyfin/Emby Live TV)

Sportarr can also emulate a [SiliconDust HDHomeRun](https://www.silicondust.com/) network tuner. Instead of exporting a playlist for a third-party IPTV app, this lets Plex, Jellyfin, or Emby add Sportarr directly as a Live TV tuner and pull your enabled, sports-tagged IPTV channels as its channel lineup. This is always on. It requires no additional configuration beyond having IPTV channels enabled under **IPTV > Channels**.

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
- Stream reconnection depends on the provider and on your ffmpeg build (see Stream reconnection above)
- Limited error handling for stream failures
- File size estimation is approximate
