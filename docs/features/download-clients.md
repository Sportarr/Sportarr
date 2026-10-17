# Download Clients

**Usenet:** SABnzbd, NZBGet, NZBdav

**Torrents:** qBittorrent, Transmission, Deluge, rTorrent, Vuze, Aria2

**Synology Download Station:** add it once for torrents and, separately, once more for usenet if you want both - Download Station handles either from the same NAS, but Sportarr tracks them as two connections since they're configured independently.

**Blackhole:** Torrent Blackhole and Usenet Blackhole. Sportarr drops the grabbed `.torrent`/`.nzb` into a folder for any external downloader and imports the finished download from a watch folder. For magnet-only torrent releases, turn on **Save Magnet Files** and have the external downloader watch for `.magnet` files.

**Debrid/Proxy:** Decypharr (torrents and usenet)

Sportarr does not unpack archives for torrents. Where an indexer delivers packed releases, run [Unpackerr](../integrations/unpackerr.md) against the same download folder and it extracts them before Sportarr imports.

## SAB-compatible clients

Give Sportarr its own category when SABnzbd, NZBdav, or Decypharr shares a queue with another application. Sportarr sends both common category parameter names so compatible clients keep the job in that category.

Sportarr tracks each grab by the job ID returned by the client. That exact ID remains authoritative if a compatible client files the job under the wrong category. If the client replaces the ID, Sportarr can recover a single exact release-title match. It refuses partial or ambiguous title matches.

## Transmission

If you set a Download Directory, it must exist and be writable by Transmission. Sportarr sends it with each new torrent and uses it when monitoring the download. On Transmission 3 or newer, the Category is also applied as a torrent label. Sportarr leaves the labels on an existing duplicate torrent alone.

External downloads are found by their download directory. Give Sportarr its own directory if you want downloads added outside Sportarr to appear in Activity.

## Post-import behavior

Each download client has a **Post-Import Mode** controlling how files reach your library:

| Mode | Behavior |
|---|---|
| Auto | Seeding-aware default. Hardlinks while the torrent is still in the client, moves once it's gone |
| Copy | Always copy, source untouched |
| Hardlink | Always hardlink (falls back to copy across filesystems), source untouched no matter what |

If you manage seeding manually and never want Sportarr to move files out of your download folder, set the client's Post-Import Mode to **Hardlink**. Auto keeps a torrent's files in place when the client's state is uncertain.

**Remove Completed Downloads** starts enabled for new clients. For Usenet, a move import can remove the completed job and its leftover folder, including nfo, sample, and archive leftovers. For torrents, Sportarr waits until the client reports that the job has stopped and its indexer's seed ratio or seed time goal has been met. Sportarr leaves the torrent in the client when no seed goal is set or it cannot confirm that removal is safe. Turn the setting off to leave completed jobs in the client.

Sportarr checks imported torrents again after import when the client reports when each job was added. rTorrent and Aria2 do not report that time. For those clients, Sportarr can remove a qualifying stopped torrent at import time, but does not remove it later because the same job ID could belong to a new download.

### A file for an event that already has one

Sportarr keeps one file per event, or per part of an event. Automatic searches, RSS grabs, completed downloads, and library scans all use the assigned quality profile. Qualities nearer the top of the profile are preferred. A lower-ranked quality never replaces a higher-ranked file, while a higher-ranked quality can replace a lower-ranked file even when its custom format score is lower.

Qualities placed in the same profile group have equal rank. Under **Settings > Media Management**, **Propers and Repacks** controls revisions at that rank. **Prefer and Upgrade** treats a newer PROPER or REPACK as an upgrade ahead of custom format score. **Do Not Upgrade Automatically** still prefers a newer revision when choosing releases, but that revision alone does not upgrade a held file. At import, a newer revision needs a higher custom format score. An older revision remains blocked. **Do Not Prefer** ignores revisions so custom format score decides. Choose it when a preferred release group's score should beat any PROPER or REPACK in the same quality group.

An equal copy beside the held file stays where it is and is listed in Activity for review. A scanned copy from another folder also stays there in Copy or Hardlink mode. In Move mode, an equal copy from another folder can replace the held file. An equal download that has finished in a client can replace it too.

