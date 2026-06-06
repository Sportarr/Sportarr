# Sportarr Media Server Agents

Metadata agents for **Plex**, **Jellyfin**, and **Emby** that fetch sports
metadata from **sportarr.net** (default) or from your own self-hosted Sportarr
metadata instance. They replace traditional sources (TVDB/TMDB) for sports
content, supplying posters, banners, descriptions, air dates, and episode
organization.

## How Sportarr maps sports to your media server

Sportarr organizes sports the way a TV library is organized, so media servers
understand it with no special configuration:

- **League → Series** (e.g. Formula 1, UFC, NFL)
- **Season → `Season {year}`** — seasons are the 4-digit year (2024, 2025, 2026)
- **Event → Episode** — each game, race, fight, or show is one episode,
  numbered in the order it happens within that season

Example: Formula 1 → Season 2026 → `S2026E12` (Bahrain Grand Prix).

## Two ways to run it (read this first)

**Option A — Run the Sportarr app (recommended).** You never name a file by
hand. Sportarr imports, names, and **keeps renaming** your files
automatically. An event's episode number is its chronological position in the
season, not a permanent label — so when the schedule changes (an event is
added, cancelled, or postponed to a new date), Sportarr **renumbers the
affected events and renames the files on disk** on its next sync. That is why a
file can move from `S2026E12` to `S2026E13` on its own: the source reordered
the season and Sportarr followed it so your library stays correct.

**Option B — Metadata agent only (no Sportarr app).** If you point your media
server at the agent but manage files yourself, **you** name them. Use the
catalog browser at <https://sportarr.net/browse> → open a league → pick a
season to see every event with its current episode number, then name your file
to match. Because you are naming manually, re-check the number if an event gets
rescheduled — the browser always shows the current one.

## File naming convention

All agents expect Sportarr's standardized format.

### Folder structure

```
<library root>/{Series}/Season {Season}/
```

```
/sports/Formula 1/Season 2026/
/sports/UFC/Season 2026/
```

### File name

```
{Series} - S{Season}E{Episode} - {Event Title} - {Quality}.ext
```

```
Formula 1 - S2026E12 - Bahrain Grand Prix - 1080p.mkv
UFC - S2026E03 - Fighter vs Opponent - 1080p WEB-DL.mkv
```

- **`{Series}`** is the league name as Sportarr lists it; **`{Season}`** is the
  4-digit year; **`{Episode}`** is the event's season-global number.
- The episode number is assigned in chronological order across the **whole
  season** and **excludes cancelled and postponed events**, so it lines up with
  what the agent serves.
- Case and zero-padding do not matter for matching — `S2026E12`, `s2026e12`,
  and `s2026e012` all resolve to episode 12.
- The format is fully customizable in the Sportarr app under
  **Settings → Media Management**.

### Multi-part events

Fighting cards and motorsport weekends add a `ptN` segment:

```
{Series} - S{Season}E{Episode} - pt{Part} - {Event Title} - {Quality}.ext
```

```
UFC - S2026E03 - pt1 - Early Prelims - 1080p.mkv
UFC - S2026E03 - pt2 - Prelims - 1080p.mkv
UFC - S2026E03 - pt3 - Main Card - 1080p.mkv
```

Fighting: up to 3 parts (Early Prelims / Prelims / Main Card). Motorsport: up
to 5 (Practice / Qualifying / Sprint / Pre-Race / Race).

## Available agents

### Plex

Two methods are supported:

- **Custom Metadata Provider (recommended, Plex 1.43.0+)** — no install. Add
  provider URL `https://sportarr.net/plex`, add an agent, restart Plex, then
  create a **TV Shows** library using the Sportarr agent.
- **Legacy bundle agent** — `plex/Sportarr-Legacy.bundle/` for older Plex.
  (Plex has announced legacy agents will be deprecated in 2026.)

See [plex/README.md](plex/README.md).

### Jellyfin

A C# metadata plugin (`jellyfin/Sportarr/`). Install the DLL, set the API URL
(sportarr.net or your own instance), then create a **Shows** library with
Sportarr enabled as a metadata downloader. See [jellyfin/README.md](jellyfin/README.md).

### Emby

Uses the same metadata API. See [emby/README.md](emby/README.md).

> All three can point at **sportarr.net** or at your **own Sportarr instance**
> by setting the API URL in the agent/plugin settings.

## Verify it works

1. Place one correctly-named file in `{Series}/Season {year}/`, or let the
   Sportarr app import it.
2. Scan the library.
3. Open the item. A correct match shows the **right episode number**, the
   **event poster**, the **air date**, and a **description**.
4. If it does not match: confirm the library is a **TV Shows / Shows** type,
   the `Season {year}` folder exists, and the episode number matches
   <https://sportarr.net/browse>. Use the server's **Fix Match / Identify** to
   pick the league manually.

## Troubleshooting

### No metadata found

1. Check the file name matches the format above (Series, `Season {year}`
   folder, `S{year}E{episode}`).
2. Use **Fix Match / Identify** to select the league manually.

### Wrong match

1. Use the media server's **Fix Match / Identify** feature.
2. Manually search for the correct league.

### Episode number changed after a rescan

Expected, not a bug. The upstream schedule moved (an event was added,
cancelled, or postponed), so the season was renumbered and the agent — or the
Sportarr app's automatic renamer — reflected it.
