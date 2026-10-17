# Blackhole

The blackhole clients keep your downloader fully independent of Sportarr. Grabs are written as files into a folder your external downloader watches, and finished downloads are imported back from a watch folder. **Torrent Blackhole** drops `.torrent` files and can save `.magnet` files for magnet-only releases. **Usenet Blackhole** drops `.nzb` files.

| | |
|---|---|
| Protocol | Torrent or usenet, one entry each |
| Authentication | None; folder based |

## Setup

1. In Sportarr, go to **Settings > Download Clients**, click **Add**, and choose **Torrent Blackhole** or **Usenet Blackhole**
2. Set the folder Sportarr should drop grabbed files into, and the watch folder it should import finished downloads from
3. For magnet-only torrent releases, turn on **Save Magnet Files** and set your external downloader to watch `.magnet` files. This option starts off.
4. Point your external downloader at the same folders
5. **Test**, then **Save**

Post-import modes and remote path mappings are shared across all clients and documented under [Download Clients](../features/download-clients.md).
