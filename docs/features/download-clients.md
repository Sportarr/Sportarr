# Download Clients

**Usenet:** SABnzbd, NZBGet, NZBdav

**Torrents:** qBittorrent, Transmission, Deluge, rTorrent, Vuze, Aria2

**Synology Download Station:** add it once for torrents and, separately, once more for usenet if you want both - Download Station handles either from the same NAS, but Sportarr tracks them as two connections since they're configured independently.

**Blackhole:** Torrent Blackhole and Usenet Blackhole. Sportarr drops the grabbed `.torrent`/`.nzb` into a folder for any external downloader and imports the finished download from a watch folder, so you can keep your downloader fully independent of Sportarr.

**Debrid/Proxy:** Decypharr (torrents and usenet)

## Post-import behavior

Each download client has a **Post-Import Mode** controlling how files reach your library:

| Mode | Behavior |
|---|---|
| Auto | Seeding-aware default. Hardlinks while the torrent is still in the client, moves once it's gone |
| Copy | Always copy, source untouched |
| Hardlink | Always hardlink (falls back to copy across filesystems), source untouched no matter what |

If you manage seeding manually and never want Sportarr to move files out of your download folder, set the client's Post-Import Mode to **Hardlink**.

## Per-indexer client assignment

Under an indexer's advanced settings you can pin a specific download client, so grabs from that indexer always go to that client regardless of priority order. Useful when one tracker should hit a dedicated seedbox client.

## Remote path mappings

When your download client runs on a different machine or container and reports paths Sportarr can't see (e.g. a seedbox reporting `/home/user/downloads` while Sportarr sees `/data/seedbox`), add a remote path mapping under **Settings > Download Clients** so imports resolve the right local path. Mappings match whole path segments, so a mapping for `/data` never claims `/database`.
