# Sportarr Plex Custom Metadata Provider API

## Overview

This document specifies the API endpoints required for sportarr.net to implement Plex's new Custom Metadata Provider system. This replaces the legacy Python-based agent that will be deprecated in 2026.

**Provider URL:** `https://sportarr.net/plex` (redirects to `/api/plex/provider/sports`)

**Provider Identifier:** `tv.plex.agents.custom.sportarr`

## User Setup in Plex

### Step 1: Add the Metadata Provider

1. Go to **Settings → Metadata Agents**
2. Click **+ Add Provider**
3. Enter URL: `https://sportarr.net/plex`
4. Click **+ Add Agent**
5. Give it a title (e.g., "Sportarr Sports")
6. Select the **Sportarr** metadata provider you just imported
7. Click **Save**
8. **Restart Plex Media Server**

### Step 2: Create a Sports Library

1. Go to **Settings → Libraries**
2. Click **+ Add Library**
3. Select **TV Shows** as the library type
4. Name it whatever you like (e.g., "Sports")
5. Select the **Sportarr** metadata agent you created
6. Add your sports media folder
7. Click **Add Library**

---

## Required Endpoints

### 1. Provider Definition

**GET** `/api/plex/provider/sports` (users paste `https://sportarr.net/plex`, which redirects here)

Returns the provider configuration and capabilities.

**Response:**
```json
{
  "MediaContainer": {
    "identifier": "tv.plex.agents.custom.sportarr.sports",
    "title": "Sportarr",
    "version": "2.0.0",
    "types": [
      { "type": 2, "title": "TV Shows" }
    ],
    "Feature": [
      { "type": "match", "key": "/api/plex/provider/sports/library/metadata/matches" },
      { "type": "metadata", "key": "/api/plex/provider/sports/library/metadata" }
    ],
    "attribution": "Metadata provided by Sportarr (powered by Sportarr API)"
  }
}
```

---

### 2. Match Endpoint (Search)

**POST** `/api/plex/provider/sports/library/metadata/matches`

Plex calls this to find matching shows/seasons/episodes based on file info.
Every request may carry `filename`, the relative path of the media file
(for a show or a season, the first file found). A Sportarr id in that
name (`sportarr-ev-2338110`, or `lg-000032` for a league) names the item
outright; titles and numbers are the fallback for a file with no id.

**Headers:**
- `X-Plex-Language`: `en` (optional)
- `X-Plex-Country`: `US` (optional)
- `Content-Type`: `application/json`

**Request Body (TV Show - type 2):**
```json
{
  "type": 2,
  "title": "UFC",
  "year": 2025,
  "manual": 0,
  "filename": "Sports/UFC/Season 2025/UFC - S2025E05 - UFC 320 - sportarr-ev-2338110.mkv"
}
```

**Request Body (Season - type 3):**
```json
{
  "type": 3,
  "parentTitle": "UFC",
  "index": 2025,
  "filename": "Sports/UFC/Season 2025/UFC - S2025E05 - UFC 320 - sportarr-ev-2338110.mkv"
}
```

**Request Body (Episode - type 4):**
```json
{
  "type": 4,
  "grandparentTitle": "UFC",
  "parentIndex": 2025,
  "index": 5,
  "title": "UFC 320",
  "filename": "Sports/UFC/Season 2025/UFC - S2025E05 - UFC 320 - sportarr-ev-2338110.mkv"
}
```

**Response (TV Show matches):**
```json
{
  "MediaContainer": {
    "offset": 0,
    "totalSize": 1,
    "identifier": "tv.plex.agents.custom.sportarr.sports",
    "size": 1,
    "Metadata": [
      {
        "ratingKey": "sportarr-league-4389",
        "key": "/api/plex/provider/sports/library/metadata/sportarr-league-4389",
        "guid": "tv.plex.agents.custom.sportarr.sports://league/4389",
        "type": 2,
        "title": "Ultimate Fighting Championship",
        "originalTitle": "UFC",
        "year": 1993,
        "thumb": "https://sportarr.net/images/leagues/4389/poster.jpg",
        "art": "https://sportarr.net/images/leagues/4389/fanart.jpg",
        "summary": "The Ultimate Fighting Championship (UFC) is the world's premier mixed martial arts organization.",
        "contentRating": "TV-14",
        "studio": "UFC",
        "Genre": [
          { "tag": "Fighting" },
          { "tag": "MMA" },
          { "tag": "Sports" }
        ]
      }
    ]
  }
}
```

