# Sportarr

Sportarr is a sports PVR for Usenet and BitTorrent users. It monitors the sports leagues you follow, searches your indexers for event releases, sends them to your download client, and renames and organizes recordings into a clean sports media library for Plex, Jellyfin, and Emby.

- Track events across major sports: fighting sports, football, soccer, basketball, hockey, baseball, motorsport, and more
- Automatic search, grab, import, rename, and upgrade of event releases
- Calendar of upcoming events for your monitored leagues
- Works with your existing Usenet and torrent setup (indexers and download clients)
- Media server integration and metadata for sports libraries
- Optional hardware-accelerated post-processing via ffmpeg (Intel QSV, VAAPI, NVIDIA NVENC)

## Quick start

```yaml
services:
  sportarr:
    image: sportarr/sportarr:latest
    container_name: sportarr
    ports:
      - 1867:1867
    environment:
      - PUID=99
      - PGID=100
      - TZ=Etc/UTC
    volumes:
      - /path/to/appdata/sportarr:/config
      - /path/to/sports:/sports
      - /path/to/downloads:/downloads
    restart: unless-stopped
```

Or with `docker run`:

```bash
docker run -d \
  --name sportarr \
  -p 1867:1867 \
  -e PUID=99 -e PGID=100 -e TZ=Etc/UTC \
  -v /path/to/appdata/sportarr:/config \
  -v /path/to/sports:/sports \
  -v /path/to/downloads:/downloads \
  --restart unless-stopped \
  sportarr/sportarr:latest
```

The web UI is available at `http://your-host:1867`.

## Ports

| Port | Description |
| ---- | ----------- |
| `1867` | Web UI and API |

## Volumes

| Path | Description |
| ---- | ----------- |
| `/config` | Application data: database, settings, logs |
| your media path | Mount your sports library wherever you like (e.g. `/sports`) |
| your downloads path | Mount your download client's completed folder (e.g. `/downloads`) |

Use the same paths your download client sees so imports can hardlink instead of copy.

## Environment variables

| Variable | Default | Description |
| -------- | ------- | ----------- |
| `PUID` | `99` | User ID that owns files created by Sportarr |
| `PGID` | `100` | Group ID that owns files created by Sportarr |
| `TZ` | `Etc/UTC` | Timezone for logs and scheduling |
| `UMASK` | `022` | File creation mask |

The container can also run fully non-root: start it with `--user 1000:1000` (or your IDs) and ensure `/config` is owned by that user. `PUID`/`PGID` are ignored in that mode.

## Hardware acceleration (optional)

For hardware-accelerated post-processing:

- Intel QSV / VAAPI: add `--device /dev/dri:/dev/dri`
- NVIDIA NVENC: add `--gpus all` (requires the NVIDIA container toolkit)

## Tags

| Tag | Description |
| --- | ----------- |
| `latest` | Latest stable release |
| `4.0.1018.1098` | Specific release (pinnable) |
| `4.0`, `4` | Latest release in that minor/major line |
| `dev` | Development branch builds, updated frequently |

Both `linux/amd64` and `linux/arm64` are published for all tags.

## Links

- [GitHub repository](https://github.com/Sportarr/Sportarr)
- [Releases and changelogs](https://github.com/Sportarr/Sportarr/releases)
- [Issue tracker](https://github.com/Sportarr/Sportarr/issues)

Licensed under GPL-3.0.
