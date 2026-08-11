# Jellyfin

Sportarr provides a metadata plugin for Jellyfin that fetches posters, banners, descriptions, and episode organization from sportarr.net. Requires Jellyfin 10.9 or later.

## Plugin repository (recommended)

Installing through a plugin repository means Jellyfin checks for plugin updates automatically.

1. In Jellyfin, go to **Dashboard > Plugins > Repositories** and click **Add**
2. Enter repository name `Sportarr` and repository URL:

    ```
    https://raw.githubusercontent.com/Sportarr/Sportarr/main/agents/jellyfin/manifest.json
    ```

3. Save, then open the **Catalog** tab, find **Sportarr** under Metadata, and click **Install**
4. Restart Jellyfin

## Manual install

Download `sportarr-jellyfin-plugin_*.zip` from the [latest release](https://github.com/Sportarr/Sportarr/releases/latest), extract it into your Jellyfin plugins directory, and restart Jellyfin:

- Docker: `/config/plugins/Sportarr/` (some images use `/config/data/plugins/`)
- Windows: `%APPDATA%\Jellyfin\Server\plugins\Sportarr\`
- Linux/macOS: `~/.local/share/jellyfin/plugins/Sportarr/`

## Configure and add a library

1. Go to **Dashboard > Plugins > Sportarr**. The API URL defaults to `https://sportarr.net`; point it at your own Sportarr install (e.g. `http://localhost:1867`) to serve metadata locally, then use **Test Connection**
2. Create a library: select **Shows**, add your sports folder
3. Under **Metadata Downloaders** and **Image Fetchers**, enable **Sportarr** and drag it to the top of both lists

See [agents/jellyfin/README.md](https://github.com/Sportarr/Sportarr/blob/main/agents/jellyfin/README.md) for the full walkthrough and troubleshooting.

!!! warning "Installed the plugin before August 2026?"
    Early plugin builds shipped with a placeholder ID that could collide
    with unrelated plugins in the Jellyfin catalog, making Sportarr show
    up merged into another plugin's entry instead of its own. The ID is
    now permanent and unique, but Jellyfin treats it as a different
    plugin: uninstall the old Sportarr plugin, restart Jellyfin, and
    install it again from the catalog. Your library and metadata are
    untouched by the swap.
