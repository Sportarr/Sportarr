# Sportarr Jellyfin Plugin

A Jellyfin metadata plugin that fetches sports metadata from sportarr.net.

## Features

- **Rich metadata**: Posters, banners, descriptions, and air dates from sportarr.net
- **Unified metadata**: Same data you see in Sportarr appears in Jellyfin
- **Multi-part support**: Handles fight cards (Early Prelims, Prelims, Main Card)
- **Year-based seasons**: Uses 4-digit year format (2024, 2025) as season numbers

## Installation

### Option 1: Build from Source

1. Clone this repository
2. Build the plugin:
   ```bash
   cd agents/jellyfin/Sportarr
   dotnet build -c Release
   ```
3. Copy the built DLL to your Jellyfin plugins directory:
   ```
   /config/plugins/Sportarr/
   ```
4. Restart Jellyfin

### Option 2: Pre-built Release

1. Download the latest release from GitHub
2. Extract the ZIP to your Jellyfin plugins directory:
   - Docker: `/config/plugins/Sportarr/`
   - Windows: `%APPDATA%\Jellyfin\Server\plugins\Sportarr\`
   - Linux: `~/.local/share/jellyfin/plugins/Sportarr/`
3. Restart Jellyfin

## Library Setup

1. In Jellyfin, go to **Dashboard** → **Libraries** → **Add Media Library**
2. Select **Shows** as the content type
3. Add your sports media folder (e.g., `/media/Sports`)
4. Under **Metadata Downloaders**, enable **Sportarr**
5. Move **Sportarr** to the top of the list
6. Click **OK**

## File Naming Convention

The plugin expects Sportarr's file naming format:

### Folder Structure
```
{Series}/Season {Season}/
```

Example:
```
My League/Season 2024/
My Sport/Season 2024/
```

### File Format
```
{Series} - S{Season}E{Episode} - {Title} - {Quality}.ext
```

Examples:
```
My League - S2024E15 - Event Title - 720p.mkv
My Sport - S2024E08 - Event Name - 1080p WEB-DL.mkv
```

### Multi-Part Events (Fighting Sports Only)

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

## How It Works

1. **Scan**: Jellyfin scans your library and identifies shows/episodes
2. **Search**: Plugin searches sportarr.net for matching leagues
3. **Match**: Best match is selected based on name similarity
4. **Fetch**: Full metadata (descriptions, dates) is retrieved
5. **Images**: Posters, banners, thumbnails are fetched
6. **Display**: Rich metadata appears in your Jellyfin library

## Troubleshooting

### Plugin Not Loading
- Check Jellyfin logs for errors: `Dashboard → Logs`
- Verify the DLL is in the correct plugins folder
- Ensure .NET 8 runtime is installed

### No Metadata Found
- Ensure files follow the naming convention
- Check Jellyfin logs for `[Sportarr]` entries

### Wrong Metadata Matched
- Use "Identify" feature to manually search and select correct league
- Check that series name in folder matches the league name

## Support

For issues, please open a GitHub issue at:
https://github.com/Sportarr/Sportarr/issues

Include:
- Jellyfin version
- Plugin version
- Relevant log entries
- Example file names that aren't matching
