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

Sportarr will be available in the Unraid Community Applications after official launch. Search for "sportarr" and install from there. The app is currently in alpha testing and remaining more hidden for limited visibility during this phase.

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
2. Add **Radarr** as an application (Sportarr isn't in Prowlarr yet, but the Radarr option works)
3. Use `http://localhost:1867` as the URL (or your actual IP/hostname)
4. Get your API key from Sportarr's Settings > General
5. Select both **Movies (2000)** and **TV (5000)** for sync categories

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

## Media Server Agents

Sportarr provides metadata agents for Plex and Jellyfin that fetch posters, banners, descriptions, and episode organization from sportarr.net.

### Plex

**Step 1: Download the Sportarr Agent**

You can get the Plex agent in two ways:

- **From Sportarr UI:** Go to Settings > General > Media Server Agents and click "Download Plex Agent"
- **Docker users:** The agent is automatically available at `/config/agents/plex/Sportarr.bundle` in your config volume

**Step 2: Copy to Plex Plugins Directory**

Copy the entire `Sportarr.bundle` folder to your Plex plugins directory:

| Platform | Copy To |
|----------|---------|
| Windows | `%LOCALAPPDATA%\Plex Media Server\Plug-ins\Sportarr.bundle` |
| macOS | `~/Library/Application Support/Plex Media Server/Plug-ins/Sportarr.bundle` |
| Linux | `/var/lib/plexmediaserver/Library/Application Support/Plex Media Server/Plug-ins/Sportarr.bundle` |
| Docker | `/config/Library/Application Support/Plex Media Server/Plug-ins/Sportarr.bundle` |

The folder structure should look like:
```
Plug-ins/
└── Sportarr.bundle/
    └── Contents/
        ├── Code/
        └── ...
```

**Step 3: Restart Plex Media Server**

The agent won't appear until Plex is restarted.

**Step 4: Create a TV Shows Library for Sports**

1. In Plex, click the **+** button to add a new library
2. Select **TV Shows** as the library type
3. Add your Sportarr root folder (the same folder you configured in Sportarr under Settings > Media Management)
4. Click **Advanced** and select **Sportarr** as the agent
5. Click **Add Library**

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

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `PUID` | User ID for file permissions | `1000` |
| `PGID` | Group ID for file permissions | `1000` |
| `TZ` | Timezone | `UTC` |

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
