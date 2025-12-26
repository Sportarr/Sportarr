# <img width="24px" src="./Logo/256.png" alt="Sportarr"></img> Sportarr

Sportarr - Sports PVR for usenet and torrents. Like Sonarr & Radarr but for sports events.

Sportarr monitors sports leagues and events, searches your indexers for releases, and handles file renaming, organization, and integrates with media servers like Plex.

![Sportarr Dashboard](docs/images/dashboard.png)

## What It Does

- Tracks events across all major sports (fighting sports, football, soccer, basketball, racing, etc.)
- Searches Usenet and torrent indexers automatically
- Manages quality upgrades when better releases become available
- Organizes files with customizable naming schemes
- Supports multi-part events (prelims, main cards) for fighting sports
- Integrates with Plex, Jellyfin, Emby for library updates

## Installation

### Docker (Recommended)

```yaml
version: "3.8"
services:
  sportarr:
    image: sportarr/sportarr:latest
    container_name: sportarr
    environment:
      - PUID=99
      - PGID=100
      - UMASK=022
      - TZ=America/New_York  # Optional: Set your timezone
    volumes:
      - /path/to/sportarr/config:/config
      - /path/to/sports:/sports
    ports:
      - 1867:1867
    restart: unless-stopped
```

The `/config` volume stores your database and settings. The `/sports` volume is your media library root folder.

After starting the container, access the web UI at `http://your-server-ip:1867`.

### Docker Run

```bash
docker run -d \
  --name=sportarr \
  -e PUID=99 \
  -e PGID=100 \
  -e UMASK=022 \
  -e TZ=America/New_York \
  -p 1867:1867 \
  -v /path/to/sportarr/config:/config \
  -v /path/to/sports:/sports \
  --restart unless-stopped \
  sportarr/sportarr:latest
```

### Unraid

Sportarr will be available in the Unraid Community Applications after official launch. Search for "sportarr" and install from there. The app is currently in alpha testing and remaining more hidden for limited visibility during this phase.

### Windows / Linux / macOS

