# Kodi

<p align="center" class="integration-logo">
  <img src="../../assets/integrations/kodi.svg" alt="" width="72" height="72" />
</p>

Kodi works differently from Plex, Jellyfin, and Emby: it doesn't need a plugin at all. Kodi reads local `.nfo` files and poster/fanart images straight off disk, and Sportarr can write those for you. A separate, optional addon also exists if you'd rather Kodi look events up dynamically instead.

## Option 1: Local metadata (recommended, no addon)

1. In Sportarr, go to **Settings > Local Metadata**
2. Turn on **Enabled**
3. Choose what to write - **Episode NFO** and **Show NFO** are on by default; **Episode Thumbnails** and **League Poster & Banner** are optional
4. Import or re-sync a league - the `.nfo` files and images appear next to your video files

Each event's `.nfo` carries its Sportarr id as a `uniqueid`, the way a tvdb id would, so the library keeps the id whatever the season and episode numbers do.

In Kodi, create a **TV Shows** library pointed at your sports folder and scan it. No scraper configuration needed - Kodi finds the local NFO automatically.

!!! tip
    If you already have a library scraping online, switch its information provider to **Local information only** so Kodi doesn't try to overwrite what Sportarr wrote.

## Option 2: Connect notification (library refresh)

Local metadata files don't tell Kodi *when* something new has been imported - for that, add a Kodi connection under **Settings > Notifications**, the same idea as the Plex/Jellyfin/Emby refresh connections.

1. In Sportarr, go to **Settings > Notifications > Add Notification > Kodi (XBMC)**
2. Set **Host** to Kodi's IP or hostname (not a full URL - just the host)
3. Leave **Port** at `8080` and **URL Base** at `/jsonrpc` unless you've changed Kodi's webserver settings
4. Turn on the triggers you want (import, upgrade, rename, delete are on by default)
5. Click **Test** to confirm Kodi responds

!!! warning "Allow remote control via HTTP"
    Kodi rejects this connection until you enable it: **Kodi > Settings > Services > Control > Allow remote control via HTTP applications**. This is the single most common reason the test fails.

**Clean Library** always sweeps your whole library when enabled - Kodi's own API has no way to clean just one folder, so leave it off unless you're troubleshooting a stale entry. **Always Update** doesn't mean "force a full rescan" - it means "don't check whether something is currently playing before scanning," which is off by default so an import doesn't interrupt what you're watching.

## Option 3: Scraper addon (optional, dynamic lookup)

If you'd rather Kodi query Sportarr live instead of relying on the files Sportarr wrote, install the Sportarr scraper addon. It calls the same metadata API the Plex/Jellyfin/Emby agents use, so season and episode numbers still match exactly what's on disk - a generic scraper can't promise that.

1. **Kodi > Settings > File manager > Add source**, enter:

    ```
    https://raw.githubusercontent.com/Sportarr/Sportarr/main/agents/kodi/repo/
    ```

2. **Add-ons > Install from zip file**, pick that source, then `repository.sportarr-<version>.zip`
3. **Add-ons > Install from repository > Sportarr Repository > Add-on metadata providers > Sportarr**
4. Open the addon's settings: set **Server URL** (`https://sportarr.net` by default, or your own instance) and **Season year** (Sportarr's seasons are the calendar year - keep this current)
5. On your TV Shows library, **Settings > Information provider > Sportarr**, then re-scan

!!! warning "Known limitation"
    The addon's episode list only covers the season set in **Season year** - it doesn't walk every historical season. If you need a full back-catalog scraped, use Option 1 instead, which has no such limit.

See [agents/kodi/README.md](https://github.com/Sportarr/Sportarr/blob/main/agents/kodi/README.md) for the full addon install walkthrough and troubleshooting.
