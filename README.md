# <img width="24px" src="./Logo/256.png" alt="Sportarr"></img> Sportarr

**Universal Sports PVR - Automatically track and download live sports events across all major sports**

Sportarr is a PVR (Personal Video Recorder) for Usenet and BitTorrent users designed for sports enthusiasts. Powered by TheSportsDB, it monitors and automatically downloads events from UFC, NFL, NBA, Premier League, Formula 1, and hundreds of other leagues and sports worldwide.

## Key Features

- 🌍 **Universal Sports Coverage** - UFC, NFL, NBA, NHL, Premier League, Formula 1, Boxing, Tennis, Golf, and more
- 📅 **Event Tracking** - Monitor upcoming games, matches, and fights across all sports
- 🔄 **Quality Upgrades** - Automatically upgrade to better quality releases (720p → 1080p → 4K)
- 🎯 **Smart Search** - Find specific events, teams, leagues, or athletes
- 📦 **Usenet & Torrents** - Full integration with SABnzbd, NZBGet, qBittorrent, Transmission, and more
- 🎬 **Media Server Integration** - Connect with Plex, Jellyfin, Emby, and Kodi
- 🐳 **Docker Ready** - Easy deployment with official Docker images
- 🌐 **Cross-Platform** - Windows, Linux, macOS, and ARM devices (Raspberry Pi, etc.)

## Quick Start

### Docker (Recommended)

```bash
docker run -d \
  --name=sportarr \
  -e PUID=99 \
  -e PGID=100 \
  -e UMASK=022 \
  -e TZ=America/New_York \
  -p 1867:1867 \
  -v /path/to/config:/config \
  -v /path/to/sports:/sports \
  -v /path/to/downloads:/downloads \
  --restart unless-stopped \
  sportarr/sportarr:latest
```

Then open `http://localhost:1867` in your browser.

### Docker Compose

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
      - TZ=America/New_York
    volumes:
      - /path/to/config:/config
      - /path/to/sports:/sports
      - /path/to/downloads:/downloads
    ports:
      - 1867:1867
    restart: unless-stopped
```

**Port 1867** - Year the Marquess of Queensberry Rules were published, a significant milestone in sports history!

### Manual Installation

Download the latest release for your platform:
- [Windows](https://github.com/Sportarr/Sportarr/releases)
- [Linux](https://github.com/Sportarr/Sportarr/releases)
- [macOS](https://github.com/Sportarr/Sportarr/releases)

## Configuration

### First Time Setup

1. **Add a Root Folder** - Where Sportarr will organize your sports library
2. **Connect Download Client** - SABnzbd, NZBGet, qBittorrent, etc.
3. **Add Indexers** - Usenet indexers or torrent trackers
4. **Search for Events** - Find events from any sport or league to monitor

### Recommended Folder Structure

```
/sports
├── UFC
│   ├── UFC 300 (2024)
│   │   ├── Main Card
│   │   └── Prelims
├── NFL
│   ├── 2024 Season
│   │   ├── Week 1
│   │   └── Super Bowl LIX
├── Premier League
│   └── 2024-25 Season
├── NBA
│   └── 2024-25 Season
└── Formula 1
    └── 2024 Season
```

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `PUID` | User ID for file permissions | `99` |
| `PGID` | Group ID for file permissions | `100` |
| `UMASK` | File creation mask for permissions | `022` |
| `TZ` | Timezone (e.g., `America/New_York`) | `UTC` |
| `SPORTARR__SERVER__PORT` | Web UI port | `1867` |
| `SPORTARR__LOG__ANALYTICSENABLED` | Enable analytics/telemetry | `false` |

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

Sportarr provides two APIs:

### Metadata API (Automatic)
- **URL**: `https://sportarr.net`
- Provides sports event data (UFC, Premier League, NBA, NFL, etc.)
- Used automatically by Sportarr - no configuration needed
- Public API for sports event information and statistics

### Control API (Your Instance)
- **Base URL**: `http://localhost:1867/api`
- Control and automate YOUR Sportarr instance
- **Authentication**: Include `X-Api-Key` header with your API key

Example:
```bash
# Get system status
curl -H "X-Api-Key: YOUR_API_KEY" http://localhost:1867/api/system/status

# List monitored events
curl -H "X-Api-Key: YOUR_API_KEY" http://localhost:1867/api/events

# Get leagues
curl -H "X-Api-Key: YOUR_API_KEY" http://localhost:1867/api/leagues

# Trigger event search
curl -X POST -H "X-Api-Key: YOUR_API_KEY" http://localhost:1867/api/command \
  -d '{"name": "EventSearch", "leagueId": 123}'
```

## Development

### Prerequisites

- .NET 8 SDK
- Node.js 20+
- Yarn package manager

### Build from Source

```bash
# Clone the repository
git clone https://github.com/Sportarr/Sportarr.git
cd Sportarr

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

- 💬 **Discord Server**: [Join our community](https://discord.gg/YjHVWGWjjG) for support, discussions, and updates
- 🐛 **Bug Reports**: [GitHub Issues](https://github.com/Sportarr/Sportarr/issues)
- 💬 **Discussions**: [GitHub Discussions](https://github.com/Sportarr/Sportarr/discussions)
- 📖 **Documentation**: Coming soon
- 💰 **Donate**: Support development (coming soon)

## Roadmap

- [ ] Enhanced athlete and team statistics
- [ ] Multi-language support
- [ ] Mobile app
- [ ] Advanced search filters (by league, team, sport type, etc.)
- [ ] Integration with additional sports statistics APIs
- [ ] Automatic highlight detection
- [ ] Season pass monitoring
- [ ] Multi-sport event scheduling

## License

[GNU GPL v3](http://www.gnu.org/licenses/gpl.html)

---

**Note**: Sportarr is a fork of Sonarr, adapted specifically for sports content across all major leagues and competitions worldwide. We're grateful to the Sonarr team for their excellent foundation.

## Credits

Built with ❤️ by sports fans, for sports fans.

Special thanks to:
- The Sonarr team for the original codebase
- TheSportsDB for comprehensive sports data
- All contributors and testers
- The global sports community
