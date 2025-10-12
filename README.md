# <img width="24px" src="./Logo/256.png" alt="Fightarr"></img> Fightarr

**Combat Sports PVR - Automatically track and download UFC, MMA, Boxing, and other fighting events**

Fightarr is a PVR (Personal Video Recorder) for Usenet and BitTorrent users designed specifically for combat sports enthusiasts. It monitors RSS feeds for new fighting events from UFC, Bellator, Boxing, and other organizations, automatically downloading, organizing, and renaming your fight library.

## Key Features

- 🥊 **Combat Sports Focused** - Built specifically for UFC, MMA, Boxing, and other fighting events
- 📅 **Event Tracking** - Monitor upcoming fights and automatically download when available
- 🔄 **Quality Upgrades** - Automatically upgrade to better quality releases (DVD → Blu-Ray → 4K)
- 🎯 **Smart Search** - Find specific events, fighters, or organizations
- 📦 **Usenet & Torrents** - Full integration with SABnzbd, NZBGet, qBittorrent, Transmission, and more
- 🎬 **Media Server Integration** - Connect with Plex, Jellyfin, Emby, and Kodi
- 🐳 **Docker Ready** - Easy deployment with official Docker images
- 🌐 **Cross-Platform** - Windows, Linux, macOS, and ARM devices (Raspberry Pi, etc.)

## Quick Start

### Docker (Recommended)

```bash
docker run -d \
  --name=fightarr \
  -e PUID=1000 \
  -e PGID=1000 \
  -e TZ=America/New_York \
  -p 1867:1867 \
  -v /path/to/config:/config \
  -v /path/to/fights:/fights \
  -v /path/to/downloads:/downloads \
  --restart unless-stopped \
  fightarr/fightarr:latest
```

Then open `http://localhost:1867` in your browser.

### Docker Compose

```yaml
version: "3.8"
services:
  fightarr:
    image: fightarr/fightarr:latest
    container_name: fightarr
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=America/New_York
    volumes:
      - /path/to/config:/config
      - /path/to/fights:/fights
      - /path/to/downloads:/downloads
    ports:
      - 1867:1867
    restart: unless-stopped
```

**Port 1867** - The year the Marquess of Queensberry Rules were published (boxing reference!)

### Manual Installation

Download the latest release for your platform:
- [Windows](https://github.com/Fightarr/Fightarr/releases)
- [Linux](https://github.com/Fightarr/Fightarr/releases)
- [macOS](https://github.com/Fightarr/Fightarr/releases)

## Configuration

### First Time Setup

1. **Add a Root Folder** - Where Fightarr will organize your fight library
2. **Connect Download Client** - SABnzbd, NZBGet, qBittorrent, etc.
3. **Add Indexers** - Usenet indexers or torrent trackers
4. **Search for Events** - Find UFC, Boxing, or MMA events to monitor

### Recommended Folder Structure

```
/fights
├── UFC
│   ├── UFC 300 (2024)
│   │   ├── Main Card
│   │   └── Prelims
├── Boxing
│   ├── Fury vs Usyk (2024)
└── Bellator
    └── Bellator 300 (2023)
```

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `PUID` | User ID for file permissions | `1000` |
| `PGID` | Group ID for file permissions | `1000` |
| `TZ` | Timezone (e.g., `America/New_York`) | `UTC` |
| `FIGHTARR__SERVER__PORT` | Web UI port | `1867` |
| `FIGHTARR__ANALYTICS_ENABLED` | Enable analytics/telemetry | `false` |

## Integration

### Media Servers

- **Plex** - Library updates, metadata, notifications
- **Jellyfin** - Library updates and notifications
- **Emby** - Library updates and notifications
- **Kodi** - Library updates and notifications

### Download Clients

- **Usenet**: SABnzbd, NZBGet
- **Torrents**: qBittorrent, Transmission, Deluge, rTorrent

### Notifications

- Discord, Telegram, Slack
- Email, Pushbullet, Pushover
- Custom scripts and webhooks

## API

Fightarr includes a full REST API for automation and integration.

**Base URL**: `http://localhost:1867/api/v3`

**Authentication**: Include `X-Api-Key` header with your API key

Example:
```bash
curl -H "X-Api-Key: YOUR_API_KEY" http://localhost:1867/api/v3/system/status
```

## Development

### Prerequisites

- .NET 8 SDK
- Node.js 20+
- Yarn package manager

### Build from Source

```bash
# Clone the repository
git clone https://github.com/Fightarr/Fightarr.git
cd Fightarr

# Build backend
dotnet build src/NzbDrone.sln

# Build frontend
yarn install
yarn build

# Run
dotnet run --project src/NzbDrone.Console
```

### Contributing

We welcome contributions! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## Support

- 🐛 **Bug Reports**: [GitHub Issues](https://github.com/Fightarr/Fightarr/issues)
- 💬 **Discussions**: [GitHub Discussions](https://github.com/Fightarr/Fightarr/discussions)
- 📖 **Documentation**: Coming soon
- 💰 **Donate**: Support development (coming soon)

## Roadmap

- [ ] Enhanced fighter statistics and records
- [ ] Multi-language support
- [ ] Mobile app
- [ ] Advanced search filters (by weight class, organization, etc.)
- [ ] Integration with fight statistics APIs
- [ ] Automatic highlight detection

## License

[GNU GPL v3](http://www.gnu.org/licenses/gpl.html)

---

**Note**: Fightarr is a fork of Sonarr, adapted specifically for combat sports content. We're grateful to the Sonarr team for their excellent foundation.

## Credits

Built with ❤️ by combat sports fans, for combat sports fans.

Special thanks to:
- The Sonarr team for the original codebase
- All contributors and testers
- The combat sports community
