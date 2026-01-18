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
| POST | `/api/indexer` | Add indexer |
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

## Sonarr v3 Compatibility

Sportarr implements Sonarr v3 API endpoints to enable integration with:
- **Prowlarr** - Indexer sync
- **Maintainerr** - Media lifecycle management
- **Other tools** expecting Sonarr API

### Mapping

| Sonarr Concept | Sportarr Equivalent |
|----------------|---------------------|
| Series | League |
| Episode | Event |
| tvdbId | TheSportsDB League ID |
| Season | Year/Season |

### Sonarr-Compatible Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/v3/system/status` | System status |
| GET | `/api/v3/series` | List leagues as series |
| PUT | `/api/v3/series/{id}` | Update series (monitored) |
| DELETE | `/api/v3/series/{id}` | Delete series |
| GET | `/api/v3/indexer` | List indexers |
| POST | `/api/v3/indexer` | Create indexer (Prowlarr sync) |
| GET | `/api/v3/rootfolder` | List root folders |
| GET | `/api/v3/qualityprofile` | List quality profiles |

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