**Response (Episode matches):**
```json
{
  "MediaContainer": {
    "offset": 0,
    "totalSize": 1,
    "identifier": "tv.plex.agents.custom.sportarr.sports",
    "size": 1,
    "Metadata": [
      {
        "ratingKey": "sportarr-event-123456",
        "key": "/api/plex/provider/sports/library/metadata/sportarr-event-123456",
        "guid": "tv.plex.agents.custom.sportarr.sports://event/123456",
        "type": 4,
        "title": "UFC 320: Jones vs. Aspinall",
        "grandparentTitle": "UFC",
        "parentTitle": "Season 2025",
        "parentIndex": 2025,
        "index": 5,
        "originallyAvailableAt": "2025-03-15",
        "thumb": "https://sportarr.net/images/events/123456/thumb.jpg",
        "summary": "Jon Jones defends his heavyweight title against Tom Aspinall in the main event.",
        "duration": 10800000,
        "contentRating": "TV-14"
      }
    ]
  }
}
```

---

### 3. Metadata Endpoint (Single Item)

**GET** `/api/plex/provider/sports/library/metadata/{ratingKey}`

Returns detailed metadata for a specific item.

**Example:** `GET /api/plex/provider/sports/library/metadata/sportarr-league-4389`

**Response (TV Show/League):**
```json
{
  "MediaContainer": {
    "offset": 0,
    "totalSize": 1,
    "identifier": "tv.plex.agents.custom.sportarr.sports",
    "size": 1,
    "Metadata": [
      {
        "ratingKey": "sportarr-league-4389",
        "key": "/api/plex/provider/sports/library/metadata/sportarr-league-4389",
        "guid": "tv.plex.agents.custom.sportarr.sports://league/4389",
        "type": 2,
        "title": "Ultimate Fighting Championship",
        "originalTitle": "UFC",
        "year": 1993,
        "thumb": "https://sportarr.net/images/leagues/4389/poster.jpg",
        "art": "https://sportarr.net/images/leagues/4389/fanart.jpg",
        "banner": "https://sportarr.net/images/leagues/4389/banner.jpg",
        "summary": "The Ultimate Fighting Championship (UFC) is the world's premier mixed martial arts organization, featuring elite fighters from around the globe competing in various weight classes.",
        "contentRating": "TV-14",
        "studio": "UFC",
        "originallyAvailableAt": "1993-11-12",
        "Genre": [
          { "tag": "Fighting" },
          { "tag": "MMA" },
          { "tag": "Sports" }
        ],
        "Country": [
          { "tag": "United States" }
        ]
      }
    ]
  }
}
```

**Example:** `GET /api/plex/provider/sports/library/metadata/sportarr-event-123456`

**Response (Episode/Event):**
```json
{
  "MediaContainer": {
    "offset": 0,
    "totalSize": 1,
    "identifier": "tv.plex.agents.custom.sportarr.sports",
    "size": 1,
    "Metadata": [
      {
        "ratingKey": "sportarr-event-123456",
        "key": "/api/plex/provider/sports/library/metadata/sportarr-event-123456",
        "guid": "tv.plex.agents.custom.sportarr.sports://event/123456",
        "type": 4,
        "title": "UFC 320: Jones vs. Aspinall",
        "grandparentTitle": "UFC",
        "grandparentKey": "/api/plex/provider/sports/library/metadata/sportarr-league-4389",
        "parentTitle": "Season 2025",
        "parentKey": "/api/plex/provider/sports/library/metadata/sportarr-season-4389-2025",
        "parentIndex": 2025,
        "index": 5,
        "originallyAvailableAt": "2025-03-15",
        "thumb": "https://sportarr.net/images/events/123456/thumb.jpg",
        "art": "https://sportarr.net/images/events/123456/fanart.jpg",
        "summary": "Jon Jones defends his UFC Heavyweight Championship against interim champion Tom Aspinall in the main event of UFC 320. The co-main event features...",
        "duration": 10800000,
        "contentRating": "TV-14",
        "Director": [
          { "tag": "UFC Productions" }
        ],
        "Role": [
          { "tag": "Jon Jones", "role": "Fighter" },
          { "tag": "Tom Aspinall", "role": "Fighter" }
        ]
      }
    ]
  }
}
```

---

### 4. Children Endpoint (Seasons)

**GET** `/api/plex/provider/sports/library/metadata/{ratingKey}/children`

Returns seasons for a show, or episodes for a season.

**Headers:**
- `X-Plex-Container-Start`: `0` (pagination offset)
- `X-Plex-Container-Size`: `20` (items per page)

