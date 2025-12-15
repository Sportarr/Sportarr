# Sportarr Media Server Agents

This directory contains metadata agents for Plex and Jellyfin that fetch sports metadata from sportarr.net.

## Overview

These agents replace traditional metadata sources (like TVDB or TMDB) for sports content. They fetch posters, banners, descriptions, and episode information for your sports library.

## Benefits

- **Unified metadata**: Same data across Sportarr, Plex, and Jellyfin
- **Year-based seasons**: Uses 4-digit years (2024, 2025) as season numbers
- **Multi-part support**: Handles fight cards and motorsport sessions with multiple segments
- **Consistent naming**: Works with Sportarr's file naming format

## Available Agents

### Plex

Sportarr supports two methods for Plex integration:

#### Custom Metadata Provider (Recommended)
For **Plex 1.43.0+** (2024 and newer), use the new Custom Metadata Provider system. No installation required!

**Provider URL:**
```
https://sportarr.net/plex
```

**Quick Setup:** Settings → Metadata Agents → + Add Provider → Enter URL → + Add Agent → Restart Plex → Create TV Shows library with Sportarr agent

#### Legacy Bundle Agent
Location: `plex/Sportarr-Legacy.bundle/`

For older Plex versions, use the Python-based legacy agent. Note that Plex has announced legacy agents will be deprecated in 2026.

See [plex/README.md](plex/README.md) for detailed installation instructions.

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
My League/Season 2024/
My Sport/Season 2024/
```

### File Naming
```
{Series} - S{Season}E{Episode} - {Title} - {Quality}.ext
```

Examples:
```
My League - S2024E15 - Event Title - 720p.mkv
My Sport - S2024E08 - Event Name - 1080p WEB-DL.mkv
```

### Multi-Part Events

Fighting sports events can have multiple parts (Early Prelims, Prelims, Main Card):

```
{Series} - S{Season}E{Episode} - pt{Part} - {Title} - {Quality}.ext
```

Examples:
```
My League - S2024E01 - pt1 - Event Title Early Prelims - 1080p.mkv
My League - S2024E01 - pt2 - Event Title Prelims - 1080p.mkv
My League - S2024E01 - pt3 - Event Title Main Card - 1080p.mkv
```

Motorsport events can have up to 5 parts (Practice, Qualifying, Sprint, Pre-Race, Race):
```
Formula 1 - S2024E01 - pt1 - Monaco GP Practice - 1080p.mkv
Formula 1 - S2024E01 - pt2 - Monaco GP Qualifying - 1080p.mkv
Formula 1 - S2024E01 - pt3 - Monaco GP Race - 1080p.mkv
```

## Troubleshooting

### No Metadata Found

1. Check file naming matches the expected format
2. Try using "Fix Match" or "Identify" to manually select the league

### Wrong Match

1. Use the media server's "Fix Match" or "Identify" feature
2. Manually search for the correct league
