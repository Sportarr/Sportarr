# Sportarr Media Server Agents

This directory contains metadata agents for Plex and Jellyfin that fetch sports metadata from sportarr.net.

## Overview

These agents replace traditional metadata sources (like TVDB or TMDB) for sports content. They fetch posters, banners, descriptions, and episode information for your sports library.

## Benefits

- **Unified metadata**: Same data across Sportarr, Plex, and Jellyfin
- **Year-based seasons**: Uses 4-digit years (2024, 2025) as season numbers
- **Multi-part support**: Handles fight cards, race sessions, etc.
- **Consistent naming**: Works with Sportarr's file naming format

## Available Agents

### Plex Agent
Location: `plex/Sportarr.bundle/`

A Python-based Plex metadata agent. See [plex/README.md](plex/README.md) for installation instructions.

### Jellyfin Plugin
Location: `jellyfin/Sportarr/`

A C# Jellyfin metadata plugin. See [jellyfin/README.md](jellyfin/README.md) for installation instructions.

## File Naming Convention

All agents expect Sportarr's standardized naming format:

### Folder Structure
```
{Series}/Season {Season}/
```

Examples:
```
UFC/Season 2024/
Premier League/Season 2024/
Formula 1/Season 2024/
```

### File Naming
```
{Series} - S{Season}E{Episode} - {Title} - {Quality}.ext
```

Examples:
```
UFC - S2024E01 - UFC 310 - 1080p HDTV.mkv
Premier League - S2024E15 - Arsenal vs Chelsea - 720p.mkv
Formula 1 - S2024E08 - Monaco Grand Prix - 1080p WEB-DL.mkv
```

### Multi-Part Events

For events with multiple parts (fight cards, race sessions):

```
{Series} - S{Season}E{Episode} - pt{Part} - {Title} - {Quality}.ext
```

Examples:
```
# UFC Fight Card
UFC - S2024E01 - pt1 - UFC 310 Early Prelims - 1080p.mkv
UFC - S2024E01 - pt2 - UFC 310 Prelims - 1080p.mkv
UFC - S2024E01 - pt3 - UFC 310 Main Card - 1080p.mkv

# F1 Race Weekend
Formula 1 - S2024E08 - pt1 - Monaco Grand Prix Practice - 1080p.mkv
Formula 1 - S2024E08 - pt2 - Monaco Grand Prix Qualifying - 1080p.mkv
Formula 1 - S2024E08 - pt3 - Monaco Grand Prix Race - 1080p.mkv
```

## Troubleshooting

### No Metadata Found

1. Check file naming matches the expected format
2. Try using "Fix Match" or "Identify" to manually select the league

### Wrong Match

1. Use the media server's "Fix Match" or "Identify" feature
2. Manually search for the correct league
