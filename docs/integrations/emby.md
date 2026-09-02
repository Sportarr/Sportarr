# Emby

<p align="center" class="integration-logo">
  <img src="../../assets/integrations/emby.svg" alt="" width="72" height="72" />
</p>

Sportarr provides a metadata plugin for Emby that fetches posters, banners, descriptions, and episode organization from sportarr.net. Requires Emby Server 4.9 or later.

## Setup

1. Download `sportarr-emby-plugin_*.zip` from the [latest release](https://github.com/Sportarr/Sportarr/releases/latest) and copy the extracted `Emby.Plugins.Sportarr.dll` to your Emby plugins directory:
    - Docker: `/config/plugins/`
    - Windows: `C:\Users\<username>\AppData\Roaming\Emby-Server\plugins\`
    - Linux: `/var/lib/emby/plugins/`
2. Restart Emby Server
3. Go to **Dashboard > Plugins > Sportarr** and set the API URL (defaults to `https://sportarr.net`; a local Sportarr install works here too)
4. Create a **TV Shows** library for your sports content, enable **Sportarr** under Metadata Downloaders, and move it to the top of the priority list

Files the Sportarr app names carry the event's id (`sportarr-ev-2338110`), and the plugin matches by it first, like a tvdb id. The folder name and the season and episode numbers only serve files without one. See [File naming](../features/file-naming.md#the-sportarr-id-token).

See [agents/emby/README.md](https://github.com/Sportarr/Sportarr/blob/main/agents/emby/README.md) for details.
