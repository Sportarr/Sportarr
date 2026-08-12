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
?apikey=your-api-key               (query)
```

The query form works on every endpoint, not only the SSE stream. Clients that
cannot set headers need it, and so does any consumer that already builds its
URLs that way.

Sportarr also supports running under a URL base, set in
**Settings > General**. Build your requests against the configured base rather
than assuming the API sits at the root.

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
| `path` | string? | Absolute folder holding this league's files. See below |
| `tags` | int[] | Tag ids. Resolve labels through `GET /api/tag` |

### League folders

`path` is the folder this league's files live in, and it is unique to the
league. It is null in two cases. The league has no root folder bound, or the
user turned OFF **Create League Folders** in Media Management.

That second case matters to any integration that keys a library on one folder
per league. With the setting off every league shares the root folder, so no
league has a folder of its own and Sportarr reports null rather than a path
that identifies nothing. Ask the user to enable Create League Folders.

### Tags

`GET /api/tag` returns every tag as `id` (int), `label` (string) and `color`
(string). League `tags` holds ids only, so read this endpoint to resolve
labels.

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

### Files and parts

Every event also carries a `files` array. An event can hold more than one
playable file: a fight card ships prelims and a main card separately, and a
race weekend ships its sessions separately. `filePath` above names only one
of them, so an integration that handles media per file must read `files`
rather than `filePath`.

Guaranteed fields per entry:

| Field | Type | Notes |
| --- | --- | --- |
| `id` | int | Stable id for this file. It changes when the file is replaced |
| `filePath` | string | Absolute path |
| `size` | long | Bytes |
| `quality` | string? | |
| `partName` | string? | e.g. `"Prelims"`, `"Main Card"`, `"Qualifying"`. Null when the event has a single file |
| `partNumber` | int? | Ordering within the event |
| `releaseTitle` | string? | Scene release name, set ONLY when a real grab produced this file. See below |
| `audioCodec` | string? | Audio codec, read from the file. `quality` above covers source and resolution |
| `languages` | string[] | Audio languages, read from the file's tracks |
| `exists` | bool | Only files present on disk are returned |

Treat a part as the unit of media, not the event. An event with one file is
simply an event with one part, so the same handling covers both.

Watch `id` to detect a replacement. An upgrade can write a better file to
the same path, so `filePath` alone will not tell you the media changed.

### Codec values

Sportarr reports what the file says, and does not normalise to any one
consumer's vocabulary. Map these yourself if your matching expects a
different spelling.

`codec` is the VIDEO codec. Common values are `H.264`, `HEVC`, `AV1`,
`MPEG-4`, `VC-1`. Note `H.264` rather than `x264` or `AVC`, and `HEVC`
rather than `x265`.

`audioCodec` is separate. Common values are `AAC`, `AC-3`, `E-AC-3`, `DTS`,
`TrueHD`, `Opus`, `MP3`. Note the hyphens in `AC-3` and `E-AC-3`.

Both are null when the probe could not read the file.

### File state during an upgrade

An upgrade replaces a file in place. Sportarr removes the old row and adds the
new one in a single write, and it no longer reports the event as file-less in
between. An integration that deletes its own records for events with no file
will not lose them across an upgrade.

### Release names

Use `releaseTitle` when you need the scene release name, for example to match
a subtitle or to identify the source release. It is set only when a real grab
produced the file. It is null for a manual import, a library import and a DVR
recording, because none of those has a release name.

Do NOT use `originalTitle` for this. That field is always populated, but
depending on how the file arrived it holds a filename or an event title. It is
useful for display and for checking which part of an event a file covers. Fed
to something that expects a release name it describes a release that never
existed, which is worse than nothing. A null `releaseTitle` at least lets a
consumer fall back to matching on the file hash.

Sportarr renames files on import, so the name on disk is never the release
name either.

### Season and episode numbers

`seasonNumber` and `episodeNumber` are guaranteed on any event that has a
file. Sportarr fills them at import if they are missing.

They can be null on an event with no file. Sync clears the episode index for
postponed and cancelled events on purpose, since an event that will not happen
holds no place in the running order.

### Paging

`GET /api/leagues/{id}/events` returns a plain array by default. Pass
`page` and/or `pageSize` to page it instead, which returns an envelope:

```
GET /api/leagues/12/events?page=1&pageSize=200
{ "page": 1, "pageSize": 200, "totalRecords": 2431, "totalPages": 13, "records": [ ... ] }
```

`pageSize` defaults to 100 and is capped at 1000. A full season can run to
a few thousand events, so page it during a first sync.

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

## Browsing folders

```
GET /api/filesystem
GET /api/filesystem?path=/library&includeFiles=true
```

Lists folders on the Sportarr host, for a settings screen where a user picks
or maps a path. With no `path` it returns the root drives.

`directories` is an array of `type`, `name`, `path` and `lastModified`.
`files` is present only with `includeFiles=true` and has the same shape.
Hidden and system entries are excluded.

## Telling Sportarr a file changed

```
POST /api/leagues/{id}/scan
```

Rescans the league's folder and picks up files that changed outside Sportarr.
Call it after writing a file next to existing media, for example a subtitle,
so Sportarr notices without waiting for its own schedule.

## Detecting removals safely

Read this before deleting your own records to match Sportarr.

**Only treat a missing item as removed after a COMPLETE read.** If you page
`GET /api/leagues/{id}/events`, walk every page first. The envelope returns
`totalRecords` and `totalPages` so you can prove the walk finished. A request
that fails halfway looks exactly like a league that lost most of its events,
and acting on that deletes records that were never gone.

**Monitoring filters shrink the list.** `GET /api/leagues/{id}/events` applies
the league's monitoring filters by default, so it returns what the Sportarr UI
shows rather than everything that exists. A user un-monitoring a team removes
events from that response while the events remain in the library. Do not read
that as deletion.

**An event with no file is not a deleted event.** Events exist before they
air and before anything is downloaded. Use `hasFile` to decide whether media
exists, and the event's presence to decide whether the event exists.

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

Every frame names the event it concerns, so there is no league level "events
changed" hint and none is needed. Sync the event named in the frame rather
than re-reading a whole league.

The feed carries real state changes only. Recomputed counts, progress and
other derived values never raise a frame, so a quiet feed means nothing
changed rather than nothing happened.

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