**Example:** `GET /api/plex/provider/sports/library/metadata/sportarr-league-4389/children`

**Response (Seasons):**
```json
{
  "MediaContainer": {
    "offset": 0,
    "totalSize": 5,
    "identifier": "tv.plex.agents.custom.sportarr.sports",
    "size": 5,
    "Metadata": [
      {
        "ratingKey": "sportarr-season-4389-2025",
        "key": "/api/plex/provider/sports/library/metadata/sportarr-season-4389-2025",
        "guid": "tv.plex.agents.custom.sportarr.sports://season/4389/2025",
        "type": 3,
        "title": "Season 2025",
        "parentTitle": "UFC",
        "parentKey": "/api/plex/provider/sports/library/metadata/sportarr-league-4389",
        "index": 2025,
        "thumb": "https://sportarr.net/images/leagues/4389/seasons/2025/poster.jpg",
        "summary": "UFC events from the 2025 season."
      },
      {
        "ratingKey": "sportarr-season-4389-2024",
        "key": "/api/plex/provider/sports/library/metadata/sportarr-season-4389-2024",
        "guid": "tv.plex.agents.custom.sportarr.sports://season/4389/2024",
        "type": 3,
        "title": "Season 2024",
        "parentTitle": "UFC",
        "parentKey": "/api/plex/provider/sports/library/metadata/sportarr-league-4389",
        "index": 2024,
        "thumb": "https://sportarr.net/images/leagues/4389/seasons/2024/poster.jpg",
        "summary": "UFC events from the 2024 season."
      }
    ]
  }
}
```

**Example:** `GET /api/plex/provider/sports/library/metadata/sportarr-season-4389-2025/children`

**Response (Episodes):**
```json
{
  "MediaContainer": {
    "offset": 0,
    "totalSize": 42,
    "identifier": "tv.plex.agents.custom.sportarr.sports",
    "size": 20,
    "Metadata": [
      {
        "ratingKey": "sportarr-event-123456",
        "key": "/api/plex/provider/sports/library/metadata/sportarr-event-123456",
        "guid": "tv.plex.agents.custom.sportarr.sports://event/123456",
        "type": 4,
        "title": "UFC 320: Jones vs. Aspinall",
        "grandparentTitle": "UFC",
        "parentTitle": "Season 2025",
        "parentIndex": 2025,
        "index": 5,
        "originallyAvailableAt": "2025-03-15",
        "thumb": "https://sportarr.net/images/events/123456/thumb.jpg",
        "summary": "Jon Jones defends his heavyweight title against Tom Aspinall."
      }
    ]
  }
}
```

---

## Rating Key Format

Rating keys are URL-safe identifiers that encode the item type and ID:

| Type | Format | Example |
|------|--------|---------|
| League (Show) | `sportarr-league-{externalId}` | `sportarr-league-4389` |
| Season | `sportarr-season-{leagueId}-{year}` | `sportarr-season-4389-2025` |
| Event (Episode) | `sportarr-event-{eventId}` | `sportarr-event-123456` |

---

## GUID Format

GUIDs follow Plex's custom agent scheme:

```
tv.plex.agents.custom.sportarr.sports://{type}/{id}
```

Examples:
- `tv.plex.agents.custom.sportarr.sports://league/4389`
- `tv.plex.agents.custom.sportarr.sports://season/4389/2025`
- `tv.plex.agents.custom.sportarr.sports://event/123456`

---

## External ID Mappings (Guid array)

In addition to the top-level `guid` attribute above, show responses carry
the optional `Guid` array from the Plex Custom Metadata Provider spec.
Plex stores these as external-id mappings on the library item, which is
what downstream tools (Maintainerr and other arr-ecosystem integrations)
read to resolve an id for the item:

```json
"Guid": [
  { "id": "sportarr://lg-000142" },
  { "id": "tvdb://900000142" }
]
```

Both entries carry the same Sportarr league id. The `sportarr://` entry is
the native form (the canonical short id). The `tvdb://` entry is a
compatibility envelope for tools that only parse the imdb/tmdb/tvdb
namespaces; its value is the Sportarr numeric alias (900,000,000 plus the
short id number), **not** a real TVDB id. The Sonarr v3 compatibility API
on a Sportarr install reports the same number in its `tvdbId` fields, so a
tool that reads the id from Plex can look the item up against the install
directly.

Note that this number is unrelated to the numeric part of the rating key.
Rating keys carry a legacy catalog id; the Guid array carries the
canonical Sportarr id. See [EXTERNAL_IDS.md](EXTERNAL_IDS.md) for the full
contract, including the frozen offsets and the envelope retirement policy.