A completed download that fails the rule stays in the queue with the reason and an **Import Anyway** button. If several videos could match one event, choose the video in the pending import before importing it. A file that appears in a league folder and fails the rule is left where it is and listed in Activity with the reason. **Library Import** also lists it. A recording held by an indexer file starts unselected and needs **Import anyway** before selection. The Remove button on an Activity row deletes the file too, to the recycle bin when one is set, unless you untick that in the remove dialog. Ignore keeps the file and only stops the scans listing it. When a copy that already sits beside the file it replaces takes over, the replaced file stays on disk untracked. A copy from anywhere else replaces it through the recycle bin.

## Activity queue

On phones and tablets, Queue shows compact rows. Open a row to see its release details and actions. The desktop sidebar shows the current download or import state and links to the queue item. If a job has made no recent progress, the sidebar says it needs attention instead of presenting it as active.

Selecting rows shows bulk import and removal actions. Bulk import works only for eligible queue rows. Pending imports need per-item review, and **Import Anyway** needs individual confirmation. **Remove Selected** opens a confirmation. For downloads held by a client, the default **Remove from Download Client** method asks the client to delete the job and its files. Check that choice before confirming. Sportarr blocklists a removed pending import from a client even if that client cannot delete the job. Check the client if the download or files remain.

When **Enable Auto Import** is off, completed downloads stay in Queue. Use **Import** on one row or select completed rows and choose **Import Selected**. Downloads that need a video choice or **Import Anyway** decision still need individual review.

## Retry a completed import

Activity shows **Retry Import** when a completed download has failed to import or a pack member is held for correction. Resolve the displayed reason, then retry the import. Retrying uses the completed download and does not submit another download job. Bulk import supports these retries. **Import Anyway** requires confirmation on each download and is not available in bulk.

Pack members are checked against their own events and parts. A completed member does not allow Sportarr to remove a shared download while another member still needs it.

## Per-indexer client assignment

Under an indexer's advanced settings you can pin a specific download client, so grabs from that indexer always go to that client regardless of priority order. Useful when one tracker should hit a dedicated seedbox client.

## Completion notifications

A client or integration can call `POST /api/download/completed` with its job ID
to wake the download monitor after a job finishes. This avoids waiting for the
next regular poll. See the [API contract and example](../APPLICATION_API.md#download-completion-notifications)
for authentication, optional client IDs, and responses.

The monitor still checks the client's status and follows your import settings.
Repeated callbacks combine into one pending check, with at least five seconds
between checks. Normal polling remains active. Unknown and already imported
jobs return successfully without requesting another check.

## Background scanning and drive activity

Sportarr finds file changes two ways. A filesystem watcher reports changes the moment they happen, and a full disk scan walks every root folder as the safety net behind it. **Disk Scan Interval** under **Settings > Download Clients** controls that walk. The default is 720 minutes, twice a day.

The walk reads every directory of every root folder and checks every tracked file, which wakes every drive holding library content. At the old hourly default, drives never sat idle long enough to reach their spin-down timers. At twice a day they rest between passes.

Nothing you download waits for the scan:

- Downloads import through the download client's own queue. The default poll interval is 30 seconds, configurable with a five-second minimum. Completion notifications can wake the monitor sooner
- Blackhole grabs are tracked per queue item on that same poll
- Files the watcher sees become pending imports immediately

**When to lower it:** root folders on network shares (NFS/SMB). Change events made by other machines never reach the watcher there, so the scan is what finds files you drop in by hand. On local storage the watcher covers that instantly and there is no reason to scan more often.

A manual scan from **System > Tasks** picks up changes immediately regardless of the interval. Between scans an idle Sportarr writes nothing to disk, and recurring health checks only read, so a resting drive stays resting.

## Remote path mappings

When your download client runs on a different machine or container and reports paths Sportarr can't see (e.g. a seedbox reporting `/home/user/downloads` while Sportarr sees `/data/seedbox`), add a remote path mapping under **Settings > Download Clients** so imports resolve the right local path. Mappings match whole path segments, so a mapping for `/data` never claims `/database`.
