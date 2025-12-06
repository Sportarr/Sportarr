# Sportarr Plex Agent

A Plex metadata agent that fetches sports metadata from sportarr.net.

## Features

- **Rich metadata**: Posters, banners, descriptions, and air dates from sportarr.net
- **Unified metadata**: Same data you see in Sportarr appears in Plex
- **Multi-part support**: Handles fight cards (Early Prelims, Prelims, Main Card)
- **Year-based seasons**: Uses 4-digit year format (2024, 2025) as season numbers

## Installation

### 1. Copy the Agent

Copy the `Sportarr.bundle` folder to your Plex plugins directory:

**Windows:**
```
%LOCALAPPDATA%\Plex Media Server\Plug-ins\
```

**macOS:**
```
~/Library/Application Support/Plex Media Server/Plug-ins/
```

**Linux:**
```
/var/lib/plexmediaserver/Library/Application Support/Plex Media Server/Plug-ins/
```

**Docker:**
```
/config/Library/Application Support/Plex Media Server/Plug-ins/
```

### 2. Restart Plex Media Server

After copying the bundle, restart Plex for the agent to be loaded.

### 3. Create a Sports Library

1. In Plex, click **+** to add a library
2. Select **TV Shows** as the type
3. Add your sports media folder (e.g., `/media/Sports`)
4. Under **Advanced**, select **Sportarr** as the agent
5. Click **Add Library**

## File Naming Convention

The agent expects Sportarr's file naming format:

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

### Multi-Part Events
```
{Series} - S{Season}E{Episode} - pt{Part} - {Title} - {Quality}.ext
```

Examples:
```
My League - S2024E01 - pt1 - Event Title Prelims - 1080p.mkv
My League - S2024E01 - pt2 - Event Title Main Card - 1080p.mkv
```

## How It Works

1. **Scan**: Plex scans your library and finds files matching the naming convention
2. **Parse**: The agent extracts series name, season, and episode from filenames
3. **Query**: Agent calls sportarr.net to find metadata
4. **Fetch**: Full metadata (posters, descriptions, air dates) is retrieved
5. **Display**: Rich metadata appears in your Plex library

## Troubleshooting

### Agent Not Appearing
- Ensure the bundle is in the correct Plug-ins directory
- Check file permissions (Plex user must have read access)
- Restart Plex Media Server

### No Metadata Found
- Ensure your files follow the naming convention
- Check Plex logs: `Plex Media Server/Logs/PMS Plugin Logs/com.sportarr.agents.sportarr.log`

### Wrong Metadata
- Refresh metadata: Right-click item → "Refresh Metadata"
- Use "Fix Match" to manually select the correct league

## Support

For issues, please open a GitHub issue at:
https://github.com/Sportarr/Sportarr/issues
