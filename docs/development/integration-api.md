# Integration API

This page is the stable contract for third-party applications that build
first-class Sportarr support. It exists so integrations like Bazarr can
develop against Sportarr's native API instead of the Sonarr compatibility
layer.

Everything documented on this page is covered by a stability promise. The
endpoints, their listed fields, and the event stream semantics will not
change shape without an announced deprecation window. Fields not listed
here may appear in responses and may change without notice. If you need an
additional field kept stable, open an issue and ask, and it gets added to
this page.

The Sonarr-compatible `/api/v3/*` surface remains available for tools that
already speak Sonarr. New integrations should prefer the endpoints below.

## Authentication

Every endpoint except `/api/health` requires the API key, found in
**Settings > General > Security**. Two forms are accepted:

```
X-Api-Key: your-api-key            (header, preferred)
?apikey=your-api-key               (query, for EventSource/SSE clients)
```

## Connection test

```
GET /api/health
```

Unauthenticated and CORS-open by design, so a settings page can probe it
directly. Returns:

```json
{
  "status": "healthy",
  "version": "4.1.0",
  "build": "4.1.0.1110",
  "timestamp": "2026-08-10T23:30:00Z"
}
```

To validate the API key itself, follow up with any authenticated call.
`GET /api/leagues` is a good choice, since a working integration needs it
anyway.

## Leagues

```
GET /api/leagues            List all leagues
GET /api/leagues?sport=X    Filter by sport
```

Leagues are Sportarr's library unit, the closest analogue to a series in
Sonarr. Guaranteed fields per league:

| Field | Type | Notes |
|---|---|---|
| `id` | int | Internal id, stable per install |
| `externalId` | string? | Metadata id (`lg-` prefix), stable across installs |
| `name` | string | |
| `sport` | string | e.g. `"Soccer"`, `"Fighting"`, `"Motorsport"` |
| `country` | string? | |
| `monitored` | bool | |
| `rootFolderId` | int? | Resolve through `/api/rootfolder` |

## Events

```
GET /api/leagues/{id}/events    All events of a league, monitoring filters applied
GET /api/events/{id}            One event
```

Events are the episode analogue. Guaranteed fields per event:

| Field | Type | Notes |
|---|---|---|
| `id` | int | Internal id |
| `externalId` | string? | Metadata id (`ev-` prefix), stable across installs |
| `title` | string | |
| `sport` | string | |
| `leagueId` | int? | |
| `leagueName` | string? | |
| `season` | string? | Source season label, e.g. `"2026"` |
| `seasonNumber` | int? | Plex-style numbering |
| `episodeNumber` | int? | Plex-style numbering |
| `eventDate` | datetime | Scheduled start, UTC |
| `broadcastDate` | datetime? | Local broadcast date when a timezone is resolved |
| `broadcastTimezone` | string? | IANA zone |
| `monitored` | bool | |
| `hasFile` | bool | |
| `filePath` | string? | Absolute path of the imported file |
| `fileSize` | long? | Bytes |
| `quality` | string? | |

`GET /api/leagues/{id}/events` applies the league's monitoring filters
(session types for motorsport, monitored teams for team sports), so it
returns the same set of events the Sportarr UI shows. That is almost
always what an integration wants.

## Root folders

```
GET /api/rootfolder
```

Returns the configured library roots. Guaranteed fields: `id` (int) and
`path` (string). Use it to map `filePath` values into your own path
handling.

## Event stream (SSE)

```
GET /api/stream?apikey=KEY
GET /api/stream?apikey=KEY&since=123
```

A Server-Sent Events feed of resource changes, so integrations learn about
new, changed, and removed events without polling. Available from the first
release after 4.1.0.

Event names and payloads:

| Event | Fired when |
|---|---|
| `event.added` | An event enters the library (sync or manual add) |
| `event.updated` | An event changes (date, title, numbering, rename) |
| `event.removed` | An event leaves the library |
| `file.imported` | A file is imported for an event |
| `file.removed` | An event's file is deleted |

Each frame carries a monotonically increasing `id` and a JSON `data`
payload:

```
id: 42
event: file.imported
data: {"id":42,"timestamp":"2026-08-10T23:30:45Z","resourceType":"file","action":"imported","eventId":17,"externalId":"ev-848683","path":"/library/UFC/Season 2026/..."}
```

`eventId`, `externalId`, `leagueId`, and `path` are present when the
change has them and omitted otherwise.

Reconnect semantics:

- Send `?since=<last seen id>`, or rely on the standard `Last-Event-ID`
  header that EventSource sets automatically. The stream replays every
  missed event in order, then goes live.
- Events are retained for 7 days. A consumer offline longer than that
  should run a full resync through the REST endpoints, which is the same
  thing a scheduled reconciliation job should do anyway.
- The server sends a comment keepalive every 15 seconds. Treat a silent
  minute as a dead connection and reconnect.

## Deprecation policy

If any endpoint, field, or semantic documented here has to change, the old
behavior keeps working through a deprecation window announced in release
notes, and this page gets updated with migration guidance first. Breaking
changes without that window are treated as bugs, so report them.
