# Media Servers

Sportarr provides metadata agents for Plex, Jellyfin, and Emby that fetch posters, banners, descriptions, and episode organization from sportarr.net.

!!! tip "Looking for Kodi?"
    Kodi works differently from the three servers on this page - it reads local files directly instead of needing a plugin. See the dedicated [Kodi](kodi.md) page.

![Media Server Agents](../images/media-server-agents.png)

## Plex

Sportarr supports two methods for Plex.

### Custom metadata provider (recommended)

For **Plex 1.43.0+**, use the Custom Metadata Provider system. No plugin installation required.

1. Open **Plex Web** and go to **Settings > Metadata Agents**
2. Click **+ Add Provider**
3. Enter the URL: `https://sportarr.net/plex`
4. Click **+ Add Agent** and give it a name (e.g. "Sportarr")
5. **Restart Plex Media Server**
6. Create a **TV Shows** library, select your sports folder, and choose the **Sportarr** agent

### Legacy bundle agent

For older Plex versions, download the legacy bundle from the Sportarr UI (**Settings > General > Media Server Agents**) and copy it to your Plex Plug-ins directory.

!!! warning
    Plex has announced legacy agents will be deprecated in 2026. Prefer the custom metadata provider above.

See [agents/plex/README.md](https://github.com/Sportarr/Sportarr/blob/main/agents/plex/README.md) for detailed instructions and troubleshooting.

## Jellyfin

### Plugin repository (recommended)

Installing through a plugin repository means Jellyfin checks for plugin updates automatically.

1. In Jellyfin, go to **Dashboard > Plugins > Repositories** and click **Add**
2. Enter repository name `Sportarr` and repository URL:

    ```
    https://raw.githubusercontent.com/Sportarr/Sportarr/main/agents/jellyfin/manifest.json
    ```

3. Save, then open the **Catalog** tab, find **Sportarr** under Metadata, and click **Install**
4. Restart Jellyfin

### Manual install

Download `sportarr-jellyfin-plugin_*.zip` from the [latest release](https://github.com/Sportarr/Sportarr/releases/latest), extract it into your Jellyfin plugins directory, and restart Jellyfin:

- Docker: `/config/plugins/Sportarr/` (some images use `/config/data/plugins/`)
- Windows: `%APPDATA%\Jellyfin\Server\plugins\Sportarr\`
- Linux/macOS: `~/.local/share/jellyfin/plugins/Sportarr/`

### Configure and add a library

1. Go to **Dashboard > Plugins > Sportarr**. The API URL defaults to `https://sportarr.net`; point it at your own Sportarr install (e.g. `http://localhost:1867`) to serve metadata locally, then use **Test Connection**
2. Create a library: select **Shows**, add your sports folder
3. Under **Metadata Downloaders** and **Image Fetchers**, enable **Sportarr** and drag it to the top of both lists

Requires Jellyfin 10.9 or later. See [agents/jellyfin/README.md](https://github.com/Sportarr/Sportarr/blob/main/agents/jellyfin/README.md) for the full walkthrough and troubleshooting.

!!! warning "Installed the plugin before August 2026?"
    Early plugin builds shipped with a placeholder ID that could collide
    with unrelated plugins in the Jellyfin catalog, making Sportarr show
    up merged into another plugin's entry instead of its own. The ID is
    now permanent and unique, but Jellyfin treats it as a different
    plugin: uninstall the old Sportarr plugin, restart Jellyfin, and
    install it again from the catalog. Your library and metadata are
    untouched by the swap.

## Emby

Emby has its own dedicated plugin.

1. Download `sportarr-emby-plugin_*.zip` from the [latest release](https://github.com/Sportarr/Sportarr/releases/latest) and copy the extracted `Emby.Plugins.Sportarr.dll` to your Emby plugins directory:
    - Docker: `/config/plugins/`
    - Windows: `C:\Users\<username>\AppData\Roaming\Emby-Server\plugins\`
    - Linux: `/var/lib/emby/plugins/`
2. Restart Emby Server
3. Go to **Dashboard > Plugins > Sportarr** and set the API URL (defaults to `https://sportarr.net`; a local Sportarr install works here too)
4. Create a **TV Shows** library for your sports content, enable **Sportarr** under Metadata Downloaders, and move it to the top of the priority list

Requires Emby Server 4.9 or later. See [agents/emby/README.md](https://github.com/Sportarr/Sportarr/blob/main/agents/emby/README.md) for details.
