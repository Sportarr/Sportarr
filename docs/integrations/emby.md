# Emby

Sportarr provides a metadata plugin for Emby that fetches posters, banners, descriptions, and episode organization from sportarr.net. Requires Emby Server 4.9 or later.

## Setup

1. Download `sportarr-emby-plugin_*.zip` from the [latest release](https://github.com/Sportarr/Sportarr/releases/latest) and copy the extracted `Emby.Plugins.Sportarr.dll` to your Emby plugins directory:
    - Docker: `/config/plugins/`
    - Windows: `C:\Users\<username>\AppData\Roaming\Emby-Server\plugins\`
    - Linux: `/var/lib/emby/plugins/`
2. Restart Emby Server
3. Go to **Dashboard > Plugins > Sportarr** and set the API URL (defaults to `https://sportarr.net`; a local Sportarr install works here too)
4. Create a **TV Shows** library for your sports content, enable **Sportarr** under Metadata Downloaders, and move it to the top of the priority list

See [agents/emby/README.md](https://github.com/Sportarr/Sportarr/blob/main/agents/emby/README.md) for details.
