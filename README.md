# <img width="24px" src="./Logo/256.png" alt="Sportarr"></img> Sportarr

A PVR for sports content. If you've used Sonarr or Radarr before, you'll feel right at home.

Sportarr monitors sports leagues and events, searches your indexers for releases, and handles file renaming, organization, and integrates with media servers like Plex.

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

**Important:** The default `PUID=99` and `PGID=100` match Unraid's default "nobody" user. If you need different permissions, override these values to match your user (run `id` to find your UID/GID).

The `/config` volume stores your database and settings. The `/sports` volume is your media library root folder.

**Note:** Unlike older versions, Sportarr does NOT require a `/downloads` volume mapping. Like Sonarr/Radarr, it gets download paths dynamically from your download client's API. If your download client and Sportarr see different paths (common in Docker), use **Remote Path Mappings** in Settings > Download Clients to translate between them.

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

2. **Download Client** - Settings > Download Clients. Add your download client (qBittorrent, Transmission, SABnzbd, NZBGet, etc.). If using Docker, make sure both containers can access the same download path.

3. **Indexers** - Settings > Indexers. Add your Usenet indexers or torrent trackers. Sportarr supports Newznab and Torznab APIs, so Prowlarr integration works out of the box.

4. **Add Content** - Use the search to find leagues or events. Add them to your library and Sportarr will start monitoring.

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

For motorsport with multi-part episodes enabled (up to 5 parts):
```
Motorsport - s2024e05 - pt1 - Event Title.mkv  (Practice)
Motorsport - s2024e05 - pt2 - Event Title.mkv  (Qualifying)
Motorsport - s2024e05 - pt3 - Event Title.mkv  (Race)
```

You can customize the naming format in Settings > Media Management.

## Supported Download Clients

**Usenet:** SABnzbd, NZBGet

**Torrents:** qBittorrent, Transmission, Deluge, rTorrent

## Media Server Agents

Sportarr provides metadata agents for Plex and Jellyfin that fetch posters, banners, descriptions, and episode organization from sportarr.net.

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
