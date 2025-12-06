# <img width="24px" src="./Logo/256.png" alt="Sportarr"></img> Sportarr

A PVR for sports content. If you've used Sonarr or Radarr before, you'll feel right at home.

Sportarr monitors sports leagues and events, searches your indexers for releases, and sends them to your download client. It handles file renaming, organization, and integrates with media servers like Plex.

## What It Does

- Tracks events across all major sports (MMA, football, soccer, basketball, motorsport, etc.)
- Searches Usenet and torrent indexers automatically
- Manages quality upgrades when better releases become available
- Organizes files with customizable naming schemes
- Supports multi-part events (prelims, main cards) for combat sports
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
      - PUID=1000
      - PGID=1000
      - TZ=America/New_York
    volumes:
      - /path/to/sportarr/config:/config
      - /path/to/sports:/sports
      - /path/to/downloads:/downloads
    ports:
      - 1867:1867
    restart: unless-stopped
```

**Important:** Make sure `PUID` and `PGID` match the user that owns your media folders. You can find these by running `id` in your terminal.

The `/config` volume stores your database and settings. The `/sports` volume is your media library root folder. The `/downloads` volume should point to where your download client saves completed files.

After starting the container, access the web UI at `http://your-server-ip:1867`.

### Docker Run

```bash
docker run -d \
  --name=sportarr \
  -e PUID=1000 \
  -e PGID=1000 \
  -e TZ=America/New_York \
  -p 1867:1867 \
  -v /path/to/sportarr/config:/config \
  -v /path/to/sports:/sports \
  -v /path/to/downloads:/downloads \
  --restart unless-stopped \
  sportarr/sportarr:latest
```

### Unraid

Sportarr is available in the Community Applications. Search for "sportarr" and install from there.

### Windows / Linux / macOS

Download the latest release from the [releases page](https://github.com/Sportarr/Sportarr/releases). Extract and run the executable. Configuration is stored in your user's application data folder.

## Initial Setup

1. **Root Folder** - Go to Settings > Media Management and add a root folder. This is where Sportarr will store your sports library.

2. **Download Client** - Settings > Download Clients. Add your download client (qBittorrent, Transmission, SABnzbd, NZBGet, etc.). If using Docker, make sure both containers can access the same download path.

3. **Indexers** - Settings > Indexers. Add your Usenet indexers or torrent trackers. Sportarr supports Newznab and Torznab APIs, so Prowlarr integration works out of the box.

4. **Add Content** - Use the search to find leagues or events. Add them to your library and Sportarr will start monitoring.

## Prowlarr Integration

If you use Prowlarr, you can sync your indexers automatically:

1. In Prowlarr, go to Settings > Apps
2. Add Sportarr as an application
3. Use `http://sportarr:1867` as the URL (or your actual IP/hostname)
4. Get your API key from Sportarr's Settings > General

Indexers will sync automatically and stay updated.

## File Naming

Sportarr uses a TV show-style naming convention that works well with Plex:

```
/sports/MMA League/Season 2024/MMA League - s2024e12 - Event Title - 1080p.mkv
```

For combat sports with multi-part episodes enabled:
```
MMA League - s2024e12 - pt1 - Event Title.mkv  (Prelims)
MMA League - s2024e12 - pt2 - Event Title.mkv  (Main Card)
```

You can customize the naming format in Settings > Media Management.

## Supported Download Clients

**Usenet:** SABnzbd, NZBGet

**Torrents:** qBittorrent, Transmission, Deluge, rTorrent

## Plex Metadata Agent

Sportarr includes a Plex metadata agent that fetches posters, banners, descriptions, and proper episode organization from the Sportarr API.

### Installing the Agent

1. Download `Sportarr.bundle` from the [Sportarr-API releases](https://github.com/Sportarr/Sportarr-api/releases)

2. Copy the entire `Sportarr.bundle` folder to your Plex plugins directory:

| Platform | Path |
|----------|------|
| Windows | `%LOCALAPPDATA%\Plex Media Server\Plug-ins\` |
| macOS | `~/Library/Application Support/Plex Media Server/Plug-ins/` |
| Linux | `/var/lib/plexmediaserver/Library/Application Support/Plex Media Server/Plug-ins/` |
| Docker | `/config/Library/Application Support/Plex Media Server/Plug-ins/` |

3. Restart Plex Media Server

### Creating a Sports Library

1. In Plex, click the + button next to your libraries
2. Select **TV Shows** as the library type
3. Add your sports media folder
4. Click **Advanced** and select **Sportarr** as the agent
5. Click **Add Library**

The agent expects files organized in Sportarr's default naming format:

```
/Sports/League Name/Season 2024/League Name - S2024E01 - Event Title - 1080p.mkv
```

Multi-part events:
```
League Name - S2024E01 - pt1 - Event Title.mkv
League Name - S2024E01 - pt2 - Event Title.mkv
```

### Self-Hosted API

By default, the agent connects to `https://sportarr.net`. If you're running your own Sportarr-API instance, set the `SPORTARR_API_URL` environment variable on your Plex server:

```yaml
services:
  plex:
    environment:
      - SPORTARR_API_URL=http://sportarr-api:3000
```

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `PUID` | User ID for file permissions | `1000` |
| `PGID` | Group ID for file permissions | `1000` |
| `TZ` | Timezone | `UTC` |
| `SPORTARR__SERVER__PORT` | Web UI port | `1867` |

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
