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
   - **Sportarr API URL**: Your Sportarr instance URL (default: `http://localhost:3000`)
   - **Enable Debug Logging**: Toggle for troubleshooting
   - **Image Cache Hours**: How long to cache images locally
3. Click **Test Connection** to verify connectivity
4. Click **Save**
5. Restart Jellyfin

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
| `/api/metadata/plex/search` | Search for leagues |
| `/api/metadata/plex/series/{id}` | Get league metadata |
| `/api/metadata/plex/series/{id}/season/{num}/episodes` | Get events |
| `/api/images/league/{id}/poster` | League poster |
| `/api/images/event/{id}/thumb` | Event thumbnail |

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
