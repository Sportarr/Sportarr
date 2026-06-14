# Sportarr Jellyfin Plugin

A metadata plugin for Jellyfin that fetches sports metadata from your Sportarr instance.

## Features

- **Rich metadata**: Posters, banners, fanart, descriptions, and air dates
- **Unified metadata**: Same data you see in Sportarr appears in Jellyfin
- **Multi-part support**: Handles fight cards (Early Prelims, Prelims, Main Card)
- **Motorsport support**: Practice, Qualifying, Sprint, Race sessions
- **Year-based seasons**: Uses 4-digit year format (2024, 2025) as season numbers

## Installation

### Option 1: Plugin Repository (Recommended)

The easiest way to install - Jellyfin will automatically check for updates!

1. In Jellyfin, go to **Dashboard** → **Plugins** → **Repositories**
2. Click **Add** and enter:
   - **Repository Name:** `Sportarr`
   - **Repository URL:** `https://raw.githubusercontent.com/sportarr/Sportarr/main/agents/jellyfin/manifest.json`
3. Click **Save**
4. Go to **Catalog** tab and find **Sportarr** under Metadata
5. Click **Install**
6. Restart Jellyfin

### Option 2: Download from Releases

1. Go to [GitHub Releases](https://github.com/sportarr/Sportarr/releases)
2. Find the latest `jellyfin-plugin-v*` release
3. Download the `sportarr-jellyfin-plugin_*.zip` file
4. Extract the ZIP contents to your Jellyfin plugins folder:
   - **Docker:** `/config/plugins/Sportarr/`
   - **Windows:** `%APPDATA%\Jellyfin\Server\plugins\Sportarr\`
   - **Linux:** `~/.local/share/jellyfin/plugins/Sportarr/`
   - **macOS:** `~/.local/share/jellyfin/plugins/Sportarr/`
5. Restart Jellyfin

### Option 3: Build from Source

```bash
cd agents/jellyfin/Sportarr
dotnet build -c Release
```

Copy `bin/Release/net8.0/Jellyfin.Plugin.Sportarr.dll` to your plugins directory.

## Configuration

After installing the plugin:

1. Go to **Dashboard** → **Plugins** → **Sportarr**
2. Configure settings:
   - **Sportarr API URL**: The Sportarr metadata API (default: `https://sportarr.net`). Point this at a local Sportarr instance (e.g. `http://localhost:1867`) to serve metadata from your own install; it exposes the same API.
   - **Enable Debug Logging**: Toggle for troubleshooting
   - **Image Cache Hours**: How long to cache images locally
3. Click **Test Connection** to verify connectivity
4. Click **Save**
5. Restart Jellyfin

When pointed at a local instance, episode numbers come from the same source that wrote them into your filenames, so the metadata and the files stay in sync. Each event is resolved individually via `/api/metadata/match` rather than fetching the whole season list per file.

## Library Setup

1. In Jellyfin, go to **Dashboard** → **Libraries** → **Add Media Library**
2. Select **Shows** as the content type
3. Add your sports media folder (e.g., `/media/Sports`)
4. Under **Metadata Downloaders**, enable **Sportarr**
5. Drag **Sportarr** to the top of the list (highest priority)
6. Under **Image Fetchers**, enable **Sportarr**
7. Drag **Sportarr** to the top of the list
8. Click **OK**

## File Naming Convention

The plugin expects Sportarr's file naming format for best metadata matching.

### Folder Structure

```
{League}/Season {Year}/
```

Example:
```
UFC/Season 2024/
Formula 1/Season 2025/
Premier League/Season 2024/
```

### File Format

```
{League} - S{Year}E{Episode} - {Title} - {Quality}.ext
```

Examples:
```
UFC - S2024E15 - UFC 300 McGregor vs Chandler - 1080p.mkv
Formula 1 - S2025E05 - Monaco Grand Prix - 720p WEB-DL.mkv
Premier League - S2024E38 - Arsenal vs Chelsea - 1080p.mkv
```

### Multi-Part Events

Fighting sports and motorsport events can have multiple parts:

```
{League} - S{Year}E{Episode} - pt{Part} - {Title} - {Quality}.ext
```

**Fighting Sports:**
```
UFC - S2024E15 - pt1 - UFC 300 Early Prelims - 1080p.mkv
UFC - S2024E15 - pt2 - UFC 300 Prelims - 1080p.mkv
UFC - S2024E15 - pt3 - UFC 300 Main Card - 1080p.mkv
```

**Motorsport:**
```
Formula 1 - S2025E05 - pt1 - Monaco GP Practice - 720p.mkv
Formula 1 - S2025E05 - pt2 - Monaco GP Qualifying - 720p.mkv
Formula 1 - S2025E05 - pt3 - Monaco GP Race - 1080p.mkv
```

## Automatic naming, episode numbers, and verification

**Episode numbers come from Sportarr, and they can change.** An event's episode
number is its chronological position within the season (cancelled and postponed
events are skipped), so when the upstream schedule shifts, the number shifts:

- **Running the Sportarr app?** You don't name anything. Sportarr imports,
  names, and **renames** files automatically — when an event is added,
  cancelled, or postponed it renumbers the season and renames the files on disk
  on its next sync so the library stays correct.
- **Using only this plugin?** You name files yourself. Look up the current
  episode number at <https://sportarr.net/browse> (open a league → season) and
  name to match. Case and zero-padding don't matter (`S2026E12`, `s2026e12`,
  and `s2026e012` all resolve to episode 12). The Sportarr app's naming format
  is customizable under **Settings → Media Management**.