---

## Matching Logic

### TV Show Matching (type 2)

1. A stored `guid` (the identity Plex already holds, a Fix Match included) resolves the league outright
2. Else a Sportarr id in `filename` names the league: a league token directly, an event token through the event's league
3. Else search by title (relevance-scored against league names) and return the best matches

Common league titles to match:
- "UFC" → Ultimate Fighting Championship
- "WWE" → World Wrestling Entertainment
- "NFL" → National Football League
- "NBA" → National Basketball Association
- "F1" / "Formula 1" → Formula One
- "Premier League" / "EPL" → English Premier League

### Season Matching (type 3)

1. A Sportarr id in `filename` names the league, and an event id names the season too
2. Else parse `parentTitle` to find the league and use `index` as the season year
3. Return the matching season

### Episode Matching (type 4)

1. An event id in `filename` names the event outright, with its own season and league, whatever the titles and numbers say. An event with no numbered slot (cancelled or postponed) answers nothing rather than another game
2. Else parse `grandparentTitle` to find the league, `parentIndex` as the season year and `index` as the episode number within that season

Note that a library scan never sends a type 4 request: once the show is matched, Plex places each file by its season and episode numbers against the provider's season children listing. Type 4 requests come from a manual Fix Match on an episode. Episode payloads carry a `Guid` array with `sportarr://ev-…` and its `tvdb://` alias, like show payloads.

**Episode Number Calculation:**
Episodes are numbered chronologically by event date within each season. This matches how Sportarr assigns episode numbers.

---

## Image URLs

Images should be served from sportarr.net CDN:

```
https://sportarr.net/images/leagues/{leagueId}/poster.jpg
https://sportarr.net/images/leagues/{leagueId}/fanart.jpg
https://sportarr.net/images/leagues/{leagueId}/banner.jpg
https://sportarr.net/images/events/{eventId}/thumb.jpg
https://sportarr.net/images/events/{eventId}/fanart.jpg
```

Images are sourced from Sportarr API and cached on sportarr.net.

---

## Error Handling

**404 Not Found:**
```json
{
  "MediaContainer": {
    "identifier": "tv.plex.agents.custom.sportarr.sports",
    "size": 0,
    "Metadata": []
  }
}
```

**500 Server Error:**
```json
{
  "error": "Internal server error",
  "message": "Failed to fetch metadata"
}
```

---

## Implementation Notes

### Database Queries

The sportarr.net API should query Sportarr API data:

1. **Leagues** → `idLeague`, `strLeague`, `strSport`, `strDescriptionEN`, etc.
2. **Events** → `idEvent`, `strEvent`, `dateEvent`, `strSeason`, etc.
3. **Images** → `strPoster`, `strFanart`, `strBanner`, `strThumb`, etc.

### Episode Number Calculation

For a given league and season, events should be ordered by date and assigned sequential episode numbers:

```sql
SELECT
  idEvent,
  strEvent,
  dateEvent,
  ROW_NUMBER() OVER (PARTITION BY idLeague, strSeason ORDER BY dateEvent) as episodeNumber
FROM events
WHERE idLeague = ? AND strSeason = ?
```

### Caching

- League metadata: Cache for 24 hours
- Event metadata: Cache for 1 hour (dates may change)
- Images: Cache indefinitely (use versioned URLs if needed)

---

## Testing

### Manual Testing

1. Add provider to Plex: `https://sportarr.net/plex/provider`
2. Create a TV library pointing to sports content
3. Use Sportarr naming: `UFC/Season 2025/UFC - S2025E05 - UFC 320.mkv`
4. Scan library and verify metadata appears

### API Testing

```bash
# Get provider info (the /plex URL users paste redirects to the manifest)
curl -L https://sportarr.net/plex

# Search for a show
curl -X POST https://sportarr.net/api/plex/provider/sports/library/metadata/matches \
  -H "Content-Type: application/json" \
  -d '{"type": 2, "title": "UFC"}'

# Get show metadata
curl https://sportarr.net/api/plex/provider/sports/library/metadata/sportarr-league-4389

# Get seasons
curl https://sportarr.net/api/plex/provider/sports/library/metadata/sportarr-league-4389/children
```

---

## Migration from Legacy Agent

1. Users should rename existing Plex libraries to use the new provider
2. Legacy agent (`Sportarr.bundle`) will continue working until Plex removes support (2026)
3. Metadata will be re-fetched using new provider - no data loss

The legacy agent has been renamed to `Sportarr-Legacy.bundle` in the Sportarr distribution.
