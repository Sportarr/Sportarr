# Download Clients

**Usenet:** SABnzbd, NZBGet, NZBdav

**Torrents:** qBittorrent, Transmission, Deluge, rTorrent, Vuze, Aria2

**Synology Download Station:** add it once for torrents and, separately, once more for usenet if you want both - Download Station handles either from the same NAS, but Sportarr tracks them as two connections since they're configured independently.

**Blackhole:** Torrent Blackhole and Usenet Blackhole. Sportarr drops the grabbed `.torrent`/`.nzb` into a folder for any external downloader and imports the finished download from a watch folder, so you can keep your downloader fully independent of Sportarr.

**Debrid/Proxy:** Decypharr (torrents and usenet)

Sportarr does not unpack archives for torrents. Where an indexer delivers packed releases, run [Unpackerr](../integrations/unpackerr.md) against the same download folder and it extracts them before Sportarr imports.

## Post-import behavior

Each download client has a **Post-Import Mode** controlling how files reach your library:

| Mode | Behavior |
|---|---|
| Auto | Seeding-aware default. Hardlinks while the torrent is still in the client, moves once it's gone |
| Copy | Always copy, source untouched |
| Hardlink | Always hardlink (falls back to copy across filesystems), source untouched no matter what |

If you manage seeding manually and never want Sportarr to move files out of your download folder, set the client's Post-Import Mode to **Hardlink**.

With **Remove Completed Downloads** enabled on a client, a move import finishes by removing the job from the client and deleting the job's leftover folder, including nfo, sample, and archive leftovers, so nothing of the release stays behind in the download directory. With the setting off, Sportarr leaves the client's jobs and folders completely alone.

## Per-indexer client assignment

Under an indexer's advanced settings you can pin a specific download client, so grabs from that indexer always go to that client regardless of priority order. Useful when one tracker should hit a dedicated seedbox client.

## Background scanning and drive activity

Sportarr finds file changes two ways. A filesystem watcher reports changes the moment they happen, and a full disk scan walks every root folder as the safety net behind it. **Disk Scan Interval** under **Settings > Download Clients** controls that walk. The default is 720 minutes, twice a day.

The walk reads every directory of every root folder and checks every tracked file, which wakes every drive holding library content. At the old hourly default, drives never sat idle long enough to reach their spin-down timers. At twice a day they rest between passes.

Nothing you download waits for the scan:

- Downloads import through the download client's own queue, polled every 30 seconds
- Blackhole grabs are tracked per queue item on that same poll
- Files the watcher sees become pending imports immediately

**When to lower it:** root folders on network shares (NFS/SMB). Change events made by other machines never reach the watcher there, so the scan is what finds files you drop in by hand. On local storage the watcher covers that instantly and there is no reason to scan more often.

A manual scan from **System > Tasks** picks up changes immediately regardless of the interval. Between scans an idle Sportarr writes nothing to disk, and recurring health checks only read, so a resting drive stays resting.

## Remote path mappings

When your download client runs on a different machine or container and reports paths Sportarr can't see (e.g. a seedbox reporting `/home/user/downloads` while Sportarr sees `/data/seedbox`), add a remote path mapping under **Settings > Download Clients** so imports resolve the right local path. Mappings match whole path segments, so a mapping for `/data` never claims `/database`.