### Verify it works

1. Place one correctly-named file in `{Series}/Season {year}/` (or let the
   Sportarr app import it) and scan the library.
2. A correct match shows the right **episode number**, **poster**, **air date**,
   and **description**.
3. No match? Confirm the library type is **Shows**, the `Season {year}` folder
   exists, and the number matches sportarr.net/browse, then use
   **Identify** to pick the league.
4. Episode number changed after a rescan? Expected — the schedule moved and
   Sportarr reflected it.

## How It Works

```
┌─────────────┐      ┌─────────────┐      ┌─────────────┐
│  Jellyfin   │──────│   Plugin    │──────│  Sportarr   │
│   Server    │      │  (This)     │      │    API      │
└─────────────┘      └─────────────┘      └─────────────┘
       │                    │                    │
       │ 1. Scan library    │                    │
       │───────────────────>│                    │
       │                    │ 2. Search leagues  │
       │                    │───────────────────>│
       │                    │<───────────────────│
       │                    │ 3. Get metadata    │
       │                    │───────────────────>│
       │                    │<───────────────────│
       │ 4. Display rich    │                    │
       │<───────────────────│                    │
       │    metadata        │                    │
```

1. **Scan**: Jellyfin scans your library and identifies shows/episodes
2. **Search**: Plugin searches Sportarr API for matching leagues
3. **Match**: Best match is selected based on name similarity and year
4. **Fetch**: Full metadata (descriptions, dates, ratings) is retrieved
5. **Images**: Posters, banners, fanart, thumbnails are fetched
6. **Display**: Rich metadata appears in your Jellyfin library

## Troubleshooting

### Plugin Not Loading
- Check Jellyfin logs: **Dashboard** → **Logs**
- Look for `[Sportarr]` entries
- Verify the DLL is in the correct plugins folder
- Ensure you're running Jellyfin 10.9.x or later

### No Metadata Found
- Ensure files follow the naming convention above
- Check that your Sportarr instance is running and accessible
- Verify the API URL in plugin configuration
- Use **Test Connection** to verify connectivity

### Wrong Metadata Matched
- Use Jellyfin's **Identify** feature to manually search and select correct league
- Ensure series folder name closely matches the league name in Sportarr
- Check that the year in "Season YYYY" matches your event years

### Images Not Loading
- Verify Sportarr API URL is correct (include http:// or https://)
- Check if images load directly in Sportarr web UI
- Try increasing Image Cache Hours in plugin settings
- Check Jellyfin logs for image fetch errors

### Connection Test Fails
- Ensure Sportarr is running and accessible from Jellyfin server
- If using Docker, use the container name or host.docker.internal
- Check firewall rules allow connections on port 3000
- Verify the URL includes the protocol (http:// or https://)

## API Endpoints Used

The plugin communicates with these Sportarr API endpoints:

| Endpoint | Purpose |
|----------|---------|
| `/api/health` | Connection test |
| `/api/metadata/agents/search` | Search for leagues |
| `/api/metadata/agents/series/{id}` | Get league metadata |
| `/api/metadata/agents/series/{id}/season/{num}/episodes` | Get events for a season |
| `/api/metadata/match?series={id}&season={num}&episode={num}` | Resolve a single event (per-file lookup) |
| `/api/metadata/agents/episode/{id}` | Get event metadata (incl. resolved thumb_url) |
| `/api/images/league/{id}/poster` | League poster image |

These endpoints are media-server agnostic and shared with the Plex and Emby agents. The hub also keeps legacy `/api/metadata/plex/*` aliases (hidden from OpenAPI) for older agent versions; new code should use `/agents/*`.

## Support

For issues, please open a GitHub issue at:
https://github.com/sportarr/Sportarr/issues

Include:
- Jellyfin version
- Plugin version
- Sportarr version
- Relevant log entries (Dashboard → Logs, filter for "Sportarr")
- Example file names that aren't matching
- Screenshots if applicable

## License

This plugin is part of Sportarr and is released under the same license.
