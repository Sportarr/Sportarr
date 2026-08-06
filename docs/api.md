# Sportarr API

Sportarr provides a comprehensive REST API for managing your sports media library. The API is designed to be similar to other *arr applications (Sonarr, Radarr) for ease of integration.

## Full Documentation

**Interactive API documentation is available at: https://sportarr.net/docs/api**

The documentation includes:
- Complete endpoint reference with all parameters
- Request/response schemas
- Interactive "Try it out" functionality
- Example requests and responses

## Quick Reference

### Authentication

All API endpoints require authentication via the `X-Api-Key` header:

```
X-Api-Key: your-api-key-here
```

Your API key can be found in **Settings > General > Security**.

### Base URL

```
http://localhost:1867/api
```

### API Versions

| Version | Base Path | Description |
|---------|-----------|-------------|
| Native | `/api/*` | Primary Sportarr API |
| Sonarr v3 | `/api/v3/*` | Sonarr-compatible endpoints for Prowlarr/Maintainerr |
| Legacy | `/api/v1/*` | Legacy endpoints |

## Key Endpoints

### Events (Sports Events)

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/events` | List all events |
| GET | `/api/events/{id}` | Get event by ID |
| POST | `/api/events` | Create event |
| PUT | `/api/events/{id}` | Update event |
| DELETE | `/api/events/{id}` | Delete event |
| POST | `/api/event/{id}/search` | Search for event releases |

### Leagues

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/leagues` | List all leagues |
| GET | `/api/leagues/{id}` | Get league by ID |
| POST | `/api/leagues` | Add league |
| PUT | `/api/leagues/{id}` | Update league |
| DELETE | `/api/leagues/{id}` | Delete league |
| POST | `/api/leagues/{id}/refresh-events` | Refresh league events |
| GET | `/api/leagues/{id}/download-history` | Download-client ids for the league's grabs (for post-delete cleanup) |

### Search & Grabbing

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/release/search` | Manual search |
| POST | `/api/release/grab` | Grab a release |
| POST | `/api/automatic-search/all` | Search all missing |

### Queue

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/queue` | Get download queue |
| GET | `/api/queue/{id}` | Get queue item |
| DELETE | `/api/queue/{id}` | Remove from queue |

### Download Clients

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/downloadclient` | List download clients |
| POST | `/api/downloadclient` | Add download client |
| POST | `/api/downloadclient/test` | Test connection |

### Indexers

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/indexer` | List indexers |
| GET | `/api/indexer/{id}` | Get one indexer |
| GET | `/api/indexer/schema` | Implementation templates (see APPLICATION_API.md) |
| POST | `/api/indexer` | Add indexer |
| PUT | `/api/indexer/{id}` | Update indexer |
| DELETE | `/api/indexer/{id}` | Remove indexer |
| POST | `/api/indexer/test` | Test indexer |

### Quality Profiles

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/qualityprofile` | List quality profiles |
| POST | `/api/qualityprofile` | Create profile |
| PUT | `/api/qualityprofile/{id}` | Update profile |

### System

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/ping` | Health check |
| GET | `/api/system/status` | System status |
| GET | `/api/system/health` | Health checks |
| GET | `/api/stats` | Application stats |

### IPTV / DVR / EPG

See [IPTV DVR Recording](features/iptv-dvr.md) for the full feature walkthrough. Key endpoints:

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/iptv/sources` | List IPTV sources (M3U/Xtream Codes) |
| POST | `/api/iptv/sources` | Add IPTV source |
| GET | `/api/iptv/channels` | List imported channels |
| GET | `/api/iptv/filtered.m3u` | Filtered M3U playlist for external IPTV apps |
| GET | `/api/iptv/filtered.xml` | Filtered EPG (XMLTV) for external IPTV apps |
| GET | `/api/iptv/stream/{channelId}` | Proxy a channel's live stream |
| GET | `/api/dvr/recordings` | List DVR recordings |
| GET | `/api/dvr/active` | Currently recording |
| POST | `/api/dvr/events/{eventId}/schedule` | Schedule a recording for an event |
| GET | `/api/dvr/settings` | DVR configuration |
| GET | `/api/epg/guide` | EPG grid data (channels + programs) |
| GET | `/api/epg/sources` | List XMLTV EPG sources |

#### HDHomeRun tuner emulation

Sportarr emulates a [SiliconDust HDHomeRun](https://www.silicondust.com/) network tuner so Plex/Jellyfin/Emby Live TV can add it directly - see [Use Sportarr as a network tuner](features/iptv-dvr.md#use-sportarr-as-a-network-tuner-plexjellyfinemby-live-tv). Unlike every other endpoint in this reference, these are served unauthenticated at the **server root**, not under `/api`, to match the real HDHomeRun HTTP API:

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/discover.json` | Device manifest (model, tuner count, BaseURL) |
| GET | `/lineup.json` | Channel lineup |
| GET | `/lineup_status.json` | Tuner status |
| GET | `/device.xml` | UPnP device description (Plex manual-add fallback) |

## Sonarr v3 Compatibility

Sportarr implements Sonarr v3 API endpoints to enable integration with:
- **Prowlarr** - Indexer sync
- **Maintainerr** - Media lifecycle management
- **Decypharr** - Symlink repair and media management
- **Other tools** expecting Sonarr API

### Mapping

| Sonarr Concept | Sportarr Equivalent |
|----------------|---------------------|
| Series | League |
| Episode | Event |
| tvdbId | Sportarr API League ID |
| Season | Year/Season |

### Sonarr-Compatible Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/v3/system/status` | System status |
| GET | `/api/v3/series` | List leagues as series |
| GET | `/api/v3/series/{id}` | Get series by ID |
| PUT | `/api/v3/series/{id}` | Update series (monitored) |
| DELETE | `/api/v3/series/{id}` | Delete series |
| GET | `/api/v3/episode` | Get episodes by seriesId |
| GET | `/api/v3/episode/{id}` | Get episode by ID |
| GET | `/api/v3/episodefile` | Get episode files by seriesId |
| GET | `/api/v3/episodefile/{id}` | Get episode file by ID |
| DELETE | `/api/v3/episodefile/{id}` | Delete episode file |
| DELETE | `/api/v3/episodefile/bulk` | Bulk delete episode files |
| GET | `/api/v3/indexer` | List indexers |
| POST | `/api/v3/indexer` | Create indexer (Prowlarr sync) |
| GET | `/api/v3/rootfolder` | List root folders |
| GET | `/api/v3/qualityprofile` | List quality profiles |
| GET | `/api/v3/tag` | List tags |
| GET | `/api/v3/importlistexclusion` | List import exclusions |

## Example: Searching for an Event

```bash
# Search for releases for event ID 123
curl -X POST "http://localhost:1867/api/event/123/search" \
  -H "X-Api-Key: your-api-key"
```

## Example: Grabbing a Release

```bash
curl -X POST "http://localhost:1867/api/release/grab" \
  -H "X-Api-Key: your-api-key" \
  -H "Content-Type: application/json" \
  -d '{
    "indexerId": 1,
    "guid": "release-guid",
    "title": "UFC.300.720p.WEB-DL",
    "downloadUrl": "https://indexer.example/nzb/123"
  }'
```

## Additional Resources

- [Interactive API Docs](https://sportarr.net/docs/api) - Full Swagger/OpenAPI documentation
- [Setup Guide](https://sportarr.net/setup) - Installation and configuration
- [GitHub Issues](https://github.com/Sportarr/Sportarr/issues) - Bug reports and feature requests
