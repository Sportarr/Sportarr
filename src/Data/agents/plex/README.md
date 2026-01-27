# Sportarr Plex Integration

Sportarr provides two methods to integrate with Plex for sports metadata:

1. **Custom Metadata Provider** (Recommended) - For Plex 1.43.0+ (2024 and newer)
2. **Legacy Bundle Agent** - For older Plex versions

## Features

- **Rich metadata**: Posters, banners, descriptions, and air dates from sportarr.net
- **Unified metadata**: Same data you see in Sportarr appears in Plex
- **Multi-part support**: Handles fight cards (Early Prelims, Prelims, Main Card) and motorsport sessions
- **Year-based seasons**: Uses 4-digit year format (2024, 2025) as season numbers

---

## Option 1: Custom Metadata Provider (Recommended)

For Plex Media Server 1.43.0 and newer, use the new Custom Metadata Provider system. This is the recommended approach as it requires no file installation and will continue to be supported long-term.

### Setup Instructions

#### Step 1: Add the Metadata Provider

1. Open **Plex Web** and go to **Settings**
2. Navigate to **Settings → Metadata Agents**
3. Click **+ Add Provider**
4. In the URL field, enter:
   ```
   https://sportarr.net/plex
   ```
5. Click **+ Add Agent**
6. Give it a title (e.g., "Sportarr Sports")
7. Select the **Sportarr** metadata provider you just imported
8. Click **Save**
9. **Restart Plex Media Server**

#### Step 2: Create a Sports Library

1. Go to **Settings → Libraries**
2. Click **+ Add Library**
3. Select **TV Shows** as the library type
4. Name it whatever you like (e.g., "Sports")
5. **Important:** Select the **Sportarr** metadata agent you created in Step 1
6. Add your sports media folder
7. Click **Add Library**

### Benefits of Custom Provider

- No files to install or update
- Automatic updates when sportarr.net improves
- Works with all Plex clients
- No restart required
- Survives Plex updates

---

## Option 2: Legacy Bundle Agent

For older Plex versions that don't support Custom Metadata Providers, use the legacy bundle agent.

> **Note**: Plex has announced that legacy bundle agents will be deprecated in 2026. We recommend migrating to the Custom Metadata Provider when possible.

### Installation

#### 1. Copy the Agent

Copy the `Sportarr-Legacy.bundle` folder to your Plex plugins directory:

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

#### 2. Restart Plex Media Server

After copying the bundle, restart Plex for the agent to be loaded.

#### 3. Create a Sports Library

1. In Plex, click **+** to add a library
2. Select **TV Shows** as the type
3. Add your sports media folder (e.g., `/media/Sports`)
4. Under **Advanced**, select **Sportarr (Legacy)** as the agent
5. Click **Add Library**

---

## File Naming Convention

Both methods expect Sportarr's file naming format:

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

Motorsport events support up to 5 parts (Practice, Qualifying, Sprint, Pre-Race, Race).

---

## How It Works

1. **Scan**: Plex scans your library and finds files matching the naming convention
2. **Parse**: Plex extracts series name, season, and episode from filenames
3. **Query**: Sportarr metadata provider is called to find matches
4. **Fetch**: Full metadata (posters, descriptions, air dates) is retrieved from sportarr.net
5. **Display**: Rich metadata appears in your Plex library

---

## Troubleshooting

### Custom Provider Not Working
- Verify Plex version is 1.43.0 or newer
- Check the provider URL is correct: `https://sportarr.net/plex`
- Ensure Plex server can reach sportarr.net (no firewall blocking)
- Make sure you restarted Plex after adding the provider
- Verify you selected the Sportarr agent when creating the library

### Legacy Agent Not Appearing
- Ensure the bundle is in the correct Plug-ins directory
- Check file permissions (Plex user must have read access)
- Restart Plex Media Server

### No Metadata Found
- Ensure your files follow the naming convention
- Check Plex logs for errors
- Try "Fix Match" to manually search and select the correct league

### Wrong Metadata
- Refresh metadata: Right-click item → "Refresh Metadata"
- Use "Fix Match" to manually select the correct league

---

## Migrating from Legacy to Custom Provider

1. Add the Custom Metadata Provider URL to your library (see Option 1)
2. Drag it above the legacy agent in the agent list
3. Refresh metadata on your library
4. Once verified working, you can remove the legacy bundle

---

## Support

For issues, please open a GitHub issue at:
https://github.com/Sportarr/Sportarr/issues