Download the latest release from the [releases page](https://github.com/Sportarr/Sportarr/releases). Extract the archive for your platform and run the executable.

By default, configuration is stored in a `data` subdirectory where you run Sportarr from. You can specify a custom location using the `-data` argument:

```bash
# Windows
Sportarr.exe -data C:\ProgramData\Sportarr

# Linux/macOS
./Sportarr -data /var/lib/sportarr
```

Or set the `Sportarr__DataPath` environment variable:

```bash
# Linux/macOS
export Sportarr__DataPath=/var/lib/sportarr
./Sportarr

# Windows PowerShell
$env:Sportarr__DataPath = "C:\ProgramData\Sportarr"
.\Sportarr.exe
```

**Priority order:** Command-line `-data` argument > Environment variable > Default `./data`

## Initial Setup

1. **Root Folder** - Go to Settings > Media Management and add a root folder. This is where Sportarr will store your sports library.

   ![Add Root Folder](docs/images/add-root-folder.png)

2. **Download Client** - Settings > Download Clients. Add your download client (qBittorrent, Transmission, Deluge, rTorrent, uTorrent, SABnzbd, NZBGet, or Decypharr). If using Docker, make sure both containers can access the same download path.

   ![Add Download Client](docs/images/add-download-client.png)

3. **Indexers** - Settings > Indexers. Add your Usenet indexers or torrent trackers. Sportarr supports Newznab and Torznab APIs, so Prowlarr integration works out of the box.

   ![Add Indexer](docs/images/add-indexer.png)

4. **Add Content** - Use the search to find leagues or events. Add them to your library and Sportarr will start monitoring.

   ![Search for Leagues](docs/images/search-league.png)

   ![Team Selection](docs/images/search-league-teams.png)

   ![League Detail View](docs/images/league-detail.png)

## Supported Download Clients

**Usenet:** SABnzbd, NZBGet, NZBdav

**Torrents:** qBittorrent, Transmission, Deluge, rTorrent, uTorrent

**Debrid/Proxy:** Decypharr (torrents and usenet)

## Prowlarr Integration

If you use Prowlarr, you can sync your indexers automatically:

1. In Prowlarr, go to Settings > Apps
2. Add **Sonarr** as an application (Sportarr isn't in Prowlarr yet, but the Sonarr option works)
3. Use `http://localhost:1867` as the URL (or your actual IP/hostname)
4. Get your API key from Sportarr's Settings > General
5. Select **TV (5000)** categories for sync - this includes TV/HD (5040), TV/UHD (5045), and TV/Sport (5060)

Indexers will sync automatically and stay updated.

## File Naming

Sportarr uses a TV show-style naming convention that works well with Plex:

```
/sports/Sports League/Season 2024/Sports League - s2024e12 - Event Title - 1080p.mkv
```

For fighting sports with multi-part episodes enabled:
```
Sports League - s2024e12 - pt1 - Event Title.mkv  (Early Prelims)
Sports League - s2024e12 - pt2 - Event Title.mkv  (Prelims)
Sports League - s2024e12 - pt3 - Event Title.mkv  (Main Card)
```

You can customize the naming format in Settings > Media Management.

![Naming Settings](docs/images/naming-settings.png)

## Media Server Agents

Sportarr provides metadata agents for Plex and Jellyfin that fetch posters, banners, descriptions, and episode organization from sportarr.net.

![Media Server Agents](docs/images/media-server-agents.png)

### Plex

Sportarr supports two methods for Plex integration:

#### Custom Metadata Provider (Recommended)

For **Plex 1.43.0+**, use the new Custom Metadata Provider system. No plugin installation required!

1. Open **Plex Web** and go to **Settings → Metadata Agents**
2. Click **+ Add Provider**
3. Enter the URL: `https://sportarr.net/plex`
4. Click **+ Add Agent** and give it a name (e.g., "Sportarr")
5. **Restart Plex Media Server**
6. Create a **TV Shows** library, select your sports folder, and choose the **Sportarr** agent

#### Legacy Bundle Agent

For older Plex versions, download the legacy bundle from Sportarr UI (Settings > General > Media Server Agents) and copy to your Plex Plug-ins directory. Note: Plex has announced legacy agents will be deprecated in 2026.

See [agents/plex/README.md](agents/plex/README.md) for detailed instructions and troubleshooting.

### Jellyfin

1. Build the plugin or download from releases:
   ```bash
   cd agents/jellyfin/Sportarr
   dotnet build -c Release
   ```

2. Copy the DLL to your Jellyfin plugins directory:
   - Docker: `/config/plugins/Sportarr/`
   - Windows: `%APPDATA%\Jellyfin\Server\plugins\Sportarr\`
   - Linux: `~/.local/share/jellyfin/plugins/Sportarr/`

3. Restart Jellyfin

4. Create a library: select **Shows**, add your sports folder, enable **Sportarr** under Metadata Downloaders

See [agents/jellyfin/README.md](agents/jellyfin/README.md) for detailed instructions.

## IPTV DVR Recording (Alpha)

> ⚠️ **ALPHA FEATURE WARNING**: The IPTV DVR functionality is in very early alpha stage. Expect bugs, missing features, and poor functionality while this feature is being developed. Use at your own risk and please report any issues you encounter.

Sportarr includes experimental support for recording live sports events directly from IPTV streams using FFmpeg.

### Features (Work in Progress)

- **IPTV Source Management** - Add M3U playlists or Xtream Codes providers
- **Channel-to-League Mapping** - Map IPTV channels to leagues for automatic recording
- **Automatic DVR Scheduling** - When you monitor an event, Sportarr can automatically schedule a DVR recording if the league has a mapped channel
- **FFmpeg Recording** - Records IPTV streams in transport stream format
- **Auto-Import** - Completed recordings are automatically imported into your event library
- **TV Guide** - EPG-style grid showing channels and their programming with DVR recordings highlighted
- **Filtered M3U/EPG Export** - Serve filtered playlists and EPG data to external IPTV apps

### Requirements

- FFmpeg must be installed and accessible in your system PATH
- A working IPTV source (M3U playlist or Xtream Codes credentials)

### Known Limitations

- Recording quality depends entirely on your IPTV source
- Stream reconnection may not work reliably with all providers
- Limited error handling for stream failures
- No hardware acceleration support yet
- File size estimation is approximate

### Setup

1. Go to Settings > IPTV Sources and add your M3U playlist URL or Xtream Codes provider

   ![IPTV Sources](docs/images/iptv-sources.png)

2. Go to Settings > IPTV Channels to view imported channels and map them to leagues

   ![IPTV Channels](docs/images/iptv-channels.png)

3. Go to Settings > DVR Recordings to configure recording settings and view scheduled/completed recordings

   ![DVR Recordings](docs/images/dvr-recordings.png)

4. When you monitor an event, if its league has a mapped channel, a DVR recording will be automatically scheduled

### TV Guide

The TV Guide provides an EPG-style grid view of your IPTV channels and their programming:

- **EPG Sources** - Add XMLTV EPG sources to populate channel programming
- **Time-based Navigation** - Browse programming in 6-hour increments
- **Filters** - Show only scheduled recordings, sports channels, or enabled channels
- **DVR Integration** - Scheduled recordings are highlighted in the guide
- **Quick Scheduling** - Click any program to view details and schedule a DVR recording

Access the TV Guide from IPTV > TV Guide in the navigation menu.

### Filtered M3U/EPG Export for External Apps

Sportarr can serve filtered M3U playlists and EPG data for use with external IPTV applications like TiviMate, IPTV Smarters, or other players:

- **Filtered M3U** - `http://your-server:1867/api/iptv/filtered.m3u`
- **Filtered EPG** - `http://your-server:1867/api/iptv/filtered.xml`

Optional query parameters:
- `sportsOnly=true` - Only include sports channels
- `favoritesOnly=true` - Only include favorite channels
- `sourceId=X` - Only include channels from a specific source

The filtered exports respect your channel settings - hidden channels are excluded, and only enabled channels are included. Find the subscription URLs in Settings > IPTV Sources under "External App Subscription URLs".

This feature is under active development. Feedback and bug reports are welcome!

## Troubleshooting

**Can't connect to download client in Docker?**
Use the container name (e.g., `qbittorrent`) instead of `localhost`. Make sure both containers are on the same Docker network.

**Files not importing?**
Check that the download path is accessible from within the Sportarr container. The path your download client reports needs to be the same path Sportarr sees.

**Indexer errors?**
Check your API keys and make sure you haven't hit rate limits. Logs are available in System > Logs.

## Support

- [Discord](https://discord.gg/YjHVWGWjjG) - Best place for quick help
- [GitHub Issues](https://github.com/Sportarr/Sportarr/issues) - Bug reports and feature requests
- [GitHub Discussions](https://github.com/Sportarr/Sportarr/discussions) - General questions

## Building from Source

Requires .NET 8 SDK and Node.js 20+.

```bash
git clone https://github.com/Sportarr/Sportarr.git
cd Sportarr

# Backend
dotnet build src/Sportarr.Api.csproj

# Frontend
cd frontend
npm install
npm run build
```

## License

GNU GPL v3 - see [LICENSE.md](LICENSE.md)

---

Sportarr is based on Sonarr. Thanks to the Sonarr team for the foundation.
