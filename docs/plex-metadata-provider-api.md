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

**GET** `/plex/provider`

Returns the provider configuration and capabilities.

**Response:**
```json
{
  "MediaContainer": {
    "identifier": "tv.plex.agents.custom.sportarr",
    "title": "Sportarr",
    "version": "2.0.0",
    "types": [
      { "type": 2, "title": "TV Shows" }
    ],
    "Feature": [
      { "type": "match", "key": "/plex/provider/library/metadata/matches" },
      { "type": "metadata", "key": "/plex/provider/library/metadata" }
    ],
    "attribution": "Metadata provided by Sportarr (powered by TheSportsDB)"
  }
}
```

---

### 2. Match Endpoint (Search)

**POST** `/plex/provider/library/metadata/matches`

Plex calls this to find matching shows/seasons/episodes based on file info.

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
  "manual": 0
}
```

**Request Body (Season - type 3):**
```json
{
  "type": 3,
  "parentTitle": "UFC",
  "index": 2025
}
```

**Request Body (Episode - type 4):**
```json
{
  "type": 4,
  "grandparentTitle": "UFC",
  "parentIndex": 2025,
  "index": 5,
  "title": "UFC 320"
}
```

**Response (TV Show matches):**
```json
{
  "MediaContainer": {
    "offset": 0,
    "totalSize": 1,
    "identifier": "tv.plex.agents.custom.sportarr",
    "size": 1,
    "Metadata": [
      {
        "ratingKey": "sportarr-league-4389",
        "key": "/plex/provider/library/metadata/sportarr-league-4389",
        "guid": "tv.plex.agents.custom.sportarr://league/4389",
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
    "identifier": "tv.plex.agents.custom.sportarr",
    "size": 1,
    "Metadata": [
      {
        "ratingKey": "sportarr-event-123456",
        "key": "/plex/provider/library/metadata/sportarr-event-123456",
        "guid": "tv.plex.agents.custom.sportarr://event/123456",
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

**GET** `/plex/provider/library/metadata/{ratingKey}`

Returns detailed metadata for a specific item.

**Example:** `GET /plex/provider/library/metadata/sportarr-league-4389`

**Response (TV Show/League):**
```json
{
  "MediaContainer": {
    "offset": 0,
    "totalSize": 1,
    "identifier": "tv.plex.agents.custom.sportarr",
    "size": 1,
    "Metadata": [
      {
        "ratingKey": "sportarr-league-4389",
        "key": "/plex/provider/library/metadata/sportarr-league-4389",
        "guid": "tv.plex.agents.custom.sportarr://league/4389",
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

**Example:** `GET /plex/provider/library/metadata/sportarr-event-123456`

**Response (Episode/Event):**
```json
{
  "MediaContainer": {
    "offset": 0,
    "totalSize": 1,
    "identifier": "tv.plex.agents.custom.sportarr",
    "size": 1,
    "Metadata": [
      {
        "ratingKey": "sportarr-event-123456",
        "key": "/plex/provider/library/metadata/sportarr-event-123456",
        "guid": "tv.plex.agents.custom.sportarr://event/123456",
        "type": 4,
        "title": "UFC 320: Jones vs. Aspinall",
        "grandparentTitle": "UFC",
        "grandparentKey": "/plex/provider/library/metadata/sportarr-league-4389",
        "parentTitle": "Season 2025",
        "parentKey": "/plex/provider/library/metadata/sportarr-season-4389-2025",
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

**GET** `/plex/provider/library/metadata/{ratingKey}/children`

Returns seasons for a show, or episodes for a season.

**Headers:**
- `X-Plex-Container-Start`: `0` (pagination offset)
- `X-Plex-Container-Size`: `20` (items per page)

**Example:** `GET /plex/provider/library/metadata/sportarr-league-4389/children`

**Response (Seasons):**
```json
{
  "MediaContainer": {
    "offset": 0,
    "totalSize": 5,
    "identifier": "tv.plex.agents.custom.sportarr",
    "size": 5,
    "Metadata": [
      {
        "ratingKey": "sportarr-season-4389-2025",
        "key": "/plex/provider/library/metadata/sportarr-season-4389-2025",
        "guid": "tv.plex.agents.custom.sportarr://season/4389/2025",
        "type": 3,
        "title": "Season 2025",
        "parentTitle": "UFC",
        "parentKey": "/plex/provider/library/metadata/sportarr-league-4389",
        "index": 2025,
        "thumb": "https://sportarr.net/images/leagues/4389/seasons/2025/poster.jpg",
        "summary": "UFC events from the 2025 season."
      },
      {
        "ratingKey": "sportarr-season-4389-2024",
        "key": "/plex/provider/library/metadata/sportarr-season-4389-2024",
        "guid": "tv.plex.agents.custom.sportarr://season/4389/2024",
        "type": 3,
        "title": "Season 2024",
        "parentTitle": "UFC",
        "parentKey": "/plex/provider/library/metadata/sportarr-league-4389",
        "index": 2024,
        "thumb": "https://sportarr.net/images/leagues/4389/seasons/2024/poster.jpg",
        "summary": "UFC events from the 2024 season."
      }
    ]
  }
}
```

**Example:** `GET /plex/provider/library/metadata/sportarr-season-4389-2025/children`

**Response (Episodes):**
```json
{
  "MediaContainer": {
    "offset": 0,
    "totalSize": 42,
    "identifier": "tv.plex.agents.custom.sportarr",
    "size": 20,
    "Metadata": [
      {
        "ratingKey": "sportarr-event-123456",
        "key": "/plex/provider/library/metadata/sportarr-event-123456",
        "guid": "tv.plex.agents.custom.sportarr://event/123456",
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
tv.plex.agents.custom.sportarr://{type}/{id}
```

Examples:
- `tv.plex.agents.custom.sportarr://league/4389`
- `tv.plex.agents.custom.sportarr://season/4389/2025`
- `tv.plex.agents.custom.sportarr://event/123456`

---

## Matching Logic

### TV Show Matching (type 2)

1. Search by title (fuzzy match against league names)
2. Filter by sport if detectable from title
3. Return best matches sorted by score

Common league titles to match:
- "UFC" → Ultimate Fighting Championship
- "WWE" → World Wrestling Entertainment
- "NFL" → National Football League
- "NBA" → National Basketball Association
- "F1" / "Formula 1" → Formula One
- "Premier League" / "EPL" → English Premier League

### Season Matching (type 3)

1. Parse `parentTitle` to find league
2. Use `index` as season year
3. Return matching season

### Episode Matching (type 4)

1. Parse `grandparentTitle` to find league
2. Use `parentIndex` as season year
3. Use `index` as episode number within that season
4. Optionally match by `title` for better accuracy

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

Images are sourced from TheSportsDB and cached on sportarr.net.

---

## Error Handling

**404 Not Found:**
```json
{
  "MediaContainer": {
    "identifier": "tv.plex.agents.custom.sportarr",
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

The sportarr.net API should query TheSportsDB data:

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
# Get provider info
curl https://sportarr.net/plex/provider

# Search for a show
curl -X POST https://sportarr.net/plex/provider/library/metadata/matches \
  -H "Content-Type: application/json" \
  -d '{"type": 2, "title": "UFC"}'

# Get show metadata
curl https://sportarr.net/plex/provider/library/metadata/sportarr-league-4389

# Get seasons
curl https://sportarr.net/plex/provider/library/metadata/sportarr-league-4389/children
```

---

## Migration from Legacy Agent

1. Users should rename existing Plex libraries to use the new provider
2. Legacy agent (`Sportarr.bundle`) will continue working until Plex removes support (2026)
3. Metadata will be re-fetched using new provider - no data loss

The legacy agent has been renamed to `Sportarr-Legacy.bundle` in the Sportarr distribution.
